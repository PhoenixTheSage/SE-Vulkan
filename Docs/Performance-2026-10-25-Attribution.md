# Attribution, LCD spikes and allocation

This implements the first session of `planning/PLAN-2026-10-25-MEET-DX11.md`:
instrumentation (0.1–0.3), the gap outside Draw→Present (0.5), the object
motion gate (0.4) and the first allocation pass (1.1).

## Result

Paired product-default runs against the archived baseline build, order BASE,
NEW, NEW, BASE (`TestResults/Session1-Paired-2026-10-25.json`). DX11 on the same
save earlier that day ran at 4.60 ms.

| Set | Present mean | Present P95 | Frames |
|---|---|---|---|
| BASE (`Baseline-2026-10-25-binaries`) | 10.26 / 10.29 ms | 15.58 / 15.48 ms | 2,926 / 2,917 |
| NEW | **9.15 / 9.30 ms** | **11.88 / 11.73 ms** | 3,279 / 3,231 |

Mean −1.05 ms (−10%) and P95 −3.7 ms (−24%), in both orders.

## New instrumentation

All of it is opt-in and inert by default.

- **GPU pass timing**: `SE_VULKAN_GPU_PASS_TIMING=<file>`. A timestamp at every
  `frame_mark` on the primary command buffer, read from completed slots
  only. Each interval to the next mark is attributed to the earlier mark,
  written as JSON at shutdown.
- **Recording laps**: `SE_VULKAN_RECORD_TIMING=<file>`. Recording-thread CPU
  time between the same marks, plus `cpu_lap` points inside preparation and
  the GBuffer pass.
- Both reports cover exactly the harness sampling window. The harness calls
  the new `sevk_set_timing_reports` export when sampling starts and stops.
- **Message census** (`-CpuDetail`): `message-census.json` gives exclusive time per
  `MyRenderMessageEnum`, and each type's share of spike frames (Messages phase
  ≥ 2 ms). `DrawCommands` children count under their own types.
- **Frame gap** (every harness run): `frame-gap.json` splits render-thread time
  outside the CpuMean span.
- **Allocation callers**: `DeepCpuAnalysis --eventpipe` now also writes
  `managed-allocation-callers.json`, the allocating method plus its plugin
  caller.

## Findings

### The 0.8 ms outside Draw→Present

`frame-gap.json`, per sampled frame:

| Part | ms |
|---|---|
| Harness `SampleFrame` in the pipelined Present prefix | 0.51 |
| Renderer `Present` body | 0.23 |
| SE loop (frame waiter, `BeforeRender`, window, sync) | 0.03 |

Half a millisecond of every pipelined Vulkan frame is the harness reading
about a dozen native telemetry values. Players do not pay it, and DX11
harness runs barely do (no native reads). Comparisons against DX11 overstate
the Vulkan cost by that much until the harness samples less.

### GPU and recording split

Clean world frames, product default (`GpuPasses-2026-10-25.json`,
`RecordLaps-2026-10-25.json`):

- **GPU, 3.29 ms full frame**: GBuffer after the first voxel draw 0.98,
  deferred lighting 0.58, HBAO 0.28, cascades 0.24, spot shadows 0.23, object
  motion 0.21, postprocess 0.20, shadow resolve 0.20.
- **Recording, 4.53 ms**: prepare materials 0.76, spot shadows 0.72 (serial
  under the native pass scene), GBuffer NEW main 0.67, object motion 0.56,
  prepare sort 0.30.

### LCD renders were the message P95

The census found `RenderOffscreenTexture` at 0.1 calls per frame and 8.1 ms per
call. It accounted for 6.2 ms of each spike frame and drove the Messages P95
(8.2 ms). Each call did four things:

- waited for the in-flight pipelined recording, because it was an ordinary call;
- allocated a new image;
- invalidated recipes and pass-scene dependencies;
- forced cached materials to rebuild on later frames.

### Allocation

The EventPipe AllocationTick capture measured 1.09 MB/frame. The largest
sources were:

