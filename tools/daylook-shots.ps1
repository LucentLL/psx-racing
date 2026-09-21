# The day look, photographed and measured (2026-09-21, the Forza daylight pass).
#
#   powershell -ExecutionPolicy Bypass -File tools\daylook-shots.ps1
#   powershell -ExecutionPolicy Bypass -File tools\daylook-shots.ps1 -Only circuit,tunnel
#
# Code, shaders and the always-included shader list go into the sandbox; then
# ONE Unity job WITH graphics runs DayLookShots.Capture. It shoots every frame
# TWICE - with the sun's shadow map and with it switched off - on the circuit,
# four Charlotte streets, a mountain stage and a tunnel, at morning, noon and
# afternoon (sunset and an overcast noon on the circuit), and
# tools\day\day_stats.py measures each pair against the numbers read off the
# owner's Forza frames: how much of the picture is in shadow, the sun-to-shade
# ratio there, and whether the shade is bluer than the sun.
#
# -Only takes any of circuit,city,stage,tunnel (comma list; PSX_DAY_ONLY).
#
# Needs an already-BUILT sandbox (the scenes): nothing here is baked, the whole
# pass is runtime, so no scene build is wanted or run.
#
# Frames: C:\Users\mcgee\PSXBuild\Screenshots\dl_<venue>_<spot>_<hour>_{shadow,flat}.png
# Log:    C:\Users\mcgee\PSXBuild\Screenshots\dl_log.txt
#
# Exits 1 (and says DAY LOOK VERIFY FAILED) on a Unity job that did not finish,
# a capture that wrote no log, no frames or no OK line, a PSX shader that did
# not compile, and a scorer that crashed.
param([string]$Only = "")
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$failed = $false

# /MIR for the code folders: a file deleted or renamed in the source must not
# live on in the sandbox and compile there. Every file in them carries a
# committed .meta, so the mirror mints no GUIDs.
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# PSX/ShadowCaster is found at runtime by Shader.Find and exists in a build
# only because of this file.
Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force

New-Item -ItemType Directory -Force -Path "$proj\Screenshots" | Out-Null
Remove-Item "$proj\Screenshots\dl_*.png" -ErrorAction SilentlyContinue
Remove-Item "$proj\Screenshots\dl_log.txt" -ErrorAction SilentlyContinue

$env:PSX_DAY_ONLY = $Only

# NO -nographics: every frame is a RenderTexture read back, and a null device
# reads back black.
$ok = Invoke-UnityJob -Log "$proj\daylook.log" -MaxMinutes 40 -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.DayLookShots.Capture",
    "-logFile","$proj\daylook.log","-quit","-accept-apiupdate")
if (-not $ok) { Write-Host "shots job did not finish" -ForegroundColor Red; $failed = $true }

Select-String -Path "$proj\daylook.log" -Pattern "error CS|Shader error|Exception" -ErrorAction SilentlyContinue |
    Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\Screenshots\dl_log.txt") {
    Get-Content "$proj\Screenshots\dl_log.txt"
    if (-not (Select-String -Path "$proj\Screenshots\dl_log.txt" -Pattern '^DAY LOOK CAPTURE OK$' -CaseSensitive -Quiet)) {
        Write-Host "the capture did not say OK - a venue threw or was skipped (see the log above)" -ForegroundColor Red
        $failed = $true
    }
}
else {
    Write-Host "no dl_log.txt written - Capture never finished; log tail:" -ForegroundColor Red
    Get-Content "$proj\daylook.log" -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
# A PSX shader that fails to compile renders magenta and throws nothing.
if ((Test-Path "$proj\daylook.log") -and (Select-String -Path "$proj\daylook.log" -Pattern "Shader error in 'PSX/" -CaseSensitive -Quiet)) {
    Write-Host "a PSX shader failed to compile (see $proj\daylook.log)" -ForegroundColor Red
    $failed = $true
}

$shots = @(Get-ChildItem "$proj\Screenshots\dl_*.png" -ErrorAction SilentlyContinue)
"$($shots.Count) day-look frames in $proj\Screenshots"
if ($shots.Count -eq 0) { $failed = $true }
else {
    & py "$PSScriptRoot\day\day_stats.py" "$proj\Screenshots"
    if ($LASTEXITCODE -ne 0) { Write-Host "day_stats.py failed" -ForegroundColor Red; $failed = $true }
}

if ($failed) { Write-Host "DAY LOOK VERIFY FAILED" -ForegroundColor Red; exit 1 }
Write-Host "DAY LOOK VERIFY OK"
exit 0
