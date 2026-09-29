# THE SMOOTHNESS GATE in the warm sandbox (plan amendment A4, WP-G; Editor/
# CitySmooth.cs). The owner's rule of 2026-09-28: painted lines and road edges
# never wobble and never kink. Every lateral limit is V = 2.5 cm (SmoothRules.cs).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\city-smooth.ps1 -Mode FULL
#   powershell -ExecutionPolicy Bypass -File tools\city-smooth.ps1 -Mode FAST [-Band 3]
#   powershell -ExecutionPolicy Bypass -File tools\city-smooth.ps1 -Mode SHOTS [-Spots "x,z,edge,line;..."] [-Before <dir>]
#   add -WriteBaseline to a FULL run to record tools\city\baseline\smooth_baseline.json
#   (REFUSED when it would loosen the gate - the data did not move but keys
#   vanished or score lower, or a check state or a pinned way loosened; add
#   -AllowLoosen for a deliberate, signed-off gate change, the list in the commit)
#
# Modes (gate spec 5.3):
#   FAST   the city audit (CityAudit.Run) with its FAST hook: the drive and
#          roadside audits' tiles, the reference spots and one band of 1/12 of
#          the road tiles (-Band k, else the day of the year mod 12). ~4.5 min.
#          The hook is OPT-IN (PSX_SMOOTH_FAST=1, set here) until its first Unity
#          run validates it (R4); then SmoothRules.FastInAudit = true puts it in
#          every city cycle. Its run counts are compared only with the baseline's
#          runs in the tiles it analysed.
#   FULL   every road tile (CitySmooth.RunFull), then SHOTS of the worst. The
#          G-ship run for every city release. ~5-7 min + shots.
#   SHOTS  plan / chase (240-line frame, 3x) / high close-ups of the worst
#          offenders in the sandbox's city_smooth.json, or of -Spots; then the
#          contact sheet (tools\city\refsheet.py, 'smooth' group).
#
# Code folders are mirrored into the sandbox and Art / Resources copied over
# (/XO), as city-cycle.ps1 does. The repo's baseline is copied to the sandbox
# root (smooth_baseline.json), where CitySmooth reads it; -WriteBaseline copies
# the newly recorded one back. Reports land in -Out (default <sandbox>\
# Screenshots\City\smooth_<Label>): city_smooth.txt / .csv / .json and shots.
# Exit 1 when a gated check fails (after SmoothRules.ReportOnly is flipped),
# when the job did not finish, or when it wrote nothing - and when the
# baseline is STALE (its 'mesh' entry was measured on other inputs: graph,
# container sections, rules, road PNGs or DEM): a stale ratchet cannot tell a
# regression from the move, so it FAILS until the explicit re-record, a FULL
# run with -WriteBaseline, which logs BEFORE -> AFTER numbers for the commit
# (and fails unless the sandbox's baseline was rewritten by this run).
#
# tools\city\linecheck.mjs is the same gate offline; the two must agree
# (Docs\CHARLOTTE.md, "Smoothness gate"). Run it first: seconds, not minutes.
param(
    [ValidateSet("FAST", "FULL", "SHOTS")][string]$Mode = "FULL",
    [int]$Band = -1,
    [string]$Spots = "",
    [int]$Shots = 24,
    [switch]$NoShots,
    [switch]$WriteBaseline,
    [switch]$AllowLoosen,
    [string]$Label = "",
    [string]$Out = "",
    [string]$Before = ""
)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Label) { $Label = "{0}-{1}" -f $Mode.ToLower(), (Get-Date -Format "yyyyMMdd-HHmm") }
if (-not $Out) { $Out = Join-Path $proj "Screenshots\City\smooth_$Label" }

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
    # /XO for the same reason as city-cycle.ps1: Resources holds baked output
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
$repoBase = Join-Path $src "tools\city\baseline\smooth_baseline.json"
$boxBase = Join-Path $proj "smooth_baseline.json"
if (Test-Path $repoBase) { Copy-Item $repoBase $boxBase -Force }

$env:PSX_SMOOTH_BAND = if ($Band -ge 0) { "$Band" } else { $null }
$env:PSX_SMOOTH_SPOTS = if ($Spots) { $Spots } else { $null }
$env:PSX_SMOOTH_SHOTS = "$Shots"
$env:PSX_SMOOTH_WRITE_BASELINE = if ($WriteBaseline) { "1" } else { $null }
$env:PSX_SMOOTH_ALLOW_LOOSEN = if ($AllowLoosen) { "1" } else { $null }
$env:PSX_SMOOTH_FAST = if ($Mode -eq "FAST") { "1" } else { $null }
$runStart = Get-Date

