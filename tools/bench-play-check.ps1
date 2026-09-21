# Does the debug bench's WORLD page and CAR page work in the RUNNING game?
#
#   powershell -ExecutionPolicy Bypass -File tools\bench-play-check.ps1
#
# The self-test drives the bench's rules on a bare CarController with no scene
# under it. What it cannot see is what happens when a collider and four wheel
# radii are rewritten under a car doing 100 km/h - so this enters play mode on
# the built circuit and does what the page does: swaps to the lightest and the
# heaviest car in the catalog AT SPEED with the clock stopped (the bench opens
# from the pause menu), then walks the hour to night and back and the weather
# through rain, snow, clear and the calendar's own.
#
# Code only, on an already-built sandbox. Needs a graphics device (the sun's
# shadow map is asked whether it went off): no -nographics. Exit 0 = it works.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_bench_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\benchplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.BenchPlayCheck.Run",
    "-logFile","$proj\benchplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\benchplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_bench_play_check.txt") {
    Get-Content "$proj\PSXRacing_bench_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_bench_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of benchplay.log:"
Get-Content "$proj\benchplay.log" -Tail 40
exit 1
