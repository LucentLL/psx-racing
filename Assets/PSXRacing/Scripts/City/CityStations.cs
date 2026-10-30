using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CHARLOTTE'S FILLING STATIONS (city fuel r1, PHASE B; the owner,
    /// 2026-10-30: "While working on the city, I'd like gas station models we
    /// have to be used where real gas station locations are").
    ///
    /// tools/city/stations.mjs decides, offline, where each of OSM's 300
    /// stations stands and how (its layout, its driveways, the boxes it stands
    /// in place of) and writes Resources/charlotte_fuel.bytes (PFUL v1, the
    /// layout in the tool's header). This class reads it ONCE, at the end of
    /// <see cref="CityMap.Parse"/> (<see cref="Install"/>), and joins every
    /// station to the map's own data, so nothing downstream needs to know a
    /// station from any other lot:
    ///   * THE LOT, paved: a rectangle appended to <see cref="CityMap.lots"/>
    ///     (no stalls; cut flush into the lattice like every lot, curbed where
    ///     it meets grass), with a hole (the ground shows) under any building
    ///     that stays on it, as an OSM lot has;
    ///   * THE DRIVEWAYS: a curb cut each (<see cref="CityMap.lotEntrances"/>:
    ///     the verge across the mouth poured concrete, the walk's curb dropped
    ///     to a lip, ApronAt) and, where the lot stands back from the street,
    ///     the paving from the verge to the lot (another lot rectangle); a big
    ///     store's fuel point's aisle to the paving near it likewise;
    ///   * THE OSM LOTS IT OVERLAPS take a hole the size of the station's lot
    ///     (inserted first: the station paves it) and lose any island (a raised
    ///     curbed bed) standing on it;
    ///   * THE BOXES IT REPLACES (OSM's own canopy or kiosk, matched by its
    ///     polygon's area centroid within 0.5 m) are hidden and marked
    ///     <see cref="CityMap.Footprint.replaced"/>: no prism, no proxy, no
    ///     skyline, no lit window, no house pad, no clearance anyone keeps;
    ///   * THE PAD: <see cref="CityHouses"/> grades the lot level as it grades a
    ///     house's (its own kind, <see cref="CityHouses.StationKind"/>: the
    ///     street's grade at its front, the road's DOT limits kept), so the
    ///     canopy and the shop stand on a level forecourt;
    ///   * THE GROUND CLAIMS: CityBuildings claims the lots' 18 m cells before
    ///     its restaurants and frontage boxes, the procedural houses keep off
    ///     them (<see cref="Near"/>), and the roadside mask (RoadsideOccupancy)
    ///     keeps every tree, pole, sign and lamp off them as off any lot.
    /// The MODEL is stood by CityWorld, a station a frame after its tile
    /// (<see cref="Stand"/>), from the part prefabs the editor bakes out of
    /// the owner's Gas_station pack (Editor/PSXRacingBuilder.Stations.cs):
    /// the canopy turned a quarter (its pumps along the road, the owner's DOT
    /// rule), the shop, the restrooms, the dumpster and the price pylon, each
    /// where <see cref="Parts"/> - the same table as stations.mjs's layout() -
    /// says. PSX_CITY_FUEL=0 in a tool's environment: no stations at all.
    /// </summary>
    public static class CityStations
    {
        public static bool On = Environment.GetEnvironmentVariable("PSX_CITY_FUEL") != "0";

        public const string ResName = "charlotte_fuel";
        /// <summary>"PFUL", read as a little-endian u32.</summary>
        public const uint Magic = 0x4C554650u;
        public const int Version = 1;

        public const byte ClsForecourt = 0, ClsTruck = 1, ClsBigBox = 2;
        public const byte LayShopBehind = 0, LayShopBeside = 1, LayCanopyAlone = 2, LayTruck = 3;
        public const byte FMirror = 1, FCorner = 2, FNotPlaced = 4, FSteep = 8, FNodeOnly = 16, FMisfit = 32;

        // ---- the model's parts, measured (stations.mjs's numbers: the pack's
        // Gas_station.fbx x0.7823, its pumps to 2.2 m). The baker measures the
        // same parts in Unity and fails its PART CHECK when one is off by more
        // than PartTolM, so the table and the prefabs cannot drift apart.
        /// <summary>One canopy module: along the road, deep, and where its
        /// front edge stands behind the lot's front line.</summary>
        public const float CanW = 13.06f, CanD = 16.98f, CanV0 = 3.43f;
        public const float ShopW = 11.19f, ShopD = 12.67f, WcW = 3.68f, WcD = 6.10f;
        /// <summary>The pylon: 2.35 m wide, square to the road (its faces look
        /// up and down the street, as the pack stands it); its front edge where
        /// stations.mjs checked the road's clear zone against it.</summary>
        public const float PylonDeepM = 2.35f;
        public const float PartTolM = 0.15f;

        /// <summary>A driveway's paving starts this far past the pavement's
        /// edge (lots.mjs' road band: the verge's toe; the walk and the curb cut
        /// are the road's) and runs this far into the lot.</summary>
        public const float DriveFromEdgeM = 1.0f, PaveOverlapM = 0.5f;

        public enum Part : byte { Canopy, Shop, Wc, Dumpster, Truck, Pylon }

        /// <summary>A part's rectangle in the station's frame: u along the
        /// road (centred on the lot), v back from the lot's front line.</summary>
        public struct PartRect { public Part part; public float u0, u1, v0, v1; public float U => 0.5f * (u0 + u1); public float V => 0.5f * (v0 + v1); }

        public struct Drive
        {
            /// <summary>The mouth on the road's pavement edge, the unit way in.</summary>
            public Vector2 mouth, dir;
            /// <summary>Half the curb cut's width; how far it runs to the lot.</summary>
            public float half, len;
            /// <summary>The road (a hint, valid while the graph hash matches):
            /// edge, arc position, side (+1 left of a->b).</summary>
            public int edge; public float s; public sbyte side;
        }

        public struct Link { public Vector2 a, b; public float half; }

        /// <summary>An oriented rectangle: centre, its first axis (unit), half
        /// sizes along it and across.</summary>
        public struct Obb { public Vector2 c, a; public float ha, hb; }

        public sealed class Station
        {
            public int index, fileIndex;
            /// <summary>The lot's front-centre (on its front line: the back of
            /// the walk), world plan metres.</summary>
            public Vector2 origin;
            /// <summary>Unity yaw of +v (away from the road), degrees.</summary>
            public float yawDeg;
            /// <summary>Along the road (right of +v) and back from it.</summary>
            public Vector2 u, v;
            public byte cls, layout, modules, flags;
            public float lotW, lotD;
            public int frontEdge; public float frontS; public sbyte frontSide;
            public Drive[] drives = Array.Empty<Drive>();
            public Link[] links = Array.Empty<Link>();
            /// <summary>The boxes it stands in place of, by area centroid.</summary>
            public Vector2[] hides = Array.Empty<Vector2>();
            public byte osmType; public ulong osmId;

            // ---- installed
            /// <summary>The lot's middle, and its ring (anticlockwise).</summary>
            public Vector2 centre;
            public Vector2[] lotRing;
            /// <summary>The lot and its paving (driveways, aisles) as oriented
            /// rectangles: what <see cref="Near"/> measures to.</summary>
            public Obb[] claims = Array.Empty<Obb>();
            /// <summary>Plan box of everything it paves: x0, z0, x1, z1.</summary>
            public Vector4 box;
            /// <summary>Its lot's index in <see cref="CityMap.lots"/>, and its
            /// paving's (the lots after it).</summary>
            public int lotIndex = -1, firstPaveLot = -1, paveLots;
            /// <summary>The footprints it replaced (hidden).</summary>
            public int[] replaced = Array.Empty<int>();
            /// <summary>Curb cuts added, and how many were re-found from their
            /// world position (the file's hint did not fit the graph).</summary>
            public int cuts, cutsRefound;

            public bool Placed => (flags & FNotPlaced) == 0;
            public bool Mirror => (flags & FMirror) != 0;
            public Vector2 World(float uu, float vv) => origin + u * uu + v * vv;
            public string OsmName => (osmType == 0 ? "node " : osmType == 1 ? "way " : "relation ") + osmId;
        }

        /// <summary>What the last install did, one line (the FPS overlay's
        /// city line and the audits read it).</summary>
        public static string LastReport { get; private set; } = "not installed";

        // ==================================================================
        //  THE FILE
        // ==================================================================

        /// <summary>Every station in a PFUL v1 file, placed or not, in the
        /// file's order; the graph hash and the footprints' CRC it was made for.</summary>
        public static Station[] Read(byte[] bytes, out uint graphHash, out uint bldCrc)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
            {
                if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a PFUL file");
                int ver = r.ReadInt32();
                if (ver != Version) throw new InvalidDataException("PFUL version " + ver + " (this reader knows " + Version + ")");
                graphHash = r.ReadUInt32();
                bldCrc = r.ReadUInt32();
                int n = r.ReadUInt16();
                var all = new Station[n];
                for (int i = 0; i < n; i++)
                {
                    var st = new Station { fileIndex = i };
                    st.origin = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                    st.yawDeg = r.ReadSingle();
                    st.cls = r.ReadByte(); st.layout = r.ReadByte(); st.modules = r.ReadByte(); st.flags = r.ReadByte();
                    st.lotW = r.ReadSingle() * CityMap.LayoutScale; st.lotD = r.ReadSingle() * CityMap.LayoutScale;
                    st.frontEdge = (int)r.ReadUInt32(); st.frontS = r.ReadSingle() * CityMap.LayoutScale; st.frontSide = r.ReadSByte();
                    int nd = r.ReadByte();
                    st.drives = new Drive[nd];
                    for (int k = 0; k < nd; k++)
                    {
                        var d = new Drive();
                        d.mouth = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                        d.dir = new Vector2(r.ReadSingle(), r.ReadSingle());
                        d.dir = d.dir.sqrMagnitude > 1e-8f ? d.dir.normalized : Vector2.up;
                        d.half = r.ReadSingle(); d.len = r.ReadSingle() * CityMap.LayoutScale;
                        d.edge = (int)r.ReadUInt32(); d.s = r.ReadSingle() * CityMap.LayoutScale; d.side = r.ReadSByte();
                        st.drives[k] = d;
                    }
                    int nl = r.ReadByte();
                    st.links = new Link[nl];
                    for (int k = 0; k < nl; k++)
                        st.links[k] = new Link
                        {
                            a = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale,
                            b = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale,
                            half = r.ReadSingle(),
                        };
                    int nh = r.ReadByte();
                    st.hides = new Vector2[nh];
                    for (int k = 0; k < nh; k++) st.hides[k] = new Vector2(r.ReadSingle(), r.ReadSingle()) * CityMap.LayoutScale;
                    st.osmType = r.ReadByte(); st.osmId = r.ReadUInt64();
                    // the frame: +v = (sin yaw, cos yaw), u its right (stations.mjs: u = (vz, -vx))
                    float yr = st.yawDeg * Mathf.Deg2Rad;
                    st.v = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
                    st.u = new Vector2(st.v.y, -st.v.x);
                    all[i] = st;
                }
                if (r.BaseStream.Position != r.BaseStream.Length)
                    throw new InvalidDataException((r.BaseStream.Length - r.BaseStream.Position) + " bytes left over after " + n + " stations");
                return all;
            }
        }

        // ==================================================================
        //  THE INSTALL
        // ==================================================================

        /// <summary>Read Resources/charlotte_fuel.bytes and join its stations
        /// to the map (see the class summary). Called by
        /// <see cref="CityMap.Parse"/>; idempotent per map.</summary>
        public static void Install(CityMap map)
        {
            map.stations = Array.Empty<Station>();
            map.stationLotsFrom = -1;
            map.stationsStale = false;
            indexFor = null;
            if (!On) { LastReport = "off (PSX_CITY_FUEL=0)"; return; }
            // its own small file in Resources (34 KB), never in the city's
            // bundle: Resources is where it always is
            var ta = Resources.Load<TextAsset>(ResName);
            if (ta == null)
            {
                LastReport = "no charlotte_fuel.bytes";
                Debug.LogWarning("[City] charlotte_fuel.bytes missing - no filling stations (node tools/city/stations.mjs --out Assets/PSXRacing/Resources)");
                return;
            }
            byte[] bytes = ta.bytes;
            Resources.UnloadAsset(ta);
            Install(map, bytes);
        }

        /// <summary><see cref="Install(CityMap)"/> from bytes a tool hands in.</summary>
        public static void Install(CityMap map, byte[] bytes)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Station[] all;
            uint gh, crc;
            try { all = Read(bytes, out gh, out crc); }
            catch (Exception ex)
            {
                LastReport = "unreadable: " + ex.Message;
                Debug.LogError("[City] charlotte_fuel.bytes: " + ex.Message + " - no filling stations");
                return;
            }
            bool hints = gh == map.graphHash;
            if (!hints)
            {
                map.stationsStale = true;
                Debug.LogError($"[City] charlotte_fuel.bytes was made for graph hash {gh:x8}; charlotte_city.bytes is {map.graphHash:x8}. " +
                               "The stations stand by their world positions and their curb cuts are re-found, but a box they replaced may " +
                               "be gone or moved - remake it: node tools/city/regen_city.mjs --no-export");
            }

            var lots = new List<CityMap.Lot>(map.lots ?? Array.Empty<CityMap.Lot>());
            var ents = new List<CityMap.LotEntrance>(map.lotEntrances ?? Array.Empty<CityMap.LotEntrance>());
            int osmLots = lots.Count;
            var placed = new List<Station>(all.Length);
            int notPlaced = 0, cuts = 0, refound = 0, cutsLost = 0, paves = 0, hidden = 0, hideMissed = 0, tableOff = 0;
            var rects = new List<PartRect>(8);
            foreach (var st in all)
            {
                if (!st.Placed) { notPlaced++; continue; }
                // the C# layout table against the file: the same lot, or the
                // table drifted from stations.mjs's layout()
                Parts(st.layout, st.modules, st.Mirror, rects, out var lotRect);
                if (Mathf.Abs((lotRect.y - lotRect.x) - st.lotW) > 0.05f || Mathf.Abs(lotRect.w - st.lotD) > 0.05f) tableOff++;

                st.lotRing = Corners(st.origin, st.u, st.v, -0.5f * st.lotW, 0.5f * st.lotW, 0f, st.lotD);
                st.centre = st.World(0f, 0.5f * st.lotD);
                var claims = new List<Obb>(4) { new Obb { c = st.centre, a = st.u, ha = 0.5f * st.lotW, hb = 0.5f * st.lotD } };
                st.lotIndex = lots.Count;
                lots.Add(NewLot(st.lotRing, st.u));
                st.firstPaveLot = lots.Count;
                foreach (var d in st.drives)
                {
                    if (Entrance(map, d, hints, out var en, out bool re))
                    {
                        ents.Add(en); st.cuts++; cuts++;
                        if (re) { st.cutsRefound++; refound++; }
                    }
                    else cutsLost++;
                    // the paving from the verge's toe to the lot (a lot that
                    // stands back from its street; nothing to pave otherwise)
                    float a0 = DriveFromEdgeM, a1 = d.len + PaveOverlapM;
                    if (a1 - a0 > 0.5f)
                    {
                        var left = new Vector2(-d.dir.y, d.dir.x);
                        lots.Add(NewLot(Corners(d.mouth, d.dir, left, a0, a1, -d.half, d.half), d.dir));
                        claims.Add(new Obb { c = d.mouth + d.dir * (0.5f * (a0 + a1)), a = d.dir, ha = 0.5f * (a1 - a0), hb = d.half });
                        paves++;
                    }
                }
                foreach (var k in st.links)
                {
                    var dir = k.b - k.a;
                    float len = dir.magnitude;
                    if (len < 0.5f) continue;
                    dir /= len;
                    var left = new Vector2(-dir.y, dir.x);
                    lots.Add(NewLot(Corners(k.a, dir, left, -PaveOverlapM, len + PaveOverlapM, -k.half, k.half), dir));
                    claims.Add(new Obb { c = 0.5f * (k.a + k.b), a = dir, ha = 0.5f * len + PaveOverlapM, hb = k.half });
                    paves++;
                }
                st.paveLots = lots.Count - st.firstPaveLot;
                st.claims = claims.ToArray();
                var bx = BoxOf(st.lotRing);
                for (int li = st.firstPaveLot; li < lots.Count; li++)
                {
                    var b = lots[li].box;
                    bx = new Vector4(Mathf.Min(bx.x, b.x), Mathf.Min(bx.y, b.y), Mathf.Max(bx.z, b.z), Mathf.Max(bx.w, b.w));
                }
                st.box = bx;
                st.replaced = Replace(map, st, ref hideMissed);
                hidden += st.replaced.Length;
                st.index = placed.Count;
                placed.Add(st);
            }

            map.lots = lots.ToArray();
            map.lotEntrances = ents.ToArray();
            map.stationLotsFrom = osmLots;
            map.stations = placed.ToArray();
            BuildIndex(map);

            // the buildings that stay on a station's lot: holes in it (the
            // ground shows), as an OSM lot has round the buildings inside it
            int bldHoles = 0;
            foreach (var st in map.stations)
            {
                var hs = BuildingHoles(map, st);
                if (hs.Count == 0) continue;
                var lot = map.lots[st.lotIndex];
                lot.holes = hs.ToArray();
                lot.holeKind = new byte[hs.Count];
                bldHoles += hs.Count;
            }

            // the OSM lots a station's lot overlaps: a hole the size of the
            // station's lot, FIRST (a lot piece is the first lot's that takes
            // it, and LotClass stops at the first hole it is in), and any
            // island on the forecourt dropped (its curb would stand there)
            int osmHoled = 0, islands = 0;
            var add = new List<Vector2[]>(2);
            for (int li = 0; li < osmLots; li++)
            {
                var L = map.lots[li];
                if (L.ring == null || L.ring.Length < 3) continue;
                add.Clear();
                ForStationsInBox(map, L.box, si =>
                {
                    var st = map.stations[si];
                    if (!Meets(L.box, BoxOf(st.lotRing))) return;
                    if (Overlaps(L.ring, st.lotRing) && !add.Contains(st.lotRing)) add.Add(st.lotRing);
                });
                if (add.Count == 0) continue;
                var hs = new List<Vector2[]>(add);
                var hk = new List<byte>(add.Count + L.holeKind.Length);
                for (int k = 0; k < add.Count; k++) hk.Add(0);
                for (int h = 0; h < L.holes.Length; h++)
                {
                    if (L.holeKind[h] == 1)
                    {
                        bool onForecourt = false;
                        foreach (var r in add) if (Overlaps(L.holes[h], r)) { onForecourt = true; break; }
                        if (onForecourt) { islands++; continue; }
                    }
                    hs.Add(L.holes[h]);
                    hk.Add(L.holeKind[h]);
                }
                L.holes = hs.ToArray();
                L.holeKind = hk.ToArray();
                osmHoled++;
            }

            LastReport = $"{map.stations.Length} stations ({notPlaced} not placed), {paves} paved driveways/aisles, " +
                         $"{cuts} curb cuts ({refound} re-found{(cutsLost > 0 ? $", {cutsLost} with no road" : "")}), " +
                         $"{hidden} boxes replaced ({hideMissed} not found), {osmHoled} OSM lots holed ({islands} islands dropped), " +
                         $"{bldHoles} buildings kept on forecourts, {clock.ElapsedMilliseconds} ms";
            Debug.Log("[City] filling stations: " + LastReport);
            if (tableOff > 0)
                Debug.LogError($"[City] filling stations: {tableOff} lots disagree with CityStations.Parts - the C# layout table has drifted from tools/city/stations.mjs layout()");
            if (hideMissed > 0 && hints)
                Debug.LogWarning($"[City] filling stations: {hideMissed} replaced boxes not found within 0.5 m of their centroid although the graph matches - charlotte_bld.bytes changed? (node tools/city/regen_city.mjs --no-export)");
        }

        static CityMap.Lot NewLot(Vector2[] ring, Vector2 u) => new CityMap.Lot
        {
            ring = ring,
            box = BoxOf(ring),
            u = u,
            runFoot = Array.Empty<Vector2>(),
            runN = Array.Empty<byte>(),
            runU = Array.Empty<Vector2>(),
        };

        /// <summary>The curb cut for a driveway: the file's (edge, s, side)
        /// while the graph matches and the mouth is where it says, else the
        /// nearest surface road to the mouth's world position.</summary>
        static bool Entrance(CityMap map, Drive d, bool hints, out CityMap.LotEntrance en, out bool refound)
        {
            refound = false;
            if (hints && d.edge >= 0 && d.edge < map.edges.Length && MouthOn(map.edges[d.edge], d.s, d.side, d.mouth))
            {
                en = new CityMap.LotEntrance { edge = d.edge, s = d.s, side = d.side, half = d.half };
                return true;
            }
            refound = true;
            if (Project(map, d.mouth, 12f, out int e, out float s, out sbyte side))
            {
                en = new CityMap.LotEntrance { edge = e, s = s, side = side, half = d.half };
                return true;
            }
            en = default;
            return false;
        }

        static bool MouthOn(CityMap.Edge e, float s, sbyte side, Vector2 mouth)
        {
            if (s < 0f || s > e.length) return false;
            var p = e.PointAt(s);
            var t = e.TangentAt(s);
            var left = new Vector2(-t.y, t.x);
            var q = mouth - p;
            return Vector2.Dot(q, left) * side > 0f && Mathf.Abs(Vector2.Dot(q, t)) < 2f && q.magnitude < e.HalfMax + 6f;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();

        /// <summary>The nearest surface road's point to <paramref name="p"/>
        /// (within its pavement + <paramref name="reach"/>): edge, arc position,
        /// side (+1 left of a->b). Ramps, tunnels, bridges and freeways are no
        /// driveway's street (stations.mjs's driveTo).</summary>
        static bool Project(CityMap map, Vector2 p, float reach, out int best, out float bestS, out sbyte bestSide)
        {
            best = -1; bestS = 0f; bestSide = 1;
            float bd = float.MaxValue;
            segScratch.Clear();
            float r = reach + 30f;
            map.EdgeSegsInRect(p - Vector2.one * r, p + Vector2.one * r, segScratch);
            foreach (int packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (e.link || e.tunnel || e.bridge || e.cls >= 5 || si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], ab = e.pts[si + 1] - a;
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                float d = Vector2.Distance(p, a + ab * t) - e.HalfMax;
                if (d > reach) continue;
                if (d < bd || (Mathf.Approximately(d, bd) && ei < best))
                {
                    bd = d; best = ei;
                    bestS = e.s[si] + Mathf.Sqrt(l2) * t;
                    bestSide = (sbyte)(ab.x * (p.y - a.y) - ab.y * (p.x - a.x) > 0f ? 1 : -1);
                }
            }
            return best >= 0;
        }

        /// <summary>Hide the footprints the station stands in place of: each
        /// listed by its polygon's area centroid, matched within 0.5 m; and a
        /// matched outline's building:parts with it.</summary>
        static int[] Replace(CityMap map, Station st, ref int missed)
        {
            if (st.hides.Length == 0 || map.footprints.Length == 0) return Array.Empty<int>();
            var got = new List<int>(st.hides.Length);
            foreach (var h in st.hides)
            {
                int tx = Mathf.FloorToInt(h.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(h.y / CityMeshes.TileSize);
                int best = -1; double bd = 0.5;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var list = map.FootprintsInTile(tx + dx, tz + dz);
                        if (list == null) continue;
                        foreach (int fi in list)
                        {
                            var f = map.footprints[fi];
                            if (f.part || f.pts == null || f.pts.Length < 3) continue;
                            if ((f.centre - h).sqrMagnitude > 120f * 120f) continue;
                            var c = AreaCentroid(f.pts);
                            double d = Math.Sqrt((c.x - h.x) * (c.x - h.x) + (c.y - h.y) * (c.y - h.y));
                            if (d < bd) { bd = d; best = fi; }
                        }
                    }
                if (best < 0) { missed++; continue; }
                if (got.Contains(best)) continue;
                got.Add(best);
                var bf = map.footprints[best];
                bf.hidden = true; bf.replaced = true;
                // its parts (an outline drawn by its building:parts)
                int px = Mathf.FloorToInt(bf.centre.x / CityMeshes.TileSize), pz = Mathf.FloorToInt(bf.centre.y / CityMeshes.TileSize);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var list = map.FootprintsInTile(px + dx, pz + dz);
                        if (list == null) continue;
                        foreach (int fi in list)
                        {
                            var f = map.footprints[fi];
                            if (f.part && f.outline == best && !f.replaced) { f.hidden = true; f.replaced = true; got.Add(fi); }
                        }
                    }
            }
            return got.ToArray();
        }

        /// <summary>The area centroid of a ring (the shoelace, in double: the
        /// tool computed it from the same float32 points).</summary>
        public static Vector2 AreaCentroid(Vector2[] r)
        {
            double a = 0, cx = 0, cz = 0;
            int n = r.Length;
            for (int i = 0; i < n; i++)
            {
                double x0 = r[i].x, z0 = r[i].y, x1 = r[(i + 1) % n].x, z1 = r[(i + 1) % n].y;
                double k = x0 * z1 - x1 * z0;
                a += k; cx += (x0 + x1) * k; cz += (z0 + z1) * k;
            }
            if (Math.Abs(a) < 1e-9)
            {
                double sx = 0, sz = 0;
                foreach (var q in r) { sx += q.x; sz += q.y; }
                return new Vector2((float)(sx / n), (float)(sz / n));
            }
            return new Vector2((float)(cx / (3.0 * a)), (float)(cz / (3.0 * a)));
        }

        /// <summary>The buildings left standing on a station's lot (stations.mjs
        /// lets one lie on other buildings for at most 12 % of it): each a hole.</summary>
        static List<Vector2[]> BuildingHoles(CityMap map, Station st)
        {
            var holes = new List<Vector2[]>();
            if (map.footprints.Length == 0) return holes;
            var lb = BoxOf(st.lotRing);
            int tx0 = Mathf.FloorToInt((lb.x - 80f) / CityMeshes.TileSize), tx1 = Mathf.FloorToInt((lb.z + 80f) / CityMeshes.TileSize);
            int tz0 = Mathf.FloorToInt((lb.y - 80f) / CityMeshes.TileSize), tz1 = Mathf.FloorToInt((lb.w + 80f) / CityMeshes.TileSize);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var list = map.FootprintsInTile(tx, tz);
                    if (list == null) continue;
                    foreach (int fi in list)
                    {
                        var f = map.footprints[fi];
                        if (f.replaced || f.part || f.pts == null || f.pts.Length < 3) continue;
                        var ring = f.gable ? ObbRing(f) : f.pts;
                        if (!Meets(BoxOf(ring), lb) || !Overlaps(ring, st.lotRing)) continue;
                        holes.Add(ring);
                    }
                }
            return holes;
        }

        /// <summary>A gabled house is drawn as its oriented box.</summary>
        static Vector2[] ObbRing(CityMap.Footprint f)
        {
            var v = new Vector2(-f.u.y, f.u.x);
            return new[] { f.centre - f.u * f.hu - v * f.hv, f.centre + f.u * f.hu - v * f.hv, f.centre + f.u * f.hu + v * f.hv, f.centre - f.u * f.hu + v * f.hv };
        }

        // ==================================================================
        //  THE INDEX AND THE QUESTIONS
        // ==================================================================

        static CityMap indexFor;
        static Dictionary<long, List<int>> byCentre, byTouch;
        /// <summary>How far round its paving a station answers <see cref="Near"/>
        /// by tile (the largest radius anyone asks with, plus a margin).</summary>
        const float TouchPadM = 60f;
        static readonly List<int> none = new List<int>(0);

        static long TileKey(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;

        static void BuildIndex(CityMap map)
        {
            byCentre = new Dictionary<long, List<int>>();
            byTouch = new Dictionary<long, List<int>>();
            float T = CityMeshes.TileSize;
            for (int i = 0; i < map.stations.Length; i++)
            {
                var st = map.stations[i];
                long ck = TileKey(Mathf.FloorToInt(st.centre.x / T), Mathf.FloorToInt(st.centre.y / T));
                if (!byCentre.TryGetValue(ck, out var cl)) byCentre[ck] = cl = new List<int>(2);
                cl.Add(i);
                var b = st.box;
                for (int tx = Mathf.FloorToInt((b.x - TouchPadM) / T); tx <= Mathf.FloorToInt((b.z + TouchPadM) / T); tx++)
                    for (int tz = Mathf.FloorToInt((b.y - TouchPadM) / T); tz <= Mathf.FloorToInt((b.w + TouchPadM) / T); tz++)
                    {
                        long k = TileKey(tx, tz);
                        if (!byTouch.TryGetValue(k, out var l)) byTouch[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
            indexFor = map;
        }

        static bool Indexed(CityMap map)
        {
            if (map == null || map.stations.Length == 0) return false;
            if (indexFor != map) BuildIndex(map);
            return true;
        }

        static void ForStationsInBox(CityMap map, Vector4 b, Action<int> visit)
        {
            if (!Indexed(map)) return;
            float T = CityMeshes.TileSize;
            var seen = seenScratch; seen.Clear();
            for (int tx = Mathf.FloorToInt(b.x / T); tx <= Mathf.FloorToInt(b.z / T); tx++)
                for (int tz = Mathf.FloorToInt(b.y / T); tz <= Mathf.FloorToInt(b.w / T); tz++)
                    if (byTouch.TryGetValue(TileKey(tx, tz), out var l))
                        foreach (int i in l) if (seen.Add(i)) visit(i);
        }
        static readonly HashSet<int> seenScratch = new HashSet<int>();

        /// <summary>The stations whose lot's MIDDLE is in the 256 m tile: the
        /// tile that stands the model and lists the pad (empty list: none).</summary>
        public static List<int> InTile(CityMap map, int tx, int tz)
        {
            if (!Indexed(map)) return none;
            return byCentre.TryGetValue(TileKey(tx, tz), out var l) ? l : none;
        }

        /// <summary>Is a station's lot, driveway or aisle within
        /// <paramref name="r"/> metres of the point (inside it: 0)? The
        /// procedural buildings, the fill houses, the restaurants and the
        /// business pole signs keep off a forecourt with this.</summary>
        public static bool Near(CityMap map, Vector2 p, float r)
        {
            if (!Indexed(map)) return false;
            float T = CityMeshes.TileSize;
            if (!byTouch.TryGetValue(TileKey(Mathf.FloorToInt(p.x / T), Mathf.FloorToInt(p.y / T)), out var l)) return false;
            foreach (int i in l)
            {
                var st = map.stations[i];
                if (p.x < st.box.x - r || p.x > st.box.z + r || p.y < st.box.y - r || p.y > st.box.w + r) continue;
                foreach (var o in st.claims) if (ObbDist(o, p) <= r) return true;
            }
            return false;
        }

        /// <summary>The station whose lot holds the point (within
        /// <paramref name="pad"/>), or -1.</summary>
        public static int LotAt(CityMap map, Vector2 p, float pad = 0f)
        {
            if (!Indexed(map)) return -1;
            float T = CityMeshes.TileSize;
            if (!byTouch.TryGetValue(TileKey(Mathf.FloorToInt(p.x / T), Mathf.FloorToInt(p.y / T)), out var l)) return -1;
            foreach (int i in l) if (ObbDist(map.stations[i].claims[0], p) <= pad) return i;
            return -1;
        }

        public static float ObbDist(Obb o, Vector2 p)
        {
            var d = p - o.c;
            var b = new Vector2(-o.a.y, o.a.x);
            float da = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, o.a)) - o.ha);
            float db = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b)) - o.hb);
            return Mathf.Sqrt(da * da + db * db);
        }

        // ==================================================================
        //  THE LAYOUT (stations.mjs's layout(), number for number)
        // ==================================================================

        /// <summary>
        /// A layout's parts as rectangles in the station's frame (u centred on
        /// the lot, v back from its front line), and the lot's rectangle (x = u0,
        /// y = u1, z = v0, w = v1). stations.mjs's layout(kind, n, mirror) number
        /// for number - Install checks every lot against the file's.
        /// </summary>
        public static void Parts(int kind, int n, bool mirror, List<PartRect> into, out Vector4 lot)
        {
            into.Clear();
            float cw = CanW * Mathf.Max(1, n);
            void Add(Part p, float u0, float u1, float v0, float v1) => into.Add(new PartRect { part = p, u0 = u0, u1 = u1, v0 = v0, v1 = v1 });
            Add(Part.Canopy, -0.5f * cw, 0.5f * cw, CanV0, CanV0 + CanD);
            float l0, l1, l3;
            if (kind == LayShopBehind)
            {
                const float su = -0.94f;   // the pack's shop centre, off the canopy's
                Add(Part.Shop, su - 0.5f * ShopW, su + 0.5f * ShopW, 25.58f, 25.58f + ShopD);
                Add(Part.Wc, 0.5f * cw + 6.92f - 0.5f * WcW, 0.5f * cw + 6.92f + 0.5f * WcW, 25.82f, 25.82f + WcD);
                Add(Part.Dumpster, 0.5f * cw + 7.75f - 0.8f, 0.5f * cw + 7.75f + 0.8f, 31.9f, 32.9f);
                l0 = Mathf.Min(-0.5f * cw, su - 0.5f * ShopW) - 5.3f;
                l1 = Mathf.Max(0.5f * cw + 7.5f, 0.5f * cw + 6.92f + 0.5f * WcW + 0.9f);
                l3 = 41.4f;
            }
            else if (kind == LayShopBeside || kind == LayTruck)
            {
                float s0 = 0.5f * cw + 7.12f;
                Add(Part.Shop, s0, s0 + ShopD, 5.32f, 5.32f + ShopW);
                Add(Part.Wc, s0 + 0.23f, s0 + 0.23f + WcD, 23.47f, 23.47f + WcW);
                Add(Part.Dumpster, s0 + 6.8f - 0.8f, s0 + 6.8f + 0.8f, 25.6f, 26.6f);
                l0 = -0.5f * cw - 5.4f;
                l1 = s0 + ShopD + 3.2f;
                l3 = 28.0f;
                if (kind == LayTruck)
                {
                    float tw = CanW * 2f, tu = 0.5f * (l0 + l1);
                    Add(Part.Truck, tu - 0.5f * tw, tu + 0.5f * tw, 34f, 34f + CanD);
                    l0 = Mathf.Min(l0, tu - 0.5f * tw - 7f);
                    l1 = Mathf.Max(l1, tu + 0.5f * tw + 7f);
                    l3 = 34f + CanD + 8f;
                }
            }
            else
            {
                l0 = -0.5f * cw - 6f;
                l1 = 0.5f * cw + 6f;
                l3 = CanV0 + CanD + 4f;
            }
            // the pylon: at the lot's front corner on the canopy's hand
            Add(Part.Pylon, l0 + 1.0f, l0 + 3.4f, 1.2f, 1.6f);
            if (mirror)
            {
                for (int i = 0; i < into.Count; i++)
                {
                    var p = into[i];
                    float a = -p.u1, b = -p.u0;
                    p.u0 = a; p.u1 = b;
                    into[i] = p;
                }
                float m0 = -l1, m1 = -l0;
                l0 = m0; l1 = m1;
            }
            // centre the lot on u = 0 (the station's x, z is the lot's front-centre)
            float mid = 0.5f * (l0 + l1);
            for (int i = 0; i < into.Count; i++)
            {
                var p = into[i];
                p.u0 -= mid; p.u1 -= mid;
                into[i] = p;
            }
            lot = new Vector4(l0 - mid, l1 - mid, 0f, l3);
        }

        /// <summary>One part prefab stood in a station: which, where (u, v at
        /// its middle, in the station's frame), turned how far from the
        /// station's own yaw.</summary>
        public struct Pose { public Part part; public string prefab; public float u, v, yawDeg; public PartRect rect; }

        public const string PrefabDir = "CityStations/";
        public const string CanopyPrefab = "StationCanopy", ShopPrefab = "StationShop", WcPrefab = "StationRestrooms",
                            DumpsterPrefab = "StationDumpster", PylonPrefab = "StationPylon";
        /// <summary>The empty markers the baker leaves under a canopy's lights:
        /// the canopy's NightGlow heads.</summary>
        public const string LightMarker = "CanopyLight";
        /// <summary>A canopy light's share of a street lamp's pool (white-green
        /// fluorescent tubes, five metres up, four a module).</summary>
        public const float CanopyLightGain = 0.75f;

        /// <summary>
        /// The prefabs a station stands, each where its layout says. The
        /// prefabs are baked in the pack's own frame (x along the road, z
        /// away from it, the shop's and the restrooms' doors on -z and -x):
        /// the shop beside the canopy turns its door toward the pumps, the
        /// restrooms beside it toward the forecourt; the pylon stands square to
        /// the road with its front edge where stations.mjs checked it.
        /// </summary>
        public static void Poses(Station st, List<Pose> into, List<PartRect> scratch)
        {
            into.Clear();
            Parts(st.layout, st.modules, st.Mirror, scratch, out _);
            bool beside = st.layout == LayShopBeside || st.layout == LayTruck;
            foreach (var r in scratch)
            {
                var p = new Pose { part = r.part, rect = r, u = r.U, v = r.V, yawDeg = 0f };
                switch (r.part)
                {
                    case Part.Canopy: p.prefab = CanopyPrefab + Mathf.Clamp(st.modules, 1, 3); break;
                    case Part.Truck: p.prefab = CanopyPrefab + "2"; break;
                    case Part.Shop:
                        p.prefab = ShopPrefab;
                        // its door (-z) toward the pumps: the road (behind the
                        // canopy), or the canopy's side (beside it)
                        p.yawDeg = beside ? (st.Mirror ? -90f : 90f) : 0f;
                        break;
                    case Part.Wc:
                        p.prefab = WcPrefab;
                        // its door (-x) toward the shop and the canopy; beside the
                        // shop, toward the forecourt
                        p.yawDeg = beside ? -90f : (st.Mirror ? 180f : 0f);
                        break;
                    case Part.Dumpster: p.prefab = DumpsterPrefab; break;
                    case Part.Pylon:
                        p.prefab = PylonPrefab;
                        p.v = r.v0 + 0.5f * PylonDeepM;
                        break;
                }
                into.Add(p);
            }
        }

        /// <summary>The ground a part stands on: the highest of the DRAWN ground
        /// (CityMeshes.LatticeAt) at its corners, edge middles and centre and at
        /// every lattice corner inside it - as CityBuildings.SeatY seats a lot.
        /// The pad is level, so these agree but at its graded edges. Read while
        /// the tile's lattice is the builder's (CityWorld.StandUp).</summary>
        public static float SeatOf(CityMap map, Station st, Pose p)
        {
            var r = p.rect;
            float hu = 0.5f * (r.u1 - r.u0), hv = 0.5f * (r.v1 - r.v0);
            if (p.part == Part.Pylon) hv = 0.5f * PylonDeepM;
            float hi = float.MinValue;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    var c = st.World(p.u + hu * i, p.v + hv * j);
                    hi = Mathf.Max(hi, CityMeshes.LatticeAt(map, c.x, c.y));
                }
            float cell = CityMeshes.TileSize / CityMeshes.GroundRes;
            var ctr = st.World(p.u, p.v);
            float rr = Mathf.Sqrt(hu * hu + hv * hv);
            for (int z = Mathf.CeilToInt((ctr.y - rr) / cell); z <= Mathf.FloorToInt((ctr.y + rr) / cell); z++)
                for (int x = Mathf.CeilToInt((ctr.x - rr) / cell); x <= Mathf.FloorToInt((ctr.x + rr) / cell); x++)
                {
                    var q = new Vector2(x * cell, z * cell) - st.origin;
                    float uu = Vector2.Dot(q, st.u) - p.u, vv = Vector2.Dot(q, st.v) - p.v;
                    if (Mathf.Abs(uu) > hu || Mathf.Abs(vv) > hv) continue;
                    hi = Mathf.Max(hi, CityMeshes.LatticeAt(map, x * cell, z * cell));
                }
            return hi;
        }

        // ==================================================================
        //  THE STAND-UP
        // ==================================================================

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly HashSet<string> warned = new HashSet<string>();

        public static GameObject Prefab(string name)
        {
            if (prefabs.TryGetValue(name, out var go)) return go;
            go = PSXTexDecode.LoadPrefab(PrefabDir + name);
            if (go == null && warned.Add(name))
                Debug.LogWarning("[City] filling station part missing: Resources/" + PrefabDir + name +
                                 " - run the scene build to bake them (PSXRacingBuilder.BakeCityStations).");
            prefabs[name] = go;
            return go;
        }

        /// <summary>
        /// Stand one station's model under <paramref name="parent"/> (its tile's
        /// root, so streaming drops it with the tile): every part where
        /// <see cref="Poses"/> puts it, on the seat its tile's build measured
        /// (<paramref name="seats"/>, one per pose), and the canopy's lights
        /// lit as white-green fluorescent tubes (a NightGlow of the station's
        /// own: its heads light the forecourt in the lamp field and wear
        /// halos). Null when no part prefab is baked.
        /// </summary>
        public static GameObject Stand(Station st, List<Pose> poses, float[] seats, Transform parent)
        {
            var root = new GameObject("FuelStation_" + st.index);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(st.origin.x, 0f, st.origin.y), Quaternion.Euler(0f, st.yawDeg, 0f));
            int stood = 0;
            for (int i = 0; i < poses.Count; i++)
            {
                var p = poses[i];
                var prefab = Prefab(p.prefab);
                if (prefab == null) continue;
                var go = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                go.name = p.prefab;
                go.transform.localPosition = new Vector3(p.u, seats != null && i < seats.Length ? seats[i] : 0f, p.v);
                go.transform.localRotation = Quaternion.Euler(0f, p.yawDeg, 0f);
                stood++;
            }
            if (stood == 0)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
            // THE CANOPY'S LIGHTS (the 90s rule: fluorescent white-green, no LEDs)
            var heads = new List<Vector3>(12);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == LightMarker) heads.Add(t.position);
            if (heads.Count > 0)
            {
                var lights = new GameObject("CanopyLights");
                lights.transform.SetParent(root.transform, false);
                var glow = lights.AddComponent<NightGlow>();
                glow.cityField = true;
                var gains = new float[heads.Count];
                var mercs = new bool[heads.Count];
                for (int i = 0; i < heads.Count; i++) { gains[i] = CanopyLightGain; mercs[i] = true; }
                glow.Init(heads, gains, mercs);
            }
            return root;
        }

        // ==================================================================
        //  plan geometry
        // ==================================================================

        /// <summary>The rectangle [a0, a1] x [b0, b1] in the frame (o, a, b),
        /// anticlockwise when b is a's left (cross(a, b) &gt; 0).</summary>
        public static Vector2[] Corners(Vector2 o, Vector2 a, Vector2 b, float a0, float a1, float b0, float b1) => new[]
        {
            o + a * a0 + b * b0, o + a * a1 + b * b0, o + a * a1 + b * b1, o + a * a0 + b * b1,
        };

        public static Vector4 BoxOf(Vector2[] r)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var q in r) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
            return new Vector4(x0, z0, x1, z1);
        }

        static bool Meets(Vector4 a, Vector4 b) => a.z >= b.x && a.x <= b.z && a.w >= b.y && a.y <= b.w;

        /// <summary>Do two simple polygons share any area (a vertex of one in
        /// the other, or two edges crossing)?</summary>
        public static bool Overlaps(Vector2[] a, Vector2[] b)
        {
            if (!Meets(BoxOf(a), BoxOf(b))) return false;
            foreach (var p in a) if (CityMap.PointInPoly(b, p)) return true;
            foreach (var p in b) if (CityMap.PointInPoly(a, p)) return true;
            for (int i = 0; i < a.Length; i++)
            {
                Vector2 p0 = a[i], p1 = a[(i + 1) % a.Length];
                for (int j = 0; j < b.Length; j++)
                    if (SegsCross(p0, p1, b[j], b[(j + 1) % b.Length])) return true;
            }
            return false;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static bool SegsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }
    }
}
