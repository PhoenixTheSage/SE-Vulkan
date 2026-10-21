# DX11 lighting and PBR parity

Status: general stock lighting/material parity accepted (2026-10-20), against
the user's clarified common-path/appearance standard. The fresh paired broad
scene, producer/topology checks, reviewed medium-off/on images and scoped
numeric evidence are recorded in the final acceptance section below. Minor
edge cases remain explicitly deferred. Earlier Creative captures exposed
darker Vulkan walls/floor and incorrect LCD shading; those historical failures
remain preserved alongside their corrections and subsequent acceptance.

Accepted completion standard (2026-10-18): match stock features and appearance,
with documented GPU tolerances. Exact vendor-specific interpolation bits are
not required. Numeric limits must retain their measured scope and cannot
excuse unexplained visual differences.

Completion priority clarified by the user (2026-10-20): establish general
stock feature and appearance parity on common rendering paths. Minor or rare
edge cases may be documented for later rather than blocking this milestone.
Do not treat unproven pathological resource collisions, unusual allocation/LOD
ties, or a missing diagnostic-only disposal marker as general-parity blockers
without evidence of a material common-path failure. Visible shading defects,
missing common features, GPU errors and real lifetime failures still require
resolution. The historical full-audit checklist below retains its original
scope as follow-up evidence, not an unconditional general-parity gate.

Current checkpoint (2026-10-20): the four-panel spatial OLD resource-order
fixture passes its exact color/LOD and depth gate. The missing normal/gloss
fallback now uses stock bytes and decoded panel normals/materials match.
Matched FP16 panel HDR RGB differs by at most one representable step. A
source-incompatible probe restart on large camera movement was removed;
the probe suites pass427 checks without indexed input and485 with indexed
input, with zero Vulkan core/synchronization errors. Fresh broad acceptance
matches quality, verifies all requested producers and room topology, and
retains the accepted HDR bounds over 26,391 exact-input pixels. Vulkan unload,
disposal and process exit complete normally. Deferred permutations and the
separate DX11 reference-host teardown limitation are recorded below.

**Quality audit correction:** runs through `62eb45a2130a4640a7d8ec11571d1eba`
pinned viewport/camera/sun but allowed the game to choose renderer-specific
quality defaults. Their config files differ in AO, anisotropy, anti-aliasing,
texture/model/shader/light/shadow quality and vegetation distance. The stage
numbers below are historical diagnostics and before/after evidence within
Vulkan, not proof of full scene equality. New lighting fixtures seed and apply
the DX11 baseline quality values, record actual settings at capture, and reject
quality drift. The comparator now requires matching `lighting-quality.xml`
unless explicitly inspecting historical data with `--allow-unpinned`.

The comparison must cover material/GBuffer inputs (color/metal, normal/gloss,
AO/emissive/mask), directional and local lighting/shadows, diffuse/specular
environment probes, and HDR/exposure/tonemapping with matching scene, camera,
sun and settings. Final acceptance requires reviewed DX11/Vulkan images and
numeric evidence at the stage being changed. No brightness multiplier or
quality reduction substitutes for a matching contract.

## Extension-map view correction — 2026-10-17

The installed `VRage.Render11.dll` method
`MyGeometryTextureSystem.GetArrayTextureFromFilepath` converts every material
channel except NormalGloss to an sRGB-compatible format. Its missing-map format
for Extension is BC7_UNorm_SRgb. The legacy `MyFileTexture.Load` path follows the
same rule. `PixelUtilsMaterials.hlsli` consumes Extension red as AO and green as
emission, while alpha remains the linear coloring mask.

VulkanTextureCache.GetWorldExtensions incorrectly requested a linear view.
It now requests sRGB, including the neutral fallback. Decal extension bindings
also use sRGB. Voxel material uploads already use `role != 1` and required no
change. Normal/gloss stays linear; raw mip bytes and alpha are preserved.

Evidence:

- `Tests/MaterialTextureContract` compiles the production cache with a recording
  upload boundary. Its extension assertion fails before the correction and all
  seven checks pass afterward, including separate linear/encoded cache entries,
  mip/alpha preservation, neutral values and resource release.
- `Tests/GBufferGpuSmoke` passes 34 checks with core/synchronization validation
  enabled and zero errors. The added stock-shader reference measures decoded AO
  and emission and verifies normal/metal/gloss are unchanged. The first run
  passed the new pixel checks but failed its old hardcoded submission count;
  that count now includes the additional reference frame and the full run passes.
- Both managed Release runtimes build with zero warnings/errors.
- Logs: `TestResults/lighting-material-contract-before.log`,
  `lighting-material-contract-after.log`, `lighting-material-gbuffer.log` and
  `lighting-material-build.log`.

Live corrected private run `8d31f7787f8f4b4f9cfc9a070f294ca1`, using the same
marked initial Creative snapshot, completed the producer/simulation fixture and
controlled unload. Its clear capture was reviewed against DX11 and still has a
substantially darker floor. This correction does not resolve that difference.

`SceneContentSmoke` was checked before editing and currently fails compilation
with 65 stale recording-backend type errors. It is not accepted as verification
and is not repaired as part of this focused correction. The new small cache
contract test avoids depending on that obsolete full-scene mock.

## Direct stock PBR kernel reference

`Tests/LightingParityGpuTests` runs the same installed MainDirectionalLight
function on DX11 WARP and Vulkan hardware with identical copied frame bytes.
All 256 metalness/gloss/view/normal combinations pass the fixed
`1e-4 + 2e-4 * abs(reference)` channel tolerance. Maximum absolute delta is
0.0000240803, validation is enabled and reports zero errors. This rules out a
large discrepancy in that sampled stock BRDF computation; it does not test
scene surface data, shadow visibility, probes or exposure.

The test stages the current Release native DLL privately. Its first integration
runs exposed an old normal asset DLL, the explicit compute entry convention,
runtime SPIR-V annotation preparation and extended-packet flag contract; those
were corrected to the existing production APIs before accepting any result.
Artifacts: `TestResults/LightingParityGpuTests/result.json`, `dx11.f32`,
`vulkan.f32`, and `TestResults/lighting-direct-reference.log`.

## Current live stage comparison

The existing Creative fixture now accepts `-LightingDiagnostics -AnomalyParity`.
It requests a single five-target compute copy during the held clear view. On
Vulkan it uses the existing RaiseFrame callback, extended packet and borrowed
GBuffer/depth/HDR aliases at BeforeTonemap, reading test-owned images after
EndFrame. On DX11 it copies the actual GBuffer views and input to
MyToneMapping.Run on the render thread, restoring the borrowed compute-stage
bindings. No new renderer protocol or normal-profile deployment is introduced.

Both use the same copy shader to output decoded float32 color/LOD, normal/AO,
metal/gloss/emission, depth and linear HDR plus raw frame constants. Dimensions
must match the actual non-MSAA 640x480 view. Complete metadata is written last;
missing/failed readback prevents the diagnostic fixture gate from passing.

DX11 run `43098c2b701c42949f23681b0a07749f` delivered all five finite stage
buffers and the 1280-byte frame, completed its fixture and unloaded. The first
Vulkan attempt `c41bed770fc24746aade083151671e8e` was correctly refused by the
compatibility scanner: the shared test DLL referenced SharpDX. The readback
implementation now lives in `CreativeFixtureHarness.Dx11.dll`, copied/loaded
only on the DX11 route; the shared harness has no SharpDX references. Both
assemblies build cleanly. Corrected Vulkan run
`9f6ef36b9a85456ca7cf94daa86b7fe2` completed the fixture and disposed cleanly,
with zero validation errors and no observed managed D3D11 device calls. The comparator reports all shared
foreground and depth/encoded-normal aligned subsets without resizing, exposure
adjustments or a fabricated visual-pass threshold. Actual paired analysis is
recorded in `TestResults/lighting-stage-comparison.json`. Among 40,804 pixels
with depth and encoded-normal agreement, mean linear base color is
DX11 (0.09859, 0.10320, 0.06507), Vulkan (0.09855, 0.10315, 0.06512).
Material AO means are 0.99818 / 0.99829. HDR luminance means are
0.25988 / 0.05021, with median Vulkan/DX11 ratio 0.09379. The main light
colors, direction, diffuse/specular factors, ambient factors and probe mip skip
match in the actual captured frames. This places a large discrepancy before
tone mapping; it does not establish equality of every GBuffer pixel or isolate
screen AO, sun visibility, probes and local lights from one another.

## Probe origin refresh

Installed `MyEnvironmentProbe.AddProbe` captures at the current camera.
VulkanProbes previously changed its origin only on reset/first capture or after
moving farther than DrawDistance. A later refresh after a smaller camera move
therefore continued capturing from the old location. Each new refresh now
updates the origin, retaining it while a staged cycle is in flight.

The actual GPU regression moves the camera ten metres within DrawDistance and
translates a fixed-world triangle into camera-relative coordinates. The triangle
must move from the forward cube face into the left face. This fails before the
fix and passes after it; the full ProbeGpuTests suite passes 324 checks with
zero core/synchronization validation errors. Logs are
`TestResults/lighting-probe-origin-before.log` and `lighting-probe-origin-after.log`.
Private live retest `64a22041478d48538dc81e8c60c5bc87` completed and disposed,
with zero validation errors. Its matching stage comparison records HDR means
0.25990 / 0.04946 and median ratio 0.09078. This fixes capture location but
does not resolve the dominant brightness gap. No second game was launched:
the startup process remained alive during Steam initialization, and the
prepared retry was correctly blocked by the existing-process guard.

## Default cascade depth ordering

Installed `MyShadowCascades` places the shadow view along SunLightDirection and
looks against it, then calls CreateOrthographicOffCenter with the farther plane
first. Its comparison sampler is less-or-equal. Vulkan's stock-fitting route
already used that order, but the default route used the opposite plane order
with the same view and comparison. This selected the opposite caster along
the sun direction. The default route now uses the stock plane order too.

ShadowOcclusionGpuTests calls the actual default fitter and checks the physical
contract: moving a point toward the sun must decrease shadow depth for a
less-or-equal comparison. It fails before the change, passes afterward, and
all 34 suite checks pass with Vulkan validation enabled and zero errors.
Logs: `TestResults/lighting-cascade-depth-before.log` and
`lighting-cascade-depth-after.log`. Private live retest
`41ea1ab72b0b4ff293ef44cf4f4d6ffc` completed and disposed with identical
scene/capture settings and zero validation errors. Among 40,811 depth/normal
aligned pixels its HDR luminance means are 0.259865 / 0.247449 (DX11/Vulkan),
with median Vulkan/DX11 ratio 1.009324. Reviewed clear-view images confirm
the dominant dark-floor deficit is removed. This does not prove exact equality:
the LCD remains dark, animation phases differ, and local/probe/shadow edges
still need inspection. Artifact: `TestResults/lighting-stage-cascade-depth.json`.

## AO policy

Stock MyGBufferResolver gates both algorithms on user AmbientOcclusionEnabled,
debug Postprocessing and debug SSAO, then prefers SSAO over HBAO. Vulkan ignored
the user/debug gates and preferred HBAO when both environment settings enabled
their algorithm. VulkanPostprocess now follows those stock gates and priority,
without modifying the stored environment settings. MyVulkanRender passes the
actual user setting. PostprocessGpuTests passes 189 checks with validation
enabled and zero errors, including actual pass counters for all gates, SSAO
priority and HBAO resumption. Log: `TestResults/lighting-ao-policy.log`.
This change is not in the pinned cascade live retest above.

## LCD alpha/material contract

The live LCD crop has similar base RGB and emission in both renderers, but
mean encoded metalness is approximately 0.041 DX11 versus 0.960 Vulkan.
Installed MyOffscreenRenderer.RenderWork uses BlendAlphaPremultNoAlphaChannel;
MyBlendStateManager gives that state an RGB-only write mask. Vulkan's four LCD
sprite variants wrote alpha, turning sprite coverage into ColorMetal metalness.
Their write mask now preserves destination alpha, including nonzero background
alpha. Display/HUD pipelines retain their existing alpha behavior.

The stock-GBuffer GPU regression checks all four LCD blend variants against
transparent and nonzero-alpha backgrounds and verifies RGB is still written.
It fails before the native change (`TestResults/lighting-lcd-alpha-before.log`).
The canonical native build/test passes 15 CTests with one optional GPU opaque
reference test skipped. All 50 stock-GBuffer checks pass after the correction;
the installed HUD/font/logo GPU suite also passes with zero validation errors.
Live `62eb45a2130a4640a7d8ec11571d1eba` completed/disposed and its reviewed
image restores the cyan LCD. In the same crop, mean encoded metalness becomes
0.03986 versus DX11 0.04115, and HDR RGB becomes (0.15834, 0.57138, 0.61228)
versus (0.20071, 0.60827, 0.63517). Its overall HDR luminance means are
0.25993 / 0.26391, median ratio 1.03335. These remain unpinned-quality
diagnostics, as described above.

Stock screen areas request GenerateMipMaps=true and MyOffscreenRenderer
generates mips after drawing. Vulkan previously replaced the image with a
single-mip target. The renderer now preserves that request and uses the
additive native `sevk_render_texture_mips` entry, keeping legacy entries valid.
The attachment has a separate base-level view owned/retired with the texture;
sampling uses the complete sRGB mip chain. Transfer barriers cover each written
mip before publication, including batched uploads. Canonical native CTest
passes again (15 passed, one optional skip). The expanded GBuffer suite passes
52 checks with zero errors: a deliberately minified checker fails the half-light
reference without mips and matches it with mips. Logs:
`TestResults/lighting-lcd-mips-native-build.log`, `lighting-lcd-mips-gbuffer.log`.
The corrected quality-pinned live pair is recorded below.
The full lighting/PBR equality goal remains active.

## Authored skybox cube mip chains

The actual world skybox path called ReadCube with top-level-only retention and
uploaded one level, unlike the stock DDS resource. It now retains each face's
authored mip chain, preserving DDS face-major order and sRGB/BC6H handling.
The six-flat-face fallback also retains mips. The additive cube-mip upload
validates complete level counts/bytes, uses one image view spanning every level,
and publishes all six face chains after their transfer writes. Old exports and
one-level callers retain their behavior.

MaterialTextureContract fails before the change and passes nine checks after
it, including distinct per-face/per-level bytes. Both managed runtimes build
without warnings/errors. Native CTest passes 15 tests (one optional reference
skip); the actual stock-probe GPU suite passes 328 checks with zero validation
errors. Its mip fixture contrasts a single red mip-zero cube with a
red-base/green-authored-mip cube through the real sky shader, including batched
upload and replacement of an existing cube.
Logs: `TestResults/lighting-cube-mips-before.log`, `lighting-cube-mips-after.log`,
`lighting-cube-mips-managed-build.log`, `lighting-cube-mips-native-build.log`.
The quality-pinned control pair (`2d049b5225f7498da43214ac1242caa3` DX11,
`5ffa70b008224cfaac3827700f3a8c69` Vulkan) predates this cube change; Vulkan
completed and disposed. Both quality manifests match.

## Stock sampler roles

Installed MySamplerStateManager.StandardSamplers binds s0/s2 linear-clamp,
s1 point-clamp, and s3 the user's anisotropic wrapping texture sampler.
Native stock bindings previously reused the anisotropic wrapping sampler.
The affected stock lighting, probe, postprocess, atmosphere, particle and
native-pass bindings now select the appropriate existing sampler. The cloud
renderer retains its explicit wrapping s0 override.

The stock world-lighting GPU regression changes only the noncontributing LUT
column at gloss zero: wrapping incorrectly imports that column; clamp does
not. It fails before the fix and passes afterward. ProbeGpuTests passes 331
checks, GBufferGpuSmoke 52 and PostprocessGpuTests 189, all with zero validation
errors. Native-pass, environment, transparency and particle GPU suites also
pass. Logs: `TestResults/lighting-sampler-*.log`.

Quality-pinned treatment `0b44fb4235f0437f8677bcc367806427` completed and
disposed, with zero validation errors. Against DX11
`2d049b5225f7498da43214ac1242caa3`, 50,900 depth/normal-aligned pixels have HDR
luminance means 0.256999 / 0.270368, median Vulkan/DX11 ratio 1.040418.
Even 24,429 pixels with exact captured GBuffer material/normal/AO values retain
a median ratio 1.039810. These fixes establish their individual contracts but
do not close the remaining lighting gap. Artifacts:
`TestResults/lighting-cube-sampler-pinned-comparison.json` and
`lighting-material-alignment.json`.

## Shadow comparison sampler

