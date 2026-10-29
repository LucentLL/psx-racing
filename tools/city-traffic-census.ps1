# THE ROAD-LOGIC CENSUS BY TRAFFIC (Editor/CityTrafficCensus.cs). Charlotte
# free roam at rush hour with the player a ghost that tours the whole city;
# every traffic car is a probe and logs what it meets (wrecks and what was
# hit, stuck cars, missed turn lanes, merges, off-pavement, wrong way, jolts,
# tight bends, crossing flows, plus a static audit of signals, control,
# one-way traps, limits, lane alignment). Then tools\traffic_census_report.py
# ranks it into Docs\ROAD_LOGIC_TRAFFIC.md, a hotspot map, the events CSV, and
# a second Unity job photographs the top 6 spots.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-traffic-census.ps1
#   ... -Minutes 60      the tour's whole budget (default 60)
#   ... -MaxSpots 8      a smoke run: only the 8 best spots
#   ... -Cap 64          cars round the ghost (default 64; the game's is 48)
#   ... -Speed 30        the time scale ceiling (physics stays 1/60 s a step)
#   ... -NoShots         skip the close-ups job
#   ... -ShotsOnly       only the close-ups of the last census (census\top6.txt)
#   ... -ReportOnly      only re-rank the last census copied back
#   ... -ThenCheck       run tools\city-traffic-check's play check in the same editor after the tour
#   ... -Watch           show the tour in a visible editor (it runs hidden: an audit)
#   ... -Spots <file>    THE SPOT LIST: tour only the "x,z" lines of <file> (a
#                        label may follow; # comments), each the same share of
#                        the budget - a fix's before/after at its own places
#                        (e.g. -Spots Docs\traffic_census\spots_lanes.txt -Minutes 20 -NoShots -ReportDir <dir>)
#   ... -ReportDir <dir> the report, map, CSVs and raw data go to <dir> instead of
#                        Docs\ (a spot-list run must not overwrite the city-wide census)
#   ... -Spots f.txt     visit ONLY the places in f.txt ("x,z[,label]" a line), in
#                        order, an equal share each: a before/after census of fixed spots
#   ... -SpotSeconds 180 game seconds at each of those spots (0: the budget decides)
#   ... -Tag name        write to Docs\traffic_census_<name>\ and Docs\ROAD_LOGIC_TRAFFIC_<name>.md
#                        (a spot census must not overwrite the city-wide one)
#
# SANDBOX RULES: waits for another agent's AGENT_LOCK.txt (younger than 90
# min), writes its own, mirrors ONLY Scripts / Editor / Shaders (never
# Resources or Art), deletes the lock even on failure. Raw data comes back to
# Docs\traffic_census\raw\ (git-ignored); the report goes to Docs\.
param([int]$Minutes = 60, [int]$MaxSpots = 0, [int]$Cap = 64, [double]$Speed = 30,
      [switch]$NoShots, [switch]$ShotsOnly, [switch]$ReportOnly, [switch]$ThenCheck, [switch]$Watch, [switch]$NoWatch,
      [string]$Spots = "", [string]$ReportDir = "", [string]$Tag = "", [int]$SpotSeconds = 0,
      [string]$Me = "city-traffic-census")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXCity" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$lock = Join-Path $proj "AGENT_LOCK.txt"
$outName = if ($Tag) { "traffic_census_$Tag" } else { "traffic_census" }
$mdName = if ($Tag) { "ROAD_LOGIC_TRAFFIC_$Tag.md" } else { "ROAD_LOGIC_TRAFFIC.md" }
$outDir = if ($ReportDir) { $ReportDir } else { Join-Path $src "Docs\$outName" }
$mdPath = if ($ReportDir) { Join-Path $ReportDir "ROAD_LOGIC_TRAFFIC.md" } else { Join-Path $src "Docs\$mdName" }
$raw = Join-Path $outDir "raw"
New-Item -ItemType Directory -Force $raw | Out-Null
if (-not (Test-Path "$raw\.gitignore")) { "*" | Out-File -FilePath "$raw\.gitignore" -Encoding ascii }

function Wait-Lock {
    while (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
        if ($txt.StartsWith($Me) -or $age -gt 90) { break }
        "sandbox locked by '$txt' ($([int]$age) min) - waiting"
        Start-Sleep -Seconds 120
    }
}
function Write-Lock { "$Me $(Get-Date -Format s)" | Out-File -FilePath $lock -Encoding ascii }
# A LOG GUARD (2026-10-29: a component that failed every frame with no graphics
# wrote 1.8 GB of log in ten minutes): a background job stops this project's
# Unity if its log passes 3 GB - a play-mode job, never an import or a build.
function Start-LogGuard([string]$Log) {
    return Start-Job -ArgumentList $Log, $proj -ScriptBlock {
        param($log, $p)
        while ($true) {
            Start-Sleep -Seconds 15
            if ((Test-Path $log) -and (Get-Item $log).Length -gt 3GB) {
                Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$p*" } |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
                "LOG GUARD: $log passed 3 GB - Unity stopped"
                break
            }
        }
    }
}
function Invoke-Report {
    & py -3 "$PSScriptRoot\traffic_census_report.py" --raw $raw --out $outDir --md $mdPath --top6 (Join-Path $proj "census\top6.txt")
    if ($LASTEXITCODE -ne 0) { "REPORT FAILED ($LASTEXITCODE)"; return $false }
    return $true
}

if ($ReportOnly) { if (Invoke-Report) { exit 0 } else { exit 1 } }

