using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ROAD-LINES ROUND (splits2, 2026-11-16 night). The owner on PC48, free roam on North Tryon
    /// Street 2.16 km (by the GPS line) before a STACK BURGER: "I still see a lot of areas around Charlotte
    /// where the road and/or road lines do not make sense, are not accurate, and would never pass DOT
    /// requirements." His picture: the double yellow S-jogs mid-road, a solid white line leaves a lane
    /// about 8 m wide on the left, dark stripes run diagonally across the lanes.
    ///   -executeMethod PSXRacing.EditorTools.CityAudit.RunSpotFind
    ///     finds where he stood: every point of PSX_SPOT_ROAD (default North Tryon Street) whose nearest
    ///     restaurant is a STACK BURGER to the car's left, PSX_SPOT_KM (2.16) by the minimap's own GPS route.
    ///     Out: city_spotfind.txt.
    /// </summary>
    public static partial class CityAudit
    {
        /// <summary>(11-17 roads3) THE GROUND PROBE (track fault H4: the Uptown Loop's rivals hit "Ground" at 39-51 m/s on
        /// I-277 e1832, between two decks). The tiles round the listed edges built and attached as the game attaches
        /// them (CityWorld.Attach, its colliders), then every PSX_GP_STEP m along each edge, across the drawn road every
        /// PSX_GP_LAT m, a ray down from 30 m over the road: every hit, the road's top and the highest Ground face there.
        ///   PSX_GP_EDGES=1832,9697,2421 [PSX_GP_STEP=1] [PSX_GP_LAT=0.5]   Out: city_groundprobe.txt / .tsv</summary>
        public static void RunGroundProbe()
        {
            outLog = new StringBuilder(); failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing"); FinishTo("city_groundprobe.txt"); return; }
            var inv = CultureInfo.InvariantCulture;
            var edges = new List<int>();
            foreach (var p in EnvS("PSX_GP_EDGES", "1832,9697,2421").Split(',')) if (int.TryParse(p.Trim(), out int ei) && ei >= 0 && ei < map.edges.Length) edges.Add(ei);
            float step = EnvF("PSX_GP_STEP", 1f), latStep = EnvF("PSX_GP_LAT", 0.5f);
            var t = CityMeshes.ComputeTrims(map);
            var noBld = new Dictionary<long, List<CityBuildings.B>>();
            // the tiles under the edges
            var tiles = new HashSet<(int, int)>();
            foreach (int ei in edges)
                foreach (var q in map.edges[ei].pts)
                    for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                        tiles.Add((Mathf.FloorToInt((q.x + dx * 30f) / CityMeshes.TileSize), Mathf.FloorToInt((q.y + dz * 30f) / CityMeshes.TileSize)));
            var root = new GameObject("~GroundProbe");
            foreach (var (tx, tz) in tiles)
            {
                var tm = CityMeshes.Build(map, t, noBld, tx, tz);
                if (tm == null) continue;
                var go = new GameObject($"~gp_{tx}_{tz}"); go.transform.SetParent(root.transform, false); go.transform.position = tm.origin;
                CityWorld.Attach(go, tm, null);   // (the game's own colliders only: Ground, Roads, Barriers, ...)
            }
            Physics.SyncTransforms();
            Line($"GROUND PROBE: edges {string.Join(",", edges)}; {tiles.Count} tiles; step {step} m, across {latStep} m; graph {map.graphHash:x8}; PSX_CITY_SPLITAXIS {LineModel.SplitAxis}");
            var tsv = new StringBuilder("edge\ts\tlat\tx\tz\troadY\tgroundTop\tdy\ttop\thits\n");
            var hitsBuf = new RaycastHit[32];
            foreach (int ei in edges)
            {
                var e = map.edges[ei];
                float worst = float.MinValue, worstS = 0f, worstLat = 0f; int above = 0, samples = 0, topNotRoad = 0;
                for (float s = 0f; s <= e.length + 1e-3f; s += step)
                {
                    LineModel.Extents(e, s, out float eM, out float eP);
                    var c = e.PointAt(s); var tg = e.TangentAt(s); var nl = new Vector2(-tg.y, tg.x);
                    float y0 = e.YAt(s);
                    for (float lat = -eM + 0.2f; lat <= eP - 0.2f + 1e-3f; lat += latStep)
                    {
                        var p = c + nl * lat;
                        int n = Physics.RaycastNonAlloc(new Vector3(p.x, y0 + 30f, p.y), Vector3.down, hitsBuf, 60f, ~0, QueryTriggerInteraction.Ignore);
                        float roadY = float.NaN, groundTop = float.NaN, topY = float.MinValue; string top = "", all = "";
                        for (int h = 0; h < n; h++)
                        {
                            var hit = hitsBuf[h]; string nm = hit.collider.gameObject.name; float hy = hit.point.y;
                            if (Mathf.Abs(hy - y0) > 6f) continue;      // another level (a deck over or a road under)
                            if (hy > topY) { topY = hy; top = nm; }
                            if (nm.StartsWith("Road") && (float.IsNaN(roadY) || hy > roadY)) roadY = hy;
                            if (nm.StartsWith("Ground") && (float.IsNaN(groundTop) || hy > groundTop)) groundTop = hy;
                            all += $"{nm}@{hy - y0:+0.000;-0.000} ";
                        }
                        samples++;
                        if (top.Length > 0 && !top.StartsWith("Road")) topNotRoad++;
                        float dy = float.IsNaN(roadY) || float.IsNaN(groundTop) ? float.NaN : groundTop - roadY;
                        if (!float.IsNaN(dy) && dy > -0.01f) above++;
                        if (!float.IsNaN(dy) && dy > worst) { worst = dy; worstS = s; worstLat = lat; }
                        if ((!float.IsNaN(dy) && dy > -0.05f) || (top.Length > 0 && !top.StartsWith("Road")) || float.IsNaN(roadY))
                            tsv.Append(ei).Append('\t').Append(s.ToString("0.0", inv)).Append('\t').Append(lat.ToString("0.0", inv)).Append('\t').Append(p.x.ToString("0.00", inv)).Append('\t').Append(p.y.ToString("0.00", inv)).Append('\t')
                               .Append(float.IsNaN(roadY) ? "-" : (roadY - y0).ToString("0.000", inv)).Append('\t').Append(float.IsNaN(groundTop) ? "-" : (groundTop - y0).ToString("0.000", inv)).Append('\t')
                               .Append(float.IsNaN(dy) ? "-" : dy.ToString("0.000", inv)).Append('\t').Append(top).Append('\t').Append(all.Trim()).Append('\n');
                    }
                }
                Line($"  e{ei} '{e.name}' {e.length:0.0} m bridge {e.bridge}: {samples} rays; top face not the road at {topNotRoad}; Ground within 1 cm of the road or over it at {above}; the highest Ground over the road {(worst > float.MinValue ? worst.ToString("+0.000;-0.000", inv) : "-")} m at s {worstS:0.0} lat {worstLat:0.0}");
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_groundprobe.tsv"), tsv.ToString());
            Object.DestroyImmediate(root);
            FinishTo("city_groundprobe.txt");
        }

        static string EnvS(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }
        static float EnvF(string k, float d) => float.TryParse(System.Environment.GetEnvironmentVariable(k) ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : d;

        public static void RunSpotFind()
        {
            outLog = new StringBuilder(); failures = 0;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing"); FinishTo("city_spotfind.txt"); return; }
            string road = EnvS("PSX_SPOT_ROAD", "North Tryon Street");
            float km = EnvF("PSX_SPOT_KM", 2.16f), tol = EnvF("PSX_SPOT_TOL", 40f);
            int oct = (int)EnvF("PSX_SPOT_OCT", 6f);       // RaceHUD's octant: 6 = "<" (the target to the left)
            var t = CityMeshes.ComputeTrims(map);
            var buildings = CityBuildings.Precompute(map);
            var food = new List<(byte kind, Vector2 pos)>();
            foreach (var kv in buildings) foreach (var b in kv.Value) if (CityProps.IsFood(b.kind)) food.Add((b.kind, b.pos));
            int burgers = 0; foreach (var f in food) if (f.kind == CityProps.Burger) burgers++;
            Line($"SPOT FIND: road '{road}', nearest restaurant a STACK BURGER in octant {oct}, {km:0.00} km by the GPS route (+/- {tol:0} m); graph {map.graphHash:x8}; restaurants {food.Count} ({burgers} burgers)");
            var splitNodes = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) splitNodes.Add(sp.node);
            var gps = new MinimapGps();
            int hits = 0, edges = 0;
            foreach (var e in map.edges)
            {
                if (e.name != road || e.a == e.b) continue;
                edges++;
                string run = null; float s0 = 0f, s1 = 0f, gA = 0f, gB = 0f; Vector2 pA = default; int dirRun = 0; float stA = 0f; Vector2 foodAt = default;
                void Flush()
                {
                    if (run == null) return;
                    hits++;
                    var tg = e.TangentAt(s0) * dirRun;
                    float hdg = Mathf.Repeat(Mathf.Atan2(tg.x, tg.y) * Mathf.Rad2Deg, 360f);
                    // what is ahead within 400 m in that direction: the nodes, their arms, a split
                    var ahead = new StringBuilder();
                    {
                        var cur = e; int at = dirRun > 0 ? e.b : e.a; float d = dirRun > 0 ? e.length - s0 : s0;
                        for (int g = 0; g < 8 && d < 450f; g++)
                        {
                            var arms = map.nodeEdges[at];
                            ahead.Append($" n{at}@{d:0}m({arms.Count} arms{(splitNodes.Contains(at) ? ", SPLT" : "")})");
                            CityMap.Edge nx = null;
                            foreach (int i in arms) { var o = map.edges[i]; if (o != cur && o.name == road && !o.oneway) nx = o; }
                            if (nx == null) { foreach (int i in arms) { var o = map.edges[i]; if (o != cur && o.name == road && (o.a == at)) nx = o; } }
                            if (nx == null) break;
                            d += nx.length; at = nx.a == at ? nx.b : nx.a; cur = nx;
                        }
                    }
                    Line($"  e{e.index} {(e.oneway ? "one-way" : "two-way")} s {s0:0}..{s1:0} of {e.length:0} m dir {(dirRun > 0 ? "a->b" : "b->a")} hdg {hdg:0.0}: at ({pA.x:0.0},{pA.y:0.0}) GPS {gA:0}..{gB:0} m, straight {stA:0} m to the burger at ({foodAt.x:0},{foodAt.y:0}); lanes {e.lsNF}+{e.lsNB} centre {e.lsCentre} width {e.width:0.00} (shl {e.shl:0.00} shr {e.shr:0.00}) cls {e.cls} {(e.speedKmh > 0 ? e.speedKmh + " km/h" : "untagged")}; ahead:{ahead}");
                    run = null;
                }
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    if (e.oneway && dir < 0) continue;
                    for (float s = 0f; s <= e.length; s += 10f)
                    {
                        var p = e.PointAt(s);
                        float best = float.MaxValue; int bi = -1;
                        for (int i = 0; i < food.Count; i++) { float d = (food[i].pos - p).sqrMagnitude; if (d < best) { best = d; bi = i; } }
                        bool ok = bi >= 0 && food[bi].kind == CityProps.Burger;
                        float straight = Mathf.Sqrt(best);
                        if (ok) ok = straight <= km * 1000f + tol && straight >= 300f;
                        if (ok)
                        {
                            var tg = e.TangentAt(s) * dir;
                            var to = food[bi].pos - p;
                            float rel = Vector3.SignedAngle(new Vector3(tg.x, 0f, tg.y), new Vector3(to.x, 0f, to.y), Vector3.up);
                            ok = Mathf.RoundToInt(Mathf.Repeat(rel, 360f) / 45f) % 8 == oct;
                        }
                        float total = 0f;
                        if (ok)
                        {
                            // the car on its own side of the line (right-hand traffic), as the minimap solves from the car
                            var tg = e.TangentAt(s) * dir; var right = new Vector2(tg.y, -tg.x);
                            ok = gps.Solve(map, p + right * 2f, food[bi].pos) && gps.Valid;
                            total = ok ? gps.Total : 0f;
                            ok = ok && Mathf.Abs(total - km * 1000f) <= tol;
                        }
                        if (ok)
                        {
                            if (run == null || dirRun != dir) { Flush(); run = "x"; s0 = s; gA = total; pA = p; dirRun = dir; stA = straight; foodAt = food[bi].pos; }
                            s1 = s; gB = total;
                        }
                        else Flush();
                    }
                    Flush();
                }
            }
            Line($"{hits} stretches on {edges} edges of '{road}'");
            FinishTo("city_spotfind.txt");
        }

        /// <summary>
        /// -executeMethod PSXRacing.EditorTools.CityAudit.RunLineSpot: what the line model draws along one
        /// road through a point. PSX_SPOT_AT = "x,z,hdg" (heading in degrees, 0 = +z, as the game's), PSX_SPOT_AHEAD
        /// (450 m) / PSX_SPOT_BACK (150 m), PSX_SPOT_TAG. For every edge of the chain: its data (lanes, widths,
        /// shoulders, line set, TAPR records), its layout's lines, its eases, and the drawn lines sampled every
        /// metre in the frame of the travel direction at the point (u along, v right). Out: city_linespot.txt,
        /// city_linespot_plan.tsv (tag, node=0, kind, edge, seq, x, z: the plans.py format + rows 'frame').
        /// </summary>
        public static void RunLineSpot()
        {
            outLog = new StringBuilder(); failures = 0;
            var inv = CultureInfo.InvariantCulture;
            var map = CityMap.Get();
            if (map == null) { Fail("charlotte_city.bytes missing"); FinishTo("city_linespot.txt"); return; }
            var at3 = EnvS("PSX_SPOT_AT", "2487,8201,273").Split(',');
            var P0 = new Vector2(float.Parse(at3[0], inv), float.Parse(at3[1], inv));
            float hdg = at3.Length > 2 ? float.Parse(at3[2], inv) : 0f;
            float ahead = EnvF("PSX_SPOT_AHEAD", 450f), back = EnvF("PSX_SPOT_BACK", 150f);
            string tag = EnvS("PSX_SPOT_TAG", LineModel.SplitAxis);
            var fwd = new Vector2(Mathf.Sin(hdg * Mathf.Deg2Rad), Mathf.Cos(hdg * Mathf.Deg2Rad));
            var right = new Vector2(fwd.y, -fwd.x);
            var t = CityMeshes.ComputeTrims(map);
            CityMeshes.EnsureMarks(map, t);
            if (!map.NearestRoadPoint(P0, 60f, false, out int e0, out float s0, out _)) { Fail("no road within 60 m of the point"); FinishTo("city_linespot.txt"); return; }
            var E0 = map.edges[e0];
            Line($"LINE SPOT '{tag}': ({P0.x:0.0},{P0.y:0.0}) hdg {hdg:0.0}; PSX_CITY_SPLITAXIS {LineModel.SplitAxis}, taper floors {(LineModel.TaperFloorOn ? "on" : "off")}, PSX_CITY_LINEFIX {(LineModel.LineFixOn ? "on" : "off")}; graph {map.graphHash:x8}; on e{e0} '{E0.name}' s {s0:0.0}");
            Line($"  LANE FIT (the seven-lane street, PSX_CITY_LINEFIX): {LineModel.LaneFitWays} ways listed, {LineModel.LaneFitEdges} edges on the seven-lane profile with their tagged lanes");
            { int q = 0; foreach (var l in LineModel.LaneFitLog) if (q++ < 80) Line("    " + l); }
            Line($"  RELAYS city-wide: {LineModel.RelayJoins} joins relayed ({LineModel.RelayShort} short, {LineModel.RelayBayCross} bay crossings); {LineModel.RelayHandOvers} hand-overs with no line moved; {LineModel.OuterLaneEnds} lane-count changes eased on the outer turn lane's side; {LineModel.CentreRunsMade} short centre-lane bays drawn as two-way lanes ({LineModel.CentreRunEdges} edges)");
            var splitNodes = new HashSet<int>();
            if (map.splt != null) foreach (var sp in map.splt) splitNodes.Add(sp.node);
            // the chain: ahead and back along the same name (the straightest continuation at each node)
            var chain = new List<(int e, int dir, float d0)>();   // d0: the chain distance of the edge's near end from the point (+ ahead)
            {
                int dir0 = Vector2.Dot(E0.TangentAt(s0), fwd) >= 0f ? 1 : -1;
                chain.Add((e0, dir0, dir0 > 0 ? -s0 : -(E0.length - s0)));
                for (int pass = 0; pass < 2; pass++)
                {
                    var cur = E0; int d = pass == 0 ? dir0 : -dir0;
                    float dist = pass == 0 ? (dir0 > 0 ? E0.length - s0 : s0) : (dir0 > 0 ? s0 : E0.length - s0);
                    float lim = pass == 0 ? ahead : back;
                    for (int g = 0; g < 40 && dist < lim; g++)
                    {
                        int nd = d > 0 ? cur.b : cur.a;
                        var tin = d > 0 ? cur.TangentAt(cur.length) : -cur.TangentAt(0f);
                        CityMap.Edge best = null; int bd = 0; float bc = 0.5f;
                        foreach (int i in map.nodeEdges[nd])
                        {
                            var o = map.edges[i];
                            if (o == cur || o.a == o.b || o.name != E0.name) continue;
                            int od = o.a == nd ? 1 : -1;
                            // a one-way carriageway is taken the way it runs when ahead, against it when walking back
                            if (o.oneway && ((pass == 0) != (od > 0))) continue;
                            var tout = od > 0 ? o.TangentAt(0f) : -o.TangentAt(o.length);
                            float c = Vector2.Dot(tin, tout);
                            if (c > bc) { bc = c; best = o; bd = od; }
                        }
                        if (best == null) break;
                        chain.Add((best.index, pass == 0 ? bd : -bd, pass == 0 ? dist : -(dist + best.length)));
                        dist += best.length; cur = best; d = bd;
                    }
                }
                chain.Sort((x, y) => x.d0.CompareTo(y.d0));
            }
            var plan = new StringBuilder("tag\tnode\tkind\tedge\tseq\tx\tz\n");
            void PL(string kind, int e, int seq, Vector2 p) => plan.Append(string.Format(inv, "{0}\t0\t{1}\t{2}\t{3}\t{4:0.00}\t{5:0.00}\n", tag, kind, e, seq, p.x, p.y));
            PL("node", -1, 0, P0); PL("axis", -1, 0, P0); PL("axis", -1, 1, P0 + fwd * 10f);
            var la = new List<LineModel.LineAt>(16);
            var taprBy = new Dictionary<int, List<int>>();
            if (map.tapr != null) for (int i = 0; i < map.tapr.Length; i++) { if (!taprBy.TryGetValue(map.tapr[i].edge, out var l)) taprBy[map.tapr[i].edge] = l = new List<int>(); l.Add(i); }
            string KindName(byte k) => k == LineModel.KYellow ? "Y" : k == LineModel.KYellowDash ? "Yd" : k == LineModel.KWhiteDash ? "Wd" : "W";
            foreach (var (ei, dir, d0) in chain)
            {
                var e = map.edges[ei];
                int nNear = dir > 0 ? e.a : e.b, nFar = dir > 0 ? e.b : e.a;
                var lay = LineModel.LayoutOf(e);
                var sbL = new StringBuilder();
                for (int k = 0; k < lay.m.Length; k++) sbL.Append($" {KindName(lay.kind[k])}@{lay.m[k]:0.00}");
                CityMeshes.TurnOnly(e, out int toF, out int toB, out int nF, out int nB, out _);
                Line($"e{ei} {(dir > 0 ? "a->b" : "b->a")} u {d0:0}..{d0 + e.length:0} ({e.length:0.0} m) n{nNear}({map.nodeEdges[nNear].Count} arms{(splitNodes.Contains(nNear) ? " SPLT" : "")}{(t.mitre[nNear] ? " mitre" : t.patch[nNear] ? " fan" : "")}) -> n{nFar}({map.nodeEdges[nFar].Count} arms{(splitNodes.Contains(nFar) ? " SPLT" : "")}{(t.mitre[nFar] ? " mitre" : t.patch[nFar] ? " fan" : "")}) {(e.oneway ? "ONE-WAY" : "two-way")} cls {e.cls} {(e.speedKmh > 0 ? e.speedKmh + " km/h" : "untagged")} width {e.width:0.00} lm +{e.lmPlus:0.00} -{e.lmMinus:0.00} off {e.lmOff:0.00} shl {e.shl:0.00} shr {e.shr:0.00} profile {e.profile} lset {(e.hasLset ? "yes" : "NO")} lanes {e.lsNF}+{e.lsNB} (TurnOnly {nF}+{nB}, bits f {toF:x} b {toB:x}) centre {e.lsCentre} flags {e.lsFlags:x} sub {e.lsSub} bike {e.lsBike}; trims {t.TrimAt(e, e.a):0.0}/{t.TrimAt(e, e.b):0.0}");
                Line($"    layout W {lay.W:0.00} twoWay {lay.twoWay} lines (a->b frame, + left):{sbL}");
                if (taprBy.TryGetValue(ei, out var tl))
                    foreach (int i in tl) { var r = map.tapr[i]; Line($"    TAPR end {(r.end == 0 ? "a" : "b")} side {(r.side == 0 ? "+" : "-")} src {r.src} flags {r.flags:x}{((r.flags & 16) != 0 ? " SHIFT" : "")} dw {r.dw:0.00} len {r.len:0.0} room {r.room:0.0} off {r.off:0.00}"); }
                if (e.lmEase != null)
                    foreach (var z in e.lmEase)
                        Line($"    ease {(z.relay ? "RELAY" : z.shift ? "SHIFT" : z.widen ? "WIDEN" : z.aux ? "AUX" : "TAPER")} side {z.side} from {(z.fromA ? "a" : "b")} dw {z.dw:0.00} len {z.len:0.0} d0 {z.d0:0.0} span {z.span:0.00} dwOther {z.dwOther:0.00} two {z.twoRole} tag {z.tag}{(z.prof != null ? $" prof[{z.prof.Length}]" : "")}{(z.partner != null ? " partner " + string.Join(",", System.Array.ConvertAll(z.partner, v => float.IsNaN(v) ? "-" : v.ToString("0.00", inv))) : "")}");
                {
                    var sbS = new StringBuilder();
                    for (float s = 0f; s <= e.length + 1e-3f; s += 10f)
                    {
                        float ss = Mathf.Min(s, e.length);
                        LineModel.CentreAt(e, ss, out float cc, out float hh);
                        LineModel.Extents(e, ss, out float eM2, out float eP2);
                        sbS.Append($" s{ss:0}:c{cc:+0.00;-0.00}/h{hh:0.00}/+{eP2:0.00}-{eM2:0.00}");
                    }
                    Line($"    band (s: centre/half/+ext-ext, a->b frame + left):{sbS}");
                }
                // the drawn edges and lines, each metre, in the point's frame (u along the travel, v right)
                float sA = t.TrimAt(e, e.a), sB = e.length - t.TrimAt(e, e.b);
                int seq = 0; float worst = 0f; string worstAt = "";
                var prev = new Dictionary<int, (float u, float v)>();
                for (float s = sA; s <= sB + 1e-3f; s += 1f, seq++)
                {
                    float ss = Mathf.Min(s, sB);
                    LineModel.Extents(e, ss, out float eM, out float eP);
                    var lf = LineModel.SectionLeft(e, ss); var p = e.PointAt(ss);
                    PL("edgeP", ei, seq, p + lf * eP); PL("edgeM", ei, seq, p - lf * eM);
                    PL("lane", ei, seq, LineModel.OwnLanePoint(e, ss));
                    // the centre line's height along the frame (x = u, z = y): for a drawing in a camera's view
                    PL("ycl", ei, seq, new Vector2(Vector2.Dot(p - P0, fwd), e.YAt(ss)));
                    LineModel.LinesAt(e, ss, la);
                    foreach (var l in la)
                    {
                        byte kd = lay.kind[l.k];
                        string kn = kd == LineModel.KYellow || kd == LineModel.KYellowDash ? "lineY" : kd == LineModel.KWhiteDash ? "lineW" : "lineE";
                        var q = p + lf * l.lat;
                        PL(kn + l.k, ei, seq, q);
                        float u = Vector2.Dot(q - P0, fwd), v = Vector2.Dot(q - P0, right);
                        if (prev.TryGetValue(l.k, out var pv) && Mathf.Abs(u - pv.u) > 0.3f)
                        {
                            float sl = Mathf.Abs((v - pv.v) / (u - pv.u));
                            if (sl > worst) { worst = sl; worstAt = $"line {l.k} ({KindName(kd)}) at u {u:0.0}"; }
                        }
                        prev[l.k] = (u, v);
                    }
                }
                for (int k = 0; k < e.pts.Length; k++) PL("osm", ei, k, e.pts[k]);
                // the cross-section at three places: the lines' laterals (the travel frame: + right) and the lane widths between them
                foreach (float f in new[] { 0.02f, 0.5f, 0.98f })
                {
                    float ss = Mathf.Lerp(sA, sB, f);
                    LineModel.Extents(e, ss, out float eM, out float eP);
                    LineModel.LinesAt(e, ss, la);
                    var xs = new List<(float v, string n)>();
                    xs.Add((dir > 0 ? eM : eP, "edge")); xs.Add((dir > 0 ? -eP : -eM, "edge"));
                    foreach (var l in la) xs.Add((dir > 0 ? -l.lat : l.lat, KindName(lay.kind[l.k])));
                    xs.Sort((x, y) => x.v.CompareTo(y.v));
                    var sx = new StringBuilder();
                    for (int i = 0; i < xs.Count; i++) sx.Append((i > 0 ? $" [{xs[i].v - xs[i - 1].v:0.00}] " : "") + $"{xs[i].n}@{xs[i].v:+0.00;-0.00}");
                    Line($"    section s {ss:0.0} (left -> right of travel): {sx}");
                }
                Line($"    steepest drawn line on this edge: {worst:0.000} m/m ({(worst > 1e-4f ? (1f / worst).ToString("0.0", inv) + ":1" : "-")}) {worstAt}");
            }
            // the plan also carries every other edge of the road inside the frame's box (the other carriageway past a split)
            {
                var inChain = new HashSet<int>(); foreach (var c in chain) inChain.Add(c.e);
                foreach (var e in map.edges)
                {
                    if (inChain.Contains(e.index) || e.a == e.b || e.tunnel || e.name != E0.name) continue;
                    var mid = e.PointAt(0.5f * e.length);
                    float um = Vector2.Dot(mid - P0, fwd), vm = Vector2.Dot(mid - P0, right);
                    if (um < -back - 40f || um > ahead + 40f || Mathf.Abs(vm) > 60f) continue;
                    var lay = LineModel.LayoutOf(e);
                    float sA = t.TrimAt(e, e.a), sB = e.length - t.TrimAt(e, e.b);
                    int seq = 0;
                    for (float s = sA; s <= sB + 1e-3f; s += 1f, seq++)
                    {
                        float ss = Mathf.Min(s, sB);
                        LineModel.Extents(e, ss, out float eM, out float eP);
                        var lf = LineModel.SectionLeft(e, ss); var p = e.PointAt(ss);
                        PL("edgeP", e.index, seq, p + lf * eP); PL("edgeM", e.index, seq, p - lf * eM);
                        LineModel.LinesAt(e, ss, la);
                        foreach (var l in la)
                        {
                            byte kd = lay.kind[l.k];
                            string kn = kd == LineModel.KYellow || kd == LineModel.KYellowDash ? "lineY" : kd == LineModel.KWhiteDash ? "lineW" : "lineE";
                            PL(kn + l.k, e.index, seq, p + lf * l.lat);
                        }
                    }
                    for (int k = 0; k < e.pts.Length; k++) PL("osm", e.index, k, e.pts[k]);
                    Line($"(also in the plan) e{e.index} {(e.oneway ? "ONE-WAY" : "two-way")} {e.length:0.0} m lanes {e.lsNF}+{e.lsNB} width {e.width:0.00}{(LineModel.TightTwinEdge(e.index) ? " [tight twin]" : "")}{(LineModel.SplitFrameOf(e.index) != null ? " [axis-frame]" : "")} n{e.a} -> n{e.b}");
                    if (taprBy.TryGetValue(e.index, out var tl2))
                        foreach (int i in tl2) { var r = map.tapr[i]; Line($"    TAPR end {(r.end == 0 ? "a" : "b")} side {(r.side == 0 ? "+" : "-")} src {r.src} flags {r.flags:x} dw {r.dw:0.00} len {r.len:0.0} room {r.room:0.0} off {r.off:0.00}"); }
                    if (e.lmEase != null)
                        foreach (var z in e.lmEase)
                            Line($"    ease {(z.relay ? "RELAY" : z.shift ? "SHIFT" : z.widen ? "WIDEN" : z.aux ? "AUX" : "TAPER")} side {z.side} from {(z.fromA ? "a" : "b")} dw {z.dw:0.00} len {z.len:0.0} d0 {z.d0:0.0} tag {z.tag}{(z.prof != null ? $" prof[{z.prof.Length}]" : "")}");
                    // the lane band's centre off the edge's own line (a->b frame, + left) and the pavement's extents, every 10 m
                    {
                        var sbS = new StringBuilder();
                        for (float s = 0f; s <= e.length + 1e-3f; s += 10f)
                        {
                            float ss = Mathf.Min(s, e.length);
                            LineModel.CentreAt(e, ss, out float cc, out float hh);
                            LineModel.Extents(e, ss, out float eM2, out float eP2);
                            sbS.Append($" s{ss:0}:c{cc:+0.00;-0.00}/h{hh:0.00}/+{eP2:0.00}-{eM2:0.00}");
                        }
                        Line($"    band (s: centre/half/+ext-ext):{sbS}");
                    }
                }
            }
            Line($"  TIGHT TWINS: {LineModel.TightTwinPairs} pairs city-wide ({LineModel.TwinOwnBlends} own-frame blends, {LineModel.TwinOuterLanes} outer lanes added, {LineModel.TwinPruned} splits pruned before sampling); split frames {LineModel.SplitFrames.Count}; skipped {LineModel.SplitSkipped.Count}");
            // the split frames drawn on this road (each carriageway's profile: its blend and where it ends)
            foreach (var f in LineModel.SplitFrames)
            {
                if (f.edge < 0 || f.edge >= map.edges.Length || map.edges[f.edge].name != E0.name) continue;
                var pn = map.nodes[f.node];
                float um = Vector2.Dot(pn - P0, fwd);
                if (um < -back - 40f || um > ahead + 40f) continue;
                Line($"    frame n{f.node} e{f.edge} (chain {string.Join(",", f.chain)}, {f.chainLen:0} m): lambda0 {f.lambda0:+0.00;-0.00} uStart {f.uStart:0.0} L {f.L:0.0} uEnd {f.uEnd:0.0} qEnd {f.qEnd:+0.00;-0.00} slopeEnd {f.slopeEnd:+0.000;-0.000} delta {f.delta:+0.00;-0.00} room {f.room:0} capped {f.capped} tip {f.uTip:0.0} devMax {f.devMax:0.00} {f.note}");
            }
            foreach (var l in LineModel.TightTwinLog) if (l.Contains("'" + E0.name + "'")) Line("    " + l);
            foreach (var l in LineModel.SplitSkipped) if (l.Contains("'" + E0.name + "'")) Line("    skipped " + l);
            string root = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllText(Path.Combine(root, "city_linespot_plan.tsv"), plan.ToString());
            FinishTo("city_linespot.txt");
        }
    }
}
