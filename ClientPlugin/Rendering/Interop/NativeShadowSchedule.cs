using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowScheduleFn(IntPtr ctx,uint enabled,uint updates,uint valid);
    private ShadowScheduleFn shadowSchedule;
    public void SetShadowSchedule(bool enabled,uint updateMask,uint validMask,bool usage=false)
    {
        if(shadowSchedule==null)shadowSchedule=(ShadowScheduleFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_shadow_schedule"),typeof(ShadowScheduleFn));
        Check(shadowSchedule(context,(enabled?1u:0u)|(enabled&&usage?2u:0u),updateMask,validMask),context);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowUsageInitFn(IntPtr ctx,byte[] data,ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowFeedbackFn(IntPtr ctx,ref NativeShadowFeedback feedback);
    private ShadowFeedbackFn shadowFeedback;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowReadFn(IntPtr ctx,uint cascade,[Out]float[] data,ulong bytes);
    private ShadowReadFn shadowRead;
    public float[] ReadbackShadow(uint cascade,int resolution){if(shadowRead==null)shadowRead=(ShadowReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_shadow"),typeof(ShadowReadFn));var data=new float[checked(resolution*resolution)];Check(shadowRead(context,cascade,data,(ulong)data.Length*4),context);return data;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowConstantsFn(IntPtr ctx,byte[] data,uint bytes);
    private ShadowConstantsFn shadowConstants;private bool hasShadowPassConstants;
    public void SetShadowPassConstants(byte[] data){if(data==null && !hasShadowPassConstants)return;if(data!=null && data.Length!=1200)throw new ArgumentException("Stock CSM constants require 1200 bytes.");if(shadowConstants==null)shadowConstants=(ShadowConstantsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_shadow_pass_constants"),typeof(ShadowConstantsFn));Check(shadowConstants(context,data,data==null?0u:1200u),context);hasShadowPassConstants=data!=null;}
    public void InitializeShadowUsage(byte[] module){var init=(ShadowUsageInitFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_init_shadow_usage"),typeof(ShadowUsageInitFn));Check(init(context,module,(ulong)module.Length),context);}
    public NativeShadowFeedback GetShadowFeedback(){if(shadowFeedback==null)shadowFeedback=(ShadowFeedbackFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_shadow_feedback"),typeof(ShadowFeedbackFn));var value=new NativeShadowFeedback{Size=(uint)Marshal.SizeOf(typeof(NativeShadowFeedback)),AbiVersion=AbiVersion,Pixels=new uint[8]};Check(shadowFeedback(context,ref value),context);return value;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeShadowFeedback {public uint Size,AbiVersion;public ulong Completion;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public uint[] Pixels;public uint UpdateMask,ValidMask;}
