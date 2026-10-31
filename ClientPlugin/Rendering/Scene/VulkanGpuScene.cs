using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Import;

namespace ClientPlugin.Rendering.Scene;
public sealed partial class VulkanScene {
    HashSet<ActorData> gpuDirty=new();
    readonly Dictionary<(VulkanModel,string,uint),ulong> gpuFamilies=new();
    readonly Dictionary<byte[],ulong> gpuSources=new();
    readonly Dictionary<ulong,HashSet<string>> gpuFamilyMaterials=new();
    readonly Dictionary<(VulkanModel,bool),HashSet<string>> gpuRelevantMaterials=new();
    readonly HashSet<uint> gpuPreviousExclusions=new();
    bool gpuActive,gpuTopologyDirty;ulong gpuIdentitySource;uint gpuEpoch,gpuViewCapacity;
    public bool GpuOpaqueOwnershipEnabled {get;set;}=true;
    public bool GpuSparseAppearanceEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_SPARSE_APPEARANCE")=="1";
    bool gpuSparseActive;
    public bool GpuOwnedInputsEnabled {get;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_GPU_OPAQUE_OWNED_INPUTS")=="1";
    byte[] gpuNewObjectDefaults;object gpuNewObjectLayout;
    public object GetGpuSceneStats()=>new{Enabled=backend.GpuSceneEnabled,Ready=backend.GpuSceneReady,Active=gpuActive,Actors=actors.Values.Count(a=>a.GpuOwned),NewActors=actors.Values.Count(a=>a.GpuOwned&&!a.GpuOld),OldActors=actors.Values.Count(a=>a.GpuOwned&&a.GpuOld),FallbackActors=actors.Values.Count(a=>a.Model!=null&&!a.GpuOwned),NativeCpuActors=actors.Values.Count(a=>a.PassOwned),ManagedFallbackActors=actors.Values.Count(a=>a.Model!=null&&!a.GpuOwned&&!a.PassOwned),ManagedFallbackReasons=actors.Values.Where(a=>a.Model!=null&&!a.GpuOwned&&!a.PassOwned).GroupBy(a=>a.SkinMatrices!=null?"skeletal":a.Dithering!=0?"deformed/dithered":HasHighlight(a.Id)?"highlight":buildLights.Excluded.Contains(a.Id)?"exclusion":!GpuModelSupported(a.Model)?"mixed/unsupported techniques":a.GpuFallbackReason??"source/admission").ToDictionary(g=>g.Key,g=>g.Count()),Families=gpuFamilies.Count,Sources=gpuSources.Count,SparseAppearance=GpuSparseAppearanceEnabled,RawGpuMaterialOverrides=actors.Values.Where(a=>a.GpuOwned).Sum(a=>(long)a.Materials.Count),ResidentGpuAppearanceEntries=actors.Values.Where(a=>a.GpuOwned).Sum(a=>(long)a.GpuAppearanceCount),MaximumGpuAppearanceEntries=actors.Values.Where(a=>a.GpuOwned).Select(a=>a.GpuAppearanceCount).DefaultIfEmpty().Max(),Epoch=gpuEpoch};
    static bool GpuModelSupported(VulkanModel model)=>model.SharedMesh!=0&&model.Parts.Count!=0&&model.Parts.All(p=>p.Technique==MyMeshDrawTechnique.MESH||p.Technique==MyMeshDrawTechnique.ALPHA_MASKED||p.Technique==MyMeshDrawTechnique.ALPHA_MASKED_SINGLE_SIDED||p.Technique==MyMeshDrawTechnique.DECAL_CUTOUT);
    bool GpuActorSupported(ActorData data)=>data.Model!=null&&data.SkinMatrices==null&&data.Dithering==0&&!HasHighlight(data.Id)&&!buildLights.Excluded.Contains(data.Id)&&GpuModelSupported(data.Model)&&data.Model.LodDistances.Length<=16&&(GpuSparseAppearanceEnabled||data.Materials.Count<=31);
    HashSet<string> GpuRelevantMaterials(ActorData data){
        var key=(data.Model,data.StaticInstances!=null);if(gpuRelevantMaterials.TryGetValue(key,out var cached))return cached;
        var names=new HashSet<string>(StringComparer.Ordinal);int count=key.Item2?1:data.Model.LodDistances.Length+1;
        for(int lod=0;lod<count;lod++){var model=models.GetLod(data.Model,lod);if(!GpuModelSupported(model))throw new InvalidOperationException("GPU model LOD requires explicit fallback");foreach(var part in model.Parts)names.Add(part.MaterialName??string.Empty);}
        gpuRelevantMaterials.Add(key,names);return names;
    }
    unsafe NativeGpuOpaqueAppearance GpuAppearance(uint material,Color tint,float emissivity,bool shown){ulong packed=new HalfVector4(new Vector4(tint.ToVector3(),emissivity)).PackedValue;var a=new NativeGpuOpaqueAppearance{Material=material,Visible=shown?1u:0u};a.Colour[0]=(uint)packed;a.Colour[1]=(uint)(packed>>32);var rgb=tint.ToVector3();a.OldColour[0]=rgb.X;a.OldColour[1]=rgb.Y;a.OldColour[2]=rgb.Z;a.OldColour[3]=emissivity;return a;}
    ulong GpuFamily(ActorData data,bool old,uint type){
        if(GpuSparseAppearanceEnabled&&data.GpuOwned&&!data.GpuBindingDirty&&data.GpuOld==old&&data.GpuType==type)return data.GpuFamily;
        var relevant=GpuRelevantMaterials(data);
        var identity=new StringBuilder();foreach(var m in data.Materials.OrderBy(x=>x.Key,StringComparer.Ordinal)){if(GpuSparseAppearanceEnabled&&!relevant.Contains(m.Key))continue;var t=m.Value.Textures;if(t.ColorMetalFileName==null&&t.NormalGlossFileName==null&&t.ExtensionsFileName==null&&t.AlphamaskFileName==null)continue;identity.Append(m.Key).Append('\0').Append(t.ColorMetalFileName).Append('\0').Append(t.NormalGlossFileName).Append('\0').Append(t.ExtensionsFileName).Append('\0').Append(t.AlphamaskFileName).Append('\0');}
        bool metal=(data.Flags&RenderFlags.MetalnessColorable)!=0;bool distanceFade=old && type>=2 && (data.Flags&RenderFlags.DistanceFade)!=0;identity.Append(metal).Append(GpuSparseAppearanceEnabled).Append(distanceFade);
        if(old&&data.OldModel!=null){RegisterOldActorLayouts(data);identity.Append("|OLD|");for(int l=0;l<data.OldModel.LodCount;l++){identity.Append(data.OldModel.Resource(l)).Append(':').Append(data.OldLodLayouts[l]).Append(':');if(data.OldModel.File!=null)foreach(int m in data.OldModel.File.Lods[l].Materials.SourceMaterials)identity.Append(m).Append(',');identity.Append(';');}}
        var key=(data.Model,identity.ToString(),old?type+1:0u);if(gpuFamilies.TryGetValue(key,out ulong found))return found;
        float[] distances=data.StaticInstances!=null?Array.Empty<float>():data.Model.LodDistances;if(distances.Any(d=>float.IsNaN(d)||float.IsInfinity(d)||d<0)||!distances.SequenceEqual(distances.OrderBy(d=>d)))throw new InvalidOperationException("GPU family thresholds require explicit fallback");var levels=new ulong[distances.Length+1];var admitted=new NativePassPart[levels.Length][];var orders=old?new NativeOldPartOrder[levels.Length][]:null;
        for(int lod=0;lod<levels.Length;lod++){var model=models.GetLod(data.Model,lod);if(!GpuModelSupported(model))throw new InvalidOperationException("GPU model LOD requires explicit fallback");var ordered=old?model.Parts:model.NewParts??model.Parts;var parts=new NativePassPart[ordered.Count];
            if(old)orders[lod]=new NativeOldPartOrder[parts.Length];
            for(int i=0;i<parts.Length;i++){var p=ordered[i];data.Materials.TryGetValue(p.MaterialName??string.Empty,out var overrides);var t=overrides?.Textures??default;
                if(old)orders[lod][i]=OldPartOrder(data,model,p,lod);
                bool masked=(p.Flags&1)!=0,cutout=p.Technique==MyMeshDrawTechnique.DECAL_CUTOUT;uint mask=(!string.IsNullOrEmpty(p.ColorTexture)?1u:0u)|(!string.IsNullOrEmpty(p.NormalTexture)?2u:0u)|(!string.IsNullOrEmpty(p.ExtensionTexture)?4u:0u);
                uint shader=old?WorldShaderVariants.Pipeline(p.Technique,type,masked,metal,distanceFade:distanceFade):cutout?4+mask*2+(metal?1u:0u):(masked?1u:0u)+(metal?2u:0u);uint main=old?shader:shader<4?shader:12+shader-4;
                if(!backend.HasGpuShader(old,main)||!backend.HasGpuShader(old,old?32+WorldShaderVariants.BasePipeline(shader):4+(masked?1u:0u))||(p.Technique==MyMeshDrawTechnique.MESH&&!backend.HasGpuShader(old,old?64+shader:8+shader)))throw new InvalidOperationException("GPU shader class requires explicit fallback");
                parts[i]=new NativePassPart{Mesh=model.SharedMesh,First=p.IndexStart,Count=(uint)p.Indices.Length,Material=PassMaterialId(p.MaterialName),Shader=shader,Flags=(masked?1u:0u)|(p.Technique==MyMeshDrawTechnique.ALPHA_MASKED?2u:0u)|(!DepthGeometrySegments.DrawsDepth(p.Technique)?4u:0u)|(p.Technique!=MyMeshDrawTechnique.MESH?8u:0u),Color=TextureCache.GetTexture(t.ColorMetalFileName??p.ColorTexture),Normal=TextureCache.GetWorldNormal(t.NormalGlossFileName??p.NormalTexture),Extensions=TextureCache.GetWorldExtensions(t.ExtensionsFileName??p.ExtensionTexture),Alpha=TextureCache.GetWorldAlpha(t.AlphamaskFileName??p.AlphaTexture)};
            }admitted[lod]=parts;
        }for(int lod=0;lod<levels.Length;lod++)levels[lod]=backend.RegisterGpuModel(admitted[lod],old,orders?[lod]);found=backend.RegisterGpuFamily(distances,levels);gpuFamilies.Add(key,found);gpuFamilyMaterials.Add(found,relevant);return found;
    }
    unsafe NativeGpuSceneActor GpuInput(ActorData data,bool old,uint type,ulong family,ulong source,List<NativeGpuOpaqueAppearance> appearances,bool replaceAppearance=true){
        var actorFrame=BuildFrameOf(data);var matrix=actorFrame.World;var bounds=data.Bounds;var pose=new NativeGpuOpaqueActor{Owner=data.Id,Generation=1,Scale=(float)actorFrame.ForwardScale,Flags=(actorFrame.Visible?1u:0u)|(old?2u:0u)|(data.DynamicShadow?8u:0u)|((data.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows?16u:0u)|(type==3||type==4?32u:0u)|((data.Flags&RenderFlags.SkipInMainView)!=0?64u:0u)|((data.Flags&RenderFlags.SkipInForward)!=0?128u:0u)};if(!(pose.Scale>0))pose.Scale=1;
        pose.Translation[0]=matrix.M41;pose.Translation[1]=matrix.M42;pose.Translation[2]=matrix.M43;pose.Minimum[0]=bounds.Min.X;pose.Minimum[1]=bounds.Min.Y;pose.Minimum[2]=bounds.Min.Z;pose.Maximum[0]=bounds.Max.X;pose.Maximum[1]=bounds.Max.Y;pose.Maximum[2]=bounds.Max.Z;
        pose.Basis[0]=(float)matrix.M11;pose.Basis[1]=(float)matrix.M21;pose.Basis[2]=(float)matrix.M31;pose.Basis[3]=(float)matrix.M12;pose.Basis[4]=(float)matrix.M22;pose.Basis[5]=(float)matrix.M32;pose.Basis[6]=(float)matrix.M13;pose.Basis[7]=(float)matrix.M23;pose.Basis[8]=(float)matrix.M33;
        var rgb=data.Color.ToVector3();pose.Colour[0]=rgb.X;pose.Colour[1]=rgb.Y;pose.Colour[2]=rgb.Z;pose.Colour[3]=1;ulong key=new HalfVector4(new Vector4(data.Hsv+colorizeHsv,0)).PackedValue;pose.KeyColour[0]=(uint)key;pose.KeyColour[1]=(uint)(key>>32);
        if(old&&actorFrame.HasParent){pose.Flags|=4;var inverse=MatrixD.Invert(matrix);double[] values={inverse.M11,inverse.M21,inverse.M31,inverse.M41,inverse.M12,inverse.M22,inverse.M32,inverse.M42,inverse.M13,inverse.M23,inverse.M33,inverse.M43};for(int i=0;i<12;i++)pose.Inverse[i]=values[i];var local=actorFrame.LocalAabb;pose.LocalMinimum[0]=local.Min.X;pose.LocalMinimum[1]=local.Min.Y;pose.LocalMinimum[2]=local.Min.Z;pose.LocalMaximum[0]=local.Max.X;pose.LocalMaximum[1]=local.Max.Y;pose.LocalMaximum[2]=local.Max.Z;}
        NativeGpuOpaqueSelector selector;if(old)selector=data.Lod.Export();else{var s=data.NewLod.Export();selector=new(){Current=s.Current,Other=s.Previous,Transition=s.Transition,Start=s.Start,Initialized=s.Initialized};selector.LastCamera[0]=data.NewViewCamera.X;selector.LastCamera[1]=data.NewViewCamera.Y;selector.LastCamera[2]=data.NewViewCamera.Z;}
        var input=new NativeGpuSceneActor{Handle=data.GpuHandle,Family=family,Source=source,SourceCount=old?(uint)(data.GpuSourceBytes?.Length/64??1):1,Group=data.OcclusionGroup?.QueryId??0,Input=pose,Selector=selector,AppearanceFirst=(uint)appearances.Count};
        if(replaceAppearance){appearances.Add(GpuAppearance(uint.MaxValue,data.Color,1,true));foreach(var m in data.Materials){if(GpuSparseAppearanceEnabled&&!gpuFamilyMaterials[family].Contains(m.Key))continue;appearances.Add(GpuAppearance(PassMaterialId(m.Key),m.Value.Color??data.Color,m.Value.Emissivity,(m.Value.Flags.Add&RenderFlags.SkipInMainView)==0&&(m.Value.Flags.Remove&RenderFlags.Visible)==0));}input.AppearanceCount=(uint)appearances.Count-input.AppearanceFirst;data.GpuAppearanceCount=input.AppearanceCount;}else input.AppearanceCount=uint.MaxValue;
        byte[] objectData;
        if(GpuOwnedInputsEnabled){
            // NEW defaults belong to the pass binding. OLD appearance defaults
            // belong to the resident actor; the GPU writes all current pose rows.
            var layout=backend.WorldLayouts[old?type:0];
            if(!old){if(gpuNewObjectDefaults==null||!ReferenceEquals(gpuNewObjectLayout,layout)){gpuNewObjectDefaults=WorldConstants.Object(layout,MatrixD.Identity,Vector3.One,Vector3.Zero,0,0);gpuNewObjectLayout=layout;}objectData=gpuNewObjectDefaults;}
            else{if(data.GpuObjectDefaults==null||replaceAppearance||!ReferenceEquals(data.GpuObjectLayout,layout)){data.GpuObjectDefaults=WorldConstants.Object(layout,MatrixD.Identity,rgb,data.Hsv+colorizeHsv,1,0);data.GpuObjectLayout=layout;}var relative=matrix;relative.Translation-=FrameView.CameraPosition;WorldConstants.WriteMatrix(data.GpuObjectDefaults,WorldConstants.ObjectRows(layout),relative);objectData=data.GpuObjectDefaults;}
        }else{var relative=matrix;relative.Translation-=FrameView.CameraPosition;objectData=WorldConstants.Object(backend.WorldLayouts[type],relative,rgb,data.Hsv+colorizeHsv,1,0);if(!old)objectData=WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,0,0);}
        for(int i=0;i<128;i++)input.Object[i]=objectData[i];return input;
    }
    void RemoveGpuActor(ActorData data,bool handoff=true){if(data.GpuHandle==0)return;if(handoff){var s=backend.GetGpuSelector(data.GpuHandle);if(data.GpuOld)data.Lod.Import(s);else{data.NewLod.Import(new(){Current=s.Current,Previous=s.Other,Transition=s.Transition,Start=s.Start,Initialized=s.Initialized});unsafe{data.NewViewCamera=new Vector3D(s.LastCamera[0],s.LastCamera[1],s.LastCamera[2]);}data.HaveNewViewCamera=s.Initialized!=0;}}backend.RemoveGpuResource(2,data.GpuHandle);data.GpuHandle=0;data.GpuOwned=false;gpuOwnedActors.Remove(data);SyncIndexMembership(data);if(backend.StableMainOrderEnabled)backend.RemoveResidentMainOwner(data.Id);nativePassDirty.Add(data);gpuTopologyDirty=true;if(data.HaveShadowPose){MarkSpots(data.ShadowCenter,data.ShadowRadius,false);MarkSpots(data.ShadowCenter,data.ShadowRadius,true);}}
    readonly HashSet<ActorData> gpuOwnedActors=new();readonly List<ActorData> gpuOwnedStale=new();
    void CommitGpuActors(){
        bool enabled=GpuOpaqueOwnershipEnabled&&backend.GpuSceneEnabled&&backend.GpuSceneReady;if(enabled!=gpuActive){gpuActive=enabled;if(enabled){RefreshGroupBounds();gpuDirty.UnionWith(buildActors.Values);}else ClearGpuOwnership();}if(!enabled)return;
        if(gpuSparseActive!=GpuSparseAppearanceEnabled){gpuSparseActive=GpuSparseAppearanceEnabled;foreach(var actor in buildActors.Values){actor.GpuBindingDirty=true;actor.GpuAppearanceDirty=true;gpuDirty.Add(actor);}}
        if(!backend.SharedModelGeometryEnabled||ShadowSystem==null||!ShadowSystem.StockSchedulingEnabled||!ShadowSystem.StockFittingEnabled||!GroupOcclusionEnabled)throw new InvalidOperationException("GPU opaque scene requires stock shared geometry, shadow policies and root occlusion");
        foreach(uint id in gpuPreviousExclusions)if(!buildLights.Excluded.Contains(id)&&buildActors.TryGetValue(id,out var d))gpuDirty.Add(d);foreach(uint id in buildLights.Excluded)if(!gpuPreviousExclusions.Contains(id)&&buildActors.TryGetValue(id,out var d))gpuDirty.Add(d);gpuPreviousExclusions.Clear();gpuPreviousExclusions.UnionWith(buildLights.Excluded);
        var updates=new List<NativeGpuSceneActor>();var appearances=new List<NativeGpuOpaqueAppearance>();foreach(var data in gpuDirty){if(BuildFrameOf(data).Destroyed||!buildActors.ContainsKey(data.Id))continue;if(!GpuActorSupported(data)){RemoveGpuActor(data);continue;}
            bool old=KnownStockOldPipeline(data)||data.StaticInstances!=null||data.InstanceBuffer!=uint.MaxValue;uint type=0,count=1;byte[] bytes=null;bool dither=false;if(old){var previous=data.RenderModel;data.RenderModel=data.Model;PrepareInstances(data,out type,out count,out bytes,out dither);data.RenderModel=previous;if(type==1||dither||count==0){RemoveGpuActor(data);continue;}}
            if(data.GpuOwned&&data.GpuOld!=old)RemoveGpuActor(data);
            try{ulong family=GpuFamily(data,old,type);ulong source=0;if(old){if(bytes==null){if(gpuIdentitySource==0){var identity=new byte[64];BitConverter.GetBytes(1f).CopyTo(identity,0);BitConverter.GetBytes(1f).CopyTo(identity,20);BitConverter.GetBytes(1f).CopyTo(identity,40);gpuIdentitySource=backend.RegisterGpuSource(identity,false);}source=gpuIdentitySource;}else{if(type==3&&Enumerable.Range(0,bytes.Length/64).Any(i=>bytes[i*64+15]!=0)){RemoveGpuActor(data);continue;}if(!gpuSources.TryGetValue(bytes,out source)){source=backend.RegisterGpuSource(bytes,type==3||type==4);gpuSources.Add(bytes,source);}data.GpuSourceBytes=bytes;}}
                if(data.PassHandle!=0)RemoveNativePassActor(data,gpu:false);data.GpuSourceBytes=bytes;var input=GpuInput(data,old,type,family,source,appearances,data.GpuHandle==0||data.GpuBindingDirty||data.GpuAppearanceDirty);input.SourceCount=count;if(data.GpuHandle==0){data.GpuHandle=backend.RegisterGpuActor(ref input,appearances.ToArray());data.GpuOwned=true;data.GpuOld=old;gpuTopologyDirty=true;gpuOwnedActors.Add(data);SyncIndexMembership(data);}else updates.Add(input);
                // Same key the traversal would declare for this actor's root, kept resident as for pass-owned actors.
                if(backend.StableMainOrderEnabled)backend.SetResidentMainOwner(data.Id,(data.Flags&RenderFlags.SkipInMainView)==0?MainGroupKey(data,true):null);data.GpuFamily=family;data.GpuSource=source;data.GpuType=type;data.GpuBindingDirty=false;data.GpuAppearanceDirty=false;data.GpuFallbackReason=null;
            }catch(InvalidOperationException error){data.GpuFallbackReason=error.Message;RemoveGpuActor(data);}
        }gpuDirty.Clear();if(updates.Count!=0)backend.UpdateGpuActors(updates.ToArray(),appearances.ToArray());
    }
    unsafe void PublishGpuScene(){
        if(!gpuActive)return;using var profile=ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.NativePassPublication);var owned=buildActors.Values.Count(a=>a.GpuOwned);if(owned==0){if(gpuTopologyDirty)ClearGpuOwnership();return;}
        NativePassFrame frame;NativeGpuOpaqueView[] packed;
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.GpuFrameInputs)){
        var views=BuildNativeViews(out frame);packed=new NativeGpuOpaqueView[views.Count];
        for(int i=0;i<views.Count;i++){var v=views[i];var p=new NativeGpuOpaqueView{Id=v.Id,Light=v.Light,Flags=(v.Flags&1)|((v.Kind==1)?2u:0u)|((v.Flags&2)!=0?4u:0u)|((v.Flags&4)!=0?8u:0u)|((v.Flags&8)!=0?16u:0u)|((v.Kind==2)?32u:0u)|((v.Id>=15)?128u:0u),MinimumLod=v.MinimumLod,Shift=v.Shift,VisibleShift=v.VisibleShift,MinimumSizeSquared=v.MinimumSizeSquared,MaximumDistance=v.MaximumDistance};for(int n=0;n<24;n++)p.Planes[n]=v.Planes[n];packed[i]=p;}
        }
        // Reserve every cascade/probe role and both roles for current lights.
        // Light growth republishes capacity; it never disables a selected view.
        uint capacity=(uint)Math.Min(64,15+buildLights.Spots.Count*2);
        if(gpuTopologyDirty||packed.Length>gpuViewCapacity){using var topologyProfile=ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.GpuTopologyPublication);backend.PublishGpuScene(capacity,0,0);gpuViewCapacity=capacity;gpuTopologyDirty=false;}
        var global=actorLodSettings.Global;var input=new NativeGpuOpaqueFrame{Coefficient=frame.Coefficient,Delta=frame.Delta,Flags=((frame.GlobalFlags&1)!=0?2u:0u)|((frame.GlobalFlags&2)!=0?1u:0u),Selection=(uint)frame.Selection,Epoch=++gpuEpoch,SmoothCameraDistance=frame.SmoothDistance,Hysteresis=global.HisteresisRatio,DeadZone=global.TransitionDeadZoneConst,DeadZoneMult=global.TransitionDeadZoneDistanceMult,MaximumSeconds=global.MaxTransitionInSeconds,MinimumSeconds=global.MinTransitionInSeconds,DistanceAdd=global.ObjectDistanceAdd,DistanceMult=global.ObjectDistanceMult};for(int n=0;n<3;n++)input.Camera[n]=frame.Camera[n];
        backend.SetSpotRedraw(buildLights.SpotStatic,buildLights.SpotDynamic);
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.NativePassPrepare))
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.GpuResidentQueue))backend.QueueGpuSceneFrame(ref input,packed);
        NativePassQueryResult[] results;
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.GpuRootQueries)){
            var queries=backend.GetGpuQueries(packed);results=new NativePassQueryResult[queries.Length];
            for(int i=0;i<queries.Length;i++){var q=queries[i];ulong hidden=0;if(buildActors.TryGetValue(q.Representative,out var actor)){if((q.Views&1)!=0&&QueryGroupVisibility(q.Representative))hidden|=1;uint cascades=(uint)(q.Views>>1)&255;hidden|=(ulong)(cascades&~FilterShadowGroupCascades(actor,cascades))<<1;}results[i]=new(){Group=q.Group,HiddenViews=hidden};}
        }
        using(ClientPlugin.Diagnostics.RendererCpuProfiler.Measure(ClientPlugin.Diagnostics.RendererCpuProfiler.Phase.GpuVisibilityPublication))backend.PublishGpuVisibility(results);
    }
    void ClearGpuOwnership(){foreach(var data in buildActors.Values)RemoveGpuActor(data);if(gpuActive||gpuFamilies.Count!=0)backend.ClearGpuScene();gpuFamilies.Clear();gpuFamilyMaterials.Clear();gpuRelevantMaterials.Clear();gpuSources.Clear();gpuIdentitySource=0;gpuDirty.Clear();gpuPreviousExclusions.Clear();gpuActive=false;gpuTopologyDirty=false;}
}
