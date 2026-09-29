# One pass over everything that can be checked without a WebGL build:
#
#   mirror -> bake car shells -> build the six circuits -> LifeSim self-test
#   -> town probe -> terrain audit -> obstacle audit (with the edge pass)
#   -> lane audit -> city audit -> reference screenshots
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
#
# THE RESULT IS WRITTEN DOWN. PSXRacing_verify_result.txt in the sandbox says
# VERIFY PASS or VERIFY FAILED, when, and every failing line, so
# tools\build-and-publish.ps1 can print what a deploy is shipping past. It is
# deleted first thing, so a run that dies leaves no result rather than the last
# one's.
#
# PER EDITION (see Scripts/Edition.cs): -Edition MAIN|CITY|ALL, ALL by
# default. The scene build and the self-test are always the whole game (the
# build settings are ALL; TestEditions checks both editions as editions).
# What the edition changes is WHAT IS MEASURED: every audit walks that
# edition's venues (PSX_EDITION for the Unity jobs -> EditionTarget), MAIN
# skips the city audit (it ships no city), and CITY skips the town probe (it
# ships no town). The result file records the edition on its second line and
# tools\build-and-publish.ps1 holds a publish to it.
param([switch]$NoMirror, [string]$Edition = "ALL")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$Edition = $Edition.ToUpperInvariant()
if (@("MAIN", "CITY", "ALL") -notcontains $Edition) { Write-Host "-Edition must be MAIN, CITY or ALL" -ForegroundColor Red; exit 1 }
# Every Unity job below inherits it; the scene builder and the self-test force
# ALL for themselves, the audits and the screenshots measure this edition.
$env:PSX_EDITION = $Edition
Write-Host "Verifying edition $Edition" -ForegroundColor Cyan

$resultFile = "$proj\PSXRacing_verify_result.txt"
if (Test-Path $resultFile) { Remove-Item $resultFile -Force }

# Every line that failed a stage, for the result file and the closing summary.
$failLines = New-Object System.Collections.Generic.List[string]
# Every owner-accepted exception the audits passed on (TrackObstacleAudit.
# OwnerAccepted: named spots, each by station, side and height). Not failures,
# but written down on every run, so a pass is never read as "nothing there".
$acceptedLines = New-Object System.Collections.Generic.List[string]

