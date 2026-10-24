# SE-Vulkan changelog

## Unreleased

- Replace the sticky startup-recovery latch: unclean exits are attributed to the renderer or the game, the main menu prompts after the second counted exit, DX11 fallback happens only for renderer-attributed crashes, and driver/plugin changes release a fallback automatically.
- Fix the crash when SE-FRS turns FSR on: FidelityFX now gets a device-proc resolver that maps KHR aliases (`vkGetBufferMemoryRequirements2KHR`) to their core 1.4 entry points and stubs the debug-utils and AMD marker calls the device never enables. `ffxQuery`/`ffxDispatch` now get the context's address, the create descriptor no longer pads before `maxRenderSize` (FFX was building zero-width resources), and depth is passed as a sampled color image rather than a depth target.
- FSR upscales the AfterTonemap image at the same point as DLSS (LDR tonemap output, or the graded FP16 image under an HDR display tenant) instead of pre-tonemap `world.hdr`, which composed untonemapped and dark. The HDR create flag follows the input, matching DX11 SE-FRS.
- Bias the world material sampler by `log2(render/display)` while DLSS or FSR renders below native, so upscaled frames keep display-resolution texture detail (fine grates no longer blur away in Balanced/Performance).

## 1.5.0 — 2026-10-24

- Add voxel geometry owners and align glass reserved inputs with DX11.
- Add native temporal jitter ownership, FSR reactive input, projection-derived FSR FOV and DLSS sharpness configuration.
- Publish tonemap inputs, grading values and the render clock; grade HDR display tenants into FP16 at the tone position.
- Replay DebugDrawLine3D and expose display state, live tearing/latency controls and HDR screenshots.
- Produce RayScene v1 frames and add a native tenant compute session with software tracing lanes, BLAS/TLAS acceleration structures and inline ray query.
- Measure tenant lists with GPU timestamps and expose completed measurements to adaptive budgets.
- Update portable ray ownership, upscaler and scene-submission fixtures; clean up late color-history fixtures after execution.

## 1.4.0 — 2026-10-23

- Include the unpublished 1.3.0 pipelined native recording and native OLD grid-cell work in this branch history.
- Capture authenticated immutable shader sources and activate transactional native owner generations for raster/postprocess, lighting, atmosphere/foliage, decals, probes, particles, shadows, geometry, OIT, glass/holo and billboards.
- Execute reserved catalog resources, binding extras, null/format semantics and fenced resource/history lifetimes; emit same-invocation velocity and attachments from admitted classic/native-cell/GPU owners.
- Pack native publications asynchronously after ordered parallel selection, defer OLD cell emission with reader/mutation joins, and reduce voxel/material/group-owner publication overhead.
- Expose async policy controls, wait-site diagnostics, strict stage interfaces, celestial probe rendering and the GBuffer velocity developer probe; preserve stock capabilities when extension interfaces fail closed.
- Expand native contract tests, production GPU owner/probe/feedback regressions, performance harness controls and next-session planning documentation.

## 1.3.0 — 2026-10-21

- Pipeline native recording: frame N records on a native worker while managed frame N+1 runs. Native calls keep their serial order (30 void calls queue with copied arguments, seven GPU-result reads answer from a launch snapshot with a verified cascade-cache prediction, every other call waits and replays the queue). Private Pertam present cadence measured 13.28 → 10.59 ms in paired runs. `SE_VULKAN_PIPELINED_RECORDING=0` restores serial recording.
- Add a native producer for static OLD cube grid cells: managed code commits cell inputs and per-LOD retained parts on change (pose-only commits for moving grids), and native code reproduces the retained-submission decision and appends the same world draws. `SE_VULKAN_NATIVE_CELLS=0` opts out.
- Order equal stock OLD sort keys by logical draw owner, then submission, instead of spatial-query order; add a submission tie-break to the final world sort branch. `SE_VULKAN_OLD_OWNER_ORDER=0` restores the previous order.
- Match GeometryStage2 forward (environment probe) parts: managed NEW-pipeline actors no longer draw non-MESH parts into probes, as stock `MyForwardRenderPass` keeps only MESH parts. Enable NativeMixedOpaque by default after the fix (`SE_VULKAN_NATIVE_MIXED_OPAQUE=0` opts out).
- Speed up native pass publication with hashed batch, retained-batch and spot-signature maps, plain keys for unhidden batches and in-place retained-row comparison; publication output is byte-identical by signature.
- Harness: frame cadence (`PresentMs`) alongside render-thread time, telemetry sampled before an asynchronous launch, per-phase native cell ownership in frozen-light lanes, and `-NativeCells`/`-PipelinedRecording` switches. Product-default lanes add `-NativeCells -NativeMixedOpaque -PipelinedRecording`.

## 1.2.2 — 2026-10-21

- Enable native pass scenes, group occlusion and shared stock depth geometry by default when prerequisite policies permit; retain managed fallback and strict checks for an explicitly forced native path.
- Parallelize native pass preparation, managed candidate planning, spotlight secondary-command recording and independent upload copies while preserving publication/submission order and joining before flush or consumption.
- Reject spotlight caster spheres wholly before the stock 0.5 m near plane in both managed and native paths; retain crossing casters and extend bounds regressions.
- Cache group identity resolution and eligibility work, add candidate-planning CPU profiling, and refine frozen-light OFF/ON/OFF reconciliation and isolated policy switches.
- Document retained-path comparisons and serial/parallel parity evidence. Historical private Pertam runs measured roughly 15-17 ms versus 21-22 ms before this step; host noise, remaining CPU costs, deferred paths and the gap to DX11 remain explicit.

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
