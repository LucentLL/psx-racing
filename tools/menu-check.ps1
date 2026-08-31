# Mirror to the sandbox, build the scenes and the car meshes, run the LifeSim
# self-test, then render every menu screen to PNG at three aspect ratios.
#
#   powershell -ExecutionPolicy Bypass -File tools\menu-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\menu-check.ps1 -SkipBuild
#
# The three stages answer different questions and none substitutes for another:
# the scene build says it COMPILES and bakes, the self-test says the RULES are
# right, the preview says the LAYOUT is.
#
# The scene build is not optional if you intend to read the self-test result.
# The mirror is a /MIR, so it deletes the built scenes and the baked car meshes
# every run -- and a third of the self-test asserts things about those. Skip it
# and nine checks fail for want of a scene, which reads exactly like nine checks
# failing because of the change you just made. -SkipBuild is for the loop where
# you have already paid for the build and are only iterating on the menu.
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

if (-not $SkipBuild) {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }

    # The car meshes are build output and the mirror above just deleted them.
    # The garage and market shots render a turntable; without this they render
    # nothing, and TestCarModels has nothing to assert against.
    Invoke-UnityJob -Log "$proj\bake.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
        "-logFile","$proj\bake.log","-accept-apiupdate") | Out-Null

    if (-not (Invoke-SceneBuild -Proj $proj)) { exit 1 }
}

Invoke-UnityJob -Log "$proj\selftest.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\selftest.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) {
    Write-Host "=== COMPILE ERRORS ==="
    $cs | ForEach-Object { $_.Line }
    exit 1
}

Write-Host "=== SELF-TEST ==="
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Get-Content "$proj\PSXRacing_selftest_log.txt" |
        Where-Object { $_ -match "FAIL|SELF-TEST" }
}

# The preview needs a graphics device: it renders into a RenderTexture, and
# -nographics gives it a null one that reads back as a black PNG.
Invoke-UnityJob -Log "$proj\menupreview.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeHomePreview.Capture",
    "-logFile","$proj\menupreview.log","-accept-apiupdate") | Out-Null

Write-Host "=== MUST FIT ON ONE SCREEN ==="
$bad = Select-String -Path "$proj\menupreview.log" -Pattern "MUST FIT"
if ($bad) { $bad | ForEach-Object { $_.Line } } else { Write-Host "  all clear" }

Write-Host "=== LAYOUT (content height vs viewport) ==="
Select-String -Path "$proj\menupreview.log" -Pattern "\[HomePreview\] (home|options)" |
    ForEach-Object { $_.Line }

# Say so explicitly. robocopy returns 1 for "files were copied" -- success -- and
# that is the last $LASTEXITCODE the mirror leaves lying around, so without this
# every clean run of this script reported failure to whatever launched it. Every
# real failure above exits 1 on its own.
exit 0
