using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The streets round every Charlotte race, baked once into
    /// Resources/RouteMaps/&lt;key&gt;.png for the menus' view from above
    /// (RouteMap). The menu must never parse the 7.7 MB city to draw a card,
    /// so the race card loads one small picture and draws the route over it
    /// in the same square (RouteGeo.Frame). White lines, the road's class in
    /// the alpha (freeways strongest); the menu tints them grey.
    ///
    /// Run again when the city data or a route changes. Menu: PSX Racing/
    /// Bake Route Maps; batch: -executeMethod PSXRacing.EditorTools.RouteMapBake.Run.
    /// </summary>
    public static class RouteMapBake
    {
        public const int Size = 1024;
        const string Dir = "Assets/PSXRacing/Resources/RouteMaps";

        [MenuItem("PSX Racing/Bake Route Maps")]
        public static void Run()
        {
            var was = Edition.Simulated;
            Edition.Simulate(EditionKind.City);
            var written = new List<string>();
            try
            {
                var map = CityMap.Get();
                if (map == null) { Debug.LogError("[RouteMapBake] no city map"); return; }
                Directory.CreateDirectory(Dir);
                var done = new HashSet<string>();
                for (int i = 0; i < TrackCatalog.Count; i++)
                {
                    var def = TrackCatalog.At(i);
                    if (!def.IsCityRace) continue;
                    string key = RouteGeo.StreetKey(def);
                    if (key == null || !done.Add(key)) continue;
                    var pts = TrackCatalog.Sample(def, TrackCatalog.Spacing);
                    RouteGeo.Frame(def, pts, out var c, out float span);
                    var px = Bake(map, c, span, out int segs);
                    var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                    tex.SetPixels32(px);
                    tex.Apply();
                    string path = Dir + "/" + key + ".png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    written.Add(path);
                    Debug.Log("[RouteMapBake] " + key + ": " + segs + " street segments over " +
                              span.ToString("0") + " m -> " + path);
                }
            }
            finally { Edition.Simulate(was); }

            AssetDatabase.Refresh();
            foreach (var path in written)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = true;
                ti.sRGBTexture = true;
                ti.maxTextureSize = Size;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Debug.Log("[RouteMapBake] DONE " + written.Count + " maps");
        }

        /// <summary>The streets in the square centred on <paramref name="c"/>,
        /// <paramref name="span"/> metres a side, least important first.</summary>
        static Color32[] Bake(CityMap map, Vector2 c, float span, out int count)
        {
            var a = new float[Size * Size];
            float scale = Size / span;
            var lo = c - Vector2.one * (span * 0.5f);
            var hi = c + Vector2.one * (span * 0.5f);
            var segs = new HashSet<int>();
            map.EdgeSegsInRect(lo, hi, segs);
            count = segs.Count;
            // Widths in pixels at 1024 over ~2.5 km; thinner as the square
            // grows so a 9 km sprint is not a grey felt of side streets.
            float k = Mathf.Clamp(2600f / span, 0.65f, 1.6f);
            float localA = Mathf.Clamp(3200f / span, 0.45f, 0.75f);
            for (int pass = 0; pass < 4; pass++)
                foreach (int packed in segs)
                {
                    int ei = packed >> 12, si = packed & 0xFFF;
                    var e = map.edges[ei];
                    int rank = e.link ? 2 : e.cls >= 5 ? 3 : e.cls >= 3 ? 2 : e.cls >= 1 ? 1 : 0;
                    if (rank != pass || si + 1 >= e.pts.Length) continue;
                    float w = rank == 3 ? 3.4f : rank == 2 ? (e.link ? 1.8f : 2.4f) : rank == 1 ? 1.8f : 1.3f;
                    float al = rank == 3 ? 1f : rank == 2 ? 0.86f : rank == 1 ? 0.74f : localA;
                    Vector2 p0 = (e.pts[si] - lo) * scale, p1 = (e.pts[si + 1] - lo) * scale;
                    Stroke(a, p0, p1, w * k * 0.5f, al);
                }
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255f));
            return px;
        }

        /// <summary>A capsule of half-width <paramref name="r"/> pixels, one
        /// pixel of soft edge, max-blended into the alpha.</summary>
        static void Stroke(float[] a, Vector2 p0, Vector2 p1, float r, float alpha)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, p1.x) - r - 1));
            int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p0.x, p1.x) + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, p1.y) - r - 1));
            int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(p0.y, p1.y) + r + 1));
            if (x0 > x1 || y0 > y1) return;
            var d = p1 - p0;
            float L2 = Mathf.Max(d.sqrMagnitude, 1e-6f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - p0, d) / L2);
                    float dist = Vector2.Distance(p, p0 + d * t);
                    float v = Mathf.Clamp01(r + 0.5f - dist) * alpha;
                    int i = y * Size + x;
                    if (v > a[i]) a[i] = v;
                }
        }
    }
}
