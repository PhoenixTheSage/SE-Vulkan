# Native pass scene by default, and parallel scene preparation

This continues `Performance-2026-10-20-ReadbackRegression.md`. It covers two
structural steps:

1. Measuring every existing retained/structural path, then promoting the one
   with real gains (the native pass scene) after same-process parity
   verification.
2. Parallelizing scene preparation where order can be reproduced exactly.

## Retained paths measured against the current product default

All runs use product-default policies plus the named set, in one interleaved
sequence on Pertam. C ran first and last as a drift check.
Evidence: `TestResults/Retained-Paths-2026-10-21.json`.

| Set | Added switches | Mean ms | Median | P95 |
|---|---|---|---|---|
| C | none | 21.89 / 20.80 | 20.16 / 19.20 | 29.36 / 27.46 |
| MB | ModelBatching, BulkModels | 20.41 | 18.85 | 26.82 |
| IND | GeometryArena, WorldIndirect | 22.71 | 21.27 | 31.45 |
| NP | NativePassScene, GroupOcclusion, StockDepthGeometry | 17.43 | 16.37 | 22.91 |
| NPM | NP + NativeMixedOpaque | 16.24 | 15.35 | 20.55 |
| GPUH | GpuOpaque set + NP + GpuOpaqueHybrid | 19.88 | 18.68 | 25.54 |

Two sets produced no timing:

- GpuOpaque alone refuses to start without shared depth geometry, the shadow
  policies and group occlusion.
- RetainedObjects cannot combine with native recipes.

GPU time is about 3.1-3.5 ms in every set, so the gains are CPU. Native pass
ownership removes NEW-pipeline actors from the managed traversal.

## Parity method

Separate paused captures are not deterministic enough to judge lighting.
Probe stages, exposure, sparks and the scene's flashing white light differ
between processes; two product-default captures differ by mean 1.9-2.7 levels.
NPM first appeared to brighten the doorway, but the flashing light can explain
that.

The decisive evidence comes from `-SelectionAudit -LightingReconciliation`, a
paused OFF/ON/OFF toggle inside one process. After the first capture it freezes
and replays lights, spot projections, frame/probe constants and postprocess
configuration, and it forces a complete probe cycle and refreshed cascades. The
lane now also dumps every shadowed spot map. Two new switches select the
toggled policy:

- `-LightingNativePass` toggles `NativePassSceneEnabled`.
- `-LightingProperty <name>` toggles any boolean scene property.

The `SetProbes` patch now names its two-argument overload; the one-argument
overload made the patch ambiguous and the lane failed before loading.

NP, same process with frozen lights (`Vulkan-6dd7b06a…`):

- The control-return phase is byte-identical to control for every artifact
  except 0.004% of view pixels (sparks).
- All six cascades are byte-identical with NP.
- The main view differs in 0.65% of pixels, almost all by 1-2 levels. Only 333
  pixels (0.004%) differ by more than 16 levels, all on one coplanar seam at
  the doorway threshold where draw order picks the edge winner.
- Six probe faces differ in at most 1% of texels, maximum 0.15.
- Four spotlight shadow maps differ completely. Under the managed product
  path every texel holds the same value, while NP produces normal depth.
  The section below explains and fixes this.

## Spotlight housings inside the shadow near plane

The pure product default (`Vulkan-aebe1b26…`, native pass not configured) also
showed the defect in three of seven shadowed spotlights, while redrawing every
map each frame. The maps are not zero: every texel is exactly 5.877472e-35
(`0x06800000`), constant across the map.

Causes ruled out:

- Parallel spot recording. A serial run (`SE_VULKAN_PARALLEL_SPOTS=0`,
  `Vulkan-e294f0df…`) gives the same maps.
- Depth clamp. Neither static nor dynamic state enables it.
- Projection uploads.
- A missing shadow-ignore exclusion. Stock sends `SetLightShadowIgnore` only
  for characters.

A temporary per-light caster trace showed the cause. Every bad light had its
own lamp housing among its casters: two meshes 0.1-0.3 m from the light with
radius about 0.3, entirely inside the stock 0.5 m spot shadow near plane
(`MyLightComponent.UpdateViewProjection`).

Stock culls spot casters against the full spot frustum, and so does NP
(`scene_actor::disjoint` against the spot `WorldProjection`). The managed path
used `spot_sphere_visible`, which tested only range and cone, so it drew the
housing and filled the map at near-plane depth.

`spot_sphere_visible` (`Native/src/spot_bounds.h`) and its managed mirror
`SpotSphereVisible` now also reject spheres that end before the 0.5 m near
plane. The sphere encloses the caster bounds, so this never rejects a caster
that the stock frustum test keeps. `spot_bounds` tests cover a housing inside
the plane, a caster crossing it, and a narrow cone.

Frozen-light lane on the pure default after the fix (`Vulkan-a323724d…`):

- All eight shadowed spot maps hold real depth.
- Control, treatment and control-return are identical.
- The housing meshes no longer appear in any caster list.

