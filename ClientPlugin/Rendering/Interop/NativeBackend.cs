using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ClientPlugin.Platform;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;

/// <summary>Owner-thread facade. Native functions copy all input arrays before returning.</summary>
public sealed partial class NativeBackend : IDisposable
{
    public const uint AbiVersion = 1;
    private static readonly object LoadLock = new();
    private static IntPtr library;
    private static NativeFunctions api;
    private IntPtr context;
    private readonly VulkanWindowHost host;
    private readonly List<IntPtr> ownedStrings = new();
    private VulkanWindowHost.SurfaceFactory surfaceFactory;
    public Vector3D CameraPosition { get=>E.CameraPosition; set=>E.CameraPosition=value; }
    public bool IsCreated => context != IntPtr.Zero;
    static long nextGuestEpoch;
    internal long GuestEpoch {get;private set;}
    public static string LibraryPath { get; private set; }
    public static string AssetsRoot { get; private set; }

    public NativeBackend(VulkanWindowHost host) { this.host = host; InitializeEmissions(); }

    public static NativeProbe Probe()
    {
        EnsureLoaded();
        var result = new NativeProbe { Size = (uint)Marshal.SizeOf(typeof(NativeProbe)), AbiVersion = AbiVersion };
        Check(api.Probe(ref result), IntPtr.Zero);
        return result;
    }

