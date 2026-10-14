# Native immutable recipes — retained, 2026-10-07

Stable validated recipe handles replace full schema submission for opaque world
packets. Default enabled, SE_VULKAN_WORLD_RECIPES=0 restores legacy blob queue.
No actor/LOD/quality/view distance or GPU recording reduction is introduced.
The prior whole ordered-schema cache is rejected; recipe reuse here does not
require order/count to match previous frames. Sorting and view selection remain
current and unchanged.

Ordinary same-binary, same-source full1920x1080 comparisons, both orders:

Vulkan-f7eab8f848424ab69df041e22da3d580: control10.811773ms vs recipes10.573819ms, saved0.237954ms (2.20%). Frames2771/2833. Material reuse99.996920%, revalidated0.062831 recipes/frame. Present0.141/0.139ms. GPU windows3.626075/3.669500ms.

Vulkan-07078bb414f645cf887a2dafffc601a9: control10.779874ms vs recipes10.549792ms, saved0.230082ms (2.13%). Frames2779/2840. Material reuse99.997518%, revalidated0.063028 recipes/frame. Present0.142/0.138ms. GPU windows3.679068/3.629482ms.

These use a copied save/private saved camera, fixed experimental authored LOD,
group/size/far-cascade policies and retained/parallel submission. Profiling,
async present and failed view-eligibility policy off. They are not normal-default
renderer performance or the exact original screenshot viewpoint. Approximately
10.55ms still fails <4.17ms. GPU independent sample windows do not establish a
speedup. Full DX11 lighting/floor/LCD/exposure/particles matching remains open.

Architecture and lifetime

56-byte immutable recipe registration validates pipeline/object layout, mesh and
material resources once.48-byte dynamic draws carry generation-bearing handle,
current count/flags/cascade mask, aligned object/instance offsets, origin/radius.
Owned blobs copy dynamic bytes before queue returns. PreparedPart, RigidBatch,
legacy PartFrame and voxel Part retain recipe hints; other opaque producers use
the same immutable-key registry. Frame-local PacketIndex remains a draw identity
for highlights/exclusions, never a retained recipe identity. Multiple physical
draws can share one recipe.

Native reverse dependencies invalidate only affected recipes on pipeline/mesh/
texture/voxel/array replacement. Mesh deletion retires slots; new registration
uses a new generation and old tokens reject. Managed mesh removal marks owner
hints dead and removes registry entries. Missing textures reject revalidation;
recreation at the same identity works. Dependency sets update when voxel material
arrays change. Context reset/dispose clears managed recipes, native cleanup
invalidates generation lifetime. Table bounded65536 slots; caller addresses are
never stored. Queue publication is transactional through world.queued=false
until validation and copies succeed; malformed spans/counts reject.

Recipe geometry references and per-slot material sets skip resource resolution
and descriptor lookup. Existing completed-slot pool ownership remains: full
reset/buffer growth changes material epoch. Resource dirtiness clears only its
recipe references before sets are retired. Transient frame-pool fallback sets
are never retained. Render checks queued recipe generation; replacements between
queue and rendering must revalidate before use. GPU synchronization, byte uploads,
view membership and command recording are unchanged.

Validation and deployment

Native Release/Test/Deploy passed all7CTest. World suite361checks includes all20
material variants (rigid/skinned/generic/cube/deformed) with exact pixels on both
slots, current depth partners, native blob ownership, resize, packet permutation,
unrelated resource update, dependent replacement, mesh/texture removal/recreation,
stale recycled generation and malformed compact ABI spans/count/reserved fields,
valid retry and teardown.83scene checks plus default-on assertion make84, covering
actual message/material/LOD/highlight/group/query/size/far transitions with stable
recipe reuse. Voxel51checks pass. Zero synchronization/core validation errors.
First fixture teardown caught two extra test meshes not removed; fixture cleanup
fixed before acceptance. These fixture results do not prove complete DX11 parity.

Both net48/net10 Release builds deployed to Pulsar Legacy/Interim. Installed
managed/native hashes match build outputs (Recipes-Deployed.json). Native SHA256
7D743587EAEF7184F80AD4CD9E4962A1E977A307AD23F73C96D7A0F146624118.
Paired benchmark plugin SHA39D71CF... predates only changing the default initializer
to enabled; benchmark0/1 overrides select exactly the same implementation. Final
Interim managed SHAFCB2FC20971C97CABDD2167D035E907DA05AF25109CE19504F381616CDCB42E7.
No FPS claim from instrumented or synthetic measurements. Source save hash
D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92 unchanged.

Evidence: Recipes-Comparisons.json; Recipes-Deployed.json; Recipes-WorldChecks.txt;
SceneSubmissionGpuTests/result.json; VoxelGpuTests/result.json. Source anchor:
Recipes-Before-2026-10-07.zip. Existing user/Grok edits preserved.

Next macro owner

The change removes repeated immutable work but saves only~0.23ms end-to-end.
Forward native queue0.574 to0.439ms, geometry0.193 to0.107ms, descriptors0.222 to
0.040ms; sorting~0.390 and uploads~0.570ms persist. Parent scopes overlap; do not
sum these with entire native record. Scene producer/traversal, world packet
packing and actual recording still dominate the CPU gap. Resolve the remaining
main-view workload difference from stock DX11 using exact selected actor/part/
instance and authored-LOD sets; current native~2389main commands and prior stock
selection summaries are not proof of equal visibility. Then redesign whole owned
scene preparation or persistent packet/selection production where evidence shows
work can disappear. Do not return to byte-fill workers, per-frame object hashes,
whole ordered-schema matching or small predicate tuning. New CPU capture on the
retained implementation may refine that ranking; old overlapping phase totals
are diagnostic context, not current post-change proof.

Reference-only research: [Khronos descriptor management](https://github.com/KhronosGroup/Vulkan-Samples/blob/main/samples/performance/descriptor_management/README.adoc)
recommends caching and removing descriptor lookup indirection by retaining handles
with materials/meshes, tied to buffer lifetime. Source map and stock
MyGeometryRendererOld/MyRenderingWork references remain in the canonical plan.
No donor source copied. Entire <4.17ms plus DX11 parity objective remains active.
