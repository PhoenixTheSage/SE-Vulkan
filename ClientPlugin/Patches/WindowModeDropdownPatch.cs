using System.Text;
using ClientPlugin.Bootstrap;
using ClientPlugin.Platform;
using ClientPlugin.Settings;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "UpdateWindowModeComboBox")]
[HarmonyPatchCategory("Runtime")]
internal static class WindowModeListPatch
{
    private static long selected;
    private static void Prefix(MyGuiScreenOptionsDisplay __instance) => selected = Key(__instance);
    private static void Postfix(MyGuiScreenOptionsDisplay __instance)
    {
        var combo = Combo(__instance);
        if (combo == null) return;
        bool fullscreenListed = combo.TryGetItemByKey(2) != null;
        if (!WindowModeChoice.Offer(BackendRuntime.ActiveBackend == RendererBackend.Vulkan, VulkanWindowHost.IsLinuxPlatform, fullscreenListed))
        {
            if (selected == WindowModeChoice.ExclusiveKey) combo.SelectItemByKey(fullscreenListed ? 2 : 1, false);
            return;
        }
        combo.AddItem(WindowModeChoice.ExclusiveKey, new StringBuilder("Exclusive Fullscreen"));
        if (selected == WindowModeChoice.ExclusiveKey) combo.SelectItemByKey(WindowModeChoice.ExclusiveKey, false);
    }
    internal static MyGuiControlCombobox Combo(MyGuiScreenOptionsDisplay screen) =>
        AccessTools.Field(typeof(MyGuiScreenOptionsDisplay), "m_comboWindowMode").GetValue(screen) as MyGuiControlCombobox;
    private static long Key(MyGuiScreenOptionsDisplay screen) => Combo(screen)?.GetSelectedKey() ?? -1;
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "WriteSettingsToControls")]
[HarmonyPatchCategory("Runtime")]
internal static class WindowModeRestorePatch
{
    private static void Postfix(MyGuiScreenOptionsDisplay __instance, MyRenderDeviceSettings deviceSettings)
    {
        var combo = WindowModeListPatch.Combo(__instance);
        if (combo == null || !ExclusiveFullscreenChoice.Enabled || deviceSettings.WindowMode != MyWindowModeEnum.Fullscreen) return;
        if (combo.TryGetItemByKey(WindowModeChoice.ExclusiveKey) == null) return;
        combo.SelectItemByKey(WindowModeChoice.ExclusiveKey, false);
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "GetSelectedWindowMode")]
[HarmonyPatchCategory("Runtime")]
internal static class WindowModeEnumPatch
{
    private static void Postfix(MyGuiScreenOptionsDisplay __instance, ref MyWindowModeEnum __result)
    {
        var combo = WindowModeListPatch.Combo(__instance);
        if (combo != null && combo.GetSelectedKey() == WindowModeChoice.ExclusiveKey) __result = MyWindowModeEnum.Fullscreen;
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsDisplay), "ReadSettingsFromControls")]
[HarmonyPatchCategory("Runtime")]
internal static class WindowModeReadPatch
{
    private static void Postfix(MyGuiScreenOptionsDisplay __instance, ref MyRenderDeviceSettings deviceSettings, ref bool __result)
    {
        var combo = WindowModeListPatch.Combo(__instance);
        if (combo == null) return;
        long key = combo.GetSelectedKey();
        bool exclusive = WindowModeChoice.WantsExclusive(key) && BackendRuntime.ActiveBackend == RendererBackend.Vulkan && !VulkanWindowHost.IsLinuxPlatform;
        if (key == WindowModeChoice.ExclusiveKey) deviceSettings.WindowMode = MyWindowModeEnum.Fullscreen;
        if (exclusive == ExclusiveFullscreenChoice.Enabled) return;
        ExclusiveFullscreenChoice.Set(exclusive);
        __result = true;
    }
}
