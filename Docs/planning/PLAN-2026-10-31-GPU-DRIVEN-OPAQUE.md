# Plan 4: GPU-driven opaque rendering (2026-10-31)

## Goal

Free CPU and GPU time so Vulkan can run well above 240 fps, not just match
DX11 at the 240 Hz refresh.

The user's direction (2026-10-31):
- Going faster than the monitor is worthwhile. Tearing can be allowed, and
  other players have higher refresh rates.
- SE simulates at 60 UPS and caps rendering at 120 fps. SmoothFrames lifts the
  cap and interpolates motion between sim ticks.
- Building the same mechanism into Vulkan is welcome, so players don't need a
  separate plugin.

**The headline metric changes.** Present cadence at 240 Hz stays a gate, but
it can't show gains above the refresh. The new metric is headroom: each
frame chain's cost (build thread, recording thread, GPU) and the frame rate
the slowest chain allows (1000 / its ms).

## Why GPU-driven, and why now

**Today's chains** (from the attribution doc and the latest recording laps):

| Chain | Cost | Opaque-related share |
|---|---|---|
| Build thread | about 3.7 ms | Most of it: publication 1.3, `QueueWorld` 0.84, scene build and traversal about 1.6 |
| Recording thread | about 3.0 ms of laps | About 1.8 ms: NEW main record 0.25, pass stream copy 0.18, pass and scene material prepare 0.26, sort and geometry prepare 0.17, spot shadows about 0.52, probe geometry 0.14, CSM and cascades 0.11, NEW main wait 0.11 |
| GPU | about 2.5 ms | GBuffer about 0.95 |

Every CPU cut so far moved the limit to the next chain. A GPU-driven opaque
path removes work from the build and recording chains at the same time, and
the GPU has about 1.7 ms of slack at 240 fps.

**It also suits high frame rates.** At 240 fps, three of every four frames
carry no new sim data. With interpolation (milestone 6), only the camera and
a few local entities move between ticks. A GPU-resident scene then needs only
a camera upload and a GPU cull on those frames: no traversal, no publication,
no per-draw recording. The CPU path has to redo its culling every frame
because the camera changes.

## What already exists (10-11 to 10-12, paused)

A full GPU-resident opaque path was built and validated, then paused when it
lost to the CPU path. Details are in `PERFORMANCE_STRUCTURAL_PLAN.md`, from
"Approved implementation continuation — 2026-10-12" to "Pause checkpoint".

**Built:**
- Hardware admission: multi-draw indirect, draw-indirect-count, descriptor
  indexing, FP64 (`se_gpu_opaque.h`, `gpu_opaque_admission.h`).
- A resident registry of families, parts and actors with delta commits
  (`SE_VULKAN_GPU_OPAQUE_RESIDENT_DELTAS`, `_OWNED_INPUTS`).
- GPU NEW/OLD LOD selectors, per-view membership, prefix and scatter, and
  indirect arguments with GPU-written counts.
- Fixed batch recording, view batches, role streams and sparse appearance.
- A hybrid mode: GPU actors and native-CPU actors on disjoint sets.
- GPU spot-shadow redraw predicates.
- About 136,700 numerical checks, about 300 raster checks and 77 live
  ownership checks, all with zero validation errors.

**Why it lost (10-12, at about 10.7 ms frames):**
- **GPU selection cost 1.6 ms.** It does world-scale culling and LOD math in
  FP64, which a GeForce runs at 1/64 rate.
- **The CPU work it should remove stayed:**
  - native commits about 0.9 ms;
  - actor traversal about 1.1 ms (fallbacks and root queries still ran);
  - resident queue about 0.46 ms;
  - recording membership about 0.64 ms.
- Last matched pair: control 10.73 ms against GPU 11.06 ms.

**Since then:**
- The CPU path got much faster: native pass scene, cells, pipelining, the
  split and two build stages.
- The GPU path was not maintained. `GpuSceneLiveTests` failed on 10-27 with
  "GPU scene stock shader admission completes".
- With `SE_VULKAN_GPU_OPAQUE=1`, the native pass scene turns off unless
  hybrid mode is set (`VulkanNativePassScene.cs`).

## SmoothFrames under Vulkan

- **Frame cap.** Works under Vulkan. It rewrites `MyRenderThread.m_waiter`'s
  `m_targetFrequency`, which is backend-neutral.
