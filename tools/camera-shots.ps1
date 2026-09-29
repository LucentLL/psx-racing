# The seven driving views of the player car on one venue, and nothing else
# (PSXScreenshotTool.CaptureCamerasOnly): psx_cam_{0..6}_<view>.png. The chase
# pair go through ChaseCamera.SteadyPose, so they are the game's framing at rest
# and 16:9 - for speed and phone framing, and the numbers, run
# tools\camframe-probe.ps1. Scripts and Editor are copied into an already-built
# sandbox first; no scene build. Graphics ON.
#
#   powershell -ExecutionPolicy Bypass -File tools\camera-shots.ps1
#   ... -Venue GillespieGap     another built venue (default: the first circuit)
param([string]$Venue = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\psx_cam_*.png" -ErrorAction SilentlyContinue

$env:PSX_SHOT_VENUE = $Venue
Invoke-UnityJob -Log "$proj\camerashots.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CaptureCamerasOnly",
    "-logFile","$proj\camerashots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\camerashots.log" -Pattern "PSXShot|error CS|Exception" |
    Select-Object -First 8 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "psx_cam_*.png" | ForEach-Object { $_.Name }
