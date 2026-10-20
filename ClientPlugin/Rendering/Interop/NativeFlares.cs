using System;
using System.Runtime.InteropServices;
using VRageMath;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private FlareFunctions flareApi;private FlareFunctions FlareApi=>flareApi??(flareApi=new FlareFunctions(GetSymbol));
    public void QueueFlares(MatrixD matrix,NativeFlareQuery[] queries)=>Check(FlareApi.Queue(context,MatrixValues(matrix),queries,(uint)(queries?.Length??0)),context);
    public void QueueFlareRaster(NativeFlareRasterQuery[] queries)=>Check(FlareApi.Raster(context,queries,(uint)(queries?.Length??0)),context);
    public NativeFlareResult[] GetFlareResults(){var results=new NativeFlareResult[4096];Check(FlareApi.Get(context,results,(uint)results.Length,out uint count),context);Array.Resize(ref results,(int)count);return results;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeFlareQuery{public ulong Id;public float X,Y,Z,Radius;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeFlareRasterQuery{public ulong Id;public float X,Y,Z,HalfWidth,HalfHeight,Area;}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeFlareResult{public ulong Id;public float Occlusion;public uint Reserved;}
internal sealed class FlareFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr c,float[] matrix,[In]NativeFlareQuery[] q,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int GetFn(IntPtr c,[Out]NativeFlareResult[] r,uint capacity,out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int RasterFn(IntPtr c,[In]NativeFlareRasterQuery[] q,uint count);
    internal readonly QueueFn Queue;internal readonly GetFn Get;internal readonly RasterFn Raster;
    internal FlareFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Queue=Get<QueueFn>("sevk_queue_flares");this.Get=Get<GetFn>("sevk_get_flare_results");Raster=Get<RasterFn>("sevk_queue_flare_raster");}
}
