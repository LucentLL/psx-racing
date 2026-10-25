# THE DRAWN PROFILE'S CENSUS (Editor\CityVSmoothCensus.cs; the presentation
# pass is Scripts\City\CityElevation.VSmooth.cs). One Unity job: the city
# solved once, its ribbon built and read off the mesh twice - the stations
# straight (BEFORE) and the drawn profile (AFTER) - grade breaks by class,
# the race routes' vertical acceleration at 150 mph and at the posted speed,
# what the knots cost, and the residuals with their reasons.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\city-vsmooth.ps1            every road tile
#   ...  -Routes      only the tiles within 300 m of a race route (faster)
#   ...  -Launch      and the launch audit after, once each way
#                     (city_launch_before.txt / city_launch_after.txt)
#   ...  -NoMirror    the sandbox as it stands
#
# Writes (sandbox root, then -OutDir): vsmooth_census.txt, vsmooth_residuals.csv.
param([switch]$Routes, [switch]$Launch, [switch]$NoMirror, [string]$OutDir = "", [int]$MaxMinutes = 60)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
Write-Host ("drawn profile census: sandbox {0}, source {1}{2}{3}" -f $proj, $src, $(if ($Routes) { ", route tiles" } else { ", every road tile" }), $(if ($Launch) { ", + launch audit both ways" } else { "" })) -ForegroundColor Cyan

if (-not $NoMirror) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
        robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}
if ($Routes) { $env:PSX_VS_TILES = "routes" } else { Remove-Item Env:\PSX_VS_TILES -ErrorAction SilentlyContinue }
if ($Launch) { $env:PSX_VS_LAUNCH = "1" } else { Remove-Item Env:\PSX_VS_LAUNCH -ErrorAction SilentlyContinue }
Remove-Item Env:\PSX_LAUNCH_BOX -ErrorAction SilentlyContinue
$outs = @("vsmooth_census.txt", "vsmooth_residuals.csv", "city_launch_before.txt", "city_launch_after.txt", "city_launch_before.json", "city_launch_after.json", "city_launch_before.csv", "city_launch_after.csv")
foreach ($f in $outs) { Remove-Item (Join-Path $proj $f) -Force -ErrorAction SilentlyContinue }
$log = "$proj\vsmooth.log"
$t0 = Get-Date
$ok = @(Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityVSmoothCensus.Run",
    "-logFile",$log,"-accept-apiupdate")) | Select-Object -Last 1
$min = ((Get-Date) - $t0).TotalMinutes
$failed = $false
if (-not $ok) { Write-Host ("job did not finish in {0} minutes (left running, never killed)" -f $MaxMinutes) -ForegroundColor Red; $failed = $true }
$cs = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 10
if ($cs) { $cs | ForEach-Object { $_.Line }; $failed = $true }
$report = Join-Path $proj "vsmooth_census.txt"
if (Test-Path $report) {
    if (Select-String -Path $report -SimpleMatch "THREW" -Quiet) { $failed = $true }
} else {
    Write-Host "no vsmooth_census.txt written - log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
if ($OutDir) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    foreach ($f in $outs) { $p = Join-Path $proj $f; if (Test-Path $p) { Copy-Item $p $OutDir -Force } }
    Write-Host "reports copied to $OutDir"
}
Write-Host ("VSMOOTH CENSUS DONE in {0:0.0} min{1}" -f $min, $(if ($failed) { " - FAILED" } else { "" })) -ForegroundColor $(if ($failed) { "Red" } else { "Green" })
if ($failed) { exit 1 }
exit 0
