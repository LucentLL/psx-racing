using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The mountain stages' MESH forest (owner 2026-11-07), from the owner's
    /// NatureManufacture "Forest Environment - Dynamic Nature" (beeches, small
    /// beeches, black cherry bushes, forest floors) and "Autumn Valley" (the
    /// autumn leaf atlas). Paid store content: it lives in the gitignored
    /// Assets/StoreAssets (tools/store-assets.ps1); without it, or outside the
    /// PC scene pass (PCContent: the web player's data file sits near GitHub's
    /// 100 MB limit), the stage is the cards and turf it always was.
    ///
    /// CANOPY: six of the pack's ten beeches, each with the pack's own three
    /// mesh LODs (HIGH / MID / LOW), chosen for height and triangle count
    /// (LOD0 5-11k; the 15k and 22k ones are left out).
    /// UNDERSTORY (owner, first A/B: "A LOT LESS trees. That is not the desired
    /// result. It should look like the provided videos of the roads"): the
    /// pack's beeches carry their crowns high, so the canopy alone left the
    /// hillside showing between bare trunks. The four small beeches (3-6 m)
    /// and six black cherry bushes (native to the Blue Ridge) stand round each
    /// canopy tree, two LODs each, drawn to <see cref="UnderMaxDist"/>.
    ///
    /// URP Lit materials of the pack's maps: base colour, tangent normal, and
    /// its HDRP-style mask (R metal, G AO, A smoothness) as URP's
    /// metallic-smoothness and occlusion maps. GPU instancing on every one.
    /// Leaves per dress (Winter, Spring, Summer, Fall, Snow): bare in winter and
    /// snow; in fall each variant wears its own tint of the autumn atlas (red,
    /// orange, gold, late green) so a hillside is not one flat orange.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string NMTreeDir = "Assets/StoreAssets/NMForest/Trees";
        const string NMUnderDir = "Assets/StoreAssets/NMForest/Under";
        const string NMDir = "Assets/StoreAssets/NMForest";
        const string NMCoverDir = "Assets/StoreAssets/NMForest/Cover";
        /// <summary>Ground cover: FBX stem -> texture stem.</summary>
        static readonly (string fbx, string tex)[] NMCover =
        {
            ("grass_beech_forest_01_1", "T_grass_beech_forest_01"), ("grass_beech_forest_01_2", "T_grass_beech_forest_01"),
            ("grass_beech_forest_01_3", "T_grass_beech_forest_01"), ("grass_beech_forest_01_4", "T_grass_beech_forest_01"),
            ("grass_beech_forest_02_1", "T_grass_beech_forest_02"), ("grass_beech_forest_02_2", "T_grass_beech_forest_02"),
            ("grass_beech_forest_02_3", "T_grass_beech_forest_02"),
            ("grass_beech_forest_03_1", "T_grass_beech_forest_03"), ("grass_beech_forest_03_2", "T_grass_beech_forest_03"),
            ("grass_beech_forest_03_3", "T_grass_beech_forest_03"),
            ("foliage_fern_01_1", "T_fern_beech_forest_01"), ("foliage_fern_01_2", "T_fern_beech_forest_01"),
            ("foliage_fern_01_3", "T_fern_beech_forest_01"), ("foliage_fern_01_4", "T_fern_beech_forest_01"),
            ("plant_beech_forest_01_1", "T_plant_beech_forest_01"), ("plant_beech_forest_01_2", "T_plant_beech_forest_01"),
            ("plant_beech_forest_01_3", "T_plant_beech_forest_01"), ("plant_beech_forest_01_4", "T_plant_beech_forest_01"),
            ("plant_beech_forest_02_1", "T_plant_beech_forest_02"), ("plant_beech_forest_02_2", "T_plant_beech_forest_02"),
            ("shaggy_soldier_beech_forest_01_1", "T_shaggy_soldier_beech_forest_01"),
            ("shaggy_soldier_beech_forest_01_2", "T_shaggy_soldier_beech_forest_01"),
            ("shaggy_soldier_beech_forest_01_3", "T_shaggy_soldier_beech_forest_01"),
        };
        const string NMDebrisDir = "Assets/StoreAssets/NMForest/Debris";
        /// <summary>The forest floor's litter and wood (owner 2026-11-08: "the
        /// ground doesn't [look] anywhere nearly as detailed as the asset pack
        /// displayed"): FBX, texture stem (dir-relative), kind - 0 litter
        /// patch (alpha-cut, two-sided, gone under snow), 1 fallen branch,
        /// 2 stone, 3 log, 4 stump.</summary>
        static readonly (string fbx, string dir, string tex, int kind)[] NMDebris =
        {
            ("beech_forest_detail_leaves_01", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_detail_leaves_02", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_detail_leaves_03", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_detail_branches_01", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_detail_branches_02", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_detail_branches_03", NMDebrisDir, "T_beech_forest_details_01", 0),
            ("beech_forest_branch_01_1", NMDebrisDir, "T_beech_forest_branches_01", 1),
            ("beech_forest_branch_01_2", NMDebrisDir, "T_beech_forest_branches_01", 1),
            ("beech_forest_branch_01_3", NMDebrisDir, "T_beech_forest_branches_01", 1),
            ("beech_forest_branch_01_4", NMDebrisDir, "T_beech_forest_branches_01", 1),
            ("beech_forest_branch_01_5", NMDebrisDir, "T_beech_forest_branches_01", 1),
            ("beech_forest_stones_01_1", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_2", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_3", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_4", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_5", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_6", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_7", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_8", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_9", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("beech_forest_stones_01_10", "Assets/StoreAssets/NMForest/Rocks", "T_beech_forest_stones_01", 2),
            ("dead_log_01", NMDebrisDir, "T_beech_dead_logs_01", 3),
            ("dead_log_02", NMDebrisDir, "T_beech_dead_logs_01", 3),
            ("dead_log_03", NMDebrisDir, "T_beech_dead_logs_01", 3),
            ("dead_log_04", NMDebrisDir, "T_beech_dead_logs_01", 3),
            ("beech_forest_stump_01_1", NMDebrisDir, "T_beech_forest_stumps_01", 4),
            ("beech_forest_stump_01_2", NMDebrisDir, "T_beech_forest_stumps_01", 4),
            ("beech_forest_stump_01_3", NMDebrisDir, "T_beech_forest_stumps_01", 4),
        };

        /// <summary>Ground cover is drawn to this (m), no shadows.</summary>
        const float CoverMaxDist = 60f;
        /// <summary>Ground cover grid (m) and how far from the road it runs.</summary>
        const float CoverPitch = 1.5f, CoverBand = 50f;
        // tree name, bark texture stem (the FBX's first material slot)
        static readonly (string tree, string bark)[] NMTrees =
        {
            ("beech_tree_01", "T_beech_bark_02"),
            ("beech_tree_02", "T_beech_trunk_02"),
            ("beech_tree_03", "T_beech_trunk_03"),
            ("beech_tree_06", "T_beech_trunk_06"),
            ("beech_tree_07", "T_beech_trunk_07"),
            ("beech_tree_08", "T_beech_bark_02"),
        };
        static readonly string[] NMSmallBeech = { "beech_tree_00_A", "beech_tree_00_B", "beech_tree_00_C", "beech_tree_00_D" };
        static readonly string[] NMBushes =
        {
            "Forest_black_cherry_00", "Forest_black_cherry_01", "Forest_black_cherry_02",
            "Forest_black_cherry_03", "Forest_black_cherry_04", "Forest_black_cherry_05",
        };
        /// <summary>How far the understory is drawn (m).</summary>
        const float UnderMaxDist = 150f;
        /// <summary>No understory plant nearer the road's edge than this past
        /// the asphalt (m), nor on the graded roadside: it has no trunk in the
        /// table, so it must stand where only a car already off the road and
        /// into the trees could reach it.</summary>
        const float UnderClearM = 3.5f;

        /// <summary>A mesh beech stands this much taller than the card it
        /// replaces. The cards are 10-19 m blobs as wide as they are tall; the
        /// pack's beeches carry a crown about half their height across, and
        /// shrunk to card height (first A/B, 2026-11-07) they read as poles.
        /// Real cove hardwood canopy is 20-30 m.</summary>
        const float MeshTreeHeightScale = 1.6f;

        const string NMGroundDir = "Assets/StoreAssets/NMForest/Ground";
        /// <summary>Repeats per ground UV unit: the stage ground's UVs run one
        /// unit per theme.groundTile (9-13 m); the pack's floors read right at
        /// about 4 m a repeat. Tile-checked 2026-11-07 (3x3, half-offset, 8x8
        /// reduced, both axes): leaves_01, leaves_02, plants_01 pass; moss_02
        /// failed (its bright clumps grid at 3x3), rocks_01 repeats its
        /// diagonal strata conspicuously - neither is used.</summary>
        const float NMGroundScale = 3f;

        /// <summary>The PC stage ground for a dress, or null (web build, no
        /// store files, or snow, which keeps its own turf).</summary>
        static string NMGroundFor(int dress)
        {
            if (!PCContent || !theme.stageForest) return null;
            string n = dress == (int)Season.Summer ? "plants_01"
                     : dress == (int)Season.Spring ? "leaves_02"
                     : dress == Seasons.DressSnow ? null
                     : "leaves_01";
            if (n == null) return null;
            string p = NMGroundDir + "/T_ground_beech_forest_" + n + "_BC_SM.tga";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null ? p : null;
        }

        static bool NMGroundOn => NMGroundFor((int)Season.Fall) != null;

        /// <summary>Puts the fall floor on a ground material MakeMat has just
        /// (re)made - every builder that makes "Ground" or "RoadEdge" resets
        /// the shared asset's texture and tint.</summary>
        static void NMGroundRetexture(Material m)
        {
            if (m == null || !NMGroundOn) return;
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NMGroundFor((int)Season.Fall));
            m.color = Color.white;
            m.mainTextureScale = new Vector2(NMGroundScale, NMGroundScale);
            GroundNormalFor(m);
        }

        /// <summary>The forest floor on a ground material and its seasons
        /// (snow keeps its turf). False = not this build; register as before.</summary>
        static bool ApplyNMGround(Material baseMat, string key, string role)
        {
            if (!NMGroundOn) return false;
            NMGroundRetexture(baseMat);
            RegisterSeasonalTexture(key, baseMat,
                d => d == Seasons.DressSnow ? DressTurfPath(d) : NMGroundFor(d), role);
            foreach (var e in seasonEntries)
            {
                if (e.baseMat != baseMat || e.variants == null) continue;
                foreach (var vm in e.variants)
                    if (vm != null && vm.mainTexture != null && vm.mainTexture.name.StartsWith("T_ground_beech"))
                    {
                        vm.mainTextureScale = new Vector2(NMGroundScale, NMGroundScale);
                        GroundNormalFor(vm);
                    }
            }
            Log("Stage " + role + ", PC: NatureManufacture forest floor (summer plants_01, spring leaves_02, fall/winter leaves_01), x" + NMGroundScale);
            return true;
        }

        class MeshTreeKit
        {
            public MeshForest.Variant[] variants;
            public int treeCount;          // variants[0 .. treeCount) are canopy trees
            public int[] smallBeech, bushes, cover;
            public int[][] debris = new int[5][];   // by kind
            public Material[] leafByDress;
        }

        static MeshTreeKit meshTreeKit;

        /// <summary>The four fall leaf tints, for the impostors to match.</summary>
        static readonly Color[] FallTints =
        {
            Color.white, new Color(1.0f, 0.58f, 0.48f), new Color(1.0f, 1.12f, 0.62f), new Color(1.08f, 0.98f, 0.55f),
        };

        /// <summary>LOD3: the SAME beech as a cross of two cards, from the
        /// pack's own impostor sheet of it (four views in a 4x1 strip; the
        /// tree fills each square cell's height). Spring and summer: the green
        /// sheet; fall: the Autumn Valley sheet in the variant's fall tint;
        /// winter and snow: that sheet darkened to a bare grey-brown (the pack
        /// has no bare impostor).</summary>
        static void AddImpostor(MeshForest.Variant v, string tree, Color fallTint)
        {
            string stem = tree.Replace("beech_tree_", "T_beech_tree_");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(NMTreeDir + "/" + stem + "_BC_A.png") == null) return;
            float h = v.height, w = h;   // square cells
            var mesh = new Mesh { name = "NMImpostor_" + tree };
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nm = new List<Vector3>();
            for (int q = 0; q < 2; q++)
            {
                var rot = Quaternion.Euler(0f, q * 90f, 0f);
                float u0 = q * 0.25f + 0.002f, u1 = (q + 1) * 0.25f - 0.002f;
                int i = vs.Count;
                vs.Add(rot * new Vector3(-w * 0.5f, 0f, 0f)); vs.Add(rot * new Vector3(-w * 0.5f, h, 0f));
                vs.Add(rot * new Vector3(w * 0.5f, h, 0f)); vs.Add(rot * new Vector3(w * 0.5f, 0f, 0f));
                uv.Add(new Vector2(u0, 0f)); uv.Add(new Vector2(u0, 1f)); uv.Add(new Vector2(u1, 1f)); uv.Add(new Vector2(u1, 0f));
                var n = rot * Vector3.back;
                for (int k = 0; k < 4; k++) nm.Add((n + Vector3.up * 0.6f).normalized);
                tr.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetNormals(nm); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            v.lods = new[] { v.lods[0], v.lods[1], v.lods[2], WorldKit.SaveMesh(mesh, mesh.name) };
            v.impostorPre = Matrix4x4.identity;   // built in the tree's own frame: base at 0, metres
            var green = NMLitMat("NM_imp_" + tree, NMDir, "Trees/" + stem + "_BC_A", "Trees/" + stem + "_N", null, true,
                                 new Color(0.80f, 0.86f, 0.78f), maskIsHdrp: false);
            var fallM = NMLitMat("NM_imp_" + tree + "_fall", NMDir, "Autumn/" + stem, "Trees/" + stem + "_N", null, true,
                                 fallTint, maskIsHdrp: false);
            var barePath = BareImpostorPath(stem);
            var bare = barePath != null
                ? NMLitMat("NM_imp_" + tree + "_bare", NMDir, "Gen/" + stem + "_bare2", null, null, true, new Color(0.85f, 0.82f, 0.80f))
                : NMLitMat("NM_imp_" + tree + "_bare", NMDir, "Autumn/" + stem, null, null, true, new Color(0.42f, 0.37f, 0.33f));
            v.impostorDress = new[] { bare, green, green, fallM, bare };
        }

        /// <summary>The variant nearest the wanted height (one of the two
        /// nearest, by hash), scaled toward it by no more than 0.8-1.2 so its
        /// proportions stay its own.</summary>
        static int PickMeshTree(MeshTreeKit kit, float want, int hsh, out float scale)
        {
            int best = 0, second = -1;
            for (int i = 1; i < kit.treeCount; i++)
            {
                float di = Mathf.Abs(kit.variants[i].height - want);
                if (di < Mathf.Abs(kit.variants[best].height - want)) { second = best; best = i; }
                else if (second < 0 || di < Mathf.Abs(kit.variants[second].height - want)) second = i;
            }
            int vi = (second >= 0 && (hsh & 1) == 1) ? second : best;
            scale = Mathf.Clamp(want / kit.variants[vi].height, 0.8f, 1.2f);
            return vi;
        }

        static MeshTreeKit LoadMeshTreeKit()
        {
            if (!PCContent) return null;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(NMTreeDir + "/" + NMTrees[0].tree + ".FBX") == null)
            {
                Log("Mesh trees: no store files in " + NMTreeDir + " (tools/store-assets.ps1) - cards only");
                return null;
            }
            if (meshTreeKit != null) return meshTreeKit;

            // Summer green a shade down: lit by URP beside PSX/Lit's world it
            // read lime against the cards and the turf.
            var green = NMLitMat("NM_beech_leaves", NMTreeDir, "T_beech_atlas_BC", "T_beech_atlas_N", "T_beech_atlas_Maskmap",
                                 true, Color.white);
            var fall = new[]
            {
                NMLitMat("NM_beech_leaves_autumn", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, Color.white),
                NMLitMat("NM_beech_leaves_autumn_red", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.0f, 0.58f, 0.48f)),
                NMLitMat("NM_beech_leaves_autumn_gold", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.0f, 1.12f, 0.62f)),
                NMLitMat("NM_beech_leaves_late", NMTreeDir, "T_beech_atlas_BC", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.08f, 0.98f, 0.55f)),
            };
            var cherry = NMLitMat("NM_black_cherry", NMUnderDir, "Forest_black_cherry_BC", "Forest_black_cherry_N", "Black_cherry_MaskMap",
                                  true, new Color(0.82f, 0.88f, 0.80f), maskIsHdrp: false);
            var cherryFall = NMLitMat("NM_black_cherry_fall", NMUnderDir, "Forest_black_cherry_BC", "Forest_black_cherry_N", "Black_cherry_MaskMap",
                                      true, new Color(1.25f, 0.62f, 0.32f), maskIsHdrp: false);
            var kit = new MeshTreeKit { leafByDress = new[] { null, green, green, fall[0], null } };

            var list = new List<MeshForest.Variant>();
            int n = 0;
            foreach (var (tree, bark) in NMTrees)
            {
                var v = LoadVariant(NMTreeDir, tree, NMLitMat("NM_" + bark, NMTreeDir, bark + "_BC", bark + "_N", bark + "_MaskMap", false, Color.white));
                if (v == null) continue;
                int fi = n++ % fall.Length;
                v.leafDress = new[] { null, green, green, fall[fi], null };
                AddImpostor(v, tree, FallTints[fi]);
                list.Add(v);
            }
            if (list.Count == 0) return null;
            kit.treeCount = list.Count;

            var small = new List<int>();
            var barkSmall = NMLitMat("NM_T_beech_bark_02", NMTreeDir, "T_beech_bark_02_BC", "T_beech_bark_02_N", "T_beech_bark_02_MaskMap", false, Color.white);
            foreach (var name in NMSmallBeech)
            {
                var v = LoadVariant(NMUnderDir, name, barkSmall);
                if (v == null) continue;
                v.leafDress = new[] { null, green, green, fall[n++ % fall.Length], null };
                v.maxDist = UnderMaxDist;
                small.Add(list.Count); list.Add(v);
            }
            var bushes = new List<int>();
            foreach (var name in NMBushes)
            {
                var v = LoadVariant(NMUnderDir, name, cherry);
                if (v == null) continue;
                // One submesh, all of it foliage: it follows the dress.
                for (int s = 0; s < v.leafSub.Length; s++) v.leafSub[s] = true;
                v.leafDress = new[] { null, cherry, cherry, cherryFall, null };
                v.maxDist = UnderMaxDist;
                bushes.Add(list.Count); list.Add(v);
            }
            var cover = new List<int>();
            var coverMats = new Dictionary<string, Material[]>();
            foreach (var (fbx, tex) in NMCover)
            {
                if (!coverMats.TryGetValue(tex, out var dressM))
                {
                    var baseM = NMLitMat("NM_" + tex, NMCoverDir, tex + "_BC_M", tex + "_N", null, true, Color.white, maskIsHdrp: false);
                    var fallM = NMLitMat("NM_" + tex + "_fall", NMCoverDir, tex + "_BC_M", tex + "_N", null, true, new Color(1.10f, 0.86f, 0.55f), maskIsHdrp: false);
                    var winM = NMLitMat("NM_" + tex + "_winter", NMCoverDir, tex + "_BC_M", tex + "_N", null, true, new Color(0.78f, 0.64f, 0.46f), maskIsHdrp: false);
                    dressM = new[] { winM, baseM, baseM, fallM, null };
                    coverMats[tex] = dressM;
                }
                var v = LoadVariant(NMCoverDir, fbx, dressM[2], ".fbx");
                if (v == null) continue;
                for (int s = 0; s < v.leafSub.Length; s++) v.leafSub[s] = true;
                v.leafDress = dressM;
                v.maxDist = CoverMaxDist;
                v.shadows = false;
                cover.Add(list.Count); list.Add(v);
            }
            kit.cover = cover.ToArray();
            var debris = new List<int>[5];
            for (int k = 0; k < 5; k++) debris[k] = new List<int>();
            var debrisMats = new Dictionary<string, Material>();
            foreach (var (fbx, ddir, tex, kind) in NMDebris)
            {
                string bcStem = FindTex(ddir, tex + "_BC_M") != null ? tex + "_BC_M"
                              : FindTex(ddir, tex + "_BC_SM") != null ? tex + "_BC_SM" : tex + "_BC";
                if (!debrisMats.TryGetValue(tex, out var dm))
                {
                    dm = NMLitMat("NM_" + tex, ddir, bcStem, tex + "_N", null, kind == 0, Color.white, maskIsHdrp: false);
                    debrisMats[tex] = dm;
                }
                var v = LoadVariant(NMDebrisDir, fbx, dm, ".fbx") ?? LoadVariant(NMDebrisDir, fbx, dm, ".FBX")
                        ?? LoadVariant("Assets/StoreAssets/NMForest/Debris", fbx, dm);
                if (v == null) continue;
                v.shadows = kind >= 2;
                v.maxDist = kind == 0 ? 55f : kind == 1 ? 70f : 160f;
                if (kind == 0)
                {
                    for (int q = 0; q < v.leafSub.Length; q++) v.leafSub[q] = true;
                    v.leafDress = new[] { dm, dm, dm, dm, null };
                }
                debris[kind].Add(list.Count); list.Add(v);
            }
            for (int k = 0; k < 5; k++) kit.debris[k] = debris[k].ToArray();
            kit.smallBeech = small.ToArray();
            kit.bushes = bushes.ToArray();
            kit.variants = list.ToArray();
            Log($"Mesh forest kit: {kit.treeCount} canopy beeches (+impostors), {small.Count} small beeches, {bushes.Count} cherry bushes, {cover.Count} ground-cover plants, debris " +
                $"{debris[0].Count} litter / {debris[1].Count} branches / {debris[2].Count} stones / {debris[3].Count} logs / {debris[4].Count} stumps.");
            meshTreeKit = kit;
            return kit;
        }

        /// <summary>One FBX's LOD meshes, its first slot's material, the leaf
        /// slot last ([bark, leaves] in every one of the pack's beeches, read in
        /// Blender), and its node transform with the base put at y = 0.</summary>
        static MeshForest.Variant LoadVariant(string dir, string name, Material barkMat, string ext = ".FBX")
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + name + ext);
            if (asset == null) { Log("WARN: mesh forest: missing " + name); return null; }
            var inst = (GameObject)Object.Instantiate(asset);
            try
            {
                var v = new MeshForest.Variant();
                Transform lod0 = null;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    string n = mf.name;
                    int lod = n.EndsWith("_LOD0") ? 0 : n.EndsWith("_LOD1") ? 1 : n.EndsWith("_LOD2") ? 2 : -1;
                    if (lod < 0) continue;
                    v.lods[lod] = mf.sharedMesh;
                    if (lod == 0) lod0 = mf.transform;
                }
                if (lod0 == null || v.lods[0] == null) { Log("WARN: mesh forest: " + name + " has no LOD0"); return null; }
                for (int i = 1; i < 3; i++) if (v.lods[i] == null) v.lods[i] = v.lods[i - 1];
                int subs = v.lods[0].subMeshCount;
                var pre = lod0.localToWorldMatrix;
                // WHICH submesh is the leaves, read off the geometry rather than
                // assumed from Blender's slot order: the trunk reaches the ground,
                // the leaves start higher. (Assuming [bark, leaves] drew the small
                // beeches' leaf cards as opaque grey bark: pale blotches low in
                // the forest, 2026-11-07.)
                int leaf = LeafSubmesh(v.lods[0], pre);
                for (int l = 1; l < 3; l++)
                    if (v.lods[l] != v.lods[0] && v.lods[l].subMeshCount == subs && LeafSubmesh(v.lods[l], pre) != leaf)
                        Log($"WARN: mesh forest: {name} LOD{l} has its leaves in another submesh than LOD0");
                v.mats = new Material[subs];
                v.leafSub = new bool[subs];
                for (int s = 0; s < subs; s++)
                {
                    v.leafSub[s] = subs > 1 && s == leaf;
                    v.mats[s] = barkMat;
                }
                var bb = v.lods[0].bounds;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = bb.center + Vector3.Scale(bb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    float y = pre.MultiplyPoint3x4(corner).y;
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
                var p0 = pre.MultiplyPoint3x4(bb.center);
                v.pre = Matrix4x4.Translate(new Vector3(-p0.x, -minY, -p0.z)) * pre;
                v.height = Mathf.Max(0.3f, maxY - minY);
                Log($"Mesh forest: {name} {v.height:0.0} m, LOD tris {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])}, {subs} submesh(es), leaves = submesh {leaf}");
                return v;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>The leaf submesh of a two-material plant: the one whose
        /// lowest vertex stands higher (the trunk meets the ground). Ties (both
        /// within 3% of the height of the base) fall back to the last slot.</summary>
        static int LeafSubmesh(Mesh m, Matrix4x4 pre)
        {
            int subs = m.subMeshCount;
            if (subs < 2) return 0;
            var low = new float[subs];
            float top = float.MinValue, bottom = float.MaxValue;
            for (int s = 0; s < subs; s++)
            {
                low[s] = float.MaxValue;
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                foreach (int i in tris)
                {
                    float y = pre.MultiplyPoint3x4(verts[i]).y;
                    if (y < low[s]) low[s] = y;
                    if (y > top) top = y;
                    if (y < bottom) bottom = y;
                }
            }
            if (subs != 2 || low[0] == float.MaxValue || low[1] == float.MaxValue) return subs - 1;
            float eps = 0.03f * Mathf.Max(0.01f, top - bottom);
            if (low[0] > low[1] + eps) return 0;
            if (low[1] > low[0] + eps) return 1;
            return 1;
        }

        static long Tris(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        /// <summary>A forest material: PSX/Lit, like the rest of the world.
        /// Owner 2026-11-08, on the first URP Lit pass: "The trees don't seem to
        /// be incorporated in the in-game lighting. They glow at night, but seem
        /// gray and dull during the day." URP Lit lit them by Unity's ambient
        /// and light; the game's day, night, lamps, headlights, fog and grade
        /// are PSX/Lit's own. PSX/Lit has GPU instancing for MeshForest; the
        /// pack's normal and mask maps go unused (PSX/Lit lights per vertex).
        /// Leaves: alpha-tested at 0.5 and two-sided (the lit normal turned to
        /// the camera). The name keeps its NM_ asset (shader switched in place).</summary>
        static Material NMLitMat(string name, string dir, string bc, string n, string mask, bool leaves, Color tint,
                                 bool maskIsHdrp = true)
        {
            string path = null;
            foreach (var ext in new[] { ".tga", ".png" })
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + bc + ext) != null) { path = dir + "/" + bc + ext; break; }
            if (path == null) Log("WARN: mesh forest: texture missing " + dir + "/" + bc);
            // FULLER CROWNS (owner 2026-11-08: "The trees leaves seemed
            // sparse"): the cut a little lower than PSX/Lit's 0.5, and mips
            // that keep the alpha-tested coverage - plain mips average the
            // leaf edges away, so a crown thinned with every halving.
            if (leaves && path != null) PreserveCoverage(path, LeafCutoff);
            var mat = MakeMat(name, path, cutoff: leaves ? LeafCutoff : 0f, tint: tint, affine: 0f, twoSided: leaves);
            mat.enableInstancing = true;
            SetHDNormal(mat, string.IsNullOrEmpty(n) ? null : FindTex(dir, n));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        const float LeafCutoff = 0.38f;

        static string FindTex(string dir, string stem)
        {
            foreach (var ext in new[] { ".tga", ".png" })
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + stem + ext) != null) return dir + "/" + stem + ext;
            return null;
        }

        /// <summary>Mips that keep an alpha-tested texture's coverage at the cut.</summary>
        static void PreserveCoverage(string path, float cutoff)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) return;
            if (imp.mipMapsPreserveCoverage && Mathf.Abs(imp.alphaTestReferenceValue - cutoff) < 0.005f) return;
            imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = cutoff;
            imp.SaveAndReimport();
        }

        /// <summary>The pack's tangent-space normal map on PSX/Lit's HD path
        /// (PSX_HD: the sun per pixel off it, a cotangent frame from screen
        /// derivatives, no mesh tangents needed). TextureHD leaves a PSX_HD it
        /// did not set alone in the PC player; off the PC profile it turns it
        /// off, as it does every HD set.</summary>
        static void SetHDNormal(Material m, string normalPath)
        {
            if (m == null) return;
            var nt = string.IsNullOrEmpty(normalPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (nt == null) { m.DisableKeyword("PSX_HD"); return; }
            m.SetTexture("_HDNormal", nt);
            m.EnableKeyword("PSX_HD");
        }

        /// <summary>The ground floor texture's own normal map (..._BC_SM -> ..._N).</summary>
        static void GroundNormalFor(Material m)
        {
            if (m == null || m.mainTexture == null) return;
            string tp = AssetDatabase.GetAssetPath(m.mainTexture);
            if (!tp.Contains("T_ground_beech_forest_")) { m.DisableKeyword("PSX_HD"); return; }
            SetHDNormal(m, tp.Replace("_BC_SM.tga", "_N.png"));
        }

        /// <summary>A bare beech's impostor for winter, made from the pack's
        /// autumn sheet of the same tree: the leaves (saturated red, orange,
        /// yellow, green) keyed out, the grey-brown trunk and limbs kept. The
        /// pack has no bare impostor, and the darkened leafy one changed shape
        /// as the bare mesh tree handed over to it (owner: "Trees change shape
        /// (especially in winter)"). Written beside the store files (never
        /// committed), once.</summary>
        static string BareImpostorPath(string stem)
        {
            string src = NMDir + "/Autumn/" + stem + ".png";
            string dst = NMDir + "/Gen/" + stem + "_bare2.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(dst) != null) return dst;
            if (!System.IO.File.Exists(src)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(System.IO.File.ReadAllBytes(src));
            var px = t.GetPixels32();
            int kept = 0;
            for (int k = 0; k < px.Length; k++)
            {
                var c = px[k];
                if (c.a < 128) { px[k] = new Color32(0, 0, 0, 0); continue; }
                Color.RGBToHSV(c, out float hh, out float ss, out float vv);
                bool leaf = ss > 0.30f && vv > 0.10f;
                if (!leaf) { kept++; continue; }
                // The crown's twig mass: a third of the leaf pixels stay as a
                // grey-brown speckle (keyed alone, the far winter hills read
                // as a field of bare poles).
                uint hsh = (uint)k * 2654435761u; hsh ^= hsh >> 15;
                if ((hsh & 1023u) < 340u)
                {
                    byte g = (byte)Mathf.Clamp(vv * 150f + 40f, 0f, 255f);
                    px[k] = new Color32(g, (byte)(g * 0.88f), (byte)(g * 0.78f), 255);
                    kept++;
                }
                else px[k] = new Color32(c.r, c.g, c.b, 0);
            }
            t.SetPixels32(px);
            System.IO.Directory.CreateDirectory(NMDir + "/Gen");
            System.IO.File.WriteAllBytes(dst, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(dst);
            Log($"Mesh forest: bare impostor {stem}: kept {kept * 100f / px.Length:0.0}% of the sheet as trunk and limbs");
            return dst;
        }

        /// <summary>Ground cover over one near chunk: grass, ferns, plants on a
        /// jittered CoverPitch grid from the shoulder out to CoverBand, thinning
        /// with distance, never on the tarmac, a bridge deck or a cut face.</summary>
        static void AddGroundCover(MeshTreeKit kit, float ox, float oz, float size,
                                   List<Vector4> inst, List<Vector2> scaleVar, ref int added, ref float ySum)
        {
            if (kit.cover == null || kit.cover.Length == 0) return;
            float roadHalf = RoadWidth * 0.5f;
            int cells = Mathf.CeilToInt(size / CoverPitch);
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    uint h = (uint)(Mathf.FloorToInt(ox) * 92837111) ^ (uint)(Mathf.FloorToInt(oz) * 689287499)
                           ^ (uint)(gx * 83492791) ^ (uint)gz * 2971215073u;
                    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                    float x = ox + (gx + ((h & 1023u) / 1023f)) * CoverPitch;
                    float z = oz + (gz + (((h >> 10) & 1023u) / 1023f)) * CoverPitch;
                    if (!StageCorridor(x, z, CoverBand + 4f, out RoadFoot f)) continue;
                    if (f.d < roadHalf + 0.9f || f.d > CoverBand) continue;
                    if (BridgeAt(f.station) > 0.35f) continue;
                    float keep = Mathf.Lerp(0.95f, 0.55f, Mathf.InverseLerp(roadHalf + 3f, CoverBand, f.d));
                    if (((h >> 20) & 1023u) / 1023f > keep) continue;
                    float lat = StageLatticeY(x, z), top = StageBankTopY(f);
                    if (OnGradedRoadside(f) && top > lat + 0.3f) continue;   // a cut face
                    float gy = Mathf.Max(Mathf.Max(lat, top), StageFillY(x, z));
                    int vi = kit.cover[(int)((h >> 5) % (uint)kit.cover.Length)];
                    float sc = Mathf.Lerp(1.5f, 2.6f, ((h >> 3) & 255u) / 255f);
                    inst.Add(new Vector4(x, gy - 0.03f, z, (h % 360u)));
                    scaleVar.Add(new Vector2(sc, vi));
                    ySum += gy;
                    added++;
                }
        }

        /// <summary>The forest floor's litter and wood over one near chunk, each
        /// piece laid on the lattice's slope. Stones, logs and stumps stand
        /// clear of the graded roadside and well off the asphalt (no
        /// colliders: they must sit where only a car already in the trees
        /// could reach them).</summary>
        static void AddDebris(MeshTreeKit kit, float ox, float oz, float size, List<Vector4> inst, List<Vector2> scaleVar,
                              Dictionary<int, Vector2> tilts, ref int added, ref float ySum)
        {
            float roadHalf = RoadWidth * 0.5f;
            // kind: pitch, nearest past the asphalt, farthest from the centreline, keep, scale, sink
            var layers = new (int kind, float pitch, float near, float far, float keep, float s0, float s1, float sink)[]
            {
                (0, 2.6f, 1.2f, 55f, 0.55f, 0.9f, 1.6f, 0.02f),
                (1, 6.0f, 2.5f, 60f, 0.35f, 0.9f, 1.3f, 0.03f),
                (2, 9.0f, 4.0f, 120f, 0.25f, 0.6f, 1.8f, 0.12f),
                (3, 16f, 6.0f, 120f, 0.25f, 0.9f, 1.3f, 0.08f),
                (4, 22f, 6.0f, 120f, 0.20f, 0.9f, 1.2f, 0.05f),
            };
            foreach (var L in layers)
            {
                var pool = kit.debris[L.kind];
                if (pool == null || pool.Length == 0) continue;
                int cells = Mathf.CeilToInt(size / L.pitch);
                for (int gz = 0; gz < cells; gz++)
                    for (int gx = 0; gx < cells; gx++)
                    {
                        uint h = (uint)(Mathf.FloorToInt(ox) * 73856093) ^ (uint)(Mathf.FloorToInt(oz) * 19349663)
                               ^ (uint)(gx * 83492791) ^ (uint)gz * 2971215073u ^ (uint)(L.kind * 1640531527);
                        h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                        if (((h >> 20) & 1023u) / 1023f > L.keep) continue;
                        float x = ox + (gx + ((h & 1023u) / 1023f)) * L.pitch;
                        float z = oz + (gz + (((h >> 10) & 1023u) / 1023f)) * L.pitch;
                        if (!StageCorridor(x, z, L.far + 4f, out RoadFoot f)) continue;
                        if (f.d < roadHalf + L.near || f.d > L.far) continue;
                        if (BridgeAt(f.station) > 0.35f) continue;
                        float lat = StageLatticeY(x, z), top = StageBankTopY(f);
                        bool graded = OnGradedRoadside(f);
                        if (graded && (L.kind >= 2 || top > lat + 0.3f)) continue;
                        float gy = Mathf.Max(Mathf.Max(lat, top), StageFillY(x, z));
                        float hx = (StageLatticeY(x + 1f, z) - StageLatticeY(x - 1f, z)) * 0.5f;
                        float hz = (StageLatticeY(x, z + 1f) - StageLatticeY(x, z - 1f)) * 0.5f;
                        var n = new Vector3(-hx, 1f, -hz).normalized;
                        if (n.y < 0.6f && L.kind >= 3) continue;   // no log or stump on a 53-degree face
                        int vi = pool[(int)((h >> 5) % (uint)pool.Length)];
                        float sc = Mathf.Lerp(L.s0, L.s1, ((h >> 3) & 255u) / 255f);
                        tilts[inst.Count] = new Vector2(Mathf.Atan2(n.z, n.y) * Mathf.Rad2Deg, -Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg);
                        inst.Add(new Vector4(x, gy - L.sink, z, (h % 360u)));
                        scaleVar.Add(new Vector2(sc, vi));
                        ySum += gy;
                        added++;
                    }
            }
        }

        /// <summary>One more canopy beech beside a planted one, in the dense
        /// part of the band: true, and where, when it fits.</summary>
        static bool ExtraCanopySite(float wx, float wz, int hsh, out float x, out float z, out float gy)
        {
            uint h = (uint)hsh * 2246822519u + 3266489917u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
            float ang = (h % 3600u) * 0.1f * Mathf.Deg2Rad, r = 3.4f + ((h >> 12) % 100u) * 0.02f;
            x = wx + Mathf.Cos(ang) * r; z = wz + Mathf.Sin(ang) * r; gy = 0f;
            if ((h >> 24) % 10u >= 7u) return false;            // 70% of trees get one
            if (!StageCorridor(x, z, ForestBand + 10f, out RoadFoot f)) return false;
            if (f.d < RoadWidth * 0.5f + 4.5f || f.d > 90f) return false;
            if (BridgeAt(f.station) > 0.35f || OnGradedRoadside(f)) return false;
            gy = Mathf.Max(Mathf.Max(StageLatticeY(x, z), StageBankTopY(f)), StageFillY(x, z));
            return true;
        }

        /// <summary>Understory round one canopy tree at (wx, wz): 1-3 small
        /// beeches and cherry bushes, by the same hash every build.</summary>
        static void AddUnderstory(MeshTreeKit kit, float wx, float wz, float d, int hsh,
                                  List<Vector4> inst, List<Vector2> scaleVar, ref int added)
        {
            if (kit.smallBeech.Length == 0 && kit.bushes.Length == 0) return;
            float roadHalf = RoadWidth * 0.5f;
            int count = d <= ForestDenseTo ? 4 : d <= 90f ? 3 : 2;
            for (int k = 0; k < count; k++)
            {
                uint h = (uint)hsh * 2654435761u + (uint)k * 40503u + 12345u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                float ang = (h % 3600u) * 0.1f * Mathf.Deg2Rad;
                float r = 1.8f + ((h >> 12) % 100u) * 0.032f;
                float ux = wx + Mathf.Cos(ang) * r, uz = wz + Mathf.Sin(ang) * r;
                if (!StageCorridor(ux, uz, ForestBand + 10f, out RoadFoot f2)) continue;
                if (f2.d < roadHalf + UnderClearM || f2.d > ForestBand) continue;
                if (BridgeAt(f2.station) > 0.35f || OnGradedRoadside(f2)) continue;
                float gy = Mathf.Max(Mathf.Max(StageLatticeY(ux, uz), StageBankTopY(f2)), StageFillY(ux, uz));
                bool bush = kit.bushes.Length > 0 && (kit.smallBeech.Length == 0 || (h & 0x10000u) != 0);
                int[] pool = bush ? kit.bushes : kit.smallBeech;
                int vi = pool[(int)((h >> 20) % (uint)pool.Length)];
                float t = ((h >> 4) % 100u) * 0.01f;
                float sc = bush ? Mathf.Lerp(1.4f, 2.3f, t) : Mathf.Lerp(0.8f, 1.4f, t);
                inst.Add(new Vector4(ux, gy - 0.1f, uz, (h % 360u)));
                scaleVar.Add(new Vector2(sc, vi));
                added++;
            }
        }
    }
}
