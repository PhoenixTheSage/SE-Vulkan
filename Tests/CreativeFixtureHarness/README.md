# Isolated Creative fixture

This test-only plugin launches the stock empty-space Creative scenario, creates
a powered large platform, small grid, astronaut, point/spot lights, gravity,
LCD, construction stage, door, RenderDirect turbine and dithered preview, then
captures, walks the fixture astronaut, deletes the small grid, saves and unloads
it. It is not deployed with Vulkan.

`-GlassOverlap` selects an independent static scene and stages the current
Release native DLL/shaders privately. It pins quality/HBAO seed and creates
three stacks of the installed HalfWindow model: OLD on the left, NEW in the
center, mixed on the right. Colored decals occupy three depths. Framing follows
the actual glass triangle and model-origin distances. Source observations must
confirm the intended groups before baseline, decal and restored captures;
each state is held until screenshot delivery. Custom objects/decals are removed
before unload. This mode refuses other camera/effect scenarios.
Captures use the supported full-frame PNG path; numeric metrics exclude the lower HUD
using the declared scene region x40,y160,width560,height160.
The central reticle remains within this diagnostic region.

Use `-GlassOverlap -DurationSeconds 300 -PrepareOnly` with the same marked
`-LoadFixtureWorld` on each backend, then `Start-Prepared.ps1`. After exit,
`scripts/verify-glass-overlap-fixture.py --vulkan <run> --dx11 <run> --output
<json>` checks inputs, source states, actual quality, captures, cleanup and
visible decal signal. Pixel metrics require separate review and never certify
whole-renderer equality.

From the repository in PowerShell, after the Release plugin/native build:

```powershell
./Tests/CreativeFixtureHarness/Run-Isolated.ps1 `
  -Bin64 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' `
  -Pulsar 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Pulsar' `
  -ObserveD3D11
```

The script refuses to run beside an existing game/Pulsar process. It copies
Pulsar, plugins and a profile into a fresh GUID folder under
`TestResults/CreativeFixture`, with separately marked game user data. Cloud
storage is disabled only there. Both renderers use a windowed 640×480 baseline;
`launch.json` records the plugin/native hashes. The script returns the child PID
immediately so its reports and bounded lifetime can be monitored.

After a successful Vulkan run, use the absolute path recorded in
`Reports/fixture-initial-save-path.txt` for the matching DX11 run:

```powershell
./Tests/CreativeFixtureHarness/Run-Isolated.ps1 `
  -Bin64 '<installed Bin64>' -Pulsar '<installed Pulsar>' `
  -Backend Dx11 -LoadFixtureWorld '<marked initial fixture snapshot>' `
  -ObserveD3D11
```

The initial snapshot is taken before the timed exercise and copied into new
private user data for comparison. The source snapshot is never loaded in place.
No ordinary player save can be selected: the marker, world name, path containment
and sandbox file are checked before loading. Snapshot creation rejects linked
files/directories.

The timed exercise also submits overlapping standard, additive-bottom and
additive-top billboards through `MyTransparentGeometry`, changes point/spot
colors and ranges with stock lighting-block setters, and checks actual Display
Cancel/OK in the active world. The committed alternate renderer applies only to
the marked private profile and requires a restart; the current world stays ready.

Review overview, LCD text, LCD sprite/clipping, animation, walking astronaut and player-HUD PNGs
alongside `fixture.xml`, `status-latest.txt`, the game log and observer report.
The timed harness records `VisualAcceptance="PendingReview"`; producing a PNG
does not establish correct rendering. DX11 uses stock Pulsar's force-kill exit,
whereas healthy Vulkan shutdown should dispose and remove its session journal.

Optional `-RenderDocDirectory 'C:/Program Files/RenderDoc'` enables the installed
capture layer only in the private child process. The harness binds its published
API 1.4 table, suppresses the overlay, and requests world/LCD frames in `Reports`.
Both `.rdc` deliveries are required for that mode to pass. This mode does not
change normal plugin deployment or global Vulkan layer settings.

## Asteroid extension

