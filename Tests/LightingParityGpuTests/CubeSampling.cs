using System.Runtime.InteropServices;
using System.Text.Json;
using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Shaders;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using VRageMath;
using VRageRender;
using Device=SharpDX.Direct3D11.Device;
using Buffer=System.Buffer;

// Independent filtering control: identical authored cube bytes, directions and
// explicit fractional LODs. No scene/probe generation or temporal differences.
static class CubeSampling
{
    const int Width=96,Height=64,Size=32,Mips=5;
    const string Source="""
        TextureCube<float4> Cube : register(t10);
        SamplerState Linear : register(s2);
        SamplerState Aniso : register(s3);
        struct Samples {float4 a:SV_Target0;float4 b:SV_Target1;float4 c:SV_Target2;float4 d:SV_Target3;float4 e:SV_Target4;float4 f:SV_Target5;};
        Samples Evaluate(uint2 p){
            uint face=p.x/16;float2 uv=((float2)(p%16)+.5)/16*2-1;
            float3 dir=face==0?float3(1,-uv.y,-uv.x):face==1?float3(-1,-uv.y,uv.x):
                face==2?float3(uv.x,1,uv.y):face==3?float3(uv.x,-1,-uv.y):
                face==4?float3(uv.x,-uv.y,1):float3(-uv.x,-uv.y,-1);
            float level=(p.y/16)+.35;
            Samples s;s.a=Cube.SampleLevel(Aniso,dir,level);s.b=Cube.SampleLevel(Linear,dir,level);
            s.c=Cube.SampleGrad(Aniso,dir,float3(.25,0,0),float3(0,.005,0));
            s.d=Cube.SampleGrad(Linear,dir,float3(.25,0,0),float3(0,.005,0));
            // Cube face projection has derivative 1/(2*major). These
            // tangents produce equal projected footprints of 2^level texels.
            float major=max(abs(dir.x),max(abs(dir.y),abs(dir.z)));
            float footprint=2*major*exp2(level)/32;
            float3 dx=face==0?float3(0,0,-footprint):face==1?float3(0,0,footprint):face==5?float3(-footprint,0,0):float3(footprint,0,0);
            float3 dy=face==2?float3(0,0,footprint):face==3?float3(0,0,-footprint):float3(0,-footprint,0);
            s.e=Cube.SampleGrad(Aniso,dir,dx,dy);s.f=Cube.SampleGrad(Linear,dir,dx,dy);return s;
        }
        Samples __pixel_shader(float4 p:SV_Position){return Evaluate((uint2)p.xy);}
        RWTexture2D<float4> Result:register(u0);
        [numthreads(8,8,1)] void main(uint3 p:SV_DispatchThreadID){
            if(any(p.xy>=uint2(96,64)))return;
            Samples s=Evaluate(p.xy);Result[p.xy]=s.a;Result[p.xy+uint2(0,64)]=s.b;Result[p.xy+uint2(0,128)]=s.c;Result[p.xy+uint2(0,192)]=s.d;Result[p.xy+uint2(0,256)]=s.e;Result[p.xy+uint2(0,320)]=s.f;
        }
        """;

