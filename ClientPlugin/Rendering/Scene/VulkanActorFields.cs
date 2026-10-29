using System;
using System.Collections.Generic;
using ClientPlugin.Rendering.Content;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

// Phase 4 frame split, milestone 2, slice 3 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): the per-actor
// fields that messages and the scene update write and Build reads. ActorData's own fields are Build's view; Apply
// writes ActorData.A through W(), which marks the actor, and LatchFrame copies A into the Build fields
// (FoldActorFields). Materials, mutated in place by messages, reach Build as a clone when they change.
// Messages that reset Build-owned state (a model change resets the LOD and submission state; a material flag
// conversion resets the stock LOD; old-identity resolution) become events applied at the latch, and the
// old-pipeline decal registration the game updater triggers runs in Build's prologue.
public sealed partial class VulkanScene
{
    sealed class ApplyFields
    {
        public VulkanModel Model;
        public uint InstanceBuffer=uint.MaxValue;public int InstanceStart,InstanceCount;
        public Matrix[] StaticInstances,SkinMatrices;
        public MySkeletonBoneDescription[] Skeleton;public int[] SkeletonIndices;
        public Vector3 Hsv;public float Dithering;
        public bool Character,TrackOldIdentity=true;public string ConvertedOldMaterial;
        public readonly Dictionary<string,MaterialOverride> Materials=new(StringComparer.Ordinal);public uint MaterialRevision;
        public bool DynamicShadow,HaveShadowPose;public Vector3D ShadowCenter;public float ShadowRadius;
        // Apply-only: frames since a dynamic caster last moved (not folded; Build never reads it).
        public int ShadowStill;
        public bool SkinDirty;public byte Events;
    }
    const byte EventResetModel=1,EventResetLod=2,EventEnsureOld=4;
    readonly HashSet<ActorData> fieldDirty=new();
    readonly List<ActorData> applyOldDecal=new(),buildOldDecal=new();
    // Apply's write access to an actor's fields: marks it for the latch.
    ApplyFields W(ActorData data){fieldDirty.Add(data);return data.A;}
    static bool KnownStockOldPipelineApply(ActorData actor)=>actor.A.Character||actor.A.InstanceBuffer!=uint.MaxValue||actor.A.ConvertedOldMaterial!=null||(actor.Flags&RenderFlags.ForceOldPipeline)!=0;
    // The game updater asks for old-pipeline decal registration during Apply; it runs in Build's prologue.
    void RequestOldDecalShaders(ActorData data){if(building)RegisterOldDecalShaders(data);else applyOldDecal.Add(data);}
    void FoldActorFields()
    {
        foreach(var data in fieldDirty)
        {
            var a=data.A;
            data.Model=a.Model;data.InstanceBuffer=a.InstanceBuffer;data.InstanceStart=a.InstanceStart;data.InstanceCount=a.InstanceCount;
            data.StaticInstances=a.StaticInstances;data.SkinMatrices=a.SkinMatrices;data.Skeleton=a.Skeleton;data.SkeletonIndices=a.SkeletonIndices;
            data.Hsv=a.Hsv;data.Dithering=a.Dithering;data.Character=a.Character;data.TrackOldIdentity=a.TrackOldIdentity;data.ConvertedOldMaterial=a.ConvertedOldMaterial;
            data.DynamicShadow=a.DynamicShadow;data.HaveShadowPose=a.HaveShadowPose;data.ShadowCenter=a.ShadowCenter;data.ShadowRadius=a.ShadowRadius;
            if(data.MaterialRevision!=a.MaterialRevision||data.Materials.Count!=a.Materials.Count)data.Materials=CloneMaterials(a.Materials);
            data.MaterialRevision=a.MaterialRevision;
            data.SkinDirty|=a.SkinDirty;a.SkinDirty=false;
            if((a.Events&EventResetModel)!=0)ResetModelState(data);
            if((a.Events&EventResetLod)!=0)data.Lod=new StockActorLodState();
            if((a.Events&EventEnsureOld)!=0)EnsureOldModelIdentity(data);
            a.Events=0;
        }
        fieldDirty.Clear();
        buildOldDecal.AddRange(applyOldDecal);applyOldDecal.Clear();
    }
    static Dictionary<string,MaterialOverride> CloneMaterials(Dictionary<string,MaterialOverride> source)
    {
        var copy=new Dictionary<string,MaterialOverride>(source.Count,StringComparer.Ordinal);
        foreach(var item in source)copy.Add(item.Key,new MaterialOverride{Color=item.Value.Color,Emissivity=item.Value.Emissivity,Textures=item.Value.Textures,Flags=item.Value.Flags});
        return copy;
    }
    // What SetModel reset on the Build side before the split.
    void ResetModelState(ActorData actor)
    {
        var model=actor.Model;actor.OldModel=null;
        actor.RenderModel=model;actor.RenderLod=0;actor.Lod=new StockActorLodState();actor.NewLod.Reset();actor.HaveNewViewCamera=false;actor.LastNewSpotLod=-1;actor.Representations.Clear();
        actor.LodShadowModel=actor.LodShadowNext=null;actor.LodShadowAlpha=0;
        actor.PartFrames=null;actor.Submission=null;
        actor.SubmissionOpaque=model!=null;
        if(model!=null)foreach(var part in model.Parts)if((part.Flags&4)!=0){actor.SubmissionOpaque=false;break;}
        actor.Packed=null;
    }
    void RunOldDecalRequests()
    {
        foreach(var data in buildOldDecal)if(buildActors.TryGetValue(data.Id,out var current)&&ReferenceEquals(current,data))RegisterOldDecalShaders(data);
        buildOldDecal.Clear();
    }
    // Verify mode: after the latch the Build fields equal Apply's.
    string FieldMismatch(ActorData data)
    {
        var a=data.A;
        if(!ReferenceEquals(data.Model,a.Model))return "Model";
        if(data.InstanceBuffer!=a.InstanceBuffer||data.InstanceStart!=a.InstanceStart||data.InstanceCount!=a.InstanceCount)return "Instances";
        if(!ReferenceEquals(data.StaticInstances,a.StaticInstances)||!ReferenceEquals(data.SkinMatrices,a.SkinMatrices)||!ReferenceEquals(data.Skeleton,a.Skeleton)||!ReferenceEquals(data.SkeletonIndices,a.SkeletonIndices))return "Arrays";
        if(data.Hsv!=a.Hsv||data.Dithering!=a.Dithering)return "Appearance";
        if(data.Character!=a.Character||data.TrackOldIdentity!=a.TrackOldIdentity||data.ConvertedOldMaterial!=a.ConvertedOldMaterial)return "Pipeline";
        if(data.MaterialRevision!=a.MaterialRevision||data.Materials.Count!=a.Materials.Count)return "Materials";
        if(data.DynamicShadow!=a.DynamicShadow||data.HaveShadowPose!=a.HaveShadowPose||data.ShadowCenter!=a.ShadowCenter||data.ShadowRadius!=a.ShadowRadius)return "ShadowPose";
        return null;
    }
}
