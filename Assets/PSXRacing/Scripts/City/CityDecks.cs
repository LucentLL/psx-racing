using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// PARKING DECKS (2026-10-05, part 1): the whitelisted OSM multi-storey
    /// car parks as decks you can drive into, up to the roof and back out.
    /// tools/city/lib/decks.mjs decides which (levels known, a rectangle that
    /// fills its footprint, wide and long enough for the helix, on the owner's
    /// whitelist) and writes <c>charlotte_decks.bytes</c>; this class reads it,
    /// solves each deck against the map once (which end is the ground turning
    /// bay, where the opening and its driveway are, the floor heights) and
    /// emits the LAP PATH that part 2's time trial will race on. CityMeshes
    /// (CityMeshes.Decks.cs) draws it from the same numbers, so the path and
    /// the floors can never disagree.
    ///
    /// THE LAYOUT: a two-bay SLOPED-FLOOR HELIX, the common US design. The
    /// rectangle's long axis is local x, the ground turning bay at -x. Bay A
    /// (z &lt; 0) rises +x, bay B (z &gt; 0) rises -x, each half a storey over its
    /// sloped run between flat turning bays of depth <see cref="Deck.T"/> at
    /// both ends; one turn round the spine wall climbs one storey
    /// (<see cref="FloorM"/>). The turning bays at -x are the levels: ground,
    /// then one per storey, the last the roof. Vertical curves of
    /// <see cref="VCurveM"/> at every grade break (no launch). Two-way
    /// circulation: up on the right (outer lane, left turns), down on the
    /// right (inner lane, right turns), <see cref="LaneOffM"/> off the aisle
    /// centre. PSX_DECKS=0 leaves every deck the solid building it was.
    /// </summary>
    public static class CityDecks
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_DECKS") != "0";
        /// <summary>Set once a deck has been drawn on a tile: the chase camera's
        /// cover clamp runs only then.</summary>
        public static bool AnyBuilt;

        public const float FloorM = 3.05f, SlabM = 0.35f, ParapetM = 1.07f, WallM = 0.3f, SpineM = 0.3f;
        public const float LaneOffM = 1.2f, VCurveM = 4f, MaxSlope = 0.072f;
        public const float RampLead = 1.6f;
        /// <summary>Where the land stands up to this far over the entry ramp
        /// the ramp drapes over it (the land's own shape); more stays solid.</summary>
        public const float DrapeM = 0.12f;
        static CityMap mapRef;
        public const float OpeningM = 8f, MaxDrivewayM = 40f, MaxDriveGrade = 0.12f;
        public const float ColumnM = 0.5f, ColumnPitchM = 3f * 2.74f, LapStepM = 2.5f;

        public sealed class Deck
        {
            public int index, bld; public uint way; public int levels; public byte layout, reason, flags;
            public Vector2 c, u; public float hu, hv, fill, h;
            public bool Listed => (flags & 1) != 0 && layout == 1 && reason == 0;
            public bool solved, ok, built; public string why = "";
            /// <summary>Local axes: U along the long side toward the far
            /// turning bay, V = U turned left (the +z side, bay B).</summary>
            public Vector2 U, V;
            public float y0, yLow, T, x0, x1, slope, lin, ltot;
            /// <summary>0: the opening is in the -x end wall; -1 / +1: in that
            /// long side, within the ground turning bay.</summary>
            public int entrySide; public float entryAt;
            public Vector2 roadEdge; public float roadY, driveLen;
            /// <summary>The street the driveway meets (graph edge, arc along
            /// it): where a deck run's grid and finish stand.</summary>
            public int streetEdge = -1; public float streetS;
            /// <summary>World waypoints, street to roof and back to the
            /// street, every ~<see cref="LapStepM"/>.</summary>
            public Vector3[] lap;
            public int trisDeck;
            /// <summary>The only name the game ever shows (no brands).</summary>
            public string Label => "Parking deck, " + levels + " levels";
            public Vector2 W(float x, float z) => c + U * x + V * z;
            public Vector3 W3(float x, float y, float z) { var p = W(x, z); return new Vector3(p.x, y0 + y, p.y); }
            /// <summary>Local outward normal of the opening, and its centre on the outer face.</summary>
            public Vector2 EntryN => entrySide == 0 ? new Vector2(-1f, 0f) : new Vector2(0f, entrySide);
            public Vector2 EntryP => entrySide == 0 ? new Vector2(-hu, entryAt) : new Vector2(entryAt, entrySide * hv);
        }

        static Deck[] all;
        static Dictionary<int, Deck> byBld;
        public static Deck[] All { get { Load(); return all; } }

        static void Load()
        {
            if (all != null) return;
            all = new Deck[0]; byBld = new Dictionary<int, Deck>();
            var ta = Resources.Load<TextAsset>("charlotte_decks");
            if (ta == null) { Debug.LogWarning("[City] charlotte_decks.bytes missing: every deck stays solid"); return; }
            var b = ta.bytes; int o = 0;
            uint U32() { uint v = System.BitConverter.ToUInt32(b, o); o += 4; return v; }
            float F() { float v = System.BitConverter.ToSingle(b, o); o += 4; return v; }
            if (U32() != 0x4B434450u || U32() != 1u) { Debug.LogWarning("[City] charlotte_decks.bytes: not PDCK v1"); return; }
            int n = (int)U32();
            all = new Deck[n];
            for (int i = 0; i < n; i++)
            {
                var d = new Deck { index = i };
                d.bld = (int)U32(); d.way = U32();
                d.levels = b[o]; d.layout = b[o + 1]; d.reason = b[o + 2]; d.flags = b[o + 3]; o += 4;
                d.c = new Vector2(F(), F()); d.u = new Vector2(F(), F()).normalized;
                d.hu = F(); d.hv = F(); d.fill = F(); d.h = F();
                all[i] = d;
                if (d.bld >= 0 && d.Listed) byBld[d.bld] = d;
            }
        }

        /// <summary>The listed deck standing on footprint <paramref name="fi"/>, or null.</summary>
        public static Deck ForFootprint(int fi)
        {
            if (!Enabled) return null;
            Load();
            return byBld.TryGetValue(fi, out var d) ? d : null;
        }

        // ---- the floors -------------------------------------------------
        /// <summary>Bay A's rise from its foot (x0) to its head (x1): 0 to
        /// half a storey, parabolic over <see cref="VCurveM"/> at both ends.</summary>
        public static float Ramp(Deck d, float x)
        {
            float Ls = d.x1 - d.x0, t = Mathf.Clamp(x - d.x0, 0f, Ls), s = d.slope, Lc = VCurveM;
            if (t < Lc) return s * t * t / (2f * Lc);
            if (t > Ls - Lc) { float r = Ls - t; return FloorM * 0.5f - s * r * r / (2f * Lc); }
            return s * (t - Lc * 0.5f);
        }
        /// <summary>Local floor heights (over <see cref="Deck.y0"/>): bay A and
        /// bay B in turn k, the -x turning bay of level k, the +x one of turn k.</summary>
        public static float BayA(Deck d, int k, float x) => k * FloorM + Ramp(d, x);
        public static float BayB(Deck d, int k, float x) => k * FloorM + FloorM * 0.5f + Ramp(d, d.x0 + d.x1 - x);
        public static float TurnLo(int k) => k * FloorM;
        public static float TurnHi(int k) => k * FloorM + FloorM * 0.5f;

        static readonly HashSet<int> segs = new HashSet<int>();

        /// <summary>Decide the deck against the map, once: false leaves it the
        /// solid building (<see cref="Deck.why"/> says why).</summary>
        public static bool Solve(CityMap map, Deck d)
        {
            if (d.solved) return d.ok;
            d.solved = true; d.ok = false;
            mapRef = map;
            if (!d.Listed || d.levels < 2) { d.why = "not listed"; return false; }
            var u = d.u; var v = new Vector2(-u.y, u.x);
            // the entrance: a way that ENDS at the footprint (a service drive or
            // parking aisle into it), else the nearest street
            Vector2 target = Vector2.zero; bool found = false;
            float r = Mathf.Max(d.hu, d.hv) + 4f;
            segs.Clear();
            map.EdgeSegsInRect(d.c - new Vector2(r, r), d.c + new Vector2(r, r), segs);
            float bestD = float.MaxValue;
            foreach (int packed in segs)
            {
                var e = map.edges[packed >> 12];
                if (e.link || e.cls >= 4) continue;
                foreach (var end in new[] { e.pts[0], e.pts[e.pts.Length - 1] })
                {
                    var q = end - d.c;
                    float qu = Mathf.Abs(Vector2.Dot(q, u)) - d.hu, qv = Mathf.Abs(Vector2.Dot(q, v)) - d.hv;
                    if (qu > 4f || qv > 4f) continue;
                    float dd = q.sqrMagnitude;
                    if (dd < bestD) { bestD = dd; target = end; found = true; }
                }
            }
            if (!found)
            {
                if (!map.NearestRoadPoint(d.c, 160f, true, out int ei, out float s, out _)) { d.why = "no street within 160 m"; return false; }
                target = map.edges[ei].PointAt(s);
            }
            var rel = target - d.c;
            float sign = Vector2.Dot(rel, u) <= 0f ? 1f : -1f;
            d.U = u * sign; d.V = new Vector2(-d.U.y, d.U.x);
            float lx = Vector2.Dot(rel, d.U), lz = Vector2.Dot(rel, d.V);
            d.T = d.hv * 0.5f + LaneOffM + 2f;
            d.x0 = -d.hu + d.T; d.x1 = d.hu - d.T;
            float Ls = d.x1 - d.x0;
            if (Ls < 2f * VCurveM + 6f) { d.why = "too short for its sloped bays"; return false; }
            d.slope = FloorM * 0.5f / (Ls - VCurveM);
            if (d.slope > MaxSlope) { d.why = "bay slope " + (d.slope * 100f).ToString("0.0") + "% over " + (MaxSlope * 100f).ToString("0.0") + "%"; return false; }
            float half = OpeningM * 0.5f;
            bool endWall = -lx - d.hu >= Mathf.Abs(lz) - d.hv;
            // a side opening always goes in the +z wall (bay B's side, over the
            // fill): the ground turning bay then ramps up across its width
            if (!endWall && lz < 0f) { d.U = -d.U; d.V = -d.V; lx = -lx; lz = -lz; }
            if (endWall) { d.entrySide = 0; d.entryAt = Mathf.Clamp(lz, -d.hv + half + 1f, d.hv - half - 1f); d.lin = d.T - 2f; }
            else { d.entrySide = 1; d.entryAt = Mathf.Clamp(lx, -d.hu + half + 1f, d.x0 - half - 0.5f); d.lin = d.hv; }
            // the driveway: straight out of the opening to the street's edge
            var eP = d.W(d.EntryP.x, d.EntryP.y);
            var nW = d.U * d.EntryN.x + d.V * d.EntryN.y;
            if (!map.NearestRoadPoint(eP + nW * 2f, MaxDrivewayM + 30f, true, out int re, out float rs, out _)) { d.why = "no street in front of the opening"; return false; }
            var edge = map.edges[re];
            d.streetEdge = re; d.streetS = rs;
            var P = edge.PointAt(rs);
            float ahead = Vector2.Dot(P - eP, nW);
            if (ahead < -1f || Vector2.Dot((P - eP).normalized, nW) < 0.4f) { d.why = "the street is not in front of the opening"; return false; }
            d.driveLen = Mathf.Max(0f, ahead - edge.HalfMax);
            if (d.driveLen > MaxDrivewayM) { d.why = "driveway " + d.driveLen.ToString("0") + " m"; return false; }
            d.roadEdge = eP + nW * d.driveLen;
            d.roadY = CityElevation.GroundY(map, d.roadEdge.x, d.roadEdge.y);
            d.ltot = d.driveLen + d.lin;
            // every floor clear of the land: y0 from each sample the entry ramp
            // does not cover (bay B's first run and the first far turn stand on fill)
            float need = d.roadY + 0.02f, lowG = float.MaxValue;
            float sx = Mathf.Max(2f, d.hu / 10f), sz = Mathf.Max(2f, d.hv / 8f);
            for (float x = -d.hu; x <= d.hu + 0.01f; x += sx)
                for (float z = -d.hv; z <= d.hv + 0.01f; z += sz)
                {
                    var p = d.W(x, z);
                    float g = CityElevation.GroundY(map, p.x, p.y), f;
                    lowG = Mathf.Min(lowG, g);
                    if (x < d.x0) { if (Depth(d, x, z) < d.lin) continue; f = 0f; }
                    else if (x > d.x1 || z > 0f) f = TurnHi(0);
                    else f = BayA(d, 0, x);
                    need = Mathf.Max(need, g + 0.06f - f);
                }
            d.y0 = need; d.yLow = lowG - 0.4f;
            for (int pass = 0; ; pass++)
            {
            float rise = d.y0 - d.roadY;
            if (rise * RampLead > MaxDriveGrade * d.ltot)
            { d.why = "entrance grade (" + rise.ToString("0.00") + " m over " + d.ltot.ToString("0.0") + " m)"; return false; }
            // the ramped part of the ground turning bay over the land
            float worst = 0f;
            for (float x = -d.hu; x <= d.x0 + 0.01f; x += 1.5f)
                for (float z = -d.hv; z <= d.hv + 0.01f; z += 1.5f)
                {
                    float dep = Depth(d, x, z);
                    if (dep >= d.lin || dep < 0f) continue;
                    var p = d.W(x, z);
                    worst = Mathf.Max(worst, CityElevation.GroundY(map, p.x, p.y) + 0.03f - ProfileY(d, d.driveLen + dep));
                }
            if (worst <= DrapeM) break;
            if (pass >= 2) { d.why = "the land stands " + worst.ToString("0.00") + " m over the entry ramp"; return false; }
            d.y0 += worst + 0.02f;   // lift the floor and try again (a steeper ramp)
            }
            d.lap = BuildLap(d);
            d.ok = true; d.why = "";
            return true;
        }

        /// <summary>Metres in from the opening's wall (negative: outside it).</summary>
        /// Inside, the distance from the OPENING (its 8 m of wall), so the ramp
        /// rises round it like a fan and the rest of the wall stands high.
        public static float Depth(Deck d, float x, float z)
        {
            float dep = -((x - d.EntryP.x) * d.EntryN.x + (z - d.EntryP.y) * d.EntryN.y);
            if (dep <= 0f) return dep;
            float along = -(x - d.EntryP.x) * d.EntryN.y + (z - d.EntryP.y) * d.EntryN.x;
            float off = Mathf.Max(0f, Mathf.Abs(along) - OpeningM * 0.5f);
            return Mathf.Sqrt(dep * dep + off * off);
        }
        /// <summary>World height of the entry ramp <paramref name="r"/> metres
        /// in from the street's edge: one smoothstep from the road to the ground
        /// floor over the driveway AND the ground turning bay's ramped part
        /// (<see cref="Deck.ltot"/>), zero grade at both ends.</summary>
        public static float ProfileY(Deck d, float r)
        {
            if (r <= 0f) return d.roadY;
            if (r >= d.ltot) return d.y0;
            // a Hermite: leaves the street at RampLead x the mean grade (a sag,
            // never a crest), arrives on the ground floor level (no launch)
            float t = r / d.ltot;
            float h = (t * t * t - 2f * t * t + t) * RampLead + t * t * (3f - 2f * t);
            return d.roadY + (d.y0 - d.roadY) * h;
        }
        /// <summary>World height <paramref name="a"/> metres OUT of the opening
        /// (negative: inside, on the ground turning bay).</summary>
        public static float DriveAt(Deck d, float a) => ProfileY(d, d.driveLen - a);
        /// <summary>The ground turning bay's floor, local (over y0).</summary>
        public static float GroundBayLocal(Deck d, float x, float z)
        {
            float dep = Depth(d, x, z), y = ProfileY(d, d.driveLen + dep);
            if (mapRef != null && dep < d.lin)
            {
                var p = d.W(x, z);
                y = Mathf.Max(y, CityElevation.GroundY(mapRef, p.x, p.y) + 0.03f);
            }
            return y - d.y0;
        }

        // ---- the lap -----------------------------------------------------
        static void Arc(List<Vector3> L, Vector2 c, float r, float a0, float a1, float y)
        {
            int n = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(a1 - a0) * Mathf.Deg2Rad * r / LapStepM));
            for (int i = 1; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                L.Add(new Vector3(c.x + r * Mathf.Cos(a), y, c.y + r * Mathf.Sin(a)));
            }
        }
        static void Run(List<Vector3> L, float xa, float xb, float z, System.Func<float, float> y)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(xb - xa) / LapStepM));
            for (int i = 1; i <= n; i++) { float x = Mathf.Lerp(xa, xb, i / (float)n); L.Add(new Vector3(x, y(x), z)); }
        }
        static void Bezier(List<Vector3> L, Vector2 p0, Vector2 h0, Vector2 p3, Vector2 h3, float y)
        {
            float k = Vector2.Distance(p0, p3) / 3f;
            Vector2 p1 = p0 + h0 * k, p2 = p3 - h3 * k;
            int n = Mathf.Max(4, Mathf.CeilToInt(3f * k / LapStepM));
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, m = 1f - t;
                var p = m * m * m * p0 + 3f * m * m * t * p1 + 3f * m * t * t * p2 + t * t * t * p3;
                L.Add(new Vector3(p.x, y, p.y));
            }
        }

        /// <summary>Street, driveway, opening, up the outer lane turn by turn,
        /// a loop on the roof's turning bay, down the inner lane, out the same
        /// opening to the street. Local points, then world.</summary>
        static Vector3[] BuildLap(Deck d)
        {
            var L = new List<Vector3>(256);
            float hz = d.hv * 0.5f, o = LaneOffM, Ro = hz + o, Ri = hz - o;
            int top = d.levels - 1;
            Vector2 E = d.EntryP, n = d.EntryN;
            // on the street, then up the driveway
            // In and out keep RIGHT on the driveway too (a deck run meets the
            // leaders coming out while the tail goes in): each way is the
            // driveway's centre moved a lane offset to the right of travel.
            var keepIn = new Vector2(-n.y, n.x) * o;      // right of -n
            var street = E + n * (d.driveLen + 2.5f);
            L.Add(new Vector3(street.x + keepIn.x, float.NaN, street.y + keepIn.y));
            int nd = Mathf.Max(2, Mathf.CeilToInt((d.driveLen + 2.5f) / LapStepM));
            for (int i = 1; i <= nd; i++)
            {
                float t = i / (float)nd; var p = Vector2.Lerp(street, E, t) + keepIn;
                L.Add(new Vector3(p.x, float.NaN, p.y));
            }
            var inP = E - n * 3f;
            L.Add(new Vector3(inP.x + keepIn.x, float.NaN, inP.y + keepIn.y));
            Bezier(L, inP + keepIn, -n, new Vector2(d.x0, -hz - o), new Vector2(1f, 0f), float.NaN);
            for (int k = 0; k <= top - 1; k++)
            {
                int kk = k;
                Run(L, d.x0, d.x1, -hz - o, x => BayA(d, kk, x));
                Arc(L, new Vector2(d.x1, 0f), Ro, -90f, 90f, TurnHi(k));
                Run(L, d.x1, d.x0, hz + o, x => BayB(d, kk, x));
                if (k < top - 1) Arc(L, new Vector2(d.x0, 0f), Ro, 90f, 270f, TurnLo(k + 1));
            }
            // the roof: a loop in the top turning bay's +z half (never near
            // the drop over bay A's last run)
            float yTop = TurnLo(top);
            float R = Mathf.Min(7f, (d.T - WallM - 2.5f) * 0.5f, Ri - 0.5f);
            var C = new Vector2(-d.hu + WallM + 1.2f + R, hz);
            Arc(L, C, R, 60f, 300f, yTop);
            L.Add(new Vector3(d.x0, yTop, hz - o));
            for (int k = top - 1; k >= 0; k--)
            {
                int kk = k;
                Run(L, d.x0, d.x1, hz - o, x => BayB(d, kk, x));
                Arc(L, new Vector2(d.x1, 0f), Ri, 90f, -90f, TurnHi(k));
                Run(L, d.x1, d.x0, -hz + o, x => BayA(d, kk, x));
                if (k > 0) Arc(L, new Vector2(d.x0, 0f), Ri, -90f, -270f, TurnLo(k));
            }
            Bezier(L, new Vector2(d.x0, -hz + o), new Vector2(-1f, 0f), inP - keepIn, n, float.NaN);
            for (int i = 0; i <= nd; i++)
            {
                float t = i / (float)nd; var p = Vector2.Lerp(E, street, t) - keepIn;
                L.Add(new Vector3(p.x, float.NaN, p.y));
            }
            var w = new Vector3[L.Count];
            for (int i = 0; i < L.Count; i++) w[i] = d.W3(L[i].x, float.IsNaN(L[i].y) ? GroundBayLocal(d, L[i].x, L[i].z) : L[i].y, L[i].z);
            return w;
        }
    }

    /// <summary>
    /// A DECK RUN (parking decks part 2): <see cref="Deck.lap"/> raced as a
    /// point-to-point TrackPath. On the street in the right-hand lane with the
    /// deck on the right, a right turn in, the lap (up every level keeping
    /// right, a loop on the roof, down keeping right), a right turn out and on
    /// along the same street to the flag. The grid stands single file on the
    /// street before the driveway; checkpoints every <see cref="GateEveryM"/>
    /// hold the player's progress until they are driven through.
    /// A time trial's best is kept here, per deck, in PlayerPrefs - the CITY
    /// edition has no career save, and a record is not worth a save version.
    /// </summary>
    public static class DeckRun
    {
        public const float Step = 2.5f, CurvGain = 2.5f, LaneM = 1.7f, TurnM = 7f;
        public const float LeadInM = 50f, LineM = 9f, FlagM = 30f, LeadOutM = 90f, GateEveryM = 40f;

        /// <summary>The start line of the last path built (the harness reads it).</summary>
        public static int LastLine;

        public static CityDecks.Deck Find(uint way)
        {
            foreach (var d in CityDecks.All) if (d.way == way) return d;
            return null;
        }

        static Vector2 Right(Vector2 h) => new Vector2(h.y, -h.x);

        /// <summary>Fill <paramref name="path"/> with the run; lineIdx is the
        /// start line (the grid stands behind it). False when the deck is not
        /// drivable in this build (PSX_DECKS=0, or it did not solve).</summary>
        public static bool BuildPath(CityMap map, CityDecks.Deck d, TrackPath path, out int lineIdx)
        {
            lineIdx = 0;
            if (!CityDecks.Enabled || map == null || d == null || path == null) return false;
            if (!CityDecks.Solve(map, d) || d.lap == null || d.streetEdge < 0) return false;
            var e = map.edges[d.streetEdge];
            Vector2 nW = d.U * d.EntryN.x + d.V * d.EntryN.y;          // deck -> street
            Vector2 t0 = e.TangentAt(d.streetS).normalized;
            int dir = Vector2.Dot(Right(t0), -nW) > 0f ? 1 : -1;       // the deck on the right
            if (e.oneway) dir = 1;
            float lat = e.oneway ? 0f : LaneM;
            Vector3 Street(float a)
            {
                float s = d.streetS + dir * a, sc = Mathf.Clamp(s, 0f, e.length);
                Vector2 tan = e.TangentAt(sc).normalized;
                Vector2 p = LineModel.LanePoint(e, sc) + tan * (s - sc) + Right(tan * dir) * lat;
                return new Vector3(p.x, e.YAt(sc), p.y);
            }

            var P = new List<Vector3>(1024);
            float lineAt = -1f, flagAt = -1f;
            float Arc() { float a = 0f; for (int i = 1; i < P.Count; i++) a += Plan(P[i - 1], P[i]); return a; }
            for (float a = -LeadInM; a <= -TurnM + 0.01f; a += Step) P.Add(Street(a));
            lineAt = Arc() - LineM;                                    // the line: LineM before the turn-in
            // the lap, without its street ends (kept: > 1 m inside the driveway)
            var w = d.lap;
            int iA = -1, iB = -1;
            for (int i = 0; i < w.Length; i++)
            {
                float into = Vector2.Dot(new Vector2(w[i].x, w[i].z) - d.roadEdge, nW);
                if (into < -1f) { if (iA < 0) iA = i; iB = i; }
            }
            if (iA < 1 || iB >= w.Length - 1 || iB - iA < 20) return false;
            Vector3 hIn = Street(-TurnM) - Street(-TurnM - 1f);
            Turn(P, Street(-TurnM), hIn, w[iA], w[iA + 1] - w[iA]);
            for (int i = iA; i <= iB; i++) P.Add(w[i]);
            Turn(P, w[iB], w[iB] - w[iB - 1], Street(TurnM), Street(TurnM + 1f) - Street(TurnM));
            float outAt = Arc();
            for (float a = TurnM + Step; a <= TurnM + LeadOutM; a += Step) P.Add(Street(a));
            flagAt = outAt + FlagM;

            // even 2.5 m stations along the plan
            var arcs = new float[P.Count];
            for (int i = 1; i < P.Count; i++) arcs[i] = arcs[i - 1] + Plan(P[i - 1], P[i]);
            float total = arcs[P.Count - 1];
            int n = Mathf.Max(2, Mathf.FloorToInt(total / Step) + 1);
            var wps = new Vector3[n];
            int seg = 0;
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Min(i * Step, total);
                while (seg < P.Count - 2 && arcs[seg + 1] < s) seg++;
                float L = arcs[seg + 1] - arcs[seg];
                wps[i] = Vector3.Lerp(P[seg], P[seg + 1], L > 1e-5f ? Mathf.Clamp01((s - arcs[seg]) / L) : 0f);
            }
            // curvature as the lap check reads it: x2.5, a deck is driven at deck speed
            var curv = new float[n];
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 a = wps[i - 1], b = wps[i], c = wps[i + 1]; a.y = b.y = c.y = 0f;
                curv[i] = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad / Step;
            }
            var sm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int k = 0;
                for (int o = -2; o <= 2; o++) { int j = i + o; if (j < 0 || j >= n) continue; sum += curv[j]; k++; }
                sm[i] = sum / k * CurvGain;
            }
            lineIdx = Mathf.Clamp(Mathf.RoundToInt(lineAt / Step), 0, n - 1);
            int finish = Mathf.Clamp(Mathf.RoundToInt(flagAt / Step), lineIdx + 1, n - 4);
            var gates = new List<int>();
            for (int g = lineIdx + Mathf.RoundToInt(GateEveryM / Step); g < finish; g += Mathf.RoundToInt(GateEveryM / Step)) gates.Add(g);

            path.waypoints = wps;
            path.curvatures = sm;
            path.spacing = Step;
            path.roadWidth = 3f;
            path.drag = false;
            path.pointToPoint = true;
            path.finishIndex = finish;
            path.reversed = false;
            path.sprintFinish = -1;
            path.gates = gates.ToArray();
            LastLine = lineIdx;
            Debug.Log("[DeckRun] deck " + d.way + ": " + n + " stations, line " + lineIdx + ", flag " + finish +
                      " (" + ((finish - lineIdx) * Step).ToString("0") + " m), " + gates.Count + " checkpoints, street edge " +
                      d.streetEdge + (e.oneway ? " one-way" : "") + ", deck at " + d.c.ToString("0"));
            return true;
        }

        static float Plan(Vector3 a, Vector3 b) => new Vector2(b.x - a.x, b.z - a.z).magnitude;

        /// <summary>A cubic from a heading to a heading (the turn in off the
        /// street and out onto it), heights eased between the two ends.</summary>
        static void Turn(List<Vector3> L, Vector3 p0, Vector3 h0, Vector3 p3, Vector3 h3)
        {
            Vector2 a = new Vector2(p0.x, p0.z), b = new Vector2(p3.x, p3.z);
            Vector2 ha = new Vector2(h0.x, h0.z).normalized, hb = new Vector2(h3.x, h3.z).normalized;
            float k = Vector2.Distance(a, b) * 0.45f;
            Vector2 c1 = a + ha * k, c2 = b - hb * k;
            int m = Mathf.Max(4, Mathf.CeilToInt(Vector2.Distance(a, b) * 1.4f / 1.25f));
            for (int i = 1; i < m; i++)
            {
                float t = i / (float)m, u = 1f - t;
                var p = u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
                L.Add(new Vector3(p.x, Mathf.Lerp(p0.y, p3.y, t * t * (3f - 2f * t)), p.y));
            }
        }

        // ---- the time trial's best, per deck ---------------------------------
        static string Key(uint way) => "psx_deckrun_best_" + way;

        /// <summary>The best time-trial time on this deck, seconds; 0 = none.</summary>
        public static float Best(uint way)
        {
            try { return PlayerPrefs.GetFloat(Key(way), 0f); } catch { return 0f; }
        }

        /// <summary>Keep <paramref name="seconds"/> if it beats the best.
        /// True when it is a new best.</summary>
        public static bool OfferBest(uint way, float seconds)
        {
            if (seconds <= 1f) return false;
            float had = Best(way);
            if (had > 0f && had <= seconds) return false;
            try { PlayerPrefs.SetFloat(Key(way), seconds); PlayerPrefs.Save(); } catch { return false; }
            return true;
        }

        public static void ClearBest(uint way)
        {
            try { PlayerPrefs.DeleteKey(Key(way)); } catch { }
        }

        public static string Clock(float s) =>
            s <= 0f ? "--:--.-" : ((int)(s / 60f)).ToString() + ":" + (s % 60f).ToString("00.0");
    }
}
