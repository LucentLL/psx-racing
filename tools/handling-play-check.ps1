# The owner's handling notes of 2026-09-30 in the RUNNING game
# (HandlingPlayCheck): A 100 mph full brake (distance, lock, screech),
# B the 180 then full gas, C the 180 then full brake (no reverse while
# moving), D a 90 mph drift slowed to 30 (grip comes back), E a 70 mph
# keyboard lane change (yaw lobes, settle, lens lag). Exit 0 = all hold.
#
#   powershell -ExecutionPolicy Bypass -File tools/handling-play-check.ps1 -NoWatch
#
param([switch]$NoWatch, [int]$MaxMinutes = 30)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\Assets\PSXRacing\Resources\rg2_cars.json" "$proj\Assets\PSXRacing\Resources\rg2_cars.json" -Force

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_handling_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\handlingplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.HandlingPlayCheck.Run",
    "-logFile","$proj\handlingplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\handlingplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_handling_play_check.txt") {
    Get-Content "$proj\PSXRacing_handling_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_handling_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of handlingplay.log:"
Get-Content "$proj\handlingplay.log" -Tail 40
exit 1
