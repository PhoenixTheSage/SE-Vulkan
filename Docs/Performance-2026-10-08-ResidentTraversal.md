# Resident traversal experiment closeout

The actor cull/view-planning stage did not produce a reliable frame-time gain.
The renderer integration is rejected and restored. This closes this experiment;
it does not complete the larger resident scene/emission replacement or the
<4.17 ms / DX11 visual-parity objective.

The prototype gave a native generational actor registry committed double bounds,
translation, hierarchy visibility, stock new-pipeline selector state and camera
history. Owner dirty updates followed transforms/geometry plus direct actor
visibility and parent callbacks. Two bulk passes evaluated culling, current
occlusion inputs, LOD transitions, cascades and spot overlap. Ordered candidate
selection still came from the existing spatial index. Lazy model selection,
representation switching and DrawWorld emission remained managed. Consequently
this tested removal of culling/view planning, not the full mapped native scene
and emission contract.

Correctness evidence: all eight native CTests passed. The experimental scene
suite passed 276 checks with zero core/synchronization validation errors. Exact
pixels and ordered logical selections matched across three render slots for
committed transform, appearance, model replacement, parent transforms, direct
ancestor visibility changes/restoration, highlights and same-ID actor recreation.
The first native frame after mutations was compared before a mode resync. Native
selector state matched managed progression across 23 distance/boundary/camera-jump
steps. This fixture evidence does not establish full game DX11 visual parity.

Ordinary private Pertam save at 1920x1080, render fraction 1, VSync off, frame cap
1000; fixed accepted bulk/recipe/new-view/group/async/parallel policies. Initial
warmup 45 seconds, each sample 30 seconds, switch settling 5 seconds. Same source
checkpoint and exact managed/native/harness hashes in both orders. Native mode
was active for 2816 resident candidates and 2683 selected actors; dirty updates
averaged 244.38 and 239.93 per frame. No profiler or validation layer was enabled
for these timings.

- Forward `Vulkan-bd7173728ac64eab9e36c8c0e278ae52`: managed 12.739564 ms,
  resident 13.137504 ms. Regression 0.397941 ms (3.12%). Completed present-call
  cadence 12.768426 -> 13.162046 ms.
- Reverse `Vulkan-fb443553b9504116b225bdfc653bc80b`: resident 12.914327 ms,
  managed 12.908546 ms. Regression 0.005782 ms (0.04%), effectively flat.
  Completed present-call cadence resident 12.944144 vs managed 12.933162 ms.

Independent unpaused simulation can drift; the two orders provide no consistent
speedup. Selection/activation were verified by the historical summarizer and
`TestResults/ResidentTraversal-Comparisons.json`. The native mode still submitted
10584 logical draws and around 7082 physical draws, with about 2518 main, 19849
cascade and 1795 spot commands in the forward sample. These counts identify work
left in this design; they do not by themselves establish its causal CPU cost.

Archive `TestResults/ResidentTraversal-Rejected-2026-10-08.zip`, SHA256
`3DFE485F18258802A78B7EA2CE10EF42390BFA55E513055A358C022194EF12BB`, contains
relative source paths, hashes, test logs and both-order comparison evidence.
Protected sources restored from `ResidentTraversal-Before-2026-10-08`.
Experimental managed interop/integration files are removed; production ABI and
harness restored. The CPU-only numerical core/registry and its standalone tests
remain isolated from the renderer for future contract validation.

Next structural boundary: a full resident scene producer must emit current bulk
instances natively, including current poses/material resources and lazy LOD
bindings, rather than returning all view slices to the same managed emission
loop. The fresh verified ETW attribution remains the prioritization evidence:
scene 5.845 ms, native record/submit/present 3.700 ms and bridge/queue 1.638 ms of
active CPU per marked frame. These disjoint sampled buckets come from an
instrumented run, not this ordinary pair. A further cull-plan wrapper or local
copy/allocation retune is not supported by this result. Command generation should
also be analyzed against its actual driver/record stacks before choosing a GPU
indirect architecture. Neither change has a validated gain yet.

Restoration verified: protected source hashes match the anchor. Exact accepted
managed C7F6595D...A251281 and native 3478C4B3...524EDB9 artifacts are deployed.
The source rebuild passed all eight native CTests; its relinked DLL hash differs,
so the original accepted native artifact was restored from the verified prior
bulk benchmark deployment. The final scene suite loaded the accepted 3478C4 DLL
and passed 180 checks with zero validation errors. Both private benchmark games
exited. See TestResults/ResidentTraversal-Restoration.json.
