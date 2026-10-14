# Production sprite packet checks

Run `dotnet run --project Tests/SpritePacketTests/SpritePacketTests.csproj -c Release -- <game Bin64>`.
The suite links the actual `VulkanSprites`, texture cache, and DDS loader, reads
the installed menu overlay and FontDataPA descriptor/atlas, and records uploads
without creating a graphics device. Its 27 checks cover message/snapshot ownership,
rotation, explicit clipping, centered viewport scaling, IgnoreBounds, atlas UV
quantization, offscreen targets, font definitions surviving scene unload, atlas
reload, stock half precision, all eleven installed BC7 font mips retaining their
source bytes, GUI-only texture readiness queries, premultiplied metadata, raw GUI preprocessing isolated from world
textures, and bounded diagnostics. GPU blending/color-space validation is separate.

Viewport/scissor behavior follows `MyRender11.ScaleMainViewport` and
`MySpritesManager.Render` in the installed game's decompiled source. Atlas source
coordinates follow `MySpritesRenderer.ProcessDrawMessage`. The glyph position
check deliberately preserves `MyRenderFont.DrawCharGlyph`'s left-bearing and
vertical baseline expression.

Separate sprite mask textures and complete text ellipsis/multiline alignment are
pending visual systems. These packet tests do not establish full in-game UI parity.
