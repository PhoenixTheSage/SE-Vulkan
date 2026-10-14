param(
    [Parameter(Mandatory=$true)][string]$World,
    [Parameter(Mandatory=$true)][string]$UserData,
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][string]$Pulsar,
    [string]$Nsight='C:\Program Files\NVIDIA Corporation\Nsight Systems 2026.5.1\target-windows-x64\nsys.exe',
    [ValidateSet('individual','batch')][string]$NsightGpuWorkload='individual',
    [switch]$CpuSampling,
    [switch]$PreparedScene
)
$ErrorActionPreference='Stop'
$sceneOptions=@{}
if($PreparedScene){foreach($key in 'RetainedSubmission','WorldRecipes','ParallelCascades','ParallelQueue','ActorLods','GroupOcclusion','ActorSizeCulling','StockFarCascades','NewActorViews','ModelBatching'){$sceneOptions[$key]=$true}}
if($CpuSampling){$sceneOptions.ProfileNative=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Native/build/Release/se_vulkan_native.dll'))}
$run=& (Join-Path $PSScriptRoot 'Run-Isolated.ps1') -World $World -UserData $UserData -Bin64 $Bin64 -Pulsar $Pulsar -Nsight $Nsight -NsightGpuWorkload $NsightGpuWorkload -NsightCpuSampling:$CpuSampling @sceneOptions -PassThru
$reports=Join-Path $run.RunRoot 'GameData/Reports'
$timer=[Diagnostics.Stopwatch]::StartNew()
while(!(Test-Path -LiteralPath (Join-Path $reports 'sampling.txt'))){
    if(!(Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue)){throw "Nsight exited before sampling; inspect $($run.RunRoot)/stderr.txt"}
    if($timer.Elapsed.TotalSeconds -gt 240){throw "Private world did not reach sampling; inspect $reports"}
    Start-Sleep -Seconds 1
}
$capture=Get-Content -LiteralPath (Join-Path $run.RunRoot 'nsight-launch.json') -Raw | ConvertFrom-Json
& $Nsight start "--session=$($capture.Session)"
if($LASTEXITCODE -ne 0){throw 'Nsight capture start failed.'}
$tracePath=Join-Path $run.RunRoot 'nsight.nsys-rep'
$sqlitePath=Join-Path $run.RunRoot 'nsight.sqlite'
# Nsight creates a zero-byte placeholder before exporting. The launched profile
# process must finish and both exports must contain data before declaring success.
while((Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue) -or
      !(Test-Path -LiteralPath (Join-Path $reports 'result.json')) -or
      !(Test-Path -LiteralPath $tracePath) -or (Get-Item -LiteralPath $tracePath).Length -eq 0 -or
      !(Test-Path -LiteralPath $sqlitePath) -or (Get-Item -LiteralPath $sqlitePath).Length -eq 0){
    if($timer.Elapsed.TotalSeconds -gt 900){throw "Capture/result timeout; inspect $($run.RunRoot)"}
    if(Test-Path -LiteralPath (Join-Path $reports 'failure.txt')){throw (Get-Content -LiteralPath (Join-Path $reports 'failure.txt') -Raw)}
    Start-Sleep -Seconds 1
}
Write-Host "Instrumented trace: $($run.RunRoot)/nsight.nsys-rep"
Write-Host 'Use the uninstrumented run for frame-time comparisons; profiler overhead is included here.'
