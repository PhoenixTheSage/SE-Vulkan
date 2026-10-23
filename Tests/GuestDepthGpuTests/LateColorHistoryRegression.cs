using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int LateColorHistoryRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.late.history",callbackId=owner+".callback",target=owner+".callback.output";
        int checks=0,nonzeroHistoryReads=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        object Add(string id,string slot,string source,string compose){
            string file=Path.Combine(output,id+".hlsl");File.WriteAllText(file,source);
            Call("Backend.GuestPassList","AddProgram",id,slot,file,compose);Call("Backend.GuestPassList","SetFlags",id,0,1);
            return ((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)==id);
        }
        void Set(object p,string field,object value)=>p.GetType().GetField(field).SetValue(p,value);
        ulong Image(object p)=>(ulong)p.GetType().GetField("StorageImage").GetValue(p);
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        float Red(ulong image,int width,int height){var data=new byte[width*height*8];read.Invoke(backend,new object[]{image,data});return (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,0));}
        string reader="Texture2D<float4> Previous:register(t7);float4 main(float4 p:SV_Position):SV_Target{return float4(Previous.Load(int3(0,0,0)).rgb,1);}";
        object writer=Add(owner+".writer","BeforeTonemap","cbuffer Inputs:register(b7){float4 Value;}float4 main():SV_Target{return Value;}","Replace");
        object early=Add(owner+".early","AfterLighting",reader,"PublishOnly"),late=Add(owner+".late","AfterUpscale",reader,"PublishOnly"),callback=Add(owner+".reader","AfterUpscale",reader,"PublishOnly");
        Call("Backend.GuestCompiler","CompilePending",assets);Set(callback,"Enabled",false);
        Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,target,1,1,10) is true,"late history callback target requested");
        bool callbackAvailable=false;uint callbackFrame=0;int callbackWidth=0,callbackHeight=0;
        Call("Shaders.OwnedPassRegistry","RegisterPortable",callbackId,"AfterUpscale",500,0,(Action<object>)(context=>{
            callbackFrame=(uint)context.GetType().GetProperty("FrameIndex").GetValue(context);
            object image=context.GetType().GetMethod("Image").Invoke(context,new object[]{"historyColor"});callbackAvailable=image!=null&&image.GetType().GetProperty("IsAvailable").GetValue(image) is true;
            callbackWidth=callbackAvailable?(int)image.GetType().GetProperty("Width").GetValue(image):0;callbackHeight=callbackAvailable?(int)image.GetType().GetProperty("Height").GetValue(image):0;
            context.GetType().GetMethod("Fullscreen").Invoke(context,new object[]{owner+".reader",target,null});
        }),1);
        var programs=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programs)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        int outputW=64,outputH=64,previousWidth=0,previousHeight=0;uint frameIndex=0;float previous=0;bool produced=false,capture=true;
        void Frame(float current,bool pipelined,bool reject=false){
            Set(writer,"Uniforms",new float[]{current,0,0,1});backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(outputW,outputH);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"late history frame begins");
            Check(Call("Backend.GuestFrame","Capture") is true,"late history captures current immutable metadata");
            frameIndex=(uint)Call("Backend.GuestFrame","U32",16);bool valid=Property("Backend.GuestFrame","History") is true;
            Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            bool expectedValid=valid&&produced&&capture&&!reject&&previousWidth==size.X&&previousHeight==size.Y;
            float expected=expectedValid?previous:0;
            if(expected>0)nonzeroHistoryReads++;
            Console.WriteLine("late history frame="+frameIndex+" expected="+expected+" current="+current+" callbackAvailable="+callbackAvailable);
            Check(Math.Abs(Red(Image(early),size.X,size.Y)-expected)<.003,"early fullscreen sees exact prior color");
            Check(Math.Abs(Red(Image(late),outputW,outputH)-expected)<.003,"AfterUpscale fullscreen sees the same exact prior color");
            object destination=Call("Buffers.BufferCatalog","Active",target);ulong id=(ulong)destination.GetType().GetProperty("ImageId").GetValue(destination);
            Check(Math.Abs(Red(id,1,1)-expected)<.003,"AfterFullscreen portable callback samples prior color before capture");
            Check(callbackFrame==frameIndex&&callbackAvailable==expectedValid&&(!expectedValid||(callbackWidth==size.X&&callbackHeight==size.Y)),"portable callback exposes exact prior-history availability and raster dimensions");
            if(capture){
                object captured=Call("Buffers.BufferCatalog","Active","historyColor");
                Check(captured!=null&&(int)captured.GetType().GetProperty("Width").GetValue(captured)==size.X&&(int)captured.GetType().GetProperty("Height").GetValue(captured)==size.Y,"owned color history retains DX11 render dimensions independently of AfterUpscale output size");
                Check(Math.Abs(Red((ulong)captured.GetType().GetProperty("ImageId").GetValue(captured),size.X,size.Y)-current)<.003,"tail capture records the current HDR payload after prior-history consumers");
            }
            previous=current;produced=capture;previousWidth=size.X;previousHeight=size.Y;
        }
        try{
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",false);produced=false;capture=false;Frame(.0625f,false,true);
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",true);capture=true;
            Frame(.125f,true);Frame(.25f,false);Frame(.375f,true);
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",false);capture=false;Frame(.5f,true);Frame(.625f,false);
            Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",true);capture=true;Frame(.75f,true);Frame(.875f,false);
            outputW=95;outputH=73;backend.Resize(95,73,0);Frame(.125f,true,true);Frame(.25f,false);
            foreach(float fraction in new[]{.5f,.75f,1f}){backend.SetRenderFraction(fraction);Frame(.375f,true);Frame(.5f,false);}
        }finally{
            Call("Shaders.OwnedPassRegistry","Unregister",callbackId);Call("Backend.GuestPassList","SetEnabled","anomaly.owned.HistoryCopy",true);
            // AddProgram fixtures carry no owner, so UnregisterRoot cannot remove them; disable them so the
            // BeforeTonemap writer does not keep overwriting HDR in later combined-run sections.
            foreach(string suffix in new[]{".writer",".early",".late",".reader"}){Call("Backend.GuestPassList","SetEnabled",owner+suffix,false);Call("Backend.GuestPassList","UnregisterRoot",owner+suffix);}
            Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetRenderFraction(1);backend.Resize(64,64,0);
        }
        Check(nonzeroHistoryReads>=6,"late history recovers nonzero preceding-frame values across lifecycle changes");
        Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"late color history has zero core/synchronization errors");return checks;
    }
}
