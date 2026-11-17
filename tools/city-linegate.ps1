# THE LINE GATE (2026-11-16): every painted line, pavement edge, junction mouth, curb and barrier of
# Charlotte as the game draws it, judged by MUTCD / AASHTO limits (Assets\PSXRacing\Editor\
# CityAudit.LineGate.cs, .LineGate2.cs). City-wide; no class is "known, ignore".
#
#   powershell -ExecutionPolicy Bypass -File tools\city-linegate.ps1
#       builds every road tile in the sandbox (25-60 min), writes city_linegate.txt / .csv / .json there,
#       compares the totals with tools\city\baseline\linegate_baseline.json and EXITS 1 WHEN ANY CLASS
#       HAS MORE FAILING PLACES THAN THE BASELINE (2: the job failed; 0: no class raised).
#   -WriteBaseline        record this run as the baseline (whole-city runs only; say why in the commit)
#   -Box "x0,z0,x1,z1"    a box run (game metres): for work on one place; never compared with the baseline
#   -Name "North Tryon Street"   one road's tiles
#   -Shots 60             also render the worst N spots (plan + driver's eye) into Screenshots\City\linegate
#   -Heat out.png         also draw the heat map (python, PIL)
#   -Sandbox / -Source    the Unity sandbox (default $env:PSX_SANDBOX or C:\Users\mcgee\PSXRoadA) and the
#                         checkout whose Scripts / Editor / Shaders are mirrored into it (default: this one)
#   -NoSync               run on the sandbox's code as it stands
#   -NoLock               the caller already holds <sandbox>\AGENT_LOCK.txt
#
# THE RULE (Docs\CHARLOTTE.md, "Line gate"): a branch may not raise any class; every build note quotes
# the totals line this prints; a class is closed only at 0.
param(
    [string]$Sandbox = $(if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRoadA" }),
    [string]$Source = (Split-Path -Parent $PSScriptRoot),
    [string]$Baseline = "",
    [switch]$WriteBaseline,
    [string]$Box = "",
    [string]$Name = "",
    [int]$Shots = 0,
    [string]$Heat = "",
    [string]$Tag = "",
    [switch]$NoSync,
    [switch]$NoLock,
    [int]$MaxMinutes = 170
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
if (-not $Baseline) { $Baseline = Join-Path $Source "tools\city\baseline\linegate_baseline.json" }
if (-not $Tag) { $Tag = (Get-Date -Format "MMdd-HHmm") }
if (-not $env:PSX_BUILD_TARGET) { $env:PSX_BUILD_TARGET = "WebGL" }
$projName = Split-Path -Leaf $Sandbox
$lock = Join-Path $Sandbox "AGENT_LOCK.txt"
function Test-SandboxUnity {
    $u = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine -match [regex]::Escape($projName) })
    return $u.Count -gt 0
}
function Read-Lock { if (Test-Path $lock) { $t = (Get-Content $lock -Raw -ErrorAction SilentlyContinue); if ($t) { return $t.Trim() } else { return "" } } return $null }

