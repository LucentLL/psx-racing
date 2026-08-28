# Mirror to the sandbox, rebuild the circuits, and drop rays onto every one of
# them: does the ground ever come up through the tarmac, is there actually a
# hole under each bridge, and can you see daylight under anything standing
# beside the road.
#
#   powershell -ExecutionPolicy Bypass -File tools\terrain-audit.ps1
#
# The rebuild is NOT optional. The mirror below is /MIR, so it overwrites the
# sandbox scenes with the ones in source -- and the source scenes are always
# stale, because a sandbox build never writes back. Mirroring and then auditing
# reads a set of circuits from whenever the source scenes were last committed:
# the first run of this reported every track dead flat with no bridge decks,
# hours after they had been built with both. The same trap silently invalidated
# a screenshot pass on the way here.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

# A tool that throws never writes its report, and the previous run's file then
# reads as a pass over a run that died.
if (Test-Path "$proj\PSXRacing_terrain_audit.txt") {
    Remove-Item "$proj\PSXRacing_terrain_audit.txt" -Force
}

if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD DID NOT FINISH - auditing would read the stale source scenes. Stopping."
    Get-Content "$proj\scenebuild.log" -Tail 6
    exit 1
}

Invoke-UnityJob -Log "$proj\terrain.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.TerrainAudit.Run",
    "-logFile","$proj\terrain.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\terrain.log" -Pattern "error CS|Exception" |
    Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_terrain_audit.txt") {
    Get-Content "$proj\PSXRacing_terrain_audit.txt"
} else {
    Write-Host "NO REPORT WRITTEN - the audit threw, see $proj\terrain.log"
}
