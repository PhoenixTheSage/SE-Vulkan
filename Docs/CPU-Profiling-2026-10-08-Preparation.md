# Current renderer stack capture

The rejected retained view-plan prototype remains removed. The next product
change is gated on current stack evidence, rather than a further local cache.
The full frame target below 4.17 ms and DX11 visual parity remain open.

Managed diagnostic run Vulkan-28217b4f8ce1465fa9da88647e523c48 uses the current
Release plugin/native DLL, private Pertam checkpoint, saved camera, 1920x1080,
retained submission/recipes, new actor views, whole-model batching, parallel
cascades/queue, and the existing experimental old-path selection policies.
It is instrumented and is not an ordinary performance comparison.

The 10-second dotnet-trace capture contains one render thread, TID 10904.
Sampled thread time includes waits and opaque native calls. Scene.Draw accounts
for 3557.58 ms, QueueWorld for 860.17 ms and voxel frontend for 434.86 ms.
Present's boundary caller accounts for 4252.31 ms. These are sampled thread-time
attributions, not exclusive active CPU or proved native functions. Runtime poll
samples account for 2651.25 ms and cannot be equated with garbage collection.

Runtime events distinguish 88 GC suspensions (reason 1), totaling 182.56 ms with
a maximum 5.29 ms, from 5427 other suspensions (reason 0), totaling 733.63 ms.
The trace contains no AllocationTick samples with this provider configuration;
zero allocation samples is an instrumentation gap, not zero allocation.

There are 511 Begin markers but no captured End markers. The event summary uses
the first-to-last Begin interval (10000.71 ms) and explicitly reports zero
complete frame spans and a null marked-frame mean. No per-frame stack-cost claim
is made. The replacement completion marker writes a long sequence and long
microsecond count through a typed EventSource overload. A real dotnet-trace
capture of the focused emitter records 200 Begin and 200 Completed events;
the in-process listener separately verifies IDs, values and payload types.
The precise cause of the old double-payload event's absence is not established.

Capture-ManagedPreparation.ps1 now waits automatically for the warmed sampling
marker and validates the owned private PID/executable. DeepCpuAnalysis supports
--eventpipe; Summarize-Managed.py adds non-overlapping top-level buckets.
Neither helper interprets sampled thread-time pseudo leaves as CPU utilization.

Native CPU diagnostic run Vulkan-887f33399c7d423e9394b9a2d339c06a uses Nsight
Systems 2026.5.1 and a 10-second warmed CPU-only sample. Correction: its copied
PDB was stale. The production DLL has no CodeView signature, so the earlier
statement that this pair matched was incorrect. Native function names cannot
be inferred from this capture.
The source checkpoint hash remains
D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.
The 30-second instrumented harness mean is 14.230832 ms; GPU mean is 3.914372 ms
and native presentation mean is 0.217895 ms. These are diagnostic observations,
not ordinary FPS acceptance or a before/after gain. Native symbol export is
pending; an empty placeholder report is not accepted capture evidence.

Next decision: resolve the native Present boundary before choosing between full
actor preparation ownership/scheduling and native command-generation changes.
Backend packet arenas, model batches and recipe registration are owner-thread
state. Moving the existing DrawWorld calls directly into concurrent jobs would
violate that ownership. A scheduling change must move complete useful work into
isolated outputs and preserve deterministic current-view/identity semantics;
the prior byte-fill-only worker experiment remains rejected.

ETW run Vulkan-15045d4ef11d45eb94dedc14b93dc18f completed with 482 complete frames,
966 frame markers and zero lost events. Active CPU sample weights per complete
frame are scene frontend 7.170 ms, bridge/native queue 1.861 ms, voxel frontend
1.073 ms, and other renderer/engine 6.332 ms. Scene's nested new actor-view path
is 3.463 ms and DrawWorld 2.680 ms; nested values overlap. Its marked mean is
21.161 ms, with scheduling-derived active CPU 16.426 ms and blocked time
4.780 ms. These are instrumented observations, not current ordinary FPS.
Native leaf module attribution is available, but its function symbols were
rejected because the copied PDB did not match. No unsafe PDB override is used.

