using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Interop
{
    // Copies the ABI inputs just as native upload does. No Vulkan device or D3D11 device is created.
    public sealed class NativeBackend
    {
        public sealed class Mesh { public float[] Vertices; public uint[] Indices; }
        public sealed class Draw { public ulong Mesh; public MatrixD World; public Color Color; public Vector4 LinearTint; public Vector3 Hsv; public float Emissivity, Dithering; public uint Flags; }
        public readonly Dictionary<ulong, Mesh> Meshes = new();
        public readonly Dictionary<ulong, byte[]> Textures = new();
        public readonly Dictionary<ulong, byte[][]> TextureMipLevels = new();
        public readonly List<Draw> Draws = new();
        public NativeLight[] Lights = Array.Empty<NativeLight>();
        public bool WorldReady=>false;
        public readonly Dictionary<uint,ClientPlugin.Rendering.Shaders.ShaderMetadata> WorldLayouts=new();
        public static float[] MatrixValues(MatrixD m)=>new[]{(float)m.M11,(float)m.M12,(float)m.M13,(float)m.M14,(float)m.M21,(float)m.M22,(float)m.M23,(float)m.M24,(float)m.M31,(float)m.M32,(float)m.M33,(float)m.M34,(float)m.M41,(float)m.M42,(float)m.M43,(float)m.M44};
        public void UploadWorldMesh(ulong id,NativeWorldVertex[] v,uint[] i)=>throw new NotSupportedException("Use WorldPassGpuTests for native stock rendering.");
        public void UploadWorldCube(ulong id,int w,int h,int f,byte[] b,bool s)=>throw new NotSupportedException("Use WorldPassGpuTests for native stock rendering.");
        public void SetWorldSkybox(ulong id)=>throw new NotSupportedException("Use WorldPassGpuTests for native stock rendering.");
        public void AddWorldPacket(uint p,ulong m,ulong c,ulong n,ulong e,ulong a,byte[] o,byte[] s=null,uint count=1,uint flags=0)=>throw new NotSupportedException("Use WorldPassGpuTests for native stock rendering.");
        public void UploadMesh(ulong id, float[] vertices, uint[] indices)
        {
            foreach (uint index in indices) if (index >= vertices.Length / 8) throw new InvalidOperationException("GPU mesh index out of bounds.");
            foreach (float value in vertices) if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Nonfinite mesh vertex.");
            Meshes[id] = new Mesh { Vertices = (float[])vertices.Clone(), Indices = (uint[])indices.Clone() };
        }
        public void UploadTexture(ulong id, int width, int height, int format, byte[] data, bool srgb = true) { Textures[id] = (byte[])data.Clone(); }
        public void UploadTextureMips(ulong id, int width, int height, int format, byte[][] levels, bool srgb = true)
        {
            var owned = new byte[levels.Length][];
            for (int i = 0; i < levels.Length; ++i) owned[i] = (byte[])levels[i].Clone();
            TextureMipLevels[id] = owned; Textures[id] = owned[0];
        }
        public void RemoveResource(uint kind, ulong id) { if (kind == 0) { Textures.Remove(id); TextureMipLevels.Remove(id); } else Meshes.Remove(id); }
        public void DrawMesh(ulong mesh, MatrixD world, Color tint, ulong texture, uint flags = 0, ulong normal = 0, ulong extensions = 0, ulong alpha = 0, Vector3? hsv = null, float emissivity = 0, float dithering = 0)
        { Draws.Add(new Draw { Mesh = mesh, World = world, Color = tint, Hsv = hsv ?? Vector3.Zero, Emissivity = emissivity, Dithering = dithering, Flags = flags }); }
        public void DrawMesh(ulong mesh, MatrixD world, Vector4 linearTint, ulong texture, uint flags = 0)
        { Draws.Add(new Draw { Mesh = mesh, World = world, LinearTint = linearTint, Flags = flags }); }
        public void SetLights(NativeLight[] lights) { Lights = lights; }
    }
    public struct NativeLight
    {
        public float[] PositionRange, ColorIntensity, DirectionCone;
        public uint Flags, Reserved0, Reserved1, Reserved2;
    }
}