$token = "city-linegate " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " pid " + $PID + " " + ([guid]::NewGuid().ToString("N").Substring(0, 8))
$mine = $false
if (-not $NoLock) {
    # the atomic lock: a random wait; go only with no live lock and no Unity on the sandbox; write, wait 3 s, read back
    $t0 = Get-Date
    while ($true) {
        if (((Get-Date) - $t0).TotalMinutes -gt 150) { Write-Host "$projName still busy after 150 min"; exit 2 }
        Start-Sleep -Milliseconds (Get-Random -Minimum 500 -Maximum 4000)
        $cur = Read-Lock
        if ($null -ne $cur) {
            $age = ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes
            if ($age -lt 90 -or (Test-SandboxUnity)) { Start-Sleep -Seconds 10; continue }
        }
        if (Test-SandboxUnity) { Start-Sleep -Seconds 10; continue }
        Set-Content -Path $lock -Value $token -Encoding ascii
        Start-Sleep -Seconds 3
        if ((Read-Lock) -eq $token) { $mine = $true; break }
        Start-Sleep -Seconds 5
    }
}
$rc = 2
try {
    if (-not $NoSync) {
        $tc = Join-Path $env:TEMP ("psxtc_linegate_" + $PID)
        $o = powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\typecheck.ps1" -Project $Sandbox -Source $Source -OutDir $tc
        if (-not ($o | Select-String -CaseSensitive -Pattern "^TYPECHECK OK" -Quiet)) {
            $o | Select-String -CaseSensitive -Pattern ": error CS" | Select-Object -First 20 | ForEach-Object { $_.Line }
            Write-Host "TYPECHECK FAILED - the gate did not run"; exit 2
        }
        if (-not $NoLock -and (Read-Lock) -ne $token) { throw "the lock is no longer mine before the copy" }
        foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
            robocopy "$Source\$d" "$Sandbox\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:2 /W:2 | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy $d failed: $LASTEXITCODE" }
        }
    }
    foreach ($n in @("city_linegate.txt", "city_linegate.csv", "city_linegate.json", "city_linegate_worst.tsv", "city_linegate_shots.tsv")) {
        $p = Join-Path $Sandbox $n; if (Test-Path $p) { Remove-Item $p -Force }
    }
    $env:PSX_LG_TAG = $Tag
    $env:PSX_LG_BOX = $Box
    $env:PSX_LG_NAME = $Name
    if ($Shots -gt 0) { $env:PSX_LG_PLANS = "$Shots" }
    $log = Join-Path $Sandbox "linegate_job.log"
    $ok = Invoke-UnityJob -Log $log -MaxMinutes $MaxMinutes -UnityArgs @("-quit", "-nographics", "-batchmode", "-projectPath", $Sandbox, "-executeMethod", "PSXRacing.EditorTools.CityAudit.RunLineGate", "-logFile", $log, "-accept-apiupdate")
    $json = Join-Path $Sandbox "city_linegate.json"
    if (-not $ok -or -not (Test-Path $json)) {
        Write-Host "the line gate job did not finish (no city_linegate.json); log tail:"
        Get-Content $log -Tail 20 -ErrorAction SilentlyContinue
        exit 2
    }
    if ($Shots -gt 0) {
        $log2 = Join-Path $Sandbox "linegate_shots.log"
        $ok2 = Invoke-UnityJob -Log $log2 -MaxMinutes 90 -UnityArgs @("-quit", "-batchmode", "-projectPath", $Sandbox, "-executeMethod", "PSXRacing.EditorTools.CityAudit.RunLineGateShots", "-logFile", $log2, "-accept-apiupdate")
        if (-not $ok2) { Write-Host "the shots job did not finish" }
    }
    $run = Get-Content $json -Raw | ConvertFrom-Json
    $classes = @("SHIFT", "SJOG", "KINK", "WIDTH", "CONT", "SURFACE", "JUNCTION", "MEDIAN", "STEP", "BARRIER", "BRIDGE")
    $line = "LINE GATE " + $run.tag + " graph " + $run.graph + ":"
    $total = 0
    foreach ($c in $classes) { $v = $run.classes.$c.places; $total += $v; $line += " $c $v" }
    $line += " | ALL $total failing places"
    Write-Host $line
    if ($Heat) {
        $py = "C:\Users\mcgee\AppData\Local\Programs\Python\Python313\python.exe"
        & $py -I "$PSScriptRoot\city\linegate_report.py" heat --csv (Join-Path $Sandbox "city_linegate.csv") --roads (Join-Path $Sandbox "city_linegate_roads.tsv") --out $Heat
    }
    if (-not $run.whole) {
        Write-Host "a box run: not compared with the baseline (the report is $Sandbox\city_linegate.txt)"
        $rc = 0
    }
    elseif ($WriteBaseline) {
        $commit = ""
        try { $commit = (git -C $Source rev-parse --short=8 HEAD) } catch { }
        $base = [ordered]@{ note = "The line gate's baseline: failing places per class, whole city. A branch may not raise any class."; commit = $commit; date = (Get-Date -Format "yyyy-MM-dd HH:mm"); graph = $run.graph; tiles = $run.tiles; classes = $run.classes; kinds = $run.kinds }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Baseline) | Out-Null
        ($base | ConvertTo-Json -Depth 6) | Set-Content -Path $Baseline -Encoding ascii
        Write-Host "baseline written: $Baseline"
        $rc = 0
    }
    elseif (-not (Test-Path $Baseline)) {
        Write-Host "NO BASELINE at $Baseline - run with -WriteBaseline on the base commit first"; $rc = 1
    }
    else {
        $b = Get-Content $Baseline -Raw | ConvertFrom-Json
        if ($b.graph -ne $run.graph) { Write-Host ("NOTE the baseline is of graph " + $b.graph + ", this run of " + $run.graph + ": a new map export; the classes are still compared") }
        $raised = @(); $open = 0
        Write-Host ("  {0,-9} {1,8} {2,8} {3,8}" -f "class", "baseline", "now", "change")
        foreach ($c in $classes) {
            $was = [int]$b.classes.$c.places; $now = [int]$run.classes.$c.places
            if ($now -gt 0) { $open++ }
            $mark = ""
            if ($now -gt $was) { $raised += "$c $was -> $now"; $mark = "  RAISED" }
            elseif ($now -eq 0 -and $was -gt 0) { $mark = "  CLOSED" }
            Write-Host ("  {0,-9} {1,8} {2,8} {3,8}{4}" -f $c, $was, $now, ($now - $was), $mark)
        }
        if ($raised.Count -gt 0) {
            Write-Host ("LINE GATE FAILED: " + $raised.Count + " class(es) raised: " + ($raised -join "; ")) -ForegroundColor Red
            # the kinds that rose, to find the cause
            foreach ($p in $run.kinds.PSObject.Properties) {
                $was = 0; $q = $b.kinds.PSObject.Properties[$p.Name]; if ($q) { $was = [int]$q.Value }
                if ([int]$p.Value -gt $was) { Write-Host ("    " + $p.Name + ": " + $was + " -> " + $p.Value) }
            }
            $rc = 1
        }
        else { Write-Host ("LINE GATE OK: no class raised; " + $open + " of " + $classes.Count + " classes still open (the count must reach 0 class by class)") -ForegroundColor Green; $rc = 0 }
    }
}
finally {
    if ($mine -and (Read-Lock) -eq $token) { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
}
exit $rc
