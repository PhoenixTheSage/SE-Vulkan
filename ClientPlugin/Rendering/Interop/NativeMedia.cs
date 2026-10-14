using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ClientPlugin.Platform;

namespace ClientPlugin.Rendering.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeMediaFrame
{
    public uint Size, AbiVersion, State, Width, Height, Reserved;
    public ulong Sequence, Frames, Dropped, ConversionUs;
    public long PositionUs, AvOffsetUs;
    public IntPtr Pixels;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public byte[] Error;
    public string Failure => Error == null ? null : Encoding.UTF8.GetString(Error).TrimEnd('\0');
    public static NativeMediaFrame Empty => new() { Size = (uint)Marshal.SizeOf(typeof(NativeMediaFrame)), AbiVersion = NativeBackend.AbiVersion };
}

public sealed partial class NativeBackend
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MediaCreateFn(IntPtr directory, out IntPtr media);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MediaPlayFn(IntPtr media, uint id, IntPtr path, float volume);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MediaUpdateFn(IntPtr media, uint id, ref NativeMediaFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MediaVolumeFn(IntPtr media, uint id, float volume);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MediaCloseFn(IntPtr media, uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MediaDestroyFn(IntPtr media);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StreamTextureFn(IntPtr context, ulong id, uint width, uint height, uint format, IntPtr pixels, ulong bytes);
    private MediaCreateFn mediaCreate; private MediaPlayFn mediaPlay; private MediaUpdateFn mediaUpdate;
    private MediaVolumeFn mediaVolume; private MediaCloseFn mediaClose; private MediaDestroyFn mediaDestroy; private StreamTextureFn streamTexture;

    internal bool TryCreateMedia(out IntPtr media)
    {
        mediaCreate ??= Load<MediaCreateFn>("sevk_media_create");
        mediaPlay ??= Load<MediaPlayFn>("sevk_media_play"); mediaUpdate ??= Load<MediaUpdateFn>("sevk_media_update");
        mediaVolume ??= Load<MediaVolumeFn>("sevk_media_volume"); mediaClose ??= Load<MediaCloseFn>("sevk_media_close");
        mediaDestroy ??= Load<MediaDestroyFn>("sevk_media_destroy");streamTexture ??= Load<StreamTextureFn>("sevk_update_stream_texture");
        var directory = TemporaryUtf8(Path.Combine(AssetsRoot, "Media", VulkanWindowHost.IsLinuxPlatform ? "linux-x64" : "win-x64"));
        try { return mediaCreate(directory, out media) == 0; } finally { Marshal.FreeHGlobal(directory); }
    }
    private T Load<T>(string name) where T : Delegate => (T)Marshal.GetDelegateForFunctionPointer(GetSymbol(name), typeof(T));
    private static IntPtr TemporaryUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0"); var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length); return pointer;
    }
    internal bool PlayMedia(IntPtr media, uint id, string path, float volume)
    {
        var text = TemporaryUtf8(path);
        try { return mediaPlay(media, id, text, volume) == 0; } finally { Marshal.FreeHGlobal(text); }
    }
    internal NativeMediaFrame UpdateMedia(IntPtr media, uint id)
    {
        var frame = NativeMediaFrame.Empty;
        if (mediaUpdate(media, id, ref frame) != 0) throw new InvalidOperationException("Native media ID or ABI mismatch.");
        return frame;
    }
    internal void SetMediaVolume(IntPtr media, uint id, float volume) { if(mediaVolume(media, id, volume)<0)throw new InvalidOperationException("Invalid video volume."); }
    internal void CloseMedia(IntPtr media, uint id) => mediaClose(media, id);
    internal void DestroyMedia(IntPtr media) { if (media != IntPtr.Zero) mediaDestroy(media); }
    internal void UploadMediaTexture(ulong texture, NativeMediaFrame frame)
        => Check(streamTexture(context, texture, frame.Width, frame.Height, (frame.Reserved & 8) != 0 ? 1u : 0u, frame.Pixels, checked((ulong)frame.Width * frame.Height * 4)), context);
}
