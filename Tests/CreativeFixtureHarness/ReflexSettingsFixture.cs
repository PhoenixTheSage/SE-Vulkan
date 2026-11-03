using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Sandbox.Graphics.GUI;

namespace CreativeFixtureHarness;

public sealed partial class Plugin
{
    int reflexUiPhase;
    double reflexUiNext;
    MyGuiScreenBase reflexUiScreen;
    MyGuiControlCombobox reflexUiMode;
    string reflexUiSaveStamp;
    string ReflexConfigPath => Path.Combine(userData, "Storage", "Reflex.cfg");
    string ReflexSaveStamp() => File.Exists(ReflexConfigPath)
        ? File.GetLastWriteTimeUtc(ReflexConfigPath).Ticks + "|" + File.ReadAllText(ReflexConfigPath) : "absent";
    void ReflexUiCheck(bool condition, string evidence)
    {
        File.AppendAllText(Path.Combine(reports, "reflex-settings-history.txt"), DateTime.UtcNow.ToString("u") +
            " phase=" + reflexUiPhase + " passed=" + condition + " " + evidence + "\n");
        if (!condition) throw new InvalidOperationException("Reflex settings: " + evidence);
    }
    void OpenReflexSettings()
    {
        var plugin = ReflexType("Reflex", "ClientPlugin.Plugin");
        var instance = plugin?.GetProperty("Instance")?.GetValue(null, null);
        if (instance == null) throw new InvalidOperationException("Reflex plugin instance missing");
        plugin.GetMethod("OpenConfigDialog").Invoke(instance, null);
        var generator = plugin.GetField("settingsGenerator", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
        reflexUiScreen = (MyGuiScreenBase)generator.GetType().GetProperty("Dialog").GetValue(generator, null);
    }
    void FindReflexDropdown()
    {
        var panel = reflexUiScreen.Controls.OfType<MyGuiControlScrollablePanel>().Single();
        var parent = (MyGuiControlParent)panel.ScrolledControl;
        reflexUiMode = parent.Controls.OfType<MyGuiControlCombobox>().Single(c => c.Name == "Mode");
        ReflexUiCheck(reflexUiScreen.GetFriendlyName() == "NVIDIA Reflex" && reflexUiMode.Visible && reflexUiMode.Enabled,
            "actual NVIDIA Reflex dialog and enabled Mode dropdown found");
    }
    void ExerciseReflexSettings(double world, Type config, Func<string, object> read)
    {
        if (Environment.GetEnvironmentVariable("SE_VULKAN_REFLEX_SETTINGS") != "1" || world < 52 || world < reflexUiNext || reflexUiPhase >= 11) return;
        if (config == null) throw new InvalidOperationException("Reflex config missing for settings exercise");
        object current = config.GetField("Current").GetValue(null);
        int mode = Convert.ToInt32(config.GetProperty("Mode").GetValue(current, null));
        switch (reflexUiPhase)
        {
            case 0:
                reflexUiSaveStamp = ReflexSaveStamp();OpenReflexSettings();break;
            case 1:
                FindReflexDropdown();reflexUiMode.SelectItemByKey(0);break;
            case 2:
                ReflexUiCheck(mode == 0 && Convert.ToInt32(read("RequestedMode")) == 0 && reflexUiMode.GetSelectedKey() == 0,
                    "Off widget selection reached Config and Anomaly request");
                ReflexUiCheck(ReflexSaveStamp() == reflexUiSaveStamp, "open dialog did not write config");
                Capture("reflex-settings-off");break;
            case 3:
                reflexUiMode.SelectItemByKey(2);break;
            case 4:
                ReflexUiCheck(mode == 2 && Convert.ToInt32(read("RequestedMode")) == 2 && reflexUiMode.GetSelectedKey() == 2,
                    "Boost widget selection reached Config and Anomaly request");
                ReflexUiCheck(ReflexSaveStamp() == reflexUiSaveStamp, "second open-dialog selection did not write config");
                Capture("reflex-settings-boost");break;
            case 5:
                reflexUiScreen.CloseScreenNow();break;
            case 6:
                VerifyReflexSaved(config, "OnWithBoost");OpenReflexSettings();break;
            case 7:
                FindReflexDropdown();
                ReflexUiCheck(reflexUiMode.GetSelectedKey() == 2, "reopened widget retains Boost");
                reflexUiMode.SelectItemByKey(1);break;
            case 8:
                ReflexUiCheck(mode == 1 && Convert.ToInt32(read("RequestedMode")) == 1 && reflexUiMode.GetSelectedKey() == 1,
                    "On widget selection reached Config and Anomaly request");
                Capture("reflex-settings-on");break;
            case 9:
                reflexUiScreen.CloseScreenNow();break;
            case 10:
                VerifyReflexSaved(config, "On");
                ReflexUiCheck(Convert.ToBoolean(read("Available")) && Convert.ToInt32(read("ConfiguredMode")) == 1,
                    "closed dialog resumes armed native On configuration");
                File.WriteAllText(Path.Combine(reports, "reflex-settings-passed.txt"), "Actual dropdown handler, request handoff, close-save/reload and resumed native On verified; captures require visual review.");
                break;
        }
        reflexUiPhase++;reflexUiNext = world + 2;
    }
    void VerifyReflexSaved(Type config, string expected)
    {
        var saved = XDocument.Load(ReflexConfigPath).Root.Element("Mode")?.Value;
        var storage = ReflexType("Reflex", "ClientPlugin.Settings.ConfigStorage");
        var loaded = storage.GetMethod("Load").Invoke(null, null);
        ReflexUiCheck(saved == expected && config.GetProperty("Mode").GetValue(loaded, null).ToString() == expected,
            "dialog close persisted and deserialized " + expected);
    }
}