    public static int Run(string bin,bool raw,bool hardware,bool mipRamp,bool unitDirection,bool maxOne)
    {
        if(maxOne && !raw)throw new ArgumentException("The max-one sampler control requires --cube-sampling-raw so its s3 operation remains intact.");
        string output=Path.GetFullPath((raw?"TestResults/CubeSamplingRaw":"TestResults/CubeSampling")+(hardware?"-Hardware":"")+(mipRamp?"-MipRamp":"")+(unitDirection?"-UnitDirection":"")+(maxOne?"-MaxOne":""));Directory.CreateDirectory(output);
        string stage=Path.Combine(output,"Assets");Directory.CreateDirectory(Path.Combine(stage,"Native/win-x64"));Directory.CreateDirectory(Path.Combine(stage,"Shaders/Compiled"));
        File.Copy(Path.GetFullPath("Native/build/Release/se_vulkan_native.dll"),Path.Combine(stage,"Native/win-x64/se_vulkan_native.dll"),true);
        foreach(string dir in new[]{"Assets/Shaders/Compiled","Native/build/shaders"})foreach(string artifact in Directory.GetFiles(dir,"*.spv"))File.Copy(artifact,Path.Combine(stage,"Shaders/Compiled",Path.GetFileName(artifact)),true);
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR",stage);
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VK_LAYER_PATH")))Environment.SetEnvironmentVariable("VK_LAYER_PATH",Path.GetFullPath("Native/build/validation/Bin"));
        Environment.SetEnvironmentVariable("VK_LAYER_ENABLES","VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT");
        Environment.SetEnvironmentVariable("VK_VALIDATION_VALIDATE_SYNC","1");
        byte[][][] faces=Enumerable.Range(0,6).Select(face=>Enumerable.Range(0,Mips).Select(mip=>{
            int n=Size>>mip;byte[] bytes=new byte[n*n*4];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                int at=(y*n+x)*4;bytes[at]=(byte)((x*37+y*13+face*29+mip*53)&255);
                bytes[at+1]=(byte)((x*17+y*43+face*31+mip*47)&255);bytes[at+2]=(byte)((x*23+y*7+face*59+mip*19)&255);bytes[at+3]=255;
                if(mipRamp){bytes[at]=(byte)(mip*255/(Mips-1));bytes[at+1]=(byte)(255-bytes[at]);bytes[at+2]=32;}
            }return bytes;
        }).ToArray()).ToArray();
        string source=unitDirection?Source.Replace("float level=","dir=normalize(dir);float level="):Source;
        float[] dx=Dx11(source,faces,hardware);
        string root=Path.GetFullPath(Path.Combine(bin,"../Content/Shaders"));
        var library=new VulkanShaderLibrary(Path.GetFullPath("Assets/Shaders/Vanilla"),root,Path.Combine(bin,"VRage.Render11.dll"));
        using var backend=new NativeBackend(null);backend.Create(Width,Height,0,true,Path.Combine(output,"NativeCache"));
        backend.InitializeGBuffer(library.Load(VanillaGBufferShaders.VertexId),library.Load(VanillaGBufferShaders.PixelId));backend.ConfigureWorld(library,root);
        backend.UploadWorldCubeMips(123,Size,Size,0,faces,false);backend.SetWorldSkybox(123);backend.SetTextureAnisotropy(16);
        using(var compiler=new VulkanShaderCompiler(Path.GetFullPath("Assets/Tools/win-x64"),Path.Combine(output,"ShaderCache"))){
            string file=Path.Combine(output,"CubeSampling.hlsl");File.WriteAllText(file,source);
            var compiled=compiler.Compile(new VulkanShaderRequest{SourcePath=file,ContentDirectory=output,Profile="ps_5_0",EntryPoint="__pixel_shader"});
            byte[] module=raw?SpirvRuntimeModule.Prepare(File.ReadAllBytes(compiled.SpirvPath)):SpirvRuntimeModule.FromArtifact(VulkanShaderArtifact.FromCompilation(compiled,"cube-sampling-control"));
            File.WriteAllBytes(Path.Combine(output,"runtime.spv"),module);backend.ConfigureLightingTerms(module);
            string twoD=Path.Combine(output,"TwoDLod.hlsl");File.WriteAllText(twoD,"Texture2D<float4> Tex:register(t10); SamplerState Aniso:register(s3); float4 __pixel_shader(float4 p:SV_Position):SV_Target0{return Tex.SampleLevel(Aniso,float2(.2,.3),1.25);}");
            var twoDCompiled=compiler.Compile(new VulkanShaderRequest{SourcePath=twoD,ContentDirectory=output,Profile="ps_5_0",EntryPoint="__pixel_shader"});
            if(!SpirvRuntimeModule.Prepare(File.ReadAllBytes(twoDCompiled.SpirvPath)).SequenceEqual(SpirvRuntimeModule.FromArtifact(VulkanShaderArtifact.FromCompilation(twoDCompiled,"2d-lod-control"))))throw new Exception("Cube-LOD adaptation changed a 2D texture operation");
        }
        backend.SetLightingDiagnostics(true);
        MatrixD projection=(MatrixD)Matrix.CreatePerspectiveFovRhInfiniteComplementary((float)Math.PI/2,Width/(float)Height,.1f);
        var renderLight=MyEnvironmentLightData.Default;renderLight.SkyboxBrightness=1;renderLight.SunDiscIntensity=0;
        void RenderFrame(){backend.BeginPacketCollection();backend.QueueWorld(WorldConstants.Frame(backend.WorldFrameLayout,MatrixD.Identity,projection,Width,Height,renderLight),NativeBackend.MatrixValues(projection),NativeBackend.MatrixValues(MatrixD.Invert(projection)));
            if(!backend.BeginFrame(projection,VRageMath.Vector3.Forward,VRageMath.Vector3.One,VRageMath.Vector3.Zero,Color.Black))throw new Exception("Cube control frame refused");backend.RequestReadback();backend.EndFrame();}
        RenderFrame();var stockSamplerResult=backend.ReadbackLightingStage(4,Width,Height);
        if(maxOne){var hdr=backend.ReadbackLightingStage(3,Width,Height);if(!hdr.Where((v,i)=>i%4<3).Any(v=>v>0))throw new Exception("Sampler isolation control has no main HDR radiance");
            backend.SetLightingDiagnosticSampler(1);RenderFrame();if(!hdr.Select(BitConverter.SingleToInt32Bits).SequenceEqual(backend.ReadbackLightingStage(3,Width,Height).Select(BitConverter.SingleToInt32Bits)))throw new Exception("Diagnostic sampler changed main rendering");}
        float[] aniso=backend.ReadbackLightingStage(4,Width,Height),linear=backend.ReadbackLightingStage(5,Width,Height),gradAniso=backend.ReadbackLightingStage(6,Width,Height),gradLinear=backend.ReadbackLightingStage(7,Width,Height),isoGradAniso=backend.ReadbackLightingStage(8,Width,Height),isoGradLinear=backend.ReadbackLightingStage(9,Width,Height);var stats=backend.GetStats();
        object Compare(float[] a,float[] b){double max=0,sum=0;int count=0,over=0;for(int i=0;i<a.Length;i++){if(i%4==3)continue;double d=Math.Abs(a[i]-b[i]);if(!double.IsFinite(d))throw new Exception("Nonfinite cube sample");max=Math.Max(max,d);sum+=d;count++;if(d>1e-3)over++;}return new{MeanAbsolute=sum/count,MaxAbsolute=max,ChannelsOver001=over};}
        var dxAniso=dx.Take(aniso.Length).ToArray();var dxLinear=dx.Skip(aniso.Length).Take(aniso.Length).ToArray();
        var result=new{Cases=Width*Height,RawUnadapted=raw,Unchanged2DExplicitLod=true,Dx11Driver=hardware?"Hardware":"Warp",MipRamp=mipRamp,UnitDirection=unitDirection,DiagnosticMaxOneSampler=maxOne,Scope="Authored RGBA8 cube, identical explicit LOD/direction; explicit gradients separately retain anisotropy.",VulkanAnisoVsDx11Aniso=Compare(aniso,dxAniso),VulkanLinearVsDx11Linear=Compare(linear,dxLinear),VulkanLinearVsDx11Aniso=Compare(linear,dxAniso),VulkanAnisoVsLinear=Compare(aniso,linear),Dx11AnisoVsLinear=Compare(dxAniso,dxLinear),VulkanGradAnisoVsLinear=Compare(gradAniso,gradLinear),IsotropicGradAnisoVsDx11Lod=Compare(isoGradAniso,dxAniso),IsotropicGradLinearVsDx11Lod=Compare(isoGradLinear,dxAniso),Samples=new[]{(0,8),(8,8),(15,8),(40,24),(56,40),(88,56)}.Select(p=>new{X=p.Item1,Y=p.Item2,Lod=(p.Item2/16)+.35f,VulkanAniso=aniso.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray(),VulkanLinear=linear.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray(),VulkanIsotropicGradAniso=isoGradAniso.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray(),VulkanIsotropicGradLinear=isoGradLinear.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray(),Dx11Aniso=dxAniso.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray(),Dx11Linear=dxLinear.Skip((p.Item2*Width+p.Item1)*4).Take(3).ToArray()}).ToArray(),stats.ValidationEnabled,stats.ValidationErrors};
        File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        foreach(var (name,data) in new[]{("vulkan-aniso",aniso),("vulkan-linear",linear),("dx11",dx),("vulkan-isotropic-grad-aniso",isoGradAniso),("vulkan-isotropic-grad-linear",isoGradLinear)}){byte[] bytes=new byte[data.Length*4];Buffer.BlockCopy(data,0,bytes,0,bytes.Length);File.WriteAllBytes(Path.Combine(output,name+".f32"),bytes);}
        Console.WriteLine(JsonSerializer.Serialize(result));
        if(stats.ValidationEnabled==0 || stats.ValidationErrors!=0)throw new Exception("Cube sampling validation failed");
        if(!raw && !aniso.SequenceEqual(linear))throw new Exception("DX11 explicit-LOD cube semantics still differ from isotropic sampling");
        if(!maxOne && gradAniso.SequenceEqual(gradLinear))throw new Exception("Explicit-gradient anisotropy was lost");
        if(maxOne){backend.SetLightingDiagnostics(false);backend.SetLightingDiagnostics(true);RenderFrame();if(!stockSamplerResult.SequenceEqual(backend.ReadbackLightingStage(4,Width,Height)))throw new Exception("Disabling diagnostics retained the sampler override");if(backend.GetStats().ValidationErrors!=0)throw new Exception("Diagnostic sampler restoration caused validation errors");Console.WriteLine("PASS diagnostic max-one sampler preserves main HDR and disable restores the stock sampler");}
        return 0;
    }

    static float[] Dx11(string source,byte[][][] faces,bool hardware)
    {
        using var device=new Device(hardware?SharpDX.Direct3D.DriverType.Hardware:SharpDX.Direct3D.DriverType.Warp,DeviceCreationFlags.None);
        using var dxgi=device.QueryInterface<SharpDX.DXGI.Device>();using var adapter=dxgi.Adapter;Console.WriteLine("DX11 control adapter: "+adapter.Description.Description);
        using var code=ShaderBytecode.Compile(source,"main","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None);
        using var shader=new ComputeShader(device,code);var context=device.ImmediateContext;
        var pins=new List<GCHandle>();var boxes=new List<DataBox>();
        try{
            for(int face=0;face<6;face++)for(int mip=0;mip<Mips;mip++){var pin=GCHandle.Alloc(faces[face][mip],GCHandleType.Pinned);pins.Add(pin);boxes.Add(new DataBox(pin.AddrOfPinnedObject(),(Size>>mip)*4,0));}
            using var cube=new Texture2D(device,new Texture2DDescription{Width=Size,Height=Size,MipLevels=Mips,ArraySize=6,Format=Format.R8G8B8A8_UNorm,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource,OptionFlags=ResourceOptionFlags.TextureCube},boxes.ToArray());
            using var srv=new ShaderResourceView(device,cube);
            using var linear=new SamplerState(device,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue});
            using var aniso=new SamplerState(device,new SamplerStateDescription{Filter=Filter.Anisotropic,MaximumAnisotropy=16,AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Wrap,MaximumLod=float.MaxValue});
            var desc=new Texture2DDescription{Width=Width,Height=Height*6,MipLevels=1,ArraySize=1,Format=Format.R32G32B32A32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.UnorderedAccess};
            using var result=new Texture2D(device,desc);using var uav=new UnorderedAccessView(device,result);
            context.ComputeShader.Set(shader);context.ComputeShader.SetShaderResource(10,srv);context.ComputeShader.SetSampler(2,linear);context.ComputeShader.SetSampler(3,aniso);context.ComputeShader.SetUnorderedAccessView(0,uav);context.Dispatch(Width/8,Height/8,1);context.ClearState();
            desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;using var staging=new Texture2D(device,desc);context.CopyResource(result,staging);
            var map=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);float[] output=new float[Width*Height*6*4];
            try{for(int y=0;y<Height*6;y++)Marshal.Copy(map.DataPointer+y*map.RowPitch,output,y*Width*4,Width*4);}finally{context.UnmapSubresource(staging,0);}return output;
        }finally{foreach(var pin in pins)pin.Free();}
    }
}
