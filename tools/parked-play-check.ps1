# THE LEFT CAR: park, get out, wait 30 s, measure (owner 2026-11-11: "I parked my
# Honda at a car meet and it rolled away, lights on, front suspension lifted like
# a monster truck"). Editor/ParkedPlayCheck.cs, in the running Town on a meet
# night: each car in a free stall of the Eastside Lot and on a 15% slope, left in
# R, out the real way (ForecourtMode.GetOut); drift, gear, lever, hold, lamps and
# the drawn tyre-to-arch gaps against the car at rest with the driver in. Runs
# the list with the left-car rules OFF (before) and ON (after); only ON grades.
#
#   powershell -ExecutionPolicy Bypass -File tools\parked-play-check.ps1 [-Cars civic_eg,roadster_99] [-Modes after] [-Seconds 30] [-NoWatch]
#
# Code only, on a sandbox whose Town scene is built (PSX_SANDBOX, default PSXBuild).
# Keeps the sandbox's build target (-BuildTarget, default: read it from the
# sandbox's last editor log is not reliable, so pass it; PSXRec is Android).
param([switch]$NoWatch, [int]$MaxMinutes = 60, [string]$Cars = "", [string]$Shots = "", [string]$Modes = "",
      [int]$Seconds = 30, [string]$BuildTarget = "", [switch]$NoSync)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if (-not $NoSync) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}
Remove-Item "$proj\PSXRacing_parked_play_check.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\Parked" -Recurse -ErrorAction SilentlyContinue

if ($Cars)  { $env:PSX_PARK_CARS = $Cars }   else { Remove-Item Env:PSX_PARK_CARS -ErrorAction SilentlyContinue }
if ($Shots) { $env:PSX_PARK_SHOTS = $Shots } else { Remove-Item Env:PSX_PARK_SHOTS -ErrorAction SilentlyContinue }
if ($Modes) { $env:PSX_PARK_MODES = $Modes } else { Remove-Item Env:PSX_PARK_MODES -ErrorAction SilentlyContinue }
$env:PSX_PARK_SECONDS = "$Seconds"

$uargs = @("-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ParkedPlayCheck.Run",
    "-logFile","$proj\parkedcheck.log","-accept-apiupdate")
if ($BuildTarget) { $uargs += @("-buildTarget", $BuildTarget) }
# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\parkedcheck.log" -MaxMinutes $MaxMinutes -UnityArgs $uargs | Out-Null

Select-String -Path "$proj\parkedcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_parked_play_check.txt") {
    Get-Content "$proj\PSXRacing_parked_play_check.txt" | Where-Object { $_ -notmatch "^    " }
    if (Select-String -Path "$proj\PSXRacing_parked_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of parkedcheck.log:"
Get-Content "$proj\parkedcheck.log" -Tail 40
exit 1
