# Phase #5: probes, shadows, occlusion and postprocessing

The production owner-thread path now executes authenticated stock passes for environment probes, CSM/spotlight shadows, SSAO/HBAO, exposure, bloom, tone mapping, highlights, FXAA and chromatic/vignette processing. Dynamic resolution uses the stock CPU heuristic and completed Vulkan timestamps. Full visual parity and broad-world acceptance are separate from these implementation and fixture results.

## Frame composition and ownership

World resolution planning calls `sevk_resolve_render_resolution` after submitting the DRS fraction and before preparing frame/postprocess constants. Native NGX/FSR mode overrides and native rounding therefore determine the same dimensions used by lighting tiles, SSAO/HBAO, and world image allocation. `Screen.resolution` is the actual internal extent; `Screen.drs` stays 1 because Vulkan owns exact-sized images, and stock SSAO must not scale its reconstruction coordinates a second time. Resolution status remains observed render/timestamp data, rather than a prediction. The additive resolver export requires deploying the managed and native builds together. Native smoke and WorldPassGpuTests cover resolution planning; live DLSS visual acceptance remains separate.

Opaque material GBuffer and depth come first. Delayed bounding-box occlusion, decals, atmosphere LUT preparation, cascaded shadow copies and probe capture precede ambient/directional/local lighting. SSAO or deinterleaved HBAO feeds that lighting. Atmosphere, clouds, OIT/glass, GPU particles and HDR billboard buckets then complete HDR lighting. The stock histogram or fixed exposure, bloom pyramid, Hable tone mapping, LDR billboards, FXAA, highlight masks and chromatic/vignette stages feed scene presentation and temporal upscaling. PostPP billboards then draw at physical output resolution, after the selected scene image is composed and before HUD sprites. This keeps Rich HUD out of DLSS/FG history and prevents an earlier NGX output from bypassing the terminal. The output depth attachment is retained and snapshotted for soft-particle sampling; output-format billboard pipelines retain premultiplied blending and read-only depth testing. Extra generated presents replay the same PostPP overlay before sprites. HUD and LCD sprites retain physical output dimensions.

The native ABI copies every settings/frame/query packet. Managed code never lends pooled message arrays across submission. GPU images, descriptors and views retire against the timeline when internal dimensions or quality change. Query/status snapshots use completed frame slots without `WAIT` query flags. Explicit screenshot/probe readbacks are verification operations and may wait. Presentation resize and probe resolution replacement remain controlled synchronization points.

The stock catalog remains 20,736 variants / 1,401 unique modules. Supplemental material/depth/forward/highlight combinations now comprise **128 stages (64 pairs)**. Postprocessing initializes **128 selected stages**: 62 base exposure/bloom/tone/FXAA/SSAO/HBAO/chromatic stages, 64 exponential highlight blur kernels, and two depth-aware shadow Gaussian blur kernels. The two counts describe different assets.

## Environment probes

Six source face orientations hold a capture origin across the cycle. Near/far RGBA16F cubes receive stock forward Standard/AlphaMasked/voxel shaders. The installed version's `MyTransparentRendering.RenderForward` captures the nearest atmosphere; it does not capture glass, billboards, particles, cloud shells or foliage. HQ AtmosphereEnv consumes the cached optical-depth LUT and ambient intensity/desaturation settings. All faces must complete before publication; GGX roughness mips and stock blend passes update complete final cubes. Timeout pauses capture while blending continues; teleport, unload and resolution/settings changes invalidate publication. The installed RG16F ambient BRDF texture retains both half-float channels. Directional/ambient lighting, glass, billboard and particle reflection descriptors consume published probes.

`VulkanProbeAmbient` ports `MyEnvironmentProbe.GatherLightAmbient` including range, falloff, cone and ambient clamps. Its results augment forward ambient during capture. The skybox matrix preserves vanilla's exceptional nontransposed CPU write under the Vulkan shader build's row-major convention; a two-direction colored-cubemap GPU fixture covers it. Capture uses one face per submitted world frame and filters the complete cube after face six. Vanilla's finer distribution of prefilter work and alternating blend cadence remain a performance/scheduling refinement.

## Shadows and occlusion

