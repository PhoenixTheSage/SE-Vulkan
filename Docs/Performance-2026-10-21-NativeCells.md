# Native OLD cube grid cells

This continues `Performance-2026-10-21-NativePassDefault.md`. It moves the
static retained OLD-pipeline actors off the per-frame managed path.

## What remained on the managed path

Census of the 975 retained-submission actors on Pertam (one product-default
frame):

- 871 were cube grid render cells: type 4 (deformed cube) or type 3, one
  part each, an instance-buffer stream, an OLD model and a parent grid.
  Together they carried about 16,300 cube instances.
- About 104 were rigid NEW-model blocks the native pass rejects. 70 have glass
  parts, 19 use converted OLD materials, and 15 are DECAL_CUTOUT + MESH
  (probably native pass fallbacks).

Each frame the managed path did the following work for every cell, although
cells rebuild about 0.005 times per frame:

- traversal, culling, LOD and cascade decisions;
- owner declaration and packet append;
- matrix rewrite;
- packing and copying about 1 MB of cube instances.

A temporary CpuDetail scope measured 1.63 ms for the cells alone, plus their
share of owner declaration and packing.

Two earlier half-measures in this area were rejected on 2026-10-08: native
culling with managed emission, and a prepared-binding producer feeding the
bulk path. Both concluded that only a producer that also emits natively pays
off.

## Design

The native pass scene emits through the stock NEW-pipeline (GeometryStage2)
shaders with per-instance transform rows. Cells draw through the OLD world
pipelines, with per-cell object constants and a cube stream. Cells therefore
get their own producer, which emits the same recipe world draws the managed
path queues today. Recording is untouched.

**`Native/src/old_cells.h` (CPU core)** reproduces the managed per-actor
decision literally:

- ancestor visibility;
- the main-frustum test (`BoundingFrustumD` planes and the stock box/plane
  test);
- the probe-distance and shadow-distance culls;
- OLD LOD with two-mesh transitions (`resident_proxy::Lod`, the
  `StockActorLodState` port), using the stock parented float distance
  (`MyActor.CalculateCameraDistanceSquared`);
- the cascade mask, from exactly the inputs `VulkanShadows.CascadeMask` uses;
- the stock far-cascade rule.

Group occlusion goes through the same managed `QueryGroupVisibility` and
`FilterShadowGroupCascades` owners as the native pass. The core writes the
same camera-relative matrix rows and fade alpha into a copy of the prepared
constants.

**`Native/src/old_cells.inc` (glue)** appends cell draws after every managed
packet and its order metadata, so managed physical indices (highlights,
exclusions) do not move. Each cell's instance bytes are written once per frame
and shared by its parts and LOD draws.

**`ClientPlugin/Rendering/Scene/VulkanOldCells.cs` (managed)** commits cells
from the native pass dirty set:

- Full commits build every LOD level's prepared parts and recipes.
- Pose commits (transforms, shadow poses, group and exclusion changes) send
  only placement and flags (`sevk_cells_update_poses`). Cells on moving grids
  otherwise re-dirtied about 440 cells each frame through `NoteShadowPose`.
- Removal hands the LOD selector and last representation back to the managed
  path.
- The traversal still declares the cell's main-group owner, then skips it.
- LOD representation changes still invalidate occlusion and mark spots.

These stay managed: highlights, shadow exclusions, dithering, glass
representations and unsupported policy combinations.

**Order.** Equal stock OLD sort keys previously fell back to submission
order, which depended on the spatial query. Draws now carry their logical
owner (`sevk_world_draw_owners`), and equal OLD keys order by owner, then by
submission. Shared batches use owner 0. The final sort branch also gained a
submission tie-break; previously it left equal keys in unspecified order.
`SE_VULKAN_OLD_OWNER_ORDER=0` restores the old order.

## Verification

- Native CTests 17/17, including the new `old_cells` test (culling, flags,
  queries, cascade gates, constants and LOD transitions).
- Same-process frozen-light lane toggling `NativeCellsEnabled`
  (`Vulkan-22968cc7…`). The new per-phase `cells.json` proves ownership: 1,797
  cells owned natively and 871 drawn in treatment, 0 in both control phases.
  - All probe faces, cascades and spot maps are byte-identical.
  - The view differs in 239 pixels (0.003%), against 358 for the unchanged
    control-return capture.
- Same-process lane toggling `OldOwnerOrderEnabled` (`Vulkan-fd342990…`): all
  artifacts identical; the view differs at the same noise level.
- GPU suites under the private Khronos layer with synchronization validation
  match the previous baseline. SceneSubmission (check 172) and Voxel (init)
  remain the pre-existing failures.
- A gameplay validation run with native cells (`Vulkan-a288c128…`) reports
  the layer enabled with zero errors.

Paired timing, product default, in the order NP, NPC, NPC, NP
(`TestResults/NativeCells-Paired-2026-10-21.json`):

| Set | Mean ms | Median | P95 |
|---|---|---|---|
| NP | 15.53 / 16.34 | 14.42 / 15.46 | 20.28 / 21.98 |
| NPC (native cells) | 15.62 / 14.37 | 14.02 / 13.62 | 24.49 / 18.52 |

Averaged over both orders, the mean drops 0.94 ms and the median 1.12 ms.
CpuDetail (instrumented):

- actor traversal 4.80 → 2.81 ms;
- `BlobPack` 0.54 → 0.23 ms;
- new cell commits 0.11 ms and cell publication 0.22 ms.

## Default

