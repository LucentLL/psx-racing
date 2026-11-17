using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LINE GATE (2026-11-16). The owner, on North Tryon Street in PC48:
    /// "I still see a lot of areas around Charlotte where the road and/or road
    /// lines do not make sense, are not accurate, and would never pass DOT
    /// requirements ... these issues cannot get through the quality gate audits."
    ///
    /// WHAT IT READS. The meshes the game draws, never the line model's own
    /// answers: every road tile is built (<see cref="CityMeshes.Build"/>) with
    /// the road tap on (<see cref="CityMeshes.RecordTap"/>), and every ribbon
    /// quad of every edge is read back out of <c>tm.roads</c> - its four
    /// corners and its U. A painted line is found where a quad's U covers a
    /// line column of its slot's texture (<see cref="LineModel.LayoutOf(int)"/>:
    /// the columns the painter put there): a narrow quad lying in the column is
    /// that line; a wide quad carries it at the iso-line U = m / W, bending on
    /// the quad's diagonal as the GPU draws it. The pavement's two edges are
    /// the outermost full-length quads' corners. Pieces are chained along each
    /// edge and through every joint where two ends meet within 3 cm.
    ///
    /// WHAT IT JUDGES (each limit cited at <see cref="LineGateRules"/>):
    ///   SHIFT    a line or pavement edge moves sideways (in its road's frame)
    ///            over a shorter length than the taper the posted speed needs
    ///   SJOG     a line turns one way and back within a short distance (the
    ///            S-jog of the owner's picture), in the driver's world frame
    ///   KINK     an angle point of the alignment (not a taper break) beyond the
    ///            deflection allowed without a curve
    ///   WIDTH    a marked lane under 2.7 m or over 4.6 m outside a taper
    ///   CONT     a line that steps sideways or ends where its road continues
    ///   SURFACE  pavement texels squeezed into a stripe / stretched over a lane,
    ///            a ribbon span drawn twice
    /// plus (LineGate2) JUNCTION, MEDIAN, STEP, BARRIER, BRIDGE.
    ///
    /// The gate is strict: no class is "known, ignore". Its counts are compared
    /// with a committed baseline by tools\city-linegate.ps1; a branch may not
    /// raise any class.
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunLineGate
    ///   PSX_LG_BOX=x0,z0,x1,z1   only the tiles touching this box (metres)
    ///   PSX_LG_NAME=North Tryon Street   only the tiles of roads so named
    ///   PSX_LG_TAG=name          names the run in the report
    ///   PSX_LG_SPOT=x,z[,r]      dump every quad and strand near a point
    ///   PSX_LG_PLANS=n           polylines round the worst n places (default 80)
    /// Writes city_linegate.txt / .csv / .json / _plans.tsv / _roads.tsv.
    /// </summary>
    public static class LineGateRules
    {
        public const float LaneM = 3.6576f;

        /// <summary>The speed a road is judged at: its posted speed (OSM
        /// maxspeed), else North Carolina's statutory or customary speed for
        /// its class (G.S. 20-141: 35 mph inside municipal limits unless
        /// posted; arterials 45, expressways 55, freeways 65), ramps 35 - the
        /// same table the line model designs its tapers with.</summary>
        public static float Mph(CityMap.Edge e) =>
            e.speedKmh > 0 ? e.speedKmh / 1.609344f : e.link ? 35f : ClassMph[Mathf.Clamp(e.cls, 0, 5)];
        static readonly float[] ClassMph = { 25f, 35f, 40f, 45f, 55f, 65f };

        /// <summary>MUTCD (11th ed. 3B.12 lane-reduction transitions and 3B.13
        /// approach markings for obstructions; 2009 ed. 3B.09 / 3B.10; Table
        /// 6C-4): taper length L = W S for 45 mph or more, L = W S^2 / 60 for
        /// 40 mph or less (W the offset in feet, S the posted or 85th
        /// percentile speed in mph, L in feet). As a rate, metres along per
        /// metre across.</summary>
        public static float TaperRate(float mph) => mph >= 42.5f ? mph : mph * mph / 60f;

        /// <summary>AASHTO Green Book (2018) 9.7.2.3, the taper that OPENS an
        /// auxiliary (turn) lane: straight-line rates of 8:1 for design speeds
        /// up to 30 mph and 15:1 at 50 mph. NCDOT Roadway Design Manual Part I
        /// 9-1 uses the same family. Between the two speeds: linear.</summary>
        public static float BayRate(float mph) => Mathf.Lerp(8f, 15f, Mathf.InverseLerp(30f, 50f, mph));

        /// <summary>A freeway ramp terminal's speed-change lane (AASHTO Green Book 2018 10.9.6, from
        /// memory, not re-read: the parallel-type entrance ends in a 300 ft taper and the exit opens with
        /// a 250 ft one for a 12 ft lane, 25:1 and 21:1). MUTCD's L = W S is for through lanes that end
        /// (2009 3B.09); an acceleration lane is not one. The gate takes 25:1, the stricter, for both, on
        /// the pavement edge and edge line of a freeway or expressway (class 4+) or a ramp within
        /// <see cref="TerminalReachM"/> of a node where a ramp meets the mainline.</summary>
        public const float TerminalRate = 25f, TerminalReachM = 350f;

        /// <summary>A measured taper may be 10 % short before it fails (the
        /// measurement's own tolerance: the 1 cm onset threshold).</summary>
        public const float TaperTol = 0.90f;
        /// <summary>A move smaller than this is not a taper (SJOG judges the
        /// small ones).</summary>
        public const float ShiftMinM = 0.30f;

        /// <summary>The deflection a line may take at a point with no curve.
        /// AASHTO Green Book 3.3.13 gives curve LENGTHS for deflections of 5
        /// degrees and under so that no kink shows (500 ft at 5 degrees, 100 ft
        /// more for each degree less); the states put a number on the angle
        /// that needs no curve at all. Read on 2026-11-16: FDOT Plans
        /// Preparation Manual vol. 1 Table 2.8.1a (arterials and collectors
        /// 2 deg 00' at 40 mph or less, 0 deg 45' at 45 mph or more; freeways
        /// 0 deg 45'); TxDOT Roadway Design Manual 4.7.2 (1 degree low speed,
        /// 30 minutes high speed). The gate uses FDOT's, the looser.</summary>
        public static float KinkDeg(float mph) => mph >= 42.5f ? 0.75f : 2f;

        /// <summary>AASHTO Green Book (2018) Table 3-7, the minimum radius by
        /// design speed at e max 4 % (the urban table; 6 % from 65 mph): a
        /// reversal made of two curves this large or larger is an alignment.</summary>
        public static float MinRadiusM(float mph)
        {
            float[] v = { 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70 };
            float[] ft = { 42, 86, 154, 250, 371, 533, 711, 926, 1190, 1500, 1660, 2040 };
            if (mph <= v[0]) return ft[0] * 0.3048f;
            for (int i = 1; i < v.Length; i++) if (mph <= v[i]) return Mathf.Lerp(ft[i - 1], ft[i], (mph - v[i - 1]) / (v[i] - v[i - 1])) * 0.3048f;
            return ft[ft.Length - 1] * 0.3048f;
        }

        /// <summary>A jog smaller than this is not counted (5 cm: twice the
        /// smoothness gate's V, a hand's breadth at 30 m).</summary>
        public const float JogMinM = 0.05f;
        /// <summary>A reversal displacing the line further than this is a pair
        /// of real curves (alignment), not a jog: two lanes.</summary>
        public const float JogMaxM = 7.4f;

        /// <summary>AASHTO Green Book 4.3: lane widths 9 to 12 ft (2.7 - 3.6
        /// m); NCDOT Roadway Design Manual 1-4B the same range. Under 2.7 m no
        /// vehicle lane exists. Over 4.6 m (15 ft) one lane is used as two
        /// (AASHTO Guide for the Development of Bicycle Facilities 4.3: wide
        /// curb lanes of 14 ft, and "widths greater than 15 ft ... may
        /// encourage the undesirable operation of two motor vehicles in one
        /// lane"). A one-lane ramp's travelled way may be 5.5 m (Green Book
        /// Table 3-27, Case I, to 18 ft). A two-way left-turn lane: 3.0 to
        /// 4.9 m (10 - 16 ft, Green Book 4.4 / MUTCD 3B.03).</summary>
        public const float LaneMinM = 2.7f, LaneMaxM = 4.6f, RampLaneMaxM = 5.5f, TwltlMinM = 3.0f, TwltlMaxM = 4.9f;
        /// <summary>A paved shoulder wider than 4.0 m is unexplained width
        /// (Green Book 4.4.2: 12 ft, 3.7 m, the widest usable shoulder; 0.3 m
        /// more for the line and the offset to a rail).</summary>
        public const float ShoulderMaxM = 4.0f;
        /// <summary>Two lines closer than this hold no lane (the AASHTO P design vehicle is 7 ft, 2.13 m, wide):
        /// they are a double line or a painted buffer between lanes (MUTCD 2009 3B.04 double white lines,
        /// 3B.24 buffer and chevron areas), not judged as a narrow lane.</summary>
        public const float BufferMaxM = 1.8f;
        /// <summary>A curb lane (no edge line: the lane runs to the gutter)
        /// may carry a 0.6 m gutter pan (NCDOT 2'-6" curb and gutter).</summary>
        public const float GutterM = 0.6f;
        /// <summary>A local or collector street's curb lane may hold parking
        /// (8 ft, Green Book 4.20).</summary>
        public const float ParkingM = 2.44f;

        /// <summary>Two ends within this are one line (the strips' 2 mm
        /// overlap each side, and float).</summary>
        public const float JoinM = 0.03f;
        /// <summary>A line that continues past a joint more than this to the
        /// side has jumped (MUTCD 3A.06: longitudinal lines are continuous).</summary>
        public const float JumpMaxM = 1.83f;
        /// <summary>Two ends join only when the lines run on into each other within 45 deg (cos): a gore's V tip
        /// is not one line turning back.</summary>
        public const double JoinCos = 0.7071;
        /// <summary>KINK reads an angle point again over chords up to this long (8 m, then 16 m): a
        /// curve's polyline facets read as a curve there, an angle point keeps its deflection.</summary>
        public const int KinkScaleMaxM = 16;
        /// <summary>A polyline facet passes when the polygon lies within this of the curve through its vertices
        /// (the smoothness gate's V, 2.5 cm: 1 px at 6 m): spacing x deflection / 8.</summary>
        public const double FacetDevM = 0.025;
        /// <summary>KINK leaves a junction's corners alone: this close to the drawn end at a node of 1 or 3+
        /// arms (the curb return, the mouth's flare, a bulb). JUNCTION judges the through lines there.</summary>
        public const float KinkCornerM = 15f;
        /// <summary>A junction's mouth strip and its stop line / crosswalk lie within this of the junction, along
        /// the road: a line ending there, or the pavement edge stepping to the curb return, is the junction's.</summary>
        public const float MouthStripM = 25f;
        /// <summary>A kink this close to a gore's painted area is the gore's V (its lines meeting), not a bend.</summary>
        public const double GoreNoseM = 3.0;
        /// <summary>A ramp's gore lies within this of its diverge / merge node: a line's sharp turn (a kink of 10 deg
        /// or more, an S-jog swinging 45 deg or more) there is the gore's V being read, not a bend (all 9 + 6
        /// sampled ones, 2026-11-17).</summary>
        public const float GoreReachM = 60f;
    }

    public static partial class CityAudit
    {
        // ================================================================
        //  What is read out of the meshes
        // ================================================================
        struct LgPiece
        {
            public float sA, sB, latA, latB, w, yA, yB;
            public double ax, az, bx, bz, dx, dz;
            public bool diag, yellow, glyph;
            public byte tk;        // the texture column's kind (LineModel.K*)
        }
        struct LgSpan
        {
            public float sA, sB; public ushort fA, fB;
            public double alx, alz, blx, blz, arx, arz, brx, brz;
            public float yAL, yBL, yAR, yBR, latAL, latBL, latAR, latBR;
            public int tile;
        }
        struct LgGhost { public int edge; public float s0, s1, w, squeeze; public double x, z; }
        /// <summary>One piece of drawn pavement in plan (a ribbon span's outer quad, a gore quad, a fan
        /// triangle, any other triangle of the roads mesh): what lies beside a ribbon's edge.
        /// edge: the ribbon's, -1 another emitter's, -2 a gore, -3 a fan.</summary>
        struct LgPoly { public float x0, z0, x1, z1, x2, z2, x3, z3, y, sA, sB; public int edge; }
        static readonly List<LgPoly> lgPolys = new List<LgPoly>(1 << 18);
        static readonly Dictionary<long, List<int>> lgPolyGrid = new Dictionary<long, List<int>>(1 << 16);
        const float LgPolyCell = 16f;
        static int lgUntappedTris;

        static void LgAddPoly(int edge, double x0, double z0, double x1, double z1, double x2, double z2, double x3, double z3, float y, float sA = 0f, float sB = 0f)
        {
            int id = lgPolys.Count;
            lgPolys.Add(new LgPoly { edge = edge, sA = sA, sB = sB, x0 = (float)x0, z0 = (float)z0, x1 = (float)x1, z1 = (float)z1, x2 = (float)x2, z2 = (float)z2, x3 = (float)x3, z3 = (float)z3, y = y });
            double mnx = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3)), mxx = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
            double mnz = Math.Min(Math.Min(z0, z1), Math.Min(z2, z3)), mxz = Math.Max(Math.Max(z0, z1), Math.Max(z2, z3));
            int cx0 = (int)Math.Floor(mnx / LgPolyCell), cx1 = (int)Math.Floor(mxx / LgPolyCell), cz0 = (int)Math.Floor(mnz / LgPolyCell), cz1 = (int)Math.Floor(mxz / LgPolyCell);
            if (cx1 - cx0 > 40 || cz1 - cz0 > 40) return;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    long k = ((long)cx << 32) ^ (uint)cz;
                    if (!lgPolyGrid.TryGetValue(k, out var l)) lgPolyGrid[k] = l = new List<int>(8);
                    l.Add(id);
                }
        }

        static bool LgInTri(float px, float pz, float ax, float az, float bx, float bz, float cx, float cz)
        {
            float d1 = (px - bx) * (az - bz) - (ax - bx) * (pz - bz);
            float d2 = (px - cx) * (bz - cz) - (bx - cx) * (pz - cz);
            float d3 = (px - ax) * (cz - az) - (cx - ax) * (pz - az);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Is the plan point on drawn pavement of another ribbon or emitter at this level?</summary>
        static bool LgCovered(double x, double z, float y, int notEdge)
        {
            long k = ((long)(int)Math.Floor(x / LgPolyCell) << 32) ^ (uint)(int)Math.Floor(z / LgPolyCell);
            if (!lgPolyGrid.TryGetValue(k, out var l)) return false;
            float px = (float)x, pz = (float)z;
            foreach (int id in l)
            {
                var q = lgPolys[id];
                if (q.edge == notEdge || Mathf.Abs(q.y - y) > 0.6f) continue;
                if (LgInTri(px, pz, q.x0, q.z0, q.x1, q.z1, q.x2, q.z2) || LgInTri(px, pz, q.x0, q.z0, q.x2, q.z2, q.x3, q.z3)) return true;
            }
            return false;
        }
        /// <summary>Is a ramp's diverge or merge (a node where a link meets a road that is not one) within reach of
        /// arc s of edge ei, along the road through its joints? Its gore lies there.</summary>
        static bool LgNearRampNode(CityMap map, int ei, float s, float reach)
        {
            var e0 = map.edges[ei];
            for (int side = 0; side < 2; side++)
            {
                var e = e0; float left = reach - (side == 0 ? s : e0.length - s);
                int node = side == 0 ? e0.a : e0.b;
                for (int hop = 0; hop < 6 && left >= 0f; hop++)
                {
                    var arms = map.nodeEdges[node];
                    bool link = false, road = false;
                    foreach (int o in arms) { if (map.edges[o].link) link = true; else road = true; }
                    if (link && (road || arms.Count >= 3)) return true;
                    if (arms.Count != 2) break;
                    int next = arms[0] == e.index ? arms[1] : arms[0];
                    if (next == e.index) break;
                    e = map.edges[next];
                    left -= e.length;
                    node = e.a == node ? e.b : e.a;
                }
            }
            return false;
        }

        /// <summary>Does another road's ribbon (a span polygon of another edge) cover this point at about this height?</summary>
        static bool LgUnderOther(double x, double z, float y, int ei)
        {
            long k = ((long)(int)Math.Floor(x / LgPolyCell) << 32) ^ (uint)(int)Math.Floor(z / LgPolyCell);
            if (!lgPolyGrid.TryGetValue(k, out var l)) return false;
            float px = (float)x, pz = (float)z;
            foreach (int id in l)
            {
                var q = lgPolys[id];
                if (q.edge < 0 || q.edge == ei || Mathf.Abs(q.y - y) > 0.6f) continue;
                if (LgInTri(px, pz, q.x0, q.z0, q.x1, q.z1, q.x2, q.z2) || LgInTri(px, pz, q.x0, q.z0, q.x2, q.z2, q.x3, q.z3)) return true;
            }
            return false;
        }

        /// <summary>Is a gore's painted area (its polygons, edge -2) within r of a point at about this height?</summary>
        static bool LgNearGore(double x, double z, float y, double r)
        {
            int cx0 = (int)Math.Floor((x - r) / LgPolyCell), cx1 = (int)Math.Floor((x + r) / LgPolyCell), cz0 = (int)Math.Floor((z - r) / LgPolyCell), cz1 = (int)Math.Floor((z + r) / LgPolyCell);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    if (!lgPolyGrid.TryGetValue(((long)cx << 32) ^ (uint)cz, out var l)) continue;
                    foreach (int id in l)
                    {
                        var q = lgPolys[id];
                        if (q.edge != -2 || Mathf.Abs(q.y - y) > 1f) continue;
                        float mnx = Mathf.Min(Mathf.Min(q.x0, q.x1), Mathf.Min(q.x2, q.x3)), mxx = Mathf.Max(Mathf.Max(q.x0, q.x1), Mathf.Max(q.x2, q.x3));
                        float mnz = Mathf.Min(Mathf.Min(q.z0, q.z1), Mathf.Min(q.z2, q.z3)), mxz = Mathf.Max(Mathf.Max(q.z0, q.z1), Mathf.Max(q.z2, q.z3));
                        double dx = Math.Max(0, Math.Max(mnx - x, x - mxx)), dz = Math.Max(0, Math.Max(mnz - z, z - mxz));
                        if (dx * dx + dz * dz <= r * r) return true;
                    }
                }
            return false;
        }
        sealed class LgFan { public int node; public double cx, cz; public float cy; public double[] x, z; public float[] y; public ulong mouths; }

        /// <summary>A strand's point: plan x, z; its own height y; yc, a pavement edge's section-middle
        /// height (the road's profile, free of crossfall and of the edge's own move); arc, lateral, edge.</summary>
        struct LgP { public double x, z; public float y, yc, s, lat; public int e; }
        sealed class LgStrand
        {
            public int id, edge; public byte role;   // 0 pavement edge on the minus side (right of a->b), 1 on the plus side, 2 paint
            public byte tk; public bool yellow, broken; public float pw;
            public readonly List<LgP> p = new List<LgP>(16);
            public int linkA = -1, linkB = -1;      // the strand joined at this one's start / end
            public bool used;
            /// <summary>A channelizing line: solid white, standing where the edge's own layout has no
            /// line (a gore's side, a turning roadway's edge through a median).</summary>
            public bool channel;
            public float S0 => p[0].s; public float S1 => p[p.Count - 1].s;
            public string Kind => role == 0 ? "pavement edge (right)" : role == 1 ? "pavement edge (left)" : role == 3 ? "mark" :
                tk == LgSolidWhite ? (channel ? "channelizing line" : "solid white lane line") : tk == LgSolidYellow ? "solid yellow line" :
                tk == LineModel.KEdgeM ? "edge line (right)" : tk == LineModel.KEdgeP ? (yellow ? "yellow edge line (left)" : "edge line (left)") :
                tk == LineModel.KYellow ? "centre yellow" : tk == LineModel.KYellowDash ? "broken yellow" : "lane line";
            public bool EdgeLine => role == 2 && (tk == LineModel.KEdgeM || tk == LineModel.KEdgeP);
            public bool Boundary => role != 2 || EdgeLine;
        }

        const byte LgSolidWhite = 20, LgSolidYellow = 21;
        static readonly List<LineModel.LineAt> lgLinesAt = new List<LineModel.LineAt>(16);

        /// <summary>After an edge's pieces are chained: a solid line drawn from the texture's edge-line
        /// column but standing further inside the pavement than a shoulder is a lane or channelizing line
        /// (lane-use paint is cut from that column); a short solid piece floating inside its edge is a
        /// mark (a crosswalk bar, a hatch bar), not a line.</summary>
        static void LgClassify(CityMap map)
        {
            foreach (var st in lgStrands)
            {
                if (st.role != 2) continue;
                var e = map.edges[st.edge];
                float a = lgTrims.atA[e.index], b = e.length - lgTrims.atB[e.index];
                float len = st.S1 - st.S0;
                if (!st.broken && Mathf.Abs(len - 3.048f) < 0.12f && st.tk != LineModel.KEdgeP && st.tk != LineModel.KYellow) st.broken = true;
                if (!st.broken && len < 4.5f && st.S0 > a + 0.75f && st.S1 < b - 0.75f) { st.role = 3; lgMarks++; continue; }
                if (st.tk != LineModel.KEdgeM && st.tk != LineModel.KEdgeP) continue;
                float sm = 0.5f * (st.S0 + st.S1);
                float lat = LgLatAt(st, sm);
                LineModel.Extents(e, sm, out float eM, out float eP);
                float pl = LgPaveLat(st.edge, sm, 0), pr = LgPaveLat(st.edge, sm, 1);
                float toEdge = st.tk == LineModel.KEdgeM ? lat - (float.IsNaN(pl) ? -eM : pl) : (float.IsNaN(pr) ? eP : pr) - lat;
                if (toEdge > Mathf.Max(e.shl, e.shr) + 0.5f)
                {
                    st.tk = st.yellow ? LgSolidYellow : LgSolidWhite; lgSolidLane++;
                    if (!st.yellow)
                    {
                        // is it one of the layout's own lines made solid (a turn lane's line), or an extra line?
                        LineModel.LinesAt(e, sm, lgLinesAt);
                        float best = float.MaxValue;
                        foreach (var ln in lgLinesAt) best = Mathf.Min(best, Mathf.Abs(ln.lat - lat));
                        st.channel = best > 0.3f;
                    }
                }
            }
        }

        /// <summary>The drawn pavement edge's lateral on a side (0 minus, 1 plus) at an arc, NaN where it is a seam.</summary>
        static float LgPaveLat(int edge, float s, int side)
        {
            var ids = lgByEdge[edge];
            if (ids == null) return float.NaN;
            foreach (int id in ids)
            {
                var st = lgStrands[id];
                if (st.role != side || st.S0 > s + 0.01f || st.S1 < s - 0.01f) continue;
                return LgLatAt(st, s);
            }
            return float.NaN;
        }

        public sealed class LgFail
        {
            public string cls, sub, what, road, line;
            public double x, z; public int edge, node = -1, tier, place, lines = 1;
            public float s, s1, measure, limit, mph, vis; public string unit;
            public bool model, absorbed;
            /// <summary>What the builder has there (for the fix's worklist): the line model's eases over
            /// the arc, a split node, a clipped branch, a merge zone.</summary>
            public string cause = "";
        }

        /// <summary>The line model's own devices on an edge over [s0, s1] (read-only: for the worklist).</summary>
        static string LgCauseOf(CityMap map, CityMeshes.Trims t, HashSet<int> splitEdges, CityMap.Edge e, float s0, float s1)
        {
            var parts = new List<string>(4);
            var ez = e.lmEase;
            if (ez != null)
                foreach (var z in ez)
                {
                    float a = z.fromA ? -z.d0 : e.length - (z.len - z.d0), b = z.fromA ? z.len - z.d0 : e.length + z.d0;
                    if (z.prof != null) { a = 0f; b = e.length; }
                    if (b < s0 - 1f || a > s1 + 1f) continue;
                    string kind = z.prof != null ? (z.widen ? "refuge-widen" : z.shift ? "refuge-shift" : "refuge-profile") : z.aux ? "aux-lane" : z.relay ? "relay" : z.shift ? "shift" : z.centreOnly ? "centre-only" : z.twoRole != 0 ? "two-sided-taper" : "taper";
                    string d = z.relay ? $"{kind}(span {z.span:0.00} m over {z.len:0} m)" : z.aux ? kind : $"{kind}({z.dw:0.00} m over {z.len:0} m)";
                    if (!parts.Contains(d)) parts.Add(d);
                }
            if (splitEdges.Contains(e.index)) parts.Add("split-end");
            if (t.branchA[e.index] >= 0 || t.branchB[e.index] >= 0) parts.Add("clipped-branch");
            if (LineModel.RefugeOf(e.index) != null) parts.Add("refuge-pair");
            if (e.link) parts.Add("ramp");
            return string.Join(" + ", parts);
        }

        static List<LgPiece>[] lgPieces; static List<LgSpan>[] lgSpans;
        static readonly List<LgGhost> lgGhosts = new List<LgGhost>();
        static readonly List<LgFan> lgFans = new List<LgFan>();
        static readonly List<LgStrand> lgStrands = new List<LgStrand>();
        static List<int>[] lgByEdge;
        static readonly List<LgFail> lgFails = new List<LgFail>();
        static int lgTapNotes, lgQuads, lgPaintQuads, lgGlyphQuads, lgDoubleSpans, lgTiles, lgSeamSpans, lgMarks, lgSolidLane, lgKinkFacets, lgEndsTypeChange, lgEndsEdgeLine, lgCtlEqual, lgKinkCorners, lgStepCorners, lgMouthPassed, lgTerminalPassed, lgJointRetyped, lgGradeFacets, lgSharpJoins, lgEndsAtJunction, lgKinkGore, lgUnderOther;
        static double lgStretchedM, lgStripeM; static int lgStripeQuads, lgChannelChains;
        static CityMap lgMap; static CityMeshes.Trims lgTrims;
        static readonly CultureInfo LgInv = CultureInfo.InvariantCulture;

        static float LgLat(CityMap.Edge e, float s, double x, double z)
        {
            var c = e.PointAt(s); var t = e.TangentAt(s);
            return (float)((x - c.x) * -t.y + (z - c.y) * t.x);
        }

        /// <summary>Read one built tile: every ribbon quad, every fan.</summary>
        static void LgCollect(CityMap map, int tx, int tz, CityMeshes.TileMeshes tm, StringBuilder spotDump, Vector2 spot, float spotR)
        {
            var tap = tm.tap;
            if (tap == null || tm.roads == null || tap.slotBase == null) return;
            var verts = tm.roads.vertices; var uvs = tm.roads.uv; var o = tm.origin;
            int tile = (tx << 16) ^ (tz & 0xFFFF);
            int BaseOf(int slot)
            {
                for (int i = 0; i < tm.roadSlots.Length; i++) if ((int)tm.roadSlots[i] == slot) return tap.slotBase[i];
                return -1;
            }
            var tapped = new bool[verts.Length];
            foreach (var sp in tap.gores)
            {
                int b = BaseOf(sp.slot);
                int nq = Math.Max(1, sp.strips);
                if (b < 0 || b + sp.bucketV + 4 * nq - 1 >= verts.Length) continue;
                for (int k = 0; k < nq; k++)
                {
                    int i = b + sp.bucketV + 4 * k;
                    for (int q = 0; q < 4; q++) tapped[i + q] = true;
                    LgAddPoly(-2, (double)o.x + verts[i].x, (double)o.z + verts[i].z, (double)o.x + verts[i + 1].x, (double)o.z + verts[i + 1].z,
                              (double)o.x + verts[i + 2].x, (double)o.z + verts[i + 2].z, (double)o.x + verts[i + 3].x, (double)o.z + verts[i + 3].z,
                              o.y + 0.25f * (verts[i].y + verts[i + 1].y + verts[i + 2].y + verts[i + 3].y));
                }
            }
            foreach (var sp in tap.spans)
            {
                int b = BaseOf(sp.slot);
                int nq = Math.Max(1, sp.strips);
                if (b < 0 || b + sp.bucketV + 4 * nq - 1 >= verts.Length || sp.edge < 0 || sp.edge >= map.edges.Length) { lgTapNotes++; continue; }
                for (int q = 0; q < 4 * nq; q++) tapped[b + sp.bucketV + q] = true;
                int profile = (sp.slot - (int)CityMeshes.Slot.RoadFirst) / CityMeshes.SurfaceCount;
                if (profile < 0 || profile >= RoadProfiles.ProfileCount)
                {
                    // a span on the junction slab: pavement, no paint
                    int i = b + sp.bucketV, il = i + 4 * (nq - 1);
                    LgAddPoly(sp.edge, (double)o.x + verts[i].x, (double)o.z + verts[i].z, (double)o.x + verts[i + 1].x, (double)o.z + verts[i + 1].z,
                              (double)o.x + verts[il + 2].x, (double)o.z + verts[il + 2].z, (double)o.x + verts[il + 3].x, (double)o.z + verts[il + 3].z,
                              o.y + 0.5f * (verts[i].y + verts[il + 2].y), sp.sA, sp.sB);
                    continue;
                }
                var e = map.edges[sp.edge];
                var tex = LineModel.LayoutOf(profile);
                float tw = tex.texW;
                int i0 = b + sp.bucketV;
                // the full-length quad: its V at the two sections
                float vA0 = uvs[i0].y, vB0 = uvs[i0 + 1].y;
                for (int k = 1; k < nq; k++)
                {
                    int i = i0 + 4 * k;
                    if (Mathf.Abs(uvs[i + 1].y - uvs[i].y) > Mathf.Abs(vB0 - vA0) + 1e-6f) { vA0 = uvs[i].y; vB0 = uvs[i + 1].y; }
                }
                float vSpan = vB0 - vA0;
                var span = new LgSpan { sA = sp.sA, sB = sp.sB, fA = sp.flagsA, fB = sp.flagsB, tile = tile, latAL = float.MaxValue, latAR = float.MinValue, latBL = float.MaxValue, latBR = float.MinValue };
                bool any = false;
                bool dump = spotDump != null && (new Vector2(o.x + verts[i0].x, o.z + verts[i0].z) - spot).sqrMagnitude < spotR * spotR;
                for (int k = 0; k < nq; k++)
                {
                    int i = i0 + 4 * k;
                    Vector3 AL = verts[i], BL = verts[i + 1], BR = verts[i + 2], AR = verts[i + 3];
                    float uL = uvs[i].x, uR = uvs[i + 3].x, vA = uvs[i].y, vB = uvs[i + 1].y;
                    float tA = Mathf.Abs(vSpan) > 1e-7f ? Mathf.Clamp01((vA - vA0) / vSpan) : 0f, tB = Mathf.Abs(vSpan) > 1e-7f ? Mathf.Clamp01((vB - vA0) / vSpan) : 1f;
                    float sA = Mathf.Lerp(sp.sA, sp.sB, tA), sB = Mathf.Lerp(sp.sA, sp.sB, tB);
                    bool fullLen = tA < 1e-4f && tB > 1f - 1e-4f;
                    lgQuads++;
                    double alx = (double)o.x + AL.x, alz = (double)o.z + AL.z, blx = (double)o.x + BL.x, blz = (double)o.z + BL.z;
                    double brx = (double)o.x + BR.x, brz = (double)o.z + BR.z, arx = (double)o.x + AR.x, arz = (double)o.z + AR.z;
                    float wA = (float)Math.Sqrt((arx - alx) * (arx - alx) + (arz - alz) * (arz - alz)), wB = (float)Math.Sqrt((brx - blx) * (brx - blx) + (brz - blz) * (brz - blz));
                    float wq = Mathf.Max(wA, wB);
                    if (fullLen)
                    {
                        float lAL = LgLat(e, sp.sA, alx, alz), lAR = LgLat(e, sp.sA, arx, arz), lBL = LgLat(e, sp.sB, blx, blz), lBR = LgLat(e, sp.sB, brx, brz);
                        if (lAL < span.latAL) { span.latAL = lAL; span.alx = alx; span.alz = alz; span.yAL = o.y + AL.y; }
                        if (lBL < span.latBL) { span.latBL = lBL; span.blx = blx; span.blz = blz; span.yBL = o.y + BL.y; }
                        if (lAR > span.latAR) { span.latAR = lAR; span.arx = arx; span.arz = arz; span.yAR = o.y + AR.y; }
                        if (lBR > span.latBR) { span.latBR = lBR; span.brx = brx; span.brz = brz; span.yBR = o.y + BR.y; }
                        any = true;
                    }
                    float lo = Mathf.Min(uL, uR), hi = Mathf.Max(uL, uR), du = hi - lo;
                    bool painted = false;
                    for (int j = 0; j < tex.m.Length; j++)
                    {
                        float aj = tex.x0[j] / tw, bj = (tex.x1[j] + 1) / tw, cj = tex.m[j] / tex.W;
                        bool narrow = du <= 2f * (bj - aj) + 1e-4f;
                        float mid = 0.5f * (lo + hi);
                        var pc = new LgPiece { sA = sA, sB = sB, tk = tex.kind[j], yellow = tex.kind[j] == LineModel.KYellow || tex.kind[j] == LineModel.KYellowDash || (tex.kind[j] == LineModel.KEdgeP && !tex.twoWay) };
                        if (narrow)
                        {
                            if (mid < aj - 1e-4f || mid > bj + 1e-4f) continue;
                            pc.ax = 0.5 * (alx + arx); pc.az = 0.5 * (alz + arz); pc.bx = 0.5 * (blx + brx); pc.bz = 0.5 * (blz + brz);
                            pc.yA = o.y + 0.5f * (AL.y + AR.y); pc.yB = o.y + 0.5f * (BL.y + BR.y);
                            pc.w = wq; pc.glyph = wq > 0.5f || wA < 0.02f || wB < 0.02f;
                        }
                        else
                        {
                            if (cj <= lo + 1e-5f || cj >= hi - 1e-5f) continue;
                            double f = (cj - uL) / (double)(uR - uL);
                            pc.ax = alx + (arx - alx) * f; pc.az = alz + (arz - alz) * f; pc.bx = blx + (brx - blx) * f; pc.bz = blz + (brz - blz) * f;
                            pc.dx = alx + (brx - alx) * f; pc.dz = alz + (brz - alz) * f; pc.diag = true;
                            pc.yA = o.y + Mathf.Lerp(AL.y, AR.y, (float)f); pc.yB = o.y + Mathf.Lerp(BL.y, BR.y, (float)f);
                            pc.w = (bj - aj) * tex.W * (du > 1e-6f ? wq / (du * tex.W) : 1f);
                        }
                        pc.latA = LgLat(e, sA, pc.ax, pc.az); pc.latB = LgLat(e, sB, pc.bx, pc.bz);
                        painted = true;
                        if (pc.glyph) lgGlyphQuads++; else lgPaintQuads++;
                        (lgPieces[sp.edge] ??= new List<LgPiece>(32)).Add(pc);
                        if (narrow) break;
                    }
                    // pavement texels squeezed into a stripe, or stretched over a lane
                    float texW = du * tex.W;
                    float squeeze = texW > 1e-4f ? wq / texW : 1f;
                    if (!painted && wq > 0.03f && sB - sA > 0.05f)
                    {
                        if (wq < 0.5f && squeeze < 0.34f) { lgStripeQuads++; lgStripeM += sB - sA; }
                        else if (wq >= 1.5f && (squeeze > 1.25f || squeeze < 0.8f))
                            lgGhosts.Add(new LgGhost { edge = sp.edge, s0 = sA, s1 = sB, w = wq, squeeze = squeeze, x = 0.25 * (alx + arx + blx + brx), z = 0.25 * (alz + arz + blz + brz) });
                    }
                    if (dump)
                        spotDump.AppendLine(string.Format(LgInv, "quad\t{0}\t{1:0.00}\t{2:0.00}\t{3}\t{4:0.000}\t{5:0.000}\t{6:0.0000}\t{7:0.0000}\t{8}\t{9:0.00}\t{10:0.00}\t{11:0.00}\t{12:0.00}\t{13:x}\t{14:x}",
                            sp.edge, sA, sB, k, LgLat(e, sA, alx, alz), LgLat(e, sA, arx, arz), uL, uR, painted ? "paint" : "pave", wA, wB, squeeze, 0.5 * (alx + arx), sp.flagsA, sp.flagsB));
                }
                if (any)
                {
                    (lgSpans[sp.edge] ??= new List<LgSpan>(16)).Add(span);
                    LgAddPoly(sp.edge, span.alx, span.alz, span.blx, span.blz, span.brx, span.brz, span.arx, span.arz, 0.25f * (span.yAL + span.yBL + span.yBR + span.yAR), sp.sA, sp.sB);
                }
                else
                {
                    int i = b + sp.bucketV, il = i + 4 * (nq - 1);
                    LgAddPoly(sp.edge, (double)o.x + verts[i].x, (double)o.z + verts[i].z, (double)o.x + verts[i + 1].x, (double)o.z + verts[i + 1].z,
                              (double)o.x + verts[il + 2].x, (double)o.z + verts[il + 2].z, (double)o.x + verts[il + 3].x, (double)o.z + verts[il + 3].z,
                              o.y + 0.5f * (verts[i].y + verts[il + 2].y), sp.sA, sp.sB);
                }
            }
            foreach (var fa in tap.fans)
            {
                int b = BaseOf(fa.slot);
                if (b < 0 || b + fa.bucketV + fa.count - 1 >= verts.Length) { lgTapNotes++; continue; }
                int i = b + fa.bucketV;
                var f = new LgFan { node = fa.node, cx = (double)o.x + verts[i].x, cz = (double)o.z + verts[i].z, cy = o.y + verts[i].y, mouths = fa.mouths,
                                    x = new double[fa.count - 1], z = new double[fa.count - 1], y = new float[fa.count - 1] };
                for (int k = 1; k < fa.count; k++) { f.x[k - 1] = (double)o.x + verts[i + k].x; f.z[k - 1] = (double)o.z + verts[i + k].z; f.y[k - 1] = o.y + verts[i + k].y; }
                for (int k = 0; k < fa.count; k++) tapped[i + k] = true;
                lgFans.Add(f);
            }
            // every triangle the tap does not name (fans' own triangles, pockets, merge patches, anything else in the roads mesh)
            for (int sm = 0; sm < tm.roadSlots.Length; sm++)
            {
                // the driving surfaces only: the roads mesh also carries the structures' concrete (parapets, fascias)
                int slot = (int)tm.roadSlots[sm];
                if (slot < (int)CityMeshes.Slot.RoadFirst || slot >= (int)CityMeshes.Slot.Sidewalk) continue;
                var tris = tm.roads.GetTriangles(sm);
                for (int q = 0; q + 2 < tris.Length; q += 3)
                {
                    int a = tris[q], b2 = tris[q + 1], c = tris[q + 2];
                    bool fan = tapped[a] && tapped[b2] && tapped[c];
                    // a tapped quad's own two triangles are its span's polygon already; a fan's are added here
                    if (fan && !LgIsFanVert(tap, tm, a)) continue;
                    // a level triangle only: a face standing up (a slab's side) is not pavement
                    float ux = verts[b2].x - verts[a].x, uy = verts[b2].y - verts[a].y, uz = verts[b2].z - verts[a].z, vx = verts[c].x - verts[a].x, vy = verts[c].y - verts[a].y, vz = verts[c].z - verts[a].z;
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (ny * ny < 0.5f * (nx * nx + ny * ny + nz * nz)) continue;
                    if (!fan) lgUntappedTris++;
                    LgAddPoly(fan ? -3 : -1, (double)o.x + verts[a].x, (double)o.z + verts[a].z, (double)o.x + verts[b2].x, (double)o.z + verts[b2].z,
                              (double)o.x + verts[c].x, (double)o.z + verts[c].z, (double)o.x + verts[c].x, (double)o.z + verts[c].z, o.y + (verts[a].y + verts[b2].y + verts[c].y) / 3f);
                }
            }
        }

        static int[] lgFanLo, lgFanHi; static CityMeshes.RoadTap lgFanTap;
        /// <summary>Is roads-mesh vertex v one of a tapped fan's (not a ribbon quad's)?</summary>
        static bool LgIsFanVert(CityMeshes.RoadTap tap, CityMeshes.TileMeshes tm, int v)
        {
            if (lgFanTap != tap)
            {
                lgFanTap = tap;
                lgFanLo = new int[tap.fans.Count]; lgFanHi = new int[tap.fans.Count];
                for (int f = 0; f < tap.fans.Count; f++)
                {
                    int b = -1;
                    for (int i = 0; i < tm.roadSlots.Length; i++) if ((int)tm.roadSlots[i] == tap.fans[f].slot) b = tap.slotBase[i];
                    lgFanLo[f] = b < 0 ? -1 : b + tap.fans[f].bucketV; lgFanHi[f] = b < 0 ? -1 : b + tap.fans[f].bucketV + tap.fans[f].count;
                }
            }
            for (int f = 0; f < lgFanLo.Length; f++) if (v >= lgFanLo[f] && v < lgFanHi[f]) return true;
            return false;
        }

        // ================================================================
        //  Chaining: pieces into strands, strands through joints
        // ================================================================
        static LgStrand LgNew(int edge, byte role, byte tk, bool yellow, float pw)
        {
            var st = new LgStrand { id = lgStrands.Count, edge = edge, role = role, tk = tk, yellow = yellow, pw = pw };
            lgStrands.Add(st);
            (lgByEdge[edge] ??= new List<int>(8)).Add(st.id);
            return st;
        }

        static void LgChainEdges(CityMap map)
        {
            var open = new List<LgStrand>(16);
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var e = map.edges[ei];
                // ---- the pavement's two edges ----
                var spans = lgSpans[ei];
                if (spans != null)
                {
                    spans.Sort((a, b) => a.sA != b.sA ? a.sA.CompareTo(b.sA) : a.sB.CompareTo(b.sB));
                    // a span drawn twice (two tiles, or twice in one)
                    for (int i = spans.Count - 1; i > 0; i--)
                        if (Mathf.Abs(spans[i].sA - spans[i - 1].sA) < 1e-3f && Mathf.Abs(spans[i].sB - spans[i - 1].sB) < 1e-3f)
                        {
                            lgDoubleSpans++;
                            var d = spans[i];
                            lgFails.Add(new LgFail { cls = "SURFACE", sub = "span drawn twice", edge = ei, s = d.sA, s1 = d.sB, x = 0.5 * (d.alx + d.arx), z = 0.5 * (d.alz + d.arz), measure = d.sB - d.sA, limit = 0f, unit = "m", what = "the same ribbon span is in the mesh twice (z-fight)" });
                            spans.RemoveAt(i);
                        }
                    for (int side = 0; side < 2; side++)
                    {
                        LgStrand st = null; ushort clipBit = side == 0 ? CityMeshes.RoadTap.FClipL : CityMeshes.RoadTap.FClipR, sqBit = side == 0 ? CityMeshes.RoadTap.FSqueezedL : CityMeshes.RoadTap.FSqueezedR;
                        ushort snapBit = side == 0 ? CityMeshes.RoadTap.FSnapL : CityMeshes.RoadTap.FSnapR;
                        ushort virt = (ushort)(clipBit | sqBit | snapBit | CityMeshes.RoadTap.FCollapsed);
                        foreach (var sp in spans)
                        {
                            // an edge that is another ribbon's (a clip, a squeeze, a collapsed nose) is not a pavement edge
                            if (((sp.fA | sp.fB) & virt) != 0) { st = null; continue; }
                            double ax = side == 0 ? sp.alx : sp.arx, az = side == 0 ? sp.alz : sp.arz, bx = side == 0 ? sp.blx : sp.brx, bz = side == 0 ? sp.blz : sp.brz;
                            float ya = side == 0 ? sp.yAL : sp.yAR, yb = side == 0 ? sp.yBL : sp.yBR, la = side == 0 ? sp.latAL : sp.latAR, lb = side == 0 ? sp.latBL : sp.latBR;
                            // other pavement 0.35 m outside this edge (a ramp's ribbon, a pocket, a twin deck): a seam inside the road
                            {
                                double oax = side == 0 ? sp.arx : sp.alx, oaz = side == 0 ? sp.arz : sp.alz, obx = side == 0 ? sp.brx : sp.blx, obz = side == 0 ? sp.brz : sp.blz;
                                double ux = ax - oax, uz = az - oaz, ul = Math.Sqrt(ux * ux + uz * uz), vx = bx - obx, vz = bz - obz, vl = Math.Sqrt(vx * vx + vz * vz);
                                if (ul < 0.05 || vl < 0.05) { st = null; continue; }
                                int seam = 0;
                                for (int q = 0; q < 3; q++)
                                {
                                    double f = q == 0 ? 0.15 : q == 1 ? 0.5 : 0.85;
                                    double qx = ax + (bx - ax) * f, qz = az + (bz - az) * f, nx = ux / ul + (vx / vl - ux / ul) * f, nz = uz / ul + (vz / vl - uz / ul) * f;
                                    if (LgCovered(qx + nx * 0.35, qz + nz * 0.35, (float)(ya + (yb - ya) * f), ei)) seam++;
                                }
                                if (seam >= 2) { lgSeamSpans++; st = null; continue; }
                            }
                            if (st != null)
                            {
                                var t = st.p[st.p.Count - 1];
                                double d = Math.Sqrt((t.x - ax) * (t.x - ax) + (t.z - az) * (t.z - az));
                                if (Mathf.Abs(t.s - sp.sA) > 0.02f || d > LineGateRules.JoinM)
                                {
                                    // (the mouth strip at a junction meets its curb return there: the fan's pavement
                                    // covers the step - seen on every sampled one; judged only away from junctions)
                                    int jn = LgEndNode(e, sp.sA, LineGateRules.KinkCornerM);
                                    // (and when the junction is a joint or two further on: the second sample set had
                                    // its remaining steps all at crosswalks of a junction 15-25 m on)
                                    bool corner = (jn >= 0 && map.nodeEdges[jn].Count != 2) || LgJunctionWithin(map, ei, sp.sA, LineGateRules.MouthStripM);
                                    if (corner && Mathf.Abs(t.s - sp.sA) <= 0.02f && d <= LineGateRules.JumpMaxM) lgStepCorners++;
                                    else if (Mathf.Abs(t.s - sp.sA) <= 0.02f && d <= LineGateRules.JumpMaxM)
                                        lgFails.Add(new LgFail { cls = "CONT", sub = "pavement edge steps", edge = ei, s = sp.sA, s1 = sp.sA, x = ax, z = az, measure = (float)d, limit = LineGateRules.JoinM, unit = "m", line = st.Kind,
                                                                 what = $"the pavement edge steps {d:0.00} m sideways between two spans of one edge" });
                                    st = null;
                                }
                            }
                            // (the height kept is the section's MIDDLE: the road's profile, free of crossfall and of the edge's own move)
                            float ymA = 0.5f * (sp.yAL + sp.yAR), ymB = 0.5f * (sp.yBL + sp.yBR);
                            if (st == null) { st = LgNew(ei, (byte)side, 0, false, 0f); st.p.Add(new LgP { x = ax, z = az, y = ya, yc = ymA, s = sp.sA, lat = la, e = ei }); }
                            st.p.Add(new LgP { x = bx, z = bz, y = yb, yc = ymB, s = sp.sB, lat = lb, e = ei });
                        }
                    }
                }
                // ---- the painted lines ----
                var pcs = lgPieces[ei];
                if (pcs == null) continue;
                pcs.Sort((a, b) => a.sA != b.sA ? a.sA.CompareTo(b.sA) : a.latA.CompareTo(b.latA));
                open.Clear();
                foreach (var pc in pcs)
                {
                    if (pc.glyph) continue;
                    LgStrand best = null, bestGap = null; double bd = LineGateRules.JoinM, bg = double.MaxValue;
                    for (int k = open.Count - 1; k >= 0; k--)
                    {
                        var st = open[k]; var t = st.p[st.p.Count - 1];
                        if (t.s < pc.sA - 13f) { open.RemoveAt(k); continue; }
                        if (st.yellow != pc.yellow) continue;
                        if (Mathf.Abs(t.s - pc.sA) <= 0.02f)
                        {
                            double d = Math.Sqrt((t.x - pc.ax) * (t.x - pc.ax) + (t.z - pc.az) * (t.z - pc.az));
                            if (d <= bd) { best = st; bd = d; }
                        }
                        else if (t.s < pc.sA)
                        {
                            // a broken line's next dash: on the line's own lateral, a dash gap on
                            float miss = Mathf.Abs(t.lat - pc.latA);
                            if (miss <= 0.10f + 0.01f * (pc.sA - t.s) && miss < bg) { bestGap = st; bg = miss; }
                        }
                    }
                    bool gap = best == null && bestGap != null;
                    if (gap) best = bestGap;
                    if (best == null) { best = LgNew(ei, 2, pc.tk, pc.yellow, pc.w); best.p.Add(new LgP { x = pc.ax, z = pc.az, y = pc.yA, s = pc.sA, lat = pc.latA, e = ei }); open.Add(best); }
                    else if (gap) { best.broken = true; best.p.Add(new LgP { x = pc.ax, z = pc.az, y = pc.yA, s = pc.sA, lat = pc.latA, e = ei }); }
                    if (pc.diag) best.p.Add(new LgP { x = pc.dx, z = pc.dz, y = 0.5f * (pc.yA + pc.yB), s = 0.5f * (pc.sA + pc.sB), lat = LgLat(e, 0.5f * (pc.sA + pc.sB), pc.dx, pc.dz), e = ei });
                    best.p.Add(new LgP { x = pc.bx, z = pc.bz, y = pc.yB, s = pc.sB, lat = pc.latB, e = ei });
                    if (pc.tk == LineModel.KWhiteDash || pc.tk == LineModel.KYellowDash) best.broken = true;
                    best.pw = Mathf.Max(best.pw, pc.w);
                }
            }
        }

        static long LgCell(double x, double z) => ((long)Math.Floor(x * 4.0) << 32) ^ (uint)(int)Math.Floor(z * 4.0);

        /// <summary>Join strands of different edges whose ends meet (a mitred
        /// joint; a tile's seam inside an edge never splits one: an edge's
        /// pieces are chained across tiles above). A broken line cut from a
        /// solid column ends a dash gap short of the joint: it joins the next
        /// edge's first dash where that lies on its own straight.</summary>
        static void LgLinkJoints(CityMap map, CityMeshes.Trims t)
        {
            var grid = new Dictionary<long, List<(int id, bool end)>>(lgStrands.Count * 2);
            foreach (var st in lgStrands)
                for (int q = 0; q < 2; q++)
                {
                    var p = q == 0 ? st.p[0] : st.p[st.p.Count - 1];
                    long c = LgCell(p.x, p.z);
                    if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<(int, bool)>(2);
                    l.Add((st.id, q == 1));
                }
            float Slack(LgStrand st) => st.role == 2 && st.broken ? 9.6f : 0.75f;
            // the nearest open end of another edge's strand at the same node; dash: measured off this end's own straight
            (int id, bool end, double d) Nearest(LgStrand st, bool end, double reach, int onlyEdge, bool dash)
            {
                var p = end ? st.p[st.p.Count - 1] : st.p[0];
                int bi = -1; bool be = false; double bd = reach;
                var ea = map.edges[st.edge];
                int na = LgEndNode(ea, p.s, Slack(st));
                if (na < 0) return (-1, false, 0);
                // this end's direction (outward)
                var p2 = end ? st.p[Math.Max(0, st.p.Count - 2)] : st.p[Math.Min(st.p.Count - 1, 1)];
                double ox = p.x - p2.x, oz = p.z - p2.z, ol = Math.Sqrt(ox * ox + oz * oz);
                if (ol > 1e-6) { ox /= ol; oz /= ol; }
                double search = dash ? 10.5 : reach;
                int cx = (int)Math.Floor(p.x * 4.0), cz = (int)Math.Floor(p.z * 4.0); int r = (int)Math.Ceiling(search * 4.0);
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l)) continue;
                        foreach (var (id, oe) in l)
                        {
                            var o = lgStrands[id];
                            if (o.edge == st.edge || (onlyEdge >= 0 && o.edge != onlyEdge)) continue;
                            if (o.role == 3 || (o.role == 2) != (st.role == 2)) continue;
                            if (o.role == 2 && (o.yellow != st.yellow || o.channel != st.channel)) continue;
                            if ((oe ? o.linkB : o.linkA) >= 0) continue;
                            var eb = map.edges[o.edge];
                            if (LgEndNode(eb, oe ? o.S1 : o.S0, Slack(o)) != na) continue;
                            var q = oe ? o.p[o.p.Count - 1] : o.p[0];
                            double vx = q.x - p.x, vz = q.z - p.z;
                            double d = Math.Sqrt(vx * vx + vz * vz);
                            // the two must run on into each other: a line's end does not join one that leaves at a
                            // sharp angle (a gore's V: the edge line met at the tip by the gore's own line, which
                            // read as an 85 deg S-jog and a 47 deg kink on every sampled one) - within 45 deg
                            {
                                var q2 = oe ? o.p[Math.Max(0, o.p.Count - 2)] : o.p[Math.Min(o.p.Count - 1, 1)];
                                double qx = q.x - q2.x, qz = q.z - q2.z, ql = Math.Sqrt(qx * qx + qz * qz);
                                if (ol > 0.2 && ql > 0.2 && (ox * qx + oz * qz) / ql > -LineGateRules.JoinCos) { lgSharpJoins++; continue; }
                            }
                            if (dash)
                            {
                                if (d > 10.5 || vx * ox + vz * oz < -0.5) continue;
                                d = Math.Abs(vx * oz - vz * ox);                  // off this end's straight
                            }
                            if (d < bd) { bd = d; bi = id; be = oe; }
                        }
                    }
                return (bi, be, bd);
            }
            void Link(LgStrand st, bool end, int id, bool oe)
            {
                var o = lgStrands[id];
                if (end) st.linkB = id; else st.linkA = id;
                if (oe) o.linkB = st.id; else o.linkA = st.id;
            }
            foreach (var st in lgStrands)
                for (int q = 0; q < 2; q++)
                {
                    bool end = q == 1;
                    if (st.role == 3 || (end ? st.linkB : st.linkA) >= 0) continue;
                    var (id, oe, d) = Nearest(st, end, LineGateRules.JoinM, -1, false);
                    if (id >= 0) Link(st, end, id, oe);
                }
            // broken lines across the joint's dash gap
            foreach (var st in lgStrands)
            {
                if (st.role != 2 || !st.broken) continue;
                for (int q = 0; q < 2; q++)
                {
                    bool end = q == 1;
                    if ((end ? st.linkB : st.linkA) >= 0) continue;
                    var (id, oe, d) = Nearest(st, end, 0.15, -1, true);
                    if (id >= 0 && lgStrands[id].broken) Link(st, end, id, oe);
                }
            }
            // what is left open at a joint the road runs on through: a step sideways, or an end
            foreach (var st in lgStrands)
                for (int q = 0; q < 2; q++)
                {
                    bool end = q == 1;
                    if (st.role == 3 || (end ? st.linkB : st.linkA) >= 0) continue;
                    var p = end ? st.p[st.p.Count - 1] : st.p[0];
                    var e = map.edges[st.edge];
                    int node = LgEndNode(e, p.s, Slack(st));
                    if (node < 0) continue;                                       // an end inside the edge: LgEnds judges it
                    if (!t.mitre[node]) continue;                                  // a fan: every line ends at its mouth
                    int other = t.throughA[node] == e.index ? t.throughB[node] : t.throughB[node] == e.index ? t.throughA[node] : -1;
                    if (other < 0 || other == e.index) continue;                   // a clipped branch ends on its host
                    bool dash = st.role == 2 && st.broken;
                    var (id, oe, d) = Nearest(st, end, LineGateRules.JumpMaxM, other, dash);
                    if (id >= 0)
                    {
                        // within the join distance but not joined: the other end is taken (a line forking
                        // at the joint) or the pattern changes there (a broken line meeting a solid one) - no step
                        // a double line meeting a single one (a centre double yellow going on as a split's yellow
                        // edge line, one of its two lines): up to 0.25 m apart, different patterns - no step
                        var ost = lgStrands[id];
                        if (st.role == 2 && ost.tk != st.tk && d <= 0.25) { if (st.id < id) lgJointRetyped++; }
                        else if (st.id < id && d > (dash ? 0.15 : LineGateRules.JoinM))
                            lgFails.Add(new LgFail { cls = "CONT", sub = st.role == 2 ? "line steps at a joint" : "pavement edge steps at a joint", edge = st.edge, node = node, s = p.s, s1 = p.s, x = p.x, z = p.z, measure = (float)d, limit = dash ? 0.15f : LineGateRules.JoinM, unit = "m", line = st.Kind,
                                                     what = $"the {st.Kind} steps {d:0.00} m sideways where e{st.edge} meets e{other}" });
                    }
                    else if (st.role == 2 && !st.EdgeLine && !st.channel && !LgWidthChanging(st.edge, p.s) && !LgWidthChanging(other, map.edges[other].a == node ? 1f : map.edges[other].length - 1f))
                    {
                        // the same line going on in another pattern on the next edge (a solid lane line broken again
                        // past the joint, a gore's wide line becoming the dotted lane line): its first piece within
                        // a dash cycle (10.5 m) on this end's straight, 0.3 m - MUTCD 2009 3B.04, 3A.06
                        var (cid, _, _) = Nearest(st, end, 0.3, other, true);
                        if (cid >= 0) { lgJointRetyped++; continue; }
                        // (an edge line may end where a curb takes over; a lane's line may end where its lane tapers)
                        lgFails.Add(new LgFail { cls = "CONT", sub = "line ends at a joint", edge = st.edge, node = node, s = p.s, s1 = p.s, x = p.x, z = p.z, measure = 0f, limit = 0f, unit = "", line = st.Kind,
                                                 what = $"the {st.Kind} of e{st.edge} ends at the joint with e{other} (lanes {e.lanes} / {map.edges[other].lanes}) with the pavement's width unchanged; nothing continues it within {LineGateRules.JumpMaxM:0.0} m" });
                    }
                }
        }

        /// <summary>Is the pavement's width changing (0.9 m or more) within 25 m of an arc?</summary>
        static bool LgWidthChanging(int edge, float s)
        {
            float len = lgMap.edges[edge].length;
            float w0 = LgPaveWidth(edge, Mathf.Clamp(s, 0f, len));
            if (w0 < 0f) return true;                       // no clean pavement edge here (a clip, a squeeze): not judged
            float wb = LgPaveWidth(edge, s - 25f), wf = LgPaveWidth(edge, s + 25f);
            return (wb > 0f && Mathf.Abs(w0 - wb) >= 0.9f) || (wf > 0f && Mathf.Abs(w0 - wf) >= 0.9f);
        }

        /// <summary>The node an arc lies at the drawn end of (-1: inside the edge).</summary>
        static int LgEndNode(CityMap.Edge e, float s, float slack = 0.75f)
        {
            float a = lgTrims.atA[e.index], b = e.length - lgTrims.atB[e.index];
            if (s <= a + slack) return e.a;
            if (s >= b - slack) return e.b;
            return -1;
        }

        /// <summary>The chain a strand belongs to, from its far start to its
        /// far end, as one polyline (each strand once).</summary>
        static List<LgP> LgChainOf(LgStrand st0, List<LgStrand> members, out bool yellow)
        {
            // walk back to the start
            var cur = st0; bool fwd = true; int guard = 0;
            var seen = new HashSet<int> { cur.id };
            while (guard++ < 100000)
            {
                int nx = fwd ? cur.linkA : cur.linkB;
                if (nx < 0 || seen.Contains(nx)) break;
                var o = lgStrands[nx];
                // we arrive at o through the end that links back to cur
                bool viaEnd = o.linkB == cur.id;
                seen.Add(nx); cur = o; fwd = viaEnd;      // continue out of o's other end: if we came in at its end, leave by its start
            }
            // cur is the chain's first strand; we were about to leave it by its start (fwd) or its end:
            // the chain begins at that end, so cur is walked start to end when fwd
            var pts = new List<LgP>(256);
            members.Clear(); yellow = st0.yellow;
            bool forward = fwd;
            var prev = -1; guard = 0;
            while (cur != null && guard++ < 100000)
            {
                cur.used = true; members.Add(cur);
                int n = cur.p.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = cur.p[forward ? i : n - 1 - i];
                    if (!forward) p.lat = -p.lat;
                    if (pts.Count > 0) { var l = pts[pts.Count - 1]; if ((l.x - p.x) * (l.x - p.x) + (l.z - p.z) * (l.z - p.z) < 1e-8) continue; }
                    pts.Add(p);
                }
                int nx = forward ? cur.linkB : cur.linkA;
                if (nx < 0 || nx == prev || lgStrands[nx].used) break;
                var o = lgStrands[nx];
                bool viaStart = o.linkA == cur.id;
                prev = cur.id; cur = o; forward = viaStart;
            }
            return pts;
        }

        // ================================================================
        //  Shape: KINK, SJOG (the world frame), SHIFT (the road's frame)
        // ================================================================
        struct LgR { public double x, z; public float lat, s; public int e; }

        static void LgResample(List<LgP> pts, List<LgR> into)
        {
            into.Clear();
            if (pts.Count < 2) return;
            double acc = 0, next = 0;
            into.Add(new LgR { x = pts[0].x, z = pts[0].z, lat = pts[0].lat, s = pts[0].s, e = pts[0].e });
            next = 1;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                double d = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                if (d < 1e-9) continue;
                // a lateral frame flips where the chain passes to an edge running the other way: no blend across
                bool same = a.e == b.e;
                while (acc + d >= next)
                {
                    double f = (next - acc) / d;
                    into.Add(new LgR { x = a.x + (b.x - a.x) * f, z = a.z + (b.z - a.z) * f, lat = same ? (float)(a.lat + (b.lat - a.lat) * f) : (f < 0.5 ? a.lat : b.lat), s = same ? (float)(a.s + (b.s - a.s) * f) : (f < 0.5 ? a.s : b.s), e = f < 0.5 ? a.e : b.e });
                    next += 1;
                }
                acc += d;
            }
        }

        static double LgTurn(List<LgR> r, int i, int b)
        {
            double ax = r[i].x - r[i - b].x, az = r[i].z - r[i - b].z, bx = r[i + b].x - r[i].x, bz = r[i + b].z - r[i].z;
            return Math.Atan2(ax * bz - az * bx, ax * bx + az * bz) * 180.0 / Math.PI;
        }
        static double LgTurnLat(List<LgR> r, int i, int b) =>
            (Math.Atan2(r[i + b].lat - r[i].lat, b) - Math.Atan2(r[i].lat - r[i - b].lat, b)) * 180.0 / Math.PI;

        /// <summary>Does arc s of edge ei lie within <see cref="LineGateRules.TerminalReachM"/> (along the road,
        /// through joints) of a node where a ramp (a link) meets a freeway or expressway (class 4+), with
        /// ei itself one of the two?</summary>
        static bool LgAtRampTerminal(CityMap map, int ei, float s)
        {
            var e0 = map.edges[ei];
            bool fwy0 = e0.cls >= 4;
            if (!fwy0 && !e0.link) return false;
            for (int side = 0; side < 2; side++)
            {
                var e = e0; float left = LineGateRules.TerminalReachM - (side == 0 ? s : e0.length - s);
                int node = side == 0 ? e0.a : e0.b;
                for (int hop = 0; hop < 6 && left >= 0f; hop++)
                {
                    var arms = map.nodeEdges[node];
                    bool link = false, fwy = false;
                    foreach (int o in arms) { var oe = map.edges[o]; link |= oe.link; fwy |= !oe.link && oe.cls >= 4; }
                    if (link && fwy) return true;
                    if (arms.Count != 2) break;
                    int next = arms[0] == e.index ? arms[1] : arms[0];
                    if (next == e.index) break;
                    e = map.edges[next];
                    left -= e.length;
                    node = e.a == node ? e.b : e.a;
                }
            }
            return false;
        }

        /// <summary>Is there a junction or a dead end (a node of 1 or 3+ arms) within reach of arc s of edge ei,
        /// along the road through its 2-arm joints? (a map piece often ends a few metres short of the junction
        /// whose crosswalk and mouth strip are drawn on it)</summary>
        static bool LgJunctionWithin(CityMap map, int ei, float s, float reach)
        {
            var e0 = map.edges[ei];
            for (int side = 0; side < 2; side++)
            {
                var e = e0; float left = reach - (side == 0 ? s : e0.length - s);
                int node = side == 0 ? e0.a : e0.b;
                for (int hop = 0; hop < 6 && left >= 0f; hop++)
                {
                    var arms = map.nodeEdges[node];
                    if (arms.Count != 2) return true;
                    int next = arms[0] == e.index ? arms[1] : arms[0];
                    if (next == e.index) break;
                    e = map.edges[next];
                    left -= e.length;
                    node = e.a == node ? e.b : e.a;
                }
            }
            return false;
        }

        /// <summary>The angle point at sample i over chords of b and 2b metres: |2 turn(b) - turn(2b)|, less
        /// the same in the road's lateral frame (a taper's own break is SHIFT's); no lateral frame across a
        /// change of edge direction.</summary>
        static double LgKinkAt(List<LgR> r, int i, int b)
        {
            double kw = 2 * LgTurn(r, i, b) - LgTurn(r, i, 2 * b), kl = 0;
            bool oneFrame = true;
            for (int q = i - 2 * b; q < i + 2 * b; q++) if (r[q].e != r[q + 1].e && Mathf.Abs(r[q].lat - r[q + 1].lat) > 0.2f) { oneFrame = false; break; }
            if (oneFrame) kl = 2 * LgTurnLat(r, i, b) - LgTurnLat(r, i, 2 * b);
            return Math.Abs(kw - kl);
        }

        static string LgRoad(CityMap.Edge e) => string.IsNullOrEmpty(e.name) ? (e.link ? "(ramp)" : "(unnamed)") : e.name;

        static void LgShape(CityMap map, List<LgP> pts, List<LgStrand> members, List<LgR> r)
        {
            LgResample(pts, r);
            int n = r.Count;
            if (n < 12) return;
            var st0 = members[0];
            string kind = st0.Kind;
            // ---- KINK: an angle point of the alignment. A circular arc turns twice as far over twice the
            // baseline, an angle point the same: K = |2 turn(4 m) - turn(8 m)| is the angle point alone.
            // The part the line's own lateral move explains (a taper's break) is SHIFT's to judge.
            int lastK = -100;
            for (int i = 4; i < n - 4; i++)
            {
                bool wide = i >= 8 && i < n - 8;
                int b = wide ? 4 : 2;
                double kw = 2 * LgTurn(r, i, b) - LgTurn(r, i, 2 * b);
                double k = LgKinkAt(r, i, b);
                var e = map.edges[r[i].e];
                float mph = LineGateRules.Mph(e), lim = LineGateRules.KinkDeg(mph);
                if (k <= lim) continue;
                // An angle point keeps its whole deflection at every scale; the facets of a curve drawn as a
                // polyline (the map's smoothed curves: a vertex every 8-9 m, 0.8 deg each, a 600 m radius drawn
                // within 2 cm) vanish once the chords span more than one facet. Read again over 8 m and 16 m
                // chords where the line runs that far: the smallest reading is the angle point.
                for (int bb = 8; bb <= LineGateRules.KinkScaleMaxM && k > lim; bb *= 2)
                    if (i - 2 * bb >= 0 && i + 2 * bb <= n - 1) k = Math.Min(k, LgKinkAt(r, i, bb));
                if (k <= lim) { lgKinkFacets++; continue; }
                // Where the line is too short for the long chords (a chain cut at a seam or a step): a facet is
                // a vertex with a neighbour vertex 4-12 m away turning the same way at least half as far, and
                // the polygon then lies within the smoothness gate's V (2.5 cm) of the curve through it.
                {
                    double ti = LgTurn(r, i, 2), facetGap = 0;
                    for (int j = Math.Max(2, i - 12); j <= Math.Min(n - 3, i + 12); j++)
                    {
                        if (Math.Abs(j - i) < 4) continue;
                        double tj = LgTurn(r, j, 2);
                        if (tj * ti > 0 && Math.Abs(tj) >= 0.5 * Math.Abs(ti)) { int gap = Math.Abs(j - i); if (facetGap == 0 || gap < facetGap) facetGap = gap; }
                    }
                    if (facetGap > 0 && facetGap * k * Math.PI / 180.0 / 8.0 <= LineGateRules.FacetDevM) { lgKinkFacets++; continue; }
                }
                // a junction's corner (the curb return, the mouth's flare, a cul-de-sac's bulb) is not an
                // alignment: within 15 m of the drawn end at a node of 1 or 3+ arms (JUNCTION judges the
                // through lines' deflection across it, FDOT PPM Table 2.8.1b)
                {
                    int jn = LgEndNode(e, r[i].s, LineGateRules.KinkCornerM);
                    if (jn >= 0 && map.nodeEdges[jn].Count != 2) { lgKinkCorners++; continue; }
                }
                // a gore's nose: a lane or edge line meeting the gore's own line at its V (second sample set: every
                // kink of 6 deg and more on a freeway was one) - the gore's lines meet there, nothing bends
                if (LgNearGore(r[i].x, r[i].z, e.YAt(r[i].s), LineGateRules.GoreNoseM)) { lgKinkGore++; continue; }
                // a sharp turn of a line lying on another road's ribbon (a ramp's pavement over the host's paint at a
                // diverge or merge): not a bend anyone sees as one (the box run at I-485: every 20-70 deg "kink" and
                // 85 deg "S-jog" sat at a ramp's gore, on the ramp's pavement); counted, not failed
                if (k >= 10.0 && (LgUnderOther(r[i].x, r[i].z, e.YAt(r[i].s), r[i].e) || LgNearRampNode(map, r[i].e, r[i].s, LineGateRules.GoreReachM))) { lgUnderOther++; continue; }
                // the local maximum
                bool peak = true;
                for (int q = Math.Max(4, i - 3); q <= Math.Min(n - 5, i + 3) && peak; q++)
                {
                    if (q == i) continue;
                    bool wq = q >= 8 && q < n - 8; int bq = wq ? 4 : 2;
                    double kq = Math.Abs(2 * LgTurn(r, q, bq) - LgTurn(r, q, 2 * bq));
                    if (kq > Math.Abs(kw) + 1e-9) peak = false;
                }
                if (!peak || i - lastK < 6) continue;
                lastK = i;
                lgFails.Add(new LgFail { cls = "KINK", sub = st0.role == 2 ? "painted line" : "pavement edge", edge = e.index, s = r[i].s, s1 = r[i].s, x = r[i].x, z = r[i].z, measure = (float)k, limit = lim, unit = "deg", mph = mph, line = kind, model = false,
                                         what = $"the {kind} turns {k:0.0} deg at a point (limit {lim:0.00} deg at {mph:0} mph)" });
            }
            // ---- SJOG: the heading swings one way and back. Heading over a 4 m chord; turning points with 0.35 deg hysteresis.
            var h = new double[n];
            double prevH = 0;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 2), b = Math.Min(n - 1, i + 2);
                double ang = Math.Atan2(r[b].z - r[a].z, r[b].x - r[a].x) * 180.0 / Math.PI;
                if (i > 0) { while (ang - prevH > 180) ang -= 360; while (ang - prevH < -180) ang += 360; }
                h[i] = ang; prevH = ang;
            }
            var tp = new List<int>(16) { 0 };
            {
                const double eta = 0.35;
                int ext = 0; int dir = 0;
                for (int i = 1; i < n; i++)
                {
                    if (dir == 0) { if (Math.Abs(h[i] - h[0]) > eta) { dir = h[i] > h[0] ? 1 : -1; ext = i; } continue; }
                    if (dir > 0 ? h[i] > h[ext] : h[i] < h[ext]) ext = i;
                    else if (Math.Abs(h[i] - h[ext]) > eta) { tp.Add(ext); dir = -dir; ext = i; }
                }
                tp.Add(n - 1);
            }
            for (int k = 0; k + 2 < tp.Count; k++)
            {
                int i0 = tp[k], i1 = tp[k + 1], i2 = tp[k + 2];
                double p1 = h[i1] - h[i0], p2 = h[i2] - h[i1];
                if (p1 * p2 >= 0) continue;
                double a1 = Math.Abs(p1), a2 = Math.Abs(p2);
                if (Math.Min(a1, a2) < 0.45 * Math.Max(a1, a2)) continue;
                // the swing's own extent: from where the heading has left its start by 10 % of the swing to where it is back within 10 %
                double hb = h[i0], thr = 0.1 * a1;
                int ja = i0; while (ja < i1 && Math.Abs(h[ja + 1] - hb) < thr) ja++;
                double hEnd = h[i2];
                int jb = i2; while (jb > i1 && Math.Abs(h[jb - 1] - hEnd) < 0.1 * a2) jb--;
                double D = Math.Max(1, jb - ja);
                // the lateral displacement off the entry tangent
                double W = 0;
                for (int i = ja; i < jb; i++) W += Math.Sin((h[i] - hb) * Math.PI / 180.0);
                W = Math.Abs(W);
                if (W < LineGateRules.JogMinM || W > LineGateRules.JogMaxM) continue;
                var e = map.edges[r[i1].e];
                float mph = LineGateRules.Mph(e); float rate = LineGateRules.TaperRate(mph);
                // the pavement edge (or edge line) bulging out and back at a freeway ramp terminal: the
                // speed-change lane, judged on the terminal's taper (AASHTO), not MUTCD's through-lane drop
                if (Math.Max(a1, a2) >= 45.0 && (LgUnderOther(r[i1].x, r[i1].z, e.YAt(r[i1].s), r[i1].e) || LgNearRampNode(map, r[i1].e, r[i1].s, LineGateRules.GoreReachM))) { lgUnderOther++; continue; }
                bool terminal = st0.Boundary && LgAtRampTerminal(map, r[i1].e, r[i1].s);
                if (terminal) rate = Mathf.Min(rate, LineGateRules.TerminalRate);
                double need = rate * W;
                if (D >= LineGateRules.TaperTol * need) { if (terminal) lgTerminalPassed++; continue; }
                // two real curves, each no tighter than its speed allows, are an alignment, not a jog
                float cMph = e.advisoryMph > 0 ? Mathf.Min(mph, e.advisoryMph) : mph;
                double rMin = LineGateRules.MinRadiusM(cMph);
                double r1 = (i1 - ja) / Math.Max(1e-6, a1 * Math.PI / 180.0), r2 = (jb - i1) / Math.Max(1e-6, a2 * Math.PI / 180.0);
                if (Math.Max(a1, a2) >= 8.0 && r1 >= rMin && r2 >= rMin) continue;
                // is it the line moving in its road's frame (the model), or the road's own line (the map data)?
                float latMin = float.MaxValue, latMax = float.MinValue; bool oneFrame = true;
                for (int i = ja; i <= jb; i++) { latMin = Mathf.Min(latMin, r[i].lat); latMax = Mathf.Max(latMax, r[i].lat); if (i > ja && r[i].e != r[i - 1].e && Mathf.Abs(r[i].lat - r[i - 1].lat) > 0.2f) oneFrame = false; }
                bool model = oneFrame && latMax - latMin >= 0.5 * W;
                lgFails.Add(new LgFail { cls = "SJOG", sub = model ? "the line model's" : "the road's own line", edge = e.index, s = r[ja].s, s1 = r[jb].s, x = r[i1].x, z = r[i1].z, measure = (float)D, limit = (float)need, unit = "m", mph = mph, line = kind, model = model,
                                         what = $"the {kind} swings {Math.Max(a1, a2):0.0} deg and back, {W:0.00} m sideways in {D:0} m; at {mph:0} mph that shift needs {need:0} m ({(terminal ? $"AASHTO ramp terminal taper {rate:0}:1" : $"MUTCD L = W x {rate:0.0}")})" });
            }
            // ---- SHIFT: a monotone lateral move in the road's own frame, against the taper its kind needs.
            {
                const float eta = 0.02f;
                int runStart = 0; int ext = 0; int dir = 0;
                void Close(int a, int b)
                {
                    if (b <= a) return;
                    float W = r[b].lat - r[a].lat;
                    if (Mathf.Abs(W) < LineGateRules.ShiftMinM) return;
                    // the extent: from 1 cm off the start value to 1 cm off the end value
                    int ja = a; while (ja < b && Mathf.Abs(r[ja + 1].lat - r[a].lat) < 0.01f) ja++;
                    int jb = b; while (jb > ja && Mathf.Abs(r[jb - 1].lat - r[b].lat) < 0.01f) jb--;
                    float ext1 = Mathf.Max(1f, jb - ja) / 0.94f;
                    float peak = 0f;
                    for (int i = ja; i + 2 <= jb; i++) peak = Mathf.Max(peak, Mathf.Abs(r[i + 2].lat - r[i].lat) / 2f);
                    if (jb - ja < 2) peak = Mathf.Abs(W) / Mathf.Max(1f, jb - ja);
                    float L = Mathf.Min(ext1, peak > 1e-5f ? 1.5f * Mathf.Abs(W) / peak : ext1);
                    int im = (ja + jb) / 2;
                    var e = map.edges[r[im].e];
                    float mph = LineGateRules.Mph(e);
                    // what moves: this boundary alone (a lane added or dropped), or the travelled lanes
                    string type = LgShiftType(map, members, r, ja, jb, out bool through);
                    float rate = type == "add" ? LineGateRules.BayRate(mph) : LineGateRules.TaperRate(mph);
                    // a freeway's ramp terminal: the speed-change lane's pavement edge and edge line run in on the
                    // terminal's own taper (AASHTO), not MUTCD's through-lane drop
                    bool terminal = type == "drop" && members[0].Boundary && LgAtRampTerminal(map, r[im].e, r[im].s);
                    if (terminal) rate = Mathf.Min(rate, LineGateRules.TerminalRate);
                    float need = rate * Mathf.Abs(W);
                    if (L >= LineGateRules.TaperTol * need) { if (terminal) lgTerminalPassed++; return; }
                    string rule = type == "add" ? $"AASHTO bay taper {rate:0.0}:1" : terminal ? $"AASHTO ramp terminal taper {rate:0}:1" : $"MUTCD L = W x {rate:0.0}";
                    lgFails.Add(new LgFail { cls = "SHIFT", sub = type == "add" ? "lane-add (bay) taper" : type == "drop" ? "lane-drop taper" : "through lanes shift", edge = e.index, s = r[ja].s, s1 = r[jb].s, x = r[im].x, z = r[im].z, measure = L, limit = need, unit = "m", mph = mph, line = kind, model = true,
                                             what = $"the {kind} moves {Mathf.Abs(W):0.00} m sideways in {L:0} m; at {mph:0} mph that needs {need:0} m ({rule})" });
                }
                for (int i = 1; i < n; i++)
                {
                    // a new lateral frame: close the run
                    if (r[i].e != r[i - 1].e && Mathf.Abs(r[i].lat - r[i - 1].lat) > 0.2f) { if (dir != 0) Close(runStart, i - 1); runStart = i; ext = i; dir = 0; continue; }
                    if (dir == 0) { if (Mathf.Abs(r[i].lat - r[runStart].lat) > eta) { dir = r[i].lat > r[runStart].lat ? 1 : -1; ext = i; } else if (i - runStart > 400) runStart = i - 200; continue; }
                    if (dir > 0 ? r[i].lat > r[ext].lat : r[i].lat < r[ext].lat) ext = i;
                    else if (Mathf.Abs(r[i].lat - r[ext].lat) > eta || i - ext > 40) { Close(runStart, ext); runStart = ext; dir = Mathf.Abs(r[i].lat - r[ext].lat) > eta ? -dir : 0; ext = i; }
                }
                if (dir != 0) Close(runStart, ext);
            }
        }

        /// <summary>What a lateral move is: "add" (this boundary alone moves
        /// out, in its side's direction of travel: a lane or bay opens),
        /// "drop" (it moves in: a lane ends), or "shift" (the lane lines move
        /// with it, or it is a lane line itself: the travelled lanes shift).
        /// The move may run on through joints: its side and its direction are
        /// read in the chain's frame (lat + = the chain's left), the lines
        /// beside it on the edge at its middle.</summary>
        static string LgShiftType(CityMap map, List<LgStrand> members, List<LgR> r, int ja, int jb, out bool through)
        {
            through = false;
            int im = (ja + jb) / 2, ei = r[im].e;
            var e = map.edges[ei];
            int a = im, b = im;
            while (a > ja && r[a - 1].e == ei) a--;
            while (b < jb && r[b + 1].e == ei) b++;
            float sa = r[a].s, sb = r[b].s, s0 = Mathf.Min(sa, sb), s1 = Mathf.Max(sa, sb);
            bool boundary = members[0].Boundary;
            LgStrand st = null;
            foreach (var m in members) if (m.edge == ei && m.S0 <= s1 + 0.5f && m.S1 >= s0 - 0.5f) { st = m; break; }
            if (st != null && s1 - s0 > 1f)
            {
                float Wd = LgLatAt(st, sb) - LgLatAt(st, sa);
                int moved = 0, held = 0;
                var ids = lgByEdge[ei];
                if (ids != null && Mathf.Abs(Wd) > 0.1f)
                    foreach (int id in ids)
                    {
                        var o = lgStrands[id];
                        if (o == st || o.role != 2 || o.EdgeLine) continue;
                        if (o.S0 > s0 + 0.5f || o.S1 < s1 - 0.5f) continue;
                        float d = LgLatAt(o, sb) - LgLatAt(o, sa);
                        if (Mathf.Abs(d) >= 0.5f * Mathf.Abs(Wd) && d * Wd > 0) moved++; else held++;
                    }
                if (!boundary || moved > held) { through = true; return "shift"; }
            }
            else if (!boundary) { through = true; return "shift"; }
            float W = r[jb].lat - r[ja].lat;                         // chain frame
            bool left = 0.5f * (r[ja].lat + r[jb].lat) > 0f;
            bool outward = left ? W > 0f : W < 0f;                    // along the chain
            bool forward = b > a ? r[b].s > r[a].s : true;            // the chain runs a->b on this edge
            bool travelWith = e.oneway ? forward : !left;             // a two-way's right side travels with the chain
            return (travelWith ? outward : !outward) ? "add" : "drop";
        }

        static float LgLatAt(LgStrand st, float s)
        {
            var p = st.p; int lo = 0, hi = p.Count - 1;
            if (s <= p[0].s) return p[0].lat;
            if (s >= p[hi].s) return p[hi].lat;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (p[m].s <= s) lo = m; else hi = m; }
            float d = p[hi].s - p[lo].s;
            return d > 1e-5f ? Mathf.Lerp(p[lo].lat, p[hi].lat, (s - p[lo].s) / d) : p[lo].lat;
        }
        static Vector2 LgPosAt(LgStrand st, float s)
        {
            var p = st.p; int lo = 0, hi = p.Count - 1;
            if (s <= p[0].s) return new Vector2((float)p[0].x, (float)p[0].z);
            if (s >= p[hi].s) return new Vector2((float)p[hi].x, (float)p[hi].z);
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (p[m].s <= s) lo = m; else hi = m; }
            float d = p[hi].s - p[lo].s, f = d > 1e-5f ? (s - p[lo].s) / d : 0f;
            return new Vector2((float)(p[lo].x + (p[hi].x - p[lo].x) * f), (float)(p[lo].z + (p[hi].z - p[lo].z) * f));
        }

        // ================================================================
        //  WIDTH: every marked lane, every 5 m
        // ================================================================
        static void LgWidths(CityMap map)
        {
            var at = new List<(float lat, LgStrand st)>(16);
            for (int ei = 0; ei < map.edges.Length; ei++)
            {
                var ids = lgByEdge[ei];
                if (ids == null) continue;
                var e = map.edges[ei];
                float s0 = lgTrims.atA[ei] + 1f, s1 = e.length - lgTrims.atB[ei] - 1f;
                if (s1 - s0 < 4f) continue;
                float mph = LineGateRules.Mph(e);
                // a run of failing stations of one kind is one failure
                string runKey = null; float runS0 = 0, runS = 0, runWorst = 0, runLim = 0; Vector2 runAt = default; string runWhat = null, runLine = null;
                void Flush()
                {
                    if (runKey != null && runS - runS0 >= 4.9f)
                        lgFails.Add(new LgFail { cls = "WIDTH", sub = runKey, edge = ei, s = runS0, s1 = runS, x = runAt.x, z = runAt.y, measure = runWorst, limit = runLim, unit = "m", mph = mph, line = runLine,
                                                 what = runWhat + $" for {runS - runS0 + 5f:0} m" });
                    runKey = null;
                }
                int nSt = Mathf.Max(1, Mathf.FloorToInt((s1 - s0) / 5f));
                for (int q = 0; q <= nSt; q++)
                {
                    float s = s0 + (s1 - s0) * q / nSt;
                    at.Clear();
                    foreach (int id in ids)
                    {
                        var st = lgStrands[id];
                        if (st.role == 3) continue;
                        float slack = st.role == 2 && st.broken ? 9.6f : 0.01f;
                        if (st.S0 > s + slack || st.S1 < s - slack) continue;
                        at.Add((LgLatAt(st, s), st));
                    }
                    if (at.Count < 2) { Flush(); continue; }
                    at.Sort((a, b) => a.lat.CompareTo(b.lat));
                    string key = null; float worst = 0, lim = 0; string what = null, line = null; Vector2 pos = default;
                    for (int i = 0; i + 1 < at.Count; i++)
                    {
                        var A = at[i].st; var B = at[i + 1].st;
                        float w = at[i + 1].lat - at[i].lat;
                        if (A.role != 2 && B.role != 2) continue;                       // an unmarked road: no lane is marked
                        // the clear width between the two lines' near sides
                        float clear = w - 0.5f * (A.role == 2 ? A.pw : 0f) - 0.5f * (B.role == 2 ? B.pw : 0f);
                        bool pave = A.role != 2 || B.role != 2;
                        var L = A.role == 2 ? A : B; var O = A.role == 2 ? B : A;
                        string k = null; float lm = 0; string wh = null;
                        if (pave && L.EdgeLine)
                        {
                            // a shoulder: edge line to the pavement edge
                            if (clear > LineGateRules.ShoulderMaxM) { k = "paved width beyond the edge line"; lm = LineGateRules.ShoulderMaxM; wh = $"{clear:0.0} m of pavement outside the {L.Kind} (a shoulder is at most {lm:0.0} m)"; }
                        }
                        else if (pave)
                        {
                            // a curb lane: the lane runs to the pavement edge (gutter; parking on a local or collector street)
                            float max = LineGateRules.LaneMaxM + LineGateRules.GutterM + (e.cls <= 1 ? LineGateRules.ParkingM : 0f);
                            if (clear > max) { k = "curb lane too wide"; lm = max; wh = $"{clear:0.0} m between the {L.Kind} and the pavement edge with no line (limit {max:0.0} m)"; }
                            // (a SOLID white line this near the curb marks a shoulder or a bike lane, MUTCD 2009 3B.06 / 9C.04;
                            // only a broken lane line bounds a curb travel lane: second sample set)
                            else if (clear < LineGateRules.LaneMinM && clear >= LineGateRules.BufferMaxM && L.broken && !L.yellow && !L.channel && LgHolds(A, B, s, w, 5f, 0.4f)) { k = "curb lane too narrow"; lm = LineGateRules.LaneMinM; wh = $"{clear:0.0} m between the {L.Kind} and the pavement edge"; }
                        }
                        else if (A.yellow && B.yellow)
                        {
                            // between two yellows: a two-way left-turn lane or a painted median; a double line is one line
                            if (clear > 0.6f && clear < LineGateRules.TwltlMinM && (A.broken || B.broken)) { k = "two-way left-turn lane too narrow"; lm = LineGateRules.TwltlMinM; wh = $"{clear:0.0} m between the yellow lines of a two-way left-turn lane"; }
                        }
                        else if (A.channel || B.channel) { }                             // beside a channelizing line: a gore, a turning roadway
                        else if (e.lsBike != 0 && clear >= 1.0f && clear <= 2.5f && (i == 0 || i + 2 == at.Count || at[i - 1].st.role != 2 || at[i + 2].st.role != 2)) { }   // a bike lane (AASHTO: 4 ft or more)
                        else
                        {
                            float max = e.link && e.lanes <= 1 ? LineGateRules.RampLaneMaxM : LineGateRules.LaneMaxM;
                            if (clear > max) { k = "lane too wide"; lm = max; wh = $"{clear:0.0} m between the {A.Kind} and the {B.Kind} (a lane is at most {max:0.0} m)"; }
                            else if (clear < LineGateRules.LaneMinM && clear >= LineGateRules.BufferMaxM && !(A.EdgeLine && B.EdgeLine) && LgHolds(A, B, s, w, 5f, 0.4f)) { k = "lane too narrow"; lm = LineGateRules.LaneMinM; wh = $"{clear:0.0} m between the {A.Kind} and the {B.Kind} (a lane is at least {lm:0.0} m)"; }
                        }
                        if (k == null) continue;
                        if (clear > lm && !LgHolds(A, B, s, w)) continue;               // widening or narrowing here: a taper (SHIFT judges it)
                        float bad = clear > lm ? clear - lm : lm - clear;
                        if (key == null || bad > Mathf.Abs(worst - lim)) { key = k; worst = clear; lim = lm; what = wh; line = L.Kind; pos = 0.5f * (LgPosAt(A, s) + LgPosAt(B, s)); }
                    }
                    if (key == null) { Flush(); continue; }
                    // outside a taper: a too-wide width must hold (within 0.3 m) over the 15 m either side
                    if (key != runKey) { Flush(); runKey = key; runS0 = s; runWorst = worst; runLim = lim; runAt = pos; runWhat = what; runLine = line; }
                    else if (Mathf.Abs(worst - lim) > Mathf.Abs(runWorst - runLim)) { runWorst = worst; runAt = pos; runWhat = what; }
                    runS = s;
                }
                Flush();
            }
        }

        /// <summary>Does the width between two strands hold (within 0.3 m) 15 m back and 15 m on?</summary>
        static bool LgHolds(LgStrand A, LgStrand B, float s, float w, float reach = 15f, float tol = 0.3f)
        {
            for (int q = -1; q <= 1; q += 2)
            {
                float s2 = s + reach * q;
                float sa = A.role == 2 && A.broken ? 9.6f : 0.01f, sb = B.role == 2 && B.broken ? 9.6f : 0.01f;
                if (A.S0 > s2 + sa || A.S1 < s2 - sa || B.S0 > s2 + sb || B.S1 < s2 - sb) continue;   // one of them ends: the next edge's
                if (Mathf.Abs(LgLatAt(B, s2) - LgLatAt(A, s2) - w) > tol) return false;
            }
            return true;
        }

        // ================================================================
        //  CONT: a line that ends inside its edge
        // ================================================================
        static void LgEnds(CityMap map)
        {
            foreach (var st in lgStrands)
            {
                if (st.role != 2 || st.channel) continue;
                var e = map.edges[st.edge];
                for (int q = 0; q < 2; q++)
                {
                    bool end = q == 1;
                    if ((end ? st.linkB : st.linkA) >= 0) continue;
                    var p = end ? st.p[st.p.Count - 1] : st.p[0];
                    if (LgEndNode(e, p.s, st.broken ? 9.6f : 0.75f) >= 0) continue;
                    // inside the edge. Legitimate where the pavement's width is changing (the line of a lane
                    // that is opening or closing starts where its lane has its width: MUTCD 3B.04 / 3B.12),
                    // and an edge line broken for a side road's mouth (it resumes within 40 m).
                    if (LgWidthChanging(st.edge, p.s)) continue;
                    // An edge line may stop inside its edge: MUTCD 2009 3B.06 (not continued through
                    // intersections or major driveways) and 3B.07 (may be omitted where curbs, parking or
                    // other markings delineate the travelled way). The pavement edge itself is judged.
                    if (st.EdgeLine) { lgEndsEdgeLine++; continue; }
                    // A line stops at the stop line or the crosswalk before a junction (MUTCD 2009 3B.16 / 3B.18),
                    // which the map piece may carry several metres short of its node: within 25 m of a junction
                    if (LgJunctionWithin(map, st.edge, p.s, LineGateRules.MouthStripM)) { lgEndsAtJunction++; continue; }
                    bool resumes = false, retyped = false;
                    var ids = lgByEdge[st.edge];
                    foreach (int id in ids)
                    {
                        var o = lgStrands[id];
                        if (o == st || o.role != 2) continue;
                        if (o.tk == st.tk)
                        {
                            float os = end ? o.S0 : o.S1;
                            float gap = end ? os - p.s : p.s - os;
                            if (gap > 0f && gap <= 40f && Mathf.Abs(LgLatAt(o, os) - p.lat) < 0.3f) resumes = true;
                        }
                        else
                        {
                            // the same line in another pattern (broken to solid before a junction, a centre
                            // line's no-passing pattern, a lane line becoming a drop line): MUTCD 2009 3B.04,
                            // 3B.02, 3A.06. It takes over within a dash cycle (12.2 m) at the same lateral.
                            float os = end ? o.S0 : o.S1;
                            float gap = end ? os - p.s : p.s - os;
                            if (gap > -2f && gap <= 12.5f && Mathf.Abs(LgLatAt(o, Mathf.Clamp(p.s, o.S0, o.S1)) - p.lat) < 0.3f) retyped = true;
                        }
                    }
                    if (retyped) { lgEndsTypeChange++; continue; }
                    lgFails.Add(new LgFail { cls = "CONT", sub = resumes ? "line interrupted" : "line ends inside its road", edge = st.edge, s = p.s, s1 = p.s, x = p.x, z = p.z, measure = 0f, limit = 0f, unit = "", line = st.Kind,
                                             what = $"the {st.Kind} {(end ? "ends" : "starts")} at s {p.s:0} of {e.length:0} m with the pavement's width unchanged" + (resumes ? " and resumes within 40 m" : "") });
                }
            }
        }

        static float LgPaveWidth(int edge, float s)
        {
            LgStrand a = null, b = null;
            var ids = lgByEdge[edge];
            if (ids == null) return -1f;
            foreach (int id in ids)
            {
                var st = lgStrands[id];
                if (st.role >= 2 || st.S0 > s + 0.01f || st.S1 < s - 0.01f) continue;
                if (st.role == 0) a = st; else b = st;
            }
            return a != null && b != null ? LgLatAt(b, s) - LgLatAt(a, s) : -1f;
        }

        // ================================================================
        //  The run
        // ================================================================
        public static void RunLineGate()
        {
            outLog = new StringBuilder(); failures = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing"); FinishTo("city_linegate.txt"); return; }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string tag = Environment.GetEnvironmentVariable("PSX_LG_TAG") ?? "run";
            lgMap = map;
            lgPieces = new List<LgPiece>[map.edges.Length]; lgSpans = new List<LgSpan>[map.edges.Length]; lgByEdge = new List<int>[map.edges.Length];
            lgGhosts.Clear(); lgFans.Clear(); lgStrands.Clear(); lgFails.Clear();
            lgTapNotes = lgQuads = lgPaintQuads = lgGlyphQuads = lgDoubleSpans = lgTiles = lgSeamSpans = lgMarks = lgSolidLane = lgUntappedTris = lgKinkFacets = lgEndsTypeChange = lgEndsEdgeLine = lgCtlEqual = lgKinkCorners = lgStepCorners = lgMouthPassed = lgTerminalPassed = lgJointRetyped = lgGradeFacets = lgSharpJoins = lgEndsAtJunction = lgKinkGore = lgUnderOther = 0; lgStretchedM = 0; lgStripeM = 0; lgStripeQuads = 0; lgChannelChains = 0;
            lgPolys.Clear(); lgPolyGrid.Clear(); lgFanTap = null; lgBarV.Clear(); lgCurbV.Clear();
            var t = CityMeshes.ComputeTrims(map);
            lgTrims = t;
            var noBld = new Dictionary<long, List<CityBuildings.B>>();

            // ---- the tiles ----
            float T = CityMeshes.TileSize;
            string box = Environment.GetEnvironmentVariable("PSX_LG_BOX"), nameOnly = Environment.GetEnvironmentVariable("PSX_LG_NAME");
            float bx0 = float.MinValue, bz0 = float.MinValue, bx1 = float.MaxValue, bz1 = float.MaxValue;
            if (!string.IsNullOrWhiteSpace(box))
            {
                var q = box.Split(',');
                bx0 = float.Parse(q[0], LgInv); bz0 = float.Parse(q[1], LgInv); bx1 = float.Parse(q[2], LgInv); bz1 = float.Parse(q[3], LgInv);
            }
            var seen = new HashSet<long>(); var tiles = new List<(int tx, int tz)>();
            var scope = new bool[map.edges.Length];
            foreach (var e in map.edges)
            {
                if (!string.IsNullOrWhiteSpace(nameOnly) && e.name != nameOnly) continue;
                float rr = e.HalfMax + 1f; bool inBox = false;
                for (float s = 0f; ; s += 2f)
                {
                    var p = e.PointAt(Mathf.Min(s, e.length));
                    if (p.x >= bx0 && p.x <= bx1 && p.y >= bz0 && p.y <= bz1)
                    {
                        inBox = true;
                        int x0 = Mathf.FloorToInt((p.x - rr) / T), x1 = Mathf.FloorToInt((p.x + rr) / T), z0 = Mathf.FloorToInt((p.y - rr) / T), z1 = Mathf.FloorToInt((p.y + rr) / T);
                        for (int tx = x0; tx <= x1; tx++) for (int tz = z0; tz <= z1; tz++) if (seen.Add(((long)tx << 32) ^ (uint)tz)) tiles.Add((tx, tz));
                    }
                    if (s >= e.length) break;
                }
                scope[e.index] = inBox;
            }
            tiles.Sort((a, b) => a.tz != b.tz ? a.tz.CompareTo(b.tz) : a.tx.CompareTo(b.tx));
            bool whole = string.IsNullOrWhiteSpace(box) && string.IsNullOrWhiteSpace(nameOnly);
            Line($"LINE GATE '{tag}': graph {map.graphHash:x8}, {map.edges.Length} edges, {tiles.Count} road tiles{(whole ? " (the whole city)" : $" (PSX_LG_BOX '{box}', PSX_LG_NAME '{nameOnly}')")}; PSX_CITY_SPLITAXIS {LineModel.SplitAxis}");

            StringBuilder spotDump = null; Vector2 spot = default; float spotR = 0f;
            string spotEnv = Environment.GetEnvironmentVariable("PSX_LG_SPOT");
            if (!string.IsNullOrWhiteSpace(spotEnv))
            {
                var q = spotEnv.Split(',');
                spot = new Vector2(float.Parse(q[0], LgInv), float.Parse(q[1], LgInv)); spotR = q.Length > 2 ? float.Parse(q[2], LgInv) : 60f;
                spotDump = new StringBuilder("kind\tedge\tsA\tsB\tk\tlatL\tlatR\tuL\tuR\twhat\twA\twB\tsqueeze\tx\tfA\tfB\n");
            }
            bool tapWas = CityMeshes.RecordTap;
            CityMeshes.RecordTap = true;
            try
            {
                foreach (var (tx, tz) in tiles)
                {
                    var tm = CityMeshes.Build(map, t, noBld, tx, tz);
                    if (tm == null) continue;
                    lgTiles++;
                    LgCollect(map, tx, tz, tm, spotDump, spot, spotR);
                    LgCollect2(tm);
                    foreach (var m in new[] { tm.ground, tm.roads, tm.barriers, tm.kerbs, tm.water, tm.banks, tm.buildings, tm.buildingCollider, tm.lampPosts })
                        if (m != null) UnityEngine.Object.DestroyImmediate(m);
                    if (lgTiles % 500 == 0) Debug.Log($"[LineGate] {lgTiles} of {tiles.Count} tiles, {clock.Elapsed.TotalSeconds:0} s");
                }
            }
            finally { CityMeshes.RecordTap = tapWas; }
            double buildS = clock.Elapsed.TotalSeconds;
            Line($"  built {lgTiles} tiles in {buildS:0} s; {lgQuads} ribbon quads read ({lgPaintQuads} paint, {lgGlyphQuads} glyph / bar), tap notes {lgTapNotes}");

            // ---- chain, judge ----
            LgChainEdges(map);
            LgClassify(map);
            LgLinkJoints(map, t);
            int chains = 0; double chainKm = 0;
            var members = new List<LgStrand>(64); var rs = new List<LgR>(1024);
            foreach (var st in lgStrands)
            {
                if (st.used || st.role == 3) continue;
                var pts = LgChainOf(st, members, out _);
                if (pts.Count < 2) continue;
                chains++;
                // a channelizing line (wide solid white: a gore's two sides, a turning roadway's edge through a median)
                // follows its turning roadway, not the road it is painted on: its shape is not judged in that road's frame
                if (st.role == 2 && st.channel) { lgChannelChains++; continue; }
                LgShape(map, pts, members, rs);
                LgGradeBreaks(map, pts, members[0]);
                chainKm += rs.Count / 1000.0;
            }
            LgWidths(map);
            LgEnds(map);
            LgSurface(map);
            LgSteps(map, t);
            LgSolids(map);
            LgJunctions(map, t, scope);
            LgThrough(map, t, scope);
            LgDoubles(map, t, scope);
            LgBridges(map, t, scope);
            Line($"  {lgStrands.Count} strands in {chains} chains ({chainKm:0} km of painted line and pavement edge walked); {clock.Elapsed.TotalSeconds - buildS:0} s to judge");

            // out-of-scope edges (a box run reads neighbours only in part): keep what lies on a road in scope
            lgFails.RemoveAll(f => f.edge >= 0 && f.edge < scope.Length && !scope[f.edge]);
            LgFinish(map, t, root, tag, whole, spotDump, spot, spotR);
            Line($"  total {clock.Elapsed.TotalSeconds:0} s");
            Debug.Log("[LineGate] DONE " + lgFails.Count + " failures");
            File.WriteAllText(Path.Combine(root, "city_linegate.txt"), outLog.ToString());
        }

        /// <summary>SURFACE: the pavement between the lines drawn with its texture stretched or squeezed
        /// (a strip 1.5 m wide or more whose texels cover more than 1.25x or less than 0.8x their own
        /// width). The PC build lays its HD road base through these same UVs (TextureHDMap.Road), so the
        /// base's seams and cracks run stretched and, where the strip tapers, diagonally across the lanes:
        /// the dark stripes of the owner's North Tryon picture. By run along each edge.
        /// CHECKED 2026-11-17 and NOT FAILED any more (census only): the HD road base, its normal, AO and
        /// roughness maps and the paint masks' grain channel (HDSrc/TexHD city_road_base_*, *_paint) carry
        /// no seam, crack or band at all (column and row means flat within 1/255 over 64 bins), and the
        /// Classic textures are flat grain too, so a stretched strip draws the same even asphalt: nothing
        /// a driver or a plan reviewer sees. The sampled places (30, editor frames) showed nothing either.
        /// PSX_LG_STRETCHFAIL=1 fails them again. The owner's dark stripes are not explained by the road's
        /// textures (tyre marks, SkidMarks.cs, are the likely cause: a PC check, not this gate's).</summary>
        static void LgSurface(CityMap map)
        {
            lgGhosts.Sort((a, b) => a.edge != b.edge ? a.edge.CompareTo(b.edge) : a.s0 != b.s0 ? a.s0.CompareTo(b.s0) : a.x.CompareTo(b.x));
            int i = 0;
            while (i < lgGhosts.Count)
            {
                int j = i; var g0 = lgGhosts[i]; float sMax = g0.s1, worst = 1f, wAt = 0f; LgGhost gw = g0;
                while (j < lgGhosts.Count && lgGhosts[j].edge == g0.edge && lgGhosts[j].s0 <= sMax + 10f)
                {
                    var g = lgGhosts[j];
                    sMax = Mathf.Max(sMax, g.s1);
                    float off = g.squeeze > 1f ? g.squeeze : 1f / Mathf.Max(1e-3f, g.squeeze);
                    if (off > worst) { worst = off; wAt = g.w; gw = g; }
                    j++;
                }
                float len = sMax - g0.s0;
                lgStretchedM += len;
                if (len >= 5f && Environment.GetEnvironmentVariable("PSX_LG_STRETCHFAIL") == "1")
                    lgFails.Add(new LgFail { cls = "SURFACE", sub = "pavement texture stretched", edge = g0.edge, s = g0.s0, s1 = sMax, x = gw.x, z = gw.z, measure = worst, limit = 1.25f, unit = "x",
                                             what = $"{len:0} m where a {wAt:0.0} m strip of pavement is drawn with its texture {(gw.squeeze > 1f ? "stretched" : "squeezed")} {worst:0.0}x (the HD road base's seams run off the lanes there)" });
                i = j;
            }
        }

        static readonly string[] LgClasses = { "SHIFT", "SJOG", "KINK", "WIDTH", "CONT", "SURFACE", "JUNCTION", "MEDIAN", "STEP", "BARRIER", "BRIDGE" };

        static void LgFinish(CityMap map, CityMeshes.Trims t, string root, string tag, bool whole, StringBuilder spotDump, Vector2 spot, float spotR)
        {
            // ---- fill in road, tier, speed; visibility ----
            var splitEdges = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) { splitEdges.Add(sp.u); splitEdges.Add(sp.a); splitEdges.Add(sp.b); }
            foreach (var f in lgFails)
            {
                if (f.s1 < f.s) { float sw = f.s; f.s = f.s1; f.s1 = sw; }
                var e = map.edges[Mathf.Clamp(f.edge, 0, map.edges.Length - 1)];
                if (f.cls == "SHIFT" || f.cls == "SJOG" || f.cls == "KINK" || f.cls == "WIDTH" || f.cls == "CONT" || f.cls == "SURFACE" || f.cls == "JUNCTION")
                    f.cause = LgCauseOf(map, t, splitEdges, e, f.s, f.s1);
                f.road = LgRoad(e); f.tier = CityTier.Of(e);
                if (f.mph <= 0f) f.mph = LineGateRules.Mph(e);
                float sp = f.mph / 30f;
                switch (f.cls)
                {
                    case "SJOG": case "SHIFT": f.vis = sp * Mathf.Clamp(f.limit / Mathf.Max(1f, f.measure), 1f, 20f) * Mathf.Clamp(f.limit / LineGateRules.TaperRate(f.mph), 0.05f, 8f); break;
                    case "KINK": f.vis = sp * Mathf.Clamp(f.measure / Mathf.Max(0.1f, f.limit), 1f, 20f) * 0.25f; break;
                    case "SURFACE": f.vis = sp * Mathf.Clamp(f.measure, 1f, 4f) * 0.6f; break;
                    case "WIDTH": f.vis = sp * Mathf.Abs(f.measure - f.limit) * 0.5f; break;
                    case "CONT": f.vis = sp * (f.measure > 0f ? 2f + 4f * f.measure : 1.5f); break;
                    case "STEP": f.vis = sp * sp * (f.unit == "mm" ? Mathf.Clamp(f.measure / 12f, 0.2f, 6f) : Mathf.Clamp(f.measure / Mathf.Max(0.1f, f.limit), 1f, 10f)); break;
                    case "BARRIER": f.vis = sp * (3f + 4f * f.measure); break;
                    case "MEDIAN": f.vis = sp * (2f + 3f * f.measure); break;
                    case "JUNCTION": f.vis = sp * (f.sub.StartsWith("solid") ? 2.5f : f.sub.StartsWith("stop") ? 1.5f + f.measure : 1.2f); break;
                    case "BRIDGE": f.vis = sp * 3f; break;
                    default: f.vis = sp * 0.5f; break;
                }
                if (f.tier == CityTier.T3) f.vis *= 0.5f;
            }
            // ---- a SHIFT inside an SJOG of the same line is the jog's own leg ----
            var jogs = new Dictionary<int, List<LgFail>>();
            foreach (var f in lgFails) if (f.cls == "SJOG") { if (!jogs.TryGetValue(f.edge, out var l)) jogs[f.edge] = l = new List<LgFail>(); l.Add(f); }
            foreach (var f in lgFails)
                if (f.cls == "SHIFT" && jogs.TryGetValue(f.edge, out var l))
                    foreach (var j in l) if (j.line == f.line && (f.x - j.x) * (f.x - j.x) + (f.z - j.z) * (f.z - j.z) < 40.0 * 40.0) { f.absorbed = true; break; }
            // ---- places: failures of one class within 25 m are one place ----
            var cells = new Dictionary<(string, long), int>();
            int places = 0;
            var order = new List<LgFail>(lgFails);
            order.Sort((a, b) => b.vis.CompareTo(a.vis));
            var placeRep = new List<LgFail>();
            foreach (var f in order)
            {
                if (f.absorbed) { f.place = -1; continue; }
                int cx = (int)Math.Floor(f.x / 25.0), cz = (int)Math.Floor(f.z / 25.0);
                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (cells.TryGetValue((f.cls, ((long)(cx + dx) << 32) ^ (uint)(cz + dz)), out int pl))
                        {
                            var rep = placeRep[pl];
                            if ((rep.x - f.x) * (rep.x - f.x) + (rep.z - f.z) * (rep.z - f.z) <= 25.0 * 25.0) found = pl;
                        }
                if (found < 0) { found = places++; placeRep.Add(f); cells[(f.cls, ((long)cx << 32) ^ (uint)cz)] = found; }
                else placeRep[found].lines++;
                f.place = found;
            }
            // ---- totals ----
            var byCls = new Dictionary<string, int[]>();   // [places, fails, T1 places, T2, T3]
            foreach (var c in LgClasses) byCls[c] = new int[5];
            foreach (var f in lgFails) if (!f.absorbed) byCls[f.cls][1]++;
            foreach (var f in placeRep) { byCls[f.cls][0]++; byCls[f.cls][1 + Mathf.Clamp(f.tier, 1, 3)]++; }
            Line("TOTALS (places = failures of one class within 25 m; every failure is in the CSV):");
            int totalPlaces = 0;
            foreach (var c in LgClasses)
            {
                var v = byCls[c];
                totalPlaces += v[0];
                Line($"  {c,-8} {v[0],6} places ({v[1]} line failures)  T1 highways+primary {v[2]}, T2 minor roads {v[3]}, T3 neighbourhood {v[4]}");
            }
            Line($"  ALL      {totalPlaces,6} places");
            // by sub-class
            var bySub = new SortedDictionary<string, int>();
            foreach (var f in placeRep) { string k = f.cls + " / " + f.sub; bySub[k] = bySub.TryGetValue(k, out int v) ? v + 1 : 1; }
            Line("BY KIND:");
            foreach (var kv in bySub) Line($"  {kv.Key}: {kv.Value}");
            // per road
            var byRoad = new Dictionary<string, int[]>();
            foreach (var f in placeRep)
            {
                if (!byRoad.TryGetValue(f.road, out var v)) byRoad[f.road] = v = new int[LgClasses.Length + 1];
                v[0]++; v[1 + Array.IndexOf(LgClasses, f.cls)]++;
            }
            var roads = new List<KeyValuePair<string, int[]>>(byRoad);
            roads.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            Line($"PER ROAD (the {Math.Min(60, roads.Count)} with the most failing places of {roads.Count}; all in city_linegate_roads_totals.tsv):");
            var rt = new StringBuilder("road\tplaces");
            foreach (var c in LgClasses) rt.Append('\t').Append(c);
            rt.Append('\n');
            for (int i = 0; i < roads.Count; i++)
            {
                var v = roads[i].Value;
                var sb = new StringBuilder();
                for (int c = 0; c < LgClasses.Length; c++) if (v[1 + c] > 0) sb.Append($" {LgClasses[c]} {v[1 + c]}");
                if (i < 60) Line($"  {roads[i].Key}: {v[0]} -{sb}");
                rt.Append(roads[i].Key).Append('\t').Append(v[0]);
                for (int c = 0; c < LgClasses.Length; c++) rt.Append('\t').Append(v[1 + c]);
                rt.Append('\n');
            }
            File.WriteAllText(Path.Combine(root, "city_linegate_roads_totals.tsv"), rt.ToString());
            // the worst: SPOTS (the failures of every class within 40 m are one spot; its score the most
            // visible of them, a little more for each further class there)
            placeRep.Sort((a, b) => b.vis.CompareTo(a.vis));
            var spots = new List<(LgFail top, List<LgFail> all)>();
            {
                var sg = new Dictionary<long, List<int>>();
                foreach (var f in placeRep)
                {
                    int cx = (int)Math.Floor(f.x / 40.0), cz = (int)Math.Floor(f.z / 40.0);
                    int found = -1;
                    for (int dx = -1; dx <= 1 && found < 0; dx++)
                        for (int dz = -1; dz <= 1 && found < 0; dz++)
                            if (sg.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                                foreach (int si in l) { var q = spots[si].top; if ((q.x - f.x) * (q.x - f.x) + (q.z - f.z) * (q.z - f.z) <= 40.0 * 40.0) { found = si; break; } }
                    if (found < 0)
                    {
                        found = spots.Count; spots.Add((f, new List<LgFail> { f }));
                        long k = ((long)cx << 32) ^ (uint)cz;
                        if (!sg.TryGetValue(k, out var l2)) sg[k] = l2 = new List<int>(2);
                        l2.Add(found);
                    }
                    else spots[found].all.Add(f);
                }
            }
            float Score((LgFail top, List<LgFail> all) sp)
            {
                var cls = new HashSet<string>(); foreach (var f in sp.all) cls.Add(f.cls);
                return sp.top.vis * (1f + 0.15f * (cls.Count - 1));
            }
            spots.Sort((x, y) => Score(y).CompareTo(Score(x)));
            // THE SHOWN LIST: the spots in order, at most 5 of one road and 12 led by one kind of failure, so one
            // road's repeated fault does not fill the sheets (every spot, in order, is in city_linegate_worst.tsv)
            int nWorst = 80;
            if (int.TryParse(Environment.GetEnvironmentVariable("PSX_LG_PLANS"), out int nw)) nWorst = nw;
            var shown = new List<int>(nWorst);
            {
                var perRoad = new Dictionary<string, int>(); var perKind = new Dictionary<string, int>();
                for (int i = 0; i < spots.Count && shown.Count < nWorst; i++)
                {
                    var f = spots[i].top; string kk = f.cls + "/" + f.sub;
                    perRoad.TryGetValue(f.road, out int nr); perKind.TryGetValue(kk, out int nk);
                    if (nr >= 5 || nk >= 12) continue;
                    perRoad[f.road] = nr + 1; perKind[kk] = nk + 1;
                    shown.Add(i);
                }
            }
            Line($"THE WORST SPOTS of {spots.Count} (a spot = every failure within 40 m; by what a driver sees: speed x how far past the limit x size; shown: the first {Math.Min(60, shown.Count)} with at most 5 a road and 12 a kind):");
            var wt = new StringBuilder("rank\tid\tx\tz\tedge\ts\troad\tmph\tscore\tlatlon\tclasses\ttext1\ttext2\ttext3\tshown\n");
            var shotList = new StringBuilder("id\tx\tz\tedge\ts\tdir\n");
            var shownAt = new Dictionary<int, int>();
            for (int k = 0; k < shown.Count; k++) shownAt[shown[k]] = k + 1;
            for (int i = 0; i < spots.Count; i++)
            {
                var sp = spots[i]; var f = sp.top;
                bool isShown = shownAt.TryGetValue(i, out int sk);
                if (i >= 400 && !isShown) continue;
                var cls = new List<string>(); foreach (var q in sp.all) if (!cls.Contains(q.cls)) cls.Add(q.cls);
                if (isShown && sk <= 60) Line($"  {sk,2}. (rank {i + 1}) [{string.Join(" + ", cls)}] {f.road} e{f.edge} s {f.s:0}-{f.s1:0} ({LatLon((float)f.x, (float)f.z)}; x {f.x:0.0} z {f.z:0.0}) {f.mph:0} mph: {f.what}");
                string T(int k) => k < sp.all.Count ? (sp.all[k].cls + ": " + sp.all[k].what).Replace('\t', ' ') : "";
                string id = isShown ? "w" + sk.ToString("000") : "r" + (i + 1).ToString("0000");
                wt.Append(i + 1).Append('\t').Append(id).Append('\t').Append(f.x.ToString("0.0", LgInv)).Append('\t').Append(f.z.ToString("0.0", LgInv)).Append('\t').Append(f.edge).Append('\t').Append((0.5f * (f.s + f.s1)).ToString("0.0", LgInv)).Append('\t')
                  .Append(f.road).Append('\t').Append(f.mph.ToString("0", LgInv)).Append('\t').Append(Score(sp).ToString("0.0", LgInv)).Append('\t').Append(LatLon((float)f.x, (float)f.z)).Append('\t').Append(string.Join("+", cls)).Append('\t')
                  .Append(T(0)).Append('\t').Append(T(1)).Append('\t').Append(T(2)).Append('\t').Append(isShown ? sk : 0).Append('\n');
                if (isShown) shotList.Append(id).Append('\t').Append(f.x.ToString("0.0", LgInv)).Append('\t').Append(f.z.ToString("0.0", LgInv)).Append('\t').Append(f.edge).Append('\t').Append((0.5f * (f.s + f.s1)).ToString("0.0", LgInv)).Append("\t0\n");
            }
            File.WriteAllText(Path.Combine(root, "city_linegate_worst.tsv"), wt.ToString());
            File.WriteAllText(Path.Combine(root, "city_linegate_shots.tsv"), shotList.ToString());
            // ---- CSV: every failure ----
            var csv = new StringBuilder("class,kind,place,road,tier,edge,node,s0,s1,x,z,lat,lon,mph,measure,limit,unit,visibility,cause,line,absorbed,what\n");
            foreach (var f in order)
            {
                string ll = LatLon((float)f.x, (float)f.z);
                csv.Append(f.cls).Append(',').Append(f.sub.Replace(',', ';')).Append(',').Append(f.place).Append(',').Append('"').Append(f.road.Replace('"', '\'')).Append('"').Append(',').Append(f.tier).Append(',')
                   .Append(f.edge).Append(',').Append(f.node).Append(',').Append(f.s.ToString("0.0", LgInv)).Append(',').Append(f.s1.ToString("0.0", LgInv)).Append(',')
                   .Append(f.x.ToString("0.00", LgInv)).Append(',').Append(f.z.ToString("0.00", LgInv)).Append(',').Append(ll).Append(',').Append(f.mph.ToString("0", LgInv)).Append(',')
                   .Append(f.measure.ToString("0.###", LgInv)).Append(',').Append(f.limit.ToString("0.###", LgInv)).Append(',').Append(f.unit).Append(',').Append(f.vis.ToString("0.00", LgInv)).Append(',')
                   .Append(f.cls == "SJOG" || f.cls == "SHIFT" || f.cls == "KINK" ? (f.model ? "model" : "map") : "").Append(f.cause.Length > 0 ? ":" + f.cause.Replace(',', ';') : "").Append(',').Append((f.line ?? "").Replace(',', ';')).Append(',').Append(f.absorbed ? 1 : 0).Append(',')
                   .Append('"').Append((f.what ?? "").Replace('"', '\'')).Append('"').Append('\n');
            }
            File.WriteAllText(Path.Combine(root, "city_linegate.csv"), csv.ToString());
            // ---- JSON: the totals the baseline compares ----
            var js = new StringBuilder();
            js.Append("{\n  \"tag\": \"").Append(tag).Append("\",\n  \"graph\": \"").Append(map.graphHash.ToString("x8")).Append("\",\n  \"whole\": ").Append(whole ? "true" : "false").Append(",\n  \"tiles\": ").Append(lgTiles).Append(",\n  \"classes\": {\n");
            for (int c = 0; c < LgClasses.Length; c++)
            {
                var v = byCls[LgClasses[c]];
                js.Append($"    \"{LgClasses[c]}\": {{ \"places\": {v[0]}, \"failures\": {v[1]}, \"T1\": {v[2]}, \"T2\": {v[3]}, \"T3\": {v[4]} }}{(c + 1 < LgClasses.Length ? "," : "")}\n");
            }
            js.Append("  },\n  \"kinds\": {\n");
            int kq = 0;
            foreach (var kv in bySub) js.Append($"    \"{kv.Key}\": {kv.Value}{(++kq < bySub.Count ? "," : "")}\n");
            js.Append("  }\n}\n");
            File.WriteAllText(Path.Combine(root, "city_linegate.json"), js.ToString());
            // ---- the plans: every strand of the edges round the worst places (and the spot) ----
            int nPlans = 80;
            if (int.TryParse(Environment.GetEnvironmentVariable("PSX_LG_PLANS"), out int np)) nPlans = np;
            var want = new List<(double x, double z, string id)>();
            if (spotDump != null) want.Add((spot.x, spot.y, "spot"));
            for (int i = 0; i < Math.Min(nPlans, shown.Count); i++) want.Add((spots[shown[i]].top.x, spots[shown[i]].top.z, "w" + (i + 1).ToString("000")));
            string extra = Environment.GetEnvironmentVariable("PSX_LG_PLAN_AT");   // "x,z,id;x,z,id"
            if (!string.IsNullOrWhiteSpace(extra))
                foreach (var q in extra.Split(';')) { var c = q.Split(','); if (c.Length >= 3) want.Add((double.Parse(c[0], LgInv), double.Parse(c[1], LgInv), c[2])); }
            var plan = new StringBuilder("id\tstrand\tedge\trole\tkind\tyellow\tbroken\tseq\tx\tz\ts\n");
            const double PlanR = 110.0;
            foreach (var st in lgStrands)
            {
                foreach (var (wx, wz, id) in want)
                {
                    bool near = false;
                    foreach (var p in st.p) if ((p.x - wx) * (p.x - wx) + (p.z - wz) * (p.z - wz) < PlanR * PlanR) { near = true; break; }
                    if (!near) continue;
                    for (int i = 0; i < st.p.Count; i++)
                    {
                        var p = st.p[i];
                        if ((p.x - wx) * (p.x - wx) + (p.z - wz) * (p.z - wz) > (PlanR + 30) * (PlanR + 30)) continue;
                        plan.Append(id).Append('\t').Append(st.id).Append('\t').Append(st.edge).Append('\t').Append(st.role).Append('\t').Append(st.tk).Append('\t').Append(st.yellow ? 1 : 0).Append('\t').Append(st.broken ? 1 : 0).Append('\t').Append(i).Append('\t')
                            .Append(p.x.ToString("0.000", LgInv)).Append('\t').Append(p.z.ToString("0.000", LgInv)).Append('\t').Append(p.s.ToString("0.00", LgInv)).Append('\n');
                    }
                }
            }
            // the fans near them
            foreach (var fa in lgFans)
                foreach (var (wx, wz, id) in want)
                {
                    if ((fa.cx - wx) * (fa.cx - wx) + (fa.cz - wz) * (fa.cz - wz) > (PlanR + 20) * (PlanR + 20)) continue;
                    for (int i = 0; i < fa.x.Length; i++)
                        plan.Append(id).Append('\t').Append(-1 - fa.node).Append('\t').Append(-1).Append('\t').Append(9).Append('\t').Append(0).Append('\t').Append(0).Append('\t').Append(0).Append('\t').Append(i).Append('\t')
                            .Append(fa.x[i].ToString("0.000", LgInv)).Append('\t').Append(fa.z[i].ToString("0.000", LgInv)).Append('\t').Append(0).Append('\n');
                }
            File.WriteAllText(Path.Combine(root, "city_linegate_plans.tsv"), plan.ToString());
            // ---- the road map (for the heat map's background): every edge's line, thinned ----
            if (whole || Environment.GetEnvironmentVariable("PSX_LG_ROADS") == "1")
            {
                var rd = new StringBuilder("edge\tcls\tlink\tseq\tx\tz\n");
                foreach (var e in map.edges)
                {
                    Vector2 last = e.pts[0];
                    for (int i = 0; i < e.pts.Length; i++)
                    {
                        if (i > 0 && i < e.pts.Length - 1 && (e.pts[i] - last).sqrMagnitude < 30f * 30f) continue;
                        last = e.pts[i];
                        rd.Append(e.index).Append('\t').Append(e.cls).Append('\t').Append(e.link ? 1 : 0).Append('\t').Append(i).Append('\t').Append(e.pts[i].x.ToString("0.0", LgInv)).Append('\t').Append(e.pts[i].y.ToString("0.0", LgInv)).Append('\n');
                    }
                }
                File.WriteAllText(Path.Combine(root, "city_linegate_roads.tsv"), rd.ToString());
            }
            if (spotDump != null)
            {
                // the strands near the spot, as (s, lat) tables
                foreach (var st in lgStrands)
                {
                    bool near = false;
                    foreach (var p in st.p) if ((p.x - spot.x) * (p.x - spot.x) + (p.z - spot.y) * (p.z - spot.y) < spotR * spotR) { near = true; break; }
                    if (!near) continue;
                    var sb = new StringBuilder($"strand\t{st.id}\te{st.edge}\t{st.Kind}\tpw {st.pw:0.00}\tbroken {st.broken}\tlinks {st.linkA}/{st.linkB}\t");
                    float lastS = float.MinValue;
                    foreach (var p in st.p) { if (p.s - lastS < 4f && p.s != st.S1) continue; lastS = p.s; sb.Append($" {p.s:0}:{p.lat:0.00}"); }
                    spotDump.AppendLine(sb.ToString());
                }
                File.WriteAllText(Path.Combine(root, "city_linegate_spot.tsv"), spotDump.ToString());
            }
            if (Environment.GetEnvironmentVariable("PSX_LG_FOOD") == "1")
            {
                // the restaurants the free-roam HUD points at (the owner's "STACK BURGER 2.2 km")
                var fd = new StringBuilder("kind\tname\tx\tz\n");
                foreach (var kv in CityBuildings.Precompute(map))
                    foreach (var b in kv.Value)
                        if (CityProps.IsFood(b.kind)) fd.Append(b.kind).Append('\t').Append(CityProps.FoodName(b.kind)).Append('\t').Append(b.pos.x.ToString("0.0", LgInv)).Append('\t').Append(b.pos.y.ToString("0.0", LgInv)).Append('\n');
                File.WriteAllText(Path.Combine(root, "city_linegate_food.tsv"), fd.ToString());
            }
            Line($"  not judged for shape: {lgChannelChains} channelizing lines (solid white where the road's own layout has no line: a gore's side, a turning roadway's edge)");
            Line($"  read: {lgPolys.Count} pavement polygons ({lgUntappedTris} triangles of other emitters), {lgSeamSpans} span sides are seams with other pavement, {lgMarks} short marks, {lgSolidLane} solid lane / channelizing lines cut from the edge-line column; {lgSharpJoins} candidate joins refused for meeting at more than 45 deg (a gore's V tip)");
            Line($"  SURFACE census: {lgStretchedM:0} m of ribbon with a pavement strip stretched or squeezed past 1.25x; {lgStripeQuads} thin strips ({lgStripeM:0} m: the gap inside a double line, the strip beside a curb) drawn from a whole lane band - not failed; {lgDoubleSpans} spans drawn twice");
            Line($"  passed by rule: {lgKinkFacets} curve facets (a deflection over 4 m chords gone over 8 / 16 m chords: a curve drawn as a polyline, or a facet within 2.5 cm of its curve); {lgUnderOther} sharp kinks or S-jogs (10 / 45 deg and more) of paint lying on another road's ribbon or within 60 m of a ramp's diverge / merge (its gore's V); {lgGradeFacets} grade breaks gone over 20 / 40 m chords (a vertical curve drawn in facets); {lgTerminalPassed} pavement-edge moves at freeway ramp terminals within the AASHTO terminal taper ({LineGateRules.TerminalRate:0}:1); {lgKinkCorners} kinks ({lgKinkGore} more at a gore's nose) and {lgStepCorners} pavement-edge steps at junction corners (15 m from the drawn end at a node of 1 or 3+ arms: the mouth strip and its curb return); {lgEndsTypeChange} line ends inside an edge and {lgJointRetyped} at a joint where another line of another pattern continues it (MUTCD 2009 3B.04 / 3A.06: a pattern change); {lgEndsAtJunction} line ends within 25 m of a junction (the stop line or crosswalk: MUTCD 2009 3B.16 / 3B.18); {lgEndsEdgeLine} edge-line ends inside an edge (MUTCD 2009 3B.06 / 3B.07: edge lines may stop at curbs, driveways, openings); {lgCtlEqual} side roads with no control meeting a road of their own class (MUTCD 2009 2B.04: the basic right-of-way rule); {lgMouthPassed} road mouths whose nearest solid line is the far side's, a centre yellow (a left turn) or a split's own yellow");
        }
    }
}
