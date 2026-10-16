# The car's sun shadow, photographed in a real race start (Editor\CarShadowShots.cs):
# the owner's Linn Cove frame (red Viper, chase cam, the field gone) at each hour,
# plus the grid with the rivals and a side view at the first hour.
#
#   powershell -ExecutionPolicy Bypass -File tools\carshadow-shots.ps1 -Prefix before
#   ... -Prefix after -Hours noon,sunset -Out C:\some\dir
#
# Scripts, Editor and Shaders are copied into the sandbox first (no mirror, no
# scene build). Runs WATCHED (a visible editor): the shots read the game camera's
# render target, which needs a device.
param([string]$Prefix = "shot", [string]$Hours = "noon,sunset", [string]$Venue = "BlueRidge",
      [string]$Car = "dodge_viper_gts__99", [string]$Out = "", [switch]$NoWatch, [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Out) { $Out = Join-Path $src "tools\shots\carshadow" }

# -NoCopy: shoot whatever the sandbox has (a BEFORE run against main's code,
# with only the harness copied in by hand).
if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
}
Remove-Item "$proj\PSXRacing_carshadow_shots.txt" -ErrorAction SilentlyContinue
$env:PSX_CS_PREFIX = $Prefix
$env:PSX_CS_HOURS = $Hours
$env:PSX_CS_VENUE = $Venue
$env:PSX_CS_CAR = $Car
$env:PSX_CS_OUT = $Out
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\carshadow.log" -MaxMinutes 25 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarShadowShots.Run",
    "-logFile","$proj\carshadow.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\carshadow.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_carshadow_shots.txt") { Get-Content "$proj\PSXRacing_carshadow_shots.txt"; exit 0 }
"NO REPORT. Tail of carshadow.log:"
Get-Content "$proj\carshadow.log" -Tail 30
exit 1
