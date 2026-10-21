param(
    [Parameter(Mandatory=$true)][string]$World,
    [Parameter(Mandatory=$true)][string]$UserData,
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][string]$Pulsar,
    [ValidateSet('Vulkan','Dx11')][string]$Backend='Vulkan',
    [switch]$RichHudAudit,
    [string]$AnomalyProject,
    [string]$HudDlssProject,
    [switch]$CompareRigidBatching,
    [switch]$CompareBatchHints,
    [switch]$CompareBatchCells,
    [switch]$ReverseBatchCells,
    [switch]$WideBatches,
    [switch]$NarrowBatches,
    [switch]$CompareBatchPixels,
    [switch]$CompareLightPixels,
    [switch]$CompareSubmissionPixels,
    [switch]$CompareGroupPixels,
    [switch]$CpuDetail,
    [switch]$NativeCritical,
    [switch]$WorkloadAudit,
    [switch]$SelectionAudit,
    [switch]$ProbeCoverageAudit,
    [switch]$ShadowDepthAudit,
    [switch]$LightingReconciliation,
    [switch]$LightingRetainedSpots,
    [switch]$LightingGpuScene,
    [switch]$LightingNativePass,
    [string]$LightingProperty,
    [string]$MatchFrame,
    [string]$BenchmarkFrame,
    [switch]$ExpandedGeometry,
    [string]$Nsight,
    [string]$ProfileNative,
    [string]$BenchmarkPlugin,
    [string]$BenchmarkNative,
    [string]$BenchmarkAssets,
    [switch]$DeepProfile,
    [switch]$NsightCpuSampling,
    [switch]$ManagedTrace,
    [switch]$LegacyVoxelLookup,
    [switch]$CompareVoxelLookup,
    [switch]$CompareVoxelObjects,
    [switch]$ReflectedVoxelObjects,
    [switch]$DisableSteamOverlay,
    [switch]$CompareTextureValidation,
    [switch]$LegacyTextureValidation,
    [switch]$CachedTextureValidation,
    [switch]$WorldIndirect,
    [switch]$GeometryArena,
    [switch]$ParallelCascades,
    [switch]$ParallelQueue,
    [switch]$CompareParallelQueue,
    [switch]$LightTiles,
    [switch]$CompareLightTiles,
    [switch]$CompareParallelCascades,
    [switch]$RetainedObjects,
    [switch]$RetainedSubmission,
    [switch]$ActorLods,
    [switch]$ActorSizeCulling,
    [switch]$StockFarCascades,
    [switch]$StockShadowSchedule,
    [switch]$StockShadowFit,
    [switch]$StockShadowOcclusion,
    [switch]$StockDepthGeometry,
    [switch]$NativePassScene,
    [switch]$GpuOpaqueScene,
    [switch]$GpuOpaqueTiming,
    [switch]$GpuOpaqueDeviceLocal,
    [switch]$GpuOpaqueViewBatches,
    [switch]$GpuOpaqueHybrid,
    [switch]$GpuOpaqueRoleStreams,
    [switch]$GpuOpaqueSparseAppearance,
    [switch]$GpuOpaqueResidentDeltas,
    [switch]$GpuOpaqueOwnedInputs,
    [switch]$GpuOpaqueViewMasks,
    [switch]$Validation,
    [switch]$NativeMixedOpaque,
    [switch]$StockLodBounds,
    [switch]$ViewEligibility,
    [switch]$WorldRecipes,
    [switch]$NewActorViews,
    [switch]$ModelBatching,
    [switch]$BulkModels,
    [switch]$CompareBulkModels,
    [switch]$CompareModelBatching,
    [switch]$CompareNewActorViews,
    [switch]$CompareWorldRecipes,
    [switch]$CompareViewEligibility,
    [switch]$AsyncPresent,
    [switch]$CompareAsyncPresent,
    [switch]$CompareFarCascades,
    [switch]$CompareActorSize,
    [switch]$GroupOcclusion,
    [switch]$CompareGroupOcclusion,
    [switch]$CompareActorLods,
    [switch]$CompareRetainedSubmission,
    [switch]$CompareRetainedObjects,
    [switch]$CompareWorldIndirect,
    [ValidateSet('individual','batch')][string]$NsightGpuWorkload='individual',
    [switch]$PassThru
)
$ErrorActionPreference='Stop'
if(($GpuOpaqueViewBatches -or $GpuOpaqueHybrid -or $GpuOpaqueRoleStreams -or $GpuOpaqueSparseAppearance -or $GpuOpaqueResidentDeltas) -and !$GpuOpaqueScene){throw 'GPU path improvements require GpuOpaqueScene'}
if($GpuOpaqueOwnedInputs -and !$GpuOpaqueResidentDeltas){throw 'Owned GPU inputs require GpuOpaqueResidentDeltas'}
if($GpuOpaqueViewMasks -and !$GpuOpaqueScene){throw 'GPU view masks require GpuOpaqueScene'}
if($GpuOpaqueHybrid -and !$NativePassScene){throw 'GPU hybrid ownership requires NativePassScene'}
foreach($taskArtifact in @($BenchmarkPlugin,$BenchmarkNative)){if($taskArtifact -and !(Test-Path -LiteralPath $taskArtifact -PathType Leaf)){throw 'Benchmark artifact must be an existing file.'}}
if(($BenchmarkPlugin -or $BenchmarkNative) -and ($ProfileNative -or $DeepProfile -or $Nsight)){throw 'Pinned ordinary benchmark artifacts cannot be mixed with profiling artifacts.'}
if(($BulkModels -or $CompareBulkModels) -and ($Backend -ne 'Vulkan' -or !$ModelBatching -or !$RetainedSubmission -or !$WorldRecipes -or $RetainedObjects)){throw 'Bulk model groups require Vulkan retained recipe/model submission.'}
if($CompareBulkModels){
    if($Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $WorkloadAudit -or $SelectionAudit){throw 'Bulk model comparison requires ordinary timing.'}
    foreach($taskParameter in $PSBoundParameters.Keys){if($taskParameter -like 'Compare*' -and $taskParameter -ne 'CompareBulkModels' -and $PSBoundParameters[$taskParameter]){throw 'Bulk models comparison requires one treatment.'}}
}
if(($AsyncPresent -or $CompareAsyncPresent) -and $Backend -ne 'Vulkan'){throw 'Async present requires Vulkan.'}
if(($ModelBatching -or $CompareModelBatching) -and ($Backend -ne 'Vulkan' -or !$RetainedSubmission)){throw 'Whole-model batching requires Vulkan retained submission.'}
if($CompareModelBatching -and ($SelectionAudit -or $WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareNewActorViews -or $CompareWorldRecipes -or $CompareViewEligibility -or $CompareAsyncPresent -or $CompareBulkModels -or $CompareFarCascades -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Whole-model comparison requires one ordinary treatment.'}
if(($NewActorViews -or $CompareNewActorViews) -and $Backend -ne 'Vulkan'){throw 'New actor views require Vulkan.'}
if($CompareNewActorViews -and ($SelectionAudit -or $WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareWorldRecipes -or $CompareViewEligibility -or $CompareAsyncPresent -or $CompareBulkModels -or $CompareFarCascades -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'New actor view comparison requires one ordinary treatment.'}
if(($WorldRecipes -or $CompareWorldRecipes) -and ($Backend -ne 'Vulkan' -or !$RetainedSubmission -or $RetainedObjects)){throw 'Native recipes require Vulkan retained submission without retained-object hashing.'}
if($CompareWorldRecipes -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareViewEligibility -or $CompareAsyncPresent -or $CompareBulkModels -or $CompareFarCascades -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Recipe comparison requires one ordinary treatment.'}
if(($ViewEligibility -or $CompareViewEligibility) -and $Backend -ne 'Vulkan'){throw 'View eligibility requires Vulkan.'}
if($CompareViewEligibility -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareAsyncPresent -or $CompareBulkModels -or $CompareFarCascades -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'View eligibility comparison requires one ordinary treatment.'}
if($CompareAsyncPresent -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareFarCascades -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Async present comparison requires one ordinary treatment.'}
if(($StockFarCascades -or $CompareFarCascades) -and $Backend -ne 'Vulkan'){throw 'Far-cascade policy requires Vulkan.'}
if($CompareFarCascades -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareActorSize -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Far-cascade comparison requires one ordinary treatment.'}
if(($ActorSizeCulling -or $CompareActorSize) -and $Backend -ne 'Vulkan'){throw 'Actor size policy requires Vulkan.'}
if($CompareActorSize -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareActorLods -or $CompareGroupOcclusion -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareParallelQueue -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Actor size comparison requires one ordinary treatment.'}
if(($ParallelQueue -or $CompareParallelQueue) -and $Backend -ne 'Vulkan'){throw 'Parallel queue requires Vulkan.'}
if($CompareParallelQueue -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareGroupOcclusion -or $CompareActorLods -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareGroupPixels -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Parallel queue comparison requires one ordinary treatment.'}
if($CompareGroupPixels -and ($Backend -ne 'Vulkan' -or $CompareGroupOcclusion -or $CompareActorLods -or $CompareSubmissionPixels -or $CompareBatchPixels -or $CompareLightPixels -or $WorkloadAudit -or $ManagedTrace -or $DeepProfile -or $Nsight)){throw 'Group pixel capture requires one standalone Vulkan diagnostic.'}
if(($GroupOcclusion -or $CompareGroupOcclusion) -and $Backend -ne 'Vulkan'){throw 'Group occlusion requires Vulkan.'}
if($CompareGroupOcclusion -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareActorLods -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Group occlusion comparison requires one ordinary treatment.'}
if(($ActorLods -or $CompareActorLods) -and $Backend -ne 'Vulkan'){throw 'Actor LOD selection requires Vulkan.'}
if($CompareActorLods -and ($WorkloadAudit -or $Nsight -or $ManagedTrace -or $DeepProfile -or $CpuDetail -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $CompareRetainedSubmission -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Actor LOD comparison requires one ordinary treatment.'}
if($WorkloadAudit -and ($CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $CompareSubmissionPixels -or $Nsight -or $ManagedTrace -or $DeepProfile)){throw 'WorkloadAudit is a standalone diagnostic lane.'}
if($WorkloadAudit -and ($CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareRetainedObjects -or $CompareParallelCascades -or $CompareLightTiles)){throw 'WorkloadAudit cannot combine with comparison treatments.'}
if(($RetainedSubmission -or $CompareRetainedSubmission) -and ($Backend -ne 'Vulkan' -or $RetainedObjects -or $CompareRetainedObjects)){throw 'Retained submission requires Vulkan and cannot combine with retained object storage.'}
if($CompareSubmissionPixels -and ($Backend -ne 'Vulkan' -or $CompareRetainedSubmission -or $CompareBatchPixels -or $CompareLightPixels -or $RetainedObjects -or $DeepProfile -or $Nsight -or $ManagedTrace)){throw 'Submission pixel comparison requires a standalone Vulkan diagnostic.'}
if($CompareRetainedSubmission -and ($DeepProfile -or $Nsight -or $ManagedTrace -or $CpuDetail -or $CompareBatchPixels -or $CompareLightPixels -or $CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $CompareVoxelLookup -or $CompareVoxelObjects -or $CompareTextureValidation -or $CompareWorldIndirect -or $CompareParallelCascades -or $CompareLightTiles)){throw 'Retained submission comparison requires one ordinary treatment.'}
if(Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue){throw 'A game is already running.'}
if(([int][bool]$CompareRigidBatching+[int][bool]$CompareBatchHints+[int][bool]$CompareBatchCells+[int][bool]$CompareVoxelLookup+[int][bool]$CompareVoxelObjects+[int][bool]$CompareTextureValidation+[int][bool]$CompareWorldIndirect+[int][bool]$CompareRetainedObjects+[int][bool]$CompareParallelCascades+[int][bool]$CompareLightTiles) -gt 1){throw 'Choose one comparison treatment per run.'}
if($CompareWorldIndirect -and ($Backend -ne 'Vulkan' -or $Nsight -or $ManagedTrace -or $DisableSteamOverlay -or $CompareBatchPixels -or $CpuDetail)){throw 'World indirect comparison requires an ordinary Vulkan workload.'}
if($CompareTextureValidation -and ($Backend -ne 'Vulkan' -or $Nsight -or $ManagedTrace -or $DisableSteamOverlay -or $CompareBatchPixels)){throw 'Texture validation comparison requires an ordinary Vulkan workload.'}
if($CachedTextureValidation -and $LegacyTextureValidation){throw 'Choose one texture validation treatment.'}
if(($RetainedObjects -or $CompareRetainedObjects) -and $Backend -ne 'Vulkan'){throw 'Retained object comparison requires Vulkan.'}
if($CompareRetainedObjects -and ($Nsight -or $ManagedTrace -or $CpuDetail -or $CompareBatchPixels)){throw 'Retained object comparison requires an ordinary workload.'}
if(($ParallelCascades -or $CompareParallelCascades) -and $Backend -ne 'Vulkan'){throw 'Parallel cascades require Vulkan.'}
if($CompareParallelCascades -and ($Nsight -or $ManagedTrace -or $CpuDetail -or $CompareBatchPixels)){throw 'Parallel cascade comparison requires an ordinary workload.'}
if(($LightTiles -or $CompareLightTiles) -and $Backend -ne 'Vulkan'){throw 'GPU light tiles require Vulkan.'}
if($CompareLightTiles -and ($Nsight -or $ManagedTrace -or $CpuDetail -or $CompareBatchPixels)){throw 'Light tile comparison requires an ordinary workload.'}
if($GeometryArena -and $Backend -ne 'Vulkan'){throw 'Geometry arena requires the Vulkan backend.'}
if($WorldIndirect -and $Backend -ne 'Vulkan'){throw 'World indirect requires the Vulkan backend.'}
if($CompareVoxelLookup -and ($CompareBatchPixels -or $Nsight -or $ManagedTrace -or $Backend -ne 'Vulkan')){throw 'Voxel lookup comparison requires a standalone Vulkan benchmark.'}
if($CompareVoxelObjects -and ($CompareBatchPixels -or $Nsight -or $ManagedTrace -or $Backend -ne 'Vulkan')){throw 'Voxel object comparison requires a standalone Vulkan benchmark.'}
if($ReverseBatchCells -and !$CompareBatchCells){throw 'ReverseBatchCells requires CompareBatchCells.'}
if($WideBatches -and $NarrowBatches){throw 'Choose one spatial batch width.'}
if($CompareBatchPixels -and ($CompareRigidBatching -or $CompareBatchHints -or $CompareBatchCells -or $Nsight -or $Backend -ne 'Vulkan')){throw 'Pixel comparison requires a standalone Vulkan run.'}
if($CompareLightPixels -and ($CompareBatchPixels -or $CompareLightTiles -or $CompareParallelCascades -or $CompareRetainedObjects -or $CompareTextureValidation -or $Nsight -or $ManagedTrace -or $Backend -ne 'Vulkan')){throw 'Light pixel comparison requires a standalone Vulkan diagnostic.'}
if($ProfileNative -and ((!$DeepProfile -and !($Nsight -and $NsightCpuSampling)) -or !(Test-Path -LiteralPath $ProfileNative -PathType Leaf) -or !(Test-Path -LiteralPath ([IO.Path]::ChangeExtension($ProfileNative,'pdb')) -PathType Leaf))){throw 'ProfileNative requires a native CPU profiling lane and matching DLL/PDB.'}
if($ProfileNative){
    & dotnet run --project (Join-Path $PSScriptRoot '../DeepCpuAnalysis/DeepCpuAnalysis.csproj') -c Release -- --check-native-profile $ProfileNative | Out-Host
    if($LASTEXITCODE -ne 0){throw 'Native DLL/PDB signature contract failed before private launch.'}
}
if($DeepProfile -and ($Nsight -or $ManagedTrace -or $CompareLightTiles -or $CompareParallelCascades -or $CompareRetainedObjects)){throw 'DeepProfile is a standalone profiling lane.'}
if($Nsight -and !(Test-Path -LiteralPath $Nsight -PathType Leaf)){throw 'Nsight executable missing.'}
if($NsightCpuSampling -and !$Nsight){throw 'CPU sampling requires an explicit Nsight executable.'}
if($Nsight -and !([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))){throw 'Nsight Vulkan layer registration requires an elevated PowerShell session. Run Capture-Nsight.ps1 from that session.'}
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourceWorld=[IO.Path]::GetFullPath($World)
$sourceData=[IO.Path]::GetFullPath($UserData)
$sourcePulsar=[IO.Path]::GetFullPath($Pulsar)
if(!(Test-Path -LiteralPath (Join-Path $sourceWorld 'Sandbox.sbc'))){throw 'Source checkpoint missing.'}
if((Get-Item -LiteralPath $sourceWorld).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked source world not supported.'}
$linked=Get-ChildItem -LiteralPath $sourceWorld -Recurse -Attributes ReparsePoint
if($linked){throw 'Linked files in source world not supported.'}
& dotnet build (Join-Path $PSScriptRoot 'ScenePerformanceHarness.csproj') -c Release -p:RunPostBuildEvent=Never | Out-Host
if($LASTEXITCODE -ne 0){throw 'Benchmark build failed.'}
$runRoot=Join-Path $repository ('TestResults/ScenePerformance/'+$Backend+'-'+[Guid]::NewGuid().ToString('N'))
$data=Join-Path $runRoot 'GameData'
$portable=Join-Path $runRoot 'Pulsar'
$edition=Join-Path $portable 'Interim'
$localVulkan=Join-Path $edition 'Local/Vulkan'
$localHarness=Join-Path $edition 'Local/ScenePerformanceHarness'
New-Item -ItemType Directory -Force -Path $data,$portable,$edition,$localVulkan,$localHarness,(Join-Path $data 'World'),(Join-Path $data 'Storage'),(Join-Path $edition 'Profiles') | Out-Null
foreach($file in 'Interim.exe','Interim.dll','Interim.deps.json','Interim.runtimeconfig.json'){
    Copy-Item -LiteralPath (Join-Path $sourcePulsar $file) -Destination $portable
}
foreach($library in 'Interim','Compiler','Interface'){
    $destination=Join-Path $portable ('Libraries/'+$library)
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $sourcePulsar ('Libraries/'+$library)) | Copy-Item -Destination $destination -Recurse
}
foreach($folder in 'GitHub','NuGet','Sources'){
    $source=Join-Path $sourcePulsar ('Interim/'+$folder)
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $edition -Recurse}
}
$sourcesPath=Join-Path $edition 'Sources/sources.xml'
if(Test-Path -LiteralPath $sourcesPath){
    $sources=[xml](Get-Content -LiteralPath $sourcesPath -Raw)
    foreach($name in 'LocalPluginSources','RemotePluginSources'){$node=$sources.DocumentElement.SelectSingleNode($name);if($node){$node.RemoveAll()}}
    $sources.Save($sourcesPath)
}
Copy-Item -LiteralPath (Join-Path $sourcePulsar 'Interim/config.xml') -Destination $edition
Copy-Item -LiteralPath $(if($BenchmarkPlugin){$BenchmarkPlugin}else{Join-Path $repository 'ClientPlugin/bin/Release/net10.0/Vulkan.dll'}) -Destination (Join-Path $localVulkan 'plugin.dll')
Copy-Item -LiteralPath (Join-Path $repository 'Vulkan.xml') -Destination (Join-Path $localVulkan 'plugin.xml')
if($BenchmarkAssets -and !(Test-Path -LiteralPath $BenchmarkAssets -PathType Container)){throw 'Benchmark assets must be an existing directory'}
Copy-Item -LiteralPath $(if($BenchmarkAssets){$BenchmarkAssets}else{Join-Path $repository 'Assets'}) -Destination (Join-Path $localVulkan 'Assets') -Recurse
if($DeepProfile){
    Copy-Item -LiteralPath (Join-Path $repository 'ClientPlugin/bin/Release/net10.0/Vulkan.pdb') -Destination (Join-Path $localVulkan 'plugin.pdb')
    Copy-Item -LiteralPath (Join-Path $repository 'ClientPlugin/bin/Release/net10.0/Vulkan.pdb') -Destination (Join-Path $localVulkan 'Vulkan.pdb')
    if($ProfileNative){
        Copy-Item -LiteralPath $ProfileNative -Destination (Join-Path $localVulkan 'Assets/Native/win-x64/se_vulkan_native.dll')
        Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($ProfileNative,'pdb')) -Destination (Join-Path $localVulkan 'Assets/Native/win-x64/se_vulkan_native.pdb')
    }
}
if($BenchmarkNative){Copy-Item -LiteralPath $BenchmarkNative -Destination (Join-Path $localVulkan 'Assets/Native/win-x64/se_vulkan_native.dll')}

if($RichHudAudit){
    if(!$AnomalyProject){throw 'RichHudAudit requires AnomalyProject (repository path).'}
    $hudProject=Join-Path $AnomalyProject 'ClientPlugin/ClientPlugin.csproj'
    if(!(Test-Path -LiteralPath $hudProject)){throw 'Anomaly shared project missing.'}
    & dotnet build $hudProject -c Release -f net10.0 -p:RunPostBuildEvent=Never | Out-Host
    if($LASTEXITCODE -ne 0){throw 'Anomaly audit build failed.'}
    $localAnomaly=Join-Path $edition 'Local/Anomaly'
    New-Item -ItemType Directory -Force -Path $localAnomaly | Out-Null
    Copy-Item -LiteralPath (Join-Path $AnomalyProject 'ClientPlugin/bin/Release/net10.0/Anomaly.dll') -Destination (Join-Path $localAnomaly 'plugin.dll')
    Copy-Item -LiteralPath (Join-Path $AnomalyProject 'Anomaly.xml') -Destination (Join-Path $localAnomaly 'plugin.xml')
    Copy-Item -LiteralPath (Join-Path $AnomalyProject 'Assets') -Destination $localAnomaly -Recurse
}
if($RichHudAudit -and $HudDlssProject){
    & dotnet build (Join-Path $HudDlssProject 'ClientPlugin/ClientPlugin.csproj') -c Release -f net10.0 -p:RunPostBuildEvent=Never -p:SkipDx11Satellite=true | Out-Host
    if($LASTEXITCODE -ne 0){throw 'DLSS audit build failed.'}
    $localDlss=Join-Path $edition 'Local/SpaceEngineersDLSS'
    New-Item -ItemType Directory -Force -Path $localDlss | Out-Null
    Copy-Item -LiteralPath (Join-Path $HudDlssProject 'ClientPlugin/bin/Release/net10.0/SpaceEngineersDLSS.dll') -Destination (Join-Path $localDlss 'plugin.dll')
    Copy-Item -LiteralPath (Join-Path $HudDlssProject 'SpaceEngineersDLSS.xml') -Destination (Join-Path $localDlss 'plugin.xml')
    Copy-Item -LiteralPath (Join-Path $HudDlssProject 'Assets') -Destination $localDlss -Recurse
    # Local DLSS deployment is documented to discover NGX beside the DLL;
    # remote Url assets have no local Path for Pulsar's LocalPlugin resolver.
    $dlssManifestPath=Join-Path $localDlss 'plugin.xml'
    $dlssManifest=[xml](Get-Content -LiteralPath $dlssManifestPath -Raw)
    foreach($asset in @($dlssManifest.DocumentElement.SelectNodes('Asset'))){[void]$asset.ParentNode.RemoveChild($asset)}
    $dlssManifest.Save($dlssManifestPath)
    Copy-Item -LiteralPath (Join-Path $HudDlssProject 'Assets/nvngx_dlss.dll') -Destination $localDlss
    Copy-Item -LiteralPath (Join-Path $sourceData 'Storage/SpaceEngineersDLSS.cfg') -Destination (Join-Path $data 'Storage/SpaceEngineersDLSS.cfg')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/net10.0/ScenePerformanceHarness.dll') -Destination (Join-Path $localHarness 'plugin.dll')
Get-ChildItem -LiteralPath $sourceWorld | Where-Object Name -ne 'Backup' | Copy-Item -Destination (Join-Path $data 'World') -Recurse
foreach($file in 'Sandbox.sbc','Sandbox_config.sbc'){
    $path=Join-Path $data ('World/'+$file)
    if(Test-Path -LiteralPath $path){$checkpoint=[xml](Get-Content -LiteralPath $path -Raw);$autosave=$checkpoint.SelectSingleNode('//Settings/AutoSaveInMinutes');if($autosave){$autosave.InnerText='0';$checkpoint.Save($path)}}
}
Copy-Item -LiteralPath (Join-Path $sourceData 'SpaceEngineers.cfg') -Destination $data
$configPath=Join-Path $data 'SpaceEngineers.cfg'
$config=[xml](Get-Content -LiteralPath $configPath -Raw)
foreach($item in $config.SelectNodes('//dictionary/item')){
    $value=switch($item.Key){'ScreenWidth'{'1920'} 'ScreenHeight'{'1080'} 'VerticalSync'{'0'} 'ExperimentalMode'{'True'} 'EnableCloud'{'False'} default{$null}}
    if($null -ne $value){$item.SelectSingleNode('Value/Value').InnerText=$value}
}
$sevkXmlSettings=[Xml.XmlWriterSettings]::new();$sevkXmlSettings.Indent=$false
$sevkWriter=[Xml.XmlWriter]::Create($configPath,$sevkXmlSettings)
try{$config.Save($sevkWriter)}finally{$sevkWriter.Dispose()}
if(Test-Path -LiteralPath (Join-Path $sourceData 'SmoothFrames.cfg')){
    $cap=[xml](Get-Content -LiteralPath (Join-Path $sourceData 'SmoothFrames.cfg') -Raw)
    $cap.DocumentElement.FrameRateCap='1000';$cap.Save((Join-Path $data 'SmoothFrames.cfg'))
}
@"
<VulkanConfig><RequestedBackend>$Backend</RequestedBackend><EnableValidation>$($Validation.IsPresent.ToString().ToLowerInvariant())</EnableValidation><RecoveryRequired>false</RecoveryRequired><RetryGeneration>0</RetryGeneration><LastFailure /></VulkanConfig>
"@ | Set-Content -LiteralPath (Join-Path $data 'Storage/Vulkan.cfg') -Encoding utf8
[IO.File]::WriteAllText((Join-Path $data '.se-vulkan-performance'),$data)
$profile=[xml](Get-Content -LiteralPath (Join-Path $sourcePulsar 'Interim/Profiles/Current.xml') -Raw)
$profile.Profile.Name='Isolated scene performance '+$Backend
$profile.Profile.SelectSingleNode('DevFolder').RemoveAll()
$local=$profile.Profile.SelectSingleNode('Local');$local.RemoveAll()
foreach($id in 'AE72EBB8-F4A8-4ADB-A400-19CC46FDAB79','ScenePerformanceHarness.dll'){$node=$profile.CreateElement('string');$node.InnerText=$id;[void]$local.AppendChild($node)}
if($RichHudAudit){$node=$profile.CreateElement('string');$node.InnerText='A9C29274-E447-49EE-881B-C980E6D190FD';[void]$local.AppendChild($node)}
if($RichHudAudit -and $HudDlssProject){$node=$profile.CreateElement('string');$node.InnerText='B6469FEE-83BF-44A9-93F9-B4A252527C35';[void]$local.AppendChild($node)}
$profilePath=Join-Path $edition 'Profiles/ScenePerformance.xml';$profile.Save($profilePath)
$arguments='-bin64 "{0}" -profile "{1}" -appdata "{2}" -noUpdate -noSplash -noDiscord -noStats -noPrompt -stableLogs' -f $Bin64,$profilePath,$data
$changes=@{SE_VULKAN_PERF_DATA=$data;SE_VULKAN_PERF_BACKEND=$Backend;SE_VULKAN_ASSET_DIR=(Join-Path $localVulkan 'Assets')}
$changes.SE_VULKAN_HUD_REQUIRE_NGX=if($RichHudAudit -and $HudDlssProject){'1'}else{'0'}
$changes.SE_VULKAN_HUD_AUDIT=if($RichHudAudit){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_BATCHING=if($CompareRigidBatching){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_BATCH_HINTS=if($CompareBatchHints){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_BATCH_CELLS=if($CompareBatchCells){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_LIGHT_PIXELS=if($CompareLightPixels){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_SUBMISSION_PIXELS=if($CompareSubmissionPixels){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_GROUP_PIXELS=if($CompareGroupPixels){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_BATCH_PIXELS=if($CompareBatchPixels){'1'}else{'0'}
$changes.SE_VULKAN_WIDE_RIGID_BATCHES=if($CompareBatchPixels -or $NarrowBatches -or ($CompareBatchCells -and $ReverseBatchCells)){'0'}else{'1'}
$changes.SE_VULKAN_CPU_DETAIL=if($CpuDetail){'1'}else{'0'}
$changes.SE_VULKAN_VOXEL_MATERIAL_CACHE=if($LegacyVoxelLookup){'0'}else{'1'}
$changes.SE_VULKAN_COMPARE_VOXEL_LOOKUP=if($CompareVoxelLookup){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_VOXEL_OBJECTS=if($CompareVoxelObjects){'1'}else{'0'}
$changes.SE_VULKAN_VOXEL_OBJECT_CACHE=if($ReflectedVoxelObjects){'0'}else{'1'}
$changes.SE_VULKAN_TEXTURE_VALIDATION_CACHE=if($LegacyTextureValidation){'0'}else{'1'}
$changes.SE_VULKAN_COMPARE_TEXTURE_VALIDATION=if($CompareTextureValidation){'1'}else{'0'}
$changes.SE_VULKAN_LIGHT_TILES=if($LightTiles){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_LIGHT_TILES=if($CompareLightTiles){'1'}else{'0'}
$changes.SE_VULKAN_PARALLEL_CASCADES=if($ParallelCascades){'1'}else{'0'}
$changes.SE_VULKAN_PARALLEL_QUEUE=if($ParallelQueue){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_PARALLEL_QUEUE=if($CompareParallelQueue){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_PARALLEL_CASCADES=if($CompareParallelCascades){'1'}else{'0'}
$changes.SE_VULKAN_RETAINED_OBJECTS=if($RetainedObjects){'1'}else{'0'}
$changes.SE_VULKAN_RETAINED_SUBMISSION=if($RetainedSubmission){'1'}else{'0'}
$changes.SE_VULKAN_ASYNC_PRESENT=if($AsyncPresent){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_ASYNC_PRESENT=if($CompareAsyncPresent){'1'}else{'0'}
$changes.SE_VULKAN_MATCH_FRAME=if($MatchFrame){if(!$SelectionAudit){throw 'MatchFrame requires paused SelectionAudit'};(Resolve-Path -LiteralPath $MatchFrame).Path}else{''}
$changes.SE_VULKAN_BENCHMARK_FRAME=if($BenchmarkFrame){if($SelectionAudit -or $MatchFrame){throw 'BenchmarkFrame cannot be combined with paused SelectionAudit/MatchFrame'};(Resolve-Path -LiteralPath $BenchmarkFrame).Path}else{''}
$changes.SE_VULKAN_PRESENT_SAMPLES='1'
$changes.SE_VULKAN_BULK_MODELS=if($BulkModels){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_BULK_MODELS=if($CompareBulkModels){'1'}else{'0'}
$changes.SE_VULKAN_MODEL_BATCHING=if($ModelBatching){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_MODEL_BATCHING=if($CompareModelBatching){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_DEVICE_LOCAL=if($GpuOpaqueDeviceLocal){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_VIEW_BATCHES=if($GpuOpaqueViewBatches){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_HYBRID=if($GpuOpaqueHybrid){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_ROLE_STREAMS=if($GpuOpaqueRoleStreams){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_SPARSE_APPEARANCE=if($GpuOpaqueSparseAppearance){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_RESIDENT_DELTAS=if($GpuOpaqueResidentDeltas){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_OWNED_INPUTS=if($GpuOpaqueOwnedInputs){'1'}else{'0'}
 $changes.SE_VULKAN_GPU_OPAQUE_VIEW_MASKS=if($GpuOpaqueViewMasks){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_TIMING=if($GpuOpaqueTiming){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE=if($GpuOpaqueScene){'1'}else{'0'}
$changes.SE_VULKAN_GPU_OPAQUE_SCENE=if($GpuOpaqueScene){'1'}else{'0'}
if($Validation){
    $taskValidationLayer=Join-Path $repository 'Native/build/validation/Bin'
    foreach($taskLayerFile in 'VkLayer_khronos_validation.json','VkLayer_khronos_validation.dll'){
        if(!(Test-Path -LiteralPath (Join-Path $taskValidationLayer $taskLayerFile))){throw 'Private Khronos validation layer missing'}
    }
    $changes.VK_LAYER_PATH=$taskValidationLayer
    $changes.VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
    $changes.VK_VALIDATION_VALIDATE_SYNC='1'
}
$changes.SE_VULKAN_NEW_ACTOR_VIEWS=if($NewActorViews){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_NEW_ACTOR_VIEWS=if($CompareNewActorViews){'1'}else{'0'}
$changes.SE_VULKAN_WORLD_RECIPES=if($WorldRecipes){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_WORLD_RECIPES=if($CompareWorldRecipes){'1'}else{'0'}
$changes.SE_VULKAN_VIEW_ELIGIBILITY=if($ViewEligibility){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_VIEW_ELIGIBILITY=if($CompareViewEligibility){'1'}else{'0'}
$changes.SE_VULKAN_STOCK_FAR_CASCADES=if($StockFarCascades){'1'}else{'0'}
$changes.SE_VULKAN_STOCK_SHADOW_SCHEDULE=if($StockShadowSchedule){'1'}else{'0'}
$changes.SE_VULKAN_STOCK_SHADOW_FIT=if($StockShadowFit){if(!$StockShadowSchedule){throw 'StockShadowFit requires StockShadowSchedule'};'1'}else{'0'}
$changes.SE_VULKAN_STOCK_SHADOW_OCCLUSION=if($StockShadowOcclusion){if(!$StockShadowFit){throw 'StockShadowOcclusion requires StockShadowFit'};'1'}else{'0'}
$changes.SE_VULKAN_SHADOW_DEPTH_AUDIT=if($ShadowDepthAudit){if(!$SelectionAudit){throw 'ShadowDepthAudit requires paused SelectionAudit'};'1'}else{'0'}
$changes.SE_VULKAN_LIGHTING_RETAINED_SPOTS=if($LightingRetainedSpots){if(!$LightingReconciliation){throw 'LightingRetainedSpots requires LightingReconciliation'};'1'}else{'0'}
$changes.SE_VULKAN_LIGHTING_GPU_SCENE=if($LightingGpuScene){if(!$LightingReconciliation -or !$GpuOpaqueScene){throw 'LightingGpuScene requires lighting reconciliation and GPU scene'};'1'}else{'0'}
$changes.SE_VULKAN_LIGHTING_NATIVE_PASS=if($LightingNativePass){if(!$LightingReconciliation -or $LightingGpuScene){throw 'LightingNativePass requires lighting reconciliation, without LightingGpuScene'};'1'}else{'0'}
$changes.SE_VULKAN_LIGHTING_PROPERTY=if($LightingProperty){if(!$LightingReconciliation -or $LightingGpuScene -or $LightingNativePass){throw 'LightingProperty requires lighting reconciliation without another ownership treatment'};$LightingProperty}else{''}
$changes.SE_VULKAN_CRITICAL_PROFILE=if($NativeCritical){if(!$CpuDetail -or $Backend -ne 'Vulkan'){throw 'NativeCritical requires Vulkan CpuDetail'};'1'}else{'0'}
$changes.SE_VULKAN_LIGHTING_RECONCILIATION=if($LightingReconciliation){if(!$SelectionAudit -or $Backend -ne 'Vulkan'){throw 'LightingReconciliation requires private paused Vulkan capture'};'1'}else{'0'}
$changes.SE_VULKAN_PROBE_COVERAGE_AUDIT=if($ProbeCoverageAudit){if(!$SelectionAudit){throw 'ProbeCoverageAudit requires paused SelectionAudit'};'1'}else{'0'}
$changes.SE_VULKAN_STOCK_DEPTH_GEOMETRY=if($StockDepthGeometry){'1'}else{'0'}
$changes.SE_VULKAN_NATIVE_PASS_SCENE=if($NativePassScene){if($Backend -ne 'Vulkan' -or !$NewActorViews -or !$GroupOcclusion -or !$StockShadowFit -or !$StockDepthGeometry){throw 'NativePassScene requires Vulkan new views, group occlusion, stock fitting and shared depth geometry'};'1'}else{'0'}
$changes.SE_VULKAN_NATIVE_MIXED_OPAQUE=if($NativeMixedOpaque){if(!$NativePassScene){throw 'NativeMixedOpaque requires NativePassScene'};'1'}else{'0'}
$changes.SE_VULKAN_STOCK_LOD_BOUNDS=if($StockLodBounds){if($Backend -ne 'Vulkan'){throw 'StockLodBounds requires Vulkan'};'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_FAR_CASCADES=if($CompareFarCascades){'1'}else{'0'}
$changes.SE_VULKAN_ACTOR_SIZE_CULLING=if($ActorSizeCulling){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_ACTOR_SIZE=if($CompareActorSize){'1'}else{'0'}
$changes.SE_VULKAN_ACTOR_LODS=if($ActorLods){'1'}else{'0'}
$changes.SE_VULKAN_GROUP_OCCLUSION=if($GroupOcclusion){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_GROUP_OCCLUSION=if($CompareGroupOcclusion){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_ACTOR_LODS=if($CompareActorLods){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_RETAINED_SUBMISSION=if($CompareRetainedSubmission){'1'}else{'0'}
$changes.SE_VULKAN_SELECTION_AUDIT=if($SelectionAudit){'1'}else{'0'}
$changes.SE_VULKAN_WORKLOAD_AUDIT=if($WorkloadAudit){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_RETAINED_OBJECTS=if($CompareRetainedObjects){'1'}else{'0'}
$changes.SE_VULKAN_GEOMETRY_ARENA=if($GeometryArena){'1'}else{'0'}
$changes.SE_VULKAN_WORLD_INDIRECT=if($WorldIndirect){'1'}else{'0'}
$changes.SE_VULKAN_COMPARE_WORLD_INDIRECT=if($CompareWorldIndirect){'1'}else{'0'}
$changes.SE_VULKAN_EXPANDED_GEOMETRY=if($ExpandedGeometry){'1'}else{'0'}
$changes.SE_VULKAN_PERF_INSTRUMENTED=if($Nsight -or $ManagedTrace -or $DeepProfile -or $WorkloadAudit -or $CompareGroupPixels -or $CpuDetail -or $NativeCritical -or $GpuOpaqueTiming -or $Validation){'1'}else{'0'}
if($DisableSteamOverlay){
    # Diagnostic only, using the installed layer manifest's disable contract.
    # Never edit the registry, Steam preferences or the user's source profile.
    $changes.DISABLE_VK_LAYER_VALVE_steam_overlay_1='1'
    $changes.SE_VULKAN_PERF_INSTRUMENTED='1'
}
# Suppress installed Nsight implicit layers in benchmark children. This is
# process-local and restores the shell's environment after launch.
foreach($suffix in '','_systemwide','_reflex','_reflex_systemwide'){
    $changes[('DISABLE_VK_LAYER_NV_nsight_sys'+$suffix)]=if($Nsight -and !$NsightCpuSampling){'0'}else{'1'}
}
$previous=@{}
$session='SeVulkan'+[Guid]::NewGuid().ToString('N')
if($Nsight){$changes.SE_VULKAN_PROFILE_MARKERS=if(!$NsightCpuSampling -and $NsightGpuWorkload -eq 'individual'){'1'}else{'0'}}
foreach($key in $changes.Keys){$previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$changes[$key],'Process')}
try{
    $executable=Join-Path $portable 'Interim.exe'
    if($Nsight){
        # Collection starts after the private world is warmed. Keep CPU-only
        # sampling separate from GPU/API tracing and its collector overhead.
        if($NsightCpuSampling){
            $trace='none'
            $arguments='profile --trace=none --sample=process-tree --cpuctxsw=process-tree --sampling-frequency=1000 --resolve-symbols=true --discard-environment=true --start-later=true --duration=10 --kill=false --export=sqlite --session-new={0} --output="{1}" "{2}" {3}' -f $session,(Join-Path $runRoot 'nsight'),$executable,$arguments
        }else{
            $trace=if($NsightGpuWorkload -eq 'batch'){'vulkan'}else{'vulkan-annotations'}
            $arguments='profile --trace={4} --vulkan-gpu-workload={5} --sample=none --cpuctxsw=none --discard-environment=true --start-later=true --duration=10 --kill=false --export=sqlite --session-new={0} --output="{1}" "{2}" {3}' -f $session,(Join-Path $runRoot 'nsight'),$executable,$arguments,$trace,$NsightGpuWorkload
        }
        $executable=$Nsight
    }
    $child=Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $portable -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.txt') -RedirectStandardError (Join-Path $runRoot 'stderr.txt')
}
finally{foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')}}
$manifest=[pscustomobject]@{RunRoot=$runRoot;ProcessId=$child.Id;SourceWorld=$sourceWorld;Backend=$Backend;Width=1920;Height=1080;Validation=[bool]$Validation;FrameRateCap=1000;Runtime='net10.0';PluginSha256=(Get-FileHash -LiteralPath (Join-Path $localVulkan 'plugin.dll')).Hash;NativeSha256=(Get-FileHash -LiteralPath (Join-Path $localVulkan 'Assets/Native/win-x64/se_vulkan_native.dll')).Hash;SourceCheckpointSha256=(Get-FileHash -LiteralPath (Join-Path $sourceWorld 'Sandbox.sbc')).Hash}
$manifest | Add-Member -NotePropertyName StockShadowScheduleRequested -NotePropertyValue ([bool]$StockShadowSchedule)
$manifest | Add-Member -NotePropertyName GpuOpaqueDeviceLocalRequested -NotePropertyValue ([bool]$GpuOpaqueDeviceLocal)
$manifest | Add-Member -NotePropertyName GpuOpaqueViewBatchesRequested -NotePropertyValue ([bool]$GpuOpaqueViewBatches)
$manifest | Add-Member -NotePropertyName GpuOpaqueHybridRequested -NotePropertyValue ([bool]$GpuOpaqueHybrid)
$manifest | Add-Member -NotePropertyName GpuOpaqueRoleStreamsRequested -NotePropertyValue ([bool]$GpuOpaqueRoleStreams)
$manifest | Add-Member -NotePropertyName GpuOpaqueSparseAppearanceRequested -NotePropertyValue ([bool]$GpuOpaqueSparseAppearance)
$manifest | Add-Member -NotePropertyName GpuOpaqueResidentDeltasRequested -NotePropertyValue ([bool]$GpuOpaqueResidentDeltas)
$manifest | Add-Member -NotePropertyName GpuOpaqueOwnedInputsRequested -NotePropertyValue ([bool]$GpuOpaqueOwnedInputs)
$manifest | Add-Member -NotePropertyName GpuOpaqueViewMasksRequested -NotePropertyValue ([bool]$GpuOpaqueViewMasks)
$manifest | Add-Member -NotePropertyName GpuOpaqueTimingRequested -NotePropertyValue ([bool]$GpuOpaqueTiming)
$manifest | Add-Member -NotePropertyName GpuOpaqueSceneRequested -NotePropertyValue ([bool]$GpuOpaqueScene)
$manifest | Add-Member -NotePropertyName NativePassSceneRequested -NotePropertyValue ([bool]$NativePassScene)
$manifest | Add-Member -NotePropertyName NativeMixedOpaqueRequested -NotePropertyValue ([bool]$NativeMixedOpaque)
$manifest | Add-Member -NotePropertyName StockLodBoundsRequested -NotePropertyValue ([bool]$StockLodBounds)
$manifest | Add-Member -NotePropertyName StockShadowFitRequested -NotePropertyValue ([bool]$StockShadowFit)
$manifest | Add-Member -NotePropertyName StockDepthGeometryRequested -NotePropertyValue ([bool]$StockDepthGeometry)
$manifest | Add-Member -NotePropertyName StockShadowOcclusionRequested -NotePropertyValue ([bool]$StockShadowOcclusion)
$manifest | Add-Member -NotePropertyName DeepProfile -NotePropertyValue ([bool]$DeepProfile)
if($ProfileNative){$manifest | Add-Member -NotePropertyName NativePdbSha256 -NotePropertyValue (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($ProfileNative,'pdb'))).Hash}
$manifest | Add-Member -NotePropertyName HarnessSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $localHarness 'plugin.dll')).Hash
$manifest | Add-Member -NotePropertyName LightTilesRequested -NotePropertyValue ([bool]$LightTiles)
$manifest | Add-Member -NotePropertyName ParallelCascadesRequested -NotePropertyValue ([bool]$ParallelCascades)
$manifest | Add-Member -NotePropertyName ParallelQueueRequested -NotePropertyValue ([bool]$ParallelQueue)
$manifest | Add-Member -NotePropertyName RetainedObjectsRequested -NotePropertyValue ([bool]$RetainedObjects)
$manifest | Add-Member -NotePropertyName RetainedSubmissionRequested -NotePropertyValue ([bool]$RetainedSubmission)
$manifest | Add-Member -NotePropertyName AsyncPresentRequested -NotePropertyValue ([bool]$AsyncPresent)
$manifest | Add-Member -NotePropertyName NativeCriticalRequested -NotePropertyValue ([bool]$NativeCritical)
$manifest | Add-Member -NotePropertyName LightingReconciliationRequested -NotePropertyValue ([bool]$LightingReconciliation)
$manifest | Add-Member -NotePropertyName LightingGpuSceneRequested -NotePropertyValue ([bool]$LightingGpuScene)
$manifest | Add-Member -NotePropertyName LightingRetainedSpotsRequested -NotePropertyValue ([bool]$LightingRetainedSpots)
$manifest | Add-Member -NotePropertyName SelectionAuditRequested -NotePropertyValue ([bool]$SelectionAudit)
$manifest | Add-Member -NotePropertyName ProbeCoverageAuditRequested -NotePropertyValue ([bool]$ProbeCoverageAudit)
$manifest | Add-Member -NotePropertyName BulkModelsRequested -NotePropertyValue ([bool]$BulkModels)
$manifest | Add-Member -NotePropertyName CompareBulkModelsRequested -NotePropertyValue ([bool]$CompareBulkModels)
$manifest | Add-Member -NotePropertyName ModelBatchingRequested -NotePropertyValue ([bool]$ModelBatching)
$manifest | Add-Member -NotePropertyName CompareModelBatchingRequested -NotePropertyValue ([bool]$CompareModelBatching)
$manifest | Add-Member -NotePropertyName NewActorViewsRequested -NotePropertyValue ([bool]$NewActorViews)
$manifest | Add-Member -NotePropertyName CompareNewActorViewsRequested -NotePropertyValue ([bool]$CompareNewActorViews)
$manifest | Add-Member -NotePropertyName WorldRecipesRequested -NotePropertyValue ([bool]$WorldRecipes)
$manifest | Add-Member -NotePropertyName CompareWorldRecipesRequested -NotePropertyValue ([bool]$CompareWorldRecipes)
$manifest | Add-Member -NotePropertyName ViewEligibilityRequested -NotePropertyValue ([bool]$ViewEligibility)
$manifest | Add-Member -NotePropertyName CompareViewEligibilityRequested -NotePropertyValue ([bool]$CompareViewEligibility)
$manifest | Add-Member -NotePropertyName StockFarCascadesRequested -NotePropertyValue ([bool]$StockFarCascades)
$manifest | Add-Member -NotePropertyName ActorSizeCullingRequested -NotePropertyValue ([bool]$ActorSizeCulling)
$manifest | Add-Member -NotePropertyName ActorLodsRequested -NotePropertyValue ([bool]$ActorLods)
$manifest | Add-Member -NotePropertyName GroupOcclusionRequested -NotePropertyValue ([bool]$GroupOcclusion)
$manifest | Add-Member -NotePropertyName GeometryArenaRequested -NotePropertyValue ([bool]$GeometryArena)
$manifest | Add-Member -NotePropertyName WorldIndirectRequested -NotePropertyValue ([bool]$WorldIndirect)
$manifest | Add-Member -NotePropertyName BenchmarkFrameRequested -NotePropertyValue ([bool]$BenchmarkFrame)
$manifest | Add-Member -NotePropertyName BenchmarkFrameSha256 -NotePropertyValue $(if($BenchmarkFrame){(Get-FileHash -LiteralPath $BenchmarkFrame -Algorithm SHA256).Hash}else{''})
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json') -Encoding utf8
[pscustomobject]@{Instrumented=[bool]$WorkloadAudit -or [bool]$DeepProfile -or [bool]$Nsight -or [bool]$ManagedTrace -or [bool]$CpuDetail -or [bool]$CompareBatchPixels -or [bool]$CompareLightPixels -or [bool]$CompareSubmissionPixels -or [bool]$DisableSteamOverlay;WorkloadAudit=[bool]$WorkloadAudit;ManagedTrace=[bool]$ManagedTrace;NsightImplicitLayersSuppressed=![bool]$Nsight -or [bool]$NsightCpuSampling;CpuDetail=[bool]$CpuDetail;PixelDiagnostic=[bool]$CompareBatchPixels -or [bool]$CompareLightPixels -or [bool]$CompareSubmissionPixels;SteamOverlaySuppressed=[bool]$DisableSteamOverlay;VoxelLookupCache=![bool]$LegacyVoxelLookup;Scope='Explicit process-local launcher controls; does not assert absence of unrelated overlays. Overlay suppression is diagnostic, not the ordinary benchmark workload.'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'instrumentation.json') -Encoding utf8
if($Nsight){[pscustomobject]@{Session=$session;Profiler=$Nsight;DurationSeconds=10;Trace=$trace;GpuWorkload=$(if($NsightCpuSampling){'none'}else{$NsightGpuWorkload});CpuSampling=[bool]$NsightCpuSampling;Instrumented=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'nsight-launch.json') -Encoding utf8}
if($PassThru){$manifest}else{$manifest | ConvertTo-Json}
