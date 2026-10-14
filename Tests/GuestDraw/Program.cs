using System.Diagnostics;
using System.Runtime.InteropServices;

// Descriptor-free IsolatedAdd guest draw. Exits 0 when the native library, DXC, or a GPU is missing.
const int Width = 8;
const int Height = 8;

var root = FindRoot();
var native = new[]
{
    Path.Combine(root, "Native", "build", "Release", "se_vulkan_native.dll"),
    Path.Combine(root, "Assets", "Native", "win-x64", "se_vulkan_native.dll")
}.FirstOrDefault(File.Exists);
var dxc = new[]
{
    Path.Combine(root, "Native", "build", "dxc", "bin", "x64", "dxc.exe"),
    Path.Combine(root, "Assets", "Tools", "win-x64", "dxc.exe")
}.FirstOrDefault(File.Exists);
if (native == null || dxc == null)
{
    Console.WriteLine("guest draw skipped: native library or dxc is not built");
    return 0;
}

var dir = Directory.CreateTempSubdirectory("anomaly-guest-draw");
try
{
    var vs = Path.Combine(dir.FullName, "vs.hlsl");
    var ps = Path.Combine(dir.FullName, "ps.hlsl");
    File.WriteAllText(vs, "float4 main(uint id : SV_VertexID) : SV_Position { float2 p = float2((id << 1) & 2, id & 2); return float4(p * float2(2, -2) + float2(-1, 1), 0, 1); }\n");
    File.WriteAllText(ps, "float4 main() : SV_Target0 { return float4(1, 0, 0, 1); }\n");
    var vsSpv = Compile(dxc, vs, "vs_5_0", Path.Combine(dir.FullName, "vs.spv"));
    var psSpv = Compile(dxc, ps, "ps_5_0", Path.Combine(dir.FullName, "ps.spv"));
    if (vsSpv == null || psSpv == null)
    {
        Console.WriteLine("guest draw skipped: dxc did not emit SPIR-V");
        return 0;
    }

    if (!Native.Load(native))
    {
        Console.WriteLine("guest draw skipped: native exports are missing");
        return 0;
    }

        var shaderDir = Directory.Exists(Path.Combine(root, "Assets", "Shaders", "Compiled"))
            ? Path.Combine(root, "Assets", "Shaders", "Compiled")
            : Path.Combine(root, "Native", "build", "shaders");
        var shaders = Marshal.StringToHGlobalAnsi(shaderDir);
        var cache = Marshal.StringToHGlobalAnsi(dir.FullName);
    try
    {
        var info = new SevkCreateInfo
        {
            size = (uint)Marshal.SizeOf<SevkCreateInfo>(),
            abi_version = 1,
            platform = 0,
            enable_validation = 1,
            width = Width,
            height = Height,
            shader_directory = shaders,
            cache_directory = cache
        };
        int created = Native.Create(ref info, out var ctx);
        if (created != 0 || ctx == IntPtr.Zero)
        {
            var reason = Native.LastError();
            Console.WriteLine("guest draw skipped: offscreen device was not created (" + created + ") " + reason);
            if (reason != null && reason.Contains("ABI")) return 1;
            return 0;
        }
        try
        {
            int plain = Run(ctx, vsSpv, psSpv);
        if (plain != 0) return plain;
        return Bus(ctx, dxc, dir.FullName);
        }
        finally
        {
            Native.Destroy(ctx);
        }
    }
    finally
    {
        Marshal.FreeHGlobal(shaders);
        Marshal.FreeHGlobal(cache);
    }
}
finally
{
    try { dir.Delete(true); } catch { }
}

