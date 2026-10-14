# SE-Vulkan

Experimental native **Vulkan 1.4** renderer for Space Engineers 1.210.014. Windows supports Pulsar Legacy/net48 and Interim/net10; native Linux uses Interim/net10 and LinuxCompat SDL3.

The backend includes early selection/recovery, native mesh/material/light/shadow and sprite rendering, actor/instance/skinning bridges, DDS/MWM content, generated LCD targets, native capture and Release-build status. The controlled Windows Creative fixture has live simulation, rendering and capture evidence, including moving skinning and in-world renderer selection. Full visual parity, broad gameplay acceptance and live Linux verification remain pending. See [architecture](Docs/Architecture.md), [acceptance gate](Docs/Acceptance.md), [validation evidence](Docs/Validation.md) and the [147-message registry](Docs/RenderMessages.json).

Installed vanilla shader compilation is tracked separately from live pass integration. All **20,736** captured runtime variants across **118** entry-point files compile and validate for Vulkan 1.4, producing **1,401** deduplicated SPIR-V modules. Five obsolete, unreferenced sources are explicitly excluded. The [shader-suite inventory and build guide](Docs/ShaderSuite.md) covers stock permutations, runtime texture arrays/instancing, exact-hash compatibility fixes and reflected layouts/capabilities. Build its local catalog with `./scripts/build-shader-suite.ps1 -GameBin '<installed Bin64>' -Package`. The world uses selected stock passes with custom presentation/composition glue.

Phase #1 now renders the Creative world through **40 stock material/depth pipelines**, stock directional/point/spot lighting and native HDR/depth composition. GPU instance/deformation streams and bone palettes replace per-frame CPU geometry expansion; resource/LCD uploads use bounded asynchronous timeline batches and persistent frame slots. The world GPU suite passes **61 checks** with zero core/synchronization validation errors, and all **1,401** driver-facing catalog modules still validate. Supplemental shader combinations are packaged before startup. Pulsar reports observed world, instance, skinning, lighting and upload counters; see [pass coverage](Docs/ShaderPassCoverage.json). Specular probes, advanced effects and live Linux acceptance remain pending.

Phase #2 adds asteroid clipmaps, worker meshing, stock single/three-material triplanar shaders and depth partners, mining invalidation, LOD selection and saved-storage reload. This brings the stock material/depth count to **44**. Mixed DDS formats and sizes are normalized on the GPU into mipmapped arrays; geometry and arrays use the shared upload/retirement timeline. The private Windows asteroid fixture completed with zero core/synchronization validation errors and released its voxel resources on unload. See [voxel contracts and remaining quality work](Docs/Voxels.md).

Phase #3 adds planet streaming/spherization, stock atmosphere LUT/scattering, cloud shells, weather fog and persistent grass/pebble streams with stock GPU geometry expansion and wind. Shared material pages select bounded resolution tiers. The private EarthLike fixture completed orbit, surface, foliage, fog, mining and atmosphere-change captures with zero validation errors and clean resource unload. The environment GPU suite passes **35 checks** and voxel suite **42**. See [planet contracts and acceptance limits](Docs/Planets.md). Stock precipitation now uses the Phase #4 particle bridge; washed-out fixed-exposure lighting, environment probes, timed voxel fades, full quality behavior and live Linux acceptance remain pending.

Phase #4 adds stock glass and transparent-model streams, GBuffer/glass decals, weighted OIT, 32 billboard combinations, asynchronous depth-tested flares and GPU particles, including rain and snow effects. Particle CPU hooks are checked before activation, and diagnostics report observed draw/dispatch/lifetime evidence. See [transparency contracts and acceptance limits](Docs/Transparency.md). Probe reflections, complete flare scheduling and later composition/visual parity remain pending.

