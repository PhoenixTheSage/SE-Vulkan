using System;
using ClientPlugin.Rendering.Interop;

namespace ClientPlugin.Rendering;

// Plugin-only half of VulkanSprites (the sprite test projects compile VulkanSprites.cs alone).
internal sealed partial class VulkanSprites
{
    // The latched frame's main batch (Build) written into a reused blittable buffer: no per-sprite array copies and no
    // element-wise marshaling at the native call. Same placement as MainPackets. Returns the sprite count.
    public unsafe int MainPacketsInto(ref NativeSpriteBlit[] buffer, int width, int height)
    {
        if (width <= 0 || height <= 0) return 0;
        if (buffer.Length < buildMain.Sprites.Count) buffer = new NativeSpriteBlit[Math.Max(buildMain.Sprites.Count, buffer.Length * 2)];
        var viewport = BeginMain(width, height, buildViewportScale);
        int count = 0;
        foreach (var packet in buildMain.Sprites)
        {
            var source = packet.Sprite;
            if (!PlaceMain(viewport, packet.IgnoreBounds, source, width, height, out var placed)) continue;
            ref var copy = ref buffer[count++];
            copy.Texture = source.Texture; copy.Angle = source.Angle; copy.Flags = source.Flags;
            for (int i = 0; i < 4; ++i) { copy.Uv[i] = source.Uv[i]; copy.Color[i] = source.Color[i]; }
            copy.Destination[0] = placed.X; copy.Destination[1] = placed.Y; copy.Destination[2] = placed.Width; copy.Destination[3] = placed.Height;
            copy.Origin[0] = placed.OriginX; copy.Origin[1] = placed.OriginY;
            copy.Clip[0] = placed.ClipX; copy.Clip[1] = placed.ClipY; copy.Clip[2] = placed.ClipWidth; copy.Clip[3] = placed.ClipHeight;
        }
        return count;
    }
}
