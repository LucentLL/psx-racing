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
         @("65b4e508c7456014798c1b31f94b24ce", "NMForest/Under/Forest_black_cherry_N.png"),
         @("d9e406607ec43bb44a5d25896ca10075", "NMForest/Trees/T_beech_tree_01_BC_A.png"),
         @("fa54f3d0151ca2d43a51901a1f39c3a2", "NMForest/Trees/T_beech_tree_01_M.png"),
         @("51f275e9ec1c6644b9e93c3b844c73cc", "NMForest/Trees/T_beech_tree_01_N.png"),
         @("acdc4b1379c8aea4683ee7c532184d44", "NMForest/Trees/T_beech_tree_02_BC_A.png"),
         @("b4725fcdaac7d234b9c91e5fa1ac6cac", "NMForest/Trees/T_beech_tree_02_M.png"),
         @("8dc1fe65232e099479490c534838925a", "NMForest/Trees/T_beech_tree_02_N.png"),
         @("574938b2ff13a1b4a90a3f3c7c57d313", "NMForest/Trees/T_beech_tree_03_BC_A.png"),
         @("cf2b2b1461409964fbb01f9214b07c1f", "NMForest/Trees/T_beech_tree_03_M.png"),
         @("a3f5d88d59fe3924998c67c52fc4adcf", "NMForest/Trees/T_beech_tree_03_N.png"),
         @("b687f3544da252b4cb6a93f9e55c632c", "NMForest/Trees/T_beech_tree_06_BC_A.png"),
         @("9df0d2464b3be3d4184ef64cd721d47a", "NMForest/Trees/T_beech_tree_06_M.png"),
         @("909bf24dc6417e744bbf2d60f923a4d6", "NMForest/Trees/T_beech_tree_06_N.png"),
         @("128b893858c3ad2428afaec6c5dfa7d6", "NMForest/Trees/T_beech_tree_07_BC_A.png"),
         @("34930c7dd29171246bd33e9e70d3041c", "NMForest/Trees/T_beech_tree_07_M.png"),
         @("40faa368496056c4ba8cf0115284d117", "NMForest/Trees/T_beech_tree_07_N.png"),
         @("f2bad4d243eea3d4abd955b36294834f", "NMForest/Trees/T_beech_tree_08_BC_A.png"),
         @("0b4a8df126e503b45a05443f9ee578f5", "NMForest/Trees/T_beech_tree_08_M.png"),
         @("b0a1c116310068a41902be4476f194f1", "NMForest/Trees/T_beech_tree_08_N.png"),
         @("17662ef9a5452e94ca19272ebd546eca", "NMForest/Trees/beech_tree_01_s.FBX"),
         @("1c7938ea3bf4b9d40a84d28fa49f5213", "NMForest/Trees/beech_tree_02_s.FBX"),
         @("52a42d4a3458cd044b5228fb61273cfa", "NMForest/Trees/beech_tree_03_s.FBX"),
         @("1f7ada168ea1ee1429a70e04742eff49", "NMForest/Trees/beech_tree_06_s.FBX"),
         @("ced3325ba33f95947aee1e75c98c5698", "NMForest/Trees/beech_tree_07_s.FBX"),
         @("c6e42d07694d77e43b3df8e506a6085a", "NMForest/Trees/beech_tree_08_s.FBX"),
         @("66f3925eef7750a4e8a6309cea7ba162", "NMForest/Cover/T_fern_beech_forest_01_BC_M.tga"),
         @("02d6ddbf419327245b430554923b9085", "NMForest/Cover/T_fern_beech_forest_01_MT_AO_SM.tga"),
         @("f31a956c97ee87542b7f71bafcd6bac1", "NMForest/Cover/T_fern_beech_forest_01_N.png"),
         @("9e29e24fd0262414abec75fd4f12d0f0", "NMForest/Cover/T_grass_beech_forest_01_BC_M.tga"),
         @("67d012a6752cb2b47a6b5b2f97f36dc6", "NMForest/Cover/T_grass_beech_forest_01_MT_AO_SM.tga"),
         @("411e30eecfedf884da59a95cf292ce2a", "NMForest/Cover/T_grass_beech_forest_01_N.png"),
         @("6dbe9058e0c62894dbbf95dbfdff6984", "NMForest/Cover/T_grass_beech_forest_02_BC_M.tga"),
         @("a7512b6d967bd96478796722c4bb797a", "NMForest/Cover/T_grass_beech_forest_02_MT_AO_SM.tga"),
         @("1bb667c911bd3474596944d212e7c5c9", "NMForest/Cover/T_grass_beech_forest_02_N.png"),
         @("1bae9eb90e3b17344a81f8fd598cc293", "NMForest/Cover/T_grass_beech_forest_03_BC_M.tga"),
         @("998ab74471d0e2e48b780e72c687799b", "NMForest/Cover/T_grass_beech_forest_03_MT_AO_SM.tga"),
         @("76798a4cc54082d47a24935f5793c71a", "NMForest/Cover/T_grass_beech_forest_03_N.png"),
         @("549e78d8244885d40a804274b1123e30", "NMForest/Cover/T_plant_beech_forest_01_AO_SM.tga"),
         @("baca900bcdf4d5e4eb2a9cbd7dd7a11c", "NMForest/Cover/T_plant_beech_forest_01_BC_M.tga"),
         @("4913ff5de82df974196188bc44b41ef2", "NMForest/Cover/T_plant_beech_forest_01_N.png"),
         @("7350bda4517884542b2221b73606833c", "NMForest/Cover/T_plant_beech_forest_02_BC_M.tga"),
         @("90686d1997ff2c541a6102d5bfba3b00", "NMForest/Cover/T_plant_beech_forest_02_MT_AO_SM.tga"),
         @("e35bfe1596382b84eb8e6d48917db8b9", "NMForest/Cover/T_plant_beech_forest_02_N.png"),
         @("34097f786da9b5b43b3468f133b4ce19", "NMForest/Cover/T_plant_beech_forest_03_AO_SM.tga"),
         @("b6a6ef3f0a0669d42a29393e47bbfa30", "NMForest/Cover/T_plant_beech_forest_03_BC_M.tga"),
         @("67703f856ec80874e8d6ed141ec4dc4a", "NMForest/Cover/T_plant_beech_forest_03_N.png"),
         @("ccd6965d521800c47a53d0e98fa9980d", "NMForest/Cover/T_shaggy_soldier_beech_forest_01_AO_SM.tga"),
         @("d95c31533e2865949a3f809e2efd6864", "NMForest/Cover/T_shaggy_soldier_beech_forest_01_BC_M.tga"),
         @("101a0988238469443adca888279b79b5", "NMForest/Cover/T_shaggy_soldier_beech_forest_01_N.png"),
         @("b057516a5b05d9b4f8965eb46886c45f", "NMForest/Cover/foliage_fern_01_1.fbx"),
         @("1cdfae11bca99944295f991c48e52b65", "NMForest/Cover/foliage_fern_01_2.fbx"),
         @("344986d05ad5aed4db9af8a1ec3a156a", "NMForest/Cover/foliage_fern_01_3.fbx"),
         @("bd529c55f12bc9840a9f622f43021825", "NMForest/Cover/foliage_fern_01_4.fbx"),
         @("0fa07d932dfb84c4b8d3fba76793e5c1", "NMForest/Cover/grass_beech_forest_01_1.fbx"),
         @("ba54c1401f1ebd3409aa51649e84c4d8", "NMForest/Cover/grass_beech_forest_01_2.fbx"),
         @("00bd8af57d588ad40a10cc63f59866b2", "NMForest/Cover/grass_beech_forest_01_3.fbx"),
         @("6bc894472945e0f47a893d45e8a88148", "NMForest/Cover/grass_beech_forest_01_4.fbx"),
         @("02a7ac27dfe42bf459d84cb1742a2390", "NMForest/Cover/grass_beech_forest_02_1.fbx"),
         @("aebd439a77f456c489696f8cf3a65ea8", "NMForest/Cover/grass_beech_forest_02_2.fbx"),
         @("f0d216df985a5f64fbf3741a9743ed3b", "NMForest/Cover/grass_beech_forest_02_3.fbx"),
         @("74256eacdc260024aad6db17290785e8", "NMForest/Cover/grass_beech_forest_03_1.fbx"),
         @("1f7508c82ff38aa4883a5ca27cd964be", "NMForest/Cover/grass_beech_forest_03_2.fbx"),
         @("70476273fc8e7f646ba881cc54df9661", "NMForest/Cover/grass_beech_forest_03_3.fbx"),
         @("aad64395b6e778a44af735d6b8d847e1", "NMForest/Cover/plant_beech_forest_01_1.fbx"),
         @("9b8dff9dfda6af948ac3c086810ddde0", "NMForest/Cover/plant_beech_forest_01_2.fbx"),
         @("1fb5bca18fbbabf4db732b2dcd7cd7ab", "NMForest/Cover/plant_beech_forest_01_3.fbx"),
         @("156548a23a9ce0c4ea689be2bd84bc24", "NMForest/Cover/plant_beech_forest_01_4.fbx"),
         @("08644d9068b29a84595a75945888db13", "NMForest/Cover/plant_beech_forest_02_1.fbx"),
         @("26e1302f3c195854da5a9b52e8b9992e", "NMForest/Cover/plant_beech_forest_02_2.fbx"),
         @("4a6f660ffffb173408d7d410b66513e1", "NMForest/Cover/shaggy_soldier_beech_forest_01_1.fbx"),
         @("5581eaa30d0491f4a8c3a28599fcd2c6", "NMForest/Cover/shaggy_soldier_beech_forest_01_2.fbx"),
         @("27efaf628d9ee164ba60280eae9aae3b", "NMForest/Cover/shaggy_soldier_beech_forest_01_3.fbx"),
         @("3b444d2f019180f4eab3c664beddc121", "NMForest/Debris/T_beech_forest_details_01_BC_M.tga"),
         @("0166341a389ff1f4a927c60b48635665", "NMForest/Debris/T_beech_forest_details_01_N.png"),
         @("0a2b0b0b47d278444b57d4ddb0ed8bb9", "NMForest/Debris/T_beech_forest_moss_01_BC_SM.tga"),
         @("1dbef6b018e442b428e2c49c77d227c6", "NMForest/Debris/T_beech_forest_moss_01_N.png"),
         @("2e405d042a8ab494281cc7b750698f71", "NMForest/Debris/beech_forest_detail_branches_01.fbx"),
         @("7562ffd725b457e41a97675ff8da09e6", "NMForest/Debris/beech_forest_detail_branches_02.fbx"),
         @("bc3db5393581997419209ff2aa52e139", "NMForest/Debris/beech_forest_detail_branches_03.fbx"),
         @("7789395437417a14098169276bac23f2", "NMForest/Debris/beech_forest_detail_leaves_01.fbx"),
         @("2b33ed0b774cf9349a1504040214e14b", "NMForest/Debris/beech_forest_detail_leaves_02.fbx"),
         @("3de8d26af8481e44cabeddfd4eb4a162", "NMForest/Debris/beech_forest_detail_leaves_03.fbx"),
         @("eabb086fc275ddf4d9902a7d2c9a1b22", "NMForest/Debris/beech_forest_detail_moss_01_1.fbx"),
         @("13ebb65a800874844b22b38953976d5f", "NMForest/Debris/beech_forest_detail_moss_01_2.fbx"),
         @("13300af2397606440935590ab87217b5", "NMForest/Debris/beech_forest_stones_01_1.fbx"),
         @("6f831031093f2fb42bb43598bd99dd4c", "NMForest/Debris/beech_forest_stones_01_10.fbx"),
         @("8d3cc89fc2a57134e8c7ac65f6d8698f", "NMForest/Debris/beech_forest_stones_01_11.fbx"),
         @("29c22f234f1f7694dbfbcbfcc7bfc8be", "NMForest/Debris/beech_forest_stones_01_12.fbx"),
         @("c066145505851ec4daf1e746d0c16e08", "NMForest/Debris/beech_forest_stones_01_13.fbx"),
         @("4f3e969432bf95242a4f07518d6c9c4e", "NMForest/Debris/beech_forest_stones_01_14.fbx"),
         @("55b6e49353c3d6a448dbaaa14e65a89a", "NMForest/Debris/beech_forest_stones_01_2.fbx"),
         @("f9e1657da836ed24d90af5adc9863fe5", "NMForest/Debris/beech_forest_stones_01_3.fbx"),
         @("6a34f645448be3c42a8307259f1ffb1f", "NMForest/Debris/beech_forest_stones_01_4.fbx"),
         @("3e3a949e2ac70c544b93b3cfc9dd916e", "NMForest/Debris/beech_forest_stones_01_5.fbx"),
         @("1080dfb56cb132c4c8922a48e161b7f1", "NMForest/Debris/beech_forest_stones_01_6.fbx"),
         @("c89ab33515cb8744194d4240ba904eff", "NMForest/Debris/beech_forest_stones_01_7.fbx"),
         @("6136fcd9297d0b24cbb72b6aa0a44bd3", "NMForest/Debris/beech_forest_stones_01_8.fbx"),
         @("8008a18e065decc4dacd6aeffbb0c7c2", "NMForest/Debris/beech_forest_stones_01_9.fbx"),
         @("f963ab18113bcc141975c4e7c686005b", "NMForest/Debris/T_beech_dead_logs_01_BC.tga"),
         @("7e7dcf44b0cbd404caa30cf19ca1fcda", "NMForest/Debris/T_beech_dead_logs_01_N.tga"),
         @("a8cdb86f6c086454a9a321ad467076c6", "NMForest/Debris/T_beech_forest_branches_01_BC_SM.tga"),
         @("149ad1d68baaca34884551a9b0ec4ec0", "NMForest/Debris/T_beech_forest_branches_01_N.png"),
         @("c07a53f7c49026948908c4bdf8fc7916", "NMForest/Debris/T_beech_forest_stumps_01_BC_SM.tga"),
         @("39f3a9e544d22644d926bffd09c8bb27", "NMForest/Debris/T_beech_forest_stumps_01_N.png"),
         @("d2b06831757154442b701bd22cac5c51", "NMForest/Debris/beech_forest_branch_01_1.fbx"),
         @("b29836f792eca8e4eb46d9b8b5c3e79a", "NMForest/Debris/beech_forest_branch_01_2.fbx"),
         @("d583664470c56584f809193ebdf518c0", "NMForest/Debris/beech_forest_branch_01_3.fbx"),
         @("8427f5c0fa9165949a838fd1eb316a26", "NMForest/Debris/beech_forest_branch_01_4.fbx"),
         @("8cc504f5d27298a4d9430b662219b690", "NMForest/Debris/beech_forest_branch_01_5.fbx"),
         @("04838d019d445af4bba64d1b0f83c3d2", "NMForest/Debris/beech_forest_stump_01_1.fbx"),
         @("045b5f5ce40e6a145a5f0521fc478cc4", "NMForest/Debris/beech_forest_stump_01_2.fbx"),
         @("1494edb7de029294da2f248c81e37d02", "NMForest/Debris/beech_forest_stump_01_3.fbx"),
         @("709ea712aad68464aaea5739fa45092a", "NMForest/Debris/dead_log_01.FBX"),
         @("e3644191f061d994b969b5258672f3b0", "NMForest/Debris/dead_log_02.FBX"),
         @("68a386d7f664d1846be1bba575dabccd", "NMForest/Debris/dead_log_03.FBX"),
         @("75a31514d9c930744802dbf2bd81b58a", "NMForest/Debris/dead_log_04.FBX"),
         @("90c7bf6c95da84545be7be9c32a31913", "NMForest/Debris/dead_log_05.FBX")) },
    @{ Pkg = "NatureManufacture\3D ModelsEnvironmentsLandscapes\Autumn Valley - Level.unitypackage"
       Files = @(
         @("fcfe3543dfe2f474883ede02af1c6adb", "NMForest/Trees/T_beech_atlas_BC_autumn.tga"),
         @("ace9571fd70fe5e4598b3ec2b64202b3", "NMForest/Autumn/T_beech_tree_01.png"),
         @("14891d73ab7b0f04c8ba592783f41279", "NMForest/Autumn/T_beech_tree_02.png"),
         @("b316ba5d18f55d54c8b4d466287d5c74", "NMForest/Autumn/T_beech_tree_03.png"),
         @("d048d3e310eccce44b7d6b4cd6045b80", "NMForest/Autumn/T_beech_tree_06.png"),
         @("070f112b5aa63d34a8c2abe87702ac52", "NMForest/Autumn/T_beech_tree_07.png"),
         @("109ffc61c782fb04b81bf70196d89443", "NMForest/Autumn/T_beech_tree_08.png")) }
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
