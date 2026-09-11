# Render the car-paint reference shots on an already-built sandbox: the
# player car in every camera view and at every hour of the first circuit
# (PSXScreenshotTool.CapturePaintOnly). Scripts, Editor and Shaders are copied
# over first, so a shader edit is a three-minute picture rather than a
# forty-minute build. Graphics ON: these render through the pipeline.
#
#   powershell -ExecutionPolicy Bypass -File tools\paint-shots.ps1
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Remove-Item "$proj\Screenshots\psx_cam_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\psx_hour_*.png" -ErrorAction SilentlyContinue

Invoke-UnityJob -Log "$proj\paintshots.log" -MaxMinutes 15 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CapturePaintOnly",
    "-logFile","$proj\paintshots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\paintshots.log" -Pattern "PSXShot|error CS|Shader error|Exception" |
    Select-Object -First 8 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "psx_cam_*.png" | ForEach-Object { $_.Name }
Get-ChildItem "$proj\Screenshots" -Filter "psx_hour_*.png" | ForEach-Object { $_.Name }
