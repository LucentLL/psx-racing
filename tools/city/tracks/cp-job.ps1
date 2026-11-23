# cp-job.ps1 - ONE Unity job of the CHECKPOINT RACES round in its sandbox, under the sandbox lock. Pure ASCII.
#
#   $j = "<worktree>\tools\city\tracks\cp-job.ps1"
#   powershell -ExecutionPolicy Bypass -File $j -Sandbox C:\Users\mcgee\PSXCity -Mode cpselftest
#        batch: Editor/CheckpointSelfTest (every Resources/CityCheckpoints file: load, hash, reachability, node
#        parity, the ruler along the suggested route, routed paths, timings)
#   ... -Mode race -Venue I485West [-Seconds 900] [-Hour morning|night] [-Weather rain] [-Traffic NONE|LIGHT]
#        [-Field 8] [-Laps 2] [-CpRaces 1|0] [-CpTest reset] [-Shots] [-NoWatch]
#        a WATCHED race-play-check -Finish (every car home) in checkpoint mode (PSX_CP_RACES=1) or its fixed-line
#        base (-CpRaces 0); -Shots: the play check's race frames (PSX_RACE_SHOTS) into -Out
#   -Out <folder>: where reports are copied (default <sandbox>\CpJob)
#
# In order: the lock (a random 0.5-4 s pause; no lock under 90 minutes old that is not ours and no Unity.exe open
# on this sandbox; a stamped line, 3 s, read back, go only if still ours); the sync (robocopy /MIR of Scripts,
# Editor and Shaders, /MIR of Resources\CityTracks and Resources\CityCheckpoints with the worktree's metas
# winning; nothing else in Assets); the build target pinned (PSXCity WebGL); the job; the metas Unity minted for
# new files copied back to the worktree (commit them); our own lock released, whatever happened.
param(
    [Parameter(Mandatory = $true)][string]$Sandbox,
    [Parameter(Mandatory = $true)][ValidateSet("cpselftest", "race", "cards", "lifeselftest")][string]$Mode,
    [string]$Venue = "I485West", [int]$Seconds = 900, [int]$MaxMinutes = 0, [string]$Hour = "morning",
    [string]$Weather = "", [string]$Traffic = "NONE", [int]$Field = 8, [int]$Laps = 0, [string]$CpRaces = "1",
    [string]$CpTest = "", [switch]$Shots, [string]$Label = "cp", [string]$Out = "", [string]$Extra = "", [string]$Matrix = "", [switch]$Release,
    [switch]$NoWatch, [string]$Agent = "cpfix", [double]$TimeScale = 1
)
$ErrorActionPreference = "Stop"
$wt = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$Sandbox = $Sandbox.TrimEnd('\')
if (-not $Out) { $Out = Join-Path $Sandbox "CpJob" }
New-Item -ItemType Directory -Force $Out | Out-Null
. "$wt\tools\unity-wait.ps1"
$targets = @{ "psxcity" = "WebGL"; "psxperf" = "StandaloneWindows64"; "psxlab" = "StandaloneWindows64"; "psxrec" = "Android" }
$leaf = (Split-Path $Sandbox -Leaf).ToLowerInvariant()
if (-not $targets.ContainsKey($leaf)) { Write-Host "cp-job: no build target known for $Sandbox"; exit 5 }
$env:PSX_BUILD_TARGET = $targets[$leaf]
$env:PSX_SANDBOX = $Sandbox
$env:PSX_CITY_TRACKS = "1"
Remove-Item Env:PSX_TRACK_CLOSURES -ErrorAction SilentlyContinue

# ---- the lock, taken atomically
$lock = Join-Path $Sandbox "AGENT_LOCK.txt"
Start-Sleep -Milliseconds (Get-Random -Minimum 500 -Maximum 4000)
if (Test-Path $lock) {
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    $who = ((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim()
    if ($age -lt 90 -and -not $who.StartsWith("$Agent ")) { Write-Host "LOCKED by '$who' ($([int]$age) min): try later"; exit 2 }
}
$pat = [regex]::Escape($Sandbox.ToLowerInvariant()) + '(["\\/\s]|$)'
$busy = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and ($_.CommandLine.ToLowerInvariant().Replace('/', '\') -match $pat) })
if ($busy.Count -gt 0) { Write-Host "BUSY: a Unity editor is open on $Sandbox (pid $($busy[0].ProcessId)); never close it, try later"; exit 4 }
$mine = "$Agent " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " $Mode $Venue #" + (Get-Random -Minimum 100000 -Maximum 999999)
Set-Content -Path $lock -Value $mine -Encoding ascii
Start-Sleep -Seconds 3
$back = ((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim()
if ($back -ne $mine) { Write-Host "LOST the lock to '$back': try later"; exit 3 }
Write-Host ("LOCK OK " + (Get-Date -Format "HH:mm:ss") + " $Sandbox ($env:PSX_BUILD_TARGET): $Mode $Venue")
$code = 0
$dataDirs = @("Assets\PSXRacing\Resources\CityTracks", "Assets\PSXRacing\Resources\CityCheckpoints")
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$wt\$d" "$Sandbox\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($td in $dataDirs) {
        if (-not (Test-Path "$wt\$td")) { continue }
        robocopy "$wt\$td" "$Sandbox\$td" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        robocopy "$wt\$td" "$Sandbox\$td" *.meta /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        if (Test-Path "$wt\$td.meta") { Copy-Item "$wt\$td.meta" "$Sandbox\$td.meta" -Force }
    }
    $mm = if ($MaxMinutes -gt 0) { $MaxMinutes } else { 30 }

    if ($Mode -eq "cpselftest") {
        $rep = "$Sandbox\PSXRacing_checkpoint_selftest.txt"
        Remove-Item $rep -ErrorAction SilentlyContinue
        $log = "$Sandbox\cpselftest.log"
        # -Release: the editor's scripts compiled with RELEASE code optimisation (closer to a player's timings)
        [string[]]$opt = if ($Release) { @("-releaseCodeOptimization") } else { @() }
        $ok = Invoke-UnityJob -Log $log -MaxMinutes $mm -UnityArgs (@("-quit", "-batchmode", "-nographics", "-projectPath", $Sandbox) + $opt + @(
            "-executeMethod", "PSXRacing.EditorTools.CheckpointSelfTest.Run", "-logFile", $log, "-accept-apiupdate"))
        Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.Line }
        $tag = if ($Release) { "_release" } else { "" }
        if (Test-Path $rep) { Copy-Item $rep "$Out\cp_selftest$tag.txt" -Force; Get-Content $rep }
        else { Write-Host "NO REPORT"; Get-Content $log -Tail 25; $code = 1 }
    }
    elseif ($Mode -eq "cards") {
        # the race cards (CityMapPreview: every city venue's card over the whole-city map), checkpoint mode on
        $env:PSX_CP_RACES = $CpRaces
        $shotDir = Join-Path $Sandbox "Screenshots"
        Get-ChildItem $shotDir -Filter "citymap_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
        $log = "$Sandbox\cp_cards.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes $mm -UnityArgs @("-quit", "-batchmode", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.CityMapPreview.Capture", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS|Exception" -ErrorAction SilentlyContinue | Select-Object -First 6 | ForEach-Object { $_.Line }
        New-Item -ItemType Directory -Force "$Out\cards_$Label" | Out-Null
        Get-ChildItem $shotDir -Filter "citymap_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination "$Out\cards_$Label" -Force
        Write-Host ("cards: " + (Get-ChildItem "$Out\cards_$Label" -Filter "citymap_*.png").Count + " pictures in $Out\cards_$Label")
    }
    elseif ($Mode -eq "lifeselftest") {
        # the LifeSim self-test (batch), on the code's defaults plus -CpRaces
        $env:PSX_CP_RACES = $CpRaces
        $rep = "$Sandbox\PSXRacing_selftest_log.txt"
        Remove-Item $rep -ErrorAction SilentlyContinue
        $log = "$Sandbox\cp_lifeselftest.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes ([Math]::Max($mm, 30)) -UnityArgs @("-quit", "-batchmode", "-nographics", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.LifeSimSelfTest.Run", "-logFile", $log, "-accept-apiupdate")
        if (Test-Path $rep) {
            Copy-Item $rep "$Out\lifeselftest_$Label.txt" -Force
            $fails = @(Select-String -Path $rep -Pattern "^\s*FAIL" | ForEach-Object { $_.Line.Trim() })
            Write-Host ("life self-test: " + $fails.Count + " FAIL lines; " + ((Get-Content $rep -Tail 1) + ""))
            $fails | Select-Object -First 30 | ForEach-Object { "  " + $_ }
        }
        else { Write-Host "NO REPORT"; Get-Content $log -Tail 20; $code = 1 }
    }
    elseif ($Mode -eq "race") {
        $env:PSX_CP_RACES = $CpRaces
        $env:PSX_CP_TEST = $CpTest
        $env:PSX_RACE_LAPS = if ($Laps -gt 0) { "$Laps" } else { "" }
        $env:PSX_RACE_FIELD = if ($Field -gt 0) { "$Field" } else { "" }
        $env:PSX_WEATHER = $Weather
        $env:PSX_WATCH = if ($NoWatch) { "0" } else { "1" }
        $env:PSX_RACE_SHOTS = if ($Shots) { "$Out\shots_$Label" } else { "" }
        $env:PSX_RACE_SHOTTAG = $Label
        if ($Extra) { foreach ($kv in $Extra.Split(';')) { $p = $kv.Split('='); if ($p.Count -eq 2) { Set-Item -Path ("Env:" + $p[0].Trim()) -Value $p[1].Trim() } } }
        $a = @("-ExecutionPolicy", "Bypass", "-File", "$wt\tools\race-play-check.ps1", "-Venue", $Venue, "-Seconds", "$Seconds",
               "-Finish", "-Traffic", $Traffic, "-Hour", $Hour, "-TimeScale", "$TimeScale")
        if ($NoWatch) { $a += "-NoWatch" }
        if ($MaxMinutes -gt 0) { $a += @("-MaxMinutes", "$MaxMinutes") }
        # several races in ONE editor session ("Venue:TRAFFIC:seed;..."): each race's report appended, one SUMMARY
        # line each in PSXRacing_race_matrix.txt (cpfix 11-17)
        if ($Matrix) { $a += @("-Matrix", $Matrix); Remove-Item "$Sandbox\PSXRacing_race_matrix.txt" -ErrorAction SilentlyContinue }
        $said = @(& powershell @a 2>&1 | ForEach-Object { "$_" })
        if ($Matrix -and (Test-Path "$Sandbox\PSXRacing_race_matrix.txt")) { Copy-Item "$Sandbox\PSXRacing_race_matrix.txt" "$Out\matrix_$Label.txt" -Force }
        $said | Out-File -FilePath "$Out\race_${Label}_console.txt" -Encoding ascii
        $said | Where-Object { $_ -match "^(WATCH|UNITY|TIMEOUT|NO REPORT)" } | Select-Object -Last 12 | ForEach-Object { $_ }
        $rep = "$Sandbox\PSXRacing_race_play_check.txt"
        if (Test-Path $rep) {
            Copy-Item $rep "$Out\race_$Label.txt" -Force
            Select-String -Path $rep -Pattern "FAIL|ALL PASS|CHECKPOINT RACE|^\s*CP |MISSED|FLICKER|POSITION vs|PROGRESS FELL|RESET|WRONG|RETIRED|FINISHED" | Select-Object -First 60 | ForEach-Object { $_.Line }
        }
        else { Write-Host "NO REPORT"; Get-Content "$Sandbox\raceplay.log" -Tail 25 -ErrorAction SilentlyContinue; $code = 1 }
    }

    # ---- the metas Unity minted for new files, back to the worktree (commit them)
    foreach ($d in @("Assets\PSXRacing\Scripts\Checkpoints", "Assets\PSXRacing\Editor") + $dataDirs) {
        # only this round's own Editor files (a meta Unity minted here for some other branch's file must not travel)
        $flt = if ($d -like "*\Editor") { "*.cs.meta" } else { "*.meta" }
        foreach ($m in @(Get-ChildItem "$Sandbox\$d" -Filter $flt -ErrorAction SilentlyContinue | Where-Object { $d -notlike "*\Editor" -or $_.Name -like "Checkpoint*" -or $_.Name -like "LaneUseWatch*" })) {
            if (-not (Test-Path "$wt\$d\$($m.Name)") -and (Test-Path ("$wt\$d\" + $m.Name.Substring(0, $m.Name.Length - 5)))) {
                Copy-Item $m.FullName "$wt\$d\$($m.Name)"; Write-Host "meta back: $d\$($m.Name)"
            }
        }
        if ((Test-Path "$Sandbox\$d.meta") -and -not (Test-Path "$wt\$d.meta")) { Copy-Item "$Sandbox\$d.meta" "$wt\$d.meta"; Write-Host "meta back: $d.meta" }
    }
}
finally {
    $now = ((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim()
    if ($now -eq $mine) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
    elseif ($now) { Write-Host "the lock is no longer ours at the end: '$now' (left in place)" }
}
exit $code
