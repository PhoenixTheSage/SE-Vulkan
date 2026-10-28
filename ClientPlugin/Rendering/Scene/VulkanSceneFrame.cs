using System;
using System.Collections.Generic;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Interop;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;

// Phase 4 frame split, milestone 1 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): the scene's per-frame
// inputs have an Apply side and a Build side. Apply (message handlers, the renderer's frame setup, the scene
// update) writes Next, the game-facing Environment and the Apply light frame; LatchFrame copies them into the
// fields Build reads at the hand-off, so a later Apply never changes a frame Build is still assembling.
// Environment stays Apply-side because the game's RenderDirect components read it during the actor update;
// Build reads FrameView. On one thread Draw runs UpdateScene, LatchFrame and BuildScene back to back.
public sealed partial class VulkanScene
{
    public sealed class FrameInputs
    {
        public MyRenderSettings RenderSettings;
        public MyShadowsSettings.Struct ShadowSettings=MyShadowsSettings.Struct.Default;
        public float ProbeDrawDistance,ShadowDrawDistance,HighlightSepia;
        public long ExternalDepthRevision;
        public BoundingFrustumD Frustum,LightFarFrustum;
        public float LodFov,GroupNear=.1f;
        public Matrix SizeFallbackProjection;public float SizeFov,SizeNear,SizeOffsetX,SizeOffsetY;public bool HaveSizeProjection;
    }
    // Written by Apply; latched into the Build fields by LatchFrame.
    public readonly FrameInputs Next=new();
    // Gate toggle (SE_VULKAN_APPLY_BUILD_ORDER=0, or the property for the same-process frozen-light lane): off runs the
    // scene update at its old place inside Build (after the guest scene callback, shadow fit and voxel plan) and
    // collects billboards there, so the lane compares the old and new order in one process.
    public static bool ApplyBuildOrderEnabled=System.Environment.GetEnvironmentVariable("SE_VULKAN_APPLY_BUILD_ORDER")!="0";
    public bool ApplyBuildOrder{get=>ApplyBuildOrderEnabled;set=>ApplyBuildOrderEnabled=value;}
    // Build's copy of Environment (camera, frame time, delta).
    public MyEnvironment FrameView;
    // Lights selected for a frame: Apply fills one (SubmitLights, PrepareExcludedActors, spot marks during the
    // update); Build reads the latched one and adds its own spot marks. Native light submission is Build's.
    sealed class LightFrame
    {
        public readonly List<LiveSpot> Spots=new();
        public readonly Dictionary<uint,uint> SpotActors=new();
        public readonly HashSet<uint> Excluded=new();
        public ulong SpotStatic,SpotDynamic;
        public NativeLight[] Lights=Array.Empty<NativeLight>();public NativeSpot[] SpotPackets=Array.Empty<NativeSpot>();
        // Every scene light as of the end of Apply (milestone 2, slice 5): flares, guest shadow inputs and the ray
        // scene read these, never the live light components and owners. Entries are reused; Count is the length.
        public readonly List<LightSnapshot> All=new();public int Count;
    }
    sealed class LightSnapshot
    {
        public VulkanLightComponent Light;public uint OwnerId;public bool HasOwner,Destroyed,Visible;public MatrixD World;
        public VRageRender.Messages.UpdateRenderLightData Data,Effective;
        // The light's DataRevision and the owner's WorldMatrixIndex as of the copies above.
        public uint Revision;public int WorldIndex=-1;
    }
    // End of Apply: copy every light's owner state and data into the Apply light frame.
    void SnapshotLights()
    {
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.LightSnapshot);
        var frame=applyLights;frame.Count=0;
        foreach(var light in lights)
        {
            if(frame.Count==frame.All.Count)frame.All.Add(new LightSnapshot());
            var snapshot=frame.All[frame.Count++];var owner=light.Owner;
            bool same=ReferenceEquals(snapshot.Light,light);
            snapshot.Light=light;snapshot.HasOwner=owner!=null;snapshot.Destroyed=owner?.IsDestroyed??true;snapshot.Visible=owner!=null&&IsVisible(owner);
            bool live=owner!=null&&!owner.IsDestroyed;snapshot.OwnerId=live?owner.ID:0;
            if(!live){snapshot.World=MatrixD.Identity;snapshot.WorldIndex=-1;}
            else{int index=owner.WorldMatrixIndex;if(!same||index!=snapshot.WorldIndex||snapshot.WorldIndex<0){snapshot.World=owner.WorldMatrix;snapshot.WorldIndex=index;}}
            if(!same||snapshot.Revision!=light.DataRevision){snapshot.Data=light.Data;snapshot.Effective=light.EffectiveData;snapshot.Revision=light.DataRevision;}
        }
        for(int i=frame.Count;i<frame.All.Count;i++){frame.All[i].Light=null;frame.All[i].WorldIndex=-1;}
    }
    LightFrame applyLights=new(),buildLights=new();
    // True on the thread running Build (BeginBuild to EndBuild, or to the next UpdateScene); code shared by both
    // sides (spot marks, billboard misting) picks its frame with it.
    [ThreadStatic] static bool building;
    LightFrame SideLights=>building?buildLights:applyLights;
    internal Vector3D SideCamera=>building?FrameView.CameraPosition:Environment.CameraPosition;
    public void LatchFrame()
    {
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.HandoffDirty)){
        RenderSettings=Next.RenderSettings;ShadowSettings=Next.ShadowSettings;
        ProbeDrawDistance=Next.ProbeDrawDistance;ShadowDrawDistance=Next.ShadowDrawDistance;HighlightSepia=Next.HighlightSepia;ExternalDepthRevision=Next.ExternalDepthRevision;
        frustum=Next.Frustum;lodFov=Next.LodFov;groupNear=Next.GroupNear;
        sizeFallbackProjection=Next.SizeFallbackProjection;sizeFov=Next.SizeFov;sizeNear=Next.SizeNear;sizeOffsetX=Next.SizeOffsetX;sizeOffsetY=Next.SizeOffsetY;haveSizeProjection=Next.HaveSizeProjection;
        FrameView=Environment;
        (applyLights,buildLights)=(buildLights,applyLights);
        LatchActorFrames();
        }
        FoldApplyState();
    }
    public void EndBuild()=>building=false;
    // Frame split, milestone 6: with the build thread, runtime models replaced during Apply release their meshes at
    // the hand-off (FlushModelReleases), after the Build that may still draw them has joined.
    public bool FrameSplit{get=>models.DeferReleases;set=>models.DeferReleases=value;}
    public void FlushModelReleases()=>models.FlushReleases();
    // Build prologue: the native light and spot submission of the latched frame.
    void CommitLights(NativeBackend target)
    {
        target.SetLights(buildLights.Lights);
        if(target.WorldReady){int resolution=RenderSettings.User.ShadowMemoryQuality.ShadowSpotResolution();target.SetSpots((uint)Math.Max(256,resolution),buildLights.SpotPackets);}
    }
}
