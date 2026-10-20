using System;
using System.IO;

namespace ClientPlugin.Rendering.Content
{
    /// <summary>API-neutral DDS reader. Compressed mip blocks go to Vulkan unchanged.</summary>
    public sealed class DdsImage
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Format { get; private set; }
        public bool Srgb { get; private set; }
        public bool IsRawRgba { get; private set; }
        public bool IsPremultiplied { get; private set; }
        public byte[] Data { get; private set; }
        public byte[][] Mips { get; private set; }

        /// <summary>Reads six DDS faces in +X,-X,+Y,-Y,+Z,-Z order, optionally retaining their authored mip chains.</summary>
        public static DdsImage[] ReadCube(Stream stream, bool retainMips = false)
        {
            if (!stream.CanSeek) throw new NotSupportedException("Cube DDS input must support seeking over mip chains.");
            byte[] header;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) header = ReadExact(reader, 128);
            if (BitConverter.ToUInt32(header, 0) != 0x20534444 || BitConverter.ToUInt32(header, 4) != 124)
                throw new InvalidDataException("Invalid cube DDS header.");
            int width = CheckedDimension(BitConverter.ToUInt32(header, 16)), height = CheckedDimension(BitConverter.ToUInt32(header, 12));
            if (width != height) throw new InvalidDataException("DDS cube faces must be square.");
            uint mips = Math.Max(1u, BitConverter.ToUInt32(header, 28));
            if (mips > 15) throw new InvalidDataException("Invalid cube DDS mip count.");
            uint fourCc = BitConverter.ToUInt32(header, 84), caps = BitConverter.ToUInt32(header, 112);
            int block = 0, bytesPerPixel = 0;
            if (fourCc == FourCC("DX10"))
            {
                byte[] extra;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) extra = ReadExact(reader, 20);
                if (BitConverter.ToUInt32(extra, 4) != 3 || (BitConverter.ToUInt32(extra, 8) & 4) == 0 || BitConverter.ToUInt32(extra, 12) != 1)
                    throw new NotSupportedException("Expected one two-dimensional DDS cube.");
                switch (BitConverter.ToUInt32(extra, 0))
                {
                    case 28: case 29: case 87: case 91: bytesPerPixel = 4; break;
                    case 71: case 72: case 80: block = 8; break;
                    case 74: case 75: case 77: case 78: case 83: case 95: case 98: case 99: block = 16; break;
                    default: throw new NotSupportedException("Unsupported DDS cube DXGI format.");
                }
                Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, extra, 8, 4);
                byte[] expanded = new byte[148];
                Buffer.BlockCopy(header, 0, expanded, 0, 128); Buffer.BlockCopy(extra, 0, expanded, 128, 20); header = expanded;
            }
            else
            {
                if ((caps & 0x200) == 0 || (caps & 0xfc00) != 0xfc00) throw new NotSupportedException("DDS does not contain all six cube faces.");
                if (fourCc == FourCC("DXT1") || fourCc == FourCC("ATI1") || fourCc == FourCC("BC4U")) block = 8;
                else if (fourCc == FourCC("DXT2") || fourCc == FourCC("DXT3") || fourCc == FourCC("DXT4") || fourCc == FourCC("DXT5") || fourCc == FourCC("ATI2") || fourCc == FourCC("BC5U")) block = 16;
                else if (fourCc == 0) bytesPerPixel = checked((int)BitConverter.ToUInt32(header, 88) / 8);
                else throw new NotSupportedException("Unsupported legacy cube DDS pixel format.");
            }
            if (block == 0 && bytesPerPixel != 1 && bytesPerPixel != 2 && bytesPerPixel != 3 && bytesPerPixel != 4)
                throw new NotSupportedException("Unsupported cube DDS pixel depth.");
            long stride = 0;
            int topSize = 0, w = width, h = height;
            for (int mip = 0; mip < mips; ++mip)
            {
                int size = block != 0 ? checked(((w + 3) / 4) * ((h + 3) / 4) * block) : checked(w * h * bytesPerPixel);
                if (mip == 0)
                {
                    uint pitch = BitConverter.ToUInt32(header, 20);
                    if (block == 0 && pitch >= w * bytesPerPixel && pitch <= w * bytesPerPixel + 4096) size = checked((int)pitch * h);
                    topSize = size;
                }
                stride = checked(stride + size); w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            }
            if(!retainMips)Buffer.BlockCopy(BitConverter.GetBytes(1u), 0, header, 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0u), 0, header, 112, 4);
            long dataStart = stream.Position;
            if (stream.Length - dataStart < stride * 6) throw new EndOfStreamException("Truncated DDS cube face/mip data.");
            var faces = new DdsImage[6];
            for (int face = 0; face < faces.Length; ++face)
            {
                stream.Position = dataStart + face * stride;
                byte[] pixels;
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true)) pixels = ReadExact(reader, retainMips?checked((int)stride):topSize);
                using (var flat = new MemoryStream(header.Length + pixels.Length))
                {
                    flat.Write(header, 0, header.Length); flat.Write(pixels, 0, pixels.Length); flat.Position = 0;
                    faces[face] = Read(flat,retainMips);
                }
            }
            stream.Position = dataStart + stride * 6;
            return faces;
        }

        public static DdsImage Read(Stream stream, bool retainMips = false)
        {
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != 0x20534444 || reader.ReadUInt32() != 124)
                    throw new InvalidDataException("Invalid DDS header.");
                reader.ReadUInt32();
                int height = CheckedDimension(reader.ReadUInt32()), width = CheckedDimension(reader.ReadUInt32());
                uint pitch = reader.ReadUInt32();
                uint depth = reader.ReadUInt32();
                uint mipCount = Math.Max(1u, reader.ReadUInt32());
                int maximumMips = 1;
                for (int dimension = Math.Max(width, height); dimension > 1; dimension /= 2) ++maximumMips;
                if (mipCount > maximumMips) throw new InvalidDataException("DDS mip count exceeds its dimensions.");
                if (!retainMips) mipCount = 1;
                reader.ReadBytes(44);
                if (reader.ReadUInt32() != 32) throw new InvalidDataException("Invalid DDS pixel format.");
                uint pixelFlags = reader.ReadUInt32(), fourCc = reader.ReadUInt32(), bits = reader.ReadUInt32();
                uint red = reader.ReadUInt32(), green = reader.ReadUInt32(), blue = reader.ReadUInt32(), alpha = reader.ReadUInt32();
                reader.ReadUInt32(); uint caps2 = reader.ReadUInt32(); reader.ReadBytes(12);
                if (depth > 1 || (caps2 & 0x200000) != 0) throw new NotSupportedException("Volume DDS textures require a 3D image path.");
                if ((caps2 & 0x200) != 0) throw new NotSupportedException("DDS cubemaps require a layered image path.");
                int format = -1, block = 0;
                bool srgb = false, premultiplied=false;
                if (fourCc == FourCC("DX10"))
                {
                    uint dxgi = reader.ReadUInt32(), dimension = reader.ReadUInt32(), misc = reader.ReadUInt32(), array = reader.ReadUInt32();
                    premultiplied=(reader.ReadUInt32() & 7)==2; // DDS_ALPHA_MODE_PREMULTIPLIED.
                    if (dimension != 3 || array != 1 || (misc & 4) != 0)
                        throw new NotSupportedException("DDS arrays/cubemaps require a layered image path.");
                    switch (dxgi)
                    {
                        case 28: case 29: format = 0; srgb = dxgi == 29; break;
                        case 34: format = 9; break; // R16G16_FLOAT, stock ambient BRDF LUT.
                        case 87: case 91: format = 1; srgb = dxgi == 91; break;
                        case 71: case 72: format = 2; block = 8; srgb = dxgi == 72; break;
                        case 74: case 75: format = 3; block = 16; srgb = dxgi == 75; break;
                        case 77: case 78: format = 4; block = 16; srgb = dxgi == 78; break;
                        case 80: format = 5; block = 8; break;
                        case 83: format = 6; block = 16; break;
                        case 95: format = 7; block = 16; break;
                        case 98: case 99: format = 8; block = 16; srgb = dxgi == 99; break;
                        default: throw new NotSupportedException("DDS DXGI format " + dxgi + " is not supported by the native upload ABI.");
                    }
                }
                else if ((pixelFlags & 4) != 0)
                {
                    if (fourCc == FourCC("DXT1")) { format = 2; block = 8; }
                    else if (fourCc == FourCC("DXT2") || fourCc == FourCC("DXT3")) { format = 3; block = 16; premultiplied=fourCc==FourCC("DXT2"); }
                    else if (fourCc == FourCC("DXT4") || fourCc == FourCC("DXT5")) { format = 4; block = 16; premultiplied=fourCc==FourCC("DXT4"); }
                    else if (fourCc == FourCC("ATI1") || fourCc == FourCC("BC4U")) { format = 5; block = 8; }
                    else if (fourCc == FourCC("ATI2") || fourCc == FourCC("BC5U")) { format = 6; block = 16; }
                    else if (fourCc == 112) { format = 9; } // D3DFMT_G16R16F.
                    else throw new NotSupportedException("Unsupported legacy DDS FourCC " + fourCc + ".");
                }
                else
                {
                    if (bits != 8 && bits != 16 && bits != 24 && bits != 32)
                        throw new NotSupportedException("Unsupported uncompressed DDS pixel depth " + bits + ".");
                    // Legacy P8 DDS stores a 256-entry RGBA palette before the
                    // index mip chain (including the stock main-menu Keen logo).
                    // Match DDSHelper's expansion before GUI premultiplication.
                    byte[] palette = null;
                    if ((pixelFlags & 0x20) != 0)
                    {
                        if (bits != 8) throw new NotSupportedException("Unsupported paletted DDS pixel depth " + bits + ".");
                        palette = ReadExact(reader, 256 * 4);
                    }
                    int bytesPerPixel = (int)bits / 8;
                    var levels = new byte[mipCount][];
                    int w = width, h = height;
                    for (int mip = 0; mip < mipCount; ++mip)
                    {
                        int rowBytes = checked(w * bytesPerPixel);
                        int rowPitch = mip == 0 && pitch >= rowBytes && pitch <= rowBytes + 4096 ? (int)pitch : rowBytes;
                        byte[] output = levels[mip] = new byte[checked(w * h * 4)];
                        for (int y = 0; y < h; ++y)
                        {
                            byte[] row = ReadExact(reader, rowPitch);
                            for (int x = 0; x < w; ++x)
                            {
                                int target = (y * w + x) * 4;
                                if (palette != null)
                                {
                                    Buffer.BlockCopy(palette, row[x] * 4, output, target, 4);
                                    continue;
                                }
                                uint pixel = 0;
                                for (int b = 0; b < bytesPerPixel; ++b) pixel |= (uint)row[x * bytesPerPixel + b] << (8 * b);
                                bool luminance = (pixelFlags & 0x20000) != 0;
                                output[target] = Channel(pixel, red != 0 ? red : luminance ? bits == 32 ? uint.MaxValue : (1u << (int)bits) - 1 : 0, 0);
                                output[target + 1] = luminance ? output[target] : Channel(pixel, green, 0);
                                output[target + 2] = luminance ? output[target] : Channel(pixel, blue, 0);
                                output[target + 3] = Channel(pixel, alpha, 255);
                            }
                        }
                        w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                    }
                    return new DdsImage { Width = width, Height = height, Format = 0, IsRawRgba = palette != null || (bits == 32 && red != 0 && green != 0 && blue != 0), Data = levels[0], Mips = levels };
                }
                var mips = new byte[mipCount][];
                int mipWidth = width, mipHeight = height;
                for (int mip = 0; mip < mipCount; ++mip)
                {
                    int size = block != 0 ? checked(((mipWidth + 3) / 4) * ((mipHeight + 3) / 4) * block) : checked(mipWidth * mipHeight * 4);
                    mips[mip] = ReadExact(reader, size);
                    mipWidth = Math.Max(1, mipWidth / 2); mipHeight = Math.Max(1, mipHeight / 2);
                }
                return new DdsImage { Width = width, Height = height, Format = format, Srgb = srgb, IsRawRgba = format == 0 || format == 1, IsPremultiplied=premultiplied,Data = mips[0], Mips = mips };
            }
        }

        private static int CheckedDimension(uint value)
        {
            if (value == 0 || value > 16384) throw new InvalidDataException("DDS dimension outside supported range.");
            return (int)value;
        }

        private static uint FourCC(string value) => (uint)(value[0] | value[1] << 8 | value[2] << 16 | value[3] << 24);

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] result = reader.ReadBytes(count);
            if (result.Length != count) throw new EndOfStreamException("Truncated DDS mip data.");
            return result;
        }

        private static byte Channel(uint value, uint mask, byte absent)
        {
            if (mask == 0) return absent;
            int shift = 0;
            while ((mask & 1) == 0) { mask >>= 1; ++shift; }
            return (byte)(((ulong)((value >> shift) & mask) * 255 + mask / 2) / mask);
        }
    }
}
