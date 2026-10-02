# The fast city loop: code + data into the WARM sandbox, then the city audit
# and the preview shots, with no scene build. About four minutes against the
# forty a full verify costs, for the part of the city that can be checked
# without a scene: the graph, the elevation solve, the tile meshes and what
# they look like.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-cycle.ps1
#   ...            -AuditOnly          the audit, no preview shots (~15 min)
#   ...            -PreviewOnly        the shots, no audit
#   ...            -Full               the roads pass's reports city-wide
#                                      (PSX_AUDIT_FULL=1; the release gates)
#   ...            -NoRatchet          linecheck without --ratchet (the city
#                                      lane: see below)
#
# Code folders are MIRRORED (a deleted script must not linger); Art and
# Resources are copied over the top, and the files this pass retired are
# removed by name so the sandbox cannot load them by accident.
#
# WHICH SANDBOX. $env:PSX_SANDBOX, default C:\Users\mcgee\PSXBuild (lane A,
# main). The city lane (C:\Users\mcgee\PSX Racing-city, branch city-pass)
# sets $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity' and runs THIS FILE FROM ITS
# OWN TREE, so the code mirrored is that lane's. One Unity job per sandbox at
# a time.
#
# THE ROADS PASS'S REPORTS (plan P0, critic C7; 2026-10-02). CityAudit.Run
# calls one partial hook per report (TwinReport, ProfileReport, PaintReport,
# MergeReport, CoverageReport, LotReport, BulbReport - Editor\CityAudit.Hooks.cs).
# Each runs CITY-WIDE ONLY under PSX_AUDIT_FULL=1 (-Full sets it), which is
# for the release gates; otherwise it is BOXED, so a package's audit costs
# minutes, not the half hour the city-wide set would. The switches, all read
# by CityAudit.ScopeFor (Editor\CityAudit.Hooks.cs) and printed at the top of
# each report:
#   PSX_AUDIT_FULL=1               every report city-wide (tiers still apply)
#   PSX_AUDIT_BOX=x0,z0,x1,z1      the box every report uses (game metres,
#                                  x east, z north; CityAudit.LatLon converts)
#   PSX_<NAME>_BOX=x0,z0,x1,z1     one report's own box, over PSX_AUDIT_BOX:
#                                  PSX_COVER_BOX, PSX_TWIN_BOX, PSX_PROFILE_BOX,
#                                  PSX_PAINT_BOX, PSX_MERGE_BOX, PSX_LOT_BOX,
#                                  PSX_BULB_BOX
#   PSX_AUDIT_TIER=1 | 1,2 | all   the tiers every report counts (CityTier:
#                                  T1 motorway/trunk/primary + links, T2
#                                  secondary/tertiary + links, T3 local,
#                                  service, parking)
#   PSX_<NAME>_TIER=...            one report's own tiers (PSX_TWIN_TIER ...)
# With no box set and no PSX_AUDIT_FULL, a report runs on the DEFAULT box,
# CityAudit.OwnerBox: uptown inside the I-277 loop plus W 5th St over I-77
# (the owner's example). The launch audit has its own runner and box:
# tools\city-launch.ps1 -Box x0,z0,x1,z1 (PSX_LAUNCH_BOX).
#
# THE JOB'S BUDGET is 45 minutes (25 until 2026-10-02: the audit alone took
# 15.1 min that day, and the roads pass adds reports). A job past it is
# reported as not finished and LEFT RUNNING (never killed); the next job on
# that sandbox waits for it.
param([switch]$AuditOnly, [switch]$PreviewOnly, [switch]$Full, [switch]$NoRatchet)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if ($Full) { $env:PSX_AUDIT_FULL = "1" }
Write-Host ("sandbox {0}, source {1}" -f $proj, $src) -ForegroundColor Cyan
$scopeVars = @(Get-ChildItem env: | Where-Object { $_.Name -match '^PSX_(AUDIT_FULL|AUDIT_BOX|AUDIT_TIER|[A-Z]+_BOX|[A-Z]+_TIER)$' } | Sort-Object Name)
if ($scopeVars.Count) { Write-Host ("report scope: " + (($scopeVars | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ' ')) -ForegroundColor Cyan }
else { Write-Host "report scope: boxed to the default box (CityAudit.OwnerBox); -Full or PSX_AUDIT_FULL=1 for city-wide" -ForegroundColor Cyan }

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
foreach ($rel in @("Assets\PSXRacing\Art\CLT", "Assets\PSXRacing\Art\CLT.meta",
                   "Assets\PSXRacing\Resources\charlotte_city.json", "Assets\PSXRacing\Resources\charlotte_city.json.meta",
                   "Assets\PSXRacing\Resources\clt_uptown.json", "Assets\PSXRacing\Resources\clt_uptown.json.meta",
                   "Assets\PSXRacing\Resources\clt_tryon.json", "Assets\PSXRacing\Resources\clt_tryon.json.meta",
                   "Assets\PSXRacing\Resources\clt_independence.json", "Assets\PSXRacing\Resources\clt_independence.json.meta")) {
    $p = Join-Path $proj $rel
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

# Exit 1 on "CITY AUDIT: N FAILURES", on an audit that wrote nothing, and on a
# job that did not finish. This script printed the failure count and exited 0
# for its whole life, so nothing downstream could tell a red city from a green
# one without reading the text.
$failed = $false

# THE SMOOTHNESS GATE, offline (plan A4 G-offline; Docs\CHARLOTTE.md
# "Smoothness gate"): tools\city\linecheck.mjs --ratchet against the recorded
# baseline, which also runs the gate's own probes (gateprobes.mjs). A new or
# worse violation, a STALE baseline (the data or the gate's code moved since
# it was recorded: re-record with --write-baseline, BEFORE -> AFTER in the
# commit), a check or a pin gating looser, or a failing probe exits 1 - and
# fails this cycle. Until review 5 nothing ran it, so no regression failed any
# automatic step. Offline, from the source tree: no Unity.
#
# -NoRatchet (the city lane; Docs\CHARLOTTE.md "P0", critic C3): the baseline
# is re-recorded only on main, by the lane-A agent performing a merge point.
# The city lane moves the gate's inputs (the data, citydata.mjs, linesim.mjs)
# in nearly every package, so its ratchet would read STALE every time. It runs
# the gate WITHOUT --ratchet instead - the per-check table is printed, nothing
# is compared or failed - and compares that table with its own before-numbers.
if (-not $PreviewOnly) {
    if ($NoRatchet) {
        $lcOut = Join-Path $proj "linecheck_noratchet"
        Write-Host "--- smoothness gate, offline (linecheck, NO ratchet: compare with your own before-numbers) ---" -ForegroundColor Cyan
        & node "$src\tools\city\linecheck.mjs" --no-census --out $lcOut | Select-String -Pattern "^SMOOTHNESS GATE|^  check |^  [A-E][0-9]+[a-z]? |^PINNED|FAIL" | ForEach-Object { $_.Line }
        if ($LASTEXITCODE -ne 0) { Write-Host "linecheck did not run (exit $LASTEXITCODE)" -ForegroundColor Red; $failed = $true }
        Write-Host "  (full table: $lcOut\linecheck.txt)"
    } else {
        Write-Host "--- smoothness gate, offline (linecheck --ratchet) ---" -ForegroundColor Cyan
        & node "$src\tools\city\linecheck.mjs" --no-census --ratchet | Select-String -Pattern "^RATCHET|FAIL|STALE|gateprobes|^LINECHECK" | ForEach-Object { $_.Line }
        if ($LASTEXITCODE -ne 0) { Write-Host "linecheck --ratchet FAILED (exit $LASTEXITCODE)" -ForegroundColor Red; $failed = $true }
    }
}
$jobs = @()
if (-not $PreviewOnly) { $jobs += @{ Name = "city audit";   Method = "PSXRacing.EditorTools.CityAudit.Run";   Out = "city_audit.txt" } }
if (-not $AuditOnly)   { $jobs += @{ Name = "city preview"; Method = "PSXRacing.EditorTools.CityPreview.Run"; Out = $null } }
foreach ($job in $jobs) {
    Write-Host ("--- {0} ---" -f $job.Name) -ForegroundColor Cyan
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
    }
    $log = "$proj\cityjob.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    $ok = Invoke-UnityJob -Log $log -MaxMinutes 45 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod",$job.Method,
        "-logFile",$log,"-accept-apiupdate")
    if (-not $ok) { Write-Host "job did not finish" -ForegroundColor Red; $failed = $true }
    $cs = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 10
    if ($cs) { $cs | ForEach-Object { $_.Line } }
    $ex = Select-String -Path $log -Pattern "Exception|\[City\]|\[CityPreview\]|\[CityAudit\]" -ErrorAction SilentlyContinue | Select-Object -First 60
    if ($ex) { $ex | ForEach-Object { $_.Line } }
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) {
            Get-Content $outFile
            if (Select-String -Path $outFile -Pattern 'CITY AUDIT: \d+ FAILURES' -CaseSensitive -Quiet) { $failed = $true }
        }
        else { Write-Host ("no {0} written - the method threw; log tail:" -f $job.Out) -ForegroundColor Red
               Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
               $failed = $true }
    }
}
Write-Host "--- preview shots ---"
Get-ChildItem "$proj\Screenshots\City" -ErrorAction SilentlyContinue | ForEach-Object { "{0}  {1}" -f $_.LastWriteTime.ToString("HH:mm"), $_.Name }
if ($failed) {
    Write-Host "CITY CYCLE DONE - FAILED (city audit failures, or a job that did not finish)" -ForegroundColor Red
    exit 1
}
Write-Host "CITY CYCLE DONE"
exit 0
