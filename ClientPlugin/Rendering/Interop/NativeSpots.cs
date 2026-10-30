using System;
using System.Runtime.InteropServices;
using ClientPlugin.Rendering.Shaders;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private SpotFunctions spotApi;
    private SpotFunctions SpotApi=>spotApi??(spotApi=new SpotFunctions(GetSymbol));
    public void ConfigureSpots(VulkanShaderLibrary library){var module=SpirvRuntimeModule.FromArtifact(library.Load("7e7f8a00e6cfc94f5e7f149a9a4df82ce433557cf56c91d2a08dfaa598952493"));Check(WorldApi.InitLighting(context,4,module,(ulong)module.Length),context);}
    public void SetSpots(uint resolution,NativeSpot[] spots)=>Check(SpotApi.Set(context,resolution,spots,(uint)spots.Length),context);
    public void SetSpotRedraw(ulong staticMask,ulong dynamicMask)=>Check(SpotApi.Redraw(context,staticMask,dynamicMask),context);
    public NativeShadowExclusion[] ShadowExclusions {get=>E.ShadowExclusions;set=>E.ShadowExclusions=value;}
    private void QueueShadowExclusions(){var draws=ShadowExclusions;if(modelIndicesRemapped&&draws.Length>0){draws=(NativeShadowExclusion[])draws.Clone();for(int i=0;i<draws.Length;i++)draws[i].Draw=SceneDrawIndex(draws[i].Draw);}Check(SpotApi.Exclusions(context,draws,(uint)draws.Length),context);}
    NativeShadowScheduleReadFn spotRead;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int NativeShadowScheduleReadFn(IntPtr ctx,uint light,[Out]float[] data,ulong bytes);
    public float[] ReadbackSpot(uint light,int resolution){spotRead??=PassFunction<NativeShadowScheduleReadFn>("sevk_readback_spot");var data=new float[resolution*resolution];Check(spotRead(context,light,data,(ulong)data.Length*4),context);return data;}
    public NativeSpotStats GetSpotStats(){var s=new NativeSpotStats{Size=40,AbiVersion=AbiVersion};Check(SpotApi.Stats(context,ref s),context);return s;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeSpot {public uint Light,Flags;public ulong Cookie;[MarshalAs(UnmanagedType.ByValArray,SizeConst=16)]public float[] Projection;}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeSpotStats {public uint Size,AbiVersion;public ulong Frames,Maps,Draws,Cookies;}
[StructLayout(LayoutKind.Sequential,Pack=4)]public struct NativeShadowExclusion {public uint Light,Draw;}
internal sealed class SpotFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int SetFn(IntPtr ctx,uint resolution,[In] NativeSpot[] spots,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int RedrawFn(IntPtr ctx,ulong staticMask,ulong dynamicMask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativeSpotStats s);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int ExclusionFn(IntPtr ctx,[In]NativeShadowExclusion[] values,uint count);
    internal readonly ExclusionFn Exclusions;internal readonly SetFn Set;internal readonly RedrawFn Redraw;internal readonly StatsFn Stats;
    internal SpotFunctions(Func<string,IntPtr> symbol){Exclusions=(ExclusionFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_set_shadow_exclusions"),typeof(ExclusionFn));Set=(SetFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_set_spots"),typeof(SetFn));Redraw=(RedrawFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_set_spot_redraw"),typeof(RedrawFn));Stats=(StatsFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_get_spot_stats"),typeof(StatsFn));}
}
