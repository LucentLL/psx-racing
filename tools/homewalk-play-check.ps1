# Walking into your own house, in the running game: on foot on the drive, the
# car with the keys in the garage, the others on the lawn, beds that offer
# sleep, no snow indoors, the lift taking the REAL car up and down, and GET IN.
#
#   powershell -ExecutionPolicy Bypass -File tools\homewalk-play-check.ps1
#
# Code only, on a sandbox whose Neighborhood scene is built (tools\nb-check.ps1
# -SkipMirror builds just that). Exit 0 = it works.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_homewalk_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\homewalk.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.HomeWalkPlayCheck.Run",
    "-logFile","$proj\homewalk.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\homewalk.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_homewalk_play_check.txt") {
    Get-Content "$proj\PSXRacing_homewalk_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_homewalk_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of homewalk.log:"
Get-Content "$proj\homewalk.log" -Tail 40
exit 1
