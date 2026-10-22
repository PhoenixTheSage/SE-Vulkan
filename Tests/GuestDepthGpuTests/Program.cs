using System.Reflection;
using System.Runtime.Loader;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Scene;
using VRage.FileSystem;
using VRage.Library.Utils;
using VRageRender.Messages;
using VRageRender.Voxels;
using VRage.Library.Collections;
using VRage;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;

partial class Program
{
    static int Main(string[] args)
    {
        string bin=Path.GetFullPath(args[0]);
        string privateLayers=Path.GetFullPath("Native/build/validation/Bin");
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_LAYER_PATH")) && File.Exists(Path.Combine(privateLayers,"VkLayer_khronos_validation.json"))) {
            Environment.SetEnvironmentVariable("VK_LAYER_PATH",privateLayers);
            if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_LAYER_ENABLES")))Environment.SetEnvironmentVariable("VK_LAYER_ENABLES","VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT");
        }
        AssemblyLoadContext.Default.Resolving+=(_,name)=>{
            string path=Path.Combine(bin,name.Name+".dll");
            return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
        };
        try{return (args.Contains("--native-stages-only")||args.Contains("--native-bindings-only")||args.Contains("--native-binding-old-host")||args.Contains("--catalog-lifecycle-only"))?NativeStagesOnly(bin,args.Contains("--native-bindings-only"),args.Contains("--native-binding-old-host"),args.Contains("--catalog-lifecycle-only")):args.Contains("--interior-only")?InteriorTopology(bin):Run(bin);}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static unsafe int Run(string bin)
    {
        string root=Directory.GetCurrentDirectory(),output=Path.GetFullPath("TestResults/GuestDepthGpuTests");
        string stage=Path.Combine(output,"Assets");
        Directory.CreateDirectory(Path.Combine(stage,"Native/win-x64"));
        Directory.CreateDirectory(Path.Combine(stage,"Shaders/Compiled"));
        string isolatedNative=Environment.GetEnvironmentVariable("ANOMALY_TEST_NATIVE_DLL");
        File.Copy(string.IsNullOrWhiteSpace(isolatedNative)?Path.Combine(root,"Native/build/Release/se_vulkan_native.dll"):Path.GetFullPath(isolatedNative),Path.Combine(stage,"Native/win-x64/se_vulkan_native.dll"),true);
        foreach(string file in Directory.GetFiles(Path.Combine(root,"Assets/Shaders/Compiled")))
            File.Copy(file,Path.Combine(stage,"Shaders/Compiled",Path.GetFileName(file)),true);
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR",stage);
        var library=new VulkanShaderLibrary(Path.Combine(root,"Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);
        backend.Create(64,64,0,true,Path.Combine(output,"Cache"));
        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));
        backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        backend.SetParallelCascades(false);backend.WorldRecipesEnabled=false;
        int checks=0;
        void Check(bool condition,string message) {if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        var create=typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        ulong[] images={5001,5002,5003};int[] sizes={64,32,16};
        for(int i=0;i<3;i++)create.Invoke(backend,new object[]{images[i],sizes[i],sizes[i],1,7});
        NativeWorldVertex Vertex(float x,float y)
        {
            uint n=VF_Packer.PackNormal(Vector3.Backward);Vector4 tangent=new(1,0,0,1);uint t=VF_Packer.PackTangentSign(ref tangent);
            return new NativeWorldVertex{Material=new NativeGBufferVertex{X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=(t&255)/255f,TangentY=((t>>8)&255)/255f,TangentZ=((t>>16)&255)/255f,TangentW=(t>>24)/255f,U=(x+1)/2,V=(1-y)/2},WeightX=1};
        }
        backend.BeginUploads();
        backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);
        backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);
        backend.UploadTexture(12,1,1,0,new byte[]{255,1,0,0},false);
        backend.UploadWorldMesh(1,new[]{Vertex(-1,-1),Vertex(1,-1),Vertex(0,1)},new uint[]{0,1,2,2,1,0});
        backend.EndUploads();
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);
        projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var shadow=MatrixD.CreateTranslation(-10,0,0)*MatrixD.CreateOrthographic(4,4,1,10);
        float[] matrices=Enumerable.Range(0,3).SelectMany(_=>NativeBackend.MatrixValues(shadow)).ToArray();
        float[] stock=Enumerable.Range(0,8).SelectMany(_=>NativeBackend.MatrixValues(MatrixD.Identity)).ToArray();
        backend.SetShadowCascades(stock,Enumerable.Range(1,8).Select(i=>(float)i*10).ToArray());
        var light=MyEnvironmentLightData.Default;light.SunDiscIntensity=0;
        byte[] Frame(bool request,uint mask,bool casts=true)
        {
            backend.BeginPacketCollection();
            backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(10,0,0),Vector3.One,Vector3.Zero,1,0),
                flags:4096u|16384u|(casts?0u:512u),cascadeMask:mask);
            if(request)Check(backend.ConfigureGuestDepthViews(images,matrices),"independent depth views accepted");
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"real world frame begins");
            Check(backend.GuestDepthReady==0,"previous depth readiness revoked at frame start");
            backend.EndFrame();return backend.Readback(64,64);
        }
        float Center(int view)
        {
            int size=sizes[view];var bytes=new byte[size*size*4];read.Invoke(backend,new object[]{images[view],bytes});
            return BitConverter.ToSingle(bytes,(size/2*size+size/2)*4);
        }
        var baseline=Frame(false,0);
        var depthFrame=Frame(true,0x700);
        Check(backend.GuestDepthReady==7,"all three views recorded alongside eight stock cascades");
        for(int i=0;i<3;i++)Check(Math.Abs(Center(i)-2f/9)<.0001,"offscreen rigid caster depth matches analytic projection in view "+i);
        Check(baseline.SequenceEqual(depthFrame),"depth-only caster does not change main-view color");
        Frame(true,0x200);
        Check(Center(0)==1 && Math.Abs(Center(1)-2f/9)<.0001 && Center(2)==1,"independent view membership and per-frame clears");
        Frame(true,0x700,false);
        Check(Center(0)==1 && Center(1)==1 && Center(2)==1,"non-shadow caster excluded from dedicated depth views");
        Frame(false,0x700);
        Check(backend.GuestDepthReady==0,"depth request is consumed once instead of reusing stale views");
        bool duplicate=false;
        try {backend.ConfigureGuestDepthViews(new[]{images[0],images[0]},matrices.Take(32).ToArray());}
        catch(InvalidOperationException error) {duplicate=error.Message.Contains("duplicate");}
        Check(duplicate,"duplicate depth targets rejected");
        MyFileSystem.Init(Path.GetFullPath(Path.Combine(bin,"../Content")),Path.Combine(output,"UserData"));
        ParallelTasks.Parallel.Scheduler=new ParallelTasks.FakeTaskScheduler();
        using var scene=new VulkanScene(backend);
        using var voxels=new VulkanVoxels(backend,scene.TextureCache);
        var failures=new List<string>();scene.ContentFailed+=(path,error)=>failures.Add(path+": "+error.Message);
        scene.Process(new MyRenderMessageCreateRenderEntity {ID=10,Model=Path.GetFullPath(Path.Combine(bin,"../Content/Models/Cubes/large/ArmorCenter.mwm")),
            DebugName="Independent depth source",WorldMatrix=MatrixD.CreateTranslation(10,0,-3),Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.SkipInMainView});
        Check(scene.ModelCount==1,"production actor imports installed armor geometry: "+string.Join("; ",failures));
        object SceneCall(string name,params object[] values)=>typeof(VulkanScene).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scene,values);
        double[] worldViews=Enumerable.Range(0,3).SelectMany(_=>new[]{shadow.M11,shadow.M12,shadow.M13,shadow.M14,shadow.M21,shadow.M22,shadow.M23,shadow.M24,shadow.M31,shadow.M32,shadow.M33,shadow.M34,shadow.M41,shadow.M42,shadow.M43,shadow.M44}).ToArray();
        backend.BeginPacketCollection();scene.SetCamera(Vector3D.Zero,MyTimeSpan.Zero,1f/60);scene.Draw(backend);
        var actors=(System.Collections.IDictionary)typeof(VulkanScene).GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);
        object actor=actors[(uint)10];var actorType=actor.GetType();
        actorType.GetField("GpuOwned").SetValue(actor,true);actorType.GetField("PassOwned").SetValue(actor,true);
        SceneCall("BeginGuestDepthFrame");
        Check((bool)SceneCall("ConfigureGuestSceneDepth",images,worldViews),"managed world-space depth views configured");
        SceneCall("CollectGuestSceneDepth",voxels);
        var sources=(object[])SceneCall("CaptureGuestSceneDepth");
        Check((bool)sources[0] && (int)sources[2]==1 && (int)sources[3]>0,"main-hidden offscreen actor collected despite resident/GPU ownership flags: "+sources[5]);
        backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"managed scene source frame begins");backend.EndFrame();
        Check(backend.GuestDepthReady==7,"managed scene depth recorded in all views");
        for(int i=0;i<3;i++) {
            var bytes=new byte[sizes[i]*sizes[i]*4];read.Invoke(backend,new object[]{images[i],bytes});
            Check(Enumerable.Range(0,sizes[i]*sizes[i]).Any(p=>BitConverter.ToSingle(bytes,p*4)<.99f),"installed main-hidden armor writes actual depth in view "+i);
        }
        SceneCall("BeginGuestDepthFrame");sources=(object[])SceneCall("CaptureGuestSceneDepth");
        Check(!(bool)sources[0] && (int)sources[3]==0,"scene collection provenance resets before next request");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility {ID=10,Visible=false});
        backend.BeginPacketCollection();SceneCall("ConfigureGuestSceneDepth",images,worldViews);SceneCall("CollectGuestSceneDepth",voxels);
        sources=(object[])SceneCall("CaptureGuestSceneDepth");
        Check((bool)sources[0] && (int)sources[2]==0 && (int)sources[3]==0,"hidden actor excluded from complete current depth sources");
        backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"empty managed scene source frame begins");backend.EndFrame();
        Check(backend.GuestDepthReady==7 && Center(0)==1 && Center(1)==1 && Center(2)==1,"empty scene clears prior directional geometry instead of retaining it");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility {ID=10,Visible=true});
        object model=actorType.GetField("Model").GetValue(actor);actorType.GetField("Model").SetValue(actor,null);
        backend.BeginPacketCollection();SceneCall("BeginGuestDepthFrame");SceneCall("ConfigureGuestSceneDepth",images,worldViews);SceneCall("CollectGuestSceneDepth",voxels);
        sources=(object[])SceneCall("CaptureGuestSceneDepth");
        Check(!(bool)sources[0] && sources[5].ToString().Contains("model unavailable"),"missing caster geometry fails closed instead of certifying empty space");
        backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
        Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"failed-source frame begins");backend.EndFrame();
        Check(backend.GuestDepthReady==0,"failed scene source cancels all native depth requests");
        actorType.GetField("Model").SetValue(actor,model);
        actorType.GetField("GpuOwned").SetValue(actor,false);actorType.GetField("PassOwned").SetValue(actor,false);
        byte[] ManagedGeometry(string label)
        {
            backend.BeginPacketCollection();scene.Draw(backend);SceneCall("BeginGuestDepthFrame");
            SceneCall("ConfigureGuestSceneDepth",images,worldViews);SceneCall("CollectGuestSceneDepth",voxels);
            var capture=(object[])SceneCall("CaptureGuestSceneDepth");
            Check((bool)capture[0] && ((int)capture[2]>0 && (int)capture[3]>0 || (int)capture[4]>0),label+" sources complete: "+capture[5]);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),label+" frame begins");backend.EndFrame();
            var pixels=new byte[64*64*4];read.Invoke(backend,new object[]{images[0],pixels});
            Check(backend.GuestDepthReady==7 && Enumerable.Range(0,64*64).Any(p=>BitConverter.ToSingle(pixels,p*4)<.99f),label+" actual instance geometry writes dedicated depth");
            return pixels;
        }
        scene.Process(new MyRenderMessageCreateRenderInstanceBuffer {ID=11});
        var cube=new MyCubeInstanceData();cube.ResetBones();cube.LocalMatrix=Matrix.CreateTranslation(.5f,0,0);
        var cubeUpdate=new MyRenderMessageUpdateRenderCubeInstanceBuffer {ID=11};cubeUpdate.InstanceData.Add(cube);scene.Process(cubeUpdate);cubeUpdate.InstanceData.Clear();
        scene.Process(new MyRenderMessageSetInstanceBuffer {ID=10,InstanceBufferId=11,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-10),new Vector3(10))});
        var cubePixels=ManagedGeometry("copied cube instance");
        var generic=new MyInstanceData(Matrix.CreateTranslation(-.5f,0,0));
        scene.Process(new MyRenderMessageUpdateRenderInstanceBufferRange {ID=11,StartOffset=2,Trim=true,InstanceData=new[]{generic}});
        scene.Process(new MyRenderMessageSetInstanceBuffer {ID=10,InstanceBufferId=11,InstanceStart=2,InstanceCount=1,LocalAabb=new BoundingBox(new Vector3(-10),new Vector3(10))});
        var genericPixels=ManagedGeometry("generic instance with nonzero stream offset");
        Check(!cubePixels.SequenceEqual(genericPixels),"different copied instance transforms change the actual depth silhouette");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility {ID=10,Visible=false});
        scene.Process(new MyRenderMessageCreateStaticGroup {ID=12,Model=Path.GetFullPath(Path.Combine(bin,"../Content/Models/Cubes/large/ArmorCenter.mwm")),
            Translation=new Vector3D(10,0,-3),LocalMatrices=new[]{Matrix.CreateTranslation(.5f,0,0)}});
        ManagedGeometry("static group instance");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility {ID=12,Visible=false});
        scene.Process(new MyRenderMessageCreateRenderCharacter {ID=13,Model=Path.GetFullPath(Path.Combine(bin,"../Content/Models/Characters/Astronaut/SE_astronaut.mwm")),
            WorldMatrix=MatrixD.CreateTranslation(10,-1,-3),Flags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.SkipInMainView});
        var skeleton=Enumerable.Range(0,256).Select(_=>new MySkeletonBoneDescription {SkinTransform=Matrix.Identity}).ToArray();
        scene.Process(new MyRenderMessageSetCharacterSkeleton {CharacterID=13,SkeletonBones=skeleton,SkeletonIndices=Enumerable.Range(0,256).ToArray()});
        var pose=Enumerable.Repeat(Matrix.CreateTranslation(.4f,0,0),256).ToArray();
        scene.Process(new MyRenderMessageSetCharacterTransforms {CharacterID=13,BoneAbsoluteTransforms=pose});
        var firstPose=ManagedGeometry("installed skinned astronaut");
        pose=Enumerable.Repeat(Matrix.CreateTranslation(-.4f,0,0),256).ToArray();
        scene.Process(new MyRenderMessageSetCharacterTransforms {CharacterID=13,BoneAbsoluteTransforms=pose});
        var secondPose=ManagedGeometry("updated astronaut bone palette");
        Check(!firstPose.SequenceEqual(secondPose),"updated production skin palette changes directional depth silhouette");
        scene.Process(new MyRenderMessageUpdateRenderObjectVisibility {ID=13,Visible=false});
        var clipmap=new Clipmap();
        voxels.Process(new MyRenderMessageVoxelCreate {Id=14,Clipmap=clipmap,WorldMatrix=MatrixD.CreateTranslation(10,0,-3),Size=new Vector3I(32),
            RenderFlags=RenderFlags.Visible|RenderFlags.CastShadows|RenderFlags.SkipInMainView});
        voxels.Materials.Update(new[]{new MyRenderVoxelMaterialData {TextureSets=new MyRenderVoxelMaterialData.TextureSet[3]}},true);
        var cell=clipmap.Actor.CreateCell(Vector3D.Zero,0);cell.SetVisible(true);
        var voxelVertices=new MyList<MyVertexFormatVoxelSingleData>();
        foreach(var p in new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(0,1,0)})
            voxelVertices.Add(new MyVertexFormatVoxelSingleData {Position=p,Material=new Byte4 {PackedValue=0},PackedNormal=new Byte4 {PackedValue=VF_Packer.PackNormal(Vector3.Backward)}});
        ushort[] voxelIndices={0,1,2,2,1,0};
        var processor=clipmap.VoxelRenderDataProcessorProvider.GetRenderDataProcessor(3,6,1);
        fixed(ushort* pointer=voxelIndices)processor.AddPart(voxelVertices,pointer,6,new MyVoxelMaterialTriple(0,-1,-1));
        var cellData=new MyVoxelRenderCellData();processor.GetDataAndDispose(ref cellData);IMyVoxelUpdateBatch batch=null;cell.UpdateMesh(ref cellData,ref batch);batch.Commit();
        ManagedGeometry("main-hidden active voxel cell");
        var voxelCapture=(object[])SceneCall("CaptureGuestSceneDepth");
        Check((int)voxelCapture[2]==0 && (int)voxelCapture[4]==1,"voxel depth comes from its active cell registry");
        checks+=AnomalyTransaction(root,output,backend,scene,voxels,projection,light);
        var stats=backend.GetStats();
        Check(stats.ValidationErrors==0,"no reported Vulkan validation errors");
        checks+=AnomalyRecreation(root,output,bin,library,backend,scene,voxels,projection,light);
        Console.WriteLine("PASS "+checks+" scene-depth/shared-volume transaction/recreation checks; validation enabled="+stats.ValidationEnabled+". Loaded-world, plugin-adapter, vendor-upscale and performance acceptance remain pending.");
        return 0;
    }
    sealed class Clipmap:IMyLodController
    {
        public IEnumerable<IMyVoxelActorCell> Cells=>Array.Empty<IMyVoxelActorCell>();public IMyVoxelActor Actor{get;private set;}
        public Vector3I Size=>new(32);public IMyVoxelRenderDataProcessorProvider VoxelRenderDataProcessorProvider{get;set;}
        public float? SpherizeRadius=>null;public Vector3D SpherizePosition=>Vector3D.Zero;public bool DoVisibilityUpdates{get;set;}
        public event Action<IMyLodController> Loaded;
        public void BindToActor(IMyVoxelActor actor)=>Actor=actor;
        public void Unload(){}public void Update(ref MatrixD view,BoundingFrustumD frustum,float far,bool smooth){}
        public void InvalidateRange(Vector3I min,Vector3I max){}public void InvalidateAll(){}public void DebugDraw(ref MatrixD matrix){}
    }
}
