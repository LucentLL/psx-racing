# The Charlotte CREEK SHOTS (R1, 2026-09-29): driver's-eye views down the
# roads that drop into the creek valleys the USGS 3DEP ground brought in -
# W Trade St to Irwin Creek, State St and Rozzelles Ferry Rd to Stewart
# Creek, Archdale Dr to Little Sugar Creek, the Independence Sprint to Briar
# Creek - and one wide raised view west of uptown over both western valleys
# to the towers (Editor/CityCreekShots.cs finds each crossing in the city
# data and stands 100-150 m up the higher side, inside the game's ~500 m fog).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXShip'
#   powershell -ExecutionPolicy Bypass -File tools\city-creek-shots.ps1
#
# Shots and creek_shots.txt (where each camera stood, the drop to the creek,
# the road's profile) land in <sandbox>\Screenshots\City\creeks.
# A few minutes: code + data into the warm sandbox, one Unity job, no scene build.
param([switch]$NoSync)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if (-not $NoSync) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
        # /XO: Resources holds baked output (see city-refspots.ps1)
        robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

$out = Join-Path $proj "Screenshots\City\creeks"
if (Test-Path "$out\creek_shots.txt") { Remove-Item "$out\creek_shots.txt" -Force }
$log = "$proj\citycreekshots.log"
if (Test-Path $log) { Remove-Item $log -Force }
$ok = Invoke-UnityJob -Log $log -MaxMinutes 30 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityCreekShots.Run",
    "-logFile",$log,"-accept-apiupdate")
Select-String -Path $log -Pattern "error CS|Exception|\[CityCreekShots\]" -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { $_.Line }
if (-not $ok -or -not (Test-Path "$out\creek_shots.txt")) {
    Write-Host "CREEK SHOTS FAILED - the job did not finish or wrote nothing; log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    exit 1
}
Get-ChildItem "$out\*.png" | ForEach-Object { Write-Host "  $($_.FullName)" }
Write-Host "CREEK SHOTS DONE -> $out"
exit 0
