using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int PostprocessGenerationRegression(Assembly core,string assets,string output,NativeBackend backend,VulkanShaderLibrary library,MatrixD projection,MyEnvironmentLightData light)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.RenderHost",true).GetField("assetFolder",stat).SetValue(null,assets);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var listType=typeof(IReadOnlyList<>).MakeGenericType(programType);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(listType),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;post.Settings.Data.ChromaticFactor=0;post.Settings.Data.VignetteStart=0;
        void Prepare()=>Call("Backend.RenderHost","PrepareShaderGenerations");
        void Frame(bool async=false,bool prepare=true)
        {
            if(prepare)Prepare();
            backend.BeginPacketCollection();var size=backend.ResolveRenderResolution(64,64);
            post.Configure(backend,size.X,size.Y,true,view:MatrixD.Identity,projection:projection);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,size.X,size.Y,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"native postprocess generation frame begins async="+async);
            Call("Backend.GuestFrame","Capture");Call("Backend.SceneView","Set",size.X,size.Y,0d,0d,0d,0f,0f,-1f);
            Call("Backend.GuestPassList","SubmitFrame",submit);
            if(async)backend.EndFrameAsync();else backend.EndFrame();
        }
        void Pixels(byte red,byte green,byte blue)
        {
            var data=backend.Readback(64,64);int at=(32*64+32)*4;
            Console.WriteLine("native postprocess final pixel="+string.Join(",",data.Skip(at).Take(4)));
            Check(Math.Abs(data[at]-red)<=2&&Math.Abs(data[at+1]-green)<=2&&Math.Abs(data[at+2]-blue)<=2,"selected native FXAA shader executes expected replacement pixels");
        }
        string root=Path.Combine(output,"NativePostPack"),overlay=Path.Combine(root,"Overlay/Post.FXAA");Directory.CreateDirectory(overlay);
        string shader=Path.Combine(overlay,"Fxaa.hlsl");File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"fixture.native.post\"}");
        void Register(string code){File.WriteAllText(shader,code);Call("Backend.GuestPassList","RegisterRoot","fixture.native.post",root,null);}
        string owned=Path.Combine(root,"Overlay/Anomaly.HistoryColor/HistoryCopy.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(owned));
        void Owned(float value)=>File.WriteAllText(owned,"float4 __pixel_shader(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target{return float4("+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+",0,0,1);}");
        object History()=>((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)p.GetType().GetField("Id").GetValue(p)=="anomaly.owned.HistoryCopy");
        T Field<T>(string name)=>(T)History().GetType().GetField(name).GetValue(History());
        void OwnedPixel(float expected)
        {
            int w=Field<int>("ImageWidth"),h=Field<int>("ImageHeight");var bytes=new byte[w*h*8];
            typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(backend,new object[]{Field<ulong>("StorageImage"),bytes});
            Check(Enumerable.Range(0,w*h).All(i=>Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*8))-expected)<.002),"coordinated owned output matches generation color "+expected);
        }
        string a="float4 __pixel_shader(float4 p:SV_Position):SV_Target{return float4(.25,.5,.75,1);}";
        try
        {
            Frame();var stock=backend.Readback(64,64);Check(backend.GetPostprocessGeneration(13).Active==0,"stock native stage starts without a pack generation");
            Owned(2);Register(a);Frame();Pixels(64,128,191);OwnedPixel(2);string ownedA=Field<string>("Generation");
            var first=backend.GetPostprocessGeneration(13);Check(first.Active!=0&&first.Executions>0&&first.LastCompletion<=first.Completed,"native stage execution status records completed generation A");
            Check(backend.ActivePostprocessGeneration.Token==first.Active&&backend.ActivePostprocessGeneration.Layouts[13].SourceGenerationSha256==backend.ActivePostprocessGeneration.Fingerprint,"managed immutable metadata matches native active generation");
            // Keep A outstanding, then prepare and commit B through the actual host boundary.
            Frame(true);string injection=Path.Combine(root,"Inject/Post.FXAA/Color.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(injection));File.WriteAllText(injection,"float NativeGreen(){return .125;}");
            Owned(4);Register("float4 __pixel_shader():SV_Target{return float4(.75,NativeGreen(),.25,1);}");
            Call("Backend.GuestNamedStages","Prepare",assets);Check(Call("Backend.GuestPostprocessStages","Prepare") is true,"coordinated B prepares both owners");
            // Another valid native transaction invalidates B's expected revision before commit.
            ulong intervening=backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{0,library.Load(PostprocessShaderVariants.Stages[0])}},"intervening");
            backend.QueuePostprocessGeneration(intervening);Check(backend.ApplyPendingPostprocessGeneration(),"intervening native transaction advances revision");
            Prepare();Check(Field<string>("Generation")==ownedA&&backend.GetPostprocessGeneration(13).Active==first.Active,"stale native activation retains both previous owners");
            Frame(prepare:false);Pixels(64,128,191);OwnedPixel(2);
            Frame(true);Pixels(191,32,64);OwnedPixel(4);Check(Field<string>("Generation")!=ownedA,"retry rebuilds discarded owned candidate alongside native B");
            var second=backend.GetPostprocessGeneration(13);Check(second.Active!=first.Active&&second.Executions>0,"pipelined native replacement activates a new executed generation");
            var state=Call("Shaders.ShaderPackRegistry","GetNativeStageState","Post.FXAA");
            var variants=((System.Collections.IEnumerable)state.GetType().GetProperty("Variants").GetValue(state)).Cast<object>().ToArray();
            Check(variants.Length==1&&(ulong)variants[0].GetType().GetProperty("ActiveToken").GetValue(variants[0])==second.Active,"public named-stage query reports actual native activation");
            Register("#error native-post-replacement-failed\n");Frame();Pixels(191,32,64);
            Check(backend.GetPostprocessGeneration(13).Active==second.Active&&((string)Property("Backend.GuestPostprocessStages","Error")).Contains("native-post-replacement-failed"),"failed native compilation retains prior generation");
            // Fail after a valid stage has been built, exercising detached cleanup.
            string bad=Path.Combine(output,"PostBadLocalSize.hlsl");File.WriteAllText(bad,"[numthreads(4,4,1)]void __compute_shader(uint3 id:SV_DispatchThreadID){}");
            using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"PostGenerationCache")))
            {
                var artifact=VulkanShaderArtifact.FromCompilation(compiler.Compile(new VulkanShaderRequest{SourcePath=bad,EntryPoint="__compute_shader",Profile="cs_5_0"}),"bad-local-size");
                bool rejected=false;try{backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{0,library.Load(PostprocessShaderVariants.Stages[0])},{9,artifact}},"bad");}catch(Exception error){rejected=error.Message.Contains("local size");}
                Check(rejected&&backend.GetPostprocessGeneration(13).Prepared==0&&backend.GetPostprocessGeneration(13).Active==second.Active,"invalid final compute stage cleans up detached batch and preserves active shaders");
            }
            using(var compiler=VulkanShaderCompiler.CreateBundled(Path.Combine(output,"PostGenerationCache")))
            {
                string incomplete=Path.Combine(output,"PostIncompleteMrt.hlsl");File.WriteAllText(incomplete,"float __pixel_shader():SV_Target0{return 1;}");
                var artifact=VulkanShaderArtifact.FromCompilation(compiler.Compile(new VulkanShaderRequest{SourcePath=incomplete,EntryPoint="__pixel_shader",Profile="ps_5_0"}),"incomplete-mrt");
                bool rejected=false;try{backend.PreparePostprocessGeneration(new Dictionary<uint,VulkanShaderArtifact>{{34,artifact}},"incomplete-mrt");}catch(Exception error){rejected=error.Message.Contains("output contract incomplete");}
                Check(rejected&&backend.GetPostprocessGeneration(13).Prepared==0,"incomplete eight-target HBAO output is refused before pipeline creation");
            }
            Call("Backend.GuestPassList","UnregisterRoot","fixture.native.post");Frame();
            var restored=backend.Readback(64,64);Check(stock.SequenceEqual(restored),"native stage unregister restores exact stock pixels");
            Frame();Check(backend.GetPostprocessGeneration(13).Prepared==0&&backend.GetPostprocessGeneration(13).Retired==0,"postprocess candidate and retirement counts return to zero");
            // Only the include changes; the stock entry point must consume its replacement.
            File.Delete(shader);File.Delete(owned);File.Delete(injection);
            string header;
            // Game root is supplied by the loaded shader library, rather than the Anomaly assets folder.
            string game=Environment.GetCommandLineArgs().Last();
            header=File.ReadAllText(Path.Combine(Path.GetDirectoryName(game),"Content/Shaders/Postprocess/Fxaa3_11.hlsli"));
            File.WriteAllText(Path.Combine(overlay,"Fxaa3_11.hlsli"),header.Replace("FxaaPixelShader","AnomalyOriginalFxaaPixelShader")+"\n#define FxaaPixelShader(a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p) float4(.375,.625,.125,1)\n");
            Call("Backend.GuestPassList","RegisterRoot","fixture.native.post",root,null);Frame();Pixels(96,159,32);
            Check(backend.GetPostprocessGeneration(13).Active!=second.Active,"include-only overlay activates an executed native generation");
            ulong formatGeneration=backend.GetPostprocessGeneration(13).Active;
            backend.SetHdrQuality(false);Frame();Pixels(96,159,32);
            Check(backend.GetPostprocessGeneration(13).Active!=formatGeneration,"unchanged registration rebuilds after HDR format changes");
            formatGeneration=backend.GetPostprocessGeneration(13).Active;Frame();Check(backend.GetPostprocessGeneration(13).Active==formatGeneration,"unchanged format and sources reuse active generation");
            backend.SetHdrQuality(true);Frame();Pixels(96,159,32);
            Check(backend.GetPostprocessGeneration(13).Active!=formatGeneration,"returning HDR format rebuilds the matching generation");
            Call("Backend.GuestPassList","UnregisterRoot","fixture.native.post");Frame();Check(stock.SequenceEqual(backend.Readback(64,64)),"include-only unregister restores stock output");
            Check(backend.GetStats().ValidationErrors==0,"native postprocess generation sync/pipelined/rollback path has zero validation errors");
        }
        finally{Call("Backend.GuestPassList","UnregisterRoot","fixture.native.post");}
        return checks;
    }
}
