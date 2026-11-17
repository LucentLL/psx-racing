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

        /// <summary>A measured taper may be 10 % short before it fails (the
        /// measurement's own tolerance: the 1 cm onset threshold).</summary>
        public const float TaperTol = 0.90f;
        /// <summary>A move smaller than this is not a taper (SJOG judges the
        /// small ones).</summary>
        public const float ShiftMinM = 0.30f;

        /// <summary>The deflection a line may take at a point with no curve.
        /// AASHTO Green Book 3.3.13: a deflection without a curve only where
        /// it is too small to show (it gives curve LENGTHS for 5 degrees and
        /// under so that no kink appears). State practice puts numbers on it;
        /// the one used here is FDOT Design Manual 210.8.1: 2 deg 00' at 40 mph
        /// or less, 0 deg 45' at 45 mph or more.</summary>
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
        /// <summary>A paved shoulder wider than 3.7 m (12 ft, Green Book 4.4.2
        /// the widest) is unexplained width.</summary>
        public const float ShoulderMaxM = 3.7f;
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
        struct LgPoly { public float x0, z0, x1, z1, x2, z2, x3, z3, y; public int edge; }
        static readonly List<LgPoly> lgPolys = new List<LgPoly>(1 << 18);
        static readonly Dictionary<long, List<int>> lgPolyGrid = new Dictionary<long, List<int>>(1 << 16);
        const float LgPolyCell = 16f;
        static int lgUntappedTris;

        static void LgAddPoly(int edge, double x0, double z0, double x1, double z1, double x2, double z2, double x3, double z3, float y)
        {
            int id = lgPolys.Count;
            lgPolys.Add(new LgPoly { edge = edge, x0 = (float)x0, z0 = (float)z0, x1 = (float)x1, z1 = (float)z1, x2 = (float)x2, z2 = (float)z2, x3 = (float)x3, z3 = (float)z3, y = y });
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
        sealed class LgFan { public int node; public double cx, cz; public float cy; public double[] x, z; public float[] y; public ulong mouths; }

        struct LgP { public double x, z; public float y, s, lat; public int e; }
        sealed class LgStrand
        {
            public int id, edge; public byte role;   // 0 pavement edge on the minus side (right of a->b), 1 on the plus side, 2 paint
            public byte tk; public bool yellow, broken; public float pw;
            public readonly List<LgP> p = new List<LgP>(16);
            public int linkA = -1, linkB = -1;      // the strand joined at this one's start / end
            public bool used;
            public float S0 => p[0].s; public float S1 => p[p.Count - 1].s;
            public string Kind => role == 0 ? "pavement edge (right)" : role == 1 ? "pavement edge (left)" : role == 3 ? "mark" :
                tk == LgSolidWhite ? (pw >= 0.18f ? "wide solid white line" : "solid white lane line") : tk == LgSolidYellow ? "solid yellow line" :
                tk == LineModel.KEdgeM ? "edge line (right)" : tk == LineModel.KEdgeP ? (yellow ? "yellow edge line (left)" : "edge line (left)") :
                tk == LineModel.KYellow ? "centre yellow" : tk == LineModel.KYellowDash ? "broken yellow" : "lane line";
            public bool EdgeLine => role == 2 && (tk == LineModel.KEdgeM || tk == LineModel.KEdgeP);
            public bool Boundary => role != 2 || EdgeLine;
        }

        const byte LgSolidWhite = 20, LgSolidYellow = 21;

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
                if (toEdge > Mathf.Max(e.shl, e.shr) + 0.5f) { st.tk = st.yellow ? LgSolidYellow : LgSolidWhite; lgSolidLane++; }
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
        }

        static List<LgPiece>[] lgPieces; static List<LgSpan>[] lgSpans;
        static readonly List<LgGhost> lgGhosts = new List<LgGhost>();
        static readonly List<LgFan> lgFans = new List<LgFan>();
        static readonly List<LgStrand> lgStrands = new List<LgStrand>();
        static List<int>[] lgByEdge;
        static readonly List<LgFail> lgFails = new List<LgFail>();
        static int lgTapNotes, lgQuads, lgPaintQuads, lgGlyphQuads, lgDoubleSpans, lgTiles, lgSeamSpans, lgMarks, lgSolidLane;
        static double lgStretchedM;
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
                              o.y + 0.5f * (verts[i].y + verts[il + 2].y));
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
                        if (wq < 0.5f && squeeze < 0.34f)
                            lgGhosts.Add(new LgGhost { edge = sp.edge, s0 = sA, s1 = sB, w = wq, squeeze = squeeze, x = 0.25 * (alx + arx + blx + brx), z = 0.25 * (alz + arz + blz + brz) });
                        else if (wq >= 1f && (squeeze > 2f || squeeze < 0.5f)) lgStretchedM += sB - sA;
                    }
                    if (dump)
                        spotDump.AppendLine(string.Format(LgInv, "quad\t{0}\t{1:0.00}\t{2:0.00}\t{3}\t{4:0.000}\t{5:0.000}\t{6:0.0000}\t{7:0.0000}\t{8}\t{9:0.00}\t{10:0.00}\t{11:0.00}\t{12:0.00}\t{13:x}\t{14:x}",
                            sp.edge, sA, sB, k, LgLat(e, sA, alx, alz), LgLat(e, sA, arx, arz), uL, uR, painted ? "paint" : "pave", wA, wB, squeeze, 0.5 * (alx + arx), sp.flagsA, sp.flagsB));
                }
                if (any)
                {
                    (lgSpans[sp.edge] ??= new List<LgSpan>(16)).Add(span);
                    LgAddPoly(sp.edge, span.alx, span.alz, span.blx, span.blz, span.brx, span.brz, span.arx, span.arz, 0.25f * (span.yAL + span.yBL + span.yBR + span.yAR));
                }
                else
                {
                    int i = b + sp.bucketV, il = i + 4 * (nq - 1);
                    LgAddPoly(sp.edge, (double)o.x + verts[i].x, (double)o.z + verts[i].z, (double)o.x + verts[i + 1].x, (double)o.z + verts[i + 1].z,
                              (double)o.x + verts[il + 2].x, (double)o.z + verts[il + 2].z, (double)o.x + verts[il + 3].x, (double)o.z + verts[il + 3].z,
                              o.y + 0.5f * (verts[i].y + verts[il + 2].y));
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
            var tris = tm.roads.triangles;
            for (int q = 0; q + 2 < tris.Length; q += 3)
            {
                int a = tris[q], b2 = tris[q + 1], c = tris[q + 2];
                bool fan = tapped[a] && tapped[b2] && tapped[c];
                // a tapped quad's own two triangles are its span's polygon already; a fan's are added here
                if (fan && !LgIsFanVert(tap, tm, a)) continue;
                if (!fan) lgUntappedTris++;
                LgAddPoly(fan ? -3 : -1, (double)o.x + verts[a].x, (double)o.z + verts[a].z, (double)o.x + verts[b2].x, (double)o.z + verts[b2].z,
                          (double)o.x + verts[c].x, (double)o.z + verts[c].z, (double)o.x + verts[c].x, (double)o.z + verts[c].z, o.y + (verts[a].y + verts[b2].y + verts[c].y) / 3f);
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
                                    if (Mathf.Abs(t.s - sp.sA) <= 0.02f && d <= LineGateRules.JumpMaxM)
                                        lgFails.Add(new LgFail { cls = "CONT", sub = "pavement edge steps", edge = ei, s = sp.sA, s1 = sp.sA, x = ax, z = az, measure = (float)d, limit = LineGateRules.JoinM, unit = "m", line = st.Kind,
                                                                 what = $"the pavement edge steps {d:0.00} m sideways between two spans of one edge" });
                                    st = null;
                                }
                            }
                            if (st == null) { st = LgNew(ei, (byte)side, 0, false, 0f); st.p.Add(new LgP { x = ax, z = az, y = ya, s = sp.sA, lat = la, e = ei }); }
                            st.p.Add(new LgP { x = bx, z = bz, y = yb, s = sp.sB, lat = lb, e = ei });
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
                            if (o.role == 2 && o.yellow != st.yellow) continue;
                            if ((oe ? o.linkB : o.linkA) >= 0) continue;
                            var eb = map.edges[o.edge];
                            if (LgEndNode(eb, oe ? o.S1 : o.S0, Slack(o)) != na) continue;
                            var q = oe ? o.p[o.p.Count - 1] : o.p[0];
                            double vx = q.x - p.x, vz = q.z - p.z;
                            double d = Math.Sqrt(vx * vx + vz * vz);
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
                        if (st.id < id)
                            lgFails.Add(new LgFail { cls = "CONT", sub = st.role == 2 ? "line steps at a joint" : "pavement edge steps at a joint", edge = st.edge, node = node, s = p.s, s1 = p.s, x = p.x, z = p.z, measure = (float)d, limit = dash ? 0.15f : LineGateRules.JoinM, unit = "m", line = st.Kind,
                                                     what = $"the {st.Kind} steps {d:0.00} m sideways where e{st.edge} meets e{other}" });
                    }
                    else if (st.role == 2 && !st.EdgeLine && !LgWidthChanging(st.edge, p.s) && !LgWidthChanging(other, map.edges[other].a == node ? 1f : map.edges[other].length - 1f))
                    {
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
                // no lateral frame across a change of edge direction
                double kw = 2 * LgTurn(r, i, b) - LgTurn(r, i, 2 * b);
                double kl = 0;
                bool oneFrame = true;
                for (int q = i - 2 * b; q < i + 2 * b; q++) if (r[q].e != r[q + 1].e && Mathf.Abs(r[q].lat - r[q + 1].lat) > 0.2f) { oneFrame = false; break; }
                if (oneFrame) kl = 2 * LgTurnLat(r, i, b) - LgTurnLat(r, i, 2 * b);
                double k = Math.Abs(kw - kl);
                var e = map.edges[r[i].e];
                float mph = LineGateRules.Mph(e), lim = LineGateRules.KinkDeg(mph);
                if (k <= lim) continue;
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
                lgFails.Add(new LgFail { cls = "KINK", sub = st0.role == 2 ? "painted line" : "pavement edge", edge = e.index, s = r[i].s, s1 = r[i].s, x = r[i].x, z = r[i].z, measure = (float)k, limit = lim, unit = "deg", mph = mph, line = kind,
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
                double need = rate * W;
                if (D >= LineGateRules.TaperTol * need) continue;
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
                                         what = $"the {kind} swings {Math.Max(a1, a2):0.0} deg and back, {W:0.00} m sideways in {D:0} m; at {mph:0} mph that shift needs {need:0} m (MUTCD L = W x {rate:0.0})" });
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
                    string type = LgShiftType(map, e, st0, r[ja], r[jb], W, out bool through);
                    float rate = type == "add" ? LineGateRules.BayRate(mph) : LineGateRules.TaperRate(mph);
                    float need = rate * Mathf.Abs(W);
                    if (L >= LineGateRules.TaperTol * need) return;
                    string rule = type == "add" ? $"AASHTO bay taper {rate:0.0}:1" : $"MUTCD L = W x {rate:0.0}";
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
        /// with it, or it is a lane line itself: the travelled lanes shift).</summary>
        static string LgShiftType(CityMap map, CityMap.Edge e, LgStrand st, LgR a, LgR b, float W, out bool through)
        {
            through = false;
            if (a.e != b.e || a.e != e.index) { through = !st.Boundary; return st.Boundary ? "drop" : "shift"; }
            // the chain may run against the edge: put the two stations back in the edge's own frame
            float sa = a.s, sb = b.s;
            // (lat is negated on a reversed walk; the strand's own points are in the edge frame)
            float Wd = LgLatAt(st, sb) - LgLatAt(st, sa);
            int moved = 0, held = 0;
            var ids = lgByEdge[e.index];
            if (ids != null)
                foreach (int id in ids)
                {
                    var o = lgStrands[id];
                    if (o == st || o.role != 2 || o.EdgeLine) continue;
                    float s0 = Mathf.Min(sa, sb), s1 = Mathf.Max(sa, sb);
                    if (o.S0 > s0 + 0.5f || o.S1 < s1 - 0.5f) continue;
                    float d = LgLatAt(o, sb) - LgLatAt(o, sa);
                    if (Mathf.Abs(d) >= 0.5f * Mathf.Abs(Wd) && d * Wd > 0) moved++; else held++;
                }
            if (!st.Boundary || moved > held) { through = true; return "shift"; }
            // a boundary moving alone: outward along its side's travel direction = a lane opens
            float mid = 0.5f * (LgLatAt(st, sa) + LgLatAt(st, sb));
            bool plusSide = mid > 0f;                       // left of a->b
            bool outward = plusSide ? Wd > 0f : Wd < 0f;    // going from sa to sb
            bool travelAB = e.oneway || !plusSide;          // the right side travels a->b; the left side b->a on a two-way
            bool opening = (sb > sa) == travelAB ? outward : !outward;
            return opening ? "add" : "drop";
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
                            else if (clear < LineGateRules.LaneMinM && clear > 0.6f && !L.yellow && L.pw < 0.18f) { k = "curb lane too narrow"; lm = LineGateRules.LaneMinM; wh = $"{clear:0.0} m between the {L.Kind} and the pavement edge"; }
                        }
                        else if (A.yellow && B.yellow)
                        {
                            // between two yellows: a two-way left-turn lane or a painted median; a double line is one line
                            if (clear > 0.6f && clear < LineGateRules.TwltlMinM && (A.broken || B.broken)) { k = "two-way left-turn lane too narrow"; lm = LineGateRules.TwltlMinM; wh = $"{clear:0.0} m between the yellow lines of a two-way left-turn lane"; }
                        }
                        else if (A.pw >= 0.18f && B.pw >= 0.18f) { }                     // between two channelizing lines: a gore
                        else if (e.lsBike != 0 && clear >= 1.0f && clear <= 2.5f && (i == 0 || i + 2 == at.Count || at[i - 1].st.role != 2 || at[i + 2].st.role != 2)) { }   // a bike lane (AASHTO: 4 ft or more)
                        else
                        {
                            float max = e.link && e.lanes <= 1 ? LineGateRules.RampLaneMaxM : LineGateRules.LaneMaxM;
                            if (clear > max) { k = "lane too wide"; lm = max; wh = $"{clear:0.0} m between the {A.Kind} and the {B.Kind} (a lane is at most {max:0.0} m)"; }
                            else if (clear < LineGateRules.LaneMinM && clear > 0.6f) { k = "lane too narrow"; lm = LineGateRules.LaneMinM; wh = $"{clear:0.0} m between the {A.Kind} and the {B.Kind} (a lane is at least {lm:0.0} m)"; }
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
        static bool LgHolds(LgStrand A, LgStrand B, float s, float w)
        {
            for (int q = -1; q <= 1; q += 2)
            {
                float s2 = s + 15f * q;
                float sa = A.role == 2 && A.broken ? 9.6f : 0.01f, sb = B.role == 2 && B.broken ? 9.6f : 0.01f;
                if (A.S0 > s2 + sa || A.S1 < s2 - sa || B.S0 > s2 + sb || B.S1 < s2 - sb) continue;   // one of them ends: the next edge's
                if (Mathf.Abs(LgLatAt(B, s2) - LgLatAt(A, s2) - w) > 0.3f) return false;
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
                if (st.role != 2) continue;
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
                    bool resumes = false;
                    var ids = lgByEdge[st.edge];
                    foreach (int id in ids)
                    {
                        var o = lgStrands[id];
                        if (o == st || o.role != 2 || o.tk != st.tk) continue;
                        float os = end ? o.S0 : o.S1;
                        float gap = end ? os - p.s : p.s - os;
                        if (gap > 0f && gap <= 40f && Mathf.Abs(LgLatAt(o, os) - p.lat) < 0.3f) { resumes = true; break; }
                    }
                    if (resumes && st.EdgeLine) continue;
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
            lgTapNotes = lgQuads = lgPaintQuads = lgGlyphQuads = lgDoubleSpans = lgTiles = lgSeamSpans = lgMarks = lgSolidLane = lgUntappedTris = 0; lgStretchedM = 0;
            lgPolys.Clear(); lgPolyGrid.Clear(); lgFanTap = null;
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
                LgShape(map, pts, members, rs);
                chainKm += rs.Count / 1000.0;
            }
            LgWidths(map);
            LgEnds(map);
            LgSurface(map);
            Line($"  {lgStrands.Count} strands in {chains} chains ({chainKm:0} km of painted line and pavement edge walked); {clock.Elapsed.TotalSeconds - buildS:0} s to judge");

            // out-of-scope edges (a box run reads neighbours only in part): keep what lies on a road in scope
            lgFails.RemoveAll(f => f.edge >= 0 && f.edge < scope.Length && !scope[f.edge]);
            LgFinish(map, t, root, tag, whole, spotDump, spot, spotR);
            Line($"  total {clock.Elapsed.TotalSeconds:0} s");
            Debug.Log("[LineGate] DONE " + lgFails.Count + " failures");
            File.WriteAllText(Path.Combine(root, "city_linegate.txt"), outLog.ToString());
        }

        /// <summary>SURFACE: pavement texels squeezed into a thin stripe (a
        /// line's column drawn with a whole lane band's texels: it shows as a
        /// streak along the lanes), by run.</summary>
        static void LgSurface(CityMap map)
        {
            lgGhosts.Sort((a, b) => a.edge != b.edge ? a.edge.CompareTo(b.edge) : a.s0.CompareTo(b.s0));
            int i = 0;
            while (i < lgGhosts.Count)
            {
                int j = i; float len = 0f; var g0 = lgGhosts[i];
                while (j < lgGhosts.Count && lgGhosts[j].edge == g0.edge && lgGhosts[j].s0 - lgGhosts[Math.Max(i, j - 1)].s1 < 15f) { len += lgGhosts[j].s1 - lgGhosts[j].s0; j++; }
                var gm = lgGhosts[(i + j - 1) / 2];
                if (len >= 3f)
                    lgFails.Add(new LgFail { cls = "SURFACE", sub = "pavement texels squeezed into a stripe", edge = g0.edge, s = g0.s0, s1 = lgGhosts[j - 1].s1, x = gm.x, z = gm.z, measure = len, limit = 0f, unit = "m",
                                             what = $"{len:0} m of {g0.w * 100f:0} cm strips drawn from a band {g0.w / Mathf.Max(1e-3f, g0.squeeze):0.0} m wide (squeezed {1f / Mathf.Max(1e-3f, g0.squeeze):0}x): a streak along the lanes" });
                i = j;
            }
        }

        static readonly string[] LgClasses = { "SHIFT", "SJOG", "KINK", "WIDTH", "CONT", "SURFACE", "JUNCTION", "MEDIAN", "STEP", "BARRIER", "BRIDGE" };

        static void LgFinish(CityMap map, CityMeshes.Trims t, string root, string tag, bool whole, StringBuilder spotDump, Vector2 spot, float spotR)
        {
            // ---- fill in road, tier, speed; visibility ----
            foreach (var f in lgFails)
            {
                if (f.s1 < f.s) { float sw = f.s; f.s = f.s1; f.s1 = sw; }
                var e = map.edges[Mathf.Clamp(f.edge, 0, map.edges.Length - 1)];
                f.road = LgRoad(e); f.tier = CityTier.Of(e);
                if (f.mph <= 0f) f.mph = LineGateRules.Mph(e);
                float sp = f.mph / 30f;
                switch (f.cls)
                {
                    case "SJOG": case "SHIFT": f.vis = sp * Mathf.Clamp(f.limit / Mathf.Max(1f, f.measure), 1f, 20f) * Mathf.Clamp(f.limit / LineGateRules.TaperRate(f.mph), 0.05f, 8f); break;
                    case "KINK": f.vis = sp * Mathf.Clamp(f.measure / Mathf.Max(0.1f, f.limit), 1f, 20f) * 0.25f; break;
                    case "WIDTH": f.vis = sp * Mathf.Abs(f.measure - f.limit) * 0.5f; break;
                    case "CONT": f.vis = sp * (f.measure > 0f ? 2f + 4f * f.measure : 1.5f); break;
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
            // the worst
            placeRep.Sort((a, b) => b.vis.CompareTo(a.vis));
            Line("THE WORST 60 PLACES (by what a driver sees: speed x how far past the limit x size):");
            for (int i = 0; i < Math.Min(60, placeRep.Count); i++)
            {
                var f = placeRep[i];
                Line($"  {i + 1,2}. {f.cls} / {f.sub} - {f.road} e{f.edge} s {f.s:0}-{f.s1:0} ({LatLon((float)f.x, (float)f.z)}; x {f.x:0.0} z {f.z:0.0}) {f.mph:0} mph: {f.what} [{f.lines} line(s)]");
            }
            // ---- CSV: every failure ----
            var csv = new StringBuilder("class,kind,place,road,tier,edge,node,s0,s1,x,z,lat,lon,mph,measure,limit,unit,visibility,cause,line,absorbed,what\n");
            foreach (var f in order)
            {
                string ll = LatLon((float)f.x, (float)f.z);
                csv.Append(f.cls).Append(',').Append(f.sub.Replace(',', ';')).Append(',').Append(f.place).Append(',').Append('"').Append(f.road.Replace('"', '\'')).Append('"').Append(',').Append(f.tier).Append(',')
                   .Append(f.edge).Append(',').Append(f.node).Append(',').Append(f.s.ToString("0.0", LgInv)).Append(',').Append(f.s1.ToString("0.0", LgInv)).Append(',')
                   .Append(f.x.ToString("0.00", LgInv)).Append(',').Append(f.z.ToString("0.00", LgInv)).Append(',').Append(ll).Append(',').Append(f.mph.ToString("0", LgInv)).Append(',')
                   .Append(f.measure.ToString("0.###", LgInv)).Append(',').Append(f.limit.ToString("0.###", LgInv)).Append(',').Append(f.unit).Append(',').Append(f.vis.ToString("0.00", LgInv)).Append(',')
                   .Append(f.cls == "SJOG" || f.cls == "SHIFT" || f.cls == "KINK" ? (f.model ? "model" : "map") : "").Append(',').Append((f.line ?? "").Replace(',', ';')).Append(',').Append(f.absorbed ? 1 : 0).Append(',')
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
            for (int i = 0; i < Math.Min(nPlans, placeRep.Count); i++) want.Add((placeRep[i].x, placeRep[i].z, "w" + (i + 1)));
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
            Line($"  read: {lgPolys.Count} pavement polygons ({lgUntappedTris} triangles of other emitters), {lgSeamSpans} span sides are seams with other pavement, {lgMarks} short marks, {lgSolidLane} solid lane / channelizing lines cut from the edge-line column");
            Line($"  SURFACE census: {lgGhosts.Count} squeezed stripe quads, {lgStretchedM:0} m of pavement strips stretched or squeezed more than 2x, {lgDoubleSpans} spans drawn twice");
        }
    }
}
