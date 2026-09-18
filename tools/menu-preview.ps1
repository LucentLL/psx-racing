# Render every LifeSim menu screen to PNG from the sandbox WITHOUT a mirror,
# a bake or a scene build: copy Scripts and Editor in (/E, so the built scenes
# and the baked shells survive) and run LifeHomePreview.Capture.
#
#   powershell -ExecutionPolicy Bypass -File tools\menu-preview.ps1
#
# For the loop where the sandbox has already been built once today and only
# the MENU is being iterated on. menu-check.ps1 -SkipBuild runs the self-test
# as well, and does NOT copy the source in first; this does the copy and only
# the preview, which is the three-minute cycle a layout change wants.
#
# Output: C:\Users\mcgee\PSXBuild\Screenshots\menu_*.png, and the fit / pad
# reach lines from the log printed at the end.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# The preview needs a graphics device: it renders into a RenderTexture, and
# -nographics gives it a null one that reads back as a black PNG.
$ok = Invoke-UnityJob -Log "$proj\menupreview.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeHomePreview.Capture",
    "-logFile","$proj\menupreview.log","-accept-apiupdate")
if (-not $ok) { exit 1 }

$cs = Select-String -Path "$proj\menupreview.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) {
    Write-Host "=== COMPILE ERRORS ==="
    $cs | ForEach-Object { $_.Line }
    exit 1
}

Write-Host "=== MUST FIT ON ONE SCREEN ==="
$bad = Select-String -Path "$proj\menupreview.log" -Pattern "MUST FIT"
if ($bad) { $bad | ForEach-Object { $_.Line } } else { Write-Host "  all clear" }

Write-Host "=== PAD REACH ==="
$lost = Select-String -Path "$proj\menupreview.log" -Pattern "UNREACHABLE BY PAD"
if ($lost) { $lost | ForEach-Object { $_.Line } } else { Write-Host "  every control reachable" }

Write-Host "=== LAYOUT (content height vs viewport) ==="
Select-String -Path "$proj\menupreview.log" -Pattern "\[HomePreview\] (home|week|month|prerace|eat|bills|jobs|options)" |
    ForEach-Object { $_.Line }

Write-Host "=== ERRORS / EXCEPTIONS ==="
$err = Select-String -Path "$proj\menupreview.log" -Pattern "Exception|\[HomePreview\] no " | Select-Object -First 20
if ($err) { $err | ForEach-Object { $_.Line } } else { Write-Host "  none" }

exit 0
