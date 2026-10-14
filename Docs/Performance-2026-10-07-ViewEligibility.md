# View eligibility experiment — 2026-10-07

The forward ordinary Pertam pair found no performance gain: control10.591ms,
treatment10.597ms (~94fps). Full1920x1080, private saved camera, copied save,
retained submission/parallel queue/cascades and experimental authored LOD,
group occlusion, stock size/far-cascade policies fixed. Profiling disabled;
external browser GPU load paused by the user. These are experimental settings,
not normal-default renderer performance or exact original screenshot camera.

The treatment skipped22.75 of2160 tested actors per frame (~1%). Main1210,
probe426.7, cascade370.6, conservative130 were retained. No extra view distance,
LOD threshold, shadow or probe quality reduction was introduced. It tests
whether already-selected opaque actors have any packet consumer, before LOD,
instance/material packing and prepared replay. Fitted masks are reused;
spot overlap mirrors native float range/cone inputs. Highlight, transparent,
static and skeletal ownership is conservative. Shadow dirty/pose processing
remains before traversal. SE_VULKAN_VIEW_ELIGIBILITY stays off by default.

82 scene checks pass, zero core/synchronization validation errors. They include
exact visible pixels for no-view/query-hidden removal, query reentry, rear
cascade caster, probe-only selection, independent highlight, live production
spotlight rotation and legacy-mode restoration. This establishes bounded
fixtures, not complete DX11 visual parity. The reverse-order ordinary pair completed: treatment10.594ms versus
control10.527ms (0.066ms regression). Both order comparisons are preserved
in ViewEligibility-Comparisons.json. No useful gain in either order; do not
tune the gate further or turn it on by default.

The forward native preparation breakdown (inclusive1.359ms) is geometry
references0.161ms, uploads/light data0.576ms, descriptor lookup0.221ms,
sort/cascade-list/indirect preparation0.398ms. Native queue validation/copy is
0.574ms (outside native render preparation). Native record total is3.331ms.
These rows have different scopes and must not all be added. Presentation is
0.139ms here; wait tuning is not justified by this accepted run.

Next structural work

1. Retain complete packet topology, rather than hashing/copying object bytes
   or repeatedly sorting unchanged draws. Map stable actor/part/LOD identity
   through managed batching and native per-slot references; publish explicit
   geometry/material/appearance/membership generations. Keep camera-relative
   constants, deforming instances, view membership and slot allocation changes
   independently dirty. Investigate whether the identified repeated work can
   actually disappear before claiming a speedup. Existing retained-object hash
   experiment and rejected byte-fill workers are not this architecture.
2. If dirty producer work still dominates after retention, move whole actor
   preparation to owned immutable snapshots and deterministic owner merge,
   following stock old-pipeline work ownership. Stock MyGeometryRendererOld
   prepares per-view filters and MyRenderingWork; its transparent path is
   separately owned. Do not concurrently mutate live actors/cache/materials,
   and do not resurrect only parallel byte-fill (~1ms regression).
3. Keep fitted caster coverage and DX11 pixel matching as acceptance gates.
   Changes/deletion/LOD fade/instancing/material and texture replacement,
   highlights/exclusions, query reentry, camera shifts, resize and both GPU
   slots must invalidate the retained graph correctly. Ordinary both-order
   full1080p comparisons follow fixture tests; profiling FPS is diagnostic.

The full <4.17ms and DX11 parity goal remains unmet. Roughly10.6ms measured
frame time needs approximately2.54x throughput; this experiment supplies no
part of that gap. GPU window3.64–3.67ms does not imply total-frame success.
Lighting/floor/LCD/exposure/particles parity remains open.

Evidence: TestResults/ScenePerformance/Vulkan-4b1fea69f03941dba682e8288913b8b2;
TestResults/ViewEligibility-Forward.json; TestResults/SceneSubmissionGpuTests.
The first read during output finalization missed the treatment report; complete
reports subsequently verified the normal paired exit, not a renderer failure.
