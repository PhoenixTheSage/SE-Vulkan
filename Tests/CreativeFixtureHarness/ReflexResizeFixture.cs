using System;
using System.IO;
using HarmonyLib;
using Sandbox.Engine.Platform.VideoMode;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRageMath;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    int reflexResizePhase;
    double reflexResizeNext, reflexResizeDeadline;
    Vector2I reflexOriginalSize;
    ulong reflexResizeEpoch;
    ulong reflexResizeProgressEpoch, reflexResizeSleeps, reflexResizeMarkers;
    MyGuiScreenOptionsDisplay reflexDisplay;
    void ReflexResizeCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-resize-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + reflexResizePhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex resize: " + evidence);
    }
    void OpenReflexDisplay()
    {
        reflexDisplay = new MyGuiScreenOptionsDisplay();MyGuiSandbox.AddScreen(reflexDisplay);
    }
    void ApplyReflexSize(Vector2I size)
    {
        AccessTools.Method(typeof(MyGuiScreenOptionsDisplay), "SelectResolution").Invoke(reflexDisplay, new object[] { size });
        var selected = (Vector2I)AccessTools.Method(typeof(MyGuiScreenOptionsDisplay), "GetSelectedResolution").Invoke(reflexDisplay, null);
        ReflexResizeCheck(selected == size, "display resolution widget selected " + selected);
        reflexDisplay.OnOkClick(null);
    }
    void ConfirmReflexSize()
    {
        bool waiting = (bool)AccessTools.Field(typeof(MyGuiScreenOptionsDisplay), "m_waitingForConfirmation").GetValue(reflexDisplay);
        ReflexResizeCheck(waiting, "actual video settings applied and requested confirmation");
        reflexDisplay.OnMessageBoxCallback(MyGuiScreenMessageBox.ResultEnum.YES);
        MyScreenManager.GetFirstScreenOfType<MyGuiScreenMessageBox>()?.CloseScreenNow();
    }
    void ExerciseReflexResize(double world, object[] binding, Func<string, object> read)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_RESIZE") != "1" || world < 52 || world < reflexResizeNext || reflexResizePhase >= 8) return;
        var settings = MyVideoSettingsManager.CurrentDeviceSettings;
        var size = new Vector2I(settings.BackBufferWidth, settings.BackBufferHeight);
        switch (reflexResizePhase)
        {
            case 0:
                ReflexResizeCheck(binding != null && Convert.ToBoolean(read("Available")), "armed native session before resize");
                reflexOriginalSize = size;reflexResizeEpoch = (ulong)binding[6];
                ReflexResizeCheck(size != new Vector2I(800, 600), "fixture begins at a different resolution");
                OpenReflexDisplay();break;
            case 1:
                ApplyReflexSize(new Vector2I(800, 600));break;
            case 2:
                ConfirmReflexSize();reflexResizeDeadline = world + 12;RememberReflexProgress(binding);break;
            case 3:
            case 7:
                var expected = reflexResizePhase == 3 ? new Vector2I(800, 600) : reflexOriginalSize;
                if (binding != null && (ulong)binding[6] != reflexResizeProgressEpoch && world < reflexResizeDeadline)
                { RememberReflexProgress(binding);return; }
                bool ready = size == expected && Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 1 &&
                    binding != null && (bool)binding[3] && (ulong)binding[7] > reflexResizeSleeps && (ulong)binding[8] > reflexResizeMarkers;
                if (!ready && world < reflexResizeDeadline) return;
                ReflexResizeCheck(ready, "renderer resumed On at " + expected + "; actual " + size);
                if (expectedBackend == ClientPlugin.Bootstrap.RendererBackend.Vulkan)
                    ReflexResizeCheck((ulong)binding[6] > reflexResizeEpoch, "Vulkan replacement swapchain has a fresh Reflex epoch");
                reflexResizeEpoch = (ulong)binding[6];
                Capture(reflexResizePhase == 3 ? "reflex-resize-expanded" : "reflex-resize-restored");
                if (reflexResizePhase == 7)
                    File.WriteAllText(Path.Combine(reports, "reflex-resize-passed.txt"), "Actual Display widget/OK/confirmation resized and restored; armed native On resumed with progressing sleep/marker counters. Captures require visual review.");
                break;
            case 4:
                OpenReflexDisplay();break;
            case 5:
                ApplyReflexSize(reflexOriginalSize);break;
            case 6:
                ConfirmReflexSize();reflexResizeDeadline = world + 12;RememberReflexProgress(binding);break;
        }
        reflexResizePhase++;reflexResizeNext = world + 2;
    }
    void RememberReflexProgress(object[] binding)
    {
        reflexResizeProgressEpoch = binding == null ? 0 : (ulong)binding[6];
        reflexResizeSleeps = binding == null ? 0 : (ulong)binding[7];
        reflexResizeMarkers = binding == null ? 0 : (ulong)binding[8];
    }
}
