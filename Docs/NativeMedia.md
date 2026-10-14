# Native media

The Vulkan renderer implements the stock SE 1.210.014 media contract on Windows
and Linux: `PlayVideo`, `UpdateVideo`, `DrawVideo`, `CloseVideo`,
`SetVideoVolume`, `IsVideoValid` and `GetVideoState`. Playback is independent of
gameplay pause. The ordinary intro/menu GUI still owns skipping, completion,
transitions and selecting the next background video.

## Runtime and ownership

The private `Assets/Media/<platform>` bundle contains libmpv 0.41.0, FFmpeg
8.0.1, their loader dependency closure, license notices and a checksummed build
manifest. It does not require an installed media player. The pinned LGPL builds
exclude Lua, JavaScript, C plugins, GPU output/decoding and automatic feature
discovery. Playback disables configuration, terminal/input handling, OSD and
subtitles. Audio uses WASAPI or PulseAudio/ALSA; missing audio devices are
reported separately from a working video stream.

Each native player owns a control/event thread and a separate software-render
thread. They exchange immutable CPU frames through three reusable slots. Only
the render owner consumes pixels and records Vulkan work. Removing/replacing
an ID retires that player object; its workers cannot publish into its successor.

FFmpeg's frame filter performs source-size color conversion with four workers,
retaining Lanczos/full-chroma/accurate-rounding settings. The software render
API then copies its already-converted BGRA frame into 64-byte-aligned output
slots. This avoids serializing the high-quality conversion in libmpv's legacy
software-render scaler.
Opening is asynchronous and is reported as valid/playing. A failed opening is
valid/stopped with diagnostics until closed; the intro advances and a looping
menu uses its existing picture fallback. Opening has a 15-second timeout.

The first update starts playback. Initial volume is set before initialization
so muted menu videos cannot briefly start at full volume. Stock DirectShow
attenuation is `100*(v-1)` dB; libmpv's cubic volume receives
`100*10^(100*(v-1)/60)`, with zero mapped to mute.

New frames upload to a persistent BGRA8 sRGB image, matching the stock player's
pixel layout and using FFmpeg's optimized conversion path. The upload ABI also
accepts RGBA8. Image/descriptor allocation
is reused at the same dimensions, staging reuse is timeline-protected, and
replacement/removal follows existing completion retirement. The graphics queue
orders previous fragment reads, transfer writes and subsequent fragment reads.
No decoder worker calls Vulkan and no steady-state device-idle wait is added.

Video sprites use the same target batches, scissor stack, viewport scaling,
premultiplied tint and ordering as other GUI sprites. `VideoGeometry` follows
the actual stock draw submission, including integer rounding and the stock
unused crop calculation; it does not introduce a different interpretation of
the five fit modes.

## Build and package

Windows uses `scripts/build-media.ps1 -MsysRoot <MSYS2-root>`. Restore the
tested packages into a private MSYS2 installation with
`bash scripts/prepare-media-windows.sh`; its archive URLs and SHA-256 hashes
are locked in `scripts/media-mingw-packages.tsv`. Main source archives are
checksum-pinned. Installed package receipts are also preserved in the bundle
manifest. The CI workflow builds both packages, then loads the Linux artifact
in a glibc 2.34 container without installing a system media player.

NASM is required on both build hosts. FFmpeg's optimized x86 paths remain
enabled with runtime CPU dispatch; a scalar build does not sustain the
installed 1080p60 backgrounds on the tested workstation. This is CPU decoding
and conversion, not a separate media GPU/device path.

Linux can build natively with `bash scripts/build-media.sh` on glibc 2.34 or
older, or cross-build on this Windows workstation with
`scripts/build-media.ps1 -CrossLinux`. Cross builds use the existing Zig 0.14.1
toolchain and checksum-locked AlmaLinux 9 RPM headers/libraries. RPM payloads
are extracted privately; their install scripts never execute. The source build
adds the declared FFmpeg sysctl-header portability patch and a pinned
fast_float header fallback for libc++. Linux loader dependencies are bundled;
glibc and its loader remain the platform ABI. The package verifier rejects a
requirement newer than glibc 2.34.
Normal rebuilds never refresh the RPM lock. Updating it requires the explicit
`prepare-media-linux.py --refresh-lock` maintenance command. A native Linux
build must also use baseline-compatible dependency versions; the verifier
rejects newer glibc requirements even in a nominal AlmaLinux 9 package.

Build renderer assets afterward using the existing `scripts/build-native.ps1`
commands. The ordinary managed build deploys the complete Assets tree. Source
profiles continue to resolve it through `SE_VULKAN_ASSET_DIR`/BootstrapAssets.

## Verification

- `dotnet run --project Tests/MediaTests -c Release -p:RunPostBuildEvent=Never`
  exercises production protocol, geometry and sprite batching without a game
  device, including pooled-message retirement and replacement.
- CTest `media_stream` exercises real GPU pixels, stable allocation count and
  in-flight image replacement/removal under validation.
- With the runtime/fixture CMake arguments, `media_decoded_gpu` carries actual
  decoded WMV frames through publication, upload, sprite composition and
  readback, checking progression, orientation and alpha.
- `sevk_media_tests <runtime-directory> <generated-WMV>` exercises actual
  playback, EOF, volume, cancellation, replacement, alpha and invalid media.
  CI generates an owned two-second WMV/audio fixture; game media is not copied
  into the repository.
- `python scripts/verify-media-assets.py` checks packaged hashes, native
  architecture, Linux loader closure/rpaths and the glibc baseline.
- The isolated Windows startup harness accepts `-TestMedia` and up to 900
  seconds. It observes the stock GUI, captures moving media, records per-second
  private memory/textures/frame progression and checks reported A/V timing.
  A 600-second run checks resource growth after its 120-second warmup.
  Use at least 630 seconds with `-TestMediaSkip` and authenticate with
  `scripts/verify-media-run.py --menu-soak --skip` to require 600 seconds of
  actual menu observation after the intro. Add `--display` for the resolution
  transaction run. The test profile re-enters the stock KSH intro chain when
  its launcher disables logos.
  Where the source ffprobe tool is available, it records installed video rates
  and requires steady conversion cadence within 2% of the source timing.

Runtime status reports frames, dropped frames (including the player's own
drop counters), software-output owner CPU time, upload CPU time, media position,
valid A/V offset and whether a real audio backend is active. These are
observations, not an assertion of audible fidelity or matched DX11 pixels.
The owner CPU counter excludes decoder/filter workers; `scripts/probe-media.py`
also reports whole-process CPU time for the isolated CPU-only probe.
The upload counter measures elapsed CPU-side call time, including waits,
rather than GPU transfer duration.

Windows and Linux live evidence is recorded separately in `Docs/Validation.md`.
An ELF build or a successful package check does not establish Linux playback,
desktop integration or cross-backend visual parity. Hardware video decoding,
HDR media and URL playback are outside this implementation.
