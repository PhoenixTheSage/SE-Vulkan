using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int JobStatsFn(ref NativeJobStats stats,uint resetMax);
    JobStatsFn jobStats;
    // Shared native job pool telemetry; process-wide and never waits for recording.
    public NativeJobStats GetJobStats(bool resetMax=false){jobStats??=(JobStatsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_job_stats"),typeof(JobStatsFn));var stats=new NativeJobStats{Size=(uint)Marshal.SizeOf<NativeJobStats>(),AbiVersion=AbiVersion,Kinds=new NativeJobKindStats[12]};Check(jobStats(ref stats,resetMax?1u:0u),IntPtr.Zero);return stats;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeJobKindStats {public ulong Jobs,Helped,WaitNs,MaxWaitNs,RunNs;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeJobStats {
    public uint Size,AbiVersion,Workers,StageThreads,PeakQueued,KindCount,Reserved0,Reserved1;
    [MarshalAs(UnmanagedType.ByValArray,SizeConst=12)]public NativeJobKindStats[] Kinds;
    public static readonly string[] KindNames={"PassView","PassPack","CellView","QueueValidate","Upload","Cascade","Spot"};
}
