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
