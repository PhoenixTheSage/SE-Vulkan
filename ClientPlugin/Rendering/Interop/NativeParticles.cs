using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRage.Render.Particles;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private ParticleFunctions particleApi;
    private ParticleFunctions ParticleApi=>particleApi??(particleApi=new ParticleFunctions(GetSymbol));
    public void ConfigureParticles(VulkanShaderLibrary library){if(Marshal.SizeOf(typeof(MyGPUEmitterLayoutData))!=464)throw new InvalidOperationException("Stock particle emitter layout changed.");for(uint i=0;i<TransparencyShaderVariants.Particles.Length;++i){var data=SpirvRuntimeModule.FromArtifact(library.Load(TransparencyShaderVariants.Particles[i]));if(i==8)data=SpirvRuntimeModule.RebaseVertexBindings(data);Check(ParticleApi.Init(context,i,data,(ulong)data.Length),context);}}
    public void QueueParticles(MyGPUEmitterLayoutData[] data,int count,ulong atlas,bool reset=false){if(count<0||count>1024||data==null&&count!=0||data!=null&&data.Length<count)throw new ArgumentException("Invalid emitter count.");Check(ParticleApi.Queue(context,data,(uint)count,(uint)count*464,atlas,reset?1u:0u),context);}
    [StructLayout(LayoutKind.Sequential,Pack=8)]struct ParticleReadback {public uint Size,AbiVersion,Kind,Reserved;public ulong Offset,Bytes;public IntPtr Data;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ParticleReadbackFn(IntPtr context,ref ParticleReadback request);
    ParticleReadbackFn particleReadback;
    public byte[] ReadbackParticleBuffer(uint kind,ulong offset,int bytes){
        if((GuestFeatures()&268435456)==0)throw new NotSupportedException("Update SE-Vulkan native library: particle buffer readback requires bit28.");
        if(kind>10||bytes<=0||bytes>65536||((offset|(ulong)(uint)bytes)&3UL)!=0)throw new ArgumentException("Invalid particle buffer diagnostic range.");
        var data=new byte[bytes];var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
        try{var request=new ParticleReadback{Size=40,AbiVersion=AbiVersion,Kind=kind,Offset=offset,Bytes=(ulong)bytes,Data=pin.AddrOfPinnedObject()};particleReadback??=(ParticleReadbackFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_particle_buffer"),typeof(ParticleReadbackFn));Check(particleReadback(context,ref request),context);return data;}finally{pin.Free();}
    }
    public NativeParticleStats GetParticleStats(){var s=new NativeParticleStats{Size=(uint)Marshal.SizeOf(typeof(NativeParticleStats)),AbiVersion=AbiVersion};Check(ParticleApi.Stats(context,ref s),context);return s;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeParticleStats{public uint Size,AbiVersion;public ulong Frames,Dispatches,Draws;public uint Alive,Dead,Indices,Reserved;}
internal sealed class ParticleFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr c,uint id,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr c,[In]MyGPUEmitterLayoutData[] data,uint count,uint bytes,ulong atlas,uint reset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr c,ref NativeParticleStats stats);
    internal readonly InitFn Init;internal readonly QueueFn Queue;internal readonly StatsFn Stats;
    internal ParticleFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Init=Get<InitFn>("sevk_init_particles");Queue=Get<QueueFn>("sevk_queue_particles");Stats=Get<StatsFn>("sevk_get_particle_stats");}
}
