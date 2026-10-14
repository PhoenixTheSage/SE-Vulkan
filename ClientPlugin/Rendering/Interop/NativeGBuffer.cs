using System;
using System.IO;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    private GBufferFunctions gbuffer;
    private GBufferFunctions StockApi => gbuffer ?? (gbuffer = new GBufferFunctions(GetSymbol));
    public void InitializeGBuffer(VulkanShaderArtifact vertex, VulkanShaderArtifact pixel)
    {
        if (vertex == null || pixel == null) throw new ArgumentNullException("Stock shader artifacts are required.");
        byte[] vs = SpirvRuntimeModule.FromArtifact(vertex), ps = SpirvRuntimeModule.FromArtifact(pixel);
        Check(StockApi.Init(context, vs, (ulong)vs.Length, ps, (ulong)ps.Length), context);
    }
    public void UploadGBufferMesh(ulong id, NativeGBufferVertex[] vertices, uint[] indices)
    {
        if (vertices == null || indices == null) throw new ArgumentNullException("Stock mesh data is required.");
        Check(StockApi.Upload(context, id, vertices, (uint)vertices.Length, indices, (uint)indices.Length), context);
    }
    public void RenderGBuffer(int width, int height, byte[] frame, float[] projection, NativeGBufferDraw[] draws)
    {
        if (width <= 0 || height <= 0 || frame == null || frame.Length != 1280 || projection == null || projection.Length != 16 || draws == null)
            throw new ArgumentException("The stock pass needs a 1280-byte reflected frame and a projection matrix.");
        foreach (var draw in draws) if (draw.ObjectConstants == null || draw.ObjectConstants.Length != 128) throw new ArgumentException("Stock object constants must contain 128 bytes.");
        Check(StockApi.Render(context, (uint)width, (uint)height, frame, (uint)frame.Length, projection, draws, (uint)draws.Length), context);
    }
    public byte[] ReadGBuffer(uint attachment, int width, int height)
    {
        if (attachment > 3 || width <= 0 || height <= 0) throw new ArgumentException("Invalid stock attachment/dimensions.");
        var state = GetGBufferStats();
        if (state.Width != (uint)width || state.Height != (uint)height) throw new ArgumentException("Stock readback dimensions must match the last completed pass.");
        var bytes = new byte[checked(width * height * 4)];
        Check(StockApi.Read(context, attachment, bytes, (ulong)bytes.Length), context); return bytes;
    }
    public NativeGBufferStats GetGBufferStats()
    {
        var stats = new NativeGBufferStats { Size = (uint)Marshal.SizeOf(typeof(NativeGBufferStats)), AbiVersion = AbiVersion };
        Check(StockApi.Stats(context, ref stats), context); return stats;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeGBufferDraw
{
    public ulong Mesh, Color, Normal, Extensions, Alpha;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] ObjectConstants;
}
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeGBufferStats
{
    public uint Size, AbiVersion, Width, Height, Ready, Reserved0, Reserved1, Reserved2;
    public ulong Frames, Draws;
}
internal sealed class GBufferFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int InitFn(IntPtr ctx, byte[] vertex, ulong vb, byte[] pixel, ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int UploadFn(IntPtr ctx, ulong id, [In] NativeGBufferVertex[] vertices, uint vc, uint[] indices, uint ic);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RenderFn(IntPtr ctx, uint w, uint h, byte[] frame, uint fb, float[] projection, [In] NativeGBufferDraw[] draws, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ReadFn(IntPtr ctx, uint attachment, [Out] byte[] pixels, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int StatsFn(IntPtr ctx, ref NativeGBufferStats stats);
    internal readonly InitFn Init; internal readonly UploadFn Upload; internal readonly RenderFn Render; internal readonly ReadFn Read; internal readonly StatsFn Stats;
    internal GBufferFunctions(Func<string, IntPtr> symbol)
    {
        T Get<T>(string name) where T : Delegate => (T)Marshal.GetDelegateForFunctionPointer(symbol(name), typeof(T));
        Init = Get<InitFn>("sevk_init_gbuffer"); Upload = Get<UploadFn>("sevk_upload_gbuffer_mesh"); Render = Get<RenderFn>("sevk_render_gbuffer");
        Read = Get<ReadFn>("sevk_readback_gbuffer"); Stats = Get<StatsFn>("sevk_get_gbuffer_stats");
    }
}
