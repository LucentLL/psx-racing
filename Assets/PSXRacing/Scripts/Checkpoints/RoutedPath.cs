using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// AN AI'S OWN ROAD in a checkpoint race (DESIGN_v2 4.2): a copy of <see cref="CityMode.BuildPath"/> (the race
    /// line: the lanes' centre through LineModel, the city's solved heights at every vertex, station, profile knot,
    /// taper and merge-zone sample; 4 m waypoints; the 5-point curvature) with these differences:
    /// - PARTIAL first and last edges (the path starts at the car and ends where the plan stops);
    /// - no loop and no rotation (a routed path always has ends);
    /// - per-waypoint facts the AI reads instead of the race-wide lane switches: the pavement either side
    ///   (<see cref="TrackPath.halfL"/>/<see cref="TrackPath.halfR"/>), one-way or not, its own direction's lane centre,
    ///   the (edge, s) under it, and the junction zones (+-25 m round a turn of 30 degrees or more);
    /// - no <see cref="TrackPath.gates"/> (that is the parking-deck flag).
    /// Junction corners (cityai 11-25, the design's M7): every join of 30 degrees or more is laid on the largest arc
    /// that keeps the car on drawn pavement (<see cref="RacingTurns"/>), its curvature written as the arc's own 1/r
    /// (no smoothing over it), so the AI brakes for the corner it drives. PSX_CP_TURNS=0: BuildPath's corners, as
    /// stage 1 had them.
    /// </summary>
    public static class RoutedPath
    {
        public struct Piece { public int st; public float t0, t1; }

        static readonly List<Vector2> pts = new List<Vector2>(8192);
        static readonly List<float> ys = new List<float>(8192), arcs = new List<float>(8192), rawS = new List<float>(8192);
        static readonly List<int> rawE = new List<int>(8192), rawD = new List<int>(8192), joinIdx = new List<int>(256);
        static readonly List<float> joinTurn = new List<float>(256);
        static readonly List<float> marks = new List<float>(256);
        static readonly List<float> orig = new List<float>(8192), origArcs = new List<float>(8192);
        static readonly List<RacingTurns.Join> joins = new List<RacingTurns.Join>(256);
        static readonly List<Vector2> joinOwn = new List<Vector2>(256);
        static readonly List<RacingTurns.Cut> cuts = new List<RacingTurns.Cut>(64);
        /// <summary>The last build's racing turns (for the logs): laid, and the smallest radius.</summary>
        public static int LastTurns; public static float LastMinR;

        /// <summary>The own side's lane offset (metres right of travel) RoutedPath gives a waypoint on edge e at s.</summary>
        static float OwnAt(CityMap.Edge e, float s, bool fwd)
        {
            if (e.oneway) return 0f;
            Vector2 tan = e.TangentAt(s); if (!fwd) tan = -tan;
            Vector2 right = new Vector2(tan.y, -tan.x);
            float lat = Vector2.Dot(LineModel.LanePoint(e, s) - e.PointAt(s), right);
            float halfR = Mathf.Max(1.5f, e.PaveEdgeM(s, fwd ? 1 : -1) - lat);
            return Mathf.Max(0.5f, (halfR - 0.3f) * 0.5f + (e.turnLane ? LaneM * 0.25f : 0f));
        }

        /// <summary>Lane width the race traffic and the AI's lanes are counted in (TrafficSystem.LaneM).</summary>
        const float LaneM = 3.66f;
        const float JunctionZoneM = 25f, JunctionMinDeg = 30f;

        /// <summary>Build <paramref name="into"/> from the pieces. False (and the path untouched) when there is
        /// nothing to drive.</summary>
        public static bool Build(CityMap map, List<Piece> pieces, TrackPath into, float roadWidthForCompat)
        {
            if (map == null || pieces == null || pieces.Count == 0 || into == null) return false;
            pts.Clear(); ys.Clear(); arcs.Clear(); rawE.Clear(); rawD.Clear(); rawS.Clear(); joinIdx.Clear(); joinTurn.Clear();
            joins.Clear(); joinOwn.Clear();
            int prevE = -1; bool prevFwd = true; float prevHi = 0f, prevLo = 0f;
            float acc = 0f;
            Vector2 lastDir = Vector2.zero;
            for (int k = 0; k < pieces.Count; k++)
            {
                var pc = pieces[k];
                var e = map.edges[pc.st >> 1];
                bool fwd = (pc.st & 1) == 0;
                float t0 = Mathf.Clamp(pc.t0, 0f, e.length), t1 = Mathf.Clamp(pc.t1, 0f, e.length);
                if (t1 - t0 < 0.05f && pieces.Count > 1) continue;
                float lo = fwd ? t0 : e.length - t1, hi = fwd ? t1 : e.length - t0;
                marks.Clear();
                marks.Add(lo); marks.Add(hi);
                for (int i = 0; i < e.s.Length; i++) if (e.s[i] > lo && e.s[i] < hi) marks.Add(e.s[i]);
                if (e.stS != null) for (int i = 0; i < e.stS.Length; i++) if (e.stS[i] > lo && e.stS[i] < hi) marks.Add(e.stS[i]);
                e.ProfileKnots(marks, lo, hi, 0f);
                LineModel.TaperSamples(e, lo, hi, marks);
                CityMeshes.ZoneSamples(e, lo, hi, marks);
                marks.Sort();
                int m = 0;
                for (int i = 1; i < marks.Count; i++)
                    if (marks[i] - marks[m] > 0.05f && marks[i] >= lo - 0.01f && marks[i] <= hi + 0.01f) marks[++m] = marks[i];
                marks.RemoveRange(m + 1, marks.Count - m - 1);
                int n = marks.Count;
                bool joined = pts.Count > 0;
                if (joined)
                {
                    // a turn from the previous edge onto this one
                    Vector2 d0 = e.TangentAt(fwd ? lo : hi); if (!fwd) d0 = -d0;
                    float turn = lastDir.sqrMagnitude > 0f ? Vector2.Angle(lastDir, d0) : 0f;
                    joinIdx.Add(pts.Count - 1); joinTurn.Add(turn);
                    // a sharp join gets an arc; a gentler one (10 degrees or more) only bounds the arc before it
                    if (prevE >= 0 && turn >= 10f)
                    {
                        var pe = map.edges[prevE];
                        bool sharp = turn >= RacingTurns.MinDeg;
                        joins.Add(new RacingTurns.Join { idx = pts.Count - 1, node = sharp ? (fwd ? e.a : e.b) : -1, ein = prevE, eout = e.index, turnDeg = sharp ? turn : Mathf.Min(turn, RacingTurns.MinDeg - 0.01f) });
                        joinOwn.Add(sharp ? new Vector2(OwnAt(pe, prevFwd ? prevHi : prevLo, prevFwd), OwnAt(e, fwd ? lo : hi, fwd)) : Vector2.zero);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    float sAt = fwd ? marks[i] : marks[n - 1 - i];
                    if (joined && i == 0) continue;   // the shared node with the previous edge
                    var p = LineModel.LanePoint(e, sAt);
                    if (pts.Count > 0) acc += Vector2.Distance(pts[pts.Count - 1], p);
                    pts.Add(p); ys.Add(e.YAt(sAt)); arcs.Add(acc);
                    rawE.Add(e.index); rawS.Add(sAt); rawD.Add(fwd ? 1 : -1);
                }
                Vector2 d1 = e.TangentAt(fwd ? hi : lo); if (!fwd) d1 = -d1;
                lastDir = d1;
                prevE = e.index; prevFwd = fwd; prevHi = hi; prevLo = lo;
            }
            if (pts.Count < 2 || acc < 1f) return false;
            // RACING TURNS: the corners laid on arcs; each point keeps its arc on the cornered line (orig), which maps
            // it to its edge and arc below
            origArcs.Clear(); origArcs.AddRange(arcs);
            orig.Clear(); orig.AddRange(arcs);
            cuts.Clear();
            if (RacingTurns.On && joins.Count > 0)
            {
                RacingTurns.Apply(map, pts, ys, orig, joins, joinOwn, cuts);
                if (cuts.Count > 0)
                {
                    arcs.Clear(); acc = 0f;
                    for (int i = 0; i < pts.Count; i++) { if (i > 0) acc += Vector2.Distance(pts[i - 1], pts[i]); arcs.Add(acc); }
                }
            }
            LastTurns = cuts.Count; LastMinR = float.PositiveInfinity;
            for (int c = 0; c < cuts.Count; c++) LastMinR = Mathf.Min(LastMinR, cuts[c].r);

            float spacing = TrackCatalog.Spacing;
            int count = Mathf.Max(2, Mathf.FloorToInt(acc / spacing) + 1);
            var wps = new Vector3[count];
            var edgeAt = new int[count]; var sAtW = new float[count]; var dirAtW = new sbyte[count];
            var halfL = new float[count]; var halfR = new float[count];
            var oneWay = new bool[count]; var own = new float[count]; var junction = new bool[count];
            var oAt = new float[count];
            var onArc = new float[count];   // 1/r on a racing turn's arc, 0 elsewhere
            int seg = 0, cseg = 0, cutAt = 0;
            for (int i = 0; i < count; i++)
            {
                float sN = Mathf.Min(i * spacing, acc);
                while (seg < arcs.Count - 2 && arcs[seg + 1] < sN) seg++;
                float segLN = arcs[seg + 1] - arcs[seg];
                float tN = segLN > 1e-5f ? Mathf.Clamp01((sN - arcs[seg]) / segLN) : 0f;
                var p = Vector2.LerpUnclamped(pts[seg], pts[seg + 1], tN);
                wps[i] = new Vector3(p.x, Mathf.LerpUnclamped(ys[seg], ys[seg + 1], tN), p.y);
                while (cutAt < cuts.Count && arcs[cuts[cutAt].i1] < sN - 0.01f) cutAt++;
                if (cutAt < cuts.Count && sN >= arcs[cuts[cutAt].i0] - 0.01f && sN <= arcs[cuts[cutAt].i1] + 0.01f) onArc[i] = 1f / cuts[cutAt].r;
                // the cornered line's arc under this point: the edge mapping below reads it
                float s = Mathf.LerpUnclamped(orig[seg], orig[seg + 1], tN);
                oAt[i] = s;
                while (cseg < origArcs.Count - 2 && origArcs[cseg + 1] < s) cseg++;
                int segO = cseg;
                float segL = origArcs[segO + 1] - origArcs[segO];
                float t = segL > 1e-5f ? Mathf.Clamp01((s - origArcs[segO]) / segL) : 0f;
                int ea = rawE[segO], eb = rawE[segO + 1];
                // ACROSS A JOIN: the first edge's last raw point IS the shared node, so the whole raw segment from it
                // to the next edge's first point lies on the NEXT edge, its arc running from that edge's node end
                // (it was the next edge's first vertex, 40 m on, for half the segment: that put an AI past the
                // Wilkinson apex gate's arc without crossing it, in the final watched race)
                int ei = ea == eb ? ea : eb;
                float es;
                if (ea == eb) es = Mathf.LerpUnclamped(rawS[segO], rawS[segO + 1], t);
                else
                {
                    float nodeS = rawD[segO + 1] > 0 ? 0f : map.edges[eb].length;
                    es = Mathf.LerpUnclamped(nodeS, rawS[segO + 1], t);
                }
                es = Mathf.Clamp(es, 0f, map.edges[ei].length);
                edgeAt[i] = ei; sAtW[i] = es; dirAtW[i] = (sbyte)(ei == ea ? rawD[segO] : rawD[segO + 1]);
            }
            // the facts under each waypoint, in the travel direction
            for (int i = 0; i < count; i++)
            {
                var e = map.edges[edgeAt[i]];
                float s = sAtW[i];
                Vector3 tan3 = (wps[Mathf.Min(i + 1, count - 1)] - wps[Mathf.Max(i - 1, 0)]); tan3.y = 0f;
                Vector2 tan = new Vector2(tan3.x, tan3.z);
                if (tan.sqrMagnitude < 1e-6f) tan = e.TangentAt(s);
                tan.Normalize();
                Vector2 right = new Vector2(tan.y, -tan.x);
                Vector2 osmT = e.TangentAt(s);
                bool alongPts = Vector2.Dot(osmT, tan) >= 0f;
                Vector2 o = e.PointAt(s);
                float lat = Vector2.Dot(new Vector2(wps[i].x, wps[i].z) - o, right);
                // PaveEdgeM(side): +1 right of the POINTS' direction
                float rightEdge = e.PaveEdgeM(s, alongPts ? 1 : -1), leftEdge = e.PaveEdgeM(s, alongPts ? -1 : 1);
                halfR[i] = Mathf.Max(1.5f, rightEdge - lat);
                halfL[i] = Mathf.Max(1.5f, leftEdge + lat);
                oneWay[i] = e.oneway;
                // own direction's lanes on a two-way road: the middle of the band from the centre line to the kerb
                own[i] = e.oneway ? 0f : Mathf.Max(0.5f, (halfR[i] - 0.3f) * 0.5f + (e.turnLane ? LaneM * 0.25f : 0f));
            }
            // junction zones
            for (int j = 0; j < joinIdx.Count; j++)
            {
                if (joinTurn[j] < JunctionMinDeg) continue;
                // on the cornered line's arcs (the arc of a racing turn is shorter than its corner)
                float at = origArcs[Mathf.Clamp(joinIdx[j], 0, origArcs.Count - 1)];
                if (cuts.Count == 0)
                {
                    // as stage 1 (byte for byte with the turns off)
                    int i0 = Mathf.Max(0, Mathf.FloorToInt((at - JunctionZoneM) / spacing));
                    int i1 = Mathf.Min(count - 1, Mathf.CeilToInt((at + JunctionZoneM) / spacing));
                    for (int i = i0; i <= i1; i++) junction[i] = true;
                }
                else for (int i = 0; i < count; i++) if (Mathf.Abs(oAt[i] - at) <= JunctionZoneM) junction[i] = true;
            }
            // curvature, flattened and lightly smoothed (BuildPath's recipe)
            int Idx(int i) => Mathf.Clamp(i, 0, count - 1);
            var curv = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 a = wps[Idx(i - 1)]; a.y = 0f;
                Vector3 b = wps[i]; b.y = 0f;
                Vector3 c = wps[Idx(i + 1)]; c.y = 0f;
                curv[i] = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad / spacing;
            }
            // a racing turn's arc carries its own 1/r and is not smoothed: the AI plans the corner it drives
            for (int i = 0; i < count; i++) if (onArc[i] > 0f) curv[i] = onArc[i];
            var smooth = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (onArc[i] > 0f) { smooth[i] = onArc[i]; continue; }
                float sum = 0f;
                for (int o2 = -2; o2 <= 2; o2++) sum += curv[Idx(i + o2)];
                smooth[i] = sum / 5f;
            }
            into.waypoints = wps;
            into.curvatures = smooth;
            into.spacing = spacing;
            into.roadWidth = roadWidthForCompat;
            into.drag = false;
            into.pointToPoint = true;
            into.finishIndex = -1;
            into.reversed = false;
            into.bankL = null; into.bankR = null;
            into.gates = null;
            into.edgeAt = edgeAt; into.sAt = sAtW; into.dirAt = dirAtW;
            into.halfL = halfL; into.halfR = halfR;
            into.oneWayAt = oneWay; into.ownLaneOff = own; into.junction = junction;
            return true;
        }
    }
}
