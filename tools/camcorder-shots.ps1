# THE CAMCORDER, played and photographed (2026-11-16; round 2: a camera at
# every race point). A WATCHED play test (Editor\CamcorderShots.cs): for each
# item of the plan a career race with the player's car on autopilot, fast-
# forwarded to its finish or a cap; SAVE TO TAPE; then the replay in its
# default view, a frame at every clip (START, each CHECKPOINT, FINISH).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\camcorder-shots.ps1 [-Base] [-Tag x]
#       [-Plan "GillespieGap:Noon,GillespieGap:Noon:slide2"] [-UntilCp 3] [-MaxRace 420] [-Fast 3] [-Laps 1]
#
# -Base runs with PSX_CAMCORDER=0 (today's replay at the same race moments, the
# honest before) in its own fresh editor. ":slideN" in the plan throws the car
# sideways through checkpoint N (the flex proof). Takes the sandbox's lock
# atomically (tools\sandbox-lock.ps1), mirrors ONLY Scripts / Editor / Shaders
# (from -Src, a snapshot, when given), copies -Extra data files by explicit
# path, releases the lock even on failure. Pictures land in
# <sandbox>\Screenshots\camcorder and are copied to -Out.
# Exit code 0 = the report has no FAIL.
param([switch]$NoWatch, [int]$MaxMinutes = 45, [switch]$Base, [string]$Plan = "", [string]$Tag = "",
      [int]$UntilCp = 0, [int]$MaxRace = 420, [string]$Fast = "3", [int]$Laps = 1, [string]$Traffic = "",
      [string]$Me = "camcorder", [string]$Src = "", [string]$Out = "", [string]$Extra = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$from = if ($Src) { $Src } else { Split-Path -Parent $PSScriptRoot }
. "$PSScriptRoot\unity-wait.ps1"
. "$PSScriptRoot\sandbox-lock.ps1"
# PSXRec is an ANDROID sandbox: never let unity-wait default it to WebGL (a
# target switch re-imports the project).
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "Android" }

$stamp = Enter-SandboxLock -Project $proj -Me $Me
$code = 1
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$from\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($f in @($Extra -split ',' | Where-Object { $_ })) {
        Copy-Item (Join-Path (Split-Path -Parent $PSScriptRoot) $f) (Join-Path $proj $f) -Force
        "copied $f"
    }
    if (-not $Tag) { $Tag = if ($Base) { "base" } else { "cam" } }
    $report = "$proj\PSXRacing_camcorder_shots_$Tag.txt"
    Remove-Item $report -ErrorAction SilentlyContinue
    Get-ChildItem "$proj\Screenshots\camcorder\$($Tag)_*.png" -ErrorAction SilentlyContinue | Remove-Item
    $env:PSX_CAMCORDER = if ($Base) { "0" } else { "1" }
    $env:PSX_CAMSHOT_TAG = $Tag
    $env:PSX_CAMSHOT_PLAN = $Plan
    $env:PSX_CAMSHOT_UNTILCP = "$UntilCp"
    $env:PSX_CAMSHOT_MAXRACE = "$MaxRace"
    $env:PSX_CAMSHOT_FAST = $Fast
    $env:PSX_CAMSHOT_LAPS = "$Laps"
    $env:PSX_CAMSHOT_TRAFFIC = $Traffic
    $env:PSX_EDITION = "ALL"
    $log = "$proj\camcordershots_$Tag.log"
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log $log -MaxMinutes $MaxMinutes -Project $proj -UnityArgs @(
        "-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.CamcorderShots.Run",
        "-logFile",$log,"-accept-apiupdate") | Out-Null
    Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
    if (Test-Path $report) {
        Get-Content $report
        $code = if (Select-String -Path $report -Pattern "FAIL" -Quiet) { 1 } else { 0 }
    } else {
        "NO REPORT - the run threw. Tail of the log:"
        Get-Content $log -Tail 40
    }
    if ($Out) {
        New-Item -ItemType Directory -Force $Out | Out-Null
        Get-ChildItem "$proj\Screenshots\camcorder\$($Tag)_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
        if (Test-Path $report) { Copy-Item $report -Destination $Out }
    }
} finally {
    Exit-SandboxLock -Project $proj -Stamp $stamp
}
exit $code
