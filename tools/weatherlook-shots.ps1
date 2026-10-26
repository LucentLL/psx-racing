# THE WEATHER LOOK, before/after (Editor/WeatherLookShots.cs): the Blue Ridge
# stage after snow, under light snow, under the bench's heavy snow (slush and
# fresh covers) and drying under a clear sky; then the Tryon sprint's start
# line (NODA) under the bench's snow, fresh snow and packed snow.
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\weatherlook-shots.ps1 -Stage after [-Check] [-Set mountain|city|all]
#   (a "before" from another tree: -Stage before -Src <dir holding Assets\PSXRacing>)
#
# -Check also runs the climate self-test (LifeSimSelfTest.RunWeather) and the
# GLES3 compile check (VolCloudsCheck) in the same Unity job.
# Frames: <sandbox>\Screenshots\wl_<stage>_*.png, copied to Docs\shots\weather3.
param([string]$Stage = "after", [switch]$Check, [string]$Src = "", [string]$Set = "all")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$repo = Split-Path -Parent $PSScriptRoot
$src  = if ($Src) { $Src } else { $repo }
. "$PSScriptRoot\unity-wait.ps1"
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Resources", "Assets\PSXRacing\Art")) {
    robocopy "$repo\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\wl_$Stage*" -ErrorAction SilentlyContinue
$env:PSX_WL_STAGE = $Stage
$env:PSX_WL_SET = $Set
$method = if ($Check) { "PSXRacing.EditorTools.WeatherLookShots.CaptureWithChecks" } else { "PSXRacing.EditorTools.WeatherLookShots.Capture" }
$log = "$proj\weatherlook.log"
Invoke-UnityJob -Log $log -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod",$method,
    "-logFile",$log,"-accept-apiupdate") | Out-Null
Select-String -Path $log -Pattern "\[WeatherLook\]|\[VolClouds\]|error CS|Shader error|WEATHER SELF-TEST|FAIL" |
    Select-Object -First 60 | ForEach-Object { $_.Line }
$dst = "$repo\Docs\shots\weather3"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item "$proj\Screenshots\wl_$Stage*.png" $dst -Force -ErrorAction SilentlyContinue
Copy-Item "$proj\PSXRacing_weatherlook.txt" "$dst\notes_$Stage.txt" -Force -ErrorAction SilentlyContinue
if ($Check) { Copy-Item "$proj\PSXRacing_selftest_weather.txt" "$dst\selftest_weather.txt" -Force -ErrorAction SilentlyContinue }
if (Select-String -Path $log -Pattern "\[WeatherLook\] done" -Quiet) { Write-Host "WEATHERLOOK OK -> $dst" }
else { Write-Host "CAPTURE FAILED"; Get-Content $log -Tail 15 }
