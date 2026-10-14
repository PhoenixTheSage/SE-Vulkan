# Probe correction closeout

The probe work is complete and retained as a quality correction, with no FPS
gain claimed. Native capture now follows the stock initial/zero-timeout complete
cycle and positive-timeout 20-stage cycle, without the former five-second hold.
Face vertex constants receive view times projection; pixel depth linearization
retains pure projection. Face-owned fenced constants and depth attachments avoid
overwrite when six faces record together. Main forward ambient and DistanceFade
match the audited stock values (0.3 and 3000).

Canonical native tests: 7/7. Probe GPU: 300 checks, zero validation errors.
Scene GPU: 113 checks, zero validation errors. Managed Release builds pass for
net48 and net10. Face isolation produces geometry only in the intended forward
face; complete publication and timed/zero-timeout behavior are covered.

Private paused capture Vulkan-53f7a62339744bab9294f1edd44ab452 confirms stock probe
settings: distance130, timeout0.3, resolution512, ambient0.3. Floor and walls are
visually closer to DX11; full pixel parity remains unproved. LCD and other
lighting differences remain outside this closed work item.

Ordinary private saved-camera 1920x1080 run
Vulkan-bd4d1b6b362e441eb615b99d2d564432: 2124 frames, mean14.102706 ms,
median13.106 ms, p95 19.1525 ms, consecutive present interval14.137850 ms
(70.73 calls/s). GPU rolling mean3.888646 ms. Native rolling probe phase0.277152
ms. Async presentation, retained submission/recipes, new actor views, model
batching and the existing experimental view policies were enabled. This is a
current cost observation, not a causal comparison against the prior13.13 ms:
probe cadence and rendered face coverage changed. The original human screenshot
camera has not been reproduced exactly.

Product hashes: managed2DEFB46588678CFA52729582550F14E5208627A048E151E6A53DCDA9DF59517D,
native5C880C03E7A748AC7DE0794CACCAE537525BBD659D7FCDD682C161673247B12B.
Source checkpoint remains D5EA52B3E5C2810B3634C8813BE2C90795143C496151A3FD390176833FA2AA92.

Return to the profiled scene frontend6.284 ms and native recording3.731 ms.
Spot recording/culling is0.875 ms inclusive in the verified ETW capture and
0.821439 ms in the current rolling ordinary profile. Its full per-light caster
selection and command recording remain serial, while cascade recording already
overlaps that serial work. A bounded opt-in parallel spotlight preparation and
secondary-recording experiment is the next native hypothesis. Owner-thread image
allocation, static-map copy/overlay barriers, execution ordering and statistics
must remain intact. No shadow coverage or quality reduction is authorized.