- **Interpolation.** Does not reach Vulkan. Its core is a Harmony patch on
  `MyRender11.ProcessMessageQueue` and `MyRender11.SetupCameraMatrices`, plus
  `MyManagers.PostponedUpdate` and the DX11 particle manager. Our renderer
  never calls those.
- **Its sim-side capture is backend-neutral:** `MyCamera.UploadViewMatrixToRender`,
  `MyRenderProxy.AfterUpdate`, billboards, HUD markers, particles and lights.
- **Result.** Vulkan players with SmoothFrames get 240 fps output that still
  moves at 60 Hz. This is a parity gap regardless of this plan.

## Milestone 0 results (2026-10-31)

**Restored.** The GPU path runs again on today's pipeline (split, two build
stages, default placement, hybrid mode with every 10-12 treatment on).
- **The in-game blocker.** World load failed with "invalid immutable stock
  batches: uploads open". Under two build stages, `make_buffer` lazily opens
  an upload batch on any allocation, and `publish_gpu_scene` allocates its
  own buffers after checking that no batch is open. Publication now
  suppresses the lazy batch, so its allocations upload immediately, as they
  do on one build thread. The native message now names the failing
  condition.
- **`GpuSceneLiveTests` was not broken.** It needs `SE_VULKAN_GPU_OPAQUE_SCENE=1`
  and the validation layer path; with both it passes 180 checks with zero
  errors. The 10-27 failure was the environment.

**Correctness.**
- Core and synchronization validation in-game: zero errors.
- **Images match the CPU path.** The paused same-process comparison toggles
  `GpuOpaqueOwnershipEnabled` off, on and off, with lights, spots, frame
  constants, probes and cascades frozen (`Tests/ScenePerformanceHarness/Compare-GpuOwnership.ps1`, diffed with `Compare-LightingPhases.py`).

  | Mode | Off vs off again | Off vs GPU on | Native cells owned |
  |---|---|---|---|
  | One build thread (native readback) | 307 px | 356 px, max delta 17 | 1,797 → 0 → 1,797 |
  | Split, two stages (game screenshot) | 256 px | 134 px | 1,797 → 0 → 1,797 |

  On one build thread the cascades, spot maps and all 36 probe faces are
  byte-identical. Under the split, the reconciliation now takes the game's
  screenshot, because the render-thread readback never lands when the
  launch thread submits. GPU-on differences stay inside the off/off noise.
- **Retracted:** an earlier note here reported missing floor-grate geometry
  and a darker entrance wall. That came from comparing screenshots of two
  separate runs. Pertam's flashing light and welding sparks change the
  lighting between runs, so cross-run screenshots can't show renderer
  differences.

**Coverage** (orbit world): 6,307 GPU actors (4,497 NEW, 1,810 OLD), 339
native-CPU, 40 managed fallbacks (22 mixed techniques, 17 explicit model-LOD
fallbacks, 1 skeletal).

**Cost** (CPU-detail pair KBC/KGC, same session):

| | CPU path | GPU path |
|---|---|---|
| Present cadence | 6.14 ms | 13.53 ms |
| Build thread | 5.84 ms | 13.17 ms |
| `NativePassCommits` | 0.34 ms | 7.43 ms |
| Actor traversal | 0.59 ms | 2.17 ms |
| Native pass publish | 0.78 ms | 0.11 ms |
| Build prologue | 0.72 ms | 0.17 ms |
| Voxels and billboards | 0.46 ms | 0.13 ms |
| GPU frame | 2.70 ms | 3.49 ms |
| GPU cull compute | — | 0.36 ms (selection 0.115) |
| Recording membership (recording thread) | — | 1.24 ms |

(CPU-detail lanes run slower than plain ones; the plain KG run measured
13.07 ms against about 4.6 ms for the CPU path.)

**Why the GPU path paces at 13 ms.** Every GPU-scene export uses the plain
`call` wrapper, which waits for any in-flight recording. The CPU pass path
uses scene calls that run while an early-released recording continues. The
pipeline census (KGP) shows `sevk_gpu_scene_updates` waiting on 4,110 of
4,277 frames, 6.9 ms per frame. The build thread therefore runs after the
previous frame's recording instead of alongside it.

**What changes in the plan.**
- **Milestone 1 is deferred.** Double-precision selection costs 0.115 ms
  here, not 1.6 ms, and the whole cull 0.36 ms. Revisit only if the GPU
  becomes the limit.
