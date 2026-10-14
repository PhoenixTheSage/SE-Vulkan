using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private EnvironmentFunctions environmentApi;
    private EnvironmentFunctions EnvironmentApi=>environmentApi??(environmentApi=new EnvironmentFunctions(GetSymbol));
    public readonly Dictionary<uint,ShaderMetadata> EnvironmentLayouts=new Dictionary<uint,ShaderMetadata>();
    public void ConfigureEnvironment(VulkanShaderLibrary library)
    {
        for(uint i=0;i<EnvironmentShaderVariants.Stages.Length;++i){var artifact=library.Load(EnvironmentShaderVariants.Stages[i]);var bytes=SpirvRuntimeModule.FromArtifact(artifact);Check(EnvironmentApi.Init(context,i,bytes,(ulong)bytes.Length),context);EnvironmentLayouts[i]=artifact.Layout;}
        var probe=library.Load("1bb6f40fef889661d4a74d17f3dc6c2cdfbc083270382103ff395321ada20f05");var module=SpirvRuntimeModule.FromArtifact(probe);Check(EnvironmentApi.Init(context,9,module,(ulong)module.Length),context);EnvironmentLayouts[9]=probe.Layout;
    }
    public void QueueEnvironment(List<NativeEnvironmentDraw> draws){foreach(var draw in draws)if(draw.Constants==null || draw.Constants.Length!=144)throw new ArgumentException("Stock environment constants must contain 144 bytes.");Check(EnvironmentApi.Queue(context,draws.ToArray(),(uint)draws.Count),context);}
    private void ClearEnvironmentPackets(){if(WorldReady)Check(EnvironmentApi.Queue(context,null,0),context);}
    public void UploadFoliage(ulong id,NativeFoliagePoint[] points)=>Check(EnvironmentApi.UploadFoliage(context,id,points,(uint)points.Length),context);
    public void QueueFoliage(List<NativeFoliageDraw> draws,byte[] materials)=>Check(EnvironmentApi.QueueFoliage(context,draws.ToArray(),(uint)draws.Count,materials,(uint)(materials?.Length??0)),context);
    private void ClearFoliagePackets(){if(WorldReady)Check(EnvironmentApi.QueueFoliage(context,null,0,null,0),context);}
    public NativeEnvironmentStats GetEnvironmentStats(){var result=new NativeEnvironmentStats{Size=(uint)Marshal.SizeOf(typeof(NativeEnvironmentStats)),AbiVersion=AbiVersion};Check(EnvironmentApi.Stats(context,ref result),context);return result;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeEnvironmentDraw
{
    public uint Size,Kind,Flags,Reserved;
    public ulong Id,Mesh,Color,Alpha;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=144)] public byte[] Constants;
    public static NativeEnvironmentDraw Create(uint kind,ulong id,byte[] constants)=>new NativeEnvironmentDraw{Size=192,Kind=kind,Id=id,Constants=constants};
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeEnvironmentStats {public uint Size,AbiVersion;public ulong Frames,AtmosphereDraws,CloudDraws,LutDispatches,Luts,FoliageDraws,FoliagePoints;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeFoliagePoint {public float X,Y,Z;public uint NormalSeedMaterial;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeFoliageDraw
{
    public uint Size,Kind,Reserved0,Reserved1;public ulong Mesh,Color,Normal;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=128)] public byte[] Constants;
}
internal sealed class EnvironmentFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int InitFn(IntPtr ctx,uint stage,byte[] bytes,ulong count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int QueueFn(IntPtr ctx,[In] NativeEnvironmentDraw[] draws,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StatsFn(IntPtr ctx,ref NativeEnvironmentStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FoliageUploadFn(IntPtr ctx,ulong id,[In] NativeFoliagePoint[] points,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int FoliageQueueFn(IntPtr ctx,[In] NativeFoliageDraw[] draws,uint count,byte[] table,uint bytes);
    internal readonly InitFn Init;internal readonly QueueFn Queue;internal readonly StatsFn Stats;
    internal readonly FoliageUploadFn UploadFoliage;internal readonly FoliageQueueFn QueueFoliage;
    internal EnvironmentFunctions(Func<string,IntPtr> symbol)
    {
        T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));
        Init=Get<InitFn>("sevk_init_environment");Queue=Get<QueueFn>("sevk_queue_environment");Stats=Get<StatsFn>("sevk_get_environment_stats");
        UploadFoliage=Get<FoliageUploadFn>("sevk_upload_foliage");QueueFoliage=Get<FoliageQueueFn>("sevk_queue_foliage");
    }
}
