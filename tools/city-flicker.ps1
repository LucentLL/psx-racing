# THE CITY FLICKER FINDER (Assets\PSXRacing\Editor\CityFlickerFinder.cs,
# city-popin 2026-10-28): ID renders at ~12 spots on the race routes and three
# distances, near plane x1.01 (depth ties), chase and cockpit near planes, under
# OpenGL core (the game is WebGL: 24-bit depth, no reversed Z). Ranked list:
#   <sandbox>\Docs\CITY_FLICKER_<Tag>.md
# With -Shots, the pop-in frames too (a Tryon street from 600 m and 300 m,
# far buildings on and off): <sandbox>\Screenshots\popin_*.png
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\city-flicker.ps1 -Tag before -Shots
#
# Knobs: -NoProxies (the finder with the far buildings hidden), -Spots a,b
# (a subset: tryon0..3, uptown0..3, indep0..1, i277s, i277n), -D3D (D3D11).
# Code only (Scripts, Editor, Shaders /MIR) - never Resources or Art. Honours
# the shared sandbox's AGENT_LOCK.txt: waits while another agent's job holds it.
# West Trade (2026-10-29): -Spots wtrade0,wtrade1,wtrade2 (West Trade Street
# westbound past the owner's flickering tan building); -ShotSpots/-ShotBacks
# pick what -Shots frames and from how far back (-ShotSpots wtrade1 -ShotBacks
# 90,40); -Owner names the lock; -BldFrom <charlotte_bld.bytes> puts that
# buildings file in the sandbox for this run only (its own copy goes back
# after; the content only - the sandbox keeps its .meta, no GUID moves), to
# check a data fix such as tools\city\bld_twins.mjs before it ships;
# -ShotBlurMph 100 adds each frame through the game's ZOOM speed blur.
param([string]$Tag = "run", [switch]$Shots, [switch]$NoProxies, [switch]$Both, [string]$Spots = "", [switch]$D3D,
      [switch]$NoSync, [switch]$NoWatch, [int]$MaxMinutes = 40, [string]$ShotSpots = "", [string]$ShotBacks = "",
      [string]$Owner = "city-popin", [string]$BldFrom = "", [int]$ShotBlurMph = 0)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

$lock = Join-Path $proj "AGENT_LOCK.txt"
while ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 90) {
    "waiting: $(Get-Content $lock -Raw)"; Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ($Owner + " " + (Get-Date).ToString("s"))
$bld = Join-Path $proj "Assets\PSXRacing\Resources\charlotte_bld.bytes"
$bldKeep = Join-Path $proj "charlotte_bld.bytes.keep"
try {
    if ($BldFrom) {
        Copy-Item $bld $bldKeep -Force
        Copy-Item $BldFrom $bld -Force
        "buildings file for this run: $BldFrom"
    }
    if (-not $NoSync) {
        foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
            robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
        }
    }
    $env:PSX_FLICK_TAG = $Tag
    $env:PSX_FLICK_SHOTS = if ($Shots) { "1" } else { "0" }
    $env:PSX_FLICK_PROXIES = if ($NoProxies) { "0" } elseif ($Both) { "both" } else { "1" }
    $env:PSX_FLICK_SPOTS = $Spots
    $env:PSX_FLICK_SHOT_SPOTS = if ($ShotSpots) { $ShotSpots } else { $null }
    $env:PSX_FLICK_SHOT_BACKS = if ($ShotBacks) { $ShotBacks } else { $null }
    $env:PSX_FLICK_SHOT_BLUR = if ($ShotBlurMph -gt 0) { "$ShotBlurMph" } else { $null }
    $log = "$proj\flicker_$Tag.log"
    Remove-Item "$proj\Docs\CITY_FLICKER_$Tag.md" -ErrorAction SilentlyContinue
    $uargs = @("-batchmode", "-projectPath", $proj, "-executeMethod", "PSXRacing.EditorTools.CityFlickerFinder.Run",
               "-logFile", $log, "-accept-apiupdate", "-quit")
    if ($D3D) { $uargs += "-force-d3d11" } else { $uargs += "-force-glcore" }
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log $log -MaxMinutes $MaxMinutes -UnityArgs $uargs | Out-Null
}
finally {
    if ($BldFrom -and (Test-Path $bldKeep)) { Copy-Item $bldKeep $bld -Force; Remove-Item $bldKeep -Force }
    Remove-Item $lock -ErrorAction SilentlyContinue
}

Select-String -Path $log -Pattern "error CS|\[Flicker\]|\[CityProxies\]|Exception" | Select-Object -First 40 | ForEach-Object { $_.Line }
$md = if ($Both) { "$proj\Docs\CITY_FLICKER_$($Tag)_on.md" } else { "$proj\Docs\CITY_FLICKER_$Tag.md" }
if (Test-Path $md) { Get-Content $md -TotalCount 40; exit 0 }
"NO REPORT. Tail of $log :"
Get-Content $log -Tail 40
exit 1
