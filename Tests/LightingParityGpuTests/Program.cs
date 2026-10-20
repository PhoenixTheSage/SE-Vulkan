using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
using VRageRender;

class Program
{
    static int Main(string[] args)
    {
        string bin=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
        try{if(args.Length>1 && args[1]=="--captured-ambient")return CapturedAmbient.Run(bin,args.Skip(2).ToArray());if(args.Length>1 && (args[1]=="--cube-sampling" || args[1]=="--cube-sampling-raw"))return CubeSampling.Run(bin,args[1].EndsWith("-raw"),args.Contains("--hardware"),args.Contains("--mip-ramp"),args.Contains("--unit-direction"),args.Contains("--max-one-sampler"));return Run(bin,args.Contains("--hardware-reference"));}catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string bin,bool hardware)
    {
        string output=Path.GetFullPath(hardware?"TestResults/LightingParityGpuTests-Hardware":"TestResults/LightingParityGpuTests"),stock=Path.GetFullPath(Path.Combine(bin,"../Content/Shaders"));Directory.CreateDirectory(output);
        string stage=Path.Combine(output,"Assets");Directory.CreateDirectory(Path.Combine(stage,"Native/win-x64"));Directory.CreateDirectory(Path.Combine(stage,"Shaders/Compiled"));
        File.Copy(Path.GetFullPath("Native/build/Release/se_vulkan_native.dll"),Path.Combine(stage,"Native/win-x64/se_vulkan_native.dll"),true);
        foreach(string artifact in Directory.GetFiles("Assets/Shaders/Compiled"))File.Copy(artifact,Path.Combine(stage,"Shaders/Compiled",Path.GetFileName(artifact)),true);
        foreach(string artifact in Directory.GetFiles("Native/build/shaders","*.spv"))File.Copy(artifact,Path.Combine(stage,"Shaders/Compiled",Path.GetFileName(artifact)),true);
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR",stage);
        string layer=Path.GetFullPath("Native/build/validation/Bin");
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_LAYER_PATH")))Environment.SetEnvironmentVariable("VK_LAYER_PATH",layer);
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VK_LAYER_ENABLES")))Environment.SetEnvironmentVariable("VK_LAYER_ENABLES","VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT");
        string source="""
        #include <Lighting/Light.hlsli>
        RWTexture2D<float4> Result : register(u0);
        [numthreads(4,4,1)] void main(uint3 p:SV_DispatchThreadID) {
            if(any(p.xy>=uint2(16,16)))return;
            uint i=p.x+p.y*16;
            SurfaceInterface s=(SurfaceInterface)0;
            s.base_color=float3(.3,.5,.7);
            s.metalness=(i%4)/3.0;
            s.gloss=.05+.9*((i/4)%4)/3.0;
            s.albedo=SurfaceAlbedo(s.base_color,s.metalness);
            s.f0=SurfaceF0(s.base_color,s.metalness);
            s.N=normalize(float3((i/64)*.2,.1,1));
            s.V=normalize(float3(((i/16)%4)*.2,0,1));
            Result[p.xy]=float4(MainDirectionalLight(s),1);
        }
        """;
        string file=Path.Combine(output,"StockDirectPbr.hlsl");File.WriteAllText(file,source);
        using var compiler=new VulkanShaderCompiler(Path.GetFullPath("Assets/Tools/win-x64"),Path.Combine(output,"Cache"));
        var compiled=compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=stock,IncludeDirectories=new(){output},Profile="cs_5_0",EntryPoint="main"});
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),stock,Path.Combine(bin,"VRage.Render11.dll"));
        var light=MyEnvironmentLightData.Default;light.SunLightDirection=-Vector3.Normalize(new Vector3(.3f,.5f,1));light.SunColorRaw=new Vector3(1.7f,1.3f,.8f);light.SunSpecularColorRaw=new Vector3(.8f,1.1f,1.5f);
        light.SunDiffuseFactor=1.2f;light.SunSpecularFactor=.9f;light.SunGlossFactor=1;
        byte[] frame=WorldConstants.Frame(library.Load(WorldShaderVariants.Lighting[1]).Layout,MatrixD.Identity,MatrixD.Identity,16,16,light);
        float[] dx=Dx11Reference.Run(source,stock,frame,hardware);
        using var backend=new NativeBackend(null);backend.Create(16,16,0,true,Path.Combine(output,"NativeCache"));
        typeof(GuestBridge).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{backend,null});
        ulong pipeline=GuestBridge.CreateExtendedPipeline(new byte[]{1,0,0,0},SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiled.SpirvPath)),"compute","main",1);
        if(pipeline==0)throw new Exception("Actual native compute pipeline was unavailable.");
        GuestBridge.CreateImage(9001,16,16,1,6);
        if(!GuestBridge.SubmitExtended(1,5,16,16,1,pipeline,new[]{9001UL},new[]{frame},Array.Empty<ulong>(),0))throw new Exception("Actual native PBR reference packet was refused.");
        GuestBridge.ReplayNow(1);byte[] bytes=new byte[16*16*16];GuestBridge.Readback(9001,bytes);
        float[] vk=new float[dx.Length];Buffer.BlockCopy(bytes,0,vk,0,bytes.Length);
        double maxAbsolute=0,maxRelative=0;int mismatches=0;
        for(int i=0;i<dx.Length;i++) {
            double delta=Math.Abs(vk[i]-dx[i]),scale=Math.Max(Math.Abs(dx[i]),1e-5);
            maxAbsolute=Math.Max(maxAbsolute,delta);maxRelative=Math.Max(maxRelative,delta/scale);
            if(!float.IsFinite(vk[i]) || !float.IsFinite(dx[i]) || delta>1e-4+2e-4*Math.Abs(dx[i]))mismatches++;
        }
        var stats=backend.GetStats();
        var result=new{Cases=256,Channels=dx.Length,Dx11Driver=hardware?"Hardware":"Warp",MaxAbsolute=maxAbsolute,MaxRelative=maxRelative,Mismatches=mismatches,stats.ValidationEnabled,stats.ValidationErrors,Scope="Stock MainDirectionalLight kernel with identical copied frame bytes; DX11 reference versus Vulkan hardware. Excludes scene/GBuffer/probes/shadows/exposure."};
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllBytes(Path.Combine(output,"vulkan.f32"),bytes);byte[] dxBytes=new byte[dx.Length*4];Buffer.BlockCopy(dx,0,dxBytes,0,dxBytes.Length);File.WriteAllBytes(Path.Combine(output,"dx11.f32"),dxBytes);
        Console.WriteLine(JsonSerializer.Serialize(result));
        if(mismatches!=0 || stats.ValidationEnabled==0 || stats.ValidationErrors!=0)throw new Exception("Cross-API stock PBR kernel parity failed.");
        Console.WriteLine("PASS 256 stock direct PBR cases across DX11/Vulkan; full scene parity remains unproven.");return 0;
    }
}
