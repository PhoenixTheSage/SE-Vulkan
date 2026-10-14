using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientPlugin.Bootstrap;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace WindowsStartupHarness;

internal sealed class ConfigControls
{
    private readonly string directory;
    private int stage;
    private double next = 10;
    private MyGuiScreenBase config, status;
    internal bool Complete { get; private set; }
    internal ConfigControls(string directory) => this.directory = directory;
    internal void Update(double now)
    {
        if (Complete || now < next) return;
        next = now + 2;
        switch (stage++)
        {
            case 0: global::ClientPlugin.Plugin.Instance.OpenConfigDialog(); break;
            case 1:
                config = MyScreenManager.Screens.Single(screen => screen.GetType().FullName == "ClientPlugin.Settings.SettingsScreen");
                Require(Buttons(config).Any(button => button.Text == "Show Status") && Buttons(config).Any(button => button.Text == "Write report to log"), "Actual Pulsar config exposes both status controls.");
                Capture("config-controls-native.png"); break;
            case 2:
                string marker = "SE_VULKAN_CONFIG_BUTTON_" + Guid.NewGuid().ToString("N");
                MyLog.Default.WriteLineAndConsole(marker + " BEGIN");
                Button(config, "Write report to log").PressButton();
                MyLog.Default.WriteLineAndConsole(marker + " END"); MyLog.Default.Flush();
                string log;
                using (var stream = new FileStream(MyLog.Default.GetFilePath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream)) log = reader.ReadToEnd();
                int begin = log.LastIndexOf(marker + " BEGIN", StringComparison.Ordinal), end = log.LastIndexOf(marker + " END", StringComparison.Ordinal);
                Require(begin >= 0 && end > begin && log.Substring(begin, end - begin).Contains("Vulkan 1.4 renderer status"), "Write report button emitted its snapshot between isolated log markers.");
                long generation = BackendRuntime.Configuration.RetryGeneration;
                Button(config, "Retry Vulkan next launch").PressButton();
                string attempt = Path.Combine(Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_USER_DATA"), "Storage", "Vulkan.cfg.startup");
                Require(!BackendRuntime.RecoveryRequired && BackendRuntime.Configuration.RetryGeneration == generation && File.Exists(attempt), "Healthy Retry button preserves the active-session journal and retry generation.");
                Button(config, "Show Status").PressButton(); break;
            case 3:
                status = MyScreenManager.Screens.Single(screen => screen.GetFriendlyName() == "SEVulkanStatus");
                var body = status.Controls.OfType<MyGuiControlMultilineText>().Single();
                Require(body.Text.ToString().Contains("Requested:") && body.ScrollbarV.Visible && body.ScrollbarV.MaxSize > body.ScrollbarV.PageSize, "Show Status reached a populated, vertically scrollable report.");
                Capture("config-status-native.png"); break;
            case 4:
                var scrolled = status.Controls.OfType<MyGuiControlMultilineText>().Single();
                scrolled.ScrollbarV.Value = scrolled.ScrollbarV.MaxSize - scrolled.ScrollbarV.PageSize;
                Require(scrolled.ScrollbarV.Value > 0, "Report scroll reaches remaining coverage and recent failures."); break;
            case 5:
                Capture("config-status-scrolled-native.png"); break;
            case 6:
                status.CloseScreenNow(); config.CloseScreenNow();
                File.WriteAllText(Path.Combine(directory, "config-controls-passed.txt"), "Real config Show Status, report logging, scroll, and healthy Retry journal protection passed.");
                Complete = true; break;
        }
    }
    private static IEnumerable<MyGuiControlButton> Buttons(MyGuiScreenBase screen) => screen.Controls.SelectMany(Flatten).OfType<MyGuiControlButton>();
    private static IEnumerable<MyGuiControlBase> Flatten(MyGuiControlBase control)
    {
        yield return control;
        if (control is MyGuiControlScrollablePanel panel)
            foreach (var child in Flatten(panel.ScrolledControl)) yield return child;
        else if (control is MyGuiControlParent parent)
            foreach (var item in parent.Controls)
                foreach (var child in Flatten(item)) yield return child;
    }
    private static MyGuiControlButton Button(MyGuiScreenBase screen, string text) => Buttons(screen).Single(button => button.Text == text);
    private void Capture(string name) => MyRenderProxy.TakeScreenshot(Vector2.One, Path.Combine(directory, name), debug: false, ignoreSprites: false, showNotification: false);
    private void Require(bool success, string evidence)
    {
        if (!success) throw new InvalidOperationException("Config control verification failed: " + evidence);
        File.AppendAllText(Path.Combine(directory, "config-controls.txt"), DateTime.UtcNow.ToString("u") + " PASS " + evidence + "\n");
    }
}
