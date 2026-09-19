# Photograph and MEASURE the speed blur on the first circuit, through both
# render pipelines (Mobile is what WebGL ships, PC is what the editor previews
# with): the middle of the frame bit-for-bit untouched, the edge smeared and
# not darkened, the HUD's footprint unchanged - plus a control frame with the
# HUD left on the world camera, which has to FAIL that last measurement.
#
#   powershell -ExecutionPolicy Bypass -File tools\speedblur-preview.ps1
#
# Code only, on an already-built sandbox: the three code folders are MIRRORED
# (a deleted script must not linger and compile), and Assets\Settings is copied
# over the top because the blur pass lives on the two URP renderer assets
# there. Needs a graphics device - no -nographics.
#
# Output: C:\Users\mcgee\PSXBuild\Screenshots\speedblur_*.png. Exit 1 on any
# failed check.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\Settings" "$proj\Assets\Settings" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

# Stale pictures certify a run that never happened as well as fresh ones do.
Get-ChildItem "$proj\Screenshots\speedblur_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path "$proj\speedblur.log") { Remove-Item "$proj\speedblur.log" -Force }

$ok = Invoke-UnityJob -Log "$proj\speedblur.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.SpeedBlurPreview.Capture",
    "-logFile","$proj\speedblur.log","-accept-apiupdate")
if (-not $ok) { exit 1 }

$cs = Select-String -Path "$proj\speedblur.log" -Pattern "error CS|Shader error" | Select-Object -First 20
if ($cs) {
    Write-Host "=== COMPILE ERRORS ==="
    $cs | ForEach-Object { $_.Line }
    exit 1
}

Write-Host "=== SPEED BLUR ==="
Select-String -Path "$proj\speedblur.log" -Pattern "^\[SpeedBlur\]" | ForEach-Object { $_.Line }

if (-not (Select-String -Path "$proj\speedblur.log" -Pattern "\[SpeedBlur\] done" -Quiet)) {
    Write-Host "PREVIEW DID NOT FINISH - tail of the log:"
    Get-Content "$proj\speedblur.log" -Tail 40
    exit 1
}
$bad = Select-String -Path "$proj\speedblur.log" -Pattern "\[SpeedBlur\].* FAIL|Exception"
if ($bad) { exit 1 }
Write-Host "SPEED BLUR PREVIEW OK"
exit 0
