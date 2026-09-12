# Play Charlotte headless (free roam, then the 277 race) and check where the
# car is: on a street, at street height, staying there; and the race field on
# its grid at the line rather than at the free-roam spawn. A PLAY-MODE check
# because both bugs it was written for are poses that only exist once physics
# has stepped (CarController.TeleportTo, CityMode.SeatOnStreet). Scripts+Editor
# only, like reverse-check.ps1: the built scenes and baked shells survive, so
# this is a ~5 minute answer.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-play-check.ps1
#
# Exit code 0 = the report has no FAIL; 1 = it does, or the run threw.
$ErrorActionPreference = "Stop"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# the city data too: the check plays the graph that ships
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_city_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\cityplaycheck.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityPlayCheck.Run",
    "-logFile","$proj\cityplaycheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\cityplaycheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_city_play_check.txt") {
    Get-Content "$proj\PSXRacing_city_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_city_play_check.txt" -Pattern "FAIL" -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of cityplaycheck.log:"
Get-Content "$proj\cityplaycheck.log" -Tail 40
exit 1
