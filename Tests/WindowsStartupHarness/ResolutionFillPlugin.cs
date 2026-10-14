using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Sandbox;
using Sandbox.Engine.Utils;
using Sandbox.Game.Screens;
using Sandbox.Graphics.GUI;
using VRage.FileSystem;
using VRage.Plugins;
using VRageRender;

namespace WindowsStartupHarness;

// This observation plugin has no compile-time Vulkan dependency, so it works
// with both the released DLL and Pulsar's dynamically named source assemblies.
public sealed class ResolutionFillPlugin : IPlugin
{
    private readonly Stopwatch elapsed = new();
    private ResolutionFill fill;
    private string directory;
    private int duration;
    private double nextReport;
    private bool exiting;
    public void Init(object gameInstance)
    {
        var userData = Path.GetFullPath(Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_USER_DATA"));
        if (userData != Path.GetFullPath(MyFileSystem.UserDataPath) || !File.Exists(Path.Combine(userData, ".se-vulkan-startup")))
            throw new InvalidOperationException("Resolution fill requires guarded private game data.");
        directory = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_REPORT_DIR");
        duration = int.Parse(Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_DURATION_SECONDS"));
        fill = new ResolutionFill(directory);
        MySandboxGame.Config.WelcomScreenCurrentStatus = MyConfig.WelcomeScreenStatus.AlreadySeen;
        MySandboxGame.Config.GDPRConsent = false; MySandboxGame.Config.Save();
        File.WriteAllText(Path.Combine(directory, "harness-started.txt"), DateTime.UtcNow.ToString("u"));
        File.WriteAllText(Path.Combine(directory, "active-backend.txt"), MyRenderProxy.RendererInterfaceName());
        elapsed.Start();
    }
    private void Report()
    {
        var diagnostics = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ClientPlugin.Diagnostics.VulkanDiagnostics")).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException("Vulkan diagnostics are unavailable.");
        var report = (string)diagnostics.GetMethod("Report", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        File.WriteAllText(Path.Combine(directory, "status-latest.txt"), report);
    }
    public void Update()
    {
        MyScreenManager.GetFirstScreenOfType<MyGuiScreenWelcomeScreen>()?.CloseScreenNow();
        MyScreenManager.GetFirstScreenOfType<MyGuiScreenGDPR>()?.CloseScreenNow();
        fill.Update(elapsed.Elapsed.TotalSeconds);
        if (elapsed.Elapsed.TotalSeconds >= nextReport) { Report(); nextReport += 10; }
        if (!exiting && elapsed.Elapsed.TotalSeconds >= duration)
        {
            if (!fill.Complete) throw new InvalidOperationException("Resolution fill did not finish.");
            exiting = true; Report(); MySandboxGame.ExitThreadSafe();
        }
    }
    public void Dispose() { if (directory != null) Report(); }
}
