using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClientPlugin.Bootstrap;
using Sandbox.Engine.Platform.VideoMode;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRageRender;

namespace WindowsStartupHarness;

// Uses the actual game screen and callbacks in private smoke-test user data.
// No production patch entry point or test substitute is invoked here.
internal sealed class DisplayTransactions
{
    private readonly string directory;
    private readonly MyRenderDeviceSettings original;
    private MyGuiScreenOptionsDisplay screen;
    private MyGuiScreenMessageBox confirmation;
    private int stage;
    private double nextStep;
    private readonly bool testTimeout;
    internal bool Complete { get; private set; }

    internal DisplayTransactions(string directory, double now)
    { this.directory = directory; original = MyVideoSettingsManager.CurrentDeviceSettings; nextStep = now; testTimeout = Environment.GetEnvironmentVariable("SE_VULKAN_STARTUP_RESOLUTION_TIMEOUT") == "1"; }

    internal void Update(double now)
    {
        if (Complete || now < nextStep) return;
        nextStep = now + 2;
        switch (stage++)
        {
            case 0:
                screen = Open(RendererBackend.Dx11);
                screen.OnOkClick(null);
                Require(BackendRuntime.RequestedBackend == RendererBackend.Dx11 && BackendRuntime.ActiveBackend == RendererBackend.Vulkan, "Renderer-only OK commits DX11 for the next launch while Vulkan remains active.");
                break;
            case 1:
                screen = Open(RendererBackend.Vulkan);
                ChangeResolution(screen);
                screen.OnOkClick(null);
                Require(Waiting(screen) && BackendRuntime.RequestedBackend == RendererBackend.Dx11, "Changed resolution keeps the Vulkan draft uncommitted while confirmation is pending.");
                break;
            case 2:
                DismissRestartPrompts();
                confirmation = LiveConfirmation();
                Require(confirmation != null, "The real resolution confirmation screen was displayed.");
                screen.OnMessageBoxCallback(MyGuiScreenMessageBox.ResultEnum.NO);
                confirmation.CloseScreenNow(); confirmation = null;
                Require(BackendRuntime.RequestedBackend == RendererBackend.Dx11, "Resolution rejection discards the Vulkan draft.");
                break;
            case 3:
                var reverted = MyVideoSettingsManager.CurrentDeviceSettings;
                Require(reverted.BackBufferWidth == original.BackBufferWidth && reverted.BackBufferHeight == original.BackBufferHeight, "The game reverted the rejected native resolution.");
                screen.CloseScreenNow();
                screen = Open(RendererBackend.Vulkan);
                ChangeResolution(screen);
                screen.OnOkClick(null);
                Require(Waiting(screen) && BackendRuntime.RequestedBackend == RendererBackend.Dx11, "The accepted-resolution test also waits before committing its Vulkan draft.");
                break;
            case 4:
                DismissRestartPrompts();
                confirmation = LiveConfirmation();
                Require(confirmation != null, "The second real resolution confirmation screen was displayed.");
                screen.OnMessageBoxCallback(MyGuiScreenMessageBox.ResultEnum.YES);
                confirmation.CloseScreenNow(); confirmation = null;
                Require(BackendRuntime.RequestedBackend == RendererBackend.Vulkan && BackendRuntime.ActiveBackend == RendererBackend.Vulkan, "Accepted resolution commits Vulkan through the screen callback.");
                break;
            case 5:
                screen = Open(RendererBackend.Vulkan);
                var resolution = screen.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "Resolution");
                long key = ((long)original.BackBufferWidth << 32) | (uint)original.BackBufferHeight;
                Require(resolution.TryGetItemByKey(key) != null, "Original native resolution remains selectable.");
                resolution.SelectItemByKey(key);
                screen.OnOkClick(null);
                Require(Waiting(screen), "Restoring the private test window follows the real confirmation path.");
                break;
            case 6:
                DismissRestartPrompts();
                confirmation = LiveConfirmation();
                Require(confirmation != null, "The restore confirmation screen was displayed.");
                screen.OnMessageBoxCallback(MyGuiScreenMessageBox.ResultEnum.YES);
                confirmation.CloseScreenNow(); confirmation = null;
                screen = Open(RendererBackend.Dx11);
                screen.OnOkClick(null);
                Require(BackendRuntime.RequestedBackend == RendererBackend.Dx11 && BackendRuntime.RestartRequired, "The private profile is left requesting DX11 for a subsequent restart test.");
                File.WriteAllText(Path.Combine(directory, "display-transactions-passed.txt"), "Real renderer-only OK, native resolution rejection/revert, accepted confirmation, and window restoration passed. Requested DX11; active Vulkan.");
                Complete = !testTimeout;
                break;
            case 7:
                screen = Open(RendererBackend.Vulkan);
                ChangeResolution(screen); screen.OnOkClick(null);
                Require(Waiting(screen) && BackendRuntime.RequestedBackend == RendererBackend.Dx11, "The actual 60-second timeout begins with an uncommitted Vulkan draft.");
                nextStep = now + 65;
                break;
            case 8:
                DismissRestartPrompts();
                var timedOut = MyVideoSettingsManager.CurrentDeviceSettings;
                Require(!Waiting(screen) && BackendRuntime.RequestedBackend == RendererBackend.Dx11 &&
                    timedOut.BackBufferWidth == original.BackBufferWidth && timedOut.BackBufferHeight == original.BackBufferHeight,
                    "The real confirmation timeout discards the renderer draft and reverts the native resolution.");
                screen.CloseScreenNow();
                File.WriteAllText(Path.Combine(directory, "display-timeout-passed.txt"), "The real game's 60-second resolution confirmation expired, reverted the window, and retained requested DX11.");
                Complete = true;
                break;
        }
    }

    private MyGuiScreenOptionsDisplay Open(RendererBackend renderer)
    {
        var display = new MyGuiScreenOptionsDisplay(); MyGuiSandbox.AddScreen(display);
        display.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "VulkanRendererSelector").SelectItemByKey((long)renderer);
        return display;
    }
    private static bool Waiting(MyGuiScreenOptionsDisplay display) => (bool)typeof(MyGuiScreenOptionsDisplay).GetField("m_waitingForConfirmation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(display);
    private static void DismissRestartPrompts()
    {
        // CloseScreenNow marks a screen closed; removal occurs on a later GUI
        // update. Re-querying the first screen in a while loop spins forever.
        foreach (var box in MyScreenManager.Screens.OfType<MyGuiScreenMessageBox>().ToArray())
        {
            var text = typeof(MyGuiScreenMessageBox).GetField("m_messageText", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box) as StringBuilder;
            if (box.State == MyGuiScreenState.CLOSED || text == null || text.ToString().IndexOf("Restart the game", StringComparison.Ordinal) < 0) continue;
            box.CloseScreenNow();
        }
    }
    private static MyGuiScreenMessageBox LiveConfirmation() => MyScreenManager.Screens.OfType<MyGuiScreenMessageBox>().LastOrDefault(box => box.State != MyGuiScreenState.CLOSED);
    private static void ChangeResolution(MyGuiScreenOptionsDisplay display)
    {
        var resolution = display.Controls.OfType<MyGuiControlCombobox>().Single(control => control.Name == "Resolution");
        long current = resolution.GetSelectedKey();
        for (int index = 0; index < resolution.GetItemsCount(); index++)
        {
            long key = resolution.GetItemByIndex(index).Key;
            if (key == current) continue;
            resolution.SelectItemByKey(key); return;
        }
        throw new InvalidOperationException("No alternate native resolution is available for this private Display test.");
    }
    private void Require(bool value, string evidence)
    {
        if (!value) throw new InvalidOperationException("Display transaction failed: " + evidence);
        File.AppendAllText(Path.Combine(directory, "display-transactions.txt"), DateTime.UtcNow.ToString("u") + " PASS " + evidence + "\n");
    }
}
