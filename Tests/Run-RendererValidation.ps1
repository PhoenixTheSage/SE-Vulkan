param(
 [string]$Bin64='T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64',
 [string]$NativeBuildDirectory,
 [ValidateSet('GpuOpaqueReference','NativePassGpuTests','SceneSubmissionGpuTests','DepthGeometryGpuTests','ShadowOcclusionGpuTests','ProbeGpuTests','StaticDecalGpuTests')]
 [string[]]$Projects=@('GpuOpaqueReference','NativePassGpuTests','SceneSubmissionGpuTests','DepthGeometryGpuTests','ShadowOcclusionGpuTests','ProbeGpuTests')
)
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
if(!$NativeBuildDirectory){$NativeBuildDirectory=Join-Path $taskRepository 'Native/build'}
$taskLayer=Join-Path $taskRepository 'Native/build/validation/Bin'
foreach($file in @('VkLayer_khronos_validation.json','VkLayer_khronos_validation.dll')){
 if(!(Test-Path -LiteralPath (Join-Path $taskLayer $file))){throw 'The repository private validation layer is missing'}
}
if(Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue){throw 'Do not run GPU validation during a private game capture'}
$taskEnvironment=@{VK_LAYER_PATH=$taskLayer;VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT';VK_VALIDATION_VALIDATE_SYNC='1'}
$taskPrevious=@{}
foreach($key in $taskEnvironment.Keys){$taskPrevious[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$taskEnvironment[$key],'Process')}
try {
 foreach($project in $Projects){
  $taskLog=Join-Path $taskRepository ('TestResults/'+$project+'/core-sync-validation.log')
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskLog) | Out-Null
  Write-Output ('Validating '+$project+' with private Khronos layer and synchronization validation')
  if($project -eq 'GpuOpaqueReference'){
   $taskNative=Join-Path $NativeBuildDirectory 'Release/sevk_gpu_opaque_reference.exe'
   if(!(Test-Path -LiteralPath $taskNative)){throw 'Build native Release before GPU opaque reference validation'}
   & $taskNative (Join-Path $NativeBuildDirectory 'shaders') > $taskLog 2>&1
  }else{
   & dotnet run --project (Join-Path $PSScriptRoot $project) -c Release -- $Bin64 > $taskLog 2>&1
  }
  $taskExit=$LASTEXITCODE
  Get-Content -LiteralPath $taskLog -Tail 7 | Out-Host
  if($taskExit -ne 0){throw ($project+' failed; see '+$taskLog)}
 }
}finally{foreach($key in $taskEnvironment.Keys){[Environment]::SetEnvironmentVariable($key,$taskPrevious[$key],'Process')}}