Phase #5 adds stock near/far GGX probes and BRDF reflections, atmosphere probe capture, quality-sized eight-cascade and spotlight shadows/cookies, asynchronous box occlusion, SSAO/HBAO, histogram exposure, bloom, tone mapping, highlights, FXAA, chromatic/vignette processing, LDR/PostPP/custom-projection billboards and Vulkan-timed dynamic resolution with bilinear upscale. [Implementation contracts and acceptance scope](Docs/Postprocessing.md) distinguish tested paths from remaining cadence/performance, cross-backend visual parity and unavailable native stereo work.

## Build and run

1. Configure the installed game/Pulsar paths in `Directory.Build.props.user` or use the preserved `setup.py` auto-detection. Open `Vulkan.sln`.
2. Build the native assets first: `./scripts/build-native.ps1 -Configuration Release -Test -Deploy`. C++20 Build Tools and CMake 3.24+ are required. Pinned Vulkan headers, volk, VMA, DXC, SPIRV-Tools and SPIRV-Reflect are resolved automatically; an installed Vulkan SDK is not required. Build/package media with `./scripts/build-media.ps1` (and `-CrossLinux` for Linux assets); [media prerequisites and checks](Docs/NativeMedia.md) include NASM for optimized CPU decoding/conversion.
3. Run `dotnet build Vulkan.sln -c Release`. On Windows this builds net48 and net10, then deploys each managed plugin, manifest and complete Assets tree to its Pulsar Local folder. Use `-p:RunPostBuildEvent=Never` for a build without deployment.
4. Use a separate Vulkan-compatible Pulsar profile. DX11-only graphics plugins still keep the launch on DX11. Plugins that publish the dual-renderer handshake can stay enabled; their DX11 device path stays closed while Vulkan is the renderer. Enable Vulkan, then select **Display → Renderer → Vulkan**, accept the settings and restart. DX11 is the default. Missing/unsupported assets, hooks, API/features or incompatible enabled plugins retain DX11 with a report.
5. Open the plugin config **Show Status** or **Write report to log**. After unexpected Vulkan termination, DX11 recovery preserves the requested choice; **Retry Vulkan next launch** acknowledges a retry. Validation is a next-restart option and availability is reported explicitly.

Renderer selection lives in `Storage/Vulkan.cfg` below the game's resolved user-data folder. The vanilla renderer key remains valid for plugin-disabled launches. Display Cancel and resolution rejection/timeouts discard the screen draft. Changing the backend never closes an active world.

For Pulsar source development, register the repository and `Vulkan.xml` using `-sources`. Source compilation also needs built native/shader assets before startup; set `SE_VULKAN_ASSET_DIR` to the repository's absolute `Assets` directory when the source cache location cannot resolve them. The normal local DLL deployment copies Assets beside the plugin before startup.

Linux builds use `cmake -S Native -B Native/build -DCMAKE_BUILD_TYPE=Release`, followed by `cmake --build Native/build --target se_vulkan_native sevk_smoke` and CTest. Package `libse_vulkan_native.so` in `Assets/Native/linux-x64`, SPIR-V in `Assets/Shaders/Compiled`, and compiler tools in `Assets/Tools/linux-x64`. The Linux CI job builds both configurations; live X11/Wayland game acceptance remains separate.

Windows cross-packaging uses `./scripts/build-native.ps1 -CrossLinux -Configuration Release -Deploy` followed by `./scripts/build-linux-tools.ps1 -Deploy`. Linux compiler tools retain `bin/dxc`, `bin/spirv-val` and `lib/*.so`; upstream DXC requires glibc 2.34. Startup uses precompiled shader assets. Run `python scripts/verify-native-assets.py` to inspect native architecture/ABI exports and record local asset hashes.

## Verification

Run `dotnet run --project Tests/BootstrapTests -c Release -- <game Bin64>/SpaceEngineers.exe`, `dotnet run --project Tests/RenderProtocolTests -c Release`, `dotnet run --project Tests/InteropTests -c Release -- <game Bin64 path>`, and the scene/content smoke project. Native CTest covers GPU pixels, resource lifetime, LCD/light/shadow passes and Windows HWND capture. Rebuild the registry against the current prepared game source with `./scripts/update-coverage.ps1 -GameCodeRoot <decompiled root>`.

