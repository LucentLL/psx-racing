# Look again at the stage the last stage-lab run built, without rebuilding it:
# cross-sections (PSX_LAB_WP, "1300L") and top-down collider plans
# (PSX_LAB_PLAN, "1049" or "1049:80" for an 80 m square) written as
# PSXRacing_lab_plan_<id>_<wp>.png in the sandbox. About a minute.
#
#   powershell -ExecutionPolicy Bypass -File tools\stage-plan.ps1 ChimneyRock -Plan "1049,666:80" -Wp "1048L"
param([Parameter(Mandatory=$true)][string]$Ids, [string]$Plan = "", [string]$Wp = "")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

robocopy "$src\Assets\PSXRacing\Editor" "$proj\Assets\PSXRacing\Editor" /E /XO /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
Remove-Item "$proj\PSXRacing_stage_lab.txt" -ErrorAction SilentlyContinue
$env:PSX_LAB_IDS = $Ids
$env:PSX_LAB_NOBUILD = "1"
$env:PSX_LAB_PLAN = $Plan
$env:PSX_LAB_WP = $Wp
Invoke-UnityJob -Log "$proj\stageplan.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.StageLab.Run",
    "-logFile","$proj\stageplan.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\stageplan.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_stage_lab.txt") { Get-Content "$proj\PSXRacing_stage_lab.txt"; exit 0 }
"NO REPORT. Tail of stageplan.log:"
Get-Content "$proj\stageplan.log" -Tail 40
exit 1
