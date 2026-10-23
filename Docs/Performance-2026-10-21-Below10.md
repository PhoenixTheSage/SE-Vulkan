# Below the 10 ms wall

This continues `Performance-2026-10-21-Pipelining.md` and implements
`planning/NEXT-SESSION-BELOW-10MS.md`. With pipelined recording, frame cadence
equals managed `Draw` time, so every change here removes work from the render
thread or moves it to a worker.

## Starting profile

CpuDetail + NativeCritical, product default of 1.3.0, two runs
(`Profile-2026-10-21-Pipelined.json`; instrumented, so absolute values run
high):

| Phase | ms per frame |
|---|---|
| Draw | 10.73 / 11.38 |
| Native pass publication | 2.31 (publish 1.31, prepare 0.45) |
| Actor traversal | 2.13 (owner declaration 0.54, query 0.38, plan 0.45) |
| QueueWorld | 1.30 |
| Voxels and billboards | 1.03 |
| Messages | 0.86 |
| Native pass commits | 0.45 |

The pipeline report gained a wait census (below). In the product path almost
nothing waits on recording: about 65 `sevk_get_stats` waits in 6,400 frames.

## Changes

### Async native pass packing (T1)

`publish()` keeps selection synchronous: LOD advance, batch assignment,
transparent tickets, fallbacks, visible owners and spot signatures. Retained-row
comparison, camera patching, stream packing and group assembly run on one
persistent background worker (`native_pass::Background`).

- Every reader of groups or streams and every resident mutation calls
  `settle()` first, so output cannot differ. That covers register, bind,
  invalidate, update, remove, prepare, cancel, the profile export and the
  render-group accessors.
- Recording reads the groups, so in pipelined mode the join lands on the
  recording worker. The census never saw it block on the render thread, and
  saw 0–4 blocking joins on the worker per run.
- Selection also runs in parallel ranges over the prepared selections when at
  least 512 are selected. Ranges merge in order, so batch order, row order and
  every selection output equal the serial result.
- Switches:
  - `SE_VULKAN_ASYNC_PASS_PACKING=0` turns async packing off; harness switch
    `-AsyncPassPacking`.
  - `SE_VULKAN_PARALLEL_SELECTION=0` makes selection serial.

### Deferred OLD cell emission (T3, revised)

Resident cell instance streams were not built: they would need a second
instance buffer binding in world recording and a new motion-history key.

Instead, `sevk_cells_emit` validates and marks the emission pending. The cell
draws are appended when `end_frame` starts, which is on the recording worker
when pipelined, so the draw list and its order are unchanged. Any earlier call
that touches the queued world emits first: world queues, models, main order,
recipe registration or invalidation, cell exports and recipe stats.
`begin_frame` counts the pending draws.

`SE_VULKAN_DEFERRED_CELL_EMISSION=0` turns it off; harness switch
`-DeferredCellEmission`.

### Managed traversal and small items (T2 order-preserving, T5, T6, T7)

- **Main-group order is unchanged.** `DeclareMainOwner` resolves the group
  directly and appends `(owner, group)`, without the scope struct and the
  per-owner dictionary; orders are read when queued.
- **Static groups:** `DrawCandidates` adds them with a per-frame stamp instead
  of a quadratic `List.Contains`.
- **Voxels:** one cell pass computes the transform, bounds and cull decision
  once. Emission replays the surviving cells after material preparation, in
  the same order.
  - `VulkanVoxelMaterials.Prepare` is memoized exactly. It only re-marks the
    used groups while the ordered distinct triples and the group set are
    unchanged since a frame that created and removed nothing.
  - `SE_VULKAN_VOXEL_FAST_PATH=0` restores the two-pass path.
- **Messages:** each render message stores its type as an integer, and the
  string is built only when the host asks for the annotation. Message
  diagnostics count with `Interlocked` instead of a lock.
- **Native pass commits:** reused update and appearance buffers instead of
  per-frame lists and arrays.

### Not done

