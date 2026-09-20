# Who is driving after the flag, in the RUNNING game.
#
#   powershell -ExecutionPolicy Bypass -File tools\finish-play-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\finish-play-check.ps1 -Venue AirfieldSprint
#
# Code only, on an already-built sandbox (Scripts and Editor over the top, no
# mirror). Exit 0 = the player still has the car past the finish line.
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
Remove-Item "$proj\PSXRacing_finish_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_FINISH_VENUE = $Venue
# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\finishplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.FinishPlayCheck.Run",
    "-logFile","$proj\finishplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\finishplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_finish_play_check.txt") {
    $report = Get-Content "$proj\PSXRacing_finish_play_check.txt"
    $report
    if ($report -match "FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of finishplay.log:"
Get-Content "$proj\finishplay.log" -Tail 40
exit 1
