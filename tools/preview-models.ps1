# Render every baked body shell and dump the catalog->shell mapping.
#
#   powershell -ExecutionPolicy Bypass -File tools\preview-models.ps1
#
# Runs WITHOUT -nographics: the preview renders through the render pipeline, and
# a headless editor has no device to render with.
$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe"
$proj  = "C:\Users\mcgee\PSXBuild"
$src   = Split-Path -Parent $PSScriptRoot

foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

function Invoke-UnityWait([string[]]$UnityArgs, [int]$MaxMinutes = 15) {
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

# Bake first: the shell prefabs are build output and live only in the sandbox,
# and the mirror above just deleted the previous run's copies.
Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarModelBaker.BakeMenu",
    "-logFile","$proj\bake.log","-accept-apiupdate")
Select-String -Path "$proj\bake.log" -Pattern "CarModelBaker|FAIL|error CS" |
    Select-Object -First 20 | ForEach-Object { $_.Line }

Invoke-UnityWait @("-quit","-batchmode","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarModelPreview.Capture",
    "-logFile","$proj\carshot.log","-accept-apiupdate")
Select-String -Path "$proj\carshot.log" -Pattern "CarShot|error CS|Exception" |
    Select-Object -First 20 | ForEach-Object { $_.Line }

Invoke-UnityWait @("-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.CarModelMappingReport.Dump",
    "-logFile","$proj\mapping.log","-accept-apiupdate")
Select-String -Path "$proj\mapping.log" -Pattern "CarModelMappingReport|error CS|Exception" |
    Select-Object -First 20 | ForEach-Object { $_.Line }
