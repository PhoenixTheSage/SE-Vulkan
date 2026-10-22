using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int LightingGenerationRegression(Assembly core,string assets,string output,NativeBackend backend,VulkanShaderLibrary library,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        var listType=typeof(IReadOnlyList<>).MakeGenericType(core.GetType("ClientPlugin.Backend.GuestProgram",true));
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(listType),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        backend.SetHdrQuality(true);backend.SetLightingDiagnostics(true);backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());
        void Prepare()=>Call("Backend.RenderHost","PrepareShaderGenerations");
        void Frame(bool pipelined=false,bool prepare=true)
        {
            if(prepare)Prepare();backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,true,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native lighting generation frame begins pipelined="+pipelined);
            Call("Backend.GuestFrame","Capture");Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
        }
        void Pixels(uint stage,float red,float green,float blue)
        {
            var data=backend.ReadbackLightingStage(stage,64,64);
            Check(Enumerable.Range(0,64*64).All(i=>Math.Abs(data[i*4]-red)<.003&&Math.Abs(data[i*4+1]-green)<.003&&Math.Abs(data[i*4+2]-blue)<.003),"lighting stage "+stage+" produces expected HDR pixels "+red+","+green+","+blue);
        }
        string root=Path.Combine(output,"NativeLightingPack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string relative,string text){string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text);}
        void Register()=>Call("Backend.GuestPassList","RegisterRoot","fixture.native.lighting",root,null);
        void Spot(bool shadow)
        {
            backend.SetLights(new[]{new NativeLight{PositionRange=new float[]{0,0,0,10},ColorIntensity=new float[]{1,1,1,1},DirectionCone=new float[]{0,0,-1,.5f},Flags=1}});
            backend.SetSpots(256,new[]{new NativeSpot{Light=0,Flags=shadow?1u:0u,Projection=NativeBackend.MatrixValues(MatrixD.CreatePerspectiveFieldOfView(Math.PI*.65,1,.1,10))}});
        }
        try
        {
            Frame();var stock=backend.ReadbackLightingStage(0,64,64);ulong stockPost=backend.GetPostprocessGeneration(13).Active;
            Write("anomaly.json","{\"id\":\"fixture.native.lighting\",\"exclusive\":[\"Lighting\"]}");
            Write("Inject/Lighting/Family.hlsli","float3 FixtureFamily(float r,float g,float b){return float3(r,g,b);}");
            Write("Inject/Lighting.Dir/Stage.hlsli","float FixtureDir(){return .5;}");
            Write("Overlay/Lighting/Light.hlsli","float FixtureHeader(){return .25;}");
            Write("Overlay/Lighting.Dir/LightDir.hlsl","#if defined(ANOMALY_COMPOSITE)\n#error named-stage-inherited-owned-composite-define\n#endif\n#include <Lighting/Light.hlsli>\nfloat4 __pixel_shader():SV_Target{return float4(FixtureFamily(FixtureHeader(),FixtureDir(),.75),1);}");
            Write("Overlay/Lighting.Point/LightPoint.hlsl","float4 __pixel_shader():SV_Target{return float4(FixtureFamily(.125,.25,.5),1);}");
            Write("Overlay/Lighting.Spot/LightSpot.hlsl","float4 __pixel_shader():SV_Target{\n#ifdef SHADOWS\nreturn float4(FixtureFamily(.125,.25,.5),1);\n#else\nreturn float4(FixtureFamily(.0625,.125,.25),1);\n#endif\n}");
            Register();Frame();Check(Property("Backend.GuestPostprocessStages","Error")==null,"actual core prepares captured lighting sources: "+Property("Backend.GuestPostprocessStages","Error"));Pixels(1,.125f,.25f,.5f);Pixels(0,.375f,.75f,1.25f);
            ulong a=backend.GetPostprocessGeneration(256).Active;
            Check(a!=0&&Enumerable.Range(256,4).All(i=>backend.GetPostprocessGeneration((uint)i).Active==a),"four lighting variants activate as one native generation");
            Check(backend.GetPostprocessGeneration(13).Active==stockPost,"lighting-only sources preserve the active postprocess pipeline");
            Spot(false);Frame();Pixels(2,.1875f,.375f,.75f);Pixels(0,.4375f,.875f,1.5f);
            Spot(true);Frame(true);Pixels(2,.25f,.5f,1);Pixels(0,.5f,1,1.75f);
            Check(Enumerable.Range(256,4).All(i=>{var state=backend.GetPostprocessGeneration((uint)i);return state.Executions>0&&state.LastCompletion<=state.Completed;}),"every selected lighting permutation records completed GPU execution");
            var publicState=Call("Shaders.ShaderPackRegistry","GetNativeStageState","Lighting");
            var publicVariants=((System.Collections.IEnumerable)publicState.GetType().GetProperty("Variants").GetValue(publicState)).Cast<object>().ToArray();
            Check(publicVariants.Length==4&&publicVariants.All(v=>(string)v.GetType().GetProperty("Profile").GetValue(v)=="ps_5_0"),"public Lighting-family status exposes all four native fragment variants");
            // Change only a shared header; the captured entry points stay byte-identical.
            Write("Overlay/Lighting/Light.hlsli","float FixtureHeader(){return .5;}");Register();Frame(true);Pixels(0,.75f,1,1.75f);
            ulong b=backend.GetPostprocessGeneration(256).Active;Check(b!=a,"shared lighting-header reload activates a new generation while A was outstanding");
            backend.SetHdrQuality(false);Frame();Pixels(0,.75f,1,1.75f);ulong packed=backend.GetPostprocessGeneration(256).Active;
            Check(packed!=b,"unchanged lighting source rebuilds for packed HDR");backend.SetHdrQuality(true);Frame();Pixels(0,.75f,1,1.75f);
            ulong beforeFailure=backend.GetPostprocessGeneration(256).Active;
            Write("Overlay/Post.FXAA/Fxaa.hlsl","#error cross-owner-lighting-rollback\n");Register();Frame();Pixels(0,.75f,1,1.75f);
            Check(backend.GetPostprocessGeneration(256).Active==beforeFailure&&backend.GetPostprocessGeneration(13).Active==stockPost&&((string)Property("Backend.GuestPostprocessStages","Error")).Contains("cross-owner-lighting-rollback"),"failed postprocess member retains the entire lighting/postprocess generation");
            Write("Overlay/Post.FXAA/Fxaa.hlsl","float4 __pixel_shader():SV_Target{return float4(.5,.25,.125,1);}");Register();Frame(true);Pixels(0,.75f,1,1.75f);
            ulong combined=backend.GetPostprocessGeneration(256).Active;Check(combined!=beforeFailure&&combined==backend.GetPostprocessGeneration(13).Active,"lighting and postprocessing commit one shared native token");
            var final=backend.Readback(64,64);int at=(32*64+32)*4;Check(Math.Abs(final[at]-128)<=2&&Math.Abs(final[at+1]-64)<=2&&Math.Abs(final[at+2]-32)<=2,"combined generation executes the selected native FXAA output");
            // A late invalid lighting descriptor must clean an earlier valid postprocess bundle.
            string invalid=Path.Combine(output,"BadLightingBinding.hlsl");File.WriteAllText(invalid,"Texture2D T:register(t6);float4 __pixel_shader():SV_Target{return T.Load(int3(0,0,0));}");
            using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"LightingGenerationCache")))
            {
                var artifact=VulkanShaderArtifact.FromCompilation(compiler.Compile(new VulkanShaderRequest{SourcePath=invalid,EntryPoint="__pixel_shader",Profile="ps_5_0"}),"bad-lighting-binding");
                bool refused=false;try{backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{0,library.Load(PostprocessShaderVariants.Stages[0])},{256,artifact}},"bad-lighting-binding");}catch(Exception error){refused=error.Message.Contains("resource contract");}
                Check(refused&&backend.GetPostprocessGeneration(256).Active==combined&&backend.GetPostprocessGeneration(256).Prepared==0,"late lighting descriptor refusal cleans the detached mixed-owner batch");
            }
            backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());Call("Backend.GuestPassList","UnregisterRoot","fixture.native.lighting");Frame();
            Check(stock.SequenceEqual(backend.ReadbackLightingStage(0,64,64)),"lighting unregister restores exact stock HDR pixels");
            Frame();Check(backend.GetPostprocessGeneration(256).Prepared==0&&backend.GetPostprocessGeneration(256).Retired==0,"mixed native owner candidates and retirements drain after completion");
            Check(backend.GetStats().ValidationErrors==0,"lighting generation replacement, HDR, rollback and retirement have zero validation errors");
        }
        finally{Call("Backend.GuestPassList","UnregisterRoot","fixture.native.lighting");backend.SetLights(Array.Empty<NativeLight>());backend.SetSpots(256,Array.Empty<NativeSpot>());backend.SetLightingDiagnostics(false);}
        return checks;
    }
}
