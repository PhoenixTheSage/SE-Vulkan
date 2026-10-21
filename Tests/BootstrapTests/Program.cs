using System;
using System.IO;
using System.Linq;
using ClientPlugin.Bootstrap;
using ClientPlugin.Platform;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Bootstrap { public static class EarlyBootstrapHooks { public static int InstallCalls; public static void Install() { InstallCalls++; } } }
namespace Pulsar.Shared
{
    public sealed class CompatibilityTestMetadata
    {
        public string Id { get; set; }
        public string RepoId { get; set; }
        public string FriendlyName { get; set; }
    }
    public sealed class Loader
    {
        public static Loader Instance = new Loader();
        public System.Collections.Generic.Dictionary<CompatibilityTestMetadata, System.Reflection.Assembly> Plugins = new();
    }
}

internal static class Program
{
    private static class ValidShutdown { public static bool Prefix() => false; }
    private static class UnsupportedShutdown { public static void Prefix() { } }
    private static class MissingShutdown { }
    private static class ValidModeForwarder { public static void Forward(int? settings) { } public static void DriveDirect(int settings) { } }
    private static class WrongModeForwarder { public static void Forward(int settings) { } public static bool DriveDirect(int settings) => true; }
    private static class GenericInvoker { internal static void Invoke<T>(Action action) { } }
    private sealed class LegacyWindow { public void OnModeChanged(byte mode, int width, int height, int bounds) { } }
    private sealed class RefreshWindow { public float Refresh; public void OnModeChanged(byte mode, int width, int height, int bounds, float refresh) { Refresh = refresh; } }
    private sealed class ChangedWindow { public void OnModeChanged(byte mode, int width, int height, int bounds, double refresh) { } }
    private static int checks;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static int Main(string[] args)
    {
        string temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SE-Vulkan-bootstrap-tests-" + Guid.NewGuid().ToString("N")));
        try
        {
            var staticPublic = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            Check(RuntimeMethodContract.FindWindowModeMethod(typeof(LegacyWindow), typeof(byte), typeof(int))?.GetParameters().Length == 4, "legacy Linux window mode callback remains supported");
            var refreshWindow = new RefreshWindow();
            var refreshMethod = RuntimeMethodContract.FindWindowModeMethod(typeof(RefreshWindow), typeof(byte), typeof(int));
            refreshMethod?.Invoke(refreshWindow, new object[] { (byte)0, 1920, 1080, 0, 144f });
            Check(refreshWindow.Refresh == 144f, "current Linux window mode callback resolves and receives refresh rate");
            Check(RuntimeMethodContract.FindWindowModeMethod(typeof(ChangedWindow), typeof(byte), typeof(int)) == null, "unsupported Linux window mode callback remains rejected");
            Check(RuntimeMethodContract.FindVoidMethod(typeof(ValidModeForwarder), "Forward", staticPublic, typeof(int?)) != null && RuntimeMethodContract.FindVoidMethod(typeof(ValidModeForwarder), "DriveDirect", staticPublic, typeof(int)) != null, "Linux mode forwarders require the pinned nullable/direct parameter contracts");
            Check(RuntimeMethodContract.FindVoidMethod(typeof(WrongModeForwarder), "Forward", staticPublic, typeof(int?)) == null, "changed nullable Linux forwarding signature is rejected before activation");
            Check(RuntimeMethodContract.FindVoidMethod(typeof(WrongModeForwarder), "DriveDirect", staticPublic, typeof(int)) == null && RuntimeMethodContract.FindVoidMethod(typeof(GenericInvoker), "Invoke", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, typeof(Action)) == null, "non-void and generic Linux host callbacks cannot satisfy preflight");
            Check(PulsarShutdownContract.GetPrefix(null) == null && PulsarShutdownContract.GetPrefix(typeof(ValidShutdown)).ReturnType == typeof(bool), "vanilla shutdown and installed force-kill signature are accepted");
            bool invalidShutdown = false;
            try { PulsarShutdownContract.GetPrefix(typeof(UnsupportedShutdown)); } catch (InvalidOperationException) { invalidShutdown = true; }
            Check(invalidShutdown, "changed force-kill signature disables Vulkan bootstrap");
            invalidShutdown = false;
            try { PulsarShutdownContract.GetPrefix(typeof(MissingShutdown)); } catch (InvalidOperationException) { invalidShutdown = true; }
            Check(invalidShutdown, "existing force-kill type without its prefix is rejected");
            var core = CoreAssembly(true);
            Check(RuntimePluginContract.IsDotNetCompat(core, "dotnet-compat", "") && !RuntimePluginContract.IsDotNetCompat(core, "GraphicsPlugin", ""), "required Interim core exemption requires its exact metadata identity");
            Check(!RuntimePluginContract.IsDotNetCompat(CoreAssembly(false), "dotnet-compat", ""), "metadata identity alone cannot exempt a DX11 plugin");
            Check(RuntimePluginContract.IsDotNetCompat(core, "se-dotnet-compat", "") && RuntimePluginContract.IsDotNetCompat(core, "", "CometWorks/dotnet-compat"), "Pulsar legacy ID and repository identity recognize the same verified compatibility core");
            Check(!RuntimePluginContract.IsDotNetCompat(CoreAssembly(true, "ClientPlugin"), "dotnet-compat", ""), "incorrect patch namespace cannot satisfy the installed compatibility contract");
            string sourceBin = Path.Combine(temporary, "Interim", "DevFolder", "plugin", "Bin");
            string sourceRepo = Path.Combine(temporary, "repo");
            Directory.CreateDirectory(sourceBin);
            Directory.CreateDirectory(Path.Combine(sourceRepo, "Assets", "Native", "win-x64"));
            File.WriteAllBytes(Path.Combine(sourceRepo, "Assets", "Native", "win-x64", "se_vulkan_native.source-probe"), new byte[] { 1 });
            Directory.CreateDirectory(Path.Combine(temporary, "Interim", "Sources"));
            File.WriteAllText(Path.Combine(temporary, "Interim", "Sources", "sources.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><SourcesConfig><LocalPluginSources><LocalPlugin><Folder>" + sourceRepo + "</Folder></LocalPlugin></LocalPluginSources></SourcesConfig>");
            string sourceAssets = BootstrapAssets.FindDirectory(Path.Combine("Native", "win-x64", "se_vulkan_native.source-probe"), sourceBin);
            Check(string.Equals(sourceAssets, Path.Combine(sourceRepo, "Assets"), StringComparison.OrdinalIgnoreCase), "Pulsar -Sources registration resolves the repository native library");
            var store = new BootstrapConfigStore(temporary);
            Check(store.Load(out _).RequestedBackend == RendererBackend.Dx11, "missing configuration defaults to DX11");
            var config = new BackendConfiguration { RequestedBackend = RendererBackend.Vulkan, EnableValidation = true };
            store.Save(config); config.RetryGeneration = 2; store.Save(config);
            var restored = store.Load(out string warning);
            Check(warning == "" && restored.RequestedBackend == RendererBackend.Vulkan && restored.RetryGeneration == 2 && restored.EnableValidation, "atomic replacement round trips backend and validation");
            var exclusiveConfig = new BackendConfiguration { RequestedBackend = RendererBackend.Vulkan, ExclusiveFullscreen = true };
            Check(exclusiveConfig.Copy().ExclusiveFullscreen, "exclusive fullscreen preference survives configuration copy");
            store.Save(exclusiveConfig);
            Check(store.Load(out warning).ExclusiveFullscreen && warning == "", "exclusive fullscreen preference round trips");
            Check(WindowModeChoice.Offer(true, false, true) && !WindowModeChoice.Offer(false, false, true) && !WindowModeChoice.Offer(true, true, true), "exclusive fullscreen is offered only for Vulkan on Windows when fullscreen is listed");
            Check(WindowModeChoice.StoredMode(WindowModeChoice.ExclusiveKey) == 2 && WindowModeChoice.WantsExclusive(WindowModeChoice.ExclusiveKey) && WindowModeChoice.StoredMode(1) == 1, "exclusive dropdown stores the game's Fullscreen mode");
            Check(WindowModeChoice.DropdownKey(2, true, true) == WindowModeChoice.ExclusiveKey && WindowModeChoice.DropdownKey(2, true, false) == 2 && WindowModeChoice.DropdownKey(2, false, true) == 2, "DX11 hides exclusive fullscreen and shows regular Fullscreen");
            Check(Directory.GetFiles(Path.GetDirectoryName(store.FilePath), "*.tmp").Length == 0, "atomic save leaves no temporary files");
            store.BeginAttempt(2);
            Check(store.RecoverIncompleteAttempt(restored) && restored.RecoveryRequired && restored.RequestedBackend == RendererBackend.Vulkan, "incomplete startup preserves request and requires recovery");
            store.Retry(restored);
            Check(!restored.RecoveryRequired && restored.RetryGeneration == 3 && !File.Exists(store.AttemptPath), "explicit retry clears recovery and advances generation");
            store.BeginAttempt(3); store.MarkStartupSuccessful();
            Check(!store.RecoverIncompleteAttempt(restored), "successful validation removes startup marker");
            File.WriteAllText(store.FilePath, "<broken");
            Check(store.Load(out warning).RequestedBackend == RendererBackend.Dx11 && warning.Length != 0, "malformed config safely defaults to DX11");
            File.WriteAllText(store.FilePath, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///nonexistent'>]><VulkanConfig><LastFailure>&e;</LastFailure></VulkanConfig>");
            Check(store.Load(out warning).RequestedBackend == RendererBackend.Dx11 && warning.Length != 0, "external XML entities are rejected");
            store.Save(new BackendConfiguration { RequestedBackend = RendererBackend.Vulkan });
            BackendRuntime.InitializeConfiguration(temporary);
            var stalePreferenceSnapshot = BackendRuntime.Configuration;
            BackendRuntime.FailStartup("simulated render-thread failure");
            stalePreferenceSnapshot.EnableValidation = true; BackendRuntime.SaveConfiguration(stalePreferenceSnapshot);
            Check(BackendRuntime.RecoveryRequired && BackendRuntime.Configuration.LastFailure == "simulated render-thread failure", "delayed preference save cannot erase render-thread recovery");
            BackendRuntime.RetryVulkan(); BackendRuntime.BeginVulkanAttempt(); BackendRuntime.MarkStartupSuccessful();
            Check(File.Exists(store.AttemptPath), "healthy startup retains active-session crash sentinel");
            BackendRuntime.SetActive(RendererBackend.Vulkan, "Test active session");
            long healthyGeneration = BackendRuntime.Configuration.RetryGeneration; BackendRuntime.RetryVulkan();
            Check(File.Exists(store.AttemptPath) && BackendRuntime.Configuration.RetryGeneration == healthyGeneration, "healthy active Vulkan retry cannot clear its crash sentinel");
            BackendRuntime.EndSession(); Check(!File.Exists(store.AttemptPath), "healthy renderer disposal clears session sentinel");
            BackendRuntime.BeginVulkanAttempt(); BackendRuntime.MarkStartupSuccessful(); BackendRuntime.FailStartup("simulated post-activation failure"); BackendRuntime.EndSession();
            Check(File.Exists(store.AttemptPath) && BackendRuntime.RecoveryRequired, "post-activation failure preserves crash sentinel through disposal");
            BackendRuntime.RetryVulkan(); Check(File.Exists(store.AttemptPath) && BackendRuntime.RecoveryRequired, "failed active Vulkan cannot acknowledge recovery before DX11 restart");
            BackendRuntime.SetActive(RendererBackend.Dx11, "Test DX11 recovery");
            BackendRuntime.RetryVulkan();
            var draft = new DisplaySelectionTransaction(RendererBackend.Dx11) { Draft = RendererBackend.Vulkan };
            Check(!draft.AcceptClose(false), "unaccepted draft does not commit");
            draft.BeginOk(); Check(draft.AcceptClose(false), "OK without resolution change commits"); draft.EndOk();
            draft.BeginOk(); draft.BeginResolutionConfirmation(); draft.EndOk();
            Check(!draft.AcceptClose(false) && !draft.AcceptClose(true), "pending resolution confirmation does not commit");
            draft.ConfirmResolution(false); Check(!draft.AcceptClose(true), "resolution rejection and timeout discard selection");
            draft.BeginOk(); draft.BeginResolutionConfirmation(); draft.EndOk(); draft.ConfirmResolution(true);
            Check(draft.AcceptClose(true), "accepted resolution commits on immediate close");
            draft.Cancel(); Check(!draft.AcceptClose(true) && !draft.AcceptClose(false), "Cancel or Escape never commits");
            TestDualRendererHandshake();
            var exclusive = DisplayPlacement.For(2, 1920, 1080, 100, 50, 2560, 1440);
            var borderless = DisplayPlacement.For(1, 1920, 1080, 0, 0, 2560, 1440);
            var windowed = DisplayPlacement.For(0, 1600, 900, 0, 0, 2560, 1440);
            var presentation = DisplayPlacement.PresentationSize(2560, 1440, 1920, 1080);
            var fallbackPresentation = DisplayPlacement.PresentationSize(0, 0, 1920, 1080);
            Check(exclusive.Exclusive && exclusive.CoverMonitor && exclusive.X == 100 && exclusive.Y == 50 && exclusive.Width == 1920 && exclusive.Height == 1080, "exclusive fullscreen sizes the window to the selected mode");
            Check(borderless.CoverMonitor && !borderless.Exclusive && borderless.Width == 2560 && borderless.Height == 1440, "borderless covers the monitor instead of the smaller requested mode");
            Check(!windowed.CoverMonitor && windowed.Width == 1600 && windowed.Height == 900, "windowed mode keeps the requested client");
            Check(presentation.Width == 2560 && presentation.Height == 1440, "presentation follows the live window client");
            Check(fallbackPresentation.Width == 1920 && fallbackPresentation.Height == 1080, "selected resolution is the presentation fallback before a client extent is available");
            if (args.Length > 0) TestInstalledGame(args[0]);
            if (args.Length > 1) TestInstalledCompat(args[1], Path.GetDirectoryName(Path.GetFullPath(args[0])));
            else Console.WriteLine("Installed-game prepatch verification skipped: pass the original SpaceEngineers.exe path.");
            Console.WriteLine(checks + " bootstrap checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            // This test created the unique directory; resolve and validate containment before recursive cleanup.
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (temporary.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(temporary).StartsWith("SE-Vulkan-bootstrap-tests-", StringComparison.Ordinal) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }
    private static System.Reflection.Assembly CoreAssembly(bool complete, string patchNamespace = "Shared")
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("SE-Vulkan-test-core-" + Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Core");
        var identity = module.DefineType("ClientPlugin.Plugin", System.Reflection.TypeAttributes.Public);
        var field = identity.DefineField("Name", typeof(string), System.Reflection.FieldAttributes.Public | System.Reflection.FieldAttributes.Static | System.Reflection.FieldAttributes.Literal);
        field.SetConstant("DotNetCompat"); identity.CreateType();
        module.DefineType("Preloader", System.Reflection.TypeAttributes.Public).CreateType();
        module.DefineType(patchNamespace + ".Patches.Serialization.XmlSerializationPrepatch", System.Reflection.TypeAttributes.Public).CreateType();
        if (complete) module.DefineType(patchNamespace + ".Patches.ImageProcessing.DecodePixelDataPrepatch", System.Reflection.TypeAttributes.Public).CreateType();
        return assembly;
    }
    private static void TestInstalledCompat(string file, string gameDirectory)
    {
        using (var metadata = AssemblyDefinition.ReadAssembly(file))
        {
            Check(metadata.MainModule.AssemblyReferences.Any(a => a.Name == "VRage.Render11"), "installed DotNetCompat really references Render11 and exercises the compatibility exemption");
        }
        System.ResolveEventHandler resolve = (_, request) =>
        {
            string path = Path.Combine(gameDirectory, new System.Reflection.AssemblyName(request.Name).Name + ".dll");
            return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try
        {
            var assembly = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(file));
            Check(RuntimePluginContract.IsDotNetCompat(assembly, "dotnet-compat", "CometWorks/dotnet-compat"), "installed Interim DotNetCompat assembly is exempt from the DX11 plugin gate");
            Check(!RuntimePluginContract.IsDotNetCompat(assembly, "GraphicsPlugin", "AnotherOwner/GraphicsPlugin"), "the installed assembly cannot bypass compatibility checks with unrelated metadata");
            var inventory = Pulsar.Shared.Loader.Instance.Plugins;
            try
            {
                inventory.Add(new Pulsar.Shared.CompatibilityTestMetadata { Id = "dotnet-compat", RepoId = "CometWorks/dotnet-compat", FriendlyName = "DotNetCompat" }, assembly);
                Check(PulsarCompatibility.Check(out _), "real installed compatibility core passes the complete enabled-plugin inventory gate");
                using (var consumer = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SE-Vulkan-test-DX11-consumer", new Version(1, 0)), "Consumer", ModuleKind.Dll))
                using (var bytes = new MemoryStream())
                {
                    consumer.MainModule.AssemblyReferences.Add(new AssemblyNameReference("VRage.Render11", new Version(1, 0)));
                    consumer.Write(bytes);
                    inventory.Add(new Pulsar.Shared.CompatibilityTestMetadata { Id = "test-dx11", FriendlyName = "Test DX11 plugin" }, System.Reflection.Assembly.Load(bytes.ToArray()));
                }
                Check(!PulsarCompatibility.Check(out string reason) && reason.Contains("Test DX11 plugin") && !reason.Contains("DotNetCompat"), "genuine DX11 consumers still trigger fallback without falsely reporting DotNetCompat");
            }
            finally { inventory.Clear(); }
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= resolve; }
    }
    private static void TestDualRendererHandshake()
    {
        var handshake = HandshakeAssembly(true, true);
        var wrongRoute = HandshakeAssembly(false, true);
        var missingGuest = HandshakeAssembly(true, false);
        Check(RuntimePluginContract.IsDualRenderer(handshake), "renderer handshake recognizes RouteTypeName plus both path properties");
        Check(!RuntimePluginContract.IsDualRenderer(wrongRoute) && !RuntimePluginContract.IsDualRenderer(missingGuest), "a wrong route or a missing guest property cannot satisfy the handshake");
        var inventory = Pulsar.Shared.Loader.Instance.Plugins;
        inventory.Clear();
        try
        {
            inventory.Add(new Pulsar.Shared.CompatibilityTestMetadata { Id = "dual", FriendlyName = "Dual renderer" }, handshake);
            inventory.Add(new Pulsar.Shared.CompatibilityTestMetadata { Id = "test-dx11", FriendlyName = "Test DX11 plugin" }, Render11Assembly("SE-Vulkan-test-DX11-only"));
            Check(!PulsarCompatibility.Check(out string mixed) && mixed.Contains("Test DX11 plugin") && !mixed.Contains("Dual renderer"), "a dual-renderer plugin does not hide a genuine DX11 consumer");
            inventory.Remove(inventory.Keys.Single(key => key.Id == "test-dx11"));
            Check(PulsarCompatibility.Check(out string accepted) && accepted.Contains("dual-renderer handshake accepted for Dual renderer [dual]"), "a Render11 reference with the renderer handshake does not fall back to DX11");
        }
        finally { inventory.Clear(); }
    }
    private static System.Reflection.Assembly HandshakeAssembly(bool route, bool guest)
    {
        using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SE-Vulkan-test-handshake-" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "Handshake", ModuleKind.Dll))
        using (var bytes = new MemoryStream())
        {
            assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference("VRage.Render11", new Version(1, 0)));
            assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference("SharpDX", new Version(1, 0)));
            var type = new TypeDefinition("ClientPlugin.Test", "AnomalyRenderer", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, assembly.MainModule.TypeSystem.Object);
            var field = new FieldDefinition("RouteTypeName", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, assembly.MainModule.TypeSystem.String);
            field.Constant = route ? "ClientPlugin.Backend.RendererRoute" : "Other.Route";
            type.Fields.Add(field);
            assembly.MainModule.Types.Add(type);
            AddBool(type, "AllowDx11Device");
            if (guest) AddBool(type, "AllowGuestWork");
            assembly.Write(bytes);
            return System.Reflection.Assembly.Load(bytes.ToArray());
        }
    }
    private static void AddBool(TypeDefinition type, string name)
    {
        var getter = new MethodDefinition("get_" + name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig, type.Module.TypeSystem.Boolean);
        var il = getter.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ret));
        type.Methods.Add(getter);
        var property = new PropertyDefinition(name, PropertyAttributes.None, type.Module.TypeSystem.Boolean);
        property.GetMethod = getter;
        type.Properties.Add(property);
    }
    private static System.Reflection.Assembly Render11Assembly(string name)
    {
        using (var consumer = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name + "-" + Guid.NewGuid().ToString("N"), new Version(1, 0)), "Consumer", ModuleKind.Dll))
        using (var bytes = new MemoryStream())
        {
            consumer.MainModule.AssemblyReferences.Add(new AssemblyNameReference("VRage.Render11", new Version(1, 0)));
            consumer.Write(bytes);
            return System.Reflection.Assembly.Load(bytes.ToArray());
        }
    }
    private static void TestInstalledGame(string file)
    {
        Preloader.Finish();
        Check(EarlyBootstrapHooks.InstallCalls == 0, "Pulsar Finish defers runtime hooks until platform initialization");
        using (var assembly = AssemblyDefinition.ReadAssembly(file))
        {
            var method = assembly.MainModule.GetType("SpaceEngineers.MyProgram").Methods.Single(m => m.Name == "InitializeRender");
            Check(Preloader.Fingerprint(method) == Preloader.SupportedInitializeRenderSha256, "installed startup matches pinned IL fingerprint");
            int count = method.Body.Instructions.Count;
            int nullConstructions = method.Body.Instructions.Count(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "VRageRender.MyNullRender");
            Preloader.Patch(assembly);
            Check(BackendRuntime.HookReady, "original assembly accepts Vulkan factory prepatch");
            Check(method.Body.Instructions.Count == count && method.Body.Instructions.Count(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "VRageRender.MyNullRender") == nullConstructions, "instruction count and both null paths preserved");
            Check(method.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.DeclaringType.FullName == "ClientPlugin.Bootstrap.BackendFactory") == 1, "one DX11 construction replaced with stack-compatible factory call");
            using (var stream = new MemoryStream()) { assembly.Write(stream); Check(stream.Length > 0, "patched assembly writes valid Cecil image"); }
            Preloader.Patch(assembly); Check(BackendRuntime.HookReady, "prepatch stamp is idempotent");
        }
        using (var changed = AssemblyDefinition.ReadAssembly(file))
        {
            var method = changed.MainModule.GetType("SpaceEngineers.MyProgram").Methods.Single(m => m.Name == "InitializeRender");
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr).Operand = "Changed startup message";
            Preloader.Patch(changed);
            Check(!BackendRuntime.HookReady && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "VRageRender.MyDX11Render"), "changed game or conflicting prepatch keeps original DX11 construction");
        }
    }
}
