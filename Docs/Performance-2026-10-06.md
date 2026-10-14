# Performance pass — 2026-10-06

## Target and baseline

The user's Pertam outpost screenshot reports 52.47 ms across 186 unpaused
frames. A 240 fps budget is 4.1667 ms; greater than 240 fps requires less than
that. The reported CPU render work must fall by about 92.1% to meet that budget.
This does not establish total game frame pacing or GPU duration.

`FrameProfiler.BeginDraw` starts the interval and `Present` commits it after
native EndFrame. It measures render-thread CPU elapsed time, including waits
and the host gap between Draw and Present. The native prepare/shadow/main/probe/
post rows are CPU portions of native command recording, not GPU execution and
not additional frame costs. Slot wait/acquire/reset similarly overlap Frame
slot. Do not add the child rows to their parent.

Largest screenshot costs:

- Native record: 13.72 ms (26.1%). Its children include prepare 4.20, spots
  2.85, cascades 2.43, main 1.33, probes/occlusion 0.80, lighting/post 1.56 ms.
- Materials: 9.13 ms (17.4%).
- Queue world: 7.56 ms (14.4%).
- Actor cull: 4.51 ms; voxels/flares: 4.47 ms; scene update: 4.08 ms.
- Instance pack: 2.07 ms. Slot wait is only 0.75 ms in this capture.

Eliminating materials and queuing entirely would still leave 35.78 ms.
Eliminating all three largest parent sections would leave 22.06 ms. A few
micro-optimizations cannot establish 240 fps; structural work and a valid
Release baseline are required.

## Changes in this pass

- Cache `NativeWorldDraw` ABI size once, rather than reflect/marshal its size
  for every world packet. Remove redundant packet writes in that loop.
- Preserve reference-sharing of managed instance streams through native queue
  ownership. Deduplicate by source pointer **and byte count within one queue
  call**; deep-copy each unique stream before the managed arrays are unpinned.
  Clear identities each frame so pointer reuse cannot retain stale instances.
- Upload the compact native instance arena with one contiguous copy. Previously
  each material part expanded and copied its actor's stream again, and GPU
  buffer sizing summed those duplicates. The 256 MiB bound now applies to
  unique resident stream bytes. Per-draw offsets/counts remain unchanged.
- Reuse the native draw vector's capacity and reserve preparation offsets.
- Fix warm material-cache texture changes with an actor material revision.
  Texture names are revalidated on appearance messages; unchanged draws retain
  the short cache path. Actor tint/HSV/dither and pipeline checks still apply.
- Expand profiler history to 4096 frames (1024 could not hold ten seconds at
  240 fps), reuse timing arrays after ring wrap, label the CPU frame interval,
  and show an independent asynchronous world GPU interval. Repeated GPU sample
  IDs are counted once; invalid/missing GPU data displays as unavailable.

No render passes, geometry, shader features, shadow quality or render resolution
were removed to produce the copy benchmark.

## Measured copy reduction

Release `sevk_world_instance_arena --benchmark`: 256 unique streams × 256
instances × 8 material parts, 200 measured iterations after storage warmup.
Expanded copying averaged **2.15578 ms**; shared copying averaged **0.290624 ms**.
Copied instance bytes per frame fell from 33,554,432 to 4,194,304 (8× reduction).
This is a synthetic CPU queue-copy benchmark, not a prediction of Pertam frame
time or a measurement of the full managed/native/GPU submission path.

## Verification

- Managed Release solution builds for net48 and net10.0.
- Native Release build and all four CTest cases pass with the local Khronos
  validation layer discoverable: instance arena, ABI, GPU smoke, HWND present.
- WorldPassGpuTests: 65 checks with validation explicitly required/active.
  Shared and independently copied streams produce identical GPU pixels,
  preserve distinct object constants and draw/instance counts, and survive
  source mutation after queueing. All material/depth partners still pass.
- WorldGpuSmoke: installed armor, astronaut, point light and skybox render with
  zero validation errors; texture-only replacement changes a warm actor's GPU
  pixels and restoration recovers its original pixels; unload releases resources.
- FrameProfilerTests: duplicate/invalid GPU samples, paused gameplay exclusion,
  2500-frame retention, 4096-frame ring wrap, live-only recording all pass.

The GPU harness had pre-existing stale work-stat size and shadow-call/reference
assumptions; these were repaired. Tests now require the validation layer to be
active instead of interpreting zero errors with validation absent as coverage.

## Reproduction findings

