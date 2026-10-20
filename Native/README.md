# Native Vulkan engine

`scripts/build-native.ps1 -Configuration Release -Test -Deploy` builds the x64
library, validates shaders, runs native tests, and places local runtime assets
under `Assets`. CMake 3.24+, Visual Studio C++20 Build Tools, Git, Python, and a
Vulkan driver are required. A Vulkan SDK installation is unnecessary.

Linux builds use `cmake -S Native -B Native/build -DCMAKE_BUILD_TYPE=Release`, then
`cmake --build Native/build --target se_vulkan_native sevk_smoke` and
`ctest --test-dir Native/build --output-on-failure`. Package only this project's
artifacts with `cmake --install Native/build --component SEVK --prefix Native/package`.
The CI includes Ubuntu and Windows Debug/Release builds. A GPU test returns skip
code 77 when the host has no Vulkan 1.4 device; this is not a rendering pass.

On Windows, `scripts/build-native.ps1 -CrossLinux -Configuration Release -Deploy`
builds an ELF64 library using SHA-verified Zig 0.14.1 and the already-built host
shader tools. `-Configuration Debug` builds a separate Linux debug tree. These
are cross-compilation checks; Linux runtime, X11, and Wayland tests still require
a Linux host. The target is x86-64 glibc 2.31.

`scripts/build-linux-tools.ps1 -Deploy` packages pinned Linux DXC and cross-built
SPIRV-Tools under `Assets/Tools/linux-x64`, retaining `bin`/`lib` and licenses.
Upstream DXC requires glibc 2.34. These optional compilation tools are separate
from renderer startup. `scripts/verify-native-assets.py` checks packaged PE/ELF
architecture, all native exports and local hashes.

The ABI is specified in `include/se_vulkan.h`. Managed callers own the render
thread, serialize API calls, and submit resource uploads before `begin_frame`.
Draw data is copied. Texture and mesh destruction is deferred against a GPU
timeline. UI sprites use pixel coordinates and normalized `(x,y,width,height)`
UV rectangles; negative scissor width disables clipping. Matrices are row major,
camera-relative, with DirectX clip Z in `[0,1]`. Main rendering uses reversed Z,
so the bridge converts forward game depth to `W-Z`. Shadow matrices use ordinary
forward depth. Negative Vulkan viewports handle Y; do not also invert matrices.

