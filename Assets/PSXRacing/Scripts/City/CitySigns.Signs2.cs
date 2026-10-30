using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S HIGHWAY SIGNS, ROUND 2 (2026-10-29). Round 1
    /// (CitySigns.Guide.cs) stood the freeway guide signs from the map's own
    /// words; this round makes them stand, read and light the way NCDOT's did
    /// in the 1990s:
    ///
    ///   LEGS      a sign bridge's leg collides where its two round posts and
    ///             their footings are drawn - not a 0.7 x 1.4 m box round both
    ///             (the drive audit read the air between the posts as an
    ///             invisible solid). PSX_CITY_SIGNS2_LEGS.
    ///   GLYPH     (the "6" of I-85 exit 36) - see <see cref="S2Glyph"/>.
    ///   DENSITY   MUTCD 2E.33: no 1 MILE sign over the previous exit's signing
    ///             (interchanges closer than a mile), one advance sign half way
    ///             where they are closer than half a mile; the advance signs on
    ///             the ground past the clear zone unless MUTCD 2A.17's overhead
    ///             factors hold (a left exit, a lane drop, more than three
    ///             lanes); an advance panel at a station within 150 m of another
    ///             exit's sign bridge hangs on that bridge. PSX_CITY_SIGNS2_DENSITY.
    ///   BRIDGE    an exit whose freeway is on structure (no ground for legs)
    ///             hangs its exit direction sign on the FASCIA of the overpass
    ///             that crosses before the exit (MUTCD 2E.25 "overcrossing
    ///             structures can often serve for the support of overhead
    ///             signs"), on brackets a real gap off the parapet, its foot
    ///             never below the bridge's soffit. PSX_CITY_SIGNS2_BRIDGE.
    ///   CURVES    MUTCD W1-1/2/3/4 + W13-1P where <see cref="CityCurves"/>'
    ///             advisory is 10 mph or more under the limit (Table 2C-5) on
    ///             freeways, ramps and arterials, at Table 2C-4's advance
    ///             distance, a supplementary sign on the left of a multi-lane
    ///             one-way carriageway (2A.16); W1-8 chevrons through a tight
    ///             ramp curve at Table 2C-6's spacing. PSX_CITY_SIGNS2_CURVES.
    ///   EXITONLY  a lane drop's black-on-yellow EXIT ONLY panel (MUTCD 2E.24;
    ///             the 1988 manual's E11-1 style): a down arrow over each
    ///             dropped lane on the overhead advance signs, the words on the
    ///             exit direction sign. PSX_CITY_SIGNS2_EXITONLY.
    ///   NIGHT     overhead guide signs LIT as 1990s NCDOT's were (MUTCD 2E.06):
    ///             a catwalk under the panels, a mercury-vapour luminaire on its
    ///             front edge every ~2.8 m of panel aimed up at the face; the face
    ///             brighter toward its lamps (each vertex carries its share in
    ///             UV2, PSX/Lit's _NightVtx path, the mercury white-green of
    ///             <see cref="StreetLights.Mercury"/>); the lamps halo dots only
    ///             (NightGlow, no road light); ground signs retroreflective only.
    ///             PSX_CITY_SIGNS2_NIGHT.
    ///   GORES     the gore sign past the PAINTED gore's nose, where the gore
    ///             visibly opens to grass (round 1 stood signs in the paved
    ///             chevron area: a gap of the legend's width + 1.4 m, inside
    ///             CityMeshes' 4.5 m painted gore), looked for along the whole
    ///             ramp and freeway chains. PSX_CITY_SIGNS2_GORES.
    /// PSX_CITY_SIGNS2=0 is round 1 throughout; each switch =0 restores its part.
    /// </summary>
    public static partial class CitySigns
    {
        static bool S2Env(string k) => System.Environment.GetEnvironmentVariable("PSX_CITY_SIGNS2") != "0" && System.Environment.GetEnvironmentVariable(k) != "0";
        /// <summary>Two post colliders a leg (each post and its footing), not one box round both.</summary>
        public static bool S2Legs = S2Env("PSX_CITY_SIGNS2_LEGS");
        /// <summary>The glyph fix (see the CHARLOTTE.md round 2 section).</summary>
        public static bool S2Glyph = S2Env("PSX_CITY_SIGNS2_GLYPH");
        public static bool S2Density = S2Env("PSX_CITY_SIGNS2_DENSITY");
        public static bool S2Bridge = S2Env("PSX_CITY_SIGNS2_BRIDGE");
        public static bool S2Curves = S2Env("PSX_CITY_SIGNS2_CURVES");
        public static bool S2ExitOnly = S2Env("PSX_CITY_SIGNS2_EXITONLY");
        public static bool S2Night = S2Env("PSX_CITY_SIGNS2_NIGHT");
        public static bool S2Gores = S2Env("PSX_CITY_SIGNS2_GORES");

        /// <summary>A DIAGNOSTIC (never shipped on): PSX_SIGNS_DBG_NOBACKING=1 lays
        /// no green backing behind a face's mosaic (the glyph probe's A/B).</summary>
        public static bool DbgNoBacking = System.Environment.GetEnvironmentVariable("PSX_SIGNS_DBG_NOBACKING") == "1";

        /// <summary>Tools: set every round-2 switch at once and forget what was decided.</summary>
        public static void SetSigns2(bool on)
        {
            S2Legs = S2Glyph = S2Density = S2Bridge = S2Curves = S2ExitOnly = S2Night = S2Gores = on;
            ClearCache(); siteMap = null;
        }

        /// <summary>While a lit panel's face is laid: a point's share of its luminaires' light.</summary>
        static System.Func<Vector3, float> nightLight;
        /// <summary>The census: advance signs MUTCD 2E.33 leaves out (interchanges too close).</summary>
        static int droppedMile, droppedHalf, droppedMinor;

        /// <summary>A minor interchange (MUTCD 2E.33 / 2E.32's classes): its exit
        /// leads to a street - no route designation on its sign, no freeway beyond.</summary>
        static bool MinorInterchange(CityMap map, int exitEdge)
        {
            var ex = ExitOf(map, exitEdge);
            return ex.hasData && string.IsNullOrEmpty(ex.data.route);
        }

        /// <summary>A one-way carriageway's lanes (the line set's, else the way's).</summary>
        static int LanesOn(CityMap.Edge e) => Mathf.Max(1, e.hasLset && e.lsNF > 0 ? e.lsNF : e.lanes);

        // ------------------------------------------------------------------
        //  LEGS
        // ------------------------------------------------------------------

        /// <summary>A sign bridge's leg as drawn (<see cref="EndFrame"/>): its two
        /// 8-sided round posts (circumradius 0.17 m) on concrete footings (0.36 m,
        /// 0.4 m down to 0.55 m up), D apart along the road - a box on each
        /// post's flats and one on its footing's (yaw -22.5: the prism's flats
        /// face 22.5 + k 45 degrees), so every box face lies on a drawn face.</summary>
        static void LegPosts(Pure pu, Vector2 leg, float gy, float top, Vector3 n3, float D, bool footing = true)
        {
            const float postR = 0.17f, footR = 0.36f;
            float flat = Mathf.Cos(Mathf.PI / 8f);
            foreach (float sz in new[] { -1f, 1f })
            {
                var pc = leg + new Vector2(n3.x, n3.z) * (sz * D * 0.5f);
                pu.posts.Add((V3(pc, (gy - 0.3f + top) * 0.5f), new Vector3(2f * postR * flat, top - gy + 0.3f, 2f * postR * flat), -22.5f));
                if (footing) pu.posts.Add((V3(pc, gy + 0.075f), new Vector3(2f * footR * flat, 0.95f, 2f * footR * flat), -22.5f));
            }
        }

        // ------------------------------------------------------------------
        //  DENSITY
        // ------------------------------------------------------------------

        /// <summary>How far back along the carriageway the previous exit
        /// diverges (a one-way link leaving it), from this exit's diverge;
        /// float.MaxValue past two miles.</summary>
        static float PrevExitGap(CityMap map, CityMap.Edge e)
        {
            float dist = e.length;
            var cur = e;
            for (int hop = 0; hop < 80 && dist < 2f * MileM; hop++)
            {
                int n = cur.a;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o != cur && o.a == n && o.link && o.oneway) return dist;
                }
                CityMap.Edge prev = null; float best = 0.5f;
                var t0 = cur.TangentAt(0f);
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o == cur || o.b != n || !IsMainline(o)) continue;
                    float d = Vector2.Dot(o.TangentAt(o.length), t0);
                    if (d > best) { best = d; prev = o; }
                }
                if (prev == null) break;
                cur = prev; dist += cur.length;
            }
            return float.MaxValue;
        }

        // ------------------------------------------------------------------
        //  EXIT ONLY
        // ------------------------------------------------------------------

        /// <summary>The through lanes an exit drops (MUTCD 2E.24): lanes that
        /// run at least a quarter mile before the diverge and do not go on
        /// along the freeway after it (the graph's own lanes; OSM turn:lanes
        /// marking the exit side's lanes for the turn only says the same), no
        /// more than the ramp carries; none where the exit carries the through
        /// route (2E.24: "signs should be used without the EXIT ONLY panel").</summary>
        static int DroppedLanes(CityMap map, CityMap.Edge e, ExitInfo x)
        {
            if (x.link == null || x.main == null) return 0;
            int nIn = LanesOn(e), nMain = LanesOn(x.main), nRamp = LanesOn(x.link);
            // a THROUGH lane: there all the way back a mile (or to the freeway's
            // start) - not a deceleration lane begun at the exit, nor an auxiliary
            // lane from the previous interchange's entrance
            int nUp = MinLanesBack(map, e, DropRunM);
            int dropped = Mathf.Min(nIn, nUp) - nMain;
            if (e.hasLset && e.lsTurnOnly != 0)
            {
                // turn:lanes: the exit side's run of turn-only lanes (bit 0 the leftmost)
                int bits = e.lsTurnOnly & 0xFF, run = 0;
                for (int k = 0; k < nIn; k++)
                {
                    int lane = x.left ? k : nIn - 1 - k;
                    if ((bits & (1 << lane)) == 0) break;
                    run++;
                }
                if (run > 0 && run < nIn && Mathf.Min(nIn, nUp) - run >= nMain) dropped = Mathf.Max(dropped, run);
            }
            dropped = Mathf.Clamp(dropped, 0, Mathf.Min(nRamp, nIn - 1));
            if (dropped > 0 && x.hasData && SameRoute(x.data.route, x.data.thru)) dropped = 0;
            return dropped;
        }

        /// <summary>A lane that drops at an exit runs at least this far before it
        /// (a parallel deceleration lane - 300-450 m with its taper on an urban
        /// freeway - is no lane drop).</summary>
        const float DropRunM = 1609f;

        /// <summary>The fewest lanes the carriageway carries from the end of e
        /// back along it as far as dist (its straightest mainline).</summary>
        static int MinLanesBack(CityMap map, CityMap.Edge e, float dist)
        {
            int min = LanesOn(e);
            float walked = e.length;
            var cur = e;
            for (int hop = 0; hop < 60 && walked < dist; hop++)
            {
                CityMap.Edge prev = null; float best = 0.5f;
                var t0 = cur.TangentAt(0f);
                foreach (int oi in map.nodeEdges[cur.a])
                {
                    var o = map.edges[oi];
                    if (o == cur || o.b != cur.a || !IsMainline(o)) continue;
                    float d = Vector2.Dot(o.TangentAt(o.length), t0);
                    if (d > best) { best = d; prev = o; }
                }
                if (prev == null) break;
                cur = prev; walked += cur.length;
                min = Mathf.Min(min, LanesOn(cur));
            }
            return min;
        }

        /// <summary>Two route legends name the same route ("I-77 NORTH", "I-77").</summary>
        static bool SameRoute(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string Des(string r)
            {
                var w = r.Split(' ');
                return w.Length >= 2 && (w[0] == "US" || w[0] == "NC" || w[0] == "SC") ? w[0] + " " + w[1] : w[0];
            }
            return Des(a) == Des(b);
        }

        /// <summary>
        /// The EXIT ONLY panel (MUTCD E11-1; 2E.24): black legend and border on
        /// yellow across the foot of a guide sign, "EXIT" and "ONLY" either side
        /// of a black down arrow over each dropped lane (an advance sign), or the
        /// two words alone (the exit direction sign, whose up-diagonal arrow
        /// says where). Laid as one mosaic row the whole width of its panel; the
        /// arrows follow the panel's place over the lanes (<see cref="shift"/>),
        /// kept between the words, so nothing overlaps or clips.
        /// </summary>
        internal sealed class GExitOnly : GBlock
        {
            const float Cap = 0.33f, Ring = 0.10f, PadX = 0.16f, PadY = 0.07f, Gap = 0.18f;
            const float AW = 0.27f, AH = 0.47f;
            readonly List<float> xs;
            readonly GText wE, wO;
            public float shift;
            public GExitOnly(List<float> laneXs, float minW)
            {
                xs = laneXs != null && laneXs.Count > 0 ? laneXs : null;
                yellow = true;
                wE = new GText("EXIT", Cap, true); wO = new GText("ONLY", Cap, true);
                float span = 0f;
                if (xs != null)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var x in xs) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                    span = hi - lo + AW;
                }
                float inner = wE.w + wO.w + (xs != null ? 2f * Gap + span : Gap);
                w = Mathf.Max(minW, inner + 2f * (Ring + PadX));
                h = Mathf.Max(wE.h, xs != null ? AH : 0f) + 2f * (Ring + PadY);
            }
            protected override void Content(GFace f, float x0, float y0, float x1, float y1) { }
            /// <summary>Across the whole width given (the panel's), its own height.</summary>
            public override void Draw(GFace f, float x, float y, float W, float H)
            {
                float X = x + W, Y = y + H;
                var corner = CityGuideFont.RingYCorner; var edge = CityGuideFont.RingYEdge;
                float ia = x + Ring, ib = X - Ring, ja = y + Ring, jb = Y - Ring;
                f.Cell(x, y, ia, ja, corner, false, false);
                f.Cell(ib, y, X, ja, corner, true, false);
                f.Cell(x, jb, ia, Y, corner, false, true);
                f.Cell(ib, jb, X, Y, corner, true, true);
                UvRect(edge, out float uo, out float vt, out float ui, out float vb);
                float vm = (vt + vb) * 0.5f;
                Vector2 O = new Vector2(uo, vm), I = new Vector2(ui, vm);
                f.Quad(x, ja, ia, jb, O, I, I, O);
                f.Quad(ib, ja, X, jb, I, O, O, I);
                f.Quad(ia, y, ib, ja, O, O, I, I);
                f.Quad(ia, jb, ib, Y, I, I, O, O);
                // the row inside the border
                float mid = x + W * 0.5f;
                var cs = new List<float>();
                if (xs != null)
                {
                    foreach (var lx in xs) cs.Add(mid + lx - shift);
                    cs.Sort();
                    // kept between the words, as one group
                    float lo = ia + PadX + wE.w + Gap + AW * 0.5f, hi = ib - PadX - wO.w - Gap - AW * 0.5f;
                    float d = 0f;
                    if (cs[0] < lo) d = lo - cs[0];
                    if (cs[cs.Count - 1] + d > hi) d = hi - cs[cs.Count - 1];
                    for (int i = 0; i < cs.Count; i++) cs[i] = Mathf.Clamp(cs[i] + d, lo, Mathf.Max(lo, hi));
                    // never two arrows overlapping
                    for (int i = 1; i < cs.Count; i++) cs[i] = Mathf.Max(cs[i], cs[i - 1] + AW + 0.05f);
                }
                float eL, eR, oL, oR;
                if (cs.Count > 0)
                {
                    eR = cs[0] - AW * 0.5f - Gap; eL = eR - wE.w;
                    oL = cs[cs.Count - 1] + AW * 0.5f + Gap; oR = oL + wO.w;
                }
                else
                {
                    float tot = wE.w + Gap + wO.w;
                    eL = mid - tot * 0.5f; eR = eL + wE.w; oL = eR + Gap; oR = oL + wO.w;
                }
                float cur = ia;
                f.Fill(cur, ja, eL, jb, true); cur = eL;
                wE.Draw(f, eL, ja, eR - eL, jb - ja); cur = eR;
                float aTop = ja + (jb - ja - AH) * 0.5f, aBot = aTop + AH;
                foreach (var c in cs)
                {
                    float a0 = c - AW * 0.5f, a1 = c + AW * 0.5f;
                    f.Fill(cur, ja, a0, jb, true);
                    f.Fill(a0, ja, a1, aTop, true);
                    f.Cell(a0, aTop, a1, aBot, CityGuideFont.ArrowDownBlack, false, false);
                    f.Fill(a0, aBot, a1, jb, true);
                    cur = a1;
                }
                f.Fill(cur, ja, oL, jb, true);
                wO.Draw(f, oL, ja, oR - oL, jb - ja);
                f.Fill(oR, ja, ib, jb, true);
            }
        }

        // ------------------------------------------------------------------
        //  NIGHT: the 1990s sign lighting
        // ------------------------------------------------------------------

        /// <summary>The luminaires under a panel: one every ~2.8 m of its width,
        /// metres from its left edge.</summary>
        static float[] LampSpots(float w)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(w / 2.8f));
            var xs = new float[n];
            for (int k = 0; k < n; k++) xs[k] = (k + 0.5f) * w / n;
            return xs;
        }

        /// <summary>A lit panel's light at a point of its face: brightest at the
        /// edge its lamps stand at (the foot; the top for a bridge-mounted sign
        /// lit from above), falling to under half at the far edge - a 1990s
        /// sign-lighting design's 3:1 - and brighter still over each lamp.</summary>
        static System.Func<Vector3, float> PanelLight(Vector3 bottomLeft, Vector3 r3, float w, float h, float[] lampXs, bool fromTop)
        {
            return p =>
            {
                float x = Vector3.Dot(p - bottomLeft, r3);
                float hn = Mathf.Clamp01((p.y - bottomLeft.y) / Mathf.Max(0.1f, h));
                if (fromTop) hn = 1f - hn;
                float vert = Mathf.Lerp(1f, 0.38f, Mathf.Pow(hn, 0.85f));
                float best = 0f;
                foreach (float lx in lampXs) { float dx = (x - lx) / 1.3f; best = Mathf.Max(best, Mathf.Exp(-dx * dx)); }
                return vert * (0.72f + 0.28f * best);
            };
        }

        /// <summary>A mercury-vapour sign luminaire (the 1990s fixture: a box
        /// housing on an arm off the catwalk's front edge, its lens turned up
        /// at the face). <paramref name="at"/>: on the face's plane at the
        /// catwalk's level (or the panel's top, from above), under its spot.</summary>
        static void Luminaire(Pure pu, Vector3 at, Vector3 r3, Vector3 n3, bool fromTop)
        {
            var up = Vector3.up;
            var head = fromTop ? at + n3 * 0.85f + up * 0.30f : at + n3 * 0.95f - up * 0.10f;
            GBox(head, r3, up, n3, 0.24f, 0.09f, 0.16f, CityGuideFont.Alum, true);
            if (fromTop)
            {
                // up from the panel's back, then out over its face
                Bar(at - n3 * 0.14f, at - n3 * 0.14f + up * 0.30f, 0.025f, CityGuideFont.Galv);
                Bar(at - n3 * 0.14f + up * 0.30f, head - n3 * 0.14f, 0.025f, CityGuideFont.Galv);
            }
            else Bar(at + n3 * 0.78f, head - n3 * 0.14f, 0.025f, CityGuideFont.Galv);
            // the halo under the housing, where a driver below sees the lamp's glare
            pu.guideLamps.Add(head - up * 0.14f);
        }

        /// <summary>The maintenance catwalk under a structure's panels: a
        /// galvanised grating from the panels' back plane to 0.85 m in front of
        /// their faces, and its kick plate along the front edge. a, b: the run's
        /// ends on the faces' plane at the walkway's level.</summary>
        static void Catwalk(Vector3 a, Vector3 b, Vector3 r3, Vector3 n3)
        {
            var mid = (a + b) * 0.5f; float L = Vector3.Distance(a, b);
            if (L < 0.5f) return;
            GBox(mid + n3 * 0.365f - Vector3.up * 0.025f, r3, Vector3.up, n3, L * 0.5f, 0.025f, 0.485f, CityGuideFont.Galv, true);
            GBox(mid + n3 * 0.84f + Vector3.up * 0.062f, r3, Vector3.up, n3, L * 0.5f, 0.06f, 0.01f, CityGuideFont.Galv, true);
        }

        // ------------------------------------------------------------------
        //  BRIDGE-MOUNTED SIGNS
        // ------------------------------------------------------------------

        static readonly HashSet<int> bridgeScratch = new HashSet<int>();
        /// <summary>The last crossing a bridge-mount search turned down, and why (the census).</summary>
        static string overReject = "";
        /// <summary>A bridge-mounted panel's back stands this far off the fascia (its brackets).</summary>
        const float BracketM = 0.35f;
        /// <summary>The most a bridge-mounted panel turns off its fascia toward its traffic.</summary>
        const float MaxSkewDeg = 35f;

        /// <summary>
        /// An exit's direction sign (or an overhead advance sign) on the fascia
        /// of the first overpass crossing its carriageway before the station:
        /// probes every 4 m from <paramref name="offFrom"/> down to
        /// <paramref name="offTo"/> metres along the carriageway (+ with the
        /// traffic) from arc s of e.
        /// </summary>
        static bool BridgeMount(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, float s, ExitInfo ex, Site site, float offFrom, float offTo)
        {
            if (!ex.hasData) { why = "no legend"; return false; }
            why = "no overpass crossing before it";
            overReject = "";
            var seen = new HashSet<int>();
            for (float off = offFrom; off >= offTo; off -= 4f)
            {
                var ce = e; float cs = s;
                if (!Slide(map, ref ce, ref cs, off)) continue;
                var p = ce.PointAt(cs); var t = ce.TangentAt(cs); float y = ce.YAt(cs);
                if (!Overcrossing(map, ce, p, t, y, out var o, out float oS, out var X)) continue;
                if (!seen.Add(o.index)) continue;
                pu.Reset(); pu.kind = Kind.Bridge; pu.breakaway = false;
                if (OnFascia(map, trims, pu, ce, cs, X, ex, site, o, oS)) return true;
            }
            pu.Reset();
            if (why == "no overpass crossing before it" && overReject.Length > 0) why += " (nearest: " + overReject + ")";
            return false;
        }

        /// <summary>A road on structure crossing over the carriageway at p
        /// (within 2.5 m along it), at 45 degrees or more, its deck at least
        /// the solve's clearance over the carriageway.</summary>
        static bool Overcrossing(CityMap map, CityMap.Edge ce, Vector2 p, Vector2 t, float y, out CityMap.Edge over, out float overS, out Vector2 X)
        {
            over = null; overS = 0f; X = p;
            bridgeScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 30f, p + Vector2.one * 30f, bridgeScratch);
            var a0 = p - t * 2.5f; var a1 = p + t * 2.5f;
            float bestY = float.MaxValue;
            foreach (int packed in bridgeScratch)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                var o = map.edges[oi];
                if (o == ce || o.tunnel || si + 1 >= o.pts.Length) continue;
                Vector2 b0 = o.pts[si], b1 = o.pts[si + 1];
                if (!SegCross(a0, a1, b0, b1, out float ua, out float ub)) continue;
                var d = (b1 - b0).normalized;
                float at = o.s[si] + Vector2.Distance(b0, b1) * ub;
                float oy = o.YAt(at);
                if (Mathf.Abs(Vector2.Dot(d, t)) > 0.71f) { overReject = $"e{o.index} '{o.name}' crosses at {Mathf.Acos(Mathf.Abs(Vector2.Dot(d, t))) * Mathf.Rad2Deg:0} deg, {oy - y:+0.0;-0.0} m"; continue; }
                if (!(o.bridge || o.ElevatedAt(at))) { overReject = $"e{o.index} '{o.name}' is not on structure there, {oy - y:+0.0;-0.0} m"; continue; }
                if (oy - y < CityElevation.ClearanceM + CityElevation.DeckThick - 0.3f) { overReject = $"e{o.index} '{o.name}' only {oy - y:+0.0;-0.0} m over it"; continue; }
                if (oy < bestY) { bestY = oy; over = o; overS = at; X = Vector2.Lerp(a0, a1, ua); }
            }
            return over != null;
        }

        static bool SegCross(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float ua, out float ub)
        {
            ua = ub = 0f;
            var r = a1 - a0; var q = b1 - b0;
            float den = r.x * q.y - r.y * q.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            var w = b0 - a0;
            ua = (w.x * q.y - w.y * q.x) / den;
            ub = (w.x * r.y - w.y * r.x) / den;
            return ua >= 0f && ua <= 1f && ub >= 0f && ub <= 1f;
        }

        /// <summary>The panels on the overpass's fascia facing the carriageway's
        /// traffic: over the exit's lanes (the pull-through over the lanes going
        /// on, where the fascia has room), the panel's back <see cref="BracketM"/>
        /// off the parapet's outer face, its foot at the bridge's soffit (the
        /// girders' underside) and never lower than 16 ft over the carriageway,
        /// lit from luminaires on arms over its top.</summary>
        static bool OnFascia(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge ce, float cs, Vector2 X, ExitInfo ex, Site site, CityMap.Edge o, float oS)
        {
            var t = ce.TangentAt(cs);
            var right = RightOf(t);
            var to = o.TangentAt(oS);
            var no = RightOf(to);
            float side = Vector2.Dot(no, -t) >= 0f ? 1f : -1f;
            var nF = no * side;                               // the fascia's outward normal, toward the traffic
            LineModel.Extents(o, oS, out float oR, out float oL);
            float edge = side > 0f ? oR : oL;
            var oc = o.PointAt(oS);
            var F0 = oc + nF * (edge + CityMeshes.RailOverhangM);   // the parapet's outer face
            // the carriageway's lanes at the crossing, as Overhead lays them
            LineModel.ExtentsNoAux(ce, cs, out float eM, out float eP);
            int total = Mathf.Max(2, ex.lanes);
            int exitN = Mathf.Clamp(ex.exitLanes, 1, total - 1);
            float laneW = Mathf.Clamp((eM + eP - ce.shl - ce.shr) / total, 2.8f, 4.2f);
            var pX = ce.PointAt(cs);
            var leftEdge = pX - right * eP;
            float LaneX(int i) => ce.shl + (i + 0.5f) * laneW;
            float exitMid = 0f;
            for (int i = 0; i < exitN; i++) exitMid += LaneX(ex.left ? i : total - 1 - i);
            exitMid /= exitN;
            // the face turned to its drivers, at most MaxSkewDeg off the fascia
            var viewer0 = Driver(map, ce, cs, -ViewAheadM, exitMid - eP);
            var toV = new Vector2(viewer0.x - X.x, viewer0.z - X.y).normalized;
            if (Vector2.Dot(toV, nF) < Mathf.Cos(MaxSkewDeg * Mathf.Deg2Rad)) { why = "the overpass crosses too skewed"; return false; }
            var n2 = Vector2.Dot(nF, toV) >= 0.92f ? nF : toV;  // parallel to the fascia where it looks at its drivers (the audit's 0.9), else turned to them
            var n3 = new Vector3(n2.x, 0f, n2.y);
            var r3 = Vector3.Cross(n3, Vector3.up).normalized;
            var r2 = new Vector2(r3.x, r3.z);
            // the legend (the exit direction sign; an advance sign's distance)
            bool advance = site.kind == GuideKind.Advance;
            int dropHere = S2ExitOnly ? ex.dropped : 0;
            var panel = ExitLegend(ex, advance ? site.miles : 0f, !advance, dropHere, null);
            var tab = string.IsNullOrEmpty(ex.data.number) && !ex.left ? null : TabLegend(ex);
            // where the exit lanes' middle meets the fascia (along the traffic)
            var Pm = leftEdge + right * exitMid;
            float den = Vector2.Dot(t, nF);
            if (Mathf.Abs(den) < 0.3f) { why = "the overpass crosses too skewed"; return false; }
            var Xm = Pm + t * (Vector2.Dot(F0 - Pm, nF) / den);
            // its centre clear of the fascia by the brackets, both ends
            float sinA = Mathf.Abs(Vector2.Dot(r2, nF));
            float cosA = Mathf.Abs(Vector2.Dot(n2, nF));
            var C = Xm + nF * (BracketM + 0.10f * cosA + panel.w * 0.5f * sinA + 0.10f);
            // the panel must stay over the deck's length (the fascia under it)
            float along = Vector2.Dot(C - oc, to);
            if (Mathf.Abs(along) > 60f) { why = "off the overpass"; return false; }
            // heights: the deck, its slab, its girders; the highest road under the panel
            float deckY = o.YAt(oS);
            float soffit = deckY - CityElevation.DeckThick - CityMeshes.GirderDepthOf(map, o);
            float roadTop = ce.YAt(cs);
            for (int k = 0; k <= 8; k++)
            {
                var q = C + r2 * ((k / 8f - 0.5f) * panel.w);
                float rt = RoadTopAt(map, trims, q, 0.5f);
                if (rt < deckY - 1.0f) roadTop = Mathf.Max(roadTop, rt);
            }
            float bottom = Mathf.Max(soffit, roadTop + 4.88f);   // 16 ft, never below the soffit
            float top = bottom + panel.h;
            float tabTop = top + (tab != null ? tab.h : 0f);
            var centre = V3(C, bottom + panel.h * 0.5f);
            var up = Vector3.up;
            System.Func<Vector3, float> lit = null;
            if (S2Night)
            {
                var lampXs = LampSpots(panel.w);
                lit = PanelLight(centre - r3 * (panel.w * 0.5f) - up * (panel.h * 0.5f), r3, panel.w, panel.h, lampXs, true);
                // over the panel's top - over the tab's where the tab stands, never in front of it
                float tabX0 = tab == null ? 1e9f : ex.left ? 0f : panel.w - tab.w, tabX1 = tab == null ? -1e9f : ex.left ? tab.w : panel.w;
                foreach (float lx in lampXs)
                {
                    float lampY = lx > tabX0 - 0.4f && lx < tabX1 + 0.4f ? tabTop : top;
                    Luminaire(pu, V3(C, lampY) + r3 * (lx - panel.w * 0.5f), r3, n3, true);
                }
            }
            HangPanel(panel, centre, r3, n3, lit);
            var viewer = Driver(map, ce, cs, -ViewAheadM, exitMid - eP);
            pu.faces.Add(new Face { centre = centre, normal = n3, w = panel.w, h = panel.h, kind = Kind.Bridge, viewer = viewer });
            if (tab != null)
            {
                float tx = ex.left ? -panel.w * 0.5f + tab.w * 0.5f : panel.w * 0.5f - tab.w * 0.5f;
                var tc = centre + r3 * tx + up * ((panel.h + tab.h) * 0.5f);
                HangPanel(tab, tc, r3, n3, lit);
                pu.faces.Add(new Face { centre = tc, normal = n3, w = tab.w, h = tab.h, kind = Kind.Bridge, viewer = viewer });
            }
            // the brackets: a galvanised upright behind the panel every ~2.4 m,
            // struts back to the parapet's face at the slab and at the rail
            int nb = Mathf.Max(2, Mathf.CeilToInt(panel.w / 2.4f));
            for (int i = 0; i < nb; i++)
            {
                float x = -panel.w * 0.5f + (i + 0.5f) * panel.w / nb;
                var back = centre + r3 * x - n3 * 0.14f;
                var b0 = new Vector3(back.x, bottom + 0.05f, back.z);
                var b1 = new Vector3(back.x, Mathf.Max(tabTop, deckY + CityMeshes.RailH) - 0.05f, back.z);
                Bar(b0, b1, 0.04f, CityGuideFont.Galv);
                // the fascia's face behind it
                var bp = new Vector2(back.x, back.z);
                var fp = bp - nF * Vector2.Dot(bp - F0, nF);
                foreach (float hy in new[] { deckY - CityElevation.DeckThick * 0.5f, deckY + CityMeshes.RailH * 0.6f })
                {
                    if (hy < bottom || hy > b1.y) continue;
                    Bar(new Vector3(back.x, hy, back.z), new Vector3(fp.x, hy, fp.y) + new Vector3(nF.x, 0f, nF.y) * 0.02f, 0.035f, CityGuideFont.Galv);
                }
            }
            pu.sign = new Sign
            {
                kind = Kind.Bridge, pos = C, ground = roadTop, bottom = bottom, top = tabTop, route = -1, edge = ce.index,
                legA = C, legB = C, clearance = bottom - roadTop, firstFace = 0, faces = pu.faces.Count, owner = pu.sign.owner,
            };
            return true;
        }

        // ------------------------------------------------------------------
        //  CURVE WARNINGS (MUTCD W1 series + W13-1P; W1-8)
        // ------------------------------------------------------------------

        public const byte SymTurn = 0, SymCurve = 1, SymRevTurn = 2, SymRevCurve = 3;
        /// <summary>The census: curve warnings by road kind, chevrons, the spans looked at and left.</summary>
        static int curveSpansSeen, curveSpansSigned, curveSpansCovered, curveSpansKink, curveSpansSmall, curveSpansCarried, curveSpansTwin;

        /// <summary>The posted limit, mph (5 mph steps), or the class's when the map has none.</summary>
        static int PostedMph(CityMap.Edge e)
        {
            if (e.speedKmh > 0) return Mathf.RoundToInt(e.speedKmh / 1.609344f / 5f) * 5;
            if (e.link) return 45;
            return e.cls >= 5 ? 65 : e.cls == 4 ? 55 : 45;
        }

        /// <summary>MUTCD Table 2C-4, Condition B (deceleration to the listed
        /// advisory speed), feet; never under 100 ft (footnote 6).</summary>
        static float AdvanceFt(int posted, int advisory)
        {
            // rows: posted 20..75 by 5; columns: advisory 0, 10, ..., 70 (0 = N/A)
            int[,] B =
            {
                { 100, 0, 0, 0, 0, 0, 0, 0 },          // 20
                { 100, 0, 0, 0, 0, 0, 0, 0 },          // 25
                { 100, 0, 0, 0, 0, 0, 0, 0 },          // 30
                { 100, 0, 0, 0, 0, 0, 0, 0 },          // 35
                { 125, 100, 100, 0, 0, 0, 0, 0 },      // 40
                { 175, 125, 100, 100, 0, 0, 0, 0 },    // 45
                { 250, 200, 175, 125, 100, 0, 0, 0 },  // 50
                { 325, 275, 225, 200, 125, 0, 0, 0 },  // 55
                { 400, 350, 325, 275, 200, 100, 0, 0 },  // 60
                { 475, 450, 400, 350, 275, 200, 100, 0 },  // 65
                { 550, 525, 500, 450, 375, 275, 150, 0 },  // 70
                { 650, 625, 600, 550, 475, 375, 250, 100 },  // 75
            };
            int row = Mathf.Clamp(Mathf.RoundToInt((posted - 20) / 5f), 0, 11);
            float col = Mathf.Clamp(advisory / 10f, 0f, 7f);
            int c0 = Mathf.FloorToInt(col), c1 = Mathf.Min(7, c0 + 1);
            float v0 = B[row, c0], v1 = B[row, c1];
            if (v1 <= 0f) v1 = 100f;
            if (v0 <= 0f) v0 = 100f;
            return Mathf.Max(100f, Mathf.Lerp(v0, v1, col - c0));
        }

        /// <summary>MUTCD Table 2C-6: chevron spacing on a curve by its advisory, feet.</summary>
        static float ChevronFt(int mph) => mph <= 15 ? 40f : mph <= 30 ? 80f : mph <= 45 ? 120f : mph <= 60 ? 160f : 200f;

        /// <summary>The sign's sizes (MUTCD Table 2C-2, inches to metres): a
        /// freeway's 48 in diamond and 30 in plaque, a ramp's or multi-lane
        /// road's 36 / 24, a single lane's 30 / 18.</summary>
        static void WarnSizes(CityMap.Edge e, out float diamond, out float plaque)
        {
            if (!e.link && e.cls >= 5) { diamond = 1.219f; plaque = 0.762f; }
            else if (e.link || LanesOn(e) >= 2 || !e.oneway && e.lanes >= 4) { diamond = 0.914f; plaque = 0.610f; }
            else { diamond = 0.762f; plaque = 0.457f; }
        }

        /// <summary>
        /// Every curve warning and chevron, once per graph (EnsureSites): each
        /// CityCurves span on a freeway, ramp or arterial whose advisory is 10
        /// mph or more under the limit (Table 2C-5: the sign and plaque
        /// "Required"), a real curve (its heading turns 15 degrees or more -
        /// 30 on an arterial - over a run of its own, not a polyline kink, and
        /// an arterial's not a junction's turn), and not the exit-speed
        /// advisory's curve (the W13-2 at the gore says it). Two opposite curves
        /// under 600 ft apart: one Reverse sign (2C.07). Turn at 30 mph or
        /// less, Curve over. On a ramp, Table 2C-5's 15 mph or more: chevrons
        /// through the curve on its outside.
        /// </summary>
        static void CurveSites(CityMap map)
        {
            curveSpansSeen = curveSpansSigned = curveSpansCovered = curveSpansKink = curveSpansSmall = curveSpansCarried = curveSpansTwin = 0;
            var order = new List<CityCurves.Span>();
            var warnAt = new List<(Vector2 p, Vector2 t)>();
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                if (e.tunnel || e.a == e.b || e.length < 25f) continue;
                bool ramp = e.link && e.cls >= 4, fwy = !e.link && e.cls >= 5, art = !e.link && (e.cls == 3 || e.cls == 4);
                if (!ramp && !fwy && !art) continue;
                var spans = CityCurves.Of(map, ei);
                if (spans.Length == 0) continue;
                int posted = PostedMph(e);
                foreach (int dir in e.oneway ? new[] { 1 } : new[] { 1, -1 })
                {
                    order.Clear();
                    order.AddRange(spans);
                    if (dir < 0) order.Reverse();
                    for (int i = 0; i < order.Count; i++)
                    {
                        var sp = order[i];
                        curveSpansSeen++;
                        if (sp.mph > posted - 10) { curveSpansSmall++; continue; }
                        if (!RealCurve(map, e, sp, dir, art, out bool leftHand, out float turnDeg)) { curveSpansKink++; continue; }
                        if (ramp && CoveredByExitSpeed(map, e, sp)) { curveSpansCovered++; continue; }
                        // a curve carried on from the edge before (a loop split at a node): signed there
                        bool carried = CurveCarriedIn(map, e, sp, dir);
                        int mph = sp.mph;
                        bool reverse = false;
                        if (i + 1 < order.Count)
                        {
                            var nx = order[i + 1];
                            float tangent = dir > 0 ? (nx.s0 + CityCurves.PadM) - (sp.s1 - CityCurves.PadM) : (sp.s0 + CityCurves.PadM) - (nx.s1 - CityCurves.PadM);
                            if (tangent < 182.9f && nx.mph <= posted - 10 && RealCurve(map, e, nx, dir, art, out bool nl, out _) && nl != leftHand)
                            { reverse = true; mph = Mathf.Min(mph, nx.mph); i++; }
                        }
                        if (carried) { curveSpansCarried++; goto Chevrons; }
                        byte sym = reverse ? (mph <= 30 ? SymRevTurn : SymRevCurve) : (mph <= 30 ? SymTurn : SymCurve);
                        WarnSizes(e, out float dia, out float plq);
                        float pc = dir > 0 ? sp.s0 + CityCurves.PadM : sp.s1 - CityCurves.PadM;
                        float adv = AdvanceFt(posted, mph) * 0.3048f;
                        // Table 2C-4's distance back from the curve, along the same road
                        // across its nodes (the road leading into the curve)
                        WalkBackSame(map, e, Mathf.Clamp(pc, 0f, e.length), dir, adv, out var we, out float ws, out int wdir);
                        var at = we.PointAt(ws);
                        var tw = we.TangentAt(ws) * wdir;
                        bool twin = false;
                        foreach (var (q, tq) in warnAt)
                            if ((q - at).sqrMagnitude < 40f * 40f && Vector2.Dot(tq, tw) > 0.8f) { twin = true; break; }
                        if (twin) { curveSpansTwin++; goto Chevrons; }
                        warnAt.Add((at, tw));
                        curveSpansSigned++;
                        sites.Add(new Site { kind = GuideKind.Curve, exitEdge = -1, edge = we.index, s = ws, nominal = at, dir = (sbyte)wdir, side = 1,
                                             sym = sym, leftHand = leftHand, mph = mph, size = dia, plaque = plq });
                        // a supplementary sign on the left of a multi-lane one-way carriageway (MUTCD 2A.16)
                        if (we.oneway && LanesOn(we) >= 2 && !we.link)
                            sites.Add(new Site { kind = GuideKind.Curve, exitEdge = -1, edge = we.index, s = ws, nominal = at, dir = (sbyte)wdir, side = -1,
                                                 sym = sym, leftHand = leftHand, mph = mph, size = dia, plaque = plq });
                        // chevrons through a tight ramp curve (Table 2C-5: required at 15 mph under)
                        Chevrons:
                        // (the 1990s: chevrons where the turn is sharp - a loop or a hook at 25 mph
                        // or less turning 75 degrees or more - not through every ramp curve)
                        if (ramp && posted - sp.mph >= 15 && sp.mph <= 25 && turnDeg >= 75f)
                        {
                            float gap = ChevronFt(sp.mph) * 0.3048f;
                            float c0 = sp.s0 + CityCurves.PadM, c1 = sp.s1 - CityCurves.PadM;
                            if (c1 - c0 < gap) { c0 = (sp.s0 + sp.s1) * 0.5f - gap * 0.5f; c1 = c0 + gap; }
                            for (float s = c0; s <= c1 + 0.1f; s += gap)
                            {
                                float sc = dir > 0 ? s : c1 - (s - c0);
                                sc = Mathf.Clamp(sc, 2f, e.length - 2f);
                                sites.Add(new Site { kind = GuideKind.Chevron, exitEdge = -1, edge = ei, s = sc, nominal = e.PointAt(sc), dir = (sbyte)dir,
                                                     side = (sbyte)(leftHand ? 1 : -1), sym = 0, leftHand = leftHand, mph = sp.mph, size = 0.61f, plaque = 0.762f });
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Back along the road the traffic drives (dir on e: +1 a to
        /// b) by dist from arc s: across a node onto the edge that is the same
        /// road (a ramp's link, a freeway's mainline, a street's straightest
        /// arm, under 45 degrees off), stopping at the last road that leads in.</summary>
        static void WalkBackSame(CityMap map, CityMap.Edge e, float s, int dir, float dist, out CityMap.Edge at, out float sAt, out int dirAt)
        {
            for (int hop = 0; hop < 12; hop++)
            {
                float room = dir > 0 ? s : e.length - s;
                if (room >= dist) { at = e; sAt = s - dir * dist; dirAt = dir; return; }
                dist -= room;
                int node = dir > 0 ? e.a : e.b;
                var tIn = dir > 0 ? e.TangentAt(0f) : -e.TangentAt(e.length);      // the traffic's way leaving the node
                CityMap.Edge best = null; int bestDir = 0; float bestDot = 0.7f;
                foreach (int oi in map.nodeEdges[node])
                {
                    var o = map.edges[oi];
                    if (o == e || o.tunnel || o.a == o.b || o.link != e.link) continue;
                    // the traffic arriving at the node along o
                    if (o.b == node) { float d = Vector2.Dot(o.TangentAt(o.length), tIn); if (d > bestDot) { bestDot = d; best = o; bestDir = 1; } }
                    if (o.a == node && !o.oneway) { float d = Vector2.Dot(-o.TangentAt(0f), tIn); if (d > bestDot) { bestDot = d; best = o; bestDir = -1; } }
                }
                if (best == null) { at = e; sAt = dir > 0 ? Mathf.Min(3f, e.length * 0.5f) : Mathf.Max(e.length - 3f, e.length * 0.5f); dirAt = dir; return; }
                e = best; dir = bestDir; s = dir > 0 ? e.length : 0f;
            }
            at = e; sAt = Mathf.Clamp(s, 0f, e.length); dirAt = dir;
        }

        /// <summary>A span that starts where its edge starts (in the traffic's
        /// way) while the edge before - the same road into that node - ends in
        /// a span: one curve split by a node, signed where it begins.</summary>
        static bool CurveCarriedIn(CityMap map, CityMap.Edge e, CityCurves.Span sp, int dir)
        {
            if (dir > 0 ? sp.s0 > 1f : sp.s1 < e.length - 1f) return false;
            int node = dir > 0 ? e.a : e.b;
            var tIn = dir > 0 ? e.TangentAt(0f) : -e.TangentAt(e.length);
            foreach (int oi in map.nodeEdges[node])
            {
                var o = map.edges[oi];
                if (o == e || o.tunnel || o.link != e.link) continue;
                int od = o.b == node ? 1 : (o.a == node && !o.oneway ? -1 : 0);
                if (od == 0) continue;
                var tArr = od > 0 ? o.TangentAt(o.length) : -o.TangentAt(0f);
                if (Vector2.Dot(tArr, tIn) < 0.3f) continue;
                foreach (var q in CityCurves.Of(map, o.index))
                    if (od > 0 ? q.s1 >= o.length - 1f : q.s0 <= 1f) return true;
            }
            return false;
        }

        /// <summary>A span that is a curve: its heading turns 15 degrees or more
        /// (30 on an arterial) between the ends of its own run, the run 6 m or
        /// longer; an arterial's also 30 m or more from a junction of three
        /// arms or more. Its hand as the traffic drives it.</summary>
        static bool RealCurve(CityMap map, CityMap.Edge e, CityCurves.Span sp, int dir, bool arterial, out bool leftHand, out float turnDeg)
        {
            float a = sp.s0 + CityCurves.PadM, b = sp.s1 - CityCurves.PadM;
            if (b - a < 6f) { float m = (sp.s0 + sp.s1) * 0.5f; a = m - 3f; b = m + 3f; }
            a = Mathf.Clamp(a, 0.05f, e.length - 0.05f); b = Mathf.Clamp(b, 0.05f, e.length - 0.05f);
            var t0 = e.TangentAt(a); var t1 = e.TangentAt(b);
            float cross = t0.x * t1.y - t0.y * t1.x;
            float turn = Vector2.Angle(t0, t1);
            turnDeg = turn;
            leftHand = dir > 0 ? cross > 0f : cross < 0f;
            if (turn < (arterial ? 30f : 15f) || b - a < 6f) return false;
            if (arterial)
            {
                if (a < 30f && map.nodeEdges[e.a].Count >= 3) return false;
                if (e.length - b < 30f && map.nodeEdges[e.b].Count >= 3) return false;
            }
            else if (e.link)
            {
                // a ramp's turn INTO the street junction at its end is the junction's, not a curve
                int endNode = dir > 0 ? e.b : e.a;
                float toEnd = dir > 0 ? e.length - b : a;
                if (toEnd < 30f && StreetJunction(map, endNode)) return false;
            }
            return true;
        }

        /// <summary>A node where a street (not a ramp, not a freeway) meets two more arms.</summary>
        static bool StreetJunction(CityMap map, int n)
        {
            if (map.nodeEdges[n].Count < 3) return false;
            foreach (int oi in map.nodeEdges[n])
            {
                var o = map.edges[oi];
                if (!o.link && o.cls < 5) return true;
            }
            return false;
        }

        /// <summary>Is a ramp span the exit-speed advisory's own curve (within
        /// 300 m of a freeway diverge along its ramp chain, no slower than the
        /// exit's advisory): the W13-2 at the gore signs it.</summary>
        static bool CoveredByExitSpeed(CityMap map, CityMap.Edge e, CityCurves.Span sp)
        {
            float dist = sp.s0;
            var cur = e;
            for (int hop = 0; hop < 8 && dist < 320f; hop++)
            {
                int n = cur.a;
                CityMap.Edge mainIn = null, linkIn = null;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o == cur || o.b != n || !o.oneway) continue;
                    if (o.link) linkIn = o; else if (o.cls >= 5) mainIn = o;
                }
                if (mainIn != null)
                {
                    int exitMph = CityCurves.ExitMph(map, FirstLinkOut(map, n) ?? cur);
                    return exitMph > 0 && sp.mph >= exitMph && dist <= 300f;
                }
                if (linkIn == null) return false;
                cur = linkIn; dist += cur.length;
            }
            return false;
        }

        static CityMap.Edge FirstLinkOut(CityMap map, int n)
        {
            CityMap.Edge best = null;
            foreach (int oi in map.nodeEdges[n])
            {
                var o = map.edges[oi];
                if (o.a == n && o.link && o.oneway && (best == null || o.index < best.index)) best = o;
            }
            return best;
        }

        /// <summary>
        /// A curve warning (W1 diamond over its W13-1P advisory plaque) or a
        /// chevron (W1-8) on breakaway posts: on the site's side of the
        /// traffic it serves, its near edge MUTCD 2A.19's offset off the
        /// pavement (6 ft past a freeway's or ramp's edge, 2 ft past a
        /// street's), the plaque's foot 6 ft over the road (2A.18: a secondary
        /// sign a foot under the 7 ft), a chevron's foot 5 ft (2C.09: 4 ft at
        /// least); turned to its drivers. Slides a few metres along the road
        /// (and out from it) to free ground.
        /// </summary>
        static bool WarnSign(CityMap map, CityMeshes.Trims trims, Pure pu, Site site)
        {
            why = "no free ground at the curve";
            var e = map.edges[site.edge];
            bool chevron = site.kind == GuideKind.Chevron;
            float diag = chevron ? site.size : site.size * 1.41421f;           // the face's width across
            float faceH = chevron ? site.plaque : site.size * 1.41421f;
            float lateral = (e.link || e.cls >= 5) ? 1.83f : 0.6f;
            foreach (float along in new[] { 0f, -4f, 4f, -8f, 8f, -14f, 14f })
                foreach (float outM in new[] { 0f, 1.5f, 3f })
                {
                    float s = site.s + along * site.dir;
                    if (s < 1f || s > e.length - 1f) continue;
                    if (e.bridge || e.ElevatedAt(s)) continue;
                    var p = e.PointAt(s);
                    var tr = e.TangentAt(s) * site.dir;              // the traffic's way
                    var right = RightOf(tr);
                    LineModel.Extents(e, s, out float eM, out float eP);
                    float ext = site.dir > 0 ? (site.side > 0 ? eM : eP) : (site.side > 0 ? eP : eM);
                    float off = ext + lateral + outM + diag * 0.5f;
                    var g = p + right * (site.side * off);
                    var viewer = Driver(map, e, s, -site.dir * (chevron ? 60f : 100f), AutoLane);
                    var vd = new Vector2(viewer.x - g.x, viewer.z - g.y);
                    if (vd.sqrMagnitude < 1f) continue;
                    vd.Normalize();
                    if (Vector2.Dot(vd, -tr) < 0.5f) continue;      // the face would not look up the road
                    var across = RightOf(-vd);
                    var a = g - across * (diag * 0.5f); var b = g + across * (diag * 0.5f);
                    bool two = !chevron && site.size > 1.0f;
                    var posts = two ? new[] { g - across * (diag * 0.22f), g + across * (diag * 0.22f) } : new[] { g };
                    bool ok = true;
                    foreach (var pp in posts) if (!BreakawayFree(pp)) { ok = false; break; }
                    if (!ok || PavementMargin(map, trims, a) < 0.3f || PavementMargin(map, trims, b) < 0.3f) continue;
                    if (RoadTopAt(map, trims, a, 0f) > float.NegativeInfinity || RoadTopAt(map, trims, b, 0f) > float.NegativeInfinity) continue;
                    if (!OffPoles(a, b, 0.4f)) { pu.wireSteps++; continue; }
                    WarnFace(map, trims, pu, site, e, g, vd, posts, viewer, e.YAt(s), diag, faceH);
                    return true;
                }
            return false;
        }

        /// <summary>The warning's face and posts (as SmallSign stands a guide sign).</summary>
        static void WarnFace(CityMap map, CityMeshes.Trims trims, Pure pu, Site site, CityMap.Edge e, Vector2 g, Vector2 look, Vector2[] posts, Vector3 viewer, float roadY, float diag, float faceH)
        {
            bool chevron = site.kind == GuideKind.Chevron;
            float gy = CityMeshes.LatticeAt(map, g.x, g.y);
            float road = Mathf.Max(RoadTopAt(map, trims, g, 4f), roadY);
            float baseY = float.IsNegativeInfinity(road) ? gy : Mathf.Max(gy, road);
            var n3 = new Vector3(look.x, 0f, look.y);
            var r3 = Vector3.Cross(n3, Vector3.up).normalized;
            var up = Vector3.up;
            float bottom, top;
            Vector3 faceC;
            if (chevron)
            {
                bottom = baseY + 1.52f;
                top = bottom + faceH;
                faceC = new Vector3(g.x, bottom + faceH * 0.5f, g.y);
                // the chevron points the way the road turns: the cell's points right
                var tl = faceC - r3 * (diag * 0.5f) + up * (faceH * 0.5f);
                UvRect(CityGuideFont.Chevron, out float u0, out float v0, out float u1, out float v1);
                if (site.leftHand) { float k = u0; u0 = u1; u1 = k; }
                GQuadUV(tl, tl + r3 * diag, tl + r3 * diag - up * faceH, tl - up * faceH, n3,
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
                PlateBack(faceC, r3, n3, diag * 0.5f, faceH * 0.5f, false);
            }
            else
            {
                // the W13-1P plaque ("40" over "MPH"), the diamond above it
                var plaque = PlaqueLegend(site.mph, site.plaque);
                bottom = baseY + 1.83f;
                var pc = new Vector3(g.x, bottom + plaque.h * 0.5f, g.y);
                HangSmall(plaque, pc, r3, n3);
                float dBottom = bottom + plaque.h + 0.05f;
                faceC = new Vector3(g.x, dBottom + diag * 0.5f, g.y);
                top = dBottom + diag;
                var cell = site.sym == SymTurn ? CityGuideFont.W1Turn : site.sym == SymCurve ? CityGuideFont.W1Curve : site.sym == SymRevTurn ? CityGuideFont.W1RevTurn : CityGuideFont.W1RevCurve;
                UvRect(cell, out float u0, out float v0, out float u1, out float v1);
                float um = (u0 + u1) * 0.5f, vm = (v0 + v1) * 0.5f;
                if (site.leftHand) { float k = u0; u0 = u1; u1 = k; }
                float hd = diag * 0.5f;
                // the diamond's four points, clockwise from the top as seen from the front
                GQuadUV(faceC + up * hd, faceC + r3 * hd, faceC - up * hd, faceC - r3 * hd, n3,
                        new Vector2(um, v0), new Vector2(u1, vm), new Vector2(um, v1), new Vector2(u0, vm));
                PlateBack(faceC, r3, n3, hd, hd, true);
                faceH = top - bottom;
            }
            foreach (var p in posts)
            {
                float py = CityMeshes.LatticeAt(map, p.x, p.y);
                var foot = new Vector3(p.x, Mathf.Min(py, baseY) - 0.4f, p.y) - n3 * 0.098f;
                var head = new Vector3(p.x, top - (chevron ? 0.05f : diag * 0.25f), p.y) - n3 * 0.098f;
                GBox((foot + head) * 0.5f, r3, up, n3, 0.038f, (head.y - foot.y) * 0.5f, 0.038f, CityGuideFont.Galv, true);
                pu.feet.Add(p);
                pu.Take(new Mark { a = p, b = p, r = 0.6f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            }
            var ga = g - new Vector2(r3.x, r3.z) * (diag * 0.5f); var gb = g + new Vector2(r3.x, r3.z) * (diag * 0.5f);
            pu.spans.Add((ga, gb));
            pu.Take(new Mark { a = ga, b = gb, r = 0.4f + RoadsideOccupancy.CellPadM, bit = RoadsideOccupancy.Other });
            var fc = new Vector3(g.x, (bottom + top) * 0.5f, g.y);
            pu.faces.Add(new Face { centre = fc, normal = n3, w = diag, h = top - bottom, kind = Kind.Guide, viewer = viewer });
            pu.sign = new Sign
            {
                kind = Kind.Guide, pos = g, ground = gy, bottom = bottom, top = top, route = -1, edge = e.index,
                legA = posts[0], legB = posts[posts.Length - 1], firstFace = 0, faces = 1, owner = pu.sign.owner,
            };
        }

        /// <summary>A warning sign's aluminium back: a plate 3 cm behind the
        /// face, facing away (a diamond, or a rectangle), and its edges.</summary>
        static void PlateBack(Vector3 c, Vector3 r3, Vector3 n3, float hx, float hy, bool diamond)
        {
            var up = Vector3.up;
            var b = c - n3 * 0.03f;
            Vector3[] f, k;
            if (diamond) { f = new[] { c + up * hy, c + r3 * hx, c - up * hy, c - r3 * hx }; k = new[] { b + up * hy, b + r3 * hx, b - up * hy, b - r3 * hx }; }
            else { f = new[] { c - r3 * hx + up * hy, c + r3 * hx + up * hy, c + r3 * hx - up * hy, c - r3 * hx - up * hy }; k = new[] { b - r3 * hx + up * hy, b + r3 * hx + up * hy, b + r3 * hx - up * hy, b - r3 * hx - up * hy }; }
            // the back, clockwise as seen from behind
            GQuadAt(k[1], k[0], k[3], k[2], -n3, CityGuideFont.Alum);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) & 3;
                var en = Vector3.Cross(f[j] - f[i], n3).normalized;   // outward
                GQuadAt(f[i], f[j], k[j], k[i], en, CityGuideFont.Alum);
            }
        }

        /// <summary>W13-1P, the advisory speed plaque: the speed over MPH, black on yellow, square.</summary>
        static GBlock PlaqueLegend(int mph, float side)
        {
            // (a 24 in plaque: 8.6 in figures over 3.6 in letters, inside its border - it fits square)
            float big = side * 0.36f, small = side * 0.15f;
            var col = new GCol(new List<GBlock> { new GText(mph.ToString(), big, true), new GText("MPH", small, true) }, side * 0.03f);
            var p = new GPlate(col, true, side * 0.06f, side * 0.05f, side * 0.03f);
            p.MinSize(side, side);
            return p;
        }

        // ------------------------------------------------------------------
        //  GORES
        // ------------------------------------------------------------------

        /// <summary>
        /// The gore sign where the gore visibly OPENS (MUTCD 2E.37: "in the
        /// gore"): past the painted gore's nose - CityMeshes paints the gore
        /// between the ramp and the freeway until they part by
        /// <see cref="CityMeshes.GorePaintedGapM"/> - where the ground between
        /// them is grass a metre and more past the nose, and wide enough for
        /// the sign and its posts. Looked for along the ramp's whole link chain
        /// (to 400 m) against the freeway's chain (the round-1 search clamped
        /// to the first edge of each and stopped at 170 m); the sign slides
        /// across the gore and a few metres along it off a lamp, a pole or
        /// another sign's ground.
        /// </summary>
        static bool GoreSign2(CityMap map, CityMeshes.Trims trims, Pure pu, CityMap.Edge e, ExitInfo ex)
        {
            why = "the gore never opens past its painted nose within 400 m";
            if (ex.link == null || ex.main == null) { why = "no ramp"; return false; }
            var legend = GoreLegend(ex);
            float need = Mathf.Max(legend.w + 1.4f, CityMeshes.GorePaintedGapM + 1.2f);
            var ramp = ChainOf(map, ex.link, true, 420f);
            var main = ChainOf(map, ex.main, false, 600f);
            bool opened = false;
            for (float t = 15f; t <= 400f; t += 4f)
            {
                if (!ChainPoint(ramp, t, out var L, out float ls)) break;
                var pl = L.PointAt(ls);
                if (!NearestOn(main, pl, out var M, out float ms)) break;
                var pm = M.PointAt(ms);
                LineModel.Extents(M, ms, out float mR, out float mL);
                LineModel.Extents(L, ls, out float lR, out float lL);
                float dist = Vector2.Distance(pl, pm);
                float gap = dist - (ex.left ? mL : mR) - (ex.left ? lR : lL);
                if (gap < need) { if (opened && gap < CityMeshes.GorePaintedGapM) break; continue; }
                opened = true;
                if (gap > need + 14f) { why = "the gore's ground taken where it opens"; break; }   // deep in the gore: no longer at its nose
                var toMain = (pm - pl).normalized;
                foreach (float frac in new[] { 0.5f, 0.4f, 0.6f })
                {
                    var g = pl + toMain * ((ex.left ? lR : lL) + gap * frac);
                    var viewer = Driver(map, e, e.length, -ViewAheadM, 0f);
                    var vd = new Vector2(viewer.x - g.x, viewer.z - g.y);
                    if (vd.sqrMagnitude < 1f) continue;
                    vd.Normalize();
                    var across = RightOf(-vd);
                    var p0 = g - across * (legend.w * 0.5f); var p1 = g + across * (legend.w * 0.5f);
                    var post0 = g - across * (legend.w * 0.3f); var post1 = g + across * (legend.w * 0.3f);
                    if (!BreakawayFree(post0) || !BreakawayFree(post1) || PavementMargin(map, trims, p0) < 0.5f || PavementMargin(map, trims, p1) < 0.5f) { why = "the gore's ground taken where it opens"; continue; }
                    if (!OffPoles(p0, p1, 0.4f)) { pu.wireSteps++; continue; }
                    SmallSign(map, trims, pu, legend, g, vd, new[] { post0, post1 }, viewer, e, 0.038f, Mathf.Max(M.YAt(ms), L.YAt(ls)));
                    return true;
                }
            }
            return false;
        }

        /// <summary>A chain of edges from one: a ramp's one-way links on (the
        /// lowest-index link out of each end), or the freeway's straightest
        /// mainline on; to a length.</summary>
        static List<CityMap.Edge> ChainOf(CityMap map, CityMap.Edge first, bool links, float reach)
        {
            var l = new List<CityMap.Edge> { first };
            float len = first.length;
            var cur = first;
            for (int hop = 0; hop < 30 && len < reach; hop++)
            {
                CityMap.Edge next = null; float best = 0.3f;
                var t1 = cur.TangentAt(cur.length);
                foreach (int oi in map.nodeEdges[cur.b])
                {
                    var o = map.edges[oi];
                    if (o == cur || o.a != cur.b || !o.oneway) continue;
                    if (links ? !o.link : !IsMainline(o)) continue;
                    float d = Vector2.Dot(o.TangentAt(0f), t1);
                    if (d > best) { best = d; next = o; }
                }
                if (next == null || l.Contains(next)) break;
                l.Add(next); len += next.length; cur = next;
            }
            return l;
        }

        static bool ChainPoint(List<CityMap.Edge> chain, float t, out CityMap.Edge e, out float s)
        {
            foreach (var c in chain)
            {
                if (t <= c.length) { e = c; s = t; return true; }
                t -= c.length;
            }
            e = null; s = 0f;
            return false;
        }

        static bool NearestOn(List<CityMap.Edge> chain, Vector2 p, out CityMap.Edge e, out float s)
        {
            e = null; s = 0f;
            float best = float.MaxValue;
            foreach (var c in chain)
            {
                float cs = ProjectS(c, p);
                float d = (c.PointAt(cs) - p).sqrMagnitude;
                if (d < best) { best = d; e = c; s = cs; }
            }
            return e != null;
        }

        // ------------------------------------------------------------------
        //  THE LAYOUT CHECK (the highway signs check): every face's mosaic, as built
        // ------------------------------------------------------------------

        /// <summary>
        /// Every placed guide face's quads, as the tiles draw them: the legend
        /// quads lying in the face's plane must stay inside its rectangle (a
        /// legend that CLIPS) and never overlap one another (two layers in one
        /// plane: a z-fight); and no two signs' faces may stand within 5 cm of
        /// one plane overlapping (a sign drawn twice). Returns the report lines;
        /// counts in the out parameters.
        /// </summary>
        public static List<string> LayoutCheck(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings,
                                               out int facesChecked, out int clipped, out int overlapped, out int twinFaces)
        {
            var lines = new List<string>();
            facesChecked = clipped = overlapped = twinFaces = 0;
            Setup(map, trims, buildings, 0, 0);
            EnsureSites(map);
            var all = new List<(Vector3 c, Vector3 n, float w, float h, int site)>();
            for (int si = 0; si < sites.Count; si++)
            {
                var st = sites[si];
                Setup(map, trims, buildings, Mathf.FloorToInt(st.nominal.x / CityMeshes.TileSize), Mathf.FloorToInt(st.nominal.y / CityMeshes.TileSize));
                var pu = PureGuide(map, trims, si);
                if (!pu.placed) continue;
                foreach (var f in pu.faces)
                {
                    facesChecked++;
                    var n = f.normal.normalized;
                    var r = Vector3.Cross(n, Vector3.up).normalized;
                    var inPlane = new List<(float x0, float y0, float x1, float y1)>();
                    foreach (var q in pu.gquads)
                    {
                        // a quad of this face: in its plane (the legend lies 1 mm in front of it), facing with it
                        if (Vector3.Dot(q.n, n) < 0.999f) continue;
                        float dz = Vector3.Dot(q.a - f.centre, n);
                        if (Mathf.Abs(dz - 0.001f) > 0.0015f) continue;
                        float qx0 = float.MaxValue, qx1 = float.MinValue, qy0 = float.MaxValue, qy1 = float.MinValue;
                        foreach (var v in new[] { q.a, q.b, q.c, q.d })
                        {
                            float x = Vector3.Dot(v - f.centre, r), y = v.y - f.centre.y;
                            qx0 = Mathf.Min(qx0, x); qx1 = Mathf.Max(qx1, x); qy0 = Mathf.Min(qy0, y); qy1 = Mathf.Max(qy1, y);
                        }
                        if (qx1 < -f.w * 0.5f - 0.5f || qx0 > f.w * 0.5f + 0.5f || qy1 < -f.h * 0.5f - 0.5f || qy0 > f.h * 0.5f + 0.5f) continue;   // another face's (a tab beside it)
                        inPlane.Add((qx0, qy0, qx1, qy1));
                    }
                    bool clip = false, over = false;
                    float hw = f.w * 0.5f, hh = f.h * 0.5f;
                    foreach (var (x0, y0, x1, y1) in inPlane)
                    {
                        // a quad that lies over this face (the tab above a panel, the panel below
                        // its tab, are other faces in the same plane) and runs past its edge
                        bool overlaps = Mathf.Min(x1, hw) - Mathf.Max(x0, -hw) > 0.002f && Mathf.Min(y1, hh) - Mathf.Max(y0, -hh) > 0.002f;
                        if (overlaps && (x0 < -hw - 0.002f || x1 > hw + 0.002f || y0 < -hh - 0.002f || y1 > hh + 0.002f)) { clip = true; break; }
                    }
                    for (int i = 0; i < inPlane.Count && !over; i++)
                        for (int j = i + 1; j < inPlane.Count; j++)
                        {
                            var A = inPlane[i]; var B = inPlane[j];
                            float ox = Mathf.Min(A.x1, B.x1) - Mathf.Max(A.x0, B.x0), oy = Mathf.Min(A.y1, B.y1) - Mathf.Max(A.y0, B.y0);
                            if (ox > 0.002f && oy > 0.002f) { over = true; break; }
                        }
                    if (clip) { clipped++; if (lines.Count < 30) lines.Add($"  CLIPS: site {si} {st.kind} face at ({f.centre.x:0.0},{f.centre.y:0.0},{f.centre.z:0.0}) {f.w:0.00} x {f.h:0.00} m"); }
                    if (over) { overlapped++; if (lines.Count < 30) lines.Add($"  OVERLAP: site {si} {st.kind} face at ({f.centre.x:0.0},{f.centre.y:0.0},{f.centre.z:0.0})"); }
                    all.Add((f.centre, n, f.w, f.h, si));
                }
            }
            // two signs' faces in one plane, overlapping: a sign drawn twice
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    var A = all[i]; var B = all[j];
                    if (A.site == B.site || (A.c - B.c).sqrMagnitude > 25f) continue;
                    if (Vector3.Dot(A.n, B.n) < 0.98f || Mathf.Abs(Vector3.Dot(B.c - A.c, A.n)) > 0.05f) continue;
                    var r = Vector3.Cross(A.n, Vector3.up).normalized;
                    float dx = Mathf.Abs(Vector3.Dot(B.c - A.c, r)), dy = Mathf.Abs(B.c.y - A.c.y);
                    if (dx < (A.w + B.w) * 0.5f && dy < (A.h + B.h) * 0.5f)
                    {
                        twinFaces++;
                        if (lines.Count < 40) lines.Add($"  TWO SIGNS IN ONE PLANE: sites {A.site} ({sites[A.site].kind}) and {B.site} ({sites[B.site].kind}) at ({A.c.x:0.0},{A.c.y:0.0},{A.c.z:0.0})");
                    }
                }
            return lines;
        }

        /// <summary>The census's look at every guide quad near a point (a
        /// glyph probe): the faces within r, and every quad of every site whose
        /// corner comes within r, by its depth along the nearest face's normal.</summary>
        public static List<string> QuadProbe(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, Vector3 at, float r)
        {
            var lines = new List<string>();
            Setup(map, trims, buildings, Mathf.FloorToInt(at.x / CityMeshes.TileSize), Mathf.FloorToInt(at.z / CityMeshes.TileSize));
            EnsureSites(map);
            for (int si = 0; si < sites.Count; si++)
            {
                var st = sites[si];
                if ((st.nominal - new Vector2(at.x, at.z)).sqrMagnitude > (r + ReachGantryM) * (r + ReachGantryM)) continue;
                Setup(map, trims, buildings, Mathf.FloorToInt(st.nominal.x / CityMeshes.TileSize), Mathf.FloorToInt(st.nominal.y / CityMeshes.TileSize));
                var pu = PureGuide(map, trims, si);
                if (!pu.placed) continue;
                int near = 0;
                var depths = new SortedDictionary<int, int>();
                Face nf = default; float nd = float.MaxValue;
                foreach (var f in pu.faces) { float d = (f.centre - at).sqrMagnitude; if (d < nd) { nd = d; nf = f; } }
                foreach (var q in pu.gquads)
                {
                    if ((q.a - at).sqrMagnitude > r * r && (q.c - at).sqrMagnitude > r * r) continue;
                    near++;
                    int mm = Mathf.RoundToInt(Vector3.Dot(q.a - nf.centre, nf.normal.normalized) * 1000f);
                    depths.TryGetValue(mm, out int k); depths[mm] = k + 1;
                }
                if (near == 0) continue;
                var sb = new System.Text.StringBuilder();
                foreach (var kv in depths) sb.Append($" {kv.Key}mm:{kv.Value}");
                lines.Add($"  site {si} {st.kind} (exit edge {st.exitEdge}): {pu.faces.Count} faces, nearest face at ({nf.centre.x:0.00},{nf.centre.y:0.00},{nf.centre.z:0.00}) n ({nf.normal.x:0.000},{nf.normal.z:0.000}); {near} quads within {r:0.0} m by depth along it:{sb}");
            }
            return lines;
        }

        /// <summary>Eye views onto round 2's new signs for the shots
        /// (CityPreview's PSX_EYE_LOOKS lines, name:x:z:road:lookX:lookZ:lens): up to
        /// <paramref name="each"/> of each kind, nearest <paramref name="near"/> first -
        /// the eye along the face's normal (its drivers' side), the lens to frame it.</summary>
        public static List<string> ShotSpots(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings, Vector2 near, int each)
        {
            Setup(map, trims, buildings, 0, 0);
            EnsureSites(map);
            var found = new Dictionary<string, List<(float d, string line)>>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int si = 0; si < sites.Count; si++)
            {
                var st = sites[si];
                Setup(map, trims, buildings, Mathf.FloorToInt(st.nominal.x / CityMeshes.TileSize), Mathf.FloorToInt(st.nominal.y / CityMeshes.TileSize));
                var pu = PureGuide(map, trims, si);
                if (!pu.placed || pu.faces.Count == 0) continue;
                string kind = null; float back = 45f, lens = 14f;
                if (pu.kind == Kind.Bridge) { kind = "bridge"; back = 120f; lens = 16f; }
                else if (st.kind == GuideKind.Advance && pu.kind == Kind.Gantry && ExitOf(map, st.exitEdge).dropped > 0) { kind = "exitonly"; back = 110f; lens = 16f; }
                else if (st.kind == GuideKind.Curve && map.edges[st.edge].link) { kind = "curve"; back = 60f; lens = 16f; }
                else if (st.kind == GuideKind.Chevron) { kind = "chevron"; back = 70f; lens = 34f; }
                else if (st.kind == GuideKind.Gore) { kind = "gore"; back = 56f; lens = 14f; }
                if (kind == null) continue;
                var f = pu.faces[0];
                // the eye where its drivers are: on its own road, that far back along it
                var se = map.edges[st.kind == GuideKind.Curve || st.kind == GuideKind.Chevron ? st.edge : pu.sign.edge];
                float ss = Mathf.Clamp(ProjectS(se, new Vector2(f.centre.x, f.centre.z)), 0f, se.length);
                int sdir = st.kind == GuideKind.Curve || st.kind == GuideKind.Chevron ? st.dir : 1;
                var v3 = Driver(map, se, ss, -sdir * back, AutoLane);
                var eye = new Vector2(v3.x, v3.z);
                float d = Vector2.Distance(eye, near);
                if (!found.TryGetValue(kind, out var l)) found[kind] = l = new List<(float, string)>();
                l.Add((d, string.Format(ci, "s2{0}{1}:{2:0.0}:{3:0.0}:{7}:{4:0.0}:{5:0.0}:{6:0}", kind, si, eye.x, eye.y, f.centre.x, f.centre.z, lens, (se.name ?? "").Replace(":", "").Replace(";", ""))));
            }
            var lines = new List<string>();
            foreach (var kv in found)
            {
                kv.Value.Sort((x, y) => x.d.CompareTo(y.d));
                for (int i = 0; i < Mathf.Min(each, kv.Value.Count); i++) lines.Add(kv.Value[i].line);
            }
            return lines;
        }

        /// <summary>The census's per-site record of round 2: the structure a
        /// site stood as, and the numbers the gate reads.</summary>
        public struct S2Stats
        {
            public int signBridges, cantilevers, bridgeMounted, groundAdvance, shared, sharedHosts, exitOnlyPanels, exitOnlyArrows,
                       curveSigns, curveSignsLeft, chevrons, curveRefused, chevronRefused, lampHeads, droppedMile, droppedHalf, droppedMinor,
                       spansSeen, spansSigned, spansCovered, spansKink, spansSmall, spansCarried, spansTwin, goresPlaced, goresOnPaint;
            public float freewayCarriagewayKm;
        }

        /// <summary>Round 2's numbers over every site (the census).</summary>
        public static S2Stats Signs2Stats(CityMap map, CityMeshes.Trims trims, Dictionary<long, List<CityBuildings.B>> buildings)
        {
            var s = new S2Stats();
            Setup(map, trims, buildings, 0, 0);
            EnsureSites(map);
            foreach (var e in map.edges) if (IsMainline(e)) s.freewayCarriagewayKm += e.length / 1000f;
            s.droppedMile = droppedMile; s.droppedHalf = droppedHalf; s.droppedMinor = droppedMinor;
            s.spansSeen = curveSpansSeen; s.spansCarried = curveSpansCarried; s.spansTwin = curveSpansTwin; s.spansSigned = curveSpansSigned; s.spansCovered = curveSpansCovered; s.spansKink = curveSpansKink; s.spansSmall = curveSpansSmall;
            for (int si = 0; si < sites.Count; si++)
            {
                var st = sites[si];
                Setup(map, trims, buildings, Mathf.FloorToInt(st.nominal.x / CityMeshes.TileSize), Mathf.FloorToInt(st.nominal.y / CityMeshes.TileSize));
                var pu = PureGuide(map, trims, si);
                if (st.kind == GuideKind.Advance && st.host > 0) { if (whyNot.TryGetValue(si, out var w) && w == "shared") s.shared++; continue; }
                if (st.extras != null && st.extras.Count > 0 && pu.placed && pu.extraDrawn) s.sharedHosts++;
                if (!pu.placed)
                {
                    if (st.kind == GuideKind.Curve) s.curveRefused++;
                    if (st.kind == GuideKind.Chevron) s.chevronRefused++;
                    continue;
                }
                s.lampHeads += pu.guideLamps.Count;
                switch (pu.kind)
                {
                    case Kind.Gantry:
                        if ((pu.sign.legA - pu.sign.legB).sqrMagnitude > 0.01f) s.signBridges++; else s.cantilevers++;
                        break;
                    case Kind.Bridge: s.bridgeMounted++; break;
                    case Kind.Guide:
                        if (st.kind == GuideKind.Advance) s.groundAdvance++;
                        else if (st.kind == GuideKind.Curve) { s.curveSigns++; if (st.side < 0) s.curveSignsLeft++; }
                        else if (st.kind == GuideKind.Chevron) s.chevrons++;
                        else if (st.kind == GuideKind.Gore) s.goresPlaced++;
                        break;
                }
                if ((st.kind == GuideKind.ExitGantry || st.kind == GuideKind.Advance) && (pu.kind == Kind.Gantry || pu.kind == Kind.Bridge))
                {
                    var ex = ExitOf(map, st.exitEdge);
                    if (ex.dropped > 0 && S2ExitOnly) { s.exitOnlyPanels++; if (st.kind == GuideKind.Advance) s.exitOnlyArrows += ex.dropped; }
                }
                if (st.kind == GuideKind.Gore && pu.faces.Count > 0)
                {
                    // round 1's flaw, measured: a gore sign standing where the gore is still painted
                    var ex = ExitOf(map, st.exitEdge);
                    if (ex.link != null && ex.main != null)
                    {
                        var g = pu.sign.pos;
                        float sm = ProjectS(ex.main, g), sl = ProjectS(ex.link, g);
                        LineModel.Extents(ex.main, sm, out float mR, out float mL);
                        LineModel.Extents(ex.link, sl, out float lR, out float lL);
                        float gap = Vector2.Distance(ex.main.PointAt(sm), ex.link.PointAt(sl)) - (ex.left ? mL : mR) - (ex.left ? lR : lL);
                        if (gap < CityMeshes.GorePaintedGapM) s.goresOnPaint++;
                    }
                }
            }
            return s;
        }
    }
}
