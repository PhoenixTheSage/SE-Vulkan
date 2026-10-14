param(
    [Parameter(Mandatory=$true)][string]$World,
    [Parameter(Mandatory=$true)][string]$UserData,
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][string]$Pulsar,
    [string]$Tracer,
    [switch]$LegacyVoxelLookup,
    [switch]$RetainedSubmission,
    [switch]$ParallelCascades,
    [switch]$ActorLods,
    [switch]$GroupOcclusion
)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$Tracer){$Tracer=Join-Path $repository 'TestResults/Tools/dotnet-trace/dotnet-trace.exe'}
if(!(Test-Path -LiteralPath $Tracer -PathType Leaf)){throw 'Install the official dotnet-trace tool before capturing.'}
$run=& (Join-Path $PSScriptRoot 'Run-Isolated.ps1') -World $World -UserData $UserData -Bin64 $Bin64 -Pulsar $Pulsar -ManagedTrace -LegacyVoxelLookup:$LegacyVoxelLookup -RetainedSubmission:$RetainedSubmission -ParallelCascades:$ParallelCascades -ActorLods:$ActorLods -GroupOcclusion:$GroupOcclusion -PassThru
$run | ConvertTo-Json | Write-Host
$reports=Join-Path $run.RunRoot 'GameData/Reports'
$timer=[Diagnostics.Stopwatch]::StartNew()
while(!(Test-Path -LiteralPath (Join-Path $reports 'sampling.txt'))){
    if(!(Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue)){throw "Private process exited before collection: $($run.RunRoot)"}
    if($timer.Elapsed.TotalSeconds -gt 240){throw 'Private world warmup timeout.'}
    Start-Sleep -Seconds 1
}
$output=Join-Path $run.RunRoot 'cpu.nettrace'
& $Tracer collect --process-id $run.ProcessId --duration 00:00:00:10 --profile dotnet-sampled-thread-time --output $output --format Speedscope | Tee-Object -FilePath (Join-Path $run.RunRoot 'managed-trace.log') | Out-Host
if($LASTEXITCODE -ne 0){throw 'Managed capture failed.'}
[pscustomobject]@{Instrumented=$true;Profile='dotnet-sampled-thread-time';DurationSeconds=10;TracerVersion=(& $Tracer --version);ProcessId=$run.ProcessId;Scope='Warmed private process thread-time samples; includes waiting/native boundaries, not CPU-only percentages or benchmark FPS.'} | ConvertTo-Json | Set-Content (Join-Path $run.RunRoot 'managed-launch.json')
while(!(Test-Path -LiteralPath (Join-Path $reports 'result.json'))){
    if($timer.Elapsed.TotalSeconds -gt 300){throw 'Private benchmark completion timeout.'}
    if(Test-Path -LiteralPath (Join-Path $reports 'failure.txt')){throw (Get-Content (Join-Path $reports 'failure.txt') -Raw)}
    Start-Sleep -Seconds 1
}
Write-Host "Instrumented managed trace: $output"
