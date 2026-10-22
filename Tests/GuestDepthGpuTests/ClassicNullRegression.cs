using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int ClassicNullRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.classic.null",name=owner+".input";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var programs=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programs)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        var probes=new[]{
            new[]{"2d","Texture2D<float4> I:register(t7);","uint w,h;I.GetDimensions(w,h);float4 v=I.Load(int3(0,0,0));return float4(w,h,v.r,v.a);"},
            new[]{"3d","Texture3D<float4> I:register(t7);","uint w,h,d;I.GetDimensions(w,h,d);return float4(w,h,d,I.Load(int4(0,0,0,0)).r);"},
            new[]{"array","Texture2DArray<float4> I:register(t7);","uint w,h,d;I.GetDimensions(w,h,d);return float4(w,h,d,I.Load(int4(0,0,0,0)).r);"},
            new[]{"structured","StructuredBuffer<float4> I:register(t7);","uint n,s;I.GetDimensions(n,s);return float4(n,s,I[0].x,I[1].y);"},
            new[]{"typed","Buffer<float4> I:register(t7);","uint n;I.GetDimensions(n);return float4(n,I[0].xyz);"},
            new[]{"sample","Texture2D<float4> I:register(t7);SamplerState S:register(s0);","return I.SampleLevel(S,float2(.5,.5),0);"}
        };
        var instances=new Dictionary<string,object>();object upload=null;
        void Frame(int mode,bool pipelined){
            backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"classic resource frame begins; mode="+mode+" pipelined="+pipelined);
            Check(Call("Backend.GuestFrame","Capture") is true,"classic resources capture immutable frame metadata");Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            foreach(var pair in instances){
                ulong image=(ulong)programs.GetField("StorageImage").GetValue(pair.Value);var data=new byte[64*64*8];read.Invoke(backend,new object[]{image,data});
                float[] actual=Enumerable.Range(0,4).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,i*2))).ToArray();
                var expected=pair.Key=="structured"?new float[]{0,16,0,0}:new float[4];
                if(mode==1&&pair.Key=="2d")expected=new[]{1f,1f,95f/255,1f};
                if(mode==1&&pair.Key=="sample")expected=new[]{95f/255,0f,0f,1f};
                if(mode==2&&pair.Key=="3d")expected=new[]{2f,2f,2f,123f/255};
                if(mode==3&&pair.Key=="structured")expected=new[]{2f,16f,3f,8f};
                Console.WriteLine("classic "+pair.Key+" mode="+mode+" actual="+string.Join(",",actual));
                Check(actual.Zip(expected,(a,b)=>Math.Abs(a-b)<.002f).All(v=>v),"classic "+pair.Key+" dimensions/values match DX11 resource-type/null semantics; mode="+mode);
                Check((ulong)programs.GetField("Submissions").GetValue(pair.Value)>0,"classic "+pair.Key+" records actual production submission");
            }
        }
        try {
            Check((GuestBridge.SupportedPacketFlags&8388608)!=0,"native host advertises bit23 classic null resources");
            foreach(var probe in probes){
                string id=owner+"."+probe[0],file=Path.Combine(output,"ClassicNull-"+probe[0]+".hlsl");File.WriteAllText(file,probe[1]+"float4 main():SV_Target{"+probe[2]+"}");
                Call("Backend.GuestPassList","AddProgram",id,"AfterLighting",file,"PublishOnly");Call("Backend.GuestPassList","SetFlags",id,0,1);
                Check(Call("Backend.GuestPassList","RequestSrv",id,name,7) is true,"classic "+probe[0]+" reserves optional catalog input");
            }
            Call("Backend.GuestCompiler","CompilePending",assets);
            foreach(var probe in probes){var program=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)programs.GetField("Id").GetValue(p)==owner+"."+probe[0]);instances.Add(probe[0],program);Check((ulong)programs.GetField("Pipeline").GetValue(program)!=0,"classic "+probe[0]+" compiles through the production compiler: "+programs.GetField("Error").GetValue(program));}
            Frame(0,false);Frame(0,true);
            upload=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));var published=upload.GetType();
            published.GetMethod("PublishRgba8").Invoke(upload,new object[]{new byte[]{95,0,0,255},1,1});Check(Call("Buffers.BufferCatalog","Publish",owner,name,upload) is true,"classic resource input publishes through Anomaly");Frame(1,true);
            published.GetMethod("PublishVolumeRgba8").Invoke(upload,new object[]{Enumerable.Range(0,8).SelectMany(_=>new byte[]{123,0,0,255}).ToArray(),2,2,2});Frame(2,false);
            published.GetMethod("PublishStructuredFloats").Invoke(upload,new object[]{new float[]{3,4,5,6,7,8,9,10},16});Frame(3,true);
            Call("Buffers.BufferCatalog","UnpublishAll",owner);Frame(0,false);
            var capabilities=Call("Shaders.ShaderPackRegistry","GetCapabilities");
            Check(capabilities.GetType().GetProperty("ClassicNullResourcesSupported").GetValue(capabilities) is true,"public capabilities expose device-qualified classic null resources");
            var stats=backend.GetStats();Check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"classic resources have zero core/synchronization errors");
        } finally { Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);upload?.GetType().GetMethod("Clear").Invoke(upload,null); }
        return checks;
    }
    static int OldClassicNullHostRegression(Assembly core,string assets,string output,NativeBackend backend)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.classic.old";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        Check((GuestBridge.SupportedPacketFlags&8388608)==0&&(GuestBridge.SupportedPacketFlags&4194304)!=0,"older native host has bit22 but lacks classic-resource bit23");
        try {
            foreach(var pair in new[]{new[]{"needed","Texture2D<float4> I:register(t7);float4 main():SV_Target{uint w,h;I.GetDimensions(w,h);return float4(w,h,I.Load(int3(0,0,0)).ra);}"},new[]{"optimized","Texture2D<float4> Unused:register(t7);float4 main():SV_Target{return float4(1,2,3,4);}"}}){
                string id=owner+"."+pair[0],file=Path.Combine(output,pair[0]+".hlsl");File.WriteAllText(file,pair[1]);Call("Backend.GuestPassList","AddProgram",id,"AfterLighting",file,"PublishOnly");
            }
            Call("Backend.GuestCompiler","CompilePending",assets);
            foreach(var program in ((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Where(p=>((string)p.GetType().GetField("Id").GetValue(p)).StartsWith(owner+"."))){
                var t=program.GetType();string id=(string)t.GetField("Id").GetValue(program),status=(string)t.GetField("Status").GetValue(program),error=(string)t.GetField("Error").GetValue(program);ulong pipeline=(ulong)t.GetField("Pipeline").GetValue(program);
                Check(t.GetField("PixelSpirv").GetValue(program)!=null,"older-host check uses actually compiled shader resources: "+id);
                if(id.EndsWith("needed"))Check(pipeline==0&&status=="capability-missing"&&error.Contains("bit23"),"older host refuses classic optional resources before native pipeline allocation with update guidance");
                else Check(pipeline!=0&&status=="pipeline","optimized-out SRV does not needlessly refuse a resource-free program on older hosts");
            }
            var caps=Call("Shaders.ShaderPackRegistry","GetCapabilities");Check(caps.GetType().GetProperty("ClassicNullResourcesSupported").GetValue(caps) is false,"older-host public null capability remains false");
            Check(backend.GetStats().ValidationErrors==0,"older classic-resource refusal reports zero validation errors");
        } finally {Call("Backend.GuestPassList","UnregisterRoot",owner);}
        return checks;
    }

}
