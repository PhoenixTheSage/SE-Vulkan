using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct NativeTenantStats
{
    public uint Size, Buffers, Images, Pipelines;
    public ulong Bytes, Frames, Commands, Dispatches, Draws, Rejected;
}

/// <summary>
/// Tenant compute session for Anomaly RayScene v1 tenants (sevk_tenant_*). Commands are 128-byte
/// SevkTenantCommand records and bindings 32-byte SevkTenantBinding records, passed as raw bytes so a
/// tenant needs no SE-Vulkan types.
/// </summary>
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantCreateBufferFn(IntPtr ctx, ulong bytes, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantCreateImageFn(IntPtr ctx, uint width, uint height, uint layers, uint format, uint flags, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantCreateComputeFn(IntPtr ctx, byte[] spirv, ulong bytes, [MarshalAs(UnmanagedType.LPStr)] string entry, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantCreateRasterFn(IntPtr ctx, byte[] vs, ulong vsBytes, [MarshalAs(UnmanagedType.LPStr)] string vsEntry,
        byte[] ps, ulong psBytes, [MarshalAs(UnmanagedType.LPStr)] string psEntry, uint targetCount, [In] uint[] formats, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantDestroyFn(IntPtr ctx, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantRecordFn(IntPtr ctx, byte[] commands, uint count, byte[] bindings, uint bindingCount, byte[] blob, ulong blobBytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantReadbackFn(IntPtr ctx, ulong id, uint layer, ulong offset, [Out] byte[] data, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TenantStatsFn(IntPtr ctx, out NativeTenantStats stats);
    TenantCreateBufferFn tenantCreateBuffer; TenantCreateImageFn tenantCreateImage; TenantCreateComputeFn tenantCreateCompute; TenantCreateRasterFn tenantCreateRaster;
    TenantDestroyFn tenantDestroy; TenantRecordFn tenantRecord; TenantReadbackFn tenantReadback; TenantStatsFn tenantStats;
    Dictionary<string, Delegate> tenantSession;

    public const int TenantCommandBytes = 128, TenantBindingBytes = 32;

    public ulong TenantCreateBuffer(long bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        tenantCreateBuffer ??= PostFunction<TenantCreateBufferFn>("sevk_tenant_create_buffer"); Check(tenantCreateBuffer(context, (ulong)bytes, out ulong id), context); return id;
    }
    /// <summary>format is a VkFormat value; flags bit0 forces an array view for one layer.</summary>
    public ulong TenantCreateImage(int width, int height, int layers, int format, int flags)
    {
        tenantCreateImage ??= PostFunction<TenantCreateImageFn>("sevk_tenant_create_image"); Check(tenantCreateImage(context, (uint)width, (uint)height, (uint)layers, (uint)format, (uint)flags, out ulong id), context); return id;
    }
    public ulong TenantCreateCompute(byte[] spirv, string entry)
    {
        if (spirv == null) throw new ArgumentNullException(nameof(spirv));
        tenantCreateCompute ??= PostFunction<TenantCreateComputeFn>("sevk_tenant_create_compute"); Check(tenantCreateCompute(context, spirv, (ulong)spirv.Length, entry, out ulong id), context); return id;
    }
    /// <summary>Fullscreen-triangle pipeline (three SV_VertexID vertices); target format 0 is the renderer HDR format.</summary>
    public ulong TenantCreateRaster(byte[] vs, string vsEntry, byte[] ps, string psEntry, int[] formats)
    {
        if (vs == null || ps == null || formats == null) throw new ArgumentNullException(vs == null ? nameof(vs) : ps == null ? nameof(ps) : nameof(formats));
        var native = Array.ConvertAll(formats, f => (uint)f);
        tenantCreateRaster ??= PostFunction<TenantCreateRasterFn>("sevk_tenant_create_raster");
        Check(tenantCreateRaster(context, vs, (ulong)vs.Length, vsEntry, ps, (ulong)ps.Length, psEntry, (uint)native.Length, native, out ulong id), context); return id;
    }
    public void TenantDestroy(ulong id) { tenantDestroy ??= PostFunction<TenantDestroyFn>("sevk_tenant_destroy"); Check(tenantDestroy(context, id), context); }
    public void TenantRecord(byte[] commands, byte[] bindings, byte[] blob)
    {
        commands ??= Array.Empty<byte>(); bindings ??= Array.Empty<byte>(); blob ??= Array.Empty<byte>();
        if (commands.Length % TenantCommandBytes != 0 || bindings.Length % TenantBindingBytes != 0) throw new ArgumentException("Tenant commands are 128-byte and bindings 32-byte records.");
        if (commands.Length == 0) return;
        tenantRecord ??= PostFunction<TenantRecordFn>("sevk_tenant_record");
        Check(tenantRecord(context, commands, (uint)(commands.Length / TenantCommandBytes), bindings, (uint)(bindings.Length / TenantBindingBytes), blob, (ulong)blob.Length), context);
    }
    public void TenantReadback(ulong id, int layer, long offset, byte[] data)
    {
        if (data == null || data.Length == 0) throw new ArgumentException("Readback needs a destination.", nameof(data));
        tenantReadback ??= PostFunction<TenantReadbackFn>("sevk_tenant_readback"); Check(tenantReadback(context, id, (uint)layer, (ulong)offset, data, (ulong)data.Length), context);
    }
    public NativeTenantStats TenantStats() { tenantStats ??= PostFunction<TenantStatsFn>("sevk_tenant_stats"); Check(tenantStats(context, out var s), context); return s; }

    /// <summary>
    /// The session a RayScene v1 frame carries at frame[7][1]: BCL delegates only, so tenants bind it by name.
    /// ApiVersion: Func&lt;int&gt;; CreateBuffer: Func&lt;long,ulong&gt;; CreateImage: Func&lt;int,int,int,int,int,ulong&gt;
    /// (width, height, layers, VkFormat, flags); CreateCompute: Func&lt;byte[],string,ulong&gt;;
    /// CreateRaster: Func&lt;byte[],string,byte[],string,int[],ulong&gt;; Destroy: Action&lt;ulong&gt;;
    /// Record: Action&lt;byte[],byte[],byte[]&gt; (commands, bindings, blob); Readback: Action&lt;ulong,int,long,byte[]&gt;;
    /// Stats: Func&lt;ulong[]&gt; {buffers, images, pipelines, bytes, frames, commands, dispatches, draws, rejected}.
    /// </summary>
    public IReadOnlyDictionary<string, Delegate> TenantSession()
    {
        return tenantSession ??= new Dictionary<string, Delegate>(StringComparer.Ordinal)
        {
            ["ApiVersion"] = (Func<int>)(() => 1),
            ["CreateBuffer"] = (Func<long, ulong>)TenantCreateBuffer,
            ["CreateImage"] = (Func<int, int, int, int, int, ulong>)TenantCreateImage,
            ["CreateCompute"] = (Func<byte[], string, ulong>)TenantCreateCompute,
            ["CreateRaster"] = (Func<byte[], string, byte[], string, int[], ulong>)TenantCreateRaster,
            ["Destroy"] = (Action<ulong>)TenantDestroy,
            ["Record"] = (Action<byte[], byte[], byte[]>)TenantRecord,
            ["Readback"] = (Action<ulong, int, long, byte[]>)TenantReadback,
            ["Stats"] = (Func<ulong[]>)(() => { var s = TenantStats(); return new[] { s.Buffers, s.Images, s.Pipelines, s.Bytes, s.Frames, s.Commands, s.Dispatches, s.Draws, s.Rejected }; }),
        };
    }
}
