# Slide a car down every wall of every venue, both ways, and report every
# place the wall grabs it (WallScrapeAudit).
#
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape.ps1
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape.ps1 -Only DragQuarter,LangstonBridge
#   powershell -ExecutionPolicy Bypass -File tools\wall-scrape.ps1 -Filter
#
# Edit mode, on an ALREADY-BUILT sandbox: Editor and Scripts are copied over
# the top (no mirror), the scenes are read as they stand. After a builder
# change, rebuild the scenes first or this measures the old walls.
#
# Two modes, and a fix wants both run:
#   (default) GhostContactFilter OFF - the wall GEOMETRY alone. Smooth here is
#             the real fix: walls with no seams to catch on.
#   -Filter   the proxy registered with GhostContactFilter the way the
#             player's car is; the report counts the ghosts it dropped.
# Either way every venue also drives the proxy INTO its walls (square at
# 72 km/h, 30 deg at 108 km/h) and slides it into a 30 cm step stood on the
# face, and each must stop it: a CONTROL FAILED line means a car can go
# through a wall, which outranks any smooth run. -Filter adds a 5 cm seam
# flown filter-off then filter-on, the only proof in the report that the
# filter is live; the filter failing to slide past a seam that stopped the
# proxy without it is a CONTROL FAILED too.
# Each mode's report is also kept under its own name, so an A/B pair sits
# side by side: PSXRacing_wall_scrape_geometry.txt / _filter.txt.
#
# Exit 0 = no snag and every control stopped; 1 = a snag, a failed control,
# a suspect harness, or the run threw.
param([string]$Only = "", [switch]$Filter)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the report first: a run that throws never writes one, and a stale
# report certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_wall_scrape.txt" -ErrorAction SilentlyContinue

$env:PSX_SCRAPE_ONLY = $Only
$env:PSX_SCRAPE_FILTER = if ($Filter) { "1" } else { "0" }
$mode = if ($Filter) { "filter" } else { "geometry" }
Invoke-UnityJob -Log "$proj\wallscrape.log" -MaxMinutes 60 -UnityArgs @(
    "-batchmode","-nographics","-quit","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.WallScrapeAudit.Run",
    "-logFile","$proj\wallscrape.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\wallscrape.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_wall_scrape.txt") {
    Copy-Item "$proj\PSXRacing_wall_scrape.txt" "$proj\PSXRacing_wall_scrape_$mode.txt" -Force
    $report = Get-Content "$proj\PSXRacing_wall_scrape.txt" -Encoding UTF8
    $report | Select-Object -First 40
    # CASE-SENSITIVE: a clean report says "no snags" and "no snag in either
    # direction", and a plain -match (case-blind) read those as SNAG and
    # failed every clean run. The alarms are all upper case on purpose.
    if ($report -cmatch "SNAG|HARNESS SUSPECT|MISSING SCENE|NO TRACKPATH|FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of wallscrape.log:"
Get-Content "$proj\wallscrape.log" -Tail 40
exit 1
