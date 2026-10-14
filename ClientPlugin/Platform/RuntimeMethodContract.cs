using System;
using System.Linq;
using System.Reflection;

namespace ClientPlugin.Platform;

internal static class RuntimeMethodContract
{
    internal static MethodInfo FindVoidMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
    {
        if (type == null) return null;
        var candidates = type.GetMethods(flags).Where(method => method.Name == name && !method.ContainsGenericParameters &&
            method.ReturnType == typeof(void) && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
