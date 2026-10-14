param(
 [Parameter(Mandatory=$true)][string]$World,
 [Parameter(Mandatory=$true)][string]$UserData,
 [Parameter(Mandatory=$true)][string]$Bin64,
 [Parameter(Mandatory=$true)][string]$Pulsar,
 [ValidateSet('Vulkan','Dx11')][string]$Backend='Vulkan',
 [switch]$RetainedSubmission,
 [switch]$PreparedScene,
 [switch]$AsyncPresent,
 [switch]$BulkModels,
 [switch]$NativePassScene,
 [switch]$StockShadowParity,
 [string]$ProfileNative
)
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$perfView=Join-Path $repository 'TestResults/Tools/PerfView/PerfView.exe'
if(!(Test-Path -LiteralPath $perfView)){throw 'PerfView missing.'}
if(!([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))){throw 'ETW collection requires an elevated process.'}
if(!$ProfileNative){$profileNative=if($Backend -eq 'Vulkan'){Join-Path $repository 'Native/build/Release/se_vulkan_native.dll'}else{$null}}
$sceneOptions=@{}
if($PreparedScene){if($Backend -ne 'Vulkan'){throw 'PreparedScene requires Vulkan.'};foreach($key in 'WorldRecipes','ParallelQueue','ActorLods','GroupOcclusion','ActorSizeCulling','StockFarCascades','NewActorViews','ModelBatching'){$sceneOptions[$key]=$true};$RetainedSubmission=$true}
if($BulkModels -and !$PreparedScene){throw 'Bulk CPU capture requires PreparedScene.'}
$sceneOptions.AsyncPresent=[bool]$AsyncPresent
$sceneOptions.BulkModels=[bool]$BulkModels
if($StockShadowParity -or $NativePassScene){if(!$PreparedScene){throw 'Stock shadow/native pass capture requires PreparedScene.'};foreach($key in 'StockShadowSchedule','StockShadowFit','StockDepthGeometry','StockShadowOcclusion'){$sceneOptions[$key]=$true}}
$sceneOptions.NativePassScene=[bool]$NativePassScene
$run=& (Join-Path $PSScriptRoot 'Run-Isolated.ps1') -World $World -UserData $UserData -Bin64 $Bin64 -Pulsar $Pulsar -Backend $Backend -DeepProfile -ProfileNative $profileNative -ParallelCascades:($Backend -eq 'Vulkan') -RetainedSubmission:$RetainedSubmission @sceneOptions -PassThru
$run | ConvertTo-Json | Write-Host
$reports=Join-Path $run.RunRoot 'GameData/Reports';$timer=[Diagnostics.Stopwatch]::StartNew()
while(!(Test-Path -LiteralPath (Join-Path $reports 'sampling.txt'))){
 if(!(Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue)){throw 'Private process exited before sampling.'}
 if($timer.Elapsed.TotalSeconds -gt 240){throw 'Private warmup timeout.'}
 Start-Sleep -Seconds 1
}
$output=Join-Path $run.RunRoot 'cpu.etl'
$pvArgs=@('/AcceptEula',('/LogFile:"'+(Join-Path $run.RunRoot 'perfview-collect.log')+'"'),'/ThreadTime','/NoNGenPdbs','/BufferSizeMB:256','/CircularMB:512','/MaxCollectSec:10','/Merge:true','/Zip:false',('/DataFile:"'+$output+'"'),'/Providers:*SEVulkan-RenderFrames','collect')
$collector=Start-Process -FilePath $perfView -ArgumentList $pvArgs -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $run.RunRoot 'perfview-stdout.log') -RedirectStandardError (Join-Path $run.RunRoot 'perfview-error.log')
if($collector.ExitCode -ne 0){throw "PerfView collection failed: $($run.RunRoot)"}
if(!(Test-Path -LiteralPath $output) -or (Get-Item -LiteralPath $output).Length -eq 0){throw 'Nonempty merged ETL missing.'}
[pscustomobject]@{ProcessId=$run.ProcessId;Instrumented=$true;Profiler='Microsoft PerfView 3.2.8';ThreadTime=$true;NativeSymbols=[bool]$profileNative;Output=$output;Scope='Warmed private scene CPU samples, context switches, CLR rundown and frame markers. Not benchmark FPS.'} | ConvertTo-Json | Set-Content (Join-Path $run.RunRoot 'deep-profile.json')
Write-Host "Deep CPU capture: $output"
