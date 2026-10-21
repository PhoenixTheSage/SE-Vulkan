using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace ClientPlugin.Rendering.Scene;

// The native pass scene is the default when every policy it depends on is on.
// An explicit SE_VULKAN_NATIVE_PASS_SCENE=1 keeps the strict prerequisite
// check; =0, or any disabled prerequisite, selects the managed scene path.
internal static class NativePassPolicy
{
    static bool On(string name)=>Environment.GetEnvironmentVariable(name)!="0";
    public static readonly bool Explicit=Environment.GetEnvironmentVariable("SE_VULKAN_NATIVE_PASS_SCENE")=="1";
    public static readonly bool Enabled=Environment.GetEnvironmentVariable("SE_VULKAN_NATIVE_PASS_SCENE") switch{
        "1"=>true,"0"=>false,
        _=>On("SE_VULKAN_NEW_ACTOR_VIEWS")&&On("SE_VULKAN_GROUP_OCCLUSION")&&On("SE_VULKAN_STOCK_DEPTH_GEOMETRY")&&On("SE_VULKAN_STOCK_SHADOW_SCHEDULE")
            &&(Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_SHADOW_FIT")??Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_SHADOW_SCHEDULE"))!="0"
            &&Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE")!="1"&&Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_SCENE")!="1"};
}

public sealed partial class VulkanScene
{
    // Full resident preparation AND native group recording. This switch never
    // re-enters the prepared color packet producer for supported actors.
    public bool NativePassSceneEnabled {get;set;}=NativePassPolicy.Enabled;
    public bool NativeMixedOpaqueEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_NATIVE_MIXED_OPAQUE")!="0";
    public bool GpuHybridPassEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_HYBRID")=="1";
    private sealed class PassFamily
    {
        public VulkanModel Root;
        public ulong Handle;
        public bool Metal,Unsupported,Mixed;
        public readonly Dictionary<string,MyTextureChange> Textures=new(StringComparer.Ordinal);
        public readonly Dictionary<int,ulong> Levels=new();
        public readonly HashSet<ActorData> Members=new();
    }
    private readonly HashSet<ActorData> nativePassDirty=new();
    private readonly Dictionary<(VulkanModel,string,bool,bool),PassFamily> nativePassFamilies=new();
    private readonly Dictionary<ulong,PassFamily> nativePassFamilyByHandle=new();
    private readonly Dictionary<string,uint> nativePassMaterials=new(StringComparer.Ordinal);
    private readonly List<ulong> nativePassRetiredModels=new();
    private readonly HashSet<uint> previousNativeExclusions=new();
    private bool nativePassActive;
    private bool nativePassMixedActive;
    private long nativePassCommits,nativePassRegistrations,nativePassFallbacks;
    public object GetNativePassSceneStats()=>new{Enabled=NativePassSceneEnabled,Active=nativePassActive,Actors=actors.Values.Count(x=>x.PassOwned),Families=nativePassFamilies.Count,Commits=nativePassCommits,Registrations=nativePassRegistrations,Fallbacks=nativePassFallbacks};

    private void DirtyNativePass(ActorData actor,bool bindings=false,bool appearance=true){nativePassDirty.Add(actor);if(bindings||appearance)cellDirty.Add(actor);else cellPoseDirty.Add(actor);gpuDirty.Add(actor);actor.GpuBindingDirty|=bindings;actor.GpuAppearanceDirty|=appearance||bindings;actor.PassBindingDirty|=bindings;actor.PassAppearanceDirty|=appearance;}
    private uint PassMaterialId(string name){name??=string.Empty;if(!nativePassMaterials.TryGetValue(name,out uint id)){id=checked((uint)nativePassMaterials.Count+1);nativePassMaterials.Add(name,id);}return id;}
    private static bool PassModelSupported(VulkanModel model,bool mixed=false)=>model.SharedMesh!=0&&model.Parts.Count!=0&&model.Parts.All(p=>p.Technique==MyMeshDrawTechnique.MESH||p.Technique==MyMeshDrawTechnique.ALPHA_MASKED||p.Technique==MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED||p.Technique==MyMeshDrawTechnique.DECAL_CUTOUT||(mixed&&p.Technique==MyMeshDrawTechnique.GLASS));
    private bool PassActorSupported(ActorData actor)=>actor.Model!=null&&!KnownStockOldPipeline(actor)&&actor.InstanceBuffer==uint.MaxValue&&actor.StaticInstances==null&&actor.SkinMatrices==null&&actor.Dithering==0&&!HasHighlight(actor.Actor.ID)&&!frameExcludedActors.Contains(actor.Actor.ID)&&PassModelSupported(actor.Model,NativeMixedOpaqueEnabled)&&models.IsStockNewModel(actor.Model);

