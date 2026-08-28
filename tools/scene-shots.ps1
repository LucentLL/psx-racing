# Mirror, build the scene, and capture the verification screenshots.
#
#   powershell -ExecutionPolicy Bypass -File tools\scene-shots.ps1
#
# The capture pass runs WITHOUT -nographics: it renders through the pipeline,
# and a headless editor has no device to render with.
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

function Invoke-UnityWait([string[]]$UnityArgs, [int]$MaxMinutes = 25) {
    $before = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
    Start-Process -FilePath $unity -ArgumentList $UnityArgs -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 5
    $deadline = (Get-Date).AddMinutes($MaxMinutes)
    while ((Get-Date) -lt $deadline) {
        $now = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object Id)
        if (-not @($now | Where-Object { $before -notcontains $_ })) { return }
        Start-Sleep -Seconds 5
    }
}

Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXRacingBuilder.Build",
    "-logFile","$proj\scenebuild.log","-accept-apiupdate")
Select-String -Path "$proj\scenebuild.log" -Pattern "error CS" |
    Select-Object -First 20 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_build_log.txt") { Get-Content "$proj\PSXRacing_build_log.txt" -Tail 12 }

Invoke-UnityWait @("-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PSXScreenshotTool.Capture",
    "-logFile","$proj\shots.log","-accept-apiupdate")
Select-String -Path "$proj\shots.log" -Pattern "PSXShot|error CS|Exception" |
    Select-Object -First 10 | ForEach-Object { $_.Line }
