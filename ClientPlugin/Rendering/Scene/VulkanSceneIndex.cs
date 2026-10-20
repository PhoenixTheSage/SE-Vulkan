using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using VRage.Render.Scene;
using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

public sealed partial class VulkanScene
{
    private bool stockLodBounds=System.Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_LOD_BOUNDS")=="1";
    public bool StockLodBoundsEnabled{get=>stockLodBounds;set{if(stockLodBounds==value)return;stockLodBounds=value;foreach(var actor in actors.Values)if(actor.ModelBoundsOwned)MarkGeometry(actor);}}
    private readonly HashSet<IMyActor> dirtyTransforms=new HashSet<IMyActor>();
    private readonly ConcurrentQueue<IMyActor> transformNotifications=new();
    private readonly ConcurrentQueue<(MyActor Actor,bool Hierarchy)> actorStateNotifications=new();
    private sealed class ResidentActorObserver:VRage.Render.Scene.Components.MyActorComponent
    {
        private readonly VulkanScene scene;
        public ResidentActorObserver(VulkanScene scene){this.scene=scene;}
        public override void OnUpdateBeforeDraw(){if(Owner is MyActor actor && scene.actors.TryGetValue(actor.ID,out var data))scene.RegisterOldDecalShaders(data);}
        public override void OnVisibilityChange(){if(Owner is MyActor actor)scene.actorStateNotifications.Enqueue((actor,false));}
        public override void OnParentSet(){if(Owner is MyActor actor)scene.actorStateNotifications.Enqueue((actor,true));}
        public override void OnParentRemoved(){if(Owner is MyActor actor)scene.actorStateNotifications.Enqueue((actor,true));}
    }
    private void AcceptActorStateNotifications(){while(actorStateNotifications.TryDequeue(out var change)){if(change.Actor.IsDestroyed)continue;if(change.Hierarchy){groupMembershipDirty=true;MarkTransforms(change.Actor);}MarkVisibility(change.Actor);}}
    private readonly HashSet<ActorData> spatialDirty=new HashSet<ActorData>(),shadowDirty=new HashSet<ActorData>(),skinnedActors=new HashSet<ActorData>(),staticGroups=new HashSet<ActorData>();
    private readonly MyDynamicAABBTreeD actorIndex=new MyDynamicAABBTreeD(Vector3D.Zero);
    private readonly List<ActorData> candidates=new List<ActorData>(),shadowWork=new List<ActorData>();
    private readonly List<(Vector3D Center,float Radius)> removedShadowRegions=new List<(Vector3D,float)>();
    public long TransformUpdates {get;private set;}
    public int CandidateActors=>candidates.Count;

