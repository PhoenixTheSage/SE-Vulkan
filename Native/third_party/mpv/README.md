# libmpv public headers

`include/mpv/client.h` and `render.h` are unmodified public headers from
mpv **v0.41.0**. Their permissive license notices are retained in each file.
Source: https://github.com/mpv-player/mpv/tree/v0.41.0/include/mpv

The native renderer resolves these APIs from its private runtime directory.
It does not link a system libmpv or create a media-owned graphics device.
`scripts/build-media.sh` builds the separately distributed LGPL runtime and
records its source checksums, build configuration, dependencies and notices.
