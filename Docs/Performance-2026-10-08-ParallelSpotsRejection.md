# Spotlight scheduling experiment rejected

Verified ETW attributed 0.875 ms inclusive to spot-shadow generation, and the
current ordinary rolling profile measured about0.82 ms. The prototype moved
complete per-light caster selection and static/dynamic depth recording into
four jobs, each owning a fenced slot/light command pool. The owner retained
image allocation, copies, barriers, deterministic execution and counters.

216 postprocess checks and113 scene checks passed with zero validation errors;
seven native tests and both managed Release builds passed. Exact serial/parallel
pixels covered two lights, static/dynamic rebuilds, frozen overlays across slots,
exclusions, light removal, movement and resolution changes. Native telemetry
confirmed four workers and roughly1840–1937 recorded spot commands/frame.

Ordinary full1080p, same-binary, same-source private saved-camera pairs reject it:

- ab192ed993b8423c827aba411132aaba: serial13.736585 ms, parallel13.899990 ms;
  added0.163405 ms (1.19%).
- e77e5981c290410aaab2558697384260: parallel13.946301 ms, serial14.147452 ms;
  saved0.201151 ms (1.42%).

Only spotlight scheduling changed inside each pair. Unpaused scene drift remains
a limitation. Opposite signs do not establish a useful repeatable frame gain.
The internal driver or job overhead responsible is not established.

Production, harness and test edits were restored byte-for-byte from the relative
path/hash anchor. Archive TestResults/ParallelSpots-Rejected-2026-10-08.zip:
61BC5DBC65CE598F439FE4E415A66789B71065D9D9088E6E043095E142959896.
The summary helper and measured artifacts remain as evidence. Do not tune this
prototype further; retain the previous async-presentation gain and probe fixes.

Next capture uses current accepted policy and the existing detailed CPU scopes.
Rank current new-view planning, emission, prepared replay, legacy actor paths
and native queue before choosing a change in scene/packet ownership. Stock
MyGeometryRenderer/MyPreparePass owns per-view instance/LOD groups and prepares
their instance streams before render jobs. Vulkan currently re-enters DrawWorld
and SubmitPreparedActor for each selected representation, then aggregates model
and part packets. That is the mapped architectural boundary to investigate;
moving an isolated copy, eligibility predicate or existing job onto more workers
does not test removal of this repeated frontend/publication work.

The full <4.17 ms target and visual parity remain open. No FPS gain is claimed
for this rejected experiment or the probe correction.
