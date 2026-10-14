# Renderer CPU diagnosis

The trace below predates removal of external GPU load. Fresh ordinary comparisons
now show Present~0.2ms rather than the old5–6ms wait. Retained submission saves
2.8–3.3ms in both run orders; see Performance-2026-10-07-RetainedSubmission.md.
The new paired elevated capture awaits a retry after Windows canceled UAC.

The dominant Vulkan gap is CPU-side scene reconstruction/submission, with a
separate presentation wait. This pass changes profiling infrastructure only;
no renderer algorithm is changed to claim a gain.

Microsoft-signed PerfView3.2.8 collected CPU samples, context switches, CLR rundown,
GC/allocation events and `SEVulkan-RenderFrames` markers at the copied Pertam save,
full1920x1080. Native symbols add /Zi /Zo and linkerPDB while retaining Release
/O2 /Ob2. Production DLL remains unchanged. Native PDB1116addresses resolved with
zero unmatched; some OS/runtime symbols remain unknown or prefixed?? and are not
used for exact attribution. Capture conditions are instrumented, so these values
are diagnosis and not ordinary FPS acceptance.

Vulkan run6540b4f42af14325997bb1f194d3cdbb:331 complete frames, zero lost events,
mean marked Draw-through-Present30.11ms. CPU samples estimate24.16ms/frame;
scheduling-derived CPU24.20ms and blocked5.95ms. Non-overlapping sample buckets:
scene frontend10.22ms, packet bridge/native queue4.66ms, native record/submit/present
6.19ms, voxel frontend0.82ms and other renderer/engine2.27ms. Most blocked time
(5.38ms/frame) has Renderer::end_frame on the blocking stack and graphics-memory-
manager readied stacks. This supports a presentation/driver-wait boundary; private
NVIDIA symbols do not identify its internal cause.

Named high self-time stacks include VulkanScene.DrawWorld, SubmitCachedPart,
NativeBackend.AddRigidWorldPacket, native mesh unordered-map count/find,
WorldInstanceArena::append, sort partitioning and spot caster preparation. These
are repeated submission/data-ownership costs, not evidence to optimize one hash
function in isolation. VulkanScene.Draw inclusive10.22ms overlaps DrawWorld and
SubmitCachedPart; do not add nested inclusive values.

DX11 run5d4b9ceb667c4a5ab4e81ee9a01ef2bc:2360 complete frames, zero lost events,
mean marked4.11ms, render-thread CPU samples2.68ms/frame. GeometryStage2 grouped
GBuffer/depth draws, GeometryRenderer.Preprocess and GeometryRendererOld rendering
appear in named stacks. Worker CPU is not included in render-thread totals, so
this is critical render-thread comparison, not total application CPU comparison.

AllocationTick estimates on Vulkan's render thread sum741MB over the marker
window versus15.25MB on DX11's. Every Vulkan allocation tick has a captured stack.
Trigger stacks identify per-frame occlusion snapshot dictionaries/arrays,
VulkanVoxels.Draw byte constants, texture-cache path strings, scene float arrays,
flare results and sprite packet arrays. Tick bytes cover allocations since the
previous tick and are approximate attribution; type/stack totals are not exact
individual-object sizes. Vulkan had78 gen0/gen1 collection suspensions totalling
228ms (max10.85ms), compared with DX11's7/49.6ms. GC alone therefore does not
explain the~26ms frame. Allocation/lookup/copy work is part of the CPU frontend cost.

Next structural priorities, grounded in this trace:

1. Frame-owned retained scene/visibility data and explicit dirty updates to remove
   repeated per-actor/per-part reconstruction, snapshots and instance packing.
2. A bulk managed/native scene contract that avoids duplicate marshaling/copying
   and resource lookup for already-owned versioned records, preserving validation.
3. Presentation scheduling investigation separately from frontend CPU work;
   end_frame is the wait boundary, not yet a proved internal driver cause.

Re-profile each structural change with the same lane, then verify real gains in
ordinary full-scene comparisons. The<4.17ms and full DX11 parity goal remains open.

Artifacts: `TestResults/DeepProfile-RendererComparison.json`, each run's
`deep-cpu-summary.json`, `frame-markers.json`, `gc-events.json`, `analysis.log`,
`capture-verification.json`, plus Vulkan `allocation-stacks.json`. Reusable capture
and analysis: `Tests/ScenePerformanceHarness/Capture-DeepCpu.ps1` and
`Tests/DeepCpuAnalysis`. The first Vulkan wrapper returned early; its collector
success/merged ETL/zero-loss conversion prove the actual capture. The corrected
wrapper uses /LogFile and waits for the process, avoiding /NoGui's interactive
console behaviour. DX11 completed through the corrected wrapper.