- **Milestone 2 starts with the wait.** The GPU-scene commit, frame queue,
  queries and visibility calls become scene calls. The recording snapshots
  the GPU scene state it reads (slot inputs, recording membership) the way
  `rec_view` does for the native pass, then releases early.
- **The correctness gate for every milestone** is `Compare-GpuOwnership.ps1`, with
  and without `-Split`: GPU-on differences must stay within the off/off
  noise.

## Milestone 2, step 1: the recording wait (2026-10-31)

**What changed.**
- **Frame latch.** Build queues each frame's GPU-scene inputs (frame block,
  views, view map) into a pending set. `begin_frame` runs with no recording
  in flight and its slot fence waited. There it:
  - moves the pending set into the fields the recording reads;
  - copies Build's committed actor rows, appearances and part bindings into
    the slot (this used to run on the recording thread);
  - copies the actor rows the recording's batch lists read (`rec_actors`).
- **Scene calls.** The registry and per-frame GPU-scene exports (model,
  family, source, actor, updates, remove, frame, queries, visibility) are
  scene calls now, so Build runs them alongside an early-released recording.
  Their checks refuse only a frame open on the caller's own thread
  (`own_frame_open`). Publication, clear and selector handoff still wait.
- **Two build stages.**
  - Stage A(N+1) can queue before stage B(N) has latched frame N, so a
    second queue waits for that latch.
  - Rebinding or publication replaces tables an open or recording frame
    uses, so it waits until none is open, pending or recording
    (`wait_scene_idle`, rare).
  - Stage A no longer closes the upload batch before publishing; a topology
    publication flushes the lazily opened batch itself.
- **Hang diagnostics.** Both new waits report their state to stderr every
  2 s they last. One validation run hung during world load before they were
  added; three validation runs since completed and none reported a wait.

**Gates.** All pass:
- the native reference fixture: 136,700 checks;
- `GpuSceneLiveTests`: 180 checks;
- in-game validation under the split: zero errors;
- `Compare-GpuOwnership.ps1`, with and without `-Split`: GPU-on differences
  within the off/off noise; cascades and spot maps identical.

**Cost** (CPU-detail runs, same pipeline):

| | CPU path | GPU before | GPU now |
|---|---|---|---|
| Present cadence | 6.14 ms | 13.53 ms | 9.03 ms |
| Build thread | 5.84 ms | 13.17 ms | 8.67 ms |
| `NativePassCommits` | 0.34 ms | 7.43 ms | 0.78 ms |
| `GpuResidentQueue` | — | 0.46 ms | 2.38 ms |
| Actor traversal | 0.59 ms | 2.17 ms | 2.55 ms |

The pipeline census shows the build thread waiting for the recording on 30
frames out of 7,057, against 4,110 of 4,277 before. The remaining plain wait
is stage B's `EndUploads` on the launch thread, as on the CPU path.

**What still keeps the GPU path behind:**
- **`GpuResidentQueue`, 2.38 ms, is mostly the latch wait.** Stage A(N+1)
  waits for `begin_frame(N)`, which comes after stage B(N)'s `EndUploads`
  wait for recording N-1. Removing it needs the recording-side copies
  double-buffered per slot, so frame N can latch at its world queue instead.
- **Actor traversal, 2.55 ms against 0.59.** The build thread frustum-tests
  every GPU-owned actor for the game's visible set. The plan's answer is the
  one-frame-late GPU visibility readback (milestone 2).

## Milestone 2, step 2: index, latch at queue time, recording (2026-10-31)

**Changes** (`c6b12de` and the commit after it):
- **Traversal.** GPU-owned actors leave the spatial index and the per-frame
  main-owner declarations, like pass-owned ones. Their main-owner orders are
  resident, set on commit. Their main-view visible set is the same frustum
  test in one flat pass (`GpuOwnedVisible`, about 1.15 ms; still a
  candidate for the GPU readback).
- **Latch at queue time.** Under two build stages, frame N's GPU-scene
  inputs latch when stage B queues its world (`capture_queued_view`):
  - into a per-submission queued frame;
  - Build's committed rows into a latched mirror.

  `begin_frame` copies the mirror into the slot, and the recording applies
  its queued frame when it starts.
