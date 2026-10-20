using System.Runtime.InteropServices;
using System.Xml.Linq;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device=SharpDX.Direct3D11.Device;
using Buffer=SharpDX.Direct3D11.Buffer;

// Diagnostic replay only: stock ambient terms over actual published resources.
// Excludes local lights, transparency, atmosphere and subsequent postprocessing.
static class CapturedAmbient
{
    public static int Run(string bin,string[] folders)
    {
        bool swap=folders.Contains("--swap-probes");folders=folders.Where(f=>f!="--swap-probes").ToArray();
        bool linearCube=folders.Contains("--linear-cube");folders=folders.Where(f=>f!="--linear-cube").ToArray();
        bool terms=folders.Contains("--terms");folders=folders.Where(f=>f!="--terms").ToArray();int planes=terms?8:6;
        bool hardware=folders.Contains("--hardware");folders=folders.Where(f=>f!="--hardware").ToArray();
        if(folders.Length==0)throw new ArgumentException("Supply captured lighting-buffers folders.");
        string root=Path.GetFullPath(Path.Combine(bin,"../Content/Shaders"));
        string source="""
        #include <Lighting/Light.hlsli>
        Texture2D<float> Sun : register(t19);
        RWTexture2D<float4> Result : register(u0);
        [numthreads(8,8,1)] void main(uint3 p:SV_DispatchThreadID) {
            uint2 size=(uint2)frame_.Screen.resolution;
            if(any(p.xy>=size))return;
            SurfaceInterface s=read_gbuffer(p.xy);
            float farFactor=GetAmbientFarFactor(s.depth);
            float3 R=-reflect(s.V,s.N);R.x=-R.x;
            uint w,h,levels;SkyboxIBLCloseTex.GetDimensions(0,w,h,levels);
            float level=max((1-s.gloss)*(levels-frame_.Light.SkipIBLevels),0)+frame_.Light.SkipIBLevels;
            float2 brdf=AmbientBRDFTex.SampleLevel(DefaultSampler,float2(s.gloss,saturate(dot(s.N,s.V))),0);
            float3 spec=AmbientSpecular(s.f0,s.gloss,s.N,s.V,farFactor);
            float indirectAO=saturate(1-(1-s.ao)*frame_.Light.aoIndirectLight);
            float lightAO=saturate(1-(1-s.ao)*frame_.Light.aoDirLight);
            float shadow=1-(1-Sun[p.xy])*(1-frame_.Light.shadowFadeout);
            float3 lit=s.albedo*s.emissive*frame_.Post.BloomEmissiveness;
            lit+=AmbientDiffuse(s.albedo,s.N,farFactor)*frame_.Light.ambientDiffuseFactor*indirectAO;
            lit+=spec*frame_.Light.ambientSpecularFactor*indirectAO;
            lit+=MainDirectionalLight(s)*shadow*lightAO;
            Result[p.xy]=float4(Fog(lit,s.depth),s.ao);
            Result[p.xy+uint2(0,size.y)]=float4(spec,farFactor);
            Result[p.xy+uint2(0,size.y*2)]=float4(SampleIBL(R,level,farFactor),level);
            Result[p.xy+uint2(0,size.y*3)]=float4(brdf,s.gloss,saturate(dot(s.N,s.V)));
            Result[p.xy+uint2(0,size.y*4)]=float4(s.N,s.depth);
            Result[p.xy+uint2(0,size.y*5)]=float4(s.V,indirectAO);
        #ifdef CAPTURE_TERMS
            Result[p.xy+uint2(0,size.y*6)]=float4(AmbientDiffuse(s.albedo,s.N,farFactor)*frame_.Light.ambientDiffuseFactor*indirectAO,indirectAO);
            Result[p.xy+uint2(0,size.y*7)]=float4(MainDirectionalLight(s)*shadow*lightAO,shadow);
        #endif
        }
        """;
        if(terms)source="#define CAPTURE_TERMS\n"+source;
        using var includes=new Includes(root);
        // Engine CPU capture uses column-major storage; Vulkan frame uses row-major.
        using var device=new Device(hardware?SharpDX.Direct3D.DriverType.Hardware:SharpDX.Direct3D.DriverType.Warp,DeviceCreationFlags.None);
        using var dxgi=device.QueryInterface<SharpDX.DXGI.Device>();using var adapter=dxgi.Adapter;
        string driver=hardware?"Hardware":"Warp",adapterName=adapter.Description.Description.TrimEnd('\0');
        var context=device.ImmediateContext;
        foreach(string input in folders){
            string folder=Path.GetFullPath(input);
            var meta=XElement.Load(Path.Combine(folder,"complete.xml"));
            int width=(int)meta.Attribute("Width"),height=(int)meta.Attribute("Height");
            if((string)meta.Attribute("Stage")!="BeforeTonemap" || (string)meta.Attribute("LightingInputs")!="1" || width<=0 || height<=0 || !new[]{"Vulkan","Dx11"}.Contains((string)meta.Attribute("Backend")))throw new InvalidDataException("Expected a complete DX11/Vulkan lighting-input capture.");
            bool native=(string)meta.Attribute("Backend")=="Vulkan";
            using var code=ShaderBytecode.Compile(source,"main","cs_5_0",ShaderFlags.OptimizationLevel3|(native?ShaderFlags.PackMatrixRowMajor:ShaderFlags.PackMatrixColumnMajor),EffectFlags.None,null,includes);
            using var shader=new ComputeShader(device,code);
            byte[] frame=File.ReadAllBytes(Path.Combine(folder,"frame.bin"));
            using var data=new DataStream(frame.Length,true,true);data.Write(frame,0,frame.Length);data.Position=0;
            using var constants=new Buffer(device,data,new BufferDescription{SizeInBytes=frame.Length,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Default});
            var owned=new List<IDisposable>();
            try{
                void Bind(int slot,Texture2D texture){owned.Add(texture);var srv=new ShaderResourceView(device,texture);owned.Add(srv);context.ComputeShader.SetShaderResource(slot,srv);}
                foreach(var (slot,name) in new[]{(0,"depth"),(1,"color-lod"),(2,"normal-ao"),(3,"metal-gloss-emissive")})
                    Bind(slot,Texture(device,width,height,Format.R32G32B32A32_Float,File.ReadAllBytes(Path.Combine(folder,name+".rgba32f")),16));
                Bind(4,Texture(device,width,height,Format.R32G32_UInt,new byte[width*height*8],8));
                Bind(12,Texture(device,width,height,Format.R8_UNorm,File.ReadAllBytes(Path.Combine(folder,"screen-ao.r8")),1));
                Bind(19,Texture(device,width,height,Format.R8_UNorm,File.ReadAllBytes(Path.Combine(folder,"sun-visibility.r8")),1));
                byte[] lut=File.ReadAllBytes(Path.Combine(bin,"../Content/Textures/Miscellaneous/ambient_brdf.dds"));
                if(lut.Length!=128+256*256*4 || BitConverter.ToUInt32(lut,84)!=112 || BitConverter.ToUInt32(lut,28)>1)throw new InvalidDataException("Unexpected installed single-mip BRDF DDS layout.");
                Bind(15,Texture(device,256,256,Format.R16G16_Float,lut.Skip(128).ToArray(),4));
                var probes=XElement.Load(Path.Combine(folder,"probes.xml"));
                foreach(var (slot,name) in new[]{(11,"close-final"),(17,"far-final")}){
                    var cube=probes.Elements("Cube").Single(e=>(string)e.Attribute("Name")==name);
                    int size=(int)cube.Attribute("Resolution"),mips=(int)cube.Attribute("MipLevels");
                    string sampledName=swap?(name=="close-final"?"far-final":"close-final"):name;
                    Bind(slot,Cube(device,size,mips,File.ReadAllBytes(Path.Combine(folder,"probe-"+sampledName+".r11g11b10"))));
                }
                using var linear=new SamplerState(device,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue});
                using var aniso=new SamplerState(device,new SamplerStateDescription{Filter=Filter.Anisotropic,MaximumAnisotropy=16,AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Wrap,MaximumLod=float.MaxValue});
                context.ComputeShader.SetSampler(0,linear);context.ComputeShader.SetSampler(2,linear);context.ComputeShader.SetSampler(3,linearCube?linear:aniso);
                var desc=new Texture2DDescription{Width=width,Height=height*planes,ArraySize=1,MipLevels=1,Format=Format.R32G32B32A32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.UnorderedAccess};
                using var result=new Texture2D(device,desc);using var uav=new UnorderedAccessView(device,result);
                context.ComputeShader.Set(shader);context.ComputeShader.SetConstantBuffer(0,constants);context.ComputeShader.SetUnorderedAccessView(0,uav);context.Dispatch((width+7)/8,(height+7)/8,1);context.ClearState();
                desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;
                using var staging=new Texture2D(device,desc);context.CopyResource(result,staging);
                var map=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                byte[] output=new byte[width*height*planes*16];
                try{for(int y=0;y<height*planes;y++)Marshal.Copy(map.DataPointer+y*map.RowPitch,output,y*width*16,width*16);}finally{context.UnmapSubresource(staging,0);}
                string outputDir=Path.GetFullPath(Path.Combine("TestResults/CapturedAmbient",new DirectoryInfo(folder).Parent.Parent.Name+(swap?"-swapped":"")+(linearCube?"-linear-cube":"")+(terms?"-terms":"")+(hardware?"-hardware":"")));Directory.CreateDirectory(outputDir);
                File.WriteAllBytes(Path.Combine(outputDir,"stock-ambient.rgba32f"),output);
                File.WriteAllText(Path.Combine(outputDir,"scope.txt"),$"Input={folder}\nDriver={driver}\nAdapter={adapterName}\nSwapCloseFar={swap}\nLinearCubeSampler={linearCube}\nWidth={width}\nHeight={height}\nPlanes=FoggedNoLocal,Specular,IBLSample,BRDFGlossNV,NormalDepth,ViewIndirectAO{(terms?",DiffuseIndirectAO,DirectShadow":"")}\nStock HLSL on DX11 {driver}; each backend's captured inputs. Not a complete frame oracle.\n");
                Console.WriteLine("Captured stock ambient replay ("+driver+", "+adapterName+"): "+outputDir);
            }finally{context.ClearState();foreach(var resource in owned.AsEnumerable().Reverse())resource.Dispose();}
        }
        return 0;
    }
    static Texture2D Texture(Device device,int w,int h,Format format,byte[] bytes,int stride){
        if(bytes.Length!=w*h*stride)throw new InvalidDataException("Incomplete texture capture.");
        var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try{return new Texture2D(device,new Texture2DDescription{Width=w,Height=h,ArraySize=1,MipLevels=1,Format=format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource},new[]{new DataBox(pin.AddrOfPinnedObject(),w*stride,0)});}finally{pin.Free();}
    }
    static Texture2D Cube(Device device,int size,int mips,byte[] bytes){
        var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);var boxes=new List<DataBox>();int at=0;
        try{
            for(int face=0;face<6;face++)for(int mip=0;mip<mips;mip++){int n=Math.Max(1,size>>mip);boxes.Add(new DataBox(pin.AddrOfPinnedObject()+at,n*4,0));at+=n*n*4;}
            if(at!=bytes.Length)throw new InvalidDataException("Incomplete cube capture.");
            return new Texture2D(device,new Texture2DDescription{Width=size,Height=size,ArraySize=6,MipLevels=mips,Format=Format.R11G11B10_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource,OptionFlags=ResourceOptionFlags.TextureCube},boxes.ToArray());
        }finally{pin.Free();}
    }
    sealed class Includes(string root):Include{
        readonly Dictionary<Stream,string> paths=new();
        public IDisposable Shadow{get;set;}
        public Stream Open(IncludeType type,string name,Stream parent){
            string path=Path.Combine(root,name);if(!File.Exists(path)&&parent!=null&&paths.TryGetValue(parent,out string parentPath))path=Path.Combine(Path.GetDirectoryName(parentPath),name);
            // The installed LUT has exactly one mip. Compute has no implicit
            // derivatives; explicit LOD zero is the same lookup for this asset.
            Stream stream=Path.GetFileName(path)=="EnvAmbient.hlsli"?new MemoryStream(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("AmbientBRDFTex.Sample(DefaultSampler, float2(gloss, nv))","AmbientBRDFTex.SampleLevel(DefaultSampler, float2(gloss, nv), 0)"))):File.OpenRead(path);
            paths.Add(stream,path);return stream;
        }
        public void Close(Stream stream){paths.Remove(stream);stream.Dispose();}public void Dispose()=>Shadow?.Dispose();
    }
}
