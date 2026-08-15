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
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$src   = Split-Path -Parent $PSScriptRoot
$proj  = "C:\Users\mcgee\PSXBuild"
$pages = "C:\Users\mcgee\psx-pages"
$repo  = "https://github.com/LucentLL/psx-racing.git"

# Unity.exe is a launcher: it spawns the real editor and returns immediately, so
# waiting on the call itself reads stale logs. Wait on the actual child PIDs.
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
    Write-Host "[1/3] Mirroring project to sandbox..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $proj | Out-Null
    foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
        # Never mirror Library: copying one from a live editor corrupts the
        # artifact database. The sandbox builds its own and keeps it warm.
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }

    Write-Host "[2/3] Generating scene + building WebGL..." -ForegroundColor Cyan
    Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.Build",
        "-logFile","$proj\scenebuild.log","-accept-apiupdate") 20 | Out-Null
    Get-Content "$proj\PSXRacing_build_log.txt" -Tail 3

    Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
        "-buildTarget","WebGL",
        "-executeMethod","PSXRacing.EditorTools.PSXBuildWebGL.BuildFromCommandLine",
        "-logFile","$proj\build.log","-accept-apiupdate") 45 | Out-Null

    if (-not (Test-Path "$proj\Build\WebGL\build_ok.txt")) {
        Write-Host "BUILD FAILED - see $proj\build.log" -ForegroundColor Red
        exit 1
    }
    Get-Content "$proj\Build\WebGL\build_ok.txt"
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
    # Without this, Pages runs Jekyll and drops anything starting with an underscore.
    New-Item -ItemType File "$pages\.nojekyll" | Out-Null

    Push-Location $pages
    # Orphan branch, force-pushed: the build is ~51 MB and committing each
    # iteration onto a normal branch would grow history by that much every time.
    git init -q -b gh-pages
    git config user.name "LucentLL"
    git config user.email "mcgeevarnell@gmail.com"
    git add -A
    git commit -q -m "Deploy PSX Racing WebGL build"
    git remote add origin $repo
    git push -q -f origin gh-pages
    Pop-Location

    Write-Host "`nLive: https://lucentll.github.io/psx-racing/" -ForegroundColor Green
    Write-Host "(Pages takes ~1 min to refresh; hard-refresh on mobile.)"
}
