# NEW GAME's opening, played (Editor/IntroPlayCheck.cs): a scratch career through the real wizard over
# black, DRIVE THIS ONE, the Little Switzerland Tunnel (the hold, the headlights, the pass, the attach, the
# exit, the drive), CHARLOTTE, and the home screen, checked against the state DRIVE THIS ONE saved.
#
#   powershell -ExecutionPolicy Bypass -File tools\intro-check.ps1 [-Off] [-NoCopy] [-NoWatch] [-Out <dir>]
#
# -Off     PSX_INTRO=0: the before (the wizard on the charcoal, DRIVE THIS ONE straight home).
# -Skip    a virtual pad holds START once the ease onto the car has begun: the skip, and its end state.
# -View    the player's saved camera view for the run (Chase, Close, Cockpit, Hood...): put back after.
# -NoCopy  run on the code already in the sandbox.
# -Out     copy the frames, the report and the video (intro.mp4, when ffmpeg is on PATH) there.
# Sandbox: $env:PSX_SANDBOX, default PSXLab (a Windows sandbox: the job runs as StandaloneWindows64).
# Takes <sandbox>\AGENT_LOCK.txt for the job and refuses a fresh lock that is not its own.
param([switch]$Off, [switch]$Skip, [switch]$NoCopy, [switch]$NoWatch, [int]$MaxMinutes = 30, [string]$Out = "", [string]$View = "", [string]$Me = "intro")
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXLab" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "StandaloneWindows64" }

$lock = Join-Path $proj "AGENT_LOCK.txt"
if (Test-Path $lock) {
    $txt = (Get-Content $lock -Raw).Trim()
    $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
    if (-not $txt.StartsWith($Me) -and $age -lt 90) { "SANDBOX LOCKED: '$txt' ($([int]$age) min old) - not started"; exit 2 }
}
Set-Content -Path $lock -Value "$Me $(Get-Date -Format s)" -Encoding ascii
try {
    if (-not $NoCopy) {
        foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
            robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
        }
    }
    $shots = Join-Path $proj "Screenshots\intro"
    Remove-Item "$shots\intro_check.txt" -ErrorAction SilentlyContinue
    if ($Off) { $env:PSX_INTRO = "0" } else { Remove-Item Env:PSX_INTRO -ErrorAction SilentlyContinue }
    if ($Skip) { $env:PSX_INTRO_CHECK = "skip" } else { Remove-Item Env:PSX_INTRO_CHECK -ErrorAction SilentlyContinue }
    if ($View) { $env:PSX_INTRO_CHECK_VIEW = $View } else { Remove-Item Env:PSX_INTRO_CHECK_VIEW -ErrorAction SilentlyContinue }

    Invoke-UnityJob -Watch:(Test-PSXWatch -NoWatch:$NoWatch) -Log "$proj\introcheck.log" -MaxMinutes $MaxMinutes -UnityArgs @(
        "-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.IntroPlayCheck.Run",
        "-logFile","$proj\introcheck.log","-accept-apiupdate") | Out-Null

    # Unity mints the new scripts' .meta in the sandbox: bring them home, once, so a later /MIR keeps the GUID.
    foreach ($f in @("Scripts\NewGameIntro.cs", "Editor\IntroPlayCheck.cs")) {
        $m = "Assets\PSXRacing\$f.meta"
        if ((Test-Path "$proj\$m") -and -not (Test-Path "$src\$m")) { Copy-Item "$proj\$m" "$src\$m"; "minted: $m" }
    }

    Select-String -Path "$proj\introcheck.log" -Pattern "error CS" | Select-Object -First 15 | ForEach-Object { $_.Line }
    Select-String -Path "$proj\introcheck.log" -Pattern "\[NewGameIntro\]" | Select-Object -First 20 | ForEach-Object { $_.Line }
    if (Test-Path "$shots\intro_check.txt") {
        Get-Content "$shots\intro_check.txt"
        if ($Out) {
            New-Item -ItemType Directory -Force $Out | Out-Null
            Copy-Item "$shots\*.png", "$shots\intro_check.txt" $Out -Force
            $ff = Get-Command ffmpeg -ErrorAction SilentlyContinue
            if ($ff -and (Test-Path "$shots\video\f0000.jpg")) {
                & $ff.Source -y -loglevel error -framerate 10 -i "$shots\video\f%04d.jpg" -c:v libx264 -pix_fmt yuv420p -crf 23 "$Out\intro.mp4"
                if (Test-Path "$Out\intro.mp4") { "video: $Out\intro.mp4" }
            }
        }
        if (Select-String -Path "$shots\intro_check.txt" -Pattern "^FAIL" -Quiet) { exit 1 }
        exit 0
    }
    "NO REPORT - the run threw. Tail of introcheck.log:"
    Get-Content "$proj\introcheck.log" -Tail 40
    exit 1
}
finally {
    Remove-Item $lock -ErrorAction SilentlyContinue
}
