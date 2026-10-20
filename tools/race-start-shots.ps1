# DRIVE TO THE START, played: Charlotte free roam as the CITY race card's
# DRIVE TO THE START hands it over (night, rain, the 277 loop's gate ahead),
# the gate from the spawn and from the line, a second gate, and START pressed
# at the line loading the race (Editor/RaceStartShots.cs).
#
#   powershell -ExecutionPolicy Bypass -File tools\race-start-shots.ps1
#
# Output: <sandbox>\Screenshots\race_start_*.png (+ _fb = the framebuffer with
# the HUD, _req = the world rendered on request) and race_start_shots.txt.
param([switch]$NoWatch, [int]$MaxMinutes = 15)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\race_start_shots.txt" -ErrorAction SilentlyContinue
$env:PSX_EDITION = "CITY"

Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\racestartshots.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.RaceStartShots.Run",
    "-logFile","$proj\racestartshots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\racestartshots.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\Screenshots\race_start_shots.txt") {
    Get-Content "$proj\Screenshots\race_start_shots.txt"
    if (Select-String -Path "$proj\Screenshots\race_start_shots.txt" -Pattern "FAIL" -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of racestartshots.log:"
Get-Content "$proj\racestartshots.log" -Tail 40
