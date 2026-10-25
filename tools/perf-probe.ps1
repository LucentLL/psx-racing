# THE FRAME-RATE PROBE (Assets\PSXRacing\Editor\PerfProbe.cs): a Charlotte
# race at night in the rain, played on autopilot, 60 s in COCKPIT and 60 s in
# CHASE, uncapped - frame times, spikes, GC a frame, every camera's cost -
# then the editor profiler read back marker by marker, then an A/B of each
# suspect (mirrors, reflections, rain, far city, skyline, wet road, blur).
#
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXCity'
#   powershell -ExecutionPolicy Bypass -File tools\perf-probe.ps1 -Tag before
#
# Knobs (environment): PSX_PERF_VENUE (UptownLoop), PSX_PERF_CAR (charger_70),
# PSX_PERF_HOUR (Night), PSX_PERF_WEATHER (Rain), PSX_PERF_SECONDS (60),
# PSX_PERF_SEED (1), PSX_PERF_PROFILE (1), PSX_PERF_AB_SECONDS (10).
# Code + the car art only (/E /XO for art and Resources: never older over
# newer); the sandbox's scenes must be built. Report:
# <sandbox>\PSXRacing_perf_probe_<Tag>.txt
param([string]$Tag = "run", [switch]$NoWatch, [switch]$NoSync, [int]$MaxMinutes = 25)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if (-not $NoSync) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Resources", "Assets\PSXRacing\Art\Car\Models", "Assets\PSXRacing\Materials\CarModels",
                     "Assets\PSXRacing\Generated\CarModels", "Assets\PSXRacing\Art\City\Facade", "Assets\PSXRacing\CockpitSrc")) {
        if (Test-Path "$src\$d") {
            robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
        }
    }
}
Remove-Item "$proj\PSXRacing_perf_probe.txt" -ErrorAction SilentlyContinue
Remove-Item "$proj\PSXRacing_perf_probe_$Tag.txt" -ErrorAction SilentlyContinue
$env:PSX_PERF_TAG = $Tag

Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\perfprobe_$Tag.log" -MaxMinutes $MaxMinutes -UnityArgs @(
    "-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PerfProbe.Run",
    "-logFile","$proj\perfprobe_$Tag.log","-accept-apiupdate") | Out-Null

Select-String -Path "$proj\perfprobe_$Tag.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_perf_probe_$Tag.txt") {
    Get-Content "$proj\PSXRacing_perf_probe_$Tag.txt"
    exit 0
}
"NO REPORT - the run threw. Tail of perfprobe_$Tag.log:"
Get-Content "$proj\perfprobe_$Tag.log" -Tail 40
exit 1
