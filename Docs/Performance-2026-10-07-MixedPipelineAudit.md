# Mixed DX11 pipeline audit and policy correction — 2026-10-07

Owning-render-job capture corrects the previous geometry diagnosis. Paused same-source
private1080p scene has Vulkan5,892,864 opaque scene indexed
invocations. DX11 old path596,418 plus GeometryStage2 new path5,125,884 totals
5,722,302; ratio1.02981, not41.22 or~10.
305 new main LOD groups and962 selected new-pipeline actors were captured. The
previous post-frame query.Size/active-view filter ran after render cleanup, so it
reported empty new/static groups without proving them empty. Its old-only counts
were real but incomplete. Withdraw conclusions of an all-old scene and huge excess
geometry. Historical raw files preserved; geometry summary now requires owning-job
NewGeometryViews and refuses older incomplete captures.

Native logical submissions are observed before rigid batching and tagged by actor.
Actual instance/index totals agree exactly with physical packets. Grouped canonical
model/debug-name/rounded pose comparisons merge duplicate cube-cell owners and are
diagnostic, not exact persistent identity or equal-visibility proof. Independent
paused loads still differ slightly in camera/settled physics. Both renderer paths
now have actor/part/instance/LOD metadata, and the initial missing actors were largely
new-pipeline objects, not proven candidates for removal. New-path draw count uses
actual render-pass InstancesCount times preprocessed GBuffer part indices, verified
against MyGBufferRenderPass.DoWork source.

Code corrections retained

Stock MyInstancing.UpdateCube sets EnabledSkinning from any enabled cube. Only such
non-generic buffers receive0.25 distance multiplier in MyRenderableComponent.
Native formerly applied0.25 to every cube buffer. It now tracks the aggregate flag
on updates, clears it on generic conversion/replacement, and applies full distance
to ordinary rigid cube buffers. A real actor fixture selects different authored
levels for rigid versus skinned cubes at the same distance. No new LOD threshold.

Experimental old20-pixel size and far-cascade rules are now limited to actors
positively identified as old pipeline: ForceOldPipeline, legacy instance-buffer
protocol, or character creation. Unknown/new actors retain geometry and all fitted
caster membership. These policies remain defaultoff. Unknown model-loader fallback
is conservative until CPU eligibility classification matches stock. Stock
CreateRenderEntity chooses MyInstanceComponent for valid new models without
ForceOldPipeline and old MyRenderableComponent otherwise; character/buffer paths
are old. Neither old size culling nor old four-near-cascade trimming is a universal
new-pipeline policy. Current actor LOD selection still does not implement complete
new per-view LOD ownership, so remaining parity work is explicit.

Refreshed ordinary baseline after corrections: frame12.045167ms, completed
interval12.067123ms, GPU window4.308453ms; 2487 accepted
full1920x1080 unpaused frames, profiling off. Source LOD/group/size/far policies and
parallel/recipe modes explicitly enabled. This is experimental-policy private
saved-camera performance, not normal default or original screenshot equivalence.
The~10.55ms earlier experimental baseline predates these scope corrections and
cannot represent fully restored coverage. Native recipe paired~0.23ms incremental
result remains historical same-binary evidence under its documented old scope;
no new matched effect size asserted. <4.17ms and complete DX11 pixels are unmet.

Validation and incident record

93 scene checks pass with zero core/synchronization validation errors. Includes
neutral logical tracing, owner/instance totals with no phantom first-batch records,
actual skinning LOD selection, and preserving new candidates against old size/far
rules. Native recipes and shader semantics unchanged. One classification audit
failed because post-frame main-query lookup found no active query; stopped exact
owned PID/path after confirmed render exception, logs preserved. Main frustum now
copied on the owning render job. Guard briefly saw the terminating process; PID
absence verified before relaunch. Corrected captures exited normally. No timeouts
or observation failures triggered restarts. Private save exits unsaved.

Evidence

Vulkan8946ee... logical capture; DX3ef62f... old-component registry; DX7147ab... both
owning old/new render passes. geometry-contract-comparison.json, Selection-
InitialComparison.json (old-only, superseded), Selection-ExtraClassification.json
(old-component-only, superseded), and corrected baseline Vulkan12b7ee... . Earlier
geometry/LOD/size/far documents have correction notices. Full source save hash
remainsD5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.

Next macro work

Classify stock new-model eligibility without creating DX11 resources, then prepare
immutable actor/model/instance state once with per-view representation ownership.
New path uses translation/scale distance and pass-specific LOD shifts; old uses
closest AABB distance and the skinning-only quarter multiplier. Restore depth/
forward policies under the correct path before benchmarking caster work. Source
DX11 prepares groups and render work through its scheduler; native C# still emits
per-part packets serially. Use the revised complete workload oracle and actual CPU
profiling for whole preparation/packet reuse or owned workers. Do not resurrect
byte-fill workers, whole ordered-schema caching, or culling based on old-only counts.
Lighting/floor/LCD/exposure/particles parity is still open.
