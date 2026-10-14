param(
    [Parameter(Mandatory=$true)][string]$Bin64,
    [Parameter(Mandatory=$true)][string]$Pulsar,
    [ValidateSet('Legacy','Interim')][string]$Edition = 'Legacy',
    [ValidateRange(30,900)][int]$DurationSeconds = 90,
    [switch]$TestMedia,
    [switch]$TestMediaSkip,
    [switch]$TestDisplayTransactions,
    [switch]$TestResolutionTimeout,
    [switch]$TestResolutionFill,
    [string]$InstalledProfile,
    [string]$ValidationLayerPath
)
$ErrorActionPreference = 'Stop'
if($TestMediaSkip -and !$TestMedia){throw '-TestMediaSkip requires -TestMedia.'}
if($TestMedia -and !$TestMediaSkip -and $DurationSeconds -lt 120){throw 'Natural KSH completion and the delayed Display checks require -DurationSeconds 120 or more.'}
if ($TestDisplayTransactions -and $DurationSeconds -lt 90) { throw 'Real Display transaction smoke requires at least 90 seconds.' }
if ($TestResolutionFill -and ($DurationSeconds -lt 100 -or $TestDisplayTransactions -or $TestResolutionTimeout)) { throw 'Resolution fill requires its own run of at least 100 seconds.' }
if ($TestResolutionTimeout -and (!$TestDisplayTransactions -or $DurationSeconds -lt 150)) { throw 'The actual 60-second resolution timeout requires Display transactions and a duration of at least 150 seconds.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$gameBin = [IO.Path]::GetFullPath($Bin64)
$pulsarSource = [IO.Path]::GetFullPath($Pulsar)
if (Get-Process -Name SpaceEngineers,Legacy,Interim -ErrorAction SilentlyContinue) { throw 'A game/Pulsar process is already running; isolated startup was not launched.' }
if (!(Test-Path -LiteralPath (Join-Path $gameBin 'SpaceEngineers.exe'))) { throw 'The game Bin64 path is invalid.' }
if (!(Test-Path -LiteralPath (Join-Path $pulsarSource ($Edition + '.exe')))) { throw 'The Pulsar path is invalid for this edition.' }
$runtimeTarget = if ($Edition -eq 'Interim') { 'net10.0' } else { 'net48' }
$builtPlugin = Join-Path $repository ('ClientPlugin/bin/Release/' + $runtimeTarget + '/Vulkan.dll')
if (!(Test-Path -LiteralPath $builtPlugin)) { throw 'Build the Release managed plugin before the startup smoke.' }
$harnessBuildOptions = @('-c','Release','-f',$runtimeTarget,'-p:RunPostBuildEvent=Never')
if ($TestResolutionFill) { $harnessBuildOptions += '-p:ResolutionFillOnly=true' }
& dotnet build (Join-Path $PSScriptRoot 'WindowsStartupHarness.csproj') @harnessBuildOptions
if ($LASTEXITCODE -ne 0) { throw 'Test harness build failed.' }
$runRoot = Join-Path $repository ('TestResults/WindowsStartup/' + [Guid]::NewGuid().ToString('N'))
$portable = Join-Path $runRoot 'Pulsar'
$editionDirectory = Join-Path $portable $Edition
$gameData = Join-Path $runRoot 'GameData'
$reports = Join-Path $runRoot 'Reports'
New-Item -ItemType Directory -Force -Path $portable,$editionDirectory,$gameData,$reports,(Join-Path $editionDirectory 'Profiles'),(Join-Path $editionDirectory 'Sources') | Out-Null
$gameData | Set-Content -LiteralPath (Join-Path $gameData '.se-vulkan-startup') -Encoding utf8
$launcherFiles = if ($Edition -eq 'Interim') { @('Interim.exe','Interim.dll','Interim.deps.json','Interim.runtimeconfig.json') } else { @('Legacy.exe','Legacy.exe.config') }
foreach ($launcherFile in $launcherFiles) { Copy-Item -LiteralPath (Join-Path $pulsarSource $launcherFile) -Destination $portable }
foreach ($library in @($Edition,'Compiler','Interface')) {
    $destination = Join-Path $portable ('Libraries/' + $library)
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $pulsarSource ('Libraries/' + $library + '/*')) -Destination $destination -Recurse
}
$vulkanLocal = Join-Path $editionDirectory 'Local/Vulkan'
$harnessLocal = Join-Path $editionDirectory 'Local/WindowsStartupHarness'
New-Item -ItemType Directory -Force -Path $vulkanLocal,$harnessLocal,(Join-Path $gameData 'Storage') | Out-Null
Copy-Item -LiteralPath $builtPlugin -Destination (Join-Path $vulkanLocal 'plugin.dll')
Copy-Item -LiteralPath (Join-Path $repository 'Vulkan.xml') -Destination (Join-Path $vulkanLocal 'plugin.xml')
Copy-Item -LiteralPath (Join-Path $repository 'Assets') -Destination $vulkanLocal -Recurse
if($TestMedia){
    $mediaProbe=Join-Path $env:LOCALAPPDATA 'SE-Vulkan/Media-0.41.0/win-x64/bin/ffprobe.exe'
    if(Test-Path -LiteralPath $mediaProbe){
        $mediaProbePath=$env:PATH
        try{
            $env:PATH=(Split-Path $mediaProbe)+';'+(Join-Path $vulkanLocal 'Assets/Media/win-x64')+';'+$env:PATH
            $mediaRates=@()
            foreach($mediaVideo in Get-ChildItem -LiteralPath (Join-Path (Split-Path $gameBin) 'Content/Videos') -Filter '*.wmv'){
                $mediaMetadata=(& $mediaProbe -v quiet -select_streams v:0 -show_entries stream=avg_frame_rate,r_frame_rate -of json $mediaVideo.FullName) | ConvertFrom-Json
                if($LASTEXITCODE -ne 0){throw 'Installed video timing inventory failed'}
                $mediaRateValue=0.0
                foreach($mediaCandidate in @($mediaMetadata.streams[0].avg_frame_rate)){
                    $mediaFraction=$mediaCandidate.Split('/')
                    if($mediaFraction.Length -eq 2 -and [double]$mediaFraction[1] -gt 0){
                        $mediaRateValue=[double]$mediaFraction[0]/[double]$mediaFraction[1]
                        if($mediaRateValue -gt 0){break}
                    }
                }
                if($mediaRateValue -le 0){
                    # ASF may report avg=0/0 and r_frame_rate=1000/1 (its
                    # timestamp grid). Measure packet cadence rather than
                    # treating that grid as a thousand video frames/sec.
                    $mediaPackets=(& $mediaProbe -v quiet -select_streams v:0 -read_intervals '%+5' -show_packets -show_entries packet=pts_time -of json $mediaVideo.FullName) | ConvertFrom-Json
                    if($LASTEXITCODE -ne 0){throw 'Installed video packet timing failed'}
                    $mediaTimes=@($mediaPackets.packets | ForEach-Object {if($_.pts_time){[double]::Parse($_.pts_time,[Globalization.CultureInfo]::InvariantCulture)}} | Sort-Object -Unique)
                    if($mediaTimes.Count -gt 2 -and $mediaTimes[-1] -gt $mediaTimes[0]){$mediaRateValue=($mediaTimes.Count-1)/($mediaTimes[-1]-$mediaTimes[0])}
                }
                if($mediaRateValue -le 0){throw ('No usable source cadence for '+$mediaVideo.Name)}
                $mediaRate=$mediaRateValue.ToString('F9',[Globalization.CultureInfo]::InvariantCulture)
                $mediaRates+=($mediaVideo.Name+"`t"+$mediaRate)
            }
            $mediaRates | Set-Content -LiteralPath (Join-Path $reports 'media-video-rates.tsv') -Encoding utf8
        }finally{$env:PATH=$mediaProbePath}
    }
    $mediaManifest=Join-Path $vulkanLocal 'Assets/Media/win-x64/manifest.json'
    if(Test-Path -LiteralPath $mediaManifest){Get-FileHash -LiteralPath $mediaManifest -Algorithm SHA256 | Select-Object Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'media-runtime-hash.json')}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('bin/Release/' + $runtimeTarget + '/WindowsStartupHarness.dll')) -Destination (Join-Path $harnessLocal 'plugin.dll')
