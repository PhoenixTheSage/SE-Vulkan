using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

partial class Program
{
    static int AtmosphereGenerationRegression(Assembly core,string assets,string output,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.native.atmosphere",imageName=owner+".image";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        core.GetType("ClientPlugin.Backend.GuestPostprocessStages",true).GetField("PreparationBound",stat).SetValue(null,true);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);
        var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        var read=typeof(NativeBackend).GetMethod("GuestReadback",BindingFlags.Instance|BindingFlags.NonPublic);
        var post=new VulkanPostprocess();post.Hbao.Enabled=false;post.Ssao.Enabled=false;post.Settings.BloomEnabled=false;post.Settings.EnableEyeAdaptation=false;
        var c=new ReflectedConstantBuffer(backend.EnvironmentLayouts[1],"AtmosphereConstants");c.Set("PlanetCenter",0,0,-80);c.Set("RadiusAtmosphere",7632000);c.Set("RadiusGround",6423600);c.Set("BetaRayleighScattering",5.8e-6f/20,1.35e-5f/7.5f,3.31e-5f/10);c.Set("BetaMieScattering",4e-7f,4e-7f,4e-7f);c.Set("MieG",.9998f);c.Set("HeightScaleRayleighMie",80000,60000);c.Set("PlanetScaleFactor",50f/6360000);c.Set("AtmosphereScaleFactor",.4f);c.Set("Intensity",35);c.Set("WorldViewProj",NativeBackend.MatrixValues(projection));
        var atmo=NativeEnvironmentDraw.Create(0,94101,c.Bytes);uint frame=0;
        string root=Path.Combine(output,"AtmospherePack-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string name,string text){string file=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text);}
        void Register()=>Call("Backend.GuestPassList","RegisterRoot",owner,root,null);
        float[] Frame(bool pipelined=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();post.Configure(backend,64,64,false,view:MatrixD.Identity,projection:projection);backend.QueueEnvironment(new(){atmo});
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,postprocess:post.Settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"atmosphere generation frame begins; pipelined="+pipelined);
            Check(Call("Backend.GuestFrame","Capture") is true,"atmosphere generation captures immutable frame");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            var history=((System.Collections.IEnumerable)Call("Backend.GuestPassList","Snapshot")).Cast<object>().Single(p=>(string)programType.GetField("Id").GetValue(p)=="anomaly.owned.HistoryCopy");ulong id=(ulong)programType.GetField("StorageImage").GetValue(history);var bytes=new byte[64*64*8];read.Invoke(backend,new object[]{id,bytes});return Enumerable.Range(0,64*64*4).Select(i=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*2))).ToArray();
        }
        void Pixel(float[] data,float r,float g,float b){Console.WriteLine("atmosphere HDR="+string.Join(",",data.Take(4)));Check(Enumerable.Range(0,64*64).All(i=>Math.Abs(data[i*4]-r)<.003&&Math.Abs(data[i*4+1]-g)<.003&&Math.Abs(data[i*4+2]-b)<.003),"owning atmosphere invocation produces expected HDR "+r+","+g+","+b);}
        void Compute(string value)=>Write("Overlay/Atmosphere/AtmospherePrecompute.hlsl","RWTexture2D<float2> Out:register(u0);[numthreads(8,8,1)]void __compute_shader(uint3 at:SV_DispatchThreadID){Out[at.xy]="+value+";}");
        void PixelShader(string source)=>Write("Overlay/Atmosphere/AtmosphereGBuffer.hlsl",source);
        const string reader="Texture2D<float2> Lut:register(t5);float4 __pixel_shader():SV_Target{return float4(Lut.Load(int3(0,0,0)),.125,0);}";
        try {
            Check((GuestBridge.SupportedPacketFlags&16777216)!=0,"native atmosphere owner generations advertise bit24");
            var stock=Frame();ulong initial=backend.GetEnvironmentStats().LutDispatches;
            Write("anomaly.json","{\"id\":\""+owner+"\"}");Compute("float2(.25,.75)");PixelShader(reader);Register();var first=Frame();
            Check(Property("Backend.GuestPostprocessStages","Error")==null,"actual core prepares captured atmosphere generation: "+Property("Backend.GuestPostprocessStages","Error"));Pixel(first,.25f,.75f,.125f);
            var a=backend.GetPostprocessGeneration(512);var b=backend.GetPostprocessGeneration(513);
            Check(a.Active!=0&&a.Active==b.Active&&a.Executions==1&&b.Executions==1&&a.LastFrame==frame&&b.LastFrame==frame,"compute and raster atmosphere activate atomically and report actual current-frame execution");
            Check(backend.GetEnvironmentStats().LutDispatches==initial+1,"compute generation invalidates unchanged cached LUT parameters");
            Pixel(Frame(true),.25f,.75f,.125f);Check(backend.GetPostprocessGeneration(512).Executions==1,"unchanged generation reuses completed LUT without redispatch");
            Compute("float2(.5,.25)");Register();Pixel(Frame(true),.5f,.25f,.125f);ulong generationB=backend.GetPostprocessGeneration(512).Active;Check(generationB!=a.Active,"reload replaces a LUT generation while preceding submissions can be outstanding");
            Compute("float2(BrokenSyntax)");Register();Pixel(Frame(),.5f,.25f,.125f);Check(Property("Backend.GuestPostprocessStages","Error") is string&&backend.GetPostprocessGeneration(512).Active==generationB,"failed atmosphere compilation preserves the full preceding generation");
            Compute("float2(.125,.375)");Write("Inject/Atmosphere/Constants.hlsli","#define ATMO_BIAS .0625\n");PixelShader("Texture2D<float2> Lut:register(t5);float4 __pixel_shader():SV_Target{return float4(Lut.Load(int3(0,0,0))+ATMO_BIAS,.125,0);}");Register();Pixel(Frame(),.1875f,.4375f,.125f);
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,imageName,1,1,10) is true,"atmosphere catalog target requested");Call("Buffers.CatalogImages","ClearColorTarget",imageName,2f,3f,4f,-1f);Call("Shaders.ShaderBindRegistry","RequestSrv","Atmosphere",imageName,7);
            PixelShader("Texture2D<float4> Input:register(t7);cbuffer Extras:register(b6){float2 RenderSize;}float4 __pixel_shader():SV_Target{return float4(Input.Load(int3(0,0,0)).rg,RenderSize.x/64,0);}");Register();Pixel(Frame(true),2,3,1);
            Check(backend.GetStageBindingState().Resolved>0,"atmosphere catalog/extras bind through native descriptor status");
            backend.SetHdrQuality(false);Pixel(Frame(),2,3,1);backend.SetHdrQuality(true);Pixel(Frame(true),2,3,1);
            var publicState=Call("Shaders.ShaderPackRegistry","GetNativeStageState","Atmosphere");var variants=((System.Collections.IEnumerable)publicState.GetType().GetProperty("Variants").GetValue(publicState)).Cast<object>().ToArray();Check(variants.Length==3&&variants.Where(v=>(uint)v.GetType().GetProperty("Variant").GetValue(v)<514).All(v=>(ulong)v.GetType().GetProperty("Executions").GetValue(v)>0),"public atmosphere status identifies both executed main-view owners");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Frame();Check(restored.Zip(stock,(x,y)=>Math.Abs(x-y)<.003f).All(v=>v),"unregister restores original stock atmosphere and cached-LUT behavior");
            Frame(true);Frame();Check(backend.GetPostprocessGeneration(512).Retired==0,"atmosphere pipelines and descriptor layouts retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"atmosphere generations have zero core/synchronization errors");
        } finally {Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetHdrQuality(true);}
        return checks;
    }
}
