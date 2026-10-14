# Manual-group query ownership

The experimental group path follows the stock DX11 ownership model: query one
inflated manual-group bound, asynchronously consume results, and keep newly
entered or near-camera groups visible. It retains main-view culling separately
from shadow/probe submission. Standalone actors remain visible in this policy.

Vulkan actors lack DX11 render components/cull proxies. Group bounds therefore
come from the scene's authoritative transformed member bounds, refitted when
members move or change. Query identities are unique to group instances. Submitted
tokens own group/epoch/frame/bounds records; recreation, membership, reentry,
near-camera, viewport/FOV/disable transitions cannot reuse old hidden epochs.
Groups moving outside a queried inflated volume become visible. Results expire
after the stock bounded frame timeout. No global revision or exact camera matrix
comparison is imposed on every completed query. Native synchronization is unchanged.

Tests exercise submillimetre camera movement, near-camera/reset rejection,
moving bounds, actual hidden main geometry, pixel preservation behind an opaque
occluder, and revealing the group after deleting that occluder. 38 scene checks
pass with clean core/synchronization validation; net48/net10 Release builds pass.

Review also found a LOD distance bug: unparented native actors lack the stock
cached WorldAabb. That branch now uses the scene's authoritative world bound;
parented actors retain the stock local-space distance calculation. A near-model
test prevents accidental lowest-level selection from an empty proxy cache.
Earlier experimental LOD/group timings predate this correction and must not be
treated as validation of the corrected artifact.

Corrected ordinary full1080p Pertam comparisons, LODs/retained submission/parallel
cascades fixed on, both orders:

- Legacy then groups:13.933→13.496ms, saving0.437ms (3.14%).
- Groups then legacy:13.483 versus13.792ms, saving0.309ms (2.24%).
- Group snapshots show88 owners, accepted results and millions of lifetime scene
  culls. Main commands fall substantially, while shadow/probe data still submits.
  These counters are cumulative, not per-frame rates.

Paused legacy/group/legacy full1080p sequence Vulkan-63a791c... preserves logical
coverage. Mean RGB cross-mode deltas0.000194/255 and0.000120/255 are within the
repeat/return controls. Applied scene culls are independently recorded in its
renderer status. This is change-local pixel equivalence, not full DX11 parity.
The first pixel wrapper lacked direct group-counter fields; later captures include
them. No ordinary FPS claim uses this diagnostic.

Default remains off: SE_VULKAN_GROUP_OCCLUSION=1. Actor LOD selection also remains
experimental. The current combined13.48–13.50ms does not meet4.17ms. Next macro
work is remaining stock visibility/size selection and parallel/retained preparation
of shadow/probe and scene data; do not replace this with a faster hash/query helper.
Lighting/floor/LCD parity remains open.

Artifacts: TestResults/GroupOcclusion-CorrectedComparisons.json; ordinary runs
Vulkan-3142c7... and Vulkan-7efcc0...; pixel-delta.json under Vulkan-63a791c...;
GroupOcclusion-SceneTests.txt and SceneSubmissionGpuTests/result.json.
