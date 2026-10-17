using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int GuestPipeline2Fn(IntPtr ctx, IntPtr vs, ulong vb, IntPtr ve, IntPtr ps, ulong pb, IntPtr pe, uint contract, out ulong pipeline);
    static GuestPipeline2Fn guestPipeline2;
    static GuestSubmitFn guestSubmit2;

    static bool EnsureGuestExtended()
    {
        EnsureGuest();
        if (guestFeatures == null || (guestFeatures() & 512) == 0) return false;
        if (guestPipeline2 == null) guestPipeline2 = Marshal.GetDelegateForFunctionPointer<GuestPipeline2Fn>(GetSymbol("sevk_guest_create_pipeline2"));
        if (guestSubmit2 == null) guestSubmit2 = Marshal.GetDelegateForFunctionPointer<GuestSubmitFn>(GetSymbol("sevk_guest_submit2"));
        return true;
    }

    internal ulong GuestCreatePipeline2(byte[] vs, byte[] ps, string ve, string pe, int contract)
    {
        if (context == IntPtr.Zero || vs == null || ps == null || !EnsureGuestExtended()) return 0;
        var v = GCHandle.Alloc(vs, GCHandleType.Pinned);
        var p = GCHandle.Alloc(ps, GCHandleType.Pinned);
        IntPtr vertex = Marshal.StringToHGlobalAnsi(ve), pixel = Marshal.StringToHGlobalAnsi(pe);
        try
        {
            Check(guestPipeline2(context, v.AddrOfPinnedObject(), (ulong)vs.Length, vertex, p.AddrOfPinnedObject(), (ulong)ps.Length, pixel, (uint)contract, out ulong id), context);
            return id;
        }
        finally { v.Free(); p.Free(); Marshal.FreeHGlobal(vertex); Marshal.FreeHGlobal(pixel); }
    }

    internal bool GuestSubmit2(int mark, int compose, int width, int height, int depth, ulong pipeline, ulong[] outputs, byte[][] constants, ulong[] sampled, int flags) =>
        GuestSubmitBatch2(new[]{new object[]{mark,compose,width,height,depth,pipeline,outputs,constants,sampled,flags}});

    // A copied pass is [mark, compose, width, height, depth, pipeline,
    // outputs, constants, sampled, flags]. Native validates the entire batch
    // before appending any pass; no provider interval may depend on a prefix.
    internal bool GuestSubmitBatch2(object[][] passes)
    {
        if (context == IntPtr.Zero || !EnsureGuestExtended()) return false;
        if(passes==null || passes.Length>256)throw new ArgumentException("Expected at most 256 extended guest passes");
        var packets=new byte[568*passes.Length];
        var pins=new List<GCHandle>();
        void U(int at,uint value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,packets,at,4);
        void L(int at,ulong value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,packets,at,8);
        try {
            for(int p=0;p<passes.Length;p++) {
                var pass=passes[p];int at=p*568;
                if(pass==null || pass.Length!=10 || !(pass[0] is int) || !(pass[1] is int) || !(pass[2] is int) || !(pass[3] is int) || !(pass[4] is int) || !(pass[5] is ulong) || !(pass[9] is int) ||
                   (pass[6]!=null && !(pass[6] is ulong[])) || (pass[7]!=null && !(pass[7] is byte[][])) || (pass[8]!=null && !(pass[8] is ulong[])))
                    throw new ArgumentException("Invalid copied extended guest pass "+p);
                var outputs=(ulong[])pass[6];var constants=(byte[][])pass[7];var sampled=(ulong[])pass[8];
                if((outputs?.Length??0)>5 || (constants?.Length??0)>8 || (sampled?.Length??0)>48)throw new ArgumentException("Extended guest binding count exceeded");
                U(at,568);U(at+4,2);U(at+8,unchecked((uint)(int)pass[0]));U(at+12,unchecked((uint)(int)pass[1]));
                U(at+16,unchecked((uint)(int)pass[2]));U(at+20,unchecked((uint)(int)pass[3]));U(at+24,unchecked((uint)(int)pass[4]));U(at+28,unchecked((uint)(int)pass[9]));L(at+32,(ulong)pass[5]);
                if(sampled!=null)for(int i=0;i<sampled.Length;i++)L(at+40+i*8,sampled[i]);
                if(outputs!=null)for(int i=0;i<outputs.Length;i++)L(at+424+i*8,outputs[i]);
                if(constants!=null)for(int i=0;i<constants.Length;i++) {
                    if(constants[i]==null || constants[i].Length==0)continue;
                    if(constants[i].Length>4096)throw new ArgumentException("Extended guest constants exceed 4096 bytes");
                    var pin=GCHandle.Alloc(constants[i],GCHandleType.Pinned);pins.Add(pin);
                    L(at+464+i*8,unchecked((ulong)pin.AddrOfPinnedObject().ToInt64()));U(at+528+i*4,(uint)constants[i].Length);
                }
            }
            if(passes.Length==0)return true;
            var packetPin=GCHandle.Alloc(packets,GCHandleType.Pinned);pins.Add(packetPin);
            Check(guestSubmit2(context,packetPin.AddrOfPinnedObject(),(uint)passes.Length),context);
            return true;
        } finally {foreach(var pin in pins)pin.Free();}
    }
}
