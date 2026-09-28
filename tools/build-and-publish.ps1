# Rebuild PSX Racing and publish it to https://lucentll.github.io/psx-racing/
#
#   powershell -ExecutionPolicy Bypass -File tools\build-and-publish.ps1
#   ...            -File tools\build-and-publish.ps1 -SkipBuild    (republish last build)
#   ...            -File tools\build-and-publish.ps1 -SkipDeploy   (build only)
#   ...            -File tools\build-and-publish.ps1 -SkipScenes   (see below)
#
# -SkipScenes builds the WebGL player from the sandbox EXACTLY AS IT STANDS:
# no mirror, no scene build. For the case where tools\verify.ps1 has just
# mirrored, built and verified every scene and nothing in the source has
# changed since -- the mirror would overwrite those fresh scenes with the
# stale source copies and the builder would spend 25 minutes making them
# again. It refuses to run without a green scene-build marker in the sandbox,
# and it is the caller's job to know that the source has not moved.
#
# Builds happen in a SANDBOX COPY rather than this project, for two reasons:
# the Unity editor holds a lock on an open project, and a WebGL build would tie
# it up for several minutes. The sandbox also lives on a short path because
# IL2CPP fails on long ones.
#
# THE PUBLISH REPLACES ITS OWN PART OF gh-pages AND KEEPS THE REST.
#
#   ...            -File tools\build-and-publish.ps1 -SkipBuild -DryRun
#                  (fetch the live gh-pages, stage the new tree, print what
#                   would be pushed, push nothing)
#   ...            -File tools\build-and-publish.ps1 -SkipBuild -BuildDir <dir>
#                  (publish a WebGL output folder other than the sandbox's)
#
# gh-pages holds more than one build: the game at the site root, and test
# builds in subfolders beside it (city/ = the Charlotte branch's test page,
# https://lucentll.github.io/psx-racing/city/). A root publish used to be
# "git init, copy the build, force-push", which wiped every subfolder. Now it
# fetches the live gh-pages tree (trees only: --filter=blob:none, so nothing
# of the 80 MB is downloaded), keeps the test folders as they are -- every
# name in -KeepDirs (default: city) and every folder holding a
# psx-subpage.txt marker -- and replaces everything else at the root with the
# new build. The result is still ONE fresh orphan commit, force-pushed, so the
# branch history never grows (the reason the orphan design exists: each build
# is ~80 MB). The force is a --force-with-lease against the commit this run
# fetched, so two publishes racing cannot silently wipe each other: the loser
# is refused, re-fetches and stages again.
#
# A TEST PAGE IN A SUBFOLDER (the Charlotte branch):
#
#   ...            -File tools\build-and-publish.ps1 -PagesDir city
#                  -> https://lucentll.github.io/psx-racing/city/
#
# -PagesDir publishes the build into that one folder of gh-pages and keeps
# EVERYTHING else exactly as it is (the game at the root, other test folders).
# The page is labelled so nobody mistakes it for the game: " - CHARLOTTE TEST"
# on the tab title and a tag on the loading screen (-PagesLabel overrides the
# text; the default for any other folder is "<FOLDER> TEST"). It also writes
# <folder>/psx-subpage.txt - the marker a root publish keeps a folder by - with
# the source branch and commit, so `curl .../city/psx-subpage.txt` says which
# build is up.
#
# SAVES DO NOT COLLIDE. Both pages share an origin (lucentll.github.io), but
# the game saves through PlayerPrefs, which Unity's WebGL runtime keeps in the
# origin's "/idbfs" IndexedDB under /idbfs/<md5 of the page's folder URL>/ -
# so /psx-racing/ and /psx-racing/city/ are two separate saves (the framework
# hands the native side document.URL; checked in a browser, see
# Docs/CHARLOTTE.md "The test page"). Shared on purpose: only the fullscreen
# preference (localStorage "psx.fullscreen", per origin). Unity's data cache
# is keyed by URL, so a phone that plays both keeps two copies of the data.
param([switch]$SkipBuild, [switch]$SkipDeploy, [switch]$SkipScenes,
      [switch]$DryRun,
      [string]$BuildDir = "",
      [string]$PagesDir = "",
      [string]$PagesLabel = "",
      [string[]]$KeepDirs = @("city"),
      [string]$StageDir = "",
      # For offline tests against a local bare repo (file:///...). The live
      # checks after a push only run against the real remote.
      [string]$PagesRemote = "https://github.com/LucentLL/psx-racing.git")

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$src   = Split-Path -Parent $PSScriptRoot
$proj  = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$pages = "C:\Users\mcgee\psx-pages"
$repo  = $PagesRemote
$liveRemote = "https://github.com/LucentLL/psx-racing.git"
$liveUrl    = "https://lucentll.github.io/psx-racing/"
if ($BuildDir -and -not $SkipBuild) {
    Write-Host "-BuildDir publishes an existing WebGL output, so it needs -SkipBuild (a build always writes to $proj\Build\WebGL)." -ForegroundColor Red
    exit 1
}
# Checked BEFORE a forty-minute build, not after it.
if ($PagesDir) {
    if ($PagesDir -cnotmatch '^[a-z0-9][a-z0-9-]*$' -or @("build", "streamingassets", "templatedata") -contains $PagesDir) {
        Write-Host "-PagesDir '$PagesDir' must be one lowercase folder name (a-z, 0-9, -), and not one the root build writes." -ForegroundColor Red
        exit 1
    }
    if (-not $PagesLabel) { $PagesLabel = if ($PagesDir -eq "city") { "CHARLOTTE TEST" } else { $PagesDir.ToUpper() + " TEST" } }
    if ($PagesLabel -notmatch '^[A-Za-z0-9 .:_-]{1,32}$') {
        Write-Host "-PagesLabel '$PagesLabel' must be 1-32 plain characters (letters, digits, space . : _ -)." -ForegroundColor Red
        exit 1
    }
    $pages = "$pages-$PagesDir"
}