function Invoke-Method([string]$Method, [string]$Expect, [int]$Minutes) {
    $outFile = if ($Expect) { Join-Path $proj $Expect } else { $null }
    if ($outFile -and (Test-Path $outFile)) { Remove-Item $outFile -Force }
    $log = "$proj\citysmooth.log"
    $ok = Invoke-UnityJob -Log $log -MaxMinutes $Minutes -UnityArgs @(
        "-quit", "-batchmode", "-projectPath", $proj,
        "-executeMethod", $Method,
        "-logFile", $log, "-accept-apiupdate")
    Select-String -Path $log -Pattern "error CS|Exception|\[CitySmooth\]" -ErrorAction SilentlyContinue | Select-Object -First 40 | ForEach-Object { $_.Line }
    if (-not $ok) { Write-Host "the job did not finish; log tail:" -ForegroundColor Red; Get-Content $log -Tail 30 -ErrorAction SilentlyContinue; return $false }
    if ($outFile -and -not (Test-Path $outFile)) { Write-Host "no $Expect written - the method threw; log tail:" -ForegroundColor Red; Get-Content $log -Tail 30 -ErrorAction SilentlyContinue; return $false }
    return $true
}

$failed = $false
New-Item -ItemType Directory -Force -Path $Out | Out-Null
if ($Mode -eq "FAST") {
    if (-not (Invoke-Method "PSXRacing.EditorTools.CityAudit.Run" "city_audit.txt" 25)) { exit 1 }
    Select-String -Path (Join-Path $proj "city_audit.txt") -Pattern "smoothness" | ForEach-Object { $_.Line }
    if (Select-String -Path (Join-Path $proj "city_audit.txt") -Pattern "FAIL smoothness" -Quiet) { $failed = $true }
}
elseif ($Mode -eq "FULL") {
    if (-not (Invoke-Method "PSXRacing.EditorTools.CitySmooth.RunFull" "city_smooth.txt" 40)) { exit 1 }
    if (Select-String -Path "$proj\citysmooth.log" -Pattern "\[CitySmooth\] FAIL" -Quiet) { $failed = $true }
}
if (Test-Path (Join-Path $proj "city_smooth.txt")) {
    Get-Content (Join-Path $proj "city_smooth.txt") | Select-Object -First 60
    foreach ($f in @("city_smooth.txt", "city_smooth.csv", "city_smooth.json")) {
        if (Test-Path (Join-Path $proj $f)) { Copy-Item (Join-Path $proj $f) $Out -Force }
    }
}
if ($WriteBaseline) {
    # only a baseline THIS run wrote: the copy made before the run is the old one
    if ((Test-Path $boxBase) -and (Get-Item $boxBase).LastWriteTime -ge $runStart) {
        New-Item -ItemType Directory -Force -Path (Split-Path $repoBase) | Out-Null
        Copy-Item $boxBase $repoBase -Force
        $rep = @(Get-Content (Join-Path $proj "city_smooth.txt") -ErrorAction SilentlyContinue)
        $at = ($rep | Select-String -Pattern '^BEFORE' | Select-Object -First 1)
        if ($at) { $rep[($at.LineNumber - 1)..([Math]::Min($rep.Count - 1, $at.LineNumber + 18))] }
        Write-Host "baseline recorded: $repoBase (commit it with the BEFORE -> AFTER numbers above, from city_smooth.txt)"
    } else { Write-Host "no baseline was written by this run in the sandbox" -ForegroundColor Red; $failed = $true }
}

if ($Mode -eq "SHOTS" -or ($Mode -eq "FULL" -and -not $NoShots)) {
    $shotDir = Join-Path $proj "Screenshots\City\smooth"
    if (Test-Path $shotDir) { Remove-Item "$shotDir\*" -Force -ErrorAction SilentlyContinue }
    if (Invoke-Method "PSXRacing.EditorTools.CitySmooth.RunShots" "" 30) {
        if (Test-Path "$shotDir\smooth_shots.txt") {
            Copy-Item "$shotDir\*" $Out -Force
            $sheetArgs = @("$src\tools\city\refsheet.py", $Out, "--title", "Smoothness gate - $Label")
            if ($Before) { $sheetArgs += @("--before", $Before) }
            & py @sheetArgs
            if ($LASTEXITCODE -ne 0) { Write-Host "the contact sheet failed" -ForegroundColor Red; $failed = $true }
        } else { Write-Host "SHOTS wrote nothing" -ForegroundColor Red; $failed = $true }
    } else { $failed = $true }
}

foreach ($v in @("PSX_SMOOTH_BAND", "PSX_SMOOTH_SPOTS", "PSX_SMOOTH_SHOTS", "PSX_SMOOTH_WRITE_BASELINE", "PSX_SMOOTH_ALLOW_LOOSEN", "PSX_SMOOTH_FAST")) { Remove-Item "env:$v" -ErrorAction SilentlyContinue }
$report = Join-Path $proj "city_smooth.txt"
if (-not $WriteBaseline -and (Test-Path $report) -and (Select-String -Path $report -Pattern "^  STALE:" -Quiet)) {
    # the gate logs it as a FAIL (outside a report-only cycle); say why, loudly, whatever the log held
    Write-Host "baseline STALE: re-record with -Mode FULL -WriteBaseline (BEFORE -> AFTER numbers in the commit)" -ForegroundColor Red
    if (-not (Select-String -Path $report -Pattern "REPORT-ONLY cycle" -Quiet)) { $failed = $true }
}
if ($failed) { Write-Host "CITY SMOOTH $Mode DONE - FAILED -> $Out" -ForegroundColor Red; exit 1 }
Write-Host "CITY SMOOTH $Mode DONE -> $Out"
exit 0
