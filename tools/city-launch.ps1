# The city LAUNCH AUDIT on its own (Editor\CityLaunchAudit.cs): every drivable
# path through Charlotte driven ballistically over the BUILT meshes - each
# edge both ways and every movement through every node - with the spots where
# the road falls away from a free-flying car (LAUNCH > 0.15 m, UNLOAD >
# 0.05 m), by cause and by race route. Code + data into the warm sandbox the
# way tools\city-cycle.ps1 does it, one Unity job, the reports copied back.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-launch.ps1
#                  city-wide (~8-13 min; a release gate's "city-launch full")
#   ...            -Box x0,z0,x1,z1    only the paths that start inside the
#                  box (game metres, x east, z north; PSX_LAUNCH_BOX): a
#                  package's boxed launch, ~1-2 min
#   ...            -Label w5th          names the run (PSX_LAUNCH_LABEL): the
#                  drive's spots go to launch_top_<label>.txt
#   ...            -OutDir <dir>        where the reports are copied (default:
#                  the sandbox root, where the audit writes them)
#   ...            -NoMirror            the sandbox as it stands (a cycle has
#                  just mirrored this source into it)
#
# WHICH SANDBOX: $env:PSX_SANDBOX, default C:\Users\mcgee\PSXBuild (lane A).
# The city lane sets $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity' and runs this
# file from its own tree (C:\Users\mcgee\PSX Racing-city\tools), so the code
# mirrored is that lane's. One Unity job per sandbox at a time; a job past its
# budget is left running, never killed.
#
# Writes (sandbox root, then -OutDir): city_launch.txt (counts city-wide and
# per route, by cause, the worst spots; and the roads pass's A1 blocks: LAUNCH
# BY TIER, FLOWN TURNS OFF THE PAVEMENT, PATH MISSES BY TIER, TURNING
# MOVEMENTS, COMPRESSION), city_launch.csv (every launch spot), city_launch.json
# (every A1 number, for tools\city\baseline\mesh_audit_baseline.json),
# launch_top_<label>.txt (the worst spots' paths, for a drive). Exit 1 when
# the job did not finish, the audit threw, or it wrote no report.
param([string]$Box = "", [string]$Label = "", [string]$OutDir = "", [int]$MaxMinutes = 45, [switch]$NoMirror)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXBuild" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if ($Box) {
    $parts = @($Box -split ',' | ForEach-Object { $_.Trim() })
    $num = 0.0
    if ($parts.Count -ne 4 -or @($parts | Where-Object { -not [double]::TryParse($_, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$num) }).Count) {
        Write-Host "-Box '$Box' must be four numbers x0,z0,x1,z1 (game metres)." -ForegroundColor Red
        exit 1
    }
    $env:PSX_LAUNCH_BOX = ($parts -join ',')
} else {
    Remove-Item Env:\PSX_LAUNCH_BOX -ErrorAction SilentlyContinue
}
if (-not $Label) { $Label = if ($Box) { "box" } else { "run" } }
if ($Label -notmatch '^[A-Za-z0-9_-]{1,40}$') { Write-Host "-Label '$Label' must be 1-40 letters, digits, _ or -." -ForegroundColor Red; exit 1 }
$env:PSX_LAUNCH_LABEL = $Label
Write-Host ("city launch audit: sandbox {0}, source {1}, {2}, label {3}" -f $proj, $src, $(if ($Box) { "box $($env:PSX_LAUNCH_BOX)" } else { "CITY-WIDE" }), $Label) -ForegroundColor Cyan

if (-not $NoMirror) {
    # The same copy as tools\city-cycle.ps1: code MIRRORED, Art and Resources
    # over the top with /XO (never a source file older than the sandbox's: the
    # scene build bakes into Resources there), and the retired files removed.
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
        robocopy "$src\$d" "$proj\$d" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    foreach ($rel in @("Assets\PSXRacing\Art\CLT", "Assets\PSXRacing\Art\CLT.meta",
                       "Assets\PSXRacing\Resources\charlotte_city.json", "Assets\PSXRacing\Resources\charlotte_city.json.meta",
                       "Assets\PSXRacing\Resources\clt_uptown.json", "Assets\PSXRacing\Resources\clt_uptown.json.meta",
                       "Assets\PSXRacing\Resources\clt_tryon.json", "Assets\PSXRacing\Resources\clt_tryon.json.meta",
                       "Assets\PSXRacing\Resources\clt_independence.json", "Assets\PSXRacing\Resources\clt_independence.json.meta")) {
        $p = Join-Path $proj $rel
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
}

# A report from an earlier run must not read as this one's.
$outs = @("city_launch.txt", "city_launch.csv", "city_launch.json", "launch_top_$Label.txt")
foreach ($f in $outs) { Remove-Item (Join-Path $proj $f) -Force -ErrorAction SilentlyContinue }
$log = "$proj\citylaunch.log"
$t0 = Get-Date
$ok = @(Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityLaunchAudit.Run",
    "-logFile",$log,"-accept-apiupdate")) | Select-Object -Last 1
$min = ((Get-Date) - $t0).TotalMinutes
$failed = $false
if (-not $ok) { Write-Host ("job did not finish in {0} minutes (left running, never killed)" -f $MaxMinutes) -ForegroundColor Red; $failed = $true }
$cs = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 10
if ($cs) { $cs | ForEach-Object { $_.Line }; $failed = $true }

$report = Join-Path $proj "city_launch.txt"
if (Test-Path $report) {
    Get-Content $report
    if (Select-String -Path $report -SimpleMatch "THREW" -Quiet) { $failed = $true }
} else {
    Write-Host "no city_launch.txt written - log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
if ($OutDir) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    foreach ($f in $outs) {
        $p = Join-Path $proj $f
        if (Test-Path $p) { Copy-Item $p $OutDir -Force }
    }
    Write-Host "reports copied to $OutDir"
}
Write-Host ("CITY LAUNCH DONE in {0:0.0} min{1}" -f $min, $(if ($failed) { " - FAILED" } else { "" })) -ForegroundColor $(if ($failed) { "Red" } else { "Green" })
if ($failed) { exit 1 }
exit 0
