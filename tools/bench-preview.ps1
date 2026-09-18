# Photograph the DEBUG BENCH (both pages, plus the pause menu that opens it)
# from the sandbox WITHOUT a mirror, a bake or a scene build: copy Scripts and
# Editor in (/E, so the built scenes and baked shells survive) and run
# DebugBenchPreview.Capture.
#
#   powershell -ExecutionPolicy Bypass -File tools\bench-preview.ps1
#
# Output: C:\Users\mcgee\PSXBuild\Screenshots\bench_*.png, and the pad-reach /
# caption-overflow / on-canvas lines from the log printed at the end. Exit 1 if
# any of them failed: a bench nobody can reach with a pad, or whose captions
# run across each other, is not worth a forty-minute build to find out about.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Stale pictures certify a run that never happened just as well as fresh ones.
Get-ChildItem "$proj\Screenshots\bench_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force

# The preview needs a graphics device: it renders into a RenderTexture, and
# -nographics gives it a null one that reads back as a black PNG.
$ok = Invoke-UnityJob -Log "$proj\benchpreview.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.DebugBenchPreview.Capture",
    "-logFile","$proj\benchpreview.log","-accept-apiupdate")
if (-not $ok) { exit 1 }

$cs = Select-String -Path "$proj\benchpreview.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) {
    Write-Host "=== COMPILE ERRORS ==="
    $cs | ForEach-Object { $_.Line }
    exit 1
}

Write-Host "=== BENCH PREVIEW ==="
Select-String -Path "$proj\benchpreview.log" -Pattern "^\[BenchPreview\]" |
    Where-Object { $_.Line -notmatch "wrote " } | ForEach-Object { $_.Line }

$bad = Select-String -Path "$proj\benchpreview.log" -Pattern "UNREACHABLE BY PAD|TEXT OVERFLOWS|OFF THE CANVAS|NO BENCH BUTTON|NOT CHECKED|NO CONTROLS|NO DRAG CATCHER|Exception"
$shots = @(Get-ChildItem "$proj\Screenshots\bench_*.png" -ErrorAction SilentlyContinue)
Write-Host ("=== " + $shots.Count + " pictures in $proj\Screenshots ===")
if (-not (Select-String -Path "$proj\benchpreview.log" -Pattern "\[BenchPreview\] done" -Quiet)) {
    Write-Host "PREVIEW DID NOT FINISH - tail of the log:"
    Get-Content "$proj\benchpreview.log" -Tail 30
    exit 1
}
if ($bad) {
    Write-Host "=== FAILURES ==="
    $bad | Select-Object -First 20 | ForEach-Object { $_.Line }
    exit 1
}
Write-Host "BENCH PREVIEW OK"
exit 0
