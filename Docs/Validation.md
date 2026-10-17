# Local validation evidence

## Visible Rich HUD terminal with DLAA — 2026-10-16

The earlier triangle, clipping and ordering corrections did not fix the actual
DLAA presentation path. A private copy of the Pertam Orbiter save, Anomaly,
Rich HUD Master and SE-DLSS reproduced the user symptom: `OpenMenu` registered
successfully, the renderer received 164 PostPP triangles and queued 164 packets,
and NGX reported `NGX evaluating`, but the screenshot contained no terminal.
With Anomaly alone, the same terminal was visible.

NGX evaluates AfterTonemap before the PostPP stage, and final composition selects
its output instead of the later postprocess image. PostPP now draws after scene
composition and temporal processing, before sprites, at output resolution.
Composed depth is retained and copied for soft-particle sampling; output-format
pipelines preserve premultiplied blending and depth testing. Generated presents
replay the same overlay outside the interpolated scene image.

The same saved-world/menu scenario now visibly shows the terminal while NGX is
evaluating. The corrected run enabled Khronos core/synchronization validation
(`enabled=True; available=1`) and reported no Vulkan validation errors.
Evidence: `TestResults/HudRoute/before-dlaa.png`, `after-dlaa.png`, and the paired
`before-packets.json` / `after-packets.json`. The trigger uses Rich HUD's public
`OpenMenu` API; this is terminal rendering evidence, not a keyboard F2 test.

PostprocessGpuTests (179 checks), TransparencyGpuTests (149 checks) and the
non-NGX HudBillboardGpuTests pass with validation enabled and zero errors.
Release native CTest: 15 passed, one unrelated GPU opaque reference skipped.
Optional headless NGX readback assertions preserve 338 HUD pixels in both DLAA
and Quality mode, with zero HUD history pixels after closing, but that process
fails during native NGX teardown. The same shutdown access violation is present
in the unchanged before-run and the corrected in-world run. Those NGX runs are
not clean-exit test passes; fixing driver teardown is separate from this render
route correction.

## Rich HUD near-plane billboards — 2026-10-16

Rich HUD Master places its PostPP triangles on `IMyCamera.NearPlaneDistance`
(0.05 m). Keen's billboard rasterizer leaves depth clipping disabled; Vulkan
previously clipped these primitives. With the stock complementary infinite
projection, the focused GPU reproduction produced 338 red pixels at -0.05 m
but zero at -0.049999 m. Enabling queried core `depthClamp` on billboard
pipelines restores 338 pixels in the latter case. Depth tests and read-only
depth attachments retain their existing settings. Other pipelines retain
their clipping state. Required-feature bit 14 reports depth-clamp support.

Keen also deliberately preserves LDR/PostPP submission order. Vulkan now keeps
that order, preventing distance sorting of coplanar background/text/cursor
layers. The earlier triangle snapshot/atlas UV fix remains in place.

`HudBillboardGpuTests` passes all three plane cases plus an installed-texture
transient `MyTriangleBillboard` through production scene conversion (338 pixels)
on NVIDIA GeForce RTX 5070 Ti, with Khronos core/synchronization validation
enabled and zero errors. Result: `TestResults/HudBillboardGpuTests/result.json`.
`BillboardPacketTests` passes snapshot, atlas UV, degenerate triangle, ordinary
quad and 32 transient HUD layer-order checks. Existing transparency (149 checks)
and postprocessing (179 checks) GPU suites pass with zero validation errors.
Release native CTest: 15 passed, one unrelated GPU opaque reference skipped;
mock device preflight now checks missing/enabled depth clamp. Both managed
targets build and deploy with matching native assets to Pulsar Local.

The complete F2 menu interaction in a running world remains unverified.

## DLSS internal-resolution alignment — 2026-10-15

Managed world setup previously used the DRS-only dimensions while native EndFrame
later replaced the fraction with NGX/FSR's mode scale. Balanced therefore rendered
1114x626 images using 1920x1080 frame/postprocess constants. The new additive
`sevk_resolve_render_resolution` export applies the same native mode precedence and
rounding before managed frame/light-tile/SSAO/HBAO setup. Stock `Screen.drs` is unity
for Vulkan's exact-sized targets, avoiding a second scale during SSAO reconstruction.

Release native CTest: 15 passed, one unrelated GPU opaque reference test skipped.
`WorldPassGpuTests`: 496 checks passed with the bundled Khronos validation layer,
including NGX mode changes, immediate disable, matching frame dimensions/light tiles,
and unity SSAO coordinate scaling. These NGX planning checks do not evaluate DLSS.
`PostprocessGpuTests` also passes, including fractional-resolution output checks,
with zero Vulkan core/synchronization validation errors.
Managed net48/net10 Release builds pass with zero warnings/errors; both builds and
the native library were deployed to Pulsar Local. The native SHA-256 is
`336A0FB6BEF00D0AB694C98467BF09478CD252A4BBB98DBF96072944A0CA3606`.
Live game DLSS-versus-DLAA visual verification remains pending after restart.
Build/test logs are in `artifacts/dlss-resolution-*.log`.

## Celestial scene-depth regression — 2026-10-15

The Anomaly celestial guest draw used a writable pipeline with a read-only depth
attachment, a sampled-depth descriptor declaring a different layout, and a
discarding depth store operation. Its loaded HDR attachment also lacked the
required read dependency. These contracts are corrected in `Native/src/guest.inc`.
Vulkan 1.4's `STORE_OP_NONE` preserves the unchanged depth attachment without a
store write; subsequent fragment and compute consumers retain their input.

`Tests/EnvironmentGpuTests/CelestialDepthRegression.cs` submits an actual celestial
draw over opaque geometry, then reads depth through a later guest pass and compares
all depth bits against a frame without that draw. The original native library
fails core/synchronization validation; the corrected library passes all 50
environment checks with zero errors. `Tests/GuestDraw` also passes both hardware
pixel checks with validation enabled. Logs are in `TestResults/CelestialDepth-*`.
These establish the repaired GPU contract; they do not establish that the user's
exact camera-dependent Pertam corruption has been reproduced or eliminated.

The subsequent private Pertam run with Anomaly, DLAA and Final Frontier exposed
NGX output transfer-clear hazards and an access violation at renderer shutdown.
The NGX output barriers now cover compute and transfer writes. Explicitly owned
parameter maps are released before NGX shutdown, following the lifetime contract
in NVIDIA's `nvsdk_ngx_vk.h`. These use the same saved-camera integration route
for their final verification; the focused celestial test remains separate.

The final integrated replay reports validation enabled, zero errors and 2,813
presented frames. The nearby reverse view renders normally. An access violation
at `NativeBackend.Dispose` remains during shutdown despite correcting parameter
lifetime ordering; the same failure occurred before these changes. This is not
a clean full-session acceptance result. The exact screenshot pose remains
unverified. The private fatal process was terminated after its evidence was saved.

Recorded through 2026-10-05 against installed Space Engineers **1.210.014**, Windows x64, NVIDIA GeForce RTX 5070 Ti. The loader reports **1.4.341** and the selected device **1.4.351**. These are implementation checks; the complete Creative acceptance gate is tracked separately in `Acceptance.md`.

## Build, packaging and bootstrap

- Managed Debug and Release build net48 and net10.0 with zero warnings/errors. Deployment copies `plugin.dll`, `plugin.xml`, both native platforms, six SPIR-V shaders and compiler tools into the corresponding Pulsar Local plugin folders.
- The installed Pulsar manifest parser accepts the permanent GUID, Windows/Linux platforms and **CLR,CoreCLR** runtimes. Its actual source compiler compiles the repository sources for both Legacy and Interim. Publication metadata still needs a real project commit and immutable asset distribution.
- Bootstrap tests use the installed original `SpaceEngineers.exe`: pinned factory signature/IL, stack-compatible replacement, preserved dedicated/null branches, idempotent patch and changed-fingerprint rejection. They also cover missing/malformed/XXE configuration, atomic replacement, stale config writes, session recovery/retry and Display draft/resolution transactions.
- `RenderProtocolTests` exercises the production frame processor with real VRage update frames: ordering, retained draw replay, state-once behavior, skipped frames, persistent ownership, one disposal per message, nested ownership and render-thread queue reuse. Animation timing also covers measured time with the stock 66 ms clamp, positive fixed-step overrides, negative pause and resume without a time jump. All 17 checks pass.
- All 28 installed HLSL compiler checks pass with pinned DXC/SPIRV-Tools, including source/include/tool-sensitive cache keys, corrupt caches, Vulkan 1.4 validation, reflected bindings/layouts, UAV counters, immutable batch snapshots and installed sprite VS/PS. The full installed runtime catalog's conversion evidence is recorded below; native pass integration remains separate.

The preloader finish callback must remain BCL-only. A real startup caught a render-settings static initializer running before `MyVRage.Platform` existed when runtime Harmony hooks were installed too early. Hooks now install at the initialized renderer factory before device creation.

## Native and scene rendering

Windows native Debug and Release each pass all three CTests with Khronos **core and synchronization validation** enabled. The private official validation package lives under `Native/build/validation/Bin`; no global Vulkan SDK installation was made. GPU assertions cover sprites, geometry, reversed depth, midgray gamma, cutouts/dither, LCD gamma, local lights, cascaded shadows, additive depth behavior, capture, resize/minimize and resource retirement. The standalone HWND test loads no D3D11 module.

