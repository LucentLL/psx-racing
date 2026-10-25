# The baked sky light, photographed in a real race (Editor\BakedLightShots.cs): the
# venue's scene opened by path, summer, clear, no traffic, the player parked on a spot.
#
#   powershell -ExecutionPolicy Bypass -File tools\bakedlight-shots.ps1 -Venue BlueRidge -Spot bridge -Views chase,sideL,sideR,highL,highR -Prefix before -Name viaduct
#   ... -Venue GillespieGap -Spot 0.3 -Pair      (after_ = the bake, before_ = PSX_SKYVIS off)
#
# Scripts, Editor and Shaders are copied into the sandbox first (no mirror, no scene
# build: the scene must already be built there). Runs WATCHED (the shots read the
# game camera's render target, which needs a device).
param([string]$Venue = "BlueRidge", [string]$Spot = "0.3", [string]$Views = "chase",
      [string]$Prefix = "shot", [string]$Name = "", [switch]$Pair, [string]$Hour = "noon",
      [string]$Car = "dodge_viper_gts__99", [string]$Out = "", [switch]$NoWatch, [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Out) { $Out = Join-Path $proj "Screenshots\bakedlight" }
if (-not $Name) { $Name = $Venue }

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
}
Remove-Item "$proj\PSXRacing_bakedlight_shots.txt" -ErrorAction SilentlyContinue
$env:PSX_BL_VENUE = $Venue
$env:PSX_BL_SPOT = $Spot
$env:PSX_BL_VIEWS = $Views
$env:PSX_BL_PREFIX = $Prefix
$env:PSX_BL_NAME = $Name
$env:PSX_BL_PAIR = $(if ($Pair) { "1" } else { "0" })
$env:PSX_BL_HOUR = $Hour
$env:PSX_BL_CAR = $Car
$env:PSX_BL_OUT = $Out
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\bakedlightshots.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.BakedLightShots.Run",
    "-logFile","$proj\bakedlightshots.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\bakedlightshots.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_bakedlight_shots.txt") { Get-Content "$proj\PSXRacing_bakedlight_shots.txt"; exit 0 }
"NO REPORT. Tail of bakedlightshots.log:"
Get-Content "$proj\bakedlightshots.log" -Tail 30
exit 1
