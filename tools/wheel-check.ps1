# RUMBLE / WHEEL / FORCE FEEDBACK checks (Editor/ControlsCheck.cs): the rumble
# mixer and FFB model on scripted events, the wheel arithmetic and capture on
# scripted pads, then PLAY MODE with a simulated HID wheel under a real car.
#
#   powershell -ExecutionPolicy Bypass -File tools\wheel-check.ps1 [-EditOnly] [-NoWatch]
#
# Code only into the sandbox (Scripts, Editor, Plugins; /E so the sandbox's
# own scenes and metas survive). Report: PSXRacing_controls_check.txt.
param([switch]$NoWatch, [switch]$EditOnly, [int]$MaxMinutes = 25)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\Plugins")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\PSXRacing_controls_check.txt" -ErrorAction SilentlyContinue
if ($EditOnly) { $env:PSX_CONTROLS_EDIT_ONLY = "1" } else { Remove-Item Env:\PSX_CONTROLS_EDIT_ONLY -ErrorAction SilentlyContinue }

$watch = if ($EditOnly) { $false } else { Test-PSXWatch -NoWatch:$NoWatch }
Invoke-UnityJob -Watch:$watch -Log "$proj\controlscheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ControlsCheck.Run",
    "-logFile","$proj\controlscheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\controlscheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_controls_check.txt") {
    Get-Content "$proj\PSXRacing_controls_check.txt"
    if (Select-String -Path "$proj\PSXRacing_controls_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of controlscheck.log:"
Get-Content "$proj\controlscheck.log" -Tail 40
exit 1
