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
        get {var flags=CurrentProbeConfig.Flags;if((flags&1)==0)return false;if(ProbesReady)return true;if((flags&4)!=0)return false;return (flags&24)!=0 || GetProbeStats().NextFace==5;}
    }
    public void ConfigureProbes(VulkanShaderLibrary library)
    {
        foreach(var spec in ProbeShaderVariants.Extra){var vs=library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines);var ps=library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines);if(spec.Id>=256 && (spec.Id-256)%5==2)ps=library.LoadObjectTintPixel(spec.Id-192,ps);byte[] v=SpirvRuntimeModule.FromArtifact(vs),p=SpirvRuntimeModule.FromArtifact(ps);Check(WorldApi.InitPipeline(context,spec.Id-192,v,(ulong)v.Length,p,(ulong)p.Length),context);}
        for(uint i=0;i<ProbeShaderVariants.Stages.Length;i++){var a=library.Load(ProbeShaderVariants.Stages[i]);var bytes=SpirvRuntimeModule.FromArtifact(a);Check(ProbeApi.Init(context,i,bytes,(ulong)bytes.Length),context);}
    }
    public void SetProbeAtmosphere(ulong id,float intensity,float desaturation,VRageMath.Vector3 direction)=>Check(ProbeApi.Atmosphere(context,id,intensity,desaturation,new[]{direction.X,direction.Y,direction.Z}),context);
    public NativeProbeConfig CurrentProbeConfig{get;private set;}
    private ProbeFunctions.SetPrefilterFn probePrefilter;
    public void SetProbes(NativeProbeConfig config)=>SetProbes(config,0);
    public void SetProbes(NativeProbeConfig config,uint skipLevels){if(skipLevels==0)Check(ProbeApi.Set(context,ref config),context);else{probePrefilter??=(ProbeFunctions.SetPrefilterFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_probes_prefilter"),typeof(ProbeFunctions.SetPrefilterFn));Check(probePrefilter(context,ref config,skipLevels),context);}CurrentProbeConfig=config;}
    public uint CurrentProbeFaceMask(){var config=CurrentProbeConfig;if((config.Flags&1)==0||(config.Flags&4)!=0)return 0;if(!ProbesReady||(config.Flags&16)!=0)return 63;var schedule=GetProbeScheduleStats();if((config.Flags&24)==0)return 1u<<((int)GetProbeStats().NextFace%6);return schedule.Stage<6?1u<<(int)schedule.Stage:0;}

    public NativeProbeStats GetProbeStats(){var s=new NativeProbeStats{Size=64,AbiVersion=AbiVersion};Check(ProbeApi.Stats(context,ref s),context);ProbesReady=s.Ready!=0;return s;}
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
