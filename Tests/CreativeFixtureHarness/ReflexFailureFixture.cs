using System;
using System.IO;
using System.Threading;
using HarmonyLib;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    const string ReflexFaultOwner = "CreativeFixture.ReflexFailure";
    static int reflexFaultArmed, reflexFaultInjected;
    int reflexFailurePhase;
    double reflexFailureNext;
    long reflexFailureFgCount;
    Harmony reflexFaultHooks;
    static bool RejectOneReflexMarker(uint __2, ref int __result)
    {
        if (__2 != 0 || Interlocked.CompareExchange(ref reflexFaultArmed, 0, 1) != 1) return true;
        Interlocked.Increment(ref reflexFaultInjected);__result = -1;return false;
    }
    void ShutdownReflexFailure()
    {
        Interlocked.Exchange(ref reflexFaultArmed, 0);
        reflexFaultHooks?.UnpatchAll(ReflexFaultOwner);reflexFaultHooks = null;
    }
    void ReflexFailureCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-failure-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + reflexFailurePhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex failure fixture: " + evidence);
    }
    void ExerciseReflexFailure(double world, Type config, Func<string, object> read)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_FAILURE") != "1" || world < 52 || world < reflexFailureNext || reflexFailurePhase >= 5) return;
        var telemetry = ReflexType("SpaceEngineersFrameGen", "ClientPlugin.FrameGen.IntegrationStatus");
        var fg = telemetry?.GetMethod("GetSnapshot")?.Invoke(null, null) as object[];
        long count = fg != null && fg.Length == 7 ? Convert.ToInt64(fg[5]) : -1;
        Action<int> setMode = mode => {
            var property = config.GetProperty("Mode");property.SetValue(config.GetField("Current").GetValue(null), Enum.ToObject(property.PropertyType, mode));
        };
        switch (reflexFailurePhase)
        {
            case 0:
                ReflexFailureCheck(Convert.ToBoolean(read("Available")) && fg != null && (bool)fg[3] && (bool)fg[4] && count > 0,
                    "armed Reflex and active custom FG before injected rejection");
                reflexFailureFgCount = count;
                var dx = ReflexType("Anomaly.Dx11", "ClientPlugin.ShaderFramework.Reflex.ReflexDx11Bridge");
                var target = dx?.GetMethod("Marker") ?? ReflexType("Vulkan", "ClientPlugin.Rendering.GuestBridge")?.GetMethod("ReflexMarker");
                ReflexFailureCheck(target != null, "exact renderer-owned marker adapter found");
                reflexFaultInjected = 0;reflexFaultArmed = 1;
                reflexFaultHooks = new Harmony(ReflexFaultOwner);
                reflexFaultHooks.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), nameof(RejectOneReflexMarker)));
                break;
            case 1:
                ReflexFailureCheck(Volatile.Read(ref reflexFaultInjected) == 1 && !Convert.ToBoolean(read("Available")),
                    "one synthetic adapter rejection retired active Reflex");
                ShutdownReflexFailure();break;
            case 2:
                ReflexFailureCheck(!Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("RequestedMode")) == 1 &&
                    Convert.ToInt32(read("ConfiguredMode")) == -1, "failed mode remains retired after injection hook removal");
                ReflexFailureCheck(count > reflexFailureFgCount, "custom FG generated additional outputs while Reflex stayed retired");
                reflexFailureFgCount = count;setMode(2);break;
            case 3:
                ReflexFailureCheck(Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 2,
                    "mode change rearms actual native Boost after the one-shot failure");
                ReflexFailureCheck(count > reflexFailureFgCount, "custom FG continues after Reflex recovery");
                Capture("reflex-failure-recovered");setMode(1);break;
            case 4:
                ReflexFailureCheck(Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 1,
                    "actual native On restored after recovery");
                File.WriteAllText(Path.Combine(reports, "reflex-failure-passed.txt"), "One simulated adapter rejection: retirement/latch, continued custom FG, mode-change rearm and On restoration passed. This is fault injection, not a physical driver failure.");
                break;
        }
        reflexFailurePhase++;reflexFailureNext = world + 4;
    }
}
