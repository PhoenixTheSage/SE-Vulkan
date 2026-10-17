using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestDepthFn(IntPtr ctx,ulong[] images,float[] matrices,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GuestDepthReadyFn(IntPtr ctx);
    static GuestDepthFn guestDepth;
    static GuestDepthReadyFn guestDepthReady;
    public bool ConfigureGuestDepthViews(ulong[] images,float[] matrices)
    {
        if(context==IntPtr.Zero || (GuestFeatures()&2048)==0)return false;
        int count=images?.Length??0;
        if(count>3 || (count!=0 && (matrices==null || matrices.Length!=count*16)))throw new ArgumentException("Expected at most three copied scene-depth views");
        guestDepth??=Marshal.GetDelegateForFunctionPointer<GuestDepthFn>(GetSymbol("sevk_guest_depth_views"));
        Check(guestDepth(context,images,matrices,(uint)count),context);return true;
    }
    public uint GuestDepthReady
    {
        get
        {
            if(context==IntPtr.Zero || (GuestFeatures()&2048)==0)return 0;
            guestDepthReady??=Marshal.GetDelegateForFunctionPointer<GuestDepthReadyFn>(GetSymbol("sevk_guest_depth_ready"));
            return guestDepthReady(context);
        }
    }
}