# Unity.exe is a launcher: it spawns the real editor and returns immediately, so
# waiting on the call itself reads stale logs. Wait on the actual child PIDs.
# Run git and judge it by its EXIT CODE.
#
# See the note at the deploy stage: PowerShell treats a native command's stderr
# as errors, so git's routine chatter — line-ending warnings, "Everything
# up-to-date", push progress — reads as a failure and stops the script. Calling
# through cmd folds stderr into stdout before PowerShell can see a separate
# stream to be upset about.
function Format-GitArgs([string[]]$GitArgs) {
    ($GitArgs | ForEach-Object {
        if ($_ -eq '') { '""' }
        elseif ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
}
function Invoke-Git([string[]]$GitArgs) {
    $line = Format-GitArgs $GitArgs
    $out = & cmd /c "git $line 2>&1"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "git $line failed ($LASTEXITCODE):" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}
# The same, handing back what git printed (and, with -AllowFail, the exit code
# instead of stopping the script -- for the calls whose failure is an answer:
# ls-remote --exit-code, a lease the remote refused).
function Invoke-GitOut([string[]]$GitArgs, [switch]$AllowFail) {
    $line = Format-GitArgs $GitArgs
    $out = @(& cmd /c "git $line 2>&1")
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFail) {
        Write-Host "git $line failed ($code):" -ForegroundColor Red
        $out | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    return [pscustomobject]@{ Code = $code; Out = $out; Text = ($out -join "`n") }
}

# THIS PUBLISH ONLY WAITS ON ITS OWN SANDBOX. Get-UnityPids (unity-wait.ps1)
# filters by command line, so the owner opening their editor mid-build no
# longer holds the deploy hostage -- see the note on that function.
function Invoke-UnityWait([string[]]$UnityArgs, [int]$MaxMinutes = 40) {
    $before = @(Get-UnityPids $proj)
    Start-Process -FilePath $unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 5
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    while ((Get-Date) -lt $deadline) {
        $now = @(Get-UnityPids $proj)
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
    if ($SkipScenes) {
        $marker = "$proj\PSXRacing_build_log.txt"
        if (-not (Test-Path $marker) -or -not (Select-String -Path $marker -Pattern "BUILD OK" -Quiet)) {
            Write-Host "-SkipScenes needs a green scene build in the sandbox (no BUILD OK in $marker) - run tools\verify.ps1 first." -ForegroundColor Red
            exit 1
        }
        Write-Host "[1/3] Sandbox kept as verified (scene build marker: $((Get-Item $marker).LastWriteTime))" -ForegroundColor Cyan
    } else {
    Write-Host "[1/3] Mirroring project to sandbox..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $proj | Out-Null
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        # Never mirror Library: copying one from a live editor corrupts the
        # artifact database. The sandbox builds its own and keeps it warm.
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
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
    if (-not $SkipScenes) {
        if (-not (Invoke-SceneBuild -Proj $proj)) {
            Write-Host "SCENE BUILD FAILED - not building a player around stale scenes." -ForegroundColor Red
            Get-Content "$proj\scenebuild.log" -Tail 6
            exit 1
        }
    }
    Get-Content "$proj\PSXRacing_build_log.txt" -Tail 3

    # NOTHING THAT SHIPS MAY POINT AT A MISSING ASSET. On 2026-09-12 the pizza
    # boxes shipped magenta: the scene build re-baked Resources/PizzaCargo
    # against freshly minted material GUIDs, a later additive copy put the
    # source's older prefabs back over the bake, and -SkipScenes built exactly
    # that. Seconds, and no Unity: every scene in the build settings, every
    # Resources asset, and every YAML asset they reference.
    & py "$PSScriptRoot\guid-audit.py" $proj
    if ($LASTEXITCODE -ne 0) {
        Write-Host "GUID AUDIT FAILED - a shipped asset references a missing GUID (it would render magenta or not at all). Run the scene build, then tools\cargo-sync-back.ps1." -ForegroundColor Red
        exit 1
    }

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

# WHAT THIS DEPLOY IS SHIPPING PAST. The owner has standing authorisation to
# deploy without being asked and tests only on the live URL, so an audit
# failure does NOT stop a publish -- but it is never silent either. The last
# tools\verify.ps1 run writes its verdict and every failing line to the
# sandbox; when that file is missing, failed, or older than the source it
# claims to cover, the whole list is printed here as the waiver this deploy is
# made under. (Until 2026-09-13 nothing was checked at all, and a stage with
# open deck ends shipped with every audit report on disk saying so.)
function Show-AuditWaiver {
    $result = "$proj\PSXRacing_verify_result.txt"
    $bar = "=" * 72
    $problem = $null
    $lines = @()
    if (-not (Test-Path $result)) {
        $problem = "NO VERIFY RESULT in the sandbox ($result): tools\verify.ps1 has not run to the end on this sandbox."
    } else {
        $lines = @(Get-Content $result)
        $verdict = if ($lines.Count -gt 0) { $lines[0] } else { "" }
        $newest = Get-ChildItem "$src\Assets\PSXRacing\Scripts", "$src\Assets\PSXRacing\Editor", "$src\Assets\PSXRacing\Shaders" `
                      -Recurse -File -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $stamp = (Get-Item $result).LastWriteTime
        if ($verdict -cnotmatch '^VERIFY PASS') {
            $problem = "THE LAST VERIFY FAILED ($verdict, $stamp)."
        } elseif ($newest -and $newest.LastWriteTime -gt $stamp) {
            $problem = "THE LAST VERIFY PASSED, BUT BEFORE THE SOURCE CHANGED: $($newest.Name) was written $($newest.LastWriteTime), the verify finished $stamp."
        }
    }
    if ($null -eq $problem) {
        Write-Host "Audits: the last tools\verify.ps1 passed on this source." -ForegroundColor Green
        return
    }
    Write-Host $bar -ForegroundColor Yellow
    Write-Host " AUDIT WAIVER - publishing without a passing tools\verify.ps1" -ForegroundColor Yellow
    Write-Host " $problem" -ForegroundColor Yellow
    Write-Host " Deploying anyway under the owner's standing authorisation. Shipped past:" -ForegroundColor Yellow
    $failures = $false
    $listed = 0
    foreach ($l in $lines) {
        if ($failures) { Write-Host "   $l" -ForegroundColor Yellow; $listed++ }
        elseif ($l -ceq "failures:") { $failures = $true }
    }
    if ($listed -eq 0) { Write-Host "   (nothing measured against this source: run tools\verify.ps1 for the list)" -ForegroundColor Yellow }
    Write-Host $bar -ForegroundColor Yellow
}

if (-not $SkipDeploy) {
    $build = if ($BuildDir) { $BuildDir } else { "$proj\Build\WebGL" }
    if ($BuildDir) {
        Write-Host "Audits: not read (publishing -BuildDir $build, which is not the sandbox's own output)." -ForegroundColor Yellow
    } else {
        Show-AuditWaiver
    }
    $how = if ($DryRun) { "DRY RUN (stage and show; nothing is pushed)" } else { "Publishing" }
    $where = if ($PagesDir) { "gh-pages/$PagesDir/ (test page; the rest of gh-pages is kept)" } else { "gh-pages/ (root)" }
    Write-Host "[3/3] $how to $where from $build" -ForegroundColor Cyan
    if (-not (Test-Path "$build\index.html")) { Write-Host "No build to deploy." -ForegroundColor Red; exit 1 }

    # -File hands "-KeepDirs city,lab" over as ONE string; split it here.
    $KeepDirs = @($KeepDirs | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($k in $KeepDirs) {
        if ($k -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]*$') { Write-Host "-KeepDirs '$k' is not a plain folder name." -ForegroundColor Red; exit 1 }
    }

    $stage = if ($StageDir) { $StageDir } else { $pages }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    # THE STAGING FOLDER IS WIPED EVERY ATTEMPT, SO IT MUST BE OURS. A publish
    # staging repo carries .git\psx-pages-stage; the one the old script left at
    # the default path is recognised by its remote. Nothing else is cleared --
    # a -StageDir typo must not be able to delete a checkout.
    function Reset-Stage([string]$dir) {
        if (Test-Path $dir) {
            $empty  = -not (Get-ChildItem $dir -Force | Select-Object -First 1)
            $marked = Test-Path "$dir\.git\psx-pages-stage"
            $legacy = ($dir -eq $pages) -and (Test-Path "$dir\.git\config") -and
                      (Select-String -Path "$dir\.git\config" -SimpleMatch "LucentLL/psx-racing" -Quiet)
            $project = (Test-Path "$dir\Assets") -or (Test-Path "$dir\ProjectSettings")
            if ($project -or -not ($empty -or $marked -or $legacy)) {
                $why = if ($project) { "it holds a Unity project (Assets or ProjectSettings)" } else { "no .git\psx-pages-stage marker" }
                Write-Host "REFUSING to clear $dir - it is not a publish staging folder ($why)." -ForegroundColor Red
                exit 1
            }
            Remove-Item $dir -Recurse -Force
        }
        New-Item -ItemType Directory -Force $dir | Out-Null
    }

    # Copy the build into $target and stamp its index.html. Returns the
    # top-level names it wrote and the stamp.
    function Stage-Build([string]$target) {
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item "$build\index.html" $target
        Copy-Item "$build\Build" $target -Recurse
        $names = @("index.html", "Build")
        if (Test-Path "$build\StreamingAssets") { Copy-Item "$build\StreamingAssets" $target -Recurse; $names += "StreamingAssets" }

        # CACHE BUSTING. Every deploy writes the same four payloads — the data, the
        # wasm, the framework and the loader — and GitHub Pages serves
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
        # FOUND, NOT ASSUMED. The payload names carry a suffix now, and it is NOT
        # the one you would guess: Brotli with decompressionFallback writes
        # WebGL.data.unityweb, not WebGL.data.br. The neutral extension is the
        # point — it stops a server helpfully interpreting a stream the LOADER is
        # going to decompress. The loader itself is never compressed and keeps its
        # plain name.
        #
        # A hardcoded list stamps nothing, fails its own check, and throws away a
        # forty-minute build. That happened twice on one afternoon: once with the
        # plain names after compression was turned on, and once with ".br" guessed.
        # Asking the directory what it holds is the only version of this that
        # survives the next change.
        $files = @()
        foreach ($stem in @("WebGL.data", "WebGL.wasm", "WebGL.framework.js", "WebGL.loader.js")) {
            $hit = Get-ChildItem "$target\Build" -Filter "$stem*" -File |
                   Where-Object { $_.Name -in @($stem, "$stem.unityweb", "$stem.br", "$stem.gz") } |
                   Select-Object -First 1
            if ($null -eq $hit) { Write-Host "  WARN: no build file for $stem"; continue }
            $files += $hit.Name
        }
        $dataFile = $files | Where-Object { $_ -like "WebGL.data*" } | Select-Object -First 1
        if ($null -eq $dataFile) {
            Write-Host "NO WebGL.data IN THE BUILD - nothing to publish." -ForegroundColor Red
            exit 1
        }
        # From the BUILD's file, not the copy: the stamp names the build.
        $stamp = (Get-Item "$build\Build\$dataFile").LastWriteTimeUtc.ToString("yyyyMMddHHmmss")
        $idx = Join-Path $target "index.html"
        # Read and written as UTF-8 WITHOUT a BOM. Get-Content -Raw read the
        # template (BOM-less UTF-8) as Windows-1252 and Set-Content -Encoding utf8
        # wrote the mojibake back with a BOM: the live page carried three bytes
        # of junk for every em dash in its comments. Harmless there; the same round trip
        # would mangle any label or text this script puts into the page.
        $html = [IO.File]::ReadAllText($idx, $utf8NoBom)
        $stamped = 0
        foreach ($f in $files) {
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
        [IO.File]::WriteAllText($idx, $html, $utf8NoBom)
        # Verified rather than assumed, for the same reason the stamp exists at
        # all: a cache-bust that quietly does nothing is indistinguishable from a
        # deploy that worked.
        $check = [IO.File]::ReadAllText($idx, $utf8NoBom)
        if ($stamped -lt 4 -or $check -notmatch [regex]::Escape("$dataFile" + "?v=$stamp")) {
            Write-Host "CACHE-BUST FAILED - index.html would serve stale build." -ForegroundColor Red
            exit 1
        }
        Write-Host "  cache-bust: $stamped URLs stamped v=$stamp"
        return [pscustomobject]@{ Names = $names; Stamp = $stamp; DataFile = $dataFile }
    }

    # What the source tree was at publish time, for the label and the marker.
    # (The build itself came from the sandbox's last mirror of it.)
    $srcNote = "source unknown"
    $r1 = Invoke-GitOut @("-C", $src, "rev-parse", "--short", "HEAD") -AllowFail
    $r2 = Invoke-GitOut @("-C", $src, "rev-parse", "--abbrev-ref", "HEAD") -AllowFail
    if ($r1.Code -eq 0 -and $r2.Code -eq 0) {
        $r3 = Invoke-GitOut @("-C", $src, "status", "--porcelain", "--untracked-files=no") -AllowFail
        $dirty = @($r3.Out | Where-Object { $_ -and $_ -notmatch '^warning:' }).Count -gt 0
        $srcNote = "$($r2.Out[0].Trim())@$($r1.Out[0].Trim())" + $(if ($dirty) { "+uncommitted" } else { "" })
    }

    # A TEST PAGE MUST SAY SO. " - <label>" on the tab title, and a tag at
    # the top of the loading screen with the source and the build time, so
    # the owner can tell at a glance which build he is on. Put in by anchor
    # and checked; a template that lost either anchor stops the publish
    # rather than shipping an unlabelled test build.
    function Add-PagesLabel([string]$idx, [string]$stamp) {
        $html = [IO.File]::ReadAllText($idx, $utf8NoBom)
        $t = [regex]::Match($html, '<title>([^<]*)</title>')
        $anchor = '<div id="splash">'
        if (-not $t.Success -or -not $html.Contains($anchor)) {
            Write-Host "CANNOT LABEL THE TEST PAGE - index.html has no <title> or no <div id=`"splash`"> (did the WebGL template change?). Not publishing an unlabelled test build." -ForegroundColor Red
            exit 1
        }
        $html = $html.Substring(0, $t.Index) + "<title>" + $t.Groups[1].Value + " - " + $PagesLabel + "</title>" + $html.Substring($t.Index + $t.Length)
        $built = [DateTime]::ParseExact($stamp, "yyyyMMddHHmmss", [Globalization.CultureInfo]::InvariantCulture).ToString("yyyy-MM-dd HH:mm")
        $sub = [Net.WebUtility]::HtmlEncode("$srcNote - built $built UTC")
        $tag = '<div id="pages-tag" style="position:absolute;top:calc(14px + env(safe-area-inset-top, 0px));left:50%;' +
               'transform:translateX(-50%);width:max-content;max-width:92vw;box-sizing:border-box;padding:5px 12px;border:2px solid #ffd766;' +
               'background:#000000aa;color:#ffd766;text-align:center;pointer-events:none;">' +
               '<div style="font-size:13px;font-weight:800;letter-spacing:.22em;white-space:nowrap;">' + $PagesLabel + '</div>' +
               '<div style="font-size:10px;font-weight:600;letter-spacing:.08em;opacity:.8;margin-top:3px;">' + $sub + '</div></div>'
        $i = $html.IndexOf($anchor) + $anchor.Length
        $html = $html.Insert($i, "`n    " + $tag)
        [IO.File]::WriteAllText($idx, $html, $utf8NoBom)
        $check = [IO.File]::ReadAllText($idx, $utf8NoBom)
        if (-not $check.Contains(" - $PagesLabel</title>") -or -not $check.Contains('id="pages-tag"')) {
            Write-Host "LABEL CHECK FAILED - the test page would not say it is a test page." -ForegroundColor Red
            exit 1
        }
        Write-Host "  label: '$PagesLabel' on the tab title and the loading screen ($srcNote)"
    }

    # Orphan branch, force-pushed: the build is ~80 MB and committing each
    # iteration onto a normal branch would grow history by that much every time.
    #
    # Every git call goes through Invoke-Git / Invoke-GitOut, and that is not
    # tidiness. Windows PowerShell wraps ANY line a native exe writes to stderr
    # in an ErrorRecord, and with $ErrorActionPreference = "Stop" that
    # terminates the script — so `git add -A` printing "LF will be replaced by
    # CRLF" killed a deploy on 2026-08-30 AFTER a successful 40-minute build,
    # with the files staged and nothing committed. Exit codes are the only
    # thing git says about failure that is actually about failure.
    $emptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904"
    $attempt = 0
    while ($true) {
        $attempt++
        Reset-Stage $stage
        Invoke-Git @("-C", $stage, "init", "-q", "-b", "gh-pages")
        Set-Content "$stage\.git\psx-pages-stage" "tools\build-and-publish.ps1 staging repo - safe to delete" -Encoding ascii
        Invoke-Git @("-C", $stage, "config", "user.name", "LucentLL")
        Invoke-Git @("-C", $stage, "config", "user.email", "mcgeevarnell@gmail.com")
        # Bytes in exactly as the build wrote them.
        Invoke-Git @("-C", $stage, "config", "core.autocrlf", "false")
        Invoke-Git @("-C", $stage, "remote", "add", "origin", $repo)

        # 1. WHAT IS LIVE. Trees only (--filter=blob:none): enough to keep a
        # folder by its tree hash without downloading a byte of it. A failed
        # read is NOT "no gh-pages": publishing blind is exactly what wiped
        # the other builds, so anything but a clean "no such branch" stops.
        $ls = Invoke-GitOut @("-C", $stage, "ls-remote", "--exit-code", "origin", "refs/heads/gh-pages") -AllowFail
        $base = $null
        if ($ls.Code -eq 0) {
            Invoke-Git @("-C", $stage, "fetch", "-q", "--filter=blob:none", "--depth", "1", "origin", "refs/heads/gh-pages")
            $base = (Invoke-GitOut @("-C", $stage, "rev-parse", "FETCH_HEAD")).Out[0].Trim()
            $info = (Invoke-GitOut @("-C", $stage, "log", "-1", "--pretty=reference", "--date=iso", $base)).Out[0]
            Write-Host "  gh-pages now : $info"
        } elseif ($ls.Code -eq 2) {
            Write-Host "  gh-pages now : (does not exist - first publish, nothing to keep)"
        } else {
            Write-Host "COULD NOT READ gh-pages from $repo (git ls-remote exit $($ls.Code)) - refusing to publish blind, it would wipe the test builds beside this one." -ForegroundColor Red
            $ls.Out | ForEach-Object { Write-Host "  $_" }
            exit 1
        }

        # 2. WHAT STAYS.
        #   Root publish: the root is ours; the test folders beside it
        #     (-KeepDirs, or holding a psx-subpage.txt) are not.
        #   -PagesDir: only that one folder is ours; EVERYTHING else stays.
        $kept = @(); $dropped = @()
        if ($base) {
            foreach ($l in (Invoke-GitOut @("-C", $stage, "ls-tree", $base)).Out) {
                if ($l -notmatch '^(\d+) (\w+) ([0-9a-f]+)\t(.+)$') { continue }
                $type = $Matches[2]; $sha = $Matches[3]; $name = $Matches[4]
                $keep = $false
                if ($PagesDir) {
                    $keep = ($name -ne $PagesDir)
                } elseif ($type -eq "tree") {
                    if ($KeepDirs -contains $name) { $keep = $true }
                    else {
                        $mk = Invoke-GitOut @("-C", $stage, "ls-tree", "--name-only", $base, "--", "$name/psx-subpage.txt")
                        if ($mk.Text.Trim()) { $keep = $true }
                    }
                }
                $shown = if ($type -eq "tree") { "$name/" } else { $name }
                if ($keep) { $kept += [pscustomobject]@{ Name = $name; Sha = $sha; Type = $type; Shown = $shown } }
                else { $dropped += $shown }
            }
        }
        if ($PagesDir) {
            # The whole live tree into the index, minus our folder. None of it
            # is on disk and none of it needs to be.
            if ($base) {
                Invoke-Git @("-C", $stage, "read-tree", $base)
                Invoke-Git @("-C", $stage, "rm", "-r", "-q", "-f", "--cached", "--ignore-unmatch", "--", $PagesDir)
            }
        } else {
            foreach ($k in $kept) {
                Invoke-Git @("-C", $stage, "read-tree", "--prefix=$($k.Name)/", $k.Sha)
            }
        }

        # 3. THE NEW BUILD: at the root, or into -PagesDir (labelled, marked).
        $target = if ($PagesDir) { Join-Path $stage $PagesDir } else { $stage }
        $st = Stage-Build $target
        if ($PagesDir) {
            Add-PagesLabel (Join-Path $target "index.html") $st.Stamp
            $marker = @(
                "PSX Racing test build: tools\build-and-publish.ps1 -PagesDir $PagesDir",
                "A root publish (no -PagesDir) keeps every gh-pages folder holding this file.",
                "label: $PagesLabel",
                "source at publish: $srcNote",
                "build stamp: $($st.Stamp) (the ?v= on this folder's index.html)",
                ("published: " + [DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC")
            ) -join "`n"
            [IO.File]::WriteAllText((Join-Path $target "psx-subpage.txt"), $marker + "`n", $utf8NoBom)
            $newNames = @($PagesDir)
            # A subfolder publish onto a site with no root still needs this.
            if (-not @($kept | Where-Object { $_.Name -eq ".nojekyll" }).Count) {
                New-Item -ItemType File -Force "$stage\.nojekyll" | Out-Null
                $newNames += ".nojekyll"
            }
        } else {
            $newNames = @($st.Names) + ".nojekyll"
            foreach ($k in $kept) {
                if ($newNames -contains $k.Name) {
                    Write-Host "THE BUILD WRITES A TOP-LEVEL '$($k.Name)' AND gh-pages KEEPS A TEST FOLDER OF THAT NAME - refusing to merge them." -ForegroundColor Red
                    exit 1
                }
            }
            # Without this, Pages runs Jekyll and drops anything starting with an underscore.
            New-Item -ItemType File -Force "$stage\.nojekyll" | Out-Null
        }
        # Named paths only, never -A: the kept entries are in the index but not
        # on disk, and -A would record them as deleted.
        Invoke-Git (@("-C", $stage, "add", "--") + $newNames)
        $tree = (Invoke-GitOut @("-C", $stage, "write-tree")).Out[0].Trim()

        # 4. PROVE IT BEFORE IT LEAVES. Every kept entry is the SAME OBJECT
        # (same hash = same bytes, all of them), nothing appeared beside a
        # subfolder publish, and the new page is there.
        foreach ($k in $kept) {
            $now = (Invoke-GitOut @("-C", $stage, "rev-parse", "$($tree):$($k.Name)")).Out[0].Trim()
            if ($now -ne $k.Sha) {
                Write-Host "KEPT ENTRY $($k.Shown) WOULD CHANGE ($($k.Sha) -> $now) - refusing to push." -ForegroundColor Red
                exit 1
            }
        }
        if ($PagesDir) {
            $tops = @((Invoke-GitOut @("-C", $stage, "ls-tree", "--name-only", $tree)).Out | Where-Object { $_ })
            $allowed = @($kept | ForEach-Object { $_.Name }) + @($PagesDir, ".nojekyll")
            $extra = @($tops | Where-Object { $allowed -notcontains $_ })
            if ($extra.Count) {
                Write-Host "A -PagesDir PUBLISH WOULD ADD $($extra -join ', ') BESIDE $PagesDir/ - refusing to push." -ForegroundColor Red
                exit 1
            }
        }
        $pre = if ($PagesDir) { "$PagesDir/" } else { "" }
        $need = @("${pre}index.html", "${pre}Build/$($st.DataFile)")
        $have = @((Invoke-GitOut (@("-C", $stage, "ls-tree", "-r", "--name-only", $tree, "--") + $need)).Out | Where-Object { $_ })
        if ($have.Count -ne $need.Count) {
            Write-Host "THE NEW TREE LACKS $($need -join ' or ') - refusing to push." -ForegroundColor Red
            exit 1
        }
        # GitHub refuses any file over 100 MiB, after the upload; the plan's
        # ratchet wants nothing over 95 MiB on Pages.
        $big = @(Get-ChildItem $stage -Recurse -File | Where-Object { $_.FullName -notlike "$stage\.git\*" -and $_.Length -gt 95MB })
        foreach ($b in $big) {
            $mib = "{0:N2}" -f ($b.Length / 1MB)
            if ($b.Length -ge 100MB) {
                Write-Host "$($b.Name) is $mib MiB - GitHub refuses files of 100 MiB or more. Not pushing." -ForegroundColor Red
                exit 1
            }
            Write-Host "  WARN: $($b.Name) is $mib MiB, over the 95 MiB ratchet." -ForegroundColor Yellow
        }

        if ($PagesDir) {
            $msg = "Deploy PSX Racing test build to /$PagesDir/ ($PagesLabel, $srcNote)"
        } else {
            $keptNote = if ($kept.Count) { " (kept: " + (($kept | ForEach-Object { $_.Shown }) -join ", ") + ")" } else { "" }
            $msg = "Deploy PSX Racing WebGL build to /$keptNote"
        }
        $commit = (Invoke-GitOut @("-C", $stage, "commit-tree", $tree, "-m", $msg)).Out[0].Trim()

        # 5. SAY WHAT IS ABOUT TO HAPPEN.
        if ($PagesDir) {
            Write-Host "  publishing   : /$PagesDir/ ($PagesLabel, v=$($st.Stamp), source $srcNote)"
        } else {
            Write-Host "  publishing   : / (the game, v=$($st.Stamp))"
        }
        if ($kept.Count) {
            foreach ($k in $kept) {
                if ($k.Type -eq "tree") {
                    $n = @((Invoke-GitOut @("-C", $stage, "ls-tree", "-r", "--name-only", $k.Sha)).Out | Where-Object { $_ }).Count
                    Write-Host ("  kept as is   : {0,-14} (tree {1}, {2} file(s), byte-identical)" -f $k.Shown, $k.Sha.Substring(0, 7), $n)
                } else {
                    Write-Host ("  kept as is   : {0,-14} (blob {1}, byte-identical)" -f $k.Shown, $k.Sha.Substring(0, 7))
                }
            }
        } elseif ($PagesDir) {
            Write-Host "  kept as is   : (gh-pages holds nothing else)"
        } else {
            Write-Host "  kept as is   : (no test folders on gh-pages)"
        }
        $ours = @($newNames | ForEach-Object { $_; "$_/" })
        $replaced = @($dropped | Where-Object { $ours -contains $_ })
        $removed  = @($dropped | Where-Object { $ours -notcontains $_ })
        if ($replaced.Count) { Write-Host ("  replaced     : " + ($replaced -join ", ")) }
        if ($removed.Count) {
            Write-Host ("  removed      : " + ($removed -join ", ") + "  (not in -KeepDirs, no psx-subpage.txt)") -ForegroundColor Yellow
        }
        $from = if ($base) { $base } else { $emptyTree }
        $diff = @((Invoke-GitOut @("-C", $stage, "diff-tree", "-r", "--no-renames", "--name-status", $from, $tree)).Out |
                  Where-Object { $_ -match '^[ADMT]\t' })
        $upload = [long]0; $nUp = 0
        $byTop = @{}
        foreach ($d in $diff) {
            $s, $p = $d -split "`t", 2
            $top = ($p -split '/')[0]
            if ($p.Contains('/')) { $top += '/' }
            if (-not $byTop.ContainsKey($top)) { $byTop[$top] = @{ A = 0; M = 0; D = 0; T = 0 } }
            $byTop[$top][$s]++
            if ($s -ne 'D') {
                $f = Join-Path $stage ($p -replace '/', '\')
                if (Test-Path $f) { $upload += (Get-Item $f).Length; $nUp++ }
            }
        }
        Write-Host "  changes      : (+added ~changed -removed, by top-level entry)"
        foreach ($top in ($byTop.Keys | Sort-Object)) {
            $c = $byTop[$top]
            Write-Host ("    {0,-24} +{1} ~{2} -{3}" -f $top, $c.A, ($c.M + $c.T), $c.D)
        }
        if (-not $diff.Count) { Write-Host "    (none: gh-pages already serves exactly this)" }
        Write-Host ("  upload       : at most {0:N1} MiB in {1} new or changed file(s)" -f ($upload / 1MB), $nUp)
        Write-Host "  new commit   : $commit (orphan, no parent; tree $tree)"

        if ($DryRun) {
            $over = if ($base) { $base.Substring(0, 7) } else { "(none)" }
            Write-Host "DRY RUN - nothing pushed. Would force-push $($commit.Substring(0, 7)) over gh-pages $over with a lease. Staged in $stage" -ForegroundColor Yellow
            break
        }

        # 6. PUSH, BUT ONLY OVER WHAT WE FETCHED. A lease, not a bare -f: if
        # another publish landed after step 1, this push is refused instead of
        # wiping it, and the whole stage is rebuilt on the new tip.
        $lease = "--force-with-lease=refs/heads/gh-pages:" + $(if ($base) { $base } else { "" })
        $push = Invoke-GitOut @("-C", $stage, "push", $lease, "origin", "$($commit):refs/heads/gh-pages") -AllowFail
        if ($push.Code -ne 0) {
            # Judged by asking the remote, not by the message: a publish that
            # lands before this push connects reads "stale info", one that lands
            # during the upload reads as a ref-lock refusal. Both mean "gh-pages
            # moved", and both are safe to redo from the top.
            $probe = Invoke-GitOut @("-C", $stage, "ls-remote", "origin", "refs/heads/gh-pages") -AllowFail
            $tip = if ($probe.Code -eq 0) { (("" + ($probe.Out | Select-Object -First 1)) -split '\s+')[0] } else { "" }
            $moved = ($probe.Code -eq 0) -and ($tip -ne $(if ($base) { $base } else { "" }))
            if ($moved -and $attempt -lt 3) {
                Write-Host "  gh-pages moved while this publish was staging (now $tip - another publish landed). Staging again on the new tip ($attempt/3)." -ForegroundColor Yellow
                continue
            }
            Write-Host "PUSH FAILED ($($push.Code)):" -ForegroundColor Red
            $push.Out | ForEach-Object { Write-Host "  $_" }
            exit 1
        }
        # A push that "worked" is not proof: 2026-08-21 printed PUSHED over a
        # branch that never moved. Ask the remote.
        $after = Invoke-GitOut @("-C", $stage, "ls-remote", "origin", "refs/heads/gh-pages")
        $liveSha = (($after.Out | Select-Object -First 1) -split '\s+')[0]
        if ($liveSha -ne $commit) {
            Write-Host "PUSH DID NOT LAND - gh-pages is $liveSha, not $commit." -ForegroundColor Red
            exit 1
        }
        Write-Host "  pushed       : gh-pages = $commit" -ForegroundColor Green
        break
    }

    if (-not $DryRun) {
        if ($repo -ne $liveRemote) {
            Write-Host "`nPushed to $repo (a test remote: no live URL to check)." -ForegroundColor Green
        } else {
            # Pages serves a push in ~20-60 s. Ask for index.html past the CDN
            # cache and look for THIS build's stamp.
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $pageUrl = if ($PagesDir) { "$liveUrl$PagesDir/" } else { $liveUrl }
            $want = @("?v=$($st.Stamp)")
            if ($PagesDir) { $want += 'id="pages-tag"' }
            $seen = $false
            $deadline = (Get-Date).AddMinutes(4)
            while (-not $seen -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 15
                try {
                    $r = Invoke-WebRequest -Uri ($pageUrl + "index.html?nocache=" + [DateTime]::UtcNow.Ticks) -UseBasicParsing -TimeoutSec 30
                    $seen = -not @($want | Where-Object { -not $r.Content.Contains($_) }).Count
                } catch {}
            }
            if ($seen) { Write-Host "  live         : $pageUrl serves v=$($st.Stamp)" -ForegroundColor Green }
            else { Write-Host "  NOT CONFIRMED: after 4 min $pageUrl does not serve v=$($st.Stamp) yet. Pages can lag - check again before calling it live." -ForegroundColor Yellow }
            # And what this publish kept is still there: the game beside a test
            # page, the test folders beside the game.
            $others = if ($PagesDir) { @("") } else { @($kept | ForEach-Object { "$($_.Name)/" }) }
            foreach ($o in $others) {
                try {
                    $r = Invoke-WebRequest -Uri ($liveUrl + $o + "?nocache=" + [DateTime]::UtcNow.Ticks) -UseBasicParsing -TimeoutSec 30
                    Write-Host "  still served : $liveUrl$o ($($r.StatusCode))" -ForegroundColor Green
                } catch {
                    Write-Host "  WARN: $liveUrl$o did not answer: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
            Write-Host "`nLive: $pageUrl" -ForegroundColor Green
            Write-Host "(Pages takes ~1 min to refresh; hard-refresh on mobile.)"
        }
    }
}
