using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WHY IS THE ROAD BLACK NEAR A STAGE'S END (2026-11-11)? At each pose in
    /// PSX_DARK_POSES ("BlueRidge=48,7124,900;SwissNC226A=11664,448"), the
    /// tarmac 6 m ahead is drawn at noon looking down at it and its brightness
    /// read; then each suspect is switched off in turn (the sun's shadows,
    /// the road material's keywords, every renderer near the spot) and the
    /// change read. Writes PSXRacing_road_dark.txt.
    /// </summary>
    public static class RoadDarkProbe
    {
        static int tweak;

        /// <summary>RoadEndsProbe, then this, in one editor session.</summary>
        public static void Both() { RoadEndsProbe.Run(); Run(); }
        public static void Run()
        {
            string spec = System.Environment.GetEnvironmentVariable("PSX_DARK_POSES") ?? "BlueRidge=48,900";
            var sb = new StringBuilder();
            try
            {
                foreach (var part in spec.Split(';'))
                {
                    var kv = part.Split('=');
                    if (kv.Length != 2) continue;
                    string id = kv[0].Trim();
                    TrackCatalog.TrackDef def = null;
                    for (int i = 0; i < TrackCatalog.Count; i++) if (TrackCatalog.At(i).id == id) def = TrackCatalog.At(i);
                    if (def == null || !PSXScreenshotTool.Open(def, out var cam, out _)) { sb.AppendLine("no scene " + id); continue; }
                    var path = Object.FindAnyObjectByType<TrackPath>();
                    var sun = GameObject.Find("Sun")?.GetComponent<Light>();
                    var globals = Object.FindAnyObjectByType<PSXGlobals>();
                    var sc = cam.GetComponent<StageCulling>();
                    if (sc != null) sc.Configure(cam);
                    int noon = 0;
                    for (int h = 0; h < TimeOfDay.Count; h++) if (TimeOfDay.At(h).name.ToLower() == "noon") noon = h;
                    var road = GameObject.Find("Road");
                    var roadMr = road.GetComponent<MeshRenderer>();
                    foreach (var msS in kv[1].Split(','))
                    {
                        float m = float.Parse(msS);
                        int at = Mathf.Clamp(Mathf.RoundToInt(m / TrackCatalog.Spacing), 2, path.Count - 4);
                        Vector3 p = path.GetPoint(at);
                        Vector3 fwd = path.GetTangent(at); fwd.y = 0f; fwd.Normalize();
                        Vector3 eye = p - fwd * 2f + Vector3.up * 3f;
                        var rot = Quaternion.LookRotation(p + fwd * 4f - eye);
                        float Lum()
                        {
                            TimeOfDay.Apply(noon, sun);
                            if (tweak == 1) globals.sunShadow = 0f;
                            if (tweak == 2) globals.skyShade = 0f;
                            globals.Apply();
                            cam.transform.SetPositionAndRotation(eye, rot);
                            SunShadows.RenderFor(cam);
                            PSXTexDecode.StampAll();
                            var rt = RenderTexture.GetTemporary(160, 90, 24, RenderTextureFormat.ARGB32);
                            var req = new RenderPipeline.StandardRequest { destination = rt };
                            RenderPipeline.SubmitRenderRequest(cam, req);
                            var prev = RenderTexture.active; RenderTexture.active = rt;
                            var tex = new Texture2D(160, 90, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, 160, 90), 0, 0); tex.Apply();
                            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                            float s = 0f; int c = 0;
                            for (int y = 30; y < 60; y++) for (int x = 65; x < 95; x++) { var col = tex.GetPixel(x, y); s += col.r * 0.3f + col.g * 0.59f + col.b * 0.11f; c++; }
                            Object.DestroyImmediate(tex);
                            return s / c;
                        }
                        float baseL = Lum();
                        sb.AppendLine($"{id} {m:0} m: road brightness {baseL:0.000}  (eye {eye.x:0.0},{eye.y:0.0},{eye.z:0.0})");
                        tweak = 1; sb.AppendLine($"   sun's shadow map off: {Lum():0.000}");
                        tweak = 2; sb.AppendLine($"   sky's (roof) map off: {Lum():0.000}");
                        tweak = 0;
                        sb.AppendLine($"   SunShadows: {SunShadows.DrawnLast} drawn into the sun's map, {SunShadows.SkyDrawnLast} into the sky's");
                        // the road's keywords, one at a time
                        foreach (var mat in roadMr.sharedMaterials)
                        {
                            if (mat == null) continue;
                            foreach (var kw in mat.shaderKeywords)
                            {
                                mat.DisableKeyword(kw);
                                sb.AppendLine($"   {mat.name} without {kw}: {Lum():0.000}");
                                mat.EnableKeyword(kw);
                            }
                        }
                        // the renderers near the spot
                        var b = new Bounds(p + fwd * 4f, new Vector3(30f, 30f, 30f));
                        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                        {
                            if (!r.enabled || !r.bounds.Intersects(b)) continue;
                            r.enabled = false;
                            float l = Lum();
                            r.enabled = true;
                            if (Mathf.Abs(l - baseL) > 0.01f || r == roadMr || r.name.StartsWith("GroundN"))
                                sb.AppendLine($"   without {r.name} ({r.GetType().Name}, layer {r.gameObject.layer}, mat {(r.sharedMaterial != null ? r.sharedMaterial.name : "-")}): {l:0.000}");
                        }
                    }
                }
            }
            catch (System.Exception e) { sb.AppendLine("THREW " + e); }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_road_dark.txt"), sb.ToString());
            Debug.Log("[RoadDarkProbe]\n" + sb);
        }
    }
}
