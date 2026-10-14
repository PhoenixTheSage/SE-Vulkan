param([Parameter(Mandatory=$true)][string]$RunRoot)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $repository 'TestResults/CreativeFixture'))
$privateRoot = [IO.Path]::GetFullPath($RunRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ([IO.Path]::GetDirectoryName($privateRoot) -ne $testRoot -or [IO.Path]::GetFileName($privateRoot) -notmatch '^[0-9a-f]{32}$') { throw 'Prepared launch must belong to a GUID Creative fixture under this repository.' }
if (Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue) { throw 'A game/Pulsar process is already running.' }
$prepared = Get-Content -LiteralPath (Join-Path $privateRoot 'prepared.json') -Raw | ConvertFrom-Json
$launch = Get-Content -LiteralPath (Join-Path $privateRoot 'launch.json') -Raw | ConvertFrom-Json
$gameData = Join-Path $privateRoot 'GameData'
$portable = Join-Path $privateRoot 'Pulsar'
if (!$launch.Prepared -or $launch.ProcessId -or $prepared.RunRoot -ne $privateRoot -or $prepared.Executable -ne (Join-Path $portable 'Legacy.exe') -or $prepared.WorkingDirectory -ne $portable -or $prepared.Environment.SE_VULKAN_CREATIVE_USER_DATA -ne $gameData -or (Get-Content -LiteralPath (Join-Path $gameData '.se-vulkan-creative-fixture') -Raw).Trim() -ne $gameData) { throw 'Prepared snapshot or private user-data marker is inconsistent, or was already launched.' }
$vulkanLocal = Join-Path $portable 'Legacy/Local/Vulkan'
if ((Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'plugin.dll') -Algorithm SHA256).Hash -ne $launch.PluginSha256 -or (Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll') -Algorithm SHA256).Hash -ne $launch.NativeSha256) { throw 'Prepared binaries changed after snapshot creation.' }
$previous = @{}
foreach ($property in $prepared.Environment.psobject.Properties) { $previous[$property.Name] = [Environment]::GetEnvironmentVariable($property.Name, 'Process'); [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process') }
try {
    $child = Start-Process -FilePath $prepared.Executable -ArgumentList $prepared.Arguments -WorkingDirectory $portable -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $privateRoot 'stdout.txt') -RedirectStandardError (Join-Path $privateRoot 'stderr.txt')
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
$launch | Add-Member -NotePropertyName ProcessId -NotePropertyValue $child.Id
$launch | Add-Member -NotePropertyName Backend -NotePropertyValue $prepared.Backend
$launch | Add-Member -NotePropertyName Arguments -NotePropertyValue $prepared.Arguments
$launch.Prepared = $false
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $privateRoot 'launch.json') -Encoding utf8
$launch | ConvertTo-Json
