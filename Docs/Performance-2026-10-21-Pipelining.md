# Pipelined native recording

This continues `Performance-2026-10-21-NativeCells.md`.

Before this change, each frame on the render thread ran managed `Draw`
(about 9.7 ms instrumented) and then native `end_frame` (about 4.1 ms),
strictly one after the other. Native recording of frame N now overlaps managed
frame N+1.

## Design

Every native export takes the renderer's single `api_mutex`. Recording reads
almost all renderer state, and the next frame writes most of it. A snapshot
with double buffering would have touched most native subsystems.

Instead, native calls keep their serial order:

- `Present()` calls `sevk_end_frame_async`. That publishes a small read
  snapshot, marks a recording pending, and starts a worker. The worker takes
  `api_mutex` and runs `end_frame` exactly as before.
- Every ordinary call waits while a recording is pending or running, then
  replays the deferred queue in issue order, then runs. Unclassified exports
  therefore keep serial semantics.
- 30 void calls (`call_deferrable`) copy their arguments and queue them while
  a recording is in flight:
  - start-of-frame setters;
  - shadow quality, options, cascades, schedule and pass constants;
  - lights, spots and skyboxes;
  - occlusion, highlight, environment, foliage, billboard, decal and particle
    queues;
  - native pass actor and cell pose commits;
  - flare, billboard projection, probe atmosphere, postprocess and HBAO
    setters.

  Glass draws are deep-copied, because they carry constant and instance
  pointers. The legacy `queue_world` defers only its empty call.
- Seven reads (`call_snapshot`) answer from the launch snapshot while
  recording: work stats, resolution stats, resolve render resolution, shadow
  feedback, shadow cache mask, occlusion results and flare results.
  - The cache mask is deterministic render state. It is predicted with the
    same rule `render_world` applies: a pending resolution change clears it,
    then this frame's scheduled updates are added. The worker checks the
    prediction after every recording.
  - Unscheduled shadows, an active upscaler, or an oversized result fall back
    to waiting.
  - Occlusion, flare, usage and timing results come from GPU-completed frames
    and were already timing-dependent.
- Recording never calls back into managed code; the only callback is surface
  creation. `sevk_destroy` waits for an in-flight recording.

## Measuring it

`CpuMeanMs` times the render thread from Draw to Present. It no longer
includes recording, so it cannot judge throughput on its own. `bench-sets` now
also reports `PresentMs`, the mean present completion interval.

The first pipelined run showed CpuMean 9.26 ms but cadence 13.27 ms, against
12.80 ms serial. The harness `Present` postfix made about a dozen ordinary
native telemetry reads right after the launch, and each one waited on the
recording. In pipelined mode the harness now takes the frame sample and
telemetry in a `Present` prefix, before the launch. Telemetry then describes
the previous frame.

For reference, DX11 on the same save (`Dx11-85c37e83…`) renders 6,858 frames
in the sampling window: about 4.4 ms cadence, CpuMean 4.25 ms. Simulation does
not cap the frame rate.

## Results

Paired product-default runs, order NP, NPP, NPP, NP
(`TestResults/Pipelined-Paired-2026-10-21.json`):

| Set | CpuMean ms | Median | Present cadence ms | Frames |
|---|---|---|---|---|
| NP (serial) | 12.97 / 13.38 | 12.29 / 12.60 | 13.08 / 13.48 | 2294 / 2228 |
| NPP (pipelined) | 10.19 / 10.44 | 9.42 / 9.67 | 10.45 / 10.72 | 2872 / 2801 |

Real cadence falls by 2.69 ms (−20%), with 25% more frames, in both orders.

Verification (`SE_VULKAN_PIPELINE_REPORT` writes pipelined frames, mask
mispredictions and waits at shutdown):

- Same-process frozen-light lane toggling `PipelinedRecordingEnabled`
  (`Vulkan-9c461949…`, 154 pipelined frames):
  - all 50 probe, cascade and spot artifacts are byte-identical;
  - the view differs in 377 pixels, against 299 for the control-return
    capture;
  - 0 mask mispredictions.
- Validation-layer gameplay run (`Vulkan-4e740a11…`, 572 pipelined frames):
  layer enabled, 0 errors, 0 mispredictions.
- Native CTests 17/17.
- The GPU suites match the previous baseline. SceneSubmission (check 172) and
  Voxel (init) are the pre-existing failures.

## Default

Pipelining defaults on; `SE_VULKAN_PIPELINED_RECORDING=0` restores serial
recording. The harness forces it off unless `-PipelinedRecording` is passed.
Product-default lanes now add
`-NativeCells -NativeMixedOpaque -PipelinedRecording`.

Frame cadence is now bound by the managed side, about 10 ms. Native recording
is hidden behind it. The next gains have to come from managed `Draw`:

- native pass publication, about 2.2 ms;
- traversal, about 1.9 ms;
- QueueWorld, about 1.1 ms;
- voxels and messages, about 1.7 ms.