static int Run(IntPtr ctx, byte[] vs, byte[] ps)
{
    var vsPin = GCHandle.Alloc(vs, GCHandleType.Pinned);
    var psPin = GCHandle.Alloc(ps, GCHandleType.Pinned);
    try
    {
        int pipe = Native.CreatePipeline(ctx, vsPin.AddrOfPinnedObject(), (ulong)vs.Length, "main", psPin.AddrOfPinnedObject(), (ulong)ps.Length, "main", out var pipeline);
        if (pipe != 0 || pipeline == 0)
        {
            Console.WriteLine("guest draw failed: pipeline " + pipe);
            return 1;
        }
        const ulong image = 7;
        int imageRc = Native.CreateImage(ctx, image, Width, Height, 1, 0);
        if (imageRc != 0)
        {
            Console.WriteLine("guest draw failed: image " + imageRc);
            return 1;
        }
        var pass = new byte[416];
        WriteU32(pass, 0, 416);
        WriteU32(pass, 4, 1);
        WriteU32(pass, 8, 1);
        WriteU32(pass, 12, 0);
        WriteU32(pass, 16, Width);
        WriteU32(pass, 20, Height);
        WriteU64(pass, 24, pipeline);
        WriteU64(pass, 32, image);
        var pin = GCHandle.Alloc(pass, GCHandleType.Pinned);
        try
        {
            int submitted = Native.Submit(ctx, pin.AddrOfPinnedObject(), 1);
            int replayed = Native.ReplayNow(ctx, 1);
            if (submitted != 0 || replayed != 0)
            {
                Console.WriteLine("guest draw failed: submit " + submitted + " replay " + replayed);
                return 1;
            }
        }
        finally { pin.Free(); }

        var stats = new SevkStats { size = (uint)Marshal.SizeOf<SevkStats>(), abi_version = 1 };
        Native.GetStats(ctx, ref stats);
        var pixels = new byte[Width * Height * 4];
        var pixelPin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            int read = Native.Readback(ctx, image, pixelPin.AddrOfPinnedObject(), (ulong)pixels.Length);
            if (read != 0)
            {
                Console.WriteLine("guest draw failed: readback " + read);
                return 1;
            }
        }
        finally { pixelPin.Free(); }

        int red = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == 255 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 255) red++;
        if (stats.validation_errors != 0 || red == 0)
        {
            Console.WriteLine("guest draw failed: validation " + stats.validation_errors + " red pixels " + red);
            return 1;
        }
        Console.WriteLine("guest draw validation clean, " + red + " red pixels");
        return 0;
    }
    finally
    {
        vsPin.Free();
        psPin.Free();
    }
}

