using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRage.Library.Collections;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Voxels;

class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};return Run(bin);}
    [MethodImpl(MethodImplOptions.NoInlining)]
    static unsafe int Run(string bin)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        string output=Path.GetFullPath("TestResults/VoxelGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        backend.WorldRecipesEnabled=true;
        Check(backend.WorldLayouts.Count==54,"40 world, ten OLD cutouts and four stock voxel/depth pipelines initialize");Check(Marshal.SizeOf<MyVertexFormatVoxelSingleData>()==24,"stock voxel vertex packing");
        var constantRandom=new Random(26417);bool exactConstants=true;
        foreach(uint pipeline in new[]{20u,21u})for(int sample=0;sample<512;++sample)
        {
            double Next()=>constantRandom.NextDouble()*200000-100000;
            MatrixD transform=MatrixD.CreateScale(Next(),Next(),Next())*MatrixD.CreateFromYawPitchRoll(Next(),Next(),Next())*MatrixD.CreateTranslation(Next(),Next(),Next());
            var offset=new Vector3D(Next(),Next(),Next());var sphereCenter=new Vector3D(Next(),Next(),Next());int lod=sample%16;float dither=(float)Next();float? radius=sample%2==0?null:(float)Math.Abs(Next());
            var reference=VulkanVoxelMaterials.ObjectReflected(backend.WorldLayouts[pipeline],transform,offset,lod,dither,radius,sphereCenter);
            var direct=VoxelObjectConstants.Write(backend.WorldLayouts[pipeline],transform,offset,lod,dither,radius,sphereCenter);
            exactConstants &= reference.SequenceEqual(direct);
        }
        Check(exactConstants,"1024 varied single/multi voxel object blocks match reflected bytes including padding");
        var invalidLayout=JsonSerializer.Deserialize<ShaderMetadata>(JsonSerializer.Serialize(backend.WorldLayouts[20]));
        var invalidField=invalidLayout.Descriptors.Single(d=>d.Name=="Object").Members.Single(m=>m.Name=="object_").Members.Single(m=>m.Name=="voxel_scale");invalidField.Type="int32";
        bool invalidConstants=false;try{VoxelObjectConstants.Write(invalidLayout,MatrixD.Identity,Vector3D.Zero,0,0,null,Vector3D.Zero);}catch(InvalidDataException){invalidConstants=true;}
        Check(invalidConstants,"specialized voxel constants reject incompatible reflected field types");
        MyVertexFormatVoxelSingleData V(float x,float y,int weight=0)=>new(){Position=new Vector3(x,y,-3),Material=new Byte4{PackedValue=(uint)(0|1<<8|2<<16|weight<<24)},PackedNormal=new Byte4{PackedValue=VF_Packer.PackNormal(Vector3.Backward)}};
        var vertices=new[]{V(-1,-1),V(1,-1,1),V(0,1,2)};uint[] indices={0,1,2};byte[] constants=new byte[30720];
        void Floats(int at,params float[] f){foreach(float v in f){Buffer.BlockCopy(BitConverter.GetBytes(v),0,constants,at,4);at+=4;}}
        void Int(int at,int v)=>Buffer.BlockCopy(BitConverter.GetBytes(v),0,constants,at,4);
        for(int material=0;material<3;++material)
        {
            int at=material*240;for(int t=0;t<2;++t)Floats(at+t*64,1,100,2,2,.1f,1000,1,1,.01f,5000,2,0,.001f,10000,3,1);Int(at+140,1);
            for(int level=0;level<3;++level)for(int role=0;role<6;++role)Int(at+144+32*level+role*4,material*6+level*2+(role%2));
        }
        byte[] cm=new byte[72],ng=new byte[72],ext=new byte[72];for(int i=0;i<18;++i){cm[i*4+i/6]=220;ng[i*4]=128;ng[i*4+1]=128;ng[i*4+2]=255;ng[i*4+3]=50;ext[i*4]=255;}
        backend.BeginUploads();backend.UploadVoxelArray(100,1,1,0,true,18,1,cm);backend.UploadVoxelArray(101,1,1,0,false,18,1,ng);backend.UploadVoxelArray(102,1,1,0,false,18,1,ext);backend.SetVoxelMaterial(103,constants,new ulong[]{100,101,102});backend.UploadVoxelMesh(104,vertices,indices);backend.EndUploads();
        Check(backend.GetWorkStats().UploadBatches==1,"voxel geometry and three material arrays share one upload batch");
        Check(backend.GetVoxelStats().Meshes==1,"voxel mesh counters use the vertex contract rather than a handle namespace");
        bool invalidMips=false;try{backend.UploadVoxelArray(199,1,1,0,false,1,2,new byte[8]);}catch(InvalidOperationException){invalidMips=true;}Check(invalidMips,"invalid mip count is rejected before Vulkan allocation");
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        byte[] Render(uint pipeline=20,MatrixD? world=null,int lod=0,float dither=0,bool shadows=false,float? spherize=null,Vector3D spherizeCenter=default)
        {
            backend.BeginPacketCollection();backend.AddWorldPacket(pipeline,104,103,0,0,0,VulkanVoxelMaterials.Object(backend.WorldLayouts[pipeline],world??MatrixD.Identity,Vector3D.Zero,lod,dither,spherize,spherizeCenter),null,1,shadows?0u:512u);
            if(shadows){var matrices=new List<float>();for(int c=0;c<4;++c)matrices.AddRange(NativeBackend.MatrixValues(MatrixD.CreateOrthographic(6,6,.1,20)));backend.SetShadowCascades(matrices.ToArray(),new[]{5f,10f,15f,20f});}else backend.SetShadowCascades(Array.Empty<float>(),Array.Empty<float>());
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"voxel frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);
        }
        int center=(32*64+32)*4;byte[] single=Render();VoxelObjectConstants.Enabled=false;byte[] reflectedPixels=Render();VoxelObjectConstants.Enabled=true;Check(single.SequenceEqual(reflectedPixels),"cached and reflected voxel object writers produce exact GPU pixels");Check(single[center]>single[center+1]+25,"stock single triplanar material produces red pixels");
        byte[] multi=Render(21);Check(multi[center+1]>single[center+1]+20&&multi[center+2]>single[center+2]+20,"stock three-material interpolation blends independent layers");
        backend.BeginUploads();backend.UploadTexture(110,1,1,0,new byte[]{220,0,0,0},true);backend.UploadTexture(111,2,2,0,Enumerable.Range(0,4).SelectMany(_=>new byte[]{0,220,0,0}).ToArray(),true);backend.UploadTexture(112,4,4,2,new byte[]{31,0,31,0,0,0,0,0},true);
        backend.BuildVoxelArray(100,8,true,Enumerable.Range(0,18).Select(i=>i<6?110UL:i<12?111UL:112UL).ToArray());backend.EndUploads();
        byte[] normalized=Render();Check(Math.Abs(normalized[center]-single[center])<5,"GPU normalization preserves sRGB color and zero-alpha material channels");
        byte[] normalizedBlend=Render(21);Check(normalizedBlend[center+1]>20&&normalizedBlend[center+2]>20,"mixed DDS sizes, BC1/RGBA formats and missing mip chains normalize with generated mips");
        byte[] shadow=Render(21,shadows:true);Check(shadow.Any(v=>v>0),"single/multi depth partners replay into cascades");
        byte[] faded=Render(dither:.5f);Check(single.Where((v,i)=>i%4!=3&&v!=faded[i]).Count()>100,"voxel construction/transition dithering changes coverage");
        byte[] scaled=Render(world:MatrixD.CreateScale(2)*MatrixD.CreateTranslation(0,0,3),lod:1);Check(scaled.Where((v,i)=>v!=single[i]).Count()>100,"LOD scale and object constants move voxel pixels");
        backend.BeginUploads();backend.UploadVoxelMesh(104,new[]{V(-.3f,-.3f),V(.3f,-.3f),V(0,.3f)},indices);backend.EndUploads();byte[] mined=Render();Check(mined.Where((v,i)=>v!=single[i]).Count()>100,"replacement cell geometry removes mined surface pixels");
        for(int material=0;material<3;++material)Floats(material*240+128,1,0,0);backend.SetVoxelMaterial(103,constants,new ulong[]{100,101,102});backend.BeginUploads();backend.UploadVoxelMesh(104,new[]{V(-4,-4),V(4,-4),V(0,4)},indices);backend.EndUploads();var previousProjection=projection;projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,1000000);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;MatrixD farObject=MatrixD.CreateScale(5000)*MatrixD.CreateTranslation(0,0,-40000);var flat=Render(world:farObject);var sphere=Render(world:farObject,spherize:2000,spherizeCenter:new Vector3D(0,0,-50000));Check(flat.Where((v,i)=>v!=sphere[i]).Count()>5,"stock distant planet spherization changes rasterized geometry");projection=previousProjection;
        var fake=new Clipmap();using var scene=new VulkanVoxels(backend);Check(scene.Process(new MyRenderMessageVoxelCreate{Id=5,Clipmap=fake,WorldMatrix=MatrixD.Identity,Size=new Vector3I(32),RenderFlags=RenderFlags.Visible}),"voxel actor binds game LOD controller");
        scene.Materials.Update(new[]{new MyRenderVoxelMaterialData{TextureSets=new MyRenderVoxelMaterialData.TextureSet[3]}},true);backend.BeginUploads();ulong fallback=scene.Materials.Get(new MyVoxelMaterialTriple(0,-1,-1));backend.EndUploads();Check(fallback!=0&&scene.Materials.ResidentGroups==1,"intentional empty voxel texture roles use exact stock generated patterns");scene.Materials.ClearResident();Check(scene.Materials.ResidentBytes==0,"voxel atlas and generated pattern residency clears");
        var definitions=Enumerable.Range(0,22).Select(_=>new MyRenderVoxelMaterialData{TextureSets=new MyRenderVoxelMaterialData.TextureSet[3],Foliage=new MyRenderFoliageData{Density=8,Entries=new[]{new MyRenderFoliageData.FoliageEntry{Size=new Vector2(.5f,1)}}}}).ToArray();scene.Materials.Update(definitions,true);var callerFoliage=definitions[0].Foliage.Value;callerFoliage.Entries[0].Size=Vector2.Zero;scene.Materials.TryGetFoliage(0,out var ownedFoliage);Check(ownedFoliage.Entries[0].Size==new Vector2(.5f,1),"foliage entries are copied before pooled caller reuse");scene.Materials.BeginFrame();backend.BeginUploads();scene.Materials.Prepare(Enumerable.Range(0,22).Select(i=>new MyVoxelMaterialTriple(i,-1,-1)));backend.EndUploads();Check(scene.Materials.Get(new MyVoxelMaterialTriple(0,-1,-1))==scene.Materials.Get(new MyVoxelMaterialTriple(21,-1,-1))&&scene.Materials.ResidentGroups==1,"visible material subsets share a single native atlas group");Check(scene.Materials.AtlasStatus=="256px:1"&&scene.Materials.ResidentBytes<=512L*1024*1024,"large material pages select a bounded resolution tier");scene.Materials.ClearResident();
        scene.Materials.BeginFrame();backend.BeginUploads();scene.Materials.Prepare(Enumerable.Range(0,22).Select(i=>new MyVoxelMaterialTriple(i,-1,-1)));backend.EndUploads();
        var lookupTriple=new MyVoxelMaterialTriple(0,-1,-1);ulong cachedGroup=scene.Materials.Get(lookupTriple);
        Check(scene.Materials.ResolvedCount==1 && Enumerable.Range(0,100).All(_=>scene.Materials.Get(lookupTriple)==cachedGroup),"warm voxel triple resolutions retain the same atlas resource");
        scene.Materials.LookupCacheEnabled=false;Check(scene.Materials.Get(lookupTriple)==cachedGroup,"cached and legacy atlas selection return the same group");scene.Materials.LookupCacheEnabled=true;
        var replacement=definitions[1];replacement.Index=0;scene.Materials.Update(new[]{replacement},false);
        Check(scene.Materials.ResolvedCount==0 && scene.Materials.ResidentGroups==0,"material replacement invalidates cached atlas resolutions before resource reuse");
        backend.BeginUploads();scene.Materials.Prepare(Enumerable.Range(0,22).Select(i=>new MyVoxelMaterialTriple(i,-1,-1)));backend.EndUploads();
        Check(scene.Materials.Get(lookupTriple)!=cachedGroup && scene.Materials.AtlasStatus=="256px:1","replacement resolves a new atlas without changing its tier");
        scene.Materials.ClearResident();Check(scene.Materials.ResolvedCount==0 && scene.Materials.ResidentBytes==0,"voxel unload clears lookup references and residency");
        int notified=0;fake.Actor.CellChange+=(_,_,_)=>notified++;fake.Actor.BeginBatch(MyVoxelActorTransitionMode.Immediate);var cell=fake.Actor.CreateCell(Vector3D.Zero,0);cell.SetVisible(true);Check(!cell.Visible,"visibility remains staged before batch commit");
        var list=new MyList<MyVertexFormatVoxelSingleData>();foreach(var v in vertices)list.Add(v);ushort[] input={0,1,2};var processor=fake.VoxelRenderDataProcessorProvider.GetRenderDataProcessor(3,3,1);fixed(ushort* p=input)processor.AddPart(list,p,3,new MyVoxelMaterialTriple(0,-1,-1));var data=new MyVoxelRenderCellData();processor.GetDataAndDispose(ref data);
        Check(data.VertexCount==3&&data.IndexCount==3,"worker processor returns owned packed cell data");IMyVoxelUpdateBatch batch=null;cell.UpdateMesh(ref data,ref batch);Check(data.Vertices==null&&data.Indices==null,"cell takes ownership and disposes native mesher buffers");batch.Commit();Check(scene.MeshUpdates==0,"cell mesh replacement waits for actor batch");fake.Actor.EndBatch(true);Check(cell.Visible&&notified==1&&scene.MeshUpdates==1,"atomic batch applies mesh and visibility notifications");
        fake.Actor.BeginBatch();fake.Actor.DeleteCell(cell,true);Check(cell.Visible,"old LOD cell remains visible until replacement commit");fake.Actor.EndBatch(false);Check(!cell.Visible&&notified==2&&scene.CellCount==0,"cell deletion retires GPU geometry and reports hidden state");
        scene.Update(MatrixD.Identity,null,100,false);Check(fake.Updates==1,"owner thread pumps API-neutral LOD controller");scene.Process(new MyRenderMessageRemoveRenderObject{ID=5});Check(fake.Unloaded&&scene.ActorCount==0,"voxel actor removal unloads clipmap");
        backend.BeginPacketCollection();backend.RemoveResource(3,104);backend.RemoveResource(6,103);foreach(ulong id in new ulong[]{100,101,102})backend.RemoveResource(5,id);foreach(ulong id in new ulong[]{110,111,112})backend.RemoveResource(0,id);backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();
        var stats=backend.GetStats();Check(stats.ValidationEnabled!=0,"Khronos validation is enabled");Check(stats.ValidationErrors==0,"zero core/synchronization validation errors");Check(stats.Meshes==0&&stats.Textures==1,"unload returns resources to native fallback baseline");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,ValidationErrors=stats.ValidationErrors,Frames=stats.SubmittedFrames,WorldDraws=backend.GetWorkStats().WorldDraws,UploadBatches=backend.GetWorkStats().UploadBatches},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
    class Clipmap:IMyLodController
    {
        public IEnumerable<IMyVoxelActorCell> Cells=>Array.Empty<IMyVoxelActorCell>();public IMyVoxelActor Actor{get;private set;}public Vector3I Size=>new(32);public IMyVoxelRenderDataProcessorProvider VoxelRenderDataProcessorProvider{get;set;}public float? SpherizeRadius=>null;public Vector3D SpherizePosition=>Vector3D.Zero;public bool DoVisibilityUpdates{get;set;}public event Action<IMyLodController> Loaded;public bool Unloaded;public int Updates;
        public void BindToActor(IMyVoxelActor a){Actor=a;}public void Unload(){Unloaded=true;}public void Update(ref MatrixD v,BoundingFrustumD f,float far,bool smooth){Updates++;Loaded?.Invoke(this);}public void InvalidateRange(Vector3I min,Vector3I max){}public void InvalidateAll(){}public void DebugDraw(ref MatrixD m){}
    }
}

