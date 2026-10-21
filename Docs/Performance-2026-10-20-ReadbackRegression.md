# 1.2.0 CPU regression: mapped readback and main-group keys

Release 1.2.0 made the private Pertam doorway scene about 9x slower than the
October 8 checkpoint. Two parity additions caused nearly all of it; both are
corrected here without changing any byte the GPU consumes. Further cuts remove
redundant command recording and struct copies. The <4.17 ms target remains open.

## Measurement lane

`Run-Isolated.ps1` forces every `SE_VULKAN_*` toggle to `0` unless its switch is
given, while the product enables several by default. Product-default runs pass
`-ActorLods -AsyncPresent -NewActorViews -RetainedSubmission -StockShadowSchedule
-StockShadowFit -WorldRecipes -ParallelQueue -ParallelCascades`. Earlier plain
harness runs measured a different policy set.

The benchmark host idles at about 25-32% CPU from desktop applications, and
single runs vary by 10% or more. The comparison below is one interleaved
sequence (A, C, B, B, C) with archived binaries passed through
`-BenchmarkPlugin`/`-BenchmarkNative`, plus a DX11 run under the same load.
Evidence: `TestResults/Optimization-2026-10-20-Paired.json`.

| Build | Mean ms | Median | P95 | P99 | FPS |
|---|---|---|---|---|---|
| DX11 (`Dx11-f16d57a2…`) | 4.81 | 4.68 | 6.07 | 7.68 | 206 |
| A: 1.2.0 (`Vulkan-83aadb33…`) | 119.19 | 106.71 | 185.90 | 338.56 | 8.4 |
| B: readback + group keys (`Vulkan-57b598f9…`, `Vulkan-27a233a1…`) | 24.59 / 27.49 | 23.39 / 26.55 | 29.03 / 34.15 | 34.82 / 40.08 | 40.5 / 36.2 |
| C: all changes (`Vulkan-1884e167…`, `Vulkan-969e5a90…`) | 21.87 / 21.91 | 20.76 / 20.77 | 26.98 / 27.06 | 33.09 / 31.54 | 45.5 / 45.4 |

The final-window world GPU time stays about 3.4 ms in every Vulkan build.
The frame is CPU-bound.

## Findings and corrections

**Uncached readback (about 40 ms).** World frame/object/instance buffers use
VMA `HOST_ACCESS_SEQUENTIAL_WRITE`, which can resolve to device-local BAR on
ReBAR GPUs. 1.2.0 motion history copied the previous slot's whole instance
buffer through its mapping. Object motion and G-buffer attachments read object
rows and the distance-fade threshold per draw from current and previous slots.
Spot fade dependencies hashed object and instance bytes from the same mappings.
Each uncached read is a PCIe round trip. The cost appeared only as unscoped
`RecordOwner` time (51.7 of 51.7 ms, with 11 ms in named scopes).
`WorldSlot` now keeps host copies of exactly the uploaded frame, main
projection, object and instance bytes. They are filled in `render_world`
beside the mapped writes, including retained object records. Every former
reader uses them. Native main recording fell from 51.2 to 1.5 ms.

**Main-group key thrash (about 40% of scene draw).** `MainGroupKey` cached one
key per actor, but each frame asks for the root model (`DeclareMainOwner`) and
each selected LOD (`DrawWorld`). Every call missed, re-sorted material overrides
with LINQ and rebuilt a file-path string and `Tuple`. The identity string
depends only on override textures/visibility, and each such change bumps
`MaterialRevision`, so it is now cached per revision. Keys are cached per
(model, metalness, dither/fade state) in an `ActorData` field. The
`MainGroupIdentity` key keeps the former tuple's structural equality, with a
precomputed hash.

**Further reductions (B to C, about 3.7 ms).**

- `world_model_raster` set cull mode, front face and depth-bias enable for every
  draw. Main, cascade, spot, probe, motion and attachment loops now skip
  unchanged values. Each loop's cache starts invalid and is invalidated after
  binding a voxel pipeline, whose raster state is static.
- The world draw sort compares compact per-draw copies of the same inputs, with
  the same comparator logic. `std::sort` therefore produces the same order.
  Native sort fell from 2.35 to 1.05 ms.
- The conservative point-light tile list is rewritten only when tile count,
  point count, capacity or the slot buffer changes.
