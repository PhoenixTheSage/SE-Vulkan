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

## Scheduler restructure, steps 1–4 (2026-10-25 and 26)

Moved here from the plan when it was rewritten. Commits: `44328e4` (step 1),
`2a6f4b3` (step 2), `ada12c4` and `73e74b3` (step 3), `a8d927c` (step 4).

### What DX11 does

`MyRenderScheduler` builds one dependency graph per frame:

- It runs as a single `DependencyBatch` on **SE's ParallelTasks pool** at
  `WorkPriority.VeryHigh`, capped at `RenderThreadCount`, so it shares
  workers with the simulation instead of competing with it.
- Each view is a chain: cull, then preprocess and prepare, then record into a
  deferred context. Different views run in parallel. There is no separate
  "record everything" stage.
- Big views get N recording jobs, using last frame's proxy count; small
  views get one.
- Job objects come from pools, so building the graph allocates nothing.
- The render thread helps execute the batch, then `Done()` executes the
  command lists in order.

### What Vulkan did before step 1

There was no shared scheduler. On 16 logical cores, these all ran without
coordination:

- **Native:** two `ViewJobs` pools with 3 threads each (pass scene, OLD
  cells), the `Background` packing thread, and a new `std::async` thread every
  frame for each of:
  - recording;
  - each cascade (up to 4);
  - spots (up to 4);
  - upload;
  - the world-queue split (up to 3);
  - async present.
- **Managed:** `Task.Run` (voxel plan-ahead) and `Parallel.For` (main groups).
- **Game:** SE's own simulation, ParallelTasks and Havok threads.

There were no priorities and no cap. A thread that joins blocks instead of
running work.

The recording task also holds `api_mutex` for all of `end_frame`, and every
ordinary native call waits for it (`await_recording`). The pipeline is
therefore only one frame deep: anything moved into recording shows up as
`FrameSlot` wait on the render thread.

### Steps

1. **One native job system** (`Native/src/job_system.h`; done, see below).
   - A fixed worker pool (`SE_VULKAN_JOB_THREADS`; default half the logical
     processors less one, 1–7).
   - Two priorities: render-thread joins high, recording-side work normal.
   - A waiter runs its own group's queued jobs instead of blocking, and never
     another group's, so a waiter holding a lock cannot run work that needs
     it.
   - Per-kind telemetry: jobs, helped, queue wait (mean and max), run time.
   - Every `ViewJobs`, `Background` and `std::async` site is ported.
     Recording and present become persistent `Stage` threads outside the
     cap: they block, so they must not occupy pool workers.

   Gate: native tests, focused GPU suites under validation, and a
   validation gameplay run. Benchmark with step 3, per the batch rule.

2. **Managed producers on SE's pool, like DX11.**
   - Use one `DependencyBatch` at `VeryHigh` with a `MaximumThreads` cap for
     voxel plan-ahead, main groups and the 2.2 producers.
   - The native pool and the managed batch together stay within the logical
     processors minus the game's main, render and recording threads.
   - Read the `job-stats.json` queue waits before and after to size both.

3. **2.1 emitters, then 2.2 fork-join** on that batch. Each producer (voxels,
   particles, decals and billboards, lighting and probe setup, actor
   traversal) gets its own packet list, merged in a fixed order. Gate:
   byte-identical merged packet signature. **First batch benchmark after
   this step.**

4. **Per-view native chains instead of one recording stage** (3.0–3.3,
   restated).
   - Each view becomes a prepare-then-record job writing secondary command
     buffers: cascades and spots (exist), probes, and main view ranges (3.1).
   - Persistent material sets (3.2) remove most of `prepare`.
   - Recording state moves from under `api_mutex` into per-frame slots, so
     `BeginFrame` waits only when the GPU is two frames behind.
   - Revisit the opt-in deferrals from sessions 5 and 6 (world intake, cell
     publish, early prepare) once this lands. **Second batch benchmark.**

5. **Phase 4 frame-build stage.** The render thread does messages and
   changes, submits the graph and joins it.

The graph only helps if its longest chain fits inside 4.17 ms, so removing
work continues in parallel: 2.3 native voxel cells, 2.4 scene update that
only touches changed actors, 3.2, and GC-free frames.

Rough expectation, unmeasured: steps 1–4 reach about 5–6 ms, and step 5 plus
removing work gets below 4.17 ms.

### Step 1 status

`job_system.h` provides `System`, `Group`, `parallel_for` and `Stage`:

- `ViewJobs` and `Background` keep their APIs and lane counts, now on the
  shared pool.
- Queue validation, upload, cascades and spots submit groups.
- Recording and present use `Stage` threads.
- `sevk_get_job_stats` takes no context and never waits for recording.
- The harness writes `job-stats.json` for the sampled window.
- `vulkan-status.txt` gains a "Native job pool" line.
- Native test: `job_system` (CTest).

Checks:

- Native CTests 19/19.
- Zero core and sync validation errors in NativePassGpuTests,
  ShadowOcclusionGpuTests and SceneSubmissionGpuTests (267/267 with the
  default stable order, after the fix below).
- Validation-layer gameplay run `Vulkan-c6d9f04a…`: layer enabled, 0 errors.

First telemetry, from that run (validation inflates run times, so read it for
shape):

- 7 workers, 2 stage threads, peak queue 6.
- Mean queue wait 35–46 µs for every kind, with a 194 µs maximum.
- No job was helped: workers always picked up first.
- PassView jobs run about 187 µs each, so the wake-up cost is about 25% of a
  job.
- Candidates once the benchmark confirms it: spin briefly before sleeping, or
  submit a whole fan-out with a single notify.

### Step 2 status

SE's scheduler, read from the decompiled code (`MySandboxGame`,
`PrioritizedScheduler`):

- `NumberOfCores` is the physical core count when there are more than 8
  logical processors, clamped to 16. The scheduler gets `NumberOfCores/2`
  threads per worker array.
- **VeryHigh** maps to its own array at **Highest** OS priority. High, Normal
  and Low share a Normal-priority array; VeryLow gets a lower array of twice
  the size.
- DX11's `DependencyBatch` uses VeryHigh with `RenderThreadCount`, which
  defaults to `int.MaxValue`. In practice it runs on the 4 Highest-priority
  workers (8 physical cores here).

Done:

- `RenderJobs` (`ClientPlugin/Rendering/RenderJobs.cs`): `Start` and `For` on
  `ParallelTasks` at VeryHigh.
  - Waiting uses `WaitOrExecute`, so a job no worker has taken runs on the
    waiter; failures rethrow.
  - Without a game scheduler (standalone tests), or with
    `SE_VULKAN_MANAGED_JOBS=0`, it falls back to the .NET thread pool.
- Voxel plan-ahead (`Task.Run`) and main-group candidate planning
  (`Parallel.For`) use it. These are the only managed render fan-outs today.
  The 2.2 producers will use it too.
- Budget: native pool 7 + SE VeryHigh 4 + render and recording threads = 13
  of 16 logical processors. The game's Normal and VeryLow arrays still
  compete by OS priority.
- `SE_VULKAN_JOB_PRIORITY=above|highest` raises the native pool's OS
  priority. It is opt-in until the batch benchmark compares it; DX11's
  render workers run at Highest.
- `vulkan-status.txt` reports which scheduler managed jobs used.

Little gain is expected from this step alone: it moves two sites. Its
purpose is that step 3's producers land on the right pool.

### Step 3 status (first producer)

**2.1, per-producer emitters** (`ClientPlugin/Rendering/Interop/NativeEmitters.cs`):

- A producer on a worker binds a `PacketEmitter` to its thread. On that
  thread, `AddWorldPacket` appends to the emitter with emitter-local orders
  and top-level scope state (no main group, part, OLD order or owner).
- `SpliceEmitter` runs on the render thread, at the producer's old serial
  position.
  - It offsets the emitter's orders by `modelSubmissionOrder` and appends.
  - It refuses to splice inside a main-group, owner or logical scope, which
    keeps the merged list identical to serial emission.
- The recipe slow path (`EnsureWorldRecipe`) now locks; the cached-handle fast
  path does not.
- Renderer CPU profiler scopes count only on the render thread.

**Packet signature gate** (`SE_VULKAN_PACKET_SIGNATURE=1`,
`PacketSignatureEnabled`): FNV-1a at `QueueWorld` over every world packet's
fields, recipe handle, main group order, constants and instances.

- WorldPassGpuTests: a worker emitter spliced at its serial position equals
  serial emission; a misplaced splice changes the signature.
- The frozen-light lane writes `packets.sig` per phase; `lightingdiff.py`
  compares `.sig` and `.bin` files too.

**2.2, first producer: voxel emit-ahead** (`SE_VULKAN_VOXEL_EMIT_AHEAD=0` turns
it off):

- The plan job continues into material preparation and emission into the
  voxel emitter, overlapping the actor traversal. `Draw` splices it and adds
  the visible ids.
- A material group or foliage material that needs the texture cache throws
  `OwnerThreadRequired` before changing state. The emitter is dropped, and
  `Draw` emits on the render thread as before.

Checks:

- WorldPassGpuTests 542/542 under validation. This includes the bulk-model
  order check, updated for the earlier stable-order fix, which had broken it.
- Frozen-light lane `Vulkan-c22fee0e…` toggling `VoxelEmitAheadEnabled`
  off/on/off:
  - `packets.sig` is `BE7ACEE2E1A366F7` (2,126 packets) in all three phases;
  - every cascade, spot, probe and frame artifact is byte-identical;
  - view noise is 254 pixels against a 241-pixel return floor.
- Validation gameplay run `Vulkan-fa73ebbb…`: 0 errors. Emit-ahead frames
  688, fallbacks 4 (the first frames, while material groups were created).

Expected gain: the remaining voxel emission (≈0.6 ms) moves off the render
thread while the traversal (≈1.1 ms) runs. Not benchmarked yet.

**Why the other producers are not forked yet:**

- Billboards, flares, particles, decals and the lighting and probe setup all
  resolve textures through `VulkanTextureCache`, which requires the owner
  render thread.
- Particles also feed lights into the scene.
- Forking them needs a thread-safe read path for textures that are already
  resident, with misses deferred to the render thread. That path is hot
  (see the `ConcurrentDictionary.Count` regression), so build it as its own
  step.
- The actor traversal itself (≈1.1 ms) emits inside main-group, owner and OLD
  scopes. Chunked parallel traversal needs emitters that carry scope state.
  That is the 2.1 generalization, and the largest remaining managed item.

### Step 3 status (texture read path, flares and billboards)

**Resident-only texture reads** (`VulkanTextureCache.ResidentOnly()`):

- The cache already read its concurrent maps off the owner thread. On a
  miss, though, it queued a load and returned 0, while the owner thread
  loads synchronously. A forked producer would therefore emit different
  packets on a miss frame.
- Inside the scope, a miss throws `TextureNotResident` before changing
  state, and the producer falls back to the render thread.
- Destroyed generated textures are read too: they change only in message
  processing.

**Measured producer costs** (CpuDetail `Vulkan-a4f595b5…`, ms/frame):

| Producer | ms |
|---|---|
| Billboards | below 0.03 |
| Decals | below 0.03 |
| Flares | 0.085 |
| Particles | 0.31 |

The four producers named in 2.2 add up to about 0.43 ms, not the
1.2–1.8 ms the plan expected from the graph. That estimate assumed voxels
and the traversal; the traversal remains the large item.

**Flares and billboards fork** (`SE_VULKAN_EFFECTS_AHEAD=0` turns it off):

- The render thread collects billboards at their serial point: shared,
  persistent and generated, with the generated queue drained.
- A `RenderJobs` job then runs the flares. Their glare billboards go to a
  thread-local capture list and are appended after the collected billboards,
  the order the queue gave them.
- The job prepares and emits billboard packets into a pending list under
  `ResidentOnly`. It overlaps lighting setup and native pass publication
  (≈1.1 ms), which read lights and actors but do not write them.
- `FinishAhead`, before `QueueWorld`, appends the pending list. After a miss
  it re-emits on the render thread from the same prepared frame.
- Guest billboards added during preparation still land in the next frame,
  as before.
- The packet signature now covers billboard packets.

**Not forked:**

- **Particles (0.31 ms):** the stock particle manager's light updates feed the
  scene update inside `scene.Draw`, so particles must finish first. They
  could only overlap the shadow fit.
- **Decals (below 0.03 ms):** they run before the scene update and read
  parent actors it may move. Moving them later could change their output.

Checks:

- BillboardPacketTests 42/42: forked flares and billboards match serial
  packets, both when the worker's packets are kept and when a texture miss
  re-emits.
- HudBillboardGpuTests under validation: 0 errors.
- Self-check (`SE_VULKAN_EFFECTS_VERIFY=1`): `FinishAhead` re-emits the same
  prepared frame on the render thread and compares every packet field.
  Validation gameplay run `Vulkan-10b6f9e8…`:
  - 0 validation errors;
  - 708 forked frames, 1 fallback (the first texture load);
  - 708 matches, 0 mismatches over 22,411 billboards.
- The frozen-light lane cannot gate billboards across phases. HUD and flare
  billboard content changed between the two serial phases (control vs
  control-return) of `Vulkan-…` (lane run with `EffectsAhead`). Billboards
  therefore get their own signature (`billboards.txt`), and `packets.sig`
  stays world-only. The world packet count was unchanged (2,126).

### Batch benchmark, steps 1–3 (2026-10-26)

See `Docs/Performance-2026-10-25-Attribution.md`, "Steps 1–3 batch benchmark".

- Present mean: unchanged within noise (8.75 against 8.72 ms, same set).
- P95: about 0.5 ms better.
- Render-thread Draw: −0.27 ms. About half the moved work came back as
  slower overlapping phases.
- `SE_VULKAN_JOB_PRIORITY=above`: no difference.
- DX11 in the same session: 4.23 ms.
- Next: step 4. FrameSlot (`BeginFrame` waiting on recording) is about
  1.1 ms, now the largest single render-thread wait.

### Step 4 status: the FrameSlot wait was a driver lock, not recording

The plan assumed `BeginFrame` waited on the recording stage. Measured with
the pipeline report (`SE_VULKAN_PIPELINE_REPORT`), which now times
deferred-call replay, `begin_frame` stages, the frame timeline and
`wait()`:

- **No recording waits.** `Waits` was 0 over about 8,000 pipelined frames.
- **Deferred-call replay:** about 30 calls and 0.36 ms per frame, at the
  first ordinary call.
- **`begin_frame` stages:**
  - the surface query (`match_surface_extent`, every frame) cost 1.52 ms;
  - after it was throttled, the slot fence wait cost 1.3 ms, although the
    fence was already signaled on every frame (`FenceNotReady` 0);
  - `vkWaitSemaphores` alone took 0.83 ms on a satisfied value, with no
    upload flushes.
- **Cause:** the async present thread sits in `vkQueuePresentKHR` for about
  3.9 ms per frame, until the frame's rendering completes. While it is
  there, the driver stalls the render thread's Vulkan calls. Present mode
  made no difference: immediate against mailbox measured the same.

Changes:

