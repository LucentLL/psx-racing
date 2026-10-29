# THE HITCH PROBE (Assets\PSXRacing\Editor\HitchProbe.cs; branch
# city-build-hitches, 2026-10-29): every tile along the Uptown Loop (I-277),
# the Tryon Street Sprint and I-77 built a step a call (each step's ms, label
# and where its time went), stood up, signed and planted, every tile hashed
# whole; then each race venue played on autopilot, chase view, uncapped, every
# frame over 16.7 ms with what the city did in it.
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\hitch-probe.ps1 -Tag before
#   ... -Tag after -Base C:\Users\mcgee\PSXRec\PSXRacing_hitch_before_hashes.txt
#
# -Mode build | play | all; -Venues UptownLoop,TryonSprint; -Seconds 100;
# -Routes uptown,tryon,i77; -MaxTiles 70 (a route); -PlayAB: each venue raced twice,
# the hitch fixes off then on (CityWorld.NoForcedFinish, CityMeshes.FineSteps, ...).
# SANDBOX RULES (a shared sandbox): waits for another agent's AGENT_LOCK.txt
# (younger than 90 min), writes its own, mirrors ONLY Scripts / Editor /
# Shaders from this checkout (never Resources or Art: a worktree's mtimes would
# overwrite the sandbox's minted .meta GUIDs), runs, deletes the lock even on
# failure. Report: <sandbox>\PSXRacing_hitch_<Tag>.txt (+ _hashes.txt).
param([string]$Tag = "run", [string]$Base = "", [string]$Mode = "all", [string]$Venues = "UptownLoop,TryonSprint",
      [int]$Seconds = 100, [string]$Routes = "uptown,tryon,i77", [int]$MaxTiles = 70, [string]$Hour = "Night",
      [switch]$NoWatch, [int]$MaxMinutes = 40, [string]$Me = "city-build-hitches", [string]$AB = "1", [switch]$PlayAB)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$lock = Join-Path $proj "AGENT_LOCK.txt"

while (Test-Path $lock) {
    $txt = (Get-Content $lock -Raw).Trim()
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if ($txt.StartsWith($Me) -or $age -gt 90) { break }
    "sandbox locked by '$txt' ($([int]$age) min) - waiting"
    Start-Sleep -Seconds 120
}
"$Me $((Get-Date).ToString('s'))" | Set-Content -Path $lock -Encoding ascii
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    Remove-Item "$proj\PSXRacing_hitch_$Tag.txt" -ErrorAction SilentlyContinue
    Remove-Item "$proj\PSXRacing_hitch_$($Tag)_hashes.txt" -ErrorAction SilentlyContinue
    $env:PSX_HITCH_TAG = $Tag
    $env:PSX_HITCH_BASE = $Base
    $env:PSX_HITCH_MODE = $Mode
    $env:PSX_HITCH_VENUES = $Venues
    $env:PSX_HITCH_SECONDS = "$Seconds"
    $env:PSX_HITCH_ROUTES = $Routes
    $env:PSX_HITCH_MAXTILES = "$MaxTiles"
    $env:PSX_HITCH_HOUR = $Hour
    $env:PSX_HITCH_AB = $AB
    $env:PSX_HITCH_PLAYAB = if ($PlayAB) { "1" } else { "0" }
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\hitchprobe_$Tag.log" -MaxMinutes $MaxMinutes -UnityArgs @(
        "-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.HitchProbe.Run",
        "-logFile","$proj\hitchprobe_$Tag.log","-accept-apiupdate") | Out-Null
}
finally {
    Remove-Item $lock -ErrorAction SilentlyContinue
}
Select-String -Path "$proj\hitchprobe_$Tag.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_hitch_$Tag.txt") {
    Get-Content "$proj\PSXRacing_hitch_$Tag.txt"
    exit 0
}
"NO REPORT - the run threw. Tail of hitchprobe_$Tag.log:"
Get-Content "$proj\hitchprobe_$Tag.log" -Tail 40
exit 1
