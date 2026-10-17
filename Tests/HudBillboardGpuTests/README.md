# Rich HUD billboard GPU regression

Run from the repository root with the Khronos validation layer available:

```powershell
$env:SE_VULKAN_ASSET_DIR = Join-Path $PWD Assets
$env:VK_LAYER_PATH = Join-Path $PWD Native/build/validation/Bin
dotnet run --project Tests/HudBillboardGpuTests -c Release -p:RunPostBuildEvent=Never -- 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64'
```

Uses the real Vulkan backend, installed billboard shaders and PostPP pass. The
camera uses Keen's complementary infinite projection with a 0.05 m near plane,
matching Rich HUD Master's pixel-to-world plane. Tests triangles behind, exactly
on, and one micrometre in front of that plane. Also submits a real transient
`MyTriangleBillboard` through production `VulkanScene.DrawBillboards`, material
lookup and texture cache. Each case must produce more than 200 red pixels, with
validation enabled and zero errors. Writes readback and result files under
`TestResults/HudBillboardGpuTests`.

Before depth clamping, the slightly closer triangle produced zero pixels; after
the fix it produces 338 on RTX 5070 Ti. This verifies the drawing contract, not
the complete F2 menu interaction in a running world.

An optional second argument is the directory containing `nvngx_dlss.dll`.
That lane claims the native `se-dlss` upscale slot and verifies that DLAA and
Quality retain the same output-resolution HUD pixel count, and that closing
the HUD leaves zero red pixels in reconstruction history. On the current
private-driver NGX path these image assertions pass, but process teardown
reproduces a pre-existing native access violation. Treat its nonzero exit as
a failure, not a complete test pass. The default non-NGX lane exits cleanly.