Native preflight tests independently reject a mocked Vulkan 1.3 loader/device and each of the six missing required features before device creation. An API 1.4 device with every optional host-copy/local-read capability absent reaches the mocked device creation call with all used features explicitly enabled. Instance cleanup is asserted on rejection. Optional capabilities are queried from Vulkan 1.4 features/properties and reported separately; they are neither required nor enabled.

GUI textures retain the installed FontDataPA premultiplied bytes; raw GUI RGBA receives the stock gamma-2.2 premultiply preprocessing in a separate cache variant. Sprite tint and alpha blending operate in linear space with sRGB attachments. All **27** production packet/cache checks pass, including stock HalfVector4 packing, complete DDS mip chains and separate GUI/world cache variants. Independent installed-font BC7 base-level and mip-two edge samples agree with the reference blend within two byte values, with zero validation errors. These checks establish the blend formula, while live glyph placement and full UI comparison remain separate.

The managed/native bridge passes real ABI packing, Vulkan 1.4 preflight, GPU red sprite/blue mesh pixels, offscreen target upload, counters, resource destruction and minimized/restore checks. Required features are explicitly enabled, including shader demotion, which validation caught during bring-up.

Installed MWM/DDS scene tests pass for armor and astronaut models, actor parenting/visibility, copied instance and skeletal arrays, deformation, skybox cube faces, local-light parent/offset behavior, texture-thread ownership and scene unload. DDS parser regression cases pass.

`Tests/WorldGpuSmoke` uses the production managed scene and native engine, rather than a stand-in renderer. Its six-frame run submits **22 mesh draws** and uploads **233,870,400 bytes**, with validation enabled and **zero errors**. Real armor changes 12,677 pixels, an added astronaut 1,160, a red point light 11,990 and skybox content all 65,536 pixels. Hiding actors restores the empty image. Unload leaves zero scene meshes/textures and one native built-in fallback texture. Captures and measurements are in `../artifacts/world-gpu-smoke/result.json` and adjacent PNGs. This headless scene check does not establish live gameplay acceptance.

Linux Debug and Release libraries cross-compile from Windows using SHA-verified Zig 0.14.1. ELF64 exports and glibc dependencies were inspected. Linux runtime/headless and X11/Wayland game tests remain pending until a suitable Linux host is available; CI can skip GPU tests with exit code 77 when Vulkan 1.4 is unavailable.

The Linux compiler bundle is packaged with pinned upstream DXC and a cross-built `spirv-val`, including runtime libraries/licenses. It retains DXC's `bin`/`lib` layout; the managed Linux path repairs executable bits lost in Windows packaging. The upstream DXC executable requires **glibc 2.34**, independently of the native renderer's glibc 2.31 build target. Compilation tools are optional for startup, which consumes compiled SPIR-V. This Windows host has no WSL installation, so ELF inspection and cross-compilation do not establish Linux execution. `scripts/verify-native-assets.py` checks PE/ELF x64 architecture, all **21** ABI exports on both native platforms, and hashes in `TestResults/Native-FinalAssets.json`.

## Actual isolated game launch

The Windows startup harness copies Pulsar into a new `TestResults/WindowsStartup/<id>` directory with its own Local plugins, profile and game user data. It does not load or write existing profiles/worlds. Vulkan attaches to the actual game HWND, with native screenshots taken through the game render-message path.

Run `bd757e56c8324e779636288e9b6416cc` presented **8,111 frames** and **7,557,405 sprite draws**, loaded 18 fonts and 1,613 textures, and reported zero core/synchronization validation errors. Its `Reports/menu-native.png` shows the readable first-run welcome screen. Cyan horizontal lines are stock `Content/Textures/GUI/Screens/main_menu_overlay.dds` artwork, retained by the static menu bypass.

That run exposed Pulsar Legacy's forced process kill in its `ExitThreadSafe` patch, which bypasses renderer disposal and retains the recovery journal. The Vulkan-scoped integration preserves vanilla orderly shutdown while Vulkan is active. Follow-up run `69e71b779d414bdaad02dfcee67cad82` exited **0**, executed harness disposal and process-exit callbacks, closed the game log normally and removed the healthy Vulkan session journal. It presented **7,937 frames** and **7,601,265 sprite draws**, with zero core/synchronization validation errors. DX11 retains Pulsar's original exit behavior.

The actual game process loads D3D11/DXGI modules, including overlay-related modules. Module presence cannot establish whether a D3D11 device was created. `Tests/D3D11DeviceObserver` instruments the installed SharpDX native creation call sites through a test-only preloader. Its 13 checks include a real CPU WARP positive control recorded as one successful call. During isolated Vulkan run `7b5c08a1f401445aa7db54d0b2fc31ee`, the loaded SharpDX assembly was stamped and observation was ready 11 seconds before renderer initialization, with zero creation attempts observed. This establishes absence of game managed SharpDX creation calls in that run; native overlay calls remain outside its scope. The observer is kept out of normal deployment.

The first actual Creative attempt exposed world-transition font lifetime and a fixture session-defaults loading failure. Font definitions now survive scene unload. The harness explicitly disables current encounter generators, initializes the public block-limit objects omitted by the legacy new-session API, creates only the selected private save path and disables cloud saves. Failed world loads are not counted as playable acceptance.

Follow-up Creative run `f84e024a7a86453385e8084cd084e3ab` reached a real ready/offline session with **86 large-grid blocks**, **9 small-grid blocks**, an astronaut/local player, working powered LCD and measured gravity **(0, −9.81, 0)**. It captured overview, LCD and player HUD, then unloaded the scene and exited orderly. Final counters are **5,182 presented frames**, **896,620 mesh draws**, **2,488,292 sprite draws** and zero core/synchronization validation errors. Scene unload leaves zero actors, instances, models and meshes while retaining 18 device-lifetime font definitions. The observer remains ready/stamped with zero managed D3D11 creation calls.

Capture review identified two LCD gaps: the camera viewed the back of the panel, and the generated-texture budget stayed zero because Windows normally populates it during DX11 device creation. Vulkan now supplies a scoped conservative generated-texture budget, and inactive LCD material surfaces are hidden. Subsequent matched-save captures below establish front-facing text/sprite/clipping delivery. CPU skinning and expanded instance uploads remain throughput limitations; cumulative uploaded bytes are not resident memory.

Windows Interim run `6f0adc4f60d84989bd242e0adc51e5a5` independently boots the actual game on **CoreCLR 10.0.11** with its installed dotnet-compat core. It presents **6,739 frames**, draws **4,653,913 sprites**, captures the main menu and Display selector, exits **0** and clears the healthy journal, with zero core/synchronization errors. The compatibility exemption is limited to the verified core plugin identity/contracts. The Display capture shows Renderer above adapter controls and a visible restart notice; cancel retains the requested Vulkan choice.

Interim transaction run `0d0fce4dcb814abfa7776de9735e571e` exercises actual Display OK and resolution confirmation: renderer-only OK, NO rejection with resolution revert, YES acceptance, restoration of the private resolution and persistence of a DX11 request while Vulkan stays active. It presents **10,547 frames**, exits **0**, clears the journal and reports zero validation errors. Its subsequent private DX11 restart uses `VRageRender.MyDX11Render`, leaves the native asset unloaded and commits a Vulkan request through Display OK. The stock Pulsar DX11 exit code is −1 because its force-kill behavior remains enabled. The following Vulkan restart `ee0c2555a57e465ead2bcd87df9fe725` reads that persisted request, presents **7,160 frames**, exits **0** and clears the journal, with zero validation errors. Both restart directions therefore have actual-game evidence.

Interim run `20f32274e2964045bdbc25d4449a4907` additionally lets the real **60-second resolution confirmation expire**. The native window returns from 720×480 to 640×480 and the Vulkan draft is discarded while requested DX11 remains persisted. The actual Pulsar config buttons open the populated status report, write it to the game log and scroll to lower coverage/recent failures. Healthy Retry leaves the active-session journal and retry generation intact. This run presents **17,133 frames**, exits **0** and removes the journal, with zero validation errors. Testing exposed and fixed the template's null instance-button binding and the healthy-session retry journal removal.

## Matched Creative captures

DX11 run `f7ceb97844a5402b868d545a5dc5bcc3` and Vulkan runs `e853ebe7eee8413399877d770300465e` / `900c43f91988436cbdb8f3cbc68bba6f` load copies of the identical initial private snapshot from `b56482a2b9894fdab9630a20bb9460ee`. Camera positions, 640×480 dimensions and timed LCD content match. Both backends load 91 large-grid blocks, nine small-grid blocks, the same fixture astronaut and a local playable character. Gravity is `(0, −9.81, 0)`, the external LCD is powered, the real door reaches maximum opening 1 then closes to 0, and the real turbine RenderDirect component receives approximately 3,900 animation commands. Five delivered PNGs cover overview, animation, LCD text, rotated/clipped LCD sprites and player HUD.