if ($Edition -eq 'Interim') {
    # Interim requires this installed compatibility core for the original
    # net48 game. Copy only its already compiled private cache and dependencies;
    # its local ID satisfies Pulsar's force-enabled core without remote sources.
    $coreCache = Join-Path $pulsarSource 'Interim/GitHub/CometWorks/dotnet-compat'
    $coreBinary = Join-Path $coreCache 'plugin.dll'
    if (!(Test-Path -LiteralPath $coreBinary)) {
        # Pulsar 2.4.3 caches by friendly name and fingerprint rather than repo path.
        $candidates = @(Get-ChildItem -LiteralPath (Join-Path $pulsarSource 'Interim/GitHub') -Directory -Filter 'DotNetCompat-*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Bin/plugin.dll') })
        if ($candidates.Count -ne 1) { throw 'A unique installed Interim dotnet-compat core cache is required.' }
        $coreCache = $candidates[0].FullName
        $coreBinary = Join-Path $coreCache 'Bin/plugin.dll'
    }
    $coreLocal = Join-Path $editionDirectory 'Local/dotnet-compat'
    New-Item -ItemType Directory -Force -Path $coreLocal | Out-Null
    Copy-Item -Path (Join-Path $coreCache '*') -Destination $coreLocal -Recurse
    Copy-Item -LiteralPath $coreBinary -Destination (Join-Path $coreLocal 'plugin.dll')
    Copy-Item -Path (Join-Path $coreCache 'Bin/*') -Destination (Join-Path $portable 'Libraries/Interim') -Force
