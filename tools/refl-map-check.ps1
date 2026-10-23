# THE REFLECTION MAPPING + FLICKER CHECK (ReflectionMapCheck, 2026-10-23) on an
# already-built sandbox: Scripts, Editor, Shaders and the always-included
# shader list are copied over first, then
# PSXRacing.EditorTools.ReflectionMapCheck.Run renders through the pipeline
# (graphics ON) under OpenGL Core (the WebGL conventions) and/or Direct3D 11.
# Prints the [ReflMap] lines; exit 1 unless every run says RESULT PASS.
#
#   powershell -ExecutionPolicy Bypass -File tools\refl-map-check.ps1                 both APIs, map only
#   ... -Api gl -Parts map,flicker -Tag before -Out <dir>    + the rainy-street flicker measure, PNGs copied out
param([string]$Api = "both", [string]$Parts = "map", [string]$Tag = "now", [string]$Out = "",
      [int]$Frames = 90, [switch]$NoCopy)
$ErrorActionPreference = "Stop"
$proj = if ($env:PSX_SANDBOX) { $env:PSX_SANDBOX } else { "C:\Users\mcgee\PSXRec" }
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

if (-not $NoCopy) {
    foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
        robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    }
    robocopy "$src\Assets\PSXRacing\Resources" "$proj\Assets\PSXRacing\Resources" /E /XO /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
    Copy-Item "$src\ProjectSettings\GraphicsSettings.asset" "$proj\ProjectSettings\GraphicsSettings.asset" -Force
}
Remove-Item "$proj\Screenshots\reflmap_${Tag}_*.png" -ErrorAction SilentlyContinue

$env:PSX_REFLMAP_PARTS = $Parts
$env:PSX_REFLMAP_TAG = $Tag
$env:PSX_REFLMAP_FRAMES = "$Frames"
$apis = if ($Api -eq "both") { @("gl", "d3d") } else { @($Api) }
$allPass = $true
foreach ($a in $apis) {
    $log = "$proj\reflmap_$a.log"
    Remove-Item $log -ErrorAction SilentlyContinue
    $uargs = @("-quit", "-batchmode", "-projectPath", $proj,
              "-executeMethod", "PSXRacing.EditorTools.ReflectionMapCheck.Run",
              "-logFile", $log, "-accept-apiupdate")
    if ($a -eq "gl") { $uargs += "-force-glcore" } else { $uargs += "-force-d3d11" }
    Invoke-UnityJob -Log $log -MaxMinutes 30 -UnityArgs $uargs | Out-Null
    Write-Host "---- $a ----"
    Select-String -Path $log -Pattern "\[ReflMap\]|error CS|Shader error|Exception" |
        Select-Object -First 80 | ForEach-Object { $_.Line }
    if (-not (Select-String -Path $log -Pattern "\[ReflMap\] RESULT PASS" -Quiet)) { $allPass = $false }
}
if ($Out) {
    $dest = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $src $Out }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-ChildItem "$proj\Screenshots" -Filter "reflmap_${Tag}_*.png" | Copy-Item -Destination $dest -Force
    Write-Host "copied reflmap_${Tag}_*.png to $dest"
}
if (-not $allPass) { Write-Host "REFLECTION MAP CHECK: FAIL"; exit 1 }
Write-Host "REFLECTION MAP CHECK: PASS"