The first matched Vulkan run presents **5,907 frames**, **1,350,458 mesh draws** and **2,586,850 sprite draws**, with zero core/synchronization errors. It proves the corrected LCD logical viewport: physical size remains 512×512 while stock `AspectRatio` scales sprite coordinates and scissor endpoints. Native and bridge assertions cover nonuniform rotation/scaling, clipping and invalid logical dimensions. The original offscreen ABI export remains available.

Review then found the stock material emission multiplier was absent: installed `Environment.sbc` sets `BloomEmissiveness=120`, and installed `Lighting/LightDir.hlsl` multiplies emission by it before tone mapping. The scene now copies that scalar from the pooled postprocess message and multiplies per-material emission once. A regression mutates the original message after dispatch and checks a material value of 0.5 becomes 60. The message remains **Pending** because adaptive exposure, bloom and other settings are not implemented. Follow-up run `900c43f91988436cbdb8f3cbc68bba6f` delivers all five images, presents **5,470 frames**, **1,322,860 mesh draws** and **2,341,645 sprite draws**, with zero validation errors. Unload leaves zero scene actors/instances/models/meshes, retains 18 device-lifetime fonts, closes the log normally and clears the healthy journal.

Qualitative comparison confirms corresponding platform geometry, paint hues, skybox orientation, construction stage/preview, turbine, door, font/HUD placement, LCD text layout, rotated bar and scissor boundaries. Brightness and material response visibly differ: fixed ACES exposure differs from DX11's adaptive exposure, and full metallic/gloss lighting, probes and postprocessing remain pending. These images establish a working controlled scene, not pixel parity or the entire acceptance checklist. The observer is ready before render initialization: Vulkan records zero managed SharpDX device creation attempts, while the matching DX11 run records eight successful calls. Native overlays remain outside that observation scope.

## Native captures and moving-actor regression

RenderDoc 1.46 was enabled only in private fixture run `d87a33f8bacb4e038d9b2bdcfa7a1112`. Two native `.rdc` files record the world and LCD sprite frames. The world capture's structured call trace contains **5 `vkCmdBeginRendering`**, **2,181 `vkCmdDrawIndexed`**, **28 `vkQueueSubmit2`** and **1 `vkQueuePresentKHR`** calls. Extracted thumbnails show the scene and LCD. This establishes actual native Vulkan commands; replay was not verified. That run's movement exercise used ordinary inputs on an uncontrolled NPC and produced no motion, so it is not an accepted simulation run.

The fixture now uses the stock copied `MyMoveNetCommand` path through `CacheMove` and follows the astronaut with a spectator camera. It also deletes the nine-block small grid and checks that the grid closes. Expanded Vulkan run `f2a8c31feae84e109a9822a4ed52d97c` and DX11 run `ff2db8a899d447ae8b40f06ddfd1b86e` both record **17.58 m** of horizontal travel, successful deletion, door animation and six delivered captures. However, the walking astronaut appears only in the DX11 image. These counters alone do not satisfy visual acceptance.

Diagnostic run `cb508145416e47fd861e9b6699d1c5dd` identifies a stale render transform: valid 81-bone palettes and bounded local skin geometry remain at `(2, 0, −1)` while physics moves the astronaut to approximately `(2, −1.25, −9.92)`. The proxy's pooled `MyRenderObjectUpdateData` does not clear presence flags in message `Init`/`Close`; DX11's postponed-update manager performs that cleanup. Vulkan now applies the packet synchronously, gives a world matrix the stock precedence, and cleans presence flags even for missing actors. The scene regression reuses one packet across a child-local update, a parent-world update and an unknown-actor update, checking both propagation and cleanup.

Corrected run `a9c6760713944c16b67ebbdd1f1b2c66` visibly renders the walking astronaut. Recorded render positions follow physics and the 81-bone palette produces changing bounded geometry. The slower walk covers **12.11 m**, the small grid closes, the door opens to 1 and closes to 0, and the turbine receives 4,325 animation commands. All six PNGs are delivered. The run presents **8,008 frames**, **1,061,237 mesh draws** and **2,979,304 sprite draws**, with zero core/synchronization validation errors and zero observed managed D3D11 creation calls. World unload leaves zero actors/instances/models/meshes, retains 18 fonts, and orderly shutdown clears the journal. The tested net48 assembly SHA-256 is `582A59716FDDD55F3F527201B8F145C90FF939FE7A5CCB1581CEDEB5C382CBD6`. This closes the disappearing-astronaut regression; final billboard/light/in-world selection checks use the later bundle below.

LDR/PostPP billboard composition is now explicitly skipped and counted as pending, along with custom-projection packets. It is no longer substituted by a standard world pass. A scene regression verifies both unsupported buckets produce no draws and increment the pending counter. Standard/additive status retains its last actual submission frame rather than advancing from historical counters or skipped packets.

Final Vulkan run `9107b52d98564801819aa8b1feee29cb` adds **12,966** standard/additive-bottom/additive-top calls through the real game API, with **7,830** native billboard submissions observed. It exercises overlapping red/green/blue fixture quads, point-light color/radius and spot-light color/range changes, and actual Display Cancel/OK in the active world. Cancel discards the draft. OK persists requested DX11 while Vulkan remains active, reports a restart, and leaves the world ready. The astronaut travels **12.11 m**, small-grid deletion succeeds, the real door cycles and the turbine receives 4,322 commands. All six PNGs are delivered; the walking astronaut, changed lighting and overlapping billboard quads are visible.

This run presents **7,906 frames**, **1,059,478 mesh draws** and **3,174,109 sprite draws**, with **zero core/synchronization validation errors**. The observer is ready before renderer initialization and records zero managed D3D11 creation attempts. World unload leaves zero actors/instances/models/meshes and retains 18 fonts. Graceful disposal clears the Vulkan journal despite a pending DX11 request. The final net48 assembly SHA-256 is `1E3DF715A5AD4D9C40D4A288D25F976CCDBD5EEEAFF187637B9C291D5FFC4CE8`; the Windows native SHA-256 is `B2C6C06C6C889BE175EB8B96012D7F268CC3F1B048CB3FA16E9A298A501A8A41`. Both Pulsar deployment folders match their Release DLL, manifest and all 32 packaged assets. Debug/Release managed targets and real Legacy/Interim source compilation pass after the final changes.

Matching final DX11 run `2e963415aa2a425e8c1a62971a8282fb` uses the identical initial snapshot and final harness. It records the same **12.11 m** travel, **12,966** billboard API calls, 4,322 turbine commands, successful small-grid deletion, changed lights, six PNGs and in-world Display Cancel/OK. It persists requested Vulkan while DX11 remains active and the world stays ready. Native assets remain unloaded; the observer records **eight successful D3D11 creation calls**. Reviewed overview/walking/LCD comparisons show corresponding geometry, poses, billboard placement, HUD, text and clipping. Stock PostPP icons remain absent under Vulkan by design of the pending composition path. Fixed-exposure brightness, bloom, specular response and full transparency remain visible parity gaps. Both private game processes have ended.

The controlled Windows Creative slice now has simulation and native-rendering evidence. This does not close broad gameplay, every material/effect permutation, complete graphics-quality behavior or live Linux acceptance. Those remain explicit in `Acceptance.md`, status and the message registry.

## Vanilla shader conversion

The current **1.210.014** renderer's CPU cache/material builders and runtime geometry layouts capture **20,736 unique variants from 118 entry-point files**. The inventory hashes all **217** shader/include/configuration files and explicitly classifies five obsolete, unreferenced HLSL files as excluded. There are zero unexplained HLSL files. A second capture exactly matches the canonical inventory. Capture never invokes shader/device creation.

The canonical bulk run validates **20,736 / 20,736** variants with **zero failures**, targeting Vulkan 1.4 and taking 1,256 seconds. The complete report is `TestResults/ShaderSuite/Canonical/results.json`. DXC emits SPIR-V 1.6; SPIRV-Tools validates Vulkan 1.4 scalar layouts. Source hashes are checked before/after compilation and each artifact dependency matches the captured raw source. The independent final tool fingerprint matches every result: `39a4a7347e870c99bc68bd6a07437e6bdb62032ff871fc47e9549e855ced5510`. The installed renderer assembly fingerprint is `b4a6502f913b70e53c8f715bfc6dfebef2ae9e87be52a95c84eb7c5fedcdde3b`.

Full cross-stage verification checks **9,528** material VS/PS pairs and **11,332** shared descriptors, with zero location/type or complete descriptor-layout mismatches. Comparison ignores XML indentation, while retaining every reflected member value. The suite packages **1,401 unique SPIR-V modules** and their reflected metadata; each variant retains its own source/definitions/cache identity in the compressed catalog. Catalog SHA-256 is `c475072906c37b1dc3833e0a465e825d35cb867dc3eb650d296b6e3255f5e33d`. No installed HLSL is redistributed.

All eight artifact-library checks pass, including loading every variant with checked binary/metadata hashes and fresh reflection. They reject unknown variants, changed catalogs, DTDs/external entities, path traversal, edited installed content, mismatched game assemblies and corrupt modules. The 28 compiler checks pass after the final changes. Managed Debug/Release targets pass with zero warnings/errors. Actual installed Pulsar source compilation passes for all 64 source files: Legacy produces 270,848 bytes and Interim 273,408 bytes. Generated catalog availability reports Ready with zero observed frames; native vanilla integration and visual verification remain explicitly Not implemented. See `ShaderSuite.md` for the reproduction command, exclusions and compatibility repairs.

