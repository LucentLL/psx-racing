# THE TAPES, played for real (2026-11-16): Editor\TapeCheck.cs. A WATCHED play
# test (a career race recorded with the AI driving, SAVE TO TAPE, the tape in
# the VCR through TapePlayback, play / FF / REW / pause / the end / EJECT home
# with nothing banked), then the shelf and the VIDEO STORE pages photographed
# (a batch capture with graphics).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\tape-check.ps1 [-SkipPages] [-Out <folder>]
#
# Takes AGENT_LOCK.txt itself, mirrors ONLY Scripts / Editor / Shaders,
# deletes the lock even on failure. Exit code 0 = no FAIL in either report.
param([switch]$NoWatch, [int]$MaxMinutes = 35, [switch]$SkipPages, [switch]$SkipRun,
      [string]$Me = "camcorder", [string]$Src = "", [string]$Out = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = if ($Src) { $Src } else { Split-Path -Parent $PSScriptRoot }
. "$PSScriptRoot\unity-wait.ps1"
# PSXRec is an ANDROID sandbox: never let unity-wait default it to WebGL (a
# target switch re-imports the project).
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "Android" }
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
$code = 0
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $env:PSX_EDITION = "ALL"
    if (-not $SkipRun) {
        $report = "$proj\PSXRacing_tape_check.txt"
        Remove-Item $report -ErrorAction SilentlyContinue
        Get-ChildItem "$proj\Screenshots\tapes\*.png" -ErrorAction SilentlyContinue | Remove-Item
        $log = "$proj\tapecheck.log"
        Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log $log -MaxMinutes $MaxMinutes -Project $proj -UnityArgs @(
            "-batchmode","-nographics","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.TapeCheck.Run",
            "-logFile",$log,"-accept-apiupdate") | Out-Null
        Select-String -Path $log -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
        if (Test-Path $report) {
            Get-Content $report
            if (Select-String -Path $report -Pattern "FAIL" -Quiet) { $code = 1 }
        } else {
            "NO TAPE REPORT - the run threw. Tail of the log:"
            Get-Content $log -Tail 40
            $code = 1
        }
    }
    if (-not $SkipPages) {
        $report = "$proj\PSXRacing_tape_pages.txt"
        Remove-Item $report -ErrorAction SilentlyContinue
        $log = "$proj\tapepages.log"
        # A capture, not a play test: hidden, but WITH graphics (it renders UI).
        Invoke-UnityJob -Log $log -MaxMinutes 20 -Project $proj -UnityArgs @(
            "-batchmode","-quit","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.TapeCheck.Pages",
            "-logFile",$log,"-accept-apiupdate") | Out-Null
        if (Test-Path $report) {
            Get-Content $report
            if (Select-String -Path $report -Pattern "FAIL" -Quiet) { $code = 1 }
        } else {
            "NO PAGES REPORT. Tail of the log:"
            Get-Content $log -Tail 30
            $code = 1
        }
    }
    if ($Out) {
        New-Item -ItemType Directory -Force $Out | Out-Null
        Get-ChildItem "$proj\Screenshots\tapes\*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $Out
        foreach ($r in @("PSXRacing_tape_check.txt", "PSXRacing_tape_pages.txt")) {
            if (Test-Path "$proj\$r") { Copy-Item "$proj\$r" -Destination $Out }
        }
    }
} finally {
    if (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        if ($txt.StartsWith($Me)) { Remove-Item $lock -Force }
    }
}
exit $code
