using System;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using ClientPlugin.Diagnostics;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool ActorSizeCullingEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_ACTOR_SIZE_CULLING")=="1";
    Matrix sizeProjection;
    Matrix sizeFallbackProjection;
    float sizeFov,sizeNear,sizeOffsetX,sizeOffsetY;
    bool haveSizeProjection;
    ActorSizeCullStats sizeCullStats;
    public ActorSizeCullStats GetActorSizeCullStats()=>sizeCullStats;
    void PrepareSizeProjection(NativeBackend target)
    {
        if(!haveSizeProjection)return;
        sizeProjection=sizeFov>0&&target.PresentationWidth>0&&target.PresentationHeight>0
            ?Matrix.CreatePerspectiveFovRhInfiniteComplementary(sizeFov,target.PresentationWidth/(float)target.PresentationHeight,sizeNear):sizeFallbackProjection;
        sizeProjection.M31=sizeOffsetX;sizeProjection.M32=sizeOffsetY;
    }
    bool CullActorByStockSize(NativeBackend target,ActorData actor,BoundingBoxD bounds,bool probeOnly)
    {
        using var sizeProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorSizeTest,false);
        bool culled=false;
        if(ActorSizeCullingEnabled&&KnownStockOldPipeline(actor)&&haveSizeProjection&&actor.StaticInstances==null&&actor.SkinMatrices==null){
            var actorFrame=BuildFrameOf(actor);bool parented=actorFrame.HasParent;
            if(!parented||actorFrame.HasLocalAabb){
                sizeCullStats.TestedActors++;
                float distance=parented?actorFrame.ParentedCameraDistanceSquared(FrameView.CameraPosition):(float)bounds.DistanceSquared(FrameView.CameraPosition);
                Vector3D extent=parented?(Vector3D)actorFrame.LocalAabb.Extents:bounds.Extents;
                float volume=(float)Math.Max(Math.Max(extent.X,extent.Y),extent.Z),last=0;
                foreach(float threshold in actor.Model.LodDistances)last=Math.Max(last,threshold*threshold);
                culled=StockActorSizeCulling.Evaluate(distance,last,volume,ref sizeProjection,target.PresentationWidth,target.PresentationHeight,out _);
            }
        }
        if(actor.SizeCulled!=culled){
            actor.SizeCulled=culled;sizeCullStats.Transitions++;
            InvalidateOcclusion("ActorSize");
            MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),false);MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),true);
        }
        if(culled){sizeCullStats.CulledActors++;if(!probeOnly)sizeCullStats.MainCulledActors++;}
        return culled;
    }
}
public struct ActorSizeCullStats {public uint Enabled,TestedActors,CulledActors,MainCulledActors,Transitions,HighlightOnlyActors;}
