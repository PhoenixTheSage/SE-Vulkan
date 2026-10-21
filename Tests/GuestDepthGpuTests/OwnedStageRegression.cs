using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int OwnedStageRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        object Program(string name)=>((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)=="anomaly.owned."+name);
        object history=Program("HistoryCopy");
        T Value<T>(object p,string field)=>(T)p.GetType().GetField(field).GetValue(p);
        string Generation()=>Value<string>(history,"Generation");
        ulong Pipeline()=>Value<ulong>(history,"Pipeline");
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        var listType=typeof(IReadOnlyList<>).MakeGenericType(history.GetType());
        var submitDelegate=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(listType),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        void Frame(bool pipelined)
        {
            backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"owned-stage replacement world frame begins pipelined="+pipelined);
            Check(Call("Backend.GuestFrame","Capture") is true,"owned-stage replacement captures actual frame metadata");
            Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestPassList","SubmitFrame",submitDelegate);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
        }
        void Pixel(float expected)
        {
            int w=Value<int>(history,"ImageWidth"),h=Value<int>(history,"ImageHeight");var bytes=new byte[w*h*8];read.Invoke(backend,new object[]{Value<ulong>(history,"StorageImage"),bytes});
            Check(w==64&&h==64&&Enumerable.Range(0,w*h).All(i=>Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8))-expected)<.002),"actual owned history output contains generation color "+expected);
        }
        string root=Path.Combine(output,"OwnedStagePack"),overlay=Path.Combine(root,"Overlay/Anomaly.HistoryColor");Directory.CreateDirectory(overlay);
        string file=Path.Combine(overlay,"HistoryCopy.hlsl");File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"fixture.actual.owned\"}");
        void Register(float value)
        {
            File.WriteAllText(file,"float4 __pixel_shader(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target{return float4("+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+",.25,.5,-1);}");
            Call("Backend.GuestPassList","RegisterRoot","fixture.actual.owned",root,null);Call("Backend.GuestNamedStages","Prepare",assets);
            Check(Property("Backend.GuestNamedStages","Prepared")!=null,"actual core prepares detached owned-stage generation: "+Property("Backend.GuestNamedStages","Error"));
        }
        try
        {
            ulong stock=Pipeline();Register(2);string a=(string)Property("Backend.GuestNamedStages","Prepared");
            Check(Pipeline()==stock&&Generation()==null,"actual core preparation preserves active stock before frame boundary");
            Frame(false);Pixel(2);Check(Generation()==a,"actual frame boundary activates requested A");
            var state=Call("Shaders.FullscreenPassRegistry","GetProgramState","anomaly.owned.HistoryCopy");
            Check((string)state.GetType().GetProperty("ActiveGeneration").GetValue(state)==a&&(ulong)state.GetType().GetProperty("Executions").GetValue(state)>0&&state.GetType().GetProperty("LastSubmissionCompleted").GetValue(state) is true,"actual public status associates completed GPU execution with active generation");
            // Keep a submission outstanding when replacement and retirement begin.
            Frame(true);ulong oldPipeline=Pipeline();ulong oldStorage=Value<ulong>(history,"StorageImage");
            Register(3);Check(Generation()==a&&Pipeline()==oldPipeline,"pipelined A remains active while B is prepared");
            string b=(string)Property("Backend.GuestNamedStages","Prepared");Frame(true);Pixel(3);
            Check(Generation()==b&&Value<ulong>(history,"StorageImage")==oldStorage,"pipelined boundary swaps pipelines while preserving the render target");
            Check(new[]{"CameraVelocity","LinearDepth","HiZ","HistoryCopy"}.All(name=>Value<string>(Program(name),"Generation")==b),"actual core atomically activates the full shared fullscreen dependency closure");
            File.WriteAllText(file,"#error actual-owned-rollback\n");Call("Backend.GuestPassList","RegisterRoot","fixture.actual.owned",root,null);Call("Backend.GuestNamedStages","Prepare",assets);
            Check(Property("Backend.GuestNamedStages","Prepared")==null&&((string)Property("Backend.GuestNamedStages","Error")).Contains("actual-owned-rollback"),"actual compiler refuses a malformed replacement");
            Frame(false);Pixel(3);Check(Generation()==b,"failed replacement retains the previous active generation on a live frame");
            Call("Backend.GuestPassList","UnregisterRoot","fixture.actual.owned");Call("Backend.GuestNamedStages","Prepare",assets);Frame(true);
            Check(Generation()!=b&&Property("Backend.GuestNamedStages","Error")==null,"actual unregister restores stock through a frame transaction");
            Check(backend.GetStats().ValidationErrors==0,"owned-stage synchronous/pipelined activation and retirement report zero validation errors");
        }
        finally{Call("Backend.GuestPassList","UnregisterRoot","fixture.actual.owned");}
        return checks;
    }
}
