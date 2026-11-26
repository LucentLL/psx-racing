using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// c7roads round 5 (Docs/ROAD_RULES.md section 2): THE JUNCTION OUTLINE SOLVER as a pure function. No CityMap: the
    /// caller hands each arm's two border polylines (in its away frame: left / right of travel AWAY from the junction,
    /// sampled every Step metres out from the junction), its class, and the holes (islands) it knows of; the solver
    /// returns one closed outline, each arm's cut (its end row, whose two corners lie on its borders), the corner shapes
    /// and a fill.
    /// Corners, between each arm's LEFT border and the next arm's (anticlockwise) RIGHT border - never a point:
    ///   THROUGH   the two are one road going on (150-210 degrees apart, same name or one-way): straight;
    ///   NOSE      the borders start under NoseGapM apart and part slowly: walk out to where the gap reaches NoseGapM
    ///             (2.4 m: a 1.2 m nose and its 0.6 m offsets), round the land's tip there (radius gap / 2), cut both arms
    ///             at that one distance (one shared line), the gore inside the outline is paved;
    ///   CONNECTOR the borders start apart and never cross (two nodes of one cluster): a tangent-continuous Hermite curve;
    ///   ARC       StreetGen's construction on the real (curved) borders: offset each border by R into the corner,
    ///             intersect the offsets (the centre), the tangent points are the centre's feet; R by class, shrunk to fit.
    /// Every curve is sampled to SagM (1 cm) and, from a 6 m radius, MaxTurnDeg (6 degrees) a joint.
    /// FILL: holes are bridged to the outer ring by zero-width cuts (the polygon becomes simple, every vertex shared) and
    /// ear-clipped; no new triangulator. Slivers (a corner under SliverDeg) are counted.
    /// </summary>
    public static class JunctionOutline
    {
        public const float Step = 0.5f, NoseGapM = 2.4f, ThroughLoDeg = 150f, ThroughHiDeg = 210f, SagM = 0.01f, MaxTurnDeg = 6f, SliverDeg = 5f;

        public sealed class Arm
        {
            public List<Vector2> L = new List<Vector2>(), R = new List<Vector2>(), C = new List<Vector2>();
            public float radius = 9f;          // the class's kerb-return radius
            public bool oneway; public string name = "";
            public int id;                     // the caller's (an edge index)
            internal float ang;
        }
        public sealed class Corner
        {
            public int a, b; public string shape = ""; public float r, deg, dA, dB; public Vector2 tA, tB;
            public List<Vector2> pts = new List<Vector2>(); public string note = "";
        }
        public sealed class Result
        {
            public List<Arm> arms;                       // sorted anticlockwise
            public List<Corner> corners = new List<Corner>();
            public float[] cut;                          // per sorted arm, metres out
            public List<Vector2> outline = new List<Vector2>();
            public List<List<Vector2>> holes = new List<List<Vector2>>();
            public List<int> tris = new List<int>();     // into fillVerts
            public List<Vector2> fillVerts = new List<Vector2>();
            public int slivers; public string refused;   // null: solved
            public double solveMs, fillMs;
        }

        public static Result Solve(List<Arm> arms, List<List<Vector2>> holes = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = new Result { arms = new List<Arm>(arms) };
            if (arms.Count < 2) { res.refused = "fewer than two arms"; return res; }
            var centre = Vector2.zero; foreach (var a in arms) centre += a.C[0]; centre /= arms.Count;
            // arms that start together (one node, or side by side within a road's width) sort by their own heading;
            // arms of a cluster that start apart sort by where they are round the cluster's centre
            float spread = 0f, maxHalf = 0f;
            foreach (var a in arms) { spread = Mathf.Max(spread, (a.C[0] - centre).magnitude); maxHalf = Mathf.Max(maxHalf, Mathf.Max((a.L[0] - a.C[0]).magnitude, (a.R[0] - a.C[0]).magnitude)); }
            bool together = spread <= maxHalf * 1.5f + 1f;
            foreach (var a in arms)
            {
                var p = a.C[Mathf.Min(a.C.Count - 1, Mathf.RoundToInt(12f / Step))];
                var from = together ? a.C[0] : centre;
                a.ang = Mathf.Atan2(p.y - from.y, p.x - from.x) * Mathf.Rad2Deg;
            }
            res.arms.Sort((p, q) => p.ang.CompareTo(q.ang));
            var A = res.arms;
            for (int i = 0; i < A.Count; i++) res.corners.Add(SolveCorner(A[i], A[(i + 1) % A.Count]));
            res.cut = new float[A.Count];
            for (int i = 0; i < A.Count; i++)
            {
                var left = res.corners[i]; var right = res.corners[(i - 1 + A.Count) % A.Count];
                res.cut[i] = Mathf.Max(left.dA, right.dB);
                // a nose cuts BOTH its arms on one shared line
                if (left.shape == "nose") res.cut[(i + 1) % A.Count] = Mathf.Max(res.cut[(i + 1) % A.Count], left.dA);
            }
            for (int i = 0; i < A.Count; i++)
                if (res.corners[i].shape == "nose") { float d = Mathf.Max(res.cut[i], res.cut[(i + 1) % A.Count]); res.cut[i] = d; res.cut[(i + 1) % A.Count] = d; }
            foreach (var c in res.corners) if (c.shape.EndsWith("not-found") || c.shape.EndsWith("did-not-fit")) { res.refused = $"corner {c.a}/{c.b}: {c.shape} ({c.note})"; }
            // the outline: arm by arm, cut row right -> left, in along the left border, the corner, out along the next arm's right border
            var o = res.outline;
            for (int i = 0; i < A.Count; i++)
            {
                var a = A[i]; var c = res.corners[i]; var b = A[(i + 1) % A.Count];
                int ka = Mathf.Min(a.L.Count - 1, Mathf.RoundToInt(res.cut[i] / Step));
                o.Add(a.R[ka]); o.Add(a.L[ka]);
                int ta = Mathf.RoundToInt(c.dA / Step);
                for (int k = ka - 1; k > ta; k--) o.Add(a.L[k]);
                if (c.pts.Count > 0) o.AddRange(c.pts); else { o.Add(c.tA); o.Add(c.tB); }
                int tb = Mathf.RoundToInt(c.dB / Step), kb = Mathf.Min(b.R.Count - 1, Mathf.RoundToInt(res.cut[(i + 1) % A.Count] / Step));
                for (int k = tb + 1; k < kb; k++) o.Add(b.R[k]);
            }
            Dedupe(o);
            if (holes != null) foreach (var h in holes) { var hh = new List<Vector2>(h); Dedupe(hh); res.holes.Add(hh); }
            res.solveMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            Fill(res);
            res.fillMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        static void Dedupe(List<Vector2> o)
        {
            for (int i = o.Count - 1; i > 0; i--) if ((o[i] - o[i - 1]).sqrMagnitude < 1e-6f) o.RemoveAt(i);
            if (o.Count > 1 && (o[0] - o[o.Count - 1]).sqrMagnitude < 1e-6f) o.RemoveAt(o.Count - 1);
        }

        // ---- corners ----
        static Corner SolveCorner(Arm A, Arm B)
        {
            var c = new Corner { a = A.id, b = B.id };
            float dAng = Mathf.Repeat(B.ang - A.ang, 360f);
            c.deg = dAng;
            if (dAng >= ThroughLoDeg && dAng <= ThroughHiDeg && (A.name == B.name || A.oneway || B.oneway))
            { c.shape = "through"; c.tA = A.L[0]; c.tB = B.R[0]; return c; }
            bool crosses = Cross(A.L, B.R, out int ia, out int ib, out Vector2 x);
            float gap0 = Vector2.Distance(A.L[0], B.R[Foot(B.R, A.L[0])]);
            // an ARC first wherever the arms are not side by side: the offset borders meet in front of the node even where
            // the raw borders cross behind it (an obtuse corner, unequal widths)
            if (dAng >= 35f && TryArc(A, B, c)) return c;
            if (!crosses && gap0 >= NoseGapM)
            {
                c.shape = "connector"; c.tA = A.L[0]; c.tB = B.R[0];
                var tIn = -(A.C[Mathf.Min(4, A.C.Count - 1)] - A.C[0]).normalized; var tOut = (B.C[Mathf.Min(4, B.C.Count - 1)] - B.C[0]).normalized;
                float k = Vector2.Distance(c.tA, c.tB);
                Hermite(c.tA, tIn * k, c.tB, tOut * k, c.pts);
                return c;
            }
            if (!crosses || dAng < 35f)
            {
                int n = Mathf.Min(A.L.Count, B.R.Count);
                for (int i = 0; i < n; i++)
                {
                    int jb = Foot(B.R, A.L[i]);
                    float gap = Vector2.Distance(A.L[i], B.R[jb]);
                    if (gap < NoseGapM) continue;
                    c.shape = "nose"; c.tA = A.L[i]; c.tB = B.R[jb]; c.dA = i * Step; c.dB = jb * Step;
                    c.r = 0.5f * gap;
                    var mid = 0.5f * (c.tA + c.tB);
                    var outA = (A.C[Mathf.Min(i + 1, A.C.Count - 1)] - A.C[Mathf.Max(i - 1, 0)]).normalized;
                    var tip = mid - outA * c.r;
                    // round the land's tip: tA -> tip -> tB on the circle about mid
                    Arc(mid, c.tA, tip, c.r, c.pts, true);
                    c.pts.RemoveAt(c.pts.Count - 1);
                    Arc(mid, tip, c.tB, c.r, c.pts, true);
                    return c;
                }
                c.shape = "nose-not-found"; c.note = "the gap never reaches 2.4 m on the arms given";
                c.tA = A.L[A.L.Count - 1]; c.tB = B.R[B.R.Count - 1]; c.dA = (A.L.Count - 1) * Step; c.dB = (B.R.Count - 1) * Step;
                return c;
            }
            c.shape = "arc-did-not-fit"; c.note = "no fillet down to 0.12 x the class radius";
            c.tA = x; c.tB = x; c.dA = Mathf.Max(0, ia) * Step; c.dB = Mathf.Max(0, ib) * Step;
            return c;
        }

        static bool TryArc(Arm A, Arm B, Corner c)
        {
            float R = A.radius > 0f && B.radius > 0f ? Mathf.Min(A.radius, B.radius) : Mathf.Max(A.radius, B.radius);
            for (int t = 0; t < 6; t++, R *= 0.7f)
            {
                var oA = new List<Vector2>(A.L.Count); var oB = new List<Vector2>(B.R.Count);
                for (int i = 0; i < A.L.Count; i++) oA.Add(Offset(A.L, i, R, true));
                for (int i = 0; i < B.R.Count; i++) oB.Add(Offset(B.R, i, R, false));
                if (!Cross(oA, oB, out _, out _, out Vector2 cc)) continue;
                int fa = Foot(A.L, cc), fb = Foot(B.R, cc);
                c.shape = "arc"; c.r = R; c.tA = A.L[fa]; c.tB = B.R[fb]; c.dA = fa * Step; c.dB = fb * Step;
                Arc(cc, c.tA, c.tB, R, c.pts, false);
                return true;
            }
            return false;
        }

        static void Hermite(Vector2 p0, Vector2 m0, Vector2 p1, Vector2 m1, List<Vector2> into)
        {
            // sampled until no joint breaks the 1 cm / 6 degree rule (by halving)
            int n = 8;
            for (int guard = 0; guard < 8; guard++, n *= 2)
            {
                into.Clear();
                for (int q = 0; q <= n; q++)
                {
                    float t = q / (float)n, t2 = t * t, t3 = t2 * t;
                    into.Add((2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1);
                }
                bool ok = true;
                for (int i = 1; i + 1 < into.Count && ok; i++) ok = !Kink(into[i - 1], into[i], into[i + 1]);
                if (ok) return;
            }
        }

        /// <summary>Rule 4 (the kink probe's test): over 1 cm off the curve through the joint, or over 6 degrees from r 6 m.</summary>
        public static bool Kink(Vector2 a, Vector2 b, Vector2 c)
        {
            var u = b - a; var v = c - b;
            float lu = u.magnitude, lv = v.magnitude;
            if (lu < 0.05f || lv < 0.05f) return false;
            float th = Vector2.Angle(u, v) * Mathf.Deg2Rad;
            if (th < 1e-4f) return false;
            float L = Mathf.Min(lu, lv), sag = L * th / 8f, r = L / th;
            return sag > SagM * 1.02f || (r >= 6f && th * Mathf.Rad2Deg > MaxTurnDeg + 0.1f);
        }

        static void Arc(Vector2 c, Vector2 from, Vector2 to, float r, List<Vector2> into, bool ccw)
        {
            float a0 = Mathf.Atan2(from.y - c.y, from.x - c.x), a1 = Mathf.Atan2(to.y - c.y, to.x - c.x);
            float sweep = a1 - a0;
            if (ccw) { while (sweep < 0f) sweep += 2f * Mathf.PI; } else { while (sweep > 0f) sweep -= 2f * Mathf.PI; }
            float step = 2f * Mathf.Acos(Mathf.Clamp01(1f - SagM / Mathf.Max(r, 0.05f)));
            if (r >= 6f) step = Mathf.Min(step, MaxTurnDeg * Mathf.Deg2Rad);
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep) / Mathf.Max(step, 1e-3f)));
            for (int k = 0; k <= n; k++) { float a = a0 + sweep * k / n; into.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
        }

        static Vector2 Offset(List<Vector2> pl, int i, float off, bool leftSide)
        {
            var t = (pl[Mathf.Min(i + 1, pl.Count - 1)] - pl[Mathf.Max(i - 1, 0)]).normalized;
            var n = new Vector2(-t.y, t.x);
            return pl[i] + (leftSide ? n : -n) * off;
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 x)
        {
            x = default;
            var r = b - a; var s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-9f) return false;
            var q = c - a;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            x = a + r * t;
            return true;
        }

        static bool Cross(List<Vector2> A, List<Vector2> B, out int ia, out int ib, out Vector2 x)
        {
            ia = ib = -1; x = default;
            for (int i = 0; i + 1 < A.Count; i++)
                for (int j = 0; j + 1 < B.Count; j++)
                    if (SegX(A[i], A[i + 1], B[j], B[j + 1], out x)) { ia = i; ib = j; return true; }
            return false;
        }

        static int Foot(List<Vector2> pl, Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pl.Count; i++) { float d = (pl[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        // ---- the fill: holes bridged to the outer ring, then an ear clip ----
        static float Area(List<Vector2> p) { float s = 0f; for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.x * b.y - b.x * a.y; } return 0.5f * s; }

        static void Fill(Result res)
        {
            var ring = new List<Vector2>(res.outline);
            if (ring.Count < 3) return;
            if (Area(ring) < 0f) ring.Reverse();
            // each hole, clockwise, joined at its rightmost vertex to the nearest outer vertex it can see (zero-width cut)
            var holes = new List<List<Vector2>>();
            foreach (var h in res.holes) { if (h.Count < 3) continue; var hh = new List<Vector2>(h); if (Area(hh) > 0f) hh.Reverse(); holes.Add(hh); }
            holes.Sort((p, q) => Max(q).x.CompareTo(Max(p).x));
            foreach (var h in holes)
            {
                int hi = 0; for (int i = 1; i < h.Count; i++) if (h[i].x > h[hi].x) hi = i;
                var hp = h[hi];
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < ring.Count; i++)
                {
                    float d = (ring[i] - hp).sqrMagnitude;
                    if (d >= bd) continue;
                    bool blocked = false;
                    for (int j = 0; j < ring.Count && !blocked; j++)
                    {
                        int j2 = (j + 1) % ring.Count;
                        if (j == i || j2 == i) continue;
                        if (SegX(hp, ring[i], ring[j], ring[j2], out _)) blocked = true;
                    }
                    for (int j = 0; j < h.Count && !blocked; j++)
                    {
                        int j2 = (j + 1) % h.Count;
                        if (j == hi || j2 == hi) continue;
                        if (SegX(hp, ring[i], h[j], h[j2], out _)) blocked = true;
                    }
                    if (!blocked) { bd = d; best = i; }
                }
                if (best < 0) { res.refused = (res.refused ?? "") + " a hole the outer ring cannot see"; continue; }
                var merged = new List<Vector2>(ring.Count + h.Count + 2);
                for (int i = 0; i <= best; i++) merged.Add(ring[i]);
                for (int k = 0; k <= h.Count; k++) merged.Add(h[(hi + k) % h.Count]);
                for (int i = best; i < ring.Count; i++) merged.Add(ring[i]);
                ring = merged;
            }
            res.fillVerts = ring;
            EarClip(ring, res.tris);
            for (int t = 0; t + 2 < res.tris.Count; t += 3)
                if (MinAngle(ring[res.tris[t]], ring[res.tris[t + 1]], ring[res.tris[t + 2]]) < SliverDeg) res.slivers++;
        }

        static Vector2 Max(List<Vector2> h) { var m = h[0]; foreach (var p in h) if (p.x > m.x) m = p; return m; }

        static float MinAngle(Vector2 a, Vector2 b, Vector2 c)
        {
            float A = Vector2.Angle(b - a, c - a), B = Vector2.Angle(a - b, c - b);
            return Mathf.Min(A, Mathf.Min(B, 180f - A - B));
        }

        /// <summary>Ear clipping of an anticlockwise simple polygon (bridged holes are simple too: their two cut edges coincide).</summary>
        static void EarClip(List<Vector2> poly, List<int> into)
        {
            var idx = new List<int>(poly.Count); for (int i = 0; i < poly.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < poly.Count * 4)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i - 1 + idx.Count) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) <= 1e-7f) continue;
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        int ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        var p = poly[ij];
                        if (p == a || p == b || p == c) continue;   // a bridge's twin vertex
                        inside = InTri(a, b, c, p);
                    }
                    if (inside) continue;
                    into.Add(ia); into.Add(ib); into.Add(ic);
                    idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { into.Add(idx[0]); into.Add(idx[1]); into.Add(idx[2]); }
        }

        static bool InTri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y), d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y), d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ---- helpers for callers and tests ----
        /// <summary>A straight synthetic arm leaving origin along dir: half widths left / right, length, step Step.</summary>
        public static Arm StraightArm(int id, Vector2 origin, Vector2 dir, float halfL, float halfR, float length = 80f, float radius = 9f, bool oneway = false, string name = "")
        {
            dir = dir.normalized; var left = new Vector2(-dir.y, dir.x);
            var a = new Arm { id = id, radius = radius, oneway = oneway, name = name };
            for (float d = 0f; d <= length + 1e-3f; d += Step) { var p = origin + dir * d; a.C.Add(p); a.L.Add(p + left * halfL); a.R.Add(p - left * halfR); }
            return a;
        }
        /// <summary>A curved synthetic arm: a circular arc of radius rc (sign: + bends left) from origin, heading dir.</summary>
        public static Arm CurvedArm(int id, Vector2 origin, Vector2 dir, float rc, float halfL, float halfR, float length = 80f, float radius = 9f, bool oneway = false, string name = "")
        {
            dir = dir.normalized;
            var a = new Arm { id = id, radius = radius, oneway = oneway, name = name };
            var p = origin; var t = dir;
            for (float d = 0f; d <= length + 1e-3f; d += Step)
            {
                var left = new Vector2(-t.y, t.x);
                a.C.Add(p); a.L.Add(p + left * halfL); a.R.Add(p - left * halfR);
                float turn = Step / rc;
                float c = Mathf.Cos(turn), s = Mathf.Sin(turn);
                t = new Vector2(t.x * c - t.y * s, t.x * s + t.y * c).normalized;
                p += t * Step;
            }
            return a;
        }
        /// <summary>Is a point inside the outline and outside every hole?</summary>
        public static bool Covers(Result r, Vector2 p)
        {
            if (!InPoly(r.outline, p)) return false;
            foreach (var h in r.holes) if (InPoly(h, p)) return false;
            return true;
        }
        public static bool InPoly(List<Vector2> poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }
        /// <summary>How many fill triangles cover a point (the 'covered exactly once' check, independent of the outline test).</summary>
        public static int CoverCount(Result r, Vector2 p)
        {
            int n = 0;
            for (int t = 0; t + 2 < r.tris.Count; t += 3)
                if (InTri(r.fillVerts[r.tris[t]], r.fillVerts[r.tris[t + 1]], r.fillVerts[r.tris[t + 2]], p)) n++;
            return n;
        }
    }
}
