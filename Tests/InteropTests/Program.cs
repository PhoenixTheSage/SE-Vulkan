using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using ClientPlugin.Rendering.Interop;
using VRageMath;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Pass the installed Space Engineers Bin64 path.");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(args[0], name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        Check(Marshal.SizeOf<NativeProbe>() == 1312, "managed probe packing");
        Check(Marshal.SizeOf<NativeSprite>() == 88, "managed sprite packing");
        Check(Marshal.SizeOf<NativeLight>() == 64, "managed light packing");
        Check(Marshal.SizeOf<NativeStats>() == 872, "managed stats packing");
        foreach (var pair in new[] { ("NativeCreateInfo", 96), ("NativeFrame", 144), ("NativeMeshDraw", 152) })
            Check(Marshal.SizeOf(typeof(NativeBackend).Assembly.GetType("ClientPlugin.Rendering.Interop." + pair.Item1)) == pair.Item2, pair.Item1 + " packing");
        NativeProbe probe = NativeBackend.Probe();
        Check(probe.LoaderApi >= 0x00404000 && probe.CompatibleDeviceCount > 0, "Vulkan 1.4 preflight across managed C ABI");
        using var backend = new NativeBackend(null);
        backend.Create(64, 64, 0, true, Path.Combine(Path.GetTempPath(), "SE-Vulkan-InteropTests"));
        backend.UploadTexture(1, 1, 1, 0, new byte[] { 255, 0, 0, 255 }, false);
        backend.UploadMesh(1, new float[] { -.6f,-.6f,.5f,0,0,1,0,0, .6f,-.6f,.5f,0,0,1,1,0, 0,.6f,.5f,0,0,1,.5f,1 }, new uint[] { 0,1,2 });
        var sprite = new NativeSprite { Texture = 1, Destination = new float[] { 4,4,16,16 }, Uv = new float[] { 0,0,1,1 }, Color = new float[] { 1,1,1,1 }, Origin = new float[2], Clip = new int[] { 0,0,-1,0 } };
        backend.RenderTexture(2, 32, 32, Color.Green, new[] { sprite });
        backend.SetLights(Array.Empty<NativeLight>()); backend.SetShadowCascades(Array.Empty<float>(), Array.Empty<float>());
        backend.BeginPacketCollection();
        backend.DrawMesh(1, MatrixD.Identity, Color.Blue, 0, 2);
        Check(backend.BeginFrame(MatrixD.Identity, Vector3.Zero, Vector3.Zero, Vector3.One, Color.Black), "headless managed frame begins");
        backend.DrawSprites(new[] { sprite }); backend.RequestReadback(); backend.EndFrame();
        byte[] pixels = backend.Readback(64, 64);
        Check(pixels[(10 * 64 + 10) * 4] > 240 && pixels[(10 * 64 + 10) * 4 + 2] < 10, "marshaled sprite writes red GPU pixels");
        Check(pixels[(32 * 64 + 32) * 4 + 2] > 240 && pixels[(32 * 64 + 32) * 4] < 10, "marshaled indexed mesh writes blue GPU pixels");
        NativeStats stats = backend.GetStats();
        Check(stats.SubmittedFrames > 0 && stats.MeshDraws > 0 && stats.SpriteDraws > 0, "managed diagnostic counters reflect native draws");
        if (stats.ValidationErrors != 0) Console.Error.WriteLine("Native validation: " + stats.Detail);
        Check(stats.ValidationErrors == 0, "native reports no validation errors (availability " + probe.ValidationAvailable + ")");
        var aspectSprite = sprite; aspectSprite.Destination = new float[] { 4,2,8,4 }; aspectSprite.Clip = new int[] { 6,3,4,2 };
        backend.RenderTexture(2,32,32,Color.Green,new[] { aspectSprite }, new Vector2(.5f,.25f));
        var lcd = sprite; lcd.Texture = 2; lcd.Destination = new float[] { 0,0,32,32 };
        backend.BeginFrame(MatrixD.Identity,Vector3.Zero,Vector3.Zero,Vector3.One,Color.Black);
        backend.DrawSprites(new[] { lcd }); backend.EndFrame(); pixels = backend.Readback(64,64);
        Check(pixels[(16*64+16)*4] > 240 && pixels[(16*64+10)*4+1] > 100 && pixels[(22*64+16)*4+1] > 100, "managed LCD aspect marshals logical viewport and scaled clipping");
        backend.RemoveResource(0, 1); backend.RemoveResource(0, 2); backend.RemoveResource(1, 1);
        backend.Resize(0, 0, 0);
        Check(!backend.BeginFrame(MatrixD.Identity, Vector3.Zero, Vector3.Zero, Vector3.One, Color.Black), "minimized presentation suspends");
        backend.Resize(32, 32, 0);
        Check(backend.BeginFrame(MatrixD.Identity, Vector3.Zero, Vector3.Zero, Vector3.One, Color.Black), "presentation restores"); backend.EndFrame();
        // Frame split, milestone 5: an upload from another thread while this thread's frame is open. With
        // SE_VULKAN_FRAME_GATE=1 it waits for the frame to close; without, it meets the open frame and fails, and
        // the failure text is that thread's own (per-thread last error).
        bool gate = Environment.GetEnvironmentVariable("SE_VULKAN_FRAME_GATE") == "1";
        Check(backend.BeginFrame(MatrixD.Identity, Vector3.Zero, Vector3.Zero, Vector3.One, Color.Black), "frame opens for the off-owner call");
        Exception offOwner = null;
        var other = new Thread(() => { try { backend.UploadTexture(3, 1, 1, 0, new byte[] { 0, 255, 0, 255 }, false); } catch (Exception e) { offOwner = e; } });
        other.Start();
        bool returned = other.Join(300);
        if (gate)
        {
            Check(!returned, "frame gate holds an off-owner call while the frame is open");
            backend.EndFrame(); other.Join();
            Check(offOwner == null, "gated off-owner upload completes after the frame closes");
            backend.RemoveResource(0, 3);
        }
        else
        {
            Check(returned && offOwner != null && offOwner.Message.Contains("precede begin_frame"), "ungated off-owner upload fails with its own thread's error");
            backend.EndFrame();
        }
        Console.WriteLine(checks + " managed/native checks passed on " + probe.DeviceName);
        return 0;
    }
}
