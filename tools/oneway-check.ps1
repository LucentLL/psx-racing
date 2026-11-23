# oneway-check.ps1 - THE ONE-WAY SELF-TEST (tier1 11-23): no traffic car against a street's legal direction. Pure ASCII.
#
#   powershell -ExecutionPolicy Bypass -File tools\oneway-check.ps1 -Sandbox C:\Users\mcgee\PSXCity [-Agent me] [-Out dir]
#
# Two halves, both HIDDEN (batch, no window), each under the sandbox's atomic lock:
#  1. FREE ROAM (Editor/OneWayTrafficCheck): Charlotte at 17:15 with the city's own traffic, a ghost driven 50 s along
#     each of three one-way streets near uptown and one divided road; every traffic car judged by Editor/OneWayLegal
#     (its place on the city map, its heading against the street's legal direction). Report PSXRacing_oneway_check.txt.
#  2. RACES (tools\city\tracks\cp-job.ps1 -Mode race -Matrix): city races with MEDIUM traffic over one-way streets
#     (CltC1, CltC2, UptownLoop, TryonSprint by default; -Matrix to change), every race's strip traffic judged the same
#     way by LaneUseWatch (its "ONE-WAY LEGAL" note and check).
# PASS = no WRONG WAY sample in either half, the free-roam streets each carried traffic, and the races together
# covered three one-way streets and one divided road with 5+ samples each.
# -SkipRaces / -SkipFree run one half. Exit 0 = PASS.
param([Parameter(Mandatory = $true)][string]$Sandbox, [string]$Agent = "oneway", [string]$Out = "",
      [string]$Matrix = "CltC1:MEDIUM:3;CltC2:MEDIUM:1;UptownLoop:MEDIUM:0;TryonSprint:MEDIUM:0",
      [string]$Extra = "", [switch]$SkipRaces, [switch]$SkipFree, [int]$MaxMinutes = 25)
$ErrorActionPreference = "Stop"
$wt = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Sandbox = $Sandbox.TrimEnd('\')
if (-not $Out) { $Out = Join-Path $Sandbox "OneWayCheck" }
New-Item -ItemType Directory -Force $Out | Out-Null
if (Get-Process PSXRacing -ErrorAction SilentlyContinue) { Write-Host "the owner's PSXRacing.exe is running: these runs are hidden (batch), going on" }
$pass = $true

if (-not $SkipFree) {
    . "$wt\tools\unity-wait.ps1"
    $targets = @{ "psxcity" = "WebGL"; "psxperf" = "StandaloneWindows64"; "psxlab" = "StandaloneWindows64"; "psxrec" = "Android"; "psxroada" = "WebGL" }
    $leaf = (Split-Path $Sandbox -Leaf).ToLowerInvariant()
    if (-not $targets.ContainsKey($leaf)) { Write-Host "no build target known for $Sandbox"; exit 5 }
    $env:PSX_BUILD_TARGET = $targets[$leaf]
    $env:PSX_SANDBOX = $Sandbox
    $env:PSX_EDITION = "ALL"
    $lock = Join-Path $Sandbox "AGENT_LOCK.txt"
    Start-Sleep -Milliseconds (Get-Random -Minimum 500 -Maximum 4000)
    if (Test-Path $lock) {
        $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
        $who = ((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim()
        if ($age -lt 90 -and -not $who.StartsWith("$Agent ")) { Write-Host "LOCKED by '$who' ($([int]$age) min): try later"; exit 2 }
    }
    $pat = [regex]::Escape($Sandbox.ToLowerInvariant()) + '(["\\/\s]|$)'
    $busy = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and ($_.CommandLine.ToLowerInvariant().Replace('/', '\') -match $pat) })
    if ($busy.Count -gt 0) { Write-Host "BUSY: a Unity editor is open on $Sandbox; never close it, try later"; exit 4 }
    $mine = "$Agent " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " oneway-check #" + (Get-Random -Minimum 100000 -Maximum 999999)
    Set-Content -Path $lock -Value $mine -Encoding ascii
    Start-Sleep -Seconds 3
    if (((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim() -ne $mine) { Write-Host "LOST the lock: try later"; exit 3 }
    try {
        foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
            robocopy "$wt\$d" "$Sandbox\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
        }
        $rep = "$Sandbox\PSXRacing_oneway_check.txt"
        Remove-Item $rep -ErrorAction SilentlyContinue
        $log = "$Sandbox\onewaycheck.log"
        Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @("-batchmode", "-nographics", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.OneWayTrafficCheck.Run", "-logFile", $log, "-accept-apiupdate") | Out-Null
        Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.Line }
        if (Test-Path $rep) {
            Copy-Item $rep "$Out\oneway_freeroam.txt" -Force
            Get-Content $rep
            if (Select-String -Path $rep -Pattern "FAIL" -Quiet) { $pass = $false }
        }
        else { Write-Host "FREE ROAM: NO REPORT"; Get-Content $log -Tail 25; $pass = $false }
    }
    finally {
        if (((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim() -eq $mine) { Remove-Item $lock -Force }
    }
}

if (-not $SkipRaces) {
    $label = "oneway_races"
    $a = @("-ExecutionPolicy", "Bypass", "-File", "$wt\tools\city\tracks\cp-job.ps1", "-Sandbox", $Sandbox, "-Mode", "race",
           "-Venue", "TryonSprint", "-Seconds", "600", "-Traffic", "MEDIUM", "-Field", "8", "-Laps", "1", "-Label", $label,
           "-Out", $Out, "-Matrix", $Matrix, "-NoWatch", "-Agent", $Agent, "-TimeScale", "2")
    if ($Extra) { $a += @("-Extra", $Extra) }
    & powershell @a | Out-Null
    $rep = "$Out\race_$label.txt"
    if (-not (Test-Path $rep)) { Write-Host "RACES: NO REPORT (see $Out\race_${label}_console.txt)"; $pass = $false }
    else {
        $lines = Get-Content $rep
        $streets = @{}; $divided = @{}; $wrong = 0; $races = 0
        foreach ($l in $lines) {
            if ($l -match "ONE-WAY LEGAL .*WRONG WAY (\d+)") { $races++; $wrong += [int]$Matches[1] }
            if ($l -match "^\s+(one-way|divided) '(.+)': (\d+) samples, wrong (\d+)") {
                $k = $Matches[2]; $n = [int]$Matches[3]
                if ($Matches[1] -eq "divided") { $divided[$k] = $n + [int]$divided[$k] } else { $streets[$k] = $n + [int]$streets[$k] }
            }
        }
        $lines | Where-Object { $_ -match "ONE-WAY LEGAL|WRONG WAY \(|^\s+(one-way|divided) '|no traffic car against" } | ForEach-Object { $_ }
        Write-Host ("RACES: $races judged, wrong-way samples $wrong; one-way streets covered " + $streets.Count + " (" + (($streets.Keys | Sort-Object) -join ", ") +
                    "); divided roads " + $divided.Count + " (" + (($divided.Keys | Sort-Object) -join ", ") + ")")
        if ($races -eq 0 -or $wrong -gt 0 -or $streets.Count -lt 3 -or $divided.Count -lt 1) { $pass = $false }
    }
}
if ($pass) { Write-Host "ONE-WAY CHECK PASS"; exit 0 } else { Write-Host "ONE-WAY CHECK FAIL"; exit 1 }
