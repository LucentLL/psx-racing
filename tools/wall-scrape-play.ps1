# Lean the REAL car on the REAL walls, in the running game (WallScrapePlayCheck).
#
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape-play.ps1
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape-play.ps1 -Venues DragQuarter,UptownLoop
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape-play.ps1 -NoFilter
#
# wall-scrape.ps1 flies a car-sized box along every wall in edit mode. This one
# loads the race scenes in PLAY mode, one editor session for the list, and
# drives the player's own car as it ships (CarController, CollisionResponder,
# the ghost filter) along wall-lined stretches both ways at 137 km/h, nose in
# and pressed against the barrier. A stretch FAILS on a snag (more than 4 m/s
# lost in one step, or 8 m/s within six), on any hard hit counted by a scrape,
# or on the body going through the wall. Each venue also drives the car square
# into a wall at 72 km/h, which must stop it and count a hard hit.
#
# Code only, on an already-built sandbox: Scripts and Editor copied over the
# top (no mirror). Resources too, /XO, because the city route streams its
# world from the data that ships. After a builder change, rebuild the scenes
# first or this drives the old walls.
#
# -NoFilter forces GhostContactFilter off for the run (the A/B); by default it
# runs as shipped.
#
# Exit 0 = every stretch slid and every control stopped; 1 = a FAIL, or the
# run threw.
param([string]$Venues = "", [switch]$NoFilter)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# /XO: never copy a source file OLDER than the sandbox's. Resources holds BAKED
# output (the pizza cargo, the city props) that the scene build rewrites in the
# sandbox; a plain /E put stale prefabs back over a fresh bake once, and the
# shipped pizzas were pink. See city-play-check.ps1.
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

# Delete the report first: a run that throws never writes one, and a stale
# report certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_wall_scrape_play.txt" -ErrorAction SilentlyContinue

$env:PSX_SCRAPE_VENUES = $Venues
$env:PSX_SCRAPE_FILTER = if ($NoFilter) { "0" } else { "" }
# NO -quit: this one enters play mode and exits itself when it is done.
$finished = @(Invoke-UnityJob -Log "$proj\wallscrapeplay.log" -MaxMinutes 60 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.WallScrapePlayCheck.Run",
    "-logFile","$proj\wallscrapeplay.log","-accept-apiupdate"))[-1]
# Invoke-UnityJob gives up at the deadline but leaves the editor running, and
# an editor with no -quit that has hung stays up for good, holding the sandbox
# against the next job. Kill it: only the process running THIS harness,
# matched by its own -executeMethod, never the owner's editor.
if ($finished -ne $true) {
    Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*WallScrapePlayCheck.Run*" } |
        ForEach-Object {
            "killing the hung play-mode editor (PID $($_.ProcessId))"
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

Select-String -Path "$proj\wallscrapeplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_wall_scrape_play.txt") {
    $report = Get-Content "$proj\PSXRacing_wall_scrape_play.txt" -Encoding UTF8
    $report
    # Case-sensitive: the alarm is the upper-case FAIL the report writes.
    if ($report -cmatch "FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of wallscrapeplay.log:"
Get-Content "$proj\wallscrapeplay.log" -Tail 40
exit 1
