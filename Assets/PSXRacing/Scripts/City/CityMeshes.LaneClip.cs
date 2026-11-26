using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  c7roads round 3, R2: NOTHING RAISED OR LAID INSIDE A LANE (owner 11-25, CltC7 shots 77-81:
    //  "Some turn lanes have curbs in the middle of the turn lane").
    //
    //  Every generator lays its own edge pieces (verge strips, foreslopes, chord faces, skirts, aprons,
    //  slabs, the lattice) from its own outline, whatever road lies there. This pass runs once at the end of
    //  a tile's build, before its meshes are made, over the ground-level buckets (Ground, Pavement, Sidewalk,
    //  MedianBed, WalkPaver, Concrete, the kerbs and the banks; never a driving surface, a marking, a
    //  structure or a building): the part of any triangle that lies inside a drawn arm's TRAVELLED WAY grown
    //  by LaneClipMarginM (0.15 m, never past the arm's own shoulder less 0.1 m, so an arm's own curb and
    //  verge at its edge stay) and stands at or above its pavement less LaneClipBelowM is cut away (polygon
    //  subtraction in plan, the UVs carried by the triangle's own barycentrics, so no texel stretches). A piece
    //  below the pavement (the ground under a road, a deck's land) is kept. Bands are laid only where the
    //  arm's ribbon is drawn (from its trims, never in a branch clip or a merge zone's cut), so a cut never
    //  opens a hole where no road surface stands. Steep faces (skirts, chord faces) go whole when their
    //  centre lies in a band and their top stands above the pavement less LaneClipBelowM.
    //  PSX_CITY_LANECLIP=0: nothing is cut (the tiles as before).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool LaneClipOn = System.Environment.GetEnvironmentVariable("PSX_CITY_LANECLIP") != "0";
        public const float LaneClipMarginM = 0.15f, LaneClipBelowM = 0.06f, LaneClipStepM = 2f;
        /// <summary>Totals since load: triangles tested, dropped whole, cut; the time.</summary>
        public static long LaneClipTested, LaneClipDropped, LaneClipCut; public static double LaneClipMs;
        /// <summary>Per bucket name: triangles dropped or cut (the report's per-emitter split by slot).</summary>
        public static readonly Dictionary<string, long> LaneClipBySlot = new Dictionary<string, long>();

        struct LBand { public Vector2 p0, p1, p2, p3; public float y, x0, z0, x1, z1; }
        static readonly List<LBand> lBands = new List<LBand>(512);
        static readonly Dictionary<long, List<int>> lGrid = new Dictionary<long, List<int>>();
        static readonly HashSet<int> lSegs = new HashSet<int>();
        static readonly HashSet<int> lEdges = new HashSet<int>();
        static readonly List<float> lStations = new List<float>(64);
        static int[] lStamp = new int[512]; static int lStampN;
        const float LCell = 8f;
        static long LKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        static void LaneClip(CityMap map, Trims trims, Vector2 min, Vector2 max)
        {
            if (!LaneClipOn) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            lBands.Clear(); foreach (var l in lGrid.Values) l.Clear();
            lSegs.Clear(); lEdges.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 12f, max + Vector2.one * 12f, lSegs);
            foreach (int pk in lSegs) lEdges.Add(pk >> 12);
            foreach (int ei in lEdges) LaneBands(map, trims, map.edges[ei], min, max);
            if (lBands.Count > 0)
            {
                foreach (var s in new[] { Slot.Ground, Slot.Pavement, Slot.Sidewalk, Slot.MedianBed, Slot.WalkPaver, Slot.Concrete })
                    ClipBucket(buckets[(int)s], s.ToString());
                ClipBucket(kerbBucket, "kerbs");
                ClipBucket(bankBucket, "banks");
            }
            LaneClipMs += clock.Elapsed.TotalMilliseconds;
        }

        static void LaneBands(CityMap map, Trims trims, CityMap.Edge e, Vector2 min, Vector2 max)
        {
            if (e.a == e.b) return;
            float s0 = trims.atA[e.index], s1 = e.length - trims.atB[e.index];
            if (s1 - s0 < 1f) return;
            lStations.Clear();
            for (float s = s0; s < s1; s += LaneClipStepM) lStations.Add(s);
            for (int k = 1; k + 1 < e.pts.Length; k++) if (e.s[k] > s0 + 0.05f && e.s[k] < s1 - 0.05f) lStations.Add(e.s[k]);
            lStations.Add(s1);
            lStations.Sort();
            bool havePrev = false; Vector2 pL = default, pR = default; float pY = 0f;
            for (int i = 0; i < lStations.Count; i++)
            {
                float s = lStations[i];
                if (i > 0 && s - lStations[i - 1] < 0.05f) continue;
                if (ClipAt(e, s) != null || ZoneCutOwner(e, s) != null) { havePrev = false; continue; }
                LineModel.Extents(e, s, out float eM, out float eP);
                float mP = Mathf.Clamp(e.shl - 0.1f, 0f, LaneClipMarginM), mM = Mathf.Clamp(e.shr - 0.1f, 0f, LaneClipMarginM);
                float plusT = eP - e.shl + mP, minusT = eM - e.shr + mM;
                if (plusT + minusT < 1f) { havePrev = false; continue; }
                var t = e.TangentAt(Mathf.Max(0f, s - 0.01f)) + e.TangentAt(Mathf.Min(e.length, s + 0.01f));
                t = t.sqrMagnitude > 1e-8f ? t.normalized : e.TangentAt(s);
                var left = new Vector2(-t.y, t.x);
                var p = e.PointAt(s);
                Vector2 L = p + left * plusT, R = p - left * minusT;
                float y = e.YAt(s);
                if (havePrev) AddBand(pL, L, R, pR, Mathf.Min(pY, y), min, max);
                pL = L; pR = R; pY = y; havePrev = true;
            }
        }

        static void AddBand(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y, Vector2 min, Vector2 max)
        {
            // anticlockwise in plan (x, z)
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y) + (c.x - a.x) * (d.y - a.y) - (d.x - a.x) * (c.y - a.y);
            if (Mathf.Abs(area) < 1e-3f) return;
            if (area < 0f) { var tmp = b; b = d; d = tmp; }
            var q = new LBand { p0 = a, p1 = b, p2 = c, p3 = d, y = y };
            q.x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)); q.x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            q.z0 = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)); q.z1 = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            if (q.x1 < min.x - 2f || q.x0 > max.x + 2f || q.z1 < min.y - 2f || q.z0 > max.y + 2f) return;
            int idx = lBands.Count; lBands.Add(q);
            for (int cx = Mathf.FloorToInt(q.x0 / LCell); cx <= Mathf.FloorToInt(q.x1 / LCell); cx++)
                for (int cz = Mathf.FloorToInt(q.z0 / LCell); cz <= Mathf.FloorToInt(q.z1 / LCell); cz++)
                {
                    long k = LKey(cx, cz);
                    if (!lGrid.TryGetValue(k, out var l)) lGrid[k] = l = new List<int>(8);
                    l.Add(idx);
                }
            if (lStamp.Length < lBands.Count) System.Array.Resize(ref lStamp, lBands.Count * 2);
        }

        static Vector2 Corner(in LBand q, int i) => i == 0 ? q.p0 : i == 1 ? q.p1 : i == 2 ? q.p2 : q.p3;

        static readonly List<int> lCand = new List<int>(16);
        static readonly List<List<Vector3>> lPieces = new List<List<Vector3>>(8), lNext = new List<List<Vector3>>(8);
        static readonly List<int> lNewT = new List<int>(1024);

        static void ClipBucket(Bucket bk, string name)
        {
            if (bk == null || bk.v.Count == 0) return;
            long before = LaneClipDropped + LaneClipCut;
            ClipList(bk, bk.t);
            if (bk.tn != null) ClipList(bk, bk.tn);
            long d = LaneClipDropped + LaneClipCut - before;
            if (d > 0) { LaneClipBySlot.TryGetValue(name, out long n); LaneClipBySlot[name] = n + d; }
        }

        static void ClipList(Bucket bk, List<int> tl)
        {
            lNewT.Clear();
            var o = tileOrigin;
            int nT = tl.Count;
            bool changed = false;
            for (int k = 0; k + 2 < nT; k += 3)
            {
                int i0 = tl[k], i1 = tl[k + 1], i2 = tl[k + 2];
                Vector3 a = bk.v[i0] + o, b = bk.v[i1] + o, c = bk.v[i2] + o;
                float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                lCand.Clear(); lStampN++;
                for (int cx = Mathf.FloorToInt(x0 / LCell); cx <= Mathf.FloorToInt(x1 / LCell); cx++)
                    for (int cz = Mathf.FloorToInt(z0 / LCell); cz <= Mathf.FloorToInt(z1 / LCell); cz++)
                    {
                        if (!lGrid.TryGetValue(LKey(cx, cz), out var l)) continue;
                        foreach (int bi in l)
                        {
                            if (lStamp[bi] == lStampN) continue;
                            lStamp[bi] = lStampN;
                            var q = lBands[bi];
                            if (q.x1 < x0 || q.x0 > x1 || q.z1 < z0 || q.z0 > z1) continue;
                            lCand.Add(bi);
                        }
                    }
                if (lCand.Count == 0) { lNewT.Add(i0); lNewT.Add(i1); lNewT.Add(i2); continue; }
                LaneClipTested++;
                float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                float ax = 0.5f * ((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z));
                float a3 = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                if (Mathf.Abs(ax) < 0.25f * a3 || Mathf.Abs(ax) < 1e-4f)
                {
                    // a steep face: whole, by its centre
                    var cen = new Vector2((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f);
                    bool drop = false;
                    foreach (int bi in lCand) { var q = lBands[bi]; if (maxY > q.y - LaneClipBelowM && InBand(q, cen)) { drop = true; break; } }
                    if (drop) { LaneClipDropped++; changed = true; continue; }
                    lNewT.Add(i0); lNewT.Add(i1); lNewT.Add(i2); continue;
                }
                // plan polygon subtraction
                lPieces.Clear(); lPieces.Add(new List<Vector3>(3) { a, b, c });
                bool cut = false;
                foreach (int bi in lCand)
                {
                    var q = lBands[bi];
                    lNext.Clear();
                    foreach (var pc in lPieces)
                    {
                        var inside = pc;
                        for (int j = 0; j < 4 && inside.Count >= 3; j++) inside = HalfPlane(inside, Corner(q, j), Corner(q, (j + 1) & 3), true);
                        if (inside.Count < 3 || LcArea(inside) < 1e-4f) { lNext.Add(pc); continue; }
                        float yIn = float.MinValue; foreach (var v in inside) yIn = Mathf.Max(yIn, v.y);
                        if (yIn < q.y - LaneClipBelowM) { lNext.Add(pc); continue; }
                        cut = true;
                        var rest = pc;
                        for (int j = 0; j < 4 && rest.Count >= 3; j++)
                        {
                            var outPart = HalfPlane(rest, Corner(q, j), Corner(q, (j + 1) & 3), false);
                            if (outPart.Count >= 3 && LcArea(outPart) > 1e-4f) lNext.Add(outPart);
                            rest = HalfPlane(rest, Corner(q, j), Corner(q, (j + 1) & 3), true);
                        }
                    }
                    lPieces.Clear(); lPieces.AddRange(lNext);
                    if (lPieces.Count == 0) break;
                }
                if (!cut) { lNewT.Add(i0); lNewT.Add(i1); lNewT.Add(i2); continue; }
                changed = true;
                if (lPieces.Count == 0) { LaneClipDropped++; continue; }
                LaneClipCut++;
                // the pieces, their UVs from the original triangle's barycentrics (plan)
                Vector2 ua = bk.uv[i0], ub = bk.uv[i1], uc = bk.uv[i2];
                float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                foreach (var pc in lPieces)
                {
                    int baseV = bk.v.Count;
                    foreach (var v in pc)
                    {
                        float w0 = ((b.z - c.z) * (v.x - c.x) + (c.x - b.x) * (v.z - c.z)) / den;
                        float w1 = ((c.z - a.z) * (v.x - c.x) + (a.x - c.x) * (v.z - c.z)) / den;
                        float w2 = 1f - w0 - w1;
                        bk.v.Add(v - o);
                        bk.uv.Add(ua * w0 + ub * w1 + uc * w2);
                        if (bk.col != null) bk.col.Add(bk.col[i0]);
                        if (bk.fac != null) bk.fac.Add(bk.fac[i0]);
                    }
                    // the original winding: the pieces keep (a, b, c)'s plan orientation
                    for (int m = 1; m + 1 < pc.Count; m++) { lNewT.Add(baseV); lNewT.Add(baseV + m); lNewT.Add(baseV + m + 1); }
                }
            }
            if (changed) { tl.Clear(); tl.AddRange(lNewT); }
        }

        static bool InBand(in LBand q, Vector2 p)
        {
            for (int j = 0; j < 4; j++)
            {
                var u = Corner(q, j); var w = Corner(q, (j + 1) & 3);
                if ((w.x - u.x) * (p.y - u.y) - (w.y - u.y) * (p.x - u.x) < 0f) return false;
            }
            return true;
        }

        static float LcArea(List<Vector3> p)
        {
            float s = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var w = p[(i + 1) % p.Count]; s += u.x * w.z - w.x * u.z; }
            return Mathf.Abs(0.5f * s);
        }

        /// <summary>The part of a planar polygon on the inside (left, the band being anticlockwise) or the outside of
        /// the plan line u -> w; heights and so every attribute interpolate linearly along the cut edges.</summary>
        static List<Vector3> HalfPlane(List<Vector3> poly, Vector2 u, Vector2 w, bool inside)
        {
            var o = new List<Vector3>(poly.Count + 2);
            float Side(Vector3 p) { float s = (w.x - u.x) * (p.z - u.y) - (w.y - u.y) * (p.x - u.x); return inside ? s : -s; }
            for (int i = 0; i < poly.Count; i++)
            {
                var P = poly[i]; var Q = poly[(i + 1) % poly.Count];
                float sp = Side(P), sq = Side(Q);
                if (sp >= 0f) o.Add(P);
                if ((sp >= 0f) != (sq >= 0f)) { float t = sp / (sp - sq); o.Add(Vector3.LerpUnclamped(P, Q, t)); }
            }
            return o;
        }
    }
}
