# Structural performance implementation

User override, October 7: use CppStudio to map and plan; no separate planning
skill installation is required. This document is the implementation plan for
that authorized workflow. The earlier missing-package block is superseded.

## Requirements and ownership

Target remains mean AND P95 full 1080p render-frame below 4.17 ms, matched DX11
visuals and preserved gameplay at full scene density/quality. The approved
comparison uses the private Pertam source save and matched camera; original
screenshot camera equivalence is unproved. The target is OPEN. Native owns
GPU resource lifetime, command recording and queue submission; managed scene
code owns fallback consumers and commits resident actor/resource changes.
No parallel implementation agents are active. Existing user changes remain.

## Current approved sequence and continuation

The user's DX11 parity implementation plan supersedes the historical sequence
below. This is the sole durable implementation/continuation document.

1. Matched system comparison: identical source/camera/settings/sun, both DX11
   geometry pipelines and static groups, per-view owners/LOD/ranges/materials,
   ordinary render-frame and completed-presentation mean/tails. Paused captures
   establish selection/image evidence; unpaused runs establish performance.
2. Installed stock shadow parity: cascade fit/schedule/usage/freeze/jump rules,
   retained image/matrix validity, stock size/group culling, dedicated model/LOD
   depth geometry and spotlight ownership. Scheduling has reproducible ordinary
   historical ordinary gains of about1.17ms; repeat under full camera locking is
   required before treating those comparisons as final matched evidence.
   Full cycle/visual acceptance remains open.
3. Complete native per-view preparation through direct emission: immutable
   shared geometry/bindings, dependency-specific resident actor inputs, selectors
   once per frame, stock model/LOD/view/state groups and instance streams.
   Required occlusion IDs, missing resources and explicit fallbacks return to
   managed code; supported actors bypass managed representation/packet replay.
   Old, skeletal, deformed, transparent and unsupported consumers retain explicit
   fallback contracts. Current producer remains opt-in and unadopted. Corrected
   full-camera comparisons establish0.53/1.00ms mean gains in both orders,
   with lower P95 and completed-presentation tails. Full-frame times remain
   above11ms and source visual/ownership parity gates remain open. Earlier
   ordinary windows lack complete camera authentication (see recovery).
4. Re-profile the complete renderer after each structural treatment. Latest
   detailed captures identify scene preparation and recording as dominant.
   Address voxels or lighting only if they become dominant. Additional
   architecture requires an updated proposal. No new worker architecture,
   hash/copy tuning or rejected cache/indirect wrapper integration in this pass.

GPU indirect is deferred until corrected pass/group architecture is measured.
The accepted renderer remains the rollback baseline, with protected artifacts
in Dx11Parity-Before-2026-10-08/manifest.json and the accepted stock scheduling
comparison. Normal gameplay defaults change only after correctness and
reproducible full-frame gates pass. Final acceptance requires three both-order
pairs with45-second warmup/30-second sampling, mean AND P95 below4.17ms, matched
DX11 visuals/gameplay and zero core/synchronization validation errors.

Current continuation: finish immutable depth registration, correct verified
source old-pipeline material ownership, then capture a fresh full-camera-locked
CPU profile to select the next dominant system. Keep prior contradictory rows.
Scope and ownership must follow the approved
sequence, not the superseded historical packet/worker roadmap below. Canonical
native build: scripts/build-native.ps1 -Configuration Release -Test -Deploy;
managed private build: dotnet build Vulkan.sln -c Release
-p:RunPostBuildEvent=Never. Never run CPU/GPU tests or unrelated builds during
benchmark sampling.

## Historical structural sequence (superseded)

1. Native bulk indirect submission: preserve packet selection/order, create
   command records, and batch compatible object-geometry draws. Current bindings
   require identical mesh, pipeline, descriptors and object constants within a
   run. Explicit capability enablement and a reference direct path are required.
2. Shared persistent geometry arenas and indexed object/material records remove
   those compatibility boundaries. Preserve mesh replacement/unload retirement,
   all shadow/probe/highlight identities and stock shader semantics.
3. Persistent scene records and dirty updates replace repeated full reconstruction
   of approximately 18.7K logical submissions. Preserve moving, skinned, dithered,
   mined and highlighted objects; never freeze the scene to obtain a result.
4. Measure coarse recording/frontend parallelism after the data contract is
   stable, using worker/frame-owned pools and substantial command batches.
5. Resolve changed presentation waiting and attribute GPU passes with fresh
   successful profiling before selecting shader changes. Earlier recorded GPU
   frame spans are above 4.17 ms even when presentation is asynchronous.
6. Finish LCD, lighting and particle parity with matched DX11 image evidence.

## Historical implementation: native batch planner

Hypothesis: adjacent compatible packets can share one indexed indirect API call
while preserving separate instance ranges and caller-selected packet membership.
First implement/test the CPU command/run contract; then connect it to fenced
per-slot indirect buffers and the main/cascade paths. Keep spotlight/probe paths
correct during integration and extend batching to them after the same ownership
contract is verified. This foundation is not accepted as an achieved FPS gain.

Pre-mortem: incorrect firstInstance rebases instance data; grouping unequal UBOs
changes transforms/tints; grouping across a filter boundary changes membership;
cross-slot buffer reuse races GPU reads. Packet IDs, byte equality, explicit
range boundaries and slot-completion fencing must remain authoritative.

Acceptance: host tests cover exact command records, unequal constants/materials,
selection order, noneligible draws, device count limits and malformed bounds.
GPU integration must prove exact direct/indirect pixels for distinct instance
ranges, shadows and filtering with zero Vulkan validation errors. Paired private
Pertam runs in both orders use full 1080p, stable hashes and native policy/call
counters. Keep the experimental path opt-in until that evidence supports it.
Canonical build: `scripts/build-native.ps1 -Configuration Release -Test -Deploy`;
managed build `dotnet build Vulkan.sln -c Release`; private benchmark under
`Tests/ScenePerformanceHarness/Run-Isolated.ps1`. Existing research/provenance is
in `RESEARCH_BRIEF.md`. No lowered quality or removed validation is an acceptance
substitute. Rollback is disabling the experimental path; retain direct submission.

## Current evidence

`Native/src/world_indirect_plan.h` now implements caller-selected compatible
runs and exact 20-byte indexed indirect records. It leaves singleton/noneligible
packets on the direct path, limits run size to the supplied device limit, and
validates every selected packet before exposing an execution plan. Tests prove
distinct instance counts/offsets, identical versus unequal constants, unchanged
selection/filtering, binding boundaries, device limits, empty passes and malformed
ranges. `scripts/build-native.ps1` includes the new host test in both explicit
build target lists. Windows Release canonical `-Test` passes all six CTests;
evidence is `TestResults/IndirectPlan-Native.log`. No production deployment or
scene performance change is claimed for this host foundation.

Next integration contracts: retain per-slot completion fencing for indirect
buffer allocation/upload; explicitly query/enable multiDrawIndirect and
drawIndirectFirstInstance; reject index-dependent shader eligibility; keep
main/cascade selections and per-packet identities while recording a run with
one call. Expose actual native policy and command counts to acceptance. GPU
execution and full-scene paired measurements remain required before retaining
the new submission policy as a default.

GPU integration now exists for main/cascade selection. Native device creation
queries optional capabilities through the existing Features2 contract, enables
both required indirect features only when supported, and otherwise uses direct
submission. Vertex input reflection excludes index-dependent builtin inputs
from eligibility. Command arrays live in two completion-fenced host-visible
buffers; uploads occur after the owning slot wait and buffers are destroyed with
world resources. Logical packet IDs/counters remain intact; API-call counters
measure actual grouped recording. Independent 32-byte stats report supported,
enabled, executed main/shadow groups, grouped packets and prepared records.

Experimental policy is off by default (`SE_VULKAN_WORLD_INDIRECT=1` opts in).
Canonical native build/test/deploy passes all six CTests. net10/net48 Release
builds pass with zero warnings/errors. WorldPassGpuTests passes 113 checks with
zero validation errors, proving actual groups, distinct counts/instance offsets,
both fenced slots, byte-identical main/cascade pixels, unequal-object fallback
and four-cascade call reduction. This targeted GPU evidence is not full-scene
visual parity or frame-time proof. Logs: `IndirectSubmission-Native.log`,
`IndirectSubmission-Release.log`, `IndirectSubmission-Gpu.log` in TestResults.

Scene harness `-WorldIndirect` explicitly controls the experimental policy and
records requested mode in launch metadata plus actual accepted-frame native
stats in `indirect-submission.json`. Pertam candidate
`Vulkan-1ce1cb545e294d78aeba1bfe3fba51ff` completed 191 sample frames with actual
support/enabled=1 and average main/shadow groups 765.963/1171.309, grouping
5575.686 packet executions per frame. Its 154.450 ms CPU and 177.368 ms final
world-GPU window are **not comparative acceptance evidence**. After its process
exited, nvidia-smi still reported 88–89% GPU utilization with another game and
other GPU clients present. This independently proves substantial external load;
it does not identify a specific client's share or the indirect path's cost.
The owned direct reference `Vulkan-49e09fb527b149fbadc7830fe61bec16` was stopped
under this contention, leaving `stopped-contention.txt`; no user app was closed.
Clean direct/indirect scene runs in both orders remain the acceptance gate before
any default change. The experimental policy remains off by default.

Candidate product hashes: net10 plugin
`930E00841619558038A1B7D171DA95B460D84178F330F80416078C43A45E860C`, native
`345CBC7928311D8920249947DCE9E6131C73949099AE35635155F9F57BE8E34A`;
harness `E74F78485F20B274F04EC134A33FE0BEE66A40BFBD8EA6F11B885E278CEE8A6F`.
Source checkpoint SHA remains
`D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92`.

After the user stopped external load, idle GPU utilization was 0%. Two clean
same-process order comparisons used the candidate product hashes above and
harness `1335EE4AFD7EFB8ED4E528F39CCA931BEA014939BDBDDB8FC9FF4855C74E85B0`:
`Vulkan-d7bf36af998243e8b1b183d0d17d3e50` direct 26.1443 ms (1147 frames)
versus indirect 26.0402 ms (1152); reverse
`Vulkan-18fb2b8b4ab94a479263e7a049f3000b` indirect 26.3855 ms (1136)
versus direct 25.9138 ms (1157). Native enabled policy and counter sample count
match every timing frame. All phases retain full 1080p, Mailbox and the saved
doorway camera (small physics drift in camera position and scene membership).
Indirect reduces actual main+shadow calls approximately 34–35%, but its
whole-frame result is not consistently faster. Final-window world GPU remains
4.743–4.881 ms; Present remains 5.758–5.889 ms. No GPU improvement or target
achievement follows. Keep the policy opt-in; continue structural work.

## Active integration: shared geometry

Replace experimental world-mesh allocations with shared device-local pages,
using existing VMA virtual allocations measured in vertex/index units. Native
Mesh records keep owned allocation tokens and byte offsets; buffer handles are
borrowed views. Existing retirement completion controls token release and page
reuse. Release retired mesh tokens before world-page teardown at device idle.
Update main/cascade, spot, probe, highlight, foliage/cloud and glass bindings;
cached binds must include geometry offsets, not just shared handles.

Indirect records use page-local firstIndex/vertexOffset, allowing adjacent
different meshes to batch when page buffers, pipeline, material and object bytes
match. Preserve selection order. Direct arena draws bind individual byte offsets
to preserve stock index semantics. Keep standalone allocations and both policies
off by default for reference/rollback. Prove nonzero offsets, unequal geometry
counts, mixed allocation policies, replacement/removal during pending uploads,
slot reuse, cleanup and direct/indirect exact pixels under synchronization
validation. Performance acceptance still requires an uncontended scene.


## Clean comparisons and shared geometry, October 7

With external GPU load stopped, full-1080p Pertam direct/indirect comparisons:
`Vulkan-d7bf36af998243e8b1b183d0d17d3e50`: direct 26.1443 ms (1147 frames),
indirect 26.0402 ms (1152). Reverse
`Vulkan-18fb2b8b4ab94a479263e7a049f3000b`: indirect 26.3855 ms (1136),
direct 25.9138 ms (1157). GPU idle before these tests was 0%. Actual main and
cascade API calls fell approximately 34-35%, without consistent whole-frame or
GPU gain. Final-window world GPU was 4.743-4.881 ms; Present 5.758-5.889 ms.
Saved-camera physics drift and small membership changes limit exact comparisons.
Indirect remains opt-in. Product hashes and actual counter correlation are in
`Docs/planning/PERFORMANCE_STRUCTURAL_PLAN.md` and each run's `launch.json`.

Shared device-local world vertex/index pages now use existing VMA virtual ranges
and completion retirement. Compatible indirect records can span different meshes;
direct draws bind individual byte offsets. Both policies remain off by default.
Six native CTests and unified net10/net48 Release builds pass. WorldPassGpuTests
passes 120 checks, including exact standalone/arena/direct/indirect pixels,
nonzero offsets, unequal counts, pending replacement/removal and page cleanup.
Arena-enabled probe (103), transparency (149), environment (35), postprocess
with spot/highlight (176), and voxel (51) checks pass with zero core and
synchronization validation errors. Evidence: `TestResults/GeometryArena-*.log`.
These tests establish focused correctness, not full-scene parity or FPS gains.

Experimental arena SHA256: plugin
`29D48B36B166FAB6FAC6FEB77631205A1905E368F104B4FF0981BFBC92EC9306`, native
`BE770FF45A41A19BF3ECB2F00290A42FEE58535B419233A3026A34C47444D390`.
Private `-GeometryArena` selects allocation before world loading and emits actual
per-frame page/live/capacity counters. Allocation comparisons require separate
processes; same-process direct/indirect tests hold allocation policy fixed.


## Next structural work: retained records and indexed object data

Ownership map: `VulkanScene.cs` actor/part records already cache material revision,
constant bytes and packed instances. `NativeWorld.cs` reconstructs managed packet
arenas; `world_render.inc::queue_world` validates and deep-copies every selected
packet, then `render_world` re-uploads object/instance data, resolves descriptors,
sorts all draws and builds selections/plans. `WorldDraw` currently receives a
transient object offset; dynamic UBO binding requires equal object bytes within
an indirect run. Geometry pages remove the mesh binding boundary but not this
object boundary. Probe/spot/highlight use selected packet identity separately.

Next acceptance slice should establish stable actor/part handles with resource
and geometry generations, frame-selected handle lists, and explicit dirty updates.
Two fenced GPU slots each track uploaded record versions; no slot may reuse a
write while the GPU reads it. Camera-relative matrices, moving grids, bones,
instance revisions, dither, materials, texture replacement, mined geometry and
highlight/shadow exclusions must update independently. Removal retires records
using the existing completion timeline, with generation checks to reject reused
handles. Preserve the full validated legacy queue as an opt-in reference.

Indexed shader/object work follows that record contract. Authenticate source and
SPIR-V layouts before adding object-index instance attributes or shader storage
reads. Vertex/fragment object accesses, stock matrix conventions and dither/tint
must remain exact. Different material tuples need indexed textures/material data
and explicit descriptor-indexing feature enablement, not merely hardware support.
Do not globally reorder transparent or identity-sensitive packets. First prove
unequal-object, cross-mesh exact pixels across both slots and all auxiliary
passes; then compare scene CPU/GPU at unchanged quality in both orders.

Parallel recording should follow stable immutable frame snapshots. Workers own
command pools and broad pass chunks; measure total preparation plus worker join,
not only one recording timer. Fresh ordinary GPU pass attribution and matched
DX11 images remain separate gates. A reduction in draw API calls alone has twice
failed to establish a whole-frame improvement and cannot justify a default flip.


Arena scene tests completed without profiling/GPU-test contention, full1080p:
`Vulkan-3d5a862db5c5411aad96c6ab64c4e51b` direct26.6773/indirect26.7729 ms;
reverse `Vulkan-79e3572e327e48038e4e6b1e45daf41f`
indirect26.4397/direct25.6884 ms. Same-binary standalone
`Vulkan-a2c75c2e319e48509b2cc60cea07f2f2`
indirect26.3391/direct25.5751 ms. Each phase's actual allocator/submission policy
and native sample count match CPU rows. No FPS acceptance follows; retain defaults.
Arena pages total65VB/8IB; live/capacity and full hashes are recorded in
`TestResults/GeometryArena-CleanComparisons.json`. Arena indirect cuts main/cascade
calls40.7% in the first run, while sort/plan grows roughly0.51ms. This motivates
retained immutable selections and indexed object records, not a default flip.


Fresh DX11 reference `Dx11-7ac15498ded24037b1a80c4b59730ffe` completed 6367 frames
at full1920x1080: CPU Draw-through-Present mean4.51798ms, median4.1496ms,
p95 6.0689ms, Present interval mean4.71536ms,
212.073 rendered FPS. This saved-doorway workload is not a >240FPS DX11 result;
the original screenshot camera remains unmatched. Vulkan standalone direct mean
25.5751ms is roughly5.66x this measured DX11 CPU mean, with target/parity unachieved.
The source save SHA remains unchanged and all private game processes have exited.

The initial fresh DX11 run `Dx11-3a8670001cb848eaaaf41b255375f078` is excluded:
no harness Reports and QuickStart loaded instead of copied Pertam. Its exact owned
process was stopped; `invalid-workload.txt` records the rejection. Harness Vulkan
field lookups are now deferred to Vulkan initialization; initialization exceptions
are recorded in the marked private data folder. The retry loaded copied Pertam
and produced valid DX11 results. Harness SHA is
`507A7182BD84EFDD280F88578FB37B6C90CCE66415529D9FC1B8F508202974EA`;
product hashes remain unchanged. The completed Vulkan comparisons retain their
previous harness hash, so distinguish these artifacts when reproducing them.

Visually inspecting the fresh DX11 view against the arena/direct Vulkan doorway
view still shows differences in door emissive color (green versus white), interior
brightness, LCD tone/brightness and floor shading. Screenshots are independently
sampled with simulation drift; this is a qualitative parity finding, not an exact
pixel-diff oracle. These differences require their own matched parity work and
cannot be credited as performance savings.


## Active slice: retained object storage

Implement a separate retained-object queue ABI using nonzero CPU object identities;
the managed bridge assigns identity to constant-array ownership without retaining
arrays strongly. Native validates resources and compares bytes on every selection;
identity alone never asserts immutable data. A CPU-owned aligned store keeps stable
object offsets, revisions and bounded age-based reclamation. Two GPU slots track
revisions by offset and upload selected dirty records only after their slot wait.
Record revisions are globally monotonic so offset reuse cannot alias an old GPU
version. Legacy queue/mode changes reset per-slot versions and descriptors when
buffer bindings/ranges change. Keep the feature off by default.

Pre-mortem: recycled arrays/changed camera transforms must revise contents; offset
reuse must force both slots to upload; duplicate IDs with inconsistent bytes must
fail; unmanaged source bytes must be owned before unpin; pool reclamation must not
free selected records; object ranges affect descriptor validity and auxiliary
pass offsets. This slice retains object data, not the entire scene or selection;
managed packing, full per-packet validation and instance rebuilding remain until
the next record ABI. Prove host ownership/reuse/budget and GPU exact legacy/retained
pixels for static, changed, reordered and both-slot selections under validation.


Retained-object prototype builds: native seven CTests; unified Release zero
warnings/errors; GPU suite136 checks with exact legacy/retained pixels and visibly
changed transforms, both slots, reordered/static zero-upload and mode reset.
First scene `Vulkan-6c21491e960841438296188fd73d0252` rejects the initial identity
policy: legacy26.3657ms1137frames versus retained39.3897ms760frames. Selected
10824records/frame but store307891 on average; temporary freshly allocated voxel
constants generated new weak-array identities. Uploaded bytes fell1.539MB to0.521MB
but CPU bookkeeping dominated. The feature remains off. Correct recyclable frame
constants to layout/packet-position storage identities; every selection still
compares bytes and all resource validation runs. Persistently owned constants keep
weak-array identity. This is storage reuse, not an assumption that packet position
has stable scene semantics. Corrected GPU136checks pass; scene remeasurement pending.

Same-engine DX11 source study (skill Data/Decompiled, recordedSE_VERSION1210014):
`VRage.Render11/VRageRender/MyMergeInstancing.cs` owns indirection/per-instance
buffers and m_tableDirty/m_instancesDataDirty; MoveToGPU uploads only dirty data,
OnDeviceReset clears buffers and marks both dirty. MyGeometryRendererOld owns
per-view MyRenderingWork, prepares filtered batches, DoWork and ConsumeWork.
These are research observations, not copied implementation. Next map the current
GeometryStage2 path and caller activation before assuming this old path represents
the benchmark. Extend Vulkan explicit dirty ownership and broad pass work only
with equivalent moving/resource-reset correctness. The user explicitly added
same-engine DX11 implementation comparison to the objective.


Corrected retained-first scene `Vulkan-c51f714d9b1c440f8e6791c2aff2d2f9`:
retained28.3622ms1057frames, legacy26.1803ms1145frames. Store12359records average,
2.917MBcapacity and226KBuploaded objects versus legacy1.542MB. Native Queue grows
2.029 to2.803ms; Upload grows0.566 to0.803ms. Per-record hashing/comparison/copy and
selected-record traversal outweigh avoided byte transfers. Reject default flip.
Machine evidence/hashes: `TestResults/RetainedObjects-Comparisons.json`.
Counters Selected/Changed in disabled phase are stale last retained values, not
legacy activity; Enabled and uploaded bytes identify actual mode. Correct readout
before a future acceptance run. The weak-array store is bounded after correction;
this experiment remains useful for ownership proof but is not a speedup.

Current DX11 scheduler verification: `VRage.Render11/VRage/Render11/Render/
MyRenderScheduler.cs` initializes both geometry managers and builds per-view
cull/preprocess/prepare/render dependency jobs; GeometryStage2 passes request
`isUsedDeferredRC:true`. Thus Old naming does not mean inactive. Current
GeometryStage2 also builds grouped instance buffers in MyPreparePass and retains
MyRenderData storage across frames. Next structural priority is explicit
indexed dirty updates and coarse pass recording/preparation concurrency, avoiding
hash-based byte detection as the final design. Match camera-relative transforms,
LOD transitions and per-view memberships when borrowing the data architecture.


## Active investigation: successful CPU capture and same-engine scheduling

Nsight CPU-only private default capture is running/exporting as
`Vulkan-b9068f177a924e42a8e2b07d8ce82d77`; UAC was approved and game reached
Pertam/sample/result. Do not treat the helper's initial 'complete' marker as
capture proof: it accepted a zero-byte report placeholder. Exporter's stderr
shows report progress, with the owned Nsight process still live. Correct helper
to await launched profiler termination plus nonempty report/SQLite before success;
inspect SQLite diagnostics/samples before selecting optimization. This timing
is instrumented and cannot replace the ordinary reference.

DX11 MyShadowCascades uses update intervals(1,0) for cascades0-3, then(4,3),
(8,5),(8,6),(8,7), with camera movement/frozen/Ansel overrides. The fixture has four cascades; the latest actual scene readout shows six.
Investigate stock scheduling for the two farther cascades separately. Scheduler initializes both geometry
paths and records independent per-view jobs/deferred contexts. Preserve rendering
quality and dynamic updates when introducing Vulkan frame/worker-owned pools.


## Active integration: concurrent cascade command recording

Hypothesis: independent per-view depth draw recording can overlap on CPU while
preserving GPU pass order, barriers, caster selection and shader bindings.
Use primary-owned image transitions/rendering scopes, secondary depth commands
with dynamic-rendering inheritance, and separate pools per cascade/frame slot.
Reset only after owning slot completion. Immutable frame geometry/descriptors
are prepared before dispatch; worker-local counts merge after join. No worker
allocates descriptors or touches primary buffer/shared stats. Keep policy off.

First slice records cascades concurrently with primary spotlight work using
std::async launch (explicit per-pass jobs, not fine-grained per-draw tasks).
Measure creation/join overhead in whole-frame time before choosing persistent
workers or extending main/probes. Exceptions must join all jobs before frame
storage/resource teardown. Cleanup destroys pools at device idle. Exact serial/
parallel main/shadow pixels, both slots, changed transforms/caster membership,
direct/indirect/arena coexistence and synchronization validation are required.
Source: Khronos command_buffer_usage performance sample and same-engine DX11
MyRenderScheduler per-view prepare/render dependency jobs. This is an original
integration; no donor implementation copied.


Concurrent cascade integration builds with seven native CTests and net10/net48
Release zero warnings/errors. GPU suite145checks passes with zero validation
errors: exact serial/parallel direct and indirect main/shadow pixels, both slots,
changed transforms, removed caster membership and actual cross-mesh page-backed
indirect depth groups. Logs `ParallelCascades-Native.log`, `-Release.log`, `-Gpu.log`.
Immutable prepared geometry/material selections are recorded in separate
cascade/frame pools with worker-local counts, async job lifetime/join and primary
barriers/order. Private `-CompareParallelCascades` has actual per-frame job counters
and requested policy/hash. Default stays off; scene timing still pending while
Nsight CPU report importer35788 remains active (report at20%, zero-byte placeholder).
Do not start clean FPS comparisons during that exporter CPU work.

Candidate hashes: native438B87C8A91BE05A6BAC3F34C7CDAE121F25CED216CE097BF796AD68DF1EB599;
net10 E231E16360F939A6041C1CE3D265D1B821D48B8236318234AF6E187D65C7036B;
harness2D543E6FBBC9D73F14FAAA1EA2A188E63D121E19D8F013C05D1CFABBD8EAA7A7.
Captured CPU process has its own earlier private binaries/hashes. No full FPS or
full-scene visual parity follows from targeted GPU correctness.


## Active parity slice: generic object tint

New rigid/GPU fixture proves a non-white tint is lost when converted to the
stock generic instancing pipeline. PixelTemplateBase initializes color_mul=1 and
PASS_OBJECT_VALUES_THROUGH_STAGES skips the UBO color read; GBuffer/Forward stage
interfaces carry HSV/custom alpha but omit color_mul unless simple-coloring is
used. Existing batch proof covered white multiplier/HSV only. Added regression
and temporarily preserves tinted rigid objects on stock rigid pipelines;148GPU
checks pass. No claim yet that the actual doorway view is fixed.

Compile offline derived generic GBuffer and Forward pixel modules from verified
stock source snapshot with one explicit initializer transformation:
pix.color_mul = object_.color_mul. Keep original HSV/dither stage interface,
texture logic, shading and object layout. Authenticate base module identity,
derived module/metadata, transformation version and packaged manifest at load.
No source game files edited; compiler cache sees transformed source dependency
hash. Enable tinted batching only for loaded derived variants. Exact tinted
rigid/generic pixels, four material variants, emissivity and probe output required.
This corrects a parity regression while retaining structural instancing.


Object-tint parity integration is now complete at targeted scope. Eight offline
pixel modules (generic main2/7/12/17, forward66/71/76/81) derive from an owned copy
of authenticated stock source with exactly one initializer replacement. Original
game content stays unchanged; compiler records transformed dependencies and
SPIR-V validation. Packager `Tests/TintShaderPackage` emits ObjectTint manifest,
module/metadata hashes and coverage binding. Loader verifies base identity,
transformation/source hash, dependencies and module/metadata before native use;
preflight checks the actual derived selections. Unavailable supplement retains
stock rigid tinted submission; loaded supplement retains grouped tinted draws.

World GPU154checks and Probe119checks pass under core/synchronization validation,
including visible green rigid/generic equality, all four material variants,
colored glow and exact rigid/generic forward probe pixels. Integrity12checks prove
all eight selections and reject wrong base, corruption and changed transformation
with a recomputed manifest hash. net10/net48 Release zero warnings/errors.
Evidence `ObjectTint-Package.log`, `-Gpu.log`, `-ProbeGpu.log`, `-Integrity.log`,
`-Release.log`. Bundle manifestSHA3EDF79FF7DE7AB1772E4412C083BA4AFD18CDABBB16EAB3A69A1D74F8A4326AA;
net10pluginC1950C8CDFA92E262604F885A2125A5EF7FC0A563253C04B05A4A6473795978A;
native438B87C8A91BE05A6BAC3F34C7CDAE121F25CED216CE097BF796AD68DF1EB599.
Full doorway screenshot verification and clean parallel timing remain pending.

