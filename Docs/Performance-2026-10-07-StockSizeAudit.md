# Stock actor projected-size contract audit

Corrected private full1080p snapshotc67df4b181ee496b82535ce4fe1a57c2 contains2125
main candidates.472 rigid-policy-eligible actors meet installed DX11 size-culling
prerequisites. No rendering policy was applied; this is not a FPS, index-savings
or visual-quality acceptance claim. JSON evidence:
TestResults/ScenePerformance/Vulkan-c67df4b181ee496b82535ce4fe1a57c2/GameData/Reports/stock-size-contract-audit.json.

Installed MyRenderableComponent.CheckDistanceCulling first requires raw squared
viewer distance to exceed the maximum authored LOD squared distance. It projects
(Owner.VolumeExtent,Owner.VolumeExtent,-sqrt(distanceSquared)) and multiplies its
X/Y coordinates by half the viewport dimensions. Absolute pixel-area below20
culls the actor. It does not use the LOD coefficient, cube quarter-distance or
global LOD distance controls for this test. UpdateAfterCull applies it before LOD
state update. This is a stock selection contract, not a new quality threshold.

Installed MyActor.UpdateVolumeExtent takes the maximum of LocalAabb.Extents when
parented and world Aabb.Extents when unparented. Native actors lack stock cull
proxies and their cached fields can be stale: parented suspension geometry with
1.45m world radius reported cached VolumeExtent0. The audit derives the local
extent directly, and world scene bounds directly for unparented distance/extent.
It records source/known-input flags and cached values separately.

Two preliminary readouts are rejected.42e661d... projected near-camera zero
distance before the stock prerequisite and failed JSON serialization. e928075...
serialized but its953 predictions used stale parented volume caches. Raw reports
and correction sidecars are preserved. The current parser rejects that schema;
only authoritative local/world inputs support the472 count.

Next product work must verify viewport/projection and boundary equivalence,
parent/unparented transforms, model/scale changes, LOD transitions, highlight
identity and cached shadow/probe invalidation, then compare actual stock DX11
selection/pixels and ordinary both-order timing. Static/skeletal eligibility is
separate. Existing LOD/group experiments remain off by default, and the full
<4.17ms plus visual-parity goal is not achieved.


Correction (2026-10-07 mixed pipeline audit): owning-render-job capture finds305 new main groups/962 new actors and5,125,884 new-path indexed invocations in this scene. Earlier post-frame active-view filtering omitted that path. Old-only596,418 is incomplete; all-old/large index-ratio conclusions are superseded by Performance-2026-10-07-MixedPipelineAudit.md. Old size/far policy applicability is now restricted to positively identified old actors. Historical timings/diagnostic artifacts are preserved with their original scope.
