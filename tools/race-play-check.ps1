# A whole field racing on autopilot: every hard hit and every retirement,
# logged (RacePlayCheck). Copies Scripts + Editor into the sandbox (no mirror,
# no scene build: the scenes must already be built there).
#
#   powershell -ExecutionPolicy Bypass -File tools\race-play-check.ps1 -Venue GillespieGap -Seconds 150 -Seed 0
#   ... -Venue ChimneyRock -Seconds 600 -Finish -MaxMinutes 30   (the whole race: every rival home)
#   ...  -Edition MAIN   plays it AS the MAIN edition (Scripts/Edition.cs): the
#                        runtime's filters and door rules are MAIN's. ALL by default.
#   ...  -Traffic HEAVY  the race's traffic setting: NONE, LIGHT, MEDIUM, HEAVY or
#                        RUSH (RUSH HOUR); empty = the hour's own (what a delivery gets).
#   ...  -TimeScale 3    the race at 3x game speed (physics keeps its fixed step).
#   ...  -Matrix "BlueRidge:HEAVY:0;CityCircuit:RUSH:1"  several races in ONE editor
#                        session; one SUMMARY line each in PSXRacing_race_matrix.txt.
# Several races in ONE launch (a line per race at the end of the report):
#   ... -Venues UptownLoop,TryonSprint,IndependenceSprint -Seeds 0,1,2,3,4 -Trees ab
# -Trees: 1 city trees on (default), 0 off, ab every race twice (on, then off).
# -Signs: the city's billboards, pole signs and gantries (WP-23), the same way.
# -Poles: the city's utility poles and wires (WP-15), the same way (off: the
#   lamp posts stand on their roads as before).
# -MaxMinutes: 0 (default) budgets the batch: 20 minutes, or more for many races.
param([string]$Venue = "GillespieGap", [int]$Seconds = 150, [int]$Seed = 0, [string]$Hour = "morning", [string]$Mistake = "",
      [switch]$NoWatch, [int]$MaxMinutes = 0, [switch]$Finish, [string]$Edition = "ALL",
      [string]$Traffic = "", [double]$TimeScale = 1, [string]$Matrix = "",
      [string]$Venues = "", [string]$Seeds = "", [string]$Trees = "1", [string]$Signs = "1", [string]$Poles = "1")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_race_play_check.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\PSXRacing_race_matrix.txt" -ErrorAction SilentlyContinue
$env:PSX_RACE_TRAFFIC = $Traffic
$env:PSX_RACE_TIMESCALE = "$TimeScale"
$env:PSX_RACE_MATRIX = $Matrix
$env:PSX_RACE_VENUE = $Venue
$env:PSX_RACE_SECONDS = "$Seconds"
$env:PSX_RACE_SEED = "$Seed"
$env:PSX_RACE_HOUR = $Hour
$env:PSX_RACE_MISTAKE = $Mistake
$env:PSX_EDITION = $Edition.ToUpperInvariant()
# -Finish: the whole distance, on past the player's flag until every rival is
# home, failing a rival that never gets there or drives the wrong way.
$env:PSX_RACE_FINISH = if ($Finish) { "1" } else { "" }
$env:PSX_RACE_VENUES = $Venues
$env:PSX_RACE_SEEDS = $Seeds
$env:PSX_CITY_TREES = $Trees
$env:PSX_CITY_SIGNS = $Signs
$env:PSX_CITY_POLES = $Poles
# a race is at most $Seconds plus about a minute of loading; one launch runs them all
$races = [Math]::Max(1, ($(if ($Venues) { $Venues } else { $Venue }).Split(",").Count) * ($(if ($Seeds) { $Seeds } else { "$Seed" }).Split(",").Count) * $(if ($Trees -eq "ab") { 2 } else { 1 }) * $(if ($Signs -eq "ab") { 2 } else { 1 }) * $(if ($Poles -eq "ab") { 2 } else { 1 }))
if ($Matrix) { $races = [Math]::Max($races, $Matrix.Split(';').Count) }
if ($MaxMinutes -le 0) { $MaxMinutes = [Math]::Max(20, [int]($races * ($Seconds + 60) / 60) + 10) }
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\raceplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.RacePlayCheck.Run",
    "-logFile","$proj\raceplay.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\raceplay.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_race_matrix.txt") { "=== MATRIX ==="; Get-Content "$proj\PSXRacing_race_matrix.txt" }
if (Test-Path "$proj\PSXRacing_race_play_check.txt") { if (-not $Matrix) { Get-Content "$proj\PSXRacing_race_play_check.txt" }; exit 0 }
"NO REPORT. Tail of raceplay.log:"
Get-Content "$proj\raceplay.log" -Tail 30
exit 1
