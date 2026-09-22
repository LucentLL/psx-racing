# Do the cars top out where their builds say, flat out in the RUNNING game?
#
#   powershell -ExecutionPolicy Bypass -File tools\topspeed-play-check.ps1
#
# The self-test checks the top-speed rule (stock GT4 figure, plus a
# percentage per power stage) on the analytical force balance. This checks
# the car: it enters play mode on the built circuit, builds a 30 km strip of
# flat tarmac above it, and holds the throttle down on a handful of cars -
# the owner's RUF stock and fully built, the slowest and fastest road cars,
# a blown NA car, the fastest race car - until each stops gaining, and
# compares where the needle stopped with the build's figure.
#
# Code and the catalog only, on an already-built sandbox. Exit 0 = they do.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\Assets\PSXRacing\Resources\rg2_cars.json" "$proj\Assets\PSXRacing\Resources\rg2_cars.json" -Force

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_topspeed_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\topspeedplay.log" -MaxMinutes 30 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TopSpeedPlayCheck.Run",
    "-logFile","$proj\topspeedplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\topspeedplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_topspeed_play_check.txt") {
    Get-Content "$proj\PSXRacing_topspeed_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_topspeed_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of topspeedplay.log:"
Get-Content "$proj\topspeedplay.log" -Tail 40
exit 1