Real Legacy/net48 run `5283011b880e42669c8346efd027c4bc` loads the full catalog and reports **20,736/20,736** conversion Ready with **1,401** modules. Existing native menus and Display Cancel continue working, with screenshots delivered, **4,866** frames and **3,038,987** sprite draws, zero validation errors and exit **0**. The healthy startup journal is removed. A preceding run `ec32a5d2a4f644a58ec1de27054ca824` reached the same Ready status but was terminated by the test's total deadline after cold startup took approximately 140 seconds; it is not a clean-shutdown acceptance run. The harness now allows 240 seconds for startup in addition to its measured test duration. Both installed Pulsar local deployments match their Release DLL, manifest and all **2,836** packaged asset files.

Interim/net10 run `eca64e5705eb44f79abd3997bf1db6d6` delivers menu/Display Cancel captures and exits cleanly, but catalog verification correctly rejects its compatibility-rewritten loaded assembly. This is not catalog acceptance. Identity verification now uses the original installed `Bin64/VRage.Render11.dll` beside the game Content directory on both platforms, preserving the original hash check. The startup harness additionally requires conversion Ready whenever a catalog is packaged.

Corrected Interim run `27369c324e214a7780d2a53354167619` passes that catalog assertion, reports **20,736/20,736** conversion Ready and keeps vanilla pass integration Not implemented. Native menu and Display Cancel screenshots are delivered. It presents **3,519** frames and **2,040,574** sprite draws with zero validation errors, exits **0** and clears the startup journal. The final net10 plugin SHA-256 is `253C17262EFB8C9443FD9D13DDF9F6B29BB06B298D960F924E868425638CE4C9`.

Final Legacy run `ae1c3c4723e644e6ab9a80975a946585` exercises the same corrected installed-assembly identity path and passes the catalog Ready assertion. Menu/Display Cancel screenshots are delivered, with **3,430** frames and **1,821,930** sprite draws, zero validation errors, exit **0** and a removed startup journal. The final net48 plugin SHA-256 is `7AFB814C75F102B00EC4F974CCC8E1E3FFC8344AC909600C4CC3AFBD4F418AB0`. Both private game processes have ended. Full shader conversion is accepted for the recorded inventory; native vanilla pass execution and live Linux acceptance remain pending.

## Phase #1 stock GBuffer continuation

The independent native Standard/AlphaMasked material pass passes **30 GPU checks** on the NVIDIA GeForce RTX 5070 Ti with Vulkan 1.4 core and synchronization validation enabled. Evidence: `TestResults/GBufferGpuSmoke/result.json`, with **10 Standard frames / 11 draws**, **3 AlphaMasked frames / 3 draws** and **zero errors**. Readbacks verify stock material channels/LOD, encoded view normals/AO, DirectX constant layouts, projection/object transforms, separate batched dynamic offsets, reversed depth, 8x8 dither, alpha-red cutouts, resize/clear, mesh replacement/removal and invalid resources/readbacks. Both dither and alpha split retain **676 of 1,352** fixture pixels. These are independent offscreen fixtures, not game-world composition evidence.

All **1,401** authenticated modules pass driver-facing reflection-annotation preparation, idempotence, executable layout/interface/capability comparisons and pinned Vulkan 1.4 validation. Evidence: `TestResults/RuntimeShaderTests/result.json`. The two optional Google reflection declarations caused real device validation errors before preparation; they are now removed with only their non-executable annotations. Other extensions and capabilities remain requirements of their future passes.

Windows native Debug and Release each pass all **3 CTests**. Linux Debug and Release libraries cross-build without warnings; PE/ELF Release packaging has all **26 ABI exports** on both platforms. Managed Debug/Release build net48 and net10, and installed Pulsar source compilers accept **68 source files** on both runtimes. Existing interop passes **16 checks**, and the original real-content world smoke still reports **22 mesh draws**, zero validation errors and zero scene resources after unload (`TestResults/WorldGpuSmoke-StockExtension`). Linux execution and stock world lighting/composition remain pending.

The isolated Interim startup run `ee00829489164a64bb652dbabdee5169` passes native menu capture, Display Cancel and the new stock pipeline Ready assertion. Its private Vulkan session presents **3,536 frames**, **2,047,818 sprite draws**, zero validation errors and exits **0**. The catalog and Standard pipeline report Ready at zero stock game-world frames; broader stock pass integration explicitly remains pending.

Legacy run `e67e7b91a314457a98415394d00ca3d7` passes the same assertions, captures and controlled shutdown, presenting **3,524 frames** with **1,882,904 sprite draws**, zero validation errors and exit **0**. Both private game processes have ended with their startup journals removed. Deployed Legacy/Interim managed DLLs and Windows/Linux Release native assets match the final local builds by SHA-256. Original user profiles and worlds were not used by these startup fixtures.

## Production stock world, GPU deformation and scheduling

The production scene now selects forty installed stock material/depth pipelines and stock directional/point/spot lighting, then composites HDR color and reversed depth before billboard/HUD draws. Imported geometry includes source tangent handedness, material DDS maps, packed cube data and immutable skin weights. Generic/static groups use hardware instance streams. Characters remap/partition material geometry once into sixty-bone palettes; animated frames do not reupload vertices. Two fenced graphics slots own frame uniforms, instances, palettes, light lists and transient descriptor sets.

`Tests/WorldPassGpuTests` passes **61 checks**: **41 frames / 41 material draws / 43 instances / 6 skinned draws / 11 cube draws / 83 lighting passes**, with zero core/synchronization validation errors. It exercises all twenty main material variants and their four-cascade palette/instance depth replay, directional/point/spot lighting, copied frame ownership, replacement/removal inside an unsubmitted upload batch, minimize/resize/restore and complete world resource release. Four initial texture/mesh uploads use one asynchronous timeline batch; the complete test uses nine upload batches and zero outstanding-slot waits. These counters establish the tested fixture behavior, not a large-world performance benchmark.

The production importer/scene bridge (`TestResults/StockWorldScene/result.json`) verifies installed armor, astronaut and cubemap pixels: **12,099 armor / 1,059 astronaut / 11,990 point-light / 65,536 skybox changed pixels**. All scene resources are released after unload, with zero validation errors. The original independent Standard/AlphaMasked correctness pass still passes all thirty checks.

The shader supplement packages sixteen validated stages for eight combinations absent from stock precompile annotations. Its binary, metadata and definition hashes are checked before activation; startup invokes no compiler process. Runtime preparation again validates all **1,401** original catalog modules without changing execution/layout contracts. Native PE/ELF packages expose all **35 ABI-1 exports**. Windows Debug/Release each pass three CTests; Linux Debug/Release cross-build. Managed Debug/Release and installed Pulsar Legacy/Interim source compilation pass for the updated **72-source** plugin.

Live stock-world run `4591ea24dd6542d58bbd4c734d7b10f5` reaches the ready Creative fixture, captures moving astronaut/HUD/LCD/animation views, exercises in-world Display changes, unloads and exits orderly. It presents **10,074 frames**, reports zero validation errors and zero actors/instances/models/meshes after unload. The observer records no managed SharpDX D3D11 creation calls. A packaging verification run caught an outdated net48 build paired with the new supplemental metadata layout; the build was refreshed, metadata is addressed by its own hash, and selected shader/ABI assets now preflight before activation. The failed run is not acceptance evidence.

Refreshed Creative run `8379118d53634d5b8e817d4919d6ec2f` verifies the prepackaged supplement and early preflight with the production counters visible in Pulsar. It observes **6,462 stock world frames / 339,553 material draws / 2,922,484 instances / 86,014 skinned draws / 19,301 lighting passes**. **5,164 upload batches** require only **one outstanding-slot wait**; cumulative upload bytes are not resident memory. The astronaut travels **12.16 m**, six PNGs are delivered, and in-world Display Cancel/OK, small-grid deletion and scene unload complete. The final **10,085 presented frames** have zero validation errors and zero scene actors/instances/models/meshes after unload. The D3D11 observer remains ready before initialization and records zero managed creation attempts. Shader conversion remains Ready while observed stock world integration is Active; visual comparison remains separate.

Interim/CoreCLR 10.0.11 run `26bd10f935dc4ce79380ee115ad6ab88` initializes all forty material/depth pipelines and stock lighting from the packaged assets, presents **7,101 frames / 5,057,764 sprite draws**, captures native menu/Display views, verifies Cancel, exits **0** and clears the healthy journal. It reports zero validation errors. Both normal Pulsar deployment folders were refreshed from the final Release builds and packaged assets.

Stock lighting currently uses a neutral diffuse ambient cube, conservative point-light tile lists and unshadowed spotlights with a neutral reflector mask. Prefiltered specular probes/BRDF resources, full stock cascade filtering, GPU tile culling, advanced transparency and other visual systems remain explicit parity work. Native Linux execution remains unverified on this Windows host.

## Reproduction

Run from the repository in PowerShell, with the installed Bin64 and Pulsar directories substituted:

