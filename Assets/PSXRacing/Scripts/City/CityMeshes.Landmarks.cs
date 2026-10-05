using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE LANDMARK HEROES (Uptown C, 2026-10-05). Five of uptown's
        //  landmarks are drawn as themselves rather than as their OSM tiers:
        //  a crown of stepped fins and a needle, an open steel frame on a
        //  tower top, a flared silver lantern, a copper pyramid with a lattice
        //  spire, and a stadium's open bowl. Procedural low-poly, emitted INTO
        //  the tile's building mesh in the facade atlas (metal by the vertex
        //  alpha's metal bit, B1b) and the flat roofs - no new material, no new
        //  draw call, and the far skyline (which draws the same EmitFootprint)
        //  carries the same shapes in its own two draws. Shapes only, from
        //  reference photographs by eye: no logo, name, sign, slogan, team mark
        //  or screen content anywhere (owner rule 2026-10-04 - the stadium's
        //  boards are blank), and neutral names in code.
        //
        //  The table is keyed by the OSM element the exporter's landmark table
        //  (tools/city/lib/parts.mjs) keys the same building by; PBLD v3 carries
        //  that table's index + 1 as each footprint's landmark byte. A hero is
        //  drawn from its ANCHOR (its outline, or for an outline its parts draw,
        //  the biggest part on the ground); parts it redraws are skipped.
        //  What a hero adds above 30 m and a stadium's bowl inside its outer
        //  wall are DRAWN but not COLLIDED (Bucket.NoCollide): the tile's
        //  collider is built from the rest (ColliderFrom).
        // ------------------------------------------------------------------
        enum HeroShape : byte { SpiredCrown, OpenFrame, SilverCrown, PyramidTop, Bowl }

        struct HeroSpec
        {
            public string osm; public byte landmark; public HeroShape shape; public bool on;
            public HeroSpec(string osm, byte landmark, HeroShape shape, bool on)
            { this.osm = osm; this.landmark = landmark; this.shape = shape; this.on = on; }
        }

        /// <summary>The heroes. <c>on</c> false leaves a landmark to its B2/B3
        /// drawing (a hero that broke a gate stays listed, switched off).</summary>
        static readonly HeroSpec[] Heroes =
        {
            new HeroSpec("w341587198",  1, HeroShape.SpiredCrown, true),   // C1 spired crown tower
            new HeroSpec("w1550692284", 2, HeroShape.OpenFrame,   true),   // C2 open-frame tower
            new HeroSpec("w131139746",  3, HeroShape.SilverCrown, true),   // C3 silver crown tower
            new HeroSpec("w90480703",   5, HeroShape.PyramidTop,  true),   // C4 pyramid-top tower
            new HeroSpec("r12346952",  14, HeroShape.Bowl,        true),   // C5 the stadium
        };

        /// <summary>The spired crown's needle top above its ground: the
        /// building's real overall height (OSM's spire part says 300 m).</summary>
        const float NeedleTopM = 265f;
        /// <summary>A hero's crown starts this high: below it a hero collides
        /// as the building it stands on.</summary>
        const float HeroCollideTopM = 30f;

        // the facade atlas texels a hero's solid surfaces sample (one texel,
        // so a member reads as one material, and never a window: both are
        // 0 in the night mask): column 0's white frame, column 3's plain precast
        static readonly Vector2 HeroMetalUV = new Vector2(0.488f, 0.8125f);
        static readonly Vector2 HeroMatteUV = new Vector2(0.113f, 0.732f);
        const int HeroMetalCol = 0, HeroMatteCol = 3;

        static CityMap heroMap;
        static readonly sbyte[] heroOfLandmark = new sbyte[256];
        static readonly int[] heroAnchorOf = new int[5];
        static readonly HashSet<int> heroSkip = new HashSet<int>();
        static readonly List<int>[] heroParts = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        static readonly List<int>[] heroDropped = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        /// <summary>True while a hero's walls are emitted one panel a wall
        /// (a 43 m stadium wall needs no nine-metre panels).</summary>
        static bool heroOnePanel;
        static readonly List<Vector2> heroP1 = new List<Vector2>(32), heroP2 = new List<Vector2>(32), heroP3 = new List<Vector2>(32);
        static readonly List<Vector2> heroPts = new List<Vector2>(256);

        /// <summary>Triangles each hero building's footprints drew in their
        /// tiles' last builds (its anchor with the hero's shapes, and the
        /// OSM parts it keeps), by footprint.</summary>
        static readonly Dictionary<int, int> heroTrisOf = new Dictionary<int, int>();
        static int BuildingTris()
        {
            int n = 0;
            foreach (var s in BuildingSlots) { var bk = buckets[(int)s]; n += bk.t.Count + (bk.tn != null ? bk.tn.Count : 0); }
            return n / 3;
        }

        /// <summary>One line per hero: its triangles as the tiles last drew
        /// them (tools print it after a probe).</summary>
        public static string HeroTriReport()
        {
            var sb = new System.Text.StringBuilder("[Landmarks]");
            for (int h = 0; h < Heroes.Length; h++)
            {
                if (heroMap == null || heroAnchorOf[h] < 0) { sb.Append($" {Heroes[h].shape} off;"); continue; }
                int sum = heroTrisOf.TryGetValue(heroAnchorOf[h], out int a) ? a : 0, anchorTris = sum;
                foreach (int pi in heroParts[h]) if (pi != heroAnchorOf[h] && heroTrisOf.TryGetValue(pi, out int t)) sum += t;
                sb.Append($" {Heroes[h].shape} {sum} tris (anchor + hero {anchorTris});");
            }
            return sb.ToString();
        }

        /// <summary>The hero a footprint's landmark byte names (-1 none or
        /// switched off).</summary>
        static int HeroOf(CityMap map, CityMap.Footprint f)
        {
            if (heroMap != map) IndexHeroes(map);
            return heroOfLandmark[f.landmark];
        }

        /// <summary>Once per map: each hero's outline, parts, anchor and the
        /// parts it draws itself. PSX_HEROES=0 switches every hero off, a
        /// list of landmark bytes keeps only those (editor A/B).</summary>
        static void IndexHeroes(CityMap map)
        {
            heroMap = map;
            for (int i = 0; i < heroOfLandmark.Length; i++) heroOfLandmark[i] = -1;
            heroSkip.Clear();
            string env = System.Environment.GetEnvironmentVariable("PSX_HEROES");
            HashSet<string> keep = null;
            if (!string.IsNullOrEmpty(env)) keep = new HashSet<string>(env.Split(','));
            var fs = map.footprints;
            for (int h = 0; h < Heroes.Length; h++)
            {
                heroAnchorOf[h] = -1; heroParts[h].Clear(); heroDropped[h].Clear();
                var spec = Heroes[h];
                if (!spec.on || (keep != null && !keep.Contains(spec.landmark.ToString()))) continue;
                int outline = -1;
                for (int i = 0; i < fs.Length; i++)
                    if (fs[i].landmark == spec.landmark && !fs[i].part) { outline = i; break; }
                if (outline < 0) continue;
                for (int i = 0; i < fs.Length; i++)
                    if (fs[i].part && fs[i].outline == outline) heroParts[h].Add(i);
                var of = fs[outline];
                int anchor = -1;
                if (!of.hidden) anchor = outline;
                else
                {
                    float best = 0f;
                    foreach (int i in heroParts[h])
                    {
                        var p = fs[i];
                        if (p.minH > 1f) continue;
                        float w = 4f * p.hu * p.hv * p.h;
                        if (w > best) { best = w; anchor = i; }
                    }
                }
                if (anchor < 0) continue;
                var af = fs[anchor];
                float anchorArea = 4f * af.hu * af.hv;
                foreach (int i in heroParts[h])
                {
                    if (i == anchor) continue;
                    var p = fs[i];
                    float area = 4f * p.hu * p.hv;
                    bool drop = false;
                    switch (spec.shape)
                    {
                        case HeroShape.SpiredCrown:
                            // the needle and its core: the hero's own needle
                            drop = area < 120f && p.h > 240f; break;
                        case HeroShape.OpenFrame:
                            // the crown's slabs (OSM's stand-in for the frame);
                            // its sloped glass stays
                            drop = p.minH >= 175f && (p.mat != 1 || 2f * p.hv < 6f); break;
                        case HeroShape.Bowl:
                            // the solid tiers inside the bowl, and the boards
                            drop = area >= 0.2f * anchorArea || (p.h >= 50f && 2f * p.hv < 8f); break;
                    }
                    if (drop) { heroSkip.Add(i); heroDropped[h].Add(i); }
                }
                heroAnchorOf[h] = anchor;
                heroOfLandmark[spec.landmark] = (sbyte)h;
            }
        }

        /// <summary>The buildings' collider when a hero left triangles out of
        /// it: the collided triangles of every used slot over only the
        /// vertices they use. Null when nothing was left out (the drawn mesh
        /// collides, as before).</summary>
        static Mesh ColliderFrom(Slot[] used)
        {
            bool any = false;
            int total = 0;
            foreach (var s in used)
            {
                var bk = buckets[(int)s];
                total += bk.Count;
                if (bk.tn != null && bk.tn.Count > 0) any = true;
            }
            if (!any) return null;
            var remap = new int[total];
            for (int i = 0; i < total; i++) remap[i] = -1;
            var verts = new List<Vector3>(total);
            var tris = new List<int>(total * 2);
            int baseV = 0;
            foreach (var s in used)
            {
                var bk = buckets[(int)s];
                foreach (int k in bk.t)
                {
                    int g = baseV + k;
                    if (remap[g] < 0) { remap[g] = verts.Count; verts.Add(bk.v[k]); }
                    tris.Add(remap[g]);
                }
                baseV += bk.Count;
            }
            var m = new Mesh { name = "bldCollider" };
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0, false);
            m.RecalculateBounds();
            return m;
        }

        // ---- the primitives --------------------------------------------------

        static Vector3 Lw(TileMeshes tm, Vector3 p) => new Vector3(p.x - tm.origin.x, p.y, p.z - tm.origin.z);
        static Vector3 W3(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        /// <summary>The facade bucket's tint for a hero surface: the atlas
        /// column, metal or not, a linear multiplier.</summary>
        static void HeroTint(int col, bool metal, float r, float g, float b) =>
            Bucket.Tint = new Color32(TintByte(r), TintByte(g), TintByte(b), (byte)(col * 32 + (metal ? FacadeMetalBit : 0)));

        /// <summary>Crown geometry is drawn, not collided (never in the
        /// skyline, whose mesh takes only the collided list).</summary>
        static void HeroNoCollide(bool on) => Bucket.NoCollide = on && !skylineBuild;

        static void HeroQuad(TileMeshes tm, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Vector2 uv) =>
            buckets[(int)Slot.FacadeGlass].Face(Lw(tm, a), Lw(tm, b), Lw(tm, c), Lw(tm, d), facing, uv, uv, uv, uv);

        static void HeroTri(TileMeshes tm, Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Vector2 uv)
        {
            var bk = buckets[(int)Slot.FacadeGlass];
            Vector3 la = Lw(tm, a), lb = Lw(tm, b), lc = Lw(tm, c);
            if (Vector3.Dot(Vector3.Cross(lc - la, lb - la), facing) >= 0f) bk.Tri(la, lb, lc, uv, uv, uv);
            else bk.Tri(la, lc, lb, uv, uv, uv);
        }

        /// <summary>A square-section member from a to b, w half its side
        /// (its ends open: they meet other members or a roof).</summary>
        static void HeroBeam(TileMeshes tm, Vector3 a, Vector3 b, float w, Vector2 uv)
        {
            var ax = b - a;
            if (ax.sqrMagnitude < 1e-4f) return;
            ax.Normalize();
            var s = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized * w;
            var t = Vector3.Cross(ax, s).normalized * w;
            Vector3 c0 = s + t, c1 = -s + t, c2 = -s - t, c3 = s - t;
            HeroQuad(tm, a + c0, b + c0, b + c1, a + c1, c0 + c1, uv);
            HeroQuad(tm, a + c1, b + c1, b + c2, a + c2, c1 + c2, uv);
            HeroQuad(tm, a + c2, b + c2, b + c3, a + c3, c2 + c3, uv);
            HeroQuad(tm, a + c3, b + c3, b + c0, a + c0, c3 + c0, uv);
        }

        /// <summary>A box round a plan centre along axis u: four sides and a
        /// top.</summary>
        static void HeroBox(TileMeshes tm, Vector2 c, Vector2 u, float hu, float hv, float y0, float y1, Vector2 uv)
        {
            var v = new Vector2(-u.y, u.x);
            Vector2 p0 = c + u * hu + v * hv, p1 = c - u * hu + v * hv, p2 = c - u * hu - v * hv, p3 = c + u * hu - v * hv;
            HeroQuad(tm, W3(p0, y0), W3(p0, y1), W3(p1, y1), W3(p1, y0), W3(v, 0f), uv);
            HeroQuad(tm, W3(p1, y0), W3(p1, y1), W3(p2, y1), W3(p2, y0), W3(-u, 0f), uv);
            HeroQuad(tm, W3(p2, y0), W3(p2, y1), W3(p3, y1), W3(p3, y0), W3(-v, 0f), uv);
            HeroQuad(tm, W3(p3, y0), W3(p3, y1), W3(p0, y1), W3(p0, y0), W3(u, 0f), uv);
            HeroQuad(tm, W3(p0, y1), W3(p1, y1), W3(p2, y1), W3(p3, y1), Vector3.up, uv);
        }

        /// <summary>A spike: an n-sided cone from a ring of radius r at
        /// base to a point at tipY.</summary>
        static void HeroSpike(TileMeshes tm, Vector3 baseC, float r, float tipY, int sides, float rot, Vector2 uv)
        {
            var tip = new Vector3(baseC.x, tipY, baseC.z);
            for (int k = 0; k < sides; k++)
            {
                float a0 = rot + k * Mathf.PI * 2f / sides, a1 = rot + (k + 1) * Mathf.PI * 2f / sides;
                var p = baseC + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r;
                var q = baseC + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
                var mid = (p + q) * 0.5f - baseC;
                HeroTri(tm, p, q, tip, mid + Vector3.up * (r * r / Mathf.Max(0.1f, tipY - baseC.y)), uv);
            }
        }

        /// <summary>A crown fin: a thin blade in the vertical plane of
        /// <paramref name="dir"/> (outward), its outer edge at
        /// <paramref name="outer"/> from y0 to yTop, its foot running
        /// <paramref name="depth"/> back in; a spike when
        /// <paramref name="pointed"/> (the top runs down to the inner foot),
        /// else a straight-topped blade. Both faces and the outer edge.</summary>
        static void HeroFin(TileMeshes tm, Vector2 outer, Vector2 dir, float depth, float y0, float yTop, float half, bool pointed, Vector2 uv)
        {
            var n = new Vector2(-dir.y, dir.x) * half;
            Vector2 inner = outer - dir * depth;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var o = n * sgn;
                var face = W3(o, 0f);
                if (pointed) HeroTri(tm, W3(outer + o, y0), W3(inner + o, y0), W3(outer + o, yTop), face, uv);
                else HeroQuad(tm, W3(outer + o, y0), W3(outer + o, yTop), W3(inner + o, yTop), W3(inner + o, y0), face, uv);
            }
            HeroQuad(tm, W3(outer + n, y0), W3(outer + n, yTop), W3(outer - n, yTop), W3(outer - n, y0), W3(dir, 0f), uv);
            if (!pointed)
                HeroQuad(tm, W3(outer + n, yTop), W3(inner + n, yTop), W3(inner - n, yTop), W3(outer - n, yTop), Vector3.up, uv);
        }

        static Vector2 Centroid(IReadOnlyList<Vector2> poly)
        {
            float ar = 0f; Vector2 c = Vector2.zero;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float cr = a.x * b.y - b.x * a.y;
                ar += cr; c += (a + b) * cr;
            }
            return Mathf.Abs(ar) > 1e-4f ? c / (3f * ar) : poly[0];
        }

        /// <summary>The smallest box over a point set, tried along each
        /// consecutive pair's direction.</summary>
        static bool ObbOfPoints(List<Vector2> pts, out Vector2 c, out Vector2 u, out float hu, out float hv)
        {
            c = default; u = Vector2.right; hu = hv = 0f;
            float bestA = float.MaxValue;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var d = pts[i + 1] - pts[i];
                if (d.sqrMagnitude < 1f) continue;
                d.Normalize();
                var e = new Vector2(-d.y, d.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var p in pts)
                {
                    float a = Vector2.Dot(p, d), b = Vector2.Dot(p, e);
                    u0 = Mathf.Min(u0, a); u1 = Mathf.Max(u1, a); v0 = Mathf.Min(v0, b); v1 = Mathf.Max(v1, b);
                }
                float area = (u1 - u0) * (v1 - v0);
                if (area < bestA)
                {
                    bestA = area; u = d; hu = (u1 - u0) * 0.5f; hv = (v1 - v0) * 0.5f;
                    c = d * ((u0 + u1) * 0.5f) + e * ((v0 + v1) * 0.5f);
                }
            }
            return bestA < float.MaxValue;
        }

        // ---- the heroes ------------------------------------------------------

        /// <summary>A hero drawn whole from its anchor (the silver crown and
        /// pyramid-top towers from their outlines, the stadium from its bowl
        /// part): true when it drew it. The spired crown and the open frame
        /// stand on their OSM tiers and only add a crown
        /// (<see cref="EmitHeroCrown"/>).</summary>
        static bool EmitHeroBody(TileMeshes tm, int hero, CityMap.Footprint f, List<Vector2> poly, float y0, float floor)
        {
            if (poly.Count < 3) return false;
            try
            {
                switch (Heroes[hero].shape)
                {
                    case HeroShape.SilverCrown: return HeroSilverCrown(tm, f, poly, y0, floor);
                    case HeroShape.PyramidTop:  return HeroPyramidTop(tm, f, poly, y0, floor);
                    case HeroShape.Bowl:        return HeroBowl(tm, hero, f, poly, y0, floor);
                    default: return false;
                }
            }
            finally { Bucket.NoCollide = false; heroOnePanel = false; }
        }

        static void EmitHeroCrown(CityMap map, TileMeshes tm, int hero, CityMap.Footprint f, float floor)
        {
            try
            {
                switch (Heroes[hero].shape)
                {
                    case HeroShape.SpiredCrown: HeroSpiredCrown(map, tm, hero, floor); break;
                    case HeroShape.OpenFrame:   HeroOpenFrame(map, tm, hero, f, floor); break;
                }
            }
            finally { Bucket.NoCollide = false; }
        }

        /// <summary>
        /// C1 THE SPIRED CROWN TOWER. OSM's tiers (B2) already step the top in
        /// round its core; the crown is a ring of upright spikes on the edge
        /// of every tier above 140 m - taller toward the top - and a needle
        /// on the top tier to the building's real height (the OSM needle part
        /// and its 7 m core column, which ran on to 300 m, are left out).
        /// </summary>
        static void HeroSpiredCrown(CityMap map, TileMeshes tm, int hero, float floor)
        {
            var fs = map.footprints;
            HeroTint(HeroMetalCol, true, 1.02f, 1.02f, 1.06f);
            HeroNoCollide(true);
            int top = -1;
            int maxFins = skylineBuild ? 6 : 14;
            foreach (int pi in heroParts[hero])
            {
                if (heroSkip.Contains(pi)) continue;
                var p = fs[pi];
                if (p.minH < 140f || p.pts.Length < 3) continue;
                if (top < 0 || p.h > fs[top].h) top = pi;
                var c = Centroid(p.pts);
                int n = p.pts.Length, step = Mathf.Max(1, Mathf.CeilToInt(n / (float)maxFins));
                float yT = floor + p.h;
                float rise = 4.5f + 0.12f * (p.h - 200f);   // the upper tiers' spikes taller
                for (int k = 0; k < n; k += step)
                {
                    var q = p.pts[k];
                    var d = q - c;
                    float r = d.magnitude;
                    if (r < 2f) continue;
                    d /= r;
                    HeroFin(tm, q - d * 0.2f, d, Mathf.Clamp(0.22f * r, 1.2f, 3.5f), yT - 0.5f, yT + rise, 0.3f, true, HeroMetalUV);
                }
            }
            if (top >= 0)
            {
                var t = fs[top];
                var c = Centroid(t.pts);
                float yT = floor + t.h;
                float ang = Mathf.Atan2(t.u.y, t.u.x) + Mathf.PI * 0.25f;
                if (!skylineBuild) HeroBox(tm, c, t.u, 1.4f, 1.4f, yT - 0.2f, yT + 3.5f, HeroMetalUV);
                HeroSpike(tm, W3(c, yT + 3.5f), 1.25f, floor + NeedleTopM, 4, ang, HeroMetalUV);
            }
            HeroNoCollide(false);
        }

        /// <summary>
        /// C2 THE OPEN-FRAME TOWER. The shaft and its sloped glass top are
        /// OSM's; OSM's stand-in slabs for the crown are replaced by an open
        /// steel frame over the same box: four corner columns, a column
        /// mid-face, ring beams in three bays and an X of bracing across every
        /// face of every bay (the skyline: columns and rings).
        /// </summary>
        static void HeroOpenFrame(CityMap map, TileMeshes tm, int hero, CityMap.Footprint f, float floor)
        {
            var fs = map.footprints;
            heroPts.Clear();
            float yt = 0f;
            foreach (int pi in heroDropped[hero])
            {
                foreach (var p in fs[pi].pts) heroPts.Add(p);
                yt = Mathf.Max(yt, fs[pi].h);
            }
            Vector2 c, u; float hu, hv;
            if (heroPts.Count < 6 || !ObbOfPoints(heroPts, out c, out u, out hu, out hv))
            { c = f.centre; u = f.u; hu = f.hu - 2f; hv = f.hv - 2f; yt = f.h + 60f; }
            float yb = floor + f.h, yTop = floor + Mathf.Max(yt, f.h + 20f);
            var v = new Vector2(-u.y, u.x);
            var K = new[] { c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv };
            HeroTint(HeroMetalCol, true, 0.50f, 0.53f, 0.58f);
            HeroNoCollide(true);
            const int Bays = 3;
            for (int i = 0; i < 4; i++)
            {
                var a = K[i]; var b = K[(i + 1) % 4];
                HeroBeam(tm, W3(a, yb), W3(a, yTop + 0.6f), 0.75f, HeroMetalUV);
                for (int k = 1; k <= Bays; k++)
                {
                    float y = Mathf.Lerp(yb, yTop, k / (float)Bays);
                    HeroBeam(tm, W3(a, y), W3(b, y), 0.55f, HeroMetalUV);
                }
                if (skylineBuild) continue;
                var m = (a + b) * 0.5f;
                HeroBeam(tm, W3(m, yb), W3(m, yTop), 0.45f, HeroMetalUV);
                for (int k = 0; k < Bays; k++)
                {
                    float y0b = Mathf.Lerp(yb, yTop, k / (float)Bays), y1b = Mathf.Lerp(yb, yTop, (k + 1) / (float)Bays);
                    HeroBeam(tm, W3(a, y0b), W3(m, y1b), 0.32f, HeroMetalUV);
                    HeroBeam(tm, W3(m, y0b), W3(b, y1b), 0.32f, HeroMetalUV);
                    HeroBeam(tm, W3(m, y0b), W3(a, y1b), 0.32f, HeroMetalUV);
                    HeroBeam(tm, W3(b, y0b), W3(m, y1b), 0.32f, HeroMetalUV);
                }
            }
            HeroNoCollide(false);
        }

        /// <summary>
        /// C3 THE SILVER CROWN TOWER (OSM's parts carry no heights; a hand
        /// table by eye): a stone podium of seven storeys over the lot, a
        /// stone shaft set in from it, two setbacks near the top, and a
        /// silver lantern that FLARES back out over the last setback, ringed
        /// by upright fins that stand proud of the roof, tallest mid-face.
        /// </summary>
        static bool HeroSilverCrown(TileMeshes tm, CityMap.Footprint f, List<Vector2> poly, float y0, float floor)
        {
            float H = f.h;
            float yPod = floor + 26f, ySet1 = floor + 0.76f * H, ySet2 = floor + 0.84f * H, yCrown = floor + 0.885f * H, yTop = floor + H;
            if (!InsetPoly(poly, 1.5f, heroP1)) { heroP1.Clear(); heroP1.AddRange(poly); }
            if (!InsetPoly(heroP1, 2.5f, heroP2)) { heroP2.Clear(); heroP2.AddRange(heroP1); }
            if (!InsetPoly(heroP2, 2.5f, heroP3)) { heroP3.Clear(); heroP3.AddRange(heroP2); }
            var roof = buckets[(int)Slot.RoofFlat];
            // the stone (granite-grey precast), its storeys from the ground
            PickFacade(f.centre, 0, 1, 3, new Color32(186, 182, 176, 255), floor, yCrown - floor);
            Walls(tm, poly, y0, yPod);
            EarcutInto(roof, poly, yPod, tm.origin, RoofFlatM);
            Walls(tm, heroP1, yPod, ySet1);
            EarcutInto(roof, heroP1, ySet1, tm.origin, RoofFlatM);
            HeroNoCollide(true);
            Walls(tm, heroP2, ySet1, ySet2);
            EarcutInto(roof, heroP2, ySet2, tm.origin, RoofFlatM);
            Walls(tm, heroP3, ySet2, yCrown);
            // the lantern: silver grid as metal, sloping out from the last
            // setback's line to the one below it
            facLook = LookSilver;
            HeroTint(LookSilver, true, 1.15f, 1.17f, 1.22f);
            var bk = buckets[(int)Slot.FacadeGlass];
            var lk = FacadeLooks[LookSilver];
            float rep = Mathf.Max(0.5f, facFloorH * lk.floors);
            int n = heroP3.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a0 = heroP3[i], b0 = heroP3[(i + 1) % n], a1 = heroP2[i], b1 = heroP2[(i + 1) % n];
                var d = b1 - a1;
                if (d.sqrMagnitude < 0.04f) continue;
                var outw = new Vector2(d.y, -d.x).normalized;
                float ur = Mathf.Max(1f, Mathf.Round(d.magnitude / lk.uM));
                float va = (yCrown - floor) / rep, vb = (yTop - floor) / rep;
                bk.Face(Lw(tm, W3(a0, yCrown)), Lw(tm, W3(a1, yTop)), Lw(tm, W3(b1, yTop)), Lw(tm, W3(b0, yCrown)),
                        new Vector3(outw.x, 0.15f, outw.y),
                        new Vector2(0f, va), new Vector2(0f, vb), new Vector2(ur, vb), new Vector2(ur, va));
            }
            EarcutInto(roof, heroP2, yTop, tm.origin, RoofFlatM);
            // the fins, on the lantern's top edge: from the setback up past
            // the roof, stepped taller toward the middle of each face
            HeroTint(HeroMetalCol, true, 1.12f, 1.14f, 1.2f);
            float spacing = skylineBuild ? 14f : 6.5f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = heroP2[i], b = heroP2[(i + 1) % n];
                var d = b - a; float len = d.magnitude;
                if (len < 3f) continue;
                d /= len;
                var outw = new Vector2(d.y, -d.x);
                int cnt = Mathf.Max(1, Mathf.FloorToInt(len / spacing));
                for (int k = 0; k <= cnt; k++)
                {
                    float s = k / (float)cnt;
                    float mid = 1f - Mathf.Abs(2f * s - 1f);              // 0 at the corners, 1 mid-face
                    float rise = 3f + 3f * Mathf.Round(mid * 3f);        // 3, 6, 9, 12 m: the face steps up to its middle
                    if (k == 0) rise = 8f;                               // the corner fins stand tall
                    var p = a + d * (len * s) + outw * 1.1f;
                    HeroFin(tm, p, outw, 3.0f, yCrown + 1.5f, yTop + rise, 0.4f, false, HeroMetalUV);
                }
            }
            HeroNoCollide(false);
            return true;
        }

        /// <summary>
        /// C4 THE PYRAMID-TOP TOWER: a stone shaft, a metal cornice, a steep
        /// copper-green pyramid over the roof and a slim four-legged lattice
        /// spire on its apex to the outline's full height.
        /// </summary>
        static bool HeroPyramidTop(TileMeshes tm, CityMap.Footprint f, List<Vector2> poly, float y0, float floor)
        {
            float H = f.h;
            float yShaft = floor + H - 30f, yBand = yShaft + 2f, yApex = floor + H - 9f, yTip = floor + H;
            if (yShaft - floor < 20f) return false;
            PickFacade(f.centre, 0, 1, 3, new Color32(196, 176, 150, 255), floor, yShaft - floor);
            Walls(tm, poly, y0, yShaft);
            HeroNoCollide(yShaft - floor > HeroCollideTopM);
            // the cornice: the silver grid as metal, in the roof's green
            facLook = LookSilver;
            HeroTint(LookSilver, true, 0.62f, 0.92f, 0.80f);
            Walls(tm, poly, yShaft, yBand);
            EarcutInto(buckets[(int)Slot.RoofFlat], poly, yBand, tm.origin, RoofFlatM);
            if (!InsetPoly(poly, 0.6f, heroP1)) { heroP1.Clear(); heroP1.AddRange(poly); }
            var apexXZ = Centroid(heroP1);
            var apex = W3(apexXZ, yApex);
            HeroTint(HeroMetalCol, true, 0.50f, 0.86f, 0.70f);
            int n = heroP1.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = heroP1[i], b = heroP1[(i + 1) % n];
                var d = b - a;
                if (d.sqrMagnitude < 0.04f) continue;
                var outw = new Vector2(d.y, -d.x).normalized;
                HeroTri(tm, W3(a, yBand), W3(b, yBand), apex, new Vector3(outw.x, 0.8f, outw.y), HeroMetalUV);
            }
            // the spire: four legs from a 2 m square low on the pyramid to
            // the tip, tied at two heights
            HeroTint(HeroMetalCol, true, 0.80f, 0.82f, 0.86f);
            var v = new Vector2(-f.u.y, f.u.x);
            float yLeg = yApex - 3f, half = 1.6f;
            var tip = W3(apexXZ, yTip);
            var legs = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                var o = (k == 0 ? f.u + v : k == 1 ? -f.u + v : k == 2 ? -f.u - v : f.u - v) * half;
                legs[k] = W3(apexXZ + o, yLeg);
                HeroBeam(tm, legs[k], tip, 0.2f, HeroMetalUV);
            }
            if (!skylineBuild)
                foreach (float t in new[] { 0.45f, 0.7f })
                    for (int k = 0; k < 4; k++)
                        HeroBeam(tm, Vector3.Lerp(legs[k], tip, t), Vector3.Lerp(legs[(k + 1) % 4], tip, t), 0.12f, HeroMetalUV);
            HeroNoCollide(false);
            return true;
        }

        /// <summary>
        /// C5 THE STADIUM. OSM's 38 parts drew it as solid tiers to 43 m; the
        /// bowl part's own outline becomes the OUTER WALL (precast, the only
        /// part that collides), and inside it the stands step down from the
        /// rim to an open field: an upper deck raked steep, a fascia band, a
        /// lower deck and the field wall - spokes from the wall's own corners
        /// to a rounded field edge along the bowl's long axis. The two board
        /// parts become BLANK boards on the rim (no screen content). The
        /// corner towers and the outer ring's parts stay OSM's.
        /// </summary>
        static bool HeroBowl(TileMeshes tm, int hero, CityMap.Footprint f, List<Vector2> poly, float y0, float floor)
        {
            float rim = f.h;
            if (rim < 15f || f.hv < 40f) return false;
            // the outer wall, collided
            PickFacade(f.centre, 1, 3, 3, new Color32(198, 196, 190, 255), floor, rim);
            heroOnePanel = true;
            Walls(tm, poly, y0, floor + rim);
            heroOnePanel = false;
            HeroNoCollide(true);

            var c = f.centre; var u = f.u; var v = new Vector2(-u.y, u.x);
            // the field's edge: a rounded box along the long axis
            float fa = Mathf.Min(72f, f.hu * 0.64f), fb = Mathf.Min(46f, f.hv * 0.46f);
            // the field stands on the highest ground under it (the outline's
            // floor is its lowest), so the terrain never shows through
            float fieldY = floor;
            for (int k = 0; k < 5; k++)
            {
                var q = c + u * (k == 1 ? fa * 0.6f : k == 2 ? -fa * 0.6f : 0f) + v * (k == 3 ? fb * 0.6f : k == 4 ? -fb * 0.6f : 0f);
                fieldY = Mathf.Max(fieldY, CityElevation.GroundY(heroMap, q.x, q.y));
            }
            float fb0 = Mathf.Min(fieldY - floor, 8f);
            // the section, from the rim (s = 1 at the wall) to the field (s = 0):
            // (s, height above the outline's floor) and what each run is
            var S = new[] { 1f, 0.965f, 0.52f, 0.52f, 0.49f, 0.035f, 0f };
            var Y = new[] { rim, rim - 1.2f, 21.5f + fb0 * 0.5f, 17.5f + fb0 * 0.5f, 15.5f + fb0 * 0.5f, 2.6f + fb0, fb0 + 0.1f };
            // concrete rim, blue upper deck, dark fascia, concrete step, blue lower deck, dark field wall
            var kind = new[] { 0, 1, 2, 0, 1, 2 };
            int n = poly.Count;
            var dirs = new Vector2[n]; var rIn = new float[n]; var rOut = new float[n];
            for (int i = 0; i < n; i++)
            {
                var d = poly[i] - c;
                float r = d.magnitude;
                dirs[i] = r > 1e-3f ? d / r : u;
                rOut[i] = r;
                float du = Mathf.Abs(Vector2.Dot(dirs[i], u)) / fa, dv = Mathf.Abs(Vector2.Dot(dirs[i], v)) / fb;
                float rr = 1f / Mathf.Pow(Mathf.Pow(du, 4f) + Mathf.Pow(dv, 4f), 0.25f);   // a superellipse
                rIn[i] = Mathf.Min(rr, r - 18f);
            }
            Vector3 P(int si, int sk) => W3(c + dirs[si] * Mathf.Lerp(rIn[si], rOut[si], S[sk]), floor + Y[sk]);
            for (int k = 0; k + 1 < S.Length; k++)
            {
                switch (kind[k])
                {
                    case 0: HeroTint(HeroMatteCol, false, 0.95f, 0.95f, 0.95f); break;
                    case 1: HeroTint(HeroMatteCol, false, 0.35f, 0.45f, 1.25f); break;
                    default: HeroTint(HeroMatteCol, false, 0.38f, 0.38f, 0.42f); break;
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 a = P(i, k), b = P(j, k), bq = P(j, k + 1), aq = P(i, k + 1);
                    // the run's normal: up and in for a rake, in for a fascia
                    var radial = W3(((dirs[i] + dirs[j]) * 0.5f), 0f).normalized;
                    float dr = (Mathf.Lerp(rIn[i], rOut[i], S[k]) - Mathf.Lerp(rIn[i], rOut[i], S[k + 1]));
                    float dy = Y[k] - Y[k + 1];
                    var facing = -radial * dy + Vector3.up * dr;
                    if (facing.sqrMagnitude < 1e-6f) facing = -radial;
                    HeroQuad(tm, a, b, bq, aq, facing, HeroMatteUV);
                }
            }
            // the field: plain turf green, no markings
            HeroTint(HeroMatteCol, false, 0.40f, 0.98f, 0.40f);
            {
                var mid = W3(c, floor + Y[S.Length - 1]);
                for (int i = 0; i < n; i++)
                    HeroTri(tm, P(i, S.Length - 1), P((i + 1) % n, S.Length - 1), mid, Vector3.up, HeroMatteUV);
            }
            // the boards: blank, dark, on the rim where OSM has them
            HeroTint(HeroMetalCol, true, 0.35f, 0.35f, 0.38f);
            var fs = heroMap.footprints;
            foreach (int bi in heroDropped[hero])
            {
                var b = fs[bi];
                if (b.h < 50f || 2f * b.hv >= 8f) continue;
                HeroBox(tm, b.centre, b.u, b.hu, 0.8f, floor + rim - 3f, floor + b.h, HeroMetalUV);
            }
            HeroNoCollide(false);
            return true;
        }
    }
}
