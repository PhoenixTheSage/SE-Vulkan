param([string]$MsysRoot,[switch]$CrossLinux)
$ErrorActionPreference='Stop'
$mediaRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$MsysRoot){$MsysRoot=Join-Path $mediaRoot 'Native/build-media/msys64'}
$mediaBash=Join-Path $MsysRoot 'usr/bin/bash.exe'
if(!(Test-Path -LiteralPath $mediaBash)){throw 'Install MSYS2 MINGW64 with make, diffutils, gcc, meson, ninja, pkgconf, libass, python-jinja and vulkan-headers; pass its root with -MsysRoot. See .github/workflows/media.yml.'}
$oldMediaSystem=$env:MSYSTEM
$oldMediaPathType=$env:MSYS2_PATH_TYPE
$oldMediaCross=$env:SEVK_MEDIA_CROSS_LINUX
$oldMediaZig=$env:SEVK_ZIG_DIR
$oldMediaSysroot=$env:SEVK_MEDIA_SYSROOT_ALIAS
try{
    $env:MSYSTEM='MINGW64';$env:MSYS2_PATH_TYPE='inherit'
    if($CrossLinux){
        $actualSysroot=Join-Path $mediaRoot 'Native/build-media/linux-sysroot'
        New-Item -ItemType Directory -Force -Path $actualSysroot | Out-Null
        $mediaSysrootAlias=Join-Path $env:LOCALAPPDATA 'SE-Vulkan/MediaLinuxSysroot'
        New-Item -ItemType Directory -Force -Path (Split-Path $mediaSysrootAlias) | Out-Null
        if(!(Test-Path -LiteralPath $mediaSysrootAlias)){ New-Item -ItemType Junction -Path $mediaSysrootAlias -Value $actualSysroot | Out-Null }
        if([IO.Path]::GetFullPath((Get-Item -LiteralPath $mediaSysrootAlias).Target) -ne [IO.Path]::GetFullPath($actualSysroot)){throw 'Media sysroot alias belongs to another checkout.'}
        $env:SEVK_MEDIA_SYSROOT_ALIAS=$mediaSysrootAlias
        & uv run --with rpmfile --with zstandard python (Join-Path $PSScriptRoot 'prepare-media-linux.py')
        if($LASTEXITCODE -ne 0){throw 'Linux media sysroot preparation failed'}
        $mediaZigDirectory=Join-Path $mediaRoot 'Native/build/cross-tools/zig-x86_64-windows-0.14.1'
        if(!(Test-Path -LiteralPath (Join-Path $mediaZigDirectory 'zig.exe'))){throw 'Run scripts/build-native.ps1 -CrossLinux first to provision Zig 0.14.1.'}
        $sysroot=$mediaSysrootAlias.Replace('\','/')
        $headerPath=(Join-Path $mediaRoot 'Native/build/_deps/vulkan_headers-src/include').Replace('\','/')
        $mediaZig=(Join-Path $mediaZigDirectory 'zig.exe').Replace('\','/')
        $crossFile=Join-Path $mediaRoot 'Native/build-media/media-linux.ini'
        @"
[binaries]
c = ['$mediaZig', 'cc', '-target', 'x86_64-linux-gnu.2.34']
cpp = ['$mediaZig', 'c++', '-target', 'x86_64-linux-gnu.2.34']
ar = ['$mediaZig', 'ar']
strip = ['$mediaZig', 'objcopy', '--strip-all']
pkg-config = 'pkg-config'
[host_machine]
system = 'linux'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
[properties]
needs_exe_wrapper = true
[built-in options]
c_args = ['-isystem', '$sysroot/usr/include', '-isystem', '$headerPath']
c_link_args = ['-L$sysroot/usr/lib64', '-L$sysroot/usr/lib64/pulseaudio', '-Wl,--disable-new-dtags,-rpath,`$ORIGIN']
"@ | Set-Content -LiteralPath $crossFile -Encoding utf8
        $env:SEVK_MEDIA_CROSS_LINUX='1';$env:SEVK_ZIG_DIR=$mediaZigDirectory
    }
    # Positional arguments avoid injecting the checkout path into shell code.
    & $mediaBash -lc 'media_script="$(cygpath -u "$1")"; bash -c "$(cat "$media_script")" "$media_script"' 'se-media-build' (Join-Path $PSScriptRoot 'build-media.sh')
    if($LASTEXITCODE -ne 0){throw 'Media runtime build failed'}
}finally{
    $env:MSYSTEM=$oldMediaSystem;$env:MSYS2_PATH_TYPE=$oldMediaPathType
    $env:SEVK_MEDIA_CROSS_LINUX=$oldMediaCross;$env:SEVK_ZIG_DIR=$oldMediaZig
    $env:SEVK_MEDIA_SYSROOT_ALIAS=$oldMediaSysroot
}