1. **Deferred acquire** (`SE_VULKAN_DEFERRED_ACQUIRE=0` restores the old
   behavior):
   - the acquire, the exclusive-mode grab and the present join move from
     `begin_frame` to the start of `end_frame` (the recording worker when
     pipelined);
   - a frame whose acquire fails there is submitted without its swapchain
     pass or present, and the next `begin_frame` recreates the swapchain.
2. **Surface polling** (`SE_VULKAN_SURFACE_POLL_FRAMES`, default 16): the
   capabilities query runs every 16 frames while presenting normally, every
   frame while suspended or without a swapchain, and at once after a
   suboptimal present or acquire.
3. **Present after GPU completion** (`SE_VULKAN_PRESENT_AFTER_GPU=0`
   restores the old behavior): the present thread waits on the frame's
   timeline value, then calls `vkQueuePresentKHR`.
4. **Diagnostic override:** `SE_VULKAN_PRESENT_MODE=immediate|mailbox|fifo`.

Single CpuDetail run, before → after all three (ms per frame):

| Phase | Before | After | Change |
|---|---|---|---|
| FrameSlot | 1.19 | 0.13 | −1.07 |
| Render-thread Draw | 7.88 | 7.44 | −0.44 |
| Scene | 3.87 | 4.17 | +0.30 |
| Lighting setup | 0.79 | 1.03 | +0.24 |

- The present call fell from 3.9 to 1.1 ms; deferred replay from 0.37 to
  0.21 ms; the interval from 8.52 to 8.14 ms.
- The remaining 1.1 ms present call still stalls whatever Vulkan calls the
  render thread makes during it. It now overlaps scene and lighting-setup
  native calls, which explains the two increases.
- Next: find which Vulkan calls the render thread still makes outside
  `BeginFrame`, defer them, or move the present to a point where the render
  thread makes none. Confirm with the paired batch benchmark.

The original step 4 idea, recording state in per-frame slots, waits until
recording actually becomes the wall. It is not the wall today.

Regression found on the way, from the stable-main-order default (`9afdc09`):
SceneSubmissionGpuTests' bulk-model scene pair fails with "invalid model
instance handle/order/cell". Bulk instances carry their main-group order in
`reserved`, and `append_models` still required `reserved <= order`, which
holds only for per-frame first-seen orders. `reserved` is only the main sort
key; the merge with legacy draws uses `order`. Fixed by dropping that check,
since legacy packets already carry stable main orders.

## Why the scheduler steps did not move the cadence (2026-10-26)

Plan item 2.2 promised −1.2 to −1.8 ms. Steps 1–3 delivered −0.27 ms of
render-thread Draw in CpuDetail and no change in the present mean. Four
measured reasons:

1. **The forkable work was small.**
   - Voxels, particles, decals, billboards and flares add up to about 1.0 ms.
   - Only 0.65 ms could move without changing output: voxel emission 0.55
     and flares 0.10.
   - Particles feed lights into the scene update and must finish first.
     Decals read actors the update moves.
   - The 1.2–1.8 ms estimate assumed the actor traversal (about 0.9 ms) would
     fork too. It cannot until emitters carry main-group, owner and OLD-order
     scope.
2. **About half of the moved work returned as slower overlapping phases.**
   Scene rose 0.24 ms and lighting setup 0.23 ms.
3. **Saved render-thread time turned into waiting.** Each frame's render
   thread meets two gates later in the frame:
   - the driver lock held by `vkQueuePresentKHR` (step 4, fixed);
   - `api_mutex`, held by the recording thread for all of `end_frame`
     (below).

   Time saved before a gate only made the render thread arrive earlier and
   wait longer. The cadence stayed set by the gates.
4. **The job pool reorganized threads without removing work.** Its value is
   the cap, priorities and telemetry, not a speedup. Each fan-out also pays
   30–40 µs of worker wake-up.

The lesson for the plan: measure where the render thread waits before moving
work off it. Work moved ahead of a wait buys nothing until the wait is gone.

## Render-thread native call census and the hidden recording wait (2026-10-26)

New pipeline report fields (`SE_VULKAN_PIPELINE_REPORT=<path>`; diagnostics
only, written at shutdown):

- `CallSites`: every ordinary native call by `sevk_*` export, with time
  in the function body and the part of it that overlapped an async present.
- `LockSites` and `LockWaitUs`: time spent acquiring `api_mutex`, by export.
- `RecordEndSinceLaunchUs`, `PostSubmitUs`, `TimelineUs`: frame timeline.
  Counted for pipelined frames only, after a first version averaged in
  menu and loading frames.
- `DeferredUs` and `BeginLapUs`: deferred-call replay and `begin_frame`
  stages.

Findings (`Vulkan-…` runs on 2026-10-26, about 8,000 pipelined frames each):

- **Ordinary call bodies take 2.4 ms of the render thread per frame**,
  excluding load-time init:

  | Export | ms/frame |
  |---|---|
  | `sevk_pass_publish` | 0.50 |
  | `sevk_pass_prepare` | 0.42 |
  | `sevk_begin_frame` | 0.27 |
  | `sevk_queue_world_recipes` | 0.21 |
  | `sevk_cells_prepare` | 0.13 |
  | `sevk_cells_publish` | 0.09 |

  These durations match their CpuDetail values from before step 4, so they
  are not inflated by the present's driver lock.
- **`sevk_get_recipe_stats` is harness-only.** It is 0.19 ms (422 µs per call)
  because it settles cells, and it is called only by the harness's telemetry
  sampling.
- **The harness sample itself is about 0.52 ms per frame.** It sits inside
  Vulkan's present interval but not DX11's, so every Vulkan/DX11 comparison
  carries that bias.
- **The pipelined-wait counter missed the real wait.** `Waits` reads 0
  because an ordinary call blocks on acquiring `api_mutex`; by the time it
  gets the lock, recording has finished.
  - Measured directly, ordinary calls spend **1.2–1.6 ms per frame acquiring
    `api_mutex`**.
  - 1.09 ms of that is `sevk_get_probe_stats`, the first ordinary call in
    lighting setup, waiting about 1.4 ms on 79% of frames.
- **Pipelined recording holds `api_mutex` for about 5.2 ms after launch.**
  The tail after the queue submit is 0.014 ms, so this is recording itself.
- **Lighting setup is probe configuration.** `ProbesConfigure` takes
  0.8–1.4 ms, of which:
  - `ProbeNative` (`GetProbeStats`, `GetProbeScheduleStats`, `SetProbes`):
    0.82 ms, the lock wait;
  - the six face frames: 0.055 ms;
  - the ambient gather: about 0.
- **The lighting setup increases after step 3 were this lock wait.** Turning
  the flare and billboard fork on or off did not change it (0.9–1.5 ms
  either way).

So recording is a wall after all. It gates the render thread at its first
ordinary native call each frame, and that call has been in lighting setup
since the earlier calls became deferrable.

## Step 5: the recording gate and a batch of cleanups (2026-10-26)

All in one pass, tested together at the end.

### Probe scheduling reads from the launch snapshot

- `VulkanProbes.Configure` reads stats and schedule from the launch snapshot
  (`sevk_get_probe_stats_snapshot`, `sevk_get_probe_schedule_stats_snapshot`)
  and queues its configuration (`sevk_set_probes_deferred`).
- The probe face mask still reads fresh state at publication. A queued reset
  counts as not ready until `BeginFrame` applies it.
- Toggles: `SE_VULKAN_PROBE_STATS_SNAPSHOT=0`, or `ProbeStatsSnapshot` on
  `VulkanScene` in-process.
- Frozen-light lane `Vulkan-65f5fff9…`: `packets.sig` `5F90615AFF9239D2`
  (2,126 packets) in all phases; every artifact identical; view noise 148
  against a 471-pixel return floor. ProbeGpuTests under validation: pass.
- Effect: `ProbeNative` 0.82 → 0.015 ms and lighting setup about 1.0 →
  0.23 ms. The wait moved to the fresh read at publication (+1.27 ms there),
  as predicted. Draw did not change until recording got shorter.

### Lazy deferred acquire

- The pipelined timeline, now counted for pipelined frames only:
  - launch → record start: 0.31 ms;
  - record start → image acquired: 1.07 ms;
  - acquired → submit: 3.8 ms.
- The 1.07 ms was step 4's deferred acquire: recording began by joining the
  previous present, which since present-after-GPU waits for the GPU and then
  presents.
- `ensure_frame_image()` now acquires just before the AfterUpscale guest
  stage and swapchain pass, or earlier if a guest reads the display image.
- Effect (single CpuDetail run):
  - recording releases `api_mutex` at 4.09 ms after launch (was 5.20);
  - render-thread lock waits 1.26 → 0.49 ms per frame;
  - Draw 7.55 → 7.08 ms; interval 8.20 → 7.81 ms.
  - Validation gameplay run: 0 errors.

### Smaller changes

- **Job pool:** workers spin for up to `SE_VULKAN_JOB_SPIN_US` (default 30;
  0 sleeps at once) before sleeping; a submit notifies only when a worker
  sleeps; a joining thread spins the same way.
- **GPU diagnostics switch:** `SE_VULKAN_GPU_DIAGNOSTICS=0` drops NV
  checkpoints and resource tracking; `SE_VULKAN_ROBUST_BUFFER=0` drops
  `robustBufferAccess`. Both stay on by default until benchmarked.
- **Harness:** the frame after each telemetry read (1 in 16) is left out of
  the interval statistics, because those reads delay the next frame's start
  and DX11 frames pay nothing similar. `SkippedTelemetryFrames` is in
  `result.json`; `SE_VULKAN_HARNESS_KEEP_TELEMETRY_FRAMES=1` keeps them.
  Intervals from before this change are not directly comparable.
- **Native pass history:** `prepare_pass_gpu` rebuilt a per-row map of
  previous actor transforms every frame. It is only read by motion consumers,
  so it is skipped without one (`object_motion_wanted`), and a later consumer
  starts from a reset.
- **Sort reuse:** world draw sorting reuses this slot's previous order when
  every sort key is identical. `SE_VULKAN_SORT_REUSE=0` sorts every frame;
  `SortReuses` is in the pipeline report.
- **Recording laps:** "prepare pass gpu" is now its own lap. "Prepare
  materials" had included it.

### Not done: parallel NEW main recording (plan 3.1)

The main record path shares mutable state:

- GBuffer velocity writes per-draw data at a running cursor, so its offsets
  depend on recording order;
- frame marks and CPU laps are written from inside the draw loop;
- the stock and forward paths allocate descriptor sets from one pool.

Prerequisites: precompute velocity offsets in prepare, give main-pass workers
their own descriptor pools, and lift the marks out of the loop.

### Checks (all under validation where noted)

- Native CTests 19/19.
- Under validation: NativePassGpuTests 126, SceneSubmissionGpuTests 267,
  ShadowOcclusionGpuTests 36, ProbeGpuTests 427, WorldPassGpuTests 542,
  InteropTests 16, HudBillboardGpuTests 0 errors.
- BillboardPacketTests 43/43.
- Validation gameplay run `Vulkan-9f630754…`: 0 errors.

### Batch benchmark, steps 4–5 (2026-10-26)

Pinned binaries:

- `TestResults/Step3-binaries` (`73e74b3`, before step 4);
- `TestResults/Step5-binaries` (the step 5 tree).

All lanes use the current harness, which drops the frame after each
telemetry read. Results:

- `TestResults/Step5-Core-Paired-2026-10-26.json`;
- `TestResults/Step5-Options-Paired-2026-10-26.json`.

Core set, order S3, S5, DX, DX, S5, S3 (present interval mean / P95, ms):

| Lane | Run 1 | Run 2 | Mean |
|---|---|---|---|
| S3 | 8.02 / 9.52 | 8.64 / 10.84 | 8.33 / 10.18 |
| S5 | 6.87 / 9.23 | 7.38 / 10.44 | **7.12** / 9.84 |
| DX11 | 4.23 / 4.88 | 4.33 / 5.23 | 4.28 / 5.06 |

- Steps 4–5 together: **−1.2 ms** (−1.15 and −1.26 in the two pairs).
- The gap to DX11 is now about 2.8 ms (was about 4.1 ms).

Option set, order S5, S5D, S5E, S5W, S5S0, then reversed. Interval means
(ms; P95 in the run files):

| Lane | Mean | P95 |
|---|---|---|
| S5 | 6.98 | 9.05 |
| S5D, GPU diagnostics and robust buffer access off | 6.98 | 8.87 |
| S5E, early pass prepare on | 7.05 | 10.63 (one 12.7 run) |
| S5W, deferred world intake and cell publish on | 7.04 | 9.11 |
| S5S0, job-pool spin off | 7.12 | 9.16 |

- No option moved the mean outside the 6.80–7.17 ms spread of the S5 runs.
  Defaults stay: diagnostics on, early prepare and deferrals off.
- Spin made no difference to queue waits: 31–41 µs with it, 28–37 µs
  without. Its default is now 0.
- `SkippedTelemetryFrames` was 275 of 4,129 sampled frames in an S5 run.

### Lessons from steps 4–5

- **Gates move, they don't vanish.** Each fix exposed the next gate, which
  only per-call lock timing could show:
  1. the present's driver lock;
  2. the probe read waiting on `api_mutex`;
  3. the publication read;
  4. recording length.

  The cadence fell only when recording itself got shorter (lazy acquire).
- **A wait on a lock is not a wait counter.** Measure lock acquisition.
- **Timeline averages must exclude non-pipelined frames.** Menu and loading
  frames hid the 1.07 ms acquire join.
- **Spinning workers did not shorten the 30–40 µs queue wait.** Fan-outs are
  too far apart for a short spin. The wait comes from somewhere else
  (thread scheduling or the stats path), not from waking a worker.
- **Options that looked promising while the render thread waited (early
  prepare, deferred intake) still do not pay.** Recording ends about when
  the render thread needs it, so moving more render-thread work into
  recording or onto workers still competes for the same few milliseconds.

## Step 6: traversal index, particle reflection, view lanes, recording laps (2026-10-26)

### Changes

- **Managed candidate index excludes native-owned actors.** Under stable main
  order the traversal skipped pass- and cell-owned actors after the spatial
  query returned them. They are now kept out of the index.
  - `SyncIndexMembership` runs in the index refresh and wherever ownership
    changes.
  - Toggles: `SE_VULKAN_INDEX_OWNED=1` keeps them indexed, or
    `IndexExcludesOwnedActors` on `VulkanScene` in-process.
- **Particle light reflection.** The per-light wrappers (`StockLight`,
  `StockEffect`) memoize their reflected reads for the one update they live
  for. `m_data` used to be boxed through reflection about 15 times per light
  per frame.
- **Probe frame buffers** are reused across non-held frames.
- **View-job lanes:** `SE_VULKAN_VIEW_LANES` (default 4). Chunked selection
  merges contiguous ranges in order, so output does not depend on the lane
  count.
- **Recording laps:** "prepare pass gpu" (opaque stock prep), "prepare pass
  streams" (instance copy and pass materials) and "prepare pass history" are
  now separate.
- **Native pass instance streams** of 256 KB or more are copied as a pool
  `parallel_for`, each stream to its precomputed offset.
- **Lane harness fix:** `Configure`'s snapshot path calls the new public
  `NativeBackend.SetProbesDeferred`, which the frozen-light lane also hooks.
  Without it the lane never saw a probe config and failed with "probe capture
  buffer too small" on every phase with the snapshot on.

