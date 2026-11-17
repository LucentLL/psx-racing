using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// TREE MATCH (2026-11-16). Owner, after PC47/PC48: "Tree pop-in is
    /// egregious. It is very distracting to see trees full of leaves (or
    /// different color trees in Fall) pop-in as you approach and then the tree
    /// is leafless (or different colors trees overtake one another in Fall
    /// orange, red, green."
    ///
    /// The far level of a mesh tree was a sheet the PACK drew of that tree in
    /// summer leaf: darkened for winter (a full dark crown that turned into a
    /// bare trunk at the hand-over: the Meadow poplars, Big Oak, Big Poplar),
    /// recoloured for fall, cut and stretched for a street form. Here the far
    /// level is made FROM THE TREE: for every species and every look it wears
    /// (bare; each leaf material of its dresses), its own LOW mesh is drawn
    /// with that dress's own materials, from the two bearings the far cross
    /// shows, into a sheet - the surface colour exactly as PSX/Lit works it
    /// out for the mesh (its tint, saturation, brightness and recolour), read
    /// through the shader's own albedo output (_PSXIdOn 2). The card that
    /// wears it stands in the tree's frame, exactly as wide and as tall as the
    /// mesh, on the mesh's own origin; its material lights it as the mesh's
    /// leaves are lit and carries the same per-tree fall colour rule
    /// (_LeafTulip), which PSX/Lit works out of the instance's origin - the
    /// same origin, so the same tree is the same colour at every distance.
    ///
    /// Sheets are pictures of store content: written beside the store files
    /// (Assets/StoreAssets/Gen/TreeMatch, never committed), made by the kit
    /// builds (the city kit; a stage's mesh tree kit) and by the patch pass for
    /// scenes built before it (TreeMatchBuild.PatchStages).
    ///
    /// PSX_TREE_MATCH=0: nothing is made here, and the game (MeshForest.TreeMatch)
    /// draws the pack's sheets as before even from a kit that carries the cards.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        const string TMDir = "Assets/StoreAssets/Gen/TreeMatch";
        internal const string TreeMatchDir = TMDir;
        internal const float TreeMatchCutoff = LeafCutoff;
        /// <summary>One view's cell (px), drawn TMSuper times as fine and averaged down.</summary>
        const int TMCell = 320, TMSuper = 4;
        /// <summary>The card is this much wider than the tree (the two views share a sheet: an empty margin between them).</summary>
        const float TMMargin = 1.06f;
        /// <summary>The cards' albedo against their meshes' (measured by TreeMatchAudit at noon; see the round's notes).</summary>
        internal static float TMLeafGain = EnvF("PSX_TM_LEAFGAIN", 1.0f), TMBareGain = EnvF("PSX_TM_BAREGAIN", 1.0f);
        /// <summary>The card's light normal: this share of the direction to the eye, and this much straight up
        /// (in leaf: the mean normal of a crown's eye-side half; bare: of limbs turned round their own axes, their
        /// upper sides to the sky).</summary>
        internal static float TMLeafN = EnvF("PSX_TM_LEAFN", 0.5f), TMLeafUp = EnvF("PSX_TM_LEAFUP", 0f),
                              TMBareN = EnvF("PSX_TM_BAREN", 0.55f), TMBareUp = EnvF("PSX_TM_BAREUP", 0.2f);

        static float EnvF(string k, float d)
        {
            var s = System.Environment.GetEnvironmentVariable(k);
            return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : d;
        }

        internal static bool TreeMatchBuildOn => System.Environment.GetEnvironmentVariable("PSX_TREE_MATCH") != "0";

        static string TMSafe(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// The match cards of a kit's species. <paramref name="everyTree"/>:
        /// also the species that have no far level at all today (the city's
        /// crape myrtle stand-ins and young trees, which vanish past the mesh
        /// range); otherwise only those that have one.
        /// </summary>
        internal static int TreeMatchKit(IList<MeshForest.Variant> list, IList<string> names, string kit, Material[] leafByDress, bool everyTree)
        {
            if (!TreeMatchBuildOn) { Log("Tree match: OFF (PSX_TREE_MATCH=0) - the packs' own far sheets"); return 0; }
            if (list == null || list.Count == 0) return 0;
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            Directory.CreateDirectory(TMDir);
            if (!AssetDatabase.IsValidFolder(TMDir)) AssetDatabase.Refresh();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int made = 0, sheets = 0, reused = 0;
            var camGo = new GameObject("TreeMatchBakeCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << TMLayer;
            cam.allowHDR = false; cam.allowMSAA = false;
            var sheetOf = new Dictionary<string, string>();
            var lines = new System.Text.StringBuilder();
            // PASS 1: every sheet drawn and written (files only); then ONE import of them all (a sheet imported
            // on its own cost the project a refresh each: eight seconds a sheet, two hundred sheets)
            var plans = new List<TMPlan>();
            try
            {
                for (int vi = 0; vi < list.Count; vi++)
                {
                    var v = list[vi];
                    if (v == null || v.lods == null || v.lods.Length < 3 || v.lods[2] == null) continue;
                    bool hasFar = v.lods.Length > 3 && v.lods[3] != null;
                    // (a plant with a draw limit - understory, ground cover, litter - thins out under the canopy: no far level)
                    if (!hasFar && (!everyTree || v.maxDist > 0f || v.height < 2.5f)) continue;
                    var pl = new TMPlan { v = v, key = kit + "_" + TMSafe(vi < names.Count && names[vi] != null ? names[vi] : (v.lods[0] != null ? v.lods[0].name : "v" + vi)) };
                    if (!TMFrame(v, out pl.halfW, out pl.yLo, out pl.yHi, out float reach)) continue;
                    pl.t = v.pre.GetColumn(3);
                    // (a species with no leaf submesh - all of it follows the dress, or none of it - is whatever its submeshes wear)
                    bool anyLeafSub = false;
                    if (v.leafSub != null) foreach (bool b in v.leafSub) anyLeafSub |= b;
                    for (int d = 0; d < 5; d++)
                    {
                        Material leaf = v.leafDress != null && v.leafDress.Length == 5 ? v.leafDress[d]
                                      : leafByDress != null && d < leafByDress.Length ? leafByDress[d] : null;
                        pl.leaf[d] = leaf;
                        string look = !anyLeafSub ? "asis" : leaf == null ? "bare" : TMSafe(leaf.name);
                        pl.sheetKey[d] = pl.key + "_" + look;
                        if (sheetOf.ContainsKey(pl.sheetKey[d])) { reused++; continue; }
                        string path = TMBakeSheet(cam, v, leaf, pl.halfW, pl.yLo, pl.yHi, reach, pl.sheetKey[d], out float cover);
                        sheetOf[pl.sheetKey[d]] = path;
                        if (path != null) { sheets++; lines.Append($"\n  {pl.sheetKey[d]}: {2f * pl.halfW:0.0} x {pl.yHi - pl.yLo:0.0} m, cover {cover * 100f:0} %"); }
                    }
                    plans.Add(pl);
                }
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXIdOn", 0f);
                Object.DestroyImmediate(camGo);
            }
            AssetDatabase.Refresh();
            // PASS 2: the cards and their materials
            var matOf = new Dictionary<string, Material>();
            foreach (var pl in plans)
            {
                var v = pl.v;
                var dress = new Material[5];
                bool any = false;
                for (int d = 0; d < 5; d++)
                {
                    string sk = pl.sheetKey[d];
                    if (!sheetOf.TryGetValue(sk, out string path) || path == null) continue;
                    if (!matOf.TryGetValue(sk, out var mat))
                    {
                        var leaf = pl.leaf[d];
                        // lit as the mesh's leaves are where it is in leaf (wrap, translucency, the crown's own
                        // shade, each tree its own shade and fall colour); as bark where it is bare
                        bool leafy = leaf != null && leaf.HasProperty("_LeafVivid") && leaf.GetVector("_LeafVivid").z > 0.5f;
                        float gain = leafy ? TMLeafGain : TMBareGain;
                        mat = MakeMat("TM_" + sk, path, cutoff: LeafCutoff, tint: new Color(gain, gain, gain), affine: 0f, twoSided: true);
                        mat.enableInstancing = true;
                        mat.SetVector("_LeafVivid", new Vector4(1f, 2f, leafy ? 1f : 0f, 0f));   // (the sheet holds half the albedo)
                        mat.SetColor("_LeafTarget", Color.white);
                        mat.SetColor("_LeafTulip", leafy && leaf.HasProperty("_LeafTulip") ? leaf.GetColor("_LeafTulip") : new Color(0f, 0f, 0f, 0f));
                        mat.SetVector("_PSXGrowFoot", new Vector4(-pl.t.x, -pl.t.y, -pl.t.z, 0f));
                        // lit as a crown, not as a board (PSX/Lit _PSXCrownCard)
                        mat.SetVector("_PSXCrownCard", leafy ? new Vector4(TMLeafN, TMLeafUp, 0f, 0f) : new Vector4(TMBareN, TMBareUp, 0f, 0f));
                        mat.DisableKeyword("PSX_HD");
                        FarGrow(mat);
                        EditorUtility.SetDirty(mat);
                        matOf[sk] = mat;
                    }
                    dress[d] = mat;
                    any |= mat != null;
                }
                if (!any) continue;
                v.matchLod = TMCardMesh(pl.halfW, pl.yLo, pl.yHi, pl.t, "TM_" + pl.key);
                v.matchDress = dress;
                v.matchPre = Matrix4x4.Translate(pl.t);
                made++;
            }
            AssetDatabase.SaveAssets();
            Log($"Tree match ({kit}): {made} species' far cards made from their own LOW meshes, {sheets} sheets of {TMCell * 2} x {TMCell} px ({reused} shared), {clock.Elapsed.TotalSeconds:0.0} s; " +
                $"albedo gain leaf x{TMLeafGain:0.00}, bare x{TMBareGain:0.00}{lines}");
            return made;
        }

        const int TMLayer = 31;

        sealed class TMPlan
        {
            public MeshForest.Variant v; public string key; public float halfW, yLo, yHi; public Vector3 t;
            public readonly Material[] leaf = new Material[5]; public readonly string[] sheetKey = new string[5];
        }

        /// <summary>The tree's frame off its LOW mesh (foot frame: through pre): half the card's width,
        /// its foot and top, and the farthest any vertex stands from the trunk's axis.</summary>
        static bool TMFrame(MeshForest.Variant v, out float halfW, out float yLo, out float yHi, out float reach)
        {
            halfW = 0f; yLo = float.MaxValue; yHi = float.MinValue; reach = 0f;
            var m = v.lods[2];
            int n = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                if (!TreeKit.Read(m, s, out var verts, out var tris)) continue;
                foreach (int i in tris)
                {
                    var q = v.pre.MultiplyPoint3x4(verts[i]);
                    reach = Mathf.Max(reach, Mathf.Sqrt(q.x * q.x + q.z * q.z));
                    yLo = Mathf.Min(yLo, q.y); yHi = Mathf.Max(yHi, q.y);
                    n++;
                }
            }
            if (n == 0 || yHi - yLo < 0.3f || reach < 0.05f) return false;
            yLo = Mathf.Max(yLo, -0.6f);                 // (roots under the ground are not drawn far away)
            yHi += 0.02f * (yHi - yLo);
            halfW = reach * TMMargin;
            return true;
        }

        /// <summary>The cross of two cards in the tree's frame, moved onto the mesh's origin
        /// (<paramref name="t"/>: pre's translation; drawn with matchPre = that translation).</summary>
        static Mesh TMCardMesh(float halfW, float yLo, float yHi, Vector3 t, string name)
        {
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>(); var nm = new List<Vector3>();
            // A row of vertices where the crown's own shade ends (PSX/Lit's foliage light darkens a tree's lower
            // 16 m over its origin, per vertex): with only a foot and a top the card ran that shade evenly up a
            // 25 m tree, a fifth darker than its mesh at mid height.
            var ys = new List<float> { yLo };
            float yShade = t.y + 16f;
            if (yShade > yLo + 1f && yShade < yHi - 1f) ys.Add(yShade);
            ys.Add(yHi);
            for (int q = 0; q < 2; q++)
            {
                var rot = Quaternion.Euler(0f, q * 90f, 0f);
                float u0 = q * 0.5f, u1 = (q + 1) * 0.5f;
                var n = rot * Vector3.back;
                int i0 = vs.Count;
                foreach (float y in ys)
                {
                    float vv = (y - yLo) / (yHi - yLo);
                    vs.Add(rot * new Vector3(-halfW, y, 0f) - t); vs.Add(rot * new Vector3(halfW, y, 0f) - t);
                    uv.Add(new Vector2(u0, vv)); uv.Add(new Vector2(u1, vv));
                    nm.Add((n + Vector3.up * 0.6f).normalized); nm.Add((n + Vector3.up * 0.6f).normalized);
                }
                for (int r = 0; r + 1 < ys.Count; r++)
                {
                    int a = i0 + r * 2;
                    tr.AddRange(new[] { a, a + 2, a + 3, a, a + 3, a + 1 });
                }
            }
            // ONE asset a species, rewritten IN PLACE (its GUID is what every scene patched before this one holds;
            // WorldKit.SaveMesh deletes and makes anew, and prefixes the name with the venue being built)
            if (!AssetDatabase.IsValidFolder(WorldKit.GenDir)) AssetDatabase.CreateFolder(WorldKit.Root, "Generated");
            string path = WorldKit.GenDir + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = mesh == null;
            if (fresh) mesh = new Mesh();
            else mesh.Clear();
            mesh.name = name;
            mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetNormals(nm); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (fresh) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>One look of one species into its sheet: the LOW mesh with its bark and this dress's leaves,
        /// from the two bearings of the cross. Returns the sheet's asset path (null when nothing drew).</summary>
        static string TMBakeSheet(Camera cam, MeshForest.Variant v, Material leaf, float halfW, float yLo, float yHi, float reach, string sheetKey, out float cover)
        {
            cover = 0f;
            int cw = TMCell * TMSuper, ch = TMCell * TMSuper;
            var mesh = v.lods[2];
            var basePos = new Vector3(0f, 6000f, 0f);
            var matrix = Matrix4x4.Translate(basePos) * v.pre;
            var rt = RenderTexture.GetTemporary(cw, ch, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            var sheet = new Color32[TMCell * 2 * TMCell];
            var alpha = new float[TMCell * 2 * TMCell];
            float dist = reach + 3f;
            try
            {
                for (int q = 0; q < 2; q++)
                {
                    var rot = Quaternion.Euler(0f, q * 90f, 0f);
                    cam.transform.SetPositionAndRotation(basePos + rot * new Vector3(0f, (yLo + yHi) * 0.5f, -dist), rot);
                    cam.orthographicSize = (yHi - yLo) * 0.5f;
                    cam.aspect = 2f * halfW / (yHi - yLo);
                    cam.nearClipPlane = 0.5f; cam.farClipPlane = 2f * dist;
                    int subs = Mathf.Min(mesh.subMeshCount, v.mats.Length);
                    for (int s = 0; s < subs; s++)
                    {
                        var mat = v.leafSub != null && s < v.leafSub.Length && v.leafSub[s] ? leaf : v.mats[s];
                        if (mat == null) continue;
                        var rp = new RenderParams(mat) { camera = cam, layer = TMLayer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                        Graphics.RenderMesh(rp, mesh, s, matrix);
                    }
                    Shader.SetGlobalFloat("_PSXIdOn", 2f);
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (!RenderPipeline.SupportsRenderRequest(cam, request)) { Log("WARN: tree match: no render request on this pipeline"); return null; }
                    RenderPipeline.SubmitRenderRequest(cam, request);
                    Shader.SetGlobalFloat("_PSXIdOn", 0f);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, cw, ch), 0, 0);
                    read.Apply();
                    RenderTexture.active = prev;
                    var px = read.GetPixels32();
                    // averaged down: the colour of what was drawn, the share of the texel it covered
                    for (int y = 0; y < TMCell; y++)
                        for (int x = 0; x < TMCell; x++)
                        {
                            int r = 0, g = 0, b = 0, n = 0;
                            for (int sy = 0; sy < TMSuper; sy++)
                                for (int sx = 0; sx < TMSuper; sx++)
                                {
                                    var c = px[(y * TMSuper + sy) * cw + x * TMSuper + sx];
                                    if (c.a < 128) continue;
                                    r += c.r; g += c.g; b += c.b; n++;
                                }
                            int o = y * (TMCell * 2) + q * TMCell + x;
                            alpha[o] = n / (float)(TMSuper * TMSuper);
                            sheet[o] = n > 0 ? new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255) : new Color32(0, 0, 0, 0);
                        }
                }
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXIdOn", 0f);
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(read);
            }
            // THE CUT KEEPS THE AREA. The card is alpha-tested at LeafCutoff: the texels that pass must cover what
            // the mesh covers (a bare crown's twigs fill a fifth of a texel each: cut at a half they are all gone,
            // cut at nothing the crown is a solid blob). The cover threshold at which as many texels pass as the
            // mesh covered in all, mapped onto the cutoff.
            double sum = 0; int nz = 0;
            var hist = new int[TMSuper * TMSuper + 1];
            for (int i = 0; i < alpha.Length; i++)
            {
                if (alpha[i] <= 0f) continue;
                sum += alpha[i]; nz++;
                hist[Mathf.Clamp(Mathf.RoundToInt(alpha[i] * TMSuper * TMSuper), 0, TMSuper * TMSuper)]++;
            }
            if (nz == 0) return null;
            cover = (float)(sum / alpha.Length);
            float tau = 1f; int pass = 0;
            for (int k = TMSuper * TMSuper; k >= 1; k--)
            {
                pass += hist[k];
                tau = k / (float)(TMSuper * TMSuper);
                if (pass >= sum) break;
            }
            // (texels at the threshold land just over the cutoff; fuller ones well over it)
            float gainA = (LeafCutoff + 0.04f) / Mathf.Max(tau, 1f / (TMSuper * TMSuper));
            for (int i = 0; i < sheet.Length; i++)
            {
                if (alpha[i] <= 0f) continue;
                var c = sheet[i];
                c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(alpha[i] * gainA) * 255f), 1, 255);
                sheet[i] = c;
            }
            TMDilate(sheet, TMCell * 2, TMCell);
            var tex = new Texture2D(TMCell * 2, TMCell, TextureFormat.RGBA32, false);
            tex.SetPixels32(sheet); tex.Apply();
            string path = TMDir + "/" + sheetKey + ".png";
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        /// <summary>The drawn texels' colours spread into the empty ones round them (a few texels: what a
        /// coarser mip and the bilinear tap mix in), so no card has a dark fringe.</summary>
        static void TMDilate(Color32[] px, int w, int h)
        {
            var have = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) have[i] = px[i].a > 0;
            for (int pass = 0; pass < 12; pass++)
            {
                var add = new List<(int i, Color32 c)>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (have[i]) continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                // (each view's own half: no colour crosses from one view into the other)
                                if ((xx < w / 2) != (x < w / 2)) continue;
                                int j = yy * w + xx;
                                if (!have[j]) continue;
                                r += px[j].r; g += px[j].g; b += px[j].b; n++;
                            }
                        if (n > 0) add.Add((i, new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0)));
                    }
                if (add.Count == 0) break;
                foreach (var (i, c) in add) { px[i] = c; have[i] = true; }
            }
        }
    }

    /// <summary>A tree-match sheet imports ONCE with its settings (clamped, mips that keep the cut's
    /// coverage, its own size): the bake writes two hundred of them.</summary>
    class TreeMatchSheetImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PSXRacingBuilder.TreeMatchDir, System.StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = PSXRacingBuilder.TreeMatchCutoff;
            ti.alphaIsTransparency = false;
            ti.maxTextureSize = 1024;
            ti.filterMode = FilterMode.Bilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.sRGBTexture = true;
        }
    }

    /// <summary>
    /// TREE MATCH for what is already built: the city kit made again, and each
    /// stage scene's mesh forest given its species' match cards in place (no
    /// scene rebuild: the forest's plants, cards and every other object stay).
    ///   -executeMethod PSXRacing.EditorTools.TreeMatchBuild.PatchStages   PSX_TM_STAGES=BlueRidge,SwissNC226A,...  (default: every stage scene with a mesh forest)
    ///   -executeMethod PSXRacing.EditorTools.TreeMatchBuild.CityKit
    /// </summary>
    public static class TreeMatchBuild
    {
        public static void CityKit()
        {
            int code = 0;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                var kit = PSXRacingBuilder.EnsureCityTreeKit(true);
                if (kit == null) { Debug.LogError("[TreeMatchBuild] no city tree kit (store files?)"); code = 1; }
                else
                {
                    int n = 0;
                    foreach (var v in kit.variants) if (v != null && v.HasMatch) n++;
                    Debug.Log($"[TreeMatchBuild] city kit: {kit.variants.Length} species, {n} with match cards");
                }
            }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void PatchStages()
        {
            int code = 0;
            var log = new System.Text.StringBuilder("TREE MATCH - stage scenes patched " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                string only = System.Environment.GetEnvironmentVariable("PSX_TM_STAGES");
                var want = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
                var done = new HashSet<string>();
                for (int i = 0; i < TrackCatalog.Count; i++)
                {
                    var def = TrackCatalog.At(i);
                    if (want != null && !want.Contains(def.id)) continue;
                    string path = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
                    if (!File.Exists(path) || !done.Add(path)) continue;
                    // (only scenes that can hold a mesh forest are worth opening: the stages)
                    if (want == null && !File.ReadAllText(path).Contains("farRange:")) continue;
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    var f = Object.FindFirstObjectByType<MeshForest>();
                    if (f == null || f.variants == null) { log.Append($"  {def.id}: no mesh forest\n"); continue; }
                    var names = new List<string>();
                    foreach (var v in f.variants) names.Add(v != null && v.lods != null && v.lods.Length > 0 && v.lods[0] != null ? v.lods[0].name : "variant");
                    int n = PSXRacingBuilder.TreeMatchKit(f.variants, names, "stage", f.leafByDress, false);
                    EditorUtility.SetDirty(f);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                    log.Append($"  {def.id}: {n} of {f.variants.Length} species with match cards, saved {ok}\n");
                    if (!ok) code = 1;
                }
            }
            catch (System.Exception e) { Debug.LogException(e); log.Append("FAILED: " + e + "\n"); code = 1; }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PSXRacing_treematch_patch.txt"), log.ToString());
            Debug.Log("[TreeMatchBuild]\n" + log);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
