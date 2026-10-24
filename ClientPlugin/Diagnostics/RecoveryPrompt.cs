using System.Text;
using ClientPlugin.Bootstrap;
using ClientPlugin.Settings;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Utils;

namespace ClientPlugin.Diagnostics;

// Raised once in the main menu after the second unclean Vulkan exit in a row.
internal static class RecoveryPrompt
{
    private static bool shown;
    public static void TryShow()
    {
        if (shown || !BackendRuntime.RecoveryPromptPending || MySession.Static != null) return;
        if (MyScreenManager.GetFirstScreenOfType<MyGuiScreenMainMenuBase>() == null) return;
        shown = true;
        if (BackendRuntime.ActiveBackend == RendererBackend.Vulkan) AskKeepVulkan();
        else AskRetryVulkan();
    }
    // A counted exit was traced to the renderer, so this launch already runs DX11.
    private static void AskRetryVulkan()
    {
        Show("Vulkan stopped unexpectedly in the last two sessions, so this launch uses DX11.\n\n" + BackendRuntime.Configuration.LastFailure
            + "\n\nVulkan is tried again automatically after a graphics driver or plugin update.", "Try Vulkan again", "Stay on DX11",
            tryAgain =>
            {
                if (!tryAgain) { BackendRuntime.DismissRecoveryPrompt(false); return; }
                ConfigStorage.Flush(); BackendRuntime.RetryVulkan(); Config.Current.ReplaceValues(BackendRuntime.Configuration);
                AskRestart("Vulkan");
            });
    }
    // The crashes were not traced to Vulkan, so it stays active unless the player chooses otherwise.
    private static void AskKeepVulkan()
    {
        Show("The game closed unexpectedly in the last two Vulkan sessions. The crash was not traced to the Vulkan renderer, so Vulkan is still active.\n\n"
            + BackendRuntime.Configuration.LastFailure, "Keep Vulkan", "Use DX11",
            keep =>
            {
                if (keep) { BackendRuntime.DismissRecoveryPrompt(true); return; }
                BackendRuntime.FallBackNextLaunch(); Config.Current.ReplaceValues(BackendRuntime.Configuration);
                AskRestart("DX11");
            });
    }
    private static void Show(string text, string yes, string no, System.Action<bool> answer) =>
        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
            messageText: new StringBuilder(text), messageCaption: new StringBuilder("Vulkan renderer"),
            yesButtonText: MyStringId.GetOrCompute(yes), noButtonText: MyStringId.GetOrCompute(no),
            callback: result => answer(result == MyGuiScreenMessageBox.ResultEnum.YES)));
    private static void AskRestart(string renderer) =>
        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
            messageText: new StringBuilder(renderer + " is used from the next launch. Restart the game now?"), messageCaption: new StringBuilder("Restart required"),
            callback: result => { if (result == MyGuiScreenMessageBox.ResultEnum.YES) MySessionLoader.ExitGame(); }));
}
