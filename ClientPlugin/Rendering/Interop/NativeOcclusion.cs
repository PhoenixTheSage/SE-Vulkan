using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private OcclusionFunctions occlusionApi;private readonly NativeOcclusionResult[] occlusionScratch=new NativeOcclusionResult[8192];
    private OcclusionFunctions OcclusionApi=>occlusionApi??(occlusionApi=new OcclusionFunctions(GetSymbol));
    public float RenderFraction{get;private set;}=1;
    public void ConfigureOcclusion(VulkanShaderLibrary library){var vs=SpirvRuntimeModule.FromArtifact(library.Load("babad18b0fbc76b619cc99914bbd22bc234ae57da0c44e005acc27b3996f2d93"));var ps=SpirvRuntimeModule.FromArtifact(library.Load("1e124ff187f7d6022692a237791b715152091c89eec94f9cac364c048ea2c7fb"));Check(OcclusionApi.Init(context,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);}
    public void QueueOcclusion(ulong token,NativeOcclusionDraw[] queries)=>Check(OcclusionApi.Queue(context,token,queries,(uint)queries.Length),context);
    public void QueueOcclusion(ulong token,NativePackedOcclusionDraw[] queries,int count){if(count<0 || count>queries.Length)throw new ArgumentOutOfRangeException(nameof(count));Check(OcclusionApi.PackedQueue(context,token,queries,(uint)count),context);}
    // Borrowed until the next GetOcclusion call. Scene consumes results before
    // reusing its eight token-owned visibility snapshots.
    internal NativeOcclusionResult[] GetOcclusionBorrowed(out int count,out ulong token){Check(OcclusionApi.Get(context,occlusionScratch,(uint)occlusionScratch.Length,out uint size,out token),context);count=checked((int)size);return occlusionScratch;}
    public NativeOcclusionResult[] GetOcclusion(out ulong token){Check(OcclusionApi.Get(context,occlusionScratch,(uint)occlusionScratch.Length,out uint count,out token),context);var values=new NativeOcclusionResult[count];Array.Copy(occlusionScratch,values,(int)count);return values;}
    public NativeOcclusionStats GetOcclusionStats(){var s=new NativeOcclusionStats{Size=48,AbiVersion=AbiVersion};Check(OcclusionApi.Stats(context,ref s),context);return s;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeOcclusionDraw {public ulong Id;[MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]public float[] Minimum;[MarshalAs(UnmanagedType.ByValArray,SizeConst=3)]public float[] Extents;public uint Flags,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativePackedOcclusionDraw {public ulong Id;public float MinimumX,MinimumY,MinimumZ,ExtentX,ExtentY,ExtentZ;public uint Flags,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeOcclusionResult {public ulong Id;public uint Visible,Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeOcclusionStats {public uint Size,AbiVersion;public ulong Frames,Queries,Results,Occluded,Token;}
internal sealed class OcclusionFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr ctx,byte[] vs,ulong vb,byte[] ps,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr ctx,ulong token,[In]NativeOcclusionDraw[] queries,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int PackedQueueFn(IntPtr ctx,ulong token,[In]NativePackedOcclusionDraw[] queries,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int GetFn(IntPtr ctx,[Out]NativeOcclusionResult[] results,uint capacity,out uint count,out ulong token);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativeOcclusionStats stats);
    internal readonly InitFn Init;internal readonly QueueFn Queue;internal readonly PackedQueueFn PackedQueue;internal readonly GetFn Get;internal readonly StatsFn Stats;
    internal OcclusionFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Init=Get<InitFn>("sevk_init_occlusion");Queue=Get<QueueFn>("sevk_queue_occlusion");PackedQueue=Get<PackedQueueFn>("sevk_queue_occlusion");this.Get=Get<GetFn>("sevk_get_occlusion");Stats=Get<StatsFn>("sevk_get_occlusion_stats");}
}
