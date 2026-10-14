# Phase #2: asteroid rendering

The asteroid path retains the original game clipmap, CPU mesher, stitching, storage invalidation and LOD controller. Vulkan implements their actor/cell/processor interfaces and owns GPU geometry, material arrays and presentation. Phase #3 extends this path to planet streaming/spherization and environments; see [Planets.md](Planets.md).

## Scene and ownership

`VulkanVoxelProcessorProvider` supplies independent worker processors. Each copies packed `MyVertexFormatVoxelSingleData` vertices and expands local indices to uint32. Workers call no Vulkan functions. Returned native arrays transfer ownership through `MyVoxelRenderCellData`; the render owner copies and disposes them before returning from the cell update. Native packets never retain pooled game messages.

Actor binding installs the processor and emits the initial matrix event after `BindToActor`, initializing the clipmap's world-to-local camera transform. Updates receive the global camera inverse view, frustum, far distance and smooth-LOD request. Batch commits stage mesh/visibility/deletion changes until `EndBatch`, keeping old cells visible while replacements are prepared. Cell callbacks, actor IDs, visibility and main/depth suppression are preserved. Actor unload cancels the original clipmap and retires resources on the shared timeline.

LOD-scaled cell transforms become camera relative when constructing draw packets. Visible cells feed stock GBuffer, sun/local lighting, four shadow cascades and HDR composition. Stock mining notifications invalidate the original controller, generating replacement cells. Readiness callbacks remain owned by that controller; legacy `InvalidateClipmapRange` and `ClipmapsReady` switch entries are source-backed baseline no-ops.

## Stock shaders and materials

`generate-voxel-variants.py` selects eight authenticated catalog stages: TriplanarSingle and TriplanarMulti main/depth pairs. Main IDs are 20/21 and depth partners 52/53, bringing the stock material/depth count to 44. Vertices retain the stock 24-byte layout: position float3, signed material byte4, packed normal uint16x2 and signed color-shift byte4. Object constants occupy 144 bytes; the 128-entry material table occupies 30,720 bytes with a 240-byte stride. Reflection checks reject changed layouts.

Visible material triples share immutable pages of color/metal, normal/gloss and extension arrays, including both projection sides and near/far/far2 slices. Each page contains at most 42 material IDs (252 layers per role); its table retains original material indices. Color/metal and extension use sRGB; normal/gloss uses linear sampling. Tiling scales are inverted as in the original manager. Empty paths use exact vanilla generated BC7 channel patterns; missing nonempty DDS paths fail with their names.

A GPU copy pass samples source DDS textures, including mixed BCn/RGBA formats, sizes and mip counts, into **RGBA8 arrays with generated mips**. Frame preparation selects a shared 512/256/128/64-pixel tier that fits the visible page set into the working budget. It preserves material alpha rather than premultiplying it. A deterministic 64×64 noise texture supplies stock material noise. Normalization views/descriptors remain alive until upload completion.

The cache targets a **512 MiB normalized-array working budget**, evicting unpinned groups by recency. Visible groups are pinned for the frame; preloads are eviction hints. This is not a total VRAM limit: source DDS/foliage residency, staging, deferred retirement and other resources are separate. A visible set that cannot fit even at the minimum tier fails explicitly. Scene unload clears residency while retaining material definitions.

## Evidence and remaining scope

`Tests/VoxelGpuTests` checks actual stock single/triple pixels, packed geometry, GPU normalization, BCn sampling, sRGB/alpha preservation, depth/cascade partners, dithering, LOD transforms, replacements, ownership, batching and unload. `Tests/WorldPassGpuTests` guards the existing world path. Live evidence and build results are recorded in `Validation.md` and `ShaderPassCoverage.json`.

The private Creative fixture adds a deterministic 128-voxel asteroid (seed 43191), near/middle/far views, a stock spherical mining operation, saved `.vx2` reload into a replacement actor, captures and unload. Saved-storage reload is distinct from a full game restart or full-world reload.

Timed LOD crossfades remain pending: the requested transition mode is retained but replacements currently swap atomically. Page tiers respond to the working budget; user graphics-quality mapping remains pending. Full DX11 visual parity, large/modded material sets and live Linux acceptance remain separate gates. Pulsar reports these limitations and observed voxel frames/draws, cells/LODs, mesh updates, arrays, groups, selected page tiers and resident bytes once per second in Release and Debug.