- voxel object constants: 306 KB;
- texture-path strings: about 170 KB over six callers;
- flare results: 85 KB;
- skinned bone palettes: 79 KB;
- glyph paths: 51 KB;
- cell poses: 38 KB;
- frame-constant member names: 35 KB.

## Changes

- **Object motion on demand.** The object-motion replay redraws every
  non-velocity draw and copies the previous slot's instances. It now runs only
  while something reads the motion target: DLSS, FSR, display frame generation
  or upscaling, Anomaly guests, tenants or the velocity probe. Without a
  consumer the target is still handed to sampling, and history resets.
  - `SE_VULKAN_MOTION_ON_DEMAND=0` restores the old behaviour.
  - The GuestDepthGpuTests motion lanes set it, because they read the target
    back without a consumer.
- **LCD renders in place.** A same-sized re-render draws into the image it
  created before. The view, descriptors, cached materials and recipes stay
  valid, so nothing is invalidated. Barriers wait on earlier frames' sampled
  reads, including every mip level.
  - `sevk_render_texture*` are now deferrable, so they replay in order at the
    next waiting call.
  - `SE_VULKAN_OFFSCREEN_REUSE=0` restores a new image per render.
- **Allocation, first pass.**
  - Voxel constants rent from the existing packet recycle pool.
  - Texture paths and their cache keys are memoized per input string.
  - Flares reuse their result buffer, query list and dictionaries.
  - Bone palettes, cell pose commits, glyph bitmap paths and frame-constant
    member paths reuse buffers or cached strings.

Instrumented check after the changes (CpuDetail, `Vulkan-5ce32eed…`):

| Measure | Before | After |
|---|---|---|
| Messages mean / P95 | 1.0 / 8.2 ms | 0.45 / 0.70 ms |
| Allocated per frame | 1,304 KB | 484 KB |
| Frames with a GC | 17% | 6% |
| FrameSlot | 0.75 ms | 0.37 ms |

The pipeline wait census then found 24 waits in 5,634 pipelined frames, all in
`sevk_get_stats` from the once-a-second diagnostics block, at about 7 ms each.

## A regression caught on the way

The first paired run of the new build was 19 ms against 10 ms
(`Session1-Regressed-Paired-2026-10-25.json`). The texture-path cache bounded
its size with `ConcurrentDictionary.Count`, which takes every bucket lock, and
texture lookups run thousands of times per frame. A CpuDetail run showed it
in Messages (8.5 ms) and GlassParts (2.6 ms). A plain counter fixed it.

## Verification

- Native CTests 18/18.
- Validation-layer suites with zero core and sync errors:
  - TransparencyGpuTests 216/216;
  - InteropTests 16/16 (renders the same target twice, so it covers reuse);
  - GBufferGpuSmoke 57/57 (covers LCD mips).
- Validation-layer gameplay runs, layer enabled, 0 errors:
  - `Vulkan-04c35c78…` (motion gate and allocation changes);
  - `Vulkan-204ff8b9…` (final build, 313 frames).
- GuestDepthGpuTests motion lanes (with `SE_VULKAN_MOTION_ON_DEMAND=0`), all
  exit 0, zero sync errors:
  - `--gbuffer-velocity-only` 198 passes;
  - `--gbuffer-velocity-cells-only` 56;
  - `--velocity-probe-only` 54;
  - `--jitter-only` 71;
  - `--voxel-owners-only` 37.
- Not run: a same-process frozen-light lane for the motion gate. Without a
  consumer the motion target is never displayed, and the view capture is
  unaffected by construction. Run it before turning on a consumer-dependent
  default.

## Not done this session

- **1.3, early pass preparation.** Overlapping prepare with voxels needs the
  probe views, which `LightingSetup` produces after voxels and after
  `CollectGuestSceneDepth`. Moving it changes when Anomaly guests see the
  preparation event, which the backend-parity contract protects. Options are
  in the plan.
- **Remaining allocation** (about 480 KB/frame): sprite UV and packet arrays,
  glass constants, light arrays, per-frame main-group states, particle
  closures and pass-preparation arrays.

## Session 2: harness cost, diagnostics stall, parallel pass spots

Changes:

- **Harness telemetry stride.** Pipelined Vulkan samples in the `Present`
  prefix, so every native read there was frame time. Frame timing and present
  samples are still read every frame; the dozen auxiliary reads (and their
  policy-consistency check) run every 16th frame. CpuDetail runs read every
  frame. `SE_VULKAN_HARNESS_TELEMETRY_STRIDE=1` restores every-frame reads.
- **Once-a-second diagnostics before the launch.** The `Present` status block
  makes ordinary native reads. With pipelined recording it now runs before
  `EndFrameAsync` instead of after, where each read waited about 7 ms for the
  recording.
- **Parallel spot recording with native pass views.** Pass spot views are
  depth views: `record_pass_view` reads only prepared material sets, groups and
  pipelines. Workers now record them after the groups are settled on the
  recording thread. `SE_VULKAN_PARALLEL_PASS_SPOTS=0` restores serial
  recording.
- **Allocation.** `MainGroupState` is pooled per frame, since keys trust a
  group only within its epoch and rigid batches key on the token. The
  capturing `All()` in pass model support is now a loop, and the pass view list
  and array are reused.

Results:

- Frame gap: harness sample 0.51 → 0.09 ms; `Present` body 0.23 → 0.11 ms.
- Recording laps: 4.53 → 3.66 ms. Spot shadows 0.72 → 0.49 ms; object motion is
  gone without a consumer. GPU frame 3.06 ms.
- Paired runs, order S1, NEW, NEW, S1, with both sets on the new harness
  (`TestResults/Session2-Paired-2026-10-25.json`). S1 is the session-1 build.

  | Set | Mean | P95 |
  |---|---|---|
  | S1 | 9.09 / 9.81 ms | 11.23 / 14.48 ms |
  | NEW | **9.02 / 9.01 ms** | **11.21 / 10.98 ms** |

  The second S1 run is noisy. The render thread is the wall, so the recording
  savings are headroom, not cadence.
- DX11 on the same harness: **4.44 ms** mean, 5.28 ms P95.
- Validation-layer gameplay run `Vulkan-460a9e7b…`: layer enabled, 0 errors.
  Native CTests 18/18.

## Session 3: stable main order (plan item 1.4), opt-in

The managed traversal visited about 2,700 native-owned actors each frame only
to declare main-group owners. Those declarations numbered NEW groups in
spatial-query order, and native used them to place pass groups among the
managed NEW draws.

With `SE_VULKAN_STABLE_MAIN_ORDER=1` (or the scene property
`StableMainOrderEnabled`):

- **Group order:** each main-group key keeps the order number it got when first
  seen this session (`NativeMainGroups.StableOrder`).
- **Pass-owned actors:** their orders are resident in native
  (`sevk_world_resident_main_owners`, deferrable). They are updated only when
  an actor's pass commit changes, or it is removed. Frame owner orders still
  take precedence.
- **Traversal:** pass- and cell-owned actors are skipped in both the plan and
  the loop. Cell-owned actors are OLD and take no main order.

Results:

- CpuDetail lane `Vulkan-0d2b206e…`: ActorTraversal 2.19 → 1.11 ms.
  MainOwnerDeclare is gone and CandidatePlan dropped from 0.35 to 0.18 ms.
- Paired runs, order NP, NPS, NPS, NP
  (`TestResults/StableMainOrder-Paired-2026-10-25.json`):

  | Set | Mean | P95 |
  |---|---|---|
  | NP | 9.83 / 9.45 ms | 12.84 / 11.79 ms |
  | NPS | **9.37 / 9.21 ms** | **12.06 / 11.21 ms** |

- Same-process frozen-light lane `Vulkan-aa4f4ce6…`, toggling the property:
  - 49 of 50 probe, cascade and spot artifacts are byte-identical.
  - The view changes in 10,348 of 8.29 M channel values (0.125%, max 41). The
    control-return noise floor is 362.
  - The differences are thin coplanar edges only: a floor trim line and
    doorframe edges (`TestResults/StableMainOrder-ViewDiff-2026-10-25.png`).
    That is z-tie resolution between coplanar surfaces whose draw order
    changed.