@'
<?xml version="1.0" encoding="utf-8"?>
<PluginData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="GitHubPlugin"><Id>dotnet-compat</Id><RepoId>CometWorks/dotnet-compat</RepoId><FriendlyName>DotNet compatibility core</FriendlyName><Runtimes>CoreCLR</Runtimes><Platforms>Windows</Platforms></PluginData>
'@ | Set-Content -LiteralPath (Join-Path $coreLocal 'plugin.xml') -Encoding utf8
}
$profile = Join-Path $editionDirectory 'Profiles/VulkanOnly.xml'
@'
<?xml version="1.0" encoding="utf-8"?>
<Profile><Name>Vulkan startup smoke</Name><GitHub /><DevFolder /><Local><string>AE72EBB8-F4A8-4ADB-A400-19CC46FDAB79</string><string>WindowsStartupHarness.dll</string></Local><Mods /></Profile>
'@ | Set-Content -LiteralPath $profile -Encoding utf8
@'
<?xml version="1.0" encoding="utf-8"?>
<SourcesConfig><ShowWarning>false</ShowWarning><LocalHubSources /><RemoteHubSources /><RemotePluginSources /><LocalPluginSources /><ModSources /></SourcesConfig>
'@ | Set-Content -LiteralPath (Join-Path $editionDirectory 'Sources/sources.xml') -Encoding utf8
# Preserve the already accepted local consent value only in this isolated copy.
Copy-Item -LiteralPath (Join-Path $pulsarSource ($Edition + '/config.xml')) -Destination (Join-Path $editionDirectory 'config.xml')
@'
<?xml version="1.0" encoding="utf-8"?>
<VulkanConfig><RequestedBackend>Vulkan</RequestedBackend><EnableValidation>true</EnableValidation><RecoveryRequired>false</RecoveryRequired><RetryGeneration>0</RetryGeneration><LastFailure /></VulkanConfig>
'@ | Set-Content -LiteralPath (Join-Path $gameData 'Storage/Vulkan.cfg') -Encoding utf8
if (!$ValidationLayerPath) { $ValidationLayerPath = Join-Path $repository 'Native/build/validation/Bin' }
$layerDirectory = [IO.Path]::GetFullPath($ValidationLayerPath)
if (!(Test-Path -LiteralPath (Join-Path $layerDirectory 'VkLayer_khronos_validation.json'))) { throw "The private validation layer is unavailable: $layerDirectory" }
$environmentChanges = @{
    SE_VULKAN_STARTUP_REPORT_DIR=$reports
    SE_VULKAN_STARTUP_USER_DATA=$gameData
    SE_VULKAN_STARTUP_DURATION_SECONDS=$DurationSeconds.ToString()
    SE_VULKAN_STARTUP_MEDIA= $(if ($TestMedia) { '1' } else { '0' })
    SE_VULKAN_STARTUP_MEDIA_SKIP= $(if ($TestMediaSkip) { '1' } else { '0' })
    SE_VULKAN_STARTUP_DISPLAY_TRANSACTIONS= $(if ($TestDisplayTransactions) { '1' } else { '0' })
    SE_VULKAN_STARTUP_RESOLUTION_TIMEOUT= $(if ($TestResolutionTimeout) { '1' } else { '0' })
    SE_VULKAN_STARTUP_RESOLUTION_FILL= $(if ($TestResolutionFill) { '1' } else { '0' })
    SE_VULKAN_STARTUP_COMMIT_RESTART='0'
    SE_VULKAN_ASSET_DIR=(Join-Path $vulkanLocal 'Assets')
    VK_LAYER_PATH=$layerDirectory
    VK_LAYER_ENABLES='VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT'
}
$previous = @{}
$launchExe = Join-Path $portable ($Edition + '.exe')
$launchDirectory = $portable
$installedHarness = $null
if ($InstalledProfile) {
    $installedProfilePath = [IO.Path]::GetFullPath($InstalledProfile)
    if (!(Test-Path -LiteralPath $installedProfilePath)) { throw 'Installed profile does not exist.' }
    [xml]$profileXml = Get-Content -LiteralPath $installedProfilePath
    $local = $profileXml.Profile.SelectSingleNode('Local')
    if (!$local) { $local = $profileXml.CreateElement('Local'); $profileXml.Profile.AppendChild($local) | Out-Null }
    $installedHarnessName = 'ResolutionFillHarness-' + [Guid]::NewGuid().ToString('N')
    $harnessId = $profileXml.CreateElement('string'); $harnessId.InnerText = $installedHarnessName + '.dll'
    $local.AppendChild($harnessId) | Out-Null
    $profileXml.Save($profile)
    $installedLocal = [IO.Path]::GetFullPath((Join-Path $pulsarSource ($Edition + '/Local')))
    $installedHarness = [IO.Path]::GetFullPath((Join-Path $installedLocal $installedHarnessName))
    if (!$installedHarness.StartsWith($installedLocal + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $installedHarness)) { throw 'Invalid temporary installed harness target.' }
    New-Item -ItemType Directory -Path $installedHarness | Out-Null
    Copy-Item -LiteralPath (Join-Path $harnessLocal 'plugin.dll') -Destination $installedHarness
    $launchExe = Join-Path $pulsarSource ($Edition + '.exe')
    $launchDirectory = $pulsarSource
    $environmentChanges.SE_VULKAN_ASSET_DIR = Join-Path $repository 'Assets'
}
foreach ($key in $environmentChanges.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process'); [Environment]::SetEnvironmentVariable($key, $environmentChanges[$key], 'Process') }
try {
    $arguments = '-bin64 "{0}" -profile "{1}" -appdata "{2}" -noUpdate -noSplash -noDiscord -noStats -noPrompt -stableLogs' -f $gameBin,$profile,$gameData
    if ($InstalledProfile) { $arguments += ' -Sources' }
    $launched = Start-Process -FilePath $launchExe -ArgumentList $arguments -WorkingDirectory $launchDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runRoot 'stdout.txt') -RedirectStandardError (Join-Path $runRoot 'stderr.txt')
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
[pscustomobject]@{RunRoot=$runRoot;Edition=$Edition;RuntimeTarget=$runtimeTarget;ProcessId=$launched.Id;Executable=$launchExe;InstalledProfile=$InstalledProfile;Arguments=$arguments;ManagedSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'plugin.dll') -Algorithm SHA256).Hash;NativeSha256=(Get-FileHash -LiteralPath (Join-Path $vulkanLocal 'Assets/Native/win-x64/se_vulkan_native.dll') -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json') -Encoding utf8
Write-Output "ISOLATED_RUN_ROOT=$runRoot"
Write-Output "ISOLATED_PROCESS_ID=$($launched.Id)"
# Allow cold game/Steam startup before the harness's measured test duration.
$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds + 240)
while (!$launched.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2; $launched.Refresh() }
if (!$launched.HasExited) {
    # Terminate only the exact child process created by this isolated smoke.
    $launched.Kill()
    $launched.WaitForExit(10000) | Out-Null
    'Timed out before controlled shutdown.' | Set-Content -LiteralPath (Join-Path $runRoot 'timeout.txt')
}
$exitCode = $launched.ExitCode
if ($installedHarness -and $launched.HasExited) { Move-Item -LiteralPath $installedHarness -Destination (Join-Path $runRoot 'RetiredCapturePlugin') }
Write-Output "ISOLATED_EXIT_CODE=$exitCode"
Get-ChildItem -LiteralPath $reports | Select-Object Name,Length
if (Test-Path -LiteralPath (Join-Path $reports 'status-latest.txt')) { Get-Content -LiteralPath (Join-Path $reports 'status-latest.txt') -TotalCount 20 }
if ($TestResolutionFill) {
    if ($exitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $reports 'resolution-fill-passed.txt'))) { throw "Resolution fill did not pass. Evidence: $runRoot" }
    if (!(Select-String -LiteralPath (Join-Path $reports 'status-latest.txt') -Pattern 'validation available: 1; enabled: 1; errors: 0' -Quiet) -or
        (Test-Path -LiteralPath (Join-Path $gameData 'Storage/Vulkan.cfg.startup'))) { throw "Resolution fill requires clean validation and orderly Vulkan shutdown. Evidence: $runRoot" }
    return
}
if ($TestMedia -and !(Test-Path -LiteralPath (Join-Path $reports 'media-passed.txt'))) { throw "Stock media acceptance did not complete. Evidence: $runRoot" }
if ($exitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $reports 'menu-native.png')) -or !(Test-Path -LiteralPath (Join-Path $reports 'display-cancel-passed.txt'))) { throw "Startup smoke did not produce successful native/menu and Display cancellation evidence. Evidence: $runRoot" }
if ((Test-Path -LiteralPath (Join-Path $vulkanLocal 'Assets/Shaders/Vanilla/coverage.xml')) -and
    !(Select-String -LiteralPath (Join-Path $reports 'status-latest.txt') -Pattern '^Vanilla shader conversion: Ready ' -Quiet)) {
    throw "The packaged shader catalog did not reach Ready. Evidence: $runRoot"
}
if ((Test-Path -LiteralPath (Join-Path $vulkanLocal 'Assets/Shaders/Vanilla/coverage.xml')) -and
    !(Select-String -LiteralPath (Join-Path $reports 'status-latest.txt') -Pattern '^Vanilla Standard GBuffer: Ready ' -Quiet)) {
    throw "The stock native GBuffer pipeline did not reach Ready. Evidence: $runRoot"
}
if (!(Select-String -LiteralPath (Join-Path $reports 'status-latest.txt') -Pattern '^Vanilla shader pass integration: (Ready|Active) ' -Quiet)) {
    throw "The stock world material/depth/lighting pipelines did not initialize. Evidence: $runRoot"
}
if ($TestDisplayTransactions -and !(Test-Path -LiteralPath (Join-Path $reports 'display-transactions-passed.txt'))) { throw "Real Display transactions did not complete. Evidence: $runRoot" }
if ($TestResolutionTimeout -and !(Test-Path -LiteralPath (Join-Path $reports 'display-timeout-passed.txt'))) { throw "The actual resolution confirmation timeout did not complete. Evidence: $runRoot" }
