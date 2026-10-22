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
        var catalog=backend.PostprocessCatalog().Where(row=>(uint)row[0]>=4096&&(uint)row[0]<4106).ToArray();Check(catalog.Length==10,"fixture selects all five admitted classic GBuffer stream pairs");
        stages.GetField("catalog",stat).SetValue(null,catalog);stages.GetField("catalogEpoch",stat).SetValue(null,Property("Backend.GuestBridgeClient","ResourceEpoch"));
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(958001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(958002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(958003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(958004,1,1,0,new byte[]{255,1,0,0},false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        float[] Bones(){var data=new float[960];for(int i=0;i<60;++i)Array.Copy(NativeBackend.MatrixValues(MatrixD.Identity),0,data,i*16,16);return data;}
        byte[] Stream(uint type){if(type==2){var bytes=new byte[64];Buffer.BlockCopy(new[]{1f,0f,0f,0f,0f,1f,0f,0f,0f,0f,1f,0f,0f,0f,0f,0f},0,bytes,0,64);return bytes;}if(type<3)return null;var cube=new MyCubeInstanceData{LocalMatrix=Matrix.Identity};if(type==4){cube.EnableSkinning=true;cube.BoneRange=1;for(int b=0;b<9;++b)cube[b]=new Vector3UByte(127,127,127);}var packed=new byte[64];for(int b=0;b<8;++b){var v=cube.GetPackedBone(b);packed[b*4]=v.X;packed[b*4+1]=v.Y;packed[b*4+2]=v.Z;packed[b*4+3]=v.W;}var t=cube.PackedOrthoMatrix;Buffer.BlockCopy(new[]{t.X,t.Y,t.Z,t.W,0f,0f,0f,0f},0,packed,32,32);return packed;}
        uint[] instanced={2,3,4};uint frame=0;
        (byte[] G,float[] M) Frame(int step,bool pipelined=false,uint pipeline=0){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.AddWorldPacket(pipeline,958001,958002,958003,958004,0,WorldConstants.Object(backend.WorldLayouts[pipeline],MatrixD.CreateTranslation(.1*step,0,0),Vector3.One,Vector3.Zero,1,0,pipeline==1?Bones():null),Stream(pipeline),1,512|16384,0);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
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
            Register(magenta.Replace("float2 anomaly_velocity = input.anomaly_velocity;","float2 anomaly_velocity = float2(3, -2);"));var custom=Run(true);Console.WriteLine("VELOCITY custom="+Motion(custom.M,centre));
            Check(Property("Backend.GuestPostprocessStages","Error")==null&&custom.M[centre]==3&&custom.M[centre+1]==-2&&custom.M[corner]==0&&Magenta(custom.G),"pack-written velocity reaches Target3 from the owning invocation; replay skips that draw");
            foreach(uint id in instanced){var stream=Run(true,id);Check(stream.M[centre]==3&&stream.M[centre+1]==-2&&stream.M[corner]==0&&Magenta(stream.G),"pack-written velocity reaches Target3 from the instanced/cube owning invocation id"+id);}
            var skinnedOwned=Run(true,1);Check(Magenta(skinnedOwned.G)&&skinnedOwned.M[centre]==3&&skinnedOwned.M[centre+1]==-2&&skinnedOwned.M[corner]==0,"pack-written velocity reaches Target3 from the skinned owning invocation");
            Check(Run().M.SequenceEqual(custom.M),"pack-written velocity agrees across recording modes");
            ulong token=backend.GetPostprocessGeneration(4097).Active;Register("BrokenSyntax");var broken=Run();Console.WriteLine("VELOCITY broken="+Motion(broken.M,centre)+" error="+Property("Backend.GuestPostprocessStages","Error")+" active="+backend.GetPostprocessGeneration(4097).Active+" was="+token);
            Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(4097).Active==token&&broken.M[centre]==3,"failed compilation retains the previous velocity owner generation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Run(true);
            Check(restored.M.SequenceEqual(stock.M)&&restored.G.SequenceEqual(stock.G),"unregister restores exact stock material and replay motion bits");
            foreach(uint id in instanced){var stream=Run(true,id);Check(stream.M.SequenceEqual(stockStreams[id].M)&&stream.G.SequenceEqual(stockStreams[id].G),"unregister restores exact stock material and replay motion id"+id);}
            {var stream=Run(true,1);Check(stream.M.SequenceEqual(skinned.M)&&stream.G.SequenceEqual(skinned.G),"unregister restores exact stock skinned material and replay motion");}
            Run();Check(backend.GetPostprocessGeneration(4097).Retired==0,"velocity owner bundles retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"GBuffer velocity owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);stages.GetField("catalog",stat).SetValue(null,null);stages.GetField("catalogEpoch",stat).SetValue(null,-1L);}
        return checks;
    }
}
