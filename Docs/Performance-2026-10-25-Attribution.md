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
