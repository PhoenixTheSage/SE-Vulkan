using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueWorkSetFn(IntPtr context,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueWorkGetFn(IntPtr context,ref NativeQueueWorkStats stats);
    private QueueWorkSetFn queueWorkSet;
    private QueueWorkGetFn queueWorkGet;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AsyncPolicyFn(IntPtr context,uint packing,uint cells);
    private AsyncPolicyFn asyncPolicy;
    public void SetAsyncPolicy(bool packing,bool cells){asyncPolicy??=(AsyncPolicyFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_async_policy"),typeof(AsyncPolicyFn));Check(asyncPolicy(context,packing?1u:0u,cells?1u:0u),context);}
    public void SetParallelQueue(bool enabled){if(queueWorkSet==null)queueWorkSet=(QueueWorkSetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_parallel_world_queue"),typeof(QueueWorkSetFn));Check(queueWorkSet(context,enabled?1u:0u),context);}
    public NativeQueueWorkStats GetQueueWorkStats(){if(queueWorkGet==null)queueWorkGet=(QueueWorkGetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_queue_work_stats"),typeof(QueueWorkGetFn));var stats=new NativeQueueWorkStats{Size=32,AbiVersion=AbiVersion};Check(queueWorkGet(context,ref stats),context);return stats;}
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeQueueWorkStats {public uint Size,AbiVersion,Enabled,Workers,Draws,ValidationUs,CopyUs,Reserved;}
