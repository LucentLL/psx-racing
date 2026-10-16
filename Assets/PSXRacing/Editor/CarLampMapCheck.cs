using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAMP CONTROL MAPS (the owner's authored running/brake lamps,
    /// 2026-10-15), checked off the files and the meshes, no rendering:
    ///   * every map is Resources/CarLights/&lt;modelKey&gt;_lightmask_rg.png
    ///     for a model the library knows, imported LINEAR, point, clamp, no
    ///     mips, uncompressed, and the size of that model's atlas;
    ///   * B is 0 and A is 255 everywhere (R running, G brake, nothing else);
    ///   * every lit texel lands only on the TAIL of its own body mesh, on
    ///     faces looking back - and the R32's lands on exactly its four round
    ///     lamps, two a side;
    ///   * the runtime loader agrees with the disk (a model without a file
    ///     gets no map), and PSX/CarPaint skips the lamp block unless an input
    ///     is set: a car without a map renders as it always did.
    /// Called by LifeSimSelfTest.TestLampControlMaps and by R32LightShots.
    /// </summary>
    public static class CarLampMapCheck
    {
        public const string Dir = "Assets/PSXRacing/Resources/CarLights";
        const string ShaderPath = "Assets/PSXRacing/Shaders/PSXCarPaint.shader";
        /// <summary>Lamps the owner authored per model (round lamps on the
        /// tail), where the count is known.</summary>
        static readonly Dictionary<string, int> LampsPerModel = new Dictionary<string, int> { { "gtr_89", 4 } };

        public static void Run(System.Action<bool, string, object> check)
        {
            string shader = File.Exists(ShaderPath) ? File.ReadAllText(ShaderPath) : "";
            check(Regex.IsMatch(shader, @"if \(_RunningLights > 0\.001 \|\| _Brake > 0\.001\)"),
                  "PSX/CarPaint lights the lamp map only when a lamp input is set (a car without a map is untouched)", null);

            var files = Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.png") : new string[0];
            check(files.Length > 0, "at least one lamp control map ships (gtr_89)", files.Length);
            var keysWithMap = new HashSet<string>();
            foreach (var full in files)
            {
                string path = full.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                bool named = name.EndsWith(CarLights.LampMapSuffix);
                string key = named ? name.Substring(0, name.Length - CarLights.LampMapSuffix.Length) : name;
                var def = named ? CarModelLibrary.Load(key) : null;
                check(def != null, $"{name}.png names a model (<modelKey>{CarLights.LampMapSuffix}.png)", key);
                if (def == null) continue;
                keysWithMap.Add(key);

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                bool importOk = imp != null && !imp.sRGBTexture && !imp.mipmapEnabled &&
                                imp.filterMode == FilterMode.Point && imp.wrapMode == TextureWrapMode.Clamp &&
                                imp.textureCompression == TextureImporterCompression.Uncompressed;
                check(importOk, $"{key}: map imported linear, point, clamp, no mips, uncompressed",
                      imp == null ? "no importer" : $"sRGB {imp.sRGBTexture} mips {imp.mipmapEnabled} {imp.filterMode} {imp.wrapMode} {imp.textureCompression}");

                var png = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                png.LoadImage(File.ReadAllBytes(path));
                var atlas = def.SkinCount > 0 && def.skinMaterials[0] != null ? def.skinMaterials[0].mainTexture : null;
                check(atlas == null || (atlas.width == png.width && atlas.height == png.height),
                      $"{key}: map is the atlas's size", $"{png.width}x{png.height} vs {(atlas != null ? atlas.width + "x" + atlas.height : "-")}");
                var px = png.GetPixels32();
                var lit = new List<Vector2>();
                int stray = 0;
                for (int y = 0; y < png.height; y++)
                    for (int x = 0; x < png.width; x++)
                    {
                        var c = px[y * png.width + x];
                        if (c.b != 0 || c.a != 255) stray++;
                        if (c.r > 0 || c.g > 0) lit.Add(new Vector2((x + 0.5f) / png.width, (y + 0.5f) / png.height));
                    }
                check(stray == 0, $"{key}: B is 0 and A is 255 everywhere (R running, G brake)", stray);
                check(lit.Count > 0, $"{key}: the map lights something", lit.Count);
                Object.DestroyImmediate(png);
                CheckCoverage(def, key, lit, check);
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
        }

        /// <summary>Where the lit texels land on the body: every triangle whose
        /// UV footprint holds one must be on the tail and face back; lamps =
        /// clusters of those triangles in the car's frame.</summary>
        static void CheckCoverage(CarModelDef def, string key, List<Vector2> lit, System.Action<bool, string, object> check)
        {
            var mesh = def.bodyMesh;
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
