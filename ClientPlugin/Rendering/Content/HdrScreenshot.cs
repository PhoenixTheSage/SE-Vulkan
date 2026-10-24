using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace ClientPlugin.Rendering.Content;

/// <summary>
/// HDR screenshots on an FP16 scRGB swapchain, matching HdrRender's DX11 ScreenshotPatch: the PNG request also
/// keeps the HDR original as a ZIP-compressed half-float EXR, and the PNG is an SDR conversion anchored at the
/// display tenant's scene paper white (BT.2446 Method A style knee, Reinhard shoulder, Hunt-effect desaturation).
/// The EXR encoder and SDR curve are ported from HdrRender's ExrWriter and ScreenshotPatch.SaveSdr.
/// </summary>
internal static class HdrScreenshot
{
    const int ZipBlockLines = 16, ChannelCount = 3, HalfSize = 2, PixelStride = 8;

    /// <summary>Writes path (SDR PNG) and path.exr (HDR) from tightly packed RGBA16F scRGB pixels.</summary>
    public static void Save(string path, int width, int height, byte[] rgba16f, float paperWhiteNits)
    {
        if (rgba16f == null || rgba16f.Length != checked(width * height * PixelStride)) throw new InvalidDataException("HDR screenshot byte count mismatch.");
        string exr = Path.ChangeExtension(path, ".exr");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        Parallel.Invoke(
            () => { using var stream = File.Create(exr); WriteExr(stream, rgba16f, width, height); },
            () => PngWriter.Save(path, width, height, ToSdr(rgba16f, width, height, paperWhiteNits)));
    }