### Checks

- Native CTests 19/19.
- Under validation: NativePass 126, SceneSubmission 267, ShadowOcclusion 36,
  Probe 427, WorldPass 542, Interop 16, HudBillboard 0 errors.
- BillboardPackets 43/43.
- Validation gameplay run `Vulkan-6a094185…`: 0 errors.
- Frozen-light lane `Vulkan-…` toggling `IndexExcludesOwnedActors`:
  - every cascade, spot, probe and frame artifact identical;
  - view noise 158 against a 486-pixel return floor.
  - `packets.sig` differs even between control and control-return. Removing
    and re-adding proxies reshapes the tree, so the order of the remaining
    candidates, and with it packet order, follows index history. Rendered
    output does not change. This toggle cannot be gated on the packet
    signature.

### Results

Paired, both orders (`TestResults/Step6-Core-Paired-2026-10-26.json`):

| Lane | Runs (interval mean / P95, ms) | Mean |
|---|---|---|
| S5 | 6.97 / 11.29, 6.59 / 8.49 | 6.78 |
| S6 | 6.69 / 8.60, 6.52 / 8.24 | **6.61** |
| DX11 | 4.25 / 5.04, 4.26 / 5.05 | 4.26 |

- S6 against S5: −0.17 ms. The same direction in both pairs; within noise.
- View lanes 8 against 4 (`Step6-Options-Paired-2026-10-26.json`): 6.52 and
  6.55 against 6.55 and 6.56 ms. No change; the default stays 4.

CpuDetail, S5 (`Vulkan-fdcaac34…`) → S6 (`Vulkan-579cd12e…`), ms per frame:

| Phase | S5 | S6 | Change |
|---|---|---|---|
| Draw | 7.08 | 6.09 | −1.00 |
| Actor traversal | 0.92 | 0.35 | −0.57 |
| · Candidate query | 0.37 | 0.02 | −0.36 |
| · Candidate plan | 0.14 | 0 | −0.14 |
| Particles | 0.38 | 0.34 | −0.04 |
| QueueWorld | 0.68 | 0.54 | −0.14 |
| Native pass publication | 1.91 | 2.34 | **+0.43** |

Particle allocation fell from 88 to 60 KB per frame.

### The wall moved back to recording

- The render thread now reaches publication's fresh probe read before
  recording has released `api_mutex`. Lock waits are about 1.05 ms per frame
  (pipeline report), and recording ends about 3.8 ms after launch.
- Render-thread savings ahead of publication turned into that wait. The
  cadence moved only 0.17 ms.
- Recording laps (`record-laps`, ms per frame), total about 3.3:

  | Lap | ms |
  |---|---|
  | Spot shadows | 0.66–0.74 |
  | GBuffer NEW main | 0.63–0.69 |
  | Prepare pass streams (instance copy plus pass materials, which are already cached per slot) | 0.44 |
  | Voxels | 0.14 |
  | Probe geometry | 0.12–0.14 |
  | Prepare geometry | 0.12–0.13 |
  | Sort (reused on 96% of frames) | 0.12–0.13 |
  | Transparency | 0.11 |
  | CSM copy | 0.11 |
  | Particles | 0.10 |
  | Materials | 0.07–0.08 |

- The motion history no longer appears in the laps: no consumer, so it is
  skipped.
- **Parallel instance-stream copy:** "prepare pass streams" went from 0.44 to
  0.405 ms (`Vulkan-bd870f1a…`), so the copy was not the bulk. The rest is
  per render group: material-cache and mesh lookups, offsets, the flush.
  Validation gameplay run `Vulkan-d2f6fd44…`: 0 errors.

## Step 7: early release of the recording lock (2026-10-26)

Step 6 left the render thread waiting about 1.05 ms per frame for recording
to release `api_mutex`. Step 7 lets recording release the lock once it has
taken what it reads from the render thread's scene state. Next-frame
publication then runs while the previous frame records.

### Design

Toggle: `SE_VULKAN_EARLY_RELEASE=0` turns it off. It is on by default since
the second paired set.

- **Release point.** Recording releases the lock in `end_frame`, right after
  `settle_cells()`, when the queued world is fully built. It skips the release
  when a deferred world intake is still pending.
- **What recording keeps.** Recording keeps the world submission it records.
  `WorldState::recording_submission` pins that submission for recording and
  for every job worker it fans out to, and `queue_world_recipes` fills the
  other one.
- **What recording snapshots** before releasing:
  - pass render groups and instance streams (shared pointers);
  - `pass_scene.ready`, the spot views and the static-dirty mask;
  - the probe plan.

  Recording reads these through accessors such as `pass_groups()` and
  `pass_ready_now()`.
- **Who sees which state.** The render thread marks itself as the scene side
  (`WorldState::tl_scene_side`) only inside a scene call. So recording and
  its job workers see the snapshot, and scene calls see live state.
- **A first version marked the recording thread instead,** with a
  thread-local pin. Recording fans out to job workers: parallel spots,
  cascades and view lanes. Those workers saw neither the pin nor the
  snapshot and read the submission and groups the render thread was
  rebuilding. The validation gameplay run crashed during load, and the
  product run failed a frame later.
- **Scene calls (`call_scene`)** proceed once the snapshot is taken, and apply
  only scene-only deferred calls meanwhile:
  - pass prepare, preparation, publish, cancel and transparent requests;
  - cells prepare, preparation, publish and emit;
  - `QueueWorld`, the world order, owner and main-owner rows;
  - probe state.
- **Deferrable scene calls (`call_deferrable_scene`):** pass actor updates,
  pass clear-frame and cell pose commits. Other deferred calls keep their FIFO
  order and replay after recording.
- **`frame_open()`** replaces the `recording` checks in those calls: during
  the overlap, the frame being recorded is not the caller's.
- **Copy-on-write for pass instance streams.** A stream that recording still
  holds is copied before preparation rewrites rows or publication rewrites
  its index. Outside the overlap no stream is shared.
- **Probe planning.** `render_probes` is split into `plan_probes` (advances
  face, stage, cycles and blend kind, and lists the operations) and an
  execute phase. Early release plans when it releases, so the render thread's
  reads during the overlap see the final schedule.
  - `probes.ready` still turns on after the blend is recorded, since this
    frame's descriptors read it.
  - A planned resize applies the reset that `probe_targets` performs. The
    first version planned before that reset, which wiped the plan and broke
    ProbeGpuTests' alternating-blend check on the default path as well.
- **Probe stats.** `sevk_get_probe_stats` waits for recording again, because
  its counters (geometry draws, prefilter passes and the like) depend on
  execution. A new `sevk_get_probe_state` returns readiness and next face.
  The plan fixes both exactly, so the managed face mask reads them without
  waiting.
- **Rare paths.** Switching off object retention, the one queue path that
  clears per-slot state recording uses, waits for the full recording.

### Deferred-call ordering: the highlight crash

The first validation run with early release on by default (`Vulkan-0b092173…`)
failed with "invalid highlight draw".

- **Cause.** `BeginPacketCollection` clears the world each Draw with an empty
  `sevk_queue_world`. That clear was a deferred non-scene call. During the
  overlap, `QueueWorld` (a scene call) ran ahead of it. At `EndUploads`, the
  queued clear replayed, switched submissions and emptied the new one. The
  frame's highlights then indexed an empty submission (67 of 0 draws; 2,995
  in the other).
- **Why earlier runs missed it.** `sevk_set_spot_redraw` was still an ordinary
  call, and it usually flushed the queue before `QueueWorld`. Making it
  deferrable put `QueueWorld` inside the overlap on most frames.
- **Fixes:**
  - the empty world queue is a deferrable scene call, ordered with
    `QueueWorld`;
  - during the overlap, `begin_next_submission` always takes the submission
    recording does not hold. The clear plus `QueueWorld` is two switches in
    one frame, and toggling would have landed the second on the submission
    being recorded.
- **Rule.** A deferred non-scene call must not change state that a scene call
  reads or writes. The other non-scene deferrables (transparency, decals,
  highlights, occlusion, shadow exclusions, resident owners, probe config,
  uploads) either index the frame `QueueWorld` built, in FIFO order after
  their own empty clears, or touch nothing scene calls use.
- Validation gameplay run after the fix (`Vulkan-050dafc2…`): 0 errors.

### Spot caster culling

The per-light caster scan now:

- filters caster-eligible draws once per frame, keeping the full set and the
  dynamic set for overlay-only refreshes, in draw order;
- runs the sphere test before the per-draw exclusion hash lookup, which it
  skips when there are no exclusions.

The spot dirty-tracking pass got the same reordering. The output order is
unchanged.

### Checks

- Native CTests 19/19.
- Under validation, with early release on, and again with the default path:
  NativePass 126, SceneSubmission 267, ShadowOcclusion 36, Probe 427,
  WorldPass 542, Interop 16, HudBillboard 0 errors. BillboardPackets 43/43.
- Validation gameplay runs, 0 errors:
  - early release: `Vulkan-cd595c56…`, `Vulkan-a22766fa…`;
  - off: `Vulkan-4db41ead…`.
- Frozen-light lane (`IndexExcludesOwnedActors`; `Vulkan-e4985097…`,
  `Vulkan-8575de7f…`), covering the probe plan and spot changes:
  - every probe, spot and cascade artifact is identical;
  - view noise is 345 and 441 pixels, against a 486-pixel floor.

### Results

Two paired sets, both orders. The box was noisier than for step 6: DX11 ran
4.40 ms against 4.26.

- `TestResults/Step7-EarlyRelease-Paired-2026-10-26.json`
- `TestResults/Step7b-EarlyRelease-Paired-2026-10-26.json`

| Lane | Interval means (ms) | Mean |
|---|---|---|
| S6 (pinned) | 6.84, 6.88, 7.10, 7.27 | 7.02 |
| Step 7, early release off | 7.08, 6.99, 7.20, 7.02 | 7.07 |
| Step 7, early release | 5.89, 6.59, 6.29, 6.12 | **6.22** |
| DX11 | 4.43, 4.41, 4.49, 4.28 | 4.40 |

- Early release against S6: **−0.80 ms**, the same direction in every pair.
- With it off, step 7 matches S6 within noise.
- The second set already included `sevk_cells_preparation` as a scene call
  and the probe-state read.

Lock census (pipeline report, per pipelined frame):

| | Step 6 | Early release (first build) | Plus cell preparation as a scene call |
|---|---|---|---|
| `api_mutex` wait | about 1.05 ms | 0.014 ms | 0.023 ms |
| Ordinary calls waiting for recording | — | 0.51/frame, 0.40 ms (`sevk_cells_preparation`) | 0.38/frame, 0.32 ms (`sevk_set_spot_redraw`) |
| Recording, launch to end | about 3.8 ms | 4.7 ms | 4.8 ms |

Recording now runs longer: it overlaps the render thread's publication
instead of blocking it, so the two compete for cores. The wait moves to the
next ordinary call. `sevk_set_spot_redraw` is now deferrable, next to the
already deferrable `set_spots` and shadow exclusions.

CpuDetail with early release (`Vulkan-e38fce40…`, first build) against S6
(`Vulkan-579cd12e…`), ms per frame:

| Phase | S6 | Step 7 | Change |
|---|---|---|---|
| Frame | 6.56 | 6.23 | −0.33 |
| Draw | 6.09 | 5.73 | −0.36 |
| Native pass publication | 2.34 | 1.87 | −0.47 |
| QueueWorld | 0.54 | 0.57 | +0.03 |
| Cell publish | 0.24 | 0.30 | +0.06 (the cell preparation wait, since moved) |
| Occlusion | 0.27 | 0.33 | +0.06 |

Most other phases rose 5–10%, because recording now runs concurrently.

### Default-on confirmation (after the highlight fix)

- Native CTests 19/19. Every validation suite passes as above (Probe
  427/427).
- Validation gameplay runs, 0 errors: `Vulkan-050dafc2…` and
  `Vulkan-3ff572be…`.
- Frozen-light lane `Vulkan-271b6a97…`: every artifact is identical; view
  noise is 294 and 346 pixels.
- Pipeline report (`Vulkan-48d4d230…`, 6.03 ms interval, P95 8.27):
  - ordinary calls wait 0.08 ms per frame (0.09 waits per frame);
  - the only sites left are texture uploads, recipe registration and stats;
  - `api_mutex` wait is 0.02 ms;
  - recording ends 4.5 ms after launch.
- Paired set `TestResults/Step7c-Default-Paired-2026-10-26.json`:
  **inconclusive.** Vortex (the mod manager) was using about one core
  throughout.
  - Step 7 runs: 7.19 and 7.43 ms, with P95 about 11.5.
  - S6 runs: 6.95 and 7.86 ms. S6 itself moved 0.9 ms between its runs.
  - DX11: 4.41 and 4.30 ms.
  - Repeat this pair on an idle box before quoting step 7's default-on
    number. Until then, the 6.22 ms from the two clean sets stands.

### Lessons

- **Thread-local markers must cover the job workers a phase fans out to.**
  Mark the side that does not fan out (here, the scene calls) and let
  everything else see the default view.
- **When an ordinary call stops waiting, the wait moves to the next
  ordinary call.** Removing a lock wall means walking the call sequence: the
  census `Sites` list named `sevk_cells_preparation`, then
  `sevk_set_spot_redraw`.
- **Moving state mutation earlier (the probe plan) must also move every
  reset that ran before it** (`probe_targets`).
- **Scene calls jump ahead of queued non-scene calls.** Any deferred call
  that changes what scene calls see must be a scene call itself (the empty
  world clear).
- **A read that serves both scheduling and statistics should be split.** The
  scheduling half can be exact early; the counters cannot.

## Second scene and the user's plugin set (2026-10-26)

A live session in the same Pertam save, in a station interior with an in-world
video screen, ran at about 11.5 ms (87 FPS). That was with every plugin
feature set to vanilla, DLSS off, FXAA on, and validation and the profiler
off. The benchmark reported about 6.2 ms.

### Setup

- **Pinned worlds**, under `TestResults/Worlds` (git-ignored). The live save
  was overwritten with the interior position, so each harness world is now a
  pinned copy:
  - `Pertam-Orbit`, the original benchmark world (copied from a run root);
  - `Pertam-Interior`, the new save.

  `bench-sets.ps1` and `lighting-lane.ps1` use `Pertam-Orbit`; the interior
  lanes are `IN*`.
- **`Run-Isolated.ps1 -UserPlugins`** keeps the source profile's DevFolder
  plugins and local plugin sources, their Storage settings and the compiled
  DevFolder cache. It drops the Vulkan repository's own source entry and
  DevFolder entry, which would otherwise replace the benchmark build and
  break the harness plugin's binding to `Vulkan`. Without the switch, runs
  already include the profile's GitHub plugins and mods.

### Results (interior)

| Lane | Interval mean / P95 (ms) | Ordinary-call waits per frame |
|---|---|---|
| Vulkan alone (`IN*`) | **5.79** / 8.0 | 0.13 ms |
| Plugin set, before | **14.93** / 20.8 | 4.47 ms |
| Plugin set, after the fixes below | **9.57** / 13.5 | 0.14 ms |

