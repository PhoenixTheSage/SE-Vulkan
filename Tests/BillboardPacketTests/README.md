# Billboard packet regression

Run from the repository root:

```powershell
dotnet run --project Tests/BillboardPacketTests -c Release -- 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64'
```

Runs the production `VulkanBillboards` converter against installed game billboard
types and a recording backend. Covers pooled triangle snapshots, persistent HUD
submission, final font atlas UVs, camera-relative vertices, the degenerate unused
quad triangle, supplied normals, PostPP routing, ordinary quad preservation, and
32 transient HUD triangles retaining their submission order despite varying
corner distances.
The scene and texture cache are test doubles; this does not verify GPU rendering.

For visible acceptance, load a world with Rich HUD Master and Anomaly using
SE-Vulkan, press F2, and verify that the terminal background, text and cursor render
and that selecting Anomaly pages works. Repeat after closing and reopening F2.
