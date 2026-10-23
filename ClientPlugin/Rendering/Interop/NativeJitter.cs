using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    // Native temporal jitter owner: active while DLSS or FSR runs; ForceJitter runs it without an upscaler (diagnostics).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ForceJitterFn(IntPtr ctx,uint force);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int GetJitterFn(IntPtr ctx,float[] state,uint count);
    ForceJitterFn forceJitter;GetJitterFn getJitter;
    public void ForceJitter(bool force){forceJitter??=PostFunction<ForceJitterFn>("sevk_force_jitter");Check(forceJitter(context,force?1u:0u),context);}
    /// <summary>Pixel x/y, clip x/y, active (0/1), phase count, next sequence index, jittered frames.</summary>
    public float[] GetJitter(){var state=new float[8];getJitter??=PostFunction<GetJitterFn>("sevk_get_jitter");Check(getJitter(context,state,8),context);return state;}
}
