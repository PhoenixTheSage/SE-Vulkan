# Stock actor LOD implementation

2026-10-18 lighting acceptance update: old actor selection and newer-actor
per-view selection are now enabled by default after matched live DX11/Vulkan
geometry captures and focused GPU routing checks. `SE_VULKAN_ACTOR_LODS=0`
and `SE_VULKAN_NEW_ACTOR_VIEWS=0` retain diagnostic opt-outs. See
LightingParity.md for current evidence and remaining GBuffer LOD-channel work.
The default-off descriptions and performance numbers below are historical.
Skinned old-pipeline actors are now included as well, verified against direct
authored geometry with nonuniform live poses and the existing 60-bone palettes.
Static-group ownership remains a separate unverified selection path.

Update: the earlier measurements below predate correction of unparented native
actors' missing stock WorldAabb cache. Near-actor distance is now based on the
authoritative world bound; see Performance-2026-10-07-GroupOcclusion.md for fresh
corrected combined-path timings. Preserve these raw prototype measurements as
history, not acceptance evidence for the newer artifact.

The old DX11 actor path selects one authored model level using camera distance,
FOV/resolution coefficient, global distance controls, GBuffer min/shift, and
quarter distance for cube instance buffers. Its two-mesh transition uses stock
object custom-alpha values and a bounded time/distance progression.

The experimental Vulkan path now imports the root LOD metadata before following
GeometryDataAsset, retains authored distances, lazily loads selected geometry
with the same rescale, and preserves actor root bounds. Material/pipeline records
are retained per representation; switching levels does not rebuild every part
each frame. Both transition representations retain their own constants. Duplicate
or missing levels render the conservative base fallback without fading it twice.
LOD/alpha changes invalidate affected spot caches and force dynamic cascade replay
through the existing geometry paths. Highlights use selected-model section ranges.

`SE_VULKAN_ACTOR_LODS=1` enables this experiment; default is off. Static groups and
skeletal actors retain their existing base paths while their separate stock
contracts are verified. No shader source or native ABI is changed. Existing
retained submission and parallel cascades remain on in comparison lanes.

Ordinary same-source Pertam save, full1080p, both orders:

- Base then LODs:15.657→13.705ms CPU, saving1.952ms (12.47%).
- LODs then base:13.834 versus15.631ms CPU, saving1.797ms (11.50%).
- Candidate present intervals13.72/13.86ms, about72–73fps.
- First candidate GPU average3.185ms; the frame total still exceeds4.17ms.
- Submitted packets~10800→7984, logical parts~18690→12310. The candidate selects
  levels for~3128 actors, reduces~2505, with~0.54 transitions/frame in the first run.

29 production scene GPU checks cover selection settings, transition state,
authored asset pixels and base rollback. The selected SciFiWall level produces
visible world pixels and matches direct rendering of its stock LOD asset.
257 world checks include visible object-alpha dithering, exact prepared-transition
pixels and clearing transition alpha without a pose change. Vulkan core and
synchronization validation is clean; net48/net10 Release builds pass.

The full-scene screenshots were inspected against DX11. Lighting/exposure and
floor/LCD differences remain; tests and faster timing do not establish complete
  visual parity. The new instrumented workload snapshot Vulkan-fed0a25e... maps
every scene packet: main indexed invocations24581913→6344400 (74.19% less),
main packets4318→3111, with2129 main candidates in both snapshots. It still
accepts zero occlusion snapshots, so excess visibility work remains. This is
diagnostic geometry evidence, not additional ordinary FPS proof.
Raw ordinary results: Vulkan-a1136ab6... and Vulkan-8c1c64cd...;
validated comparison: TestResults/ActorLods-CleanComparisons.json.

Next macro work remains stock group occlusion/visibility ownership, with separate
static/skeletal LOD contracts and parallel preparation afterward. Current
occlusion rejects every snapshot despite millions of hidden results. Preserve
stock conservative inflated bounds, near-camera/reentry behavior and lifetime;
do not simply remove exact camera/revision guards on uninflated actor queries.
The<4.17ms frame total and full DX11 parity goal remains open.


Correction (2026-10-07 mixed pipeline audit): owning-render-job capture finds305 new main groups/962 new actors and5,125,884 new-path indexed invocations in this scene. Earlier post-frame active-view filtering omitted that path. Old-only596,418 is incomplete; all-old/large index-ratio conclusions are superseded by Performance-2026-10-07-MixedPipelineAudit.md. Old size/far policy applicability is now restricted to positively identified old actors. Historical timings/diagnostic artifacts are preserved with their original scope.
