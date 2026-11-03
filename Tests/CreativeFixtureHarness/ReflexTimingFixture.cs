using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using HarmonyLib;
using VRage.Library.Utils;
using VRageMath;
using VRageRender;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    // Held scene; mirrored conditions: On/FG-off, On/FG-on,
    // Boost/FG-off, Boost/FG-on, then reverse. This is an instrumented
    // 60 Hz diagnostic, not an uncapped FPS or click-to-photon benchmark.
    bool reflexTimingMode, timingInitialized, timingFinishing, timingOriginalFg;
    int timingLimiterState, timingPhase = -1, timingSamples;
    double timingWarmUntil, timingPhaseStart, timingNextSample;
    long timingFirstGenerated, timingLastGenerated;
    ulong timingLastFrame;
    object timingOldLimiter;
    Action timingLimiterInstall, timingLimiterRestore;
    Type timingConfig, timingFgConfig, timingService, timingFgTelemetry;
    MethodInfo timingBinding, timingLatency;
    static string TimingValue(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);
    static bool TimingComplete(object[] t) => (ulong)t[5] > 0 && (ulong)t[6] >= (ulong)t[5] &&
        (ulong)t[7] >= (ulong)t[6] && (ulong)t[8] >= (ulong)t[7] && (ulong)t[9] >= (ulong)t[8] &&
        (ulong)t[10] >= (ulong)t[9] && (ulong)t[11] >= (ulong)t[5] && (ulong)t[12] > (ulong)t[11];
    void TimingSet(Type type, string property, object value) => type.GetProperty(property).SetValue(type.GetField("Current").GetValue(null), value, null);
    object TimingRead(string property) => timingService.GetProperty(property).GetValue(null, null);
    int TimingCondition => timingPhase < 4 ? timingPhase : 7 - timingPhase;
    int TimingMode => TimingCondition < 2 ? 1 : 2;
    bool TimingFg => (TimingCondition & 1) != 0;
    void TimingCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-timing-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + timingPhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex timing: " + evidence);
    }
    void TimingConfigurePhase()
    {
        TimingSet(timingConfig, "Mode", Enum.ToObject(timingConfig.GetProperty("Mode").PropertyType, TimingMode));
        TimingSet(timingFgConfig, "Enabled", TimingFg);
        timingPhaseStart = clock.Elapsed.TotalSeconds;timingNextSample = timingPhaseStart + 4;
        timingSamples = 0;timingFirstGenerated = timingLastGenerated = -1;timingLastFrame = 0;
        File.AppendAllText(Path.Combine(reports, "reflex-timing-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + timingPhase + " mode=" + TimingMode + " fg=" + TimingFg + " warmupSeconds=4 measuredSeconds=8 rendererCapHz=60\n");
    }
    void ShutdownReflexTiming()
    {
        if (!timingInitialized) return;
        if (timingLimiterInstall != null) MyRenderProxy.RenderThread.BeforeDraw -= timingLimiterInstall;
        if (timingOldLimiter == null || Volatile.Read(ref timingLimiterState) == 2) return;
        if (timingLimiterRestore != null) return;
        timingLimiterRestore = () => {
            AccessTools.Field(MyRenderProxy.RenderThread.GetType(), "m_waiter").SetValue(MyRenderProxy.RenderThread, timingOldLimiter);
            MyRenderProxy.RenderThread.BeforeDraw -= timingLimiterRestore;
            Volatile.Write(ref timingLimiterState, 2);
        };
        MyRenderProxy.RenderThread.BeforeDraw += timingLimiterRestore;
    }
    void ExerciseReflexTiming()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (!timingInitialized)
        {
            TimingCheck(ReflexWindowState().Contains("visible=True") && !ReflexWindowState().Contains("minimized=True"), "visible non-minimized comparison window");
            timingConfig = ReflexType("Reflex", "ClientPlugin.Config");timingFgConfig = ReflexType("SpaceEngineersFrameGen", "ClientPlugin.Config");
            timingService = ReflexType("Anomaly", "ClientPlugin.Backend.ReflexService");
            timingFgTelemetry = ReflexType("SpaceEngineersFrameGen", "ClientPlugin.FrameGen.IntegrationStatus");
            var dx = ReflexType("Anomaly.Dx11", "ClientPlugin.ShaderFramework.Reflex.ReflexDx11Bridge");
            var vk = ReflexType("Vulkan", "ClientPlugin.Rendering.GuestBridge");
            timingBinding = dx?.GetMethod("Snapshot") ?? vk?.GetMethod("ReflexBindingState");
            timingLatency = dx?.GetMethod("LatencyStatus") ?? vk?.GetMethod("ReflexLatencyStatus");
            TimingCheck(timingConfig != null && timingFgConfig != null && timingService != null && timingFgTelemetry != null && timingBinding != null && timingLatency != null,
                "actual request, generation and driver timing endpoints resolved");
            timingOriginalFg = (bool)timingFgConfig.GetProperty("Enabled").GetValue(timingFgConfig.GetField("Current").GetValue(null), null);
            TimingSet(timingFgConfig, "Enabled", false);
            Camera(new Vector3D(15, 11, -19), Vector3D.Zero);
            File.WriteAllText(Path.Combine(reports, "reflex-timing.csv"), "utc,phase,mode,fg_enabled,fg_ready,generated,epoch,sleeps,markers,frame_id,sim_start_us,sim_end_us,render_start_us,render_end_us,present_start_us,present_end_us,gpu_start_us,gpu_end_us,visible,minimized,foreground\n");
            timingInitialized = true;timingWarmUntil = now + 10;
            timingLimiterInstall = () => {
                var thread = MyRenderProxy.RenderThread;
                var field = AccessTools.Field(thread.GetType(), "m_waiter");timingOldLimiter = field.GetValue(thread);
                var timer = (MyGameTimer)AccessTools.Field(thread.GetType(), "m_timer").GetValue(thread);
                var limiter = new WaitForTargetFrameRate(timer, 60);field.SetValue(thread, limiter);
                new XDocument(new XElement("ReflexTimingWorkload", new XAttribute("Schema", 1),
                    new XAttribute("Backend", expectedBackend), new XAttribute("RendererCapHz", 60),
                    new XAttribute("OriginalTicksPerFrame", ((WaitForTargetFrameRate)timingOldLimiter).TickPerFrame),
                    new XAttribute("ComparisonTicksPerFrame", limiter.TickPerFrame),
                    new XAttribute("Camera", "15,11,-19"), new XAttribute("Target", "0,0,0"),
                    new XAttribute("WindowWidth", thread.CurrentSettings.BackBufferWidth), new XAttribute("WindowHeight", thread.CurrentSettings.BackBufferHeight),
                    new XAttribute("VSync", thread.CurrentSettings.VSync), new XAttribute("SampleIntervalSeconds", ".1"),
                    new XAttribute("PhaseSeconds", 12), new XAttribute("DiscardedWarmupSeconds", 4),
                    new XAttribute("Scope", "Held scene and stock limiter; driver marker-to-GPU timing, not click-to-photon latency or uncapped performance")))
                    .Save(Path.Combine(reports, "reflex-timing-workload.xml"));
                thread.BeforeDraw -= timingLimiterInstall;Volatile.Write(ref timingLimiterState, 1);
            };
            MyRenderProxy.RenderThread.BeforeDraw += timingLimiterInstall;
            return;
        }
        if (timingFinishing)
        {
            if (Volatile.Read(ref timingLimiterState) != 2) return;
            Capture("reflex-timing-after");
            completed = true;completedAt = now;
            WriteFixture("Held-scene Reflex/FG timing capture completed; numerical and capture review remain separate.");
            File.WriteAllText(Path.Combine(reports, "reflex-timing-completed.txt"), "Eight repeated/mirrored 12-second phases; four-second warmup; valid unique driver reports and FG counter invariants passed. Stock limiter and FG setting restored.");
            File.WriteAllText(Path.Combine(reports, "fixture-completed.txt"), DateTime.UtcNow.ToString("u"));return;
        }
        if (now < timingWarmUntil || Volatile.Read(ref timingLimiterState) != 1) return;
        if (timingPhase < 0)
        { Capture("reflex-timing-before");timingPhase = 0;TimingConfigurePhase();return; }
        if (now - timingPhaseStart >= 12)
        {
            TimingCheck(timingSamples >= 20, "at least 20 valid unique driver reports in the measured phase; actual=" + timingSamples);
            TimingCheck(timingFirstGenerated >= 0 && (TimingFg ? timingLastGenerated > timingFirstGenerated : timingLastGenerated == timingFirstGenerated),
                "measured FG counter " + timingFirstGenerated + " -> " + timingLastGenerated + "; enabled=" + TimingFg);
            if (++timingPhase == 8)
            {
                TimingSet(timingConfig, "Mode", Enum.ToObject(timingConfig.GetProperty("Mode").PropertyType, 1));
                TimingSet(timingFgConfig, "Enabled", timingOriginalFg);timingFinishing = true;ShutdownReflexTiming();return;
            }
            TimingConfigurePhase();return;
        }
        if (now < timingNextSample) return;
        timingNextSample = now + .1;
        var binding = timingBinding.Invoke(null, null) as object[];
        var latency = timingLatency.Invoke(null, null) as object[];
        var fg = timingFgTelemetry.GetMethod("GetSnapshot").Invoke(null, null) as object[];
        if (binding == null || binding.Length != 9 || latency == null || latency.Length != 13 || fg == null || fg.Length != 7 ||
            !Convert.ToBoolean(TimingRead("Available")) || Convert.ToInt32(TimingRead("ConfiguredMode")) != TimingMode ||
            (int)binding[4] != TimingMode || (ulong)binding[6] != (ulong)latency[1] || (int)latency[2] != 0 || (int)latency[3] == 0 ||
            (bool)fg[3] != TimingFg || (ulong)latency[4] <= timingLastFrame || !TimingComplete(latency)) return;
        string window = ReflexWindowState();
        if (!window.Contains("visible=True") || window.Contains("minimized=True")) return;
        timingLastFrame = (ulong)latency[4];long generated = Convert.ToInt64(fg[5]);
        if (timingFirstGenerated < 0) timingFirstGenerated = generated;timingLastGenerated = generated;
        object[] row = { DateTime.UtcNow.ToString("o"), timingPhase, TimingMode, TimingFg ? 1 : 0, (bool)fg[4] ? 1 : 0, generated,
            binding[6], binding[7], binding[8], latency[4], latency[5], latency[6], latency[7], latency[8], latency[9], latency[10], latency[11], latency[12],
            1, 0, window.Contains("foreground=True") ? 1 : 0 };
        File.AppendAllText(Path.Combine(reports, "reflex-timing.csv"), string.Join(",", row.Select(TimingValue)) + "\n");timingSamples++;
    }
}
