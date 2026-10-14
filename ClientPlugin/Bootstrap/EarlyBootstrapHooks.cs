using System;
using System.Runtime.CompilerServices;
using ClientPlugin.Patches;
using ClientPlugin.Platform;
using HarmonyLib;

namespace ClientPlugin.Bootstrap;

public static class EarlyBootstrapHooks
{
    private static readonly object Sync = new object();
    private static bool attempted;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Install()
    {
        lock (Sync)
        {
            if (attempted) return;
            attempted = true;
            try
            {
                // Called by the factory after MyVRage.Platform and MyRenderSettings
                // are initialized, before renderer/device creation.
                var harmony = new Harmony("AE72EBB8-F4A8-4ADB-A400-19CC46FDAB79.Bootstrap");
                harmony.CreateClassProcessor(typeof(GraphicsRendererIdentityPatch)).Patch();
                harmony.CreateClassProcessor(typeof(GraphicsOptionsCapabilityPatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeMediaUpdatePatch)).Patch();
                harmony.CreateClassProcessor(typeof(NativeMediaPlayPatch)).Patch();
                harmony.CreateClassProcessor(typeof(VulkanGracefulExitPatch)).Patch();
                harmony.CreateClassProcessor(typeof(VulkanMemoryBudgetPatch)).Patch();
                if (VulkanWindowHost.IsLinuxPlatform) VulkanWindowHost.InstallLinuxForwardingGuard(harmony);
            }
            catch (Exception error) { BackendRuntime.SetHook(false, BackendRuntime.HookFingerprint, "Early runtime hooks failed: " + error.Message); }
        }
    }
}
