# CARS AT REST ON THE REAL GRIDS (Editor/GridGripCheck.cs; owner 2026-11-17:
# "cars slide on banked roads. Even when resting in place at the start of a
# race"). Each venue is loaded as a booked race with its countdown HELD; every
# car is measured standing on its own grid for 20 s in the game's own hold, with
# no pedals, the brake, the lever, then a launch; then the player at the route's
# most banked spot; -Lap N races N s on autopilot for the at-speed telemetry.
# Reports PSXRacing_grip_check[_TAG].txt / .csv in the sandbox: SIDE / FORE
# (centre, mm), YAW (deg), CORNER (worst body corner, mm), settle, held %.
#
#   powershell -ExecutionPolicy Bypass -File tools\grid-grip-check.ps1 -Venues LakeLure,GillespieGap -StaticGrip 0 -Tag before
#   ... -StaticGrip 1 -Tag after -Lap 90
#   ... -Shots   overhead frames of the hold (a run WITH graphics) in Screenshots\GripStrip\
#
# Also: PSX Racing/Grip Sim (Bank Sweep) = Editor/GripSim.cs, the same cases on
# tilted planes 2-12% x 0-15% plus the launch / crawl / crossfall / banked ring.
# Code only (Scripts + Editor mirrored), on a sandbox whose venue scenes are
# built (PSX_SANDBOX; set PSX_BUILD_TARGET). Hidden (batch) always.
param([string]$Venues = "LakeLure,ChimneyRock,SwissNC226A,BlueRidge,GillespieGap,UptownLoop",
      [string]$StaticGrip = "1", [string]$Tag = "", [int]$Lap = 0, [int]$Seconds = 20,
      [switch]$Shots, [int]$MaxMinutes = 40, [switch]$NoSync)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $NoSync) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
}
$env:PSX_STATIC_GRIP = $StaticGrip
$env:PSX_GRIP_TAG = $Tag
$env:PSX_GRIP_VENUES = $Venues
$env:PSX_GRIP_LAP = "$Lap"
$env:PSX_GRIP_SECONDS = "$Seconds"
$env:PSX_GRIP_SHOTS = if ($Shots) { "1" } else { "" }
$env:PSX_GRIP_ONLYHOLD = if ($Shots) { "1" } else { "" }
$name = "PSXRacing_grip_check" + $(if ($Tag) { "_$Tag" } else { "" }) + ".txt"
Remove-Item "$proj\$name" -ErrorAction SilentlyContinue
$uargs = @("-batchmode", "-projectPath", $proj, "-executeMethod", "PSXRacing.EditorTools.GridGripCheck.Run",
           "-logFile", "$proj\gridgrip.log", "-accept-apiupdate")
if (-not $Shots) { $uargs = @("-batchmode", "-nographics") + $uargs[1..($uargs.Count - 1)] }
Invoke-UnityJob -Log "$proj\gridgrip.log" -MaxMinutes $MaxMinutes -UnityArgs $uargs | Out-Null
Select-String -Path "$proj\gridgrip.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\$name") { Get-Content "$proj\$name" | Where-Object { $_ -notmatch "^        " }; exit 0 }
"NO REPORT. Tail of gridgrip.log:"
Get-Content "$proj\gridgrip.log" -Tail 30
exit 1
