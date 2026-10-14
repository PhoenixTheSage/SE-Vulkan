using ClientPlugin.Bootstrap;
using HarmonyLib;
using Sandbox.Game.Gui;
using ClientPlugin.Rendering;
using ClientPlugin.Diagnostics;
using VRageRender;

namespace ClientPlugin.Patches;

// The stock GUI owns playback, looping and skipping. Only failed media falls back.
[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "Update")]
[HarmonyPatchCategory("Bootstrap")]
internal static class NativeMediaUpdatePatch
{
    private static bool Prefix(MyGuiScreenIntroVideo __instance, bool ___m_loop, ref uint ___m_videoID, ref bool __result)
    {
        if (BackendRuntime.ActiveBackend != RendererBackend.Vulkan) return true;
        var renderer = MyVulkanRender.Active;
        if (renderer == null || (renderer.MediaAvailable && !renderer.MediaFailed(___m_videoID))) return true;
        if (___m_loop) { if (___m_videoID != uint.MaxValue) { MyRenderProxy.CloseVideo(___m_videoID); ___m_videoID = uint.MaxValue; } __instance.ShowPictures = true; return true; }
        // Closing preserves the screen's existing Closed callback, which advances startup to the main menu.
        __instance.CloseScreenNow(); __result = true; return false;
    }
}
[HarmonyPatch(typeof(MyGuiScreenIntroVideo), "TryPlayVideo")]
[HarmonyPatchCategory("Bootstrap")]
internal static class NativeMediaPlayPatch
{
    private static bool Prefix() => BackendRuntime.ActiveBackend != RendererBackend.Vulkan || MyVulkanRender.Active?.MediaAvailable == true;
    private static void Postfix(MyGuiScreenIntroVideo __instance, bool ___m_loop, uint ___m_videoID, string ___m_currentVideo)
    {
        // Stock does not enqueue PlayVideo when a configured file is missing.
        // That case must still leave the intro, or select the menu picture path.
        if (BackendRuntime.ActiveBackend != RendererBackend.Vulkan || ___m_videoID != uint.MaxValue || __instance.ShowPictures) return;
        VulkanDiagnostics.Failure("Native media could not start " + ___m_currentVideo + "; using the stock screen fallback.");
        if (___m_loop) __instance.ShowPictures = true;
        else __instance.CloseScreenNow();
    }
}
