# How much of a car is at the top of the scale? The owner's frame, rebuilt:
# chase camera, sun ahead / behind / abeam, white, blue, red and dark liveries,
# each written with the car and without it so the paint can be MEASURED.
#
#   powershell -ExecutionPolicy Bypass -File tools\paint-glow-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\paint-glow-check.ps1 -Tag before
#
# Shader/Scripts/Editor only, on an already-built sandbox (copied over the top,
# no mirror; a shader edit needs no scene build). WITH a graphics device: it
# renders. -Tag names the contact sheet, so a before and an after can sit
# side by side in Screenshots\.
param([string]$Tag = "after")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\psx_glow.txt" -ErrorAction SilentlyContinue

Invoke-UnityJob -Log "$proj\paintglow.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PaintGlowCheck.Run",
    "-logFile","$proj\paintglow.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\paintglow.log" -Pattern "error CS|Shader error|Exception" |
    Select-Object -First 8 | ForEach-Object { $_.Line }
if (-not (Test-Path "$proj\Screenshots\psx_glow.txt")) {
    "NO SHOTS - the run threw. Tail of paintglow.log:"
    Get-Content "$proj\paintglow.log" -Tail 30
    exit 1
}
Get-Content "$proj\Screenshots\psx_glow.txt"
& py "$PSScriptRoot\paint\glow_stats.py" "$proj\Screenshots" "$proj\Screenshots\glow_sheet_$Tag.png"
exit 0
