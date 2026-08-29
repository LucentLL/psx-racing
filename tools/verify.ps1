# One pass over everything that can be checked without a WebGL build:
#
#   mirror -> bake car shells -> build the six circuits -> LifeSim self-test
#   -> terrain audit -> obstacle audit -> reference screenshots
#
#   powershell -ExecutionPolicy Bypass -File tools\verify.ps1
#
# Order is not arbitrary. The shell prefabs are build output and the mirror
# deletes them, so the bake goes first; the scene build consumes those prefabs;
# and both audits and the screenshots read the SAVED SCENES, so they have to
# come after a build that actually finished. Auditing a mirrored sandbox
# without rebuilding reads the stale scenes from source and reports whatever
# the project looked like the last time somebody committed them.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Every report is deleted before the run that writes it. A tool that throws
# never writes its own output, and the previous run's file then reads as a pass
# over a run that died -- which has happened here more than once.
foreach ($f in @("PSXRacing_terrain_audit.txt", "PSXRacing_selftest_log.txt",
                 "PSXRacing_obstacle_audit.txt")) {
    if (Test-Path "$proj\$f") { Remove-Item "$proj\$f" -Force }
}

Write-Host "[1/5] Baking car shells..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\bake.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
    "-logFile","$proj\bake.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\bake.log" -Pattern "FAIL |error CS" |
    Select-Object -First 10 | ForEach-Object { $_.Line }

Write-Host "[2/5] Building circuits..." -ForegroundColor Cyan
if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD FAILED - nothing downstream would be measuring this code." -ForegroundColor Red
    Get-Content "$proj\scenebuild.log" -Tail 8
    exit 1
}
Get-Content "$proj\PSXRacing_build_log.txt" -Tail 1

Write-Host "[3/5] Self-test..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\selftest.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL|SELF-TEST" |
        ForEach-Object { $_.Line }
} else {
    Write-Host "SELF-TEST WROTE NOTHING - it threw, see $proj\selftest.log" -ForegroundColor Red
    Select-String -Path "$proj\selftest.log" -Pattern "error CS|Exception" |
        Select-Object -First 6 | ForEach-Object { $_.Line }
}

Write-Host "[4/5] Terrain + obstacle audits..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\terrain.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TerrainAudit.Run",
    "-logFile","$proj\terrain.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_terrain_audit.txt") { Get-Content "$proj\PSXRacing_terrain_audit.txt" }
else { Write-Host "TERRAIN AUDIT WROTE NOTHING - see $proj\terrain.log" -ForegroundColor Red }

Invoke-UnityJob -Log "$proj\obstacle.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TrackObstacleAudit.Run",
    "-logFile","$proj\obstacle.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_obstacle_audit.txt") {
    Select-String -Path "$proj\PSXRacing_obstacle_audit.txt" -Pattern "CLEAR|worst|intrusion|RE-ENTRY|ON TRACK|RUN-OFF|FACING|BACKWARDS" |
        Select-Object -First 40 | ForEach-Object { $_.Line }
}

# Graphics on: these render through the pipeline, and a headless editor has no
# device to render with.
Write-Host "[5/5] Screenshots..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\shots.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.Capture",
    "-logFile","$proj\shots.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\shots.log" -Pattern "PSXShot|error CS|Exception" |
    Select-Object -First 6 | ForEach-Object { $_.Line }

Write-Host "VERIFY PASS COMPLETE" -ForegroundColor Green