```powershell
dotnet build Vulkan.sln -c Debug -p:RunPostBuildEvent=Never
dotnet build Vulkan.sln -c Release
./scripts/build-native.ps1 -Configuration Release -Test -Deploy
./scripts/build-native.ps1 -CrossLinux -Configuration Release -Deploy
./scripts/build-linux-tools.ps1 -Deploy
python ./scripts/verify-native-assets.py

$env:VK_LAYER_PATH = "$PWD/Native/build/validation/Bin"
$env:VK_LAYER_ENABLES = 'VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
dotnet run --project Tests/BootstrapTests -c Release -- '<Bin64>/SpaceEngineers.exe'
dotnet run --project Tests/RenderProtocolTests -c Release
dotnet run --project Tests/InteropTests -c Release -- '<Bin64>'
dotnet run --project Tests/SceneContentSmoke -c Release -- '<Bin64>'
./ClientPlugin/Rendering/Content/Tests/Test-DdsReader.ps1
dotnet run --project Tests/WorldGpuSmoke -c Release -- '<Bin64>'
dotnet run --project Tests/ShaderCompilerTests -c Release -- 'Assets/Tools/win-x64' '<Content>/Shaders'
./scripts/build-shader-suite.ps1 -GameBin '<Bin64>' -Package
dotnet run --project Tests/ShaderLibraryTests -c Release -- 'Assets/Shaders/Vanilla' '<Content>/Shaders' '<Bin64>/VRage.Render11.dll'
dotnet run --project Tests/RuntimeShaderTests -c Release -- 'Assets/Shaders/Vanilla/Modules' 'Native/build/_deps/spirv_tools-build/tools/Release/spirv-val.exe' 'TestResults/RuntimeShaderTests'
dotnet run --project Tests/GBufferGpuSmoke -c Release -p:RunPostBuildEvent=Never -- '<Bin64>'
dotnet run --project Tests/PulsarSourceValidation -c Release -- "$PWD" '<Bin64>' '<Pulsar>'
./Tests/WindowsStartupHarness/Run-Isolated.ps1 -Bin64 '<Bin64>' -Pulsar '<Pulsar>'

./Tests/CreativeFixtureHarness/Run-Isolated.ps1 -Bin64 '<Bin64>' -Pulsar '<Pulsar>' -ObserveD3D11 -ValidationLayerPath "$PWD/Native/build/validation/Bin"
# Use the resulting marked initial snapshot for the matching DX11 run:
./Tests/CreativeFixtureHarness/Run-Isolated.ps1 -Bin64 '<Bin64>' -Pulsar '<Pulsar>' -Backend Dx11 -ObserveD3D11 -LoadFixtureWorld '<marked initial snapshot>'
```

The message registry inventories all **147** kinds. Source-backed handler presence, frame observations and visual verification are distinct. Effects and partial semantics remain visible; no empty handler or baseline no-op establishes a completed visual system.

## Phase #2 asteroid evidence (2026-10-05)

Windows RTX 5070 Ti, loader 1.4.341/device 1.4.351, game 1.210.014:

- `TestResults/VoxelGpuTests/result.json`: **36 checks**, nine frames, eight stock voxel draws, four upload batches, zero core/synchronization validation errors. Tests include mixed BCn/RGBA array normalization, stock empty-channel defaults, alpha/sRGB retention, invalid mip rejection and handle-independent mesh accounting.
- `TestResults/WorldPassGpuTests/result.json`: **61 checks**, zero validation errors after adding four voxel pipelines. Unused vertex-output interface warnings remain distinct from errors.
- `voxel-native-debug-final.log` / `voxel-native-release-final.log`: Windows Debug/Release, all three native CTests pass in each. `voxel-linux-debug-final.log` / `voxel-linux-final.log`: Debug/Release ELF cross-builds pass; no Linux runtime tests were run.
- `voxel-managed-debug-final.log` / `voxel-managed-release-final.log`: both managed targets build. `voxel-pulsar-final.log`: installed Pulsar parses the manifest and compiles all 77 plugin source files for Legacy/CLR and Interim/CoreCLR. `voxel-native-assets-final.log`: both x64 PE/ELF libraries expose all 40 required ABI exports. Final deployment hashes are in `Native-FinalAssets.json`.
- `CreativeFixture/979edbda0edb4f71989a9807c99ce3c1`: initial completed asteroid run; 11,881 observed voxel frames, 301,777 draws, 66 mesh updates, zero validation errors and zero voxel meshes/arrays/groups/resident bytes after unload. The mined hole persisted across saved-storage reload; the initial camera showed an unlit silhouette.
- `CreativeFixture/e3ec7277bc9b4b8a9dbdc318017bd2ae`: completed sun-facing run; 9,847 voxel frames, 254,835 draws, 77 mesh updates, zero validation errors and zero voxel residency after unload. Actual game clipmaps became ready, near/middle/far camera changes drove replacement meshes, stock mining changed storage and a saved `.vx2` recreated the actor. The before/after captures show textured surface and a newly opened hole; reload preserves that hole. All fixture completion/unload/disposal markers exist. The SharpDX observer was ready before initialization and recorded zero device creation attempts; its scope excludes native overlays.

Failed development runs are retained as diagnostics, not acceptance: mixed DDS dimensions/mips first exposed incompatible raw arrays, then intentionally empty stock texture paths exposed missing default patterns. GPU normalization and exact stock defaults addressed those failures. The private EmptyWorld scenario also supplied an undefined sun vector; the harness now assigns a finite direction before sun-facing views. `aa438d25217e4ed5becf262f190472c5` is the failed camera experiment and is excluded.

Saved-storage reload is not full-world restart acceptance. Timed LOD fades, higher quality atlas tiers, broad mod/material parity, full visual parity and live Linux remain pending. See `Voxels.md` for ownership and memory-budget scope.

Matching DX11 comparison `CreativeFixture/b879bee78ac9458895d776ca2a20f477` loaded the marked initial snapshot from the sun-facing Vulkan run and completed the same stock mining, stored-voxel reload, in-world Display checks and world unload. Before/after/reload captures were reviewed: corresponding asteroid silhouette, triplanar surface placement and the same cut opening are present. Vulkan's fixed-exposure surface is brighter; skybox orientation/postprocessing also differ. This establishes controlled geometry/mining correspondence, not complete material/lighting/sky parity. The earlier DX11 attempt `0ed6ca17fa964c4d90514af662430cb3` was stopped after no startup progress; the retry reached the game after a long startup wait. The retry ran without the D3D11 observer. Both comparison game processes have ended.

`TestResults/VoxelCaptureComparison.json` records only within-backend change in asteroid region `(200,100)-(445,355)`: mean RGB difference before/after mining is 16.165 Vulkan / 13.649 DX11, versus 0.474 / 0.015 after mining versus storage reload. These values support persistence, not cross-backend pixel parity. Near/middle/far requests in these historical runs were followed immediately by camera changes; their image labels are not exact camera evidence. Actual controller updates and counter history establish LOD activity. The harness now holds each camera for another half second after requesting its capture and rejects nonfinite cameras; that test-only scheduling refinement compiles and awaits its next live run.

## Phase #3 planet environments — 2026-10-05

The source-backed environment extension integrates nine authenticated stock stages: optical-depth LUT compute, HQ main-view atmosphere, cloud VS/PS, foliage VS and grass/rock GS/PS. Planet terrain retains the original clipmap/mesher and supplies stock distant-spherization constants. Infinite reversed main depth and far-projection voxel visibility preserve orbital rendering. Shared material pages replaced triple-specific duplication; visible sets select a 512/256/128/64-pixel tier within a 512 MiB normalized-array working budget. Source DDS, foliage, staging and deferred retirement are separate.

Managed Debug/Release builds pass net48/net10 with zero warnings/errors. Installed Pulsar source compilation passes all 81 sources on Legacy (382,976 assembly bytes) and Interim (385,536 bytes). Both final local deployments match their Release DLL/manifest and all 2881 asset files (`TestResults/PlanetDeployment.json`). Native Windows Debug/Release builds each pass all three CTests; Linux Debug/Release cross-builds pass. PE/ELF packaging inspection authenticates all **45 ABI-1 exports** on both platforms. Eight required feature bits (0xC3F), including geometryShader and shaderStorageImageExtendedFormats, are queried and explicitly enabled; mock preflight rejects each missing bit and accepts optional-feature absence. Native CTest availability is distinct from validation-enabled GPU/game evidence. Live Linux runtime/headless/X11/Wayland acceptance remains pending.

`Tests/EnvironmentGpuTests` passes **35 checks**, with stock sky scattering, cached/recomputed LUTs, cloud blending/depth, fog, grass/pebble geometry, wind/time, alpha cutouts, persistent deterministic CPU point distribution, managed atmosphere enable/disable and LUT cleanup. `Tests/VoxelGpuTests` passes **42 checks**, adding actual rasterized distant spherization, copied foliage definitions and a shared 22-material page that selects 256-pixel arrays within budget. Both have **zero core/synchronization validation errors**. `WorldPassGpuTests` remains **61 passing checks** with zero errors (unused vertex-output warnings remain); bootstrap **36** and render protocol **17** also pass. Compilation and controlled GPU checks do not imply full visual parity.

