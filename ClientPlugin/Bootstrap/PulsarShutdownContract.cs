using System;
using System.Linq;
using System.Reflection;

namespace ClientPlugin.Bootstrap;

internal static class PulsarShutdownContract
{
    // Both installed Legacy and Interim use this exact force-kill prefix. An
    // older launcher without the hook can use the game's original shutdown.
    internal static MethodInfo GetPrefix(Type type)
    {
        if (type == null) return null;
        MethodInfo[] candidates = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(method => method.Name == "Prefix").ToArray();
        if (candidates.Length != 1 || !candidates[0].IsPublic || !candidates[0].IsStatic ||
            candidates[0].ReturnType != typeof(bool) || candidates[0].ContainsGenericParameters || candidates[0].GetParameters().Length != 0)
            throw new InvalidOperationException("Pulsar's ExitThreadSafe force-kill hook has an unsupported signature; orderly Vulkan shutdown cannot be installed.");
        return candidates[0];
    }
}
