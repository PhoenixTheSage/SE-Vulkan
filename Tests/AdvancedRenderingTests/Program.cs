using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Bootstrap;

static class Program
{
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); ++checks; }
    [DllImport("ucrtbase.dll", EntryPoint="getenv", CallingConvention=CallingConvention.Cdecl, CharSet=CharSet.Ansi)] static extern IntPtr GetEnv(string name);
    static int Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "vulkan-advanced-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach(var option in AdvancedRenderingOptions.Definitions) Environment.SetEnvironmentVariable(option.EnvironmentVariable, null);
            var store = new BootstrapConfigStore(folder);
            var original = store.Load(out _);
            Check(AdvancedRenderingOptions.Definitions.Length > 30 && AdvancedRenderingOptions.Definitions.All(o => original.AdvancedRendering.Get(o.Id)), "all optimized settings default on");
            Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath));
            File.WriteAllText(store.FilePath, "<VulkanConfig><RequestedBackend>Vulkan</RequestedBackend><EnableValidation>false</EnableValidation></VulkanConfig>");
            var migrated = store.Load(out string warning);
            Check(warning.Length == 0 && migrated.RequestedBackend == RendererBackend.Vulkan && migrated.AdvancedRendering.Get("GpuOpaqueScene"), "old configuration gains optimized defaults");
            migrated.AdvancedRendering.Set("FrameSplit", false);
            var copy = migrated.Copy(); copy.AdvancedRendering.Set("FrameSplit", true);
            Check(!migrated.AdvancedRendering.Get("FrameSplit"), "configuration snapshots do not alias");
            store.Save(migrated);
            Check(!store.Load(out _).AdvancedRendering.Get("FrameSplit"), "disabled preference survives XML round trip");
            migrated.AdvancedRendering.Set("FrameSplit", true);
            if(args.Contains("dependency")) migrated.AdvancedRendering.Set("StockDepthGeometry", false);
            Environment.SetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_RUNS", "0");
            Environment.SetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_RECORD_CACHE", "");
            AdvancedRenderingOptions.ApplyStartup(migrated.AdvancedRendering);
            Check(!AdvancedRenderingOptions.RestartRequired(migrated.AdvancedRendering), "fresh startup does not request restart");
            Check(AdvancedRenderingOptions.IsOverridden("GpuOpaqueRuns") && !AdvancedRenderingOptions.EffectiveEnabled("GpuOpaqueRuns"), "explicit rollback environment overrides saved defaults");
            Check(!AdvancedRenderingOptions.IsOverridden("GpuOpaqueRecordCache") && AdvancedRenderingOptions.EffectiveEnabled("GpuOpaqueRecordCache"), "empty shell environment entries use configured defaults and remain editable");
            foreach(var option in AdvancedRenderingOptions.Definitions)
            {
                string expected=AdvancedRenderingOptions.EffectiveValue(option.Id);
                Check(Environment.GetEnvironmentVariable(option.EnvironmentVariable)==expected, "managed startup " + option.Id);
                Check(Marshal.PtrToStringAnsi(GetEnv(option.EnvironmentVariable))==expected, "native CRT startup " + option.Id);
            }
            Check(AdvancedRenderingOptions.EffectiveEnabled("NativePassScene") == !args.Contains("dependency"), "native passes follow prerequisite policy");
            foreach(var option in AdvancedRenderingOptions.Definitions.Where(o=>!AdvancedRenderingOptions.IsOverridden(o.Id)))
            {
                bool before=migrated.AdvancedRendering.Get(option.Id);
                migrated.AdvancedRendering.Set(option.Id,!before);
                Check(AdvancedRenderingOptions.RestartRequired(migrated.AdvancedRendering), "change requires restart " + option.Id);
                migrated.AdvancedRendering.Set(option.Id,before);
                Check(!AdvancedRenderingOptions.RestartRequired(migrated.AdvancedRendering), "revert clears restart " + option.Id);
            }
            migrated.AdvancedRendering.Set("GpuOpaqueRuns", false);
            Check(!AdvancedRenderingOptions.RestartRequired(migrated.AdvancedRendering), "external override does not promise a restart can change it");
            AdvancedRenderingOptions.ApplyStartup(new AdvancedRenderingSettings());
            Check(Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_RUNS")=="0", "startup application is idempotent");
            Console.WriteLine($"PASS {checks} advanced settings checks ({(args.Contains("dependency")?"dependency":"optimized")})"); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }
}
