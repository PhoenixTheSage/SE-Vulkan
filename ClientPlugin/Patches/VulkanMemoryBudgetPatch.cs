using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using HarmonyLib;
using VRage;

namespace ClientPlugin.Patches;

// These platform fields are normally populated by Windows' DX11 device path.
// A zero generated-texture budget prevents the game's LCD scheduler running.
[HarmonyPatch]
[HarmonyPatchCategory("Bootstrap")]
internal static class VulkanMemoryBudgetPatch
{
    private const ulong GeneratedBudget = 256UL * 1024 * 1024;
    private const ulong StreamedBudget = 512UL * 1024 * 1024;
    private static IEnumerable<MethodBase> TargetMethods()
    {
        object renderer = MyVRage.Platform.Render ?? throw new InvalidOperationException("Platform render capabilities are unavailable at the Vulkan factory.");
        var map = renderer.GetType().GetInterfaceMap(typeof(IVRageRender));
        foreach (string name in new[] { "GetMemoryBudgetForGeneratedTextures", "GetMemoryBudgetForStreamedResources", "GetMemoryBudgetForVoxelTextureArrays" })
        {
            int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == name && method.ReturnType == typeof(ulong) && method.GetParameters().Length == 0);
            if (index < 0 || map.TargetMethods[index].IsAbstract) throw new MissingMethodException("Platform render budget contract changed: " + name);
            yield return map.TargetMethods[index];
        }
    }
    private static bool Prefix(MethodBase __originalMethod, ref ulong __result)
    {
        if (BackendRuntime.ActiveBackend != RendererBackend.Vulkan) return true;
        string name = __originalMethod.Name;
        __result = name.EndsWith("GetMemoryBudgetForGeneratedTextures", StringComparison.Ordinal) ? GeneratedBudget
            : name.EndsWith("GetMemoryBudgetForStreamedResources", StringComparison.Ordinal) ? StreamedBudget : 512UL*1024*1024;
        VulkanDiagnostics.Set("Platform memory budgets", SubsystemState.Active,
            "Conservative generated=256 MiB, streamed=512 MiB; voxel arrays=512 MiB bounded residency. Native memory-budget telemetry remains pending.");
        return false;
    }
}
