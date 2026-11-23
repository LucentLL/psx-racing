using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  SERVICE WAYS AS PAVEMENT (2026-11-15; the owner: "continue correcting
    //  roads around the city (including minor roads, driveways, neighborhood
    //  roads, etc)").
    //
    //  THE CAUSE. The export keeps a service road only when it has a name
    //  (tools/city/export_osm.mjs:405) and drops private ones (:406), and
    //  outside the core it keeps a parking aisle only inside a lot of
    //  2000 m2 or more (:1722): the access drives, alleys, drive-throughs,
    //  loading yards and apartment drives of the map extract - 2,300 km of
    //  them - were not in the game, so the lots and buildings they serve
    //  had no way in.
    //
    //  THE RULE. They cannot join the road graph (race routes, saved spots
    //  and refuge lists are keyed to its nodes and edges), so they come in
    //  as PAVEMENT ONLY, from Resources/charlotte_service.bytes (made by
    //  tools/city/service_ways.mjs from the extract and the game's own
    //  data; the graph is read, never written):
    //   - each way's centre line and width (a two-way drive 24 ft, an alley
    //     16 ft, a driveway or drive-through lane 12 ft; narrowed between
    //     buildings, stopped short of a wall, water or a freeway) is laid
    //     as a chain of aisle-only LOTS (CityMap.Lot quads, mitred at the
    //     bends): the tile cuts them into the ground's own lattice, in the
    //     road layer's asphalt, as a parking lot's aisle is - so there is
    //     no second surface over a lot (inside a lot the way IS the lot's
    //     aisle and no quad is added), the collider is the drawn ground,
    //     and no tree, house or lamp is put on it (they all read the lots);
    //   - where a way crosses a lot's ring the lot's kerb opens (a paved
    //     THROAT over each 4 m kerb piece the way meets), the stall rows
    //     across it are taken out and an island on it is paved over;
    //   - each MOUTH on a street is a lot entrance (CityMap.LotEntrance:
    //     the curb and gutter drop to the lip with wings, the walk carries
    //     across, the apron's ramp behind it, no lamp, tree, inlet or bench
    //     on it) and CurbOpeningAt is true on it (wrapped, not replaced).
    //  No node or edge is added: no traffic, no route. The file holds the
    //  graph hash it was made for and is not read for another graph.
    //  PSX_CITY_SERVICE=0: the picture as it was. PSX_CITY_SERVICE_AISLES=0:
    //  without the loose parking aisles outside the core (the core's
    //  service ways alone: a quarter of the pavement).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool ServiceOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SERVICE") != "0";
        public static bool ServiceAislesOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SERVICE_AISLES") != "0";
        /// <summary>(roadsmerge 11-23) a divided road's median stays whole: no service pavement between two
        /// carriageways of one road and no mouth on a carriageway's median side (the way across it is a
        /// crossover the graph does not have: the graph round's list). PSX_CITY_SERVICE_MEDIAN=0: as 11-15.</summary>
        public static bool ServiceMedianOn = System.Environment.GetEnvironmentVariable("PSX_CITY_SERVICE_MEDIAN") != "0";
        static readonly HashSet<int> svcMedianSegs = new HashSet<int>();
        /// <summary>True when p lies between two opposite carriageways of one divided road (same name, one-way,
        /// not links, headings opposed), within 40 m of each, outside both pavements.</summary>
        static bool ServiceInMedian(CityMap map, Vector2 p)
        {
            svcMedianSegs.Clear();
            map.EdgeSegsInRect(p - new Vector2(40f, 40f), p + new Vector2(40f, 40f), svcMedianSegs);
            CityMap.Edge left = null, right = null; Vector2 lt = default; float ld = 41f, rd = 41f;
            foreach (int pk in svcMedianSegs)
            {
                var e = map.edges[pk >> 12];
                if (!e.oneway || e.link || e.cls < 2 || string.IsNullOrEmpty(e.name)) continue;
                CityElevation.ProjectOn(e, p, out float sp);
                sp = Mathf.Clamp(sp, 0f, e.length);
                var q = e.PointAt(sp); var tg = e.TangentAt(sp);
                float d = Vector2.Distance(q, p);
                if (d < e.width * 0.5f) return false;                       // on a pavement
                if (sp <= 0.01f || sp >= e.length - 0.01f) continue;        // beyond its ends: not beside it
                float side = tg.x * (p.y - q.y) - tg.y * (p.x - q.x);       // > 0: p left of the edge
                if (side > 0f) { if (d < ld) { ld = d; left = e; lt = tg; } }
                else if (d < rd) { rd = d; right = e; }
            }
            // a median is LEFT of each carriageway (right-hand traffic): p left of A and left of B, A and B opposed
            if (left == null) return false;
            foreach (int pk in svcMedianSegs)
            {
                var e = map.edges[pk >> 12];
                if (e == left || !e.oneway || e.link || e.name != left.name) continue;
                CityElevation.ProjectOn(e, p, out float sp);
                if (sp <= 0.01f || sp >= e.length - 0.01f) continue;
                var q = e.PointAt(sp); var tg = e.TangentAt(sp);
                if (Vector2.Distance(q, p) > 40f || Vector2.Dot(tg, lt) > -0.8f) continue;
                if (tg.x * (p.y - q.y) - tg.y * (p.x - q.x) > 0f) return true;
            }
            return false;
        }
        public const string ServiceResName = "charlotte_service";
        const uint ServiceMagic = 0x57435653u;   // "SVCW"
        const int ServiceVersion = 1;
        /// <summary>A mitre is at most this many half widths (a bend sharper than ~100 degrees is cut).</summary>
        const float ServiceMitreMax = 1.6f;
        /// <summary>A mouth's opening for CurbOpeningAt: this far either side of its half width, and from inside the pavement to behind the walk.</summary>
        const float ServiceMouthSideM = 0.3f, ServiceMouthInM = 2.5f, ServiceMouthOutM = 4f;

        /// <summary>One piece of a service way: its line (travel order where one-way), the half width at
        /// each point, whether the whole width at a point lies on a parking lot's own asphalt.</summary>
        public sealed class ServiceWay
        {
            public uint osmId; public byte kind, flags;
            public Vector2[] pts; public float[] half; public bool[] onLot;
            public Vector4 box; public float length;
            /// <summary>Its mouths at the first and last point (index into ServiceMouths, -1 none).</summary>
            public int mouthA = -1, mouthB = -1;
            public bool OneWay => (flags & 1) != 0;
            public bool Private => (flags & 2) != 0;
            public bool LooseAisle => (flags & 32) != 0;
        }
        /// <summary>A way's mouth on a street: the edge, arc position, side (+1 left of a to b), the
        /// opening's half width; the lot entrance it became (-1 not installed).</summary>
        public struct ServiceMouth { public int way; public byte end; public int edge; public float s; public sbyte side; public float half; public int entrance; public bool off; }

        public static readonly string[] ServiceKinds = { "service", "alley", "driveway", "drive-through", "parking aisle", "private street", "bus / emergency" };
        public static ServiceWay[] ServiceWays = System.Array.Empty<ServiceWay>();
        public static ServiceMouth[] ServiceMouths = System.Array.Empty<ServiceMouth>();
        /// <summary>The lots and entrances this pass put on the map (-1: none).</summary>
        public static int ServiceLotsFrom = -1, ServiceLotsCount, ServiceEntrancesFrom = -1, ServiceEntrancesCount;
        public static string ServiceSource = "off";
        static CityMap serviceMapFor, serviceInstalledFor;
        static Dictionary<long, List<long>> serviceSegByCell;
        const float ServiceCell = 64f;

        public static class ServiceStats
        {
            public static int ways, mouths, quads, quadsOnLot, throats, stallsCut, islandsCut, lotsCut, points, buildingsCleared, quadsOnStation, mouthsOnStation, quadsInMedian, mouthsOnMedian;
            public static float km, kmOnLot, ms, msRead, msQuads, msLots;
            public static readonly float[] kmByKind = new float[7];
            public static void Reset()
            {
                ways = mouths = quads = quadsOnLot = throats = stallsCut = islandsCut = lotsCut = points = quadsOnStation = mouthsOnStation = quadsInMedian = mouthsOnMedian = 0;
                km = kmOnLot = ms = msRead = msQuads = msLots = 0f;
                for (int i = 0; i < kmByKind.Length; i++) kmByKind[i] = 0f;
            }
            public static string Line()
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"service ways ({(ServiceOn ? "ON" : "OFF (PSX_CITY_SERVICE=0)")}{(ServiceOn && !ServiceAislesOn ? ", loose aisles off (PSX_CITY_SERVICE_AISLES=0)" : "")}; {ServiceSource}): ");
                sb.Append($"{ways} pieces, {km:0.0} km ({kmOnLot:0.0} km on a lot's own asphalt), {points} points; ");
                for (int i = 0; i < kmByKind.Length; i++) if (kmByKind[i] > 0.05f) sb.Append($"{ServiceKinds[i]} {kmByKind[i]:0.0} km, ");
                sb.Append($"{quads} paved quads ({quadsOnLot} segments left to the lot they run on), {throats} kerb openings where a way meets a lot, ");
                sb.Append($"{stallsCut} stalls and {islandsCut} islands taken off the ways in {lotsCut} lots; {mouths} mouths on streets; ");
                sb.Append($"{quadsOnStation} segments and {mouthsOnStation} mouths left to the filling station they lie on; ");
                sb.Append($"{quadsInMedian} segments and {mouthsOnMedian} mouths left out of a divided road's median{(ServiceMedianOn ? "" : " (PSX_CITY_SERVICE_MEDIAN=0: kept)")}; ");
                sb.Append($"{ms:0} ms (read {msRead:0}, quads and throats {msQuads:0}, stalls and islands {msLots:0})");
                return sb.ToString();
            }
        }

        // ---- the file ---------------------------------------------------------
        /// <summary>The ways and mouths of the file for this map (nothing put on the map); false when
        /// there is no file or it was made for another graph.</summary>
        static bool ServiceLoad(CityMap map)
        {
            serviceMapFor = map; serviceInstalledFor = null; serviceSegByCell = null; serviceMouthByTile = null;
            ServiceWays = System.Array.Empty<ServiceWay>(); ServiceMouths = System.Array.Empty<ServiceMouth>();
            ServiceLotsFrom = ServiceEntrancesFrom = -1; ServiceLotsCount = ServiceEntrancesCount = 0;
            ServiceStats.Reset();
            ServiceSource = "off";
            if (!ServiceOn || map == null || map.edges == null) return false;
            var ta = Resources.Load<TextAsset>(ServiceResName);
            if (ta == null) { ServiceSource = "no " + ServiceResName + ".bytes"; return false; }
            var bytes = ta.bytes;
            Resources.UnloadAsset(ta);
            try
            {
                using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes)))
                {
                    if (r.ReadUInt32() != ServiceMagic || r.ReadInt32() != ServiceVersion) { ServiceSource = "bad file"; Debug.LogWarning("[City] charlotte_service.bytes: not a service ways file of this version"); return false; }
                    uint hash = r.ReadUInt32(); int ne = (int)r.ReadUInt32();
                    if (hash != map.graphHash || ne != map.edges.Length)
                    {
                        ServiceSource = "made for another graph";
                        Debug.LogWarning($"[City] charlotte_service.bytes was made for graph {hash:x8} ({ne} edges), the map is {map.graphHash:x8} ({map.edges.Length}): the service ways are left out. Remake it: node tools/city/service_ways.mjs --cache <the extract>");
                        return false;
                    }
                    int nw = (int)r.ReadUInt32();
                    var ways = new List<ServiceWay>(nw);
                    var index = new int[nw];
                    for (int i = 0; i < nw; i++)
                    {
                        var w = new ServiceWay { osmId = r.ReadUInt32(), kind = r.ReadByte(), flags = r.ReadByte() };
                        int n = r.ReadUInt16();
                        float ox = r.ReadSingle(), oz = r.ReadSingle();
                        w.pts = new Vector2[n]; w.half = new float[n]; w.onLot = new bool[n];
                        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                        for (int k = 0; k < n; k++)
                        {
                            var p = new Vector2(ox + r.ReadInt16() * 0.05f, oz + r.ReadInt16() * 0.05f);
                            byte hb = r.ReadByte();
                            w.pts[k] = p; w.half[k] = (hb & 0x7F) * 0.05f; w.onLot[k] = (hb & 0x80) != 0;
                            if (k > 0) w.length += (p - w.pts[k - 1]).magnitude;
                            x0 = Mathf.Min(x0, p.x - w.half[k]); z0 = Mathf.Min(z0, p.y - w.half[k]); x1 = Mathf.Max(x1, p.x + w.half[k]); z1 = Mathf.Max(z1, p.y + w.half[k]);
                        }
                        w.box = new Vector4(x0, z0, x1, z1);
                        if (n < 2 || w.kind >= ServiceKinds.Length || (w.LooseAisle && !ServiceAislesOn)) { index[i] = -1; continue; }
                        index[i] = ways.Count; ways.Add(w);
                    }
                    int nm = (int)r.ReadUInt32();
                    var mouths = new List<ServiceMouth>(nm);
                    for (int i = 0; i < nm; i++)
                    {
                        var m = new ServiceMouth { way = (int)r.ReadUInt32(), end = r.ReadByte(), edge = (int)r.ReadUInt32(), s = r.ReadSingle(), side = r.ReadSByte(), half = r.ReadSingle(), entrance = -1 };
                        if (m.way < 0 || m.way >= nw || m.edge < 0 || m.edge >= map.edges.Length) { ServiceSource = "bad file"; return false; }
                        m.way = index[m.way];
                        if (m.way < 0) continue;
                        if (m.end == 0) ways[m.way].mouthA = mouths.Count; else ways[m.way].mouthB = mouths.Count;
                        mouths.Add(m);
                    }
                    ServiceWays = ways.ToArray(); ServiceMouths = mouths.ToArray();
                }
            }
            catch (System.Exception ex)
            {
                ServiceWays = System.Array.Empty<ServiceWay>(); ServiceMouths = System.Array.Empty<ServiceMouth>();
                ServiceSource = "bad file";
                Debug.LogWarning("[City] charlotte_service.bytes could not be read: " + ex.Message);
                return false;
            }
            ServiceSource = "read from " + ServiceResName + ".bytes";
            foreach (var w in ServiceWays)
            {
                ServiceStats.ways++; ServiceStats.points += w.pts.Length; ServiceStats.km += w.length / 1000f; ServiceStats.kmByKind[w.kind] += w.length / 1000f;
            }
            ServiceStats.mouths = ServiceMouths.Length;
            // the segments by 64 m cell (ServiceWayAt)
            var by = new Dictionary<long, List<long>>();
            for (int wi = 0; wi < ServiceWays.Length; wi++)
            {
                var w = ServiceWays[wi];
                for (int k = 0; k + 1 < w.pts.Length; k++)
                {
                    float h = Mathf.Max(w.half[k], w.half[k + 1]) + 0.5f;
                    Vector2 a = w.pts[k], b = w.pts[k + 1];
                    for (int cx = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - h) / ServiceCell); cx <= Mathf.FloorToInt((Mathf.Max(a.x, b.x) + h) / ServiceCell); cx++)
                        for (int cz = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - h) / ServiceCell); cz <= Mathf.FloorToInt((Mathf.Max(a.y, b.y) + h) / ServiceCell); cz++)
                        {
                            long key = LotTileKey(cx, cz);
                            if (!by.TryGetValue(key, out var l)) by[key] = l = new List<long>(8);
                            l.Add(((long)wi << 16) | (uint)k);
                        }
                }
            }
            serviceSegByCell = by;
            return ServiceWays.Length > 0;
        }

        // ---- the questions ------------------------------------------------------
        /// <summary>FOR THE LOTS PASS AND THE TOOLS: is the plan point on a service way's pavement
        /// (within its width, plus <paramref name="pad"/>)? The way and its segment. Loads the file by
        /// itself when the pass has not run for this map yet; false with PSX_CITY_SERVICE=0.</summary>
        public static bool ServiceWayAt(CityMap map, Vector2 p, float pad, out int way, out int seg)
        {
            way = seg = -1;
            if (!ServiceOn || map == null) return false;
            if (serviceMapFor != map) ServiceLoad(map);
            var by = serviceSegByCell;
            if (by == null || !by.TryGetValue(LotTileKey(Mathf.FloorToInt(p.x / ServiceCell), Mathf.FloorToInt(p.y / ServiceCell)), out var l)) return false;
            foreach (long packed in l)
            {
                int wi = (int)(packed >> 16), k = (int)(packed & 0xFFFF);
                var w = ServiceWays[wi];
                Vector2 a = w.pts[k], ab = w.pts[k + 1] - a;
                float l2 = ab.sqrMagnitude;
                float u = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                float lim = Mathf.Lerp(w.half[k], w.half[k + 1], u) + pad;
                if ((p - (a + ab * u)).sqrMagnitude <= lim * lim) { way = wi; seg = k; return true; }
            }
            return false;
        }

        /// <summary>Does an invented frontage building (CityBuildings.Precompute's box or model; not a
        /// restaurant landmark, not a model fitted on a real footprint) stand on a service way? 25 points
        /// over its footprint.</summary>
        public static bool ServiceUnderBuilding(CityMap map, in CityBuildings.B b)
        {
            if (!ServiceOn || serviceMapFor != map || ServiceWays.Length == 0) return false;
            // (yaw faces the road: forward = (sin, cos); the width lies across it)
            float cs = Mathf.Cos(b.yaw), sn = Mathf.Sin(b.yaw);
            var ax = new Vector2(cs, -sn); var ay = new Vector2(sn, cs);
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    var p = b.pos + ax * (b.w * 0.25f * i) + ay * (b.d * 0.25f * j);
                    if (ServiceWayAt(map, p, 0.3f, out _, out _)) return true;
                }
            return false;
        }

        /// <summary>CityBuildings.Precompute's last step: the frontage's invented buildings that stand on a
        /// service way are taken out (the way is the map's, the box is the generator's guess at an empty
        /// frontage). The ten restaurant landmarks and the models on real footprints stay.</summary>
        public static int ServiceClearBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> byTile)
        {
            ServiceStats.buildingsCleared = 0;
            if (!ServiceOn || byTile == null || serviceInstalledFor != map || ServiceWays.Length == 0) return 0;
            int n = 0;
            foreach (var kv in byTile)
                n += kv.Value.RemoveAll(b => !CityProps.IsFood(b.kind) && b.scale == Vector3.zero && ServiceUnderBuilding(map, b));
            ServiceStats.buildingsCleared = n;
            if (n > 0) Debug.Log($"[City] service ways: {n} invented frontage buildings stood on a way and were taken out");
            return n;
        }

        /// <summary>FOR THE LOTS PASS: "is there service pavement here?"</summary>
        public static bool ServicePavementAt(CityMap map, float x, float z) => ServiceWayAt(map, new Vector2(x, z), 0f, out _, out _);

        // ---- the mouths (CurbOpeningAt) ---------------------------------------------
        static Vector4[] serviceMouthA, serviceMouthB;   // mouth x, z, t x, z; n x, z, half, 0
        static Dictionary<long, List<int>> serviceMouthByTile;
        static readonly object serviceMouthLock = new object();
        static System.Func<CityMap, float, float, bool> serviceHook, servicePrevHook;

        /// <summary>(Built at the first question a tile asks, not at the pass: a road's eases are in
        /// its pavement's extents only after ComputeTrims - as the lot entrances' ramps are.)</summary>
        static void EnsureServiceMouths(CityMap map)
        {
            if (serviceMouthByTile != null) return;
            lock (serviceMouthLock)
            {
                if (serviceMouthByTile != null) return;
                int n = ServiceMouths.Length;
                var A = new Vector4[n]; var B = new Vector4[n];
                var by = new Dictionary<long, List<int>>();
                for (int i = 0; i < n; i++)
                {
                    var m = ServiceMouths[i];
                    if (m.off) continue;
                    LotEntFrame(map.edges[m.edge], m.s, m.side, out var mouth, out var nn, out var t);
                    A[i] = new Vector4(mouth.x, mouth.y, t.x, t.y); B[i] = new Vector4(nn.x, nn.y, m.half, 0f);
                    float r = m.half + ServiceMouthSideM + ServiceMouthOutM + 1f;
                    for (int tx = Mathf.FloorToInt((mouth.x - r) / TileSize); tx <= Mathf.FloorToInt((mouth.x + r) / TileSize); tx++)
                        for (int tz = Mathf.FloorToInt((mouth.y - r) / TileSize); tz <= Mathf.FloorToInt((mouth.y + r) / TileSize); tz++)
                        {
                            long k = LotTileKey(tx, tz);
                            if (!by.TryGetValue(k, out var l)) by[k] = l = new List<int>(4);
                            l.Add(i);
                        }
                }
                serviceMouthA = A; serviceMouthB = B; serviceMouthByTile = by;
            }
        }

        /// <summary>Is the plan point in a service way's mouth (the curb is open there)?</summary>
        public static bool ServiceMouthAt(CityMap map, float x, float z)
        {
            if (!ServiceOn || serviceInstalledFor != map || ServiceMouths.Length == 0) return false;
            EnsureServiceMouths(map);
            if (!serviceMouthByTile.TryGetValue(LotTileKey(Mathf.FloorToInt(x / TileSize), Mathf.FloorToInt(z / TileSize)), out var l)) return false;
            foreach (int i in l)
            {
                var a = serviceMouthA[i]; var b = serviceMouthB[i];
                float dx = x - a.x, dz = z - a.y;
                float dep = dx * b.x + dz * b.y;
                if (dep < -ServiceMouthInM || dep > ServiceMouthOutM) continue;
                if (Mathf.Abs(dx * a.z + dz * a.w) <= b.z + ServiceMouthSideM) return true;
            }
            return false;
        }

        /// <summary>For the tools: a mouth's frame (its middle on the pavement's edge, the way out of the road, along the street).</summary>
        public static void ServiceMouthFrame(CityMap map, int mouth, out Vector2 at, out Vector2 n, out Vector2 t)
        {
            var m = ServiceMouths[mouth];
            LotEntFrame(map.edges[m.edge], m.s, m.side, out at, out n, out t);
        }

        // ---- the pass ---------------------------------------------------------------
        static readonly Vector2[] svcEmptyV2 = System.Array.Empty<Vector2>();
        static readonly byte[] svcEmptyB = System.Array.Empty<byte>();
        static CityMap.Lot ServiceLot(Vector2[] ring, Vector2 u) => new CityMap.Lot
        {
            ring = ring, box = CityStations.BoxOf(ring), u = u,
            runFoot = svcEmptyV2, runN = svcEmptyB, runU = svcEmptyV2,
            aisleOnly = true,
        };

        static bool ServiceConvexCcw(Vector2[] q)
        {
            for (int i = 0; i < 4; i++)
                if (Cross2(q[(i + 1) & 3] - q[i], q[(i + 2) & 3] - q[(i + 1) & 3]) <= 1e-4f) return false;
            return true;
        }

        /// <summary>Do two convex quads overlap (separating axes)?</summary>
        static bool ServiceQuadsMeet(Vector2[] A, Vector2[] B)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var P = pass == 0 ? A : B;
                for (int i = 0; i < 4; i++)
                {
                    var d = P[(i + 1) & 3] - P[i];
                    var ax = new Vector2(-d.y, d.x);
                    float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        float va = Vector2.Dot(A[k], ax), vb = Vector2.Dot(B[k], ax);
                        a0 = Mathf.Min(a0, va); a1 = Mathf.Max(a1, va); b0 = Mathf.Min(b0, vb); b1 = Mathf.Max(b1, vb);
                    }
                    if (a1 < b0 || b1 < a0) return false;
                }
            }
            return true;
        }

        static bool ServiceInQuad(Vector2[] q, Vector2 p, float pad)
        {
            for (int i = 0; i < 4; i++)
            {
                var d = q[(i + 1) & 3] - q[i];
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                if (Cross2(d, p - q[i]) / len < -pad) return false;
            }
            return true;
        }

        /// <summary>
        /// THE PASS (once per map, in CityMap.Parse after the lot entrances'
        /// pass): the file's ways as aisle-only lots, the kerb openings where
        /// they meet a lot, the stalls and islands off them, the mouths as lot
        /// entrances, CurbOpeningAt wrapped. Deterministic from the file and
        /// the map.
        /// </summary>
        public static void InstallServiceWays(CityMap map)
        {
            if (map == null) return;
            if (serviceInstalledFor == map) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!ServiceOn || !LotsOn || map.lots == null) { ServiceLoad(null); ServiceSource = !ServiceOn ? "off" : "the lots are off"; return; }
            // (the lot entrances' bake writes every lot and entrance from its first to the END of the map's
            // arrays into charlotte_lotent.bytes: with this pass's behind them it would keep them as its own,
            // and they would come twice at the next load. So the bake runs without this pass.)
            if (System.Environment.GetEnvironmentVariable("PSX_LOTENT_LIVE") == "1" && System.Environment.GetEnvironmentVariable("PSX_CITY_SERVICE_INBAKE") != "1")
            {
                ServiceLoad(null); ServiceSource = "left out of the lot entrances' bake (PSX_LOTENT_LIVE=1)";
                Debug.Log("[City] service ways: " + ServiceSource);
                return;
            }
            bool have = serviceMapFor == map && ServiceWays.Length > 0 ? true : ServiceLoad(map);
            if (!have) { if (ServiceSource != "off") Debug.Log("[City] service ways: " + ServiceSource); return; }
            ServiceStats.msRead = (float)clock.Elapsed.TotalMilliseconds;
            EnsureLots(map);
            svcRunBoxes.Clear();
            int lots0 = map.lots.Length;
            var lots = new List<CityMap.Lot>(ServiceStats.points);
            var quad = new Vector2[4];
            var seen = new HashSet<long>();
            var cutStalls = new Dictionary<int, HashSet<int>>();
            var cutIslands = new Dictionary<int, HashSet<int>>();
            var stall = new Vector2[4];
            var L = new List<Vector2>(64); var R = new List<Vector2>(64);
            double tLots = 0;
            foreach (var w in ServiceWays)
            {
                int n = w.pts.Length;
                // the mitred sides
                L.Clear(); R.Clear();
                for (int i = 0; i < n; i++)
                {
                    Vector2 dP = i > 0 ? (w.pts[i] - w.pts[i - 1]).normalized : Vector2.zero, dN = i + 1 < n ? (w.pts[i + 1] - w.pts[i]).normalized : Vector2.zero;
                    Vector2 m; float scale = 1f;
                    if (i == 0) m = new Vector2(-dN.y, dN.x);
                    else if (i == n - 1) m = new Vector2(-dP.y, dP.x);
                    else
                    {
                        var t = dP + dN;
                        if (t.sqrMagnitude < 0.04f) m = new Vector2(-dN.y, dN.x);
                        else
                        {
                            t.Normalize(); m = new Vector2(-t.y, t.x);
                            scale = Mathf.Min(ServiceMitreMax, 1f / Mathf.Max(0.2f, Vector2.Dot(m, new Vector2(-dN.y, dN.x))));
                        }
                    }
                    L.Add(w.pts[i] + m * (w.half[i] * scale)); R.Add(w.pts[i] - m * (w.half[i] * scale));
                }
                // one tone for every way: the weathered asphalt (a fresh black piece between two grey ones read as a patch)
                const bool fresh = false;
                for (int i = 0; i + 1 < n; i++)
                {
                    var d = w.pts[i + 1] - w.pts[i]; float len = d.magnitude;
                    if (len < 0.3f) continue;
                    d /= len;
                    quad[0] = R[i]; quad[1] = R[i + 1]; quad[2] = L[i + 1]; quad[3] = L[i];
                    if (!ServiceConvexCcw(quad))
                    {
                        // a short segment between two sharp bends: its own rectangle, lapped on its neighbours
                        var lf = new Vector2(-d.y, d.x); float h = Mathf.Max(w.half[i], w.half[i + 1]);
                        quad[0] = w.pts[i] - d * 0.3f - lf * h; quad[1] = w.pts[i + 1] + d * 0.3f - lf * h; quad[2] = w.pts[i + 1] + d * 0.3f + lf * h; quad[3] = w.pts[i] - d * 0.3f + lf * h;
                    }
                    // the lots this segment lies on or meets: their stalls and islands off it; their kerb open where it crosses
                    var t0 = clock.Elapsed.TotalMilliseconds;
                    float bx0 = Mathf.Min(Mathf.Min(quad[0].x, quad[1].x), Mathf.Min(quad[2].x, quad[3].x)), bx1 = Mathf.Max(Mathf.Max(quad[0].x, quad[1].x), Mathf.Max(quad[2].x, quad[3].x));
                    float bz0 = Mathf.Min(Mathf.Min(quad[0].y, quad[1].y), Mathf.Min(quad[2].y, quad[3].y)), bz1 = Mathf.Max(Mathf.Max(quad[0].y, quad[1].y), Mathf.Max(quad[2].y, quad[3].y));
                    // a filling station's forecourt is the station's own (its lot, pumps, shop, curb cuts): no quad on it
                    {
                        float hs = Mathf.Max(w.half[i], w.half[i + 1]);
                        if (CityStations.LotAt(map, 0.5f * (w.pts[i] + w.pts[i + 1]), hs) >= 0 || CityStations.LotAt(map, w.pts[i], hs) >= 0 || CityStations.LotAt(map, w.pts[i + 1], hs) >= 0)
                        { ServiceStats.quadsOnStation++; continue; }
                        // a divided road's median is not cut (the crossovers are not in the graph)
                        if (ServiceMedianOn && (ServiceInMedian(map, 0.5f * (w.pts[i] + w.pts[i + 1])) || ServiceInMedian(map, w.pts[i]) || ServiceInMedian(map, w.pts[i + 1])))
                        { ServiceStats.quadsInMedian++; continue; }
                    }
                    bool lies = w.onLot[i] && w.onLot[i + 1];
                    for (int tx = Mathf.FloorToInt((bx0 - 2.5f) / TileSize); tx <= Mathf.FloorToInt((bx1 + 2.5f) / TileSize); tx++)
                        for (int tz = Mathf.FloorToInt((bz0 - 2.5f) / TileSize); tz <= Mathf.FloorToInt((bz1 + 2.5f) / TileSize); tz++)
                        {
                            if (!lotsByTile.TryGetValue(LotTileKey(tx, tz), out var ll)) continue;
                            foreach (int li in ll)
                            {
                                var lot = map.lots[li]; var b = lot.box;
                                if (lot.aisleOnly || lot.ring == null || lot.ring.Length < 3) continue;
                                if (b.z < bx0 - 2.5f || b.x > bx1 + 2.5f || b.w < bz0 - 2.5f || b.y > bz1 + 2.5f) continue;
                                ServiceClearLot(map, li, lot, quad, stall, cutStalls, cutIslands);
                                if (!lies) ServiceThroats(map, li, lot, quad, bx0, bz0, bx1, bz1, seen, lots, fresh);
                            }
                        }
                    tLots += clock.Elapsed.TotalMilliseconds - t0;
                    if (lies) { ServiceStats.quadsOnLot++; ServiceStats.kmOnLot += len / 1000f; continue; }
                    var ring = new[] { quad[0], quad[1], quad[2], quad[3] };
                    // one tone along a way (IsFresh reads a ring's first point: slid along the joint's own line, outward)
                    var slide = (ring[0] - ring[3]).normalized;
                    for (int k = 0; k < 60 && IsFresh(ring[0]) != fresh; k++) ring[0] += slide * 0.011f;
                    lots.Add(ServiceLot(ring, d));
                    ServiceStats.quads++;
                }
            }
            ServiceStats.msLots = (float)tLots;
            // the stalls and islands off the ways
            foreach (var kv in cutStalls)
            {
                var lot = map.lots[kv.Key];
                var feet = new List<Vector2>(lot.runFoot.Length + 4); var ns = new List<byte>(); var us = new List<Vector2>();
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    var u = lot.runU != null && lot.runU.Length > r ? lot.runU[r] : lot.u;
                    int cnt = lot.runN[r], start = -1;
                    for (int k = 0; k <= cnt; k++)
                    {
                        bool keep = k < cnt && !kv.Value.Contains(r * 256 + k);
                        if (keep && start < 0) start = k;
                        if (!keep && start >= 0) { feet.Add(lot.runFoot[r] + u * (start * StallW)); ns.Add((byte)(k - start)); us.Add(u); start = -1; }
                    }
                }
                ServiceStats.stallsCut += kv.Value.Count;
                lot.runFoot = feet.ToArray(); lot.runN = ns.ToArray(); lot.runU = us.ToArray();
            }
            foreach (var kv in cutIslands)
            {
                var lot = map.lots[kv.Key];
                var hs = new List<Vector2[]>(lot.holes.Length); var hk = new List<byte>(lot.holes.Length);
                for (int h = 0; h < lot.holes.Length; h++) if (!kv.Value.Contains(h)) { hs.Add(lot.holes[h]); hk.Add(lot.holeKind[h]); }
                ServiceStats.islandsCut += kv.Value.Count;
                lot.holes = hs.ToArray(); lot.holeKind = hk.ToArray();
            }
            var touched = new HashSet<int>(cutStalls.Keys); touched.UnionWith(cutIslands.Keys);
            ServiceStats.lotsCut = touched.Count;
            // onto the map: the lots, then the mouths as lot entrances
            if (lots.Count > 0)
            {
                var all = new CityMap.Lot[lots0 + lots.Count];
                System.Array.Copy(map.lots, all, lots0);
                lots.CopyTo(all, lots0);
                map.lots = all; ServiceLotsFrom = lots0; ServiceLotsCount = lots.Count;
            }
            if (ServiceMouths.Length > 0)
            {
                int e0 = map.lotEntrances != null ? map.lotEntrances.Length : 0;
                var ents = new CityMap.LotEntrance[e0 + ServiceMouths.Length];
                if (e0 > 0) System.Array.Copy(map.lotEntrances, ents, e0);
                int ne = 0;
                for (int i = 0; i < ServiceMouths.Length; i++)
                {
                    var m = ServiceMouths[i];
                    // (a mouth on a filling station's frontage: the station has its own curb cuts)
                    var e = map.edges[m.edge];
                    float sc = Mathf.Clamp(m.s, 0f, e.length);
                    var tg = e.TangentAt(sc);
                    var behind = e.PointAt(sc) + new Vector2(-tg.y, tg.x) * (m.side * (e.width * 0.5f + 4f));
                    if (CityStations.LotAt(map, behind, 2f) >= 0) { ServiceMouths[i].off = true; ServiceStats.mouthsOnStation++; continue; }
                    // a mouth on a carriageway's median side: the median stays whole
                    if (ServiceMedianOn && e.oneway && !e.link && ServiceInMedian(map, e.PointAt(sc) + new Vector2(-tg.y, tg.x) * (m.side * (e.width * 0.5f + 1.5f))))
                    { ServiceMouths[i].off = true; ServiceStats.mouthsOnMedian++; continue; }
                    ents[e0 + ne] = new CityMap.LotEntrance { edge = m.edge, s = m.s, side = m.side, half = m.half };
                    ServiceMouths[i].entrance = e0 + ne;
                    ne++;
                }
                if (ne < ServiceMouths.Length) System.Array.Resize(ref ents, e0 + ne);
                map.lotEntrances = ents; ServiceEntrancesFrom = e0; ServiceEntrancesCount = ne;
            }
            // the lots' caches again with the ways in (as the lot entrances' pass ends)
            lotMapFor = null; leEntByTile = null; lotEntRampByTile = null;
            serviceInstalledFor = map;
            // the curb's opening: the hook WRAPPED (the lot entrances and the inlets ask it too)
            if (serviceHook == null || CurbOpeningAt != serviceHook)
            {
                servicePrevHook = CurbOpeningAt;
                var prev = servicePrevHook;
                serviceHook = (m, x, z) => (prev != null && prev(m, x, z)) || ServiceMouthAt(m, x, z);
                CurbOpeningAt = serviceHook;
            }
            ServiceStats.ms = (float)clock.Elapsed.TotalMilliseconds;
            ServiceStats.msQuads = ServiceStats.ms - ServiceStats.msRead - ServiceStats.msLots;
            Debug.Log("[City] " + ServiceStats.Line());
        }

        static readonly Dictionary<int, Vector4[]> svcRunBoxes = new Dictionary<int, Vector4[]>();
        static Vector4[] svcRunBox;

        /// <summary>The stalls of the lot whose rectangle meets the way's quad, and the islands on it.</summary>
        static void ServiceClearLot(CityMap map, int li, CityMap.Lot lot, Vector2[] quad, Vector2[] stall, Dictionary<int, HashSet<int>> cutStalls, Dictionary<int, HashSet<int>> cutIslands)
        {
            if (lot.runFoot.Length == 0 && lot.holes.Length == 0) return;
            float qx0 = Mathf.Min(Mathf.Min(quad[0].x, quad[1].x), Mathf.Min(quad[2].x, quad[3].x)), qx1 = Mathf.Max(Mathf.Max(quad[0].x, quad[1].x), Mathf.Max(quad[2].x, quad[3].x));
            float qz0 = Mathf.Min(Mathf.Min(quad[0].y, quad[1].y), Mathf.Min(quad[2].y, quad[3].y)), qz1 = Mathf.Max(Mathf.Max(quad[0].y, quad[1].y), Mathf.Max(quad[2].y, quad[3].y));
            // each run's box, once per lot
            if (lot.runFoot.Length > 0 && !svcRunBoxes.TryGetValue(li, out svcRunBox))
            {
                svcRunBox = new Vector4[lot.runFoot.Length];
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    var u = lot.runU != null && lot.runU.Length > r ? lot.runU[r] : lot.u;
                    var v = new Vector2(-u.y, u.x);
                    Vector2 a = lot.runFoot[r], b = a + u * (lot.runN[r] * StallW), c2 = b + v * StallD, d2 = a + v * StallD;
                    svcRunBox[r] = new Vector4(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c2.x, d2.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c2.y, d2.y)), Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c2.x, d2.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c2.y, d2.y)));
                }
                svcRunBoxes[li] = svcRunBox;
            }
            for (int r = 0; r < lot.runFoot.Length; r++)
            {
                var rb = svcRunBox[r];
                if (rb.z < qx0 || rb.x > qx1 || rb.w < qz0 || rb.y > qz1) continue;
                var u = lot.runU != null && lot.runU.Length > r ? lot.runU[r] : lot.u;
                var v = new Vector2(-u.y, u.x);
                int cnt = lot.runN[r];
                // the run's rectangle first
                stall[0] = lot.runFoot[r]; stall[1] = stall[0] + u * (cnt * StallW); stall[2] = stall[1] + v * StallD; stall[3] = stall[0] + v * StallD;
                if (!ServiceQuadsMeet(quad, stall)) continue;
                for (int k = 0; k < cnt; k++)
                {
                    stall[0] = lot.runFoot[r] + u * (k * StallW); stall[1] = stall[0] + u * StallW; stall[2] = stall[1] + v * StallD; stall[3] = stall[0] + v * StallD;
                    if (!ServiceQuadsMeet(quad, stall)) continue;
                    if (!cutStalls.TryGetValue(li, out var set)) cutStalls[li] = set = new HashSet<int>();
                    set.Add(r * 256 + k);
                }
            }
            var hbs = lotHoleBox != null && li < lotHoleBox.Length ? lotHoleBox[li] : null;
            for (int h = 0; h < lot.holes.Length; h++)
            {
                if (lot.holeKind[h] != 1) continue;
                if (hbs != null && h < hbs.Length) { var hb = hbs[h]; if (hb.z < qx0 - 0.3f || hb.x > qx1 + 0.3f || hb.w < qz0 - 0.3f || hb.y > qz1 + 0.3f) continue; }
                var ring = lot.holes[h];
                // (corners in the quad, the quad's corners in it, or two edges crossing: the service drive found an
                // island whose side cut a quad's corner with every corner of both outside - a wheel off for 0.27 s)
                bool hit = false;
                var c = 0.25f * (quad[0] + quad[1] + quad[2] + quad[3]);
                if (InRing(ring, c)) hit = true;
                for (int k = 0; k < 4 && !hit; k++) if (InRing(ring, quad[k])) hit = true;
                for (int k = 0; k < ring.Length && !hit; k++)
                {
                    Vector2 ra = ring[k], rb = ring[(k + 1) % ring.Length];
                    if (ServiceInQuad(quad, ra, 0.3f) || ServiceInQuad(quad, 0.5f * (ra + rb), 0.3f)) { hit = true; break; }
                    for (int j = 0; j < 4 && !hit; j++)
                    {
                        Vector2 qa = quad[j], qb = quad[(j + 1) & 3];
                        float d1 = Cross2(rb - ra, qa - ra), d2 = Cross2(rb - ra, qb - ra), d3 = Cross2(qb - qa, ra - qa), d4 = Cross2(qb - qa, rb - qa);
                        if ((d1 > 0f) != (d2 > 0f) && (d3 > 0f) != (d4 > 0f)) hit = true;
                    }
                }
                if (!hit) continue;
                if (!cutIslands.TryGetValue(li, out var set)) cutIslands[li] = set = new HashSet<int>();
                set.Add(h);
            }
        }

        /// <summary>
        /// The lot's kerb where the way's quad crosses its ring: EmitLotCurbs
        /// lays each ring edge in equal pieces of 4 m or less and drops a piece
        /// whose middle, 0.8 m out, lies on another lot. Every piece the quad
        /// meets whose middle the ways do not cover gets a paved THROAT over it
        /// (0.4 m inside to 1.7 m outside the ring), so no kerb stands across
        /// the way.
        /// </summary>
        static void ServiceThroats(CityMap map, int li, CityMap.Lot lot, Vector2[] quad, float bx0, float bz0, float bx1, float bz1, HashSet<long> seen, List<CityMap.Lot> lots, bool fresh)
        {
            var r = lot.ring; int nr = r.Length;
            float area = 0f;
            for (int i = 0; i < nr; i++) { var p = r[i]; var q = r[(i + 1) % nr]; area += p.x * q.y - q.x * p.y; }
            float sgn = area > 0f ? 1f : -1f;
            for (int i = 0; i < nr; i++)
            {
                Vector2 a = r[i], b = r[(i + 1) % nr];
                if (Mathf.Max(a.x, b.x) < bx0 - 0.3f || Mathf.Min(a.x, b.x) > bx1 + 0.3f || Mathf.Max(a.y, b.y) < bz0 - 0.3f || Mathf.Min(a.y, b.y) > bz1 + 0.3f) continue;
                var d = b - a; float len = d.magnitude;
                if (len < 0.05f) continue;
                // the part of the edge inside the quad (grown 0.2 m)
                float u0 = 0f, u1 = 1f; bool any = true;
                for (int k = 0; k < 4 && any; k++)
                {
                    var e0 = quad[k]; var ed = quad[(k + 1) & 3] - e0; float el = ed.magnitude;
                    if (el < 1e-4f) continue;
                    float fa = Cross2(ed, a - e0) / el + 0.2f, fb = Cross2(ed, b - e0) / el + 0.2f;
                    if (fa < 0f && fb < 0f) { any = false; break; }
                    if (fa < 0f) u0 = Mathf.Max(u0, fa / (fa - fb)); else if (fb < 0f) u1 = Mathf.Min(u1, fa / (fa - fb));
                }
                if (!any || u1 <= u0) continue;
                var outw = new Vector2(d.y, -d.x) / len * sgn;
                int kp = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                for (int j = Mathf.Clamp(Mathf.FloorToInt(u0 * kp), 0, kp - 1); j <= Mathf.Clamp(Mathf.FloorToInt(u1 * kp - 1e-4f), 0, kp - 1); j++)
                {
                    long key = ((long)li << 32) | ((long)(uint)i << 10) | (uint)j;
                    if (!seen.Add(key)) continue;
                    var pa = a + d * ((float)j / kp); var pb = a + d * ((float)(j + 1) / kp);
                    var land = 0.5f * (pa + pb) + outw * 0.8f;
                    // (a piece whose test point this quad covers is dropped by the quad itself; a neighbouring
                    // segment that covers it may lie on the lot and lay no quad, so nothing else is trusted)
                    if (ServiceInQuad(quad, land, -0.05f)) continue;
                    var ring = new[] { pa - outw * 0.4f, pb - outw * 0.4f, pb + outw * 1.7f, pa + outw * 1.7f };
                    if (Cross2(ring[1] - ring[0], ring[2] - ring[1]) < 0f) System.Array.Reverse(ring);
                    for (int k = 0; k < 60 && IsFresh(ring[0]) != fresh; k++) ring[0] -= outw * 0.011f;
                    lots.Add(ServiceLot(ring, d / len));
                    ServiceStats.throats++;
                }
            }
        }
    }
}