The renderer uses Vulkan 1.4 core, dynamic rendering, synchronization2, timeline
semaphores, VMA device memory, staging uploads, binary acquire/present semaphores,
shader reflection, and a validated driver-specific pipeline cache. It supports
RGBA/BGRA and BC1–BC7 mip-chain uploads, textured indexed geometry, alpha cutouts,
ColorMetal/AddMaps paint and emissivity, normal maps, dithered previews, point/spot
lights with source falloff/diffuse metadata, four 1024-pixel depth shadow cascades with PCF, skybox geometry, fixed
exposure ACES tone mapping, alpha and additive geometry/sprites, sprite
scissors/rotation, and sampled sRGB offscreen LCDs. Geometry uses premultiplied
blend factors; AdditiveTop (flag 2048) bypasses scene depth, while ordinary
additive geometry respects it. Sprite ABI flag zero retains straight-alpha
blending; flag 2 accepts premultiplied texture input and flag 4 converts straight
input to premultiplied output. Both stock modes multiply linear tint RGB by tint
alpha and blend with ONE. Main presentation/offscreen attachments and LCDs use
sRGB formats so blending happens in linear space and display encoding happens
once. Surfaces without RGBA8/BGRA8 sRGB support are rejected with a diagnostic.
The original texture upload export remains a one-level operation; the mip-chain
export checks exact tightly packed level sizes and exposes all levels to sampling.
Sprites use stock linear clamp sampling; world materials use wrap sampling.
The offscreen aspect export keeps the physical attachment extent while using
the game's logical viewport for sprite coordinates and clipping. Scissor
endpoints scale independently, and rotation occurs before viewport scaling.
LCD sprite pipelines preserve destination alpha, matching stock offscreen
rendering: material ColorMetal alpha must not become sprite coverage/metalness.
The additive `sevk_render_texture_mips` export honors generated LCD mip requests;
legacy exports retain their one-level behavior. Mips filter in the sRGB image's
linear color space and are published together after transfer barriers. A
separate base-mip attachment view is retired with the owning texture.
The stock world-material path uses authored tangent bases and installed shader
variants; the legacy fallback uses derivative normals. Current lighting/PBR
evidence and remaining equality work are tracked in `Docs/LightingParity.md`.
The main lighting target follows HqTarget: normal quality uses R11G11B10 and
high quality uses RGBA16F. Both pipeline formats are owned by the context;
quality changes wait for prior work before switching/recreating targets.
HBAO per-pass jitter comes from the managed stock MyRandom algorithm, uploaded
as sixteen validated/copied float4 values. Production uses its time-seeded
default; deterministic seeds are used only by explicit private diagnostics.
Stock HBAO's negative-depth border is emulated in sixteen authenticated derived
coarse-AO variants (internal postprocess stages 128–143). Their only shader
change returns float.MinValue for out-of-range point samples; in-range sampling
and AO math stay stock. The compiler validates source dependencies and records
the base module/transform in its cache. This uses core Vulkan, with no custom
border-color extension requirement; the derived modules compile once and cache.
`sevk_set_hdr_quality` selects the mode; legacy callers retain RGBA16F until
they select it. HDR readback and feedback snapshots retain RGBA16F, with exact
packed-bit expansion for readback and format-converting GPU blits for snapshots.
Bloom and histogram half-resolution intermediates use the selected lighting
format. Bloom returns its first downsample level, following stock's pyramid
extent/pass count. Postprocess image reuse includes format as well as size.
Generated environment probes use stock R11G11B10 storage and follow stock's
level count, ending at 2x2. Their explicit RGBA16F readback ABI expands the actual
packed RGB bits exactly and supplies implicit alpha one;
authored skybox DDS cubes retain their complete chains independently. The
additive `sevk_set_world_skyboxes` selects visible and indirect probe sources
separately. The legacy setter clears the indirect override, and removing a cube
clears its selected bindings before the next capture.
Generated probe target zero is close (scene geometry included), target one
is far. Stock t11 binds close and t17 binds far in opaque/glass lighting;
lit billboards and GPU particles consume close at t11. Explicit readback
kinds 4/5 preserve that close/far publication order. Probe sky constants retain
the row-major view because the stock sky shader transposes it itself.
Glass draw geometry flags bit0 selects clockwise back-face culling from the
registered transparent material. Zero retains two-sided behavior. Both normal
and decal-depth glass pipelines own these variants; packet/export sizes stay
unchanged.
Optional `sevk_set_lighting_diagnostics` snapshots HDR after directional/ambient,
point, and spot lighting. It defaults off and allocates nothing while disabled.
`sevk_readback_lighting_stage` returns exact RGBA32F expansion of the current
HDR storage: stages 0/1/2 are those snapshots, stage 3 reads completed HDR and
needs no opt-in. Disabling waits for completion and releases snapshot images;
the stock render order is points, spots, then additive directional/environment.
Consequently stage 0 includes local radiance; stages 1/2 precede directional.
resize/format changes retire and rebuild them. Capturing splits dynamic
rendering around copies. GPU tests check identical HDR bits and presentation
with capture on/off in both HDR formats, real point-light separation, disabled
read rejection and resize. These are diagnostics, not a lighting quality mode.
Optional lighting kind 5 installs a seven-output FP32 diagnostic shader under
the same light resource contract. When enabled, it renders to separate owned
images after directional lighting with that pass's actual descriptor set.
Stage readback indices 4..10 expose those outputs; disabling frees their images
as well. The diagnostic pipeline persists until replacement or world cleanup.
`sevk_set_lighting_diagnostic_sampler` can select a maxAnisotropy=1 anisotropic
sampler for only that extra term pass. Mode zero uses the actual lighting set.
Mode one copies the set and overrides s3; it leaves the main lighting/material
samplers unchanged. Disabling diagnostics releases it and restores mode zero.
`sevk_set_probes_prefilter` adds the stock leading-mip skip without changing
the existing probe config ABI. Skipped original mips are copied exactly; only
the remaining rough levels are filtered. The legacy entry restores zero skip.
Timed probes alternate one blended cube each frame, retain completed filtered
work across refreshes and copy leading mips per processed face. The twenty processing
operations span nineteen frames because the sixth capture also postprocesses
face zero. Startup and zero-timeout captures still complete in one frame.
Timed blend strength follows the actual frame delta without a minimum step;
zero is valid and retains published radiance while capture work advances.
The managed renderer defaults to stock cascade fitting, projected-size pruning,
retained scheduling and usage feedback. `SE_VULKAN_STOCK_SHADOW_SCHEDULE=0`
selects the simplified fallback (including fitting unless explicitly overridden);
`SE_VULKAN_STOCK_SHADOW_FIT=0` retains scheduling with the simplified fitter.
Explicit stock fitting still requires scheduling.
Skybox cubes and ordinary two-dimensional DDS files retain authored mip chains.
`sevk_upload_world_cube_mips` accepts face-major chains and validates every
face/level byte count; the original cube export retains one-level semantics.
The managed bridge preserves stock HalfVector4 packet precision before viewport
scaling. Vertex and index uploads share one staging allocation/submission/wait
per mesh, with distinct transfer-to-vertex and transfer-to-index barriers.
Staging uploads and LCD rendering currently wait for their upload timeline;
asynchronous streaming/batching is later performance work.

Windows presentation attaches to the game's HWND. Linux supplies SDL3's instance
extensions, `SDL_Vulkan_GetVkGetInstanceProcAddr`, and a surface callback. The
caller must invoke SDL surface creation on the window's owning thread; native code
does not import SDL or create another window. The callback-created surface is
owned and destroyed by this engine, while the SDL window is owned by the game.

Screenshots call `sevk_request_readback` before `end_frame`; the renderer copies
the acquired surface image before presentation. Afterwards `sevk_readback` waits
only for that capture and returns RGBA8 using actual `SevkStats.width/height`.
Reading a presented swapchain image without first requesting capture is rejected.
Offscreen test contexts can also read their last completed frame directly.

