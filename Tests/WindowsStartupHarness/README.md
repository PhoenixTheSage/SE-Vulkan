# Isolated Windows startup and renderer selection checks

This test-only Pulsar plugin exercises the real game screens and render proxy.
It has no Render11 or SharpDX assembly dependency. It runs only when the launch
environment, actual game user-data path, and `.se-vulkan-startup` marker agree.
The launchers reject an already-running game/Pulsar process and retain all logs
and captures under `TestResults/WindowsStartup/<guid>`.

Build `ClientPlugin` in Release before launching. The managed assembly and
current `Assets` bundle must be compatible; avoid snapshotting during a native
ABI change. Both launchers copy managed/native assets into the private portable
Pulsar tree and record their hashes. Existing game data and Pulsar profiles are
not edited. Fresh test data opts out of GDPR consent and closes the known
welcome/consent screens.

```powershell
& Tests/WindowsStartupHarness/Run-Isolated.ps1 `
  -Bin64 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64' `
  -Pulsar 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Pulsar' `
  -Edition Interim -DurationSeconds 150 `
  -TestDisplayTransactions -TestResolutionTimeout
```

`-Edition Legacy` uses net48; Interim uses net10.0 and an isolated copy of the
installed required `dotnet-compat` core. The validation layer is private to the
child process. `-ValidationLayerPath` overrides its default location under
`Native/build/validation/Bin`.

The normal menu smoke captures the actual backend, selects the alternate
renderer in Display, captures the draft, cancels, and verifies unchanged
persisted selection. `-TestDisplayTransactions` requires at least 90 seconds
and adds real renderer-only OK, native resolution confirmation, explicit
rejection/revert, accepted confirmation, and restoration. It ends requesting
DX11 while Vulkan remains active. `-TestResolutionTimeout` requires at least
150 seconds and lets the game's real 60-second confirmation expire; it also
opens the actual plugin configuration and presses Show Status, Write report to
log, and the healthy-session Retry button, captures the scrollable report, and
checks that retry preserves the active session journal.

Test both restart directions using the printed private run root:

```powershell
& Tests/WindowsStartupHarness/Run-Restart.ps1 `
  -RunRoot '<printed private run root>' `
  -Bin64 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64' `
  -ExpectedBackend Dx11 -CommitOppositeForNextRestart

& Tests/WindowsStartupHarness/Run-Restart.ps1 `
  -RunRoot '<same private run root>' `
  -Bin64 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64' `
  -ExpectedBackend Vulkan
