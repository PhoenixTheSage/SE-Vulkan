using ClientPlugin.Rendering.Interop;
using System.Threading;

namespace ClientPlugin.Rendering;

/// <summary>
/// Anomaly submits guest passes through this type. Managed code does not
/// record a Vulkan command buffer.
/// </summary>
public static class GuestBridge
{
    static NativeBackend current;
    static Scene.VulkanScene scene;
    static object[] frameInputs;
    internal static void SetFrameInputs(VRageMath.Vector3D camera, VRageMath.Vector3 color, VRageMath.Vector3 direction, float diffuse, float ambient)
    {
        frameInputs = new object[] { new double[] { camera.X, camera.Y, camera.Z },
            new float[] { color.X, color.Y, color.Z, diffuse, direction.X, direction.Y, direction.Z, ambient } };
    }
    public static object[] CaptureFrameInputs() => frameInputs == null ? null : new object[] {
        ((double[])frameInputs[0]).Clone(), ((float[])frameInputs[1]).Clone() };
    public static bool SetFrameEnvironment(byte[] packet) => current != null && current.GuestFrameEnvironment(packet);

    /// <summary>True keeps stock solar glare. A celestial draw that owns glare sets this false.</summary>
    public static bool NativeSunGlare { get; private set; } = true;

    public static void SetNativeSunGlare(bool enabled) => NativeSunGlare = enabled;
    static uint sunLightRenderId=uint.MaxValue;
    internal static uint SunLightRenderId=>Volatile.Read(ref sunLightRenderId);
    internal static void SetSunLightRenderId(uint id)=>Volatile.Write(ref sunLightRenderId,id);

    internal static void Bind(NativeBackend backend, Scene.VulkanScene currentScene = null)
    {
        current = backend;
        scene = currentScene;
    }

    public static object[] CaptureShadowInputs(uint localActor) => scene?.CaptureGuestShadowInputs(localActor);
    public static object[] CaptureCharacter(uint localActor) => scene?.CaptureGuestCharacter(localActor);

    public static object[] StageBindingState()=>current?.StageBindingState();
    public static object[][] StageBindingOwners()=>current?.StageBindingOwners();
    public static bool SetStageBindings(uint frame,uint[] ids,ulong[] images,uint[] requested,uint[] fresh,uint[] attachments,ulong velocity)=>
        current!=null&&current.SetStageBindings(frame,ids,images,requested,fresh,attachments,velocity);
    public static ulong NativeOwnerFeatures()=>current?.NativeOwnerFeatures()??0UL;
    public static object[] CompileNativeOwnerGeneration(string identity,System.Collections.Generic.Dictionary<string,byte[]> sources,System.Collections.Generic.Dictionary<string,string> defines,uint[] stages,bool restoresStock)=>current?.CompileNativeOwnerGeneration(identity,sources,defines,stages,restoresStock);
    public static object[] CompileNativeOwnerGenerationWithContracts(string identity,System.Collections.Generic.Dictionary<string,byte[]> sources,System.Collections.Generic.Dictionary<string,string> defines,uint[] stages,bool restoresStock,uint contracts)=>current?.CompileNativeOwnerGeneration(identity,sources,defines,stages,restoresStock,contracts);
    public static object[][] PostprocessCatalog()=>current?.PostprocessCatalog();
    public static object[] CompilePostprocessGeneration(string identity,System.Collections.Generic.Dictionary<string,byte[]> sources,System.Collections.Generic.Dictionary<string,string> defines,uint[] stages)=>current?.CompilePostprocessGeneration(identity,sources,defines,stages);
    public static void QueuePostprocessGeneration(ulong token,System.Func<bool> guard)=>current?.QueuePostprocessGeneration(token,guard);
    public static bool ApplyPostprocessGeneration()=>current?.ApplyPendingPostprocessGeneration() is true;
    public static void DiscardPostprocessGeneration(ulong token)=>current?.DiscardPostprocessGeneration(token);
    public static object[] PostprocessGenerationState(uint id)=>current?.PostprocessGenerationState(id);
    public static void Reset() => current?.GuestReset();
    public static int SupportedPacketFlags => current == null ? 0 : current.GuestFeatures();
    public static long ResourceEpoch=>current!=null && current.IsCreated?current.GuestEpoch:0;
    public static object[] CaptureVolumeScene()=>scene?.CaptureGuestVolumeScene();
    public static bool ConfigureDepthViews(ulong[] images,float[] matrices)=>current!=null && current.ConfigureGuestDepthViews(images,matrices);
    public static uint DepthViewsReady=>current?.GuestDepthReady??0;
    /// <summary>Diagnostics: replaces the live scene as the RayScene source.</summary>
    internal static System.Func<object[]> RayCaptureOverride;
    /// <summary>
    /// Scene half of an Anomaly RayScene v1 frame from the current Vulkan scene (meshes, instances, lights, cookies,
    /// physical light origins, camera center, bloom emissiveness). Null without a scene.
    /// </summary>
    public static object[] CaptureRayScene() => RayCaptureOverride != null ? RayCaptureOverride() : scene?.CaptureRayScene();
    /// <summary>
    /// Tenant compute session for RayScene v1 frames (frame[7][1], where DX11 passes the device context):
    /// named BCL delegates over sevk_tenant_*. Null without a renderer.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyDictionary<string, System.Delegate> TenantSession() => current?.TenantSession();

