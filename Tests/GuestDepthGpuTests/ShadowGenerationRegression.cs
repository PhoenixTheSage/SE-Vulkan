using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
partial class Program
{
    static int ShadowGenerationRegression(Assembly core,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.shadows",catalog=owner+".input";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        backend.SetShadowCascades(Enumerable.Range(0,8).SelectMany(_=>NativeBackend.MatrixValues(MatrixD.Identity)).ToArray(),Enumerable.Range(1,8).Select(i=>(float)i*10).ToArray());backend.SetShadowSchedule(true,255,255,true);backend.SetShadowOptions(false);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        uint frame=0;byte[] Frame(bool pipelined=false,bool usage=true){Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();backend.SetShadowSchedule(true,255,255,usage);post.Configure(backend,64,64,true,view:MatrixD.Identity,projection:projection);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"shadow generation frame begins; pipelined="+pipelined);Check(Call("Backend.GuestFrame","Capture") is true,"shadow generation captures immutable frame");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return backend.ReadbackLightingInput(1,64,64);}
        string root=Path.Combine(output,"ShadowPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string file,string text){string path=Path.Combine(root,file);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text);}
        void Register()=>Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
        const string shader="Texture2D<float4> Input:register(t7);cbuffer Extras:register(b6){float2 RenderSize;}RWTexture2D<unorm float> Output:register(u0);[numthreads(16,16,1)]void __compute_shader(uint3 id:SV_DispatchThreadID){if(all(id.xy<(uint2)RenderSize))Output[id.xy]=Input.Load(int3(0,0,0)).r+SHADOW_BIAS;}";
        const string stats="Texture2D<float4> Input:register(t7);cbuffer Screen:register(b2){float2 Offset;float2 Size;}cbuffer Extras:register(b6){float2 RenderSize;}RWStructuredBuffer<uint> Stats:register(u1);[numthreads(16,16,1)]void __compute_shader(uint3 id:SV_DispatchThreadID){if(all(id.xy<(uint2)Size/2))InterlockedAdd(Stats[(uint)Input.Load(int3(0,0,0)).g],(uint)(RenderSize.x/64));}";
        void Pixels(byte[] data,float value){Check(data.All(v=>Math.Abs(v-value*255)<=1),"native CSM output matches independently expected UNORM value "+value);}
        try{
            Check((GuestBridge.SupportedPacketFlags&536870912)!=0,"native shadow generations advertise bit29");var stock=Frame();Check(stock.SequenceEqual(Frame(true)),"stock CSM output is deterministic across recording modes");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,catalog,1,1,10) is true,"shadow catalog input target accepted");Call("Shaders.ShaderBindRegistry","RequestSrv","Shadows",catalog,7);Call("Buffers.CatalogImages","ClearColorTarget",catalog,.25f,2f,0f,0f);Write("anomaly.json","{\"id\":\""+owner+"\"}");Write("Inject/Shadows/Constants.hlsli","#define SHADOW_BIAS .125\n");Write("Overlay/Shadows/Shadows.hlsl",shader);Write("Overlay/Shadows/ShadowStats.hlsl",stats);Register();var first=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"native shadow generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Pixels(first,.375f);Check(first.SequenceEqual(Frame()),"shadow replacement outputs match exactly between recording modes");
            var a=backend.GetPostprocessGeneration(1792);var b=backend.GetPostprocessGeneration(1793);Check(a.Active!=0&&a.Active==b.Active&&a.Executions>0&&b.Executions>0&&a.LastFrame==frame&&b.LastFrame==frame&&a.LastCompletion<=a.Completed&&b.LastCompletion<=b.Completed,"both shadow owners activate atomically and report completed current-frame execution");
            Frame();Frame(true);var feedback=backend.GetShadowFeedback();Console.WriteLine("shadow feedback="+string.Join(",",feedback.Pixels));Check(feedback.Pixels[2]==1024&&feedback.Pixels.Where((v,i)=>i!=2).All(v=>v==0),"actual usage shader writes exact per-view cascade feedback from scope b2 and catalog input");Check(backend.GetStageBindingState().Resolved>=2,"CSM and usage owners resolve frame-copied reserved resources");
            ulong usageCount=backend.GetPostprocessGeneration(1793).Executions;Pixels(Frame(true,false),.375f);Check(backend.GetPostprocessGeneration(1793).Executions==usageCount&&backend.GetPostprocessGeneration(1793).LastFrame!=frame&&backend.GetPostprocessGeneration(1792).LastFrame==frame,"disabled usage owner remains inactive while CSM evaluation executes");
            Call("Buffers.CatalogImages","ClearColorTarget",catalog,.5f,3f,0f,0f);Pixels(Frame(),.625f);Write("Overlay/Shadows/Shadows.hlsl","BrokenSyntax");Register();Pixels(Frame(true),.625f);Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(1792).Active==a.Active,"compile failure preserves both active shadow owners");
            Write("Overlay/Shadows/Shadows.hlsl",shader.Replace("numthreads(16,16,1)","numthreads(8,8,1)"));Register();Pixels(Frame(),.625f);Check((Property("Backend.GuestPostprocessStages","Error") as string)?.Contains("compute interface")==true&&backend.GetPostprocessGeneration(1792).Active==a.Active,"wrong shadow workgroup rejects before activation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(stock.SequenceEqual(Frame(true)),"unregister restores exact stock CSM output");Frame();Check(backend.GetPostprocessGeneration(1792).Retired==0,"shadow compute pipelines/layouts retire after completed submission");Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"shadow owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetShadowSchedule(false,255,255);}
        return checks;
    }
}
