using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE MERGE REPORT (roads pass L5; plan A7's MERGE audit, lean: plan
    /// geometry plus the drawn host edge, no new raster - COVERAGE's
    /// COPLANAR / OVERSHOOT / UNDERLAP blocks measure the built meshes).
    /// Every zone <see cref="CityMeshes.Zones"/> found (T1 hosts), in scope
    /// (the node in the box) and city-wide:
    ///   ZONES     merges / diverges, style G (OSM adds the lane at the node)
    ///             / T, k, the angle at N, the zone length D (N to the node),
    ///             and why the other branch ends were not zones
    ///   ROOM      host-carried lane room beyond the through-lane edge over
    ///             N..node (+ the extension): metres short of k lanes, BEFORE
    ///             (L4's pavement: no aux lane, the 90 m mouth ease) and AFTER
    ///             (the drawn edge, CityMeshes.LaneExtents) from N to the taper
    ///             start - GATE 0 m in scope
    ///   CUT       metres of ramp no longer drawn between N and the node (L4
    ///             drew them as a narrowing strip and a collapsed sliver)
    ///   NOSE      the ramp's end section against the host's section at N:
    ///             both vertices within 1 cm - GATE (shared vertices)
    ///   Q2        full width short of AASHTO's length (clamped: why), taper
    ///             off the rule by more than 10 %, style G whose OSM aux lane is
    ///             shorter than AASHTO (left as OSM has it)
    ///   SIDE      the data's lane change on the other side from the ramp
    ///   SEAT      the ramp's own height against the host's at N (the end
    ///             section takes the host's)
    /// </summary>
    public static partial class CityAudit
    {
        static partial void MergeReport(CityMap map, CityMeshes.Trims trims)
        {
            var sc = ScopeFor("MERGE");
            Line("");
            Line("MERGE ZONES (roads pass L5: plan A7 + A9, owner Q2) - " + sc.Describe() + (CityMeshes.MergeZonesOn ? $" - built in {CityMeshes.BuildMs:0} ms (find {CityMeshes.BuildMsFind:0}, again {CityMeshes.BuildMsAgain:0})" : " - OFF (PSX_CITY_MERGEZONES=0)"));
            var zones = CityMeshes.Zones;
            foreach (var z in zones) if (z.style == 'G') z.dataAux = CityMeshes.ZoneDataAux(map, trims, z);
            const float LANE = RoadProfiles.LaneM;
            for (int pass = 0; pass < 2; pass++)
            {
                bool box = pass == 0;
                var zs = zones.Where(z => !box || sc.Contains(map.nodes[z.node])).ToList();
                string where = box ? "in scope" : "CITY-WIDE";
                int mg = zs.Count(z => z.merge), dv = zs.Count - mg, g = zs.Count(z => z.style == 'G');
                var Ds = zs.Select(z => z.D).OrderBy(v => v).ToList();
                var ths = zs.Select(z => z.theta).OrderBy(v => v).ToList();
                float P(List<float> l, float q) => l.Count == 0 ? 0f : l[Mathf.Clamp(Mathf.FloorToInt(q * (l.Count - 1) + 0.5f), 0, l.Count - 1)];
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: {1} zones ({2} merges, {3} diverges; style G {4}, T {5}; k=1 {6}, k=2 {7}, k=3 {8}); N to node D p10 {9:0} / p50 {10:0} / p90 {11:0} m; angle at N p50 {12:0.0} / p90 {13:0.0} deg",
                    where, zs.Count, mg, dv, g, zs.Count - g, zs.Count(z => z.k == 1), zs.Count(z => z.k == 2), zs.Count(z => z.k == 3),
                    P(Ds, 0.1f), P(Ds, 0.5f), P(Ds, 0.9f), P(ths, 0.5f), P(ths, 0.9f)));
                // ROOM: before (L4 pavement) over N..node, after (drawn) over N..taper start
                float shortBefore = 0f, shortAfter = 0f, cut = 0f, noseErr = 0f; int pinched = 0, shortZones = 0, noseBad = 0, overlapped = 0;
                var pinchLens = new List<float>();
                string worstNose = null, worstShort = null;
                foreach (var z in zs)
                {
                    float pinch = 0f;
                    for (float x = 0.5f; x < z.D; x += 1f) if (Room(map, z, x, false, out _) < z.k * LANE - 0.05f) { shortBefore += 1f; pinch += 1f; }
                    if (pinch > 0f) { pinched++; pinchLens.Add(pinch); }
                    float zs0 = 0f;
                    for (float x = z.D - 0.5f; x > z.xf; x -= 1f)
                        if (Room(map, trims, z, x, out string at) < z.k * LANE - 0.05f) { shortAfter += 1f; zs0 += 1f; worstShort ??= at; }
                    if (zs0 > 0f) shortZones++;
                    foreach (var c in z.cuts) cut += c.s1 - c.s0;
                    float err = NoseError(map, z);
                    if (err > 0.01f) { noseBad++; if (noseBad <= 6) Line($"    nose off {err:0.000} m: {Describe(map, z)}{OtherAux(map, z)}{NoseDetail(map, z)}"); }
                    if (err > noseErr) { noseErr = err; worstNose = Describe(map, z); }
                    // another zone's aux lane already on the host where this ramp still runs beside it
                    if (z.HostAt(map, z.D + 5f, out var Hb, out float sb, out int sdb) && LineModel.AuxWidth(Hb, sb, sdb) > 0.05f) overlapped++;
                }
                pinchLens.Sort();
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0} ROOM: lane room on the host short of k lanes over N..node - BEFORE (L4: no aux lane, the mouth ease) {1:0} m in {2} zones (pinch p50 {3:0} / p90 {4:0} m); AFTER, drawn, N..taper start: {5:0} m in {6} zones; ramp no longer drawn between N and its node: {7:0} m",
                    where, shortBefore, pinched, P(pinchLens, 0.5f), P(pinchLens, 0.9f), shortAfter, shortZones, cut));
                if (worstShort != null) Line("    first short: " + worstShort);
                Line(string.Format(CultureInfo.InvariantCulture, "  {0} NOSE: ramp end section vs the host's section at N: {3} over 1 cm, worst {1:0.000} m{2}; ramps beside another zone's aux lane before their N: {4}", where, noseErr, worstNose != null ? " at " + worstNose : "", noseBad, overlapped));
                // Q2
                var tz = zs.Where(z => z.style == 'T').ToList();
                int clamped = tz.Count(z => z.clamp != null && !z.backToBack), aux = tz.Count(z => z.backToBack);
                float fwShort = tz.Sum(z => Mathf.Max(0f, z.xf - (z.D - z.lReq)));
                int taperOff = tz.Count(z => z.lt > 0f && Mathf.Abs(z.lt - z.ltRule) > 0.1f * z.ltRule);
                var ext = tz.Where(z => z.need > 0f).Select(z => Mathf.Max(0f, -(z.xf - z.lt))).OrderBy(v => v).ToList();
                var reasons = tz.Where(z => z.clamp != null).GroupBy(z => z.clamp).Select(q => $"{q.Key} {q.Count()}");
                int gShort = zs.Count(z => z.style == 'G' && z.D + z.dataAux < z.lReq);
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0} Q2 (AASHTO length, then the taper): style T {1}, {2} run on past the node (p50 {3:0} / max {4:0} m of new pavement on the other arm); clamped {5} (full width short {6:0} m in all: {7}); run on into the next exit as an aux lane {8}; taper off the rule by > 10 % {9}; style G whose OSM aux lane is shorter than AASHTO (left as OSM) {10}",
                    where, tz.Count, ext.Count, P(ext, 0.5f), ext.Count > 0 ? ext[ext.Count - 1] : 0f, clamped, fwShort, string.Join(", ", reasons), aux, taperOff, gShort));
                // A9: the lane path through the zone - continuous with the
                // ramp's own lane at N and with the host's lanes at the node
                var jN = new List<float>(); var jNode = new List<float>(); string worstJ = null; float worstJv = 0f;
                foreach (var z in zs)
                {
                    var E = map.edges[z.cutEdge];
                    int up = 0; foreach (var c in z.cuts) if (c.end) up = -c.nodeDir;
                    float sOut = Mathf.Clamp(z.sCut + up * 0.05f, 0f, E.length);
                    var a = LineModel.LanePoint(E, z.sCut); var b = LineModel.LanePoint(E, sOut);
                    var tE = E.TangentAt(z.sCut);
                    float lat = Mathf.Abs(Vector2.Dot(a - b, new Vector2(-tE.y, tE.x)));
                    jN.Add(lat);
                    var L = map.edges[z.branch]; var O = map.edges[z.other];
                    float sL = L.a == z.node ? 0f : L.length, sO = O.a == z.node ? 0f : O.length;
                    float dn = (LineModel.LanePoint(L, sL) - LineModel.LanePoint(O, sO)).magnitude;
                    jNode.Add(dn);
                    if (Mathf.Max(lat, dn) > worstJv) { worstJv = Mathf.Max(lat, dn); worstJ = Describe(map, z) + $" (at N {lat:0.00} m, at the node {dn:0.00} m)"; }
                }
                jN.Sort(); jNode.Sort();
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0} LANE PATH (A9): the ramp's lane at its N against the aux lane p50 {1:0.00} / max {2:0.00} m; at the node against the host's lanes p50 {3:0.00} / max {4:0.00} m{5}",
                    where, P(jN, 0.5f), jN.Count > 0 ? jN[jN.Count - 1] : 0f, P(jNode, 0.5f), jNode.Count > 0 ? jNode[jNode.Count - 1] : 0f, worstJ != null ? "; worst " + worstJ : ""));
                var dys = zs.Select(z => Mathf.Abs(z.seatDy)).OrderBy(v => v).ToList();
                Line(string.Format(CultureInfo.InvariantCulture,
                    "  {0} SIDE: the data's lane change on the far side from the ramp {1}; SEAT: |ramp - host| height at N p50 {2:0.000} / max {3:0.000} m (the end section takes the host's)",
                    where, zs.Count(z => z.sideMismatch), P(dys, 0.5f), dys.Count > 0 ? dys[dys.Count - 1] : 0f));
                if (box)
                {
                    Check(noseErr <= 0.01f, "MERGE NOSE: a ramp's end section shares the host's vertices at N (in scope)", $"worst {noseErr:0.000} m");
                    Check(shortAfter <= 0f, "MERGE ROOM: no lane-room shortfall from N to the taper start (in scope, drawn)", $"{shortAfter:0} m in {shortZones} zones");
                    foreach (var z in zs.OrderBy(z => z.node).Take(12)) Line("    " + Describe(map, z));
                }
            }
            // the city routes that drive a ramp through its zone (A9: their race
            // line now follows the aux lane there) or a host's aux lane
            if (map.routes != null)
                foreach (var r in map.routes)
                {
                    var set = new HashSet<int>(r.edges);
                    int ramps = 0, hosts = 0;
                    foreach (var z in zones)
                    {
                        if (z.cuts.Any(c => set.Contains(c.edge))) ramps++;
                        if (set.Contains(z.host) || set.Contains(z.other)) hosts++;
                    }
                    Line($"  route {r.id} '{r.name}': {ramps} zones whose ramp it drives (its line follows the aux lane), {hosts} whose host it drives");
                }
            Line("  not zones (city-wide, T1 hosts): " + string.Join(", ", CityMeshes.ZoneRejects.Select(kv => $"{kv.Key} {kv.Value}")));
            foreach (var kv in CityMeshes.ZoneRejectAt) Line($"    {kv.Key}: e.g. {kv.Value}");
        }

        static string OtherAux(CityMap map, CityMeshes.MergeZone z)
        {
            var H = map.edges[z.endHost];
            var sb = new System.Text.StringBuilder(" | aux on e" + H.index + ":");
            if (H.lmEase != null) foreach (var q in H.lmEase) if (q.aux) sb.Append($" [side {q.side} arm {q.auxArm} D {q.auxD:0.0} wN {q.auxWN:0.00} W {q.auxW:0.00} xf {q.auxXF:0.0} lt {q.auxLt:0.0} d0 {q.d0:0.0}]");
            return sb.ToString();
        }

        static string Describe(CityMap map, CityMeshes.MergeZone z)
        {
            var L = map.edges[z.branch]; var M = map.edges[z.host];
            var np = map.nodes[z.node];
            return string.Format(CultureInfo.InvariantCulture,
                "{0} e{1} at node {2} ({3:0},{4:0}) into/off e{5} '{6}': style {7} k {8}, {9:0.0} deg, D {10:0} m, wN {11:0.00} / {12:0.00} m, AASHTO {13:0} m (V {14:0}/{15:0} mph{16}), full to x {17:0}, taper {18:0} m (rule {19:0}){20}",
                z.merge ? "merge" : "diverge", L.index, z.node, np.x, np.y, M.index, M.name, z.style, z.k, z.theta, z.D, z.wN, z.target,
                z.lReq, z.vHost, z.vRamp, z.loop ? " loop" : "", z.xf, z.lt, z.ltRule, z.clamp != null ? " clamp: " + z.clamp : "");
        }

        /// <summary>Host lane room beyond the through-lane edge at zone
        /// distance x (x &lt; 0: the other arm): BEFORE = L4's pavement.</summary>
        static float Room(CityMap map, CityMeshes.MergeZone z, float x, bool unused, out string at)
        {
            at = null;
            if (!z.HostAt(map, x, out var H, out float s, out int sideH)) return float.MaxValue;
            LineModel.ExtentsBefore(H, s, out float eM, out float eP);
            LineModel.ExtentsNoAux(H, s, out float nM, out float nP);
            float laneEdge = (sideH > 0 ? nP - H.shl : nM - H.shr);
            return (sideH > 0 ? eP : eM) - laneEdge;
        }

        /// <summary>AFTER: the DRAWN host edge (squeeze and all).</summary>
        static float Room(CityMap map, CityMeshes.Trims trims, CityMeshes.MergeZone z, float x, out string at)
        {
            at = null;
            if (!z.HostAt(map, x, out var H, out float s, out int sideH)) return float.MaxValue;
            CityMeshes.LaneExtents(map, trims, H, s, out float hl, out float hr);
            LineModel.ExtentsNoAux(H, s, out float nM, out float nP);
            float laneEdge = (sideH > 0 ? nP - H.shl : nM - H.shr);
            float room = (sideH > 0 ? hr : hl) - laneEdge;
            at = string.Format(CultureInfo.InvariantCulture, "zone e{0} node {1}: host e{2} s {3:0.0} (x {4:0.0}) room {5:0.00} m", z.branch, z.node, H.index, s, x, room);
            return room;
        }

        /// <summary>The ramp's end vertices against the host's own section
        /// at N (its extents there, plain and widened).</summary>
        static float NoseError(CityMap map, CityMeshes.MergeZone z)
        {
            var H = map.edges[z.endHost];
            float s = z.endHostS;
            var p = H.PointAt(s); var t = H.TangentAt(s); var left = new Vector2(-t.y, t.x);
            LineModel.Extents(H, s, out float eM, out float eP);
            LineModel.ExtentsNoAux(H, s, out float nM, out float nP);
            int sideH = Vector2.Dot(left, z.endOuter - p) >= 0f ? 1 : -1;
            var outer = p + left * (sideH > 0 ? eP : -eM);
            var inner = p + left * (sideH > 0 ? nP : -nM);
            return Mathf.Max((outer - z.endOuter).magnitude, (inner - z.endInner).magnitude);
        }

        static string NoseDetail(CityMap map, CityMeshes.MergeZone z)
        {
            var H = map.edges[z.endHost];
            float s = z.endHostS;
            var p = H.PointAt(s); var t = H.TangentAt(s); var left = new Vector2(-t.y, t.x);
            LineModel.Extents(H, s, out float eM, out float eP);
            LineModel.ExtentsNoAux(H, s, out float nM, out float nP);
            int sideH = Vector2.Dot(left, z.endOuter - p) >= 0f ? 1 : -1;
            var outer = p + left * (sideH > 0 ? eP : -eM);
            var inner = p + left * (sideH > 0 ? nP : -nM);
            return string.Format(CultureInfo.InvariantCulture, " | host e{0} s {1:0.00}/{2:0.0} sideH {3} ext {4:0.00}/{5:0.00} noAux {6:0.00}/{7:0.00}; endInner lat {8:0.00} along {9:0.00}; endOuter lat {10:0.00} along {11:0.00}; inner err {12:0.000} outer err {13:0.000}",
                H.index, s, H.length, sideH, eM, eP, nM, nP, Vector2.Dot(z.endInner - p, left), Vector2.Dot(z.endInner - p, t), Vector2.Dot(z.endOuter - p, left), Vector2.Dot(z.endOuter - p, t),
                (inner - z.endInner).magnitude, (outer - z.endOuter).magnitude);
        }
    }
}
