using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int EarlyOwnedRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Add(string id,string source,int contract)
        {
            string file=Path.Combine(output,id+".hlsl");File.WriteAllText(file,source);
            Call("Backend.GuestPassList","AddProgram",id,"AfterLighting",file,"PublishOnly");
            var p=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(v=>(string)v.GetType().GetField("Id").GetValue(v)==id);
            p.GetType().GetField("ResourceContract").SetValue(p,contract);p.GetType().GetField("Enabled").SetValue(p,false);return p;
        }
        object producer=Add("fixture.early.producer","cbuffer Values:register(b7){float4 Value;}float4 main(float4 p:SV_Position):SV_Target{return Value;}",0);
        object consumer=Add("fixture.early.consumer","Texture2D<float4> Product:register(t0);float4 main(float4 p:SV_Position):SV_Target{return Product.Load(int3(0,0,0));}",1);
        object legacy=Add("fixture.early.null","Texture2D<float4> Product:register(t0);float4 main(float4 p:SV_Position):SV_Target{return Product.Load(int3(0,0,0));}",0);
        Call("Backend.GuestCompiler","CompilePending",assets);
        ulong Pipeline(object p)=>(ulong)p.GetType().GetField("Pipeline").GetValue(p);
        Check(Pipeline(producer)!=0&&Pipeline(consumer)!=0&&Pipeline(legacy)!=0,"early-product fixtures compile through actual Anomaly core");
        var create=typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit=typeof(NativeBackend).GetMethod("GuestSubmit",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit2=typeof(NativeBackend).GetMethod("GuestSubmit2",BindingFlags.Instance|BindingFlags.NonPublic);
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        var replay=typeof(NativeBackend).GetMethod("GuestReplayNow",BindingFlags.Instance|BindingFlags.NonPublic);
        const ulong source=7201,observed=7202,missing=7203;
        foreach(ulong id in new[]{source,observed,missing})create.Invoke(backend,new object[]{id,1,1,1,1});
        create.Invoke(backend,new object[]{7204UL,2,2,2,1});
        create.Invoke(backend,new object[]{7205UL,16,1,1,4});
        byte[] Values(float value){var bytes=new byte[16];Buffer.BlockCopy(new[]{value,.125f,.25f,-1f},0,bytes,0,16);return bytes;}
        float[] Pixels(ulong id){var bytes=new byte[8];read.Invoke(backend,new object[]{id,bytes});return Enumerable.Range(0,4).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*2))).ToArray();}
        void Write(int mark,ulong destination,int flags,float value)=>submit.Invoke(backend,new object[]{mark,5,1,1,Pipeline(producer),destination,Values(value),Array.Empty<ulong>(),flags});
        void Refuse(Action action,string message){bool refused=false;try{action();}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}Check(refused,message);}
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        for(int frame=0;frame<4;frame++)
        {
            backend.SetRenderFraction(new[]{1f,.5f,.75f,1f}[frame]);backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"early-product frame begins "+frame);GuestBridge.Reset();
            // Queue the consumer first: the public callback phase must nevertheless
            // receive this frame's early product, before the ordinary slot write.
            Check(submit2.Invoke(backend,new object[]{1,5,1,1,1,Pipeline(consumer),new[]{observed},null,new[]{source},1024}) is true,"BeforeFullscreen consumer accepted");
            submit.Invoke(backend,new object[]{1,5,1,1,Pipeline(legacy),missing,null,new[]{0UL},0});
            float expected=frame+1;Write(1,source,4096,expected);Write(1,source,0,.5f);
            if(frame==0)
            {
                Refuse(()=>Write(2,source,4096,9),"early producer refuses a public mark other than AfterLighting");
                Refuse(()=>Write(1,0xFFFF000000000001UL,4096,9),"early producer refuses renderer-owned HDR destinations");
                Refuse(()=>Write(1,source,4096|8,9),"early producer refuses incompatible mip flags");
                Refuse(()=>Write(1,7204,4096,9),"early producer refuses volume targets");
                Refuse(()=>Write(1,7205,4096,9),"early producer refuses storage-buffer targets");
            }
            if((frame&1)==0)backend.EndFrame();else backend.EndFrameAsync();
            var result=Pixels(observed);
            Check(BitConverter.ToUInt64(GuestBridge.ExecutionStatus(Pipeline(producer)),16)==(ulong)(2*(frame+1)),"early producer executes exactly once alongside the ordinary write, frame "+frame);
            Check(Math.Abs(result[0]-expected)<.002&&result[3]==-1,"public callback reads the current early product with negative alpha, frame "+frame);
            Check(Math.Abs(Pixels(source)[0]-.5f)<.002,"ordinary slot remains after public BeforeFullscreen, frame "+frame);
            Check(Pixels(missing).All(v=>v==0),"absent 2D texture samples deterministic zero including alpha, frame "+frame);
        }
        Refuse(()=>replay.Invoke(backend,new object[]{7}),"internal early replay mark is unavailable to public callers");
        Check(backend.GetStats().ValidationErrors==0,"early products, fallback clear and callback phases have zero validation errors");
        Call("Backend.GuestPassList","UnregisterRoot","fixture.early.producer");Call("Backend.GuestPassList","UnregisterRoot","fixture.early.consumer");Call("Backend.GuestPassList","UnregisterRoot","fixture.early.null");backend.SetRenderFraction(1);
        return checks;
    }
}
