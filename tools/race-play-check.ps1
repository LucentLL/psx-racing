# A whole field racing on autopilot: every hard hit and every retirement,
# logged (RacePlayCheck). Copies Scripts + Editor into the sandbox (no mirror,
# no scene build: the scenes must already be built there).
#
#   powershell -ExecutionPolicy Bypass -File tools\race-play-check.ps1 -Venue GillespieGap -Seconds 150 -Seed 0
#   ... -Venue ChimneyRock -Seconds 600 -Finish -MaxMinutes 30   (the whole race: every rival home)
param([string]$Venue = "GillespieGap", [int]$Seconds = 150, [int]$Seed = 0, [string]$Hour = "morning", [string]$Mistake = "", [switch]$NoWatch, [int]$MaxMinutes = 20, [switch]$Finish)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_race_play_check.txt" -ErrorAction SilentlyContinue
$env:PSX_RACE_VENUE = $Venue
$env:PSX_RACE_SECONDS = "$Seconds"
$env:PSX_RACE_SEED = "$Seed"
$env:PSX_RACE_HOUR = $Hour
$env:PSX_RACE_MISTAKE = $Mistake
# -Finish: the whole distance, on past the player's flag until every rival is
# home, failing a rival that never gets there or drives the wrong way.
$env:PSX_RACE_FINISH = if ($Finish) { "1" } else { "" }
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\raceplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.RacePlayCheck.Run",
    "-logFile","$proj\raceplay.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\raceplay.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_race_play_check.txt") { Get-Content "$proj\PSXRacing_race_play_check.txt"; exit 0 }
"NO REPORT. Tail of raceplay.log:"
Get-Content "$proj\raceplay.log" -Tail 30
exit 1