Wait-Lock
Write-Lock
$code = 0
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $env:PSX_EDITION = "ALL"
    if (-not $ShotsOnly) {
        Remove-Item "$proj\PSXRacing_city_traffic_census.txt", "$proj\PSXRacing_city_traffic_check.txt" -ErrorAction SilentlyContinue
        Remove-Item "$proj\census\*" -ErrorAction SilentlyContinue
        $env:PSX_CENSUS_MINUTES = "$Minutes"
        $env:PSX_CENSUS_MAXSPOTS = "$MaxSpots"
        $env:PSX_CENSUS_CAP = "$Cap"
        $env:PSX_CENSUS_SPEED = "$Speed"
        $env:PSX_CENSUS_THENCHECK = if ($ThenCheck) { "1" } else { "0" }
        $env:PSX_CENSUS_SPOTS = if ($Spots) { (Resolve-Path $Spots).Path } else { "" }
        $env:PSX_CENSUS_SPOT_S = "$SpotSeconds"
        $max = $Minutes + 25 + $(if ($ThenCheck) { 25 } else { 0 })
        "census: $Minutes min budget, $(if ($MaxSpots -gt 0) { "$MaxSpots spots" } else { "every spot" }), cap $Cap, time scale up to $Speed$(if ($ThenCheck) { ', then the traffic check' })"
        Remove-Item "$proj\census.log" -ErrorAction SilentlyContinue
        $guard = Start-LogGuard "$proj\census.log"
        Invoke-UnityJob -Watch:($Watch -and -not $NoWatch) -Log "$proj\census.log" -MaxMinutes $max -UnityArgs @(
            "-batchmode","-nographics","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.CityTrafficCensus.Run",
            "-logFile","$proj\census.log","-accept-apiupdate") | Out-Null
        Stop-Job $guard -ErrorAction SilentlyContinue; Receive-Job $guard -ErrorAction SilentlyContinue; Remove-Job $guard -Force -ErrorAction SilentlyContinue
        Select-String -Path "$proj\census.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
        if (Test-Path "$proj\PSXRacing_city_traffic_census.txt") {
            Get-Content "$proj\PSXRacing_city_traffic_census.txt"
            if (Select-String -Path "$proj\PSXRacing_city_traffic_census.txt" -Pattern "FAIL" -Quiet) { $code = 1 }
        } else {
            "NO CENSUS REPORT - the run threw. Tail of census.log:"
            Get-Content "$proj\census.log" -Tail 40
            $code = 1
        }
        if ($ThenCheck) {
            if (Test-Path "$proj\PSXRacing_city_traffic_check.txt") {
                Get-Content "$proj\PSXRacing_city_traffic_check.txt"
                if (Select-String -Path "$proj\PSXRacing_city_traffic_check.txt" -Pattern "FAIL" -Quiet) { $code = 1 }
            } else { "NO TRAFFIC CHECK REPORT (it runs after the census in the same editor)"; $code = 1 }
        }
        Get-ChildItem "$raw\*" -Include *.csv, *.txt, *.json, *.png -ErrorAction SilentlyContinue | Remove-Item
        Get-ChildItem "$proj\census\*" -ErrorAction SilentlyContinue | Copy-Item -Destination $raw
        if (Test-Path "$raw\summary.json") { if (-not (Invoke-Report)) { $code = 1 } }
    }
    if (-not $NoShots -and (Test-Path "$proj\census\top6.txt")) {
        Write-Lock
        Remove-Item "$proj\PSXRacing_city_traffic_census_shots.txt" -ErrorAction SilentlyContinue
        # -ShotsOnly -ThenCheck: the traffic check follows the close-ups in the same (watched) editor
        $env:PSX_CENSUS_THENCHECK = if ($ThenCheck -and $ShotsOnly) { "1" } else { "0" }
        if ($ThenCheck -and $ShotsOnly) { Remove-Item "$proj\PSXRacing_city_traffic_check.txt" -ErrorAction SilentlyContinue }
        Get-ChildItem "$proj\Screenshots\census_top*.png" -ErrorAction SilentlyContinue | Remove-Item
        # the list photographed travels with the pictures (the sheet's captions)
        Copy-Item (Join-Path $proj "census\top6.txt") (Join-Path $raw "shots_top6.txt") -Force
        Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\censusshots.log" -MaxMinutes 30 -UnityArgs @(
            "-batchmode","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.CityTrafficCensus.Shots",
            "-logFile","$proj\censusshots.log","-accept-apiupdate") | Out-Null
        if (Test-Path "$proj\PSXRacing_city_traffic_census_shots.txt") { Get-Content "$proj\PSXRacing_city_traffic_census_shots.txt" }
        else { "NO SHOTS REPORT. Tail of censusshots.log:"; Get-Content "$proj\censusshots.log" -Tail 30; $code = 1 }
        Get-ChildItem "$proj\Screenshots\census_top*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $raw
        if ($ThenCheck -and $ShotsOnly) {
            if (Test-Path "$proj\PSXRacing_city_traffic_check.txt") {
                Get-Content "$proj\PSXRacing_city_traffic_check.txt"
                if (Select-String -Path "$proj\PSXRacing_city_traffic_check.txt" -Pattern "FAIL" -Quiet) { $code = 1 }
                $shots = Join-Path $src "Docs\shots\citytraffic"
                New-Item -ItemType Directory -Force $shots | Out-Null
                Get-ChildItem "$proj\Screenshots\citytraffic_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $shots
            } else { "NO TRAFFIC CHECK REPORT (it runs after the close-ups in the same editor)"; $code = 1 }
        }
        if (-not (Invoke-Report)) { $code = 1 }
    }
} finally {
    if (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        if ($txt.StartsWith($Me)) { Remove-Item $lock -Force }
    }
}
exit $code