function Write-VerifyResult([int]$Bad) {
    $head = if ($Bad -gt 0) { "VERIFY FAILED ($Bad stage(s))" } else { "VERIFY PASS" }
    $body = @($head, "edition $Edition", ("finished " + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")), "source $src")
    # Before "failures:" - build-and-publish lists everything after that line.
    $body += @($acceptedLines | ForEach-Object { "accepted: " + $_ })
    $body += "failures:"
    $body += @($failLines)
    Set-Content -Path $resultFile -Value $body -Encoding ASCII
}

if ($NoMirror) {
    if (-not (Test-Path "$proj\Assets\PSXRacing\Generated")) {
        Write-Host "-NoMirror needs a sandbox that has been built before; run without it." -ForegroundColor Red
        exit 1
    }
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
        # /XO: never copy a source file OLDER than the sandbox's. Resources holds BAKED
        # output (the pizza cargo, the city props) that the scene build rewrites in the
        # sandbox; a plain /E put the source's Aug 30 cargo prefabs back over the Sep 11
        # re-bake, their material GUIDs no longer existed, and the shipped pizzas were pink.
        robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    # The URP pipeline and renderer assets. Code lives on them too: the speed-blur
    # pass is a renderer FEATURE, and a sandbox without it renders a game with no blur
    # while every assertion about SpeedBlur.cs passes.
    robocopy "$src\Assets\Settings" "$proj\Assets\Settings" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    # The WebGL template ships BESIDE the player (index.html, and LICENSES.txt,
    # which PSXBuildWebGL.PickLicenses picks per edition from LICENSES-MAIN /
    # LICENSES-CITY.txt), and link.xml is what keeps IL2CPP's stripper off
    # OSSpecificSynchronizationContext. Neither is under Scripts/Editor/Shaders,
    # so a -NoMirror pass followed by build-and-publish -SkipScenes built with
    # whatever the sandbox last had: 2026-09-29, the per-edition LICENSES files
    # were in the source and not in the sandbox, and the CITY player would have
    # carried ALL's credits (the SRTM line the CITY door no longer has).
    # Mirrored like code: a stale extra LICENSES-*.txt must not linger.
    robocopy "$src\Assets\WebGLTemplates" "$proj\Assets\WebGLTemplates" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    robocopy "$src\Assets\PSXRacing" "$proj\Assets\PSXRacing" link.xml link.xml.meta /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
} else {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

# Every report is deleted before the run that writes it. A tool that throws
# never writes its own output, and the previous run's file then reads as a pass
# over a run that died -- which has happened here more than once.
foreach ($f in @("PSXRacing_terrain_audit.txt", "PSXRacing_selftest_log.txt",
                 "PSXRacing_townprobe.txt", "PSXRacing_obstacle_audit.txt", "city_audit.txt",
                 "PSXRacing_lane_audit.txt")) {
    if (Test-Path "$proj\$f") { Remove-Item "$proj\$f" -Force }
}

if ($NoMirror) {
    Write-Host "[1/7] Car shells kept from the sandbox's last bake." -ForegroundColor Cyan
} else {
    Write-Host "[1/7] Baking car shells..." -ForegroundColor Cyan
    Invoke-UnityJob -Log "$proj\bake.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
        "-logFile","$proj\bake.log","-accept-apiupdate") | Out-Null
    Select-String -Path "$proj\bake.log" -Pattern "FAIL |error CS" |
        Select-Object -First 10 | ForEach-Object { $_.Line }
}

Write-Host "[2/7] Building circuits..." -ForegroundColor Cyan
if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD FAILED - nothing downstream would be measuring this code." -ForegroundColor Red
    Get-Content "$proj\scenebuild.log" -Tail 8
    $failLines.Add("SCENE BUILD FAILED - no audit ran")
    Write-VerifyResult 1
    exit 1
}
Get-Content "$proj\PSXRacing_build_log.txt" -Tail 1

Write-Host "[3/7] Self-test..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\selftest.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null
# THE EXIT CODE HAS TO MEAN SOMETHING, and it did not. This script printed its
# findings and then ended on a Write-Host, so its exit status was whatever
# PowerShell happened to be carrying: a run whose self-test THREW exited 0, and
# a run where everything passed exited 1. Both happened on the same afternoon,
# and either one teaches you to stop reading the exit code -- which is the only
# thing an automated caller can read.
$bad = 0
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL|SELF-TEST" |
        ForEach-Object { $_.Line }
    if (Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "SELF-TEST FAILED" -Quiet) {
        $bad++
        Select-String -Path "$proj\PSXRacing_selftest_log.txt" -Pattern "FAIL" |
            ForEach-Object { $failLines.Add("[self-test] " + $_.Line.Trim()) }
    }
} else {
    Write-Host "SELF-TEST WROTE NOTHING - it threw, see $proj\selftest.log" -ForegroundColor Red
    Select-String -Path "$proj\selftest.log" -Pattern "error CS|Exception" |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $failLines.Add("[self-test] wrote nothing - it threw")
    $bad++
}

# THE TOWN AND YOUR STREET ARE MEASURED TOO. TownProbe is the only thing that
# walks their road edges for lips, their respawns for tarmac and (since
# 2026-09-13) the barrier warrant beside them, and it ran only from
# town-check.ps1 -- so the round-one roadside work on both maps verified green
# without ever being measured in Unity. Graphics ON: the probe renders its
# shots into a RenderTexture, and a null device reads back black.
#
# What fails the run is what the probe itself calls a failure: a lip over
# RoadsideRules.EdgeDropFailM ("FAIL"), a spawn or respawn off the road layer
# ("NOT ROAD", "NOTHING UNDER"), no car, no scene. Its FALL list (any ground a
# metre down within 2.5 m, at any slope) and its WARRANT list (the shared
# critical-fall walk) are printed, and FALL does not fail it: FALL is a
# screen, not the warrant. A WARRANT summary line fails it too: the one
# critical garden it reported, beside NbDrive0, is graded to 1V:4H by
# NbGroundY's drive side grades (2026-09-14), so any critical station on
# either map is a regression.
Write-Host "[4/7] Town probe..." -ForegroundColor Cyan
if ($Edition -eq "CITY") {
    Write-Host "  skipped: the CITY edition ships no town and no street" -ForegroundColor DarkGray
} else {
Invoke-UnityJob -Log "$proj\townprobe.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TownProbe.Run",
    "-logFile","$proj\townprobe.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_townprobe.txt") {
    $section = ""
    $townFails = 0
    foreach ($line in Get-Content "$proj\PSXRacing_townprobe.txt") {
        if ($line -cmatch '^=== (.+) ===') { $section = $Matches[1] }
        if ($line -cmatch '^(edge lips:|falls beside an edge|barrier warrant|respawns:)') {
            Write-Host ("[town {0}] {1}" -f $section, $line.Trim())
        }
        if ($line -cmatch 'FAIL|NOT ROAD|NOTHING UNDER|NO PLAYER CAR|scene missing|unguarded  WARRANT') {
            $failLines.Add(("[town {0}] {1}" -f $section, $line.Trim()))
            $townFails++
        }
    }
    if ($townFails -gt 0) {
        Write-Host "TOWN PROBE: $townFails failing line(s)" -ForegroundColor Red
        $bad++
    }
} else {
    Write-Host "TOWN PROBE WROTE NOTHING - see $proj\townprobe.log" -ForegroundColor Red
    Select-String -Path "$proj\townprobe.log" -Pattern "error CS|Exception" -ErrorAction SilentlyContinue |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $failLines.Add("[town] wrote nothing - it threw")
    $bad++
}
}

