using System;
using System.IO;
using System.Threading;
using HarmonyLib;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    const string ReflexOwnerHook = "CreativeFixture.ReflexOwner", ReflexTestOwner = "CreativeFixture.Owner";
    static int reflexOwnerSuspended;
    int reflexOwnerPhase;
    double reflexOwnerNext;
    ulong reflexOwnerRetiredEpoch;
    long reflexOwnerFgCount;
    object reflexOwnerPlugin;
    Type reflexOwnerPluginType;
    Harmony reflexOwnerHooks;
    static bool AllowReflexOwnerUpdate() => Volatile.Read(ref reflexOwnerSuspended) == 0;
    void ReleaseReflexTestOwner() => ReflexType("Anomaly", "ClientPlugin.Backend.ReflexService")?.GetMethod("ReleaseOwner")?.Invoke(null, new object[] { ReflexTestOwner });
    void ShutdownReflexOwner()
    {
        if (reflexOwnerHooks == null) return;
        ReleaseReflexTestOwner();Volatile.Write(ref reflexOwnerSuspended, 0);
        reflexOwnerHooks.UnpatchAll(ReflexOwnerHook);reflexOwnerHooks = null;
    }
    void ReflexOwnerCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-owner-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + reflexOwnerPhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex owner fixture: " + evidence);
    }
    long ReflexOwnerFgCounter()
    {
        var telemetry = ReflexType("SpaceEngineersFrameGen", "ClientPlugin.FrameGen.IntegrationStatus");
        var state = telemetry?.GetMethod("GetSnapshot")?.Invoke(null, null) as object[];
        return state != null && state.Length == 7 ? Convert.ToInt64(state[5]) : -1;
    }
    void ExerciseReflexOwner(double world, object[] binding, Func<string, object> read)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_OWNER") != "1" || world < 52 || world < reflexOwnerNext || reflexOwnerPhase >= 5) return;
        switch (reflexOwnerPhase)
        {
            case 0:
                ReflexOwnerCheck(Convert.ToBoolean(read("Available")), "armed native session before owner disposal");
                ReflexType("Anomaly", "ClientPlugin.Backend.ReflexService").GetMethod("RequestFor").Invoke(null, new object[] { ReflexTestOwner, 1 });
                reflexOwnerPluginType = ReflexType("Reflex", "ClientPlugin.Plugin");
                reflexOwnerPlugin = reflexOwnerPluginType.GetProperty("Instance").GetValue(null, null);
                reflexOwnerHooks = new Harmony(ReflexOwnerHook);
                reflexOwnerHooks.Patch(reflexOwnerPluginType.GetMethod("Update"), prefix: new HarmonyMethod(typeof(Plugin), nameof(AllowReflexOwnerUpdate)));
                Volatile.Write(ref reflexOwnerSuspended, 1);
                reflexOwnerPluginType.GetMethod("Dispose").Invoke(reflexOwnerPlugin, null);
                break;
            case 1:
                ReflexOwnerCheck(reflexOwnerPluginType.GetProperty("Instance").GetValue(null, null) == null &&
                    Convert.ToInt32(read("RequestedMode")) == 1 && Convert.ToBoolean(read("Available")),
                    "real plugin Dispose releases its owner while another owner's native On survives");
                reflexOwnerFgCount = ReflexOwnerFgCounter();ReleaseReflexTestOwner();break;
            case 2:
                ReflexOwnerCheck(Convert.ToInt32(read("RequestedMode")) == 0 && !Convert.ToBoolean(read("Available")) &&
                    Convert.ToInt32(read("ConfiguredMode")) == -1 && binding != null && (int)binding[4] == -1,
                    "last owner release retires the native session without a remaining request");
                ReflexOwnerCheck(ReflexOwnerFgCounter() > reflexOwnerFgCount, "custom FG continues across consumer unload and native retirement");
                reflexOwnerRetiredEpoch = (ulong)binding[6];break;
            case 3:
                ReflexOwnerCheck(binding != null && (ulong)binding[6] == reflexOwnerRetiredEpoch && (ulong)binding[7] == 0 && (ulong)binding[8] == 0,
                    "unloaded consumer leaves no native sleep or marker calls");
                reflexOwnerPluginType.GetMethod("Init").Invoke(reflexOwnerPlugin, new object[] { fixtureGameInstance });
                ShutdownReflexOwner();break;
            case 4:
                ReflexOwnerCheck(ReferenceEquals(reflexOwnerPluginType.GetProperty("Instance").GetValue(null, null), reflexOwnerPlugin) &&
                    Convert.ToInt32(read("RequestedMode")) == 1 && Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 1 &&
                    binding != null && (ulong)binding[7] > 0 && (ulong)binding[8] > 0,
                    "actual plugin Init and resumed host Update rearm native On");
                Capture("reflex-owner-reloaded");
                File.WriteAllText(Path.Combine(reports, "reflex-owner-passed.txt"), "Actual Dispose/Init, other-owner preservation, last-owner retirement and native rearm passed. Host Update suppression models unload; this is not a Pulsar loader unload test.");
                break;
        }
        reflexOwnerPhase++;reflexOwnerNext = world + 4;
    }
}
