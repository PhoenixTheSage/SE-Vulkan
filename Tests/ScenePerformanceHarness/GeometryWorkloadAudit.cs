using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using HarmonyLib;
using VRageMath;

namespace ScenePerformanceHarness;

// Diagnostic-only: one render-thread snapshot and per-view snapshots on the
// owning DX11 render jobs. It never changes geometry, LOD or selection.
public sealed partial class Plugin
{
    static bool workloadAudit;
    static volatile bool captureWorkloadFrame;
    static object workloadSnapshot;
    static object occlusionDiagnosticSnapshot;
    static bool diagnosticPhaseStarted;
    static int diagnosticPhaseFrames;
    static readonly ConcurrentDictionary<int,object> oldGeometryViews=new();
    static readonly ConcurrentDictionary<int,object> newGeometryViews=new();
    static MatrixD selectionMainFrustum;static bool haveSelectionMainFrustum;
    static readonly ConcurrentDictionary<int,List<object>> oldSizeCandidates=new();
    static void BeginWorkloadDiagnostics(object renderer){if(!workloadAudit || renderer is not MyVulkanRender)return;var scene=(VulkanScene)SceneField.GetValue(renderer);scene.OcclusionDiagnosticsEnabled=sampling;if(sampling&&!diagnosticPhaseStarted){scene.ResetOcclusionDiagnostics();diagnosticPhaseStarted=true;}}
    static void ObserveWorkloadDiagnostics(object renderer){if(!workloadAudit || renderer is not MyVulkanRender)return;if(occlusionDiagnosticSnapshot==null || ++diagnosticPhaseFrames%120==0)occlusionDiagnosticSnapshot=((VulkanScene)SceneField.GetValue(renderer)).CaptureOcclusionDiagnostics();}
    static object Member(object owner,string name){if(owner==null)return null;var t=owner.GetType();var f=AccessTools.Field(t,name);if(f!=null)return f.GetValue(owner);var p=AccessTools.Property(t,name);if(p!=null)return p.GetValue(owner);throw new MissingMemberException(t.FullName,name);}
    static IEnumerable<object> Items(object source)=>source is IEnumerable values?values.Cast<object>():Enumerable.Empty<object>();
    static int Number(object owner,string name)=>Convert.ToInt32(Member(owner,name));
    static VRage.Render.Scene.MyActor RootOf(VRage.Render.Scene.MyActor actor){for(var node=actor;node!=null;node=node.Parent as VRage.Render.Scene.MyActor)if(node.IsRoot())return node;return null;}
    static double[] BoxMin(BoundingBoxD box)=>new[]{box.Min.X,box.Min.Y,box.Min.Z};
    static double[] BoxMax(BoundingBoxD box)=>new[]{box.Max.X,box.Max.Y,box.Max.Z};
    static int? OptionalNumber(object owner,string name){var f=AccessTools.Field(owner.GetType(),name);var p=AccessTools.Property(owner.GetType(),name);return f!=null?Convert.ToInt32(f.GetValue(owner)):p!=null?Convert.ToInt32(p.GetValue(owner)):null;}
    static ulong Id(object owner,string name)=>Convert.ToUInt64(Member(owner,name));
    static void AuditNewRender(object __instance,object query){
        bool probe=probeCoverageCollecting&&Convert.ToString(Member(query,"ViewType"))=="EnvironmentProbe";
        if(!captureWorkloadFrame&&!probe)return;int view=Number(query,"ViewId");int face=probe?Number(query,"ViewIndex"):-1;if((!probe&&newGeometryViews.ContainsKey(view))||(probe&&probeCoverage.ContainsKey(face)))return;
        systemViews.TryAdd(view,AuditSystemView(query));var groups=new List<object>();var staticGroups=new List<object>();
        object data=((Array)Member(__instance,"m_instanceRenderData")).GetValue(view);
        void AddGroups(object renderData,List<object> rows){foreach(object group in Items(Member(renderData,"InstanceLodGroups"))){object lod=Member(group,"Lod"),parts=Member(lod,"PreprocessedParts");rows.Add(new{Model=Convert.ToString(Member(lod,"DebugName")),Lod=Number(lod,"LodNum"),Instances=Number(group,"InstancesCount"),InstanceIncrement=Number(group,"InstancesIncrement"),ColorParts=AuditPartRanges(parts,"GBufferParts"),DepthParts=AuditPartRanges(parts,"DepthParts"),ForwardParts=AuditPartRanges(parts,"ForwardParts"),TransparentParts=AuditPartRanges(parts,"TransparentParts"),State=Convert.ToString(Member(group,"State")),GBufferIndexCount=Items(Member(parts,"GBufferParts")).Sum(p=>(long)Number(p,"IndicesCount")),DepthIndexCount=Items(Member(parts,"DepthParts")).Sum(p=>(long)Number(p,"IndicesCount"))});}}
        AddGroups(data,groups);foreach(var item in Items(((Array)Member(__instance,"m_staticGroupsRenderData")).GetValue(view)))AddGroups(item,staticGroups);
        var actors=new List<object>();foreach(var instance in Items(Member(Member(query,"Results"),"Instances"))){var actor=(VRage.Render.Scene.MyActor)Member(Member(instance,"Owner"),"Owner");var root=RootOf(actor);object model=Member(instance,"Model"),lod=Member(instance,"LodStrategy");actors.Add(new{Owner=actor.ID,OwnerName=actor.DebugName,BaseModel=Convert.ToString(Member(model,"Filepath")),WorldMatrix=NativeBackend.MatrixValues(actor.WorldMatrix),WorldTranslation=new[]{actor.WorldMatrix.Translation.X,actor.WorldMatrix.Translation.Y,actor.WorldMatrix.Translation.Z},LocalMin=BoxMin((BoundingBoxD)actor.LocalAabb),LocalMax=BoxMax((BoundingBoxD)actor.LocalAabb),CurrentLod=Number(lod,"CurrentLod"),TransitionLod=Number(lod,"TransitionLod"),Transition=Convert.ToSingle(Member(lod,"Transition")),RootName=root?.DebugName,RootOccluded=root==null?(bool?)null:root.OccludedState[view]});}
        var snapshot=new{ViewId=view,ViewType=Convert.ToString(Member(query,"ViewType")),ViewIndex=Number(query,"ViewIndex"),Groups=groups,StaticGroups=staticGroups,Actors=actors};if(captureWorkloadFrame)newGeometryViews.TryAdd(view,snapshot);if(probe)probeCoverage.TryAdd(face,snapshot);
    }
    static void AuditOldRender(object __instance,object cullQuery)
    {
        bool probe=probeCoverageCollecting&&Convert.ToString(Member(cullQuery,"ViewType"))=="EnvironmentProbe";if(!captureWorkloadFrame&&!probe)return;
        int view=Number(cullQuery,"ViewId");systemViews.TryAdd(view,AuditSystemView(cullQuery));
        int face=probe?Number(cullQuery,"ViewIndex"):-1;if((!probe&&oldGeometryViews.ContainsKey(view))||(probe&&oldProbeCoverage.ContainsKey(face)))return;
        if(Convert.ToString(Member(cullQuery,"ViewType"))=="Main"){selectionMainFrustum=((BoundingFrustumD)Member(cullQuery,"Frustum")).Matrix;haveSelectionMainFrustum=true;}
        var work=Member(__instance,"m_workList") as IList;object entry=work?[view];
        var rows=new List<object>();
        if(entry!=null){object item=Member(entry,"WorkItem");foreach(var value in Items(Member(item,"Renderables"))){
            object proxy=Member(value,"RenderProxy"),mesh=Member(proxy,"Mesh"),info=Member(mesh,"Info"),submesh=Member(proxy,"DrawSubmesh");
            var owner=(VRage.Render.Scene.MyActor)Member(Member(proxy,"Parent"),"Owner");object component=Member(proxy,"Parent");var getModel=AccessTools.Method(component.GetType(),"GetModel");string baseModel=null;if(getModel!=null)baseModel=Convert.ToString(Member(Member(getModel.Invoke(component,null),"Info"),"Name"));
            rows.Add(new{Owner=owner.ID,OwnerName=owner.DebugName,Parent=owner.Parent==null?(ulong?)null:Id(owner.Parent,"ID"),BaseModel=baseModel,WorldMatrix=NativeBackend.MatrixValues(owner.WorldMatrix),WorldTranslation=new[]{owner.WorldMatrix.Translation.X,owner.WorldMatrix.Translation.Y,owner.WorldMatrix.Translation.Z},CurrentLod=OptionalNumber(component,"CurrentLod"),PartIndex=Number(proxy,"PartIndex"),StartInstance=Number(proxy,"StartInstance"),ProxyFlags=Convert.ToString(Member(proxy,"Flags")),InstancingEnabled=Convert.ToBoolean(Member(proxy,"InstancingEnabled")),MeshFile=Convert.ToString(Member(info,"FileName")),MeshName=Convert.ToString(Member(info,"Name")),LodDistance=Convert.ToSingle(Member(info,"LodDistance")),IndexCount=Number(submesh,"IndexCount"),Instances=Math.Max(1,Number(proxy,"InstanceCount")),Technique=Convert.ToString(Member(proxy,"Technique")),MaterialType=Convert.ToString(Member(proxy,"Type"))});
        }}
        var sizeCandidates=oldSizeCandidates.TryGetValue(view,out var capturedSize)?capturedSize:new List<object>();
        var snapshot=new{ViewId=view,ViewType=Convert.ToString(Member(cullQuery,"ViewType")),ViewIndex=Number(cullQuery,"ViewIndex"),Rows=rows,SizeCandidates=sizeCandidates};if(captureWorkloadFrame)oldGeometryViews.TryAdd(view,snapshot);if(probe)oldProbeCoverage.TryAdd(face,snapshot);
    }
    static void AuditOldSizeBeforeTrim(object cullQuery)
    {
        if(!captureWorkloadFrame)return;
        int view=Number(cullQuery,"ViewId");if(oldSizeCandidates.ContainsKey(view))return;
        var sizeCandidates=new List<object>();
        {
            var renderType=AccessTools.TypeByName("VRageRender.MyRender11");var env=AccessTools.Field(renderType,"Environment").GetValue(null);
            Matrix projection=(Matrix)Member(Member(env,"Matrices"),"Projection");var resolution=VRageRender.MyRenderProxy.BackBufferResolution;
            foreach(object proxy in Items(Member(Member(cullQuery,"Results"),"CullProxies"))){
                object component=Member(proxy,"Parent");if(component==null)continue;
                var owner=(VRage.Render.Scene.MyActor)Member(component,"Owner");float squared=owner.CalculateCameraDistanceSquared(),last=Convert.ToSingle(Member(component,"m_lastLodDistanceSqr"));
                bool predicted=StockActorSizeCulling.Evaluate(squared,last,owner.VolumeExtent,ref projection,(uint)resolution.X,(uint)resolution.Y,out float? area);
                var model=AccessTools.Method(component.GetType(),"GetModel").Invoke(component,null);
                sizeCandidates.Add(new{Id=owner.ID,Model=Convert.ToString(Member(Member(model,"Info"),"Name")),WorldPosition=owner.WorldMatrix.Translation.ToString(),Parented=owner.Parent!=null,VolumeExtent=owner.VolumeExtent,ViewerDistanceSquared=squared,FinalAuthoredLodDistanceSquared=last,ProjectedPixelArea=area,PredictedSizeCull=predicted,ActualSizeCulled=Convert.ToBoolean(Member(component,"IsCulled"))});
            }
        }
        oldSizeCandidates.TryAdd(view,sizeCandidates);
    }
    static object[] AuditMaterialFlags(object data)
    {
        var flags=new List<object>();
        foreach(DictionaryEntry entry in (IDictionary)Member(data,"Materials")){
            object value=Member(entry.Value,"Flags");
            flags.Add(new{Material=Convert.ToString(entry.Key),Add=Convert.ToString(Member(value,"Add")),Remove=Convert.ToString(Member(value,"Remove"))});
        }
        return flags.ToArray();
    }
    static object AuditVulkan(object renderer)
    {
        object scene=SceneField.GetValue(renderer),cache=Member(scene,"models");var models=(IDictionary)Member(cache,"models");
        var keys=new Dictionary<object,string>(ReferenceEqualityComparer.Instance);var meshParts=new Dictionary<ulong,(string Model,int Indices)>();var modelRows=new List<object>();
        foreach(DictionaryEntry value in models){string key=Convert.ToString(value.Key);object model=value.Value;if(model==null){modelRows.Add(new{Key=key,Missing=true});continue;}keys[model]=key;var parts=new List<object>();foreach(object part in Items(Member(model,"Parts"))){ulong mesh=Id(part,"Mesh");int indices=((Array)Member(part,"Indices")).Length;meshParts[mesh]=(key,indices);parts.Add(new{Mesh=mesh,IndexStart=Member(part,"IndexStart"),IndexCount=indices,Technique=Convert.ToString(Member(part,"Technique")),Flags=Id(part,"Flags"),Material=Convert.ToString(Member(part,"MaterialName"))});foreach(object skin in Items(Member(part,"SkinParts")))meshParts[Id(skin,"Mesh")]=(key,Number(skin,"IndexCount"));}if(Member(model,"DepthRanges") is IDictionary ranges)foreach(DictionaryEntry range in ranges)meshParts[Convert.ToUInt64(range.Value)]=(key,checked((int)(Convert.ToUInt64(range.Key)&uint.MaxValue)));modelRows.Add(new{Key=key,File=Convert.ToString(Member(model,"Name")),SharedMesh=Member(model,"SharedMesh"),Parts=parts});}
        var target=(NativeBackend)BackendField.GetValue(renderer);var packets=new List<object>();long mainIndices=0;int mainPackets=0,unmappedScenePackets=0;var cascadeIndices=new long[8];
        foreach(object packet in Items(Member(target,"worldPackets"))){ulong mesh=Id(packet,"Mesh");int type=Number(packet,"Pipeline"),flags=Number(packet,"Flags"),mask=Number(packet,"CascadeMask"),instances=Number(packet,"Count");bool known=meshParts.TryGetValue(mesh,out var part);bool main=(flags&(4096|8192))==0;
            if(type<20){if(!known)unmappedScenePackets++;else{long work=(long)part.Indices*instances;if(main){mainIndices+=work;mainPackets++;}if((flags&(512|8192))==0)for(int c=0;c<8;c++)if((mask&(1<<c))!=0)cascadeIndices[c]+=work;}}
            packets.Add(new{Pipeline=type,Mesh=mesh,Instances=instances,Flags=flags,Cascades=mask,MainSelected=main,IndexCount=known?(int?)part.Indices:null,ModelKey=known?part.Model:null});
        }
        var logicalRows=new List<object>();long logicalMainIndices=0;var logicalCascadeIndices=new long[8];int unmappedLogicalRows=0;var actorMap=(IDictionary)Member(scene,"actors");
        foreach(var draw in target.PeekLogicalSelectionCapture()){
            if(draw.Pipeline>=20 || draw.Owner==uint.MaxValue)continue;
            object data=actorMap[draw.Owner];if(data==null)throw new Exception("Logical scene owner was not captured");var identity=(VRage.Render.Scene.MyActor)Member(data,"Actor");var root=RootOf(identity);object nativeGroup=Member(data,"OcclusionGroup");var logicalBounds=(BoundingBoxD)Member(data,"Bounds");
            object baseModel=Member(data,"Model"),selectedModel=Member(data,"RenderModel");bool newModel=Convert.ToBoolean(AccessTools.Method(cache.GetType(),"IsStockNewModel").Invoke(cache,new[]{baseModel}));bool mapped=meshParts.TryGetValue(draw.Mesh,out var part);
            if(!mapped)unmappedLogicalRows++;else{long work=(long)part.Indices*draw.Count;if((draw.Flags&(4096|8192))==0)logicalMainIndices+=work;if((draw.Flags&(512|8192))==0)for(int c=0;c<8;c++)if((draw.Cascades&(1u<<c))!=0)logicalCascadeIndices[c]+=work;}
            logicalRows.Add(new{ConvertedOldMaterial=AccessTools.Field(data.GetType(),"ConvertedOldMaterial")?.GetValue(data),MaterialFlags=AuditMaterialFlags(data),NewModelEligible=newModel,ActorFlags=Convert.ToString(Member(data,"Flags")),RootName=root?.DebugName,RootId=root?.ID,GroupHidden=nativeGroup==null?(bool?)null:Convert.ToBoolean(Member(nativeGroup,"Hidden")),GroupMin=nativeGroup==null?null:BoxMin((BoundingBoxD)Member(nativeGroup,"Bounds")),GroupMax=nativeGroup==null?null:BoxMax((BoundingBoxD)Member(nativeGroup,"Bounds")),LocalMin=BoxMin((BoundingBoxD)identity.LocalAabb),LocalMax=BoxMax((BoundingBoxD)identity.LocalAabb),BoundsMin=BoxMin(logicalBounds),BoundsMax=BoxMax(logicalBounds),InstanceStart=Number(data,"InstanceStart"),DeclaredInstances=Number(data,"InstanceCount"),Owner=draw.Owner,OwnerName=identity.DebugName,Parent=identity.Parent==null?(ulong?)null:Id(identity.Parent,"ID"),BaseModel=Convert.ToString(Member(baseModel,"Name")),SelectedModel=selectedModel==null?null:Convert.ToString(Member(selectedModel,"Name")),WorldMatrix=NativeBackend.MatrixValues(identity.WorldMatrix),WorldTranslation=new[]{identity.WorldMatrix.Translation.X,identity.WorldMatrix.Translation.Y,identity.WorldMatrix.Translation.Z},Instances=draw.Count,Pipeline=draw.Pipeline,Flags=draw.Flags,Cascades=draw.Cascades,MainSelected=(draw.Flags&(4096|8192))==0,Mesh=draw.Mesh,IndexCount=mapped?(int?)part.Indices:null,ModelKey=mapped?part.Model:null});
        }
        var selected=(HashSet<uint>)Member(scene,"visible");var actorRows=new List<object>();Vector3D camera=Sandbox.Game.World.MySector.MainCamera.Position;
        object shadows=Member(renderer,"shadows");vulkanShadowSettings=AuditValue(Member(shadows,"Settings"));systemViews.TryAdd(0,new{ViewId=0,ViewType="Main",ViewProjection=AuditValue(Member(scene,"frustum"))});var lightMatrices=(MatrixD[])Member(shadows,"worldMatrices");var fitted=(bool[])Member(shadows,"fitted");var splits=(float[])Member(shadows,"splits");
        bool scheduled=Convert.ToBoolean(Member(shadows,"StockSchedulingEnabled")),stockFit=Convert.ToBoolean(Member(shadows,"StockFittingEnabled"));uint updates=Convert.ToUInt32(Member(shadows,"UpdateMask"));var factors=Member(shadows,"ProjectionFactors") as float[];var shadowSettings=(VRageRender.MyShadowsSettings)Member(shadows,"Settings");
        bool shadowOcclusion=stockFit && Convert.ToBoolean(Member(scene,"StockShadowOcclusionEnabled"));
        for(int c=0;c<splits.Length;c++)systemViews.TryAdd(c+1,new{ViewId=c+1,ViewType="ShadowCascade",ViewIndex=c,Frustum=AuditValue(lightMatrices[c]),Fitted=fitted[c],Split=splits[c],Scheduled=!scheduled || (updates&(1u<<c))!=0,SmallObjects=new{ProjectionFactor=factors?[c],SkipThreshold=shadowSettings.Cascades[c].SkippingSmallObjectThreshold},ProjectedSizePruningApplied=stockFit,PerViewOcclusionApplied=shadowOcclusion,Occlusion=AccessTools.Method(scene.GetType(),"GetShadowOcclusionStats").Invoke(scene,null)});
        var lightFrusta=new BoundingFrustumD[splits.Length];for(int i=0;i<lightFrusta.Length;i++)if(fitted[i])lightFrusta[i]=new BoundingFrustumD(lightMatrices[i]);
        var shadowRows=new List<object>();
        foreach(DictionaryEntry entry in (IDictionary)Member(scene,"actors")){
            object actor=entry.Value;if(Member(actor,"Model")==null)continue;
            var bounds=(BoundingBoxD)Member(actor,"Bounds");uint current=(uint)AccessTools.Method(shadows.GetType(),"CascadeMask").Invoke(shadows,new object[]{bounds,camera});uint exact=0;
            for(int i=0;i<lightFrusta.Length;i++)if(lightFrusta[i]==null||lightFrusta[i].Contains(bounds)!=ContainmentType.Disjoint)exact|=1u<<i;
            uint eligible=scheduled?exact&updates:exact;shadowRows.Add(new{Id=Convert.ToUInt32(entry.Key),CurrentCameraSliceMask=current,FittedLightMask=exact,ScheduledFittedMask=eligible,MissedLightCascades=stockFit?0u:eligible&~current,ProjectedSizePrunedMask=stockFit?eligible&~current:0u,ExtraCameraCascades=current&~exact,Center=bounds.Center.ToString(),Radius=bounds.HalfExtents.Length()});
        }
        var projection=(Matrix)(MatrixD)Member(renderer,"cameraProjection");
        int viewportWidth=VRageRender.MyRenderProxy.BackBufferResolution.X,viewportHeight=VRageRender.MyRenderProxy.BackBufferResolution.Y;
        foreach(DictionaryEntry value in (IDictionary)Member(scene,"actors")){
            object actor=value.Value,model=Member(actor,"Model");if(model==null || !selected.Contains(Convert.ToUInt32(value.Key)))continue;
            var bounds=(BoundingBoxD)Member(actor,"Bounds");var identity=(VRage.Render.Scene.MyActor)Member(actor,"Actor");var matrix=identity.WorldMatrix;
            bool parented=identity.Parent!=null;float squared=parented?identity.CalculateCameraDistanceSquared():(float)bounds.DistanceSquared(camera);
            var extent=parented&&identity.HasLocalAabb?(Vector3D)identity.LocalAabb.Extents:bounds.Extents;
            float volume=(float)Math.Max(Math.Max(extent.X,extent.Y),extent.Z);bool inputsKnown=!parented||identity.HasLocalAabb;
            var distances=(float[])Member(model,"LodDistances");float finalSquared=distances.Length==0?0:distances.Max(d=>d*d);
            float? area=null;
            if(inputsKnown&&squared>finalSquared){Vector3 position=new(volume,volume,-(float)Math.Sqrt(squared));Vector3.TransformProjection(ref position,ref projection,out Vector3 projected);float projectedArea=Math.Abs(projected.X*viewportWidth*.5f*projected.Y*viewportHeight*.5f);if(float.IsFinite(projectedArea))area=projectedArea;}
            actorRows.Add(new{Id=Convert.ToUInt32(value.Key),ModelKey=keys.TryGetValue(model,out string key)?key:null,Distance=Vector3D.Distance(camera,matrix.Translation),BoundDistance=Vector3D.Distance(camera,bounds.Center),WorldScale=matrix.Forward.Length(),Radius=bounds.HalfExtents.Length(),Parented=parented,VolumeExtent=volume,CachedActorVolumeExtent=identity.VolumeExtent,VolumeSource=parented?"AuthoritativeLocalAabb":"AuthoritativeWorldBounds",SizeContractInputsKnown=inputsKnown,ViewerDistanceSquared=squared,FinalAuthoredLodDistanceSquared=finalSquared,ProjectedPixelArea=area,StockSizeWouldCull=area.HasValue&&area.Value<20,StockActorPolicyEligible=Member(actor,"StaticInstances")==null&&Member(actor,"SkinMatrices")==null});
        }
        var nativePassRows=new List<object>();var nativeOwnerRows=new List<object>();if(target.NativePassReady){var groups=target.GetPassGroupAudit(out uint[] groupOwners);var sharedModels=new Dictionary<ulong,string>();foreach(DictionaryEntry value in models){if(value.Value!=null)sharedModels[Id(value.Value,"SharedMesh")]=Convert.ToString(value.Key);}foreach(var group in groups)nativePassRows.Add(new{group.View,group.Kind,group.Shader,group.First,group.Count,group.Instances,group.Flags,group.Mesh,group.Color,group.Normal,group.Extensions,group.Alpha,ModelKey=sharedModels.TryGetValue(group.Mesh,out var modelName)?modelName:null,Owners=groupOwners.Skip((int)group.OwnerOffset).Take((int)group.Instances).ToArray()});
            // Render IDs depend on asynchronous load order. Capture persistent
            // game identities and exact pose once, rather than claiming that
            // matching numeric render IDs proves cross-run owner parity.
            var nativeOwners=new HashSet<uint>(groupOwners);foreach(DictionaryEntry entry in (IDictionary)Member(scene,"actors")){if(!nativeOwners.Contains(Convert.ToUInt32(entry.Key)))continue;var data=entry.Value;var actor=(VRage.Render.Scene.MyActor)Member(data,"Actor");var root=RootOf(actor);var bounds=(BoundingBoxD)Member(data,"Bounds");nativeOwnerRows.Add(new{Owner=actor.ID,OwnerName=actor.DebugName,RootName=root?.DebugName,ActorFlags=Convert.ToString(Member(data,"Flags")),ConvertedOldMaterial=AccessTools.Field(data.GetType(),"ConvertedOldMaterial")?.GetValue(data),MaterialFlags=AuditMaterialFlags(data),OldViewerDistanceSquared=actor.Parent==null?(float)bounds.DistanceSquared(latestSystemCamera?.CameraPosition??camera):actor.CalculateCameraDistanceSquared(),BaseModel=Convert.ToString(Member(Member(data,"Model"),"Name")),WorldMatrix=NativeBackend.MatrixValues(actor.WorldMatrix),WorldTranslation=new[]{actor.WorldMatrix.Translation.X,actor.WorldMatrix.Translation.Y,actor.WorldMatrix.Translation.Z},BoundsMin=BoxMin(bounds),BoundsMax=BoxMax(bounds)});}
        }
        return new{Backend="Vulkan",Camera=camera.ToString(),NativePassGroups=nativePassRows,NativePassOwners=nativeOwnerRows,NativePassScene=AccessTools.Method(scene.GetType(),"GetNativePassSceneStats").Invoke(scene,null),LogicalRows=logicalRows,LogicalMainSceneIndexInvocations=logicalMainIndices,LogicalCascadeSceneIndexInvocations=logicalCascadeIndices,UnmappedLogicalRows=unmappedLogicalRows,ShadowScheduling=new{Enabled=Member(shadows,"StockSchedulingEnabled"),Updates=Member(shadows,"UpdateMask"),Valid=Member(shadows,"ValidMask"),Usage=Member(shadows,"UsageFeedback")},Models=modelRows,MainCandidates=actorRows,ShadowCandidates=shadowRows,Packets=packets,MainSceneIndexInvocations=mainIndices,MainScenePackets=mainPackets,CascadeSceneIndexInvocations=cascadeIndices,UnmappedScenePackets=unmappedScenePackets,
            Scope="One authoritative queued frame; main opaque index counts multiplied by instances. Voxels, glass, foliage and other passes excluded from scene totals. MainCandidates precede occlusion; packet flags define actual main selection. StockSizeWouldCull is a read-only prediction of installed old-pipeline distance/extent/projection semantics, not an applied rendering policy or FPS gain. Native unparented cached AABB is empty; authoritative scene bounds supply its world distance/extent."};
    }
    static object AuditDx11()
    {
        Type managers=AccessTools.TypeByName("VRage.Render11.Common.MyManagers");object geometry=AccessTools.Field(managers,"GeometryRenderer").GetValue(null),cull=AccessTools.Field(managers,"Cull").GetValue(null);object queries=AccessTools.Method(cull.GetType(),"GetCullQueries").Invoke(cull,null);var activeViews=new HashSet<int>();var queryArray=(Array)Member(queries,"CullQueries");for(int i=0;i<Number(queries,"Size");i++)activeViews.Add(Number(queryArray.GetValue(i),"ViewId"));var rows=new List<object>();int pass=0;
        foreach(object data in Items(Member(geometry,"m_instanceRenderData"))){if(activeViews.Contains(pass))foreach(object group in Items(Member(data,"InstanceLodGroups"))){object lod=Member(group,"Lod"),parts=Member(lod,"PreprocessedParts");rows.Add(new{ViewId=pass,Model=Convert.ToString(Member(lod,"DebugName")),Lod=Number(lod,"LodNum"),Instances=Number(group,"InstancesCount"),State=Convert.ToString(Member(group,"State")),GBufferIndexCount=Items(Member(parts,"GBufferParts")).Sum(p=>(long)Number(p,"IndicesCount")),DepthIndexCount=Items(Member(parts,"DepthParts")).Sum(p=>(long)Number(p,"IndicesCount"))});}pass++;}
        var staticRows=new List<object>();pass=0;
        foreach(object list in Items(Member(geometry,"m_staticGroupsRenderData"))){if(activeViews.Contains(pass))foreach(object data in Items(list))foreach(object group in Items(Member(data,"InstanceLodGroups"))){object lod=Member(group,"Lod"),parts=Member(lod,"PreprocessedParts");staticRows.Add(new{ViewId=pass,Model=Convert.ToString(Member(lod,"DebugName")),Lod=Number(lod,"LodNum"),Instances=Number(group,"InstancesCount"),State=Convert.ToString(Member(group,"State")),GBufferIndexCount=Items(Member(parts,"GBufferParts")).Sum(p=>(long)Number(p,"IndicesCount")),DepthIndexCount=Items(Member(parts,"DepthParts")).Sum(p=>(long)Number(p,"IndicesCount"))});}pass++;}
        var allActors=new List<object>();Type componentType=AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent");var getComponent=AccessTools.Method(typeof(VRage.Render.Scene.MyActor),"GetComponent").MakeGenericMethod(componentType);
        var trackers=(IDictionary)AccessTools.Field(typeof(VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>),"m_dict").GetValue(null);
        if(!haveSelectionMainFrustum)throw new Exception("Main render job frustum was not captured");var mainFrustum=new BoundingFrustumD(selectionMainFrustum);int skippedBounds=0;
        foreach(DictionaryEntry entry in trackers){var identity=(VRage.Render.Scene.MyActor)Member(entry.Value,"Value");if(identity==null||identity.IsDestroyed)continue;object component=getComponent.Invoke(identity,null);if(component==null)continue;if(!identity.HasLocalAabb){skippedBounds++;continue;}
            object model=AccessTools.Method(component.GetType(),"GetModel").Invoke(component,null);var root=RootOf(identity);var bounds=identity.CalculateAabb();object instance=Member(component,"m_instancing");
            bool ancestorVisible=true;for(var parent=identity;parent!=null;parent=parent.Parent as VRage.Render.Scene.MyActor)ancestorVisible&=parent.IsVisible;
            allActors.Add(new{Owner=identity.ID,OwnerName=identity.DebugName,BaseModel=Convert.ToString(Member(Member(model,"Info"),"Name")),WorldTranslation=new[]{identity.WorldMatrix.Translation.X,identity.WorldMatrix.Translation.Y,identity.WorldMatrix.Translation.Z},WorldMatrix=NativeBackend.MatrixValues(identity.WorldMatrix),LocalMin=BoxMin((BoundingBoxD)identity.LocalAabb),LocalMax=BoxMax((BoundingBoxD)identity.LocalAabb),BoundsMin=BoxMin(bounds),BoundsMax=BoxMax(bounds),Frustum=mainFrustum.Contains(bounds).ToString(),Visible=identity.IsVisible,AncestorVisible=ancestorVisible,IsRendered=Convert.ToBoolean(Member(component,"IsRendered")),Standalone=Convert.ToBoolean(Member(component,"IsRenderedStandAlone")),SizeCulled=Convert.ToBoolean(Member(component,"IsCulled")),CurrentLod=OptionalNumber(component,"CurrentLod"),LodMultiplier=Convert.ToSingle(Member(component,"m_lodDistanceMultiplierInv")),RootId=root?.ID,RootName=root?.DebugName,RootOccluded=root==null?(bool?)null:root.OccludedState[0],RootMin=root==null?null:BoxMin(root.WorldAabb),RootMax=root==null?null:BoxMax(root.WorldAabb)});
        }
        Type common=AccessTools.TypeByName("VRageRender.MyCommon");object coefficient=AccessTools.Property(common,"LODCoefficient").GetValue(null);
        return new{Backend="Dx11",AllActorsSkippedMissingBounds=skippedBounds,AllActors=allActors,Camera=Sandbox.Game.World.MySector.MainCamera.Position.ToString(),LodCoefficient=coefficient,StockLodSettings=AccessTools.Property(common,"LoddingSettings").GetValue(null),GlobalLodSettings=Member(geometry,"m_globalLoddingSettings"),PerViewLodSettings=Member(geometry,"m_loddingSetting"),InstanceLodGroups=rows,StaticLodGroups=staticRows,OldGeometryViews=oldGeometryViews.Values.ToArray(),NewGeometryViews=newGeometryViews.Values.ToArray(),
            Scope="Prepared GeometryStage2 instance/static groups after render completion and old-pipeline renderables on owning render jobs. Includes per-view data; not all transparent/decal/voxel work. No FPS claim."};
    }
}