Write-Host "[5/7] Terrain + obstacle + lane audits..." -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\terrain.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TerrainAudit.Run",
    "-logFile","$proj\terrain.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_terrain_audit.txt") {
    Get-Content "$proj\PSXRacing_terrain_audit.txt"
    $venue = ""
    $terrainFails = 0
    foreach ($line in Get-Content "$proj\PSXRacing_terrain_audit.txt") {
        if ($line -cmatch '^=== (\S+) ===') { $venue = $Matches[1] }
        if ($line -cmatch '^\s*FAIL') { $failLines.Add("[terrain $venue] " + $line.Trim()); $terrainFails++ }
        # The audit's own count as well: a MISSING SCENE or a scene with no
        # track, ground or road is a problem it counts without a FAIL line.
        if ($line -cmatch '^TERRAIN AUDIT: \d+ PROBLEM') { $failLines.Add("[terrain] " + $line.Trim()); $terrainFails++ }
    }
    if ($terrainFails -gt 0) { $bad++ }
} else {
    Write-Host "TERRAIN AUDIT WROTE NOTHING - see $proj\terrain.log" -ForegroundColor Red
    $failLines.Add("[terrain] wrote nothing - it threw")
    $bad++
}

# THE OBSTACLE AUDIT FAILS THE RUN NOW. For its whole life this step printed
# the first 40 of its matching lines and never touched $bad, so a stage with
# 21 km of unrecoverable shoulder, an open deck end and a wall hiding a pocket
# verified green -- and the 40-line cut stopped at Mt Mitchell, so three stages
# were never even shown. Every line matching one of these is a failure and is
# printed, all of them, tagged with its venue. The patterns are CASE-SENSITIVE
# on purpose: TrackObstacleAudit writes its ok and warn lines in lower case and
# names a finding in capitals only when it failed. (tools\obstacle-audit.ps1
# carries the same list.)
$obstaclePattern = '^\s*FAIL|EDGE FALL|DECK RAIL|UNWARRANTED|RUN END|GHOST BARRIERS|ON TRACK|SURFACE: \d+ launch|FORECOURT (WALLED|BLOCKED)|BACKWARDS|MISSING SCENE'
Invoke-UnityJob -Log "$proj\obstacle.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TrackObstacleAudit.Run",
    "-logFile","$proj\obstacle.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_obstacle_audit.txt") {
    $venue = ""
    $obstacleFails = 0
    foreach ($line in Get-Content "$proj\PSXRacing_obstacle_audit.txt") {
        # "track obstacle audit - <Venue>": the venue is the last word.
        if ($line -cmatch '^track obstacle audit') { $venue = ($line -split '\s+')[-1] }
        if ($line -cmatch '^\s+edge: ') { Write-Host ("[{0}] {1}" -f $venue, $line.Trim()) }
        if ($line -cmatch $obstaclePattern) {
            $tagged = "[{0}] {1}" -f $venue, $line.Trim()
            Write-Host $tagged
            $failLines.Add("[obstacle] " + $tagged)
            $obstacleFails++
        }
        # The owner-accepted spots, each on its own run line (and the note
        # when the build no longer finds one). Printed and written down every
        # run; a face that is not exactly one of them is a FAIL line above.
        if ($line -cmatch '^\s+owner-accepted ' -and $line -cnotmatch $obstaclePattern) {
            $tagged = "[{0}] {1}" -f $venue, $line.Trim()
            if ($line -cmatch '^\s{4}') {
                Write-Host $tagged -ForegroundColor Yellow
                $acceptedLines.Add("[obstacle] " + $tagged)
            } elseif ($line -cmatch 'not found') {
                Write-Host $tagged -ForegroundColor Yellow
            }
        }
    }
    if ($acceptedLines.Count -gt 0) {
        Write-Host ("OBSTACLE AUDIT: {0} owner-accepted spot run(s), fix pending (TrackObstacleAudit.OwnerAccepted)" -f $acceptedLines.Count) -ForegroundColor Yellow
    }
    if ($obstacleFails -gt 0) {
        Write-Host "OBSTACLE AUDIT: $obstacleFails failing line(s)" -ForegroundColor Red
        $bad++
    } else {
        Write-Host "OBSTACLE AUDIT OK" -ForegroundColor Green
    }
} else {
    Write-Host "OBSTACLE AUDIT WROTE NOTHING - see $proj\obstacle.log" -ForegroundColor Red
    Select-String -Path "$proj\obstacle.log" -Pattern "error CS|Exception" -ErrorAction SilentlyContinue |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $failLines.Add("[obstacle] wrote nothing - it threw")
    $bad++
}

