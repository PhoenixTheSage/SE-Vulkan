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