- **Recording.**
  - The per-view batch lists are computed in four chunks on the pool.
  - The new-main family order is computed in four chunks on the pool.
  - Two data races are fixed: the new-main ordering and the indexed motion
    vectors read Build's live actor rows during the recording; both now read
    `rec_actors`.

**Results** (plain runs, interleaved pairs):

| | Present cadence |
|---|---|
| CPU path | 4.28–4.32 ms |
| GPU, step 1 | 9.2 ms |
| GPU, after the index change | 7.6 ms |
| GPU, after the latch change | 7.5 ms |
| GPU, after the recording changes | 6.98–7.00 ms |

The build thread no longer paces: `GpuResidentQueue` 3.36 → 0.24 ms, and
its scene work 7.2 → 3.85 ms. **The recording thread paces now:**

| Recording lap | CPU path | GPU path |
|---|---|---|
| Total | 2.87 ms | 6.25 ms |
| `new main record` (recording the entry list) | 0.23 ms | 2.25 ms |
| `prepare pass gpu` (batch lists) | — | 0.96 ms (was 1.40) |
| `spot shadows` | about 0.45 ms (all spot laps) | 0.67 ms |
| `prepare opaque reference` | — | 0.41 ms |

**The next structural step: batch granularity.** The GPU path's main view
records **5,850 entries a frame** (CPU path 1,890). Stock batches are keyed
per mesh, so each mesh is its own pipeline bind, descriptor setup and
indirect-count draw. Part offsets are already absolute within the shared
geometry buffers (`set_opaque_stock_batches`), so batches that share
pipelines and buffers can merge into one indirect draw each. That changes:
- the compute layout (per-batch argument ranges);
- draw order within a batch, which affects coplanar parts, cutout and decal
  order, and the "NEW group parts remain contiguous" contract.

So it needs its own parity gate.

**Gates** for this step:
- in-game validation: zero errors;
- `Compare-GpuOwnership.ps1 -Split`: GPU-on 83 px against 482 off/off;
- reference fixture: 136,700 checks;
- `GpuSceneLiveTests`: all modes pass except one pre-existing failure.

**Open issues found, not caused by this work:**
- **`GpuSceneLiveTests --new-group-only` fails** ("NEW group parts remain
  contiguous in channel 0: managed"). The DLL from before milestone 2
  (`3837b6a`) fails identically, with the index exclusion off too.
- **"invalid highlight draw N/0" at world unload.** It crashes about 4 s
  after the results are written, on the CPU path too (three runs on 10-29
  and 10-30). It now hits most GPU-path runs, because stage A runs alongside
  stage B more often. Results are unaffected; files written at shutdown
  (recording laps) are lost. Native validates highlight draws against
  `world.current()`, which depends on the calling thread and on whether a
  recording is running.

## Milestones

Each milestone keeps the CPU path as the default and fallback until its gate
passes. Every gate includes:
- core and synchronization validation;
- the frozen-light lane;
- the GPU suites;
- matched Pertam orbit and interior runs.

### 0. Restore and re-baseline (about 1 session)

- Fix `GpuSceneLiveTests` and the stock shader admission against today's
  shader suite.
- Run the GPU path in hybrid mode under the split with two build stages.
- Measure three numbers for three lanes (CPU-only, hybrid GPU, full GPU):
  build ms, recording ms and GPU ms, plus the coverage counts (GPU actors,
  native-CPU actors, managed fallbacks).
- **Gate:** correct images and zero validation errors. No speed gate yet;
  this is the honest starting point.

### 1. Float selection with conservative bounds (about 1 session)

- Replace FP64 culling and LOD math with camera-relative float. Positions
  are subtracted in double on the CPU once per frame for the camera and per
  commit for actors.
- Expand frustum and LOD boundaries outward by a proven roundoff envelope.
  Objects inside the envelope band fall back to an exact path: a small FP64
  pass over only those candidates, or the CPU.
- **Target:** GPU selection under 0.3 ms (from 1.6).
- **Gate:** selection identical to FP64 over the numerical suite and a
  recorded camera path, including far-orbit positions.

### 2. Take GPU-owned actors off the CPU entirely (about 2 sessions)

- GPU-owned actors skip build-thread traversal, LOD, view eligibility and
  publication. Only pose and appearance deltas are committed, and only for
  dirty actors.
- Root and group visibility for GPU-owned actors comes from the GPU.
- **The game's visible set (`VisibleObjects`).** The game uses it for LOD,
  animation and grid decisions, and it already lags a frame. It comes from a
  one-frame-late readback of GPU visibility bits into a host-cached mirror
  (never a CPU read of write-combined memory).
- **Recording.** One indirect-count draw per pipeline batch. The batch list
  only changes when the topology does, so cache it as secondary command
  buffers and re-record only when it changes.
- **Target:** build-thread opaque work under 1.0 ms and recording opaque
  work under 0.4 ms in full GPU mode.

### 3. Shadows and probes on the GPU path (about 1–2 sessions)

- Cascades, spot shadows and probe views use the same per-view GPU cull with
  view masks. Keep the existing static-spot redraw predicates.
- This removes spot shadow recording (about 0.5 ms) and probe geometry
  (0.14 ms) from the recording chain.
- **Gate:** cascade and spot depth images identical in the frozen-light
  lane, and the full cascade and light update cycles.

### 4. Coverage (about 1 session)

- Measure which actors still fall back and why: skinned, deformed,
  transparent mixed, highlight identities, light exclusions, admission
  limits.
- Move the largest group that can be moved. Skinned characters stay on the
  CPU path unless measurement says otherwise.
- **Target:** under 10% of visible opaque draws on fallback in Pertam orbit
  and interior.

### 5. Measure above the refresh (about 1 session)

- Add an opt-in presentation lane for benchmarks: mailbox where the driver
  offers it, otherwise tearing. Earlier immediate mode measured 28 ms;
  re-check that before relying on it.
- Report headroom fps alongside the 240 Hz cadence.
- **Decision point:** promote GPU-driven opaque to the default if both hold:
  - every chain beats the CPU path's slowest chain;
  - images match.

### 6. Built-in smooth frames (about 2 sessions, can run in parallel with 2–5)

- **Frame cap.** When SmoothFrames isn't loaded, Vulkan sets the render
  waiter's target frequency itself (default: monitor refresh, up to uncapped
  with tearing).
