param([string[]]$Order=@('G'),[string]$MatchFrame,[string]$Out,[string]$Property='GpuOpaqueOwnershipEnabled',[switch]$Split)
# Same-process paused OFF/ON/OFF comparison of GPU-opaque ownership (Plan 4), hybrid with every 10-12 treatment,
# with frozen lights, spots, frame constants, probe cycle and refreshed cascades. -Split runs the frame split with two
# build stages and captures the game screenshot only. Gate: Compare-LightingPhases.py <run root>; GPU-on differences
# must stay within the control/control-return noise.
$ErrorActionPreference='Stop'
$repo='T:\Cursor Projects\SE-Vulkan'
$common=@{World='T:\Cursor Projects\SE-Vulkan\TestResults\Worlds\Pertam-Orbit';UserData='T:/SteamLibrary/steamapps/common/SpaceEngineers/Profile';Bin64='T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64';Pulsar='T:\SteamLibrary\steamapps\common\SpaceEngineers\Pulsar';PassThru=$true;Backend='Vulkan';SelectionAudit=$true;LightingReconciliation=$true;LightingNativePass=$true}
$product=@('LightTiles','ActorLods','AsyncPresent','NewActorViews','RetainedSubmission','StockShadowSchedule','StockShadowFit','WorldRecipes','ParallelQueue','ParallelCascades','NativePassScene','GroupOcclusion','StockDepthGeometry','NativeCells','NativeMixedOpaque','PipelinedRecording','AsyncPassPacking','DeferredCellEmission')
$rows=@()
foreach($label in $Order){
    # C = pure product defaults: the toggle is inert without a configured native pass scene.
    $args=$common.Clone();foreach($k in $product){if($label -ne 'C' -or $k -notin @('NativePassScene','GroupOcclusion','StockDepthGeometry','NativeCells','NativeMixedOpaque','PipelinedRecording','AsyncPassPacking','DeferredCellEmission')){$args[$k]=$true}};if($label -eq 'NPM'){$args.NativeMixedOpaque=$true}
    foreach($k in @('GpuOpaqueScene','GpuOpaqueDeviceLocal','GpuOpaqueHybrid','GpuOpaqueViewBatches','GpuOpaqueRoleStreams','GpuOpaqueSparseAppearance','GpuOpaqueResidentDeltas','GpuOpaqueOwnedInputs','GpuOpaqueViewMasks')){$args[$k]=$true}
    if($MatchFrame){$args.MatchFrame=$MatchFrame}
    if($Property){$args.Remove('LightingNativePass');$args.LightingProperty=$Property}
    if($Split){$env:SE_VULKAN_FRAME_SPLIT='1';$env:SE_VULKAN_BUILD_STAGES='2'}
    $run=& (Join-Path $repo 'Tests\ScenePerformanceHarness\Run-Isolated.ps1') @args | Select-Object -Last 1
    Remove-Item Env:SE_VULKAN_FRAME_SPLIT,Env:SE_VULKAN_BUILD_STAGES -ErrorAction SilentlyContinue
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(Get-Process -Id $run.ProcessId -ErrorAction SilentlyContinue){if($timer.Elapsed.TotalMinutes -gt 16){Stop-Process -Id $run.ProcessId -Force;break};Start-Sleep -Seconds 3}
    while(Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue){if($timer.Elapsed.TotalMinutes -gt 18){break};Start-Sleep -Seconds 2}
    $lighting=Join-Path $run.RunRoot 'GameData\Reports\Lighting'
    $rows+=[pscustomobject]@{Label=$label;Run=(Split-Path $run.RunRoot -Leaf);Phases=if(Test-Path $lighting){(Get-ChildItem $lighting -Name) -join ','}else{'none'};Failure=(Get-ChildItem (Join-Path $run.RunRoot 'GameData\Reports') -Filter '*fail*' -Name -ErrorAction SilentlyContinue) -join ','}
}
$rows | Format-List | Out-String -Width 300
if($Out){$rows | ConvertTo-Json | Set-Content -LiteralPath $Out -Encoding utf8}