    private PassFamily GetPassFamily(ActorData actor)
    {
        if(actor.PassFamily!=null&&!actor.PassBindingDirty)return actor.PassFamily;
        bool metal=(actor.Flags&RenderFlags.MetalnessColorable)!=0;
        var identity=new StringBuilder();var textures=new Dictionary<string,MyTextureChange>(StringComparer.Ordinal);
        foreach(var pair in actor.Materials.OrderBy(x=>x.Key,StringComparer.Ordinal)){
            var t=pair.Value.Textures;if(t.ColorMetalFileName==null&&t.NormalGlossFileName==null&&t.ExtensionsFileName==null&&t.AlphamaskFileName==null)continue;
            textures.Add(pair.Key,t);foreach(string value in new[]{pair.Key,t.ColorMetalFileName,t.NormalGlossFileName,t.ExtensionsFileName,t.AlphamaskFileName}){if(value==null)identity.Append("-:");else identity.Append(value.Length).Append(':').Append(value);}
        }
        var key=(actor.Model,identity.ToString(),metal,NativeMixedOpaqueEnabled);
        if(!nativePassFamilies.TryGetValue(key,out var family)){
            family=new PassFamily{Root=actor.Model,Metal=metal,Mixed=NativeMixedOpaqueEnabled,Handle=backend.RegisterPassFamily(actor.Model.LodDistances)};
            foreach(var item in textures)family.Textures.Add(item.Key,item.Value);
            nativePassFamilies.Add(key,family);nativePassFamilyByHandle.Add(family.Handle,family);
        }
        actor.PassFamily?.Members.Remove(actor);actor.PassFamily=family;family.Members.Add(actor);actor.PassBindingDirty=false;return family;
    }
    private void ResolvePassLevel(PassFamily family,int lod)
    {
        var model=models.GetLod(family.Root,lod);
        if(!PassModelSupported(model,family.Mixed)){family.Unsupported=true;foreach(var member in family.Members)DirtyNativePass(member);return;}
        var opaque=(model.NewParts??model.Parts).Where(p=>p.Technique!=MyMeshDrawTechnique.GLASS).ToArray();
        var parts=new NativePassPart[opaque.Length];
        for(int i=0;i<parts.Length;i++){
            var part=opaque[i];family.Textures.TryGetValue(part.MaterialName??string.Empty,out var t);
            bool masked=part.Technique==MyMeshDrawTechnique.ALPHA_MASKED||part.Technique==MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED;
            bool cutout=part.Technique==MyMeshDrawTechnique.DECAL_CUTOUT;
            // Shader presence macros come from the authored stock material;
            // a replacement binding does not rewrite its preprocessed technique.
            uint textureMask=(!string.IsNullOrEmpty(part.ColorTexture)?1u:0u)|(!string.IsNullOrEmpty(part.NormalTexture)?2u:0u)|(!string.IsNullOrEmpty(part.ExtensionTexture)?4u:0u);
            uint shader=cutout?4+textureMask*2+(family.Metal?1u:0u):(masked?1u:0u)+(family.Metal?2u:0u);
            parts[i]=new NativePassPart{Mesh=model.SharedMesh,First=part.IndexStart,Count=(uint)part.Indices.Length,Material=PassMaterialId(part.MaterialName),Shader=shader,Flags=(masked?1u:0u)|(part.Technique==MyMeshDrawTechnique.ALPHA_MASKED?2u:0u)|(cutout?4u:0u)|(part.Technique!=MyMeshDrawTechnique.MESH?8u:0u),
                Color=TextureCache.GetTexture(t.ColorMetalFileName??part.ColorTexture),Normal=TextureCache.GetWorldNormal(t.NormalGlossFileName??part.NormalTexture),Extensions=TextureCache.GetWorldExtensions(t.ExtensionsFileName??part.ExtensionTexture),Alpha=TextureCache.GetWorldAlpha(t.AlphamaskFileName??part.AlphaTexture)};
        }
        bool glass=model.Parts.Any(p=>p.Technique==MyMeshDrawTechnique.GLASS);
        ulong replacement=family.Mixed?backend.RegisterMixedPassModel(parts,glass):backend.RegisterPassModel(parts);backend.BindPassLevel(family.Handle,(uint)lod,replacement);
        if(family.Levels.TryGetValue(lod,out ulong previous))nativePassRetiredModels.Add(previous);
        family.Levels[lod]=replacement;
    }
    private static unsafe NativePassAppearance PassAppearance(uint material,Color tint,float emissivity,bool visible)
    {
        ulong packed=new HalfVector4(new Vector4(tint.ToVector3(),emissivity)).PackedValue;
        var value=new NativePassAppearance{Material=material,Visible=visible?1u:0u};for(int i=0;i<4;i++)value.Color[i]=(ushort)(packed>>(16*i));return value;
    }
    private unsafe NativePassActor PassActorInput(ActorData data,PassFamily family,List<NativePassAppearance> appearance,bool replaceAppearance=true)
    {
        var matrix=data.Actor.WorldMatrix;var bounds=data.Bounds;
        var actor=new NativePassActor{Handle=data.PassHandle,Owner=data.Actor.ID,Family=family.Handle,Group=data.OcclusionGroup?.QueryId??0,Scale=(float)data.Actor.WorldMatrixForwardScale,Lod=data.NewLod.Export(),HaveCamera=data.HaveNewViewCamera?1u:0u,AppearanceOffset=(uint)appearance.Count};
        if(!(actor.Scale>0))actor.Scale=1;
        actor.Flags=32u|(IsVisible(data.Actor)?1u:0u)|((data.Flags&RenderFlags.SkipInMainView)!=0?2u:0u)|((data.Flags&RenderFlags.SkipInForward)!=0?4u:0u)|((data.Flags&RenderFlags.CastShadows)!=0?8u:0u)|((data.Flags&RenderFlags.SkipInDepth)==0?16u:0u)|(data.DynamicShadow?64u:0u);
        actor.Translation[0]=matrix.M41;actor.Translation[1]=matrix.M42;actor.Translation[2]=matrix.M43;
        actor.Bounds[0]=bounds.Min.X;actor.Bounds[1]=bounds.Min.Y;actor.Bounds[2]=bounds.Min.Z;actor.Bounds[3]=bounds.Max.X;actor.Bounds[4]=bounds.Max.Y;actor.Bounds[5]=bounds.Max.Z;
        actor.Basis[0]=(float)matrix.M11;actor.Basis[1]=(float)matrix.M21;actor.Basis[2]=(float)matrix.M31;actor.Basis[3]=(float)matrix.M12;actor.Basis[4]=(float)matrix.M22;actor.Basis[5]=(float)matrix.M32;actor.Basis[6]=(float)matrix.M13;actor.Basis[7]=(float)matrix.M23;actor.Basis[8]=(float)matrix.M33;
        ulong key=new HalfVector4(new Vector4(data.Hsv+colorizeHsv,0)).PackedValue;for(int i=0;i<3;i++)actor.KeyColor[i]=(ushort)(key>>(16*i));
        actor.LastCamera[0]=data.NewViewCamera.X;actor.LastCamera[1]=data.NewViewCamera.Y;actor.LastCamera[2]=data.NewViewCamera.Z;
        if(replaceAppearance){
            appearance.Add(PassAppearance(uint.MaxValue,data.Color,1,true));
            foreach(var pair in data.Materials){var item=pair.Value;bool shown=(item.Flags.Add&RenderFlags.SkipInMainView)==0&&(item.Flags.Remove&RenderFlags.Visible)==0;appearance.Add(PassAppearance(PassMaterialId(pair.Key),item.Color??data.Color,item.Emissivity,shown));}
        }else actor.Flags|=128; // Transform/ownership input preserves native resident materials.
        actor.AppearanceCount=(uint)appearance.Count-actor.AppearanceOffset;return actor;
    }
    private void RemoveNativePassActor(ActorData actor,bool handoff=true,bool gpu=true)
    {
        if(gpu)RemoveGpuActor(actor,handoff);if(actor.PassHandle==0)return;
        if(handoff){var state=actor.NewLod.Export();var camera=actor.NewViewCamera;bool have=actor.HaveNewViewCamera;backend.PassActorSelector(actor.PassHandle,false,ref state,ref camera,ref have);actor.NewLod.Import(state);actor.NewViewCamera=camera;actor.HaveNewViewCamera=have;}
        backend.RemovePassResource(2,actor.PassHandle);actor.PassHandle=0;actor.PassOwned=false;actor.PassFamily?.Members.Remove(actor);actor.PassFamily=null;actor.PassBindingDirty=true;actor.PassAppearanceDirty=true;
        if(actor.HaveShadowPose){MarkSpots(actor.ShadowCenter,actor.ShadowRadius,false);MarkSpots(actor.ShadowCenter,actor.ShadowRadius,true);}
    }
    private void CommitNativePassActors()
    {
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassCommits);
        CommitGpuActors();bool enabled=NativePassSceneEnabled&&backend.NativePassReady&&(!gpuActive||GpuHybridPassEnabled);
        bool prerequisites=NewActorViewsEnabled&&GroupOcclusionEnabled&&backend.SharedModelGeometryEnabled&&ShadowSystem!=null&&ShadowSystem.StockSchedulingEnabled&&ShadowSystem.StockFittingEnabled;
        // Default ownership yields to the managed path whenever a prerequisite
        // policy is off; only an explicitly requested native scene is strict.
        if(enabled&&!prerequisites){if(NativePassPolicy.Explicit)throw new InvalidOperationException("Native pass scene requires stock new views, group occlusion, shared depth geometry and stock shadow fitting/scheduling.");enabled=false;}
        if(enabled!=nativePassActive){nativePassActive=enabled;if(enabled){RefreshGroupBounds();nativePassDirty.UnionWith(actors.Values);}else{foreach(var data in actors.Values)RemoveNativePassActor(data,gpu:false);backend.ClearPassFrame();}}
        if(!enabled)return;
        if(nativePassMixedActive!=NativeMixedOpaqueEnabled){nativePassMixedActive=NativeMixedOpaqueEnabled;foreach(var actor in actors.Values){actor.PassBindingDirty=true;DirtyNativePass(actor);}}
        foreach(uint id in previousNativeExclusions)if(!frameExcludedActors.Contains(id)&&actors.TryGetValue(id,out var old))DirtyNativePass(old,appearance:false);
        foreach(uint id in frameExcludedActors)if(!previousNativeExclusions.Contains(id)&&actors.TryGetValue(id,out var current))DirtyNativePass(current,appearance:false);
        previousNativeExclusions.Clear();previousNativeExclusions.UnionWith(frameExcludedActors);
        var updates=new List<NativePassActor>();var appearance=new List<NativePassAppearance>();
        foreach(var data in nativePassDirty){
            if(data.Actor.IsDestroyed||!actors.ContainsKey(data.Actor.ID))continue;
            // The GPU and native CPU selectors own disjoint actor sets.
            if(data.GpuOwned||!PassActorSupported(data)){RemoveNativePassActor(data,gpu:false);continue;}
            var family=GetPassFamily(data);if(family.Unsupported){RemoveNativePassActor(data,gpu:false);continue;}
            if(!family.Levels.ContainsKey(0))ResolvePassLevel(family,0);
            if(data.PassHandle==0){var localAppearance=new List<NativePassAppearance>();var input=PassActorInput(data,family,localAppearance);data.PassHandle=backend.RegisterPassActor(ref input,localAppearance.ToArray());data.PassOwned=true;nativePassRegistrations++;}
            else updates.Add(PassActorInput(data,family,appearance,data.PassAppearanceDirty));
            data.PassAppearanceDirty=false;
        }
        nativePassDirty.Clear();if(updates.Count!=0){backend.UpdatePassActors(updates.ToArray(),updates.Count,appearance.ToArray(),appearance.Count);nativePassCommits+=updates.Count;}
    }
    private static unsafe NativePassView PassView(uint id,uint kind,BoundingFrustumD frustum,MyPassLoddingSetting lod,uint flags=1,uint light=0,double minimum=0,double maximum=0)
    {
        var view=new NativePassView{Id=id,Kind=kind,Flags=flags,Light=light,MinimumLod=lod.MinLod,Shift=lod.LodShift,VisibleShift=lod.LodShiftVisible,MinimumSizeSquared=minimum,MaximumDistance=maximum};
        if(frustum!=null){
            view.Flags|=2;var m=frustum.Matrix;
            // Same six stock zero-to-one clip inequalities. Normalization is
            // unnecessary for AABB rejection and divides by zero for the
            // infinite complementary probe projection's unbounded plane.
            PlaneD[] planes={new(-m.M13,-m.M23,-m.M33,-m.M43),new(-m.M14+m.M13,-m.M24+m.M23,-m.M34+m.M33,-m.M44+m.M43),new(-m.M14-m.M11,-m.M24-m.M21,-m.M34-m.M31,-m.M44-m.M41),new(-m.M14+m.M11,-m.M24+m.M21,-m.M34+m.M31,-m.M44+m.M41),new(-m.M14+m.M12,-m.M24+m.M22,-m.M34+m.M32,-m.M44+m.M42),new(-m.M14-m.M12,-m.M24-m.M22,-m.M34-m.M32,-m.M44-m.M42)};
            for(int i=0;i<6;i++){view.Planes[i*4]=planes[i].Normal.X;view.Planes[i*4+1]=planes[i].Normal.Y;view.Planes[i*4+2]=planes[i].Normal.Z;view.Planes[i*4+3]=planes[i].D;}
        }
        return view;
    }
    public unsafe void PublishNativePassScene()
    {
        PublishGpuScene();if(!nativePassActive){if(gpuActive)FinishPassVisibility();return;}using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassPublication);
        var views=BuildNativeViews(out NativePassFrame frame);
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassPrepare))backend.PreparePassFrame(ref frame,views.ToArray(),views.Count);
        bool pending=true;
        try{
            NativePassPreparation preparation;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassResources)){
                preparation=backend.GetPassPreparation();foreach(var request in preparation.Missing)ResolvePassLevel(nativePassFamilyByHandle[request.Family],(int)request.Lod);
            }
            NativePassQueryResult[] results;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassQueries)){
            results=new NativePassQueryResult[preparation.Queries.Length];
            for(int i=0;i<results.Length;i++){var query=preparation.Queries[i];ulong hidden=0;if(actors.TryGetValue(query.Representative,out var actor)){if((query.Views&1)!=0&&QueryGroupVisibility(query.Representative))hidden|=1;uint cascades=(uint)(query.Views>>1)&255u;hidden|=(ulong)(cascades&~FilterShadowGroupCascades(actor,cascades))<<1;}results[i]=new NativePassQueryResult{Group=query.Group,HiddenViews=hidden};}
            }
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassPublish))backend.PublishPassFrame(results,results.Length);pending=false;
            if(NativeMixedOpaqueEnabled)foreach(var ticket in backend.GetPassTransparentRequests())DrawNativePassGlass(ticket);
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassFallbacks)){
            foreach(var fallback in backend.GetPassPreparation().Fallbacks)if(actors.TryGetValue(fallback.Owner,out var data)){
                nativePassFallbacks++;RemoveNativePassActor(data,gpu:false);data.NewLod.Import(fallback.Lod);DirtyNativePass(data);DrawNativePassFallback(data);
            }
            }
        }finally{if(pending)backend.CancelPassFrame();}
        PublishCells();
        FinishPassVisibility();
    }
    private void FinishPassVisibility()
    {
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassEndVisibility)){
        foreach(ulong model in nativePassRetiredModels)backend.RemovePassResource(0,model);nativePassRetiredModels.Clear();
        EndOcclusion();EndShadowGroupOcclusion();backend.ShadowExclusions=frameShadowExclusions.ToArray();backend.SetSpotRedraw(frameSpotStatic,frameSpotDynamic);
        }
    }
    private unsafe List<NativePassView> BuildNativeViews(out NativePassFrame frame)
    {
        var views=new List<NativePassView>();
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativePassFrameInputs)){
        var global=actorLodSettings.Global;frame=new NativePassFrame{Coefficient=lodFov>0?(float)(Math.Tan(lodFov*.5)/Math.Tan(70*Math.PI/360))*1080/Math.Max(1,backend.PresentationHeight):1,Delta=Environment.LastFrameDelta,GlobalFlags=(global.EnableLodSelection?1u:0u)|(global.IsUpdateEnabled?2u:0u),Selection=global.LodSelection,SmoothDistance=global.MaxDistanceForSmoothCameraMovement};
        frame.Camera[0]=Environment.CameraPosition.X;frame.Camera[1]=Environment.CameraPosition.Y;frame.Camera[2]=Environment.CameraPosition.Z;
        frame.Global[0]=global.HisteresisRatio;frame.Global[1]=global.TransitionDeadZoneConst;frame.Global[2]=global.TransitionDeadZoneDistanceMult;frame.Global[3]=global.MaxTransitionInSeconds;frame.Global[4]=global.MinTransitionInSeconds;frame.Global[5]=global.ObjectDistanceAdd;frame.Global[6]=global.ObjectDistanceMult;
        views.Add(PassView(0,0,frustum,actorLodSettings.GBuffer));
        for(int c=0;c<8;c++)if((ShadowSystem.UpdateMask&(1u<<c))!=0){var lod=c<actorLodSettings.CascadeDepths.Length?actorLodSettings.CascadeDepths[c]:actorLodSettings.SingleDepth;float threshold=ShadowSystem.Settings.Cascades[c].SkippingSmallObjectThreshold,factor=ShadowSystem.ProjectionFactors[c];views.Add(PassView((uint)c+1,1,ShadowSystem.CasterFrustum(c),lod,minimum:threshold*threshold/(factor*factor)));}
        var probe=backend.CurrentProbeConfig;uint faces=backend.CurrentProbeFaceMask();
        if(ProbeDrawDistance>0)for(int face=0;face<6;face++)if((faces&(1u<<face))!=0){var p=probe.Projections;int at=face*16;var relative=new MatrixD(p[at],p[at+1],p[at+2],p[at+3],p[at+4],p[at+5],p[at+6],p[at+7],p[at+8],p[at+9],p[at+10],p[at+11],p[at+12],p[at+13],p[at+14],p[at+15]);var world=MatrixD.CreateTranslation(-Environment.CameraPosition)*relative;views.Add(PassView((uint)face+9,2,new BoundingFrustumD(world),actorLodSettings.Forward,maximum:ProbeDrawDistance));}
        for(int slot=0;slot<liveSpots.Count;slot++){var spot=liveSpots[slot];views.Add(PassView((uint)(15+slot*2),1,new BoundingFrustumD(spot.WorldProjection),actorLodSettings.SingleDepth,1|8,(uint)spot.Index));views.Add(PassView((uint)(16+slot*2),1,new BoundingFrustumD(spot.WorldProjection),actorLodSettings.SingleDepth,1|4,(uint)spot.Index));}
        }
        return views;
    }
    private void DrawNativePassFallback(ActorData data)
    {
        if(data.Model==null||!IsVisible(data.Actor))return;var bounds=data.Bounds;bool probeOnly=(data.Flags&RenderFlags.SkipInMainView)!=0||frustum!=null&&frustum.Contains(bounds)==ContainmentType.Disjoint;
        double distance=Vector3D.Distance(bounds.Center,Environment.CameraPosition);bool outside=ProbeDrawDistance<=0||distance>ProbeDrawDistance+bounds.HalfExtents.Length();
        bool hidden=!probeOnly&&QueryActorVisibility(data.Actor.ID,bounds);if(!probeOnly)visible.Add(data.Actor.ID);
        DrawActorLods(backend,data,data.Actor.WorldMatrix,probeOnly,hidden,outside,bounds);
    }
    private void DrawNativePassGlass(NativePassTransparent ticket)
    {
        if(!RenderSettings.DrawTransparentModelsInstanced)return;
        if(!actors.TryGetValue(ticket.Owner,out var actor)||!actor.PassOwned||actor.PassHandle!=ticket.Actor)return;
        var model=models.GetLod(actor.Model,(int)ticket.Lod);var world=actor.Actor.WorldMatrix;world.Translation-=Environment.CameraPosition;
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.GlassParts,false);
        foreach(var part in model.Parts){
            if(part.Technique!=MyMeshDrawTechnique.GLASS)continue;
            if(!MyTransparentMaterials.TryGetMaterial(VRage.Utils.MyStringId.GetOrCompute(part.MaterialName??string.Empty),out var glass)){UnsupportedMaterialDraws++;continue;}
            actor.Materials.TryGetValue(part.MaterialName??string.Empty,out var material);
            if(material!=null&&((material.Flags.Add&RenderFlags.SkipInMainView)!=0||(material.Flags.Remove&RenderFlags.Visible)!=0))continue;
            var constants=WorldConstants.Object(backend.GlassLayouts[0],world,(material?.Color??actor.Color).ToVector3(),actor.Hsv+colorizeHsv,material?.Emissivity??1,ticket.Alpha,null,actor.Dithering,false,255);
            backend.AddGlassPacket(0,part.Mesh,TextureCache.GetTexture(material?.Textures.ColorMetalFileName??glass.Texture),TextureCache.GetTexture(material?.Textures.NormalGlossFileName??glass.GlossTexture,false),constants,NativeBackend.GlassMaterial(glass,model.Bounds.HalfExtents.X,RenderSettings),triangleFaceCulling:glass.TriangleFaceCulling);DrawnMeshes++;
        }
    }
    private void ClearNativePassScene()
    {
        ClearGpuOwnership();if(backend.NativePassReady)backend.ClearPassFrame();
        foreach(var family in nativePassFamilies.Values){foreach(ulong model in family.Levels.Values)backend.RemovePassResource(0,model);backend.RemovePassResource(1,family.Handle);}
        foreach(ulong model in nativePassRetiredModels)backend.RemovePassResource(0,model);
        nativePassFamilies.Clear();nativePassFamilyByHandle.Clear();nativePassMaterials.Clear();nativePassRetiredModels.Clear();nativePassDirty.Clear();previousNativeExclusions.Clear();nativePassActive=false;
    }
}
