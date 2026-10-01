# The Charlotte REFERENCE SPOTS (refinement WP-01): fixed driver-eye shots at
# the Street View spots, the kink verdicts and the transects' crests and dips
# (Editor/CityRefSpots.cs), and a contact sheet of them - side by side with
# an earlier run when -Before is given. Every package shoots these before and
# after and attaches the sheet (plan 2.3, G-ship).
#
#   $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\city-refspots.ps1 -Label wp01-baseline
#   powershell -ExecutionPolicy Bypass -File tools\city-refspots.ps1 -Label wp04 -Before <dir of an earlier run>
#
# Shots and the sheet land in -Out (default <sandbox>\Screenshots\City\ref_<Label>).
# About 3 minutes: code + data into the warm sandbox, one Unity job, no scene build.
param([string]$Label = "", [string]$Out = "", [string]$Before = "")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Label) { $Label = Get-Date -Format "yyyyMMdd-HHmm" }
if (-not $Out) { $Out = Join-Path $proj "Screenshots\City\ref_$Label" }

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
    # /XO for the same reason as city-cycle.ps1: Resources holds baked output
    robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

$shots = Join-Path $proj "Screenshots\City\ref"
if (Test-Path "$shots\ref_spots.txt") { Remove-Item "$shots\ref_spots.txt" -Force }
$log = "$proj\cityrefspots.log"
$ok = Invoke-UnityJob -Log $log -MaxMinutes 20 -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityRefSpots.Run",
    "-logFile",$log,"-accept-apiupdate")
Select-String -Path $log -Pattern "error CS|Exception|\[CityRefSpots\]" -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { $_.Line }
if (-not $ok -or -not (Test-Path "$shots\ref_spots.txt")) {
    Write-Host "REFSPOTS FAILED - the job did not finish or wrote nothing; log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    exit 1
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item "$shots\*" $Out -Force
$sheetArgs = @("$src\tools\city\refsheet.py", $Out, "--title", "Charlotte reference spots - $Label")
if ($Before) { $sheetArgs += @("--before", $Before) }
& py @sheetArgs
if ($LASTEXITCODE -ne 0) { Write-Host "REFSPOTS: the sheet failed" -ForegroundColor Red; exit 1 }
Write-Host "REFSPOTS DONE -> $Out"
exit 0
