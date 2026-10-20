using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using HarmonyLib;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using Device=SharpDX.Direct3D11.Device;

namespace CreativeFixtureHarness;

// Invoked only in the DX11 route, on the stock tone-mapping render thread.
// The engine device and views are borrowed; only test-owned objects are disposed.
static partial class Dx11LightingReadback
{
    const BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Read(object value,string name)
    {
        var type=value as Type??value.GetType();object target=value is Type?null:value;
        var field=type.GetField(name,Members);if(field!=null)return field.GetValue(target);
        var property=type.GetProperty(name,Members);if(property!=null)return property.GetValue(target,null);
        foreach(var contract in type.GetInterfaces())if(contract.GetProperty(name) is PropertyInfo p)return p.GetValue(value,null);
        throw new MissingMemberException(type.FullName,name);
    }
    static ShaderResourceView View(object value)=>(ShaderResourceView)Read(value,"Srv");
    static void CaptureProbes(Device device,DeviceContext context,string folder)
    {
        var manager=Read(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"EnvironmentProbe");
        var root=new XElement("LightingProbes",new XAttribute("Schema",1),new XAttribute("Backend","Dx11"),new XAttribute("Layout","FaceMajorMipChain"),new XAttribute("WorkingStage",Read(manager,"m_state")),new XAttribute("WorkingBlendDone",Read(manager,"m_blendDone")),new XAttribute("LastAmbient",Read(manager.GetType(),"LastAmbient")));
        string[] names={"close-original","far-original","close-final","far-final"},fields={"CloseCubemapOriginal","FarCubemapOriginal","CloseCubemapFinal","FarCubemapFinal"};
        var environment=Read(AccessTools.TypeByName("VRageRender.MyRender11"),"Environment");
        root.SetAttributeValue("ProbeTimeOut",Read(Read(Read(environment,"Data"),"EnvironmentProbe"),"TimeOut"));
        root.SetAttributeValue("LastFrameDelta",AccessTools.TypeByName("VRageRender.MyCommon").GetMethod("GetLastFrameDelta",Members).Invoke(null,null));
        for(int cube=0;cube<names.Length;cube++)using(var source=View(Read(manager,fields[cube])).ResourceAs<Texture2D>()){
            var desc=source.Description;
            if(desc.Format!=Format.R11G11B10_Float || desc.Width!=desc.Height || desc.ArraySize!=6 || desc.SampleDescription.Count!=1)throw new InvalidOperationException("Unexpected stock probe layout: "+fields[cube]);
            int resolution=desc.Width,mips=desc.MipLevels;desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;desc.OptionFlags=ResourceOptionFlags.None;
            using(var staging=new Texture2D(device,desc))using(var file=File.Create(Path.Combine(folder,"probe-"+names[cube]+".r11g11b10"))){
                context.CopyResource(source,staging);
                for(int face=0;face<6;face++)for(int mip=0;mip<mips;mip++){
                    int size=Math.Max(1,resolution>>mip),subresource=mip+face*mips;var map=context.MapSubresource(staging,subresource,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                    try{var row=new byte[size*4];for(int y=0;y<size;y++){Marshal.Copy(map.DataPointer+y*map.RowPitch,row,0,row.Length);file.Write(row,0,row.Length);}}finally{context.UnmapSubresource(staging,subresource);}
                }
            }
            root.Add(new XElement("Cube",new XAttribute("Name",names[cube]),new XAttribute("Resolution",resolution),new XAttribute("MipLevels",mips),new XAttribute("Faces",6),new XAttribute("Format",desc.Format)));
        }
        root.Save(Path.Combine(folder,"probes.xml"));
    }
    public static void CaptureFxaa(string reports,object rc,object image,bool after){
        var device=(Device)Read(AccessTools.TypeByName("VRageRender.MyRender11"),"DeviceInstance");var context=(DeviceContext)Read(rc,"DeviceContext");
        if(context.TypeInfo!=DeviceContextType.Immediate)throw new InvalidOperationException("FXAA capture requires its owning immediate context.");
        using(var source=View(image).ResourceAs<Texture2D>()){
            var desc=source.Description;if(desc.Width!=640 || desc.Height!=480 || desc.ArraySize!=1 || desc.MipLevels!=1 || desc.SampleDescription.Count!=1 ||
                (desc.Format!=Format.R8G8B8A8_UNorm && desc.Format!=Format.R8G8B8A8_Typeless && desc.Format!=Format.R8G8B8A8_UNorm_SRgb))throw new InvalidOperationException("Unexpected stock FXAA image layout: "+desc.Format);
            desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;desc.OptionFlags=ResourceOptionFlags.None;
            using(var staging=new Texture2D(device,desc)){context.CopyResource(source,staging);var map=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                try{var bytes=new byte[640*480*4];for(int y=0;y<480;y++)Marshal.Copy(map.DataPointer+y*map.RowPitch,bytes,y*640*4,640*4);File.WriteAllBytes(Path.Combine(reports,"lighting-buffers",after?"post-fxaa.rgba8":"pre-fxaa.rgba8"),bytes);}finally{context.UnmapSubresource(staging,0);}
            }
        }
    }
    // Capture the actual arguments before the stock directional lighting draw;
    // these pooled inputs may have been recycled by the later tone-map hook.
    static Texture2D[] inputCopies;
    public static void CaptureInputs(object rc,object sun,object ao)
    {
        if(inputCopies!=null)throw new InvalidOperationException("Lighting input copy is already queued.");
        var device=(Device)Read(AccessTools.TypeByName("VRageRender.MyRender11"),"DeviceInstance");var context=(DeviceContext)Read(rc,"DeviceContext");
        var inputs=new[]{ao,sun};string[] names={"screen-ao","sun-visibility"};
        var copies=new Texture2D[2];try{for(int i=0;i<inputs.Length;i++)using(var source=View(inputs[i]).ResourceAs<Texture2D>()) {
            var description=source.Description;
            if(description.Width!=640 || description.Height!=480 || description.SampleDescription.Count!=1 || description.MipLevels!=1 || description.ArraySize!=1 || description.Format!=Format.R8_UNorm)throw new InvalidOperationException("Directional lighting input must be the actual 640x480 single-level R8 texture: "+names[i]);
            description.BindFlags=BindFlags.None;description.Usage=ResourceUsage.Staging;description.CpuAccessFlags=CpuAccessFlags.Read;description.OptionFlags=ResourceOptionFlags.None;
            copies[i]=new Texture2D(device,description);
            // MyGBufferResolver records on a deferred context. Record the copy
            // there too; mapping on the immediate context must wait until the
            // engine has executed that command list, at the tone-map hook.
            context.CopyResource(source,copies[i]);
        }inputCopies=copies;}catch{foreach(var copy in copies)copy?.Dispose();throw;}
    }
    static void CompleteInputs(DeviceContext context,string folder)
    {
        var copies=inputCopies;inputCopies=null;
        if(copies==null)throw new InvalidOperationException("Directional lighting inputs were not queued on their owning context.");
        string[] names={"screen-ao","sun-visibility"};
        try{for(int i=0;i<copies.Length;i++){
            var map=context.MapSubresource(copies[i],0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
            try{var bytes=new byte[640*480];for(int y=0;y<480;y++)Marshal.Copy(map.DataPointer+y*map.RowPitch,bytes,y*640,640);File.WriteAllBytes(Path.Combine(folder,names[i]+".r8"),bytes);}finally{context.UnmapSubresource(copies[i],0);}
        }}finally{foreach(var copy in copies)copy?.Dispose();}
    }
    public static void Capture(string reports,object hdr,string sourceShader)
    {
        var render=AccessTools.TypeByName("VRageRender.MyRender11");
        var device=(Device)Read(render,"DeviceInstance");var context=device.ImmediateContext;
        var gbuffer=Read(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main");
        var sources=new[]{View(Read(gbuffer,"GBuffer0")),View(Read(gbuffer,"GBuffer1")),View(Read(gbuffer,"GBuffer2")),View(Read(Read(gbuffer,"DepthStencil"),"SrvDepth")),View(hdr)};
        string hdrFormat;
        using(var source=sources[4].ResourceAs<Texture2D>())hdrFormat=source.Description.Format.ToString();
        string folder=Path.Combine(reports,"lighting-buffers");Directory.CreateDirectory(folder);
        CaptureProbes(device,context,folder);
        CompleteInputs(context,folder);
        const int width=640,height=480;
        using(var source=sources[0].ResourceAs<Texture2D>())if(source.Description.Width!=width || source.Description.Height!=height || source.Description.SampleDescription.Count!=1)throw new InvalidOperationException("Lighting readback requires actual non-MSAA 640x480 buffers.");
        var textures=new Texture2D[5];var views=new UnorderedAccessView[5];
        using(var code=ShaderBytecode.Compile(sourceShader,"main","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None))
        using(var shader=new ComputeShader(device,code)) {
            var previousShader=context.ComputeShader.Get();var previousInputs=context.ComputeShader.GetShaderResources(0,5);var previousOutputs=context.ComputeShader.GetUnorderedAccessViews(0,5);
            try {
                for(int i=0;i<5;i++) {
                    textures[i]=new Texture2D(device,new Texture2DDescription{Width=width,Height=height,ArraySize=1,MipLevels=1,Format=Format.R32G32B32A32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.UnorderedAccess});
                    views[i]=new UnorderedAccessView(device,textures[i]);
                }
                context.ComputeShader.SetUnorderedAccessViews(0,new UnorderedAccessView[5]);context.ComputeShader.Set(shader);context.ComputeShader.SetShaderResources(0,sources);context.ComputeShader.SetUnorderedAccessViews(0,views);context.Dispatch((width+7)/8,(height+7)/8,1);
            } finally {
                // Restore the exact stage state so MyRenderContext's cache stays
                // consistent. ClearState would invalidate its cached bindings.
                context.ComputeShader.SetShaderResources(0,new ShaderResourceView[5]);context.ComputeShader.SetUnorderedAccessViews(0,previousOutputs);context.ComputeShader.SetShaderResources(0,previousInputs);context.ComputeShader.Set(previousShader);
                previousShader?.Dispose();foreach(var v in previousInputs)v?.Dispose();foreach(var v in previousOutputs)v?.Dispose();
            }
            try {
                string[] names={"color-lod","normal-ao","metal-gloss-emissive","depth","hdr"};
                for(int i=0;i<5;i++) {
                    var description=textures[i].Description;description.BindFlags=BindFlags.None;description.Usage=ResourceUsage.Staging;description.CpuAccessFlags=CpuAccessFlags.Read;
                    using(var stage=new Texture2D(device,description)) {
                        context.CopyResource(textures[i],stage);var map=context.MapSubresource(stage,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                        try{var bytes=new byte[width*height*16];for(int y=0;y<height;y++)Marshal.Copy(map.DataPointer+y*map.RowPitch,bytes,y*width*16,width*16);File.WriteAllBytes(Path.Combine(folder,names[i]+".rgba32f"),bytes);}finally{context.UnmapSubresource(stage,0);}
                    }
                }
            } finally {foreach(var view in views)view?.Dispose();foreach(var texture in textures)texture?.Dispose();}
        }
        var common=AccessTools.TypeByName("VRageRender.MyCommon");var frame=Read(common,"FrameConstantsData");int size=Marshal.SizeOf(frame);var memory=Marshal.AllocHGlobal(size);
        try{Marshal.StructureToPtr(frame,memory,false);var bytes=new byte[size];Marshal.Copy(memory,bytes,0,size);File.WriteAllBytes(Path.Combine(folder,"frame.bin"),bytes);}finally{Marshal.FreeHGlobal(memory);}
        new XDocument(new XElement("LightingBuffers",new XAttribute("Backend","Dx11"),new XAttribute("Width",width),new XAttribute("Height",height),new XAttribute("Thread",Thread.CurrentThread.ManagedThreadId),
            new XAttribute("Stage","BeforeTonemap"),new XAttribute("HdrFormat",hdrFormat),new XAttribute("LightingInputs",1),new XAttribute("CapturedUtc",DateTime.UtcNow.ToString("o")),
            new XElement("Scope","Current-frame decoded GBuffer/depth and the actual input of stock MyToneMapping.Run; producer medium disabled. Frame matrices retain stock CPU storage."))).Save(Path.Combine(folder,"complete.xml"));
    }
}
