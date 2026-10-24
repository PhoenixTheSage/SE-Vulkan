using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

public sealed class Config : INotifyPropertyChanged
{
    private BackendConfiguration values = new BackendConfiguration();
    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();
    [XmlIgnore] public string Title => "Vulkan renderer";
    // Renderer choice belongs to the transactional Display screen, not this live config dialog.
    public RendererBackend RequestedBackend
    {
        get => values.RequestedBackend;
        set { if (values.RequestedBackend == value) return; values.RequestedBackend = value; Changed(); }
    }
    [Checkbox("Enable validation", description: "Enables available Vulkan validation layers at the next game restart.")]
    public bool EnableValidation
    {
        get => values.EnableValidation;
        set { if (values.EnableValidation == value) return; values.EnableValidation = value; Changed(); }
    }
    public bool RecoveryRequired { get => values.RecoveryRequired; set => values.RecoveryRequired = value; }
    public long RetryGeneration { get => values.RetryGeneration; set => values.RetryGeneration = value; }
    public string LastFailure { get => values.LastFailure; set => values.LastFailure = value; }
    [Button("Show Status", "Show bootstrap, Vulkan layers and visual-system coverage.")]
    public void ShowStatus() => StatusScreen.Show();
    [Button("Show Profiler", "Show frame time. Record keeps a 10 second gameplay average; turn it off to drop that history. Live samples the current frame, including while this menu is open.")]
    public void ShowProfiler() => ProfilerScreen.Show();
    [Button("Write report to log", "Write the same status snapshot into the game log.")]
    public void WriteReport() => VulkanDiagnostics.WriteReportToLog();
    [Button("Retry Vulkan next launch", "Clear DX11 recovery now instead of waiting for the main-menu prompt or a driver/plugin update; Vulkan is used after restarting.")]
    public void RetryVulkan()
    {
        if (RequestedBackend != RendererBackend.Vulkan || !BackendRuntime.RecoveryRequired || BackendRuntime.ActiveBackend == RendererBackend.Vulkan) return;
        ConfigStorage.Flush(); BackendRuntime.RetryVulkan(); ReplaceValues(BackendRuntime.Configuration);
    }
    internal BackendConfiguration Snapshot() => values.Copy();
    internal void ReplaceValues(BackendConfiguration configuration) => values = configuration.Copy();
    public event PropertyChangedEventHandler PropertyChanged;
    private void Changed([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        ConfigStorage.ScheduleSave(this);
    }
}
