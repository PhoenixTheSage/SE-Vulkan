using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRageMath;
using VRageRender;
partial class Program
{
    // DX11 serves this exact transform for the key; GBuffer pack overlays start from it on both hosts.
    static string VelocityGBufferPixelStage(Assembly core,string bin)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        var source=core.GetType("ClientPlugin.Contracts.GBufferVelocitySource",true);string key=(string)source.GetField("PixelStage",stat).GetValue(null);
        return (string)source.GetMethod("Patch",stat).Invoke(null,new object[]{key,File.ReadAllBytes(Path.Combine(bin,"../Content/Shaders",key)),true});
    }
    static int GBufferVelocityRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.gbuffer-velocity";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&8)!=0,"native owner extension advertises the GBuffer velocity contract on this device");
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        uint[] extra={2562,128};var catalog=backend.PostprocessCatalog().Where(row=>(uint)row[0]>=4096&&(uint)row[0]<4106||extra.Any(id=>(uint)row[0]>>1==2048+id)).ToArray();Check(catalog.Length==14,"fixture selects the five classic streams plus fade-bank and blended-decal GBuffer pairs: "+string.Join(",",catalog.Select(r=>(uint)r[0])));
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(958001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(958002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(958003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(958004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        float[] Bones(){var data=new float[960];for(int i=0;i<60;++i)Array.Copy(NativeBackend.MatrixValues(MatrixD.Identity),0,data,i*16,16);return data;}
        byte[] Stream(uint type){if(type==2){var bytes=new byte[64];Buffer.BlockCopy(new[]{1f,0f,0f,0f,0f,1f,0f,0f,0f,0f,1f,0f,0f,0f,0f,0f},0,bytes,0,64);return bytes;}if(type<3)return null;var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};if(type==4){cube.EnableSkinning=true;cube.BoneRange=1;for(int b=0;b<9;++b)cube[b]=new Vector3UByte(127,127,127);}var packed=new byte[64];for(int b=0;b<8;++b){var v=cube.GetPackedBone(b);packed[b*4]=v.X;packed[b*4+1]=v.Y;packed[b*4+2]=v.Z;packed[b*4+3]=v.W;}var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0f,0f,0f},0,packed,32,32);return packed;}
        // Fade-bank variants need a real threshold; it leaves the base streams unchanged.
        var fadeSettings=new MyRenderSettings();fadeSettings.User.DistanceFade=3000;
        uint[] instanced={2,3,4};uint frame=0;
        (byte[] G,float[] M) Frame(int step,bool pipelined=false,uint pipeline=0){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(pipeline,958001,958002,958003,958004,0,WorldConstants.Object(backend.WorldLayouts[pipeline],MatrixD.CreateTranslation(.1*step,0,0),Vector3.One,Vector3.Zero,1,0,pipeline==1?Bones():null),Stream(pipeline>=2560?pipeline-2560:pipeline),1,512|16384,0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,renderSettings:fadeSettings,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"velocity frame step="+step+" pipelined="+pipelined+" pipeline="+pipeline);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return(backend.ReadbackGbufferColor(64,64),backend.ReadbackMotion(64,64));
        }
        // Four steps establish matching draw history; the last frame moved +0.1 in x at depth 3.
        (byte[] G,float[] M) Run(bool pipelined=false,uint pipeline=0){(byte[],float[]) last=default;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3,pipeline);return last;}
        Vector2 Analytic(){Vector4D Clip(double x){var p=Vector4D.Transform(new Vector4D(x,0,-3,1),projection);return p/p.W;}var curr=Clip(.3);var prev=Clip(.2);return new Vector2((float)((prev.X-curr.X)*.5*64),(float)((curr.Y-prev.Y)*.5*64));}
        int centre=(32*64+32)*2,corner=(2*64+2)*2,colour=(32*64+32)*4;var expected=Analytic();
        string Motion(float[] m,int at)=>m[at]+","+m[at+1];
        bool Near(float[] m,Vector2 v,float tolerance)=>Math.Abs(m[centre]-v.X)<=tolerance&&Math.Abs(m[centre+1]-v.Y)<=tolerance;
        bool Magenta(byte[] g)=>g[colour]==255&&g[colour+1]==0&&g[colour+2]==255;
        string root=Path.Combine(output,"GBufferVelocityPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");
        string patched=VelocityGBufferPixelStage(core,bin),magenta=patched.Replace("ApplyMultipliers(material_output);","ApplyMultipliers(material_output);material_output.base_color=float3(1,0,1);");
        Check(magenta!=patched&&patched.Contains("float2 anomaly_velocity = input.anomaly_velocity;"),"shared Anomaly transform yields the velocity-aware GBuffer pixel stage");
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            var stock=Run();Console.WriteLine("VELOCITY analytic="+expected+" stock="+Motion(stock.M,centre));
            Check(Near(stock.M,expected,.02f)&&stock.M[corner]==0&&stock.M[corner+1]==0,"stock replay writes the analytic object motion and leaves uncovered pixels at zero");
            Check(stock.M.SequenceEqual(Run(true).M),"stock replay motion agrees across recording modes");
            var stockStreams=instanced.ToDictionary(id=>id,id=>Run(pipeline:id));foreach(uint id in instanced){Console.WriteLine("VELOCITY stock stream"+id+"="+Motion(stockStreams[id].M,centre));Check(Near(stockStreams[id].M,expected,.02f)&&stockStreams[id].M[corner]==0,"stock replay writes the analytic motion for instanced/cube stream id"+id);}
            var fade=Run(pipeline:2562);var decal=Run(pipeline:128);Console.WriteLine("VELOCITY stock fade="+Motion(fade.M,centre)+" decal="+Motion(decal.M,centre)+" fadeG="+string.Join(",",fade.G.Skip(colour).Take(4))+" decalG="+string.Join(",",decal.G.Skip(colour).Take(4))+" rigidG="+string.Join(",",stock.G.Skip(colour).Take(4)));
            Check(Near(fade.M,expected,.02f)&&fade.M[corner]==0,"stock replay writes the analytic motion for the fade-bank stream");
            Check(decal.M[centre]==0&&decal.M[centre+1]==0,"stock replay does not cover blended-decal draws (control)");
            var skinned=Run(pipeline:1);Check(skinned.M[centre]==0&&skinned.M[centre+1]==0,"stock replay does not cover skinned classic draws (control)");
            Register(magenta);var owned=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"velocity-aware GBuffer owner prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            Console.WriteLine("VELOCITY owned="+Motion(owned.M,centre));Check(Magenta(owned.G),"admitted rigid owner draws the replacement material");
            var state=backend.GetPostprocessGeneration(4097);Check(state.Active!=0&&state.Executions>0&&state.LastFrame==frame&&state.LastCompletion<=state.Completed,"admitted rigid owner pair executes and completes this frame");
            Check(Near(owned.M,expected,.02f)&&Math.Abs(owned.M[centre]-stock.M[centre])<.01f&&Math.Abs(owned.M[centre+1]-stock.M[centre+1])<.01f&&owned.M[corner]==0&&owned.M[corner+1]==0,"owner-emitted Target3 matches the analytic and replay motion; uncovered pixels stay zero");
            Check(owned.M.SequenceEqual(Run().M)&&owned.G.SequenceEqual(Frame(3).G),"owner-emitted motion and material agree across recording modes");
            foreach(uint id in instanced){var stream=Run(true,id);Console.WriteLine("VELOCITY owned stream"+id+"="+Motion(stream.M,centre));var status=backend.GetPostprocessGeneration(4097+2*id);
                Check(Magenta(stream.G)&&status.Active==state.Active&&status.Executions>0&&status.LastFrame==frame,"admitted instanced/cube owner pair executes the replacement id"+id);
                Check(Near(stream.M,expected,.02f)&&Math.Abs(stream.M[centre]-stockStreams[id].M[centre])<.01f&&Math.Abs(stream.M[centre+1]-stockStreams[id].M[centre+1])<.01f&&stream.M[corner]==0,"owner-emitted Target3 matches analytic and replay motion for stream id"+id);
                Check(stream.M.SequenceEqual(Run(pipeline:id).M),"owner-emitted motion agrees across recording modes id"+id);}
            {var stream=Run(true,1);Console.WriteLine("VELOCITY owned skinned="+Motion(stream.M,centre));var status=backend.GetPostprocessGeneration(4099);
                Check(Magenta(stream.G)&&status.Active==state.Active&&status.Executions>0&&status.LastFrame==frame,"admitted skinned owner pair executes the replacement");
                Check(Near(stream.M,expected,.02f)&&stream.M[corner]==0,"skinned owner emits DX11 rigid-body currToPrev motion that the replay never covered");
                Check(stream.M.SequenceEqual(Run(pipeline:1).M),"skinned owner motion agrees across recording modes");}
            {var stream=Run(true,2562);Console.WriteLine("VELOCITY owned fade="+Motion(stream.M,centre));Check(backend.GetPostprocessGeneration(4097+2*2562).Executions>0&&Near(stream.M,expected,.02f)&&Math.Abs(stream.M[centre]-fade.M[centre])<.01f&&stream.M[corner]==0,"fade-bank owner emits analytic/replay motion from its own invocation");}
            {var stream=Run(true,128);Console.WriteLine("VELOCITY owned decal="+Motion(stream.M,centre));Check(backend.GetPostprocessGeneration(4097+2*128).Executions>0&&Near(stream.M,expected,.02f)&&stream.M[corner]==0,"blended-decal owner writes analytic motion as an RG replace the replay never covered");}
            Register(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);"));var custom=Run(true);Console.WriteLine("VELOCITY custom="+Motion(custom.M,centre));
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&custom.M[centre]==3&&custom.M[centre+1]==-2&&custom.M[corner]==0&&Magenta(custom.G),"pack-written velocity reaches Target3 from the owning invocation; replay skips that draw");
            foreach(uint id in instanced){var stream=Run(true,id);Check(stream.M[centre]==3&&stream.M[centre+1]==-2&&stream.M[corner]==0&&Magenta(stream.G),"pack-written velocity reaches Target3 from the instanced/cube owning invocation id"+id);}
            foreach(uint id in extra){var stream=Run(true,id);Check(stream.M[centre]==3&&stream.M[centre+1]==-2&&stream.M[corner]==0,"pack-written velocity reaches Target3 from the owning invocation id"+id);}
            var skinnedOwned=Run(true,1);Check(Magenta(skinnedOwned.G)&&skinnedOwned.M[centre]==3&&skinnedOwned.M[centre+1]==-2&&skinnedOwned.M[corner]==0,"pack-written velocity reaches Target3 from the skinned owning invocation");
            Check(Run().M.SequenceEqual(custom.M),"pack-written velocity agrees across recording modes");
            ulong token=backend.GetPostprocessGeneration(4097).Active;Register("BrokenSyntax");var broken=Run();Console.WriteLine("VELOCITY broken="+Motion(broken.M,centre)+" error="+Property("Backend.GuestPostprocessStages","Error")+" active="+backend.GetPostprocessGeneration(4097).Active+" was="+token);
            Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(4097).Active==token&&broken.M[centre]==3,"failed compilation retains the previous velocity owner generation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Run(true);
            Check(restored.M.SequenceEqual(stock.M)&&restored.G.SequenceEqual(stock.G),"unregister restores exact stock material and replay motion bits");
            foreach(uint id in instanced){var stream=Run(true,id);Check(stream.M.SequenceEqual(stockStreams[id].M)&&stream.G.SequenceEqual(stockStreams[id].G),"unregister restores exact stock material and replay motion id"+id);}
            {var stream=Run(true,1);Check(stream.M.SequenceEqual(skinned.M)&&stream.G.SequenceEqual(skinned.G),"unregister restores exact stock skinned material and replay motion");}
            foreach(var (id,control) in new[]{(2562u,fade),(128u,decal)}){var stream=Run(true,id);Check(stream.M.SequenceEqual(control.M)&&stream.G.SequenceEqual(control.G),"unregister restores exact stock material and motion id"+id);}
            Run();Check(backend.GetPostprocessGeneration(4097).Retired==0,"velocity owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"GBuffer velocity owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
    // GPU NEW owners compile USE_SIMPLE_INSTANCING (t15 per instance); GPU OLD owners keep the classic per-draw currToPrev rows.
    static unsafe int GpuVelocityRegression(Assembly core,string output,string bin,NativeBackend backend,VulkanShaderLibrary library,MatrixD projection,MyEnvironmentLightData light,bool old=false)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;string owner=old?"fixture.native.gpu-old-velocity":"fixture.native.gpu-velocity";uint pixelOwner=old?49153u:32769u;
        // GPU OLD instanced/cube streams read source instances (t-free) and keep the per-draw currToPrev contract.
        uint[] streams=old?new uint[]{0,2,3,4}:new uint[]{0};string path=old?"GPU OLD":"GPU NEW";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&(old?12u:10u))==(old?12u:10u),"native owner extension advertises "+path+" owners and the GBuffer velocity contract");
        typeof(NativeBackend).GetMethod("ConfigureIndexedMotion",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(backend,null);
        backend.ConfigureNativePasses(library);
        using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"GpuVelocityStockCache")))foreach(uint id in old?streams.SelectMany(s=>new[]{s,32+s}).ToArray():new uint[]{0,4}){var spec=old?WorldShaderVariants.Materials.Single(v=>v.Id==id):NativePassShaderVariants.Materials.Single(v=>v.Id==id);
            backend.InitializeGpuStockPipeline(id,old,VulkanShaderArtifact.FromCompilation(compiler.Compile(library.GpuOpaqueRequest(spec.Vertex)),"fixture-indexed-vs"),VulkanShaderArtifact.FromCompilation(compiler.Compile(library.GpuOpaqueRequest(spec.Pixel)),"fixture-indexed-ps"));}
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        var catalog=backend.PostprocessCatalog().Where(row=>streams.Any(id=>((uint)row[0]|1)==pixelOwner+2*id)).ToArray();Check(catalog.Length==2*streams.Length,"fixture selects the initialized "+path+" GBuffer owner pairs: "+string.Join(",",catalog.Select(r=>(uint)r[0])));
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=0,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(957001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(957002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(957003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(957004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        uint sourceFirst=0;NativeGpuOpaqueActor Actor(int step){var a=new NativeGpuOpaqueActor{Owner=10,Family=0,Generation=1,Flags=1|16,Scale=1,SourceFirst=sourceFirst,SourceCount=1};double x=.1*step;a.Translation[0]=x;a.Translation[2]=-5;a.Minimum[0]=x-1;a.Minimum[1]=-1;a.Minimum[2]=-5;a.Maximum[0]=x+1;a.Maximum[1]=1;a.Maximum[2]=-5;a.Basis[0]=a.Basis[4]=a.Basis[8]=1;a.Colour[0]=a.Colour[1]=a.Colour[2]=1;if(old){a.Flags|=2;a.Inverse[0]=a.Inverse[5]=a.Inverse[10]=1;}return a;}
        var part=new NativeGpuOpaquePart{Family=0,IndexCount=6,Material=10,Flags=3,Generation=1};part.Textures[0]=1;part.Textures[1]=2;part.Textures[2]=3;
        byte[] Cube(bool skinned){var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};if(skinned){cube.EnableSkinning=true;cube.BoneRange=1;for(int b=0;b<9;++b)cube[b]=new Vector3UByte(127,127,127);}var packed=new byte[64];for(int b=0;b<8;++b){var v=cube.GetPackedBone(b);packed[b*4]=v.X;packed[b*4+1]=v.Y;packed[b*4+2]=v.Z;packed[b*4+3]=v.W;}var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0f,0f,0f},0,packed,32,32);return packed;}
        var source=new byte[192];BitConverter.GetBytes(1f).CopyTo(source,0);BitConverter.GetBytes(1f).CopyTo(source,20);BitConverter.GetBytes(1f).CopyTo(source,40);Cube(false).CopyTo(source,64);Cube(true).CopyTo(source,128);var objects=new byte[128];WorldConstants.Object(old?backend.WorldLayouts[0]:library.Load(NativePassShaderVariants.Materials[0].Vertex).Layout,MatrixD.Identity,Vector3.One,Vector3.Zero,0,0).CopyTo(objects,0);
        backend.InitializeGpuOpaqueReference(new[]{Actor(0)},new[]{new NativeGpuOpaqueFamily{Generation=1,FirstPart=0,PartCount=1}},new[]{part},1,new float[]{-1,-1,0,1,-1,0,0,1,0},new uint[]{0,1,2,2,1,0},source,new ulong[]{0,957002,957003,957004},8,16,1);
        // Stream 0 is rigid; 2..4 select the generic, cube and deformed-cube source records.
        void Use(uint id){sourceFirst=id<2?0u:id-2;backend.SetGpuStockBatches(new[]{new NativeGpuOpaqueBatch{Mesh=957001,Old=old?1u:0u,MainPipeline=id,DepthPipeline=old?32+id:4u,ForwardPipeline=uint.MaxValue,Flags=1}},new[]{957001UL},objects);}
        Use(0);
        uint frame=0,epoch=1;
        (byte[] G,float[] M) Frame(int step,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);backend.ClearPassFrame();
            backend.UpdateGpuOpaqueActors(new uint[]{0},new[]{Actor(step)});
            var views=new[]{new NativeGpuOpaqueView{Flags=1}};var p=NativeBackend.MatrixValues(projection);for(int i=0;i<16;i++)views[0].Projection[i]=p[i];
            var f=new NativeGpuOpaqueFrame{Epoch=epoch++,Coefficient=1,DistanceMult=1,Flags=3,SmoothCameraDistance=100,MaximumSeconds=1,MinimumSeconds=.25f};backend.QueueGpuOpaqueFrame(ref f,views);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),path+" velocity frame step="+step+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return(backend.ReadbackGbufferColor(64,64),backend.ReadbackMotion(64,64));
        }
        (byte[] G,float[] M) Run(bool pipelined=false){(byte[],float[]) last=default;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3);return last;}
        Vector2 Analytic(){Vector4D Clip(double x){var c=Vector4D.Transform(new Vector4D(x,0,-5,1),projection);return c/c.W;}var curr=Clip(.3);var prev=Clip(.2);return new Vector2((float)((prev.X-curr.X)*.5*64),(float)((curr.Y-prev.Y)*.5*64));}
        int centre=(32*64+32)*2,corner=(2*64+2)*2,colour=(32*64+32)*4;var expected=Analytic();
        string Motion(float[] m)=>m[centre]+","+m[centre+1];
        bool Near(float[] m,float tolerance)=>Math.Abs(m[centre]-expected.X)<=tolerance&&Math.Abs(m[centre+1]-expected.Y)<=tolerance;
        bool Magenta(byte[] g)=>g[colour]==255&&g[colour+1]==0&&g[colour+2]==255;
        string root=Path.Combine(output,"GpuVelocityPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");
        string magenta=VelocityGBufferPixelStage(core,bin).Replace("ApplyMultipliers(material_output);","ApplyMultipliers(material_output);material_output.base_color=float3(1,0,1);");
        void Register(string text){File.WriteAllText(file,text);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            var stock=Run();var stockG1=backend.ReadbackWorldGbuffer(1,64,64);Console.WriteLine(path+" VELOCITY analytic="+expected+" stock="+Motion(stock.M));
            Check(Near(stock.M,.02f)&&stock.M[corner]==0&&stock.M[corner+1]==0,"stock indexed replay writes the analytic "+path+" motion");
            Check(stock.M.SequenceEqual(Run(true).M),"stock indexed replay agrees across recording modes");
            var stockStreams=new Dictionary<uint,(byte[] G,float[] M,byte[] G1)>();
            foreach(uint id in streams.Skip(1)){Use(id);var r=Run();stockStreams[id]=(r.G,r.M,backend.ReadbackWorldGbuffer(1,64,64));Console.WriteLine(path+" VELOCITY stock stream"+id+"="+Motion(r.M));
                Check(Near(r.M,.02f)&&r.M[corner]==0,"stock indexed replay writes the analytic "+path+" motion for instanced/cube stream id"+id);}
            Use(0);
            Register(magenta);var owned=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"velocity-aware "+path+" owner prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            Console.WriteLine(path+" VELOCITY owned="+Motion(owned.M));var state=backend.GetPostprocessGeneration(pixelOwner);
            Check(Magenta(owned.G)&&state.Active!=0&&state.Executions>0&&state.LastFrame==frame,path+" owner pair executes the replacement through indirect draws");
            Check(Near(owned.M,.02f)&&Math.Abs(owned.M[centre]-stock.M[centre])<.01f&&Math.Abs(owned.M[centre+1]-stock.M[centre+1])<.01f&&owned.M[corner]==0,"owner-emitted Target3 from "+(old?"per-draw currToPrev":"the t15 instance history")+" matches analytic and replay motion");
            Check(owned.M.SequenceEqual(Run().M),path+" owner motion agrees across recording modes");
            Register(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);"));var custom=Run(true);Console.WriteLine(path+" VELOCITY custom="+Motion(custom.M));
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&custom.M[centre]==3&&custom.M[centre+1]==-2&&custom.M[corner]==0&&Magenta(custom.G),"pack-written velocity reaches Target3 from the "+path+" owning invocation; replay skips that batch");
            AttachmentOwnerPhase(core,backend,owner,root,file,magenta,path,pixelOwner,stockG1,p=>Run(p).M,m=>Near(m,.02f),Check);
            int pixel=32*64+32,outside=2*64+2,slot=AttachmentSlot(core,out _);
            foreach(uint id in streams.Skip(1)){Use(id);var stream=Run(true);var g1=backend.ReadbackWorldGbuffer(1,64,64);var a=backend.ReadbackAttachment((uint)slot,64,64,4);var status=backend.GetPostprocessGeneration(pixelOwner+2*id);var control=stockStreams[id];
                Console.WriteLine(path+" VELOCITY owned stream"+id+"="+Motion(stream.M)+" objectid="+BitConverter.ToUInt32(a,pixel*4)+" gbuffer1.a="+g1[pixel*4+3]);
                Check(Magenta(stream.G)&&status.Active!=0&&status.Executions>0&&status.LastFrame==frame,path+" instanced/cube owner pair executes the replacement id"+id);
                Check(Near(stream.M,.02f)&&Math.Abs(stream.M[centre]-control.M[centre])<.01f&&Math.Abs(stream.M[centre+1]-control.M[centre+1])<.01f&&stream.M[corner]==0,"owner-emitted Target3 from per-draw currToPrev matches analytic and replay motion id"+id);
                Check(BitConverter.ToUInt32(a,pixel*4)==777&&BitConverter.ToUInt32(a,outside*4)==0&&Math.Abs(g1[pixel*4+3]-128)<=1&&g1[outside*4+3]==control.G1[outside*4+3],path+" instanced/cube owning invocation writes the attachment and packed GBuffer1.a id"+id);
                var sync=Run();Check(sync.M.SequenceEqual(stream.M)&&sync.G.SequenceEqual(stream.G)&&backend.ReadbackAttachment((uint)slot,64,64,4).SequenceEqual(a),path+" instanced/cube owner outputs agree across recording modes id"+id);}
            if(streams.Length>1){File.WriteAllText(file,AttachmentPixelStage(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);")));Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
                foreach(uint id in streams.Skip(1)){Use(id);var stream=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null&&stream.M[centre]==3&&stream.M[centre+1]==-2&&stream.M[corner]==0&&Magenta(stream.G),"pack-written velocity reaches Target3 from the "+path+" instanced/cube owning invocation id"+id);}}
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Run(true);
            foreach(uint id in streams.Skip(1)){Use(id);var stream=Run(true);var control=stockStreams[id];Check(stream.M.SequenceEqual(control.M)&&stream.G.SequenceEqual(control.G)&&backend.ReadbackWorldGbuffer(1,64,64).SequenceEqual(control.G1),"unregister restores exact stock "+path+" material, GBuffer1 and replay motion id"+id);}
            if(streams.Length>1){Use(0);restored=Run(true);}
            Check(restored.M.SequenceEqual(stock.M)&&restored.G.SequenceEqual(stock.G)&&backend.ReadbackWorldGbuffer(1,64,64).SequenceEqual(stockG1),"unregister restores exact stock "+path+" material, GBuffer1 and replay motion");
            Run();Check(backend.GetPostprocessGeneration(pixelOwner).Retired==0,path+" velocity owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,path+" velocity owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);backend.ClearPassFrame();stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
    // Native cells draw with firstInstance 0 per group; DX11 indexes t15[SV_InstanceID + InstanceBase].
    static unsafe int CellVelocityRegression(Assembly core,string output,string bin,NativeBackend backend,VulkanShaderLibrary library,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.cell-velocity";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&9)==9,"native owner extension advertises native-cell owners and the GBuffer velocity contract");
        backend.ConfigureNativePasses(library);
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        var catalog=backend.PostprocessCatalog().Where(row=>(uint)row[0]==16384||(uint)row[0]==16385).ToArray();Check(catalog.Length==2,"fixture selects the native-cell GBuffer owner pair");
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=0,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(956001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(956002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(956003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(956004,1,1,0,new byte[]{255,1,0,0},false);
        ulong model=backend.RegisterPassModel(new[]{new NativePassPart{Mesh=956001,Color=956002,Normal=956003,Extensions=956004,First=0,Count=6,Material=10,Flags=2}}),family=backend.RegisterPassFamily(Array.Empty<float>());backend.BindPassLevel(family,0,model);
        NativePassActor Actor(int step){var value=new NativePassActor{Owner=10,Family=family,Group=7,Flags=1|8|16|32,Scale=1,Lod=new(){Previous=-1}};double x=.1*step;value.Translation[0]=x;value.Translation[2]=-5;value.Bounds[0]=x-1;value.Bounds[1]=-1;value.Bounds[2]=-5;value.Bounds[3]=x+1;value.Bounds[4]=1;value.Bounds[5]=-5;value.Basis[0]=value.Basis[4]=value.Basis[8]=1;return value;}
        var actor=Actor(0);ulong handle=backend.RegisterPassActor(ref actor,Array.Empty<NativePassAppearance>());
        NativePassFrame CellInput(){var input=new NativePassFrame{Coefficient=1,Delta=.1f,GlobalFlags=3,SmoothDistance=100};input.Global[4]=.25f;input.Global[6]=1;return input;}var cellViews=new[]{new NativePassView{Id=0,Kind=0,Flags=1}};
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        uint frame=0;
        (byte[] G,float[] M) Frame(int step,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            var current=Actor(step);current.Handle=handle;backend.UpdatePassActors(new[]{current},1,Array.Empty<NativePassAppearance>(),0);var cellFrame=CellInput();backend.PreparePassFrame(ref cellFrame,cellViews,cellViews.Length);backend.PublishPassFrame(Array.Empty<NativePassQueryResult>(),0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"cell velocity frame step="+step+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return(backend.ReadbackGbufferColor(64,64),backend.ReadbackMotion(64,64));
        }
        (byte[] G,float[] M) Run(bool pipelined=false){(byte[],float[]) last=default;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3);return last;}
        Vector2 Analytic(){Vector4D Clip(double x){var c=Vector4D.Transform(new Vector4D(x,0,-5,1),projection);return c/c.W;}var curr=Clip(.3);var prev=Clip(.2);return new Vector2((float)((prev.X-curr.X)*.5*64),(float)((curr.Y-prev.Y)*.5*64));}
        int centre=(32*64+32)*2,corner=(2*64+2)*2,colour=(32*64+32)*4;var expected=Analytic();
        string Motion(float[] m)=>m[centre]+","+m[centre+1];
        bool Near(float[] m,float tolerance)=>Math.Abs(m[centre]-expected.X)<=tolerance&&Math.Abs(m[centre+1]-expected.Y)<=tolerance;
        bool Magenta(byte[] g)=>g[colour]==255&&g[colour+1]==0&&g[colour+2]==255;
        string root=Path.Combine(output,"CellVelocityPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"]}");
        string magenta=VelocityGBufferPixelStage(core,bin).Replace("ApplyMultipliers(material_output);","ApplyMultipliers(material_output);material_output.base_color=float3(1,0,1);");
        void Register(string text){File.WriteAllText(file,text);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            var stock=Run();var stockG1=backend.ReadbackWorldGbuffer(1,64,64);Console.WriteLine("CELL VELOCITY analytic="+expected+" stock="+Motion(stock.M));
            Check(Near(stock.M,.02f)&&stock.M[corner]==0&&stock.M[corner+1]==0,"stock cell replay writes the analytic per-instance motion");
            Check(stock.M.SequenceEqual(Run(true).M),"stock cell replay agrees across recording modes");
            Register(magenta);var owned=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"velocity-aware native-cell owner prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            Console.WriteLine("CELL VELOCITY owned="+Motion(owned.M));var state=backend.GetPostprocessGeneration(16385);
            Check(Magenta(owned.G)&&state.Active!=0&&state.Executions>0&&state.LastFrame==frame,"native-cell owner pair executes the replacement");
            Check(Near(owned.M,.02f)&&Math.Abs(owned.M[centre]-stock.M[centre])<.01f&&Math.Abs(owned.M[centre+1]-stock.M[centre+1])<.01f&&owned.M[corner]==0,"owner-emitted Target3 from the t15 history stream matches analytic and replay motion");
            Check(owned.M.SequenceEqual(Run().M),"native-cell owner motion agrees across recording modes");
            Register(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);"));var custom=Run(true);Console.WriteLine("CELL VELOCITY custom="+Motion(custom.M));
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&custom.M[centre]==3&&custom.M[centre+1]==-2&&custom.M[corner]==0&&Magenta(custom.G),"pack-written velocity reaches Target3 from the native-cell owning invocation; replay skips that group");
            AttachmentOwnerPhase(core,backend,owner,root,file,magenta,"native-cell",16385,stockG1,p=>Run(p).M,m=>Near(m,.02f),Check);
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Run(true);
            Check(restored.M.SequenceEqual(stock.M)&&restored.G.SequenceEqual(stock.G)&&backend.ReadbackWorldGbuffer(1,64,64).SequenceEqual(stockG1),"unregister restores exact stock native-cell material, GBuffer1 and replay motion");
            Run();Check(backend.GetPostprocessGeneration(16385).Retired==0,"native-cell velocity owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"native-cell velocity owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);backend.ClearPassFrame();stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
    // Shared pack: R32_UINT objectid 777 at SV_Target4+slot and GBuffer1.a 0.5 through ANOMALY_PACK_GBUFFER1A.
    static string AttachmentManifest(string owner)=>"{\"id\":\""+owner+"\",\"exclusive\":[\"GBuffer\"],\"attachments\":[{\"name\":\"objectid\",\"format\":\"R32_UINT\"},{\"name\":\"flags\",\"format\":\"GBuffer1.a\"}]}";
    static string AttachmentPixelStage(string patched)=>patched.Replace("#ifdef ANOMALY_PACK_GBUFFER1A\n#ifndef ANOMALY_GBUFFER1A_OVERRIDE","#ifdef ANOMALY_PACK_GBUFFER1A\n#define ANOMALY_GBUFFER1A_OVERRIDE\nfloat AnomalyGBuffer1A(PixelInterface pixel, MaterialOutputInterface material_output)\n{\n\treturn 0.5;\n}\n#endif\n#ifdef ANOMALY_PACK_GBUFFER1A\n#ifndef ANOMALY_GBUFFER1A_OVERRIDE")
            .Replace("#ifndef STATIC_DECAL\n#ifdef ANOMALY_PACK_GBUFFER1A\n\toutput.gbuffer1.a","#ifdef ANOMALY_ATTACH_OBJECTID\n\toutput.objectid = 777u;\n#endif\n#ifndef STATIC_DECAL\n#ifdef ANOMALY_PACK_GBUFFER1A\n\toutput.gbuffer1.a");
    static int AttachmentSlot(Assembly core,out int format)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        object Field(object o,string n)=>o.GetType().GetField(n).GetValue(o);
        var attachments=((System.Collections.IEnumerable)core.GetType("ClientPlugin.Backend.GuestPassList",true).GetMethod("SnapshotAttachments",stat).Invoke(null,null)).Cast<object>();
        var objectid=attachments.SingleOrDefault(a=>(string)Field(a,"Name")=="objectid");format=objectid==null?-1:(int)Field(objectid,"NativeFormat");return objectid==null?-1:(int)Field(objectid,"Slot");
    }
    // Native-cell and GPU owners record inside the same main GBuffer scope; their invocations must write the pack attachments too.
    static void AttachmentOwnerPhase(Assembly core,NativeBackend backend,string owner,string root,string file,string magenta,string path,uint pixelOwner,byte[] stockG1,Func<bool,float[]> run,Func<float[],bool> near,Action<bool,string> check)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        object Error()=>core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetProperty("Error",stat).GetValue(null);
        File.WriteAllText(Path.Combine(root,"anomaly.json"),AttachmentManifest(owner));File.WriteAllText(file,AttachmentPixelStage(magenta));
        core.GetType("ClientPlugin.Backend.GuestPassList",true).GetMethod("RegisterRoot",stat).Invoke(null,new object[]{owner,root,null});
        int slot=AttachmentSlot(core,out int format);check(slot>=0&&format==4,path+" pack admits the R32_UINT attachment to a DX11 SV_Target4..6 slot");
        (byte[] G1,float[] M,byte[] A) Read(bool pipelined){var m=run(pipelined);return(backend.ReadbackWorldGbuffer(1,64,64),m,backend.ReadbackAttachment((uint)slot,64,64,4));}
        int centre=32*64+32,corner=2*64+2;var owned=Read(true);check(Error()==null,"attachment-writing "+path+" owner prepares: "+Error());
        uint id=BitConverter.ToUInt32(owned.A,centre*4),outside=BitConverter.ToUInt32(owned.A,corner*4);int alpha=owned.G1[centre*4+3];Console.WriteLine(path+" ATTACH owned objectid="+id+" outside="+outside+" gbuffer1.a="+alpha+" stock="+stockG1[centre*4+3]+" slot="+slot);
        var state=backend.GetPostprocessGeneration(pixelOwner);check(state.Active!=0&&state.Executions>0,"attachment-writing "+path+" owner pair executes");
        check(id==777&&outside==0,path+" owning invocation writes the named attachment at SV_Target"+(4+slot)+"; uncovered pixels stay cleared");
        check(Math.Abs(alpha-128)<=1&&owned.G1[corner*4+3]==stockG1[corner*4+3],path+" packed GBuffer1.a carries the pack value; uncovered pixels keep stock alpha");
        check(near(owned.M),path+" velocity Target3 stays correct beside the pack attachments");
        var sync=Read(false);check(sync.A.SequenceEqual(owned.A)&&sync.G1.SequenceEqual(owned.G1)&&sync.M.SequenceEqual(owned.M),path+" attachment, GBuffer1.a and velocity outputs agree across recording modes");
    }
    // DX11 pack attachments: fields at SV_Target4+slot and packed GBuffer1.a, written by the owning GBuffer invocation.
    static int AttachmentVelocityRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.gbuffer-attachments";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&8)!=0,"native owner extension advertises the GBuffer velocity contract");
        var stages=core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true);stages.GetField("PreparationBound",stat).SetValue(null,true);
        var catalog=backend.PostprocessCatalog().Where(row=>(uint)row[0]==4096||(uint)row[0]==4097).ToArray();Check(catalog.Length==2,"fixture selects the rigid classic GBuffer owner pair");
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(955001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(955002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(955003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(955004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        uint frame=0;int objectSlot=-1;
        (byte[] G1,float[] M,byte[] A) Frame(int step,bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(0,955001,955002,955003,955004,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(.1*step,0,0),Vector3.One,Vector3.Zero,1,0),null,1,512|16384,0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"attachment frame step="+step+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            return(backend.ReadbackWorldGbuffer(1,64,64),backend.ReadbackMotion(64,64),objectSlot>=0?backend.ReadbackAttachment((uint)objectSlot,64,64,4):null);
        }
        (byte[] G1,float[] M,byte[] A) Run(bool pipelined=false){(byte[],float[],byte[]) last=default;for(int step=0;step<4;++step)last=Frame(step,pipelined&&step==3);return last;}
        int centre=32*64+32,corner=2*64+2;
        string root=Path.Combine(output,"GBufferAttachmentPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Geometry/Passes/GBuffer/PixelStage.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),AttachmentManifest(owner));
        string patched=VelocityGBufferPixelStage(core,bin);
        string pack=AttachmentPixelStage(patched);
        Check(pack.Contains("output.objectid = 777u;")&&pack.Contains("#define ANOMALY_GBUFFER1A_OVERRIDE"),"pack writes a named attachment and overrides packed GBuffer1.a in the velocity-aware pixel stage");
        try{
            var stock=Run();int stockAlpha=stock.G1[centre*4+3];Console.WriteLine("ATTACH stock gbuffer1.a="+stockAlpha);
            File.WriteAllText(file,pack);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
            objectSlot=AttachmentSlot(core,out int objectFormat);Check(objectSlot>=0&&objectFormat==4,"core admits the R32_UINT attachment to a DX11 SV_Target4..6 slot");
            var owned=Run(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"attachment-writing GBuffer owner prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            uint id=BitConverter.ToUInt32(owned.A,centre*4),outside=BitConverter.ToUInt32(owned.A,corner*4);int alpha=owned.G1[centre*4+3];Console.WriteLine("ATTACH owned objectid="+id+" outside="+outside+" gbuffer1.a="+alpha+" slot="+objectSlot);
            Check(id==777&&outside==0,"owning GBuffer invocation writes the named attachment at SV_Target"+(4+objectSlot)+"; uncovered pixels stay cleared");
            Check(Math.Abs(alpha-128)<=1&&owned.G1[corner*4+3]==stock.G1[corner*4+3],"packed GBuffer1.a carries the pack value from the owning invocation; uncovered pixels keep stock alpha");
            var state=backend.GetPostprocessGeneration(4097);Check(state.Active!=0&&state.Executions>0&&state.LastFrame==frame,"attachment-writing owner pair executes this frame");
            Check(owned.M[centre*2]<-1&&owned.M[centre*2]>-1.2&&owned.M[corner*2]==0,"velocity Target3 stays correct beside the pack attachments");
            var sync=Run();Check(sync.A.SequenceEqual(owned.A)&&sync.G1.SequenceEqual(owned.G1)&&sync.M.SequenceEqual(owned.M),"attachment, GBuffer1.a and velocity outputs agree across recording modes");
            Call("Backend.GuestPassList","UnregisterRoot",owner);objectSlot=-1;var restored=Run(true);
            Check(restored.G1.SequenceEqual(stock.G1)&&restored.M.SequenceEqual(stock.M),"unregister restores exact stock GBuffer1 and motion bits");
            Run();Check(backend.GetPostprocessGeneration(4097).Retired==0,"attachment owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"pack attachment owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
}
