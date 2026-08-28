# Mirror to the sandbox and run ONLY the scene builder — the fast loop for
# checking that scripts compile and the scene bakes, without waiting on IL2CPP.
#
#   powershell -ExecutionPolicy Bypass -File tools\scene-build-only.ps1
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$src   = Split-Path -Parent $PSScriptRoot
$proj  = "C:\Users\mcgee\PSXBuild"

New-Item -ItemType Directory -Force $proj | Out-Null
foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

$before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
Start-Process -FilePath $unity -ArgumentList @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.Build",
    "-logFile","$proj\scenebuild.log","-accept-apiupdate") -WindowStyle Hidden | Out-Null
Start-Sleep -Seconds 5
$deadline = (Get-Date).AddMinutes(25)
while ((Get-Date) -lt $deadline) {
    $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    if (-not @($now | Where-Object { $before -notcontains $_ })) { break }
    Start-Sleep -Seconds 5
}
Write-Host "--- compile errors ---"
Select-String -Path "$proj\scenebuild.log" -Pattern "error CS|BUILD FAILED" -SimpleMatch:$false |
    Select-Object -First 40 | ForEach-Object { $_.Line }
Write-Host "--- build log tail ---"
if (Test-Path "$proj\PSXRacing_build_log.txt") { Get-Content "$proj\PSXRacing_build_log.txt" -Tail 40 }
