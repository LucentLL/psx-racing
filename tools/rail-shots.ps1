# What a stage's barriers look like: four guard runs, from the seat, close up
# on the shoulder, and from past the barrier. On an already-built sandbox scene
# (stage-lab.ps1 builds held-back ones). WITH a graphics device: it renders.
#
#   powershell -ExecutionPolicy Bypass -File tools\rail-shots.ps1 -Venue SwissNC226A
#
# Writes Screenshots\psx_rail_*.png in the sandbox.
param([string]$Venue = "SwissNC226A")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$env:PSX_RAIL_VENUE = $Venue
Invoke-UnityJob -Log "$proj\railshots.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.RailShots.Capture",
    "-logFile","$proj\railshots.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\railshots.log" -Pattern "error CS|Exception|\[RailShots\]" |
    Select-Object -First 12 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "psx_rail_*.png" | ForEach-Object { $_.Name }
exit 0
