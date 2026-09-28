# The chase camera IN THE RUNNING GAME (Editor\CamFramePlayCheck.cs): a race on
# a drag strip in the FD, the car driven kinematically at a held 0 / 100 /
# 200 km/h in CHASE and CLOSE, and the live camera compared with
# ChaseCamera.SteadyPose - the pose the framing probe measures - plus the
# car's width share, contact line and horizon read through the live lens.
# Proves the follow lag's steady trail is led out (it used to park the lens
# 1.2 m back of the framing above 22 km/h).
#
#   powershell -ExecutionPolicy Bypass -File tools\camframe-play-check.ps1
#
# Code only, on an already-built sandbox (Scripts and Editor copied over).
# WITH a graphics device. Exit code 0 = no FAIL in the report.
param([string]$Venue = "DragQuarter")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_camframe_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_CAMPLAY_VENUE = $Venue
# NO -quit: the check enters play mode and exits itself.
Invoke-UnityJob -Log "$proj\camframeplay.log" -MaxMinutes 15 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CamFramePlayCheck.Run",
    "-logFile","$proj\camframeplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\camframeplay.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_camframe_play_check.txt") {
    Get-Content "$proj\PSXRacing_camframe_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_camframe_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
Write-Host "NO REPORT - see $proj\camframeplay.log"
exit 1
