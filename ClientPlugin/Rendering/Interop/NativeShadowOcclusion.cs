using System;
using System.Runtime.InteropServices;
namespace ClientPlugin.Rendering.Interop;
public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowOcclusionQueueFn(IntPtr ctx,uint cascade,ulong token,[In]NativePackedOcclusionDraw[] rows,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate int ShadowOcclusionGetFn(IntPtr ctx,uint cascade,[Out]NativeOcclusionResult[] rows,uint capacity,out uint count,out ulong token);
    private ShadowOcclusionQueueFn shadowOcclusionQueue;private ShadowOcclusionGetFn shadowOcclusionGet;
    private readonly NativeOcclusionResult[] shadowOcclusionResults=new NativeOcclusionResult[8192];
    public void QueueShadowOcclusion(uint cascade,ulong token,NativePackedOcclusionDraw[] rows,int count){if(cascade>=8 || count<0 || count>rows.Length)throw new ArgumentOutOfRangeException();if(shadowOcclusionQueue==null)shadowOcclusionQueue=(ShadowOcclusionQueueFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_queue_shadow_occlusion"),typeof(ShadowOcclusionQueueFn));Check(shadowOcclusionQueue(context,cascade,token,rows,(uint)count),context);}
    internal NativeOcclusionResult[] GetShadowOcclusionBorrowed(uint cascade,out int count,out ulong token){if(shadowOcclusionGet==null)shadowOcclusionGet=(ShadowOcclusionGetFn)Marshal.GetDelegateForFunctionPointer(GetSymbol("sevk_get_shadow_occlusion"),typeof(ShadowOcclusionGetFn));Check(shadowOcclusionGet(context,cascade,shadowOcclusionResults,(uint)shadowOcclusionResults.Length,out uint size,out token),context);count=(int)size;return shadowOcclusionResults;}
    public NativeOcclusionResult[] GetShadowOcclusion(uint cascade,out ulong token){var rows=GetShadowOcclusionBorrowed(cascade,out int count,out token);var result=new NativeOcclusionResult[count];Array.Copy(rows,result,count);return result;}
}
