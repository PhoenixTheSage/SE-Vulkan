using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace ClientPlugin.Bootstrap;

public sealed class AdvancedRenderingChoice
{
    [XmlAttribute] public string Id { get; set; }
    [XmlAttribute] public bool Enabled { get; set; } = true;
}

public sealed class AdvancedRenderingSettings
{
    [XmlElement("Option")] public AdvancedRenderingChoice[] Options { get; set; } = Array.Empty<AdvancedRenderingChoice>();

    public bool Get(string id)
    {
        AdvancedRenderingOptions.Find(id);
        var options = Options;
        if (options != null) for (int i = options.Length - 1; i >= 0; --i)
            if (options[i]?.Id == id) return options[i].Enabled;
        return true;
    }

    public void Set(string id, bool enabled)
    {
        if (Get(id) == enabled) return;
        var values = (Options ?? Array.Empty<AdvancedRenderingChoice>()).Where(choice => choice != null && choice.Id != id).ToList();
        if (!enabled) values.Add(new AdvancedRenderingChoice { Id = id, Enabled = false });
        Options = values.ToArray();
    }

    public AdvancedRenderingSettings Copy() => new AdvancedRenderingSettings
    {
        Options = (Options ?? Array.Empty<AdvancedRenderingChoice>()).Where(choice => choice != null)
            .Select(choice => new AdvancedRenderingChoice { Id = choice.Id, Enabled = choice.Enabled }).ToArray()
    };
}

public sealed class AdvancedRenderingOption
{
    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public string EnvironmentVariable { get; }
    public string EnabledValue { get; }
    public string DisabledValue { get; }
    public string Group { get; }

    internal AdvancedRenderingOption(string id, string label, string description, string variable, string group, string enabled = "1", string disabled = "0")
    {
        Id = id; Label = label; Description = description; Group = group;
        EnvironmentVariable = "SE_VULKAN_" + variable; EnabledValue = enabled; DisabledValue = disabled;
    }
}