    static Scene.VulkanPostprocess postprocess;
    internal static void SetPostprocess(Scene.VulkanPostprocess value)=>postprocess=value;
    /// <summary>
    /// Keen MyToneMapping grading inputs for Display tenants, as DX11 reads MyRender11.Postprocess.Data:
    /// BloomMult, BloomDirtRatio, GrainStrength, GrainAmount, GrainSize, Contrast, Brightness, Saturation,
    /// BrightnessFactorR/G/B, Vibrance, SepiaStrength, LightColor xyz, DarkColor xyz, enableTonemapping,
    /// needsAlphaLuminance (FXAA). Null before the first postprocess configuration.
    /// </summary>
    public static float[] TonemapValues()
    {
        var p=postprocess;if(p==null)return null;var s=p.Settings;var d=s.Data;
        return new[]{d.BloomMult,d.BloomDirtRatio,d.GrainStrength,d.GrainAmount,d.GrainSize,d.Contrast,d.Brightness,d.Saturation,
            d.BrightnessFactorR,d.BrightnessFactorG,d.BrightnessFactorB,d.Vibrance,d.SepiaStrength,d.LightColor.X,d.LightColor.Y,d.LightColor.Z,
            d.DarkColor.X,d.DarkColor.Y,d.DarkColor.Z,s.EnableTonemapping?1f:0f,p.Fxaa?1f:0f};
    }
    /// <summary>Render draw time in seconds, the timebase DX11 exposes as MyCommon.FrameTime.</summary>
    public static double FrameSeconds()=>scene==null?0:scene.Environment.FrameTime.Seconds;
    /// <summary>Renderer image ids of avgLuminance, bloom and dirt; valid after the frame's tone stage.</summary>
    public static ulong[] TonemapInputImages()=>current==null?null:new[]{0xFFFF000000000018UL,0xFFFF000000000019UL,0xFFFF00000000001AUL};
    internal static void BeginScene(NativeBackend backend,Scene.VulkanScene currentScene)
    { Bind(backend,currentScene);scene?.BeginGuestDepthFrame();current?.ConfigureGuestDepthViews(System.Array.Empty<ulong>(),null); }
    public static bool ConfigureSceneDepthViews(ulong[] images,double[] worldMatrices)=>scene!=null && scene.ConfigureGuestSceneDepth(images,worldMatrices);
    public static object[] CaptureSceneDepth()=>scene?.CaptureGuestSceneDepth();

    public static bool DeclareGBufferAttachment(int slot, int format) =>
        current != null && current.DeclareGBufferAttachment((uint)slot, (uint)format);
    public static bool SetGBufferVelocityProbe(uint mode) => current != null && current.SetGBufferVelocityProbe(mode);
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
    public static bool ResourceCommand(byte[] command) => current != null && current.GuestResourceCommand(command);
    public static byte[] ExecutionStatus(ulong pipeline) => current?.GuestExecutionStatus(pipeline);

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

    /// <summary>HUD transform for an HDR display tenant: paper white x UI gain and peak in scRGB units, shoulder.</summary>
    public static void SetHdrUi(float paperWhite, float shoulder, float peak) => current?.SetHdrUi(paperWhite, shoulder, peak);

    static float hdrScreenshotPaperWhite;
    /// <summary>Scene paper white (nits) for HDR screenshots: PNG requests on an FP16 swapchain also keep an EXR. Zero disables.</summary>
    public static void SetHdrScreenshot(float paperWhiteNits) => hdrScreenshotPaperWhite = paperWhiteNits > 0 ? paperWhiteNits : 0;
    internal static float HdrScreenshotPaperWhite => hdrScreenshotPaperWhite;

    /// <summary>
    /// Display state for the HDR display contract: hdrCapable, hdrActive, tenantGraded, presentMode (-1 without a swapchain),
    /// tearingSupported, tearingActive, lowLatency, displayKnown, displayHdr10, bitsPerColor, maxLuminance,
    /// maxFullFrameLuminance, minLuminance (nits; DXGI Output6 for the window's monitor). Null without a renderer.
    /// </summary>
    public static float[] DisplayInfo()
    {
        if (current == null) return null;
        var i = current.GetDisplayInfo();
        return new float[] { i.HdrCapable, i.HdrActive, i.TenantGraded, i.PresentMode == uint.MaxValue ? -1 : i.PresentMode, i.TearingSupported, i.TearingActive,
            i.LowLatency, i.DisplayKnown, i.DisplayHdr10, i.BitsPerColor, i.MaxLuminance, i.MaxFullFrameLuminance, i.MinLuminance };
    }

    public static void SetUpscaleOwner(string id) => current?.SetUpscaleOwner(id);

    public static void SetViewClip(float farPlane) => current?.SetViewClip(farPlane);

    public static void InvalidateHistory() => current?.InvalidateGuestHistory();
}
