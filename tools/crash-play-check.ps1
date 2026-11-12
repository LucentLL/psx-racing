# R0a CRASH STOP-GAP in the RUNNING game (Editor/CrashPlayCheck.cs): the
# crash pull's hands-off drift at 100 km/h (minor 0.5-1.5 m, severe 2-5 m per
# 100 m, toward the struck side, left and right, counter-steer assist OFF /
# STANDARD / FULL, three chassis), the hit applied live through the career
# car's ledger, the next drive built through the real bank path (also with an
# alignment the car already carries), real wall contacts located FRONT LEFT
# (35/60/75% of the nose) / FRONT RIGHT / FRONT CENTRE with no alignment /
# SIDE RIGHT (a 200 m barrier) / REAR CENTRE, and a swapped-in car starting
# clean. Exit 0 = all hold.
#
#   powershell -ExecutionPolicy Bypass -File tools\crash-play-check.ps1
#
# Code only, on an already-built sandbox (Scripts and Editor over the top, no
# mirror; PSX_SANDBOX, default PSXBuild). Watched by default: hidden batch
# play mode crashes in RenderTexture::Create on the owner's PC. The self-test
# half (table, sweep, apply-back) is LifeSimSelfTest.RunCrash.
param([switch]$NoWatch, [int]$MaxMinutes = 30)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# A stale report certifies the previous run as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_crash_play_check.txt" -ErrorAction SilentlyContinue

# NO -quit: it enters play mode and exits itself when it is done.
Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\crashplay.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CrashPlayCheck.Run",
    "-logFile","$proj\crashplay.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\crashplay.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_crash_play_check.txt") {
    Get-Content "$proj\PSXRacing_crash_play_check.txt"
    if (Select-String -Path "$proj\PSXRacing_crash_play_check.txt" -Pattern "FAIL" -CaseSensitive -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of crashplay.log:"
Get-Content "$proj\crashplay.log" -Tail 40
exit 1
