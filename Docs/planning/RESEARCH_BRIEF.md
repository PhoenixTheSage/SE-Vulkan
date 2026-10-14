# Vulkan structural performance research — October 7, 2026

Engineering intake for the active performance investigation. This is a research
record, not a substitute Planning Harness Work Package or implementation plan.
Target: full 1080p frame total below 4.1667 ms with DX11 visual parity, preserving
scene density and quality. Latest matched private Pertam sample remains about
26 ms CPU and 4.875 ms world GPU; world GPU is not full-frame GPU time.

## Evidence shaping the architectural choice

The frontend rebuilds approximately 18,701 logical part submissions and 10.8K
packets/frame. Main, shadow, spot and probe counters together imply roughly
12.4K recorded draw commands. Native emission changes dynamic object UBO
offsets and binds independent mesh buffers. Consequently, changing each draw
to one indirect draw would retain the structural bottleneck. Existing material
and part caches have high hit rates; additional cache tuning is a lower priority.
Present remains about 5.9 ms despite actual Mailbox, VSync off and three images.
GPU timestamps cover the world span, not individual passes; CPU pass timers
cannot establish shader GPU cost.

## Primary sources and decisions

- [VMA virtual allocator](https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/virtual_allocator.html):
  reference-only; use the project's existing dependency. Virtual blocks manage
  subranges without allocating GPU memory. The documented unit-based interface
  fits non-power-of-two vertex strides. Allocation tokens must be freed from
  their owning block; native timeline retirement supplies the lifetime rule.

