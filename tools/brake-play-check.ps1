# The brakes in the RUNNING game: a 100 km/h stop (distance, g, how hard
# the tyres mark the road), full brake + full lock at 80 km/h (does it still
# steer), and a sideways slide with the brake held (never reverse). On the
# flat strip the top-speed check builds. Exit 0 = all three hold.
#
#   powershell -ExecutionPolicy Bypass -File tools/brake-play-check.ps1
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
Remove-Item "$proj\PSXRacing_brake_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\brakeplay.log" -MaxMinutes 30 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.BrakePlayCheck.Run",
    "-logFile","$proj\brakeplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\brakeplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_brake_play_check.txt") {
    Get-Content "$proj\PSXRacing_brake_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_brake_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of brakeplay.log:"
Get-Content "$proj\brakeplay.log" -Tail 40
exit 1
