param(
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][string]$Pulsar,
    [ValidateSet('Vulkan','Dx11')][string]$Backend = 'Vulkan',
    [ValidateRange(120,600)][int]$DurationSeconds = 240,
    [switch]$ObserveD3D11,
    [switch]$Asteroids,
    [switch]$Planets,
    [switch]$Transparency,
    [switch]$Postprocessing,
    [switch]$AnomalyParity,
    [switch]$PrepareOnly,
    [string]$LoadFixtureWorld,
    [string]$ValidationLayerPath,
    [string]$RenderDocDirectory
)
$ErrorActionPreference = 'Stop'
function Copy-FixtureAssets([string]$Source,[string]$Target) {
    & robocopy $Source $Target /E /MT:8 /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if($LASTEXITCODE -ge 8){throw "Fixture asset copy failed: robocopy exit $LASTEXITCODE"}
    # Robocopy's 0..7 results are success bitmasks, not shell failures.
    $global:LASTEXITCODE=0
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gameBin = [IO.Path]::GetFullPath($Bin64)
$pulsarSource = [IO.Path]::GetFullPath($Pulsar)
if (!$PrepareOnly -and (Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue)) { throw 'A game/Pulsar process is already running; isolated Creative fixture was not launched.' }
if (!(Test-Path -LiteralPath (Join-Path $gameBin 'SpaceEngineers.exe'))) { throw 'The game Bin64 path is invalid.' }
if (!(Test-Path -LiteralPath (Join-Path $pulsarSource 'Legacy.exe'))) { throw 'The Pulsar path is invalid.' }
$builtPlugin = Join-Path $repository 'ClientPlugin/bin/Release/net48/Vulkan.dll'
& dotnet build (Join-Path $repository 'ClientPlugin/ClientPlugin.csproj') -c Release -p:RunPostBuildEvent=Never
if ($LASTEXITCODE -ne 0) { throw 'Current managed renderer build failed.' }
if ($AnomalyParity) {
    $anomalyRoot = [IO.Path]::GetFullPath((Join-Path $repository '../Anomaly'))
    & dotnet build (Join-Path $anomalyRoot 'ClientPlugin/ClientPlugin.csproj') -c Release -m:1 -p:BuildInParallel=false -p:RunPostBuildEvent=Never
    if ($LASTEXITCODE -ne 0) { throw 'Current Anomaly build failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $repository 'Native/build/Release/se_vulkan_native.dll'))) { throw 'Build the current native renderer before the parity fixture.' }
}
if (!(Test-Path -LiteralPath $builtPlugin)) { throw 'Build the Release managed plugin first.' }
& dotnet build (Join-Path $PSScriptRoot 'CreativeFixtureHarness.csproj') -c Release -p:RunPostBuildEvent=Never
if ($LASTEXITCODE -ne 0) { throw 'Creative fixture harness build failed.' }
if ($ObserveD3D11) {
    & dotnet build (Join-Path $repository 'Tests/D3D11DeviceObserver/D3D11DeviceObserver.csproj') -c Release -p:RunPostBuildEvent=Never
    if ($LASTEXITCODE -ne 0) { throw 'D3D11 observer build failed.' }
}
$runRoot = Join-Path $repository ('TestResults/CreativeFixture/' + [Guid]::NewGuid().ToString('N'))
$portable = Join-Path $runRoot 'Pulsar'
$edition = Join-Path $portable 'Legacy'
$gameData = Join-Path $runRoot 'GameData'
$reports = Join-Path $runRoot 'Reports'
New-Item -ItemType Directory -Force -Path $portable,$edition,$gameData,$reports,(Join-Path $edition 'Profiles'),(Join-Path $edition 'Sources'),(Join-Path $gameData 'Storage') | Out-Null
Copy-Item -LiteralPath (Join-Path $pulsarSource 'Legacy.exe'),(Join-Path $pulsarSource 'Legacy.exe.config') -Destination $portable
foreach ($library in @('Legacy','Compiler','Interface')) {
    $destination = Join-Path $portable ('Libraries/' + $library)
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $pulsarSource ('Libraries/' + $library + '/*')) -Destination $destination -Recurse
}
$vulkanLocal = Join-Path $edition 'Local/Vulkan'
$harnessLocal = Join-Path $edition 'Local/CreativeFixtureHarness'
New-Item -ItemType Directory -Force -Path $vulkanLocal,$harnessLocal | Out-Null
Copy-Item -LiteralPath $builtPlugin -Destination (Join-Path $vulkanLocal 'plugin.dll')
Copy-Item -LiteralPath (Join-Path $repository 'Vulkan.xml') -Destination (Join-Path $vulkanLocal 'plugin.xml')
Copy-FixtureAssets (Join-Path $repository 'Assets') (Join-Path $vulkanLocal 'Assets')
$anomalyId = ''
$anomalySha = ''
if ($AnomalyParity) {
    Copy-Item -LiteralPath (Join-Path $repository 'Native/build/Release/se_vulkan_native.dll') -Destination (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll')
    $anomalyLocal = Join-Path $edition 'Local/Anomaly'
    New-Item -ItemType Directory -Force -Path $anomalyLocal | Out-Null
    $anomalyBuilt = Join-Path $anomalyRoot 'ClientPlugin/bin/Release/net48'
    Copy-Item -LiteralPath (Join-Path $anomalyBuilt 'Anomaly.dll') -Destination (Join-Path $anomalyLocal 'plugin.dll')
    $anomalySha = (Get-FileHash -LiteralPath (Join-Path $anomalyLocal 'plugin.dll') -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (Join-Path $anomalyBuilt 'Anomaly.Dx11.dll'),(Join-Path $anomalyBuilt '0Harmony.dll') -Destination $anomalyLocal
    Copy-Item -Path (Join-Path $anomalyBuilt 'Mono.Cecil*.dll') -Destination $anomalyLocal
    Copy-Item -LiteralPath (Join-Path $anomalyRoot 'Anomaly.xml') -Destination (Join-Path $anomalyLocal 'plugin.xml')
    Copy-FixtureAssets (Join-Path $anomalyRoot 'Assets') (Join-Path $anomalyLocal 'Assets')
    $anomalyId = '<string>A9C29274-E447-49EE-881B-C980E6D190FD</string>'
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/net48/CreativeFixtureHarness.dll') -Destination (Join-Path $harnessLocal 'plugin.dll')
$observerId = ''
if ($ObserveD3D11) {
    $observerLocal = Join-Path $edition 'Local/D3D11DeviceObserver'
    New-Item -ItemType Directory -Force -Path $observerLocal | Out-Null
    Copy-Item -LiteralPath (Join-Path $repository 'Tests/D3D11DeviceObserver/bin/Release/net48/D3D11DeviceObserver.dll') -Destination (Join-Path $observerLocal 'plugin.dll')
    $observerId = '<string>D3D11DeviceObserver.dll</string>'
}
$profile = Join-Path $edition 'Profiles/CreativeFixture.xml'
@"
<?xml version="1.0" encoding="utf-8"?>
<Profile><Name>Isolated $Backend Creative fixture</Name><GitHub /><DevFolder /><Local><string>AE72EBB8-F4A8-4ADB-A400-19CC46FDAB79</string>$anomalyId<string>CreativeFixtureHarness.dll</string>$observerId</Local><Mods /></Profile>
"@ | Set-Content -LiteralPath $profile -Encoding utf8
@'
<?xml version="1.0" encoding="utf-8"?>
<SourcesConfig><ShowWarning>false</ShowWarning><LocalHubSources /><RemoteHubSources /><RemotePluginSources /><LocalPluginSources /><ModSources /></SourcesConfig>
'@ | Set-Content -LiteralPath (Join-Path $edition 'Sources/sources.xml') -Encoding utf8
# Existing consent is copied only into this disposable, explicitly isolated Pulsar instance.
Copy-Item -LiteralPath (Join-Path $pulsarSource 'Legacy/config.xml') -Destination (Join-Path $edition 'config.xml')
# Pin both backends before device creation so baseline captures use identical pixels.
@'
<?xml version="1.0" encoding="utf-8"?>
<MyObjectBuilder_ConfigData xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><Data><dictionary>
<item><Key>GraphicsRenderer</Key><Value><Value xsi:type="xsd:string">DirectX 11</Value></Value></item>
<item><Key>ScreenWidth</Key><Value><Value xsi:type="xsd:string">640</Value></Value></item>
<item><Key>ScreenHeight</Key><Value><Value xsi:type="xsd:string">480</Value></Value></item>
<item><Key>RefreshRate</Key><Value><Value xsi:type="xsd:string">60000</Value></Value></item>
<item><Key>WindowMode</Key><Value><Value xsi:type="xsd:string">0</Value></Value></item>
</dictionary></Data></MyObjectBuilder_ConfigData>
'@ | Set-Content -LiteralPath (Join-Path $gameData 'SpaceEngineers.cfg') -Encoding utf8
@"
<?xml version="1.0" encoding="utf-8"?>
<VulkanConfig><RequestedBackend>$Backend</RequestedBackend><EnableValidation>true</EnableValidation><RecoveryRequired>false</RecoveryRequired><RetryGeneration>0</RetryGeneration><LastFailure /></VulkanConfig>
"@ | Set-Content -LiteralPath (Join-Path $gameData 'Storage/Vulkan.cfg') -Encoding utf8
$gameData | Set-Content -LiteralPath (Join-Path $gameData '.se-vulkan-creative-fixture') -Encoding utf8
if (!$ValidationLayerPath) { $ValidationLayerPath = Join-Path $repository 'Native/build/validation/Bin' }
$layerDirectory = [IO.Path]::GetFullPath($ValidationLayerPath)
if ($Backend -eq 'Vulkan' -and !(Test-Path -LiteralPath (Join-Path $layerDirectory 'VkLayer_khronos_validation.json'))) { throw "Private validation layer missing: $layerDirectory" }
$environmentChanges = @{
    SE_VULKAN_POSTPROCESS_FIXTURE=([int]$Postprocessing.IsPresent).ToString()
    SE_VULKAN_ANOMALY_PARITY=([int]$AnomalyParity.IsPresent).ToString()
    SE_VULKAN_CREATIVE_FIXTURE='1'
    SE_VULKAN_CREATIVE_REPORT_DIR=$reports
    SE_VULKAN_CREATIVE_USER_DATA=$gameData
    SE_VULKAN_CREATIVE_DURATION_SECONDS=$DurationSeconds.ToString()
    SE_VULKAN_CREATIVE_BACKEND=$Backend
    SE_VULKAN_D3D11_REPORT_DIR=$reports
    SE_VULKAN_ASSET_DIR=(Join-Path $vulkanLocal 'Assets')
    VK_LAYER_PATH=$layerDirectory
    VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
}
if ($LoadFixtureWorld) {
    $fixtureSource = [IO.Path]::GetFullPath($LoadFixtureWorld)
    if (!(Test-Path -LiteralPath (Join-Path $fixtureSource '.se-vulkan-private-fixture-world'))) { throw 'Baseline input is not an explicitly marked private fixture save.' }
    # Copy that private fixture into this run; never load or change its source in place.
    $fixtureCopy = Join-Path $gameData 'FixtureBaseline'
    Copy-Item -LiteralPath $fixtureSource -Destination $fixtureCopy -Recurse
    if($AnomalyParity) {
        # LoadCheckpoint replaces Sandbox.sbc.Settings from Sandbox_config.sbc
        # when present; configure both authoritative files in this private copy.
        foreach($settingsFile in @('Sandbox.sbc','Sandbox_config.sbc')) {
            $checkpointPath=Join-Path $fixtureCopy $settingsFile
            if(!(Test-Path -LiteralPath $checkpointPath)){continue}
            [xml]$checkpoint=Get-Content -LiteralPath $checkpointPath -Raw
            $settingsNode=$checkpoint.SelectSingleNode('//*[local-name()="Settings"]')
            if(!$settingsNode){throw 'Marked fixture checkpoint has no session settings.'}
            foreach($name in @('EnableOxygen','EnableOxygenPressurization')) {
                $node=$settingsNode.SelectSingleNode("*[local-name()='$name']")
                if(!$node){$node=$checkpoint.CreateElement($name,$settingsNode.NamespaceURI);$settingsNode.AppendChild($node)|Out-Null}
                $node.InnerText='true'
            }
            $checkpoint.Save($checkpointPath)
        }
        'Only this private copy enables oxygen/pressurization for authoritative room topology.' | Set-Content -LiteralPath (Join-Path $fixtureCopy 'anomaly-parity-settings.txt') -Encoding utf8
    }
    $environmentChanges.SE_VULKAN_CREATIVE_LOAD_WORLD = $fixtureCopy
}
$previous = @{}
if ($Asteroids -and $Planets) { throw 'Choose one voxel fixture per run.' }
if ($Asteroids) { $environmentChanges.SE_VULKAN_ASTEROID_FIXTURE = '1' }
if ($Planets) { $environmentChanges.SE_VULKAN_PLANET_FIXTURE = '1' }
if ($Transparency) { $environmentChanges.SE_VULKAN_TRANSPARENCY_FIXTURE = '1' }
if ($RenderDocDirectory) {
    if ($Backend -ne 'Vulkan') { throw 'This capture mode requires the native Vulkan fixture.' }
    $renderDoc = [IO.Path]::GetFullPath($RenderDocDirectory)
    if (!(Test-Path -LiteralPath (Join-Path $renderDoc 'renderdoc.json')) -or !(Test-Path -LiteralPath (Join-Path $renderDoc 'renderdoc.dll'))) { throw 'Installed RenderDoc layer/library are missing.' }
    $environmentChanges.VK_LAYER_PATH = $layerDirectory + ';' + $renderDoc
    $environmentChanges.VK_INSTANCE_LAYERS = 'VK_LAYER_RENDERDOC_Capture'
    $environmentChanges.ENABLE_VULKAN_RENDERDOC_CAPTURE = '1'
    $environmentChanges.SE_VULKAN_CREATIVE_RENDERDOC = '1'
}
$arguments = '-bin64 "{0}" -profile "{1}" -appdata "{2}" -noUpdate -noSplash -noDiscord -noStats -noPrompt -stableLogs' -f $gameBin,$profile,$gameData
if ($PrepareOnly) {
    [pscustomobject]@{RunRoot=$runRoot;Backend=$Backend;DurationSeconds=$DurationSeconds;Executable=(Join-Path $portable 'Legacy.exe');WorkingDirectory=$portable;Arguments=$arguments;Environment=$environmentChanges} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'prepared.json') -Encoding utf8
    [pscustomobject]@{RunRoot=$runRoot;Prepared=$true;AnomalyParity=[bool]$AnomalyParity;AnomalySha256=$anomalySha;PluginSha256=(Get-FileHash -LiteralPath $builtPlugin -Algorithm SHA256).Hash;NativeSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll') -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json') -Encoding utf8
    Write-Output "PREPARED_RUN_ROOT=$runRoot"
    return
}
foreach ($key in $environmentChanges.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $environmentChanges[$key], 'Process') }
try {
    $launched = Start-Process -FilePath (Join-Path $portable 'Legacy.exe') -ArgumentList $arguments -WorkingDirectory $portable -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.txt') -RedirectStandardError (Join-Path $runRoot 'stderr.txt')
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
$launch = [pscustomobject]@{RunRoot=$runRoot;ProcessId=$launched.Id;Backend=$Backend;DurationSeconds=$DurationSeconds;Width=640;Height=480;AnomalyParity=[bool]$AnomalyParity;AnomalySha256=$anomalySha;PluginSha256=(Get-FileHash -LiteralPath $builtPlugin -Algorithm SHA256).Hash;NativeSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll') -Algorithm SHA256).Hash;Arguments=$arguments}
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json') -Encoding utf8
$launch | ConvertTo-Json
# Return immediately so the caller can monitor evidence and communicate during gameplay.
