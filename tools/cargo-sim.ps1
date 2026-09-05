# Bake the pizza cargo and photograph the physics harness.
#
#   powershell -ExecutionPolicy Bypass -File tools\cargo-sim.ps1
#
# Two Unity runs, in this order and for this reason: the bake writes the
# prefabs the sim stands up, so a sim run against a stale bake photographs the
# LAST set of boxes. Syncs code only -- a /MIR of Assets would delete the built
# scenes, and nothing here needs them.
$ErrorActionPreference = "Stop"
$proj = "C:\Users\mcgee\PSXBuild"
$src  = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\unity-wait.ps1"

foreach ($d in @("Assets\PSXRacing\Scripts", "Assets\PSXRacing\Editor", "Assets\PSXRacing\Shaders")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
}

Write-Host "--- bake ---" -ForegroundColor Cyan
Invoke-UnityJob -Log "$proj\cargobake.log" -UnityArgs @(
    "-quit","-batchmode","-nographics","-projectPath",$proj,
    "-executeMethod","PSXRacing.EditorTools.PizzaCargoBaker.Bake",
    "-logFile","$proj\cargobake.log","-accept-apiupdate") | Out-Null
Select-String -Path "$proj\cargobake.log" -Pattern "\[PizzaCargo\]" |
    ForEach-Object { $_.Line }

# WITH graphics: the sim shoots the cargo through the render pipeline, and a
# headless editor has no device to render with.
Write-Host "--- sim ---" -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\cargosim.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.PizzaCargoSim.Shoot",
        "-logFile","$proj\cargosim.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\cargosim.log" -Pattern "\[PizzaSim\] at rest" -Quiet) { break }
    Write-Host "sim attempt $i did not finish, retrying..."
}
Select-String -Path "$proj\cargosim.log" -Pattern "\[PizzaSim\]" |
    ForEach-Object { $_.Line }
Write-Host "shots -> $proj\Screenshots\PizzaCargo"

# The carry rig, from the eye that carries it. Not part of Shoot: nothing is
# simulated here and nothing is graded -- the only thing that can be wrong is
# the framing, and that is a picture.
Write-Host "--- carry ---" -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    Invoke-UnityJob -Log "$proj\cargocarry.log" -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod","PSXRacing.EditorTools.PizzaCargoSim.ShootCarry",
        "-logFile","$proj\cargocarry.log","-accept-apiupdate") | Out-Null
    if (Select-String -Path "$proj\cargocarry.log" -Pattern "\[PizzaCarry\]" -Quiet) { break }
    Write-Host "carry attempt $i did not finish, retrying..."
}
Select-String -Path "$proj\cargocarry.log" -Pattern "\[PizzaCarry\]" |
    ForEach-Object { $_.Line }
