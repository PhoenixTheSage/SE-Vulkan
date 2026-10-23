# Next session: below the 10 ms wall

## Where 1.3.0 stands

Pipelined recording hides native recording (about 4.0 ms) behind managed
`Draw`. Frame cadence now equals managed render-thread time.

| Measure (private Pertam, product default) | Value |
|---|---|
| Present cadence, paired (`Pipelined-Paired-2026-10-21.json`) | 10.45 / 10.72 ms |
| Render-thread time (`CpuMeanMs`) | 10.19 / 10.44 ms |
| GPU | about 3.1 ms |
| DX11, same save (`Dx11-85c37e83…`) | about 4.4 ms cadence |

Goal for the session: present cadence below 10.0 ms in paired both-order
runs, with parity gates unchanged. Stretch: about 8 ms.

## Where managed Draw spends its time

Last CpuDetail profile, instrumented, so absolute values run high
(`Vulkan-ddd42e7a…`, before pipelining; managed phases are unchanged by it):

| Bucket | ms | What it is |
|---|---|---|
| Native pass publication | 2.17 | prepare 0.43, publish 1.24 (emit about 1.1: about 7,500 view memberships and camera-relative rewrites of every retained row), cell publish 0.22 |
| Actor traversal | 1.92 | 3,714 candidates; about 2,700 are native-owned (native pass or cells) and are visited only to declare main-group owners (`MainOwnerDeclare` 0.51) after `CandidateQuery` 0.34 and `CandidatePlan` 0.33 |
| QueueWorld | 1.20 | `BlobPack` 0.16, native recipe queue 0.29, `cells_emit` about 0.36 (copies about 1 MB of cell instances each frame), order and owner metadata |
| Voxels and billboards | 0.97 | managed clipmap submission |
| Messages | 0.78 (0.96 pipelined) | render-message processing; pipelined adds argument copies |
| Scene update | about 1.0 | transforms 0.21, spatial index 0.39, occlusion 0.27, lights 0.10, shadow poses 0.10 |
| Native pass commits | 0.43 | dirty actor commits, mostly pose updates |
| Particles, lighting setup, sprites | about 0.6 | small |

The native calls inside `Draw` (prepare, publish, cells, recipe queue) run on
the render thread, after recording has finished. They sit on the critical
path, so moving that work is as valuable as removing managed work.

## Targets, ranked by expected gain

### 1. Split native pass publish into selection and packing (0.6–1.0 ms)

`publish()` does two things:

- **Selection.** Batch assignment, LOD advance, transparent tickets, fallbacks,
  visible owners and spot signatures. Managed code consumes these right after
  publish.
- **Packing.** Retained-row comparison and rewrite, stream packing, groups.
  Nothing reads these until `end_frame`.

Plan:

- Keep selection synchronous.
- Start packing on `ViewJobs` lanes and join it at the start of `end_frame`
  (or `queue_world_recipes`, if a consumer appears earlier).
- Gate: the publish benchmark signature is unchanged, and the native-pass
  lane is byte-identical.

### 2. Stop visiting native-owned actors in the managed loop (0.8–1.3 ms)

The traversal walks every candidate only to call `DeclareMainOwner` in
candidate order, because that order numbers the main groups that order NEW
draws.

Plan:

- Give native-owned actors (pass and cells) their main-group token at commit.
- Let native assign main-group order from the same first-appearance rule.
- Then exclude native-owned actors from `CandidateQuery` and `CandidatePlan`:
  either a separate spatial index for managed-owned actors, or an owned flag
  checked before planning.
- Risk: group order is spatial-query order today, the same issue the OLD
  owner tie-break solved. Expect to make main-group order deterministic
  (group token, then owner) under a toggle first.
- Gates: a frozen-light lane on the order change alone, then on the
  ownership move.

### 3. Resident cell instance streams (0.3–0.5 ms)

`cells_emit` copies every drawn cell's instances (about 1 MB) into the
frame's instance blob every frame, although cells rebuild about 0.005 times
per frame.

Plan:

- Keep cell instance bytes in a persistent per-slot region, written only when
  a cell commits.
- Point emitted draws at it.
- Watch motion history: it keys on draw-list instance offsets. Stable
  resident offsets should keep the signature stable; verify it.
- This was rejected as stage 1 earlier because it could not be gated alone.
  It is worth it now that cells are native.

### 4. Camera-relative translation in the shader (0.3–0.6 ms)

Native pass rows store camera-relative transforms, so every retained row is
rewritten whenever the camera moves; that is most of `pack`. Store
world-relative rows against a coarse origin. The stock NEW-pipeline VS adds
`camera_origin - camera` from a frame constant.

- This changes shader inputs, so it needs precision analysis (float rows,
  large worlds) and a parity lane.
- Keep it behind a toggle; it may not be byte-identical.

### 5. Voxel submission (0.4–0.6 ms)

Profile `VoxelsAndBillboards` at phase level first. Candidates:

- retained clipmap cell packets, re-sent only when a cell changes;
- parallel per-clipmap emission.

Do not start without a breakdown.

### 6. Messages and deferral overhead (0.2–0.4 ms)

- Use `SE_VULKAN_PIPELINE_REPORT` waits and wait time to confirm that nothing
  in the product path waits.
- Measure the copy cost of the deferred `pass_update_actors` (about 120 KB a
  frame) and `cells_update_poses`. A persistent scratch arena could avoid
  per-call vector allocation.
- Break `Messages` down by message type. Most is per-entity transform update
  traffic that DX11 also processes.

### 7. Small items, only once the above are in

- Native pass commits: commit pose-only updates through a compact path, as
  cells do.
- `DrawCandidates` static-group `List.Contains` (quadratic).
- `Particles`, `Sprites`, `LightingSetup`.

Expected: targets 1–3 give about 1.7–2.8 ms, enough for under 10 ms. Targets
4–6 head toward about 8 ms.

## Method (unchanged rules)

1. Start with a fresh CpuDetail + NativeCritical profile of the 1.3.0
   default. With pipelining, in-Draw waits are now real time; check the
   pipeline report for waits.
2. Judge throughput by present cadence (`PresentMs`) or frames per window,
   not `CpuMeanMs` alone.
3. Use paired interleaved runs in both orders (NP, X, X, NP); host noise is
   about ±1 ms.
4. Parity: a same-process frozen-light lane per toggle, proving the policy
   was active (per-phase ownership or counters). Use byte-identical
   signatures where the change is pure ordering or packing.
5. GPU suites under validation, plus a validation-layer gameplay run for
   anything that changes the call path or threading.
6. Product-default flags: `-ActorLods -AsyncPresent -NewActorViews
   -RetainedSubmission -StockShadowSchedule -StockShadowFit -WorldRecipes
   -ParallelQueue -ParallelCascades -NativePassScene -GroupOcclusion
   -StockDepthGeometry -NativeCells -NativeMixedOpaque -PipelinedRecording`.
   For DX11, pass `-Backend Dx11` with that exact spelling.
