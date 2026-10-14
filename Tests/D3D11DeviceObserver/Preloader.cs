using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using D3D11DeviceObserver;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Pulsar requires this exact namespace-free type name.
public sealed class Preloader
{
    public static IEnumerable<string> TargetDLLs { get; } = new[] { "SharpDX.Direct3D11.dll", "SpaceEngineers.exe" };
    private const string Stamp = "SharpDX.Direct3D11.SE_Vulkan_D3D11_Observer_1";
    private static readonly HashSet<string> Entries = new HashSet<string> { "D3D11CreateDevice", "D3D11CreateDeviceAndSwapChain", "D3D11On12CreateDevice" };
    public static void Initialize() => Observer.Initialize(DisableLazyCache());
    public static void Finish() => Observer.Milestone("preloader-finish");
    private static bool DisableLazyCache()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var flags = assembly.GetType("Pulsar.Shared.Arguments.Flags", false);
            if (flags == null) continue;
            try
            {
                var current = flags.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var property = flags.GetProperty("LazyPreload", BindingFlags.Public | BindingFlags.Instance);
                var setter = property?.GetSetMethod(true);
                if (current == null || setter == null) return false;
                setter.Invoke(current, new object[] { false }); return !(bool)property.GetValue(current);
            }
            catch { return false; }
        }
        return false;
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; }
    }
    public static string Fingerprint(MethodDefinition method)
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
                case MemberReference member: operand = member.FullName; break;
                default: operand = Convert.ToString(instruction.Operand, CultureInfo.InvariantCulture); break;
            }
            lines.Add(instruction.OpCode.Code + ":" + operand);
        }
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "").ToLowerInvariant();
    }
    public static void Patch(AssemblyDefinition assembly)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SE_VULKAN_D3D11_REPORT_DIR"))) return;
        try
        {
            if (assembly.Name.Name == "SharpDX.Direct3D11") PatchSharpDx(assembly);
            else if (assembly.Name.Name == "SpaceEngineers") PatchRenderMilestone(assembly);
        }
        catch (Exception error) { Observer.Failed("Prepatch " + assembly.Name.Name + ": " + error); }
    }
    private static void PatchSharpDx(AssemblyDefinition assembly)
    {
        var module = assembly.MainModule;
        if (module.GetType(Stamp) != null) { Observer.Failed("Observer stamp already present; use a clean isolated preloader cache."); return; }
        var methods = AllTypes(module.Types).SelectMany(t => t.Methods).ToArray();
        var imports = methods.Where(m => m.PInvokeInfo != null && Entries.Contains(m.PInvokeInfo.EntryPoint)).ToArray();
        var calls = methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference)
            .Select(i => new { Method = m, Instruction = i, Import = imports.FirstOrDefault(p => p.FullName == ((MethodReference)i.Operand).FullName) }))
            .Where(c => c.Import != null).ToArray();
        if (!imports.Any(m => m.PInvokeInfo.EntryPoint == "D3D11CreateDevice") || calls.Length == 0 || imports.Any(m => !calls.Any(c => c.Import == m)))
            throw new InvalidOperationException("Native device import inventory is incomplete; no negative evidence allowed.");
        var attempt = module.ImportReference(typeof(Observer).GetMethod(nameof(Observer.Attempt)));
        var result = module.ImportReference(typeof(Observer).GetMethod(nameof(Observer.Result)));
        var details = new List<string> { "imports=" + string.Join(",", imports.Select(i => i.PInvokeInfo.EntryPoint)) };
        details.Add("D3D11CreateDeviceAndSwapChain-import=" + imports.Any(i => i.PInvokeInfo.EntryPoint == "D3D11CreateDeviceAndSwapChain"));
        foreach (var group in calls.GroupBy(c => c.Method))
        {
            details.Add(group.Key.FullName + " sha256=" + Fingerprint(group.Key));
            foreach (var call in group)
            {
                if (call.Import.ReturnType.FullName != "System.Int32") throw new InvalidOperationException("Unsupported device creation HRESULT type.");
                string entry = call.Import.PInvokeInfo.EntryPoint;
                var il = call.Method.Body.GetILProcessor();
                // Native arguments already on the evaluation stack are preserved.
                il.InsertBefore(call.Instruction, il.Create(OpCodes.Ldstr, entry));
                il.InsertBefore(call.Instruction, il.Create(OpCodes.Call, attempt));
                var duplicate = il.Create(OpCodes.Dup); il.InsertAfter(call.Instruction, duplicate);
                var label = il.Create(OpCodes.Ldstr, entry); il.InsertAfter(duplicate, label);
                il.InsertAfter(label, il.Create(OpCodes.Call, result));
                call.Method.Body.MaxStackSize += 2;
            }
        }
        module.Types.Add(new TypeDefinition("SharpDX.Direct3D11", "SE_Vulkan_D3D11_Observer_1", Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object));
        Observer.Patched(assembly.Name.Name, string.Join("; ", details), calls.Length);
    }
    private static void PatchRenderMilestone(AssemblyDefinition assembly)
    {
        var program = assembly.MainModule.GetType("SpaceEngineers.MyProgram");
        var calls = program.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference reference && reference.Name == "InitializeRender" && reference.DeclaringType.FullName == program.FullName).Select(i => new { Method = m, Instruction = i })).ToArray();
        if (calls.Length != 1) throw new InvalidOperationException("Expected one MyProgram.InitializeRender call site.");
        var call = calls[0]; var il = call.Method.Body.GetILProcessor();
        var milestone = assembly.MainModule.ImportReference(typeof(Observer).GetMethod(nameof(Observer.Milestone)));
        il.InsertBefore(call.Instruction, il.Create(OpCodes.Ldstr, "before-render-initialization")); il.InsertBefore(call.Instruction, il.Create(OpCodes.Call, milestone));
        var label = il.Create(OpCodes.Ldstr, "after-render-initialization"); il.InsertAfter(call.Instruction, label); il.InsertAfter(label, il.Create(OpCodes.Call, milestone));
        call.Method.Body.MaxStackSize += 1;
        Observer.Patched(assembly.Name.Name, "InitializeRender caller=" + call.Method.FullName + "; InitializeRender body unchanged", 0);
    }
}
