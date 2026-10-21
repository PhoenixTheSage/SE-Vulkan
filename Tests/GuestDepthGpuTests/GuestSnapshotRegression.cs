using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int SnapshotRegression(Assembly core,string assets,string output,NativeBackend backend,VulkanShaderLibrary library,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;post.Settings.EnableTonemapping=true;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Program(string id,string source,int contract) {
            string file=Path.Combine(output,id+".hlsl");File.WriteAllText(file,source);
            Call("Backend.GuestPassList","AddProgram",id,"AfterLighting",file,"PublishOnly");
            var p=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(v=>(string)v.GetType().GetField("Id").GetValue(v)==id);
            p.GetType().GetField("ResourceContract").SetValue(p,contract);p.GetType().GetField("Enabled").SetValue(p,false);return p;
        }
        object fill=Program("fixture.snapshot.fill","cbuffer Values:register(b0){float4 Color;}float4 main(float4 p:SV_Position):SV_Target{return Color;}",0);
        object probe=Program("fixture.snapshot.probe","Texture2D<float4> H:register(t0);float4 main(float4 p:SV_Position):SV_Target{uint w,h;H.GetDimensions(w,h);return float4(H.Load(int3(0,0,0)).rgb,w/64.);}",1);
        object combine=Program("fixture.snapshot.combine","Texture2D<float4> H:register(t0);Texture2D<float4> U:register(t1);float4 main(float4 p:SV_Position):SV_Target{return float4(H.Load(int3(0,0,0)).r,U.Load(int3(0,0,0)).g,H.Load(int3(0,0,0)).b,1);}",0);
        Call("Backend.GuestCompiler","CompilePending",assets);
        ulong Pipeline(object p)=>(ulong)p.GetType().GetField("Pipeline").GetValue(p);
        Check(new[]{fill,probe,combine}.All(p=>Pipeline(p)!=0),"snapshot regression pipelines compile through the actual core");
        var create=typeof(NativeBackend).GetMethod("GuestCreateImage",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit=typeof(NativeBackend).GetMethod("GuestSubmit",BindingFlags.Instance|BindingFlags.NonPublic);
        var submit2=typeof(NativeBackend).GetMethod("GuestSubmit2",BindingFlags.Instance|BindingFlags.NonPublic);
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        const ulong hdr=0xFFFF000000000001UL,upscale=0xFFFF000000000006UL;
        ulong[] targets={7101,7102,7103,7104};foreach(ulong target in targets)create.Invoke(backend,new object[]{target,1,1,1,1});
        var fillBytes=new byte[16];Buffer.BlockCopy(new[]{.1f,.2f,.3f,1},0,fillBytes,0,16);
        void Ordinary(int mark,object p,ulong[] sampled)=>submit.Invoke(backend,new object[]{mark,4,64,64,Pipeline(p),hdr,ReferenceEquals(p,fill)?fillBytes:null,sampled,ReferenceEquals(p,fill)?32:0});
        void Probe(int mark,ulong target,ulong input)=>Check(submit2.Invoke(backend,new object[]{mark,5,1,1,1,Pipeline(probe),new[]{target},null,new[]{input},0}) is true,"snapshot read packet accepted at mark "+mark);
        float[] Pixels(ulong target){var data=new byte[8];read.Invoke(backend,new object[]{target,data});return Enumerable.Range(0,4).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,i*2))).ToArray();}
        void Frame(bool packets) {
            backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"snapshot world frame begins");GuestBridge.Reset();
            if(packets){Ordinary(2,fill,Array.Empty<ulong>());Probe(5,targets[0],hdr);Probe(5,targets[1],upscale);Ordinary(5,combine,new[]{hdr,upscale});Probe(6,targets[2],upscale);Probe(6,targets[3],hdr);}
            backend.EndFrame();
        }
        foreach(float fraction in new[]{1f,.5f,.75f,1f}) {
            backend.SetRenderFraction(fraction);Frame(false);Frame(true);
            var h=Pixels(targets[0]);var u=Pixels(targets[1]);var combined=Pixels(targets[2]);var late=Pixels(targets[3]);
            Console.WriteLine("snapshot fraction="+fraction+" HDR="+string.Join(",",h)+" tone="+string.Join(",",u)+" combined="+string.Join(",",combined)+" late="+string.Join(",",late));
            Check(Math.Abs(h[0]-.1)<.001 && Math.Abs(h[2]-.3)<.001,"snapshot linear HDR reference retains controlled color at fraction "+fraction);
            Check(Math.Abs(h[0]-u[0])>.02,"linear and tone snapshots are distinguishable at fraction "+fraction);
            Check(Math.Abs(combined[0]-h[0])<.006 && Math.Abs(combined[1]-u[1])<.006 && Math.Abs(combined[2]-h[2])<.006,"one pass reads distinct HDR and tone snapshots at fraction "+fraction);
            Check(Enumerable.Range(0,4).All(i=>Math.Abs(h[i]-late[i])<.001) && Math.Abs(late[3]-fraction)<.001,"AfterUpscale HDR snapshot keeps render dimensions and pixels at fraction "+fraction);
        }
        backend.SetRenderFraction(1);
        // Exercise Anomaly's actual Chain construction into the native final
        // output while every vendor/display service is disabled.
        object chain=Program("fixture.snapshot.chain","cbuffer Values:register(b7){float4 Color;}float4 main(float4 p:SV_Position):SV_Target{return Color;}",0);
        chain.GetType().GetField("Enabled").SetValue(chain,true);
        chain.GetType().GetField("Mark").SetValue(chain,6);
        chain.GetType().GetField("Slot").SetValue(chain,"AfterUpscale");
        chain.GetType().GetField("Compose").SetValue(chain,"Chain");
        chain.GetType().GetField("Priority").SetValue(chain,9999);
        chain.GetType().GetField("Uniforms").SetValue(chain,new[]{.1f,.2f,.3f,1});
        Call("Backend.GuestCompiler","CompilePending",assets);
        foreach(bool pipelined in new[]{false,true})
        foreach(float fraction in new[]{.5f,.75f,1f})
        {
            backend.SetRenderFraction(fraction);backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,false,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"Chain output frame begins");
            Call("Backend.GuestFrame","Capture");
            Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestBridgeClient","Submit",Call("Backend.GuestPassList","Snapshot"));
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            var pixels=new byte[64*64*8];read.Invoke(backend,new object[]{upscale,pixels});
            var expected=new[]{.1f,.2f,.3f,1f};
            Check(Enumerable.Range(0,4).All(i=>Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels,i*2))-expected[i])<.001f),
                "final AfterUpscale Chain actually writes output pixels without an upscaler at fraction "+fraction+" pipelined="+pipelined);
            Check((int)chain.GetType().GetField("ImageWidth").GetValue(chain)==64,"AfterUpscale isolated output uses output dimensions");
        }
        chain.GetType().GetField("Enabled").SetValue(chain,false);backend.SetRenderFraction(1);return checks;
    }
}
