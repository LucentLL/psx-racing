# RAIN 2 PLAYED: Editor\Rain2PlayCheck.cs - free roam in Charlotte in the rain,
# the player's car on I-77 south, SITTING IDLE, CRUISING (45 mph) and RACING,
# chase and cockpit. A frame of the game's camera in each, what the rain did
# (splashes, wiper strokes, puddle hits, sound), what its own per-frame parts
# cost on the CPU and whether they allocated.
#
#   tools\rain2-play-check.ps1            # PSX_RAIN2=1
#   tools\rain2-play-check.ps1 -Off       # the rain as it was
#   tools\rain2-play-check.ps1 -Hour Afternoon
#
# WATCHED by default (a visible editor); -NoWatch or PSX_WATCH=0 runs it hidden
# (batch mode WITH graphics: the frames are render requests). Run it hidden
# while the owner's game is running. Set PSX_SANDBOX to your sandbox.
param(
    [switch]$Off,
    [switch]$NoWatch,
    [string]$Hour = "Night",
    [string]$Tier = "high",
    [string]$Out = "",
    [int]$MaxMinutes = 20
)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXPerf" }
$src = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
if (-not $Out) { $Out = Join-Path $proj "Screenshots\rain2" }
$tag = if ($Off) { "off" } else { "on" }
$env:PSX_RAIN2 = if ($Off) { "0" } else { "1" }
$env:PSX_PROFILE = "PC"
$env:PSX_RAIN2_TIER = $Tier
$env:PSX_RAIN2_TAG = $tag
$env:PSX_RAIN2_OUT = $Out
$env:PSX_RAIN2_HOUR = $Hour
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "StandaloneWindows64" }
$log = "$proj\rain2play_$tag.log"
Remove-Item "$proj\PSXRacing_rain2_play.txt" -ErrorAction SilentlyContinue
# no -nographics: the frames need a graphics device, hidden or not
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode", "-projectPath", $proj,
    "-executeMethod", "PSXRacing.EditorTools.Rain2PlayCheck.Run",
    "-logFile", $log, "-accept-apiupdate") | Out-Null
Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_rain2_play.txt") {
    Get-Content "$proj\PSXRacing_rain2_play.txt"
    if (Select-String -Path "$proj\PSXRacing_rain2_play.txt" -Pattern "FAIL" -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of the log:"
Get-Content $log -Tail 40
exit 1
