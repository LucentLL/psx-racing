# The sea and the sky over it (the Tidewater pass): the crest of each Bogue
# Banks bridge at noon, sunset, dusk and night - toward the light, away from it,
# down onto the shallows and (dusk, night) up. On an already-built sandbox
# scene; code, shaders and Resources are copied over it. WITH a graphics device.
#
#   powershell -ExecutionPolicy Bypass -File tools\sea-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\sea-shots.ps1 -Venue LangstonBridge
#
# Writes Screenshots\psx_sea_*.png in the sandbox.
#   powershell -ExecutionPolicy Bypass -File tools\sea-shots.ps1 -Dynamic    (the computed sky: psx_sea_dyn_*)
param([string]$Venue = "LangstonBridge,AtlanticBeachBridge", [switch]$Dynamic)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders", "Assets\PSXRacing\Resources")) {
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$env:PSX_SEA_VENUES = $Venue
$env:PSX_SKY_DYNAMIC = if ($Dynamic) { "1" } else { "0" }
Invoke-UnityJob -Log "$proj\seashots.log" -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SeaShots.Capture",
    "-logFile","$proj\seashots.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\seashots.log" -Pattern "error CS|Exception|Shader error|\[SeaShots\]" |
    Select-Object -First 16 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter ($(if ($Dynamic) { "psx_sea_dyn_*.png" } else { "psx_sea_*.png" })) | ForEach-Object { $_.Name }
exit 0
