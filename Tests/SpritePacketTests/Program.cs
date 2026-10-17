using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRage;
using VRage.FileSystem;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Messages;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); ++checks; Console.WriteLine("PASS " + message); }
    private static bool Near(float x, float y) => Math.Abs(x - y) < .0001f;
    private static bool Near(float[] x, params float[] y) => x.Length == y.Length && x.Zip(y, Near).All(v => v);
    public static int Main(string[] args)
    {
        string bin = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(bin, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
        Run(Path.Combine(Path.GetDirectoryName(bin), "Content")); return 0;
    }
    private static void Run(string content)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "SeVulkanSprites-" + Guid.NewGuid().ToString("N"));
        MyFileSystem.Init(content, temporary);
        // MyFont reads MyRenderProxy.Log, whose settings initializer queries only
        // platform resource budgets. Supply a CPU platform before that initializer.
        typeof(MyVRage).GetProperty(nameof(MyVRage.Platform)).SetValue(null, DispatchProxy.Create<IVRagePlatform, CpuPlatform>());
        typeof(MyRenderProxy).GetField("m_render", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, DispatchProxy.Create<IMyRender, CpuMessageRenderer>());
        ParallelTasks.Parallel.Scheduler = new ParallelTasks.FakeTaskScheduler();
        var backend = new NativeBackend();
        using var cache = new VulkanTextureCache(backend);
        var sprites = new VulkanSprites(cache);
        var warnings = new List<string>(); sprites.Warning += warnings.Add;
        string overlay = "Textures/GUI/Screens/main_menu_overlay.dds";
        sprites.DrawTimeSeconds = 3;
        var draw = new MyRenderMessageDrawSprite { Texture = overlay, DestinationRectangle = new RectangleF(100, 80, 200, 40), SourceRectangle = new Rectangle(32, 64, 128, 256), Color = Color.White, Rotation = .25f, RotationSpeed = .5f };
        sprites.Process(draw);
        cache.GetSize(overlay, out int textureWidth, out int textureHeight);
        Check(textureWidth == 2048 && textureHeight == 2048 && backend.Textures.Count == 1, "installed overlay DDS dimensions/upload loaded through production cache");
        Check(cache.IsLoaded(overlay), "GUI-only uploaded texture reports loaded to the simulation query");
        draw.DestinationRectangle = new RectangleF(999, 999, 1, 1); draw.SourceRectangle = null; draw.Rotation = 0; draw.Texture = null;
        var packet = sprites.MainPackets(1000, 800).Single();
        Check(Near(packet.Destination, 100, 80, 200, 40) && Near(packet.Uv, 32f / 2048, 64f / 2048, 128f / 2048, 256f / 2048), "packet owns original pooled-message destination/source UV");
        var packedRotation = new HalfVector4(200,100,(float)Math.Cos(1.75f),(float)Math.Sin(1.75f)).ToVector4();
        Check(Near(packet.Angle, (float)Math.Atan2(packedRotation.W,packedRotation.Z)) && Near(packet.Origin, 100, 20), "rotation speed uses frame time and stock half-packed tangent/relative native origin");
        Check(packet.Flags == 2 && backend.Srgb[packet.Texture], "stock standard sprite uses premultiplied input and sRGB texture view");
        packet.Destination[0] = -20; packet.Uv[0] = -20; packet.Clip[0] = -20; packet.Color[0] = -20; packet.Origin[0] = -20;
        packet = sprites.MainPackets(1000, 800).Single();
        Check(Near(packet.Destination[0], 100) && packet.Uv[0] >= 0 && packet.Clip[0] == 0 && packet.Color[0] == 1 && packet.Origin[0] == 100, "returned snapshot arrays cannot mutate retained packets");

        sprites.BeginFrame();
        sprites.Process(new MyRenderMessageSpriteScissorPush { ScreenRectangle = new Rectangle(20, 10, 100, 70) });
        sprites.Process(new MyRenderMessageSpriteScissorPush { ScreenRectangle = new Rectangle(40, 30, 100, 70) });
        sprites.Process(new MyRenderMessageDrawSpriteExt { Texture = overlay, DestinationRectangle = new RectangleF(10, 20, 50, 30), Origin = new Vector2(35, 40), RightVector = Vector2.UnitY, Color = Color.White });
        sprites.Process(new MyRenderMessageDrawSprite { Texture = overlay, DestinationRectangle = new RectangleF(10, 20, 50, 30), Color = Color.White, IgnoreBounds = true });
        sprites.Process(new MyRenderMessageSpriteMainViewportScale { ScaleFactor = .5f });
        var packets = sprites.MainPackets(1000, 800);
        Check(Near(packets[0].Destination, 255, 210, 25, 15) && Near(packets[0].Origin, 12.5f, 10) && Near(packets[0].Angle, (float)Math.PI / 2), "bounded viewport centers/scales ext geometry and relative rotation origin");
        Check(packets[0].Clip.SequenceEqual(new[] { 270, 215, 40, 25 }), "nested scissor intersection scales into bounded viewport");
        Check(Near(packets[1].Destination, 10, 20, 50, 30) && packets[1].Clip.SequenceEqual(new[] { 40, 30, 80, 50 }), "IgnoreBounds keeps full viewport while retaining explicit scissor");
        sprites.Process(new MyRenderMessageSpriteScissorPop());
        sprites.Process(new MyRenderMessageDrawSprite { Texture = overlay, DestinationRectangle = new RectangleF(0, 0, 10, 10), Color = Color.White });
        Check(sprites.MainPackets(1000, 800)[2].Clip.SequenceEqual(new[] { 260, 205, 50, 35 }), "scissor pop restores parent clipping");
        sprites.BeginFrame();
        sprites.Process(new MyRenderMessageDrawSprite { Texture = overlay, DestinationRectangle = new RectangleF(-100, -100, 1200, 1000), Color = Color.White });
        Check(sprites.MainPackets(1000, 800).Single().Clip.SequenceEqual(new[] { 250, 200, 500, 400 }), "unclipped bounded geometry still clips to scaled viewport");

        sprites.BeginFrame(); sprites.Process(new MyRenderMessageSpriteMainViewportScale { ScaleFactor = .75f });
        sprites.Process(new MyRenderMessageSpriteScissorPush { ScreenRectangle = new Rectangle(1, 1, 3, 3) });
        sprites.Process(new MyRenderMessageDrawSprite { Texture = overlay, DestinationRectangle = new RectangleF(0, 0, 5, 5), Color = Color.White });
        Check(sprites.MainPackets(1001, 701).Single().Clip.SequenceEqual(new[] { 125, 88, 3, 2 }), "fractional viewport scissor truncates transformed endpoints like stock renderer");

        sprites.BeginFrame();
        sprites.Process(new MyRenderMessageDrawSpriteAtlas { Texture = overlay, Position = new Vector2(50, 60), HalfSize = new Vector2(10, 5), Scale = new Vector2(2, 3), TextureOffset = new Vector2(.1301f, .2003f), TextureSize = new Vector2(.1234f, .2345f), RightVector = Vector2.UnitX, Color = Color.White, TargetTexture = "lcd" });
        var lcd = sprites.ConsumeTarget("lcd").Single();
        Check(Near(lcd.Destination, 80, 165, 40, 30) && Near(lcd.Origin, 20, 15), "LCD target atlas geometry ignores main viewport scale");
        Check(Near(lcd.Uv, (int)(.1301f * 2048) / 2048f, (int)(.2003f * 2048) / 2048f, (int)(.1234f * 2048) / 2048f, (int)(.2345f * 2048) / 2048f), "atlas UVs preserve stock integer source-pixel quantization");
        Check(sprites.ConsumeTarget("lcd").Length == 0 && sprites.SpriteCount == 0, "target consumption drains only target packets");

        string fontPath = Path.Combine(content, "Fonts", "white", "FontDataPA.xml");
        Check(File.Exists(fontPath), "installed white font descriptor available");
        sprites.Process(new MyRenderMessageCreateFont { FontId = 5, FontPath = fontPath, ColorMask = Color.White, IsDebugFont = true });
        var font = new MyFont(fontPath); font.TryGetGlyphInfo('A', out var glyph);
        sprites.Process(new MyRenderMessageDrawString { FontIndex = 5, ScreenCoord = new Vector2(20, 30), ScreenScale = 1, ScreenMaxWidth = 200, Text = "A", ColorMask = Color.White, IgnoreBounds = true });
        packet = sprites.MainPackets(1000, 800).Single();
        float factor = 144f / 185f;
        var packedGlyph = new HalfVector4(glyph.pxWidth*factor,glyph.pxHeight*factor,20+glyph.pxLeftSideBearing*factor,30+(glyph.pxHeightOffset+glyph.pxLeftSideBearing+3.8333333f)*factor).ToVector4();
        Check(Near(packet.Destination[0], packedGlyph.Z) && Near(packet.Destination[1], packedGlyph.W), "font glyph placement preserves stock half-packed left-bearing/vertical baseline formula");
        Check(Near(packet.Destination[2], packedGlyph.X) && Near(packet.Destination[3], packedGlyph.Y) && packet.Texture != 0, "installed font glyph atlas uploads and supplies stock half-packed dimensions");
        string atlasPath = Path.Combine(font.FontDirectory,font.BitmapInfoById[glyph.nBitmapID].strFilename);
        using (var atlasStream = File.OpenRead(atlasPath))
        {
            var dds = DdsImage.Read(atlasStream,true); byte[] original=File.ReadAllBytes(atlasPath);
            Check(dds.Mips.Length==11 && dds.Mips.SelectMany(level=>level).SequenceEqual(original.Skip(148)), "installed BC7 font retains all eleven DDS mip levels without altering compressed bytes");
            Check(backend.Mips[packet.Texture].Length==11 && backend.Mips[packet.Texture][2].SequenceEqual(dds.Mips[2]), "production GUI cache uploads the actual minification mip chain");
        }
        Check(packet.Flags == 2 && backend.Srgb[packet.Texture], "FontDataPA atlas metadata selects premultiplied input with sRGB sampling");
        var statsOverlay = new VulkanRenderStats();
        sprites.BeginFrame();
        MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.NoDraw;
        statsOverlay.Draw(sprites, 1000);
        Check(sprites.SpriteCount == 0, "disabled Shift-F11 HUD adds no sprite packets");
        VRageRender.Utils.Stats.Timing.Write("FPS", 60, VRage.Stats.MyStatTypeEnum.CurrentValue, 0, 0);
        MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.SimpleTimingStats;
        statsOverlay.Draw(sprites, 1000);
        Check(sprites.SpriteCount > 0 && sprites.MainPackets(1000, 800).All(p => p.Clip[0] == 0 && p.Clip[2] == 1000), "stock timing collector renders through debug-font packets in the full viewport");
        string timing = VulkanRenderStats.ReplaceGpuTiming("FPS: 60\nRender GPU Load: 0% 0.00ms\nSimulation speed: 1\n", new ClientPlugin.Diagnostics.FrameProfiler.View { HaveFrame = true, GpuSamples = 1, GpuMs = 2.5f }, true);
        Check(timing.Contains("FPS: 60") && timing.Contains("Simulation speed: 1") && !timing.Contains("Render GPU Load") && timing.Contains("2.50ms (timestamps)"), "stock CPU/simulation values survive while DX11 GPU load is replaced by Vulkan timestamps");
        Check(VulkanRenderStats.ReplaceGpuTiming("", default, true).Contains("unavailable"), "missing Vulkan timestamps do not display a fabricated zero GPU time");
        MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.MoveNext;
        statsOverlay.Draw(sprites, 1000);
        Check(MyRenderProxy.DrawRenderStats == MyRenderProxy.MyStatsState.Draw, "developer timing cycle enters a Vulkan renderer page");
        MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.MoveNext;
        statsOverlay.Draw(sprites, 1000);
        Check(MyRenderProxy.DrawRenderStats == MyRenderProxy.MyStatsState.Last, "developer cycle exits for the game's screen owner to close");
        MyRenderProxy.DrawRenderStats = MyRenderProxy.MyStatsState.NoDraw;
        cache.Clear(); sprites.Clear();
        Check(sprites.FontCount == 1 && backend.Textures.Count == 0, "scene unload clears GPU atlases while preserving device-lifetime font definitions");
        sprites.Process(new MyRenderMessageDrawString { FontIndex = 5, ScreenCoord = Vector2.Zero, ScreenScale = 1, ScreenMaxWidth = 200, Text = "A", ColorMask = Color.White });
        Check(sprites.MainPackets(1000, 800).Length == 1 && backend.Textures.Count == 1, "HUD font atlas reloads without another CreateFont after unload");
        string rawPath = Path.Combine(temporary, "sprite-rgba.dds"); Directory.CreateDirectory(temporary);
        var header = new byte[128];
        void U32(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(header, offset);
        U32(0, 0x20534444); U32(4,124); U32(8,0x100f); U32(12,1); U32(16,1); U32(20,4); U32(76,32); U32(80,0x41); U32(88,32); U32(92,0xff); U32(96,0xff00); U32(100,0xff0000); U32(104,0xff000000); U32(108,0x1000);
        byte[] rgba = {255,80,20,128}; File.WriteAllBytes(rawPath, header.Concat(rgba).ToArray());
        ulong gui = cache.GetSpriteTexture(rawPath, out bool raw), world = cache.GetTexture(rawPath);
        byte Premul(byte c) => (byte)(Math.Pow(Math.Pow(c / 255.0,2.2)*(128/255.0),.45454545454545453)*255);
        Check(raw && backend.Srgb[gui] && backend.Textures[gui].SequenceEqual(new byte[] {Premul(255),Premul(80),Premul(20),128}), "raw GUI RGBA uses stock gamma-2.2 premultiply table before sRGB upload");
        Check(gui != world && backend.Textures[world].SequenceEqual(rgba), "GUI preprocessing never changes the world-material texture variant");
        CheckKeenLogo(content, cache, backend, sprites);
        sprites.BeginFrame();
        for (int i = 0; i < 100; ++i) sprites.Process(new MyRenderMessageDrawString { FontIndex = 99, Text = "missing" });
        Check(warnings.Count == 1, "missing-font warnings deduplicate across frames/messages");
        for (int i = 0; i < 300; ++i) sprites.Process(new MyRenderMessageDrawString { FontIndex = 1000 + i, Text = "missing" });
        Check(warnings.Count == 129 && warnings.Last().Contains("suppressed"), "unique sprite diagnostics remain bounded");
        Console.WriteLine(checks + " checks PASS; production sprite packets/cache, installed DDS/font; no GPU device created.");
    }

    private static void CheckKeenLogo(string content, VulkanTextureCache cache, NativeBackend backend, VulkanSprites sprites)
    {
        string path = Path.Combine(content, "Textures", "GUI", "KeenLogo.dds");
        byte[] source = File.ReadAllBytes(path);
        Check(BitConverter.ToUInt32(source, 80) == 0x20 && BitConverter.ToUInt32(source, 88) == 8, "installed main-menu Keen logo is palette-indexed P8 DDS");
        using var input = new MemoryStream(source);
        var image = DdsImage.Read(input, true);
        Check(image.Width == 512 && image.Height == 256 && image.Mips.Length == 10 && image.IsRawRgba, "Keen logo expands to RGBA with all ten mip levels");
        int offset = 128 + 1024, width = image.Width, height = image.Height;
        foreach (byte[] mip in image.Mips)
        {
            var expected = new byte[width * height * 4];
            for (int i = 0; i < width * height; ++i)
                Array.Copy(source, 128 + source[offset + i] * 4, expected, i * 4, 4);
            Check(mip.SequenceEqual(expected), "Keen logo mip " + width + "x" + height + " matches stock RGBA palette expansion");
            offset += width * height; width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        Check(offset == source.Length && input.Position == source.Length, "palette is consumed once before the full mip chain");
        ulong world = cache.GetTexture(path), gui = cache.GetSpriteTexture(path, out bool premultiplied);
        Check(world != 0 && gui != 0 && world != gui && premultiplied && backend.Srgb[gui], "Keen logo uses separate world and premultiplied GUI uploads");
        Check(backend.Textures[world].SequenceEqual(image.Data), "world variant preserves palette RGBA bytes");
        var expectedGui = (byte[])image.Data.Clone();
        for (int i = 0; i < expectedGui.Length; i += 4)
            for (int c = 0; c < 3; ++c)
                expectedGui[i + c] = (byte)(Math.Pow(Math.Pow(expectedGui[i + c] / 255.0, 2.2) * (expectedGui[i + 3] / 255.0), .45454545454545453) * 255);
        Check(backend.Textures[gui].SequenceEqual(expectedGui), "palette RGBA follows stock GUI alpha preprocessing");
        sprites.BeginFrame(); sprites.ViewportScale = 1;
        sprites.Process(new MyRenderMessageDrawSprite { Texture = path, DestinationRectangle = new RectangleF(700, 20, 198, 99), Color = Color.White });
        var packet = sprites.MainPackets(1000, 800).Single();
        Check(packet.Texture == gui && packet.Flags == 2 && Near(packet.Destination, 700, 20, 198, 99), "main-menu logo draw reaches the sprite batch with its GUI texture");
        using var truncated = new MemoryStream(source.Take(128 + 1023).ToArray());
        bool rejected = false;
        try { DdsImage.Read(truncated); } catch (EndOfStreamException) { rejected = true; }
        Check(rejected, "truncated palette is rejected instead of decoded as pixels");
    }
}

public class CpuPlatform : DispatchProxy
{
    private readonly IVRageRender render = DispatchProxy.Create<IVRageRender, CpuRender>();
    protected override object Invoke(MethodInfo targetMethod, object[] args)
        => targetMethod.Name == "get_Render" ? render : targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
}
public class CpuRender : DispatchProxy
{
    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        if (targetMethod.Name.StartsWith("GetMemoryBudget")) return (ulong)(1024 * 1024 * 1024);
        throw new InvalidOperationException("Unexpected platform renderer call in CPU sprite test: " + targetMethod.Name);
    }
}
public class CpuMessageRenderer : DispatchProxy
{
    protected override object Invoke(MethodInfo targetMethod, object[] args)
        => targetMethod.Name == "get_Log" ? null : throw new InvalidOperationException("Unexpected GPU renderer call in CPU sprite test: " + targetMethod.Name);
}
