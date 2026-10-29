# city-popin perf A/B (2026-10-28): tools\perf-probe.ps1 in a SHARED sandbox,
# under its AGENT_LOCK.txt, code mirrored first (Scripts, Editor, Shaders only -
# never Resources or Art), the probe run with -NoSync. -Before: the far
# buildings, the tile fade and the build-ahead order all off (PSX_CITY_PROXIES=0,
# PSX_CITY_FADE=0, PSX_CITY_AHEAD=0: the streaming as on main).
#   $env:PSX_SANDBOX = 'C:\Users\mcgee\PSXRec'
#   powershell -ExecutionPolicy Bypass -File tools\popin-perf.ps1 -Tag popin_after
param([string]$Tag = "popin", [switch]$Before, [string]$Venue = "TryonSprint", [int]$AbSeconds = 0)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
$lock = Join-Path $proj "AGENT_LOCK.txt"
while ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 90) {
    "waiting: $(Get-Content $lock -Raw)"; Start-Sleep -Seconds 30
}
Set-Content -Path $lock -Value ("city-popin " + (Get-Date).ToString("s"))
try {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    $v = if ($Before) { "0" } else { "" }
    $env:PSX_CITY_PROXIES = $v; $env:PSX_CITY_FADE = $v; $env:PSX_CITY_AHEAD = $v
    $env:PSX_PERF_VENUE = $Venue
    $env:PSX_PERF_AB_SECONDS = "$AbSeconds"
    & powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\perf-probe.ps1" -Tag $Tag -NoSync -NoWatch
}
finally {
    Remove-Item $lock -ErrorAction SilentlyContinue
    Remove-Item Env:PSX_CITY_PROXIES, Env:PSX_CITY_FADE, Env:PSX_CITY_AHEAD -ErrorAction SilentlyContinue
}
