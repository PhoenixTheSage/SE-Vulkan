using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    // HDR display tenant's HUD transform on an HDR swapchain (HdrRender's DX11 UI composite). Zero paper white disables.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int SetHdrUiFn(IntPtr ctx,float paperWhite,float shoulder,float peak);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ForceHdrDisplayFn(IntPtr ctx,uint force);
    SetHdrUiFn setHdrUi;ForceHdrDisplayFn forceHdrDisplay;
    public void SetHdrUi(float paperWhite,float shoulder,float peak){if(context==IntPtr.Zero)return;setHdrUi??=PostFunction<SetHdrUiFn>("sevk_set_hdr_ui");Check(setHdrUi(context,paperWhite,shoulder,peak),context);}
    /// <summary>Diagnostics: lets display tenancy grade without an HDR swapchain.</summary>
    public void ForceHdrDisplay(bool force){forceHdrDisplay??=PostFunction<ForceHdrDisplayFn>("sevk_force_hdr_display");Check(forceHdrDisplay(context,force?1u:0u),context);}
    /// <summary>The display tenant's FP16 graded tone image (final=false) or final post output (final=true), as floats.</summary>
    public float[] ReadbackGradedOutput(bool final,int width,int height)
    {
        if(width<=0 || height<=0)throw new ArgumentOutOfRangeException();
        lightingRead??=(LightingReadFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_readback_lighting_input"),typeof(LightingReadFn));
        var data=new byte[checked(width*height*8)];Check(lightingRead(context,final?13u:12u,data,(ulong)data.Length),context);
        var result=new float[width*height*4];for(int i=0;i<result.Length;i++)result[i]=HalfToFloat(BitConverter.ToUInt16(data,i*2));return result;
    }
    static float HalfToFloat(ushort h)
    {
        int sign=(h>>15)!=0?-1:1,exponent=(h>>10)&31,mantissa=h&1023;
        if(exponent==0)return sign*mantissa*(float)Math.Pow(2,-24);
        if(exponent==31)return mantissa==0?sign*float.PositiveInfinity:float.NaN;
        return sign*(1+mantissa/1024f)*(float)Math.Pow(2,exponent-15);
    }
}
