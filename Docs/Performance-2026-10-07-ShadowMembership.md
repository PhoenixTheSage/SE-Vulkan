# Cascade membership correction

Vulkan formerly assigned cascade bits from camera-space depth slices and lateral
tests, while the GPU consumes fitted light-space orthographic projections. It also
fit shadows after scene/voxel submission, making those bits depend on the previous
frame. These predicates are not interchangeable for offscreen shadow casters.

Private instrumented snapshot9569aa85117e4b9094a4f20a84dbc6dc compared6686 model
bounds:2451 had fitted light memberships absent from the camera-slice mask; one
had an extra camera membership. Includes non-casters/invisible actors, so this is
predicate disagreement rather than a count of rendered lost shadows or a speedup.

CascadeMask now tests the same fitted world-space clip volumes used to form GPU
matrices. Fit executes before scene/voxel packet production, and scene draw reach
is updated from that current fit. Frozen cascades keep their world-space volume
as camera-relative uniforms change; disabled/invalid shadows expose zero active
masks. No arbitrary camera epsilon or quality reduction was introduced.

The scene suite passes59 checks with zero core/synchronization validation errors.
New cases prove a caster behind the camera remains in a light frustum, edge/
outside/spanning boxes match exact fitted volumes, current camera replaces old
membership, frozen volume remains fixed and disable clears stale bits. Fixture
uses an explicit render setting rather than the platform-dependent static default.

Corrected instrumented snapshot475eabc2d35e4140b22188e3b649ab5b reports zero missed
or extra fitted memberships across6686 model bounds. Source checkpoint remains
D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.
The captured image was inspected: floor/gloss, dark lighting, overexposed LCD and
particle differences from DX11 remain. This fixes caster eligibility, not complete
visual parity. GPU timestamp4.38ms in this diagnostic exceeds the target; correct
caster coverage costs more than the earlier incomplete mask. Do not present that
diagnostic's CPU/FPS as ordinary performance or claim an optimization speedup.

Evidence lives in both runs' shadow-membership-audit.json/geometry-workload.json
and the scene-shadow-membership-validation.log. No-view preparation elimination
must use current valid main/probe/spot/cascade membership before reducing actor
work. Stock per-view query membership still differs from the broad native camera/
distance candidate union. The complete <4.17ms and parity goal remains active.

Ordinary corrected control c6e0880c34f94899a3855aaf5c6fab24 completed with
experimental LOD/group/size fixed on: 16.2596ms mean draw-through-present,
15.7853ms median, 61.35 rendered FPS. Renderer ten-second Frame16.1516ms,
GPU4.3945ms; Present call5.3173ms. Diagnostics off, full1080p, source/build
recorded. This is the current corrected-path observation, not a matched causal
speedup or complete visual-parity claim. GPU exceeds4.17ms as does CPU.