The symbol contract now requires a DLL CodeView record and matching native PDB
GUID/age. Focused positive/negative checks accept the new pair and reject both
the no-signature production DLL and a deliberately mismatched old PDB. Canonical
Release -ProfileSymbols -Test passed all seven native tests. Profile linking
explicitly retains /OPT:REF /OPT:ICF alongside /DEBUG:FULL /INCREMENTAL:NO;
compilation retains Release optimization with /Zi /Zo. Production asset DLL
hash remains EFDBFE35D5519E708826A56DF5E934EB3FBA4E34D66C5A38C3DB760370DC9ADF.
The dedicated profile pair is DLL F7DF707D181CD5BB4609D011F4CFCF8379310F0CDD5DB3BB03BD21EAB1A0F040,
PDB E6F2169EAD6B197B59D71428F75026055A98B53F565CA569084D60876EC1FC11,
GUID eec0f6c8-e976-4c94-a99b-57adef905a42, age 3.

Nsight's capture wrapper timed out at 15 minutes, while its exporter remained
live and reported generating final report. That timeout is not process completion
and does not authorize a restart. The independent verified-pair ETW run is
Vulkan-56877a4081bd4e1ea256a5c6ceaa8022; collection is pending.

Verified-pair ETW capture 56877a40 is complete: 550 complete frames, 1102 markers,
zero lost events. Its PDB resolved all 905 sampled native addresses to 168
distinct symbols, with zero unmatched addresses. The marked mean is 18.463 ms;
scheduling-derived active CPU is 13.949 ms/frame and blocked time 4.579 ms/frame.
Non-overlapping active CPU sample weights per frame: scene 6.284 ms, native
record/submit/present 3.731 ms, bridge/native queue 1.467 ms, voxel frontend
0.758 ms, other renderer/engine 1.685 ms. Native render_world is 3.182 ms inclusive,
spot-shadow generation 0.875 ms, and queue_world_recipes 0.542 ms; these nested
native values cannot be added to the top-level buckets.

Named managed self samples still identify DrawNewActorViews (0.869 ms/frame),
TryModelBatch (0.565 ms), and SubmitPreparedActor (0.384 ms). Nested view emission,
old-path drawing, ancestor visibility and spatial work add to the complete scene
unit; none of these self values alone is the whole bottleneck. The architectural
comparison is stock MyRenderScheduler's dependent per-view cull/prepare/render
jobs, versus Vulkan's serial actor preparation and shared backend publication.
Next product work must isolate complete useful preparation outputs before owner
merge, not revive rejected byte-copy workers or the retained view-plan cache.

Presentation backpressure is separately verified in this instrumented capture:
native PresentUs averages 4699.80 us; scheduling stacks have end_frame as the
blocking boundary and graphics-memory-manager readied stacks. GPU averages
3.777 ms. The internal driver cause is unproved and should not be attributed to
browser load by association. Async presentation remains an existing opt-in with
small prior gains under low wait; a high-wait comparison would be a separate
macro scheduling experiment, not proof that this profile already improved FPS.
No ordinary gain is claimed between the two ETW runs: different profile binary
and instrumentation/independent unpaused state prohibit a causal comparison.

Nsight export subsequently completed (40.6 MB native report). NVIDIA's supported
report-context/thread_scheduling/callstack_summary tools loaded it after installing
their report dependencies in the workspace. The selected render thread has
9848.319 ms on-CPU upper-bound time, 100% transition-confirmed; its blocked bound
is 155.336 ms but only 26.8% confirmed. This capture's low presentation wait differs
from the later high-wait ordinary pairs and cannot establish the internal cause.
All 9866 periodic CPU samples remain unresolved at symbol level; module inclusion
reports are overlapping stack coverage, not exclusive function time. Native
function attribution therefore comes from the verified-pair ETW capture, rather
than inferring symbols from the Nsight report. Evidence files are
NsightPrepared-Context.json, NsightPrepared-Scheduling.json and
NsightPrepared-Callstacks.json. Nsight's earlier wrapper timeout did not terminate
its exporter; waiting preserved this completed report.
