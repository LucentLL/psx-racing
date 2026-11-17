# track-job.ps1 - ONE Unity job of a Charlotte track lane in ITS sandbox, under the sandbox lock.
# Pure ASCII. Run from anywhere; the worktree is the one this script sits in.
#
#   $j = "<worktree>\tools\city\tracks\track-job.ps1"
#   powershell -ExecutionPolicy Bypass -File $j -Agent lane2 -Sandbox C:\Users\mcgee\PSXPerf -Mode scenes
#        rebuild the four city scenes with this branch's code (CityTracksRegistrar on each); once per sandbox
#   ... -Mode closures                       batch: every city route, sanctioned closures, 0 on the race line
#   ... -Mode race -Venue CltD1 [-Seconds 420] [-MaxMinutes 0]
#        WATCHED race-play-check: sanctioned (closures built), -Traffic NONE, -Finish (every rival home)
#   ... -Mode cards                          the whole-city map tiles (citymap-bake) + every city race card
#   ... -Mode eye -Spots "a,lat,lon,road,hdg;b,lat,lon,road,hdg"   driver-eye frames, day and night
#        (node tools/city/tracks/spots.mjs <route id> prints -Spots for the grid line and the middle)
#   ... -Mode selftest                       the LifeSim self-test on the code's defaults (no switch set)
#   -Out <folder>: where reports and pictures are copied (default <sandbox>\TrackJob)
#
# In order: the lock (a random 0.5-4 s pause; no lock under 90 minutes old that is not this agent's and
# no Unity.exe open on this sandbox; write a stamped line, wait 3 s, read it back, go only if it is still
# ours); the sync (robocopy /MIR of Scripts, Editor and Shaders, /MIR of Resources\CityTracks with the
# worktree's metas winning; nothing else in Assets is touched); the build target pinned for the sandbox
# (PSXCity WebGL, PSXPerf and PSXLab StandaloneWindows64, PSXRec Android); the job; the metas Unity minted
# for new track files copied back to the worktree (commit them); our own lock released, whatever happened.
param(
    [Parameter(Mandatory = $true)][string]$Agent,
    [Parameter(Mandatory = $true)][string]$Sandbox,
    [Parameter(Mandatory = $true)][ValidateSet("closures", "race", "cards", "eye", "scenes", "selftest")][string]$Mode,
    [string]$Venue = "", [int]$Seconds = 420, [int]$MaxMinutes = 0, [string]$Spots = "", [string]$Label = "track",
    [string]$Out = "",
    # ONLY to re-run a race whose watched editor stopped on Unity's "Revert All Window Layouts" dialog
    # (never press Continue there): the same race, hidden. Play tests are watched otherwise (owner's rule).
    [switch]$NoWatch
)
$ErrorActionPreference = "Stop"
$wt = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$Sandbox = $Sandbox.TrimEnd('\')
if (-not $Out) { $Out = Join-Path $Sandbox "TrackJob" }
New-Item -ItemType Directory -Force $Out | Out-Null
. "$wt\tools\unity-wait.ps1"

# ---- the build target of this sandbox (unity-wait.ps1 defaults to WebGL)
$targets = @{ "psxcity" = "WebGL"; "psxperf" = "StandaloneWindows64"; "psxlab" = "StandaloneWindows64"; "psxrec" = "Android" }
$leaf = (Split-Path $Sandbox -Leaf).ToLowerInvariant()
if (-not $targets.ContainsKey($leaf)) { Write-Host "track-job: no build target known for $Sandbox (PSXCity, PSXPerf, PSXLab, PSXRec)"; exit 5 }
$env:PSX_BUILD_TARGET = $targets[$leaf]
$env:PSX_SANDBOX = $Sandbox
$env:PSX_CITY_TRACKS = "1"
# the self-test runs on the code's own defaults (no switch set), as the game ships
if ($Mode -eq "selftest") { Remove-Item Env:PSX_CITY_TRACKS, Env:PSX_TRACK_CLOSURES -ErrorAction SilentlyContinue }

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
try {
    # ---- the sync: code, and the tracks' own data folder
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$wt\$d" "$Sandbox\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $td = "Assets\PSXRacing\Resources\CityTracks"
    robocopy "$wt\$td" "$Sandbox\$td" /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    robocopy "$wt\$td" "$Sandbox\$td" *.meta /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if (Test-Path "$wt\$td.meta") { Copy-Item "$wt\$td.meta" "$Sandbox\$td.meta" -Force }
    # the single tracks file of before 2026-11-16 19:00: nothing reads it any more
    foreach ($old in @("charlotte_tracks.json", "charlotte_tracks.json.meta")) {
        Remove-Item "$Sandbox\Assets\PSXRacing\Resources\$old" -Force -ErrorAction SilentlyContinue
    }
    $mm = if ($MaxMinutes -gt 0) { $MaxMinutes } else { 30 }

    if ($Mode -eq "scenes") {
        # a sandbox built with PC content keeps it (the city tree kit is deleted by any other pass)
        if (Test-Path "$Sandbox\Assets\PSXRacing\Resources\CityTreeKit.asset") { $env:PSX_PC_CONTENT = "1" } else { Remove-Item Env:PSX_PC_CONTENT -ErrorAction SilentlyContinue }
        Write-Host "PSX_PC_CONTENT=$env:PSX_PC_CONTENT"
        $log = "$Sandbox\citybuild_tracks.log"
        Remove-Item "$Sandbox\PSXRacing_city_build_log.txt" -ErrorAction SilentlyContinue
        $t0 = Get-Date
        $ok = Invoke-UnityJob -Log $log -MaxMinutes ([Math]::Max($mm, 40)) -UnityArgs @("-quit", "-batchmode", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.PSXRacingBuilder.BuildCityScenesOnly", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS|CITY BUILD FAILED" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.Line }
        $done = (Test-Path "$Sandbox\PSXRacing_city_build_log.txt") -and (Select-String -Path "$Sandbox\PSXRacing_city_build_log.txt" -Pattern "CITY BUILD OK" -Quiet)
        Write-Host ("scenes: " + $(if ($done) { "CITY BUILD OK" } else { "FAILED" }) + " in " + [int]((Get-Date) - $t0).TotalMinutes + " min")
        if (-not $done) { $code = 1 }
    }
    elseif ($Mode -eq "selftest") {
        # the LifeSim self-test (batch, about 5 minutes), on the code's defaults; its FAIL lines are judged
        # against the base's: a new one that names a track, CityTracks or a host scene is ours
        $rep = "$Sandbox\PSXRacing_selftest_log.txt"
        Remove-Item $rep -ErrorAction SilentlyContinue
        $log = Join-Path $Sandbox "trackselftest.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes ([Math]::Max($mm, 30)) -UnityArgs @("-quit", "-batchmode", "-nographics", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.LifeSimSelfTest.Run", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.Line }
        if (Test-Path $rep) {
            Copy-Item $rep "$Out\selftest.txt" -Force
            $fails = @(Select-String -Path $rep -Pattern "^\s*FAIL" | ForEach-Object { $_.Line.Trim() })
            Write-Host ("self-test: " + $fails.Count + " FAIL lines; " + ((Get-Content $rep -Tail 1) + ""))
            $fails | Where-Object { $_ -match "Clt|I485|CityTrack|hostScene|host scene|charlotte_tracks|Rev " } | ForEach-Object { "  " + $_ }
        }
        else { Write-Host "NO REPORT"; Get-Content $log -Tail 20; $code = 1 }
    }
    elseif ($Mode -eq "closures") {
        $rep = "$Sandbox\PSXRacing_closures_check.txt"
        Remove-Item $rep -ErrorAction SilentlyContinue
        $log = "$Sandbox\closurescheck.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes $mm -UnityArgs @("-quit", "-batchmode", "-nographics", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.CityClosuresCheck.Run", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.Line }
        if (Test-Path $rep) { Copy-Item $rep "$Out\closures_check.txt" -Force; Get-Content $rep }
        else { Write-Host "NO REPORT"; Get-Content $log -Tail 20; $code = 1 }
        if ((Test-Path $rep) -and -not (Select-String -Path $rep -Pattern "ALL CLEAR" -Quiet)) { $code = 1 }
    }
    elseif ($Mode -eq "race") {
        if (-not $Venue) { Write-Host "race: -Venue <id> (CltD1, I485WestRev ...)"; $code = 2; return }
        # a SANCTIONED event: its closures stand (a play check carries no event, =1 builds them for every city race)
        $env:PSX_TRACK_CLOSURES = "1"
        $env:PSX_WATCH = if ($NoWatch) { "0" } else { "1" }
        $a = @("-ExecutionPolicy", "Bypass", "-File", "$wt\tools\race-play-check.ps1", "-Venue", $Venue, "-Seconds", "$Seconds", "-Finish", "-Traffic", "NONE")
        if ($NoWatch) { $a += "-NoWatch" }
        if ($MaxMinutes -gt 0) { $a += @("-MaxMinutes", "$MaxMinutes") }
        $said = @(& powershell @a 2>&1 | ForEach-Object { "$_" })
        $said | Out-File -FilePath "$Out\race_${Venue}_console.txt" -Encoding ascii
        # the watch helper's own lines (why an editor was closed, a dialog, a stall)
        $said | Where-Object { $_ -match "^(WATCH|UNITY|TIMEOUT|NO REPORT)" } | Select-Object -Last 12 | ForEach-Object { $_ }
        $rep = "$Sandbox\PSXRacing_race_play_check.txt"
        if (Test-Path $rep) {
            Copy-Item $rep "$Out\race_$Venue.txt" -Force
            Select-String -Path $rep -Pattern "FAIL|FAILURE|ALL PASS|SUMMARY $Venue|wrong way|closure|stuck|retired" | Select-Object -First 30 | ForEach-Object { $_.Line }
        }
        else { Write-Host "NO REPORT"; Get-Content "$Sandbox\raceplay.log" -Tail 25 -ErrorAction SilentlyContinue; $code = 1 }
    }
    elseif ($Mode -eq "cards") {
        & powershell -ExecutionPolicy Bypass -File "$wt\tools\citymap-bake.ps1" -Project $Sandbox 2>&1 | Select-Object -Last 3
        $shots = "$Sandbox\Screenshots"
        Get-ChildItem $shots -Filter "citymap_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
        $log = "$Sandbox\citymap_cards.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes $mm -UnityArgs @("-quit", "-batchmode", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.CityMapPreview.Capture", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS|Exception" -ErrorAction SilentlyContinue | Select-Object -First 6 | ForEach-Object { $_.Line }
        New-Item -ItemType Directory -Force "$Out\cards" | Out-Null
        Get-ChildItem $shots -Filter "citymap_*.png" -ErrorAction SilentlyContinue | Copy-Item -Destination "$Out\cards" -Force
        Write-Host ("cards: " + (Get-ChildItem "$Out\cards" -Filter "citymap_*.png").Count + " pictures in $Out\cards")
    }
    elseif ($Mode -eq "eye") {
        if (-not $Spots) { Write-Host "eye: -Spots ""name,lat,lon,road,hdg;..."" (node tools/city/tracks/spots.mjs <route id>)"; $code = 2; return }
        $env:PSX_POLE_EXTRA = $Spots
        $env:PSX_POLE_SPOTS = (($Spots.Split(';') | Where-Object { $_ } | ForEach-Object { $_.Split(',')[0].Trim() }) -join ',')
        $env:PSX_POLE_LABEL = $Label
        $log = "$Sandbox\trackeye.log"
        $ok = Invoke-UnityJob -Log $log -MaxMinutes $mm -UnityArgs @("-quit", "-batchmode", "-projectPath", $Sandbox,
            "-executeMethod", "PSXRacing.EditorTools.CityRefSpots.RunPoles", "-logFile", $log, "-accept-apiupdate")
        Select-String -Path $log -Pattern "error CS|Exception|\[CityRefSpots\]" -ErrorAction SilentlyContinue | Select-Object -First 6 | ForEach-Object { $_.Line }
        New-Item -ItemType Directory -Force "$Out\eye" | Out-Null
        foreach ($n in $env:PSX_POLE_SPOTS.Split(',')) {
            Get-ChildItem "$Sandbox\Screenshots\City\poles" -Filter ($n + "_" + $Label + "_*.png") -ErrorAction SilentlyContinue | Copy-Item -Destination "$Out\eye" -Force
        }
        Write-Host ("eye: " + (Get-ChildItem "$Out\eye" -Filter "*.png").Count + " pictures in $Out\eye")
    }

    # ---- the metas Unity minted for new track files, back to the worktree (commit them)
    foreach ($m in @(Get-ChildItem "$Sandbox\$td" -Filter "*.meta" -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path "$wt\$td\$($m.Name)")) { Copy-Item $m.FullName "$wt\$td\$($m.Name)"; Write-Host "meta back: $td\$($m.Name)" }
    }
    if ((Test-Path "$Sandbox\$td.meta") -and -not (Test-Path "$wt\$td.meta")) { Copy-Item "$Sandbox\$td.meta" "$wt\$td.meta"; Write-Host "meta back: $td.meta" }
}
finally {
    # release only our own lock, never another agent's
    $now = ((Get-Content $lock -Raw -ErrorAction SilentlyContinue) + "").Trim()
    if ($now -eq $mine) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
    elseif ($now) { Write-Host "the lock is no longer ours at the end: '$now' (left in place)" }
}
exit $code