    public void Create(uint width, uint height, int vsync, bool validation, string cacheDirectory, uint adapterIndex = 0, uint anisotropy = 1)
    {
        resourceCacheDirectory=cacheDirectory;
        EnsureLoaded();
        var extensions = host?.RequiredInstanceExtensions ?? Array.Empty<string>();
        IntPtr extensionPointers = IntPtr.Zero;
        if (extensions.Length > 0)
        {
            extensionPointers = Marshal.AllocHGlobal(IntPtr.Size * extensions.Length);
            ownedStrings.Add(extensionPointers);
            for (var i = 0; i < extensions.Length; ++i) Marshal.WriteIntPtr(extensionPointers, i * IntPtr.Size, Utf8(extensions[i]));
        }
        surfaceFactory = host?.CreateSurface;
        var info = new NativeCreateInfo
        {
            Size = (uint)Marshal.SizeOf(typeof(NativeCreateInfo)), AbiVersion = AbiVersion,
            Platform = host == null ? 0u : host.IsLinux ? 2u : 1u,
            Validation = validation ? 1u : 0u, Window = host?.WindowHandle ?? IntPtr.Zero,
            Width = width, Height = height, VSync = (uint)Math.Max(0, vsync), AdapterIndex = adapterIndex,
            ShaderDirectory = Utf8(Path.Combine(AssetsRoot, "Shaders", "Compiled")), CacheDirectory = Utf8(cacheDirectory),
            InstanceExtensions = extensionPointers, InstanceExtensionCount = (uint)extensions.Length, Reserved = anisotropy,
            CreateSurface = surfaceFactory == null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(surfaceFactory),
            GetInstanceProcAddr = host?.GetInstanceProcAddr ?? IntPtr.Zero
        };
        Check(api.Create(ref info, out context), context);ResetWorldRecipes();PresentationWidth=width;PresentationHeight=height;
        GuestEpoch=System.Threading.Interlocked.Increment(ref nextGuestEpoch);ResetPostprocessGenerations();
        textureValidationCacheApplied=false;TextureValidationCacheEnabled=textureValidationCacheEnabled;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetAnisotropyFn(IntPtr ctx, uint samples);
    private SetAnisotropyFn setAnisotropy;
    public void SetTextureAnisotropy(uint samples)
    {
        EnsureLoaded();
        setAnisotropy ??= (SetAnisotropyFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_texture_anisotropy"), typeof(SetAnisotropyFn));
        Check(setAnisotropy(context, samples), context);
    }

    public uint PresentationWidth{get;private set;}public uint PresentationHeight{get;private set;}
    public bool ExclusiveFullscreen{get;private set;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetExclusiveFn(IntPtr ctx, uint enabled, IntPtr monitor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetExclusiveFn(IntPtr ctx, out uint acquired);
    private SetExclusiveFn setExclusive; private GetExclusiveFn getExclusive;
    public void SetExclusiveFullscreen(IntPtr monitor)
    {
        EnsureLoaded();
        setExclusive ??= (SetExclusiveFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_exclusive_fullscreen"), typeof(SetExclusiveFn));
        Check(setExclusive(context, monitor == IntPtr.Zero ? 0u : 1u, monitor), context);
        ExclusiveFullscreen = false;
    }
    public bool RefreshExclusiveFullscreen()
    {
        EnsureLoaded();
        getExclusive ??= (GetExclusiveFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_exclusive_fullscreen"), typeof(GetExclusiveFn));
        Check(getExclusive(context, out uint acquired), context);
        ExclusiveFullscreen = acquired != 0;
        return ExclusiveFullscreen;
    }
    public void Resize(uint width, uint height, int vsync){Check(api.Resize(context,width,height,(uint)Math.Max(0,vsync)),context);PresentationWidth=width;PresentationHeight=height;}
    public void ResizeOutput(uint width, uint height, uint logicalWidth, uint logicalHeight, int vsync){Check(api.ResizeOutput(context,width,height,logicalWidth,logicalHeight,(uint)Math.Max(0,vsync)),context);PresentationWidth=width;PresentationHeight=height;}

    public void UploadTexture(ulong id, int width, int height, int format, byte[] bytes, bool srgb = true)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        // Accept VkFormat values as well as the ABI's compact format identifiers.
        Check(api.UploadTexture(context, id, (uint)width, (uint)height, TextureFormat(format), srgb ? 1u : 0u, bytes, (ulong)bytes.LongLength), context);
    }

    public void UploadTextureMips(ulong id, int width, int height, int format, byte[][] levels, bool srgb = true)
    {
        if (levels == null || levels.Length == 0) throw new ArgumentException("A texture needs at least one mip.", nameof(levels));
        if (levels.Length == 1) { UploadTexture(id, width, height, format, levels[0], srgb); return; }
        int length = 0;
        foreach (var level in levels) length = checked(length + (level?.Length ?? throw new ArgumentException("Null texture mip.", nameof(levels))));
        var bytes = new byte[length]; int offset = 0;
        foreach (var level in levels) { Buffer.BlockCopy(level, 0, bytes, offset, level.Length); offset += level.Length; }
        Check(api.UploadTextureMips(context, id, (uint)width, (uint)height, TextureFormat(format), srgb ? 1u : 0u, bytes, (ulong)bytes.LongLength, (uint)levels.Length), context);
    }
    private static uint TextureFormat(int format) => (uint)(format switch { 37 or 43 => 0, 44 or 50 => 1, 131 or 132 or 133 or 134 => 2,
        135 or 136 => 3, 137 or 138 => 4, 139 or 140 => 5, 141 or 142 => 6, 143 => 7, 145 or 146 => 8, 83 => 9, _ => format });

    public void UploadMesh(ulong id, float[] vertices, uint[] indices)
    {
        if (vertices == null || vertices.Length % 8 != 0 || indices == null) throw new ArgumentException("Vertices must be position3/normal3/UV2.");
        Check(api.UploadMesh(context, id, vertices, (uint)(vertices.Length / 8), indices, (uint)indices.Length), context);
    }

    public void DrawMesh(ulong mesh, MatrixD world, Color tint, ulong texture, uint flags = 0,
        ulong normal = 0, ulong extensions = 0, ulong alpha = 0, Vector3? hsv = null, float emissivity = 0, float dithering = 0)
    {
        world.Translation -= CameraPosition;
        var mask = hsv ?? Vector3.Zero;
        var draw = new NativeMeshDraw { Mesh = mesh, Texture = texture, NormalTexture = normal, ExtensionsTexture = extensions, AlphaTexture = alpha,
            World = MatrixValues(world), Tint = (flags & 1024) != 0 ? RawColorValues(tint) : ColorValues(tint), ColorMaskHsv = new[] { mask.X, mask.Y, mask.Z, 0f },
            Emissivity = emissivity, Dithering = dithering, Flags = flags };
        if (E.Collecting) E.FrameMeshes.Add(draw);
        else Check(api.DrawMesh(context, ref draw), context);
    }

    public void BeginPacketCollection() {
        RotateEmission();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PrologueClearPass))if(NativePassReady)ClearPassFrame();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PrologueClearWorld)){E.FrameMeshes.Clear(); ClearWorldPackets();}
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PrologueClearRigid))BeginRigidBatches();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.PrologueClearOther)){ClearEnvironmentPackets(); ClearFoliagePackets(); ClearTransparencyPackets(); ClearDecalPackets();}
        E.Collecting = true; }
    public void DrawMesh(ulong mesh, MatrixD world, Vector4 linearTint, ulong texture, uint flags = 0)
    {
        world.Translation -= CameraPosition;
        var draw = new NativeMeshDraw { Mesh = mesh, Texture = texture, World = MatrixValues(world),
            Tint = new[] { linearTint.X, linearTint.Y, linearTint.Z, linearTint.W }, ColorMaskHsv = new float[4], Flags = flags };
        if (E.Collecting) E.FrameMeshes.Add(draw); else Check(api.DrawMesh(context, ref draw), context);
    }

    public bool BeginFrame(MatrixD viewProjection, Vector3 sunDirection, Vector3 sunColor, Vector3 ambient, Color clear)
    {
        var frame = new NativeFrame { ViewProjection = MatrixValues(viewProjection), Camera = new float[4],
            SunDirection = new[] { sunDirection.X, sunDirection.Y, sunDirection.Z, 0f },
            SunColor = new[] { sunColor.X, sunColor.Y, sunColor.Z, 1f },
            Ambient = new[] { ambient.X, ambient.Y, ambient.Z, 1f }, Clear = ColorValues(clear) };
        var status = api.BeginFrame(context, ref frame);
        Check(status, context);
        ProbeConfigurationApplied();
        E.Collecting = false;
        if (status == 0)
            foreach (var packet in E.FrameMeshes) { var draw = packet; Check(api.DrawMesh(context, ref draw), context); }
        E.FrameMeshes.Clear();
        return status == 0;
    }

    public unsafe void DrawSprites(NativeSpriteBlit[] sprites, int count)
    {
        if (count <= 0) return;
        if (count > sprites.Length) throw new ArgumentOutOfRangeException(nameof(count));
        fixed (NativeSpriteBlit* data = sprites) Check(api.DrawSpritesBlit(context, data, (uint)count), context);
    }
    public void DrawSprites(NativeSprite[] sprites)
    {
        if (sprites != null && sprites.Length > 0) Check(api.DrawSprites(context, sprites, (uint)sprites.Length), context);
    }

    public void SetLights(NativeLight[] lights) => Check(api.SetLights(context, lights, (uint)lights.Length), context);
    public void SetShadowCascades(float[] matrices, float[] splits)
    {
        if (splits.Length > 8 || matrices.Length != splits.Length * 16) throw new ArgumentException("Shadow cascades require one matrix per split, at most eight.");
        Check(api.SetShadows(context, matrices, (uint)splits.Length, splits), context);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int RenderTextureMipsFn(IntPtr ctx,ulong id,uint width,uint height,float[] clear,[In] NativeSprite[] sprites,uint count,float logicalWidth,float logicalHeight,uint generateMips);
    private RenderTextureMipsFn renderTextureMips;
    public void RenderTexture(ulong id, int width, int height, Color clear, NativeSprite[] sprites, Vector2? aspectRatio = null,bool generateMips=false)
    {
        if(generateMips){
            if(renderTextureMips==null)renderTextureMips=(RenderTextureMipsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_render_texture_mips"),typeof(RenderTextureMipsFn));
            Check(renderTextureMips(context,id,(uint)width,(uint)height,ColorValues(clear),sprites,(uint)sprites.Length,width*(aspectRatio?.X??1),height*(aspectRatio?.Y??1),1),context);
        }else if (aspectRatio.HasValue)
            Check(api.RenderTextureAspect(context, id, (uint)width, (uint)height, ColorValues(clear), sprites, (uint)sprites.Length, width * aspectRatio.Value.X, height * aspectRatio.Value.Y), context);
        else Check(api.RenderTexture(context, id, (uint)width, (uint)height, ColorValues(clear), sprites, (uint)sprites.Length), context);
    }

    public void EndFrame() => Check(api.EndFrame(context), context);
    // Pipelined recording: native records this frame on a worker while the next
    // managed frame runs; calls replay in issue order (sevk_end_frame_async).
    public bool PipelinedRecordingEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_PIPELINED_RECORDING")!="0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EndFrameAsyncFn(IntPtr context);
    private EndFrameAsyncFn endFrameAsync;
    public void EndFrameAsync(){endFrameAsync??=(EndFrameAsyncFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_end_frame_async"),typeof(EndFrameAsyncFn));Check(endFrameAsync(context),context);}
    public void RequestReadback() => Check(api.RequestReadback(context), context);
    public void RemoveResource(uint kind, ulong id){Check(api.RemoveResource(context, kind, id), context);DropWorldRecipes(kind,id);}
    public NativeStats GetStats()
    {
        var stats = new NativeStats { Size = (uint)Marshal.SizeOf(typeof(NativeStats)), AbiVersion = AbiVersion };
        if (IsCreated) Check(api.GetStats(context, ref stats), context);
        return stats;
    }
    public byte[] Readback(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        Check(api.Readback(context, pixels, (ulong)pixels.LongLength), context);
        return pixels;
    }

    public void Dispose()
    {
        if (context != IntPtr.Zero) { api.Destroy(context); context = IntPtr.Zero;ResetWorldRecipes();NativePassReady=false; }
        foreach (var pointer in ownedStrings) Marshal.FreeHGlobal(pointer);
        ownedStrings.Clear();
        surfaceFactory = null;
    }

    public static float[] MatrixValues(MatrixD m) => new[] {
        (float)m.M11, (float)m.M12, (float)m.M13, (float)m.M14,
        (float)m.M21, (float)m.M22, (float)m.M23, (float)m.M24,
        (float)m.M31, (float)m.M32, (float)m.M33, (float)m.M34,
        (float)m.M41, (float)m.M42, (float)m.M43, (float)m.M44 };
    public static void MatrixValues(MatrixD m, float[] into)
    {
        into[0]=(float)m.M11;into[1]=(float)m.M12;into[2]=(float)m.M13;into[3]=(float)m.M14;
        into[4]=(float)m.M21;into[5]=(float)m.M22;into[6]=(float)m.M23;into[7]=(float)m.M24;
        into[8]=(float)m.M31;into[9]=(float)m.M32;into[10]=(float)m.M33;into[11]=(float)m.M34;
        into[12]=(float)m.M41;into[13]=(float)m.M42;into[14]=(float)m.M43;into[15]=(float)m.M44;
    }
    public static float[] RawColorValues(Color c) => new[] { c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f };
    public static float[] ColorValues(Color c) => new[] { ToLinear(c.R / 255f), ToLinear(c.G / 255f), ToLinear(c.B / 255f), c.A / 255f };
    private static float ToLinear(float value) => value <= .04045f ? value / 12.92f : (float)Math.Pow((value + .055f) / 1.055f, 2.4);

    private IntPtr Utf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        ownedStrings.Add(pointer);
        return pointer;
    }

    private static void Check(int result, IntPtr ctx)
    {
        if (result >= 0) return;
        var error = api?.LastError(ctx) ?? IntPtr.Zero;
        throw new InvalidOperationException("Vulkan native failure " + result + ": " + (Marshal.PtrToStringAnsi(error) ?? "unknown"));
    }

    private static void EnsureLoaded()
    {
        lock (LoadLock)
        {
            if (api != null) return;
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Vulkan requires an x64 process.");
            var windows = Path.DirectorySeparatorChar == '\\';
            var file = windows ? "se_vulkan_native.dll" : "libse_vulkan_native.so";
            var assemblyDirectory = Path.GetDirectoryName(typeof(NativeBackend).Assembly.Location);
            var roots = new List<string> { assemblyDirectory, Path.Combine(assemblyDirectory, "Assets") };
            var explicitAssets = BootstrapAssets.FindDirectory(Path.Combine("Native", windows ? "win-x64" : "linux-x64", file));
            if (explicitAssets != null) roots.Insert(0, explicitAssets);
            int depth = 0;
            for (var parent = new DirectoryInfo(assemblyDirectory); parent != null && depth < 7; parent = parent.Parent, ++depth)
                if (File.Exists(Path.Combine(parent.FullName, "Vulkan.xml"))) roots.Add(Path.Combine(parent.FullName, "Assets"));
            foreach (var root in roots)
            {
                var candidates = new[] { Path.Combine(root, file), Path.Combine(root, "Native", windows ? "win-x64" : "linux-x64", file) };
                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate)) continue;
#if NETCOREAPP
                    library = NativeLibrary.Load(candidate);
#else
                    library = LoadLibraryEx(candidate, IntPtr.Zero, 0x00000008);
                    if (library == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
#endif
                    LibraryPath = candidate;
                    AssetsRoot = Directory.Exists(Path.Combine(root, "Shaders")) ? root : Path.Combine(assemblyDirectory, "Assets");
                    ValidateRequiredSymbols(GetSymbol);
                    api = new NativeFunctions(GetSymbol);
                    if (api.ApiVersion() != AbiVersion) { api = null; throw new InvalidOperationException("Vulkan native ABI mismatch."); }
                    return;
                }
            }
            throw new FileNotFoundException("Native Vulkan backend is missing; run scripts/build-native.ps1 or install the platform's complete asset bundle.", file);
        }
    }

    internal static void ValidateRequiredSymbols(Func<string, IntPtr> symbol)
    {
        foreach (string name in new[] {
            "sevk_api_version",
            "sevk_init_postprocess",
            "sevk_set_billboard_projections", "sevk_set_shadow_options", "sevk_init_stock_shadows", "sevk_init_occlusion", "sevk_queue_occlusion", "sevk_get_occlusion", "sevk_get_occlusion_stats", "sevk_set_shadow_exclusions", "sevk_set_spots", "sevk_get_spot_stats", "sevk_set_probe_atmosphere", "sevk_set_shadow_quality", "sevk_queue_highlights", "sevk_get_highlight_stats", "sevk_set_render_fraction", "sevk_get_resolution_stats",
            "sevk_guest_reset", "sevk_guest_create_pipeline", "sevk_guest_create_image", "sevk_guest_update_image", "sevk_guest_submit", "sevk_guest_readback", "sevk_guest_replay_now", "sevk_guest_replay_count", "sevk_get_guest_frame", "sevk_set_view_clip", "sevk_set_display_flags", "sevk_set_upscale_owner", "sevk_invalidate_guest_history", "sevk_ngx_configure", "sevk_ngx_status", "sevk_fsr_configure", "sevk_fsr_status", "sevk_fg_status", "sevk_get_ray_capabilities", "sevk_set_ray_options",
            "sevk_init_probes",
            "sevk_set_probes",
            "sevk_get_probe_stats",
            "sevk_readback_probe",
            "sevk_set_postprocess",
            "sevk_set_hbao",
            "sevk_get_postprocess_stats",
            "sevk_begin_frame",
            "sevk_begin_uploads",
            "sevk_build_voxel_array",
            "sevk_create",
            "sevk_set_texture_anisotropy",
            "sevk_destroy",
            "sevk_draw_mesh",
            "sevk_draw_sprites",
            "sevk_end_frame",
            "sevk_end_uploads",
            "sevk_get_environment_stats",
            "sevk_get_flare_results",
            "sevk_get_gbuffer_stats",
            "sevk_get_particle_stats",
            "sevk_get_stats",
            "sevk_get_transparency_stats",
            "sevk_get_voxel_stats",
            "sevk_get_work_stats",
            "sevk_init_decals",
            "sevk_init_environment",
            "sevk_init_gbuffer",
            "sevk_init_object_motion",
            "sevk_init_gbuffer_attachment",
            "sevk_init_particles",
            "sevk_init_transparency",
            "sevk_init_world_lighting",
            "sevk_init_world_pipeline",
            "sevk_last_error",
            "sevk_probe",
            "sevk_queue_billboards",
            "sevk_queue_decals",
            "sevk_queue_environment",
            "sevk_queue_flares",
            "sevk_queue_flare_raster",
            "sevk_queue_foliage",
            "sevk_queue_particles",
            "sevk_queue_transparency",
            "sevk_queue_world",
            "sevk_world_object_alignment",
            "sevk_queue_world_blob",
            "sevk_readback",
            "sevk_readback_gbuffer",
            "sevk_remove_resource",
            "sevk_render_gbuffer",
            "sevk_render_texture",
            "sevk_render_texture_aspect",
            "sevk_request_readback",
            "sevk_resize",
            "sevk_resize_output",
            "sevk_set_exclusive_fullscreen",
            "sevk_get_exclusive_fullscreen",
            "sevk_set_lights",
            "sevk_set_shadow_cascades",
            "sevk_set_voxel_material",
            "sevk_set_world_skybox",
            "sevk_upload_foliage",
            "sevk_upload_gbuffer_mesh",
            "sevk_upload_mesh",
            "sevk_upload_texture",
            "sevk_upload_texture_mips",
            "sevk_upload_voxel_array",
            "sevk_upload_voxel_mesh",
            "sevk_upload_world_cube",
            "sevk_upload_world_mesh",
        })
            if (symbol(name) == IntPtr.Zero) throw new EntryPointNotFoundException(name);
    }

    private static IntPtr GetSymbol(string name)
    {
#if NETCOREAPP
        return NativeLibrary.GetExport(library, name);
#else
        var pointer = GetProcAddress(library, name);
        if (pointer == IntPtr.Zero) throw new EntryPointNotFoundException(name);
        return pointer;
#endif
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}

[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
public struct NativeProbe
{
    public uint Size, AbiVersion, LoaderApi, DeviceApi, DeviceCount, CompatibleDeviceCount, Features, ValidationAvailable;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string Detail;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeCreateInfo
{
    public uint Size, AbiVersion, Platform, Validation;
    public IntPtr Window;
    public uint Width, Height, VSync, AdapterIndex;
    public IntPtr ShaderDirectory, CacheDirectory, InstanceExtensions;
    public uint InstanceExtensionCount, Reserved;
    public IntPtr CreateSurface, SurfaceUser, GetInstanceProcAddr;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeFrame
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public float[] ViewProjection;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] Camera, SunDirection, SunColor, Ambient, Clear;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeMeshDraw
{
    public ulong Mesh, Texture, NormalTexture, ExtensionsTexture, AlphaTexture;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public float[] World;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] Tint, ColorMaskHsv;
    public float Dithering, Emissivity;
    public uint Flags, Reserved;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeLight
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] PositionRange, ColorIntensity, DirectionCone;
    public uint Flags, Reserved0, Reserved1, Reserved2;
}
// Blittable twin of NativeSprite with the same native layout, for the per-frame main batch.
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public unsafe struct NativeSpriteBlit
{
    public ulong Texture;
    public fixed float Destination[4], Uv[4], Color[4];
    public float Angle;
    public fixed float Origin[2];
    public fixed int Clip[4];
    public uint Flags;
    public NativeSprite ToSprite()
    {
        var sprite = new NativeSprite { Texture = Texture, Angle = Angle, Flags = Flags, Destination = new float[4], Uv = new float[4], Color = new float[4], Origin = new float[2], Clip = new int[4] };
        for (int i = 0; i < 4; ++i) { sprite.Destination[i] = Destination[i]; sprite.Uv[i] = Uv[i]; sprite.Color[i] = Color[i]; sprite.Clip[i] = Clip[i]; }
        sprite.Origin[0] = Origin[0]; sprite.Origin[1] = Origin[1];
        return sprite;
    }
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeSprite
{
    public ulong Texture;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] Destination, Uv, Color;
    public float Angle;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] Origin;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] Clip;
    public uint Flags;
}
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Ansi)]
public struct NativeStats
{
    public uint Size, AbiVersion, ApiVersion, ValidationEnabled;
    public ulong SubmittedFrames, PresentedFrames, MeshDraws, SpriteDraws, UploadedBytes, Textures, Meshes, ValidationErrors, CompletedTimeline;
    public uint Width, Height, SurfaceReady, DeviceLost;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string Detail;
}

