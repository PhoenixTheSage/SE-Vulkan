using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRageMath;
using VRageRender;
using VRageRender.Import;

class Program {
    static int Main(string[] args) {
        string bin=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving+=(_,n)=>{string path=Path.Combine(bin,n.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};
        return Run(bin);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static unsafe int Run(string bin) {
        int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        string content=Path.GetFullPath(Path.Combine(bin,"../Content")),output=Path.GetFullPath("TestResults/StaticDecalGpuTests");Directory.CreateDirectory(output);MyFileSystem.Init(content,output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(content,"Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(content,"Shaders"));backend.WorldRecipesEnabled=false;
        Check(backend.WorldLayouts.Count==54,"all ten OLD cutout streams initialize beside 44 existing pipelines");
        for(uint type=0;type<5;type++)foreach(bool metal in new[]{false,true}){
            uint id=WorldShaderVariants.Pipeline(MyMeshDrawTechnique.DECAL_CUTOUT,type,false,metal);
            Check(WorldShaderVariants.IsCutout(id)&&WorldShaderVariants.BasePipeline(id)==type+(metal?10u:0u),"OLD cutout stream/metal selection "+type+"/"+metal);
        }
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M13=projection.M14-projection.M13;projection.M23=projection.M24-projection.M23;projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=new Vector3(2);light.SunDiffuseFactor=1;
        var shadow=MatrixD.CreateLookAt(new Vector3D(0,0,10),new Vector3D(0,0,-5),Vector3D.Up)*MatrixD.CreateOrthographic(20,20,1,100);backend.SetShadowQuality(256,0);backend.SetShadowCascades(NativeBackend.MatrixValues(shadow),new[]{100f});backend.SetShadowSchedule(true,1,1);
        var shadowConstants=new byte[1200];BitConverter.GetBytes(1u).CopyTo(shadowConstants,1192);backend.SetShadowPassConstants(shadowConstants);
        backend.UploadTexture(10,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(11,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(12,1,1,0,new byte[]{0,1,0,0},false);backend.UploadTexture(14,1,1,0,new byte[]{20,20,240,0},true);
        NativeWorldVertex V(float x,float y,float z){uint normal=VRage.Import.VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=z,Scale=1,NormalX=normal&65535,NormalY=normal>>16,TangentX=1,TangentW=1,U=(x+1)/2,V=(y+1)/2},WeightX=1};}
        backend.UploadWorldMesh(1,new[]{V(-1,-1,0),V(1,-1,-.2f),V(0,1,-.1f)},new uint[]{0,1,2});
        backend.UploadWorldMesh(2,new[]{V(-1,-1,-.001f),V(1,-1,-.201f),V(0,1,-.101f)},new uint[]{0,1,2});
        backend.UploadMesh(3,new float[]{-1,-1,0,0,0,1,0,0,1,-1,-.2f,0,0,1,1,0,0,1,-.1f,0,0,1,.5f,1},new uint[]{0,1,2});
        byte[] Render(float distance,uint? decal,bool probe=false){
            backend.BeginPacketCollection();var world=MatrixD.CreateTranslation(0,0,-distance);
            backend.AddWorldPacket(0,1,10,11,12,0,WorldConstants.Object(backend.WorldLayouts[0],world,Vector3.One,Vector3.Zero,0,0));
            if(decal.HasValue)backend.AddWorldPacket(decal.Value,2,14,11,12,13,WorldConstants.Object(backend.WorldLayouts[decal.Value],world,Vector3.One,Vector3.Zero,0,0),flags:512|16384);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"frame begins");
            if(probe)backend.DrawMesh(3,MatrixD.CreateTranslation(0,0,-distance+.01),Color.Lime,0,4|512);
            backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);
        }
        foreach(float distance in new[]{2f,5f,12f}){
            backend.UploadTexture(13,1,1,0,new byte[]{255,255,255,255},false);
            var support=Render(distance,null);var supportDepth=backend.ReadbackShadow(0,256);
            var broken=Render(distance,0);var fixedPixels=Render(distance,22);
            int restored=support.Zip(fixedPixels).Count(p=>p.First!=p.Second);
            Check(broken.SequenceEqual(support)&&restored>30,"ordinary OLD shader loses surface detail; stock cutout restores it at "+distance+"m");
            Check(backend.ReadbackShadow(0,256).SequenceEqual(supportDepth),"cutout adds no shadow/depth caster at "+distance+"m");
            Check(Render(distance,22).SequenceEqual(fixedPixels),"cutout remains stable across both frame slots at "+distance+"m");
            backend.UploadTexture(13,1,1,0,new byte[]{0,0,0,0},false);
            Check(Render(distance,22).SequenceEqual(support),"alpha holes preserve underlying surface at "+distance+"m");
            if(distance==5){backend.UploadTexture(13,1,1,0,new byte[]{255,255,255,255},false);Check(Render(distance,22,true).SequenceEqual(Render(distance,null,true)),"cutout depth stays read-only for later transparent geometry");}
            File.WriteAllBytes(Path.Combine(output,"before-"+distance+".rgba"),broken);File.WriteAllBytes(Path.Combine(output,"after-"+distance+".rgba"),fixedPixels);
        }
        // Exercise the resident GPU registry using the same stock program and
        // geometry, independently of the managed packet selection.
        using var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"CompilerCache"));using var batch=compiler.CreateBatch(Path.Combine(content,"Shaders"));
        foreach(uint type in new[]{0u,2u,3u,4u}){
            foreach(uint id in new[]{type,32+type,22+type}){
                var direct=WorldShaderVariants.Materials.Where(s=>s.Id==id).ToArray();VulkanShaderRequest vs,ps;
                if(direct.Length!=0){vs=library.GpuOpaqueRequest(direct[0].Vertex);ps=library.GpuOpaqueRequest(direct[0].Pixel);}else{var spec=WorldShaderVariants.Extra.Single(s=>s.Id==id);vs=library.GpuOpaqueExtraRequest(id,spec.Material,"Vertex",spec.Defines);ps=library.GpuOpaqueExtraRequest(id,spec.Material,"Pixel",spec.Defines);}
                backend.InitializeGpuStockPipeline(id,true,VulkanShaderArtifact.FromCompilation(batch.Compile(vs),"cutout-gpu-vs"),VulkanShaderArtifact.FromCompilation(batch.Compile(ps),"cutout-gpu-ps"));
            }
            backend.UploadTexture(13,1,1,0,new byte[]{255,255,255,255},false);
            var source=new byte[64];if(type<3){foreach(int at in new[]{0,20,40})BitConverter.GetBytes(1f).CopyTo(source,at);}else{BitConverter.GetBytes(4f).CopyTo(source,44);source[23]=7;source[27]=3;source[31]=0x12;}
            var constants=WorldConstants.Object(backend.WorldLayouts[type],MatrixD.CreateTranslation(0,0,-5),Vector3.One,Vector3.Zero,0,0);
            byte[] StockStream(){
                backend.BeginPacketCollection();
                backend.AddWorldPacket(type,1,10,11,12,0,constants,type==0?null:source,1,0,1,new Vector3(0,0,-5),2,false);
                backend.AddWorldPacket(22+type,2,14,11,12,13,constants,type==0?null:source,1,512|16384,1,new Vector3(0,0,-5),2,false);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
                Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"stock stream frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);
            }
            var expected=StockStream();var expectedDepth=backend.ReadbackShadow(0,256);
            backend.WorldRecipesEnabled=true;Check(StockStream().SequenceEqual(expected)&&StockStream().SequenceEqual(expected),"retained cutout recipes preserve instance offsets in both slots stream "+type);backend.WorldRecipesEnabled=false;
            Check(Enumerable.Range(0,expected.Length/4).Any(i=>expected[i*4+2]>expected[i*4]+5),"OLD stream control has visible blue decal pixels "+type);
            var parts=new[]{new NativePassPart{Mesh=1,First=0,Count=3,Material=1,Shader=type,Flags=8,Color=10,Normal=11,Extensions=12},new NativePassPart{Mesh=2,First=0,Count=3,Material=2,Shader=22+type,Flags=4|8,Color=14,Normal=11,Extensions=12,Alpha=13}};
            ulong model=backend.RegisterGpuModel(parts,true),family=backend.RegisterGpuFamily(Array.Empty<float>(),new[]{model});
            var actor=new NativeGpuSceneActor{Family=family,Source=backend.RegisterGpuSource(source,type>=3),SourceCount=1,Selector=new(){Other=-1}};
            actor.Input.Owner=10+type;actor.Input.Generation=1;actor.Input.Flags=1|2|16|(type>=3?32u:0u);actor.Input.Scale=1;actor.Input.Translation[2]=-5;actor.Input.Basis[0]=actor.Input.Basis[4]=actor.Input.Basis[8]=1;actor.Input.Inverse[0]=actor.Input.Inverse[5]=actor.Input.Inverse[10]=1;actor.Input.Minimum[0]=actor.Input.Minimum[1]=-1;actor.Input.Minimum[2]=-5.21;actor.Input.Maximum[0]=actor.Input.Maximum[1]=1;actor.Input.Maximum[2]=-5;actor.Input.Colour[0]=actor.Input.Colour[1]=actor.Input.Colour[2]=actor.Input.Colour[3]=1;
            for(int i=0;i<128;i++)actor.Object[i]=constants[i];actor.Handle=backend.RegisterGpuActor(ref actor,Array.Empty<NativeGpuOpaqueAppearance>());backend.PublishGpuScene(2,0,0);
            var views=new NativeGpuOpaqueView[2];views[0].Flags=1;views[1].Flags=1|2|4;views[1].Id=1;for(int i=0;i<16;i++)views[1].Projection[i]=NativeBackend.MatrixValues(shadow)[i];
            for(uint epoch=1;epoch<=2;epoch++){
                backend.BeginPacketCollection();var frame=new NativeGpuOpaqueFrame{Epoch=epoch,Coefficient=1,DistanceMult=1,Flags=3};backend.QueueGpuSceneFrame(ref frame,views);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"resident frame begins");backend.RequestReadback();backend.EndFrame();
                Check(backend.Readback(64,64).SequenceEqual(expected),"resident OLD cutout matches stock pixels stream/slot "+type+"/"+epoch);Check(backend.ReadbackShadow(0,256).SequenceEqual(expectedDepth),"resident cutout keeps stock shadow exclusion stream/slot "+type+"/"+epoch);
            }
            backend.ClearGpuScene();
        }
        Console.WriteLine(JsonSerializer.Serialize(new{ValidationEnabled=backend.GetStats().ValidationEnabled,ValidationErrors=backend.GetStats().ValidationErrors}));
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"zero core/synchronization validation errors");
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{Checks=checks,NativeBackend.LibraryPath,Validation=backend.GetStats().ValidationEnabled,Errors=backend.GetStats().ValidationErrors},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(checks+" static decal checks passed");return 0;
    }
}
