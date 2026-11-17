# THE TAPES, played for real (2026-11-16): Editor\TapeCheck.cs. A WATCHED play
# test (a career race recorded with the AI driving, SAVE TO TAPE, the tape in
# the VCR through TapePlayback, play / FF / REW / pause / the end / EJECT home
# with nothing banked), then the shelf and the VIDEO STORE pages photographed
# (a batch capture with graphics).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\tape-check.ps1 [-SkipPages] [-SkipRun] [-Out <folder>]
#
# Takes the sandbox's lock atomically (tools\sandbox-lock.ps1), mirrors ONLY
# Scripts / Editor / Shaders (from -Src, a snapshot, when given), releases the
# lock even on failure. Exit code 0 = no FAIL in either report.
param([switch]$NoWatch, [int]$MaxMinutes = 35, [switch]$SkipPages, [switch]$SkipRun,
      [string]$Me = "camcorder", [string]$Src = "", [string]$Out = "", [string]$PagesDir = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$from = if ($Src) { $Src } else { Split-Path -Parent $PSScriptRoot }
. "$PSScriptRoot\unity-wait.ps1"
. "$PSScriptRoot\sandbox-lock.ps1"
# PSXRec is an ANDROID sandbox: never let unity-wait default it to WebGL (a
# target switch re-imports the project).
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "Android" }

$stamp = Enter-SandboxLock -Project $proj -Me $Me
$code = 0
# The owner's game is running: no editor in front of it. The test runs HIDDEN,
# with a graphics device, and says so.
if (Get-Process PSXRacing -ErrorAction SilentlyContinue) { "PSXRacing.exe is running: this play test runs HIDDEN"; $NoWatch = $true }
$watch = Test-PSXWatch -NoWatch:$NoWatch
$mode = if ($watch) { @("-batchmode","-nographics") } else { @("-batchmode") }
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$from\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $env:PSX_EDITION = "ALL"
    if (-not $SkipRun) {
        $report = "$proj\PSXRacing_tape_check.txt"
        Remove-Item $report -ErrorAction SilentlyContinue
        Get-ChildItem "$proj\Screenshots\tapes\vcr_*.png" -ErrorAction SilentlyContinue | Remove-Item
        $log = "$proj\tapecheck.log"
        Invoke-UnityJob -Watch:$watch -Log $log -MaxMinutes $MaxMinutes -Project $proj -UnityArgs ($mode + @(
            "-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.TapeCheck.Run",
            "-logFile",$log,"-accept-apiupdate")) | Out-Null
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
        Get-ChildItem "$proj\Screenshots\tapes\menu_*.png" -ErrorAction SilentlyContinue | Remove-Item
        $log = "$proj\tapepages.log"
        # -PagesDir: the pages with another tool's tapes (CamcorderShots' races).
        $env:PSX_TAPE_PAGES_DIR = $PagesDir
        # A capture, not a play test: hidden, but WITH graphics (it renders UI).
        Invoke-UnityJob -Log $log -MaxMinutes 20 -Project $proj -UnityArgs @(
            "-batchmode","-quit","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.TapeCheck.Pages",
            "-logFile",$log,"-accept-apiupdate") | Out-Null
        if (Test-Path $report) {
            Get-Content $report
            if (Select-String -Path $report -Pattern "FAIL" -Quiet) { $code = 1 }
            Select-String -Path $log -Pattern "\[HomePreview\] (CLIP|OVERLAP)" | Select-Object -First 20 | ForEach-Object { $_.Line }
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
    Exit-SandboxLock -Project $proj -Stamp $stamp
}
exit $code
