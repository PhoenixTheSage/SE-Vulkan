using System;
using System.Runtime.CompilerServices;
using System.Text;
using ClientPlugin.Bootstrap;
using ClientPlugin.Settings;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Patches;

internal static class DisplayRendererSelector
{
    private sealed class ScreenState
    {
        internal DisplaySelectionTransaction Transaction;
        internal MyGuiControlCombobox Combo;
        internal MyGuiControlLabel Notice;
    }
    private static readonly ConditionalWeakTable<MyGuiScreenOptionsDisplay, ScreenState> Screens = new ConditionalWeakTable<MyGuiScreenOptionsDisplay, ScreenState>();
    private static ScreenState State(MyGuiScreenOptionsDisplay screen) => Screens.GetValue(screen, _ => new ScreenState { Transaction = new DisplaySelectionTransaction(BackendRuntime.RequestedBackend) });
    internal static void AddControls(MyGuiScreenOptionsDisplay screen, bool constructor)
    {
        if (!constructor) return;
        var state = State(screen);
        // Expand the stock panel, retaining its control grouping and keeping the renderer above the adapter row.
        var adapter = AccessTools.Field(typeof(MyGuiScreenOptionsDisplay), "m_comboVideoAdapter").GetValue(screen) as MyGuiControlCombobox;
        if (adapter == null) return;
        screen.Size = new Vector2(screen.Size.Value.X, screen.Size.Value.Y + 0.075f);
        bool topSeparatorMoved = false;
        foreach (var control in screen.Controls)
        {
            control.Position += new Vector2(0, 0.032f);
            if (!topSeparatorMoved && control is MyGuiControlSeparatorList)
            { control.Position -= new Vector2(0, 0.0695f); topSeparatorMoved = true; }
        }
        foreach (var element in screen.Elements)
            if (element.Name == "CaptionLabel") element.Position -= new Vector2(0, 0.0375f);
        var combo = new MyGuiControlCombobox(size: adapter.Size, toolTip: "Choose the render backend for the next game launch. A restart is required.")
        {
            Name = "VulkanRendererSelector", Position = adapter.Position - new Vector2(0, 0.052f), OriginAlign = adapter.OriginAlign
        };
        combo.AddItem((long)RendererBackend.Dx11, new StringBuilder("DX11"));
        combo.AddItem((long)RendererBackend.Vulkan, new StringBuilder("Vulkan"));
        combo.SelectItemByKey((long)state.Transaction.Draft, false);
        state.Combo = combo;
        var label = new MyGuiControlLabel(text: "Renderer", originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
        { Position = new Vector2(-0.25f, combo.Position.Y + 0.008f) };
        var notice = new MyGuiControlLabel(text: "", textScale: 0.70f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
        { Position = new Vector2(-0.25f, screen.Size.Value.Y / 2 - 0.158f) };
        state.Notice = notice;
        combo.ItemSelected += () => { state.Transaction.Draft = (RendererBackend)combo.GetSelectedKey(); UpdateNotice(state); };
        screen.Controls.Add(label); screen.Controls.Add(combo); screen.Controls.Add(notice);
        UpdateNotice(state);
    }
    private static void UpdateNotice(ScreenState state)
    {
        state.Notice.Text = state.Transaction.Draft != BackendRuntime.ActiveBackend
            ? "Restart required" + (state.Transaction.Draft == RendererBackend.Vulkan && BackendRuntime.RecoveryRequired ? " — acknowledge recovery in Pulsar config" : "")
            : "Active renderer: " + (BackendRuntime.ActiveBackend == RendererBackend.Dx11 ? "DX11" : "Vulkan");
    }
    internal static void BeginOk(MyGuiScreenOptionsDisplay screen) => State(screen).Transaction.BeginOk();
    internal static void EndOk(MyGuiScreenOptionsDisplay screen) => State(screen).Transaction.EndOk();
    internal static void Confirm(MyGuiScreenOptionsDisplay screen, MyGuiScreenMessageBox.ResultEnum result) => State(screen).Transaction.ConfirmResolution(result == MyGuiScreenMessageBox.ResultEnum.YES);
    internal static void ModeChanged(MyGuiScreenOptionsDisplay screen, bool success)
    {
        if (success) State(screen).Transaction.BeginResolutionConfirmation();
        else State(screen).Transaction.Cancel();
    }
    internal static void Cancel(MyGuiScreenOptionsDisplay screen) => State(screen).Transaction.Cancel();
    internal static void Commit(MyGuiScreenOptionsDisplay screen, bool immediate)
    {
        var state = State(screen);
        if (!state.Transaction.AcceptClose(immediate)) return;
        var draft = state.Transaction.Draft;
        try { ConfigStorage.CommitRenderer(draft); }
        catch (Exception error)
        {
            BackendRuntime.RecordFailure("Renderer selection could not be saved: " + error.Message);
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Error, MyMessageBoxButtonsType.OK,
                messageText: new StringBuilder("Renderer selection was not saved: " + error.Message), messageCaption: new StringBuilder("Vulkan renderer")));
            state.Transaction.Cancel();
            return;
        }
        state.Transaction.Cancel();
        PromptRestart(draft);
    }
    internal const string RestartPromptMarker = "Restart the game";
    private static void PromptRestart(RendererBackend draft)
    {
        if (draft == BackendRuntime.ActiveBackend) return;
        if (draft == RendererBackend.Vulkan && BackendRuntime.RecoveryRequired)
        {
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.OK,
                messageText: new StringBuilder("Vulkan is saved for the next launch, but startup recovery is still active. In the Vulkan plugin config, choose Retry Vulkan next launch, then restart the game."),
                messageCaption: new StringBuilder("Restart required")));
            return;
        }
        string renderer = draft == RendererBackend.Vulkan ? "Vulkan" : "DX11";
        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Info, MyMessageBoxButtonsType.YES_NO,
            messageText: new StringBuilder(renderer + " is saved for the next launch. " + RestartPromptMarker + " now?"),
            messageCaption: new StringBuilder("Restart required"),
            callback: result => { if (result == MyGuiScreenMessageBox.ResultEnum.YES) MySessionLoader.ExitGame(); }));
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "RecreateControls")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayControlsPatch { private static void Postfix(MyGuiScreenOptionsDisplay __instance, bool constructor) => DisplayRendererSelector.AddControls(__instance, constructor); }

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "OnOkClick")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayOkPatch
{
    private static void Prefix(MyGuiScreenOptionsDisplay __instance) { ExclusiveFullscreenChoice.Begin(); DisplayRendererSelector.BeginOk(__instance); }
    private static Exception Finalizer(MyGuiScreenOptionsDisplay __instance, Exception __exception) { DisplayRendererSelector.EndOk(__instance); return __exception; }
}
[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "OnCancelClick")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayCancelPatch { private static void Prefix(MyGuiScreenOptionsDisplay __instance) => DisplayRendererSelector.Cancel(__instance); }

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "OnVideoModeChangedAndConfirm")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayModeConfirmationPatch
{
    private static void Prefix(MyGuiScreenOptionsDisplay __instance, object result)
    {
        bool success = Convert.ToInt32(result) == 0;
        if (!success) ExclusiveFullscreenChoice.Rollback();
        DisplayRendererSelector.ModeChanged(__instance, success);
    }
}
[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "OnMessageBoxCallback")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayConfirmationPatch
{
    private static void Prefix(MyGuiScreenOptionsDisplay __instance, MyGuiScreenMessageBox.ResultEnum callbackReturn)
    {
        if (callbackReturn == MyGuiScreenMessageBox.ResultEnum.YES) ExclusiveFullscreenChoice.Keep();
        else ExclusiveFullscreenChoice.Rollback();
        DisplayRendererSelector.Confirm(__instance, callbackReturn);
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "CloseScreen")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayClosePatch
{
    private static void Postfix(MyGuiScreenOptionsDisplay __instance, bool __result) { if (__result) DisplayRendererSelector.Commit(__instance, false); }
}
[HarmonyPatch(typeof(MyGuiScreenBase), "CloseScreenNow")]
[HarmonyPatchCategory("Runtime")]
internal static class DisplayCloseNowPatch
{
    private static void Postfix(MyGuiScreenBase __instance) { if (__instance is MyGuiScreenOptionsDisplay display) DisplayRendererSelector.Commit(display, true); }
}
