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
         @("e177add074387204780e488ef24ed147", "PaintBooth/PaintBooth.prefab")) },
    @{ Pkg = "NatureManufacture\3D ModelsVegetation\Forest Environment - Dynamic Nature.unitypackage"
       Files = @(
         @("4cc27bd429a223f40ac611afbccac943", "NMForest/Trees/T_beech_atlas_BC.tga"),
         @("6bb59cb60f1363f49b1c4c14e99351af", "NMForest/Trees/T_beech_atlas_Maskmap.tga"),
         @("fe6cc3f9821c7cf468ab343446d0b534", "NMForest/Trees/T_beech_atlas_N.tga"),
         @("108bb32b3ae516444aa4d347cd11db83", "NMForest/Trees/T_beech_bark_01_BC.tga"),
         @("34453a332c22bbe4f8b8ace2cb142853", "NMForest/Trees/T_beech_bark_01_MaskMap.tga"),
         @("862c78df8baca6c46abe192a1f9d2b99", "NMForest/Trees/T_beech_bark_01_N.tga"),
         @("9590d59b02bd9d6438db6ec2e8872de8", "NMForest/Trees/T_beech_bark_02_BC.tga"),
         @("f35c1447312f57047a1dabfa554e5022", "NMForest/Trees/T_beech_bark_02_MaskMap.tga"),
         @("e0fa558b10ffe8b45a2254efd66d3e7f", "NMForest/Trees/T_beech_bark_02_N.tga"),
         @("6bc36933f2eb3a541b21e511ec969790", "NMForest/Trees/T_beech_trunk_02_BC.tga"),
         @("1ea360b2f877d924c9f2ccdd6a7f9e48", "NMForest/Trees/T_beech_trunk_02_MaskMap.tga"),
         @("70d17b7f7e31ec44bb922af4b4dbc780", "NMForest/Trees/T_beech_trunk_02_N.tga"),
         @("fa07e5a779d44de448d90aa654866987", "NMForest/Trees/T_beech_trunk_03_BC.tga"),
         @("4b18f34816cfc0d45ae4ce635d8e33ea", "NMForest/Trees/T_beech_trunk_03_MaskMap.tga"),
         @("bc002fdc234b9ef4d86f1b96be2ef649", "NMForest/Trees/T_beech_trunk_03_N.tga"),
         @("583a61a478e003347850106461a3e68f", "NMForest/Trees/T_beech_trunk_06_BC.tga"),
         @("b9d7e19edc777044490978287adc8a49", "NMForest/Trees/T_beech_trunk_06_MaskMap.tga"),
         @("3a558fc990e2e574ba058651e0b1c939", "NMForest/Trees/T_beech_trunk_06_N.tga"),
         @("ad52be398ab9669478ed520f21046b37", "NMForest/Trees/T_beech_trunk_07_BC.tga"),
         @("3f18eb1dd0dc9354abc71d1b7334725c", "NMForest/Trees/T_beech_trunk_07_MaskMap.tga"),
         @("8df1c4f3bfd6bd84794d054cdbd7e832", "NMForest/Trees/T_beech_trunk_07_N.tga"),
         @("da7e7750376bf2649ae825bc9d2e5739", "NMForest/Trees/beech_tree_01.FBX"),
         @("8fb09a02eea01134184ab701e75f51dd", "NMForest/Trees/beech_tree_02.FBX"),
         @("8ea4d9b8b8edcf84eb3dd453c78f0905", "NMForest/Trees/beech_tree_03.FBX"),
         @("764eea74b24d6ad44ba2a0607480b983", "NMForest/Trees/beech_tree_06.FBX"),
         @("7609a6571b8ed55499aa110c02cd764d", "NMForest/Trees/beech_tree_07.FBX"),
         @("a7205cd30aa86c740bf4e4d3dbf7b90a", "NMForest/Trees/beech_tree_08.FBX"),
         @("5af05d805d74c974286984104e05ccd9", "NMForest/Ground/T_ground_beech_forest_leaves_01_BC_SM.tga"),
         @("3905e92499e0b194587776e8f6cd4c75", "NMForest/Ground/T_ground_beech_forest_leaves_01_N.png"),
         @("9ba3ffdf81bfde0458fbee298134e5a5", "NMForest/Ground/T_ground_beech_forest_leaves_02_BC_SM.tga"),
         @("a19ce1611dc05c54fa247710907e8871", "NMForest/Ground/T_ground_beech_forest_leaves_02_N.png"),
         @("55c91db62ff79ac4a95ec25ac2e05a7e", "NMForest/Ground/T_ground_beech_forest_moss_02_BC_SM.tga"),
         @("f5f011fa4047c2a4faefe3788490b062", "NMForest/Ground/T_ground_beech_forest_moss_02_N.png"),
         @("2da5b1647d91761429a14e1b39899ad5", "NMForest/Ground/T_ground_beech_forest_plants_01_BC_SM.tga"),
         @("57fb86875251b1c40a0b2a7ff8779dca", "NMForest/Ground/T_ground_beech_forest_plants_01_N.png"),
         @("a5834c0ff764b0e4a9df15774c59b6af", "NMForest/Ground/T_ground_beech_forest_rocks_01_BC_SM.tga"),
         @("35c67879cd7202246993b5fad84a9872", "NMForest/Ground/T_ground_beech_forest_rocks_01_N.png"),
         @("2d286ecf643e7314989e0c27af290aad", "NMForest/Ground/T_ground_beech_forest_soil_01_BC_SM.tga"),
         @("352e3ca834b69d440b37ab66433b63e4", "NMForest/Ground/T_ground_beech_forest_soil_01_N.png"),
         @("745b1ac00b719f74fb3e4abf91a846cd", "NMForest/Ground/T_ground_beech_forest_stones_01_BC_SM.tga"),
         @("692f0a190c8c11043ae938fbed12869e", "NMForest/Ground/T_ground_beech_forest_stones_01_N.png"),
         @("7f7afb2ea25258e48b955e950d68498d", "NMForest/Rocks/T_beech_forest_stones_01_BC_SM.tga"),
         @("35087183b332c44418e1d4c71262a724", "NMForest/Rocks/T_beech_forest_stones_01_N.png"),
         @("dec5cddd7e32edf409f4410a4fc52b6f", "NMForest/Under/beech_tree_00_A.FBX"),
         @("35cbd4d9534afde4789fe462a90affac", "NMForest/Under/beech_tree_00_B.FBX"),
         @("b3f400b7fe747aa4eb3f6ad0ce29bd25", "NMForest/Under/beech_tree_00_C.FBX"),
         @("991374844a123ac469d5e626031905d3", "NMForest/Under/beech_tree_00_D.FBX"),
         @("32fbd98a946c7594eadbd4d79680c80e", "NMForest/Under/Forest_black_cherry_00.FBX"),
         @("53a5f4a5cfed71841863fefa353e96b8", "NMForest/Under/Forest_black_cherry_01.FBX"),
         @("d4a598ba79e8a1b48b208d38a079ed4e", "NMForest/Under/Forest_black_cherry_02.FBX"),
         @("48db8f629b5b62046938deb87352149c", "NMForest/Under/Forest_black_cherry_03.FBX"),
         @("086a7ef964ce4534ab62109983902276", "NMForest/Under/Forest_black_cherry_04.FBX"),
         @("14835cb9bdb85924299ec60f415cac06", "NMForest/Under/Forest_black_cherry_05.FBX"),
         @("951631ab54189f643956d255364fe292", "NMForest/Under/Black_cherry_MaskMap.png"),
         @("2a84d7d5bf042dd49990bde48ed10206", "NMForest/Under/Forest_black_cherry_BC.png"),
         @("65b4e508c7456014798c1b31f94b24ce", "NMForest/Under/Forest_black_cherry_N.png")) },
    @{ Pkg = "NatureManufacture\3D ModelsEnvironmentsLandscapes\Autumn Valley - Level.unitypackage"
       Files = @(
         ,@("fcfe3543dfe2f474883ede02af1c6adb", "NMForest/Trees/T_beech_atlas_BC_autumn.tga")) }
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
