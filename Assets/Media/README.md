# Bundled software media runtime

This directory contains the private Windows and Linux libmpv 0.41.0 runtime,
FFmpeg 8.0.1 and the loader dependency closure. These are dynamically loaded
libraries, built with GPL and nonfree features disabled. Each platform's
`Legal` directory contains its applicable license texts, build configuration,
dependency receipts and declared source patch. Its `manifest.json` identifies
source archive URLs and SHA-256 hashes and authenticates every shipped file.

The corresponding build recipes are `scripts/build-media.sh` and
`scripts/build-media.ps1`. Exact auxiliary Windows archives are listed in
`scripts/media-mingw-packages.tsv`; Linux RPM archive hashes and source RPM
names are in `scripts/media-linux-rpms.json`. Windows packaging records the
installed MSYS2 package receipts and archive hashes. Upstream packaging
recipes and source histories are available from
[MSYS2 MINGW-packages](https://github.com/msys2/MINGW-packages) and
[AlmaLinux source repositories](https://git.almalinux.org/rpms).

The plugin does not modify mpv, libplacebo or Windows FFmpeg sources. Linux
FFmpeg uses the documented sysctl-header detection patch, included alongside
its license. The runtime remains replaceable through the dynamic-library ABI.
See `Docs/NativeMedia.md` for rebuilding and verification. Game videos and
other installed game assets are not distributed here.