Nsight CPU importer35788 is still live with accumulating CPU time, report at20%
and zero bytes. Do not restart due to observation timeout. Raw QDSTRM exists at
C:/Users/Zeridian/AppData/Local/Temp/nsys-Zeridian/nsys-report-c54f.qdstrm;
retain it while import runs. Goal remains active, not blocked: parity work has
advanced independently. Parallel recording comparisons must use current tint-
corrected product hashes in both phases after exporter activity ends.


Clean serial/parallel orders with tint-corrected product:
`Vulkan-3ab7f9f22d5a401d867b77aca02b7ebd` serial26.6868ms1123frames,
parallel26.0409ms1151frames; reverse `Vulkan-ebb26c50cd524deeaec5f32315bc5079`
parallel25.2877ms1186frames, serial25.8367ms1160frames. Policy/counter row counts
match timings; parallel actual Tasks=6 and Commands=all cascade commands.
Both phases use full1080p and unchanged legacy allocation/indirect/object policies.
Retain native parallel default when hardware_concurrency>=4, with
SE_VULKAN_PARALLEL_CASCADES=0 serial reference. Benchmark child flags remain
explicit (absent -ParallelCascades selects serial), so private default is controlled.
Six actual scene cascades correct the earlier fixture/scene conflation. Next
stock cadence investigation applies to far indices4/5, respecting stock overrides.
The doorway screenshot now shows the green strip; broader lighting/LCD/floor
parity remains open. Scene screenshots have independent simulation drift.

CPU capture export completed: SQLite quick_check=ok;10.011seconds,9,710 periodic
render samples and11,047 scheduling stacks. Use cpuCycles=1 or sum(cpuCycles)
per NVIDIA Analysis Guide; zero-cycle scheduling stacks are not hotspots.
All function symbols unresolved. Leaf modules47.27%unknown,24.24%native,
10.78%NVIDIA driver,7.88%coreclr,3.49%vcruntime. These are sampled module shares,
not named function/absolute frame time attribution. Diagnostic warns missing
Windows Performance Toolkit for merged ETL, not absence of exported sampling.
Read-only helper Summarize-NsightCpu.py writes nsight-cpu-summary.json in the run.
No additional profiler is running; follow future capture with optimized native
symbols/JIT data for function-level ownership rather than interpreting unknown IPs.


Retained default build evidence: seven native CTests, net10/net48 Release zero
warnings/errors, World GPU155checks pass including startup default policy and
all serial/parallel/tint comparisons. Final nativeSHA
B5998D982C924A1E092C99016CCC7ED4051767575B518A37C8549309E035CBC9;
net10 pluginC1950C8CDFA92E262604F885A2125A5EF7FC0A563253C04B05A4A6473795978A.
Paired performance artifact hashes precede only the default-selection change.
All private games/profiler processes exited; source checkpoint SHA unchanged.

## Next mapped GPU structural slice: tiled light selection

Native world_light_data currently assigns every point light to every16x16 tile,
writing all64 index positions per tile. Stock LightPoint.hlsl only contributes
when reconstructed surface distance<light.range. Same-engine PrepareLights.hlsl
instead intersects spheres with tile frusta/depth. This is an algorithmic workload
difference, not a shader-quality option. Investigate conservative projected sphere
bounds first (owned input projection/view, negative-height viewport, render extent,
near-camera fallback, increasing light index order), with the full-list reference
retained. Exact legacy/culled pixels at upper/lower screen edges, camera-crossing
spheres, moving lights, resize/fraction and64-light bounds must prove no missed
contribution. Expose actual tile assignment counts and compare ordinary scene
GPU/CPU before retaining it. If camera-containing bounds dominate, use the
stock GPU depth/frustum preparation contract instead of tightening an unsafe bound.
Full frame CPU still~25ms and GPU final windows4.61-4.95ms; neither target reached.


## Active integration: stock GPU tile frusta/depth

Use same-engine PrepareLights compute source rather than the looser CPU rectangle
prototype. Frame/depth/sphere data remain identical to LightPoint. An owned verified
source snapshot changes only tile-list emission to a two-word shared mask followed
by ascending indices for the renderer's64-light budget, preserving baseline
floating addition order. Canonical compiler validates the derived SPIR-V; packaged
source/module/metadata/base identities authenticate loading. Slot-owned GPU tile
buffer receives compute writes after GBuffer depth is readable; barriers publish
to fragment and fenced diagnostic readback. Original all-lights host list remains
reference and default until GPU/pixel and scene tests pass.

Premortem: depth convention/empty tiles, partial edge groups, max64 lists, view
projection, buffer read/write hazards, result lag, mode changes, resizing and GPU
slot reuse can cause missing lights or races. Read counters only after slot wait;
report completed frame/epoch separately from current-frame policy/dispatch.
Exact reference output for overlapping/edge/near/moving lights and resize/fraction
plus zero validation errors are mandatory before scene acceptance.


GPU tile integration first proof: native seven CTests and unified Release zero
warnings/errors; World GPU189checks with zero validation errors. Exact reference
pixels include visible central point lighting, both slots, edge/near/behind/offscreen
spheres,64 overlapping lights with ascending accumulation, empty lists and65x49
partial edge groups. Fenced completed-frame counters prove assignments reduced;
current/completed frame are explicitly different. Uniform LightConstants reflects
16-byte padded native size (4-byte logical metadata member); descriptor range is
16 and remaining bytes zeroed. No relaxed descriptor/type validation.
Scene first order `Vulkan-daea982b54ed4f6881d5d5f73f4aa44c` launched with retained
parallel cascades and all other experimental policies off, full1080p. Native/plugin/
harness hashes and policy requests in launch.json. Full-list/GPU phases expose
actual dispatch and completed assignment scope. GPU policy remains off by default.
Do not claim scene timing gain before both orders/unchanged quality checks.


GPU light selection completed both ordinary full1080p orders with parallel
cascades held on: `Vulkan-daea982b54ed4f6881d5d5f73f4aa44c` full26.3561ms1137frames,
GPU26.9104ms1113; reverse `Vulkan-e70121a4725c4dceb660074b2f01d571`
GPU26.3310ms1138, full25.9227ms1156. GPU mode is slower in whole-frame CPU in both
orders, so it stays off by default. Completed assignments fall approximately94%
(30K versus471K), with completed frame exactly two frames behind current policy
and one dispatch per accepted GPU frame. Final-window GPU4.8226->4.7627ms and
5.1870->4.9016ms are lower but main command counts vary, so no precise isolated GPU
speedup follows. Machine evidence: `TestResults/LightTiles-CleanComparisons.json`.

Paused full/GPU/full scene diagnostic `Vulkan-ea474cf4ddbd4bafa0ff58f62d566b15`
keeps identical camera,18701 logical and10835 physical packets across six captures,
full1920x1080. Actual GPU counts41580/465120. World-region cross-mode mean channel
delta0.0000884 and0.0001803 (8-bit units) is below the largest same-mode control
0.0003771; cross-mode above-four fractions0.000625%/0.001528% are below control
0.003056%. No broad light-selection regression detected, but images are not byte-
identical because temporal effects remain. This diagnostic is not FPS or DX11
parity proof. PixelDiagnostic is true; its historical Instrumented field did not
include the new switch, corrected for future launches. `pixel-comparison.json`
and `pixel-delta.json` preserve actual policies, counts, resolution and controls.

World GPU195checks pass with zero core/synchronization validation errors, including
partial edge groups, off-center projection/rotated view, fractional extent and64
ordered lights. LightTileShaderIntegrityTests5checks reject changed source,
corrupted module and transformation with a recomputed manifest hash. Native7CTest
and Release builds pass. Shader/package/native logs under TestResults/LightTiles-*.
The native compute integration uses16-byte padded LightConstants and device-local
lists, explicit compute/fragment/transfer/host dependencies and slot-fenced readback.
Full-list reference remains the production default; all original game source files
are untouched. The full<4.17ms/visual-parity objective remains unachieved.


Further evidence reconciles initial tile CPU regression: native work.acquire_us
includes collect/sample_light_tiles between timer stamps, not just vkAcquire.
GPU phase final window showed0.593ms versus0.004ms reference while sampling8160
counts. make_buffer configured all mapped allocations for sequential CPU writes,
which VMA may select uncached/write-combined memory. GPU readback is a CPU-read
resource: use existing allocator's HOST_ACCESS_RANDOM flag and requireHOST_CACHED
only for this buffer, retaining slot wait/invalidation. Expose readback_us in the
previous reserved stats word so this cost is attributed explicitly. This is an
ownership/allocation correction to the new algorithm, not a shader-quality change.
Remeasure both orders after validation; earlier comparisons remain historical.
Reference: VMA usage_patterns.html readback guidance. Other buffer policies unchanged.


Cached readback correction is validated: native7CTest, Release zero warnings/errors,
World GPU195checks pass. The tile count buffer now uses VMA HOST_ACCESS_RANDOM and
requires HOST_CACHED for CPU reads; other buffer policies retain their previous
settings. New ReadbackUs field occupies the prior reserved stats word. Remeasured
readback is4.8-5.2us and acquire interval about9us, resolving the earlier~0.6ms
sampling interval. VMA guidance: https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/usage_patterns.html

New full1080p comparisons use identical current product/harness hashes:
`Vulkan-eab442683fc94051a9cf1741e01fc65e` full26.2876ms1140frames versus
GPU26.4159ms1134; reverse `Vulkan-50fa17fad1b04defac6cb2800507d34f`
GPU26.0652ms1150 versus full26.4662ms1132. GPU final windows4.9574->4.8871ms
and5.0975->4.7900ms; main draw counts differ, so no exact isolated GPU delta follows.
Full-frame CPU improvement remains inconsistent. GPU light selection stays off by
default; cached readback is retained as the correct ownership policy for the
experimental path. Machine evidence: LightTiles-CachedReadback-Comparisons.json.
The retained parallel-cascade default and tint fix remain active.

NativeSHA96588CCAF3344203A390263EA92DA77C7DA3750F9ABBF99449872E2E8F447185;
net10pluginDF31C62F033020B0BE043EA349B0ED66BE6AFAD9D2E534D2DFF30D8888E5CD6C;
pairedharnessDE4A6CB437528552C6F34BDEA4B32C3D7F48B619F4FD3A0F049AEBBAA86ECF31.
All private game processes exited; source checkpoint remains unchanged.
The<4.17ms and full DX11 parity requirements remain unmet. Next macro work should
continue explicit dirty/indexed scene records and same-engine far-cascade scheduling,
not infer completion from tile-count reduction or narrow fixture equality.


## Active investigation: resolved CPU/wait ownership

User explicitly requests deeper profiling and real measured gains, stopping the
optimization hypothesis loop. CppStudio state Investigative. No further renderer
algorithm changes until CPU samples, named managed/native stacks and thread wait
intervals rank the dominant costs at the full saved scene. Existing Nsight data
has unresolved function symbols; dotnet sampled-thread-time also includes native
wait boundaries. Add Microsoft-signed PerfView3.2.8 ETW ThreadTime capture with CLR
rundown, scheduling/CPU stacks and SEVulkan-RenderFrames event markers. Native
Release profiling build adds /Zi /Zo and linkerPDB only, retaining /O2 /Ob2; no
Deploy. Private launcher substitutes DLL/PDB and copies managed PDB. Production
assets remain unchanged. Compare same-lane DX11 after successful Vulkan attribution.
Gate: lost-events/stack coverage, exact render TID and frame marker coverage,
resolved functions and CPU-vs-blocked time; do not interpret unknown stacks as
named hotspots. Source: official Microsoft PerfView documentation/TraceEvent API.


Deeper profiler pass (explicit user steering) completed Vulkan and DX11 same-scene
ETW captures with PerfView3.2.8, CPU/scheduling/CLR/rundown and frame markers. Native
optimized symbols resolved1116addresses with0 unmatched, and both captures lost
0events. Marker window excludes capture setup/rundown; completeframes331Vulkan,
2360DX11. Profiling overhead means no ordinary FPS comparison is claimed.
Vulkan scene frontend10.22ms CPU/frame, bridge/queue4.66ms, native record/submit/
present6.19ms; these non-overlapping stack buckets rank structural work. Scheduling
classifies~24.20ms CPU and5.95ms blocked per marked Vulkan frame. Most blocking
(5.38ms) is under native end_frame with graphics-memory-manager wake stacks.
DX11 render-thread CPU is2.68ms/frame in the matched profile. AllocationTick
estimates741MB Vulkan render allocations versus15.25MB DX11 over respective10s
windows; stacks identify occlusion snapshots, voxel constants, texture paths and
packet arrays. GC suspensions0.69ms/Vulkanframe alone do not explain the gap.
No renderer algorithm changed during this pass. Next implementation must target
retained/dirty scene submission and bulk native ownership, with presentation
investigated separately; do not substitute a hash/helper micro-fix.
Detailed evidence/caveats: Docs/CPU-Profiling-2026-10-07.md and
TestResults/DeepProfile-RendererComparison.json. Production nativeSHA96588...7185
unchanged; private capture DLL/PDB hashes live in launch.json.


## Active implementation: retained actor submission and frame-owned bridge

Profiler-driven scope: scene frontend10.22ms/frame and bridge/queue4.66ms/frame.
Resolve opaque non-skinned actor part descriptors on model/material/appearance/
instance-type changes; replay typed records with current pose, instances, visibility,
cascade masks and bounds. Preserve skinned/glass/highlight/exclusion identity paths
through the validated legacy route. No static-scene freeze or reduced selection.
Prepared rigid records own hints, avoiding repeated material-key reconstruction;
actor instance matrix/HSV is prepared once and reused across parts.

A bounded flat batch ABI carries aligned object/instance blobs and offset records,
so native owns one copy per blob without per-draw pointer fixups/dedup hashing.
All per-packet resource/layout/budget validations remain. Frame-owned eight-token
visibility snapshots and packed occlusion arrays reuse capacity, with results
consumed before overwriting a slot; token/revision/camera guards remain exact.
Keep one experimental policy off by default and a full legacy path. Validate
exact pixels with moving poses, changed material/type/visibility/cascades and
both GPU slots; then ordinary both-order timing and matching deep profile.
Pre-mortem: descriptor invalidation, stale hints/mode switching, resource deletion,
encoded-offset bounds/alignment, array lifetime after unpin, snapshot token expiry,
and highlight/exclusion draw identity. No default flip without scene evidence.

Implementation now passes paired scene pixels, production scene-message mutation
tests, world/postprocess/voxel GPU validation and ordinary both-order comparison:
19.023→15.746ms and18.448vs15.639ms CPU (15.2–17.2% reduction). Native Present
fell to~0.2ms with external GPU load removed; stale blocked-time diagnosis is
superseded. Fresh DX113.534ms CPU/4.244ms interval. Capture/profile the remaining
work under these clean conditions before selecting another macro change.
Evidence: Docs/Performance-2026-10-07-RetainedSubmission.md and
TestResults/RetainedSubmission-CleanComparisons.json. Goal remains open.
After the change-local pixel and production mutation checks, retained submission
is enabled by default. Environment SE_VULKAN_RETAINED_SUBMISSION=0 is the rollback.
Fresh elevated capture was canceled at Windows UAC; retry is awaiting the user.

Next source-backed structural investigations: DX11 MyRenderScheduler schedules
per-view cull/preprocess/prepare/render work through DependencyBatch; Vulkan's
remaining submission still runs serially. DX11 MyGeometryRenderer.UpdateLods and
MyPreparePass.PrepareLodData select per-view stock model LODs, while VulkanModelCache
imports one geometry asset without the LODs tag and VulkanScene has no model LOD
selection. Audit actual submitted geometry against stock selection before changing
it; do not invent a reduced-quality distance threshold. New ordinary frame scope
shows~3.6ms material submission/~2.45ms queue/~4.65ms native record and~4.66ms GPU.
These are scope timings, not exact new CPU-stack attribution. Resolve the fresh
profiler and workload evidence before selecting between the two macro changes.

## Geometry contract investigation outcome — October 7

Complete old/new/static DX11 audit yields596418 main scene indexed invocations
versusVulkan24581913 (41.22x snapshot workload; not speedup/equal visibility).
424/460 mapped models have stock LODs that Vulkan does not load. Actual stock
settings captured: main shift0/min0, shadow/single-depth shift2, forward shift/min10.
Occlusion attribution:0/1911 completed snapshots accepted over1920frames;
all rejected by revision,1909 also by camera, no extent/token failure. Hidden
GPU results3428938 yielded0 scene culls. Routine message/transform invalidation
and submillimetre camera motion make current reuse impossible. Full evidence in
Docs/Performance-2026-10-07-GeometryContracts.md and run artifacts cited there.

Next macro owner remains scene/model/visibility preparation: implement stock LOD
and stock inflated manual-group occlusion contracts, not arbitrary camera epsilon
or reduced-quality geometry. Preserve per-view shifts, LOD transitions/material
IDs, highlights, cube/skin streams, transforms and removal. Stock projected-size
culling is also absent; apply its exact prerequisites if implemented. Parallel
preparation follows removal of excess submitted work. The expanded diagnostics
are instrumented; first audit metadata corrected by preserved sidecars, no FPS
gain claimed from them. Goal remains active, source save unchanged.
The complete captured scene uses the old pipeline: new instance and static groups
are both zero. Old MyRenderableComponent chooses one shared current LOD using
GBuffer settings. Per-view shadow/forward shifts are new-pipeline contracts and
must not be applied blindly to the old path. Prioritize old stock LOD selection,
including its transitions and cube distance multiplier, for this actual workload.

## Actor LOD implementation and evidence

Experimental old-pipeline selector now uses authored LODs, FOV/resolution/global
controls, cube quarter distance and stock two-mesh alpha transitions. Per-model
representation caches preserve material/highlight identity and owned constants;
LOD changes dirty spot maps and mark transition cascade replay dynamic. Missing
levels conservatively retain base geometry. Static/skeletal paths remain pending
and on base geometry. Defaultoff SE_VULKAN_ACTOR_LODS=1; do not claim full parity.
Both ordinary orders save11.50–12.47% CPU:15.657→13.705ms and15.631vs13.834ms.
GPU~3.185ms; submitted packets~7984. Goal still not achieved; Frame>4.17ms.
29 scene/257 world GPU checks and clean sync validation. Evidence in
Docs/Performance-2026-10-07-ActorLods.md and ActorLods-CleanComparisons.json.
Final LOD workload snapshot fed0a25e... maps all packets: main indexed invocations
24581913→6344400 (74.19% fewer), packets4318→3111; main candidates2129 in both.
Zero occlusion snapshots still accepted. Source save SHA unchanged. Keep LOD
experiment off by default until broader parity/transition/ownership verification;
current ordinary 13.7–13.8ms is experimental, normal default remains~15.6ms.

## Group query implementation and corrected LOD distance

Opt-in manual-group ownership now accepts query results and applies scene culls.
Inflated authoritative member bounds, unique query identities, epoch/token records,
reentry/near/viewport guards, movement containment and timeout are tested. Native
sync unchanged. Unparented actor LOD distance corrected to use authoritative world
bounds instead of an empty stock proxy cache; earlier experimental timing artifacts
predate this correction. Corrected both-order group delta0.309–0.437ms (2.24–3.14%),
combined experimental13.48–13.50ms. Paused legacy/group/legacy pixel differences
within repeat/return controls;38 scene checks clean. Keep group/LOD off by default.
Evidence: Docs/Performance-2026-10-07-GroupOcclusion.md and
GroupOcclusion-CorrectedComparisons.json. Goal active; next stock size/visibility
selection and parallel/retained scene plus shadow/probe preparation.
Active queue work: corrected combined managed capture4e1f7c... shows scene traversal
and immutable blob queue as major boundaries. NativeQueue~1.2ms in prior ordinary
scopes. Test opt-in bounded parallel native validation of disjoint owned packet
records, with per-worker validation cache/counters and const resource lookup. All
legacy contracts retained; copy/upload/GPU/fence ownership stays render-thread owned.
Pre-mortem: exception completion, resource-map mutation, offset arithmetic, partial
queue publication, shared counters. Validate both slots/malformed inputs/pixels,
then ordinary both-order timing. Do not claim native sample-exclusive CPU from
opaque managed thread-time boundaries. Transform callbacks now publish identities
to a concurrent queue; updater source confirms parallel blocking component calls.

Parallel immutable validation completed and deployed default-on for hardware>=4;
all validations retained. Normal15.249→14.912ms; combined experimental both orders
13.867→13.424 and13.443vs13.064ms. Evidence: ParallelQueue-CleanComparisons.json
and Performance-2026-10-07-ParallelQueue.md. Goal remains open.

Active work: deepen actual CPU attribution before the next structural change.
RendererCpuProfiler opt-in CPU_DETAIL wraps exact scene/update/queue/native
boundaries with QPC wall time, render-thread OS CPU counters/cycles and allocations.
Fine actor/part buckets use wall-only clocks to avoid thousands of OS calls; their
CPU/allocation fields explicitly unavailable. Scopes are inclusive, workers
excluded, CPU time quantized, cycles not converted to ms. Windows UAC capture
retry remains pending; this diagnostic needs no elevation. Pre-mortem: mismatched
frame sampling, fabricated zero coverage, profiler overhead and overlapping rows.
Unit checks distinguish real wait/busy/allocation and reject duplicate frames;
private source-identical full1080p diagnostic and ordinary verification follow.

Preliminary phase evidence: normal scene traversal5.59ms, prepared actor replay
2.22ms, legacy cached opaque parts0.80ms, glass0.28ms; native record4.7ms with
sort0.75ms/upload0.58ms/descriptors0.38ms. Transparency queues~0.1ms, so bulk glass
queueing is not selected. Full frame CPU15.17vswall15.32ms: scheduling/present wait
is not the leading current owner. Short-phase GetThreadTimes aliases (one scene
scope CPU8.64msvswall6.92ms): discard those phase CPU-ms fields. Final profiler
exposes CPU-ms only for whole Frame, cycles/allocation for broad phases, QPC-only
for fine actor/part buckets. Final capture plus diagnostics-off control pending.
Next macro target: scene producer scheduling/retained frame preparation, with
explicit immutable worker snapshots and render-owner merge/resource mutation,
and restoration of the captured stock visibility/LOD workload. Protect actor pose,
LOD fade, material revisions, highlight/exclusion identity, group epochs and
two-slot resource lifetime. Native per-slot binding/sort evidence can guide a
subsequent retained per-view preparation design; do not select isolated hash/copy
changes or arbitrary lower-quality thresholds to chase the target.

Final CPU-phase capturebdc68f... validates1892 accepted frames, whole-frame
wall15.648/CPU15.435ms, scene6.892ms/24.62Mcycles, native record4.637ms/16.49Mcycles.
Prepared replay2.280ms vsrebuild0.00448ms; legacy opaque0.800ms; glass0.238ms.
Matched diagnostics-off a28bab... ordinary15.531msmean/14.8765median/64.27FPS,
renderer10secondFrame15.402ms/GPU4.735ms. Do not claim a new optimization gain
from these profiler/control runs. CpuPhaseSummary checks hash/source/mode/coverage.
Release both runtimes deployed; measurement checks and39 scene GPU tests clean.
Evidence: CPU-Profiling-2026-10-07-RendererPhases.md. Goal remains active.

Active macro experiment: parallel prepared-actor byte production, default off.
DX11 MyRenderScheduler explicitly schedules old prepare/render dependencies;
Vulkan PreparedReplay currently spends2.28ms on the render owner. Reserve exact
existing packet/batch order on owner; defer pose/object and disjoint instance-range
writes to up to four workers, joined before scene completion/native copy. No
worker resource lookup, cache/map mutation, GPU calls or packet publication.
Pre-mortem: batch buffer relocation after reservation, repeated PreparedActor
identity within one frame, stale deferred jobs on mode/collection reset, worker
exceptions, pose/LOD/material mutations and count/range overflow. Store batch
identity plus bounded offset (not a pre-growth array reference); duplicate actor
flushes preceding jobs; join all work before return. Preserve serial fallback.
Exact both-slot/moving/tint/LOD/mixed generic/cube tests, normal paired scene pixels,
then both-order ordinary timing determine whether this experiment is retained.

Result: byte-production workers rejected and removed from production/harness.
274 world GPU checks and paused scene coverage passed, but parallel added0.960ms
forward and1.178ms reverse. Owner planning1.76–1.82ms remains; worker dispatch/join
0.49–0.55ms adds cost. Do not rescue through micro-tuning. Archive/evidence in
Performance-2026-10-07-PreparedWorkers.md. Present now5.4–5.6ms independently in
both modes; source cause not established, not blamed on external load without proof.
Next source-backed macro owner: stock distance/projected-size culling and full
owned preparation scheduling. MyGeometryRendererOld merge preparation returns
null, so no uncounted merge path invalidates the captured old/new/static audit.
CppStudio state: Investigative; reason=measured worker hypothesis failed;
evidence=ParallelPrepared-NegativeComparisons.json; exit=verified next macro contract
and matched scene outcome. Full goal active; failed experiment not shipped.

Stock size-culling read-only audit added to GeometryWorkloadAudit. It captures
authoritative world-AABB distance/extent for unparented native actors and stock
local distance/VolumeExtent for parented actors, final authored LOD distance,
projected pixel area and the exact installed20-pixel criterion. Near actors do
not project at all, matching stock prerequisites and avoiding divide-by-zero.
Static/skeletal eligibility is reported separately. Capture42e661d... is private,
full1080p and instrumented; do not use its timing rows as ordinary FPS evidence.
Summarize-StockSizeAudit.py verifies criterion inputs before selecting a policy.

42e661d... terminated with verified diagnostic JSON serialization failure:
near-camera zero-distance projection produced a non-finite number. Raw logs and
failure preserved; no stock-size conclusions from that run. Readout now projects
only after the exact stock final-distance prerequisite and exposes only finite
areas. Authoritative old MWM audit max authored distance200m, no squared overflow.
Canonical corrected retrye928075... PID25128 launched; verify current process and
reports before any next action. This is a readout repair, not a product restart
triggered by an observation timeout. Production rollback263world/39scene checks
pass with zero validation errors; source checkpoint hash unchanged.

e928075... completed readout but exposed a stale parented actor VolumeExtent cache:
953 predicted actors were falsely tiny, including valid1.45m-radius suspension
models with cached volume0. Reject that count; correction sidecar preserved.
Installed MyActor.UpdateVolumeExtent source uses LocalAabb.Extents when parented
and world Aabb.Extents otherwise. Audit now derives both authoritative extents
directly and reports cached versus derived fields and input-known flags.
Summarize-StockSizeAudit rejects old cache-derived rows. Corrected private
c67df4b... PID10552 launched; no rendering policy changed by any audit.

Authoritative c67df4b... completed and parser verifies472 of2125 main candidates
(472 eligible rigid actors) meet exact stock size prerequisites. Parent local
versus unparented world extents are explicit; stale cache values are retained only
as diagnostic fields. No geometry/index-savings or FPS claim from prediction.
stock-size-contract-audit.json is the current evidence; e928's953 count invalid.
Next bounded product hypothesis: apply the installed old actor distance/size
contract, with authored final-LOD distance and original projection/viewport, then
validate threshold boundaries, parent/unparented/scale, fade and highlight behavior
and cached shadow/probe invalidation. Keep experimental policy separate until
source-equivalent DX11 selection/pixels and ordinary both-order timing are verified.

Active size-policy implementation: default-off ACTOR_SIZE_CULLING uses the exact
installed raw distance/final-authored-LOD/projected-area rule and authoritative
parent local/unparented world bounds. Independent of LOD experiment. Static/skin
ownership excluded pending its own contract. Stock old PrepareFilter skips culled
actors across main/depth/probe paths; highlight rendering is separate and retained
via8192 highlight-only packets. Culled actors freeze LOD state. Size transitions
dirty static/dynamic spot maps; native cascade membership hashes see removal.
Pre-mortem: zero cached volume, zero-distance projection, threshold strictness,
resolution/FOV/scale/parent changes, model replacement, mode reentry, highlight IDs,
group query depth and stale cached shadows. Test boundaries and production messages
before ordinary both-order timing and matched stock selection/pixel analysis.

