using System.Reflection;
using ClientPlugin.Rendering;
using HarmonyLib;
using Sandbox.Game.Lights;
using Sandbox.Game.World;

namespace ClientPlugin.Patches;

// The renderer's celestial ownership flag names the solar light, rather than
// every directional/distant flare. Read the authoritative game-owned identity.
[HarmonyPatch(typeof(MySector),nameof(MySector.UpdateSunLight))]
[HarmonyPatchCategory("Runtime")]
internal static class SolarGlareIdentityPatch
{
    static readonly FieldInfo Sun=AccessTools.Field(typeof(MySector),"m_sunFlare");
    static bool Prepare()=>Sun!=null;
    static void Postfix()=>GuestBridge.SetSunLightRenderId((Sun.GetValue(null) as MyLight)?.RenderObjectID??uint.MaxValue);
}

[HarmonyPatch(typeof(MySector),"UnloadData")]
[HarmonyPatchCategory("Runtime")]
internal static class SolarGlareUnloadPatch
{
    static void Postfix()=>GuestBridge.SetSunLightRenderId(uint.MaxValue);
}
