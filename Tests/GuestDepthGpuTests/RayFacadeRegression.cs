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
        int frames=0;object[] last=null;bool commitInFrame=false;Action<object[]> frame=f=>{frames++;last=f;if(commitInFrame)O("Commit",tenant);};
        // Synthetic renderer capture: one quad instance 5 m ahead and one point light.
        var quad=new float[64];float[][] corners={new[]{-1f,-1,0},new[]{1f,-1,0},new[]{1f,1,0},new[]{-1f,1,0}};for(int v=0;v<4;v++){quad[v*16]=corners[v][0];quad[v*16+1]=corners[v][1];quad[v*16+5]=1;}
        var quadPart=new object[]{0,6,0,"fixture",new string[4],"MESH"};var worldRow=new float[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,-5,1};
        var synthetic=new object[]{new[]{new object[]{1,quad,new[]{0,1,2,0,2,3},new[]{quadPart},false}},new[]{new object[]{4242u,0,worldRow,new object[]{new object[]{quadPart,new[]{1f,1,1,0},new object[4],Array.Empty<float>(),null!}},new[]{-1f,-1,-5,1,1,-5}}},
            new float[]{0,2,0,10,1,1,1,1,0,0,-1,-1,0,0,1,1},new[]{new object[]{null!,new[]{0f,1,0}}},new float[]{0,2,0},new[]{0d,0,0},1f};
        var overrideField=typeof(ClientPlugin.Rendering.GuestBridge).GetField("RayCaptureOverride",BindingFlags.NonPublic|BindingFlags.Static)!;
        try{
            Check(R("Register",tenant,2,frame) is false&&R("Register",tenant,1,frame) is true&&(bool)scene!.GetProperty("CaptureRequested")!.GetValue(null)!,"Vulkan accepts API v1 registrations only");
            R("SetFarSunEnabled",tenant,true);R("SetFrameEnabled",tenant,true);R("SetEmitter",7u,new float[16],null!,0u,1u);R("RemoveEmitter",7u);
            Check(((string)scene!.GetProperty("Status")!.GetValue(null)!).Contains("registered"),"status reports the Vulkan registration");
            string refused=null;try{R("CompileShader","x.hlsl",0);}catch(TargetInvocationException e){refused=e.InnerException?.Message;}
            Check(refused!=null&&refused.Contains("Ray tenant shader compilation failed"),"CompileShader compiles through the guest compiler on Vulkan and reports a failed source");

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
            // Delivery: the renderer capture becomes a v1 frame before submission, so a commit inside the callback owns this frame.
            overrideField.SetValue(null,(Func<object[]>)(()=>synthetic));frames=0;Frame(false);
            uint frameIndex=(uint)Call("Backend.GuestFrame","U32",16);var viewProj=(Matrix)Call("Backend.GuestFrame","MatrixAt",136);
            Check(frames==1&&last!=null&&last.Length==16&&(int)last[0]==1&&last[2] is uint index&&index==frameIndex&&((object[])last[4]).Length==1&&((float[])last[6]).Length==16,"a registered tenant receives one v1 frame per guest frame with the renderer capture");
            var devices=(object[])last[7];var view=(float[])last[10];
            // DX11 frame[7] positions: device, context (tenant session), LBuffer, depth, GBuffer0-2, velocity, frame constants, sun shadows, close/far cubes, BRDF.
            Check(devices.Length==13&&(string)devices[0]=="vulkan"&&devices[1] is IReadOnlyDictionary<string,Delegate> session&&session.ContainsKey("Record")&&(ulong)devices[2]==0xFFFF000000000001UL&&(ulong)devices[3]==0xFFFF000000000002UL
                &&(ulong)devices[8]==0xFFFF000000000024UL&&(ulong)devices[9]==0xFFFF000000000020UL&&(ulong)devices[12]==0xFFFF000000000023UL
                &&((int[])last[8]).SequenceEqual(new[]{64,64})&&Math.Abs(view[0]-viewProj.M11)<1e-6&&Math.Abs(view[5]-viewProj.M22)<1e-6&&last[15]==null,"the frame keeps DX11's resource positions with the tenant session as context; far sun is pending");
            var sert=Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../SE-RT/ClientPlugin/bin/Release/net10.0/SERT.dll")));
            var geometry=sert.GetType("SERT.Core.SceneAssembler",true)!.GetMethod("Build")!.Invoke(null,new object[]{last})!;
            Check(((Array)geometry.GetType().GetField("Triangles")!.GetValue(geometry)!).Length==2&&((Array)geometry.GetType().GetField("Instances")!.GetValue(geometry)!).Length==1,"SE-RT SceneAssembler builds the delivered Vulkan frame");
            commitInFrame=true;Frame(false);commitInFrame=false;Check(!Submitted(),"a tenant committing inside its frame callback skips the owned program in that same frame");
            Frame(false);Check(Submitted(),"without the in-frame commit the program submits again");
            Call("Backend.GuestPassList","SetEnabled","prism.ssgi.fixture",false);

            R("Unregister",tenant);
            Check(ownership!.GetProperty("Owner")!.GetValue(null)==null&&!(bool)O("Commit",tenant)&&!(bool)scene!.GetProperty("CaptureRequested")!.GetValue(null)!,"Unregister releases the tenant's ownership claim and capture request");
            overrideField.SetValue(null,null);frames=0;Frame(false);Check(frames==0,"an unregistered tenant receives no frames");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"ray facade lane raises zero validation errors");
        }finally{overrideField.SetValue(null,null);R("Unregister",tenant);O("Release","fixture.other");O("BeginFrame");}
        return checks;
    }
}
