# Renders the plume outside KSP, in KSP's engine (Unity 2019.4.18f1), through the mod's own shader and
# look (AfterburnerHarness.Render): one panel per variant, on a night and a day background, to
# Bundles/plume/plume-night.png and plume-day.png. Log lines in plume.log.
#   .\plume.ps1 -Variants 'now:;dim:exposure=40;short:maxLengthD=10'
# Keys: any public field of AfterburnerParams or AfterburnerProfile, plus nozzle, throttle, speed, altitude, burner (0 to 1).
param(
    [string]$Variants = 'now:',
    [int]$Width = 800,
    [int]$Height = 260,
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe'
)
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$port = Join-Path $PSScriptRoot 'Assets\Editor\Port'
if (Test-Path $port) { Remove-Item -Recurse -Force $port }
New-Item -ItemType Directory -Force $port | Out-Null
# The KSP-free part of the mod: the model, the nozzle fit, the draw call and the atmosphere.
foreach ($f in 'PlumeModel.cs', 'NozzleLip.cs', 'PlumeDraw.cs', 'Atmosphere.cs') { Copy-Item (Join-Path $repo "Source\$f") $port }
$log = Join-Path $PSScriptRoot 'plume.log'
$p = Start-Process -FilePath $Unity -PassThru -ArgumentList @(
    '-batchmode', '-quit',
    '-projectPath', "`"$PSScriptRoot`"",
    '-executeMethod', 'AfterburnerHarness.Render',
    '-variants', "`"$Variants`"", '-width', $Width, '-height', $Height,
    '-logFile', "`"$log`"")
$p.WaitForExit()
Select-String -Path $log -Pattern '\[PlumeHarness\]|error CS|Shader error|Exception' | ForEach-Object { $_.Line }
"Unity exit code: $($p.ExitCode)"
exit $p.ExitCode