- [Khronos multi-draw indirect sample](https://docs.vulkan.org/samples/latest/samples/performance/multi_draw_indirect/README.html)
  and [implementation](https://github.com/KhronosGroup/Vulkan-Samples/blob/main/samples/performance/multi_draw_indirect/multi_draw_indirect.cpp):
  reference-only. Shared geometry buffers and indexed draw/material records
  support bulk indirect submission and GPU visibility. Useful for replacing
  repeated frontend reconstruction and thousands of individual commands.
- [Khronos descriptor indexing sample](https://docs.vulkan.org/samples/latest/samples/extensions/descriptor_indexing/README.html):
  reference-only. Indexed resource arrays are an option for batching heterogeneous
  materials, subject to supported/enabled features and fenced resource lifetimes.
- [Khronos command-buffer usage sample](https://docs.vulkan.org/samples/latest/samples/performance/command_buffer_usage/README.html):
  reference-only. Coarse parallel recording needs independent worker/frame pools;
  pool reset follows completion. Many tiny secondary buffers can erase benefits.
- [NVIDIA Vulkan guidance](https://developer.nvidia.com/blog/vulkan-dos-donts/):
  reference-only, updated January 2025. Application task scheduling must supply
  recording parallelism. Submission count, waits and retained allocation matter;
  sample-level gains are not forecasts for this game.
- [Indexed indirect draw contract](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDrawIndexedIndirect.html)
  and [dynamic-rendering inheritance](https://docs.vulkan.org/refpages/latest/refpages/source/VkCommandBufferInheritanceRenderingInfo.html):
  authoritative correctness anchors for feature gating, command bounds and
  secondary recording. No source code has been copied or new dependency added.

## Structural direction to investigate

Persistent geometry arenas and persistent draw records, followed by bulk indexed
indirect commands, address both command count and repeated packet preparation.
Current per-draw dynamic UBO offsets require an explicit object-record/shader
contract before heterogeneous draws can share a command. Current device feature
enablement does not enable multiDrawIndirect, drawIndirectFirstInstance or
descriptor indexing: query and enable supported capabilities with a tested
reference fallback. Preserve cascade masks, light exclusions, highlights,
dither, LOD, transforms and resource-generation identity.

Parallel frontend/recording is a complementary candidate, with coarse ownership
boundaries and per-worker command/descriptor pools. It must be measured against
the same full scene; adding workers cannot by itself reduce draw count or GPU
work. No macro prototype is claimed implemented by this intake.

### Current hardware and executable shader evidence

The read-only `Tests/ScenePerformanceHarness/Inspect-IndirectShaderContract.py`
authenticates catalog, supplement and selected modules, recording actual source
and module hashes in `TestResults/Indirect-Shader-Contract.json`. All 40 selected
object-geometry vertex pipelines have no VertexId/VertexIndex, InstanceId/
InstanceIndex, BaseVertex, BaseInstance or DrawIndex input. Voxel/other-pass
selection is excluded. This removes an explicit index-input concern about
rebasing instance streams; it does not prove a new indirect path valid. Dynamic
object offsets and mesh-buffer bindings remain the structural compatibility
boundaries, and pixel equivalence still needs runtime proof.

`vulkaninfo -o TestResults/Vulkan-Structural-Capabilities-2026-10-07.txt`
successfully queried the RTX 5070 Ti, driver 617.14. Hardware exposes
multiDrawIndirect, drawIndirectFirstInstance, shaderDrawParameters,
descriptorIndexing, runtimeDescriptorArray, sampled-image nonuniform indexing
and partially bound descriptors. maxDrawIndirectCount is 4,294,967,295.
The loader emitted a registry layer-manifest lookup warning; the feature query
completed with exit 0. This is device support, not application enablement.
Current creation still enables none of the indirect/indexing features above.

Native submission timing independently resolves the coarse Present ambiguity.
In reverse cached validation, FinishUs=6.352, SubmitUs=59.418 and
PresentUs=5911.819. The code's PresentUs bracket surrounds the presentation
branch after submission, not the full managed EndFrame call. The older cached
voxel-object sample reports FinishUs=6.539, SubmitUs=60.099 and
PresentUs=187.166. The later wait is therefore in the presentation branch;
its driver/compositor cause remains unproved. Preserve this separate structural
question when interpreting CPU/GPU overlap and future command-count changes.

The existing Nsight batch trace `Vulkan-d815e7fafa7b41c6920b67b1ba8632bd`
now has host-submit/next-present correlation in `nsight-summary.json`, produced
by `Summarize-Nsight.py`. Of 442 recorded batches, 382 match a following present
on the same context/thread before the next submit; 60 are unpaired (not assumed
to be frames). All 382 matched presents return before their recorded GPU batch
ends. Their mean present API duration is 0.202238 ms and mean GPU batch span is
4.644790 ms. This establishes asynchronous presentation in those historical
recorded pairs and distinguishes it from the later ~5.9 ms presentation branch.
Dropped-event warnings remain and PassAttributionComplete stays false; these
spans are not complete occupancy, clean benchmark results, or an explanation of
the later wait. Frame-level GPU spans also differ in scope from world timestamps.

## Project dos and don'ts

### App / domain

- Preserve full scene and quality. Acceptance uses the same camera and workload,
  total frame intervals, scoped CPU measurements and correctly attributed GPU
  timing; never subtract Present to claim achieved FPS.
- Preserve packet/object identity and fenced lifetimes through arena/indirect
  changes. Validate unload, replacement, animation, shadows and collision cases
  plus matched GPU pixels, following the Khronos contracts above.
- Resolve presentation wait attribution with a successful profiler capture;
  layer absence and Mailbox state are diagnostic evidence, not causal proof.
- Compare DX11 on the matched camera. The original screenshot camera is not
  proven identical to the copied save's doorway camera.

### GUI / product surface

- Retain the game's visual behavior and existing performance UI semantics.
  New counters identify instrumentation state and scope explicitly. LCD clipping,
  lighting and particles remain open parity requirements.
- Keep experimental controls in the private harness until supported behavior
  and resource lifetimes are established; no per-tick configuration writes.

## Historical planning dependency discovery — superseded by user override

Installed CppStudio `modules/process/governed.md` requires loading
`agent-planning-harness` for durable coordinated work. It is absent from the
searched Codex, Agents, Cursor and plugin skill roots. The public CppStudio
recursive tree and managed skill inventory do not contain it. An inferred
`https://api.github.com/repos/tarkansarim/Agent-Planning-Harness` returned
404 on October 7; this does not establish whether a private source exists.
Further discovery inspected a read-only CppStudio clone, the installation audit's
source commit `69ca731da714`, and introduction commit `2cea6c9`. They contain
the requirement but no harness package or install route. The author's public
repository inventory, CppStudio issues, exact-name GitHub repository search,
and additional Claude/Copilot/local skill roots produced no matching package.
No similarly named third-party package was installed. The user has no source
location and requested continued searching. Coordinated implementation remains
pending the canonical source; bounded investigation and recording already
validated work can continue.

Superseded October 7 by explicit user direction: CppStudio supplies the mapping
and planning workflow; no separate install is needed. Proceed under that override
using `PERFORMANCE_STRUCTURAL_PLAN.md`. The objective and full-quality acceptance
requirements remain unchanged; this historical discovery record is not a blocker.

## Current geometry/visibility references

Reference-only installed-game sources, not third-party donor code:
MyRenderableComponent.FindLod/CalculateViewerDistance/CheckDistanceCulling and
UpdateProxiesCustomAlpha define old-pipeline distance, projected-size and transition
contracts. MyGeometryRenderer.UpdateLods and MyPreparePass.PrepareLodData define
new per-view shifts/grouping; MyStaticGroupComponent defines group ownership.
MyActorOcclusionRenderer.Render defines inflated manual-group queries, near/reentry
guards and asynchronous timeout. These live under the prepared se-dev-game-code
Data/Decompiled tree, VRage.Render11; captured stock settings and MWM descriptors
are authoritative runtime evidence. Vulkan counterparts: VulkanModelCache,
VulkanScene/VulkanOcclusion/VulkanSceneIndex and native world/occlusion recording.
Use these to restore the same workload, not to invent a quality tradeoff.

2026-10-07 retained topology references (reference-only, no copied source):
Khronos Vulkan-Samples performance/descriptor_management README,
https://github.com/KhronosGroup/Vulkan-Samples/blob/main/samples/performance/descriptor_management/README.adoc
covers descriptor caching and its dependency on buffer lifetime/packing. Existing
SE-Vulkan already caches material descriptor sets; this work retains the resolved
per-slot packet references and sort order too. Explicit invalidation is required
on resource replacement, descriptor pool recycling or buffer growth. Stock
MyGeometryRendererOld/MyRenderingWork is the same-system ownership reference;
private scope remains opaque world topology, transparent/probe frame sets remain
in their existing owners. GPU completion rules unchanged; no spec extension.
