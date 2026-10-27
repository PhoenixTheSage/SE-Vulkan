using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Rendering.Content;
using ClientPlugin.Rendering.Interop;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Rendering;

/// <summary>Copies sprite messages into owner-thread packets; never keeps pooled messages.</summary>
internal sealed partial class VulkanSprites
{
    private sealed class FontEntry { public MyFont Font; public Color Mask; public bool Premultiplied; public readonly Dictionary<int, string> BitmapPaths = new(); }
    private readonly struct Packet
    {
        public readonly NativeSprite Sprite;
        public readonly bool IgnoreBounds;
        public Packet(NativeSprite sprite, bool ignoreBounds) { Sprite = sprite; IgnoreBounds = ignoreBounds; }
    }
    private sealed class Batch { public readonly List<Packet> Sprites = new(); public readonly Stack<Rectangle> Clips = new(); }
    private readonly VulkanTextureCache textures;
    private readonly Dictionary<int, FontEntry> fonts = new();
    private readonly Dictionary<string, Batch> targets = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private bool warningLimitReported;
    // Frame split (milestone 3): messages and the renderer's stats overlay fill main during Apply; LatchMain hands it
    // to Build as buildMain (with the viewport scale), which MainPacketsInto blits. MainPackets reads the Apply batch
    // (tests and direct callers that run no frame).
    private Batch main = new(), buildMain = new();
    private float buildViewportScale = 1;
    private int? debugFont;
    public int FontCount => fonts.Count;
    public int SpriteCount => main.Sprites.Count;
    public float ViewportScale = 1;
    public double DrawTimeSeconds;
    public event Action<string> Warning;
    public VulkanSprites(VulkanTextureCache textures) { this.textures = textures; }
    public void BeginFrame() { main.Sprites.Clear(); main.Clips.Clear(); }
    public void LatchMain() { (main, buildMain) = (buildMain, main); buildViewportScale = ViewportScale; }
    // Main-batch placement shared by MainPackets and the plugin's blittable MainPacketsInto
    // (VulkanSprites.Blit.cs): transformed destination and origin, and the clip rectangle.
    struct MainPlacement {public float X,Y,Width,Height,OriginX,OriginY;public int ClipX,ClipY,ClipWidth,ClipHeight;}
    struct MainViewport {public bool ValidScale;public float Scale,OffsetX,OffsetY;}
    MainViewport BeginMain(int width, int height, float viewportScale)
    {
        bool validScale = !float.IsNaN(viewportScale) && !float.IsInfinity(viewportScale) && viewportScale >= 0;
        if (!validScale) Warn("Invalid main sprite viewport scale: " + viewportScale);
        // MyRender11.ScaleMainViewport centers a scaled bounded viewport. Its
        // IgnoreBounds batches use the full viewport; explicit scissors remain
        // active in either viewport (MySpritesManager.Render).
        return new MainViewport { ValidScale = validScale, Scale = viewportScale, OffsetX = (width - width * viewportScale) * .5f, OffsetY = (height - height * viewportScale) * .5f };
    }
    bool PlaceMain(in MainViewport viewport, bool ignoreBounds, in NativeSprite sprite, int width, int height, out MainPlacement placed)
    {
        placed = default;
        if (!ignoreBounds && (!viewport.ValidScale || viewport.Scale == 0)) return false;
        float scale = ignoreBounds ? 1 : viewport.Scale;
        float x = ignoreBounds ? 0 : viewport.OffsetX, y = ignoreBounds ? 0 : viewport.OffsetY;
        placed.X = sprite.Destination[0] * scale + x; placed.Y = sprite.Destination[1] * scale + y;
        placed.Width = sprite.Destination[2] * scale; placed.Height = sprite.Destination[3] * scale;
        placed.OriginX = sprite.Origin[0] * scale; placed.OriginY = sprite.Origin[1] * scale;
        Rectangle bounds = new Rectangle(0, 0, width, height);
        if (!ignoreBounds)
        {
            // Raster samples at pixel centers. Match the floating viewport
            // clip when emulating it with a full native viewport/scissor.
            int left = (int)Math.Ceiling(x - .5f), top = (int)Math.Ceiling(y - .5f);
            int right = (int)Math.Ceiling(x + width * scale - .5f), bottom = (int)Math.Ceiling(y + height * scale - .5f);
            bounds = Rectangle.Intersect(bounds, new Rectangle(left, top, right - left, bottom - top));
        }
        if (sprite.Clip[2] >= 0)
        {
            // Stock truncates transformed left/top and right/bottom, rather
            // than truncating transformed dimensions independently.
            float left = sprite.Clip[0] * scale + x, top = sprite.Clip[1] * scale + y;
            int l = (int)left, t = (int)top, r = (int)(left + sprite.Clip[2] * scale), b = (int)(top + sprite.Clip[3] * scale);
            bounds = Rectangle.Intersect(bounds, new Rectangle(l, t, r - l, b - t));
        }
        placed.ClipX = bounds.X; placed.ClipY = bounds.Y; placed.ClipWidth = Math.Max(0, bounds.Width); placed.ClipHeight = Math.Max(0, bounds.Height);
        return true;
    }
    public NativeSprite[] MainPackets(int width, int height)
    {
        if (width <= 0 || height <= 0) return Array.Empty<NativeSprite>();
        var result = new List<NativeSprite>(main.Sprites.Count);
        var viewport = BeginMain(width, height, ViewportScale);
        foreach (var packet in main.Sprites)
        {
            if (!PlaceMain(viewport, packet.IgnoreBounds, packet.Sprite, width, height, out var placed)) continue;
            var copy = Copy(packet.Sprite);
            copy.Destination[0] = placed.X; copy.Destination[1] = placed.Y; copy.Destination[2] = placed.Width; copy.Destination[3] = placed.Height;
            copy.Origin[0] = placed.OriginX; copy.Origin[1] = placed.OriginY;
            copy.Clip = new[] { placed.ClipX, placed.ClipY, placed.ClipWidth, placed.ClipHeight };
            result.Add(copy);
        }
        return result.ToArray();
    }
    public NativeSprite[] ConsumeTarget(string target)
    {
        if (!targets.TryGetValue(target, out var batch)) return Array.Empty<NativeSprite>();
        var copy = new NativeSprite[batch.Sprites.Count];
        for (int i = 0; i < copy.Length; ++i) copy[i] = Copy(batch.Sprites[i].Sprite);
        batch.Sprites.Clear(); batch.Clips.Clear(); return copy;
    }
    // Font definitions belong to the device, not the world. The game does not
    // resend CreateFont on UnloadData; atlas textures reload through the cache.
    public void Clear() { BeginFrame(); buildMain.Sprites.Clear(); buildMain.Clips.Clear(); targets.Clear(); }