static int Bus(IntPtr ctx, string dxc, string dir)
{
    var vs = Path.Combine(dir, "bus-vs.hlsl");
    var ps = Path.Combine(dir, "bus-ps.hlsl");
    var outside = Path.Combine(dir, "outside-ps.hlsl");
    File.WriteAllText(vs, "struct VsOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; }; VsOut main(uint id : SV_VertexID) { float2 uv = float2((id << 1) & 2, id & 2); VsOut o; o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); o.uv = uv; return o; }\n");
    File.WriteAllText(ps, "Texture2D<float4> Color : register(t0); SamplerState Point : register(s0); float4 main(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 { return Color.SampleLevel(Point, uv, 0); }\n");
    File.WriteAllText(outside, "Texture2D<float4> Color : register(t20); float4 main() : SV_Target0 { return Color.Load(int3(0, 0, 0)); }\n");
    string shifts = "-fvk-b-shift 0 0 -fvk-t-shift 128 0 -fvk-s-shift 256 0 -fvk-u-shift 384 0";
    var vsSpv = Compile(dxc, vs, "vs_5_0", Path.Combine(dir, "bus-vs.spv"), shifts);
    var psSpv = Compile(dxc, ps, "ps_5_0", Path.Combine(dir, "bus-ps.spv"), shifts);
    var badSpv = Compile(dxc, outside, "ps_5_0", Path.Combine(dir, "outside-ps.spv"), shifts);
    if (vsSpv == null || psSpv == null || badSpv == null)
    {
        Console.WriteLine("guest bus skipped: dxc did not emit the bus shaders");
        return 0;
    }
    var vsPin = GCHandle.Alloc(vsSpv, GCHandleType.Pinned);
    var psPin = GCHandle.Alloc(psSpv, GCHandleType.Pinned);
    var badPin = GCHandle.Alloc(badSpv, GCHandleType.Pinned);
    try
    {
        int bad = Native.CreatePipeline(ctx, vsPin.AddrOfPinnedObject(), (ulong)vsSpv.Length, "main", badPin.AddrOfPinnedObject(), (ulong)badSpv.Length, "main", out var rejected);
        if (bad != 0 || rejected != 0)
        {
            Console.WriteLine("guest bus failed: a t20 shader was not skipped");
            return 1;
        }
        int pipe = Native.CreatePipeline(ctx, vsPin.AddrOfPinnedObject(), (ulong)vsSpv.Length, "main", psPin.AddrOfPinnedObject(), (ulong)psSpv.Length, "main", out var pipeline);
        if (pipe != 0 || pipeline == 0)
        {
            Console.WriteLine("guest bus failed: pipeline " + pipe);
            return 1;
        }
        const ulong source = 11, dest = 12;
        if (Native.CreateImage(ctx, source, Width, Height, 1, 0) != 0 || Native.CreateImage(ctx, dest, Width, Height, 1, 0) != 0)
            return 1;
        var red = new byte[Width * Height * 4];
        for (int i = 0; i < red.Length; i += 4) { red[i] = 255; red[i + 3] = 255; }
        var redPin = GCHandle.Alloc(red, GCHandleType.Pinned);
        try
        {
            if (Native.UpdateImage(ctx, source, redPin.AddrOfPinnedObject(), (ulong)red.Length) != 0)
                return 1;
        }
        finally { redPin.Free(); }
        Native.Reset(ctx);
        var pass = new byte[416];
        WriteU32(pass, 0, 416); WriteU32(pass, 4, 1); WriteU32(pass, 8, 1); WriteU32(pass, 12, 5);
        WriteU32(pass, 16, Width); WriteU32(pass, 20, Height); WriteU64(pass, 24, pipeline); WriteU64(pass, 32, dest);
        WriteU64(pass, 304, source);
        var pin = GCHandle.Alloc(pass, GCHandleType.Pinned);
        try
        {
            if (Native.Submit(ctx, pin.AddrOfPinnedObject(), 1) != 0 || Native.ReplayNow(ctx, 1) != 0)
            {
                Console.WriteLine("guest bus failed: submit or replay");
                return 1;
            }
        }
        finally { pin.Free(); }
        var pixels = new byte[Width * Height * 4];
        var pixelPin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { if (Native.Readback(ctx, dest, pixelPin.AddrOfPinnedObject(), (ulong)pixels.Length) != 0) return 1; }
        finally { pixelPin.Free(); }
        int copied = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == 255 && pixels[i + 1] == 0 && pixels[i + 2] == 0 && pixels[i + 3] == 255) copied++;
        var stats = new SevkStats { size = (uint)Marshal.SizeOf<SevkStats>(), abi_version = 1 };
        Native.GetStats(ctx, ref stats);
        if (stats.validation_errors != 0 || copied == 0)
        {
            Console.WriteLine("guest bus failed: validation " + stats.validation_errors + " red pixels " + copied);
            return 1;
        }
        Console.WriteLine("guest bus validation clean, " + copied + " red pixels");
        return 0;
    }
    finally
    {
        vsPin.Free(); psPin.Free(); badPin.Free();
    }
}