LastSession.sbl and the latest user's game log identify `Pertam Orbiter
2026-08-30 07-22`. The checkpoint was last saved September 28, so its restored
camera is not assumed to match the October 6 screenshot until captures agree.
The user's display config is 1920×1080 with VSync off.

The active Interim source profile selects `DebugBuild=true`. SmoothFrames.cfg
caps output at 240 fps. Validation is requested in Vulkan.cfg, but the latest
Vulkan session log says `available=0`, so the screenshot must **not** be described
as suffering active validation-layer overhead.

The new ScenePerformanceHarness copies this explicitly selected world/profile
into private data, pins Release net10 DLL/native assets, uses 1080p and VSync off,
disables validation, raises only the private cap to 1000, captures the restored
view, warms up 45 seconds and measures 30 seconds. Both CPU render duration and
Present interval are recorded. Source world/profile files stay untouched.

## Structural investigation plan (completed below)

1. Establish CPU/GPU/draw-count baselines from the private live runs. Compare
   view captures, graphics quality, render fraction and visible content before
   comparing DX11 with Vulkan. A faster result with missing content is not parity.
2. Materials/pack: cache instance encoding by buffer revision, slice and color
   state; remove unchanged per-actor preparation only with correct mutation
   invalidation. Reduce managed packet count through compatible actor batching.
3. Native prepare: measure descriptor cache misses and their causes. LCD texture
   replacement currently marks every world material pool stale; changing one
   LCD can force rebuilding unrelated descriptors. Use targeted invalidation or
   persistent LCD images only with fenced descriptor/image lifetime ownership.
4. Native recording: precompute spotlight/cascade caster lists, share the main
   prepared material/mesh lookups across passes, and batch compatible geometry
   with hardware instances/indirect submission. Measure total commands across
   all passes, not just the main-view cumulative counter.
5. Scene/cull: measure the two `UpdateWorldTransforms` traversals around
   `Updater.Update` and the all-actor scan. Replace them with dirty propagation
   and spatial candidate lists while preserving RenderDirect updates, visibility,
   off-camera shadow/probe casters and asynchronous occlusion behavior.

The new GPU row measures the native world timestamp region only. Uploads,
final world composition, HUD and swapchain present are outside it; it is
neither an exclusive child of CPU Frame nor full-frame GPU duration.

## Private live Vulkan result

Run: `TestResults/ScenePerformance/Vulkan-cbe04fb041a54e3db407d06c8aff79eb`.
Release net10.0 on runtime 10.0.11, RTX 5070 Ti, 1920×1080 world and output,
render fraction 1, VSync off, validation disabled, private frame cap 1000.
815 frames sampled over 30 seconds after warmup:

- CPU Draw-through-Present mean **36.7947 ms**, median 35.5653 ms,
  p95 44.3675 ms, p99 50.2739 ms.
- Mean Present interval **36.8279 ms**, rendered throughput **27.1533 fps**.
- Final ten-second profiler window: CPU Frame **36.4774 ms**, asynchronous
  world GPU **4.5495 ms**, 274 distinct GPU samples, **9114 main-view draws/frame**.
- Largest CPU rows in that window: Native record 15.9781 ms, Queue world
  4.6276 ms, Materials 4.0551 ms, Actor cull 2.7601 ms, Scene update 2.6178 ms,
  Voxels/flares 2.2867 ms, Instance pack 0.8185 ms. Slot wait was 0.0289 ms.
- Native prepare 3.4274 ms, spots 1.8436 ms, cascades 1.8431 ms, main 1.0784 ms,
  probes/occlusion 0.6629 ms, lighting/post 1.3659 ms. The roughly **5.76 ms**
  remainder inside Native record covers final composition/HUD, command ending,
  queue submit and present; instrument those separately before attributing it
  to a specific driver/API call.
- Scene counters: 6955 actors, 28876 instances, 1453 models, 8605 resident meshes.
  Upload scheduling reports zero outstanding-slot waits.

The view capture faces the outpost doorway from a different angle than the
user's paused screenshot. These results establish the restored save's baseline;
they do **not** establish a 52.47→36.79 ms same-camera optimization gain.
The GPU world interval already exceeds 4.1667 ms, but CPU submission is far
larger; both require work to reach >240 fps. Colorful Icons contains unsupported
legacy DDS files in this Vulkan run, and broader visual parity remains pending.

The source checkpoint SHA-256 remained
`D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92`.

## Matched private DX11 comparison

Run: `TestResults/ScenePerformance/Dx11-faaa9be2ddd746c79c809901c2bdc245`.
Same checkpoint hash, active plugin/mod selection, quality settings, 1080p,
runtime 10.0.11, VSync off and private cap 1000. 5931 frames over 30 seconds:

- CPU Draw-through-Present mean **5.0209 ms**, median 4.8320 ms,
  p95 6.2774 ms, p99 7.5236 ms.
- Mean Present interval **5.0609 ms**, rendered throughput **197.5930 fps**.
- Camera positions differ by approximately 0.00035 world units. Captures show
  the same doorway framing. Vulkan is about 7.3× slower in this saved view.

Both saved-view runs fall short of >240 fps. This is a different view from the
user's target screenshot and does not contradict a >240 fps DX11 result there.
Vulkan's capture is darker, its LCD is more clipped/overexposed, some particles
are absent, and Colorful Icons toolbar textures fail to load. These comparisons
do not certify equal rendered content/quality or visual parity.

Evidence:

- `Vulkan-cbe04fb041a54e3db407d06c8aff79eb/GameData/Reports/result.json`,
  `frame-profile.json`, `view.png`, `vulkan-status.txt`.
- `Dx11-faaa9be2ddd746c79c809901c2bdc245/GameData/Reports/result.json`,
  `view.png`, `vulkan-status.txt`.
- Both run roots contain `launch.json` with hashes and source identity.

Source SpaceEngineers.cfg and Current.xml retained their original modification
times (05:50:27 October 6 and 17:34:41 October 5, respectively). Both successful
benchmark child processes exited. The initial private attempts were abandoned
after detecting custom-source precedence and an XML writer's whitespace in an
empty string config field; the launcher fixes both and retains those logs.

## Completed structural pass

Implemented and retained:

- Instance packing caches the unchanged buffer revision, source, slice and HSV
  state. Bone packing and dither signs retain the stock representation. Material
  changes invalidate the affected state; repeated identical updates do not.
- Compatible opaque rigid parts use stock hardware instancing, with matching
  mesh, material, flags, cascade membership and a bounded spatial bucket.
  Actors consumed by highlights or active shadow exclusions retain individual
  draw identities. Logical parts and physical packets are reported separately.
- Scene transforms use parent-first dirty propagation around the scene updater.
  Cached bounds and conservative AABB-tree queries preserve main-view,
  off-camera shadow/probe and static-instance candidates. Movement, removal,
  visibility, geometry and instance-buffer changes invalidate the affected state.
- Native material descriptors invalidate only keys that reference replaced
  textures/arrays/voxel materials, after their frame slot completes. Per-frame
  probe descriptor reuse follows its pool lifetime. Prepared mesh/material
  references and sorted caster lists are shared across native passes.
- Object data uses one aligned native arena/upload, and instance de-duplication
  uses a reusable flat table. Queued native data owns its snapshot after managed
  buffers are modified. Arena sizes remain bounded.
- Static/dynamic shadow partitioning avoids invalidating frozen static maps for
  each bone update or movement of an already dynamic caster. Transitioning into
  animation removes the former static caster; settling restores it. Ignore-list
  changes, light-slot ownership and projection-up changes also invalidate the
  affected static map. An unchanged static light remains cached.
- Native composition, sprites, command completion, submit, Present, queue copy,
  geometry preparation, upload, descriptor and sort costs are now separate
  diagnostics. The extension is 84 bytes; the existing 112-byte work ABI remains
  unchanged. Optional profiler labels are disabled during normal timing runs.

The labels are diagnostic additions, not shader or quality changes. This pass
does not certify broader Vulkan visual parity.

### Kept/reverted experiments and live evidence

Same-process batching A/B:
`Vulkan-8513b36b6c244a20af46932bbec29a60` (741 frames on, 690 off).
Both phases retain full 1920×1080 world rendering and the same restored camera.
Mean CPU time was **40.4425 ms on / 43.4701 ms off**, a 3.0276 ms difference.
The on phase submitted approximately **13,674 physical packets for 18,701
logical parts**. The second phase ran later in simulation time, with a five-second
transition warmup; this is evidence for retaining batching, not a claim of an
equally sized improvement over every previous run.

An Immediate presentation preference and a per-frame sort-key reuse experiment
were **reverted**. Their combined run
`Vulkan-0b1822291b054b4099a48d9ce268ad85` averaged 44.1221 ms CPU and 11.1049 ms
Present. The Immediate experiment did not improve this workload, and sort reuse
missed as packet order changed while adding key construction cost. Mailbox
preference and ordinary sorting were restored. Presentation semantics are
defined by the [Vulkan specification](https://docs.vulkan.org/refpages/latest/refpages/source/VkPresentModeKHR.html).

After isolating the repeated static-shadow invalidation, run
`Vulkan-52b397b8b53545ca99ca63086665d4b5` sampled 800 frames:

- CPU mean **37.4767 ms**, median 35.6415 ms, p95 48.2308 ms, p99 57.8031 ms;
  Present interval 37.5085 ms, **26.6606 fps**.
- Approximately 18,690 logical parts, 13,660 physical packets, 5,340 main
  commands, 8,052 cascade commands, 1,529 spot commands and 198 probe commands.
  Spotlight commands fell from approximately 2,894 in the earlier on phase.
- Present averaged **9.6209 ms**, submit 0.0657 ms, finish 0.0077 ms,
  composition 0.0081 ms and sprites 0.0581 ms. The previous unexplained Native
  record remainder is predominantly inside the CPU Present call in this run.
- Native queue snapshot 2.7154 ms; preparation geometry 0.4111 ms,
  upload 0.6818 ms, descriptors 0.5428 ms, sort 1.0946 ms.
  Descriptor hits 13,659.185/frame, misses 0.4525/frame, pool resets zero.
- Final ten-second profiler: CPU 36.0675 ms, world GPU 8.4115 ms,
  Scene update 0.8440 ms, cull 1.6525 ms, instance pack 0.2881 ms,
  Materials 7.0986 ms, Queue world 5.1890 ms, Native record 16.1225 ms.

These stage reductions do **not** establish a net improvement over the earlier
36.7947 ms saved-view baseline. Materials and presentation costs increased, and
the world GPU interval also varies. The <4.1667 ms target is still unmet.
The last ignore-list/light-identity correctness additions have separate focused
GPU proof; the final full saved-scene run is recorded below.

### Final code: private Pertam result

Run `Vulkan-3b9a618f4b6e4adebc63b9ff10c4cc27` includes the ignore-list/owner
corrections and leaves profiler labels disabled. It sampled **852 frames** at
full 1920×1080 after 45 seconds of warmed gameplay:

- CPU Draw-through-Present **35.2170 ms mean**, 34.1340 ms median,
  41.4304 ms p95 and 45.8027 ms p99.
- Present interval **35.2475 ms**, throughput **28.3708 fps**.
- Native Present **7.3911 ms**, queue snapshot 2.7042 ms, geometry 0.4054 ms,
  upload 0.6884 ms, descriptors 0.5422 ms and sorting 1.0544 ms.
- Approximately **18,695 logical parts → 13,673 physical packets**;
  5,637 main, 8,059 cascade, 1,532 spot and 171 probe commands/frame.
  Descriptor misses 0.4261/frame, with no pool resets.
- Final ten-second window: CPU Frame 34.9004 ms; world GPU **6.5137 ms**;
  Scene update **0.7747 ms**, cull 1.8279 ms, instance pack **0.2912 ms**,
  Materials **7.4524 ms**, Queue world **5.2818 ms**, Native record 14.4645 ms.
  Slot wait 0.0327 ms and acquisition 0.0038 ms.

The mean is 1.5777 ms (4.29%) below the original 36.7947 ms private baseline.
This is a measured difference between separate runs, not a controlled causal
gain: Present alone varied by over 2 ms between the last two runs. Do not
attribute that difference to the final cache-identity fix or extrapolate it to
the screenshot's 52.47 ms view. The remaining CPU gap to 4.1667 ms is about
**31.05 ms**, or an approximately **8.45×** reduction in measured frame time.

The final capture retains the restored doorway framing. Lighting/reflections
and particle appearance differ between captures; the clipped LCD and unsupported
toolbar DDS issues remain. Full visual parity is still unverified. The game
exited, the source checkpoint hash stayed unchanged, and original config/profile
modification times are unchanged. Evidence is in this run's `launch.json` and
`GameData/Reports/{result.json,frame-profile.json,native-performance.json,view.png,vulkan-status.txt}`.

### Correctness/build evidence

- Release native build and four CTest cases passed: arena, ABI, GPU smoke and
  HWND presentation. The same cases also passed with profiling labels enabled.
- WorldPassGpuTests: **81 checks**, including visible HSV paint in all four
  batching variants, separate/batched pixel equivalence, source ownership,
  descriptor replacement and synchronization validation; zero validation errors.
- WorldGpuSmoke: real installed models and scene bridge, **46 mesh draws**,
  zero validation errors and zero scene resources after unload. Checks include
  dirty parent transforms, cached instance mutation, actor-specific exclusions,
  frozen-map ignore changes, owner replacement/restoration, unchanged static
  lights and animated dynamic overlays. The ignore-change check failed before
  its isolated fix and passed afterward.
- Earlier retained-code regression runs: PostprocessGpuTests 175 checks,
  ProbeGpuTests 103 checks and FrameProfilerTests passed. Probe tests advance
  synthetic time to respect the production five-second recapture floor.
- Both net48 and net10.0 Release builds passed and were deployed to the local
  Pulsar plugin folders. The source profile's Vulkan DevFolder remains Debug;
  custom-source precedence can override these DLLs during the user's normal
  launch. Private measurements explicitly load the copied Release DLL.

### Next priorities toward <4.17 ms

1. **Resolve Present and actual GPU work with Nsight.** A 7.39 ms CPU Present
   call already exceeds the entire target budget, while slot wait/acquire were
   small. This does not identify a shader bottleneck. Inspect GPU ranges,
   acquire-semaphore waits, queue scheduling and presentation pacing before
   changing synchronization. The world timestamp starts before the world
   commands and its submission waits on acquisition at color-attachment output;
   elapsed GPU timestamps can include waits. Uploads, final composition and HUD
   are outside the world region. Attribute a complete frame, not just this row.
2. **Measure managed material/packet preparation.** Materials 7.45 ms plus
   Queue world 5.28 ms is about three entire target budgets. Split batching key
   lookup, constant updates, skin palette construction and managed arena copies
   before the next change. A reusable per-part batch handle could avoid repeated
   key hashing, provided texture/tint/flags/cascade/bucket changes invalidate it.
   Current QueueWorld already pins two arenas rather than each individual part;
   investigate its copies, second traversal and native snapshot rather than
   reintroducing per-part pinning.
3. **Reduce cross-pass packet/command work with proof.** Cascade recording still
   emits roughly 8,059 commands. Investigate revision-driven caster membership
   and compatible depth instancing/indirect submission after tracing the pass.
   Keep off-camera casters, alpha testing, animation, exclusions and current
   shadow quality. Indirect submission alone does not remove GPU geometry work.
   The rejected sort-reuse approach should not be reintroduced without a stable
   ordering/invalidation scheme and measured net benefit.
4. **Retain the warm descriptor cache and prioritize larger costs.** Removing
   every measured descriptor preparation operation saves at most about 0.54 ms
   in this run; it cannot explain or close the roughly 31 ms CPU gap. GPU shader
   and pass-specific recommendations await a valid trace. Visual-content parity
   and the exact screenshot camera remain prerequisites for claiming DX11 parity.

Nsight Systems **2026.5.1.161** is installed. The attempted capture
`Vulkan-1741daee5bae492ab952b000494ec840` failed **before game launch**, with
“Failed to register Vulkan extension JSON file(s). This operation requires
registry writing permissions.” No trace or profiler result was produced.
The desktop shell is not elevated; NVIDIA's [Windows CLI guide](https://docs.nvidia.com/nsight-systems/UserGuide/index.html)
requires administrator execution. `Capture-Nsight.ps1` now prepares the same
private lane, starts ten-second collection after warmup and requests Vulkan
API/annotation/GPU workload data. Its syntax and markers are validated; actual
elevated trace generation was unverified at this point (superseded below). See the harness README for the
capture command. The launcher now rejects non-elevated capture before copying
files. No Nsight finding has been invented from this failed attempt.

Concrete command for the known saved scene, from elevated PowerShell in this
repository (only the private copy is loaded):

```powershell
./Tests/ScenePerformanceHarness/Capture-Nsight.ps1 `
  -World 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Profile/Saves/76561197961268393/Pertam Orbiter 2026-08-30 07-22' `
  -UserData 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Profile' `
  -Bin64 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' `
  -Pulsar 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Pulsar'
