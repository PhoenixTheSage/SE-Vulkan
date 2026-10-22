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
    static int SharedDependencyRegression(Assembly core,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string owner="fixture.native.dependencies";int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        NativeWorldVertex V(float x,float y){uint n=VF_Packer.PackNormal(Vector3.Backward);return new(){Material=new(){X=x,Y=y,Z=-3,Scale=1,NormalX=n&65535,NormalY=n>>16,TangentX=1,TangentY=.5f,TangentZ=.5f,TangentW=1,U=(x+1)/2,V=(1-y)/2},WeightX=1};}
        backend.UploadWorldMesh(957001,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2,2,1,0});backend.UploadTexture(957002,1,1,0,new byte[]{180,180,180,0},true);backend.UploadTexture(957003,1,1,0,new byte[]{128,128,255,100},false);backend.UploadTexture(957004,1,1,0,new byte[]{255,1,0,0},false);
        backend.SetShadowCascades(Enumerable.Range(0,8).SelectMany(_=>NativeBackend.MatrixValues(MatrixD.Identity)).ToArray(),Enumerable.Range(1,8).Select(i=>(float)i*10).ToArray());backend.SetShadowOptions(false);backend.SetShadowSchedule(false,255,255);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        (byte[] Shadows,float[] Hdr) Frame(bool pipelined=false){Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,true,view:MatrixD.Identity,projection:projection);backend.AddWorldPacket(0,957001,957002,957003,957004,0,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0),flags:512);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"shared dependency frame begins; pipelined="+pipelined);Call("Backend.GuestFrame","Capture");Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);if(pipelined)backend.EndFrameAsync();else backend.EndFrame();return(backend.ReadbackLightingInput(1,64,64),backend.ReadbackLightingStage(3,64,64));}
        string root=Path.Combine(output,"DependencyPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Overlay/Shadows"));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");string original=File.ReadAllText(Path.Combine(bin,"../Content/Shaders/Shadows/Csm.hlsli"));string marker="float SampleShadow(float distance, float3 shadowmapPosition, float3 worldPosition, int cascadeIndex, uint2 rotationOffset = 0)";int brace=original.IndexOf('{',original.IndexOf(marker));Check(brace>=0,"fixture identifies the authenticated stock SampleShadow body");
        void Register(float value){File.WriteAllText(Path.Combine(root,"Overlay/Shadows/Csm.hlsli"),original.Insert(brace+1,"\nreturn "+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+";\n"));Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        int at=32*64+32;
        try{
            var catalog=backend.PostprocessCatalog();Check(catalog.All(e=>e.Length==4&&e[3] is string[] a&&a.Contains((string)e[1],StringComparer.OrdinalIgnoreCase)),"every native catalog row publishes authenticated dependency paths and its entry point");
            var csm=catalog.Where(e=>((string[])e[3]).Contains("Shadows/Csm.hlsli",StringComparer.OrdinalIgnoreCase)).Select(e=>(uint)e[0]).ToArray();Console.WriteLine("CSM dependent owners="+string.Join(",",csm));Check(csm.Contains(1792u)&&csm.Any(id=>id>=256&&id<260)&&csm.Any(id=>id>=1536&&id<1546),"authenticated shared-header graph crosses shadows, lighting and particle owners");
            var stock=Frame();ulong unrelated=backend.GetPostprocessGeneration(13).Active;Register(.375f);var first=Frame(true);Check(Property("Backend.GuestPostprocessStages","Error")==null,"header-only cross-family native generation prepares: "+Property("Backend.GuestPostprocessStages","Error"));Check(Math.Abs(first.Shadows[at]-.375*255)<=1,"header-only CSM override executes the selected native shadow shader");ulong token=backend.GetPostprocessGeneration(1792).Active;Check(token!=0&&csm.All(id=>backend.GetPostprocessGeneration(id).Active==token),"every authenticated CSM dependent joins the same atomic generation");Check(backend.GetPostprocessGeneration(256).Executions>0&&backend.GetPostprocessGeneration(1792).Executions>0,"cross-family lighting and shadow owners execute the selected generation");Check(backend.GetPostprocessGeneration(13).Active==unrelated,"unrelated tone owner is not rebuilt for a CSM header");Check(first.Shadows.SequenceEqual(Frame().Shadows),"header-dependent native outputs agree across recording modes");
            Register(.625f);var second=Frame(true);Check(Math.Abs(second.Shadows[at]-.625*255)<=1&&backend.GetPostprocessGeneration(1792).Active!=token,"header reload rebuilds all native consumers and changes actual GPU output");ulong next=backend.GetPostprocessGeneration(1792).Active;
            File.WriteAllText(Path.Combine(root,"Overlay/Shadows/Csm.hlsli"),"BrokenSyntax");Call("Backend.GuestPassList","RegisterRoot",owner,root,null);Check(second.Shadows.SequenceEqual(Frame().Shadows)&&Property("Backend.GuestPostprocessStages","Error") is string&&csm.All(id=>backend.GetPostprocessGeneration(id).Active==next),"one invalid shared header preserves the complete prior multi-family generation");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Frame(true);Check(stock.Shadows.SequenceEqual(restored.Shadows)&&stock.Hdr.SequenceEqual(restored.Hdr),"unregister restores exact stock shadow and HDR output after cross-family replacement");Frame();Check(backend.GetPostprocessGeneration(1792).Retired==0,"shared-header generations retire all dependent layouts after completion");Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"shared-header cross-family execution has zero core/synchronization errors");
        }finally{Call("Backend.GuestPassList","UnregisterRoot",owner);}
        return checks;
    }
}
