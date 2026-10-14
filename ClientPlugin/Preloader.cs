using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ClientPlugin.Bootstrap;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Pulsar discovers this exact namespace-free name before game assemblies are loaded.
public sealed class Preloader
{
    public static IEnumerable<string> TargetDLLs { get; } = new[] { "SpaceEngineers.exe" };
    public const string SupportedInitializeRenderSha256 = "c9da608669d96f7384271f9f25c336eeaa34cef2476624cf9eae7dde0bc74185";
    private const string Stamp = "SE_Vulkan_Factory_ABI_1";
    public static void Initialize()
    {
        // Never condition this on user configuration: that path exists only once the game initializes its filesystem.
        // The factory is permanent while enabled; backend choice is read on every launch.
        if (!PulsarCompatibility.DisableLazyCache())
            BackendRuntime.SetHook(false, "Not inspected", "Pulsar LazyPreload flag could not be disabled; selection will remain DX11.");
    }
    public static void Patch(AssemblyDefinition assembly)
    {
        try
        {
            var program = assembly.MainModule.GetType("SpaceEngineers.MyProgram");
            var method = program?.Methods.SingleOrDefault(m => m.Name == "InitializeRender" && m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void");
            if (method == null || !method.HasBody) { BackendRuntime.SetHook(false, "Missing", "Unsupported MyProgram.InitializeRender signature."); return; }
            if (program.Fields.Any(f => f.Name == Stamp))
            {
                bool valid = method.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.DeclaringType.FullName == "ClientPlugin.Bootstrap.BackendFactory" && m.Name == "Create") == 1;
                BackendRuntime.SetHook(valid, "ABI 1 stamped", valid ? "" : "Invalid existing Vulkan factory stamp."); return;
            }
            string hash = Fingerprint(method);
            if (hash != SupportedInitializeRenderSha256)
            {
                BackendRuntime.SetHook(false, hash, "Game startup IL changed or another plugin patched it; DX11 preserved. Expected " + SupportedInitializeRenderSha256 + "."); return;
            }
            var construction = method.Body.Instructions.SingleOrDefault(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "VRageRender.MyDX11Render");
            var constructor = construction?.Operand as MethodReference;
            var rendererField = program.Fields.SingleOrDefault(f => f.Name == "m_renderer" && f.FieldType.FullName == "VRageRender.IMyRender");
            if (constructor == null || constructor.Parameters.Count != 1 || rendererField == null)
            { BackendRuntime.SetHook(false, hash, "DX11 construction contract changed; DX11 preserved."); return; }
            var name = typeof(Preloader).Assembly.GetName();
            var scope = assembly.MainModule.AssemblyReferences.FirstOrDefault(a => a.Name == name.Name);
            if (scope == null)
            {
                scope = new AssemblyNameReference(name.Name, name.Version) { PublicKeyToken = name.GetPublicKeyToken() };
                assembly.MainModule.AssemblyReferences.Add(scope);
            }
            var factoryType = new TypeReference("ClientPlugin.Bootstrap", "BackendFactory", assembly.MainModule, scope);
            var factory = new MethodReference("Create", rendererField.FieldType, factoryType) { HasThis = false };
            factory.Parameters.Add(new ParameterDefinition(constructor.Parameters[0].ParameterType));
            // Replacing this one stack-compatible instruction preserves all settings, dedicated/null paths and Initialize ordering.
            construction.OpCode = OpCodes.Call; construction.Operand = factory;
            program.Fields.Add(new FieldDefinition(Stamp, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, assembly.MainModule.TypeSystem.Int32) { Constant = 1 });
            BackendRuntime.SetHook(true, hash, "");
        }
        catch (Exception error) { BackendRuntime.SetHook(false, "Inspection failed", "Vulkan prepatch inspection failed: " + error.Message); }
    }
    public static string Fingerprint(MethodDefinition method, bool exactFloatBits = false)
    {
        var instructions = method.Body.Instructions;
        var lines = new List<string>();
        foreach (var instruction in instructions)
        {
            string operand;
            switch (instruction.Operand)
            {
                case null: operand = ""; break;
                case Instruction target: operand = "@" + instructions.IndexOf(target); break;
                case Instruction[] targets: operand = string.Join(",", targets.Select(t => "@" + instructions.IndexOf(t))); break;
                case VariableDefinition variable: operand = "v" + variable.Index; break;
                case ParameterDefinition parameter: operand = "p" + parameter.Index; break;
                case float value when exactFloatBits: operand = "f32:"+BitConverter.ToUInt32(BitConverter.GetBytes(value),0).ToString("x8",CultureInfo.InvariantCulture); break;
                case double value when exactFloatBits: operand = "f64:"+BitConverter.ToUInt64(BitConverter.GetBytes(value),0).ToString("x16",CultureInfo.InvariantCulture); break;
                case MemberReference member: operand = member.FullName; break;
                default: operand = Convert.ToString(instruction.Operand, CultureInfo.InvariantCulture); break;
            }
            lines.Add(instruction.OpCode.Code + ":" + operand);
        }
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "").ToLowerInvariant();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Finish()
    {
        // Pulsar calls Finish before MyProgram.Main initializes MyVRage.Platform.
        // Harmony target preparation can run game static constructors (including
        // MyRenderSettings). Install the runtime hooks in the injected factory,
        // once the original InitializeRender has resolved its settings instead.
    }
}
