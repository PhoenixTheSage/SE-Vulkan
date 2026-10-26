using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ClientPlugin.Diagnostics;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Voxels;

namespace ClientPlugin.Rendering.Scene;
/// <summary>Owner-thread clipmap bridge. Game workers mesh; Vulkan owns copied geometry.</summary>
public sealed partial class VulkanVoxels : IDisposable
{
    private readonly NativeBackend backend;
    private readonly Dictionary<uint,Actor> actors=new Dictionary<uint,Actor>();
    private readonly VulkanVoxelProcessorProvider provider=new VulkanVoxelProcessorProvider();
    private ulong nextMesh=0x6000000000000000;
    private int thread;
    public VulkanVoxelMaterials Materials {get;}
    public VulkanFoliage Foliage {get;}
    public int ActorCount=>actors.Count;
    public int PlanetCount=>actors.Values.Count(a=>a.SpherizeRadius.HasValue);
    public int CellCount=>actors.Values.Sum(a=>a.Cells.Count);
    public int VisibleCells=>actors.Values.Sum(a=>a.Cells.Count(c=>c.Visible));
    public long MeshUpdates {get;private set;}
    public long Draws {get;private set;}
    public long LodChanges {get;private set;}
    public string LodStatus=>string.Join(",",actors.Values.SelectMany(a=>a.Cells).Where(c=>c.Visible).GroupBy(c=>c.Lod).OrderBy(g=>g.Key).Select(g=>g.Key+":"+g.Count()));
    public VulkanVoxels(NativeBackend backend,VulkanTextureCache textures=null){this.backend=backend;var cache=textures??new VulkanTextureCache(backend);Materials=new VulkanVoxelMaterials(backend,cache);Foliage=new VulkanFoliage(backend,cache,Materials);}
    private void RequireOwner(){int current=Thread.CurrentThread.ManagedThreadId;if(thread==0)thread=current;if(thread!=current)throw new InvalidOperationException("Voxel scene callback must run on the render owner thread.");}
    public bool Process(MyRenderMessageBase message)
    {
        RequireOwner();
        switch(message.MessageType)
        {
            case MyRenderMessageEnum.VoxelCreate:
                var create=(MyRenderMessageVoxelCreate)message;
                if(actors.ContainsKey(create.Id))throw new InvalidOperationException("Duplicate voxel actor id.");
                if(create.SpherizeRadius.HasValue && (!create.SpherizePosition.IsValid() || (float.IsNaN(create.SpherizeRadius.Value) || float.IsInfinity(create.SpherizeRadius.Value)) || create.SpherizeRadius.Value<=0))throw new InvalidOperationException("Invalid planet spherization parameters.");
                var actor=new Actor(this,create.Id,create.Size,create.WorldMatrix,create.RenderFlags,create.Dithering,create.Clipmap){SpherizeRadius=create.SpherizeRadius,SpherizeCenter=create.SpherizePosition};actors.Add(create.Id,actor);
                try{create.Clipmap.VoxelRenderDataProcessorProvider=provider;create.Clipmap.BindToActor(actor);actor.SetMatrix(create.WorldMatrix);}catch{actors.Remove(create.Id);actor.Dispose();throw;}return true;
            case MyRenderMessageEnum.CreateRenderVoxelMaterials:Materials.Update(((MyRenderMessageCreateRenderVoxelMaterials)message).Materials,true);return true;
            case MyRenderMessageEnum.UpdateRenderVoxelMaterials:Materials.Update(((MyRenderMessageUpdateRenderVoxelMaterials)message).Materials,false);return true;
            case MyRenderMessageEnum.PreloadVoxelMaterials:Materials.Preload(((MyRenderMessagePreloadVoxelMaterials)message).Materials);return true;
            case MyRenderMessageEnum.RemoveRenderObject:
                uint id=((MyRenderMessageRemoveRenderObject)message).ID;if(!actors.TryGetValue(id,out var removed))return false;removed.Dispose();actors.Remove(id);return true;
            case MyRenderMessageEnum.UpdateRenderObject:
                var update=(MyRenderMessageUpdateRenderObject)message;if(!actors.TryGetValue(update.ID,out var moved))return false;
                try{if(update.Data.HasWorldMatrix)moved.SetMatrix(update.Data.WorldMatrix);}finally{update.Data.Clean();}return true;
            case MyRenderMessageEnum.UpdateRenderEntity:
                var appearance=(MyRenderMessageUpdateRenderEntity)message;if(!actors.TryGetValue(appearance.ID,out var changed))return false;if(appearance.Dithering.HasValue)changed.Dither=appearance.Dithering.Value;return true;
        }
        return false;
    }
    public void Update(MatrixD cameraWorld,BoundingFrustumD frustum,float far,bool smooth)
    {
        RequireOwner();foreach(var actor in actors.Values.ToArray())
        {actor.Clipmap.DoVisibilityUpdates=true;if(MyClipmap.EnableUpdate)actor.Clipmap.Update(ref cameraWorld,frustum,far,smooth);}
    }
    public float ProbeDrawDistance,ShadowDrawDistance;
    internal System.Func<BoundingBoxD, Vector3D, uint> CascadeMaskFor;
    // Single cell pass plus memoized material preparation; the previous two-pass
    // path stays for same-process parity lanes (VulkanScene.VoxelFastPathEnabled).
    public static bool FastPathEnabled=System.Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_FAST_PATH")!="0";
    // Plan-ahead (SE_VULKAN_VOXEL_PLAN_AHEAD=0, or VulkanScene.VoxelPlanAheadEnabled, turns it off):
    // BeginPlan runs the cell cull and every surviving part's object constants and cascade mask on a
    // worker while the actor traversal runs. Draw joins it and does the render-thread work at the
    // same point and in the same order as before: foliage retirement, materials, recipes, packets,
    // foliage and visibility. Everything the plan reads is fixed before the shadow fit returns.
    public static bool PlanAheadEnabled=System.Environment.GetEnvironmentVariable("SE_VULKAN_VOXEL_PLAN_AHEAD")!="0";
    RenderJobs.Job? planTask;Vector3D planCamera;
    public void BeginPlan(Vector3D camera,BoundingFrustumD frustum)
    {
        RequireOwner();DropPlan();
        if(!FastPathEnabled||!PlanAheadEnabled||!backend.WorldLayouts.TryGetValue(20,out var single)||!backend.WorldLayouts.TryGetValue(21,out var triple))return;
        // First use of a layout fills shared reflection caches; do it on this thread.
        VoxelObjectConstants.Warm(single);VoxelObjectConstants.Warm(triple);
        planCamera=camera;planTask=RenderJobs.Start(()=>Plan(camera,frustum,false));
    }
    void DropPlan(){var task=planTask;planTask=null;if(task==null)return;task.Value.Join();foreach(var item in cellScratch)RecycleConstants(item);}
    void RecycleConstants(CellDraw item){for(int i=0;i<item.PartCount;i++)ReflectedConstantBuffer.Recycle(partConstants[item.PartStart+i]);}
    public void Draw(Vector3D camera,BoundingFrustumD frustum,HashSet<uint> visible)
    {
        RequireOwner();Materials.BeginFrame();Foliage.BeginFrame();
        if(planTask!=null&&planCamera==camera){var task=planTask.Value;planTask=null;task.Wait();Emit(camera,visible);return;}
        DropPlan();
        if(!FastPathEnabled){DrawTwoPass(camera,frustum,visible);return;}
        Plan(camera,frustum,true);Emit(camera,visible);
    }
    // One cell pass: the material set and the emitted packets both use the
    // same transform, bounds and cull decision, so survivors are recorded once
    // and emitted after the materials are prepared, in the same order.
    // Touches no shared mutable state: it may run on a worker.
    // The profiler's scopes belong to the render thread; a worker plan is not scoped.
    void Plan(Vector3D camera,BoundingFrustumD frustum,bool profile)
    {
        frameMaterials.Clear();cellScratch.Clear();retiredParts.Clear();partConstants.Clear();
        var cullProfile=profile?RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.VoxelCull,false):default;
        foreach(var actor in actors.Values)
        {
            if((actor.Flags&(RenderFlags.Visible|RenderFlags.SkipInMainView))!=RenderFlags.Visible)continue;
            bool casts=(actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
            foreach(var cell in actor.Cells)
            {
                if(!cell.Visible)continue;
                MatrixD matrix=MatrixD.CreateScale(1<<cell.Lod)*MatrixD.CreateTranslation(cell.Offset)*actor.Matrix;
                var bounds=new BoundingBoxD(cell.Bounds.Min,cell.Bounds.Max).TransformFast(matrix);
                double distance=bounds.DistanceSquared(ref camera);
                bool outside=frustum!=null&&frustum.Contains(bounds)==ContainmentType.Disjoint;bool outsideProbe=distance>ProbeDrawDistance*ProbeDrawDistance;
                bool culled=outside && outsideProbe && ((actor.Flags&RenderFlags.CastShadows)==0 || distance>ShadowDrawDistance*ShadowDrawDistance);
                if(!culled)foreach(var part in cell.Parts)frameMaterials.Add(part.Material);
                if(cell.Parts.Count==0)continue;
                if(distance>Foliage.Distance*Foliage.Distance*2.25)foreach(var p in cell.Parts)retiredParts.Add(p);
                if(culled)continue;
                MatrixD relative=matrix;relative.Translation-=camera;
                var draw=new CellDraw{Actor=actor,Cell=cell,Matrix=relative,Distance=distance,Outside=outside,OutsideProbe=outsideProbe,Casts=casts,
                    Origin=(Vector3)(bounds.Center-camera),Radius=(float)bounds.HalfExtents.Length(),Cascades=casts?(CascadeMaskFor?.Invoke(bounds,camera)??0xFFu):0u,PartStart=partConstants.Count,PartCount=cell.Parts.Count};
                foreach(var part in cell.Parts)partConstants.Add(VulkanVoxelMaterials.Object(backend.WorldLayouts[part.Material.SingleMaterial?20u:21u],relative,cell.Offset,cell.Lod,actor.Dither,actor.SpherizeRadius,actor.SpherizeCenter-camera));
                cellScratch.Add(draw);
            }
        }
        cullProfile.Dispose();
    }
    void Emit(Vector3D camera,HashSet<uint> visible)
    {
        foreach(var p in retiredParts){Foliage.Retire(p.Foliage);p.Foliage=null;}
        using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.VoxelMaterials,false))Materials.Prepare(frameMaterials);
        using var emitProfile=RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.VoxelEmit,false);
        Actor seenActor=null;bool seen=false;
        foreach(var item in cellScratch)
        {
            var actor=item.Actor;var cell=item.Cell;MatrixD matrix=item.Matrix;double distance=item.Distance;bool outside=item.Outside,outsideProbe=item.OutsideProbe;
            if(!ReferenceEquals(actor,seenActor)){if(seen)visible.Add(seenActor.Id);seenActor=actor;seen=false;}
            for(int i=0;i<item.PartCount;i++)
            {
                var part=cell.Parts[i];var objectConstants=partConstants[item.PartStart+i];
                uint pipeline=part.Material.SingleMaterial?20u:21u;ulong material=Materials.Get(part.Material);
                if(backend.WorldRecipesEnabled)backend.EnsureWorldRecipe(ref part.Recipe,pipeline,part.Mesh,material,0,0,0,objectConstants.Length);
                backend.AddWorldPacket(pipeline,part.Mesh,material,0,0,0,objectConstants,null,1,(item.Casts?0u:512u)|(outside?4096u:0u)|(outsideProbe?16384u:0u),item.Cascades,item.Origin,item.Radius,recipe:part.Recipe);Draws++;
                if(!outside && distance<Foliage.Distance*Foliage.Distance && Foliage.Density>0){if(part.Foliage==null || part.FoliageVersion!=Materials.Version || part.Density!=Foliage.Density){Foliage.Retire(part.Foliage);part.Foliage=Foliage.Generate(part.Vertices,part.Indices,part.Material,cell.Lod,actor.Matrix);part.FoliageVersion=Materials.Version;part.Density=Foliage.Density;}Foliage.Draw(part.Foliage,MatrixD.CreateScale(1.0/(1<<cell.Lod))*matrix,actor.Dither);}
            }
            seen|=!outside;
        }
        if(seen)visible.Add(seenActor.Id);
        cellScratch.Clear();partConstants.Clear();
        Foliage.EndFrame();
    }
    void DrawTwoPass(Vector3D camera,BoundingFrustumD frustum,HashSet<uint> visible)
    {
        var frameMaterials=new List<MyVoxelMaterialTriple>();foreach(var actor in actors.Values){if((actor.Flags&(RenderFlags.Visible|RenderFlags.SkipInMainView))!=RenderFlags.Visible)continue;foreach(var cell in actor.Cells){if(!cell.Visible)continue;MatrixD transform=MatrixD.CreateScale(1<<cell.Lod)*MatrixD.CreateTranslation(cell.Offset)*actor.Matrix;var bounds=new BoundingBoxD(cell.Bounds.Min,cell.Bounds.Max).TransformFast(transform);if(frustum!=null && frustum.Contains(bounds)==ContainmentType.Disjoint && bounds.DistanceSquared(ref camera)>Math.Pow(Math.Max(ProbeDrawDistance,(actor.Flags&RenderFlags.CastShadows)!=0?ShadowDrawDistance:0),2))continue;foreach(var part in cell.Parts)frameMaterials.Add(part.Material);}}Materials.Prepare(frameMaterials);
        foreach(var actor in actors.Values)
        {
            if((actor.Flags&(RenderFlags.Visible|RenderFlags.SkipInMainView))!=RenderFlags.Visible)continue;bool seen=false;
            foreach(var cell in actor.Cells)
            {
                if(!cell.Visible||cell.Parts.Count==0)continue;
                MatrixD matrix=MatrixD.CreateScale(1<<cell.Lod)*MatrixD.CreateTranslation(cell.Offset)*actor.Matrix;
                var bounds=new BoundingBoxD(cell.Bounds.Min,cell.Bounds.Max).TransformFast(matrix);
                double distance=bounds.DistanceSquared(ref camera);if(distance>Foliage.Distance*Foliage.Distance*2.25)foreach(var p in cell.Parts){Foliage.Retire(p.Foliage);p.Foliage=null;}
                bool outside=frustum!=null&&frustum.Contains(bounds)==ContainmentType.Disjoint;bool outsideProbe=distance>ProbeDrawDistance*ProbeDrawDistance;
                if(outside && outsideProbe && ((actor.Flags&RenderFlags.CastShadows)==0 || distance>ShadowDrawDistance*ShadowDrawDistance))continue;
                matrix.Translation-=camera;
                bool casts=(actor.Flags&(RenderFlags.CastShadows|RenderFlags.SkipInDepth))==RenderFlags.CastShadows;
                Vector3 origin=(Vector3)(bounds.Center-camera);float radius=(float)bounds.HalfExtents.Length();
                uint cascades=casts?(CascadeMaskFor?.Invoke(bounds,camera)??0xFFu):0u;
                foreach(var part in cell.Parts)
                {
                    uint pipeline=part.Material.SingleMaterial?20u:21u;ulong material=Materials.Get(part.Material);
                    var objectConstants=VulkanVoxelMaterials.Object(backend.WorldLayouts[pipeline],matrix,cell.Offset,cell.Lod,actor.Dither,actor.SpherizeRadius,actor.SpherizeCenter-camera);
                    if(backend.WorldRecipesEnabled)backend.EnsureWorldRecipe(ref part.Recipe,pipeline,part.Mesh,material,0,0,0,objectConstants.Length);
                    backend.AddWorldPacket(pipeline,part.Mesh,material,0,0,0,objectConstants,null,1,(casts?0u:512u)|(outside?4096u:0u)|(outsideProbe?16384u:0u),cascades,origin,radius,recipe:part.Recipe);Draws++;
                    if(!outside && distance<Foliage.Distance*Foliage.Distance && Foliage.Density>0){if(part.Foliage==null || part.FoliageVersion!=Materials.Version || part.Density!=Foliage.Density){Foliage.Retire(part.Foliage);part.Foliage=Foliage.Generate(part.Vertices,part.Indices,part.Material,cell.Lod,actor.Matrix);part.FoliageVersion=Materials.Version;part.Density=Foliage.Density;}Foliage.Draw(part.Foliage,MatrixD.CreateScale(1.0/(1<<cell.Lod))*matrix,actor.Dither);}
                }
                seen|=!outside;
            }
            if(seen)visible.Add(actor.Id);
        }Foliage.EndFrame();
    }
    struct CellDraw {public Actor Actor;public Cell Cell;public MatrixD Matrix;public double Distance;public bool Outside,OutsideProbe,Casts;public Vector3 Origin;public float Radius;public uint Cascades;public int PartStart,PartCount;}
    readonly List<CellDraw> cellScratch=new();readonly List<MyVoxelMaterialTriple> frameMaterials=new();readonly List<Part> retiredParts=new();readonly List<byte[]> partConstants=new();
    public void Clear(){RequireOwner();foreach(var actor in actors.Values.ToArray())actor.Dispose();actors.Clear();Foliage.Clear();Materials.ClearResident();}
    public void Dispose(){Clear();Foliage.Dispose();Materials.Dispose();}
    private sealed class Part {public NativeBackend.WorldRecipe Recipe;public ulong Mesh;public MyVoxelMaterialTriple Material;public MyVertexFormatVoxelSingleData[] Vertices;public uint[] Indices;public List<VulkanFoliage.Stream> Foliage;public int FoliageVersion;public float Density;}
    private sealed class Actor : IMyVoxelActor,IDisposable
    {
        internal readonly VulkanVoxels Scene;internal readonly IMyLodController Clipmap;
        internal readonly HashSet<Cell> Cells=new HashSet<Cell>();private readonly List<Action> pending=new List<Action>();
        internal MatrixD Matrix;internal readonly RenderFlags Flags;internal float Dither;internal bool Dead;
        internal float? SpherizeRadius;internal Vector3D SpherizeCenter;
        public uint Id {get;}
        public Vector3I Size {get;set;}
        private MyVoxelActorTransitionMode mode=MyVoxelActorTransitionMode.Immediate;
        public MyVoxelActorTransitionMode TransitionMode {get=>mode;set{if(IsBatching)throw new InvalidOperationException("Cannot change voxel transition mode during a batch.");mode=value;}}
        public bool IsBatching {get;private set;}
        public event VisibilityChange CellChange;
        public event ActionRef<MatrixD> Move;
        public Actor(VulkanVoxels scene,uint id,Vector3I size,MatrixD matrix,RenderFlags flags,float dither,IMyLodController clipmap){Scene=scene;Id=id;Size=size;Matrix=matrix;Flags=flags;Dither=dither;Clipmap=clipmap??throw new ArgumentNullException(nameof(clipmap));}
        public void SetMatrix(MatrixD matrix){if(Matrix.M11!=matrix.M11 || Matrix.M12!=matrix.M12 || Matrix.M13!=matrix.M13 || Matrix.M21!=matrix.M21 || Matrix.M22!=matrix.M22 || Matrix.M23!=matrix.M23 || Matrix.M31!=matrix.M31 || Matrix.M32!=matrix.M32 || Matrix.M33!=matrix.M33)foreach(var cell in Cells)foreach(var part in cell.Parts){Scene.Foliage.Retire(part.Foliage);part.Foliage=null;}Matrix=matrix;Move?.Invoke(ref Matrix);}
        internal void Change(Action action){Scene.RequireOwner();if(Dead)return;if(IsBatching)pending.Add(action);else action();}
        internal void Notify(Cell cell,bool visible){CellChange?.Invoke(cell.Offset,cell.Lod,visible);Scene.LodChanges++;}
        public IMyVoxelActorCell CreateCell(Vector3D offset,int lod,bool notify=false)
        {Scene.RequireOwner();if(Dead||lod<0||lod>=16)throw new InvalidOperationException("Invalid voxel cell creation.");var cell=new Cell(this,offset,lod);Cells.Add(cell);if(notify)Change(()=>Notify(cell,true));return cell;}
        public void DeleteCell(IMyVoxelActorCell value,bool notify=false)
        {if(!(value is Cell cell)||cell.Actor!=this)throw new ArgumentException("Foreign voxel cell.");Change(()=>{cell.Dispose();Cells.Remove(cell);if(notify)Notify(cell,false);});}
        public void BeginBatch(MyVoxelActorTransitionMode? switchMode=null)
        {Scene.RequireOwner();if(IsBatching)throw new InvalidOperationException("Nested voxel batch.");if(switchMode.HasValue)TransitionMode=switchMode.Value;IsBatching=true;}
        public void EndBatch(bool justLoaded)
        {Scene.RequireOwner();if(!IsBatching)throw new InvalidOperationException("No open voxel batch.");IsBatching=false;var changes=pending.ToArray();pending.Clear();foreach(var action in changes)action();}
        public void Dispose(){if(Dead)return;Scene.RequireOwner();Dead=true;Clipmap.Unload();pending.Clear();foreach(var cell in Cells)cell.Dispose();Cells.Clear();}
    }
    private sealed class Batch : IMyVoxelUpdateBatch
    {
        public readonly List<Action> Updates=new List<Action>();private bool committed;
        public void Commit(bool allowEmpty=false){if(committed)throw new InvalidOperationException("Voxel upload batch already committed.");committed=true;foreach(var update in Updates)update();Updates.Clear();}
    }
    private sealed class Cell : IMyVoxelActorCell,IDisposable
    {
        public readonly Actor Actor;public readonly List<Part> Parts=new List<Part>();internal BoundingBox Bounds;
        public Vector3D Offset {get;}
        public int Lod {get;}
        public bool Visible {get;private set;}
        private bool dead;
        public Cell(Actor actor,Vector3D offset,int lod){Actor=actor;Offset=offset;Lod=lod;}
        public void SetVisible(bool visible,bool notify=true){Actor.Change(()=>{if(dead)return;bool changed=Visible!=visible;Visible=visible;if(notify&&changed)Actor.Notify(this,visible);});}
        public unsafe void UpdateMesh(ref MyVoxelRenderCellData data,ref IMyVoxelUpdateBatch updateBatch)
        {
            Actor.Scene.RequireOwner();MyVertexFormatVoxelSingleData[] vertices;uint[] indices;MyVoxelMeshPartIndex[] parts;BoundingBox bounds=data.CellBounds;
            try
            {
                if(data.VertexCount<0||data.IndexCount<0||data.Vertices==null||data.Indices==null||data.Vertices.Size<data.VertexCount*24L||data.Indices.Size<data.IndexCount*(data.ShortIndices?2L:4L))throw new InvalidOperationException("Invalid copied voxel render data.");
                vertices=new MyVertexFormatVoxelSingleData[data.VertexCount];indices=new uint[data.IndexCount];parts=(MyVoxelMeshPartIndex[])data.Parts.Clone();
                fixed(MyVertexFormatVoxelSingleData* p=vertices)Buffer.MemoryCopy((void*)data.Vertices.Ptr,p,vertices.LongLength*24,vertices.LongLength*24);
                if(data.ShortIndices){var source=(ushort*)data.Indices.Ptr;for(int i=0;i<indices.Length;++i)indices[i]=source[i];}else{fixed(uint* p=indices)Buffer.MemoryCopy((void*)data.Indices.Ptr,p,indices.LongLength*4,indices.LongLength*4);}
            }
            finally{data.Dispose();}
            if(updateBatch==null)updateBatch=new Batch();if(!(updateBatch is Batch batch))throw new InvalidOperationException("Foreign voxel upload batch.");
            batch.Updates.Add(()=>Actor.Change(()=>Apply(vertices,indices,parts,bounds)));
        }
        private void Apply(MyVertexFormatVoxelSingleData[] vertices,uint[] indices,MyVoxelMeshPartIndex[] parts,BoundingBox bounds)
        {
            if(dead||Actor.Dead)return;var uploaded=new List<Part>();
            try
            {
                foreach(var part in parts)
                {
                    if(part.StartIndex<0||part.IndexCount<0||part.IndexCount%3!=0||part.StartIndex>indices.Length-part.IndexCount)throw new InvalidOperationException("Invalid voxel material index range.");if(part.IndexCount==0)continue;
                    var map=new Dictionary<uint,uint>();var packed=new List<MyVertexFormatVoxelSingleData>();var own=new uint[part.IndexCount];
                    for(int i=0;i<own.Length;++i){uint original=indices[part.StartIndex+i];if(original>=vertices.Length)throw new InvalidOperationException("Voxel index out of bounds.");if(!map.TryGetValue(original,out uint mapped)){mapped=(uint)packed.Count;map.Add(original,mapped);packed.Add(vertices[original]);}own[i]=mapped;}
                    var native=new Part{Mesh=Actor.Scene.nextMesh++,Material=part.Materials,Vertices=packed.ToArray(),Indices=own};Actor.Scene.backend.UploadVoxelMesh(native.Mesh,native.Vertices,own);uploaded.Add(native);
                }
                foreach(var old in Parts){Actor.Scene.Foliage.Retire(old.Foliage);Actor.Scene.backend.RemoveResource(3,old.Mesh);}Parts.Clear();Parts.AddRange(uploaded);Bounds=bounds;Actor.Scene.MeshUpdates++;
            }
            catch{foreach(var part in uploaded)Actor.Scene.backend.RemoveResource(3,part.Mesh);throw;}
        }
        public void Dispose(){if(dead)return;dead=true;foreach(var part in Parts){Actor.Scene.Foliage.Retire(part.Foliage);Actor.Scene.backend.RemoveResource(3,part.Mesh);}Parts.Clear();Visible=false;}
    }
}