public static class AdvancedRenderingOptions
{
    public static readonly AdvancedRenderingOption[] Definitions =
    {
        new("FrameSplit", "Split frame preparation", "Run scene preparation on a worker while the render thread processes the next frame.", "FRAME_SPLIT", "Frame scheduling"),
        new("BuildStages", "Two-stage frame building", "Overlap preparation of the next frame with submission of the previous one. Requires split frame preparation.", "BUILD_STAGES", "Frame scheduling", "2", "1"),
        new("PipelinedRecording", "Pipelined command recording", "Record a submitted frame on a worker while the next frame is prepared.", "PIPELINED_RECORDING", "Frame scheduling"),
        new("AsyncPresent", "Asynchronous presentation", "Present completed frames on a worker when the surface supports it.", "ASYNC_PRESENT", "Frame scheduling"),
        new("AsyncSubmit", "Asynchronous queue submission", "Submit frame and upload commands on a FIFO worker when decoupled presentation is active.", "ASYNC_SUBMIT", "Frame scheduling"),
        new("ParallelQueue", "Parallel draw preparation", "Prepare independent draw ranges on multiple workers.", "PARALLEL_QUEUE", "Frame scheduling"),
        new("ParallelCascades", "Parallel shadow cascades", "Record independent cascade shadow commands on workers.", "PARALLEL_CASCADES", "Frame scheduling"),
        new("NewActorViews", "Per-view actor selection", "Select actor geometry for each camera and shadow view.", "NEW_ACTOR_VIEWS", "Scene preparation"),
        new("ActorLods", "Actor levels of detail", "Use stock actor distance and transition rules to select model detail.", "ACTOR_LODS", "Scene preparation"),
        new("GroupOcclusion", "Group occlusion", "Use visibility results from actor groups to avoid hidden work.", "GROUP_OCCLUSION", "Scene preparation"),
        new("LightTiles", "Tiled lighting", "Build light lists for screen tiles to reduce lighting work.", "LIGHT_TILES", "Scene preparation"),
        new("StockShadowSchedule", "Stock shadow scheduling", "Use the stock schedule for shadow updates.", "STOCK_SHADOW_SCHEDULE", "Scene preparation"),
        new("StockShadowFit", "Stock shadow fitting", "Fit the shadow projections to the current frame.", "STOCK_SHADOW_FIT", "Scene preparation"),
        new("StockDepthGeometry", "Shared depth geometry", "Reuse stock model geometry for depth and shadow passes.", "STOCK_DEPTH_GEOMETRY", "Scene preparation"),
        new("WorldRecipes", "Reusable world draws", "Reuse prepared descriptions for unchanged world draws.", "WORLD_RECIPES", "Scene preparation"),
        new("RetainedSubmission", "Retained draw submission", "Keep reusable submission data between frames.", "RETAINED_SUBMISSION", "Scene preparation"),
        new("NativePassScene", "Native pass scene", "Prepare eligible main and shadow views in the native backend when stock view and depth policies permit.", "NATIVE_PASS_SCENE", "Native scene"),
        new("NativeCells", "Native cell rendering", "Prepare eligible cell geometry in the native backend.", "NATIVE_CELLS", "Native scene"),
        new("NativeMixedOpaque", "Mixed opaque ownership", "Allow native and managed opaque draws in the same frame.", "NATIVE_MIXED_OPAQUE", "Native scene"),
        new("AsyncPassPacking", "Parallel pass packing", "Pack native scene pass data asynchronously.", "ASYNC_PASS_PACKING", "Native scene"),
        new("DeferredCellEmission", "Deferred cell submission", "Defer cell command generation until it can overlap other frame work.", "DEFERRED_CELL_EMISSION", "Native scene"),
        new("GpuOpaque", "GPU opaque device features", "Request the Vulkan device features used by GPU-driven opaque rendering.", "GPU_OPAQUE", "GPU opaque scene"),
        new("GpuOpaqueScene", "GPU opaque actor selection", "Keep eligible actors in the GPU scene and select their opaque draws on the device.", "GPU_OPAQUE_SCENE", "GPU opaque scene"),
        new("GpuOpaqueDeviceLocal", "Device-local scene buffers", "Place working buffers for GPU opaque selection in device-local memory.", "GPU_OPAQUE_DEVICE_LOCAL", "GPU opaque scene"),
        new("GpuOpaqueHybrid", "Hybrid native passes", "Retain native pass rendering for eligible actors outside the GPU opaque scene.", "GPU_OPAQUE_HYBRID", "GPU opaque scene"),
        new("GpuOpaqueViewBatches", "Per-view draw batches", "Build opaque draw batches separately for each view.", "GPU_OPAQUE_VIEW_BATCHES", "GPU opaque scene"),
        new("GpuOpaqueRoleStreams", "Separate pass streams", "Generate distinct opaque draw streams for main, shadow and probe views.", "GPU_OPAQUE_ROLE_STREAMS", "GPU opaque scene"),
        new("GpuOpaqueSparseAppearance", "Sparse actor appearance", "Store changed material values separately from inherited model defaults.", "GPU_OPAQUE_SPARSE_APPEARANCE", "GPU opaque scene"),
        new("GpuOpaqueResidentDeltas", "Incremental scene updates", "Upload changed resident actors without copying the entire scene.", "GPU_OPAQUE_RESIDENT_DELTAS", "GPU opaque scene"),
        new("GpuOpaqueOwnedInputs", "Resident actor inputs", "Reuse resident object inputs for GPU-owned geometry.", "GPU_OPAQUE_OWNED_INPUTS", "GPU opaque scene"),
        new("GpuOpaqueViewMasks", "GPU view masks", "Use per-view visibility masks when preparing opaque draw streams.", "GPU_OPAQUE_VIEW_MASKS", "GPU opaque scene"),
        new("GpuVisibleFast", "Fast frustum tests", "Use precomputed camera planes for game-visible actor publication.", "GPU_OPAQUE_VISIBLE_FAST", "Recording and visibility"),
        new("GpuVisiblePacked", "Packed actor bounds", "Read visibility bounds from the latched frame snapshot.", "GPU_VISIBLE_PACKED", "Recording and visibility"),
        new("GpuVisibleDense", "Dense visibility list", "Visit registered GPU-owned actors through a compact list when packed bounds are enabled.", "GPU_VISIBLE_DENSE", "Recording and visibility"),
        new("GpuOpaqueRecordCache", "Cache view membership", "Retain conservative actor membership across unchanged views.", "GPU_OPAQUE_RECORD_CACHE", "Recording and visibility"),
        new("GpuOpaqueRecordKeys", "Compare membership inputs", "Recheck membership only when inputs to the visibility test change.", "GPU_OPAQUE_RECORD_KEYS", "Recording and visibility"),
        new("GpuOpaqueBatchCache", "Cache view batch lists", "Reuse draw batch lists for views with unchanged family membership.", "GPU_OPAQUE_BATCH_CACHE", "Recording and visibility"),
        new("GpuOpaqueGeometryCache", "Cache geometry checks", "Reuse successful stock geometry checks until meshes or topology change.", "GPU_OPAQUE_GEOMETRY_CACHE", "Recording and visibility"),
        new("GpuOpaqueMainOrderCache", "Cache opaque draw order", "Reuse stock main-view order while preserving fallback and coplanar ordering.", "GPU_OPAQUE_MAIN_ORDER_CACHE", "Recording and visibility"),
        new("GpuOpaqueRuns", "Merge compatible draws", "Combine adjacent compatible stock GPU draw commands.", "GPU_OPAQUE_RUNS", "Recording and visibility"),
        new("GpuOpaqueRunCache", "Cache merged runs", "Reuse the per-slot plan for compatible draw runs.", "GPU_OPAQUE_RUN_CACHE", "Recording and visibility"),
        new("GpuOpaqueParallelMain", "Parallel main recording", "Record eligible main-view draw ranges on workers.", "GPU_OPAQUE_PARALLEL_MAIN", "Recording and visibility"),
        new("GpuOpaqueParallelSpots", "Parallel spotlight recording", "Record independent spotlight shadow commands on workers.", "GPU_OPAQUE_PARALLEL_SPOTS", "Recording and visibility"),
        new("GpuOpaqueFusedRetained", "Fuse retained shadow work", "Combine retained spotlight filtering with the opaque selection pass.", "GPU_OPAQUE_FUSED_RETAINED", "Recording and visibility"),
    };