```

## Continued goal: Nsight and texture parity

The <4.1667 ms and DX11 visual-match requirements remain active. The following
diagnostic and parity results do not close either requirement.

With explicit user authorization for Windows UAC, the elevated private capture
`Vulkan-22ba31f8af3c4068ba6764fef7df7f7d` produced `nsight.nsys-rep` and
`nsight.sqlite`. Collection started after the private warmup and lasted about
10.012 seconds. The trace includes 304 Presents, Vulkan API events and CPU/GPU
debug labels. Its initial API summary also includes startup state records with
negative timestamps; use the 0–10 second filter for steady-state findings.

The native Present average under injection was approximately **0.266 ms**,
versus several milliseconds in uninstrumented runs. World GPU timing under
injection was approximately 20.39 ms in the final gameplay window. Neither is
an uninstrumented performance result. The camera also moved about one world
unit relative to the previous saved-view measurement, preventing a matched
frame-time comparison with that run.

Nsight reports “Not all Vulkan events might have been collected.” GPU marker
coverage is uneven: 303 frame-begin ranges, 290 spot ranges, but only 35 cascade
and 41 GBuffer geometry ranges. These ranges cannot establish the complete
normal-frame GPU budget. `Summarize-Nsight.py` filters the collection window,
retains diagnostics and emits marker coverage with
`PassAttributionComplete=false`. Its `nsight-summary.json` and the CLI's
`steady_*_start=0_end=10000000000.json` summaries are retained in the run folder.
The marker report's percentages apply to recorded range totals, not total
frame wall time, as described in [NVIDIA's analysis guide](https://docs.nvidia.com/nsight-systems/AnalysisGuide/index.html).

### DXT4 toolbar parity correction

The unsupported FourCC 877942852 is **DXT4**, which is BC3 with premultiplied
alpha. DXT2 similarly maps to BC2 with premultiplied alpha. This follows
[Microsoft's DDS mapping](https://github.com/microsoft/DirectXTex/wiki/DDS-I-O-Functions)
and the installed game's DDSHelper mapping. The DDS reader now accepts both
legacy forms, retains their compressed mip bytes and alpha-mode metadata, and
recognizes DX10 premultiplied metadata. Compressed GUI input is passed through;
raw GUI preprocessing retains the installed MyFileTexture behavior.

The reader tests cover DXT2/4 format, compressed byte/mip preservation and DX10
alpha metadata. SpriteGpuTests loads a DXT4 file through the production cache
and compares all three blended GPU channels against the BC3/premultiplied
reference. Existing installed BC7 font-edge and mip checks also pass, with zero
core/synchronization validation errors.

Private run `Vulkan-8043c09b93244b8c9fdb6d9a4a9eb4e4` visibly restores the toolbar
icons in `Reports/view.png`; its status report no longer contains the prior
unsupported legacy DDS failures. Resident textures increased from 2312 to 2320.
This is a demonstrated content/parity fix, not certification of overall visual
parity. The LCD clipping and broader lighting/particle matching still need work.

### CPU preparation diagnosis

That run also enables `-CpuDetail`, with additional clock reads. Its 40.7808 ms
CPU average over 735 frames is an **instrumented diagnostic**, not a regression
benchmark. `packet-preparation.json` reports per-frame averages:

- Rigid lookup **1.8575 ms**; append **1.5646 ms**.
- Managed arena packing **2.2265 ms**; native queue call **3.4487 ms**;
  auxiliary queues 0.1583 ms; upload finalization 0.0277 ms.
- Cached part hits 16,821.18; refreshes 1.75; skinned parts 9;
  newly created rigid groups 47.17; resident groups 21,792.75.
- Object source bytes 1,809,842 and instance bytes 2,057,168 per frame.

This supports reducing repeated grouping lookup and packet construction. It
does not support changing shader quality or reducing scene content. The next
bounded experiment is a full-key-validated per-part group hint, with cache epoch
invalidation, no retained retired instance arrays, GPU equivalence tests and a
same-process uninstrumented A/B run.

### Retained per-part batch hints

Run `Vulkan-1851cda2ad4a451b8463fccf0b81a5fc` compares hints on/off with rigid
batching retained throughout. Both phases are uninstrumented, full 1080p,
validation off, with the restored camera positions within about 0.00089 units:

- Hints on: **34.1080 ms** CPU mean, 32.9448 median, 40.5214 p95, 47.4444 p99;
  interval 34.1374 ms, **29.2934 fps**, 880 sampled frames.
- Hints off: **34.3437 ms** CPU mean, 33.4608 median, 39.9240 p95, 44.1209 p99;
  interval 34.3708 ms, **29.0945 fps**, 873 sampled frames.
- Final ten-second Materials row: **6.7958 ms on / 7.6082 ms off**. The mean
  full-frame difference is only **0.2356 ms (0.69%)**. The later phase has less
  main/spot recording work and different simulation time; this single A/B does
  not prove a repeatable whole-frame speedup. Tail latency did not improve.

The retained hint stores the full grouping key, backend owner, cache epoch and
slot index. A key mutation or cache-budget reset returns to the ordinary lookup.
It does not hold a reference to retired instance arrays. Geometry, textures,
flags, cascade membership, spatial buckets and physical grouping are unchanged.
WorldPassGpuTests now passes **84 checks**, including visible paint across
variants, spatial/visibility key changes and a real 32768-entry budget reset
mixing a stale hint with a fresh lookup. Core/synchronization validation errors
remain zero. The scene bridge also passed 46 mesh draws with zero errors and
zero scene resources after unload.

### Next evidence-backed investigation: memory pressure

During the loaded private run, `nvidia-smi` reported **15,529 MiB used of
16,303 MiB**, with 469 MiB free and 23% GPU utilization. This is system-wide
device memory usage, not an allocator attribution to the plugin. A loading-stage
sample was 2,921 MiB used. The scene's status reports approximately 15 GB of
lifetime uploaded bytes; uploaded bytes are not resident bytes.

Source inspection finds that VulkanModelCache uploads the full model vertex
array separately for every material part. Measure the duplicated geometry
bytes before deciding how much this contributes to memory pressure. A candidate
fix is a lossless per-part vertex/index remap, preserving triangle order,
attributes, bone data and all draw ranges. Verify that active shader variants
do not require the original SV_VertexID, and test main/depth/highlight/skin paths.
No geometry compaction, resolution reduction, content pruning or quality change
has been made at this point.

## Lossless material-part geometry compaction

The retained uploader now remaps each material part to its referenced vertices.
It copies the complete 80-byte attributes, retains index/triangle order and
keeps original CPU indices for sections, highlights and skin palette construction.
It does not remove triangles, change LOD, reduce resolution or replace shaders.
Runtime models use the same path; skin partitions retain their existing palette
remap. `-ExpandedGeometry` reproduces the former upload for a comparison run.

Vertex remapping changes the shader's VertexIndex value, as defined by the
[Vulkan interface contract](https://docs.vulkan.org/spec/latest/chapters/interfaces.html).
The installed VertexTemplateBase accesses vertex-index-driven data only inside
USE_MERGE_INSTANCING. `Verify-GeometryContract.py` verifies that all **32 selected
stock vertex variants** and extra world definitions exclude that path.
WorldPassGpuTests passes **93 checks**, including full attribute/bone byte
equivalence and identical expanded/compacted rigid and skinned GPU pixels.
The scene bridge passes 60 mesh draws, including 13 skinned draws and
depth/highlight/animation cases, with zero validation errors and zero scene
resources after unload. The lifetime fixture now supplies the installed
astronaut's complete identity pose; creation alone had exercised its base mesh
and incorrectly assumed skin buffers existed. All five logical geometry byte
counters are zero after clear, including 1,628,240 skin vertex bytes and 299,388
skin index bytes owned before clear. The Release build is deployed to both
Pulsar Local runtime folders; the user's existing Debug source profile remains
unchanged and can still take precedence on a normal launch.

Two full 1080p uninstrumented private Pertam runs use the same DLL/native hashes,
save/profile and quality. Camera positions differ by approximately 0.00038 units:

- Expanded: `Vulkan-f55d2cf3690c4f288b47e22f78ca5d41`, 933 frames;
  CPU **32.1281 ms**, median 31.2192, p95 37.7273, p99 43.7609;
  interval 32.1591 ms, **31.0954 fps**.
- Compacted: `Vulkan-7f9b65694d4d489db9872b8d45183962`, 1033 frames;
  CPU **29.0073 ms**, median 28.2893, p95 33.5175, p99 37.8898;
  interval 29.0373 ms, **34.4384 fps**.

The measured mean difference is **3.1207 ms (9.71%)**. These are separate runs
with simulation/presentation variance; the result supports retaining the change,
but is not a repeatability guarantee. Final ten-second world GPU timing was
**6.3243 → 4.7747 ms**. Mean native Present was **7.3679 → 5.7856 ms**. Materials
was 5.3142 → 4.9185 ms and Queue world 5.1468 → 4.7239 ms in those final windows.

`geometry-storage.json` measures current logical base vertex storage:
**9,189,054,000 → 1,062,031,920 bytes**, saving **8,127,022,080 bytes (88.44%)**.
Base index bytes stay **120,769,452** in both; skin vertex/index bytes remain
1,244,720/212,376. Both report 6955 actors, 28876 instances, 1453 models and
8605 resident meshes. Logical parts remain approximately 18,701/frame and
physical packets approximately 13,679; the optimization preserves the workload.

A system-wide device sample while compacted gameplay was sampling reported
**9,289 MiB used, 6,709 MiB free, 22% GPU utilization**, versus roughly
15,509–15,529 MiB used in the expanded lane. Those readings are observations from
`nvidia-smi`, not per-plugin allocation attribution. The sample collection
attempt ran after exit, so there is no persisted `device-memory.json` for these
readings. Logical geometry bytes and their exact reduction are persisted and
do not rely on that device-memory estimate.

The view captures retain doorway geometry and restored toolbar content. Their
lighting, reflections and particles differ with capture time; they are not a
pixel-identical scene parity proof. The LCD remains clipped and full DX11 visual
matching is still unverified. The current CPU frame target remains unmet by
approximately **24.84 ms**; even the measured world GPU region remains above
4.1667 ms and excludes final composition/HUD/uploads.

Next performance evidence should separate remaining presentation pacing from
the roughly 4.9 ms material and 4.7 ms queue costs, then test persistent/delta
packet preparation without changing scene or shadow coverage. The incomplete
Nsight marker capture still cannot justify a shader-quality reduction or certify
a complete GPU pass budget.

## Compacted CPU preparation diagnostic

`Vulkan-7b02f2b0a83d4358a6da737b0c2c7ce6` uses the same retained DLL/native
hashes with `-CpuDetail`, 940 sampled frames after 45 seconds of world warmup.
It reports CPU 31.9260 ms and a final-window world GPU region of 4.6133 ms.
Extra hot-path clocks make this a diagnostic, not a replacement FPS benchmark.

Across the sample, rigid lookup is 1.0275 ms, rigid append 1.1682 ms, managed
packing 2.0516 ms, native queue call 3.0618 ms, auxiliary queues 0.1545 ms and
upload finalization 0.0218 ms. These belong to existing parent sections; do not
add them to the frame total a second time. Native's internal queue timer is
2.6381 ms, so the call includes approximately 0.4236 ms outside that region.
There are approximately 16,823 cached parts, 1.77 part refreshes, nine skinned
parts, 15,892 batch hint hits and 34.5 newly created groups per frame.
Every frame still packs roughly 1.81 MB object constants and 2.06 MB instances.
Material descriptor hits are 13,657/frame, misses 0.82, resets 0.0021.
Further descriptor caching alone therefore does not target the dominant work.

`device-memory.json` preserves a system-wide sampling observation of 9,379 MiB
used/6,619 MiB free. This includes other applications and is not plugin-owned
allocation accounting.

The next candidate is persistent CPU packet storage with explicit per-part and
resource revisions, measured separately from GPU upload changes. The current
`QueueWorld` builds and traverses all draw records, copies object/instance bytes,
then native `queue_world` validates all referenced meshes/material textures and
copies them into owned storage again. A candidate must preserve camera-relative
transforms, visibility membership, per-light exclusions, material replacements,
model replacement, skin animation, travel/cache eviction and world reload. Native
storage must own every byte beyond the synchronous call; managed pinned pointers
cannot persist. Each frame slot must still wait for GPU completion before reuse.
An implementation should compare full pixel output and draw coverage against the
current path before a same-scene uninstrumented benchmark can justify retention.

Separately, use submission-level Nsight tracing to investigate the remaining
roughly 5.6 ms Present API duration. The new `-NsightGpuWorkload batch` lane
traces Vulkan API/submission batches with application labels disabled. It avoids
requesting individual GPU workload events, but still changes runtime behavior
and cannot attribute per-pass shader costs. Capture completeness and submission
coverage must be checked before making a GPU bottleneck claim.

## Submission-level Nsight follow-up

The elevated `batch` lane completed as
`Vulkan-d815e7fafa7b41c6920b67b1ba8632bd`, producing a 30.6 MB report and
88.8 MB SQLite export. It uses the retained plugin/native hashes, full 1080p
and the private copied save, then exits unsaved. The capture window is
10.0160 seconds: 382 recorded Present calls, 442 recorded QueueSubmit2 calls
and 442 GPU submission batches. Mean CPU Submit2 duration is 0.0679 ms;
mean recorded GPU batch duration is 4.0152 ms, range 0.0063–6.9186 ms.
These batches include submissions of different sizes, so their mean is not a
complete rendered-frame GPU budget. Per-pass labels are deliberately disabled.

The collector again reports **not all Vulkan events might have been collected**.
`nsight-summary.json` retains the warning and `PassAttributionComplete=false`.
Matching recorded submit/batch counts does not establish completeness of every
API event or rendering pass. Present API mean is 0.2022 ms under injection,
versus approximately 5.6 ms in the CPU diagnostic without Nsight. The captured
process reports CPU 26.7080 ms, but that cannot supersede the **29.0073 ms
uninstrumented** result or establish a performance improvement.

This capture exposed a harness metadata issue: its original `result.json`
inferred instrumentation from CPU-detail clocks or application labels. Since
the batch lane disables both, that field incorrectly says false. The original
file is retained; `result.corrected.json` separately records true, the original
value and correction reason, supported by `nsight-launch.json` and the trace.
Future launches explicitly propagate the profiler instrumentation flag.
The updated harness builds successfully. The save checksum, original settings
and original Pulsar profile remain unchanged.

## Managed packet offset reuse

`QueueWorld` now stores bounded object/instance offsets in its scratch records
while packing. After pinning the arenas, it resolves those offsets in place.
This removes the second instance-array dictionary lookup and the second large
packet-structure read for every draw. Native receives only final pointers and
continues copying them into owned storage synchronously; no pinned address is
retained across calls. The shader, native ABI, GPU storage, draw ordering and
resource lifetime remain unchanged.

All 93 WorldPassGpuTests checks pass, including shared instance streams with
different object constants, source mutation after queueing, real skin palettes,
depth partners, all 20 material variants, resizing and world unload. Native
core/synchronization validation reports zero errors. Both net10.0 and net48
Release builds pass and are deployed to their respective Pulsar Local folders.

The private full-1080p `-CpuDetail` run
`Vulkan-57735af5a0bd431e9fd4c7ba0ea8302b` samples 968 frames. Managed packing
mean is **1.4058 ms**, versus **2.0516 ms** in the prior compacted diagnostic:
a measured **0.6458 ms (31.48%)** reduction in that section. Native queue-call
mean is 2.9983 ms; rigid lookup/append is 0.9640/1.0946 ms. Object bytes remain
approximately 1.815 MB, instance bytes exactly 2,061,120, cache hits approximately
16,823 and batch hint hits approximately 15,894/frame. CPU mean is 30.9946 ms,
versus 31.9260 in the earlier diagnostic. These are separate instrumented runs;
their total-frame difference includes run variance and cannot establish an
uninstrumented FPS gain.

Next native inspection confirms world, spotlight and probe command recording
already caches pipeline/descriptor/buffer binds. Repeating that optimization
would not target a missing behavior. The outstanding native queue work still
validates every packet and copies its object/instance bytes into owned storage.

Two subsequent full-1080p runs without CPU-detail clocks report:

- `Vulkan-b6795f09d95a4f5694a69db5efa214f9`: 1278 frames, CPU **23.4399 ms**,
  median 22.5549, p95 28.1636, p99 38.1537, interval 23.4804 ms,
  **42.5886 FPS**. Mean Present API duration is 0.1885 ms.
- `Vulkan-431d985d9f5f488aa051e9e011f890c5`: 1267 frames, CPU **23.6478 ms**,
  median 22.8594, p95 28.4345, p99 33.7374, interval 23.6885 ms,
  **42.2146 FPS**. Mean Present duration is 0.1900 ms. This repeat explicitly
  suppresses installed Nsight regular/systemwide/Reflex layers in the private
  child's environment. `module-inspection.json` records 335 loaded modules
  during ready gameplay, no ToolsInjection DLL and no profiler modules. Its
  final ten-second world GPU region is **4.4428 ms**.

The larger difference from the earlier 29.0073 ms frame measurement is mostly
Present pacing changing from approximately 5.8 to 0.19 ms. Main-view command
counts also vary (approximately 5,370 and 5,630 in these runs, versus 6,140 in
the earlier compacted run). The **0.6458 ms diagnostic packing reduction** does
not explain the entire frame difference. These runs establish observations for
the current implementation, not a causal claim that offset reuse saves 5.6 ms.
The second run rules out an active Nsight injection DLL in the observed ready
process snapshot. No registry setting, original save or original user profile
was changed to suppress it. Future launcher runs persist those process-local
controls in `instrumentation.json`.

Both retain approximately 13,668 physical packets, unchanged logical geometry
storage and the saved doorway camera. The repeated capture still shows the
unresolved bright LCD and lighting mismatch; full DX11 parity is unverified.
The current repeat is approximately **19.48 ms** above the 4.1667 ms CPU frame
budget. Its world GPU region alone exceeds that budget and excludes composition,
HUD and uploads. The goal remains unmet.

## Conservative spotlight sphere/cone overlap

Before widening rigid groups, inspection found an under-conservative spotlight
caster rejection. The old test used `along*tan(halfAngle)+radius` as its radial
limit. The signed side-plane distance is instead
`radial*cos(halfAngle)-along*sin(halfAngle)`, so the sphere's horizontal margin
must be `radius/cos(halfAngle)`. For a 60-degree half-angle, centre radial=20,
along=10 and radius=2.5, the old limit is 19.82 and incorrectly rejects a sphere
only approximately 1.34 units from the cone side plane.

`spot_bounds.h` now supplies the production range/apex/cone test with the correct
margin, preserving unbounded-radius handling and the existing conservative
fallback for very narrow/wide aperture values. The managed caller normalizes
the light direction before queueing. Packet grouping remains at 16 metres;
this change preserves edge casters rather than reducing scene coverage.

The new native CTest exercises the concrete edge case, outside-range/cone/apex
cases and 100,000 visible witness points with containing spheres and larger
enclosing batch spheres. All five canonical native tests pass. The build script
now builds the new target before invoking CTest on both its Windows and Linux
branches; the Linux branch has not been executed in this pass.
PostprocessGpuTests passes **176 checks**, including the real production queue
retaining a triangle whose sphere reaches the cone edge in a spotlight shadow
command. This checks submission coverage, not a full scene pixel comparison.
Its existing cookie, shadow quality, exclusions, composition and resource tests
also pass, with **zero core/synchronization validation errors**.

The native Release library is deployed along with both managed runtime builds.
The new native SHA256 is
`4AB44D5D1C27F401F24759EC2B4279BC69848BF88D15D2360B23F0BEA216D729`.
This is a parity prerequisite for larger conservative batches, not a claimed
performance improvement. More correct edge caster coverage can add shadow work.

Regression discrimination is verified against the preserved pre-fix native
library from `Vulkan-431d985d9f5f488aa051e9e011f890c5`: the same GPU suite
fails specifically on the new cone-edge submission assertion. Its hash is the
old `E7D623FB365AB57C1DBB837AC867353AD71AD25CC102EF16D36FE18814E356C8`.
`SpotBounds-Baseline.log/json` records this expected failure; the corrected
library rerun records the passing assertion in `SpotBounds-Corrected.log`.

The corrected full-1080p private run
`Vulkan-c2334c7543e349d998cf3eda6c9649fa` samples 1272 frames, 45-second
warmup/30-second sample, profiler layers suppressed: CPU **23.5600 ms**, median
22.7456, p95 28.6517, p99 33.7966, interval 23.6029 ms, **42.3678 FPS**.
Mean Present is 0.1916 ms and spotlight commands approximately 1599.9/frame
(preceding run 1533.9); main-view commands also change from approximately 5629.5
to 6131.2. Final-window world GPU timing is **4.8770 ms**, versus 4.4428 in
the preceding run. Simulation/occlusion/particle variance and the changed
coverage prevent attributing that entire GPU difference to the corrected cone
test. No performance gain is claimed for this parity fix. The CPU and GPU frame
targets remain unmet, full scene DX11 visual parity remains unverified, and
wider rigid grouping has not yet been implemented.

## Retained 64-metre rigid grouping

Rigid grouping now uses 64-metre cells by default. The cell width is included
in the key and hint validation; pipeline, mesh, textures, tint, emissivity,
dither, visibility/dynamic-shadow flags and cascade masks remain distinct.
The merged sphere continues enclosing the authoritative source spheres.
Highlight and per-light exclusion identity rules remain unchanged. No triangles,
attributes, shader features, output resolution or quality settings are removed.
`SE_VULKAN_WIDE_RIGID_BATCHES=0` or harness `-NarrowBatches` selects the former
16-metre path. The new default is deployed to both Pulsar Local runtime folders.

WorldPassGpuTests passes **102 checks**: exact wide/narrow GPU pixels for actual
transformed geometry across a former cell boundary, merged-sphere containment,
unknown-bound preservation, visibility/cascade separation, mode/hint changes,
cache eviction, all material/depth paths and native ownership. The production
scene bridge also passes 60 mesh draws in wide mode, including highlights,
per-light exclusions and animation, with zero core/synchronization validation
errors and zero scene resources after unload.

Two full-1080p uninstrumented same-process comparisons use the same plugin/native
hashes, saved camera, quality and copied save. Both retain 18,701 logical draws
per frame, identical logical geometry storage and full 1920x1080 world/output
dimensions at fraction=1:

- `Vulkan-c4e0026bccd74de3bea415eacae0e4df`, 64 then 16: CPU
  **22.2577 vs 24.7338 ms**, approximately 10.0% lower with 64 m. Physical
  packets **10,806.05 vs 13,665.50**, approximately 20.9% fewer. Final-window
  world GPU timing **4.6280 vs 4.6318 ms**.
- `Vulkan-3766c78836bb49a68bdf071e3fc06026`, 16 then 64: CPU
  **23.2876 vs 21.9703 ms**, approximately 5.7% lower with 64 m. The 64 m
  sample contains 1363 frames, median 21.2565, p95 26.3693, p99 31.0354,
  interval 22.0134 ms and **45.4269 FPS**. World GPU timing is
  **4.4680 vs 4.6415 ms**. Physical packets in the 64 m sample average
  10,847.61. Present remains approximately 0.19 ms in both treatments.

Each initial phase warms for 45 seconds and samples for 30; the later phase
settles for five seconds and samples for 30. Reversing order reduces the
ordering ambiguity, but simulation, occlusion and particle differences remain.
No consistent GPU improvement is claimed. Initial live view captures differed
in upper-wall lighting, so a paused diagnostic was required before retention.

## Paused grouping comparison and metadata ownership

The harness now captures repeated 16/64/16 views of the private paused save,
waiting for file completion before switching modes. A first capture lacked
actual per-frame grouping counters (`Vulkan-bed1aca42a0048aabda9a045e4ad5b47`).
A second (`Vulkan-4b0b01f75ec942ddae84bb3970f690a5`) read logical counters from
the update thread during render-thread packet construction. Its partial logical
values are rejected by the verifier; it is not coverage evidence. The original
metadata is retained. These findings did not change the renderer's draw data.

The corrected harness publishes immutable completed-frame evidence from
`EndPresent`. `Vulkan-05c7e24d4bbb4172a5c39a7ec289d068` verifies actual
packets **13,744 → 10,802 → 13,744**, logical draws **18,690 in every capture**,
identical camera strings and full 1920x1080 world/output dimensions. Paused
logical coverage differs from live coverage; equality is asserted within the
paused sequence only. `Summarize-BatchPixels.py` rejects absent/mismatched mode,
coverage, camera and resolution evidence and emits `pixel-delta.json`.

Full-image 16 m → 64 m RGB differences exceed four channel levels in
**0.001302%** of pixels, versus **0.002074%** between the earlier and returned
16 m images. Mean absolute channel differences are 0.000178 and 0.000245,
respectively, on the 0–255 scale. The selected upper-wall region differs by
one channel level in one pixel between modes, matching its same-mode return
difference. The paused comparison supports retaining grouping without the
large lighting difference seen between live captures. It is not a proof of
bit-identical frames, every scene, or full DX11 visual parity. Existing LCD,
lighting and particle-parity issues remain unresolved.

The best wide CPU sample remains approximately **17.80 ms above** the
4.1667 ms frame budget; its measured world GPU region also remains above it.
The original save checksum, settings and Pulsar source profile remain unchanged.
The full performance and parity objective remains unmet.

## Managed render-thread sampling and voxel lookup investigation

The prepared elevated CPU-only Nsight capture did not execute: Windows returned
`operation was canceled by the user` for the UAC launch, including the latest
authorized retry. No CPU Nsight artifact or success is claimed. The existing
GPU traces remain subject to their previously recorded event-loss limitations.

A private non-elevated capture with the official local dotnet-trace tool completed
in `Vulkan-bb2a397931404a6f87a9eb3c218f1add`. It uses the .NET 10
`dotnet-sampled-thread-time` profile for ten warmed seconds. This estimates
thread wall time, including waits and native boundaries; it is not CPU utilization
or uninstrumented FPS. See the [Microsoft tool documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).
`Summarize-Managed.py` verifies balanced, monotonic event stacks and identifies
the render thread by `MyVulkanRender.Draw_Patch` rather than ranking all worker
threads together. Its durable `managed-summary.json` keeps inclusive samples,
pseudo leaves, real boundary callers and voxel constant-buffer caller chains
separate. Inclusive entries overlap and must not be summed.

Render thread 7904 has 10,042.85 ms of covered thread time. The inclusive
Draw patch accounts for 7,328.35 ms, with Scene.DrawWorld 3,261.08 ms,
NativeBackend.QueueWorld 1,737.55 ms and VulkanVoxels.Draw 616.75 ms.
The Present patch accounts for 2,705.79 ms and includes native EndFrame work;
it does not mean vkQueuePresentKHR alone consumed that time. Voxel drawing
contains 266.31 ms in ReflectedConstantBuffer.Locate, inside 314.71 ms in
Write. These samples support investigating pre-resolved voxel constant fields.
PollGC/native-boundary samples do not establish a garbage-collection bottleneck
or attribute native functions. The instrumented full sample is 22.2419 ms CPU,
44.8798 FPS over 1346 frames; no performance gain is inferred from it.

Voxel material lookup now reuses exact triple-to-group results while preserving
the existing first-containing-atlas selection. Successful group creation/removal
invalidates the cache, including updates, eviction and unload. The cache records
the actual group key so its visible-frame pin and usage epoch remain correct.
Atlas content, selected tier and residency budget are unchanged. The legacy
lookup remains available through `SE_VULKAN_VOXEL_MATERIAL_CACHE=0` and harness
`-LegacyVoxelLookup`. VoxelGpuTests passes 47 checks with zero validation errors,
including repeat lookup, identical cached/legacy selection, material replacement,
new group identity, retained 256-pixel tier and reference cleanup on unload.
A new `-CompareVoxelLookup` mode toggles only that treatment on the render thread
in one private process, excludes transition frames, and reports the actual mode.
Its uninstrumented performance result is recorded below when available.

### Retained voxel material lookup cache

Both full-1080p same-process comparisons use plugin F45CDF0997E23752… and
native 4AB44D5D1C27F401…, with no CPU detail or profiler injection:

- `Vulkan-6d94f2c4876e4653ac99c5a61bd8eb07`, cache then legacy:
  CPU **21.8857 vs 23.1350 ms**, 5.4% lower with caching. Rendered FPS
  **45.6116 vs 43.1455**. Live logical draws average 18,690 vs 18,697.36;
  packets 10,819.97 vs 10,856.62. Final-window world GPU 4.4958 vs 4.6629 ms.
- `Vulkan-7c56239c3356489aaab93ac70d60b16f`, legacy then cache:
  CPU **22.0274 vs 21.0075 ms**, 4.6% lower with caching. Cached sample:
  1426 frames, median 20.2765, p95 24.6142, p99 28.5055 ms, interval
  21.0494 ms and **47.5072 FPS**. Both phases retain 18,701 logical draws;
  packets 10,841.69 vs 10,832.74. Final-window world GPU 4.8287 vs 4.8297 ms.

Every phase retains full 1920x1080 world/output at fraction=1. Reversed ordering
supports retaining the CPU optimization; live simulation/particle/occlusion
variation prevents treating every difference as caused by the cache. No GPU
speedup or full DX11 parity is claimed. The best cached CPU sample is still
approximately 16.84 ms above the 4.1667 ms budget.

## Pre-resolved voxel object constants

The managed trace's repeated field-lookup evidence led to a bounded specialized
writer. It resolves the same twelve paths through reflection once per immutable
shader metadata object and validates float type, exact width and block bounds.
Each packet still gets fresh zeroed bytes. Matrix rows, voxel offset/scale/LOD,
dither and planet spherization are written from the current draw; actor or camera
state is not cached. Offsets are not hard-coded and there is no native ABI change.
The reflected path remains selectable through `SE_VULKAN_VOXEL_OBJECT_CACHE=0`.

VoxelGpuTests passes **51 checks**, including 1024 varied single/multi-material
object blocks that match the old writer byte-for-byte including padding, rejection
of an incompatible reflected field type, exact rendered pixels between writers,
LOD movement, dither, mining, distant spherization and unload. The initial run
omitted the repository's validation-layer path and failed the enabled-layer
assertion despite passing byte/pixel checks; it is not validation evidence.
The corrected canonical run explicitly uses `Native/build/validation/Bin`, passes
with zero core/synchronization errors and returns resources to fallback baseline.
See `TestResults/VoxelObjectConstants-Validated-Gpu.log`.

Two uninstrumented full-1080p comparisons support retaining the writer:

- `Vulkan-ac200bf353864ac2af90a89acf9f3db7`, pre-resolved then reflected:
  CPU **21.2514 vs 21.6903 ms** (2.0% lower). Final-window voxels/flares
  **0.7505 vs 1.6322 ms**; world GPU 4.8052 vs 4.6669 ms. Live logical draws
  average 18,701 vs 18,692.22, packets 10,831.64 vs 10,840.28.
- `Vulkan-2fc569cc00434a37be7d520a4be6f40f`, reflected then pre-resolved:
  CPU **22.7607 vs 21.5892 ms** (5.1% lower). Final-window voxels/flares
  **1.7167 vs 0.7776 ms**; world GPU 4.8123 vs 4.8540 ms. Both phases retain
  18,701 logical draws; packets 10,853.61 vs 10,832.42. The pre-resolved sample
  has 1387 frames, median 20.7756, p95 25.7797, p99 29.1420 ms, interval
  21.6314 ms and 46.2292 FPS. Native recording remains 5.9110 vs 5.9590 ms;
  Present remains approximately 0.19 ms.

Plugin SHA256 is `6D77FCF5E6B57D24982EE440694B15F6E20A3F4CAF83C680FE9CD7F8B511035F`;
native remains `4AB44D5D1C27F401F24759EC2B4279BC69848BF88D15D2360B23F0BEA216D729`.
All phases have world/output 1920x1080 and fraction=1. Dynamic scene variation
remains; neither separate run minima nor GPU-window variation are causal gains.
The lower voxel phase in both orders supports the field-lookup hypothesis.
`dotnet build Vulkan.sln -c Release` passes without warnings/errors and deploys
net48/net10.0 to the Legacy/Interim Local folders. Original save checksum,
settings and source profile remain unchanged. The full goal is not achieved.

### Next evidence-led investigations toward 4.1667 ms

The cached lookup run's final ten-second window assigns approximately 5.90 ms
to native recording, 4.90 ms to material/part submission, 3.64 ms to queue packing,
1.77 ms to actor culling and 1.70 ms to voxels/flares. These are owning CPU phases,
not GPU pass times; the constant-writer treatment directly targets the last one.
They establish the next priorities rather than a promise that removing one phase
will achieve the total budget.

1. Separate native queue validation/hash lookups from actual copies using an
   opt-in diagnostic. QueueWorld currently checks mesh and four texture handles
   per draw before copying object bytes and deduplicating instance streams.
   Its measured native portion is approximately 2.06 ms. Any cached validation
   must preserve replacement/unload contracts and native ownership before the
   managed arenas are unpinned; dropping validation or delaying copies is not
   an accepted optimization.
2. Profile material/part and glass packet preparation by actual packet population.
   The managed trace contains AddGlassPacket and fixed-array marshaling samples,
   while stock opaque parts already have revision-based preparation caches.
   Count cache refresh causes and allocations before adding another cache. Keep
   skinning, construction/dither, highlight identities and light exclusions intact.
3. Obtain native CPU function attribution and reliable GPU pass timestamps.
   Native recording still replays thousands of main/shadow/spot commands.
   Existing world GPU timestamps around 4.5–4.8 ms already exceed the entire
   target and exclude uploads, composition, HUD and presentation. Complete GPU
   pass attribution is necessary before selecting shader or shadow work.

The former packet-order sort-reuse experiment remains rejected: changing packet
order defeated reuse and key reconstruction added cost. A future sorting change
needs its own bounded experiment and equivalence proof. No lower resolution,
reduced geometry, shader quality or shadow coverage is proposed as a shortcut.
The user profile still has `DevFolder/DebugBuild=true`; private measurements use
an explicit Release Local plugin. Deploying Release Local DLLs alone does not
prove that a normal source-profile launch uses them.

### October 7 packet-owner follow-up

The completed opt-in CPU-detail run `Vulkan-3eed9e463a754ac0b6f9ffc7766c8229`
uses both retained voxel changes at full 1080p. Across 1100 sampled frames:
part-cache hits average 16,823.38, refreshes 1.62, skinned parts 9, batch-hint
hits 15,941.03, new batches 5.35 and resident batches 23,651.29. This rules out
widespread warm material-cache rebuilding as the primary next hypothesis.
Diagnostic rigid lookup/append readings are 1.0532/1.1011 ms and include their
per-packet clock overhead; they are not uninstrumented costs.

Managed packing reads 1.2063 ms, the full native-queue call 2.4198 ms and its
native queue timer 2.0941 ms. Auxiliary queues read 0.1411 ms; upload completion
0.0181 ms. Logical object bytes average 1,448,367.65, and unique instance bytes
2,061,120. Native material misses average 0.33 with no resets. Preparation
geometry/upload/descriptors/sort timers are approximately 0.302/0.594/0.406/0.771 ms.
The next queue investigation should distinguish repeated resource validation
from per-packet copying, preserving checks and deep-copy ownership.

This later diagnostic has **5.9089 ms Present**, unlike approximately 0.19 ms
in the matched treatment comparisons. Its 27.2537 ms total is instrumented and
presentation-state affected, so it is not evidence that the retained changes
regressed. A fresh ordinary Release run is required before shader optimization
or comparing its total to the earlier baseline. World GPU remains 4.8830 ms;
it does not establish full-frame GPU time or pass attribution.

The ordinary October 7 follow-up `Vulkan-de021cbd8dca4d928372687b15b779a6`
completed with the same plugin/native hashes, no CPU detail or profiler, full
1920x1080 world/output and fraction=1. It confirms the later presentation cost:
**26.0370 ms CPU**, 1151 frames, median 25.2677, p95 30.5122, p99 35.3339 ms,
interval 26.0644 ms and **38.3665 FPS**. Present averages **5.7802 ms** and
native queue **2.1094 ms**, with 18,690 logical draws and 10,807.58 packets.
This is the latest ordinary baseline, not the earlier 21 ms comparison sample.
No numerical subtraction of Present is used to claim achieved frame time or FPS.
The extra presentation cost survives removal of CPU-detail clocks; its cause
is not established. Next inspect actual swapchain presentation state and driver/
compositor waiting before selecting GPU-pass changes. Original save/config/profile
remain unchanged; full <4.1667 ms performance and DX11 visual parity remain unmet.

## Actual presentation state and layer isolation, October 7

Added the independent read-only `SevkPresentationStats` extension (32 bytes).
It stores the successfully created swapchain mode/generation and reports actual
image count, applied VSync and readiness. Offscreen mode is UINT32_MAX rather
than a valid-looking Immediate zero. Existing work/performance/stat ABI layouts
remain unchanged. The private harness reads it on the completed render thread
and emits `presentation-state.json`; no presentation policy is changed.
All five native CTests pass, including actual HWND state, VSync resize selecting
FIFO and offscreen invalid-mode semantics. Native log:
`TestResults/PresentationState-Native.log`.

Ordinary run `Vulkan-bcf04b19c65d456baa7dfaef497e1963` proves **Mailbox (mode 1),
applied VSync off, three images, generation 1**, with surface/swapchain ready.
Present is still **5.8863 ms**, native queue 2.1035 ms; CPU frame time 27.3954 ms,
interval 27.4281 ms and 36.4589 FPS over 1094 frames. Full world/output remains
1920x1080 at fraction=1. This rules out accidentally selecting FIFO through
the game config as the cause of the later wait; it does not prove where the
driver spends that time. Matched hashes for this readout/diagnostic lane:
plugin `FA93265B56F8D03459E8448E42F3BBA22471CAE7BC6FEEC355A19545A1FADCE7`,
native `82856DAD173581D02CBD34ACDA965397276B1CD1E1A820FD76BCFED47DA52478`.

The ordinary process's loaded-module snapshot contains SteamOverlayVulkanLayer64,
VkLayer_steam_fossilize64 and gameoverlayrenderer64, with no Nsight Vulkan layer
observed. The private `-DisableSteamOverlay` diagnostic uses the installed
manifest's exact disable variable. In `Vulkan-455841c9b73944fba492b9b9c400627f`
the Vulkan overlay module is absent while Fossilize and the general Steam overlay
module remain loaded. This is evidence for the intended narrow layer difference,
not absence of all overlay hooks. The diagnostic changes no game rendering quality
and no persistent settings, but its total is not the ordinary workload or proof
of target achievement. The diagnostic completed at 25.3886 ms CPU over 1180
frames (39.3272 FPS); Present remained 5.6492 ms and native queue 2.0453 ms.
Removing this Vulkan layer did not remove the presentation wait. No production
overlay suppression is retained.

## Call-scoped texture validation reuse, October 7

Retained a fixed-size, full-key cache local to each native world queue call.
Repeated successfully validated four-texture tuples reuse their existence check.
Mesh/layout/budget checks and voxel-group validation still run per packet;
packet data remains deep-copied. Failures are not cached, collisions compare
the entire tuple, and no validation result survives the queue call. Resource
removal between calls therefore remains observable. Set
`SE_VULKAN_TEXTURE_VALIDATION_CACHE=0` for the reference lane.

The cache-first paired run `Vulkan-3d77a0c01fc9403b8894804dda0917cd`
measured queue 2.0130 ms cached versus 2.3804 ms legacy. Reverse order
`Vulkan-67ab094d8e2b459081b1c7ab9e41828a` measured 1.9246 versus 2.1070 ms.
These are 8.66–15.44% queue improvements, approximately 0.182–0.367 ms.
Whole-frame improvements vary with workload/presentation; do not attribute
their entire delta to the cache. The reverse cached sample is 26.0495 ms CPU,
1151 frames, 38.3400 FPS, 18,701 logical draws, 10,829.42 submitted packets,
5.9118 ms Present and 4.8753 ms world GPU. This is far above the target.

Native counters correlate to every accepted phase frame. In reverse order,
legacy performed 35,866.75 texture checks/frame versus cached 1,131.86,
with 8,679.45 cache hits; all 1,867 voxel checks/frame and all mesh checks
remain active. Cached/legacy GPU output matches exactly in the targeted test.
All five native CTests and 106 world-pass GPU checks pass with zero validation
errors, including forced collisions, uncached failures and texture removal
between queue calls. Evidence: `TestResults/QueueTextureValidation-Gpu.log`,
`QueueTextureValidation-Retained-Native.log` and
`QueueTextureValidation-Retained-Release.log`. Release builds for net10/net48
pass with zero warnings/errors and are deployed to the local release lanes.

Retained net10 plugin SHA256:
`D36C258564A51BB6CC823059E3C399C5101A754F03A87352FF1383EDADC5339E`;
native SHA256:
`7803D5DD9027838FCFFACED469F911E5CDBEADC15199684B074C5E8D104D28E8`.
These hashes include the default-enabled flip after the paired candidate runs.
The user DevFolder DebugBuild profile can compile a different product on launch.
No full-scene DX11 visual-parity claim follows from targeted pixel tests.


## Clean comparisons and shared geometry, October 7

With external GPU load stopped, full-1080p Pertam direct/indirect comparisons:
`Vulkan-d7bf36af998243e8b1b183d0d17d3e50`: direct 26.1443 ms (1147 frames),
indirect 26.0402 ms (1152). Reverse
`Vulkan-18fb2b8b4ab94a479263e7a049f3000b`: indirect 26.3855 ms (1136),
direct 25.9138 ms (1157). GPU idle before these tests was 0%. Actual main and
cascade API calls fell approximately 34-35%, without consistent whole-frame or
GPU gain. Final-window world GPU was 4.743-4.881 ms; Present 5.758-5.889 ms.
Saved-camera physics drift and small membership changes limit exact comparisons.
Indirect remains opt-in. Product hashes and actual counter correlation are in
`Docs/planning/PERFORMANCE_STRUCTURAL_PLAN.md` and each run's `launch.json`.

Shared device-local world vertex/index pages now use existing VMA virtual ranges
and completion retirement. Compatible indirect records can span different meshes;
direct draws bind individual byte offsets. Both policies remain off by default.
Six native CTests and unified net10/net48 Release builds pass. WorldPassGpuTests
passes 120 checks, including exact standalone/arena/direct/indirect pixels,
nonzero offsets, unequal counts, pending replacement/removal and page cleanup.
Arena-enabled probe (103), transparency (149), environment (35), postprocess
with spot/highlight (176), and voxel (51) checks pass with zero core and
synchronization validation errors. Evidence: `TestResults/GeometryArena-*.log`.
These tests establish focused correctness, not full-scene parity or FPS gains.

Experimental arena SHA256: plugin
`29D48B36B166FAB6FAC6FEB77631205A1905E368F104B4FF0981BFBC92EC9306`, native
`BE770FF45A41A19BF3ECB2F00290A42FEE58535B419233A3026A34C47444D390`.
Private `-GeometryArena` selects allocation before world loading and emits actual
per-frame page/live/capacity counters. Allocation comparisons require separate
processes; same-process direct/indirect tests hold allocation policy fixed.


The arena scene comparison completed in both orders with the product hashes above
and harness `5B031F7D099272907688754031933AF8A1A8A2C040D3950C3F25354CF4B8F6EC`.
`Vulkan-3d5a862db5c5411aad96c6ab64c4e51b`: arena direct 26.6773 ms (1123 frames),
arena indirect 26.7729 ms (1119). Reverse
`Vulkan-79e3572e327e48038e4e6b1e45daf41f`: arena indirect 26.4397 ms (1134),
arena direct 25.6884 ms (1167). Same-binary standalone reference
`Vulkan-a2c75c2e319e48509b2cc60cea07f2f2`: indirect 26.3391 ms (1138),
direct 25.5751 ms (1172). Actual enabled policies and counter counts match all
sample frames. No whole-frame arena/indirect improvement is established.

Arena-first actual main+shadow calls fall 10217.6 to 6059.2 (40.7%). Native
sort/plan preparation grows 0.776 to 1.285 ms, offsetting reduced recording.
The sampled allocator has 65 vertex pages (1,070,561,768 live bytes;
1,090,523,144 capacity) and 8 index pages (125,234,328 live; 134,217,728 capacity).
The modest packing overhead and lower call count establish working foundations,
not achieved FPS. Both policies remain off by default. Machine-readable evidence
with hashes and per-phase metrics: `TestResults/GeometryArena-CleanComparisons.json`.


Fresh DX11 reference `Dx11-7ac15498ded24037b1a80c4b59730ffe` completed 6367 frames
at full1920x1080: CPU Draw-through-Present mean4.51798ms, median4.1496ms,
p95 6.0689ms, Present interval mean4.71536ms,
212.073 rendered FPS. This saved-doorway workload is not a >240FPS DX11 result;
the original screenshot camera remains unmatched. Vulkan standalone direct mean
25.5751ms is roughly5.66x this measured DX11 CPU mean, with target/parity unachieved.
The source save SHA remains unchanged and all private game processes have exited.

The initial fresh DX11 run `Dx11-3a8670001cb848eaaaf41b255375f078` is excluded:
no harness Reports and QuickStart loaded instead of copied Pertam. Its exact owned
process was stopped; `invalid-workload.txt` records the rejection. Harness Vulkan
field lookups are now deferred to Vulkan initialization; initialization exceptions
are recorded in the marked private data folder. The retry loaded copied Pertam
and produced valid DX11 results. Harness SHA is
`507A7182BD84EFDD280F88578FB37B6C90CCE66415529D9FC1B8F508202974EA`;
product hashes remain unchanged. The completed Vulkan comparisons retain their
previous harness hash, so distinguish these artifacts when reproducing them.

Visually inspecting the fresh DX11 view against the arena/direct Vulkan doorway
view still shows differences in door emissive color (green versus white), interior
brightness, LCD tone/brightness and floor shading. Screenshots are independently
sampled with simulation drift; this is a qualitative parity finding, not an exact
pixel-diff oracle. These differences require their own matched parity work and
cannot be credited as performance savings.


## Retained object experiment and DX11 structural comparison

Added experimental native aligned object storage with stable revisions and two
fenced slot upload versions; resource/packet validation still runs on selection.
Default remains off (`SE_VULKAN_RETAINED_OBJECTS=1` opts in). Seven native CTests
and Release builds pass. GPU WorldPass136checks proves exact pixels, reordered
static zero-upload, visible transform changes, both slots and reference switches.
Retained probe103/transparency149/postprocess176/voxel51checks pass with zero
core/synchronization errors. Logs: `TestResults/RetainedObjects-*.log`.

The initial weak-array identity lane over-retained temporary voxel constants and
regressed from26.3657 to39.3897ms; replaced recyclable constants with reusable
layout/position storage identities, keeping byte comparison authoritative.
Corrected run `Vulkan-c51f714d9b1c440f8e6791c2aff2d2f9` still regresses:
28.3622ms retained (1057frames) versus26.1803ms legacy (1145). Native queue
2.803 versus2.029ms and upload0.803 versus0.566ms. Object uploads fall to226KB
from1.542MB; store12359records/2.917MBcapacity. These counters do not constitute
a speedup; retain the legacy default. `RetainedObjects-Comparisons.json` preserves
exact hashes/frame-correlated stats. Disabled-phase selected/changed counters in
that historical binary are stale retained values; the readout is subsequently
corrected to zero those fields when disabled.

The decompiled installed-version DX11 implementation confirms persistent dirty
merge-instancing tables and instance buffers, per-view grouped instance buffers,
and dependency-scheduled culling/preparation/rendering jobs for both old and
GeometryStage2 managers. Vulkan still reconstructs and serially processes packet
work. Next prioritize explicit dirty record updates/direct indexing and coarse
pass parallelism under frame-owned Vulkan resources. Study-only source paths and
ownership contracts are in `Docs/planning/PERFORMANCE_STRUCTURAL_PLAN.md`.
The objective remains<4.17ms at full1080p with visual parity; no acceptance follows
from this experimental foundation.


## Concurrent cascade recording and fresh CPU capture

Implemented experimental per-cascade CPU recording with secondary depth command
buffers, worker-local counts and separate cascade/frame command pools. Primary
owns render scopes, image barriers and execution order. Pools reset after slot
completion; jobs join before prepared data/resource teardown. Default is off
(`SE_VULKAN_PARALLEL_CASCADES=1`). Private `-CompareParallelCascades` supports both
orders and frame-correlated actual task/command counts in `cascade-recording.json`.
Seven native CTests and unified Release builds pass. WorldPassGpuTests145checks
passes with zero validation errors, proving exact serial/parallel direct/indirect
pixels, both slots, changed/removed casters and shared-page cross-mesh depth groups.
Evidence: `TestResults/ParallelCascades-Native.log`, `-Release.log`, `-Gpu.log`.
No full-scene frame-time improvement is claimed until clean comparisons complete.

Design studies use same-engine DX11 MyRenderScheduler per-view jobs and Khronos
command_buffer_usage sample:
https://docs.vulkan.org/samples/latest/samples/performance/command_buffer_usage/README.html
DX11 updates the first four cascades every frame; its staggered far-cascade policy
does not justify skipping the first four. Actual later scene counters show six
cascades; the previous four-cascade scene statement conflated fixture and scene.

Fresh CPU-only Nsight capture `Vulkan-b9068f177a924e42a8e2b07d8ce82d77` reached
copied Pertam and completed instrumented game sampling, but report import remains
live at20% with a zero-byte placeholder. Initial helper completion was premature;
Capture-Nsight now waits for profiler termination and nonempty report/SQLite.
Do not claim usable CPU profile or hot-function attribution until report integrity,
diagnostics and samples are inspected. Do not benchmark alongside the exporter.
The active import has its own private earlier product hashes in launch.json;
current parallel candidate differs. Last observed importerPID35788 still consumes
CPU; keep following that handle rather than launching another capture.


## Generic color multiplier parity correction

Found a reproducible batching regression: stock generic instancing forwards HSV
but leaves pixel color_mul=1 when PASS_OBJECT_VALUES_THROUGH_STAGES is enabled.
White/HSV fixtures did not cover non-white multipliers. Derived offline GBuffer
and forward pixel variants now read object_.color_mul while retaining the stock
stage interface, shading and descriptors. Tinted rigid objects still group into
one draw. Eight modules and metadata are authenticated against the base module,
source transformation and coverage-bound manifest; originals remain untouched.
World154 and Probe119GPUchecks pass with zero validation errors. They prove
visible tint, all four material variants, colored emission and exact grouped
forward probe output. Integrity12checks reject changed base/modules/transforms.
Release builds pass with zero warnings/errors. Logs are TestResults/ObjectTint-*.
This establishes a focused color-contract fix; actual doorway visual matching,
lighting/LCD/floor parity and<4.17ms full-frame performance remain unverified.
Full-scene comparison waits on the existing live Nsight CPU importer.


Clean parallel recording comparison completed both orders with tint-corrected
shaders: `Vulkan-3ab7f9f22d5a401d867b77aca02b7ebd` serial26.6868ms1123frames,
parallel26.0409ms1151. Reverse `Vulkan-ebb26c50cd524deeaec5f32315bc5079`
parallel25.2877ms1186, serial25.8367ms1160. Frame counters align with all timing
rows; parallel executes six cascade jobs and all selected cascade commands.
Net saving0.55-0.65ms (~2.1-2.4%) is modest and remains far from<4.17ms.
First run had brief read-only capture-summary query activity; reverse had none.
Native default now enables parallel on CPUs with>=4 hardware threads; override
SE_VULKAN_PARALLEL_CASCADES=0 selects serial. Private launcher still sets either
policy explicitly. Evidence/hashes: TestResults/ParallelCascades-CleanComparisons.json.

The tint-corrected default saved view restores the green door strip, consistent
with DX11. Independent images still differ in LCD brightness/interior lighting/
floor shading; camera and simulation drift preclude exact scene-wide pixel proof.

Nsight CPU-only capture b9068f177a924e42a8e2b07d8ce82d77 has completed and
SQLite integrity passes. 10.011second window includes9710 periodic render samples
and11047 scheduling-event stacks. Summarize-NsightCpu.py uses cpuCycles=1 per
NVIDIA Analysis Guide: https://docs.nvidia.com/nsight-systems/AnalysisGuide/index.html#cpucycles-attribution
Leaf shares47.27%unknown,24.24%native,10.78%NVIDIA driver,7.88%coreclr,3.49%vcruntime.
All function symbols are unresolved, so these are module-level sampling evidence,
not hot-function or absolute frame-time proof. Missing Windows Performance Toolkit
prevented merged ETL creation; exported CPU sampling exists. No profiler remains
running. nsight-cpu-summary.json preserves diagnostics, hashes and filtered counts.


Final retained-default build: native seven CTests; World GPU155checks; Release
net10/net48 zero warnings/errors. NativeSHA
B5998D982C924A1E092C99016CCC7ED4051767575B518A37C8549309E035CBC9;
net10pluginC1950C8CDFA92E262604F885A2125A5EF7FC0A563253C04B05A4A6473795978A.
The default-selection flip follows the paired candidate hashes; no other native
submission change follows those timings. Source checkpoint remains unchanged.
The normal DevFolder DebugBuild profile can compile a different managed artifact;
these checks concern the canonical Release/private lanes.


GPU light selection completed both ordinary full1080p orders with parallel
cascades held on: `Vulkan-daea982b54ed4f6881d5d5f73f4aa44c` full26.3561ms1137frames,
GPU26.9104ms1113; reverse `Vulkan-e70121a4725c4dceb660074b2f01d571`
GPU26.3310ms1138, full25.9227ms1156. GPU mode is slower in whole-frame CPU in both
orders, so it stays off by default. Completed assignments fall approximately94%
(30K versus471K), with completed frame exactly two frames behind current policy
and one dispatch per accepted GPU frame. Final-window GPU4.8226->4.7627ms and
5.1870->4.9016ms are lower but main command counts vary, so no precise isolated GPU
speedup follows. Machine evidence: `TestResults/LightTiles-CleanComparisons.json`.

Paused full/GPU/full scene diagnostic `Vulkan-ea474cf4ddbd4bafa0ff58f62d566b15`
keeps identical camera,18701 logical and10835 physical packets across six captures,
full1920x1080. Actual GPU counts41580/465120. World-region cross-mode mean channel
delta0.0000884 and0.0001803 (8-bit units) is below the largest same-mode control
0.0003771; cross-mode above-four fractions0.000625%/0.001528% are below control
0.003056%. No broad light-selection regression detected, but images are not byte-
identical because temporal effects remain. This diagnostic is not FPS or DX11
parity proof. PixelDiagnostic is true; its historical Instrumented field did not
include the new switch, corrected for future launches. `pixel-comparison.json`
and `pixel-delta.json` preserve actual policies, counts, resolution and controls.

World GPU195checks pass with zero core/synchronization validation errors, including
partial edge groups, off-center projection/rotated view, fractional extent and64
ordered lights. LightTileShaderIntegrityTests5checks reject changed source,
corrupted module and transformation with a recomputed manifest hash. Native7CTest
and Release builds pass. Shader/package/native logs under TestResults/LightTiles-*.
The native compute integration uses16-byte padded LightConstants and device-local
lists, explicit compute/fragment/transfer/host dependencies and slot-fenced readback.
Full-list reference remains the production default; all original game source files
are untouched. The full<4.17ms/visual-parity objective remains unachieved.


Cached readback correction is validated: native7CTest, Release zero warnings/errors,
World GPU195checks pass. The tile count buffer now uses VMA HOST_ACCESS_RANDOM and
requires HOST_CACHED for CPU reads; other buffer policies retain their previous
settings. New ReadbackUs field occupies the prior reserved stats word. Remeasured
readback is4.8-5.2us and acquire interval about9us, resolving the earlier~0.6ms
sampling interval. VMA guidance: https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/usage_patterns.html

New full1080p comparisons use identical current product/harness hashes:
`Vulkan-eab442683fc94051a9cf1741e01fc65e` full26.2876ms1140frames versus
GPU26.4159ms1134; reverse `Vulkan-50fa17fad1b04defac6cb2800507d34f`
GPU26.0652ms1150 versus full26.4662ms1132. GPU final windows4.9574->4.8871ms
and5.0975->4.7900ms; main draw counts differ, so no exact isolated GPU delta follows.
Full-frame CPU improvement remains inconsistent. GPU light selection stays off by
default; cached readback is retained as the correct ownership policy for the
experimental path. Machine evidence: LightTiles-CachedReadback-Comparisons.json.
The retained parallel-cascade default and tint fix remain active.

NativeSHA96588CCAF3344203A390263EA92DA77C7DA3750F9ABBF99449872E2E8F447185;
net10pluginDF31C62F033020B0BE043EA349B0ED66BE6AFAD9D2E534D2DFF30D8888E5CD6C;
pairedharnessDE4A6CB437528552C6F34BDEA4B32C3D7F48B619F4FD3A0F049AEBBAA86ECF31.
All private game processes exited; source checkpoint remains unchanged.
The<4.17ms and full DX11 parity requirements remain unmet. Next macro work should
continue explicit dirty/indexed scene records and same-engine far-cascade scheduling,
not infer completion from tile-count reduction or narrow fixture equality.


Deeper profiler pass (explicit user steering) completed Vulkan and DX11 same-scene
ETW captures with PerfView3.2.8, CPU/scheduling/CLR/rundown and frame markers. Native
optimized symbols resolved1116addresses with0 unmatched, and both captures lost
0events. Marker window excludes capture setup/rundown; completeframes331Vulkan,
2360DX11. Profiling overhead means no ordinary FPS comparison is claimed.
Vulkan scene frontend10.22ms CPU/frame, bridge/queue4.66ms, native record/submit/
present6.19ms; these non-overlapping stack buckets rank structural work. Scheduling
classifies~24.20ms CPU and5.95ms blocked per marked Vulkan frame. Most blocking
(5.38ms) is under native end_frame with graphics-memory-manager wake stacks.
DX11 render-thread CPU is2.68ms/frame in the matched profile. AllocationTick
estimates741MB Vulkan render allocations versus15.25MB DX11 over respective10s
windows; stacks identify occlusion snapshots, voxel constants, texture paths and
packet arrays. GC suspensions0.69ms/Vulkanframe alone do not explain the gap.
No renderer algorithm changed during this pass. Next implementation must target
retained/dirty scene submission and bulk native ownership, with presentation
investigated separately; do not substitute a hash/helper micro-fix.
Detailed evidence/caveats: Docs/CPU-Profiling-2026-10-07.md and
TestResults/DeepProfile-RendererComparison.json. Production nativeSHA96588...7185
unchanged; private capture DLL/PDB hashes live in launch.json.
