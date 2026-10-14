# Accepted bulk path CPU attribution

Fresh ETW run20eb1f1c uses the accepted bulk/model/new-view/recipe/async-present
policies at1920x1080 in the copied Pertam save. The copied optimized profile DLL
and PDB match GUID eec0f6c8-e976-4c94-a99b-57adef905a42, age4. Profile DLL SHA256
F94AFA11B7BC2EB7E428F2EDE913EB2543EEBD84CE60E1E2D1775804164BA969;
PDB F7219D0FDD78100FAD91F490F7D3A20555C29A1D1FA056472F41BC246C737F01.
Production native asset3478C4 and managedC7F659 remain unchanged.

The trace contains690 complete frames,1382 markers, zero lost events. All1278
sampled native addresses resolve to209 distinct symbols, with zero unmatched.
Render thread28628 belongs to private process4060; capture and game processes
exited. The accepted source checkpoint remainsD5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.

Non-overlapping active CPU sample weights per complete frame:

- Scene frontend5.844928 ms.
- Native record/submit/present3.700000 ms.
- Packet bridge/native queue1.637681 ms.
- Voxel frontend0.934783 ms.
- Other renderer/engine1.846377 ms.

Separate scheduling categories give13.973494 ms active CPU and0.644405 ms blocked
per frame. Marked wall mean is14.592859 ms. This is instrumented attribution,
not ordinary FPS or a causal comparison against an earlier capture. A majority
of the remaining time is active work; moving the API return cannot erase it.

Nested inclusive sample costs overlap: DrawNewActorViews1731/690=2.509 ms,
DrawWorld1230/690=1.783 ms, native render_world2414/690=3.499 ms, and native
append_models610/690=0.884 ms. Native driver leaf samples are893/690=1.294 ms,
overlapping their callers. Managed self samples include DrawNewActorViews599,
Draw415, IsVisible230, DrawCandidates227, and backend SubmitPreparedActor292 ms
over the complete window. These are not an additive decomposition of scene work.

The rejected resident binding producer still executed essentially the same
actor/view traversal and transform/material publication in another call chain.
It did not prove a useful full-frame gain and remains removed. Future work needs
to remove complete actor/view preparation and command construction, guided by
this active-CPU evidence. A retained actor input contract must cover hierarchy
visibility, current bounds/transforms, camera/frustum/distance tests, current LOD
state including transitions, lazy resources, spot/cascade selection, logical
ordering and all dynamic/identity-sensitive fallbacks. Existing dense model
registrations are one boundary, not proof that an entire resident scene works.

Capture-DeepCpu.ps1 now accepts explicit BulkModels and AsyncPresent so current
policy can be reproduced. The private elevated wrapper used the user's existing
capture authorization. No production DLL was replaced by the symbol build.
Evidence: deep-cpu-summary.json, accepted-bulk-cpu-attribution.json, frame-markers,
analysis.log and the merged nonempty cpu.etl under the run directory. The full
<4.17 ms frame and complete DX11 visual parity goal remains active and unachieved.
