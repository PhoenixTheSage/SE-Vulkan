using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
using VRageMath;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private HighlightFunctions highlightApi;
    private HighlightFunctions HighlightApi=>highlightApi??(highlightApi=new HighlightFunctions(GetSymbol));
    private readonly List<NativeHighlightDraw> highlightPackets=new();
    public void ConfigureHighlights(VulkanShaderLibrary library){foreach(var spec in HighlightShaderVariants.Extra){var vs=SpirvRuntimeModule.FromArtifact(library.LoadExtra(spec.Id,spec.Material,"Vertex",spec.Defines));var ps=SpirvRuntimeModule.FromArtifact(library.LoadExtra(spec.Id,spec.Material,"Pixel",spec.Defines));Check(WorldApi.InitPipeline(context,spec.Id-416,vs,(ulong)vs.Length,ps,(ulong)ps.Length),context);}}
    public void AddHighlightPacket(uint draw,uint first,uint count,uint instance,uint instances,Vector4 color,float thickness,bool overlap=false)=>highlightPackets.Add(new NativeHighlightDraw{Draw=draw,FirstIndex=first,IndexCount=count,FirstInstance=instance,InstanceCount=instances,Flags=overlap?1u:0u,Color=new[]{color.X,color.Y,color.Z,color.W},Thickness=thickness});
    private void QueueHighlights(){var draws=highlightPackets.ToArray();if(modelIndicesRemapped)for(int i=0;i<draws.Length;i++)draws[i].Draw=SceneDrawIndex(draws[i].Draw);Check(HighlightApi.Queue(context,draws,(uint)draws.Length),context);}
    public NativeHighlightStats GetHighlightStats(){var s=new NativeHighlightStats{Size=40,AbiVersion=AbiVersion};Check(HighlightApi.Stats(context,ref s),context);return s;}
}
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeHighlightDraw {public uint Draw,FirstIndex,IndexCount,FirstInstance,InstanceCount,Flags;[MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]public float[] Color;public float Thickness;public uint Reserved;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeHighlightStats {public uint Size,AbiVersion;public ulong Frames,Draws,BlurPasses,Compositions;}
internal sealed class HighlightFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int QueueFn(IntPtr ctx,[In] NativeHighlightDraw[] draws,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativeHighlightStats stats);
    internal readonly QueueFn Queue;internal readonly StatsFn Stats;
    internal HighlightFunctions(Func<string,IntPtr> symbol){Queue=(QueueFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_queue_highlights"),typeof(QueueFn));Stats=(StatsFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_get_highlight_stats"),typeof(StatsFn));}
}
