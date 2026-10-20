# Material texture upload contract

Run from the repository root:

```powershell
dotnet run --project Tests/MaterialTextureContract -c Release -p:RunPostBuildEvent=Never
```

This compiles the actual production DDS reader and texture cache with an upload
recorder. A three-mip RGBA DDS fixture exercises the installed DX11 Extension
sRGB versus NormalGloss linear-view contract, cache identity, unchanged source
bytes/alpha, neutral maps and disposal. It creates no graphics device.
Distinct cube face/mip bytes also exercise complete authored mip retention and
the face-major cube upload contract. The Indirect sibling tests cover probe
source selection, absent-sibling fallback and preservation of the visible cube.
All twelve checks use production code.

Pair it with GBufferGpuSmoke's stock-shader AO/emission decoding check and the
matched live captures. Upload observations do not establish visual parity.
