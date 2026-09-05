# Build ONLY the player's street and photograph it.
#
#   powershell -ExecutionPolicy Bypass -File tools\nb-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\nb-check.ps1 -SkipMirror
#
# WHY THIS EXISTS. verify.ps1 rebuilds eleven circuits, four mountain stages and
# the whole of Charlotte to answer a question about one suburban road, and takes
# forty minutes doing it. The street is where every recent report has landed, and
# iterating on its geometry at forty minutes a look means guessing instead of
# measuring. This builds one scene and takes four pictures.
#
# It is NOT a substitute for verify.ps1 — no self-test, no terrain audit, no
# obstacle audit. Run this while shaping the street; run verify before shipping.
#
# The capture stage must NOT pass -nographics: it renders into a RenderTexture
# and a null device reads back as a black PNG, which is indistinguishable from a
# scene that failed to build.
param([switch]$SkipMirror)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

if (-not $SkipMirror) {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

Remove-Item "$proj\Assets\PSXRacing\Scenes\Neighborhood.unity" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\nbbuild.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.BuildNeighborhoodOnly",
    "-logFile","$proj\nbbuild.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\nbbuild.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

# The scene file is the only proof the builder finished. An -executeMethod that
# throws still exits 0 in some Unity versions, and the capture below would then
# photograph the PREVIOUS build and call a crash a success.
if (-not (Test-Path "$proj\Assets\PSXRacing\Scenes\Neighborhood.unity")) {
    Write-Host "NEIGHBOURHOOD DID NOT BUILD" -ForegroundColor Red
    Get-Content "$proj\nbbuild.log" -Tail 40
    exit 1
}
Select-String -Path "$proj\nbbuild.log" -Pattern "\[Neighborhood\]|\[Town\]" |
    ForEach-Object { "  " + ($_.Line -replace '^\[PSXBuild\] ', '') }

Invoke-UnityJob -Log "$proj\nbshots.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CaptureNeighborhood",
    "-logFile","$proj\nbshots.log","-accept-apiupdate") | Out-Null

Write-Host "=== SHOTS ==="
Get-ChildItem "$proj\Screenshots\psx_nb_*.png" -ErrorAction SilentlyContinue |
    ForEach-Object { "  {0}  {1:n0} bytes  {2:HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime }
exit 0
