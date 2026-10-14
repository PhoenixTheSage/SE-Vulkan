using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using Sandbox;
using Sandbox.Engine.Utils;
using Sandbox.Game.Gui;
using Sandbox.Game;
using Sandbox.Game.Screens;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace WindowsStartupHarness;

// Test-only plugin, used solely in an isolated profile. It intentionally has no
// VRage.Render11/SharpDX references and cannot be packaged with the client plugin.
public sealed class Plugin : IPlugin
{
    private readonly Stopwatch elapsed = new Stopwatch();
    private string directory;
    private double nextReport;
    private bool captured;
    private bool exitRequested;
    private MyGuiScreenOptionsDisplay display;
    private bool displayCaptured;
    private bool displayCancelled;
    private RendererBackend requestedBeforeDraft;
    private DisplayTransactions transactions;
    private bool testTransactions;
    private bool commitRestart;
    private bool restartCommitted;
    private ConfigControls configControls;
    private int duration = 90;
    private MediaChecks mediaChecks;

    public void Init(object gameInstance)
    {
        directory = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_REPORT_DIR");
        if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("This test-only plugin requires an isolated report directory.");
        string userData = Path.GetFullPath(Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_USER_DATA") ?? throw new InvalidOperationException("Private user-data guard is required."));
        if (!string.Equals(userData, Path.GetFullPath(MyFileSystem.UserDataPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(userData, ".se-vulkan-startup")) || !string.Equals(File.ReadAllText(Path.Combine(userData, ".se-vulkan-startup")).Trim(), userData, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The private startup user-data marker does not match the running game.");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        MySandboxGame.Config.WelcomScreenCurrentStatus = MyConfig.WelcomeScreenStatus.AlreadySeen;
        MySandboxGame.Config.GDPRConsent = false;
        MySandboxGame.Config.Save();
        MyVRage.Platform.CrashReporting.ExitingProcessOnCrash += error => File.WriteAllText(Path.Combine(directory, "process-crash.txt"), error?.ToString() ?? "Native crash");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => File.WriteAllText(Path.Combine(directory, "unhandled-exception.txt"), args.ExceptionObject?.ToString() ?? "Unknown exception");
        AppDomain.CurrentDomain.ProcessExit += (_, __) => File.WriteAllText(Path.Combine(directory, "process-exit.txt"), Environment.ExitCode.ToString());
        int configured;
        testTransactions = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_DISPLAY_TRANSACTIONS") == "1";
        if (Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_RESOLUTION_TIMEOUT") == "1") configControls = new ConfigControls(directory);
        commitRestart = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_COMMIT_RESTART") == "1";
        if (Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_MEDIA") == "1") {
            mediaChecks = new MediaChecks(directory);
            // Pulsar's normal fast startup disables logos. Exercise the real
            // stock KSH screen in this private media acceptance profile.
            MyPlatformGameSettings.ENABLE_LOGOS = true;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_DURATION_SECONDS"), out configured) && configured >= 30 && configured <= 900) duration = configured;
        File.WriteAllText(Path.Combine(directory, "harness-started.txt"), DateTime.UtcNow.ToString("u"));
        File.WriteAllText(Path.Combine(directory, "active-backend.txt"), "Requested=" + BackendRuntime.RequestedBackend + "; active=" + BackendRuntime.ActiveBackend + "; render interface=" + MyRenderProxy.RendererInterfaceName());
        elapsed.Start();
        Report("Initialization");
    }

    public void Update()
    {
        mediaChecks?.Update(elapsed.Elapsed.TotalSeconds);
        // The fresh-user welcome may already be queued before IPlugin.Init.
        // Close only that known test screen and preserve its isolated setting.
        var welcome = MyScreenManager.GetFirstScreenOfType<MyGuiScreenWelcomeScreen>();
        if (welcome != null) welcome.CloseScreenNow();
        // Closing this isolated consent screen uses the base game's opt-out
        // path. The harness never grants consent or touches normal user data.
        var consent = MyScreenManager.GetFirstScreenOfType<MyGuiScreenGDPR>();
        if (consent != null) consent.CloseScreenNow();
        configControls?.Update(elapsed.Elapsed.TotalSeconds);
        if (elapsed.Elapsed.TotalSeconds >= nextReport) { nextReport += 10; Report("Update at " + elapsed.Elapsed.TotalSeconds.ToString("F1") + " seconds"); }
        double captureAt = testTransactions ? Math.Min(30, duration - 40) : Math.Min(60, duration - 15);
        if(mediaChecks != null && Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_MEDIA_SKIP") != "1") captureAt=Math.Max(captureAt,85);
        if (!captured && elapsed.Elapsed.TotalSeconds >= captureAt)
        {
            captured = true;
            {
                string output = Path.Combine(directory, BackendRuntime.ActiveBackend == RendererBackend.Vulkan ? "menu-native.png" : "menu-dx11.png");
                MyRenderProxy.TakeScreenshot(Vector2.One, output, debug: false, ignoreSprites: false, showNotification: false);
                File.WriteAllText(Path.Combine(directory, "capture-requested.txt"), output);
            }
        }
        if (captured && display == null && elapsed.Elapsed.TotalSeconds >= captureAt + 2)
        {
            requestedBeforeDraft = BackendRuntime.RequestedBackend;
            display = new MyGuiScreenOptionsDisplay();
            MyGuiSandbox.AddScreen(display);
            var selector = display.Controls.OfType<MyGuiControlCombobox>().Single(c => c.Name == "VulkanRendererSelector");
            var draft = BackendRuntime.ActiveBackend == RendererBackend.Vulkan ? RendererBackend.Dx11 : RendererBackend.Vulkan;
            selector.SelectItemByKey((long)draft);
            File.WriteAllText(Path.Combine(directory, "display-draft.txt"), "Draft=" + draft + "; requested=" + BackendRuntime.RequestedBackend + "; active=" + BackendRuntime.ActiveBackend);
        }
        if (display != null && !displayCaptured && elapsed.Elapsed.TotalSeconds >= captureAt + 6)
        {
            displayCaptured = true;
            MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(directory, BackendRuntime.ActiveBackend == RendererBackend.Vulkan ? "display-draft-native.png" : "display-draft-dx11.png"), debug: false, ignoreSprites: false, showNotification: false);
        }
        if (display != null && !displayCancelled && elapsed.Elapsed.TotalSeconds >= captureAt + 10)
        {
            displayCancelled = true;
            display.CloseScreenNow();
            if (BackendRuntime.RequestedBackend != requestedBeforeDraft) throw new InvalidOperationException("Cancel committed the Display renderer draft.");
            File.WriteAllText(Path.Combine(directory, "display-cancel-passed.txt"), "Requested renderer unchanged after cancelling the alternate backend draft.");
        }
        if (testTransactions && displayCancelled && elapsed.Elapsed.TotalSeconds >= captureAt + 12)
        {
            if (transactions == null) transactions = new DisplayTransactions(directory, elapsed.Elapsed.TotalSeconds);
            transactions.Update(elapsed.Elapsed.TotalSeconds);
        }
        if (commitRestart && displayCancelled && !restartCommitted && elapsed.Elapsed.TotalSeconds >= captureAt + 12)
        {
            restartCommitted = true;
            var requested = BackendRuntime.ActiveBackend == RendererBackend.Vulkan ? RendererBackend.Dx11 : RendererBackend.Vulkan;
            var restartDisplay = new MyGuiScreenOptionsDisplay(); MyGuiSandbox.AddScreen(restartDisplay);
            restartDisplay.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "VulkanRendererSelector").SelectItemByKey((long)requested);
            restartDisplay.OnOkClick(null);
            if (BackendRuntime.RequestedBackend != requested || !BackendRuntime.RestartRequired) throw new InvalidOperationException("Renderer-only OK did not commit the private restart choice.");
            File.WriteAllText(Path.Combine(directory, "restart-choice-committed.txt"), "Requested=" + requested + "; active=" + BackendRuntime.ActiveBackend);
        }
        if (!exitRequested && elapsed.Elapsed.TotalSeconds >= duration)
        {
            if (testTransactions && (transactions == null || !transactions.Complete)) throw new InvalidOperationException("The private Display transaction sequence did not finish before its deadline.");
            if (configControls != null && !configControls.Complete) throw new InvalidOperationException("The private config control sequence did not complete.");
            // Preserve orderly renderer/worker teardown even when a media
            // acceptance assertion fails. The runner still rejects the run.
            try { mediaChecks?.Complete(elapsed.Elapsed.TotalSeconds); }
            catch (Exception error) { File.WriteAllText(Path.Combine(directory, "media-failed.txt"), error.ToString()); }
            exitRequested = true;
            Report("Controlled exit requested");
            File.WriteAllText(Path.Combine(directory, "exit-requested.txt"), DateTime.UtcNow.ToString("u"));
            MySandboxGame.ExitThreadSafe();
        }
    }

    private void Report(string context)
    {
        string report = "WindowsStartupHarness: " + context + "; render interface: " + MyRenderProxy.RendererInterfaceName() + "\n" + VulkanDiagnostics.Report();
        File.WriteAllText(Path.Combine(directory, "status-latest.txt"), report);
        File.AppendAllText(Path.Combine(directory, "status-history.txt"), report + "\n\n");
        MyLog.Default?.WriteLineAndConsole(report);
    }

    public void Dispose()
    {
        if (directory != null) { Report("Harness disposed"); File.WriteAllText(Path.Combine(directory, "harness-disposed.txt"), DateTime.UtcNow.ToString("u")); }
    }
}