It stays off by default. DX11 orders NEW geometry by its own cull tree, so
neither order is DX11's. Make it default only after a visual check of those
edges against DX11.

## Session 4: job graph, first stage (voxel plan-ahead)

New CpuDetail phases split the producers (`VoxelCull`, `VoxelMaterials`,
`VoxelEmit`, `Flares`, `Billboards`, `ParticleDraw`, `DecalDraw`). Baseline,
ms per frame:

| Phase | ms |
|---|---|
| Voxel emission loop | 0.78 |
| Voxel cull | 0.18 |
| Particle draw | 0.39 |
| Flares and billboards | 0.17 |

### Voxel plan-ahead (default on)

`SE_VULKAN_VOXEL_PLAN_AHEAD=0` or `VulkanScene.VoxelPlanAheadEnabled` turns it
off.

Right after the shadow fit, `VulkanVoxels.BeginPlan` starts a worker. It runs
the cell cull, every surviving part's object constants and cascade mask, and
the foliage retirement set, overlapping `scene.Draw`. `Draw` joins it at the
original point and does the render-thread work in the original order:

- foliage retirement;
- material preparation;
- recipes and packets;
- foliage and visibility.

Packet order and numbering are unchanged by construction. Thread safety:

- Every plan input is final once the shadows are fitted.
- The constants pool is locked, and constant layouts are warmed on the render
  thread before the fork.
- Profiler scopes stay on the render thread.
- If the camera changed, or the frame never reached `Draw`, the plan is
  dropped and its constants are recycled.

Results:

- CpuDetail: voxels and billboards 1.13 → 0.82 ms. What remains in emission
  (≈0.62 ms) is `AddWorldPacket`, materials, recipes and foliage.
- Same-process frozen-light lane `Vulkan-0c295556…`: all 50 artifacts are
  byte-identical. The view changes in 677 values against a control-return
  floor of 203, the capture noise seen before.
- Paired runs, both orders: new 8.89 / 8.67 / 8.23 / 8.97 ms against baseline
  9.74 / 8.18 / 9.19 / 8.94 ms. Means are 8.69 against 9.01 ms (≈ −0.3 ms); P95
  is 11.0 against 12.5 ms. The host drifted by up to 1.5 ms within a set, so
  read this as a likely 0.2–0.3 ms.

Not forked:

- **Particles.** The stock particle manager runs by reflection and feeds
  particle lights into the scene.
- **Flares and billboards.** 0.17 ms.
- **The scene update steps.** Transforms, spatial index, lights, shadow poses
  and occlusion depend on each other.

Overlapping the actor loop's own emission with other emitters needs the
per-producer emitters of plan item 2.1. They pay only once emission rather
than compute dominates.

## Session 5: work moved from the render thread to recording

### Deferred world intake (opt-in)

`SE_VULKAN_DEFERRED_WORLD_QUEUE=1` enables it; see the results below.

What `queue_world_recipes` still does synchronously:

- checks the frame contract;
- checks every draw's recipe handle, generation, count and object/instance
  span, and revalidates recipes that are not currently valid;
- copies the blobs.

What waits for `settle_world()`, which runs first in `settle_cells()` (at
`end_frame` on the recording worker when pipelined, or at any earlier call
that reads queued draws or mutates recipes):

- building the draw list;
- the OLD order, draw owner and main-order rows sent after the queue, which are
  stashed and applied in order after the build.

Supporting changes:

- `set_world_main_owners` no longer settles; it only replaces the owner map.
- Highlights settle first.
- Shadow exclusions and the `begin_frame` draw count use `queued_draw_count()`.
- The per-draw checks read a compact `recipe_brief` table: generation, object
  bytes, alive, valid, instanced, decal. They no longer scan the full recipe
  records. Scanning those records cache-bound cost about 200 µs for 3,000
  draws: the synchronous intake fell only from 328 to 288 µs when the 196 µs
  build first moved off-thread.

### Deferred cell publish build (opt-in)

`SE_VULKAN_DEFERRED_CELL_PUBLISH=1` enables it, together with deferred cell emission.