- The interior view itself is not slower than the orbit view.
- **The plugin set was waiting on the recording lock.** The census showed:
  - `sevk_update_stream_texture`: 3,004 waits, about 3.5 ms per frame. It
    uploads a 1920×1080 in-world video every frame (video 7330, driven by
    the plugin set; the Vulkan-alone run played only the intro video);
  - `sevk_get_ray_capabilities`: SE-RT queries it every frame, about 0.9 ms
    per frame.
- **Fixes:**
  - while recording, a stream texture update keeps the latest frame per
    texture in a reused buffer and queues one upload, replayed before the
    next frame opens. Removing the texture drops the pending frame;
  - ray capabilities and options take only `ray.mutex` (`call_unlocked`);
  - with an upscaler requested, `sevk_resolve_render_resolution` computes the
    upscaler's fraction from its request and queues the `render_fraction`
    write, instead of waiting for recording. This is the DLSS/FRS case, which
    in the first live screenshot put about 4 ms into the "Particles and
    decals" lap.
- **What remains (3.8 ms): the plugins' own frame work.** The harness
  frame-gap report puts the renderer's Present body at 2.85 ms per frame with
  the plugin set, against 0.07 ms alone. Present runs
  `BackendRuntime.RaiseFrame()`, the plugin frame callbacks (Anomaly guest
  passes, HDR, DLSS, FrameGen and the rest), before launching recording.
  With every feature off, that is idle plugin and framework cost, and it
  needs its own investigation in the plugin repositories.
- Draw-to-Present on the render thread is 6.62 ms with the plugin set,
  against about 5.6 alone.

Runs:
- Vulkan alone: `Vulkan-aaba2530…`;
- plugin set before: `Vulkan-b8149378…`;
- plugin set after: `Vulkan-ae8d1b3b…`, CpuDetail `Vulkan-9dbe4372…`.

### Idle plugin cost in Present (2026-10-26, continued)

New instrumentation, CpuDetail runs only:
- the `GuestFrame` profiler phase around `BackendRuntime.RaiseFrame`;
- `Reports/guest-listeners.json` (`ClientPlugin/Bootstrap/GuestTiming.cs`),
  per-listener time and allocation for the frame, preparation and scene
  callbacks.

Findings:

- **One listener was nearly all of it:** Anomaly's `RenderHost.SubmitVulkan`.
  `GuestFrame` took 3.16 ms and 1,058 KB per frame with every feature off.
  Temporary step laps inside Anomaly found:
  - **Bridge reflection.** Every `GuestBridgeClient.Call`, `ResourceEpoch`
    read and `SupportsPacketFlags` check scanned every loaded assembly
    (`Find()`), and `Call` also scanned every method. This was about 2 ms,
    mostly through `ImagesFor` → `Catalog` → `GuestRendererImage.IsAvailable`
    → `ResourceEpoch`.
  - **`Ordered(programs)`** copied and sorted the program list once per mark,
    seven times a frame.
  - **Plugin portable callbacks while off:** Prism.SSGI `guest.prepare`
    0.19 ms (it re-pushed every setting) and VolumetricClouds `uniforms`
    0.14 ms (it uploaded studio placement).
- **Fixes**, in their repositories and uncommitted:
  - Anomaly caches the bridge type, methods and properties, applies the same
    caching to `Dx11Call` for DX11 parity, and orders by mark once per frame;
  - Prism.SSGI keeps only its disable flags while off;
  - VolumetricClouds returns early while disabled.
- **CpuDetail after:** `GuestFrame` 0.50 ms and 120 KB per frame;
  `GuestPreparation` 0.33 → 0.05 ms; frame allocation 1,550 → 511 KB.

Paired, interior, both orders
(`TestResults/Interior-PluginSet-Paired-2026-10-26.json`):

| Lane | Interval means (ms) | Mean |
|---|---|---|
| Vulkan alone | 5.87, 6.09 | 5.98 |
| Plugin set, all fixes | 6.96, 8.36 | **7.66** (from 14.93, then 9.57) |

The remaining gap is about 1.7 ms. GPU accounts for about 0.6 ms (3.5–3.6
against 2.9). Anomaly's six owned producer passes (CameraVelocity,
LinearDepth, HiZ, ReactiveClear, LitMips, HistoryCopy) submit every frame
with no consumer. That gap is filed in Anomaly's `wiki/Framework-gaps.md`
("Idle framework cost on Vulkan"), for consumer-driven activation on both
backends. The second plugin-set run was 1.4 ms slower than the first;
repeat the pair before quoting a final number.

## Step 8: code for plan items 1–6, not yet benchmarked (2026-10-26)

All new paths are behind toggles that are off by default, except the
allocation fixes, which change no output. Lanes in `bench-sets.ps1`: `NPG`,
`NPM`, `NPD`, `NPMAIN`, `NPEP2`, and `NP8` / `NP8C` with all of them on.

| Item | Change | Toggle | Expected (from the step 7 profile) |
|---|---|---|---|
| Spot casters | 32 m grid over the eligible casters, built once per frame; each light visits only cells in its reach, then restores eligible order and runs the same tests | `SE_VULKAN_SPOT_GRID=1` | Recording: the per-light scan of every caster (spot lap 0.66–0.74 ms before the step 7 prefilter) |
| Pass materials | Per-slot reuse of last frame's material sets when the group keys and kinds, the slot cache (new `material_revision`, bumped on reset and on dirty-texture removal), its epoch and the world mesh set (`world_mesh_revision`) are unchanged; only when every set was cached | `SE_VULKAN_PASS_MATERIAL_REUSE=1` | Part of "prepare pass streams" (0.40 ms) |
| Pass LOD readiness | Each LOD of an actor's family is checked once in preparation (requests) and publication (missing models); request order is unchanged | `SE_VULKAN_PASS_LOD_DEDUPE=1` | Part of prepare 449 µs (requests 154 µs) and publish 551 µs |
| Parallel NEW main | Sorted main entries split into up to four contiguous ranges, each recorded into a secondary on the job pool, executed in order inside a resumed rendering instance with load ops | `SE_VULKAN_PARALLEL_MAIN=1` | Recording: GBuffer NEW main 0.63–0.69 ms |
| Early prepare | Existing toggle, worth re-measuring now that publication no longer waits for the lock | `SE_VULKAN_EARLY_PASS_PREPARE=1` | Prepare (about 0.45 ms) off the render thread |
| Publication allocation | Reused preparation buffers, query results and transparent tickets; the second preparation read reuses them too | always on | Part of 74 KB per frame |
| Sprites | `NativeSpriteBlit`, a blittable twin of `NativeSprite` with the same layout; the main batch fills a reused buffer, with no per-sprite array clones and no element-wise marshaling | always on | About 70 KB per frame, plus marshaling CPU |
| View frusta | Probe-face and spot frusta are pooled and updated through `BoundingFrustumD.Matrix` | always on | A few KB per frame |

Supporting changes:

- the GBuffer velocity cursor is atomic, so workers each take a block;
- `frame_mark` is suppressed on worker threads (thread-local);
- main-pass workers use profiler lanes 12–15 and per-slot command pools,
  reset like the spot pools;
- the native pass-scene unit test now runs every check with LOD dedupe off
  and on.

Parallel main stays serial when:
- GPU-opaque stock entries are present (they allocate shared descriptors);
- the frame has fewer than 384 entries;
- there is only one worker.

Checks (correctness only):

- Native CTests 19/19.
- The GPU suites under validation pass with defaults and again with every new
  toggle on: NativePass 126, SceneSubmission 267, ShadowOcclusion 36, Probe
  427, WorldPass 542, Interop 16, HudBillboard 0 errors, BillboardPackets
  43/43.
- Validation gameplay runs, 0 errors: defaults `Vulkan-269dd92d…`, all
  toggles `Vulkan-84abb0a6…`.
- SpriteGpuTests 29/29 under validation. `MainPackets` now shares placement
  with the blittable path. The plugin-only half is in
  `VulkanSprites.Blit.cs`, because the test projects compile
  `VulkanSprites.cs` alone.
- SpritePacketTests does not build, already at HEAD: its recording backend
  lacks `UploadWorldCubeMips`, which `VulkanTextureCache.cs` calls.
- **Engagement.** An NP8 run with the pipeline report (`Vulkan-8db1542c…`,
  new `ParallelMainFrames` counter) recorded the main pass in parallel on
  12,111 of 12,111 pipelined frames.
  - It showed a 5.46 ms interval (P95 7.61), against about 6.0 for comparable
    single runs with the report on.
  - This is one uncontrolled run, not a benchmark.

Not done, with reasons:

- **QueueWorld skip.** Copying the previous submission would race with
  recording, which still reads it during early release. A separate cache
  would copy about as much as it saves. Attribute `NativeWorldQueue` and
  `BlobPack` first.
- **Harness sampling.** The frame-gap report already puts it at 0.09 ms per
  frame.
- **1a, parallel traversal (0.35–0.40 ms).** Needs the scoped-emitter refactor
  in the plan. The scene update's largest piece, group occlusion (0.31 ms),
  recomputes dirty group bounds over all members. Growing them incrementally
  is easy; shrinking needs a recompute. Measure before changing it.
- **Lights (30 KB per frame).** `NativeLight` and `NativeSpot` need the same
  blittable-twin treatment as sprites.
- **Phase 4.** Design only; see the plan.
- **No pinned Step7 build.** Exporting the commit cannot configure CMake (its
  native dependencies are untracked). Compare `NP8` against `NP`, `S6` and
  DX11.

### Step 8 benchmark (2026-10-26, idle box, both orders)

`TestResults/Step8-Orbit-Paired-2026-10-26.json` and
`TestResults/Step8-Interior-Paired-2026-10-26.json`. Interval mean / P95, ms:

| Lane | Orbit | Interior |
|---|---|---|
| NP, current defaults (steps 7–8, plugin-wait fixes, allocation fixes) | 5.28 / 7.31, 5.36 / 7.51 → **5.32** | 5.32 / 7.49, 5.71 / 8.05 → **5.52** |
| NP8, all step 8 toggles | 5.44 / 7.77, 5.35 / 7.28 → 5.40 | 5.35 / 7.48, 5.51 / 7.59 → 5.43 |
| S6 (pinned) | 7.03 / 11.34, 6.33 / 8.57 → 6.68 | 6.56 / 9.00, 7.28 / 11.96 → 6.92 |
| DX11 | 4.31 / 5.25, 4.23 / 5.05 → **4.27** | 4.31 / 5.20, 4.30 / 5.16 → **4.31** |

- **Current defaults against step 6:** −1.36 ms (orbit) and −1.40 ms
  (interior). The gap to DX11 is now about 1.05–1.2 ms on the mean and about
  2.3 ms on P95.
- **The step 8 toggles do not move the cadence** (+0.08 orbit, −0.09
  interior; both within noise). No single-toggle pairs were run.

Recording laps (`record-laps`; step 6 `NPT` against `NP8C`, ms per frame):

| Lap | Step 6 | NP8 | Change |
|---|---|---|---|
| Spot shadows | 0.661 | 0.488 | −0.17 (step 7 prefilter plus grid) |
| GBuffer NEW main | 0.654 | 0.453 | −0.20 (parallel recording) |
| Prepare pass streams | 0.405 | 0.411 | none (material reuse has no effect) |
| Total | 3.32 | 3.08 | −0.24 |

Render thread (CpuDetail, step 7 `Vulkan-85e727b4…` against NP8C
`Vulkan-49d73068…`):

| Phase | Step 7 (ms / KB) | Step 8 (ms / KB) |
|---|---|---|
| Frame | 6.76 / 385 | 5.93 / 297 |
| Draw | 6.17 / 381 | 5.40 / 293 |
| Native pass publication | 1.77 / 74 | 1.69 / 61 |
| Sprites | 0.15 / 70 | ~0 / 0 |
| QueueWorld | 0.67 | 0.63 |
| Messages | 0.42 / 70 | 0.35 / 70 |
| Particle draw | 0.38 / 60 | 0.32 / 60 |

GC-tagged frames fell from 222 in 4,374 to 199 in 4,982.

Conclusions:

- **The render thread is the wall.** Recording has about 0.24 ms more
  headroom now, so the cadence does not move with recording cuts.
- **Spot grid and parallel main** work as intended (−0.37 ms of recording,
  validation clean). Keep them as opt-ins, and turn them on when render-thread
  cuts bring recording back to the wall.
- **Pass LOD dedupe has no effect** (requests 154 → 160 µs; prepare 449 →
  444 µs). Readiness checks are not the cost; the per-view loops are.
- **Pass material reuse has no effect** on "prepare pass streams". That lap
  is dominated by the stream copy, flush and offsets, not material lookups.
- **The allocation fixes are real:** about 88 KB less per frame, sprite
  marshaling is gone, and fewer frames are tagged with a GC. P95 still
  trails DX11 by about 2.3 ms.

## Quick checks and steps 2–3 (2026-10-26)

### P95 tail attribution

From the NP8C tails, `Vulkan-49d73068…`.

- **The slowest 5% of frames average 9.08 ms against a 5.93 ms mean.** Draw
  accounts for 2.82 ms of the 3.15 ms difference.
- **Garbage collection is not the cause.** Of the 16 recorded slowest frames,
  only four had a gen0 collection and none had gen1 or gen2.
- **Two shapes:**
  - **Broad inflation.** Nearly every phase runs 0.3–0.9 ms long on the same
    frame (transforms, traversal, occlusion, messages, publish, commits): the
    render thread losing CPU, not one phase growing.
  - **Single stalls of about 3 ms that move between phases:** Messages +3.3,
    probe configuration +3.3, glass parts +3.5, prepared-actor rebuild +3.0.
- **Total excess over those 16 frames:** Messages 10.6 ms, TransformsBefore
  7.6, ActorTraversal 6.0, Occlusion 5.4, GlassParts 4.8, probe configuration
  4.8, publish 4.8, commits 4.5.
- During gameplay the game runs its render thread at Normal priority (Highest
  only during the intro video; `MyRenderThread.RenderFrame` applies
  `MyRenderProxy.Settings.RenderThreadHighPriority` every frame). That is the
  same priority as our job workers, the recording thread and the game's
  parallel tasks.

### Lanes

New opt-ins:
- `SE_VULKAN_RENDER_PRIORITY=high` sets the game's own
  `RenderThreadHighPriority`;
- `SE_VULKAN_GC_LATENCY=sustained` selects `SustainedLowLatency`;
- `SE_VULKAN_PASS_VIEW_CACHE=1` reuses an unchanged actor's frustum results
  for views whose parameters are unchanged (exact; the native unit test runs
  all four combinations with LOD dedupe).

Existing opt-ins re-tested now that recording has headroom:
- `SE_VULKAN_DEFERRED_WORLD_QUEUE`;
- `SE_VULKAN_DEFERRED_CELL_PUBLISH`.

Results:

- **Early prepare (`SE_VULKAN_EARLY_PASS_PREPARE=1`) is worse:** 6.60 and
  6.49 against NP 5.57–6.17 (`QuickChecks-Paired-2026-10-26.json`; the first
  runs of that set were contaminated by a native build, but the last
  early-prepare run was not).
