using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueWorkSetFn(IntPtr context,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueueWorkGetFn(IntPtr context,ref NativeQueueWorkStats stats);
    private QueueWorkSetFn queueWorkSet;
    private QueueWorkGetFn queueWorkGet;
    public void SetParallelQueue(bool enabled){if(queueWorkSet==null)queueWorkSet=(QueueWorkSetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_parallel_world_queue"),typeof(QueueWorkSetFn));Check(queueWorkSet(context,enabled?1u:0u),context);}
    public NativeQueueWorkStats GetQueueWorkStats(){if(queueWorkGet==null)queueWorkGet=(QueueWorkGetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_queue_work_stats"),typeof(QueueWorkGetFn));var stats=new NativeQueueWorkStats{Size=32,AbiVersion=AbiVersion};Check(queueWorkGet(context,ref stats),context);return stats;}
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeQueueWorkStats {public uint Size,AbiVersion,Enabled,Workers,Draws,ValidationUs,CopyUs,Reserved;}