- `WorldPacket` (about 160 bytes with four references) was copied out and back
  for every rigid instance append, about 8K per frame, through
  `BulkMoveWithWriteBarrier`. Appends and per-frame scans now use in-place
  references (`CollectionsMarshal` on net10, the list's backing array on net48).

## Parity and validation

No shader, constant, upload byte, draw membership or submission order changed.
The GPU consumes identical buffers. Every skipped state command repeats a value
already set in that command buffer, and the sort comparator is unchanged.

- Canonical native build: 16/16 CTests (one run skipped the optional reference
  test, as before).
- Release net10 and net48 builds have zero warnings and zero errors.
- Run under the private Khronos layer with synchronization validation:
  WorldPassGpuTests 538/538, ProbeGpuTests 427, LightingParityGpuTests (256
  DX11/Vulkan PBR cases), NativePassGpuTests, DepthGeometryGpuTests and
  ShadowOcclusionGpuTests. All pass with zero core/synchronization errors.
- SceneSubmissionGpuTests stops after 172 passes at "live shadowed spotlight
  retains the rear caster through production light messages". The original
  1.2.0 managed sources and native DLL fail identically
  (`TestResults/SceneSubmissionGpuTests/baseline-ab-validation.log`), so this
  is a pre-existing 1.2.0 failure, not from this work.
- Unpaused doorway captures fall within the same-build noise between three
  original-build captures (mean channel delta 1.7 against a 3.8-12.7 control
  range; particles and exposure move). This is not pixel proof; the GPU suites
  above are the controlled evidence.

## Managed traversal pass

EventPipe thread-time sampling (`dotnet-trace`, the only managed sampler
available without elevation) attributes samples to call sites and P/Invoke
boundaries. With `DOTNET_JitNoInline=1` it ranked `List<WorldPacket>.Add`,
`Path.GetFullPath` and `Stopwatch.GetTimestamp` highly. A microbenchmark shows
`GetTimestamp` costs 13 ns here, and removing the first two did not move paired
results (`Optimization-2026-10-20-Traversal1*.json`, both orders). Wall-clock
phases are used for magnitudes. Five new `-CpuDetail`-only phases split the
traversal: `MainOwnerDeclare`, `NewViewSpots`, `NewViewSelect`,
`DrawWorldSetup` and `PreparedAppend`. They are inert without `-CpuDetail`.

`MainOwnerDeclare` measured 2.51 ms for 3714 calls. Every actor held a
separate copy of its material-identity string, so native main-group lookups
for equal keys compared long texture-path strings character by character.
Identity strings are now interned per scene, so equal identities share one
instance and compare by reference. Each key also remembers the group it
resolved to in the current group epoch; `BeginMainGroups` advances the epoch
when it clears the dictionary. Equal keys still meet through the dictionary
on first use, so grouping is unchanged. With interning, `MainOwnerDeclare`
fell to 1.18 ms and `DrawWorldSetup` from 1.14 to 0.81 ms.

Also retained, though too small to measure alone:

- in-place `WorldPacket` adds on net10
- a per-session memo of OLD source-LOD path matches (failures are not cached
  and still throw)
- a per-path memo of the indirect skybox probe `File.Exists`

Paired, interleaved (`Optimization-2026-10-20-Traversal2.json`), with E as this
pass and C as the previous build:

| Build | Mean ms | Median | P95 | FPS |
|---|---|---|---|---|
| E (`Vulkan-4b01a8fe…`, `Vulkan-5a383106…`) | 20.53 / 21.49 | 18.56 / 19.42 | 27.82 / 29.17 | 48.4 / 46.3 |
| C (`Vulkan-54a28c82…`, `Vulkan-074a024a…`) | 21.70 / 21.56 | 20.34 / 20.24 | 27.96 / 27.12 | 45.8 / 46.1 |

The instrumented traversal (`Vulkan-316e511f…`) is 9.9 ms. It breaks down as
NewViewEmission 4.0 (PreparedActors 2.6, of which PreparedAppend is 1.7 across
about 8300 parts), MainOwnerDeclare 1.2, NewViewPlan 0.85 and DrawWorldSetup
0.8, plus visibility/LOD gates and legacy parts. No single per-actor item now
exceeds about 1.7 ms. Further managed gains need fewer per-actor operations,
not cheaper ones.

## Remaining profile and next work

Before the traversal pass, the instrumented frame (`Vulkan-f95b1c0f…`, about
12% inflated by host load) had managed scene traversal at 14.0 ms. That breaks down as
NewViewEmission 5.4, PreparedActors 3.5 and the remaining per-actor
visibility/LOD/view gates. Native recording is 7.6 ms (WorldPrepare 2.7, main
2.2, spots 2.0, cascades 1.7). QueueWorld is 3.3 ms, with blob pack 1.4 ms.
Messages take 1.5 ms and voxels/billboards 1.3 ms.

The remaining 4.5x gap to DX11 is structural. About 2,600 actors and 8,000
parts are still walked and replayed through managed code every frame. The
existing opt-in retained paths (`NativePassScene`, `GpuOpaqueScene`) target
this, but their documented ownership and visual parity gates are still open.
Adopting either is a separate decision. Smaller items: the conservative tile
list still makes the GPU loop over every point light per pixel. The GPU
light-tile path exists but is off by default.
