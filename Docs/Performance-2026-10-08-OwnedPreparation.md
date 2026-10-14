# Rejected complete actor preparation experiment

Current ETW identifies scene preparation as the largest active render-thread CPU
bucket. The prototype moves new-actor LOD/view/cascade/spot planning and cached
representation transform/constants preparation into exclusive actor units. Four
workers complete before original-order owner publication. Native resources,
occlusion publication, shared batches/recipes, highlights and exclusions remain
owner operations. Cold/dirty/transparent states finish on owner using the already
computed plan, without advancing LOD twice. No worker GPU calls or shared backend
dictionary writes occur. This is broader than the rejected byte-fill-only design.

124 scene GPU checks passed with zero core/synchronization validation errors.
Coverage includes exact two-slot pixels, actual multiple worker threads, new actor
resource misses, appearance/model changes, highlights, movement, current cascade
masks and exact ordered logical owner/instance identities. The full-scene capture
processed 2683 actor jobs/frame on four threads, approximately 2575.9 (96%) with
cached complete CPU outputs and 107.1 owner completions.

Ordinary 1080p Pertam both-order comparisons reject the implementation:

- Vulkan-77bdfedd87d74968b89e6e09decbace9: serial 18.570998 ms, owned 18.445797 ms;
  saved 0.125201 ms (0.67%).
- Vulkan-602b03ab9b3d4e4f805049d74727ee1d: owned 18.793570 ms, serial 18.022845 ms;
  added 0.770725 ms (4.28%).

Both runs have identical managed/native/source hashes and fixed experimental
policy. Each phase warms/samples separately; unpaused state and presentation
conditions remain caveats. No useful robust frame gain is established. Do not
tune this worker design. The production/harness files were restored byte-for-byte
from the anchor and the added worker source removed. Restored scene has 113 checks
and zero validation errors. The prototype and path/hash manifest are preserved in
TestResults/OwnedPreparation-Rejected-2026-10-08.zip (SHA256
E15FDE11428907788B1DAFA14C291954C9C977FF3DFBED0585DA49736CA8AFEB).
The reusable summary reads preserved measurements; it is not an active feature.

The remaining decision moves beyond these actor jobs. Investigate retained
scene/group ownership that removes repeated work, and separately test existing
presentation overlap under the currently observed roughly 4.7 ms blocked call.
Earlier overlap pairs used roughly 0.15 ms calls and cannot predict this condition.
The full <4.17 ms total and DX11 visual parity goal remains open.
