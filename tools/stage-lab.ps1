# Build ONE stage (or a few) and audit its roadside, in minutes: the self-test's
# roadside checks and the obstacle audit's passes on just that scene. For
# shaping the stage builder against a road it does not handle yet - the venues
# in TrackCatalog.HeldBack build here too.
#
#   powershell -ExecutionPolicy Bypass -File tools\stage-lab.ps1 ChimneyRock,GillespieGap
#
# Code (and Resources/Art) are copied over the sandbox; no mirror, no other scene.
param([Parameter(Mandatory=$true)][string]$Ids)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Resources", "Assets\PSXRacing\Art")) {
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_stage_lab.txt" -ErrorAction SilentlyContinue
$env:PSX_LAB_IDS = $Ids
Invoke-UnityJob -Log "$proj\stagelab.log" -MaxMinutes 30 -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.StageLab.Run",
    "-logFile","$proj\stagelab.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\stagelab.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_stage_lab.txt") { Get-Content "$proj\PSXRacing_stage_lab.txt"; exit 0 }
"NO REPORT. Tail of stagelab.log:"
Get-Content "$proj\stagelab.log" -Tail 40
exit 1
