# Installed font edge GPU check

Run `dotnet run --project Tests/SpriteGpuTests/SpriteGpuTests.csproj -c Release -p:RunPostBuildEvent=Never -- <game Bin64> <private output directory>`.
Set the private Khronos layer path and synchronization-validation enable flag,
and build/deploy current native assets first. Python with Pillow is required for
an independent reference decode when the installed atlas is BC7.

The test links production sprite packets, uses the production cache/native bridge,
loads the real white FontDataPA font and DDS atlas, selects one antialiased A glyph
edge, and samples that exact texel through Vulkan. It also preserves the actual
eleven-level DDS chain and compares a fourfold minified sample against an
independently decoded mip-two edge. It checks each channel against
the stock GUI preprocessing/premultiplied-tint formula over a nonblack background
with a midtone tint. Main targets use an sRGB attachment for linear blending.
Raw RGBA atlases receive the stock gamma-2.2 premultiply table; compressed atlases
retain their source bytes. The filename/raw-format rule matches MyRenderFont.

`font-edge.json` records source/prepared bytes and expected/actual base and mip pixels;
`font-edge.png` and `font-minified.png` show the actual native outputs. This proves the selected asset and
shader formula, while a complete live DX11/Vulkan UI image comparison remains
separate acceptance work.
