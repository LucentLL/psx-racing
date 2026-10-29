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
        // 0 in the night mask): column 0's white frame, column 3's plain precast.
        // (2026-10-29, the 1990s city: the building pass re-laid column 0 at
        // 8 floors a repeat, and (0.488, 0.8125) became a GLASS PANE - albedo
        // (75, 89, 89), night mask glass - so every hero's metal read slate
        // blue by day and a floodlit member washed teal. Now the left mullion
        // where a spandrel crosses it: texel 2, (194-220) grey-white, mask 0.)
        static readonly Vector2 HeroMetalUV = new Vector2(0.0195f, 0.8926f);
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
        /// column, metal or not, a linear multiplier. A hero's metal is its
        /// crown (spikes, needle, frame, lantern, fins, pyramid, spire), so it
        /// is floodlit after dark unless <paramref name="crown"/> says not.</summary>
        static void HeroTint(int col, bool metal, float r, float g, float b, bool crown = true)
        {
            Bucket.Tint = new Color32(TintByte(r), TintByte(g), TintByte(b), (byte)(col * 32 + (metal ? FacadeMetalBit : 0) + facLed));
            FacadeCrown(metal && crown);
        }

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
        /// base to a point at tipY - or, with <paramref name="neckAt"/>, a
        /// shaft narrowing to <paramref name="neckR"/> that share of the way
        /// up, then the point. A crown member (the needle): PSX/Lit keeps
        /// its rings a pixel across far off, so the shaft stays drawn and
        /// only the point thins out.</summary>
        static void HeroSpike(TileMeshes tm, Vector3 baseC, float r, float tipY, int sides, float rot, Vector2 uv, float neckAt = 0f, float neckR = 0f)
        {
            HeroMember(tm, new Vector2(baseC.x, baseC.z), 0f, -2f);
            var tip = new Vector3(baseC.x, tipY, baseC.z);
            float h = Mathf.Max(0.1f, tipY - baseC.y);
            var neck = baseC + Vector3.up * (h * neckAt);
            for (int k = 0; k < sides; k++)
            {
                float a0 = rot + k * Mathf.PI * 2f / sides, a1 = rot + (k + 1) * Mathf.PI * 2f / sides;
                var e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var p = baseC + e0 * r;
                var q = baseC + e1 * r;
                var mid = (e0 + e1) * 0.5f;
                if (neckAt > 0f)
                {
                    var pn = neck + e0 * neckR; var qn = neck + e1 * neckR;
                    HeroQuad(tm, p, q, qn, pn, mid * r + Vector3.up * ((r - neckR) * r / Mathf.Max(0.1f, h * neckAt)), uv);
                    HeroTri(tm, pn, qn, tip, mid * neckR + Vector3.up * (neckR * neckR / Mathf.Max(0.1f, tipY - neck.y)), uv);
                }
                else HeroTri(tm, p, q, tip, mid * r + Vector3.up * (r * r / h), uv);
            }
            Bucket.MemberOn = false;
        }

        /// <summary>The vertices emitted next belong to a crown member round
        /// the vertical line through <paramref name="core"/> (world plan),
        /// facing <paramref name="bearing"/>: <see cref="Bucket.MemberOn"/>.</summary>
        static void HeroMember(TileMeshes tm, Vector2 core, float bearing, float w)
        {
            Bucket.MemberOn = true;
            Bucket.MemberA = new Vector2(core.x - tm.origin.x, core.y - tm.origin.z);
            Bucket.MemberDir = bearing;
            Bucket.MemberW = w;
        }

        /// <summary>A crown fin: a thin blade in the vertical plane of
        /// <paramref name="dir"/> (outward), its outer edge at
        /// <paramref name="outer"/> from y0 to yTop, its foot running
        /// <paramref name="depth"/> back in; a spike when
        /// <paramref name="pointed"/> (the top runs down to the inner foot),
        /// else a straight-topped blade. Closed on every side but the foot.
        /// A crown member round its outer edge: PSX/Lit keeps it at least a
        /// pixel thick and a pixel deep (a blade 0.6 m thick is a fifth of a
        /// pixel at 1.3 km, and popped in and out as the eye moved), and in
        /// the far skyline a blade turned toward the eye is drawn a little
        /// nearer than it is, so it never ties with the wall behind it.</summary>
        static void HeroFin(TileMeshes tm, Vector2 outer, Vector2 dir, float depth, float y0, float yTop, float half, bool pointed, Vector2 uv)
        {
            HeroMember(tm, outer, Mathf.Atan2(dir.y, dir.x), -1f);
            var n = new Vector2(-dir.y, dir.x) * half;
            Vector2 inner = outer - dir * depth;
            // the top's back edge: a spike's is a hair behind its point, so far
            // off (where the blade is given a pixel's depth) the point is a
            // pixel wide, not a sliver seen side-on
            Vector2 back = pointed ? outer - dir * 0.05f : inner;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                var o = n * sgn;
                HeroQuad(tm, W3(outer + o, y0), W3(outer + o, yTop), W3(back + o, yTop), W3(inner + o, y0), W3(o, 0f), uv);
            }
            HeroQuad(tm, W3(outer + n, y0), W3(outer + n, yTop), W3(outer - n, yTop), W3(outer - n, y0), W3(dir, 0f), uv);
            HeroQuad(tm, W3(outer + n, yTop), W3(back + n, yTop), W3(back - n, yTop), W3(outer - n, yTop), Vector3.up, uv);
            // the back (a spike's slope, a blade's inner end): seen from the
            // ring's far side a blade edge-on was nothing
            HeroQuad(tm, W3(inner + n, y0), W3(back + n, yTop), W3(back - n, yTop), W3(inner - n, y0),
                     W3(-dir, 0f) + Vector3.up * (pointed ? depth / Mathf.Max(0.1f, yTop - y0) : 0f), uv);
            Bucket.MemberOn = false;
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
            // every fin in the far skyline too (owner 2026-10-16: stop the
            // flicker, never remove objects - PSX/Lit keeps each a pixel wide)
            const int maxFins = 14;
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
                    // (the far skyline's fins stand ON the roof line: the half
                    // metre sunk behind the wall tied with it in the squeeze)
                    HeroFin(tm, q - d * 0.2f, d, Mathf.Clamp(0.22f * r, 1.2f, 3.5f), yT - (skylineBuild ? 0f : 0.5f), yT + rise, 0.3f, true, HeroMetalUV);
                }
            }
            if (top >= 0)
            {
                var t = fs[top];
                var c = Centroid(t.pts);
                float yT = floor + t.h;
                float ang = Mathf.Atan2(t.u.y, t.u.x) + Mathf.PI * 0.25f;
                HeroBox(tm, c, t.u, 1.4f, 1.4f, yT - 0.2f, yT + 3.5f, HeroMetalUV);
                HeroSpike(tm, W3(c, yT + 3.5f), 1.25f, floor + NeedleTopM, 4, ang, HeroMetalUV, 0.8f, 0.45f);
            }
            HeroNoCollide(false);
        }

        /// <summary>
        /// C2 THE OPEN-FRAME TOWER. The shaft and its sloped glass tops are
        /// OSM's; so is the crown's frame, drawn as mapped round them
        /// (2026-10-12; the braced cage is gone): each face's glass runs on
        /// up to the sloped white member along its top edge, and the bridge
        /// across the top, its two short posts and the ridge beam over them
        /// are white, with the sky through the openings between (2026-10-29).
        /// </summary>
        static void HeroOpenFrame(CityMap map, TileMeshes tm, int hero, CityMap.Footprint f, float floor)
        {
            // 2026-10-12, the owner ("I live in Charlotte and I've never seen
            // this"): the first cut threw OSM's crown pieces away and drew an
            // invented braced cage that read as scaffolding. The real crown
            // is what OSM maps: two glass slopes in a frame of white concrete
            // - a sloped piece along the top of each face, posts, a bridge
            // across the top at 218-220 m, a ridge beam at 230-240 m.
            // 2026-10-29, the owner on his photographs ("a strange glass/light
            // layer in triangles on top"): the sloped pieces were drawn SOLID
            // WHITE, so each face carried a white triangle from 180 m up to
            // its top edge, and after dark the crown wash lit them as one
            // big blue triangle. OSM tags them otherwise: their walls the
            // tower's glass (building:colour = the shaft's), only their tops
            // white (roof:colour) - and the real faces are glass right up to
            // a white edge, sky through the frame above the glass slopes. So
            // a piece coloured like its tower's glass wears the shaft's own
            // facade, storeys and all, and only its sloped top is white. Its
            // glass twin (OSM maps each face's top twice: as glass and as
            // concrete on the same plan, a metre or so over it) is that same
            // glass, drawn once as the concrete piece's walls (two copies of
            // one wall fought in depth). After dark (the 1990s city,
            // 2026-10-29: no LED lines - "LEDs were not common") the WHITE
            // members are floodlit from the roof as a 1990s crown was, in the
            // flood NightCrowns gives the tower (metal halide), and the glass
            // under them stays dark: the frame reads as white lines on the sky.
            var fs = map.footprints;
            // (the crown's glass marked crown + FloodDark: no wash, and PSX/Lit
            // lights no office windows in it - nobody works in a crown)
            if (NightFrameOf(f.landmark)) { FacadeCrown(true); FacadeFlood(FloodDark); }
            var glass = Bucket.Tint;   // the shaft's own facade (PickFacade), before the white
            HeroTint(HeroMetalCol, false, 0.93f, 0.94f, 0.95f);
            if (NightFrameOf(f.landmark)) { FacadeCrown(true); FacadeFlood(NightFloodOf(f.landmark)); }
            var white = Bucket.Tint;
            HeroNoCollide(true);
            foreach (int pi in heroDropped[hero])
            {
                var p = fs[pi];
                var pts = p.pts;
                int n = pts.Length;
                if (n < 3) continue;
                if (p.mat == 1 && FrameTwinOf(fs, hero, pi)) continue;
                bool glassWalls = p.mat == 1 || (p.colour.a == 255 && f.colour.a == 255 && p.colour.r == f.colour.r && p.colour.g == f.colour.g && p.colour.b == f.colour.b);
                float y0 = floor + p.minH, top = floor + p.h;
                float rise = p.roof == 4 ? Mathf.Clamp(p.roofH, 0f, top - y0) : 0f;
                Vector2 dir = p.roofDir >= 0
                    ? new Vector2(Mathf.Sin(p.roofDir * Mathf.Deg2Rad), Mathf.Cos(p.roofDir * Mathf.Deg2Rad))
                    : new Vector2(p.u.y, -p.u.x);
                float s0 = float.MaxValue, s1 = float.MinValue;
                foreach (var q in pts) { float sp = Vector2.Dot(q, dir); s0 = Mathf.Min(s0, sp); s1 = Mathf.Max(s1, sp); }
                float span = Mathf.Max(0.5f, s1 - s0);
                float YAt(Vector2 q) => top - rise * (Vector2.Dot(q, dir) - s0) / span;
                Bucket.Tint = glassWalls ? glass : white;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i]; var c = pts[(i + 1) % n];
                    var d = c - a;
                    if (d.sqrMagnitude < 0.01f) continue;
                    float ya = YAt(a), yc = YAt(c);
                    if (ya < y0 + 0.02f && yc < y0 + 0.02f) continue;   // the slope down to its floor: no wall
                    var nrm = new Vector3(d.y, 0f, -d.x);
                    if (glassWalls) FrameGlassWall(tm, a, c, y0, ya, yc, nrm);
                    else HeroQuad(tm, W3(a, y0), W3(c, y0), W3(c, yc), W3(a, ya), nrm, HeroMetalUV);
                }
                // the top (earcut: a thin wedge's fan from its middle strayed
                // outside it), white unless the piece is glass through
                Bucket.Tint = p.mat == 1 ? glass : white;
                FrameCap(tm, pts, YAt, p.mat == 1, true);
                Bucket.Tint = white;
                if (p.minH > 1f) FrameCap(tm, pts, q => y0, false, false);
            }
            HeroNoCollide(false);
        }

        /// <summary>A thin glass crown piece of the open frame that OSM maps
        /// again as concrete on the same plan (its twin, which draws it).</summary>
        static bool FrameTwinOf(CityMap.Footprint[] fs, int hero, int pi)
        {
            var p = fs[pi];
            var c = Centroid(p.pts);
            float area = 4f * p.hu * p.hv;
            foreach (int qi in heroDropped[hero])
            {
                var q = fs[qi];
                if (qi == pi || q.mat == 1 || q.pts.Length < 3) continue;
                if ((Centroid(q.pts) - c).sqrMagnitude < 4f && Mathf.Abs(4f * q.hu * q.hv - area) < 0.3f * area) return true;
            }
            return false;
        }

        /// <summary>The tower's glass carried up a frame piece's wall: the
        /// shaft's own facade (the tint the caller set), storeys counted from
        /// its ground floor, the top sloping with the piece's.</summary>
        static void FrameGlassWall(TileMeshes tm, Vector2 a, Vector2 c, float y0, float ya, float yc, Vector3 facing)
        {
            var lk = FacadeLooks[facLook];
            float rep = Mathf.Max(0.5f, facFloorH * lk.floors);
            float ur = Mathf.Max(1f, Mathf.Round(Vector2.Distance(a, c) / lk.uM));
            float v0 = (y0 - facBase) / rep;
            buckets[(int)Slot.FacadeGlass].Face(Lw(tm, W3(a, y0)), Lw(tm, W3(a, ya)), Lw(tm, W3(c, yc)), Lw(tm, W3(c, y0)), facing,
                new Vector2(0f, v0), new Vector2(0f, (ya - facBase) / rep), new Vector2(ur, (yc - facBase) / rep), new Vector2(ur, v0));
        }

        /// <summary>A frame piece's top (<paramref name="up"/>) or floor, each
        /// corner at <paramref name="yAt"/>: the white member's one texel, or
        /// the shaft's facade (<paramref name="facade"/>, a glass piece's top).
        /// Earcut through the flat-roof bucket's scratch.</summary>
        static void FrameCap(TileMeshes tm, Vector2[] pts, System.Func<Vector2, float> yAt, bool facade, bool up)
        {
            var rf = buckets[(int)Slot.RoofFlat];
            int v0 = rf.v.Count, t0 = rf.t.Count;
            heroPts.Clear();
            heroPts.AddRange(pts);
            EarcutInto(rf, heroPts, 0f, tm.origin, RoofFlatM);
            var lk = FacadeLooks[facLook];
            float rep = Mathf.Max(0.5f, facFloorH * lk.floors);
            Vector2 UvOf(Vector2 q, float y) => facade ? new Vector2((q.x + q.y) / lk.uM, (y - facBase) / rep) : HeroMetalUV;
            for (int j = t0; j + 2 < rf.t.Count; j += 3)
            {
                Vector3 la = rf.v[rf.t[j]], lb = rf.v[rf.t[j + 1]], lc = rf.v[rf.t[j + 2]];
                Vector2 qa = new Vector2(la.x + tm.origin.x, la.z + tm.origin.z), qb = new Vector2(lb.x + tm.origin.x, lb.z + tm.origin.z), qc = new Vector2(lc.x + tm.origin.x, lc.z + tm.origin.z);
                float ya = yAt(qa), yb = yAt(qb), yc = yAt(qc);
                Vector3 a = Lw(tm, W3(qa, ya)), b = Lw(tm, W3(qb, yb)), c = Lw(tm, W3(qc, yc));
                var bk = buckets[(int)Slot.FacadeGlass];
                bool faces = Vector3.Dot(Vector3.Cross(c - a, b - a), up ? Vector3.up : Vector3.down) >= 0f;
                if (faces) bk.Tri(a, b, c, UvOf(qa, ya), UvOf(qb, yb), UvOf(qc, yc));
                else bk.Tri(a, c, b, UvOf(qa, ya), UvOf(qc, yc), UvOf(qb, yb));
            }
            rf.t.RemoveRange(t0, rf.t.Count - t0);
            rf.v.RemoveRange(v0, rf.v.Count - v0);
            rf.uv.RemoveRange(v0, rf.uv.Count - v0);
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
            PickFacade(f.centre, 0, 1, 3, new Color32(186, 182, 176, 255), floor, yCrown - floor, f.rhythm);
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
            const float spacing = 6.5f;   // the far skyline's too: every fin (PSX/Lit keeps each a pixel wide)
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
            PickFacade(f.centre, 0, 1, 3, new Color32(196, 176, 150, 255), floor, yShaft - floor, f.rhythm);
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
        // ------------------------------------------------------------------
        //  THE STADIUM AS THE OWNER'S AERIAL SHOWS IT (the skyline pass,
        //  2026-10-29: "ours is a dark grey drum with a dark navy bowl"). OSM
        //  tags none of its 38 parts, so the ring round the bowl took the
        //  building's hashed look - a blue glass tower's - and the stands a
        //  navy. In the photograph: the ring's charcoal panels in a white
        //  precast grid, white domes on the gate towers, the bowl's back a
        //  light concrete wall with a row of tall ARCHES (the concourse's
        //  colonnade), bright teal-blue seats in both decks between light
        //  concrete fascias, and a big black scoreboard at each end - blank
        //  here: no name, logo or team mark (the game is sold).
        //  PSX_CITY_LOOKFIX=0 is the stadium as it was.
        // ------------------------------------------------------------------
        /// <summary>The colonnade: arches this far apart round the bowl's back,
        /// their openings this share of it, from/to this high over the floor.</summary>
        const float ArchPitchM = 8.5f, ArchOpenShare = 0.62f, ArchFromM = 21f, ArchToM = 34f;
        /// <summary>A plain white texel of the precast ribbon column (6): the
        /// middle of a white band (212, linear 0.66; its night mask is no
        /// window) - the stadium's concrete and its seats' base, matte.</summary>
        static readonly Vector2 HeroWhiteUV = new Vector2(0.5f, 0.740f);
        const int HeroWhiteCol = LookPrecast;

        /// <summary>A hero tint that may go darker than the facade's own floor
        /// (TintByte keeps 0.35 of a texel at least: a seat's teal, a
        /// scoreboard's black need less red, less of everything).</summary>
        static void HeroTintLow(int col, bool metal, float r, float g, float b)
        {
            byte T(float k) => (byte)(Mathf.Clamp(Mathf.RoundToInt(k * 127.5f), 4, 254) & 0xFE);
            Bucket.Tint = new Color32(T(r), T(g), T(b), (byte)(col * 32 + (metal ? FacadeMetalBit : 0) + facLed));
            FacadeCrown(false);
        }

        /// <summary>The stadium's own parts round the bowl (EmitFootprint,
        /// after their pick): the ring's charcoal panels in a white precast
        /// grid - the silver grid's frames, its panes dark: as METAL (B1b),
        /// so a pane is the panel's matte charcoal under a little of the sky,
        /// not a curtain wall's glass mirroring all of it.</summary>
        static void StadiumPartLook(int hero, CityMap.Footprint f, int fi)
        {
            if (LookFixOff || Heroes[hero].shape != HeroShape.Bowl || heroAnchorOf[hero] == fi) return;
            facLook = LookSilver;
            var t = Bucket.Tint;
            Bucket.Tint = new Color32(TintR(0.66f, LookSilver), TintByte(0.68f), TintByte(0.72f), (byte)((t.a & 15) | FacadeMetalBit | ((LookSilver & 7) * 32)));
        }

        /// <summary>A stadium part's roof shape (EmitFootprint): the gate
        /// towers' domes are white.</summary>
        static void StadiumRoofLook(int hero, CityMap.Footprint f)
        {
            if (LookFixOff || hero < 0 || Heroes[hero].shape != HeroShape.Bowl || f.roof != 2) return;
            var t = Bucket.Tint;
            Bucket.Tint = new Color32(TintR(1.99f, LookSilver), TintByte(1.99f), TintByte(1.99f), t.a);
        }

        /// <summary>The colonnade on the bowl's back: a dark arched opening
        /// every <see cref="ArchPitchM"/> round the wall (whole arches only,
        /// none across a corner), a hand's breadth proud of it.</summary>
        static void BowlArches(TileMeshes tm, List<Vector2> poly, float floor)
        {
            HeroTintLow(HeroMatteCol, false, 0.20f, 0.22f, 0.27f);
            float a0 = floor + ArchFromM, a1 = floor + ArchToM;
            int n = poly.Count;
            // round the wall by its length (a curved bowl is many short walls):
            // an arch every pitch, its opening the chord between the wall's
            // points either side of its middle, a hand's breadth proud of it;
            // none across a corner (a turn of CornerMinDeg or more)
            var cum = new float[n + 1];
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + Vector2.Distance(poly[i], poly[(i + 1) % n]);
            float total = cum[n];
            if (total < ArchPitchM * 4f) return;
            Vector2 At(float s, out int seg)
            {
                s = Mathf.Repeat(s, total);
                seg = 0;
                while (seg < n - 1 && cum[seg + 1] < s) seg++;
                float L = Mathf.Max(cum[seg + 1] - cum[seg], 1e-4f);
                return Vector2.Lerp(poly[seg], poly[(seg + 1) % n], (s - cum[seg]) / L);
            }
            int count = Mathf.FloorToInt(total / ArchPitchM);
            float pitch = total / count;
            float w = pitch * ArchOpenShare, r = w * 0.5f;
            float spring = a1 - r;
            for (int k = 0; k < count; k++)
            {
                float sm = (k + 0.5f) * pitch;
                Vector2 pl = At(sm - r, out int segL), pr = At(sm + r, out int segR);
                var chord = pr - pl;
                if (chord.sqrMagnitude < 0.25f * w * w) continue;
                var d = chord.normalized;
                // a corner inside the opening: no arch there
                bool corner = false;
                for (int s = segL; s != segR && !corner; s = (s + 1) % n)
                {
                    var e0 = poly[(s + 1) % n] - poly[s];
                    var e1 = poly[(s + 2) % n] - poly[(s + 1) % n];
                    if (Vector2.Angle(e0, e1) >= CornerMinDeg) corner = true;
                }
                if (corner) continue;
                var outw = new Vector2(d.y, -d.x);
                var face = W3(outw, 0f);
                {
                    var c = (pl + pr) * 0.5f + outw * 0.3f;
                    Vector2 l = c - d * r, rr = c + d * r;
                    HeroQuad(tm, W3(l, a0), W3(l, spring), W3(rr, spring), W3(rr, a0), face, HeroMatteUV);
                    // the round head: a fan of four from the spring line's middle
                    var hub = W3(c, spring);
                    for (int q = 0; q < 4; q++)
                    {
                        float t0 = Mathf.PI * q / 4f, t1 = Mathf.PI * (q + 1) / 4f;
                        var p0 = W3(c - d * (r * Mathf.Cos(t0)), spring + r * Mathf.Sin(t0));
                        var p1 = W3(c - d * (r * Mathf.Cos(t1)), spring + r * Mathf.Sin(t1));
                        HeroTri(tm, hub, p0, p1, face, HeroMatteUV);
                    }
                }
            }
        }

        static bool HeroBowl(TileMeshes tm, int hero, CityMap.Footprint f, List<Vector2> poly, float y0, float floor)
        {
            float rim = f.h;
            if (rim < 15f || f.hv < 40f) return false;
            // the outer wall, collided
            PickFacade(f.centre, 1, 3, 3, new Color32(198, 196, 190, 255), floor, rim);
            heroOnePanel = true;
            if (!LookFixOff)
            {
                // (the skyline pass) light concrete with no office windows: the
                // structure's own stone strip (column 7: no window in its night
                // mask), one strip across, laid storey by storey up the wall (so
                // the night darkens it up the wall as it does every other)
                facLook = LookParts;
                Bucket.Tint = new Color32(TintR(0.92f, LookParts), TintByte(0.92f), TintByte(0.90f), (byte)((LookParts & 7) * 32 + facLed));
                float rep = Mathf.Max(0.5f, facFloorH * FacadeLooks[LookParts].floors);
                float v0 = (y0 - facBase) / rep, v1 = (floor + rim - facBase) / rep;
                var wb = buckets[(int)Slot.FacadeGlass];
                int pn = poly.Count;
                for (int i = 0; i < pn; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % pn];
                    var d = b - a;
                    if (d.sqrMagnitude < 0.04f) continue;
                    var o = new Vector2(d.y, -d.x).normalized;
                    wb.Face(Lw(tm, W3(a, y0)), Lw(tm, W3(a, floor + rim)), Lw(tm, W3(b, floor + rim)), Lw(tm, W3(b, y0)), W3(o, 0f),
                            new Vector2(0.25f, v0), new Vector2(0.25f, v1), new Vector2(0.25f, v1), new Vector2(0.25f, v0));
                }
            }
            else Walls(tm, poly, y0, floor + rim);
            heroOnePanel = false;
            HeroNoCollide(true);
            if (!LookFixOff && !skylineBuild) BowlArches(tm, poly, floor);

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
                if (LookFixOff)
                    switch (kind[k])
                    {
                        case 0: HeroTint(HeroMatteCol, false, 0.95f, 0.95f, 0.95f); break;
                        case 1: HeroTint(HeroMatteCol, false, 0.35f, 0.45f, 1.25f); break;
                        default: HeroTint(HeroMatteCol, false, 0.38f, 0.38f, 0.42f); break;
                    }
                // (the skyline pass) the photograph's: light concrete rim and club
                // fascia, sky-blue seats (sunlit (66,158,187): an albedo of about
                // 0.035, 0.30, 0.50 on the white band's 0.66), a dark field wall
                else if (kind[k] == 1) HeroTintLow(HeroWhiteCol, false, 0.053f, 0.46f, 0.76f);
                else if (k == S.Length - 2) HeroTintLow(HeroMatteCol, false, 0.30f, 0.40f, 0.45f);
                else HeroTintLow(HeroWhiteCol, false, 0.68f, 0.68f, 0.66f);
                var runUV = LookFixOff || k == S.Length - 2 ? HeroMatteUV : HeroWhiteUV;
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
                    HeroQuad(tm, a, b, bq, aq, facing, runUV);
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
            // (the skyline pass: a black housing, and on its field side a
            // blank screen - unlit, no content of any kind)
            if (LookFixOff) HeroTint(HeroMetalCol, true, 0.35f, 0.35f, 0.38f, crown: false);
            else HeroTintLow(HeroMatteCol, false, 0.10f, 0.10f, 0.12f);
            var fs = heroMap.footprints;
            foreach (int bi in heroDropped[hero])
            {
                var b = fs[bi];
                if (b.h < 50f || 2f * b.hv >= 8f) continue;
                HeroBox(tm, b.centre, b.u, b.hu, 0.8f, floor + rim - 3f, floor + b.h, LookFixOff ? HeroMetalUV : HeroMatteUV);
                if (LookFixOff) continue;
                var bv = new Vector2(-b.u.y, b.u.x);
                if (Vector2.Dot(bv, c - b.centre) < 0f) bv = -bv;          // toward the field
                float sy0 = floor + rim - 1.5f, sy1 = floor + b.h - 1.2f, shu = Mathf.Max(1f, b.hu - 1.2f);
                Vector2 s0 = b.centre + bv * 0.95f - b.u * shu, s1 = b.centre + bv * 0.95f + b.u * shu;   // 15 cm proud of the housing
                HeroTintLow(HeroMatteCol, false, 0.12f, 0.15f, 0.22f);
                HeroQuad(tm, W3(s0, sy0), W3(s0, sy1), W3(s1, sy1), W3(s1, sy0), W3(bv, 0f), HeroMatteUV);
                HeroTintLow(HeroMatteCol, false, 0.10f, 0.10f, 0.12f);
            }
            HeroNoCollide(false);
            return true;
        }
    }
}