    // Renderer-owned overlays use the same font atlas as Keen's debug text,
    // but go directly into the main Vulkan batch instead of DEBUG_TARGET.
    public void DrawDebugText(string text, Vector2 position, float scale = .6f, bool rightAligned = false)
    {
        if (!debugFont.HasValue || !fonts.TryGetValue(debugFont.Value, out var entry)) return;
        if (rightAligned) position.X -= entry.Font.MeasureString(text, scale).X;
        AddText(main, new MyRenderMessageDrawString
        {
            FontIndex = debugFont.Value, Text = text, ScreenCoord = position,
            ScreenScale = scale, ScreenMaxWidth = float.PositiveInfinity,
            ColorMask = Color.Yellow, IgnoreBounds = true
        }, 0, MyRenderTextAlignmentEnum.Align_Left);
    }

    public bool Process(MyRenderMessageBase message)
    {
        if (message is MyRenderMessageCreateFont font)
        {
            var entry = new FontEntry { Font = new MyFont(font.FontPath), Mask = font.ColorMask ?? Color.White };
            foreach (var bitmap in entry.Font.BitmapInfoById.Values)
            {
                textures.GetSpriteTexture(Path.Combine(entry.Font.FontDirectory, bitmap.strFilename), out bool rawRgba);
                entry.Premultiplied |= rawRgba || bitmap.strFilename.Contains("FontDataPA");
            }
            fonts[font.FontId] = entry;
            if (font.IsDebugFont) debugFont = font.FontId;
            return true;
        }
        if (message is MyRenderMessageSpriteMainViewportScale viewport) { ViewportScale = viewport.ScaleFactor; return true; }
        if (!(message is MySpriteDrawRenderMessage sprite)) return false;
        var batch = GetBatch(sprite.TargetTexture);
        switch (message)
        {
            case MyRenderMessageSpriteScissorPush push:
                var clip = push.ScreenRectangle;
                if (batch.Clips.Count > 0) clip = Rectangle.Intersect(clip, batch.Clips.Peek());
                batch.Clips.Push(clip); return true;
            case MyRenderMessageSpriteScissorPop:
                if (batch.Clips.Count > 0) batch.Clips.Pop(); else Warn("Unbalanced sprite scissor pop.");
                return true;
            case MyRenderMessageDrawSprite draw:
                Add(batch, draw.Texture, draw.DestinationRectangle, draw.SourceRectangle, draw.Color,
                    draw.Rotation + draw.RotationSpeed * (float)DrawTimeSeconds,
                    new Vector2(draw.DestinationRectangle.Width, draw.DestinationRectangle.Height) * .5f, draw.IgnoreBounds);
                if (draw.MaskTexture != null) Warn("Separate sprite-mask textures are pending: " + draw.MaskTexture);
                return true;
            case MyRenderMessageDrawSpriteExt draw:
                Add(batch, draw.Texture, draw.DestinationRectangle, draw.SourceRectangle, draw.Color,
                    (float)Math.Atan2(draw.RightVector.Y, draw.RightVector.X),
                    draw.Origin - new Vector2(draw.DestinationRectangle.X, draw.DestinationRectangle.Y), draw.IgnoreBounds);
                if (draw.MaskTexture != null) Warn("Separate sprite-mask textures are pending: " + draw.MaskTexture);
                return true;
            case MyRenderMessageDrawSpriteAtlas atlas:
                var half = atlas.HalfSize * atlas.Scale;
                var rect = new RectangleF(atlas.Position.X * atlas.Scale.X - half.X, atlas.Position.Y * atlas.Scale.Y - half.Y, half.X * 2, half.Y * 2);
                Add(batch, atlas.Texture, rect, null, atlas.Color, (float)Math.Atan2(atlas.RightVector.Y, atlas.RightVector.X), half,
                    atlas.IgnoreBounds, new[] { atlas.TextureOffset.X, atlas.TextureOffset.Y, atlas.TextureSize.X, atlas.TextureSize.Y });
                return true;
            case MyRenderMessageDrawStringAligned text: AddText(batch, text, text.TextureWidthInPx, text.Alignment); return true;
            case MyRenderMessageDrawString text: AddText(batch, text, 0, MyRenderTextAlignmentEnum.Align_Left); return true;
            default: return false;
        }
    }

