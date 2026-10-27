using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// Phase 4 frame split, milestone 2, slice 2 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): the scene
// containers. Build owns the candidate tree, static groups, occlusion groups, the native pass/cell/GPU ownership
// state and its dirty sets; Apply (messages and the scene update) no longer writes them.
// - Work Apply used to do on that state (ownership releases from SetModel and Remove, candidate-tree updates,
//   group removal) is queued as BuildOps in issue order and replayed at the start of BuildScene.
// - Dirty marks made during Apply (native pass, cells, GPU, occlusion groups, group membership, occlusion revision)
//   collect on the Apply side and fold into Build's at LatchFrame.
// - Build reads buildActors, a copy of the actors dictionary kept by replaying Apply's adds and removes at
//   LatchFrame (the same operations in the same order, so it enumerates in the same order).
// Clear() runs with Build joined (unload), so it flushes the queue and works on Build state directly.
public sealed partial class VulkanScene
{
    enum BuildOpKind:byte{ReleaseOwnership,RemoveActor,SpatialRefresh}
    struct BuildOp{public BuildOpKind Kind;public ActorData Data;public BoundingBoxD Sphere;public bool Removed,Static;}
    readonly List<BuildOp> buildOps=new();
    readonly Dictionary<uint,ActorData> buildActors=new();
    readonly List<(uint Id,ActorData Data)> actorLog=new();
    // Apply-side dirty marks, folded into the Build sets by LatchFrame.
    readonly HashSet<ActorData> applyPassDirty=new(),applyCellDirty=new(),applyCellPoseDirty=new(),applyGpuDirty=new(),applyGroupDirty=new();
    bool applyMembershipDirty;long applyVisibilityRevision;
    const byte DirtyGpuBinding=1,DirtyGpuAppearance=2,DirtyPassBinding=4,DirtyPassAppearance=8;
    void LogActor(uint id,ActorData data)=>actorLog.Add((id,data));
    void SyncBuildActors()
    {
        foreach(var (id,data) in actorLog){if(data!=null)buildActors.Add(id,data);else buildActors.Remove(id);}
        actorLog.Clear();
    }
    void MarkMembershipDirty(){if(building)groupMembershipDirty=true;else applyMembershipDirty=true;}
    void FoldApplyState()
    {
        SyncBuildActors();
        nativePassDirty.UnionWith(applyPassDirty);cellDirty.UnionWith(applyCellDirty);cellPoseDirty.UnionWith(applyCellPoseDirty);
        foreach(var actor in applyGpuDirty)
        {
            gpuDirty.Add(actor);byte bits=actor.ApplyDirtyBits;actor.ApplyDirtyBits=0;
            actor.GpuBindingDirty|=(bits&DirtyGpuBinding)!=0;actor.GpuAppearanceDirty|=(bits&DirtyGpuAppearance)!=0;
            actor.PassBindingDirty|=(bits&DirtyPassBinding)!=0;actor.PassAppearanceDirty|=(bits&DirtyPassAppearance)!=0;
        }
        applyPassDirty.Clear();applyCellDirty.Clear();applyCellPoseDirty.Clear();applyGpuDirty.Clear();
        foreach(var data in applyGroupDirty)DirtyGroupNow(data);applyGroupDirty.Clear();
        if(applyMembershipDirty){groupMembershipDirty=true;applyMembershipDirty=false;}
        visibilityRevision+=applyVisibilityRevision;applyVisibilityRevision=0;
    }
    void FlushBuildOps()
    {
        foreach(var op in buildOps)
        {
            var data=op.Data;
            switch(op.Kind)
            {
                case BuildOpKind.ReleaseOwnership:RemoveNativePassActor(data,false,markSpots:false);ReleaseCell(data,false);break;
                case BuildOpKind.RemoveActor:RemoveBuildState(data);break;
                case BuildOpKind.SpatialRefresh:
                    if(op.Removed){if(data.Proxy>=0){actorIndex.RemoveProxy(data.Proxy);data.Proxy=-1;}break;}
                    var sphere=op.Sphere;
                    if(!InManagedIndex(data)){if(data.Proxy>=0){actorIndex.RemoveProxy(data.Proxy);data.Proxy=-1;}}
                    else if(data.Proxy<0)data.Proxy=actorIndex.AddProxy(ref sphere,data,0);
                    else actorIndex.MoveProxy(data.Proxy,ref sphere,Vector3D.Zero);
                    if(op.Static)staticGroups.Add(data);else staticGroups.Remove(data);
                    break;
            }
        }
        buildOps.Clear();
    }
    // A removed actor's Build state: native ownership, dirty entries, its occlusion group and candidate proxy.
    void RemoveBuildState(ActorData actor)
    {
        RemoveNativePassActor(actor,false,markSpots:false);nativePassDirty.Remove(actor);ReleaseCell(actor,false);cellDirty.Remove(actor);cellPoseDirty.Remove(actor);
        occlusionGroups.Remove(actor.Actor);
        if(actor.Proxy>=0)actorIndex.RemoveProxy(actor.Proxy);
        staticGroups.Remove(actor);
    }
    long BuildActorChecks,BuildActorMismatches;
    void VerifyBuildActors()
    {
        BuildActorChecks++;
        bool same=buildActors.Count==actors.Count;
        if(same)foreach(var pair in actors)if(!buildActors.TryGetValue(pair.Key,out var data)||!ReferenceEquals(data,pair.Value)){same=false;break;}
        if(!same){BuildActorMismatches++;FramePacketMismatches++;FramePacketFirstMismatch??="buildActors at frame "+FrameCounter;}
    }
}
