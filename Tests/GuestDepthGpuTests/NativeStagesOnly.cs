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
    static int NativeStagesOnly(string bin,bool bindingsOnly=false,bool oldBindingHost=false,bool catalogLifecycle=false)
    {
        string root=Directory.GetCurrentDirectory(),output=Path.GetFullPath(oldBindingHost?"TestResults/NativeBindingOldHostTests":"TestResults/NativeStagesGpuTests"),stage=Path.Combine(output,"Assets");
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
        if(oldBindingHost){int refused=OldBindingHostRegression(core,output,backend);Console.WriteLine("PASS "+refused+" actual older-host resource contracts");return 0;}
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
