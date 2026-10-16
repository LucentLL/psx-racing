# The car-reflection shots (ReflectionShots, 2026-10-12; extended 2026-10-15
# for the GT4 retune) on an already-built sandbox: Scripts, Editor, Shaders and
# the always-included shader list are copied over first, then
# PSXRacing.EditorTools.ReflectionShots.Run renders through the pipeline
# (graphics ON). Writes Screenshots\refl_<venue>_<hour>_<view>_off|on.png and
# refl_<venue>_<hour>_pano.png.
#
#   powershell -ExecutionPolicy Bypass -File tools\refl-shots.ps1
#   ... -Venues BlueRidge -Hours noon,sunset -Views chase,rear34c -Car viper_gts -Wp auto
#   ... -Debug 4,6     the paint's debug terms too (_PSXPaintDebug)
#   ... -Time          log the refresh cost per face size ([ReflShot] TIME)
#   ... -Tag before -Out tools\shots\carreflect   copy the frames out as before_*.png
param([string]$Venues = "BlueRidge", [string]$Hours = "noon", [string]$Views = "chase,rear34c",
      [string]$Car = "viper_gts", [int]$Skin = 0, [string]$Wp = "auto", [string]$Debug = "",
      [switch]$Time, [string]$Tag = "", [string]$Out = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
Remove-Item "$proj\Screenshots\refl_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\reflshots.log" -ErrorAction SilentlyContinue

$env:PSX_REFL_VENUES = $Venues
$env:PSX_REFL_HOUR = $Hours
$env:PSX_REFL_VIEWS = $Views
$env:PSX_REFL_CAR = $Car
$env:PSX_REFL_SKIN = "$Skin"
$env:PSX_REFL_WP = $Wp
$env:PSX_REFL_DEBUG = $Debug
$env:PSX_REFL_TIME = $(if ($Time) { "1" } else { "" })

Invoke-UnityJob -Log "$proj\reflshots.log" -MaxMinutes 25 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ReflectionShots.Run",
    "-logFile","$proj\reflshots.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\reflshots.log" -Pattern "ReflShot|error CS|Shader error|Exception" |
    Select-Object -First 40 | ForEach-Object { $_.Line }
$shots = Get-ChildItem "$proj\Screenshots" -Filter "refl_*.png"
$shots | ForEach-Object { $_.Name }
if ($Tag -and $Out) {
    $dest = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $src $Out }
    New-Item -ItemType Directory -Force $dest | Out-Null
    foreach ($f in $shots) {
        Copy-Item $f.FullName (Join-Path $dest ($Tag + "_" + ($f.Name -replace '^refl_', ''))) -Force
    }
    Write-Host "copied $($shots.Count) frames to $dest as ${Tag}_*"
}