Same-process managed versus NP after the fix (`Vulkan-55c52ac7…`):

- Six of seven spot maps are byte-identical. Before the fix, four differed
  completely. The seventh differs in 0.53% of texels, max 0.0037.
- All cascades are identical.
- 299 view pixels differ by more than 16 levels, against 333 before, all on
  the same doorway seam. The growth in total changed pixels is entirely
  1-2-level differences.

The NPM lane timed out in a capture phase ("Lighting phase exceeded 120
seconds"), so NativeMixedOpaque remains unverified and off.

A validation-layer gameplay run with NP (`Vulkan-bbd36db5…`, 203 sampled
frames) reports the layer enabled with zero core/synchronization errors.

## Default change

`NativePassScene`, `GroupOcclusion` and `StockDepthGeometry` now default on.

- `NativePassPolicy` enables the native pass scene unless
  `SE_VULKAN_NATIVE_PASS_SCENE=0`, or a prerequisite policy is disabled by
  environment, or the GPU opaque scene is requested.
- With the scene enabled by default, a prerequisite turned off at runtime
  returns ownership to the managed path.
- Only an explicit `SE_VULKAN_NATIVE_PASS_SCENE=1` keeps the strict exception.
- `SE_VULKAN_GROUP_OCCLUSION=0` and `SE_VULKAN_STOCK_DEPTH_GEOMETRY=0` still
  roll back their policies.
- The harness still forces all three off unless their switches are passed.
  Product-default benchmarks must add `-NativePassScene -GroupOcclusion
  -StockDepthGeometry`.

`ShadowOcclusionGpuTests` pins `NativePassSceneEnabled=false`, because its
checks cover the managed group bridge, which native ownership bypasses. With
the new defaults:

- SceneSubmission still stops at its pre-existing 1.2.0 spotlight check after
  172 passes.
- VoxelGpuTests fails pipeline initialization with both old and new defaults
  (pre-existing).

## Parallel scene preparation

**Native pass publication.** Per batch, the bookkeeping (owners, epochs,
states, activated materials, retained-layout checks, row packing or patching)
touches only that batch and its own retained entry. Batches now prepare on
`ViewJobs` lanes, using `SE_VULKAN_PARALLEL_PASS`, which defaults on with at
least four hardware threads. Stream indices, groups and the next retained map
are then assembled serially in batch order.

**Native pass preparation.** Actor ranges select views and LODs into
range-local selections, fallbacks, query owners and model requests. Merging in
range order reproduces first-appearance query merging, request deduplication
and list order exactly.

**Managed traversal.** A parallel pre-pass computes each candidate's ancestor
visibility and root main-group key (`SE_VULKAN_PARALLEL_TRAVERSAL`). Owner
declaration and emission stay serial in candidate order. The planned values
match what the loop would compute, because an actor's key inputs change only
during its own processing. Supporting changes:

- Lazy per-model NEW-pipeline classification is settled serially before the
  pre-pass.
- Identity interning is locked.
- A new `CandidatePlan` CpuDetail phase measures the pre-pass.

Validation:

- Native CTests 16/16.
- Serial/parallel/serial byte A/B with the private Khronos layer and
  synchronization validation: NativePass, WorldPass 538/538, DepthGeometry,
  Probe, ShadowOcclusion and Postprocess. All 147 binary outputs are identical
  across modes and repeats, with zero errors.
- In the real scene, the same-process lane toggling `ParallelTraversalEnabled`
  (`Vulkan-6eb70d84…`) keeps all 49 probe/cascade/spot artifacts
  byte-identical. The view differs in 273 pixels (max 10), at the same
  magnitude as the serial return capture (97 pixels, max 7).

Timing, same binaries with the policy toggled
(`Lever2-Parallel-2026-10-21.json`, order NP, NPs, NPs, NP):

| Policy | Mean ms | Median | P95 | Queue section ms |
|---|---|---|---|---|
| Parallel (`Vulkan-3f501a5b…`, `Vulkan-225d0374…`) | 15.32 / 17.10 | 14.44 / 16.28 | 19.73 / 22.93 | 3.28 / 3.39 |
| Serial (`Vulkan-9050b982…`, `Vulkan-41441656…`) | 16.49 / 16.21 | 15.42 / 15.19 | 21.60 / 21.16 | 4.13 / 4.04 |

The Queue section contains native pass prepare/publish, and it drops by about
0.75 ms in both orders. Whole-frame means stay within host noise.

## State and remaining work

The product default now runs about 15-17 ms on Pertam, against about 21-22 ms
before this step and 119 ms in 1.2.0. DX11 measures 4.8 ms under the same
host load. Remaining large items:

- Native recording, about 4.2 ms.
- Managed traversal of OLD-pipeline actors (instanced grid cells, characters,
  skinned and static-instance actors), about 3 ms.
- QueueWorld, about 1.3 ms.
- Unverified NativeMixedOpaque, about 1.2 ms (its lane must complete first).
