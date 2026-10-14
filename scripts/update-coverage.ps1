param([Parameter(Mandatory=$true)][string]$GameCodeRoot)
$ErrorActionPreference='Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$enumPath = Join-Path $GameCodeRoot 'VRage.Render/VRageRender/Messages/MyRenderMessageEnum.cs'
$enumText = Get-Content -LiteralPath $enumPath -Raw
$kinds = [regex]::Matches($enumText,'(?m)^\s*([A-Za-z][A-Za-z0-9_]*)\s*,?\s*$') | ForEach-Object {$_.Groups[1].Value} | Where-Object {$_ -ne 'MaxMessageIndex'}
if ($kinds.Count -ne 147) { throw "Render message inventory changed: $($kinds.Count) kinds. Audit the game version before updating the backend." }
$outbound = @('ScreenshotTaken','ExportToObjComplete','Error','VideoAdaptersResponse','CreatedDeviceSettings','MainThreadCallback','TasksFinished','ParticleEffectRemoved')
$baseline = @('InvalidateClipmapRange','ClipmapsReady','RebuildCullingStructure','CreateEffect','UpdateNewPipelineSettings','UpdateMaterialsSettings','UpdateEnvironmentMap','SetDecalsBlacklist','DebugDrawLine3DBatch')
$partial = @('UpdateShadowSettings','UpdateNewLoddingSettings','SwitchRenderSettings')
$handlers = @{}
foreach ($relative in @('ClientPlugin/Rendering/MyVulkanRender.cs','ClientPlugin/Rendering/Scene/VulkanScene.cs','ClientPlugin/Rendering/Scene/VulkanVoxels.cs','ClientPlugin/Rendering/Scene/VulkanEnvironment.cs','ClientPlugin/Rendering/Scene/VulkanDecals.cs','ClientPlugin/Rendering/Scene/VulkanParticles.cs','ClientPlugin/Rendering/VulkanSprites.cs','ClientPlugin/Rendering/Scene/VulkanPostprocess.cs','ClientPlugin/Rendering/Scene/VulkanHighlights.cs','ClientPlugin/Rendering/Scene/VulkanShadowExclusions.cs','ClientPlugin/Rendering/VulkanShadows.cs')) {
    $source = Get-Content -LiteralPath (Join-Path $projectRoot $relative) -Raw
    foreach ($match in [regex]::Matches($source,'case MyRenderMessageEnum\.(\w+)\s*:|(?:case|is) MyRenderMessage(\w+)\b')) {
        $kind = $match.Groups[1].Value
        if (!$kind) {$kind=$match.Groups[2].Value}
        if ($kinds -contains $kind) {
            if (!$handlers.ContainsKey($kind)) {$handlers[$kind]=@()}
            $handlers[$kind] += $relative
        }
    }
}
$mediaSource='ClientPlugin/Rendering/VulkanMedia.cs'
foreach($kind in @('PlayVideo','UpdateVideo','DrawVideo','CloseVideo','SetVideoVolume')) { $handlers[$kind]=@($mediaSource) }
$handlers['RegisterDecalsMaterials']=@('ClientPlugin/Rendering/Scene/VulkanDecals.cs');$handlers['ClearDecals']=@('ClientPlugin/Rendering/Scene/VulkanDecals.cs')
$notes = @{
    PlayVideo='Bundled cross-platform libmpv; asynchronous opening; CPU frames and independent audio. Runtime acceptance is documented separately.'
    UpdateVideo='Consumes newest bounded-buffer frame into a persistent sRGB Vulkan texture; no pooled message retained.'
    DrawVideo='Stock destination geometry, tint, premultiplied blend, target texture, scissor, viewport and submission order.'
    CloseVideo='Cancels workers, rejects retired-generation results and releases proxy video ID; GPU resources retire on completion.'
    SetVideoVolume='Stock DirectShow decibel attenuation converted to libmpv cubic volume, including mute/fades.'
    CreateScreenDecal='Copied parent/topology/material state; stock GBuffer roles and two glass depth layers; deformed cube and bone attachments handled.'
    UpdateScreenDecal='Copied transform/position updates; no pooled message or array retained.'
    RegisterDecalsMaterials='Copied material dictionary/list values; immutable strings resolve through DDS cache.'
    ClearDecals='Scene-owned decal state cleared; registered materials preserved.'
    PauseTimer='Stock CPU and Vulkan GPU particle frame delta freeze; no simulation-time advance.'
    CreateParticleEffect='Stock CPU effect/curve/emitter manager; native copied 464-byte emitters and authenticated stock compute/indirect rendering.'
    UpdateParticleEffect='Stock state update, stop/autodelete and output removal callback preserved.'
    UpdateRenderObject='Immediate value application with stock world-over-local precedence and pooled presence-flag cleanup; late pose updates remain pending.'
    UpdatePostprocessSettings='Copied complete stock layout; histogram/fixed exposure, bloom pyramid/dirt, Hable tone mapping, chromatic/vignette, grain, saturation/filters. Authenticated modules and pixel evidence.'
    UpdateSSAOSettings='Copied stock SSAO parameters and sample kernels with optional Gaussian blur.'
    UpdateHBAO='Copied global/slice constants and all coarse/bilateral permutations; actual deinterleaved depth/normal/AO processing.'
    UpdateModelHighlight='Copied sections/subparts/instance selection, pulse/sepia and color; stock highlight geometry and all exponential blur radii; overlap composition.'
    UpdateOverlappingModelsForHighlight='Scene-owned overlap mask excludes stock interior/outer highlight blending.'
    UpdateShadowSettings='Copied count/distance/spread/bias/blur/frozen settings; stock CSM PCF and cascade blending. Small-object pruning and usage-based cascade cadence remain pending.'
    InvalidateClipmapRange='Vanilla message switch is a no-op. Stock storage range events invalidate the API-neutral controller directly; Vulkan consumes its replacement meshes.'
    ClipmapsReady='Vanilla message switch is a no-op. Stock controller Loaded callbacks preserve game-side clipmap readiness.'
    UpdateRenderEnvironment='Copied sun/sky/environment settings; complete near/far cubemap capture, GGX mip filtering, blending, timeout and stock BRDF LUT.'
    TakeScreenshot='Native-size full-frame PNG handled; scaled/world-only capture remains pending.'
    SetCameraViewMatrix='Camera-relative main view handled; stereo and late camera update remain pending.'
    DrawSprite='Texture, clipping, center rotation and rotation speed handled; secondary masks and live UI acceptance remain pending.'
    DrawSpriteExt='Texture, clipping and absolute rotation origin handled; secondary masks remain pending.'
    SpriteMainViewportScale='Centered bounded viewport and full IgnoreBounds viewport handled, including explicit scissor transforms; live UI acceptance remains separate.'
    DrawString='Glyph/kerning sprites handled; full wrapping/ellipsis acceptance remains pending.'
    SetLightShadowIgnore='Copied light/entity identities; excluded native world draw indices preserve other shadow casters.'
    ClearLightShadowIgnore='Clears exclusions for the selected light; subsequent frame depth replay resumes.'
    VoxelCreate='API-neutral asteroid/planet clipmap bound to Vulkan actor/processor; stock spherization constants and far-distance projection/culling supplied.'
    CreateRenderEntityAtmosphere='Copied planet geometry; stock optical-depth LUT CS and HQ main-view atmosphere PS. Stock HQ atmosphere capture also supplies all six near/far probe faces.'
    CreateRenderEntityClouds='Copied shell settings and texture list, stock MWM/VS/PS, gameplay-frame rotation, scaling, fades, far-to-near sorting, read-only depth.'
    UpdateAtmosphereSettings='Copied and normalized scattering settings invalidate optical-depth LUT parameters; bounded fade lifetimes.'
    EnableAtmosphere='Stock global toggle suppresses atmosphere and associated cloud draws; unassociated cloud layers remain independent.'
    UpdateFogSettings='Stock linear fog color/density/multiplier/sky/atmosphere constants, including weather overrides.'
    UpdateCloudLayerFogFlag='Pending audit: vanilla stores this flag but the installed cloud draw path does not read it; no invented cloud-only fog effect.'
    UpdatePlanetSettings='Copied atmosphere/cloud intensity controls; ambient probe atmosphere intensity/desaturation are also applied.'
    UpdateNewLoddingSettings='Global atmosphere/cloud fade duration copied; remaining actor LOD settings remain pending.'
    SwitchRenderSettings='Copied grass distance/density, scaling and wind settings; unsupported debug/graphics options remain pending.'
    CreateRenderVoxelMaterials='Copied stock 128-entry material definitions, triplanar DDS arrays with GPU normalization/mip generation, stock generated missing-channel patterns.'
    UpdateRenderVoxelMaterials='Copied definition updates invalidate affected immutable material groups; native resources retire on the timeline.'
    PreloadVoxelMaterials='Bounded material residency hint; visible frame groups remain pinned during collection.'
}
$rows = @(); $index=0; $cs = [System.Text.StringBuilder]::new()
[void]$cs.AppendLine('// Generated by scripts/update-coverage.ps1 from SE 1.210.014 and actual dispatchers.')
[void]$cs.AppendLine('namespace ClientPlugin.Diagnostics;')
[void]$cs.AppendLine('internal static class MessageCatalog { internal static readonly MessageDisposition[] Dispositions = {')
foreach ($kind in $kinds) {
    $disposition = if ($outbound -contains $kind) {'OutboundResponse'} elseif ($baseline -contains $kind) {'BaselineNoOp'} elseif ($partial -contains $kind) {'Pending'} elseif ($handlers.ContainsKey($kind)) {'Handler'} else {'Pending'}
    $gameFiles = @(Get-ChildItem -LiteralPath (Join-Path $GameCodeRoot 'VRage.Render/VRageRender/Messages') -Filter "MyRenderMessage${kind}.cs" | ForEach-Object {'SE-1.210.014/VRage.Render/VRageRender/Messages/'+$_.Name})
    $sources = @($handlers[$kind] | Select-Object -Unique) + $gameFiles
    if (!$sources.Count) {$sources=@('SE-1.210.014/VRage.Render/VRageRender/Messages/MyRenderMessageEnum.cs')}
    $rows += [pscustomobject][ordered]@{Index=$index;Kind=$kind;Disposition=$disposition;Sources=$sources;Verified=$false;Notes=$notes[$kind]}
    [void]$cs.AppendLine("MessageDisposition.$disposition, // $index $kind")
    $index++
}
[void]$cs.AppendLine('}; }')
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'Docs') | Out-Null
[ordered]@{GameVersion='1.210.014';Count=147;CoverageMeaning='Handler presence is separate from runtime observation and visual acceptance.';Messages=$rows} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $projectRoot 'Docs/RenderMessages.json') -Encoding utf8
$cs.ToString() | Set-Content -LiteralPath (Join-Path $projectRoot 'ClientPlugin/Diagnostics/MessageCatalog.cs') -Encoding utf8
$rows | Group-Object Disposition | Select-Object Name,Count
