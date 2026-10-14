# Parallel queue validation and transform notification ownership

Normal full1080p private Pertam workload: serial validation15.248650ms versus
parallel14.911961ms CPU draw-through-present (2.21%). The renderer now enables
bounded parallel immutable-blob validation on CPUs with at least four hardware
threads. SE_VULKAN_PARALLEL_QUEUE=0 retains serial validation. Small batches and
legacy/retained-object calls remain serial. This is above the <4.17ms target.

Two ordinary orders with experimental stock actor LODs and manual-group queries:
13.866896→13.423985ms and13.443247 versus13.063680ms (2.82–3.19%). Native validation
931–948us becomes551–565us; four workers inspect about8087 immutable records.
Copy remains about211–217us. These combined figures are experimental, not the
normal default workload. LOD/group policies remain off by default.

Machine-readable proof: TestResults/ParallelQueue-CleanComparisons.json generated
by Tests/ScenePerformanceHarness/Summarize-ParallelQueueTimings.py. Full1920x1080,
validation off, copied source world, warm45/sample30 seconds, then warm5/sample30
for the second policy. Source Sandbox.sbc SHA remains
D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.
Comparison native SHA E165E0E5BD4C490234D67C88E537B92721B1C7F9EAC759B23CEC08120B040DE0;
default-policy deployment SHA2600E25A9B3A2D093A456063F6FD5193CE4DF614EE02F670A949524F6084E2AE.
Only the implicit default changed after explicit-policy comparisons.

## Ownership and validation

The render owner copies and sizes destination arrays before spawning up to four
workers. Const resource maps and shared validation logic preserve every packet,
mesh, texture, layout, instance, offset and budget check. Workers write disjoint
records and maintain independent texture-validation caches/counters. All futures
complete, including exception paths, before unpinning input or returning. Queue
publication follows successful completion. Vulkan calls, allocation, uploads,
descriptor state and slot fences remain on their original owners.

A fresh managed capture exposed a separate transform-notification race: stock
MyActorUpdater executes component callbacks through blocking Parallel.ForEach;
OnMove mutated render-owned HashSets from those workers. Callbacks now publish
actor identities through ConcurrentQueue. The render owner drains before and
after the updater and owns recursive dirty propagation. A20,000-notification
concurrency fixture preserves exact pixels without collection corruption.
Failed run48ab75f... retains the terminal renderer exception and logs. Retry
4e1f7c... completed its managed trace and supplies the current CPU boundary evidence.

Validation: canonical Release native build/deploy and seven CTest checks;
263 world GPU checks (4096-draw parallel batch, both slots, exact pixels,
malformed last-worker input and valid retry),39 scene checks and51 voxel checks,
with zero core/synchronization validation errors. No full DX11 visual-parity claim.

## Remaining investigation

Managed sampled thread-time shows scene traversal and world queue as major
boundaries, but includes waits and opaque native/PollGC leaves. It does not prove
exclusive CPU or a GC bottleneck. Opt-in renderer CPU phases now split active
render-thread CPU cycles/time, wall scopes, allocation and actor-path populations.
Use them before selecting the next structural change. Material/scene preparation,
native binding/sort/record and excess stock workload remain candidates; the goal
and unresolved lighting/exposure/LCD/particle parity remain open.
