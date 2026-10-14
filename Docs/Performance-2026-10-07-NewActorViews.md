# New-path per-view actor ownership — retained opt-in, 2026-10-07

SE_VULKAN_NEW_ACTOR_VIEWS=1 enables the new-path representation planner. Default
off until broader saved-scene/motion parity checks are completed. Old-path actors
remain on their existing route; static/skinned/invalid model ownership retains
fallback. No authored distance, shadow count or rendering resolution is reduced.
Full <4.17ms + DX11 visual parity remains unmet.

Ordinary full1080p private Pertam both-order comparisons, same binaries/source:

Vulkan-eb786d4f700a4c9caa7585e949df7ee9: frame12.166982 to11.572748ms, saved0.594235ms (4.88%). GPU windows4.311146 to3.710662ms. 2683new actors/2401representations, 8176.4current cascade memberships.

Vulkan-4eb23b0cce5b4360b3f6eed04f4d0518: frame12.096284 to11.461721ms, saved0.634563ms (5.25%). GPU windows4.347991 to3.675434ms. 2683new actors/2401representations, 8170.7current cascade memberships.

Fixed recipe/parallel submission and experimental LOD/group/old-only size/far
flags, profiling/async/failed view gate off. These compare this change to the
corrected covered baseline, not normal-default renderer performance or the exact
original screenshot camera. GPU independent windows support phase context,
not scanout FPS. Actual frame remains11.46–11.57ms (~87fps); GPU below4.17ms alone
is not success.

Eligibility and ownership

CPU-only MWM checks mirror stock MyModel suitability: no bone streams, complete
normal/tangent/binormal/UV stream lengths, supported techniques and no facing,
including all authored assets. Metadata importer previously requested Bitangents,
while stock consumes Binormals: initial classifier falsely rejected every model.
Adding/using the stock Binormals tag corrected eligibility; rendering tangent
construction was intentionally left unchanged for a separate parity audit.
Shelf accepted, skinned astronaut rejected. Private88fc617... matches905 actual
DX11 new main actors as eligible,57unmatched independent selection,0conflicts.
No renderer GPU/DX11 resources are created for eligibility. Unknown/error fallback
is conservative; model loading/dummy/minimum-loaded-LOD policy still needs wider
verification against stock factory behavior.

New state uses camera-relative translation length divided by world forward scale,
stock FOV/resolution coefficient and global distance settings. Old uses AABB
closest distance and skinning-only cube quarter multiplier. State includes stock
hysteresis/dead-zone/min/max transition rates, explicit selection and reset on
model replacement/camera discontinuity. Main transitions keep both representations;
depth/forward use current only, as stock prepare-pass policies specify.
Pass LOD shift-visible/min/clamping applies separately to GBuffer, each cascade,
single-depth spot and forward probe. Same LOD/alpha merges consumers, preserving
fitted cascade masks. Spot eligibility uses the exact conservative native sphere/
cone predicate, so representations with no spot consumer are omitted. Spot LOD
changes dirty both static/dynamic maps.

Packet0x02000000 skips spot depth independently of cascades. Current main/probe
exclusion flags and cascade masks route each planned representation. Flag flows
through prepared/legacy batch keys, compact recipe and native spot filters; ABI
comment added. No queue, descriptor lifetime or GPU synchronization change.
Highlights keep an independent authored-base packet with8192 normal-view exclusion;
normal view highlight suppression does not suppress shadow-exclusion ownership.
Model change resets new state/view camera/spot representation. No object-byte
hashing, whole ordered-schema cache or rejected byte-fill workers introduced.

Validation and limits

Native canonical Release/Test/Deploy passed all7CTest. Final scene suite109checks,
zero core/synchronization errors. Includes actual main pixel equality, separate
main/cascade/forward packets, independent spotlight depth draw17commands and masks,
base highlight identity, model replacement and fallback; CPU hysteresis/transition
state and eligibility checks. The spot fixture initially configured an empty
cascade settings array, so it correctly fell back to single-depth and combined
representations; distinct settings now establish four independent view records.
One-frame counted spot commands matter because subsequent static depth can cache.
All failures/logs preserved; no performance claims drawn from fixture timings.
Both net48/net10 Release builds pass. Bench code predates only model-change reset
addition; steady unchanged-model branches are the same. Source save unchanged.

Still required: saved-scene pixel checks for multi-view/transition/highlight
coverage, source factory classification over broader supported/unsupported assets,
new/old material technique treatment and explicit fade/visibility state parity.
Opaque fixtures do not prove glass/shield/alpha behavior or full DX11 parity.
Rendering tangent basis/binormal integration, lighting/floor/LCD/exposure/particles
remain open. Keep opt-in while these are audited. CurrentCPU11.5ms still dominates
budget. Next macro owner is entire producer/packet preparation and owned scheduling
under the corrected mixed-pipeline oracle; source per-view output improves GPU
work but does not remove serial C# traversal/packing. Re-profile the complete
retained path before selecting the next structural change.

Evidence: NewViews-Comparisons.json, NewEligibility-Classification.json,
NewActorViews-SceneChecks.txt (initial fixture output; final result.json current),
SceneSubmissionGpuTests/result.json. SourceMyModel/MyMwmData/MyGeometryRenderer/
MyPreparePass/MyDepthPreparePass1/MyForwardPreparePass1 are the installed primary
ownership/policy references. Existing user/Grok work and prior corrected mixed
pipeline captures preserved. Canonical plan remains the durable work authority.
