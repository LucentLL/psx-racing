# Play the map where the owner lost it (2026-10-29: "Map does not always
# appear while racing/free roaming") and count the pixels its streets/route
# put on the frame: Charlotte free roam (spawn, West Trade Street, the map
# rebuilt, cockpit, after a pause), the 277 race (chase, cockpit, rebuilt),
# BlueRidge (chase, cockpit). See Editor/MinimapPlayCheck.cs.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\minimap-play-check.ps1
#
# Code only (Scripts, Editor, Shaders mirrored); never Resources or Art.
# Takes the sandbox's AGENT_LOCK.txt for the run. Exit 0 = no FAIL.
param([switch]$NoWatch, [int]$MaxMinutes = 30, [string]$Tag = "minimap-missing")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXCity" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

$lock = Join-Path $proj "AGENT_LOCK.txt"
while ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 90) {
    "sandbox locked: " + (Get-Content $lock -Raw).Trim() + " - waiting"
    Start-Sleep -Seconds 60
}
Set-Content -Path $lock -Value ("$Tag " + (Get-Date).ToString("o")) -Encoding utf8
try {
    Wait-ProjectIdle -ProjectPath $proj | Out-Null
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $report = Join-Path $proj "PSXRacing_minimap_play_check.txt"
    Remove-Item $report -ErrorAction SilentlyContinue
    $env:PSX_EDITION = "ALL"
    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\minimapplaycheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
        "-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.MinimapPlayCheck.Run",
        "-logFile","$proj\minimapplaycheck.log","-accept-apiupdate") | Out-Null
    Select-String -Path "$proj\minimapplaycheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
    if (Test-Path $report) {
        Get-Content $report
        $code = if (Select-String -Path $report -Pattern "FAIL" -Quiet) { 1 } else { 0 }
    } else {
        "NO REPORT - the run threw. Tail of minimapplaycheck.log:"
        Get-Content "$proj\minimapplaycheck.log" -Tail 40
        $code = 1
    }
} finally {
    Remove-Item $lock -ErrorAction SilentlyContinue
}
exit $code
