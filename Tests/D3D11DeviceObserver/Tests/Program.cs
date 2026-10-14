using System;
using System.IO;
using System.Linq;
using System.Reflection;
using D3D11DeviceObserver;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); ++checks; Console.WriteLine("PASS " + detail); }
    private static void Main(string[] args)
    {
        string bin = Path.GetFullPath(args[0]);
        string reports = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(reports);
        Environment.SetEnvironmentVariable("SE_VULKAN_D3D11_REPORT_DIR", reports);
        Observer.Initialize(true);
        byte[] patched;
        using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(bin, "SharpDX.Direct3D11.dll")))
        {
            Preloader.Patch(assembly);
            Check(Observer.PatchedCallSites == 2, "installed native inventory contains two creation import call sites");
            var wrappers = assembly.MainModule.GetType("SharpDX.Direct3D11.D3D11").Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == typeof(Observer).FullName && r.Name == "Attempt")).ToArray();
            Check(wrappers.Length == 2, "both CreateDevice and On12CreateDevice wrappers instrumented");
            Check(wrappers.All(m => m.Body.Instructions.Count(i => i.Operand is MethodReference r && r.DeclaringType.FullName == typeof(Observer).FullName && r.Name == "Result") == 1), "each native HRESULT is observed without changing its value");
            Check(!assembly.MainModule.AssemblyReferences.Any(r => r.Name == "VRage.Render11"), "patched SharpDX adds no DX11 renderer reference");
            using (var stream = new MemoryStream()) { assembly.Write(stream); patched = stream.ToArray(); }
            using (var check = AssemblyDefinition.ReadAssembly(new MemoryStream(patched))) Check(check.MainModule.GetType("SharpDX.Direct3D11.SE_Vulkan_D3D11_Observer_1") != null, "patched assembly writes and round-trips observer stamp");
        }
        using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(bin, "SpaceEngineers.exe")))
        {
            var method = game.MainModule.GetType("SpaceEngineers.MyProgram").Methods.Single(m => m.Name == "InitializeRender");
            string before = Preloader.Fingerprint(method); Preloader.Patch(game);
            Check(before == Preloader.Fingerprint(method), "renderer factory fingerprint remains unchanged");
            Check(game.MainModule.GetType("SpaceEngineers.MyProgram").Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Instruction>()).Count(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "before-render-initialization") == 1, "early renderer milestone inserted into caller");
            using (var stream = new MemoryStream()) { game.Write(stream); Check(stream.Length > 0, "game milestone assembly writes valid Cecil image"); }
        }
        Observer.Milestone("before-render-initialization");
        AppDomain.CurrentDomain.AssemblyResolve += (_, args2) =>
        {
            string path = Path.Combine(bin, new AssemblyName(args2.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var live = Assembly.Load(patched);
        Check(live.GetType("SharpDX.Direct3D11.SE_Vulkan_D3D11_Observer_1") != null, "CLR loads patched installed SharpDX bytes");
        var baseSharpDx = Assembly.LoadFrom(Path.Combine(bin, "SharpDX.dll"));
        var driverType = baseSharpDx.GetType("SharpDX.Direct3D.DriverType", true);
        var flagsType = live.GetType("SharpDX.Direct3D11.DeviceCreationFlags", true);
        var deviceType = live.GetType("SharpDX.Direct3D11.Device", true);
        var constructor = deviceType.GetConstructor(new[] { driverType, flagsType });
        Check(constructor != null, "installed two-parameter Device constructor identified");
        // CPU WARP device is a deliberate positive control outside the game/profile.
        var device = constructor.Invoke(new[] { Enum.Parse(driverType, "Warp"), Enum.ToObject(flagsType, 0) });
        try { Check(Observer.AttemptCount == 1 && Observer.SuccessCount == 1, "real WARP device creation produces one successful observed native call"); }
        finally { ((IDisposable)device).Dispose(); }
        Observer.Milestone("positive-control-complete");
        string json = File.ReadAllText(Path.Combine(reports, "d3d11-observer.json"));
        Check(json.Contains("\"readyBeforeRenderInitialization\":true") && json.Contains("\"loadedAssemblyStamped\":true") && json.Contains("\"lastError\":\"\""), "report proves early readiness and loaded stamp without observer errors");
        Check(!typeof(Observer).Assembly.GetReferencedAssemblies().Any(a => a.Name == "VRage.Render11" || a.Name.StartsWith("SharpDX")), "observer plugin is compatible with Vulkan enabled-plugin checks");
        Console.WriteLine(checks + " checks PASS; report=" + reports);
    }
}
