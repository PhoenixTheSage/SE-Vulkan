using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering;

/// <summary>
/// Anomaly submits guest passes through this type. Managed code does not
/// record a Vulkan command buffer.
/// </summary>
public static class GuestBridge
{
    static NativeBackend current;
    static Scene.VulkanScene scene;

    /// <summary>True keeps stock directional glare. A celestial draw that owns glare sets this false.</summary>
    public static bool NativeSunGlare { get; private set; } = true;

    public static void SetNativeSunGlare(bool enabled) => NativeSunGlare = enabled;

    internal static void Bind(NativeBackend backend, Scene.VulkanScene currentScene = null)
    {
        current = backend;
        scene = currentScene;
    }

    public static object[] CaptureShadowInputs(uint localActor) => scene?.CaptureGuestShadowInputs(localActor);
    public static object[] CaptureCharacter(uint localActor) => scene?.CaptureGuestCharacter(localActor);

    public static void Reset() => current?.GuestReset();
    public static int SupportedPacketFlags => current == null ? 0 : current.GuestFeatures();
    public static long ResourceEpoch=>current!=null && current.IsCreated?current.GuestEpoch:0;
    public static object[] CaptureVolumeScene()=>scene?.CaptureGuestVolumeScene();
    public static bool ConfigureDepthViews(ulong[] images,float[] matrices)=>current!=null && current.ConfigureGuestDepthViews(images,matrices);
    public static uint DepthViewsReady=>current?.GuestDepthReady??0;
    internal static void BeginScene(NativeBackend backend,Scene.VulkanScene currentScene)
    { Bind(backend,currentScene);scene?.BeginGuestDepthFrame();current?.ConfigureGuestDepthViews(System.Array.Empty<ulong>(),null); }
    public static bool ConfigureSceneDepthViews(ulong[] images,double[] worldMatrices)=>scene!=null && scene.ConfigureGuestSceneDepth(images,worldMatrices);
    public static object[] CaptureSceneDepth()=>scene?.CaptureGuestSceneDepth();

    public static bool InitGBufferAttachment(int slot, int format, byte[] pixel) =>
        current != null && current.InitGBufferAttachment((uint)slot, (uint)format, pixel);

    public static ulong CreatePipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry) =>
        current == null ? 0 : current.GuestCreatePipeline(vs, ps, vsEntry, psEntry);

    public static ulong CreateExtendedPipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry, int contract) =>
        current == null ? 0 : current.GuestCreatePipeline2(vs, ps, vsEntry, psEntry, contract);

    public static bool SubmitExtended(int mark, int compose, int width, int height, int depth, ulong pipeline, ulong[] outputs, byte[][] constants, ulong[] sampled, int flags = 0) =>
        current != null && current.GuestSubmit2(mark, compose, width, height, depth, pipeline, outputs, constants, sampled, flags);
    public static bool SubmitExtendedBatch(object[][] passes)=>current!=null && current.GuestSubmitBatch2(passes);

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