static byte[] Compile(string dxc, string source, string profile, string output, string extra = "")
{
    var psi = new ProcessStartInfo(dxc, "-spirv -fspv-target-env=vulkan1.3 -fvk-use-dx-layout -fvk-use-dx-position-w -HV 2016 -Zpr -T " + profile + " -E main " + extra + " -Fo \"" + output + "\" \"" + source + "\"")
    {
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = Process.Start(psi);
    process.WaitForExit();
    if (process.ExitCode != 0 || !File.Exists(output)) return null;
    return File.ReadAllBytes(output);
}

static void WriteU32(byte[] data, int offset, uint value)
{
    data[offset] = (byte)value;
    data[offset + 1] = (byte)(value >> 8);
    data[offset + 2] = (byte)(value >> 16);
    data[offset + 3] = (byte)(value >> 24);
}

static void WriteU64(byte[] data, int offset, ulong value)
{
    WriteU32(data, offset, (uint)value);
    WriteU32(data, offset + 4, (uint)(value >> 32));
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Native", "include", "se_vulkan.h"))) return dir.FullName;
        dir = dir.Parent;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
struct SevkCreateInfo
{
    public uint size, abi_version, platform, enable_validation;
    public IntPtr window;
    public uint width, height, vsync, adapter_index;
    public IntPtr shader_directory, cache_directory, instance_extensions;
    public uint instance_extension_count, reserved;
    public IntPtr create_surface, surface_user, get_instance_proc_addr;
}

[StructLayout(LayoutKind.Sequential)]
struct SevkStats
{
    public uint size, abi_version, api_version, validation_enabled;
    public ulong submitted_frames, presented_frames, mesh_draws, sprite_draws;
    public ulong uploaded_bytes, textures, meshes, validation_errors, completed_timeline;
    public uint width, height, surface_ready, device_lost;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string device_name;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string detail;
}

static class Native
{
    static IntPtr module;

    public static bool Load(string path)
    {
        module = LoadLibrary(path);
        if (module == IntPtr.Zero) return false;
        return GetProcAddress(module, "sevk_guest_replay_now") != IntPtr.Zero && GetProcAddress(module, "sevk_create") != IntPtr.Zero;
    }

    public static int Create(ref SevkCreateInfo info, out IntPtr ctx)
    {
        ctx = IntPtr.Zero;
        var fn = Marshal.GetDelegateForFunctionPointer<CreateDelegate>(GetProcAddress(module, "sevk_create"));
        return fn(ref info, ref ctx);
    }

    public static int CreatePipeline(IntPtr ctx, IntPtr vs, ulong vsBytes, string vsEntry, IntPtr ps, ulong psBytes, string psEntry, out ulong pipeline)
    {
        pipeline = 0;
        var fn = Marshal.GetDelegateForFunctionPointer<PipelineDelegate>(GetProcAddress(module, "sevk_guest_create_pipeline"));
        return fn(ctx, vs, vsBytes, vsEntry, ps, psBytes, psEntry, ref pipeline);
    }

    public static int Reset(IntPtr ctx)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ReplayDelegate>(GetProcAddress(module, "sevk_guest_reset"));
        return fn(ctx, 0);
    }

    public static int UpdateImage(IntPtr ctx, ulong id, IntPtr data, ulong bytes)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ReadbackDelegate>(GetProcAddress(module, "sevk_guest_update_image"));
        return fn(ctx, id, data, bytes);
    }

    public static int CreateImage(IntPtr ctx, ulong id, uint width, uint height, uint depth, uint format)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ImageDelegate>(GetProcAddress(module, "sevk_guest_create_image"));
        return fn(ctx, id, width, height, depth, format);
    }

    public static int Submit(IntPtr ctx, IntPtr passes, uint count)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<SubmitDelegate>(GetProcAddress(module, "sevk_guest_submit"));
        return fn(ctx, passes, count);
    }

    public static int ReplayNow(IntPtr ctx, uint mark)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ReplayDelegate>(GetProcAddress(module, "sevk_guest_replay_now"));
        return fn(ctx, mark);
    }

    public static int Readback(IntPtr ctx, ulong id, IntPtr rgba, ulong bytes)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ReadbackDelegate>(GetProcAddress(module, "sevk_guest_readback"));
        return fn(ctx, id, rgba, bytes);
    }

    public static int GetStats(IntPtr ctx, ref SevkStats stats)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<StatsDelegate>(GetProcAddress(module, "sevk_get_stats"));
        return fn(ctx, ref stats);
    }

    public static string LastError()
    {
        var fn = Marshal.GetDelegateForFunctionPointer<ErrorDelegate>(GetProcAddress(module, "sevk_last_error"));
        var text = fn(IntPtr.Zero);
        return text == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(text);
    }

    public static void Destroy(IntPtr ctx)
    {
        var fn = Marshal.GetDelegateForFunctionPointer<DestroyDelegate>(GetProcAddress(module, "sevk_destroy"));
        fn(ctx);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateDelegate(ref SevkCreateInfo info, ref IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PipelineDelegate(IntPtr ctx, IntPtr vs, ulong vsBytes, string vsEntry, IntPtr ps, ulong psBytes, string psEntry, ref ulong pipeline);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ImageDelegate(IntPtr ctx, ulong id, uint width, uint height, uint depth, uint format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SubmitDelegate(IntPtr ctx, IntPtr passes, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReplayDelegate(IntPtr ctx, uint mark);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReadbackDelegate(IntPtr ctx, ulong id, IntPtr rgba, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StatsDelegate(IntPtr ctx, ref SevkStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorDelegate(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyDelegate(IntPtr ctx);

    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
}
