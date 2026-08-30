# Rebuild PSX Racing and publish it to https://lucentll.github.io/psx-racing/
#
#   powershell -ExecutionPolicy Bypass -File tools\build-and-publish.ps1
#   ...            -File tools\build-and-publish.ps1 -SkipBuild    (republish last build)
#   ...            -File tools\build-and-publish.ps1 -SkipDeploy   (build only)
#
# Builds happen in a SANDBOX COPY rather than this project, for two reasons:
# the Unity editor holds a lock on an open project, and a WebGL build would tie
# it up for several minutes. The sandbox also lives on a short path because
# IL2CPP fails on long ones.
param([switch]$SkipBuild, [switch]$SkipDeploy)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$src   = Split-Path -Parent $PSScriptRoot
$proj  = "C:\Users\mcgee\PSXBuild"
$pages = "C:\Users\mcgee\psx-pages"
$repo  = "https://github.com/LucentLL/psx-racing.git"

# Unity.exe is a launcher: it spawns the real editor and returns immediately, so
# waiting on the call itself reads stale logs. Wait on the actual child PIDs.
# Run git and judge it by its EXIT CODE.
#
# See the note at the deploy stage: PowerShell treats a native command's stderr
# as errors, so git's routine chatter — line-ending warnings, "Everything
# up-to-date", push progress — reads as a failure and stops the script. Calling
# through cmd folds stderr into stdout before PowerShell can see a separate
# stream to be upset about.
function Invoke-Git([string[]]$GitArgs) {
    $line = ($GitArgs | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $out = & cmd /c "git $line 2>&1"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git $line failed ($LASTEXITCODE):" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}

function Invoke-UnityWait([string[]]$UnityArgs, [int]$MaxMinutes = 40) {
    $before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-Process -FilePath $unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 5
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    while ((Get-Date) -lt $deadline) {
        $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (-not @($now | Where-Object { $before -notcontains $_ })) { return $true }
        Start-Sleep -Seconds 5
    }
    return $false
}

if (-not $SkipBuild) {
    # Stamped BEFORE anything runs, so the freshness check below can ask the
    # only question that matters — "is this output from THIS run?" — instead of
    # guessing from how long ago it was written.
    $runStart = Get-Date
    Write-Host "[1/3] Mirroring project to sandbox..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $proj | Out-Null
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        # Never mirror Library: copying one from a live editor corrupts the
        # artifact database. The sandbox builds its own and keeps it warm.
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }

    # Delete the success marker BEFORE building. It is the only thing the check
    # below looks at, so a leftover one from an earlier run certifies a build
    # that failed - which is exactly what happened on 2026-08-23: IL2CPP died,
    # the script printed "WebGL build succeeded", and the deploy shipped the
    # previous build's output while reporting the new one as live.
    Remove-Item "$proj\Build\WebGL\build_ok.txt" -Force -ErrorAction SilentlyContinue

    Write-Host "[2/3] Generating scene + building WebGL..." -ForegroundColor Cyan
    # Retries, because a cold or reimporting sandbox kills the editor partway
    # through the 560 engine-audio clips - no error, the process just stops.
    # Every pass gets further, and the marker file is deleted first so a stale
    # BUILD OK cannot certify a build that never ran, which would ship the
    # PREVIOUS set of circuits inside a player reporting itself as new.
    if (-not (Invoke-SceneBuild -Proj $proj)) {
        Write-Host "SCENE BUILD FAILED - not building a player around stale scenes." -ForegroundColor Red
        Get-Content "$proj\scenebuild.log" -Tail 6
        exit 1
    }
    Get-Content "$proj\PSXRacing_build_log.txt" -Tail 3

    # Same waiter as everything else now: the local Invoke-UnityWait sleeps five
    # seconds and then polls once for a new Unity PID, which is a race the child
    # editor loses under load — and losing it here means checking build_ok.txt
    # while IL2CPP is still running.
    Invoke-UnityJob -Log "$proj\build.log" -MaxMinutes 45 -UnityArgs @(
        "-quit","-batchmode","-nographics","-projectPath",$proj,
        "-buildTarget","WebGL",
        "-executeMethod","PSXRacing.EditorTools.PSXBuildWebGL.BuildFromCommandLine",
        "-logFile","$proj\build.log","-accept-apiupdate") | Out-Null

    if (-not (Test-Path "$proj\Build\WebGL\build_ok.txt")) {
        Write-Host "BUILD FAILED - see $proj\build.log" -ForegroundColor Red
        Select-String -Path "$proj\build.log" -Pattern "IL2CPP error|Error building Player|error CS" |
            Select-Object -First 6 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
        exit 1
    }
    Get-Content "$proj\Build\WebGL\build_ok.txt"

    # Belt and braces: a marker can be fresh while the player output is not, so
    # check the player itself.
    #
    # Against $runStart, NOT against a wall-clock age. The age version refused a
    # perfectly good 2026-08-24 build: the output was complete at 18:19 and the
    # check ran at 18:30, because Invoke-UnityJob waits for every Unity PID it
    # did not start to disappear AND STAY gone, and something in the WebGL
    # toolchain lingered for eleven minutes after the last file was written. How
    # long the waiter takes to notice is not evidence about the build, and any
    # fixed threshold is a race between two unrelated durations.
    # ANY of the four payload files, not WebGL.wasm specifically.
    #
    # A scene-only change - which is most terrain, track and builder work,
    # since all of that lives in Editor code that never ships - leaves the
    # player assemblies byte-identical, so Unity correctly reuses the cached
    # wasm/framework/loader and rewrites only WebGL.data. Asking the wasm
    # whether the build ran therefore rejected a complete and correct build of
    # the Blue Ridge terrain fix on 2026-08-27. The honest question is whether
    # this run produced ANY output; build_ok.txt is excluded because the script
    # deletes and rewrites it itself, so it would always answer yes.
    $outs = Get-ChildItem "$proj\Build\WebGL\Build" -File
    $fresh = @($outs | Where-Object { $_.LastWriteTime -ge $runStart })
    if ($fresh.Count -eq 0) {
        $newest = ($outs | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
        Write-Host ("STALE OUTPUT - nothing under Build/ was written by this run (newest is {0} at {1}, run began {2}). Refusing to deploy." -f $newest.Name, $newest.LastWriteTime, $runStart) -ForegroundColor Red
        exit 1
    }
    Write-Host ("Rebuilt this run: {0}" -f (($fresh | ForEach-Object { $_.Name }) -join ", "))
}

if (-not $SkipDeploy) {
    Write-Host "[3/3] Publishing to gh-pages..." -ForegroundColor Cyan
    $build = "$proj\Build\WebGL"
    if (-not (Test-Path "$build\index.html")) { Write-Host "No build to deploy." -ForegroundColor Red; exit 1 }

    Remove-Item $pages -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $pages | Out-Null
    Copy-Item "$build\index.html" $pages
    Copy-Item "$build\Build" $pages -Recurse
    if (Test-Path "$build\StreamingAssets") { Copy-Item "$build\StreamingAssets" $pages -Recurse }

    # CACHE BUSTING. Every deploy writes the same four filenames — WebGL.data,
    # WebGL.wasm, WebGL.framework.js, WebGL.loader.js — and GitHub Pages serves
    # them with caching headers, so a browser holding the previous 65 MB .data
    # happily reuses it and runs the OLD GAME. The deploy looks green, the
    # bytes on the server are correct, and the player sees the last build: this
    # shipped three new tracks that were simply invisible until a hard refresh,
    # and there was no way to tell that apart from a build that had failed.
    #
    # Stamping a version query on each URL makes every deploy a distinct URL,
    # so the browser fetches it. Stamped from the build's own timestamp, so
    # republishing the SAME build (-SkipBuild) keeps the same stamp and does
    # not force a pointless 65 MB re-download.
    $stamp = (Get-Item "$pages\Build\WebGL.data").LastWriteTimeUtc.ToString("yyyyMMddHHmmss")
    $idx = Join-Path $pages "index.html"
    $html = Get-Content $idx -Raw
    $stamped = 0
    foreach ($f in @("WebGL.data", "WebGL.wasm", "WebGL.framework.js", "WebGL.loader.js")) {
        # Pattern into a variable, NOT inlined: `-replace [regex]::Escape(..) +
        # "(?!\?)", ..` parses its operands ambiguously and silently replaced
        # nothing, which printed a stamp and shipped an unstamped page.
        $needle = "Build/$f"
        $sub    = "Build/$f" + "?v=$stamp"
        if ($html.Contains($needle + "?")) { continue }   # already stamped
        if (-not $html.Contains($needle)) { Write-Host "  WARN: $needle not in index.html"; continue }
        $html = $html.Replace($needle, $sub)
        $stamped++
    }
    Set-Content $idx $html -Encoding utf8 -NoNewline
    # Verified rather than assumed, for the same reason the stamp exists at
    # all: a cache-bust that quietly does nothing is indistinguishable from a
    # deploy that worked.
    $check = (Get-Content $idx -Raw)
    if ($stamped -lt 4 -or $check -notmatch [regex]::Escape("WebGL.data?v=$stamp")) {
        Write-Host "CACHE-BUST FAILED - index.html would serve stale build." -ForegroundColor Red
        exit 1
    }
    Write-Host "  cache-bust: $stamped URLs stamped v=$stamp"
    # Without this, Pages runs Jekyll and drops anything starting with an underscore.
    New-Item -ItemType File "$pages\.nojekyll" | Out-Null

    Push-Location $pages
    # Orphan branch, force-pushed: the build is ~51 MB and committing each
    # iteration onto a normal branch would grow history by that much every time.
    #
    # Every git call goes through Invoke-Git, and that is not tidiness. Windows
    # PowerShell wraps ANY line a native exe writes to stderr in an ErrorRecord,
    # and with $ErrorActionPreference = "Stop" that terminates the script — so
    # `git add -A` printing "LF will be replaced by CRLF" killed a deploy on
    # 2026-08-30 AFTER a successful 40-minute build, with the files staged and
    # nothing committed. Exit codes are the only thing git says about failure
    # that is actually about failure.
    Invoke-Git @("init", "-q", "-b", "gh-pages")
    Invoke-Git @("config", "user.name", "LucentLL")
    Invoke-Git @("config", "user.email", "mcgeevarnell@gmail.com")
    Invoke-Git @("add", "-A")
    Invoke-Git @("commit", "-q", "-m", "Deploy PSX Racing WebGL build")
    # The remote survives a re-run of this script; adding it twice is an error
    # and not an interesting one.
    git remote add origin $repo 2>&1 | Out-Null
    Invoke-Git @("push", "-q", "-f", "origin", "gh-pages")
    Pop-Location

    Write-Host "`nLive: https://lucentll.github.io/psx-racing/" -ForegroundColor Green
    Write-Host "(Pages takes ~1 min to refresh; hard-refresh on mobile.)"
}
