using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Solves road heights and answers ground heights for the city.
    ///
    /// The circuits' rule carries over whole: the land is graded TO the road,
    /// never the other way round. Every edge starts life following an analytic
    /// base terrain, smoothed and grade-limited; then the exporter's FACTS
    /// turn into structure:
    ///
    ///   crossing (no shared node)  -> the OVER edge takes a hump that clears
    ///                                 the under road by <see cref="ClearanceM"/>,
    ///                                 approach-graded, merging into a viaduct
    ///                                 where humps overlap (I-277 uptown).
    ///   water span                 -> the road HOLDS its line across the span
    ///                                 while the ground carves a creek bed
    ///                                 under it.
    ///
    /// Stations marked elevated get a deck and piers and NO ground pin; the
    /// ground query pins only to grounded stations, with the circuits' shelf /
    /// sink / blend shape — but looked up through the tile-local spatial hash
    /// instead of a whole-track Gaussian, because GroundHeightAt's O(track)
    /// walk is a non-starter against 7,000 edges.
    /// </summary>
    public static class CityElevation
    {
        public const float StationStep = 10f;
        public const float ClearanceM = 5.0f;   // under-side of deck over road below
        public const float DeckThick = 0.55f;
        public const float CorridorSink = 0.12f;
        public const float CorridorBlend = 26f;
        public const float ElevMarginM = 1.0f;  // above pre-raise line = on structure

        const float ApproachGrade = 0.045f;

        static float MaxGrade(CityMap.Edge e) =>
            e.cls >= 5 && !e.link ? 0.04f : e.link ? 0.08f : 0.065f;

        // ------------------------------------------------------------------
        //  Base terrain: gently rolling piedmont, O(1) per query.
        // ------------------------------------------------------------------
        public static float BaseY(float x, float z)
        {
            return ValueNoise(x, z, 1701f) * 8.0f
                 + ValueNoise(x + 9173f, z - 4711f, 613f) * 4.2f
                 + ValueNoise(x - 3137f, z + 8291f, 211f) * 1.5f;
        }

        static float ValueNoise(float x, float z, float wavelength)
        {
            float fx = x / wavelength, fz = z / wavelength;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Hash01(ix, iz), b = Hash01(ix + 1, iz);
            float c = Hash01(ix, iz + 1), d = Hash01(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz) * 2f - 1f;
        }

        static float Hash01(int x, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ------------------------------------------------------------------
        //  The solve, at load.
        // ------------------------------------------------------------------
        /// <summary>Which crossings the solver actually enforced after the
        /// mutual-conflict prune. The audit reads this: judging a pruned
        /// crossing by the clearance rule it was excused from is a false FAIL.</summary>
        public static bool[] EnforcedCrossings { get; private set; }

        public static void Solve(CityMap map)
        {
            crossingOn = null;
            crossingTarget = null;
            map.nodeY = new float[map.nodes.Length];
            for (int i = 0; i < map.nodes.Length; i++)
                map.nodeY[i] = BaseY(map.nodes[i].x, map.nodes[i].y);

            // 1. per-edge profile from terrain
            foreach (var e in map.edges)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(e.length / StationStep) + 1);
                e.stS = new float[n];
                e.stY = new float[n];
                e.stElev = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    float at = i == n - 1 ? e.length : i * e.length / (n - 1);
                    e.stS[i] = at;
                    var p = e.PointAt(at);
                    e.stY[i] = BaseY(p.x, p.y);
                }
                Smooth(e.stY, 3);
                ClampGrade(e, MaxGrade(e));
                BlendEndsToNodes(map, e);
            }

            // 2-7. structure and junction agreement, to a fixed point.
            //
            // Two forces both only push roads UP: a crossing lifts its OVER
            // edge clear of the road below, and a junction node takes the
            // highest incident end so ramps meet the mainline they climb to.
            // Each can invalidate the other (a reconciled node lifts an under
            // road whose overpass was already solved), so neither order of two
            // passes settles it — the first cut left 18 separations under
            // height and the second 26. Monotone + bounded means iterating
            // CONVERGES; run to quiescence and finish on a raise, so the
            // clearance rule is the one that holds exactly.
            for (int it = 0; it < 4; it++)
            {
                RaiseAllCrossings(map);
                HoldWaterSpans(map);
                float moved = ReconcileNodes(map);
                if (moved < 0.05f) break;
            }

            // 8. a gentle pass over the interiors: reconciliation can leave a
            // cliff INSIDE a mid-length edge (its ends are fixed by nodes that
            // genuinely disagree). Ends stay put; interiors ease. BEFORE the
            // final raise, so nothing can lower a deck after its clearance is
            // guaranteed — running it after was how a crushed window ended up
            // flush with the ramp it was built to clear.
            foreach (var e in map.edges)
            {
                float g = MaxGrade(e) * 1.6f;
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 1; i < e.stY.Length - 1; i++)
                    {
                        float ds = e.stS[i] - e.stS[i - 1];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
                    }
                    for (int i = e.stY.Length - 2; i >= 1; i--)
                    {
                        float ds = e.stS[i + 1] - e.stS[i];
                        e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
                    }
                }
            }

            // Holds before the final raise: a water span lifts its own road,
            // and one that happened to be an UNDER road after the fresh raise
            // was the last 4.24 m clearance miss. Nothing below this line may
            // move a road except the raise itself.
            HoldWaterSpans(map);
            // Three fresh passes: within one pass, an under-road that is
            // itself OVER something later in the same z-group can rise after
            // being measured (I-77 over Ramp 1060, both z5, missed by 1.3 m).
            // Each pass re-reads once and only nudges upward; chains here are
            // two deep, three passes is margin.
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);
            RaiseAllCrossings(map, fresh: true);
            SnapNodesToEnds(map);

            // 9. mark structure LAST, so lifted approaches near reconciled
            // nodes get decks and lose their ground pin too.
            foreach (var e in map.edges)
            {
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stElev[i]) continue;
                    var p = e.PointAt(e.stS[i]);
                    if (e.stY[i] > BaseY(p.x, p.y) + ElevMarginM) e.stElev[i] = true;
                }
            }

            // 10. lakes get one flat surface each; the shore owns the level
            foreach (var w in map.waters)
            {
                if (!w.lake) { w.surfaceY = 0f; continue; }
                float min = float.MaxValue;
                foreach (var p in w.pts) min = Mathf.Min(min, BaseY(p.x, p.y));
                w.surfaceY = min - 0.6f;
            }
        }

        /// <summary>Every grade separation lifts its OVER edge clear of the
        /// under road's CURRENT height. Lowest stack first, so an edge that is
        /// over one road and under a third reads the raised height when the
        /// higher deck solves. Idempotent and monotonic: safe to run again.</summary>
        static bool[] crossingOn;
        static float[] crossingTarget;

        static void RaiseAllCrossings(CityMap map, bool fresh = false)
        {
            crossingOn ??= PruneMutualCrossings(map);
            EnforcedCrossings = crossingOn;
            if (crossingTarget == null || crossingTarget.Length != map.crossings.Length)
            {
                crossingTarget = new float[map.crossings.Length];
                for (int i = 0; i < crossingTarget.Length; i++) crossingTarget[i] = float.NaN;
            }
            var order = new List<int>();
            for (int i = 0; i < map.crossings.Length; i++) if (crossingOn[i]) order.Add(i);
            order.Sort((p, q) => map.edges[map.crossings[p].over].z
                .CompareTo(map.edges[map.crossings[q].over].z));
            foreach (var ci in order)
            {
                var c = map.crossings[ci];
                var over = map.edges[c.over];
                var under = map.edges[c.under];
                ProjectOn(over, c.at, out float sOver);
                // The target LATCHES on first computation. A ramp that both
                // MEETS a street at a node and passes under it further along
                // otherwise feeds back: crossing raises street, node lifts
                // ramp tip, next pass reads the lifted ramp and raises the
                // street again — one such South Tryon cluster ratcheted 24 m
                // into the sky. Stacks still solve: within the first pass,
                // lower decks are raised before higher ones read them.
                if (fresh || float.IsNaN(crossingTarget[ci]))
                {
                    ProjectOn(under, c.at, out float sUnder);
                    float t = under.YAt(sUnder) + ClearanceM + DeckThick;
                    // fresh = the one post-everything pass: read live heights
                    // so the guarantee is exact, applied once so it cannot
                    // ratchet. Latched otherwise.
                    crossingTarget[ci] = fresh && !float.IsNaN(crossingTarget[ci])
                        ? Mathf.Max(crossingTarget[ci], t) : t;
                }
                RaiseHump(over, sOver, crossingTarget[ci]);
            }
        }

        /// <summary>
        /// Two crossings of the SAME two edges in OPPOSITE directions, close
        /// enough that their approach humps overlap, cannot both hold at these
        /// grades — each iteration of the solver raised one past the other and
        /// the pair ratcheted 20 m into the sky (braided interchange ramps are
        /// where this lives). Keep the one whose over-edge carries the higher
        /// stack (z, then class, then length); the loser's roads still cross,
        /// carried by whatever heights the winner's structure leaves them.
        /// </summary>
        static bool[] PruneMutualCrossings(CityMap map)
        {
            var on = new bool[map.crossings.Length];
            for (int i = 0; i < on.Length; i++) on[i] = true;
            var byPair = new Dictionary<long, List<int>>();
            for (int i = 0; i < map.crossings.Length; i++)
            {
                var c = map.crossings[i];
                long a = Mathf.Min(c.over, c.under), b = Mathf.Max(c.over, c.under);
                long key = (a << 20) | b;
                if (!byPair.TryGetValue(key, out var list)) byPair[key] = list = new List<int>(2);
                list.Add(i);
            }
            int pruned = 0;
            foreach (var list in byPair.Values)
            {
                if (list.Count < 2) continue;
                for (int m = 0; m < list.Count; m++)
                    for (int n = m + 1; n < list.Count; n++)
                    {
                        var cm = map.crossings[list[m]];
                        var cn = map.crossings[list[n]];
                        if (cm.over == cn.over) continue;             // same direction: a real double-cross
                        if (!on[list[m]] || !on[list[n]]) continue;
                        if (Vector2.Distance(cm.at, cn.at) > 500f) continue;
                        var em = map.edges[cm.over];
                        var en = map.edges[cn.over];
                        bool mWins = em.z != en.z ? em.z > en.z
                                   : em.cls != en.cls ? em.cls > en.cls
                                   : em.length >= en.length;
                        on[mWins ? list[n] : list[m]] = false;
                        pruned++;
                    }
            }
            if (pruned > 0) Debug.Log($"[City] pruned {pruned} mutually-conflicting grade separations");
            return on;
        }

        /// <summary>Water spans hold a straight line between their approach
        /// heights and are always structure.</summary>
        static void HoldWaterSpans(CityMap map)
        {
            foreach (var ws in map.wspans)
            {
                var e = map.edges[ws.edge];
                float s0 = Mathf.Clamp(ws.s0, 0f, e.length);
                float s1 = Mathf.Clamp(ws.s1, 0f, e.length);
                if (s1 - s0 < 2f) continue;
                float y0 = e.YAt(s0), y1 = e.YAt(s1);
                for (int i = 0; i < e.stS.Length; i++)
                {
                    if (e.stS[i] < s0 || e.stS[i] > s1) continue;
                    float t = (e.stS[i] - s0) / (s1 - s0);
                    float hold = Mathf.Lerp(y0, y1, t);
                    if (e.stY[i] < hold) e.stY[i] = hold;
                    e.stElev[i] = true;
                }
            }
        }

        /// <summary>
        /// Junctions meet exactly: a node takes the HIGHEST incident end (a
        /// raised mainline lifts its ramps' tips, never the reverse) and every
        /// edge re-blends. A stub too short to ramp levels its two nodes — but
        /// ONLY over small disagreements: levelling a 20 m sliver over a 30 cm
        /// mismatch kills the "384% grade" artifact, while levelling one that
        /// bridges a real deck-to-street drop hauled whole streets up to
        /// viaduct height (the second audit's regression). A big disagreement
        /// across a stub stays a steep little ramp, which is what the geometry
        /// honestly is. Returns how far any node moved, for convergence.
        /// </summary>
        const float RigidStubM = 25f;
        const float RigidStubMaxDelta = 2.5f;

        static float ReconcileNodes(CityMap map)
        {
            float moved = 0f;
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue)
                {
                    moved = Mathf.Max(moved, Mathf.Abs(best - map.nodeY[n]));
                    map.nodeY[n] = best;
                }
            }
            foreach (var e in map.edges)
            {
                if (e.a == e.b) continue;
                if (e.length < RigidStubM)
                {
                    float d = Mathf.Abs(map.nodeY[e.a] - map.nodeY[e.b]);
                    if (d > 0.01f && d < RigidStubMaxDelta)
                    {
                        float m = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                        map.nodeY[e.a] = m;
                        map.nodeY[e.b] = m;
                        moved = Mathf.Max(moved, d);
                    }
                }
                else if (e.length < 90f)
                {
                    // A short viaduct fragment whose nodes disagree by more
                    // than it can climb gets its LOW node raised to what the
                    // climb can reach — an 85% cliff on a 36 m piece of I-77
                    // was the alternative. Raising only, so clearances hold;
                    // the lift cascades outward through later rounds.
                    float feasible = e.length * MaxGrade(e) * 1.6f;
                    float hi = Mathf.Max(map.nodeY[e.a], map.nodeY[e.b]);
                    float lo = Mathf.Min(map.nodeY[e.a], map.nodeY[e.b]);
                    if (hi - lo > feasible)
                    {
                        float lift = hi - feasible - lo;
                        if (map.nodeY[e.a] < map.nodeY[e.b]) map.nodeY[e.a] += lift;
                        else map.nodeY[e.b] += lift;
                        moved = Mathf.Max(moved, lift);
                    }
                }
            }
            foreach (var e in map.edges) BlendEndsToNodes(map, e);
            return moved;
        }

        /// <summary>After the final raise, patches need node heights that match
        /// the raised arm ends — max of ends, no re-blend, so the clearance
        /// the raise just guaranteed is not disturbed.</summary>
        static void SnapNodesToEnds(CityMap map)
        {
            for (int n = 0; n < map.nodes.Length; n++)
            {
                float best = float.MinValue;
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float endY = e.a == n ? e.stY[0] : e.stY[e.stY.Length - 1];
                    if (endY > best) best = endY;
                }
                if (best > float.MinValue) map.nodeY[n] = best;
            }
        }

        static void Smooth(float[] y, int passes)
        {
            for (int p = 0; p < passes; p++)
            {
                float prev = y[0];
                for (int i = 1; i + 1 < y.Length; i++)
                {
                    float cur = y[i];
                    y[i] = (prev + 2f * cur + y[i + 1]) * 0.25f;
                    prev = cur;
                }
            }
        }

        static void ClampGrade(CityMap.Edge e, float g)
        {
            for (int i = 1; i < e.stY.Length; i++)
            {
                float ds = e.stS[i] - e.stS[i - 1];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i - 1] - g * ds, e.stY[i - 1] + g * ds);
            }
            for (int i = e.stY.Length - 2; i >= 0; i--)
            {
                float ds = e.stS[i + 1] - e.stS[i];
                e.stY[i] = Mathf.Clamp(e.stY[i], e.stY[i + 1] - g * ds, e.stY[i + 1] + g * ds);
            }
        }

        static void BlendEndsToNodes(CityMap map, CityMap.Edge e)
        {
            float L = Mathf.Min(e.length * 0.5f, 90f);
            if (L < 1f)
            {
                // a stub too short to blend just takes its nodes' line
                float ya = map.nodeY[e.a], yb = map.nodeY[e.b];
                for (int i = 0; i < e.stY.Length; i++)
                    e.stY[i] = Mathf.Lerp(ya, yb, e.length > 0f ? e.stS[i] / e.length : 0f);
                return;
            }
            float dA = map.nodeY[e.a] - e.stY[0];
            float dB = map.nodeY[e.b] - e.stY[e.stY.Length - 1];
            for (int i = 0; i < e.stY.Length; i++)
            {
                float fromA = e.stS[i], fromB = e.length - e.stS[i];
                if (fromA < L) e.stY[i] += dA * (1f - fromA / L);
                if (fromB < L) e.stY[i] += dB * (1f - fromB / L);
            }
        }

        static void RaiseHump(CityMap.Edge e, float sAt, float targetY)
        {
            // No reach cap, deliberately. A capped hump under a four-level
            // stack (25 m of height wants ~550 m of 4.5% approach) ended in a
            // fifteen-metre CLIFF at the cap — which the grade-relax pass then
            // "fixed" by hauling the whole deck down through the road it was
            // built to clear. The cone fades below terrain on its own; distant
            // stations are a comparison and a no-op.
            for (int i = 0; i < e.stS.Length; i++)
            {
                float want = targetY - Mathf.Abs(e.stS[i] - sAt) * ApproachGrade;
                if (e.stY[i] < want) e.stY[i] = want;
            }
        }

        static void ProjectOn(CityMap.Edge e, Vector2 p, out float arcS)
        {
            float best = float.MaxValue; arcS = 0f;
            for (int i = 0; i + 1 < e.pts.Length; i++)
            {
                Vector2 a = e.pts[i], d = e.pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dd = (p - q).sqrMagnitude;
                if (dd < best) { best = dd; arcS = e.s[i] + Mathf.Sqrt(L2) * t; }
            }
        }

        // ------------------------------------------------------------------
        //  Ground height, per query. Tile-local by construction.
        // ------------------------------------------------------------------
        static HashSet<int> segScratch;
        static HashSet<int> waterScratch;

        public const float MaxCorridorHalf = 24f;

        public static float GroundY(CityMap map, float x, float z)
        {
            float baseY = BaseY(x, z);

            // creeks carve, lakes sink
            waterScratch ??= new HashSet<int>();
            waterScratch.Clear();
            float reachW = 40f;
            map.WaterSegsInRect(new Vector2(x - reachW, z - reachW), new Vector2(x + reachW, z + reachW), waterScratch);
            var p2 = new Vector2(x, z);
            foreach (var packed in waterScratch)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (w.lake)
                {
                    // inside: pinned under the surface; near shore: blended down
                    if (CityMap.PointInPoly(w.pts, p2))
                        baseY = Mathf.Min(baseY, w.surfaceY - 2.2f);
                    else
                    {
                        float dsh = DistToSeg(w.pts, si, p2);
                        if (dsh < 14f)
                            baseY = Mathf.Min(baseY, Mathf.Lerp(w.surfaceY + 0.4f, baseY, dsh / 14f));
                    }
                }
                else
                {
                    float reach = w.width * 0.5f + 16f;
                    float d = DistToSeg(w.pts, si, p2);
                    if (d < reach)
                    {
                        float t = 1f - d / reach;
                        t = t * t * (3f - 2f * t);
                        baseY -= 3.6f * t;
                    }
                }
            }

            // road corridors pin the land to the tarmac (grounded stations only)
            segScratch ??= new HashSet<int>();
            segScratch.Clear();
            float reachR = MaxCorridorHalf + CorridorBlend;
            map.EdgeSegsInRect(new Vector2(x - reachR, z - reachR), new Vector2(x + reachR, z + reachR), segScratch);

            float wSum = 0f, tSum = 0f, wMax = 0f;
            foreach (var packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p2 - a, d) / L2) : 0f;
                Vector2 q = a + d * t;
                float dist = Vector2.Distance(p2, q);
                float ch = Mathf.Min(e.CorridorHalf, MaxCorridorHalf);
                if (dist > ch + CorridorBlend) continue;
                float at = e.s[si] + Mathf.Sqrt(L2) * t;
                if (e.ElevatedAt(at)) continue;        // structure does not pin the land
                float target = e.YAt(at) - CorridorSink;
                float w = dist <= ch ? 1f
                    : 1f - (dist - ch) / CorridorBlend;
                w = w * w * (3f - 2f * w);
                wSum += w; tSum += target * w;
                if (w > wMax) wMax = w;
            }
            if (wSum > 1e-4f)
            {
                float target = tSum / wSum;
                baseY = Mathf.Lerp(baseY, target, wMax);
            }
            return baseY;
        }

        static float DistToSeg(Vector2[] pts, int si, Vector2 p)
        {
            if (si + 1 >= pts.Length) si = Mathf.Max(0, pts.Length - 2);
            Vector2 a = pts[si], d = pts[si + 1] - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
            return Vector2.Distance(p, a + d * t);
        }

        /// <summary>Water surface height for a river point: the carved bed
        /// plus a little depth. Lakes use their flat surfaceY instead.</summary>
        public static float RiverSurfaceY(float x, float z) => BaseY(x, z) - 3.6f + 0.5f;
    }
}