internal sealed class NativeFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint ApiVersionFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProbeFn(ref NativeProbe probe);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CreateFn(ref NativeCreateInfo info, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ResizeFn(IntPtr ctx, uint width, uint height, uint vsync);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ResizeOutputFn(IntPtr ctx, uint width, uint height, uint logicalWidth, uint logicalHeight, uint vsync);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UploadTextureFn(IntPtr ctx, ulong id, uint width, uint height, uint format, uint srgb, byte[] bytes, ulong count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UploadTextureMipsFn(IntPtr ctx, ulong id, uint width, uint height, uint format, uint srgb, byte[] bytes, ulong count, uint mipCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UploadMeshFn(IntPtr ctx, ulong id, float[] vertices, uint vertexCount, uint[] indices, uint indexCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int BeginFrameFn(IntPtr ctx, ref NativeFrame info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DrawMeshFn(IntPtr ctx, ref NativeMeshDraw draw);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int DrawSpritesFn(IntPtr ctx, [In] NativeSprite[] sprites, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate int DrawSpritesBlitFn(IntPtr ctx, NativeSpriteBlit* sprites, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetLightsFn(IntPtr ctx, [In] NativeLight[] lights, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int SetShadowsFn(IntPtr ctx, float[] matrices, uint count, float[] splits);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RenderTextureFn(IntPtr ctx, ulong id, uint width, uint height, float[] clear, [In] NativeSprite[] sprites, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RenderTextureAspectFn(IntPtr ctx, ulong id, uint width, uint height, float[] clear, [In] NativeSprite[] sprites, uint count, float logicalWidth, float logicalHeight);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ContextFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RemoveFn(IntPtr ctx, uint kind, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StatsFn(IntPtr ctx, ref NativeStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ReadbackFn(IntPtr ctx, [Out] byte[] rgba, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DestroyFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ErrorFn(IntPtr ctx);
    internal ApiVersionFn ApiVersion;
    internal ProbeFn Probe;
    internal CreateFn Create;
    internal ResizeFn Resize;
    internal ResizeOutputFn ResizeOutput;
    internal UploadTextureFn UploadTexture;
    internal UploadTextureMipsFn UploadTextureMips;
    internal UploadMeshFn UploadMesh;
    internal BeginFrameFn BeginFrame;
    internal DrawMeshFn DrawMesh;
    internal DrawSpritesFn DrawSprites; internal DrawSpritesBlitFn DrawSpritesBlit;
    internal SetLightsFn SetLights;
    internal SetShadowsFn SetShadows;
    internal RenderTextureFn RenderTexture;
    internal RenderTextureAspectFn RenderTextureAspect;
    internal ContextFn EndFrame;
    internal ContextFn RequestReadback;
    internal RemoveFn RemoveResource;
    internal StatsFn GetStats;
    internal ReadbackFn Readback;
    internal DestroyFn Destroy;
    internal ErrorFn LastError;
    internal NativeFunctions(Func<string, IntPtr> symbol)
    {
        T Get<T>(string name) where T : Delegate => (T)Marshal.GetDelegateForFunctionPointer(symbol(name), typeof(T));
        ApiVersion = Get<ApiVersionFn>("sevk_api_version"); Probe = Get<ProbeFn>("sevk_probe"); Create = Get<CreateFn>("sevk_create");
        Resize = Get<ResizeFn>("sevk_resize"); ResizeOutput = Get<ResizeOutputFn>("sevk_resize_output"); UploadTexture = Get<UploadTextureFn>("sevk_upload_texture"); UploadMesh = Get<UploadMeshFn>("sevk_upload_mesh");
        UploadTextureMips = Get<UploadTextureMipsFn>("sevk_upload_texture_mips");
        BeginFrame = Get<BeginFrameFn>("sevk_begin_frame"); DrawMesh = Get<DrawMeshFn>("sevk_draw_mesh"); DrawSprites = Get<DrawSpritesFn>("sevk_draw_sprites"); DrawSpritesBlit = Get<DrawSpritesBlitFn>("sevk_draw_sprites");
        SetLights = Get<SetLightsFn>("sevk_set_lights"); RenderTexture = Get<RenderTextureFn>("sevk_render_texture");
        RenderTextureAspect = Get<RenderTextureAspectFn>("sevk_render_texture_aspect");
        SetShadows = Get<SetShadowsFn>("sevk_set_shadow_cascades");
        EndFrame = Get<ContextFn>("sevk_end_frame"); RemoveResource = Get<RemoveFn>("sevk_remove_resource"); GetStats = Get<StatsFn>("sevk_get_stats");
        RequestReadback = Get<ContextFn>("sevk_request_readback");
        Readback = Get<ReadbackFn>("sevk_readback"); Destroy = Get<DestroyFn>("sevk_destroy"); LastError = Get<ErrorFn>("sevk_last_error");
    }
}
