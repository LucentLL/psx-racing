# Fill up in CHARLOTTE (city fuel r1), in the running game: at one of the city's own
# filling stations (the plainest nearest uptown), parked at a pump the FUEL button is offered;
# on foot at the pump it says FILL UP; pressed, the tank rises (and the money goes, with a career);
# STOP stops it; with a career the shop's GO INSIDE hangs at its door. PSX_EDITION=CITY: free fuel, no door.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-fuel-play-check.ps1
#
# Code only, on a sandbox whose Charlotte (and its station prefabs) is built. Exit 0 = it works.
param([switch]$NoWatch, [int]$MaxMinutes = 20)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_city_fuel_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\cityfuelcheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityFuelPlayCheck.Run",
    "-logFile","$proj\cityfuelcheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\cityfuelcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_city_fuel_play_check.txt") {
    Get-Content "$proj\PSXRacing_city_fuel_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_city_fuel_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of cityfuelcheck.log:"
Get-Content "$proj\cityfuelcheck.log" -Tail 40
exit 1
