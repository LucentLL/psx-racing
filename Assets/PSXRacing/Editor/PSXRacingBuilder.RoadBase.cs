using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD BASE'S PAINT MASKS (Charlotte texture batch 01, 2026-11-11).
    /// The HD tier draws a Charlotte carriageway as Codex's 4 m base surface
    /// (city_road_base_&lt;surface&gt;) with the lane paint laid over it, instead
    /// of the painter's 19 cm hash grain (TextureHD road path, PSX/Lit
    /// _RoadBaseOn). The paint comes from a mask cut here from the SHIPPED
    /// road PNG, texel by texel, by what the painter put there:
    ///   R  white paint (the painter's exact White)
    ///   G  yellow paint (its exact Yellow)
    ///   B  the shoulder rumble strip's factor / 2 (0.5 = plain pavement,
    ///      0.35 a groove, 0.7 a land: DrawProfileTex's 0.70 / 1.40)
    /// Every other texel must be Grain(x, y, surface) exactly; a texel that is
    /// none of these is counted (the import logs it) and drawn as pavement.
    /// Editor-only and reading the PNG, so the painter, Scripts/City and the
    /// tile bake's fingerprint are untouched.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        internal static Color PaintWhiteLinear => ((Color)White).linear;
        internal static Color PaintYellowLinear => ((Color)Yellow).linear;

        /// <summary>A road texture's profile and surface from its basename
        /// (city_road_&lt;profile&gt;_&lt;surface&gt;). False for anything else
        /// (a circuit's city_road_track_*, an unknown profile).
        /// <paramref name="junction"/>: the unmarked junction slab.</summary>
        internal static bool RoadTexOf(string baseName, out int profile, out CityMeshes.Surface surf, out bool junction)
        {
            profile = -1; surf = CityMeshes.Surface.AsphaltOld; junction = false;
            if (baseName == null || !baseName.StartsWith("city_road_")) return false;
            for (int s = 0; s < CityMeshes.SurfaceCount; s++)
            {
                string sk = "_" + SurfaceKey((CityMeshes.Surface)s);
                if (!baseName.EndsWith(sk)) continue;
                string key = baseName.Substring("city_road_".Length, baseName.Length - "city_road_".Length - sk.Length);
                surf = (CityMeshes.Surface)s;
                if (key == "junction") { junction = true; return true; }
                for (int p = 0; p < RoadProfiles.Count; p++)
                    if (RoadProfiles.All[p].key == key) { profile = p; return true; }
                return false;
            }
            return false;
        }

        internal static float RoadProfileWidth(int profile) => RoadProfiles.All[profile].Width;
        internal static string RoadSurfaceKey(CityMeshes.Surface s) => SurfaceKey(s);

        /// <summary>The mask for one shipped road PNG (see the class comment).
        /// <paramref name="unknown"/>: texels that were neither paint, grain nor
        /// rumble.</summary>
        internal static Texture2D RoadPaintMask(string pngPath, CityMeshes.Surface surf, out int unknown, out int painted)
        {
            unknown = 0; painted = 0;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                src.LoadImage(File.ReadAllBytes(pngPath));
                int w = src.width, h = src.height;
                var px = src.GetPixels32();
                var o = new Color32[px.Length];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[y * w + x];
                        byte r = 0, g = 0, b = 128;
                        if (Same(c, White)) { r = 255; painted++; }
                        else if (Same(c, Yellow)) { g = 255; painted++; }
                        else
                        {
                            var gr = Grain(x, y, surf);
                            // the rumble within 2 codes: on the concrete motorways
                            // ~1% of the band's texels round a code off this float
                            // arithmetic (measured offline over all 80 PNGs: 660 exact
                            // misses, 0 left within 2)
                            if (Same(c, gr)) b = 128;
                            else if (Near(c, Scaled(gr, 0.70f), 2)) b = 89;
                            else if (Near(c, Scaled(gr, 1.40f), 2)) b = 179;
                            else unknown++;
                        }
                        o[y * w + x] = new Color32(r, g, b, 255);
                    }
                var mask = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                mask.SetPixels32(o);
                mask.Apply();
                return mask;
            }
            finally { Object.DestroyImmediate(src); }
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
        static bool Near(Color32 a, Color32 b, int d) =>
            Mathf.Abs(a.r - b.r) <= d && Mathf.Abs(a.g - b.g) <= d && Mathf.Abs(a.b - b.b) <= d;

        // exactly DrawProfileTex's rumble arithmetic
        static Color32 Scaled(Color32 px, float k) =>
            new Color32((byte)Mathf.Min(255f, px.r * k), (byte)Mathf.Min(255f, px.g * k), (byte)Mathf.Min(255f, px.b * k), 255);
    }
}