```

The restart launcher reads the existing persisted selection and refuses to
change it. The DX11 run commits its next Vulkan choice through the real Display
OK handler. Pulsar 2.4.2 normally force-kills the DX11 process on ExitThreadSafe
with exit code -1; the Vulkan-scoped compatibility patch preserves orderly
renderer disposal, resulting in exit code 0 and removal of its healthy session
journal. An unexpected active Vulkan failure retains the journal and requires
explicit retry after a DX11 recovery launch.

Resolution-fill regression uses `-TestResolutionFill -DurationSeconds 105` in its own run. It selects 1920×1080, then 2560×1080 through the real Display controls in windowed, borderless, fullscreen and exclusive modes, checking both `MySandboxGame.ScreenSize` and the GUI fullscreen rectangle and saving Options-screen captures. It returns to windowed mode before selecting exclusive fullscreen so the native transition is actually exercised.

Add `-InstalledProfile '<Pulsar>/Interim/Profiles/Current.xml'` to run the installed launcher with a private copy of that full profile and private game data. All source registrations and companion plugins remain enabled. The resolution-fill observer has no compile-time Vulkan reference and finds diagnostics in the loaded source/DLL assembly. The runner adds a uniquely named temporary local capture plugin, verifies its target is beneath the installed Local directory, and moves it into the retained run evidence after the test process exits. The user's profile file and game settings are not changed. The default remains the private portable Vulkan-only launcher.

Resolution-fill evidence, 2026-10-14: the pre-fix run `535088f155d54dcfae926e4fed2f8daa` resized presentation but left the fresh profile's game/GUI at 640×480 in every mode, producing a small upper-left image. Installed Interim/source-profile run `b0ff8f01b0e546b5848d68f12b75b4aa` passed all six checks (1920 baseline, 2560 windowed/borderless/fullscreen, windowed reset, exclusive), produced full-fill Options PNGs, acquired exclusive fullscreen, and exited 0 with zero core/synchronization validation errors and a cleared startup journal. All companion plugins were enabled; native SHA-256 was `A19475F8D259108F489B863A4201CD11DEDF7ADF3D0D45D51E80D406851E1818`. Captures include HDR presentation converted to SDR PNG, not HDR color-parity proof. No gameplay world was loaded in this menu regression.

## Observed evidence, 2026-10-04

- Legacy native menu/Display Cancel: run
  `556216bf57a149f48b28d1cb0e2a4398`, exit 0, 6,861 presented frames, 18 font
  atlases, zero Vulkan validation errors. Its menu capture still had the fresh
  consent overlay; the later harness closes that private test screen.
- Interim native menu/Display Cancel: run
  `6f0adc4f60d84989bd242e0adc51e5a5`, CoreCLR 10.0.11, exit 0, 6,739 presented
  frames, 18 font atlases, zero errors, and a clear main menu capture.
- Real Display OK/rejection/acceptance/restore: run
  `0d0fce4dcb814abfa7776de9735e571e`, exit 0, 10,547 presented frames, zero
  errors. Its `Reports/display-transactions.txt` records each assertion.
- Vulkan → DX11 restart: that run's
  `Restarts/3206aaceb71d42c98e8350d8ce670806` records
  `VRageRender.MyDX11Render`, native backend not loaded, actual DX11 PNGs,
  canceled draft, and a GUI-committed Vulkan choice.
- DX11 → Vulkan restart:
  `Restarts/ee0c2555a57e465ead2bcd87df9fe725` records
  `ClientPlugin.Rendering.MyVulkanRender`, exit 0, 7,160 presents, zero errors,
  and healthy journal removal.
- Actual timeout and config controls: run
  `20f32274e2964045bdbc25d4449a4907`, exit 0, 17,133 presented frames,
  5,332,452 sprite draws, 18 fonts, zero core/synchronization validation errors.
  `Reports/display-timeout-passed.txt` records the game timer's unprompted
  rejection and native resolution revert. `config-controls.txt` records button
  discovery, marked log output, populated/scrollable status, and journal-safe
  healthy retry. All three config PNGs were inspected.

These control-flow checks used native SHA-256
`C02D696382B1C9726E8161DDF9BAA3B27081B3F626104F6E304A0AA1BFB5E37E`.
The timeout run's managed hash is
`8E8B7465D12FAFDFE7D30963B778A85A21C4BC3936F05A511E0CD7D1425B9181`.
Later native mip/normal-map changes require a matching rebuilt asset bundle.

These tests establish real startup, selection, recovery-journal, and config
controls. Creative-world visual acceptance, Linux live presentation, and full
rendering parity are tracked separately. Native glyph appearance differed
from DX11 at 640×480 in this bundle; the native font/mip work addresses that
separate visual gap.

## Native media checks

Add `-TestMedia -DurationSeconds 600` for a ten-minute overall intro/menu observation.
Run Legacy and Interim separately. The observer records source video, media
position, decoded/dropped frames, real audio backend, reported A/V offset,
private bytes and texture count. It checks source cadence within 2%, A/V
offset within 100 ms and resource growth after 120 seconds of warmup. Source
timing uses ffprobe average rates, or a five-second packet sample for ASF
files whose average rate is undefined. The ffprobe tool remains a build/test
dependency; it is not required for game playback.

`-TestMediaSkip` invokes the stock intro's `Canceling` action after two seconds
of KSH playback and checks that its ID becomes invalid before menu playback
progresses. It exercises the same action called by Space/Enter/mouse input;
physical input delivery remains a separate live check. Add
`-TestDisplayTransactions` to exercise resolution rejection, acceptance and
restoration while a video is active.
Natural KSH completion requires at least 120 seconds; the recorded Legacy
run uses 150. For a full ten-minute menu interval after skipping, use
`-TestMedia -TestMediaSkip -DurationSeconds 630` and verify the printed root
with `python scripts/verify-media-run.py <root> --menu-soak --skip`.

The reports include the exact runtime-manifest hash, per-second CSV and media
captures. Media assertion failures are recorded in `media-failed.txt` while
still allowing orderly renderer/worker shutdown; the runner rejects missing
`media-passed.txt`. Audible fade fidelity, matched DX11 timestamp pixels and
live Linux desktop/audio acceptance require separate evidence.
