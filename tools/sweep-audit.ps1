# Mirror to the sandbox, rebuild the circuits, and drive a box the size of the
# player's car over every square metre of all six of them: is there anywhere
# inside the barrier line a car simply does not fit.
#
#   powershell -ExecutionPolicy Bypass -File tools\sweep-audit.ps1
#
# The rebuild is NOT optional, for the reason spelled out in terrain-audit.ps1:
# the mirror is /MIR, a sandbox build never writes back, so the source scenes
# are always stale and sweeping straight after a mirror measures whichever
# circuits were last committed rather than the ones the code builds now.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

if (Test-Path "$proj\PSXRacing_sweep_audit.txt") {
    Remove-Item "$proj\PSXRacing_sweep_audit.txt" -Force
}

if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD DID NOT FINISH - sweeping would read the stale source scenes. Stopping."
    Get-Content "$proj\scenebuild.log" -Tail 6
    exit 1
}

Invoke-UnityJob -Log "$proj\sweep.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TrackSweepAudit.Run",
    "-logFile","$proj\sweep.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\sweep.log" -Pattern "error CS|Exception" |
    Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_sweep_audit.txt") {
    Get-Content "$proj\PSXRacing_sweep_audit.txt"
} else {
    Write-Host "NO REPORT WRITTEN - the sweep threw, see $proj\sweep.log"
}
