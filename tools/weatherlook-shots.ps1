# THE WEATHER LOOK, before/after (Editor/WeatherLookShots.cs): the Tryon
# sprint's start line (NODA) at noon under the climate's own snow, slush and
# rain, in PHOTO / phone DYNAMIC / desktop DYNAMIC skies.
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\weatherlook-shots.ps1 -Stage after [-Check]
#
# -Check also runs the climate self-test (LifeSimSelfTest.RunWeather) and the
# GLES3 compile check (VolCloudsCheck) in the same Unity job.
# Frames: <sandbox>\Screenshots\wl_<stage>_*.png, copied to Docs\shots\weather2.
param([string]$Stage = "after", [switch]$Check)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Resources", "Assets\PSXRacing\Art")) {
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\wl_$Stage*" -ErrorAction SilentlyContinue
$env:PSX_WL_STAGE = $Stage
$method = if ($Check) { "PSXRacing.EditorTools.WeatherLookShots.CaptureWithChecks" } else { "PSXRacing.EditorTools.WeatherLookShots.Capture" }
$log = "$proj\weatherlook.log"
Invoke-UnityJob -Log $log -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod",$method,
    "-logFile",$log,"-accept-apiupdate") | Out-Null
Select-String -Path $log -Pattern "\[WeatherLook\]|\[VolClouds\]|error CS|Shader error|WEATHER SELFTEST|FAIL" |
    Select-Object -First 40 | ForEach-Object { $_.Line }
$dst = "$src\Docs\shots\weather2"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item "$proj\Screenshots\wl_$Stage*.png" $dst -Force -ErrorAction SilentlyContinue
Copy-Item "$proj\PSXRacing_weatherlook.txt" "$dst\notes_$Stage.txt" -Force -ErrorAction SilentlyContinue
if (Select-String -Path $log -Pattern "\[WeatherLook\] done" -Quiet) { Write-Host "WEATHERLOOK OK -> $dst" }
else { Write-Host "CAPTURE FAILED"; Get-Content $log -Tail 15 }
