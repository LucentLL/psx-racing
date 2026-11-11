# CAR CLUBS AND THE ENFORCED WEEK, played in the running game, headless:
# the club cars in the Eastside Lot, JOIN at a meet, signing in by pulling into
# the lot (and the wrong car's miss), a clash night, the club race at the line
# in the wrong car, a car-meet pizza order announced, driven and judged, and
# the club race launched from the pre-race page, banked, and the board moved.
#
#   powershell -ExecutionPolicy Bypass -File tools\club-play-check.ps1
#
# Code only, on a sandbox whose scenes are built (LifeHome, Town, the venues).
# Exit 0 = it works. Hidden by default (-batchmode, no window); -Watch shows it.
param([switch]$Watch, [int]$MaxMinutes = 30)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_club_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Watch:$Watch -Project $proj -Log "$proj\clubcheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ClubPlayCheck.Run",
    "-logFile","$proj\clubcheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\clubcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_club_play_check.txt") {
    Get-Content "$proj\PSXRacing_club_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_club_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of clubcheck.log:"
Get-Content "$proj\clubcheck.log" -Tail 40
exit 1
