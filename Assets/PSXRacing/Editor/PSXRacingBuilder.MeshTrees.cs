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
        const float UnderMaxDist = 110f;
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
                        vm.mainTextureScale = new Vector2(NMGroundScale, NMGroundScale);
            }
            Log("Stage " + role + ", PC: NatureManufacture forest floor (summer plants_01, spring leaves_02, fall/winter leaves_01), x" + NMGroundScale);
            return true;
        }

        class MeshTreeKit
        {
            public MeshForest.Variant[] variants;
            public int treeCount;          // variants[0 .. treeCount) are canopy trees
            public int[] smallBeech, bushes;
            public Material[] leafByDress;
        }

        static MeshTreeKit meshTreeKit;

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
                                 true, new Color(0.80f, 0.86f, 0.78f));
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
                v.leafDress = new[] { null, green, green, fall[n++ % fall.Length], null };
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
            kit.smallBeech = small.ToArray();
            kit.bushes = bushes.ToArray();
            kit.variants = list.ToArray();
            Log($"Mesh forest kit: {kit.treeCount} canopy beeches, {small.Count} small beeches, {bushes.Count} cherry bushes.");
            meshTreeKit = kit;
            return kit;
        }

        /// <summary>One FBX's LOD meshes, its first slot's material, the leaf
        /// slot last ([bark, leaves] in every one of the pack's beeches, read in
        /// Blender), and its node transform with the base put at y = 0.</summary>
        static MeshForest.Variant LoadVariant(string dir, string name, Material barkMat)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + name + ".FBX");
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
                v.mats = new Material[subs];
                v.leafSub = new bool[subs];
                for (int s = 0; s < subs; s++)
                {
                    v.leafSub[s] = s == subs - 1 && subs > 1;
                    v.mats[s] = barkMat;
                }
                var pre = lod0.localToWorldMatrix;
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
                Log($"Mesh forest: {name} {v.height:0.0} m, LOD tris {Tris(v.lods[0])}/{Tris(v.lods[1])}/{Tris(v.lods[2])}, {subs} submesh(es)");
                return v;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static long Tris(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        static Material NMLitMat(string name, string dir, string bc, string n, string mask, bool leaves, Color tint,
                                 bool maskIsHdrp = true)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = lit;
            Texture2D T(string stem)
            {
                foreach (var ext in new[] { ".tga", ".png" })
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + stem + ext);
                    if (t != null) return t;
                }
                Log("WARN: mesh forest: texture missing " + dir + "/" + stem);
                return null;
            }
            mat.SetTexture("_BaseMap", T(bc));
            mat.SetColor("_BaseColor", tint);
            var nt = T(n);
            mat.SetTexture("_BumpMap", nt);
            if (nt) mat.EnableKeyword("_NORMALMAP");
            // The black cherry's mask is not laid out like the beeches'
            // (HDRP R metal / G AO / A smooth): read as metallic it turned the
            // bushes into grey mirrors of the sky. Plain, rough foliage there.
            var mt = maskIsHdrp ? T(mask) : null;
            mat.SetTexture("_MetallicGlossMap", mt);
            mat.SetTexture("_OcclusionMap", mt);
            if (mt) { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.EnableKeyword("_OCCLUSIONMAP"); }
            else { mat.DisableKeyword("_METALLICSPECGLOSSMAP"); mat.DisableKeyword("_OCCLUSIONMAP"); }
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", mt ? 1f : 0.2f);   // with a map: scaled by its alpha
            mat.SetFloat("_OcclusionStrength", 1f);
            if (leaves)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", (float)CullMode.Back);
                mat.renderQueue = -1;
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Understory round one canopy tree at (wx, wz): 1-3 small
        /// beeches and cherry bushes, by the same hash every build.</summary>
        static void AddUnderstory(MeshTreeKit kit, float wx, float wz, float d, int hsh,
                                  List<Vector4> inst, List<Vector2> scaleVar, ref int added)
        {
            if (kit.smallBeech.Length == 0 && kit.bushes.Length == 0) return;
            float roadHalf = RoadWidth * 0.5f;
            int count = d <= ForestDenseTo ? 3 : d <= 90f ? 2 : 1;
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
