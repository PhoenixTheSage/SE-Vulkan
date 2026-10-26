using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp.PixelFormats;
using VRage.FileSystem;
using VRage.Render.Image;
using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering.Content
{
    /// <summary>Render-thread-owned textures shared by world materials, sprites and fonts.</summary>
    public sealed class VulkanTextureCache : IDisposable
    {
        private sealed class Entry
        {
            public ulong Handle;
            public int Width, Height, Format;
            public bool Failed;
            public bool PremultipliedSprite;
        }

        private readonly NativeBackend backend;
        private readonly ConcurrentDictionary<string, Entry> entries = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Entry> generated = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> requested = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<KeyValuePair<string, uint>> pending = new ConcurrentQueue<KeyValuePair<string, uint>>();
        private readonly ConcurrentDictionary<ulong, byte> singleChannelTextures = new ConcurrentDictionary<ulong, byte>();
        private readonly Dictionary<string, ulong[]> cubes = new Dictionary<string, ulong[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ulong> worldCubes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> worldProbeCubePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        private ulong neutralNormal, neutralExtensions;
        // A handle stays valid for the session once handed out: prepared submissions, old cells and native pass
        // families keep texture ids across frames, and native rejects a draw naming a removed texture. A destroyed
        // generated texture (LCD) keeps its handle as a 1x1 transparent placeholder that re-creation reuses; an
        // unloaded file texture keeps its handle as the placeholder and reloads into it.
        private readonly Dictionary<string, ulong> destroyedGenerated = new Dictionary<string, ulong>(StringComparer.Ordinal);
        private readonly Dictionary<string, ulong> unloaded = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        private long generation;
        /// <summary>Advances when a file texture is unloaded; texture-id caches rebuild so they look it up (and reload) again.</summary>
        public long Generation => Interlocked.Read(ref generation);
        private void Placeholder(ulong handle) => backend.UploadTexture(handle, 1, 1, 0, new byte[4], false);
        private int ownerThread;
        private ulong nextHandle = 1;
        private static readonly Lazy<byte[]> guiPremultiplyTable = new Lazy<byte[]>(() =>
        {
            var table = new byte[65536];
            for (int color = 0; color < 256; ++color)
                for (int alpha = 0; alpha < 256; ++alpha)
                    table[color * 256 + alpha] = (byte)(Math.Pow(Math.Pow(color / 255.0, 2.2) * (alpha / 255.0), .45454545454545453) * 255.0);
            return table;
        });
        public bool IsSingleChannel(ulong handle) => singleChannelTextures.ContainsKey(handle);
        // Stock MakeSrgb applies to alpha-mask views too. Formats without an
        // sRGB counterpart (notably BC4) retain their linear native format.
        public ulong GetWorldAlpha(string path)=>GetTexture(path,true);
        public int Count => entries.Count + generated.Count + cubes.Count * 6;
        public event Action<string, Exception> LoadFailed;

        // Resident-only reads for forked producers. Off the owner thread, a plain GetTexture miss queues
        // the load and returns 0, which differs from the owner thread's synchronous load. Inside a
        // ResidentOnly scope a miss throws TextureNotResident instead, before changing any state, so the
        // producer can fall back to the render thread and emit exactly what serial emission would.
        // Resident hits read only the concurrent maps; generated-texture removals happen in message
        // processing on the owner thread, never while a forked producer runs.
        public sealed class TextureNotResident : Exception { public TextureNotResident(string path) : base("Texture is not resident: " + path) { } }
        [ThreadStatic] private static bool residentOnly;
        public readonly struct ResidentScope : IDisposable
        {
            private readonly bool previous, active;
            internal ResidentScope(bool previous) { this.previous = previous; active = true; residentOnly = true; }
            public void Dispose() { if (active) residentOnly = previous; }
        }
        public static ResidentScope ResidentOnly() => new ResidentScope(residentOnly);
        public VulkanTextureCache(NativeBackend backend) { this.backend = backend; ownerThread = Thread.CurrentThread.ManagedThreadId; }

        public void BindOwnerThread() => Volatile.Write(ref ownerThread, Thread.CurrentThread.ManagedThreadId);

        public ulong GetWorldNormal(string path)
        {
            ulong result=GetTexture(path,false);if(result!=0)return result;RequireOwner();
            // MyGeneratedTextureManager.ReleaseMissingNormalGlossTex uses127,
            // not128. The one-byte tangent-space bias survives GBuffer packing.
            if(neutralNormal==0){neutralNormal=nextHandle++;backend.UploadTexture(neutralNormal,1,1,0,new byte[]{127,127,255,0},false);}return neutralNormal;
        }
        public ulong GetWorldExtensions(string path)
        {
            // Stock MyGeometryTextureSystem and MyFileTexture use sRGB views
            // for Extension RGB (AO/emissive); alpha/color-mask stays linear.
            ulong result=GetTexture(path,true);if(result!=0)return result;RequireOwner();
            if(neutralExtensions==0){neutralExtensions=nextHandle++;backend.UploadTexture(neutralExtensions,1,1,0,new byte[]{255,0,0,0},true);}return neutralExtensions;
        }
        public ulong GetWorldProbeCube(string path)
        {
            RequireOwner();if(string.IsNullOrWhiteSpace(path))return 0;
            // The skybox asks every frame; probe the file system once per path.
            if(!worldProbeCubePaths.TryGetValue(path,out string source)){
                string resolved=ResolvePath(path);
                string indirect=Path.Combine(Path.GetDirectoryName(resolved)??"",Path.GetFileNameWithoutExtension(resolved)+"Indirect"+Path.GetExtension(resolved));
                worldProbeCubePaths.Add(path,source=MyFileSystem.FileExists(indirect)?indirect:resolved);}
            return GetWorldCube(source);
        }
        public ulong GetWorldCube(string path)
        {
            RequireOwner();if(string.IsNullOrWhiteSpace(path))return 0;string resolved=ResolvePath(path);
            if(worldCubes.TryGetValue(resolved,out ulong handle))return handle;
            try
            {
                using(var input=MyFileSystem.OpenRead(resolved))
                {
                    if(input==null)throw new FileNotFoundException("World cubemap was not found.",resolved);
                    var faces=DdsImage.ReadCube(input,true);
                    handle=nextHandle++;backend.UploadWorldCubeMips(handle,faces[0].Width,faces[0].Height,faces[0].Format,faces.Select(f=>f.Mips).ToArray(),faces[0].Srgb || faces[0].Format!=7);
                    worldCubes.Add(resolved,handle);return handle;
                }
            }
            catch(Exception error){worldCubes[resolved]=0;LoadFailed?.Invoke(resolved,error);return 0;}
        }

        public void DrainPendingLoads()
        {
            RequireOwner();
            KeyValuePair<string, uint> request;
            while (pending.TryDequeue(out request))
            {
                bool srgb = (request.Value & 1) != 0, gui = (request.Value & 2) != 0;
                GetTexture(request.Key, srgb, gui);
                byte ignored;
                requested.TryRemove((gui ? "g:" : srgb ? "s:" : "l:") + request.Key, out ignored);
            }
        }

        public ulong GetTexture(string path, bool srgb = true, bool gui = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return 0;
            ulong handle;
            Entry dynamic;
            if (generated.TryGetValue(path, out dynamic)) return dynamic.Handle;
            if ((residentOnly || Thread.CurrentThread.ManagedThreadId == Volatile.Read(ref ownerThread)) && destroyedGenerated.TryGetValue(path, out handle)) return handle;
            var keys = Keys(path);
            string resolved = keys.Resolved;
            string key = keys.For(srgb, gui);
            Entry existing;
            if (entries.TryGetValue(key, out existing) && (existing.Handle != 0 || existing.Failed)) return existing.Handle;
            if (residentOnly) throw new TextureNotResident(path);
            if (Thread.CurrentThread.ManagedThreadId != Volatile.Read(ref ownerThread))
            {
                if (requested.TryAdd(key, 0)) pending.Enqueue(new KeyValuePair<string, uint>(resolved, (srgb ? 1u : 0u) | (gui ? 2u : 0u)));
                return 0;
            }
            try
            {
                using (Stream stream = MyFileSystem.OpenRead(resolved))
                {
                    if (stream == null) throw new FileNotFoundException("Texture was not found.", resolved);
                    int width, height, format;
                    byte[] pixels;
                    byte[][] levels;
                    bool rawRgba, premultiplied=false;
                    if (Path.GetExtension(resolved).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        var image = DdsImage.Read(stream, true);
                        width = image.Width; height = image.Height; format = image.Format; pixels = image.Data;
                        levels = image.Mips;
                        rawRgba = image.IsRawRgba;
                        premultiplied=image.IsPremultiplied;
                    }
                    else
                    {
                        var image = MyImage<uint>.Create<Rgba32>(stream);
                        width = image.Size.X; height = image.Size.Y; format = 0;
                        pixels = new byte[checked(image.Data.Length * 4)];
                        Buffer.BlockCopy(image.Data, 0, pixels, 0, pixels.Length);
                        levels = new[] { pixels };
                        rawRgba = true;
                    }
                    // Stock MyFileTexture.PremultiplyAlpha transforms raw GUI
                    // pixels with a gamma-2.2 lookup table before an sRGB view.
                    // Keep a separate cache entry so world textures stay intact.
                    if (gui && rawRgba) foreach (var level in levels) PremultiplyGui(level);
                    bool reload = unloaded.TryGetValue(key, out handle);
                    if (!reload) handle = nextHandle++;
                    // Stock MakeSrgb leaves floating-point RG16 unchanged;
                    // that native format has no sRGB-compatible view.
                    backend.UploadTextureMips(handle, width, height, format, levels, srgb && format!=9);
                    if (reload) unloaded.Remove(key);
                    entries[key] = new Entry { Handle = handle, Width = width, Height = height, Format = format, PremultipliedSprite = rawRgba || premultiplied };
                    if (format == 5) singleChannelTextures.TryAdd(handle, 0); // BC4 is a red-only view.
                    return handle;
                }
            }
            catch (Exception error)
            {
                entries[key] = new Entry { Failed = true, Width = existing?.Width ?? 0, Height = existing?.Height ?? 0 };
                LoadFailed?.Invoke(resolved, error);
                return 0;
            }
        }

        public ulong GetSpriteTexture(string path, out bool premultiplied)
        {
            ulong handle = GetTexture(path, true, true); premultiplied = false;
            if (!string.IsNullOrWhiteSpace(path) && entries.TryGetValue(Keys(path).Gui, out Entry entry)) premultiplied = entry.PremultipliedSprite;
            return handle;
        }

        private static void PremultiplyGui(byte[] pixels)
        {
            byte[] table = guiPremultiplyTable.Value;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                for (int c = 0; c < 3; ++c)
                    pixels[i + c] = table[pixels[i + c] * 256 + alpha];
            }
        }

        public bool GetSize(string path, out int width, out int height)
        {
            width = height = 1;
            if (string.IsNullOrWhiteSpace(path)) return false;
            Entry dynamic;
            if (generated.TryGetValue(path, out dynamic)) { width = dynamic.Width; height = dynamic.Height; return true; }
            var keys = Keys(path);
            string resolved = keys.Resolved;
            Entry entry;
            if ((entries.TryGetValue(keys.Gui, out entry) || entries.TryGetValue(keys.Srgb, out entry) || entries.TryGetValue(keys.Linear, out entry)) && entry.Width > 0 && entry.Height > 0)
            { width = entry.Width; height = entry.Height; return true; }
            try
            {
                using (var stream = MyFileSystem.OpenRead(resolved))
                {
                    if (stream == null) return false;
                    if (Path.GetExtension(resolved).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new BinaryReader(stream))
                        {
                            if (reader.ReadUInt32() != 0x20534444 || reader.ReadUInt32() != 124) return false;
                            reader.ReadUInt32(); height = checked((int)reader.ReadUInt32()); width = checked((int)reader.ReadUInt32());
                        }
                    }
                    else
                    {
                        var image = MyImage.Load(stream, false, true, resolved);
                        if (image == null) return false;
                        width = image.Size.X; height = image.Size.Y;
                    }
                    if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return false;
                    entries.TryAdd("s:" + resolved, new Entry { Width = width, Height = height });
                    return true;
                }
            }
            catch { width = height = 1; return false; }
        }

        public bool IsLoaded(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            Entry entry;
            if (generated.TryGetValue(path, out entry)) return entry.Handle != 0;
            string resolved = ResolvePath(path);
            return entries.TryGetValue("g:" + resolved, out entry) && entry.Handle != 0
                || entries.TryGetValue("s:" + resolved, out entry) && entry.Handle != 0
                || entries.TryGetValue("l:" + resolved, out entry) && entry.Handle != 0;
        }

        public ulong[] GetCubeTextures(string path)
        {
            RequireOwner();
            if (string.IsNullOrWhiteSpace(path)) return null;
            string resolved = ResolvePath(path);
            ulong[] handles;
            if (cubes.TryGetValue(resolved, out handles)) return handles;
            try
            {
                using (var stream = MyFileSystem.OpenRead(resolved))
                {
                    if (stream == null) throw new FileNotFoundException("Skybox cube DDS was not found.", resolved);
                    var faces = DdsImage.ReadCube(stream,true);
                    handles = new ulong[6];
                    for (int i = 0; i < faces.Length; ++i)
                    {
                        handles[i] = nextHandle++;
                        backend.UploadTextureMips(handles[i], faces[i].Width, faces[i].Height, faces[i].Format, faces[i].Mips, faces[i].Srgb || faces[i].Format != 7);
                    }
                    cubes.Add(resolved, handles);
                    return handles;
                }
            }
            catch (Exception error)
            {
                if (handles != null) foreach (ulong handle in handles) if (handle != 0) backend.RemoveResource(0, handle);
                cubes[resolved] = null;
                LoadFailed?.Invoke(resolved, error);
                return null;
            }
        }

        internal ulong ReserveMediaHandle() { RequireOwner(); return nextHandle++; }

        public ulong PutGenerated(string name, int width, int height, byte[] rgba)
        {
            RequireOwner();
            Entry entry;
            if (!generated.TryGetValue(name, out entry))
                entry = new Entry { Handle = destroyedGenerated.TryGetValue(name, out ulong kept) ? kept : nextHandle++ };
            backend.UploadTexture(entry.Handle, width, height, 0, rgba, false);
            destroyedGenerated.Remove(name);
            generated[name] = new Entry { Handle = entry.Handle, Width = width, Height = height };
            return entry.Handle;
        }

        public void RemoveGenerated(string name)
        {
            RequireOwner();
            Entry entry;
            if (!generated.TryRemove(name, out entry)) return;
            Placeholder(entry.Handle); destroyedGenerated[name] = entry.Handle;
        }

        public void Unload(string path)
        {
            RequireOwner();
            if (string.IsNullOrWhiteSpace(path)) return;
            string resolved = ResolvePath(path);
            foreach (string key in new[] { "g:" + resolved, "s:" + resolved, "l:" + resolved })
            {
                Entry entry;
                if (!entries.TryRemove(key, out entry)) continue;
                if (entry.Handle != 0) { Placeholder(entry.Handle); unloaded[key] = entry.Handle; Interlocked.Increment(ref generation); }
                singleChannelTextures.TryRemove(entry.Handle, out _);
            }
        }

        public void Clear()
        {
            RequireOwner();
            foreach (Entry entry in entries.Values) if (entry.Handle != 0) backend.RemoveResource(0, entry.Handle);
            foreach (Entry entry in generated.Values) backend.RemoveResource(0, entry.Handle);
            foreach (ulong[] cube in cubes.Values) if (cube != null) foreach (ulong handle in cube) backend.RemoveResource(0, handle);
            foreach (ulong handle in destroyedGenerated.Values) backend.RemoveResource(0, handle);
            foreach (ulong handle in unloaded.Values) backend.RemoveResource(0, handle);
            entries.Clear(); generated.Clear(); singleChannelTextures.Clear(); destroyedGenerated.Clear(); unloaded.Clear(); Interlocked.Increment(ref generation);
            cubes.Clear();
            foreach(ulong cube in worldCubes.Values)if(cube!=0)backend.RemoveResource(4,cube);worldCubes.Clear();worldProbeCubePaths.Clear();
            if(neutralNormal!=0)backend.RemoveResource(0,neutralNormal);if(neutralExtensions!=0)backend.RemoveResource(0,neutralExtensions);neutralNormal=neutralExtensions=0;
            KeyValuePair<string, uint> ignored;
            while (pending.TryDequeue(out ignored)) { }
            requested.Clear();
        }

        public void Dispose() => Clear();

        private void RequireOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != Volatile.Read(ref ownerThread)) throw new InvalidOperationException("Vulkan texture uploads must run on the owner render thread.");
        }

        public static string ResolvePath(string path) => Keys(path).Resolved;
        // Resolved paths and their cache keys depend only on the input and the content root,
        // so per-frame lookups reuse them instead of building new strings.
        internal sealed class PathKeys
        {
            public readonly string Resolved, Gui, Srgb, Linear;
            public PathKeys(string resolved) { Resolved = resolved; Gui = "g:" + resolved; Srgb = "s:" + resolved; Linear = "l:" + resolved; }
            public string For(bool srgb, bool gui) => gui ? Gui : srgb ? Srgb : Linear;
        }
        static readonly ConcurrentDictionary<string, PathKeys> pathKeys = new ConcurrentDictionary<string, PathKeys>(StringComparer.Ordinal);
        static string pathKeysRoot;
        // ConcurrentDictionary.Count takes every bucket lock; the bound uses its own counter.
        static int pathKeysAdded;
        internal static PathKeys Keys(string path)
        {
            string root = MyFileSystem.ContentPath;
            if (!ReferenceEquals(root, Volatile.Read(ref pathKeysRoot)) || Volatile.Read(ref pathKeysAdded) > 65536) { pathKeys.Clear(); Volatile.Write(ref pathKeysAdded, 0); Volatile.Write(ref pathKeysRoot, root); }
            if (pathKeys.TryGetValue(path, out PathKeys keys)) return keys;
            string normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            keys = new PathKeys(Path.IsPathRooted(normalized) ? normalized : Path.Combine(root, normalized));
            if (pathKeys.TryAdd(path, keys)) Interlocked.Increment(ref pathKeysAdded);
            return keys;
        }
    }
}
