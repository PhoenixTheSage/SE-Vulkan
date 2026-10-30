using ClientPlugin.Rendering.Interop;
using ClientPlugin.Rendering.Scene;
using VRageRender;
using VRageMath;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

// Plan 3 milestone 4: everything stage B ("launch": QueueWorld through EndFrameAsync) needs, filled by stage A at its
// end. Stage B reads only this record and its emission, never the scene, settings or latches that the next frame's
// hand-off rewrites. Two records alternate, so stage A can fill frame N+1's while stage B launches frame N.
internal sealed class LaunchRecord
{
    public bool DrawScene,WorldQueued,Captured;
    // Stage gate: launched on the launch thread (counted by the managed hand-off); queued reported (managed and native).
    public bool Launched,Reported;
    public byte[] WorldFrame;public float[] Projection,Inverse;
    public MatrixD ViewProjection;public Vector3D Camera;public MyEnvironmentLightData Light;
    public bool HqTarget;
    public NativeSpriteBlit[] Sprites=System.Array.Empty<NativeSpriteBlit>();public int SpriteCount;
    public readonly VulkanDebugLines.LineBatch Lines=new();
    public uint ShadowUpdateMask;
    public NativeBackend.FrameEmission Emission;
}
