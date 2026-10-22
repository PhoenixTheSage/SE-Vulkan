using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int ProbeProcessingGenerationRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.probe.processing",catalog=owner+".image";
        int checks=0;var failures=new List<string>();void Check(bool ok,string text){checks++;Console.WriteLine((ok?"PASS ":"FAIL ")+text);if(!ok)failures.Add(text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        using(var file=File.OpenRead(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"))){var image=DdsImage.Read(file,true);backend.UploadTextureMips(955001,image.Width,image.Height,image.Format,image.Mips,false);}
        var oldConfig=backend.CurrentProbeConfig;var probes=new VulkanProbes{Resolution=16};probes.Settings.TimeOut=0;float time=0;uint frame=0;
        byte[][] Frame(bool pipelined=false,bool hold=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();time+=.1f;backend.SetProbeAtmosphere(0,0,0,Vector3.Forward);probes.Configure(backend,Vector3D.Zero,light,MyPostprocessSettings.Default,MatrixD.Identity,955001,.1f,time);
            if(hold){var config=backend.CurrentProbeConfig;config.Flags=1|4;backend.SetProbes(config);}
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"probe processing frame begins; pipelined="+pipelined+" hold="+hold);Check(Call("Backend.GuestFrame","Capture") is true,"probe processing captures immutable frame");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            return Enumerable.Range(0,144).Select(i=>backend.ReadbackProbe((uint)(i/24),(uint)(i%24/4),(uint)(i%4),16)).ToArray();
        }
        bool Same(byte[][] a,byte[][] b)=>a.Zip(b,(x,y)=>x.SequenceEqual(y)).All(v=>v);
        void Pixels(byte[][] data,float red,float green,float blue){
            for(int kind=0;kind<6;++kind)for(int face=0;face<6;++face)for(int mip=0;mip<4;++mip){
                float[] expected=kind<2||mip==0?new[]{red+.125f,green+.125f,blue+.125f}:new[]{(face+1)/8f,(16>>mip)/16f,red};if(kind>=4)expected[2]+=(face+1)*.125f;
                byte[] bytes=data[kind*24+face*4+mip];bool ok=Enumerable.Range(0,bytes.Length/8).All(i=>Enumerable.Range(0,3).All(c=>Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8+c*2))-expected[c])<.002f));
                Check(ok,"probe cube"+kind+" face"+face+" mip"+mip+" matches independent processing equation "+string.Join(",",expected));
            }
        }
        string root=Path.Combine(output,"ProbeProcessingPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string path,string text){string file=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text);}
        void Register()=>Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
        const string forward="Texture2D<float4> Input:register(t7);cbuffer View:register(b2){row_major float4x4 V;row_major float4x4 P;float3 Sun;float Size;}cbuffer Extras:register(b6){float2 RenderSize;}float3 __pixel_shader():SV_Target0{return Input.Load(int3(0,0,0)).rgb+float3(Size/16*.125,RenderSize.x/64*.125,PROBE_BLUE);}";
        const string prefilter="Texture2D<float4> Input:register(t7);cbuffer Config:register(b1){uint Samples;uint Resolution;uint MipSize;uint Face;float Gloss;}float3 __pixel_shader():SV_Target0{return float3((Face+1)/8.0,MipSize/16.0,Input.Load(int3(0,0,0)).r);}";
        string Copy(bool dormant=false){var s=new System.Text.StringBuilder();for(int i=0;i<6;++i)s.Append("Texture2D<float4> Source"+i+":register(t"+i+");");s.Append("struct Out{");for(int i=0;i<6;++i)s.Append("float3 v"+i+":SV_Target"+i+";");s.Append("};void __pixel_shader(float4 pos:SV_Position,out Out o){");for(int i=0;i<6;++i)s.Append("o.v"+i+"="+(dormant?"float3(16,16,16)":"Source"+i+".Load(int3(int2(pos.xy),0)).rgb+float3(0,0,"+((i+1)*.125f).ToString(System.Globalization.CultureInfo.InvariantCulture)+")")+";");return s+"}";}
        try {
            Check((GuestBridge.SupportedPacketFlags&134217728)!=0,"probe processing owner generations advertise bit27");var stock=Frame();Check(Same(stock,Frame(true)),"stock probe processing is deterministic across all six cubes, faces and mips");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,catalog,1,1,10) is true,"probe processing catalog target accepted");Call("Shaders.ShaderBindRegistry","RequestSrv","EnvProbe",catalog,7);Call("Buffers.CatalogImages","ClearColorTarget",catalog,.25f,.5f,.75f,0f);
            Write("anomaly.json","{\"id\":\""+owner+"\"}");Write("Inject/EnvProbe/Constants.hlsli","#define PROBE_BLUE .125\n");Write("Overlay/EnvProbe/ForwardPostprocess.hlsl",forward);Write("Overlay/EnvProbe/EnvPrefiltering.hlsl",prefilter);Write("Overlay/EnvProbe/EnvProbeCopy.hlsl",Copy());Write("Overlay/EnvProbe/EnvProbeBlend.hlsl",Copy(true));Register();var first=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"probe processing generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Pixels(first,.25f,.5f,.75f);
            var a=backend.GetPostprocessGeneration(1280);Check(a.Active!=0&&Enumerable.Range(1280,4).All(i=>backend.GetPostprocessGeneration((uint)i).Active==a.Active),"all four selected processing generations activate atomically");Check(a.Executions==36&&backend.GetPostprocessGeneration(1281).Executions==12&&backend.GetPostprocessGeneration(1282).Executions==8,"native prefilter, sky and copy record their actual face/mip invocations");
            Check(Enumerable.Range(1280,3).All(i=>{var s=backend.GetPostprocessGeneration((uint)i);return s.LastFrame==frame&&s.LastCompletion<=s.Completed;}),"all invoked probe owners report current-frame completed execution");Check(backend.GetPostprocessGeneration(1283).Executions==0,"stock-disabled factor-blend generation has no invented GPU execution");
            Check(Same(first,Frame()),"all probe outputs match exactly between synchronous and pipelined execution");Check(backend.GetStageBindingState().Resolved>=48,"prefilter and sky invocations resolve frame-copied catalog inputs");
            var before=backend.GetPostprocessGeneration(1280);Check(Same(first,Frame(true,true)),"holding probe capture preserves produced outputs");Check(backend.GetPostprocessGeneration(1280).Executions==before.Executions&&backend.GetPostprocessGeneration(1280).LastFrame!=frame&&backend.GetPostprocessGeneration(1282).LastFrame==frame,"held capture distinguishes inactive prefilter from the executing publication copy");
            Call("Buffers.CatalogImages","ClearColorTarget",catalog,.5f,.25f,.125f,0f);var changed=Frame();Pixels(changed,.5f,.25f,.125f);
            var state=Call("Shaders.ShaderPackRegistry","GetNativeStageState","EnvProbe");var variants=((System.Collections.IEnumerable)state.GetType().GetProperty("Variants").GetValue(state)).Cast<object>().ToArray();Check(variants.Length==4&&variants.Where(v=>(uint)v.GetType().GetProperty("Variant").GetValue(v)<1283).All(v=>(ulong)v.GetType().GetProperty("Executions").GetValue(v)>0),"public probe stage state exposes executed owners and separate dormant variant");
            Write("Overlay/EnvProbe/EnvPrefiltering.hlsl","BrokenSyntax");Register();Check(Same(changed,Frame(true))&&Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(1280).Active==a.Active,"failed probe compilation retains the prior complete owner generation");
            Write("Overlay/EnvProbe/EnvPrefiltering.hlsl",prefilter);Write("Overlay/EnvProbe/EnvProbeCopy.hlsl","float3 __pixel_shader():SV_Target0{return 0;}");Register();Check(Same(changed,Frame())&&(Property("Backend.GuestPostprocessStages","Error") as string)?.Contains("output missing")==true&&backend.GetPostprocessGeneration(1282).Active==a.Active,"missing MRT rejects the candidate and preserves every previous output");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(Same(stock,Frame(true)),"unregister restores exact stock probe processing on all cubes/faces/mips");Frame();Check(backend.GetPostprocessGeneration(1280).Retired==0,"probe processing pipeline/layout generations retire after completed submissions");Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"probe processing owners have zero core/synchronization errors");
        } finally {
            Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);if(oldConfig.Size==8096&&oldConfig.Frames!=null)backend.SetProbes(oldConfig);else backend.SetProbes(new NativeProbeConfig{Size=8096,AbiVersion=NativeBackend.AbiVersion,Resolution=16,Frames=new byte[7680],Projections=new float[96]});
        }
        if(failures.Count!=0)throw new Exception("Probe processing contract failures: "+string.Join("; ",failures));return checks;
    }
}
