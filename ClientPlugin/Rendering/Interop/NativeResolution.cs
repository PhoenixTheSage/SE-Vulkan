using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    private ResolutionFunctions resolutionApi;
    private ResolutionFunctions ResolutionApi=>resolutionApi??(resolutionApi=new ResolutionFunctions(GetSymbol));
    public void SetRenderFraction(float value){Check(ResolutionApi.Set(context,value),context);RenderFraction=value;}
    public NativeResolutionStats GetResolutionStats(){var value=new NativeResolutionStats{Size=48,AbiVersion=AbiVersion};Check(ResolutionApi.Stats(context,ref value),context);return value;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]
public struct NativeResolutionStats {public uint Size,AbiVersion,Width,Height,OutputWidth,OutputHeight,TimingSupported,Reserved;public ulong Samples;public float Fraction,GpuMs;}
internal sealed class ResolutionFunctions
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int SetFn(IntPtr ctx,float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate int StatsFn(IntPtr ctx,ref NativeResolutionStats value);
    internal readonly SetFn Set;internal readonly StatsFn Stats;
    internal ResolutionFunctions(Func<string,IntPtr> symbol){Set=(SetFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_set_render_fraction"),typeof(SetFn));Stats=(StatsFn)Marshal.GetDelegateForFunctionPointer(symbol("sevk_get_resolution_stats"),typeof(StatsFn));}
}
