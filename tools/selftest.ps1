# Mirror, bake the car models, and run the LifeSim self-test in the sandbox.
#
#   powershell -ExecutionPolicy Bypass -File tools\selftest.ps1
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

function Invoke-UnityWait([string[]]$UnityArgs, [int]$MaxMinutes = 20) {
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

# The shell prefabs are build output; the mirror above just deleted them.
Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
    "-logFile","$proj\bake.log","-accept-apiupdate")

Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.LifeSimSelfTest.Run",
    "-logFile","$proj\selftest.log","-accept-apiupdate")
Select-String -Path "$proj\selftest.log" -Pattern "error CS" | Select-Object -First 10 | ForEach-Object { $_.Line }
if (Test-Path "$proj\PSXRacing_selftest_log.txt") { Get-Content "$proj\PSXRacing_selftest_log.txt" }
