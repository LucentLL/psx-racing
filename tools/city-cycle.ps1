# The fast city loop: code + data into the WARM sandbox, then the city audit
# and the preview shots, with no scene build. About four minutes against the
# forty a full verify costs, for the part of the city that can be checked
# without a scene: the graph, the elevation solve, the tile meshes and what
# they look like.
#
#   powershell -ExecutionPolicy Bypass -File tools\city-cycle.ps1
#
# Code folders are MIRRORED (a deleted script must not linger); Art and
# Resources are copied over the top, and the files this pass retired are
# removed by name so the sandbox cannot load them by accident.
param([switch]$AuditOnly, [switch]$PreviewOnly)
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
foreach ($d in @("Assets\PSXRacing\Art", "Assets\PSXRacing\Resources")) {
    robocopy "$src\$d" "$proj\$d" /E /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}
foreach ($rel in @("Assets\PSXRacing\Art\CLT", "Assets\PSXRacing\Art\CLT.meta",
                   "Assets\PSXRacing\Resources\charlotte_city.json", "Assets\PSXRacing\Resources\charlotte_city.json.meta",
                   "Assets\PSXRacing\Resources\clt_uptown.json", "Assets\PSXRacing\Resources\clt_uptown.json.meta",
                   "Assets\PSXRacing\Resources\clt_tryon.json", "Assets\PSXRacing\Resources\clt_tryon.json.meta",
                   "Assets\PSXRacing\Resources\clt_independence.json", "Assets\PSXRacing\Resources\clt_independence.json.meta")) {
    $p = Join-Path $proj $rel
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

$jobs = @()
if (-not $PreviewOnly) { $jobs += @{ Name = "city audit";   Method = "PSXRacing.EditorTools.CityAudit.Run";   Out = "city_audit.txt" } }
if (-not $AuditOnly)   { $jobs += @{ Name = "city preview"; Method = "PSXRacing.EditorTools.CityPreview.Run"; Out = $null } }
foreach ($job in $jobs) {
    Write-Host ("--- {0} ---" -f $job.Name) -ForegroundColor Cyan
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
    }
    $log = "$proj\cityjob.log"
    if (Test-Path $log) { Remove-Item $log -Force }
    $ok = Invoke-UnityJob -Log $log -MaxMinutes 25 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod",$job.Method,
        "-logFile",$log,"-accept-apiupdate")
    if (-not $ok) { Write-Host "job did not finish" -ForegroundColor Red }
    $cs = Select-String -Path $log -Pattern "error CS" | Select-Object -First 10
    if ($cs) { $cs | ForEach-Object { $_.Line } }
    $ex = Select-String -Path $log -Pattern "Exception|\[City\]|\[CityPreview\]|\[CityAudit\]" | Select-Object -First 60
    if ($ex) { $ex | ForEach-Object { $_.Line } }
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Get-Content $outFile }
        else { Write-Host ("no {0} written - the method threw; log tail:" -f $job.Out) -ForegroundColor Red
               Get-Content $log -Tail 30 }
    }
}
Write-Host "--- preview shots ---"
Get-ChildItem "$proj\Screenshots\City" -ErrorAction SilentlyContinue | ForEach-Object { "{0}  {1}" -f $_.LastWriteTime.ToString("HH:mm"), $_.Name }
Write-Host "CITY CYCLE DONE"
