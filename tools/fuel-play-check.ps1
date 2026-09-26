# Fill up in the town, in the running game: parked at a pump the FUEL button is
# offered; on foot, looking at the pump, it says FILL UP; pressed, the tank
# rises and the money goes; STOP stops it.
#
#   powershell -ExecutionPolicy Bypass -File toolsuel-play-check.ps1
#
# Code only, on a sandbox whose town is built. Exit 0 = it works.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_fuel_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\fuelcheck.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.FuelPlayCheck.Run",
    "-logFile","$proj\fuelcheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\fuelcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_fuel_play_check.txt") {
    Get-Content "$proj\PSXRacing_fuel_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_fuel_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of fuelcheck.log:"
Get-Content "$proj\fuelcheck.log" -Tail 40
exit 1
