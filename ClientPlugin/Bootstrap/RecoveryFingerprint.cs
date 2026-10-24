using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClientPlugin.Bootstrap;

// What a DX11 fallback was decided against: this plugin build, the enabled plugin set and the display drivers.
// Any change gets Vulkan another try at the next launch.
public static class RecoveryFingerprint
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    public static string Compute()
    {
        var parts = new List<string> { "plugin:" + typeof(RecoveryFingerprint).Assembly.ManifestModule.ModuleVersionId };
        try { parts.AddRange(EnabledPlugins()); } catch (Exception error) { BackendRuntime.RecordFailure("Recovery fingerprint plugin inventory failed: " + error.Message); }
        try { parts.AddRange(DisplayDrivers()); } catch (Exception error) { BackendRuntime.RecordFailure("Recovery fingerprint driver inventory failed: " + error.Message); }
        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }
    private static IEnumerable<string> EnabledPlugins()
    {
        var loaderType = PulsarCompatibility.FindType("Pulsar.Shared.Loader");
        var instance = loaderType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (!(loaderType?.GetField("Plugins", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is IEnumerable entries)) yield break;
        foreach (object entry in entries)
            if (entry.GetType().GetProperty("Value")?.GetValue(entry) is Assembly assembly && assembly != typeof(RecoveryFingerprint).Assembly)
                yield return "mod:" + assembly.ManifestModule.ModuleVersionId;
    }
    private static List<string> DisplayDrivers()
    {
        var drivers = new List<string>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return drivers;
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (root == null) return drivers;
        foreach (string name in root.GetSubKeyNames())
        {
            // The class key also holds an access-restricted "Properties" subkey.
            try
            {
                using var adapter = root.OpenSubKey(name);
                if (adapter?.GetValue("DriverVersion") is string version)
                    drivers.Add("gpu:" + adapter.GetValue("DriverDesc") + "=" + version);
            }
            catch (System.Security.SecurityException) { }
        }
        return drivers;
    }
}
