using System;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageRender;
using ClientPlugin.Diagnostics;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool ActorLodsEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_ACTOR_LODS")!="0";
    private readonly MyNewLoddingSettings actorLodSettings=new();
    private float lodFov;
    private ActorLodStats actorLodStats;
    public ActorLodStats GetActorLodStats()=>actorLodStats;
    private sealed class ActorRepresentation
    {
        public PartFrame[] Frames;
        public NativeBackend.PreparedActor Submission;
        public uint Revision,Type;
        public Color Color;
        public Vector3 Hsv;
        public float Dither;
        public bool Metal,Opaque,DistanceFade;
    }
    private void SelectRepresentation(ActorData actor,VulkanModel model,int lod=0)
    {
        // Authored levels can share geometry or fall back to the same model;
        // their shader-visible LOD and cached object constants remain distinct.
        if(ReferenceEquals(actor.RenderModel,model)&&actor.RenderLod==(uint)lod)return;
        if(actor.RenderModel!=null){var previous=(actor.RenderModel,actor.RenderLod);if(!actor.Representations.TryGetValue(previous,out var saved))actor.Representations[previous]=saved=new ActorRepresentation();saved.Frames=actor.PartFrames;saved.Submission=actor.Submission;saved.Revision=actor.SubmissionRevision;saved.Type=actor.SubmissionType;saved.Color=actor.SubmissionColor;saved.Hsv=actor.SubmissionHsv;saved.Dither=actor.SubmissionDither;saved.Metal=actor.SubmissionMetal;saved.DistanceFade=actor.SubmissionDistanceFade;saved.Opaque=actor.SubmissionOpaque;}
        actor.RenderModel=model;actor.RenderLod=(uint)lod;
        if(actor.Representations.TryGetValue((model,actor.RenderLod),out var existing)){actor.PartFrames=existing.Frames;actor.Submission=existing.Submission;actor.SubmissionRevision=existing.Revision;actor.SubmissionType=existing.Type;actor.SubmissionColor=existing.Color;actor.SubmissionHsv=existing.Hsv;actor.SubmissionDither=existing.Dither;actor.SubmissionMetal=existing.Metal;actor.SubmissionDistanceFade=existing.DistanceFade;actor.SubmissionOpaque=existing.Opaque;}
        else{actor.PartFrames=null;actor.Submission=null;actor.SubmissionOpaque=true;foreach(var part in model.Parts)if((part.Flags&4)!=0){actor.SubmissionOpaque=false;break;}}
    }
    private void DrawActorLods(NativeBackend target,ActorData actor,MatrixD world,bool probeOnly,bool occluded,bool outsideProbe,BoundingBoxD bounds,uint? selectedCascades=null)
    {
        if(DrawNewActorViews(target,actor,world,probeOnly,occluded,outsideProbe,bounds,selectedCascades))return;
        // Stock highlights draw authored LOD0 independently of normal culling.
        // This branch is reached only for a culled actor with a live highlight.
        if(actor.SizeCulled){SelectRepresentation(actor,actor.Model);actor.LodAlpha=0;DrawWorld(target,actor,world,false,occluded,outsideProbe,bounds,selectedCascades);return;}
        var lodProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorLodSelection,false);
        int lod=0;float alpha=0;
        // Stock old-pipeline skinned actors select authored LODs using the same
        // distance policy; each mesh owns its own remapped live-bone palettes.
        // Static groups retain their separate ownership path.
        bool eligible=ActorLodsEnabled && actor.StaticInstances==null && actor.Model.LodDistances.Length!=0;
        if(eligible&&!actor.SizeCulled){float coefficient=lodFov>0?(float)(Math.Tan(lodFov*.5)/Math.Tan(70*Math.PI/360))*1080/Math.Max(1,target.PresentationHeight):1;
            // Native actors do not install DX11 cull proxies, so their stock
            // cached WorldAabb is empty when unparented. Use our authoritative
            // transformed local bound for that branch of the same distance rule.
            var actorFrame=BuildFrameOf(actor);float squared=!actorFrame.HasParent?(float)bounds.DistanceSquared(FrameView.CameraPosition):actorFrame.ParentedCameraDistanceSquared(FrameView.CameraPosition);
            float distance=(float)Math.Sqrt(squared);distance=(distance*coefficient+actorLodSettings.Global.ObjectDistanceAdd)*actorLodSettings.Global.ObjectDistanceMult;
            if(actor.InstanceBuffer!=uint.MaxValue && instances.TryGetValue(actor.InstanceBuffer,out var buffer) && buffer.Cubes!=null && buffer.EnabledSkinning)distance*=.25f;
            actor.Lod.Update(distance,actor.Model.LodDistances,actorLodSettings.Global,actorLodSettings.GBuffer,FrameView.LastFrameDelta);lod=actor.Lod.Current;alpha=actor.Lod.Transition;
            actorLodStats.SelectedActors++;if(lod!=0)actorLodStats.ReducedActors++;if(alpha!=0)actorLodStats.TransitionActors++;
        }
        var current= models.GetLod(actor.Model,lod);VulkanModel next=eligible&&alpha!=0?models.GetLod(actor.Model,actor.Lod.Next):null;
        // Duplicate or missing authored levels must not fade the same owned
        // constants twice or introduce a hole in the conservative fallback.
        if(ReferenceEquals(current,next)){next=null;alpha=0;}
        if(!ReferenceEquals(actor.LodShadowModel,current)||!ReferenceEquals(actor.LodShadowNext,next)||actor.LodShadowAlpha!=alpha){
            if(actor.LodShadowModel!=null || eligible){InvalidateOcclusion("ActorLOD");MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),false);MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),true);}
            actor.LodShadowModel=current;actor.LodShadowNext=next;actor.LodShadowAlpha=alpha;
        }
        SelectRepresentation(actor,current,lod);actor.LodAlpha=alpha;
        lodProfile.Dispose();
        DrawWorld(target,actor,world,probeOnly,occluded,outsideProbe,bounds,selectedCascades);
        if(next!=null){SelectRepresentation(actor,next,actor.Lod.Next);actor.LodAlpha=3-alpha;DrawWorld(target,actor,world,probeOnly,occluded,outsideProbe,bounds,selectedCascades);}
    }
}
public struct ActorLodStats {public uint Enabled,SelectedActors,ReducedActors,TransitionActors;}