Release-native Windows Creative planet run **30c8ac126b3f46d2acbc440301d0c58b** completed the deterministic 19-km EarthLike orbit/approach/surface fixture, real weather fog overrides, an 8-metre mining cut, save, atmosphere-height change, in-world Display transactions and unload. All six planet PNGs were delivered and reviewed. Final evidence reports **12,284 submitted/presented frames**, **2,532 voxel frames / 1,613,372 voxel draws / 409 mesh updates**, **2,534 atmosphere draws / 4,463 cloud draws / 4 LUT dispatches**, and **176,535 generated foliage points / 70,495 foliage draws / 75,452,833 submitted points**. Core/synchronization validation reports **zero errors**. After unload, actors, native meshes, voxel arrays/material groups/resident bytes, atmospheres/clouds and LUTs are all zero; UI/font textures remain resident by design. The healthy session journal is absent. The D3D11 observer was ready before render initialization and observed **zero managed SharpDX native device creation attempts**; native overlays/other libraries are outside its scope.

That live run used managed SHA-256 `75DE6E239503E4790CA22D3769D77C3A89867033BACA52832C98902E83D9DAEE` and Release native SHA-256 `475E906B0BA70A198E57456AAC92EE7233A7803CACCF7B52B83874C34D849A51`. Subsequent minor render-ID cleanup, foliage rotation-cache invalidation, atmosphere toggle and independent-cloud sentinel refinements pass final builds/source compilation and the GPU suites; the earlier live run does not establish their live verification. Final managed fingerprints are retained in `TestResults/PlanetAcceptance.json`.

Matching **DX11 run c96f0263ddca4924989c41988565de56** completed the same freshly generated seeded planet fixture, fog/mining/settings stages, Display checks and unload. All six held planet camera coordinates match exactly. Reviewed captures show corresponding planet silhouette, terrain/tree placement, grass and the same mined opening. Fog changes both backends; the atmosphere-settings capture and native LUT invalidation evidence are retained. **Vulkan terrain, clouds and sky are visibly washed out relative to DX11**, and grass coverage/quality differs. The current lighting/fixed exposure, atmospheric probes, quality paths and later postprocessing need visual parity work; no pixel-equivalence claim is made.

Failed development run `b37e00714c8b461ebf6ef24682dee763` exposed triple-specific atlas duplication exceeding the visible budget during surface approach. It is retained as failure evidence; shared pages and adaptive tiers address it. Earlier successful Debug-native run `7da4ee489832427d876df3293cfc1956` also completed the six-stage planet fixture with zero validation errors; Release-native evidence above is the recorded acceptance run.

The **147-message registry now contains 66 handlers, nine baseline no-ops, eight outbound responses and 64 Pending entries**. Partial planet/LOD/graphics/postprocess settings remain Pending, and no empty handler is counted as a visual system. Precipitation awaits Phase #4 GPU particles. Atmospheric probes, adaptive exposure, atmosphere quality/MSAA, timed voxel crossfades, user quality mapping, broad mod/multiple-planet acceptance, saved-planet/full-world reload and live Linux acceptance remain open. `Docs/Planets.md`, `ShaderPassCoverage.json` and `TestResults/PlanetAcceptance.json` preserve this scope.

## Phase #4 transparency and particles — 2026-10-05

The native backend now integrates stock glass and dedicated Holo materials, five geometry streams each, ten depth/normal partners, weighted OIT resolve, 32 billboard pairs, five decal material roles and ten GPU-particle stages. Fourteen glass/Holo combinations join the eight world combinations in a **44-stage authenticated supplement**. The complete installed catalog remains **20736 validated variants / 1401 modules**; all catalog records load with checked source/assembly/module/layout hashes. All 1401 driver-facing modules validate for Vulkan 1.4 after reflection-annotation removal and vertex descriptor relocation; the relocation test confirms only binding operands change. No runtime shader compiler is needed.

Managed Debug/Release net48/net10 builds pass. Pulsar source compilation validates the manifest/runtime/platform gates and all **92 sources**, producing Legacy **442880 bytes** and Interim **445440 bytes**. Native Windows Debug/Release each pass all three CTests; Linux Debug/Release cross-builds pass. Packaging validates x64 PE/ELF and all **56 ABI-1 exports**. The required mask is **0x3C3F**, adding explicitly queried/enabled independent blending and robust buffer access. Mock device preflight rejects each required feature's absence; optional features remain optional. A final startup export guard rejects a missing particle export before activation; this later guard has unit/source/build evidence separate from the game snapshot below.

`TransparencyGpuTests` passes **149 checks** with zero core/synchronization validation errors: actual stock glass and hologram pixels, rigid/generic agreement, OIT order independence, opaque depth occlusion, resize, all 32 billboard combinations, BC4 channel replication, decal roles/fade/glass layers, asynchronous flare visibility, GPU emission/simulation/indirect pixels, expiry, slot reuse and reset. `ParticleBridgeTests` passes **10 checks** for complete/missing native exports, valid/changed installed particle hook IL, source flare curves, exact-type API-neutral particle lights and copied decal ownership. Regression suites remain world **61**, voxel **42**, environment **35**, bootstrap **36** and render protocol **17**, with zero GPU validation errors. Stock shader interface-output warnings remain distinct from errors.

Release-native capture run **a8181057d81646788597e54f6a1fe0e0** completed the Phase #4 private Creative fixture, Display transactions, five transparency/effect PNGs, particle expiry, decal removal and world unload. Recorded totals: **49128 glass draws**, **12733 OIT resolves**, **12115 decal draws**, **33366 glass-layer draws**, **4423 flare depth queries / 8834 flare billboards**, **7867 GPU-particle frames / 19202 dispatches / 7867 indirect draws**. Completed-slot history observed up to **1205 alive particles**, then **0 alive / 409600 dead / zero indirect indices**. Final actors/instances/models and native meshes are zero. Validation was enabled and reported **zero core/synchronization errors**. The SharpDX observer was ready before initialization and observed **zero managed native device creation attempts**; overlays and other libraries are outside its scope.

Four native RenderDoc captures were delivered, including electrical-particle and rain stages. The electrical frame's converted command trace contains **563 vkCmdDrawIndexed**, **266 vkCmdDraw**, **4 vkCmdDispatch**, **one vkCmdDispatchIndirect**, **one vkCmdDrawIndexedIndirect**, **17 dynamic rendering scopes** and **one vkQueueSubmit2**. `TestResults/TransparencyNativeCommands.json` records captured command chunks, not replay or shader-level pixel-equivalence verification. Snapshot managed SHA-256 is `4B3B470DA3B0C88B13C3AC44674B154A7B7690BB1B19B50C5F37C3948B425FE4`, native SHA-256 `827F1D3850285067E3B9536A4FC61E9D5053D646A1B50D1603E76CCB6D9F27DC`. This run predates the dedicated Holo extension; that extension has subsequent GPU/build/source evidence and a separate final Creative run.

Matching **DX11 comparison 5e63d58eab9b4662ad878ce457429f7e** completed the same window/decal/electrical/rain/snow fixture, Display transactions and world unload. All five capture-request camera coordinates match. Reviewed captures show corresponding window/grid placement and glass silhouettes; the electrical effect is visible in Vulkan. Weather captures are sparse on both backends and do not establish precipitation pixel parity. Vulkan glass reflections, sky/lighting and LCD composition differ from DX11. The probe/BRDF/fixed-exposure and later postprocessing dependencies remain visible; complete glass/decal/particle/flare visual parity is not claimed. DX11 Legacy uses Pulsar's forced exit and has exit/unload markers instead of a harness-disposal marker. Vulkan retains graceful disposal evidence.

Failed development run **648402e1286846fc8d8f7be165cb59c0** correctly recovered to DX11 when cross-runtime floating-point formatting produced different particle-light IL fingerprints. Exact IEEE-bit operands resolve the mismatch while preserving the original startup-hook fingerprint. Earlier successful Debug run **93d3ef65a7804af38b68c7a6220f469f** completed electrical/weather emission and cleanup, but immediate camera changes made its first captures unsuitable for matching-view comparison. Those runs are retained separately, and are not the final comparison evidence.

The source-backed **147-message registry contains 76 handlers, nine baseline no-ops, eight outbound responses and 54 pending entries**. Partial settings stay pending. Complex glass ordering, exact vanilla flare query scheduling/smoothing, probe reflections, particle lighting/collision/quality, weather placement/quality, LDR/PostPP/custom-projection billboards, content mods, live Interim and native Linux remain open visual/runtime acceptance work. Implementation contracts are in `Transparency.md`; the artifact verifier and `TestResults/TransparencyAcceptance.json` keep counters, markers, immutable run hashes and scope separate from manual visual review.

Final dedicated-Holo Release run **506ffa3bf82f42e385c76b51445e4d0a** also completed the held Phase #4 fixture, Display checks, expiry and world unload with **zero core/synchronization errors** and **zero observed managed D3D11 device attempts**. It recorded **55761 glass draws / 14121 OIT resolves / 13911 decal draws / 38684 glass-layer draws**, **5352 flare queries / 10692 flare billboards**, and **8596 particle frames / 20936 dispatches / 8596 indirect draws**, with **1251 peak observed alive**, then **0 alive / 409600 dead**. `TestResults/TransparencyFinalAcceptance.json` verifies all five matched capture-camera requests, PNG delivery and cleanup. Managed SHA-256 `2D3A22FDC9B549E6483E2E54EC3FC6ECDC820D154F0961E3BB8933FE5F0A8C5C`, Release native `6E6FDDCFCB43553D1B8DDDA0A04E14B3F75FC406C29990978B02A622663CADEF`. Its startup initialized all glass/Holo streams; actual dedicated Holo pixels are verified by the GPU fixture rather than an in-game Holo asset. The later export guard has separate unit/source/build validation. Final deployments and their full asset matches are recorded in `TestResults/TransparencyDeployment.json`; live Interim/Linux and the documented visual gaps remain pending.