    private Batch GetBatch(string target)
    {
        if (target == null) return main;
        if (!targets.TryGetValue(target, out var batch)) targets.Add(target, batch = new Batch());
        return batch;
    }

    private void AddText(Batch batch, MyRenderMessageDrawString draw, int textureWidth, MyRenderTextAlignmentEnum alignment)
    {
        if (!fonts.TryGetValue(draw.FontIndex, out var entry)) { Warn("Font " + draw.FontIndex + " has not been created."); return; }
        if (string.IsNullOrEmpty(draw.Text)) return;
        var font = entry.Font;
        var scale = draw.ScreenScale * (144f / 185f);
        var color = draw.ColorMask * entry.Mask;
        var lines = draw.Text.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; ++lineIndex)
        {
            var line = lines[lineIndex];
            var lineWidth = font.MeasureString(line, draw.ScreenScale).X;
            var x = draw.ScreenCoord.X;
            if (textureWidth > 0)
            {
                if (alignment == MyRenderTextAlignmentEnum.Align_Center) x += (textureWidth - lineWidth) * .5f;
                else if (alignment == MyRenderTextAlignmentEnum.Align_Right) x += textureWidth - lineWidth;
            }
            var start = x;
            var previous = '\0';
            foreach (var character in line)
            {
                if (!font.TryGetGlyphInfo(character, out var glyph)) continue;
                if (font.KernEnabled) x += font.CalcKern(previous, character) * scale;
                if (draw.ScreenMaxWidth > 0 && x - start + glyph.pxAdvanceWidth * scale > draw.ScreenMaxWidth) break;
                previous = character;
                if (glyph.pxWidth > 0 && glyph.pxHeight > 0 && font.BitmapInfoById.TryGetValue(glyph.nBitmapID, out var bitmap))
                {
                    var source = new Rectangle(glyph.pxLocX, glyph.pxLocY, glyph.pxWidth, glyph.pxHeight);
                    var destination = new RectangleF(x + glyph.pxLeftSideBearing * scale,
                        draw.ScreenCoord.Y + (glyph.pxHeightOffset + glyph.pxLeftSideBearing + 3.8333333f + lineIndex * font.LineHeight) * scale,
                        glyph.pxWidth * scale, glyph.pxHeight * scale);
                    // One path string per glyph bitmap, not per drawn glyph.
                    if (!entry.BitmapPaths.TryGetValue(glyph.nBitmapID, out var bitmapPath)) entry.BitmapPaths.Add(glyph.nBitmapID, bitmapPath = Path.Combine(font.FontDirectory, bitmap.strFilename));
                    Add(batch, bitmapPath, destination, source, color, 0, Vector2.Zero, draw.IgnoreBounds, premultiplied: entry.Premultiplied);
                }
                x += (glyph.pxAdvanceWidth + font.Spacing) * scale;
            }
        }
    }

    private void Add(Batch batch, string texture, RectangleF destination, Rectangle? source, Color color,
        float angle, Vector2 origin, bool ignoreBounds, float[] explicitUv = null, bool premultiplied = true)
    {
        var handle = textures.GetSpriteTexture(texture, out _);
        if (!string.IsNullOrEmpty(texture) && handle == 0) return; // Missing content is reported by the cache, not painted as a white substitute.
        var uv = new[] { 0f, 0f, 1f, 1f };
        if (source.HasValue)
        {
            textures.GetSize(texture, out var width, out var height);
            var r = source.Value; uv = new[] { r.X / (float)width, r.Y / (float)height, r.Width / (float)width, r.Height / (float)height };
        }
        else if (explicitUv != null)
        {
            // DrawSpriteAtlas converts normalized coordinates to integer source
            // pixels before MySpritesRenderer normalizes the rectangle again.
            textures.GetSize(texture, out var width, out var height);
            uv = new[] { (int)(explicitUv[0] * width) / (float)width, (int)(explicitUv[1] * height) / (float)height,
                (int)(explicitUv[2] * width) / (float)width, (int)(explicitUv[3] * height) / (float)height };
        }
        AddPacket(batch, handle, destination, uv, color, angle, origin, ignoreBounds, premultiplied);
    }
    internal void AddVideo(MyRenderMessageDrawVideo draw, ulong texture, int width, int height)
    {
        var destination = VideoGeometry.Destination(draw.Rectangle, width, height, draw.FitMode);
        if (destination.Width <= 0 || destination.Height <= 0) return;
        AddPacket(GetBatch(draw.TargetTexture), texture, destination, new[] { 0f, 0f, 1f, 1f }, draw.Color, 0, Vector2.Zero, draw.IgnoreBounds, true);
    }
    internal void RemoveVideoTexture(ulong texture)
    {
        // Stock resolves queued video draws after state changes. A close or
        // replacement in the same frame must not leave a missing-ID sprite.
        main.Sprites.RemoveAll(packet => packet.Sprite.Texture == texture);
        foreach (var batch in targets.Values) batch.Sprites.RemoveAll(packet => packet.Sprite.Texture == texture);
    }
    private static void AddPacket(Batch batch, ulong handle, RectangleF destination, float[] uv, Color color, float angle, Vector2 origin, bool ignoreBounds, bool premultiplied)
    {
        var scissor = batch.Clips.Count > 0 ? batch.Clips.Peek() : new Rectangle(0, 0, -1, -1);
        // Stock MySpritesRenderer packs these values into HalfVector4 before
        // viewport transformation. Preserve its subpixel glyph/atlas sampling.
        Vector4 rect = new HalfVector4(destination.Width,destination.Height,destination.X,destination.Y).ToVector4();
        Vector4 coords = new HalfVector4(uv[0],uv[1],uv[2],uv[3]).ToVector4();
        Vector4 rotation = new HalfVector4(destination.X+origin.X,destination.Y+origin.Y,(float)Math.Cos(angle),(float)Math.Sin(angle)).ToVector4();
        destination = new RectangleF(rect.Z,rect.W,rect.X,rect.Y);
        origin = new Vector2(rotation.X-destination.X,rotation.Y-destination.Y);
        angle = (float)Math.Atan2(rotation.W,rotation.Z);
        uv = new[] { coords.X,coords.Y,coords.Z,coords.W };
        batch.Sprites.Add(new Packet(new NativeSprite {
            Texture = handle, Destination = new[] { destination.X, destination.Y, destination.Width, destination.Height },
            Uv = uv, Color = NativeBackend.ColorValues(color), Angle = angle, Origin = new[] { origin.X, origin.Y },
            Clip = new[] { scissor.X, scissor.Y, scissor.Width, scissor.Height }, Flags = premultiplied ? 2u : 4u
        }, ignoreBounds));
    }
    private static NativeSprite Copy(NativeSprite sprite)
    {
        sprite.Destination = (float[])sprite.Destination.Clone(); sprite.Uv = (float[])sprite.Uv.Clone();
        sprite.Color = (float[])sprite.Color.Clone(); sprite.Origin = (float[])sprite.Origin.Clone(); sprite.Clip = (int[])sprite.Clip.Clone();
        return sprite;
    }
    private void Warn(string message)
    {
        if (warnings.Contains(message)) return;
        if (warnings.Count < 128) { warnings.Add(message); Warning?.Invoke(message); }
        else if (!warningLimitReported) { warningLimitReported = true; Warning?.Invoke("Further unique sprite warnings suppressed after 128 entries."); }
    }
}
