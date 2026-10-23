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
    // Vulkan interface matching on a real owner: the OIT resolve consumes the stock fullscreen VS (float2 texcoord at
    // Location 0). Narrower (maintenance4) and differently interpolated inputs are legal; a mismatched type is refused.
    static int StageInterfaceRegression(Assembly core,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.stage-interface";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-2,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2}};}
        backend.UploadWorldMesh(951001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(951002,1,1,0,new byte[]{255,255,255,255},false);var material=new byte[96];Buffer.BlockCopy(new[]{.25f,.5f,.75f,.5f},0,material,16,16);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;light.AmbientDiffuseFactor=0;light.SunDiffuseFactor=0;
        uint frame=0;float[] Frame(bool pipelined=false){Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);backend.AddGlassPacket(0,951001,951002,951002,WorldConstants.Object(backend.GlassLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),material);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"interface frame begins; pipelined="+pipelined);Call("Backend.GuestFrame","Capture");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return backend.ReadbackLightingStage(3,64,64);}
        int at=(32*64+32)*4;string Hdr(float[] d)=>string.Join(",",d.Skip(at).Take(3));
        string root=Path.Combine(output,"InterfacePack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/Transparent/OIT"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");string file=Path.Combine(root,"Overlay/Transparent/OIT/Resolve.hlsl");
        const string head="Texture2D<float4> Accum:register(t0);Texture2D<float> Coverage:register(t1);";
        const string body="int2 p=pos.xy;float4 a=Accum.Load(int3(p,0));float reveal=Coverage.Load(int3(p,0));";
        void Register(string source){File.WriteAllText(file,source);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        object Error()=>Property("Backend.GuestPostprocessStages","Error");
        try{
            backend.SetHdrQuality(true);var stock=Frame();Console.WriteLine("INTERFACE stock="+Hdr(stock));Check(Math.Abs(stock[at]-.25f)<.015f&&Math.Abs(stock[at+2]-.75f)<.015f,"stock OIT resolve composes the glass ColorAdd");
            // A scalar input reads only the x of the float2 texcoord; the centre texel's x is 32.5/64.
            Register(head+"float4 __pixel_shader(float4 pos:SV_Position,float u:TEXCOORD0):SV_Target{"+body+"return float4(a.rgb/max(a.a,1e-3)*(1-reveal)*(u*2),reveal);}");
            var narrow=Frame(true);Console.WriteLine("INTERFACE narrow="+Hdr(narrow)+" error="+Error());float scale=2*32.5f/64;
            Check(Error()==null&&Math.Abs(narrow[at]-stock[at]*scale)<.003f&&Math.Abs(narrow[at+1]-stock[at+1]*scale)<.003f&&Math.Abs(narrow[at+2]-stock[at+2]*scale)<.003f,"maintenance4: a scalar input linked to the float2 fullscreen output reads its x component");
            var state=backend.GetPostprocessGeneration(2048);Check(state.Active!=0&&state.Executions>0&&state.LastFrame==frame,"narrow-input OIT owner executes this frame");Check(narrow.SequenceEqual(Frame()),"narrow-input owner agrees across recording modes");
            // Interpolation decorations do not take part in matching; a flat texcoord is legal (it takes the provoking vertex value).
            Register(head+"float4 __pixel_shader(float4 pos:SV_Position,nointerpolation float2 uv:TEXCOORD0):SV_Target{"+body+"return float4(a.rgb/max(a.a,1e-3)*(1-reveal)*(any(isnan(uv))?0:1),reveal);}");
            var flat=Frame(true);Console.WriteLine("INTERFACE flat="+Hdr(flat)+" error="+Error());
            Check(Error()==null&&backend.GetPostprocessGeneration(2048).Active!=state.Active&&Math.Abs(flat[at]-stock[at])<.003f&&Math.Abs(flat[at+2]-stock[at+2])<.003f,"a nointerpolation input links to the smooth fullscreen output and executes");
            ulong previous=backend.GetPostprocessGeneration(2048).Active;
            // An integer input cannot link to a float output, whatever its interpolation.
            Register(head+"float4 __pixel_shader(float4 pos:SV_Position,nointerpolation int2 uv:TEXCOORD0):SV_Target{"+body+"return float4(a.rgb/max(a.a,1e-3)*(1-reveal)*(uv.x+1),reveal);}");
            var refused=Frame(true);Console.WriteLine("INTERFACE refused error="+Error());
            Check(Error() is string e&&e.Contains("type mismatch")&&backend.GetPostprocessGeneration(2048).Active==previous&&refused.SequenceEqual(flat),"an int input against the float output is refused before activation and keeps the previous owner");
            // An input with no producer output is refused.
            Register(head+"float4 __pixel_shader(float4 pos:SV_Position,float2 uv:TEXCOORD0,float4 extra:TEXCOORD1):SV_Target{"+body+"return float4(a.rgb/max(a.a,1e-3)*(1-reveal)+extra.rgb,reveal);}");
            Frame(true);Console.WriteLine("INTERFACE unmatched error="+Error());
            Check(Error() is string m&&m.Contains("no matching output")&&backend.GetPostprocessGeneration(2048).Active==previous,"an input without a producer output is refused and keeps the previous owner");
            Call("Backend.GuestPassList","UnregisterRoot",owner);Check(stock.SequenceEqual(Frame(true)),"unregister restores exact stock OIT output");
            Frame();Check(backend.GetPostprocessGeneration(2048).Retired==0,"interface owners retire after completion");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"legal widened and flat interfaces raise zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);backend.SetHdrQuality(true);}
        return checks;
    }
}