Add `-Asteroids -DurationSeconds 360` to the isolated command. The script rebuilds both managed renderer targets before copying the Legacy DLL, preventing stale cross-target deployment. The fixture creates a 128-voxel procedural asteroid (seed 43191), captures near/middle/far views, applies a real spherical storage cut, saves and reloads the mined `.vx2` into a new render actor, captures again and unloads. Use the same switches with `-Backend Dx11 -LoadFixtureWorld '<marked initial snapshot>'` for comparison. Only marked private user data is modified.

Inspect `asteroid-evidence.txt`, `asteroid-mining.txt`, `asteroid-storage-reloaded.txt` and all `asteroid-*.png` captures. The camera uses the sun-facing direction; EmptyWorld's undefined sun is assigned a finite direction only inside the private fixture. Actual game clipmap readiness, render counters and resource release supplement the GPU suite. Stored voxel reload is not a full-world restart test. Full parity and timed crossfade acceptance remain pending.

Camera changes following near/middle/far captures are delayed by half a second so asynchronous screenshot requests can consume the intended view. Historical captures predating this refinement may show the following view; use their status/camera history for LOD activity rather than treating those PNG labels as exact camera evidence. Before/after/reload views remain stationary for their captures.

## Anomaly producer parity mode

Add `-AnomalyParity -DurationSeconds 480` to the isolated command. This builds and snapshots the actual sibling Anomaly core/companion, overlays the current Release native DLL only in the private copy, requests occupancy, point shadows/player depth and lit mips, and registers a controlled shared medium. `launch.json` records the Anomaly core hash; Start-Prepared verifies it. Bundled assets copy through bounded parallel robocopy without deletion or mirroring.

`anomaly-parity.txt` and its history record current availability, dimensions, near-interval callbacks and character/light geometry status. Readiness gates fixed-camera `anomaly-volume-off.png` and `anomaly-volume-on.png`; producing those PNGs is not visual acceptance. The fixture requires all four producers and capture delivery before writing `anomaly-producers-passed.txt`. Use the marked initial snapshot for the matching DX11 command with the same switch. `--interior-only` on Tests/GuestDepthGpuTests runs ten actual-core topology-demand lifecycle checks without opening a GPU context.

The first Vulkan attempt at `TestResults/CreativeFixture/1d8ec3f48464406298ca45bd3999b5de` loaded a real world and proved occupancy, lit mips, 24,949 copied skinned character triangles, 12 point-shadow faces and player depth. It failed the volume gate. Inspection found its saved EmptyWorld disabled oxygen and pressurization, so physical grids had no gas service; a second run with the same settings also could not activate volumes. Both are retained failure evidence, not producer-parity acceptance. Parity mode now enables both settings before creating a new world or loading its private copy; `FixtureBaseline/anomaly-parity-settings.txt` records the copied-world change. The source snapshot and player configuration remain unchanged. The shared snapshot excludes previews/render-only grids, keeps pending physical grids incomplete, bounds selected physical grids, retains the engine ForcePressurize request while tracking, explicitly starts initial topology generation, and restores prior demand on release. The corrected live rerun remains required; do not treat unavailable gas services as empty complete topology.

The third run (`7955ef23f804487c955bef12d2603d2d`) exposed a preparation error: setting only `Sandbox.sbc` did not change the loaded world. Installed `MyLocalCache.LoadCheckpoint` replaces checkpoint settings with `Sandbox_config.sbc` when present. Preparation now updates both XML files in the private copy, and their values must be checked before launch. The run was stopped after its status explicitly reported oxygen=False/pressurization=False; it is failure evidence, not another producer algorithm failure.

Run `31f275d05c3d4781aacbc2ebbc0852a6` reached 295 shared-volume commits and concurrent readiness of all products with actual room topology. At the fixed overview camera it exposed incorrect native projection-scale metadata: QueueWorld supplies a combined view-projection matrix, whose diagonal changes sign under rotation. The bridge now reads the pure projection from the stock frame buffer. Retain the run's history/off PNG as failure evidence; its missing on capture means the fixture did not pass. The strict GPU lane includes this rotated overview direction before the next live run.

