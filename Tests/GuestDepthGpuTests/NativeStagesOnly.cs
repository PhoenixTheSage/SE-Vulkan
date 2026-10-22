using System.Reflection;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRage.FileSystem;
using VRageMath;
using VRageRender;

partial class Program
{
    // Focused actual-core gate for native owner work; the full recreation gate remains required.
    static int NativeStagesOnly(string bin,bool bindingsOnly=false,bool oldBindingHost=false,bool catalogLifecycle=false,bool lateHistory=false,bool classicNulls=false,bool oldClassicHost=false,bool atmosphereOnly=false,bool environmentBatch=false,bool oldEnvironmentHost=false,bool decalsOnly=false,bool probeProcessingOnly=false,bool oldProbeProcessingHost=false,bool particlesOnly=false,bool oldParticleHost=false,bool shadowsOnly=false,bool oldShadowHost=false,bool dependenciesOnly=false,bool oitOnly=false,bool oldOitHost=false,bool geometryOnly=false,bool oldGeometryHost=false,bool geometryFamily=false,bool geometryDepth=false)
    {
        string root=Directory.GetCurrentDirectory(),output=Path.GetFullPath(oldGeometryHost?"TestResults/GeometryOldHostTests":oldOitHost?"TestResults/OitOldHostTests":oldShadowHost?"TestResults/ShadowOldHostTests":oldParticleHost?"TestResults/ParticleOldHostTests":oldProbeProcessingHost?"TestResults/ProbeProcessingOldHostTests":oldEnvironmentHost?"TestResults/EnvironmentOldHostTests":oldClassicHost?"TestResults/ClassicNullOldHostTests":oldBindingHost?"TestResults/NativeBindingOldHostTests":"TestResults/NativeStagesGpuTests"),stage=Path.Combine(output,"Assets");
        Directory.CreateDirectory(Path.Combine(stage,"Native/win-x64"));Directory.CreateDirectory(Path.Combine(stage,"Shaders/Compiled"));
        string native=Environment.GetEnvironmentVariable("ANOMALY_TEST_NATIVE_DLL");
        File.Copy(string.IsNullOrWhiteSpace(native)?Path.Combine(root,"Native/build/Release/se_vulkan_native.dll"):Path.GetFullPath(native),Path.Combine(stage,"Native/win-x64/se_vulkan_native.dll"),true);
        foreach(string file in Directory.GetFiles(Path.Combine(root,"Assets/Shaders/Compiled")))File.Copy(file,Path.Combine(stage,"Shaders/Compiled",Path.GetFileName(file)),true);
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR",stage);
        MyFileSystem.Init(Path.GetFullPath(Path.Combine(bin,"../Content")),Path.Combine(output,"UserData"));
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),Path.Combine(bin,"../Content/Shaders"),Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(64,64,0,true,Path.Combine(output,"Cache"));backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,Path.Combine(bin,"../Content/Shaders"));
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        typeof(GuestBridge).GetMethod("Bind",stat).Invoke(null,new object[]{backend,null});
        string anomaly=Path.GetFullPath(Path.Combine(root,"../Anomaly")),assets=Path.Combine(anomaly,"Assets");var core=Assembly.LoadFrom(Path.Combine(anomaly,"ClientPlugin/bin/Release/net10.0/Anomaly.dll"));
        object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,stat).Invoke(null,args);
        var host=core.GetType("ClientPlugin.Backend.RenderHost",true);host.GetField("committed",stat).SetValue(null,true);host.GetField("choice",stat).SetValue(null,1);host.GetField("assetFolder",stat).SetValue(null,assets);
        if(Call("Backend.GuestDevice","Synchronize") is not true)throw new Exception("Focused native-stage core device synchronization failed");
        Call("Backend.GuestOwned","Register",assets);Call("Backend.GuestCompiler","CompilePending",assets);
        var projection=MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.1,100);projection.M33=projection.M34-projection.M33;projection.M43=projection.M44-projection.M43;
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=Vector3.Forward;light.SunColorRaw=Vector3.One;light.SunDiffuseFactor=1;light.SunDiscIntensity=0;
        if(geometryDepth){int count=GeometryDepthGenerationRegression(core,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core focused native depth owner checks");return 0;}
        if(oldGeometryHost){int count=OldGeometryHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host geometry checks");return 0;}
        if(geometryOnly){int count=GeometryGenerationRegression(core,output,bin,backend,projection,light,geometryFamily);Console.WriteLine("PASS "+count+" actual-core focused native geometry owner checks");return 0;}
        if(oldOitHost){int count=OldOitHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host OIT checks");return 0;}
        if(oitOnly){int count=OitGenerationRegression(core,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core OIT owner checks");return 0;}
        if(dependenciesOnly){int count=SharedDependencyRegression(core,output,bin,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core shared dependency checks");return 0;}
        if(oldShadowHost){int count=OldShadowHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host shadow checks");return 0;}
        if(shadowsOnly&&!environmentBatch){int count=ShadowGenerationRegression(core,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core shadow owner checks");return 0;}
        if(oldParticleHost){int count=OldParticleHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host particle checks");return 0;}
        if(particlesOnly){int count=ParticleGenerationRegression(core,output,bin,backend,projection,light,library);Console.WriteLine("PASS "+count+" actual-core particle owner checks");return 0;}
        if(oldProbeProcessingHost){int count=OldProbeProcessingHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host probe processing checks");return 0;}
        if(probeProcessingOnly&&!environmentBatch){int count=ProbeProcessingGenerationRegression(core,output,bin,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core probe processing checks");return 0;}
        if(decalsOnly){int count=DecalGenerationRegression(core,assets,output,backend,projection,light,library);Console.WriteLine("PASS "+count+" actual-core decal owner checks");return 0;}
        if(oldEnvironmentHost){int count=OldEnvironmentHostRegression(core,output,backend);Console.WriteLine("PASS "+count+" actual older-host environment checks");return 0;}
        if(environmentBatch){int count=FoliageGenerationRegression(core,assets,output,backend,projection,light,library)+AtmosphereGenerationRegression(core,assets,output,backend,projection,light)+ProbeAtmosphereGenerationRegression(core,assets,output,bin,backend,projection,light);if(probeProcessingOnly)count+=ProbeProcessingGenerationRegression(core,output,bin,backend,projection,light);if(shadowsOnly)count+=ShadowGenerationRegression(core,output,backend,projection,light);Console.WriteLine("PASS "+count+(shadowsOnly?" actual-core environment/shadow owner batch checks":probeProcessingOnly?" actual-core environment/probe owner batch checks":" actual-core environment owner batch checks"));return 0;}
        if(atmosphereOnly){int count=AtmosphereGenerationRegression(core,assets,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core atmosphere generation checks");return 0;}
        if(oldClassicHost){int count=OldClassicNullHostRegression(core,assets,output,backend);Console.WriteLine("PASS "+count+" actual older-host classic resource checks");return 0;}
        if(classicNulls){int count=ClassicNullRegression(core,assets,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core classic null checks");return 0;}
        if(oldBindingHost){int refused=OldBindingHostRegression(core,output,backend);Console.WriteLine("PASS "+refused+" actual older-host resource contracts");return 0;}
        if(lateHistory){int count=LateColorHistoryRegression(core,assets,output,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core late color-history checks");return 0;}
        if(catalogLifecycle){int count=CatalogLifecycleRegression(core,backend,projection,light);Console.WriteLine("PASS "+count+" actual-core catalog lifecycle checks");return 0;}
        int checks=0;
        if(!bindingsOnly){
        checks+=StageBindingFacadeContracts(core);
        checks+=EarlyOwnedRegression(core,assets,output,backend,projection,light);
        checks+=OwnedStageRegression(core,assets,output,backend,projection,light);
        checks+=PostprocessGenerationRegression(core,assets,output,backend,library,projection,light);
        checks+=LightingGenerationRegression(core,assets,output,backend,library,projection,light);
        }
        checks+=NativeBindingRegression(core,assets,output,backend,projection,light);
        var stats=backend.GetStats();if(stats.ValidationEnabled==0||stats.ValidationErrors!=0)throw new Exception("Focused native-stage core/synchronization validation failed");
        Console.WriteLine("PASS "+checks+" focused actual-core owned/postprocess/lighting checks; validation enabled="+stats.ValidationEnabled+", errors="+stats.ValidationErrors);return 0;
    }
}
