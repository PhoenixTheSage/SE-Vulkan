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
    }
    [ThreadStatic]static FrameEmission boundEmission;
    FrameEmission current;readonly FrameEmission[] rotation=new FrameEmission[2];int batchFrameCounter;
    // The calling thread's emission: the bound one, otherwise the current one.
    FrameEmission E{get{var bound=boundEmission;return bound!=null&&ReferenceEquals(bound.Owner,this)?bound:current;}}
    void InitializeEmissions(){rotation[0]=current=new FrameEmission(this);rotation[1]=new FrameEmission(this);BindAliases();}
    void BindAliases(){worldPackets=current.WorldPackets;glassPackets=current.GlassPackets;}
    // Gate for milestone 1 (SE_VULKAN_EMISSION_ROTATE=1, or the property for the same-process lane): every frame's
    // packet collection starts on the other emission. Output must be identical; a difference is per-frame state that
    // lives outside the emission. Takes effect at the next BeginPacketCollection; the camera, set just before it, carries over.
    public bool EmissionRotateEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_EMISSION_ROTATE")=="1";
    void RotateEmission(){if(!EmissionRotateEnabled||boundEmission!=null)return;var camera=current.CameraPosition;current=ReferenceEquals(current,rotation[0])?rotation[1]:rotation[0];current.CameraPosition=camera;BindAliases();}
}