Corrected Vulkan run `241403d2adea45149545b157f21c225c` wrote `anomaly-producers-passed.txt` and delivered both fixed-camera PNGs after re-enable recovery. All products were available together; the viewed captures show the controlled medium versus the restored starfield. Its SDK validation remained enabled with zero reported errors. The overall fixture subsequently failed the powered-door cycle (maximum open ratio 0), so do not label it a complete simulation fixture pass. The strict projection regression lane passes 273 checks with validation enabled and zero errors.

Current parity mode also assigns the same finite sun direction on both backends and creates a temporary ventless armor shell. `anomaly-interior-history.txt`/`anomaly-interior-passed.txt` require agreement between engine gas rooms and the active Anomaly snapshot for sealed zero-oxygen center, removed wall/open center, then rebuilt wall/sealed center. The producer gate now requires this check. The shell is not saved. DX11 diagnostic `9ebdc4973ed74608b1d7f27ba915ce32` found the undefined EmptyWorld sun and a missing depth-demand dependency for dynamic producers without a manifest; neither is matched visual evidence. Anomaly now keeps depth active for explicit occupancy or requested enabled media. New live verification remains required.

DX11 run `2f3820429cb4490eacfcb2d09ff09eac` verified producer readiness and the sealed zero-oxygen snapshot, but remained at the removed-wall gate. Installed gas code does not queue a deleted block in its ForcePressurize-only incremental path. Shared tracking now invalidates immediately on grid/door edits and coalesces an explicit full regeneration. The focused `--interior-only` lane passes thirteen checks, including immediate invalidation, coalescing and replacement completion; live open/reseal proof is still required.

Corrected DX11 run `a83b4531c5ac4d5883bb4415b68698f7` passes `anomaly-interior-passed.txt`, `anomaly-producers-passed.txt`, reviewed off/on captures, broader `fixture-completed.txt` and controlled unload/exit. It proves actual zero-oxygen room opening/resealing and dynamic no-manifest depth demand on DX11. The matching corrected Vulkan run is `7c0c93ed3506463ebac77222c6c31f56`; await its results before labeling the pair complete.

Vulkan `7c0c93ed3506463ebac77222c6c31f56` passes both Anomaly markers and reviewed captures with SDK validation enabled/errors=0. Its broader fixture failed the door cycle. Cold compilation can jump wall time over both open/close thresholds in one update; the close command now waits for OpenRatio > .95, preserving the existing maximum-open and final-closed assertions. This refinement needs its own live rerun. `TestResults/GuestDepthGpuTests/live-producer-comparison.json` records the matched enabled sky-region mean RGB difference (about 3/3/2 levels); do not interpret it as whole-image parity, because baseline scene lighting differs.

Run `763298d91a0747d29e713c363c934832` passed both Anomaly gates and advanced past the door assertion, but failed missing `fixture-lcd-text.png`. Its request log shows four capture stages requested in the same second following cold compilation. In parity mode only, scenario time now accumulates at most 100 ms per update so a long render stall cannot collapse several camera/capture stages into one tick. The outer wall-clock timeout is unchanged; allow `-DurationSeconds 600` for this mode's full exercise. The fixture still requires every capture and the original simulation assertions. The clock refinement requires a live rerun.

Final Vulkan run `7d7c38e694614d43a63267a3686fa8ff` passes both Anomaly gates, every capture, the complete simulation fixture and controlled unload/exit with the capped schedule. Door maximum/final ratios are 1/0; astronaut travel is 11.77 m. SDK validation remains enabled/errors=0, device-lost=0, and no managed D3D11 creation attempts are observed. Its off/on captures were reviewed. Together with DX11 `a83b4531c5ac4d5883bb4415b68698f7`, this is the accepted live pair for the four producers; prior runs remain diagnostic history. It does not establish identical base renderer lighting or untested vendor/fault/performance lanes.

## Lighting stage diagnostics

Add `-LightingDiagnostics -AnomalyParity -DurationSeconds 600` to either renderer
route. At the held volume-off camera, a one-frame copy captures decoded
GBuffer color/LOD, normal/AO, metal/gloss/emission, depth and linear HDR before
tone mapping. The Vulkan path appends an extended guest compute packet after
existing frame listeners and reads its owned targets after EndFrame. DX11 hooks
the actual input to stock MyToneMapping.Run, borrowing its device/views and
restoring the compute-stage bindings. This code lives in the optional
CreativeFixtureHarness.Dx11.dll, built/copied/loaded only for DX11 diagnostics;
the shared harness has no SharpDX references. It creates no extra DX11 device.

