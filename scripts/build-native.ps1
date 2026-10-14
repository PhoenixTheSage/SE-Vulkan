param([ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$Test,[switch]$Deploy,[switch]$CrossLinux,[switch]$ProfileSymbols,[string]$ZigPath,[string]$BuildDirectory)
$ErrorActionPreference='Stop'
if($ProfileSymbols -and ($Deploy -or $CrossLinux -or $Configuration -ne 'Release')){throw 'ProfileSymbols requires optimized Windows Release without Deploy.'}
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeBuild = Join-Path $projectRoot 'Native/build'
$nativeCache = $nativeBuild
if ($BuildDirectory) {
    if ($CrossLinux -or $Deploy) { throw 'An isolated BuildDirectory is for Windows validation without deployment.' }
    $nativeBuild = [IO.Path]::GetFullPath($BuildDirectory)
}
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) { $cmakePath=$cmakeCommand.Source } else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vsInstall = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmakePath = Join-Path $vsInstall 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
}
if (!(Test-Path -LiteralPath $cmakePath)) { throw 'Install CMake 3.24+ and a C++20 compiler (Visual Studio C++ Build Tools).' }
if ($CrossLinux) {
    $hostDxc = Join-Path $nativeBuild 'dxc/bin/x64/dxc.exe'
    $hostValidator = Join-Path $nativeBuild '_deps/spirv_tools-build/tools/Release/spirv-val.exe'
    if (!(Test-Path -LiteralPath $hostDxc) -or !(Test-Path -LiteralPath $hostValidator)) {
        throw 'Build Windows Release first to provision the pinned host DXC and SPIR-V validator.'
    }
    if (!$ZigPath) {
        $zigDirectory = Join-Path $nativeBuild 'cross-tools'
        $zigArchive = Join-Path $zigDirectory 'zig.zip'
        $ZigPath = Join-Path $zigDirectory 'zig-x86_64-windows-0.14.1/zig.exe'
        if (!(Test-Path -LiteralPath $ZigPath)) {
            New-Item -ItemType Directory -Force -Path $zigDirectory | Out-Null
            if (!(Test-Path -LiteralPath $zigArchive)) {
                Invoke-WebRequest -Uri 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -OutFile $zigArchive
            }
            if ((Get-FileHash -LiteralPath $zigArchive -Algorithm SHA256).Hash -ne '554F5378228923FFD558EAC35E21AF020C73789D87AFEABF4BFD16F2E6FEED2C') { throw 'Zig checksum mismatch' }
            Expand-Archive -LiteralPath $zigArchive -DestinationPath $zigDirectory -Force
        }
    }
    if (!(Test-Path -LiteralPath $ZigPath)) { throw 'Zig compiler not found' }
    if ((& $ZigPath version) -ne '0.14.1') { throw 'Cross build requires pinned Zig 0.14.1' }
    $ninjaCommand = Get-Command ninja -ErrorAction SilentlyContinue
    if ($ninjaCommand) { $ninjaPath=$ninjaCommand.Source } else {
        $cmakeBundleRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $cmakePath))
        $ninjaPath=Join-Path $cmakeBundleRoot 'Ninja/ninja.exe'
    }
    if (!(Test-Path -LiteralPath $ninjaPath)) { throw 'Ninja is required for Linux cross-compilation' }
    if ($Configuration -eq 'Release') { $nativeBuild=Join-Path $projectRoot 'Native/build-linux' }
    else { $nativeBuild=Join-Path $projectRoot 'Native/build-linux-debug' }
    & $cmakePath -S (Join-Path $projectRoot 'Native') -B $nativeBuild -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninjaPath" "-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $projectRoot 'Native/cmake/zig-linux.cmake')" "-DSEVK_ZIG_EXECUTABLE=$ZigPath" "-DCMAKE_BUILD_TYPE=$Configuration" "-DSEVK_DXC=$hostDxc" "-DSEVK_SPIRV_VAL=$hostValidator"
    if ($LASTEXITCODE -ne 0) { throw 'Linux cross configuration failed' }
    & $cmakePath --build $nativeBuild --target se_vulkan_native sevk_smoke sevk_media_tests sevk_media_stream sevk_world_instance_arena sevk_world_indirect_plan sevk_world_object_records sevk_spot_bounds sevk_scene_actor_plan sevk_native_pass_scene sevk_resident_proxy_references --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Linux cross build failed' }
    if ($Deploy) {
        $nativeTarget=Join-Path $projectRoot 'Assets/Native/linux-x64'
        New-Item -ItemType Directory -Force -Path $nativeTarget | Out-Null
        Copy-Item -LiteralPath (Join-Path $nativeBuild 'libse_vulkan_native.so') -Destination $nativeTarget
    }
    if ($Test) { Write-Host 'Linux library compiled; runtime tests require a Linux host and were not run.' }
    return
}
$configureOptions=@()
if ($BuildDirectory -and $nativeBuild -ne [IO.Path]::GetFullPath($nativeCache)) {
    # Reuse only immutable pinned source dependencies and host tools. Object files,
    # generated projects, shader outputs and PDBs belong to the isolated build.
    foreach ($dependency in @('vulkan_headers','volk','vma','spirv_reflect','spirv_headers')) {
        $dependencySource=Join-Path $nativeCache ('_deps/'+$dependency+'-src')
        if (Test-Path -LiteralPath $dependencySource) { $configureOptions+=('-DFETCHCONTENT_SOURCE_DIR_'+$dependency.ToUpperInvariant()+'='+$dependencySource) }
    }
    $cachedDxc=Join-Path $nativeCache 'dxc/bin/x64/dxc.exe'
    $cachedValidator=Join-Path $nativeCache "_deps/spirv_tools-build/tools/$Configuration/spirv-val.exe"
    if(Test-Path -LiteralPath $cachedDxc){$configureOptions+="-DSEVK_DXC=$cachedDxc"}
    if(Test-Path -LiteralPath $cachedValidator){$configureOptions+="-DSEVK_SPIRV_VAL=$cachedValidator"}
}
& $cmakePath -S (Join-Path $projectRoot 'Native') -B $nativeBuild -A x64 ("-DSEVK_PROFILE_SYMBOLS="+$(if($ProfileSymbols){'ON'}else{'OFF'})) @configureOptions
if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed' }
& $cmakePath --build $nativeBuild --config $Configuration --target se_vulkan_native sevk_smoke sevk_media_tests sevk_media_stream sevk_world_instance_arena sevk_world_indirect_plan sevk_world_object_records sevk_spot_bounds sevk_scene_actor_plan sevk_native_pass_scene sevk_resident_proxy_references sevk_ray_policy sevk_gpu_opaque_admission sevk_gpu_opaque_registry sevk_gpu_opaque_reference --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
if ($Test) {
    & (Join-Path (Split-Path $cmakePath) 'ctest.exe') --test-dir $nativeBuild -C $Configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed' }
}
if ($Deploy) {
    $nativeTarget = Join-Path $projectRoot 'Assets/Native/win-x64'
    $shaderTarget = Join-Path $projectRoot 'Assets/Shaders/Compiled'
    New-Item -ItemType Directory -Force -Path $nativeTarget,$shaderTarget | Out-Null
    Copy-Item -LiteralPath (Join-Path $nativeBuild "$Configuration/se_vulkan_native.dll") -Destination $nativeTarget
    Copy-Item -Path (Join-Path $nativeBuild 'shaders/*.spv') -Destination $shaderTarget
    $toolsTarget = Join-Path $projectRoot 'Assets/Tools/win-x64'
    New-Item -ItemType Directory -Force -Path $toolsTarget | Out-Null
    Copy-Item -Path (Join-Path $nativeBuild 'dxc/bin/x64/*.dll'),(Join-Path $nativeBuild 'dxc/bin/x64/dxc.exe') -Destination $toolsTarget
    Copy-Item -LiteralPath (Join-Path $nativeBuild "_deps/spirv_tools-build/tools/$Configuration/spirv-val.exe") -Destination $toolsTarget
    $legalTarget = Join-Path $toolsTarget 'Legal'
    New-Item -ItemType Directory -Force -Path $legalTarget | Out-Null
    Copy-Item -Path (Join-Path $nativeBuild 'dxc/LICENSE-*.txt') -Destination $legalTarget
    Copy-Item -LiteralPath (Join-Path $nativeBuild '_deps/volk-src/LICENSE.md') -Destination (Join-Path $legalTarget 'volk-MIT.txt')
    Copy-Item -LiteralPath (Join-Path $nativeBuild '_deps/vma-src/LICENSE.txt') -Destination (Join-Path $legalTarget 'VMA-MIT.txt')
    Copy-Item -LiteralPath (Join-Path $nativeBuild '_deps/spirv_reflect-src/LICENSE') -Destination (Join-Path $legalTarget 'SPIRV-Reflect-Apache.txt')
    Copy-Item -LiteralPath (Join-Path $nativeBuild '_deps/spirv_tools-src/LICENSE') -Destination (Join-Path $legalTarget 'SPIRV-Tools-Apache.txt')
}
