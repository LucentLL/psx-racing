# Do the sun's shadows fall where things ARE, in the RUNNING game?
#
#   powershell -ExecutionPolicy Bypass -File tools\sunshadow-play-check.ps1
#
# DayLookShots photographs the shadow maps in edit mode, where every mesh is
# its own mesh. In play Unity folds the static ones into combined meshes, and
# SunShadows redraws renderers by hand - so this enters play mode on the built
# circuit, lets the hook draw the maps for the real chase camera, reads the
# sun's map back and checks it against ray casts: the road is where the road
# is, the car is in it, it follows the car down the road, and night turns it
# off.
#
# Code and shaders only, on an already-built sandbox. Needs a graphics device
# (the map is read back): no -nographics. Exit 0 = the shadows are honest.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_sunshadow_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\sunshadowplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SunShadowPlayCheck.Run",
    "-logFile","$proj\sunshadowplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\sunshadowplay.log" -Pattern "error CS|Shader error" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_sunshadow_play_check.txt") {
    Get-Content "$proj\PSXRacing_sunshadow_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_sunshadow_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of sunshadowplay.log:"
Get-Content "$proj\sunshadowplay.log" -Tail 40
exit 1
