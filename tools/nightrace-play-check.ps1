# A race at NIGHT, booked on the planner and started at the line, in the
# running game: the six-block day (save v21) lets a race be written into
# tonight's NIGHT block, and START runs it under the NIGHT sky.
#
#   powershell -ExecutionPolicy Bypass -File tools\nightrace-play-check.ps1
#
# Code only, on a sandbox whose scenes are built. Exit 0 = it works.
param([switch]$NoWatch, [int]$MaxMinutes = 20)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_nightrace_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
# Watched by default: a visible editor plays the test in front of you.
# -NoWatch (or $env:PSX_WATCH='0') runs it hidden; -MaxMinutes raises the
# budget (a cold sandbox imports for an hour). See tools\unity-wait.ps1.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\nightrace.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.NightRacePlayCheck.Run",
    "-logFile","$proj\nightrace.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\nightrace.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_nightrace_play_check.txt") {
    Get-Content "$proj\PSXRacing_nightrace_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_nightrace_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of nightrace.log:"
Get-Content "$proj\nightrace.log" -Tail 40
exit 1
