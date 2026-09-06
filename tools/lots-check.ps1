# Build and photograph the two WALK-AROUND lots: your own garage, and a private
# seller's street.
#
#   powershell -ExecutionPolicy Bypass -File tools\lots-check.ps1
#   powershell -ExecutionPolicy Bypass -File tools\lots-check.ps1 -SkipMirror
#
# WHY THIS EXISTS, and it is the same argument nb-check.ps1 makes. Both of these
# scenes have a DRIVEWAY in them and both are scenes the player walks around at
# eye level, which is the one viewpoint from which a slab with no thickness is
# obvious. verify.ps1 builds eleven circuits and the whole of Charlotte before
# it gets to either of them; this builds two lots.
#
# It is NOT a substitute for verify.ps1 -- no self-test, no audits. Run this
# while shaping a lot; run verify before shipping.
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

# The scene FILES are the only proof the builders finished: an -executeMethod
# that throws still exits 0, and the capture below would photograph the previous
# build and call a crash a success.
$garage = "$proj\Assets\PSXRacing\Scenes\Garage.unity"
$seller = "$proj\Assets\PSXRacing\Scenes\SellerLot.unity"
Remove-Item $garage, $seller -ErrorAction SilentlyContinue

foreach ($m in @("GarageSceneBuilder", "SellerLotSceneBuilder")) {
    Invoke-UnityJob -Log "$proj\lotsbuild.log" -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.$m.Build",
        "-logFile","$proj\lotsbuild.log","-accept-apiupdate") | Out-Null
    $cs = Select-String -Path "$proj\lotsbuild.log" -Pattern "error CS" | Select-Object -First 20
    if ($cs) { Write-Host "=== COMPILE ERRORS ==="; $cs | ForEach-Object { $_.Line }; exit 1 }
    Select-String -Path "$proj\lotsbuild.log" -Pattern "\[Home\]|\[SellerLot\]" |
        ForEach-Object { "  " + ($_.Line -replace '^\[PSXBuild\] ', '') }
}

foreach ($f in @($garage, $seller)) {
    if (-not (Test-Path $f)) {
        Write-Host "$([IO.Path]::GetFileName($f)) DID NOT BUILD" -ForegroundColor Red
        Get-Content "$proj\lotsbuild.log" -Tail 40
        exit 1
    }
}

Remove-Item "$proj\PSXRacing_townprobe.txt" -ErrorAction SilentlyContinue
Invoke-UnityJob -Log "$proj\lotsshots.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.CaptureGarageOnly",
    "-logFile","$proj\lotsshots.log","-accept-apiupdate") | Out-Null
Invoke-UnityJob -Log "$proj\lotsprobe.log" -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TownProbe.Run",
    "-logFile","$proj\lotsprobe.log","-accept-apiupdate") | Out-Null

Write-Host "=== SHOTS ==="
Get-ChildItem "$proj\Screenshots\psx_garage_*.png","$proj\Screenshots\Town\seller_*.png" -ErrorAction SilentlyContinue |
    ForEach-Object { "  {0}  {1:n0} bytes  {2:HH:mm:ss}" -f $_.Name, $_.Length, $_.LastWriteTime }
exit 0