    private void TrackTransforms(MyActor actor)
    {
        actor.OnMove+=()=>MarkTransforms(actor);
        MarkTransforms(actor);
    }
    private void MarkTransforms(IMyActor actor)
    {
        // RenderDirect/game callbacks may run outside the render owner. Publish
        // only an identity; actor collections and descendants are touched when
        // the owner drains this queue before its spatial/visibility snapshot.
        if(actor!=null)transformNotifications.Enqueue(actor);
    }
    private void AcceptTransformNotification(IMyActor actor)
    {
        if(actor==null || actor.IsDestroyed || !dirtyTransforms.Add(actor))return;
        InvalidateOcclusion("Transforms");
        if(actor is MyActor owned)
        {
            if(actors.TryGetValue(owned.ID,out var data)){spatialDirty.Add(data);shadowDirty.Add(data);DirtyGroup(data);DirtyNativePass(data,appearance:false);}
            foreach(var child in owned.Children)AcceptTransformNotification(child);
        }
    }
    private void MarkGeometry(ActorData data)
    {
        DirtyNativePass(data,true);
        InvalidateOcclusion("Geometry");
        DirtyGroup(data);
        data.GeometryDirty=true;shadowDirty.Add(data);spatialDirty.Add(data);
        Updater.AddToNextUpdate(data.Actor);
    }
    private void MarkVisibility(MyActor actor)
    {
        if(actors.TryGetValue(actor.ID,out var data))MarkGeometry(data);
        foreach(var child in actor.Children)if(child is MyActor owned)MarkVisibility(owned);
    }
    private void MarkInstanceGeometry(uint id)
    {
        foreach(var data in actors.Values)if(data.InstanceBuffer==id)MarkGeometry(data);
    }
    private void RefreshSpatialIndex()
    {
        foreach(var data in spatialDirty)
        {
            if(data.Actor.IsDestroyed || data.Model==null){if(data.Proxy>=0){actorIndex.RemoveProxy(data.Proxy);data.Proxy=-1;}continue;}
            // Message-authored and RenderDirect-authored local bounds take
            // ownership from model bounds. A quality toggle must preserve them.
            if(data.ModelBoundsOwned&&data.Actor.LocalAabb!=data.InstalledModelBounds)data.ModelBoundsOwned=false;
            if(data.ModelBoundsOwned){var bounds=stockLodBounds&&!KnownStockOldPipeline(data)&&data.InstanceBuffer==uint.MaxValue&&data.StaticInstances==null&&data.SkinMatrices==null?models.StockNewCullingBounds(data.Model):data.Model.Bounds;if(data.Actor.LocalAabb!=bounds)data.Actor.SetLocalAabb(bounds);data.InstalledModelBounds=bounds;}
            data.Bounds=data.Actor.CalculateAabb();
            double radius=data.Bounds.HalfExtents.Length();
            // Enclose the same conservative bounding sphere used by the old
            // shadow/probe distance test, including long/thin models.
            BoundingBoxD sphereBox=new BoundingBoxD(data.Bounds.Center-new Vector3D(radius),data.Bounds.Center+new Vector3D(radius));
            if(data.Proxy<0)data.Proxy=actorIndex.AddProxy(ref sphereBox,data,0);
            else actorIndex.MoveProxy(data.Proxy,ref sphereBox,Vector3D.Zero);
            if(data.StaticInstances!=null)staticGroups.Add(data);else staticGroups.Remove(data);
        }
        spatialDirty.Clear();
    }
    private IEnumerable<ActorData> DrawCandidates(bool native)
    {
        if(!native || frustum==null)return actors.Values;
        double reach=Math.Max(0,Math.Max(ProbeDrawDistance,ShadowDrawDistance));
        BoundingBoxD query=BoundingBoxD.CreateFromPoints(frustum.GetCorners());
        query.Min=Vector3D.Min(query.Min,Environment.CameraPosition-new Vector3D(reach));
        query.Max=Vector3D.Max(query.Max,Environment.CameraPosition+new Vector3D(reach));
        if(!query.Min.IsValid() || !query.Max.IsValid())return actors.Values;
        actorIndex.OverlapAllBoundingBox(ref query,candidates);
        // Static groups historically bypass actor-level main frustum rejection.
        // Preserve that contract even when their imported model bound is small.
        foreach(var data in staticGroups)if(!candidates.Contains(data))candidates.Add(data);
        return candidates;
    }
    private void RefreshShadowPoses()
    {
        foreach(var region in removedShadowRegions){MarkSpots(region.Center,region.Radius,false);MarkSpots(region.Center,region.Radius,true);}removedShadowRegions.Clear();
        shadowWork.Clear();foreach(var data in shadowDirty)shadowWork.Add(data);
        foreach(var data in skinnedActors)if(!shadowDirty.Contains(data))shadowWork.Add(data);
        shadowDirty.Clear();
        foreach(var data in shadowWork)
        {
            if(data.Actor.IsDestroyed || data.Model==null)continue;
            NoteShadowPose(data);
            if(data.DynamicShadow && data.SkinMatrices==null)shadowDirty.Add(data); // settle moving rigid casters back into the static map next frame
        }
    }
}
