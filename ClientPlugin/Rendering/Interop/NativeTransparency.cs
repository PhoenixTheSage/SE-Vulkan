using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using ClientPlugin.Diagnostics;
using VRageRender;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private TransparencyFunctions transparencyApi;
    private TransparencyFunctions TransparencyApi=>transparencyApi??(transparencyApi=new TransparencyFunctions(GetSymbol));
    public readonly Dictionary<uint,ShaderMetadata> GlassLayouts=new Dictionary<uint,ShaderMetadata>();
    private readonly List<(WorldPacket Geometry,byte[] Material)> glassPackets=new List<(WorldPacket,byte[])>();
    public void ConfigureTransparency(VulkanShaderLibrary library)
    {
        foreach(var spec in TransparencyShaderVariants.Glass){var v=library.Load(spec.Vertex);var p=library.Load(spec.Pixel);var vs=SpirvRuntimeModule.FromArtifact(v);var ps=SpirvRuntimeModule.FromArtifact(p);Check(TransparencyApi.Init(context,spec.Id,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);GlassLayouts[spec.Id]=v.Layout;}
        foreach(var spec in TransparencyShaderVariants.Billboards){var vs=SpirvRuntimeModule.FromArtifact(library.Load(spec.Vertex));var ps=SpirvRuntimeModule.FromArtifact(library.Load(spec.Pixel));Check(TransparencyApi.Init(context,spec.Id,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);}
        var ldr=SpirvRuntimeModule.FromArtifact(library.Load("a1b699b3ad82adec90ba1af8318067d3410d117c5c94b9b424560f40e2fdf7b4"));foreach(var spec in TransparencyShaderVariants.Billboards){if(((spec.Id-20)&8)==0)continue;var pixel=SpirvRuntimeModule.FromArtifact(library.Load(spec.Pixel));var vertex=((spec.Id-20)&1)!=0?SpirvRuntimeModule.FromArtifact(library.Load(spec.Vertex)):ldr;Check(TransparencyApi.Init(context,100+spec.Id-20,vertex,(ulong)vertex.Length,pixel,(ulong)pixel.Length),context);}
        foreach(var spec in TransparencyShaderVariants.Extra){var v=library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines);var p=library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines);var vs=SpirvRuntimeModule.FromArtifact(v);var ps=SpirvRuntimeModule.FromArtifact(p);uint pipeline=spec.Id>=90?spec.Id-90+65:spec.Id>=80?spec.Id-80+5:spec.Id>=70?spec.Id-70+60:spec.Id-64;Check(TransparencyApi.Init(context,pipeline,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);if(spec.Id<70 || spec.Id>=80 && spec.Id<90)GlassLayouts[pipeline]=v.Layout;}
        var resolve=SpirvRuntimeModule.FromArtifact(library.Load(TransparencyShaderVariants.Resolve));Check(TransparencyApi.Init(context,10,null,0,resolve,(ulong)resolve.Length),context);
    }
    public static byte[] GlassMaterial(MyTransparentMaterial material,float radius,MyRenderSettings? settings=null)
    {
        var bytes=new byte[96];void V(int offset,VRageMath.Vector4 v){Buffer.BlockCopy(new[]{v.X,v.Y,v.Z,v.W},0,bytes,offset,16);}
        var config=settings??MyRenderSettings.Default;V(0,material.Color*config.TransparentColorMultiplier);V(16,material.ColorAdd);V(32,material.ShadowMultiplier);V(48,material.LightMultiplier);
        Buffer.BlockCopy(new[]{material.Reflectivity*config.TransparentReflectivityMultiplier,material.Fresnel*config.TransparentFresnelMultiplier,material.ReflectionShadow,material.Gloss*config.TransparentGlossMultiplier,material.GlossTextureAdd,material.SpecularColorFactor,radius,material.SoftParticleDistanceScale},0,bytes,64,32);return bytes;
    }
    public void AddGlassPacket(uint type,ulong mesh,ulong color,ulong normal,byte[] objects,byte[] material,byte[] instances=null,uint count=1)
    {
        if(!GlassLayouts.ContainsKey(type)||glassPackets.Count>=8192||material==null||material.Length!=96)throw new ArgumentException("Invalid glass pipeline/material batch.");
        glassPackets.Add((new WorldPacket{Pipeline=type,Mesh=mesh,Color=color,Normal=normal,Constants=objects,Instances=instances,Count=count},(byte[])material.Clone()));
    }
    public void SetBillboardProjections(float[] values){if(values==null || values.Length!=512)throw new ArgumentException("Expected 32 copied billboard matrices.");Check(TransparencyApi.Projections(context,values),context);}
    public readonly List<NativeBillboardDraw> BillboardPackets=new List<NativeBillboardDraw>();
    private void QueueTransparency()
    {
        var pins=new List<GCHandle>();IntPtr Pin(byte[] data){var p=GCHandle.Alloc(data,GCHandleType.Pinned);pins.Add(p);return p.AddrOfPinnedObject();}
        try{
            NativeTransparentDraw[] draws;
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.TransparencyPack)){
                draws=new NativeTransparentDraw[glassPackets.Count];for(int i=0;i<draws.Length;++i){var p=glassPackets[i];if(p.Geometry.Constants==null || p.Geometry.Instances!=null && p.Geometry.Instances.LongLength!=p.Geometry.Count*64L)throw new ArgumentException("Invalid copied glass geometry.");draws[i]=new NativeTransparentDraw{Geometry=new NativeWorldDraw{Size=(uint)Marshal.SizeOf(typeof(NativeTransparentDraw)),Pipeline=p.Geometry.Pipeline,Count=p.Geometry.Count,Mesh=p.Geometry.Mesh,Color=p.Geometry.Color,Normal=p.Geometry.Normal,Constants=Pin(p.Geometry.Constants),ConstantBytes=(uint)p.Geometry.Constants.Length,Instances=p.Geometry.Instances==null?IntPtr.Zero:Pin(p.Geometry.Instances)},Material=p.Material};}
            }
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeTransparencyQueue))Check(TransparencyApi.Queue(context,draws,(uint)draws.Length),context);
            using(RendererCpuProfiler.Measure(RendererCpuProfiler.Phase.NativeBillboardQueue))Check(TransparencyApi.Billboards(context,BillboardPackets.ToArray(),(uint)BillboardPackets.Count),context);
        }finally{foreach(var p in pins)p.Free();}
    }
    private void ClearTransparencyPackets(){glassPackets.Clear();BillboardPackets.Clear();if(WorldReady){Check(TransparencyApi.Queue(context,null,0),context);Check(TransparencyApi.Billboards(context,null,0),context);}}
    public NativeTransparencyStats GetTransparencyStats(){var s=new NativeTransparencyStats{Size=(uint)Marshal.SizeOf(typeof(NativeTransparencyStats)),AbiVersion=AbiVersion};Check(TransparencyApi.Stats(context,ref s),context);return s;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)] internal struct NativeTransparentDraw{public NativeWorldDraw Geometry;[MarshalAs(UnmanagedType.ByValArray,SizeConst=96)]public byte[] Material;}
