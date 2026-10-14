# Renderer CPU phase attribution

The current normal private Pertam control is15.531ms mean draw-through-present,
14.8765ms median,15.559ms mean present interval and64.27 rendered FPS. The last
ten-second renderer Frame average is15.402ms and GPU4.735ms. The <4.17ms goal is
unmet. This source-identical private camera is not an exact frozen reproduction
of the original user screenshot; compare its view.png before claiming that.

Opt-in RendererCpuProfiler is deployed but disabled by default. CpuDetail enables
it and emits renderer-cpu-phases.json for accepted sampling frames. It records
QPC wall scopes, render-thread CPU cycles, allocations and actor/part populations.
Whole-frame OS CPU time is also recorded. No phase cycle count is converted to ms.
Workers, native allocations and allocations on other threads are excluded.
Scopes are inclusive and overlap; parent and child values must not be summed.

## Validated capture

RunVulkan-bdc68f887d614a33ba696aea3980618b contains1892 accepted frames at full
1920x1080, with the usual45-second warmup and30-second sample. Retained submission,
parallel cascades and parallel queue enabled; stock LOD/group prototypes disabled.
Renderer CPU15.435ms versus wall15.648ms indicates predominantly active CPU work,
not a large present/scheduling wait. Reported counter-read overhead0.299ms/frame
is diagnostic evidence, not a full causal estimate of profiling overhead.

The leading owned scopes are:

- Scene6.892ms,24.62M render-thread cycles; traversal6.027ms is its child.
- Prepared actor replay2.280ms over3287 actors/frame. Descriptor rebuild is only
  0.00448ms and0.108 actors/frame; cache misses cannot explain this cost.
- Legacy cached opaque parts0.800ms over2538 parts/frame; glass0.238ms over344parts.
- World queue2.132ms, including managed blob packing0.938ms and native queue1.029ms.
- Native record4.637ms,16.49M render-thread cycles. Existing native subscopes show
  sorting0.755ms, upload0.591ms, descriptor resolution0.387ms and geometry0.268ms.
- Native present0.181ms. Transparency pack plus native queue remains roughly0.1ms.

Prepared/legacy fine buckets use only QPC clocks to avoid thousands of OS counter
calls; CPU-cycle/allocation coverage there is explicitly unavailable. CPU cycles
for their broader owning scopes verify active execution without naming an
exclusive native/managed stack hotspot. About1.48MB/frame of render-thread managed
allocation is observed; this alone does not prove a GC bottleneck.

Authoritative artifacts under
TestResults/ScenePerformance/Vulkan-bdc68f887d614a33ba696aea3980618b/GameData/Reports:
renderer-cpu-summary.json, renderer-cpu-phases.json, packet-preparation.json,
native-performance.json, frame-profile.json and view.png.

## Ordinary control and measurement correction

RunVulkan-a28babf04dc9461b869545f22a185293 is1929 ordinary frames, diagnostics off.
The report validator checks identical plugin/native hashes, source checkpoint,
resolution and policy. Its mean15.531ms and median14.8765ms are the current
ordinary figures. This is a separate unpaused run; do not turn its difference
from the diagnostic into a randomized optimization or profiler-overhead claim.
Earlier queue comparisons remain historical evidence of that specific change.

Preliminary75f22016... and da868e22... captured short-phase GetThreadTimes CPU-ms.
The latter reported scene CPU8.64ms versus wall6.92ms: timer charging aliases the
short scopes. Those phase CPU-ms values are rejected. Raw reports are preserved
with correction sidecars. Final code emits CpuMs=null/CpuAvailable=false for all
phases except the whole Frame aggregate; phase attribution uses thread cycles.
Microsoft's [GetThreadTimes contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getthreadtimes)
defines kernel/user CPU time, while [QueryThreadCycleTime](https://learn.microsoft.com/en-us/windows/win32/api/realtimeapiset/nf-realtimeapiset-querythreadcycletime)
defines user/kernel cycle counts and prohibits converting them to elapsed time.

## Validation and next macro work

Both net48 and net10.0 Release builds succeeded and deployed. Measurement tests
distinguish a real sleep from active execution through cycles, account for a
known1MiB allocation, reject fabricated short-phase CPU-ms coverage, keep wall-only
buckets unavailable, prevent duplicate-frame accumulation and verify reset/off.
The production scene-message suite passes39 checks with exact slot/mutation
coverage and zero core/synchronization validation errors.

The next structural owner is scene producer/replay and retained per-view
preparation. Compare immutable worker snapshots and deterministic owner merge
against the stock DX11 scheduler. Preserve model/material/LOD changes, highlights,
exclusions, pose, visibility epochs and GPU slot lifetime. Native sort/binding
timings supply a later retained-topology target. Bulk glass queueing, rebuilding
material caches and present-wait tuning are not selected as leading work.

Base-model GPU4.735ms also exceeds the budget: verified stock visibility/LOD
workload restoration remains necessary alongside CPU work removal. Experimental
LOD/group timings do not establish complete DX11 visual parity. Lighting/exposure,
LCD/floor and particle matching remain open, and the active goal is unchanged.
