using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Rendering.Interop
{
    // CPU upload sink. Packet/cache/font production sources run unchanged;
    // native drawing and color-space correctness have separate GPU tests.
    public sealed class NativeBackend
    {
        public readonly Dictionary<ulong, byte[]> Textures = new();
        public readonly Dictionary<ulong, bool> Srgb = new();
        public readonly Dictionary<ulong, byte[][]> Mips = new();
        public void UploadTextureMips(ulong id, int width, int height, int format, byte[][] levels, bool srgb = true) { UploadTexture(id,width,height,format,levels[0],srgb); Mips[id]=Array.ConvertAll(levels, x=>(byte[])x.Clone()); }
        public void UploadTexture(ulong id, int width, int height, int format, byte[] bytes, bool srgb = true) { Textures[id] = (byte[])bytes.Clone(); Srgb[id] = srgb; }
        public void RemoveResource(uint kind, ulong id) => Textures.Remove(id);
        public static float[] ColorValues(Color c) => new[] { c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f };
    }
    public struct NativeSprite
    {
        public ulong Texture;
        public float[] Destination, Uv, Color;
        public float Angle;
        public float[] Origin;
        public int[] Clip;
        public uint Flags;
    }
}
