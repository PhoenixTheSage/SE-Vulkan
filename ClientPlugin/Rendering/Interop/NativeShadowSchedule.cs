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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowCacheMaskFn(IntPtr ctx,out uint mask);
    private ShadowCacheMaskFn shadowCacheMask;
    public uint GetShadowCacheMask(){shadowCacheMask??=(ShadowCacheMaskFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_shadow_cache_mask"),typeof(ShadowCacheMaskFn));Check(shadowCacheMask(context,out uint mask),context);return mask;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowReadFn(IntPtr ctx,uint cascade,[Out]float[] data,ulong bytes);
    private ShadowReadFn shadowRead;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int LightingReadFn(IntPtr ctx,uint input,[Out]byte[] data,ulong bytes);
    private LightingReadFn lightingRead;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int LightingDiagnosticFn(IntPtr ctx,uint enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int LightingStageReadFn(IntPtr ctx,uint stage,[Out]float[] data,ulong bytes);
    private LightingDiagnosticFn lightingDiagnostics;
    private LightingStageReadFn lightingStageRead;
    private LightingDiagnosticFn lightingDiagnosticSampler;
    public void SetLightingDiagnosticSampler(uint mode){if(mode>1)throw new ArgumentOutOfRangeException(nameof(mode));lightingDiagnosticSampler??=(LightingDiagnosticFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_lighting_diagnostic_sampler"),typeof(LightingDiagnosticFn));Check(lightingDiagnosticSampler(context,mode),context);}
    public void SetLightingDiagnostics(bool enabled){lightingDiagnostics??=(LightingDiagnosticFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_lighting_diagnostics"),typeof(LightingDiagnosticFn));Check(lightingDiagnostics(context,enabled?1u:0u),context);}
    public void ConfigureLightingTerms(byte[] module){if(module==null)throw new ArgumentNullException(nameof(module));Check(WorldApi.InitLighting(context,5,module,(ulong)module.Length),context);}
    public float[] ReadbackLightingStage(uint stage,int width,int height){if(stage>10 || width<=0 || height<=0)throw new ArgumentOutOfRangeException();lightingStageRead??=(LightingStageReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_stage"),typeof(LightingStageReadFn));var data=new float[checked(width*height*4)];Check(lightingStageRead(context,stage,data,(ulong)data.Length*4),context);return data;}
    public byte[] ReadbackLightingInput(uint input,int width,int height){if(input>1 || width<=0 || height<=0)throw new ArgumentOutOfRangeException();lightingRead??=(LightingReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_input"),typeof(LightingReadFn));var data=new byte[checked(width*height)];Check(lightingRead(context,input,data,(ulong)data.Length),context);return data;}
    public byte[] ReadbackFxaaInput(bool after,int width,int height){if(width<=0 || height<=0)throw new ArgumentOutOfRangeException();lightingRead??=(LightingReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_input"),typeof(LightingReadFn));var data=new byte[checked(width*height*4)];Check(lightingRead(context,after?3u:2u,data,(ulong)data.Length),context);return data;}
    public byte[] ReadbackGbufferColor(int width,int height){if(width<=0 || height<=0)throw new ArgumentOutOfRangeException();lightingRead??=(LightingReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_input"),typeof(LightingReadFn));var data=new byte[checked(width*height*4)];Check(lightingRead(context,4,data,(ulong)data.Length),context);return data;}
    public byte[] ReadbackWorldGbuffer(uint target,int width,int height){if(target>3 || width<=0 || height<=0)throw new ArgumentOutOfRangeException();lightingRead??=(LightingReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_input"),typeof(LightingReadFn));var data=new byte[checked(width*height*4)];Check(lightingRead(context,4+target,data,(ulong)data.Length),context);return data;}
    public float[] ReadbackShadow(uint cascade,int resolution){if(shadowRead==null)shadowRead=(ShadowReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_shadow"),typeof(ShadowReadFn));var data=new float[checked(resolution*resolution)];Check(shadowRead(context,cascade,data,(ulong)data.Length*4),context);return data;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowConstantsFn(IntPtr ctx,byte[] data,uint bytes);
    private ShadowConstantsFn shadowConstants;private bool hasShadowPassConstants;
    public void SetShadowPassConstants(byte[] data){if(data==null && !hasShadowPassConstants)return;if(data!=null && data.Length!=1200)throw new ArgumentException("Stock CSM constants require 1200 bytes.");if(shadowConstants==null)shadowConstants=(ShadowConstantsFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_set_shadow_pass_constants"),typeof(ShadowConstantsFn));Check(shadowConstants(context,data,data==null?0u:1200u),context);hasShadowPassConstants=data!=null;}
    public void InitializeShadowUsage(byte[] module){var init=(ShadowUsageInitFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_init_shadow_usage"),typeof(ShadowUsageInitFn));Check(init(context,module,(ulong)module.Length),context);}
    public NativeShadowFeedback GetShadowFeedback(){if(shadowFeedback==null)shadowFeedback=(ShadowFeedbackFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_shadow_feedback"),typeof(ShadowFeedbackFn));var value=new NativeShadowFeedback{Size=(uint)Marshal.SizeOf(typeof(NativeShadowFeedback)),AbiVersion=AbiVersion,Pixels=new uint[8]};Check(shadowFeedback(context,ref value),context);return value;}
}
[StructLayout(LayoutKind.Sequential,Pack=8)]public struct NativeShadowFeedback {public uint Size,AbiVersion;public ulong Completion;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public uint[] Pixels;public uint UpdateMask,ValidMask;}
