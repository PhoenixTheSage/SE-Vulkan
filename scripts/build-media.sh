#!/usr/bin/env bash
# Run in MSYS2 MINGW64 on Windows, or a Linux glibc <=2.34 build container.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
platform=linux-x64
build=${SEVK_MEDIA_BUILD:-${TMPDIR:-/tmp}/se-vulkan-media-0.41.0}
audio=(-Dpulse=enabled -Dalsa=enabled)
native=()
cross=()
ass_cross=()
programs=(--enable-ffmpeg --enable-ffprobe)
if [[ ${SEVK_MEDIA_CROSS_LINUX:-0} == 1 ]]; then
    build=/tmp/SE-Vulkan-Linux-Media-0.41.0
    native=(--cross-file="$root/Native/build-media/media-linux.ini")
    cross=(--enable-cross-compile --target-os=linux --arch=x86_64 --cc='zig cc -target x86_64-linux-gnu.2.34' --cxx='zig c++ -target x86_64-linux-gnu.2.34' --ar='zig ar' --ranlib='zig ranlib' --strip='zig objcopy --strip-all')
    ass_cross=(--host=x86_64-linux-gnu --build=x86_64-w64-mingw32)
    programs=(--disable-programs)
    export PATH="$(cygpath -u "$SEVK_ZIG_DIR"):$PATH"
    export CC='zig cc -target x86_64-linux-gnu.2.34'
    export CXX='zig c++ -target x86_64-linux-gnu.2.34'
    export AR='zig ar' RANLIB='zig ranlib'
    # Zig's ELF linker is GNU-compatible; libtool cannot discover it via the
    # unrelated MinGW ld installed on the Windows build host.
    export lt_cv_prog_gnu_ld=yes
    export MSYS2_ARG_CONV_EXCL='--prefix='
elif [[ ${MSYSTEM:-} == MINGW64 ]]; then
    platform=win-x64
    build=$(cygpath -u "${TEMP:?}")/SE-Vulkan-Media-0.41.0
    audio=(-Dwasapi=enabled -Dwin32-threads=enabled)
    native=(--native-file="$root/scripts/media-mingw.ini")
fi
mkdir -p "$build" "$root/Native/build-media"
prefix="$build/prefix"
if [[ ${MSYSTEM:-} == MINGW64 ]]; then
    # FFmpeg emits unquoted shell assignments into config.sh. A private
    # prefix outside a checkout with spaces avoids corrupt .pc files.
    prefix=$(cygpath -m "${LOCALAPPDATA:?}")/SE-Vulkan/Media-0.41.0/$platform
fi
meson_prefix="$prefix"
if [[ ${SEVK_MEDIA_CROSS_LINUX:-0} == 1 ]]; then meson_prefix=/sevk-media; fi
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
if [[ ${MSYSTEM:-} == MINGW64 ]]; then export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"; fi
if [[ ${SEVK_MEDIA_CROSS_LINUX:-0} == 1 ]]; then
    export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig;$(cygpath -m "$SEVK_MEDIA_SYSROOT_ALIAS")/usr/lib64/pkgconfig;$(cygpath -m "$SEVK_MEDIA_SYSROOT_ALIAS")/usr/share/pkgconfig"
    export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
