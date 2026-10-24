using System.Reflection;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    // Audit P3 steps 1-2: portable OpaqueLightingOwnership and RayScene API v1 in Anomaly core on Vulkan.
    static int RayFacadeRegression(Assembly core,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;const string tenant="sert";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var scene=core.GetType("ClientPlugin.Shaders.RayScene",false);var ownership=core.GetType("ClientPlugin.Shaders.OpaqueLightingOwnership",false);
        // SE-RT's AnomalyHost requires both types in one assembly and reflects these members.
        Check(scene!=null&&ownership!=null,"Anomaly core carries RayScene and OpaqueLightingOwnership together, as SE-RT binds them");
        Check(new[]{"Register","Unregister","CompileShader","SetFarSunEnabled","SetFrameEnabled","RegisterCompleted","SetEmitter","RemoveEmitter"}.All(m=>scene!.GetMethod(m,BindingFlags.Public|BindingFlags.Static)!=null)
            &&new[]{"Claim","Commit","Release","ShouldRun","BeginFrame","RegisterContribution"}.All(m=>ownership!.GetMethod(m)!=null)&&(int)scene!.GetField("ApiVersion")!.GetValue(null)! ==1,"RayScene v1 and ownership expose SE-RT's reflected members");
        object R(string method,params object[] args)=>scene!.GetMethod(method,BindingFlags.Public|BindingFlags.Static)!.Invoke(null,args)!;
        object O(string method,params object[] args)=>ownership!.GetMethod(method)!.Invoke(null,args)!;
        bool Run(string id)=>(bool)O("ShouldRun",id);
        int frames=0;Action<object[]> frame=_=>frames++;
        try{
            Check(R("Register",tenant,2,frame) is false&&R("Register",tenant,1,frame) is true&&(bool)scene!.GetProperty("CaptureRequested")!.GetValue(null)!,"Vulkan accepts API v1 registrations only");
            R("SetFarSunEnabled",tenant,true);R("SetFrameEnabled",tenant,true);R("SetEmitter",7u,new float[16],null!,0u,1u);R("RemoveEmitter",7u);
            Check(((string)scene!.GetProperty("Status")!.GetValue(null)!).Contains("producer is pending"),"status names the pending Vulkan scene producer");
            string refused=null;try{R("CompileShader","x.hlsl",0);}catch(TargetInvocationException e){refused=e.InnerException?.Message;}
            Check(refused!=null&&refused.Contains("guest compiler"),"CompileShader refuses DX11 bytecode on Vulkan");

            Check((bool)O("Claim",tenant,7)&&!(bool)O("Claim","fixture.other",2),"a claim overlapping another tenant's signals is refused");
            Check(!(bool)O("Commit",tenant),"a commit is refused while an overlapping claim stands");
            O("Release","fixture.other");Check((bool)O("Commit",tenant)&&(string)ownership!.GetProperty("Owner")!.GetValue(null)! ==tenant,"commit succeeds once the conflict is released");
            O("RegisterContribution","fixture.ao",2);
            Check(!Run("prism.ssgi.gi")&&!Run("sss.contact")&&!Run("fixture.ao")&&Run("fixture.unrelated"),"committed signals skip aliased and declared contributors only");
            O("BeginFrame");Check(Run("prism.ssgi.gi")&&Run("fixture.ao"),"BeginFrame clears the frame's commitment");

            // Guest submission honours ownership: an owned diffuse-indirect program is not submitted that frame.
            string file=Path.Combine(output,"fixture.ownership.hlsl");File.WriteAllText(file,"float4 main():SV_Target{return float4(.25,.5,.75,1);}");
            Call("Backend.GuestPassList","AddProgram","prism.ssgi.fixture","BeforeTonemap",file,"Replace");
            Call("Backend.GuestCompiler","CompilePending",Path.Combine(Path.GetDirectoryName(core.Location)!,"../../../../Assets"));
            var program=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id")!.GetValue(p)! =="prism.ssgi.fixture");
            bool Submitted()=>(bool)program.GetType().GetField("SubmittedThisFrame")!.GetValue(program)!;
            var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true)!;
            var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true)!.GetMethod("Submit",stat)!);
            var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
            void Frame(bool commit){
                Call("Backend.RenderHost","PrepareShaderGenerations");if(commit)O("Commit",tenant);
                backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);
                backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
                Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"ray facade frame begins");Call("Backend.GuestFrame","Capture");
                Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);backend.EndFrame();
            }
            Frame(false);Check(Submitted(),"without a committed tenant the program submits");
            Frame(true);Check(!Submitted(),"a committed DiffuseIndirect owner skips the SSGI-aliased program at submission");
            Frame(false);Check(Submitted(),"the next frame without a commitment submits it again");
            Call("Backend.GuestPassList","SetEnabled","prism.ssgi.fixture",false);

            R("Unregister",tenant);
            Check(ownership!.GetProperty("Owner")!.GetValue(null)==null&&!(bool)O("Commit",tenant)&&!(bool)scene!.GetProperty("CaptureRequested")!.GetValue(null)!,"Unregister releases the tenant's ownership claim and capture request");
            Check(frames==0,"no frames are delivered before the Vulkan scene producer exists");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"ray facade lane raises zero validation errors");
        }finally{R("Unregister",tenant);O("Release","fixture.other");O("BeginFrame");}
        return checks;
    }
}
