# Isolated Creative fixture

This test-only plugin launches the stock empty-space Creative scenario, creates
a powered large platform, small grid, astronaut, point/spot lights, gravity,
LCD, construction stage, door, RenderDirect turbine and dithered preview, then
captures, walks the fixture astronaut, deletes the small grid, saves and unloads
it. It is not deployed with Vulkan.

From the repository in PowerShell, after the Release plugin/native build:

```powershell
./Tests/CreativeFixtureHarness/Run-Isolated.ps1 `
  -Bin64 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' `
  -Pulsar 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Pulsar' `
  -ObserveD3D11
```

The script refuses to run beside an existing game/Pulsar process. It copies
Pulsar, plugins and a profile into a fresh GUID folder under
`TestResults/CreativeFixture`, with separately marked game user data. Cloud
storage is disabled only there. Both renderers use a windowed 640×480 baseline;
`launch.json` records the plugin/native hashes. The script returns the child PID
immediately so its reports and bounded lifetime can be monitored.

After a successful Vulkan run, use the absolute path recorded in
`Reports/fixture-initial-save-path.txt` for the matching DX11 run:

```powershell
./Tests/CreativeFixtureHarness/Run-Isolated.ps1 `
  -Bin64 '<installed Bin64>' -Pulsar '<installed Pulsar>' `
  -Backend Dx11 -LoadFixtureWorld '<marked initial fixture snapshot>' `
  -ObserveD3D11
```

The initial snapshot is taken before the timed exercise and copied into new
private user data for comparison. The source snapshot is never loaded in place.
No ordinary player save can be selected: the marker, world name, path containment
and sandbox file are checked before loading. Snapshot creation rejects linked
files/directories.

The timed exercise also submits overlapping standard, additive-bottom and
additive-top billboards through `MyTransparentGeometry`, changes point/spot
colors and ranges with stock lighting-block setters, and checks actual Display
Cancel/OK in the active world. The committed alternate renderer applies only to
the marked private profile and requires a restart; the current world stays ready.

Review overview, LCD text, LCD sprite/clipping, animation, walking astronaut and player-HUD PNGs
alongside `fixture.xml`, `status-latest.txt`, the game log and observer report.
The timed harness records `VisualAcceptance="PendingReview"`; producing a PNG
does not establish correct rendering. DX11 uses stock Pulsar's force-kill exit,
whereas healthy Vulkan shutdown should dispose and remove its session journal.

Optional `-RenderDocDirectory 'C:/Program Files/RenderDoc'` enables the installed
capture layer only in the private child process. The harness binds its published
API 1.4 table, suppresses the overlay, and requests world/LCD frames in `Reports`.
Both `.rdc` deliveries are required for that mode to pass. This mode does not
change normal plugin deployment or global Vulkan layer settings.

## Asteroid extension

Add `-Asteroids -DurationSeconds 360` to the isolated command. The script rebuilds both managed renderer targets before copying the Legacy DLL, preventing stale cross-target deployment. The fixture creates a 128-voxel procedural asteroid (seed 43191), captures near/middle/far views, applies a real spherical storage cut, saves and reloads the mined `.vx2` into a new render actor, captures again and unloads. Use the same switches with `-Backend Dx11 -LoadFixtureWorld '<marked initial snapshot>'` for comparison. Only marked private user data is modified.

Inspect `asteroid-evidence.txt`, `asteroid-mining.txt`, `asteroid-storage-reloaded.txt` and all `asteroid-*.png` captures. The camera uses the sun-facing direction; EmptyWorld's undefined sun is assigned a finite direction only inside the private fixture. Actual game clipmap readiness, render counters and resource release supplement the GPU suite. Stored voxel reload is not a full-world restart test. Full parity and timed crossfade acceptance remain pending.

Camera changes following near/middle/far captures are delayed by half a second so asynchronous screenshot requests can consume the intended view. Historical captures predating this refinement may show the following view; use their status/camera history for LOD activity rather than treating those PNG labels as exact camera evidence. Before/after/reload views remain stationary for their captures.

## Anomaly producer parity mode

Add `-AnomalyParity -DurationSeconds 480` to the isolated command. This builds and snapshots the actual sibling Anomaly core/companion, overlays the current Release native DLL only in the private copy, requests occupancy, point shadows/player depth and lit mips, and registers a controlled shared medium. `launch.json` records the Anomaly core hash; Start-Prepared verifies it. Bundled assets copy through bounded parallel robocopy without deletion or mirroring.

`anomaly-parity.txt` and its history record current availability, dimensions, near-interval callbacks and character/light geometry status. Readiness gates fixed-camera `anomaly-volume-off.png` and `anomaly-volume-on.png`; producing those PNGs is not visual acceptance. The fixture requires all four producers and capture delivery before writing `anomaly-producers-passed.txt`. Use the marked initial snapshot for the matching DX11 command with the same switch. `--interior-only` on Tests/GuestDepthGpuTests runs ten actual-core topology-demand lifecycle checks without opening a GPU context.

