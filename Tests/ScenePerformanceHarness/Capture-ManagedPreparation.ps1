param([Parameter(Mandatory=$true)][string]$World,
      [Parameter(Mandatory=$true)][string]$UserData,
      [Parameter(Mandatory=$true)][string]$Bin64,
      [Parameter(Mandatory=$true)][string]$Pulsar)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$collector=Join-Path $repo 'TestResults/ProfilerTools/dotnet-trace.exe'
if(!(Test-Path -LiteralPath $collector)){throw 'Install the local dotnet-trace collector first.'}
$launch=& (Join-Path $PSScriptRoot 'Run-Isolated.ps1') -World $World -UserData $UserData -Bin64 $Bin64 -Pulsar $Pulsar -Backend Vulkan -RetainedSubmission -WorldRecipes -ParallelCascades -ParallelQueue -ActorLods -GroupOcclusion -ActorSizeCulling -StockFarCascades -NewActorViews -ModelBatching -ManagedTrace -PassThru
$marker=Join-Path $launch.RunRoot 'GameData/Reports/sampling.txt'
$expected=Join-Path $launch.RunRoot 'Pulsar/Interim.exe'
$deadline=[DateTime]::UtcNow.AddMinutes(10)
while(!(Test-Path -LiteralPath $marker)){
    $target=Get-CimInstance Win32_Process -Filter "ProcessId=$($launch.ProcessId)"
    if(!$target){throw "Private process exited before sampling: $($launch.RunRoot)"}
    if(![string]::Equals($target.ExecutablePath,$expected,[StringComparison]::OrdinalIgnoreCase)){throw "Unexpected process path: $($target.ExecutablePath)"}
    if([DateTime]::UtcNow -gt $deadline){throw 'Sampling marker wait expired; process left intact.'}
    Start-Sleep -Milliseconds 250
}
$output=Join-Path $launch.RunRoot 'managed-preparation.nettrace'
& $collector collect --process-id $launch.ProcessId --profile dotnet-common,dotnet-sampled-thread-time --providers SEVulkan-RenderFrames --duration 00:00:10 --format Speedscope --output $output
if($LASTEXITCODE -ne 0){throw "Managed collector failed: $LASTEXITCODE"}
$launch | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repo 'TestResults/ManagedPreparation-Capture.json') -Encoding utf8
Write-Output "Managed trace captured: $output"
