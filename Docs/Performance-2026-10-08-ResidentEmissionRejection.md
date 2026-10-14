# Resident representation producer rejected

The prototype compiles opaque material/model bindings on input revision changes
and emits current view records directly to the accepted bulk model path. Camera,
visibility, LOD, cascade and spotlight decisions remain current. Transitions,
unsupported representations and identity consumers use the existing path.

307 scene GPU checks and 471 world GPU checks passed with zero validation errors.
Coverage includes exact pixels and ordered logical owner/LOD/part selections,
actor and camera movement, current transition fallback, appearance, per-material
tint/emission/visibility, model replacement, highlights and backend reclamation.
Both managed Release builds passed. Native code and shaders were unchanged.

Ordinary full1080p private Pertam comparisons reject this design:

- d55a89fb: prepared13.461054 ms, resident13.348304 ms;
  saved0.112750 ms (0.84%).
- a3c1c685: resident14.190027 ms, prepared13.616768 ms;
  added0.573259 ms (4.21%).

Both pairs use identical product/source hashes and workload policy, with only
the producer toggled. Native telemetry confirms2289 directly emitted views/frame
and2683 eligible actors; compiling averaged about0.06 representations/frame in
the first pair. The path was active and warm, but did not give a useful repeatable
full-frame gain. Unpaused scene drift remains a limitation. Do not tune this
producer further or interpret passing pixel checks as performance acceptance.

All protected files were restored byte-for-byte, the added source removed, and
source timestamps advanced before rebuilding to prevent incremental stale DLLs.
The rebuilt managed hash is the acceptedC7F6595D1B2B55FD8B2C6DD97640BBE9FC454A31E9C9CB267E0C5ED11A251281;
native asset remains3478C4B327469A762DD19CF7A095D2804F35E3462AD75EF67F66AB1CA524EDB9.
Archive ResidentEmission-Rejected-2026-10-08.zip:
36ECF6A336308B17FA92F06DA79375681B7B1888B53952B0E4960CFC07C2376C.
Measured comparisons and the summary helper remain as historical evidence.

Retain the accepted bulk gain of0.74–1.20 ms and earlier async/probe corrections.
The next evidence lane is a fresh warmed CPU stack/context-switch capture of that
accepted policy, with a verified optimized native DLL/PDB pair. The broad scope
profile remains inclusive; it cannot assign the traversal remainder precisely.
This investigation must guide removal of whole actor/view preparation rather
than another prepared-binding layer, worker rescue, hash, clock or byte-copy pass.
Full frame below4.17 ms and complete DX11 visual parity remain unachieved.