`cells_publish` checks the visibility results (duplicates, missing groups)
at once and stashes them. `old_cells::Scene::finish()` builds the draws and
updates the per-cell LOD and shadow state. It runs when cells are emitted, or
before any mutation or cell-state read.

### Not done

- **Scene-update overlap.** The spatial index mutates the single-threaded AABB
  tree, and occlusion begin issues native queries in order.
- **Early pass prepare.** It needs probe views before guest preparation. That
  is an Anomaly ordering question.

### Checks

- Native CTests 18/18, including a new deferred-vs-immediate cell publish case.
- Under validation, zero errors: WorldPassGpuTests 538 (its recipe ABI
  contract checks found every place the deferral had to keep synchronous) and
  NativePassGpuTests 123.
- Validation-layer gameplay run `Vulkan-0a07a377…`: layer enabled, 0 errors.

### Results, and why both are opt-in

CpuDetail lane `Vulkan-1d0c28ef…`, with both enabled:

- `NativeWorldQueue` 0.33 → 0.16 ms; synchronous intake 328 → 162 µs.
- `CellPublish` 0.29 → 0.18 ms.
- But `FrameSlot` rose 0.35 → 0.62 ms (P95 1.70). The build work now sits in
  recording, and `BeginFrame` waits for recording more often.
- The lane's interval matched the previous build (8.97 against 9.01 ms).
- `LogicalDraws` telemetry undercounts at sample time, because cell draws are
  built after the harness reads it.

One paired set, order JG1 (committed build), NEW, NEW, JG1
(`TestResults/Session5-Paired-2026-10-25.json`): new 10.39 / 9.35 ms against
9.13 / 9.01 ms. That is no gain, and possibly a loss.

Both stay in the tree as opt-in switches, and their contract checks pass.
Revisit them once recording has headroom (plan 3.1/3.2), or with `BeginFrame`
made non-waiting.

## Session 6: early pass preparation (opt-in)

This is plan option (b): predict the probe faces instead of moving `LightingSetup`.

- `VulkanProbes.Plan` is a side-effect-free copy of `Configure`'s hold, reset
  and origin decision. It gives the flags and face projections `Configure`
  will publish.
- Right after the actor traversal, `BeginNativePassPrepare` builds the views
  from that plan and calls the new `sevk_pass_prepare_async`, which runs
  the selection on the pass background worker.
- Publication rebuilds the views from the published probe state. It keeps the
  early preparation only if frame and views are byte-identical; otherwise it
  cancels and prepares again.
- The query, missing and fallback accessors join only an in-flight
  preparation (a `preparing` flag). The first version also joined publish's
  async packing, which added 0.5 ms to fallbacks.
- `SE_VULKAN_EARLY_PASS_PREPARE=1` enables it.

Results:

