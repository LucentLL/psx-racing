using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CHARLOTTE'S MESH TREE KIT (2026-11-11): Resources/CityTreeKit.asset for
    /// CityMeshTrees, from the owner's packs (gitignored store files,
    /// tools/store-assets.ps1). PC content only: written by a PC scene pass
    /// (PSX_PC_CONTENT=1), deleted by any other, so the web and phone builds
    /// carry none of it and keep their card trees.
    ///
    ///   Broad      the Forest pack's six canopy beeches (the mountain kit's
    ///              materials, LODs and impostors: one lighting rule)
    ///   Small      its four small beeches (young understory trees)
    ///   Snag       two beeches that never leaf (the dead snags)
    ///   Poplar     six of Meadow's broad poplars: their parts per LOD merged
    ///              into one mesh of [trunk, leaves], the pack's own impostor
    ///              sheet (four views of that tree) as the far level
    ///   PineStandIn two of them in a dark evergreen leaf all year (no owned pine)
    ///   Shrub      Meadow's four grey willows (crape myrtle's multi-stem stand-in)
    ///   Oak        Objective Environment's Big Oak (its billboard keyed to alpha
    ///              as its impostor)
    ///   Columnar   Big Poplar (as the mountains use it)
    /// Each species' CROWN: the radius of its LOD0 about the trunk in twelve
    /// height bands, and where its leaves begin - what CityMeshTrees fits.
    ///
    ///   -executeMethod PSXRacing.EditorTools.PSXRacingBuilder.BuildCityTreeKit  (PSX_PC_CONTENT=1)
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string CTPoplarDir = "Assets/StoreAssets/Meadow/Poplar";
        const string CTWillowDir = "Assets/StoreAssets/Meadow/Willow";
        const string CTOakDir = "Assets/StoreAssets/OE/Oak";
        const string CityTreeKitPath = Root + "/Resources/" + CityTreeKit.ResourcePath + ".asset";

        // poplar FBX, its trunk texture
        static readonly (string fbx, string trunk, string sheet)[] CTPoplars =
        {
            ("01_Poplar.FBX", "T_Poplar_bark_06_BC", "T_Poplar_01_Cross_A_T"),
            ("02_Poplar.FBX", "T_Poplar_bark_06_BC", "T_Poplar_02_Cross_A_T"),
            ("03_Poplar_skan.FBX", "T_Poplar_trunk_03_BC", "T_Poplar_03_Cross_A_T"),
            ("04_Poplar_skan.FBX", "T_Poplar_trunk_04_BC", "T_Poplar_04_Cross_A_T"),
            ("05_Poplar_skan.FBX", "T_Poplar_trunk_05_BC", "T_Poplar_05_Cross_A_T"),
            ("07_Poplar_skan.FBX", "T_Poplar_trunk_07_BC", "T_Poplar_07_Cross_A_T"),
        };

        /// <summary>Batch entry: the kit now (PSX_PC_CONTENT=1), exit 0 when written.</summary>
        public static void BuildCityTreeKit()
        {
            int code = 0;
            try
            {
                var kit = EnsureCityTreeKit(true);
                if (kit == null) { Debug.LogError("[CityTreeKit] not written (PSX_PC_CONTENT=1? store files?)"); code = 1; }
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static bool cityTreeKitDone;

        internal static CityTreeKit EnsureCityTreeKit(bool force = false)
        {
            if (!PCContent || AssetDatabase.LoadAssetAtPath<GameObject>(NMTreeDir + "/" + NMTrees[0].tree + ".FBX") == null)
            {
                if (AssetDatabase.LoadAssetAtPath<CityTreeKit>(CityTreeKitPath) != null)
                {
                    AssetDatabase.DeleteAsset(CityTreeKitPath);
                    Log("City tree kit: removed (not a PC content pass, or no store files) - the city draws its card trees");
                }
                return null;
            }
            if (cityTreeKitDone && !force) return AssetDatabase.LoadAssetAtPath<CityTreeKit>(CityTreeKitPath);
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            EnsureFolders();
            if (!AssetDatabase.IsValidFolder(Root + "/Resources")) AssetDatabase.CreateFolder(Root, "Resources");

            var list = new List<MeshForest.Variant>();
            var names = new List<string>();
            var roles = new List<CityTreeKit.Role>();
            void Add(MeshForest.Variant v, string n, CityTreeKit.Role r) { if (v == null) return; list.Add(v); names.Add(n); roles.Add(r); }

            // ---- the Forest beeches, as the mountain kit makes them ----
            var green = NMLitMat("NM_beech_leaves", NMTreeDir, "T_beech_atlas_BC", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, Color.white);
            var fall = new[]
            {
                NMLitMat("NM_beech_leaves_autumn", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, Color.white),
                NMLitMat("NM_beech_leaves_autumn_red", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.0f, 0.58f, 0.48f)),
                NMLitMat("NM_beech_leaves_autumn_gold", NMTreeDir, "T_beech_atlas_BC_autumn", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.0f, 1.12f, 0.62f)),
                NMLitMat("NM_beech_leaves_late", NMTreeDir, "T_beech_atlas_BC", "T_beech_atlas_N", "T_beech_atlas_Maskmap", true, new Color(1.08f, 0.98f, 0.55f)),
            };
            green.SetColor("_LeafTulip", GreenSpread);
            for (int f = 0; f < fall.Length; f++) fall[f].SetColor("_LeafTulip", f == 3 ? GreenSpread : FallTulip);
            Material bare = null;
            if (FindTex(NMDir, "Gen/T_beech_atlas_bare") != null)
            {
                bare = NMLitMat("NM_beech_twigs", NMDir, "Gen/T_beech_atlas_bare", null, null, true, new Color(0.86f, 0.82f, 0.78f));
                bare.SetVector("_LeafVivid", new Vector4(1f, 1f, 0f, 0f));
            }
            int fi = 0;
            MeshForest.Variant snagFrom1 = null, snagFrom2 = null;
            foreach (var (tree, bark) in NMTrees)
            {
                var v = LoadVariant(NMTreeDir, tree, NMLitMat("NM_" + bark, NMTreeDir, bark + "_BC", bark + "_N", bark + "_MaskMap", false, Color.white));
                if (v == null) continue;
                int t = fi++ % fall.Length;
                v.leafDress = new[] { bare, green, green, fall[t], bare };
                AddImpostor(v, tree, FallTints[t], t == 3);
                Add(v, tree, CityTreeKit.Role.Broad);
                if (snagFrom1 == null) snagFrom1 = v; else if (snagFrom2 == null) snagFrom2 = v;
            }
            foreach (var src in new[] { snagFrom1, snagFrom2 })
            {
                if (src == null) continue;
                var s = new MeshForest.Variant
                {
                    lods = (Mesh[])src.lods.Clone(), mats = (Material[])src.mats.Clone(), leafSub = (bool[])src.leafSub.Clone(),
                    height = src.height, pre = src.pre, impostorPre = src.impostorPre, leafDress = new Material[5],
                    impostorDress = src.impostorDress != null ? new[] { src.impostorDress[0], src.impostorDress[0], src.impostorDress[0], src.impostorDress[0], src.impostorDress[0] } : null,
                };
                Add(s, (src.lods[0] != null ? src.lods[0].name : "beech") + "_snag", CityTreeKit.Role.Snag);
            }
            var barkSmall = NMLitMat("NM_T_beech_bark_02", NMTreeDir, "T_beech_bark_02_BC", "T_beech_bark_02_N", "T_beech_bark_02_MaskMap", false, Color.white);
            foreach (var name in NMSmallBeech)
            {
                var v = LoadVariant(NMUnderDir, name, barkSmall);
                if (v == null) continue;
                v.leafDress = new[] { bare, green, green, fall[fi++ % 3], bare };
                v.thin = 0.45f;
                Add(v, name, CityTreeKit.Role.Small);
            }

            // ---- Big Poplar (columnar), as the mountain kit makes it ----
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OEPoplarDir + "/PoplarTree001.fbx") != null)
            {
                var pBark = NMLitMat("OE_poplar_bark", OEPoplarDir, "Poplar_Bark001", "Poplar_Bark001_N", null, false, Color.white);
                var pGain = new Color(PoplarLeafGain, PoplarLeafGain, PoplarLeafGain);
                var pLeaf = NMLitMat("OE_poplar_leaves", OEPoplarDir, "BranchesPoplar001", "BranchesPoplar001_N", null, true, pGain);
                var pFall = NMLitMat("OE_poplar_leaves_fall", OEPoplarDir, "BranchesPoplar001", "BranchesPoplar001_N", null, true, pGain);
                pLeaf.SetColor("_LeafTulip", GreenSpread);
                pFall.SetColor("_LeafTulip", GreenSpread);
                pFall.SetColor("_LeafTarget", new Color(1.0f, 0.78f, 0.12f));
                var lv = pFall.GetVector("_LeafVivid"); lv.w = 0.85f; pFall.SetVector("_LeafVivid", lv);
                var pv = LoadVariant(OEPoplarDir, "PoplarTree001", pBark, ".fbx", skip: 1);
                if (pv != null)
                {
                    pv.leafDress = new[] { null, pLeaf, pLeaf, pFall, null };
                    AddBillboardImpostor(pv, OEPoplarDir, "PoplarTree001_Billboard", 8.7f / 23.2f);
                    Add(pv, "PoplarTree001", CityTreeKit.Role.Columnar);
                }
            }

            // ---- Meadow's broad poplars ----
            string pl = FindTex(CTPoplarDir, "T_Poplar_leaves_BC");
            if (pl != null)
            {
                string pn = FindTex(CTPoplarDir, "T_Poplar_leaves_N");
                var leaf = CTMat("CT_poplar_leaves", pl, pn, true, Color.white);
                var leafFall = CTMat("CT_poplar_leaves_fall", pl, pn, true, Color.white);
                leaf.SetColor("_LeafTulip", GreenSpread);
                leafFall.SetColor("_LeafTulip", GreenSpread);
                leafFall.SetColor("_LeafTarget", new Color(1.0f, 0.78f, 0.12f));   // the tulip tree's clear yellow
                var lv = leafFall.GetVector("_LeafVivid"); lv.w = 0.85f; leafFall.SetVector("_LeafVivid", lv);
                // the loblolly stand-in: in leaf all year, a dark blue-green
                var pine = CTMat("CT_pine_standin_leaves", pl, pn, true, new Color(0.50f, 0.62f, 0.50f));
                var pineLv = pine.GetVector("_LeafVivid"); pineLv.x = 0.85f; pine.SetVector("_LeafVivid", pineLv);
                pine.SetColor("_LeafTulip", GreenSpread);
                int k = 0;
                foreach (var (fbx, trunk, sheet) in CTPoplars)
                {
                    string tp = FindTex(CTPoplarDir, trunk);
                    if (tp == null || AssetDatabase.LoadAssetAtPath<GameObject>(CTPoplarDir + "/" + fbx) == null) { Log("WARN: city trees: missing " + fbx + " / " + trunk); continue; }
                    var barkM = CTMat("CT_" + trunk, tp, null, false, Color.white);
                    var v = LoadMergedVariant(CTPoplarDir, fbx, barkM);
                    if (v == null) continue;
                    v.leafDress = new[] { null, leaf, leaf, leafFall, null };
                    string sp = FindTex(CTPoplarDir, sheet);
                    if (sp != null)
                    {
                        var ig = CTMat("CT_imp_" + sheet, sp, null, true, new Color(0.84f, 0.88f, 0.80f));
                        var ifall = CTMat("CT_imp_" + sheet + "_fall", sp, null, true, new Color(0.9f, 0.9f, 0.9f));
                        ifall.SetColor("_LeafTarget", new Color(1.0f, 0.78f, 0.12f));
                        var ilv = ifall.GetVector("_LeafVivid"); ilv.w = 0.85f; ifall.SetVector("_LeafVivid", ilv);
                        var ibare = CTMat("CT_imp_" + sheet + "_bare", sp, null, true, new Color(0.40f, 0.36f, 0.32f));
                        var ibv = ibare.GetVector("_LeafVivid"); ibv.x = 0.15f; ibare.SetVector("_LeafVivid", ibv);
                        ig.SetColor("_LeafTulip", GreenSpread); ifall.SetColor("_LeafTulip", GreenSpread);
                        AddStripImpostor(v, sp, 4, "CTImpostor_" + fbx.Replace(".FBX", ""), new[] { ibare, ig, ig, ifall, ibare });
                    }
                    string stem = fbx.Replace(".FBX", "");
                    Add(v, stem, CityTreeKit.Role.Poplar);
                    // the pine stand-ins: the two fullest crowns, a second species of the same meshes
                    if (stem == "03_Poplar_skan" || stem == "05_Poplar_skan")
                    {
                        var s = new MeshForest.Variant
                        {
                            lods = (Mesh[])v.lods.Clone(), mats = (Material[])v.mats.Clone(), leafSub = (bool[])v.leafSub.Clone(),
                            height = v.height, pre = v.pre, impostorPre = v.impostorPre,
                            leafDress = new[] { pine, pine, pine, pine, pine },
                        };
                        if (sp != null)
                        {
                            var ip = CTMat("CT_imp_" + sheet + "_pine", sp, null, true, new Color(0.42f, 0.52f, 0.42f));
                            ip.SetColor("_LeafTulip", GreenSpread);
                            s.impostorDress = new[] { ip, ip, ip, ip, ip };
                        }
                        Add(s, stem + "_pine", CityTreeKit.Role.PineStandIn);
                    }
                    k++;
                }
            }
            else Log("City trees: no Meadow poplars in " + CTPoplarDir + " (tools/store-assets.ps1)");

            // ---- Meadow's grey willows: the crape myrtles ----
            string wb = FindTex(CTWillowDir, "T_Grey_willow_BC");
            if (wb != null)
            {
                string wn = FindTex(CTWillowDir, "T_Grey_willow_N");
                var w = CTMat("CT_grey_willow", wb, wn, true, new Color(0.92f, 0.96f, 0.88f));
                var wFall = CTMat("CT_grey_willow_fall", wb, wn, true, Color.white);
                wFall.SetColor("_LeafTarget", new Color(0.86f, 0.26f, 0.10f));   // a crape myrtle's red-orange
                var wlv = wFall.GetVector("_LeafVivid"); wlv.w = 0.8f; wFall.SetVector("_LeafVivid", wlv);
                var wBare = CTMat("CT_grey_willow_bare", wb, null, true, new Color(0.42f, 0.36f, 0.30f));
                var wbv = wBare.GetVector("_LeafVivid"); wbv.x = 0.15f; wBare.SetVector("_LeafVivid", wbv);
                foreach (var m in new[] { w, wFall, wBare }) m.SetColor("_LeafTulip", GreenSpread);
                for (int i = 1; i <= 4; i++)
                {
                    var v = LoadVariant(CTWillowDir, "grey_willow_0" + i, w);
                    if (v == null) continue;
                    for (int s = 0; s < v.leafSub.Length; s++) v.leafSub[s] = true;
                    v.leafDress = new[] { wBare, w, w, wFall, wBare };
                    v.thin = 0.45f;
                    Add(v, "grey_willow_0" + i, CityTreeKit.Role.Shrub);
                }
            }

            // ---- Big Oak ----
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CTOakDir + "/OakBigTree01.fbx") != null)
            {
                var oBark = CTMat("CT_oak_bark", CTOakDir + "/barkOak001.png", CTOakDir + "/barkOak001_N.png", false, Color.white);
                string ol = CTOakDir + "/BranchesOak001.tif";
                var oLeaf = CTMat("CT_oak_leaves", ol, CTOakDir + "/BranchesOak001_N.png", true, Color.white);
                var oFall = CTMat("CT_oak_leaves_fall", ol, CTOakDir + "/BranchesOak001_N.png", true, Color.white);
                oFall.SetColor("_LeafTarget", new Color(0.78f, 0.42f, 0.12f));   // a willow oak's late yellow-brown
                var olv = oFall.GetVector("_LeafVivid"); olv.w = 0.75f; oFall.SetVector("_LeafVivid", olv);
                oLeaf.SetColor("_LeafTulip", GreenSpread); oFall.SetColor("_LeafTulip", GreenSpread);
                var probe = LoadVariant(CTOakDir, "OakBigTree01", oBark, ".fbx");
                int skip = probe != null && Tris(probe.lods[0]) > 30000 ? 1 : 0;
                var v = skip == 0 ? probe : LoadVariant(CTOakDir, "OakBigTree01", oBark, ".fbx", skip: 1);
                if (v != null)
                {
                    v.leafDress = new[] { null, oLeaf, oLeaf, oFall, null };
                    string keyed = KeyedBillboard(CTOakDir + "/BigOakBillboard01.tif", CTOakDir + "/Gen/BigOak_billboard_keyed.png");
                    if (keyed != null)
                    {
                        var ig = CTMat("CT_imp_oak", keyed, null, true, new Color(0.9f, 0.94f, 0.86f));
                        var ifall = CTMat("CT_imp_oak_fall", keyed, null, true, Color.white);
                        ifall.SetColor("_LeafTarget", new Color(0.78f, 0.42f, 0.12f));
                        var ilv = ifall.GetVector("_LeafVivid"); ilv.w = 0.75f; ifall.SetVector("_LeafVivid", ilv);
                        var ibare = CTMat("CT_imp_oak_bare", keyed, null, true, new Color(0.40f, 0.36f, 0.32f));
                        var ibv = ibare.GetVector("_LeafVivid"); ibv.x = 0.15f; ibare.SetVector("_LeafVivid", ibv);
                        ig.SetColor("_LeafTulip", GreenSpread); ifall.SetColor("_LeafTulip", GreenSpread);
                        AddStripImpostor(v, keyed, 3, "CTImpostor_BigOak", new[] { ibare, ig, ig, ifall, ibare });
                    }
                    Log($"City trees: Big Oak {v.height:0.0} m, LOD tris {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])} (FBX LOD{skip}..{skip + 2})");
                    Add(v, "OakBigTree01", CityTreeKit.Role.Oak);
                }
            }

            if (list.Count == 0) return null;
            // ---- ROUND 2: the LOD ladder on every tree, and the street trees ----
            CTRound2(list, names, roles);
            // ---- ROUND 3: Charlotte's forms (round street trees, avenue oaks, pines, crape myrtles) ----
            var groups3 = new List<byte>();
            CTRound3(list, names, roles, groups3);
            // ---- every level standing on the ground (the packs' coarser levels have no roots: PSX_CT5_FOOT=0 = as they were) ----
            CTFeet(list, names);
            // ---- TREE MATCH: every tree's far card made from its own LOW mesh, in each dress (PSX_TREE_MATCH=0: none) ----
            TreeMatchKit(list, names, "city", null, true);
            // ---- the crowns ----
            var crown = new float[list.Count * CityTreeKit.Bands];
            var cbase = new float[list.Count];
            for (int v = 0; v < list.Count; v++) CrownProfile(list[v], crown, v, out cbase[v]);

            var kit = AssetDatabase.LoadAssetAtPath<CityTreeKit>(CityTreeKitPath);
            bool fresh = kit == null;
            if (fresh) kit = ScriptableObject.CreateInstance<CityTreeKit>();
            kit.variants = list.ToArray();
            kit.names = names.ToArray();
            kit.roles = roles.ToArray();
            kit.crown = crown;
            kit.crownBase = cbase;
            kit.group3 = groups3.ToArray();
            // TREE MATCH: the cards' atlas (tile cards, CityFar's far trees) cut from the kit's own trees, per dress
            kit.cardAtlas = TMCityCardAtlas();
            kit.lodTris = new int[list.Count * 3];
            for (int v = 0; v < list.Count; v++)
                for (int l = 0; l < 3; l++) kit.lodTris[v * 3 + l] = list[v].lods[l] != null ? (int)Tris(list[v].lods[l]) : 0;
            kit.cull = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/PSXRacing/Shaders/PSXForestCull.compute");
            // the stages' MEDIUM distances (FOREST DETAIL and PopFix scale them alike);
            // impostors out past the baked city's ring (CityRingBaked: 9 x 9 tiles)
            kit.lod0To = 55f; kit.lod1To = 140f; kit.meshRange = 240f; kit.farRange = 1200f;   // (round 2: nearest level 65 -> 55 m = 102 m on MEDIUM after PopFix; cost v pop measured in ctrees2_log)
            if (fresh) AssetDatabase.CreateAsset(kit, CityTreeKitPath);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            CityTreeKit.Reset();
            cityTreeKitDone = true;
            var sb = new System.Text.StringBuilder($"City tree kit: {list.Count} species ->");
            for (int v = 0; v < list.Count; v++)
                sb.Append($"\n  {v} {names[v]} [{roles[v]}{(groups3[v] != 0 ? " g3:" + groups3[v] : "")}] {list[v].height:0.0} m, tris {Tris(list[v].lods[0])}/{Tris(list[v].lods[1])}/{Tris(list[v].lods[2])}, impostor {(list[v].lods.Length > 3 && list[v].lods[3] != null ? "yes" : "no")}, crown from {cbase[v]:0.00} of the height, widest r {MaxOf(crown, v):0.0} m");
            Log(sb.ToString());
            return kit;
        }

        static float MaxOf(float[] crown, int v)
        {
            float m = 0f;
            for (int b = 0; b < CityTreeKit.Bands; b++) m = Mathf.Max(m, crown[v * CityTreeKit.Bands + b]);
            return m;
        }

        /// <summary>A city tree material: NMLitMat's rules from a texture path
        /// (the packs' .tif and odd names included).</summary>
        static Material CTMat(string name, string bcPath, string normalPath, bool leaves, Color tint)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(bcPath) == null) Log("WARN: city trees: texture missing " + bcPath);
            else if (leaves) PreserveCoverage(bcPath, LeafCutoff);
            var mat = MakeMat(name, bcPath, cutoff: leaves ? LeafCutoff : 0f, tint: tint, affine: 0f, twoSided: leaves);
            mat.enableInstancing = true;
            mat.SetVector("_LeafVivid", leaves ? new Vector4(LeafSaturation, LeafBrightness, 1f, 0f) : new Vector4(1f, 1f, 0f, 0f));
            SetHDNormal(mat, string.IsNullOrEmpty(normalPath) || AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) == null ? null : normalPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A Meadow poplar: its trunk, roots and leaves are separate
        /// nodes per LOD; merged here into one mesh a level of [bark, leaves],
        /// with the nodes' transforms, saved under Generated. The trunk's foot
        /// at the origin, as LoadVariant stands its trees.</summary>
        static MeshForest.Variant LoadMergedVariant(string dir, string file, Material bark)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + file);
            if (asset == null) { Log("WARN: city trees: missing " + file); return null; }
            var inst = (GameObject)Object.Instantiate(asset);
            try
            {
                var root = inst.transform;
                // ROUND 2: all five of the pack's levels read; the ladder takes the
                // first inside each level's triangle budget (CTBudget)
                const int NL = 5;
                var barkCi = new List<CombineInstance>[NL];
                var leafCi = new List<CombineInstance>[NL];
                for (int l = 0; l < NL; l++) { barkCi[l] = new List<CombineInstance>(); leafCi[l] = new List<CombineInstance>(); }
                bool ladder = System.Environment.GetEnvironmentVariable("PSX_CT_R2KIT") != "0";
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    string n = mf.name;
                    int li = n.LastIndexOf("_LOD", System.StringComparison.OrdinalIgnoreCase);
                    if (li < 0 || !int.TryParse(n.Substring(li + 4), out int lod) || lod >= (ladder ? NL : 3) || mf.sharedMesh == null) continue;
                    bool leaf = n.IndexOf("leaves", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                        (leaf ? leafCi : barkCi)[lod].Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = m });
                }
                string stem = file.Substring(0, file.LastIndexOf('.'));
                var v = new MeshForest.Variant();
                var all = new Mesh[NL];
                for (int l = 0; l < NL; l++)
                {
                    if (barkCi[l].Count == 0 || leafCi[l].Count == 0) { all[l] = null; continue; }
                    // merged by hand (CombineMeshes cannot read the packs' Read/Write-off meshes)
                    var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>();
                    var idx = new[] { new List<int>(), new List<int>() };
                    for (int part = 0; part < 2; part++)
                        foreach (var ci in part == 0 ? barkCi[l] : leafCi[l])
                        {
                            if (!TreeKit.Read(ci.mesh, ci.subMeshIndex, out var vv, out var tt)) continue;
                            var nrm = ReadNormals(ci.mesh); var uv = ReadUV(ci.mesh);
                            var nm = ci.transform.inverse.transpose;
                            var remap = new Dictionary<int, int>();
                            foreach (int ti in tt)
                            {
                                if (!remap.TryGetValue(ti, out int ni))
                                {
                                    ni = pv.Count; remap[ti] = ni;
                                    pv.Add(ci.transform.MultiplyPoint3x4(vv[ti]));
                                    pn.Add(nrm != null && ti < nrm.Length ? nm.MultiplyVector(nrm[ti]).normalized : Vector3.up);
                                    pu.Add(uv != null && ti < uv.Length ? uv[ti] : Vector2.zero);
                                }
                                idx[part].Add(ni);
                            }
                        }
                    var c = new Mesh { name = "CT_" + stem + "_LOD" + l };
                    c.indexFormat = pv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                    c.SetVertices(pv); c.SetNormals(pn); c.SetUVs(0, pu);
                    c.subMeshCount = 2;
                    c.SetTriangles(idx[0], 0, false); c.SetTriangles(idx[1], 1, false);
                    c.RecalculateBounds();
                    all[l] = c;
                }
                // the ladder: level k = the first pack level (at or after the one before) inside its budget
                {
                    int from = 0;
                    var used = new HashSet<int>();
                    var tl = new System.Text.StringBuilder();
                    for (int l = 0; l < NL; l++) tl.Append(all[l] != null ? Tris(all[l]).ToString() : "-").Append(l < NL - 1 ? "/" : "");
                    for (int k = 0; k < 3; k++)
                    {
                        int pick = -1, last = -1;
                        for (int l = from; l < NL; l++)
                        {
                            if (all[l] == null) continue;
                            last = l;
                            if (!ladder && l == k) { pick = l; break; }
                            if (ladder && Tris(all[l]) <= CTBudget[k]) { pick = l; break; }
                        }
                        if (pick < 0) pick = last;
                        // (no pack level left past the one before: that one again; CTRound2 thins it to the budget)
                        if (pick < 0) { v.lods[k] = k > 0 ? v.lods[k - 1] : null; continue; }
                        if (!used.Contains(pick)) { all[pick] = WorldKit.SaveMesh(all[pick], all[pick].name); used.Add(pick); }
                        v.lods[k] = all[pick];
                        from = ladder ? pick + 1 : pick;   // each level a coarser pack level than the one before, while there is one
                    }
                    Log($"City trees: {stem} pack levels {tl} tris -> ladder {v.lods[0]?.name}/{v.lods[1]?.name}/{v.lods[2]?.name}");
                }
                if (v.lods[0] == null) { Log("WARN: city trees: " + file + " has no LOD0 trunk + leaves"); return null; }
                v.mats = new[] { bark, bark };
                v.leafSub = new[] { false, true };
                // the trunk's foot (the bark's lowest band, its 2nd percentile) at the origin
                var pre = Matrix4x4.identity;
                Vector3 p0 = v.lods[0].bounds.center;
                float baseY = v.lods[0].bounds.min.y, maxY = v.lods[0].bounds.max.y;
                if (TreeKit.Read(v.lods[0], 0, out var bv, out var bt))
                {
                    var ys = new List<float>();
                    foreach (int ti in bt) ys.Add(bv[ti].y);
                    ys.Sort();
                    if (ys.Count == 0) ys.Add(baseY);
                    float footY = ys[Mathf.Min(ys.Count - 1, ys.Count / 50)];
                    double sx = 0, sz = 0; int cnt = 0;
                    foreach (int ti in bt) { var q = bv[ti]; if (q.y > footY + 0.6f) continue; sx += q.x; sz += q.z; cnt++; }
                    if (cnt > 0) { p0.x = (float)(sx / cnt); p0.z = (float)(sz / cnt); }
                    baseY = footY;
                }
                v.pre = Matrix4x4.Translate(new Vector3(-p0.x, -baseY, -p0.z)) * pre;
                v.height = Mathf.Max(0.3f, maxY - baseY);
                Log($"City trees: {stem} {v.height:0.0} m, merged LOD tris {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])}");
                return v;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static Vector3[] ReadNormals(Mesh m)
        {
            if (m.isReadable) return m.normals;
            using (var d = Mesh.AcquireReadOnlyMeshData(m))
            {
                var md = d[0];
                if (!md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) return null;
                var a = new Unity.Collections.NativeArray<Vector3>(md.vertexCount, Unity.Collections.Allocator.Temp);
                md.GetNormals(a); var r = a.ToArray(); a.Dispose(); return r;
            }
        }
        static Vector2[] ReadUV(Mesh m)
        {
            if (m.isReadable) return m.uv;
            using (var d = Mesh.AcquireReadOnlyMeshData(m))
            {
                var md = d[0];
                if (!md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) return null;
                var a = new Unity.Collections.NativeArray<Vector2>(md.vertexCount, Unity.Collections.Allocator.Temp);
                md.GetUVs(0, a); var r = a.ToArray(); a.Dispose(); return r;
            }
        }

        static readonly Dictionary<Mesh, Mesh> readableCopies = new Dictionary<Mesh, Mesh>();

        /// <summary>A readable copy of a pack mesh (the packs import with
        /// Read/Write off, and CombineMeshes skips such a mesh): positions,
        /// normals, UV0, colours and every submesh, through the read-only
        /// MeshData the editor may see.</summary>
        static Mesh ReadableCopy(Mesh src)
        {
            if (src == null || src.isReadable) return src;
            if (readableCopies.TryGetValue(src, out var have) && have != null) return have;
            var m = new Mesh { name = src.name + "_rw", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            using (var data = Mesh.AcquireReadOnlyMeshData(src))
            {
                var md = data[0];
                int n = md.vertexCount;
                var v = new Unity.Collections.NativeArray<Vector3>(n, Unity.Collections.Allocator.Temp);
                md.GetVertices(v); m.SetVertices(v); v.Dispose();
                if (md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal))
                { var nn = new Unity.Collections.NativeArray<Vector3>(n, Unity.Collections.Allocator.Temp); md.GetNormals(nn); m.SetNormals(nn); nn.Dispose(); }
                if (md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
                { var uv = new Unity.Collections.NativeArray<Vector2>(n, Unity.Collections.Allocator.Temp); md.GetUVs(0, uv); m.SetUVs(0, uv); uv.Dispose(); }
                if (md.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                { var c = new Unity.Collections.NativeArray<Color>(n, Unity.Collections.Allocator.Temp); md.GetColors(c); m.SetColors(c); c.Dispose(); }
                m.subMeshCount = md.subMeshCount;
                for (int s = 0; s < md.subMeshCount; s++)
                {
                    var sm = md.GetSubMesh(s);
                    var idx = new Unity.Collections.NativeArray<int>(sm.indexCount, Unity.Collections.Allocator.Temp);
                    md.GetIndices(idx, s);
                    m.SetIndices(idx, sm.topology, s);
                    idx.Dispose();
                }
            }
            m.RecalculateBounds();
            readableCopies[src] = m;
            return m;
        }

        /// <summary>The crown as a volume: the farthest vertex of LOD0 from the
        /// trunk's axis in each of CityTreeKit.Bands height bands (m, scale 1),
        /// and where the leaf submesh begins (share of the height).</summary>
        static void CrownProfile(MeshForest.Variant v, float[] crown, int vi, out float baseFrac)
        {
            baseFrac = 0f;
            var m = v.lods[0];
            float h = Mathf.Max(0.3f, v.height);
            float leafLow = float.MaxValue;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                bool leaf = s < v.leafSub.Length && v.leafSub[s];
                foreach (int ti in tris)
                {
                    var q = v.pre.MultiplyPoint3x4(verts[ti]);
                    int b = Mathf.Clamp(Mathf.FloorToInt(q.y / h * CityTreeKit.Bands), 0, CityTreeKit.Bands - 1);
                    float r = Mathf.Sqrt(q.x * q.x + q.z * q.z);
                    int at = vi * CityTreeKit.Bands + b;
                    if (r > crown[at]) crown[at] = r;
                    if (leaf && q.y < leafLow) leafLow = q.y;
                }
            }
            baseFrac = leafLow < float.MaxValue ? Mathf.Clamp01(leafLow / h) : 0f;
        }

        /// <summary>LOD3 from a strip of views (Meadow's 4x1 cross sheets, Big
        /// Oak's 3-cell billboard): a cross of the first two cells, each cut to
        /// where its tree actually is in the cell (alpha bounds read off the
        /// sheet), the tree's foot on the ground and its top at its height.</summary>
        static void AddStripImpostor(MeshForest.Variant v, string sheetPath, int cells, string meshName, Material[] dress)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            if (tex == null) return;
            int W = 256 * cells, Hh = 256;
            var px = ReadTex(tex, W, Hh);
            float h = v.height;
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nm = new List<Vector3>();
            for (int q = 0; q < 2; q++)
            {
                int x0 = q * 256, x1 = x0 + 256, minX = x1, maxX = x0, minY = Hh, maxY = 0;
                for (int y = 0; y < Hh; y++)
                    for (int x = x0; x < x1; x++)
                        if (px[y * W + x].a > 100) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
                if (maxX <= minX || maxY <= minY) { minX = x0; maxX = x1 - 1; minY = 0; maxY = Hh - 1; }
                float u0 = minX / (float)W, u1 = (maxX + 1) / (float)W, v0 = minY / (float)Hh, v1 = (maxY + 1) / (float)Hh;
                // the sheet's pixel aspect: a cell is (sheet width / cells) x sheet height
                float cellAspect = tex.width / (float)cells / tex.height;
                float w = h * ((maxX + 1 - minX) / 256f * cellAspect) / ((maxY + 1 - minY) / 256f);
                var rot = Quaternion.Euler(0f, q * 90f, 0f);
                int i = vs.Count;
                vs.Add(rot * new Vector3(-w * 0.5f, 0f, 0f)); vs.Add(rot * new Vector3(-w * 0.5f, h, 0f));
                vs.Add(rot * new Vector3(w * 0.5f, h, 0f)); vs.Add(rot * new Vector3(w * 0.5f, 0f, 0f));
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u1, v0));
                var n = rot * Vector3.back;
                for (int k = 0; k < 4; k++) nm.Add((n + Vector3.up * 0.6f).normalized);
                tr.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            ShareMeshOrigin(v, vs);
            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetNormals(nm); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            v.lods = new[] { v.lods[0], v.lods[1], v.lods[2], WorldKit.SaveMesh(mesh, meshName) };
            v.impostorDress = dress;
            foreach (var im in dress) FarGrow(im);
        }

        /// <summary>A texture's pixels at w x h, through the GPU (the importer
        /// need not be readable; .tif included).</summary>
        static Color32[] ReadTex(Texture tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        /// <summary>Big Oak's billboard is drawn on black without an alpha
        /// channel: keyed here (alpha from the brightest channel) into a PNG
        /// beside the store files, imported as an alpha-tested sheet.</summary>
        static string KeyedBillboard(string src, string dst)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(dst) != null) return dst;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
            if (tex == null) return null;
            int w = 1596, h = 512;
            var px = ReadTex(tex, w, h);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                int mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                float a = Mathf.InverseLerp(8f, 26f, mx);
                px[i] = new Color32(c.r, c.g, c.b, (byte)(a * 255f));
            }
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
            o.SetPixels32(px); o.Apply();
            string full = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, dst);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            System.IO.File.WriteAllBytes(full, o.EncodeToPNG());
            Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(dst);
            if (AssetImporter.GetAtPath(dst) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            return dst;
        }
    }
}