The diagnostics also capture the actual R8 screen AO and resolved sun-visibility
inputs. DX11 records copies on RenderDirectionalEnvironmentLight's owning
deferred context, then maps them at tone mapping after command-list execution,
before pooled resources can be reused; Vulkan reads the completed owned images
and restores their sampling layouts. These are separate from material AO in
the GBuffer. The native readback is an internal diagnostic entry, with no new
guest aliases or Anomaly API.

Same-frame pre/post-FXAA RGBA8 images are also captured through the owning
immediate DX11 context or completed Vulkan targets. `hbao-constants.bin`
contains the actual 208-byte HBAO global constants used by that frame.
The capture completion gate requires both FXAA images; `PostprocessInputs=1`
records their presence in the metadata.

`Reports/lighting-buffers/complete.xml` records all five 640x480 float32
RGBA files, both R8 inputs, both FXAA images and `frame.bin`. Failure writes `lighting-capture-failed.txt` and
blocks the fixture gate. The raw frame matrices retain each API's CPU storage
convention and must not be compared as byte-identical matrices.

```powershell
python Tests/CreativeFixtureHarness/Compare-LightingBuffers.py `
  '<DX11 Reports/lighting-buffers>' '<Vulkan Reports/lighting-buffers>' `
  --output 'TestResults/lighting-stage-comparison.json'
```

The report includes all shared foreground, subsets with matching depth and
encoded normals, and the subset with exactly matching captured material inputs.
It compares screen AO/sun visibility when both captures include them and records
`LightingInputsComparable=false` for older pairs. It rejects incomplete/nonfinite or differently sized captures,
does no resampling/exposure correction and makes no automatic parity claim.
It compares both FXAA stages when present and records
`PostprocessInputsComparable=false` for older captures. Optional final PNG
metrics are explicitly asynchronous and exclude the HUD region; they do not
substitute for same-frame stage evidence.

The comparator also reports bit-exact depth/GBuffer and AO/sun subsets separately
from its historical tolerance-based subsets. Identical opaque inputs do not
isolate transparent contributions in the before-tonemap HDR target. New captures
write `lighting-buffers/flares.xml` with actual light positions, glare descriptors
and current occlusion factors, plus stock query normalization/state on DX11.

Lighting diagnostics also pin the DX11 baseline quality values before startup
and reapply them through MyVideoSettingsManager after the menu's config reload.
`LightingQuality()` contains the complete explicit profile. The capture checks
actual render settings and writes `Reports/lighting-quality.xml`. The comparator
requires identical quality evidence; renderer-specific automatic defaults are
not a valid full-scene comparison. Use `--allow-unpinned` only to inspect older
diagnostic captures, which are reported with `QualityMatched=false`.

For an explicit Vulkan stock-shadow experiment, add `-StockShadowSchedule
-StockShadowFit`. Fitting requires scheduling/retained cascade ownership.
The switches are saved in the prepared environment; the lighting capture also
writes `shadow-state.xml` from the actual renderer properties. DX11 rejects
these Vulkan-only switches. Stock fitting and scheduling are now renderer
defaults; omit both switches to verify default selection. The captured actual
properties distinguish that run from an explicit opt-in treatment.

`-StockActorLods` explicitly selects the stock Vulkan actor LOD path for
an isolated comparison. It is Vulkan-only. Lighting diagnostics write
`actor-lod-state.xml` with actual old/new LOD enablement and frame selection
counters. Both LOD policies are now production defaults; omit the switches
to verify default selection.
The Vulkan capture also writes `actors.xml` once for the requested frame,
including model/selected representation, shader LOD, visibility, skin/static
ownership, world bounds, camera position and view-projection byte offset.
This supports tracing local stage differences to their actual actor owners.
`-StockNewActorViews` additionally selects the separate newer-actor policy
with independent main/probe/depth levels. Both switches are Vulkan-only.