The first Vulkan attempt at `TestResults/CreativeFixture/1d8ec3f48464406298ca45bd3999b5de` loaded a real world and proved occupancy, lit mips, 24,949 copied skinned character triangles, 12 point-shadow faces and player depth. It failed the volume gate. Inspection found its saved EmptyWorld disabled oxygen and pressurization, so physical grids had no gas service; a second run with the same settings also could not activate volumes. Both are retained failure evidence, not producer-parity acceptance. Parity mode now enables both settings before creating a new world or loading its private copy; `FixtureBaseline/anomaly-parity-settings.txt` records the copied-world change. The source snapshot and player configuration remain unchanged. The shared snapshot excludes previews/render-only grids, keeps pending physical grids incomplete, bounds selected physical grids, retains the engine ForcePressurize request while tracking, explicitly starts initial topology generation, and restores prior demand on release. The corrected live rerun remains required; do not treat unavailable gas services as empty complete topology.

The third run (`7955ef23f804487c955bef12d2603d2d`) exposed a preparation error: setting only `Sandbox.sbc` did not change the loaded world. Installed `MyLocalCache.LoadCheckpoint` replaces checkpoint settings with `Sandbox_config.sbc` when present. Preparation now updates both XML files in the private copy, and their values must be checked before launch. The run was stopped after its status explicitly reported oxygen=False/pressurization=False; it is failure evidence, not another producer algorithm failure.

Run `31f275d05c3d4781aacbc2ebbc0852a6` reached 295 shared-volume commits and concurrent readiness of all products with actual room topology. At the fixed overview camera it exposed incorrect native projection-scale metadata: QueueWorld supplies a combined view-projection matrix, whose diagonal changes sign under rotation. The bridge now reads the pure projection from the stock frame buffer. Retain the run's history/off PNG as failure evidence; its missing on capture means the fixture did not pass. The strict GPU lane includes this rotated overview direction before the next live run.

Corrected Vulkan run `241403d2adea45149545b157f21c225c` wrote `anomaly-producers-passed.txt` and delivered both fixed-camera PNGs after re-enable recovery. All products were available together; the viewed captures show the controlled medium versus the restored starfield. Its SDK validation remained enabled with zero reported errors. The overall fixture subsequently failed the powered-door cycle (maximum open ratio 0), so do not label it a complete simulation fixture pass. The strict projection regression lane passes 273 checks with validation enabled and zero errors.

Current parity mode also assigns the same finite sun direction on both backends and creates a temporary ventless armor shell. `anomaly-interior-history.txt`/`anomaly-interior-passed.txt` require agreement between engine gas rooms and the active Anomaly snapshot for sealed zero-oxygen center, removed wall/open center, then rebuilt wall/sealed center. The producer gate now requires this check. The shell is not saved. DX11 diagnostic `9ebdc4973ed74608b1d7f27ba915ce32` found the undefined EmptyWorld sun and a missing depth-demand dependency for dynamic producers without a manifest; neither is matched visual evidence. Anomaly now keeps depth active for explicit occupancy or requested enabled media. New live verification remains required.

DX11 run `2f3820429cb4490eacfcb2d09ff09eac` verified producer readiness and the sealed zero-oxygen snapshot, but remained at the removed-wall gate. Installed gas code does not queue a deleted block in its ForcePressurize-only incremental path. Shared tracking now invalidates immediately on grid/door edits and coalesces an explicit full regeneration. The focused `--interior-only` lane passes thirteen checks, including immediate invalidation, coalescing and replacement completion; live open/reseal proof is still required.

Corrected DX11 run `a83b4531c5ac4d5883bb4415b68698f7` passes `anomaly-interior-passed.txt`, `anomaly-producers-passed.txt`, reviewed off/on captures, broader `fixture-completed.txt` and controlled unload/exit. It proves actual zero-oxygen room opening/resealing and dynamic no-manifest depth demand on DX11. The matching corrected Vulkan run is `7c0c93ed3506463ebac77222c6c31f56`; await its results before labeling the pair complete.

Vulkan `7c0c93ed3506463ebac77222c6c31f56` passes both Anomaly markers and reviewed captures with SDK validation enabled/errors=0. Its broader fixture failed the door cycle. Cold compilation can jump wall time over both open/close thresholds in one update; the close command now waits for OpenRatio > .95, preserving the existing maximum-open and final-closed assertions. This refinement needs its own live rerun. `TestResults/GuestDepthGpuTests/live-producer-comparison.json` records the matched enabled sky-region mean RGB difference (about 3/3/2 levels); do not interpret it as whole-image parity, because baseline scene lighting differs.

Run `763298d91a0747d29e713c363c934832` passed both Anomaly gates and advanced past the door assertion, but failed missing `fixture-lcd-text.png`. Its request log shows four capture stages requested in the same second following cold compilation. In parity mode only, scenario time now accumulates at most 100 ms per update so a long render stall cannot collapse several camera/capture stages into one tick. The outer wall-clock timeout is unchanged; allow `-DurationSeconds 600` for this mode's full exercise. The fixture still requires every capture and the original simulation assertions. The clock refinement requires a live rerun.

Final Vulkan run `7d7c38e694614d43a63267a3686fa8ff` passes both Anomaly gates, every capture, the complete simulation fixture and controlled unload/exit with the capped schedule. Door maximum/final ratios are 1/0; astronaut travel is 11.77 m. SDK validation remains enabled/errors=0, device-lost=0, and no managed D3D11 creation attempts are observed. Its off/on captures were reviewed. Together with DX11 `a83b4531c5ac4d5883bb4415b68698f7`, this is the accepted live pair for the four producers; prior runs remain diagnostic history. It does not establish identical base renderer lighting or untested vendor/fault/performance lanes.
