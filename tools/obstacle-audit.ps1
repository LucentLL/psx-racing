# Mirror to the sandbox, rebuild the circuits, and measure every collider
# against the racing corridor AND the gravel either side of it: is there
# anything solid standing where a car can reach it and the player cannot see it.
#
#   powershell -ExecutionPolicy Bypass -File tools\obstacle-audit.ps1
#
# The rebuild is NOT optional, for the reason spelled out in terrain-audit.ps1:
# the mirror below is /MIR, the source scenes are always stale because a sandbox
# build never writes back, and auditing straight after a mirror reads a set of
# circuits from whenever the source scenes were last committed.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# A tool that throws never writes its report, and the previous run's file then
# reads as a pass over a run that died.
if (Test-Path "$proj\PSXRacing_obstacle_audit.txt") {
    Remove-Item "$proj\PSXRacing_obstacle_audit.txt" -Force
}

if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD DID NOT FINISH - auditing would read the stale source scenes. Stopping."
    Get-Content "$proj\scenebuild.log" -Tail 6
    exit 1
}

Invoke-UnityJob -Log "$proj\obstacle.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TrackObstacleAudit.Run",
    "-logFile","$proj\obstacle.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\obstacle.log" -Pattern "error CS|Exception" |
    Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_obstacle_audit.txt") {
    Get-Content "$proj\PSXRacing_obstacle_audit.txt"
} else {
    Write-Host "NO REPORT WRITTEN - the audit threw, see $proj\obstacle.log"
}
