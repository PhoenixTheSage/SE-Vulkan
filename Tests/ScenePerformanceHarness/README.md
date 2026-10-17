# Private saved-scene performance comparison

`Run-Isolated.ps1` copies an explicitly selected saved world and the current
Interim profile into `TestResults/ScenePerformance`. It preserves graphics
settings and enabled plugins/mods, uses the built net10 Release renderer DLL,
disables validation and VSync, selects 1920×1080, and changes the private Smooth
Frames cap to 1000. Custom source registrations are cleared only in the copy so
they cannot override the Release DLL. Shader/native assets are copied with the
plugin. `launch.json` records the DLL/native/source checkpoint hashes.

The source save and source user profile are never loaded or edited. Checkpoints
are copied without their Backup directory; private autosave and cloud storage
are disabled. Linked source files are rejected. The script refuses to launch
beside an existing game. All outputs and save/config writes go to private data.

The harness loads the copied checkpoint, waits 45 seconds after session
readiness, captures `Reports/view.png`, then samples 30 seconds of unpaused
rendered frames. `result.json` reports CPU Draw-through-Present duration,
consecutive Present intervals, mean/median/p95/p99, actual dimensions, runtime
and camera position. It exits without saving. `frame-profile.json` records the
Vulkan panel's final gameplay window, including independent asynchronous world
GPU timestamps. That GPU interval excludes uploads, final composition, HUD and
present. Rendered fps comes from Present intervals, not inverse mean CPU work.

Run once with `-Backend Vulkan`, then once with `-Backend Dx11` and identical
arguments. Example:

```powershell
./Tests/ScenePerformanceHarness/Run-Isolated.ps1 `
  -World '<explicit save directory>' -UserData '<source user data>' `
  -Bin64 '<installed Bin64>' -Pulsar '<installed Pulsar>' -Backend Vulkan
```

Compare both view captures before treating results as a same-scene measurement.
The saved camera may differ from a more recent unsaved player view. This is a
benchmark of a saved-world snapshot, not an assertion of visual parity. A
timeout, failure file, missing result or missing screenshot is not a pass.
The returned PID belongs to this private run; allow it to exit before the next
launch. Validation-enabled regression suites are separate from timing runs.

`-CompareRigidBatching` samples batching enabled, then disabled in the same
process. The second phase has a five-second transition warmup and its own
capture/reports under `Reports/without-rigid-batching`. Order and simulation
time still differ; this is a controlled diagnostic, not a promise of speedup.

## Nsight Systems 2026.5.1

From an **elevated PowerShell**, run the same saved-world lane with:

```powershell
./Tests/ScenePerformanceHarness/Capture-Nsight.ps1 `
  -World '<explicit save directory>' -UserData '<source user data>' `
  -Bin64 '<installed Bin64>' -Pulsar '<installed Pulsar>'
```

The script uses the installed Windows x64 CLI, launches the private game under
Nsight, starts collection after `Reports/sampling.txt`, and requests ten seconds
of Vulkan API, annotations and individual GPU workloads. CPU sampling is
disabled in this first lane. It requests SQLite export, excludes environment
variables from the report and leaves the harness to exit unsaved.
`nsight-launch.json` records the capture settings. This run is instrumented;
compare frame times using a separate uninstrumented run.

Normal benchmark children explicitly disable the installed Nsight regular,
systemwide and Reflex implicit layers using their manifest environment controls.
These settings are process-local; the launcher restores its own environment.
`instrumentation.json` records those controls for future runs. A runtime module
inspection can separately verify whether `ToolsInjection64.dll` was loaded.

`Capture-Nsight.ps1 -NsightGpuWorkload batch` selects `--trace=vulkan` and
GPU activity per queue submission, with application debug labels disabled.
This provides a narrower submission/presentation diagnostic when individual
workload tracing drops events. It cannot attribute individual rendering passes;
keep it separate from both the marker lane and uninstrumented frame benchmarks.

