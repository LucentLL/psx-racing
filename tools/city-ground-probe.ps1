# Why is the ground that height at a point? Runs CityGroundProbe in the
# sandbox (Scripts+Editor copy, no scene build) and prints the corridors that
# pin every lattice vertex round the point.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-ground-probe.ps1 -At "1939,2506"
param([string]$At = "")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

Remove-Item "$proj\city_ground_probe.txt" -ErrorAction SilentlyContinue
if ($At) { $env:PSX_PROBE = $At } else { Remove-Item Env:PSX_PROBE -ErrorAction SilentlyContinue }
Invoke-UnityJob -Log "$proj\groundprobe.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityGroundProbe.Run",
    "-logFile","$proj\groundprobe.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\groundprobe.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\city_ground_probe.txt") { Get-Content "$proj\city_ground_probe.txt" }
else { "NO PROBE OUTPUT - tail of groundprobe.log:"; Get-Content "$proj\groundprobe.log" -Tail 30 }