`sevk_readback_lighting_input` is an explicit completed-frame diagnostic for
the owned R8 screen-AO (0), resolved sun-visibility (1), pre/post-FXAA RGBA8
(2/3) and raw GBuffer0 sRGB RGBA8 including LOD/marker alpha (4). Its byte count
must equal the main render width times height, multiplied by four for RGBA8.
The post-FXAA selection returns tone output when FXAA is disabled. It waits for completion, copies
through owned staging and restores sampling layouts; it cannot run during frame
recording. This adds no guest image aliases. The isolated lighting fixture uses
it alongside stock DX11 copies made on the owning deferred context.

Pinned upstream dependencies are in CMake: Vulkan-Headers 1.4.321, volk SDK
1.4.321.0, VMA 3.3.0, SPIRV-Tools/Headers/Reflect SDK 1.4.321.0, and DXC
1.8.2505.1. DXC Windows/Linux archives have enforced SHA-256 checks. SPIR-V 1.6
is compiled for DXC's Vulkan 1.3 environment and validated against Vulkan 1.4;
scalar block layouts are explicitly enabled. Runtime startup uses the compiled
shader assets and needs no shader compiler. Optional content-shader tooling is
packaged separately with its upstream licenses.

Native tests verify ABI packing and invalid inputs, reject a mocked older loader
and physical device, and reject each missing required feature independently.
An API 1.4 device with all optional host-copy/local-read features absent reaches
device creation with the six used features explicitly enabled and unused 1.4
features disabled. The mock verifies cleanup before the real GPU tests run. Tests
assert actual GPU pixels for sprites/meshes/LCD/local lights/shadows and reversed
depth, midgray gamma, cutouts/dither, mip selection, clamp edges, derivative normal
X convention, stock light attenuation, and additive bucket depth behavior. They
exercise resize/minimize and resource lifetimes, and test a hidden Windows
surface with capture. The Windows presentation test checks D3D11 is not loaded.
Alpha tests compare straight/premultiplied edge coverage, midtone tint over a
nonblack background, and LCD compositing. `Tests/SpriteGpuTests` also compares a
real installed FontDataPA BC7 base and mip-two edge texels with the stock
preprocessing/shader formula using an independent Pillow reference decode. Raw GUI RGBA uploads use
the stock gamma-2.2 premultiply table in their separate managed cache variant.
These tests do not establish in-game parity. Linux/SDL and validation-layer runs
require their respective host facilities; an absent validation layer is reported
by probe/status, and zero validation errors without that layer proves nothing.


Managed OLD mesh-decal fallback draws set `SevkWorldDraw.flags`
`0x04000000`. The main-pass sorter keeps OLD mesh decals before NEW mesh decals
and applies stock DECAL, DECAL_NOPREMULT, DECAL_CUTOUT priority across actors.
Cutouts share the late decal sequence and preserve the opaque depth. NEW fallback imports retain the stock MyMwmData technique-sorted part
list. Direct, prepared and indirect overlap cases are covered by the scene
GPU suite. This bit does not claim full OLD resource-key tie-breaks or mixed opaque/decal source interleaving,
including accelerated source paths; those parity items are
tracked in `Docs/LightingParity.md`.

Production fallback model packets carry stock raster flags: `0x08000000`
back-culls clockwise fronts, `0x10000000` disables culling in main/forward
views, and `0x20000000` supplies the OLD proxy depth no-cull override.
The OLD depth no-cull override also disables the view depth bias, as stock
replaces the complete rasterizer. NEW depth parts retain the cascade/spot view rasterizer. The first two flags
are mutually exclusive; raw packets with neither retain their prior defaults.
Cull mode, front face and depth-bias enable use core Vulkan 1.3 dynamic state (the renderer requires
Vulkan 1.4), set after pipeline binding for every model draw. Indirect groups
include all three raster flags in their compatibility key. Voxel and highlight
pipelines retain their own static raster policies.


OLD instanced distance-fade variants use a disjoint +2560 pipeline bank.
Core, cutout and blended identities retain their original low-bank semantics;
depth/forward/highlight partners preserve the bank. Production selection
uses it only when the OLD actor requests DistanceFade and the source stream
is actually instanced. NEW and non-instanced sources keep stock behavior.
The source generator owns the IDs/macros and the supplement manifest remains
strictly authenticated. Wider live/view validation is tracked as incomplete
in Docs/LightingParity.md.

World cascade cache ownership is observable through
`sevk_get_shadow_cache_mask`. This is CPU submission state, with no GPU wait or
readback; it is independent of the scheduler's valid/usage mask. Menu/preview
shadow rendering shares the first four cascade images and invalidates those
world ownership bits, plus automatic-cache reuse. The managed scheduler queries
ownership before selecting depth views, so even a frozen cascade is initialized
again when its image was overwritten. Untouched higher cascades retain their
ownership. A successful world redraw restores the bits, allowing ordinary
scheduled reuse on subsequent frames. Focused regression commands are
WorldPassGpuTests and GpuSceneLiveTests with `--shadow-cache-only`.
