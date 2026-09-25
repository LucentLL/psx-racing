# Moving traffic in the RUNNING game: lanes (right of its own direction),
# heading, ride height, speed, and a rear-end that must wreck the traffic car
# and damage the player. With graphics, so it can save chase-camera frames
# (Screenshots/traffic_*.png).
#
#   powershell -ExecutionPolicy Bypass -File tools/traffic-play-check.ps1 [-Venue RidgePass]
#
param([string]$Venue = "RidgePass")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a run that throws never writes its report, and a
# stale one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_traffic_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_TRAFFIC_VENUE = $Venue
# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\trafficplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TrafficPlayCheck.Run",
    "-logFile","$proj\trafficplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\trafficplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_traffic_play_check.txt") {
    $report = Get-Content "$proj\PSXRacing_traffic_play_check.txt"
    $report
    if ($report -match "FAIL") { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of trafficplay.log:"
Get-Content "$proj\trafficplay.log" -Tail 40
exit 1
