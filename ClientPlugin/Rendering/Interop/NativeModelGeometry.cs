using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    public bool SharedModelGeometryEnabled {get;set;}=Environment.GetEnvironmentVariable("SE_VULKAN_STOCK_DEPTH_GEOMETRY")!="0";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int MeshRangeFn(IntPtr context,ulong id,ulong source,uint first,uint count);
    private MeshRangeFn meshRange;
    public void RegisterWorldMeshRange(ulong id,ulong source,uint first,uint count){if(meshRange==null)meshRange=(MeshRangeFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_register_world_mesh_range"),typeof(MeshRangeFn));Check(meshRange(context,id,source,first,count),context);}
}
