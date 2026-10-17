using System.Collections.Generic;
using VRage.Render.Scene;
using VRageMath;
using VRageRender;
namespace ClientPlugin.Rendering.Interop
{
    public sealed class NativeBackend
    {
        public int PresentationWidth => 1920;
        public int PresentationHeight => 1080;
        public readonly List<NativeBillboardDraw> BillboardPackets = new();
        public static float[] MatrixValues(MatrixD m) => new[] {(float)m.M11,(float)m.M12,(float)m.M13,(float)m.M14,(float)m.M21,(float)m.M22,(float)m.M23,(float)m.M24,(float)m.M31,(float)m.M32,(float)m.M33,(float)m.M34,(float)m.M41,(float)m.M42,(float)m.M43,(float)m.M44};
        public void SetBillboardProjections(float[] values) { }
        public void RemoveResource(uint kind, ulong handle) { }
    }
    public struct NativeBillboardDraw { public ulong Texture; public uint Flags, Bucket; public float[] Vertices; public byte[] Data; }
}
namespace ClientPlugin.Rendering.Content
{
    public sealed class VulkanTextureCache
    {
        public ulong GetTexture(string path) => 1;
        public bool IsSingleChannel(ulong handle) => false;
    }
}
namespace ClientPlugin.Rendering.Scene
{
    public sealed class VulkanScene
    {
        public sealed class EnvironmentData { public Vector3D CameraPosition = new(100,200,300); }
        public EnvironmentData Environment = new();
        public MyRenderSettings RenderSettings = new() { DrawBillboards=true, DrawBillboardsStandard=true, DrawBillboardsBottom=true, DrawBillboardsTop=true, DrawBillboardsLDR=true, DrawBillboardsPostPP=true };
        public ClientPlugin.Rendering.Content.VulkanTextureCache TextureCache = new();
        public static bool IsVisible(MyActor actor) => true;
    }
}
