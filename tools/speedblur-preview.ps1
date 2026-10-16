# Photograph and MEASURE the speed blur on the first circuit, through both
# render pipelines (Mobile is what WebGL ships, PC is what the editor previews
# with): the middle of the frame bit-for-bit untouched, the edge smeared and
# not darkened, the HUD's footprint unchanged - plus a control frame with the
# HUD left on the world camera, which has to FAIL that last measurement.
#
# Then SPEED BLUR: MOTION (2026-10-13): the car and the HUD untouched, the sky
# sharp on a straight while the near roadside smears, a turn smearing the sides
# sideways, a camera cut drawn sharp, and the zoom unchanged after it - and the
# owner's four pictures (Blue Ridge at 100 mph, chase and cockpit, ZOOM and
# MOTION, a rival alongside), copied to tools\shots\motionblur\, and the smear
# table: MOTION's near-roadside / far / rival-car smear at 40-140 mph (2026-10-16).
#
#   powershell -ExecutionPolicy Bypass -File tools\speedblur-preview.ps1
#   ... -Baseline <dir>   compare every ZOOM picture byte for byte with the
#                         same-named PNGs a previous run left in <dir> (the
#                         zoom must be bit-identical across a change that is
#                         not about the zoom). The *_0_off pictures are the
#                         control: if THEY differ, the scene is not still.
#   ... -NoMotion         the zoom alone (PSX_SPEEDBLUR_MOTION=0)
#
# Code only, on an already-built sandbox: the three code folders are COPIED
# (/E, never /MIR: the sandbox may hold files another job generated there),
# and Assets\Settings is copied over the top because the blur pass lives on
# the two URP renderer assets there. Needs a graphics device - no -nographics.
#
# Output: C:\Users\mcgee\PSXBuild\Screenshots\speedblur_*.png. Exit 1 on any
# failed check.
param([string]$Baseline = "", [switch]$NoMotion)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\Settings" "$proj\Assets\Settings" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null

# Stale pictures certify a run that never happened as well as fresh ones do.
Get-ChildItem "$proj\Screenshots\speedblur_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path "$proj\speedblur.log") { Remove-Item "$proj\speedblur.log" -Force }

$env:PSX_SPEEDBLUR_MOTION = if ($NoMotion) { "0" } else { "" }
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
$bad = @(Select-String -Path "$proj\speedblur.log" -Pattern "\[SpeedBlur\].* FAIL|Exception")

# The zoom, byte for byte against a previous run's pictures.
if ($Baseline) {
    $same = 0; $diff = @(); $missing = 0
    foreach ($f in Get-ChildItem "$Baseline\speedblur_*.png") {
        $now = Join-Path "$proj\Screenshots" $f.Name
        if (-not (Test-Path $now)) { $missing++; continue }
        if ((Get-FileHash $f.FullName).Hash -eq (Get-FileHash $now).Hash) { $same++ } else { $diff += $f.Name }
    }
    Write-Host ("=== ZOOM vs BASELINE: {0} identical, {1} different, {2} missing ===" -f $same, $diff.Count, $missing)
    # A picture that differs while its own set's blur-OFF picture differs too
    # says the scene was not still (the HUD map's markers, the night lamps),
    # not that the zoom changed. Only a difference with an identical control
    # is a failure.
    $real = @(); $still = 0
    foreach ($n in $diff) {
        $ctl = if ($n -match '^(speedblur_.+?)_(\d_|control_)') { $Matches[1] + "_0_off.png" } else { "" }
        if ($ctl -and ($diff -contains $ctl)) { $still++ }
        else { Write-Host "  DIFFERS with an identical control: $n"; $real += $n }
    }
    Write-Host ("  {0} differ only where their own blur-OFF control differs too (scene not still)" -f $still)
    if ($real.Count -gt 0 -or $missing -gt 0) { $bad += "baseline" }
}

# The owner's pictures, into the source tree.
$shots = Join-Path $src "tools\shots\motionblur"
$owner = @(Get-ChildItem "$proj\Screenshots\speedblur_owner_*.png" -ErrorAction SilentlyContinue)
if ($owner.Count -gt 0) {
    New-Item -ItemType Directory -Force $shots | Out-Null
    $owner | ForEach-Object { Copy-Item $_.FullName $shots -Force; Write-Host "  owner shot: $shots\$($_.Name)" }
}

if ($bad.Count -gt 0) { exit 1 }
Write-Host "SPEED BLUR PREVIEW OK"
exit 0
