param([Parameter(Mandatory=$true)][string]$RunRoot)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testRoot=[IO.Path]::GetFullPath((Join-Path $repository 'TestResults/CreativeFixture'))
$privateRoot=[IO.Path]::GetFullPath($RunRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if([IO.Path]::GetDirectoryName($privateRoot) -ne $testRoot -or [IO.Path]::GetFileName($privateRoot) -notmatch '^[0-9a-f]{32}$'){throw 'Clock observation requires an owned GUID fixture directory.'}
$launch=Get-Content -LiteralPath (Join-Path $privateRoot 'launch.json') -Raw | ConvertFrom-Json
$prepared=Get-Content -LiteralPath (Join-Path $privateRoot 'prepared.json') -Raw | ConvertFrom-Json
if(!$launch.ReflexTiming -or !$launch.ProcessId -or $launch.Prepared){throw 'Launch the prepared timing fixture before observing clocks.'}
$gameData=Join-Path $privateRoot 'GameData'
if((Get-Content -LiteralPath (Join-Path $gameData '.se-vulkan-creative-fixture') -Raw).Trim() -ne $gameData){throw 'Private user-data marker mismatch.'}
$game=Get-Process -Id $launch.ProcessId -ErrorAction Stop
if($game.Path -ne $prepared.Executable){throw 'The live process is not this fixture executable.'}
$started=$game.StartTime
$report=Join-Path $privateRoot 'Reports/gpu-clocks.csv'
'utc,index,name,pci_bus_id,graphics_mhz,pstate,gpu_percent,power_w' | Set-Content -LiteralPath $report -Encoding utf8
$watch=[Diagnostics.Stopwatch]::StartNew();$rows=0
while($watch.Elapsed.TotalSeconds -lt ([int]$prepared.DurationSeconds+180)){
    $game=Get-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue
    if(!$game -or $game.StartTime -ne $started -or $game.Path -ne $prepared.Executable){break}
    # Read-only device telemetry; no clock, power, persistence or driver changes.
    $reading=& nvidia-smi --query-gpu=index,name,pci.bus_id,clocks.current.graphics,pstate,utilization.gpu,power.draw --format=csv,noheader,nounits
    if($LASTEXITCODE -ne 0){throw 'NVIDIA clock query failed; do not treat missing telemetry as zero clocks.'}
    $stamp=[DateTime]::UtcNow.ToString('o')
    foreach($line in $reading){($stamp+','+$line) | Add-Content -LiteralPath $report -Encoding utf8;$rows++}
    Start-Sleep -Milliseconds 1000
}
('Read-only clock observation finished; rows='+$rows+'; PID='+$launch.ProcessId) | Set-Content -LiteralPath (Join-Path $privateRoot 'Reports/gpu-clock-observer-completed.txt') -Encoding utf8
Write-Output ('Clock observation complete: '+$rows+' rows')
