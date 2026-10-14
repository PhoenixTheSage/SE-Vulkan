using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Bootstrap;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Platform.VideoMode;
using SpaceEngineers.Game.GUI;
using VRage.Utils;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyVideoSettingsManager), "get_RunningGraphicsRenderer")]
[HarmonyPatchCategory("Bootstrap")]
internal static class GraphicsRendererIdentityPatch
{
    private static void Postfix(ref MyStringId __result)
    {
        if (BackendRuntime.ActiveBackend == RendererBackend.Vulkan) __result = MyStringId.GetOrCompute("Vulkan");
    }
}

[HarmonyPatch(typeof(MyGuiScreenOptionsGraphics), "RecreateControls")]
[HarmonyPatchCategory("Bootstrap")]
internal static class GraphicsOptionsCapabilityPatch
{
    public static bool SupportsGraphicsOptions() => BackendRuntime.ActiveBackend == RendererBackend.Vulkan ||
        MyVideoSettingsManager.RunningGraphicsRenderer == MySandboxGame.DirectX11RendererKey;
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
    {
        var il = input.ToList();
        var getter = AccessTools.PropertyGetter(typeof(MyVideoSettingsManager), "RunningGraphicsRenderer");
        var key = AccessTools.Field(typeof(MySandboxGame), nameof(MySandboxGame.DirectX11RendererKey));
        var replacement = AccessTools.Method(typeof(GraphicsOptionsCapabilityPatch), nameof(SupportsGraphicsOptions));
        int patched = 0;
        for (int i = 0; i + 2 < il.Count; i++)
        {
            if (!il[i].Calls(getter) || !il[i + 1].LoadsField(key) || il[i + 2].operand is not MethodInfo compare || compare.Name != "op_Equality") continue;
            var instruction = new CodeInstruction(OpCodes.Call, replacement);
            for (int j = 0; j < 3; j++) { instruction.labels.AddRange(il[i + j].labels); instruction.blocks.AddRange(il[i + j].blocks); }
            il.RemoveRange(i, 3); il.Insert(i, instruction); patched++;
        }
        if (patched != 1) throw new InvalidOperationException("Graphics options renderer capability gate changed; expected exactly one DX11 comparison.");
        return il;
    }
}
