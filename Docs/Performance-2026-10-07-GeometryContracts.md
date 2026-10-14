# Geometry workload and occlusion diagnosis

The current performance gap includes substantially different submitted geometry,
not just slower execution of the same draws. This investigation adds diagnostic
snapshots/counters and read-only MWM metadata inspection; it does not claim a new
production speedup or relax the rendering policy.

Copied Pertam save, full1080p, separate unpaused diagnostic frames:

- Vulkan-c6d2422bdc6743a998ac365f46aaa9a5 maps every scene packet to its model.
  Its main opaque scene contains24581913 indexed element invocations in4318
  physical packets, about8193971 triangle instances.
- DX11-4a35eb1e84344048a18193cb23a91b39 includes old pipeline, GeometryStage2
  instance groups and static groups. The latter two are empty in this frame.
  Excluding voxel geometry, the prepared main scene has596418 indexed element
  invocations in545 renderables. The snapshot ratio is41.22; it is a workload
  difference, not a speedup or proof of equal visibility in different frames.
- Read-only MWM audit covers460 mapped Vulkan models:424 have stock LODs,
  zero unavailable files. VulkanModelCache imports one geometry asset without
  the LODs tag; VulkanScene has no actor model LOD selector.
- DX11 captured LOD coefficient0.791676, main shift0/min0; shadow/single-depth
  shift2 for non-main-visible objects; forward shift/min10. Those per-view shifts
  belong to GeometryStage2, which is empty in this captured frame. The old pipeline
  selects its shared current LOD with GBuffer settings; do not blindly apply new
  pipeline shadow/forward shifts to it. It actually selects
  nonzero stock LOD thresholds in the main frame. These are quality contracts,
  not permission to invent cheaper distance thresholds.

Vulkan-5c2577e5409c46b4b31f32b726044963 attributes the unchanged conservative
occlusion policy over1920 sampled frames. It accepts zero completed snapshots,
rejects1911 for scene revision and1909 for camera matrix; extent and token
ownership do not fail. Completed hidden results3428938 produce zero scene culls.
Maximum camera step is0.000156metres. Revision causes include75180 render-object
messages,842869 propagated transform notifications,14328 model-property messages,
and routine gameplay/planet/SSAO/HBAO/postprocess updates. Causes overlap.
The process-wide log independently records~7.6million hidden GPU results and
zero applied scene culls. A faster query/hash helper cannot fix this contract.

DX11 MyActorOcclusionRenderer instead queries inflated manual-group bounds,
resets newly reentered/near-camera groups, asynchronously consumes their results
and handles a bounded query timeout. It does not require an identical global
scene revision and camera matrix on every completed result. Port the stock group
ownership/latency/near-camera behavior deliberately; do not simply remove the
guards from Vulkan's uninflated per-actor queries or invent a camera epsilon.

Additional source-backed gaps: stock MyRenderableComponent applies projected
size culling (<20 pixel product) only beyond the last LOD distance. Vulkan does
not implement this. Stock main culling uses clipped frusta; Vulkan's camera
projection matches the original projection contract, so an assumed frustum-matrix
bug is not established. Static-group bounds/selection need their own audit before
removing the current bypass.

Fresh non-elevated managed trace Vulkan-7922f88398ab4fdb977982af4dee64b8 remains
dominated by scene DrawWorld and the blob queue/native EndFrame boundary. It is
sampled thread wall time, including opaque native execution and waits; PollGC
samples do not establish a GC bottleneck. The elevated paired ETW capture still
awaits the user's retry reply after canceled UAC. Neither trace is ordinary FPS.

Next implementation scope: stock old-pipeline model LOD metadata/selection, with
new per-view/static contracts tracked separately, alongside stock group occlusion
ownership. Preserve transforms,
material visibility, highlights, skin/cube streams, transitions, shadow/probe
selection and deletion. Validate fixed-camera geometry against the DX11 audit,
moving/disocclusion cases and full-size pixels, then ordinary both-order timing.
Parallel preparation remains a follow-on after the excess workload is addressed.
The<4.17ms and full DX11 visual parity goal remains open.

The first workload launcher mislabeled diagnostic timing as uninstrumented.
Raw reports are preserved and each affected run has
workload-diagnostic-correction.json. Their timing/FPS rows are excluded from
ordinary comparison evidence. The launcher and harness now mark workload audits
instrumented independently. The incomplete first DX11 audit is superseded; one
intermediate run was stopped before sampling to repair a diagnostic property
access error. Product rendering behavior was not changed by that repair.

Artifacts: geometry-contract-comparison.json and model-lod-assets.json in the
Vulkan-c6d2422... Reports directory; occlusion-diagnostics.json in Vulkan-5c2577...;
managed-summary.json and raw cpu.nettrace in Vulkan-7922f8.... Capture helpers:
GeometryWorkloadAudit.cs, Summarize-GeometryWorkload.py and Tests/ModelLodAudit.
Diagnostics-enabled production scene replay passes20 checks with clean Vulkan
core/synchronization validation; net48/net10 Release builds pass.


Correction (2026-10-07 mixed pipeline audit): owning-render-job capture finds305 new main groups/962 new actors and5,125,884 new-path indexed invocations in this scene. Earlier post-frame active-view filtering omitted that path. Old-only596,418 is incomplete; all-old/large index-ratio conclusions are superseded by Performance-2026-10-07-MixedPipelineAudit.md. Old size/far policy applicability is now restricted to positively identified old actors. Historical timings/diagnostic artifacts are preserved with their original scope.
