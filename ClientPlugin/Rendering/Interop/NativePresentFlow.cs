using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PresentSetFn(IntPtr ctx,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PresentFlowFn(IntPtr ctx,ref NativePresentFlowStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int PresentSamplesFn(IntPtr ctx,ulong after,IntPtr rows,uint capacity,out uint count,out ulong oldest);
    PresentSamplesFn presentSamples;
    public unsafe int GetPresentSamples(ulong after,NativePresentSample[] rows,out ulong oldest){if(presentSamples==null)presentSamples=(PresentSamplesFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_present_samples"),typeof(PresentSamplesFn));fixed(NativePresentSample* p=rows){Check(presentSamples(context,after,(IntPtr)p,(uint)rows.Length,out uint count,out oldest),context);return (int)count;}}
    PresentSetFn presentSet;PresentFlowFn presentFlow;
    public void SetAsyncPresent(bool enabled){if(presentSet==null)presentSet=(PresentSetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_async_present"),typeof(PresentSetFn));Check(presentSet(context,enabled?1u:0u),context);}
    public NativePresentFlowStats GetPresentFlowStats(){if(presentFlow==null)presentFlow=(PresentFlowFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_present_flow_stats"),typeof(PresentFlowFn));var stats=new NativePresentFlowStats{Size=64,AbiVersion=AbiVersion};Check(presentFlow(context,ref stats),context);return stats;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativePresentFlowStats {public uint Size,AbiVersion,Enabled,Pending;public ulong CompletedCalls,LastCompletedNs,TotalIntervalNs,TotalCallNs;public uint LastCallUs,LastJoinUs,Result,Reserved;}

[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativePresentSample {public ulong Sequence,EndNs,IntervalNs,CallNs;}
