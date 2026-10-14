using System.Reflection;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif
    
namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "Vulkan";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

        var harmony = new Harmony(Name);
        harmony.PatchCategory(Assembly.GetExecutingAssembly(), "Runtime");
        VulkanDiagnostics.Set("Bootstrap", BackendRuntime.HookReady ? SubsystemState.Ready : SubsystemState.Unavailable, BackendRuntime.SelectionSummary);
    }

    public void Dispose()
    {
        ConfigStorage.Flush();
        Instance = null;
    }

    public void Update()
    {
        VulkanDiagnostics.Refresh();
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }

}
