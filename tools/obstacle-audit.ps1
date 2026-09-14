# Mirror to the sandbox, rebuild the circuits, and measure every collider
# against the racing corridor AND the gravel either side of it: is there
# anything solid standing where a car can reach it and the player cannot see it.
# Then the EDGE of every venue, stages included, against RoadsideRules: can a
# car that ran wide get back on, can it fall off (bridge rails included), and
# is every barrier a warranted one. Exits 1 on any failing line.
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

Select-String -Path "$proj\obstacle.log" -Pattern "error CS|Exception" -ErrorAction SilentlyContinue |
    Select-Object -First 10 | ForEach-Object { $_.Line }
if (-not (Test-Path "$proj\PSXRacing_obstacle_audit.txt")) {
    Write-Host "NO REPORT WRITTEN - the audit threw, see $proj\obstacle.log"
    exit 1
}
Get-Content "$proj\PSXRacing_obstacle_audit.txt"

# The whole report above, then every failing line again, venue-tagged, and an
# exit code that says whether there were any. Same CASE-SENSITIVE list as
# tools\verify.ps1: the audit names a finding in capitals only when it failed.
$obstaclePattern = '^\s*FAIL|EDGE FALL|DECK RAIL|UNWARRANTED|RUN END|GHOST BARRIERS|ON TRACK|SURFACE: \d+ launch|FORECOURT (WALLED|BLOCKED)|BACKWARDS|MISSING SCENE'
$venue = ""
$fails = 0
foreach ($line in Get-Content "$proj\PSXRacing_obstacle_audit.txt") {
    if ($line -cmatch '^track obstacle audit') { $venue = ($line -split '\s+')[-1] }
    if ($line -cmatch $obstaclePattern) {
        if ($fails -eq 0) { Write-Host "--- failing lines ---" -ForegroundColor Red }
        Write-Host ("[{0}] {1}" -f $venue, $line.Trim())
        $fails++
    }
}
if ($fails -gt 0) {
    Write-Host "OBSTACLE AUDIT: $fails failing line(s)" -ForegroundColor Red
    exit 1
}
Write-Host "OBSTACLE AUDIT OK" -ForegroundColor Green
exit 0
