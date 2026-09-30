# A DEVELOPMENT WebGL player of the whole game (edition ALL) for the colour
# harness (Scripts\Dev\ShotLink.cs + tools\colour\shot-link.mjs). Never
# published: it is uncompressed, it is a development build, and it lands in
# <sandbox>\Build\WebGL-SHOT, which no publish reads.
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\colour\harness-build.ps1 [-NoCopy]
#
# Needs a sandbox with built scenes. The same release budget runs as for a
# shipped build (ReleaseBudget.Apply: the 16-bit texture overrides), because
# the point is the picture the shipped player draws.
param([switch]$NoCopy, [int]$MaxMinutes = 75)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. "$src\tools\unity-wait.ps1"
$outDir = "$proj\Build\WebGL-SHOT"

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    robocopy "$src\Assets\Plugins" "$proj\Assets\Plugins" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
}
$marker = "$proj\PSXRacing_build_log.txt"
if (-not (Test-Path $marker) -or -not (Select-String -Path $marker -Pattern "BUILD OK" -Quiet)) {
    Write-Host "no green scene build in $proj (BUILD OK in $marker) - run the scene build first" -ForegroundColor Red; exit 1
}
& py "$src\tools\guid-audit.py" $proj
if ($LASTEXITCODE -ne 0) { Write-Host "GUID AUDIT FAILED" -ForegroundColor Red; exit 1 }

Remove-Item "$outDir\build_ok.txt" -Force -ErrorAction SilentlyContinue
$runStart = Get-Date
$log = "$proj\build_shot.log"
Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-buildTarget","WebGL",
    "-executeMethod","PSXRacing.EditorTools.PSXBuildWebGL.BuildFromCommandLine",
    "-psxEdition","ALL","-psxDevelopment","-psxOutput",$outDir,
    "-logFile",$log,"-accept-apiupdate") | Out-Null
if (-not (Test-Path "$outDir\build_ok.txt")) {
    Write-Host "HARNESS BUILD FAILED - see $log" -ForegroundColor Red
    Select-String -Path $log -Pattern "IL2CPP error|Error building Player|error CS" | Select-Object -First 8 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(220, $_.Line.Length)) }
    exit 1
}
$fresh = @(Get-ChildItem "$outDir\Build" -File | Where-Object { $_.LastWriteTime -ge $runStart })
if ($fresh.Count -eq 0) { Write-Host "STALE OUTPUT - nothing under $outDir\Build was written by this run" -ForegroundColor Red; exit 1 }
Get-Content "$outDir\build_ok.txt"
Get-ChildItem "$outDir\Build" -File | ForEach-Object { "  {0,-40} {1,8:0.0} MiB" -f $_.Name, ($_.Length / 1MB) }
