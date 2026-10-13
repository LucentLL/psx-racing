# The sun-in-the-lens and wet-road shots (GlareShots, 2026-10-13, the GT look
# plan item 2) on an already-built sandbox: Scripts, Editor, Shaders and the
# always-included shader list are copied over first, then
# PSXRacing.EditorTools.GlareShots.Run renders through the pipeline (graphics
# ON). Writes Screenshots\glare_*.png and wet_*.png; the eye pixel per shot
# is in the log as [GlareShot].
#
#   powershell -ExecutionPolicy Bypass -File tools\glare-shots.ps1
#   PSX_GLARE_VENUES=BlueRidge,CityCircuit  PSX_GLARE_HOURS=afternoon,sunset
#   PSX_GLARE_WET=0 skips the wet-road pair.
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
Remove-Item "$proj\Screenshots\glare_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\wet_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\glareshots.log" -ErrorAction SilentlyContinue

Invoke-UnityJob -Log "$proj\glareshots.log" -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.GlareShots.Run",
    "-logFile","$proj\glareshots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\glareshots.log" -Pattern "GlareShot|error CS|Shader error|Exception" |
    Select-Object -First 40 | ForEach-Object { $_.Line }
Get-ChildItem "$proj\Screenshots" -Filter "glare_*.png" | ForEach-Object { $_.Name }
Get-ChildItem "$proj\Screenshots" -Filter "wet_*.png" | ForEach-Object { $_.Name }
