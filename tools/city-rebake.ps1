# Rebuild the FOUR CITY SCENES and nothing else (Charlotte refinement WP-07):
# code into the warm sandbox, then PSXRacingBuilder.BuildCityScenesOnly - the
# city prop variants baked from the full prefabs the last full build left,
# the city kit rewritten, and Charlotte + UptownLoop + TryonSprint +
# IndependenceSprint rebuilt. Every other scene, the build settings and the
# full build's log are left alone, so a later build-and-publish -SkipScenes
# still reads the last FULL build's verdict.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXScenery'
#   powershell -ExecutionPolicy Bypass -File tools\city-rebake.ps1
#
# Needs a sandbox that has had one full scene build (the full prop prefabs
# and the other scenes). About five minutes warm. Hidden and batch, like every
# bake. Exit 0 = CITY BUILD OK.
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
# NOT Materials: 221 of its committed .meta files have no .mat beside them in
# the source (the scene build makes the .mat), so the sandbox minted its own
# GUIDs for them and every built scene points at THOSE. A copy from a fresh
# worktree (every mtime new, so /XO lets it all through) put the source's
# GUIDs back over 219 of them and broke every prefab and scene that used them
# (2026-09-29, found by this package's first bake). The build writes the
# materials; nothing needs copying in.
foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
    # /XO: Resources holds BAKED output the sandbox rewrites (see city-cycle.ps1)
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

$marker = Join-Path $proj "PSXRacing_city_build_log.txt"
if (Test-Path $marker) { Remove-Item $marker -Force }
$log = "$proj\citybuild.log"
$ok = Invoke-UnityJob -Log $log -MaxMinutes 40 -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.BuildCityScenesOnly",
    "-logFile",$log,"-accept-apiupdate")
Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_city_props.txt") { Write-Host "--- city prop variants ---"; Get-Content "$proj\PSXRacing_city_props.txt" }
if (-not (Test-Path $marker)) {
    Write-Host "CITY REBAKE FAILED - no build log written (the method threw or never ran); log tail:" -ForegroundColor Red
    Get-Content $log -Tail 40 -ErrorAction SilentlyContinue
    exit 1
}
Get-Content $marker | Select-Object -Last 12
if (-not $ok -or -not (Select-String -Path $marker -Pattern "CITY BUILD OK" -CaseSensitive -Quiet)) {
    Write-Host "CITY REBAKE FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "CITY REBAKE DONE"
exit 0