Native media implements stock intro/menu playback, audio/fades and sprite composition through bundled cross-platform libmpv/FFmpeg. [Runtime build, ownership and acceptance](Docs/NativeMedia.md) distinguish implementation and package verification from live platform/visual evidence.

Known limitations and pending effects are listed in status and the coverage registry. The current adapter bridge exposes the first compatible Vulkan device; multi-adapter switching, complete graphics quality behavior, broad transparency/particle visual parity, advanced postprocessing, scaled/world-only capture and existing graphics-plugin ports remain future work.

Publication requires a real SE-Vulkan project commit and immutable native-asset hashes. The local manifest deliberately contains no placeholder commit or fabricated release hashes.

## Local workstation setup

### Plugin version

The plugin version lives in `Version.Build.props`, which **is** committed and imported by
`Directory.Build.props`. Keeping the version separate from the local path overrides means it
is shared by all contributors and stays under version control. Bump the version there.

### Folder path overrides

`Directory.Build.props` **is** committed and declares the overridable folder paths with empty
defaults:

- `Bin64` — the folder containing `SpaceEngineers.exe`
- `Pulsar` — the Pulsar folder the plugin is deployed into after each build

It optionally imports `Directory.Build.props.user` from the repository root, which is **not
committed** (matched by `*.user` in `.gitignore`), so each contributor keeps their own local
paths there.

To override a path manually, copy the first `PropertyGroup` of `Directory.Build.props` into
`Directory.Build.props.user`, wrapped into a top-level `<Project>` element, and fill in your
paths. `setup.py` writes that file for you with the auto-detected install location, creating
it if needed and keeping any other overrides already in it.

Leaving a path empty (or having no `Directory.Build.props.user` at all) falls back to
auto-detection in `Directory.Build.props`:

- **Bin64** — Steam App 244850 `InstallLocation` on Windows, then Steam's
  `libraryfolders.vdf` (secondary libraries), then the default Steam library path.
  On Linux it walks the usual Steam / Flatpak locations.
- **Pulsar** — next to the game (`<GameRoot>/Pulsar/Legacy.exe`, the Pulsar-Installer
  layout) first, then `%AppData%\Pulsar` on Windows or `$XDG_CONFIG_HOME/Pulsar`
  (or `~/.config/Pulsar`) on Linux. A path is used only when that install actually exists.

`setup.py` writes the same paths into `Directory.Build.props.user`. It reads Steam from
`HKCU\SOFTWARE\Valve\Steam` first (then the WOW6432Node / HKLM keys), parses
`libraryfolders.vdf` without crashing on a partial file, and prompts for a manual folder
if auto-detect fails.

The build fails with a clear message if `Bin64` cannot be resolved, and warns instead of failing
if the Pulsar folder is missing.

On Windows, Visual Studio 17 / ReSharper Build Tools MSBuild can host `net48` only.
`dotnet build` still builds both `net48` and `net10.0`. The project disables the .NET
workload resolver so those older MSBuild hosts do not fail looking up .NET 10 workloads.

### Deployment

Each successful build copies itself into Pulsar's `Local` plugin folder, so there is nothing to
run by hand:

| Build     | Deployed to                                    |
|-----------|------------------------------------------------|
| `net48`   | `<Pulsar>/Legacy/Local/Vulkan/`          |
| `net10.0` | `<Pulsar>/Interim/Local/Vulkan/`         |

The DLL is copied as `plugin.dll`, its symbols as `plugin.pdb` and the PluginHub registration
XML from the repository root as `plugin.xml`, so Pulsar shows the plugin under its friendly name
and honours the runtime and platform restrictions declared there.

`Interim` is the Pulsar executable running Space Engineers 1 on .NET 10. It falls back to the
`Legacy` data folder when `<Pulsar>/Interim` does not exist, and so does the deployment.
(`<Pulsar>/Modern` belongs to Space Engineers 2 and is never a deployment target here.)
