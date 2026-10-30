using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 11 (2026-10-30): the tile builder's round-11 rules and their debug
    /// traces. See Docs/CHARLOTTE.md "REAL HEIGHTS - round 11".
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>(round 11) The tile builder's round-11 switches as one set (the look tool's A/B).</summary>
        public static bool[] Round11Flags() => new[] { R11GoreStartOn, R11GoreFanOn, R11MitrePokeOn, R11SkirtPaveOn, R11InnerMajorOn };
        public static void SetRound11Flags(bool[] f)
        {
            if (f == null) return;
            if (f.Length > 0) R11GoreStartOn = f[0];
            if (f.Length > 1) R11GoreFanOn = f[1];
            if (f.Length > 2) R11MitrePokeOn = f[2];
            if (f.Length > 3) R11SkirtPaveOn = f[3];
            if (f.Length > 4) R11InnerMajorOn = f[4];
        }

        // ------------------------------------------------------------------
        //  INNERMAJOR (round 10's INVISIBLE lane on Cates Street e21212 s 3,
        //  "the connector's ribbon 0.18 m over the lane"): a junction cluster's
        //  inside edge stays out of the major road's pavement. FANMAJOR holds
        //  the major pair's own pavement at its profile through the junction
        //  and keeps the minor ARMS' line points out of it; a cluster's inside
        //  edges' points (the quarter points that carry an inside edge's own
        //  profile) still went in wherever they fell. At the Cates St / North
        //  Cedar St cluster (nodes 17829 + 19428) the 9.9 m connector e23417
        //  leaves the through pair at its centreline and climbs 9.7% east to
        //  node 19428; its first quarter point (s 2.5, 119.10) lay 1.1 m inside
        //  the through pair's kerb, among the major's edge points at 118.87:
        //  a 0.23 m tent across Cates Street's east lane (drawn and solid - the
        //  car box met it from under its apex, so the audit called it
        //  invisible). Now an inside edge's point inside the major's pavement
        //  stays out (the major's own points carry the surface there), and the
        //  points beyond it lie on a straight grade from the major's height
        //  where the edge leaves its pavement to the edge's own far end - the
        //  minor warps outside the major, as AASHTO and FANMAJOR have it.
        //  PSX_CITY_R11_INNERMAJOR=0: before.
        // ------------------------------------------------------------------
        public static bool R11InnerMajorOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_INNERMAJOR") != "0";
        /// <summary>(INNERMAJOR) inside-edge points left out of a major's pavement / re-graded beyond it,
        /// fan builds since load (a fan is built once per tile it touches).</summary>
        public static int R11InnerMajorSkips, R11InnerMajorEased;

        /// <summary>(INNERMAJOR) FanSteiner's major pair, as it finds it: the main arm, its
        /// partner, the node they go through, the widest half-width of their pavement within
        /// the fan, and each arm's line from the node to its trim (mi = -1: none).</summary>
        static void R11FanMajor(CityMap map, Trims t, int n, JunctionCluster cl, out int mi, out int pi, out int mn,
            out float majHw, out Vector2 nP, out Vector2 endM, out Vector2 endP)
        {
            mi = pi = -1; mn = n; majHw = 0f;
            nP = endM = endP = map.nodes[n];
            if (!R11InnerMajorOn || cl == null || !FanMajorOn || t.fanMain == null || t.fanPartner == null) return;
            int fk = FanKey(t, n);
            mi = t.fanMain[fk]; pi = t.fanPartner[fk];
            if (mi < 0 || pi < 0 || mi == pi) { mi = pi = -1; return; }
            mn = t.fanMainNode[fk];
            var P = map.edges[pi];
            if (mn < 0 || (P.a != mn && P.b != mn)) { mi = pi = -1; mn = n; return; }
            nP = endM = endP = map.nodes[mn];
            for (int q = 0; q < 2; q++)
            {
                var A = map.edges[q == 0 ? mi : pi];
                float tA = Mathf.Min(t.TrimAt(A, mn), A.length);
                float sEnd = A.a == mn ? tA : A.length - tA;
                if (q == 0) endM = A.PointAt(sEnd); else endP = A.PointAt(sEnd);
                for (float d = 1.5f; d <= tA - 0.4f; d += Mathf.Min(3f, Mathf.Max(1f, (tA - 1.9f) * 0.5f)))
                {
                    float s = A.a == mn ? d : A.length - d;
                    LineModel.Extents(A, s, out float eM, out float eP);
                    majHw = Mathf.Max(majHw, Mathf.Max(eM, eP));
                }
            }
        }

        /// <summary>(INNERMAJOR) An inside edge's three points, kept out of the major's pavement:
        /// false (nothing put) where none of them lies in it.</summary>
        static bool R11InnerOutOfMajor(CityMap map, CityMap.Edge e, int mi, int pi, int mn, float majHw,
            Vector2 nP, Vector2 endM, Vector2 endP, System.Action<Vector2, float> put)
        {
            bool In(Vector2 q) => DistToSeg(q, nP, endM) < majHw + 0.5f || DistToSeg(q, nP, endP) < majHw + 0.5f;
            float L = e.length;
            bool any = false;
            for (int q = 1; q <= 3 && !any; q++) any = In(e.PointAt(L * q * 0.25f));
            if (!any) return false;
            // how far in from each end the edge runs inside the major's pavement
            const float step = 0.25f;
            float sA = 0f, sB = 0f;
            if (In(e.PointAt(0f))) { sA = step; while (sA < L && In(e.PointAt(sA))) sA += step; }
            if (In(e.PointAt(L))) { sB = step; while (sB < L && In(e.PointAt(L - sB))) sB += step; }
            float s0 = Mathf.Min(sA, L), s1 = Mathf.Max(L - sB, s0);
            float y0 = sA > 0f ? R11MajorY(map, mi, pi, mn, nP, endM, endP, e.PointAt(s0)) : e.YAt(0f);
            float y1 = sB > 0f ? R11MajorY(map, mi, pi, mn, nP, endM, endP, e.PointAt(s1)) : e.YAt(L);
            bool regrade = (sA > 0f || sB > 0f) && s1 - s0 > 0.5f;
            for (int q = 1; q <= 3; q++)
            {
                float s = L * q * 0.25f;
                var p = e.PointAt(s);
                if (In(p)) { R11InnerMajorSkips++; continue; }
                float y = regrade ? Mathf.Lerp(y0, y1, Mathf.Clamp01((s - s0) / (s1 - s0))) : e.YAt(s);
                if (Mathf.Abs(y - e.YAt(s)) > 0.005f) R11InnerMajorEased++;
                put(p, y);
            }
            return true;
        }

        /// <summary>(INNERMAJOR) The major's height at a plan point: the nearer major arm's
        /// profile where the point projects on its line from the node.</summary>
        static float R11MajorY(CityMap map, int mi, int pi, int mn, Vector2 nP, Vector2 endM, Vector2 endP, Vector2 q)
        {
            bool useM = DistToSeg(q, nP, endM) <= DistToSeg(q, nP, endP);
            var A = map.edges[useM ? mi : pi];
            var d = (useM ? endM : endP) - nP;
            float len = d.magnitude;
            float along = len > 1e-4f ? Mathf.Clamp(Vector2.Dot(q - nP, d / len), 0f, len) : 0f;
            float s = A.a == mn ? along : A.length - along;
            return A.YAt(Mathf.Clamp(s, 0f, A.length));
        }

        // ------------------------------------------------------------------
        //  SKIRTPAVE (round 10's INVISIBLE lane at North Tryon Street e14645
        //  s ~118, under the E 12th St bridge and its ramps): the skirt pass
        //  closes a sheet's open edge with a face straight down to the land,
        //  except over another road's LANES (OverRoad: a fascia instead). The
        //  lanes it asks are the map's designed lanes; North Tryon's left side
        //  there is drawn 9.4 m wide (an added lane), so a ground sheet at the
        //  ramps' level (134.67 m) over that lane counted as over a shoulder and
        //  got a 6.6 m concrete face from 128.06 m up - in the Roads collider,
        //  one face of it drawn toward the route, the other side not: the probe's
        //  triangle (Roads/Concrete, untapped, normal (-0.66, 0, -0.75)). Now the
        //  lanes OverRoad asks include what the line model draws beyond the
        //  designed lanes, less a shoulder's metre (R11SkirtShoulderM): such a
        //  sheet edge takes the fascia. PSX_CITY_R11_SKIRTPAVE=0: before.
        // ------------------------------------------------------------------
        public static bool R11SkirtPaveOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_SKIRTPAVE") != "0";
        public const float R11SkirtShoulderM = 1.0f;
        /// <summary>(SKIRTPAVE) lane hits only the drawn extent gave, tile builds since load.</summary>
        public static int R11SkirtPaveFascias;

        // ------------------------------------------------------------------
        //  MITREPOKE: a branch's last metres over the OTHER through arm.
        //  At a mitred node a branch (clipped onto the through arm it leaves)
        //  ends in a section square to its own line at the node, and the
        //  through arm beyond the node meets its host on the mitre - so the
        //  branch's last quad runs on over that other arm's first metre or
        //  two, at the branch's height. Where the two roads' grades part at the
        //  node (South Boulevard at node 8870: the branch e13286 on -1%, the
        //  through arm e7288 falling at 7%) it stood 5-13 cm over the other
        //  arm's lanes and a car on that arm met it as a kerb (the launch
        //  audit's "mitred joint" spots on a through path, the other road a
        //  branch of the one it continues). Round 5's MitreSnap does this for a
        //  divided road's split only, within 20 m of the node (OFF: all 57k
        //  branch vertices city-wide cost the census). Here: every branch, only
        //  within R11PokeReachM of the node - the poke itself - each vertex
        //  lying on the other arm's drawn pavement and above it by less than
        //  MitreSnapMaxM comes down a centimetre under it, so the through arm is
        //  the surface on top. PSX_CITY_R11_MITREPOKE=0: before.
        // ------------------------------------------------------------------
        public static bool R11MitrePokeOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_MITREPOKE") != "0";
        public const float R11PokeReachM = 4f;
        /// <summary>(MITREPOKE) vertices brought down, tile builds since load.</summary>
        public static int R11PokeSnaps;

        static void R11MitrePoke(CityMap map, Trims trims, CityMap.Edge e, ref Section sec)
        {
            for (int end = 0; end < 2; end++)
            {
                int n = end == 0 ? e.a : e.b;
                int host = end == 0 ? trims.branchA[e.index] : trims.branchB[e.index];
                if (host < 0 || !trims.mitre[n]) continue;
                if ((end == 0 ? sec.s : e.length - sec.s) > R11PokeReachM) continue;
                for (int k = 0; k < 2; k++)
                {
                    int oi = k == 0 ? trims.throughA[n] : trims.throughB[n];
                    if (oi < 0 || oi == host || oi == e.index) continue;
                    var ol = RawOutlineOf(map, trims, oi);
                    if (ol.L == null) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var v = sec.Edge(side);
                        float h = OutlineHeightAt(ol, new Vector2(v.x, v.z));
                        if (float.IsNaN(h)) continue;
                        float want = h - MitreSnapProudM;
                        if (v.y <= want || v.y - want > MitreSnapMaxM) continue;
                        v.y = want;
                        if (side < 0) { sec.L = v; sec.snapL = true; } else { sec.R = v; sec.snapR = true; }
                        R11PokeSnaps++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  GOREFAN: no painted gore inside a junction's fan.
        //  A branch clipped onto its host at a FAN node (a fork drawn as a
        //  junction: Christenbury Parkway's e14530 off Cox Mill Road at node
        //  4212, trims 25 m) still had its gore painted from the node out, at
        //  the host's height an inch down - but inside the fan the junction's
        //  own surface paves (a TIN between the arms' corners, the major road
        //  held), and where that surface lies lower the gore stood over it:
        //  0.24 m in one metre, the launch audit's #2 (lift-off from 17 km/h).
        //  The gore starts where the fan ends. PSX_CITY_R11_GOREFAN=0: before.
        // ------------------------------------------------------------------
        public static bool R11GoreFanOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_GOREFAN") != "0";
        /// <summary>(GOREFAN) gore samples left to the fan (a sample whose host-chain arc from the
        /// node is inside the host's own trim there: the fan's ground), tile builds since load.</summary>
        public static int R11GoreFanSkips;

        // ------------------------------------------------------------------
        //  GORESTART (round 10's BLUNT, I-277 e9902 s 18): an exit ramp's
        //  (or any branch's) outer rail that starts where its ribbon comes out
        //  of its host's pavement. OSM joins a ramp at the end of its taper, so
        //  its first metres are drawn collapsed onto the host's edge; the
        //  approach rail it carries (a structure ahead) began on the first span
        //  past that zone - ON the host's edge line, square to the host's
        //  traffic in its outside lane. The rail-end plan skipped any end whose
        //  open span was a collapsed (skip / wedge) span, so nothing turned it.
        //  DOT: no blunt end faces traffic; here there is no roadside to lead a
        //  W-beam in along (the host's lanes lie past it), so the run's own first
        //  TaperLenM is TURNED DOWN - sloped up from a curb's height to full, the
        //  Roadside Design Guide's sloped end - with no drop exception (what the
        //  approach rail guards lies ahead of it). PSX_CITY_R11_GORESTART=0: before.
        // ------------------------------------------------------------------
        public static bool R11GoreStartOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_GORESTART") != "0";
        const int EndGoreStart = 4;
        /// <summary>(GORESTART) rail starts turned down, tile builds since load (the probe prints it).</summary>
        public static int R11GoreStarts;
        /// <summary>Is this edge a branch at either end (clipped onto a host)?</summary>
        static bool R11BranchEdge(Trims trims, CityMap.Edge e) => trims.branchA[e.index] >= 0 || trims.branchB[e.index] >= 0;

        /// <summary>(round 11, STREETSEAT) Every STREET branch end's seat where its ribbon runs
        /// INTO its host's pavement (the ramps' rule, <see cref="SeatIntoOnly"/>): the solve seats
        /// it there as it seats a ramp (CityElevation.PrepareSeats).</summary>
        public static List<Seat> StreetIntoSeats(CityMap map, Trims trims) => StreetIntoSeats(map, trims, out _, out _);

        /// <summary>(round 11, STREETSEAT) ...only where the climb-out has room: the seat must end on
        /// the branch's first piece with at least <see cref="R11StreetClimbMinM"/> of it left past the
        /// zone, the offset there (host's height less the branch's own, as the profiles stand when the
        /// seats are prepared) easable within R11StreetClimbGrade of extra grade over what is left, and
        /// the edge seated at one end only (a slip street seated at both ends had a 14% step between
        /// the two: Pineville-Matthews Road's e14962 in AB1).</summary>
        public static List<Seat> StreetIntoSeats(CityMap map, Trims trims, out int refusedRoom, out int refusedBoth)
        {
            refusedRoom = refusedBoth = 0;
            var cand = new List<(Seat seat, int ei)>();
            var ends = new Dictionary<int, int>();
            for (int ei = 0; ei < map.edges.Length; ei++)
                for (int end = 0; end < 2; end++)
                {
                    int hostIdx = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                    if (hostIdx < 0) continue;
                    var L = map.edges[ei];
                    if (L.link || L.a == L.b) continue;
                    var seat = SeatOf(map, trims, L, map.edges[hostIdx], end == 0 ? L.a : L.b, true);
                    if (seat == null || seat.pieces.Count == 0) continue;
                    if (R11StreetNarrowOn)
                    {
                        var (pe, s0, s1, dir) = seat.pieces[seat.pieces.Count - 1];
                        if (pe != L.index || seat.pieces.Count > 1) { refusedRoom++; continue; }
                        float zoneEnd = dir > 0 ? s1 : s0, left = dir > 0 ? L.length - s1 : s0;
                        if (left < R11StreetClimbMinM) { refusedRoom++; continue; }
                        int h = seat.HostAt(L.PointAt(zoneEnd), out float hs);
                        if (h < 0) { refusedRoom++; continue; }
                        float off = Mathf.Abs(map.edges[h].YAt(hs) - L.YAt(zoneEnd));
                        if (off > R11StreetClimbGrade * left) { refusedRoom++; continue; }
                    }
                    cand.Add((seat, ei));
                    ends[ei] = ends.TryGetValue(ei, out int c) ? c + 1 : 1;
                }
            var result = new List<Seat>();
            foreach (var (seat, ei) in cand)
            {
                if (R11StreetNarrowOn && ends[ei] > 1) { refusedBoth++; continue; }
                result.Add(seat);
            }
            return result;
        }
        /// <summary>(STREETSEAT, narrow) the switch (on: only seats with room to climb out).</summary>
        public static bool R11StreetNarrowOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_STREETNARROW") != "0";
        public const float R11StreetClimbMinM = 20f, R11StreetClimbGrade = 0.02f;

        /// <summary>(round 11, the launch audit's EXPLAIN) A fan's interior points (FanSteiner: cluster
        /// members, the major pair's pavement, the arms' lines) as "(x,z) y" in world space.</summary>
        public static string R11FanPoints(CityMap map, Trims trims, int n)
        {
            if (!trims.patch[n]) return "";
            var pts = new List<Vector3>(32);
            FanSteiner(map, trims, FanKey(trims, n), Vector3.zero, pts);
            var sb = new System.Text.StringBuilder(" steiner:");
            foreach (var p in pts) sb.Append($" ({p.x:0.0},{p.z:0.0}) {p.y - FanProudM:0.00};");
            FanCentre(map, trims, FanKey(trims, n), out var c, out float cy);
            sb.Append($" centre ({c.x:0.0},{c.y:0.0}) {cy:0.00}");
            return sb.ToString();
        }

        // ---- debug: the rail-end plan on one edge -------------------------------------
        /// <summary>PSX_R11_RAILTRACE=edge: every barrier kind change the rail-end plan sees
        /// on that edge (each side), with the flags that decide whether it is planned
        /// (the editor's CityR11 probe prints <see cref="R11RailTrace"/>).</summary>
        public static readonly int R11RailTraceEdge =
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_R11_RAILTRACE"), out int r11rte) ? r11rte : -1;
        public static readonly List<string> R11RailTrace = new List<string>();

        static string R11RailTraceLine(CityMap.Edge e, TileMeshes tm, int side, int k, int n, int kL, int kR)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string Span(int i)
            {
                if (i < 1 || i >= n) return "-";
                var f = spanFlags[i]; var sf = f[side];
                return (f.decided ? "D" : "d") + (f.elev ? "E" : "") + (f.wedge ? "W" : "") + (f.skip ? "S" : "") + (f.approach ? "A" : "")
                       + (sf.rail ? "r" : "") + (sf.cut ? "c" : "") + (sf.median ? "m" : "") + (sf.gap ? "g" : "") + (sf.union ? "u" : "") + (sf.retain ? "t" : "");
            }
            var sec = sections[k];
            var p = (side < 0 ? sec.L : sec.R) + tm.origin;
            return string.Format(ci, "tile {0:0},{1:0} e{2} side {3} k {4}/{5} s {6:0.00} at ({7:0.0},{8:0.00},{9:0.0}) kinds {10}->{11} spans [{12}] [{13}] sec{14}{15}{16} strip {17:0.00}",
                tm.origin.x / TileSize, tm.origin.z / TileSize, e.index, side, k, n, sec.s, p.x, p.y, p.z, kL, kR, Span(k), Span(k + 1),
                sec.collapsed ? " collapsed" : "", sec.zoneEnd ? " zoneEnd" : "", sec.elev ? " elev" : "", sec.Strip(side));
        }
    }
}
