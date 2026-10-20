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
        private ulong neutralNormal, neutralExtensions;
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
            string resolved=ResolvePath(path);
            string indirect=Path.Combine(Path.GetDirectoryName(resolved)??"",Path.GetFileNameWithoutExtension(resolved)+"Indirect"+Path.GetExtension(resolved));
            return GetWorldCube(MyFileSystem.FileExists(indirect)?indirect:resolved);
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
            string resolved = ResolvePath(path);
            string key = (gui ? "g:" : srgb ? "s:" : "l:") + resolved;
            Entry existing;
            if (entries.TryGetValue(key, out existing) && (existing.Handle != 0 || existing.Failed)) return existing.Handle;
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
                    handle = nextHandle++;
                    // Stock MakeSrgb leaves floating-point RG16 unchanged;
                    // that native format has no sRGB-compatible view.
                    backend.UploadTextureMips(handle, width, height, format, levels, srgb && format!=9);
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
            if (!string.IsNullOrWhiteSpace(path) && entries.TryGetValue("g:" + ResolvePath(path), out Entry entry)) premultiplied = entry.PremultipliedSprite;
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
            string resolved = ResolvePath(path);
            Entry entry;
            if ((entries.TryGetValue("g:" + resolved, out entry) || entries.TryGetValue("s:" + resolved, out entry) || entries.TryGetValue("l:" + resolved, out entry)) && entry.Width > 0 && entry.Height > 0)
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
            if (!generated.TryGetValue(name, out entry)) entry = new Entry { Handle = nextHandle++ };
            backend.UploadTexture(entry.Handle, width, height, 0, rgba, false);
            generated[name] = new Entry { Handle = entry.Handle, Width = width, Height = height };
            return entry.Handle;
        }

        public void RemoveGenerated(string name)
        {
            RequireOwner();
            Entry entry;
            if (!generated.TryRemove(name, out entry)) return;
            backend.RemoveResource(0, entry.Handle);
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
                if (entry.Handle != 0) backend.RemoveResource(0, entry.Handle);
                singleChannelTextures.TryRemove(entry.Handle, out _);
            }
        }

        public void Clear()
        {
            RequireOwner();
            foreach (Entry entry in entries.Values) if (entry.Handle != 0) backend.RemoveResource(0, entry.Handle);
            foreach (Entry entry in generated.Values) backend.RemoveResource(0, entry.Handle);
            foreach (ulong[] cube in cubes.Values) if (cube != null) foreach (ulong handle in cube) backend.RemoveResource(0, handle);
            entries.Clear(); generated.Clear(); singleChannelTextures.Clear();
            cubes.Clear();
            foreach(ulong cube in worldCubes.Values)if(cube!=0)backend.RemoveResource(4,cube);worldCubes.Clear();
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

        public static string ResolvePath(string path)
        {
            string normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(normalized) ? normalized : Path.Combine(MyFileSystem.ContentPath, normalized);
        }
    }
}
