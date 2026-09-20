# Drive the REAL car into the REAL trees, in the running game.
#
#   powershell -ExecutionPolicy Bypass -File tools\tree-play-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\tree-play-check.ps1 -Venue BlueRidge
#
# tree-check.ps1's crash sim is edit mode: a box on a slab against a capsule the
# table is stepped into standing. This one loads the race scene in PLAY mode with
# the player's own car and drives it at the trees nearest the road, dead on and
# offset, and reads whether the car was stopped. Code only, on an already-built
# sandbox (Scripts and Editor copied over the top, no mirror).
#
# Exit 0 = every reachable tree stopped the car; 1 = one did not, or the run threw.
param([string]$Venue = "MtMitchell")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a run that throws never writes its report, and a
# stale one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_tree_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_TREE_VENUE = $Venue
# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\treeplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TreePlayCheck.Run",
    "-logFile","$proj\treeplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\treeplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_tree_play_check.txt") {
    $report = Get-Content "$proj\PSXRacing_tree_play_check.txt"
    $report
    if ($report -match "FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of treeplay.log:"
Get-Content "$proj\treeplay.log" -Tail 40
exit 1