Stock Shadowmap uses comparison-linear min/mag/mip filtering, clamp-to-edge
and less-or-equal. Native used a white border and nearest mip selection;
it also selected nearest filtering when the ordinary linear-format bit was
absent. Native now follows the stock comparison sampler. Vulkan's ordinary
linear-filter requirement applies when comparison is disabled; depth
comparison has its own capability contract (see the official
[drawing specification](https://docs.vulkan.org/spec/latest/chapters/drawing.html)).
The tested RTX 5070 Ti supports ordinary D32 linear filtering as well.

The actual spotlight sampler GPU regression proves both occluded outer D32
texels before testing the corner. The white-border implementation fails by
leaking light; clamp passes. A second fixture verifies bilinear PCF across a
half-occluded texel boundary. PostprocessGpuTests, ShadowOcclusionGpuTests and
TransparencyGpuTests pass with zero core/synchronization validation errors.
Canonical native CTest passes 15 tests, one optional skip. Logs:
`TestResults/lighting-shadow-sampler-{before,after,occlusion,transparency}.log`.
Quality-pinned live treatment `accc48cd7df641849b1184302713075b` completed,
unloaded and disposed with zero validation errors. Its reviewed capture keeps
the corrected floor and cyan LCD. Against the same pinned DX11 baseline,
50,994 depth/normal-aligned pixels have HDR means 0.258179 / 0.268326 and
median ratio 1.042113. The broader gap remains. Artifact:
`TestResults/lighting-shadow-sampler-pinned-comparison.json`.

## Lighting input diagnostics

The exact-GBuffer subset still differs in HDR, so the fixture now captures the
actual screen AO and resolved sun visibility as well. Native has an additive,
completed-frame R8 readback entry, restoring shader-read layouts and keeping
staging alive through its completed copy. PostprocessGpuTests passes 200 checks,
including neutral AO bytes, repeated sun/AO reads and following frames, with
zero validation errors. Native CTest passes 15 tests, one optional skip.

DX11's GBufferResolver uses a deferred render context. The fixture records its
input copies on that same context and maps the owned staging textures at tone
mapping after the engine executes the list. Copying on the immediate context
inside that recording hook would read stale inputs, so it is deliberately not
used. No new device, state reset, guest resource alias or shared Anomaly API is
introduced. Both harnesses and managed runtimes build without warnings/errors.
DX11 `ac274fa34f71444bb9e4fdfc57ef4bc1` captured both additional inputs,
completed the fixture, unloaded and exited. Screen AO ranges 68..255 and sun
visibility 0..255; neither is a placeholder. Comparing its five original buffers
against the prior Vulkan capture retains median HDR ratio 1.040671 with matching
quality manifests (`TestResults/lighting-new-dx11-control.json`). The matching
Vulkan input capture `1714e39947f84939b9d7a4afe91961e4` completed, unloaded
and disposed with zero validation errors, before the HBAO blur correction below.
Both manifests match. Among 50,642 depth/normal-aligned pixels the HDR median
ratio is 1.041177. Crucially, 12,214 pixels with identical captured material,
screen AO and sun visibility retain median ratio 1.041404 (HDR means 0.232609 /
0.244786). Screen AO agrees closely overall (aligned means 0.954312 / 0.954048),
but sun visibility has a separate spatial mismatch (mean absolute delta 0.090865).
Artifact: `TestResults/lighting-input-pinned-comparison.json`. This isolates a
remaining lighting-input/processing gap beyond AO and visibility, while leaving
the spatial sun-shadow difference open as well.

## HBAO blur sampler override

Stock MyHBAO.DrawBlurXPS explicitly binds Point at s0 and Linear at s1; BlurY
retains them. Native's custom HBAO binding path used the anisotropic wrapping
sampler at s1, bypassing the standard-role correction. It now uses linear-clamp.

The GPU regression uses actual unblurred HBAO on geometry with different
occlusion at opposite viewport edges. With exponent one and zero depth
sharpness, the stock blur reduces to a separable Gaussian with its adaptive
radius-four samples. The independent CPU reference includes clamped fractional
samples and R16 intermediate rounding. The old sampler fails with maximum
delta three byte levels, after proving minimum AO 124 and edge contrast 12.
Earlier fixture setups without edge contrast are invalid diagnostic attempts,
not product failure evidence. Before log: `TestResults/lighting-hbao-blur-before.log`.
The corrected native build passes CTest (15 passed, one optional skip).
The exact same GPU regression now passes with maximum delta one, and all 204
PostprocessGpuTests checks pass with zero core/synchronization validation errors.
After log: `TestResults/lighting-hbao-blur-after.log`. This fixes the boundary
contract; the paired scene inputs above still use the preceding snapshot and
do not include this correction.
The private live treatment `ba8458052efd4f3280161954e0c1d588` completed,
unloaded and disposed with zero validation errors. Both extended input dumps
and quality manifests are complete. Aligned HDR means are 0.258924 / 0.268377,
median ratio 1.040339. The HBAO edge correction does not close the central
lighting difference. Artifact: `TestResults/lighting-hbao-blur-pinned-comparison.json`.

## Generated probe level count

Installed MyEnvironmentProbe.CreateResources computes log2(resolution) levels
and passes that exact count to MyArrayTextureManager.CreateRtvCube, whose image
and SRV descriptors preserve it. Generated probes therefore end at 2x2.
Native allocated log2(resolution)+1 levels, ending at 1x1. This also changed the
actual shader contract: EnvAmbient.hlsli uses GetDimensions' level count for
roughness LOD, and the prefilter gloss ramp uses the generated level count.
Native now uses the stock count for original, filtered and final generated
probes; complete authored skybox DDS mip chains are a separate resource path.

The GPU regression reads the final 2x2 level, then requires the extra 1x1
readback to be rejected. The old implementation fails the latter check:
`TestResults/lighting-probe-levels-before.log`. Native build/CTest passes
(15 tests, one optional skip); the complete probe suite passes 329 checks with
zero core/synchronization validation errors after the correction:
`TestResults/lighting-probe-levels-after.log`. The 32px fixture now expects 48
prefilter passes (two cubes, six faces, four filtered levels) and validates all
five actual levels, rather than treating an extra level as correct.
The private generated-level treatment `6d615007bf76412a90bf96519d562847`
completed, unloaded and disposed with zero validation errors; it predates the
indirect-skybox correction below. Quality and extended inputs match. Its
50,968 aligned pixels have HDR means 0.258445 / 0.268795 and median ratio
1.038144. Artifact: `TestResults/lighting-probe-levels-pinned-comparison.json`.

Other observed probe contracts remain open: probe startup and temporal
publication differ from stock. Reflected main-frame offset 756 proves SkipIBLevels=0 in both current
paired captures, so that unsupported nonzero setting is not their current
brightness cause. Observed HDR quantization also confirms R11G11B10 versus
RGBA16F main-buffer storage (`TestResults/lighting-hdr-precision-observation.json`);
that alone does not establish the cause of the residual brightness difference.
Rounding only the final Vulkan HDR capture to R11G11B10 on the CPU retains a
median ratio 1.042139 on the 12,214 fully matched pixels (original 1.041404).
This control does not reproduce per-pass/probe quantization, so it rules out
neither of those effects and is not GPU format-parity evidence. Artifact:
`TestResults/lighting-hdr-final-quantization-control.json`.

## Separate indirect skybox source

Stock UpdateRenderEnvironment constructs the sibling filename with `Indirect`
before the extension. MyEnvProbeProcessing.RunForwardPostprocess uses it when
MyFileSystem reports it exists, otherwise the visible skybox. The installed
default BackgroundCubeIndirect.dds exists. Native probe postprocessing always
used the visible world cube instead.
The installed visible asset is 4096px/13 mips and its indirect sibling is
512px/10 mips, both BC7 sRGB; they are separately authored cube resources.

The texture cache now selects the stock sibling through the game's filesystem,
retains its complete authored mips and keeps it distinct from the visible cube.
The world renderer receives both handles through an additive internal native
entry; probe postprocessing uses the indirect source. The legacy setter clears
the override and preserves its single-cube behavior. Removing a selected cube
clears its binding, while the existing timeline retirement owns image lifetime.
No Anomaly API, guest alias or shared lighting control is added.

MaterialTextureContract passes 12 checks, including absent-sibling fallback,
present-sibling selection with distinct bytes and preservation of the visible
cube. Both managed targets build without warnings/errors. Native CTest passes
15 tests (one optional skip). ProbeGpuTests passes 336 checks with zero
core/synchronization validation errors, including separate blue probe-source
radiance, unchanged visible sky pixels, legacy fallback and removal fallback.
The first removal fixture used texture kind 0; the correct cube kind 4 exercises
the actual ownership path. It passes without a product change to that path.
GPU log: `TestResults/lighting-indirect-skybox-gpu.log`. Private live treatment
`82c588fda2a54608a860e8ba1e925cb8` completed, unloaded and disposed with zero
validation errors. Quality and extended lighting inputs match. Aligned HDR means
are 0.259030 / 0.258972 and median ratio 1.007962; 12,487 pixels with identical
captured materials, screen AO and sun visibility retain median ratio 1.008286
(means 0.232072 / 0.234450). This removes most of the roughly four-percent
brightness difference but does not establish whole-renderer equality.
Artifact: `TestResults/lighting-indirect-skybox-pinned-comparison.json`.
Logs: `TestResults/lighting-indirect-skybox-{texture,managed-build,native-build}.log`.

## Nonzero probe prefilter skip

Stock MyEnvProbeProcessing.Prefilter copies original levels 0 through
SkipIBLevels, then filters the remaining levels using a gloss ramp relative to
that remaining count. Native previously copied only mip zero and filtered all
later levels. The port now copies every leading level across all six faces,
filters only the remainder and adjusts that ramp. A changed skip setting resets
the staged capture so its copied and filtered portions use one configuration.

The additive internal `sevk_set_probes_prefilter` entry carries the explicit
integer setting; the original config size, flags/reserved fields and legacy
entry remain intact. Zero uses the legacy path and restores zero-skip behavior.
Validation rejects skips outside the allocated chain before changing state.
The all-skipped boundary emits only copies, with no zero-denominator gloss ramp.

Both managed targets build without warnings/errors. Native CTest passes 15
tests, one optional skip. ProbeGpuTests passes all 346 checks with zero
core/synchronization validation errors. They read actual original/filtered
bytes from both cubes and every face, check pass counts at skip two and maximum
skip, reject an invalid skip, then restore zero. Logs:
`TestResults/lighting-probe-skip-{managed-build,native-build,gpu}.log`.
The pinned default scene uses zero and is not evidence for the nonzero case.

## Generated probe storage precision

Installed MyEnvironmentProbe.CreateResources always uses R11G11B10_Float for
original, filtered and final cubes. Native used RGBA16F. This changes capture,
mip filtering and repeated final-cube blending precision, beyond final main-HDR
quantization. The GPU regression first confirms nontrivial blue sky radiance,
then checks the actual stored RGB grid and implicit alpha one. The RGBA16F
implementation fails it (`TestResults/lighting-probe-format-before.log`).

All generated probe images and their legacy/resident/indexed forward, sky,
prefilter, blend and atmosphere pipelines now use the stock packed float format.
Capability checks require sampled linear filtering, blended color attachment
and mip blits before creating those pipelines. Explicit readback keeps its
RGBA16F byte ABI by exactly expanding packed unsigned channels into half-float
mantissa bits (same exponent bias), with implicit alpha one. Image transfers
read the actual packed bytes; no extra GPU images or quantization are inserted.
Readback also rejects an active upload batch, whose copy would not be completed.

Native build/CTest passes 15 tests, one optional skip. The complete GPU suite
passes 348 checks with zero validation errors after the correction
(`TestResults/lighting-probe-format-after.log`). A fresh pinned scene comparison
is pending. A further startup regression compares every filtered/final mip in
both cubes and all faces on the initial completed frame. It passes already:
probe_fullscreen forces blend strength one while not ready. The earlier
fade-from-black suspicion was incorrect, and no lifecycle product change was
made. The added regression yields 349 passing checks, zero validation errors
(`TestResults/lighting-probe-startup-before.log`; despite its provisional filename,
it records a pass and is not before-failure evidence). The indexed-GPU variant
also passes 375 checks with zero validation errors, including both GPU frame
slots: `TestResults/lighting-probe-format-indexed-gpu.log`. Its launch must set
both SE_VULKAN_TEST_GPU_OPAQUE=1 and SE_VULKAN_GPU_OPAQUE=1 before process
creation; enabling only the selector or changing the managed environment later
did not admit the native device. Those setup failures never reached an indexed
draw and are not product/format failure evidence. Packed-format live treatment
`05f69a3bbbde497e8cbb471c7e7ab929` completed, unloaded and disposed with zero
validation errors. Its matching-quality comparison has 50,854 aligned pixels,
HDR means 0.258307 / 0.256364 and median ratio 1.004007. The 12,223 pixels with
identical captured materials, AO and visibility retain median ratio 1.004779.
Artifacts: `TestResults/lighting-probe-format-pinned-comparison.json` and the
explicit CPU-only `lighting-probe-format-final-quantization-control.json`.
The latter's toward-zero final rounding gives median 1.0 but only 44.2% exact
RGB pixels. It is not evidence of a device conversion rule or full equality.

## Same-frame startup reflection availability

The initial probe images themselves already publish correctly. A separate
handoff bug zeroed ambient specular in the first main frame: MyVulkanRender
built its frame constants from the prior ProbesReady status, even though the
native full startup capture completes before world_light_set/main lighting.
The renderer now uses ProbesAvailableForFrame, considering the configured
complete capture (and the final legacy face) while preserving published-status
reporting and the disabled/held cases.

The real GPU regression disables main directional lighting, diffuse ambient
and material emission, leaving specular reflection over a known blue sky.
Before the change the first surface pixel is 0/0/0 with sky blue 116. Afterward
it is 0/0/3, with the same sky, proving the same-frame reflected light is used.
Both managed targets build without warnings/errors. The whole probe suite
passes 350 checks with zero validation errors. Logs:
`TestResults/lighting-probe-first-frame-{before,after,managed-build}.log`.
The held-camera packed-format scene above predates this one-frame correction.

## Stock timed probe cadence

Stock MyEnvironmentProbe alternates close/far blending every timed frame. It
seeds work from the currently published cube at refresh start, then copies and
filters each face during that face's prefilter step. Native blended both cubes,
paused blending during the staged refresh and copied all leading face mips
early during mip generation. Its separate capture/finalization steps also
introduced an extra frame: stock postprocesses face zero alongside the sixth
capture, completing the timed refresh in nineteen frames.

Native now follows that sequence for the stock timed mode. Work starts from
published radiance, leading mips copy only for the processed face, blending
continues on one alternating cube throughout the cycle, and startup/immediate
publication still covers both cubes. Legacy per-face capture mode is retained.
Copies use owned images, explicit transfer transitions and restored read layouts.

The prior implementation fails the actual warm-frame blend-count regression:
`TestResults/lighting-probe-cadence-before.log`. Native CTest passes 15 tests,
one optional skip. The default GPU suite passes 352 checks with zero validation
errors (`TestResults/lighting-probe-cadence-after.log`). New checks verify the source stage sequence, a blue-to-green transition
changing only the selected cube, and retention of an unprocessed face across a
second refresh (including the mip-generation stages).
The indexed-GPU suite additionally verifies a timed nineteen-frame refresh and
all close/far face radiance against its resident reference, with validation
enabled/errors zero (`TestResults/lighting-probe-cadence-indexed-gpu.log`).
Private live treatment `f46bb6b4795143c499224277db0e8da0` completed, unloaded
and disposed with zero validation errors, including the same-frame startup
correction. Matching-quality aligned HDR means are 0.258757 / 0.256747 and
median ratio 1.003055. Artifact:
`TestResults/lighting-probe-cadence-pinned-comparison.json`.

## Stock shadow-fit investigation

The first treatment compared the simplified fitter/schedule against the existing
stock fitter, which requires retained cascade scheduling. The private fixture
accepts explicit StockShadowSchedule/StockShadowFit switches, records them in
the prepared environment, and captures the actual Vulkan shadow-mode properties
with its lighting buffers.
The opt-in treatment `adae445638c945c190858a6be489f1d0` completed and
disposed after 350.8 seconds. Its captured shadow-state.xml confirms both
stock switches active (UpdateMask=2, ValidMask=34, DrawDistance=67000).
Matching-quality aligned mean sun visibility is 0.674926 / 0.674023;
mean absolute visibility delta is 0.073263, versus 0.089787 for the prior
simplified treatment. Aligned HDR means are 0.258982 / 0.261984 and the
median ratio is 1.008244. This is a shape/coverage improvement, not proof
of full shadow equality: independent runs still have moving caster phases.
Artifact: `TestResults/lighting-stock-shadow-pinned-comparison.json`.

ShadowScheduleTests passes 206 CPU checks. SceneSubmissionGpuTests passes its
scheduled-update, every-frame/frozen depth, stock projected-size, infinite-camera
matrix and canceled-publication shadow checks, but the **whole suite fails** a
later bulk-model representation-count assertion. Its prior shadow successes
are focused evidence, not a green full-suite claim. The later failure remains
reproduced on an isolated archive of unmodified HEAD 86e355f, built without
deployment and run with the original native DLL (SHA256 876D526E...043).
That control fails the same Program.cs:278 assertion, so this is a pre-existing
suite failure, not introduced by these lighting changes. The full suite remains
red; its bulk-model eligibility issue is not repaired or silently waived. Logs:
`TestResults/lighting-stock-shadow-{schedule-cpu,submission-gpu}.log`.
Baseline logs: `TestResults/lighting-stock-shadow-baseline-{build,submission}.log`.

The managed production defaults now select stock fitting and scheduling.
Explicit `SE_VULKAN_STOCK_SHADOW_SCHEDULE=0` retains the simplified fallback;
an unset fitting override follows that opt-out. `SE_VULKAN_STOCK_SHADOW_FIT=0`
can retain scheduling with simplified fitting. Explicit stock fitting without
scheduling remains invalid. The default-selection regression fails before
this change. The shadow GPU suite passes 36 checks, including sunward depth
orientation through both the production fitter and the fallback, with explicit
synchronization validation requested/enabled and zero errors. Private run
`bc2a45029d3b4bc9885fb68f404fc995`, without either opt-in environment flag,
confirms actual stock fitting/scheduling, then completes controlled unload and
disposal at 342.1 seconds with validation errors zero. Its matching-quality
aligned HDR means are 0.259364 / 0.261868 (median ratio 1.008441).
Artifact: `TestResults/lighting-stock-shadow-default-pinned-comparison.json`.

## Zero-delta probe blending

Stock MyCommon.UpdateTimers publishes zero frame delta while paused, and
MyEnvironmentProbe derives timed blend strength directly from that delta.
Vulkan clamped the delta to at least 1 ms and rejected zero native blend
strength. It now preserves stock's measured rate, including zero and
sub-millisecond deltas; capture scheduling and first publication are unchanged.

The prior implementation fails the zero-strength regression. The corrected
GPU suite passes 379 checks with zero validation errors, verifying both
published cubes retain their radiance while distinct new capture work finishes,
then change when the clock resumes. Native CTest passes 15 tests with one
optional skip. Logs: `TestResults/lighting-probe-zero-delta-{before,after}.log`
and `TestResults/lighting-probe-zero-delta-native-build.log`.

## Main lighting target precision

Installed MyGBuffer.LBufferFormat selects R11G11B10_Float for normal HDR and
R16G16B16A16_Float for HDR_HQ. Native previously always allocated RGBA16F.
The renderer now forwards HqTarget to an additive internal native setter.
Deferred lighting, atmosphere/clouds, OIT resolve and additive billboards own
both format pipelines. Switching waits for prior work and recreates world
targets before use; legacy native callers retain RGBA16F until selecting quality.
The guest HDR alias supports both render formats. Feedback snapshots retain
RGBA16F through format-converting GPU blits, and diagnostic readback expands
packed bits exactly to the existing RGBA16F layout with implicit alpha one.
Owned guest images keep their existing native readback semantics.

FSR's color descriptor follows the actual selected image format: SDK 1.1.4
defines RGBA16F=4 and R11G11B10=16 in its
[primary header](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/v1.1.4/ffx-api/include/ffx_api/ffx_api_types.h).
This descriptor correction is source-verified; a vendor-upscale run is separate
from the current non-upscaled lighting fixture.

The postprocess GPU suite passes 216 checks with validation enabled and zero
errors. Actual main HDR samples have R11/G11 six-bit and B10 five-bit mantissas,
alpha one, stable radiance across both frame slots, visible additive billboards,
and exact restoration of prior RGBA16F radiance after toggling HqTarget back.
Log: `TestResults/lighting-hdr-format-postprocess-gpu.log`.
The normal-HDR environment GPU suite additionally passes 50 checks with zero
validation errors, covering actual atmosphere/cloud/foliage pixels and lifecycle.
Its new `--normal-hdr` switch selects the real target; the default suite retains
RGBA16F. Artifact: `TestResults/EnvironmentGpuTests/result-normal-hdr.json`,
log `TestResults/lighting-hdr-format-environment-gpu.log`.
The normal-HDR transparency suite passes 149 checks with zero validation errors,
including glass/OIT, decals, particle emit/simulate/render and resource reset.
Artifact: `TestResults/TransparencyGpuTests/result-normal-hdr.json`,
log `TestResults/lighting-hdr-format-transparency-gpu.log`. The final native
build passes 15 CTests with one optional skip
(`TestResults/lighting-hdr-format-native-build.log`).

First private packed-HDR attempt `d5221bb0d1f8454c99a843db530cbf8d` failed
before the menu: the managed draw caller invoked the quality setter inside an
open upload batch, correctly rejected by the native guard. The call is now
after the draw's upload-finally boundary and before BeginFrame. That failed
attempt provides no scene comparison; corrected private treatment
`d0c79d62bd9142feaf76b8d7f8692a94` completed, unloaded and disposed at
334.3 seconds with validation errors zero, using native SHA256
`A960269C052D6452D6AE5CAA735746E7183F71C5E790F37A6C212D0C0AD954AD`
and managed SHA256
`06EFBD947C05887A1C348CE7841139648DC21F42A743B382F3E6B10AAF0A6792`.
Its matching-quality aligned HDR means are 0.258726 / 0.257690, median
ratio 0.999297. The 13,284 pixels with identical captured material, AO and
sun visibility have HDR means 0.226126 / 0.225791 and median ratio 0.999696.
The actual images were inspected: broad floor/LCD shading now appears close,
but turbine phase differs and changes shadow coverage. Artifact:
`TestResults/lighting-hdr-format-pinned-comparison.json`.
Full image equality remains unproven.

## Static held-camera isolation

The harness now has an explicit StaticLightingDiagnostics option. It hides the
asynchronously rotating turbine only after the earlier animation captures,
checks actual visibility at lighting capture, and writes lighting-scene.xml.
The comparator rejects mixed scene states. This is a diagnostic isolation of
static lighting/shadows, not replacement of moving-geometry acceptance.
Private DX11 control `1ad899035bcb4effab0e16c6a2fa37df` and Vulkan treatment
`152b66b2205e46b99d9df63c58a80051` completed, unloaded and exited (Vulkan
disposed at 210.8 seconds, validation errors zero). Static aligned HDR means
are 0.284487 / 0.284747, median ratio 1.0. Mean absolute sun-visibility delta
is 0.003232; its means are 0.757135 / 0.757397. Screen AO still differs by
0.005532 on average. This isolates most of the prior shadow discrepancy to
the asynchronously moving caster but does not establish complete equality.
Artifact: `TestResults/lighting-static-pinned-comparison.json`.

## Bloom pyramid and intermediate precision

Installed MyModernBloom.Run uses MyGBuffer.LBufferFormat throughout and returns
the first downsample level, not the prefilter resolution. MyEyeAdaptation.Run
also creates its half-resolution input in src.Format. Native used RGBA16F for
both and upsampled once more into the prefilter-sized target.

The corrected path owns both format pipelines for those passes, switches them
with HqTarget, and reuses PostImage only when size **and format** match. Its
pyramid has one prefilter, N downsamples and N-1 upsamples. The extra-upsample
regression fails before this correction. Afterward PostprocessGpuTests passes
224 checks with zero validation errors, including one-level/taller bloom,
actual normal-quality pixels and exact restoration after switching quality at
unchanged dimensions. Native CTest passes 15 tests with one optional skip.
Logs: `TestResults/lighting-bloom-topology-before.log`,
`TestResults/lighting-bloom-format-postprocess-gpu.log`,
`TestResults/lighting-bloom-format-native-build.log`.
Native SHA256: `C3804399BEF66534C3BB144A31DE33473FA139B61CC3A0AD82A6420A92489675`.

## Per-face moving-camera probes and timeout changes

Stock MyEnvironmentProbe.AddProbe uses the camera's current position for each
face, while Vulkan retained the cycle's starting origin. Each active face now
uses the current origin. The valid moving-camera GPU regression compares a
staged face with an immediate capture at the same camera **after** the later
sky postprocess step; it passes with the correction and fails under the former
origin rule. Earlier probes of this regression were invalid controls: an old
resident actor/held timeout masked movement, and a pre-postprocess comparison
mistook cleared sky for an origin error. They are not supporting evidence.

Changing a positive timeout to zero also wrongly retained the former hold.
The hold condition now requires a positive timeout. Its independent regression
proves the fixture is holding first, then verifies all six faces publish on
the next frame. Both regressions pass within 407 GPU checks, validation errors
zero. Logs: `TestResults/lighting-probe-moving-camera-{before,after}.log` and
`TestResults/lighting-probe-timeout-zero-{before,after}.log`.
The indexed-GPU suite also passes 465 checks with validation errors zero,
including these regressions. Its moving-camera fixture explicitly disables
the registered GPU actor before switching geometry and retains the old owned
mesh until teardown; the earlier attempt correctly tripped the generation
ownership guard. Log: `TestResults/lighting-probe-moving-camera-indexed-gpu.log`.

## Stock HBAO jitter

Stock MyHBAO.InitializeConstantBuffer uses three MyRandom samples for each of
sixteen float4 jitters; its default seed is time/thread-dependent. Native used
an invented golden-angle/modulo sequence. The managed renderer now generates
the exact stock sequence and uploads the copied values through an additive
internal setter. Native validates the entire array before changing it and
retains legacy unconfigured behavior only for old callers.

Production retains stock's time-seeded default. Private lighting diagnostics
pin the actual initialization on both backends to 20261018 and record it in
quality evidence. A late DX11 HBAO initialization is also pinned before Run.
The capture fails if the seed was not actually pinned; old unseeded and new
seeded captures do not form a matching-quality pair.

PostprocessGpuTests passes 231 checks with zero validation errors: changing
the seed changes actual AO, restoring it restores exact sixteen-slice pixels,
and the existing independent blur reference still passes. A nonfinite last
component and wrong element count are rejected without partially mutating the
prior sample pattern. Both managed targets
and the harness build without warnings/errors. Native CTest passes 15 tests
with one optional skip. Logs: `TestResults/lighting-hbao-stock-jitter-*`.
Seeded private DX11 control `bf682258346b4874ae0e198d10c16bd2` completed,
unloaded and exited, and its quality manifest records the actual pinned seed.
Vulkan attempt `33d648e5c3ab4d6abb0fabd50ab4dc5e` correctly failed its
capture guard: HBAO initialized before the harness attached, so no pinned-seed
claim could be made. The harness now also pins at the first render-thread
SetHbao call, before frame recording. Replacement treatment
`c826c84a85564d7ca3b2176c735d2afa` captured successfully with the actual
seed in its quality manifest (native 880F9327...089, managed 9D64C46B...44D).
Its matching seeded/static pair has aligned HDR means 0.283862 / 0.283314,
median ratio 1.0. Mean absolute AO delta falls from the unseeded pair's
0.005532 to 0.003382; sun visibility delta remains 0.003232. The 19,083 pixels
with identical captured material/AO/sun inputs have HDR means
0.246767 / 0.246307 and median ratio 0.999437.
Artifact: `TestResults/lighting-hbao-stock-jitter-pinned-comparison.json`.

Separate source/GPU evidence identifies another unresolved edge contract:
stock PointHBAOBorder is float.MinValue, while native's border sampler is white.
A full flat-plane GPU control has no clamp-edge self-occlusion but gains false
edge occlusion under the former border path. It fails
`TestResults/lighting-hbao-border-before.log`.

The correction derives sixteen stock coarse-AO variants from authenticated
catalog inputs. Only the out-of-range depth fetch returns float.MinValue
explicitly; in-range sampling and AO computation remain stock. This preserves
core Vulkan portability without requiring custom-border-color device features.
The base module, source dependencies and transform identity are included in
compilation/cache validation; a changed dependency is rejected, and a cache hit
retains exact provenance. Native owns the additional sixteen stage pipelines
and uses them only for border-mode HBAO.

The flat-plane regression now passes, and the whole postprocess GPU suite
passes 237 checks with zero validation errors. All sixteen coarse variants,
clamp/border selection, independent blur reference, quality switching and
malformed-jitter checks run in this suite. Native CTest passes 15 tests with
one optional skip. Both managed targets build with zero warnings/errors.
Logs: `TestResults/lighting-hbao-border-{after,native-build,managed-build}.log`.
Native SHA256: `C4CAF5BD7F4BCFB0D34F22496148F1395E1D5C6B793B5FCAEED6B88C6C65B70D`.
The seeded-jitter treatment above intentionally predates this correction;
matched live border treatment `015d7cff72cc45b88e7f73531b601e14` completed,
unloaded and disposed at 211.7 seconds with zero validation errors. Against
the seeded/static DX11 reference its aligned HDR means are 0.283879 / 0.283329,
median ratio 1.0; mean absolute AO delta is 0.003380. This fixture's central
geometry is largely unaffected by the viewport-edge correction. Artifact:
`TestResults/lighting-hbao-border-pinned-comparison.json`.

## Same-frame postprocess diagnostics

Near-equal HDR means do not establish final-image equality. Asynchronous final
screenshots still contain localized differences, especially the translucent
construction preview. The private harness now captures the actual RGBA8 image
entering and leaving FXAA on each backend, together with the actual 208-byte
HBAO global constants. DX11 copies through the owning immediate context;
Vulkan copies completed owned targets and restores their sampled layouts.
The private capture completion gate requires both postprocess images. These
hooks make no production tone-mapping or FXAA change.

PostprocessGpuTests passes 239 checks with zero validation errors, including
actual pre-FXAA luma alpha, post-FXAA alpha, changed antialiased pixels and
repeatable completed-frame readback. Native CTest passes 15 tests with one
optional skip. Logs: `TestResults/lighting-fxaa-input-*.log`.
Native SHA256: `3BEF511FB083C169B48389E5B37451FD650FA665BBCA1C76E5A43889F5826665`.
DX11 run `61c3be3e25e34f5bac5359ed6c8be3e1` has captured both images and
HBAO constants. Matching Vulkan snapshot `034bfc4f899a4046a0599453debb838c`
completed its simulation/capture gates, unloaded and exited. Both postprocess
images are present in both captures and the quality/scene evidence matches.
Aligned HDR means are 0.283898 / 0.283339, median ratio 0.999829. All named
HBAO scalar/vector parameters match exactly; normal-matrix storage matches
apart from signed zero. Artifact:
`TestResults/lighting-fxaa-input-pinned-comparison.json`.

The pre-FXAA RGB mean absolute differences are about 0.9–1.0 byte levels;
post-FXAA differences are about 1.6–1.7 levels on the aligned subset. The
large preview discrepancy at (392,218) is not an isolated equal-input FXAA
failure: both input pixels are gray 141, but their neighboring GBuffer depths,
materials and pre-FXAA pixels differ substantially. DX11 outputs (33,35,31)
while Vulkan outputs (141,141,141). This locates a geometry/coverage mismatch
upstream of FXAA; it does not yet prove FXAA equality for identical images.

Actor LOD selection was implemented but default-off. A private opt-in treatment
`eb0a52c671944164ba30e6c17402101f` completed against the same DX11 control,
using `-StockActorLods`. The harness now records actual actor LOD enablement
and selection counters at capture. Actual state records 16 selected actors,
13 reduced actors, no active transitions and newer-actor views disabled.
Depth agreement rises from 64,552 / 67,336 shared foreground pixels (95.87%)
to 67,404 / 67,437 (99.95%). Depth/normal agreement rises from 52,900 to
59,283 pixels. Post-FXAA mean absolute RGB differences on that aligned subset
fall from (1.63,1.61,1.71) to (0.92,0.89,0.95) byte levels. The subsets differ,
so this is not an identical-mask effect-size estimate or a full parity claim.
Artifact: `TestResults/lighting-actor-lod-pinned-comparison.json`.

The matching newer-actor per-view treatment
`6ffc67e87d70409f873dc96ab62a2206` captured successfully with both LOD switches:
7 old selections (4 reduced) and 9 newer-actor view plans. Depth agreement is
67,403 / 67,437 shared foreground pixels (99.95%). Both final images were
reviewed. Artifact: `TestResults/lighting-new-actor-view-pinned-comparison.json`.
`SceneSubmissionGpuTests --lod-only` independently passes 158 checks with zero
validation errors, including old/new selection, transitions and actual
independent main, probe, cascade, spotlight and highlight routes. This focused
mode stops before the later bulk-model cases; the full suite retains its
previously established baseline failure. Logs:
`TestResults/lighting-actor-lod-focused-{build,before}.log`.

Both existing policies are now production defaults; explicit environment
values `SE_VULKAN_ACTOR_LODS=0` and `SE_VULKAN_NEW_ACTOR_VIEWS=0` retain the
diagnostic opt-outs. No selection algorithm, distance, threshold, transition,
geometry ownership or native ABI changed. Both managed targets build with
zero warnings/errors. The focused suite passes 159 checks with zero validation
errors after promotion, including actual default property selection. Logs:
`TestResults/lighting-actor-lod-default-managed-build.log` and
`TestResults/lighting-actor-lod-focused-after.log`.

Another confirmed contract still needs implementation: stock writes authored
LOD in GBuffer0 alpha, while current Vulkan captures write zero for every
pixel. Selecting the correct mesh alone does not establish that channel's
parity. Stock `MyRenderableComponent` writes `CommonObjectData.LOD`; stock
PixelTemplateBase reads `object_.LOD`, and GBufferWrite stores `lod / 255`.

## GBuffer LOD values and newer-pipeline marker

The retained selected-LOD regression fails before correction: the actor selects
LOD3 geometry but its submitted object constants still contain zero. The
production object writer now supplies the selected old-pipeline LOD. Cached
representations include both geometry and shader LOD identity; rigid/model
batch keys and native model templates retain that identity too. Restoration,
uncached submissions and independent view routing are tested explicitly.
Two actors sharing identical geometry/materials but different LOD values
remain separate through both rigid and model batching.

Stock GeometryStage2 has a different channel contract: MyGBufferRenderPass,
MyForwardRenderPass and transparent passes request GetPlaceholderObjectCB
with 255. DebugLOD.hlsl also explicitly recognizes that newer-pipeline marker.
The managed newer-actor path and native-pass placeholder/glass consumer now
preserve 255 independently of their selected geometry level. The initial
intermediate capture predates that distinction and is not final acceptance.
The first native-pass rerun exposed its mixed-glass consumer still using zero;
the production consumer was corrected, preserving the unchanged byte-equality
reference. The whole native-pass suite then passed 107 checks with core and
synchronization validation enabled and zero errors.

Focused scene tests pass 170 checks with zero validation errors. Stock GBuffer
tests pass 54, including production-writer RGB preservation, authored LOD3 and
marker255 through the actual pixel shader. Both managed targets build cleanly.
Logs: `TestResults/lighting-lod-channel-{before,after,managed-build,gbuffer,native-pass}.log`.
Intermediate private run `52fb9736d68349a4aee2883c84b685b0` captured under the
new production defaults without LOD switches: old LOD values are now present,
and shared-foreground alpha agreement rises from 49,433 to 63,790 of 67,437
pixels. Its newer actors still contain geometry LOD rather than marker255.
Artifact: `TestResults/lighting-lod-channel-intermediate-comparison.json`.
Corrected marker run `12be920c0fc148a5abc9d2bdc60d4d1d` completed its captures,
simulation, unload and controlled exit with zero validation errors,
using the production defaults without either LOD switch. Its actual state is
7 old selections (4 reduced), 9 newer-actor plans and no transitions. Native
SHA256 remains `3BEF511FB083C169B48389E5B37451FD650FA665BBCA1C76E5A43889F5826665`;
managed SHA256 is `C291508ECDE65D4857F82114544C6486C82BA6B6152E1C308205284A8437B27A`.
Shared-foreground LOD/marker agreement is now 67,348 / 67,437 (99.87%). All
3,558 stock newer-pipeline marker pixels match; Vulkan has 29 additional marker
pixels where stock sees old geometry. Of the other mismatches, 59 are stock
LOD2 versus Vulkan zero in the small (351,228)–(358,247) region; 57 of those
have aligned depth. One stock LOD1 pixel differs at a geometry boundary. These
are residuals to investigate, not excused by the high aggregate agreement.

Depth agreement is 67,404 / 67,437; depth/normal agreement is 59,283. On the
latter subset HDR means are 0.298860 / 0.298408, median ratio 0.999663, and
post-FXAA mean absolute RGB differences are (0.89,0.87,0.93) byte levels.
The final image was reviewed. The comparator now reports separate old/new
pipeline subsets and material-plus-LOD agreement. Artifact:
`TestResults/lighting-lod-marker-pinned-comparison.json`.

At that stage the separate indexed-GPU shared placeholder and background clear
alpha still needed their own correction/verification, addressed below.
Full lighting/material equality remains open.

## Canonical indexed placeholder and background clear

The indexed path copied the first actor's OLD compatibility object constants
into the shared NEW placeholder. A control with a nonidentity matrix, tint,
emission, custom alpha and LOD17 fails the actual stock color comparison under
the previous native DLL. Stock's GetPlaceholderObjectCB instead supplies zero
nonvoxel data, identity rows, white color multiplier, zero key/emission/alpha,
and marker255. The native path now builds exactly those canonical 128 bytes;
actor compatibility inputs remain in their separate per-actor storage. Both
mapped and staged/device-local placeholder publication retain existing waits,
flushes and barriers.

Both main-world and standalone stock GBuffer clears now write alpha1 for
attachment0, matching stock MyGBuffer.Clear and the live DX11 background.
The background regression fails with the prior zero clear and passes afterward.
An internal completed-frame readback now also exposes raw GBuffer0 bytes as
selector4, using the existing staging/restore path and exact RGBA byte count.

The focused indexed test passes 13 checks with validation enabled/errors zero:
incompatible first-actor data no longer changes NEW stock color/depth, and
material RGB plus marker match the independent native-pass reference exactly
in both slots. GBufferGpuSmoke passes 55 checks, PostprocessGpuTests 239, each
with zero validation errors. Native CTest passes 15 tests with one optional skip;
both managed targets build with zero warnings/errors. Native SHA256:
`FEFA6D096257751D02B995E7FEA33FA523BE67AD591864605D694AEBC91167F7`.
Logs: `TestResults/lighting-{placeholder,background}-*.log`.

At that stage the full indexed raster suite remained red at its later resident source pose
comparison for epoch802. The original HEAD test, rebuilt against the current
managed library and previous native DLL3BEF511F...6665, reproduces that same
failure before this native correction. That control is preserved under
`TestResults/LightingPlaceholderBaseline` with
`lighting-placeholder-baseline-{build,before}.log`. The initial attempt without
the process-start GPU opt-in failed admission and is not regression evidence;
the valid control sets SE_VULKAN_GPU_OPAQUE=1 before process creation.
`--placeholder-only` isolates the new placeholder checks; it does not certify
the later resident pose/appearance cases. That broader failure is investigated
and resolved in the actor-lifetime section below.

Live run `6516b5e8ddad4c6cac2196a939d4db7f` completed its producer/simulation
gates, captures, unload and controlled exit with zero validation errors using
the corrected native DLL and production LOD defaults. Its actor manifest records
model/selected representation, shader LOD, visibility, skin/static ownership,
world bounds and the actual view-projection offset alongside the stage dumps,
to identify the localized remaining LOD2-versus-zero region. Full-frame alpha
agreement is now 307,111 / 307,200 pixels; the remaining 89 foreground
mismatches are unchanged. Aligned HDR means are 0.298864 / 0.298314 with median
ratio 0.999455. Artifacts: `TestResults/lighting-placeholder-pinned-comparison.json`
and `lighting-placeholder-actor-candidates.json`.

Projection uses the actual captured view-projection offset, camera position,
world AABB corners and negative-height viewport. The smallest visible candidate
over the LOD2-versus-zero region is actor51, SE_astronaut.mwm, with 81 skin
matrices, shader LOD0, and authored distances7/10. Its bounds project to roughly
(341,212)–(369,263). These AABBs are candidate attribution, not exact pixel
ownership proof. Source VulkanActorLods explicitly excludes SkinMatrices,
which makes the skinned-actor LOD contract the next concrete investigation.

## Skinned actor LOD selection

Stock MyRenderableComponent rebuilds every authored LOD with the live skinning
component and each submesh's BonesMapping. Character skinning does not opt out
of its regular old-pipeline distance selection. Vulkan already remaps each
mesh into owned 60-matrix palettes, but its selection gate explicitly excluded
all actors with SkinMatrices, leaving the live astronaut at LOD0.

The installed astronaut regression reproduces that exclusion before correction.
The gate now includes skinned actors while preserving the distinct static-group
ownership path. No skeleton messages, palette layout, shader, distance policy or
native ABI changed. The regression selects actual LOD2 geometry, compares it
with direct authored LOD2 under a nonuniform live bone pose, proves a changed
pose changes visible pixels, verifies both frame slots match the current pose,
and exercises the two-mesh transition back to the near base geometry.
The installed fixture uses more than 60 global bones and multiple independently
remapped draw palettes, asserted after actual LOD rendering. All 179 focused
scene checks pass with zero validation errors;
both managed targets build with zero warnings/errors. Logs:
`TestResults/lighting-skinned-lod-{before,after,managed-build,test-build}.log`.

Live treatment `0ea7659129ed44f282b830aec6bb4628` completed its producer and
simulation gates, captures, unload and controlled exit with zero validation
errors under the normal
LOD defaults against the existing pinned DX11 control. Native SHA256 remains
FEFA6D09...67F7; managed SHA256 is
`B13C7E2DA60C9D923C4D5B53308E8F436048C501AE5DB1CCA26166BC5A05BF86`.
Its manifest confirms astronaut actor51 selects SE_astronaut_LOD2.mwm and
shader LOD2 with 81 live matrices. The 59-pixel interior mismatch is removed:
LOD/marker agreement is 67,407 / 67,437 shared foreground pixels, and full-frame
alpha agreement is 307,170 / 307,200. Of the remaining 30 mismatches, 28 fail
depth agreement too; two are at the old/new geometry boundary with aligned
quantized depth. Artifacts: `TestResults/lighting-skinned-lod-pinned-comparison.json`
and `lighting-skinned-lod-alpha-residuals.json`.

## Recreated actor lifetime in retained native-pass streams

The broader indexed pose-update failure was traced to its independent native-pass
reference rendering stale geometry. The old and indexed GBuffer dumps differ
in coverage by 56 pixels after the requested vertical movement, rather than
slightly different lighting. Source retained streams use owner IDs plus an
actor depth_epoch; registration restarted that epoch at1. Removing an actor and
recreating its owner within the same family therefore reused retired transform
and key-color bytes. Static spotlight signatures had the same collision.

A native CPU regression reproduces stale pose/appearance on a new generation
handle with the same owner ID. Scene-local commit epochs now increase across
all registrations and updates, so both cached streams and static spotlight
signatures recognize the new lifetime. Normal stable-frame reuse, camera-only
patches, selection state and public ABI remain unchanged. The regression now
also verifies the recreated owner invalidates prior static spotlight depth.

Canonical native CTest passes15 tests with one optional skip. The full indexed
raster suite now passes all268 checks, including epoch802 and later pose,
appearance and sparse-material cases; the native-pass GPU suite passes107.
Both report core/synchronization validation enabled with zero errors. The
placeholder regression also passes13 checks with device-local working buffers,
covering the staged placeholder upload path. Logs:
`TestResults/lighting-recreated-owner-{before,native-build,raster,pass,placeholder-device-local}.log`.
Native SHA256: `81AE6CCF1F54AF47002E7333ADD5114504CBEAD5D01B829CD6D3B61BE864D93F`.

The full scene-submission suite was also rerun with this DLL. Its later,
previously established bulk-model participation assertion still fails: main
pixels and ordered owner/LOD/part selection match, but that fixture does not
enter the requested native model representation. This is separate from the
resolved indexed pose/control failure; the whole scene suite is not reported
green. Log: `TestResults/lighting-recreated-owner-full-scene.log`.

## Authored mesh precision

Stock old and new mesh pipelines copy authored packed normal bytes into their
vertex streams. Vulkan decoded and repacked those normals, introducing integer
truncation: the installed shelf regression differs at 16,410 / 77,723 vertices
before correction. Imported meshes now preserve the original normal bytes;
runtime meshes without an authored packed stream retain generated packing.
All four installed model controls now match the authored bytes exactly.

Both stock pipelines also repack PatternScale-adjusted UVs into HalfVector2.
The four installed controls use scales1 or4 and did not expose the float-only
Vulkan difference. A read-only tag audit examined22,923 installed model files
and found no nonbinary scales among successfully read assets (nine legacy or
malformed inputs could not be parsed). This does not explain the current stock
scene's material residual. A test-owned clone of the actual square-plate geometry
with only its indexed PatternScale changed to3 provides an independent import
witness: all nine UVs fail before preserving stock half rounding and pass after.
Installed source assets are never modified. The full focused scene/LOD route
now passes191 checks with core/synchronization validation and zero errors.
Both managed Release targets build with zero warnings/errors. Logs:
`TestResults/lighting-normal-packing-{before,after,build}.log`,
`lighting-scaled-uv-{before,after,build}.log`, and
`lighting-import-precision-managed-build.log`.

Normal-only live treatment `32a0f109219749ce96d571f2dcb84385` completed the
producer/simulation gates, unload and controlled exit with zero validation
errors. Managed SHA256 is
`F0CCAB51F94E7B5F6162801656FAA1B203E094E1D785D42B89151EAB7126EF86`;
native remains81AE6CCF...D93F. The paired images were reviewed against the
pinned DX11 control. Shared foreground remains67,437 pixels, depth-aligned
67,407, and depth/normal-aligned59,305. Mean HDR luminance on the latter subset
is0.298780 /0.298341, median ratio0.999678. This small change is not claimed
to resolve the remaining lighting or texture-filtering differences.

The comparator now separately reports bit-exact depth, all four decoded GBuffer
channels, and AO/sun inputs without changing historical tolerance subsets.
The normal-only run has26,859 pixels with all those captured inputs bit-exact;
HDR RGB mean absolute deltas are0.001511/0.001172/0.001234 and maximum deltas
2.375/1.0625/2.25. Before-tonemap HDR includes transparent/flare contributions,
so identical captured opaque inputs do not isolate every lighting contribution.
Artifacts: `TestResults/lighting-normal-packing-pinned-comparison.json` and
`lighting-strict-hdr-outliers.json` (the latter uses the preceding skinned run).
Flare occlusion/state attribution is the next active investigation; whole
lighting/PBR parity remains unproven.

## Flare query coverage and normalization

Diagnostic paired runs DX11 `02b02f0ef9754674ae6ec31948ac9eed` and Vulkan
`78be3be3077c472dadd7e5bbcb154886` capture actual glare descriptors and query
factors in `flares.xml`. They completed their producer/simulation gates and
controlled exit. The two colored lights have stock factors0.162752986 and
0.211365581 versus Vulkan0 and0.125. Their projected centers are(448.24,241.88)
and(282.59,316.77), on the largest recorded HDR outliers. This identifies a
query contract difference without claiming it accounts for every HDR delta.

The installed stock OcclusionQuery shader rasterizes a camera-facing quad with
side QuerySize and a shifted center. DepthTestReadOnly uses strict Greater for
reverse depth. MyFlareOcclusionRenderer normalizes passing sample counts using
the projected area at radial camera distance, which differs from the actual
quad rectangle area away from the view axis. Reconstruction from the captured
DX11 depth and actual matrices counts5 and11 passing pixels; division by actual
LastVolumeSquared5.97195339 and13.9481611 reproduces both captured factors to
floating-point precision. Artifact:
`TestResults/lighting-flare-stock-raster-reconstruction.json`.

The old GPU4x4 query also fails a controlled partial-coverage witness: an8x8
quad with24 blocked pixels returns0.5 rather than0.375. This is retained in
`TestResults/lighting-flare-raster-audit.log`. Native queries now count every
covered pixel cooperatively, without depth epsilon or repeated clamped border
samples. A separate32-byte raster-query entry carries NDC geometry and stock
normalization; the old pixel-radius entry is preserved. Mapped buffers,
completed slots, barriers and readback ownership remain the existing path.
The scene now sends the stock half extents (the old radius was twice as large)
and radial normalization. Temporal query cadence/smoothing remains open.

Canonical native build/CTest passes15 tests with one optional skip. The full
transparency GPU suite passes159 checks with zero core/synchronization errors,
including partial coverage, independent normalization and viewport clipping.
Logs: `TestResults/lighting-flare-raster-{native-build,test-build,gpu}.log`.
Private stage is`TestResults/LightingFlareRasterAssets`; native SHA256 is
`F8385CA0E695113772716648A798F5830D691F95295CC10DC9082ED4A66DE006`, shader
SHA256 is`FC99100A345C51FA12BF21ED0221A8F56EE99C1B4F1EAEB5739EEBA3A564F898`.
The fixture now stages current native-build SPIR-V with its DLL, preserving the
normal asset deployment. Live query-only treatment
`41257e6008164c34a6eab0df08e5ed08` completed the producer/simulation gates,
unload and controlled exit with zero validation errors. Its two colored-light
factors match stock within1.14e-7 and5.9e-8. The largest strict-input HDR
outliers remain2.375/1.0625/2.25, so this is not their complete explanation.
Artifact: `TestResults/lighting-flare-raster-pinned-comparison.json`.

## Additive-top billboard depth contract

Stock MyBillboardRenderer.RenderAdditiveTop sets IgnoreDepthStencil, attaches
only LBuffer and passes null depth into the billboard renderer. Vulkan reused
the depth-tested additive-bottom pipelines and supplied opaque depth to its
soft-particle variant. The focused regression fails because a top billboard
behind opaque geometry is removed, contrary to the stock path.

Separate additive-top pipelines now disable depth testing and omit the depth
attachment. Top packets select the stock shader variant without soft fade,
matching the null-depth input's unoccluded result. Bottom, standard/OIT and
LDR/PostPP selection retain their separate behavior. Both HDR formats have
owned pipeline pairs, quality switching and cleanup. No new sampled image or
synchronization path is introduced.

Canonical CTest again passes15 tests with one optional skip. Full transparency
passes163 GPU checks under each HDR format with core/synchronization validation
and zero errors. Checks include the previously failing top-over-opaque case
and equality of top soft/nonsoft variants. CPU particle/flare bridge passes11
checks including early rejection of a missing new native raster export.
Logs: `TestResults/lighting-top-billboard-{before,native-build,test-build,gpu,normal-hdr-gpu}.log`
and `lighting-flare-export-test.log`.
Native SHA256 is`E482027AA1C8E0DFB59001B8B6104BD6FCFE77A8AE9B69409A7F3152F1082B79`;
private stage is`TestResults/LightingTopBillboardAssets`.
Live treatment `77047667a59d447793d6a1ea92dc8924` completed the producer/simulation
gates, unload and controlled exit with zero validation errors. Paired images
were reviewed. Maximum HDR deltas over the bit-exact captured-input subset
fall from 2.375/1.0625/2.25 to 0.25/0.25/0.25; mean RGB absolute deltas fall
to 0.001315/0.001116/0.001069. The additive-top depth correction resolves the
large light-adjacent outliers. Artifact:
`TestResults/lighting-top-billboard-pinned-comparison.json`.

## Rotated flare geometry, cadence and solar ownership

The production rotated-glare path failed an installed-helper regression under
a rolled camera: Vulkan derived its basis from camera up, while stock uses
MyUtils.GetBillboardQuadAdvancedRotated's world-up projection and pole handling.
The renderer now uses that same API-neutral helper. Logs:
`TestResults/lighting-rotated-flare-{before,after,test-build}.log`.

Managed query state now matches the stock minimum/random retry intervals,
half-minimum first-visible retry, interpolation over the scheduled interval,
immediate fully blocked result, and zero-frequency behavior. Request identities
are unique across scene lifetimes. Repeated completed-slot results cannot
satisfy a later in-flight query, and deleted owners release temporal/pending
state. The CPU bridge passes 19 checks; the native-slot scene regression proves
actual completion/retry, fresh identities, stale-result rejection and removal.
Logs: `TestResults/lighting-flare-temporal-{test,scene,managed-build,harness-build}.log`.

Combined live treatment `373d8551439a4c26a372359e0edf04fb` completed and disposed
with zero validation errors. Its colored-light factors still match stock within
1.14e-7 and 5.9e-8, and captured Last/Next values are settled. Maximum strict-input
HDR RGB deltas are now 0.25/0.25/0.0625. Managed SHA256:
`875F53CFD257CDB488834BFF0E751689B8161AFA9908C4BEA1D65CD0DBBCB350`.
Artifact: `TestResults/lighting-flare-temporal-pinned-comparison.json`.

The celestial ownership flag previously filtered Directional glare, suppressing
local reflectors/character lights while leaving the actual Distant solar light.
The host now captures MySector's authoritative solar render ID after its sun
update and clears it on unload, matching Anomaly's DX11 ownership boundary.
Only that exact actor is suppressed. A failing-before production regression
now proves solar suppression while preserving a directional local light and
an unrelated distant light. No new public Anomaly control was introduced.
Live treatment `ea9e74c49088419c974c2a53e5cb592d` completed with zero errors;
its diagnostic captured the actual solar owner ID1. Managed SHA256:
`24405799DCEBD29799EDC26BE73FC87B703551D0B47DC910FF043AB2E154A942`.
Logs: `TestResults/lighting-solar-glare-{before,after,build}.log`.

The center-only viewport/depth cull was also removed: stock can emit a partially
visible billboard or ghost with an offscreen light center. The independent
edge regression fails before and passes after this change. Native rectangle
clipping and raster clipping own visibility. The focused scene/LOD route now
passes 202 checks with zero validation errors. Logs:
`TestResults/lighting-flare-edge-{before,after,build}.log`.

## Remaining low-light specular residual

The remaining HDR differences cannot all be attributed to R11G11B10 rounding.
An exact format-code audit finds differences up to 98/97/45 encoded RGB steps
on the strict-input subset. At pixel (191,247), for example, stock HDR RGB is
(0.005737,0.006714,0.004272) versus Vulkan (0.015625,0.018066,0.010986).
Depth, captured material, sun visibility and the complete bilinear screen-AO
footprint agree. The pixel is fully metallic and shadowed. This points the next
investigation at ambient/specular inputs, rather than accepting near-unity mean
luminance as full parity. Artifacts: `TestResults/lighting-hdr-code-steps.json`,
`lighting-hdr-step-outliers.py`, and `lighting-ao-footprint.py`.

New diagnostics copy original/final close/far probe cubes from the actual
completed lighting frame, preserving all packed R11G11B10 faces/mips. DX11 uses
its borrowed engine device; Vulkan uses the existing explicit readback with
exact packing recovery. No image is resized. Compare-LightingProbes.py validates
quality/scene/shape/format and reports all faces and levels without an automatic
parity verdict. A known (1,2,4) packed-format control confirms decode/order and
zero-delta behavior. DX11 run `5d979e55796d4ec8946f813d19dec432` delivered all four
512-square, nine-level cubes and completed its gates/controlled exit. Vulkan
run `9db1e1d67b6845759ad1c76f864a8b56` completed. Its original cubes were sampled
during a staged refresh: black working faces are not evidence of missing
published lighting. Diagnostics now record working stage/busy/cycle metadata.

## Probe sky view and reflector cookie corrections — 2026-10-18

An independent six-axis colored sky control exposed an extra CPU transpose in
the probe sky view constants. The stock shader already transposes that view;
the native row-major upload must preserve it. The two nonsymmetric X-face
views were exchanged. After removing that transpose, all six GPU face controls
pass; the complete probe suite passes 414 checks with zero validation errors.
Live run `64e3d318ec7145218f3a2d4ce11ea047` completed; published X-face sky MAD
drops from approximately 0.023 to 0.0001. This did not resolve the dark metallic
HDR residual. Native SHA256:
`D6BED8171B58ADF69E8EE3C7BA6BFAAE4AD821D016873FA072175FA01E833BFB`.

Stock CUSTOM reflector cookies use sRGB views where the format supports them.
Native spotlight submission requested linear views. It now requests sRGB;
floating RG16 DDS data remains linear because that format has no sRGB view.
Failing-before actual scene/cache and floating-cookie controls now pass:
203 focused scene/LOD checks with zero validation errors, and 13 texture
contract checks. Live run `2cdb13ecaef044be90d1c1413719544c` completed its
capture. Managed SHA256:
`45260864B1784FA66285DB36C123549739B8FE1081593B0970EF3E1F01054391`.
Its dark pixel (191,247) remains (0.015869,0.018555,0.011230), versus stock
(0.005737,0.006714,0.004272). Matching aggregate means do not establish parity.
Logs: `lighting-spot-cookie-{before,after,build}.log`,
`lighting-float-cookie-{before,after}.log`.

## Close/far probe consumer investigation

`LightingParityGpuTests --captured-ambient` replays stock shader ambient terms
on DX11 WARP using each backend's actual frame/material/AO/depth/sun inputs,
published probes and installed single-mip RG16F BRDF LUT. The LUT lookup alone
uses explicit LOD zero because this is a compute diagnostic. Local lights and
later composition are excluded. Captured native frame matrices use row-major
compilation; captured DX11 matrices use column-major compilation.

At (191,247), the DX11 replay gives (0.005762,0.006766,0.004281), closely
matching actual stock HDR. Replaying the native published probes in their
declared roles gives (0.005846,0.006839,0.004286), while actual native HDR is
approximately 2.7 times brighter. Deliberately swapping the native probes gives
(0.016714,0.019539,0.011702), reproducing most of the excess. The other three
tracked dark pixels show the same pattern. BRDF lookups match exactly.

The owning source confirms reversal: `Common.hlsli` assigns t11 to close and
t17 to far, but both `world_light_set` and glass submission bound final[1]
(far) to t11 and final[0] (close) to t17. Both consumers now bind the declared
roles. A production main-light descriptor regression uses distinct published
geometry/sky cubes and a diagnostic split shader; the prior binary visibly
returns the near geometry from the far slot and fails. The corrected native
build passes 15 tests with one optional skip; the full probe GPU suite passes
419 checks and transparency passes 163 checks, all with zero validation errors.
Native SHA256:
`7848EB670719B24A66F77C6D62AC7D5FDE80AE67CFF976568FDA8AC5F18BD290`.
Matched live treatment `f335f6ef2a984fb5a9ad2eeefb1d5350` delivered its pinned
capture with zero validation errors. Pixel (191,247) now matches all three
stock HDR channels exactly. The other tracked dark metallic pixels differ by
at most 0.000244 per channel. The 2–3x residual is resolved; full equality is
still not established. The strict material/depth/sun/AO-footprint subset has
25,963 pixels, HDR RGB MAD 0.000856/0.000636/0.000755 and max deltas
0.125/0.0625/0.03125. Off captures were visually reviewed beside the stock
reference; remaining smaller probe and final-image differences need analysis.
The full fixture completed, unloaded and disposed at 09:12:29 UTC. Artifacts:
`TestResults/lighting-captured-ambient{,-swapped}.log`,
`TestResults/CapturedAmbient`, `lighting-probe-binding-before.log`.
After logs: `lighting-probe-binding-{native-build,after,transparency}.log`.
Comparison: `lighting-probe-binding-pinned-comparison.json`.

The follow-up consumer audit found the same wrong close binding in lit
billboards and GPU particles. Decompiled `MyBillboardRenderer` binds
CloseCubemapFinal at pixel t11 (and far at t17); `MyGPUParticleRenderer` binds
CloseCubemapFinal at vertex t11. Both native consumers used final[1]. They now
use final[0]. This follows the same stock role contract, without changing
sampling or probe production. The follow-up build passes the native suite,
419 probe GPU checks and 163 normal-HDR transparency checks, with zero
validation errors. The broader static-window live comparison is pending. Native
SHA256: `C44E45211E4B61BB77C3BE973DE2C696FA7B77A6BCDAEFE7FC3B459B4AD3BD36`.
Log: `lighting-probe-consumers-native-build.log`.
GPU logs: `lighting-probe-consumers-{gpu,transparency-normal}.log`.

The static-window comparison reuses the immutable baseline from
`5e63d58eab9b4662ad878ce457429f7e`, containing three Window1x1Flat blocks.
It omits the dynamic transparency fixture workflow so unrelated new particles,
weather and decals do not obscure the glass material comparison. Prepared
DX11 `08e1810e6ffa41caabe940b47fe2b2eb` and Vulkan
`e93f96d5ed464e4da364771cd5c8fa1c` have identical sector bytes, SHA256
`e59163925f4fe0067c50415bf9d4fee5a8772a6b9efacd368681b3104bc58181`.
Both completed their fixture checks and controlled exit; Vulkan disposed at
09:21:27 UTC. The windows expose a real unresolved dark-pane defect, clearly
visible in the paired off screenshots. The strict matched-input subset HDR
MAD is 0.03255/0.03072/0.02106, max 11.15625/5.25/2.09180. These are not a
parity pass. Compare these captures to each other, not to the basic scene
without windows. Artifacts: `lighting-static-glass-pinned-comparison.json`,
`lighting-static-glass-roi.json`. Stock material binding, material constants,
face-culling policy and OIT need evidence before any glass fix is selected.

The corrected basic pair still exceeds pure target-format rounding: among
25,963 strict input/AO-footprint matches, 84.2% are within one packed RGB code,
89.3% within two, and 2,768 pixels exceed two. Max encoded RGB steps are
15/16/9 (previously about 100/100/47). This is improvement, not equality.
Artifact: `lighting-hdr-code-steps-binding.json`. Published close-probe mip5
RGB MAD is 0.00139/0.00152/0.00111, versus approximately
0.000081/0.000087/0.000019 in far mip5. The remaining difference is concentrated
in scene-containing probes, but its cause is not yet established. Do not
attribute it to filtering or moving actors without a controlled comparison.

## Retaining completed probe filtering work

Displaying the published close probes exposes faint geometry from earlier
camera positions in the native sky-facing faces. Decompiled
`MyEnvironmentProbe.PrepareEnvProbe/FinalizeEnvProbes` seeds work from final
once on entering timed mode (`m_state < 0`), via `m_initWorkProbes`. Native
instead copied final into filtered at every timed cycle's stage zero. This
overwrites completed source radiance with its lagging publication repeatedly.
Startup already initializes both cubes identically; the recurring reseed is
removed. Completed filtered faces persist until their next face processing.

The stopped-clock GPU control creates distinct completed filtered and published
images, then starts another timed refresh. It fails before because the source
is overwritten. After the fix, the suite passes 421 checks with zero core/sync
validation errors. A prior test incorrectly asserted publication remained
unchanged until that face's refresh, mirroring the reseed bug. It now observes
the filtered source instead; publication legitimately continues blending the
previous completed source while the next refresh proceeds. Native tests pass
15 with one optional skip. Native SHA256:
`8D95A9B14621F540397A243B54279C6B2525587E12502E4231C5428B1662B014`.
Logs: `lighting-probe-reseed-{before,after,native-build}.log`.

Live static-window diagnostic run `2a48638530a14faa8c6a137090d9d4b7` delivered
its capture with this native fix. Close-probe mip5 RGB MAD against the matched
glass DX11 reference is now 0.000451/0.000392/0.000328.
For the same window scene before this fix, close mip5 MAD was
0.001300/0.001460/0.001044. Far mip5 improves from
0.0000834/0.0000861/0.0000300 to 0.00000234/0.00000334/0.00000246.
Before artifact: `lighting-static-glass-probes-before.json`; after:
`lighting-probe-retained-glass-probes.json`. The dark glass panes
remain (strict HDR MAD approximately 0.03252/0.03069/0.02105). This separates
the probe lifecycle correction from the unresolved glass defect.
New `LightingGlassDiagnostics` records registered stock
material definitions and actual native glass packet constants/texture cache
identities, to identify the dark-pane defect without guessing. Its source
build succeeds and runtime `glass.xml` was delivered. The actual native window
draws use the registered SquareWindowDirtInside_ca sRGB color texture and linear
Chrome_ng gloss texture, plus the expected distinct inside/outside constants.
No guessed texture substitution was made. The fixture completed and disposed
at 09:30:19 UTC.
Unlaunched older prepared runs `be68cb617e234b268628ec8ba474a512`,
`3a0808b0d4df4e8a9d474b04a18fa4a0` are not evidence of the latest treatment.

## Glass material face-culling investigation

Stock `MyTransparentRenderPass` selects back-face culling when
`material.TriangleFaceCulling` is true; its object-builder default is true.
Native glass always disabled culling. The captured inside/outside window
materials both explicitly report True; drawing both opposite surfaces also
includes the outside material's high alpha on the wrong side.

The managed packet now carries that material policy in glass geometry.flags
bit0. Native normal/decal-depth glass each have owned culled and two-sided
pipeline variants, selected per draw and destroyed with the transparency state.
Legacy callers default to two-sided behavior; production scene submission
passes the actual registered material policy. No packet size/export changes.
The regression renders both windings with and without the flag; the previous
binary renders both and fails. Clockwise front-face selection follows the
existing stock raster pipeline convention; the test's initial labels for its
source triangle winding were corrected. The native suite passes 15 with one
optional skip. Transparency passes 169 checks in each HDR mode with zero
validation errors. The matched live treatment is required before claiming the
dark-pane defect is fixed. Logs:
`lighting-glass-culling-{before,after,normal-after,native-build}.log`.
Native SHA256: `0E97F717D1BD508BC5DDC2801FEE82AC2C635234AFA86320ECE6F2B438C5F409`.
Managed SHA256: `62D473C5AE8FFD5A2EE2BC9B04CC73ED0147BB4AA07D2249383271E3EFDC6636`.
Live run `082f5f59bdbc4074be96272e66207385` delivered its matched capture from
the same static window seed. All seven submitted glass packets record flags=1
in `glass.xml`. The panes now visually match the clear stock panes; paired off
screenshots were reviewed. In fixed window ROI [390,226,477,319], RGB HDR MAD
falls from 0.14317/0.11605/0.06782 to 0.00344/0.00313/0.00263. On 26,171
strict input/AO-footprint matches, full-scene HDR RGB MAD is now
0.000858/0.000598/0.000755, max 0.125/0.0625/0.0625. The dark-pane defect is
resolved; remaining numeric differences still require investigation. These
figures do not establish complete renderer equality. The full fixture completed
at 09:36:05 UTC and disposed at 09:36:34 UTC, with validation enabled/errors=0.
No game/Pulsar process remains. Artifacts:
`lighting-glass-culling-{pinned-comparison,roi,probes}.json`.

Next acceptance work remains the smaller matched-input HDR/probe residuals,
plus broader lighting/material coverage. The corrected opaque and static-window
captures are evidence for their represented paths, not proof of every renderer
feature. Normal deployed assets remain unchanged and this work is uncommitted.

## Locating the remaining HDR contribution

Stock ambient/direct shader replay over the latest static-window pair produces
much closer results than the actual HDR comparison on some shadowed pixels.
Outside the fixed glass region, 9,587 strict input matches are explained by the
stock no-local-light replay within one packed code. On that subset the replay
cross-input RGB MAD is 0.0000181/0.0000132/0.0000060, while actual cross-API
HDR MAD is 0.000454/0.000312/0.000124. Native actual versus its own captured-input
replay still differs by up to 15 codes. This does not identify the cause: local
or subsequent composition is excluded from the replay. It does show that
remaining captured probe differences alone do not explain all of the residual.
Artifacts: `lighting-remaining-{ambient-replay.log,replay-audit.json,frame-input-deltas.json}`.
The repeated frame audit still finds matching meaningful light/fog scalars;
matrix ULP differences and intentional world-offset/tile-list representation
differences remain as previously documented.

Optional native stage diagnostics now retain HDR after directional/ambient,
point and spot passes, plus direct completed-HDR readback. They default off.
Capturing splits dynamic rendering around exact same-format copies; therefore
GPU controls explicitly compare HDR bits and presentation with capture on/off,
isolate an actual additive point contribution, reject disabled stale reads and
exercise resized snapshot ownership. Both HDR modes pass 183 checks with zero
core/sync validation errors. Native tests pass 15 with one optional skip.
Logs: `lighting-stage-diagnostics-{native-build,hq-gpu,normal-gpu}.log`.
Native SHA256: `D9176DC4E171CD8A4DCD7DD30825D401D004F0E67225C93B3C1354393560A8D2`.
Managed SHA256: `AEE1E8BDDB215BC6FF2DF3E719DEA39094E5B76BBBD67631B17F80A5B744BC2D`.
Live run `2f395c76e4a84ba78b0865f42f7deb07` completed from the same window
seed at 09:53:26 UTC and requested controlled exit at 09:53:46 UTC; the engine
log closed at 09:53:47 UTC and its process is gone. The capture reports zero
validation errors. Completed-HDR readback is bit-identical to the pre-tonemap
guest capture. Tracked residuals already occur after directional/ambient:
pixel (200,243), for example, is RGB 0.012085/0.014038/0.010010, versus
captured-input stock WARP replay 0.013583/0.016060/0.011703. Points, spots and
later composition leave this pixel unchanged. Pixel (216,255) is already
0.001602/0.003906/0.001678 before locals versus replay
0.001409/0.003609/0.001532; the point pass only adds 0.0000305 to red.
Artifact: `lighting-stage-contribution-audit.json`.

Changing only the replay cube sampler from anisotropic to trilinear produces
bit-identical tracked results (`lighting-stage-ambient-linear-cube.log`), so
that control does not explain the discrepancy. All 22 dependency hashes of
the authenticated native LightDir module match the current installed sources.
No product lighting adjustment follows from these exclusions. The next
diagnostic captures FP32 normal/view, actual probe and BRDF samples, diffuse,
direct and recomposed fogged output through the actual native light descriptor
set. It is optional, and requires separate no-perturbation and lifetime checks
before the live result can be trusted.

The optional seven-target transport now builds and passes 187 transparency
checks in HQ HDR and 189 in normal HDR, with zero core/synchronization validation
errors. Both runs preserve exact rendered HDR bits and presentation, read all
seven finite covered-surface outputs and agree with actual packed directional
lighting. Normal HDR additionally checks both resize/rebuild directions after
those assertions were added. Initial depth assertion expected the untransformed
mesh depth 2; the actual test draw translates it forward by 0.5, so the assertion
was corrected to the authored transformed depth 1.5, without shader changes.
Native tests pass 15 with one optional skip. Logs: `lighting-terms-{native-build,
managed-build,harness-build,hq-gpu,normal-gpu}.log`.
Private native SHA256:
`06DAA8D34983B351B6B1BC0AEDB0D049AA84F6D2FDECA512741E453D84DF4A4F`.
Managed SHA256:
`B0BA7FB6AA521E8A12BE0CF78546D13B8F2F198E22AD2DA592628B5F259D4BB2`.
Live run `126f9523abd94c0eb63cf97f98ebb588` used that immutable private
pair and the same static-window seed. It completed at 10:14:50 UTC, requested
exit at 10:15:10 UTC and disposed at 10:15:11 UTC. Its process is gone. The
capture reports validation errors=0 and bit-identical end/pre-tonemap HDR.
`lighting-terms-live-comparison.json` compares its actual bound-input FP32
terms to the stock WARP replay. Normal/view vectors agree within 3e-7; gloss
and requested probe mip levels match exactly. Diffuse/direct terms are close.
The main tracked discrepancy is the specular cube sample: at (216,255), native
RGB is 0.014984/0.017438/0.011850 versus WARP
0.012746/0.014887/0.010282 at the identical LOD 4.482353. At (200,243), native
is 0.014285/0.016421/0.011831 versus WARP 0.015960/0.018684/0.013599 at LOD
5.541177. BRDF filtering also has smaller half-precision differences. This
locates the sampled residual without relying on local-light speculation.
The normal deployed native asset remains unchanged.

## Explicit cube LOD filtering

Microsoft's [sample_l contract](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sample-l--sm4---asm-)
states that address derivatives are ignored and anisotropic samplers behave
isotropically. A focused hardware control samples identical authored cube
bytes with identical directions and explicit fractional LODs, across six faces
and 6,144 cases. DX11 WARP anisotropic/linear results are bit-identical; Vulkan
anisotropic versus linear has mean absolute RGB delta 0.08595 and max 0.32401.
The native anisotropic-versus-WARP mean is 0.08501, versus 0.001144 for native
linear. Validation is enabled with zero errors. Before evidence:
`lighting-cube-sampling-before.json`, `lighting-cube-sampling.log`.

`Dx11CubeLodAdapter` now changes only explicit-Lod cube operations using stock
s3 on an authenticated executable copy. It selects stock isotropic s2 for those
operations, creates separate combined image/sampler instructions when needed,
and retains original s3 for implicit and explicit-gradient samples. If s2 was
optimized out, its sampler variable and SPIR-V 1.4 entry interfaces are added.
Catalog bytes/hashes remain authoritative and unchanged. All 1,401 catalog
runtime modules pass SPIR-V validation; 74 are affected. The audit allows only
the optional s2 descriptor addition, preserves existing descriptors/stage
interfaces/capabilities and checks idempotency. Particle and decal layouts
also need to accept/bind that additional standard sampler. The GPU filtering
control, affected render paths and matched live treatment remain acceptance
requirements for this correction.

The corrected control passes all 6,144 cases with exact native agreement
between s3 explicit LOD (adapted) and isotropic sampling. The explicit-gradient
anisotropic/linear mean delta remains 0.22512, and a 2D explicit-LOD module is
byte-unchanged. Bypassing only this adaptation reproduces the previous error.
Both controls have validation enabled/errors=0. The residual linear versus WARP
RGB mean is 0.001144 (max 0.006427) on the authored high-frequency RGBA8 cube;
the control does not claim cross-API filter bit equality. Artifacts:
`CubeSampling{,Raw}/result.json`, `lighting-cube-sampling-{after,raw}.log`.
All 189 transparency checks pass in each HDR format, including actual particle
and decal draws. All 421 probe checks pass with user anisotropy set to 16.
Native tests pass 15 with one optional skip. Logs:
`lighting-cube-lod-{transparency-hq,transparency-normal,probes,native-build}.log`.

Live treatment `9cad9651aa974d5b9855ca2fd5f59a37` used native
`5250819E720543A2207A10C3F447FE6E5FB9EF35FC630E1D82170575258DDC4D`
and managed
`FC74D673586DB162170F47D727A559624B68B589C3C687BDC9ACA89D9CF8B13B`.
It completed at 10:31:57 UTC and disposed/requested exit at 10:32:17 UTC; its
process is gone. Capture validation errors=0 and completed/pre-tonemap HDR
remain bit-identical. Mean actual IBL-versus-WARP RGB error falls from
0.000674/0.000781/0.000544 to 0.00000852/0.00000987/0.00000693; max is now
0.0000806/0.0000854/0.0000553. Mean recomposed fogged RGB error falls from
0.0000889/0.0001017/0.0000619 to 0.00000206/0.00000233/0.00000144.
Matched DX11/Vulkan clear screenshots were reviewed and the clear windows
remain correct. This resolves the sampled specular operation mismatch.

The broader strict-input HDR comparison is still not equal: on 26,084 matched
GBuffer/light/AO-footprint pixels RGB MAD is 0.000861/0.000584/0.000749, with
max 0.125/0.0625/0.0625. This aggregate barely changes because other residuals
dominate it. It must not be presented as full lighting/material parity.
Artifacts: `lighting-cube-lod-{live-terms,pinned-comparison,probes}.json`.

Separate code audit finding to investigate after this correction: transparent
decal submission currently binds the constant ambient fallback to both probe
slots, while the corrected glass/billboard/particle paths use published probes.
That is not covered by the static-window lighting captures.

The stock `MyScreenDecals.Render` explicitly binds CloseCubemapFinal at t11 and
FarCubemapFinal at t17. Its transparent shader uses AmbientDiffuse through
those slots. A new GPU control runs a diagnostic pixel shader on the actual
transparent-decal pipeline/descriptors, with zero-color glass supplying its
usual two depth-layer transactions. Existing distinct close-geometry/far-sky
probes should produce distinct left/right colors. Before correction, both
read exactly RGB 0.0588074: the constant fallback. The regression fails as
expected (`lighting-decal-probe-binding-before.log`). The production bindings
now select published final[0]/final[1], with the fallback retained until ready.
The canonical native build and the same GPU regression remain pending.

After correction, the actual decal set reads close RGB 0.2890625 and far RGB 0,
matching the separately read published probe samples. The initial assertion
presumed a blue far probe, but this fixture's actual far input is black. It now
compares both outputs against the four center texels sampled by direction
(0,0,-1), allowing the two packed render-target conversions. The same assertion
still fails against the prior binary's gray fallback, and passes with the new
bindings. All 424 probe checks pass with 16x anisotropy and zero validation
errors. Native tests pass 15 with one optional skip. Native SHA256:
`8F510319362796041EE80D057D2CD9877C5DFB318A9D4808E92FC8D80F62CC06`.
Logs: `lighting-decal-probe-{native-build,binding-before,binding-after}.log`.

## Remaining stock lighting pass order

The stock `MyLightsRendering.Render` submits tiled point lights, spotlights and
then directional/environment lighting, with additive blending. Native currently
submits directional first (overwrite), then additive local lights. Real-number
addition would hide that ordering difference, but the R11G11B10 target rounds
between passes. A new focused stage control will verify the stock ordering and
that directional lighting preserves the already accumulated local radiance.
The current wider strict-input comparison still has 1,372 pixels beyond two
packed codes (out of 26,084); the pass-order finding alone does not explain all
of them. Artifact: `lighting-cube-lod-packed-code-audit.json`.

The stage regression fails before on the empty local stage already containing
directional radiance. After matching the stock order and enabling additive
blending for the directional pass, 192 transparency checks pass in each HDR
format with zero validation errors. They compare the point-stage RGB bits to
an independently rendered point-only frame and require the final directional
stage to add radiance. All 424 probe checks also pass, and native tests pass
15 with one optional skip. Snapshot indices retain their meanings; Schema 2
records the actual order so comparisons include preceding local accumulation.
Logs: `lighting-pass-order-{before,normal-after,hq-after,probes,native-build}.log`.
Live run `0dda4260e78548c5b0ea24e5f820b6a0` is pending with native
`D049025D333F6A6F04233C020EBF516830547EFEF5145CDED6FC5D0350EA6925`
and managed `FC74D673586DB162170F47D727A559624B68B589C3C687BDC9ACA89D9CF8B13B`.

That live run completed at 10:50:20 UTC and requested exit at 10:50:40 UTC;
it disposed at 10:50:41 UTC and its process is gone. Capture validation errors
are zero, Schema 2 reports the stock order, and completed/pre-tonemap HDR is
bit-identical. On 26,119 strict matching raster/light/AO-footprint pixels,
RGB HDR MAD is now 0.0000518/0.0000541/0.0000283, versus the prior
0.000861/0.000584/0.000749. Max channel delta falls to 0.015625 in each channel;
P95 is 0.0000610/0.0001221/0.0000610. This validates the pass-order correction
on the matched scene, without closing the remaining sampler/material coverage.
Artifacts: `lighting-pass-order-{pinned-comparison,live-terms}.json`.

## Hardware reference correction and sampling recovery

CppStudio state: Recovery; reason=the WARP filtering oracle does not predict
the actual DX11 hardware sampler; evidence=`lighting-ambient-hardware-{replay,
linear}.log`; exit=the sampler hypothesis is reconciled against hardware DX11
and one canonical control, with no speculative product sampling edits.

`CapturedAmbient --hardware` now runs identical captured-input HLSL on the
RTX 5070 Ti, with driver/adapter identity recorded. Hardware DX11 anisotropic
SampleLevel differs from WARP, while hardware linear closely matches WARP.
At (205,253), DX11 actual HDR RGB is 0.001190/0.003174/0.001343;
hardware replay is 0.001198/0.003195/0.001346, while WARP predicts
0.001298/0.003375/0.001424. The hardware anisotropic IBL sample is
0.009974/0.011651/0.008336 versus hardware linear
0.011053/0.012957/0.009077. This explains a remaining sampled mismatch without
requiring a guessed probe-capture timing change.

Patch audit: visible glass culling/probe slot/lifecycle fixes retain their prior
live and focused proof. Transparent-decal probe binding and stock additive pass
order are proven source contracts with fail-before/pass-after GPU controls; the
new order's matched live result is recorded above. Seven-target terms and replay
driver controls are required diagnostic evidence. The global `Dx11CubeLodAdapter`
is **partial/unproven for hardware parity**: its WARP/isotropic proof is valid,
but the earlier wording that the sampled hardware mismatch was resolved was too
broad. Its before/after private binaries and raw control are retained, and
normal deployed assets remain unchanged. Freeze further product sampling edits
until the hardware kernel difference is isolated. No brightness/LOD fitting is
accepted as proof.

Primary-source realignment: Microsoft's sample_l contract specifies isotropic
derivative handling. Khronos [derivative and anisotropic filtering rules](https://raw.githubusercontent.com/KhronosGroup/Vulkan-Docs/main/chapters/textures.adoc)
also zero derivatives for explicit Lod, but make the anisotropic filtering
scheme implementation-dependent. These facts do not prove that disabling the
anisotropic sampler reproduces a driver's isotropic kernel. The next bounded
control separates fractional mip interpolation from spatial filtering, using
authored constant-color mip ramps on both hardware APIs.

The mip-ramp control proves a hardware interpolation difference even without
spatial texture detail. At requested LOD 0.35, Vulkan anisotropic samples red
0.171786, Vulkan/DX11 linear both sample 0.085893, and hardware DX11 anisotropic
samples 0.048249. Unit versus nonunit direction does not change those explicit
LOD results. Isotropic projected gradients do not reproduce the hardware DX11
SampleLevel result, so that hypothesis is rejected. See
`lighting-cube-hardware-{mip-ramp,isotropic-gradient}.log` and the corresponding
CubeSamplingRaw-Hardware directories.

An isolated diagnostic sampler with anisotropy enabled and maxAnisotropy=1
also behaves like linear sampling, rather than hardware DX11 anisotropic
SampleLevel. That hypothesis is rejected too. The new control API affects only
the seven-target diagnostic pass, defaults to the actual set, clears stale term
readback bits when changed, and releases its sampler on disable/world cleanup.
Its GPU control verifies exact unchanged nonzero main HDR and restoration on
disable, with validation enabled/errors=0. Native tests pass 15 with one
optional skip. Native diagnostic build SHA256:
`BB3CD0DC21AE7E2F4F484101241AFCAFD4E05F94E2287A5DF41A0BDC5D642B0A`.
No further product sampling changes have been made. Logs:
`lighting-{cube-hardware-max-one,diagnostic-max-one-native-build}.log`.

The direct PBR kernel now also runs on hardware DX11, rather than relying only
on WARP. All 256 copied-input metalness/gloss/view/normal cases pass on the
RTX 5070 Ti versus Vulkan, with maximum absolute error 1.19209e-7, maximum
relative error 3.56455e-7, zero mismatches and zero enabled validation errors.
This excludes scene inputs, probe sampling, local shadows and presentation.
Artifacts: `LightingParityGpuTests-Hardware/result.json`,
`lighting-direct-hardware-reference.log`.

Current remaining acceptance: reconcile the hardware sampler kernel without
empirical brightness/LOD fitting, finish the wider material/lighting coverage
audit, and rerun any affected canonical lanes. A user-facing fidelity question
was pending about documented GPU tolerances versus driver-specific pixel
equality (resolved in favor of documented tolerances below); the goal remains active and
has not been narrowed. All changes remain uncommitted and the normal deployed
native asset still has SHA256
`876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043`.

## 2026-10-18: transparent material route and decal-depth audit

The hardware filtering investigation remains in Recovery, with further product
sampling changes frozen. A bounded read-only NVAPI audit reports driver 617.14,
branch r617_07. Legacy.exe and SpaceEngineers.exe profiles are found; dotnet.exe
is not. All queried filtering settings return status -160 through GetSetting
for base/global/game profiles. This does **not** establish default or effective
setting values, and supplies no driver-profile explanation of the sampler
difference. Only initialization, profile/session reads and cleanup are called;
no driver settings are changed. Artifact:
`TestResults/lighting-nvidia-filtering-readonly.json`.

The separate transparent-source audit found a missed visible-glass caller:
`VulkanNativePassScene.DrawNativePassGlass` omitted `TriangleFaceCulling`, unlike
`VulkanScene.DrawWorld`. The existing mixed-model handoff test previously
compared geometry, textures and constants while omitting geometry flags. Its
installed HalfWindow control now registers one culled and one two-sided
material and includes flags in the packet comparison. It fails before at the
managed/native handoff and passes after forwarding the registered material
policy. All 107 NativePassGpuTests checks pass, including identical composite
pixels and shadow depth across both frame slots, with core/synchronization
validation enabled and zero errors. Both managed runtimes build with zero
warnings/errors. Logs: `lighting-mixed-glass-{culling-before,culling-after,
managed-build}.log`. Managed net10 DLL SHA256:
`23F13DD40CCE89DA1A5D520A623CA45A300D82A28004AE7CC8F825230CFEFBAA`.

The same audit corrected an earlier overbroad culling assumption. Stock OLD
`MyTransparentModelPass.BeginDepthOnly` and NEW
`MyTransparentForDecalsRenderPass.DrawInstanceLodGroup` both select a null
rasterizer state, independently of the visible material's culling policy.
[D3D11's documented default](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_rasterizer_desc)
is clockwise front faces and back-face culling. Native decal-depth rasterization
previously followed the visible material and incorrectly admitted back-facing,
two-sided glass. It now always selects the existing clockwise back-cull depth
pipeline. Visible glass keeps its material policy. No resource lifetime or
barrier changes are needed.

The stock-shader/OIT regression fails before because the rejected winding
receives a decal. After correction, that winding matches undecaled two-sided
glass exactly; the accepted winding receives the decal and is independent of
the visible material's culling flag. TransparencyGpuTests passes 196 checks
in each HDR format with zero validation errors. Native tests pass 15 with one
optional skip, and all 424 probe checks pass, including actual transparent-decal
probe bindings. Logs: `lighting-decal-depth-culling-{before,native-build,
normal-after,hq-after,probes}.log`. Private native SHA256:
`CD6718DAE46AAEF6F82DFC180B6E89263641C452031D10C58E2766B80E750969`.
These are focused contracts, not a completed live overlap comparison.

Remaining overlap divergence is now traced to the exact stock owners. OLD
`MyTransparentModelRenderer.Render` collects depth candidates only for owners
with registered decals and actor distance squared below 10000. Its sorted
distance groups use thresholds 2.25 then 0; one copied depth image is retained
between groups. OLD depth drawing disables the pixel shader and does not write
new normals. NEW glass has two separate copies of opaque depth, filters the
second layer by the first and writes the GBuffer-normal copy. Native still
runs two global geometry peels across all glass streams. Replacing that owner
and selection policy requires matching source-pipeline identity and decal-owner
membership, rather than merely increasing the layer count. The apparent OLD
`j < count` selection bound in the decompiled method also needs confirmation
against installed IL before reproducing behavior. No speculative overlap-route
change has been made.

The coverage audit also removed stale claims that LDR/PostPP/custom projections
were unimplemented or that probe/BRDF inputs remained placeholders. Those paths
exist; source existence does not establish full visual parity. All 39 billboard
packet checks and the production PostPP near-plane GPU control pass; enabled
GPU validation reports zero errors. `Docs/Transparency.md` records exactly
what these controls exercise and excludes unrequested NGX/custom-projection
acceptance. No normal asset deployment or commit was performed.

## 2026-10-18: OLD/NEW glass decal ownership and OIT order

The prior goal turn is verified progress: two culling fixes and their targeted
regressions passed. This follow-up advances the remaining overlap contract;
it does not narrow or complete the lighting/PBR equality goal. The separate
hardware sampler incident remains frozen in Recovery.

Installed IL confirms the unusual OLD depth selection bound: V_5 starts at
the below-minimum count, but its loop compares against the in-range count,
not the sum. The source DLL SHA256 is
`B4A6502F913B70E53C8F715BFC6DFEBEF2AE9E87BE52A95C84EB7C5FEDCDDE3B`;
`TestResults/lighting-stock-old-glass-depth.il` contains the actual method.
The native packet selection preserves this behavior rather than correcting
an inferred stock bug.

`VulkanDecals.HasEntityDecals` now supplies a lazily rebuilt copied-parent
membership index to the actual scene. OLD glass packets carry actor distance
only for registered owners; positive infinity excludes other OLD owners from
decal depth. Source identity is determined independently of the rigid/skinned/
instanced vertex layout. NEW packets retain separate geometric layers, including
the native mixed-model consumer. Queue-time flags select the sorted OLD groups
and record both requested passes, even when the stock skip/count bound selects
no geometry in the far group. The existing packet size/ABI is unchanged; these
flags require the matching current native implementation.

Native OLD depth uses the authenticated vertex transform with no fragment
shader, no normal attachment and default clockwise back-culling. Its second
distance group retains the first group's depth. NEW depth retains the two
independent filtered depth/normal copies. NEW decals now use their layer as
both sampled input and read-only depth attachment; the explicit descriptor
layout matches that use. OLD decals retain opaque attachment depth and the
untouched normal copy. Each temporary image tracks its own initialized layout,
including switching from OLD-only frames to NEW frames and resize.

Source-family separation also exposed two distinct visible rules. OLD glass
always disables culling, while NEW visible glass honors its material flag.
OIT submission now follows stock standard billboards, particles, OLD glass/
decals, then NEW glass/decals. This ordering is numerically observable: the
production shader control with radiances 1000/1/1 previously resolved red 619
versus 619.5 when its NEW/OLD input order changed. After source ordering, both
resolve to 619. The initial 512/0.3/0.3 control rounded to the same final value
and supplied no fail-before evidence; it was replaced with the discriminating
control. This changes test inputs, not product brightness or sampling.

The first order build exposed a synchronization error: the read-only scene
depth attachment retained STORE, whose write was absent from the new read-only
barrier contract. It now uses STORE_OP_NONE across OIT scopes. The final
canonical native build passes 15 tests with one optional skip. Both managed
runtimes build with zero warnings/errors. Final native SHA256:
`991DE20C8B9EABD7ED074854D562AEF8A17A595AC0DBE99CBC81E99C8D9660BE`.

Focused final evidence: TransparencyGpuTests passes 213 checks in **each** HDR
format; NativePassGpuTests passes 111, including actual forced-OLD model
classification and owner-distance exclusion; ParticleBridgeTests passes 21,
including copied owner membership/removal; ProbeGpuTests passes 424. All GPU
lanes use enabled core/synchronization validation and report zero errors.
The source-split regression fails on the prior private culling-only binary;
the order and OLD visible-culling regressions fail on the intermediate split
binary. Per-frame delta assertions use cumulative native counters correctly.
Logs: `lighting-glass-source-{split-*,order-*}.log` in TestResults.

Private live run `403ce4a31758464bad702dc08cde7c82` was prepared and started
with this native, managed net48
`9EFA921B739F78E576CEA09F55474BF855CEFA657ABB7D7381D4916BBA358FA3`,
and Anomaly `322F609E56962A6018E00F4B371AF080F79939D24EDC988D66EF5AF38A522EEF`.
It combines the canonical transparency and pinned/static lighting scenarios;
their cameras and dynamic particles make the early transparency captures
integration evidence until explicitly matched. Schema 2 glass diagnostics
record source pipeline, owner distance and queued depth flags. Completion,
validation, actual route evidence and the paired DX11 capture remain pending.
Prepared snapshots are immutable; normal assets remain undeployed and all
expanded-goal work remains uncommitted.

Vulkan completed at 11:44:02 UTC, requested exit at 11:44:22 and disposed at
11:44:27; the process was gone by 11:45:17. Its 640x480 lighting capture is
current-frame Schema 2 with validation errors zero. The held glass snapshot
contains six NEW packets with flag 1 and one OLD packet with flag 3/distance
infinity. This proves live NEW routing and OLD exclusion at that capture, not
positive OLD distance-group coverage. Live counters recorded 4,910 decal draws
and 13,856 glass depth draws before removal, with zero validation errors.
The early transparency image includes the overlapping Anomaly volume scenario,
as expected from the combined choreography; it is not a clean isolated glass
appearance reference. Positive OLD groups and mixed ordering retain the
focused GPU proof described above.

The matching prepared DX11 run `ae9ee3be4b0c46ffb1debfa353119078` started after
Vulkan shutdown, using the same managed/Anomaly binaries and world seed. Its
capture delivery, state identity and visual comparison are still pending.

## 2026-10-18: independent glass switches and comparison scope

The prior turn made verified progress on source selection, culling and OIT
ordering. The next source audit found that the stock independent
`DrawTransparentModels` (OLD) and `DrawTransparentModelsInstanced` (NEW)
settings were ignored by Vulkan's glass consumers. Ordinary scene submission
now gates by source identity; the mixed native consumer honors the NEW switch
too. Disabling glass leaves opaque native geometry intact, and restoring the
settings restores both consumers without changing ownership.

The new actual-model regression fails before on the OLD switch. After the fix,
NativePassGpuTests passes 121 checks with enabled core/synchronization
validation and zero errors. The fixture now supplies explicit stock glass
multipliers and enabled gates, instead of a zero-initialized settings struct,
so its material/composite comparisons have a visible glass signal. Calling the
full `MyRenderSettings.Default` initializer in this standalone process failed
because streaming budgets require `MyVRage.Platform`; explicit relevant values
avoid claiming an initialized game platform. Both managed runtimes build with
zero warnings/errors. Logs: `lighting-glass-draw-gates-{before,after,
managed-build}.log`. Current managed net48 SHA256:
`F0AC7BC6FB7F5E408DE5C4AD5CA27A21295CF013E91259F1D6C602CDDB489E9A`.

DX11 mixed run `ae9ee3be4b0c46ffb1debfa353119078` completed at 11:48:53 UTC,
unloaded at 11:49:04 and requested exit at 11:49:13; its process is gone.
The controlled transparency verifier passes activity, peak/expired particle
counts, cleanup, capture cameras and Vulkan validation zero. It does not claim
visual parity. In fact, the two transparency-glass screenshots capture opposite
medium states at the shared volume-toggle boundary: Vulkan is blue/fogged and
DX11 is clear. This pair cannot certify glass appearance, despite camera
positions matching. Artifact:
`lighting-glass-source-transparency-acceptance.json`.

The mixed before-tonemap captures have matched quality and 25,985 strict
GBuffer/AO-footprint/sun-visibility matches, but HDR MAD is
0.013654/0.013717/0.013950, max 1.46875/1.61719/1.59375. These subset names
never established equality of local-light buffers, probe texels or transparency
inputs. The comparator now states that boundary explicitly. Its new Schema 2
attribution groups pixels by whether native HDR changes after completed
local/directional lighting. Here 98.68–98.80% of absolute pairwise error is on
the 3,842 later-changed pixels; their MAD is about 0.091–0.093. The 22,143
unchanged pixels have MAD about 0.000197–0.000212, with max about 0.57. This
narrows the evidence; it does not identify a guilty backend or prove the
remaining local/transparent inputs equal. Artifact:
`lighting-glass-source-pinned-comparison.json`.

To avoid substituting this dynamic combined scene for the established static
lighting comparison, run `0f9dc720aac14683a3bb1c9a7d8705ce` used the same
seed, pinned/static lighting scenario and current binaries without the
transparency/electrical/weather scenario. It captured at 11:57:05 UTC,
completed at 11:58:05, requested exit at 11:58:25 and disposed at 11:58:26;
the process is gone. Enabled validation reports zero errors. Against the
existing same-quality DX11 reference `08e1810e6ffa41caabe940b47fe2b2eb`,
26,117 strict captured-input footprint matches give HDR MAD
0.0000528/0.0000539/0.0000269, max 0.015625/channel and unchanged P95
0.0000610/0.0001221/0.0000610. The clear paired images were reviewed and remain
visually close. This excludes a large static regression from the source split
and gates; it does not close hardware filtering, positive OLD overlap in a
live scene, or dynamic transparent appearance. Artifact:
`lighting-glass-static-pinned-comparison.json`.

Next acceptance must isolate transparent source groups from the Anomaly
camera/volume choreography and compare the actual relevant inputs. No new
product sampling changes, normal asset deployment or commit was performed.

## 2026-10-18: isolated glass acceptance and finite lighting inputs

The human completion standard is now explicitly stock feature and appearance
parity with documented GPU tolerances. Vendor-specific exact interpolation is
not required. This resolves the open fidelity question above; it does not
permit unexplained large visual differences or missing stock features.

CppStudio state: Recovery for the isolated overlap fixture. Product binaries
remain frozen at managed `F0AC7BC6...` / native `991DE20C...`. Repeated invalid
fixture assumptions are being reconciled against installed source, rather
than used as evidence for speculative renderer patches. Exit requires a
canonical matching pair with verified source, quality and finite inputs,
followed by actual image review and a plain diagnosis of any residual.

`-GlassOverlap` now runs an independent static nine-pane scene using the
installed HalfWindow geometry: OLD, NEW and mixed source columns, followed by
colored transparent decals, restored state and render-object cleanup. It
excludes other camera/volume scenarios, privately stages current native/SPIR-V
assets, pins quality/HBAO seed, observes the actual source lists/queued flags
and holds each source state until its full-frame PNG is delivered. Numeric
image metrics use the HUD-free x40,y160,w560,h160 ROI. The companion verifier
checks source, factory inputs, material DDS bytes, camera, quality, lifecycle,
binary identity and Vulkan validation; image errors remain diagnostic.

Invalid fixture runs are retained: `dc7c6f91...` missed the authored geometry
stub redirect; `88b36974...` guessed surface distances instead of actor-origin
distances; `f5b8630a...` requested an unsupported world-only screenshot;
`6b7efa11...` observed the DX11 list after stock Render had cleared it. Each
run reached terminal state before replacement. Installed source supplied the
geometry redirect, supported capture contract and correct list lifetime.
No product normal, sampling or brightness changes followed these failures.

The source/capture pair Vulkan `361e8b564c1d46ccbfbe35bfea482f0f` and DX11
`6b33d93dfdc84fc7919db651c07ad427` passes those source checks: 8 OLD/10 NEW
visible parts; 6 near/2 far OLD decal candidates; actual installed selection
6 near/0 far with both passes requested; cleanup and validation zero. It
does **not** prove appearance parity: baseline ROI RGB8 MAD was approximately
47/45/45 and the images visibly disagree. Artifact:
`lighting-glass-overlap-acceptance.json`.

Additional actual CPU frame/material snapshots in DX11 run
`edc67e27fa164e02819bf90010a6ef87` exposed NaNs at offsets 640/644/648.
Installed MyCommon.MyFrameConstantsLayout and MyEnvironmentLightData confirm
these are SunLightDirection (environment-light offset 624 plus field offset
16). The established Anomaly lighting fixture already pins a finite sun for
this EmptyWorld condition, but the new isolated fixture had omitted that
setup. The isolated fixture now pins the same normalized (.3,.5,1) toward-sun
direction, derives the actual field offset from installed layouts, and checks
the captured main-frame vector against its expected negative direction before
accepting observations. The verifier independently checks the saved raw bytes.
The prior large image difference is invalid-input evidence, not an established
glass PBR defect. A fresh finite-input pair is required.

The first guarded run `75b5c7e2ed584b16b42d2b73f0d183f4` rejected a pre-scene
observation before the three-second initialization phase. This was a fixture
phase-order mistake, not evidence that stock overwrote the finite assignment.
The observer now waits for scene initialization. Its prepared Vulkan partner
`de9a7471...` remains unlaunched/stale. Fresh matching snapshots are DX11
`2fa158cc9cc1452ab60306dabe532685` and Vulkan
`8082a445382b4475a4b8e3ca369dfc4c`; acceptance is still pending.

That finite-input pair completed, unloaded and exited normally. The verifier
passes matching materials/multipliers, finite actual sun, source membership,
geometry, camera, quality, capture delivery and cleanup; Vulkan validation is
enabled with zero errors. Baseline scene-ROI RGB8 MAD remains
23.30/24.02/24.45, with maxima 166/158/158. Glass is visibly too dark despite
close opaque-frame appearance. This is an unresolved defect, not an accepted
GPU tolerance. Artifact: `lighting-glass-overlap-finite-acceptance.json`.

The follow-up source audit invalidates the earlier **OLD always-two-sided**
claim in this journal and its regression. `MyTransparentModelPass.Begin`
selects null for visible glass, just as BeginDepthOnly does. The installed
MyRenderContext forwards null to its device state; D3D11's default is back-face
culling. No per-material OLD override exists. The prior equality-only control
tested that two flags behaved alike, which could not distinguish always-culled
from always-two-sided. Its replacement checks each winding against visible and
empty controls and fails on native `991DE20C...` at the expected culling gate.
Log: `lighting-old-glass-default-cull-before.log`. Native selection now chooses
the culled pipeline for every OLD visible draw; NEW retains its material flag.
Canonical build, targeted GPU checks and live treatment remain required.

The canonical build passes 15 tests with one optional GPU-reference skip.
TransparencyGpuTests passes 216 checks in each HDR format, with enabled core
and synchronization validation and zero errors. Logs:
`lighting-old-glass-default-cull-{native-build,after,normal}.log`.
Private native SHA256 is
`E63BA05A063B017A978D23613AC83B742119CD46A7F3874762F79AACF9F28CF1`.
Managed production bytes are unchanged. Live treatment
`9a4360ef60ff4d4f84475aac83291c1c` uses the same finite-input harness, scene
and current native build. Its source/capture/visual acceptance remains pending;
no normal-profile deployment has occurred.

The treatment completed at 13:11:03 UTC and disposed at 13:11:24; its process
is gone. The same DX11 reference `2fa158cc...` and Vulkan `9a4360ef...` pass
all source, material, quality, finite-sun, capture and lifecycle checks, with
Vulkan validation enabled/errors zero. Baseline RGB8 MAD improves from
23.30/24.02/24.45 to **0.463/0.369/0.382**. Decal MAD is
0.468/0.390/0.382. Per-channel P95 is one code in all three phases. The
OLD/NEW/mixed pane-interior means are reported separately. Reviewed images
closely match, including near green/red layers and the additional mixed blue
layer. The former OLD two-sided geometry overlapped the other columns, so
its correction also removes their dark obstruction.

Residuals are documented rather than hidden: baseline maxima are 72/42/42,
decal maxima 87/72/45. Of 89,600 scene-ROI pixels, 336 baseline / 363 decal
pixels have maximum-channel error above 16; every such pixel borders a
greater-than-15-code local contrast in one of the images. Only 15 / 25 pixels
exceed 32. This establishes spatial concentration at high-contrast edges; it
does not independently identify a particular interpolation/AA operation.
The ROI excludes the lower HUD but retains the central reticle. No global
maximum-error exemption is inferred from these values. Restored-versus-baseline
MAD is about 0.01 code in either backend, with P95 zero.
Artifact: `lighting-glass-overlap-default-cull-acceptance.json`.

Recovery exit for the overlap incident: the invalid NaN-sun oracle and the
false OLD-culling assumption are reconciled; the canonical finite-input scene
now passes source and reviewed appearance acceptance under the human's GPU
tolerance standard. Next state is Investigative for the remaining wider
lighting/material coverage audit. This does not by itself close the full goal.
Production native assets still hash `876D526E...`; all work is uncommitted.

## 2026-10-18: non-default transparent gloss setting

The wider field audit found one more mismatch in `GlassMaterial`: installed
MyRenderPassUtils.GetTransparentMaterialCB multiplies **ReflectionShadow** as
well as Gloss by TransparentGlossMultiplier. Native packing multiplied only
Gloss. ReflectionShadow now uses that same stock multiplier; no other fields
or formulas change. The finite-input live pair above uses multiplier one,
for which its captured finite material bytes are unchanged by this correction.

The existing installed-model/native handoff fixture now uses gloss multiplier
two, exercising the non-default setting through both consumers. All 121
NativePassGpuTests checks pass: material/object/texture/culling packets,
visible composite pixels, shadow depth, source switches, ownership and cleanup
remain identical across managed/native routes, with enabled core/synchronization
validation and zero errors. This checks the handoff and source-backed field
mapping; it is not a fresh stock DX11 image comparison at multiplier two.
Both managed runtimes build with zero warnings/errors. Logs:
`lighting-glass-gloss-settings-{build,native-pass}.log`.
Managed net48 SHA256:
`1BC9F7EB21C551E0250EF25C75A1762903722239CAB81287034FF6FC29A9E2FB`.
Native remains `E63BA05A...`; no deployment or commit occurred.

## 2026-10-18: published texture material controls

Installed MyCommon.UpdateFrameConstants copies the environment's complete
MyTextureDebugMultipliers block. Vulkan previously hardcoded six multipliers
to one and left all shifts zero. The twelve scalar multiplier/shift values now
flow from UpdateRenderEnvironment into the main frame and probe capture frames,
and return to defaults on unload. Existing scene-owned ColorizeHSV application
to object key colors is preserved, with its frame vector left zero to avoid
double application. Defaults retain the former frame values.

The actual stock Standard shader now receives non-default albedo, metalness,
gloss, AO and emissive controls through the production frame writer. Independent
expected sRGB albedo and packed material-channel checks pass on the GPU.
GBufferGpuSmoke passes 57 checks, core/synchronization errors zero; artifact
`LightingMaterialControlsGBuffer/result.json`, log
`lighting-material-controls-gbuffer.log`. Its isolated output directory is now
created before the first dump; the initial run exposed that pre-existing
harness ordering bug. The harness also needed the VRage.Render assembly
reference for the actual environment structs.

Both managed runtimes build without warnings/errors. ProbeGpuTests passes all
424 checks with validation zero. Logs: `lighting-material-controls-{build,
probes}.log`. Managed net48 SHA256:
`E7841E0394F1CDE583B9BA56782F3F01BBCA4CD285CC9DD8FC5224EAD25A51A5`.
Native remains `E63BA05A...`; no normal assets are deployed.

The next coverage audit has identified a separate capacity mismatch to trace:
stock point-light quality selects 256/512/2048/4096 lights, while native local
light storage/queues and conservative tile stride are limited to 64. This is
outside the sparse-light image fixture and prevents claiming full stock
lighting-feature completion. Selection, point/spot partitioning, tile layout
and shadow-index ownership must be audited together before changing capacity.

## 2026-10-18: independent local-light budgets and dense tile capacity

CppStudio state remains Investigative. Installed MyLightsRendering selects
256/512/2048/4096 point lights for LOW/NORMAL/HIGH/EXTREME and independently
limits spotlights to 32. The previous combined native/managed limit was 64.
The graph query is saved as `lighting-local-capacity-graph-query.log`; direct
installed source and callers, rather than the stale graph, establish ownership.

The managed scene now submits separate spotlight and point budgets, placing
spots in the first slots to retain existing 64-bit shadow identities. Point
selection still uses the existing distance ordering; stock grouped-cull
selection remains a distinct open audit. The arbitrary .001 intensity gate
was removed: stock's prepared light path honors the on flags and its own shader
contribution cutoff. Native storage accepts 4096 points plus 64 spotlight slots,
validates spotlight identities, and preserves the preview shader's fixed
64-entry constant-buffer ABI. The stock world path uses the complete storage
buffer. Packet structures/export signatures are unchanged.

World frame maxTileLights now follows stock quality. Conservative and compute
tile buffers allocate that stride; the compute selection mask covers 4096
points and retains ascending accumulation order. The producer rejects a point
list larger than its frame capacity before recording. Point constants are
written into the fenced current slot's existing mapped buffer, retaining its
allocation/flush lifetime policy. Preview buffers and spotlight redraw masks
are not enlarged or reinterpreted.

The new dense GPU regression first fails on native `E63BA05A...` because its
API rejects 256 lights (`lighting-local-capacity-before.log`). The corrected
test passes all four capacities and 4096 points plus 32 spots, with exact
conservative/compute HDR and display pixels. All 110 focused checks pass;
the full WorldPassGpuTests suite passes 534, validation zero. The real managed
scene test creates 4097 point candidates plus 32 spotlights, verifies each
quality's selected count and increasing visible HDR contribution, then removes
all sources and confirms zero points. Its 15 checks pass, validation zero.
Logs: `lighting-local-capacity-{after,world-full,scene}.log`.

Two fixture corrections are retained explicitly. Completed tile counts are
sampled when a fenced slot is reused; three identical GPU frames are required
before checking the result. The first two-frame observation was premature.
The original equivalent-sum fixture also made 4096 individual lights too weak:
stock LightPoint.hlsl rejects dot(light_factor,light_factor) <= 1e-7. Its zero
output is expected stock behavior, not missing native lights. The corrected
fixture uses fixed visible per-light intensity and normalizes captured HDR by
light count, retaining the original .002 absolute comparison bound. Production
brightness, cutoff and shaders are not fitted. Raw HDR and the cutoff log are
retained under WorldPassGpuTests / `lighting-local-capacity-stock-cutoff.log`.

Canonical native build: 15 passes, one optional skip. Managed builds: both
runtimes, zero warnings/errors. Derived shader integrity: five checks pass;
all 1401 original runtime modules validate. The integrity runner needed its
VRage.Render reference, already required by the linked world-variant selector.
The derived tile contract is now `StockDepthTiles-Ordered4096-2`; module SHA256
`42afd9a20b2343890d14d6e758ff0bf9f98d2be9b06792d7879d5a3d1f2ab799`.
Its generated package is rebuilt reproducibly with:

```powershell
dotnet run --project Tests/LightTileShaderPackage -c Release -- '<Content/Shaders>' 'Assets/Shaders/Vanilla' '<Bin64/VRage.Render11.dll>'
```

The generated Vanilla asset directory is ignored by Git, as before; its
authenticated manifest and coverage checksum must accompany the updated source
when packaging. No normal-profile deployment occurred. Current private native
SHA256 `977A22D47DA998FA0BABC461412D46CE75C85C881DADC001436008D8AD493DF2`,
managed net48 SHA256
`E6C06E1D5B6BF93DB1250246B393BC5E8D200693052DD5BA08E7278E862696BA`.
The established sparse static live treatment `ffdd1b8f46e648b98fd083a85609b1b4`
is running against the same seeded DX11 reference; its acceptance is pending.
The unrelated full SceneSubmission bulk-representation gate remains red at
its previously documented baseline check; the focused capacity lane does not
claim to resolve that separate failure.

The focused capacity lane also passes 110 checks in normal R11G11B10 HDR,
validation zero (`lighting-local-capacity-normal-hdr.log`). In both formats,
all four normalized dense-light outputs match the one-light reference exactly
in this fixture; CPU and GPU tile HDR values also match exactly. The canonical
`build-shader-suite.ps1 -Package` now invokes the tile package owner as well,
so the updated ignored asset is reproducible from the normal packaging path.
Its added command is the same independently successful package invocation;
the full 20,736-variant suite was not unnecessarily recompiled. Script parsing,
the derived integrity checks and all 1401 runtime-module validations pass.

Live treatment `ffdd1b8...` captured the controlled frame at 13:42:50 UTC,
completed at 13:43:49, requested exit at 13:44:09 and disposed at 13:44:10;
the process is gone. Actual quality matches DX11 `08e1810e...`, validation is
enabled/errors zero, and the reviewed off images closely match. On 26,123
strict captured GBuffer/AO-footprint/sun-visibility matches, HDR RGB MAD is
0.0000528/0.0000543/0.0000282, max 0.015625 per channel, with unchanged P95
0.0000610/0.0001221/0.0000610. These are the same scale as the prior static
pair, establishing no large sparse-scene regression. Artifact:
`lighting-local-capacity-static-comparison.json`. The scope still does not
assert equality of every local-light/probe input from those masks alone.

Capacity, separate budgets, tile storage and preserving bounded spotlight
identity are verified. Full lighting parity remains active: stock's actual
frustum/group candidate discovery and over-budget point-light selection differ
from the current global distance sort and require a separate source-backed
implementation/acceptance audit. The existing static fixture cannot prove
that overload-selection behavior.

## 2026-10-18: spatial light discovery and installed selection oracle

The selection audit replaces the global nearest-light scan with the shared
actor spatial trees: root and far queries, manual groups (contained,
brute/tree and incremental paths), then the stock independent point/spot
budget selection. `StockLightSelection` preserves root query order and the
installed grouped-overflow loop, including rounded distances and strict
group-fit comparison. `LightSelectionTests` invokes the actual installed
private DX11 selection methods: 164 checks agree on selected identities and
order across all four point budgets, root/group overflow, spot overflow and
rounded ties (`lighting-local-selection-oracle.log`). This establishes the
selector contract for supplied candidates, not live spatial equivalence.

`VulkanLightBounds` registers real actor cull callbacks and reproduces the
installed point offset, cone, flare bounds and effective intensity flags.
Correction to the preceding capacity entry: the .001 intensity suppression
is part of the stock component's effective data and is restored before
discovery. Authored `Data` remains intact; flare and probe-ambient consumers
use the effective flags.

The actual managed scene GPU lane passes 30 checks, validation zero
(`lighting-local-discovery-scene.log`): intersecting and disjoint root bounds,
point offsets, rotated spot bounds, weak-light activation, movement/removal,
partial/contained groups, all four group cull policies, visibility restoration,
DrawGroups, 270 root candidates preserving the first 256 query entries, and
40 spots selecting the nearest 32 independently. The first run caught hidden
parent groups still submitting lights. Shared `MyActor.SetVisibility` leaves
the manual-group tree entry in place; discovery now explicitly preserves
Vulkan's existing ancestor-visibility contract. Stock's group traversal itself
checks occlusion but does not perform this ancestor check. This is a retained
scene visibility behavior, not claimed byte-for-byte stock traversal.

Particle/flare/ownership bridge regressions pass 21 checks, and both managed
targets build with zero warnings/errors. The existing capacity lane passed
15 checks after the initial discovery changes. Native binaries are unchanged.
The private canonical live treatment `dd9c024bbf4346dbb9b9f0887890e675`
uses managed SHA256
`8454C64481238F3FCC81D7115D6E390CED919B645272B1CFCE4595A3059C0DE2`
and native SHA256
`977A22D47DA998FA0BABC461412D46CE75C85C881DADC001436008D8AD493DF2`.
Live comparison and the final wider material/lighting audit remain pending.

The canonical static capture for that exact discovery build is now compared
against seeded DX11 reference `08e1810e6ffa41caabe940b47fe2b2eb` in
`lighting-local-discovery-static-comparison.json`. Quality matches; the
26,134-pixel captured GBuffer/AO-footprint/sun-visibility exact subset retains
the previously observed small HDR residual (about 5e-5 mean absolute RGB).
The reviewed off PNGs closely match. On the captured-material-exact screenshot
subset, mean RGB8 difference is .262/.249/.274, P95 one code value; edge maxima
remain 79/52/58. This sparse static view does not prove overloaded discovery,
all local/probe inputs, or later edits. Validation is enabled with zero errors
and no device loss at capture; controlled fixture completion is still pending.

Terminal follow-up: the fixture completed at 18:35:02Z, requested exit at
18:35:22Z and disposed at 18:35:23Z; PID 2664 is gone. The final observer
reports no managed D3D11 creation attempts. The new ambient build also passes
the 15-check capacity lane with validation zero
(`lighting-probe-ambient-discovery-capacity.log`).

## 2026-10-18: probe-ambient candidate collection audit

The next direct source trace found `GatherProbeAmbient` scanning all scene
lights, whereas installed `MyLightsRendering.GatherLights` queries a bounding
sphere against root actor bounds and appends the whole collection of each
intersecting manual group (no view budget, no far tree). An offset root whose
bounds miss the sphere was incorrectly adding ambient energy. The focused
test fails before the fix (`lighting-probe-ambient-discovery-before.log`).
Ambient now uses those shared spatial queries and authored falloff/color
after effective flags have determined callback membership. It retains the
existing ancestor-visibility contract. All 35 discovery checks pass,
validation zero (`lighting-probe-ambient-discovery-after.log`), including the
excluded offset root, included root authored falloff, whole-group gather,
weak-child exclusion, hidden parent and destruction. Ambient query references
are cleared with the scene. Both managed targets build cleanly; current net48
SHA256 is `F0732AAD1B23C02307EE3353959CAF7EF83B5348C60FDCC2CEC3960886DF90D6`.
The earlier live snapshot predates this edit; live acceptance remains required.

The local-light source audit also identified a remaining spotlight projection
contract to fix and verify: installed `UpdateViewProjection` uses near .5 and
far `max(ShadowQuality.ReflectorShadowDistance()*Range,1.5)`, reading current
`MyShadowCascades.Settings`. Vulkan currently uses a range-dependent near
plane and full light range. Current shadow settings, shadow-map redraw
invalidation, caster eligibility and cookie consumers must be traced together.
Do not claim spotlight projection parity or final goal completion from the
passing discovery checks or sparse static capture.

## 2026-10-18: spotlight projection, settings and cache correction

The actual managed-scene native-packet regression failed with the old
range-dependent near/full-range far projection
(`lighting-spot-projection-before.log`). `SubmitLights` now uses installed
`MyLightComponent.UpdateViewProjection`'s fixed .5 near plane, float projection,
and `max(ReflectorShadowDistance*Range,1.5)` far plane. The renderer copies
current `VulkanShadows.Settings.Data` into the scene each frame. All five
shadow quality choices read the corresponding current reflector setting;
zero range disables shadow work while retaining the minimum cookie projection.
The native packet supplies both depth replay and cookie/shadow consumers,
so they retain a common projection. Native code and shaders are unchanged.

Caster eligibility and change detection now use the projected reach rather
than the full light range. Any far-plane change invalidates cached static
depth, including changes below the former .01 tolerance. The actual scene
suite passes 46 checks with zero validation errors
(`lighting-spot-projection-after.log`), covering default LOW projection,
non-default reflector settings, all qualities, matching caster reach,
cache reuse and invalidation, and zero-range behavior, in addition to prior
discovery/ambient/overload checks. Native-pass GPU regressions pass 121 checks,
core/synchronization validation zero (`lighting-spot-projection-native-pass.log`).

Private canonical static treatment `f22ab95117e24228a7000aa23b07d069` contains
both this correction and the ambient candidate fix. It uses managed SHA256
`68E65A3D658743231EFC41D8993F827034D851E979E0BEC8738953F3F964E4DD`
and the unchanged native
`977A22D47DA998FA0BABC461412D46CE75C85C881DADC001436008D8AD493DF2`.
It is running; live comparison and the wider material coverage audit remain
open. This sparse fixture alone cannot establish all spotlight edge cases.

The wider material audit has found a separate remaining path: model mesh
techniques `DECAL` and `DECAL_NOPREMULT` fall through
`WorldShaderVariants.Pipeline` to the ordinary opaque material pipeline.
Installed OLD `MyRenderableProxy.UpdateTechniqueGbuffer` and NEW
`MyGBufferRenderPass.InitPartForDecal` instead use read-only depth and
texture-role-specific premultiplied/non-premultiplied GBuffer blends.
`MyShaderBundleManager` supplies `STATIC_DECAL`; installed GBuffer PixelStage
samples the authored alpha mask and calls `GbufferWriteBlend`. The runtime
inventory contains authenticated variants, but `generate-world-variants.py`
currently selects only `STATIC_DECAL_CUTOUT`. Current cutout and screen-decal
passes do not prove these distinct mesh-decal contracts. Implementation,
actual pixel/blend/depth tests across texture roles and streams, and source
OLD/NEW state differences remain acceptance work. Do not close the material
goal while these techniques still route as opaque.

The combined ambient/spotlight build's live stage comparison is recorded in
`lighting-spot-projection-static-comparison.json`. Quality matches. In 26,123
pixels with exact captured GBuffer/AO-footprint/sun-visibility inputs, HDR RGB
mean absolute error is .0000520/.0000558/.0000281, max .015625 per channel,
P95 .000061/.000122/.000061. These masks do not certify every local light or
probe input. Reviewed held-camera off PNGs closely match; the captured-material
exact screenshot subset has RGB8 MAD .252/.239/.263, P95 one code value,
edge maxima 79/52/58. Validation remains enabled/errors=0, device loss=0.
This accepts the sparse static appearance regression for the current managed
build, not the still-missing blended mesh-decal techniques or all spot cases.

Terminal live evidence: fixture completed 18:42:17Z, exit requested 18:42:37Z,
harness disposed 18:42:38Z, and PID 30504 is gone. Final managed D3D11 observer
attempts are empty. No ordinary profile deployment was performed.

## 2026-10-18: blended mesh-decal pixel regression and integration trace

The new focused `WorldPassGpuTests --mesh-decals-only` lane reproduces the
missing `DECAL_NOPREMULT` contract on the actual stock Vulkan material path.
It draws a verified gray material background, then a red mesh with an authored
zero alpha mask. Stock non-premultiplied blend semantics require preservation;
the current fall-through pipeline replaces center GBuffer0
`180,180,180,0` with `255,0,0,0`. The test is intentionally red in
`lighting-mesh-decals-before.log`; raw buffers are in
`TestResults/MeshDecalGpuTests/{background,zero-alpha}.rgba8`. This is no longer
only a source-coverage suspicion. No product fix is claimed yet.

Direct source confirms four supported texture bundles (color, normal,
color+normal, color+normal+extensions), separate premultiplied/non-premultiplied
blend factors, preserved destination alpha, and read-only depth. OLD raster
culling can be overridden by `DisableFaceCulling`; NEW defaults to null
(clockwise back-cull). Both need coverage rather than assuming two-sided
because the existing generic world pipeline is uncullled. Source-captured
`STATIC_DECAL` variants are present in the runtime inventory; existing world
selection only packages the cutout family.

Integration trace: world packet validation and object uploads currently admit
only IDs below 32; classification helpers map cutout IDs to base geometry.
Recipe dependencies, indirect preparation and highlights depend on that base
mapping. Mesh decals must be excluded from cascade/spot/forward capture,
retain correct main-pass ordering and avoid inappropriate model batching.
`TryModelBatch` already rejects pipelines >=20. Native resident NEW/OLD fast
paths reject these techniques and require an explicit compatible fallback;
their success cannot be inferred from the ordinary world regression. The
implementation must connect the shader-family/texture-role selection through
both direct and prepared scene submission before the regression can pass.

## 2026-10-18: blended mesh-decal implementation and GPU coverage

`DECAL` and `DECAL_NOPREMULT` now select explicit `STATIC_DECAL` shader
families. IDs 128–207 cover the four stock texture bundles, two alpha
conventions, both metalness-colorable choices and all five geometry streams.
The generator derives macros from authenticated stock source/layout templates;
the supplement package contains 302 validated stages. The loader keeps
hash/definition/count validation and now allows the larger bounded manifest.
Both ordinary and prepared scene material selection pass the authored texture
bundle; unsupported stock bundles are rejected rather than guessed.

Native pipelines use stock bundle write masks, premultiplied or straight RGB
blend factors, retained destination alpha, read-only depth and default
clockwise back-culling. The fixture's original triangle is back-facing under
that policy: the opposite-winding opaque control proves rendering before the
zero-alpha and fractional-alpha checks. Blended decals execute after ordinary
world and native-pass geometry, preserving submission order among themselves.
World packet/recipe admission accepts the new IDs, maps their geometry/depth
dependencies explicitly, and requires exclusion from depth/forward passes.
Prepared/model batching retains its existing eligibility gates. Resident fast
paths still use their explicit compatible fallback for these techniques.

The actual completed-frame readback entry now exposes the other GBuffer
targets and opaque depth as inputs 5–7; managed `ReadbackWorldGbuffer` bounds
the target index. Depth readback uses a depth-aspect transition and restores
the shader-readable layout under the existing completed-frame wait.

The focused lane passes **684 checks**, validation zero
(`lighting-mesh-decals-after.log`, `MeshDecalGpuTests/result.json`): visible
opaque/half/zero-alpha controls; opposite windings; all 80 variants; authored
normal/gloss/emissive changes; exact preserved unauthored channels and alpha;
unchanged actual depth; exact native-recipe, indirect and supported prepared
GBuffer/depth results. Skinned geometry exercises the direct/recipe path;
the production prepared path excludes it, and the test does not invent a
prepared skinning route. The full WorldPass suite passes 534 checks and
NativePass suite 121 checks, both validation zero. Runtime supplement
validation passes all 83 unique manifest-referenced executable modules;
the tool now resolves hashed supplement metadata from its manifest rather
than assuming catalog-style module/XML basenames.

Native Release tests pass 15 with one optional opaque-reference skip. Managed
net48/net10 builds are clean. Current private hashes: managed
`87126DF512C8FBC1A7C6CAF647FDD2F85CB785F59885325F50CDE5E8732A6AA8`,
native `2731ED509CDCB46EE31A0F4CDEC537BBBACA7675C0C1D798937ECE2EB381B91B`.
GPU staging is `TestResults/LightingMeshDecalAssets`; the ordinary native asset
has not been deployed over. Logs share the `lighting-mesh-decals-` prefix.

Remaining acceptance: actual scene admission/fallback with authored mesh-decal
models and paired visible DX11/Vulkan coverage, including overlapping material
ordering and per-part appearance. The earlier static live scene contains no
such authored mesh-decal fixture and cannot certify this feature. Wider final
material/lighting audit and final goal completion remain open.

## 2026-10-18: authored scene admission and live mesh-decal fixture

`MeshDecalFixtureAssets` writes private MWM assets with the installed exporter:
an opaque quad and a slightly offset blended decal, full authored normal/UV
basis, valid static-model metadata, and explicit material textures. These are
ordinary model files, not mutations of renderer internals. The actual scene
lane round-trips their techniques through the stock importer, enables native
passes and checks normal versus forced OLD admission, correct bundle pipeline
and depth/forward flags, direct/prepared pixel agreement and object removal.
It passes 94 checks with validation zero (`lighting-mesh-decals-scene.log`),
including installed DX11 DDS header acceptance for every generated texture.

The independent `Run-Isolated.ps1 -MeshDecals` mode uses sixteen held-camera
panels: four texture bundles, two alpha conventions and OLD/normal rows.
It pins quality/HBAO and a finite sun, records DX11 actual OLD/NEW component
admission or Vulkan source flags plus actual queued blended packets, waits for
capture delivery, removes all render objects and verifies cleanup. The paired
comparison script checks quality, normalized authored MWM/DDS identity,
admission, terminal artifacts, texture-loader errors and per-panel interior
RGB8 differences. Its report is an aid to image review, not automatic parity.

Two diagnostic DX11 runs are explicitly invalid for acceptance. Run
`8525093126a24bbdbaeb2659fc3d44cd` caught a fixture observer incorrectly casting
the indexed component container to IEnumerable; the observer now uses its
documented Count/indexer. Run `7baa2767d9b84129b97eea98b7cc8000` captured and
removed 8 OLD/8 NEW actors, but a subsequent render-log audit found generated
texture header failures. Installed `DDSHelper.TryReadDDSHeader` insists on
148 bytes even for legacy DDS headers; our valid 1x1 RGBA DDS was only 132.
It fell through to ImageSharp, so those images may contain fallbacks and are
not accepted. The generator now uses the known 16x16 fixture format, the
scene test calls that actual installed header reader, and live capture plus
comparison reject texture-loader errors. Do not infer texture correctness
from actor admission or screenshot delivery.

Fresh corrected DX11 run `ff6a09a4954249ef8587541620ec35dc` is running.
The paired corrected Vulkan snapshot is prepared separately; prior unlaunched
snapshots predate these fixture corrections and must not be used. Live visual
acceptance, overlap/per-part material ordering and final wider audit remain
open. Product native/managed hashes are unchanged by these fixture edits.

The fresh run `ff6a09a4954249ef8587541620ec35dc` is also diagnostic-only: its
new log gate opened the live renderer log with File.ReadLines' restrictive
sharing and stopped before capture. There were no repeated DDS-header errors;
the gate now uses an explicit read stream with FileShare.ReadWrite. Corrected
DX11 run `b10288de81a64fae8e267427c6ea80c1` is running and corrected Vulkan
snapshot `096084b7a6ef452bb920a555b1da70ef` is prepared. Earlier unlaunched
Vulkan snapshots are obsolete; no prepared snapshot was modified in place.

The corrected pair is now terminal: DX11
`b10288de81a64fae8e267427c6ea80c1` completed at 19:17:56Z; Vulkan
`096084b7a6ef452bb920a555b1da70ef` completed at 19:20:23Z and disposed at
19:20:44Z. Both removed all sixteen objects, unloaded their private world,
requested exit and have no live process (PIDs 33700/20860). Vulkan retained
validation zero and no observed managed D3D11 creation attempts. DX11 has no
harness-dispose marker, also absent in the previous accepted DX11 static and
glass references. The comparator records that absence explicitly; its terminal
gate uses completed/cleanup/unloaded/exit witnesses plus separately checked
process absence, and does not claim DX11 plugin disposal.

**Paired appearance fails.** The comparison report
`lighting-mesh-decals-live-comparison.json` verifies identical quality and
normalized MWM/DDS assets, 8 OLD/8 NEW admission on DX11, 16 queued Vulkan
blended packets, cleanup, no fixture texture-loader errors, and per-panel
interior ROIs. Reviewed images reveal substantial differences: normal-only
panels are darker in Vulkan (about 21–30 RGB8 levels), while some straight-alpha
color/normal panels are much brighter. The largest panel mean absolute RGB8
delta is 64.17/66.23/66.45. These are not accepted GPU tolerances. The new
standalone 684-check suite and 94-check scene suite prove their stated
contracts but do not establish stock appearance equality for these assets.

Next canonical diagnosis: capture the actual held-panel GBuffer/material/depth
and HDR on both backends, then isolate whether the inputs diverge before
lighting. Existing DX11 readback companion can supply the stock stage; native
completed GBuffer/HDR readbacks are available without an Anomaly dependency.
Compare stock blend output, texture roles and frame/object material inputs
before changing any lighting or exposure parameter. Overlap/per-part ordering
and the remaining wider completion audit are still open.


### 2026-10-18: paired mesh-decal buffers isolate alpha views and OLD cache

The held-panel buffer route now captures decoded DX11 G0/G1/G2/depth/HDR,
AO/sun inputs, frame constants and probes, and the corresponding completed
native buffers without introducing an Anomaly dependency. DX11 reference
`a1f1c19f9dc44a85b213b79059be1953` completed and exited. Initial Vulkan
`3ede3a4874884a7c9faf7fff1180e4cf` failed its late HBAO witness: its closed
material files support diagnosis only, not fixture acceptance. The HBAO hook
now retains the last observed settings even before capture is requested.
`Compare-MeshDecalBuffers.py --allow-partial-vulkan` explicitly labels that
failed diagnostic source; it cannot turn it into a complete capture.

Installed MyGeometryTextureSystem and MyFileTexture make all non-normal map
views sRGB when a format has an sRGB counterpart. RGBA8 alphamasks therefore
sample byte 128 as approximately .216, not .502; BC4 remains linear because
it has no sRGB counterpart. `GetWorldAlpha` applies that semantic at all eight
model color/depth/forward/native consumers. The texture contract passes 14
checks, the raw GPU path 685, authored scene admission 102, and native-pass
regressions remain green, all with zero Vulkan validation errors where used.
Logs: `lighting-mesh-alpha-{contract,gpu,scene,native-pass}.log`.

The alpha-corrected Vulkan run `0246dc239e0a4b33b70af1f80d34f956` completed,
removed its panels, unloaded, disposed and exited (PID 29672 absent).
Its private managed SHA256 is
`DB8FD6E905B6BFCCBB35316AEA3688C56C784FEEB888F4548618A2FAF5A251C8`;
native remains `2731ED509CDCB46EE31A0F4CDEC537BBBACA7675C0C1D798937ECE2EB381B91B`.
`lighting-mesh-alpha-buffer-comparison.json` shows **exact G2 equality on all
16 panels**. NEW normal/AO also match within packed-format decode precision;
NEW HDR panel means differ by at most roughly .001 per RGB channel. CPU sRGB
decode of native G0 differs slightly from the DX11 GPU Load decode and is not
proof of a byte mismatch. No exposure or lighting fit was applied.

**OLD appearance still fails.** The image comparator still measures up to
44.72/54.38/54.54 RGB8 panel mean error. Its normal/AO differences remain large.
Installed MyMaterialShaders.Get hashes material, pass, vertex layout and flags
but omits TextureTypes; BundleInfo retains the first caller's roles. DECAL and
DECAL_NOPREMULT share STATIC_DECAL. The first OLD fixture bundle is color-only,
so later normal-bearing OLD panels inherit its lack of USE_NORMALGLOSS_TEXTURE
and USE_EXTENSIONS_TEXTURE. GBuffer PixelStage uses those macros for the blend
normal alpha and AO fallback; it still samples material textures for G2.
NEW MyShaderBundleManager includes texture-presence bits and does not alias.
This explains why alpha-corrected G2 matches while OLD normal/AO does not.

Current implementation work separates authored blend masks from effective
shader macros using four banks of the 80 supported blended pipelines. OLD
first-use registration runs from the shared actor updater before visibility
selection and includes all LODs; its process-wide cache survives actor removal
and scene teardown like the installed source. NEW retains authored macros.
The native build passes its 15 runnable tests (one optional skip). Shader
packaging and focused GPU/scene tests are still pending for this change;
**the cache correction is not yet accepted**, and live rerun remains required.
Overlap/per-part ordering and the wider lighting/material completion audit
also remain open. The ordinary installed native asset has not been deployed.


The cache correction now passes **1,325 focused GPU checks** and **113 actual
scene checks**, core/synchronization validation zero. The GPU matrix covers all
320 independent authored-blend/effective-macro combinations across five streams,
two conventions and two metalness choices, with direct/recipe agreement. The
scene uses a hidden first OLD color-only actor, removes it, then verifies the
later OLD consumers preserve its packed normal/AO while G2 still samples each
part's actual textures. Separate metalness and distance-fade keys are checked
with a different first bundle and visibly mapped normal coordinates. The
initial additional test confused packed G1's AO component with normal Z; its
assertion now compares the actual two encoded normal coordinates. This was a
test interpretation correction, not a rendering change.

Additional banks use native/material IDs 608..847, preserving 128..207 and
avoiding reserved supplement probe IDs 256..275 and highlight IDs 512..531.
The first package attempt exposed the numeric overlap and was rejected by the
strict loader; that package was not used for live acceptance. The final
manifest contains 782 stages, zero duplicate ID/stage pairs and 83 unique
modules. The packager now rejects overlapping family IDs before compiling.
Native tests remain 15 passed/one optional skip; no device or image lifetime
contract changed. Logs: `lighting-mesh-cache-{native-build,managed-build,
shader-package,gpu,scene}.log`.

Live Vulkan run `68da3669baab435590d832811624a7f4` is launched (PID 33864),
using managed SHA256
`1488508681D035EF05F613E4811183C41AB246441AA6C4F898DE2E54C4DC481A`
and native SHA256
`DEAA08FE41D80FFE26AFD0F44CC1CA4D31FA3D505121C06347A313238354492F`.
It must complete, deliver actual stage/image captures, retain matched inputs,
clean up and exit before its result is accepted. The ordinary native asset
remains SHA256 `876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043`.

A source audit also identified a follow-up requiring verification: the world
frame supplies `DistanceFade`, but the generated material variants do not
currently emit the actor `DISTANCE_FADE` macro. Installed VertexTemplateBase
uses it for distance scaling/fade. The first-use cache correctly keeps this
flag's key separate; that does **not** establish the flag's rendering parity.
Resolve its actual applicable path in the remaining material audit. Overlap
and per-part decal ordering, broad final regressions, and the full completion
audit remain open. No goal-completion claim is made by these focused tests.


The fresh live cache-corrected run is now accepted for the sixteen-panel
material fixture: it completed at 23:53:46Z, removed all objects, unloaded,
disposed at 23:54:07Z, requested exit and PID 33864 is absent. Actual source
admission is 8 OLD/8 NEW with sixteen blended Vulkan packets, validation zero;
the managed D3D11 observer records no creation attempts. Quality and normalized
MWM/DDS identities match DX11 reference `a1f1c19f9dc44a85b213b79059be1953`.
Both actual PNGs were reviewed. No exposure adjustment or lighting fit was used.

`lighting-mesh-cache-buffer-comparison.json` shows exact packed G2 equality,
exact depth/AO/sun visibility and normal/AO agreement within 2.41e-8 decode
precision across all interior panel ROIs. Largest panel mean linear-HDR RGB
error is .00096069. Largest isolated HDR channel error is .25 (panel row 3,
column 2, pixel 367/334, blue 11.5 versus 11.75); its cause is not independently
attributed to a particular hardware operation. This is retained as a scoped
numeric limitation, not silently rounded away. Display comparison
`lighting-mesh-cache-live-comparison.json` measures largest panel mean RGB8
absolute deltas .2031/.0992/.1954, largest panel P95 2/1/2 and largest individual
channel differences 4/2/4. The alpha-only pair's corresponding largest panel
means were 44.72/54.38/54.54. The reviewed stock appearance now agrees for these
held, non-overlapping assets under the accepted GPU-tolerance standard.

Final regression for this change: 1,325 focused GPU checks; 113 authored-scene
checks (now comparing all four direct/prepared GBuffer/depth targets); 534 full
world checks; 121 native-pass checks; 83 unique supplemental runtime modules
validated, including two cube-sampler adaptations; native 15 runnable tests
passed, one optional skip. All GPU suites report zero validation errors.
Logs `lighting-mesh-cache-{gpu,scene,world-full,native-pass,runtime-shaders}.log`.
No normal-profile binary was deployed and no goal-completion claim is made.

Next canonical proof: overlapping authored mesh decals and per-part ordering,
using the installed OLD SortingKeys/SortComparer and NEW batch order as the
source oracle. The installed DISTANCE_FADE vertex branch is specifically under
generic/cube/deformed instancing; rigid flags alone do not prove that path.
Audit and, where missing, implement its applicable instanced material behavior.
Broader lighting/material completion remains unproven until those items and
the remaining coverage audit are resolved.


### 2026-10-19 UTC: overlap ordering audit and an independent lifetime failure

The new `-MeshDecals -MeshDecalOverlap` scenario exports a background and two
intersecting blue/red decal parts per panel. Alternate row pairs author DECAL
and DECAL_NOPREMULT in opposite orders; all four texture bundles and 8 OLD/8
NEW sources remain covered, with 32 expected blended packets. Comparators
reject overlap/single-decal scenario mixing. DX11 capture also records actual
OLD CullProxy sort keys, part techniques and mesh/material pool indices.

DX11 `cfab699ee9d441e0ab7deb11af3d82c0` completed the overlap capture, cleanup,
unload and exit; PID 37904 is absent. Actual OLD keys confirm global technique
priority: even when an actor authors NOPREMULT first, its later DECAL sorts
before it. Installed MyRenderCullResultFlat.SortComparer compares unsigned
SortKey. Its lower fields use vertex flags/layout, mesh and material pool
indices. This full resource ordering is still open; authored/spatial actor
order is not an equivalent substitute.

The NEW draw loop retains preprocessed order, but MyMwmData imports by sorting
the complete source list by technique first. Vulkan incorrectly used authored
order for both source classes. The model cache now retains both lists: the
original continues to drive OLD proxy/cache registration; NEW direct/prepared
submission uses the stock-sorted list. Zero-index parts participate in the
source sort before filtering, as in stock. The authored-scene suite passes 121
checks, validation zero. It invokes the installed NEW comparer, verifies the
actual reversed asset, queued order and visible blend, compares all GBuffer/
depth targets under direct/prepared replay, and restores the original-order
control on the same GPU route. That control visibly differs; restoring NEW
stock order restores exact corrected pixels. Evidence:
`lighting-mesh-overlap-scene.log` and `SceneSubmissionGpuTests/new-overlap-*.rgba8`.
This does not close OLD global ordering or mixed-model ordering.

The unchanged Vulkan baseline `bbd5541b429a48818dd398a0171df07c` failed before
creating overlap panels: one validation VUID-vkDestroyBuffer-buffer-00922
reported destruction of buffer 0x314f000000314f while command buffer
0x1904595d8d0 was in flight. It exited and disposed; PID 34052 is absent.
It is diagnostic-only and supplies no overlap appearance acceptance. A native
error-only diagnostic now reports destruction call site/function, size and
usage without changing resource-retirement behavior or masking validation.
Native tests pass 15 runnable tests with one optional skip. The lifetime cause
remains unresolved; no clean rerun alone will be called its fix.

Fresh diagnostic/NEW-corrected Vulkan run
`6464b2eb0a5148558aa47e0f76146c0d` is launched, PID 45884. Managed SHA256
`CFD6E537AD3B9322A05F3BB664AAAE7C7CD6E89FDE4722B42D6BC83C1ABF5135`;
native SHA256
`30AF769916BE482C7E33961055EEE73D95300D78B9DD745537D3B84067069458`.
It is the next canonical live proof/diagnosis, not accepted in advance.
OLD global ordering, applicable instanced distance-fade coverage, the lifetime
failure, broader regressions and final completion audit remain open.


The diagnostic/NEW-corrected live run completed at 00:10:46Z, cleaned up,
unloaded, disposed at 00:11:09Z and exited (PID 45884 absent). Validation stayed
zero; the previous world-load buffer failure did not recur and remains
unresolved. `lighting-mesh-overlap-buffer-comparison.json` and
`lighting-mesh-overlap-live-diagnosis.json` retain the paired result.
The NEW rows now agree on packed material, normal/AO and depth inputs. OLD's
reversed-author-order row still differs: up to .21484 in decoded color and
8/255 in gloss. The largest panel mean RGB8 difference is 3.97/1.09/10.13.
Actual reviewed PNGs and stock proxy sort keys confirm this is an OLD ordering
defect, not a GPU tolerance. The first scene-order control and restoration
also pass, raising the focused authored-scene count to 121, validation zero.

The next bounded product change carries explicit OLD blended-decal provenance
through direct/prepared flags and sorts that class globally by stock DECAL
before DECAL_NOPREMULT priority. The native scheduler source confirms OLD
DoneFrame consumes before NEW DoneFrame; within OLD, the decals job is consumed
last. Relative order of blended source classes follows that scheduler.
This source ordering does not prove correct interleaving with all NEW opaque
models: the existing ordinary/decal phases still require a mixed-model audit.
Same-technique resource-key tie-breaks and OLD cutout versus blended ordering
also remain open. The current correction is being built/tested and is not yet
accepted. No full-ordering or goal-completion claim is made.


The OLD technique-priority change now passes 131 authored-scene checks, with
validation zero. This adds explicit OLD provenance, reversed mixed-convention
parts and two overlapping OLD actors created in both orders, comparing all
GBuffer/depth channels against the stock-sorted NEW reference. Direct,
prepared and actual indirect submissions agree. Native tests again pass 15
runnable tests with one optional skip. Logs:
`lighting-mesh-overlap-order-scene.log`,
`lighting-mesh-overlap-native-order-build.log`.
The full paired overlap rerun is being launched before appearance acceptance.
The world-load lifetime failure still has no identified owner/fix; diagnostics
remain enabled, with no validation suppression.


The full ordering rerun `3fdc9c727de5482cacf499fa5c3eec0e` is now terminal,
cleaned up, unloaded and disposed at 00:19:25Z (PID 36068 absent). It uses
managed SHA256 `B05576F167D761A23CB0943BFCF43B9B6FE74A48B6F423A78FC06CBDAB4E9878`
and native SHA256 `8F1DE9FE4C081810E4D946D987FAA3303C9BBC4E51E295E8C62AB22AB7BA4E07`.
Actual admission is 8 OLD/8 NEW actors and 32 blended packets, validation zero,
no observed managed D3D11 creation attempts, matched normalized assets/quality.
The actual paired PNGs were reviewed against DX11
`cfab699ee9d441e0ab7deb11af3d82c0`.

`lighting-mesh-overlap-order-buffer-comparison.json` shows exact packed G2 and
depth equality across all sixteen interior panel ROIs; normal/AO differences
are below 3e-8 decode precision. OLD reversed-author-order color/gloss defects
are removed. G0 CPU-vs-GPU sRGB decode differences remain scoped to .001619;
largest panel mean HDR RGB errors are .007807/.003260/.006045, with isolated
maximum .25. These figures are not attributed to a specific unproven hardware
operation. `lighting-mesh-overlap-order-live-comparison.json` measures largest
panel mean RGB8 errors 1.712/1.090/1.912 (before OLD correction:
3.970/1.090/10.133), largest panel P95 3/3/5 and isolated maxima 8/4/11.
The strongest errors are near bright specular regions; the reviewed overall
appearance matches for this controlled overlap fixture under the accepted
feature/appearance standard with documented GPU tolerances. The raw numbers
are retained; no exposure fit or quality change was applied.

This closes NEW imported part order and OLD global mixed-convention technique
priority for the covered paths. It does not close same-technique resource-key
ties, OLD cutout versus blended priority, or interleaving mixed OLD/NEW opaque
models and decals. The earlier in-flight buffer destruction remains open
because two clean subsequent runs did not identify or repair its owner.
Instanced distance fade and the remaining full coverage audit also stay open.
Final regressions for the current native/managed combination are running.


Current-combination regressions completed: 1,325 focused mesh-decal GPU checks,
131 authored-scene checks, 534 full world checks and 121 native-pass checks all
pass, with zero core/synchronization validation errors. Native Release tests
pass all 15 runnable cases with one optional opaque-reference skip. Logs are
`lighting-mesh-overlap-order-{gpu,scene,world-full,native-pass}.log` and
`lighting-mesh-overlap-native-order-build.log`. No game or GPU-test process is
left running. This is a completed bounded ordering correction, not completion
of the remaining full lighting/material goal.


### 2026-10-19 UTC: cutout versus blended ordering

Installed OLD keys place DECAL_CUTOUT after DECAL and DECAL_NOPREMULT; NEW's
importer also sorts in that order. Vulkan's managed fallback incorrectly
rendered cutouts in the early ordinary phase before both blended techniques.
An authored GPU fixture inserts an opaque green cutout before red/blue blended
parts. The initial run fails its stock late-cutout assertion after the clipped
mask case passes (`lighting-mesh-cutout-before.log`). The observed opaque-case
center was RGBA8 [128,206,229,0], rather than green. That scalar was read from
the failed actual GPU capture; its reused scratch file was later overwritten
by the corrected run. The failing log and source-native version remain the
before witnesses, not a retained full before image.

The fallback renderer now classifies cutout and blended pipelines together for
the late main-pass sequence. OLD provenance covers all three mesh-decal
techniques and applies their global priority. `OldMeshDecalOrder` names the
existing 0x04000000 bit; the former `OldBlendedDecalOrder` constant remains an
alias. No pipeline IDs, shader modules, ABI sizes or resource-retirement
behavior changed. The actual corrected OLD/NEW centers are [0,255,0,0], saved
separately as `lighting-mesh-cutout-after-{old,new}.rgba8`.

The authored-scene suite passes **156 checks**, validation zero. It covers
OLD/NEW, zero/full cutout masks, exact opaque-depth preservation, every GBuffer
channel under direct/prepared/indirect submission, and an external blended
actor created on either side of the cutout actor's creation order. Native
Release passes 15 runnable tests with one optional skip. Logs:
`lighting-mesh-cutout-{native-build,scene,harness-build}.log`.

`-MeshDecalCutout` extends the independent overlap fixture with sixteen green
cutouts and alternating zero/full masks. Its witness requires 32 blended and
16 cutout packets from historical fixture mesh identities, including after
actor removal. Source classes cannot disappear from cleanup by losing actor
membership. DX11 `b6f5c899e0ca4c308a772886fb952475` completed at 00:31:46Z,
cleaned up, unloaded and exited (PID 45092 absent); it records eight actual OLD
cutout proxies. Vulkan `5557aac77e254521a4bdeb4761696325` is launched (PID 768).
Both snapshots use managed SHA256
`4A1E01D03EB0DA722D2D7C7893B43DBB2054E397C9E1B09E622D8CFE745BD2CD`
and the Vulkan native is SHA256
`03ED5E4F037C39C0FDCB62F9C06FBA4AD562A8324482F2017592528A4C578012`.
Paired live appearance remains unproven until this capture completes.

Remaining audit includes resource-key ties, mixed opaque/decal source and
accelerated-path interleaving, instanced distance fade and the unresolved
world-load buffer failure. A separate raster-state audit must verify ordinary
MESH/default cutout culling, ALPHA_MASKED_SINGLE_SIDED and explicit
NoBackFaceCulling. The current core/cutout fallback pipeline is no-cull while
an unflagged OLD proxy selects the stock default rasterizer; front-facing
material captures cannot establish those cases. This is an audit item requiring
its own source/flag trace and GPU proof, not acceptance from the current tests.


The paired cutout run is now complete and reviewed. Vulkan
`5557aac77e254521a4bdeb4761696325` completed at 00:39:20Z, cleaned up all
fixture actors and both packet classes, unloaded, disposed at 00:39:42Z and
exited (PID 768 absent). It records 8 OLD/8 NEW actors, 32 blended and 16 cutout
packets, validation zero and no observed managed D3D11 creation attempts.
The comparator verifies matched quality, normalized MWM/DDS assets, scenario
flags, eight actual DX11 OLD cutout proxies, cleanup and terminal witnesses.
Both actual PNGs were reviewed.

`lighting-mesh-cutout-buffer-comparison.json` shows exact packed material and
depth equality over all sixteen interior panels, normal/AO errors below 3e-8
from decode precision, largest panel mean HDR RGB error
.007807/.003260/.006048, and isolated HDR channel maximum .25.
`lighting-mesh-cutout-live-comparison.json` reports largest panel mean RGB8
errors 1.066/.648/1.141, largest panel P95 2/2/3 and isolated maxima 5/2/5.
These are scoped fixture tolerances under the accepted appearance standard;
no exposure fit, shader brightness adjustment or quality reduction was used.
The opaque and discarded cutout columns both agree with stock.

Final current-combination regressions: 1,325 focused mesh-decal GPU checks,
156 authored-scene checks, 534 full world checks and 121 native-pass checks
pass with validation zero. Native Release passes all 15 runnable tests with
one optional skip. Logs: `lighting-mesh-cutout-{gpu,scene,world-full,
native-pass,native-build}.log`. The ordinary native asset remains SHA256
`876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043`;
no normal-profile deployment occurred. No game or GPU-test process remains.

This closes the measured cutout-versus-blended ordering defect in the covered
managed fallback paths. Accelerated and mixed-model ordering, resource-key
ties, the raster-state audit, applicable instanced distance fade and the prior
world-load buffer-retirement failure remain open. Subsequent clean runs have
not been called a repair of that lifetime failure. Full goal completion is
still unproven.


### 2026-10-19 material raster-state audit (in progress)

The pre-fix `--raster-only` scene failed on actual reversed MESH geometry,
OLD source, without a no-cull override (`lighting-raster-before.log`). The
fallback core/cutout pipelines had no culling; this was a visible contract gap.
Stock `MyRenderableComponent.MapTechniqueToRenderableFlags` plus
`MyProxiesFactory.GetRenderableProxyFlags` establish OLD technique/actor
no-cull state, then material Add/Remove masks apply before proxy shader state.
NEW GBuffer parts instead fix raster state by technique; actor no-cull creation
flags are retained for compatibility but do not affect the NEW renderer.
ALPHA_MASKED is two-sided; ALPHA_MASKED_SINGLE_SIDED and ordinary/cutout/
blended parts select the default clockwise back-cull state.

Production fallback packets now carry explicit main/forward raster state and
a separate OLD depth no-cull override through direct, retained, recipe and
model-template submissions. Indirect compatibility includes the raster flags.
Core Vulkan dynamic cull/front-face/depth-bias-enable state is set after model
pipeline binding. Raw packets without flags retain their previous defaults;
voxel/highlight pipelines keep their static policies. The OLD depth override
also disables stock view depth bias, because `MyDepthPass` replaces the whole
rasterizer with NocullRasterizerState. NEW preprocessed depth parts inherit the
view rasterizer (`MyPreprocessedPart.Init`, `MyDepthRenderPass`), regardless
of ALPHA_MASKED's main-view state. Fallback main, cascade, spot, probe,
object-motion and additional-attachment draw sites consume the appropriate
state; this code trace is not itself a live proof of every view.

The authored scene passes **189 checks**, including six techniques, both source
classes, front/back winding, actor flags, actual material override conversion
of NEW to OLD, exact prepared GBuffer equality, opposing stock cascade views
and OLD no-cull depth-bias removal. Core/synchronization validation is zero.
The full world suite passes **536 checks**, including actual instanced draws
with unequal raster flags that must not merge; direct/indirect pixels agree.
Native Release passes 15 runnable tests, one optional skip; the expanded CPU
indirect boundary test also passes independently. Logs:
`lighting-raster-{after,world-full,native-build,indirect-test}.log`.

A paired `-MeshRaster` live scene uses four techniques by four source/flag
columns. Every panel has front-facing left and reversed right geometry.
The live verifier requires actual main-depth coverage on all front halves;
only OLD actor NoBackFaceCulling may expose the reversed halves. This prevents
an all-sky match from passing. DX11 `2f0a66bfd02d451d8a06144a33db8ae7`
completed, removed all fixture actors, unloaded and exited (PID 40412 absent).
Its actual depth confirms the expected 16 front/4 back visibility pattern.
Vulkan `1319c242f98741b9b56931a883026584` is currently running (PID 7880).
Its prepared native SHA256 is
`448335943FDC98F2B1A4B08DC51551627F395F5805DDA4A1C8886941FC870857`,
managed SHA256
`BAE84DC302C32233CFC8AE1CB02961FC5CC6C1C487320C1DE50341548F794ABE`.
Earlier unused Vulkan preparation `d28d7ef5ce6348629e7f6eff5d1cdbf0` predates
the depth-bias refinement and was never launched or accepted. Paired live
appearance remains unproven until the running capture and comparisons finish.
The previous world-load lifetime failure is not repaired by these changes.


The raster live pair is now complete and reviewed. Vulkan
`1319c242f98741b9b56931a883026584` completed at 05:08:59Z, removed all
fixture actors and both decal packet classes, unloaded, disposed at 05:09:20Z
and exited (PID 7880 absent). It records 8 OLD/8 NEW, 4 blended/4 cutout
packets, validation zero and observed managed D3D11 creation attempts zero.
The paired verifier validates all sixteen front halves and exactly four
reversed halves in each backend's actual depth. Only OLD actor no-cull
columns expose the reversed geometry. Actual PNGs from both backends were
reviewed. The DX11 fixture completed its unload/exit path; as with previous
DX references its disposal marker is absent, and process absence was checked
separately. Vulkan has its disposal marker.

`lighting-raster-buffer-comparison.json` records exact G0, packed material,
main depth, screen AO and sun visibility on all sixteen interior ROIs.
Normal/AO decode maximum is below 2.5e-9. Largest panel mean absolute HDR RGB
difference is .000362/.000383/.000401, with isolated maxima
.06640625/.0634765625/.0625. `lighting-raster-live-comparison.json` verifies
pinned quality and normalized identical MWM/DDS bytes; largest panel mean RGB8
error is 1.351/.207/.210, P95 maxima 2/1/1 and isolated maxima 17/14/13.
These are measured fixture tolerances under the accepted appearance criterion,
not a claim about a proven arithmetic cause. No exposure fitting, brightness
adjustment or quality reduction was applied.

This closes the measured main-view fallback culling defect and verifies the
covered opposing-cascade and material-override cases. Other view draw sites
have the source-derived raster-state plumbing described above; broader final
view/accelerated-path coverage is still part of the completion audit. Adjacent
mesh-scene (156) and native-pass (121) checks pass with zero validation errors.
All fixture and game processes are gone. The ordinary native asset remains
`876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043`;
no normal-profile deployment occurred. Same-technique resource ties, mixed
OLD/NEW opaque/decal interleaving including accelerated sources, actual
instanced DISTANCE_FADE macros and the prior buffer-retirement failure remain
open. Full goal completion remains unproven.


Final raster-combination regressions also pass 1,325 focused mesh-decal GPU
checks and 424 probe checks with zero validation errors. Logs:
`lighting-raster-{mesh-gpu,mesh-scene,native-pass,probes}.log`.
The current harness source builds without warnings/errors after its raster
scenario scope text was clarified (`lighting-raster-harness-build.log`);
prepared live snapshots were not modified. Native and managed SHA256 remain
as recorded for the accepted Vulkan raster run. No game or GPU-test process
remains at this checkpoint; only the idle reusable MSBuild node is present.


### 2026-10-19 instanced DISTANCE_FADE implementation (validation in progress)

The frame threshold alone was insufficient. A fresh actual-scene negative
control (`lighting-distance-fade-before.log`) rendered 484 covered pixels for
an OLD generic instance with RenderFlags.DistanceFade when the stock macro's
custom-alpha fade should fully remove it. `MyRenderableComponent` appends
DISTANCE_FADE from AdditionalFlags and `MyMaterialShaders` emits that macro.
The source branch is inside generic/cube/deformed-cube instancing, so it has
no effect on rigid/skinned non-instanced source geometry. NEW shader bundles
ignore that OLD creation flag.

`generate-world-variants.py` now derives 246 additional instanced stock-macro
variants, covering standard/masked/metal, OLD cutouts and all blend/macro
banks, plus depth/forward/highlight partners. A disjoint +2560 bank preserves
stream/pass arithmetic (divisible by 32,20,5). Source flags and the OLD cache
key remain separate from main blend/write roles. Material selection enables
this bank only for applicable OLD instance streams; rigid batching of a
non-instanced source does not gain a fade it did not have in stock. The GPU
resident family identity includes this shader flag and its OLD pipeline
registration/admission understands the new bank. Probe supplement IDs alone
are remapped to forward slots; the new bank is not accidentally shifted.
These accelerated changes still require their own runtime proof.

Native pipeline classification and bounds, depth/forward/highlight lookups,
and motion/extra-attachment stream selection recognize the bank. The latter
views have not yet proven distance-driven deformation history; retain that
as an explicit view audit item. Packaging produces 1,274 authenticated
supplemental stages, with a 347,695-byte manifest under the raised finite
524,288-byte reader bound. Exact stage-count, per-definition hash and source/
module checks remain enforced. There are 620 world layout entries. Native
Release passes 15 runnable tests with the optional reference test skipped;
managed net48/net10 builds pass. Logs: `lighting-distance-fade-{native-build,
managed-build,package}.log`.

The focused scene currently passes **20 checks**, validation zero, including
484-to-zero generic coverage, exact retained GBuffer agreement, ordinary cube
near-field behavior, far cube position collapse, and rigid OLD/NEW controls
that remain visible with the flag. Stock cube/deformed output overwrites
customAlpha.y after the distance branch: unlike generic instances it does not
keep the distance-dither value. Its position scale starts beyond
max(DistanceFade,900). An early cube test incorrectly assumed generic dither
behavior; source review corrected the test rather than altering stock HLSL.
The far cube control uses threshold900, uniform model scale350 and a 10,000
far plane; the unflagged control has 484 pixels and the flagged control zero.
`lighting-distance-fade-after.log` is the current focused result.

This is not yet distance-fade closure. Deformed streams, partial-distance
curves, depth/forward/accelerated execution, relevant auxiliary views, and a
paired live DX11/Vulkan fixture remain to be verified. Full world regression
is currently running. Current private native SHA256 is
`99BD3D756EC1DDD870D8657B1D6BECFD4B41F5552708BFF96D0770C357A2C57F`.
The ordinary native asset remains unchanged; no normal-profile deployment.
Deeper resource/source ordering and the prior buffer-lifetime failure remain
open alongside the final audit.


The current full world regression has completed: **536 checks pass**, with
zero validation errors (`lighting-distance-fade-world-full.log`). Managed
net48 SHA256 is `E67F17E1F89A5409EA443887155E148E03A2785C6AFF674FA3A5B8F6439AB3B6`.
No game, package compiler or GPU-test process remains at this checkpoint.
The distance-fade validation items above remain open; this regression does
not establish their completion.


### 2026-10-19 distance-fade deformation, intermediate and cache audit

The scene matrix now includes authored nonempty blend weights/indices and
actual deformed cube source data. It verifies both explicit ForceOld and
ordinary actors converted by SetInstanceBuffer; stock MyRender11 explicitly
converts NEW actors to OLD for that message. Exact shader IDs and nonempty
packet counts witness generic/cube/deformed admission rather than accepting
an empty scene. The previous 20-check suite expanded through 34, 49 and 67
checks with zero validation errors. A single-layer equidistant quad provides
an analytic intermediate-fade oracle: three non-boundary alpha values
.23/.53/.83 are reproduced by setting DistanceFade = vertexDistance /
(1 + alpha/10). The actual distance shader agrees in all four GBuffer targets
with an unflagged shader using that explicit custom alpha. Both controls have
partial coverage and exact retained replay; no exposure/appearance fitting.

Generic, cube and deformed stock depth partners now have actual cascade
coverage controls. Full fade removes all shadow pixels; unflagged controls
retain thousands, with exact retained depth replay. This reached 79 checks.
A new fixed-actor/projection test then reproduced stale cached cascades:
threshold1000 had 16,384 depth pixels, and changing only the global threshold
to1 incorrectly retained those pixels (`lighting-distance-fade-cache-before.log`).
Native reflection now authenticates the shared Frame.DistanceFade offset from
DF vertex modules and includes its raw value in the cache signature only for
DF casters. It does not disable the cache or override explicit stock cascade
schedule masks. The 1000 -> 1 -> 1000 sequence now yields 16,384 -> 0 -> 16,384.
Steady-threshold cache-reuse coverage is being added separately.

The accelerated audit also exposed an independent source discrepancy: OLD
indexed pipeline defaults were no-cull, unlike stock default back-culling.
They now use the default clockwise back-cull pipeline; intrinsic part no-cull
selection is retained. Explicit OLD actor/material no-cull and broader depth/
view override coverage remain in the wider accelerated audit. The initial
whole-image mismatch persisted after that source correction; it was isolated
to one albedo pixel, with depth, packed normal/AO and packed material exactly
equal. At (38,35), resident G0 was [229,0,0,0], fallback [229,229,229,0].
Actual Shelf_1 geometry redirects to Shelf_1_LOD0 and includes cutout decals.
The indexed adapter's generic tint rewrite had incorrectly covered cutouts,
where fallback preserves the stock initializer. Restricting it to the same
core/forward generic policy (also excluding DF variants) restores exact
whole-image and every-GBuffer equality. Derivation contract is now
GpuOpaqueIndexedStock-2, invalidating old compiled derivatives. Corrected raw evidence:
`GpuSceneLiveTests/instance-{resident,fallback}-g[0..3].bin` and RGBA8 files;
pre-correction logs include `lighting-distance-fade-gpu-{raster-before,
diagnostic}.log`. Do not attribute the original whole-image failure solely
to culling; the color-policy correction was required. The pre-change scalar
byte values are retained above and in tool output, but the scratch binary
captures were overwritten by corrected reruns and are not a preserved
pre-change image set.

The next accelerated check found retained submission shader keys omitted
the effective DF flag: a changed GPU family could fall back to an old normal
prepared shader. Actor submission and saved LOD representations now retain
that flag and rebuild on changes. A dedicated retained false/true/false
roundtrip yields 484/0/484 pixels. The 85-check focused suite and initial
88-check GPU-resident suite pass, with validation errors zero. The latter
includes actual retained GPU ownership, no managed packet substitution,
fully faded fallback equality and ownership reentry, plus its earlier
hierarchy/appearance/hybrid cases. It emits the existing depth-fragment unused
color-location warning; zero errors must not be reported as zero messages.
Positive visible/intermediate resident controls are being added before claiming
accelerated fade closure. Both SE_VULKAN_GPU_OPAQUE=1 and
SE_VULKAN_GPU_OPAQUE_SCENE=1 are required for this test; the first attempt
omitted device admission and stopped before any scene work, not a hardware
capability failure.

Current native build and managed net48/net10 builds pass. Native CTest has
15 runnable passes and the optional reference skip. Private native SHA256 is
53CFE55973452752D631CB5F7EDCA5D4081008732619D6253B7D4B09CD40787E.
Spot/global-threshold caching, forward execution, relevant motion/attachment
history and the paired live fade fixture remain unverified; the broader
ordering and lifetime gaps also remain open. No normal-profile deployment.


The fixed current focused matrix passes **91 checks**, validation zero.
Repeating each threshold confirms zero cascade draw commands for unchanged
settings, while transitions still clear/restore the actual map. Thus the
new dependency fixes stale content without disabling steady-state reuse.
The expanded positive/intermediate accelerated run is currently in progress.


The expanded GPU-resident run passes **103 checks**, validation errors zero
(`lighting-distance-fade-gpu-resident.log`). Beyond fully hidden equality, it
proves actual GPU ownership with no managed packets for a visible DF variant
at threshold10000 and an intermediate variant at5.1234. Each differs from
fully hidden output; the intermediate case also differs from the near-field
control. Both match fallback pixels exactly and survive ownership reentry.
This verifies the DF tint-policy correction on visible geometry, rather than
allowing an all-sky comparison to certify it. Source settings and prior family
restore their exact pre-fade pixels. The existing unused depth-fragment-output
warning remains as described above; no validation errors occurred.
Full world regression is being rerun against this native/managed combination.


Final current-combination regressions pass **536 world** and **121 native-pass**
checks with validation errors zero. The Creative harness builds with no
warnings/errors after the private helper gained deformed and single-layer
fixtures. Logs: `lighting-distance-fade-{world-full,native-pass,harness-build}.log`.
Managed net48 SHA256 is
EC20D18F26BABEA833534A025779DE50CF56700FC4A50A6DBB3C99B8D582BDDE;
private native remains 53CFE55973452752D631CB5F7EDCA5D4081008732619D6253B7D4B09CD40787E.
The ordinary native asset remains
876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043.
No game, package compiler or GPU-test process remains at this checkpoint.

The paired fade fixture should include genuine colored generic instances.
Template.hlsli enables PASS_OBJECT_VALUES_THROUGH_STAGES for generic streams,
and the stock pixel initializer defaults color_mul to1. The existing generic
ObjectTint derivation was introduced for rigid-to-generic optimization, while
current core slots also serve genuine generic sources. Verify that distinction
against actual DX11 behavior; accelerated/fallback equality alone cannot
certify stock color semantics for this shared alias. This is a remaining
source/fixture audit requirement, not a closed claim or an accepted tolerance.
Distance-fade closure remains unproven pending the remaining view/caching,
auxiliary-history and paired live checks. Ordering/resource ties, broader
accelerated raster overrides, and the earlier lifetime error also remain open.


Spotlight distance-fade cache follow-up (2026-10-19): a same-actor,
same-light, same-projection threshold change left 7,396 stale shadow texels
before the fix (`lighting-distance-fade-spot-before.log`). The first admission
frame required a warm draw; that test setup issue is separate from the stale
cache reproduction. Native spot dependencies now hash reflected fade threshold,
actual object/instance constants, geometry and source membership separately for
static/dynamic casters, limited to lights whose ranges/cones admit them. Changed
static depth preserves any unchanged dynamic overlay. Empty membership also
invalidates removal; unchanged input retains reuse. No-shadowed-light scenes
skip the dependency scan.

`lighting-distance-fade-spot-after.log` passes 120 focused checks with validation
errors zero on native 0B9CE40F5219EB131F23A542E69FD620016CE31D5FF68ADADB747D987A7D7052:
threshold 1000/1/1000 and camera-relative distance round trips remove/restore the
same 7,396 texels, change only the affected map, and preserve steady reuse. The
final no-active-spot early return is in native
C747C40B636C7545FC4ABD12EE56E43A879DE595E87B29612C9B17A657230023;
its native build passes 15 tests plus the optional skip. A current-combination
GPU rerun is still required. GPU-only/hybrid spotlight fade dependencies remain
unverified and are not covered by the CPU world-draw signature proof. Forward,
auxiliary-history, and paired active-fade evidence remain open.

Entity diffuse-color investigation: the private DX11 runs
6b853c2ce14748a2ad23bb7cece2c157 and
624ab15db58d4279a1087948381576ec both show neutral G0 centers
(0.45703125 per RGB channel) in all 16 panels: rigid OLD, genuine generic OLD,
rigid NEW, and inactive-DF generic OLD under white/red/green/blue API values.
MyRenderProxy.CreateRenderEntity already sends UpdateRenderEntity, so the first
run was already testing that update; the second repeats it explicitly. Stock
MyRender11 ignores DiffuseColor in UpdateRenderEntity and character creation.
Vulkan applied both. The focused `--entity-color-only` test fails on the old
update behavior, then passes 46 checks after ignoring these legacy entity fields,
including all GBuffer channels and prepared reuse. Material tint and HSV handlers
remain independent. Logs: lighting-entity-color-{before,after}.log. The paired
live Vulkan result is pending; this does not settle genuine generic versus
optimized-rigid *material* tint semantics. No shader alias redesign is justified
by the entity-color fixture alone.


The entity-color paired capture is now verified: DX11
624ab15db58d4279a1087948381576ec and Vulkan
ec922d049ce24d62816ee5cbabdf7601 have all 16 visible-depth panels, source
12 OLD/4 NEW and 8 genuine generic actors (16 actual DX11 generic proxies),
plus 8 Vulkan DF packets. Reviewed PNG interiors and HDR are exact in all
panels. G0 decoded delta is 0.00062018633 per RGB channel (SRGB representation),
G1 decode delta <2.5e-9; material/depth/AO/sun buffers are exact. Reports:
lighting-instance-color-update-{fixture,buffer}-comparison.json. Both runs
completed/unloaded/requested exit; actual Legacy/SpaceEngineers process absence
was checked, and Vulkan reported harness disposal, zero validation errors,
zero actors/packets on cleanup, and zero observed managed D3D11 creation attempts.
Current managed SHA256:
67D7D2357004E23CA8724064333D0C55420096A9D1289866D7E50E6970AD3AE7;
private native C747C40B636C7545FC4ABD12EE56E43A879DE595E87B29612C9B17A657230023.
Ordinary native asset remains 876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043.

A separate --MeshInstanceMaterialColor fixture now sends stock
UpdateColorEmissivity for every authored part, in addition to the ignored entity
color fields, to distinguish genuine generic material tint from rigid optimization.
Its DX11 run e5627eae4dc8490d8f81cde66a37f8c4 is launched; no result is claimed yet.


Material-tint oracle follow-up: DX11 e5627eae4dc8490d8f81cde66a37f8c4
shows effective red/green/blue tint for rigid OLD and NEW, while genuine generic
OLD and inactive-DF generic OLD stay neutral. Vulkan before-fix run
0bef6a7fb9f54f6e834ed4d2147965f5 wrongly tints the non-DF generic row; for a
red update its missing green/blue G0 channels differ by 0.45703125 each.
The DF row already matches stock. Both captures completed cleanup and exited;
actual Legacy process absence was checked. Diagnostic decoded comparison:
lighting-instance-material-before-buffer-comparison.json. The focused test
also fails specifically at the genuine-generic red material update in
lighting-instance-material-before.log.

The correction preserves the existing derived generic tint shader for internally
batched *rigid* sources. Genuine instanced sources pack neutral color_mul in the
legacy, cached-part, and prepared material paths, as stock PASS_OBJECT_VALUES_
THROUGH_STAGES requires. Rigid sources retain material tint before any internal
batch conversion. Indexed GPU generic shaders now retain the stock initializer;
the removed tint rewrite was borrowed from the rigid batching optimization.
GpuOpaqueShaderAdapter cache contract is now GpuOpaqueIndexedStock-3.
No shader-bank expansion or stock HLSL edits were required. The expanded focused
suite passes 82 checks with validation errors zero, including ignored entity
updates preserving actual material tint. Resident and after-fix live validation
are pending at this checkpoint. Prepared after-fix Vulkan run:
023aeba5c74b41ffadd98a6dc768d611, managed
935A9F8D7FA023A4198327D8FE15F7C249F26B2D21D72D4B639B8BE8F94A4D79.


The updated resident suite passes 112 checks on the current material fix
(`lighting-instance-material-resident-after.log`), including genuine-generic
material-tint invariance, exact fallback equivalence, ownership without managed
packets, and reentry/roundtrip behavior. All previous visible/intermediate DF,
hierarchy and hybrid checks also pass; validation errors zero. The existing
unused fragment output/depth-only rendering warning remains present and is not
represented as a warning-free run. The final material-tint live run
023aeba5c74b41ffadd98a6dc768d611 has now launched.


Final material-tint proof: after-fix Vulkan 023aeba5c74b41ffadd98a6dc768d611
matches DX11 e5627eae4dc8490d8f81cde66a37f8c4 in all 16 reviewed panel RGB8
interiors exactly. Rigid OLD/NEW rows tint; both genuine generic rows stay
neutral. HDR largest panel mean absolute RGB differences are
7.8591e-6 / 2.14791e-5 / 1.61748e-5, with maximum channel difference
3.14713e-5. Decoded G0 differs by at most 0.00062018633 per RGB channel;
material/depth/AO/sun are exact. All 16 panels have finite positive depth
witnesses in each backend. Source admission and no managed D3D11 creation
attempts in Vulkan pass the verifier. Both runs completed, unloaded, requested
exit, and actual Legacy/SpaceEngineers process absence was checked. Reports:
lighting-instance-material-after-{fixture,buffer}-comparison.json.

Current-combination regressions pass 120 distance-fade, 536 world and 123
native-pass checks, validation errors zero. This also GPU-tests the final
no-active-spot early return in native C747C40B636C7545FC4ABD12EE56E43A879DE595E87B29612C9B17A657230023.
Logs: lighting-material-color-{distance-fade,world,native-pass}-regression.log.
Native-pass tests now use actual material updates for resident appearance and
mixed glass tint; a separate assertion verifies legacy entity diffuse updates
leave mixed-glass descriptors unchanged. They no longer assert the obsolete
entity-color behavior. The 82 focused and 112 resident results above remain
valid for this unchanged production code. The world test build retains its
existing CS8632 nullable annotation warning; the native-pass build is clean.
No test/game process remains. Ordinary native asset hash is unchanged.

These checks close the entity-color and genuine generic material-tint audit
items. They do not close same-technique OLD resource-key ties, mixed OLD/NEW
opaque/decal and accelerated interleaving, remaining forward/probe/auxiliary
fade proof, GPU-only/hybrid spotlight fade cache coverage, broader accelerated
raster overrides, the intermittent in-flight buffer destruction report, or the
final whole-scope completion audit. Full light/material parity remains active.


GPU spotlight fade investigation (2026-10-19): the resident test now creates
actual shadow-casting scene lights and checks cached depth while the OLD
instanced actor remains GPU-owned without managed packets. Native C747... leaves
578 stale depth texels when only DistanceFade changes from 10000 to 1;
lighting-gpu-spot-fade-before.log preserves the failure. Stock GPU predicates
tracked selection and depth revision, but lacked the threshold/camera fade
inputs. CPU fade invalidation also ran after GPU predicate preparation, which
could miss CPU-only changes in a hybrid spotlight.

The fix adds internal shader dependency metadata to derived stock parts, a
reflected threshold input to the 120-byte internal compute push constants
(still below the Vulkan 128-byte baseline), and a third uint4 signature for
selected DF depth casters containing the actual float camera-relative actor
translation and threshold bits. Public actor/part/frame ABI sizes do not
change. Non-DF and unselected casters retain zero fade signatures. CPU fade
hashing now precedes the GPU selector dispatch, using the already fenced world
uploads, so hybrid force-redraw flags reach the same frame's predicates.
Signature allocation grows from 32 to 48 bytes per actor/view; slot fences and
existing dispatch barriers remain in place. Native build passes 15 tests plus
its optional GPU-reference skip.

The initial after-fix resident suite passes 127 checks with validation errors
zero, including the formerly stale threshold roundtrip and off-cone control.
Native SHA256: 99C4272D8C5EDFC0DD5422C4E131F1A525FC594FB6C64C89E58CF9C4C89011BE.
The expanded camera and CPU-only/hybrid-caster tests are running; those cases
and current-combination regressions are not claimed passed yet. Logs:
lighting-gpu-spot-fade-{native-build,after,expanded}.log. The existing unused
fragment-output warning remains. This does not resolve other ordering,
forward/auxiliary-fade, accelerated raster or lifetime gaps.


The expanded spotlight suite now passes 158 checks with default inputs and
another 158 with SE_VULKAN_GPU_OPAQUE_OWNED_INPUTS=1. It verifies cached depth
under threshold 10000/1/1/10000, fixed-light camera 0/5/5/0/0, positive-depth
controls, exact restored maps, and off-cone empty maps while the actor stays
GPU-owned without managed packets. A second genuine generic actor is forced
into managed submission using an explicit admission limit; only this CPU caster
fades while the GPU caster stays non-DF. Its disappearance/restoration and
removal reproduce the exact fixed-GPU/mixed depth controls. This establishes
the same-frame CPU-to-GPU invalidation order rather than letting a GPU fade
change mask the hybrid case. Internal GPU stage8 already propagates changed
static predicates to their dynamic overlays.

Current-combination regressions also pass 120 CPU distance-fade, 536 world and
123 native-pass checks with validation errors zero. Canonical native build
passes 15 tests with the optional GPU-reference test skipped. Logs:
lighting-gpu-spot-fade-{expanded,owned-inputs,cpu-regression,world-regression,
native-pass-regression}.log. The repeated unused depth-fragment-output warning
remains documented; this is not a warning-free claim. Both compute/data layouts
and fenced owned-part uploads are exercised. Native remains
99C4272D8C5EDFC0DD5422C4E131F1A525FC594FB6C64C89E58CF9C4C89011BE;
ordinary asset remains 876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043.
Managed production code remains the previously verified 935A9F... version.
Actual game/GPU-test process absence was checked after completion.

This closes the reproduced GPU-resident and hybrid spotlight fade-cache gap
for the tested generic-stream cases. Shader-class metadata is derived from
actual depth pipeline IDs for all DF variants, rather than assuming an actor
flag implies a particular shader. Remaining whole-goal work: forward/probe and
auxiliary-history fade evidence, paired active-fade views, same-technique OLD
resource-key ties, mixed OLD/NEW/accelerated opaque-decal ordering, broader
accelerated raster overrides, the intermittent buffer-lifetime failure and
final finite coverage audit. No completion claim is made for those items.


Forward/probe fade follow-up (2026-10-19): stock MyCommon sets the global
FrameConstantsData.DistanceFade from current render settings. Stock AddProbe
uses the current camera and that frame context. VulkanProbes constructed its
six face frames without renderSettings, so WorldConstants supplied zero. The
focused capture test reproduces missing geometry with a nominally inactive
DF generic shader at threshold 10000, while the non-DF forward control has
positive captured geometry. Log: lighting-probe-fade-before.log. The optional
Configure argument was exposed for the reproducer before connecting the field;
the old frame-building behavior was retained for the negative run. An initial
headless MyRenderSettings.Default initialization failed before capture and was
corrected to explicit test settings; that setup failure is not the product
reproduction.

MyVulkanRender now passes its current renderConfiguration into probe Configure,
and each newly captured face forwards it to WorldConstants.Frame. Existing
hold/scheduling and publication rules are preserved. The focused suite passes
24 checks: visible/full fade, analytic .23/.53/.83 partial dither, and exact
roundtrip radiance, validation errors zero. The full probe suite passes 482
checks including indexed GPU forward captures, validation errors zero. Logs:
lighting-probe-fade-{after,full-regression}.log. Cube/deformed forward cases have
been added but are not run yet. Native remains 99C4272D...; current managed
net48 SHA256 is 4E3CA86BA4FFB420CF3F6A3336B5B14F0273EC4F02D0CE3A21DE82EEADA9A703.

A separate paired live --MeshInstanceFade scenario uses actual generic instance
buffers and DistanceFade=8.5, with non-DF OLD/NEW controls and a partially faded
DF row. The verifier requires partial finite depth coverage on faded panels,
rather than treating missing geometry as an appearance match. DX11 run
9a834e81ffa1475ebee0e0b79f15356c is launched; Vulkan
5b0928b4ce4046e6a1ee095ab07d500d is prepared, unlaunched. Earlier unused
preparations d7e20058... and c0ebdd7f... were superseded before launch to correct
the fixture scope caption; their snapshots were not mutated. Paired active-fade
appearance remains unproven until these captures complete and are reviewed.


Forward fade coverage now passes 44 focused checks, adding cube and deformed
streams to the generic analytic cases. Near cube DF preserves stock's later
overwrite of dither alpha, while scale-350 distant controls produce real probe
geometry at threshold 900 and the DF variants collapse it; threshold 10000
restores their exact original radiance. This verifies actual forward material
pipelines, not just GBuffer equality. The prior full 482-check probe regression
covers the same unchanged production code, including indexed NEW forward and
nineteen-frame publication. Logs: lighting-probe-fade-expanded.log and
lighting-probe-fade-full-regression.log. The scratch fade-before-or-after.rgba16f
is the latest after-fix capture; negative evidence is the preserved failure log,
not a retained before-image binary.

Paired active-fade live proof is complete: DX11
9a834e81ffa1475ebee0e0b79f15356c and Vulkan
5b0928b4ce4046e6a1ee095ab07d500d. Their raw 1280-byte frame blocks contain
DistanceFade=8.5 at authenticated reflected offset1276. The proxy settings
snapshot still says2000 because the explicit full-settings switch does not
update MyRenderProxy.Settings's static copy; it is not the rendered threshold.
Both comparison scripts now require the actual captured frame value to match
the authored threshold. All four DF panel interiors have real partial coverage
(1283,2611,2832,760 of3721 pixels), with exact cross-backend depth masks and
geometry depth. Materials, AO and sun visibility are exact; G1 decode delta
<2.5e-9 and decoded G0 has the previously observed0.00062018633 channel delta.
All foreground HDR pixels match exactly, including the faded row. Remaining HDR
differences occur only on background sky exposed by fade holes: largest panel
mean RGB0.00013109/0.00013741/0.00014149; maximum channel0.025390625. Reviewed
panel RGB8 mean deltas are at most0.01720/0.01828/0.01613 of a channel step.
These scoped observations satisfy the user's feature/appearance standard with
documented tolerances; no geometry/dither coverage tolerance was needed.
Reports: lighting-active-fade-{fixture,buffer}-comparison.json and
lighting-active-fade-hdr-diagnostics.json.

Source admission is16 actors,12OLD/4NEW,8genuine generic, with16 actual DX11
instanced proxies and8VulkanDFpackets. Capture/cleanup validation errors are0;
Vulkan managed D3D11 creation attempts are0. Both runs completed/unloaded and
requested exit, and actual Legacy/SpaceEngineers process absence was checked.
Vulkan disposal was observed; DX11 disposal marker was absent, as reported by
the verifier. Private native remains99C4272D... and managed remains4E3CA86B...;
ordinary native asset is unchanged. No test/game process remains.

This closes the missing render-settings input to forward/probe captures and
provides the paired active generic-fade appearance proof. Dedicated indexed
OLD DF forward captures remain to be exercised; the indexed full-suite controls
are NEW non-DF. Auxiliary motion/reactive history, ordering/resource ties,
broader accelerated raster overrides, the intermittent buffer-lifetime failure
and the final whole-scope audit remain open. Full parity is not complete.


Indexed OLD forward-fade audit (2026-10-19): the dedicated test admits all nine
main/depth/forward DF shader classes for generic, cube and deformed streams,
then uses the production model/family/source/actor registry and no managed world
draw packets for indexed captures. It compares visible, analytic partial and
fully faded radiance against actual fallback captures in both frame slots,
with actor-removal controls. Initial setup diagnostics are retained separately:
the required scene feature flag was absent in the first run, and redundant
pipeline registration was corrected to the canonical production admission path.
A raster mismatch was isolated to the authored quad's opposite winding: matching
no-cull state gave exact radiance. The fixture now uses stock-facing winding
and explicit stock back culling on its fallback control. No renderer culling
change was made. Retained diagnostic logs:
lighting-indexed-probe-fade-{environment-diagnostic,registration-diagnostic,
first-capture,diagnostic,raster-diagnostic}.log; mismatched and no-cull raw
radiance captures remain in ProbeGpuTests/indexed-fade-2-10000-1-0-*.

The proper production route then exposed a separate real gap: deformed sources
were accepted by the resident registry but rejected during publication by a
restriction shared with the standalone numerical reference renderer. Generic
and cube controls passed; deformed publication failed in
lighting-indexed-probe-fade-resident-before.log. Native validation now records
whether geometry uses authenticated shared stock shaders. Those shaders support
bone deformation; the standalone numerical reference still rejects it. Cube
orientation, finite translation and source-range checks remain enforced. Public
actor/part/frame ABI sizes are unchanged. The after-fix suite passes116 forward
checks, including all three stream classes, both slots and explicit numerical
reference rejection, with validation errors0. Native build passes15 tests plus
its optional skip. Current native SHA256:
C3D00C34A0E4E2D708EFC022099C954E9AEC436DE302A6C4E622B0461D7DAD48.
Logs: lighting-indexed-probe-fade-{native-build,resident-after}.log.

The GPU-owned input variant and a normal VulkanScene producer regression are
running. The latter adds real authored blend weights, packed cube-bone source
updates, ownership/fallback/reentry comparisons, and far DF collapse/restoration.
Their outcomes are not claimed at this checkpoint. Managed production remains
4E3CA86B...; ordinary native asset remains unchanged.


The owned-input indexed suite also passes116 checks with validation errors0.
The normal VulkanScene regression exposed the corresponding managed admission
exclusions: enabled skinning and packed bone flags removed deformed buffers
from GPU ownership even when the stock shader class was available. The class4
path now admits them; skeletal/dither exclusions remain unchanged and class3's
nonzero packed-flag restriction is retained. The test uses a real exported MWM
with blend weights and real cube instance updates, rather than only native API
inputs. A first geometry-change oracle used a fully metallic black control;
its final RGBA did not change in either GPU or fallback despite valid geometry.
That is not treated as a rendering failure. The corrected neutral nonmetallic
control requires nonempty final color and verifies all four GBuffer channels,
positive geometry coverage and no managed draw packets. Diagnostic logs and
black-control captures remain available; they are not acceptance images.

The complete scene suite now passes180 checks in each GPU input mode. New cases
verify genuine class4 ownership, real bone updates, exact GPU/fallback GBuffer
and final-pixel agreement, reentry, far scale350/depth1050 controls, threshold900
position collapse and threshold10000 exact restoration, then cleanup. Logs:
lighting-deformed-producer-{default,owned}.log; previous admission failure:
lighting-deformed-producer-admission-before.log; black-control diagnostic:
lighting-deformed-producer-diagnostic.log. These tests explicitly require zero
managed world packets while claiming GPU ownership.

Final broad regressions pass482 probe checks (including indexed NEW forward),
536 world checks and123 native-pass checks with validation errors0. Logs:
lighting-indexed-probe-{full,world,native-pass}-regression.log. The current probe
suite was rebuilt against final managed production sources before the final
482-check run. Canonical native tests pass15 with the optional numerical GPU
reference test skipped; the dedicated indexed suite explicitly checks that
unsupported deformed sources still fail in the standalone numerical reference.
Existing unused fragment-output warnings remain, so these are not warning-free
claims. Managed net48 SHA256:
C7B3C60C4F8C7A0C660FA0118B39FE4B09D43709166CF7E23B90B172124515C5;
private native C3D00C34A0E4E2D708EFC022099C954E9AEC436DE302A6C4E622B0461D7DAD48.
Ordinary native asset remains876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043.

This closes the dedicated indexed OLD DF forward capture requirement and the
newly reproduced deformed-source admission gap in both the registry publication
and normal scene producer. It does not close auxiliary motion/reactive history,
OLD resource-key ties, mixed OLD/NEW/accelerated opaque-decal interleaving,
broader accelerated raster overrides, the intermittent in-flight buffer
lifetime error, or the final whole-scope audit.

Next history/lifetime investigation: object_motion.inc reads previous-slot
projection and instance buffers, while world_buffer can immediately destroy a
current-slot buffer on growth after only that slot's fence. A prior submitted
motion pass can still reference that slot as its previous input. Existing
readback-heavy tests serialize frames and may hide this pattern. This is a
source-backed hypothesis, not a verified cause of the earlier00922 report.
The next proof should submit steady frames without per-frame readback, then
grow/recycle world buffers under core and synchronization validation, before
choosing a lifetime or history implementation change. DF position scaling is
also absent from the motion shader, so history fidelity remains unproven.

### Asynchronous previous-frame buffer lifetime reproduction and fix

The new `WorldPassGpuTests --buffer-history-only` mode reproduces
VUID-vkDestroyBuffer-buffer-00922 on native C3D00C34 at the first instance
buffer growth (32768 instances). It submits three steady frames at each of
4096, 32768 and 131072 instances without per-frame readback. The allocation
diagnostic identifies a 262144-byte vertex buffer owned by world_buffer.
The motion pass borrowed the previous slot's instance/projection buffers;
waiting for that slot's own older frame did not wait for the newer motion
submission still reading it. This proves the reproduced failure's cause,
but does not retroactively identify the handle in the original live report.

Each current slot now owns a CPU-copied snapshot of previous projection and
the required valid instance span, protected by that slot's existing fence.
Motion and attachment bindings use those snapshots, with valid byte ranges
tracked separately from allocation capacity. No extra GPU wait is added.
The attachment path had the same borrowed bindings; its independent active
contribution to the original failure has not been established.

Private native AACB5DBEBBA3AF81E278134DC20BFE0138526AD20D2FAF50EADC932D8BEB1120
passes all 22 focused checks with an initialized attachment consumer and
zero validation errors. Canonical native tests pass 15, with the optional
GPU-reference test skipped. Evidence: TestResults/lighting-buffer-history-
{before,after,native-build,build}.log. The stale-build diagnostic executed an
older binary after a missing-using compile error and is not acceptance proof;
the attachment-entry diagnostic used the wrong shader entry point before
correction. Both diagnostic logs are preserved. Broader regression and live
world-load verification remained pending at that focused checkpoint. Subsequent
world regression passes all 536 checks; native-pass regression passes all 123
checks with zero core/synchronization validation errors. Logs:
lighting-buffer-history-{world,native-pass}-regression.log. Live world-load
verification is still pending. DF motion/reactive fidelity, ordering,
accelerated raster overrides and the final whole-scope audit remain open.

The live Anomaly world-load run 640fe66a7d844c799963b218ecc3f7e6 used native
AACB5DBE and managed C7B3C60C from an immutable private snapshot. It completed
the producer/interior checks, simulation fixture, controlled unload, plugin
disposal and actual process exit. Final status records 7706 submitted / 7705
presented frames, validation enabled/errors0 and device-lost0. The bounded
SharpDX managed-call observer records no D3D11 creation attempts. All five
catalog inputs were simultaneously ready at the producer gate, and the
volume-on capture was visually inspected. Reports, off/on captures and logs
remain in that run root. This closes the live-load verification of the
reproduced previous-frame buffer lifetime fix; it does not prove DF history
fidelity, accelerated auxiliary coverage or the remaining ordering/raster work.

### Stock instanced distance-fade geometry in motion and attachments

Stock Geometry/VertexTemplateBase.hlsli measures the unscaled camera-relative
instanced vertex, then shrinks the object-space position by
1-saturate((distance-max(DistanceFade,900))/(DistanceFade/10)). The auxiliary
object_motion.hlsl path omitted this transform, so equal-depth motion and
attachment rendering missed geometry surviving the stock GBuffer draw.

`WorldPassGpuTests --distance-history-only` provides an independent CPU
projection/interpolation oracle and samples the actual native motion and
attachment images through guest passes. Against native AACB5DBE plus the
original shader, the generic partial-fade case has 232 surviving pixels with
missing attachment data (lighting-distance-history-before.log). Non-faded
rigid and generic controls pass before that failure. The before shader was
copied from the immutable live snapshot into the private test stage; ordinary
native assets were not replaced. Capture scratch files are not a preserved
before/after pair; the negative log is the retained reproduction evidence.

The fixed shader applies the stock shrink to current and previous positions
using each frame's own threshold. Native push constants reuse the existing
screen.zw words (112 bytes total). The threshold is carried with a negative
sign (including -0), and +0 disables the operation for non-DF draws. This
preserves the prior zero-filled words' disabled behavior without changing
threshold precision. Attachment projection uses the current threshold. No public
ABI, stock shader or main GBuffer geometry changed.

Final focused coverage passes 284 checks with validation errors0. Cases cover
rigid controls plus generic/cube/deformed-cube instancing both with and without
DF; translated frames; threshold10000 ->900 ->10000; both frame slots; every
surviving depth pixel's attachment; and motion from current/previous transforms
and thresholds. The oracle checks interior pixels with an absolute .01-pixel
tolerance (maximum observed error below .002 pixels). Initial test compilation
needed a missing VRage.Import using; the failed build did not execute a stale
binary. Final logs: lighting-distance-history-{build,after,native-build}.log.
Native tests pass15 plus the optional GPU-reference skip. Final native SHA256:
42BDA4A6383035746CF01C0914B9F742E0FBD1B9760408EBD37D41D5405ECBE7;
object_motion.hlsl SHA256:
ED4A4BE01B10141D41EC6358FDA284CDF9F4441C0CCC6FB35423775E1EA8EE12.
The preceding 06A8FA73 build used a negative disabled sentinel; it also passed
the focused284, lifetime22 and world536 checks, but was superseded to preserve
the old native zero-word behavior. Its initial focused/world logs are retained
as lighting-distance-history-initial-{encoding,world-regression}.log.

This proves stock shrink/history fidelity for the covered native CPU-submitted
core draws. GPU-owned/native-pass auxiliary motion/attachment coverage remains
unproven: those sources are not part of world.current().draws. Same-technique
resource ties, mixed-source interleaving, accelerated raster overrides and the
final whole-scope audit remain open. Final 42BDA4A6 broad world regression passes
536 checks with zero validation errors (lighting-distance-history-world-
regression.log). Ordinary native asset remains
876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043;
no normal-profile deployment occurred.

Next auxiliary audit should include multiple draws sharing the same mesh and
instance-buffer handles but using different instance offsets: the motion
binding cache currently compares handles without comparing those offsets.
That is a source-identified risk, not a captured regression from the one-draw
focused oracle. GPU-owned/native-pass coverage is likewise still open.

### Shared-buffer motion binding offsets

The expanded independent history test reproduced the source-identified cache
gap on native42BDA4A6: two draws share one mesh and instance-buffer handles but
use different instance spans. First-draw motion matches the oracle, while the
second draw has an 8.53333-pixel error because its vertex binding retained the
first span. Evidence: lighting-shared-instance-history-before.log. As above,
raw scratch captures are overwritten by later runs and are not preserved as
a before/after image pair.

The motion cache now compares vertex, index and instance offsets in addition
to buffer handles. Rebinding updates those offsets together. The final focused
suite passes497 checks with zero validation errors: the previous284 controls,
two independently moving draws for generic/cube/deformed streams with and
without DF across both slots, and two rigid meshes with nonzero vertex/index
offsets in shared arena pages. The latter uses different depths and indices
3..5 for the second mesh; its first three vertices are offscreen, so using the
wrong vertex/index range cannot silently satisfy the motion oracle.

Final native SHA256:
7493AA6899F4953BDF7E447810107C64849ACA635A91B3FC8559EB23C45C2FB0.
The shader remains ED4A4BE0 from the prior section. Native tests pass15 with
the optional GPU-reference skip. Logs: lighting-shared-instance-history-
{build,native-build,after}.log. Final broad world regression passes536 checks,
zero validation errors (lighting-shared-instance-history-world-regression.log). This
closes the demonstrated CPU-submitted shared-buffer motion cache gap;
GPU-owned/native-pass auxiliary coverage, resource ties, mixed-source ordering,
accelerated raster overrides and final whole-scope verification remain open.

### Native-pass attachment coverage and reproduced motion omission

Both auxiliary loops were limited to world.current().draws, excluding native
pass groups and indexed GPU draws. New NativePassGpuTests
`--auxiliary-history-only` drives a real resident actor with zero managed world
packets and samples the actual native motion/attachment images. Native7493AA68
renders72/78 main depth pixels but leaves every attachment pixel empty; after
a .5m move at z=-5 in a64px viewport, the independent motion expectation is
-3.2px and observed motion remains0. Evidence:
lighting-native-pass-auxiliary-before.log. This is an actual missing path,
not a validation error (errors0).

Native pass Colour/view0 groups now render into live GBuffer attachments using
their existing64-byte simple instance stream, exact mesh first/count ranges,
shared geometry offsets and intrinsic cull state. They retain instanced draws.
The generic auxiliary vertex path projects the stream transform with an
identity object matrix. A negative attachment-only screen width selects those
per-instance rows as the attachment payload; CPU-submitted generic draws keep
their prior object-row payload. No public ABI or stock shader changed.

`--attachments-only` passes41 focused checks with two independently transformed
actors in one group, zero managed draw packets, positive coverage for both,
all144/150 surviving depth pixels covered, correct per-actor translation rows,
both frame slots and validation errors0. The same mode deliberately reports
the still-missing motion (3.2px error); it is not whole-auxiliary acceptance.
The stricter `--auxiliary-history-only` mode continues to require motion.
Native tests pass15 with the optional GPU-reference skip. Logs:
lighting-native-pass-{auxiliary-build,attachment-native-build,attachment-after}.log.
Native SHA256:
62AA40F95C35E25DE7790E4523E32B171F12EBD87494C2D2E6031BB5B6C9C244;
object_motion.hlsl SHA256:
39717CE2C290D5CC0A0A86A1F2778161E6E4E6E7B6BA357807D6C5CA0EF8D6BF.
CPU history regression passes497 checks; the broad native-pass suite passes123,
both with zero core/synchronization validation errors. Logs:
lighting-native-pass-attachment-{cpu-history,world}-regression.log. Indexed GPU
attachments and native-pass/indexed motion remain open, as do ordering,
resource ties, accelerated raster overrides and final whole-scope verification.

The final strict auxiliary run (lighting-native-pass-motion-gap.log) passes
coverage and both actor-transform payload checks, then fails specifically on
motion: all150 current depth pixels have attachment data, but the moving
actor's motion error remains3.2px. That red optional lane is intentionally
retained as the next acceptance gate; the attachment-only pass does not
supersede it. Next implementation needs identity-aware previous native actor
transforms in fenced current-slot storage, preserving native instanced groups
and camera projection history rather than borrowing another slot's buffers.

### Native-pass actor motion history

Native Colour/view0 groups now contribute to the motion target using their
existing instanced streams and exact geometry ranges. The prior transform is
matched by the actor's generational native handle, independent of group/stream
offsets, material activation and camera-relative pose rewrites. The scene
publishes those identities alongside instance rows; no public ABI changed.
Only unique main-view streams get history storage. Each current slot owns its
prior-row buffer; the previous-frame CPU row map is read before replacement,
and no submitted motion pass borrows another slot's instance buffer. Cleanup
releases both history buffers. Nonconsecutive submitted world frames invalidate
the CPU map. New/recreated/newly visible identities carry invalid prior rows,
which the generic motion shader explicitly maps to zero object motion.

The strict optional lane now passes156 checks, including the original3.2px
motion reproduction, both slots, first-frame zero, restoration, a material
stream change, same-owner recreation with a different handle, camera
translation/roundtrip, a two-menu-frame gap and subsequent recovery. The
independent pixel expectation uses .01px tolerance; largest observed moving
control error is .0007813px. Attachments retain complete144/150/156-pixel
coverage and per-instance transforms. The test then grows one instanced group
64 ->1024 ->4096 actors, three steady frames at each size, with no per-frame
readback; a final depth readback proves completion. Core/synchronization
validation remains enabled with errors0. Actors are registered dormant before
the overlap sequence, so registration does not serialize each growth boundary.

The new menu-gap case initially uncovered a distinct existing bug: menu shadow
render area and mesh viewport were hardcoded1024 after world shadow quality
had resized their target to256. The old run emitted VUID06079/06080 and then
device loss before returning to the world. It is not evidence of a history
counter failure. Both dimensions now use actual shadow_resolution; the same
menu gap and return pass cleanly. The initial diagnostic is preserved in
lighting-native-pass-{history-gap-before,menu-shadow-diagnostic}.log. The first
history implementation (0C8BFAC0) passed106 controls before this extension;
lighting-native-pass-motion-initial-history.log retains that scoped evidence.

Final native SHA256:
5CDE11395D0F91402BB529229459478858118FC0D8648BE959D48C81164783FD;
object_motion.hlsl SHA256:
17B33EF2AD11455F20745DE6EBB8E5B892111ADD4A40A373D7135309019F62B5.
Native tests pass15 with the optional GPU-reference skip. Logs:
lighting-native-pass-motion-{build,native-build,after}.log. Final broad
regressions pass497 CPU history,123 native-pass and536 world checks, validation
errors0 (lighting-native-pass-motion-{cpu-history,resident,world}-regression.log).
This closes the demonstrated native-pass motion omission for
the covered simple-instancing path; indexed GPU auxiliary coverage, ordering,
resource ties, accelerated raster overrides and whole-scope acceptance remain
open. Ordinary native asset remains876D526E; no normal-profile deployment.

The menu-gap evidence above checks render dimensions, finite motion, history
reset and resumed rendering. It does not compare cached cascade contents
across menu-only frames; that ownership/cache behavior remains an explicit
item for the final lifecycle audit.

### Indexed GPU attachment replay

New GpuSceneLiveTests `--auxiliary-only` reproduced an actual missing indexed
attachment path on native5CDE1139: the production scene owned its actor with
zero managed world packets and400 populated depth pixels, but every attachment
pixel was missing. Evidence: lighting-indexed-auxiliary-before.log.

The renderer now initializes five indexed attachment vertex classes (NEW
simple and OLD rigid/generic/cube/deformed). They consume the GPU-generated
stock instance/object/draw records and immutable source stream through a
separate frame-slot storage descriptor set. The pass reuses the exact main-view
GPU indirect argument/count ranges, including draw-base, first-instance and
geometry offsets. There is no selected-actor readback or managed fallback.
Attachment geometry/deformation/DF helpers are shared with object_motion via
object_transforms.hlsli; their math was moved unchanged. Projection and storage
descriptors update once before any attachment binds, including multiple live
targets. Pipelines replace old handles only after successful creation and are
destroyed with their layouts/pools during world cleanup. The additive native
export is sevk_init_indexed_attachment; public existing packet layouts remain
unchanged. Managed production scene setup initializes it when GpuSceneReady.

The first setup used explicit vk::binding attributes, which the existing HLSL
compiler correctly rejects before native pipeline creation. The correction
uses register(t0..t3,space1), mapped to128..131 by the existing compiler policy;
no compiler policy was relaxed. The initial and detailed binding diagnostics
are retained as lighting-indexed-attachment-{init,binding}-diagnostic.log.
Those setup runs are not rendering acceptance.

The focused suite passes262 checks in the default GPU input mode. It uses the
real scene producer and checks actual GpuType for OLD sources, two simultaneous
attachments, both frame slots, every surviving depth pixel, actor translation
and basis rows, NEW plus OLD rigid/generic/cube/deformed streams, near DF and
far scale350/translation-1050 controls. The single-layer MWM is offset from the
object origin, so DF changes its depth as well as footprint. Far threshold1100
retains visible partial coverage below the10000 control; source deformation
uses nonzero bone displacement. Validation errors0. Scratch captures are not a
preserved before/after pair; the negative log is the retained before evidence.
Final focused log: lighting-indexed-attachment-after.log. The alternate
GPU-owned-input mode also passes262 checks (lighting-indexed-attachment-
owned-inputs.log). Broad owned-input scene regression passes180 checks;
freshly rebuilt CPU history and native-pass history suites pass497 and156
checks, respectively. Logs: lighting-indexed-attachment-{scene-owned,
cpu-history,native-history}-regression.log. All report validation errors0;
the existing unused fragment-output warning remains in the broad scene lane.

Native SHA256:
7ADB17BFCD3F3BE8D756DEBE8C82B5F78EB6FA26600DE1AD473119D46FBB5807;
managed net48:
CCE9C582725D85BCA1E5C5EC396673DB5AC7E11E7025B5692845F9D1B319560C.
Shader SHA256 values: indexed_attachment.hlsl
44DF249764B648B102216FE80F13DCD82C42EDB7C8FDCE36D86478D7544EE1CB;
object_motion.hlsl86EAE222CD7F45FC6288CA4334F023ED37E4AE5269CEA10543396A7F814E1081;
object_transforms.hlsli26EA5A914EE3E6444B0E3BB01540A3619EED7E9810FD9C6C3F45612B05C79E43.
Native tests pass15 with the optional GPU-reference skip; both managed targets
build. This evidence covers the tested MESH geometry and DF sources; it does
not certify every read-only decal or mixed-source attachment ordering case.
Indexed GPU motion, material/resource ordering, accelerated raster overrides
and final whole-scope/lifecycle acceptance remain open. Ordinary native asset
remains876D526E; no normal-profile deployment occurred.

### Indexed GPU motion history

The focused `--motion-only` lane reproduced missing motion on a real NEW
GPU-owned actor with zero managed packets: parent movement required
-3.995123354 pixels but returned zero (lighting-indexed-motion-before.log).
Indexed motion now reuses the same GPU indirect argument/count ranges and
five geometry classes as the main pass. OLD draws identify their actor in
the formerly reserved draw-record word; NEW instances use the existing
GPU reference record. Public packet layouts remain unchanged.

Previous actor rows are keyed by owner and generation from committed input
records, independently of GPU visibility selection. Previous immutable
source snapshots preserve generic/cube/deformed source updates. Headers and
source buffers belong to the current fenced slot; source uploads are cached
per slot by immutable snapshot identity. History rejects recreated actors,
changed source counts and nonconsecutive world frames. Explicit scene clear
frees history buffers after its existing GPU wait. No selected-actor readback
or new per-frame wait was added.

The 364-check focused lane passed in both GPU input modes before adding the
FOV roundtrip. It covers NEW plus OLD rigid/generic/cube/deformed actors,
independent actor/source motion, different source handles, selected source
ranges, nonzero bone displacement, active far DF and flag/threshold changes,
source-count resets, menu-frame gaps and camera translation. It observes the
actual motion image against independent projection calculations and reports
zero core/synchronization validation errors. Current renderer regressions
pass262 attachment checks and180 full-scene checks in owned-input mode.
Native CTest passes15 with the optional GPU-reference test skipped.

The additional FOV90/60/90 diagnostic found the fixed .01-pixel test bound
too small for RG16F motion above16 pixels: expected17.203193978,
observed17.1875 (one .015625 step plus .000068978). The projection oracle now
budgets one half-float representable step per component plus .001 pixel of
raster interpolation error. This changes only the test precision budget;
the renderer and projection calculation are unchanged. Diagnostic evidence:
lighting-indexed-motion-projection-diagnostic.log. Both expanded input modes
then passed374 checks. Final asynchronous growth runs pass429 checks in each
input mode (lighting-indexed-motion-{after,owned-inputs}.log), validation
errors0. Real deformed OLD source counts grow16/1024/4096; NEW actor counts
grow64/1024/4096. Three consecutive submissions per size omit per-frame
readbacks, retain GPU ownership with zero managed packets and finish with
positive geometry. This exercises history allocation growth, both slots,
source replacement and OLD-to-NEW ownership recreation.
The freshly rebuilt CPU-history and native-pass-history regressions pass497
and156 checks with zero validation errors (lighting-indexed-motion-
{cpu-history,native-history}-regression.log).

Candidate native SHA256:
EC650F2B6654641BD7AC416DDD64C0C80CDACD3503AE3C19B67D42B0D9BADFD8;
paired compute SPV:
9C6BD14C30502E61BD2C44185ACFD1E6E959BD7CD7E9BFA620D0F72538E48134;
managed net48:
67DBF5ACA1CC0D97B8BBF64949301FF3ABB50F5F04CF7A3C60CCD0A12C26A4B9.
Final whole-scene lifecycle acceptance, read-only decal/mixed auxiliary
ordering, resource-key ties, accelerated raster overrides and the
final whole-scope live comparison remain open. Ordinary native assets were
not deployed.

### Mixed-source ordering audit: source trace

Stock MyRenderScheduler.Done consumes GeometryRendererOld.DoneFrame before
GeometryRenderer.DoneFrame. OLD MyRenderingWork.ConsumeWork consumes its
decals job after its other jobs. Its ordering key in
MyRenderableComponent packs decal bit50, technique
bits45..49, vertex shader flags20..44, layout14..19, mesh7..13 and material0..6.
Thus a backend-wide "all solids, then all decals" rule is not sufficient
evidence of stock OLD/NEW ordering. NEW work still follows OLD decal work.
Equal-technique resource ordering needs those lower key fields considered.

Current Vulkan render_world records managed solids, record_pass_view
(GPU-indexed work followed by native-pass groups), then managed decals.
record_opaque_stock_view also has its own solid/decal phases. This trace
identifies cross-owner ordering risk; it does not yet establish the visible
failure or the complete replacement schedule. Next acceptance must use
overlapping OLD/NEW geometry with actual ownership witnesses and compare
all GBuffer/depth channels. No production ordering change was made from
this source trace alone.

### OLD/NEW main-pass boundary across ownership paths

New GpuSceneLiveTests `--mixed-order-only` reproduces a visible ordering error
on nativeEC650. An OLD base, an OLD read-only red cutout, and a NEW blue solid
overlap. The NEW solid is between the base and cutout depths. Stock submits
the OLD cutout first, then the NEW solid passes against the OLD base depth
and overwrites its material. Vulkan instead produced red[255,0,0] with exactly
the expected depth. Negative log: lighting-mixed-order-before.log. Negative
GBuffer/depth captures are preserved in TestResults/MixedOrdering/Before-EC650.

Main submission now shares OLD solid, OLD DECAL/NOPREMULT/CUTOUT, NEW solid
and NEW decal phases across managed, indexed and native-pass ownership.
The existing0x04000000 packet provenance bit now marks every OLD geometry
packet (OldGeometryOrder); old decal aliases remain. Indirect compatibility
includes that bit so a run cannot cross the OLD/NEW boundary. Shadow/probe
recording keeps its separate view path. This changes submission order, not
material shaders, image layouts or the public packet layout.

The initial focused correction passes90 checks across GPU, native and managed
ownership, direct/indirect submission and reversed creation order. Every
GBuffer/depth channel exactly matches the independently blue baseline.
An expanded141-check lane also covers managed OLD blended decals against
NEW cutouts; it passes in both GPU input modes with validation errors0.
The managed mesh-decal156, raster189, full-world536 and native-pass-history156
regressions also pass with zero validation errors. Final owned-input GPU
scene180, motion429 and attachment262 regressions pass with validation
errors0. The broad scene retains the existing unused fragment-output warning.
Logs: lighting-mixed-order-{after,owned-inputs}.log and
lighting-mixed-order-{mesh,raster,world,native-history,scene,motion,attachment}
-regression.log. Native CTest passes15 with the optional
GPU-reference skip; both managed targets build. Candidate native SHA256:
F939E8DD597236D9D2CE8D893296E4C2995198A44977A6ADB94720EB13FE3F65;
managed net48:
09F7B07F23BBC397783B8DBF8D1CB288DAD9167C88B4331D9EFDA8ADEE1D9094.

This is evidence for the OLD/NEW boundary, not for all lower resource-key
ties or NEW model-group interleaving. Stock NEW DrawInstanceLodGroup emits
each group's preprocessed parts together; global per-technique phases do not
alone establish equality for arbitrary overlapping NEW groups. Read-only
decal auxiliary outputs, accelerated raster overrides, menu cached-cascade
contents and final matched live acceptance also remain open.

The next NEW ordering oracle is more specific: MyPreparePass.
PrepareInstanceableGroups appends a group on first encounter of each
LodInstance.UniqueId/state in prepared LOD data. MyRenderPass iterates that
list, and MyGBufferRenderPass.DrawInstanceLodGroup emits all preprocessed
parts of the group. Thus NEW ordering is group-contiguous and first-seen,
not a backend-wide technique sort. This remains a separate acceptance item
from the OLD/NEW boundary correction above.

### NEW group contiguity and shared-page GPU batches (active)

`GpuSceneLiveTests --new-group-only` reproduces red instead of blue in the
managed path on nativeF939 (lighting-new-group-before.log). Group A has an
opaque base and read-only red cutout; group B's blue solid lies between their
depths. Stock emits A's parts together before B. Negative captures are in
TestResults/NewGroupOrdering/Before-F939. Removing the global NEW decal
phase and preserving emitted NEW packet order fixes the initial case.
Native-pass grouping now emits batches in first-seen selected order, rather
than model-handle map order. Its audit records owners300,300,301 after B's
resource was resolved while A was hidden.

An initial51-check pass did not establish the shared-page GPU case. The
stronger fixture enables the real geometry arena and asserts one vertex/index
page. It passes managed/native draws but reproduces red in GPU ownership on
native6E250 (lighting-new-group-shared-page.log). Those before captures and
the6E250 native DLL are retained in NewGroupOrdering/Before-Gpu-6E250.

GPU immutable NEW batch keys now retain committed first-seen family and
preprocessed-part ordinal before page/shader fields. OLD keys retain their
existing page/shader grouping. Dedicated depth segments map back to the source
part's batch identity, including split material coverage. Both GPU and native
NEW adapters consume NewParts ordering. No selected GPU geometry is read back
to choose draw order; visibility/LOD selection, source geometry, indirect ranges
and shader ABI remain on the existing paths. The software batch ceiling now
equals the existing65535 part ceiling, avoiding a new admission restriction:
its64-view count table is below17 MiB within the existing128 MiB storage-range
requirement. No hardware feature requirement was relaxed.

The strengthened two-actor case passes58 checks in default GPU input mode,
including actual owner witnesses, shared pages, direct/indirect submission,
independent blue reference pixels and all four GBuffer/depth channels. Final
validation errors0. Native CTest passes15 with the optional GPU-reference skip;
the registry lane passes40 checks including NEW group/part ordering and OLD
batch identity. Its first added OLD control omitted a required source stream;
that test setup was corrected before accepting CTest. Exception diagnostics
now print the actual failure instead of an uninformative Windows fast-fail.
Both managed targets build. Candidate native SHA256:
2CC155A10188DBE7D16A76FB13876F422BF385927D51957B4BBCEBC439AD8A3D;
managed net48:
B8B68EFCA22E112C9460424523C79D1C26AC5E1D9DFCD7E73F7DD7D801F2D87B.
Compute SPV remains9C6BD14C. Ordinary native asset remains876D526E.

The alternate input mode also passes58 checks. Broad owned-input GPU-scene180
and OLD/NEW boundary141 regressions pass with validation errors0; the broad
scene retains the existing unused fragment-output warning. An A/B/A repeated-
group diagnostic then fails in the managed path on candidate2CC155: the
second A actor's cutout follows B and returns red[255,0,0] instead of blue.
Session62036 is terminal; no test process remains. The source test includes
the repeated-group case, but the initial queued runs deliberately used the
previously built58-check binary; the queue then built the expanded source
before running that diagnostic. Native/GPU repeated-group cases have not yet
been reached. Logs are
lighting-new-group-{owned-inputs,scene-regression,boundary-regression,
repeat-diagnostic}.log.

Do not call this complete NEW ordering. Repeated managed groups, mixed NEW
ownership, per-view first-seen family order and transition-state ordering need
their own witnesses. A shared main-group ordering contract may be needed;
preserving packet emission alone does not prove A/B/A coalescing. Lower OLD
resource-key ties, read-only decal auxiliary outputs, accelerated raster,
cached cascade contents and final matched live acceptance also remain open.

Next implementation must preserve managed group identity through submission
and sorting, rather than merely preserve packet insertion order. Existing
WorldPacket.Order/modelSubmissionOrder and bulk model first-seen merge are
relevant owners, but raw native draw packets do not currently carry a NEW
group tag. Do not reuse cascade/guest-view mask bits for it. Stock
MyModel.GetInstance(changes) caches model instances by texture-change dictionary
reference/content hash; MyLodInstance.UniqueId is allocated per cached model
instance/LOD. Per-instance appearance values alone must not split groups.
Repeated negative captures and candidate DLL are retained under
NewGroupOrdering/Before-Repeated-2CC155.

### Managed NEW group metadata through every queue route

The A/B/A failure is corrected by explicit first-seen group and part metadata.
VulkanScene caches a model/texture-binding/render-state key, excluding
per-instance color, HSV and emissivity values. NativeBackend scopes that key
while emitting an actor, remembers its first actual main submission, and tags
each emitted part. Rigid/model batch keys include the frame's group identity,
so later binding variants cannot be silently merged into an earlier group.
The native main sorter uses group order, then preprocessed part order, then
original draw index. Physical draw IDs, object offsets and cascade masks stay
unchanged; highlight/exclusion consumers retain their existing indices.

`sevk_world_main_order` is an additive8-byte-per-draw CPU ordering submission
after queueing and before native model merging. It validates the full count
and all nonzero order values before updating the queued immutable submission.
Raw public draw layouts and shader ABI remain unchanged. Bulk model instances
use their previously reserved word for optional first-seen group order
(zero retains the existing order; future group order is rejected), and bulk
parts retain their preprocessed ordinal through the existing legacy merge.
This adds no GPU wait/readback and changes no descriptor or image lifetime.

The first scoped correction passed97 checks. The expanded matrix found an
existing native bulk-admission omission: model-part validation rejected the
stock raster/provenance flags produced by real actors. It now accepts those
known flags and rejects conflicting back-cull/no-cull combinations. The bulk
route's positive pixel test witnesses actual native model instances rather
than merely enabling a flag. Diagnostic:
lighting-main-groups-bulk-admission-diagnostic.log. This is not a claim of
complete accelerated raster coverage.

Final147 checks pass in both GPU input modes, with validation errors0. They
cover A/B/A actors, both indirect settings, blob/pointer/retained-object/bulk
managed queues, actual managed/native/GPU ownership, shared geometry pages,
per-instance appearance staying grouped, a later independent green texture
variant, and binding reset restoring shared instancing. All four GBuffer/depth
channels match the reference over covered pixels. Logs:
lighting-main-groups-{after,owned-inputs}.log.

Regressions pass156 mesh-decal scene,189 raster scene,538 full world,497 CPU
history,156 native-pass history,141 OLD/NEW boundary,180 broad GPU-scene,
429 indexed motion and262 indexed attachment checks. Validation errors0;
the existing unused fragment-output warning remains in the broad scene lane.
Native CTest passes15 with the optional GPU-reference skip; both managed
targets build. The full world lane also checks explicit group-order acceptance
and rejection and invalid model raster combinations. Logs are
lighting-main-groups-{mesh,raster,world,cpu-history,native-history,boundary,
scene,motion,attachment}-regression.log. Native SHA256:
A984CBF91BA0DE939E2F45263BEF6FD23E7BC3D50F07026E8C1B9F9E3F39E400;
managed net48:
AF6248191C89D4FA3BFF36CB646A98BE24605AF476354142E351D7A659D7F6C4.
Ordinary native remains876D526E; no normal-profile deployment occurred.

Mixed NEW ownership is still open: render_world currently records all managed
NEW work, then indexed/native-pass NEW work. `--new-mixed-only` now authors an
eligible accelerated A group and an unsupported managed B blend group whose
reference requires A before B. The source has owner witnesses and independent
blue-blend baseline captures. The confirmed diagnostic passes both ownership
and actual NEW-provenance assertions, then fails: GPU A overwrites managed B,
returning red[255,0,0] instead of reference[162,162,199]. Log:
lighting-new-mixed-before.log. Initial unguarded diagnostic is retained as
lighting-new-mixed-initial-diagnostic.log. Captures and candidate DLL are saved
under TestResults/MixedNewOwners/Before-A984. Session27314 is terminal; no GPU
test/game process remains. The native-pass/managed and indirect variants in
this new lane have not yet been reached. Next work must share NEW group order
across ownership producers, rather than only sorting within each producer.
Lower OLD resource-key ties, per-view/state order, read-only decal auxiliary
outputs, broader accelerated raster, cached cascade contents and final live
acceptance also remain open.

### Mixed NEW ownership: shared main sequence

The preceding red-versus-blue-gray negative is corrected. Managed scene
submission declares first-seen main groups before accelerated-owner early
exits. The additive sevk_world_main_owners submission carries owner/group
labels into the immutable native frame. NEW managed draw ranges, indexed GPU
batches and native-pass groups now merge by group and preprocessed-part order.
The recorder reuses real indirect commands and one shared GPU descriptor set;
it does not select GPU geometry on the CPU or introduce a GPU readback.
Unmapped records retain geometry and their producer sequence. Raw submissions
without owner metadata retain their previous path. Owner zero is valid.

Final lighting-new-mixed-{after,owned-inputs}.log each pass64 checks, including
GPU/managed, native/managed and all-three-owner cases, direct/indirect draws,
actual ownership/provenance witnesses, owner zero, and exact covered-pixel
GBuffer/depth comparisons. Final core/synchronization validation errors0.
Regressions pass147 NEW-group,141 OLD/NEW-boundary,180 broad GPU-scene,429
indexed-motion,262 indexed-attachment,156 mesh,189 raster,538 world,497 CPU
history and156 native-history checks. Logs use lighting-new-mixed- prefixes.
The broad scene retains the existing unused fragment-output warning. Native
CTest passes15 runnable tests with the optional GPU-reference skip; both
managed targets build. No game or GPU test process remains after the runs.

Candidate native SHA256:
8C74C9DA4DF4D026A3176E52B3A44B47E96A6DFA514D92FAC1F178FB29DFB823.
Managed net48:
33F944E43D4955E1102141502E250ADAA5D6E42C85149CBCDF90814D31F2820E.
Ordinary native remains876D526E; no normal-profile deployment occurred.

This proves the authored mixed-owner scenes, not all NEW ordering. Root-model
candidate labels versus selected LOD/state groups, per-view family order and
same-group instance ties remain unverified. Lower OLD resource-key ties,
read-only decal auxiliary outputs, broader accelerated raster, cached cascade
contents and final matched live acceptance also remain open. Stock OLD
material sort uses an allocated MyMeshMaterialId index; GetMaterialId(ref
descriptor) allocates proxies and queues resources, so it must not be invoked
as a side-effect-free ordering query.

### Cached cascade ownership across no-world/menu frames

The open menu/cache audit has a confirmed negative. A real raw world caster
fills the cascade, an unchanged scheduled frame reuses it without draws, and
a no-world frame clears the shared image. Returning with update mask zero
then leaves the cascade empty. See lighting-shadow-cache-before.log and
ShadowCacheMenu/Before-8C74 for the original DLL and depth captures.

The native menu recorder now invalidates automatic world-cache reuse and only
the ownership bits for the first four images it actually overwrites. The
initial correction passed13 focused checks and an expanded338 checks covering
scheduled/automatic caching,1/4/8 cascades,serial/parallel recording and1/2/3
menu frames. Candidate16890B078390CFB55D61D5AB1494878F8CADBFBCB7DE1735E3C8815C432D6D93.

That native correction alone is insufficient for accelerated view selection.
The stronger scene test freezes an initialized cascade, witnesses genuine GPU
ownership and positive depth, crosses a menu frame, then resumes. The managed
scheduler still selects update mask0; coverage becomes0. See
lighting-shadow-cache-owner-selection-before.log. The first owner diagnostic
used256 instead of stock LOW cascade resolution512 and failed the explicit
readback contract; it is retained as a setup diagnostic only. The corrected
negative uses512. The prior DLL and managed net10 binary are preserved under
ShadowCacheMenu/Before-ManagedSelection-16890.

An additive sevk_get_shadow_cache_mask reports CPU submission ownership without
a GPU wait/readback. VulkanShadows queries it before cascade scheduling/view
selection and invalidates fitted state for overwritten images, including frozen
cascades. This allows managed, native-pass and indexed owners to prepare the
required depth work. The scheduler's existing valid/usage feedback semantics
remain unchanged. Final native invalidation preserves ownership of cascades
above the first four; the managed query therefore reinitializes exactly the
overwritten views rather than thawing untouched far cascades.

Final raw-cache458 checks pass in lighting-shadow-cache-final.log, including
explicit ownership-mask assertions and unchanged far-image contents. Expanded
scene204 checks pass in each GPU input mode:
lighting-shadow-cache-owner-expanded-{0,1}.log. They cover actual GPU,
native-pass and managed owners,1/6 cascades, frozen scheduling,1/2/3 menu
frames, exact resumed depth contents and return to zero-draw cache reuse.
Six-cascade resumes select mask15, preserving the two frozen far cascades.
The initial one-cascade scene93 checks also pass in
lighting-shadow-cache-owner-after.log. All final lanes assert validation
enabled and zero core/synchronization errors.

Regressions pass206 stock scheduling,36 shadow-occlusion,538 full-world,123
full native-pass and156 native auxiliary-history checks. Logs are
lighting-shadow-cache-{schedule,occlusion,world,native-pass,native-history}-regression.log.
The first native-pass dispatch used an unrecognized auxiliary flag and ran
the full123-check lane; its log was renamed to native-pass-regression. The
156-check history lane was then explicitly run with --auxiliary-history-only.
Native CTest passes15 runnable tests with the optional GPU-reference skip;
both managed targets build with zero warnings/errors.

Final native SHA256:
98AB2D575C56A600E17E7D82FAD4A68A4B77B4099A7526E744787CE4682ABA91.
Managed net48:
949EC7CB931FE271D73598064494F3EE0A137EB454E91E249D70B568D52D07EC.
Ordinary native remains876D526E. This closes the reproduced cached-cascade
menu ownership issue in these paths; final matched live/lifecycle acceptance
is still required for the full lighting/material goal.

The OLD ordering trace additionally confirms that proxy.Mesh is LodMeshId,
shared across parts of a LOD, rather than Vulkan's per-part mesh allocation.
Stock packs vertex flags at20..44, layout index14..19, LOD-mesh index7..13 and
allocated material index0..6 below technique/decal bits. CreatePartInfos groups
non-array material parts by material identity before AddNonMergableGroup emits
their original relative part order. Any compatibility metadata must account
for both material reuse and truncated allocation indices; alphabetical sorting
or per-part Vulkan handles are not equivalent. No OLD sort implementation was
changed in this slice. OLD resource ordering, NEW selected-LOD/state/per-view
and instance ties, read-only decal auxiliary semantics, broader accelerated
raster and final matched live acceptance keep the full goal open.

### OLD resource-key ordering: matched live negative

The isolated harness now supports -MeshDecals -MeshOldOrder. It preloads the
base, red cutout A and blue cutout B through stock ForceOldPipeline preload
messages, then creates four panels: A/B, B/A, A/B with A recreation, and A/B.
All12 surviving actors are genuinely OLD; the Vulkan capture witnesses eight
cutout packets and no blended packets. Capture and render-object cleanup both
complete with validation errors0. This is an ordering oracle, not a parity pass.

Stock DX11 run64f1d73eac1b4fddadf2921115d22042 renders all four panels blue.
Its actual CullProxy keys are1653665488374690 for A (LOD mesh23/material34)
and1653665488374819 for B (mesh24/material35). Actor reversal and removal/
recreation preserve these keys. Vulkan run5622022b90864e2a970f4dcd95bc3799 on
candidate98AB2D57 renders columns0,2,3 red and column1 blue. Both PNGs were
visually inspected. In each failing panel, decoded color-lod mean absolute
delta is[1,0,1,0], while depth and metal/gloss/emissive match exactly. See
TestResults/lighting-old-resource-live-before.json for the full stage report.
The blue control also has an HDR blue-channel mean delta about0.1473; this pair
does not establish final matched-input HDR acceptance independently of ordering.
Both runs completed render-object cleanup, world unload and controlled exit;
both actual Legacy.exe processes are terminal. Vulkan also recorded harness
disposal. DX11's harness-disposed marker is absent, so this pair is not proof
of final DX11 plugin disposal despite the completed unload/exit evidence.

The focused GpuSceneLiveTests --old-resource-order-only lane authors the same
resource preload/actor-order distinction with independent blue reference
buffers, actual ownership and OLD provenance guards, and direct/indirect
variants. It passes64 checks in lighting-old-resource-single-panel-control.log
on the same candidate that fails the live fixture. This is a weaker single-
panel control, not proof of resource-order parity. The spatial live scene is
the canonical acceptance path for this gap. No product sorting implementation
is changed yet.
Sorting by Vulkan per-part mesh handles would happen to improve this fixture,
but would not reproduce shared-LOD IDs, material reuse or truncated stock keys.

The owning CPU identity producer must account for eager OLD LOD loading,
ForceOld versus NEW-only preloads, shared file-model lifetime across session
end, and runtime/voxel allocation in the LOD-mesh pool. Stock's initialization
also loads Cone/Sphere mesh assets before creating common material identities.
The stock geometry-array path substitutes deterministic array texture names
before material RK allocation. Calling the stock material allocator directly
is not a metadata query: it allocates proxies and queues resource work. Those
facts rule out both a per-actor key and an arbitrary descriptor/alphabetic sort.

Verify-OldResourceOrder.py now regenerates the matched buffer comparison,
verifies actual stock key persistence and blue reference ROIs, checks real
ownership/render-object cleanup and rejects exact color/LOD mismatches at
identical depth. The confirmed negative produces Status=FAIL with columns
0,2,3 in lighting-old-resource-live-gate-before.json. Its first invocation
stopped on the absent DX11 disposal marker; that was a prerequisite failure,
not an ordering-gate result. The gate now reports disposal evidence separately
from its ordering/capture scope. The subsequent invocation was checked for the
specific output status and failed-column list, rather than only exit code1.
HDR/light and final plugin-lifecycle acceptance remain explicit separate work.
Both prepared runs use native98AB2D57 and managed949EC7CB; ordinary native asset
remains876D526E. No game or GPU test process remains after the proof runs.

### CPU OLD identity building blocks against the installed stock oracle

StockOldMaterialIdentities now owns a CPU-only RK/material-index/name table.
It preserves stock hash-only collisions, original identity reuse, latest newly
allocated name aliases, the default-zero unknown-name result, explicit common
material bootstrap order and material reset semantics. It never invokes stock
registration, proxy allocation or resource gathering. StockOldSortKey packs
the exact stock fields/masks and reproduces both captured live A/B keys.

StockOldMaterialDescriptions resolves original import paths and geometry-array
representation names before identity allocation. It uses the installed stock
CPU image-description reader and pure format/mip helpers, without constructing
textures/arrays or accessing FileTextures/geometry-array managers. Its default
header cache persists across devices/sessions like the stock params cache.
The source cached-default/missing-header success behavior is preserved, including
zero-size Unknown-format names on subsequent cache hits. Source impostor normal
inference, authored array exclusion and empty-alpha behavior are covered.

The first path-helper experiment called MyResourceUtils.GetTextureFullPath;
its generated-name check initialized MyRenderProxy/MyRenderSettings and failed
in the headless test on the absent platform renderer. Production now implements
that inspected path/name logic directly. The complete actual-DDS header/path
producer runs with MyVRage.Platform still null and leaves stock CPU resource
queues/pools untouched. A test-only platform stub permits only the budget
constants needed by stock reference import conversion; graphics operations
throw. No production dependency on that stub exists.

StockOldResourceIndices provides source-equivalent shared LOD-index allocation
and LIFO retirement/reuse. File LODs, runtime meshes and voxel cells must share
one retained owner; Vulkan upload IDs and per-actor clocks are not equivalent.
The primitive exposes an explicit clear rather than assuming that material,
LOD-pool, scene and device lifetimes are interchangeable. Those lifetimes still
need to be connected to their actual message/import owners.

Tests/OldResourceIdentityTests links these production sources and invokes the
installed stock CPU RK/allocator, bit helper, import conversion, array-key
formatter and MyFreelist as independent references. Final7959 checks pass on
both net48 and net10.0, including growth beyond1024, interleaved source releases,
duplicate retirement rejection, index wrapping, all stock techniques, actual
RK collisions and device/material resets. Logs:
lighting-old-identities-{build,managed-build,net48,net10}.log. Both managed
plugin targets build with zero warnings/errors. An initial structured decimal
name search found no Framework collision; an attempted numeric-enum collision
was invalid because the stock technique is byte-backed. Both test diagnostics
were retained, and the final collision case uses a fixed dispersed valid-name
sequence and verifies the actual stock RK/allocator alias. No weaker enum
contract was introduced.

These building blocks are not yet connected to retained file/LOD metadata,
vertex-layout identity registration, runtime/voxel ownership or managed/native/
GPU submission keys. They do not fix or qualify the failing spatial live
fixture. Next work must integrate those owners, retain eager import/material
and merged-part allocation order, then repeat the canonical live gate. No
sorting implementation or native/shader ABI changed in this slice. Managed
net48 SHA256:
7030CD1D6C6A134845746B7E3A3CEA20E32925B38483E5A3C4592515E220E970.
Private native remains98AB2D57, ordinary native876D526E. No deployment occurred.

### Retained OLD part and file identity metadata (integration still open)

StockOldPartIdentities now retains source-material to emitted-proxy mapping,
including original allocation order, array singleton migration, synthetic
Merged:: counters and rejected-merge fallback ordering. A headless attempt to
invoke the actual MyMeshes part builder initialized D3D managers and was
rejected as an oracle path. The reference comparison instead runs after held
capture on an already initialized isolated DX11 render thread, with no live
table resets. Run ed09b00f4e524bba8d1bbfbf3cf0d107 reports253 checks, Passed=true
in Reports/old-part-identities.xml. It compares actual stock material IDs,
proxy order, synthetic counters and allocation counts. Rejected-merge final
partitioning is covered there; the actual bone eligibility calculation is
currently covered separately by CPU specifications, not that live oracle.
Cleanup, fixture-unloaded and exit-requested evidence exists; the DX11 harness
disposal marker is absent and final plugin disposal is not qualified.

StockOldFileIdentityReader imports metadata without geometry upload or stock
resource-manager registration. StockOldFileIdentities retains file-name
ownership, eagerly allocates successful authored LODs, follows geometry-linked
LOD metadata, preserves cached first-load representation and frees explicit
removals in source order through the shared resource-index pool. Animation
preprocessing retains shared bone-remap effects and can disable final skin
layout after an unsupported separate part. Layout identity remains supplied
by an external resolver: the stock global layout allocator/bootstrap is not
yet reproduced or qualified. Runtime/voxel ownership and renderer submission
integration remain open.

The focused CPU suite passes7993 checks on both net48 and net10.0. It covers
missing-LOD compaction, eager import order, cache reuse, child direct loads,
retirement/reuse, geometry indirection, authored skin streams and bone-remap
specifications. Installed Cone/Sphere metadata each yields one LOD and zero
new material identities before common registration in this installation.
Logs: lighting-old-file-identities-{build,net48,net10,managed-build}.log.
Both managed targets build with zero warnings/errors. Ordinary native remains
876D526E, unchanged. No deployment or product draw-order change occurred.
The failing four-panel spatial ordering gate remains the acceptance blocker;
these metadata tests do not qualify visible OLD ordering or final HDR parity.

### OLD vertex-layout identities and transient allocation lifetimes

StockOldVertexLayouts owns the stock hash-only identity table, including the
empty layout, ordered file/skin/array streams and combined instance layouts.
The component struct uses runtime-specific ValueType hashing; production boxes
only that installed metadata type and never invokes the stock layout allocator
or resource managers. CPU tests independently call installed MyVertexLayouts
GetLayout and compare allocation/reuse for all component types, slots0..2,
both frequencies, file signatures and combined layouts. Both Framework and
net10 pass. Framework initially exposed a test LoadFrom-context error: a second
identical assembly image has a different runtime type hash. The oracle now
verifies identical installed assembly bytes and uses the production type's
load context. Diagnostic: lighting-old-layout-identities-net48-diagnostic.log.

The first initialized-DX11 bootstrap comparison failed at index2 in run
3d8b35a372e341f0a74226399f6caeb9. Its read-only snapshot exposed three NEW shader
bundle layouts allocated by ModelFactory.OnDeviceInit's LoadingQuad load,
before the occlusion managers. Source inspection confirmed the caller and
GBuffer/depth/highlight component sequences; these now participate in the
bootstrap. The default installed setting disables geometry arrays, and the
captured rigid layout12 is the standard MeshTable layout, not an array layout.
The first failed diagnostic run requested exit without qualifying cleanup.

Canonical repeat491043d5ec3f426b80ba53a7f053637f reports22 layout checks,
Passed=true: all15 bootstrap entries match component order/slot/frequency,
and all19 allocated layouts replay to their actual retained stock indices.
The oracle only reads the real layout table; no stock allocation or reset is
performed. The post-capture stock part/material oracle again passes253 checks.
Reports/old-layout-identities.xml and old-part-identities.xml retain evidence.
Fixture cleanup, fixture-unloaded and controlled exit exist; Legacy is terminal.
The DX11 harness-disposed marker is still absent, so final plugin lifecycle
proof remains open. Snapshot managed755F0064, private native98AB2D57.

StockOldTransientIdentities now retains runtime and voxel LOD allocations
through the same externally supplied pool used for files. Runtime creation
always allocates, duplicate names replace aliases without freeing old resources,
updates reuse the LOD and resolve existing material names, and explicit removal
returns the source index. Session end frees currently named dynamic runtimes
and voxel cells, clears aliases, and retains static/overwritten allocations,
matching the inspected MyMeshes lifetime. This component is not yet connected
to renderer messages or voxel callbacks. Its lifetime checks are source-derived
CPU specifications; an initialized stock transient-owner comparison remains
separate work. Cross-file/runtime name-removal coordination belongs to the
upcoming shared owner integration, not to this transient table alone.

Final focused CPU suite:8547 checks pass on both net48 and net10.0, including
layout allocation oracles and transient lifetime specifications. Logs:
lighting-old-transient-identities-{build,net48,net10}.log. Canonical managed
build: lighting-old-layout-transient-managed-build.log, both targets zero
warnings/errors. Ordinary native remains876D526E, unchanged. No deployment,
draw-order change or native ABI change occurred. The four-panel visible OLD
ordering gate remains open. Next work connects the shared identity producer
to source message/import lifetimes and propagates the resulting keys through
managed, cached, retained, bulk, native and GPU submission paths.

### Shared OLD owner connected to live file/material allocation events

StockOldResourceIdentities now bootstraps and retains the shared file, material,
layout and transient tables. The real Vulkan host initializes it from the
geometry-array setting. OLD preloads, ordinary OLD admission/conversion and
AddRuntimeModel feed this owner; scene clear preserves files/material/layouts
while applying source runtime-session retirement. Runtime dispatch respects
stock's existing file/runtime-name guard. Cross-name runtime removal forgets
the file alias without freeing the separately retained file allocation.
Static NEW groups and the deprecated synthetic SetRenderEntityData path are
excluded from OLD admission, including later actor-updater callbacks.

The first attempted Vulkan launch8faef21620384754ab75d143bd232b99 stopped on
the existing compatibility guard: the shared fixture harness had acquired a
Render11 reference from the newly linked stock oracles. No guard was bypassed.
Both stock oracle entry points and linked metadata sources now live in the
existing CreativeFixtureHarness.Dx11 companion, loaded only for DX11 captures.
The shared harness emits no Render11/SharpDX assembly references. Source runs
b2dde0193b2f4960a770eca1985545c6 and8d4890912f4e4ed381e26ccb893461e3 retain
the22-check layout/253-check part proofs and read-only file/material snapshots.

Live Vulkan5c3ac7c4c3da4c7cb4ecaf638c0814b3 initially placed the fixture's
LOD IDs five slots early and its materials three slots early. The fresh stock
file table identified all five missing LOD allocations as RockDebris02.mwm.
CreateRenderVoxelDebris now records its OLD model import while the outer
dispatcher still reports its existing pending visual path. This does not
implement debris rendering. Repeatab0c24a9f734432e8d995cc77ea856a4 matches
LOD22/23/24 but remains two material slots early. Negative controls are
lighting-old-resource-identities-before.json and
lighting-old-resource-identities-lod-clock-only.json; both explicitly FAIL.

The two remaining material allocations came from OLD actor texture changes.
The source rebuild allocates a changed global material/proxy representation
while preserving the original proxy's sort-material identity. The owner now
copies retained RK fields, resolves only explicitly replaced textures without
the original mod context, registers variants in material/LOD/proxy order at
actor update, and schedules that update for color-only changes too. Base
sort IDs stay immutable; subsequent runtime name lookups observe the latest
new alias. Repeated/unused overrides do not allocate new identities.

Vulkan repeat e4c5c5f7b6b34fb390b907d781a8e7d9 (managedE5B16BC1,
private native98AB2D57) now produces the exact stock fixture fields:
base LOD22/material33/key199457, A LOD23/material34/key1653665488374690,
B LOD24/material35/key1653665488374819. All four actor-order/recreation
panels match. Verify-OldResourceIdentities.py reports PASS in
lighting-old-resource-identities-after.json. It qualifies CPU identity
production, not draw submission. A separate full material-table comparison
against source8d4890912f4e4ed381e26ccb893461e3 finds36 entries on each side
with zero differing fields after normalizing only isolated fixture-directory
GUIDs (lighting-old-material-table-comparison.json). Generated LCD texture
names and all other material fields compare unchanged.

The visual gate still explicitly FAILS columns0,2,3 at identical depth in
lighting-old-resource-producer-only-visual-gate.json. No sorting/native ABI
change has occurred. Vulkan cleanup/unload/disposal is present; source cleanup
and exit are present but its DX11 harness-disposed marker remains absent.

Combined instance layouts now register at the actor updater before texture
variants, following source proxy rebuild order. A final isolated Vulkan run
ac9c876787694950ae7a6372876b5395 (managed42577788) is checking the full retained
file/layout/material tables against the source snapshot. Its result must be
inspected before claiming that additional layout integration is qualified.
CPU suite8647 checks pass on both net48 and net10; both managed targets build
with zero warnings/errors. Logs: lighting-old-shared-owner-{build,net48,net10}
and lighting-old-owner-managed-build.log. Ordinary native remains876D526E.

Open integration boundaries remain explicit: voxel callback/transition clocks,
dynamic line meshes, runtime replacement-model remapping, broader reload/device
lifetimes, selected OLD LOD/part mapping, source vertex flags/ties, and key/batch
propagation through every managed/native/GPU submission path. The rigid held
fixture does not qualify these other resource families or final lighting/HDR.

The final ac9c876787694950ae7a6372876b5395 run is now terminal, with clean
render-object cleanup, world unload, controlled exit and harness disposal.
Verify-OldResourceIdentities.py --full-tables against source8d4890912f4e4ed381e26ccb893461e3
reports PASS in lighting-old-full-resource-identities-after.json: all12 retained
file names/LOD-index sequences, all19 layout component sequences and all36 full
material representations match in allocation order. Only private fixture
directory GUIDs are normalized. The fixture base/A/B complete rigid keys also
match across all12 actors and the reversal/recreation cases. This is a scoped
live producer proof; source vertex flags for instanced/skinned draws and actual
batch submission remain separate. The subsequent exact visual gate still
reports FAIL columns0,2,3 in lighting-old-full-owner-visual-gate.json, as expected
with unchanged sorting. Source DX11 final harness disposal remains unqualified.

### Stock OLD identities now reach managed/native/GPU draw ordering

An additive32-byte SevkOldPartOrder carries the complete packed stock key plus
full LOD resource, material, layout and emitted-proxy identities. The full IDs
prevent unrelated draws with equal truncated keys from merging. Public draw
structs and their ABI version are unchanged. Optional exports cover queued
world rows, immutable bulk model registration and immutable resident GPU model
registration. Metadata is validated before committed state changes, copied on
the render owner and retained with the actual model/draw. No new per-frame GPU
readback, selector synchronization or shader-source modification is introduced.
The original managed PreparedPart constructor and two-argument RegisterGpuModel
remain available as binary-compatible overloads.

The scene retains authored source-part indices, maps selected physical LODs to
their retained OLD identities, and derives original skin/instance flags from
actor ownership rather than the Vulkan batching shader. Legacy emission,
prepared actors, rigid cache keys, cached model-template equality, blob/retained
queues and native bulk models preserve the row. GPU model families retain it
immutably; their batch keys and binding-only compatibility checks include it.
The native main recorder merges managed/indirect and resident OLD entries by
the same stock key within the existing OLD/NEW phases. Source ties retain the
stable existing order and remain a separate stock-tie audit, not a new invented
full-ID ordering. Unannotated/raw callers retain their prior rendering path.

Canonical private live Vulkan2f56ff9df560459089efa84570e6ec6c, managed50FE967B
and native8167C0E9, now PASSES Verify-OldResourceOrder.py against initialized
DX11 reference8d4890912f4e4ed381e26ccb893461e3. FailedColumns is empty in
lighting-old-order-live-visual-gate.json. All four held ROIs match decoded
color/LOD and depth exactly, including reversed creation and A recreation.
Both actual PNGs were reviewed: all four panels are blue in each renderer.
The full producer gate also remains PASS for12 files,19 layouts,36 materials
and complete fixture keys (lighting-old-order-live-identities.json).
Vulkan render-object cleanup, world unload, controlled exit and final harness
disposal all exist; the stock DX11 final disposal marker is still absent.

Focused hardware controls now require real CPU metadata and ordered GPU/bulk
registration, and include a GPU red owner with a managed blue owner. Unused
appearance entries force that fallback without changing the tested material.
Legacy, prepared/blob, retained-object and bulk paths, direct/indirect commands,
both fenced slots and reversal/recreation compare all four GBuffer/depth
buffers to the independently blue reference. Final320 checks pass in each
GPU input mode, with zero core/synchronization errors. Earlier112-check logs
retain the initial narrower mechanism proof. GPU-scene tests now initialize
the owner like the real host. Logs:
lighting-old-order-gpu-{build,owned0,owned1,paths-owned0,paths-owned1}.log.

Canonical native build/CTest passes15 runnable tests; optional
gpu_opaque_reference remains skipped. New host checks reject malformed metadata,
separate wrapped full identities and distinct source proxies on shared pages,
and reject identity changes as texture-only binding patches. Regressions pass:
538 world checks,123 native-pass checks,180 live GPU scene checks,64 mixed NEW
ownership checks and141 OLD/NEW boundary checks. Logs:
lighting-old-order-{native-build,managed-build,world-regression,
native-pass-regression,live-owner-regression,new-mixed-regression,
boundary-regression}.log. The native-pass result is retained separately as
lighting-old-order-native-pass-result.json. The180-check suite retains its
existing unused-fragment-output validation warning; error counters stay zero.
The world test project retains one unrelated nullable-context compiler warning;
both plugin targets build with zero warnings/errors.

This closes the tested spatial OLD ordering mismatch, not full lighting parity.
The first held panel still differs in decoded normal channels by up to
0.00195503, and unmatched raw HDR blue differs by mean0.15157324/max0.25
(DX11 mean10.08024883 versus Vulkan10.23182201). Screen AO, sun visibility,
color/LOD, metal/gloss/emissive and depth match there. Those differing inputs
need diagnosis; the earlier matched-input HDR tolerance cannot qualify this
new case. Broader source tie/merged-part, voxel/runtime clocks, raster variants,
NEW state/LOD and read-only auxiliary cases remain under the active goal.

Private native stage now8167C0E9BF7A301320447691C94614EEADE64E83A0C0FCC1906FDD97F7A6C1FF;
previous98AB is preserved under LightingMeshDecalAssets/Before-OldOrder-98AB
and immutable prior run snapshots. Ordinary native remains876D526E, unchanged.
No normal-profile deployment occurred. Latest managed net48 after restoring
legacy overloads:1B81718589A1A3BCDC12073E212976D2D0DCCC35164009763961017D5C737A35.

### Missing normal/gloss fallback: source byte correction and HDR follow-up

Stock MyGeneratedTextureManager.CreateAllTextures creates the release missing
normal/gloss texture from Color(127,127,255,0). VulkanTextureCache previously
uploaded (128,128,255,0). GetWorldNormal now uses the stock bytes, retaining
linear sampling and leaving authored textures unchanged. The focused
MaterialTextureContract suite passes all14 checks
(lighting-missing-normal-contract.log). Managed build8168CCC5 contains the fix;
private native8167 and ordinary native876D526E are unchanged.

Live Vulkan5c11f2bcd8944aa1bc2006fab7b9dfee against stock
8d4890912f4e4ed381e26ccb893461e3 removes the systematic normal mismatch:
all four panel decoded normals match exactly. Compare-MeshDecalBuffers now
decodes Vulkan UNORM to float32, matching the DX11 export precision; the
pre-fix control still shows the real one/two-LSB mismatch. Blue HDR mean error
falls from0.15157324 to0.00013220518 in the first panel: two of3782 pixels
differ by one R11G11B10 blue output ULP (0.25). Other panels have one, one,
and zero such pixels. Red/green maximum error is9.53674e-7. This is evidence
of a large improvement, not yet acceptance of the residual HDR difference.
The full identity and spatial ordering gates both pass on this fixed run:
lighting-normal-fixed-identities.json and lighting-normal-fixed-order-gate.json.

An initial FP16 diagnostic7266e8e3c118496b926329179131bd53 is invalid as
FP16 evidence: requested Hdr16=true but actual HqTarget=False. Stock
MyRenderSettings1.Equals omits HqTarget, and MyVideoSettingsManager.Apply
uses that equality to decide whether to forward render settings. The private
harness now explicitly calls MyRenderProxy.SwitchRenderSettings when this
field differs, and SaveCaptureQuality explicitly checks it. The paired
comparator also rejects a requested/applied mismatch. DX11 complete.xml now
records the actual source texture HdrFormat before float32 conversion.
Fresh diagnostic pair ac2afc98ed974cfcb67bd89f6f9906a6 (DX11) and
9e7f617e561648e7991f06d3ef0c7d6c (Vulkan) completed live validation.
The prepared but unlaunched ff385cf66c4d4c828778067a30cdb391 snapshot retains
the old harness and must not be used for FP16 acceptance.

The corrected DX11 capture records actual R16G16B16A16_Float; both sides
record HqTarget=True. Vulkan blue values also exclude a packed B10 source
(3660+ panel pixels contain values between its representable steps).
Both full identity and spatial ordering gates pass on this pair. All panel
GBuffer/depth/AO/sun-visibility values match exactly. Positive RGB HDR differs
by at most one FP16 representable step per channel: red/green max5.960464e-8,
blue max0.0078125; blue mean errors range1.239424e-5 to1.404680e-4.
The screenshots were inspected and show matching four blue panels.
Reports: lighting-normal-fixed-fp16-{comparison,steps,order-gate,identities}.json.
The comparator explicitly rejects the old mislabeled7266 capture.
Vulkan reaches Harness disposed at70.8s; actual game process is terminal.
DX11 controlled exit is terminal but lacks the final disposal marker.

These findings do not establish matched inputs for every lighting producer.
The published final probes are not byte-identical, and their differences
are larger than the panel's FP16 rounding. close-final mip0 mean absolute
RGB delta is approximately(0.00587,0.00606,0.01120), maxima
(0.5625,0.5625,1.125); mip8 still differs by approximately
(0.00726,0.00723,0.01324) on average. Far-final mip8 differs much less
(1.53e-5,3.05e-5,3.05e-5). Per-mip decoded measurements are retained in
lighting-normal-fixed-fp16-probes.json. This is a separate open input issue;
the one-step panel HDR result must not be used to waive it. HDR alpha also
differs by1; stock tone mapping explicitly reads Source[texel].xyz, so this
does not explain the visible RGB result, but general alpha-consumer parity
has not been established by this fixture.

### Separating probe source radiance from publication history

Decoded close-original face5 backing-panel samples in the FP16 pair are
exactly(6.25,6.25,6.25) in both backends. At those same pixels DX11
close-final is(5.6875,5.6875,5.125), while Vulkan close-final is
(6.25,6.25,6.25). Thus the bright-panel discrepancy is introduced after
forward capture, not evidence that forward material shading is brighter.
The inspected source uses alternating fixed-function blends into R11G11B10,
with strength1/max(10,TimeOut/lastFrameDelta). Native uses the same factors.
The independent captures do not share timing/history: held main-frame delta
is0.03105078 DX11 versus0.00930950 Vulkan, and their probe working stages
differ. This does not prove why their publication differs, but invalidates
attributing the observed difference solely to material shading.
lighting-normal-fixed-probe-faces.png displays all six final faces and deltas.

A private explicit MeshImmediateProbes control sets the stock environment
message's TimeOut=0 on both backends. It preserves the timed scenario as a
separate test and changes no production renderer behavior. Manifest
ImmediateProbes authenticates the requested control; captures record actual
DX11 ProbeTimeOut/LastFrameDelta and accepted native ConfigFlags/BlendStrength.
Both mesh and probe comparators reject an unapplied control. The probe
comparator's --mesh-fixture route requires this explicit mode and retains
all per-face/mip measurements without assigning an automatic parity verdict.
Completed control pair: b2e7f92029c3426c985086193299ecde DX11 and
e598d10027244e71ab4e58c8ff5ff464 Vulkan. Native remains8167 and managed
renderer8168; only the private harness/readback companion changed.

Both actual immediate modes are authenticated: DX11 TimeOut=0/state=-1;
Vulkan ConfigFlags=17/BlendStrength=1/stage=0. The spatial OLD gate passes.
All-face/mip probe measurements are in lighting-probe-immediate-cubes.json.
Close-final mip5 mean absolute RGB errors become
(3.258387e-6,3.298124e-6,1.589457e-6), versus approximately
(0.007731,0.007917,0.013481) with independent timed histories. Far-final
mip8 is exact; close-final mip8 differs only in red, mean0.000244140625,
max0.001953125. Mip0 sky retains small spatial sampling differences with
RGB mean approximately(2.89e-5,3.05e-5,2.81e-5), maxima
(0.04834,0.05176,0.05078). This control removes most of the previously
observed publication discrepancy; it does not make timed histories equal.

Source UpdateCullQuery resets to AddAllProbes only at initialization or
TimeOut=0 (and its debug override). VulkanProbes.Configure additionally reset
when camera motion exceeded DrawDistance. New stopped-clock teleport checks
reproduce a real mismatch: the current refresh restarts six faces and discards
publication history. Before log lighting-probe-teleport-before.log fails at
the stage-continuity assertion. Removing the extra camera-distance reset
preserves source AddProbe's current-camera behavior while retaining the
existing refresh. The full GPU probe suite now passes427 checks, including
one-face continuation and byte-identical close/far publication after the
zero-delta teleport; zero core/synchronization errors. Build has zero
warnings/errors. Logs: lighting-probe-teleport-{after-build,after}.log.
The indexed-owner regression passes485 checks with zero core/synchronization
errors (lighting-probe-teleport-indexed-explicit.log and its frozen result JSON).
Its explicit pipeline registration requires SE_VULKAN_GPU_OPAQUE=1 and
SE_VULKAN_GPU_OPAQUE_SCENE=0. Earlier attempts with automatic registration on,
or with native device admission off, fail during setup; their logs remain
preserved as lighting-probe-teleport-indexed.log and -indexed-manual.log.
No native or shader changes were required.

The live timed follow-up90f30297ca254d3ba86a8c12c6ff5782 uses managed
00163B4C5A528FCF303D785613F0A5CA401EE3819DA1E221F9EB8C3E571B6FA6
and native8167. The spatial ordering gate passes; panel RGB HDR remains bounded
to one FP16 step. It reaches Harness disposed at71.5s and its game process is
terminal. Source backing-panel close-original is6.25 in every channel on both
backends. New Vulkan close-final is(5.1875,5.1875,4.25), versus source
(5.6875,5.6875,5.125); the previous extra-reset Vulkan had published6.25
immediately. The correction retains gradual timed history, but these independent
render-step histories do not establish numerical timed-publication equality.
Reports: lighting-probe-teleport-live-{panels,publication,order-gate}.json.

### Pause checkpoint for repository push preparation — 2026-10-20

Parity work is paused at the user's request, not complete. No fixture or GPU
test process remains live. All current source changes and ignored local capture
evidence are retained; no normal-profile deployment occurred. The ordinary
asset DLL remains876D526ECF9E27A2B34DF90BDACDE09FA11DD2F4774D54450AB14195F90FA043.
Build outputs and TestResults are ignored, and native/compiled shader assets
are not tracked. A source checkout must build the native DLL and shader suite
to use the new additive interop exports; do not pair the changed managed code
with the old ordinary asset DLL.

The next parity proof is a matched-render-step timed-probe comparison. Remaining
full-goal cases include OLD ties/merged parts and ownership clocks, NEW selected
LOD/root/per-view order and instance ties, read-only decal auxiliary semantics,
broader accelerated raster variants, and final matched-scene/lifecycle acceptance.
Earlier focused passes do not certify those cases or the complete scene suite.
The user's accepted standard remains stock features and appearance with
documented, scoped GPU tolerances.

### General parity acceptance resumed — 2026-10-20

The user resumed work after release1.2.0 (d03e121) and explicitly allowed
minor/rare edge cases to be noted for later. The milestone now targets
general stock feature and appearance parity rather than exhaustive equivalence
of every allocation clock, tie and optional extension output. Common visible
differences and real validation/lifetime failures still block acceptance.

Fresh broad comparison: DX11 c0e7f193b2144e23a2ee985122d6897f and Vulkan
2c0c3ca588554f7c8ef8b1830147c88f. Both use the same marked baseline world,
pinned render quality/HBAO seed, static held lighting view, moving-geometry
exercises before that view, and actual Anomaly producer/room-topology checks.
Snapshots use managed648ACBA6, native8167C0E9 and Anomaly5C57FE30. These runs
must complete and their captured images/stages be reviewed before acceptance.

Deferred follow-ups, unless broad acceptance exposes a material failure:

- Pathological truncated resource-key collisions, exact allocator reuse clocks,
  and ambiguous coplanar ties in unusual merged/LOD combinations.
- Additional root/selected-LOD, per-view and equal-instance tie permutations
  beyond the covered ordinary LOD, ordering, ownership and history paths.
- Read-only decal writes into optional temporal motion/reactive extension
  targets; stock appearance acceptance does not certify those extra outputs.
- Non-default OLD actor/material culling overrides on optional accelerated
  submission routes, beyond the validated fallback and ordinary technique paths.
- A missing DX11 harness-disposal marker after verified render-object cleanup,
  world unload and actual process exit; real leaks or GPU errors remain blockers.

Independent timed-probe snapshots are not matched-history inputs. Immediate
stock probe controls and stopped-clock scheduling regressions retain their
measured scope; exhaustive render-step-identical convergence is deferred if
the broad scene has no material reflection/lighting discrepancy.

### General lighting/material parity acceptance — 2026-10-20

The fresh broad pair named above completed simulation, producer/topology and
all requested capture checks on release1.2.0. Actual quality dictionaries
match, including the pinned HBAO seed. Both backends expose occupancy512x512,
pointShadowAtlas384x129, playerDepth/litMips/volumeReactive640x480. The atlas
records two lights, twelve face draws and24,949 copied skinned-character
triangles. Both room checks observe actual sealed zero-oxygen topology,
opening a wall and rebuilding it. These new live observations complement the
earlier strict producer and lifecycle tests; availability alone is not being
substituted for their implementation/contract evidence.

Both medium-off and medium-on images were reviewed. The platform materials,
LCD/sprites, glass, lighting and shadows closely match. No exposure fit,
brightness compensation, resampling or quality reduction was used. Detailed
stage comparison: lighting-general-parity-comparison.json. Frozen acceptance
inputs and lifecycle qualifications: lighting-general-parity-acceptance.json.

There are67,538 shared foreground pixels and59,477 with aligned depth/normals.
The26,391-pixel subset with bit-exact captured GBuffer, AO footprint and sun
inputs has RGB HDR mean absolute errors
(0.00005386484,0.00005620459,0.00002869573), maxima0.015625/channel and
P95(0.00006103516,0.00012207031,0.00006103516). These retain the previously
accepted measured GPU tolerance. This subset does not assert that all probe,
local-light, transparent or independently animated inputs are bit-identical.

Across the aligned image region above the HUD, asynchronous final-image RGB8
mean errors are(0.599,0.538,0.623), with P95(2,2,2). On the exact-material
image subset, means are(0.305,0.301,0.327), P95(1,1,1), and93.75% of pixels
are within one RGB8 level in every channel. Isolated image maxima77/51/77
remain recorded rather than hidden by averages. Broader raw HDR differences
above0.25 affect101/67,538 foreground pixels (0.150%), with maximum8.625;
these are outside the exact-input subset and do not redefine its tolerance.
Their sparse highlight/material-boundary variation does not create a material
visual mismatch in the reviewed common scene; further per-pixel attribution
is a follow-up under the user's clarified criterion.

Vulkan validation is enabled with zero errors and no device loss. It completes
world unload, exit request and Harness disposed at215.4s; its actual process
is absent. DX11 completes every capture and simulation/producer check but
stalls in its return-to-menu unload while the stock host logs DLC banner
downloads. Its verified private process is stopped at447s, with an explicit
host-deadline-stop.json. No clean DX11 unload/disposal is claimed. This
reference-host limitation does not reproduce in the current Vulkan run.

The existing live mesh-decal/raster/OLD ordering proofs and normal fallback,
probe, local-light, shadow, motion/history and lifetime regressions retain
their recorded scopes. Together with this fresh broad run they establish the
general lighting/material milestone. The deferred list above remains open for
later work, along with unrelated renderer-registry gaps such as voxel-debris
drawing. General lighting/material acceptance is not a certification of every
backend feature or every possible extension/driver configuration.
