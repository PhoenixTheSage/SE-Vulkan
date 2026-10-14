# Phase #3: planet environments

Planet terrain, main-view atmosphere, cloud shells, fog, grass/pebbles and wind now execute through the native Vulkan renderer. The original simulation owns planet generation, streaming, storage, meshing, stitching, mining and weather updates. Compilation coverage remains **20,736 variants / 1,401 unique SPIR-V modules**; this phase integrates nine existing catalog stages rather than converting another shader suite.

## Terrain and material residency

`VulkanVoxels` supplies the original planet clipmap with the same worker processor and atomic cell batches used for asteroids. Planet radius and camera-relative center populate the stock voxel object constants. `VertexTemplateBase.hlsli` performs distant spherization above 30 km, with its original maximum 0.8 blend. Main rendering uses the stock infinite reversed-depth projection; voxel visibility uses the camera's far projection while clipmap updates retain its original main frustum and far distance.

Visible material triples share pages containing up to 42 material IDs, each with six layers per texture role. The renderer chooses a 512, 256, 128 or 64-pixel page tier to keep the visible normalized arrays within 512 MiB. This avoids duplicating the same planetary materials in many triple-specific arrays. DDS sources, foliage arrays, staging and deferred retirement remain outside that working budget. User graphics-quality mapping and timed voxel LOD crossfades remain pending.

## Atmosphere and clouds

`VulkanEnvironment` copies atmosphere/cloud messages and texture lists before pooled messages are returned. It normalizes atmosphere defaults using the original renderer's rules, computes camera-relative constants, applies main-view planet intensity settings and orders distant atmosphere/cloud draws first. Cloud rotation follows gameplay-frame deltas; shell scaling, altitude alpha and creation/removal fades follow the stock contracts.

`environment.inc` reflects and binds the authenticated atmosphere precompute CS, high-quality atmosphere PS and cloud VS/PS. Each atmosphere owns a 512×128 RG32-float optical-depth LUT. Changes to scattering, radii, height or scale trigger compute dispatch; unchanged parameters reuse the LUT. Explicit compute-write/fragment-read barriers guard sampling. Atmosphere uses the stock blend equation. Clouds alpha blend against read-only reversed scene depth before HDR tone mapping, so terrain occludes clouds.

The additive ABI-1 environment packet is 192 bytes; its 144-byte constants are copied before the native call returns. Native removal kind 7 retires the atmosphere LUT through the existing completion timeline. Cloud geometry and DDS maps use the shared model/texture caches and unload with the scene.

## Foliage and weather

`VulkanFoliage` reproduces the stock triangle-area/material-weight point distribution and Park–Miller sequence on the CPU once per visible cell replacement. Persistent 16-byte native points feed the stock foliage VS and grass/rock GS/PS. This keeps geometry expansion and material shading on the GPU without introducing a transform-feedback extension dependency. Foliage entries are copied, normal directions follow actor rotation, and material/density/mesh changes invalidate cached points. Mining replaces the terrain and its associated foliage. Trees use the existing generic actor path.

Stock foliage size/variation and texture-count constants occupy the 4096-byte material table. Color/alpha and normal/gloss maps become separate array textures. Grass draw distance, geometry scaling, wind vector and render time populate the stock Frame block. Point resources retire when their cell disappears or leaves the retention distance; scene unload removes all point streams and foliage arrays.

Weather remains simulated by `MySectorWeatherComponent`. Its fog updates reach the stock world, sky, cloud and atmosphere shaders, and its wind settings reach foliage. Rain/snow and weather effects now use the Phase #4 stock GPU-particle bridge; planet-weather visual acceptance remains separate. Partial graphics, planet-probe intensity, postprocess and actor-LOD settings remain Pending in the message registry even when their applicable environment fields are copied.

## Source contracts

The installed **1.210.014** source and its base-game graph were inspected for `MyRender11.ProcessMessageInternal`, `MyRender11.SetupCameraMatricesInternal`, `MyRenderVoxelActor`, `MyAtmosphereRenderer.FillConstants`, `MyCloudRenderer.Render`, `MyFoliageGeneratingPass`, `FoliageStreaming.hlsl`, the grass/rock shaders and `MySectorWeatherComponent`. Generated variant IDs are in `EnvironmentShaderVariants.cs`; `generate-environment-variants.py` selects exact source/profile/define identities from the authenticated catalog. No game HLSL source is redistributed.

The engine now requires and explicitly enables `geometryShader` and `shaderStorageImageExtendedFormats` in addition to its six previous feature bits. Preflight tests reject either missing feature before activation. Optional host-image-copy/local-read features remain unused.

## Acceptance and practical limits

`Tests/EnvironmentGpuTests` passes 35 checks for stock atmosphere pixels, LUT reuse/invalidation, cloud depth/blending, fog, grass/pebble expansion, wind, alpha cutouts, deterministic point generation and unload, with zero core/synchronization validation errors. `Tests/VoxelGpuTests` passes 42 checks including rasterized distant spherization, copied foliage definitions and bounded shared material pages. World, bootstrap and protocol regression results are retained separately.

Run `Tests/CreativeFixtureHarness/Run-Isolated.ps1 -Planets -DurationSeconds 480` with the installed Bin64/Pulsar paths. It creates an isolated 19-km EarthLike planet, views it from orbit, approaches the surface, shows grass/trees, changes real weather fog, mines an 8-metre region, saves storage, changes atmospheric height and unloads. It does not modify normal user saves. A fresh `-Backend Dx11` run creates the same seeded scene for comparison; the initial world snapshot predates planet creation.

The recorded Windows Release-native run `30c8ac126b3f46d2acbc440301d0c58b` delivered all six planet captures, observed native terrain/environment/foliage activity and ended with zero validation errors and zero voxel/LUT residency. Main-view geometry and environment activity have evidence; full visual parity does not. Fixed exposure currently washes out terrain and clouds. Adaptive exposure, environment probes/ambient atmosphere, all atmosphere quality/MSAA paths, weather particles, broad mod/material and multiple-planet acceptance, full-world planet reload and live Linux acceptance remain open. See `Validation.md` for build fingerprints and comparison results.