`-StaticLightingDiagnostics` requires `-LightingDiagnostics` and hides only
the fixture turbine during the held lighting comparison, after the earlier
animation captures. Its actual visibility is checked at capture and recorded
in lighting-scene.xml. Use it on both backends; the comparator rejects mixed
scene states. This isolates static lighting/shadow errors without replacing
the broader moving-geometry acceptance.

Lighting diagnostics pin HBAO's actual initialization to MyRandom seed
20261018 on both backends and record Diagnostics.HbaoSeed in quality evidence.
Production uses stock's time-seeded default. Captures fail if initialization
was not pinned; seeded and historical unseeded captures are not a matched pair.

Lighting diagnostics also save the actual published close/far environment
probes plus their working original cubes as packed `R11G11B10_Float`, in
face-major order with each face's complete mip chain. The Vulkan metadata
records refresh stage/busy/cycle counts: working originals can be partially
cleared during refresh and are not the published main-light inputs.

```powershell
python Tests/CreativeFixtureHarness/Compare-LightingProbes.py '<DX11 Reports/lighting-buffers>' '<Vulkan Reports/lighting-buffers>' --output '<comparison.json>'
```

The comparator checks matching quality/scene/format/shape and reports every
face/mip without fitting exposure, resizing or declaring parity. See
`Tests/LightingParityGpuTests/README.md` for an independent stock ambient
shader replay over these captured inputs.

`glass.xml` records registered transparent material definitions on both
backends, and current native glass packet constants, culling flags and texture
cache identities on Vulkan. It does not claim to capture stock draw CB bytes.
Use the same saved window-containing baseline for both backends when comparing
glass; matching only the turbine visibility flag does not prove that two
independently chosen baseline worlds contain identical blocks.

The isolated `-GlassOverlap` mode also pins a finite sun for EmptyWorld and
saves the actual main-frame constants plus registered material definitions and
renderer multipliers for every phase. It derives SunLightDirection's offset
from the installed stock layouts and rejects nonfinite or unpinned inputs
after scene initialization. The verifier checks those raw bytes and matching
definitions/multipliers independently. This prevents undefined environment
inputs from being mistaken for a cross-API glass shading defect.

Vulkan lighting diagnostics opt into native HDR stage snapshots for the
requested comparison frame. `lighting-stages.xml` describes float dumps named
`after-directional`, `after-points`, `after-spots`, and `end-frame-hdr`.
Schema 2 records `RenderOrder=points,spots,directional`, matching the stock
additive accumulation order. Indices remain 0=after-directional, 1=after-points,
2=after-spots, so stage 0 now includes the accumulated local lights. Historical
Schema 1 captures used directional first. Treat the metadata as authoritative
when attributing differences between stages.
Snapshots are released after readback. The last dump can be compared bitwise
with `hdr.rgba32f` to check that later passes have not changed the source since
the before-tonemap capture. GPU controls verify that the capture itself leaves
HDR bits unchanged in both supported HDR formats.
The same requested frame also installs a stock-source diagnostic pixel shader
through lighting kind 5. Seven `term-*.rgba32f` files record normal/depth,
view/AO, sampled IBL/LOD, BRDF/gloss/NdotV, diffuse/indirect-AO, direct/shadow
and recomposed fogged color/directional-AO, using the actual directional light
descriptor set. These are independent FP32 targets; normal rendering keeps its
selected HDR format. The covered-surface shader does not provide a sky oracle.
The native stage indices 4..10 require this optional shader and capture enabled.
Replay the capture with `Tests/LightingParityGpuTests --captured-ambient --terms`,
then run `python Tests/CreativeFixtureHarness/Compare-LightingTerms.py <capture>
<replay> --output <report.json>`. It checks input identity and dimensions before
comparing foreground values, and reports the intermediate divergence without
declaring full renderer parity.

## Authored blended mesh decals

`Run-Isolated.ps1 -MeshDecals -ObserveD3D11 -Backend Dx11` (then Vulkan)
selects an independent quality-pinned sixteen-panel fixture. It uses private
MWM files written by the installed exporter, both decal alpha conventions,
four texture bundles, and forced OLD/normal admission. Use the same
`-LoadFixtureWorld` seed for both runs. The mode copies current native build
artifacts into each private snapshot; it does not deploy the ordinary assets.

