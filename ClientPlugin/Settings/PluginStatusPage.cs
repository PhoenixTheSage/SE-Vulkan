using System;
using System.Reflection;

namespace ClientPlugin;

/// <summary>Renderer-neutral status navigation, shared by config and optional Rich HUD.</summary>
internal static class PluginStatusPage
{
    const string RegistryName = "ClientPlugin.RichHud.TerminalConfigRegistry";
    static object page;
    static Type registry;
    static string owner;
    static DateTime nextAttempt;
    public static string Status { get; private set; } = "waiting for Anomaly terminal";

    public static string RendererLine
    {
        get
        {
            var route = Find("ClientPlugin.Backend.RendererRoute");
            try { return "Renderer: " + (route?.GetProperty("ActiveName")?.GetValue(null) as string ?? "Anomaly route unavailable"); }
            catch (Exception e) { return "Renderer: unreadable (" + e.GetBaseException().Message + ")"; }
        }
    }

    public static bool TryInstall(string folder, Action show, Action<object> extra = null)
    {
        if (page != null) return true;
        if (DateTime.UtcNow < nextAttempt) return false;
        nextAttempt = DateTime.UtcNow.AddSeconds(1);
        try
        {
            registry = registry ?? Find(RegistryName);
            if (registry == null) return false;
            var request = registry.GetMethod("RequestFolderPage", new[] { typeof(string), typeof(string) });
            var candidate = request != null
                ? request.Invoke(null, new object[] { folder, "Status" })
                : registry.GetMethod("RequestPage", new[] { typeof(string) })?.Invoke(null, new object[] { folder + " Status" });
            if (candidate == null) return false;
            owner = request != null ? folder + "/Status" : folder + " Status";
            Call(candidate, "Category", "Status");
            AddButton(candidate, "Show Status", show, "Open diagnostics for the active renderer.");
            extra?.Invoke(candidate);
            page = candidate;
            Status = "registered: " + owner;
            return true;
        }
        catch (Exception e)
        {
            Status = "status page: " + e.GetBaseException().Message;
            try { if (owner != null) registry?.GetMethod("UnregisterPage")?.Invoke(null, new object[] { owner }); }
            catch { }
            return false;
        }
    }

    public static void AddButton(object target, string label, Action action, string description) =>
        Call(target, "Button", label, action, description);

    public static void Refresh()
    {
        if (page == null) return;
        try { Call(page, "Refresh"); } catch { }
    }

    public static string ProgramStatus(string id)
    {
        try
        {
            var type = Find("ClientPlugin.Shaders.FullscreenPassRegistry");
            var method = type?.GetMethod("TryGetProgramStatus", new[] { typeof(string), typeof(string).MakeByRefType() });
            if (method == null) return "Anomaly program registry unavailable";
            var args = new object[] { id, null };
            return method.Invoke(null, args) is true ? args[1] as string ?? "no report" : "not registered";
        }
        catch (Exception e) { return e.GetBaseException().Message; }
    }

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { var type = assembly.GetType(name, false); if (type != null) return type; }
            catch { }
        }
        return null;
    }

    static void Call(object target, string name, params object[] args)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != name || method.IsGenericMethodDefinition) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != args.Length) continue;
            bool match = true;
            for (int i = 0; i < args.Length; i++)
                if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i])) { match = false; break; }
            if (!match) continue;
            method.Invoke(target, args);
            return;
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }
}
