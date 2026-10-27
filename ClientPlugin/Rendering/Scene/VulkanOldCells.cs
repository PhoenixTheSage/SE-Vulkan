using System;
using System.Collections.Generic;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Rendering.Scene;
// Static OLD cube grid cells leave the per-frame managed traversal. Managed
// code commits each cell's inputs and per-LOD retained parts when they change;
// native code reproduces the retained-submission frame decision and appends
// the same world draws. Highlights, shadow exclusions, dithering, glass and
// unsupported policies stay on the managed path.
public sealed partial class VulkanScene
{
    public bool NativeCellsEnabled {get;set;}=System.Environment.GetEnvironmentVariable("SE_VULKAN_NATIVE_CELLS")!="0";
    public bool OldOwnerOrderEnabled {get=>backend?.OldOwnerOrderEnabled??false;set{if(backend!=null)backend.OldOwnerOrderEnabled=value;}}
    public bool PipelinedRecordingEnabled {get=>backend?.PipelinedRecordingEnabled??false;set{if(backend!=null)backend.PipelinedRecordingEnabled=value;}}
    // Full commits rebuild representations; pose commits (transforms, shadow
    // poses, group and exclusion changes) send only placement and flags.
    readonly HashSet<ActorData> cellDirty=new(),cellPoseDirty=new();
    bool cellsActive;int cellPolicy=-1;uint cellRecipeRevision;
    int ownedCells;
    public int NativeCellCount=>ownedCells;
    int CellPolicy()=>(NativeCellsEnabled?1:0)|(ActorLodsEnabled?2:0);
    bool CellsAllowed()=>NativeCellsEnabled&&nativePassActive&&!gpuActive&&backend.WorldReady&&backend.RetainedSubmissionEnabled&&backend.WorldRecipesEnabled&&backend.OldOwnerOrderEnabled
        &&!backend.ModelBatchingEnabled&&!backend.BulkModelsEnabled&&GroupOcclusionEnabled&&!ActorSizeCullingEnabled&&!ViewEligibilityEnabled&&ShadowSystem!=null&&CascadeMaskFor!=null;
    bool CellSupported(ActorData data)
    {
        if(data.Model==null||data.InstanceBuffer==uint.MaxValue||!instances.TryGetValue(data.InstanceBuffer,out var buffer)||buffer.Cubes==null)return false;
        if(data.StaticInstances!=null||data.SkinMatrices!=null||data.Dithering!=0||data.GpuOwned||data.PassOwned||!KnownStockOldPipeline(data))return false;
        uint id=data.Id;return !HasHighlight(id)&&!buildLights.Excluded.Contains(id);
    }
    void ReleaseCell(ActorData data,bool handoff)
    {
        if(!data.CellOwned)return;var lod=backend.RemoveCell(data.CellHandle);data.CellHandle=0;data.CellOwned=false;ownedCells--;SyncIndexMembership(data);
        if(!handoff)return;
        data.Lod.Import(new NativeGpuOpaqueSelector{Current=lod.Current,Other=lod.Next,Transition=lod.Transition,Start=lod.Start,Initialized=lod.Initialized,Border=lod.Border});
        var models=data.CellModels;VulkanModel Model(int key)=>key>=0&&models!=null&&key<models.Length?models[key]:null;
        data.LodShadowModel=Model(lod.ShadowCurrent);data.LodShadowNext=Model(lod.ShadowNext);data.LodShadowAlpha=lod.ShadowAlpha;
    }
    // Runs after native pass commits; every dirty or policy-affected actor is
    // committed to native ownership or returned to managed traversal.
    void CommitCells()
    {
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CellCommits);
        bool allowed=CellsAllowed()&&frustum!=null;int policy=allowed?CellPolicy():0;
        if(policy!=cellPolicy){cellPolicy=policy;foreach(var data in actors.Values)if(data.CellOwned||data.InstanceBuffer!=uint.MaxValue)cellDirty.Add(data);}
        // Retired recipes (mesh removal) only recommit the cells that used them.
        if(backend.WorldRecipeRevision!=cellRecipeRevision){cellRecipeRevision=backend.WorldRecipeRevision;foreach(var data in actors.Values)if(data.CellOwned&&data.CellRecipes!=null)foreach(var recipe in data.CellRecipes)if(!recipe.Alive){cellDirty.Add(data);break;}}
        cellsActive=allowed;
        foreach(var data in cellPoseDirty){
            if(cellDirty.Contains(data)||BuildFrameOf(data).Destroyed||!actors.ContainsKey(data.Id))continue;
            if(data.CellOwned&&allowed&&CellSupported(data))AppendPose(data);else cellDirty.Add(data);
        }
        cellPoseDirty.Clear();
        if(cellPoses.Count!=0){
            // Reused buffers: the native call copies the first count entries before returning.
            if(cellPoseArray.Length<cellPoses.Count){cellPoseArray=new NativeCellPose[cellPoses.Count*2];cellPoseHandleArray=new ulong[cellPoses.Count*2];}
            cellPoses.CopyTo(cellPoseArray);cellPoseHandles.CopyTo(cellPoseHandleArray);
            backend.UpdateCellPoses(cellPoseHandleArray,cellPoseArray,cellPoses.Count);cellPoses.Clear();cellPoseHandles.Clear();}
        if(cellDirty.Count==0)return;
        cellInputs.Clear();cellHandles.Clear();cellOwners.Clear();cellLevels.Clear();cellParts.Clear();cellThresholds.Clear();cellBytes.Clear();
        foreach(var data in cellDirty){
            if(BuildFrameOf(data).Destroyed||!actors.ContainsKey(data.Id))continue;
            if(!allowed||!CellSupported(data)||!AppendCell(data)){ReleaseCell(data,true);continue;}
            cellOwners.Add(data);
        }
        cellDirty.Clear();
        int count=cellInputs.Count;if(count==0)return;
        var handles=cellHandles.ToArray();var output=new ulong[count];
        backend.CommitCells(cellInputs.ToArray(),count,handles,output,cellLevels.ToArray(),cellLevels.Count,cellParts.ToArray(),cellParts.Count,cellThresholds.ToArray(),cellThresholds.Count,cellBytes.ToArray(),cellBytes.Count);
        for(int i=0;i<count;i++){var data=cellOwners[i];if(!data.CellOwned)ownedCells++;data.CellHandle=output[i];data.CellOwned=true;SyncIndexMembership(data);}
    }
    readonly List<NativeCellPose> cellPoses=new();readonly List<ulong> cellPoseHandles=new();NativeCellPose[] cellPoseArray=Array.Empty<NativeCellPose>();ulong[] cellPoseHandleArray=Array.Empty<ulong>();
    uint CellFlags(ActorData data,bool eligible,bool skinning){var flags=data.Flags;bool castFlag=(flags&RenderFlags.CastShadows)!=0,casts=(flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
        return (BuildFrameOf(data).Visible?1u:0u)|((flags&RenderFlags.SkipInMainView)!=0?2u:0u)|(castFlag?4u:0u)|(casts?8u:0u)|(data.DynamicShadow?16u:0u)|(BuildFrameOf(data).HasParent?32u:0u)|(eligible?64u:0u)|(skinning?128u:0u)|((flags&RenderFlags.DrawOutsideViewDistance)!=0?256u:0u);}
    unsafe void AppendPose(ActorData data)
    {
        var pose=new NativeCellPose{Size=344,Flags=CellFlags(data,false,instances[data.InstanceBuffer].EnabledSkinning),Group=data.OcclusionGroup?.QueryId??0};
        var bounds=data.Bounds;pose.Bounds[0]=bounds.Min.X;pose.Bounds[1]=bounds.Min.Y;pose.Bounds[2]=bounds.Min.Z;pose.Bounds[3]=bounds.Max.X;pose.Bounds[4]=bounds.Max.Y;pose.Bounds[5]=bounds.Max.Z;
        var actorFrame=BuildFrameOf(data);var local=actorFrame.LocalAabb;pose.LocalMinimum[0]=local.Min.X;pose.LocalMinimum[1]=local.Min.Y;pose.LocalMinimum[2]=local.Min.Z;pose.LocalMaximum[0]=local.Max.X;pose.LocalMaximum[1]=local.Max.Y;pose.LocalMaximum[2]=local.Max.Z;
        var world=actorFrame.World;var inverse=actorFrame.WorldInv;for(int r=0;r<4;r++)for(int c=0;c<4;c++){pose.World[r*4+c]=world[r,c];pose.Inverse[r*4+c]=inverse[r,c];}
        cellPoses.Add(pose);cellPoseHandles.Add(data.CellHandle);
    }
    readonly List<NativeCell> cellInputs=new();readonly List<ulong> cellHandles=new();readonly List<ActorData> cellOwners=new();
    readonly List<NativeCellLevel> cellLevels=new();readonly List<NativeCellPart> cellParts=new();readonly List<float> cellThresholds=new();readonly List<byte> cellBytes=new();
    // Mirrors SubmitPreparedActor's representation and PrepareInstances for
    // one cell, for every LOD level the stock selector can draw.
    unsafe bool AppendCell(ActorData data)
    {
        var buffer=instances[data.InstanceBuffer];var root=data.Model;
        bool eligible=ActorLodsEnabled&&root.LodDistances.Length!=0;int levelCount=eligible?root.LodDistances.Length+1:1;
        var distinct=new List<VulkanModel>();var levelModels=new VulkanModel[levelCount];
        for(int lod=0;lod<levelCount;lod++){var model=models.GetLod(root,lod);levelModels[lod]=model;if(!distinct.Contains(model))distinct.Add(model);
            foreach(var part in model.Parts)if((part.Flags&4)!=0)return false;} // Glass representations use the legacy path.
        int levelStart=cellLevels.Count,partStart=cellParts.Count;var recipes=new List<NativeBackend.WorldRecipe>();var hsv=data.Hsv+colorizeHsv;bool oldShaders=UsesOldDecalShaders(data);
        for(int lod=0;lod<levelCount;lod++){
            var model=levelModels[lod];uint type=model.BlendWeights!=null&&model.BlendWeights.Length!=0?4u:3u;int first=cellParts.Count;
            int partIndex=0;
            foreach(var part in oldShaders?model.Parts:model.NewParts??model.Parts){
                MaterialOverride material=null;if(data.Materials.Count!=0)data.Materials.TryGetValue(part.MaterialName??string.Empty,out material);
                if(material!=null&&((material.Flags.Add&RenderFlags.SkipInMainView)!=0||(material.Flags.Remove&RenderFlags.Visible)!=0))continue;
                uint pipeline=MaterialPipeline(data,part,type);
                var prepared=new NativeBackend.PreparedPart(pipeline,part.Mesh,
                    TextureCache.GetTexture(material?.Textures.ColorMetalFileName??part.ColorTexture),
                    TextureCache.GetWorldNormal(material?.Textures.NormalGlossFileName??part.NormalTexture),
                    TextureCache.GetWorldExtensions(material?.Textures.ExtensionsFileName??part.ExtensionTexture),
                    TextureCache.GetWorldAlpha(material?.Textures.AlphamaskFileName??part.AlphaTexture),
                    MaterialTint(type,material?.Color??data.Color),hsv,material?.Emissivity??1,data.Dithering,backend.WorldLayouts[pipeline],MaterialRasterFlags(data,part,material)|(oldShaders?NativeBackend.OldGeometryOrder:0u),(uint)lod,OldPartOrder(data,model,part,lod));
                var recipe=backend.EnsureWorldRecipe(ref prepared.Recipe,prepared.Pipeline,prepared.Mesh,prepared.Color,prepared.Normal,prepared.Extensions,prepared.Alpha,prepared.Constants.Length);recipes.Add(recipe);
                uint viewFlags=prepared.ViewFlags|(WorldShaderVariants.IsMeshDecal(pipeline)?512u|16384u:0u);
                cellParts.Add(new NativeCellPart{Recipe=recipe.Handle,ViewFlags=viewFlags,Row0=(uint)prepared.Rows.Row0,Row1=(uint)prepared.Rows.Row1,Row2=(uint)prepared.Rows.Row2,AlphaOffset=(uint)prepared.AlphaOffset,MainPart=(uint)partIndex++,ConstantOffset=(uint)cellBytes.Count,ConstantBytes=(uint)prepared.Constants.Length,OldOrder=prepared.OldOrder});
                cellBytes.AddRange(prepared.Constants);
            }
            cellLevels.Add(new NativeCellLevel{Model=(uint)distinct.IndexOf(model),PartOffset=(uint)first,PartCount=(uint)(cellParts.Count-first)});
        }
        int available=buffer.Cubes.Length,start=Math.Max(0,data.InstanceStart);uint count=(uint)Math.Max(0,Math.Min((long)available-start,Math.Max(0,data.InstanceCount)));
        var stream=new byte[checked((int)count*64)];for(int i=0;i<count;i++)WriteCubeInstance(stream,i,buffer.Cubes[start+i],colorizeHsv);
        var input=new NativeCell{Size=416,Owner=data.Id,Group=data.OcclusionGroup?.QueryId??0,InstanceCount=count,InstanceOffset=(ulong)cellBytes.Count,LevelOffset=(uint)levelStart,LevelCount=(uint)levelCount,ThresholdOffset=(uint)cellThresholds.Count,ThresholdCount=eligible?(uint)root.LodDistances.Length:0};
        cellBytes.AddRange(stream);if(eligible)cellThresholds.AddRange(root.LodDistances);
        input.Flags=CellFlags(data,eligible,buffer.EnabledSkinning);
        var bounds=data.Bounds;input.Bounds[0]=bounds.Min.X;input.Bounds[1]=bounds.Min.Y;input.Bounds[2]=bounds.Min.Z;input.Bounds[3]=bounds.Max.X;input.Bounds[4]=bounds.Max.Y;input.Bounds[5]=bounds.Max.Z;
        var actorFrame=BuildFrameOf(data);var local=actorFrame.LocalAabb;input.LocalMinimum[0]=local.Min.X;input.LocalMinimum[1]=local.Min.Y;input.LocalMinimum[2]=local.Min.Z;input.LocalMaximum[0]=local.Max.X;input.LocalMaximum[1]=local.Max.Y;input.LocalMaximum[2]=local.Max.Z;
        var world=actorFrame.World;var inverse=actorFrame.WorldInv;for(int r=0;r<4;r++)for(int c=0;c<4;c++){input.World[r*4+c]=world[r,c];input.Inverse[r*4+c]=inverse[r,c];}
        var selector=data.Lod.Export();int Key(VulkanModel model)=>model==null?-1:distinct.IndexOf(model);
        input.Lod=new NativeCellLod{Current=selector.Current,Next=selector.Other,Transition=selector.Transition,Start=selector.Start,Initialized=selector.Initialized,Border=selector.Border,ShadowCurrent=Key(data.LodShadowModel),ShadowNext=Key(data.LodShadowNext),ShadowAlpha=data.LodShadowAlpha};
        if(input.Lod.Current<0||input.Lod.Current>=levelCount||input.Lod.Next<0||input.Lod.Next>=levelCount){input.Lod.Current=input.Lod.Next=0;input.Lod.Transition=0;input.Lod.Initialized=0;input.Lod.Border=0;}
        data.CellModels=distinct.ToArray();data.CellRecipes=recipes.ToArray();cellInputs.Add(input);cellHandles.Add(data.CellOwned?data.CellHandle:0);return true;
    }
    // Frame decision after native pass publication; group visibility uses the
    // same managed occlusion owners as the traversal and the native pass.
    unsafe void PublishCells()
    {
        if(!cellsActive||ownedCells==0)return;
        using var profile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.CellPublish);
        var frame=new NativeCellFrame{Flags=(NewActorViewsEnabled?1u:0u)|(StockFarCascadesEnabled?2u:0u)|(actorLodSettings.Global.EnableLodSelection?4u:0u)|(actorLodSettings.Global.IsUpdateEnabled?8u:0u)};
        var camera=FrameView.CameraPosition;frame.Camera[0]=camera.X;frame.Camera[1]=camera.Y;frame.Camera[2]=camera.Z;
        for(int i=0;i<6;i++){var plane=frustum[i];frame.MainPlanes[i*4]=plane.Normal.X;frame.MainPlanes[i*4+1]=plane.Normal.Y;frame.MainPlanes[i*4+2]=plane.Normal.Z;frame.MainPlanes[i*4+3]=plane.D;}
        frame.ProbeDistance=ProbeDrawDistance;frame.ShadowDistance=ShadowDrawDistance;ShadowSystem.WriteCellCascades(&frame.C0);
        var global=actorLodSettings.Global;frame.Coefficient=lodFov>0?(float)(Math.Tan(lodFov*.5)/Math.Tan(70*Math.PI/360))*1080/Math.Max(1,backend.PresentationHeight):1;frame.Delta=FrameView.LastFrameDelta;
        frame.MaximumSeconds=global.MaxTransitionInSeconds;frame.DistanceAdd=global.ObjectDistanceAdd;frame.DistanceMult=global.ObjectDistanceMult;frame.Selection=global.LodSelection;frame.MinimumLod=actorLodSettings.GBuffer.MinLod;frame.Shift=actorLodSettings.GBuffer.LodShift;
        backend.PrepareCells(ref frame);bool pending=true;
        try{
            var prepared=backend.GetCellPreparation();
            for(int i=0;i<prepared.VisibleCount;i++)visible.Add(prepared.Visible[i]);
            for(int i=0;i<prepared.ChangeCount;i++){var change=prepared.Changes[i];var centre=new Vector3D(change.X,change.Y,change.Z);InvalidateOcclusion("ActorLOD");MarkSpots(centre,(float)change.Radius,false);MarkSpots(centre,(float)change.Radius,true);}
            if(cellResults.Length<prepared.QueryCount)cellResults=new NativeCellResult[prepared.QueryCount];
            for(int i=0;i<prepared.QueryCount;i++){var query=prepared.Queries[i];uint hidden=0,cascades=0;
                if(actors.TryGetValue(query.Representative,out var actor)){if(query.Main!=0&&QueryGroupVisibility(query.Representative))hidden=1;cascades=query.Cascades&~FilterShadowGroupCascades(actor,query.Cascades);}
                cellResults[i]=new NativeCellResult{Group=query.Group,MainHidden=hidden,CascadesHidden=cascades};}
            backend.PublishCells(cellResults,prepared.QueryCount);pending=false;
        }finally{if(pending)backend.CancelCells();}
    }
    NativeCellResult[] cellResults=Array.Empty<NativeCellResult>();
}
