# What the forest looks like from the driver's seat: five stations down a stage,
# eye height, looking where the road goes - summer, winter and fall dresses.
#
#   powershell -ExecutionPolicy Bypass -File tools\forest-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\forest-shots.ps1 -Venue BlueRidge
#
# On an already-built sandbox (Scripts/Editor/Shaders copied over the top, no
# mirror). WITH a graphics device: it renders. Writes Screenshots\psx_forest_*.png.
param([string]$Venue = "MtMitchell")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$env:PSX_TREE_VENUE = $Venue
Invoke-UnityJob -Log "$proj\forestshots.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ForestShots.Capture",
    "-logFile","$proj\forestshots.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\forestshots.log" -Pattern "error CS|Exception|\[ForestShots\]" |
    Select-Object -First 8 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "psx_forest_*.png" | ForEach-Object { $_.Name }
exit 0
