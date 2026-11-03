using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using VRage;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    string ReflexWindowState()
    {
        var window = MyVRage.Platform.Windows.Window;
        if (window == null) return "missing";
        var property = window.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? window.GetType().GetProperty("WindowHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        IntPtr handle = property == null ? IntPtr.Zero : (IntPtr)property.GetValue(window, null);
        return "valid=" + (handle != IntPtr.Zero) + ", visible=" + (handle != IntPtr.Zero && IsWindowVisible(handle)) +
            ", minimized=" + (handle != IntPtr.Zero && IsIconic(handle)) + ", foreground=" + (handle != IntPtr.Zero && GetForegroundWindow() == handle);
    }
    double nextReflexReport, reflexWorldStarted = -1;
    int reflexMode = -1;
    Type ReflexType(string assembly, string name) => AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == assembly)?.GetType(name, false);
    void ObserveReflex()
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_FIXTURE") != "1" || clock.Elapsed.TotalSeconds < nextReflexReport) return;
        nextReflexReport = clock.Elapsed.TotalSeconds + 1;
        var config = ReflexType("Reflex", "ClientPlugin.Config");
        var service = ReflexType("Anomaly", "ClientPlugin.Backend.ReflexService");
        if (spawned && reflexWorldStarted < 0) reflexWorldStarted = clock.Elapsed.TotalSeconds;
        double world = reflexWorldStarted < 0 ? -1 : clock.Elapsed.TotalSeconds - reflexWorldStarted;
        if (world >= 0 && expectedBackend == ClientPlugin.Bootstrap.RendererBackend.Vulkan)
        {
            var validation = System.Text.RegularExpressions.Regex.Match(ClientPlugin.Diagnostics.VulkanDiagnostics.Report(),
                @"validation available: \d+; enabled: (\d+); errors: (\d+)");
            if (!validation.Success || validation.Groups[1].Value != "1" || validation.Groups[2].Value != "0")
                throw new InvalidOperationException("Reflex Vulkan fixture requires active validation with zero errors; inspect stderr and status history.");
        }
        int mode = world >= 20 && world < 30 ? 0 : world >= 10 && world < 20 || world >= 40 && world < 50 ? 2 : 1;
        if (config != null && !reflexTimingMode && !reflexOverlayMode && reflexUiPhase == 0 && reflexFailurePhase == 0 && reflexMode != mode)
        {
            var current = config.GetField("Current").GetValue(null);var property = config.GetProperty("Mode");
            property.SetValue(current, Enum.ToObject(property.PropertyType, mode));reflexMode = mode;
        }
        Func<string, object> read = name => service?.GetProperty(name)?.GetValue(null);
        ExerciseReflexSettings(world, config, read);
        ExerciseReflexFailure(world, config, read);
        if (config != null) reflexMode = Convert.ToInt32(config.GetProperty("Mode").GetValue(config.GetField("Current").GetValue(null), null));
        string report = DateTime.UtcNow.ToString("u") + " worldSeconds=" + world.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
            " activeBackend=" + ClientPlugin.Bootstrap.BackendRuntime.ActiveBackend +
            " configVsync=" + Sandbox.MySandboxGame.Config.VerticalSync + " mode=" + reflexMode + " requested=" + read("RequestedMode") + " available=" + read("Available") +
            " configured=" + read("ConfiguredMode") + " applied=" + read("AppliedMode") + "\n" + read("Status");
        var dx = ReflexType("Anomaly.Dx11", "ClientPlugin.ShaderFramework.Reflex.ReflexDx11Bridge");
        var vk = ReflexType("Vulkan", "ClientPlugin.Rendering.GuestBridge");
        object[] binding = dx?.GetMethod("Snapshot")?.Invoke(null, null) as object[] ?? vk?.GetMethod("ReflexBindingState")?.Invoke(null, null) as object[];
        ExerciseReflexOwner(world, binding, read);
        ExerciseReflexMinimize(world, binding, read);
        ExerciseReflexResize(world, binding, read);
        report += "\nwindow=" + ReflexWindowState();
        report += "\nbinding=" + (binding == null ? "missing" : string.Join(",", binding.Select(v => v?.ToString() ?? "null")));
        object[] driver = dx?.GetMethod("DriverStatus")?.Invoke(null, null) as object[];
        report += "\ndriver=" + (driver == null ? "missing" : string.Join(",", driver.Select(v => v?.ToString() ?? "null")));
        object[] timings = dx?.GetMethod("LatencyStatus")?.Invoke(null, null) as object[] ?? vk?.GetMethod("ReflexLatencyStatus")?.Invoke(null, null) as object[];
        report += "\nlatency=" + (timings == null ? "missing" : string.Join(",", timings.Select(v => v?.ToString() ?? "null")));
        var fgRuntime = ReflexType("SpaceEngineersFrameGen.Dx11", "ClientPlugin.FrameGen.FrameGenRuntime");
        var fgHost = ReflexType("SpaceEngineersFrameGen.Dx11", "ClientPlugin.FrameGen.FrameGenHost");
        if (dx == null)
        {
            var fgVulkan = ReflexType("SpaceEngineersFrameGen", "ClientPlugin.FrameGen.FrameGenVulkan");
            report += "\nFGnative=" + fgVulkan?.GetMethod("StatusText")?.Invoke(null, null);
        }
        report += "\nFGpath=" + fgRuntime?.GetProperty("LastPath")?.GetValue(null) + "; error=" + fgHost?.GetProperty("LastError")?.GetValue(null);
        var probe = ReflexType("Reflex", "ClientPlugin.FrameGenerationProbe");
        report += "\nFG=" + probe?.GetProperty("Status", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) + "\n";
        File.WriteAllText(Path.Combine(reports, "reflex-latest.txt"), report);
        File.AppendAllText(Path.Combine(reports, "reflex-history.txt"), report + "\n");
    }
}
