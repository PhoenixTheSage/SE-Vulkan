using VRage.Library.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

// Phase 4 frame split, milestone 1 (Docs/planning/PLAN-2026-10-25-MEET-DX11.md, item 0): the renderer inputs
// Build reads, captured at the end of Apply. Build never reads the renderer fields that message processing
// rewrites (camera, light, settings, draw flags); the scene's own inputs latch through VulkanScene.LatchFrame.
internal sealed class FramePacket
{
    public bool DrawScene;
    // The scene update ran in Apply (VulkanScene.ApplyBuildOrderEnabled); otherwise Build runs it at the old place.
    public bool ApplyBuildOrder;
    public Vector3D Camera;
    public MatrixD CameraView,CameraProjection,ViewProjection;
    public BoundingFrustumD VoxelDrawFrustum;
    public MyEnvironmentLightData Light;
    public MyTextureDebugMultipliers TextureMultipliers;
    public MyRenderSettings RenderConfiguration;
    public MyRenderDeviceSettings DeviceSettings;
    public Vector2I BackBuffer;
    public long GameplayFrame;
    public MyTimeSpan DrawTime;
    public float AnimationDelta;
    // An UnloadData during Apply: Build forgets the previous particle camera.
    public bool ResetParticleCamera;
}
