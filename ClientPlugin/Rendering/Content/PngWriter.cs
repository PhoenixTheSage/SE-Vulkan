using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ClientPlugin.Rendering.Content;

/// <summary>Portable RGBA8 PNG writer used only on an explicit screenshot request.</summary>
internal static class PngWriter
{
    public static void Save(string path, int width, int height, byte[] rgba)
    {
        if (rgba.Length != checked(width * height * 4)) throw new ArgumentException("RGBA dimensions mismatch.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var header = new byte[13]; Write32(header, 0, (uint)width); Write32(header, 4, (uint)height); header[8] = 8; header[9] = 6;
        Chunk(file, "IHDR", header);
        using var payload = new MemoryStream();
        payload.WriteByte(0x78); payload.WriteByte(0x9C);
        uint a = 1, b = 0;
        using (var deflate = new DeflateStream(payload, CompressionMode.Compress, true))
        {
            var row = new byte[checked(width * 4 + 1)];
            for (var y = 0; y < height; ++y)
            {
                Buffer.BlockCopy(rgba, y * width * 4, row, 1, width * 4);
                deflate.Write(row, 0, row.Length);
                foreach (var value in row) { a = (a + value) % 65521; b = (b + a) % 65521; }
            }
        }
        var adler = new byte[4]; Write32(adler, 0, b << 16 | a); payload.Write(adler, 0, 4);
        Chunk(file, "IDAT", payload.ToArray()); Chunk(file, "IEND", Array.Empty<byte>());
    }
    private static void Chunk(Stream stream, string name, byte[] bytes)
    {
        var length = new byte[4]; Write32(length, 0, (uint)bytes.Length); stream.Write(length, 0, 4);
        var type = Encoding.ASCII.GetBytes(name); stream.Write(type, 0, type.Length); stream.Write(bytes, 0, bytes.Length);
        uint crc = 0xFFFFFFFF;
        void Add(byte value) { crc ^= value; for (var i = 0; i < 8; ++i) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u); }
        foreach (var value in type) Add(value); foreach (var value in bytes) Add(value);
        Write32(length, 0, ~crc); stream.Write(length, 0, 4);
    }
    private static void Write32(byte[] bytes, int offset, uint value)
    { bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16); bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value; }
}
