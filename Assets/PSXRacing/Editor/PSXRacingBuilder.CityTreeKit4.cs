using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Imports the delivered Charlotte and Blue Ridge forms into the PC city tree kit.
    /// tools/charlotte-trees.ps1 copies the owner's models and textures into Assets/CharlotteTrees.
    /// Each FBX supplies three mesh levels, bark and leaf submeshes, and a ground-level pivot.
    /// The five game seasons bind the supplied green, twig, fall and bloom atlases where available.
    /// The kit projects each dressed LOD2 into four views and mirrors them for opposite bearings.
    /// Each pruned street form is baked from its own geometry. The game uses its PSX/Lit shader
    /// and the measured alpha cutoffs from the delivery's Distance/GameCrownSampling.csv.
    /// Appended after round 3's forms (CityTreeKit.G4Charlotte): the indices of
    /// rounds 1-3 are unchanged, so PSX_CT_CHARLOTTE=0 draws round 3's trees.
    /// DEFAULT OFF (owner 11-15: "the 'after' trees look pretty poor in comparison"): PSX_CT_R4KIT=1 adds
    /// them; without it, or with no delivery, round 3's kit exactly.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string CT4Dir = "Assets/CharlotteTrees";
        const string CT4Gen = CT4Dir + "/Gen";
        /// <summary>A distance card's cell (px, as AddStripImpostor reads it) and the supersampling.</summary>
        const int CT4Cell = 256, CT4SS = 2;

        struct CT4Form
        {
            public string stem, leaf, bark;
            public bool ever, bloom;
            public Color fall; public float fallW;
            public CityTreeKit.Role role;
        }

        static CT4Form F4(string stem, string leaf, string bark, CityTreeKit.Role role, Color fall, float fallW, bool ever = false, bool bloom = false)
            => new CT4Form { stem = stem, leaf = leaf, bark = bark, role = role, fall = fall, fallW = fallW, ever = ever, bloom = bloom };

        /// <summary>The catalogue forms (FBX stem, leaf atlas, bark set as the delivery binds them) and the fall each turns.</summary>
        static readonly CT4Form[] CT4Forms =
        {
            F4("LoblollyPine_Mature", "LoblollyPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("LoblollyPine_Young", "LoblollyPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("LoblollyPine_WoodlandEdge", "LoblollyPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            // willow and water oak hold their green late: a light turn
            F4("WillowOak_Avenue", "WillowOak", "Oak", CityTreeKit.Role.Street, new Color(0.70f, 0.50f, 0.16f), 0.35f),
            F4("WillowOak_YoungStreet", "WillowOak", "Smooth", CityTreeKit.Role.Street, new Color(0.70f, 0.50f, 0.16f), 0.35f),
            F4("WillowOak_OpenGrown", "WillowOak", "Oak", CityTreeKit.Role.Oak, new Color(0.70f, 0.50f, 0.16f), 0.35f),
            F4("CrapeMyrtle_Vase", "CrapeMyrtle", "Myrtle", CityTreeKit.Role.Shrub, new Color(0.86f, 0.26f, 0.10f), 0.80f),
            F4("CalleryPear", "CalleryPear", "Maple", CityTreeKit.Role.Small, new Color(0.50f, 0.08f, 0.12f), 0.80f, bloom: true),
            F4("FloweringDogwood", "FloweringDogwood", "Oak", CityTreeKit.Role.Small, new Color(0.55f, 0.08f, 0.10f), 0.82f, bloom: true),
            F4("FloweringCherry", "FloweringCherry", "Cherry", CityTreeKit.Role.Small, new Color(0.95f, 0.42f, 0.10f), 0.80f, bloom: true),
            F4("PurpleleafPlum", "PurpleleafPlum", "Cherry", CityTreeKit.Role.Small, new Color(0.45f, 0.12f, 0.12f), 0.30f, bloom: true),
            F4("SouthernMagnolia", "SouthernMagnolia", "Smooth", CityTreeKit.Role.Broad, default, 0f, ever: true),
            F4("AmericanHolly", "AmericanHolly", "Smooth", CityTreeKit.Role.Small, default, 0f, ever: true),
            F4("EasternRedcedar", "EasternRedcedar", "Cedar", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("WaterOak", "WaterOak", "Oak", CityTreeKit.Role.Broad, new Color(0.66f, 0.52f, 0.20f), 0.25f),
            F4("WhiteOak", "WhiteOak", "Maple", CityTreeKit.Role.Broad, new Color(0.55f, 0.22f, 0.12f), 0.70f),
            F4("NorthernRedOak", "NorthernRedOak", "Oak", CityTreeKit.Role.Broad, new Color(0.62f, 0.20f, 0.08f), 0.75f),
            F4("RedMaple", "RedMaple", "Maple", CityTreeKit.Role.Broad, new Color(0.88f, 0.12f, 0.08f), 0.85f),
            F4("SugarMaple", "SugarMaple", "Maple", CityTreeKit.Role.Broad, new Color(1.00f, 0.50f, 0.08f), 0.85f),
            F4("SilverMaple", "SilverMaple", "Maple", CityTreeKit.Role.Broad, new Color(0.90f, 0.82f, 0.35f), 0.60f),
            F4("Sweetgum", "Sweetgum", "Oak", CityTreeKit.Role.Broad, new Color(0.62f, 0.10f, 0.22f), 0.80f),
            F4("TulipPoplar", "TulipPoplar", "Maple", CityTreeKit.Role.Poplar, new Color(1.00f, 0.80f, 0.14f), 0.85f),
            F4("Hickory", "Hickory", "Maple", CityTreeKit.Role.Broad, new Color(0.95f, 0.75f, 0.15f), 0.85f),
            F4("AmericanSycamore", "AmericanSycamore", "Sycamore", CityTreeKit.Role.Broad, new Color(0.72f, 0.55f, 0.30f), 0.75f),
            F4("RiverBirch", "RiverBirch", "Birch", CityTreeKit.Role.Broad, new Color(0.95f, 0.85f, 0.25f), 0.80f),
            F4("EasternRedbud", "EasternRedbud", "Cherry", CityTreeKit.Role.Small, default, 0f, bloom: true),
            F4("LeylandCypress", "LeylandCypress", "Cedar", CityTreeKit.Role.Columnar, default, 0f, ever: true),
            F4("VirginiaPine", "VirginiaPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("EasternWhitePine_Yard", "EasternWhitePine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("JapaneseZelkova", "JapaneseZelkova", "Smooth", CityTreeKit.Role.Street, default, 0f),
            F4("Ginkgo", "Ginkgo", "Maple", CityTreeKit.Role.Street, default, 0f),
            F4("LondonPlane", "LondonPlane", "Sycamore", CityTreeKit.Role.Street, default, 0f),
            F4("LacebarkElm", "LacebarkElm", "Myrtle", CityTreeKit.Role.Street, default, 0f),
            F4("AmericanElm", "AmericanElm", "Oak", CityTreeKit.Role.Broad, default, 0f),
            F4("Hackberry", "Hackberry", "Maple", CityTreeKit.Role.Broad, default, 0f),
            F4("JapaneseMaple", "JapaneseMaple", "Smooth", CityTreeKit.Role.Small, default, 0f),
            F4("SaucerMagnolia", "SaucerMagnolia", "Smooth", CityTreeKit.Role.Small, default, 0f, bloom: true),
            F4("Azalea", "Azalea", "Smooth", CityTreeKit.Role.Shrub, default, 0f, ever: true, bloom: true),
            F4("Boxwood", "Boxwood", "Smooth", CityTreeKit.Role.Shrub, default, 0f, ever: true),
            F4("FoundationHolly", "AmericanHolly", "Smooth", CityTreeKit.Role.Shrub, default, 0f, ever: true),
            F4("WaxMyrtle", "WaxMyrtle", "Smooth", CityTreeKit.Role.Shrub, default, 0f, ever: true),
            F4("Kudzu_EmbankmentDrape", "Kudzu", "Cedar", CityTreeKit.Role.Shrub, default, 0f),
            F4("RedSpruce", "RedSpruce", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("FraserFir", "FraserFir", "Smooth", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("EasternHemlock", "EasternHemlock", "Oak", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("EasternWhitePine_Forest", "EasternWhitePine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("PitchPine", "PitchPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
            F4("TableMountainPine", "TableMountainPine", "Pine", CityTreeKit.Role.PineStandIn, default, 0f, ever: true),
        };

        /// <summary>THE STREET'S WILLOW OAKS (Uptown's grates, the old city's avenues, roadside rows). The
        /// delivered avenue and young street forms carry their lowest limbs at 2-4 m, into the lane's 4.6 m
        /// envelope and the walls of a 3 m walk: in the first after run 91% of Uptown's grate trees fell back
        /// to round 3's stand-ins and the old-city avenues to young trees. Round 3's cut (Rework3: the lower
        /// crown cut at a share of the leaves, the limbs under it raised into the crown's base, the stem
        /// drawn to put that base at a share of the height) applied to the owner's models, as the city
        /// prunes its street trees: round (lr), small-high (ls), narrow under a wall (ln), avenue limbed to
        /// a third (av), broad (lb). Named "CT4P_&lt;stem&gt;_&lt;tag&gt;".</summary>
        static readonly CT3Form[] CT4Pruned =
        {
            new CT3Form { tag = "lr", group = CityTreeKit.G4Charlotte, role = CityTreeKit.Role.Street, w = 1.0f, cy = 0.85f, baseFrac = 0.512f, cutShare = 0.30f, from = new[] { "WillowOak_YoungStreet" } },
            new CT3Form { tag = "ls", group = CityTreeKit.G4Charlotte, role = CityTreeKit.Role.Street, w = 0.9f, cy = 0.80f, baseFrac = 0.637f, cutShare = 0.35f, from = new[] { "WillowOak_YoungStreet" } },
            new CT3Form { tag = "ln", group = CityTreeKit.G4Charlotte, role = CityTreeKit.Role.Street, w = 0.62f, cy = 0.90f, baseFrac = 0.575f, cutShare = 0.30f, from = new[] { "WillowOak_YoungStreet" } },
            new CT3Form { tag = "av", group = CityTreeKit.G4Charlotte, role = CityTreeKit.Role.Street, w = 1.0f, cy = 1.0f, baseFrac = 0.325f, cutShare = 0.12f, from = new[] { "WillowOak_Avenue" } },
            new CT3Form { tag = "lb", group = CityTreeKit.G4Charlotte, role = CityTreeKit.Role.Street, w = 1.0f, cy = 0.90f, baseFrac = 0.45f, cutShare = 0.22f, from = new[] { "WillowOak_Avenue" } },
        };

        /// <summary>Crape myrtle's four summer colours (the delivery's Variants), one kit variant each.</summary>
        static readonly string[] CT4MyrtleBlooms = { "Pink", "Red", "White", "Purple" };

        /// <summary>The kit build's distance cards: how many sheets, their pixels and GPU bytes (DXT5 + mips).</summary>
        internal static int CT4SheetCount, CT4SheetsWritten;
        internal static long CT4SheetGpuBytes;

        static string CT4Tex(string key, string suffix)
        {
            string p = CT4Dir + "/Textures/" + key + "_overhaul1_hd" + suffix + ".png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p) != null ? p : null;
        }

        /// <summary>Round 4's forms appended (group <see cref="CityTreeKit.G4Charlotte"/>).</summary>
        static void CTRound4(List<MeshForest.Variant> list, List<string> names, List<CityTreeKit.Role> roles, List<byte> groups)
        {
            CT5Feet(list, names, groups);       // (round 5b: PSX_CT5_FOOT=1, every level drawn down to its LOD0 trunk base; default OFF)
            CT5NMCrowns(list, names, groups);   // (round 5: PSX_CT5_NMCROWN=1, the NM trees' far crowns from their own LOD2; default OFF)
            if (System.Environment.GetEnvironmentVariable("PSX_CT_R4KIT") != "1") { Log("City trees: Charlotte's own trees not in the kit (default OFF; PSX_CT_R4KIT=1 adds them)"); return; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CT4Dir + "/Models/" + CT4Forms[0].stem + ".FBX") == null)
            {
                Log("City trees: no Charlotte tree delivery in " + CT4Dir + " (tools/charlotte-trees.ps1) - round 3's kit");
                return;
            }
            if (!AssetDatabase.IsValidFolder(CT4Gen)) AssetDatabase.CreateFolder(CT4Dir, "Gen");
            CT4SheetCount = 0; CT4SheetsWritten = 0; CT4SheetGpuBytes = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MeshForest.Variant greenMyrtle = null;
            var barkMats = new Dictionary<string, Material>();
            var sb = new System.Text.StringBuilder("City trees round 4 - CHARLOTTE'S OWN TREES (the owner's models, the game's materials, cards rendered here):");
            void Add(MeshForest.Variant v, string n, CityTreeKit.Role r)
            {
                list.Add(v); names.Add(n); roles.Add(r); groups.Add(CityTreeKit.G4Charlotte);
                sb.Append($"\n  {list.Count - 1} {n}: {v.height:0.0} m, tris {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])}, impostor {(v.lods.Length > 3 && v.lods[3] != null ? "yes" : "NO")}");
            }
            foreach (var f in CT4Forms)
            {
                string barkTex = CT4Tex("Bark_" + f.bark, ""), leafTex = CT4Tex("Leaves_" + f.leaf + "_Leaf", "");
                if (barkTex == null || leafTex == null) { sb.Append($"\n  (no {f.stem}: bark {f.bark} {(barkTex != null ? "ok" : "MISSING")}, leaf {f.leaf} {(leafTex != null ? "ok" : "MISSING")})"); continue; }
                if (!barkMats.TryGetValue(f.bark, out var barkM))
                    barkMats[f.bark] = barkM = CTMat("CT4_bark_" + f.bark, barkTex, CT4Tex("Bark_" + f.bark, "_normal"), false, Color.white);
                var v = LoadVariant(CT4Dir + "/Models", f.stem, barkM);
                if (v == null) { sb.Append("\n  (no " + f.stem + ": the FBX did not load)"); continue; }
                int leafSub = System.Array.IndexOf(v.leafSub, true);
                if (leafSub < 0) { sb.Append("\n  (no " + f.stem + ": no leaf submesh found)"); continue; }
                // THE DELIVERY'S FOOT IS ITS PIVOT (the trunk's foot at the origin, ground level). LoadVariant takes
                // the foot as the bark's 2nd-percentile height, which on the packs' trunks (root flares, rings all
                // the way down) is the ground; on these lean trunks (few rings low down, thousands in the limbs) it
                // is 0.15-2.4 m up the stem - the tree stood that deep in the ground, up to 2.4 m short (measured
                // height + that offset = the delivery's index height, every form). Seated on the pivot here.
                {
                    var q0 = v.pre.MultiplyPoint3x4(Vector3.zero);
                    if (q0.y > -3f && q0.y < 0.5f && new Vector2(q0.x, q0.z).magnitude < 1.2f)
                    {
                        v.pre = Matrix4x4.Translate(-q0) * v.pre;
                        v.height = Mathf.Max(0.3f, v.height - q0.y);
                        if (q0.y < -0.05f) sb.Append($"\n  {f.stem}: seated on its pivot (LoadVariant's foot {-q0.y:0.00} m up the stem, {new Vector2(q0.x, q0.z).magnitude:0.00} m across) -> {v.height:0.0} m");
                    }
                    else sb.Append($"\n  (WARN {f.stem}: the pivot is not a trunk foot ({q0.x:0.00}, {q0.y:0.00}, {q0.z:0.00}): LoadVariant's foot kept)");
                }
                if (CT4AO) CT4CrownAO(v, f.stem, leafSub);
                {
                    // TRUNK BASE PER LEVEL (round 5b, "some even seem to be floating in air"): the lowest bark of each
                    // level in the tree's frame - all levels and the far card (made from LOD2) stand on this foot
                    var tb = new System.Text.StringBuilder();
                    for (int l = 0; l < 3; l++)
                    {
                        float mn = float.MaxValue;
                        var lm = v.lods[l];
                        if (lm != null)
                            for (int s2 = 0; s2 < lm.subMeshCount; s2++)
                            {
                                if (v.leafSub != null && s2 < v.leafSub.Length && v.leafSub[s2]) continue;
                                if (!TreeKit.Read(lm, s2, out var bvv, out var btt)) continue;
                                foreach (int bi in btt) mn = Mathf.Min(mn, v.pre.MultiplyPoint3x4(bvv[bi]).y);
                            }
                        tb.Append(l == 0 ? "" : " / ").Append(mn < float.MaxValue ? mn.ToString("0.00") : "-");
                    }
                    sb.Append($"\n  {f.stem}: trunk base LOD0/1/2 {tb} m");
                }
                var summer = CT4Leaf("CT4_leaf_" + f.leaf, leafTex, CT4Tex("Leaves_" + f.leaf + "_Leaf", "_normal"), default, 0f, CT4LeafVivid);
                v.mats[leafSub] = summer;
                Material fall = summer, spring = summer;
                if (!f.ever) fall = CT4Leaf("CT4_leaf_" + f.leaf + "_fall", leafTex, CT4Tex("Leaves_" + f.leaf + "_Leaf", "_normal"), f.fall, f.fallW, CT4LeafVivid);
                string springTex = f.bloom ? CT4Tex("Leaves_" + f.leaf + "_Spring", "") : null;
                if (springTex != null) spring = CT4Leaf("CT4_leaf_" + f.leaf + "_Spring", springTex, CT4Tex("Leaves_" + f.leaf + "_Spring", "_normal"), default, 0f, CT4BloomVivid);
                else if (f.bloom) sb.Append($"\n  (WARN {f.stem}: no spring bloom atlas)");
                string fallTex = CT4Tex("Leaves_" + f.leaf + "_Fall", "");
                if (fallTex != null) fall = CT4Leaf("CT4_leaf_" + f.leaf + "_authoredFall", fallTex, CT4Tex("Leaves_" + f.leaf + "_Fall", "_normal"), default, 0f, CT4LeafVivid);
                string winterTex = CT4Tex("Leaves_" + f.leaf + "_Winter", "");
                var winter = winterTex != null ? CT4Leaf("CT4_leaf_" + f.leaf + "_twigs", winterTex, CT4Tex("Leaves_" + f.leaf + "_Winter", "_normal"), default, 0f, CT4LeafVivid) : (f.ever ? summer : null);
                v.leafDress = new[] { winter, spring, summer, fall, winter };

                CT4Geo BakeDress(MeshForest.Variant form, string tag)
                {
                    var geometry = CT4Geometry(form);
                    Material Bake(string key, string foliage, Vector2 vivid)
                    {
                        string sheet = CT4Sheet(geometry, tag + key, foliage, barkTex, 0f, vivid);
                        return CT4Imp("CT4_imp_" + tag + key, sheet, default, 0f, vivid);
                    }
                    var green = Bake("_leaf", leafTex, CT4LeafVivid);
                    var twigs = winterTex != null ? Bake("_winter", winterTex, CT4LeafVivid) : f.ever ? green : Bake("_bare", null, CT4LeafVivid);
                    var blossom = springTex != null ? Bake("_spring", springTex, CT4BloomVivid) : green;
                    var autumn = fallTex != null ? Bake("_fall", fallTex, CT4LeafVivid) : f.fallW > 0f ?
                        CT4Imp("CT4_imp_" + tag + "_fall", AssetDatabase.GetAssetPath(green.mainTexture), f.fall, f.fallW, CT4LeafVivid) : green;
                    CT4AddCard(form, "CTImpostor4_" + tag, new[] { twigs, blossom, green, autumn, twigs }, geometry);
                    return geometry;
                }
                var geo = BakeDress(v, f.stem);

                if (f.stem != "CrapeMyrtle_Vase")
                {
                    Add(v, "CT4_" + f.stem, f.role);
                    // ---- the willow oaks PRUNED FOR THE STREET: round 3's cut (Rework3) on the owner's model ----
                    foreach (var pf in CT4Pruned)
                    {
                        if (pf.from[0] != f.stem) continue;
                        string pn = "CT4P_" + f.stem + "_" + pf.tag;
                        var pv = Rework3(v, pn, pf, out string how);
                        if (pv == null) { sb.Append($"\n  (no {pn}: {how})"); continue; }
                        BakeDress(pv, pn);
                        Add(pv, pn, CityTreeKit.Role.Street);
                        sb.Append($" <- pruned: {how}");
                    }
                    continue;
                }
                // ---- crape myrtle: every one in bloom in summer, one variant a colour ----
                greenMyrtle = v;
                foreach (var col in CT4MyrtleBlooms)
                {
                    string bt = CT4Tex("Leaves_CrapeMyrtle_" + col, "");
                    if (bt == null) { sb.Append("\n  (WARN no crape myrtle bloom " + col + ")"); continue; }
                    var bm = CT4Leaf("CT4_leaf_CrapeMyrtle_" + col, bt, CT4Tex("Leaves_CrapeMyrtle_" + col, "_normal"), default, 0f, CT4BloomVivid);
                    var ib = CT4Imp("CT4_imp_" + f.stem + "_" + col.ToLowerInvariant(), CT4Sheet(geo, f.stem + "_" + col.ToLowerInvariant(), bt, barkTex, 0f, CT4BloomVivid), default, 0f, CT4BloomVivid);
                    var cv = new MeshForest.Variant
                    {
                        lods = (Mesh[])v.lods.Clone(), mats = (Material[])v.mats.Clone(), leafSub = (bool[])v.leafSub.Clone(),
                        height = v.height, pre = v.pre, impostorPre = v.impostorPre, shadows = v.shadows, thin = v.thin, maxDist = v.maxDist,
                        leafDress = (Material[])v.leafDress.Clone(),
                        impostorDress = (Material[])v.impostorDress.Clone(),
                    };
                    cv.mats[leafSub] = bm;
                    cv.leafDress[2] = bm; cv.impostorDress[2] = ib;
                    Add(cv, "CT4_" + f.stem + "_" + col.ToLowerInvariant(), f.role);
                }
            }
            if (greenMyrtle != null) Add(greenMyrtle, "CT4_CrapeMyrtle_Vase", CityTreeKit.Role.Shrub);
            sb.Append($"\n  distance cards: {CT4SheetCount} sheets of {4 * CT4Cell} x {CT4Cell} px ({CT4SheetsWritten} written this build), {CT4SheetGpuBytes / 1048576.0:0.0} MB on the GPU (DXT5 + mips); round 4 took {clock.Elapsed.TotalSeconds:0.0} s");
            CT4Measure(list, names, groups);
            Log(sb.ToString());
        }

        /// <summary>THE LEAVES' VIVID STEP (_LeafVivid x saturation, y brightness). The game's 1.3 / 1.6 was
        /// tuned for NatureManufacture's photographed sprays (much grey and shadow in them); on these drawn,
        /// evenly lit atlases it made a lime canopy: measured on six driver's-eye frames, foliage saturation
        /// 0.71 against the NatureManufacture trees' 0.46 and luminance 79 against 73. The shader's step
        /// solved for those: x 0.88, y 1.48. Blossoms (white, pink): 1.0 / 1.25, short of paper white.</summary>
        static Vector2 CT4LeafVivid => new Vector2(0.88f, CT4EnvF("PSX_CT4_BRIGHT", 1.48f));   // (PSX_CT4_BRIGHT: the look experiment's brightness step)
        static readonly Vector2 CT4BloomVivid = new Vector2(1.0f, 1.25f);

        static Material CT4Leaf(string name, string tex, string normal, Color fall, float fallW, Vector2 vivid)
        {
            var m = CTMat(name, tex, normal, true, Color.white);
            m.SetColor("_LeafTulip", CT4Jitter && fallW <= 0f && vivid == CT4LeafVivid ? CT4JitterTint : GreenSpread);
            if (m.HasProperty("_HouseTint")) m.SetFloat("_HouseTint", CT4AO ? 1f : 0f);
            var lv = m.GetVector("_LeafVivid");
            lv.x = vivid.x; lv.y = vivid.y;
            if (fallW > 0f) { m.SetColor("_LeafTarget", fall); lv.w = fallW; }
            m.SetVector("_LeafVivid", lv);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void CT4CardSampling(Material m, string sheet, Vector2 vivid)
        {
            // Calibrated against the outgoing geometry both at native atlas
            // sampling and at the roughly 25–65 pixel crowns of the handoff.
            // Sparse needle sprays, broad leaves and dense blooms filter differently.
            float cut = 0.46f, farCut = 0.55f, gain = 1.23f, farGain = 2.10f;
            if (sheet.Contains("Pine"))
            { cut = 0.56f; farCut = 0.52f; gain = 0.74f; farGain = 2.06f; }
            else if (sheet.Contains("Spruce") || sheet.Contains("Redcedar") || sheet.Contains("Hemlock") || sheet.Contains("Fir") || sheet.Contains("Cypress"))
            { cut = 0.55f; farCut = 0.615f; gain = 0.83f; farGain = 1.98f; }
            else if (vivid == CT4BloomVivid)
            { cut = 0.53f; farCut = 0.725f; gain = 1.50f; farGain = 2.05f; }
            m.color = new Color(gain, gain, gain);
            m.EnableKeyword("PSX_CROWN_CARD");
            m.EnableKeyword("PSX_LODFADE");
            m.SetFloat("_Cutoff", cut);
            m.SetFloat("_CrownFarCut", farCut);
            m.SetFloat("_CrownMipBias", -0.90f);
            m.SetColor("_CrownFarTint", new Color(farGain, farGain, farGain));
            m.SetVector("_PSXFarGrow", new Vector4(0f, 0f, 1f, 0f));
            for (int i = 0; i < 4; i++) m.SetVector("_CrownAngles" + i, -Vector4.one);
            var samples = AssetDatabase.LoadAssetAtPath<TextAsset>(CT4Dir + "/Distance/GameCrownSampling.csv");
            if (samples != null)
            {
                if (samples != ct4SamplingSource)
                {
                    ct4Sampling.Clear();
                    foreach (string row in samples.text.Split('\n'))
                    {
                        var fields = row.Trim().Split(',');
                        if (fields.Length != 19) continue;
                        var values = new float[18];
                        bool valid = true;
                        for (int i = 0; i < values.Length; i++)
                            valid &= float.TryParse(fields[i + 1], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out values[i]) && values[i] > 0f && values[i] < 1f;
                        if (valid) ct4Sampling[fields[0]] = values;
                    }
                    ct4SamplingSource = samples;
                }
                string key = System.IO.Path.GetFileNameWithoutExtension(sheet);
                if (ct4Sampling.TryGetValue(key, out var fitted))
                {
                    m.SetFloat("_Cutoff", fitted[0]);
                    m.SetFloat("_CrownFarCut", fitted[1]);
                    for (int i = 0; i < 4; i++)
                    {
                        int n = 2 + 4 * i;
                        m.SetVector("_CrownAngles" + i, new Vector4(fitted[n], fitted[n + 1], fitted[n + 2], fitted[n + 3]));
                    }
                }
            }
        }

        static TextAsset ct4SamplingSource;
        static readonly Dictionary<string, float[]> ct4Sampling = new Dictionary<string, float[]>();

        static Material CT4Imp(string name, string sheet, Color fall, float fallW, Vector2 vivid)
        {
            var m = CTMat(name, sheet, null, true, Color.white);
            CT4CardSampling(m, sheet, vivid);
            m.SetColor("_LeafTulip", CT4Jitter && fallW <= 0f && vivid == CT4LeafVivid ? CT4JitterTint : GreenSpread);
            { var v0 = m.GetVector("_LeafVivid"); v0.x = vivid.x; v0.y = vivid.y; m.SetVector("_LeafVivid", v0); }
            if (fallW > 0f)
            {
                m.SetColor("_LeafTarget", fall);
                var lv = m.GetVector("_LeafVivid"); lv.w = fallW; m.SetVector("_LeafVivid", lv);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        // ==================================================================
        //  THE DISTANCE CARDS, RENDERED ON THE CPU
        // ==================================================================
        class CT4Geo
        {
            public Vector3[] p;   // triangle corners, three a triangle, in the tree's frame (foot at 0)
            public Vector2[] uv;
            public bool[] leaf;   // per triangle
            public float[] ao;    // per triangle: the crown-depth darkening (PSX_CT4_AO), 1 without
            public float S, R, h; // the cell's side (m), the crown's reach, the height
        }

        // All four directions share one projection scale. The baked crown must
        // preserve the branch gaps in the last geometric level at the handoff.
        static void CT4AddCard(MeshForest.Variant v, string name, Material[] dress, CT4Geo g)
        {
            float half = Mathf.Min(g.S * 0.5f, g.R + g.S / CT4Cell * 2f);
            float top = g.S;
            float u = half / g.S;
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(-half, 0, 0), new Vector3(-half, top, 0),
                                    new Vector3(half, top, 0), new Vector3(half, 0, 0) };
            mesh.uv = new[] { new Vector2(0.5f-u, 0), new Vector2(0.5f-u, 1),
                              new Vector2(0.5f+u, 1), new Vector2(0.5f+u, 0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.up * top * 0.5f, new Vector3(half*2, top, half*2));
            v.impostorPre = Matrix4x4.identity;
            v.lods = new[] { v.lods[0], v.lods[1], v.lods[2], WorldKit.SaveMesh(mesh, name) };
            v.impostorDress = dress;
        }

        static CT4Geo CT4Geometry(MeshForest.Variant v)
        {
            var m = v.lods.Length > 2 && v.lods[2] != null ? v.lods[2] : v.lods[0];
            var g = new CT4Geo { h = v.height };
            var uv = ReadUV(m);
            var vcol = m.isReadable && m.colors != null && m.colors.Length == m.vertexCount ? m.colors : null;
            var aos = new List<float>();
            var ps = new List<Vector3>(); var us = new List<Vector2>(); var lf = new List<bool>();
            float rx = 0f, rl = 0f;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                bool leaf = v.leafSub != null && s < v.leafSub.Length && v.leafSub[s];
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int i = tris[t + j];
                        var q = v.pre.MultiplyPoint3x4(verts[i]);
                        ps.Add(q); us.Add(uv != null && i < uv.Length ? uv[i] : Vector2.zero);
                        rx = Mathf.Max(rx, Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.z)));
                        if (leaf) rl = Mathf.Max(rl, Mathf.Sqrt(q.x * q.x + q.z * q.z));
                    }
                    lf.Add(leaf);
                    aos.Add(vcol != null ? (vcol[tris[t]].r + vcol[tris[t + 1]].r + vcol[tris[t + 2]].r) * (2f / 3f) : 1f);
                }
            }
            g.p = ps.ToArray(); g.uv = us.ToArray(); g.leaf = lf.ToArray(); g.ao = aos.ToArray();
            g.R = Mathf.Max(0.5f, rl);
            g.S = 1.04f * Mathf.Max(v.height, 2f * Mathf.Max(rl, rx));
            return g;
        }

        static readonly Dictionary<string, (Color32[] px, int n)> ct4Px = new Dictionary<string, (Color32[], int)>();

        static (Color32[] px, int n) CT4Pixels(string path)
        {
            if (string.IsNullOrEmpty(path)) return (null, 0);
            if (ct4Px.TryGetValue(path, out var have)) return have;
            const int N = 1024;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var r = (tex != null ? ReadTex(tex, N, N) : null, N);
            ct4Px[path] = r;
            return r;
        }

        static uint CT4Hash(int x, int y, int z)
        {
            uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)z * 0xcb1ab31fu;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }

        /// <summary>One sheet: the four views (cell q looks along Euler(0, 45q, 0) * +Z, its right
        /// = that * +X), the trunk on the cell's middle
        /// column, the foot on its bottom row, geo.S metres a side; albedo with a soft light from
        /// above the viewer baked in. Bark texels are pre-divided by the leaf material's vivid
        /// step (the card is drawn with a leaf material), so the trunk comes out as the mesh's.
        /// twig &gt; 0: the bare dress - the limbs, and that share of the leaf cards' texels as a
        /// grey-brown haze of twigs. Written only when the bytes differ.</summary>
        static string CT4Sheet(CT4Geo g, string tag, string leafTex, string barkTex, float twig, Vector2 vivid)
        {
            string path = CT4Gen + "/CT4Sheet_" + tag + ".png";
            var (lp, ln) = CT4Pixels(leafTex);
            var (bp, bn) = CT4Pixels(barkTex);
            int C = CT4Cell * CT4SS, W = 4 * CT4Cell;
            var outPx = new Color32[W * CT4Cell];
            var col = new Color[C * C];
            var z = new float[C * C];
            var twigC = new Color(0.37f, 0.33f, 0.30f);
            byte cut = (byte)(LeafCutoff * 255f);
            for (int q = 0; q < 4; q++)
            {
                var rot = Quaternion.Euler(0f, q * 45f, 0f);
                Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
                var L = (Vector3.up * 0.85f - fwd * 0.45f + right * 0.25f).normalized;
                for (int i = 0; i < z.Length; i++) { z[i] = float.MaxValue; col[i] = new Color(0f, 0f, 0f, 0f); }
                float k = C / g.S;
                var sx = new float[3]; var sy = new float[3]; var sz = new float[3];
                for (int t = 0; t < g.leaf.Length; t++)
                {
                    bool leaf = g.leaf[t];
                    if (leaf ? lp == null : bp == null) continue;
                    int b = t * 3;
                    for (int j = 0; j < 3; j++)
                    {
                        var p = g.p[b + j];
                        sx[j] = (Vector3.Dot(p, right) * k) + C * 0.5f;
                        sy[j] = p.y * k;
                        sz[j] = Vector3.Dot(p, fwd);
                    }
                    float area = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0]);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sx[0], Mathf.Min(sx[1], sx[2])))), x1 = Mathf.Min(C - 1, Mathf.CeilToInt(Mathf.Max(sx[0], Mathf.Max(sx[1], sx[2]))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sy[0], Mathf.Min(sy[1], sy[2])))), y1 = Mathf.Min(C - 1, Mathf.CeilToInt(Mathf.Max(sy[0], Mathf.Max(sy[1], sy[2]))));
                    if (x0 > x1 || y0 > y1) continue;
                    // the face's light: a leaf card lit from the side it is seen from (two-sided)
                    var n = Vector3.Cross(g.p[b + 1] - g.p[b], g.p[b + 2] - g.p[b]).normalized;
                    if (leaf && Vector3.Dot(n, fwd) > 0f) n = -n;
                    float dif = Mathf.Max(0f, Vector3.Dot(n, L));
                    float shade;
                    if (leaf)
                    {
                        var c3 = (g.p[b] + g.p[b + 1] + g.p[b + 2]) / 3f;
                        float rr = Mathf.Sqrt(c3.x * c3.x + c3.z * c3.z) / (0.8f * g.R);
                        shade = (0.60f + 0.40f * dif) * (0.76f + 0.24f * Mathf.Clamp01(rr)) * (g.ao != null && t < g.ao.Length ? g.ao[t] : 1f);
                    }
                    else shade = 0.58f + 0.42f * dif;
                    float inv = 1f / area;
                    Vector2 u0 = g.uv[b], u1 = g.uv[b + 1], u2 = g.uv[b + 2];
                    for (int y = y0; y <= y1; y++)
                    {
                        float py = y + 0.5f;
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f;
                            float w0 = ((sx[1] - px) * (sy[2] - py) - (sx[2] - px) * (sy[1] - py)) * inv;
                            float w1 = ((sx[2] - px) * (sy[0] - py) - (sx[0] - px) * (sy[2] - py)) * inv;
                            float w2 = 1f - w0 - w1;
                            if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                            int idx = y * C + x;
                            float d = w0 * sz[0] + w1 * sz[1] + w2 * sz[2];
                            if (d >= z[idx]) continue;
                            float u = w0 * u0.x + w1 * u1.x + w2 * u2.x, vv = w0 * u0.y + w1 * u1.y + w2 * u2.y;
                            Color c;
                            if (leaf)
                            {
                                int tx = (int)((u - Mathf.Floor(u)) * ln), ty = (int)((vv - Mathf.Floor(vv)) * ln);
                                var c32 = lp[Mathf.Clamp(ty, 0, ln - 1) * ln + Mathf.Clamp(tx, 0, ln - 1)];
                                if (c32.a < cut) continue;
                                if (twig > 0f)
                                {
                                    // (one hash an output texel, the four samples alike: the haze survives the reduction)
                                    if ((CT4Hash(x / CT4SS, y / CT4SS, q) & 0xFFFF) >= twig * 65535f) continue;
                                    c = CT4Unvivid(twigC * (0.8f + 0.4f * dif), vivid);
                                }
                                else c = (Color)c32 * shade;
                            }
                            else
                            {
                                int tx = (int)((u - Mathf.Floor(u)) * bn), ty = (int)((vv - Mathf.Floor(vv)) * bn);
                                c = CT4Unvivid((Color)bp[Mathf.Clamp(ty, 0, bn - 1) * bn + Mathf.Clamp(tx, 0, bn - 1)] * shade, vivid);
                            }
                            c.a = 1f;
                            z[idx] = d; col[idx] = c;
                        }
                    }
                }
                // the samples reduced: colour the mean of the covered ones, alpha the share covered
                for (int y = 0; y < CT4Cell; y++)
                    for (int x = 0; x < CT4Cell; x++)
                    {
                        Color acc = new Color(0f, 0f, 0f, 0f); int nCov = 0;
                        for (int dy = 0; dy < CT4SS; dy++)
                            for (int dx = 0; dx < CT4SS; dx++)
                            {
                                var c = col[(y * CT4SS + dy) * C + x * CT4SS + dx];
                                if (c.a > 0.5f) { acc += c; nCov++; }
                            }
                        Color o = nCov > 0 ? acc / nCov : new Color(0f, 0f, 0f, 0f);
                        o.a = nCov / (float)(CT4SS * CT4SS);
                        outPx[y * W + q * CT4Cell + x] = o;
                    }
            }
            // ---- written only when it changed (an unchanged sheet is not re-imported) ----
            var tex = new Texture2D(W, CT4Cell, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx); tex.Apply();
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string full = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, path);
            bool same = System.IO.File.Exists(full) && CT4Same(System.IO.File.ReadAllBytes(full), png);
            if (!same) { System.IO.File.WriteAllBytes(full, png); CT4SheetsWritten++; AssetDatabase.ImportAsset(path); }
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = !same;
                if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (ti.maxTextureSize != W) { ti.maxTextureSize = W; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.Compressed) { ti.textureCompression = TextureImporterCompression.Compressed; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            CT4SheetCount++;
            CT4SheetGpuBytes += (long)W * CT4Cell * 4 / 3;   // DXT5: a byte a texel, mips a third more
            return path;
        }

        static bool CT4Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }


        // ==================================================================
        //  THE LOOK EXPERIMENT (owner 11-15 on the sheet: "the 'after' trees look pretty poor in
        //  comparison"): kit switches, each its own, to learn how much of the gap is shading
        // ==================================================================
        static float CT4EnvF(string n, float d) => float.TryParse(System.Environment.GetEnvironmentVariable(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : d;
        /// <summary>PSX_CT4_AO=1: crown-depth darkening in the leaf vertices' colour (PSX/Lit's _HouseTint path: light x 2 x colour).</summary>
        static bool CT4AO => System.Environment.GetEnvironmentVariable("PSX_CT4_AO") == "1";
        /// <summary>PSX_CT4_JITTER=1: three crowns in ten turned toward an olive green (_LeafTulip, the instance's own hash), on mesh and card alike.</summary>
        static bool CT4Jitter => System.Environment.GetEnvironmentVariable("PSX_CT4_JITTER") == "1";
        static readonly Color CT4JitterTint = new Color(0.60f, 0.66f, 0.20f, 0.30f);

        /// <summary>CROWN-DEPTH DARKENING, as the NM trees carry AO in their vertex colour: each leaf vertex
        /// of every level darker the deeper it sits in the crown (its distance from the crown's axis against
        /// the crown's reach at that height) and the lower it hangs in it (the underside); bark as drawn.
        /// The levels are copied (Generated/CT4AO_*) with the colour.</summary>
        static void CT4CrownAO(MeshForest.Variant v, string stem, int leafSub)
        {
            if (!TreeKit.Read(v.lods[0], leafSub, out var lv, out var lt) || lt.Length == 0) return;
            var cc = Vector2.zero; var ys = new List<float>();
            foreach (int i in lt) { var q = v.pre.MultiplyPoint3x4(lv[i]); cc += new Vector2(q.x, q.z); ys.Add(q.y); }
            cc /= lt.Length; ys.Sort();
            float yB = ys[(int)(ys.Count * 0.03f)], yT = ys[Mathf.Min(ys.Count - 1, (int)(ys.Count * 0.98f))];
            const int NB = 12;
            var lists = new List<float>[NB]; for (int b = 0; b < NB; b++) lists[b] = new List<float>();
            int Band(float y) => Mathf.Clamp((int)((y - yB) / Mathf.Max(0.5f, yT - yB) * NB), 0, NB - 1);
            foreach (int i in lt) { var q = v.pre.MultiplyPoint3x4(lv[i]); lists[Band(q.y)].Add(Vector2.Distance(new Vector2(q.x, q.z), cc)); }
            var rb = new float[NB];
            for (int b = 0; b < NB; b++) { var l = lists[b]; l.Sort(); rb[b] = l.Count > 0 ? l[Mathf.Min(l.Count - 1, (int)(l.Count * 0.9f))] : 0f; }
            for (int b = 1; b < NB; b++) if (rb[b] <= 0f) rb[b] = rb[b - 1];
            for (int b = NB - 2; b >= 0; b--) if (rb[b] <= 0f) rb[b] = rb[b + 1];
            float AO(Vector3 q)
            {
                float d = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(q.x, q.z), cc) / Mathf.Max(0.3f, rb[Band(q.y)]));
                float u = 1f - Mathf.Clamp01((q.y - yB) / Mathf.Max(0.5f, yT - yB));
                return Mathf.Clamp(1f - 0.55f * Mathf.Pow(d, 1.2f) - 0.30f * Mathf.Pow(u, 1.5f), 0.32f, 1f);
            }
            var done = new Dictionary<Mesh, Mesh>();
            float sum = 0f; int cnt = 0;
            for (int l = 0; l < 3; l++)
            {
                var src = v.lods[l];
                if (src == null) continue;
                if (done.TryGetValue(src, out var have)) { v.lods[l] = have; continue; }
                var m = Object.Instantiate(ReadableCopy(src));
                m.name = "CT4AO_" + stem + "_L" + l;
                var verts = m.vertices;
                var col = new Color[verts.Length];
                for (int i = 0; i < col.Length; i++) col[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
                if (leafSub < m.subMeshCount)
                    foreach (int i in m.GetTriangles(leafSub))
                    {
                        float a = AO(v.pre.MultiplyPoint3x4(verts[i]));
                        col[i] = new Color(0.5f * a, 0.5f * a, 0.5f * a, 1f);
                        if (l == 0) { sum += a; cnt++; }
                    }
                m.colors = col;
                var saved = WorldKit.SaveMesh(m, m.name);
                done[src] = saved; v.lods[l] = saved;
            }
            Log($"City trees: {stem} crown-depth AO mean {(cnt > 0 ? sum / cnt : 1f):0.00} (crown {yB:0.0}-{yT:0.0} m)");
        }

        /// <summary>THE LEAF CARDS MEASURED (for a Codex v2 request): per tree at LOD0, the leaf triangles
        /// and cards (two triangles a card), the mean card area, the leaf area against the crown's
        /// silhouette, the atlas and its texels per metre of leaf, the distinct card mappings (UV
        /// rectangles), the vertex data: NatureManufacture's beeches and Meadow poplars beside the owner's forms.</summary>
        static void CT4Measure(List<MeshForest.Variant> list, List<string> names, List<byte> groups)
        {
            var sb = new System.Text.StringBuilder("LEAF CARD MEASURE (LOD0; texels/m = sqrt(atlas texels x UV area / leaf area)):");
            for (int i = 0; i < list.Count; i++)
            {
                string n = names[i];
                bool nm = groups[i] == 0 && ((n.StartsWith("beech_tree_0") && !n.Contains("snag")) || n.EndsWith("_Poplar") || n.EndsWith("_skan"));
                bool ct = groups[i] == CityTreeKit.G4Charlotte && n.StartsWith("CT4_") && !n.EndsWith("_red") && !n.EndsWith("_white") && !n.EndsWith("_purple");
                if (!nm && !ct) continue;
                var v = list[i]; var m = v.lods[0];
                if (m == null) continue;
                var uv = ReadUV(m);
                int tris = 0; double area = 0, uvA = 0; float rMax = 0f;
                var rects = new HashSet<long>();
                Texture tex = null;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    if (v.leafSub == null || s >= v.leafSub.Length || !v.leafSub[s]) continue;
                    if (tex == null) { var lm = v.leafDress != null && v.leafDress.Length > 2 && v.leafDress[2] != null ? v.leafDress[2] : v.mats[Mathf.Min(s, v.mats.Length - 1)]; tex = lm != null ? lm.mainTexture : null; }
                    if (!TreeKit.Read(m, s, out var vv, out var tt)) continue;
                    for (int t = 0; t + 2 < tt.Length; t += 3)
                    {
                        Vector3 a = v.pre.MultiplyPoint3x4(vv[tt[t]]), b = v.pre.MultiplyPoint3x4(vv[tt[t + 1]]), c = v.pre.MultiplyPoint3x4(vv[tt[t + 2]]);
                        area += Vector3.Cross(b - a, c - a).magnitude * 0.5;
                        rMax = Mathf.Max(rMax, new Vector2(a.x, a.z).magnitude);
                        tris++;
                        if (uv == null) continue;
                        Vector2 ua = uv[tt[t]], ub = uv[tt[t + 1]], uc = uv[tt[t + 2]];
                        uvA += Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * 0.5;
                        int x0 = Mathf.RoundToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)) * 64f), y0 = Mathf.RoundToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y)) * 64f);
                        rects.Add(((long)x0 << 20) ^ (long)y0);
                    }
                }
                if (tris == 0) continue;
                int W = tex != null ? tex.width : 0, H = tex != null ? tex.height : 0;
                double tpm = area > 0 && uvA > 0 ? System.Math.Sqrt(W * (double)H * uvA / area) : 0;
                double sil = System.Math.PI * rMax * rMax;
                string vd = (m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color) ? "colour " : "") + (m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1) ? "uv1 " : "") +
                            (m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2) ? "uv2 " : "") + (m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) ? "tangent" : "");
                sb.Append($"\n  {(nm ? "NM " : "CT ")}{n,-30} {v.height,5:0.0} m x {2f * rMax,4:0.0} m: leaf tris {tris,6}, cards ~{tris / 2,5}, mean card {area / (tris / 2.0),5:0.00} m2, leaf area {area,6:0} m2 ({area / System.Math.Max(1, sil),4:0.0} x the plan circle), atlas {W}x{H}, {tpm,4:0} texels/m, card mappings {rects.Count,4}, vertex data [{vd.Trim()}]");
            }
            Log(sb.ToString());
        }

        /// <summary>The inverse of a leaf material's vivid step (saturation LeafSaturation, then
        /// x LeafBrightness): what is not a leaf comes out of the card as it was drawn.</summary>
        static Color CT4Unvivid(Color c, Vector2 vivid)
        {
            float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            var o = new Color(lum, lum, lum) + (c - new Color(lum, lum, lum)) * (1f / vivid.x);
            o *= 1f / vivid.y;
            return new Color(Mathf.Max(0f, o.r), Mathf.Max(0f, o.g), Mathf.Max(0f, o.b), 1f);
        }
    }
}