Size-policy scene suite52 checks passes: strict20-pixel and authored-distance
boundaries, zero-distance prerequisite, extent/viewport, far actor removal both
slots, mode restore, parented reentry and model-scale replacement. Highlight test
now enables the existing postprocess/highlight pass rather than expecting draws
from a disabled fixture lane. Culled highlights use authored LOD0, matching stock
MyHighlight.DrawRenderableComponent; normal views retain8192 exclusion.
Projection follows installed MyRender11.SetupCameraMatricesInternal: infinite
complementary FOV/aspect/near plus projection offsets, or supplied projection if
FOV absent. Rebuild once/frame on viewport changes; area uses identical Vector2
multiply/divide ordering. Stock DX11 WorkloadAudit now captures owning-job live
candidate IsCulled alongside independent evaluation and exact distance/extent/
last-LOD/projection inputs. Ordinary forward8551b7e... active (same binary both
phases, LOD/group experiments fixed on); reverse and stock verification follow.

Ordinary size policy verified both orders:15.640→13.848ms (11.46%) and
15.340vs14.005ms (8.70%). Same4056B7A... plugin/native/source hashes; actual
1386–1392 actors/frame culled, packets~8080→6480. GPU2.76–2.81ms; no consistent
GPU speedup, present~3.7ms. Keep policy off by default pending broader source
selection/pixel equivalence. ActorSize-CleanComparisons.json and
Performance-2026-10-07-ActorSizePolicy.md preserve proof. Goal remains open.
DX11 stock contract capture771f9026... PID25328 launched and sampling; inspect
live handle/reports before further action. No builds/GPU tests during sampling.

771f9026... reached a verified terminal renderer exception in the diagnostic:
root MyMeshInfo has Name, unlike LodMeshInfo.FileName. Logs/minidumps retained;
owned process/path validated before stopping, not restarted from wait timeout.
Exact source field corrected; canonical stock-contract retry launched. Ordinary
Vulkan timing evidence unaffected (this changes only diagnostic metadata).

Stock retry35a316d... completed:478 sampled main candidates,0 size culled,0
prediction mismatches. Limited negative coverage; do not claim positive rule
equivalence. Installed TrimCullProxies removes IsCulled actors, so observation
moved before trimming; d36e3e... still main-only478/0/0. Extend pre-trim capture
to all views for positive offscreen depth/probe candidates. All-view stock run
launched; current handle/report must be checked. Shader parity visibly remains
open: inspected matched-source DX11/size-enabled Vulkan views show floor/gloss,
lighting, exposure/LCD and particle differences. No default policy promotion.

All-view stock49d69534... completed:2493 candidate observations across views,
627 unique actor identities;0 actual/predicted size culls,0 mismatches. Of627,
358 exceed final-LOD distance but smallest projected area89pixels, above20.
This validates negative live rule cases only. Crucial workload evidence: stock
visibility queries admit far fewer actors than Vulkan's~3550 prepared candidate
tests. Size policy is a source-backed gap/gain but cannot establish complete
visibility parity. Next macro investigation: align native parent/manual-group/
per-view query membership against these actual stock selections before further
parallel planning or smaller cached lookups. No unsupported positive scene-rule
claim from this capture. Current macro gain8.7–11.5% remains independently measured.

Active view-membership correctness investigation: Vulkan's camera-slice cascade
predicate differs from fitted GPU light-space clip volumes, and shadow fit ran
after scene/voxel preparation. Diagnostic9569aa85... reports2451/6686 model bounds
with missing fitted memberships (includes non-casters/invisible actors; no actual
shadow-loss or GPU claim from that count). Corrected CascadeMask uses fitted
world-space frusta; fit executes before scene/voxel submission in the same frame.
Disabled/invalid shadows publish zero active masks; frozen world frusta remain
fixed while camera-relative uniform matrices change. Scene suite59 checks clean,
including a behind-camera caster, edge/outside/spanning bounds, moved/frozen
camera and disable. This restores a required view contract; no speedup claim.
Verify corrected private image/workload before eliminating any unused actors.

Corrected475eabc2... audit reports0 missed/extra fitted memberships across6686
model bounds;59 scene checks pass. Current shadow reach updated before Scene.Draw
as well. Image still shows existing floor/lighting/LCD/particle differences.
Diagnostic GPU4.38ms vs earlier incomplete-mask~2.8ms: required coverage restores
work, not a speedup. Do not reuse old <3ms GPU claims for the corrected path.
Performance-2026-10-07-ShadowMembership.md records proof/limits. Next elimination
requires valid current view membership; goal active, source save unchanged.

Corrected ordinary c6e0880c... verifies16.260msmean/15.785median/61.35FPS,
GPU4.395ms, present5.317ms (experimental LOD/group/size on, diagnostics off).
Do not retain stale2.8ms GPU claims for valid caster coverage. Source checkpoint
unchanged; next view workload elimination must preserve current fitted frusta,
spot range/cone, probe and main/highlight eligibility. Goal remains active.

Active stock far-cascade experiment: installed MyGeometryRendererOld.TrimCullProxies
requires DrawOutsideViewDistance for old-pipeline actors in cascade index>=4;
MyProxiesFactory maps the entity flag directly. Native currently submits all normal
casters to six fitted volumes (~22901 shadow commands/frame). StockFarCascades
filters only ordinary actor far bits, leaves near fitted bits and flagged actors
intact, leaves voxel paths unchanged. Defaultoff preserves baseline. Scene65 checks
pass, including authoritative queued masks and mode/batch-hint switch. Ordinary
forward comparison launched; capture must prove GPU/CPU savings and verify stock
selection/image before promotion. Do not apply new-pipeline containment trimming
to this old-pipeline scene. Far cadence and frame/cache ownership remain separate.

Far policy both ordinary orders pass:CPU16.214→15.387ms and16.491vs15.468ms;
GPU4.395→3.754ms and4.433vs3.710ms. Actualshadow commands~22900→13770,
4155 memberships/frame removed. Allbuild/source/policy checks in
FarCascades-Comparisons.json.65 scene checks clean. Defaultoff retained pending
broader source/pixel parity; inspected view has no obvious new near geometry
loss but prior lighting/floor/LCD/particle differences remain. GoalCPU15ms>4.17;
do not declare success from experimentalGPU<4.17 alone. See FarCascades doc.

Active macro presentation overlap: measured synchronous vkQueuePresentKHR span
~4.66ms. Opt-in async worker captures queue/swapchain/image/semaphore by value;
join before every owner queue submit, begin-frame acquire, swapchain destruction/
resize, cleanup and mode changes. One pending call, no concurrent queue access.
Per-image semaphores unchanged; Vulkan Khronos present/semaphore-reuse contracts
checked. Worker never mutates renderer state; result/counters apply on owner.
Native HWND test covers20 async calls plus capture, minimize/restore, resize and
mode switch, actual completed25 calls and clean validation. All7CTest passed.
Present-flow64-byte ABI reports completed call clock intervals separately from
EndFrame return; cannot claim FPS from moving the blocking call off that scope.
Async off by default. Canonical forward ordinary paired run launched, all other
experimental flags fixed. Compare actual completed call cadence and GPU time,
not only render-thread mean. Resource/generation/error paths and exception join
are the protected boundary. Goal active; no performance claim before evidence.

Presentation paired result: sync call~0.15ms now, earlier4.66ms condition absent;
not attributed to async worker. Forward10.248vs10.164ms, reverse10.269vs10.065ms;
completed-present-call intervals verify0.082/0.199ms savings and near-full coverage.
No substantial macro gain established; async remains defaultoff. Evidence in
AsyncPresent-Comparisons.json and PresentOverlap doc. CurrentCPU~10ms still>4.17.
Native present-flow64-byte telemetry separates actual call-completion cadence
from EndFrame returns. Next refresh CPU evidence under this current state; no
shader quality/default flags changed. Source save unchanged, goal active.

Current CPU diagnostic2d46e4e... completed2820 accepted frames:wall10.503ms,
render-threadCPU10.167ms, scene4.525ms, traversal3.818ms, native record3.324ms,
prepared replay0.958ms, queue1.075ms (blob0.410/native0.559), voxels0.655ms.
Inclusive rows overlap; fine actor scopes wall-only and workerCPU excluded.
Async disabled; all source LOD/group/size/far prototypes on. Present~0.15ms
current provider state. Do not resurrect stale blocked-present diagnosis or
select tiny replay-copy tuning. Next macro owner: remaining traversal/view
eligibility and whole recording/preparation. GoalCPU~10ms>4.17, parity open.
Evidence renderer-cpu-summary.json in that run; no instrumented FPS gain claim.

2026-10-07 bounded view-eligibility pass (in progress): owner traversal now tests
all existing opaque actor consumers before prepared replay: main query, probe
range, current fitted/stock-filtered cascade mask, native-equivalent float spot
sphere/cone, independent highlights. Static, skeletal and transparent ownership
remain conservative. Opt-in SE_VULKAN_VIEW_ELIGIBILITY, legacy fallback preserved.
Shadow dirty/pose processing remains before traversal, including skipped actors.
Selected cascade mask reused for both authored LOD representations. No-view
means no existing packet consumer, not a new draw distance or quality threshold.
Risk: incorrectly dropping offscreen caster or query reentry. Scene tests cover
exact pixels, rear cascade/probe/highlight and production spotlight rotation;
79 checks clean validation. Ordinary both-order experiment required before any
performance conclusion; first launch4b1fea69... underway. GPU-heavy user browser
load paused explicitly. Whole <4.17ms and DX11 visual parity remain unachieved.

View-eligibility bounded attempt completed, no gain: forward10.591control vs
10.597treatment; reverse10.527control vs10.594treatment. Skipped~23actors/~2160
per frame; native upload/sort/descriptor work not substantially reduced. Both
ordinary full1080p same-source experimental-policy comparisons complete with
matching plugin/native/checkpoint hashes; no instrumented FPS claims.82scene
checks clean, Release net48/net10 builds succeeded. Remains defaultoff as an
explicit negative experiment; do not microtune to rescue. Source/save intact.
Evidence ViewEligibility-Comparisons.json and Performance-2026-10-07-
ViewEligibility.md. Next mapped structural owner is complete retained packet
topology with explicit generations, dirty view membership and fenced per-slot
references, followed only if justified by whole owned producer snapshot lanes.
Existing byte-fill worker and retained-object hashing are rejected precedents,
not proof of that design. CPU~10.5ms and visual parity goal still open.

Native retained topology work item: remove repeated immutable resource validation,
geometry resolution, per-slot material lookup and full sorting when the complete
ordered schema is unchanged. Dynamic spans/counts/origin/flags/cascade masks and
object/instance bytes are still published each frame, with original constraints.
Schema fields: ABI size, pipeline/object layout, mesh and four material IDs.
Resource epoch invalidates recipes on pipeline/mesh replacement, release and
texture/array/voxel descriptor retirement; per-slot descriptor epoch protects
GPU-completed pool recycling and nonpersistent fallback sets. Full validated
legacy path on miss, nonblob and defaultoff. Publication only after success.
No topology retention across mode toggles. Existing sort comparator retained.
Risks: stale mesh/set after removal, slot reuse, failed partial queue publication,
and accidental constant/view reuse. Canonical native build/Test/Deploy, actual
world GPU fixture both slots and mutation/removal/retry, then ordinary Pertam
both orders with schema-hit telemetry. Anchor Topology-Before-2026-10-07.zip.

Native complete ordered-topology attempt rejected:94f749... ordinary pair
10.494727control vs10.566852treatment. ~39.3% validation reuse but3.8%material/
sort reuse;61%whole-schema rebuilds. Native geometry work0.184→0.034ms fell,
queue0.568→0.599/sort0.390→0.548 erased savings. Rebuild causes not individually
attributed; order/count/recipe/resource epoch all can force miss. Do not claim
packet reorder alone proven. No quality reduction, no end-goal gain claim.

Prototype removed from production; archive FD78E6...A937EA with relativepaths.
Exact source restoration verified against anchor, nativeTest/Deploy7CTest pass,
restored263world/82scene clean and net48/net10builds pass. Source save intact.
Evidence Topology-Forward.json and Performance-2026-10-07-NativeTopology.md.
Next macro map: immutable PreparedPart/legacyPartFrame/RigidBatch/voxelPart
recipe identity into a compact native dynamic queue. Frame-local PacketIndex
cannot be that identity. Per-dependent-resource invalidation/generation handles
and fenced slot descriptor epochs required; full ordered-schema retention and
hash-object/byte-fill-worker prototypes are rejected. Proposed design not yet
implemented, no separate planning authority. Whole <4.17ms + parity remains open.

Stable native recipe work item: immutable56-byte validated recipe registration,
generation-bearing handle, compact48-byte frame spans. Recipe persists across
packet reorder/cardinality; transient constants/counts/flags/masks remain current.
Dependency invalidation for mesh/material/pipeline/voxel changes; only mesh removal
retires/recycles handles, managed owner refs invalidated on native mesh release.
Per-slot descriptor epochs protect pool completion/reset/growth; transient fallback
sets excluded. PreparedPart/RigidBatch/PartFrame/voxelPart own recipe hints;
fallback dictionary for other opaque producers. Native recipe table bounded65536
and generation checked. Legacy blob route/defaultoff intact, unchanged sort/view
selection/recording. Canonical7CTest+world/scene/voxel fixtures with permutations,
dynamic data, replacement/removal/recreation, failure/retry and both slots, then
ordinary Pertam both orders with recipe revalidation and material reuse counters.
Anchor Recipes-Before-2026-10-07.zip. Same Khronos cached-direct descriptor reference
and stock owner contracts as prior mapped work. No claimed budget/parity completion.

Stable native recipe work item retained: same-binary ordinary both-order pairs
10.811773→10.573819 (0.237954ms/2.20%) and10.779874→10.549792
(0.230082ms/2.13%). ~99.997%material reuse,0.063recipe revalidations/frame;
packet-order independence confirmed in real game and permutation fixtures.
Native queue/geometry/descriptor work lower; sorting/uploads/scene production
still remain. Defaulton with explicit SE_VULKAN_WORLD_RECIPES=0 fallback after
all20 material/instance variants preserve exact both-slot pixels and depth/resize.

Release nativeTest/Deploy7CTest,361world/84scene/51voxel checks clean validation;
net48/net10builds and actual Legacy/Interim managed/native installation hashes
verified. Source save unchanged. Native7D7435...624118, managedInterimFCB2FC...CB42E7.
Bench39D71C... plugin precedes default-initializer-only change; explicit0/1 paths
unchanged. Data Recipes-Comparisons.json/Recipes-Deployed.json; NativeRecipes doc.
Recipe registration56B/dynamic48B, bounded generation handles, native reverse
resource dependencies, mesh retirement/reuse, context reset, GPU-slot descriptor
epochs/transient-set exclusion are protected contracts. Queue refs preserve draw
indices for highlight/exclusion, dynamic byte publication unchanged. No copied
donor code. Original shader/quality policies intact, all preexisting work preserved.

Full objective still open: CPU~10.55ms (experimental source policies), GPU~3.6ms,
DX11 pixels not matched. Next macro owner is exact native-vs-stock main actor/
part/instance/LOD selection plus whole scene/packet producer ownership. Previous
old source audits don't prove equal visibility. Do not tune the failed predicates,
ordered schema, object hashes or byte-fill workers. Current post-recipe CPU phase
ranking requires actual capture; no summing overlapping old scopes or new success
criteria around this small gain.

Mixed pipeline audit correction, authoritative2026-10-07: owning render pass
DX7147ab... captures old596418 + new5125884=5722302 indexed invocations; native
Vulkan8946ee...5892864 (~1.03ratio). Earlier post-frame active-view filtering
omitted new path after query/render cleanup. All-old and41x/~10x excess-geometry
claims withdrawn; historical raw files retained, relevant docs correction notices.
SourceMyGeometryRenderer.Render prefix and oldRender prefix now capture owner-job
work, including962 new main actors/305groups. Native logical prebatch trace exactly
matches physical index totals, no phantom first-batch records. Summary helper now
rejects captures without NewGeometryViews. Old-component-only classifications
cannot label missing new actors as culled/absent. No FPS claim from paused snapshots.

Stock cube quarter-distance only when any cube EnabledSkinning, now corrected;
ordinary cube buffers full distance. Old size/far rules experimentally restricted
to positively old actors (ForceOldPipeline, legacy instance-buffer protocol,
character), preserving unknown/new geometry/casters. Remain defaultoff.93scene
checks clean plus Release net48/net10 builds deployed. Corrected ordinary12b7ee...
frame12.045167/interval12.067123ms, GPU4.308453ms; previous~10.55experimental
baseline predates coverage scope correction. Goal performance+DX11pixels unmet.

Next structural ownership: CPU new-model eligibility classification, per-path
old/new LOD distance/state and per-view depth/forward representations, then whole
scene/packet prepare/reuse or owned work scheduling under complete workload oracle.
Old same-AABB/sharedLOD/size/far logic is not universal; new translation/scale and
pass shifts belong to new pipeline. Correct coverage precedes benchmarking gains.
One diagnostic main-query lookup failure corrected by copying its frustum on the
owning job; confirmed fatal/PIDpath stopped, logs retained; no timeout restarts.
Report Performance-2026-10-07-MixedPipelineAudit.md and geometry-contract-comparison.
No separate planning hierarchy or missing-harness block. Source save unchanged.

New actor per-view work item2026-10-07 in progress: CPU new-model eligibility uses
stock stream, material technique/facing and all authored-LOD checks, no D3D resource
creation. Initial every-model failure traced to native Bitangents tag vs stock
Binormals; metadata now reads Binormals while rendering tangents stay unchanged.
Shelf accepted/skinned character rejected; updated private88fc617... matches905
DXnew actors as eligible,57unmatched independent selection,0classified conflicts.
Data NewEligibility-Classification.json. Do not confuse unmatched actors with
proven misses. Final source eligibility must handle model/dummy/loading policies.

Opt-in SE_VULKAN_NEW_ACTOR_VIEWS owns current new-path translation/scale LOD state,
main two-level transitions, cascade/single-depth current level and forward current
level with stock pass shifts/clamping. Same LOD/alpha plans combine consumers;
packet masks preserve current fitted coverage. New0x02000000 packet flag excludes
spot depth independently of cascade mask; descriptor/batch flags propagate unchanged.
Spot plans use native conservative cone/range overlap, mark static/dynamic redraw
when representation changes. Highlights retain separate authored base identity;
shadow exclusions continue current packet IDs. Legacy old actors remain untouched;
unsupported/skinned/static ownership retains existing route. Defaultoff, not success.
NativeRelease/Test/Deploy7CTest and106scene checks pass: exact main pixels,
three-view routing, independent highlight, fallback, hysteresis/completion. Actual
Pertam both-order ordinary comparison required; forwardeb786d... launched.
No object hashes, byte-fill workers or whole ordered-schema cache resurrected.
Full <4.17ms + all DX11 pixels stays active.

New actor per-view opt-in retained: eb786d forward12.166982→11.572748ms (saved
0.594235/4.88%);4eb23b reverse12.096284→11.461721ms (saved0.634563/5.25%).
GPU windows4.311146→3.710662 and4.347991→3.675434ms (independent windows;
exact values NewViews-Comparisons authoritative).2683new actors/2401representations,
~8176current cascade memberships. Source coverage and policy restoration rather
than arbitrary quality reduction. Defaultoff pending wider saved-scene/motion/
transparent/shield/alpha/explicit fade parity. Final109scene checks clean, native
ReleaseTestDeploy7CTest pass, net48/net10builds. Source save untouched, fullgoalopen.

Initial classifier allfalse due Bitangents vs stock Binormals tag, correctedCPU
metadata only; actual905matchednew actors eligible,57unmatched,0conflicts. Source
rendering tangent basis still unchanged and remains a parity investigation. Pure
eligibility reads allauthoredLOD metadata, source factory/dummy/minloadedLOD still
needs broader matching. Old protocol/Force/character actors stayold. New distance
translation/scale, independent cascade/single-depth/forward shifts and main fade
state; native0x02000000 spot exclusion, masks/current byte/packet identities remain
live. Highlightbase independent, spotlightLOD dirties depth, modelchange resets.
Spot fixture failure corrected emptycascade settings (correctlysharedsingleDepth)
not renderer bug; actualdepth17commands anddistinctmasksverified. No claim4.17
achieved justbecause GPUwindow now<4.17. Next complete CPU producer/packet profiling
and owned scheduling under corrected mixed pipeline oracle, preserve allquality.
Doc Performance-2026-10-07-NewActorViews.md plusNewViews comparisons andeligibility.

Whole-model batch work item: currentCPU profilea2d393...2241frames wall13.213/
renderCPU13.080 diagnostic, scene7.221 traversal6.396 native3.104. New plan0.731/
emission2.744, preparedreplay1.559, blob0.624/nativequeue0.471. Inclusive scopes,
instrumented overhead, no FPS claim. Target repeated per-part rigid append:
whole immutable model schema groups share one actor instance stream and bounding
sphere per cell/flags/mask. Preserve per-part textures/tints/emission/dither and
logical owner counts, one physical packet per part; deferred finalize afterall
actors beforequeue. Identity-sensitive highlight/exclusions stay existing route.
Old/newview mask andbothslots/modes/appearance/pose exacttests, ordinarybothorders.
No worker bytefill, objecthash, wholeorderedframecache. AnchorModelBatches-Before.


Whole-model batch opt-in retained, both-order ordinary results:
Vulkan-1dd0d695343d4f24a89e0bb197933f66: 11.486616->10.947112ms, saved0.539504ms.
Vulkan-7b37f48580ea46d78b5520b5180d5763: 11.190541->10.625706ms, saved0.564835ms.
1246model actors/6494parts share79744instance append bytes perframe; no usefulGPU
gain. Model template interning onlyonrebuild, current cell/flags/masks/hsv/bounds
publish aftertraversal, physicalperpart packets. Material/tint/emission/dither
preserved, mixedHSV/nonrigid/identity-sensitive goesold path. Defaultoff until
widerpixel/motion/identitycoverage.368world/109scene checks clean, net48/net10builds.
LogicalDraws telemetry fixed tosubtract physicalmodelpackets; benchmarkfield
pre-fix is inflated andcannotbegeometryproof, nativepasscommandcounts unaffected.
Currentbaseline10.626-10.947experimental frame still>4.17, allDXpixelsnotmatched.
CPUprofilea2d393 identifies scene7.221/traversal6.396/replay1.559/native3.104
inclusive/instrumented scopes, noFPS claim. Newplan/emissionQPC scopes added;
onlywholeframeCPUmsvalidquantizedOS accounting. No per-frameobjecthash/frameorder
cache orrejectedworkerbytefill. Source save unchanged. Source anchorModelBatches-
Before. Evidence ModelBatches-Comparisons and ModelBatches doc. Next wholeproducer
reuse/ownedjobs undercorrectmixedpipelineoracle; post-sharingcapture can refine
residual, do notmicrotune isolated copies. Goal remains active/fullscope.


Authoredbasis parity correction retained2026-10-07: actual stock Binormals stream
feeds rendering, no orthogonalization/normalization of authoredtangent before
stockVF tangent/sign packing. Sign cross(T,N) dotB already correct. Generated
fallback for incomplete/runtime geometry remains. Exactall-vertex packed bytes
for4models (shelf/wall/column/plate) matchsource,113scene checks clean, net48/net10
builds. No FPS gain claim forone-time import change. Privatepaused98dcfb screenshot
visuallycomparedwithDX7147ab: darkwall/glossfloor/LCD/particle gaps remain. Do not
callpackedbasisproof fullpixelparity. Report Parity-2026-10-07-AuthoredBasis.md,
checks/screenshot/launch artifacts preserved. Source save unchanged. Performance
and allpixelgoals remainactive; nextCPU wholepreparation ownership/reuse/scheduling
stillneedsimplementation/measurement, no oldshaderlightingguesses.

Retained per-actor view plan work item: current vis/LOD/currentcascade/spotinputs
areevaluated eachframe, but complete mergedrepresentationplan andresolvedmodel
handles reuse ifcurrent/previous/alpha/main/probe/cast/spot/masks/settingsrevision
match. Model replacement resetsplan. No frozenvisibleoutput, cameraepsilon or
frame-objecthash. Defaultoff.116scene checks clean inclsettings invalidation,
spotremoval/modelreplacement/exactpixels. Ordinarypairedfull1080p required to
keep. Allcoverage andtransitionbytes staycurrent. AnchorViewPlan-Before preserves
pre-edit paths. Aim wholeplanningunits notisolatedpredicate tuning; fullgoalopen.


Retained viewplan prototype rejected/removed2026-10-08: rebuilt10.910970 vs retained10.943573ms,99.85%hits no gain. Exactanchorrestore,113scene clean, Release builds. Archive08B584...2F66074; ViewPlans-Rejected.json and ViewPlanRejection doc. Do not tune further. Original fullgoal active; next requires preparation ownership/scheduling change.

Current preparation investigation 2026-10-08: managed warmed stack trace
28217b4f captured; sampled thread time is not active CPU. Present boundary remains
opaque. Runtime events show 88 actual GC suspensions/182.56 ms, separately from
sampler/other suspensions. Missing End markers prohibit marked-frame means;
integer completion event verified in real EventPipe capture (200 paired markers).
Nsight current-policy native CPU capture 887f3339 completed sampling; export
pending. Keep this work item investigative until native attribution is readable.
See CPU-Profiling-2026-10-08-Preparation.md. No product speedup claimed this slice.

ETW follow-up 15045d4e: 482 complete frames, zero lost events, scene sampled
active CPU 7.170 ms/frame. Native PDB was stale: production DLL has no CodeView
signature, so native function claims withdrawn. Added pre-launch GUID/age check;
new optimized symbol pair passes and mismatched/stale pairs reject. Seven native
CTest checks pass; production asset unchanged. Verified-pair ETW 56877a40 now
collecting. Nsight wrapper timed out, exporter still live; preserve it.

Verified-pair ETW 56877a40 completed: 550 complete frames, zero lost events,
905/905 native addresses resolved, zero unmatched. Active sample buckets/frame:
scene 6.284 ms, native record 3.731 ms, bridge 1.467 ms, voxels 0.758 ms. Separate
native presentation backpressure ~4.700 ms is verified, internal cause unproved.
Next product item: complete actor/representation preparation ownership with
isolated CPU outputs and deterministic owner publication, guided by stock DX11
dependent cull/prepare/render jobs. Do not split only byte copying; do not tune
the rejected view-plan cache. Presentation scheduling is a separate macro lane.
Full frame <4.17 ms plus visual parity remains required, no new FPS gain claimed.

Owned preparation implementation item (Governed; existing harness interpretation
per user override): keep message/transform/spatial/occlusion decisions and GPU
resource creation on owner; dispatch complete new-actor view/LOD/cascade/spot and
cached representation CPU preparation as independent actor units. Stable model
and material handles are read only on workers. Cold/resource-dirty/transparent or
identity-sensitive work completes on owner from the already-computed plan, without
advancing LOD twice. All jobs join before deterministic original-order publication
into shared batches/recipes/native queues. No Vulkan calls or shared backend
dictionaries on workers. Exact per-slot pixels, current masks/transitions,
material/model updates and fallback/mode switching required before ordinary
full1080p both-order comparison. Opt-in only until measured. Anchor:
OwnedPreparation-Before-2026-10-08.zip plus path/hash manifest.

Owned preparation rejected/removed after both ordinary orders: +0.125201 ms
saved forward, -0.770725 ms reverse. 2683 jobs/frame, 96% cached CPU outputs,
four actual threads; 124 scene checks clean but no robust full-frame gain. Exact
anchor restored; added worker source removed; restored 113 checks clean. Archive
E15FDE...8AFEB and OwnedPreparation-Comparisons.json preserve evidence. No tuning
of this design. Next macro directions: retained scene/group ownership removing
work and independent presentation overlap at currently verified ~4.7 ms wait.
PresentBackpressure forward ordinary pair launched; no conclusion yet.

