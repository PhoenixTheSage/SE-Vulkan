# Retained view plan experiment — rejected, 2026-10-08

Ordinary paired1920x1080 copied-Pertam comparison: rebuilt10.910970ms,
retained10.943573ms; no gain (0.032602ms regression).
Plan hits2679.094 and misses3.906 per frame, ~99.85% reuse.
High reuse did not remove enough total work to justify the additional structure.
No reverse-order gain claim. Do not tune this into another micro-optimization loop.

The prototype reused merged representation plans/resolved handles after current
LOD/transition/main/probe/cast/spot/masks/settings checks. It passed116scene checks
including exactpixels/currentsettings/spotremoval/modelreplacement, but actual
frame time failed. It has been removed, exact pre-edit files restored. Current
scene113checks clean and Release net48/net10builds pass. Authored basis, recipe,
new-view and whole-model changes remain intact; source save untouched.

Archive ViewPlan-Rejected-2026-10-07.zip, relative paths+manifest, SHA256
08B584738955E86F8DE56B9CD9384200B4D5F0C175E47EB0F094534412F66074.
Evidence ViewPlans-Rejected.json; captured Vulkan798c2ee... . Exact input policy
is in the launch and result. Profiling disabled, other experimental modes fixed.
GPU windows independent. No visual parity or <4.17ms total-frame success claim.

Next work must change renderer ownership/scheduling or persistent scene production,
rather than cache merged-plan records. Current scene/traversal profile dominates
active CPU; byte-fill workers, object hashes and ordered-frame schema cache are
already rejected. Whole preparation must keep GPU/native uploads and descriptor
slot changes owned, merge deterministically, and handle live mutations/views.
The full goal remains active.
