# The same view down the same road under every candidate fog setting.
#
#   powershell -ExecutionPolicy Bypass -File tools\fog-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\fog-shots.ps1 -Venue MtMitchell -Hour noon
#
# Code only, on an already-built sandbox: Scripts, Editor and Shaders copied
# over the top, no mirror and no scene build. The shots come back in
# Screenshots\psx_fog_<venue>_<view>_<variant>.png.
#   ...  -BeforeAfter   the owner's 2026-10-03 rule (no fog unless the day is
#                    foggy): Charlotte's I-277 by night and noon, Mt Mitchell (three
#                    stations) and
#                    the circuit at noon, each with the old band (_old) and the
#                    rule (_new), and the circuit on a fog day:
#                    Screenshots\fe_<view>_<old|new>.png (FogShots.CaptureBeforeAfter).
param([string]$Venue = "CityCircuit", [string]$Hour = "noon", [switch]$BeforeAfter)
# -Venue takes a comma-separated list; the whole sweep runs in one Unity launch.
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
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
    "-executeMethod",$(if ($BeforeAfter) { "PSXRacing.EditorTools.FogShots.CaptureBeforeAfter" } else { "PSXRacing.EditorTools.FogShots.Capture" }),
    "-logFile","$proj\fogshots.log","-quit","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\fogshots.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
Select-String -Path "$proj\fogshots.log" -Pattern "\[FogShots\]" | ForEach-Object { $_.Line }
$shots = Get-ChildItem "$proj\Screenshots\$(if ($BeforeAfter) { 'fe_*.png' } else { 'psx_fog_*.png' })" -ErrorAction SilentlyContinue
"$($shots.Count) shots in $proj\Screenshots"
if ($shots.Count -eq 0) { Get-Content "$proj\fogshots.log" -Tail 30; exit 1 }
exit 0
