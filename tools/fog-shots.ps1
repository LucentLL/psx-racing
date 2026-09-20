# The same view down the same road under every candidate fog setting.
#
#   powershell -ExecutionPolicy Bypass -File tools\fog-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\fog-shots.ps1 -Venue MtMitchell -Hour noon
#
# Code only, on an already-built sandbox: Scripts, Editor and Shaders copied
# over the top, no mirror and no scene build. The shots come back in
# Screenshots\psx_fog_<venue>_<view>_<variant>.png.
param([string]$Venue = "CityCircuit", [string]$Hour = "noon")
# -Venue takes a comma-separated list; the whole sweep runs in one Unity launch.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

$env:PSX_FOG_VENUE = $Venue
$env:PSX_FOG_HOUR  = $Hour

# NO -nographics: Shot renders into a RenderTexture and a null device reads
# back as a black PNG.
Invoke-UnityJob -Log "$proj\fogshots.log" -MaxMinutes 25 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.FogShots.Capture",
    "-logFile","$proj\fogshots.log","-quit","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\fogshots.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
Select-String -Path "$proj\fogshots.log" -Pattern "\[FogShots\]" | ForEach-Object { $_.Line }
$shots = Get-ChildItem "$proj\Screenshots\psx_fog_*.png" -ErrorAction SilentlyContinue
"$($shots.Count) shots in $proj\Screenshots"
if ($shots.Count -eq 0) { Get-Content "$proj\fogshots.log" -Tail 30; exit 1 }
exit 0
