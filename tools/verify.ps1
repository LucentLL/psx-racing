# One pass over everything that can be checked without a WebGL build:
#
#   mirror -> bake car shells -> build the six circuits -> LifeSim self-test
#   -> terrain audit -> obstacle audit -> reference screenshots
#
#   powershell -ExecutionPolicy Bypass -File tools\verify.ps1
#   powershell -ExecutionPolicy Bypass -File tools\verify.ps1 -NoMirror
#
# -NoMirror is for the second run of an afternoon: the sandbox already holds
# this source's mirror and shell bake, and only a few files have moved since.
# Code folders are still MIRRORED (a renamed or deleted script must not linger
# and compile against its replacement); art and data are copied over the top
# (a stale extra texture is harmless, a stale extra script is not); the shell
# prefabs survive because nothing deletes them, so the bake is skipped. The
# .meta files the sandbox generated for new source files must be in the
# source first (tools\*-sync-back.ps1), or the mirror deletes them and every
# reference to their GUIDs is reborn.
#
# Order is not arbitrary. The shell prefabs are build output and the mirror
# deletes them, so the bake goes first; the scene build consumes those prefabs;
# and both audits and the screenshots read the SAVED SCENES, so they have to
# come after a build that actually finished. Auditing a mirrored sandbox
# without rebuilding reads the stale scenes from source and reports whatever
# the project looked like the last time somebody committed them.
param([switch]$NoMirror)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if ($NoMirror) {
    if (-not (Test-Path "$proj\Assets\PSXRacing\Generated")) {
        Write-Host "-NoMirror needs a sandbox that has been built before; run without it." -ForegroundColor Red
        exit 1
    }
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
        robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
} else {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

# Every report is deleted before the run that writes it. A tool that throws
# never writes its own output, and the previous run's file then reads as a pass
# over a run that died -- which has happened here more than once.
foreach ($f in @("PSXRacing_terrain_audit.txt", "PSXRacing_selftest_log.txt",
                 "PSXRacing_obstacle_audit.txt")) {
    if (Test-Path "$proj\$f") { Remove-Item "$proj\$f" -Force }
}

if ($NoMirror) {
    Write-Host "[1/5] Car shells kept from the sandbox's last bake." -ForegroundColor Cyan
} else {
    Write-Host "[1/5] Baking car shells..." -ForegroundColor Cyan
    Invoke-UnityJob -Log "$proj\bake.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
        "-logFile","$proj\bake.log","-accept-apiupdate") | Out-Null
    Select-String -Path "$proj\bake.log" -Pattern "FAIL |error CS" |
        Select-Object -First 10 | ForEach-Object { $_.Line }
}

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
# THE EXIT CODE HAS TO MEAN SOMETHING, and it did not. This script printed its
# findings and then ended on a Write-Host, so its exit status was whatever
# PowerShell happened to be carrying: a run whose self-test THREW exited 0, and
# a run where everything passed exited 1. Both happened on the same afternoon,
# and either one teaches you to stop reading the exit code — which is the only
# thing an automated caller can read.
$bad = 0
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL|SELF-TEST" |
        ForEach-Object { $_.Line }
    if (Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "SELF-TEST FAILED" -Quiet) { $bad++ }
} else {
    Write-Host "SELF-TEST WROTE NOTHING - it threw, see $proj\selftest.log" -ForegroundColor Red
    Select-String -Path "$proj\selftest.log" -Pattern "error CS|Exception" |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $bad++
}

Write-Host "[4/5] Terrain + obstacle audits..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\terrain.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TerrainAudit.Run",
    "-logFile","$proj\terrain.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_terrain_audit.txt") {
    Get-Content "$proj\PSXRacing_terrain_audit.txt"
    if (Select-String -Path "$proj\PSXRacing_terrain_audit.txt" -Pattern "^\s*FAIL" -Quiet) { $bad++ }
} else {
    Write-Host "TERRAIN AUDIT WROTE NOTHING - see $proj\terrain.log" -ForegroundColor Red
    $bad++
}

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

# A mesh wound inside out renders nothing and collides perfectly, so the only
# place it can ever be caught is the build log.
$inv = Select-String -Path "$proj\scenebuild.log" -Pattern "wound INSIDE OUT" `
                     -ErrorAction SilentlyContinue
if ($inv) { $inv | Select-Object -First 5 | ForEach-Object { $_.Line }; $bad++ }

if ($bad -gt 0) {
    Write-Host "VERIFY FAILED ($bad stage(s))" -ForegroundColor Red
    exit 1
}
Write-Host "VERIFY PASS COMPLETE" -ForegroundColor Green
exit 0
