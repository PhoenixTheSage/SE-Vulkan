# Native model/group submission

The current profile identifies scene traversal/emission as the dominant CPU
owner. The bulk path registers each immutable generic model's parts, recipe
generations and material constants once. Managed code publishes one 120-byte
current pose/HSV/view/mask/cell/bounds/order record per eligible representation.
Native groups own instance gathering and part expansion. First-seen groups merge
with the ordered legacy stream, and a physical-index remap preserves highlight
and shadow-exclusion consumers. No per-actor native call or shader change occurs.

Current LOD and visibility decisions remain managed and current. Mixed-HSV parts,
skin/cubes, transitions, transparency and sensitive actors retain the existing
route. Native template dependencies reuse recipe lifetime/generation checks;
unrelated mesh retirement cannot discard pending records. Changed dependencies
re-register their templates, and frame-boundary limits bound registry residency.
Actual GPU payloads still belong to completed/fenced slots. This is model/group
ownership, not persistent object uploads or frozen scene/view output.

471 world GPU checks and 180 scene checks pass, with zero core/synchronization
validation errors. Seven native tests and net48/net10 Release builds pass.
Coverage includes exact pixels, ordered logical owners/parts, both frame slots,
visible highlights, exclusions without caller-array mutation, masks and motion,
per-material tint/emission/visibility and actor appearance revisions, stock
per-view LOD representations, mesh/texture replacement, unrelated retirement
while records are pending, bounded managed/native cache reclamation, model retirement, malformed spans/handles/orders,
and switching back to the managed reference without a pose change.

Initial ordinary both-order pairs (before the selective-retirement audit fix):

- 04181914: managed 13.957516 ms, native 13.271177 ms; saved 0.686338 ms (4.92%).
- ac6f1c57: managed 13.520287 ms, native 12.768452 ms; saved 0.751835 ms (5.56%).

Native telemetry confirms 2289 representation records expand into 4207–4222 part
packets/frame, with 2868–2869 legacy packets. Logical draws remain about 10584;
GPU rolling means 3.899/3.810 ms. Native grouping adds queue CPU while removing
managed replay/publication work; only the full-frame paired result is acceptance.
Initial measurements are preserved in BulkModels-InitialComparisons.json.

Final deployed-build ordinary acceptance, with both lifetime corrections:

- a1166b6f: managed 14.622245 ms -> native 13.421813 ms;
  saved 1.200433 ms (8.21%).
- 7bcc9722: native 13.221688 ms versus managed 13.961346 ms;
  saved 0.739658 ms (5.30%).

Completed native present-call cadence improves in the forward pair from 14.652358
  to 13.446514 ms, matching the full-frame gain. These are completed calls, not
  monitor scanout FPS. Final telemetry still has 2289 representation records and
  about 4212–4232 expanded parts/frame; logical draw count remains about 10584.
  GPU rolling means 3.863/3.866 ms. All workload policies except bulk ownership
  match within each pair. Unpaused scene state can drift, so the saving range is
  more defensible than one fixed claimed number. BulkModels-Comparisons.json and
  Summarize-BulkModels.py preserve activation/policy/build/source checks.

Native DLL: 3478C4B327469A762DD19CF7A095D2804F35E3462AD75EF67F66AB1CA524EDB9.
Final managed: C7F6595D1B2B55FD8B2C6DD97640BBE9FC454A31E9C9CB267E0C5ED11A251281.
Private copied source checkpoint: D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92
is unchanged. This saved camera is not proved identical to the original human
screenshot. Independent unpaused scene state can drift; diagnostics are excluded
from ordinary comparisons. Full visual parity and <4.17 ms remain unachieved.

Policy remains opt-in: `SE_VULKAN_BULK_MODELS=1` and currently requires existing
retained submission, native recipes and model batching. Wider normal-policy
adoption needs its own full-quality validation. Rollback anchor BulkModels-Before
contains relative paths and hashes; newly added NativeSceneModels.cs and
world_models.inc must also be removed if reverting. Keep earlier async-present
and probe corrections intact.

Post-change diagnostic fa84af56 uses the identical accepted managed/native/source
hashes and matched policies. The helper verifies that control pairing, including
new-view, model, recipe, async-present and bulk flags. It records 1924 complete
frames: frame wall 15.218 ms, owning-thread CPU 14.675 ms, counter reads 0.809 ms.
These are instrumented observations, not another FPS result. Inclusive scopes:
scene 7.497 ms, traversal 6.390 ms, new-view emission 1.959 ms (prepared replay 0.626
nested), planning 0.784 ms, queue 1.562 ms (blob 0.307/native recipe queue 0.297
nested), native record 3.601 ms. Native bulk expansion is inside the full queue
scope, outside the recipe-only sub-scope. Do not sum overlapping values or infer
exclusive CPU changes between independent diagnostic runs.

Scene traversal remains the next dominant ownership unit. Continue toward
persistent actor/view records and dirty state updates that remove broad repeated
preparation, preserving current camera, LOD transitions, visibility, resources,
identity consumers and all dynamic content. Existing model registrations and
bulk frame ownership provide the boundary; local clock/hash/copy tuning and the
rejected worker/cache designs are not the next experiment. Normal-policy
independence/adoption remains unproved and cannot be inferred from these pairs.
