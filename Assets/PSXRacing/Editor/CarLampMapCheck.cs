using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAMP CONTROL MAPS (the owner's authored lamps, 2026-10-15/16),
    /// checked off the files and the meshes, no rendering:
    ///   * every map is Resources/CarLights/&lt;modelKey&gt;_lightmask.png (RGBA:
    ///     R running, G brake, B projector core, A lens scatter) or the legacy
    ///     &lt;modelKey&gt;_lightmask_rg.png (R/G only), for a model the library
    ///     knows, imported LINEAR, point, clamp, no mips, uncompressed (an RGBA
    ///     map keeps its alpha as DATA: from input, not transparency), and the
    ///     size of that model's atlas;
    ///   * a legacy map: B is 0 and A is 255 everywhere;
    ///   * every R/G texel lands only on the TAIL of its own body mesh, on
    ///     faces looking back - and the R32's lands on exactly its four round
    ///     lamps, two a side; every B/A texel only on the NOSE, on faces
    ///     looking forward, a lamp a side;
    ///   * a shell's LENS VARIANT (the clear covers, B): all three meshes, the
    ///     body the A body plus its cavity walls, both covers inside the A
    ///     body and on lamp texels of the map;
    ///   * the runtime loader agrees with the disk (a model without a file
    ///     gets no map), and PSX/CarPaint skips the lamp block unless an input
    ///     is set: a car without a map renders as it always did.
    /// Called by LifeSimSelfTest.TestLampControlMaps and by R32LightShots.
    /// </summary>
    public static class CarLampMapCheck
    {
        public const string Dir = "Assets/PSXRacing/Resources/CarLights";
        const string ShaderPath = "Assets/PSXRacing/Shaders/PSXCarPaint.shader";
        const string LensShaderPath = "Assets/PSXRacing/Shaders/PSXCarLens.shader";
        /// <summary>Lamps the owner authored per model (round lamps on the
        /// tail), where the count is known.</summary>
        static readonly Dictionary<string, int> LampsPerModel = new Dictionary<string, int> { { "gtr_89", 4 } };

        public static void Run(System.Action<bool, string, object> check)
        {
            string shader = File.Exists(ShaderPath) ? File.ReadAllText(ShaderPath) : "";
            check(Regex.IsMatch(shader, @"if \(_RunningLights > 0\.001 \|\| _Brake > 0\.001\)"),
                  "PSX/CarPaint lights the lamp map only when a lamp input is set (a car without a map is untouched)", null);
            string lens = File.Exists(LensShaderPath) ? File.ReadAllText(LensShaderPath) : "";
            check(lens.Contains("Blend One OneMinusSrcAlpha, Zero One") && lens.Contains("ZWrite Off") &&
                  lens.Contains("Cull Back") && lens.Contains("LENS_REAR_SHARE") && lens.Contains("LENS_HEAD_SHARE"),
                  "PSX/CarLens: premultiplied, keeps the emitter mask, no depth write, outward faces, emits only its share", null);

            var files = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.png") : new string[0];
            check(files.Length > 0, "at least one lamp control map ships (gtr_89)", files.Length);
            var keysWithMap = new HashSet<string>();
            var keysWithHead = new HashSet<string>();
            foreach (var full in files)
            {
                string path = full.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                bool legacy = name.EndsWith(CarLights.LegacyLampMapSuffix);
                bool named = legacy || name.EndsWith(CarLights.LampMapSuffix);
                string suffix = legacy ? CarLights.LegacyLampMapSuffix : CarLights.LampMapSuffix;
                string key = named ? name.Substring(0, name.Length - suffix.Length) : name;
                var def = named ? CarModelLibrary.Load(key) : null;
                check(def != null && def.key == key, $"{name}.png names a model (<modelKey>{CarLights.LampMapSuffix}.png or {CarLights.LegacyLampMapSuffix}.png)", key);
                if (def == null || def.key != key) continue;
                check(keysWithMap.Add(key), $"{key}: one map per model (not both conventions)", null);
                if (!legacy) keysWithHead.Add(key);

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                bool importOk = imp != null && !imp.sRGBTexture && !imp.mipmapEnabled &&
                                imp.filterMode == FilterMode.Point && imp.wrapMode == TextureWrapMode.Clamp &&
                                imp.textureCompression == TextureImporterCompression.Uncompressed &&
                                (legacy || (imp.alphaSource == TextureImporterAlphaSource.FromInput && !imp.alphaIsTransparency));
                check(importOk, $"{key}: map imported linear, point, clamp, no mips, uncompressed" + (legacy ? "" : ", alpha kept as data"),
                      imp == null ? "no importer" : $"sRGB {imp.sRGBTexture} mips {imp.mipmapEnabled} {imp.filterMode} {imp.wrapMode} {imp.textureCompression} alpha {imp.alphaSource}/{imp.alphaIsTransparency}");

                var png = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                png.LoadImage(File.ReadAllBytes(path));
                var atlas = def.SkinCount > 0 && def.skinMaterials[0] != null ? def.skinMaterials[0].mainTexture : null;
                check(atlas == null || (atlas.width == png.width && atlas.height == png.height),
                      $"{key}: map is the atlas's size", $"{png.width}x{png.height} vs {(atlas != null ? atlas.width + "x" + atlas.height : "-")}");
                var px = png.GetPixels32();
                var lit = new List<Vector2>();
                var head = new List<Vector2>();
                int stray = 0, core = 0, scatter = 0;
                for (int y = 0; y < png.height; y++)
                    for (int x = 0; x < png.width; x++)
                    {
                        var c = px[y * png.width + x];
                        var at = new Vector2((x + 0.5f) / png.width, (y + 0.5f) / png.height);
                        if (legacy) { if (c.b != 0 || c.a != 255) stray++; }
                        else if (c.b > 0 || c.a > 0)
                        {
                            head.Add(at);
                            if (c.b > 0) core++;
                            if (c.a > 0) scatter++;
                        }
                        if (c.r > 0 || c.g > 0) lit.Add(at);
                    }
                if (legacy) check(stray == 0, $"{key}: B is 0 and A is 255 everywhere (R running, G brake)", stray);
                else check(core > 0 && scatter > 0, $"{key}: the RGBA map has a projector core (B) and a lens scatter (A)", $"{core} core, {scatter} scatter texels");
                check(lit.Count > 0, $"{key}: the map lights something", lit.Count);
                Object.DestroyImmediate(png);
                CheckCoverage(def, def.bodyMesh, key, lit, check);
                if (!legacy) CheckHead(def, key, head, check);
                if (def.HasLensVariant) CheckLensVariant(def, key, lit, head, check);
            }

            // The loader's convention, from the game's side.
            int wrong = 0;
            string sample = "";
            foreach (var m in CarModelLibrary.Models)
            {
                bool has = CarLights.LampMapFor(m.key) != null;
                if (has != keysWithMap.Contains(m.key)) { wrong++; sample = m.key; }
            }
            check(wrong == 0, "CarLights.LampMapFor finds exactly the models with a map file", wrong > 0 ? sample : null);
            int wrongHead = 0;
            foreach (var m in CarModelLibrary.Models)
                if (CarLights.LampMapHasHead(m.key) != keysWithHead.Contains(m.key)) { wrongHead++; sample = m.key; }
            check(wrongHead == 0, "...and reads the headlamps (B/A) only off an RGBA map, never a legacy R/G one", wrongHead > 0 ? sample : null);
        }

        static Matrix4x4 ToCar(CarModelDef def) =>
            Matrix4x4.TRS(new Vector3(0f, def.bodyYOffset, def.bodyZOffset), Quaternion.Euler(0f, def.bodyYaw, 0f), Vector3.one);

        /// <summary>The headlamp channels (B core, A scatter): every triangle
        /// holding one is on the NOSE (front fifth) and faces forward, and the
        /// texels make one lamp a side.</summary>
        static void CheckHead(CarModelDef def, string key, List<Vector2> head, System.Action<bool, string, object> check)
        {
            var mesh = def.bodyMesh;
            var v = mesh.vertices;
            var uv = mesh.uv;
            var nrm = mesh.normals;
            var tris = mesh.triangles;
            var toCar = ToCar(def);
            var cv = new Vector3[v.Length];
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < v.Length; i++) { cv[i] = toCar.MultiplyPoint3x4(v[i]); minZ = Mathf.Min(minZ, cv[i].z); maxZ = Mathf.Max(maxZ, cv[i].z); }
            float len = maxZ - minZ;
            var hit = new HashSet<int>();
            foreach (var p in head)
                for (int t = 0; t < tris.Length; t += 3)
                    if (InTri(p, uv[tris[t]], uv[tris[t + 1]], uv[tris[t + 2]])) hit.Add(t);
            int back = 0, rearward = 0, left = 0, right = 0;
            foreach (int t in hit)
            {
                Vector3 a = cv[tris[t]], b = cv[tris[t + 1]], c = cv[tris[t + 2]];
                Vector3 centre = (a + b + c) / 3f;
                Vector3 n = nrm != null && nrm.Length == v.Length
                    ? toCar.MultiplyVector(nrm[tris[t]] + nrm[tris[t + 1]] + nrm[tris[t + 2]]).normalized
                    : Vector3.Cross(b - a, c - a).normalized;
                if (centre.z < maxZ - 0.2f * len) back++;
                if (n.z <= 0f) rearward++;
                if (centre.x < 0f) left++; else right++;
            }
            check(hit.Count > 0 && back == 0, $"{key}: every headlamp texel (B/A) lands on the nose (front fifth)", $"{hit.Count} tris, {back} behind it");
            check(rearward == 0, $"{key}: ...on faces that look forward", rearward);
            check(left > 0 && right > 0, $"{key}: ...on both headlamps", $"{left} left / {right} right tris");
        }

        /// <summary>A shell's lens variant (B): the body is A plus its cavity
        /// walls, the covers sit inside A's box, and each cover is UV'd onto
        /// its own lamps' texels (tail covers on R/G, head covers on B/A).</summary>
        static void CheckLensVariant(CarModelDef def, string key, List<Vector2> rear, List<Vector2> head, System.Action<bool, string, object> check)
        {
            int aTris = def.bodyMesh.triangles.Length / 3, bTris = def.lensBodyMesh.triangles.Length / 3;
            int hTris = def.lensHeadMesh.triangles.Length / 3, tTris = def.lensTailMesh.triangles.Length / 3;
            check(bTris > aTris && bTris - aTris < 400, $"{key}: lens body = the A body plus its cavity walls", $"A {aTris}, B {bTris} (+{bTris - aTris}), covers {hTris} + {tTris}");
            var box = def.bodyMesh.bounds;
            box.Expand(0.01f);
            int outside = 0;
            foreach (var m in new[] { def.lensBodyMesh, def.lensHeadMesh, def.lensTailMesh })
                foreach (var p in m.vertices) if (!box.Contains(p)) outside++;
            check(outside == 0, $"{key}: the lens body and both covers sit inside the A body's box (same frame, same scale)", outside);
            int OnTexels(Mesh m, List<Vector2> texels)
            {
                var uv = m.uv; var tris = m.triangles; int n = 0;
                foreach (var p in texels)
                    for (int t = 0; t < tris.Length; t += 3)
                        if (InTri(p, uv[tris[t]], uv[tris[t + 1]], uv[tris[t + 2]])) { n++; break; }
                return n;
            }
            int tailOn = OnTexels(def.lensTailMesh, rear), headOn = OnTexels(def.lensHeadMesh, head);
            check(tailOn > 0 && headOn > 0, $"{key}: tail covers lie on R/G texels, head covers on B/A texels", $"tail {tailOn}/{rear.Count}, head {headOn}/{head.Count}");
            var hm = def.lensHeadMaterial; var tm = def.lensTailMaterial;
            check(hm != null && tm != null && hm.shader != null && hm.shader.name == "PSX/CarLens" && tm.shader == hm.shader &&
                  Mathf.Abs(hm.GetFloat("_Opacity") - 0.06f) < 1e-4f && Mathf.Abs(tm.GetFloat("_Opacity") - 0.09f) < 1e-4f &&
                  Mathf.Abs(hm.GetFloat("_Smoothness") - 0.9f) < 1e-4f,
                  $"{key}: covers wear PSX/CarLens at his numbers (opacity 0.06 / 0.09, smoothness 0.90)",
                  hm != null && tm != null ? $"{hm.shader?.name} {hm.GetFloat("_Opacity")} / {tm.GetFloat("_Opacity")}" : "missing");
        }

        /// <summary>Where the lit texels land on the body: every triangle whose
        /// UV footprint holds one must be on the tail and face back; lamps =
        /// clusters of those triangles in the car's frame.</summary>
        static void CheckCoverage(CarModelDef def, Mesh mesh, string key, List<Vector2> lit, System.Action<bool, string, object> check)
        {
            if (mesh == null) { check(false, $"{key}: has a body mesh to check the map against", null); return; }
            var v = mesh.vertices;
            var nrm = mesh.normals;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var toCar = Matrix4x4.TRS(new Vector3(0f, def.bodyYOffset, def.bodyZOffset),
                                      Quaternion.Euler(0f, def.bodyYaw, 0f), Vector3.one);
            float minZ = float.MaxValue, maxZ = float.MinValue;
            Vector2 uvMin = Vector2.positiveInfinity, uvMax = Vector2.negativeInfinity;
            var cv = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                cv[i] = toCar.MultiplyPoint3x4(v[i]);
                minZ = Mathf.Min(minZ, cv[i].z); maxZ = Mathf.Max(maxZ, cv[i].z);
                uvMin = Vector2.Min(uvMin, uv[i]); uvMax = Vector2.Max(uvMax, uv[i]);
            }
            check(uvMin.x > -0.001f && uvMin.y > -0.001f && uvMax.x < 1.001f && uvMax.y < 1.001f,
                  $"{key}: body UVs inside 0..1 (the map clamps where the atlas repeats)", $"{uvMin}..{uvMax}");
            float len = maxZ - minZ;
            var hit = new HashSet<int>();
            int unmapped = 0;
            foreach (var p in lit)
            {
                bool any = false;
                for (int t = 0; t < tris.Length; t += 3)
                    if (InTri(p, uv[tris[t]], uv[tris[t + 1]], uv[tris[t + 2]])) { hit.Add(t); any = true; }
                if (!any) unmapped++;
            }
            int front = 0, sideways = 0;
            var centres = new List<Vector3>();
            foreach (int t in hit)
            {
                Vector3 a = cv[tris[t]], b = cv[tris[t + 1]], c = cv[tris[t + 2]];
                Vector3 centre = (a + b + c) / 3f;
                // The mesh's own normals (the exported ones), into the car's frame.
                Vector3 n = nrm != null && nrm.Length == v.Length
                    ? toCar.MultiplyVector(nrm[tris[t]] + nrm[tris[t + 1]] + nrm[tris[t + 2]]).normalized
                    : Vector3.Cross(b - a, c - a).normalized;
                if (centre.z > minZ + 0.2f * len) front++;
                if (n.z >= 0f) sideways++;
                centres.Add(centre);
            }
            check(hit.Count > 0 && front == 0, $"{key}: every lit texel lands on the tail (rear fifth of the body)",
                  $"{hit.Count} tris, {front} forward, {unmapped} texels on no face");
            check(sideways == 0, $"{key}: ...on faces that look back", sideways);
            // Lamps = triangles closer than 6 cm, joined.
            var lamps = new List<List<Vector3>>();
            foreach (var c in centres)
            {
                List<Vector3> into = null;
                for (int i = lamps.Count - 1; i >= 0; i--)
                {
                    bool near = false;
                    foreach (var q in lamps[i]) if ((q - c).sqrMagnitude < 0.06f * 0.06f) { near = true; break; }
                    if (!near) continue;
                    if (into == null) { into = lamps[i]; into.Add(c); }
                    else { into.AddRange(lamps[i]); lamps.RemoveAt(i); }
                }
                if (into == null) lamps.Add(new List<Vector3> { c });
            }
            int left = 0, right = 0;
            var where = new List<string>();
            foreach (var l in lamps)
            {
                Vector3 m = Vector3.zero;
                foreach (var q in l) m += q;
                m /= l.Count;
                if (m.x < 0f) left++; else right++;
                where.Add($"({m.x:0.00},{m.y:0.00},{m.z:0.00})");
            }
            string got = $"{lamps.Count} lamps, {left} left / {right} right: " + string.Join(" ", where);
            if (LampsPerModel.TryGetValue(key, out int want))
                check(lamps.Count == want && left == right, $"{key}: the map lights its {want} tail lamps and nothing else", got);
            else
                check(lamps.Count > 0 && left == right, $"{key}: the map's lamps come in pairs", got);
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-12f) return false;
            float l1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            float l2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            float l3 = 1f - l1 - l2;
            return l1 >= -1e-5f && l2 >= -1e-5f && l3 >= -1e-5f;
        }
    }
}
