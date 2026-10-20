using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using Device=SharpDX.Direct3D11.Device;
using Buffer=SharpDX.Direct3D11.Buffer;

static class Dx11Reference
{
    public static float[] Run(string source,string root,byte[] frame,bool hardware=false)
    {
        using var include=new Includes(root);
        using var code=ShaderBytecode.Compile(source,"main","cs_5_0",ShaderFlags.OptimizationLevel3|ShaderFlags.PackMatrixRowMajor,EffectFlags.None,null,include);
        using var device=new Device(hardware?SharpDX.Direct3D.DriverType.Hardware:SharpDX.Direct3D.DriverType.Warp,DeviceCreationFlags.None);
        using var dxgi=device.QueryInterface<SharpDX.DXGI.Device>();using var adapter=dxgi.Adapter;Console.WriteLine("DX11 PBR reference adapter: "+adapter.Description.Description);
        using var shader=new ComputeShader(device,code);
        using var data=new DataStream(frame.Length,true,true);data.Write(frame,0,frame.Length);data.Position=0;
        using var constants=new Buffer(device,data,new BufferDescription{SizeInBytes=frame.Length,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Default});
        var description=new Texture2DDescription{Width=16,Height=16,ArraySize=1,MipLevels=1,Format=Format.R32G32B32A32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.UnorderedAccess};
        using var texture=new Texture2D(device,description);using var uav=new UnorderedAccessView(device,texture);
        var context=device.ImmediateContext;context.ComputeShader.Set(shader);context.ComputeShader.SetConstantBuffer(0,constants);context.ComputeShader.SetUnorderedAccessView(0,uav);context.Dispatch(4,4,1);context.ClearState();
        description.BindFlags=BindFlags.None;description.Usage=ResourceUsage.Staging;description.CpuAccessFlags=CpuAccessFlags.Read;
        using var staging=new Texture2D(device,description);context.CopyResource(texture,staging);var map=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
        float[] pixels=new float[16*16*4];try{for(int y=0;y<16;y++)System.Runtime.InteropServices.Marshal.Copy(map.DataPointer+y*map.RowPitch,pixels,y*16*4,16*4);}finally{context.UnmapSubresource(staging,0);}
        return pixels;
    }
    sealed class Includes(string root):Include
    {
        public IDisposable Shadow{get;set;}
        public Stream Open(IncludeType type,string name,Stream parent){string path=Path.Combine(root,name);if(!File.Exists(path) && parent is FileStream f)path=Path.Combine(Path.GetDirectoryName(f.Name),name);return File.OpenRead(path);}
        public void Close(Stream stream)=>stream.Dispose();
        public void Dispose()=>Shadow?.Dispose();
    }
}
