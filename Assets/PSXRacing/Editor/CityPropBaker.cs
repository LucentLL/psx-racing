using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CITY PROP VARIANTS (Charlotte refinement WP-07, "draw-call
    /// prepay"): cheap copies of the four props that cost the streamed city
    /// the most draw calls, baked beside the full prefabs into
    /// Resources/CityProps/City and stood up by CityWorld instead of them
    /// (CityProps.CityPrefab).
    ///
    ///   house_simple  13 draws  -> 1: its twelve pack materials drawn from
    ///   trailers       9 draws  -> 1   ONE 256 px atlas per family, the
    ///                                  foundation skirt folded in.
    ///   burger_drive, pizzeria  -> the shell merged by material; the room
    ///                                  (300-odd renderers) OFF until the
    ///                                  viewer is within 40 m.
    ///
    /// THE ATLAS. The pack materials TILE - a wall's UVs run to 8 - so a
    /// plain atlas cannot hold them. Every vertex carries its texture's cell
    /// in its colour and PSX/Lit's PSX_ATLAS_RECT variant wraps the UV
    /// inside that cell with frac(); point-sampled and without mips that wrap
    /// is exact (plan critic C35). The cells are the pack textures themselves,
    /// resampled from their source files: 64 px, the busiest one 128 when
    /// the sheet has room. Nothing is drawn in code.
    ///
    /// THE RESTAURANTS keep everything that makes them places (plan critic
    /// C1): the DriveThru order bay, the hinged doors (never merged: they
    /// swing), the apron, and every collider on every piece, so the dining
    /// room can still be walked into. What is INTERIOR is decided by looking:
    /// rays from a ring of viewpoints round the lot, at kerb, cab and first-
    /// floor height and from above, to points in each piece's bounds; a piece
    /// no ray reaches without passing through another piece's collider is
    /// room, and goes behind CityPropInterior's switch. The city props wear
    /// opaque windows, so from outside the room is never seen anyway.
    ///
    /// Run by BakeCityProps (every full scene build) and by
    /// PSXRacingBuilder.BuildCityScenesOnly; menu PSX Racing/Bake City Prop
    /// Variants. Writes PSXRacing_city_props.txt.
    /// </summary>
    public static class CityPropBaker
    {
        const string Root = "Assets/PSXRacing";
        const string FullDir = Root + "/Resources/CityProps";
        public const string OutDir = FullDir + "/City";
        const string ArtDir = Root + "/Art/City/Props";
        public const int AtlasPx = 256;
        /// <summary>Where the restaurant stands while it is looked at: far
        /// above anything the open scene might hold.</summary>
        static readonly Vector3 Far = new Vector3(0f, 6000f, 0f);

        /// <summary>A family sharing one atlas (null key: no atlas, the
        /// restaurants).</summary>
        static string AtlasKey(byte kind) =>
            kind == CityProps.House ? "house" :
            kind >= CityProps.Trailer0 && kind <= CityProps.Trailer2 ? "trailer" : null;

        class Piece
        {
            public MeshRenderer r;
            public Mesh mesh;
            public Matrix4x4 toRoot;
            public Material[] mats;
        }

        class TexInfo
        {
            public Texture tex;
            public Color tint;
            public float area;
            public int size;
            public RectInt cell;
        }

        [MenuItem("PSX Racing/Bake City Prop Variants")]
        public static void BakeMenu()
        {
            foreach (var l in BakeVariants()) Debug.Log("[CityProps] " + l);
        }

        /// <summary>Bake every variant from the full prefabs; the report lines.</summary>
        public static List<string> BakeVariants()
        {
            var report = new List<string>();
            atlasable.Clear();
            EnsureFolder(FullDir, "City");
            EnsureFolder(Root + "/Art/City", "Props");

            // family -> kinds, in the table's order
            var families = new Dictionary<string, List<byte>>();
            var restaurants = new List<byte>();
            foreach (var kv in CityProps.Defs)
            {
                if (!CityProps.HasCityVariant(kv.Key)) continue;
                string key = AtlasKey(kv.Key);
                if (key == null) { restaurants.Add(kv.Key); continue; }
                if (!families.TryGetValue(key, out var list)) families[key] = list = new List<byte>();
                list.Add(kv.Key);
            }

            foreach (var fam in families)
            {
                try { BakeAtlasFamily(fam.Key, fam.Value, report); }
                catch (System.Exception e) { report.Add("FAILED " + fam.Key + ": " + e.Message); Debug.LogException(e); }
            }
            foreach (var kind in restaurants)
            {
                try { BakeRestaurant(kind, report); }
                catch (System.Exception e) { report.Add("FAILED " + NameOf(kind) + ": " + e.Message); Debug.LogException(e); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_city_props.txt"), report);
            return report;
        }

        static string NameOf(byte kind) => Path.GetFileName(CityProps.Defs[kind].res);

        static GameObject LoadFull(byte kind)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(FullDir + "/" + NameOf(kind) + ".prefab");
            if (p == null) throw new System.Exception("no full prefab " + FullDir + "/" + NameOf(kind) + ".prefab - bake the city props first");
            return p;
        }

        // ==================================================================
        //  Houses and trailers: one atlas per family, one draw per prop.
        // ==================================================================
        static void BakeAtlasFamily(string key, List<byte> kinds, List<string> report)
        {
            // Every prop of the family, stood up, and its pieces.
            var insts = new List<(byte kind, GameObject go, List<Piece> pieces, int drawsBefore, int collidersBefore)>();
            try
            {
                foreach (var kind in kinds)
                {
                    var go = (GameObject)Object.Instantiate(LoadFull(kind));
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var pieces = new List<Piece>();
                    int draws = 0;
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        if (!r.enabled) continue;
                        draws += r.sharedMaterials.Length;
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        pieces.Add(new Piece { r = r, mesh = mf.sharedMesh, toRoot = go.transform.worldToLocalMatrix * r.transform.localToWorldMatrix, mats = r.sharedMaterials });
                    }
                    insts.Add((kind, go, pieces, draws, go.GetComponentsInChildren<Collider>(true).Length));
                }

                // The textures the atlas will hold: every opaque PSX/Lit
                // material, by texture and tint, weighted by the surface it
                // covers.
                var texes = new List<TexInfo>();
                foreach (var it in insts)
                    foreach (var pc in it.pieces)
                        for (int s = 0; s < pc.mats.Length && s < pc.mesh.subMeshCount; s++)
                        {
                            var m = pc.mats[s];
                            if (!Atlasable(m)) continue;
                            var ti = FindOrAdd(texes, m);
                            // the foundation skirt is mostly buried (its top and
                            // bottom never show): it must not win the big cell
                            ti.area += SurfaceArea(pc.mesh, s, pc.toRoot) * (pc.r.name == "Skirt" ? 0.1f : 1f);
                        }
                if (texes.Count == 0) throw new System.Exception("nothing to atlas");
                PlanCells(texes);
                var atlasMat = WriteAtlas(key, texes);

                foreach (var it in insts)
                {
                    var keptApart = new List<Material>();
                    var mesh = MergePieces(it.pieces, texes, atlasMat, keptApart, out var mats);
                    mesh.name = NameOf(it.kind) + "_city";
                    var cellsWritten = mesh.colors32;
                    SaveMesh(mesh, OutDir + "/" + NameOf(it.kind) + "_merged.asset");
                    // read the cells back off the SAVED asset: they are texel
                    // addresses, and one quantised byte is a seam
                    var back = AssetDatabase.LoadAssetAtPath<Mesh>(OutDir + "/" + NameOf(it.kind) + "_merged.asset");
                    var cellsBack = back != null ? back.colors32 : new Color32[0];
                    int moved = cellsBack.Length == cellsWritten.Length ? 0 : cellsWritten.Length;
                    for (int i = 0; moved == 0 && i < cellsBack.Length; i++)
                        if (!cellsBack[i].Equals(cellsWritten[i])) moved++;
                    if (moved > 0) report.Add($"FAILED {NameOf(it.kind)}: {moved} vertices' atlas cells changed on save");
                    foreach (var pc in it.pieces) StripRenderer(pc.r);
                    AddMergedRenderer(it.go, mesh, mats);
                    it.go.name = NameOf(it.kind);
                    int drawsAfter = DrawsOf(it.go);
                    int collAfter = it.go.GetComponentsInChildren<Collider>(true).Length;
                    PrefabUtility.SaveAsPrefabAsset(it.go, OutDir + "/" + NameOf(it.kind) + ".prefab");
                    report.Add($"{NameOf(it.kind)}: {it.drawsBefore} -> {drawsAfter} draws (atlas '{key}' of {texes.Count} textures" +
                               (keptApart.Count > 0 ? $", {keptApart.Count} material(s) kept apart" : "") +
                               $"), {mesh.vertexCount} verts, colliders {it.collidersBefore} -> {collAfter}");
                }
                var sizes = new List<string>();
                foreach (var t in texes) sizes.Add($"{(t.tex != null ? t.tex.name : "white")} {t.size}px");
                report.Add($"atlas '{key}' {AtlasPx}px: " + string.Join(", ", sizes));
            }
            finally
            {
                foreach (var it in insts) if (it.go != null) Object.DestroyImmediate(it.go);
            }
        }

        /// <summary>An opaque PSX/Lit material, or a cutout one whose
        /// texture never actually cuts (every alpha at or over the cutoff).</summary>
        static readonly Dictionary<Material, bool> atlasable = new Dictionary<Material, bool>();

        static bool Atlasable(Material m)
        {
            if (m == null) return false;
            if (atlasable.TryGetValue(m, out bool known)) return known;
            return atlasable[m] = AtlasableNow(m);
        }

        static bool AtlasableNow(Material m)
        {
            if (m.shader == null || m.shader.name != "PSX/Lit") return false;
            if (m.IsKeywordEnabled("PSX_ATLAS_RECT")) return false;
            float cut = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0f;
            if (m.HasProperty("_NightWin") && m.GetFloat("_NightWin") > 0.5f) return false;
            if (cut <= 0f) return true;
            var px = LoadPixels(m.mainTexture);
            if (px == null) return false;
            try
            {
                foreach (var c in px.GetPixels32()) if (c.a < cut * 255f) return false;
                return true;
            }
            finally { Object.DestroyImmediate(px); }
        }

        static TexInfo FindOrAdd(List<TexInfo> list, Material m)
        {
            var tex = m.mainTexture;
            var tint = m.HasProperty("_Color") ? m.color : Color.white;
            foreach (var t in list) if (t.tex == tex && t.tint == tint) return t;
            var n = new TexInfo { tex = tex, tint = tint };
            list.Add(n);
            return n;
        }

        /// <summary>
        /// Cell sizes on a buddy grid of the sheet: every texture 64 px when
        /// that fits (16 cells), halved from the least-covered up while it
        /// does not, then the most-covered raised to 128 while there is room.
        /// </summary>
        static void PlanCells(List<TexInfo> texes)
        {
            texes.Sort((a, b) => b.area.CompareTo(a.area));
            int Units(int s) => (s / 32) * (s / 32);
            const int Cap = (AtlasPx / 32) * (AtlasPx / 32);
            foreach (var t in texes) t.size = 64;
            int total = 0; foreach (var t in texes) total += Units(t.size);
            for (int i = texes.Count - 1; total > Cap && i >= 0; i--)
            {
                total -= Units(texes[i].size) - Units(32);
                texes[i].size = 32;
            }
            if (total > Cap) throw new System.Exception(texes.Count + " textures do not fit one " + AtlasPx + " px sheet");
            for (int i = 0; i < texes.Count && i < 3; i++)
            {
                if (texes[i].size != 64) break;
                int extra = Units(128) - Units(64);
                if (total + extra > Cap) break;
                texes[i].size = 128; total += extra;
            }
            // buddy allocation, largest first
            var free = new List<RectInt> { new RectInt(0, 0, AtlasPx, AtlasPx) };
            var order = new List<TexInfo>(texes);
            order.Sort((a, b) => b.size.CompareTo(a.size));
            foreach (var t in order)
            {
                int best = -1;
                for (int i = 0; i < free.Count; i++)
                    if (free[i].width >= t.size && (best < 0 || free[i].width < free[best].width)) best = i;
                if (best < 0) throw new System.Exception("atlas packing failed");
                var f = free[best]; free.RemoveAt(best);
                while (f.width > t.size)
                {
                    int h = f.width / 2;
                    free.Add(new RectInt(f.x + h, f.y, h, h));
                    free.Add(new RectInt(f.x, f.y + h, h, h));
                    free.Add(new RectInt(f.x + h, f.y + h, h, h));
                    f = new RectInt(f.x, f.y, h, h);
                }
                t.cell = f;
            }
        }

        /// <summary>The sheet from the textures' SOURCE files (read as bytes,
        /// so it works headless), each resampled into its cell with a box
        /// filter that wraps, times its material's tint. Saved beside the
        /// other city art with a point-filtered, mip-less importer, and its
        /// PSX_ATLAS_RECT material.</summary>
        static Material WriteAtlas(string key, List<TexInfo> texes)
        {
            var atlas = new Texture2D(AtlasPx, AtlasPx, TextureFormat.RGB24, false);
            var fill = new Color32[AtlasPx * AtlasPx];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(128, 128, 128, 255);
            atlas.SetPixels32(fill);
            foreach (var t in texes)
            {
                var src = LoadPixels(t.tex);
                int S = t.size;
                int k = src != null ? Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(src.width, src.height) / (float)S), 1, 4) : 1;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        Color acc = Color.black;
                        if (src != null)
                        {
                            for (int j = 0; j < k; j++)
                                for (int i = 0; i < k; i++)
                                    acc += src.GetPixelBilinear((x + (i + 0.5f) / k) / S, (y + (j + 0.5f) / k) / S);
                            acc /= k * k;
                        }
                        else acc = Color.white;
                        var c = acc * t.tint;
                        c.a = 1f;
                        atlas.SetPixel(t.cell.x + x, t.cell.y + y, c);
                    }
                if (src != null) Object.DestroyImmediate(src);
            }
            atlas.Apply();
            string texPath = ArtDir + "/city_" + key + "_atlas.png";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), texPath), atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(texPath) is TextureImporter imp)
            {
                bool dirty = imp.filterMode != FilterMode.Point || imp.mipmapEnabled ||
                             imp.textureCompression != TextureImporterCompression.Uncompressed ||
                             imp.maxTextureSize != AtlasPx || imp.alphaSource != TextureImporterAlphaSource.None;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.maxTextureSize = AtlasPx;
                imp.alphaSource = TextureImporterAlphaSource.None;
                imp.wrapMode = TextureWrapMode.Repeat;
                // THE SAME 16 BITS AS THE TEXTURES IT HOLDS. ReleaseBudget ships
                // every opaque pack texture to WebGL as RGB565, which samples
                // WITHOUT the sRGB decode - that brighter picture is the one
                // the owner signed off. An atlas left at 24-bit sRGB rendered
                // every atlased house a fifth darker than the house it replaced
                // (measured: 0.76-0.81 on the comparison sheet), so the atlas
                // takes the override here, and the editor, the probe and the
                // shipped build all see what ReleaseBudget would make of it.
                var web = imp.GetPlatformTextureSettings("WebGL");
                if (!web.overridden || web.format != TextureImporterFormat.RGB16 || web.maxTextureSize != AtlasPx ||
                    web.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    web.overridden = true;
                    web.format = TextureImporterFormat.RGB16;
                    web.maxTextureSize = AtlasPx;
                    web.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.SetPlatformTextureSettings(web);
                    dirty = true;
                }
                if (dirty) imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string matPath = ArtDir + "/City_" + key + "_atlas.mat";
            var sh = Shader.Find("PSX/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = sh;
            mat.mainTexture = tex;
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            mat.color = Color.white;
            mat.SetFloat("_Cutoff", 0f);
            if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0f);
            if (mat.HasProperty("_Wet")) mat.SetFloat("_Wet", 0f);
            if (mat.HasProperty("_AtlasPx")) mat.SetFloat("_AtlasPx", AtlasPx);
            mat.EnableKeyword("PSX_ATLAS_RECT");
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A texture's pixels from its source file, CPU-side, wrapping;
        /// null when there is no file to read (an untextured material, whose
        /// cell is then its tint).</summary>
        static Texture2D LoadPixels(Texture tex)
        {
            if (tex == null) return null;
            string path = AssetDatabase.GetAssetPath(tex);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return null;
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            if (!File.Exists(full)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(full))) { Object.DestroyImmediate(t); return null; }
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        // ==================================================================
        //  The restaurants: shell merged by material, room behind a switch.
        // ==================================================================
        static void BakeRestaurant(byte kind, List<string> report)
        {
            var go = (GameObject)Object.Instantiate(LoadFull(kind));
            try
            {
                go.transform.SetPositionAndRotation(Far, Quaternion.identity);
                int drawsBefore = DrawsOf(go);
                int collBefore = go.GetComponentsInChildren<Collider>(true).Length;
                int trigBefore = 0;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) trigBefore++;

                var rends = new List<MeshRenderer>();
                int doors = 0;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    // a leaf on a hinge swings: it keeps its own renderer
                    if (r.GetComponentInParent<SwingDoor>(true) != null) { doors++; continue; }
                    rends.Add(r);
                }

                var seen = SeenFromOutside(go, rends);
                var shell = new List<Piece>();
                var room = new List<Renderer>();
                foreach (var r in rends)
                {
                    if (seen.Contains(r))
                        shell.Add(new Piece { r = r, mesh = r.GetComponent<MeshFilter>().sharedMesh, toRoot = go.transform.worldToLocalMatrix * r.transform.localToWorldMatrix, mats = r.sharedMaterials });
                    else room.Add(r);
                }

                var mesh = MergePieces(shell, null, null, null, out var mats);
                mesh.name = NameOf(kind) + "_city";
                SaveMesh(mesh, OutDir + "/" + NameOf(kind) + "_merged.asset");
                foreach (var pc in shell) StripRenderer(pc.r);
                foreach (var r in room) r.enabled = false;
                AddMergedRenderer(go, mesh, mats);
                var sw = go.AddComponent<CityPropInterior>();
                sw.interior = room.ToArray();

                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.name = NameOf(kind);
                int drawsAfter = DrawsOf(go);
                int roomDraws = 0;
                foreach (var r in room) roomDraws += r.sharedMaterials.Length;
                int collAfter = go.GetComponentsInChildren<Collider>(true).Length;
                int trigAfter = 0;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) trigAfter++;
                bool bay = go.GetComponentInChildren<DriveThru>(true) != null;
                PrefabUtility.SaveAsPrefabAsset(go, OutDir + "/" + NameOf(kind) + ".prefab");
                report.Add($"{NameOf(kind)}: {drawsBefore} -> {drawsAfter} draws beyond {CityPropInterior.OnM:0} m " +
                           $"(shell {shell.Count} pieces as {mats.Length} materials, {doors} door leaves on hinges; room {room.Count} renderers / {roomDraws} draws switched on within {CityPropInterior.OnM:0} m), " +
                           $"{mesh.vertexCount} verts, colliders {collBefore} -> {collAfter} (triggers {trigBefore} -> {trigAfter}), order bay {(bay ? "kept" : "MISSING")}");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// The pieces some ray from outside reaches: from 24 bearings at three
        /// heights (kerb, cab, first floor) and a grid from above, to the
        /// centre and eight inset corners of each piece's bounds. A ray that
        /// meets nothing, or meets the piece itself, sees it; one stopped by
        /// another piece's collider does not.
        /// </summary>
        static HashSet<Renderer> SeenFromOutside(GameObject go, List<MeshRenderer> rends)
        {
            Physics.SyncTransforms();
            bool back = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var seen = new HashSet<Renderer>();
            try
            {
                var hull = rends.Count > 0 ? rends[0].bounds : new Bounds(go.transform.position, Vector3.one);
                foreach (var r in rends) hull.Encapsulate(r.bounds);
                var eyes = new List<Vector3>();
                float R = Mathf.Max(hull.extents.x, hull.extents.z) + 12f;
                foreach (float h in new[] { 1.2f, 3.0f, 7.0f })
                    for (int a = 0; a < 24; a++)
                    {
                        float ang = a * Mathf.PI * 2f / 24f;
                        eyes.Add(new Vector3(hull.center.x + Mathf.Cos(ang) * R, hull.min.y + h, hull.center.z + Mathf.Sin(ang) * R));
                    }
                for (int gz = -1; gz <= 1; gz++)
                    for (int gx = -1; gx <= 1; gx++)
                        eyes.Add(new Vector3(hull.center.x + gx * hull.extents.x, hull.max.y + 25f, hull.center.z + gz * hull.extents.z));

                var targets = new Vector3[9];
                foreach (var r in rends)
                {
                    var b = r.bounds;
                    targets[0] = b.center;
                    for (int c = 0; c < 8; c++)
                        targets[c + 1] = b.center + Vector3.Scale(b.extents * 0.85f,
                            new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                    bool vis = false;
                    for (int e = 0; e < eyes.Count && !vis; e++)
                        for (int t = 0; t < targets.Length && !vis; t++)
                        {
                            var d = targets[t] - eyes[e];
                            float len = d.magnitude;
                            if (len < 1e-3f) continue;
                            if (!Physics.Raycast(eyes[e], d / len, out var hit, len - 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                vis = true;
                            else if (hit.collider != null && hit.collider.gameObject == r.gameObject)
                                vis = true;
                        }
                    if (vis) seen.Add(r);
                }
            }
            finally { Physics.queriesHitBackfaces = back; }
            return seen;
        }

        // ==================================================================
        //  Shared: merge, save, stand up.
        // ==================================================================

        /// <summary>
        /// Every submesh of every piece, in the prop root's space, into one
        /// mesh: the atlased ones (when an atlas is given) as ONE submesh in
        /// the atlas material, their UV carrying the source material's tiling
        /// and their colour the cell; every other material as a submesh of
        /// its own, merged across pieces.
        /// </summary>
        static Mesh MergePieces(List<Piece> pieces, List<TexInfo> texes, Material atlasMat,
                                List<Material> keptApart, out Material[] mats)
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var C = new List<Color32>();
            var subOf = new Dictionary<Material, List<int>>();
            var order = new List<Material>();
            List<int> Sub(Material m)
            {
                if (!subOf.TryGetValue(m, out var l)) { subOf[m] = l = new List<int>(); order.Add(m); }
                return l;
            }

            foreach (var pc in pieces)
            {
                using (var arr = Mesh.AcquireReadOnlyMeshData(pc.mesh))
                {
                    var d = arr[0];
                    int vc = d.vertexCount;
                    var pv = new NativeArray<Vector3>(vc, Allocator.Temp);
                    var pn = new NativeArray<Vector3>(vc, Allocator.Temp);
                    var pu = new NativeArray<Vector2>(vc, Allocator.Temp);
                    d.GetVertices(pv);
                    bool hasN = d.HasVertexAttribute(VertexAttribute.Normal);
                    bool hasU = d.HasVertexAttribute(VertexAttribute.TexCoord0);
                    if (hasN) d.GetNormals(pn);
                    if (hasU) d.GetUVs(0, pu);
                    var M = pc.toRoot;
                    var NM = M.inverse.transpose;
                    bool flip = M.determinant < 0f;
                    int subs = Mathf.Min(d.subMeshCount, pc.mats.Length);
                    for (int s = 0; s < subs; s++)
                    {
                        var desc = d.GetSubMesh(s);
                        if (desc.topology != MeshTopology.Triangles || desc.indexCount == 0) continue;
                        var m = pc.mats[s];
                        if (m == null) continue;
                        TexInfo cell = null;
                        if (texes != null && Atlasable(m))
                        {
                            var tint = m.HasProperty("_Color") ? m.color : Color.white;
                            foreach (var t in texes) if (t.tex == m.mainTexture && t.tint == tint) { cell = t; break; }
                        }
                        var target = cell != null ? Sub(atlasMat) : Sub(m);
                        if (cell == null && texes != null && keptApart != null && !keptApart.Contains(m)) keptApart.Add(m);
                        Vector2 st = m.mainTextureScale, so = m.mainTextureOffset;
                        var col = cell != null
                            ? new Color32((byte)cell.cell.x, (byte)cell.cell.y, (byte)(cell.cell.width - 1), 255)
                            : new Color32(255, 255, 255, 255);

                        var idx = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                        d.GetIndices(idx, s);
                        var remap = new Dictionary<int, int>();
                        for (int k = 0; k < idx.Length; k += 3)
                        {
                            int a = idx[k], b = idx[k + 1], c = idx[k + 2];
                            if (flip) { int tmp = b; b = c; c = tmp; }
                            foreach (int vi in new[] { a, b, c })
                            {
                                if (!remap.TryGetValue(vi, out int ni))
                                {
                                    ni = V.Count;
                                    remap[vi] = ni;
                                    V.Add(M.MultiplyPoint3x4(pv[vi]));
                                    var n = hasN ? NM.MultiplyVector(pn[vi]) : Vector3.up;
                                    N.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
                                    var uv = hasU ? pu[vi] : Vector2.zero;
                                    U.Add(new Vector2(uv.x * st.x + so.x, uv.y * st.y + so.y));
                                    C.Add(col);
                                }
                                target.Add(ni);
                            }
                        }
                        idx.Dispose();
                    }
                    pv.Dispose(); pn.Dispose(); pu.Dispose();
                }
            }

            var mesh = new Mesh { indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetUVs(0, U);
            if (atlasMat != null) mesh.SetColors(C);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(subOf[order[i]], i);
            mesh.RecalculateBounds();
            mats = order.ToArray();
            return mesh;
        }

        static float SurfaceArea(Mesh mesh, int sub, Matrix4x4 M)
        {
            float area = 0f;
            using (var arr = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var d = arr[0];
                var pv = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
                d.GetVertices(pv);
                var desc = d.GetSubMesh(sub);
                if (desc.topology == MeshTopology.Triangles && desc.indexCount > 0)
                {
                    var idx = new NativeArray<int>(desc.indexCount, Allocator.Temp);
                    d.GetIndices(idx, sub);
                    for (int k = 0; k + 2 < idx.Length; k += 3)
                    {
                        Vector3 a = M.MultiplyPoint3x4(pv[idx[k]]), b = M.MultiplyPoint3x4(pv[idx[k + 1]]), c = M.MultiplyPoint3x4(pv[idx[k + 2]]);
                        area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    }
                    idx.Dispose();
                }
                pv.Dispose();
            }
            return area;
        }

        /// <summary>The piece's drawing goes (the merged mesh draws it); its
        /// GameObject, its transform and its collider stay.</summary>
        static void StripRenderer(MeshRenderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            Object.DestroyImmediate(r);
            if (mf != null) Object.DestroyImmediate(mf);
        }

        static void AddMergedRenderer(GameObject root, Mesh mesh, Material[] mats)
        {
            var g = new GameObject("CityMerged");
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Draws the prop costs as it stands: a submesh per enabled renderer.</summary>
        public static int DrawsOf(GameObject go)
        {
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(false))
                if (r.enabled) n += r.sharedMaterials.Length;
            return n;
        }

        /// <summary>Saved as an asset beside the prefab. The restaurants' shells
        /// are quantised (Medium, as the builder does for everything nobody
        /// drives on - a 36 m lot's positions land on half a millimetre).
        /// NEVER a mesh that carries atlas cells in its colours: Medium
        /// quantises colours to SIX bits (read back: a cell at texel 64 came
        /// out at 65, 192 at 190), which moves every cell off its texels and
        /// bleeds the neighbours in along every repeat - black seams down a
        /// house's foundation, the first render of this bake.</summary>
        static void SaveMesh(Mesh m, string path)
        {
            if (m.colors32 == null || m.colors32.Length == 0)
                MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        // ==================================================================
        //  The look check: full prefab beside its city variant.
        // ==================================================================

        /// <summary>
        /// Screenshots/City/props_compare.png: every prop with a variant, the
        /// FULL prefab and the CITY variant side by side, from two corners at
        /// kerb-side distance, under the preview's daylight. The restaurants'
        /// variant is shot with its room OFF (its state beyond 40 m) from
        /// inside that distance, the hardest case: anything visible from
        /// outside that the switch took away shows here. Headless:
        /// -executeMethod PSXRacing.EditorTools.CityPropBaker.CompareShots.
        /// </summary>
        [MenuItem("PSX Racing/City Prop Variants - Compare Shots")]
        public static void CompareShots()
        {
            PSXRacingBuilder.EnsureCityTextures();
            Shader.SetGlobalFloat("_PSXFogNear", 900f);
            Shader.SetGlobalFloat("_PSXFogFar", 2000f);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            const int W = 480, H = 300;
            var rows = new List<byte>();
            foreach (var kv in CityProps.Defs) if (CityProps.HasCityVariant(kv.Key)) rows.Add(kv.Key);
            var sheet = new Texture2D(W * 4, H * rows.Count, TextureFormat.RGB24, false);
            var cam = new GameObject("~propCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f; cam.fieldOfView = 50f;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
            var lines = new List<string>();
            try
            {
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    byte kind = rows[ri];
                    var def = CityProps.Defs[kind];
                    var full = (GameObject)Object.Instantiate(LoadFull(kind));
                    var city = AssetDatabase.LoadAssetAtPath<GameObject>(OutDir + "/" + NameOf(kind) + ".prefab");
                    var cityGo = city != null ? (GameObject)Object.Instantiate(city) : null;
                    try
                    {
                        foreach (var g in new[] { full, cityGo })
                            if (g != null) g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, def.yawOffsetDeg, 0f));
                        var b = new Bounds(Vector3.up * def.h * 0.4f, new Vector3(def.w, def.h, def.d));
                        float dist = Mathf.Max(def.w, def.d) * 1.25f + 8f;
                        for (int col = 0; col < 4; col++)
                        {
                            bool showCity = (col & 1) == 1;
                            if (full != null) full.SetActive(!showCity);
                            if (cityGo != null) cityGo.SetActive(showCity);
                            // the front (+Z after the yaw) from two corners
                            float az = (col < 2 ? 35f : -35f) * Mathf.Deg2Rad;
                            var eye = b.center + new Vector3(Mathf.Sin(az) * dist, 0f, Mathf.Cos(az) * dist);
                            eye.y = 3f;
                            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(b.center - eye, Vector3.up));
                            cam.Render();
                            RenderTexture.active = rt;
                            shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                            shot.Apply();
                            RenderTexture.active = null;
                            sheet.SetPixels(col * W, (rows.Count - 1 - ri) * H, W, H, shot.GetPixels());
                        }
                        lines.Add($"{NameOf(kind)}: full {DrawsOf(full)} draws | city {(cityGo != null ? DrawsOf(cityGo).ToString() : "missing")} draws, shot from {dist:0} m");
                    }
                    finally
                    {
                        Object.DestroyImmediate(full);
                        if (cityGo != null) Object.DestroyImmediate(cityGo);
                    }
                }
                sheet.Apply();
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "City");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "props_compare.png"), sheet.EncodeToPNG());
                lines.Add("columns: FULL, CITY (front-right corner), FULL, CITY (front-left corner); rows: " + string.Join(", ", rows.ConvertAll(k => NameOf(k))));
                File.WriteAllLines(Path.Combine(dir, "props_compare.txt"), lines);
                foreach (var l in lines) Debug.Log("[CityProps] " + l);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(shot);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
