using System;
using System.Reflection;
using ClientPlugin.Bootstrap;
using HarmonyLib;

namespace ClientPlugin.Patches;

// Pulsar 2.4.2's ExitThreadSafe prefix kills the process before the game's
// renderer disposal. Vulkan needs the original orderly shutdown to retire GPU
// resources, persist its pipeline cache and close the active-session journal.
[HarmonyPatch]
[HarmonyPatchCategory("Bootstrap")]
internal static class VulkanGracefulExitPatch
{
    private static bool Prepare() => Target() != null;
    private static MethodBase TargetMethod() => Target();
    private static MethodInfo Target()
    {
        Type type = PulsarCompatibility.FindType("Pulsar.Legacy.Patch.Patch_ExitThreadSafe");
        return PulsarShutdownContract.GetPrefix(type);
    }
    private static bool Prefix(ref bool __result)
    {
        if (BackendRuntime.ActiveBackend != RendererBackend.Vulkan) return true;
        // This is the return value of Pulsar's prefix, which controls whether
        // Harmony invokes the base game's ExitThreadSafe implementation.
        __result = true;
        return false;
    }
}