## Phase #5: environment probes and postprocessing (2026-10-05)

The authenticated catalog remains **20,736 Vulkan 1.4-validated variants / 1,401 unique SPIR-V modules**. Production source now selects **316 stock catalog variants**, plus **128 supplemental material/forward/highlight stages**. These selection counts are not full-suite visual acceptance. The source-backed 147-message registry now reports **84 handlers, nine baseline no-ops, eight outbound responses and 46 pending entries**; partial settings remain pending.

`PostprocessGpuTests` passes **174 checks** with zero core/synchronization validation errors: actual exposure/bloom/tone/FXAA/chromatic/AO output; every HBAO and highlight blur permutation; fractional highlight radius rounding; eight-cascade quality/blur; spotlight cookies/depth/exclusions; completed visibility; independent DRS/output dimensions and timestamps; sustained overload/recovery/history-wrap/CPU-bubble heuristic behavior; LDR/PostPP/custom-projection pixels. `ProbeGpuTests` passes **98 checks** with zero errors: installed RG16F BRDF channels, near/far geometry, six-face publication, all GGX mips, replacement/resolution reset, atmosphere output and timeout/blend behavior. Results remain in `TestResults/{PostprocessGpuTests,ProbeGpuTests}/result.json`.

Regression checks also pass for world passes, transparency, voxels, environment and particle bridge. Windows native Debug/Release pass all three ABI/GPU/HWND CTests. Managed Debug/Release targets and installed Pulsar source compilation for Legacy/CLR and Interim/CoreCLR pass. Linux Debug/Release libraries cross-compile; the PE/ELF verifier authenticates all **80 ABI exports** and packaged Linux tools. Live Interim/Linux/VR acceptance remains pending. Logs use the `TestResults/phase5-final-*` prefix.

The first successful Release-native Creative run **98c7817435464970a04247354ec998f9** completed menu/world Display transactions, highlights, HUD/LCD/animation captures, walking, save and unload. It observed **5,954 submitted/presented frames**, **1,568 probe faces / 28,188 GGX passes**, **2,338 exposure/histogram/tone passes**, **21,042 bloom passes**, **51,436 HBAO passes**, **255,310 spot depth draws**, **28,848 completed box queries**, and **zero core/synchronization validation errors and zero observed managed SharpDX D3D11 device attempts**. SSAO in that run remained zero because the game's per-frame sector settings overwrote a one-shot fixture message; the corrected fixture persists the sector settings and has separate final evidence. Managed SHA-256 `38C93A4CA598469968D3502DE518A1BA971931B8E096E2BD76EDA85DB34F4840`; native `41EE57A91543C2B89577D9674DA5A3D3F46C132107F3E5330E55DB31EA8E5E0C`.

Earlier failed development runs are retained and excluded from acceptance. Loading supplied an unset/nonfinite sun vector; the renderer now guards environment direction and invalid camera projections before fitting cascades. Core validation also caught unmatched lit LDR varyings and an overly broad mutable sRGB view usage; both were corrected before the passing fixture results.

Full cross-backend visual parity is not established. Usage-based cascade updates, small-object pruning/full spatial hierarchy, stock cascade extrusion details, GPU tiled light culling beyond nearest-64 lights, finer probe filtering cadence, broad-world performance, native stereo and external temporal/vendor upscalers remain further work. Native media/audio, advanced capture/debug/profiling and complete graphics settings are Phase #6.

The pre-skybox-correction managed/native Release run **8d65b68a5c654e25bd3cc0749a5b0f08** completed the corrected SSAO/HBAO fixture and graceful unload with zero validation errors and zero observed managed D3D11 attempts. It recorded **423 SSAO passes / 38,283 HBAO passes**, **1,814 histogram/exposure/tone/CSM passes**, **16,326 bloom passes**, **1,469 probe faces / 26,352 GGX passes**, **195,795 spot depth draws**, and **21,516 completed box queries**. Dynamic actors invalidate hidden results conservatively; this fixture reports zero scene culls even though hidden GPU results occur. Static hidden/front/near behavior is independently verified by the GPU fixture. `TestResults/PostprocessingAcceptance.json` verifies completion markers, PNG dimensions, counters, asset hashes, Display tests, D3D11 observation and unload.

Two RenderDoc captures were delivered. The second frame contains **188 Vulkan dynamic-rendering passes**, **1,017 indexed draws**, **473 draws**, **four compute dispatches**, **18 occlusion queries**, **two timestamp writes**, and **one synchronization2 queue submission**. `TestResults/PostprocessingNativeCommands.json` records actual captured command chunks; this is not replay or shader-level visual parity acceptance. That captured managed SHA-256 is `FB4ED85C3EF6178FAC394F4355B7AD399EA68AC5F2512CD1A9FE80DDB9887DCC`; native `2C90BD76FAEDF01F20A6D6D9BCCEB895A2E3EE985EF3AEB834B1DEE27BC17948`. `TestResults/PostprocessingDeployment.json` authenticates **3,037 matching packaged files** in each local Legacy/net48 and Interim/net10 deployment.

The held DX11 comparison run **88ef330f07c247f5b787a57740f07e76** completed the same fixture and both Phase #5 capture requests. Geometry/HUD/LCD placement are recognizable, but world/LCD brightness differs. The initial skybox mismatch was traced to vanilla's exceptional background matrix storage under the Vulkan shader build's `-Zpr` convention. A colored-cubemap regression failed before the correction and passes for both rotation directions afterward; `WorldPassGpuTests` now reports **63 checks**, with zero validation errors. Probe regressions remain **98 passing checks**. The final corrected source/deployment has separate live evidence below. Full visual parity remains pending.

Final corrected Creative run **74c1d1d8494b498bb2c67bb8903e5f77** completed Display transactions, all eight captures, world save/unload and graceful disposal with **zero Vulkan core/synchronization errors and zero observed managed D3D11 device attempts**. It recorded **519 SSAO / 52,085 HBAO passes**, **2,449 exposure/histogram/tone/CSM frames**, **22,041 bloom passes**, **1,590 probe faces / 28,620 GGX passes**, and **2,449 completed timestamp samples**. Both Phase #5 camera requests match the DX11 fixture. Manual review confirms the skybox orientation correction and recognizable geometry/HUD/LCD placement; world/LCD brightness still differs. `PostprocessingAcceptance.json` is the final fixture receipt. Managed SHA-256 `2CFC3907082C28B939E397E0FBDAB6B1665751DAC2317839826BE19C86FB1D8A`; native `2C90BD76FAEDF01F20A6D6D9BCCEB895A2E3EE985EF3AEB834B1DEE27BC17948`. Full visual parity and the culling/cadence/host limits above remain open.


## DotNetCompat compatibility gate correction (2026-10-05)

The installed Pulsar Interim DotNetCompat DLL references `VRage.Render11` but declares its renderer-independent compatibility patches in `Shared.Patches.ImageProcessing` and `Shared.Patches.Serialization`. The previous exemption mistakenly expected `ClientPlugin.Patches`, so a valid required runtime core was reported as a graphics conflict before Vulkan preflight. The contract now checks the actual shared patch types, the constant `ClientPlugin.Plugin.Name`, the preloader and the exact current/legacy metadata identities. It does not exempt arbitrary Render11 consumers.

Bootstrap tests reproduce the old failure against the installed cached DLL and pass the corrected full inventory gate. They also verify unrelated metadata, missing/wrong patch namespaces and a separate Render11 consumer still fail. **43 bootstrap checks pass** with the installed game and compatibility core. Managed Debug/Release and both installed Pulsar source compilers pass; Release assemblies are deployed to local Legacy/net48 and Interim/net10 folders with matching hashes recorded in `TestResults/DotNetCompatFix.json`. A game restart is required to load the updated assembly; no running process was modified or restarted.

## Native media implementation — 2026-10-14

The five video messages and both GUI state queries now use a private libmpv
0.41.0 / FFmpeg 8.0.1 software runtime. Windows and Linux packages include their
loader closure, build receipts, source hashes and license notices. Package
verification passes for both x64 architectures and the Linux glibc 2.34
baseline. The Linux library and media test executables cross-build; Linux
runtime execution has not occurred on this Windows-only host. The media CI
recipe builds the locked packages and exercises the generated WMV fixture in
a Linux container; that workflow has not been submitted or executed here.

Release net48/net10 builds pass. **34 production protocol/geometry/sprite
checks pass**, covering asynchronous loading, pooled ownership, skipped and
retained updates, volume during opening, replacement/early close, EOF,
terminal failure and shutdown, all fit modes for wide/tall media, channel
tint, clipping, scaling, IgnoreBounds, ordinary-sprite order and target
batches. Windows native CTest has **15 passes and one unrelated
`gpu_opaque_reference` skip**. The media GPU fixture alternates 100 streamed
BGRA frames, checks actual readback channels and stable image count, and
checks dimensions/format replacement, render-target resize and resource
removal with submissions in flight under synchronization validation.

