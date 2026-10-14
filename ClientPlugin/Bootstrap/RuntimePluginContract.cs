using System;
using System.Reflection;

namespace ClientPlugin.Bootstrap;

internal static class RuntimePluginContract
{
    internal const string RendererRouteTypeName = "ClientPlugin.Backend.RendererRoute";
    internal static bool IsDotNetCompat(Assembly assembly, string id, string repo)
    {
        if (!string.Equals(id, "dotnet-compat", StringComparison.Ordinal) &&
            !string.Equals(id, "se-dotnet-compat", StringComparison.Ordinal) &&
            !string.Equals(repo, "CometWorks/dotnet-compat", StringComparison.Ordinal)) return false;
        // Pulsar's required net10 compatibility core references Render11 while
        // fixing net48 DDS/XML behavior. This verified contract is independent
        // of the selected renderer and is necessary to launch the stock game.
        var identity = assembly.GetType("ClientPlugin.Plugin", false)?.GetField("Name", BindingFlags.Public | BindingFlags.Static);
        return identity != null && identity.IsLiteral && Equals(identity.GetRawConstantValue(), "DotNetCompat") &&
            assembly.GetType("Shared.Patches.ImageProcessing.DecodePixelDataPrepatch", false) != null &&
            assembly.GetType("Shared.Patches.Serialization.XmlSerializationPrepatch", false) != null &&
            assembly.GetType("Preloader", false) != null;
    }

    // Packs that run on both renderers publish this handshake and keep SharpDX
    // and VRage.Render11 behind AllowDx11Device. The properties are not invoked:
    // startup only checks that the DX11 path is declared closed unless the route
    // says DX11, and that guest work is declared separately.
    internal static bool IsDualRenderer(Assembly assembly)
    {
        if (assembly == null) return false;
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { types = error.Types; }
        catch (Exception) { return false; }
        if (types == null) return false;
        foreach (Type type in types)
        {
            if (type == null) continue;
            var route = type.GetField("RouteTypeName", BindingFlags.Public | BindingFlags.Static);
            if (route == null || !route.IsLiteral || !Equals(route.GetRawConstantValue(), RendererRouteTypeName)) continue;
            if (IsStaticBool(type, "AllowDx11Device") && IsStaticBool(type, "AllowGuestWork")) return true;
        }
        return false;
    }

    private static bool IsStaticBool(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        return property != null && property.PropertyType == typeof(bool) && property.GetGetMethod() != null;
    }
}