- Prediction: 7,157 frames kept, 0 re-prepared (`vulkan-status.txt`, "Early pass
  preparation"). Validation-layer gameplay run: 0 errors.
- NativePassGpuTests 126: worker preparation draws the same colour and depth
  as synchronous preparation.
- CpuDetail: NativePassPublication 1.40 → 1.00 ms, prepare call 0.44 → 0.01 ms.
  FrameSlot rose to 0.75 ms.
- Paired toggle on the same binary, order off, on, on, off
  (`TestResults/EarlyPassPrepare-Paired-2026-10-25.json`): on 9.22 / 9.29 ms
  against off 8.50 / 8.67 ms.
- That is slower by about 0.65 ms in both orders. Likely CPU contention (the
  prepare fans out over view-job threads while the voxel plan worker, render
  thread and recording also run) and `BeginFrame` waiting on recording. It is
  off by default.

### Pattern across sessions 5 and 6

Moving render-thread work onto workers stopped paying once the render thread
fell to about 8.5 ms. The frame now loses it to contention and to
`BeginFrame` waits on recording. The next gains need removing work outright
rather than moving it: stable main order (pending a DX11 visual check),
recording cost (3.1/3.2), or a non-waiting `BeginFrame`.


## Stable main order: on by default after a DX11 visual check

The coplanar edges the stable order changed were compared, zoomed, against a
DX11 capture of the same saved view
(`TestResults/StableMainOrder-FloorTrim-vs-DX11-2026-10-25.png`):

- **Floor trim:** the old per-frame order draws a dark dashed z-fighting line
  under the light trim. DX11 has no such line, and neither does the stable
  order: it matches DX11.
- **Doorframe:** the changed pixels are single edge pixels, with no visible
  difference from DX11 under either order.

`SE_VULKAN_STABLE_MAIN_ORDER` now defaults on; `=0` restores the per-frame
declaration order.

## Steps 1–3 batch benchmark (2026-10-26)

What it measures:

- step 1, the shared native job pool;
- step 2, managed render jobs on SE's VeryHigh workers;
- step 3, voxel emit-ahead and the flare and billboard fork.

All lanes use the product flag set and pinned binaries:

- `TestResults/Step0-binaries` is `9afdc09`, before step 1, built in a
  worktree with identical compiled shaders;
- `TestResults/Step3-binaries` is `73e74b3`.

Results: `TestResults/Steps1to3-Paired-2026-10-26.json` and
`TestResults/Steps1to3-Baseline-Paired-2026-10-26.json`.

Present interval, ms:

| Lane | Runs (mean / P95) |
|---|---|
| S0, before step 1 | 8.50 / 11.58, 8.93 / 11.43 |
| S3, steps 1–3, same set as S0 | 8.76 / 10.80, 8.74 / 11.15 |
| S3, first set | 8.25 / 9.99, 8.45 / 10.77 |
| S3 with `SE_VULKAN_JOB_PRIORITY=above` | 8.35 / 10.17, 8.26 / 9.84 |
| DX11 | 4.23 / 4.99, 4.23 / 4.87 |

- **Mean:** no measurable change. Interleaved with S0, S3 averages 8.75
  against 8.72 ms; the host moved single runs by about 0.4 ms.
- **P95:** about 0.5 ms better (11.0 against 11.5 ms in the same set).
- **Priority:** above-normal native worker priority made no measurable
  difference, so it stays opt-in.

The first S0 attempts never sampled. The harness, built from HEAD,
referenced `NativeJobStats` from a lambda closure, and Pulsar's type scan
rejected the harness against the old plugin. The job and signature hooks now
live in separate, non-inlined methods without closures, so pinned baselines
load again.

CpuDetail, S0C → S3C (`Vulkan-b690c247…` → `Vulkan-a4bf115a…`), ms per frame:

| Phase | S0C | S3C | Change |
|---|---|---|---|
| Draw (render thread) | 8.10 | 7.83 | −0.27 |
| Voxels and billboards | 0.76 | 0.12 | −0.64 |
| FrameSlot (`BeginFrame` wait) | 1.22 | 1.07 | −0.16 |
| Scene | 3.77 | 4.01 | +0.24 |
| Lighting setup | 0.46 | 0.69 | +0.23 |

- The moved work left the render thread: voxel emission 0.55 and flares 0.10.
- About half of it came back as slower overlapping phases:
  - The voxel emit-ahead job runs during the scene update: transforms
    +0.04, occlusion +0.06, pass commits +0.05.
  - The flare and billboard job runs during lighting setup.
- This is the sessions 5–6 pattern, smaller now that the work runs on a
  capped pool.

Job pool telemetry (`job-stats.json`, `Vulkan-f9701cdf…`):

- Every kind waits 30–40 µs from queue to start.
- Pass-view jobs: about 9 per frame, 204 µs each.
- Joining threads almost never ran their own jobs. That 30–40 µs is wake-up
  latency on each render-thread fan-out.

What this says about the next step:

- Moving 0.6–0.7 ms of render-thread work saved about 0.3 ms, and the cadence
  did not move.
- The largest single wait on the render thread is now FrameSlot: about
  1.1 ms in `BeginFrame` waiting for the recording stage.
- Step 4 (per-frame recording slots, so `BeginFrame` waits only when the GPU
  is two frames behind) addresses that directly. It is the next item.
- Cheaper fan-out (spin briefly before sleeping, or one notify per batch)
  would trim the 30–40 µs per parallel-for.
