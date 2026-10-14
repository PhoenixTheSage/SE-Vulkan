using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering;

/// <summary>
/// Anomaly submits guest passes through this type. Managed code does not
/// record a Vulkan command buffer.
/// </summary>
public static class GuestBridge
{
    static NativeBackend current;

    /// <summary>True keeps stock directional glare. A celestial draw that owns glare sets this false.</summary>
    public static bool NativeSunGlare { get; private set; } = true;

    public static void SetNativeSunGlare(bool enabled) => NativeSunGlare = enabled;

    internal static void Bind(NativeBackend backend) => current = backend;

    public static void Reset() => current?.GuestReset();

    public static bool InitGBufferAttachment(int slot, int format, byte[] pixel) =>
        current != null && current.InitGBufferAttachment((uint)slot, (uint)format, pixel);

    public static ulong CreatePipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry) =>
        current == null ? 0 : current.GuestCreatePipeline(vs, ps, vsEntry, psEntry);

    public static void CreateImage(ulong id, int width, int height, int depth, int format) =>
        current?.GuestCreateImage(id, width, height, depth, format);

    public static void UpdateImage(ulong id, byte[] data) => current?.GuestUpdateImage(id, data);

    public static void Submit(int mark, int compose, int width, int height, ulong pipeline, ulong storage, byte[] uniforms, ulong[] sampled, int flags = 0) =>
        current?.GuestSubmit(mark, compose, width, height, pipeline, storage, uniforms, sampled, flags);

    public static bool TryGetFrame(byte[] frame)
    {
        if (current == null || frame == null || frame.Length < 344)
            return false;
        return current.GuestFrame(frame);
    }

    public static void Readback(ulong id, byte[] rgba) => current?.GuestReadback(id, rgba);

    public static void ReplayNow(int mark) => current?.GuestReplayNow(mark);

    public static ulong ReplayCount() => current == null ? 0 : current.GuestReplayCount();

    public static void SetDisplayFlags(int flags) => current?.SetDisplayFlags(flags);

    public static void SetUpscaleOwner(string id) => current?.SetUpscaleOwner(id);

    public static void SetViewClip(float farPlane) => current?.SetViewClip(farPlane);

    public static void InvalidateHistory() => current?.InvalidateGuestHistory();
}
