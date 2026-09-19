# Photograph the DRIVING HUD a phone player sees - touch panel and both dials
# on one frame - at three aspects, and CHECK the placement in pixels: the pedal
# column as far from the right edge as the wheel is from the left, each dial
# clear of its control, and the middle of the frame clear.
#
#   powershell -ExecutionPolicy Bypass -File tools\drivehud-preview.ps1
#
# Code only: copies Scripts, Editor and Shaders into an already-built sandbox (/E, so
# the scenes survive). Needs a graphics device - no -nographics.
#
# (The speed-streak overlay this also used to check is gone; the blur that
# replaced it has its own instrument, tools\speedblur-preview.ps1.)
#
# Output: C:\Users\mcgee\PSXBuild\Screenshots\drivehud_*.png. Exit 1 on any
# failed check.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

# Shaders too: a stale copy of a shader in the sandbox is exactly how a bug
# passes here and fails on the phone.
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Stale pictures certify a run that never happened as well as fresh ones do.
Get-ChildItem "$proj\Screenshots\drivehud_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force

$ok = Invoke-UnityJob -Log "$proj\drivehud.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.DriveHudPreview.Capture",
    "-logFile","$proj\drivehud.log","-accept-apiupdate")
if (-not $ok) { exit 1 }

$cs = Select-String -Path "$proj\drivehud.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) {
    Write-Host "=== COMPILE ERRORS ==="
    $cs | ForEach-Object { $_.Line }
    exit 1
}

Write-Host "=== DRIVING HUD ==="
Select-String -Path "$proj\drivehud.log" -Pattern "^\[DriveHud\]" |
    Where-Object { $_.Line -notmatch "wrote " } | ForEach-Object { $_.Line }

if (-not (Select-String -Path "$proj\drivehud.log" -Pattern "\[DriveHud\] done" -Quiet)) {
    Write-Host "PREVIEW DID NOT FINISH - tail of the log:"
    Get-Content "$proj\drivehud.log" -Tail 30
    exit 1
}
$bad = Select-String -Path "$proj\drivehud.log" -Pattern "\[DriveHud\].* FAIL|Exception"
if ($bad) { exit 1 }
Write-Host "DRIVING HUD PREVIEW OK"
exit 0