- **Interpolation.**
  - Capture camera and local-entity poses at the same sim-side points
    SmoothFrames uses.
  - In `ApplyFrame`, re-pose the camera and those entities at
    `lerp(prev, curr, alpha)`.
  - Particles, billboards, attached lights and HUD markers follow the same
    alpha.
- **Coexistence.** If SmoothFrames is loaded, defer to its capture and
  config. Offer it a Vulkan hook point (an Anomaly contract, below) rather
  than duplicating its work.
- **Gate:** motion smoothness at 240 fps, measured as distinct camera poses
  per presented frame; no added input latency beyond one frame; HUD and
  Rich HUD unaffected.

## DX11 parity and Anomaly

- **GPU-driven opaque is Vulkan-only.** DX11's frame belongs to Keen's
  `MyRender11`. Replacing its opaque path would mean cloning Keen's
  renderer, which Anomaly's rules forbid, and DX11 has no
  draw-indirect-count. This is the documented DX11 limitation.
- **Interpolation already exists on DX11** through SmoothFrames.
- **Anomaly gaps to file** (`Docs/Extensibility.md`, `wiki/Framework-gaps.md`):
  - **Frame interpolation contract.** A backend-neutral hook ("before frame
    camera setup, with sim alpha") that SmoothFrames and Vulkan can share, so
    interpolation isn't tied to `MyRender11.ProcessMessageQueue`.
  - **GPU-culled visibility.** Guests that read the per-frame visible set or
    expect per-draw callbacks for opaque actors will see GPU-owned actors
    only through the readback mirror (one frame late). The contract has to
    say so.

## Risks

- **Precision.** Float selection at planet scale is the reason FP64 was
  used. The envelope plus exact fallback must be proven, not assumed.
- **Visual regressions the unit tests missed.** On 10-12 the dark triangles
  only showed in full-scene images. Every milestone needs full-scene image
  checks.
- **The GPU budget.** At 240 fps the GPU has about 1.7 ms of slack. Added
  compute has to fit, and fewer draws should give some back.
- **Maintenance.** Two opaque paths must both stay correct until one is
  retired.

## Effort

About 8–10 sessions. Milestones 0–2 decide whether the approach works. If
milestone 2 can't get the build thread's opaque work under about 1.0 ms,
stop and re-plan.