Presentation backpressure pair complete in both orders: sync18.070280 ->
async13.133450 ms and sync18.664304 -> async13.127653 ms. Saves4.936830/5.536652 ms
(27.32/29.66%); completed native present-call cadence improves equally, so this
is real throughput under measured blocked-call condition, not early return only.
Async default promoted, environment0 synchronous opt-out preserved. Seven native
CTest pass, explicit opt-out HWND rerun pass, net48/net10 builds/deploy pass;
installed native D7F6CD...721FD. Full target and parity remain open. Next macro
item removes scene/instance/packet work through retained group/native ownership;
failed whole-actor worker and view-plan caches stay removed.

Static recipe payload ownership work item: rigid/model generic batch constants
are immutable identity-transform/material bytes, currently repacked/uploaded
per draw every frame. Register that exact payload once in native-owned recipes;
select versioned persistent GPU object spans independently per completed slot.
Dynamic constants/instances/bounds/flags/masks remain current. No per-frame object
hash/byte comparison; immutable identity derives from registration and generation.
Use existing bounded reclaimed object arena; prefix offsets apply to every
consumer including indirect draws and legacy/retained fallback modes. Buffer
growth invalidates slot versions/descriptors. Retired/reused handles remain
generation checked. Opt-in until GPU pixel/invalidation/mode/growth coverage and
ordinary full1080p both-order gain. Anchor StaticRecipe-Before-2026-10-08.zip.

Static recipe payload prototype rejected/removed: 388 world and 113 scene GPU
checks, seven CTest clean, but forward -0.037979 ms and reverse +0.053320 ms
saved (opposite signs, no useful gain). ~4600 static draws share2233 objects and
steady uploads fall to4-10 bytes/frame; copying was not a dominant full-frame
cost. Existing MDI already groups equal bytes at different offsets, disproving
the proposed extra-grouping premise. Archive CCA9F3...7AA3D; exact anchor restored.
Keep async default gain. Next removes scene/packet/command construction itself,
not another object-byte cache or local worker rescue. Full goal remains active.

Frame-state parity audit 2026-10-08: actual DX/Vulkan 1280-byte blocks reveal
main forward ambient0.3 vs0 and DistanceFade3000 vs0. Source-consistent corrections
applied; actual corrected359eaae6 capture nowmatches both fields. World370/scene113
GPU checks clean, Release builds pass. No FPS gain claimed. Images stilldiffer.
Stock probe timeout0.3/res512 proves native forced5-second hold is a fidelity
gap; source stock cycle20 stages/zero-timeout allfaces remain open. Installed
RenderForward under the RenderTransparent-named helper is atmosphere only;
missing glass/billboard probe content is not established. Preserve fullgoal;
investigate this macro render
ownership contract rather than guessed lighting multipliers. See FrameState doc.

Probe scheduler ownership item: match stock first/zero-timeout all-face processing
and positive-timeout20 stages (capture6, sky/atmosphere6, mip2, GGX6), timeout from
cycle start without forced5-second hold. Face-owned uniforms/atmosphere blocks in
each fenced slot plus per-face depth storage prevent overwrite when all faces
record in one frame. Processing-stage statistics separate from face index; complete
initial probe publication and old completed final during refresh are required.
Stock RenderForward is atmosphere-only. Existing samples/resolution/mips/material
passes preserved. GPU validation/face orientation/timeout/current changes then
real private frame-state/parity and ordinary timing; no quality reduction for FPS.
Anchor ProbeSchedule-Before-2026-10-08.zip and path/hash map.
Probe work closed2026-10-08: stock20-stage/startup scheduler, face-specific V*P
vertex binding and pure pixel projection validated (300probe/113scene/7CTest,
zero errors). Ordinarybd4d1b6b 2124frames mean14.102706/median13.106ms, GPU3.889;
not a causal gain comparison, probe workload corrected. ProbeCloseout doc.
User directs return to greatest profiled costs. Next bounded native hypothesis:
complete per-light spot caster selection and secondary geometry recording in
isolated worker outputs; owner allocates images, issues copies/barriers/render
scopes and deterministic stats/execution. Fenced slot/light command pools, no
shared descriptor writes or pool access; static/dynamic cache semantics unchanged.
Opt-in until exact pixels/invalidation/resize/slot tests and both ordinary orders
prove full-frame gain. Scene frontend6.284/native3.731 remain priority; no reuse
of failed actor workers, view-plan/object caches or indirect experiments.
Parallel spotlight prototype rejected2026-10-08: ordinarybothorders saved
-0.163405/+0.201151ms (opposite signs), fourworkers ~1840-1937spotcommands.
216postprocess/113scene/7CTest clean but no robust gain. Exactrelativeanchor
restored; archive61BC5D...959896 and ParallelSpots-Comparisons.json. Do not tune
this prototype. Return to largest scene frontend and native command generation.
Current-policy detailed CPU scopes will rank new-view planning/emission/replay,
legacy paths and bridge after model batching, async present and probe correction.
Stock per-view LOD/instance groups versus Vulkan per-representation DrawWorld/
SubmitPreparedActor then packet aggregation is the next ownership boundary.
No product hypothesis until current scope measurements and owner inputs agree.
Current accepted-policy CPU detail23cb21b2 completed1823frames: wall16.090/CPU
15.385ms instrumented, scene8.312/traversal7.175/newemission2.651 (replay1.297
nested), newplan0.838, queue1.522(blob0.634/native0.574nested), record3.606.
Do not sum inclusive scopes or claimFPS. Scene remains largest; next macro
hypothesis maps retained model/group registration plus bulk current instance/view
records to eliminate actor/representation re-entry and managed packet rebuilding.
No peractor native calls, no static output freeze; all dirty appearances/LOD/
exclusions/highlights/fallbacks preserve source semantics. Requires mapped complete
registration/frame publication/invalidation contract, exactlogical/pixelchecks,
ordinarybothorders beforekeep. CurrentSceneOwnership doc is source/evidence intake.
Rollback build caveat reconciled: timestamp preserving restore reused old outputs;
f916d836 invalidated, touched/restored owners rebuilt, rejected nativeexports absent,
113scene/7CTest clean. Actual restored managed2DEFB4/nativeC556AB. Fullgoalactive.
Bulk model/group submission work item (Governed, user harness interpretation):
register immutable eligible generic model parts/recipe generation/constants once;
collect one120-byte pose/HSV/current flags/cascade/cell/bounds/ordering record per
representation. Native persistent keyed groups own instance gathering and part
expansion, preserving first-seen order with legacy events. Owner bulk API returns
legacy draw remap for highlight/exclusion consumers. Single-part generic models
included; mixed part HSV, skin/cubes, transitions, transparency and sensitive
actors retain existing routes. No peractor native calls, frozen views, workers,
objectbytehash cache or new shader semantics. Bounded generational registrations,
resource retirement clears template references before next collection; slots own
actual GPU buffers and fence reuse unchanged. Opt-in; exactlogical/pixels/material/
model/texture invalidation/bothslots/modes/consumerindices beforeordinarybothorders.
AnchorBulkModels-Before relativefiles/hashmap. NativeSceneModels added files must
be removed with rollback. One implementation owner; no subagents authorized.
Bulk native model/groups prototype initialbothorders saves0.686338/0.751835ms
(4.92/5.56%); native2289instance records expand~4207-4222modelpartpackets,
logical10584 andGPU3.8-3.9 preserved. Retain provisionalopt-in, finalretirement
ownership fix requiresfreshordinarypairs. Auditfixedglobalmeshretirement dropping
pendingrecords: dependentrecipeAlivestamps invalidateaffectedtemplates only,
unrelatedmeshes do not discardcurrentrepresentations, registries boundatframe
start.470world/180scene/7CTest clean. Finalmanaged32E5FC/native3478C4. Freshpair
launchBulkModels-FinalForward; no normal-policy/defaultperformanceclaim yet.
SeeBulkModels doc forproof andremaining normal-policy validation; fullgoalactive.
Final model-group lifetime audit complete: unrelated retirement preservespending
records; dependentrecipeAlivestamps onlyrefreshaffectedtemplates. Managedtemplate
slots andnative registrations reclaimedatframeboundary, exactpixelcache-rebuild
proof.471world/180scene clean, native7CTestunchanged. FinalmanagedC7F659/native3478C4.
Acceptanceforwarda1166b6f nowcollecting; no moresourcechangesbeforebothorders.
Bulk model/group finalbuild accepted opt-in: a1166b6f managed14.622245 ->
native13.421813ms saves1.200433(8.21%); reverse7bcc9722 native13.221688 vs
managed13.961346 saves0.739658(5.30%). Completedpresentcadence followsgain,
GPU3.863/3.866ms, logical10584, native2289records/~4212-4232expandedparts.
Noqualityreduction, currentviews remainmanaged.471world/180scene/7CTestclean,
finalmanagedC7F659/native3478C4; defaultOFF, requiresmodelbatch/recipe/retained
policy. Goalstill<4.17 plusfullDXpixels, unachieved. Current-policy CPUdetail
captureBulkModels-Detail collecting toguide largerremaining actor/view ownership.
Normal-policy independence/defaultadoption stillrequires ownproof, not inferred
fromthese experimentalpolicy pairs. Keep earlierasync/probecorrections intact.
Post-bulk CPUdetailfa84af56 completed1924frames, exactacceptedhash/policycontrol
verified(C7F659/3478C4/D5EA52). Wall15.218/owningCPU14.675ms instrumented;
scene7.497/traversal6.390/newemission1.959(replay0.626nested), plan0.784,
queue1.562(blob0.307/recipequeue0.297nested), record3.601. Bulkexpansion inside
fullqueue, outside recipe-onlyscope. Inclusive notsum/exclusive/FPS. Larger
remaining traversal is nextowner: persistent actor/view records anddirty updates
remove repeatedpreparation, preservecamera/currentLOD/visibility/dynamicresources/
consumers. Current model registration/bulk boundary is retained. No rescue of
rejected actorworkers/view/object caches, no localcopy/timer tuning. Normalpolicy
independence/defaultadoption unproved; full<4.17 andallDXpixels remainactive.
Resident actor/representation producer work item: compile opaque model/material
bindings onlyon model/appearance/resource changes; directcurrent view emission
throughacceptedbulk contract removes SelectRepresentation/DrawWorld/prepared
reconstruction re-entry for steady views (currentemission1.959msinclusive).
Camera/visibility/LOD/cascade/spot evaluation remains current; no viewplan cache.
Unsupported/transition/identity-sensitive slices retainreference route. Actor
binding version checks onceperactor, perLOD resources lazy; handles epoch/dependency
validatedbeforeuse. No worker rescue, frozen output, newshader or objectbytecache.
Optin until currentlogical/pixels/motion/appearance/resource/model/mode/slot proof,
ordinarybothorders. AnchorResidentEmission-Before relativehashfiles; added
VulkanResidentEmission.cs removedwithrollback. This is one stage of larger
resident actor/view ownership; full4.17 andnormal-policy proof remain open.
Resident emission rejected/removed: forward saves0.112750ms(.84%), reverse adds
0.573259ms(4.21%); active2289directviews/2683actors, .06compiles/frame.307scene/
471world clean butno robustFPSgain. Exactanchor restored, addedsource removed,
touchedsourcesrebuild returnsacceptedC7F659 managed/native3478C4. Archive36ECF6...
C2376C, ResidentEmissionRejection doc+Comparisons. Do not tune this design. Broad
inclusiveTraversal6.39ms doesnotidentifyexact remainingCPUowner. Next freshETW
stack/context-switch capture acceptedbulk/async/viewpolicy, currentoptimized
symbolpair verifiedGUID/age. CaptureDeepCpu accepts explicitBulkModels/AsyncPresent
so policy matches. ProfilebuildNoDeploy inprogress; source assetstaysproduction.
Goalfull4.17/pixelsactive; no newperformancegain claimedthis rejectedattempt.
Fresh acceptedbulkETW20eb1f1c complete:690frames/1382markers/0lost,1278of1278
nativeaddressesresolved209symbols/0unmatched. OptimizedF94AFA/F7219D GUIDeec0...
age4verified; productionC7F659/3478C4 unchanged. ActiveCPUbuckets/frame scene5.845,
record3.700, bridge1.638, voxel.935, other1.846; schedulingactive13.973/blocked.644.
InstrumentednotFPS/inclusivefunctionsnotadditive. CPU-Profiling-AcceptedBulk doc.
Complete actor/view preparation andcommandconstruction mustbe removed, not another
preparedbinding layer. Resident contract maps hierarchyvisibility/currentposes/
bounds, camera/frustum/distance, stockLOD/transitions/lazyresources, cascades/spots,
logicalorder/dynamicconsumers. No prototype until fullowner/dirty/publication contract
mapped; rejectedResidentEmitter staysremoved. SourceRootSavehashD5EA52 unchanged.
Goal4.17/fullpixelsstillactive; no newspeedupclaimthisturn. Restored180scene clean.
Full resident actor/view contract intake: native generational actor records own
current double world pose/bounds, hierarchy visibility inputs, stocknew LOD state
and perLOD model registrations. Owner dirty updates follow message/transform/
updater/spatial commit; directvisibility/parent changes observed through actor
component callbacks. Frame inputs are current camera/frustum/cascades/spots/global
and pass LOD settings, ordered candidate identities from existing spatial query.
Native cull/plan loops replace managed peractor preparation for supported actors;
legacy/transition/unsupported consumers preserve exact ordering and source state.
Group/peractor occlusion remains owner policy through batched current main IDs,
then completes views using resulting current hidden flags. Lazy missingLOD
requests resolve onowner and replay frame plan without advancing selector twice.
Mode switching synchronizes complete selector state. Existing accepted bulk model
ownership supplies immutable material/geometry payloads. Native outputs either
current bulk records or explicit fallback slices, plus main-visible IDs/statistics.
No frozen visibility cache, cameraepsilon, packetbytecache or worker retune. All
pose/visibility/hierarchy/material/resource/model/LOD changes and bothslots/modes
need exactlogical/pixel tests beforeordinaryfull1080p bothorders. No savings
claimed untilmeasured. SharedSource protectedbyrelativeanchor beforeediting.
Resident traversal stage closed/rejected: native generational actor records,
committed dirty visibility/pose and stock selector state; two bulk cull/plan passes
with current occlusion. Emission stayed managed, so this is not completion of the
full resident scene producer. 8 CTests/276 scene checks zero validation errors.
Ordinary both orders bd717... managed12.739564->resident13.137504 (+.397941ms3.12%);
fb443... resident12.914327 vsmanaged12.908546 (+.005782msflat). 2816resident
candidates/2683selected actual; no reliable gain. Archive3DFE485F...194EF12BB,
ResidentTraversal doc/Comparisons. Restore protected production/harness sources,
remove experimental managed files; retain isolated CPU planner/registry tests.
Next macro needs native current bulk emission as well as traversal, not a further
cull/view wrapper or local tuning. Fresh accepted ETW scene5.845/record3.700/
bridge1.638ms remains prioritization; actual command construction must be examined
before GPU-driven indirect design. Full4.17/pixels and normal-policy proof open.
Restore complete: exact protected source hashes; managedC7F659/native3478C4
redeployed. Native source rebuild8CTests clean, source relink artifact D923 differs
from accepted; exact3478C4 restoredfromverified oldbulk deployment. Finalscene180
checks zeroerrors loadedverified3478C4. RestorationJSON recordsproof. No newgain.
Approved DX11 parity implementation work package 2026-10-08: final gate mean and
P95 full-frame <4.17 ms, DX11 visuals/gameplay. Stage1 matched camera/settings/sun/
per-view geometry/state/command audit and cross-backend CPU worker attribution;
Stage2 stock shadow fitting/scheduling/pruning/occlusion + dedicated depth segments;
Stage3 full native pass-group producer, immutable/shared geometry/materials + dirty
actors, stock perinstance color/depth streams, no returned actor-slice replay;
Stage4 complete reprofile and ordinary bothorders, final threepairs. No micro/worker/
indirect rescue; new architecture needs new proposal. Baseline C7F659/3478C4 relative
anchor Dx11Parity-Before, protects original user changes. One integration owner.
CppStudio governed uses user interpretation: mapped/planned approved work is the
planning harness; no nonexistent external dependency. Current Stage1 active.
Stage1 matched diagnostic DX718c/Vkc0dc complete; same imported camera/environment,
quality/shadow settings and source checkpoint. Native logical bulk coverage now
audited separately from partial managed packets. Cross-backend ETW reanalysis in
Dx11Parity-CPU includes disjoint preparation/recording and all worker CPU rows.
Native completion sensor records actual queue-present completion interval tails,
loss checked; it does not claim scanout. New pinned benchmark artifact parameters
preserve exact managed/native treatment hashes during further source work.
Stage2 stock schedule/usage implementation opt-in SE_VULKAN_STOCK_SHADOW_SCHEDULE:
stock intervals, every-frame/freeze/jump/light-direction delays, GPU ShadowStats
authenticated base e3018f...744457, per-view delayed fenced feedback (main/probes),
native update/valid masks, persistent depth/matrix pairing, skip preparation/jobs/
execution/copies on unscheduled cascades. 206 policy and215 scene checks/0errors,
8CTests. Live matched72db updates2cascades with nonzero completed usage feedback.
Ordinary forward2fc7 baseline12.585184/P9517.0991 -> d0b6 treatment11.418752/
P9515.3391; reversea8f5 treatment11.830118/P9516.0624 vs5ea8 baseline13.005861/
P9518.0272. Full CPU gain1.166432/1.175743ms, cadence improves bothorders, no lost
completion samples. Retain experiment, no default adoption or final acceptance.
Exact tested binaries managed1F4F4F...53DBDFE/nativeFB4E5F...05874A in private runs.
Stock fitting/pruning, exact CSM constants and rasterizer states, shared model
geometry/ranges and dedicated depth segments are now being implemented; untested
new sources are not the measured schedule artifact. Per-view shadow occlusion and
complete native scene producer still required. Existing DX LCD/lighting/particle
pixel differences remain; target<4.17 mean/P95 and all gameplay gates stay open.
Continuation 2026-10-10: Stage2 stock fit/CSM constants, shared geometry and depth
segments, per-cascade group occlusion implemented opt-in. Scene221, depth29,
shadow-occlusion33 GPU checks zero core/sync errors. Warm-matched DX9dc3/Vkefdd
D32 captures identified texel snapping about relative zero rather than stock
world zero; corrected, recapture pending. No new ordinary gain claimed for these
changes. Far retained-image validity and complete scheduling cycles remain open.
Stage3 new native_pass_scene owns generational immutable model/family/actor inputs,
current per-view selection, unique query/missing-resource/fallback results and
publication to view-specific native groups. Dedicated 48B depth / 64B packed color
streams record directly into main/cascade/probe/spot passes, without WorldDraw or
shared packet expansion. Twelve authenticated simple-instance shader pairs; 9/9
CTests and NativePassGpuTests19 checks zero core/sync errors. Game scene integration
is current work: owner-committed dirty actors, immutable texture/material variants,
lazy resolution preserving prepared selection, explicit highlight/exclusion and
unsupported fallbacks, selector handoff, resource invalidation and spotlight
static image ownership. Source review confirms MyInstanceMaterial.Default packs
emissivity1 (setters initialize it); preserve this exact default. Stock main
rasterizer is clockwise; cascade counterclockwise. Native default remains off.
Acceptance still open: real scene coverage/visual/gameplay proof, structural full
frame both-orders gain and final mean/P95 below4.17. Current sources are not the
previous measured schedule artifacts. Protected baseline remains available.
Stage3 game integration now implemented: dirty resident commits, immutable binding
families, lazy selected LOD resolution without second selector advance, native per
view publication, explicit identity/unsupported fallback with selector handoff,
main-visible gameplay identities, per-view spotlight ownership and static LOD
invalidation, dependency invalidation, scene teardown before model retirement.
Actor observer queues direct hierarchy/visibility changes to the render owner.
Infinite complementary probe frusta use exact unnormalized stock clip inequalities
(the stock BoundingFrustumD normalization yields NaN for the unbounded plane).
9CTests clean; NativePassGpuTests44 checks zero core/sync errors, including imported
ArmorCenter/Shelf, direct parent visibility, highlights, both modes/slots, real
silhouettes, lazy LOD, packed streams and stock cutout material initialization.
First successful native matched abda A036A9/8268D6: 1766 resident actors, 137 native
main groups; native plus fallback main5630310/c0=1344189/c1=4680144 indices EXACTLY
match prior managed capture. Current managed4ee5 after snap shares artifacts A036/
8268; native c0/c1 D32 bit-identical to managed, far-cycle differences still audited.
DX c0 depth maximum3.576e-7, c1 only19 silhouette pixels; near fitting now exact.
Heaviest remaining eligibility gap identified by audit: DECAL_CUTOUT+MESH models
retain ~4.04 million main indices on managed path. Extend installed stock cutout
macros (sixteen texture/metal combinations), read-only depth/no blend/default back
culling; no depth or forward cutout. Stock forward also excludes alpha (MESH only).
This remains approved pass/model material parity, no new architecture. Latest
native/cutout build and imported fixture pass; matched cutout capture is next.
No ordinary structural-producer gain claimed yet; normal defaults unchanged and
final full-frame mean/P95<4.17/DX pixels/gameplay acceptance stays open.


Cutout live c611 (managed83E501/nativeF8E75A) completes:4527 native resident actors,
1135 native main groups, 4,129,683 native main indices. Native+fallback5630310 main,
1344189/4680144 scheduled c0/c1 exactly match managed4ee5; c0/c1 D32 bit-identical.
Full DX visual parity still open: cutout appearance and unscheduled retained far
images require further proof. GPU44checks zero core/sync. Ordinary forward baseline
49b355=12.177715 mean/15.8395 P95 vs nativee145=13.282862/17.2572. Reverse nativea4c3
13.110018/16.6657; baseline89037 collecting. No gain claimed or default adoption.
Native bridge .357ms vs1.002 and main commands1936 vs2484, but late preparation adds
~2.6ms; full frame regresses. Complete source-backed CPU/ETW attribution is next;
no micro/cache/worker rescue. Source MyPreparePass reveals stock per-model/LOD/view
instance streams shared across default-material parts, with extra streams only for
activated material overrides; current new producer duplicates streams per range.
Investigate this incomplete stock stream contract against measured native hotspots
before changing it. Renderer phase attribution separates late scene publication
from lighting setup. Gameplay visible identities retain pre-occlusion main-frustum
membership like existing managed AddVisible. These newest source corrections are
not the pinned83E501/F8E75A ordinary treatment (preserved in c611 private artifacts).

Reverse baseline89037 completed12.262496/P9516.1408 vs nativea4c3 13.110018/
16.6657 (+.847523ms); forward+1.105147. Queue-present completion cadence also
regresses, zero lost samples. NativeProducer-Comparisons JSON preserves bothorders.
Producer benchmark gate FAILS; do not adopt or claim gains. Baseline protected.
Profiler native EB50C445/PDBD388F251, GUIDeec0f6c8-e976-4c94-a99b-57adef905a42 age5
verified; canonical optimized ProfileSymbols9CTests pass, no production deployment.
ETW NativeProducerCpuCapture-2026-10-10 launcher prepared and UAC dispatched;
awaiting OS approval (no status/transcript or child game yet at last check).
Latest source (not pinned83/F8 treatment) corrects pre-occlusion gameplay visibility
and phase accounting; no new renderer behavior/architecture beyond this approved
boundary before fresh profile. Native per-model shared stock streams remain the
source-backed contract to investigate, not an unmeasured performance claim.

2026-10-10 continuation: elevated ETW launch was canceled by Windows/UAC; no ETW
capture started. No repeated elevation launch. Added opt-in native wall scopes
(commits, prepare/resource requests, publish/emission/static-spot decisions) and
per-frame work counts to existing CpuDetail harness. Nested scopes are attribution,
not ordinary FPS. Native9 CTests and resident GPU44 checks pass with zero core/sync
errors. Private current-artifact CpuDetail capture051fec is loading/warming; original
83E501/F8E75A producer and matched-baseline artifacts remain preserved. Ordinary
native producer gate remains FAILED; defaults unchanged. Next bounded hypothesis:
stock per-model/LOD/view/state shared instance streams, only activated appearance
material streams duplicated, direct groups refer to shared stream offsets. Inspect
native timing first, then implement only if emission is a major measured owner.
Contract map: Scene.publish -> shared CPU view streams -> prepare_pass_gpu uploads
once per stream into existing two fenced slots -> record_pass_view binds stream
slice -> diagnostic audit resolves same stream owner list. No worker, cache,
submission, shader quality or retirement policy change. Risks: per-material hidden
parts/depth merge boundaries, transitions clamped to same LOD, independent caster
views, shared offsets surviving both slots, exact owner/triangle coverage. Required
proof: CPU structural tests, real-model GPU validation, matched selection/depth,
then ordinary both-order comparisons before retention/adoption.

Continuation measured051fec current57BDF18/ABEC3A62 CpuDetail2116 frames: native
commits803.75us, prepare625.19us (requests103.98 nested), publish1712.35us
(emit1373.53 nested), spots49.39us. 292.48 committed/1702.96 selected/4904.87
packed rows,287811 bytes/frame. Source-backed shared model/LOD/view/state streams
implemented with extra streams only activated material appearances; hidden-material
coverage partitions preserve skipped depth boundaries. CPU tests assert shared base,
activated material tint/emissivity and independent depth. Nine CTests and GPU44
checks pass zero core/sync. Private matched814690 managed57BDF18/nativeC68020:
combined logical+native main5630310/c0=1344189/c1=4680144 exactly priorc611 and
managed4ee5; scheduled near D32 bit-identical. Physical main groups1252 vs1135
(stock model groups preserve parts). Full DX image mismatch still present.
Cross-run numeric actor IDs are not stable (async game load), so raw ID difference
is not a selection regression oracle; diagnostic harness now captures native owner
persistent names/root/model/exact world pose once. Prior captures only prove totals
and scheduled depth. Do not claim complete owner or texture-state parity from them.
After97b275 CpuDetail2266 frames: commits783.89us, prepare649.23us,
publish1042.77us (emit763.05),2300.13 rows/130600 bytes/frame. Diagnostic publication
reduction669.58us; no ordinary gain inferred. Native ordinary930aa9=12.588188 mean/
17.005P95; baselinebbda93=12.476978/16.3825 (+.111210 native regression). Reverse
baseline3dbf23=12.179167/15.8366; native42d485 is warming/sampling. Reverse baseline
completed all reports/zero lost presentation samples but remained exiting from
04:23:08 to04:25:01; stopped only verified privatePID25476, recorded shutdown JSON.
Normal defaults still off. Producer gate not passed. Scope further attribution of
remaining~.78ms native actor commit (input parsing/appearance entries vs resident
application), source MyInstance.UpdateWorldMatrix independently updates transforms
while SetInstanceMaterial owns appearance changes. Added diagnostic counters only,
not another performance treatment. Do not build/test during ordinary sampling.

Deep commit profile97803e managedA5CCDFE/nativeF162C70 (diagnostic-only):2280 frames,
291.49 commits/frame BUT9042.89 appearance entries/frame. Parse701.01us vs
resident apply77.84us; commits780.89us. This identifies repeated appearance
reconstruction as the dominant commit work, rather than transform application.
DX source MyInstance.UpdateWorldMatrix/SetInstanceMaterial and MyInstanceMaterials
retain material state independently from pose updates. Continue APPROVED stage3
resident input/dependency-specific invalidation contract: full appearance on initial
registration and material/model/visibility changes; explicit preserve-appearance
actor update for transform/group/dynamic-owner/exclusion notifications. No hash,
copy-loop, workers or unrelated cache tuning. Resident appearance belongs to Actor,
not a frame cache. New flag128 on batched update requires zero appearance entries;
reject on registration. On commit retain previous appearance then store normal
resident state, preserve selector and depth epoch logic. C# dirty appearance bit
separate from pose dirty; removal/registration resets full state. Source baseline
F162/A5CC and shared C680/57 retained in private captures; no default adoption.
Risk/proof: missed appearance invalidation, hidden material depth boundaries,
registration/mode handoff, parent visibility, native packed tint/emissivity after
pose; atomic batch rejection and generation handles. CPU and real GPU lifecycle
checks then paired ordinary benchmark. Current producer still fails the stage gate.

