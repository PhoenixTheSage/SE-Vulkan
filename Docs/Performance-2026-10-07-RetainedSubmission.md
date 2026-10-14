# Retained actor submission

The profiler identified repeated scene traversal/packing and managed/native
ownership as major CPU costs. This change prepares opaque non-skinned actor
material descriptors when model, material revision, appearance or instance type
changes, then replays them with current transforms, instances, visibility,
shadow masks and bounds. Skinned, glass, highlighted and explicitly excluded
actors retain the existing path and individual draw identities.

The flat bridge owns one device-aligned constant blob and one instance blob per
queue call. Offset records eliminate pointer fixups, draw-array marshaling and
duplicate native instance deduplication. Native validates every packet and owns
both blobs before the managed arrays unpin. GPU uploads still use the existing
fenced frame slots. Eight token-owned visibility snapshots and packed occlusion
boxes reuse capacity; completed results are consumed before a snapshot is reused.

Ordinary copied Pertam save, full1920x1080, VSync off, cap1000, parallel cascades
on, other experimental GPU/object policies off, external GPU load removed:

- Legacy then prepared: CPU19.023 →15.746ms, saving3.277ms (17.23%).
- Prepared then legacy: CPU15.639 versus18.448ms, saving2.809ms (15.23%).
- Candidate present intervals15.77/15.66ms, about63.4/63.8fps.
- Native queue in first pair2.004 →1.428ms.
- Native Present is now~0.19–0.20ms. The previous5–6ms wait measured under
  external GPU load must not be used as the current bottleneck.
- Fresh DX11 CPU Draw-through-Present3.534ms; present interval4.244ms,
  235.65fps. These scopes differ and must not be conflated.

Steady candidate replay covers~3287–3293 actors /14334–14340 parts, rebuilding
only0.071–0.108 actor/frame. Both-order comparisons use identical release hashes.
Unpaused physics/selection drifts slightly; exact equivalence is checked separately.
Paused legacy/prepared/legacy captures keep18690 logical and10804 physical draws.
Legacy-to-prepared mean RGB difference0.000090/255 is below repeat-capture noise;
return and repeat controls retain small temporal differences. This establishes
change-local equivalence, not full DX11 visual parity.

Validation:253 world GPU checks,179 postprocess checks,51 voxel checks,18
production scene-message checks,7 native CTests. Core/synchronization validation
is clean. Tests exercise both slots, moving poses, unchanged-pose batching
switches, tint/Hsv changes, generic/cube streams, copied-source ownership,
invalid offsets and missing geometry, material visibility and model replacement.

Boundary review: actor signatures cover all descriptor-changing production
messages; SetModel always invalidates even for the same cached model. Dynamic
selection flags remain outside the signature and are read each frame. Highlight
and shadow-exclusion paths do not lose individual IDs. Rigid hints remain
backend/epoch/cell/flag guarded. Rejected native blobs leave queued=false;
buffer lengths, alignment, pipeline layouts and resources remain validated.
No Vulkan synchronization, quality or shader semantics changed.

Evidence: TestResults/RetainedSubmission-CleanComparisons.json; pixel evidence
in Vulkan-4384ca2ec35a4c87a2a2064a381395ca; tests under the named TestResults
directories. A fresh paired deep CPU capture under clean GPU conditions is
waiting for a retry after Windows canceled the UAC launch. The validated path
is enabled by default; SE_VULKAN_RETAINED_SUBMISSION=0 restores the legacy path.
Re-rank a fresh trace before choosing the next
structural change. The<4.17ms performance and DX11 parity goal remains open.