Pass ranges are opt-in through `SE_VULKAN_PROFILE_MARKERS=1`. These add Vulkan
debug labels without enabling the validation layer or changing rendering
quality. The normal launcher does not enable them. Each range extends to the
next named phase, so it may include intervening barriers or preparation.

The first local attempt failed before launching the game because the current
desktop shell lacks registry write permission for Nsight's Vulkan layer.
An elevated capture subsequently produced a trace. It warns of missing Vulkan
events and has uneven GPU marker coverage; it provides diagnostic evidence,
but does not establish complete pass attribution or benchmark FPS.
The launcher now checks elevation before creating a private run. This follows
the Windows CLI requirement in the [NVIDIA guide](https://docs.nvidia.com/nsight-systems/UserGuide/index.html).

Once a trace exists, inspect it in Nsight and use the installed CLI's reports:

```powershell
& 'C:/Program Files/NVIDIA Corporation/Nsight Systems 2026.5.1/target-windows-x64/nsys.exe' `
  stats --report vulkan_api_sum,vulkan_gpu_marker_sum '<run>/nsight.nsys-rep'
```

Capture failure, missing GPU ranges, an empty report or a missing private result
is not a successful profiling run. The trace must show warmed gameplay, the
expected resolution and pass markers before attributing a GPU bottleneck.

Use `Summarize-Nsight.py <run-directory>` with Python to summarize the SQLite
export. It filters out negative startup timestamps, retains profiler diagnostics
and compares GPU/CPU marker counts. `PassAttributionComplete=false` is an
explicit evidence gap. API sums are not critical-path CPU frame time.

`-CpuDetail` enables extra CPU clock reads and emits `packet-preparation.json`
only for that lane. It splits rigid lookup/append, managed arena packing, native
queue, auxiliary queues and upload finalization, with cache and byte counters.
The result is marked `Instrumented=true`; use a normal run for FPS comparisons.

`-CompareBatchHints` keeps rigid batching enabled while comparing the per-part
lookup handle enabled/disabled. The second report goes to
`Reports/without-batch-hints`. It cannot be combined with
`-CompareRigidBatching`. Each treatment must keep pixels, grouping rules and
world/output dimensions unchanged. The later phase remains subject to simulation
time and presentation variance.

`-CompareBatchCells` samples 64-metre rigid groups first, then 16-metre groups
after five seconds of settling in the same process. The later result is under
`Reports/16m-batches`; `RigidCellMetres` identifies each sample. It cannot be
combined with another comparison treatment. `-WideBatches` selects the 64-metre
treatment for a standalone run. The retained normal default is 64 metres;
`-NarrowBatches` reproduces the former 16-metre behavior. All grouping keys except spatial cell width
remain the same; the packet sphere encloses the input bounds conservatively.

`-CompareBatchCells -ReverseBatchCells` reverses the sample order. The later
64-metre sample is under `Reports/64m-batches`.
`-CompareBatchPixels` is a separate paused diagnostic: after world warmup it
captures two 16-metre views, two 64-metre views and two 16-metre return views.
It waits for screenshot files before changing modes, records pause/camera and
full-resolution world evidence, then exits the private process unsaved. It emits
`pixel-comparison.json` instead of frame-time results and cannot be combined with
a performance comparison or Nsight capture.

`-ExpandedGeometry` selects the former full-model vertex upload for each material
part. Normal runs use a lossless per-part vertex/index remap. Both retain all
triangles, attributes, shader quality and CPU section indices. Compare separate
private runs with the same save/profile. `geometry-storage.json` reports logical
current MWM/runtime vertex/index bytes and separate skin-buffer bytes; it excludes
allocator overhead, retired resources, terrain and textures. It is not total VRAM.

`Verify-GeometryContract.py` checks the selected stock vertex variants and extra
world definitions against the index-driven merge-instancing constraint. A future
shader using that path requires preserving its vertex-index contract before using
the remap. GPU tests separately verify complete attribute bytes and rigid/skinned
pixel equivalence, with the scene bridge exercising depth and highlight ranges.

`-CompareVoxelLookup` samples the exact-triple atlas lookup cache, then the
legacy group scan in the same process. `-LegacyVoxelLookup` reverses the order;
the second result is under `legacy-voxel-lookup` or `cached-voxel-lookup`.
`VoxelLookupCache` identifies the actual sampled treatment. Only one comparison
treatment is permitted per run.

`-CompareVoxelObjects` compares pre-resolved voxel object field offsets against
the reflected writer. `-ReflectedVoxelObjects` reverses the order; the second
result is under `reflected-voxel-objects` or `cached-voxel-objects`.
`VoxelObjectCache` identifies the actual sampled treatment. Every packet still
receives fresh, zeroed storage and freshly written dynamic fields.

`Capture-Managed.ps1` uses an installed official dotnet-trace executable to
collect ten warmed seconds with the .NET 10 sampled thread-time profile. Its
run is marked instrumented. `Summarize-Managed.py <cpu.speedscope.json>` emits
`managed-summary.json` for the render thread identified by the renderer Draw
patch. Inclusive entries overlap; waits and opaque native boundary samples are
kept separate and do not represent exclusive CPU usage or benchmark FPS.

`Capture-Nsight.ps1 -CpuSampling` requires an elevated host and collects CPU
sampling/context switches with Vulkan tracing and implicit Nsight layers
disabled. It is separate from the Vulkan GPU capture lane.

Vulkan runs now emit `presentation-state.json` from completed render-thread
state. It records the created swapchain, readiness, applied VSync, raw
VkPresentModeKHR, actual image count and generation. Mode is UINT32_MAX
offscreen; the original native work/performance layouts remain unchanged.

`-DisableSteamOverlay` is a private diagnostic using the installed Steam layer's
`DISABLE_VK_LAYER_VALVE_steam_overlay_1=1` contract. It restores the caller's
environment after launch and marks results instrumented/diagnostic. It does not
edit Steam preferences, registry, source save or profile, or suppress Fossilize.
Verify loaded modules before accepting the intended layer difference. Such a
run is not the ordinary overlay-enabled performance workload.

`-CompareTextureValidation` compares the call-scoped native texture validation
cache with the legacy per-packet checks. `-LegacyTextureValidation` reverses the
order or selects the legacy standalone path. The normal retained default is
cached; `-CachedTextureValidation` also selects it explicitly. The later phase
is under `cached-texture-validation` or `legacy-texture-validation`.
`queue-validation.json` records actual native policy and counters for the same
accepted completed frames as the timing rows. Every mesh, voxel-group, layout,
budget and deep-copy check remains active. `launch.json` also records the private
harness hash for provenance.

`Inspect-IndirectShaderContract.py` is a read-only structural intake helper.
It authenticates the catalog, supplemental manifest and selected vertex SPIR-V,
then inspects index-related input builtins for `WorldShaderVariants` object
geometry. It writes `TestResults/Indirect-Shader-Contract.json`. It excludes
voxel/other-pass selection and does not establish indirect execution correctness,
visual parity or a performance gain. Member-decorated index builtins fail
explicitly until input-block resolution is implemented.

`-WorldIndirect` enables the experimental native main/cascade indirect policy.
The launcher explicitly disables it when the flag is absent. Actual device
support, enabled policy and executed group counts are recorded for accepted
completed frames in `indirect-submission.json`; `launch.json` records the request.
Logical packets remain distinct. This option changes submission, not quality;
the direct path remains the default until matched scene evidence supports it.

`-CompareWorldIndirect` samples direct then indirect in one private process;
add `-WorldIndirect` to reverse that order. Each phase correlates accepted timing
rows with the actual native enabled policy. The second phase gets five seconds
of settling and is saved under `indirect-world` or `direct-world`. Experimental
indirect mode is applied only at a policy change, preserving normal cascade reuse.


`-GeometryArena` selects experimental shared device-local world geometry pages
before loading; the launcher explicitly disables it when absent. Actual policy,
page counts, live bytes and capacity are correlated to accepted timing frames in
`geometry-arena.json`. Combine with `-CompareWorldIndirect` (and `-WorldIndirect`
for reverse order) to compare submission at fixed arena allocation. Compare
allocation policies in separate processes: changing it does not migrate meshes.
Both production policies remain off by default.

Harness renderer field lookup is deferred to Vulkan initialization. Initialization failures write `harness-init-failure.txt` only in a launcher-marked private data directory. Reject runs without copied-world identity and completed result metadata.


`-CompareRetainedObjects` selects legacy then experimental retained object storage;
`-RetainedObjects` reverses order. Accepted-frame `object-records.json` correlates
actual enabled mode, selected/changed records, residency and object upload bytes.
This retains native object storage while managed packing/validation remain active.
The production default is off; no speedup is implied by reduced upload bytes.


`-CompareParallelCascades` samples serial then experimental parallel cascade
recording; `-ParallelCascades` reverses order. Actual mode, task count and task
commands are correlated to accepted frames in `cascade-recording.json`. Workers
own separate per-frame command pools, with primary transitions and GPU execution
order preserved. The experimental production policy remains off by default.

Capture-Nsight now waits for the launched profiler to finish plus nonempty report
and SQLite exports. An empty placeholder report or helper marker alone does not
prove capture completion. Inspect diagnostic/sample coverage before attribution.


Native parallel recording is now retained by default for CPUs with at least four
hardware threads. SE_VULKAN_PARALLEL_CASCADES=0 restores serial. The private
launcher continues to set the requested mode explicitly. Summarize-NsightCpu.py
reads CPU-only exports using periodic cpuCycles=1 events, excluding scheduling
stacks from hotspot counts; unresolved symbols limit findings to modules/IPs.

TintShaderPackage builds eight authenticated derived generic pixel variants from
an owned verified stock source copy. Original game shader files remain unchanged.
The derived initializer preserves object tint in grouped GBuffer/probe draws;
World/Probe/integrity tests exercise color, emission, exact pixels and tampering.


`-CompareLightTiles` samples full host lists then experimental GPU frustum/depth
lists; add `-LightTiles` to reverse. Hold `-ParallelCascades` fixed on when testing
the retained policy. `light-tiles.json` correlates actual policy/dispatch with timing
rows and explicitly labels assignments as two-slot completed-frame diagnostics.
The GPU policy remains off by default after whole-frame regressions in both orders.

`-CompareLightPixels` captures paused full/GPU/full pairs, maintaining cell width
and all other rendering settings. Summarize-LightPixels.py verifies camera,
logical/physical coverage, actual mode/dispatch/result epoch and full1080p before
reporting RGB deltas with repeated/return controls. This is a diagnostic, not FPS.
LightTileShaderPackage builds deterministic ordered64 emission from an owned verified
stock PrepareLights snapshot; no installed game file is modified.

Light-list completed counters now report ReadbackUs. Their mapped readback allocation uses cached CPU-read memory, keeping upload buffers on their existing CPU-write policy.


Deeper CPU lane: build `scripts/build-native.ps1 -Configuration Release
-ProfileSymbols -Test` without Deploy, then elevated `Capture-DeepCpu.ps1` for
Vulkan/Dx11. PerfView3.2.8 ThreadTime includes CPU/context-switch/CLR stacks and
SEVulkan-RenderFrames markers. Release /O2 /Ob2 remain; /Zi /Zo/PDB add symbols.
Private ProfileNative DLL/PDB and managed PDB copies preserve exact symbol identity.
Use Tests/DeepCpuAnalysis with run path/PID for marker-bounded CPU/blocked and
allocation/GC attribution. Never treat instrumented times as benchmark FPS.
`/LogFile` permits automation; `/NoGui` creates an interactive console and should
not be used by the capture wrapper. Preserve raw ETL and inspect lost events,
complete frame coverage and symbol logs before interpreting a hotspot.

Retained submission: `-CompareRetainedSubmission` compares the ordinary legacy
and prepared actor/blob/visibility path. Add `-RetainedSubmission` to reverse
the order. Actual actor/part/rebuild/blob counters are in retained-submission.json.
`-CompareSubmissionPixels` performs a paused legacy/prepared/legacy sequence;
Summarize-SubmissionPixels.py checks coverage, camera and full1080p before RGB
comparison. Capture-DeepCpu.ps1 accepts `-RetainedSubmission` for the matching
candidate profile. Distinguish Draw-through-Present CPU from present intervals.
The renderer defaults to retained submission; SE_VULKAN_RETAINED_SUBMISSION=0
restores the legacy route. This harness sets the mode explicitly in every lane.

`-WorkloadAudit` captures queued Vulkan scene mesh/index/instance selection or
prepared DX11 old/new/static geometry and stock LOD settings. It is instrumented
and cannot be used as FPS proof. Vulkan additionally attributes occlusion snapshot
rejection causes during sampling. ModelLodAudit reads stock MWM LOD metadata and
index counts offline. Summarize-GeometryWorkload.py requires both DX11 instance
and static paths and matched save/full1080p before comparing indexed invocations.
This count comparison excludes voxel/transparent geometry and does not establish
identical visibility between separate unpaused frames.

Actor LOD experiment: `-ActorLods` enables authored old-pipeline actor selection;
`-CompareActorLods` compares it against base models, with or without ActorLods to
reverse order. RetainedSubmission/ParallelCascades should be explicit. Actual
selection/reduction/transition counters are in actor-lods.json. Static/skeletal
paths remain on base geometry; the experiment defaults off and is not complete
DX11 parity. Summarize-ActorLodTimings.py preserves both-order proof separately
from instrumented WorkloadAudit snapshots.

Parallel queue: ParallelQueue/CompareParallelQueue selects native immutable-blob
validation. Queue-work.json reports actual workers/draws/copy/validation time.
Normal renderer default is on for CPUs with>=4 hardware threads, with
SE_VULKAN_PARALLEL_QUEUE=0 rollback; harness always sets it explicitly.

CpuDetail now additionally emits renderer-cpu-phases.json for accepted sampled
frames only. QPC scopes are inclusive; CPU time/cycles name the render thread
including native calls on it, and exclude workers. CPU time is quantized. Cycles
are active execution evidence, not milliseconds. Fine actor/part buckets use
wall-only clocks and explicitly mark CPU/allocation unavailable. Counter overhead
is reported. Diagnostic results are not ordinary FPS or exclusive CPU samples.
Short-phase OS CPU time aliases on this system, including averages greater than
the measured phase wall duration. It is now omitted for every phase except Frame;
phase attribution uses cycles. Do not interpret phase CpuMs in the preliminary
75f22016... or da868e22... runs. Their QPC/cycle evidence is retained separately.
Summarize-RendererCpuPhases.py enforces final coverage/CPU validity and can attach
a matched ordinary control via --ordinary-reports.

CpuDetail also emits renderer-cpu-tails.json from the same accepted frames, using
bounded diagnostic storage and reporting dropped samples. Per-phase P95/P99 and
mean phase costs on the slowest 5% of full profiler frames are distinct measures;
inclusive rows must not be summed. The slowest 16 records retain completed Draw
time, profiler wall/CPU, allocation and process-wide GC collection overlap. GC
overlap does not measure a pause or establish causation. Phase arrays follow the
Rows order. Native publication and managed fallback planning now have separate
scopes, including commits, view inputs, preparation, resource requests, occlusion
results, publication, fallbacks and visibility completion. Diagnostics remain off
in ordinary benchmarks and normal gameplay.

Ordinary paired comparisons must supply BenchmarkFrame with the same authenticated
matched-frame.json for both modes. This locks full view/projection/position/FOV
and environment inputs through warmup and sampling, suppresses private gameplay
input, and keeps simulation unpaused. benchmark-frame.json checks every accepted
frame and fails the run on missing or mismatched inputs; launch.json records the
source SHA256. CameraPosition alone cannot qualify a pair: mouse input changed
orientation in the October10 NativeTail baseline1 while leaving position intact.
Those timing rows are preserved but invalid as a matched treatment comparison.
MatchFrame remains the separate paused selection/image mode. Neither camera mode
changes rendering quality or production gameplay settings.

Stock actor size policy: ActorSizeCulling/CompareActorSize selects or compares
the exact installed20-pixel/final-authored-distance rule. Actual actor removal,
main removal, transitions and highlight-only counters are in actor-size-culling.json.
Environment SE_VULKAN_ACTOR_SIZE_CULLING=1 is experimental, default off. Specify
other policies explicitly. CompareActorSize is ordinary only; WorkloadAudit is a
separate diagnostic. DX11 owning-job SizeCandidates records live IsCulled and
independent source-rule evaluation; Summarize-Dx11SizeContract.py reports mismatches.
Summarize-SizeTimings.py verifies modes/build/source/coverage before timing claims.

StockFarCascades/CompareFarCascades selects the installed old actor rule requiring
DrawOutsideViewDistance for cascade index>=4. Actual counters are in
far-cascade-policy.json. Policy is experimental/off by default; voxels unchanged.
Other policies must be explicit. Summarize-FarCascades.py verifies both-order
ordinary modes/build/source/removal before reporting CPU and GPU-window changes.

Group visibility experiment: GroupOcclusion/CompareGroupOcclusion selects or
compares manual-group query ownership; keep ActorLods/RetainedSubmission/
ParallelCascades explicit. Group counters are lifetime snapshots. CompareGroupPixels
captures paused legacy/group/legacy visible output; Summarize-GroupPixels.py compares
RGB with repeat/return controls and permits physical packet regrouping. Both group
and LOD policy remain experimental/off by default. Corrected timings are preserved
by Summarize-GroupTimings.py separately from earlier LOD proxy-bound prototypes.

Camera rotation diagnostics use SE_VULKAN_CAMERA_SWEEP=1. This mode bypasses
the benchmark Draw/Present feature overrides, preserving the renderer's defaults.
SE_VULKAN_PLAYER_TURN=1 routes yaw input through gameplay HandleInput and the
saved character's MoveAndRotate, leaving the first-person camera and actor intact.
It captures 49 settled views through a full turn and reverse return, with actual
camera metadata. The usual private-data marker guard remains required. These
captures require visual review and do not constitute performance samples.

## Rich HUD terminal capture

`-RichHudAudit -AnomalyProject '<Anomaly repository>'` builds and loads Anomaly
in the private profile, opens the terminal with its public `OpenMenu` API, records
transient billboard/material/packet data, captures `Reports/hud-terminal.png`,
and exits without saving. Add `-HudDlssProject '<SE-DLSS repository>'` to load
DLSS with the source profile's copied `Storage/SpaceEngineersDLSS.cfg`; this lane
requires observed `NGX evaluating` status. The local NGX feature library is copied
beside the private plugin, matching DLSS's documented local deployment. Add
`-Validation` for the private Khronos core/synchronization layer.

`Reports/hud-open.txt` is a command/registration acknowledgment, not visual proof.
Inspect the screenshot and `hud-packets.json`. A populated packet queue can still
be invisible if the final scene source bypasses it. Current NGX-enabled runs also
reproduce a pre-existing shutdown access violation; distinguish a valid captured
rendering observation from a clean-exit test pass.