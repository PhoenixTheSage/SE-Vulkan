using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;
[StructLayout(LayoutKind.Sequential,Pack=4)]
public struct NativeDisplayInfo
{
    public uint Size,AbiVersion,HdrCapable,HdrActive,TenantGraded,PresentMode,TearingSupported,TearingActive,LowLatency,DisplayKnown,DisplayHdr10,BitsPerColor;
    public float MaxLuminance,MaxFullFrameLuminance,MinLuminance,Reserved;
}
public sealed partial class NativeBackend
{
    // HDR display contract (P4): display detection/state and the raw FP16 presentation capture.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int DisplayInfoFn(IntPtr ctx,ref NativeDisplayInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ReadbackHdrFn(IntPtr ctx,[Out]byte[] rgba16f,ulong bytes);
    DisplayInfoFn displayInfo;ReadbackHdrFn readbackHdr;
    public NativeDisplayInfo GetDisplayInfo()
    {
        var info=new NativeDisplayInfo{Size=(uint)Marshal.SizeOf<NativeDisplayInfo>(),AbiVersion=AbiVersion};
        if(context==IntPtr.Zero)return info;
        displayInfo??=PostFunction<DisplayInfoFn>("sevk_get_display_info");Check(displayInfo(context,ref info),context);return info;
    }
    /// <summary>Raw RGBA16F scRGB pixels of the requested presentation capture (FP16 swapchain only).</summary>
    public byte[] ReadbackHdr(int width,int height)
    {
        var pixels=new byte[checked(width*height*8)];readbackHdr??=PostFunction<ReadbackHdrFn>("sevk_readback_hdr");
        Check(readbackHdr(context,pixels,(ulong)pixels.LongLength),context);return pixels;
    }
}