The Windows bundled runtime test passes with the generated 160x96 WMV/audio
fixture: **26 observed frames**, natural EOF, volume changes, opaque alpha,
top-down orientation, channels, a retained buffer protected while newer
frames publish, 20 close/replacement operations, and asynchronous missing-file
failure. The generated fixture is owned test data; installed game videos are
not redistributed. `Native/build-media/final-native-fixture.log` and
`final-gpu-media-tests.log` retain these results.

Interim menu run **4cb68552389243adae52adb03399b9d1** passed the 600-second
overall soak with **34,661 observed frames**, **0.9968 source-cadence ratio**,
**32,859 us maximum reported A/V offset**, **571 real audio samples**,
**-2,260,992 private-byte delta after warmup**, and **one texture of variation**.
It exited 0, disposed the harness/renderer, cleared its private startup
journal, and reported **zero Vulkan core/synchronization errors**. Its source
CSV establishes about 594 seconds of actual menu observation, rather than a
full 600-second menu-only interval. The final menu-only gate is recorded
separately below. This launcher initially disabled logos; this run proves
menu playback, not KSH intro completion.

Legacy natural-intro run **54d08bea4cef47bcb9de9cedd94de4fc** passed 150 seconds
with the stock KSH intro chain explicitly restored in the isolated harness.
`KSH.wmv` progressed through its 20.483-second duration, the screen's Closed
event reported **Cancelled=False**, and background videos resumed. Results:
**8,230 observed frames**, **1.0002 measured cadence ratio** (sampling jitter),
**9,855 us maximum reported A/V offset**, **134 real audio samples**, zero
Vulkan validation errors, exit 0 and cleared startup journal. Immutable
managed/native/runtime hashes and per-player counters are authenticated by
each run's `Reports/media-receipt.json` using `scripts/verify-media-run.py`.

Failed and superseded runs remain separate evidence. Scalar conversion run
`d7f1abd168f14edb892dcd8ce519a6a7` failed the 100-ms A/V gate at 141,640 us.
Runs `e789788d2e8b4175a8d44159c4390122` and
`eeea6040e2444c8e989e8a89ae249c37` exposed undefined ASF average-rate metadata
and an unusable 1,000-Hz declared timestamp grid; packet timing now measures
the affected background at approximately 60 fps. The latter run was stopped
before acceptance. Run `fc93740c73f7419f88a5487d4770c60a` ended with code -1
during a Legacy display transaction and has no disposal or acceptance marker;
its root `timeout.txt` confirms harness timeout and it is excluded from acceptance.
Interim reproduction `23fb301dfe7b47caa90c8b9136e6c45b` was stopped after
`Native/build-media/resize-stall-stacks.txt` located the main thread in the
test harness's `DismissRestartPrompts` loop, while the render thread continued
presenting. Deferred removal kept returning the same closed dialog. Closing
a snapshot and filtering closed confirmation dialogs fixes that harness bug;
subsequent live resolution checks pass. Enabling the stock
intro in the harness also corrected the earlier mistaken assumption that
menu-only footage established KSH behavior.

Software-output owner CPU and upload CPU counters are observations, not total
FFmpeg filter cost or GPU transfer duration. The upload label measures elapsed
CPU-side call time, including any staging/scheduler wait. In the first passing Interim
soak, sampled per-player totals cover 32,046 frames and report approximately
**2,123 us output-owner CPU/frame**, **1,248 us upload CPU/frame**, and **128
drops**; periodic reports omit some player tails. The CPU-only probe additionally
records whole-process CPU cost in `TestResults/MediaConversionFinal.json`.
No acoustic A/V measurement or audible fade-fidelity assertion is made.

Cross-platform runtime parity remains open until live Linux video/audio,
looping, minimize/restore and orderly shutdown pass. Matched DX11 media
timestamps/pixels, physical skip-input delivery, audible fade fidelity and
GPU-specific upload timing also need separate evidence. Implementation,
threading, source/packaging and repeatable checks are in `NativeMedia.md`.

Final Interim run **05884f7f95f3470390bf928e3233bbb1** passed **630 seconds**,
including **614.95 seconds of continuous menu observation after KSH**. The
observer invoked the stock Canceling action after three seconds of KSH; the
screen reported **Cancelled=True**, its video ID became invalid, and a new
menu ID progressed. Actual Display callbacks rejected, accepted and restored
the resolution during playback. Results: **36,362 observed frames**, **0.9976
source-cadence ratio**, **55,183 us maximum reported A/V offset**, **601 real
audio samples**, **-651,460,608 private-byte delta after warmup**, and **one
texture of variation**. Negative memory growth reflects managed/cache cleanup;
it is not a per-player allocation measurement. Validation reported zero errors;
the process exited 0 and cleared its startup journal. Managed SHA-256
`39030F290D93457B1D3DF18EA753C885F821DA453C15BD1970E64C7BDECF43FC`, native
`93CEC18650318732A1333D684BDC487E42AC90F003AFDBE7A5BC98AB33632944`, runtime
manifest `E3B0489DBE186D16E297A0C7EAE30C97A22AB09A094FD31BDA153E77B90D7EA8`.

Final Legacy run **e054b9369d0e4e6ab3a9923c4c122e59** passed the stock KSH
natural-completion chain and the same corrected Display transaction sequence
in **150 seconds**. It observed **8,219 frames**, **1.0001 cadence ratio**,
**10,229 us maximum reported A/V offset**, **134 real audio samples**, no texture
count variation after warmup, zero validation errors, exit 0 and a cleared
startup journal. Both final receipts authenticate exact snapshots. This host
uses an **NVIDIA GeForce RTX 5070 Ti**, Vulkan device API **1.4.351**.

The additional decoder-to-GPU fixture passes **30 actual decoded WMV frames**,
changing readback pixels, top-down orientation and opaque GPU alpha with zero
validation errors. It uses the same native media publication and streaming
upload APIs as production, rather than treating synthetic texture colors as
decoder integration proof. `Native/build-media/final-decoded-gpu.log` records
the result; CMake registers `media_decoded_gpu` when the runtime and generated
fixture paths are provided. Its Linux executable cross-builds, but execution
still requires a Linux GPU host.

## Main-menu Keen logo palette decoding, 2026-10-16

The main menu's `MyGuiConstants.TEXTURE_KEEN_LOGO` points to
`Textures/GUI/KeenLogo.dds`, a 512x256 legacy P8 image with ten mip levels.
The reader previously interpreted its palette and indices as uncompressed
channel data, producing opaque black. The stock DDSHelper consumes the
1024-byte RGBA palette once, expands each index, then the GUI texture path
premultiplies the expanded pixels. The Vulkan reader now follows that order.
The indexed-format contract is also documented in
[Microsoft DirectXTex](https://github.com/microsoft/DirectXTex/blob/main/DirectXTex/DirectXTexDDS.cpp).

`SpritePacketTests` reproduced the failure before the fix and now passes
**51 checks**, including byte-for-byte expansion of every installed logo mip,
GUI/world cache separation, stock alpha preprocessing, the main sprite packet
and truncated-palette rejection. `SpriteGpuTests` renders the installed asset
through the production cache and sprite path: all **131,072 pixels** agree with
an independent palette/preprocessing/blend reference within **one byte value**.
The fixture includes 81,260 transparent, 17,642 opaque and 32,170 partial-alpha
pixels. Existing font/base/mip-two and DXT4 checks also pass, with zero
core/synchronization validation errors. Evidence: `TestResults/KeenLogoGpu`.
Set `VK_LAYER_PATH` to `Native/build/validation/Bin` for the GPU test on this host.
Release net48 and net10 builds pass.

The isolated Interim startup run
`TestResults/WindowsStartup/05170408cb3f4e35866f166f60007d70` captures the
actual main menu with the logo visible at the upper right (`Reports/menu-native.png`).
The older Legacy menu capture in run `e054b9369d0e4e6ab3a9923c4c122e59`
shows the opaque black rectangle at that location; these use the same menu draw
path but different launcher runtimes and background-video frames. The fixed
Interim run completed its Display draft/cancel sequence, exited 0 and reported
zero validation errors on the RTX 5070 Ti. Linux live verification remains separate.

## Hardware ray sun visibility, 2026-10-15

The repaired pass supplies opaque sun occlusion to stock directional lighting.
`Tests/RayShadowGpuTests` passes 116 checks on the RTX 5070 Ti, including four
rotations, rigid/generic streams, actual receiver shadows, exact unblocked pixels,
over-budget native fallback, disabled shadows, both pending frame slots and clean
cleanup. Core/synchronization validation reports zero errors. The existing world
GPU suite passes 471 checks. An isolated Pertam first-person full turn and reverse
return produced 49 clean captures with SE-RT and Hardware Acceleration enabled,
678 BLAS builds, 330 ray dispatches, 344 budget refusals and zero validation errors.
Evidence is under TestResults/RayShadowGpuTests and the ScenePerformance fixture;
see Docs/HardwareRayShadows.md for scope. Native fallback and basic shadow behavior
are accepted here; this does not accept full hardware GI, reflections or software
RT reconstruction parity.
