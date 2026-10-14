# Whole-model instance sharing — retained opt-in, 2026-10-07

Current actual-path CPU profilea2d393...2241frames: inclusive frame wall13.213ms,
render-threadCPU13.080ms, scene7.221ms, traversal6.396ms, native recording3.104ms.
New-view plan0.731/emission2.744ms; prepared replay1.559ms; blob0.624/nativequeue
0.471ms. Fine actor scopes wall-only, workers excluded, overlapping rows not summed.
Instrumented overhead makes this diagnostic context, not FPS proof. The hypothesis
selected is repeated per-part rigid instance append/lookup/bound update.

Whole-model templates retain immutable per-part pipeline/mesh/textures/tint/
emission/dither, excluding actor HSV. Templates intern once on rebuild, not per-frame
object bytes or complete frame order. Model groups use the existing cell/flags/
cascade semantics. Each eligible actor appends one64-byte transform/HSV record and
updates one union bounding sphere; all parts share that stream. Physical per-part
packets are allocated at the first actor and finalized after traversal before
QueueWorld, preserving frame packet IDs. Unsupported/one-part/nonrigid/transition/
identity-sensitive ownership uses existing part routing. Highlights and exclusions
remain individual scene submissions. Appearance/model/pose/fallback constants and
both GPU slots are preserved. Actor hints and bounded arenas invalidate by epoch.
No workers, DX11 resources, shader/LOD/quality/view distance changes.

Ordinary same-binary/source private1080p comparisons, both orders:

Vulkan-1dd0d695343d4f24a89e0bb197933f66: frame11.486616 to10.947112ms, saved0.539504ms (4.70%). GPU independent windows3.673939/3.703522ms. Actors1246,parts6494,instance writes79744bytes per frame.

Vulkan-7b37f48580ea46d78b5520b5180d5763: frame11.190541 to10.625706ms, saved0.564835ms (5.05%). GPU independent windows3.700562/3.660529ms. Actors1246,parts6494,instance writes79744bytes per frame.

1246actors/6494logical parts use79744instance bytes vs theoretical repeated415616
per-part append bytes (not GPU upload total). Physical packet count may grow when
model boundaries split previously shared part groups; native pass commands/GPU
windows are therefore retained in comparison data. No useful GPU gain established.
This reduces CPU preparation only. Other source new-view/LOD/group/old-size/far/
recipe/parallel modes fixed; profiling/async off. It is experimental-policy private
saved-camera performance, not normal-default or the exact original screenshot.
Approximately10.95ms remains well above4.17ms; full DX11 parity remains open.

Validation

World368checks pass, scene109with whole-model mode enabled pass, zero core/
synchronization errors. Tests cover exact two-slot pixels, shared instance count,
movement/current main flags, per-part tint/emission and per-actor HSV, restored
per-part constants after disabling without a pose change, scene model/material/
LOD/highlight/exclusion mutations and new per-view/spot routing. First count fixture
crossed the batch cell boundary and correctly made two groups; fixture now uses
one cell. No assertion was loosened to hide rendering failure. Counter fix adds
physical model packets to rigidBatchPackets, avoiding inflated LogicalDraws. Paired
artifacts predate only this telemetry correction; their LogicalDraws field is
inflated by model physical packets and must not support geometry claims. Native
Main/Shadow/Spot/Probe command counters and actual draw bytes unaffected.

Model batching stays defaultoff, SE_VULKAN_MODEL_BATCHING=1 opt-in. Bigger saved
scene/motion/material/identity validation is still required before default adoption.
Source model shader structures remain unchanged. Both net48/net10 Release builds
pass. Source save unchanged. AnchorModelBatches-Before-2026-10-07.zip preserves
pre-edit files. Existing user/Grok work and all prior prototypes/corrections retained.
Evidence ModelBatches-Comparisons.json, world/scene result files; CPU profile
renderer-cpu-summary.json in a2d393... . No smaller success criterion substituted.

Next macro work

Whole serial scene/traversal remains dominant and native recording persists. Use
immutable model/part templates and instance ownership as the basis for complete
producer reuse or owned preparation jobs, with deterministic merge and current
view/mutation invalidation. Do not reintroduce byte-fill-only workers (measured
regression), per-frame object hashing or ordered-frame schema caching. Current
profiling has identified owners; a post-sharing capture can refine the residual
before another architecture change. Saved-scene parity (including transition,
lighting/floor/LCD/exposure/particles and binormal/tangent basis) remains incomplete.
The full <4.17ms + DX11 pixels objective stays active.
