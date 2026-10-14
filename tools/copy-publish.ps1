# Push main, copy the Assets/ProjectSettings/Packages files changed since $Base (the last
# PUBLISHED commit) into PSXBuild - a -SkipScenes publish does not mirror them - then publish
# both editions from one commit. Usage: tools\copy-publish.ps1 -Base <last live commit>
# ProjectSettings matters: a new Always-Included shader lives in GraphicsSettings.asset.
param([Parameter(Mandatory=$true)][string]$Base)
$ErrorActionPreference = "Stop"
$main = "C:\Users\mcgee\PSX Racing"
$sb   = "C:\Users\mcgee\PSXBuild"
Set-Location $main
if (git status --porcelain --untracked-files=no) { throw "main tree is not clean" }
git push origin main; if ($LASTEXITCODE -ne 0) { throw "push failed" }
foreach ($line in (git diff --name-status "$Base" HEAD)) {
    $parts = $line -split "`t"; $st = $parts[0]; $path = $parts[-1]
    if (-not ($path.StartsWith("Assets/") -or $path.StartsWith("ProjectSettings/") -or $path.StartsWith("Packages/"))) { continue }
    $dst = Join-Path $sb $path
    if ($st -like "D*") { Remove-Item $dst -Force -ErrorAction SilentlyContinue; Write-Host "deleted $path" }
    else { New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null; Copy-Item (Join-Path $main $path) $dst -Force; Write-Host "copied  $path" }
    if ($st -like "R*") { Remove-Item (Join-Path $sb $parts[1]) -Force -ErrorAction SilentlyContinue }
}
& powershell -ExecutionPolicy Bypass -File "$main\tools\build-and-publish.ps1" -Both -SkipScenes
exit $LASTEXITCODE
