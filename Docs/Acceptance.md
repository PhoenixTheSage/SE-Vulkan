# First playable acceptance

The first pass is complete only after the Creative fixture works in the actual game, selection/recovery checks pass, captures show native rendering and the registry accurately identifies remaining work. Native unit tests alone do not complete this gate.

## Fixture

Use an isolated empty-space **Creative** world with no planets or asteroids, in a Vulkan-compatible Pulsar profile. Keep a DX11 capture of the same save and camera position. Include:

- A large-grid platform with color-masked armor, construction-stage blocks, a door or rotor, a fan/wind turbine or other RenderDirect animator, emissive blocks and a cutout material.
- A small grid with generic/cube instances and visible deformation; create and delete it during the test.
- A visible astronaut with animated skinning, movement and a controlled gravity field.
- Point and spot lights with visible color/range changes, plus sun/ambient lighting and cascaded shadows.
- An external LCD whose content exercises text, sprites, clipping and material replacement.
- HUD, loading screens and menus, followed by screenshots of the world and the LCD.

Compare geometry, texture orientation, material channels, paint masks, character animation, light falloff, shadow coverage, billboard blend, glyph baselines/kerning, scissor bounds and LCD contents. Pending effects must be visible in status.

## Required scenarios

Select DX11 then Vulkan and restart; repeat in reverse. Cancel a draft, reject a changed resolution, let confirmation time out, and accept it. Check persistence and plugin-disabled startup. Test malformed config, missing assets, changed factory fingerprint, incompatible enabled graphics plugins, and crash recovery plus explicit retry. A backend draft must never close an active world.

During Vulkan activation, inspect resource destruction, resize, minimize/restore, VSync, scene unload/reload, character and instance lifetimes, and copied pooled messages. Capture a native frame and verify GPU draw/submission counters. Windows Vulkan startup must create no D3D11 device. Run Vulkan 1.4 validation with core and synchronization checks and compare captures against DX11. The status report must identify validation availability rather than interpreting an unavailable layer as a passing validation run.

Linux acceptance requires both X11 and Wayland native hosts, SDL owner-thread surface creation, input, window mode/resize, null rendering and presentation readiness. A Linux cross-build or headless test does not establish live Linux game compatibility.

## Automated evidence

- `Tests/BootstrapTests`: config atomicity/malformed input, recovery journal/retry, Display transaction behavior, installed factory IL patch/null preservation and mismatch fallback.
- `Tests/InteropTests`: actual managed/native structure packing, Vulkan 1.4 preflight, textured sprite/indexed mesh pixels, offscreen upload, counters, minimized/restore presentation and resource lifetime.
- `Tests/SceneContentSmoke`: real installed MWM/model/packing/skin data; DDS parser regression cases.
- `Tests/CreativeFixtureHarness`: actual stock simulation, moving astronaut, grid movement/deletion, door cycle, RenderDirect animation, HUD/LCD PNG delivery, standard/additive billboard submissions, light changes and in-world Display transactions. Every run has marked private user data; capture delivery is separate from visual review.
- `Native/tests/smoke.cpp`: ABI/error cases, older loader/device rejection, each required-feature failure, optional-feature absence, real mesh/sprite/LCD/light/shadow/depth GPU pixels, resource retirement and resize. Windows adds real HWND surface/capture and D3D11 module absence.
- Shader compilation tests: content-sensitive keys, reflection/register layout and installed HLSL compilation through pinned tools.
- `Tests/WorldPassGpuTests`: actual stock world/light/composition pixels, palette-only skinning, hardware instances/deformation, cascade replay, copied native packet ownership, asynchronous replacement/removal, resize/minimize/restore and persistent-resource unload. `Tests/WorldGpuSmoke` additionally exercises the production MWM/DDS/scene bridge.

Record each run, host/API/driver, validation state and resulting artifacts in `Validation.md`. Live checks remain open until their evidence exists.

## Recorded scope

The final Windows fixture pair recorded in `Validation.md` exercises the controlled Creative slice with actual movement, scene lifecycle, LCD/HUD captures, billboard submissions, light changes and in-world Display transactions. The Vulkan run has zero core/synchronization validation errors and native command-capture evidence is retained separately. Selection in both restart directions, resolution rejection/timeout and Pulsar status controls also have actual-game evidence.

