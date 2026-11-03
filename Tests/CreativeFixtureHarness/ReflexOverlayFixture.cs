using System;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Stats;
using VRageMath;
using VRageRender;
using VRageRender.Utils;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    bool reflexOverlayMode;
    int overlayPhase;
    double overlayNext;
    string overlayCapturePath;
    Action overlayAfterCapture;
    double overlayCaptureDeadline;
    void OverlayCapture(string name, Action after)
    {
        Capture(name);overlayCapturePath = Path.Combine(reports, name + ".png");
        overlayAfterCapture = after;overlayCaptureDeadline = clock.Elapsed.TotalSeconds + 15;
    }
    void OverlayMode(int mode)
    {
        var config = ReflexType("Reflex", "ClientPlugin.Config");
        var property = config.GetProperty("Mode");
        property.SetValue(config.GetField("Current").GetValue(null), Enum.ToObject(property.PropertyType, mode), null);
    }
    void OverlayCheck(bool value, string message)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-overlay-history.txt"), DateTime.UtcNow.ToString("u") + " phase=" + overlayPhase + " passed=" + value + " " + message + "\n");
        if (!value) throw new InvalidOperationException("Reflex overlay: " + message);
    }
    string OverlayText(string name)
    {
        var text = new StringBuilder();Stats.Timing.WriteTo(text);
        var pluginType = ReflexType("Reflex", "ClientPlugin.Plugin");
        var overlay = AccessTools.Field(pluginType, "latencyOverlay").GetValue(pluginType.GetProperty("Instance").GetValue(null, null));
        var source = AccessTools.Field(overlay.GetType(), "source").GetValue(overlay);
        File.AppendAllText(Path.Combine(reports, "reflex-overlay-provider.txt"), DateTime.UtcNow.ToString("u") + " " + name + ": " +
            AccessTools.Property(source.GetType(), "LastIssue").GetValue(source, null) + "\n");
        File.WriteAllText(Path.Combine(reports, name + ".txt"), text.ToString());return text.ToString();
    }
    void OverlayScreenAction()
    {
        var gui = AccessTools.Field(typeof(MyGuiSandbox), "Gui").GetValue(null);
        AccessTools.Method(gui.GetType(), "SwitchTimingScreen").Invoke(gui, null);
    }
    void ExerciseReflexOverlay()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (overlayAfterCapture != null)
        {
            if (!File.Exists(overlayCapturePath) || new FileInfo(overlayCapturePath).Length == 0)
            {
                if (now > overlayCaptureDeadline) OverlayCheck(false, "capture delivery timed out before page transition");
                return;
            }
            OverlayCheck(true, "capture delivered before page transition: " + Path.GetFileName(overlayCapturePath));
            var after = overlayAfterCapture;overlayAfterCapture = null;after();overlayNext = now + 5;return;
        }
        if (now < overlayNext) return;
        if (overlayPhase == 0)
        {
            Camera(new Vector3D(15, 11, -19), Vector3D.Zero);OverlayMode(1);
            OverlayCheck(MyRenderProxy.DrawRenderStats == MyRenderProxy.MyStatsState.NoDraw, "closed timing-page precondition");
            overlayNext = now + 3;overlayPhase++;return;
        }
        if (overlayPhase == 1)
        {
            OverlayCheck(!OverlayText("reflex-overlay-hidden").Contains("Reflex frame latency"), "no owned rows while closed");
            OverlayScreenAction();
        }
        else if (overlayPhase == 2)
        {
            OverlayCheck(MyRenderProxy.DrawRenderStats == MyRenderProxy.MyStatsState.SimpleTimingStats, "actual Shift-F11 action opened simple timing page after queued LoadData");
            string text = OverlayText("reflex-overlay-simple");
            OverlayCheck(text.Contains("Reflex frame latency (sim -> GPU)") && !text.Contains("no fresh driver report"), "simple timing page has actual driver latency");
            // Same page state assigned by the developer Shift-F11 branch;
            // no change to the player's developer/debug settings.
            OverlayCapture("reflex-overlay-simple", () => MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.ComplexTimingStats);
        }
        else if (overlayPhase == 3)
        {
            OverlayCheck(OverlayText("reflex-overlay-complex").Contains("Reflex render latency"), "complex timing collector retains Reflex rows");
            OverlayCapture("reflex-overlay-complex", () => OverlayMode(0));
        }
        else if (overlayPhase == 4)
        {
            string text = OverlayText("reflex-overlay-off");
            OverlayCheck(text.Contains("no fresh driver report") && !text.Contains("sim -> GPU"), "Off removes numeric latency instead of showing zero");
            OverlayCapture("reflex-overlay-off", () => OverlayMode(2));
        }
        else if (overlayPhase == 5)
        {
            OverlayCheck(OverlayText("reflex-overlay-boost").Contains("Reflex frame latency (sim -> GPU)"), "Boost rearms live timing");
            OverlayCapture("reflex-overlay-boost", () => {
                var gui = AccessTools.Field(typeof(MyGuiSandbox), "Gui").GetValue(null);
                var field = AccessTools.Field(gui.GetType(), "m_currentStatisticsScreen");
                MyGuiSandbox.RemoveScreen((MyGuiScreenBase)field.GetValue(gui));field.SetValue(gui, null);
            });
        }
        else
        {
            OverlayCheck(MyRenderProxy.DrawRenderStats == MyRenderProxy.MyStatsState.NoDraw && !OverlayText("reflex-overlay-closed").Contains("Reflex frame latency"), "closed timing page removes owned rows");
            OverlayMode(1);completed = true;completedAt = now;
            File.WriteAllText(Path.Combine(reports, "reflex-overlay-completed.txt"), DateTime.UtcNow.ToString("u"));
            File.WriteAllText(Path.Combine(reports, "fixture-completed.txt"), DateTime.UtcNow.ToString("u"));
            WriteFixture("Actual timing-page action and shared Reflex rows exercised; review delivered captures separately.");return;
        }
        overlayNext = now + 5;overlayPhase++;
    }
}
