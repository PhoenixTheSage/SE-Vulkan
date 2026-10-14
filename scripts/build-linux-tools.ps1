param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$hostBuild = Join-Path $repository 'Native/build'
$build = Join-Path $repository 'Native/build-linux-tools'
$zig = Join-Path $hostBuild 'cross-tools/zig-x86_64-windows-0.14.1/zig.exe'
if (!(Test-Path -LiteralPath $zig) -or (& $zig version) -ne '0.14.1') { throw 'Provision pinned Zig with build-native.ps1 -CrossLinux first.' }
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmake) { $cmakePath = $cmake.Source } else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmakePath = Join-Path $visualStudio 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
}
$bundle = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $cmakePath))
$ninja = Get-Command ninja -ErrorAction SilentlyContinue
$ninjaPath = if ($ninja) { $ninja.Source } else { Join-Path $bundle 'Ninja/ninja.exe' }
$toolsSource = Join-Path $hostBuild '_deps/spirv_tools-src'
$headersSource = Join-Path $hostBuild '_deps/spirv_headers-src'
$headersSource = $headersSource.Replace('\', '/')
if ((& git -C $toolsSource rev-parse HEAD) -ne '33e02568181e3312f49a3cf33df470bf96ef293a' -or (& git -C $headersSource rev-parse HEAD) -ne '2a611a970fdbc41ac2e3e328802aed9985352dca') { throw 'SPIRV-Tools/Headers revisions do not match the native dependency pins.' }
& $cmakePath -S $toolsSource -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninjaPath" "-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $repository 'Native/cmake/zig-linux.cmake')" "-DSEVK_ZIG_EXECUTABLE=$zig" '-DCMAKE_BUILD_TYPE=Release' "-DSPIRV-Headers_SOURCE_DIR=$headersSource" '-DSPIRV_SKIP_TESTS=ON' '-DSPIRV_SKIP_EXECUTABLES=OFF' '-DSPIRV_WERROR=OFF'
if ($LASTEXITCODE -ne 0) { throw 'Linux SPIRV-Tools configuration failed.' }
& $cmakePath --build $build --target spirv-val --parallel 6
if ($LASTEXITCODE -ne 0) { throw 'Linux SPIRV-Tools build failed.' }
$archive = Join-Path $build 'linux_dxc_2025_07_14.x86_64.tar.gz'
if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri 'https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.8.2505.1/linux_dxc_2025_07_14.x86_64.tar.gz' -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'F2213DA1FC99DC8778C8823078E16BA97C7F80F86A1D4520AB1ADF4B462BC48C') { throw 'Linux DXC archive checksum mismatch.' }
$dxc = Join-Path $build 'dxc'
New-Item -ItemType Directory -Force -Path $dxc | Out-Null
& tar -xzf $archive -C $dxc
if ($LASTEXITCODE -ne 0) { throw 'Linux DXC extraction failed.' }
if ($Deploy) {
    $target = Join-Path $repository 'Assets/Tools/linux-x64'
    New-Item -ItemType Directory -Force -Path (Join-Path $target 'bin'),(Join-Path $target 'lib'),(Join-Path $target 'Legal') | Out-Null
    Copy-Item -LiteralPath (Join-Path $dxc 'bin/dxc'),(Join-Path $build 'tools/spirv-val') -Destination (Join-Path $target 'bin')
    Copy-Item -Path (Join-Path $dxc 'lib/*.so*') -Destination (Join-Path $target 'lib')
    Copy-Item -Path (Join-Path $dxc 'LICENSE-*.txt') -Destination (Join-Path $target 'Legal')
    Copy-Item -LiteralPath (Join-Path $toolsSource 'LICENSE') -Destination (Join-Path $target 'Legal/SPIRV-Tools-Apache.txt')
}
Write-Output 'Linux shader tools built/packaged; execution requires a Linux host and remains unverified here.'
