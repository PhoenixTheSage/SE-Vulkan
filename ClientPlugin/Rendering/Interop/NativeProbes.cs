using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private ProbeFunctions probeApi;
    private ProbeFunctions ProbeApi=>probeApi??(probeApi=new ProbeFunctions(GetSymbol));
    public bool ProbesReady {get;private set;}
    // Full startup captures complete before main lighting in this frame.
    // ProbesReady deliberately reports only the already-published state.
    public bool ProbesAvailableForFrame {
        get {var flags=CurrentProbeConfig.Flags;if((flags&1)==0)return false;if(ProbesReady)return true;if((flags&4)!=0)return false;return (flags&24)!=0 || ProbeNextFace()==5;}
    }
    public void ConfigureProbes(VulkanShaderLibrary library)
    {
        foreach(var spec in ProbeShaderVariants.Extra){var vs=library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines);var ps=library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines);if(StockGeometryObjectTint(spec.Id-192))ps=library.LoadObjectTintPixel(spec.Id-192,ps);byte[] v=SpirvRuntimeModule.FromArtifact(vs),p=SpirvRuntimeModule.FromArtifact(ps);Check(WorldApi.InitPipeline(context,spec.Id-192,v,(ulong)v.Length,p,(ulong)p.Length),context);}
        for(uint i=0;i<ProbeShaderVariants.Stages.Length;i++){var a=library.Load(ProbeShaderVariants.Stages[i]);var bytes=SpirvRuntimeModule.FromArtifact(a);Check(ProbeApi.Init(context,i,bytes,(ulong)bytes.Length),context);}
    }
    public void SetProbeAtmosphere(ulong id,float intensity,float desaturation,VRageMath.Vector3 direction)=>Check(ProbeApi.Atmosphere(context,id,intensity,desaturation,new[]{direction.X,direction.Y,direction.Z}),context);
    public NativeProbeConfig CurrentProbeConfig{get;private set;}
    private ProbeFunctions.SetPrefilterFn probePrefilter;
    public void SetProbes(NativeProbeConfig config)=>SetProbes(config,0);
    public void SetProbes(NativeProbeConfig config,uint skipLevels){if(skipLevels==0)Check(ProbeApi.Set(context,ref config),context);else{probePrefilter??=(ProbeFunctions.SetPrefilterFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_probes_prefilter"),typeof(ProbeFunctions.SetPrefilterFn));Check(probePrefilter(context,ref config,skipLevels),context);}CurrentProbeConfig=config;}
    public uint CurrentProbeFaceMask()=>ProbeFaceMask(CurrentProbeConfig.Flags);
    // The faces a probe configuration with these flags captures this frame.
    public uint ProbeFaceMask(uint flags){if((flags&1)==0||(flags&4)!=0)return 0;uint next=ProbeStatsSnapshotEnabled?ProbeNextFace():0;if(!ProbesReady||(flags&16)!=0)return 63;var schedule=GetProbeScheduleStats();if((flags&24)==0)return 1u<<((int)(ProbeStatsSnapshotEnabled?next:GetProbeStats().NextFace)%6);return schedule.Stage<6?1u<<(int)schedule.Stage:0;}

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ProbeStateFn(IntPtr ctx,out uint ready,out uint nextFace);ProbeStateFn probeState;
    // Readiness and next face without the counters: answered during an early-released recording without waiting.
    uint ProbeNextFace(){probeState??=Marshal.GetDelegateForFunctionPointer<ProbeStateFn>(GetSymbol("sevk_get_probe_state"));Check(probeState(context,out uint ready,out uint next),context);ProbesReady=ready!=0&&!probeResetPending;return next;}
    public NativeProbeStats GetProbeStats(){var s=new NativeProbeStats{Size=64,AbiVersion=AbiVersion};Check(ProbeApi.Stats(context,ref s),context);ProbesReady=s.Ready!=0&&!probeResetPending;return s;}
    // Probe scheduling without waiting on recording (SE_VULKAN_PROBE_STATS_SNAPSHOT=0 restores the waiting
    // reads; ProbeStatsSnapshot on VulkanScene toggles it in-process). VulkanProbes.Configure reads the
    // schedule from the launch snapshot, the state after the previous completed recording, and queues its
    // configuration as a deferrable call. The face mask still reads fresh state at publication, where the
    // wait now happens; a queued reset counts as not ready until BeginFrame applies it.
    public static bool ProbeStatsSnapshotEnabled=Environment.GetEnvironmentVariable("SE_VULKAN_PROBE_STATS_SNAPSHOT")!="0";
    bool probeResetPending;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ProbeStatsSnapshotFn(IntPtr ctx,ref NativeProbeStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ProbeScheduleSnapshotFn(IntPtr ctx,ref NativeProbeScheduleStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ProbeSetDeferredFn(IntPtr ctx,ref NativeProbeConfig config,uint skipLevels);
    ProbeStatsSnapshotFn probeStatsSnapshot;ProbeScheduleSnapshotFn probeScheduleSnapshot;ProbeSetDeferredFn probeSetDeferred;
    public NativeProbeStats GetProbeScheduleStatsForConfigure(out NativeProbeScheduleStats schedule)
    {
        if(!ProbeStatsSnapshotEnabled){var fresh=GetProbeStats();schedule=GetProbeScheduleStats();return fresh;}
        probeStatsSnapshot??=Marshal.GetDelegateForFunctionPointer<ProbeStatsSnapshotFn>(GetSymbol("sevk_get_probe_stats_snapshot"));probeScheduleSnapshot??=Marshal.GetDelegateForFunctionPointer<ProbeScheduleSnapshotFn>(GetSymbol("sevk_get_probe_schedule_stats_snapshot"));
        var s=new NativeProbeStats{Size=64,AbiVersion=AbiVersion};Check(probeStatsSnapshot(context,ref s),context);
        schedule=new NativeProbeScheduleStats{Size=32,AbiVersion=AbiVersion};Check(probeScheduleSnapshot(context,ref schedule),context);return s;
    }
    public void SetProbesForConfigure(NativeProbeConfig config,uint skipLevels)
    {
        if(!ProbeStatsSnapshotEnabled){SetProbes(config,skipLevels);GetProbeStats();return;}
        SetProbesDeferred(config,skipLevels);
        if((CurrentProbeConfig.Flags&2)!=0){probeResetPending=true;ProbesReady=false;}
    }
    // Public and separate so the harness can observe it as it does SetProbes.
    public void SetProbesDeferred(NativeProbeConfig config,uint skipLevels)
    {
        probeSetDeferred??=Marshal.GetDelegateForFunctionPointer<ProbeSetDeferredFn>(GetSymbol("sevk_set_probes_deferred"));
        Check(probeSetDeferred(context,ref config,skipLevels),context);CurrentProbeConfig=config;
    }
    // BeginFrame has applied every queued probe configuration.
    void ProbeConfigurationApplied()=>probeResetPending=false;
    public NativeProbeScheduleStats GetProbeScheduleStats(){var s=new NativeProbeScheduleStats{Size=32,AbiVersion=AbiVersion};Check(ProbeApi.Schedule(context,ref s),context);return s;}
    public byte[] ReadbackProbe(uint kind,uint face,uint mip,int resolution){int size=resolution>>(int)mip;var pixels=new byte[checked(size*size*8)];Check(ProbeApi.Readback(context,kind,face,mip,pixels,(ulong)pixels.Length),context);return pixels;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeProbeConfig {public uint Size,AbiVersion,Resolution,Flags;public ulong Brdf;public float Blend;public uint Reserved;[MarshalAs(UnmanagedType.ByValArray,SizeConst=7680)]public byte[] Frames;[MarshalAs(UnmanagedType.ByValArray,SizeConst=96)]public float[] Projections;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeProbeStats {public uint Size,AbiVersion;public ulong Faces,GeometryDraws,PrefilterPasses,BlendPasses,Cycles,AtmospherePasses;public uint Ready,NextFace;}
[StructLayout(LayoutKind.Sequential,Pack=8)] public struct NativeProbeScheduleStats {public uint Size,AbiVersion,Stage,Busy;public ulong Cycles,Reserved;}
internal sealed class ProbeFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr ctx,uint id,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int SetFn(IntPtr ctx,ref NativeProbeConfig config);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int SetPrefilterFn(IntPtr ctx,ref NativeProbeConfig config,uint skipLevels);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativeProbeStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int ScheduleFn(IntPtr ctx,ref NativeProbeScheduleStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int ReadbackFn(IntPtr ctx,uint kind,uint face,uint mip,[Out]byte[] pixels,ulong capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int AtmosphereFn(IntPtr ctx,ulong id,float intensity,float desaturation,float[] direction);
    internal readonly AtmosphereFn Atmosphere;internal readonly InitFn Init;internal readonly SetFn Set;internal readonly StatsFn Stats;internal readonly ScheduleFn Schedule;internal readonly ReadbackFn Readback;
    internal ProbeFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Atmosphere=Get<AtmosphereFn>("sevk_set_probe_atmosphere");Init=Get<InitFn>("sevk_init_probes");Set=Get<SetFn>("sevk_set_probes");Stats=Get<StatsFn>("sevk_get_probe_stats");Schedule=Get<ScheduleFn>("sevk_get_probe_schedule_stats");Readback=Get<ReadbackFn>("sevk_readback_probe");}
}
