using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClientPlugin.Bootstrap;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Scene;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Utils;
using Sandbox.Game.World;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage.FileSystem;
using VRage.Plugins;
using VRageMath;
using VRageRender;

namespace ScenePerformanceHarness;

// This plugin runs only against a launcher-marked private copy. It never saves
// the source world or applies the CreativeFixtureHarness's scene mutations.
public sealed partial class Plugin : IPlugin
{
    static readonly object Gate = new();
    static readonly List<(double Cpu, double Interval)> Frames = new();
    static readonly List<NativePerformanceStats> NativeFrames=new();
    static readonly List<NativePassProfile> NativePassFrames=new();
    static readonly List<PacketPreparationStats> PacketFrames=new();
    static readonly RendererCpuProfiler.Window CpuPhases=new();
    static readonly List<NativeQueueValidationStats> ValidationFrames=new();
    static readonly List<NativeIndirectStats> IndirectFrames=new();
    static readonly List<NativeGeometryArenaStats> GeometryFrames=new();
    static readonly List<NativeObjectRecordStats> ObjectFrames=new();
    static readonly List<SceneSubmissionStats> SubmissionFrames=new();
    static readonly List<ActorLodStats> LodFrames=new();
    static readonly List<ActorSizeCullStats> SizeFrames=new();
    static readonly List<FarCascadePolicyStats> FarFrames=new();
    static readonly List<ViewEligibilityStats> ViewFrames=new();
    static readonly List<NativeRecipeStats> RecipeFrames=new();
    static readonly List<NewActorViewStats> NewViewFrames=new();
    static readonly List<ModelBatchStats> ModelFrames=new();
    static volatile bool modelsEnabled;
    static bool frameModels;
    static volatile bool newViewsEnabled;
    static bool frameNewViews;
    static volatile bool recipesEnabled;
    static bool frameRecipes;
    static volatile bool viewEnabled;
    static bool frameView;
    static readonly List<NativeModelGroupStats> BulkModelFrames=new();
    static volatile bool bulkModelsEnabled;static bool frameBulkModels;
    static readonly List<NativePresentFlowStats> PresentFlows=new();
    static readonly List<NativePresentSample> PresentSamples=new();static readonly NativePresentSample[] PresentSampleBuffer=new NativePresentSample[64];static ulong lastPresentSample;static bool lostPresentSamples;
    static volatile bool asyncPresentEnabled;
    static bool frameAsyncPresent;
    static bool? asyncPresentApplied;
    static volatile bool farEnabled;
    static bool frameFar;
    static volatile bool sizeEnabled;
    static bool frameSize;
    static volatile bool groupOcclusionEnabled;
    static bool frameGroupOcclusion;
    static object groupOcclusionSnapshot;
    // Shared native job pool counters at the first and latest sampled frames.
    static object JobStatsFirst,JobStatsLast;static long jobStatsCounter;
    // Separate and not inlined: pinned baseline plugins predate GetJobStats, and a missing
    // member must fail only this call, not the JIT of SampleFrame.
    static bool jobStatsAvailable=true;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void WriteJobStats(string reports){
        var jobFirst=(NativeJobStats)JobStatsFirst;var jobLast=(NativeJobStats)JobStatsLast;{
            // A loop, not a lambda: a closure over NativeJobStats would be a type that fails to load against older plugins.
            var kinds=new List<object>();for(int k=0;k<(int)jobLast.KindCount;k++){var a=jobFirst.Kinds[k];var b=jobLast.Kinds[k];ulong jobs=b.Jobs-a.Jobs;
                kinds.Add(new{Kind=k<NativeJobStats.KindNames.Length?NativeJobStats.KindNames[k]:k.ToString(),Jobs=jobs,Helped=b.Helped-a.Helped,MeanWaitUs=jobs>0?(b.WaitNs-a.WaitNs)/(double)jobs/1000:0,MaxWaitUs=b.MaxWaitNs/1000.0,MeanRunUs=jobs>0?(b.RunNs-a.RunNs)/(double)jobs/1000:0,RunMs=(b.RunNs-a.RunNs)/1e6});}
            File.WriteAllText(Path.Combine(reports,"job-stats.json"),JsonSerializer.Serialize(new{jobLast.Workers,jobLast.StageThreads,jobLast.PeakQueued,Kinds=kinds,
                Scope="Shared native job pool between the first and latest sampled frames. Wait: queued to started. Helped: run by the joining thread. MaxWait and PeakQueued cover the window."},new JsonSerializerOptions{WriteIndented=true}));}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void SampleJobStats(object renderer){var jobBackend=(NativeBackend)BackendField.GetValue(renderer);if(JobStatsFirst==null)JobStatsFirst=jobBackend.GetJobStats(true);else if(++jobStatsCounter%TelemetryStride==0)JobStatsLast=jobBackend.GetJobStats();}
    static volatile bool actorLodsEnabled;
    static bool frameActorLods;
    static volatile bool submissionEnabled;
    static bool frameSubmission;
    static readonly List<NativeCascadeRecordStats> CascadeFrames=new();
    static readonly List<NativeQueueWorkStats> QueueWorkFrames=new();
    static volatile bool parallelQueueEnabled;
    static bool frameParallelQueue;
    static bool? parallelQueueApplied;
    static readonly List<NativeLightTileStats> LightTileFrames=new();
    static volatile bool lightTilesEnabled;
    static bool frameLightTiles;
    static bool? lightTilesApplied;
    static volatile bool parallelEnabled;
    static bool frameParallel;
    static bool? parallelApplied;
    static volatile bool retainedEnabled;
    static bool frameRetained;
    static FieldInfo BackendField;
    static FieldInfo SceneField;
    static FieldInfo VoxelsField;
    static GeometryStorageStats geometryStorage;
    static NativePresentationStats presentationState;
    static volatile bool sampling;
    static volatile bool batchingEnabled=true;
    static volatile bool hintsEnabled=true;
    static volatile bool wideEnabled;
    static volatile bool voxelLookupEnabled;
    static volatile bool voxelObjectEnabled;
    static volatile bool textureValidationEnabled;
    static volatile bool indirectEnabled;
    static bool geometryArenaEnabled;
    static bool frameIndirect;
    static bool? indirectApplied;
    static bool frameBatching;
    static bool frameHints;
    static bool frameWide;
    static bool frameVoxelLookup;
    static bool frameVoxelObject;
    static bool frameTextureValidation;
    static long drawStarted, previousPresent,profileFrameSequence;
    readonly Stopwatch lifetime = Stopwatch.StartNew();
    Harmony harmony;
    string reports, world, expected;
    bool loadRequested, finished;
    bool compareBatching,compareHints,compareCells,compareVoxelLookup,compareVoxelObjects,compareTextureValidation,compareIndirect,compareRetained,compareParallel,compareLightTiles,compareSubmission,compareActorLods,compareGroupOcclusion,compareParallelQueue,compareSize,compareFar,compareView,compareRecipes,compareNewViews,compareModels,compareAsyncPresent,compareBulkModels,secondPhase;
    double readyAt = -1, sampleAt = -1;
    double transitionUntil=-1;

    public void Init(object gameInstance)
    {
        string diagnosticData=Environment.GetEnvironmentVariable("SE_VULKAN_PERF_DATA");
        try { Initialize(gameInstance); }
        catch(Exception error) {
            if(!string.IsNullOrEmpty(diagnosticData) && File.Exists(Path.Combine(diagnosticData,".se-vulkan-performance")))
                File.WriteAllText(Path.Combine(diagnosticData,"harness-init-failure.txt"),error.ToString());
            throw;
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    void Initialize(object gameInstance)
    {
        string data = Path.GetFullPath(Environment.GetEnvironmentVariable("SE_VULKAN_PERF_DATA") ?? throw new Exception("Private benchmark mode required."));
        string marker = Path.Combine(data, ".se-vulkan-performance");
        if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker).Trim(), data, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFullPath(MyFileSystem.UserDataPath), data, StringComparison.OrdinalIgnoreCase)) throw new Exception("Private data guard failed.");
        reports = Path.Combine(data, "Reports");world = Path.Combine(data, "World");
        if (!File.Exists(Path.Combine(world, "Sandbox.sbc"))) throw new Exception("Private benchmark world missing.");
        expected = Environment.GetEnvironmentVariable("SE_VULKAN_PERF_BACKEND");
        if(expected=="Vulkan"){
            BackendField=AccessTools.Field(typeof(MyVulkanRender),"backend");
            SceneField=AccessTools.Field(typeof(MyVulkanRender),"scene");
            VoxelsField=AccessTools.Field(typeof(MyVulkanRender),"voxels");
        }
        compareBatching=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_BATCHING")=="1" && expected=="Vulkan";
        compareHints=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_BATCH_HINTS")=="1" && expected=="Vulkan";
        compareCells=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_BATCH_CELLS")=="1" && expected=="Vulkan";
        compareVoxelLookup=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_VOXEL_LOOKUP")=="1" && expected=="Vulkan";
        voxelLookupEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_MATERIAL_CACHE")!="0";
        compareVoxelObjects=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_VOXEL_OBJECTS")=="1" && expected=="Vulkan";
        voxelObjectEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_OBJECT_CACHE")!="0";
        compareTextureValidation=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_TEXTURE_VALIDATION")=="1" && expected=="Vulkan";
        textureValidationEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_TEXTURE_VALIDATION_CACHE")=="1";
        compareIndirect=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_WORLD_INDIRECT")=="1" && expected=="Vulkan";
        indirectEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_WORLD_INDIRECT")=="1";indirectApplied=null;
        compareLightTiles=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_LIGHT_TILES")=="1";
        lightTilesEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_LIGHT_TILES")=="1";lightTilesApplied=null;
        compareParallel=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_PARALLEL_CASCADES")=="1";
        parallelEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_PARALLEL_CASCADES")=="1";parallelQueueEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_PARALLEL_QUEUE")=="1";compareParallelQueue=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_PARALLEL_QUEUE")=="1";parallelQueueApplied=null;parallelApplied=null;
        compareRetained=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_RETAINED_OBJECTS")=="1";
        retainedEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_RETAINED_OBJECTS")=="1";
        submissionEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_RETAINED_SUBMISSION")=="1";
        asyncPresentEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_ASYNC_PRESENT")=="1";compareAsyncPresent=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_ASYNC_PRESENT")=="1";asyncPresentApplied=null;
        bulkModelsEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_BULK_MODELS")=="1";compareBulkModels=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_BULK_MODELS")=="1";
        modelsEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_MODEL_BATCHING")=="1";compareModels=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_MODEL_BATCHING")=="1";
        newViewsEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_NEW_ACTOR_VIEWS")=="1";compareNewViews=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_NEW_ACTOR_VIEWS")=="1";
        recipesEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_WORLD_RECIPES")=="1";compareRecipes=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_WORLD_RECIPES")=="1";
        viewEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_VIEW_ELIGIBILITY")=="1";compareView=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_VIEW_ELIGIBILITY")=="1";
        farEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_FAR_CASCADES")=="1";compareFar=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_FAR_CASCADES")=="1";
        sizeEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_ACTOR_SIZE_CULLING")=="1";compareSize=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_ACTOR_SIZE")=="1";
        actorLodsEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_ACTOR_LODS")=="1";groupOcclusionEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_GROUP_OCCLUSION")=="1";compareGroupOcclusion=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_GROUP_OCCLUSION")=="1";compareActorLods=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_ACTOR_LODS")=="1";
        compareSubmission=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_RETAINED_SUBMISSION")=="1";
        geometryArenaEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_GEOMETRY_ARENA")=="1";
        wideEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_WIDE_RIGID_BATCHES")=="1";
        compareLightPixels=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_LIGHT_PIXELS")=="1";
        compareSubmissionPixels=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_SUBMISSION_PIXELS")=="1";
        if(compareSubmissionPixels)pixelFiles=new[]{"legacy-a.png","legacy-b.png","prepared-a.png","prepared-b.png","legacy-c.png","legacy-d.png"};
        compareGroupPixels=Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_GROUP_PIXELS")=="1";
        if(compareGroupPixels)pixelFiles=new[]{"legacy-a.png","legacy-b.png","group-a.png","group-b.png","legacy-c.png","legacy-d.png"};
        comparePixels=compareGroupPixels || compareSubmissionPixels || compareLightPixels || Environment.GetEnvironmentVariable("SE_VULKAN_COMPARE_BATCH_PIXELS")=="1";
        if(compareLightPixels)pixelFiles=new[]{"full-a.png","full-b.png","gpu-a.png","gpu-b.png","full-c.png","full-d.png"};
        if (expected != "Vulkan" && expected != "Dx11") throw new Exception("Explicit backend required.");
        Directory.CreateDirectory(reports);
        MySandboxGame.Config.EnableCloud = false;
        MySandboxGame.Config.ExperimentalMode = true;
        MySandboxGame.Config.WelcomScreenCurrentStatus = MyConfig.WelcomeScreenStatus.AlreadySeen;
        harmony = new Harmony("SEVulkan.PrivateScenePerformance");
        selectionAudit=Environment.GetEnvironmentVariable("SE_VULKAN_SELECTION_AUDIT")=="1";selectionArmed=false;workloadAudit=selectionAudit || Environment.GetEnvironmentVariable("SE_VULKAN_WORKLOAD_AUDIT")=="1";workloadSnapshot=null;occlusionDiagnosticSnapshot=null;diagnosticPhaseStarted=false;diagnosticPhaseFrames=0;oldGeometryViews.Clear();newGeometryViews.Clear();oldSizeCandidates.Clear();
        if(workloadAudit && expected=="Dx11"){harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer"),"Render"),prefix:new HarmonyMethod(typeof(Plugin),nameof(AuditNewRender)));harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGeometryRendererOld"),"Render"),prefix:new HarmonyMethod(typeof(Plugin),nameof(AuditOldRender)));harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGeometryRendererOld"),"TrimCullProxies"),prefix:new HarmonyMethod(typeof(Plugin),nameof(AuditOldSizeBeforeTrim)));}
        if(expected=="Dx11"&&Environment.GetEnvironmentVariable("SE_VULKAN_PROBE_COVERAGE_AUDIT")=="1")harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe"),"UpdateCullQuery"),prefix:new HarmonyMethod(typeof(Plugin),nameof(RefreshSourceProbe)));
        Type renderer = expected == "Vulkan" ? typeof(MyVulkanRender) : AccessTools.TypeByName("VRageRender.MyRender11");
        // Interactive reproduction must keep the renderer's production defaults.
        // Benchmark hooks deliberately override feature switches for A/B timing.
        if(!CameraSweepEnabled){
            harmony.Patch(AccessTools.Method(renderer, "Draw", new[] { typeof(bool) }), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeginDraw)));
            harmony.Patch(AccessTools.Method(renderer, "Present"), prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePresent)), postfix: new HarmonyMethod(typeof(Plugin), nameof(EndPresent)));
        }
        else if(SweepIsolation)harmony.Patch(AccessTools.Method(renderer, "Draw", new[] { typeof(bool) }),prefix:new HarmonyMethod(typeof(Plugin),nameof(ApplySweepPolicy)));
        if(!CameraSweepEnabled)InstallFrameGapCensus();
        nativeCriticalEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_CRITICAL_PROFILE")=="1";haveCritical=false;InstallFrameStateAudit();InstallSystemParityAudit();InstallLightingReconciliation();
    }
    // Render-thread time outside Draw..Present-prefix, split by owner. Pipelined Vulkan samples in the
    // Present prefix, so the Present body and this harness's postfix fall outside CpuMeanMs too.
    static long gapPrefixAt,gapPrefixEndAt,gapPostfixAt,gapExitAt,gapWaitAt,gapBeforeAt,gapFrames;static int gapRenderThread;
    static double gapHarnessSample,gapPresentBody,gapPostfix,gapLoop,gapWaiter,gapBeforeRender;
    static double GapMs(long from,long to)=>(to-from)*1000.0/Stopwatch.Frequency;
    void InstallFrameGapCensus(){
        var waiter=AccessTools.Method(AccessTools.TypeByName("VRage.Library.Utils.WaitForTargetFrameRate"),"Wait");
        if(waiter!=null)harmony.Patch(waiter,prefix:new HarmonyMethod(typeof(Plugin),nameof(GapWaitBegin)),postfix:new HarmonyMethod(typeof(Plugin),nameof(GapWaitEnd)));
        var before=AccessTools.Method(typeof(MyRenderProxy),"BeforeRender");
        if(before!=null)harmony.Patch(before,prefix:new HarmonyMethod(typeof(Plugin),nameof(GapBeforeBegin)),postfix:new HarmonyMethod(typeof(Plugin),nameof(GapBeforeEnd)));
    }
    // The update thread also paces itself with WaitForTargetFrameRate; only the render thread counts.
    static bool GapRenderThread=>gapRenderThread!=0&&Environment.CurrentManagedThreadId==gapRenderThread;
    static void GapWaitBegin(){if(GapRenderThread)gapWaitAt=Stopwatch.GetTimestamp();}
    static void GapWaitEnd(){if(sampling&&GapRenderThread&&gapWaitAt!=0){gapWaiter+=GapMs(gapWaitAt,Stopwatch.GetTimestamp());gapWaitAt=0;}}
    static void GapBeforeBegin(){if(GapRenderThread)gapBeforeAt=Stopwatch.GetTimestamp();}
    static void GapBeforeEnd(){if(sampling&&GapRenderThread&&gapBeforeAt!=0){gapBeforeRender+=GapMs(gapBeforeAt,Stopwatch.GetTimestamp());gapBeforeAt=0;}}
    // Native timing reports (opt-in env) cover exactly the sampling window; older native DLLs lack the export.
    static readonly bool timingReports=!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SE_VULKAN_GPU_PASS_TIMING"))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SE_VULKAN_RECORD_TIMING"));
    static bool timingReportsSampling;
    // Separate, non-inlined: archived benchmark plugins lack these APIs and must still JIT the hot hooks.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void ApplyTimingReports(object instance,bool active)=>((NativeBackend)BackendField.GetValue(instance)).SetTimingReports(active);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void WriteMessageCensus(string reports){
        RendererCpuProfiler.MessageSnapshot messages;lock(Gate)messages=CpuPhases.CaptureMessages();
        File.WriteAllText(Path.Combine(reports,"message-census.json"),JsonSerializer.Serialize(new{Instrumented=true,
            Scope="Exclusive wall time per render message type on accepted frames. Spike frames have a Messages phase at or above the threshold; MsPerSpikeFrame is that type's mean inside them.",Data=messages},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
    static void GapDrawBegin(object instance){
        if(timingReports&&sampling!=timingReportsSampling&&instance is MyVulkanRender){timingReportsSampling=sampling;ApplyTimingReports(instance,sampling);}
        gapRenderThread=Environment.CurrentManagedThreadId;long now=Stopwatch.GetTimestamp();if(sampling&&gapExitAt!=0){gapLoop+=GapMs(gapExitAt,now);gapFrames++;}gapExitAt=0;}
    static void BeginDraw(object __instance) {GapDrawBegin(__instance);BeginNativeCritical(__instance);ApplyMatchedFrameOnce(__instance);LightingBeginDraw(__instance);BeginWorkloadDiagnostics(__instance);captureWorkloadFrame=workloadAudit && (sampling || selectionArmed) && workloadSnapshot==null;if((captureWorkloadFrame || probeCoverageCollecting) && selectionAudit && __instance is MyVulkanRender)((NativeBackend)BackendField.GetValue(__instance)).BeginLogicalSelectionCapture();drawStarted = Stopwatch.GetTimestamp();RenderFrameEvents.Log.Begin(++profileFrameSequence);frameBatching=batchingEnabled;frameHints=hintsEnabled;frameWide=wideEnabled;frameVoxelLookup=voxelLookupEnabled;frameVoxelObject=voxelObjectEnabled;frameTextureValidation=textureValidationEnabled;frameIndirect=indirectEnabled;frameRetained=retainedEnabled;frameSubmission=submissionEnabled;frameActorLods=actorLodsEnabled;frameSize=sizeEnabled;frameFar=farEnabled;frameView=viewEnabled;frameRecipes=recipesEnabled;frameNewViews=newViewsEnabled;frameModels=modelsEnabled;frameBulkModels=bulkModelsEnabled;frameAsyncPresent=asyncPresentEnabled;frameGroupOcclusion=groupOcclusionEnabled;frameParallel=parallelEnabled;frameParallelQueue=parallelQueueEnabled;frameLightTiles=lightTilesEnabled;if(__instance is MyVulkanRender){var target=(NativeBackend)BackendField.GetValue(__instance);if(indirectApplied!=frameIndirect){target.SetWorldIndirect(frameIndirect);indirectApplied=frameIndirect;}if(parallelApplied!=frameParallel){target.SetParallelCascades(frameParallel);parallelApplied=frameParallel;}if(lightTilesApplied!=frameLightTiles){target.SetLightTiles(frameLightTiles);lightTilesApplied=frameLightTiles;}if(parallelQueueApplied!=frameParallelQueue){target.SetParallelQueue(frameParallelQueue);parallelQueueApplied=frameParallelQueue;}if(asyncPresentApplied!=frameAsyncPresent){target.SetAsyncPresent(frameAsyncPresent);asyncPresentApplied=frameAsyncPresent;}target.ObjectRetentionEnabled=frameRetained;target.RetainedSubmissionEnabled=frameSubmission;target.WorldRecipesEnabled=frameRecipes;target.ModelBatchingEnabled=frameModels;target.BulkModelsEnabled=frameBulkModels;((VulkanScene)SceneField.GetValue(__instance)).ActorLodsEnabled=frameActorLods;((VulkanScene)SceneField.GetValue(__instance)).NewActorViewsEnabled=frameNewViews;((VulkanScene)SceneField.GetValue(__instance)).ActorSizeCullingEnabled=frameSize;((VulkanScene)SceneField.GetValue(__instance)).StockFarCascadesEnabled=frameFar;((VulkanScene)SceneField.GetValue(__instance)).ViewEligibilityEnabled=frameView;((VulkanScene)SceneField.GetValue(__instance)).GroupOcclusionEnabled=frameGroupOcclusion;target.RigidBatchingEnabled=frameBatching;target.RigidBatchHintsEnabled=frameHints;target.WideRigidBatchesEnabled=frameWide;target.TextureValidationCacheEnabled=frameTextureValidation;((VulkanVoxels)VoxelsField.GetValue(__instance)).Materials.LookupCacheEnabled=frameVoxelLookup;VoxelObjectConstants.Enabled=frameVoxelObject;}}
    static object gpuSceneSnapshot;static ulong[] gpuTopologyFirst,gpuTopologyLast,gpuComputeFirst,gpuComputeLast,gpuCpuCostsFirst,gpuCpuCostsLast;
    // With pipelined recording, Present only launches native recording. The
    // frame sample and its native telemetry are then taken before the launch:
    // after it, every telemetry read would wait for the in-flight recording,
    // which the product never does. Telemetry then describes the previous frame.
    static bool PipelinedPresent(object __instance)=>__instance is MyVulkanRender&&((NativeBackend)BackendField.GetValue(__instance)).PipelinedRecordingEnabled;
    static void BeforePresent(object __instance){gapPrefixAt=Stopwatch.GetTimestamp();if(PipelinedPresent(__instance))SampleFrame(__instance);gapPrefixEndAt=Stopwatch.GetTimestamp();if(sampling)gapHarnessSample+=GapMs(gapPrefixAt,gapPrefixEndAt);}
    static void EndPresent(object __instance)
    {
        gapPostfixAt=Stopwatch.GetTimestamp();if(sampling&&gapPrefixEndAt!=0)gapPresentBody+=GapMs(gapPrefixEndAt,gapPostfixAt);
        try{EndPresentObserved(__instance);}
        finally{gapExitAt=Stopwatch.GetTimestamp();if(sampling)gapPostfix+=GapMs(gapPostfixAt,gapExitAt);}
    }
    static void EndPresentObserved(object __instance)
    {
        ObserveRichHud(__instance);
        LightingAfterPresent(__instance);CaptureProbeState(__instance);ObserveProbeCoverage(__instance);if(probeCoverageCollecting && __instance is MyVulkanRender)((NativeBackend)BackendField.GetValue(__instance)).EndLogicalSelectionCapture();
        if(comparePixels && __instance is MyVulkanRender){
            var pixelTarget=(NativeBackend)BackendField.GetValue(__instance);
            pixelEvidence=new PixelFrameEvidence(compareGroupPixels?frameGroupOcclusion:compareSubmissionPixels?frameSubmission:compareLightPixels?frameLightTiles:frameWide,pixelTarget.GetPerformanceStats(),pixelTarget.GetResolutionStats(),pixelTarget.GetLightTileStats(),pixelTarget.GetSubmissionStats(),((VulkanScene)SceneField.GetValue(__instance)).GetGroupOcclusionStats());
        }
        if(selectionAudit && captureWorkloadFrame){workloadSnapshot=__instance is MyVulkanRender?AuditVulkan(__instance):AuditDx11();CaptureShadowDepth(__instance);if(__instance is MyVulkanRender)((NativeBackend)BackendField.GetValue(__instance)).EndLogicalSelectionCapture();captureWorkloadFrame=false;selectionArmed=false;}
        if(!PipelinedPresent(__instance))SampleFrame(__instance);
    }
    static readonly int TelemetryStride=int.TryParse(Environment.GetEnvironmentVariable("SE_VULKAN_HARNESS_TELEMETRY_STRIDE"),out int stride)&&stride>0?stride:16;
    static long telemetryCounter;
    // A telemetry frame reads native stats in the Present prefix, which delays the next frame's start. DX11 frames pay
    // no such cost, so the frame after each telemetry read is left out of the interval statistics
    // (SE_VULKAN_HARNESS_KEEP_TELEMETRY_FRAMES=1 keeps it). Present-completion samples are not filtered.
    static readonly bool KeepTelemetryFrames=Environment.GetEnvironmentVariable("SE_VULKAN_HARNESS_KEEP_TELEMETRY_FRAMES")=="1";
    static bool skipNextFrame;static long skippedTelemetryFrames;
    // Caller holds Gate.
    static void SamplePresent(NativeBackend native){
        if(native==null)return;
        var flow=native.GetPresentFlowStats();PresentFlows.Add(flow);if(lastPresentSample==0)lastPresentSample=flow.CompletedCalls;int count=native.GetPresentSamples(lastPresentSample,PresentSampleBuffer,out ulong oldest);if(oldest>lastPresentSample+1)lostPresentSamples=true;for(int i=0;i<count;i++){var row=PresentSampleBuffer[i];if(row.Sequence!=lastPresentSample+1)lostPresentSamples=true;PresentSamples.Add(row);lastPresentSample=row.Sequence;}BulkModelFrames.Add(native.GetModelGroupStats());
    }
    static void SampleFrame(object __instance)
    {
        long now = Stopwatch.GetTimestamp(), previous = previousPresent;previousPresent = now;RenderFrameEvents.Log.End(profileFrameSequence,(now-drawStarted)*1000.0/Stopwatch.Frequency);
        if (!sampling || frameBatching!=batchingEnabled || frameHints!=hintsEnabled || frameWide!=wideEnabled || frameVoxelLookup!=voxelLookupEnabled || frameVoxelObject!=voxelObjectEnabled || frameTextureValidation!=textureValidationEnabled || Sandbox.MySandboxGame.IsPaused || drawStarted == 0 || previous == 0) return;
        if(__instance is MyVulkanRender&&jobStatsAvailable)try{SampleJobStats(__instance);}catch(MissingMethodException){jobStatsAvailable=false;}catch(EntryPointNotFoundException){jobStatsAvailable=false;}
        if(gpuSceneSnapshot==null&&__instance is MyVulkanRender)gpuSceneSnapshot=((VulkanScene)SceneField.GetValue(__instance)).GetGpuSceneStats();
        if(gpuAdmissionSnapshot==null&&__instance is MyVulkanRender&&Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_TIMING")=="1")gpuAdmissionSnapshot=AuditGpuAdmission((VulkanScene)SceneField.GetValue(__instance));
        ObserveWorkloadDiagnostics(__instance);if(captureWorkloadFrame){workloadSnapshot=__instance is MyVulkanRender?AuditVulkan(__instance):AuditDx11();captureWorkloadFrame=false;}
        var native=__instance is MyVulkanRender?(NativeBackend)BackendField.GetValue(__instance):null;
        // Pipelined Vulkan samples in the Present prefix, so every native read here is frame time.
        // Timing and present samples are read every frame; auxiliary telemetry every stride frames.
        bool telemetry=native==null||RendererCpuProfiler.Enabled||++telemetryCounter%TelemetryStride==0;
        bool skipThis=skipNextFrame&&!KeepTelemetryFrames;skipNextFrame=false;if(skipThis)skippedTelemetryFrames++;
        if(!telemetry){lock(Gate){if(!sampling)return;if(!skipThis)Frames.Add(((now-drawStarted)*1000.0/Stopwatch.Frequency,(now-previous)*1000.0/Stopwatch.Frequency));ObserveBenchmarkCompletedFrame();SamplePresent(native);}return;}
        skipNextFrame=native!=null&&!RendererCpuProfiler.Enabled;
        var performance=native!=null?native.GetPerformanceStats():default;
        if(native!=null){gpuTopologyLast=native.GetGpuTopologyCounts();gpuTopologyFirst??=gpuTopologyLast;gpuComputeLast=native.GetGpuComputeTimes();gpuComputeFirst??=gpuComputeLast;if(RendererCpuProfiler.Enabled){gpuCpuCostsLast=native.GetType().GetMethod("GetGpuCpuCosts")?.Invoke(native,null) as ulong[];gpuCpuCostsFirst??=gpuCpuCostsLast;}}
        var validation=native!=null?native.GetQueueValidationStats():default;
        var indirect=native!=null?native.GetIndirectStats():default;
        var lightTiles=native!=null?native.GetLightTileStats():default;
        var cascade=native!=null?native.GetCascadeRecordStats():default;
        var objects=native!=null?native.GetObjectRecordStats():default;
        var geometry=native!=null?native.GetGeometryArenaStats():default;
        var actualPresentation=native!=null?native.GetPresentationStats():default;
        if(native!=null && ((validation.Enabled!=0)!=frameTextureValidation || (indirect.Enabled!=0)!=frameIndirect || (geometry.Enabled!=0)!=geometryArenaEnabled || (objects.Enabled!=0)!=frameRetained || (cascade.Enabled!=0)!=frameParallel || (lightTiles.Enabled!=0)!=frameLightTiles))return;
        lock (Gate) {
            // Update can finish/switch a phase while metadata is being read.
            // Keep every row correlated with the same accepted completed frame.
            if(!sampling || frameBatching!=batchingEnabled || frameHints!=hintsEnabled || frameWide!=wideEnabled || frameVoxelLookup!=voxelLookupEnabled || frameVoxelObject!=voxelObjectEnabled || frameTextureValidation!=textureValidationEnabled || frameIndirect!=indirectEnabled || frameRetained!=retainedEnabled || frameSubmission!=submissionEnabled || frameActorLods!=actorLodsEnabled || frameSize!=sizeEnabled || frameFar!=farEnabled || frameView!=viewEnabled || frameRecipes!=recipesEnabled || frameNewViews!=newViewsEnabled || frameModels!=modelsEnabled || frameBulkModels!=bulkModelsEnabled || frameAsyncPresent!=asyncPresentEnabled || frameGroupOcclusion!=groupOcclusionEnabled || frameParallel!=parallelEnabled || frameParallelQueue!=parallelQueueEnabled || frameLightTiles!=lightTilesEnabled)return;
            if(!skipThis)Frames.Add(((now-drawStarted)*1000.0/Stopwatch.Frequency, (now-previous)*1000.0/Stopwatch.Frequency));
            ObserveBenchmarkCompletedFrame();
            SamplePresent(native);
            if(native!=null&&RendererCpuProfiler.Enabled){CpuPhases.AddCompleted((now-drawStarted)*1000.0/Stopwatch.Frequency);SampleNativeCritical(native);if(native.NativePassReady)NativePassFrames.Add(native.GetPassProfile(true));}
            if(native!=null){IndirectFrames.Add(indirect);GeometryFrames.Add(geometry);ObjectFrames.Add(objects);SubmissionFrames.Add(native.GetSubmissionStats());LodFrames.Add(((VulkanScene)SceneField.GetValue(__instance)).GetActorLodStats());SizeFrames.Add(((VulkanScene)SceneField.GetValue(__instance)).GetActorSizeCullStats());FarFrames.Add(((VulkanScene)SceneField.GetValue(__instance)).GetFarCascadePolicyStats());ViewFrames.Add(((VulkanScene)SceneField.GetValue(__instance)).GetViewEligibilityStats());RecipeFrames.Add(native.GetRecipeStats());NewViewFrames.Add(((VulkanScene)SceneField.GetValue(__instance)).GetNewActorViewStats());ModelFrames.Add(native.GetModelBatchStats());if(LodFrames.Count%120==0)groupOcclusionSnapshot=((VulkanScene)SceneField.GetValue(__instance)).GetGroupOcclusionStats();CascadeFrames.Add(cascade);QueueWorkFrames.Add(native.GetQueueWorkStats());LightTileFrames.Add(lightTiles);}
            if(native!=null){ValidationFrames.Add(validation);presentationState=actualPresentation;NativeFrames.Add(performance);if(native.PacketProfilingEnabled)PacketFrames.Add(native.GetPacketPreparationStats());geometryStorage=((VulkanScene)SceneField.GetValue(__instance)).GeometryStorage;}
        }
    }
    public void Update()
    {
        if (finished) return;
        try
        {
            if (lifetime.Elapsed.TotalSeconds > (CameraSweepEnabled?600:240)) throw new Exception("Benchmark load/sample timeout.");
            if (!loadRequested && lifetime.Elapsed.TotalSeconds > 10 && MyGuiScreenGamePlay.Static == null)
            {
                if (BackendRuntime.ActiveBackend.ToString() != expected) throw new Exception("Requested backend was not active.\n"+VulkanDiagnostics.Report());
                MySandboxGame.Config.EnableCloud = false;MySandboxGame.Config.ExperimentalMode = true;MySandboxGame.Config.Save();
                loadRequested = true;File.WriteAllText(Path.Combine(reports,"load-requested.txt"),world);
                MySessionLoader.LoadSingleplayerSession(world);
            }
            if (!loadRequested || MySession.Static == null || !MySession.Static.Ready) return;
            if(transitionUntil>=0){if(lifetime.Elapsed.TotalSeconds<transitionUntil)return;transitionUntil=-1;sampleAt=lifetime.Elapsed.TotalSeconds;sampling=true;MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,"view.png"),debug:false,ignoreSprites:false,showNotification:false);}
            if (readyAt < 0) { readyAt = lifetime.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(reports,"ready.txt"),MySession.Static.Name); }
            double seconds = lifetime.Elapsed.TotalSeconds-readyAt;
            if(HudAuditEnabled && seconds>=10){AuditRichHud(seconds);return;}
            if(CameraSweepEnabled){SweepCamera(seconds);return;}
            if(selectionAudit && seconds>=45){if(lightingReconciliation)CaptureLightingReconciliation();else CapturePausedSelection();return;}
            if(comparePixels && seconds>=45){CapturePausedViews();return;}
            if (sampleAt < 0 && seconds >= 45)
            {
                MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(reports,"view.png"),debug:false,ignoreSprites:false,showNotification:false);
                sampleAt = lifetime.Elapsed.TotalSeconds;sampling = true;
                File.WriteAllText(Path.Combine(reports,"sampling.txt"),DateTime.UtcNow.ToString("O"));
            }
            if (sampleAt >= 0 && lifetime.Elapsed.TotalSeconds-sampleAt >= 30) Finish();
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(reports,"failure.txt"),error.ToString());finished=true;sampling=false;MySandboxGame.ExitThreadSafe(); }
    }
    void Finish()
    {
        sampling=false;finished=true;WriteFrameStateAudit();WriteNativeCritical();
        WriteBenchmarkFrameAudit();
        if(gpuComputeLast!=null)File.WriteAllText(Path.Combine(reports,"gpu-compute-timings.json"),JsonSerializer.Serialize(new{Names=new[]{"Samples","Selection","MembershipCounts","SpotUnion","RetainedGroups","GroupScan","BlockScan","SuperScan","ScanOffsets","Scatter","Arguments"},First=gpuComputeFirst,Last=gpuComputeLast,Delta=gpuComputeLast.Select((v,i)=>v-gpuComputeFirst[i]).ToArray(),Unit="Nanoseconds accumulated over completed prior-slot GPU samples"},new JsonSerializerOptions{WriteIndented=true}));
        if(gpuTopologyLast!=null)File.WriteAllText(Path.Combine(reports,"gpu-topology-counters.json"),JsonSerializer.Serialize(new{Names=new[]{"ActorAdds","ActorRemoves","FamilyChanges","SourceChanges","RootGroupChanges","Publications","Frames"},First=gpuTopologyFirst,Last=gpuTopologyLast,Delta=gpuTopologyLast.Select((v,i)=>v-gpuTopologyFirst[i]).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        if(gpuCpuCostsLast!=null)File.WriteAllText(Path.Combine(reports,"gpu-scene-cpu-costs.json"),JsonSerializer.Serialize(new{Instrumented=true,Names=new[]{"Snapshot","Capacity","Initialize","InitRanges","InitBuffers","InitPipeline","StockPublication","DeltaIndices","DeltaInputs","DeltaAppearance","FrameValidation","StockGeometry","RecordingMembership","OwnedBindings"}.Take(gpuCpuCostsLast.Length/2).ToArray(),First=gpuCpuCostsFirst,Last=gpuCpuCostsLast,Delta=gpuCpuCostsLast.Select((v,i)=>v-gpuCpuCostsFirst[i]).ToArray(),Unit="Interleaved call-count/nanosecond pairs; all calls, including rare publications; inclusive scopes"},new JsonSerializerOptions{WriteIndented=true}));
        if(gpuSceneSnapshot!=null)File.WriteAllText(Path.Combine(reports,"gpu-scene-ownership.json"),JsonSerializer.Serialize(gpuSceneSnapshot,new JsonSerializerOptions{WriteIndented=true}));
        if(gpuAdmissionSnapshot!=null)File.WriteAllText(Path.Combine(reports,"gpu-admission-audit.json"),JsonSerializer.Serialize(gpuAdmissionSnapshot,new JsonSerializerOptions{WriteIndented=true}));
        (double Cpu,double Interval)[] samples;lock(Gate) samples=Frames.ToArray();
        if(samples.Length<10) throw new Exception("Too few rendered frames.");
        var cpu=samples.Select(f=>f.Cpu).OrderBy(x=>x).ToArray();var interval=samples.Select(f=>f.Interval).OrderBy(x=>x).ToArray();
        double P(double[] a,double quantile)=>a[(int)Math.Min(a.Length-1,Math.Ceiling(a.Length*quantile)-1)];
        File.WriteAllText(Path.Combine(reports,"result.json"),JsonSerializer.Serialize(new{
            Backend=expected,RigidBatching=batchingEnabled,BatchHints=hintsEnabled,Instrumented=workloadAudit || PacketFrames.Count>0 || Environment.GetEnvironmentVariable("SE_VULKAN_PROFILE_MARKERS")=="1" || Environment.GetEnvironmentVariable("SE_VULKAN_PERF_INSTRUMENTED")=="1",World=MySession.Static.Name,Runtime=Environment.Version.ToString(),Frames=samples.Length,
            RigidCellMetres=wideEnabled?64:16,VoxelLookupCache=voxelLookupEnabled,VoxelObjectCache=voxelObjectEnabled,TextureValidationCache=textureValidationEnabled,WorldIndirect=indirectEnabled,GeometryArena=geometryArenaEnabled,RetainedObjects=retainedEnabled,RetainedSubmission=submissionEnabled,ActorLods=actorLodsEnabled,ActorSizeCulling=sizeEnabled,StockFarCascades=farEnabled,ViewEligibility=viewEnabled,WorldRecipes=recipesEnabled,NewActorViews=newViewsEnabled,ModelBatching=modelsEnabled,BulkModels=bulkModelsEnabled,AsyncPresent=asyncPresentEnabled,GroupOcclusion=groupOcclusionEnabled,ParallelCascades=parallelEnabled,ParallelQueue=parallelQueueEnabled,LightTiles=lightTilesEnabled,WarmupSeconds=secondPhase?5:45,InitialWorldWarmupSeconds=45,SampleSeconds=30,Width=MyRenderProxy.BackBufferResolution.X,Height=MyRenderProxy.BackBufferResolution.Y,
            CpuMeanMs=cpu.Average(),CpuMedianMs=P(cpu,.5),CpuP95Ms=P(cpu,.95),CpuP99Ms=P(cpu,.99),
            IntervalMeanMs=interval.Average(),IntervalP95Ms=P(interval,.95),RenderedFps=1000/interval.Average(),SkippedTelemetryFrames=skippedTelemetryFrames,KeptTelemetryFrames=KeepTelemetryFrames,
            CameraPosition=Sandbox.Game.World.MySector.MainCamera.Position.ToString(),
            Scope="Private saved camera; screenshot must be compared with the user's current view. CPU Draw through Present and consecutive Present intervals."
        },new JsonSerializerOptions{WriteIndented=true}));
        if(workloadAudit){if(occlusionDiagnosticSnapshot!=null)File.WriteAllText(Path.Combine(reports,"occlusion-diagnostics.json"),JsonSerializer.Serialize(occlusionDiagnosticSnapshot,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));if(workloadSnapshot==null)throw new Exception("Requested workload snapshot was not captured.");File.WriteAllText(Path.Combine(reports,"geometry-workload.json"),JsonSerializer.Serialize(workloadSnapshot,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));}
        if(gapFrames>0){double n=gapFrames;File.WriteAllText(Path.Combine(reports,"frame-gap.json"),JsonSerializer.Serialize(new{Frames=gapFrames,
            HarnessPrefixSampleMs=gapHarnessSample/n,PresentBodyMs=gapPresentBody/n,HarnessPostfixMs=gapPostfix/n,LoopMs=gapLoop/n,FrameWaiterMs=gapWaiter/n,BeforeRenderMs=gapBeforeRender/n,OtherLoopMs=(gapLoop-gapWaiter-gapBeforeRender)/n,
            Scope="Per sampled frame. HarnessPrefixSample: the harness frame sample in the Present prefix (pipelined Vulkan only; it starts where CpuMeanMs ends). PresentBody: prefix end to postfix, the renderer's Present. Loop: postfix exit to the next Draw; it contains the frame waiter, BeforeRender, the window update and the render/update sync wait."},new JsonSerializerOptions{WriteIndented=true}));}
        File.WriteAllText(Path.Combine(reports,"vulkan-status.txt"),VulkanDiagnostics.Report());
        if(expected=="Vulkan"){GeometryStorageStats storage;lock(Gate)storage=geometryStorage;File.WriteAllText(Path.Combine(reports,"geometry-storage.json"),JsonSerializer.Serialize(storage,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));}
        if(BulkModelFrames.Count>0){var rows=BulkModelFrames.ToArray();var averages=typeof(NativeModelGroupStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>rows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"bulk-models.json"),JsonSerializer.Serialize(new{Frames=rows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
        if(PresentSamples.Count>0){var rows=PresentSamples.ToArray();var values=rows.Where(r=>r.IntervalNs>0).Select(r=>r.IntervalNs/1e6).OrderBy(v=>v).ToArray();double Q(double q)=>values[(int)Math.Ceiling(q*values.Length)-1];File.WriteAllText(Path.Combine(reports,"present-completions.json"),JsonSerializer.Serialize(new{Samples=rows.Length,LostSamples=lostPresentSamples,MeanMs=values.Average(),P95Ms=Q(.95),P99Ms=Q(.99),FirstSequence=rows.First().Sequence,LastSequence=rows.Last().Sequence,Scope="Actual queue-present call completion intervals; no scanout claim and no wait for unfinished present. Benchmark telemetry enabled equally for comparison phases."},new JsonSerializerOptions{WriteIndented=true}));}
        if(JobStatsFirst!=null&&JobStatsLast!=null)try{WriteJobStats(reports);}catch(MissingMethodException){}catch(TypeLoadException){}
        if(PresentFlows.Count>1){NativePresentFlowStats[] flow;lock(Gate)flow=PresentFlows.ToArray();var first=flow[0];var last=flow[flow.Length-1];ulong completed=last.CompletedCalls-first.CompletedCalls,intervals=last.TotalIntervalNs-first.TotalIntervalNs;File.WriteAllText(Path.Combine(reports,"present-flow.json"),JsonSerializer.Serialize(new{Frames=flow.Length,Enabled=asyncPresentEnabled,CompletedCalls=completed,CompletionIntervalMeanMs=completed>0?intervals/(double)completed/1000000:-1,CompletedPresentCallFps=intervals>0?completed*1e9/intervals:0,MeanCallMs=completed>0?(last.TotalCallNs-first.TotalCallNs)/(double)completed/1000000:-1,First=first,Last=last,Scope="Native vkQueuePresentKHR call completion cadence, not scanout FPS. Counts may lag submitted frames by one. Distinct from render-thread EndFrame return cadence; GPU and completed present throughput must be verified."},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));}
        if(PacketFrames.Count>0)
        {
            RendererCpuProfiler.Snapshot phases;lock(Gate)phases=CpuPhases.Capture();
            File.WriteAllText(Path.Combine(reports,"renderer-cpu-phases.json"),JsonSerializer.Serialize(new{Instrumented=true,
                Scope="Inclusive QPC wall scopes; CPU cycles for the owning render thread only. Workers are excluded. Fine actor/part buckets are wall-only. OS CPU milliseconds are quantized and exposed only for the whole-frame aggregate; no phase CPU-ms claim. Cycles are not converted to time. Allocations exclude workers/native. Rows overlap and must not be summed.",Data=phases},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            WriteMessageCensus(reports);
            RendererCpuProfiler.TailSnapshot tails;lock(Gate)tails=CpuPhases.CaptureTails();
            File.WriteAllText(Path.Combine(reports,"renderer-cpu-tails.json"),JsonSerializer.Serialize(new{Instrumented=true,
                Scope="Same accepted completed diagnostic frames. Phase rows overlap. MeanOnSlowest5PercentMs selects frames by full profiler Frame wall time, not each phase's own quantile. Slowest phase arrays use Rows order. GC counts are process-wide collection overlap, not measured pause durations or proof of causation. CompletedDrawMs also includes harness completion overhead outside profiler Frame.",Data=tails},new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            PacketPreparationStats[] detail;lock(Gate)detail=PacketFrames.ToArray();
            var averages=typeof(PacketPreparationStats).GetFields().Where(f=>f.Name!="Enabled").ToDictionary(f=>f.Name,f=>detail.Average(n=>Convert.ToDouble(f.GetValue(n))));
            File.WriteAllText(Path.Combine(reports,"packet-preparation.json"),JsonSerializer.Serialize(new{Enabled=true,Instrumented=true,Frames=detail.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(NativePassFrames.Count>1){var first=NativePassFrames[0];var last=NativePassFrames[NativePassFrames.Count-1];double frames=last.Publishes-first.Publishes;var detail=typeof(NativePassProfile).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>(Convert.ToDouble(f.GetValue(last))-Convert.ToDouble(f.GetValue(first)))/Math.Max(1,frames));File.WriteAllText(Path.Combine(reports,"native-pass-preparation.json"),JsonSerializer.Serialize(new{Instrumented=true,Frames=frames,PerFrame=detail,Scope="Nested native render-owner wall scopes, no GPU calls or waits in these boundaries. EmitUs is inside PublishUs; RequestsUs is inside PrepareUs; ActorParseUs and ActorApplyUs are inside CommitsUs. Do not add nested rows or treat these as ordinary FPS."},new JsonSerializerOptions{WriteIndented=true}));}
        if(NativeFrames.Count>0)
        {
            NativePerformanceStats[] native;lock(Gate)native=NativeFrames.ToArray();
            var averages=typeof(NativePerformanceStats).GetFields().Where(f=>f.Name!="Size" && f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>native.Average(n=>(uint)f.GetValue(n)));
            File.WriteAllText(Path.Combine(reports,"native-performance.json"),JsonSerializer.Serialize(averages,new JsonSerializerOptions{WriteIndented=true}));
        }
        if(ValidationFrames.Count>0){NativeQueueValidationStats[] values;lock(Gate)values=ValidationFrames.ToArray();var averages=typeof(NativeQueueValidationStats).GetFields().Where(f=>f.Name!="Size" && f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>values.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"queue-validation.json"),JsonSerializer.Serialize(new{Frames=values.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
        if(expected=="Vulkan")
        {
            if(groupOcclusionSnapshot!=null)File.WriteAllText(Path.Combine(reports,"group-occlusion.json"),JsonSerializer.Serialize(groupOcclusionSnapshot,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            ModelBatchStats[] modelRows;lock(Gate)modelRows=ModelFrames.ToArray();
            if(modelRows.Length>0){var averages=typeof(ModelBatchStats).GetFields().ToDictionary(f=>f.Name,f=>modelRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"model-batches.json"),JsonSerializer.Serialize(new{Frames=modelRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NewActorViewStats[] newViewRows;lock(Gate)newViewRows=NewViewFrames.ToArray();
            if(newViewRows.Length>0){var averages=typeof(NewActorViewStats).GetFields().ToDictionary(f=>f.Name,f=>newViewRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"new-actor-views.json"),JsonSerializer.Serialize(new{Frames=newViewRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeRecipeStats[] recipeRows;lock(Gate)recipeRows=RecipeFrames.ToArray();
            if(recipeRows.Length>0){var averages=typeof(NativeRecipeStats).GetFields().ToDictionary(f=>f.Name,f=>recipeRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"world-recipes.json"),JsonSerializer.Serialize(new{Frames=recipeRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            ViewEligibilityStats[] viewRows;lock(Gate)viewRows=ViewFrames.ToArray();
            if(viewRows.Length>0){var averages=typeof(ViewEligibilityStats).GetFields().ToDictionary(f=>f.Name,f=>viewRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"view-eligibility.json"),JsonSerializer.Serialize(new{Frames=viewRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            FarCascadePolicyStats[] farRows;lock(Gate)farRows=FarFrames.ToArray();
            if(farRows.Length>0){var averages=typeof(FarCascadePolicyStats).GetFields().ToDictionary(f=>f.Name,f=>farRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"far-cascade-policy.json"),JsonSerializer.Serialize(new{Frames=farRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            ActorSizeCullStats[] sizeRows;lock(Gate)sizeRows=SizeFrames.ToArray();
            if(sizeRows.Length>0){var averages=typeof(ActorSizeCullStats).GetFields().ToDictionary(f=>f.Name,f=>sizeRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"actor-size-culling.json"),JsonSerializer.Serialize(new{Frames=sizeRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            ActorLodStats[] lodRows;lock(Gate)lodRows=LodFrames.ToArray();
            if(lodRows.Length>0){var averages=typeof(ActorLodStats).GetFields().ToDictionary(f=>f.Name,f=>lodRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"actor-lods.json"),JsonSerializer.Serialize(new{Frames=lodRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            SceneSubmissionStats[] submissions;lock(Gate)submissions=SubmissionFrames.ToArray();
            if(submissions.Length>0){var averages=typeof(SceneSubmissionStats).GetFields().ToDictionary(f=>f.Name,f=>submissions.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"retained-submission.json"),JsonSerializer.Serialize(new{Frames=submissions.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeLightTileStats[] lightRows;lock(Gate)lightRows=LightTileFrames.ToArray();
            if(lightRows.Length>0){var averages=typeof(NativeLightTileStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>lightRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"light-tiles.json"),JsonSerializer.Serialize(new{Frames=lightRows.Length,AssignmentScope="Completed frame after owning slot wait; CompletedFrame differs from current Frame; no added GPU wait",Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeQueueWorkStats[] queueRows;lock(Gate)queueRows=QueueWorkFrames.ToArray();
            if(queueRows.Length>0){var averages=typeof(NativeQueueWorkStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>queueRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"queue-work.json"),JsonSerializer.Serialize(new{Frames=queueRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeCascadeRecordStats[] cascades;lock(Gate)cascades=CascadeFrames.ToArray();
            if(cascades.Length>0){var averages=typeof(NativeCascadeRecordStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>cascades.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"cascade-recording.json"),JsonSerializer.Serialize(new{Frames=cascades.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeObjectRecordStats[] objectRows;lock(Gate)objectRows=ObjectFrames.ToArray();
            if(objectRows.Length>0){var averages=typeof(NativeObjectRecordStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>objectRows.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"object-records.json"),JsonSerializer.Serialize(new{Frames=objectRows.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeGeometryArenaStats[] arena;lock(Gate)arena=GeometryFrames.ToArray();
            if(arena.Length>0){var averages=typeof(NativeGeometryArenaStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>arena.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"geometry-arena.json"),JsonSerializer.Serialize(new{Frames=arena.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            NativeIndirectStats[] indirect;lock(Gate)indirect=IndirectFrames.ToArray();
            if(indirect.Length>0){var averages=typeof(NativeIndirectStats).GetFields().Where(f=>f.Name!="Size"&&f.Name!="AbiVersion").ToDictionary(f=>f.Name,f=>indirect.Average(v=>Convert.ToDouble(f.GetValue(v))));File.WriteAllText(Path.Combine(reports,"indirect-submission.json"),JsonSerializer.Serialize(new{Frames=indirect.Length,Averages=averages},new JsonSerializerOptions{WriteIndented=true}));}
            var profiler=typeof(MyVulkanRender).Assembly.GetType("ClientPlugin.Diagnostics.FrameProfiler",throwOnError:true);
            NativePresentationStats presentation;lock(Gate)presentation=presentationState;
            File.WriteAllText(Path.Combine(reports,"presentation-state.json"),JsonSerializer.Serialize(presentation,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            object view=profiler.GetMethod("Capture",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{false});
            File.WriteAllText(Path.Combine(reports,"frame-profile.json"),JsonSerializer.Serialize(view,view.GetType(),new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
        }
        if(!secondPhase && (compareBatching || compareHints || compareCells || compareVoxelLookup || compareVoxelObjects || compareTextureValidation || compareIndirect || compareRetained || compareParallel || compareLightTiles || compareSubmission || compareActorLods || compareGroupOcclusion || compareParallelQueue || compareSize || compareFar || compareView || compareRecipes || compareNewViews || compareModels || compareAsyncPresent || compareBulkModels))
        {
            secondPhase=true;if(compareBulkModels)bulkModelsEnabled=!bulkModelsEnabled;if(compareModels)modelsEnabled=!modelsEnabled;if(compareNewViews)newViewsEnabled=!newViewsEnabled;if(compareRecipes)recipesEnabled=!recipesEnabled;if(compareView)viewEnabled=!viewEnabled;if(compareAsyncPresent)asyncPresentEnabled=!asyncPresentEnabled;if(compareFar)farEnabled=!farEnabled;if(compareSize)sizeEnabled=!sizeEnabled;if(compareParallelQueue)parallelQueueEnabled=!parallelQueueEnabled;if(compareGroupOcclusion)groupOcclusionEnabled=!groupOcclusionEnabled;if(compareActorLods)actorLodsEnabled=!actorLodsEnabled;if(compareSubmission)submissionEnabled=!submissionEnabled;if(compareLightTiles)lightTilesEnabled=!lightTilesEnabled;if(compareParallel)parallelEnabled=!parallelEnabled;if(compareRetained)retainedEnabled=!retainedEnabled;if(compareBatching)batchingEnabled=false;if(compareHints)hintsEnabled=false;if(compareCells)wideEnabled=!wideEnabled;if(compareVoxelLookup)voxelLookupEnabled=!voxelLookupEnabled;if(compareVoxelObjects)voxelObjectEnabled=!voxelObjectEnabled;if(compareTextureValidation)textureValidationEnabled=!textureValidationEnabled;if(compareIndirect)indirectEnabled=!indirectEnabled;
            reports=Path.Combine(reports,compareBulkModels?(bulkModelsEnabled?"native-model-groups":"managed-model-groups"):compareModels?(modelsEnabled?"model-batches":"part-batches"):compareNewViews?(newViewsEnabled?"stock-new-views":"shared-actor-views"):compareRecipes?(recipesEnabled?"native-recipes":"legacy-recipe-submission"):compareView?(viewEnabled?"view-eligibility":"legacy-view-preparation"):compareAsyncPresent?(asyncPresentEnabled?"async-present":"sync-present"):compareFar?(farEnabled?"stock-far-cascades":"legacy-far-cascades"):compareSize?(sizeEnabled?"stock-size-culling":"legacy-size-selection"):compareParallelQueue?(parallelQueueEnabled?"parallel-queue":"serial-queue"):compareGroupOcclusion?(groupOcclusionEnabled?"group-occlusion":"legacy-occlusion"):compareActorLods?(actorLodsEnabled?"stock-actor-lods":"base-actor-models"):compareSubmission?(submissionEnabled?"retained-submission":"legacy-submission"):compareLightTiles?(lightTilesEnabled?"gpu-light-tiles":"full-light-lists"):compareParallel?(parallelEnabled?"parallel-cascades":"serial-cascades"):compareRetained?(retainedEnabled?"retained-objects":"legacy-objects"):compareIndirect?(indirectEnabled?"indirect-world":"direct-world"):compareBatching?"without-rigid-batching":compareHints?"without-batch-hints":compareVoxelLookup?(voxelLookupEnabled?"cached-voxel-lookup":"legacy-voxel-lookup"):compareVoxelObjects?(voxelObjectEnabled?"cached-voxel-objects":"reflected-voxel-objects"):compareTextureValidation?(textureValidationEnabled?"cached-texture-validation":"legacy-texture-validation"):wideEnabled?"64m-batches":"16m-batches");Directory.CreateDirectory(reports);
            lock(Gate){Frames.Clear();skippedTelemetryFrames=0;NativeFrames.Clear();NativePassFrames.Clear();PacketFrames.Clear();CpuPhases.Reset();ValidationFrames.Clear();IndirectFrames.Clear();GeometryFrames.Clear();ObjectFrames.Clear();SubmissionFrames.Clear();LodFrames.Clear();SizeFrames.Clear();FarFrames.Clear();ViewFrames.Clear();RecipeFrames.Clear();NewViewFrames.Clear();ModelFrames.Clear();PresentFlows.Clear();PresentSamples.Clear();JobStatsFirst=JobStatsLast=null;lastPresentSample=0;lostPresentSamples=false;BulkModelFrames.Clear();CascadeFrames.Clear();QueueWorkFrames.Clear();LightTileFrames.Clear();}
            transitionUntil=lifetime.Elapsed.TotalSeconds+5;sampleAt=-2;finished=false;sampling=false;return;
        }
        MySandboxGame.ExitThreadSafe();
    }
    public void Dispose() { sampling=false;ReleasePixelPause();ReleaseSelectionPause();harmony?.UnpatchAll(harmony.Id); }
}

