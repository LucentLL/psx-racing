# Every tree in the game: an X from every side, on a trunk that stops a car.
#
#   powershell -ExecutionPolicy Bypass -File tools\tree-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\tree-check.ps1 -Build
#
# Three instruments, on an already-built sandbox (the three code folders are
# mirrored; -Build runs the scene build first, which is what a BUILDER change
# needs - the trunk tables and the two-sided materials are baked):
#
#   FoliageAudit  every scene: each tree two crossing cards, drawn from both
#                 sides (and in all five season dresses), standing on a trunk
#                 collider, with no collider across its crown. FOLIAGE OK.
#   TreeCrashSim  a car into a tree at 100 km/h, stage and circuit kinds, plus
#                 the control with no tree. Graded by TreeCrashSim.Passed.
#   TreePreview   one tree of each kind from eight compass points, drawn as
#                 shipped (backs culled) and as fixed, the collider a red post;
#                 composed into Screenshots\Trees\trees_sheet.png.
#
# Exit 0 = FOLIAGE OK and every crash case passed; 1 otherwise.
param([switch]$Build)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

if ($Build) {
    Write-Host "[0/3] Scene build..." -ForegroundColor Cyan
    if (-not (Invoke-SceneBuild -Proj $proj)) { Write-Host "SCENE BUILD FAILED" -ForegroundColor Red; exit 1 }
}

$bad = 0

Write-Host "[1/3] Foliage audit..." -ForegroundColor Cyan
Remove-Item "$proj\PSXRacing_foliage_audit.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\foliage.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.FoliageAudit.Run",
    "-logFile","$proj\foliage.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_foliage_audit.txt") {
    Select-String -Path "$proj\PSXRacing_foliage_audit.txt" -Pattern "FAIL|MISSING|FOLIAGE" | ForEach-Object { $_.Line }
    if (-not (Select-String -Path "$proj\PSXRacing_foliage_audit.txt" -Pattern "FOLIAGE OK" -Quiet)) { $bad++ }
} else {
    Write-Host "FOLIAGE AUDIT WROTE NOTHING - see $proj\foliage.log" -ForegroundColor Red
    Select-String -Path "$proj\foliage.log" -Pattern "error CS|Exception" | Select-Object -First 6 | ForEach-Object { $_.Line }
    $bad++
}

Write-Host "[2/3] Tree crash sim..." -ForegroundColor Cyan
Remove-Item "$proj\PSXRacing_treecrash.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\treecrash.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TreeCrashSim.Shoot",
    "-logFile","$proj\treecrash.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\PSXRacing_treecrash.txt") {
    Get-Content "$proj\PSXRacing_treecrash.txt"
    if (Select-String -Path "$proj\PSXRacing_treecrash.txt" -Pattern "FAIL" -Quiet) { $bad++ }
} else {
    Write-Host "CRASH SIM WROTE NOTHING - see $proj\treecrash.log" -ForegroundColor Red
    $bad++
}

# Graphics ON: it renders.
Write-Host "[3/3] Turntable..." -ForegroundColor Cyan
Remove-Item "$proj\Screenshots\Trees\trees.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\treepreview.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TreePreview.Run",
    "-logFile","$proj\treepreview.log","-accept-apiupdate") | Out-Null
if (Test-Path "$proj\Screenshots\Trees\trees.txt") {
    Get-Content "$proj\Screenshots\Trees\trees.txt"
    & py "$PSScriptRoot\trees\tree_sheet.py" "$proj\Screenshots\Trees" "$proj\Screenshots\Trees\trees_sheet.png"
} else {
    Write-Host "TURNTABLE WROTE NOTHING - see $proj\treepreview.log" -ForegroundColor Red
    $bad++
}

if ($bad -gt 0) { Write-Host "TREE CHECK FAILED ($bad)" -ForegroundColor Red; exit 1 }
Write-Host "TREE CHECK OK" -ForegroundColor Green
exit 0
