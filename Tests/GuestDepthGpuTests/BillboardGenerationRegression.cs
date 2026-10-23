using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;
partial class Program
{
    // Billboard owners: Transparent/Billboards.hlsl has no named stage, so packs reach it (on DX11 too) through shared
    // headers. A Transparent/OIT/Globals.hlsli overlay replaces the OIT, additive, top, LDR and PostPP programs.
    static int BillboardGenerationRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.billboards";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        Check((backend.NativeOwnerFeatures()&32)!=0,"native owner extension advertises billboard owners (flag32)");
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var rows=backend.PostprocessCatalog().Where(r=>(uint)r[0]>=2560&&(uint)r[0]<2625).ToArray();
        Check(rows.Length==65&&rows.All(r=>(string)r[1]=="Transparent/Billboards.hlsl")&&rows.Count(r=>(string)r[2]=="ps_5_0")==32,"catalog lists 32 billboard VS/PS pairs and the LDR VS, all from Transparent/Billboards.hlsl");
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        backend.UploadTexture(952002,1,1,0,new byte[]{255,255,255,255},true);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;light.AmbientDiffuseFactor=0;light.SunDiffuseFactor=0;
        // One camera-relative quad over the centre; bucket 1 is Standard (OIT variant 0), the rest use additive variant 8.
        NativeBillboardDraw Billboard(uint bucket){var data=new byte[48];Buffer.BlockCopy(new[]{.25f,.5f,.75f,1f},0,data,0,16);Buffer.BlockCopy(BitConverter.GetBytes(-1),0,data,16,4);
            float d=-2,s=1;return new NativeBillboardDraw{Texture=952002,Flags=bucket==1?0u:8u,Bucket=bucket,Data=data,Vertices=new[]{-s,-s,d,0,1,s,-s,d,1,1,s,s,d,1,0,-s,s,d,0,0}};}
        uint frame=0;
        // HDR buckets read the lit HDR target; LDR and PostPP read the presented image.
        float[] Hdr(uint bucket,bool pipelined=false){Run(bucket,pipelined,false);return backend.ReadbackLightingStage(3,64,64);}
        byte[] Ldr(uint bucket){Run(bucket,false,true);return backend.Readback(64,64);}
        void Run(uint bucket,bool pipelined,bool present){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
            if(bucket<5)backend.BillboardPackets.Add(Billboard(bucket));
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"billboard owner frame begins; bucket="+bucket+" pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(present)backend.RequestReadback();if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
        }
        int at=(32*64+32)*4;
        bool Swapped(float[] owned,float[] stock)=>Math.Abs(owned[at]-stock[at+2])<.003f&&Math.Abs(owned[at+1]-stock[at+1])<.003f&&Math.Abs(owned[at+2]-stock[at])<.003f;
        bool SwappedBytes(byte[] owned,byte[] stock)=>Math.Abs(owned[at]-stock[at+2])<=1&&Math.Abs(owned[at+1]-stock[at+1])<=1&&Math.Abs(owned[at+2]-stock[at])<=1;
        string root=Path.Combine(output,"BillboardPack-"+Guid.NewGuid().ToString("N")),file=Path.Combine(root,"Overlay/Transparent/OIT/Globals.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");
        // Exchange red and blue after fog (OIT) and in the premultiplied path: every bucket's oracle is the stock pixel swizzled.
        const string oitAnchor="    // Insert your favorite weighting function here.",premultAnchor="    accumTarget = color;\n    coverageTarget = 0;";
        string stockSource=File.ReadAllText(Path.Combine(bin,"../Content/Shaders/Transparent/OIT/Globals.hlsli")).Replace("\r\n","\n");Check(stockSource.Contains(oitAnchor)&&stockSource.Contains(premultAnchor),"stock OIT globals expose the billboard color output anchors");
        string swap=stockSource.Replace(oitAnchor,"    color.rgb = color.bgr;\n"+oitAnchor).Replace(premultAnchor,"    color.rgb = color.bgr;\n"+premultAnchor);
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        try{
            backend.SetHdrQuality(true);var hdrBuckets=new uint[]{1,0,2};
            var stockHdr=hdrBuckets.ToDictionary(b=>b,b=>Hdr(b));var stockLdr=new uint[]{3,4}.ToDictionary(b=>b,b=>Ldr(b));var empty=Ldr(5);
            foreach(uint b in hdrBuckets){Console.WriteLine("BILLBOARD stock bucket"+b+"="+string.Join(",",stockHdr[b].Skip(at).Take(3)));Check(stockHdr[b][at]>0&&stockHdr[b][at+2]>stockHdr[b][at]&&stockHdr[b].SequenceEqual(Hdr(b,true)),"stock bucket"+b+" billboard is visible, blue-dominant and exact across recording modes");}
            foreach(uint b in new uint[]{3,4}){Console.WriteLine("BILLBOARD stock bucket"+b+"="+string.Join(",",stockLdr[b].Skip(at).Take(3))+" empty="+string.Join(",",empty.Skip(at).Take(3)));Check(stockLdr[b][at+2]>stockLdr[b][at]+20,"stock bucket"+b+" billboard draws blue-dominant LDR pixels");}
            Register(swap);var owned=Hdr(1,true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"billboard owner generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));
            var oit=backend.GetPostprocessGeneration(2561);Check(oit.Active!=0&&oit.Executions>0&&oit.LastFrame==frame&&backend.GetPostprocessGeneration(2560).Executions==oit.Executions,"selected OIT billboard owner pair (variant 0) executes this frame");
            foreach(uint b in hdrBuckets){var value=b==1?owned:Hdr(b,true);Console.WriteLine("BILLBOARD owned bucket"+b+"="+string.Join(",",value.Skip(at).Take(3)));
                Check(Swapped(value,stockHdr[b]),"bucket"+b+" owner exchanges the stock red and blue channels from its own invocation");Check(value.SequenceEqual(Hdr(b)),"bucket"+b+" owner output agrees across recording modes");}
            var additive=backend.GetPostprocessGeneration(2577);Check(additive.Active!=0&&additive.Executions>0&&additive.LastFrame==frame,"selected additive billboard owner pair (variant 8) executes this frame");
            ulong ldrVertex=backend.GetPostprocessGeneration(2624).Executions;
            foreach(uint b in new uint[]{3,4}){var value=Ldr(b);Console.WriteLine("BILLBOARD owned bucket"+b+"="+string.Join(",",value.Skip(at).Take(3)));Check(SwappedBytes(value,stockLdr[b]),"bucket"+b+" owner program exchanges the stock red and blue channels");
                var vertex=backend.GetPostprocessGeneration(2624);Check(vertex.Executions>ldrVertex&&vertex.LastFrame==frame,"bucket"+b+" runs the owner LDR vertex shader this frame");ldrVertex=vertex.Executions;}
            ulong count=backend.GetPostprocessGeneration(2561).Executions;Hdr(5,true);Check(backend.GetPostprocessGeneration(2561).Executions==count,"billboard owners have no invented execution in a frame without billboards");
            ulong previous=backend.GetPostprocessGeneration(2561).Active;Register("BrokenSyntax");var broken=Hdr(1);
            Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(2561).Active==previous&&Swapped(broken,stockHdr[1]),"failed billboard compilation retains the previous owner closure");
            backend.SetHdrQuality(false);var low=Hdr(0);backend.SetHdrQuality(true);Check(low[at+2]>0&&Swapped(Hdr(0),stockHdr[0]),"additive owner pipelines draw at both HDR formats and survive the format switch");
            Call("Backend.GuestPassList","UnregisterRoot",owner);
            foreach(uint b in hdrBuckets)Check(stockHdr[b].SequenceEqual(Hdr(b,true)),"unregister restores exact stock bucket"+b+" output");
            foreach(uint b in new uint[]{3,4})Check(stockLdr[b].SequenceEqual(Ldr(b)),"unregister restores exact stock bucket"+b+" output");
            Hdr(5);Check(backend.GetPostprocessGeneration(2561).Retired==0,"billboard owner pipelines, modules and layouts retire after completion");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"billboard owners have zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);backend.SetHdrQuality(true);}
        return checks;
    }
}
