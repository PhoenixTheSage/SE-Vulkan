using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ClientPlugin.Bootstrap;

public static class PulsarCompatibility
{
    // Ids stay here only for assemblies that reference VRage.Render11 or SharpDX
    // and do not publish the dual-renderer handshake. A handshake pack may carry
    // those references for its closed DX11 path.
    private static readonly HashSet<string> KnownConsumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static bool DisableLazyCache()
    {
        try
        {
            Type flags = FindType("Pulsar.Shared.Arguments.Flags");
            var current = flags?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var lazy = flags?.GetProperty("LazyPreload", BindingFlags.Public | BindingFlags.Instance);
            var setter = lazy?.GetSetMethod(true);
            if (current == null || setter == null) return false;
            setter.Invoke(current, new object[] { false });
            BackendRuntime.LazyCacheDisabled = !(bool)lazy.GetValue(current);
            return BackendRuntime.LazyCacheDisabled;
        }
        catch (Exception error) { BackendRuntime.RecordFailure("Pulsar lazy-preload guard failed: " + error.Message); return false; }
    }
    public static bool Check(out string reason)
    {
        try
        {
            var loaderType = FindType("Pulsar.Shared.Loader");
            var instance = loaderType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var entries = loaderType?.GetField("Plugins", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as IEnumerable;
            if (instance == null || entries == null) { reason = "Pulsar enabled-plugin inventory unavailable; Vulkan startup disabled."; return false; }
            var conflicts = new List<string>();
            var dual = new List<string>();
            foreach (object entry in entries)
            {
                var type = entry.GetType();
                var metadata = type.GetProperty("Key")?.GetValue(entry);
                var assembly = type.GetProperty("Value")?.GetValue(entry) as Assembly;
                if (assembly == null || assembly == typeof(PulsarCompatibility).Assembly) continue;
                string id = Read(metadata, "Id");
                string repo = Read(metadata, "RepoId");
                string name = Read(metadata, "FriendlyName");
                if (IsLinuxCompat(assembly, id, repo) || RuntimePluginContract.IsDotNetCompat(assembly, id, repo)) continue;
                // A Render11 or SharpDX reference is the closed DX11 path when the
                // assembly publishes Anomaly's renderer handshake. Without that
                // handshake the same reference still keeps startup on DX11.
                bool known = KnownConsumers.Contains(id) || KnownConsumers.Contains(repo);
                bool dx11Reference = assembly.GetReferencedAssemblies().Any(a =>
                    a.Name == "VRage.Render11" || a.Name.StartsWith("SharpDX", StringComparison.OrdinalIgnoreCase));
                if (!known && !dx11Reference) continue;
                string label = (string.IsNullOrEmpty(name) ? assembly.GetName().Name : name) + " [" + id + "]";
                if (RuntimePluginContract.IsDualRenderer(assembly)) dual.Add(label);
                else conflicts.Add(label);
            }
            reason = conflicts.Count == 0
                ? dual.Count == 0
                    ? "Enabled plugin inventory checked: no DX11 consumers."
                    : "Enabled plugin inventory checked: dual-renderer handshake accepted for " + string.Join(", ", dual) + "."
                : "Incompatible DX11 graphics plugins: " + string.Join(", ", conflicts);
            BackendRuntime.CompatibilitySummary = reason;
            return conflicts.Count == 0;
        }
        catch (Exception error) { reason = "Enabled plugin inventory failed: " + error.Message; BackendRuntime.CompatibilitySummary = reason; return false; }
    }
    public static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        { var type = assembly.GetType(fullName, false); if (type != null) return type; }
        return null;
    }
    public static Assembly FindLinuxCompatAssembly()
    {
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            a.GetType("ClientPlugin.Compatibility.SdlRenderThread", false) != null &&
            a.GetType("ClientPlugin.Compatibility.SdlGameWindow", false) != null);
    }
    private static string Read(object value, string property)
    {
        if (value == null) return "";
        Type type = value.GetType();
        // Preserve inventory labels from both launcher metadata contracts.
        return (type.GetProperty(property)?.GetValue(value) ?? type.GetField(property)?.GetValue(value)) as string ?? "";
    }
    private static bool IsLinuxCompat(Assembly assembly, string id, string repo) =>
        id == "linux-compat" || repo == "CometWorks/linux-compat" || assembly.GetType("ClientPlugin.Compatibility.SdlRenderThread", false) != null;
}
