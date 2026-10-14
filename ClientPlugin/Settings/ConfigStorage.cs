using System;
using System.Threading;
using ClientPlugin.Bootstrap;
using VRage.FileSystem;
using VRage.Utils;

namespace ClientPlugin.Settings;

public static class ConfigStorage
{
    private static readonly object Sync = new object();
    private static readonly Timer Writer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    private static BackendConfiguration pending;
    public static Config Load()
    {
        BackendRuntime.InitializeConfiguration(MyFileSystem.UserDataPath);
        var config = new Config(); config.ReplaceValues(BackendRuntime.Configuration); return config;
    }
    public static void ScheduleSave(Config config)
    {
        lock (Sync) { pending = config.Snapshot(); Writer.Change(500, Timeout.Infinite); }
    }
    public static void Save(Config config) { ScheduleSave(config); Flush(); }
    public static void Flush()
    {
        lock (Sync)
        {
            if (pending == null) return;
            try { BackendRuntime.SaveConfiguration(pending); pending = null; }
            catch (Exception error)
            {
                BackendRuntime.RecordFailure("Configuration save failed: " + error.Message);
                try { MyLog.Default?.Warning("Vulkan configuration save failed: " + error.Message); } catch { }
            }
        }
    }
    public static void CommitRenderer(RendererBackend backend)
    {
        // Flush older dialog values before Display selection, so a worker cannot overwrite it.
        lock (Sync)
        {
            Flush(); BackendRuntime.RequestBackend(backend); Config.Current.ReplaceValues(BackendRuntime.Configuration);
        }
    }
}