# THE PAINT ON THE TARMAC. 2026-09-28: every real-width stage shipped with its
# double yellow 3.658 m in from the LEFT edge (a 3.60 m lane beside a 2.68 m
# one on the Parkway loops) because nothing measured paint against pavement.
# LaneAudit cuts every ribbon every 2 m against its own triangles and fails a
# venue whose lanes differ by more than 2.5 cm, whose centre line stands off
# the ribbon's centre, whose paint zigzags off its smooth line, or whose
# texture is not centred - and (2026-09-29, "nor should any sharp angles of
# road or road lines") any painted line, tarmac edge or kerb strip edge whose
# chord misses the curve through its own rings by more than 2.5 cm: the V
# corner every 4 m ring made on a bend before the ribbon was laid on a curve.
# Held-back venues are reported, never failed.
Invoke-UnityJob -Log "$proj\laneaudit.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LaneAudit.Run",
    "-logFile","$proj\laneaudit.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_lane_audit.txt") {
    $laneFails = 0
    $inSummary = $false
    foreach ($line in Get-Content "$proj\PSXRacing_lane_audit.txt") {
        if ($line -ceq 'SUMMARY') { $inSummary = $true }
        if ($inSummary) { Write-Host "[lanes] $line" }
        if ($line -cmatch '^\s*FAIL ') { $failLines.Add("[lanes] " + $line.Trim()); $laneFails++ }
    }
    if ($laneFails -gt 0) {
        Write-Host "LANE AUDIT: $laneFails failing line(s)" -ForegroundColor Red
        $bad++
    }
} else {
    Write-Host "LANE AUDIT WROTE NOTHING - see $proj\laneaudit.log" -ForegroundColor Red
    Select-String -Path "$proj\laneaudit.log" -Pattern "error CS|Exception" -ErrorAction SilentlyContinue |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $failLines.Add("[lanes] wrote nothing - it threw")
    $bad++
}

# The city is not in either audit above (its tiles are built at runtime, and
# its scene has no TrackPath), so it was never in verify at all: the city
# audit ran only from the city scripts, which exited 0 on "N FAILURES".
# Graphics on, as the city scripts run it.
Write-Host "[6/7] City audit..." -ForegroundColor Cyan
if ($Edition -eq "MAIN") {
    Write-Host "  skipped: the MAIN edition ships no Charlotte (city-verify.ps1 / -Edition CITY is its gate)" -ForegroundColor DarkGray
} else {
$cityOk = Invoke-UnityJob -Log "$proj\cityaudit.log" -MaxMinutes 25 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityAudit.Run",
    "-logFile","$proj\cityaudit.log","-accept-apiupdate")
if (Test-Path "$proj\city_audit.txt") {
    Get-Content "$proj\city_audit.txt"
    $cityFail = Select-String -Path "$proj\city_audit.txt" -Pattern 'CITY AUDIT: \d+ FAILURES' -CaseSensitive
    if ($cityFail) {
        $cityFail | ForEach-Object { $failLines.Add("[city] " + $_.Line.Trim()) }
        $bad++
    }
} else {
    Write-Host "CITY AUDIT WROTE NOTHING - see $proj\cityaudit.log" -ForegroundColor Red
    if (-not $cityOk) { Write-Host "(the job did not finish)" -ForegroundColor Red }
    Select-String -Path "$proj\cityaudit.log" -Pattern "error CS|Exception" -ErrorAction SilentlyContinue |
        Select-Object -First 6 | ForEach-Object { $_.Line }
    $failLines.Add("[city] wrote nothing - it threw or did not finish")
    $bad++
}
}

# Graphics on: these render through the pipeline, and a headless editor has no
# device to render with.
Write-Host "[7/7] Screenshots..." -ForegroundColor Cyan
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
if ($inv) {
    $inv | Select-Object -First 5 | ForEach-Object { $_.Line; $failLines.Add("[scene build] " + $_.Line.Trim()) }
    $bad++
}

Write-VerifyResult $bad
if ($bad -gt 0) {
    Write-Host ("VERIFY FAILED ({0} stage(s), {1} failing line(s) - listed in {2})" -f $bad, $failLines.Count, $resultFile) -ForegroundColor Red
    exit 1
}
Write-Host "VERIFY PASS COMPLETE" -ForegroundColor Green
exit 0
