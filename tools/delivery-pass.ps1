# The 2026-08-29 delivery pass: rebuild the scenes, photograph the pizza shop
# with the vertex snap on AND off at the resolution the game runs at, and run
# the self-test.
#
#   powershell -ExecutionPolicy Bypass -File tools\delivery-pass.ps1
#
# The snap A/B is the point of this one. Every preview tool in the project has
# always forced _PSXSnap to 0 before shooting, so no screenshot pass has ever
# been able to see the artefact that was reported ("many textures are
# interfering when moving"). PizzeriaPreview now shoots the pair deliberately.
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\unity-wait.ps1"
$src  = Split-Path -Parent $PSScriptRoot
$proj = "C:\Users\mcgee\PSXBuild"

Write-Host "--- mirror ---" -ForegroundColor Cyan
foreach ($d in @("Assets", "Packages", "ProjectSettings")) {
    robocopy "$src\$d" "$proj\$d" /MIR /NFL /NDL /NJH /NJS /NP /MT:8 /R:1 /W:1 | Out-Null
}

Write-Host "--- scene build ---" -ForegroundColor Cyan
if (-not (Invoke-SceneBuild -Proj $proj)) {
    Write-Host "SCENE BUILD FAILED" -ForegroundColor Red
    Get-Content "$proj\scenebuild.log" -Tail 30
    exit 1
}
Get-Content "$proj\PSXRacing_build_log.txt" -Tail 12

# The previews render, so NO -nographics here. A headless editor returns black
# frames from cam.Render() and the pass certifies nothing.
$jobs = @(
    @{ Name = "pizzeria preview"; Method = "PSXRacing.EditorTools.PizzeriaPreview.Run"; Out = $null },
    @{ Name = "self-test"; Method = "PSXRacing.EditorTools.LifeSimSelfTest.Run"; Out = "PSXRacing_selftest_log.txt" }
)

foreach ($job in $jobs) {
    Write-Host ("--- {0} ---" -f $job.Name) -ForegroundColor Cyan
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        # Delete first: a -executeMethod that throws never writes its file, and
        # the previous run's copy reads exactly like a fresh one.
        if (Test-Path $outFile) { Remove-Item $outFile -Force }
    }
    $ok = Invoke-UnityJob -Log "$proj\deliveryjob.log" -MaxMinutes 25 -UnityArgs @(
        "-quit","-batchmode","-projectPath",$proj,
        "-executeMethod",$job.Method,
        "-logFile","$proj\deliveryjob.log","-accept-apiupdate")
    if (-not $ok) { Write-Host "job did not finish" -ForegroundColor Red }
    $cs = Select-String -Path "$proj\deliveryjob.log" -Pattern "error CS" | Select-Object -First 10
    if ($cs) { $cs | ForEach-Object { $_.Line } }
    $notes = Select-String -Path "$proj\deliveryjob.log" -Pattern "\[PizzaShot\]|\[Pizzeria\]" |
             Select-Object -First 14
    if ($notes) { $notes | ForEach-Object { $_.Line } }
    if ($job.Out) {
        $outFile = Join-Path $proj $job.Out
        if (Test-Path $outFile) { Get-Content $outFile }
        else {
            Write-Host ("no {0} written - the method threw; log tail:" -f $job.Out) -ForegroundColor Red
            Get-Content "$proj\deliveryjob.log" -Tail 30
        }
    }
}

Write-Host "--- shots ---" -ForegroundColor Cyan
Get-ChildItem "$proj\Screenshots\Pizzeria" -Filter *.png -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    ForEach-Object { "{0}  {1}" -f $_.LastWriteTime.ToString("HH:mm"), $_.FullName }