Resident appearance input boundary implemented: explicit update flag128 preserves
Actor.appearance; requires empty replacement input, forbidden on registration,
atomic batch validation. C# PassAppearanceDirty separates pose/group/dynamic/exclusion
notifications from initial/material/model/visibility changes. Registration and mode
handoff restore complete state. CPU tests cover retained named tint/emissivity and
invalid mixed batch atomicity. Nine native CTests pass; GPU62 checks zero core/sync,
including real material replacement -> pose roundtrip in both slots -> explicit
clear; actual imported actor appearance message commits material data, parent-pose
notifications commit transforms with ZERO replacement appearances, exact image
roundtrip, parent visibility, highlights/fallback/mode/real cutout/depth/lifetimes.
One fixture initially addressed the culled winding's material; corrected fixture to
visible material11, no renderer workaround. All current checks then pass. Current
artifacts3986CA9A/96FEA620 preserved in private matched894be780. Matched after ready;
launching full-appearance A5CCDFE/F162C70 reference with current persistent-owner
harness to audit exact owner/model/pose/range coverage. No ordinary gain claimed.
Shared-stream previous full-frame gate failed both orders (+.111210,+.249131 mean;
+.6225,+.715 P95), recorded SharedStreams-Comparisons JSON; stage3 remains opt-in,
under development and not adopted. New resident appearance contract is measured
at dominant~.70ms parsing9043 entries, not a hash/copy/worker rescue experiment.

2026-10-10 resumed: resident-appearance ordinary paired comparison completed with
the same3986CA9A/96FEA620 binaries and harnessE317FD0F; only NativePassScene varies.
Forward73d48d=11.480107/P9515.4239 vs10ec094 baseline11.730114/15.5756;
reverse1e52c6=12.280028/P9517.5702 vs8ae28c baseline12.031672/15.7284.
First mean gain.250007ms is NOT reproduced; reverse regresses.248356ms mean,
1.8418ms P95 and8.4944ms P99. Queue-present completion tails also regress;
zero lost samples in all four windows. GPU rolling means2.38-2.51ms.
ResidentAppearances-Comparisons JSON retains every row and marks ordinary gate
FAILED, no adoption/default changes. Diagnosticba2827:290 actor commits but only
2.709 appearance entries/frame vs9042.89 before; native commit106.300us vs780.891us,
parse49.032us vs701.009us. This is a measured dependency-boundary saving, not a
reproducible full-frame improvement. Frame/scene/native record11.545/5.831/2.69ms
diagnostic wall; fallback traversal2.47ms and publication are nested attribution.
Scene and recording remain dominant; no pivot to voxels/lighting or micro tuning.

CppStudio state: Investigative; reason=reverse-order frame tails contradict first
pair improvement; evidence=ResidentAppearances-Comparisons; exit=matched owner
attribution and complete-frame repeatability. Continue original approved plan;
no external harness dependency or new renderer architecture. Same-pinned managed
CpuDetail capturecc15ea launched to compare scene ownership againstba2827.
Existing averaged phase bucket conflates native publication with fallback planning
and cannot locate worst frames. Add opt-in bounded per-frame phase distributions,
full-frame-tail conditional means, slowest16 frame records and process-wide GC
overlap counts; separate native commits/inputs/prepare/resources/queries/publish/
fallbacks/visibility scopes. GC overlap is not pause-duration/causal proof.
No rendering, submission, worker, quality or resource-lifetime changes. Ordinary
sampling artifacts above remain pinned; no builds/tests during that sampling.

2026-10-10/11 camera recovery: native tail capturee8c28f2388 frames and managed
2452d32003 frames pass tail self-consistency with zero dropped samples. Native
phase wall means: scene6.155, traversal2.627, publication2.122 (prepare.688,
publish1.089, endvisibility.301 nested), recording2.811ms. Slowest5% frame means
18.000ms, messages2.793, scene8.111, recording3.430. GC overlaps473 frames, but
many of the slowest16 have no collection; no causal GC/pause claim.
Matched-root/model/range multisets agree before/after resident appearance in
main/c0/c1. Pose comparison matches772 main owners within3.47micrometres with
zero ambiguous candidates;22 unmatched are animated engine subparts/lights,
c0 all90 match, c1 two rotating-light bases differ. Exact authored identity,
packed GPU pose/texture binding and full DX11 visuals remain separate gates.
Like-for-like DX scene comparison must exclude old-pipeline voxel techniques:
historical main5710K vs Vulkan5630K indices, not5919K vs5630K. Stub geometry paths
resolved from authenticated stock metadata/counts. Medical-room LOD and other
per-model/cascade selection differences remain unresolved; do not claim parity.

CppStudio state: Recovery; reason=canonical ordinary repeat contradicted scope
timings and view screenshots invalidate claimed workload matching; evidence=
NativeTail baseline09edb8 versus native466743; exit=authenticated full-camera
ordinary capture. Baseline screenshot points at side machinery, native points
through the doorway. Camera positions alone agreed. Baseline MainActors60 and
WorldDraws1375 versus usual~894/2485, GPU~2.00 instead~2.4ms. Forward pair
11.375585/P9515.4062 versus10.846740/14.5562 is INVALID as a treatment comparison,
not proof of a native regression. Preserve all four completed NativeTail runs.
Prior ordinary pairs likewise lack per-frame full-camera authentication; matching
startup PNGs would not prove the entire sample. All historical savings/regression
claims require requalification with the corrected camera lane. No speculative
renderer patch added to compensate. Resident producer remains opt-in/unadopted.

Stage1 required repro fix: new private BenchmarkFrame input locks view/projection/
position/FOV and renderer environment throughout warmup and sampling; suppresses
private gameplay input only, simulation remains unpaused. Every accepted frame
checks camera and environment; failure/missing input prevents a successful timing
report. benchmark-frame.json and launch source SHA256 authenticate workload.
Paused MatchFrame remains separate. No rendering/quality/worker changes.
NativeTail profiler ABI AddCompleted() overload retained for existing callers.
Managed/harness builds pass. LockedCameraControl capture launched October11;
verify result, frame audit and screenshot before any new matched performance claim.

LockedCameraControl8c5148 completed ordinary12.379193/P9515.9555 but its frame
audit serialized as{}: anonymous read-only properties were excluded by the shared
field-oriented camera serializer. Internal frame checks passed, but the published
evidence is incomplete, so no authenticated comparison claim. Corrected writer
uses explicit dictionary keys while retaining exact camera/environment fields.
LockedCameraControl2 rerun launched; keep the first run as report-writer failure
evidence. No renderer change or extra performance treatment.

LockedCameraControl2fabea3 verifies2483 ordinary unpaused frames, ZERO camera/
environment mismatches, full view/projection matrix fields and input SHA
5435D8C321FC5A8AABEFF5FDC764641DEC672BF9632EBEF77CB7F7D6117D2594.
Verify-BenchmarkFrame.py passes; view.png visibly confirms intended doorway.
Control mean12.022408/P9515.6028ms. Writer correction compiled successfully;
same managed2BDC8829/native96FEA620 artifacts. LockedCameraNative launching next
with same inputs, no further renderer treatment. Recovery camera blocker is
resolved; reclassify next renderer investigation after matched paired evidence.
Target remains OPEN, no stage3 adoption or final acceptance claim.

LockedCameraNative8f9902 verifies2597 ordinary frames with the SAME2BDC8829/
96FEA620 binaries, harnessA6A925AC and source camera/environment SHA as fabea3.
Both source/audit checks pass, zero mismatches/unpaused. Native mean11.489822/
P9515.4453 vs control12.022408/15.6028: first qualified pair saves.532586ms mean
and.1575ms P95. Reverse native/control launched via Run-LockedCameraReverse;
not yet reproducible or final acceptance. No default change. Camera incident
resolved; CppStudio state: Investigative; reason=complete-frame benefit needs
reverse-order qualification and remaining system parity; evidence=locked pair;
exit=reproducible paired evidence plus next dominant owner. Historical unlocked
timings remain unqualified regardless of their direction.

Qualified reverse pair now complete: c49991 native11.362423/P9515.2853 versus
b52df2 control12.360414/16.4010, mean gain.997991ms/P95gain1.1157ms. All four
locked windows verify full camera/environment, same2BDC/96FE/harnessA6A925AC,
45warm/30sample, zero lost completion samples. Completed presentation P95 also
improves in both orders. LockedCamera-Comparisons JSON marks intermediate ordinary
gain TRUE, final acceptance FALSE. Native11.36-11.49ms mean/P9515.29-15.45 remains
far above4.17. Experimental retention only; visual/gameplay gates/defaults open.

Finish approved stage2 immutable model/LOD depth representation in native producer:
register_model precompiles consecutive compatible solid segments, preserving
skipped and alpha boundaries and shared geometry handles. Store source material
pieces inside each solid segment so appearance visibility splits only that segment;
publication consumes dedicated depth segments rather than replaying colour parts.
No geometry reupload, worker/submission/lifetime/shader/culling change. Baseline
2BDC/96FE retained in the four locked captures. CPU test covers hidden middle solid,
skip boundary, alpha binding and restoration. Canonical native tests and real-model
GPU validation required; no claimed performance saving until ordinary comparison.

Immutable depth registration implementation built/deployed as native33CB2757,
managed2BDC/harnessA6A925AC unchanged. All9 canonical native CTests pass;
fresh NativePassGpuTests now71 checks with ZERO core/synchronization validation
errors, including hide-all/restore preprocessed material pieces in both slots.
Paused fixed-source Pertam f918d6 launched for geometry/D32 comparison against
96FE rollback producer. No full-frame saving or final parity claim yet.

Paused f918d6 coverage compares equal to894be96FE in main/c0/c1 root/model/range/
shader/flags multiplicity. Near c0/c1 retained matrices equal and4,194,304 D32
pixels each BIT-IDENTICAL; Compare-CompiledDepthCoverage records bounded proof.
Full DX11/far-cycle gates remain OPEN. Ordinary compiled-depth pairs launched
in both orders; first completed2630 old11.344401/P9515.2750 versus2700 new
11.051101/P9514.8842, pending reverse order. No defaults changed.

Source parity lead: installed MyRender11.UpdateModelProperties (around2979)
converts a new-pipeline actor permanently to old when FlagsChange.HasValue and
IsUsedMaterialWithinModel. Vulkan applies per-material flags but does not retain
this source ownership transition, allowing native new LOD selection. Paused
persistent OwnerName comparison finds3 DX old main owners currently native:
FoodDispenser110178464399472731, MedicalRoom140452411652740817,
Jukebox104351172337410322. Medical room shares exact authored pose; DX60918
indices =LOD0 total60942 minus24 hidden screen indices, Vulkan34770 =LOD1
34794 minus24. Verify actual used-material flags/message and then correct
explicit old fallback ownership inside approved stage3. No pipeline migration,
new worker architecture or speculative LOD constant patch is authorized by this
lead. Keep depth benchmarks artifacts fixed while completing their comparison.

Compiled-depth pairs complete and all audits pass, unchanged2BDC/A6 binaries
plus onlynative96FE->33CB. Both-order mean deltas-.293300/-.041931ms; P95
-.3908/-.0227. Completed presentation P95 lower both. This is a SMALL signal,
not a substantial new saving or established stable effect size. Keep opt-in;
no normal default/final acceptance. Current treatment means11.051/11.071ms.
Reverse controller initial launch hit shutdown termination race after completed
reports; preserved evidence, waited exact private process exit, resumed only
unrun captures. Helper now waits exit after guarded termination; no timing-window
or renderer changes.

Used-material ownership regression added to NativePassGpuTests and run BEFORE
implementation: unused material preserves native ownership (passes), used
material FlagsChange={} fails expected old fallback assertion (StockMaterial
Ownership-before.log). This directly reproduces the source-rule gap without
changing geometry/quality. Diagnostic-only paused audit now exposes material
flags and source old-viewer distance for native owners;2BDC pre-fix capture
launched to verify the three real Pertam owners before applying the bounded
stage3 fallback correction. No builds/tests overlap ordinary captures.

Pre-fix c435c1 paused audit confirms actual used-material flags: medical
CockpitScreen_01/_02 remove Visible; food/jukebox screen material adds Visible.
Medical old viewer distance squared351.85596 and DX coefficient.791676 place
old selector below15m threshold; new translation selector was above it. Bounded
stage3 correction retains ConvertedOldMaterial independently of flag reset/model
replacement, uses material names from all immutable authored levels, and removes
native ownership through existing owner-thread dirty commits.79 GPU functional
checks pass. Test fixture mode handoff was moved before permanent conversion;
its unconverted-owner scenario otherwise contradicted the new sticky contract.

Verification correction: initial71/79 standalone native results DID NOT prove
active validation. Renderer silently disables validation when layer is not found;
broader SceneSubmission test correctly failed its enabled assertion, with no VUID.
Private layer already exists at Native/build/validation/Bin; process environment
was missing VK_LAYER_PATH. Tests/Run-RendererValidation.ps1 now uses that existing
layer plus VK_LAYER_ENABLES synchronization feature and official
VK_VALIDATION_VALIDATE_SYNC=1, restoring process environment after every run.
Native/depth/occlusion checks now require ValidationEnabled!=0 and report it;
earlier unconfigured zero-error statements are superseded, not validation proof.
Fresh configured suites PASS: native79, scene221, depth29, shadow-occlusion33,
probe300 =662 checks, ZERO errors. Native/depth/occlusion JSON confirms enabled1
and sync requestedtrue; scene/probe final checks also assert enabled. No VUID
suppression, barrier patch, shader change or global install needed.
Source-only SceneContentSmoke still has a stale mock facade missing existing
PreparedActor/WorldRecipe/occlusion APIs and cannot compile; this is not reported
as passing. Real installed-model scene GPU suite221 checks validates current path.
Managed net10/net48 Release build0warnings/errors; managed24995341, native33CB.
Corrected matched private Pertam capture launches next; target/default gates OPEN.

October11 continuation closeout: corrected paused e4f558 demonstrates exact
persistent identity/pose and main-view old geometry for medical60918 indices,
food25152, jukebox13926 against installed DX9dc3. All3 are explicit old
fallbacks with captured used-material conversion reasons. StockMaterial-
OwnershipCoverage JSON is bounded proof; all scene/texture/far-cycle gates remain.

Fresh LOCKED diagnostic f18c6e validates2481 completed unpaused frames, exact
camera/environment, zero dropped phase samples. Wall means (inclusive): scene
5.790ms, actor traversal2.481, native publication1.982 (prepare.602,
publish1.055,endvisibility.294), native recording2.731, queueworld.847,
voxels/billboards.881, messages.639. Mean on slowest5% full frames: messages
3.435, scene7.099, recording3.114.493 frames overlap process GC; no claim that
GC causes every tail. Owner allocations1.728MB/frame, scene.583MB and voxels/
billboards.641MB; nested scopes must not be summed. Counter read overhead.288ms.
This is diagnostic attribution, not an ordinary FPS or fresh worker/driver
stack trace. Existing ETW/Nsight leads remain historical; no repeated UAC launch.

First corrected ordinary backend comparison: Vulkan ed7b71,2555 frames,
11.674565 mean/P9516.1626ms; DX989b81,6976 frames,4.210494/P955.2785ms.
Both uninstrumented45warm/30sample,1080p, source D5EA52B3 and locked full-camera/
environment SHA5435D8C3, exact per-frame audits. Same24995341 managed and7F1C7D8F
harness; Vulkan33CB native. Frame cadence11.749261/16.2644 vs4.304311/5.3779ms.
Single backend comparison is not reproducible treatment acceptance. Current
corrected producer does NOT meet4.17ms; even this DX P95 is above target.
Do not compare corrected11.675 directly to prior2BDC11.05 to claim a controlled
regression: ownership/coverage and run order differ. No normal default adoption.
No game/capture controller remains active. Interrupted line's bounded changes
are verified, but overall approved goal and full parity gates remain OPEN.

## Proposed stage4 extension — requires user approval before implementation

Reason for extending scope: current source-parity producer still leaves substantial
opaque scene work in managed traversal/shared packets. Fresh full-camera profile
places scene preparation5.790ms first, including traversal2.481ms and native
publication1.982ms. Recording2.731ms remains second; packet bridge.847ms is
separate. These are attribution scopes, not promised removable savings.
The approved stage3 explicitly keeps old-pipeline consumers on fallback; moving
their opaque preparation/emission native changes that boundary. User approved
this extension on October11. Existing depth/appearance/parser experiments do not satisfy the goal.

Proposed work, one treatment at a time:
1. Extend immutable pass registration and resident inputs to stock old opaque
   rigid and undeformed instance consumers. Preserve OLD selector semantics,
   per-object/material compatibility and installed old shader variants rather
   than routing these actors through the new selector or changing their quality.
   Group compatible selected owners per view/model/LOD/render state; emit native
   opaque groups directly. Preserve authored instance ranges, cube HSV/dither
   flags, per-material visibility/texture changes and source shadow exclusions.
   Keep skeletal, deformed, unsupported techniques, static groups, highlights
   and unsupported instancing on explicit managed fallback. Scope instance
   support only where installed-source geometry/stream contracts are verified.
2. Split opaque and transparent consumers of eligible stock NEW mixed models,
   as installed GBuffer/Depth passes do. Current snapshot contains127 emitted
   eligible unconverted non-instance-count owners with GLASS plus opaque parts
   (103cutout/glass/mesh,16glass/mesh,8alpha/cutout/glass/mesh); this is workload
   footprint, NOT their exclusive CPU timing. Opaque preparation/recording goes
   native, GLASS keeps the existing transparent shader/sort/submission consumer.
   Return only explicit selected transparent-consumer requests, missing resources
   and unsupported fallbacks. Never return every actor's slices for managed
   opaque replay. Advance each old/new selector once; lazy resolution and
   transparent consumption reuse that selection, including transition state.

Interfaces/ownership: add a typed immutable old-pass/instance-buffer registry
with generation-checked handles, batched dependency-specific commits and
view preparation/publication. Share existing geometry allocations. Retain GPU
slot fences and retirement, existing command-pool ownership, owner-thread queue
submission and publication. No GPU-indirect wrapper, new worker architecture,
worker-count/hash/copy tuning or whole-scene cache proposal is included.

Source map: installed MyRenderableComponent/MyGeometryRendererOld govern old
LOD/proxy flags/instancing; MyPreparePass and MyGBufferRenderPass/MyDepthRenderPass
govern new instance groups and opaque/transparent separation. Local owners are
VulkanScene.Draw/DrawWorld/SubmitPreparedActor, VulkanActorLods/NewActorViews,
VulkanNativePassScene, native_pass_scene/render, WorldPackets/ModelInstances and
world queue/preparation. Existing old shader bindings/CB layouts are the oracle;
material values that remain per-object in DX11 must remain group compatibility
constraints, not be silently moved to an incompatible new shader stream.

Gates: separate benchmark switches default OFF, preserve2499/33CB corrected
rollback artifacts in current captures. First test exact per-view authored owner,
old/new LOD and triangle/segment coverage against DX11, transition/appearance/
model-resource replacement, hierarchy visibility, both slots, exclusions and
alpha/depth silhouettes. Require active core/sync native/scene/shadow/probe tests
with zero errors, then ordinary both-order45/30 comparisons, completed-presentation
tails and a fresh system profile. Reject integrations without reproducible full-
frame benefit. Re-rank remaining native publication/recording after each step.
Parallel preparation or GPU-driven architecture requires another evidence-backed
proposal if still needed. Final acceptance remains three both-order pairs with
mean AND P95 below4.17ms, matching DX11 visuals and preserved gameplay.

October11 approved extension intake: governed CppStudio work item is old opaque
resident preparation and direct recording. Owner is this chat; no worker lane.
Corrected2499/33CB private captures protect rollback. Risks: old/new selector
confusion, parent-local distance, old CB precision/layout, instance subranges,
resource replacement, and two-slot descriptor lifetime. Separate old typed ABI
and switch OFF by default; use installed old shaders unchanged, old per-object
float constants and committed 64-byte generic/cube streams. Static groups,
skinning/deformed cubes, glass/unsupported techniques, highlights/exclusions
stay managed. Old matrix/appearance constraints prevent combining incompatible
objects. Canonical native/managed builds and active core+sync tests precede any
private capture. No performance saving is claimed before matched ordinary runs.

Old opaque depth gate caught a real branch mismatch in synthetic coverage: direct
recording always chose the biased stock shadow pipeline, whereas the installed
local old recorder chooses it only when stock pass constants are provided. The
synthetic fixture intentionally uses the other supported branch. Match the
existing old recorder's conditional for cascades AND spotlights; rerun active
validation and exact depth comparison before ordinary capture. No benchmark
was launched while this gate was red.

Old boundary functional checks: typed old state, parent-local bounds distance,
all-view old transitions, retained-static/dynamic spotlight handoff, generation
checks, source rigid/generic/cube shaders and float object state. Active suites
initially700 checks/zero errors (117 native plus221/29/33/300). Source images
match unbatched old managed reference in both slots, including tint, subranges,
material hide/restore and compatible old instance grouping. Conditional biased
shadow rasterizer fix reran exact old depth comparison green.
Paused894d4e(control) /40089a(treatment), sameCCC43423/425647E0 artifacts and
79B67F01 harness, locked inputs/cascade matrices identical, nearD32 bit-identical,
but OLD ownership was ZERO in both: this is a NO-OP and no timing claim. Detailed
346e3d audit classified1832 resident stock old owners:1797 weighted cube roots,
34 rigid mixed-technique roots,1 character. Cube blend streams alone do not mean
active deformation. Installed MyCubeInstanceData.EnableSkinning is bit0 of
packed bone3.W (byte15 in the existing64-byte stream); preserve installed3/4
shader selection per model/LOD, admit only selected cube ranges with this bit
clear, and validate it natively on complete stream commits. Enabled deformation
remains fallback. Weighted skinning-disabled source shader4 fixture now matches
managed pixels, and enabling the bit returns explicit fallback. Fresh real
footprint/coverage captures required; old treatment is not retained or accepted
on the earlier no-op evidence. No normal defaults change.

Verified cube classification admits1670 owners with skinning disabled, keeps127
enabled cube owners plus34 mixed rigid roots and1 character on fallback. Source
shade type4 is preserved where weights exist; type3 remains the authored
weight-free LOD shader. Active fresh suites707 checks/zero errors (124 native).
Paused04af8d/f02ba3 with skin-aware treatment: native ownership4514->6184 (1670
old), logical rows1526->914, source old main model/LOD/range/shader-type/instance
multiplicities equal:402372 index invocations. Near cascades0/1 bit-identical.
Industrial Dropship independent-load translation differs7.629e-6m, so strict
full-pose owner joins falsely missed41 aggregate main ranges; do not call these
selection changes. Persistent root/bucket/model coverage and captured pose
residuals are separate facts. Full renderer/DX11 visual gates remain open.
Complete immutable material ownership by reusing existing WorldSlot cache kind6
for old bindings. If old object storage grows, invalidate existing slot cache
BEFORE creating world descriptor sets, under its completed-slot fence. No new
cache/worker architecture. Canonical build and active validation, then ordinary
paired captures one old switch at a time. No ordinary saving yet claimed.
October11 old opaque ordinary gate REJECTED. Both orders regressed mean by
1.428724 and1.850498ms, P95 by1.1115 and1.9042ms. Exact ordinary inputs and
completed-presentation tails verified; evidence Dx11Parity-OldOpaque-Comparisons.json.
Fresh diagnostic3847aa: traversal2.481->1.287ms, publication1.982->4.176ms,
native emitter2.169ms nested within publish2.928ms, with no GPU calls/waits
in these scopes. Fewer commands did not produce full-frame gains. Source archived
in TestResults/RejectedOldOpaque-2026-10-11 and active integration removed.
Restored managed build green; five fresh active core+sync suites green. No defaults
changed. Do not reintroduce the old emitter or tune its maps/copies.

Next approved work item: NEW mixed opaque+GLASS resident preparation. Separate
benchmark switch OFF; immutable LODs retain opaque shared ranges and glass-consumer
metadata. Native publication returns sparse generation-checked main-view glass
requests (owner, selected LOD, existing glass transition alpha), never opaque
slices for managed replay. Managed glass shader/sort/submission owns transparent
parts only; unsupported techniques and consumers retain explicit fallback.
Resource resolution and glass consume the same once-advanced native selector.
Rollback source anchor TestResults/MixedOpaque-Baseline-2026-10-11/Source.
Required gates: CPU selection/transition/cancel/fallback checks, active GPU
validation, real paused coverage and glass visuals, ordinary both-order timings.
Overall mean AND P95 below4.17ms remains open.
NEW mixed boundary implemented OFF by default (SE_VULKAN_NATIVE_MIXED_OPAQUE).
Native immutable LOD includes opaque shared ranges plus glass metadata; glass-only
LOD is supported without empty GPU groups. Sparse main-view tickets carry actor
generation, owner, selected LOD and existing glass shader alpha (3-t/current,
t/previous); selection/resources/publication remain native and selectors advance
once. Existing managed glass sort/shader/queue path consumes only GLASS parts.
Static/instanced/skeletal/highlight/exclusion/unsupported consumers remain fallback.
Canonical native9 tests and managed build green. Active suites679 checks/zero
core+sync errors; additional exact mixed colour gate green (native97 checks).
Installed HalfWindow glass mesh/texture/object/material packets and composed colour
match managed reference exactly, depth matches in both slots with normal stock
shadow constants; appearance, parent visibility, switch handoff and teardown pass.
CPU checks cover lazy transition tickets, hidden main vs independent views,
cancellation, empty opaque LOD and missing-resource fallback.
Paused478190/a4da31 same binaries/input/cascade matrices: main aggregate grid/
base-model/selected-model/range multiplicities equal (5,656,458 index invocations),
near cascades0/1 D32 bit-identical. Native resident actors4514->4836. Generated
subpart entity names differ on independent loads; aggregate coverage is not exact
individual instance identity proof. Full scene image has visible doorway lighting
difference; forward/probe work changes under installed MESH-only preprocessing.
This is an open full visual gate, not accepted quality parity. Ordinary both-order
45/30 timings will quantify intermediate benefit only; no gameplay default adoption.
Ordinary both-order NEW mixed performance gate: control10595b -> treatmentbb7b8c
mean10.766730->10.458398ms, P9513.2279->12.5648; reversed treatmentf4d335 ->
control06845d mean10.354797 vs10.732241ms, P9512.4793 vs12.7923.
Mean gains0.308331/0.377443ms; P95 gains0.663100/0.313000ms. Exact matched frame
verification passed all four; identical hashes/settings except switch. Completed
presentation P95 also improved13.2899->12.6377 and12.8650->12.5403, no lost
samples. Not final acceptance, not promoted: full scene visual gate remains open.
Evidence TestResults/Dx11Parity-MixedOpaque-Comparisons.json. Detailed both-config
phase profiles underway to rank the next large systems. Additional architecture
still requires an updated proposal and user approval before implementation.
Fresh detailed comparison3f753e/56d038 (instrumented, exact unpaused inputs):
Frame11.565->10.825ms; Scene5.861->5.422; ActorTraversal2.454->1.708;
NativePassPublication2.092->2.407 (prepare.681->.733, publish1.076->1.184,
end visibility.304->.288 plus sparse glass consumer); QueueWorld.828->.694;
NativeRecord2.789->2.682; VoxelsAndBillboards.790->.773.
Native emit.783->.861ms nested in publish1.073->1.182; native preparation
has no GPU calls/waits. Owner allocations1.739->1.741MB/frame; no allocation
reduction claim. Workers excluded; inclusive rows overlap and cannot be summed.
GPU last10-second windows2.480/2.432ms are not full sampling distributions.
Evidence Dx11Parity-MixedOpaque-PhaseComparison.json. Latest native colour
fixture97 checks plus other suites221/29/33/300:680 checks, active zero errors.
Implementation benchmark switches remain OFF. Full visual and4.17ms gates OPEN.

