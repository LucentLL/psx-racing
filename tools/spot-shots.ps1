# A venue's named places with the player's car standing on them - the chase
# view the game frames at rest and a view from above the inside of the road
# (PSXScreenshotTool.CaptureSpotsOnly). On an already-built sandbox scene; no
# scene build. Graphics ON (it renders).
#
#   powershell -ExecutionPolicy Bypass -File tools\spot-shots.ps1 -Venue ChimneyRock -Spots "0:start,715:hairpin,1049:hairpin,1081:finish"
#
# Writes Screenshots\psx_spot_<venue>_<wp>_<name>_{chase,above}.png in the sandbox.
param([Parameter(Mandatory=$true)][string]$Venue, [string]$Spots = "0:start")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\psx_spot_$Venue*.png" -ErrorAction SilentlyContinue
$env:PSX_SHOT_VENUE = $Venue
$env:PSX_SHOT_SPOTS = $Spots
Invoke-UnityJob -Log "$proj\spotshots.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CaptureSpotsOnly",
    "-logFile","$proj\spotshots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\spotshots.log" -Pattern "PSXShot|error CS|Exception" |
    Select-Object -First 8 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "psx_spot_$Venue*.png" | ForEach-Object { $_.Name }
