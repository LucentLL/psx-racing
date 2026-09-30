# The Charlotte WATER SHOTS (WP-25, 2026-09-30): the three creeks (W Trade St
# over Irwin Creek, State St over Stewart Creek, Archdale Dr over Little Sugar
# Creek) from the bridge, the bank and above; one culvert from its channel and
# from the road; one pond from the road. The cameras depend only on the city
# data and the ground (Editor/CityHydroShots.cs), so a -Label before run and a
# -Label after run line up frame for frame.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXScenery'
#   powershell -ExecutionPolicy Bypass -File tools\city-hydro-shots.ps1 -Label after
#
# PNGs and hydro_shots.txt land in <sandbox>\Screenshots\City\hydro\<label>.
# A few minutes: code + data into the warm sandbox, one Unity job, no scene build.
param([string]$Label = "now", [switch]$NoSync)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if (-not $NoSync) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources", "Assets\PSXRacing\Materials")) {
        # /XO: Resources holds baked output (see city-refspots.ps1)
        robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}

$out = Join-Path $proj "Screenshots\City\hydro\$Label"
if (Test-Path "$out\hydro_shots.txt") { Remove-Item "$out\hydro_shots.txt" -Force }
$log = "$proj\cityhydroshots.log"
if (Test-Path $log) { Remove-Item $log -Force }
$env:PSX_HYDRO_LABEL = $Label
$ok = Invoke-UnityJob -Log $log -MaxMinutes 30 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityHydroShots.Run",
    "-logFile",$log,"-accept-apiupdate")
Select-String -Path $log -Pattern "error CS|Exception|\[CityHydroShots\]" -ErrorAction SilentlyContinue | Select-Object -First 40 | ForEach-Object { $_.Line }
if (-not $ok -or -not (Test-Path "$out\hydro_shots.txt")) {
    Write-Host "HYDRO SHOTS FAILED - the job did not finish or wrote nothing; log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    exit 1
}
Get-ChildItem "$out\*.png" | ForEach-Object { Write-Host "  $($_.FullName)" }
Write-Host "HYDRO SHOTS DONE -> $out"
exit 0
