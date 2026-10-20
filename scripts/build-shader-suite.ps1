param([string]$GameBin,[ValidateRange(1,16)][int]$Workers=8,[switch]$Package)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if (!$GameBin) { throw 'Pass the installed game Bin64 directory with -GameBin.' }
$GameBin=[IO.Path]::GetFullPath($GameBin)
$shaderRoot=Join-Path (Split-Path -Parent $GameBin) 'Content/Shaders'
$output=Join-Path $projectRoot 'TestResults/ShaderSuite/Current'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$inventory=Join-Path $output 'inventory.json'
& dotnet run --project (Join-Path $projectRoot 'Tests/ShaderSuiteCapture') -c Release "-p:Bin64=$GameBin" -- $GameBin $inventory
if ($LASTEXITCODE -ne 0) { throw 'Stock shader inventory capture failed.' }
$toolRid=if ([IO.Path]::DirectorySeparatorChar -eq '\') { 'win-x64' } else { 'linux-x64' }
# A nonempty all-source selector also survives Windows PowerShell 5 native argument passing.
& dotnet run --project (Join-Path $projectRoot 'Tests/ShaderSuiteCompile') -c Release -- $inventory $shaderRoot (Join-Path $projectRoot "Assets/Tools/$toolRid") $output '*' $Workers
if ($LASTEXITCODE -ne 0) { throw "Shader suite failed; inspect $output/results.json. No new package was published." }
& python (Join-Path $PSScriptRoot 'verify-shader-interfaces.py') $inventory (Join-Path $output 'results.json') (Join-Path $output 'interfaces.json')
if ($LASTEXITCODE -ne 0) { throw 'Material shader interface verification failed.' }
if ($Package) {
    & python (Join-Path $PSScriptRoot 'package-shader-suite.py') $inventory (Join-Path $output 'results.json') (Join-Path $projectRoot 'Assets/Shaders/Vanilla') (Join-Path $projectRoot 'Docs/ShaderSuiteCoverage.json')
    if ($LASTEXITCODE -ne 0) { throw 'Shader catalog packaging failed.' }
    & dotnet run --project (Join-Path $projectRoot 'Tests/WorldShaderPackage') -c Release -p:RunPostBuildEvent=Never -- $shaderRoot (Join-Path $projectRoot 'Assets/Shaders/Vanilla')
    if ($LASTEXITCODE -ne 0) { throw 'World shader supplement packaging failed.' }
    & dotnet run --project (Join-Path $projectRoot 'Tests/LightTileShaderPackage') -c Release -p:RunPostBuildEvent=Never -- $shaderRoot (Join-Path $projectRoot 'Assets/Shaders/Vanilla') (Join-Path $GameBin 'VRage.Render11.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Stock light tile supplement packaging failed.' }
}
