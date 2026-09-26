# Standing quarter miles in the RUNNING game (ET, trap, 0-60) for the
# owner's 95 Civic SiR-II: stock, full NA, full turbo, turbo + weight +
# tyres. Report only (no pass/fail): the numbers are the answer.
#
#   powershell -ExecutionPolicy Bypass -File tools/drag-play-check.ps1
#
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\Assets\PSXRacing\Resources\rg2_cars.json" "$proj\Assets\PSXRacing\Resources\rg2_cars.json" -Force

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_drag_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\dragplay.log" -MaxMinutes 30 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.DragPlayCheck.Run",
    "-logFile","$proj\dragplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\dragplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_drag_play_check.txt") {
    Get-Content "$proj\PSXRacing_drag_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_drag_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of dragplay.log:"
Get-Content "$proj\dragplay.log" -Tail 40
exit 1