Quality selects 512/1024/2048/4096 CSM maps and 256/512/1024/2048 reflector maps. CSM supports up to eight cascades, frustum-sphere fitting, camera-relative texel stabilization, stock distance/spread/back extrusion, bias, frozen cameras/cascades, PCF, cascade blending and optional depth-aware Gaussian blur. Offscreen opaque/voxel casters remain available within probe/shadow coverage. Spotlight cookies and the stock shadowed spotlight shader consume copied projections; sixteen nearest enabled spotlight shadows bound the budget. Set/ClearLightShadowIgnore map entity identities to per-light native draw exclusions.

Stock group box geometry supplies asynchronous visibility queries. Near-plane and camera-containing boxes stay visible. Camera, dimensions, DRS fraction, actor transforms, render-message revisions, texture residency and voxel depth revisions invalidate delayed hidden results. Native results are accepted once, after their slot completes. The visible-actor output remains CPU/frustum based for simulation contracts. Ignore/disable query settings preserve conservative visibility.

Usage-based cascade cadence, small-object cascade pruning, the full stock culling hierarchy, tiled GPU light culling beyond the existing nearest-64 local-light budget and planet-scale performance remain pending. UpdateShadowSettings stays explicitly partial in the 147-message registry.

## Postprocessing, highlights and DRS

All stock processed postprocess layout fields are reflected into frame constants, including physical exposure, grain, saturation, contrast, sepia and filters. Eye adaptation uses histogram compute plus ping-pong exposure; fixed exposure remains selectable. Bloom supports high/low quality, anti-flicker, dirt and down/up pyramids. SSAO copies the source sample kernels. HBAO uses linearized/deinterleaved depth, reconstructed or GBuffer normals, sixteen coarse slices, reinterleave and all bilateral radius/profile permutations. HBAO jitter is deterministic rather than the game's initialization random sequence.

Highlight messages retain section/subpart/instance selection, pulse/sepia, color and removal. Stock highlight material variants share rigid, bone and instance streams, with explicit interior/overlap masks and all 32 exponential blur radii. Color-mask composition emulates the stock stencil exclusions. Like stock `MyHighlight.Draw`, blur uses the maximum requested thickness for the frame.

LDR billboards blend into an sRGB view of the encoded tone target before FXAA; PostPP billboards blend after chromatic processing. Read-only reversed depth and premultiplied alpha are preserved. Custom projection matrices are copied with the source viewport mapping. Vulkan requires matched vertex outputs for lit LDR pixel shaders; their stock lit vertex partner supplies those outputs rather than relying on DX11's permissive undefined varying behavior.

DRS ports `MyDRSHeuristicProxy`, using completed Vulkan GPU timings and device presets. Internal world targets scale independently from the physical HUD/presentation extent. Upscale currently uses bilinear sampling; external temporal/vendor upscaler plugins remain subsequent ports. No per-frame config writes occur.

## Stereo applicability

The stock desktop profile is mono. Native per-eye rendering and VR compositor submission are unavailable and reported as such. Profiles requesting stereo through the game's default device settings fall back before activation; an unsupported late device request triggers explicit recovery. A native VR host and hardware acceptance are required before claiming stereo support.

## Evidence and remaining acceptance

`Tests/PostprocessGpuTests` verifies actual exposure/bloom/FXAA/chromatic/AO pixels, all HBAO and highlight blur permutations, internal resolution changes, timestamps, CSM quality, reflector cookies/depth replay/exclusions, overlap removal, delayed visibility and LDR/PostPP/custom projection pixels. `Tests/ProbeGpuTests` verifies source BRDF upload, six-face publication, actual near/far geometry, every mip, replacement/resize, atmosphere pixels and timeout. Core and synchronization validation must both remain at zero. The fixture JSON records observed counters and its scope; compilation alone is never visual verification.

Controlled live Creative captures exercise stock settings, highlights, SSAO/HBAO, LDR/PostPP, HUD/LCD, actor animation and renderer selection. Matched DX11 views have been reviewed: skybox orientation is corrected, while world/LCD brightness still differs. Live Interim/Linux/VR, large-world performance and the remaining refinements above are tracked separately. See `Validation.md` for the final run evidence.
