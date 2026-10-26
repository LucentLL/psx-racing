# Photograph the CONTROLS tab and the four WHEEL SETUP pages (and the pause
# screen / GAMEPLAY, whose strips now hold five pages) at the five pause sizes,
# with the clipped-text and pad-reach checks. No scene, no bake:
# DebugBenchPreview.CaptureControls builds the menus on an empty stage.
#
#   powershell -ExecutionPolicy Bypass -File tools\wheel-preview.ps1
#
# Output: <sandbox>\Screenshots\controls\*.png. Exit 1 on any CLIP / OVERLAP /
# unreachable / off-canvas line.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\Plugins")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# The preview needs a graphics device (no -nographics): it renders to a RenderTexture.
$ok = Invoke-UnityJob -Log "$proj\wheelpreview.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.DebugBenchPreview.CaptureControls",
    "-logFile","$proj\wheelpreview.log","-accept-apiupdate")
if (-not $ok) { exit 1 }
$cs = Select-String -Path "$proj\wheelpreview.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }
$bad = Select-String -Path "$proj\wheelpreview.log" -Pattern "CLIP|OVERLAP|UNREACHABLE BY PAD|TEXT OVERFLOWS|OFF THE CANVAS|NO CONTROLS|Exception"
$shots = @(Get-ChildItem "$proj\Screenshots\controls\*.png" -ErrorAction SilentlyContinue)
"=== " + $shots.Count + " pictures in $proj\Screenshots\controls ==="
if (-not (Select-String -Path "$proj\wheelpreview.log" -Pattern "\[BenchPreview\] done" -Quiet)) {
    "PREVIEW DID NOT FINISH - tail of the log:"; Get-Content "$proj\wheelpreview.log" -Tail 30; exit 1
}
if ($bad) { "=== FAILURES (" + @($bad).Count + ") ==="; $bad | Select-Object -First 40 | ForEach-Object { $_.Line }; exit 1 }
"WHEEL PREVIEW OK (CLIP/OVERLAP 0)"
exit 0
