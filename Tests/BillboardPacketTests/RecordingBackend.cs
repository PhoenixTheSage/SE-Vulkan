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
        // Resident-only contract of the production cache: a miss inside the scope throws.
        public static bool Resident = true;
        [System.ThreadStatic] private static bool strict;
        public sealed class TextureNotResident : System.Exception { public TextureNotResident(string path) : base(path) { } }
        public readonly struct ResidentScope : System.IDisposable { private readonly bool previous; internal ResidentScope(bool previous) { this.previous = previous; strict = true; } public void Dispose() => strict = previous; }
        public static ResidentScope ResidentOnly() => new ResidentScope(strict);
        public ulong GetTexture(string path) { if (strict && !Resident) throw new TextureNotResident(path); return 1; }
        public bool IsSingleChannel(ulong handle) => false;
    }
}
namespace ClientPlugin.Rendering.Scene
{
    public sealed class VulkanScene
    {
        public sealed class EnvironmentData { public Vector3D CameraPosition = new(100,200,300); }
        public EnvironmentData Environment = new();
        // Build-side view and the side-picked camera of the production scene (frame split, milestone 1).
        public EnvironmentData FrameView => Environment;
        public Vector3D SideCamera => Environment.CameraPosition;
        public MyRenderSettings RenderSettings = new() { DrawBillboards=true, DrawBillboardsStandard=true, DrawBillboardsBottom=true, DrawBillboardsTop=true, DrawBillboardsLDR=true, DrawBillboardsPostPP=true };
        public ClientPlugin.Rendering.Content.VulkanTextureCache TextureCache = new();
        public static bool IsVisible(MyActor actor) => true;
    }
}
