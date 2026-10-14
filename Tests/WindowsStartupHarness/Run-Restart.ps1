param(
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][ValidateSet('Dx11','Vulkan')][string]$ExpectedBackend,
    [ValidateRange(30,180)][int]$DurationSeconds = 60,
    [switch]$CommitOppositeForNextRestart,
    [string]$ValidationLayerPath
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $repository 'TestResults/WindowsStartup'))
$privateRoot = [IO.Path]::GetFullPath($RunRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($privateRoot) -ne $testRoot -or [IO.Path]::GetFileName($privateRoot) -notmatch '^[0-9a-f]{32}$' -or !(Test-Path -LiteralPath (Join-Path $privateRoot 'launch.json'))) { throw 'Restart testing is limited to a GUID startup run created by Run-Isolated.ps1.' }
if (Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue) { throw 'A game/Pulsar process is already running; private restart was not launched.' }
$prior = Get-Content -LiteralPath (Join-Path $privateRoot 'launch.json') -Raw | ConvertFrom-Json
$editionName = if ($prior.Edition) { $prior.Edition } else { 'Legacy' }
if ($editionName -notin @('Legacy','Interim')) { throw 'Unsupported original startup edition.' }
$target = if ($editionName -eq 'Interim') { 'net10.0' } else { 'net48' }
$portable = Join-Path $privateRoot 'Pulsar'
$editionDirectory = Join-Path $portable $editionName
$gameData = Join-Path $privateRoot 'GameData'
$profile = Join-Path $editionDirectory 'Profiles/VulkanOnly.xml'
$gameBin = [IO.Path]::GetFullPath($Bin64)
if (!(Test-Path -LiteralPath (Join-Path $gameBin 'SpaceEngineers.exe')) -or !(Test-Path -LiteralPath $profile)) { throw 'The original private startup profile or game path is invalid.' }
[xml]$selection = Get-Content -LiteralPath (Join-Path $gameData 'Storage/Vulkan.cfg') -Raw
if ($selection.VulkanConfig.RequestedBackend -ne $ExpectedBackend) { throw 'The persisted private renderer choice does not match the expected restart. This script does not alter the selection.' }
$gameData | Set-Content -LiteralPath (Join-Path $gameData '.se-vulkan-startup') -Encoding utf8
& dotnet build (Join-Path $PSScriptRoot 'WindowsStartupHarness.csproj') -c Release -f $target -p:RunPostBuildEvent=Never -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'The restart harness failed to build.' }
$vulkanLocal = Join-Path $editionDirectory 'Local/Vulkan'
Copy-Item -LiteralPath (Join-Path $repository ('ClientPlugin/bin/Release/' + $target + '/Vulkan.dll')) -Destination (Join-Path $vulkanLocal 'plugin.dll') -Force
Copy-Item -LiteralPath (Join-Path $repository 'Vulkan.xml') -Destination (Join-Path $vulkanLocal 'plugin.xml') -Force
Copy-Item -LiteralPath (Join-Path $repository 'Assets') -Destination $vulkanLocal -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('bin/Release/' + $target + '/WindowsStartupHarness.dll')) -Destination (Join-Path $editionDirectory 'Local/WindowsStartupHarness/plugin.dll') -Force
$repeatRoot = Join-Path $privateRoot ('Restarts/' + [Guid]::NewGuid().ToString('N'))
$reports = Join-Path $repeatRoot 'Reports'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
if (!$ValidationLayerPath) { $ValidationLayerPath = Join-Path $repository 'Native/build/validation/Bin' }
$layerDirectory = [IO.Path]::GetFullPath($ValidationLayerPath)
if (!(Test-Path -LiteralPath (Join-Path $layerDirectory 'VkLayer_khronos_validation.json'))) { throw 'The private validation layer is missing.' }
$changes = @{
    SE_VULKAN_STARTUP_REPORT_DIR=$reports
    SE_VULKAN_STARTUP_USER_DATA=$gameData
    SE_VULKAN_STARTUP_DURATION_SECONDS=$DurationSeconds.ToString()
    SE_VULKAN_STARTUP_DISPLAY_TRANSACTIONS='0'
    SE_VULKAN_STARTUP_RESOLUTION_TIMEOUT='0'
    SE_VULKAN_STARTUP_COMMIT_RESTART=$(if ($CommitOppositeForNextRestart) { '1' } else { '0' })
    SE_VULKAN_ASSET_DIR=(Join-Path $vulkanLocal 'Assets')
    VK_LAYER_PATH=$layerDirectory
    VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
}
$previous = @{}
foreach ($key in $changes.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $changes[$key], 'Process') }
try {
    $arguments = '-bin64 "{0}" -profile "{1}" -appdata "{2}" -noUpdate -noSplash -noDiscord -noStats -noPrompt -stableLogs' -f $gameBin,$profile,$gameData
    $child = Start-Process -FilePath (Join-Path $portable ($editionName + '.exe')) -ArgumentList $arguments -WorkingDirectory $portable -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $repeatRoot 'stdout.txt') -RedirectStandardError (Join-Path $repeatRoot 'stderr.txt')
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
[pscustomobject]@{RunRoot=$privateRoot;RepeatRoot=$repeatRoot;Edition=$editionName;ExpectedBackend=$ExpectedBackend;ProcessId=$child.Id;Arguments=$arguments;ManagedSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'plugin.dll') -Algorithm SHA256).Hash;NativeSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll') -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repeatRoot 'launch.json') -Encoding utf8
Write-Output "RESTART_RUN_ROOT=$repeatRoot"
Write-Output "RESTART_PROCESS_ID=$($child.Id)"
$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds + 120)
while (!$child.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2; $child.Refresh() }
if (!$child.HasExited) { $child.Kill(); $child.WaitForExit(10000) | Out-Null; throw "Private restart timed out: $repeatRoot" }
$exitCode = $child.ExitCode
Write-Output "RESTART_EXIT_CODE=$exitCode"
$expectedExit = if ($ExpectedBackend -eq 'Vulkan') { 0 } else { -1 }
$capture = if ($ExpectedBackend -eq 'Vulkan') { 'menu-native.png' } else { 'menu-dx11.png' }
$active = Get-Content -LiteralPath (Join-Path $reports 'active-backend.txt') -Raw
if ($exitCode -ne $expectedExit -or $active -notmatch ('active=' + $ExpectedBackend + ';') -or !(Test-Path -LiteralPath (Join-Path $reports $capture)) -or !(Test-Path -LiteralPath (Join-Path $reports 'exit-requested.txt'))) { throw "Private restart did not match its persisted backend: $repeatRoot" }
if ($CommitOppositeForNextRestart -and !(Test-Path -LiteralPath (Join-Path $reports 'restart-choice-committed.txt'))) { throw "The next private restart choice was not committed: $repeatRoot" }
if ($ExpectedBackend -eq 'Vulkan' -and (Test-Path -LiteralPath (Join-Path $gameData 'Storage/Vulkan.cfg.startup'))) { throw "The healthy Vulkan session journal was not cleared: $repeatRoot" }
Get-Content -LiteralPath (Join-Path $reports 'active-backend.txt')
Get-ChildItem -LiteralPath $reports | Select-Object Name,Length
