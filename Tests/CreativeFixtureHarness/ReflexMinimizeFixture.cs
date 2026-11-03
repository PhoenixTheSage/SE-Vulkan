using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using VRage;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    sealed class MinimizedSample { internal double At; internal ulong Epoch, Sleeps; }
    readonly List<MinimizedSample> reflexMinSamples = new List<MinimizedSample>();
    Form reflexMinForm;
    System.Windows.Forms.Timer reflexRestoreTimer;
    FormWindowState reflexOriginalWindowState;
    bool reflexMinChanged;
    IntPtr reflexMinHandle;
    int reflexMinPhase, reflexMinRestored;
    string reflexMinError;
    double reflexMinNext, reflexMinDeadline;
    ulong reflexMinResumeEpoch, reflexMinResumeSleeps, reflexMinResumeMarkers;
    void ReflexMinCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-minimize-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + reflexMinPhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex minimize: " + evidence);
    }
    void RestoreReflexWindow()
    {
        reflexRestoreTimer?.Stop();reflexRestoreTimer?.Dispose();reflexRestoreTimer = null;
        if (reflexMinChanged && reflexMinForm != null && !reflexMinForm.IsDisposed) reflexMinForm.WindowState = reflexOriginalWindowState;
        reflexMinChanged = false;
        Volatile.Write(ref reflexMinRestored, 1);
    }
    void ShutdownReflexMinimize()
    {
        var form = reflexMinForm;
        if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
        try { form.BeginInvoke((Action)RestoreReflexWindow); } catch (InvalidOperationException) { }
    }
    void ExerciseReflexMinimize(double world, object[] binding, Func<string, object> read)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_MINIMIZE") != "1" || world < 52 || world < reflexMinNext || reflexMinPhase >= 3) return;
        var error = Volatile.Read(ref reflexMinError);
        if (error != null) throw new InvalidOperationException("Reflex minimize UI dispatch: " + error);
        switch (reflexMinPhase)
        {
            case 0:
                ReflexMinCheck(Convert.ToBoolean(read("Available")) && binding != null, "armed native session before minimization");
                reflexMinForm = MyVRage.Platform.Windows.Window as Form;
                ReflexMinCheck(reflexMinForm != null, "actual game window is the inspected Windows Forms surface");
                reflexMinForm.BeginInvoke((Action)(() => {
                    try {
                        if (!reflexMinForm.Visible) throw new InvalidOperationException("This scenario requires Start-Prepared -Visible.");
                        reflexOriginalWindowState = reflexMinForm.WindowState;Volatile.Write(ref reflexMinHandle, reflexMinForm.Handle);
                        reflexRestoreTimer = new System.Windows.Forms.Timer { Interval = 3500 };
                        reflexRestoreTimer.Tick += (sender, args) => RestoreReflexWindow();
                        reflexRestoreTimer.Start();reflexMinChanged = true;reflexMinForm.WindowState = FormWindowState.Minimized;
                    } catch (Exception ex) { Volatile.Write(ref reflexMinError, ex.Message);RestoreReflexWindow(); }
                }));
                reflexMinDeadline = world + 12;reflexMinPhase = 1;return;
            case 1:
                if (Volatile.Read(ref reflexMinHandle) != IntPtr.Zero && IsIconic(Volatile.Read(ref reflexMinHandle)) && binding != null)
                    reflexMinSamples.Add(new MinimizedSample { At = world, Epoch = (ulong)binding[6], Sleeps = (ulong)binding[7] });
                if (Volatile.Read(ref reflexMinRestored) == 0 && world < reflexMinDeadline) return;
                ReflexMinCheck(Volatile.Read(ref reflexMinRestored) == 1 && !IsIconic(reflexMinHandle), "UI timer restored the actual minimized game window");
                ReflexMinCheck(reflexMinSamples.Count >= 2, "independent iconic-window and native-counter samples were observed");
                var samples = reflexMinSamples.Where(s => s.Epoch == reflexMinSamples.Last().Epoch).ToArray();
                ReflexMinCheck(samples.Length >= 2 && samples.Last().At - samples.First().At >= 1 && samples.Select(s => s.Sleeps).Distinct().Count() == 1,
                    "native sleep count stays constant across at least one second of actual minimization");
                reflexMinResumeEpoch = binding == null ? 0 : (ulong)binding[6];
                reflexMinResumeSleeps = binding == null ? 0 : (ulong)binding[7];reflexMinResumeMarkers = binding == null ? 0 : (ulong)binding[8];
                reflexMinPhase = 2;reflexMinNext = world + 3;reflexMinDeadline = world + 12;return;
            case 2:
                if (binding != null && (ulong)binding[6] != reflexMinResumeEpoch && world < reflexMinDeadline)
                { reflexMinResumeEpoch = (ulong)binding[6];reflexMinResumeSleeps = (ulong)binding[7];reflexMinResumeMarkers = (ulong)binding[8];return; }
                bool resumed = !IsIconic(reflexMinHandle) && Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 1 &&
                    binding != null && (ulong)binding[7] > reflexMinResumeSleeps && (ulong)binding[8] > reflexMinResumeMarkers;
                if (!resumed && world < reflexMinDeadline) return;
                ReflexMinCheck(resumed, "restored window resumes native On with advancing sleep and marker counts");
                Capture("reflex-minimize-restored");
                File.WriteAllText(Path.Combine(reports, "reflex-minimize-passed.txt"), "Actual iconic-window observations, stopped sleep progression and restored native On verified. Capture requires visual review.");
                reflexMinPhase = 3;break;
        }
    }
}
