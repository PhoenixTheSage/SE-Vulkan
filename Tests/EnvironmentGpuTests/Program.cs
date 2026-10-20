using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using ClientPlugin.Rendering.Shaders;
using VRage;
using VRage.Import;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Messages;

class Program
{
    static int Main(string[] args){string bin=Path.GetFullPath(args[0]);AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};try{return Run(bin,args.Contains("--normal-hdr"));}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string bin,bool normalHdr)
    {
        int checks=0;void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        string output=Path.GetFullPath("TestResults/EnvironmentGpuTests");Directory.CreateDirectory(output);
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));if(normalHdr)backend.SetHdrQuality(false);
        Check(backend.EnvironmentLayouts.Count==10,"ten authenticated stock planet/foliage/probe stages initialize");Check(Marshal.SizeOf<NativeEnvironmentDraw>()==192&&Marshal.SizeOf<NativeFoliageDraw>()==168&&Marshal.SizeOf<NativeFoliagePoint>()==16,"copied native ABI layouts");
        MatrixD projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,200000);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunDiscIntensity=0;light.SunColorRaw=Vector3.One;light.SunDiffuseFactor=1;
        var settings=new MyRenderSettings();settings.User.GrassDrawDistance=100;settings.GrassGeometryScalingNearDistance=50;settings.GrassGeometryScalingFarDistance=350;settings.GrassGeometryDistanceScalingFactor=5;
        NativeEnvironmentDraw Atmosphere(float height=80000){var c=new ReflectedConstantBuffer(backend.EnvironmentLayouts[1],"AtmosphereConstants");c.Set("PlanetCenter",0,0,-80);c.Set("RadiusAtmosphere",7632000);c.Set("RadiusGround",6423600);c.Set("BetaRayleighScattering",5.8e-6f/20,1.35e-5f/7.5f,3.31e-5f/10);c.Set("BetaMieScattering",4e-7f,4e-7f,4e-7f);c.Set("MieG",.9998f);c.Set("HeightScaleRayleighMie",height,60000);c.Set("PlanetScaleFactor",50f/6360000);c.Set("AtmosphereScaleFactor",.4f);c.Set("Intensity",35);c.Set("WorldViewProj",NativeBackend.MatrixValues(projection));return NativeEnvironmentDraw.Create(0,1,c.Bytes);}
        byte[] Render(List<NativeEnvironmentDraw> environment=null,List<NativeFoliageDraw> foliage=null,byte[] table=null,MyRenderFogSettings? fog=null,float time=0,bool opaque=false){backend.BeginPacketCollection();if(opaque)backend.AddWorldPacket(0,2,3,4,4,4,WorldConstants.Object(backend.WorldLayouts[0],MatrixD.CreateTranslation(0,0,.5),Vector3.One,Vector3.Zero,1,0),flags:512);if(environment!=null)backend.QueueEnvironment(environment);if(foliage!=null)backend.QueueFoliage(foliage,table);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light,fog:fog,time:time,renderSettings:settings),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));Check(backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black),"environment frame begins");backend.RequestReadback();backend.EndFrame();return backend.Readback(64,64);}
        byte[] baseline=Render(),atmo=Render(new(){Atmosphere()});Check(atmo.Where((v,i)=>i%4!=3&&v>baseline[i]+4).Count()>100,"stock atmosphere changes sky pixels");Check(backend.GetEnvironmentStats().LutDispatches==1,"atmosphere dispatches optical-depth LUT once");
        byte[] cached=Render(new(){Atmosphere()});Check(cached.SequenceEqual(atmo)&&backend.GetEnvironmentStats().LutDispatches==1,"unchanged atmosphere reuses LUT deterministically");Render(new(){Atmosphere(40000)});Check(backend.GetEnvironmentStats().LutDispatches==2,"height/scattering change recomputes LUT");
        NativeWorldVertex V(float x,float y)=>new(){Material=new NativeGBufferVertex{X=x,Y=y,Z=-2,Scale=1,NormalX=VF_Packer.PackNormal(Vector3.Backward)&65535,NormalY=VF_Packer.PackNormal(Vector3.Backward)>>16,U=(x+1)/2,V=(y+1)/2}};
        backend.BeginUploads();backend.UploadWorldMesh(2,new[]{V(-1,-1),V(1,-1),V(0,1)},new uint[]{0,1,2});backend.UploadTexture(3,1,1,0,new byte[]{220,220,220,255},true);backend.UploadTexture(4,1,1,0,new byte[]{255,255,255,255},false);backend.UploadVoxelArray(5,1,1,0,true,1,1,new byte[]{0,220,0,255});backend.UploadVoxelArray(6,1,1,0,false,1,1,new byte[]{128,128,255,0});
        var points=Enumerable.Range(0,32).Select(i=>new NativeFoliagePoint{X=(i%8-4)*.18f,Y=-.6f,Z=-2.5f-(i/8)*.2f,NormalSeedMaterial=127|191u<<8|(uint)i<<16}).ToArray();backend.UploadFoliage(7,points);backend.EndUploads();
        var cb=new ReflectedConstantBuffer(backend.EnvironmentLayouts[2],"CloudsConstants");cb.Set("World",NativeBackend.MatrixValues(MatrixD.Identity));cb.Set("ViewProj",NativeBackend.MatrixValues(projection));cb.Set("Color",1,1,1,1);var cloud=NativeEnvironmentDraw.Create(1,8,cb.Bytes);cloud.Mesh=2;cloud.Color=3;cloud.Alpha=4;
        byte[] cloudy=Render(new(){cloud});Check(cloudy.Where((v,i)=>i%4!=3&&v>baseline[i]+10).Count()>50,"stock cloud shell blends visible pixels");Check(backend.GetEnvironmentStats().CloudDraws>0,"cloud command counters observed");var opaque=Render(opaque:true);var occluded=Render(new(){cloud},opaque:true);Check(opaque.SequenceEqual(occluded),"opaque terrain depth occludes a cloud shell");byte[] fogged=Render(new(){cloud},fog:new MyRenderFogSettings{FogColor=Color.Red,FogDensity=1,FogMultiplier=1,FogSkybox=1});Check(!fogged.SequenceEqual(cloudy),"stock weather fog changes cloud and sky pixels");
        byte[] table=new byte[4096];Buffer.BlockCopy(BitConverter.GetBytes(.6f),0,table,0,4);Buffer.BlockCopy(BitConverter.GetBytes(1f),0,table,4,4);Buffer.BlockCopy(BitConverter.GetBytes(1u),0,table,12,4);
        var draw=new NativeFoliageDraw{Size=168,Mesh=7,Color=5,Normal=6,Constants=WorldConstants.Object(backend.EnvironmentLayouts[4],MatrixD.Identity,Vector3.One,Vector3.Zero,1,0)};
        byte[] grass=Render(foliage:new(){draw},table:table);Check(grass.Where((v,i)=>i%4!=3&&v!=baseline[i]).Count()>30,"stock geometry shader expands grass into GBuffer pixels");settings.WindStrength=2;settings.WindAzimuth=90;var windy=Render(foliage:new(){draw},table:table,time:4);Check(!windy.SequenceEqual(grass),"stock wind and render time bend grass pixels");settings.WindStrength=0;draw.Kind=1;byte[] rocks=Render(foliage:new(){draw},table:table);Check(!rocks.SequenceEqual(grass),"pebble geometry differs from grass");Check(backend.GetEnvironmentStats().FoliagePoints==96,"foliage command counts reflect persistent points");
        backend.BeginUploads();backend.UploadVoxelArray(5,1,1,0,true,1,1,new byte[]{0,220,0,0});backend.EndUploads();draw.Kind=0;var cutout=Render(foliage:new(){draw},table:table);Check(cutout.SequenceEqual(baseline),"foliage alpha cutout removes transparent blades");
        MyVertexFormatVoxelSingleData P(float x,float z)=>new(){Position=new Vector3(x,0,z),Material=new Byte4(),PackedNormal=new Byte4{PackedValue=VF_Packer.PackNormal(Vector3.Up)}};
        var terrain=new[]{P(0,0),P(2,0),P(0,2)};uint[] indices={0,1,2};var generated=VulkanFoliage.GeneratePoints(terrain,indices,0,0,10,1,MatrixD.Identity);var again=VulkanFoliage.GeneratePoints(terrain,indices,0,0,10,1,MatrixD.Identity);Check(generated.Length==20&&generated.Select(p=>p.NormalSeedMaterial).SequenceEqual(again.Select(p=>p.NormalSeedMaterial))&&generated.Select(p=>p.X).SequenceEqual(again.Select(p=>p.X)),"stock triangle distribution is persistent and deterministic");Check(generated.All(p=>p.X>=0&&p.Z>=0&&p.X+p.Z<=2.0001f),"generated points stay within their source triangle");
        CelestialDepthRegression.Run(backend,()=>Render(opaque:true),Check,output);
        using(var environment=new VulkanEnvironment(backend,null)){
            environment.Process(new MyRenderMessageCreateRenderEntityAtmosphere{ID=9,WorldMatrix=MatrixD.CreateTranslation(0,0,-80),PlanetRadius=50,AtmosphereRadius=60});
            void ManagedFrame(){backend.BeginPacketCollection();environment.Draw(Vector3D.Zero,projection);backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,64,64,light),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));backend.BeginFrame(projection,Vector3.Forward,Vector3.One,Vector3.Zero,Color.Black);backend.EndFrame();}
            var before=backend.GetEnvironmentStats().AtmosphereDraws;ManagedFrame();Check(backend.GetEnvironmentStats().AtmosphereDraws==before+1,"managed atmosphere submits stock native scattering");
            environment.Process(new MyRenderMessageEnableAtmosphere{Enabled=false});ManagedFrame();Check(backend.GetEnvironmentStats().AtmosphereDraws==before+1,"stock atmosphere disable suppresses submissions");
            environment.Process(new MyRenderMessageEnableAtmosphere{Enabled=true});ManagedFrame();Check(backend.GetEnvironmentStats().AtmosphereDraws==before+2,"stock atmosphere re-enable resumes submissions");
        }
        backend.BeginPacketCollection();backend.RemoveResource(7,1);backend.RemoveResource(3,2);backend.RemoveResource(3,7);backend.RemoveResource(0,3);backend.RemoveResource(0,4);backend.RemoveResource(5,5);backend.RemoveResource(5,6);Render();var stats=backend.GetStats();Check(backend.GetEnvironmentStats().Luts==0,"unload retires atmosphere LUT");Check(stats.ValidationEnabled!=0&&stats.ValidationErrors==0,"zero Vulkan core and synchronization validation errors");File.WriteAllText(Path.Combine(output,normalHdr?"result-normal-hdr.json":"result.json"),JsonSerializer.Serialize(new{Checks=checks,HighQualityHdr=!normalHdr,ValidationErrors=stats.ValidationErrors,Environment=backend.GetEnvironmentStats()},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));return 0;
    }
}
