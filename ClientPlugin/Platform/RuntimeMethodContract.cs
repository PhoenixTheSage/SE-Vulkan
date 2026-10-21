using System;
using System.Linq;
using System.Reflection;

namespace ClientPlugin.Platform;

internal static class RuntimeMethodContract
{
    internal static MethodInfo FindWindowModeMethod(Type type, Type modeType, Type boundsType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        return FindVoidMethod(type, "OnModeChanged", flags, modeType, typeof(int), typeof(int), boundsType, typeof(float)) ??
            FindVoidMethod(type, "OnModeChanged", flags, modeType, typeof(int), typeof(int), boundsType);
    }

    internal static MethodInfo FindVoidMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
    {
        if (type == null) return null;
        var candidates = type.GetMethods(flags).Where(method => method.Name == name && !method.ContainsGenericParameters &&
            method.ReturnType == typeof(void) && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