## Resident per-view preparation jobs — approved, implementation in progress

Problem: even after NEW mixed handoff, instrumented scene5.422ms exceeds the
entire4.17ms budget. Scene preparation/publication and recording remain the largest
systems. The rejected OLD emitter saved traversal but materialized temporary
per-view groups/streams and doubled publication; do not revive it or tune its maps.
Installed MyRenderScheduler explicitly schedules each cull view's NEW preprocess,
OLD proxy update/prepare, and rendering with dependency jobs. MyPreparePass counts
LOD/state groups and writes their instance planes into allocated offsets. Vulkan
still prepares/emits its resident views sequentially on the owner and expands the
remaining managed producer. This is a structural job/data-flow parity gap.

Proposal: resident per-view preparation jobs and direct stock proxy recording.
1. First reconcile mixed probe/lighting coverage against installed DX11 and audit
all active view ranges/material states. Preserve full quality; no performance
acceptance from a reduced probe workload or unresolved visual differences.
2. Freeze one generation-checked frame input epoch after owner commits. Advance
NEW/OLD selectors once, resolve resources and collect owner-thread occlusion
results before workers consume the selected snapshot. Worker jobs cannot mutate
actors/models, submit queues, retire resources or call managed game APIs.
3. Prepare independent active views as CPU jobs. For NEW groups, use stock count/
base-offset/fill preparation and separate transform/appearance planes. Publish
complete model/LOD/state groups, not a per-part colour-packet expansion. This
changes the preparation boundary, not hash/copy/worker-count tuning.
4. For OLD supported consumers, immutable proxy definitions and committed source
instance resources remain resident. View jobs output selected proxy references
and subranges; record their installed OLD shader/object-state contract directly.
Do not scatter every selected source instance into temporary per-view streams or
rebuild old group/part maps. Skeletal/deformed/unsupported/highlight/exclusion
consumers retain explicit fallbacks. Retained GPU resources retire only after use.
5. Join preparation transactionally on the render owner, then publish/submit.
Initially preserve existing GPU command-pool architecture. If recording remains
large, separately gate DX11-style per-view recording jobs with exclusive slot/
worker pools and owner-controlled submission; no rejected spotlight worker reuse.
No GPU-indirect packet wrapper is included. Indirect remains a separate proposal.

Ownership map: Native/src/native_pass_scene.h and native_pass_render.inc;
Native/src/world_models.inc/world_render.inc; managed VulkanNativePassScene,
VulkanScene/ActorLods/NewActorViews and immutable model/instance registration.
Oracle: installed GeometryStage2/PreparePass/MyPreparePass.cs and
Render/MyRenderScheduler.cs; OLD proxy contracts remain installed Render11 source.
This expands the worker architecture beyond the earlier no-new-worker pass.
The user's October11 approval authorizes this proposal; no further approval is
needed for the described implementation.
Benchmark independent stages OFF by default against protected current controls.
Measure owner active CPU, worker CPU, joins/dependency waits and full-frame tails;
per-view source coverage, visuals/gameplay, generation/resource replacement and
both slots must pass active core+sync validation with zero errors. Keep only
reproducible full-frame gains, then three both-order45/30 pairs for final mean AND
P95 below4.17ms. Phase speedups do not meet the goal. No saving is promised before
those measurements; workers can merely move work onto waits and must be rejected
if full-frame time does not improve.

October11 continuation: user Approved the per-view jobs/direct proxy proposal. Current owner remains this chat; no subagent lane. Begin mixed probe/lighting parity gate, then independent count/fill and CPU view-job switches. Backup TestResults/ViewPreparation-Baseline-2026-10-11/Source protects current mixed experiment and accepted switches-off control. Previously stated approval requirement is now satisfied for this proposal.

NEW count/fill and CPU view jobs are integrated behind separate process benchmark
switches, both OFF. Model generation and material visibility bind resident state
IDs; pose and appearance values preserve compatible bindings. Each job counts
view groups/activated appearance planes, allocates offsets and fills final view
storage directly. It neither expands colour packets nor calls Vulkan/managed
APIs. All worker lanes (including lanes with zero tasks) acknowledge completion
before the epoch is reused. Failed views drain/join before publication; selectors
commit only after complete output allocation. GPU uploads/pools/submission remain
owner-owned with existing frame-slot fences. Retired model CPU states clear actor
bindings before ID reuse; GPU geometry uses its established retirement lane.
Native tests exercise all three modes plus1000epochs, zero-job epochs and injected
worker failure; native9tests and managed/harness builds green. Fresh full active
core+sync GPU suites pass with parallel preparation enabled and zero errors.
Ordinary both-order count/fill captures are running; no saving/adoption claimed.
Worker wall/thread CPU/cycles and owner dispatch/join profiling are separate from
the existing nested owner scopes; worker sums are not frame-critical-path times.

Six-face probe captures1ae2a76(Vulkan)/3dc43996(DX11) completed. Initial DX11
paused capture093f9a24 was cancelled as incomplete: paused game time prevented
stock probe scheduling from advancing. Repeat requests one owner-thread stock
probe refresh, diagnostic only. Aggregate model/LOD/material/triangle counts
match exactly on comparable face1 groups; other faces show a small set of owner
differences. Raw first indices are not comparable because DX11 preprocesses and
reorders technique ranges. Evidence Dx11Parity-ViewJobsProbeCoverage.json;
no full visual acceptance. Installed MyModel.UpdateBoundingBoxes unions all LOD
boxes, whereas native scene currently installs root Bounds. Identical railing
transforms have different culling boxes and five source probe owners are omitted.
This is a concrete parity investigation to validate/fix independently after the
fixed-binary count/fill window. Direct OLD proxy implementation remains pending;
the rejected OLD scatter/map emitter stays removed. Overall4.17ms target OPEN.

NEW view preparation gate rejected, source archived and active integration removed.
Serial mean regressed0.479111/2.219991ms in both orders (P95-.1437/+5.8106).
Complete parallel culling plus per-view selection/count/fill mean regressed
0.505764/0.293953ms; P95+2.1751/+1.3630, completion tails also worse. All eight
ordinary captures authenticated fixed camera/environment, unpaused45/30 windows,
same binaries within each experiment and no lost completions. The complete
diagnostic reduced native publication1.225->.902ms (emit.895->.496) but prepare
.877->.878 and Scene5.824->5.842, Frame11.585->12.071. Worker task wall sum1.302ms,
owner task wall.729, dispatch/join.096; these are not additive frame critical path.
Evidence Dx11Parity-ViewPreparation-CountFill-Comparisons.json,
ParallelPreparation-Comparisons.json and PhaseComparison.json. No promotion and
no further tuning of this rejected NEW integration. Protected original pass
producer/ABI/recording restored; archived source and manifests under
TestResults/RejectedViewPreparation-2026-10-11. CPU executor is only an unconnected
dependency of the approved OLD reference prototype, not active NEW rendering.

Bounds parity investigation: union of MWM LOD boxes alone did not match the
captured actor box. Installed MyModelFactory asynchronously starts with
Models/Debug/LoadingQuad.mwm; MyInstanceComponent.Init and MyInstance.SetModel
use AddLocalAabb, retaining that loading bound after real model reload. Preserve
stub-authored bounds separately from GeometryDataAsset vertex metadata. The
OFF-by-default SE_VULKAN_STOCK_LOD_BOUNDS switch uses installed loading-model
bounds plus authored NEW LOD union; no root geometry/appearance change. Explicit
message/RenderDirect local bounds retain ownership. Installed railing numerical
oracle and both frame slots pass; native106 + scene221 + depth29 + occlusion33 +
probe321 checks, active core+sync zero errors. Real six-face Pertam capture running;
full visual and performance gates remain open, no default adoption.

OLD reference CPU prototype now compiles and passes in the canonical native lane
(10 tests). Separate stock_proxy_lod/resident_proxy_scene/references retain OLD
current/next float alpha, parent-local squared distance, generation-checked CPU
models/actors, sparse lazy requests/fallbacks and per-view source stream subranges.
No per-instance stream scatter, no managed part packet expansion. Checks cover
lazy resolution, independent main/shadow visibility, transitions, cancellation
and retired handles. GPU resource registry, shader/object ownership and direct
recording are not connected yet; do not claim renderer savings from these tests.
They are the next approved implementation surface; OLD rejected emitter remains
removed. Overall mean AND P95 below4.17ms is still OPEN.

Completed bounds capture e9ce4242: face0 comparable model/LOD/material/count
coverage now equals DX11 (191,292 index invocations), resolving the five omitted
railing owners in that face; face4 also equals (95,490). Face2 retains two railing
owner differences and includes more moving small-grid work; face1 moving-light/
small-grid selection differs on the independent load. Other faces retain small
owner differences. Evidence Dx11Parity-StockLodBoundsProbeCoverage.json. This is
partial source parity evidence, not full visual acceptance, actor identity proof
or performance saving. Capture exited unsaved. Stock bounds remain OFF; accepted
producer/command ownership and normal gameplay defaults remain protected.
Next work is still the approved OLD resident resource/object/shader registry and
direct GPU recording integration, followed by source coverage and ordinary gates.
Latest complete ordinary control2539233d: mean11.224120/P9515.4826ms; target OPEN.

October11 approved OLD direct recording continuation: native GPU model/stream
registry and managed scene ownership are now connected behind
SE_VULKAN_RESIDENT_PROXIES (OFF). Source generic/cube rows remain resident at
64 bytes, shared across selected views/subranges; committed revisions upload
only after each slot fence. Model parts use shared immutable geometry/recipe
handles; workers publish native proxy references and never call Vulkan. The
owner builds OLD float object constants and binds/records their installed
pipelines directly, with no per-view instance scatter or managed color packets.
Publication validates source generations/ranges before selector commit; lazy
models reuse the selection. Retired buffers use existing timeline retirement.
Scene callbacks commit transforms/hierarchy/visibility/material changes;
OLD selector import/export preserves handoff. Skeletal/deformed/static/GLASS,
highlight/per-light-exclusion and unsupported consumers retain explicit fallbacks.
Static spotlight images invalidate on proxy/selector changes; transitions move
to dynamic ownership. Native stock size policy/far-cascade parity is being
validated before private capture. NEW rejected preparation remains removed.

Native10tests and the first full active GPU validation pass are green: native159,
scene221, depth29, occlusion33, probes321 (763 checks), zero core/sync errors.
Evidence ResidentProxy-AuditNativeBuild.log and ResidentProxy-AllValidation.log.
Native generic-source fixtures match legacy color/depth on both slots; scene
cube fixtures match pixels, bypass worldPackets, reuse steady source streams,
and check visibility, appearance, source replacement and deformation fallback.
No performance/visual acceptance or default adoption. Final size/far policy and
selected-view diagnostic audit changes require their fresh canonical rerun.
Protected rollback source: ResidentProxy-GpuBaseline-2026-10-11/Source.
Next: completed-cycle Pertam source/probe coverage, ordinary fixed-input full
frame comparisons in both orders, then profile measured remaining costs.
Mean AND P95 below4.17ms remains OPEN.

## October11 pause checkpoint — user requested pause after current work

Completed the approved OLD resident-proxy GPU integration, its correctness
fixtures and both-order ordinary screening. The experiment FAILED retention
and is now archived and REMOVED from the active renderer. User requested pause
at this checkpoint; do not continue implementations/captures until resumed.
Current implementation owner is this chat; no agents or scheduled follow-ups.

Ordinary locked-input45/30 results (mean/P95 ms):
control b2a2d72412.322252/17.2096 -> treatment68bbe18412.133903/16.7021;
reversed treatment7c0e231815.484988/23.5139 ->
control0bd4850b11.015529/14.7128.
First-pair gain.188349/.5075; reversed regression4.469459/8.8011.
Completed-presentation P95 control/treatment17.1246/16.2118 and
14.4820/23.5232. All four frame-input authentication checks passed, unpaused,
identical plugin/native/harness hashes, no lost completion samples. Opera video
decode7% was observed during later control startup; no synchronized whole-window
external-load trace. This failure establishes no reproducible benefit, not the
precise cause/magnitude attributable to architecture alone.
Evidence Dx11Parity-ResidentProxy-Comparisons.json and OrdinaryPairs.log.

Paused treatment594930/control2cc9db57 audited1,584 resident OLD actors,
509 immutable families and52 source streams, zero publication fallbacks in the
snapshot. Main geometry totals agree5,656,458 index invocations; near cascade0/1
readbacks agree bitwise. Owner/LOD assignment and lighting still differ;
image channel MAE6.120884/changed61.37%, far cascade/probe differences unresolved.
No full source/visual acceptance. Native/scene and all-six-face OLD generic/cube
radiance fixtures passed active core/sync, but fixtures do not certify Pertam.
Evidence Dx11Parity-ResidentProxy-Coverage.json. After failed retention,29 source
files archived under RejectedResidentProxy-2026-10-11/Source with SHA manifest;
14 protected original files restored and seven new live GPU/managed files
removed. Rejected OLD scatter emitter and NEW count/fill integration remain
removed. CPU-only reference/selector/policy tests remain unconnected to Renderer.
StockLodBounds remains OFF; NEW mixed opaque remains an unpromoted experiment.
No normal gameplay defaults changed. Native10tests, managed/harness builds green;
fresh full GPU rollback validation passes710checks with zero core/sync errors.

Plan status at pause:
1. Matched-input benchmarking and view/geometry auditing exist. Full DX11 OLD,
NEW/static selection/material parity and correlated owner/worker/driver/wait
coverage remain incomplete; independent animated/load states need reconciliation.
2. Stock fitting/scheduling/usage and dedicated NEW/shared depth geometry exist.
Complete retained image/matrix update-cycle, moving caster/spot ownership and
visual quality gates remain open. Bounds fixes resolve some missing owners,
with conservative loading/LOD bound history and other face differences open.
3. Native NEW resident producer exists opt-in. Mixed producer has historical
both-order gains. Complete scene boundary, all fallback/identity/resource and
source quality gates remain open. NEW jobs/countfill, OLD scatter emitter and
this direct OLD integration are rejected. Do not revive/tune them in isolation.
4. Latest complete protected-control diagnostic: Frame11.585ms, Scene5.824,
NativeRecord2.705, QueueWorld.724, Voxels/Billboards.812. ActorTraversal1.859 and
NativePassPublication2.625 are nested within Scene. Do not add overlapping rows.
Latest ordinary control mean11.015529/P9514.7128; target mean AND P95<4.17 OPEN.
Halving11.02ms gives5.51ms, still above goal; about62% mean reduction is required.

Structural paths for the next proposal, not authorized changes during pause:
- Persistent native scene epochs: separate immutable geometry/material state,
actor/parent transform updates, view constants and resource generations; batch
only committed dependencies and build final render-owned output once. Remove
repeated scene/managed bridge work across the complete boundary. Source parity
and an exact current CPU critical-path profile must identify the repeated work;
no return to an isolated culling worker or expansion into shared packets.
- A complete preparation/recording dependency graph using exclusive frame/view
pools, immutable inputs and owner submission. Target measured nonoverlapping
Scene and NativeRecord together. This must shorten the frame critical path;
previous isolated NEW jobs did not. Do not repeat rejected spotlight workers.
- A GPU-resident draw/material/actor database with genuine indirect view culling,
LOD/command generation and per-view execution. This could remove CPU selection,
packet expansion and thousands of driver draw/bind operations together. It is
explicitly deferred architecture and needs a fresh design/approval; another
indirect wrapper around current packets is rejected. Preserve OLD shader/alpha,
source subranges, exclusions and gameplay quality; GPU timing/headroom gate too.
- Exact shadow caster dependency/update parity and valid retained static depth
work can eliminate work rather than accelerate every replay, but only if a fresh
profile shows it is dominant. Scheduling already exists; do not promise the
original19,800-cascade-command savings again or reduce quality/update settings.

These are hypotheses for millisecond-scale changes, not measured savings or a
promise to halve CPU. Voxels/lighting follow only if they become dominant. Next
resume should reconcile source/lighting state and obtain one correlated current
critical-path profile, then propose a bounded complete architecture against the
largest measured system. Final proof still needs zero active validation errors,
matched DX11 visual/gameplay quality, completed cadence tails, and three both-order
45/30 pairs with both mean and P95 below4.17ms.

## Lighting reconciliation — resumed by user October11

The user resumed only the lighting reconciliation before returning to the plan.
CppStudio Investigative: distinguish an OLD renderer shading/retention defect
from mismatched independent-load light/probe/exposure state. Production renderer
and rejected integration remain protected; use the archived experiment DLLs only
inside the private capture. No performance change is authorized by this diagnosis.
Existing frame-state snapshots have identical encoded main light/environment
fields, but control probe cycle was complete and treatment was busy atstage10.
Local-light packets, spotlight projections/depth and temporal buffers were not
captured in the original comparison, so historical lighting cause is not isolated.

New diagnostic-only harness runs OFF/ON/OFF in one paused loaded world. After
control, replay exact native local-light/spot/main-frame/probe-frame/postprocess
inputs. Complete zero-timeout probes/all-cascade refresh; retained-spot policy is
an independent variant. RGB/probe/cascade completed readbacks carry SHA identities.
No ordinary FPS claims or gameplay setting/default changes.
First54d31 capture failed at readback request hook and was cancelled as incomplete;
request now binds the actual renderer Present entry point. Second9faab captured
valid OFF and ON snapshots but generic240s watchdog ended it before return due to
an incorrect phase-delay/deadline coupling. Corrected that coupling; repeat with
natural retained spotlight policy. These are diagnostic harness failures, not
renderer hypothesis tests or accepted full three-phase runs.
Partial fresh-map OFF/ON: exactmain/light/spot/postprocess/probe/projection input
hashes, all36 probe images and six cascade depth images match bitwise. RGB mean
absolute channel difference.000946 (scene.001970/floor.009536), versus historical
6.120884. Retained-map OFF/ON/OFF must pass before closing this uncertainty.

Lighting reconciliation completed: exact-policy retained spotlight OFF/ON/OFF
run d8c7616e exited unsaved with all three snapshots. Actual RetainedSpotPolicy
is TRUE in launch manifest and each snapshot. Prior complete4ce796e run was
fresh-map policy FALSE despite its filename; a missing helper parameter was
caught from state metadata, fixed and repeated. That earlier fresh run remains
valid only as a fresh-map diagnostic. No retention claim derives from it.
Evidence LightingReconciliation-RetainedSpots-Comparison.json and
LightingReconciliation-FreshSpots-Comparison.json; final launch manifest
LightingReconciliation-RetainedSpotsLaunch2.json. Both complete same-process
OFF/ON/OFF runs match exactmain-frame, native local lights, spot projections,
postprocess configuration, probe-frame/projection hashes and cascade matrices.
All36 probe images (original, filtered and final near/far, all6faces,mip0) and
all six cascade depth images match bitwise in both phase comparisons. Retained
policy screenshot control/treatment MAE.001181038 channel levels (0..255),
99.949315% identical pixels; left-stair region is bitwise identical. Returned
control MAE.000136799; fresh-map control/treatment MAE.000915959. Historical
independent-load MAE6.120884 is not reproduced under matched temporal inputs.

Conclusion: the earlier broad lighting difference is not evidence of an OLD
shading/spot retention regression. Historical main blocks differ only atfloat
offsets1264/1272 (time/random-seed); encoded sun/ambient fields agree. Historical
local-light packets and retained temporal GPU state were not captured, and
probe cycle stages differed, so a unique historical cause cannot be recovered.
Classify those images as unmatched temporal/local-light capture state, not a
certified renderer defect. No speculative brightness, shader or quality patch.
This closes the large cold-load lighting discrepancy as a blocker to critical-
path analysis; it does NOT certify all DX11 visuals, alpha/highlight/fallback
coverage, natural staggered probe cycles, or performance acceptance. Retention
run uses normal spotlight-map invalidation, with identical completed probe and
cascade refresh conditions. Rejected integrations remain removed and archived.

Only diagnostic harness/launcher/parser and this document changed in this
investigation. Separate concurrent ray-query edits were found in Native
Renderer.cpp/se_vulkan.h while checking the old source anchor; preserved without
modification. Pinned private DLL SHA validation excludes those changes from this
lighting comparison. Do not claim current-ray-work validation from these runs.
Return to canonical sequence: correlated current CPU critical-path attribution,
then an updated millisecond-scale proposal for the dominant scene/recording
systems. Mean AND P95<4.17ms target and full DX11 quality/gameplay gates OPEN.

## Current critical path and approved GPU-resident opaque scene

CppStudio state: Governed for the following cross-subsystem proposal. Existing
user override applies: CppStudio supplies map/plan; there is no separate skill
installation blocker. The active canonical work item is current critical-path
attribution followed by a structural proposal. No architectural implementation
was approved by the profiling request alone. Subsequent explicit user approval:
"Approval granted, including the complete GPU-resident opaque scene path."
Stages A through D below are now authorized; normal defaults and acceptance
remain gated. Implementation started 2026-10-11 with protected source and binary
anchor TestResults/GpuOpaque-Baseline-2026-10-11. Concurrent ray work is preserved.

### Completed evidence

Frozen artifacts CurrentCriticalPath-2026-10-11/Binaries: plugin779519DF,
optimized symbol-bearing native403272A8, harness88A402F5. Native source includes
concurrent ray-query work, preserved; supported hardware is detected, but both
captures report ZERO ray builds and dispatches. None of these profile binaries
was deployed to normal gameplay. Vulkan API wrappers and scopes are diagnostic,
OFF unless the private harness enables them, sampled every8th frame. They do
not change render selection, quality, GPU synchronization, pool ownership or
queue ordering. Native11tests pass; fresh private core+sync GPU native107 and
probe321 checks pass with profiling enabled and zero errors.

Ordinary control c3eff772:2,749 unpaused completed frames,45s warmup/30s sample,
1920x1080, render fraction1, VSync OFF, cap1000. Mean10.820850/P9514.3900ms;
completed-present cadence mean10.915116/P9514.1807ms, no lost completions. GPU
rolling10s mean2.403350ms is a lead only, not a whole-window GPU distribution.
Exact full camera/environment verification passes; source save SHA remains
D5EA52B3. This is the existing opt-in NEW/mixed/stock parity control, not proof
that all normal defaults or DX11 quality gates are accepted.

Repeated instrumented captures66e87f53/20df2e28:2,545/2,517 accepted unpaused
frames; same plugin/native/harness and matched inputs. CPU attribution mean:
Frame11.4746/11.5945ms; whole render-owner CPU aggregate11.3765/11.4596ms.
Scene5.5055/5.4815; native recording2.8912/2.8846; packet publication.7250/.7663.
These three NONOVERLAPPING scopes total9.1271ms,79.13% of diagnostic frame wall.
Scene includes actor traversal1.8166/1.8108, native pass publication2.3362/2.3791
and commits.4194/.3663; do NOT add those nested rows to Scene.
Native selected-frame prepare.6651/.6916ms, publish1.1755/1.2096, GPU preparation
.9218/.8989. Native group publication emits about2,541 rows/144KB and2,289 groups
per frame;303 actor commits/frame, about1,713 selected actors. These volumes do
not support a bandwidth/copy-tuning proposal.

Recording samples318/315. Command API calls20,427/20,741 per selected frame;
raw timing1.3437/1.3444ms includes loader/layers/driver and timer cost. Empty API
probe calibration14.336ns/call gives approximate above-probe-baseline1.0508/
1.0471ms, NOT exact isolated driver latency. Descriptor calls3,291/3,356;
approximate adjusted time.1254/.1279ms. Other explicit wait wall scopes are small:
GPU timeline.0267/.0259ms, cascade joins.0544/.0416, present joins.0031/.0026,
acquire.0021/.0022. Worker recording elapsed sums.6417/.6391 overlap the owner,
not additional frame critical path. Sampled RecordOwner4.0585/4.0700ms includes
heavy probes and must NOT replace the all-frame2.89ms recording estimate.
Native counter read overhead.785/.799ms per SELECTED frame plus in-interval
probe cost; managed counter reads.230/.228ms per diagnostic frame. Profiling
cannot be presented as ordinary benchmark timing or as an optimization gain.

Short sampled Windows CPU-ms fields aliased (one probe bucket exceeded its own
wall time). Those phase CPU-ms values are explicitly excluded from analysis and
future report presentation. Use whole-frame CPU aggregation, raw thread cycles,
QPC wall intervals and explicit waits; do not convert cycles to milliseconds.
Nsight2026.5.1 CPU sampling/scheduling requires admin; earlier canceled elevation
is not repeated. No complete OS scheduling trace or isolated driver CPU claim.
Periodic GPU observations found no external engine>=10%, which is not proof of
continuous absence. Evidence CurrentCriticalPath-Analysis.json, original per-run
reports, input verification, completed presentation and calibration artifacts.

On the slowest5% of diagnostic frames, Messages rise from.61-.63ms mean to
2.879/3.169ms, Scene to7.063/6.717, Record to3.662/3.509. Render-owner allocations
average1.752MB/frame, of which Scene.613MB and voxels/billboards.636MB. GC collection
overlap occurs512/509 frames, but only3/7 of the worst16; overlap is not GC pause
causation. The P95 goal also requires controlling message/resource bursts after
the dominant mean costs move. Do not assert a scene rewrite alone meets P95.

### Recommendation and alternatives

Recommend a GPU-resident opaque draw database that replaces CPU per-view
selection/stream publication AND per-part recording for supported NEW and OLD
opaque actors. This is a new architecture proposal for the previously deferred
GPU-indirect path. The subsequent user approval authorizes this complete boundary.

Another NEW count/fill worker, another OLD scatter/direct-reference emitter,
per-call hash/copy/worker-count tuning, or an indirect wrapper around current
packets cannot remove the complete measured9.13ms boundary. Prior experiments
already failed those isolated designs. Parallel recording alone targets at most
part of2.89ms, leaving Scene5.49ms above the entire budget. A CPU-only complete
render graph is a fallback architecture to reassess if GPU capability/precision/
GPU-time gates fail; it is not an automatic additional implementation lane.

### Proposed complete boundary

1. Native immutable model/LOD/color/depth/material definitions share current
geometry allocations. Committed actor inputs, OLD source instance streams,
appearance, hierarchy, visibility and resource generations stay resident.
Managed code supplies committed changes, frame/view inputs, root occlusion
feedback and explicit fallback/exclusion/highlight ownership. It does not replay
every supported representation, material part or selected view slice each frame.
Keep OLD64-byte generic/cube source ranges; do not scatter them into CPU per-view
streams. Stock NEW appearance belongs in instance data, OLD appearance/alpha
retains its float object contract. No resurrection of rejected emitters.

2. GPU preparation selects active views and stock LOD/transition state, builds
NEW view instance/index planes and OLD proxy draw references, and generates the
actual draw counts/arguments. Selectors advance once per frame. Honor installed
NEW vs OLD distance rules, parent-local bounds, scale/global settings, visible
view shifts, alpha techniques and transition encodings. Honor cascade scheduling,
usage/freeze/jump rules, retained image/matrix pairing, spot static/dynamic ownership
and source exclusions. No reduced density, shadow cadence, quality or precision.
Resolve/register required authored LODs before transferring ownership; lazy or
invalid resources keep their explicit safe CPU fallback. No per-frame CPU wait
for GPU culling/readback or reuse of a stale selector result.

3. GPU-addressable actor/draw/material records and a texture descriptor table
replace per-part dynamic object offsets/material binds on this owned path.
Adapt authenticated stock shader inputs to indexed records; preserve original
geometry, shading, rasterizer, alpha and appearance behavior. Record fixed
pipeline/view batches with actual GPU-generated indirect counts, not copied
legacy packet arguments. Optional capability admission must query/enable
multiDrawIndirect, drawIndirectCount, drawIndirectFirstInstance/draw parameters,
required storage/descriptor-indexing features and limits. Double precision or an
otherwise proven identical/conservative policy is required for world-scale
culling/LOD/transform math; silently reducing precision is forbidden. Current
Vulkan1.4 support alone is not admission proof.

