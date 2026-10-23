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
    // DX11 CelestialProbePatch: a celestial provider also draws its ANOMALY_CELESTIAL_PROBE variant on every probe face.
    static int CelestialProbeRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string id="fixture.celestial.probe";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        Check((backend.NativeOwnerFeatures()&64)!=0,"native owner extension advertises probe-face celestial passes (flag64)");
        var registry=core.GetType("ClientPlugin.Shaders.CelestialBackgroundRegistry",true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        using(var file=File.OpenRead(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"))){var image=DdsImage.Read(file,true);backend.UploadTextureMips(949001,image.Width,image.Height,image.Format,image.Mips,false);}
        var oldConfig=backend.CurrentProbeConfig;var probes=new VulkanProbes{Resolution=16};probes.Settings.TimeOut=0;float time=0;
        (float[][] Faces,float[] Hdr) Frame(bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();time+=.1f;probes.Configure(backend,Vector3D.Zero,light,MyPostprocessSettings.Default,MatrixD.Identity,949001,.1f,time);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"celestial probe frame begins; pipelined="+pipelined);Call("Backend.GuestFrame","Capture");Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            var faces=Enumerable.Range(0,12).Select(i=>{var bytes=backend.ReadbackProbe((uint)(i/6),(uint)(i%6),0,16);return Enumerable.Range(0,bytes.Length/2).Select(j=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,j*2))).ToArray();}).ToArray();
            return(faces,backend.ReadbackLightingStage(3,64,64));
        }
        bool Uniform(float[] pixels,float r,float g,float b)=>Enumerable.Range(0,16*16).All(i=>Math.Abs(pixels[i*4]-r)<.002f&&Math.Abs(pixels[i*4+1]-g)<.002f&&Math.Abs(pixels[i*4+2]-b)<.002f);
        string root=Path.Combine(output,"CelestialProbe-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string shader=Path.Combine(root,"Background.hlsl");
        File.WriteAllText(shader,"#include <AnomalyCelestial.hlsli>\nfloat3 AnomalyEvaluateCelestial(AnomalyCelestialInput input){return input.isProbe?float3(.25,.5,.75):float3(.75,.5,.25);}\n#include <AnomalyCelestialEntry.hlsli>\n");
        try{
            var stock=Frame();Console.WriteLine("CELESTIAL stock face0="+string.Join(",",stock.Faces[0].Take(3)));
            Check(registry.GetMethod("Register",stat).Invoke(null,new object[]{id,shader}) is true,"celestial provider registers on Vulkan");
            var snapshot=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().ToArray();
            Check(snapshot.Any(p=>(string)programType.GetField("Id").GetValue(p)==id+".probe"),"registration adds the probe-face variant beside the main program");
            Frame();var owned=Frame(true);int at=(32*64+32)*4;Console.WriteLine("CELESTIAL owned face0="+string.Join(",",owned.Faces[0].Take(3))+" main="+string.Join(",",owned.Hdr.Skip(at).Take(3)));
            for(int i=0;i<12;i++)Check(Uniform(owned.Faces[i],.25f,.5f,.75f),"probe target"+(i/6)+" face"+(i%6)+" holds the provider's isProbe=1 colour on every texel");
            Check(owned.Hdr[at]>owned.Hdr[at+2]&&owned.Hdr[at+1]>0,"main view keeps the isProbe=0 variant (red-dominant), never the probe colour");
            var sync=Frame();Check(sync.Faces.Zip(owned.Faces,(x,y)=>x.SequenceEqual(y)).All(v=>v),"probe-face celestial output agrees across recording modes");
            registry.GetMethod("SetEnabled",stat).Invoke(null,new object[]{id,false});var disabled=Frame(true);
            Check(disabled.Faces.Zip(stock.Faces,(x,y)=>x.SequenceEqual(y)).All(v=>v),"disabling the provider restores exact stock probe faces");
            registry.GetMethod("SetEnabled",stat).Invoke(null,new object[]{id,true});Frame();Check(Frame(true).Faces.All(f=>Uniform(f,.25f,.5f,.75f)),"re-enabling restores the probe-face draw");
            registry.GetMethod("Unregister",stat).Invoke(null,new object[]{id});var restored=Frame(true);
            Check(restored.Faces.Zip(stock.Faces,(x,y)=>x.SequenceEqual(y)).All(v=>v),"unregister restores exact stock probe faces");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"probe-face celestial draws raise zero core/synchronization errors");
        }finally{
            registry.GetMethod("Unregister",stat).Invoke(null,new object[]{id});
            if(oldConfig.Size==8096&&oldConfig.Frames!=null)backend.SetProbes(oldConfig);else backend.SetProbes(new NativeProbeConfig{Size=8096,AbiVersion=NativeBackend.AbiVersion,Resolution=16,Frames=new byte[7680],Projections=new float[96]});
        }
        return checks;
    }
}