Broad gameplay/material/effect parity and native Linux runtime/headless/X11/Wayland acceptance remain open. Cross-compilation, handler counts and delivered screenshots must not turn those pending checks into verified systems. The stock world path now uses GPU instance/deformation/skin streams, persistent geometry, asynchronous staging batches and fenced frame uploads. Large-world throughput and visual comparison remain separate performance/acceptance work.

## Phase #2 asteroid gate

Run the isolated Creative harness with `-Asteroids -DurationSeconds 360`. It adds one deterministic asteroid to the existing fixture, moves through near/middle/far LOD views, mines using the stock storage operation, saves/reloads its `.vx2` into a replacement actor and unloads. Require delivered before/after/reload captures, active native voxel counters, zero core/synchronization validation errors and zero voxel residency after unload. Compare captures against DX11 from the marked initial snapshot. Saved-storage reload does not establish full-world restart acceptance. Timed LOD fades, all quality tiers, broad material/mod parity and native Linux runtime remain separate checks.

## Phase #3 planet gate

Run the isolated Creative harness with `-Planets -DurationSeconds 480`. It creates a deterministic 19-km EarthLike planet (seed 731), holds orbit/approach/surface cameras, exercises grass/trees and weather fog, mines an 8-metre region, saves, changes atmosphere height and unloads. Require six delivered captures, active planet/atmosphere/cloud/foliage counters, zero core/synchronization validation errors and zero voxel/foliage/LUT resources after unload. Use a fresh `-Backend Dx11` run of this same seeded fixture; the initial snapshot is created before planet creation.

This gate establishes controlled main-view environment activity and cleanup. It does not establish complete planet visual parity, saved-planet reload, multiple-planet/mod coverage or live Linux acceptance. Washed-out fixed exposure, atmospheric probes, precipitation particles, timed voxel fades and complete graphics-quality behavior remain explicit dependencies. See `Planets.md` and `Validation.md` for the recorded evidence.

## Phase #4 transparency/particle gate

Run the isolated harness with `-Transparency -ObserveD3D11 -DurationSeconds 420`, and the same fixture with `-Backend Dx11 -Transparency`. Require glass, OIT/layers, decal and flare draw evidence; positive completed GPU particle counts from stock electrical/rain/snow definitions; eventual zero alive / 409600 dead counts; all five PNGs; world/Display completion and unload markers; zero core/synchronization validation errors; and zero observed managed SharpDX native device creation attempts during Vulkan. Optional RenderDoc captures establish actual native draw/dispatch/indirect command evidence. Validate recorded artifacts with `scripts/verify-transparency-fixture.py`.

Review corresponding held cameras, material/decal placement and effects against DX11. Sparse weather captures and differing lighting/exposure cannot establish pixel parity. Probe reflections, complex layered glass, exact flare scheduling/smoothing, particle lighting/collisions/quality, later LDR/PostPP/stereo composition, broad content mods and live Interim/Linux acceptance remain separate checks. See `Transparency.md`.

## Native media gate

Use `Tests/WindowsStartupHarness/Run-Isolated.ps1 -TestMedia` separately with
Legacy/net48 and Interim/net10, allowing at least 120 seconds for natural KSH
completion and the Display checks. A 600-second overall run checks looping, actual
frame progression, reported A/V offset, source cadence and resource growth
after warmup. Retain the packaged-runtime hash, CSV samples, PNGs and orderly
shutdown markers. Include a separate display-transaction run while media is
active. An undefined WMV average frame rate must use measured packet timing;
an ASF timestamp grid is not a usable video frame rate.
For the full menu-only soak, use `-TestMediaSkip -DurationSeconds 630` and
`scripts/verify-media-run.py --menu-soak --skip`; the verifier requires 600
seconds of background playback after the last KSH observation.

Run the generated-fixture native protocol tests with the private bundle on
both operating systems, and the GPU streaming/readback test with synchronization
validation. Live Linux additionally requires a working audio backend, looping,
minimize/restore and clean exit. Manually verify skip behavior and audible
fades, and compare held media timestamps with DX11. Performance requires the
installed media's native cadence, A/V offset within 100 ms and no continuing
allocation growth during a ten-minute loop. Missing Linux runtime evidence or
matched-timestamp comparisons leave cross-platform parity open. See
`NativeMedia.md` and `Validation.md` for implementation and actual evidence.
