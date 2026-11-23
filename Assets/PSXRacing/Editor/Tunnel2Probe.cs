using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TUNNEL GAP PROBE AND STRETCH CHECK (tunnel2, owner 2026-11-23 on PC60: "I see a lot of gaps around the
    /// tunnel remodel. I also see stretched textures on the banks. Textures should never be stretched to noticeable
    /// extent."). Both run in edit mode on a built stage scene (StageLab calls them after the roadside checks;
    /// <see cref="Run"/> alone on the scene as it stands).
    ///
    /// GAPS: a ring of cameras the game can put there - chase (6 m behind, 2 m up) and cockpit (1.15 m) in each
    /// lane, both directions, every few metres from 150 m out to 30 m into each portal and from 30 m inside each
    /// exit to 60 m out of it, and replay cameras 5 m up beside the road every 30 m - each rendered with NO sky
    /// over a loud magenta background. A magenta pixel is a GAP where the eye sees through the world: below the
    /// horizon; or, above it, along a ray that enters a bore's mouth and does not leave by the other mouth (a
    /// camera outside), or that leaves the bore by neither mouth (a camera inside). Zero is the pass. The worst
    /// frames are kept as PNGs beside the project (T2Probe/).
    ///
    /// STRETCH: for every drawn mesh near a tunnel (and, listed apart, over the whole stage), each triangle's
    /// texture mapping as the shader samples it - the mesh UV, or for PSX_ROCKFACE the world planes the shader
    /// projects on - as UV per metre along the triangle's own two principal directions: ANISOTROPY = the larger
    /// over the smaller (a stretch; fail past 1.5:1 over more than 2 % of a mesh's area), DENSITY = texels a metre
    /// (texture width x the geometric mean), compared with the surface it meets (the ground chunks' median for a
    /// ground-material mesh, the road cut's rock for a rock one; fail past 30 %).
    /// </summary>
    public static class Tunnel2Probe
    {
        static string Env(string n, string d) { var v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        static void L(StringBuilder sb, string s) { sb.AppendLine(s); Debug.Log("[T2] " + s); }

        /// <summary>Batch entry: PSX_T2_VENUE (default LittleSwitzerland), the scene as built; writes
        /// PSXRacing_tunnel2_probe.txt. PSX_T2_TAG names the PNG folder.</summary>
        public static void Run()
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                string venue = Env("PSX_T2_VENUE", "LittleSwitzerland");
                if (!TrackCatalog.TryIndexOf(venue, out int v)) { L(sb, "FAIL no venue " + venue); code = 1; }
                else
                {
                    var def = TrackCatalog.At(v);
                    int s = TrackCatalog.SceneIndex(v);
                    var scenes = EditorBuildSettings.scenes;
                    string path = s >= 0 && s < scenes.Length ? scenes[s].path : null;
                    if (path == null || !File.Exists(path)) { L(sb, "FAIL no scene for " + venue); code = 1; }
                    else
                    {
                        sb.AppendLine(Gaps(def, path, Env("PSX_T2_TAG", "probe"), out int gapFails));
                        sb.AppendLine(Stretch(def, path, true, out int strFails));
                        if (gapFails > 0 || strFails > 0) code = 1;
                    }
                }
            }
            catch (System.Exception e) { L(sb, "THREW " + e); code = 2; }
            File.WriteAllText(Path.Combine(ProjectDir, "PSXRacing_tunnel2_probe.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------
        //  Gaps
        // ------------------------------------------------------------------
        struct Mouth { public Vector3 c, right, fwd; }   // fwd: along the path (into the bore at the entry)

        /// <summary>The self-test's gap probe (see the class).</summary>
        public static string Gaps(TrackCatalog.TrackDef def, string scenePath, string tag, out int failCams)
        {
            var sb = new StringBuilder();
            failCams = 0;
            if (def != null) TrackCatalog.EnsureStage(def);
            if (def == null || def.tunnels == null || def.tunnels.Length == 0) { L(sb, "tunnel gap probe: no tunnels"); return sb.ToString(); }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { L(sb, "tunnel gap probe: NOT RUN (no graphics device: run without -nographics)"); return sb.ToString(); }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            TrackCatalog.EnsureStage(def);
            var pts = def.stagePts;
            int n = pts.Length;
            float sp = TrackCatalog.Spacing;
            // no sky: every renderer of the sky, clouds and sun off for the probe
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                string sh = m != null && m.shader != null ? m.shader.name : "";
                string nm = r.gameObject.name;
                if (sh.Contains("Sky") || sh.Contains("Cloud") || sh.Contains("SunGlare") || nm.Contains("Sky") || nm.Contains("Cloud"))
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            }
            var keepSky = RenderSettings.skybox; RenderSettings.skybox = null;
            const int W = 640, H = 360;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            var go = new GameObject("T2GapCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 6000f;
            cam.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            string outDir = Path.Combine(ProjectDir, "T2Probe", tag);
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch (System.Exception) { }
            Directory.CreateDirectory(outDir);

            Vector3 Fwd(int i) { var f = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]; f.y = 0f; return f.normalized; }
            float R = def.roadWidth * 0.5f + 1.1f;   // the bore's half-width (PSXRacingBuilder TunnelWallOut)
            var results = new List<(string name, int gaps, Vector2 at, Texture2D shot)>();
            int cams = 0, totalGap = 0;
            int no = 0;
            foreach (var span in def.tunnels)
            {
                int iA = Mathf.Clamp(Mathf.RoundToInt(span.x / sp), 1, n - 2), iB = Mathf.Clamp(Mathf.RoundToInt(span.y / sp), 1, n - 2);
                var mouths = new[] { new Mouth { c = pts[iA], fwd = Fwd(iA) }, new Mouth { c = pts[iB], fwd = Fwd(iB) } };
                for (int k = 0; k < 2; k++) mouths[k].right = Vector3.Cross(Vector3.up, mouths[k].fwd).normalized;
                bool InOpening(Mouth mo, Vector3 o, Vector3 d)
                {
                    float den = Vector3.Dot(d, mo.fwd);
                    if (Mathf.Abs(den) < 1e-5f) return false;
                    float t = Vector3.Dot(mo.c - o, mo.fwd) / den;
                    if (t <= 0f) return false;
                    Vector3 q = o + d * t - mo.c;
                    float x = Vector3.Dot(q, mo.right), y = q.y;
                    if (Mathf.Abs(x) > R + 0.25f || y < -1.5f) return false;
                    return y < 1.6f + Mathf.Sqrt(Mathf.Max(0f, (R + 0.25f) * (R + 0.25f) - x * x)) + 0.25f;
                }
                float lenM = span.y - span.x;
                foreach (int dir in new[] { 1, -1 })
                {
                    // metres along the travel from the entry mouth of this direction
                    float entryM = dir > 0 ? span.x : span.y, exitM = dir > 0 ? span.y : span.x;
                    var marks = new List<float>();
                    for (float d = -150f; d < -60f; d += 15f) marks.Add(d);
                    for (float d = -60f; d < -10f; d += 5f) marks.Add(d);
                    for (float d = -10f; d <= 30f; d += 2.5f) marks.Add(d);
                    var exitMarks = new List<float>();
                    for (float d = -30f; d <= 60f; d += 5f) exitMarks.Add(d);
                    foreach (var (list, baseM, tagM) in new[] { (marks, entryM, "in"), (exitMarks, exitM, "out") })
                        foreach (float d in list)
                        {
                            float m = baseM + dir * d;
                            int i = Mathf.Clamp(Mathf.RoundToInt(m / sp), 1, n - 2);
                            Vector3 f = Fwd(i) * dir, r = Vector3.Cross(Vector3.up, f).normalized;
                            bool insideBore = m > span.x + 0.5f && m < span.y - 0.5f;
                            // (the eye's own metre decides inside / outside: a chase camera is 6 m behind its car)
                            bool EyeIn(float em) => em > span.x + 0.3f && em < span.y - 0.3f;
                            var poses = new List<(string v, Vector3 eye, Vector3 look, float fov, bool eyeIn)>();
                            foreach (float lane in new[] { 1.6f, -1.6f })
                            {
                                Vector3 car = pts[i] + r * lane;
                                poses.Add(("chase" + (lane > 0 ? "R" : "L"), car - f * 6f + Vector3.up * 2.0f, car + f * 4f + Vector3.up * 1.0f, 60f, EyeIn(m - dir * 6f)));
                                poses.Add(("cockpit" + (lane > 0 ? "R" : "L"), car + Vector3.up * 1.15f - r * 0.35f, car + f * 30f + Vector3.up * 1.0f, 65f, EyeIn(m)));
                            }
                            if (!insideBore && Mathf.Abs(Mathf.Round(d / 30f) * 30f - d) < 0.1f)
                                foreach (float side in new[] { 1f, -1f })
                                {
                                    Vector3 car = pts[i] + r * 1.6f;
                                    Vector3 eye = pts[i] + r * side * (def.roadWidth * 0.5f + 3f) + Vector3.up * 5f;
                                    poses.Add(("replay" + (side > 0 ? "R" : "L"), eye, car + Vector3.up * 0.8f, 45f, false));
                                }
                            foreach (var p in poses)
                            {
                                cam.transform.position = p.eye;
                                cam.transform.rotation = Quaternion.LookRotation(p.look - p.eye, Vector3.up);
                                cam.fieldOfView = p.fov;
                                cam.aspect = W / (float)H;
                                cam.Render();
                                RenderTexture.active = rt;
                                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                                tex.Apply(false);
                                RenderTexture.active = null;
                                var px = tex.GetPixels32();
                                int gaps = 0; long sx = 0, sy = 0;
                                bool camIn = p.eyeIn;
                                for (int y = 0; y < H; y += 1)
                                    for (int x = 0; x < W; x += 1)
                                    {
                                        var c = px[y * W + x];
                                        if (c.r < 235 || c.g > 20 || c.b < 235) continue;
                                        var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / W, (y + 0.5f) / H, 0f));
                                        bool gap;
                                        if (ray.direction.y < -0.01f) gap = true;
                                        else
                                        {
                                            bool a = InOpening(mouths[0], ray.origin, ray.direction), b = InOpening(mouths[1], ray.origin, ray.direction);
                                            gap = camIn ? !(a || b) : (a != b);
                                        }
                                        if (!gap) continue;
                                        gaps++; sx += x; sy += y;
                                        px[y * W + x] = new Color32(0, 255, 0, 255);   // marked green in the kept frame
                                    }
                                cams++;
                                if (gaps == 0) continue;
                                totalGap += gaps;
                                failCams++;
                                string name = $"t{no}_{(dir > 0 ? "fwd" : "rev")}_{tagM}{d:+0.0;-0.0}_{p.v}";
                                Texture2D keep = null;
                                if (results.Count < 400)
                                {
                                    keep = new Texture2D(W, H, TextureFormat.RGBA32, false);
                                    keep.SetPixels32(px); keep.Apply(false);
                                }
                                // where the gap is: the centre pixel's ray where it falls 2.5 m under the eye's road (or 25 m on),
                                // as path metre and offset (+ right of the forward path)
                                var cr = cam.ViewportPointToRay(new Vector3((sx / (float)gaps + 0.5f) / W, (sy / (float)gaps + 0.5f) / H, 0f));
                                float tt = cr.direction.y < -0.02f ? Mathf.Min(60f, (cr.origin.y - (pts[i].y - 2.5f)) / -cr.direction.y) : 25f;
                                Vector3 hp = cr.origin + cr.direction * tt;
                                int bi = 1; float bd = float.MaxValue;
                                for (int q = Mathf.Max(1, i - 40); q < Mathf.Min(n - 1, i + 40); q++) { float dd = (new Vector2(pts[q].x - hp.x, pts[q].z - hp.z)).sqrMagnitude; if (dd < bd) { bd = dd; bi = q; } }
                                float off = Vector3.Dot(hp - pts[bi], Vector3.Cross(Vector3.up, Fwd(bi)).normalized);
                                name += $" [at path m {bi * sp:0}, {off:+0.0;-0.0} m, y {hp.y - pts[bi].y:+0.0;-0.0}]";
                                if (results.Count < 8) name += " first mesh on the ray: " + FirstHit(cr, 300f);
                                results.Add((name, gaps, new Vector2(sx / (float)gaps, H - sy / (float)gaps), keep));
                            }
                        }
                }
                no++;
            }
            results.Sort((a, b) => b.gaps.CompareTo(a.gaps));
            L(sb, $"tunnel gap probe: {cams} cameras (chase, cockpit, replay; both lanes, both directions, both mouths), " +
                  $"{failCams} see through ({totalGap} px at {W}x{H}) - " + (failCams == 0 ? "ok" : "FAIL"));
            int kept = 0;
            foreach (var res in results)
            {
                if (kept < 40) L(sb, $"  GAP {res.name}: {res.gaps} px, centre at ({res.at.x:0}, {res.at.y:0}) from top-left");
                string fn = res.name.IndexOf(' ') > 0 ? res.name.Substring(0, res.name.IndexOf(' ')) : res.name;
                if (kept < 24 && res.shot != null) File.WriteAllBytes(Path.Combine(outDir, $"{kept:00}_{fn}.png"), res.shot.EncodeToPNG());
                kept++;
            }
            foreach (var res in results) if (res.shot != null) Object.DestroyImmediate(res.shot);
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            Object.DestroyImmediate(go);
            rt.Release(); Object.DestroyImmediate(rt);
            foreach (var r in hidden) if (r != null) r.enabled = true;
            RenderSettings.skybox = keepSky;
            return sb.ToString();
        }

        /// <summary>What drawn mesh the ray crosses first (a triangle test, both faces), for a gap's diagnosis: a
        /// hit there means a surface that was not drawn (culled, clipped by its shader, a back face).</summary>
        static string FirstHit(Ray ray, float maxD)
        {
            float best = maxD; string what = "nothing within " + maxD + " m";
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                if (!mr.bounds.IntersectRay(ray, out float bd) || bd > best) continue;
                var mf = mr.GetComponent<MeshFilter>(); var m = mf != null ? mf.sharedMesh : null;
                if (m == null || !m.isReadable) continue;
                var v = m.vertices; var t = m.triangles; var xf = mr.transform.localToWorldMatrix;
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    Vector3 a = xf.MultiplyPoint3x4(v[t[k]]), b = xf.MultiplyPoint3x4(v[t[k + 1]]), c = xf.MultiplyPoint3x4(v[t[k + 2]]);
                    Vector3 e1 = b - a, e2 = c - a, pv = Vector3.Cross(ray.direction, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    float inv = 1f / det; Vector3 tv = ray.origin - a;
                    float uu = Vector3.Dot(tv, pv) * inv; if (uu < 0f || uu > 1f) continue;
                    Vector3 qv = Vector3.Cross(tv, e1); float vv = Vector3.Dot(ray.direction, qv) * inv;
                    if (vv < 0f || uu + vv > 1f) continue;
                    float d = Vector3.Dot(e2, qv) * inv;
                    if (d > 0.05f && d < best)
                    {
                        best = d;
                        var n = Vector3.Cross(e1, e2);
                        what = mr.gameObject.name + " (" + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "-") + ") at " + d.ToString("0.0") + " m, " +
                               (Vector3.Dot(n, ray.direction) < 0f ? "front" : "BACK") + " face, layer " + LayerMask.LayerToName(mr.gameObject.layer);
                    }
                }
            }
            return what;
        }

        // ------------------------------------------------------------------
        //  Stretch
        // ------------------------------------------------------------------
        class MeshStat
        {
            public string name, mat;
            public double area, badArea;
            public List<(float d, float a)> dens = new List<(float, float)>();
            public float worstAniso;
            public bool nearTunnel, ground, rock;
            public float Median()
            {
                if (dens.Count == 0) return 0f;
                dens.Sort((x, y) => x.d.CompareTo(y.d));
                double half = 0, tot = 0;
                foreach (var e in dens) tot += e.a;
                foreach (var e in dens) { half += e.a; if (half >= tot * 0.5) return e.d; }
                return dens[dens.Count - 1].d;
            }
        }

        static bool IsGroundMat(Material m) => m != null && (m.name.Contains("Ground") || m.name.Contains("Verge") || m.name.Contains("Grass"));

        /// <summary>The stretch check (see the class): the tunnel's meshes judged; with <paramref name="wholeStage"/>
        /// the stage's other offenders listed (not judged).</summary>
        public static string Stretch(TrackCatalog.TrackDef def, string scenePath, bool wholeStage, out int fails)
        {
            var sb = new StringBuilder();
            fails = 0;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != scenePath)
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            TrackCatalog.EnsureStage(def);
            var pts = def.stagePts;
            float sp = TrackCatalog.Spacing;
            var zones = new List<Bounds>();
            if (def.tunnels != null)
                foreach (var span in def.tunnels)
                {
                    var b = new Bounds(pts[Mathf.Clamp(Mathf.RoundToInt(span.x / sp), 0, pts.Length - 1)], Vector3.one);
                    for (float m = span.x - 60f; m <= span.y + 60f; m += sp)
                        b.Encapsulate(pts[Mathf.Clamp(Mathf.RoundToInt(m / sp), 0, pts.Length - 1)]);
                    b.Expand(new Vector3(50f, 80f, 50f));
                    zones.Add(b);
                }
            var stats = new List<MeshStat>();
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                bool near = false;
                foreach (var z in zones) if (z.Intersects(mr.bounds)) near = true;
                bool tunnelPart = false;
                for (var t = mr.transform; t != null; t = t.parent) if (t.name == "Tunnels") tunnelPart = true;
                if (!wholeStage && !near) continue;
                var mats = mr.sharedMaterials;
                Vector3[] w = null;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                var mat = mats[s];
                if (mat == null) continue;
                bool rock = mat.IsKeywordEnabled("PSX_ROCKFACE");
                Texture tx = rock ? mat.GetTexture("_RockTex") : mat.mainTexture;
                if (tx == null) continue;
                string sh = mat.shader != null ? mat.shader.name : "";
                if (!sh.StartsWith("PSX/Lit")) continue;        // the cards, glass, water, lamps: not tiled surfaces
                // the plants' cards and crowns (an atlas cell a card, cut out: not a tiled surface)
                string mn = mat.name;
                if (mn.Contains("Forest") || mn.Contains("Crown") || mn.Contains("Tree") || mn.Contains("Leaf") || mn.Contains("Fern") || mn.Contains("Plant") || mn.Contains("Sign")) continue;
                if (mr.gameObject.layer == LayerMask.NameToLayer("TransparentFX")) continue;
                var uv = mesh.uv;
                if (!rock && (uv == null || uv.Length == 0)) continue;
                var rp = rock ? mat.GetVector("_RockParams") : Vector4.zero;
                float rockTop = rock && mat.HasProperty("_RockTop") ? mat.GetFloat("_RockTop") : 0f;
                Vector2 tile = rock ? Vector2.one : mat.mainTextureScale;
                Vector2 texel = new Vector2(tx.width, tx.height);
                if (w == null)
                {
                    var vs = mesh.vertices;
                    var xf = mr.transform.localToWorldMatrix;
                    w = new Vector3[vs.Length];
                    for (int i = 0; i < vs.Length; i++) w[i] = xf.MultiplyPoint3x4(vs[i]);
                }
                var st = new MeshStat { name = mr.gameObject.name, mat = mat.name, nearTunnel = tunnelPart || near, rock = rock, ground = IsGroundMat(mat) };
                st.nearTunnel = near; st.name = (tunnelPart ? "Tunnels/" : "") + mr.gameObject.name + (s > 0 ? "#" + s : "");
                {
                    var tri = mesh.GetTriangles(s);
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        Vector3 a = w[tri[k]], b = w[tri[k + 1]], c = w[tri[k + 2]];
                        Vector3 e1 = b - a, e2 = c - a, nrm = Vector3.Cross(e1, e2);
                        float area = nrm.magnitude * 0.5f;
                        if (area < 1e-4f) continue;
                        nrm /= (area * 2f);
                        Vector2 ua, ub, uc;
                        if (rock)
                        {
                            // the shader's own projection (PSXLit, PSX_ROCKFACE): the zy and xy planes weighted by
                            // |n.x|^4 : |n.z|^4 at the fine scale; the xz plane where _RockTop takes the up faces
                            // (with _RockTop the plane of the largest weight - the largest normal component)
                            float ax = Mathf.Pow(Mathf.Abs(nrm.x) + 1e-3f, 4f), az = Mathf.Pow(Mathf.Abs(nrm.z) + 1e-3f, 4f);
                            bool top = rockTop > 0.5f && Mathf.Abs(nrm.y) >= Mathf.Abs(nrm.x) && Mathf.Abs(nrm.y) >= Mathf.Abs(nrm.z);
                            System.Func<Vector3, Vector2> P = top ? (q => new Vector2(q.x, q.z)) : ax > az ? (q => new Vector2(q.z, q.y)) : (q => new Vector2(q.x, q.y));
                            ua = P(a) * rp.x; ub = P(b) * rp.x; uc = P(c) * rp.x;
                        }
                        else { ua = Vector2.Scale(uv[tri[k]], tile); ub = Vector2.Scale(uv[tri[k + 1]], tile); uc = Vector2.Scale(uv[tri[k + 2]], tile); }
                        // in TEXELS (a texture that is not square is not stretched by its own aspect)
                        ua = Vector2.Scale(ua, texel); ub = Vector2.Scale(ub, texel); uc = Vector2.Scale(uc, texel);
                        // the triangle's own 2D frame
                        Vector3 t1 = e1.normalized, t2 = Vector3.Cross(nrm, t1).normalized;
                        float p1x = e1.magnitude, p2x = Vector3.Dot(e2, t1), p2y = Vector3.Dot(e2, t2);
                        if (Mathf.Abs(p1x * p2y) < 1e-8f) continue;
                        Vector2 d1 = ub - ua, d2 = uc - ua;
                        // M [p1 p2] = [d1 d2], P = [[p1x, p2x],[0, p2y]]
                        float inv = 1f / (p1x * p2y);
                        float m00 = (d1.x * p2y) * inv, m01 = (d2.x * p1x - d1.x * p2x) * inv;
                        float m10 = (d1.y * p2y) * inv, m11 = (d2.y * p1x - d1.y * p2x) * inv;
                        // singular values of M
                        float aa = m00 * m00 + m10 * m10, bb = m01 * m01 + m11 * m11, ab = m00 * m01 + m10 * m11;
                        float tr = aa + bb, det = Mathf.Sqrt(Mathf.Max(0f, (aa - bb) * (aa - bb) + 4f * ab * ab));
                        float s1 = Mathf.Sqrt(Mathf.Max(0f, (tr + det) * 0.5f)), s2 = Mathf.Sqrt(Mathf.Max(0f, (tr - det) * 0.5f));
                        float aniso = s2 > 1e-6f ? s1 / s2 : 99f;
                        if (rock)
                        {
                            // what the shader draws is the BLEND of its planes, each stretched 1 / |n . axis| on this
                            // facet, weighted |n . axis|^4 (PSXLit PSX_ROCKFACE; the up plane only with _RockTop)
                            float nx = Mathf.Abs(nrm.x) + 1e-3f, ny = Mathf.Abs(nrm.y) + 1e-3f, nz = Mathf.Abs(nrm.z) + 1e-3f;
                            float wx = Mathf.Pow(nx, 4f), wz = Mathf.Pow(nz, 4f), wy = rockTop > 0.5f ? Mathf.Pow(ny, 4f) : 0f;
                            float wsum = wx + wy + wz;
                            aniso = (wx / nx + wy / ny + wz / nz) / wsum;
                        }
                        float dens = Mathf.Sqrt(s1 * s2);
                        st.area += area;
                        if (aniso > 1.5f) st.badArea += area;
                        if (aniso > st.worstAniso && aniso < 99f) st.worstAniso = aniso;
                        st.dens.Add((dens, area));
                    }
                }
                if (st.area > 0) stats.Add(st);
                }
            }
            // the references: the ground chunks and the road cut's rock near the tunnels
            float Ref(System.Func<MeshStat, bool> pick)
            {
                var all = new MeshStat { name = "ref" };
                foreach (var s in stats) if (s.nearTunnel && pick(s)) all.dens.AddRange(s.dens);
                return all.Median();
            }
            float gRef = Ref(s => s.name.StartsWith("GroundN") || s.name.StartsWith("Ground_"));
            float rRef = Ref(s => s.rock && !s.name.StartsWith("Tunnels/"));
            L(sb, $"stretch check: reference texel density near the tunnels - ground {gRef:0.0} texels/m, cut rock {rRef:0.0} texels/m");
            L(sb, "  TUNNEL meshes (fail: anisotropy > 1.5 over > 2 % of the area, or density off its neighbour by > 30 %):");
            foreach (var s in stats)
            {
                if (!s.name.StartsWith("Tunnels/")) continue;
                float med = s.Median();
                float rf = s.rock ? rRef : s.ground ? gRef : 0f;
                float mis = rf > 0f ? med / rf - 1f : 0f;
                float badPct = (float)(100.0 * s.badArea / s.area);
                bool fail = badPct > 2f || (rf > 0f && Mathf.Abs(mis) > 0.30f);
                if (fail) fails++;
                L(sb, $"    {(fail ? "FAIL" : "ok  ")} {s.name} [{s.mat}{(s.rock ? ", rock planes" : ", uv")}] {s.area:0} m2: stretched >1.5 {badPct:0.0}% (worst {s.worstAniso:0.0}), " +
                      $"density {med:0.0}" + (rf > 0f ? $" vs {(s.rock ? "rock" : "ground")} {rf:0.0} ({mis * 100f:+0;-0}%)" : " (no neighbour reference)"));
            }
            L(sb, "  near the tunnels, other meshes (stretched share):");
            var nearList = stats.FindAll(s => s.nearTunnel && !s.name.StartsWith("Tunnels/") && s.badArea / s.area > 0.02);
            nearList.Sort((a, b) => b.badArea.CompareTo(a.badArea));
            for (int i = 0; i < nearList.Count && i < 25; i++)
                L(sb, $"    {nearList[i].name} [{nearList[i].mat}] {nearList[i].area:0} m2: >1.5 on {100.0 * nearList[i].badArea / nearList[i].area:0.0}% ({nearList[i].badArea:0} m2), worst {nearList[i].worstAniso:0.0}, density {nearList[i].Median():0.0}");
            if (wholeStage)
            {
                // the whole stage by kind (the name less its numbers), listed for the coordinator - not judged
                var kinds = new Dictionary<string, (double area, double bad, float worst, string mat)>();
                foreach (var s in stats)
                {
                    if (s.name.StartsWith("Tunnels/")) continue;
                    string k = System.Text.RegularExpressions.Regex.Replace(s.name, @"[\d_\-\.]+$|[_\-]?\d+", "") + " [" + s.mat + "]";
                    kinds.TryGetValue(k, out var e);
                    kinds[k] = (e.area + s.area, e.bad + s.badArea, Mathf.Max(e.worst, s.worstAniso), s.mat);
                }
                var list = new List<KeyValuePair<string, (double area, double bad, float worst, string mat)>>(kinds);
                list.RemoveAll(e => e.Value.bad < 20.0 || e.Value.bad / e.Value.area < 0.02);
                list.Sort((a, b) => b.Value.bad.CompareTo(a.Value.bad));
                L(sb, "  WHOLE STAGE, other offenders (listed, not fixed): kind, stretched area > 1.5:1");
                for (int i = 0; i < list.Count && i < 20; i++)
                    L(sb, $"    {list[i].Key}: {list[i].Value.bad:0} m2 of {list[i].Value.area:0} m2 ({100.0 * list[i].Value.bad / list[i].Value.area:0.0}%), worst {list[i].Value.worst:0.0}");
            }
            L(sb, "stretch check: " + (fails == 0 ? "ok" : fails + " tunnel mesh(es) FAIL"));
            return sb.ToString();
        }
    }
}
