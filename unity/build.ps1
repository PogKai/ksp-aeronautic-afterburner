# Builds the plume shader bundles into ../Shaders with Unity 2019.4.18f1 (KSP 1.12's version).
param([string]$Unity = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe')
# The editor harness compiles against the KSP-free part of the mod, copied into Port.
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$port = Join-Path $PSScriptRoot 'Assets\Editor\Port'
if (Test-Path $port) { Remove-Item -Recurse -Force $port }
New-Item -ItemType Directory -Force $port | Out-Null
foreach ($f in 'PlumeModel.cs', 'NozzleLip.cs', 'PlumeDraw.cs', 'Atmosphere.cs') { Copy-Item (Join-Path $repo "Source\$f") $port }
$log = Join-Path $PSScriptRoot 'build.log'
$p = Start-Process -FilePath $Unity -WindowStyle Hidden -PassThru -ArgumentList @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', "`"$PSScriptRoot`"",
    '-executeMethod', 'BuildBundles.BuildAll',
    '-logFile', "`"$log`"")
$p.WaitForExit()
Select-String -Path $log -Pattern '\[BuildBundles\]|Shader error|error CS' | Select-Object -Last 20 | ForEach-Object { $_.Line }
"Unity exit code: $($p.ExitCode)"
if (Select-String -LiteralPath $log -Pattern 'Shader error|error CS') { throw 'Unity reported shader or C# compilation errors.' }
if ((Select-String -Path $log -Pattern '\[BuildBundles\] (windows|macos|linux):').Count -ne 3) { throw 'Unity did not build all three bundles.' }
exit $p.ExitCode
