# What did the roadside audit see at a spot? Runs CityRoadsideProbe in the
# sandbox (Scripts+Editor copy, no scene build): the tiles round each spot are
# stood up as the audit stands them, and the walk out from the lane edge, the
# ground terms and the roads round it are printed.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\city-roadside-probe.ps1 -Spots "328:111.4:1;11145:6.5:1"
#
# A spot is edge:s:side (side -1 left, 1 right), as the audit's notes name it.
param([Parameter(Mandatory = $true)][string]$Spots)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# /XO: Resources holds baked output (see city-cycle.ps1)
robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null

Remove-Item "$proj\city_roadside_probe.txt" -ErrorAction SilentlyContinue
$env:PSX_RSPROBE = $Spots
Invoke-UnityJob -Log "$proj\rsprobe.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityRoadsideProbe.Run",
    "-logFile","$proj\rsprobe.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\rsprobe.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\city_roadside_probe.txt") { Get-Content "$proj\city_roadside_probe.txt"; exit 0 }
"NO PROBE OUTPUT - tail of rsprobe.log:"
Get-Content "$proj\rsprobe.log" -Tail 30
exit 1