- **Clean set** (`Steps2to3-Paired-2026-10-26.json`), interval mean, ms:

  | Lane | Runs |
  |---|---|
  | NP | 5.54, 6.01 (P95 10.1) |
  | Render priority high | 5.52, 5.48 |
  | GC sustained | 5.67, 5.66 |
  | View cache | 5.52, 5.53 |
  | Deferred world queue | 5.52, 5.61 |
  | Deferred world queue + cell publish | 5.69, 5.52 |

  None moves the cadence beyond about 0.1 ms. GC sustained is slightly
  worse. Render priority is consistently at or below NP, with marginally
  better P95 (7.61 and 7.69 against 7.87).
- **The view cache cuts native preparation 478 → 423 µs** (CpuDetail
  `Vulkan-cc209ef5…` against `Vulkan-4266a266…`). "Requests" (about 185 µs)
  is summed worker time and includes per-actor profiling clocks, so it
  overstates the real cost.
- **QueueWorld split (0.63 ms):**

  | Part | ms |
  |---|---|
  | Native intake | 0.23 |
  | Managed blob packing | 0.16 |
  | Transparency | 0.07 |
  | Frame constants, orders, models, decals, highlights | about 0.16 |

  Each world draw's constants are copied twice (managed arena, then native
  submission); removing one copy needs a pointer-based queue ABI.
- **Occlusion split (0.32 ms):** `OcclusionBounds` 0.28 (`RefreshGroupBounds`
  recomputes every dirty group over all members) and `OcclusionResults` 0.03.

### Steps 4 and 6a, and new defaults (2026-10-26)

- **Incremental group bounds** (`SE_VULKAN_INCREMENTAL_GROUP_BOUNDS`):
  - group boxes now update from the dirty members only;
  - an old box that touched an extreme falls back to the full recompute;
  - a verify mode compares every incremental result with the full one:
    82,688 incremental updates, 0 mismatches (`Vulkan-036669a6…`);
  - `OcclusionBounds` was 0.28 ms per frame.
- **Stacked lane** (view cache, incremental bounds, render priority, spot
  grid, parallel main; `Step4-Stacked-*`): ahead of NP in all four pairs,
  by 0.05–0.52 ms (about 0.1 orbit, 0.3 interior). The box was noisy (DX11 ran
  4.45; Codex was active).
- **New defaults:** view cache, incremental bounds, spot grid and parallel
  main.
  - Validation: suites all pass, the gameplay run has 0 errors, and the
    frozen-light lane is clean.
  - Against everything off (`Defaults9-*`) they are flat: orbit 6.09 / 6.10
    against 6.04; interior 5.89 / 5.67 against 5.81 / 5.78. One NPOFF run was
    invalid (it never rendered the world).
  - So the stacked lane's gain came mostly from render priority. It stays
    opt-in, because a Highest render thread could starve the game's update
    thread on CPUs with few cores.
- **Early pass prepare, fixed (6a).** Two hidden waits made it 0.6–0.9 ms
  slower:
  - `sevk_pass_prepare_async` was still an ordinary call;
  - `VulkanProbes.Plan` read `GetProbeStats`, about 1.5 ms per frame of
    waiting found by the census.

  Now the prepare is a scene call, and `Plan` reads the same launch snapshot
  as `Configure`. Waits are back to streaming only. Kept 11,911 frames,
  re-prepared 12.
  - Pairs (`EarlyPrepareFix2-*`): orbit 5.50 / 5.60 against 5.60 / 5.65;
    interior 5.96 / 5.68 against 6.36 / 5.73. Ahead in every pair, by about
    0.05–0.1 ms.
  - On by default after validation.
- **QueueWorld deferral** (deferred world queue and cell publish): no gain;
  stays off.
- **Allocation round two:** not pursued.
  - The tails show P95 is not GC-driven.
  - Lights would need blittable `NativeLight` and `NativeSpot`, but the
    frozen-light lane hooks `SetLights(NativeLight[])` and `SetSpots`, and
    pinned-baseline runs depend on those signatures.

### Where this leaves the goal

Today's sets ran 5.5–5.65 ms (NP) against DX11 4.33–4.47. Every
render-thread lever measured since step 8 is worth about 0.1 ms or less on
its own:
- LOD dedupe;
- material reuse;
- view cache;
- incremental bounds;
- QueueWorld deferral;
- early prepare;
- GC mode.

The render thread does about 5.4 ms of serial managed and native work per
frame. Getting under 4.17 ms needs that work to overlap the next frame
(Phase 4, frame-build stage) or a deep incremental rewrite of publication
(the full 1b design). Small cuts will not close a 1.2 ms gap.

## Phase 4 step 1: overlapped native publication (2026-10-27)

"Complete Phase 4" was scoped to what can run without a frame-packet snapshot.
The full Apply/Build split needs one, because the traversal reads game-owned
`MyActor` objects that `ProcessMessages` mutates for the next frame. This step
is the intra-frame part: the native publish leaves the render thread's
critical path inside the same frame.

### What it does (`SE_VULKAN_OVERLAP_PUBLICATION=1`, opt-in)

- The native pass preparation starts right after the native commits, from the
  probe plan, so it overlaps the actor traversal.
- After the traversal, `BeginNativePassPublication` resolves missing levels and
  runs the occlusion queries on the render thread (same order as before), then
  launches the native publish as a render job.
- The publish overlaps voxels, flares and lighting setup.
  `FinishNativePassPublication` joins it, rebuilds the probe views from the
  configured probes, and on a mismatch (a probe reset, about 17 frames per run)
  prepares and publishes again synchronously. Then it emits glass and
  fallbacks, publishes the cells and ends visibility.
- New profiler phase `PublicationJoin`; the diagnostic line reports
  republishes and the job's queue, run, join-wait and slack times.

Validation with the toggle on: GPU suites pass, the VAL gameplay run has 0
errors, and the frozen-light lane is clean.

### Measurements

Job timing (orbit, one run): queue 0.028 ms, run 0.664 ms, join wait
0.288 ms, ready early 0.071 ms. The publish job runs longer than the work it
overlaps (about 0.4 ms of voxels and lighting setup), and the render thread
waits about 0.29 ms at the join.

Lock census (NPOVP): `LockBlocked` 1,424 over 8,222 frames. Almost all of it
is two lighting-setup calls waiting behind the publish, which holds
`api_mutex` for the whole selection:
- `sevk_set_probe_atmosphere`: 658 waits, 346 ms;
- `sevk_get_flare_results`: 659 waits, 345 ms.

That averages about 0.08 ms per frame. Moving the probe-view check before the
join gains nothing: `ProbeFaceMask` makes native calls, which block on the
same lock.

Paired runs, quiet box (about 9% CPU), present interval mean / P95, ms:

| World | NP (off) | NPOV (on) |
|---|---|---|
| Orbit | 5.47 / 7.84, 5.32 / 7.42, 5.55 / 7.90 (mean 5.45) | 5.78 / 8.16, 5.19 / 7.25, 5.42 / 7.28 (mean 5.46) |
| Interior | 5.44 / 7.48, 5.47 / 7.50 (mean 5.455) | 5.43 / 7.43, 5.42 / 7.58 (mean 5.425) |

Results: `TestResults/Phase4Step1-Orbit-2026-10-27.json`,
`TestResults/Phase4Step1-Interior-2026-10-27.json`.

### Conclusion

Flat within noise on both worlds, so the toggle stays opt-in. The overlap can
save at most about 0.4 ms (the overlapped window). It loses roughly that much
to the join wait (0.29 ms), the lighting-setup calls that block on
`api_mutex` (0.08 ms), and job-pool contention with the forked flares and
billboards, which inflates the publish from about 0.5 to 0.66 ms.

Making it pay would need:
- the publish to run under its own pass-scene lock instead of `api_mutex`;
- the queries to move off the traversal's shared occlusion buffers, so the
  publish can start before the traversal.

Even then, the ceiling is about 0.4 ms. That confirms the plan: only the
cross-frame Apply/Build split, with a frame-packet snapshot, is sized to the
1.2 ms gap.

## Phase 4 frame split, milestone 1: the Apply/Build boundary on one thread (2026-10-27)

The plan's item 0 splits the render thread's `Draw` into Apply(N+1) and
Build(N). Milestone 1 draws that boundary on one thread with no behaviour
change; nothing runs concurrently yet.

### What changed

- **`MyVulkanRender.Draw`** is now `ApplyFrame` then `BuildFrame`, run back to
  back.
  - **Apply:** message intake, clipmaps, packet collection start, particles
    and decals, then the scene update and billboard collection.
  - **Build:** world resolution, guest scene callback, shadow fit, voxel plan,
    the scene's Build part, voxels and flares, lighting setup, publication,
    `QueueWorld`, frame slot, sprites.
- **`FramePacket`** (new) carries the renderer inputs Build reads: camera,
  view and projections, voxel draw frustum, light, texture multipliers,
  render and device settings, back-buffer size, gameplay frame, draw time,
  animation delta and the particle-camera reset. Build never reads the
  renderer fields that messages rewrite.
- **`VulkanScene.Draw`** is now `UpdateScene` (Apply), `LatchFrame` (the
  hand-off) and `BuildScene` (Build).
  - Apply writes `Next`: render and shadow settings, probe and shadow
    distances, highlight sepia, external depth revision, frustum, LOD field
    of view, size projection, group near plane.
  - `LatchFrame` copies `Next` into the fields Build already reads, so the
    Build code keeps its names.
  - Light selection, which runs in Apply, now reads `Next`.
- **`Environment` stays Apply-side.** The game's RenderDirect components (for
  example `MyThrustFlameAnimator`) read `Owner.Scene.Environment` during the
  actor update. Build reads a latched copy, `FrameView`, at about 45 sites in
  18 files.
- **Light frames are double-buffered** (`LightFrame`: spots, spot owners,
  excluded actors, spot redraw masks, light and spot packets).
  - Apply fills one in `SelectFrameLights` (the old `SubmitLights` without
    its native calls) and in `PrepareExcludedActors`.
  - `LatchFrame` swaps the two.
  - Build submits the latched frame (`CommitLights`, the `set_lights` and
    `set_spots` calls, now in Build's prologue) and adds its own spot marks.
  - Code that runs on both sides picks its frame with a thread-static
    `building` flag: `MarkSpots`, and billboard misting through `SideCamera`.
- **The skybox moves to Build.**
- **Billboards are collected at the end of Apply.**
  - The collection copies the billboards, the persistent billboards and the
    view-projection matrices, so Build never reads the game's read buffer.
  - Build first appends what was generated since the collection (flare
    glare), so the billboard order is unchanged.
- **One visible ordering change:** guest `RaiseScene` callbacks now see this
  frame's updated transforms; before, they ran before the scene update. No
  guest runs in the gates below.
- **Gate toggle:** `SE_VULKAN_APPLY_BUILD_ORDER=0`, or the scene property
  `ApplyBuildOrder`, runs the scene update and billboard collection at their
  old place inside Build.

### Gates

- **Same-process frozen-light lane** (`-Property ApplyBuildOrder`, old,
  new, old), run `Vulkan-9b489010…`:
  - `packets.sig` is `BEE2C5EBCCAC7D61` (2,126 packets) in all three phases;
  - every cascade, spot, probe, main-frame and probe-frame artifact is
    byte-identical;
  - view noise is 303 pixels against a 370-pixel return floor;
  - the billboard signature differs between control and control-return as
    well, so it varies over time and is not evidence.
- **Cross-process comparison against a pinned `main` build is not a usable
  gate.** Two runs of the same `main` plugin (`Vulkan-f5c4bd8a…`,
  `Vulkan-73c64c9e…`) differed in packet count (8,419 vs 8,400) and in 96 of
  150 probe, spot and cascade maps. Same-process lanes stay the only parity
  evidence.
- **GPU suites under validation:**
  - NativePass 126 PASS;
  - SceneSubmission 267 PASS, zero core and synchronization errors;
  - ShadowOcclusion 36;
  - Probe 427;
  - WorldPass 542/542;
  - Interop 16;
  - HudBillboard with 0 validation errors;
  - BillboardPacket 43.
- **Validation gameplay run** `Vulkan-5032f157…`: 0 errors.
- **Test updates:**
  - SceneSubmission's flare and ray-scene checks call `SetCamera` and then a
    Build method directly, so they now latch first;
  - the light-selection checks read `buildLights` by reflection;
  - the CreativeFixture lighting diagnostics patch `BuildScene` instead of
    `Draw`;
  - BillboardPacketTests' scene stub gains `FrameView` and `SideCamera`.

### Left for later milestones

These are found but not yet split. Each is still shared between the two sides,
which is harmless on one thread:
- **Milestone 2 (message-owned scene state Build reads):**
  - actor fields and the game's `MyActor` objects;
  - `colorizeHsv`, bloom emissiveness, skybox path and orientation,
    highlights, `actorLodSettings`;
  - `FrameCounter` (static on `MyScene`);
  - Build's calls to `MyActor.CalculateCameraDistanceSquared`
    (`VulkanActorLods.cs`, `VulkanActorSizeCulling.cs`), which read the
    Apply-side `Environment`;
  - message-time `RemoveNativePassActor` and `Clear()`, which touch Build
    state.
- **Milestone 3 (emission):** `BeginPacketCollection`, `backend.CameraPosition`
  and particle and decal emission into the shared packet lists; the sprite
  `main` list.
- **Milestone 4 (subsystems):**
  - postprocess, probe and shadow settings objects;
  - voxels (`voxels.Update` still runs in Apply);
  - environment;
  - texture cache owner (the skybox and Build-side texture reads).
- **Timing:** not measured. The order change moves the voxel plan-ahead
  start after the scene update. It is measured with the thread split, not
  per milestone.

## Phase 4 frame split, milestone 2, slice 1: actor frames (2026-10-27)

Milestone 2 makes Build stop reading state that Apply mutates. It is larger
than one change. Build reaches into:
- the game's `MyActor` objects, at about 45 sites;
- the shared `actors` dictionary, at about 40;
- the candidate tree and the occlusion-group structures;
- most `ActorData` fields.

It is done in slices, each gated by a verify mode and the same-process lane.

### Slice 1: what changed

- **`ActorFrame`** (new, `VulkanActorFrames.cs`): a per-actor copy of the game
  actor state Build reads.
  - Fields: world matrix (with its `WorldMatrixIndex` and forward scale), local
    box, has-parent, resolved ancestor visibility, destroyed, and the scene's
    `Bounds`.
  - Two per actor, one per frame parity. The end of `UpdateScene` refreshes
    the Apply parity, and `LatchFrame` hands it to Build.
  - Build also gets the inverse world matrix (cached per frame) and the
    game's parented camera distance from the copy.
  - The game's `WorldMatrix` and `WorldMatrixInv` getters compute lazily, so
    even reading them from Build would be a write.
- **Build reads only the copy** for every scene actor: traversal, candidate
  planning (parallel workers), actor LODs, size culling, new actor views,
  native pass inputs, fallbacks and glass, cell poses, GPU-scene inputs,
  group bounds, guest depth, guest scene API, ray-scene capture.
- **`ActorData.Id`** caches the game actor id at creation (36 sites), so Build
  never touches the `MyActor` for its id.
- **Refresh is dirty-driven.**
  - The first version refreshed every actor every frame and cost **2.1 ms**
    per frame (CpuDetail `ActorSnapshot`): visibility moved every frame, so
    every ancestor chain was re-resolved.
  - Transform, geometry, visibility and parent changes (descendants included)
    all reach `spatialDirty`, which `RefreshSpatialIndex` drains. The
    snapshot now refreshes the actors noted there, plus creations and
    removals, in the frame they change and in the next one, so both parity
    slots catch up. Cost: **0.039 ms** per frame (orbit, CpuDetail run
    `Vulkan-7607c526…`, Draw 5.45 ms).
- **Ray-scene capture box:** it used the game's `WorldAabb`, which the game's
  updater refreshes in `UpdateProxy` a frame after the change is noted. That
  is the one field the dirty funnel misses: the first verify runs found
  23–30 thousand mismatches, all on it. The capture now uses the scene's own
  `Bounds` (the same local box transformed by the world matrix). It differs
  only for actors whose box the game aggregates from children. Only the
  SE-RT guest reads it.
- **Verify mode** `SE_VULKAN_FRAME_PACKET_VERIFY=1`: at Build start it compares
  every actor's Build frame with the live actor (world, forward scale, local
  box, parent, visibility, destroyed, bounds). The result is reported as
  "Frame packet verify" in `vulkan-status.txt`.

