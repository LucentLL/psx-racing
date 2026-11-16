# THE CAMCORDER, played and photographed (2026-11-16). A WATCHED play test:
# a career race on each venue / hour of the plan with the player's car on
# autopilot, cut after ~24 s, then the replay in its default view, frames of
# the PSX framebuffer every 1.6 s (Editor\CamcorderShots.cs).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\camcorder-shots.ps1 [-Base] [-Plan "GillespieGap:Noon"] [-Seq]
#
# -Base runs with PSX_CAMCORDER=0 (today's replay, the honest before) in its
# own fresh editor. Takes AGENT_LOCK.txt itself (waits while another's lock is
# under 90 min old), mirrors ONLY Scripts / Editor / Shaders, deletes the lock
# even on failure. Pictures land in <sandbox>\Screenshots\camcorder and are
# copied to -Out.
# Exit code 0 = the report has no FAIL.
param([switch]$NoWatch, [int]$MaxMinutes = 35, [switch]$Base, [string]$Plan = "", [switch]$Seq,
      [string]$Me = "camcorder", [string]$Src = "", [string]$Out = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = if ($Src) { $Src } else { Split-Path -Parent $PSScriptRoot }
. "$PSScriptRoot\unity-wait.ps1"
$lock = Join-Path $proj "AGENT_LOCK.txt"

function Wait-Lock {
    while (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
        if ($txt.StartsWith($Me) -or $age -gt 90) { break }
        "sandbox locked by '$txt' ($([int]$age) min) - waiting"
        Start-Sleep -Seconds 60
    }
}

Wait-Lock
"$Me $(Get-Date -Format s)" | Out-File -FilePath $lock -Encoding ascii
$code = 1
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $tag = if ($Base) { "base" } else { "cam" }
    $report = "$proj\PSXRacing_camcorder_shots_$tag.txt"
    Remove-Item $report -ErrorAction SilentlyContinue
    Get-ChildItem "$proj\Screenshots\camcorder\$($tag)_*.png" -ErrorAction SilentlyContinue | Remove-Item
    $env:PSX_CAMCORDER = if ($Base) { "0" } else { "1" }
    $env:PSX_CAMSHOT_TAG = $tag
    $env:PSX_CAMSHOT_PLAN = $Plan
    $env:PSX_CAMSHOT_SEQ = if ($Seq) { "1" } else { "0" }
    $env:PSX_EDITION = "ALL"
    $log = "$proj\camcordershots_$tag.log"
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
        Get-ChildItem "$proj\Screenshots\camcorder\$($tag)_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
        if (Test-Path $report) { Copy-Item $report -Destination $Out }
    }
} finally {
    if (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        if ($txt.StartsWith($Me)) { Remove-Item $lock -Force }
    }
}
exit $code
