# SE-Vulkan changelog

## 1.2.1 — 2026-10-20

- Keep CPU mirrors of uploaded frame/projection/object/instance data and use them for motion, attachment and spotlight dependency reads instead of reading uncached GPU mappings.
- Cache material identity by revision and main-group keys by model/fade inputs; use structural keys with precomputed hashes and avoid repeated sorting/path construction.
- Skip redundant dynamic raster-state commands, sort compact copies of the same draw-order inputs, reuse conservative light-tile lists and access large world packets in place.
- Cache indirect skybox path selection and add CPU profiling phases for owner declaration, view selection, setup and prepared appends.
- Support both LinuxCompat window-mode callback signatures, convert millihertz to hertz for the newer callback and reject unsupported signatures; extend bootstrap regressions and cover stable probe-path caching across file appearance and fresh-cache creation.
- Document paired CPU-regression benchmarks and parity checkpoints. The archived private scene improved from about 119 ms mean in 1.2.0 to about 22 ms with these changes; this is workload-specific historical evidence, not a universal performance guarantee. The sub-4.17 ms target remains open; the general lighting/material milestone and deferred parity gates retain the scope documented in LightingParity.md.

## 1.2.0 — 2026-10-20

Heavy DX11 visual parity is in place, but lower performance than previous builds is expected until further optimization passes. Full scene/material parity acceptance remains open; scoped fixture matches do not establish complete renderer equivalence.

- Correct stock material texture color spaces, neutral normal/gloss fallbacks, authored mesh precision, LCD alpha/material inputs, cube LOD filtering and sampler roles.
- Align directional/local lighting, independent light budgets, spatial selection and tile capacity, spotlight projection/cookies/cache behavior, HBAO jitter/borders, HDR targets, bloom and postprocess inputs with stock contracts.
- Recover stock environment-probe mip/precision, sky/indirect sources, staged publication, timed cadence, filtering retention and blend behavior; remove the camera-motion restart that discarded completed work.
- Preserve OLD/NEW opaque and glass material/resource identities, ordering, LOD, ownership and retained actor lifetime across native/grouped/resident submission; extend indexed auxiliary attachments, shadows and motion.
- Implement blended mesh-decal rendering and stock transparent/glass routes, independent settings, depth/culling/OIT order and published material controls.
- Repair flare query coverage/normalization, rotated geometry, cadence and solar ownership, plus additive-top billboard depth behavior.
- Expand generated stock world variants, shader metadata/adapters, native ABI helpers and native policy tests; add material/light/identity, GPU submission/probe/postprocess and matched DX11/Vulkan fixture coverage.
- Document detailed parity checkpoints, corrected quality-pinning comparisons, remaining appearance/order/lifecycle gates and optimization work.

## 1.1.0 — 2026-10-17

- Extend ABI-1 guest capabilities with feature-gated compute/geometry packets, atomic batches, extended constants/resources, mip generation, occupancy, copied point-shadow geometry and independent scene-depth views.
- Integrate shared Anomaly volume prerequisites, current catalog-write validity, before/after phases, safe image replacement, context recovery and render-size/attachment alias handling.
- Replace duplicate hardware-ray HDR lighting with conservative sun visibility in stock material lighting; repair reflected rigid transforms, instance rotation, camera/sun reconstruction, scratch synchronization/alignment, descriptors and BLAS cleanup.
- Preserve scene depth during celestial guest draws and synchronize guest attachment loads, sampled layouts, fresh image clears, HDR snapshots and NGX transfer clears; release NGX parameter maps before shutdown.
- Resolve vendor-upscaler render dimensions before frame/light/postprocess constants and correct stock SSAO coordinate scaling.
- Preserve triangle billboard snapshots, atlas UVs, normals, near-plane depth clamp and LDR/PostPP submission order; draw PostPP on the final output after temporal reconstruction and on generated presents.
- Decode legacy paletted DDS mip chains for GUI textures and render stock Shift-F11 statistics with Vulkan GPU timing plus a developer renderer page.
- Expand native policy/smoke and managed packet/GPU regressions, isolated topology/parity/camera harnesses, architecture and validation documentation.

