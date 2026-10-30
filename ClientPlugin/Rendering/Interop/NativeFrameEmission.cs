using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Interop;

// Plan 3 milestone 1: the per-frame emission state (packets, scopes and batch caches) lives in a FrameEmission. A
// thread uses the emission bound to it (BindEmission), otherwise the backend's current one, so a later second build
// stage can emit one frame while another thread packs the previous. Batch caches are per emission because each
// cached batch carries the per-frame instance array its packets point at. Model batches, bulk models, pending
// resident-owner changes and the logical-selection capture stay on the backend (opt-in or diagnostic paths).
public sealed partial class NativeBackend
{
    internal sealed class FrameEmission
    {
        internal readonly NativeBackend Owner;
        internal FrameEmission(NativeBackend owner){Owner=owner;}
        internal readonly List<NativeMeshDraw> FrameMeshes=new();
        internal bool Collecting;
        internal Vector3D CameraPosition;
        internal readonly List<WorldPacket> WorldPackets=new();
        internal int Precollected,PrecollectedFor=-1;
        internal readonly Dictionary<object,MainGroupState> MainGroups=new();
        internal MainGroupState MainGroup;internal uint MainGroupEpoch=1;internal uint MainPart;
        internal readonly List<uint> MainOwnerIds=new();internal readonly List<MainGroupState> MainOwnerGroups=new();
        internal readonly List<MainGroupState> MainGroupPool=new();internal int MainGroupsRented;
        internal NativeOldPartOrder OldPartOrder;internal uint PacketOwner;
        internal int BatchFrame;internal uint RigidBatchPackets,LogicalRigidDraws;internal SceneSubmissionStats SubmissionStats;
        internal readonly Dictionary<RigidBatchKey,RigidBatch> RigidBatches=new();internal readonly List<RigidBatch> RigidBatchSlots=new();internal uint RigidCacheEpoch;
        internal readonly List<NativeHighlightDraw> HighlightPackets=new();
        internal readonly List<NativeDecalDraw> DecalPackets=new();
        internal readonly List<(WorldPacket Geometry,byte[] Material,float? OldDecalDistance)> GlassPackets=new();
        internal readonly List<NativeBillboardDraw> BillboardPackets=new();
        internal NativeShadowExclusion[] ShadowExclusions=Array.Empty<NativeShadowExclusion>();
        // Stage gate (Plan 3): what QueueWorld reads that stage A used to keep on the backend. The model batches this
        // frame touched; the recycle blocks its pack collected (and their spare); the resident owner changes stage A
        // sealed at its end, which stage B flushes with this frame's main order.
        internal readonly List<ModelBatch> ActiveModelBatches=new();
        internal byte[][] RecycleBlocks=Array.Empty<byte[]>(),RecycleSpare=Array.Empty<byte[]>();
        internal readonly List<MainOwnerOrder> ResidentSets=new();internal readonly List<uint> ResidentRemovals=new();
    }
    [ThreadStatic]static FrameEmission boundEmission;
    // Stage B binds the emission stage A filled (Plan 3); the scope restores the previous binding.
    internal FrameEmission CurrentEmission=>current;
    internal readonly struct EmissionScope:IDisposable {readonly FrameEmission previous;readonly bool bound;internal EmissionScope(FrameEmission emission){previous=boundEmission;boundEmission=emission;bound=true;}public void Dispose(){if(bound)boundEmission=previous;}}
    internal EmissionScope BindEmission(FrameEmission emission)=>new(emission);
    FrameEmission current;readonly FrameEmission[] rotation=new FrameEmission[2];int batchFrameCounter;
    // The calling thread's emission: the bound one, otherwise the current one.
    FrameEmission E{get{var bound=boundEmission;return bound!=null&&ReferenceEquals(bound.Owner,this)?bound:current;}}
    void InitializeEmissions(){rotation[0]=current=new FrameEmission(this);rotation[1]=new FrameEmission(this);BindAliases();}
    void BindAliases(){worldPackets=current.WorldPackets;glassPackets=current.GlassPackets;}
    // Gate for milestone 1 (SE_VULKAN_EMISSION_ROTATE=1, or the property for the same-process lane): every frame's
    // packet collection starts on the other emission. Output must be identical; a difference is per-frame state that
    // lives outside the emission. Takes effect at the next BeginPacketCollection; the camera, set just before it, carries over.
    public bool EmissionRotateEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_EMISSION_ROTATE")=="1";
    // Census phase of the calling thread (Plan 3 milestone 3; read by SE_VULKAN_PIPELINE_REPORT's PhaseCallSites).
    // Only with the census on; a native library without the export ignores it.
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]delegate int CensusPhaseFn(IntPtr ctx,uint phase);
    static CensusPhaseFn censusPhase;static readonly bool CensusEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_PIPELINE_REPORT")!=null;static bool censusMissing;
    public void SetCensusPhase(uint phase){if(!CensusEnabled||censusMissing)return;if(censusPhase==null){try{censusPhase=(CensusPhaseFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_census_phase"),typeof(CensusPhaseFn));}catch(Exception){censusMissing=true;return;}}censusPhase(context,phase);}
    // Build capture (Plan 3 milestone 3): stage A's deferrable native calls queue in issue order until stage B
    // replays them, so they reach the frame stage B finishes.
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]delegate int ContextToggleFn(IntPtr ctx,uint value);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]delegate int ContextOnlyFn(IntPtr ctx);
    ContextToggleFn buildCapture,buildStages;ContextOnlyFn replayCapture,sealCapture;
    public void SetBuildCapture(bool enable){buildCapture??=(ContextToggleFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_build_capture"),typeof(ContextToggleFn));Check(buildCapture(context,enable?1u:0u),context);}
    public void ReplayBuildCapture(){replayCapture??=(ContextOnlyFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_replay_build_capture"),typeof(ContextOnlyFn));Check(replayCapture(context),context);}
    // Stage B reports its captured frame's world queued; the next stage A's first scene-changing call waits for it.
    public void StageQueued(){stageQueued??=(ContextOnlyFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_stage_queued"),typeof(ContextOnlyFn));Check(stageQueued(context),context);}
    ContextOnlyFn stageQueued;
    // Two stages: called by QueueWorld once the world queue and the cell emission are done (the stage gate's point).
    internal Action StageReportHook;
    // End of stage A: the frame's resident owner changes move into its emission, so stage B flushes exactly them while
    // the next stage A records new ones.
    internal void SealFrameEmission(){
        var e=E;e.ResidentSets.Clear();e.ResidentRemovals.Clear();
        foreach(var entry in residentSets)e.ResidentSets.Add(new MainOwnerOrder{Owner=entry.Key,Group=entry.Value});
        foreach(uint owner in residentRemovals)e.ResidentRemovals.Add(owner);residentSets.Clear();residentRemovals.Clear();}
    // Ends stage A's capture: its calls wait, in frame order, for that frame's stage B to replay them.
    public void SealBuildCapture(){sealCapture??=(ContextOnlyFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_seal_build_capture"),typeof(ContextOnlyFn));Check(sealCapture(context),context);}
    // Two build stages (Plan 3 milestone 5): queue-time pass snapshots per submission and lazily opened upload batches.
    public void SetBuildStages(bool enable){buildStages??=(ContextToggleFn)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_build_stages"),typeof(ContextToggleFn));Check(buildStages(context,enable?1u:0u),context);}
    void RotateEmission(){if(!EmissionRotateEnabled||boundEmission!=null)return;var camera=current.CameraPosition;current=ReferenceEquals(current,rotation[0])?rotation[1]:rotation[0];current.CameraPosition=camera;BindAliases();}
}