- **The full T2 ownership move**, which would skip native-owned actors in the
  traversal. Main-group order is spatial-query order interleaved with managed
  packet orders, so skipping actors changes draw order. That needs its own
  deterministic-order toggle and lane.
- **T4, camera-relative translation in the shader.** Its saving was the
  per-row camera patch, which now runs in async packing and never blocked the
  render thread.

### Diagnostics

`SE_VULKAN_PIPELINE_REPORT` now also writes:

- `Sites`: which exports waited on recording, symbolized from the PDB;
- `PackingBlocked`/`PackingWorkerBlocked` with microseconds;
- `PackingSites`: the exports behind render-thread packing joins.

The publish benchmark takes `[serial|parallel] [async]`, and its profile line
reports `select` and `merge`.

## Parity

- Publish benchmark signature `9f932baf54a9f593`: identical serial, parallel,
  serial async and parallel async, before and after parallel selection.
- Same-process frozen-light lane, `AsyncNativeWorkEnabled` toggling packing and
  deferred cells (`Vulkan-fb363cdd…`): all 50 probe, cascade and spot artifacts
  byte-identical. The view differs in 441 pixels, against 204 for the
  control-return capture.
- Same-process frozen-light lane, `VoxelFastPathEnabled` (`Vulkan-273d289e…`):
  all 50 artifacts byte-identical; view 511 against 200. The first attempt
  timed out in phase 0 with no error logged, and the retry passed.
- Validation-layer gameplay run (`Vulkan-868b9893…`): layer enabled, 0 errors,
  557 pipelined frames, 0 mask mispredictions.
- Native CTests 17/17.
- GPU suites at baseline: SceneSubmission (check 172) and Voxel (init) are the
  pre-existing failures.

## Timing

Present cadence (`PresentMs`). Host conditions shifted during the session, so
compare only within a group.

**Quiet hour** (`T1T3-Paired-2026-10-21.json`, order NP, NPA, NPD, NPD, NPA,
NP). NP was the 1.3.0 native toggles plus the managed T2 changes:

| Set | Present ms |
|---|---|
| NP | 9.90 / 9.80 |
| NPA (+ async packing) | 9.25 / 9.30 |
| NPD (+ deferred cells) | 9.58 / 9.12 |

**Later, slower host.** DX11 reference fell from 4.25 to 4.63 ms render-thread
time, and from 6,858 to 6,458 frames:

| Pair | Set | Present ms |
|---|---|---|
| `Below10-Paired-2026-10-21.json` | NP0 (1.3.0 native toggles) | 11.48 / 11.29 |
| | NP (all new defaults) | 11.07 / 11.14 |
| Bisect | NP | 11.20 / 11.18 |
| | NPV (voxel fast path off) | 11.26 |
| | NPQ (serial selection) | 11.56 |

Async packing is the clear win: about −0.55 ms in the quiet pair, in both
orders. Deferred cell emission and the voxel fast path are within noise for
cadence; the CpuDetail profile shows QueueWorld −0.34 ms and voxels −0.10 ms.

Profile after the changes (`Profile-2026-10-21-T1T5.json`, instrumented):

| Phase | Before | After |
|---|---|---|
| Draw | 10.73 / 11.38 | 9.45 |
| Native pass publish | 1.31 / 1.37 | 0.67 |
| QueueWorld | 1.30 / 1.38 | 0.96 |
| Native pass commits | 0.45 / 0.48 | 0.35 |

Under quiet conditions the product default ran below 10 ms present cadence,
9.1–9.3 ms. Under the later host state it ran at about 11.1 ms, against about
11.4 ms for the 1.3.0 native toggles.

## Remaining targets

- Traversal, 2.0 ms: the ownership move above, behind a deterministic-order
  toggle.
- Synchronous publish selection, 0.67 ms instrumented, and prepare, 0.48 ms.
- Messages, about 0.85 ms: break down by message type.
- Native pass pose commits: about 300 actors a frame go through the full commit
  path.