## Changes included in 1.1.0 (implementation notes)

- Draw PostPP billboards on the final presentation image after temporal reconstruction, fixing the invisible Rich HUD terminal with DLAA/DLSS. Preserve output-resolution depth testing and soft-particle sampling, and replay the overlay on generated presents. Reproduced the missing terminal with 164 queued triangles and NGX evaluating; the corrected in-world capture shows the terminal. The pre-existing NGX shutdown access violation remains separate.
- Match Keen's unclipped billboard rasterization with queried/enabled Vulkan depth clamp, preventing Rich HUD's near-plane triangles from disappearing with small camera/float differences. Preserve LDR/PostPP submission order so backgrounds cannot cover later text or cursor layers. GPU near-plane and production transient-billboard regressions pass on RTX 5070 Ti with validation enabled; live F2 verification remains separate.
- Decode legacy P8 DDS palettes before GUI alpha preprocessing, restoring the main-menu Keen logo on Vulkan. Cover the installed asset's full mip chain, sprite packet and GPU pixels with regressions.
- Preserve Rich HUD triangle billboard snapshots, final atlas UVs and normals on Vulkan, and degenerate the unused quad triangle like Keen's renderer. Add focused billboard packet regressions; live F2 menu verification remains pending.
- Resolve DLSS/FSR render dimensions before building frame, light-tile and postprocess constants, fixing displaced lighting/color when temporal upscaling runs below DLAA resolution. Keep stock SSAO coordinate scaling at unity for exact-sized Vulkan targets. Add native mode/rounding and managed frame-constant regressions; live game visual verification remains separate.
- Replace the hardware ray bridge's duplicate HDR light addition with conservative sun visibility consumed by stock material lighting. Correct reflected rigid transforms, instance rotations, camera reconstruction, sun direction, scratch reuse/alignment, frame-owned descriptors and BLAS cleanup. Add real hardware shadow, rotation and budget-fallback regressions.
- Preserve scene depth across Anomaly celestial guest draws, disable depth writes for their read-only attachment, match the sampled-depth layout, and synchronize guest attachment loads. Add a hardware regression that compares downstream depth bits and checks core/synchronization validation.
- Synchronize transfer clears performed by NGX/DLSS evaluation and release NGX parameter maps before shutting down its device instance.
- Draw the base game's Shift-F11 timing HUD through Vulkan, retaining stock CPU/simulation/network collectors and using Vulkan GPU timestamps in place of DX11 GPU-load statistics. Add a Vulkan renderer page to developer-mode cycling. Live game verification remains pending.

## 1.0.1 — 2026-10-14

- Register an independently retried renderer-neutral Status page through the shared Anomaly terminal.
- Expose Show Status, Show Profiler, and Write report to log from that diagnostic page.

## 1.0.0 — previous release

Release SE-Vulkan 1.0.0 native Vulkan renderer

Version: 1.0.0 (first published release).

Changelog:
- Add a native Vulkan 1.4 renderer for Space Engineers 1.210.014. Windows covers Pulsar Legacy/net48 and Interim/net10. Native Linux uses Interim/net10.
- Select the renderer from Display, keep DX11 as the default, and recover to DX11 when assets, hooks, or incompatible plugins fail closed. A failed Vulkan launch can retry on the next start.
- Render stock materials and depth, lighting, skinning, and instancing, then asteroid clipmaps, planets, atmosphere, grass, transparency, weighted OIT, billboards, and GPU particles.
- Add probes, cascaded and spotlight shadows, SSAO/HBAO, exposure, bloom, tone mapping, FXAA, and dynamic resolution.
- Bundle libmpv and FFmpeg for stock intro and menu playback.
- Replay Anomaly guest fullscreen packets and catalog images, and expose display services for DLSS, FSR, frame generation, HDR, and hardware ray queries.
- Full visual parity, broad gameplay acceptance, and live Linux game verification remain pending.

Co-authored-by: Cursor <cursoragent@cursor.com>
