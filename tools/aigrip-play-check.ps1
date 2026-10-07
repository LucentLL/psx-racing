# The AI's grip, MEASURED (AIGripPlayCheck): a steady skidpad at three radii
# and a full ABS stop for cars of every layout, each against AIGrip's
# prediction - the ratios AIGrip.LateralCal / BrakeCal are set from.
#
#   powershell -ExecutionPolicy Bypass -File tools/aigrip-play-check.ps1 -NoWatch
#   ... -Cars "RX-7 Type RS;VIPER GTS"   just those (name substrings)
#
param([switch]$NoWatch, [int]$MaxMinutes = 30, [string]$Cars = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_aigrip_play_check.txt" -ErrorAction SilentlyContinue
$env:PSX_AIGRIP_CARS = $Cars

Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\aigripplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.AIGripPlayCheck.Run",
    "-logFile","$proj\aigripplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\aigripplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_aigrip_play_check.txt") { Get-Content "$proj\PSXRacing_aigrip_play_check.txt"; exit 0 }
"NO REPORT - the run threw. Tail of aigripplay.log:"
Get-Content "$proj\aigripplay.log" -Tail 40
exit 1
