# Play a race headless for twelve seconds, then play it back and check the
# replay is the race: cars kinematic and on their recorded poses, the director
# holding the lens, the clock running, pause holding it, and everything put
# back when it ends. Scripts+Editor only, like reverse-check.ps1: the built
# scenes and the baked shells survive, so this is a ~4 minute answer.
#
#   powershell -ExecutionPolicy Bypass -File tools\replay-check.ps1
#
# Exit code 0 = the report has no FAIL; 1 = it does, or the run threw.
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# Delete the marker first: a tool that throws never writes its log, and a stale
# one certifies the previous run just as convincingly as a fresh one.
Remove-Item "$proj\PSXRacing_replay_check.txt" -ErrorAction SilentlyContinue

# NO -quit: this one enters play mode and exits itself when it is done.
Invoke-UnityJob -Log "$proj\replaycheck.log" -MaxMinutes 20 -UnityArgs @(
    "-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.ReplayCheck.Run",
    "-logFile","$proj\replaycheck.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\replaycheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_replay_check.txt") {
    Get-Content "$proj\PSXRacing_replay_check.txt"
    if (Select-String -Path "$proj\PSXRacing_replay_check.txt" -Pattern "FAIL" -Quiet) { exit 1 }
    exit 0
}
"NO REPORT - the run threw. Tail of replaycheck.log:"
Get-Content "$proj\replaycheck.log" -Tail 40
exit 1
