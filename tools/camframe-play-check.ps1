# The chase camera IN THE RUNNING GAME (Editor\CamFramePlayCheck.cs): a race on
# a drag strip in the FD (or -Shell <key>), the game camera pointed at a 16:9
# and then a 19.5:9 framebuffer, and the car driven kinematically along a road
# laid for it, in CHASE and CLOSE:
#   level        0 / 100 / 200 km/h - the live lens against
#                ChaseCamera.SteadyPose (the pose the framing probe measures),
#                and the share, contact line, horizon and roof gap through it;
#   grades       a straight +7% and -7% at 100 / 200 km/h - the level road's
#                picture turned onto the grade: lens over the roof, roof gap
#                under the ROAD's horizon (rig band), road over the roof from
#                as near the nose; plus what a LEVEL rig would show there;
#   curves       a sag into +8% and a crest into -8% (80 m at 100 km/h, 200 m
#                at 200) - the worst frame: the lens never within half its
#                level margin of the roof, the road still showing on the sag.
# Proves the follow lag's steady trail is led out on the level (it used to
# park the lens 1.2 m back) AND up a hill (it used to drop it v x grade / 5).
# Frames: Screenshots\CamFrame\cf_play_<shell>_<screen>_*.png in the sandbox.
#
#   powershell -ExecutionPolicy Bypass -File tools\camframe-play-check.ps1 [-Shell supra_a80]
#
# Code only, on an already-built sandbox (Scripts and Editor copied over).
# WITH a graphics device. Exit code 0 = no FAIL in the report.
param([string]$Venue = "DragQuarter", [string]$Shell = "", [switch]$NoWatch, [int]$MaxMinutes = 20)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_camframe_play_check.txt" -ErrorAction SilentlyContinue

$env:PSX_CAMPLAY_VENUE = $Venue
$env:PSX_CAMPLAY_SHELL = $Shell
# NO -quit: the check enters play mode and exits itself.
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\camframeplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
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
