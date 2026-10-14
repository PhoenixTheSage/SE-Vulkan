using ClientPlugin.Bootstrap;

namespace ClientPlugin.Settings;

// Vulkan-only display choice. The game config keeps WindowMode = Fullscreen so DX11 can load it.
public static class ExclusiveFullscreenChoice
{
    private static bool? rollback;
    public static bool Enabled => BackendRuntime.Configuration.ExclusiveFullscreen;
    public static void Set(bool enabled)
    {
        ConfigStorage.Flush();
        var updated = BackendRuntime.Configuration;
        if (updated.ExclusiveFullscreen == enabled) return;
        updated.ExclusiveFullscreen = enabled;
        BackendRuntime.SaveConfiguration(updated);
        Config.Current.ReplaceValues(BackendRuntime.Configuration);
    }
    public static void Begin() => rollback = Enabled;
    public static void Keep() => rollback = null;
    public static void Rollback()
    {
        if (rollback == null) return;
        bool restore = rollback.Value;
        rollback = null;
        Set(restore);
    }
}
