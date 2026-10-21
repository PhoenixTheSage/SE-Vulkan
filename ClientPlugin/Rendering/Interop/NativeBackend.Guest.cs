using System;
using System.Runtime.InteropServices;

namespace ClientPlugin.Rendering.Interop;

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestResetFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestPipelineFn(IntPtr ctx, IntPtr vs, ulong vsBytes, IntPtr vsEntry, IntPtr ps, ulong psBytes, IntPtr psEntry, out ulong pipeline);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestImageFn(IntPtr ctx, ulong id, uint width, uint height, uint depth, uint format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestUpdateFn(IntPtr ctx, ulong id, IntPtr data, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestSubmitFn(IntPtr ctx, IntPtr passes, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestReadFn(IntPtr ctx, ulong id, IntPtr rgba, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestReplayFn(IntPtr ctx, uint mark);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong GuestCountFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestFrameFn(IntPtr ctx, IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestClipFn(IntPtr ctx, float farPlane);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestFlagsFn(IntPtr ctx, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestOwnerFn(IntPtr ctx, IntPtr id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestDropFn(IntPtr ctx);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GuestFeaturesFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestResourceFn(IntPtr ctx, IntPtr command);
    static GuestResourceFn guestResource;
    static GuestResourceFn guestExecution;
    internal byte[] GuestExecutionStatus(ulong pipeline)
    {
        if ((GuestFeatures() & 16384) == 0) return null;
        if (guestExecution == null) guestExecution = Marshal.GetDelegateForFunctionPointer<GuestResourceFn>(GetSymbol("sevk_guest_execution_status"));
        var packet = new byte[48];
        Buffer.BlockCopy(new[] { 48, 1 }, 0, packet, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(pipeline), 0, packet, 8, 8);
        var pin = GCHandle.Alloc(packet, GCHandleType.Pinned);
        try { Check(guestExecution(context, pin.AddrOfPinnedObject()), context); return packet; }
        finally { pin.Free(); }
    }

    internal bool GuestResourceCommand(byte[] command)
    {
        if (context == IntPtr.Zero || command == null || command.Length != 48) return false;
        EnsureGuest();
        if ((GuestFeatures() & 8192) == 0)
            throw new NotSupportedException("Update SE-Vulkan native library: guest target clears and resource retirement require feature bit13.");
        if (guestResource == null) guestResource = Marshal.GetDelegateForFunctionPointer<GuestResourceFn>(GetSymbol("sevk_guest_resource_command"));
        // Pin only for the call: the native queue copies the complete command.
        var pin = GCHandle.Alloc(command, GCHandleType.Pinned);
        try { Check(guestResource(context, pin.AddrOfPinnedObject()), context); return true; }
        finally { pin.Free(); }
    }

    static GuestResetFn guestReset;
    static GuestPipelineFn guestPipeline;
    static GuestImageFn guestImage;
    static GuestUpdateFn guestUpdate;
    static GuestSubmitFn guestSubmit;
    static GuestReadFn guestRead;
    static GuestReplayFn guestReplay;
    static GuestCountFn guestCount;
    static GuestFrameFn guestFrame;
    static GuestClipFn guestClip;
    static GuestFlagsFn guestFlags;
    static GuestOwnerFn guestOwner;
    static GuestDropFn guestDrop;
    static GuestFeaturesFn guestFeatures;

    internal int GuestFeatures()
    {
        if (context == IntPtr.Zero) return 0;
        EnsureGuest();
        return guestFeatures == null ? 7 : (int)guestFeatures();
    }

    internal void GuestReset()
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        Check(guestReset(context), context);
    }

    internal ulong GuestCreatePipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry)
    {
        if (context == IntPtr.Zero || vs == null || ps == null) return 0;
        EnsureGuest();
        var vsPin = GCHandle.Alloc(vs, GCHandleType.Pinned);
        var psPin = GCHandle.Alloc(ps, GCHandleType.Pinned);
        IntPtr vsName = Marshal.StringToHGlobalAnsi(vsEntry ?? "main");
        IntPtr psName = Marshal.StringToHGlobalAnsi(psEntry ?? "main");
        try
        {
            Check(guestPipeline(context, vsPin.AddrOfPinnedObject(), (ulong)vs.Length, vsName, psPin.AddrOfPinnedObject(), (ulong)ps.Length, psName, out ulong id), context);
            return id;
        }
        finally
        {
            if (vsPin.IsAllocated) vsPin.Free();
            if (psPin.IsAllocated) psPin.Free();
            Marshal.FreeHGlobal(vsName);
            Marshal.FreeHGlobal(psName);
        }
    }

    internal void GuestCreateImage(ulong id, int width, int height, int depth, int format)
    {
        if (context == IntPtr.Zero || id == 0) return;
        EnsureGuest();
        Check(guestImage(context, id, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1), (uint)Math.Max(depth, 1), (uint)format), context);
    }

    internal void GuestUpdateImage(ulong id, byte[] data)
    {
        if (context == IntPtr.Zero || data == null || data.Length == 0) return;
        EnsureGuest();
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { Check(guestUpdate(context, id, pin.AddrOfPinnedObject(), (ulong)data.Length), context); }
        finally { if (pin.IsAllocated) pin.Free(); }
    }

    internal void GuestSubmit(int mark, int compose, int width, int height, ulong pipeline, ulong storage, byte[] uniforms, ulong[] sampled, int flags = 0)
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        var packet = GuestPacket(mark, compose, width, height, pipeline, storage, uniforms, sampled, flags);
        var pin = GCHandle.Alloc(packet, GCHandleType.Pinned);
        try { Check(guestSubmit(context, pin.AddrOfPinnedObject(), 1), context); }
        finally { if (pin.IsAllocated) pin.Free(); }
    }

    internal void GuestReadback(ulong id, byte[] rgba)
    {
        if (context == IntPtr.Zero || rgba == null) return;
        EnsureGuest();
        var pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try { Check(guestRead(context, id, pin.AddrOfPinnedObject(), (ulong)rgba.Length), context); }
        finally { if (pin.IsAllocated) pin.Free(); }
    }

    internal void GuestReplayNow(int mark)
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        Check(guestReplay(context, (uint)mark), context);
    }

    internal ulong GuestReplayCount()
    {
        if (context == IntPtr.Zero) return 0;
        EnsureGuest();
        return guestCount(context);
    }

    internal bool GuestFrame(byte[] frame)
    {
        if (context == IntPtr.Zero || frame == null || frame.Length < 344) return false;
        EnsureGuest();
        if (guestFrame == null) return false;
        var pin = GCHandle.Alloc(frame, GCHandleType.Pinned);
        try
        {
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(344), 0, frame, 0, 4);
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(1), 0, frame, 4, 4);
            return guestFrame(context, pin.AddrOfPinnedObject()) == 0;
        }
        finally { if (pin.IsAllocated) pin.Free(); }
    }

    static void EnsureGuest()
    {
        EnsureLoaded();
        if (guestReset != null) return;
        guestReset = Marshal.GetDelegateForFunctionPointer<GuestResetFn>(GetSymbol("sevk_guest_reset"));
        try
        {
            var featuresSymbol = GetSymbol("sevk_guest_features");
            if (featuresSymbol != IntPtr.Zero)
                guestFeatures = Marshal.GetDelegateForFunctionPointer<GuestFeaturesFn>(featuresSymbol);
        }
        catch (EntryPointNotFoundException) { /* Older ABI-1 libraries support flags 0..2 only. */ }
        guestPipeline = Marshal.GetDelegateForFunctionPointer<GuestPipelineFn>(GetSymbol("sevk_guest_create_pipeline"));
        guestImage = Marshal.GetDelegateForFunctionPointer<GuestImageFn>(GetSymbol("sevk_guest_create_image"));
        guestUpdate = Marshal.GetDelegateForFunctionPointer<GuestUpdateFn>(GetSymbol("sevk_guest_update_image"));
        guestSubmit = Marshal.GetDelegateForFunctionPointer<GuestSubmitFn>(GetSymbol("sevk_guest_submit"));
        guestRead = Marshal.GetDelegateForFunctionPointer<GuestReadFn>(GetSymbol("sevk_guest_readback"));
        guestReplay = Marshal.GetDelegateForFunctionPointer<GuestReplayFn>(GetSymbol("sevk_guest_replay_now"));
        guestCount = Marshal.GetDelegateForFunctionPointer<GuestCountFn>(GetSymbol("sevk_guest_replay_count"));
        var frameSymbol = GetSymbol("sevk_get_guest_frame");
        if (frameSymbol != IntPtr.Zero)
            guestFrame = Marshal.GetDelegateForFunctionPointer<GuestFrameFn>(frameSymbol);
        var clipSymbol = GetSymbol("sevk_set_view_clip");
        if (clipSymbol != IntPtr.Zero)
            guestClip = Marshal.GetDelegateForFunctionPointer<GuestClipFn>(clipSymbol);
        var flagsSymbol = GetSymbol("sevk_set_display_flags");
        if (flagsSymbol != IntPtr.Zero)
            guestFlags = Marshal.GetDelegateForFunctionPointer<GuestFlagsFn>(flagsSymbol);
        var ownerSymbol = GetSymbol("sevk_set_upscale_owner");
        if (ownerSymbol != IntPtr.Zero)
            guestOwner = Marshal.GetDelegateForFunctionPointer<GuestOwnerFn>(ownerSymbol);
        var dropSymbol = GetSymbol("sevk_invalidate_guest_history");
        if (dropSymbol != IntPtr.Zero)
            guestDrop = Marshal.GetDelegateForFunctionPointer<GuestDropFn>(dropSymbol);
    }

    internal void SetUpscaleOwner(string id)
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        if (guestOwner == null) return;
        var native = Marshal.StringToHGlobalAnsi(id ?? "");
        try { Check(guestOwner(context, native), context); }
        finally { Marshal.FreeHGlobal(native); }
    }

    internal void SetViewClip(float farPlane)
    {
        if (context == IntPtr.Zero || farPlane < 1f) return;
        EnsureGuest();
        if (guestClip != null) Check(guestClip(context, farPlane), context);
    }

    internal void InvalidateGuestHistory()
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        if (guestDrop != null) Check(guestDrop(context), context);
    }

    internal void SetDisplayFlags(int flags)
    {
        if (context == IntPtr.Zero) return;
        EnsureGuest();
        if (guestFlags != null) Check(guestFlags(context, unchecked((uint)flags)), context);
    }

    internal static byte[] GuestPacket(int mark, int compose, int width, int height, ulong pipeline, ulong storage, byte[] uniforms, ulong[] sampled, int flags = 0)
    {
        var bytes = new byte[416];
        Write32(bytes, 0, 416);
        Write32(bytes, 4, 1);
        Write32(bytes, 8, (uint)mark);
        Write32(bytes, 12, (uint)compose);
        Write32(bytes, 16, (uint)Math.Max(width, 0));
        Write32(bytes, 20, (uint)Math.Max(height, 0));
        Write64(bytes, 24, pipeline);
        Write64(bytes, 32, storage);
        int count = uniforms == null ? 0 : Math.Min(uniforms.Length, 256);
        Write32(bytes, 40, (uint)count);
        Write32(bytes, 44, (uint)flags);
        if (count > 0) Buffer.BlockCopy(uniforms, 0, bytes, 48, count);
        if (sampled != null)
            for (int i = 0; i < Math.Min(sampled.Length, 14); i++)
                Write64(bytes, 304 + i * 8, sampled[i]);
        return bytes;
    }

    static void Write32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    static void Write64(byte[] bytes, int offset, ulong value)
    {
        Write32(bytes, offset, (uint)value);
        Write32(bytes, offset + 4, (uint)(value >> 32));
    }
}