4. Owner commits/publication/submission remain serialized. GPU preparation and
indirect/vertex/storage reads receive explicit synchronization2 dependencies.
Keep existing validated command-pool/frame-slot ownership. Fenced per-slot
material tables avoid mutating descriptors still in use; retain resources,
handles and descriptor indices through their last GPU use. Skeletal/deformed,
transparent, unsupported and authored highlight/per-light identity consumers
stay on tested explicit fallback paths until separately supported.

### Owning files and contracts

Native/src/native_pass_scene.h currently reconstructs selected/batch/view streams;
Native/src/native_pass_render.inc uploads streams and records every group;
Native/src/world_render.inc owns preparation, selected draws, cascade jobs and
queue ordering; world_recipes.inc/world_models.inc own fallback immutable state;
world_pipeline.inc/Renderer.cpp own GPU layouts, lifetime and features.
Managed VulkanNativePassScene/VulkanScene/VulkanActorLods and Interop publication
own message commits and explicit fallbacks. ShaderMetadata/SpirvRuntimeModule,
stock material/depth/forward variants own authenticated shader provenance.
Installed DX11 MyPreparePass/MyRenderScheduler remain behavioral references.
Khronos multi-draw sample: https://docs.vulkan.org/samples/latest/samples/performance/multi_draw_indirect/README.html
Indirect count admission: https://docs.vulkan.org/guide/latest/extensions/VK_KHR_draw_indirect_count.html
Descriptor features: https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceDescriptorIndexingFeatures.html
These sources support contracts, not a promised performance gain.

### Approved implementation order and reviewable gates

A. Establish actual hardware/precision admission and freeze the current rollback
build. Complete missing source owner/LOD/range/material audits for the intended
supported set; retain current same-process lighting protocol. Define bounded
GPU capacities and generation/lifetime failures with immediate safe fallback.

B. Implement the complete resident registry -> GPU view/selector -> GPU argument/
instance output -> direct batch recording in an isolated reference fixture.
Verify NEW and OLD ordinary/disabled-skin cube contracts, depth silhouettes,
alpha masks, color/emissivity, transitions, parent visibility, resource/model
replacement, moving casters, camera jumps, highlights/exclusions, both slots,
resize and full shadow/probe cycles. Zero core+sync errors required. Do not
integrate a partial GPU wrapper and call that the architecture.

C. Integrate behind one OFF benchmark switch, with each supported actor bypassing
managed DrawWorld/SubmitPreparedActor and native batch rebuilding. Audits must
prove owner/LOD/triangle/material coverage; command reductions alone are not
acceptance. Re-profile scene/recording/publication, command counts, waits and GPU
work together. Keep only correctness plus reproducible full-frame gains.

D. Ordinary45/30 comparisons in both orders, then three pairs for final mean AND
P95<4.17ms, matched DX11 visuals/gameplay and completed-presentation tails. Added
GPU preparation must fit the GPU budget; a2.40ms rolling baseline does not prove
P95 headroom. If messages or voxels become dominant, profile and propose that
system next rather than tune unrelated helpers.

Engineering budget, NOT an estimate or promise: diagnostic other scopes already
consume about2.407ms mean. A viable design would need Scene near<=1.0ms, recording
<=.6ms and publication<=.15ms to approach4.16ms mean before controlling tails.
Halving current10.82ms gives5.41ms and is insufficient. The target stays OPEN
until the complete acceptance gate passes. No architecture implementation or
normal-gameplay deployment was made in this profiling/proposal pass.

### Approved implementation continuation — 2026-10-12

User approval for the COMPLETE GPU-resident opaque boundary is active. No new
architecture approval is needed to continue stages A-D. CppStudio Governed;
one implementation owner; no additional worker/subagent architecture.
Protected pre-implementation source398files + profile binaries:
TestResults/GpuOpaque-Baseline-2026-10-11. Normal assets/defaults NOT deployed.

Hardware admission implemented in se_gpu_opaque.h/gpu_opaque_admission.h and
Renderer device creation. Actual RTX5070Ti passes multiDrawIndirect,
drawIndirectCount, drawIndirectFirstInstance, shaderDrawParameters, float64,
runtimeDescriptorArray, nonuniform sampled images, compute queue, storage,
texture, per-stage resource, indirect count and workgroup limits. Required
budgets:4096 textures,16 storage descriptors,65536 indirect commands,128MiB per
storage range,64 threads/group. Optional features are enabled ONLY when
SE_VULKAN_GPU_OPAQUE=1 and ALL admission conditions pass. Caps.enabled means
device feature enablement, NOT live scene ownership or performance acceptance.

Native isolated numerical/raster fixture implemented end-to-end:
immutable families/parts/shared colour+depth geometry -> changed resident actors
and current views -> GPU once-per-epoch NEW/OLD selectors -> GPU per-view group
counts/prefix/compact references -> GPU actual indirect args/counts -> direct
fixed batch raster/depth recording. No CPU selected packets, managed actor view
slices, per-view CPU scatter or CPU-built indirect args enter the fixture.
NEW actors group across spatial positions; OLD generic and rigid-cube draws
reference original64B source subranges. Cube orientation follows installed
VertexTransformations.hlsli SHA50AFA1F8; packed3.w!=0 refuses deformation.
Double subtraction precedes float transforms. NEW/OLD distances/transitions use
separate contracts and preserve once-per-epoch selector advancement. Frustum
boundary arithmetic has an outward roundoff envelope; double square root uses
normalized exponent plus double Newton refinement (DXC sqrt(double) otherwise
narrows to float). This is numerical fixture evidence, NOT complete stock
precision/selection/quality admission for all scene owners.

Owner/slot synchronization retained: after slot completion, only changed actor
rows update; descriptor tables are fully populated, fenced per slot, without
update-after-bind. Texture replacement marks both slots dirty; each refreshes
after its own completion. Persistent selectors receive previous-compute write
dependencies; fill->compute, compute->compute, compute->indirect/vertex/host
dependencies are explicit. Depth images retain actual matrix AND camera when
unscheduled; resize retires old depth images at their last submitted timeline.
Failed fixture registry replacement preserves the previous complete epoch;
source/geometry/instance/descriptor/command capacities reject before ownership.
Fixture readback explicitly waits for verification only; there is NO GPU
selection readback/wait in fixture frame preparation or batch recording.

Owning additions: Native/include/se_gpu_opaque*.h,
Native/src/gpu_opaque_admission.h/gpu_opaque_reference.inc,
Assets/Shaders/gpu_opaque_reference*.hlsl/hlsli,
Native/tests/gpu_opaque_admission.cpp/gpu_opaque_reference.cpp.
Canonical native build now includes both targets. Run-RendererValidation adds
GpuOpaqueReference and can use frozen NativeBuildDirectory. The fixture refuses
to claim an unvalidated GPU run; CTest skips it if Khronos validation is absent.
Use the canonical private core+sync runner for actual GPU verification.

Verified frozen native DLL E03674C1C9086AB5FB88FA85384930BBFDCB31B7243D5DB3E4B21A0E0D28DE7A.
TestResults/GpuOpaque-Foundation-2026-10-12 contains NativeBuild/Release, compiled
shaders, source snapshot/manifest, ValidationAssets and validation-evidence.json.
Native13tests pass with private core+sync enabled; actual fixture4523checks pass,
zero validation errors. Checks include stock-helper NEW/OLD transition/LOD
coverage, independent depth membership/pruning, world-scale transforms/frustum,
generic/cube ranges, nonuniform material textures, both slots, generation and
texture replacement, resize, unscheduled image/matrix pairing, duplicate epoch,
and130 compatible actors compacted across multiple workgroups. Existing
NativePass/SceneSubmission/DepthGeometry/ShadowOcclusion/Probe GPU suites also
pass against the frozen private DLL with zero core+sync errors. No game captures
or performance acceptance runs were made on this numerical fixture.

A concurrent guest.inc change introduced a uint32/VkDeviceSize std::max compiler
error during build; its exact version is protected in
TestResults/GpuOpaque-GuestConcurrent-BeforeBuildFix.inc. Only the alignment
expression received a type-correct cast, preserving the guest changes. Another
concurrent native build caused a PDB collision; a subsequent canonical build
completed cleanly. Do not revert other guest/ray work or treat their concurrent
source edits as part of an opaque performance treatment.

STATUS: A hardware/rollback portion done; source owner/range/material audit still
OPEN. B numerical/raster foundation done, full stock reference fixture OPEN.
C live ownership/stock integration NOT implemented. D ordinary performance
and final acceptance NOT run. Goal remains mean AND P95<4.17ms, OPEN.

NEXT authorized work, in order:
- Generate authenticated installed stock shader adaptations for indexed
  actor/object/material records and exact NEW48B-depth/64B-colour planes. Preserve
  OLD float object/alpha/HSV and disabled-skin weighted-cube UV/normal contracts.
  The fixture's simple coloured/texture triangles do NOT prove stock shading,
  alpha masks, tint/emissivity, dither transitions, forward probes or those cubes.
- Complete resident model/LOD/material/source registries using EXISTING shared
  geometry allocations and generation-checked handles; add batched change commits
  and per-pipeline/geometry-page/view GPU batches. The fixture's fixed immutable
  table/one pipeline is NOT live registration or the complete production boundary.
- Carry current main/cascade/spot/probe policies, root occlusion, projected-size
  rules, appearances/visibility, exclusions and explicit fallback/highlight
  ownership. Resolve authored LODs before admission; define exact selector handoff
  for ownership changes. No stale selection, CPU per-view replay or per-frame
  GPU-selection readback. Replace the bounded fixture serial prefix with a
  production-scaled scan before using large model/view registries; measure GPU
  preparation headroom rather than assuming it from the old2.40ms rolling mean.
- Complete stock fixture/image/source-coverage gates, integrate OFF benchmark
  switch, then re-profile complete scene/record/publication/GPU and run ordinary
  matched Pertam45/30 comparisons. Do not benchmark the numerical fixture as a
  gameplay improvement. Default/retention/final three-pair gates unchanged.

DXC DrawIndex builtin contract: https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst

### Stock GPU boundary implementation in progress — 2026-10-12

Continuation request explicitly asks to finish the complete path; approval
remains active. No deployment/performance acceptance/default promotion yet.
GpuOpaqueShaderAdapter derives only stock input/resource addressing on the
immutable compiler preprocessed snapshot. ShaderLibrary captures the catalog's
authenticated definitions and SOURCE hashes. Actual dependencies are checked
against that source table (stock SPIR-V deduplication can put AlphaMasked source
paths in the metadata of a Standard variant). Cache/provenance records identify
the source-transform contract and original module. No material/depth/lighting
program is replaced. NEW48/64B planes, OLD128B object records, original generic/
cube64B source ranges, flat draw identity and nonuniform float4/scalar-alpha
texture tables replace instance attributes/object/material binding replay.

84 selected authenticated NEW/OLD stages compile and spirv-val validate:
all56NEW stages including forward/alpha/cutout, plus28OLD rigid/generic/cube/
weighted-cube and corresponding depth stages. Tests/GpuOpaqueStockShaders and
TestResults/GpuOpaqueStockShaders/results.json contain provenance/layout evidence.
This compile gate is not full quality or performance admission.

Native GPU output now writes stock NEW planes and indexed object/draw records.
GpuOpaqueStock uses shared world mesh allocations and indexed shaders on the
existing owner/slot/command-pool lane. Isolated stock fixture still uses one
immutable mesh/pipeline batch: it is NOT the live multi-model registry. Parallel
hierarchical64-lane group prefix replaces the fixture's serial whole-group scan;
the final serial superblock step is bounded256 entries at maximum admission.
Admission now accounts for18 storage buffers and two4096-entry sampled-image
tables (colour and scalar alpha). Actor fixture ABI304B, part48B; managed typed
interop added. Normal callers do not initialize this fixture.

Tests/GpuOpaqueStockRaster performs actual full renderer colour/depth readbacks.
NEW stock streams and OLD generic/weighted-cube inputs match their original
colour pixels AND depth silhouettes bitwise in both slots;23checks pass with
core/synchronization/GPU-assisted validation requested and zero errors. Weighted
cube source fixture includes stock UV pattern bytes with skinning disabled.
TestResults/GpuOpaque-StockRasterOld2.log is the accepted run. Earlier runs exposed
an invalid UBO usage flag, then an OLD control that used the unbiased shadow
branch. Corrected stock-object buffer usage, matched stock shadow constants and
preserved OLD's actual no-cull cascade pipeline; no quality reduction. Failed GPU
fault run remains in GpuOpaque-StockRaster4.log; never used as a performance run.

Active source additions: GpuOpaqueShaderAdapter.cs, Interop/GpuOpaqueScene.cs,
Native/src/gpu_opaque_stock.inc and stock-plane output in the GPU compute shader.
Current private native assets: TestResults/GpuOpaque-StockValidationAssets.
Stock/native sources changed after the E03674C1 foundation; that older frozen
validation does not cover these changes. Latest canonical managed and native
builds pass; comprehensive updated foundation/fallback suites still required.

STILL OPEN: alpha/cutout/appearance/transition/forward stock raster cases; large
hierarchical-scan fixture; dynamic generation-checked multi-model/source/material
registries, per-pipeline/geometry-page/view batches, live NEW+OLD ownership and
change commits, view/occlusion/scheduling/fallback/handoff contracts, then full
Pertam parity/profile/ordinary comparisons. No live scene ownership or FPS gain
claimed from23 isolated stock checks. Continue these authorized items without
another architecture approval request. Mean AND P95<4.17ms goal remains OPEN.

### Indexed stock batching and committed inputs — 2026-10-12 continuation

User authorized pausing other SE-Vulkan work after concurrent native/managed edits
introduced sevk_init_gbuffer_attachment. No other active Codex SE-Vulkan chat was
found. NativeBackend.cs/guest.inc external timestamps then remained unchanged.
Preserved their changes; rebuilt a matching native DLL. Canonical build script
now accepts -BuildDirectory for isolated non-deploy Windows validation. It reuses
only pinned source dependencies and host tools, with separate projects/PDBs/shader
outputs. Corrected CMake's install validator reference for an explicit host tool.

GPU arguments now occupy one per-view arena partitioned into immutable batch
ranges, with separate actual GPU counts per pipeline/geometry-page/view. Each
part uses page-relative firstIndex/vertexOffset from existing shared world mesh
allocations. Batches retain checked mesh stamps, buffer identities and allocation
offsets. NEW and OLD cannot share a batch input contract. Main/forward double-sided
state and stock depth/spot rasterizers remain explicit. No geometry re-upload or
CPU packet expansion in the mixed stock fixture. Added stage-interface checks
for reflected varying locations/formats/flat interpolation.

Actor/object updates are transactional batches. Mutable object inputs now live
in each fenced slot alongside actor inputs; committed CPU bytes propagate only
after that slot's existing fence. There is no GPU wait or selector readback in
these pose/appearance commits. Immutable topology publication remains a cold
operation. Forward membership is explicit and NEW forward does not replay the
GBuffer transition slices. Actor main/forward exclusions are explicit flags.
Current reference actor ABI304B, part64B, stock batch32B.

Accepted stock raster: TestResults/GpuOpaque-StockCommitImages.log,54 checks,
bitwise NEW/OLD generic/weighted-cube/masked color and depth in both slots,
three mixed pipeline/page batches and repeated moving-caster/key/tint/emissivity
commits. Visible emission output is nontrivial and differs from the prior control.
Accepted stock forward: TestResults/GpuOpaque-StockForwardImages.log, all six near
and far probe faces bitwise in both slots. Optional indexed forward case uses
SE_VULKAN_TEST_GPU_OPAQUE=1 in ProbeGpuTests; ordinary fallback validation keeps
that extra treatment off. GPU-assisted/core/sync requested; zero errors.
Updated foundation8686 checks and all five fallback suites pass with core/sync
validation in TestResults/GpuOpaque-StockViewsValidation.log. Optimized native and
net48/net10 managed builds pass. Native build without the admission environment
skips the reference case; the explicit validated run above actually executes it.

Frozen source/build rollback: TestResults/GpuOpaque-StockBoundary-2026-10-12,
211 source files and source-hashes.json; native DLL SHA256
903A58ABFB83BE13F513C263460D3F397D4F64C4104227848EF5B9CC06DA12EE.
No normal assets deployment, defaults promotion or game benchmark.

NEXT OPEN: resident scene/model/source/material registration, per-material
visibility/depth-boundary GPU handling, root occlusion, retained static-spot
update decisions, generation/lifetime/handoff and live NEW+OLD ownership;
then matched source/image audits, critical-path profile and ordinary Pertam45/30
comparisons. Sparse fixed batch submission is proved on fixtures, not yet wired
to gameplay ownership. Mean AND P95<4.17ms remains OPEN. Continue the already
approved complete path; do not reinterpret fixture quality as performance proof.

### Resident registration boundary and corrected stock proof — 2026-10-12

Canonical native build now includes gpu_opaque_registry checks. Added committed
model/family/source/actor handles, immutable retained source leases, atomic sparse
actor commits, dedicated depth preprocessing, pipeline/page batching and upload
planning. Family admission requires all authored LODs. Model registration records
world geometry allocation identities/generations and original material bindings.
Actor selector generations change for incompatible selection contracts; ordinary
pose/material commits preserve GPU selector ownership. Registry input validation
rejects invalid pose/bounds/material/source contracts before publication.

Native gpu_scene APIs and managed Interop/GpuResidentScene.cs register those
inputs and publish them to GPU selection/emission against existing world mesh
allocations. Shared-geometry initialization does not upload fixture vertices or
indices or create the fixture raster pipelines. Actor updates propagate to fenced
slot inputs without a GPU wait/readback. Topology replacement and explicit selector
handoff are completed-GPU operations. No managed per-view slices or WorldDraw
expansion return from this boundary. Source/family/model/actor retirement APIs
have generation checks; immutable CPU leases remain held by retained actors and
publication snapshots. Full GPU lifetime/admission/fallback integration still OPEN.

GPU material records preserve NEW half values and OLD float values. Visibility
can select whole dedicated solid depth segments or their visible pieces. Root
hidden-view masks keep main visibility independent of caster visibility and
advance selectors before occlusion rejection. Current actor ABI320B, part196B,
stock batch32B, resident actor commit560B, material record32B. Material admission
currently bounded32 records/actor and16 thresholds/family; explicit fallback must
handle out-of-admission inputs rather than silently dropping them.

The first material/root-occlusion image mismatch was a CONTROL error: the fixture
passed appearance arrays while NativePassActor.AppearanceCount stayed0. Corrected
that field. GPU and control then matched both slots. Also discovered the initial
NEW color control produced only black pixels (back-face winding). Earlier NEW
color equality alone was insufficient proof and is superseded. The new fixture
requires nonempty NEW output, tests visible default back-face winding separately,
and matches double-sided color plus depth with the appropriate independent stock
rasterizers. No production rasterizer or quality default was relaxed.

Accepted TestResults/GpuOpaque-ResidentCommitsImages.log:91 stock checks, including
visible NEW default/back-face/double-sided, OLD rigid/generic/cube/weighted-cube,
scalar alpha masks, mixed pipeline/page batches, pose/key/tint/emissivity/material
visibility/root occlusion in both slots, and the new resident registry publication
and pose-commit API against original stock color/depth. Native numerical coverage
8693 checks adds merged/split owner/triangle coverage and occlusion independence.
Five fallback suites pass with zero core/sync errors in
GpuOpaque-ResidentFinalValidation.log. The extra forward run initially failed its
ValidationEnabled guard because Run-RendererValidation restored the private layer
environment before the chained direct invocation; it had no validation errors.
Explicitly enabled private layer rerun GpuOpaque-ResidentForwardDiagnostic.log
passes all near/far faces in both slots with zero core/sync errors.

STILL OPEN (do not declare full path complete): gameplay NEW+OLD actor admission,
source/model/resource change hooks, sparse root-query publication and visible-owner
notifications, packed active-view mapping/capacity sizing, retained spotlight
static/dynamic scheduling without CPU selector replay or same-frame GPU selection
readback, exact ownership handoff/fallback/highlight/exclusion coverage, model and
resource replacement/resize/complete cascade cycles, full source/image parity,
current critical-path profile and matched ordinary Pertam45/30 comparisons.
Normal gameplay remains on its accepted baseline; no deployment/default switch
or new performance claim. Mean AND P95<4.17ms remains OPEN. These production APIs
are a validated boundary, not yet a gameplay-complete renderer.

Latest resident boundary rollback snapshot: TestResults/GpuOpaque-ResidentBoundary-2026-10-12;
source-hashes.json and matching private DLL/shaders. Native DLL SHA256
A0EA02B4D2B2B50F5B7A0EED709F48BEEF1A34AE9F189E3CBFDDB0D945FD7779.
Optimized native and net48/net10 managed builds pass. Shader library8/compiler28
checks pass (their linked unsafe constant-buffer source requires AllowUnsafeBlocks).
The lower boundary remains opt-in/unconnected to gameplay. Continue the OPEN
live ownership/view/shadow/handoff/performance gates listed above; no completion
or deployment claim is made by this snapshot.

### GPU opaque live ownership, spotlight scheduling and handoff closeout — 2026-10-12

CppStudio remains Governed against this canonical work item; the user's override
of the separate planning-harness lookup remains in force. This continuation
connects the previously validated GPU boundary to opt-in production gameplay.

Live NEW and supported OLD actors register complete immutable model/LOD families
against shared geometry, commit actor/source changes, publish packed active views,
and bypass managed representation/packet emission. Unsupported deformation,
skinning, transparent mixed models, highlight identities and light exclusions
retain explicit fallback. Both catalog and authenticated supplementary OLD
color/depth/forward shader families are admitted. Device support is checked before
ownership; conditional rendering is required by the full scene path. Default
normal gameplay remains unchanged (SE_VULKAN_GPU_OPAQUE_SCENE is opt-in).

Sparse root queries carry original view identities, translated to packed GPU
membership masks without returning actor slices. Hidden-parent groups with no
visible members issue no queries. Main owner notifications honor SkipInMainView.
Selectors are imported only at completed ownership/topology handoffs. Prior
pipeline ownership is retained when model/pipeline classification changes; runtime
GPU-to-CPU and CPU-to-GPU handoff preserves selectors without deleting newly
admitted actors during CPU pass teardown.

Static and dynamic spotlight membership/LOD/depth revision signatures now live on
the GPU. Static redraw predicates suppress unchanged static preparation/scatter
and raster work. Dynamic predicates include static invalidation; a conditional
fullscreen exact depth Load/write restores the frozen static image only when the
final image changes, followed by the current dynamic overlay. No unconditional
image transfer or CPU selector replay is needed. Frozen layout state is explicit
across GPU/fallback handoffs and resize. Descriptors are updated only in the fenced
current slot; frozen images use the renderer's completion-based retirement policy.
ABI: view296B (original identity/light appended); actor320B, part196B, stock batch32B,
resident commit560B unchanged. Compute storage descriptor requirement19.

Verification: GpuOpaque-LiveCompletionValidation.log contains45 production-message
checks: packet bypass, hierarchy/appearance/parent visibility, highlights,
model replacement, runtime GPU/CPU image parity, NEW/OLD pipeline changes,
SkipInMainView, resize, generic source replacement and fallback/reentry image
parity, both slots and teardown. GpuOpaque-ConditionalSpotValidation.log contains
123 stock raster checks, including changing/retained static and dynamic casters,
hidden removal/reentry, retained-image fallback handoff, reordered root view IDs,
independent main/caster occlusion and selector handoff. All have zero core/sync
errors. Six Run-RendererValidation suites pass, including8693 numerical GPU
reference checks and existing native/scene/depth/shadow/probe image checks. Native
CTest passes13 enabled tests; its optional GPU reference is separately exercised
by the validation runner. Managed net48/net10 and benchmark harness builds pass.

Frozen source/binary/shader rollback anchor:
TestResults/GpuOpaque-LiveBoundary-2026-10-12 (source-hashes.json).
Managed SHA256 C95F142C906FD8ABF2CE2B1D1DFDBED7BDF0DAB1C9C30EAD3EFDED9C513E6646.
Native SHA256 C558AC28066E36E279E1E72FB462AC4DD24C245EB033103703468F5577F3E281.
Private launcher supports pinned BenchmarkAssets as well as managed/native DLLs;
no validation assets or new shader files were deployed to normal gameplay.

Pertam admission/visual/performance gates remain OPEN. A private ordinary45/30
smoke comparison has started with the matched unpaused camera and the pinned
artifacts above. Ownership counts are captured once in benchmark telemetry, not
repeatedly scanned in the measured renderer. This is not acceptance evidence yet.
Complete matched DX11 per-view coverage/visual audits, full cascade/light update
cycles and three paired ordinary runs are still required. Source replacement
currently uses completed topology publication; frequently changing instance
sources need critical-path measurement before claiming their update cost is solved.
Mean AND P95 below4.17ms remains OPEN; no speedup or full renderer acceptance is
claimed from synthetic/live fixture tests alone.

Pertam smoke follow-up: the initial validation asset folder lacked packaged game
supplements (capture packaging failure, no rendering/timing result). The full
GameAssets private overlay corrected that. Actual game startup then exposed
publication inside BeginUploads/EndUploads. MyVulkanRender now completes the
upload batch after the last scene/lighting resource consumer and before GPU
publication; the outer finally still closes batches on exceptional/early exits.
GpuSceneLiveTests now exercises this outer upload/publication boundary and passes45
checks with zero core/sync errors. Managed net48/net10 builds pass.

The next real startup diagnosed capacity:1833 actors,321 families,12315 parts,
64 reserved views,7210 source instances,543884 references,29288 draws,314 batches,
338 textures. The fixture part/batch caps (8192/256) rejected that valid topology.
Raised them to32767/4096 within the admitted128MiB storage range and the guaranteed
linear Vulkan dispatch bound (at64 views,32767 parts produce65534 groups of64).
Shader and CPU buffer/index layouts are unchanged. The large numerical GPU test
now exercises12315 parts across320 batches and64 views:54495 checks, zero core/sync
errors. A first test assertion incorrectly treated batch-major counts as view-major;
corrected the test indexing, with no production layout change. Stock123 checks
still pass against the new admission caps. No failed startup is performance evidence.

Current pinned continuation: managed in GpuOpaque-LiveBoundary2-2026-10-12 (delta
snapshot, parent LiveBoundary); native in GpuOpaque-PertamCapacity2, with native hash
and changed source files. Complete GameAssets stay in the parent anchor, with the
new native supplied by BenchmarkNative. Ordinary Pertam retry is still pending;
no baseline promotion, realized gain or below4.17ms acceptance is claimed.

Ordinary live GPU smoke completed in Vulkan-4f8529fb3b8e474b8a3e063981f16547:
204.295696ms mean,262.7217ms P95,204.160196ms completed-present interval mean.
GPU rolling scope3.302017ms. REJECTED as performance acceptance. Current CPU profile
Vulkan-a001ffdb6d6045c9ae26c6dc1bdfaf0b found one complete topology publication per
sampled frame:PassPublish142.613932ms,PassPrepare142.664416ms (inclusive). Managed
publication137.819158ms; these are attribution scopes, not ordinary timings.

Corrected dependency invalidation: unused immutable resource registration and
external handle retirement do not change the topology reachable from retained
actors. Added registry tests for those exact boundaries. Whole model/LOD families
and shader classes are preflighted before any GPU model registration, preventing
partial late-LOD failures from repeatedly registering resources. Native current
family replacement now recalculates current output capacity instead of reusing
old topology capacities. Appearance-only override entries do not enter immutable
geometry-family identities. A private retry revealed the old-capacity defect;
that failed run has no timing result and was stopped unsaved.

