# Play Charlotte headless (free roam, then the 277 race) and check where the
# car is: on a street, at street height, staying there; and the race field on
# its grid at the line rather than at the free-roam spawn. A PLAY-MODE check
# because both bugs it was written for are poses that only exist once physics
# has stepped (CarController.TeleportTo, CityMode.SeatOnStreet). Scripts+Editor
# only, like reverse-check.ps1: the built scenes and baked shells survive, so
# this is a ~5 minute answer.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-play-check.ps1
#   ...  -Edition CITY   plays it AS the Charlotte test page (Scripts/Edition.cs):
#                        both drives are launched through the CITY front end's
#                        own request (CityFrontEnd.FillFreeRoam / FillRace).
#
# In free roam it also parks the car, stopped, in a drive-thru's order bay:
# under ALL/MAIN the window must offer an order (proof the car is really in
# the bay); under CITY nothing may - no prompt, no ORDER button, no food
# signpost, no store (DriveThru.Serves: the test page has no career). The
# game camera's frame of that stop goes to
# <sandbox>\Screenshots\city_play_orderbay.png. Then it opens the pause menu
# and checks EXIT TO MENU is on it.
#
# Exit code 0 = the report has no FAIL; 1 = it does, or the run threw.
param([switch]$NoWatch, [int]$MaxMinutes = 20, [string]$Edition = "ALL")
$ErrorActionPreference = "Stop"
$proj  = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src   = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# the city data too: the check plays the graph that ships
# /XO: never copy a source file OLDER than the sandbox's. Resources holds BAKED
# output (the pizza cargo, the city props) that the scene build rewrites in the
# sandbox; a plain /E put the source's Aug 30 cargo prefabs back over the Sep 11
# re-bake, their material GUIDs no longer existed, and the shipped pizzas were pink.
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_city_play_check.txt" -ErrorAction SilentlyContinue
$env:PSX_EDITION = $Edition.ToUpperInvariant()

# NO -quit: this one enters play mode and exits itself when it is done.
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\cityplaycheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
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
