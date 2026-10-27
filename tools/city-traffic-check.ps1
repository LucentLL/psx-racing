# Play Charlotte free roam with the city's own traffic (CityTraffic) and
# measure it: rush hour and 03:00, parked and driving, the frame time with and
# without, a STOP-sign site and one real hit. Editor/CityTrafficCheck.cs.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-traffic-check.ps1
#   ... -NoWatch     hidden, -batchmode -nographics (no pictures)
#   ... -CityPlay    then run Editor/CityPlayCheck too (spawns, race grid)
#
# SANDBOX RULES (the publish sandbox is shared): waits for another agent's
# AGENT_LOCK.txt (younger than 90 min) to go, writes its own, mirrors ONLY
# Scripts / Editor / Shaders from this checkout (never Resources or Art: a
# fresh checkout's mtimes would overwrite the sandbox's minted .meta GUIDs),
# runs, and deletes the lock even on failure. Pictures are copied to
# Docs\shots\citytraffic\ (not committed when large).
#
# Exit code 0 = the report has no FAIL.
param([switch]$NoWatch, [int]$MaxMinutes = 40, [switch]$CityPlay, [switch]$SkipTraffic)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
$lock = Join-Path $proj "AGENT_LOCK.txt"
$me = "city-traffic"

function Wait-Lock {
    while (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
        if ($txt.StartsWith($me) -or $age -gt 90) { break }
        "sandbox locked by '$txt' ($([int]$age) min) - waiting"
        Start-Sleep -Seconds 120
    }
}

Wait-Lock
"$me $(Get-Date -Format s)" | Out-File -FilePath $lock -Encoding ascii
$code = 1
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $code = 0
    if (-not $SkipTraffic) {
        Remove-Item "$proj\PSXRacing_city_traffic_check.txt" -ErrorAction SilentlyContinue
        Get-ChildItem "$proj\Screenshots\citytraffic_*.png" -ErrorAction SilentlyContinue | Remove-Item
        $env:PSX_EDITION = "ALL"
        Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\citytraffic.log" -MaxMinutes $MaxMinutes -UnityArgs @(
            "-batchmode","-nographics","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.CityTrafficCheck.Run",
            "-logFile","$proj\citytraffic.log","-accept-apiupdate") | Out-Null
        Select-String -Path "$proj\citytraffic.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
        if (Test-Path "$proj\PSXRacing_city_traffic_check.txt") {
            Get-Content "$proj\PSXRacing_city_traffic_check.txt"
            if (Select-String -Path "$proj\PSXRacing_city_traffic_check.txt" -Pattern "FAIL" -Quiet) { $code = 1 }
        } else {
            "NO REPORT - the run threw. Tail of citytraffic.log:"
            Get-Content "$proj\citytraffic.log" -Tail 40
            $code = 1
        }
        $shots = Join-Path $src "Docs\shots\citytraffic"
        New-Item -ItemType Directory -Force $shots | Out-Null
        Get-ChildItem "$proj\Screenshots\citytraffic_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination $shots
    }
    if ($CityPlay) {
        Remove-Item "$proj\PSXRacing_city_play_check.txt" -ErrorAction SilentlyContinue
        $env:PSX_EDITION = "ALL"
        Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\cityplaycheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
            "-batchmode","-nographics","-projectPath",$proj,
            "-executeMethod","PSXRacing.EditorTools.CityPlayCheck.Run",
            "-logFile","$proj\cityplaycheck.log","-accept-apiupdate") | Out-Null
        if (Test-Path "$proj\PSXRacing_city_play_check.txt") {
            Get-Content "$proj\PSXRacing_city_play_check.txt"
            if (Select-String -Path "$proj\PSXRacing_city_play_check.txt" -Pattern "FAIL" -Quiet) { $code = 1 }
        } else {
            "NO CITY PLAY REPORT. Tail of cityplaycheck.log:"
            Get-Content "$proj\cityplaycheck.log" -Tail 40
            $code = 1
        }
    }
} finally {
    if (Test-Path $lock) {
        $txt = (Get-Content $lock -Raw).Trim()
        if ($txt.StartsWith($me)) { Remove-Item $lock -Force }
    }
}
exit $code
