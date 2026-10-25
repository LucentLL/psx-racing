# THE SUSPENSION RIDE CHECK (Editor\SuspensionRideCheck.cs): the reference FD,
# the '70 Charger and a stage-4 S13 on the bump-stop harness's flat world (the
# control drive, a landing, a kerb, a 150 mph freeway flat and kinked, 90 mph
# kinked), then an UPTOWN LOOP (I-277) race on autopilot with every car's
# suspension tallied: bottoming hits, clunks (>= 2 m/s), time in the rubber
# and on the stop, travel used, least room left. Scripts + Editor (+ Resources
# /XO) only; the scenes must already be built in the sandbox.
#
#   powershell -ExecutionPolicy Bypass -File tools\susp-ride-check.ps1
#   ... -NoRace            the flat-world part only (exits after it)
#   ... -Seconds 150       race length
#   ... -NoWatch           hidden editor
param([switch]$NoWatch, [switch]$NoRace, [int]$Seconds = 150, [int]$MaxMinutes = 25,
      [string]$Cars = "dodge_charger_440_r_t__70;dodge_charger_440_r_t__70;mazda_rx_7_type_rs__fd___98")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
Remove-Item "$proj\PSXRacing_susp_ride.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\PSXRacing_race_play_check.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\PSXRacing_race_matrix.txt" -ErrorAction SilentlyContinue
$env:PSX_SUSP_RACE = if ($NoRace) { "0" } else { "1" }
$env:PSX_RACE_VENUE = "UptownLoop"
$env:PSX_RACE_SECONDS = "$Seconds"
$env:PSX_RACE_SEED = "0"
$env:PSX_RACE_TRAFFIC = "NONE"
$env:PSX_RACE_CARS = $Cars
$env:PSX_EDITION = "ALL"
# NO -quit: the race leaves for play mode and exits itself.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\suspride.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SuspensionRideCheck.Run",
    "-logFile","$proj\suspride.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\suspride.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_susp_ride.txt") { Get-Content "$proj\PSXRacing_susp_ride.txt" } else { "NO FLAT-WORLD REPORT" }
if (Test-Path "$proj\PSXRacing_race_play_check.txt") {
    Select-String -Path "$proj\PSXRacing_race_play_check.txt" -Pattern "SUSP |FAIL|RETIRED|race goes live|field " | ForEach-Object { $_.Line }
}
