# The POLE SHOTS (Charlotte WP-15): driver's-eye frames by day and at night at
# the plan's shot roads - two arterials (Albemarle Rd, Central Ave), two
# two-lane streets (Rocky River Rd, Brentwood Pl), uptown (E Trade St,
# N Tryon St) and Queens Rd in Myers Park - from the same cameras every run
# (Editor/CityRefSpots.cs, RunPoles). Run it on the tree before the package
# with -Label before and after it with -Label after; -Sheet then puts the
# pairs side by side.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXShip'
#   powershell -ExecutionPolicy Bypass -File tools\city-pole-shots.ps1 -Label before
#   powershell -ExecutionPolicy Bypass -File tools\city-pole-shots.ps1 -Label after -Sheet
#
# -WireSigns: a second job (CityRefSpots.RunWireSigns, the WP-15 review): the
# first business signs along the Tryon Sprint a wire ran through when the signs
# did not keep off the poles, shot from the driver's lane before and after
# (wiresign_<n>_{before,after}.png, wiresign.txt): one job, the same cameras.
#
# -Spots a15_albemarle,a1_tryon: only those cameras (PSX_POLE_SPOTS). Every
# spot's log line checks the poles and lamp posts within 120 m stand clear of
# the lanes and driveways (CityRefSpots.SpotFeet): CLEAR or NOT CLEAR.
#
# About 4 minutes: code + art into the warm sandbox, one Unity job, no scene build.
param([string]$Label = "now", [string]$Out = "", [switch]$Sheet, [switch]$WireSigns, [string]$Spots = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Out) { $Out = Join-Path $proj "Screenshots\City\poles_out" }

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources", "Assets\PSXRacing\Materials")) {
    # /XO: Resources holds baked output (see city-cycle.ps1)
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

$shots = Join-Path $proj "Screenshots\City\poles"
$txt = Join-Path $shots "poles_shots_$Label.txt"
if (Test-Path $txt) { Remove-Item $txt -Force }
$log = "$proj\citypoleshots.log"
$env:PSX_POLE_LABEL = $Label
$env:PSX_POLE_SPOTS = $Spots
$ok = Invoke-UnityJob -Log $log -MaxMinutes 25 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityRefSpots.RunPoles",
    "-logFile",$log,"-accept-apiupdate")
Select-String -Path $log -Pattern "error CS|Exception|\[CityRefSpots\]|\[CityPoles\]" -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { $_.Line }
if (-not $ok -or -not (Test-Path $txt)) {
    Write-Host "POLE SHOTS FAILED - the job did not finish or wrote nothing; log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    exit 1
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item "$shots\*_$Label*" $Out -Force
# the feet at each spot, clear of the lanes and driveways (CityRefSpots.SpotFeet)
$feetBad = @(Select-String -Path $txt -Pattern "-> NOT CLEAR" -CaseSensitive).Count
if ($feetBad -gt 0) { Write-Host "POLE SHOTS: feet NOT CLEAR at $feetBad spot(s) - see $txt" -ForegroundColor Red }
if ($WireSigns) {
    $wtxt = Join-Path $shots "wiresign.txt"
    if (Test-Path $wtxt) { Remove-Item $wtxt -Force }
    $wlog = "$proj\citywiresigns.log"
    $ok = Invoke-UnityJob -Log $wlog -MaxMinutes 30 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CityRefSpots.RunWireSigns",
        "-logFile",$wlog,"-accept-apiupdate")
    Select-String -Path $wlog -Pattern "error CS|Exception|\[CityRefSpots\]" -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { $_.Line }
    if (-not $ok -or -not (Test-Path $wtxt)) {
        Write-Host "WIRE-SIGN SHOTS FAILED - the job did not finish or wrote nothing" -ForegroundColor Red
        exit 1
    }
    Copy-Item "$shots\wiresign*" $Out -Force
}
if ($Sheet) {
    & py "$src\tools\city\polesheet.py" $Out
    if ($LASTEXITCODE -ne 0) { Write-Host "POLE SHOTS: the sheet failed" -ForegroundColor Red; exit 1 }
}
Write-Host "POLE SHOTS DONE ($Label) -> $Out"
if ($feetBad -gt 0) { exit 1 }
exit 0
