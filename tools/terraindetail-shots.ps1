# The terrain detail layer, photographed in a real race start (Editor\TerrainDetailShots.cs):
# the owner's Linn Cove frame (red Viper, chase cam, noon, the field gone) and the car
# parked on a spread of waypoints, once per dress (summer, snow).
#
#   powershell -ExecutionPolicy Bypass -File tools\terraindetail-shots.ps1 -Prefix before
#   ... -Prefix after -Dresses summer -Spots 0.3,0.6 -Out C:\some\dir
#   py tools\terraindetail\detail_metric.py <dir>      (the before/after numbers)
#
# Scripts, Editor and Shaders are copied into the sandbox first (no mirror, no
# scene build). Runs WATCHED (a visible editor): the shots read the game camera's
# render target, which needs a device.
param([string]$Prefix = "shot", [string]$Dresses = "summer,snow", [string]$Spots = "",
      [string]$Venue = "BlueRidge", [string]$Car = "dodge_viper_gts__99", [string]$Hour = "noon",
      [string]$Out = "", [switch]$NoWatch, [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Out) { $Out = Join-Path $proj "Screenshots\terraindetail" }

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
    # the detail sheet only: the sandbox's Resources hold BUILT assets that a
    # copy of the tracked ones would make stale
    robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" "TerrainDetail*" /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_terraindetail_shots.txt" -ErrorAction SilentlyContinue
$env:PSX_TD_PREFIX = $Prefix
$env:PSX_TD_DRESSES = $Dresses
$env:PSX_TD_VENUE = $Venue
$env:PSX_TD_CAR = $Car
$env:PSX_TD_HOUR = $Hour
$env:PSX_TD_OUT = $Out
if ($Spots) { $env:PSX_TD_SPOTS = $Spots } else { Remove-Item Env:PSX_TD_SPOTS -ErrorAction SilentlyContinue }
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\terraindetail.log" -MaxMinutes 25 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TerrainDetailShots.Run",
    "-logFile","$proj\terraindetail.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\terraindetail.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_terraindetail_shots.txt") { Get-Content "$proj\PSXRacing_terraindetail_shots.txt"; exit 0 }
"NO REPORT. Tail of terraindetail.log:"
Get-Content "$proj\terraindetail.log" -Tail 30
exit 1
