using System;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Diagnostics;
using VRageMath;
using VRageRender;
namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene
{
    public bool NewActorViewsEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_NEW_ACTOR_VIEWS")!="0";
    NewActorViewStats newViewStats;
    public NewActorViewStats GetNewActorViewStats()=>newViewStats;
    struct ActorViewSlice {public int Lod;public float Alpha;public bool Main,Probe,Spots;public uint Cascades;}
    readonly ActorViewSlice[] actorViewSlices=new ActorViewSlice[12];int actorViewSliceCount;
    bool DrawNewActorViews(NativeBackend target,ActorData actor,MatrixD world,bool probeOnly,bool occluded,bool outsideProbe,BoundingBoxD bounds,uint? selectedCascades){
        if(!NewActorViewsEnabled || KnownStockOldPipeline(actor) || actor.StaticInstances!=null || actor.SkinMatrices!=null || !models.IsStockNewModel(actor.Model))return false;
        var planProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NewViewPlan,false);
        newViewStats.Actors++;actorViewSliceCount=0;
        float coefficient=lodFov>0?(float)(Math.Tan(lodFov*.5)/Math.Tan(70*Math.PI/360))*1080/Math.Max(1,target.PresentationHeight):1;
        float scale=(float)actor.Actor.WorldMatrixForwardScale;if(!(scale>0))scale=1;
        Vector3 translation=(Vector3)(world.Translation-Environment.CameraPosition);float distance=(translation.Length()/scale*coefficient+actorLodSettings.Global.ObjectDistanceAdd)*actorLodSettings.Global.ObjectDistanceMult;
        bool immediate=!actor.HaveNewViewCamera || Vector3D.Distance(actor.NewViewCamera,Environment.CameraPosition)>actorLodSettings.Global.MaxDistanceForSmoothCameraMovement;
        actor.NewLod.Update(distance,actor.Model.LodDistances,actorLodSettings.Global,Environment.LastFrameDelta,immediate);actor.NewViewCamera=Environment.CameraPosition;actor.HaveNewViewCamera=true;
        bool main=!probeOnly&&!occluded&&(actor.Flags&RenderFlags.SkipInMainView)==0;
        bool probe=!outsideProbe&&(actor.Flags&RenderFlags.SkipInForward)==0;
        bool casts=(actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
        bool spots=false;if(casts){Vector3 origin=(Vector3)(bounds.Center-Environment.CameraPosition);float radius=(float)bounds.HalfExtents.Length();foreach(var light in liveSpots)if(SpotSphereVisible(origin-(Vector3)(light.Position-Environment.CameraPosition),light.Direction,light.Range,light.Cone,radius)){spots=true;break;}}
        uint masks=selectedCascades??(casts?(CascadeMaskFor?.Invoke(bounds,Environment.CameraPosition)??255u):0u);
        if(casts)masks=FilterShadowGroupCascades(actor,masks);
        int LodFor(MyPassLoddingSetting pass,int lod)=>Math.Min(actor.Model.LodDistances.Length,Math.Max(pass.MinLod,lod+(main?pass.LodShiftVisible:pass.LodShift)));
        void Add(int lod,float alpha,bool isMain,bool isProbe,bool spots,uint cascades){
            for(int i=0;i<actorViewSliceCount;i++)if(actorViewSlices[i].Lod==lod&&actorViewSlices[i].Alpha==alpha){ref var entry=ref actorViewSlices[i];entry.Main|=isMain;entry.Probe|=isProbe;entry.Spots|=spots;entry.Cascades|=cascades;return;}
            if(actorViewSliceCount==actorViewSlices.Length)throw new InvalidOperationException("Actor view slice bound exceeded.");
            actorViewSlices[actorViewSliceCount++]=new ActorViewSlice{Lod=lod,Alpha=alpha,Main=isMain,Probe=isProbe,Spots=spots,Cascades=cascades};
        }
        if(main){int current=LodFor(actorLodSettings.GBuffer,actor.NewLod.Current);
            if(actor.NewLod.Previous>=0&&actor.NewLod.Transition>0){Add(current,3-actor.NewLod.Transition,true,false,false,0);Add(LodFor(actorLodSettings.GBuffer,actor.NewLod.Previous),actor.NewLod.Transition,true,false,false,0);}
            else Add(current,0,true,false,false,0);newViewStats.MainActors++;}
        if(probe){Add(LodFor(actorLodSettings.Forward,actor.NewLod.Current),0,false,true,false,0);newViewStats.ProbeActors++;}
        if(casts){for(int c=0;c<8;c++)if((masks&(1u<<c))!=0){var pass=c<actorLodSettings.CascadeDepths.Length?actorLodSettings.CascadeDepths[c]:actorLodSettings.SingleDepth;Add(LodFor(pass,actor.NewLod.Current),0,false,false,false,1u<<c);newViewStats.CascadeMemberships++;}
            if(spots){Add(LodFor(actorLodSettings.SingleDepth,actor.NewLod.Current),0,false,false,true,0);newViewStats.SpotActors++;}}
        int spotLod=spots?LodFor(actorLodSettings.SingleDepth,actor.NewLod.Current):-1;
        if(actor.LastNewSpotLod!=spotLod){MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),false);MarkSpots(bounds.Center,(float)bounds.HalfExtents.Length(),true);actor.LastNewSpotLod=spotLod;}
        planProfile.Dispose();using var emissionProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NewViewEmission,false);
        actor.SuppressViewHighlight=true;
        // Stock GeometryStage2 uses the 255 pipeline marker in its placeholder
        // object buffer; the selected geometry level belongs to the view plan.
        try{for(int i=0;i<actorViewSliceCount;i++){var view=actorViewSlices[i];SelectRepresentation(actor,models.GetLod(actor.Model,view.Lod),255);actor.LodAlpha=view.Alpha;
                actor.ExtraViewFlags=(view.Spots?0u:0x2000000u)|((view.Cascades==0&&!view.Spots)?512u:0u);
                DrawWorld(target,actor,world,!view.Main,false,!view.Probe,bounds,view.Cascades);newViewStats.Representations++;}
        }finally{actor.ExtraViewFlags=0;actor.SuppressViewHighlight=false;}
        if(HasHighlight(actor.Actor.ID)){SelectRepresentation(actor,actor.Model,255);actor.LodAlpha=0;actor.ExtraViewFlags=8192u|512u|0x2000000u;
            try{DrawWorld(target,actor,world,false,false,true,bounds,0);}finally{actor.ExtraViewFlags=0;}newViewStats.HighlightActors++;}
        return true;
    }
}
public struct NewActorViewStats {public uint Enabled,Actors,Representations,MainActors,ProbeActors,SpotActors,CascadeMemberships,HighlightActors;}