    /// <summary>scRGB (1 = 80 nits) to sRGB RGBA8, anchored at paper white.</summary>
    public static byte[] ToSdr(byte[] rgba16f, int width, int height, float paperWhiteNits)
    {
        float paperWhite = Math.Max(paperWhiteNits, 1f) / 80f;
        const float knee = .75f, delta = .5f, range = 1f - knee, desaturation = 1f;
        var sdr = new byte[width * height * 4];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int source = (y * width + x) * PixelStride, target = (y * width + x) * 4;
                float r = Math.Max(Half(rgba16f, source), 0f) / paperWhite, g = Math.Max(Half(rgba16f, source + 2), 0f) / paperWhite, b = Math.Max(Half(rgba16f, source + 4), 0f) / paperWhite;
                float luma = Math.Max(.2126f * r + .7152f * g + .0722f * b, 1e-6f);
                float overbright = Math.Min(Math.Max((luma - desaturation) / luma, 0f), 1f);
                r = r * (1f - overbright) + luma * overbright; g = g * (1f - overbright) + luma * overbright; b = b * (1f - overbright) + luma * overbright;
                sdr[target] = Pack(Srgb(ToneMap(r))); sdr[target + 1] = Pack(Srgb(ToneMap(g))); sdr[target + 2] = Pack(Srgb(ToneMap(b))); sdr[target + 3] = 255;
            }
        });
        return sdr;
        static float ToneMap(float n) => n <= knee ? n : knee + range * (n - knee) / (n - knee + delta);
    }

    static float Srgb(float c) => c <= .0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1 / 2.4) - .055f;
    static byte Pack(float v) => (byte)Math.Round(Math.Min(Math.Max(v, 0f), 1f) * 255f);

    static float Half(byte[] data, int offset)
    {
        int h = data[offset] | (data[offset + 1] << 8), sign = (h >> 15) != 0 ? -1 : 1, exponent = (h >> 10) & 31, mantissa = h & 1023;
        if (exponent == 0) return sign * mantissa * 5.9604645e-8f;
        if (exponent == 31) return mantissa == 0 ? sign * float.PositiveInfinity : float.NaN;
        return sign * (1 + mantissa / 1024f) * (float)Math.Pow(2, exponent - 15);
    }

    /// <summary>Single-part scanline OpenEXR with half B,G,R channels and ZIP (16-line) compression.</summary>
    public static void WriteExr(Stream output, byte[] rgba16f, int width, int height)
    {
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        WriteHeader(writer, width, height);
        int chunks = (height + ZipBlockLines - 1) / ZipBlockLines;
        long table = output.Position;
        for (int i = 0; i < chunks; i++) writer.Write(0L);
        var offsets = new long[chunks];
        int channelBytes = width * HalfSize, lineBytes = channelBytes * ChannelCount;
        var compressed = new byte[chunks][];
        Parallel.For(0, chunks, chunk =>
        {
            int first = chunk * ZipBlockLines, lines = Math.Min(ZipBlockLines, height - first);
            var raw = new byte[lines * lineBytes];
            for (int line = 0; line < lines; line++)
            {
                int row = (first + line) * width * PixelStride, b = line * lineBytes, g = b + channelBytes, r = g + channelBytes;
                for (int x = 0; x < width; x++, b += HalfSize, g += HalfSize, r += HalfSize)
                {
                    int p = row + x * PixelStride;
                    raw[b] = rgba16f[p + 4]; raw[b + 1] = rgba16f[p + 5];
                    raw[g] = rgba16f[p + 2]; raw[g + 1] = rgba16f[p + 3];
                    raw[r] = rgba16f[p]; raw[r + 1] = rgba16f[p + 1];
                }
            }
            var zip = Zlib(raw);
            // EXR readers recognise uncompressed ZIP blocks by their size.
            compressed[chunk] = zip.Length < raw.Length ? zip : raw;
        });
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            offsets[chunk] = output.Position;
            writer.Write(chunk * ZipBlockLines); writer.Write(compressed[chunk].Length); writer.Write(compressed[chunk]);
        }
        output.Seek(table, SeekOrigin.Begin);
        foreach (long offset in offsets) writer.Write(offset);
    }

    static byte[] Zlib(byte[] raw)
    {
        int n = raw.Length, half = (n + 1) / 2, even = 0, odd = 0;
        var tmp = new byte[n];
        for (int i = 0; i < n; i++) { if ((i & 1) == 0) tmp[even++] = raw[i]; else tmp[half + odd++] = raw[i]; }
        int previous = tmp[0];
        for (int i = 1; i < n; i++) { int current = tmp[i]; tmp[i] = (byte)((current - previous + 384) & 0xFF); previous = current; }
        using var stream = new MemoryStream();
        stream.WriteByte(0x78); stream.WriteByte(0x9C);
        using (var deflate = new DeflateStream(stream, CompressionLevel.Optimal, true)) deflate.Write(tmp, 0, n);
        uint adler = Adler32(tmp, n);
        stream.WriteByte((byte)(adler >> 24)); stream.WriteByte((byte)(adler >> 16)); stream.WriteByte((byte)(adler >> 8)); stream.WriteByte((byte)adler);
        return stream.ToArray();
    }

    static uint Adler32(byte[] data, int length)
    {
        const uint mod = 65521; const int nmax = 5552;
        uint a = 1, b = 0; int i = 0;
        while (i < length) { int end = i + Math.Min(nmax, length - i); while (i < end) { a += data[i++]; b += a; } a %= mod; b %= mod; }
        return (b << 16) | a;
    }

    static void WriteHeader(BinaryWriter writer, int width, int height)
    {
        writer.Write(20000630); writer.Write(2);
        Name(writer, "channels"); Name(writer, "chlist");
        writer.Write(3 * (1 + 1 + 16) + 1);
        foreach (string channel in new[] { "B", "G", "R" }) { Name(writer, channel); writer.Write(1); writer.Write(0); writer.Write(1); writer.Write(1); }
        writer.Write((byte)0);
        Name(writer, "compression"); Name(writer, "compression"); writer.Write(1); writer.Write((byte)3);
        foreach (string window in new[] { "dataWindow", "displayWindow" }) { Name(writer, window); Name(writer, "box2i"); writer.Write(16); writer.Write(0); writer.Write(0); writer.Write(width - 1); writer.Write(height - 1); }
        Name(writer, "lineOrder"); Name(writer, "lineOrder"); writer.Write(1); writer.Write((byte)0);
        Name(writer, "pixelAspectRatio"); Name(writer, "float"); writer.Write(4); writer.Write(1f);
        Name(writer, "screenWindowCenter"); Name(writer, "v2f"); writer.Write(8); writer.Write(0f); writer.Write(0f);
        Name(writer, "screenWindowWidth"); Name(writer, "float"); writer.Write(4); writer.Write(1f);
        writer.Write((byte)0);
    }

    static void Name(BinaryWriter writer, string value) { writer.Write(Encoding.ASCII.GetBytes(value)); writer.Write((byte)0); }
}
