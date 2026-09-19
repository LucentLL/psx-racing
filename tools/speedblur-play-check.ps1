# Run the speed blur in the RUNNING GAME and read the framebuffer back: a race
# is started under the Mobile pipeline (the one WebGL ships), the clock is
# stopped, and five frames are compared - blur off and blur full, each with the
# HUD and without it, plus a control with the HUD left on the world camera.
# Then SPEED BLUR is switched off (the HUD must go straight back, pixel for
# pixel) and the framebuffer is rebuilt at 480 lines (the HUD camera must
# follow it). A PLAY-MODE check: the stacked HUD camera, the canvas sizing and
# the pref toggle only exist once Update has run.
#
#   powershell -ExecutionPolicy Bypass -File tools\speedblur-play-check.ps1
#
# Code only, on an already-built sandbox (the three code folders are mirrored,
# Assets\Settings is copied over the top - the blur pass lives on the renderer
# assets there). WITH a graphics device: it reads pixels, so no -nographics.
#
# Exit code 0 = the report has no FAIL; 1 = it does, or the run threw.
$ErrorActionPreference = "Stop"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\Settings" "$proj\Assets\Settings" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_speedblur_play_check.txt" -ErrorAction SilentlyContinue
Get-ChildItem "$proj\Screenshots\speedblur_play_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force

# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\speedblurplay.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SpeedBlurPlayCheck.Run",
    "-logFile","$proj\speedblurplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\speedblurplay.log" -Pattern "error CS|Shader error" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_speedblur_play_check.txt") {
    Get-Content "$proj\PSXRacing_speedblur_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_speedblur_play_check.txt" -Pattern "FAIL" -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of speedblurplay.log:"
Get-Content "$proj\speedblurplay.log" -Tail 40
exit 1
