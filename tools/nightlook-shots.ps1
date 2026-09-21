# The night look, photographed and measured (2026-09-21, the NFS 2015 pass).
#
#   powershell -ExecutionPolicy Bypass -File tools\nightlook-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\nightlook-shots.ps1 -Only city
#   powershell -ExecutionPolicy Bypass -File tools\nightlook-shots.ps1 -Build
#
# Code, shaders, the always-included shader list and the night window masks
# go into the sandbox; then ONE Unity job WITH graphics runs
# NightLookShots.Capture (CityCircuit, Charlotte at three streamed spots,
# BlueRidge; night, dusk, rain, lens) and night_stats.py scores every frame
# against the numbers measured off the reference frames.
#
# -Only takes any of circuit,city,stage (comma list; PSX_NIGHT_ONLY).
# -Build runs the FULL scene build first. The runtime half of the pass (lamps,
# halos, hour, grade, lens, rain) needs no rebuild, but the wet roads (_Wet on
# the baked materials), the night window masks on the facades and the retired
# pool quads only exist in a scene the current builder has built: without
# -Build those frames show whatever the sandbox's last build baked.
#
# -SelfTest runs the LifeSim self-test between the build and the shots and
# prints its "night look:" section and any FAIL lines.
#
# The whole verification, from a cold sandbox:
#   powershell -ExecutionPolicy Bypass -File tools\nightlook-shots.ps1 -Build -SelfTest
#
# Frames: C:\Users\mcgee\PSXBuild\Screenshots\nl_<venue>_<spot>_<hour>[_rain][_lens].png
# Log:    C:\Users\mcgee\PSXBuild\Screenshots\nl_log.txt
#
# Exits 1 (and says NIGHT LOOK VERIFY FAILED) on a failed scene build, a
# Unity job that did not finish, a self-test that FAILED or wrote no log, a
# shots job that wrote no nl_log.txt or no frames, and a scorer that crashed.
# Until 2026-09-21 it printed every one of those and exited 0 - the same trap
# city-verify.ps1 had until 2026-09-13, and the reason a caller that reads only
# the exit code (a workflow, a chained command) called a failed pass green.
param([string]$Only = "", [switch]$Build, [switch]$SelfTest)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

# Set by every check below and read ONCE, at the end: one bad step does not
# hide the report of the next (the shots still run after a failed self-test
# check - the owner wants the pictures either way - and the run still fails).
$failed = $false

# /MIR for the code folders: a file deleted or renamed in the source must not
# live on in the sandbox and compile there. Every file in them carries a
# committed .meta, so the mirror mints no GUIDs.
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# The always-included shader list: PSX/Halo, PSX/Rain and PSX/Lens are made
# at runtime by Shader.Find and exist in a build only because of this file.
Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
# The facade window masks, with their hand-written metas (fixed GUIDs: a
# sandbox-minted one would be lost on the next mirror).
if (Test-Path "$src\Assets\PSXRacing\Art\City\Night") {
    robocopy "$src\Assets\PSXRacing\Art\City\Night" "$proj\Assets\PSXRacing\Art\City\Night" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    # and the folder's own meta, which lives beside it rather than in it
    if (Test-Path "$src\Assets\PSXRacing\Art\City\Night.meta") {
        Copy-Item "$src\Assets\PSXRacing\Art\City\Night.meta" "$proj\Assets\PSXRacing\Art\City\Night.meta" -Force
    }
}

if ($Build) {
    if (-not (Invoke-SceneBuild -Proj $proj)) {
        Write-Host "SCENE BUILD FAILED - see $proj\scenebuild.log"
        # Nothing after this can be trusted: the frames would show the last
        # build's scenes and the self-test would audit its materials.
        Write-Host "NIGHT LOOK VERIFY FAILED (the scene build)" -ForegroundColor Red
        exit 1
    }
}

# -SelfTest: the LifeSim self-test (its "night look:" section pins the
# machinery: shaders and their GUIDs, the lamp table, halos, the lens switch,
# the hour rules, the baked wet/window materials, the city lamps). After the
# build and before the shots, the order the pass is verified in.
#
# $selfTestDone: whether the self-test's Unity is known to be GONE. Invoke-UnityJob returns
# false on a timeout (the editor is still running) and on a launch it never saw
# appear (which may yet appear): either way a second job now would put two
# editors on one sandbox, the trap unity-wait.ps1 exists for.
$selfTestDone = $true
if ($SelfTest) {
    # A stale log from the last run reads exactly like a fresh one.
    Remove-Item "$proj\PSXRacing_selftest_log.txt" -ErrorAction SilentlyContinue
    $ok = Invoke-UnityJob -Log "$proj\nightlook-selftest.log" -MaxMinutes 30 -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
        "-logFile","$proj\nightlook-selftest.log","-accept-apiupdate")
    if (-not $ok) {
        Write-Host "self-test job did not finish" -ForegroundColor Red
        $failed = $true
        $selfTestDone = $false
    }
    Select-String -Path "$proj\nightlook-selftest.log" -Pattern "error CS" -ErrorAction SilentlyContinue |
        Select-Object -First 10 | ForEach-Object { $_.Line }
    $st = "$proj\PSXRacing_selftest_log.txt"
    if (Test-Path $st) {
        $lines = @(Get-Content $st)
        $at = 0
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -eq "night look:") { $at = $i; break } }
        $lines[$at..($lines.Count - 1)]
        $lines | Where-Object { $_ -match "^  FAIL" } | Select-Object -First 20
        # The verdict line is written in capitals and only by the self-test;
        # -CaseSensitive so no check's own prose can trip it.
        if (Select-String -Path $st -Pattern 'SELF-TEST FAILED' -CaseSensitive -Quiet) { $failed = $true }
    } else {
        # The method threw before its last line (or never compiled): the
        # absence of a FAIL is not a pass.
        Write-Host "SELF-TEST WROTE NOTHING - see $proj\nightlook-selftest.log; its tail:" -ForegroundColor Red
        Get-Content "$proj\nightlook-selftest.log" -Tail 25 -ErrorAction SilentlyContinue
        $failed = $true
    }
}

