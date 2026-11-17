# RAIN 2's still frames (and, with -Timing, its GPU cost): Editor\Rain2Shots.cs.
#
# Edit mode, Charlotte in the rain, fixed poses: chase, a look along the curb,
# the cockpit parked / cruising / racing, the bonnet, a road under a bridge.
# One run shoots ONE side - run it twice for a before / after pair:
#
#   tools\rain2-shots.ps1 -Off           # PSX_RAIN2=0, frames in <Out>\off
#   tools\rain2-shots.ps1                # PSX_RAIN2=1, frames in <Out>\on
#   tools\rain2-shots.ps1 -Timing        # + the cost table at 3832x2054 in <Out>\on\r2_log.txt
#
# Set PSX_SANDBOX to your sandbox (Windows target). The frames are the PC
# picture (PSX_PROFILE=PC) at the HIGH tier unless -Tier says otherwise.
# Do not run -Timing while a game or another Unity job is using the GPU.
param(
    [switch]$Off,
    [switch]$Timing,
    [string]$Tier = "high",
    [string]$Out = "",
    [int]$MaxMinutes = 30
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
$env:PSX_RAIN2_TIMING = if ($Timing -and -not $Off) { "1" } else { "0" }
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "StandaloneWindows64" }
$log = "$proj\rain2shots_$tag.log"
Remove-Item (Join-Path $Out "$tag\r2_log.txt") -ErrorAction SilentlyContinue
Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode", "-projectPath", $proj,
    "-executeMethod", "PSXRacing.EditorTools.Rain2Shots.Run",
    "-logFile", $log, "-accept-apiupdate", "-quit") | Out-Null
Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
$report = Join-Path $Out "$tag\r2_log.txt"
if (Test-Path $report) {
    Get-Content $report
    if (Select-String -Path $report -Pattern "RAIN2 SHOTS OK" -Quiet) { exit 0 }
    exit 1
}
"NO REPORT - the run threw. Tail of the log:"
Get-Content $log -Tail 40
exit 1
