using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage.Import;
using VRageMath;
using VRageRender;
partial class Program
{
    static int OitGenerationRegression(Assembly core,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.oit",catalog=owner+".input";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-2,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2}};}
        backend.UploadWorldMesh(958001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(958002,1,1,0,new byte[]{255,255,255,255},false);var material=new byte[96];Buffer.BlockCopy(new[]{.25f,.5f,.75f,.5f},0,material,16,16);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;light.AmbientDiffuseFactor=0;light.SunDiffuseFactor=0;
        uint frame=0;float[] Frame(bool pipelined=false,bool draw=true){Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);if(draw)backend.AddGlassPacket(0,958001,958002,958002,WorldConstants.Object(backend.GlassLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),material);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"OIT owner frame begins; pipelined="+pipelined+" draw="+draw);Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return backend.ReadbackLightingStage(3,64,64);}
        int at=(32*64+32)*4;void Pixel(float[] data,float r,float g,float b){Console.WriteLine("OIT HDR="+string.Join(",",data.Skip(at).Take(4)));Check(Math.Abs(data[at]-r)<.015&&Math.Abs(data[at+1]-g)<.015&&Math.Abs(data[at+2]-b)<.015,"native OIT resolve matches independent HDR blend equation");}
        string root=Path.Combine(output,"OitPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/Transparent/OIT"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");string file=Path.Combine(root,"Overlay/Transparent/OIT/Resolve.hlsl");
        const string shader="Texture2D<float4> Accum:register(t0);Texture2D<float> Coverage:register(t1);Texture2D<float4> Input:register(t7);cbuffer Extras:register(b6){float2 RenderSize;}float4 __pixel_shader(float4 pos:SV_Position):SV_Target{int2 p=pos.xy;float4 a=Accum.Load(int3(p,0));float reveal=Coverage.Load(int3(p,0));return float4(a.rgb/max(a.a,1e-3)*(1-reveal)+Input.Load(int3(0,0,0)).rgb*RenderSize.x/64,reveal);}";
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            Check((GuestBridge.SupportedPacketFlags&1073741824)!=0,"native OIT resolve generations advertise bit30");backend.SetHdrQuality(true);var stock=Frame();Pixel(stock,.25f,.5f,.75f);Check(stock.SequenceEqual(Frame(true)),"stock OIT output is exact between recording modes");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,catalog,1,1,10) is true,"OIT catalog target accepted");Call("Shaders.ShaderBindRegistry","RequestSrv","Transparent",catalog,7);Call("Buffers.CatalogImages","ClearColorTarget",catalog,.25f,.125f,.0625f,0f);Register(shader);var first=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"OIT generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Pixel(first,.5f,.625f,.8125f);var state=backend.GetPostprocessGeneration(2048);Check(state.Active!=0&&state.Executions==1&&state.LastFrame==frame&&state.LastCompletion<=state.Completed,"selected native OIT shader executes and completes this frame");Check(backend.GetStageBindingState().Resolved>=1,"OIT native invocation resolves catalog bindings and extras");
            Check(first.SequenceEqual(Frame()),"OIT replacement output agrees exactly between recording modes");backend.SetHdrQuality(false);Pixel(Frame(true),.5f,.625f,.8125f);backend.SetHdrQuality(true);Pixel(Frame(),.5f,.625f,.8125f);
            ulong count=backend.GetPostprocessGeneration(2048).Executions;Frame(true,false);Check(backend.GetPostprocessGeneration(2048).Executions==count&&backend.GetPostprocessGeneration(2048).LastFrame!=frame,"active OIT generation has no invented execution in a frame without transparency");
            Call("Buffers.CatalogImages","ClearColorTarget",catalog,.5f,.25f,.125f,0f);Pixel(Frame(),.75f,.75f,.875f);ulong previous=backend.GetPostprocessGeneration(2048).Active;Register("BrokenSyntax");Pixel(Frame(true),.75f,.75f,.875f);Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(2048).Active==previous,"failed OIT compilation retains the previous HDR pair and layout");Register("float3 __pixel_shader():SV_Target{return 0;}");Pixel(Frame(),.75f,.75f,.875f);Check((Property("Backend.GuestPostprocessStages","Error") as string)?.Contains("preserve alpha")==true&&backend.GetPostprocessGeneration(2048).Active==previous,"missing blend alpha refuses before native activation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(stock.SequenceEqual(Frame(true)),"unregister restores exact stock OIT output");Frame();Check(backend.GetPostprocessGeneration(2048).Retired==0,"OIT HDR pipelines and descriptor layouts retire after completion");Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"OIT owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetHdrQuality(true);}
        return checks;
    }
}