if (-not $selfTestDone) {
    Write-Host "the self-test's Unity may still be running on $proj - NOT starting the shots job" -ForegroundColor Red
    Write-Host "NIGHT LOOK VERIFY FAILED (the self-test job did not finish)" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path "$proj\Screenshots" | Out-Null
Remove-Item "$proj\Screenshots\nl_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\nl_log.txt" -ErrorAction SilentlyContinue

$env:PSX_NIGHT_ONLY = $Only

# NO -nographics: every frame is a RenderTexture read back, and a null device
# reads back black. -quit: the method returns when the last frame is written.
$ok = Invoke-UnityJob -Log "$proj\nightlook.log" -MaxMinutes 30 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.NightLookShots.Capture",
    "-logFile","$proj\nightlook.log","-quit","-accept-apiupdate")
if (-not $ok) { Write-Host "shots job did not finish" -ForegroundColor Red; $failed = $true }

Select-String -Path "$proj\nightlook.log" -Pattern "error CS|Shader error|Exception" -ErrorAction SilentlyContinue |
    Select-Object -First 15 | ForEach-Object { $_.Line }
# Capture writes nl_log.txt in its finally, so a run that got as far as its
# first line always leaves one. None means the method never ran (a compile
# error, a missing -executeMethod) or the editor died under it - and frames
# left from a half run are no verification.
if (Test-Path "$proj\Screenshots\nl_log.txt") {
    Get-Content "$proj\Screenshots\nl_log.txt"
    # A venue that threw or was skipped still leaves the others' frames and a
    # log, so neither of those is a verdict. Capture writes one, last: the OK
    # line must be THERE (the absence of FAILED is not enough).
    if (-not (Select-String -Path "$proj\Screenshots\nl_log.txt" -Pattern '^NIGHT LOOK CAPTURE OK$' -CaseSensitive -Quiet)) {
        Write-Host "the capture did not say OK - a venue threw or was skipped (see the log above)" -ForegroundColor Red
        $failed = $true
    }
}
else {
    Write-Host "no nl_log.txt written - Capture never finished; log tail:" -ForegroundColor Red
    Get-Content "$proj\nightlook.log" -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
# A PSX shader that fails to compile renders magenta and throws nothing. The
# import happens in whichever job runs first after the copy, so both logs.
$shaderLogs = @("$proj\nightlook.log")
if ($SelfTest) { $shaderLogs += "$proj\nightlook-selftest.log" }   # this run's, never a stale one
$shaderLogs = @($shaderLogs | Where-Object { Test-Path $_ })
if ($shaderLogs.Count -gt 0 -and (Select-String -Path $shaderLogs -Pattern "Shader error in 'PSX/" -CaseSensitive -Quiet)) {
    Write-Host "a PSX shader failed to compile (see the Unity logs)" -ForegroundColor Red
    $failed = $true
}

$shots = @(Get-ChildItem "$proj\Screenshots\nl_*.png" -ErrorAction SilentlyContinue)
"$($shots.Count) night-look frames in $proj\Screenshots"
if ($shots.Count -eq 0) {
    Get-Content "$proj\nightlook.log" -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
else {
    # PowerShell does not expand a wildcard for a native command, so the list
    # is handed over file by file. The scorer always returns 0 when it ran to
    # the end (the bands are a report, not a gate); anything else is a crash.
    py "$src\tools\night\night_stats.py" @($shots | ForEach-Object { $_.FullName })
    if ($LASTEXITCODE -ne 0) { Write-Host "night_stats.py failed (exit $LASTEXITCODE)" -ForegroundColor Red; $failed = $true }
}

if ($failed) {
    Write-Host "NIGHT LOOK VERIFY FAILED (a failed self-test, a job that did not finish, or a log that was never written - see above)" -ForegroundColor Red
    exit 1
}
Write-Host "NIGHT LOOK VERIFY OK" -ForegroundColor Green
exit 0