GpuOpaque-CurrentTopologyLiveValidation.log:47 live checks (including material
appearance retaining its immutable family). GpuOpaque-CurrentTopologyStockValidation.log:
126 stock checks (including larger existing-actor family replacement matching
color and caster images). Zero core/sync errors. Native/managed builds pass.
The ordinary current-topology run Vulkan-cc416b0b3eb5451f88f6066844af07b8 completed:
62.994356ms mean,94.3198ms P95,63.101507ms completed-present interval mean;
rolling GPU3.282450ms. It remains REJECTED for performance acceptance and cannot
be called a gain over the accepted renderer. The path owns1507 NEW and361 OLD
actors with4818 explicit fallback actors at the sampled scene point. No default
or normal asset deployment occurred.

Current profile instrumentation separates actor-add/remove, family, source and
root-group causes of topology changes, plus successful publications and frames.
Counters are host-only, with no GPU wait/readback. Existing inclusive native
PassPrepare/PassPublish and managed publication scopes remain available. The next
private instrumented run determines the remaining structural publication cause.
The four requested integrations have exercised correctness proof, but gameplay
performance acceptance and full matched DX11 parity remain OPEN; do not mark the
complete GPU path accepted from these fixture results. Goal mean AND P95<4.17ms
remains OPEN.

### Requested integration closeout and current gate — 2026-10-12

Live gameplay ownership, GPU-retained static/dynamic spotlight predicates, packed
root-query integration and explicit GPU/CPU ownership handoffs are implemented.
Final correctness fixtures:47 live checks,126 stock raster/shadow/query checks,
54495 large GPU numerical checks (12315 parts/320 batches/64 views), zero core/sync
errors. Native and managed builds pass. Real private Pertam validation in
Vulkan-b1c7299256ec42acb19586b020c281df completes with validation enabled, errors0,
device-lost0, across startup, gameplay and complete sampling. This supersedes the
previous fixture-only proof. Normal defaults/assets remain on the accepted control.

The deeper profile found two additional integration failures. GPU frame timestamps
started after resident dispatches; corrected them to bracket resident preparation
through final frame commands. The previous ~3.2ms GPU scope omitted that work.
Per-stage asynchronous prior-slot timestamp queries now expose selector, membership,
scan, scatter and argument costs without same-frame selector readback or a new wait.
The prototype required CPU-cached host memory for every working buffer, and actual
resident preparation cost~35ms GPU. Required mapped device-local allocation failed
at large topology due to the device's host-visible VRAM mapping budget. The final
benchmark device-local switch uses mapped device-local small inputs and unmapped
device-local scratch/outputs. Explicit verification uses staged readback; gameplay
directly consumes the GPU buffers. Allocation and frame-slot ownership remain on
existing validated owners; no new worker architecture was introduced.

Real device-local startup initially lost the device. Core validation identified
GBuffer-only batches bound in a reflection-probe forward pass even with zero
indirect count. Non-forward batches now carry UINT32_MAX and are skipped before
binding. Added a registry exclusion check. A remaining real-scene descriptor-pool
threading error identified GPU cascade recording allocating from the owner's shared
WorldSlot pool; world_set allocation is now externally synchronized. No errors
remain in the final real-scene validation. Those failed runs are retained as
diagnostics and carry no acceptance/performance claim.

GPU selector advancement remains one thread/actor once/frame. Membership counting
and scatter use actor/view jobs rather than serially traversing every view in each
actor thread. Numerical owner/LOD/range coverage and images remain identical in
fixtures. Final stage profile in the real validated scene: selection0.439ms,
membership0.379ms, scatter0.406ms; all resident stages total~1.287ms. This is an
instrumented attribution result, not a full-frame acceptance result.

Matched ordinary45s warmup/30s sampling, same final managed/native binaries and
shader bundle, source hash, 1080p, VSync off/cap1000, graphics/shadow controls:
- GPU scene + device-local (Vulkan-8c2626e912e0472ab71605d2a7fbbc9e): mean13.051786ms,
  P9517.896ms; completed render/present cadence13.240014ms mean; rolling full GPU4.295616ms.
- GPU scene off control (Vulkan-5ceb716e7d924b38b9cbad9fda44264f): mean10.379793ms,
  P9514.1399ms; completed present intervals10.462476ms mean/P9513.8583ms;
  rolling full GPU2.563847ms.
This single treatment/control pair FAILS the performance retention gate. Do not
promote gameplay defaults, claim a gain over the accepted renderer, or call the
complete GPU path accepted. GPU remains an experimental benchmark switch. The
requested integrations' correctness gate is closed; final rendering/performance
acceptance and goal mean AND P95 below4.17ms remain OPEN. Three final pairs and
matched DX11 coverage/visual/gameplay gates have not passed.

Current coherent artifact anchor:TestResults/GpuOpaque-ViewJobsArtifacts contains
matching managed/native DLLs and the updated full GameAssets shader bundle. Reuse
this tuple for further diagnostics. Preserve the accepted control as rollback.

Next structural proposal (not implemented in this closeout): restore stock native
CPU pass ownership for actors outside GPU admission instead of routing the entire
fallback population through managed legacy emission when GPU mode is active.
The current sample owns1507 NEW +361 OLD actors, with4818 fallback actors. Audit
fallback reasons and exact DX11 pipeline identities first. Unify CPU/GPU root-query
publication and record both disjoint ownership sets while advancing each selector
once. This requires explicit split-consumer integration/parity proof, not a cache
or hash tweak. Separately profile native spotlight/command recording: GPU treatment
rolling native record3.855ms/spot1.702ms versus control2.163ms/spot0.368ms, and world
packets1027 versus597. Quantify active/zero-count batch commands before proposing
further command architecture. Current preparation-to-emission correctness is
proved; whole-renderer CPU parity and <4.17ms remain the objective.

### Approved GPU path improvements — 2026-10-12

User authorized (1) conservative view batch recording, (2) native CPU fallback
co-ownership, and (3) selected-LOD/pass-role GPU preparation, followed by matched
re-tests against pinned ViewJobs control. CppStudio Governed state; one owner.
Anchor: TestResults/GpuOpaque-ThreeChanges-Anchor; existing pinned control assets
remain unchanged. Each treatment has a separate benchmark switch. No gameplay
promotion. Main risks: false-negative caster batch pruning, double selectors or
premature root-query closure, missing OLD object or NEW instance data. Gate with
numerical and stock raster/shadow/live ownership tests, zero core/sync errors,
then ordinary 45s warmup/30s private Pertam sampling with control in both orders.
Completion for this work item means tested implementations and honest comparison;
the overall <4.17ms mean/P95 objective remains open unless its full gate passes.

### Approved three GPU improvements: implementation and matched re-test closeout

Implemented behind separate benchmark-only switches:
SE_VULKAN_GPU_OPAQUE_VIEW_BATCHES builds immutable family/role batch lists once
and current conservative view candidates on the render owner. Recording skips
provably empty batches and redundant bound state. It does not read GPU counts
back or replay GPU LOD selection. Frustum rounding is outward conservative;
appearance, projected-size and distance pruning remain on the GPU. Recorded
spotlight commands fell from about4547 to943; these are issued recording
commands, not a claim about physical executed draw counts.

SE_VULKAN_GPU_OPAQUE_HYBRID retains the native CPU pass for non-GPU-owned eligible
actors. GPU and CPU selectors own disjoint actor sets. Root visibility and
shadow exclusions finalize once after both publications; GPU removals dirty the
CPU handoff. The matched scene uses1868 GPU actors (1507 NEW/361 OLD),3329 native
CPU actors and1489 managed fallbacks. Counts describe the loaded scene, not the
visible cost distribution. Explicit skeletal/deformed/highlight/exclusion and
unsupported-technique fallbacks remain.

SE_VULKAN_GPU_OPAQUE_ROLE_STREAMS uses an immutable internal per-family/LOD/role
index buffer, preserving authored part identities and the public family/part
ABI. NEW depth writes transforms, NEW colour/forward writes packed instance data,
and OLD references write object data. Selection still advances once per actor
per frame. The optional path admits20 storage descriptors; both slots retain
existing fenced resource lifetimes and command-pool ownership. The old traversal
is retained when the treatment switch is off.

Verification: isolated native CTest13 enabled tests pass (the optional GPU test
is exercised separately). Numerical coverage tests pass54888 checks with role
streams OFF and ON, including GPU-selected draws remaining in conservative
recording lists. Stock raster/shadow/root/retained-light tests pass126 checks;
live ownership passes57, including simultaneous GPU/CPU groups and both ownership
directions. Native pass, scene submission, dedicated depth, shadow occlusion and
probe regression suites pass. Exact native-control image comparison passes;
legacy fallback comparison differs only by15 channels of1/255, with equal alpha.

Eight ordinary unpaused private Pertam captures use identical1920x1080, render
fraction1, VSync off, cap1000,45s warmup and30s sampling. Fixed-camera/environment
verification passes every run, inputSHA5435D8C321FC5A8AABEFF5FDC764641DEC672BF9632EBEF77CB7F7D6117D2594.
The original ViewJobs control bundle is unchanged (plugin5531C3DD...; native23085418...).
Comparison evidence: TestResults/GpuOpaque-ThreeChanges-Comparison.json.

- Control-1:10.578084ms mean /14.3905ms P95.
- Prior GPU path:13.199750ms /17.8092ms.
- View batches only:11.395145ms /16.0893ms.
- View batches plus hybrid:11.201959ms /15.9486ms.
- All three, first:10.871654ms /15.3264ms.
- Control-2:10.434186ms /14.2105ms.
- All three, repeat:10.942369ms /15.2032ms.
- Control-3:10.323528ms /13.9626ms.

Both ordering directions are represented. Full GPU treatment improves about17.4%
in mean over prior GPU, but remains slower than control in mean and P95. This is
an intermediate repeated comparison, not the goal's final three-pair acceptance.
Completed-present intervals agree with render cadence; no early-return win.
Rolling full GPU scope is approximately3.4ms, versus2.6ms control; it is not GPU
P95 or full-frame acceptance. No normal gameplay defaults or deployments changed.

Two final diagnostic captures are excluded from ordinary comparison. The first,
Vulkan-04639aa80c9046d0ae6f27238875e560, requested validation but the process lacked
its layer search path (available0/enabled0); it is not validation evidence. Its
instrumented preparation profile is useful: scene5.54ms and native record2.54ms
inclusive owner scopes; GPU selection/count/scatter stages total about0.795ms,
versus prior about1.28ms. Nested CPU rows must not be added.

The private launcher now supplies the repository Khronos layer path and core/sync
settings when Validation is requested, and labels validation/timestamp captures
instrumented. Re-run Vulkan-387c3a37a1194052a391698a261d7772 confirms validation
available1/enabled1/errors0 and deviceLost0 over379 sampled gameplay frames.
Its heavy validation timings are diagnostic only, never benchmark gains.

The admission audit identifies the next structural constraint:3148 native CPU
actors and1454 managed actors exceed the fixed31-material-override admission
limit. There are no native CPU actors satisfying GPU predicates without a
recorded resource rejection. This is not a lost dirty-update/retry issue.
A subsequent proposal should audit relevant authored material overrides across
all LODs and consider sparse/variable resident appearance storage to lift that
ceiling, while preserving unsupported geometry and fallback consumers. Do not
infer visible savings from loaded counts or replace quality with admission.

Current coherent treatment: TestResults/GpuOpaque-ThreeChangesArtifacts; original
rollback/control: TestResults/GpuOpaque-ViewJobsArtifacts; source pre-edit anchor:
TestResults/GpuOpaque-ThreeChanges-Anchor. The requested implementations and their
re-test are complete. PERFORMANCE GATE FAILED: mainline control remains faster;
mean AND P95<4.17ms remains OPEN. No further architecture is implemented here.

### Approved sparse GPU appearance work item

User authorized lifting the material-override admission ceiling and matched
re-tests against the unchanged ViewJobs control. CppStudio Governed; single owner.
Replace fixed1152-byte actor object/appearance slabs with144-byte object/header
records plus sorted variable appearance spans in the same storage binding.
Version appearance separately from pose; resize/update only the completed frame
slot and preserve the existing host-write/compute-read dependency. Preserve public
actor/family/part ABI, default-material precedence and per-material visibility.
Managed resident inputs retain overrides relevant to every authored family LOD;
unbound dictionary entries remain available for resource changes and fallbacks.
Register/commit/handoff tests must cover >32 genuinely referenced appearances,
growth/shrink/removal, late-LOD materials, both slots and OLD/NEW stock raster.
Risks: missing an authored override, stale span after growth, default overriding a
specific material, and double advancement when admission changes. Gate with core
and synchronization validation, pinned-camera ordinary45s/30s comparisons in both
orders. Sparse admission is a benchmark switch; no normal gameplay promotion.
Anchor: TestResults/GpuOpaque-SparseAppearance-Anchor. Prior experimental treatment
and control bundles remain unchanged. <4.17ms mean/P95 remains the open goal.

### Sparse GPU appearance closeout — 2026-10-12

Status: implementation and matched re-test complete; PERFORMANCE GATE FAILED.
The ViewJobs control remains the accepted rollback baseline. Sparse admission is
opt-in through SE_VULKAN_GPU_OPAQUE_SPARSE_APPEARANCE; no gameplay promotion or
deployment was performed. The overall mean AND P95 <4.17ms goal remains OPEN.

Implemented sparse model-relevant appearance spans across every authored LOD,
including OLD float appearance, NEW packed appearance, default-material
precedence, and dedicated depth coverage. Pose commits preserve appearance
through the documented UINT32_MAX sentinel. Native actor and appearance
revisions are separate. Both fenced slots grow their appearance buffer and
rewrite its descriptor only after that slot completes; immutable geometry is
still shared. Public structure sizes and queue/command-pool ownership remain.
The CPU source arena retains spare span capacity until a topology replacement;
its 128MiB admission ceiling remains an explicit experimental limitation.

The first expanded capture (Vulkan-e9faeb0f82f74758b7cde821157960d1) failed
admission and has no valid timing result. Its requirements exceeded the former
part and whole-view argument limits. The corrected path admits up to65535 parts,
checks per-batch indirect counts against the device limit, checks full argument
storage against the128MiB range, and uses guarded2D dispatch for large linear
work. View allocation reserves all15 cascade/probe/main roles and both roles
for current spotlights, growing on new selected views; selected work and quality
are preserved. Numerical tests cover dispatch above65535 workgroups.

Verification: both managed targets build cleanly. Native CTest13 enabled tests
pass; the optional GPU reference test is run explicitly. Numerical coverage:
136700 checks; stock color/depth:198 checks; live ownership:64 checks. Core and
synchronization validation report zero errors. Existing native pass, scene,
depth, shadow occlusion and probe regression evidence is retained. Exact
stock comparisons cover genuinely referenced >32-material tables, late LODs,
growth/shrink/removal, pose-only preservation, ownership handoff and both slots.
These focused comparisons do not constitute a full DX11 scene parity gate.

Ordinary evidence: TestResults/GpuOpaque-SparseAppearance-Comparison.json.
All eight valid captures pass Verify-BenchmarkFrame.py: identical fixed camera
and environment, source frame SHA5435D8C321FC5A8AABEFF5FDC764641DEC672BF9632EBEF77CB7F7D6117D2594,
1920x1080, render fraction1, VSync off, cap1000,45s warmup and30s unpaused samples.
Three adjacent GPU/control pairs include both ordering directions.

- Fresh Control-1:10.542748ms mean /14.3918ms P95.
- Prior three-change GPU:10.615524ms /14.5305ms.
- Sparse GPU-1:15.452519ms /20.0502ms; adjacent Control-2:10.994713ms /15.1486ms.
- Sparse GPU-2:14.252887ms /18.5368ms; adjacent Control-3:11.007097ms /15.6513ms.
- Sparse GPU-3:14.841477ms /19.0063ms; adjacent Control-4:10.808464ms /15.5039ms.

Completed queue-present intervals agree with frame cadence and have no lost
samples. More GPU ownership is not a realized gain: each treatment reaches6094
GPU actors,339 native CPU actors and253 managed fallbacks.162046 raw overrides
reduce to10930 relevant resident entries (maximum8 for an actor in this scene).

Pinned ordinary treatment: TestResults/GpuOpaque-SparseAppearanceArtifacts.
Plugin SHA65E968B086659D97ED2CA8D9CF5B63001C50E010764C978CABC572414DCD6978;
native SHA9D9189D28817294C7AA8E2845DDCBCC40188332608B5FF49DB61F17BFC7E867F;
compute SPV SHA44CF1F1DF31BA9AC3FEB14E426A7A0293ACC75593EE60F34B224AC327DC35014.
The original control hashes still match5531C3DD... /23085418....

Diagnostic-only scopes now separate GPU frame inputs, topology publication,
resident queue, root queries and visibility publication. The profile-only
plugin is pinned separately in TestResults/GpuOpaque-SparseAppearance-ProfileArtifacts;
it does not alter the ordinary treatment bundle. CPU attribution capture
Vulkan-bf78d7e18d3b4a4b8ae2734d151ff44b has1939 accepted frames with zero dropped
tail samples, validation OFF, CPU detail/native critical/GPU timestamps ON.
These inclusive instrumented scopes are not ordinary performance claims:
GPU resident queue3.424ms, root queries0.131ms, visibility publication0.045ms,
native record2.961ms. Scene8.395ms includes publication and must not be added to
those nested scopes. Frame CPU14.67ms versus wall15.03ms and queue CPU cycles
show substantial active CPU work. Native critical sampling is every eighth
frame and does not capture every rare publication hitch.

The five worst frames spend630.59–647.85ms in GpuResidentQueue. Their total
3200.1012ms contributes1.650ms to its mean; excluding those five, the queue still
averages1.778ms. The same window records five family changes and five full
publications, with zero actor adds/removes or source/group changes. The call
path is Registry::update_actors -> topology increment -> queue_gpu_scene_frame
-> publish_gpu_scene -> complete registry upload, new reference buffers/pipeline
and stock batch publication. Matching counts and this path strongly implicate
global topology replacement; per-event timestamps or an isolated intervention
are still required for definitive attribution within that publication.

Completed compute timestamps identify selection1.547ms of2.170ms total compute;
membership0.218ms and scatter0.274ms follow. The final rolling world GPU scope
is5.065ms, excluding uploads/final composition/HUD/present, and is not GPU P95.
Removing CPU hitches alone cannot establish the4.17ms goal or a control win.

Separate full-scene validation capture Vulkan-57d5dc28e791447f8c61dc54dba3b27a
completes358 sampled gameplay frames with validation available1/enabled1,
errors0 and deviceLost0. Its83.73ms mean is validation overhead, excluded from
ordinary comparisons. TestResults/GpuOpaque-SparseAppearance-Diagnostic.json
and -CpuProfile.json keep the two diagnostic lanes separate.

Next structural proposal (not implemented or approved by this closeout): make
the resident GPU tables stable across actor family/source/root rebinding.
Publish immutable family/LOD/role geometry once with generation-checked indices;
apply actor and appearance deltas into retained slot storage. Family changes
within admitted capacity must retain unrelated buffers, batch identities,
selectors and depth validity. Grow storage only at an actual capacity boundary
and retire old allocations after their GPU use. Attribute snapshot construction,
allocation/pipeline work, and ordinary delta commits separately before claiming
a saving. Preserve selector advancement and explicit fallback/handoff contracts.
After that CPU boundary is measured, propose a compact active-view GPU membership
path if selection remains dominant; another global packet/indirect wrapper or
hash/copy/worker tuning is outside this proposal. Both changes need renewed
matched comparisons, full-scene visual/coverage gates and zero validation errors.

### Approved GPU resident stall and regression work — 2026-10-12

User authorizes fixing the publication stalls and GPU-path regression. CppStudio
Governed; one implementation owner. Anchor: TestResults/GpuOpaque-ResidentDelta-Anchor;
failed sparse treatment and ViewJobs control remain pinned. Measure all native
publication calls (strided profiling misses rare hitches), including range
construction, allocation, pipeline construction, stock publication and delta
input handling. Replace scene-wide work with resident table/delta work while
preserving geometry, appearances, selector advancement and current view quality.
Generation checks, output-capacity proof, frame-slot fences and retirement stay
explicit. Test family/source/root rebinds and unsupported capacity transitions,
both slots and OLD/NEW depth. Benchmark one treatment at a time with the existing
fixed Pertam camera and 45s/30s windows, retaining control unchanged. Re-profile
remaining dominant costs before further structural fixes; <4.17ms remains open.

Measured continuation: the first treatment reaches12.254ms/17.376ms against
control10.924ms/15.325ms. Queue attribution falls3.36 ->0.62ms, but five new
texture-binding families still cause cold publications. Extend the same approved
fix with geometry-compatible, exclusive family-slot rebinding: stage part binding
metadata per completed frame slot, reuse unreferenced texture indices, retain
geometry/ranges/role lists/selectors, and reject shared-family or layout changes
to the explicit cold path. Keep NEW object defaults at registration; OLD object
appearance changes own their defaults while the GPU writes current pose rows.
Validate unique shared geometry leases once per frame rather than per color/depth
consumer. Separate benchmark switch: GPU_OPAQUE_OWNED_INPUTS. Risks: modifying a
family still used by another actor, rewriting an in-flight part/descriptor table,
or retaining stale appearance. Require exact stock images/depth in both slots,
shared-family rejection, palette/appearance/pose changes and zero sync errors.

OwnedInputs diagnostic closeout (not accepted): ordinary first pair control
11.251ms/15.466ms; GPU11.496ms/17.037ms. All five family changes retain tables,
zero publications in sampling. Instrumented repeat2449frames: queue0.514ms,
commits1.047ms, stock geometry0.118ms, recording membership0.637ms;
selection GPU1.618ms. No accepted full-frame/control win. Full-scene image
inspection exposes large dark triangles and changed entrance-wall shading,
absent in ResidentDelta. Unit/reference checks alone missed this; reject
OwnedInputs visuals/timing as acceptance evidence. Isolate retained binding
metadata versus managed object defaults before additional performance work.
Frozen artifacts preserve both; diagnostic OBJECT_DEFAULTS=0 disables only
the latter. BindingOnly first launch omitted native OwnedInputs, was aborted
before sampling and recorded as excluded; corrected launch explicitly sets it.

BindingOnly isolation removes the large black triangles when OLD object inputs
retain their canonical camera-relative pose bytes. Preserve that byte contract
while retaining reflected appearance preparation; write current rows into the
resident OLD input. It is not safe to assume every downstream consumer replaces
those rows. BindingOnly mean15.039ms/P9523.197ms is an unpaired diagnostic, not a
control comparison. Its entrance-wall lighting differs too: paused matched
lighting/selection proof remains required before any promotion.

Continue approved regression repair at measured GPU selection (1.618ms): prepare
actor/view membership in parallel, using the same double-precision member rules
once per pair; reduce to actor main/any visibility, advance its selector once,
then resolve view LODs. Reuse the existing slot selection storage as temporary
membership, with a compute dependency before selector resolution. No quality,
LOD, coverage, matrix, ownership or workers change. Benchmark switch
GPU_OPAQUE_PARALLEL_SELECTION. Keep the legacy dispatch selectable. Timestamp
selection includes membership and resolution. Require numerical/raster parity,
once-per-epoch selectors, occlusion and retained spot tests, both slots, zero
validation and full-scene comparisons before retained performance claims.

ParallelSelection is rejected: measured completed GPU selection1.883ms versus
OwnedInputs1.618ms; the first matched ordinary pair also loses13.196/21.688ms
against12.072/18.346ms. Remove its extra dispatch and harness integration;
keep frozen artifacts for evidence. SPIR-V confirms the legacy membership
branch is skipped, so no speculative compiler-branch fix. Instead evaluate
single actor-owned view masks: one unchanged member(ignore_occlusion) evaluation
per actor/view, derive visible membership from that result plus hidden bits,
and reuse a local64-bit mask for LOD publication. This directly removes the
three repeated FP64 view traversals without a second global selection pass,
new buffers or workers. Separate GPU_OPAQUE_VIEW_MASKS switch; same oracle,
selector, lighting and ordinary performance gates. Still no accepted control win.

### Pause checkpoint requested by user — 2026-10-12

ResidentDeltas plus exclusive OwnedInputs rebinding remove the measured global
publication stalls. The latest ordinary Pertam treatment handles five family
changes with zero full publications. Instrumented resident queue attribution
falls from3.36ms before this work to0.457ms; the latter capture contains2730
frames. This is a measured scope improvement, not a full-frame acceptance claim.
OLD object inputs now preserve canonical camera-relative pose rows, fixing the
large dark triangles exposed by the actual scene. ParallelSelection's extra
dispatch is removed after its measured regression. ViewMasks remains a private,
default-off experiment; its selection1.572ms versus OwnedInputs1.618ms is too
small and insufficiently isolated to claim a structural control win.

Latest ordinary matched pair,45s warmup/30s unpaused sampling:
control Vulkan-4c3f65f55f6b49fe8c8d0eaee9f2d2af:10.730462ms mean/14.534ms P95;
GPU Vulkan-0fa60d54d924463192875451859a5de1:11.062250ms/16.1044ms.
Both pass exact renderer-camera/environment/source-hash verification. Completed
queue-present intervals have no lost samples: control10.820674ms/14.326ms P95,
GPU11.171413ms/16.0819ms P95. The remaining regression is0.332ms mean and1.570ms
P95 in this pair. Earlier sparse treatment's roughly4ms excess is substantially
reduced, but neither cross-run subtraction nor one pair establishes acceptance.
Evidence: TestResults/GpuOpaque-ViewMasks-Comparison.json. Pinned treatment DLLs
and shader are in GpuOpaque-ViewMasksArtifacts; ViewJobs control stays unchanged.

Focused current checks:136700 reference,302 stock raster/depth,77 live ownership,
13 enabled native tests, managed net48/net10 builds; zero validation errors in
the focused GPU checks. The paused same-process lighting comparison has exact
probe images and cascade depths across control/GPU/control-return; main image
still differs at approximately0.103% of RGB pixels and needs localized review.
This lighting proof precedes ViewMasks and is not a complete DX11 parity gate.
Final full-scene validation Vulkan-f7f1605288934c58bb999034e79bc5d3 completes392
sampled gameplay frames with core/synchronization validation available1,
enabled1, errors0, deviceLost0. Camera/environment mismatches0; same source SHA.
Its76.533ms mean is validation overhead, excluded from performance comparison.
The captured scene has no recurrence of the large dark triangles. This is a
correctness checkpoint, not completion of the full visual/coverage gate.

Current inclusive CPU profile: scene preparation4.838ms and native recording
2.510ms; scene includes native commits0.922ms, actor traversal1.077ms and resident
queue0.457ms. Completed GPU selection1.572ms of2.132ms scene compute remains
dominant there. Rolling world GPU4.777ms excludes final composition/HUD/present
and is not full GPU P95. Slowest-frame message work averages5.264ms; attribution
of its spikes is still required. Do not sum nested scopes or infer GC causation
from coincident telemetry alone.

Resume options require a measured structural proposal before implementation:
resident pass-owned preparation/recording that removes duplicate CPU view work
and recurring driver setup; committed actor input ownership that avoids scene
reconstruction while preserving the OLD pose contract; conservative camera-
relative GPU broad-phase selection with exact boundary fallback, if justified
by kernel/coverage evidence. These are candidate upgrade paths, not guaranteed
savings. No additional architecture is started at this user-requested pause.
The GPU treatment is not promoted; mean and P95 below4.17ms, final scene parity,
and three ordinary pairs in both orders remain outstanding.

Deployment correction: an earlier managed build unintentionally invoked the
project's automatic DeployPlugin target. At this checkpoint, Interim is restored
to the exact pinned ViewJobs managed DLL; both Pulsar editions receive its asset
bundle with the explicit pinned native override used by the comparison harness.
Legacy is a clean net48 rebuild of the pinned Rendering source plus the current
non-rendering shell, not a byte-identical original DLL restoration. All copied
experimental DLL/PDB/metadata files were backed up before this correction.
Provenance/hashes: TestResults/GpuOpaque-PauseControlRestore/restore.json.
No experimental switch is enabled for normal gameplay. Work is paused at this
checkpoint by explicit user request; no further capture or implementation runs.
