# The owner's authored tail lamps (Resources/CarLights/<modelKey>_lightmask_rg.png),
# photographed in a real race on the R32 (Editor\R32LightShots.cs): a rear view at
# NIGHT off / running / braking / brake-only, at DUSK running / braking, and one
# chase-cam frame of the R32 braking at speed at night. Also runs CarLampMapCheck
# and the "a car without a map is untouched" checks.
#
#   powershell -ExecutionPolicy Bypass -File tools\r32lights-shots.ps1
#   ... -Venue GillespieGap -Hours NIGHT -Out C:\some\dir
#
# -Mode lens (2026-10-16, the owner's lens test): every view as an A/B pair (A
# opaque lamps, B the shell's lens variant with clear covers) - NIGHT rear
# running/braking + front headlamps, a day rear three-quarter - composed side by
# side (tools\r32lenses-pairs.py) into -Out; the raw frames stay in -Raw. The
# sandbox must hold the baked lens variant (CarModelBaker.BakeMenu).
#   ... -Mode lens -Hours NIGHT,NOON -Out tools\shots\r32lenses
#
# Scripts, Editor, Shaders and Resources\CarLights are copied into the sandbox
# (no mirror, no scene build: the venue must be built there). -BeforeRef: the
# commit whose PSX/CarPaint is written into the sandbox as
# Hidden/PSXCarPaintBefore for the bit-identity check (removed afterwards;
# empty = skip it). Runs WATCHED (a visible editor): the shots need a device.
param([string]$Venue = "BlueRidge", [string]$Hours = "NIGHT,DUSK",
      [string]$Car = "nissan_skyline_gt_r__r32___89", [string]$Out = "",
      [string]$BeforeRef = "0588a19c", [string]$Mode = "", [string]$Raw = "",
      [switch]$NoWatch, [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Out) { $Out = Join-Path $src "tools\shots\r32lights" }

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    }
    # The lamp maps MIRRORED: a renamed map (_lightmask_rg -> _lightmask) must
    # not leave its old file behind as a second map for the same model.
    robocopy "$src\Assets\PSXRacing\Resources\CarLights" "$proj\Assets\PSXRacing\Resources\CarLights" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    Copy-Item "$src\Assets\PSXRacing\Resources\CarLights.meta" "$proj\Assets\PSXRacing\Resources\CarLights.meta" -Force
}
$before = "$proj\Assets\PSXRacing\Shaders\PSXCarPaintBefore.shader"
if ($BeforeRef) {
    $tmp = Join-Path $env:TEMP "psx_carpaint_before.shader"
    cmd /c "git -C `"$src`" show $($BeforeRef):Assets/PSXRacing/Shaders/PSXCarPaint.shader > `"$tmp`""
    $text = [IO.File]::ReadAllText($tmp, (New-Object Text.UTF8Encoding $false))
    $text = $text.Replace('Shader "PSX/CarPaint"', 'Shader "Hidden/PSXCarPaintBefore"')
    [IO.File]::WriteAllText($before, $text, (New-Object Text.UTF8Encoding $false))
}
Remove-Item "$proj\PSXRacing_r32lights_shots.txt" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Out | Out-Null
$shotDir = $Out
if ($Mode -eq "lens") {
    if (-not $Raw) { $Raw = Join-Path $env:TEMP "psx_r32lenses_raw" }
    New-Item -ItemType Directory -Force $Raw | Out-Null
    $shotDir = $Raw
}
$env:PSX_RL_MODE = $Mode
$env:PSX_RL_VENUE = $Venue
$env:PSX_RL_HOURS = $Hours
$env:PSX_RL_CAR = $Car
$env:PSX_RL_OUT = $shotDir
try {
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\r32lights.log" -MaxMinutes 30 -UnityArgs @(
        "-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.R32LightShots.Run",
        "-logFile","$proj\r32lights.log","-accept-apiupdate") | Out-Null
} finally {
    # The before-shader is a test fixture: never left in the sandbox.
    Remove-Item $before, "$before.meta" -ErrorAction SilentlyContinue
}
Select-String -Path "$proj\r32lights.log" -Pattern "error CS|Exception" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_r32lights_shots.txt") {
    Get-Content "$proj\PSXRacing_r32lights_shots.txt"
    if ($Mode -eq "lens") { py "$PSScriptRoot\r32lenses-pairs.py" $Raw $Out }
    exit 0
}
"NO REPORT. Tail of r32lights.log:"
Get-Content "$proj\r32lights.log" -Tail 30
exit 1
