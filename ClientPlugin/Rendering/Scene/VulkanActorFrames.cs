using System.Collections.Generic;
using ClientPlugin.Diagnostics;
using VRage.Render.Scene;
using VRageMath;

namespace ClientPlugin.Rendering.Scene;

// Phase 4 frame split, milestone 2, slice 1 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): the game actor
// state Build reads. Apply's scene update and messages move and destroy the game's MyActor objects (its WorldMatrix
// and WorldMatrixInv getters even compute lazily), so Build reads a per-actor ActorFrame instead. Each actor has two,
// one per frame parity: the end of UpdateScene refreshes the Apply parity for the actors that changed, and
// LatchFrame hands it to Build.
// SE_VULKAN_FRAME_PACKET_VERIFY=1 compares every actor's Build frame with the live actor when Build starts; on one
// thread they must be identical.
public sealed partial class VulkanScene
{
    sealed class ActorFrame
    {
        public MatrixD World;public int WorldIndex=-1;public float ForwardScale;
        public BoundingBox LocalAabb;public bool HasLocalAabb,HasParent,Visible,Destroyed;
        public BoundingBoxD Bounds;
        // The nearest root ancestor (occlusion group key), or null.
        public MyActor GroupRoot;
        MatrixD inverse;int inverseIndex=-1;
        // Build-only cache of the inverse world matrix (the game's MyActor.WorldMatrixInv).
        public MatrixD WorldInv{get{if(inverseIndex!=WorldIndex){MatrixD.Invert(ref World,out inverse);inverseIndex=WorldIndex;}return inverse;}}
        // MyActor.CalculateCameraDistanceSquared for a parented actor.
        public float ParentedCameraDistanceSquared(Vector3D camera)=>(float)LocalAabb.DistanceSquared(Vector3D.Transform(camera,WorldInv));
    }
    int applyParity,buildParity;
    // Actors whose frame may have changed: everything RefreshSpatialIndex drains (transform, geometry, visibility
    // and parent changes reach it, including descendants), plus creation and removal. An actor is refreshed in the
    // frame it is noted and in the next one, so both parity slots catch up; everything else keeps its frames.
    HashSet<ActorData> frameDirty=new(),frameDirtyPrevious=new();
    void NoteActorFrame(ActorData data)=>frameDirty.Add(data);
    ActorFrame BuildFrameOf(ActorData data)=>data.Frames[buildParity];
    void SnapshotActors()
    {
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.ActorSnapshot);
        int slot=applyParity;
        foreach(var data in frameDirty)RefreshActorFrame(data.Frames[slot],data);
        foreach(var data in frameDirtyPrevious)if(!frameDirty.Contains(data))RefreshActorFrame(data.Frames[slot],data);
        frameDirtyPrevious.Clear();(frameDirty,frameDirtyPrevious)=(frameDirtyPrevious,frameDirty);
    }
    void RefreshActorFrame(ActorFrame frame,ActorData data)
    {
        var actor=data.Actor;
        frame.Destroyed=actor.IsDestroyed;
        if(!frame.Destroyed)
        {
            ref readonly var world=ref actor.WorldMatrix;int index=actor.WorldMatrixIndex;
            if(index!=frame.WorldIndex){frame.World=world;frame.WorldIndex=index;frame.ForwardScale=actor.WorldMatrixForwardScale;}
            frame.HasLocalAabb=actor.HasLocalAabb;if(frame.HasLocalAabb)frame.LocalAabb=actor.LocalAabb;
            frame.HasParent=actor.Parent!=null;
        }
        frame.Visible=IsVisible(actor);
        frame.GroupRoot=GroupRootOf(actor);
        frame.Bounds=data.Bounds;
    }
    static MyActor GroupRootOf(MyActor actor){if(actor.IsDestroyed)return null;for(IMyActor node=actor.Parent;node!=null;node=node.Parent)if(node is MyActor root&&root.IsRoot())return root;return null;}
    void LatchActorFrames(){buildParity=applyParity;applyParity^=1;}
    public static bool FramePacketVerifyEnabled=System.Environment.GetEnvironmentVariable("SE_VULKAN_FRAME_PACKET_VERIFY")=="1";
    public long FramePacketChecks{get;private set;}
    public long FramePacketMismatches{get;private set;}
    public string FramePacketFirstMismatch{get;private set;}
    void VerifyActorFrames()
    {
        foreach(var data in actors.Values)
        {
            var frame=BuildFrameOf(data);var actor=data.Actor;FramePacketChecks++;
            string field=null;
            if(frame.Destroyed!=actor.IsDestroyed)field="Destroyed";
            else if(!frame.Destroyed)
            {
                if(frame.World!=actor.WorldMatrix)field="World";
                else if(frame.ForwardScale!=actor.WorldMatrixForwardScale)field="ForwardScale";
                else if(frame.HasLocalAabb!=actor.HasLocalAabb||frame.HasLocalAabb&&frame.LocalAabb!=actor.LocalAabb)field="LocalAabb";
                else if(frame.HasParent!=(actor.Parent!=null))field="Parent";
            }
            if(field==null&&frame.Visible!=IsVisible(actor))field="Visible";
            if(field==null&&frame.Bounds!=data.Bounds)field="Bounds";
            field??=FieldMismatch(data);
            if(field==null&&!actor.IsDestroyed&&!ReferenceEquals(frame.GroupRoot,GroupRootOf(actor)))field="GroupRoot";
            if(field!=null){FramePacketMismatches++;FramePacketFirstMismatch??=field+" of actor "+data.Id+" at frame "+FrameCounter;}
        }
    }
}
