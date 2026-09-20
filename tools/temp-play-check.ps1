# Is the temperature gauge actually wired to the engine, in the RUNNING game?
#
#   powershell -ExecutionPolicy Bypass -File tools\temp-play-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\temp-play-check.ps1 -Venue AirfieldSprint
#
# The MODEL is pinned in edit mode by the self-test (where the needle settles,
# what an overheat costs, what destroys an engine). This asks the other half:
# whether the model is on the car, reading the calendar's air, driving the
# needle in the tach, taking power off the wheels, cutting the throttle when it
# lets go, and reaching the garage through the exit stamp. Every one of those is
# a wire, and a wire compiles just as well disconnected.
#
# Code only, on an already-built sandbox (Scripts and Editor over the top, no
# mirror). Exit 0 = the gauge means something.
param([string]$Venue = "DragEighth")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a run that throws never writes its report, and a
# stale one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_temp_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_TEMP_VENUE = $Venue
# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\tempplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TempPlayCheck.Run",
    "-logFile","$proj\tempplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\tempplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_temp_play_check.txt") {
    $report = Get-Content "$proj\PSXRacing_temp_play_check.txt"
    $report
    if ($report -match "FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of tempplay.log:"
Get-Content "$proj\tempplay.log" -Tail 40
exit 1
