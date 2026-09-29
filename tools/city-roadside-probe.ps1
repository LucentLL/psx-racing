# What did the roadside audit see at a spot? Runs CityRoadsideProbe in the
# sandbox (Scripts+Editor copy, no scene build): the tiles round each spot are
# stood up as the audit stands them, and the walk out from the lane edge, the
# ground terms and the roads round it are printed.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\city-roadside-probe.ps1 -Spots "328:111.4:1;11145:6.5:1"
#
# A spot is edge:s:side (side -1 left, 1 right), as the audit's notes name it.
#
# -Lanes asks the lane survey's question instead (what solid stands in a lane,
# and whose rail it is): "edge:s:lane" as the audit's LANE lines name them
# (lane 0 left, 1 middle, 2 right), "fan:node:edge:inset:lat" for a FAN note's
# point as the fan mouth probe places it, or "pt:x:z:y" for a world point.
#   powershell -ExecutionPolicy Bypass -File tools\city-roadside-probe.ps1 -Lanes "1891:137:2;fan:6995:5914:1.0:-5.3"
#
# A lane probe also walks the first SURFACE across the lane line (any layer)
# and names the strip that laid any land it meets (verge, seam, half strip,
# fan chord verge or corner fill, by edge, side and span, from
# CityMeshes.groundLog) or "the lattice"; the spot walk names it too.
# "grid:x:z:y:half:step" maps the first surface round a world point, north
# up: '=' road, 'B' barrier, 'X' other solid, ground 'o' within 0.3 m of y,
# 'v' lower (a hole), '^' higher.
#   powershell -ExecutionPolicy Bypass -File tools\city-roadside-probe.ps1 -Lanes "280:320.5:0;grid:-3455.22:5766.12:105.276:4:0.1"
#
# -ArmLog "edge,edge" also prints, under each lane probe, how the rail builder
# read those edges' rails against the junction's other roads and its fan
# (CityMeshes.RailOverArms), piece by piece.
param([string]$Spots = "", [string]$Lanes = "", [string]$ArmLog = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# /XO: Resources holds baked output (see city-cycle.ps1)
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

Remove-Item "$proj\city_roadside_probe.txt" -ErrorAction SilentlyContinue
$env:PSX_RSPROBE = $Spots
$env:PSX_LANEPROBE = $Lanes
$env:PSX_ARMLOG = $ArmLog
Invoke-UnityJob -Log "$proj\rsprobe.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityRoadsideProbe.Run",
    "-logFile","$proj\rsprobe.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\rsprobe.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\city_roadside_probe.txt") { Get-Content "$proj\city_roadside_probe.txt"; exit 0 }
"NO PROBE OUTPUT - tail of rsprobe.log:"
Get-Content "$proj\rsprobe.log" -Tail 30
exit 1
