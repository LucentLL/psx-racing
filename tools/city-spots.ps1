# city-spots.ps1 - the junction spot table (Docs/ROAD_RULES.md, section 8).
#
# Builds only the tiles round each row of tools/city/spots/junction_spots.csv in a Unity project (a sandbox that already
# holds this checkout's Scripts / Editor and the city data: this script does not copy code) and prints one line per spot:
#   SPOT <id> [<kind>]: RAISED n SEETHRU n OVERLAY n LINEUNDER n HOLES n LAYERS n STEP n KINK n => PASS | FAIL (...)
# followed by the head where that spot last passed (tools/city/spots/last_pass.csv, updated on a PASS) and its known
# failures (tools/city/spots/known.csv).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\city-spots.ps1 -Project C:\Users\me\PSXRoadA [-Ids 77,80] [-Unity <Unity.exe>]
# Edit-mode batch only (no play mode: silent by construction). Exit code 1 when any spot fails.
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$Ids = "",
    [string]$Unity = "",
    [int]$MaxMinutes = 90
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repo "tools\city\spots"))) { $repo = Split-Path -Parent $PSCommandPath | Split-Path -Parent }
$spots = Join-Path $repo "tools\city\spots\junction_spots.csv"
$known = Join-Path $repo "tools\city\spots\known.csv"
$last = Join-Path $repo "tools\city\spots\last_pass.csv"
if (-not $Unity) {
    $hub = "C:\Program Files\Unity\Hub\Editor"
    $Unity = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Unity -or -not (Test-Path $Unity)) { throw "Unity.exe not found: pass -Unity" }
$head = (git -C $repo rev-parse --short HEAD).Trim()
$tag = "spots"
$env:PSX_ISL_SPOTS = $spots
$env:PSX_ISL_TAG = $tag
$env:PSX_ISL_SPOT_IDS = $Ids
$env:PSX_TILECACHE = "0"
$log = Join-Path $Project "city_spots_unity.log"
$out = Join-Path $Project "city_islandgate_$tag.txt"
if (Test-Path $out) { Remove-Item $out -Force }
$p = Start-Process -FilePath $Unity -ArgumentList @("-batchmode", "-nographics", "-quit", "-projectPath", "`"$Project`"", "-executeMethod", "PSXRacing.EditorTools.CityIslandGate.Run", "-logFile", "`"$log`"") -PassThru -WindowStyle Hidden
if (-not $p.WaitForExit($MaxMinutes * 60 * 1000)) { throw "Unity did not finish in $MaxMinutes minutes (left running: PID $($p.Id))" }
if (-not (Test-Path $out)) { throw "no output: $out (see $log)" }
$lastPass = @{}
if (Test-Path $last) { Import-Csv $last | ForEach-Object { $lastPass[$_.id] = $_ } }
$knownRows = @(); if (Test-Path $known) { $knownRows = @(Import-Csv $known) }
$fail = $false
foreach ($line in Get-Content $out) {
    if ($line -notmatch "^SPOT (\S+) ") { continue }
    $id = $matches[1]
    $pass = $line -match "=> PASS"
    if (-not $pass) { $fail = $true }
    if ($pass) { $lastPass[$id] = [pscustomobject]@{ id = $id; head = $head; date = (Get-Date -Format "yyyy-MM-dd HH:mm") } }
    $lp = if ($lastPass.ContainsKey($id)) { "last passed at $($lastPass[$id].head) ($($lastPass[$id].date))" } else { "never passed" }
    Write-Host "$line | $lp"
    foreach ($k in ($knownRows | Where-Object { $_.id -eq $id })) { Write-Host "    known: $($k.check) $($k.value) (base $($k.base)) since $($k.since): $($k.reason)" }
}
$lastPass.Values | Sort-Object id | Export-Csv -NoTypeInformation -Path $last
foreach ($k in ($knownRows | Where-Object { $_.id -eq "city" })) { Write-Host "city known: $($k.check) $($k.value) (base $($k.base)) since $($k.since): $($k.reason)" }
if ($fail) { exit 1 } else { exit 0 }