    private static readonly Dictionary<string, AdvancedRenderingOption> ById = Definitions.ToDictionary(option => option.Id, StringComparer.Ordinal);
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, string> Applied = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Inherited = new(StringComparer.Ordinal);
    private static AdvancedRenderingSettings startup;

    internal static AdvancedRenderingOption Find(string id) => ById.TryGetValue(id, out var option)
        ? option : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown advanced Vulkan setting.");

    public static bool Started { get { lock (Sync) return startup != null; } }
    // Shells may preserve an empty entry when clearing a process variable.
    // It is not an explicit on/off choice; publishing it to the CRT deletes it.
    static string InheritedValue(string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? null : value;
    }
    public static bool IsOverridden(string id)
    {
        var option = Find(id);
        lock (Sync) return startup == null ? InheritedValue(option.EnvironmentVariable) != null : Inherited.Contains(id);
    }
    public static string EffectiveValue(string id)
    {
        var option = Find(id);
        lock (Sync) return Applied.TryGetValue(id, out string value) ? value : Environment.GetEnvironmentVariable(option.EnvironmentVariable);
    }
    public static bool EffectiveEnabled(string id)
    {
        var option = Find(id);
        var value = EffectiveValue(id);
        return value == null || value == option.EnabledValue || (id == "NativePassScene" && value == "1");
    }

    public static bool RestartRequired(AdvancedRenderingSettings settings)
    {
        lock (Sync)
        {
            if (startup == null) return false;
            foreach (var option in Definitions)
                if (!Inherited.Contains(option.Id) && (settings?.Get(option.Id) ?? true) != startup.Get(option.Id)) return true;
            return false;
        }
    }

    public static void ApplyStartup(AdvancedRenderingSettings settings)
    {
        lock (Sync)
        {
            if (startup != null) return;
            var requested = settings?.Copy() ?? new AdvancedRenderingSettings();
            var selected = Definitions.ToDictionary(option => option.Id,
                option => InheritedValue(option.EnvironmentVariable)
                    ?? (requested.Get(option.Id) ? option.EnabledValue : option.DisabledValue));
            // A saved native-pass preference must remain safe when a prerequisite
            // is disabled. Explicit environment overrides retain their strict
            // diagnostic contract. Hybrid GPU/native is the validated default.
            if (InheritedValue(Find("NativePassScene").EnvironmentVariable) == null &&
                new[] { "NewActorViews", "GroupOcclusion", "StockDepthGeometry", "StockShadowSchedule", "StockShadowFit" }.Any(id => selected[id] == "0"))
                selected["NativePassScene"] = "0";
            foreach (var option in Definitions)
            {
                string inherited = InheritedValue(option.EnvironmentVariable);
                string value = selected[option.Id];
                // Win32 process variables and the UCRT's getenv table can differ after
                // process startup. Linux .NET also maintains a managed environment.
                // The native renderer reads the CRT, so publish the same value to both.
                int result = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? PutWindowsEnvironment(option.EnvironmentVariable, value)
                    : SetUnixEnvironment(option.EnvironmentVariable, value, 1);
                if (result != 0) throw new InvalidOperationException("Could not configure the native environment for " + option.EnvironmentVariable);
                if (inherited == null) Environment.SetEnvironmentVariable(option.EnvironmentVariable, value, EnvironmentVariableTarget.Process);
                else Inherited.Add(option.Id);
                Applied[option.Id] = value;
            }
            startup = requested;
        }
    }

    [DllImport("ucrtbase.dll", EntryPoint = "_putenv_s", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int PutWindowsEnvironment(string name, string value);
    [DllImport("libc", EntryPoint = "setenv", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetUnixEnvironment(string name, string value, int overwrite);
}
