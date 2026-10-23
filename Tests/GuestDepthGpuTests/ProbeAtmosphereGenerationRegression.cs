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
    static int ProbeAtmosphereGenerationRegression(Assembly core,string assets,string output,string bin,NativeBackend backend,MatrixD projection,MyEnvironmentLightData light)
    {
        const BindingFlags stat=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
        const string owner="fixture.native.probe.atmosphere",catalog="fixture.native.atmosphere.image";
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        object Property(string type,string name)=>core.GetType("ClientPlugin."+type,true).GetProperty(name,stat).GetValue(null);
        var programType=core.GetType("ClientPlugin.Backend.GuestProgram",true);var submit=Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(typeof(IReadOnlyList<>).MakeGenericType(programType)),core.GetType("ClientPlugin.Backend.GuestBridgeClient",true).GetMethod("Submit",stat));
        using(var file=File.OpenRead(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"))){var image=DdsImage.Read(file,true);backend.UploadTextureMips(953001,image.Width,image.Height,image.Format,image.Mips,false);}
        var c=new ReflectedConstantBuffer(backend.EnvironmentLayouts[1],"AtmosphereConstants");c.Set("PlanetCenter",0,0,-80);c.Set("RadiusAtmosphere",7632000);c.Set("RadiusGround",6423600);c.Set("BetaRayleighScattering",5.8e-6f/20,1.35e-5f/7.5f,3.31e-5f/10);c.Set("BetaMieScattering",4e-7f,4e-7f,4e-7f);c.Set("MieG",.9998f);c.Set("HeightScaleRayleighMie",80000,60000);c.Set("PlanetScaleFactor",50f/6360000);c.Set("AtmosphereScaleFactor",.4f);c.Set("Intensity",35);c.Set("WorldViewProj",NativeBackend.MatrixValues(projection));var atmo=NativeEnvironmentDraw.Create(0,953002,c.Bytes);
        var oldConfig=backend.CurrentProbeConfig;var probes=new VulkanProbes{Resolution=16};probes.Settings.TimeOut=0;float time=0;uint frame=0;
        float[][] Frame(bool pipelined=false,bool capacity=false){
            Call("Backend.RenderHost","PrepareShaderGenerations");backend.BeginPacketCollection();backend.QueueEnvironment(new(){atmo});backend.SetProbeAtmosphere(atmo.Id,35,0,Vector3.Forward);time+=.1f;probes.Configure(backend,Vector3D.Zero,light,MyPostprocessSettings.Default,MatrixD.Identity,953001,.1f,time);
            backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"probe atmosphere owner frame begins; pipelined="+pipelined);Check(Call("Backend.GuestFrame","Capture") is true,"probe captures the shared immutable frame");frame=(uint)Call("Backend.GuestFrame","U32",16);Call("Backend.SceneView","Set",64,64,0d,0d,0d,0f,0f,-1f);Call("Backend.GuestPassList","SubmitFrame",submit);
            if(capacity){
                var method=typeof(NativeBackend).GetMethod("SetStageBindings",BindingFlags.Instance|BindingFlags.NonPublic);
                var ids=Enumerable.Range(0,62).Concat(Enumerable.Range(128,16)).Concat(Enumerable.Range(256,4)).Concat(Enumerable.Range(512,3)).Concat(new[]{770,772}).Concat(new[]{1025,1027,1029,1031,1033}).Concat(Enumerable.Range(1280,4)).Concat(Enumerable.Range(1536,8)).Concat(new[]{1545}).Concat(new[]{1792,1793,2048}).Concat(Enumerable.Range(0,20).Select(i=>2305+2*i)).Select(i=>(uint)i).ToArray();
                Check(ids.Length==128,"all current native raster/compute owners, including glass/holo PS owners, require128 possible binding rows");
                object[] Args(uint[] values)=>new object[]{frame,values,new ulong[values.Length*10],new uint[values.Length],new uint[values.Length],new uint[values.Length],0UL};
                Check(method.Invoke(backend,Args(ids)) is true&&backend.GetStageBindingState().Rows==128,"managed/native transport accepts every current owner in one immutable frame");
                try{method.Invoke(backend,Args(new uint[138]));throw new Exception("oversized native binding packet accepted");}catch(TargetInvocationException error){Check(error.InnerException is ArgumentException,"binding transport refuses rows beyond its coordinated137-row bound");}
                Check(backend.GetStageBindingState().Rows==128,"oversized packet cannot alter copied native binding rows");
            }
            if(pipelined)backend.EndFrameAsync();else backend.EndFrame();
            return Enumerable.Range(0,12).Select(i=>{var bytes=backend.ReadbackProbe((uint)(i/6),(uint)(i%6),0,16);return Enumerable.Range(0,bytes.Length/2).Select(j=>(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,j*2))).ToArray();}).ToArray();
        }
        void Pixels(float[][] data,float[] a,float[] b){foreach(int target in new[]{0,1})foreach(int face in Enumerable.Range(0,6)){var expected=target==0?a:b;var pixels=data[target*6+face];Check(Enumerable.Range(0,16*16).All(i=>Enumerable.Range(0,3).All(k=>Math.Abs(pixels[i*4+k]-expected[k])<.002f)),"probe MRT"+target+" face"+face+" has independently expected shader values");}}
        string root=Path.Combine(output,"ProbeAtmospherePack-"+Guid.NewGuid().ToString("N"));string filePath=Path.Combine(root,"Overlay/Atmosphere/AtmosphereEnv.hlsl");Directory.CreateDirectory(Path.GetDirectoryName(filePath));File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\""+owner+"\"}");
        void Register(string shader){File.WriteAllText(filePath,shader);Call("Backend.GuestPassList","RegisterRoot",owner,root,null);}
        const string shader="Texture2D<float4> Input:register(t7);struct Out{float4 a:SV_Target0;float4 b:SV_Target1;};void __pixel_shader(out Out o){float3 v=Input.Load(int3(0,0,0)).rgb;o.a=float4(v,0);o.b=float4(v.brg,0);}";
        try {
            var stock=Frame();var repeat=Frame(true);Check(stock.Zip(repeat,(x,y)=>x.SequenceEqual(y)).All(v=>v),"stock six-face probe atmosphere repeat control is deterministic");
            Check(Call("Buffers.CatalogImages","EnsureColorTarget",owner,catalog,1,1,10) is true,"probe atmosphere shared catalog target accepted");Call("Shaders.ShaderBindRegistry","RequestSrv","Atmosphere",catalog,7);Call("Buffers.CatalogImages","ClearColorTarget",catalog,.25f,.5f,.75f,0f);Register(shader);Pixels(Frame(true),new[]{.25f,.5f,.75f},new[]{.75f,.25f,.5f});
            var state=backend.GetPostprocessGeneration(514);Check(state.Active!=0&&state.Executions==6&&state.LastFrame==frame&&state.LastCompletion<=state.Completed,"selected probe shader executes and completes on all six actual faces");
            Call("Buffers.CatalogImages","ClearColorTarget",catalog,.5f,.25f,.125f,0f);Pixels(Frame(),new[]{.5f,.25f,.125f},new[]{.125f,.5f,.25f});Check(backend.GetStageBindingState().Resolved>=6,"native probe invocation resolves copied catalog bindings separately for each face");
            const string scope="cbuffer View:register(b2){row_major float4x4 V;row_major float4x4 P;float3 Sun;float Size;}cbuffer Extras:register(b6){float2 RenderSize;}struct Out{float4 a:SV_Target0;float4 b:SV_Target1;};void __pixel_shader(out Out o){o.a=float4(Size/16,.5,.25,0);o.b=float4(RenderSize.x/64,.25,.5,0);}";
            Register(scope);Pixels(Frame(true),new[]{1f,.5f,.25f},new[]{1f,.25f,.5f});ulong token=backend.GetPostprocessGeneration(514).Active;Check(token!=state.Active,"probe generation reload activates scope-specific constants and immutable frame extras");
            Register("float4 __pixel_shader():SV_Target0{return 0;}");Pixels(Frame(),new[]{1f,.5f,.25f},new[]{1f,.25f,.5f});Check((Property("Backend.GuestPostprocessStages","Error") as string)?.Contains("output missing")==true&&backend.GetPostprocessGeneration(514).Active==token,"missing probe MRT output retains the previous complete pipeline and layout");
            Call("Backend.GuestPassList","UnregisterRoot",owner);var restored=Frame(true);Check(restored.Zip(stock,(x,y)=>x.SequenceEqual(y)).All(v=>v),"unregister restores the exact stock probe output across both MRTs and all faces");Frame(capacity:true);Check(backend.GetPostprocessGeneration(514).Retired==0,"probe pipeline/layout generations retire after completed submissions");
            Check(backend.GetStats().ValidationEnabled!=0&&backend.GetStats().ValidationErrors==0,"probe owner has zero core/synchronization errors");
        } finally {
            Call("Backend.GuestPassList","UnregisterRoot",owner);Call("Buffers.BufferCatalog","UnpublishAll",owner);backend.SetProbeAtmosphere(0,0,0,Vector3.Forward);
            if(oldConfig.Size==8096&&oldConfig.Frames!=null)backend.SetProbes(oldConfig);else backend.SetProbes(new NativeProbeConfig{Size=8096,AbiVersion=NativeBackend.AbiVersion,Resolution=16,Frames=new byte[7680],Projections=new float[96]});
        }
        return checks;
    }
}
