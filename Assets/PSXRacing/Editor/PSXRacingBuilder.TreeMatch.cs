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
        /// <summary>Each view is drawn TMJitter x TMJitter times, a part of a pixel aside each: the share of a
        /// texel the tree covers.</summary>
        const int TMJitter = 4;
        /// <summary>Pictures round the half turn (the other half are these mirrored): the card shows the nearest.</summary>
        internal const int TMViews = 4;
        /// <summary>A screen pixel's size on a tree at the hand-over to its far card (m): MEDIUM's 240 m on a
        /// 2054-line frame through the game's 58-degree lens. THE SHEET'S TEXEL IS THAT PIXEL: the card is
        /// read one texel a pixel where it takes over from the mesh (no mip between them: a mip keeps the
        /// sheet's cover as a whole, by fattening limbs and dropping twigs), and from its mips beyond.</summary>
        const float TMHandOverPixelM = 0.13f;
        /// <summary>The card is this much wider than the tree (the two views share a sheet: an empty margin between them).</summary>
        const float TMMargin = 1.06f;
        /// <summary>The cards' albedo against their meshes' (measured by TreeMatchAudit at noon; see the round's notes).</summary>
        internal static float TMLeafGain = EnvF("PSX_TM_LEAFGAIN", 0.93f), TMBareGain = EnvF("PSX_TM_BAREGAIN", 1.0f);
        /// <summary>The card's light normal: this share of the direction to the eye, and this much straight up
        /// (in leaf: the mean normal of a crown's eye-side half; bare: of limbs turned round their own axes, their
        /// upper sides to the sky).</summary>
        /// (Measured at noon from three bearings, audit m2: the beeches' meshes answer the sun's bearing as a
        /// card at 0.5 does, the Meadow poplars' hardly at all (0.2): 0.4 serves both within a tenth. Bare, a
        /// card at 0.55 + 0.2 up swung 1.7 times as far as its mesh between bearings: 0.30 + 0.11.)
        internal static float TMLeafN = EnvF("PSX_TM_LEAFN", 0.4f), TMLeafUp = EnvF("PSX_TM_LEAFUP", 0f),
                              TMBareN = EnvF("PSX_TM_BAREN", 0.30f), TMBareUp = EnvF("PSX_TM_BAREUP", 0.11f);
        /// <summary>VIEW FADE (PSX/Lit _PSXCrownCard.w): within this share of a view step (45 degrees) of the
        /// half-way bearing between two of a card's views, the next view dithers in (2 x 2 pixel quads; half
        /// at the half-way bearing) instead of the whole picture switching there. 0 = the switch.</summary>
        internal static float TMViewFade = Mathf.Clamp(EnvF("PSX_TM_VIEWFADE", 0.16f), 0f, 0.5f);

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
            tmPlanOf.Clear();
            if (list == null || list.Count == 0) return 0;
            if (psxLit == null) psxLit = Shader.Find("PSX/Lit");
            Directory.CreateDirectory(TMDir);
            if (!AssetDatabase.IsValidFolder(TMDir)) AssetDatabase.Refresh();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int made = 0, sheets = 0, reused = 0;
            long texels = 0;
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
                    if (!TMFrame(v, out pl.halfW, out pl.yLo, out pl.yHi, out float reach, out pl.cw, out pl.ch)) continue;
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
                        string path = TMBakeSheet(cam, v, leaf, pl.halfW, pl.yLo, pl.yHi, reach, pl.cw, pl.ch, pl.sheetKey[d], out float cover);
                        sheetOf[pl.sheetKey[d]] = path;
                        if (path != null) { sheets++; texels += (long)pl.cw * TMViews * pl.ch; lines.Append($"\n  {pl.sheetKey[d]}: {2f * pl.halfW:0.0} x {pl.yHi - pl.yLo:0.0} m, {pl.cw * TMViews} x {pl.ch} px, cover {cover * 100f:0} %"); }
                    }
                    plans.Add(pl);
                    tmPlanOf[vi < names.Count && names[vi] != null ? names[vi] : pl.key] = pl;
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
                        // (a gain measured for this very card - TreeMatchAudit's PSX_TM_CALIB - before the general one)
                        if (TMGains().TryGetValue("TM_" + sk, out float measured)) gain = measured;
                        mat = MakeMat("TM_" + sk, path, cutoff: LeafCutoff, tint: new Color(gain, gain, gain), affine: 0f, twoSided: true);
                        mat.enableInstancing = true;
                        mat.SetVector("_LeafVivid", new Vector4(1f, 2f, leafy ? 1f : 0f, 0f));   // (the sheet holds half the albedo)
                        mat.SetColor("_LeafTarget", Color.white);
                        mat.SetColor("_LeafTulip", leafy && leaf.HasProperty("_LeafTulip") ? leaf.GetColor("_LeafTulip") : new Color(0f, 0f, 0f, 0f));
                        // FALL 2: the season's phase moves the card as it moves its tree
                        // (its brightness y is in the sheet already: 1 here)
                        if (leafy && leaf.HasProperty("_LeafFall") && leaf.GetVector("_LeafFall").x > 0.5f) mat.SetVector("_LeafFall", new Vector4(1f, 1f, 0f, 0f));
                        mat.SetVector("_PSXGrowFoot", new Vector4(-pl.t.x, -pl.t.y, -pl.t.z, 0f));
                        // lit as a crown, not as a board (PSX/Lit _PSXCrownCard)
                        mat.SetVector("_PSXCrownCard", leafy ? new Vector4(TMLeafN, TMLeafUp, TMViews, TMViewFade) : new Vector4(TMBareN, TMBareUp, TMViews, TMViewFade));
                        // (PSX/Lit turns the card to the eye in its PSX_LODFADE path: on for the mirrors' draws too, which wear this material as it is)
                        mat.EnableKeyword("PSX_LODFADE");
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
            Log($"Tree match ({kit}): {made} species' far cards made from their own LOW meshes, {sheets} sheets ({TMViews} views a sheet, a texel {TMHandOverPixelM:0.00} m of the tree: {texels / 1e6:0.00} M texels in all) ({reused} shared), {clock.Elapsed.TotalSeconds:0.0} s; " +
                $"albedo gain leaf x{TMLeafGain:0.00}, bare x{TMBareGain:0.00}{lines}");
            return made;
        }

        const int TMLayer = 31;

        /// <summary>The cards' measured gains by material name (TreeMatchAudit.Calibrate writes them).</summary>
        internal const string TreeMatchGainsPath = "Assets/PSXRacing/Editor/TreeMatchGains.txt";
        static Dictionary<string, float> tmGains;
        static Dictionary<string, float> TMGains()
        {
            if (tmGains != null) return tmGains;
            tmGains = new Dictionary<string, float>();
            if (System.Environment.GetEnvironmentVariable("PSX_TM_NOGAINS") == "1" || !File.Exists(TreeMatchGainsPath)) return tmGains;
            foreach (var line in File.ReadAllLines(TreeMatchGainsPath))
            {
                var q = line.Split('\t');
                if (q.Length == 2 && float.TryParse(q[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g)) tmGains[q[0]] = g;
            }
            return tmGains;
        }
        static readonly byte TMKeptAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(2f * LeafCutoff * 255f), 2, 255);

        /// <summary>The last kit's species by name: their frames and sheets (TMCityCardAtlas).</summary>
        static readonly Dictionary<string, TMPlan> tmPlanOf = new Dictionary<string, TMPlan>();

        sealed class TMPlan
        {
            public MeshForest.Variant v; public string key; public float halfW, yLo, yHi; public Vector3 t; public int cw, ch;
            public readonly Material[] leaf = new Material[5]; public readonly string[] sheetKey = new string[5];
        }

        /// <summary>The tree's frame off its LOW mesh (foot frame: through pre): half the card's width,
        /// its foot and top, and the farthest any vertex stands from the trunk's axis.</summary>
        static bool TMFrame(MeshForest.Variant v, out float halfW, out float yLo, out float yHi, out float reach, out int cw, out int ch)
        {
            halfW = 0f; yLo = float.MaxValue; yHi = float.MinValue; reach = 0f; cw = ch = 0;
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
            // the card's cell in whole texels (fours: the sheet is block-compressed), the frame grown to it
            cw = Mathf.Clamp(4 * Mathf.CeilToInt(2f * reach * TMMargin / TMHandOverPixelM / 4f), 8, 512);
            ch = Mathf.Clamp(4 * Mathf.CeilToInt((yHi - yLo) / TMHandOverPixelM / 4f), 8, 512);
            halfW = Mathf.Max(reach * TMMargin, cw * TMHandOverPixelM * 0.5f);
            yHi = Mathf.Max(yHi, yLo + ch * TMHandOverPixelM);
            return true;
        }

        /// <summary>The far card in the tree's frame, moved onto the mesh's origin (<paramref name="t"/>:
        /// pre's translation; drawn with matchPre = that translation): ONE upright card facing -Z, which
        /// PSX/Lit turns to the eye about the tree's upright (_PSXCrownCard.z) - its picture the first of the
        /// sheet's TMViews, the shader stepping along the row.</summary>
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
            foreach (float y in ys)
            {
                float vv = (y - yLo) / (yHi - yLo);
                vs.Add(new Vector3(-halfW, y, 0f) - t); vs.Add(new Vector3(halfW, y, 0f) - t);
                uv.Add(new Vector2(0f, vv)); uv.Add(new Vector2(1f / TMViews, vv));
                nm.Add((Vector3.back + Vector3.up * 0.6f).normalized); nm.Add((Vector3.back + Vector3.up * 0.6f).normalized);
            }
            for (int r = 0; r + 1 < ys.Count; r++)
            {
                int a = r * 2;
                tr.AddRange(new[] { a, a + 2, a + 3, a, a + 3, a + 1 });
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
            // (the card turns about the tree's upright: its bounds are the turn's whole circle)
            mesh.bounds = new Bounds(new Vector3(0f, (yLo + yHi) * 0.5f, 0f) - t, new Vector3(2f * halfW, yHi - yLo, 2f * halfW));
            mesh.RecalculateTangents();
            if (fresh) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>A texel's own number 0..1 (the screen-door's, by its place in the sheet).</summary>
        static float TMNoise(int x, int y)
        {
            float f = 52.9829189f * ((0.06711056f * x + 0.00583715f * y) % 1f);
            return f - Mathf.Floor(f);
        }

        /// <summary>One look of one species into its sheet: the LOW mesh with its bark and this dress's leaves,
        /// from TMViews bearings over the half turn, each into a cell of <paramref name="cw"/> x <paramref name="ch"/>
        /// texels. Returns the sheet's asset path (null when nothing drew).</summary>
        static string TMBakeSheet(Camera cam, MeshForest.Variant v, Material leaf, float halfW, float yLo, float yHi, float reach, int cw, int ch, string sheetKey, out float cover)
        {
            cover = 0f;
            int SW = cw * TMViews;
            var mesh = v.lods[2];
            var basePos = new Vector3(0f, 6000f, 0f);
            var matrix = Matrix4x4.Translate(basePos) * v.pre;
            // THE PICTURE IS TAKEN AT THE HAND-OVER'S OWN SCALE: a texel of the sheet is a pixel of the screen
            // where the mesh hands over to this card. Drawn finer, a leaf sheet's fine mips show twigs and leaf
            // edges the mip that distance reads has lost (the first cards, drawn 13 times as fine, wore 1.8
            // times the twigs the winter beech shows at 240 m). So each view is drawn at that pixel size - the
            // same mips, the same alpha test a pixel - TMJitter x TMJitter times, each a part of a pixel aside:
            // the share of each texel the tree covers, and its colour there.
            float pxW = 2f * halfW / cw, pxH = (yHi - yLo) / ch;
            var rt = RenderTexture.GetTemporary(cw, ch, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            var sheet = new Color32[SW * ch];
            var sr = new int[cw * ch]; var sg = new int[cw * ch]; var sbl = new int[cw * ch]; var sn = new int[cw * ch];
            float dist = reach + 3f;
            int subs = Mathf.Min(mesh.subMeshCount, v.mats.Length);
            double sum = 0; int nz = 0;
            try
            {
                for (int q = 0; q < TMViews; q++)
                {
                    var rot = Quaternion.Euler(0f, q * 180f / TMViews, 0f);
                    cam.orthographicSize = (yHi - yLo) * 0.5f;
                    cam.aspect = 2f * halfW / (yHi - yLo);
                    cam.nearClipPlane = 0.5f; cam.farClipPlane = 2f * dist;
                    System.Array.Clear(sr, 0, sr.Length); System.Array.Clear(sg, 0, sg.Length); System.Array.Clear(sbl, 0, sbl.Length); System.Array.Clear(sn, 0, sn.Length);
                    for (int jy = 0; jy < TMJitter; jy++)
                        for (int jx = 0; jx < TMJitter; jx++)
                        {
                            var off = new Vector3(((jx + 0.5f) / TMJitter - 0.5f) * pxW, ((jy + 0.5f) / TMJitter - 0.5f) * pxH, 0f);
                            cam.transform.SetPositionAndRotation(basePos + rot * (new Vector3(0f, (yLo + yHi) * 0.5f, -dist) + off), rot);
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
                            var got = read.GetPixels32();
                            for (int i = 0; i < got.Length; i++)
                            {
                                if (got[i].a < 128) continue;
                                sr[i] += got[i].r; sg[i] += got[i].g; sbl[i] += got[i].b; sn[i]++;
                            }
                        }
                    // THE CUT KEEPS THE AREA AND THE OUTLINE: a texel is kept where the share of it the tree covers
                    // is over the texel's own number (a screen-door), so as much of the sheet passes the card's
                    // alpha test as the mesh covers, and a twig a fifth of a pixel wide stays a dotted line, as
                    // the mesh draws it at this distance (one cut for the whole sheet took every thin twig off the
                    // crown's edge and fattened the limbs: the bare poplars' cards came out 0.73 of their width).
                    for (int y = 0; y < ch; y++)
                        for (int x = 0; x < cw; x++)
                        {
                            int i = y * cw + x, n = sn[i];
                            if (n == 0) continue;
                            float share = n / (float)(TMJitter * TMJitter);
                            sum += share; nz++;
                            bool keep = share > TMNoise(q * cw + x, y) * 0.96f + 0.02f;
                            // (kept texels at twice the cutoff: between a kept texel and a dropped one the bilinear tap
                            // crosses the cutoff half way, so a magnified card neither swells nor thins)
                            sheet[y * SW + q * cw + x] = new Color32((byte)(sr[i] / n), (byte)(sg[i] / n), (byte)(sbl[i] / n), keep ? TMKeptAlpha : (byte)1);
                        }
                }
            }
            finally
            {
                Shader.SetGlobalFloat("_PSXIdOn", 0f);
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(read);
            }
            if (nz == 0) return null;
            cover = (float)(sum / sheet.Length);
            TMDilate(sheet, SW, ch, cw);
            var outTex = new Texture2D(SW, ch, TextureFormat.RGBA32, false);
            outTex.SetPixels32(sheet); outTex.Apply();
            string path = TMDir + "/" + sheetKey + ".png";
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            File.WriteAllBytes(full, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            return path;
        }

        // ---- CHARLOTTE'S CARDS IN THE KIT'S OWN TREES ------------------------------
        // Past the mesh forest's far range, outside the view's cone, in the seconds before a tile's trees are
        // fitted, and from the streamed ring out to 2.2 km (CityFar's far trees and the tint of the woods beyond
        // them), Charlotte's trees are CARDS wearing the stage forest's sixteen-cell atlas of the retro billboard
        // pack: other trees, other colours, and in winter another leaf state than the meshes that take over from
        // them. The same sixteen cells, cut from the kit's own match sheets in each dress: what stands in a cell
        // is one of the kit trees the planting draws for that cell's species (CityTrees.Cells).
        /// <summary>Cell -> the kit species tried in turn (the first the kit has).</summary>
        static readonly string[][] TMCellTrees =
        {
            new[] { "03_Poplar_skan", "05_Poplar_skan", "beech_tree_03" },                 // 0  hardwood / sycamore
            new[] { "beech_tree_02", "beech_tree_03" },                                     // 1  hardwood
            new[] { "05_Poplar_skan", "04_Poplar_skan", "beech_tree_06" },                  // 2  hardwood
            new[] { "grey_willow_02_r3_vase", "grey_willow_02" },                           // 3  crape myrtle
            new[] { "07_Poplar_skan", "02_Poplar", "beech_tree_07" },                       // 4  hardwood / sycamore
            new[] { "grey_willow_03_r3_vase", "grey_willow_03" },                           // 5  crape myrtle
            new[] { "beech_tree_03", "beech_tree_01" },                                     // 6  hardwood / sycamore
            new[] { "grey_willow_04_r3_vase", "grey_willow_04" },                           // 7  crape myrtle
            new[] { "beech_tree_06_r3_av", "beech_tree_06" },                               // 8  willow oak (and the street trees' bole)
            new[] { "beech_tree_07_r3_av", "beech_tree_07" },                               // 9  willow oak
            new[] { "beech_tree_08_r3_av", "beech_tree_08" },                               // 10 willow oak
            new[] { "04_Poplar_skan_r3_av", "beech_tree_06" },                              // 11 willow oak
            new[] { "03_Poplar_skan_pine_r3_pine", "03_Poplar_skan_pine" },                 // 12 pine
            new[] { "05_Poplar_skan_pine_r3_pine", "05_Poplar_skan_pine" },                 // 13 pine
            new[] { "03_Poplar_skan_pine", "05_Poplar_skan_pine" },                         // 14 pine
            new[] { "beech_tree_01_LOD0_snag", "beech_tree_01" },                           // 15 a dead or bare one
        };
        const int TMAtlasCell = 128;

        /// <summary>THE STAGES' cells 0-11 (the broadleaf cells; 12-15 are the conifers and the bare tree,
        /// which stay cards in the A chunks), each the kit tree whose FALL dress is that cell's colour (the
        /// planting clusters its cells by colour: PSXRacingBuilder.Stage's Fall / Red / Green groups): the
        /// tulip poplar's gold; the beeches' orange (autumn: 01, 07), red (02, 08), gold (03) and late green (06).</summary>
        static readonly string[][] TMStageCellTrees =
        {
            new[] { "PoplarTree001_LOD1", "beech_tree_03_LOD0" },   // 0  tulip poplar gold
            new[] { "beech_tree_01_LOD0" },                          // 1  orange maple
            new[] { "beech_tree_02_LOD0" },                          // 2  big orange-red (red group)
            new[] { "beech_tree_07_LOD0" },                          // 3  orange
            new[] { "beech_tree_03_LOD0" },                          // 4  gold
            new[] { "beech_tree_08_LOD0" },                          // 5  red over green (red group)
            new[] { "beech_tree_01_LOD0" },                          // 6  russet oak
            new[] { "beech_tree_02_LOD0" },                          // 7  scarlet maple (red group)
            new[] { "beech_tree_03_LOD0" },                          // 8  brown oak
            new[] { "beech_tree_06_LOD0" },                          // 9  yellow-green (green group)
            new[] { "beech_tree_06_LOD0" },                          // 10 green hardwood (green group)
            new[] { "beech_tree_06_LOD0" },                          // 11 green hardwood (green group)
            null, null, null, null,                                  // 12-15 conifers, the bare tree: as they are
        };

        /// <summary>The city's card atlas per dress (5), cut from the match sheets just made; null entries
        /// where there were none. Dresses that look alike (winter and snow; spring and summer) share a file.</summary>
        internal static Texture2D[] TMCityCardAtlas() => TMCardAtlas("cityatlas_", TMCellTrees, null);

        /// <summary>A STAGE's card atlas per dress (5): the broadleaf cells cut from the stage kit's match sheets
        /// just made, the rest (conifers, the bare tree) as the dress's own atlas has them. Worn by the B chunks'
        /// cards and crowns (MeshForest.matchCardAtlas): what the mirrors show behind the car and the forest past
        /// the far range are then the kit's own trees in the dress, as the meshes and far cards are.</summary>
        internal static Texture2D[] TMStageCardAtlas() => TMCardAtlas("stageatlas_", TMStageCellTrees, TMStageBaseAtlas);

        /// <summary>The dress's own stage atlas: the stage being built's (its theme's art folder), or - outside a
        /// stage build (TreeMatchBuild.PatchStages: no theme is set) - the mountain stages' shared one in Art/BRP.</summary>
        static string TMStageBaseAtlas(int dress)
        {
            if (theme != null) return DressAtlasPath(dress);
            return Root + "/Art/BRP/Gen" + (dress == (int)Season.Fall ? "/TreeAtlas.png" : "/TreeAtlas_" + DressSuffix[dress] + ".png");
        }

        static Texture2D[] TMCardAtlas(string stem, string[][] cellTrees, System.Func<int, string> baseOfDress)
        {
            var outTex = new Texture2D[5];
            if (!TreeMatchBuildOn || tmPlanOf.Count == 0) return outTex;
            var pathOf = new Dictionary<string, string>();
            var cache = new Dictionary<string, Color32[]>();
            for (int d = 0; d < 5; d++)
            {
                var plans = new TMPlan[16];
                string basePath = baseOfDress != null ? baseOfDress(d) : null;
                string sig = (basePath ?? "") + "|";
                for (int c = 0; c < 16; c++)
                {
                    if (cellTrees[c] != null) foreach (var nm in cellTrees[c]) if (tmPlanOf.TryGetValue(nm, out plans[c])) break;
                    sig += (plans[c] != null ? plans[c].sheetKey[d] : "-") + "|";
                }
                if (!pathOf.TryGetValue(sig, out string path))
                {
                    int A = TMAtlasCell * 4;
                    var px = new Color32[A * A];
                    // (a stage's: the dress's own atlas under it - the cells without a match keep their pictures)
                    if (basePath != null)
                    {
                        string bf = Path.Combine(Directory.GetParent(Application.dataPath).FullName, basePath);
                        if (File.Exists(bf))
                        {
                            var bt = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            bt.LoadImage(File.ReadAllBytes(bf));
                            if (bt.width == A && bt.height == A) px = bt.GetPixels32();
                            Object.DestroyImmediate(bt);
                        }
                    }
                    int cells = 0;
                    for (int c = 0; c < 16; c++)
                    {
                        var pl = plans[c];
                        if (pl == null) continue;
                        string sp = TMDir + "/" + pl.sheetKey[d] + ".png";
                        if (!cache.TryGetValue(sp, out var src))
                        {
                            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, sp);
                            if (File.Exists(full))
                            {
                                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                                t.LoadImage(File.ReadAllBytes(full));
                                src = t.width == pl.cw * TMViews && t.height == pl.ch ? t.GetPixels32() : null;
                                Object.DestroyImmediate(t);
                            }
                            cache[sp] = src;
                        }
                        if (src == null) continue;
                        int TMCellW = pl.cw, TMCellH = pl.ch;
                        cells++;
                        {
                            // (the cell cleared first: a stage's base picture must not show round the kit's tree)
                            int cc = c % 4, cr = c / 4;
                            for (int y = 0; y < TMAtlasCell; y++)
                                for (int x = 0; x < TMAtlasCell; x++)
                                    px[(cr * TMAtlasCell + y) * A + cc * TMAtlasCell + x] = new Color32(0, 0, 0, 0);
                        }
                        // the tree stood in its cell: its foot on the cell's foot, its top just under the cell's,
                        // its trunk on the cell's middle; a conifer's cell is drawn onto a card 0.62 as wide as
                        // it is tall (CityTrees.CardWidth), so its picture is that much wider here
                        bool conifer = cellTrees == TMCellTrees && c >= 12 && c <= 14;
                        float treeH = pl.yHi - Mathf.Max(0f, pl.yLo), treeW = 2f * pl.halfW;
                        float mPerPxY = treeH / (TMAtlasCell * 0.98f);
                        float mPerPxX = mPerPxY * (conifer ? 0.62f : 1f);
                        if (treeW / mPerPxX > TMAtlasCell * 0.98f) { float k = treeW / mPerPxX / (TMAtlasCell * 0.98f); mPerPxX *= k; mPerPxY *= k; }
                        int col = c % 4, row = c / 4;
                        for (int y = 0; y < TMAtlasCell; y++)
                            for (int x = 0; x < TMAtlasCell; x++)
                            {
                                float mx = (x + 0.5f - TMAtlasCell * 0.5f) * mPerPxX, my = (y + 0.5f) * mPerPxY + Mathf.Max(0f, pl.yLo);
                                float su = (mx + pl.halfW) / (2f * pl.halfW) * TMCellW, sv = (my - pl.yLo) / (pl.yHi - pl.yLo) * TMCellH;
                                int sx = Mathf.FloorToInt(su), sy = Mathf.FloorToInt(sv);
                                if (sx < 0 || sx >= TMCellW || sy < 0 || sy >= TMCellH) continue;
                                var s0 = src[sy * (TMCellW * TMViews) + sx];   // (the first view)
                                // (the sheet holds half the albedo: whole here, for a material that reads it as it is)
                                px[(row * TMAtlasCell + y) * A + col * TMAtlasCell + x] =
                                    new Color32((byte)Mathf.Min(255, s0.r * 2), (byte)Mathf.Min(255, s0.g * 2), (byte)Mathf.Min(255, s0.b * 2), s0.a > 128 ? (byte)255 : (byte)0);
                            }
                    }
                    if (cells == 0) { pathOf[sig] = null; continue; }
                    TMDilate(px, A, A, TMAtlasCell);
                    var tex = new Texture2D(A, A, TextureFormat.RGBA32, false);
                    tex.SetPixels32(px); tex.Apply();
                    path = TMDir + "/" + stem + pathOf.Count + ".png";
                    File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    pathOf[sig] = path;
                    Log($"Tree match: card atlas {path} for dress {Seasons.DressNames[d]}: {cells} of 16 cells from the kit's own trees");
                }
            }
            AssetDatabase.Refresh();
            for (int d = 0; d < 5; d++)
            {
                string sig = (baseOfDress != null ? baseOfDress(d) : "") + "|";
                for (int c = 0; c < 16; c++)
                {
                    TMPlan pl = null;
                    if (cellTrees[c] != null) foreach (var nm in cellTrees[c]) if (tmPlanOf.TryGetValue(nm, out pl)) break;
                    sig += (pl != null ? pl.sheetKey[d] : "-") + "|";
                }
                if (pathOf.TryGetValue(sig, out string path) && path != null) outTex[d] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return outTex;
        }

        /// <summary>The drawn texels' colours spread into the empty ones round them (a few texels: what a
        /// coarser mip and the bilinear tap mix in), so no card has a dark fringe.</summary>
        static void TMDilate(Color32[] px, int w, int h, int cellW)
        {
            var have = new bool[px.Length];
            // (a texel the screen-door left out keeps its own colour: alpha 1)
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
                                if (xx / cellW != x / cellW) continue;
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
            ti.maxTextureSize = 2048;
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

        /// <summary>The city kit ON DISK given its match cards again (no kit rebuild: its species, levels and
        /// materials stay; only the cards, their sheets and the card atlas are made anew). For a change to the
        /// bake alone.   -executeMethod PSXRacing.EditorTools.TreeMatchBuild.CityMatchOnly</summary>
        public static void CityMatchOnly()
        {
            int code = 0;
            try
            {
                System.Environment.SetEnvironmentVariable("PSX_PC_CONTENT", "1");
                CityTreeKit.Reset();
                var kit = CityTreeKit.Get();
                if (kit == null || kit.variants == null) { Debug.LogError("[TreeMatchBuild] no city tree kit in Resources"); code = 1; }
                else
                {
                    int n = PSXRacingBuilder.TreeMatchKit(kit.variants, kit.names, "city", null, true);
                    kit.cardAtlas = PSXRacingBuilder.TMCityCardAtlas();
                    EditorUtility.SetDirty(kit);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[TreeMatchBuild] city kit (cards only): {n} of {kit.variants.Length} species with match cards");
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
                    f.matchCardAtlas = PSXRacingBuilder.TMStageCardAtlas();
                    EditorUtility.SetDirty(f);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                    int na = 0; if (f.matchCardAtlas != null) foreach (var t in f.matchCardAtlas) if (t != null) na++;
                    log.Append($"  {def.id}: {n} of {f.variants.Length} species with match cards, the old cards' atlas in {na} dresses, saved {ok}\n");
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
