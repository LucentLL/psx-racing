# The DYNAMIC sky round the compass (Editor/DynamicSkyShots): per set (hour +
# sky) sunward / side / away / up, OLD clouds then VOLUMETRIC, after the GLES3
# (WebGL 2) compile of the cloud shaders. Syncs Scripts, Editor and Shaders
# like sky-shots.ps1 (a sky shader edit has to reach the sandbox) and the
# clouds' noise (Resources\Clouds). Sheets: py tools\sky\cloud_sheets.py
#
#   powershell -ExecutionPolicy Bypass -File tools\dynsky-shots.ps1 [-Venue EmeraldIsle] [-Sets "noon:partly,night:partly"] [-Modes "old,new"]
param([string]$Venue = "EmeraldIsle", [string]$Sets = "", [string]$Modes = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}
robocopy "$src\Assets\PSXRacing\Resources\Clouds" "$proj\Assets\PSXRacing\Resources\Clouds" /E /XO /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
Copy-Item "$src\Assets\PSXRacing\Resources\Clouds.meta" "$proj\Assets\PSXRacing\Resources\Clouds.meta" -Force
Remove-Item "$proj\Screenshots\dsky_*" -ErrorAction SilentlyContinue
$env:PSX_SKY_VENUE = $Venue
if ($Sets) { $env:PSX_DSKY_SETS = $Sets }
if ($Modes) { $env:PSX_DSKY_MODES = $Modes }
for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\dynsky.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.DynamicSkyShots.Capture",
        "-logFile","$proj\dynsky.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\dynsky.log" -Pattern "Screenshots written to|error CS" -Quiet) { break }
    Write-Host "capture attempt $i did not finish, retrying..."
}
Select-String -Path "$proj\dynsky.log" -Pattern "\[VolClouds\]|\[DynSky\]|error CS|Shader error" | Select-Object -First 40 | ForEach-Object { $_.Line }
if (Select-String -Path "$proj\dynsky.log" -Pattern "Screenshots written to" -Quiet) {
    Write-Host "DYNSKY SHOTS OK -> $proj\Screenshots\dsky_*"
} else {
    Write-Host "CAPTURE FAILED"
    Get-Content "$proj\dynsky.log" -Tail 15
}