`NativeCellsEnabled` defaults on (`SE_VULKAN_NATIVE_CELLS=0` opts out). The
policy requires all of the following:

- the native pass scene, retained submission, world recipes, group occlusion
  and the owner order;
- no model batching, bulk models, actor size culling, view eligibility or GPU
  opaque scene.

The harness forces it off unless `-NativeCells` is passed; product-default
lanes add it.

## Remaining retained actors

The remaining 104 retained actors cost `PreparedActors` about 0.18 ms. That
is below this host's paired-timing resolution, so widening ownership for
them alone cannot be gated. The measurable remainder is NativeMixedOpaque,
which owns the glass group.

NativeMixedOpaque now completes the same-process lane
(`-LightingProperty NativeMixedOpaqueEnabled`, `Vulkan-50ce1f55…`); the
earlier 120-second timeout did not recur. It fails parity:

- Cascades and spot maps are identical.
- Six faces in each probe set differ in up to 3.6% of texels, max 0.18.
- The main view differs in 6.9% of pixels against 0.006% return noise. 556k
  of those differ by one level, 351 by more than 4 and 23 by more than 16.
  They are spread across the frame, which fits a lighting shift from the
  changed probes rather than missing geometry.

The forward (probe) representation of mixed glass models differs from the
managed path. NativeMixedOpaque stays off until that is reconciled.

### Forward probe parts (fixed)

The managed path was the side that deviated. Stock GeometryStage2 prepares
environment probe parts in `MyForwardRenderPass.PreprocessData`, which keeps
only non-transparent `MESH` parts. The native pass registers forward parts
the same way.

Managed NEW-pipeline actors (fallbacks and models the native pass does not
own) instead sent DECAL_CUTOUT and ALPHA_MASKED parts to the probes. The probe
recorder draws every world draw without flag 16384. When NativeMixedOpaque
moved the glass models native, those parts left the probes and the lighting
shifted.

`MaterialRasterFlags` now sets 16384 (excluded from forward probes) on the
non-MESH parts of NEW-pipeline actors. OLD actors are unchanged, because
stock OLD submeshes default to all flags and draw every part into probes.

After the fix:

| Lane | View | Probes | Cascades and spots |
|---|---|---|---|
| NativeMixedOpaque toggle (`Vulkan-ebcb4546…`) | 0.007% of pixels, max 16 (return noise 0.003%) | at most 0.375% of texels | identical |
| Native pass toggle (`Vulkan-a8c6af6b…`) | — | at most 0.28% (was 1%) | — |

Details:

- The NativeMixedOpaque probe residual is 22 texels in one face, in a
  10-texel-wide edge strip of a bright object, plus 0.001-0.002 differences
  from half-precision appearance tints.
- In the native pass toggle lane, one spot map differs by about 0.003 in
  depth on 1% of texels.

The GPU suites match the previous baseline. A gameplay validation run with
both defaults on (`Vulkan-b869906e…`) reports the layer enabled with zero
errors.

Paired timing (product default with native cells, order NP, NPM, NPM, NP;
`TestResults/MixedOpaque-Paired-2026-10-21.json`):

| Set | Mean ms | Median | P95 |
|---|---|---|---|
| NP | 13.08 / 13.17 | 12.68 / 12.70 | 15.68 / 15.74 |
| NPM | 12.63 / 12.69 | 12.31 / 12.30 | 14.97 / 15.07 |

NativeMixedOpaque saves 0.47 ms mean, 0.39 ms median and 0.69 ms P95. It now
defaults on (`SE_VULKAN_NATIVE_MIXED_OPAQUE=0` opts out). Product-default
harness lanes add `-NativeCells -NativeMixedOpaque`.

## Native pass publication

A fresh CpuDetail profile of these defaults (`Profile-2026-10-21-Defaults2.json`,
13.9 ms instrumented, GPU 3.1 ms) put native pass publication at 2.55 ms.
Publish was 1.63 ms, of which emit was 1.18 ms, for about 2,400 groups and
about 130 rewritten rows.

`Native/tests/native_pass_publish_bench.cpp` drives `prepare` and `publish`
over a synthetic scene: 1,800 actors, 48 families, colour, cascade, probe and
spot views. It hashes every published group, stream byte, owner, identity
and request. Every change below keeps that signature unchanged.

- Batch selection, retained batches and spot signatures use hash maps.
  Publication order still comes from `batch_order`, and the retained and spot
  maps are only looked up.
- Batch keys without hidden materials, which are nearly all of them, use a
  plain-key map.
- An unchanged batch layout compares and updates its retained owners, epochs
  and states in place. Previously every batch allocated three new vectors per
  frame just to compare them.
- The hidden-view lookup no longer inserts empty entries.
- Emission has one profiling scope; a clock pair per `select_batch` call had
  inflated the profiled numbers. Packing has its own `pack_us` (C++ profile
  only).

Benchmark: best frame time 5.03 → 2.64 ms serial, 4.78 → 2.41 ms parallel.
In game (CpuDetail, `Vulkan-ddd42e7a…`):

- publish 1.63 → 1.24 ms;
- spot signatures 53 → 23 µs;
- native pass publication 2.55 → 2.17 ms.

The remaining emit time, about 1.1 ms, is split about evenly between:

- selection, at about 7,500 view memberships and about 65 ns each;
- rewriting camera-relative transforms for every retained row as the camera
  moves.

The second needs a shader-side change to camera-relative translation, so it is
not a cheap target.

The NativePass fixture now sets NativeMixedOpaque off explicitly before its
managed reference frame, because the policy defaults on.