### Gates

- **Verify mode, product defaults, 0 mismatches on both worlds:**
  - orbit `Vulkan-9d33e916…` was rerun after the `WorldAabb` fix (59.97
    million checks);
  - interior: 57.04 million checks.
- **Same-process frozen-light lane** (`ApplyBuildOrder` old/new/old,
  `Vulkan-373dff8a…`):
  - `packets.sig` is `3D2C288D3BDF751B` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 202 pixels against a 590-pixel return floor.
- **GPU suites under validation:** SceneSubmission 267, NativePass 126,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43.
- **Validation gameplay run** `Vulkan-b9a26873…`: validation enabled, 0
  errors.
- **Test update:** SceneSubmission's ray-scene check runs the scene update
  and latch before each capture.

### Remaining slices of milestone 2

1. **Scene containers.**
   - A Build view of the `actors` dictionary, latched from an Apply change
     log.
   - The candidate tree query (`actorIndex`), which Apply moves and Build
     queries.
   - Occlusion-group membership, rebuilt in Build from live parent chains
     (`VulkanGroupOcclusion.cs:46`).
   - Static groups.
   - Message-time native removals (`RemoveNativePassActor` and `ReleaseCell`
     from `SetModel` and `Remove`), deferred to Build's prologue.
2. **`ActorData` fields Build reads.**
   - Model, flags, instances, static instances, skin, `DynamicShadow`, colour,
     HSV, dithering.
   - Materials, which are mutated in place and need copy-on-write.
   - Build-owned LOD, key and skinned-mesh state; message resets become
     per-actor events.
3. **Message-owned scene state:** `colorizeHsv`, bloom emissiveness, skybox,
   highlights, `actorLodSettings`, the `instances` dictionary, `FrameCounter`.
4. **Leftover live reads:**
   - billboard owner actors (`VulkanBillboards.cs:123`);
   - light owners in the guest scene API;
   - manual-cull group actors in probe ambient;
   - the once-a-second `CharacterStatus` diagnostic.

## Phase 4 frame split, milestone 2, slice 2: scene containers (2026-10-27)

Build now owns the scene's container state. Apply (messages and the scene
update) no longer writes any of it:
- the candidate tree (`actorIndex`) and static groups;
- the occlusion groups;
- native pass, cell and GPU ownership, with their dirty sets.

New partial `VulkanSceneHandoff.cs`.

### What changed

- **Queued Build work.** The work Apply used to do on that state is queued as
  `BuildOp`s in issue order. `BuildScene` replays it first, because it
  preceded everything else in the frame before the split. The queued work:
  - the ownership release in `SetModel` (native pass, GPU and cell);
  - `Remove`'s native release, dirty-entry removal, group removal and
    candidate proxy removal;
  - every candidate-tree update from `RefreshSpatialIndex`. Apply still
    computes the bounds and sphere; Build decides managed-index membership
    from its own ownership state and adds, moves or removes the proxy.
- **Dirty marks collect on the Apply side and fold in at `LatchFrame`:**
  - `DirtyNativePass` (pass, cell, cell-pose and GPU sets, plus the four
    binding and appearance flags, as a per-actor bit mask);
  - `DirtyGroup`;
  - group-membership dirtiness;
  - the occlusion revision counter.
- **`buildActors`:** Build's copy of the `actors` dictionary, kept by replaying
  Apply's adds and removes at the latch. It replays the same operations in
  the same order, so it enumerates in the same order. All Build reads of
  `actors` now use it: commits, queries, fallbacks, glass, candidates, guest
  depth, guest scene, ray scene, group rebuild. The once-a-second statistics
  getters still read `actors`.
- **Occlusion-group membership** is rebuilt from `ActorFrame.GroupRoot` (the
  nearest root ancestor, refreshed with the other actor-frame fields) instead
  of walking live parent chains.
- **`SyncIndexMembership`** reads the actor frame's destroyed flag and bounds.
- **Releases queued from messages skip their spot marks.** Before the split,
  a message-time release marked the light frame that `SubmitLights` reset
  right after, so those marks never had an effect.
- **`Clear()` (unload) flushes the queue and works on Build state directly.**
  The thread milestone must join Build before unload, which the plan already
  requires.
- **Verify mode** also checks that `buildActors` equals `actors` at Build start,
  and each actor's group root.

### Gates

- **GPU suites under validation, all passing:** NativePass 126,
  SceneSubmission 267, ShadowOcclusion 36, Probe 427, WorldPass 542/542,
  Interop 16, HudBillboard with 0 validation errors, BillboardPacket 43.
