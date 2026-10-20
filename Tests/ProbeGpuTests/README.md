# Stock environment-probe GPU contracts

Run from the repository root with a private asset directory containing the
current native build and compiled stock shaders:

```powershell
$env:SE_VULKAN_ASSET_DIR='<private Assets directory>'
$env:VK_LAYER_PATH='<Vulkan validation layer directory>'
$env:VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
$env:VK_VALIDATION_VALIDATE_SYNC='1'
dotnet run --project Tests/ProbeGpuTests -c Release -p:RunPostBuildEvent=Never -- '<game Bin64>'
```

The suite reads actual generated probe images and checks stock level count,
packed-float precision, first complete publication, staged updates, geometry,
atmosphere, authored skybox mips, separate indirect source, BRDF sampler edges,
and nonzero prefilter skips. A split-screen diagnostic shader also exercises
the actual main-light descriptor writer with distinct published close/far
radiance, proving stock t11 selects close geometry and t17 selects far sky.
It requires enabled validation with zero errors.

For indexed-GPU forward probes, set these variables before launching dotnet:

```powershell
$env:SE_VULKAN_TEST_GPU_OPAQUE='1'
$env:SE_VULKAN_GPU_OPAQUE='1'
$env:SE_VULKAN_GPU_OPAQUE_SCENE='0'
```

The first selects extra checks; the second admits the required native device
features. Keep automatic scene registration off because this suite explicitly
registers its indexed test pipelines. Setting the device feature variable only
inside managed code after process startup is
insufficient for the native runtime's environment lookup. The optional checks
compare real indexed forward radiance in both frame slots with the captured
resident reference. They do not replace the matched live scene comparison.

Timed controls also require a large camera move to retain the current refresh
and leave both published probe cubes unchanged when the render delta is zero.
