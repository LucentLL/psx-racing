# Mirror, build the scenes, run the self-test, then PHOTOGRAPH the town and the
# seller's street.
#
#   powershell -ExecutionPolicy Bypass -File tools\town-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\town-check.ps1 -SkipBuild
#
# The probe stage must NOT pass -nographics: it renders into a RenderTexture and
# a null device reads back as a black PNG, which is indistinguishable from a
# scene that failed to build.
param([switch]$SkipBuild)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"

$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot

if (-not $SkipBuild) {
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    Invoke-UnityJob -Log "$proj\bake.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
        "-logFile","$proj\bake.log","-accept-apiupdate") | Out-Null
    if (-not (Invoke-SceneBuild -Proj $proj)) { exit 1 }
}

# Delete the marker first. An -executeMethod that throws never writes its file,
# and the check below would then read the PREVIOUS run's result and call a
# crashed pass a success.
Remove-Item "$proj\PSXRacing_selftest_log.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\selftest.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate") | Out-Null

$cs = Select-String -Path "$proj\selftest.log" -Pattern "error CS" | Select-Object -First 20
if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }

Write-Host "=== SELF-TEST ==="
if (Test-Path "$proj\PSXRacing_selftest_log.txt") {
    Get-Content "$proj\PSXRacing_selftest_log.txt" | Where-Object { $_ -match "FAIL|SELF-TEST" }
} else { Write-Host "  self-test wrote nothing - it threw"; Get-Content "$proj\selftest.log" -Tail 25 }

Remove-Item "$proj\PSXRacing_townprobe.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\townprobe.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TownProbe.Run",
    "-logFile","$proj\townprobe.log","-accept-apiupdate") | Out-Null

Write-Host "=== TOWN PROBE ==="
if (Test-Path "$proj\PSXRacing_townprobe.txt") { Get-Content "$proj\PSXRacing_townprobe.txt" }
else { Write-Host "  probe wrote nothing - it threw"; Get-Content "$proj\townprobe.log" -Tail 30 }

exit 0
