using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
[StructLayout(LayoutKind.Sequential,Pack=8)]public unsafe struct NativeCriticalProfile {
    public uint Size,AbiVersion,Enabled,Stride;
    public ulong Begun,Completed,Selected,CounterOverheadNs;
    public fixed ulong Rows[110];
    public object Describe(NativeCriticalProfile first){
        string[] names={"QueueOwner","QueueTasks","PassPrepare","PassPublish","WorldPrepare","MainGeometry","CascadeGeometry","CascadeWorkers","CascadeJoin","SpotRecord","ProbeRecord","LightingPostRecord","RecordOwner","CommandAPI","DescriptorAPI","BarrierAPI","SubmitAPI","WaitAPI","AcquireAPI","PresentJoin","PoolAPI","FlushAPI"};
        var rows=new object[22];double samples=Math.Max(1,Selected-first.Selected);
        fixed(ulong* current=Rows){ulong* previous=first.Rows;for(int i=0;i<rows.Length;i++){int at=i*5;ulong calls=current[at]-previous[at],cpuReads=current[at+4]-previous[at+4];rows[i]=new{Phase=names[i],Calls=calls/samples,WallMs=(current[at+1]-previous[at+1])/1e6/samples,CpuMs=(double?)null,CpuCycles=cpuReads==calls&&calls>0?(double?)(current[at+3]-previous[at+3])/samples:null,CpuReads=cpuReads};}}
        return new{Enabled=Enabled!=0,Stride,BegunFrames=Begun-first.Begun,CompletedFrames=Completed-first.Completed,SelectedFrames=Selected-first.Selected,CounterReadMs=(CounterOverheadNs-first.CounterOverheadNs)/1e6/samples,Rows=rows,Scope="Sampled completed frames; inclusive scopes. Worker sums are not frame critical path. API buckets include loader/layers/driver work, not driver alone. Phase CPU milliseconds are withheld: Windows thread time aliases these short sampled scopes. Whole-frame CPU aggregation is provided by the managed profiler. Cycles are not converted to time. PresentAPI runs on its existing async worker; completed-present telemetry owns that timing."};
    }
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeRayProfileState {public uint Size,AbiVersion,Supported,RayQuery,MaxPrimitives,MaxInstances,ShaderReady,Refused;public ulong Builds,Dispatches;}
public sealed partial class NativeBackend {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CriticalBeginFn(IntPtr ctx,uint stride);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CriticalGetFn(IntPtr ctx,ref NativeCriticalProfile profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int CriticalRayFn(IntPtr ctx,ref NativeRayProfileState state);
    CriticalBeginFn criticalBegin;CriticalGetFn criticalGet;CriticalRayFn criticalRay;
    public void BeginCriticalFrame(uint stride=8){criticalBegin??=PassFunction<CriticalBeginFn>("sevk_critical_begin");Check(criticalBegin(context,stride),context);}
    public NativeCriticalProfile GetCriticalProfile(){criticalGet??=PassFunction<CriticalGetFn>("sevk_critical_profile");var result=new NativeCriticalProfile{Size=(uint)Marshal.SizeOf<NativeCriticalProfile>(),AbiVersion=AbiVersion};Check(criticalGet(context,ref result),context);return result;}
    public NativeRayProfileState GetCriticalRayState(){criticalRay??=PassFunction<CriticalRayFn>("sevk_get_ray_capabilities");var result=new NativeRayProfileState{Size=(uint)Marshal.SizeOf<NativeRayProfileState>(),AbiVersion=AbiVersion};Check(criticalRay(context,ref result),context);return result;}
}