fi
export PATH="$prefix/bin:$PATH"
jobs=${SEVK_MEDIA_JOBS:-8}
source_archive() {
    local name=$1 url=$2 hash=$3
    local archive="$root/Native/build-media/$name"
    [[ -f "$archive" ]] || curl --fail --location --retry 3 "$url" -o "$archive"
    echo "$hash  $archive" | sha256sum -c -
    tar -xf "$archive" -C "$build"
}
materialize_links() {
    if [[ ${SEVK_MEDIA_CROSS_LINUX:-0} == 1 ]]; then
        for link in "$prefix"/lib/*.so*; do
            if [[ -L "$link" ]]; then cp --remove-destination "$(readlink -f "$link")" "$link"; fi
        done
        python "$root/scripts/media-elf-aliases.py" "$prefix/lib"
    fi
}
install_meson() {
    if [[ ${SEVK_MEDIA_CROSS_LINUX:-0} == 1 ]]; then
        meson install -C "$1" --destdir="$(cygpath -m "$build/install-stage")"
        cp -a "$build/install-stage/sevk-media/." "$prefix/"
        # pkg-config is a build-host tool; point installed target metadata at
        # the private staging directory without embedding a host runtime path.
        for pc in "$prefix"/lib/pkgconfig/*.pc; do sed -i "s|/sevk-media|$prefix|g" "$pc"; done
    else
        meson install -C "$1"
    fi
    materialize_links
}
materialize_links
if [[ ! -f "$prefix/lib/pkgconfig/libavcodec.pc" ]] || ! grep -q '#define HAVE_X86ASM 1' "$build/ffmpeg-build/config.h"; then
    source_archive ffmpeg-8.0.1.tar.xz https://ffmpeg.org/releases/ffmpeg-8.0.1.tar.xz 05ee0b03119b45c0bdb4df654b96802e909e0a752f72e4fe3794f487229e5a41
    # glibc retains the obsolete sysctl symbol after removing its public
    # header. The CPU probe must require the header as well as the symbol.
    if [[ $platform == linux-x64 ]]; then
        sed -i 's/^check_func  sysctl$/check_func_headers sys\/sysctl.h sysctl/' "$build/ffmpeg-8.0.1/configure"
    fi
    mkdir -p "$build/ffmpeg-build"
    cd "$build/ffmpeg-build"
    "$build/ffmpeg-8.0.1/configure" --prefix="$prefix" --enable-shared --disable-static "${cross[@]}" \
        --disable-autodetect --disable-gpl --disable-nonfree --disable-doc --disable-debug --enable-x86asm --disable-stripping \
        --disable-hwaccels --disable-d3d11va --disable-dxva2 --disable-vulkan --disable-vaapi --disable-vdpau \
        --disable-everything "${programs[@]}" --enable-avdevice \
        --windres='windres --preprocessor=gcc --preprocessor-arg=-E --preprocessor-arg=-xc --preprocessor-arg=-DRC_INVOKED' \
        --enable-decoder=wmv1,wmv2,wmv3,wmv3image,vc1,vc1image,wmav1,wmav2,wmapro,wmalossless,aac,h264,mpeg4,mp3,pcm_s16le,rawvideo,wrapped_avframe,pcm_f64le \
        --enable-parser=vc1,h264,mpeg4video,mpegaudio,aac \
        --enable-demuxer=asf,asf_o,mov,matroska,wav,mp3,aac --enable-protocol=file \
        --enable-filter=anull,aformat,aresample,format,null,scale,testsrc2,sine \
        --enable-indev=lavfi --enable-encoder=wmv2,wmav2,pcm_s16le --enable-muxer=asf,wav
    make -j"$jobs"
    make install STRIP=true
    materialize_links
fi
if [[ ! -f "$prefix/lib/pkgconfig/libplacebo.pc" ]]; then
    source_archive libplacebo-7.349.0.tar.gz https://codeload.github.com/haasn/libplacebo/tar.gz/refs/tags/v7.349.0 627e32439a0b3d2b90368ead7e919f470ee7446c87cc0f7841bbe319b23aa8b1
    source_archive fast_float-6.1.6.tar.gz https://codeload.github.com/fastfloat/fast_float/tar.gz/refs/tags/v6.1.6 4458aae4b0eb55717968edda42987cabf5f7fc737aee8fede87a70035dba9ab0
    mkdir -p "$build/libplacebo-7.349.0/3rdparty/fast_float"
    cp -r "$build/fast_float-6.1.6/include" "$build/libplacebo-7.349.0/3rdparty/fast_float/"
    reconfigure=(); if [[ -f "$build/placebo-build/build.ninja" ]]; then reconfigure=(--reconfigure); fi
    meson setup "${reconfigure[@]}" "$build/placebo-build" "$build/libplacebo-7.349.0" "${native[@]}" --prefix="$meson_prefix" --libdir=lib \
        --buildtype=release --auto-features=disabled -Ddefault_library=shared -Ddemos=false -Dtests=false -Ddovi=disabled
    meson compile -C "$build/placebo-build" -j "$jobs"
    install_meson "$build/placebo-build"
fi
if [[ ! -f "$prefix/lib/pkgconfig/mpv.pc" ]]; then
    if [[ $platform == linux-x64 && ! -f "$prefix/lib/pkgconfig/libass.pc" ]]; then
        source_archive libass-0.17.3.tar.xz https://github.com/libass/libass/releases/download/0.17.3/libass-0.17.3.tar.xz eae425da50f0015c21f7b3a9c7262a910f0218af469e22e2931462fed3c50959
        mkdir -p "$build/ass-build"
        cd "$build/ass-build"
        "$build/libass-0.17.3/configure" --prefix="$prefix" "${ass_cross[@]}" --enable-shared --disable-static --disable-asm --disable-fontconfig --disable-require-system-font-provider
        make -j"$jobs"
        make install
        materialize_links
    fi
    source_archive mpv-0.41.0.tar.gz https://codeload.github.com/mpv-player/mpv/tar.gz/refs/tags/v0.41.0 ee21092a5ee427353392360929dc64645c54479aefdb5babc5cfbb5fad626209
    reconfigure=(); if [[ -f "$build/mpv-build/build.ninja" ]]; then reconfigure=(--reconfigure); fi
    meson setup "${reconfigure[@]}" "$build/mpv-build" "$build/mpv-0.41.0" "${native[@]}" --prefix="$meson_prefix" --libdir=lib \
        --buildtype=release --auto-features=disabled -Dgpl=false -Dlibmpv=true -Dcplayer=false \
        -Dlua=disabled -Dgl=disabled -Dbuild-date=false "${audio[@]}"
    meson compile -C "$build/mpv-build" -j "$jobs"
    install_meson "$build/mpv-build"
fi
python "$root/scripts/package-media.py" "$prefix" "$root/Assets/Media/$platform" --platform "$platform" --build "$build"
