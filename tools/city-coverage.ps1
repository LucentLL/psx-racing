# The city COVERAGE AUDIT on its own (Editor\CityAudit.Coverage.cs, roads pass
# A1): the built road meshes rasterised by owner - COPLANAR double cover,
# HOLES in the intended outline, pavement OUTSIDE it, branch OVERSHOOT into its
# host, UNDERLAPS - by tier, plus the tap identity check and the data-gap
# census (dead ends just short of another road). The same report CityAudit.Run
# prints as its CoverageReport hook, without the rest of the audit: ~3 min
# boxed against the ~15 min of a city-cycle -AuditOnly.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-coverage.ps1
#                  boxed to the default box (CityAudit.OwnerBox: uptown + W 5th/I-77)
#   ...            -Box x0,z0,x1,z1    a box of your own (game metres, x east,
#                  z north; PSX_COVER_BOX)
#   ...            -Full               city-wide (PSX_AUDIT_FULL=1; a release
#                  gate's number, about ten minutes)
#   ...            -Tier 1             the tiers counted (PSX_COVER_TIER: 1, 1,2, all)
#   ...            -OutDir <dir>       where the reports are copied
#   ...            -NoMirror           the sandbox as it stands
#
# WHICH SANDBOX: $env:PSX_SANDBOX, default C:\Users\mcgee\PSXBuild (lane A).
# The city lane sets $env:PSX_SANDBOX='C:\Users\mcgee\PSXCity' and runs this
# file from its own tree. One Unity job per sandbox at a time; a job past its
# budget is left running, never killed.
#
# Writes (sandbox root, then -OutDir): city_coverage.txt, city_coverage.json.
# Exit 1 when the job did not finish, the audit threw, wrote no report, or the
# tap identity check failed.
param([string]$Box = "", [switch]$Full, [string]$Tier = "", [string]$OutDir = "", [int]$MaxMinutes = 45, [switch]$NoMirror)
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
    $env:PSX_COVER_BOX = ($parts -join ',')
} else { Remove-Item Env:\PSX_COVER_BOX -ErrorAction SilentlyContinue }
if ($Full) { $env:PSX_AUDIT_FULL = "1" }
if ($Tier) { $env:PSX_COVER_TIER = $Tier }
Write-Host ("city coverage audit: sandbox {0}, source {1}, {2}" -f $proj, $src, $(if ($Full) { "CITY-WIDE" } elseif ($Box) { "box $($env:PSX_COVER_BOX)" } else { "default box (CityAudit.OwnerBox)" })) -ForegroundColor Cyan

if (-not $NoMirror) {
    # the same copy as tools\city-cycle.ps1 and tools\city-launch.ps1
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

$outs = @("city_coverage.txt", "city_coverage.json")
foreach ($f in $outs) { Remove-Item (Join-Path $proj $f) -Force -ErrorAction SilentlyContinue }
$log = "$proj\citycoverage.log"
$t0 = Get-Date
$ok = @(Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @(
    "-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CityCoverage.RunHeadless",
    "-logFile",$log,"-accept-apiupdate")) | Select-Object -Last 1
$min = ((Get-Date) - $t0).TotalMinutes
$failed = $false
if (-not $ok) { Write-Host ("job did not finish in {0} minutes (left running, never killed)" -f $MaxMinutes) -ForegroundColor Red; $failed = $true }
$cs = Select-String -Path $log -Pattern "error CS" -ErrorAction SilentlyContinue | Select-Object -First 10
if ($cs) { $cs | ForEach-Object { $_.Line }; $failed = $true }
$report = Join-Path $proj "city_coverage.txt"
if (Test-Path $report) {
    Get-Content $report
    if (Select-String -Path $report -Pattern "THREW|  FAIL " -Quiet) { $failed = $true }
} else {
    Write-Host "no city_coverage.txt written - log tail:" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    $failed = $true
}
if ($OutDir) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    foreach ($f in $outs) { $p = Join-Path $proj $f; if (Test-Path $p) { Copy-Item $p $OutDir -Force } }
    Write-Host "reports copied to $OutDir"
}
Write-Host ("CITY COVERAGE DONE in {0:0.0} min{1}" -f $min, $(if ($failed) { " - FAILED" } else { "" })) -ForegroundColor $(if ($failed) { "Red" } else { "Green" })
if ($failed) { exit 1 }
exit 0
