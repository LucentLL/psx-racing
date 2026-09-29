# The lane audit on the sandbox's BUILT scenes, and the owner's two Blowing Rock
# frames rendered from the game camera (2026-09-28: "one side of the road looks
# wider than the other"). Changes no asset. Copies ONLY Editor\LaneAudit.cs into
# the sandbox (no mirror, so the built scenes survive). verify.ps1 runs the
# audit itself (LaneAudit.Run); this is the quick look.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXBuild'; powershell -ExecutionPolicy Bypass -File tools\lane-audit.ps1
#   -Method Run | Shots | All      -Venue BlowingRock    -Only "BlowingRock,MtMitchell"
#   -Pose1 "608,2.0,-0.5,-0.5" -Pose2 "528,-1.3,-2.5,-1.25"   (s m, lateral m, yaw deg, rig grade deg:
#        render exactly there instead of fitting the pose to the owner's lines)
#   -Tag after      (the shots' file-name tag for the road as built; default asbuilt)
#
# Writes <sandbox>\PSXRacing_lane_audit.txt and <sandbox>\Screenshots\lanes\
# (lane_census_<id>.csv, lane_shots.txt, lanes_*.png). WITH a graphics device:
# the shots render.
param([string]$Method = "Run", [string]$Venue = "BlowingRock", [string]$Only = "",
      [string]$Pose1 = "", [string]$Pose2 = "", [string]$Tag = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

Copy-Item "$src\Assets\PSXRacing\Editor\LaneAudit.cs" "$proj\Assets\PSXRacing\Editor\LaneAudit.cs" -Force
Copy-Item "$src\Assets\PSXRacing\Editor\LaneAudit.cs.meta" "$proj\Assets\PSXRacing\Editor\LaneAudit.cs.meta" -Force
# The probe it grew out of: the same GUID, so it must not linger beside it.
foreach ($old in @("LaneCensusProbe.cs", "LaneCensusProbe.cs.meta")) {
    if (Test-Path "$proj\Assets\PSXRacing\Editor\$old") { Remove-Item "$proj\Assets\PSXRacing\Editor\$old" -Force }
}
$out = "$proj\Screenshots\lanes"
if ($Method -ne "Run" -and (Test-Path $out)) { Remove-Item "$out\*" -Force -ErrorAction SilentlyContinue }
if (Test-Path "$proj\PSXRacing_lane_audit.txt") { Remove-Item "$proj\PSXRacing_lane_audit.txt" -Force }
$env:PSX_LANES_VENUE = $Venue
$env:PSX_LANES_ONLY = $Only
$env:PSX_LANES_POSE1 = $Pose1
$env:PSX_LANES_POSE2 = $Pose2
$env:PSX_LANES_TAG = $Tag
$log = "$proj\laneaudit.log"
$unityArgs = @("-quit","-batchmode")
if ($Method -eq "Run") { $unityArgs += "-nographics" }
$unityArgs += @("-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LaneAudit.$Method",
    "-logFile",$log,"-accept-apiupdate")
Invoke-UnityJob -Log $log -MaxMinutes 40 -UnityArgs $unityArgs | Out-Null
Select-String -Path $log -Pattern "error CS|Exception|\[Lanes\]" | Select-Object -First 20 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_lane_audit.txt") {
    $lines = Get-Content "$proj\PSXRacing_lane_audit.txt"
    $lines | Where-Object { $_ -cmatch '^\s*(FAIL|held) ' }
    $at = [Array]::IndexOf($lines, "SUMMARY")
    if ($at -ge 0) { $lines[$at..($lines.Count - 1)] }
}
if (Test-Path "$out\lane_shots.txt") { Get-Content "$out\lane_shots.txt" }
if ((Test-Path "$proj\PSXRacing_lane_audit.txt") -and
    (Select-String -Path "$proj\PSXRacing_lane_audit.txt" -Pattern "LANE AUDIT: \d+ PROBLEM" -CaseSensitive -Quiet)) { exit 1 }
exit 0
