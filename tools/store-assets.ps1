# Paid Asset Store files the builder uses. The repo is PUBLIC, so they are
# gitignored (Assets/StoreAssets/) and extracted here from the owner's local
# Asset Store cache into a checkout or sandbox. Each file keeps its package
# GUID (.meta comes from the package too), so materials and scenes that point
# at it survive a re-extract. A missing package is a warning, not an error:
# every builder that reads Assets/StoreAssets falls back to its old geometry.
#
#   powershell -ExecutionPolicy Bypass -File tools\store-assets.ps1 [-Dest <project root>]
param([string]$Dest = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = "Stop"
$cache = Join-Path $env:APPDATA "Unity\Asset Store-5.x"

# package (relative to the cache) -> list of (guid, path under Assets/StoreAssets)
$wanted = @(
    @{ Pkg = "Testmobile\3D ModelsEnvironments\UAA - Business - Paint booth.unitypackage"
       Files = @(
         @("205f38c8ca6458843ae7c41f7e35dd6e", "PaintBooth/PaintBooth.fbx"),
         @("0274b023e4dbe244fac4188afbe58795", "PaintBooth/PaintBooth.psd"),
         @("936c9acc3bbf27d4e815a9d8ad3bb4ea", "PaintBooth/PaintBooth.mat"),
         @("e177add074387204780e488ef24ed147", "PaintBooth/PaintBooth.prefab")) }
)

$root = Join-Path $Dest "Assets\StoreAssets"
foreach ($w in $wanted) {
    $pkg = Join-Path $cache $w.Pkg
    if (-not (Test-Path $pkg)) { Write-Warning "missing package: $($w.Pkg) (download it in Package Manager > My Assets)"; continue }
    $need = $w.Files | Where-Object { -not (Test-Path (Join-Path $root $_[1])) }
    if (-not $need) { Write-Host "up to date: $($w.Pkg)"; continue }
    $tmp = Join-Path $env:TEMP ("storeassets_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    try {
        $members = @(); foreach ($f in $need) { $members += "$($f[0])/asset"; $members += "$($f[0])/asset.meta" }
        & "$env:SystemRoot\System32\tar.exe" -xzf $pkg -C $tmp @members
        if ($LASTEXITCODE -ne 0) { throw "tar failed on $pkg" }
        foreach ($f in $need) {
            $out = Join-Path $root $f[1]
            New-Item -ItemType Directory -Force (Split-Path -Parent $out) | Out-Null
            Copy-Item (Join-Path $tmp "$($f[0])\asset") $out -Force
            Copy-Item (Join-Path $tmp "$($f[0])\asset.meta") "$out.meta" -Force
            Write-Host "extracted $($f[1])"
        }
    } finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
}
