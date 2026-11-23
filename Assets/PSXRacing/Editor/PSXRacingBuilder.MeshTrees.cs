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
        /// <summary>The owner's free Objective Environment tulip poplar
        /// (2026-11-08): a cove's tall, straight, narrow tree that turns clear
        /// yellow. Its own LOD1-3 serve as HIGH / MID / LOW (LOD0 is 20k
        /// tris), its billboard as the impostor.</summary>
        const string OEPoplarDir = "Assets/StoreAssets/OE/Poplar";
        /// <summary>THE POPLAR'S SHARE (owner 2026-11-11, the title's drone over
        /// Linn Cove: "poplars seem very repetitive, clumped"). It was one canopy
        /// tree in four by a hash, the one model of it a narrow 23 m column - and
        /// a hillside of columns is not the Blue Ridge, whose "yellow poplar" is
        /// the broad-crowned tulip tree. Now at most ONE site in each
        /// PoplarBlock x PoplarBlock block of the forest grid (30 m square) is a
        /// poplar, by a chance that leans toward the coves (the cluster noise,
        /// softly): never two side by side, about one grid site in 23 (4.4%,
        /// from 25%). The tulip yellow lives on in the broad crowns (FallTulip).</summary>
        const int PoplarBlock = 4;
        const float PoplarChanceLow = 0.4f, PoplarChanceHigh = 1.0f;
        /// <summary>Tulip-poplar yellow on this share of the fall canopy beeches,
        /// tree by tree (PSX/Lit _LeafTulip; the share is the alpha). A share
        /// this small on the green dresses carries only the per-tree hue spread.</summary>
        static readonly Color FallTulip = new Color(1.0f, 0.80f, 0.16f, 0.22f);
        static readonly Color GreenSpread = new Color(1f, 1f, 1f, 0.0001f);

        // ---- FALL 2 (the fallcolour round, 2026-11-23). The owner, after PC59: "Its very dull, the
        // yellows are wrong, the oranges are wrong, the reds are wrong, the browns are wrong." Measured
        // (OKLCH, scratchpad fallcolour measure0): his Parkway pictures' canopy is golden yellow h 81,
        // flame orange h 57, scarlet h 36 and copper-brown h 48-55; ours was olive (the autumn sheet
        // itself sits at h 89-90) under multiplied tints that made brick and mustard, with no orange.
        // Now each canopy beech recolours the autumn sheet AT ITS OWN LUMINANCE (PSX/Lit LeafRecolour,
        // so the leaf's shading stays) to one of his colours; the far cards, impostors and tree-match
        // sheets are made from these very materials, so a tree is the same colour at every distance.
        // PSX_FALL2=0 at the build: the materials as they were (their own assets, untouched).
        internal static bool Fall2 => System.Environment.GetEnvironmentVariable("PSX_FALL2") != "0";
        /// <summary>The fall colours, sRGB with the largest channel 1 (only the hue and chroma count:
        /// the recolour keeps each texel's luminance), from the owner's pictures' class medians.</summary>
        internal static readonly Color Fall2Gold = new Color(1.00f, 0.74f, 0.10f), Fall2Amber = new Color(1.00f, 0.62f, 0.07f),
            Fall2Orange = new Color(1.00f, 0.52f, 0.045f), Fall2Scarlet = new Color(1.00f, 0.30f, 0.19f),
            Fall2Crimson = new Color(1.00f, 0.22f, 0.30f), Fall2Copper = new Color(1.00f, 0.56f, 0.27f);
        /// <summary>The canopy beeches in NMTrees order: 01 orange, 02 scarlet, 03 gold, 06 copper (the oaks'
        /// russet), 07 amber, 08 crimson (sourwood, black gum). The tulip share adds gold tree by tree.</summary>
        static readonly (string name, Color target)[] Fall2Beech =
        {
            ("orange", Fall2Orange), ("scarlet", Fall2Scarlet), ("gold", Fall2Gold),
            ("copper", Fall2Copper), ("amber", Fall2Amber), ("crimson", Fall2Crimson),
        };
        static readonly Color Fall2Tulip = new Color(1.0f, 0.74f, 0.10f, 0.16f);
        const float Fall2Strength = 0.92f;

        /// <summary>A FALL 2 leaf material: the sheet at its own tint (white), recoloured to
        /// <paramref name="target"/>, the tree-by-tree tulip share, the phase flag.</summary>
        static void Fall2Dress(Material m, Color target, bool tulip, float strength = Fall2Strength)
        {
            if (m == null) return;
            m.SetColor("_LeafTarget", target);
            var lv = m.GetVector("_LeafVivid"); lv.w = strength; m.SetVector("_LeafVivid", lv);
            m.SetColor("_LeafTulip", tulip ? Fall2Tulip : GreenSpread);
            m.SetVector("_LeafFall", new Vector4(1f, 0f, 0f, 0f));
            EditorUtility.SetDirty(m);
        }
        /// <summary>The poplar's sheets against the beeches' (texture means of the
        /// alpha-tested texels, scratchpad texstats.py): leaves 0.405 v the beech
        /// atlas's 0.323, the billboard 0.564 v the beech impostor sheets' 0.32 -
        /// the poplar's albedo scaled to the beeches', so one light reads alike.</summary>
        const float PoplarLeafGain = 0.80f, PoplarSheetGain = 0.57f;

        /// <summary>Whether the canopy site at grid cell (gx, gz) of the chunk at
        /// (ox, oz) is a poplar: the one chosen cell of its block, at a chance by
        /// the cove noise.</summary>
        static bool PoplarSite(float ox, float oz, int gx, int gz, float wx, float wz)
        {
            int GX = Mathf.RoundToInt(ox / ForestPitch) + gx, GZ = Mathf.RoundToInt(oz / ForestPitch) + gz;
            int sx = Mathf.FloorToInt(GX / (float)PoplarBlock), sz = Mathf.FloorToInt(GZ / (float)PoplarBlock);
            uint h = (uint)sx * 0x8da6b343u ^ (uint)sz * 0xd8163841u ^ 0x5bd1e995u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            int pick = (int)(h % (uint)(PoplarBlock * PoplarBlock));
            if ((GX - sx * PoplarBlock) + (GZ - sz * PoplarBlock) * PoplarBlock != pick) return false;
            float cove = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, -0.6f, ForestClusterNoise(wx, wz)));
            return ((h >> 8) & 1023u) / 1023f < Mathf.Lerp(PoplarChanceLow, PoplarChanceHigh, cove);
        }
        const string OEGrassVerge = "Assets/StoreAssets/OE/Grass/Grass01_BigUV.png";
        /// <summary>The mowed verge's repeats per ground UV unit (one unit per
        /// theme.groundTile, 9-13 m): the 4096 "BigUV" sheet reads right at about
        /// 8 m a repeat. Tile-checked 2026-11-08: seamless both ways, a faint
        /// grid only at 8x8 reduced.</summary>
        const float VergeGrassScale = 1.5f;
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
        const float CoverMaxDist = 120f;
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
        const float UnderMaxDist = 220f;
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
            if (dress == (int)Season.Fall && Fall2Ground != null) return Fall2Ground;
            string n = dress == (int)Season.Summer ? "plants_01"
                     : dress == (int)Season.Spring ? "leaves_02"
                     : dress == Seasons.DressSnow ? null
                     : "leaves_01";
            if (n == null) return null;
            string p = NMGroundDir + "/T_ground_beech_forest_" + n + "_BC_SM.tga";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null ? p : null;
        }

        static bool NMGroundOn => NMGroundFor((int)Season.Fall) != null;

        // ---- FALL 2: THE GROUND. His pictures' forest floor and verges are an orange-brown leaf carpet
        // (OKLCH L .39-.55, C .074-.12, h 41-57) over tan grass; ours was the beech pack's grey-brown
        // litter (C .03) and, on the verge, the summer-mowed green grass (48 % green in his PC59 frame).
        // Now, in fall: the floor is the Meadow pack's leaf litter warmed to copper, patched with the beech
        // litter; the verge is the Meadow's straw-and-grass meadow under copper leaf patches that reach
        // the asphalt. All tile-checked pack textures (2026-11-08: the Meadow ground five pass both axes);
        // the tints are material colours (the store files untouched).
        static string Fall2Ground => Fall2 && PCContent && theme.stageForest
            && AssetDatabase.LoadAssetAtPath<Texture2D>(MeadowGroundDir + "/T_forest_ground_leaves_01_A_SM.tga") != null
            ? MeadowGroundDir + "/T_forest_ground_leaves_01_A_SM.tga" : null;
        static string Fall2Verge => Fall2Ground != null && AssetDatabase.LoadAssetAtPath<Texture2D>(MeadowGroundDir + "/T_ground_meadow_grass_02_A_SM.tga") != null
            ? MeadowGroundDir + "/T_ground_meadow_grass_02_A_SM.tga" : null;
        /// <summary>The floor's leaf carpet (main and verge patches) and the verge's straw, as material tints.</summary>
        internal static Color Fall2FloorTint = new Color(1.16f, 0.86f, 0.62f), Fall2VergeTint = new Color(1.10f, 0.98f, 0.80f),
            Fall2VergeLeafTint = new Color(1.10f, 0.78f, 0.56f);

        const string MeadowGroundDir = "Assets/StoreAssets/Meadow/Ground";

        /// <summary>The second ground layer for a dress, from the Meadow pack
        /// (tile-checked 2026-11-08, all five pass): grassy patches in spring and
        /// summer, the darker leaf litter in fall and winter; none under snow.</summary>
        static string MeadowBlendFor(int dress)
        {
            if (dress == Seasons.DressSnow) return null;
            if (dress == (int)Season.Fall && Fall2Ground != null)
            {
                string bl = NMGroundDir + "/T_ground_beech_forest_leaves_01_BC_SM.tga";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(bl) != null) return bl;
            }
            string n = dress == (int)Season.Summer || dress == (int)Season.Spring
                ? "T_forest_ground_grass_01_A_SM" : "T_forest_ground_leaves_01_A_SM";
            string p = MeadowGroundDir + "/" + n + ".tga";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null ? p : null;
        }

        static void SetGroundBlend(Material m, int dress)
        {
            if (m == null) return;
            string bp = PCContent ? MeadowBlendFor(dress) : null;
            var bt = bp != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(bp) : null;
            if (m.HasProperty("_BlendTint")) m.SetColor("_BlendTint", Color.white);
            if (bt == null) { m.DisableKeyword("PSX_GROUNDBLEND"); return; }
            m.SetTexture("_BlendTex", bt);
            m.SetVector("_BlendParams", new Vector4(0.24f, 13f, 0.42f, 0f));
            // FALL 2: the beech litter patches over the copper carpet, the copper undone on them
            if (dress == (int)Season.Fall && Fall2Ground != null)
                m.SetColor("_BlendTint", new Color(1f / Fall2FloorTint.r, 1f / Fall2FloorTint.g, 1f / Fall2FloorTint.b, 1f));
            m.EnableKeyword("PSX_GROUNDBLEND");
        }

        /// <summary>The ROAD-EDGE verge: mowed green in every dress but winter
        /// (the floor's green-and-brown) and snow (its turf) - the Parkway's
        /// shoulders stay mown green through the fall (owner's frames,
        /// 2026-11-08), where the forest floor beyond is leaf litter.</summary>
        static string NMVergeFor(int dress)
        {
            if (NMGroundFor((int)Season.Fall) == null || dress == Seasons.DressSnow) return null;
            if (dress == (int)Season.Fall && Fall2Verge != null) return Fall2Verge;
            if (dress != (int)Season.Winter && AssetDatabase.LoadAssetAtPath<Texture2D>(OEGrassVerge) != null)
                return OEGrassVerge;   // the owner's mowed-grass sheet (Grass Flowers Pack)
            string n = dress == (int)Season.Winter ? "leaves_02" : "plants_01";
            return NMGroundDir + "/T_ground_beech_forest_" + n + "_BC_SM.tga";
        }

        /// <summary>Puts the fall floor on a ground material MakeMat has just
        /// (re)made - every builder that makes "Ground" or "RoadEdge" resets
        /// the shared asset's texture and tint.</summary>
        static void NMGroundRetexture(Material m, bool verge = false)
        {
            if (m == null || !NMGroundOn) return;
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(verge ? NMVergeFor((int)Season.Fall) : NMGroundFor((int)Season.Fall));
            m.color = Fall2Ground == null ? Color.white : verge ? Fall2VergeTint : Fall2FloorTint;
            float gs = m.mainTexture != null && m.mainTexture.name == "Grass01_BigUV" ? VergeGrassScale : NMGroundScale;
            m.mainTextureScale = new Vector2(gs, gs);
            GroundNormalFor(m);
            if (!verge) SetGroundBlend(m, (int)Season.Fall);
            else if (Fall2Ground != null) SetVergeLeaves(m);
            else { m.DisableKeyword("PSX_GROUNDBLEND"); if (m.HasProperty("_BlendTint")) m.SetColor("_BlendTint", Color.white); }
        }

        /// <summary>FALL 2: the verge's leaf carpet - the Meadow leaf litter in wide patches over the straw,
        /// a little over half of it, the straw's tan undone on the leaves and the copper put on.</summary>
        static void SetVergeLeaves(Material m)
        {
            var bt = AssetDatabase.LoadAssetAtPath<Texture2D>(Fall2Ground);
            if (bt == null) { m.DisableKeyword("PSX_GROUNDBLEND"); return; }
            m.SetTexture("_BlendTex", bt);
            m.SetVector("_BlendParams", new Vector4(0.30f, 5f, 0.58f, 0f));
            m.SetColor("_BlendTint", new Color(Fall2VergeLeafTint.r / Fall2VergeTint.r, Fall2VergeLeafTint.g / Fall2VergeTint.g, Fall2VergeLeafTint.b / Fall2VergeTint.b, 1f));
            m.EnableKeyword("PSX_GROUNDBLEND");
        }

        /// <summary>The forest floor on a ground material and its seasons
        /// (snow keeps its turf). False = not this build; register as before.</summary>
        static bool ApplyNMGround(Material baseMat, string key, string role, bool verge = false)
        {
            if (!NMGroundOn) return false;
            NMGroundRetexture(baseMat, verge);
            RegisterSeasonalTexture(key, baseMat,
                d => d == Seasons.DressSnow ? DressTurfPath(d) : verge ? NMVergeFor(d) : NMGroundFor(d), role);
            if (!verge)
            {
                SetGroundBlend(baseMat, (int)Season.Fall);
                foreach (var e in seasonEntries)
                    if (e.baseMat == baseMat && e.variants != null)
                        for (int d = 0; d < e.variants.Length; d++)
                            if (e.variants[d] != baseMat) SetGroundBlend(e.variants[d], d);
            }
            foreach (var e in seasonEntries)
            {
                if (e.baseMat != baseMat || e.variants == null) continue;
                foreach (var vm in e.variants)
                    if (vm != null && vm.mainTexture != null && vm.mainTexture.name.StartsWith("T_ground_beech"))
                    {
                        vm.mainTextureScale = new Vector2(NMGroundScale, NMGroundScale);
                        GroundNormalFor(vm);
                    }
                    else if (vm != null && vm.mainTexture != null && vm.mainTexture.name == "Grass01_BigUV")
                    {
                        vm.mainTextureScale = new Vector2(VergeGrassScale, VergeGrassScale);
                        vm.color = Color.white;
                        vm.DisableKeyword("PSX_HD");
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
            public int poplar = -1;
            public List<CliffInfo> cliffs;
            public List<FenceInfo> fences;
            public int[][] debris = new int[5][];   // by kind
            public Material[] leafByDress;
            /// <summary>TREE MATCH: the B cards' atlas per dress (TMStageCardAtlas); null entries = the dress's own.</summary>
            public Texture2D[] matchCardAtlas;
        }

        static MeshTreeKit meshTreeKit;

        /// <summary>The four fall leaf tints, for the impostors to match.</summary>
        static readonly Color[] FallTints =
        {
            Color.white, new Color(1.0f, 0.58f, 0.48f), new Color(1.0f, 1.12f, 0.62f), new Color(1.08f, 0.98f, 0.55f),
        };

        /// <summary>LOD3 from a single-view billboard (the poplar's): a cross of
        /// two cards, each the whole sheet, <paramref name="aspect"/> wide for
        /// its height; fall recoloured, winter the sheet darkened (the pack has
        /// no bare one).</summary>
        static void AddBillboardImpostor(MeshForest.Variant v, string dir, string stem, float aspect)
        {
            if (FindTex(dir, stem) == null) return;
            float h = v.height, w = h * aspect;
            var mesh = new Mesh { name = "OEImpostor_" + stem };
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nm = new List<Vector3>();
            for (int q = 0; q < 2; q++)
            {
                var rot = Quaternion.Euler(0f, q * 90f, 0f);
                int i = vs.Count;
                vs.Add(rot * new Vector3(-w * 0.5f, 0f, 0f)); vs.Add(rot * new Vector3(-w * 0.5f, h, 0f));
                vs.Add(rot * new Vector3(w * 0.5f, h, 0f)); vs.Add(rot * new Vector3(w * 0.5f, 0f, 0f));
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
                var n = rot * Vector3.back;
                for (int k = 0; k < 4; k++) nm.Add((n + Vector3.up * 0.6f).normalized);
                tr.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            ShareMeshOrigin(v, vs);
            mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetNormals(nm); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            v.lods = new[] { v.lods[0], v.lods[1], v.lods[2], WorldKit.SaveMesh(mesh, mesh.name) };
            // the sheet is a bright pre-lit billboard (texel mean 0.56): scaled to
            // the beech impostors' albedo, with their green impostor's cast
            float g = PoplarSheetGain;
            var green = NMLitMat("OE_imp_" + stem, dir, stem, null, null, true, new Color(0.80f * g / 0.84f, 0.86f * g / 0.84f, 0.78f * g / 0.84f));
            var fallM = NMLitMat("OE_imp_" + stem + (Fall2 ? "_fall2" : "_fall"), dir, stem, null, null, true, new Color(g, g, g));
            fallM.SetColor("_LeafTarget", new Color(1.0f, 0.78f, 0.12f));
            var lv = fallM.GetVector("_LeafVivid"); lv.w = 0.85f; fallM.SetVector("_LeafVivid", lv);
            green.SetColor("_LeafTulip", GreenSpread);
            fallM.SetColor("_LeafTulip", GreenSpread);
            if (Fall2) Fall2Dress(fallM, Fall2Gold, false);
            var bare = NMLitMat("OE_imp_" + stem + "_bare", dir, stem, null, null, true, new Color(0.40f, 0.36f, 0.32f));
            var bv = bare.GetVector("_LeafVivid"); bv.x = 0.15f; bare.SetVector("_LeafVivid", bv);
            v.impostorDress = new[] { bare, green, green, fallM, bare };
            foreach (var im in v.impostorDress) FarGrow(im);
        }

        /// <summary>LOD3: the SAME beech as a cross of two cards, from the
        /// pack's own impostor sheet of it (four views in a 4x1 strip; the
        /// tree fills each square cell's height). Spring and summer: the green
        /// sheet; fall: the Autumn Valley sheet in the variant's fall tint;
        /// winter and snow: that sheet darkened to a bare grey-brown (the pack
        /// has no bare impostor).</summary>
        static void AddImpostor(MeshForest.Variant v, string tree, Color fallTint, bool greenFall = false, Color? fall2 = null)
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
            ShareMeshOrigin(v, vs);
            mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetNormals(nm); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            v.lods = new[] { v.lods[0], v.lods[1], v.lods[2], WorldKit.SaveMesh(mesh, mesh.name) };
            var green = NMLitMat("NM_imp_" + tree, NMDir, "Trees/" + stem + "_BC_A", "Trees/" + stem + "_N", null, true,
                                 new Color(0.80f, 0.86f, 0.78f), maskIsHdrp: false);
            // the LATE (still green) fall tint's impostor from the green sheet,
            // or a green tree "pops in" where an orange one stood (owner 2026-11-09)
            var fallM = NMLitMat("NM_imp_" + tree + (fall2.HasValue ? "_fall2" : "_fall"), NMDir, greenFall ? "Trees/" + stem + "_BC_A" : "Autumn/" + stem,
                                 "Trees/" + stem + "_N", null, true, fallTint, maskIsHdrp: false);
            var barePath = BareImpostorPath(stem);
            var bare = barePath != null
                ? NMLitMat("NM_imp_" + tree + "_bare", NMDir, "Gen/" + stem + "_bare2", null, null, true, new Color(0.85f, 0.82f, 0.80f))
                : NMLitMat("NM_imp_" + tree + "_bare", NMDir, "Autumn/" + stem, null, null, true, new Color(0.42f, 0.37f, 0.33f));
            green.SetColor("_LeafTulip", GreenSpread);
            fallM.SetColor("_LeafTulip", greenFall ? GreenSpread : FallTulip);
            if (fall2.HasValue) Fall2Dress(fallM, fall2.Value, true);
            v.impostorDress = new[] { bare, green, green, fallM, bare };
            foreach (var im in v.impostorDress) FarGrow(im);
        }

        /// <summary>THE IMPOSTOR ON THE MESH'S ORIGIN. PSX/Lit gives each tree
        /// its own shade, hue and (fall) its tulip yellow from the instance
        /// matrix's origin, and lights the crown's AO from it; the mesh levels'
        /// origin is the FBX's own (the plant's matrix x pre), the impostor's was
        /// the trunk's foot - so a tree changed shade, and could change colour,
        /// as it handed over to its impostor. The cards are built in the tree's
        /// frame (foot at 0); moved by minus pre's translation and drawn with
        /// impostorPre = that translation, they land where they did, and the
        /// matrix's origin is the mesh's.</summary>
        static void ShareMeshOrigin(MeshForest.Variant v, List<Vector3> verts)
        {
            Vector3 t = v.pre.GetColumn(3);
            v.impostorPre = Matrix4x4.identity;
            // (a far impostor GROWS about its origin, _PSXFarGrow: an origin far
            // off the foot would walk the grown card off its tree - keep the foot)
            if (t.magnitude > 1.5f) { Log($"Mesh forest: impostor keeps its own origin, pre offset {t.magnitude:0.00} m"); return; }
            for (int i = 0; i < verts.Count; i++) verts[i] -= t;
            v.impostorPre = Matrix4x4.Translate(t);
            Log($"Mesh forest: impostor on the mesh's origin ({t.x:0.00}, {t.y:0.00}, {t.z:0.00})");
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
            green.SetColor("_LeafTulip", GreenSpread);
            for (int f = 0; f < fall.Length; f++) fall[f].SetColor("_LeafTulip", f == 3 ? GreenSpread : FallTulip);
            // FALL 2: one material per canopy beech, each its own colour (their own assets: the old ones untouched)
            Material[] fall2 = null;
            if (Fall2)
            {
                fall2 = new Material[Fall2Beech.Length];
                for (int f = 0; f < fall2.Length; f++)
                {
                    fall2[f] = NMLitMat("NM_beech_leaves_fall2_" + Fall2Beech[f].name, NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, Color.white);
                    Fall2Dress(fall2[f], Fall2Beech[f].target, true);
                }
                Log("FALL2: canopy beeches recoloured to the owner's fall (" + string.Join(", ", System.Array.ConvertAll(Fall2Beech, b => b.name)) + "), tulip gold share " + Fall2Tulip.a);
            }
            var cherry = NMLitMat("NM_black_cherry", NMUnderDir, "Forest_black_cherry_BC", "Forest_black_cherry_N", "Black_cherry_MaskMap",
                                  true, new Color(0.82f, 0.88f, 0.80f), maskIsHdrp: false);
            var cherryFall = Fall2
                ? NMLitMat("NM_black_cherry_fall2", NMUnderDir, "Forest_black_cherry_BC", "Forest_black_cherry_N", "Black_cherry_MaskMap", true, Color.white, maskIsHdrp: false)
                : NMLitMat("NM_black_cherry_fall", NMUnderDir, "Forest_black_cherry_BC", "Forest_black_cherry_N", "Black_cherry_MaskMap",
                                      true, new Color(1.35f, 0.36f, 0.26f), maskIsHdrp: false);
            if (Fall2) Fall2Dress(cherryFall, Fall2Scarlet, false);
            // WINTER TWIGS (owner 2026-11-08, a winter frame: the bare beeches
            // read as a burned forest - a beech's fine twigs live in its leaf
            // texture). The autumn atlas with the leaves keyed to a grey-brown
            // twig speckle (Gen/, made beside the store files); no translucency.
            Material bare = null;
            if (FindTex(NMDir, "Gen/T_beech_atlas_bare") != null)
            {
                bare = NMLitMat("NM_beech_twigs", NMDir, "Gen/T_beech_atlas_bare", null, null, true, new Color(0.86f, 0.82f, 0.78f));
                bare.SetVector("_LeafVivid", new Vector4(1f, 1f, 0f, 0f));
            }
            var kit = new MeshTreeKit { leafByDress = new[] { bare, green, green, fall2 != null ? fall2[0] : fall[0], bare } };

            var list = new List<MeshForest.Variant>();
            int n = 0;
            foreach (var (tree, bark) in NMTrees)
            {
                var v = LoadVariant(NMTreeDir, tree, NMLitMat("NM_" + bark, NMTreeDir, bark + "_BC", bark + "_N", bark + "_MaskMap", false, Color.white));
                if (v == null) continue;
                int f2 = n % Fall2Beech.Length;
                int fi = n++ % fall.Length;
                if (fall2 != null)
                {
                    v.leafDress = new[] { bare, green, green, fall2[f2], bare };
                    AddImpostor(v, tree, Color.white, false, Fall2Beech[f2].target);
                }
                else
                {
                    v.leafDress = new[] { bare, green, green, fall[fi], bare };
                    AddImpostor(v, tree, FallTints[fi], fi == 3);
                }
                list.Add(v);
            }
            if (list.Count == 0) return null;
            kit.treeCount = list.Count;
            // THE TULIP POPLAR (after the beeches, so PickMeshTree never picks it)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OEPoplarDir + "/PoplarTree001.fbx") != null)
            {
                var pBark = NMLitMat("OE_poplar_bark", OEPoplarDir, "Poplar_Bark001", "Poplar_Bark001_N", null, false, Color.white);
                var pGain = new Color(PoplarLeafGain, PoplarLeafGain, PoplarLeafGain);
                var pLeaf = NMLitMat("OE_poplar_leaves", OEPoplarDir, "BranchesPoplar001", "BranchesPoplar001_N", null, true, pGain);
                var pFall = NMLitMat(Fall2 ? "OE_poplar_leaves_fall2" : "OE_poplar_leaves_fall", OEPoplarDir, "BranchesPoplar001", "BranchesPoplar001_N", null, true, pGain);
                pLeaf.SetColor("_LeafTulip", GreenSpread);
                pFall.SetColor("_LeafTulip", GreenSpread);
                pFall.SetColor("_LeafTarget", new Color(1.0f, 0.78f, 0.12f));
                var lv = pFall.GetVector("_LeafVivid"); lv.w = 0.85f; pFall.SetVector("_LeafVivid", lv);
                if (Fall2) Fall2Dress(pFall, Fall2Gold, false);
                var pv = LoadVariant(OEPoplarDir, "PoplarTree001", pBark, ".fbx", skip: 1);
                if (pv != null)
                {
                    pv.leafDress = new[] { null, pLeaf, pLeaf, pFall, null };
                    AddBillboardImpostor(pv, OEPoplarDir, "PoplarTree001_Billboard", 8.7f / 23.2f);
                    kit.poplar = list.Count;
                    list.Add(pv);
                }
            }

            var small = new List<int>();
            var barkSmall = NMLitMat("NM_T_beech_bark_02", NMTreeDir, "T_beech_bark_02_BC", "T_beech_bark_02_N", "T_beech_bark_02_MaskMap", false, Color.white);
            foreach (var name in NMSmallBeech)
            {
                var v = LoadVariant(NMUnderDir, name, barkSmall);
                if (v == null) continue;
                v.leafDress = new[] { bare, green, green, fall2 != null ? fall2[n++ % fall2.Length] : fall[n++ % 3], bare };   // no late green: they end at 220 m
                v.maxDist = UnderMaxDist;
                v.thin = 0.45f;
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
                v.thin = 0.45f;
                bushes.Add(list.Count); list.Add(v);
            }
            var cover = new List<int>();
            var coverMats = new Dictionary<string, Material[]>();
            foreach (var (fbx, tex) in NMCover)
            {
                if (!coverMats.TryGetValue(tex, out var dressM))
                {
                    var baseM = NMLitMat("NM_" + tex, NMCoverDir, tex + "_BC_M", tex + "_N", null, true, Color.white, maskIsHdrp: false);
                    var fallM = Fall2
                        ? NMLitMat("NM_" + tex + "_fall2", NMCoverDir, tex + "_BC_M", tex + "_N", null, true, new Color(1.05f, 0.92f, 0.70f), maskIsHdrp: false)
                        : NMLitMat("NM_" + tex + "_fall", NMCoverDir, tex + "_BC_M", tex + "_N", null, true, new Color(1.10f, 0.86f, 0.55f), maskIsHdrp: false);
                    // FALL 2: the ferns and grasses bronze and tan (bracken, broomsedge), not mustard
                    if (Fall2) Fall2Dress(fallM, Fall2Copper, false, 0.7f);
                    var winM = NMLitMat("NM_" + tex + "_winter", NMCoverDir, tex + "_BC_M", tex + "_N", null, true, new Color(0.78f, 0.64f, 0.46f), maskIsHdrp: false);
                    dressM = new[] { winM, baseM, baseM, fallM, null };
                    coverMats[tex] = dressM;
                }
                var v = LoadVariant(NMCoverDir, fbx, dressM[2], ".fbx");
                if (v == null) continue;
                for (int s = 0; s < v.leafSub.Length; s++) v.leafSub[s] = true;
                v.leafDress = dressM;
                v.maxDist = CoverMaxDist;
                v.thin = 0.3f;
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
                v.maxDist = kind == 0 ? 100f : kind == 1 ? 110f : 180f;
                v.thin = kind <= 1 ? 0.35f : 0.55f;
                if (kind == 0)
                {
                    for (int q = 0; q < v.leafSub.Length; q++) v.leafSub[q] = true;
                    v.leafDress = new[] { dm, dm, dm, dm, null };
                }
                debris[kind].Add(list.Count); list.Add(v);
            }
            for (int k = 0; k < 5; k++) kit.debris[k] = debris[k].ToArray();
            // THE MEADOW PACK: maple bushes into the understory (scarlet in
            // fall - the red at the road's edge in the owner's Parkway frames)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MeadowMapleDir + "/maple_bush_01.FBX") != null)
            {
                var mGreen = NMLitMat("NM_maple_bush", MeadowMapleDir, "T_maple_bush_BC", "T_maple_bush_N", null, true, Color.white);
                var mFall = NMLitMat(Fall2 ? "NM_maple_bush_fall2" : "NM_maple_bush_fall", MeadowMapleDir, "T_maple_bush_BC", "T_maple_bush_N", null, true, Color.white);
                mFall.SetColor("_LeafTarget", new Color(0.92f, 0.14f, 0.07f));
                var mlv = mFall.GetVector("_LeafVivid"); mlv.w = 0.8f; mFall.SetVector("_LeafVivid", mlv);
                if (Fall2) Fall2Dress(mFall, Fall2Scarlet, false);
                for (int m = 1; m <= 4; m++)
                {
                    var v = LoadVariant(MeadowMapleDir, "maple_bush_0" + m, mGreen);
                    if (v == null) continue;
                    for (int s = 0; s < v.leafSub.Length; s++) v.leafSub[s] = true;
                    v.leafDress = new[] { null, mGreen, mGreen, mFall, null };
                    v.maxDist = UnderMaxDist;
                    v.thin = 0.45f;
                    bushes.Add(list.Count); list.Add(v);
                }
            }
            kit.cliffs = LoadCliffs(kit, list);
            kit.fences = LoadFences(list);
            kit.smallBeech = small.ToArray();
            kit.bushes = bushes.ToArray();
            kit.variants = list.ToArray();
            // TREE MATCH: the canopy's far cards made from their own LOW meshes, in each dress (PSX_TREE_MATCH=0: none)
            {
                var tmNames = new List<string>();
                foreach (var tv in kit.variants) tmNames.Add(tv != null && tv.lods != null && tv.lods.Length > 0 && tv.lods[0] != null ? tv.lods[0].name : "variant");
                TreeMatchKit(kit.variants, tmNames, "stage", kit.leafByDress, false);
                kit.matchCardAtlas = TMStageCardAtlas();
            }
            Log($"Mesh forest kit: {kit.treeCount} canopy beeches (+impostors), {small.Count} small beeches, {bushes.Count} cherry bushes, poplar {(kit.poplar >= 0 ? "yes" : "no")}, {cover.Count} ground-cover plants, debris " +
                $"{debris[0].Count} litter / {debris[1].Count} branches / {debris[2].Count} stones / {debris[3].Count} logs / {debris[4].Count} stumps.");
            meshTreeKit = kit;
            return kit;
        }

        /// <summary>One FBX's LOD meshes, its first slot's material, the leaf
        /// slot last ([bark, leaves] in every one of the pack's beeches, read in
        /// Blender), and its node transform with the base put at y = 0.</summary>
        static MeshForest.Variant LoadVariant(string dir, string name, Material barkMat, string ext = ".FBX", int skip = 0)
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
                    int lod = n.EndsWith("_LOD0") ? 0 : n.EndsWith("_LOD1") ? 1 : n.EndsWith("_LOD2") ? 2
                            : n.EndsWith("_LOD3") ? 3 : -1;
                    lod -= skip;
                    if (lod < 0 || lod > 2) continue;
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
                float baseY = minY;
                // A TREE STANDS ON ITS TRUNK, not on its bounds' centre (owner
                // 2026-11-09: "floating trees ... trees that aren't fully in the
                // ground"): a lopsided crown moved the trunk off its spot - and
                // off its collider - and roots below the base lifted it. The
                // trunk's foot is the bark's lowest band: its centre, its height.
                if (subs > 1 && TreeKit.Read(v.lods[0], leaf == 0 ? 1 : 0, out var bv, out var bt))
                {
                    var ys = new List<float>();
                    foreach (int ti in bt) ys.Add(pre.MultiplyPoint3x4(bv[ti]).y);
                    ys.Sort();
                    float footY = ys[Mathf.Min(ys.Count - 1, ys.Count / 50)];   // the 2nd percentile: past the root tips
                    double sx = 0, sz = 0; int cnt = 0;
                    foreach (int ti in bt)
                    {
                        var q = pre.MultiplyPoint3x4(bv[ti]);
                        if (q.y > footY + 0.6f) continue;
                        sx += q.x; sz += q.z; cnt++;
                    }
                    if (cnt > 0) { p0.x = (float)(sx / cnt); p0.z = (float)(sz / cnt); }
                    baseY = footY;
                }
                v.pre = Matrix4x4.Translate(new Vector3(-p0.x, -baseY, -p0.z)) * pre;
                v.height = Mathf.Max(0.3f, maxY - baseY);
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

        /// <summary>Far impostors grow from 450 m to 2.4x at 1.8 km, so a slope
        /// of them stays a closed canopy instead of sub-pixel cards.</summary>
        static void FarGrow(Material m)
        {
            if (m == null) return;
            m.SetVector("_PSXFarGrow", new Vector4(450f, 1800f, 2.4f, 0f));
            EditorUtility.SetDirty(m);
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
            // Leaves and plants a good deal more saturated and a little
            // brighter than the scans: the fall forest the owner's Parkway
            // frames show, under the game's matte film grade.
            // z = 1: PSX/Lit's FOLIAGE LIGHT (wrap + translucency), which is
            // what lifts a canopy in daylight - not the albedo.
            mat.SetVector("_LeafVivid", leaves ? new Vector4(LeafSaturation, LeafBrightness, 1f, 0f) : new Vector4(1f, 1f, 0f, 0f));
            SetHDNormal(mat, string.IsNullOrEmpty(n) ? null : FindTex(dir, n));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        const float LeafCutoff = 0.33f;
        const float LeafSaturation = 1.3f, LeafBrightness = 1.6f;   // measured: x1.12 left the fall canopy at value 0.26 against the owner's Parkway frame's 0.62

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
            if (tp.Contains("Meadow/Ground/") && tp.EndsWith("_A_SM.tga")) { SetHDNormal(m, tp.Replace("_A_SM.tga", "_N.png")); return; }   // FALL 2's floors
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
                    if (StageKeepOut(x, z)) continue;
                    if (f.d < roadHalf + 0.9f || f.d > CoverBand) continue;
                    if (BridgeAt(f.station) > 0.35f) continue;
                    float keep = Mathf.Lerp(0.95f, 0.55f, Mathf.InverseLerp(roadHalf + 3f, CoverBand, f.d));
                    if (((h >> 20) & 1023u) / 1023f > keep) continue;
                    float lat = StageLatticeY(x, z), top = StageBankTopY(f);
                    if (OnGradedRoadside(f) && top > lat + 0.3f && !OnRockTop(f)) continue;   // a cut face (its top may grow)
                    float gy = Mathf.Max(Mathf.Max(lat, top), StageFillY(x, z));
                    int vi = kit.cover[(int)((h >> 5) % (uint)kit.cover.Length)];
                    float sc = Mathf.Lerp(1.5f, 2.6f, ((h >> 3) & 255u) / 255f);
                    float cy = gy - 0.03f;
                    // no plant through a deck, a wall or into the lanes (PlantClear)
                    if (FitPlant(PlantClass.Cover, kit, ref x, ref z, ref cy, ref sc, ref vi, CoverBaseAt, 0.4f,
                                 new[] { new PlantSwap { pool = kit.cover, s0 = 0.7f, s1 = 1.3f } }, h % 360u) == PlantFit.Dropped) continue;
                    inst.Add(new Vector4(x, cy, z, (h % 360u)));
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
                        if (StageKeepOut(x, z)) continue;
                        if (f.d < roadHalf + L.near || f.d > L.far) continue;
                        if (BridgeAt(f.station) > 0.35f) continue;
                        float lat = StageLatticeY(x, z), top = StageBankTopY(f);
                        bool graded = OnGradedRoadside(f) && !OnRockTop(f);
                        if (graded && (L.kind >= 2 || top > lat + 0.3f)) continue;
                        float gy = Mathf.Max(Mathf.Max(lat, top), StageFillY(x, z));
                        float hx = (StageLatticeY(x + 1f, z) - StageLatticeY(x - 1f, z)) * 0.5f;
                        float hz = (StageLatticeY(x, z + 1f) - StageLatticeY(x, z - 1f)) * 0.5f;
                        var n = new Vector3(-hx, 1f, -hz).normalized;
                        if (n.y < 0.6f && L.kind >= 3) continue;   // no log or stump on a 53-degree face
                        int vi = pool[(int)((h >> 5) % (uint)pool.Length)];
                        float sc = Mathf.Lerp(L.s0, L.s1, ((h >> 3) & 255u) / 255f);
                        // laid on its own slope: shortened or dropped, never moved (PlantClear)
                        float dy = gy - L.sink;
                        var tilt = new Vector2(Mathf.Atan2(n.z, n.y) * Mathf.Rad2Deg, -Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg);
                        if (FitPlant(PlantClass.Debris, kit, ref x, ref z, ref dy, ref sc, ref vi, (px, pz, ps) => gy - L.sink, 0.4f, null,
                                     h % 360u, tilt, canMove: false, tilted: true, envFloor: L.kind <= 1 ? 0.3f : 0.02f) == PlantFit.Dropped) continue;
                        tilts[inst.Count] = tilt;
                        inst.Add(new Vector4(x, dy, z, (h % 360u)));
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
            if (StageKeepOut(x, z)) return false;
            if (f.d < RoadWidth * 0.5f + 4.5f || f.d > 90f) return false;
            if (BridgeAt(f.station) > 0.35f || (OnGradedRoadside(f) && !OnRockTop(f))) return false;
            gy = Mathf.Max(Mathf.Max(StageLatticeY(x, z), StageBankTopY(f)), StageFillY(x, z));
            return true;
        }

        /// <summary>How far to sink a trunk of radius <paramref name="r"/> so its
        /// downhill side meets the slope (m).</summary>
        static float SlopeSink(float x, float z, float r)
        {
            float hx = (StageLatticeY(x + 1f, z) - StageLatticeY(x - 1f, z)) * 0.5f;
            float hz = (StageLatticeY(x, z + 1f) - StageLatticeY(x, z - 1f)) * 0.5f;
            return Mathf.Min(1.6f, Mathf.Sqrt(hx * hx + hz * hz) * r);
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
                if (StageKeepOut(ux, uz)) continue;
                if (f2.d < roadHalf + UnderClearM || f2.d > ForestBand) continue;
                if (BridgeAt(f2.station) > 0.35f || (OnGradedRoadside(f2) && !OnRockTop(f2))) continue;
                float gy = Mathf.Max(Mathf.Max(StageLatticeY(ux, uz), StageBankTopY(f2)), StageFillY(ux, uz));
                bool bush = kit.bushes.Length > 0 && (kit.smallBeech.Length == 0 || (h & 0x10000u) != 0);
                int[] pool = bush ? kit.bushes : kit.smallBeech;
                int vi = pool[(int)((h >> 20) % (uint)pool.Length)];
                float t = ((h >> 4) % 100u) * 0.01f;
                float sc = bush ? Mathf.Lerp(1.4f, 2.3f, t) : Mathf.Lerp(0.8f, 1.4f, t);
                float uy = gy - 0.1f - SlopeSink(ux, uz, 0.35f);
                // no plant through a deck, a wall or into the lanes (PlantClear)
                var swaps = bush
                    ? new[] { new PlantSwap { pool = kit.cover, s0 = 1.0f, s1 = 2.0f } }
                    : new[] { new PlantSwap { pool = kit.bushes, s0 = 1.0f, s1 = 2.0f }, new PlantSwap { pool = kit.cover, s0 = 1.0f, s1 = 2.0f } };
                if (FitPlant(bush ? PlantClass.Bush : PlantClass.SmallBeech, kit, ref ux, ref uz, ref uy, ref sc, ref vi, UnderBaseAt, 0.4f,
                             swaps, h % 360u) == PlantFit.Dropped) continue;
                inst.Add(new Vector4(ux, uy, uz, (h % 360u)));
                scaleVar.Add(new Vector2(sc, vi));
                added++;
            }
        }
    }
}
