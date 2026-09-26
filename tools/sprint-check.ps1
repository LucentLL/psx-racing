# Race every sprint that runs on a loop's road (TrackDef.sprintOf) in its loop's scene:
# the whole loop standing, the list rotated to the sprint's start, the grid on
# that line, a finish band where the finish is, and a car carried from the line
# timed out at the quoted distance.
#
#   powershell -ExecutionPolicy Bypass -File tools\sprint-check.ps1
#
# Code only, on a sandbox whose loop scenes are built. Exit 0 = it works.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_sprint_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\sprintcheck.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SprintRaceCheck.Run",
    "-logFile","$proj\sprintcheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\sprintcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_sprint_check.txt") {
    Get-Content "$proj\PSXRacing_sprint_check.txt"
    if (Select-String -Path "$proj\PSXRacing_sprint_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of sprintcheck.log:"
Get-Content "$proj\sprintcheck.log" -Tail 40
exit 1
