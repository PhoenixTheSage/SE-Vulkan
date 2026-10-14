using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private DecalFunctions decalApi;private DecalFunctions DecalApi=>decalApi??(decalApi=new DecalFunctions(GetSymbol));
    public readonly List<NativeDecalDraw> DecalPackets=new List<NativeDecalDraw>();
    public void ConfigureDecals(VulkanShaderLibrary library){foreach(var spec in TransparencyShaderVariants.Decals){var v=SpirvRuntimeModule.FromArtifact(library.Load(spec.Vertex));var p=SpirvRuntimeModule.FromArtifact(library.Load(spec.Pixel));Check(DecalApi.Init(context,spec.Id,v,(ulong)v.Length,p,(ulong)p.Length),context);}}
    private void QueueDecals()=>Check(DecalApi.Queue(context,DecalPackets.ToArray(),(uint)DecalPackets.Count),context);
    private void ClearDecalPackets(){DecalPackets.Clear();if(WorldReady)Check(DecalApi.Queue(context,null,0),context);}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeDecalDraw{public uint Size,Role,Reserved0,Reserved1;public ulong Alpha,Color,Normal,Extensions;[MarshalAs(UnmanagedType.ByValArray,SizeConst=128)]public byte[] Constants;}
internal sealed class DecalFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int InitFn(IntPtr c,uint role,byte[] vertex,ulong vb,byte[] pixel,ulong pb);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr c,[In]NativeDecalDraw[] draws,uint count);
    internal readonly InitFn Init;internal readonly QueueFn Queue;
    internal DecalFunctions(Func<string,IntPtr> symbol){T Get<T>(string name)where T:Delegate=>(T)Marshal.GetDelegateForFunctionPointer(symbol(name),typeof(T));Init=Get<InitFn>("sevk_init_decals");Queue=Get<QueueFn>("sevk_queue_decals");}
}