- **Validation gameplay run** `Vulkan-7d9b7764…`: validation enabled, 0 errors.
- **Verify mode, full length, 0 mismatches:** the actor frames, `buildActors`
  and group roots, checked every frame at Build start:
  - orbit `Vulkan-a76a2f3e…`: 53.49 million checks;
  - interior `Vulkan-ce938338…`: 57.31 million checks.

  These were rerun on an idle GPU (GPU time 2.9–3.0 ms). The first batch
  overlapped another game holding the GPU (97%, 242 W), so its verify runs
  (`Vulkan-2cbcfb69…`, `Vulkan-4656d4c2…`) covered only about 130 frames and
  1.5 million checks each. They also showed 0 mismatches, and that batch's
  timings are invalid.
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-2a212887…`):
  - `packets.sig` is `4C1EF0BB63CF6507` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 279 pixels against a 187-pixel return floor, with max delta
    24 against 46.

### Known differences from the old order (edge cases, not semantic)

- **Candidate-tree history after a removal or model change.** Before the
  split, releasing a pass-owned actor could add its proxy and then remove it
  in the same message. The queued replay uses the latched bounds, so the
  tree's internal shape can differ after such an event, which can change
  candidate enumeration order.
- **Dirty-set order.** The dirty sets lose the slot reuse that message-time
  removals caused, so commit order can differ after removals.
- Neither changes what is drawn. The steady-state lane shows the output is
  identical.

### Remaining slices of milestone 2

- Per-actor fields Build reads (slice 3).
- Message-owned scene state (slice 4).
- Leftover live reads (slice 5).
- **New for slice 3:** `ReleaseDynamicMeshes` in `Remove` and `SetModel` still
  removes skinned-mesh resources during Apply, while Build may draw them.
- **Toggle-only paths:** the `IndexExcludesOwnedActors` and
  `StockLodBoundsEnabled` setters still touch Build state when the lane flips
  them. They run between frames, so this is harmless.

## Phase 4 frame split, milestone 2, slice 3: per-actor fields (2026-10-27)

### What changed

New partial `VulkanActorFields.cs`.
- **Two copies of the per-actor fields.** `ActorData`'s own fields are now
  Build's view of the fields that messages and the scene update write.
  Apply writes a second copy, `ActorData.A`, through `W()`, which marks the
  actor. `LatchFrame` copies `A` into the Build fields (`FoldActorFields`).
  - The fields: model, instance buffer, start and count, static instances,
    skin matrices, skeleton and indices, HSV, dithering, character,
    converted old material, track-old-identity, material revision, and the
    shadow pose (dynamic flag, centre, radius).
  - Actor `Flags` and colour are set only at creation, so they stay single.
  - `ShadowPose` and `GeometryDirty` are Apply-only.
- **Materials** are mutated in place by messages, so Build gets a clone
  (dictionary and override objects) whenever the revision or entry count
  changes.
- **Skin matrices:** each message fills a fresh array, so Build keeps the one
  it latched.
- **Resets become events applied at the latch:**
  - a model change resets the LOD, submission and old-identity state;
  - a material-flag conversion resets the stock LOD state;
  - old-identity resolution happens at the latch. `OldModel` is now
    Build-owned; Apply only requests the resolve.
- **Old-pipeline decal registration** is triggered by the game updater during
  Apply. It wrote Build caches, temporarily swapped `RenderModel` and
  resolved `OldModel`, so it now runs in Build's prologue for the actors that
  asked.
- **Skinned and deformed mesh releases** from model changes, removals and
  instance-buffer updates join the queued Build ops, because Build may still
  draw those meshes.
- **Apply-side readers use `A`:**
  - `RefreshSpatialIndex`, `NoteShadowPose`, `RefreshShadowPoses` and
    `MarkInstanceGeometry`;
  - decal bones and the removal shadow regions;
  - `KnownStockOldPipelineApply`.
- **Verify mode** also compares every actor's Build fields with `A` after the
  latch.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-985b9684…` (50.53 million
  checks) and interior `Vulkan-7122bd24…` (56.42 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-1cf11fbe…`):
  - `packets.sig` is `2C172F3DEE2C3FEC` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 479 pixels against a 425-pixel return floor, with max delta
    23 against 37.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, and now DepthGeometry 41 and
  GuestDepth 1,900.
- **Validation gameplay run** `Vulkan-b3685cb3…`: validation enabled, 0 errors.

### Test coverage found missing

- **GuestDepthGpuTests** was not in the batch. It had failed since slice 1
  ("hidden actor excluded from complete current depth sources"): it changes
  visibility by message and then calls Build methods without a frame. It now
  runs the scene update and latch first (`Latch()`), and passes 1,900
  checks. It and DepthGeometryGpuTests now join every gate batch.
- **GpuSceneLiveTests** fails at its first check, `backend.GpuSceneReady`
  ("GPU scene stock shader admission completes"), before any scene object
  exists. This is the opt-in GPU-scene path. No native code changed in these
  milestones, so it is recorded as a pre-existing failure, not investigated
  here. A pre-split baseline worktree could not compile shaders (untracked
  shader includes), so it was not confirmed there.

### Remaining slices of milestone 2

- **Slice 4, message-owned scene state:** `colorizeHsv`, bloom emissiveness,
  skybox, highlights, `actorLodSettings`, the `instances` dictionary (its
  arrays are resized and copied in place), `FrameCounter`.
- **Slice 5, leftover live reads:** billboard owner actors, light owners in the
  guest scene API, manual-cull group actors in probe ambient, the
  `CharacterStatus` diagnostic.

## Phase 4 frame split, milestone 2, slice 4: message-owned scene state (2026-10-27)

### What changed

New partial `VulkanSceneState.cs`. Messages now write Apply copies, which
`LatchFrame` folds into the fields Build already reads (`FoldSceneState`):
- **Colour-mask HSV and bloom emissiveness** (from `UpdateRenderEnvironment`
  and `UpdatePostprocessSettings`).
- **The skybox:** `Set` is deferred to the latch, so Build's `Draw` and the
  background orientation read the latched sky.
- **The new-LOD settings:** the message copies into an Apply settings object,
  and the latch copies that into Build's.
- **Highlights and overlapping highlights:** when they change, Build gets
  fresh lists. The highlight entries never change once added, so they are
  shared.
- **Instance buffers:** messages write `applyInstances`. The latch gives Build
  a snapshot of each changed buffer. Generic arrays are copied, because range
  updates write into them in place; cube arrays are shared, because each
  update builds a new one.
- **The frame counter:** `MyScene.FrameCounter` is static and the scene update
  increments it, so the occlusion and shadow-group code reads
  `BuildFrameCounter`, latched with the frame.
- **Verify mode** also compares Build's copies with the Apply state: HSV,
  bloom, highlight counts, each instance buffer's revision and arrays, and
  the frame counter.
- **Test update:** SceneSubmission's cube skinning-policy check inspects how
  messages build the buffer, so it now reads `applyInstances`.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-9ce2ca41…` (53.52 million
  checks) and interior `Vulkan-4e26c538…` (55.17 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-b3530666…`):
  - `packets.sig` is `4A4CDE387EF84D0D` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 231 pixels against a 429-pixel return floor.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900.
- **Validation gameplay run** `Vulkan-9568b593…`: validation enabled, 0 errors.

### Remaining in milestone 2

**Slice 5, leftover live reads:**
- billboard owner actors (`VulkanBillboards.cs:123`);
- light owners in the guest scene API;
- manual-cull group actors in probe ambient;
- the once-a-second `CharacterStatus` diagnostic.

## Phase 4 frame split, milestone 2, slice 5: leftover live reads; milestone 2 done (2026-10-27)

### What changed

- **Lights.** Each frame, the end of Apply copies every scene light into the
  light frame (`LightSnapshot`): owner id, destroyed, ancestor visibility,
  world matrix, light data and effective data. The flares, the guest shadow
  inputs (`CaptureGuestShadowInputs`) and the ray-scene capture now read that
  copy, latched with the frame, instead of the live light components and
  owners. Until now, Apply's message processing and particle lights mutated
  those while Build read them.
- **Probe ambient.** `GatherProbeAmbient` queries the game's cull trees and live
  light owners, so it now runs in Apply every drawn frame (0.0065 ms per
  frame, CpuDetail run `Vulkan-b54a6dce…`). Build's probe capture takes the
  value from the frame packet. It used to gather only on capture frames,
  from the same frame's state.
- **Billboard parents.** Parented billboards are resolved into world space when
  Apply collects them, the same way emission and the sort key did before.
  A billboard whose parent is missing or hidden keeps its place in the
  (unstable) sort and is skipped at emission. Billboards generated during
  Build (flare glare) have no parent.
- **`CharacterStatus`,** the once-a-second diagnostic on the render thread, now
  reads Apply's fields and frustum. Its skinned-bounds read is a Build field,
  which is acceptable for a diagnostic.
- **Test update.** SceneSubmission's flare checks create and destroy flare
  lights and expect the change at once, so each direct flare call now runs
  the scene update and latch first (`DrawFlaresNow`).

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-ed42b3e8…` (63.76 million
  checks) and interior `Vulkan-a1430e52…` (64.36 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-33385296…`):
  - `packets.sig` is `5E5DB5B6802AA664` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 400 pixels against a 381-pixel return floor, with max delta
    30 against 31.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900.
- **Validation gameplay run** `Vulkan-597e8b36…`: validation enabled, 0 errors.

### Milestone 2 summary

Build now reads only state it owns or state latched from Apply:
- **Actor frames:** transform, visibility, bounds, parent, group root
  (slice 1).
- **Scene containers:** the candidate tree, groups, native ownership and dirty
  sets, plus a replayed actors view (slice 2).
- **Per-actor fields:** with materials copy-on-write and LOD resets as latch
  events (slice 3).
- **Message-owned scene state:** HSV, bloom, skybox, LOD settings,
  highlights, instance buffers, frame counter (slice 4).
- **Lights, probe ambient and billboard parents** (slice 5).

Work Apply used to do on Build state is queued and replayed at the start of
Build. Verify mode checks all of this every frame.

### Still shared, assigned to later milestones

- **Milestone 3 (emission):**
  - `NativeBackend` packet lists, scopes and batches, written by particles and
    decals in Apply and by Build;
  - `backend.CameraPosition`;
  - the sprite `main` list;
  - the visible-objects set.
- **Milestone 4 (subsystems):**
  - postprocess, probe and shadow settings objects;
  - voxels, whose clipmap update still runs in Apply;
  - the environment;
  - the texture cache owner thread (Build's texture reads);
  - profilers.
- **Milestone 5 (native):**
  - the frame gate;
  - the deferred-queue ordering assert;
  - the error string.
- **Toggle-only paths:** the lane property setters still touch Build state
  between frames.

## Phase 4 frame split, milestone 3: per-frame emission and outputs (2026-10-27)

The plan expected a double-buffered `FrameEmission` object in `NativeBackend`.
Mapping the writers showed Apply touched the backend's per-frame emission
state in only three ways:
- the packet-collection start (`BeginPacketCollection`, `CameraPosition`) at
  the end of Apply;
- particles, through native `QueueParticles` and the atlas build;
- decals, appending to `backend.DecalPackets`.

Everything else that emits runs in Build. So instead of a backend-wide
double buffer, the packet-collection start moved into Build, and particles
and decals produce their output on the Apply side for Build to submit. The
backend's packet lists, scopes and batches are now touched only by Build.

### What changed

- **Packet collection:** `BeginPacketCollection` and `backend.CameraPosition`
  run at the start of `BuildFrame`.
- **Particles:** `VulkanParticles.Draw` still simulates and gathers in Apply,
  because it drives the game's static particle manager. Its native
  submission is held and handed to Build by `LatchQueue`, and `SubmitQueued`
  issues it right after packet collection starts. The submission is the
  atlas rebuild and a copy of the emitter array, which the stock manager
  reuses.
- **Decals:** `VulkanDecals.Draw` writes its own list. `LatchPackets` hands it
  to Build, and `SubmitPackets` adds it to `backend.DecalPackets` after the
  clear. The 16,384-packet cap counts the same list.
- **Sprites:** the main batch and the viewport scale are double-buffered.
  Messages and the renderer's stats overlay fill the Apply batch, which
  `LatchMain` hands over, and `MainPacketsInto` blits the latched batch with
  the latched scale.
  - The stats overlay moved to the end of Apply; it reads only the profiler
    and the game's stats.
  - `MainPackets`, used by tests and direct callers, reads the Apply batch.
- **Visible objects:** Build writes into a plugin set, `frameVisible`, and
  `Draw` copies it into the game's `VisibleObjects.Write` after Build, before
  `AfterRender` commits it. With the thread split, this copy is the hand-off
  point.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-7a6962ce…` (48.42 million
  checks) and interior `Vulkan-3980ef63…` (51.65 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-1868a81e…`):
  - `packets.sig` is `41D0F8FA5D831847` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 555 pixels against a 364-pixel return floor, with max delta
    40 against 36. That is in the range earlier slices showed.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900. Run separately for this milestone: TransparencyGpuTests 216 with zero
  validation errors, SpriteGpuTests passing (installed-font evidence), and
  MediaTests 34.
- **Validation gameplay run** `Vulkan-2ed0b15f…`: validation enabled, 0 errors.

### Pre-existing failures, not caused by the split

- **StaticDecalGpuTests** fails its first check,
  `backend.WorldLayouts.Count==54`, a fixed pipeline count asserted during
  native setup before any managed scene code runs.
- **SpritePacketTests** does not compile (`UploadWorldCubeMips`), as already
  noted on main.
- **GpuSceneLiveTests** fails at GPU-scene shader admission (recorded under
  slice 3).

### Still shared after milestone 3

- **Milestone 4 (subsystems):**
  - postprocess, probe and shadow settings objects;
  - voxels (clipmap update in Apply; voxels emit in Build);
  - the environment;
  - the texture cache owner thread;
  - the media `RemoveVideoTexture`, which removes from the Apply batch while
    a latched batch may still reference the texture;
  - profilers.
- **Milestone 5 (native):**
  - the frame gate for Apply's native calls during a Build frame (texture
    uploads, LCD offscreen renders, resource removal);
  - the deferred-queue assert;
  - the error string.

## Phase 4 frame split, milestone 4, slice 1: probe, shadow and postprocess settings (2026-10-27)

Milestone 4 gives each subsystem a single owner. It runs in slices:
1. settings (this slice);
2. environment;
3. voxels;
4. texture cache and media;
5. profilers.

### What changed

The public settings fields stay as Build's view. Build reads them, and tests
and the lane harness set them directly. Messages now write `Applied` copies
during Apply, and the latch at the hand-off copies a changed one in:
- **`VulkanProbes`:** settings, resolution and texture multipliers. Reset and
  unload requests are OR-ed in, because Build clears `reset` itself.
- **`VulkanShadows`:** the settings object (copied only when a message changed
  it, so the harness's direct `Settings.Data` write still holds) and the debug
  shadows override.
- **`VulkanPostprocess`:** postprocess, SSAO and HBAO settings, the AO debug
  override, and the unload reset.
- **Apply's own readers** now read the `Applied` copies: the scene's probe
  and shadow draw distances, the shadow settings, the highlight sepia, and
  the probe ambient gather.

A first attempt renamed Build's reads to new latched fields instead. That
would have meant editing about 35 test sites, which build these objects and
call `Configure` directly, so it was reverted for this layout. The only test
change kept is a latch after PostprocessGpuTests' debug-override messages.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-6e6dec11…` (56.95 million
  checks) and interior `Vulkan-7f0f31c6…` (56.73 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-95edf0b1…`):
  - `packets.sig` is `6C98419263F8FCF1` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 403 pixels against a 488-pixel return floor.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900. PostprocessGpuTests (run separately): 239 with zero validation
  errors.
- **Validation gameplay run** `Vulkan-29f2d4cf…`: validation enabled, 0 errors.

### Remaining in milestone 4

2. **Environment:** atmospheres, clouds and fog are written by messages and
   drawn in lighting setup.
3. **Voxels:**
   - clipmap update and voxel messages run in Apply, while Build plans and
     draws;
   - `RequireOwner` pins the first thread that calls it.
4. **Texture cache and media:**
   - owner-thread loads;
   - removals and unloads while a frame references them;
   - video texture removal.
5. **Profilers:** `FrameProfiler` statics, and a Build lane for
   `RendererCpuProfiler`.

## Phase 4 frame split, milestone 4, slice 2: the environment (2026-10-27)

### What changed

`VulkanEnvironment` (atmospheres, clouds, fog, planet settings) processes
messages in Apply and draws in Build's lighting setup.

- **Apply validates and queues.** It checks geometry and duplicate IDs
  against its own ID sets. It then queues each change in issue order, with
  the Apply time and gameplay frame it arrived at: create, update and remove
  atmosphere; enable; create and remove cloud.
- **The latch replays the queue** onto the dictionaries `Draw` reads, and
  copies the fog, planet settings and transition time if a message changed
  them. Immediate atmosphere removals, which free a native resource, happen
  at the latch.
- **Build owns `Time`, `GameplayFrame` and the faded-out removals.** The
  renderer sets `Time` and `GameplayFrame` from the frame packet before
  `Draw`. Apply's timestamps use `ApplyTime` and `ApplyGameplayFrame`.
- **Edge-case differences:**
  - an ID reused while its predecessor is still fading out now replaces it,
    where before it threw a duplicate-ID error;
  - an update or a second removal for an atmosphere that is already fading
    is ignored.
- **Test update:** EnvironmentGpuTests latches in its `ManagedFrame` helper,
  since it sends messages and then draws directly. It passes 50 checks with
  zero validation errors.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-3ec4b928…` (52.85 million
  checks) and interior `Vulkan-e4dd8a7c…` (49.70 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-24c0ab04…`):
  - `packets.sig` is `4B95981B5B9FF584` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 524 pixels against a 548-pixel return floor.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900, Environment 50.
- **Validation gameplay run** `Vulkan-43e1c543…`: validation enabled, 0 errors.

### Remaining in milestone 4

3. Voxels.
4. Texture cache and media.
5. Profilers.

## Phase 4 frame split, milestone 4, slice 3: voxels (2026-10-27)

### What changed

Voxels (actors, clipmap cells and their native meshes) are Build state now.
The game's clipmap callbacks create, delete and show cells and swap meshes,
which frees the old native meshes. Build plans (on a worker), emits and draws
from those same cells.

- **Voxel messages are queued.** Create, remove, matrix and dither, and
  material create, update and preload run in Apply: `Process` validates them
  against an Apply ID set, copies the payload and queues it in issue order.
  `FlushMessages` replays the queue on the owner at the start of `Update`,
  `BeginPlan` and `Draw`. The dispatch return values (handled or not) still
  come from the Apply ID set.
- **The clipmap update runs at the start of Build,** after particles and
  decals are submitted, with the camera inputs carried in the frame packet.
  When no frame is built it runs on the render thread, because the loading
  screen only advances inside `Clipmap.Update`.
- **The external depth revision** (voxel mesh and LOD changes, which
  invalidate occlusion) is set in Build right after the update, so it
  matches what Apply used to read.
- **`BindOwnerThread`** hands the owner check to whichever thread runs the
  update, including unload's `Clear`.
- **Test updates:**
  - VoxelGpuTests and GuestDepthGpuTests call `FlushMessages()` after
    messages whose effect they use straight away (clipmap actor binding,
    unload).
  - VoxelGpuTests' first check asserted 54 world pipelines, a count from
    before the distance-fade and voxel variants. It now asserts 620, the
    count WorldPassGpuTests asserts after the same setup. It then passes 51
    checks under validation, including the voxel actor binding, the clipmap
    update and unload.
  - StaticDecalGpuTests has the same stale 54 and is not updated here.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-41ce797f…` (53.16 million
  checks) and interior `Vulkan-ff0c00fc…` (52.69 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-f07e5df2…`):
  - `packets.sig` is `A80773F0EFB164E0` (2,126 packets) in all three phases;
  - every other artifact is byte-identical;
  - view noise is 288 pixels against a 164-pixel return floor, with max delta
    21 against 21.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900, Voxel 51.
- **Validation gameplay run** `Vulkan-f7d7a64a…`: validation enabled, 0 errors.

### The timing run

The single CpuDetail run of this batch (`Vulkan-6f8c14b6…`) drew an 8.69 ms
interval. The previous two batches drew 7.01 ms (slice 2) and 6.01 ms
(slice 1).

| Phase (wall ms per frame) | Slice 1 | Slice 2 | Slice 3 |
|---|---|---|---|
| Draw | 5.40 | 6.28 | 7.76 |
| Messages | 0.365 | 0.448 | 0.514 |
| QueueWorld | 0.648 | 0.771 | 1.028 |
| Clipmaps (moved in this slice) | 0.110 | 0.113 | 0.086 |

The slowdown is close to uniform across phases these slices never touched,
and the phase this slice moved got cheaper. That points to machine state
(load, clocks, temperature), not the code. Single unpaired runs cannot
settle it; a paired NP run against the pre-split commit would.

### Remaining in milestone 4

4. Texture cache and media.
5. Profilers.

## Phase 4 frame split, milestone 4, slices 4 and 5: texture cache, media and profilers; milestone 4 done (2026-10-27)

### What changed

- **Texture cache** (`VulkanTextureCache`):
  - The cube, world-cube and probe-path maps and the removed-generated map
    are concurrent.
  - On the owner thread nothing changes.
  - Off the owner, a cube, world cube, probe cube or neutral normal or
    extensions miss queues a deduplicated owner request and returns 0 (or
    null) for that frame, the same contract as an ordinary texture miss.
    `DrainPendingLoads` serves the requests on the owner and `Clear` drops
    them.
  - The removed-generated lookup now runs on every thread. A removal keeps
    the handle and uploads a 1×1 placeholder, so a reader never sees a freed
    handle.
- **Media:** `Close` drops the video from the Apply sprite batch at once, but
  the native texture is removed at the hand-off (`media.FlushRemovals()`
  after the latches). By then the latched batch Build draws no longer names
  it. Shutdown flushes the remaining removals.
- **`FrameProfiler`:**
  - Lap marks are per-thread. A thread's first lap in a frame starts its mark
    and adds nothing.
  - Updates to the shared `Pending` totals take a lock.
  - On one thread, attribution is unchanged.
  - Separating Build(N) laps from Apply(N+1) laps needs a per-frame buffer,
    which comes with the build thread (milestone 6).
- **`RendererCpuProfiler`:** no code change. Scopes still record only on the
  thread that began the frame, and Build runs there. Milestone 6 adds a
  Build lane.
- **Tests:** MediaTests' fake device implements `FlushRemovals`.
  SpriteGpuTests joins the batch for the sprite and texture path.

### Gates

- **Verify mode, 0 mismatches:** orbit `Vulkan-e5b34e32…` (47.69 million
  checks) and interior `Vulkan-808f796d…` (47.53 million).
- **Same-process lane** (`ApplyBuildOrder` old/new/old, `Vulkan-57eb32f5…`):
  `packets.sig` is identical in all three phases.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 16, HudBillboard
  with 0 validation errors, BillboardPacket 43, DepthGeometry 41, GuestDepth
  1,900, Voxel 51, Sprite 29.
- **CPU tests:** MediaTests 34 checks, FrameProfilerTests pass.
  RendererCpuProfilerTests does not compile (missing `VRageRender`
  reference). This slice only touched comments in that profiler.
- **Validation gameplay run** `Vulkan-b7202a39…`: validation enabled, 0 errors.

### The timing run

The CpuDetail run (`Vulkan-aceb5282…`) drew a 7.13 ms interval.

| Phase (wall ms per frame) | Slice 2 | Slice 3 | Slices 4–5 |
|---|---|---|---|
| Draw | 6.28 | 7.76 | 6.37 |
| Messages | 0.448 | 0.514 | 0.455 |
| QueueWorld | 0.771 | 1.028 | 0.799 |
| Clipmaps | 0.113 | 0.086 | 0.071 |

The numbers are back at slice 2's level, and slices 4 and 5 add no work to
these phases. That supports the earlier reading: slice 3's 8.69 ms was
machine state, not the voxel change. Single CpuDetail runs remain unpaired;
the milestone 7 benchmark decides timing.

### Next

Milestone 5, the native frame gate: calls from a thread other than the open
frame's owner wait instead of throwing, a debug assert that Apply issues no
scene-mutating native calls, a guarded `error`, and the sort kept on the
Build thread.

## Phase 4 frame split, milestone 5: the native frame gate, and the single-thread cost of milestones 1-4 (2026-10-28)

### What changed (`Native/src/renderer.cpp`)

- **Frame owner.** `begin_frame` records the calling thread as the frame
  owner; `end_frame` clears it.
- **Frame gate** (`SE_VULKAN_FRAME_GATE=1`, also on with
  `SE_VULKAN_FRAME_SPLIT=1`): in `call` and `call_scene`, a call from another
  thread while a frame is open waits on `record_cv`. It resumes once no frame
  is open and no pipelined recording is pending or running, instead of meeting
  the open frame and throwing on its `recording` checks. Waiters are woken
  when the owner's call returns.
  - Off by default: on one thread it cannot change anything.
  - With `SE_VULKAN_PIPELINE_REPORT` the report carries:
    - `GateOffOwnerCalls`: off-owner calls that met an open frame, counted
      even with the gate off;
    - `GateWaits` and `GateWaitUs`.
- **Scene-call census and assert.** `call_scene` and `call_deferrable_scene`
  count calls from a thread other than the last frame owner
  (`OffOwnerSceneCalls`). They also count those made while a frame or
  recording was in flight (`OffOwnerScenePending`), where an interleaved call
  would reorder the deferred queue. `SE_VULKAN_SCENE_CALL_ASSERT=1` prints the
  first one to stderr.
- **Per-thread last error.** Wrappers record failures only in the
  thread-local `global_error`, and `sevk_last_error` returns the calling
  thread's last failure. The managed side reads it on the failing thread, so
  its text is unchanged.
  - The per-renderer string could be rewritten by another thread while a
    caller copied it; it is no longer written.
- **Sort.** No change: `QueueWorld` and its `thread_local` sort reuse run in
  Build, on one thread.
- **InteropTests** gains an off-owner upload during an open frame:
  - without the gate it fails with the calling thread's own error
    (18 checks);
  - with `SE_VULKAN_FRAME_GATE=1` it waits until the frame closes, then
    succeeds (19 checks).

### Gates

- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 18 (19 with the
  gate), HudBillboard with 0 validation errors, BillboardPacket 43,
  DepthGeometry 41, GuestDepth 1,900, Voxel 51, Sprite 29.
- **Validation gameplay run** `Vulkan-3d40dbae…`: validation enabled, 0
  errors.
- **Census** (orbit, pipeline report): gate off `Vulkan-b6be1343…` and gate on
  `Vulkan-5012ca40…`. Both show 0 off-owner calls, 0 gate waits and 0
  off-owner scene calls.
- **Not rerun:** the verify runs and the order lane. Milestone 5 changes no
  managed code, and with one thread the native changes cannot alter packets.

### The end-of-milestone benchmark: milestones 1-5 against pre-split 8588f01

**Setup:**
- The pre-split plugin is built from 8588f01 and stored in
  `TestResults/PreSplit-8588f01-binaries` (sets `PS0`, `INPS0`).
- Both sides load the same, current native DLL. Native code is unchanged
  between 8588f01 and milestone 4, so the pair measures the managed
  milestones 1-4 plus milestone 5's thread-id check.
- Interleaved pairs on a quiet box, plus one DX11 run per world.

| Run | Orbit present ms | Interior present ms |
|---|---|---|
| Current (1st) | 7.09 | 8.06 (P95 13.4, a disturbed run) |
| Pre-split (1st) | 6.54 | 5.86 |
| Pre-split (2nd) | 6.40 | 5.87 |
| Current (2nd) | 7.12 | 6.51 |
| DX11 | 4.41 | 4.68 |

**Findings:**
- Milestones 1-4 cost about **0.6 ms per frame on one thread**: orbit
  7.10 against 6.47, interior 6.51 against 5.86.
- The box ran slow overall this session: pre-split ran 6.4-6.5 ms against the
  5.3-5.5 ms measured on 2026-10-27, and DX11 4.41 against 4.27-4.47. So
  compare within the pairs only.

**Where the cost goes.** `FrameProfiler` sections, the mean of the paired
runs:

| Section (ms) | Orbit pre-split | Orbit current | Interior pre-split | Interior current (2nd) |
|---|---|---|---|---|
| Messages | 0.35 | 0.43 | 0.34 | 0.36 |
| Clipmaps (now: latch and Build prologue up to the clipmap update) | 0.07 | 0.53 | 0.07 | 0.45 |
| Particles and decals | 0.55 | 0.32 | 0.52 | 0.27 |
| Scene update | 0.72 | 0.99 | 0.65 | 0.82 |
| Queue world | 2.78 | 2.61 | 2.45 | 2.58 |
| Total | 6.42 | 6.99 | 5.81 | 6.41 |

- **Scene update, +0.17 to +0.27 ms.** These are Apply's new copies: the
  actor snapshots, Applied fields, light snapshots and probe ambient.
- **Clipmaps plus particles, +0.13 to +0.23 ms.** These are the hand-off:
  `LatchFrame`, the Build-op and actor-field fold, and the sprite, particle,
  decal and settings latches. The particle and decal submit moved out of the
  particles lap into this one.
- **Messages, about +0.05 ms.** Queued op copies.
- The rest is within noise.

**What this means for the split:**
- The copies are what lets Build run on its own thread. They pay back only
  when Apply and Build overlap (milestone 6).
- From the current sections, Apply (messages, particles, scene update, the
  stats overlay) is about 1.8 ms, plus the 0.8 ms game-loop gap. Build (the
  rest, including the 0.5 ms hand-off) is about 5.2 ms on this slow box.
- Present cadence would be about max(2.6, 5.2) = 5.2 ms against 7.1 today. On
  the 2026-10-27 box state, roughly 20% faster, Build would be about 4.2 ms:
  at the 4.17 target, with no margin. Build-side trims (plan item 1b, parallel
  traversal) remain necessary after the split.
- Before milestone 6, two cheap pieces of work would also help single-thread
  mode:
  - trim the hand-off (dirty-only folds; skipping latches whose inputs did not
    change);
  - trim the actor snapshot.
- A `CpuDetail` pair (current against pre-split) would split the 0.6 ms by
  phase before choosing.

### Next

Milestone 6, the build thread behind `SE_VULKAN_FRAME_SPLIT=1`, or first the
hand-off and snapshot trims above.

## Phase 4 frame split: pricing and trimming the single-thread cost (2026-10-28)

### Pricing it

New CpuDetail phases cover work the split added that no phase measured:
- `Handoff`, split into:
  - `HandoffFields`: `SyncBuildActors` and `FoldActorFields`;
  - `HandoffSceneState`;
  - `HandoffDirty`: the settings latch and the dirty-set folds;
  - `HandoffLatches`: the subsystem latches;
- `LightSnapshot`;
- `BuildPrologue`, split into `PrologueCollection` (`BeginPacketCollection`)
  and `PrologueSubmit` (the particle and decal submit);
- `BuildOps` (`FlushBuildOps`).

First same-session CpuDetail pair, current `Vulkan-12613c83…` against
pre-split `Vulkan-c7f79605…` (run 1 of each; run 3 failed and run 4 was
disturbed):
- Draw: 7.57 against 7.08 ms.
- The split's phases: hand-off 0.183, light snapshot 0.150, actor snapshot
  0.087, Build prologue 0.257.
- Native waits are not the cause: 1,509 pipelined waits in 9,029 frames,
  nearly all texture uploads.
- Allocation is unchanged (about 300 KB per frame).

**Cross-session CpuDetail runs cannot attribute this.** Comparing with the
2026-10-27 runs moved whole frames by 1.5 ms.

### The trims

- **Light snapshot.**
  - `VulkanLightComponent.DataRevision` is bumped in `UpdateData`, the only
    writer of `Data` and `EffectiveData`; the decompiled `MyLightComponent`
    never writes `m_data`.
  - The snapshot copies the two `UpdateRenderLightData` structs only when the
    revision moved.
  - It copies the owner's world matrix only when `WorldMatrixIndex` moved.
- **Actor snapshot.** An actor noted last frame but not this one used to be
  recomputed: world matrix, visibility and group-root parent walks. Now
  `ActorFrame.CopyFrom` copies the other parity slot, which was refreshed
  from the same, unchanged state one frame earlier.
- **Dirty-set folds.** `FoldDirty` swaps Build's and Apply's native-pass,
  cell, cell-pose and GPU dirty sets when Build's is empty, instead of
  `UnionWith` plus `Clear`. Apply sets only ever Add and Clear, so the
  contents and the iteration order are identical.

### Results

After the first two trims (single CpuDetail run `Vulkan-ed583bd9…`, verify
`Vulkan-8695fa3f…` with 0 mismatches in 42.9 million checks):
- light snapshot 0.150 → 0.061;
- actor snapshot 0.087 → 0.061;
- hand-off 0.145 (fields 0.048, dirty sets and settings 0.085);
- prologue 0.227 (`BeginPacketCollection` 0.196).

**`BeginPacketCollection` is not new.** At `8588f01` it ran at the same point
of Draw outside any phase.

Gate batch with all three trims:
- **Verify:** 0 mismatches. Orbit `Vulkan-f308b16d…` 32.9 million checks,
  interior `Vulkan-d5146466…` 44.8 million.
- **Lane** `Vulkan-65d1bd48…`: `packets.sig` identical in all three phases.
- **GPU suites under validation:** NativePass 126, SceneSubmission 267,
  ShadowOcclusion 36, Probe 427, WorldPass 542/542, Interop 18 (19 with the
  gate), HudBillboard 0 errors, BillboardPacket 43, DepthGeometry 41,
  GuestDepth 1,900, Voxel 51, Sprite 29.
- **VAL** `Vulkan-2e44aabc…`: 0 errors.
- **CpuDetail pair.** The box ran about 13% slower than for the first pair.

  | Run | Present ms | Draw ms |
  |---|---|---|
  | Current `Vulkan-6106dc10…` | 9.63 | 8.57 |
  | Pre-split `Vulkan-5bb37792…` | 9.12 | 8.09 |
  | Pre-split `Vulkan-fd96fedc…` (disturbed, P95 16.7) | 10.88 | 9.60 |
  | Current `Vulkan-f32771e1…` | 9.76 | 8.69 |

  The split's own phases in current run 1:

  | Phase | ms/frame |
  |---|---|
  | `Handoff` | 0.163: fields 0.075, dirty sets and settings 0.074, latches 0.002 |
  | `ActorSnapshot` | 0.096 |
  | `LightSnapshot` | 0.079 |
  | `PrologueSubmit` (moved from `ParticleDraw`, -0.073) | 0.036 |
  | `BuildOps` | 0.019 |

  Apply-side marks: shadow poses +0.045, spatial index +0.024, messages
  +0.023. Occlusion is 0.11 ms cheaper, because its bounds refresh moved to
  the incremental Build-side path.

**Reading:**
- The Draw gap is still about 0.49 ms: 6% of Draw here, 7% in the first pair.
  Normalized for the slower box, the trims recovered about 0.05–0.1 ms, which
  is inside single-pair noise.
- The light snapshot is the clear win: 0.150 → 0.079 on a slower box.
- What remains is the copy cost of the hand-off itself. It is spread over a
  dozen sites, none above 0.1 ms.
- Milestone 6 moves about 2.3 ms of Apply off Build's critical path, against
  this 0.5 ms. Further one-thread trimming is low-value unless the split
  stays off by default.

### Next

Milestone 6, the build thread behind `SE_VULKAN_FRAME_SPLIT=1`.

## Phase 4 frame split, milestone 6: the build thread (2026-10-28)

The plan's milestone 6 entry has the as-built description: `FrameBuildThread`,
`DrawSplit`, the joins, upload-batch ownership, the races closed, profiler
lanes and Anomaly documentation. This section has the measurements.

### First split runs: five more races

Every split run crashed in its first frames until five split-only races were
closed:
- **Voxel textures:** "Voxel DDS missing". Synchronous loads now run on the
  build thread as the texture cache's second loader thread.
- **The build-op queue:** "Collection was modified". It is now latched at the
  hand-off.
- **Billboard collection:** now double-buffered.
- **The model cache:** now locked, with atomic mesh ids.
- **`OldResources`:** now locked.

All five behave identically on one thread. After the fixes the single-thread
gates pass:
- verify: 0 mismatches, orbit `Vulkan-45000d3a…` 32.9 million checks,
  interior `Vulkan-4ee0dc66…` 48.7 million;
- lane `Vulkan-ec2bee36…`: identical `packets.sig`;
- suites pass;
- VAL `Vulkan-ed4c45ce…`: 0 errors.

The split runs show no exceptions, and VAL with the split
(`Vulkan-87398742…`) has 0 validation errors.

The single-thread VAL "present rendered frame failed" (out-of-date surface)
at session unload is pre-existing: every earlier VAL run logs it twice.

### The harness's render-thread telemetry

The first census with the split (`Vulkan-69ae1b71…`) counted 3,820 gate
waits, 19.8 s in all, about 2.7 ms per frame:
- `sevk_get_present_flow_stats`: 17.7 s;
- `sevk_get_performance_stats`: 1.2 s.

These are the harness's per-frame reads in its Present prefix. Under the
split they land in Build's open frame and recording.

Production waits are small:
- LCD renders: 0.08 s;
- texture uploads: a handful;
- the once-a-second `SampleDiagnostics` at the hand-off: 0.3 s.

The harness now samples on the build thread (a `SubmitFrame` prefix) right
before the native launch. After that (`Vulkan-58c250fc…`):
- 72 gate waits, 0.36 s;
- present interval 6.28 ms, unpaired.

The off-owner scene calls in both runs are the frames the split builds on the
render thread (the harness's screenshot frames), with Build joined.

### The pair

Same session, interleaved; the box ran about 25% slow (DX11 5.50 / 5.52
against 4.41 / 4.68 earlier).

| | Orbit present ms | Interior present ms |
|---|---|---|
| Single thread | 8.30, 8.30 | 6.32, 8.38 |
| Split | 8.11, 8.07 | 8.08, 8.03 |
| DX11 | 5.50 | 5.52 |

- **Orbit:** the split is about 0.2 ms faster.
- **Interior:** unusable. The two single-thread runs straddle a change of box
  state (render-thread total 6.20 → 8.20 ms).

### Why the overlap mostly vanished

- The render thread loops with about 0.03 ms outside Draw, so under the split
  its Draw (Apply plus the join) tracks Build.
- Build(N) launches right after Build(N−1) launched the native recording of
  N−1. On one thread, Apply sat between them and absorbed about 2 ms of that
  recording; now Build's native calls wait for it.
- Pipelined waits on ordinary calls rose from 0.17 ms per frame (single
  thread, `Vulkan-b6be1343…`) to about 0.97 ms per frame (split,
  `Vulkan-58c250fc…`). Scene-call waits for the early release are not counted
  yet.
- The wall moved from the render thread to the recording thread and Build's
  wait on it.

### Next

1. Count scene-call waits for the early release, and Build-thread waits by
   export.
2. Take a same-session CpuDetail pair (single thread against split) with the
   census.
3. Then pick the remedy:
   - an earlier recording snapshot;
   - deferrable or snapshot-answered Build-prologue calls;
   - a deeper pipeline.