The fixture records 8 OLD/8 NEW admitted DX11 component types, or Vulkan source
flags and 16 actual blended packets, then captures `mesh-decals.png`, removes
the objects and verifies cleanup. The generated 16x16 DDS files deliberately
exceed the installed header reader's 148-byte minimum. Capture rejects fixture
texture-loader errors. Admission and delivered screenshots cannot prove that
textures loaded successfully by themselves.

Run `scripts/verify-mesh-decal-fixture.py <dx11-run-root> <vulkan-run-root>
--output <report.json>` after both processes have exited. It checks pinned
quality, normalized asset identity, source/cleanup and terminal markers, then
reports interior RGB differences per panel. Review the actual images and the
render logs; the metrics do not declare appearance parity automatically.


Add `-MeshDecalOverlap` to `-MeshDecals` on both backends to render two
intersecting decals per panel, with opposite authored alpha-convention order
on alternate row pairs. This requires 32 blended Vulkan packets while retaining
16 actors and 8 OLD/8 NEW sources. The exported asset manifest records Overlap;
image and buffer comparators reject mixing the two scenarios. DX11 also saves
`mesh-decals-old-sort.xml`, containing actual current OLD proxy keys and resource
indices, whose installed comparer orders globally. The mode remains a private
fixture and uses the same capture/cleanup/exit gates as the single-decal mode.


Add `-MeshDecalCutout` to the overlap mode on both backends to insert a green
cutout authored before the two blended parts. Alternating columns use zero
and full cutout masks. The Vulkan witness requires 32 blended packets and 16
cutout packets from fixture-owned mesh identities; historical owned identities
remain tracked through cleanup so stale queued draws cannot disappear from
the witness merely because their actors were removed. DX11 records eight actual
OLD cutout proxies. Comparators reject mixing cutout and ordinary overlap
scenarios, and cleanup requires both packet classes to be zero.
# OLD resource-order fixture

Use `-MeshDecals -MeshOldOrder` as an independent material scenario with the
ordinary isolated Dx11/Vulkan preparation and launch commands. It preloads a
base mesh, red cutout A and blue cutout B, then creates four held panels:
A/B, B/A, A/B with A removed and recreated, and a second A/B control. All
sources request the OLD pipeline. Two read-only cutouts pass against each
panel's opaque base; stock resource sort keys decide which color survives.

The stock observer records the actual `CullProxy.SortingKeys`, LOD-mesh index
and material index in `mesh-decals-old-sort.xml`. Do not infer the oracle from
actor creation order or replace it with Vulkan handles. Admission requires12
actual OLD actors, eight Vulkan cutout packets and no blended packets. Both
captures retain all GBuffer/depth, HDR and lighting inputs, and the standard
comparison script checks that the `OldResourceOrder` scenario matches. Pixel
comparison remains separate from capture/cleanup completion.

`python Tests/CreativeFixtureHarness/Verify-OldResourceOrder.py <dx11-run>
<vulkan-run> --output <gate.json>` regenerates the buffer comparison and exits
nonzero on an ordering mismatch. It verifies actual stock key persistence,
ownership and capture/render-object cleanup markers first. It reports disposal
markers separately; HDR/lighting and final plugin-lifecycle acceptance remain
separate. The single-panel GpuSceneLiveTests control with
`--old-resource-order-only` passes on the known failing live renderer and cannot
replace this four-panel gate.

### Paired HDR and probe controls

`-MeshHdr16` selects the stock high-quality HDR target for both backends. The
harness applies and checks `HqTarget` explicitly because stock quality equality
omits that field. DX11 readback records the actual HDR texture format.

`-MeshImmediateProbes` sets the stock environment-probe timeout to zero for an
explicit control without timed publication history. It requires `-MeshDecals`;
both manifests and actual probe metadata authenticate the selected mode. The
ordinary timed fixture remains separate. Compare its completed probe folders
with `Compare-LightingProbes.py --mesh-fixture`; this route requires the
immediate control and reports all faces/mips without assigning a parity verdict.