[StructLayout(LayoutKind.Sequential,Pack=8)] public struct NativeTransparencyStats{public uint Size,AbiVersion;public ulong Frames,GlassDraws,Instances,Resolves,BillboardDraws,DecalDraws,GlassLayerDraws;}
[StructLayout(LayoutKind.Sequential,Pack=8)] public struct NativeBillboardDraw{public ulong Texture;public uint Flags,Bucket;[MarshalAs(UnmanagedType.ByValArray,SizeConst=20)] public float[] Vertices;[MarshalAs(UnmanagedType.ByValArray,SizeConst=48)] public byte[] Data;}
internal sealed class TransparencyFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int ProjectionsFn(IntPtr ctx,float[] values);
    internal readonly ProjectionsFn Projections;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr c,uint id,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr c,[In]NativeTransparentDraw[] draws,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr c,ref NativeTransparencyStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int BillboardFn(IntPtr c,[In]NativeBillboardDraw[] draws,uint count);
    internal readonly BillboardFn Billboards;internal readonly InitFn Init;internal readonly QueueFn Queue;internal readonly StatsFn Stats;
    internal TransparencyFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Billboards=Get<BillboardFn>("sevk_queue_billboards");Projections=Get<ProjectionsFn>("sevk_set_billboard_projections");Init=Get<InitFn>("sevk_init_transparency");Queue=Get<QueueFn>("sevk_queue_transparency");Stats=Get<StatsFn>("sevk_get_transparency_stats");}
}
