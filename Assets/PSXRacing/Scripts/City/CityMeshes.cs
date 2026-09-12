using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Turns one 256 m tile of the CityMap into meshes. Pure geometry — no
    /// GameObjects; CityWorld wraps the results. Everything is deterministic
    /// and TILE-LOCAL: vertices are relative to the tile's origin corner, so
    /// float precision never depends on how far from uptown the tile sits.
    ///
    /// Ownership rule at tile seams: a road span, river span, junction or
    /// building belongs to the tile that contains its MIDPOINT (or node, or
    /// footprint centre), so nothing is ever emitted twice. Adjacent tiles
    /// derive shared boundary vertices from the same stations and the same
    /// GroundY, so edges meet exactly.
    ///
    /// WINDING, ONCE. Two helpers decide which way every face points so no
    /// call site has to: <see cref="Bucket.Up"/> orders a horizontal quad so
    /// it faces UP (its corners read counter-clockwise in map view), and
    /// <see cref="Bucket.Wall"/> orders a vertical quad so it faces the
    /// OUTWARD normal it is given. The inside-out buildings and the roads
    /// visible only from underneath were both per-call-site winding errors;
    /// the road ribbon and the deck code below predate the helpers and keep
    /// their verified order.
    /// </summary>
    public static class CityMeshes
    {
        public const float TileSize = 256f;
        public const int GroundRes = 32;          // 8 m cells
        /// <summary>Metres of road per texture repeat — and therefore the
        /// length of ONE DASH CYCLE, because the painter draws the broken lane
        /// line as the first quarter of the repeat. 12.192 m is 40 feet, the
        /// US standard: a 10 ft stripe and a 30 ft gap.</summary>
        public const float RoadVTile = 12.192f;
        public const float RailH = 0.95f;
        public const float RailW = 0.3f;
        public const float PierEvery = 26f;
        public const float BuildingSink = 0.55f;
        /// <summary>A Jersey barrier: 81 cm tall, half a metre thick, on the
        /// paved edge of every freeway carriageway that is on the ground
        /// (decks carry rails). It is what keeps a race on the freeway and
        /// what a freeway looks like.</summary>
        public const float BarrierH = 0.81f;
        public const float BarrierW = 0.5f;
        /// <summary>How far the tarmac hangs below its own surface at the
        /// edge. A KERB, not a cliff — the roads mesh carries the collider,
        /// so this face is what the car climbs back onto the road over.</summary>
        public const float KerbDepth = 0.32f;

        /// <summary>Longest edge of one facade panel, in metres. Affine warp is
        /// proportional to how much a triangle spans, so a 40 m wall as one
        /// quad swims; PS1 content subdivided, and so does this.</summary>
        const float FacadePanelMax = 9f;
        const int FacadePanelCap = 4;
        /// <summary>Up a TOWER the cap is doubled: four panels over a 200 m
        /// wall are 50 m each, and the curtain wall visibly bowed in every
        /// preview. Only walls past this height pay for it.</summary>
        const float TallWallM = 40f;
        const int TallPanelCap = 8;

        /// <summary>What a stretch of road is MADE of, which is a separate
        /// question from what is painted on it: the same carriageway is fresh
        /// blacktop on the ground and poured concrete where it crosses a
        /// river. Ported from RG2's (material x age) pair.</summary>
        public enum Surface { AsphaltNew = 0, AsphaltOld, ConcreteNew, ConcreteOld }
        public const int SurfaceCount = 4;

        /// <summary>Driving surfaces are one row per RoadProfiles entry plus
        /// the unpainted junction slab, times four surfaces.</summary>
        public const int JunctionProfile = RoadProfiles.ProfileCount;
        public const int RoadClassCount = RoadProfiles.ProfileCount + 1;

        public enum Slot
        {
            Ground = 0, Concrete, Water, Pavement,
            FacadeTower, FacadeMid, FacadeBrick, Shops,
            FacadeGlass, FacadeHouse, RoofTiles, RoofFlat,
            // Driving surfaces, laid out as RoadFirst + profile * SurfaceCount +
            // surface. Arithmetic rather than eighty named members.
            RoadFirst,
            COUNT = RoadFirst + RoadClassCount * SurfaceCount,
        }

        public static Slot SlotOf(int profile, Surface surf) =>
            (Slot)((int)Slot.RoadFirst + profile * SurfaceCount + (int)surf);

        /// <summary>
        /// Has this stretch been resurfaced recently? RG2's own _roadAge hash,
        /// ported constant for constant. 40% new, deterministic from position
        /// alone — so a road is the same age on every visit, across a tile
        /// unload, with nothing to store.
        /// </summary>
        public static bool IsFresh(Vector2 seed)
        {
            unchecked
            {
                int x = (int)(seed.x * 100f), y = (int)(seed.y * 100f);
                uint h = (uint)((x * unchecked((int)0x9e3779b1)) ^ (y * unchecked((int)0x6a09e667)));
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return h % 100u < 40u;
            }
        }

        /// <summary>Concrete where the road is on structure, asphalt where it
        /// is on the ground. A bridge deck IS poured concrete.</summary>
        public static Surface SurfaceOf(CityMap.Edge e, bool elevated)
        {
            bool fresh = IsFresh(e.pts[0]);
            return elevated ? (fresh ? Surface.ConcreteNew : Surface.ConcreteOld)
                            : (fresh ? Surface.AsphaltNew : Surface.AsphaltOld);
        }

        public static Slot RoadSlot(CityMap.Edge e, bool elevated) =>
            SlotOf(e.profile, SurfaceOf(e, elevated));

        /// <summary>A freeway carriageway on the ground carries a barrier
        /// both sides. Expressway carriageways too; ramps and two-way roads
        /// do not (a ramp's edge is where its gore is).</summary>
        public static bool Barriered(CityMap.Edge e) =>
            !e.link && ((e.cls >= 5) || (e.cls == 4 && e.oneway));

        // facade texture footprints in metres (how much wall one repeat covers)
        static readonly Vector2[] FacadeMeters =
        {
            new Vector2(9.5f, 12.5f),   // FacadeTower
            new Vector2(10.5f, 13.5f),  // FacadeMid
            new Vector2(6.5f, 6.5f),    // FacadeBrick
            new Vector2(24.0f, 4.2f),   // Shops (the atlas carries FOUR 6 m fronts per repeat)
            new Vector2(8.0f, 8.0f),    // FacadeGlass: 4x4 panes of 2 m
            new Vector2(6.0f, 3.1f),    // FacadeHouse: siding, one window per repeat, one storey tall
        };
        const float ShopFloorH = 4.2f;
        const float RoofTileM = 3.5f;
        const float RoofFlatM = 8f;

        public class SolidBox
        {
            public Vector3 center;   // tile-local
            public Vector3 size;
            public float yawDeg;
        }

        public class TileMeshes
        {
            public Vector3 origin;
            public Mesh ground;     public Slot[] groundSlots;
            public Mesh roads;      public Slot[] roadSlots;
            public Mesh barriers;   // concrete, its own collider
            public Mesh water;
            public Mesh buildings;  public Slot[] buildingSlots;
            public List<SolidBox> solids = new List<SolidBox>();
            public List<Vector4> lamps = new List<Vector4>(); // xyz + yaw, future use
            /// <summary>Walls the emitter caught pointing the wrong way. Zero,
            /// or the audit fails the build.</summary>
            public int wallFacingErrors;
            public int footprintCount, houseCount, goreCount;
        }

        // ---- growable buckets, one per slot, reused across tiles ----------
        class Bucket
        {
            public List<Vector3> v = new List<Vector3>(512);
            public List<Vector2> uv = new List<Vector2>(512);
            public List<int> t = new List<int>(1024);
            public void Clear() { v.Clear(); uv.Clear(); t.Clear(); }
            public int Count => v.Count;

            /// <summary>Raw quad: emits (a,c,b)+(a,d,c). Shows the side from
            /// which a→b→c→d reads anticlockwise. Prefer <see cref="Up"/> and
            /// <see cref="Wall"/>, which decide that for you.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }

            /// <summary>A horizontal-ish quad that faces UP whatever order
            /// its corners arrive in: the map-view signed area decides.</summary>
            public void Up(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                           Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
                           + (c.x - a.x) * (d.z - a.z) - (d.x - a.x) * (c.z - a.z);
                if (area >= 0f) Quad(a, b, c, d, ua, ub, uc, ud);
                else Quad(a, d, c, b, ua, ud, uc, ub);
            }

            /// <summary>A vertical quad between two plan points, facing the
            /// side <paramref name="outward"/> points to. u runs along the
            /// wall from p to q, v up.</summary>
            public void Wall(Vector3 p, Vector3 q, float y0, float y1, Vector2 outward,
                             float u0, float u1, float v0, float v1)
            {
                // a wall from a to c faces the LEFT of a→c in map view
                Vector2 d = new Vector2(q.x - p.x, q.z - p.z);
                Vector2 left = new Vector2(-d.y, d.x);
                if (Vector2.Dot(left, outward) < 0f) { var t = p; p = q; q = t; var tu = u0; u0 = u1; u1 = tu; }
                Quad(new Vector3(p.x, y0, p.z), new Vector3(p.x, y1, p.z),
                     new Vector3(q.x, y1, q.z), new Vector3(q.x, y0, q.z),
                     new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
            }

            /// <summary>Normal of the most recent triangle, for the emitter's
            /// own facing check.</summary>
            public Vector3 LastNormal()
            {
                int n = t.Count;
                if (n < 3) return Vector3.zero;
                Vector3 a = v[t[n - 3]], b = v[t[n - 2]], c = v[t[n - 1]];
                return Vector3.Cross(b - a, c - a);
            }
        }

        static readonly Bucket[] buckets = NewBuckets();
        static readonly Bucket barrierBucket = new Bucket();
        static Bucket[] NewBuckets()
        {
            var b = new Bucket[(int)Slot.COUNT];
            for (int i = 0; i < b.Length; i++) b[i] = new Bucket();
            return b;
        }

        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> edgeScratch = new HashSet<int>();
        static readonly List<Vector2> polyScratch = new List<Vector2>();

        /// <summary>Junction trim distance per node, computed once. Degree-2
        /// nodes are polyline continuations and are never trimmed; real
        /// junctions trim every arm back past the widest incident road.</summary>
        public static float[] NodeTrims(CityMap map)
        {
            var trims = new float[map.nodes.Length];
            for (int n = 0; n < map.nodes.Length; n++)
            {
                var list = map.nodeEdges[n];
                if (list.Count < 3) { trims[n] = 0f; continue; }
                float wMax = 0f;
                foreach (var ei in list) wMax = Mathf.Max(wMax, map.edges[ei].width);
                trims[n] = wMax * 0.5f + 2.0f;
            }
            return trims;
        }

        // ==================================================================
        public static TileMeshes Build(CityMap map, float[] nodeTrims,
            Dictionary<long, List<CityBuildings.B>> buildings, int tx, int tz)
        {
            var tm = new TileMeshes { origin = new Vector3(tx * TileSize, 0f, tz * TileSize) };
            var min = new Vector2(tx * TileSize, tz * TileSize);
            var max = min + new Vector2(TileSize, TileSize);

            foreach (var b in buckets) b.Clear();
            barrierBucket.Clear();
            goreGaps.Clear();

            BuildGround(map, tm, min);
            BuildGores(map, nodeTrims, tm, min, max);
            BuildRoadsAndDecks(map, nodeTrims, tm, min, max);
            BuildJunctions(map, nodeTrims, tm, min, max);
            BuildWater(map, tm, min, max);
            BuildBuildings(map, buildings, tm, tx, tz);
            BuildFootprints(map, tm, tx, tz);
            BuildHouses(map, tm, tx, tz);

            tm.ground = MeshFrom("ground", new[] { Slot.Ground, Slot.Pavement }, out var gSlots);
            tm.groundSlots = gSlots;
            tm.roads = MeshFrom("roads", RoadAndStructureSlots, out var roadSlots);
            tm.roadSlots = roadSlots;
            tm.barriers = MeshFromBucket("barriers", barrierBucket);
            tm.water = MeshFrom("water", new[] { Slot.Water }, out _);
            tm.buildings = MeshFrom("bld", BuildingSlots, out var bSlots);
            tm.buildingSlots = bSlots;
            return tm;
        }

        static readonly Slot[] BuildingSlots =
        {
            Slot.FacadeTower, Slot.FacadeMid, Slot.FacadeBrick, Slot.Shops,
            Slot.FacadeGlass, Slot.FacadeHouse, Slot.RoofTiles, Slot.RoofFlat,
        };

        /// <summary>Every driving surface plus the structural concrete, in slot
        /// order. MeshFrom drops the empty ones, so a tile with two profiles on
        /// it still ends up with two submeshes and not eighty.</summary>
        static readonly Slot[] RoadAndStructureSlots = BuildRoadSlotList();
        static Slot[] BuildRoadSlotList()
        {
            var list = new Slot[RoadClassCount * SurfaceCount + 1];
            for (int i = 0; i < list.Length - 1; i++) list[i] = (Slot)((int)Slot.RoadFirst + i);
            list[list.Length - 1] = Slot.Concrete;
            return list;
        }

        static Mesh MeshFrom(string name, Slot[] wanted, out Slot[] usedSlots)
        {
            int totalV = 0;
            var used = new List<Slot>();
            foreach (var s in wanted)
                if (buckets[(int)s].Count > 0) { used.Add(s); totalV += buckets[(int)s].Count; }
            usedSlots = used.ToArray();
            if (totalV == 0) return null;

            var mesh = new Mesh { name = name };
            if (totalV > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new List<Vector3>(totalV);
            var uvs = new List<Vector2>(totalV);
            foreach (var s in used) { verts.AddRange(buckets[(int)s].v); uvs.AddRange(buckets[(int)s].uv); }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = used.Count;
            int baseV = 0;
            for (int i = 0; i < used.Count; i++)
            {
                var bk = buckets[(int)used[i]];
                var tris = new int[bk.t.Count];
                for (int j = 0; j < tris.Length; j++) tris[j] = bk.t[j] + baseV;
                mesh.SetTriangles(tris, i, false);
                baseV += bk.Count;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh MeshFromBucket(string name, Bucket bk)
        {
            if (bk.Count == 0) return null;
            var mesh = new Mesh { name = name };
            if (bk.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(bk.v);
            mesh.SetUVs(0, bk.uv);
            mesh.SetTriangles(bk.t, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The ground, one quad per 8 m cell, grass or PAVEMENT. A downtown is
        /// paved edge to edge, and the first preview stood the skyline on a
        /// lawn: a cell within a kilometre of Trade & Tryon, or within 20 m of
        /// any real building that is not a house, is concrete. Per-cell quads
        /// rather than a shared lattice so a cell can change bucket; four
        /// times the ground vertices of a tile, which is still nothing.
        /// </summary>
        static void BuildGround(CityMap map, TileMeshes tm, Vector2 min)
        {
            var grass = buckets[(int)Slot.Ground];
            var pave = buckets[(int)Slot.Pavement];
            int res = GroundRes;
            float cell = TileSize / res;
            int stride = res + 1;
            var heights = new float[stride * stride];
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                    heights[z * stride + x] =
                        CityElevation.GroundY(map, min.x + x * cell, min.y + z * cell);

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var c = new Vector2(min.x + (x + 0.5f) * cell, min.y + (z + 0.5f) * cell);
                    bool paved = Vector2.Distance(c, map.uptown) < 1000f ||
                                 map.AnyFootprintNear(c, 20f, nonHouseOnly: true);
                    var bk = paved ? pave : grass;
                    float tile = paved ? 6f : 24f;
                    Vector3 P(int dx, int dz) => new Vector3((x + dx) * cell, heights[(z + dz) * stride + x + dx], (z + dz) * cell);
                    Vector2 U(int dx, int dz) => new Vector2((min.x + (x + dx) * cell) / tile, (min.y + (z + dz) * cell) / tile);
                    bk.Up(P(0, 0), P(0, 1), P(1, 1), P(1, 0), U(0, 0), U(0, 1), U(1, 1), U(1, 0));
                }
        }

        // ------------------------------------------------------------------
        //  Merge gores: where a ramp meets its mainline, the pavement between
        //  them is filled for as long as they run side by side. OSM joins the
        //  ramp to the carriageway at the END of the taper; the ramp's own
        //  geometry then diverges at a shallow angle, and the wedge between its
        //  inner edge and the mainline's outer edge is the acceleration lane
        //  and the painted gore. Filled from the junction trim outward until
        //  the two pavements are more than a few metres apart.
        // ------------------------------------------------------------------
        const float GoreStep = 6f;
        const float GoreReach = 190f;
        const float GoreMaxGap = 4.5f;
        const float GoreCos = 0.82f;    // ~35 degrees between ramp and mainline

        /// <summary>Barrier gaps: (edge, side, s0, s1) — where a gore attaches
        /// to a barriered mainline, the barrier stands down.</summary>
        static readonly List<(int edge, int side, float s0, float s1)> goreGaps = new List<(int, int, float, float)>();

        static void BuildGores(CityMap map, float[] trims, TileMeshes tm, Vector2 min, Vector2 max)
        {
            // Nodes within reach of the tile: a gore starting outside can end
            // inside, and a barrier gap inside can come from a node outside.
            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * (GoreReach + 20f), max + Vector2.one * (GoreReach + 20f), segScratch);
            edgeScratch.Clear();
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);
            var nodesSeen = new HashSet<int>();
            foreach (var ei in edgeScratch)
            {
                var L = map.edges[ei];
                if (!L.link) continue;
                for (int end = 0; end < 2; end++)
                {
                    int n = end == 0 ? L.a : L.b;
                    if (trims[n] <= 0f) continue;
                    if (!nodesSeen.Add((ei << 1) | end)) continue;
                    var np = map.nodes[n];
                    if (np.x < min.x - GoreReach - 20f || np.x > max.x + GoreReach + 20f ||
                        np.y < min.y - GoreReach - 20f || np.y > max.y + GoreReach + 20f) continue;
                    bool merge = end == 1;   // the ramp ARRIVES at this node
                    Vector2 tL = merge ? L.TangentAt(L.length) : L.TangentAt(0f);
                    // The mainline edge the ramp runs ALONGSIDE. OSM puts a
                    // merge node at the END of the taper, so the ramp's last
                    // hundred metres lie beside the mainline edge ARRIVING at
                    // that node; a diverge node is at the start of its
                    // taper, beside the edge LEAVING. The first cut looked
                    // the other way and every projection clamped onto the
                    // node itself: zero gores in the whole city.
                    CityMap.Edge M = null; float best = GoreCos;
                    foreach (var mi in map.nodeEdges[n])
                    {
                        var c = map.edges[mi];
                        if (c.link || c.cls < 3 || c == L) continue;
                        Vector2 tM;
                        if (merge) { if (c.b != n) continue; tM = c.TangentAt(c.length); }
                        else { if (c.a != n) continue; tM = c.TangentAt(0f); }
                        float d = Vector2.Dot(tL, tM);
                        if (d > best) { best = d; M = c; }
                    }
                    if (M == null) continue;
                    EmitGore(map, trims, tm, min, max, L, M, n, merge);
                }
            }
        }

        static void EmitGore(CityMap map, float[] trims, TileMeshes tm, Vector2 min, Vector2 max,
                             CityMap.Edge L, CityMap.Edge M, int node, bool merge)
        {
            float trim = trims[node];
            float sL0 = merge ? L.length - trim : trim;   // where the ramp ribbon starts
            float dirL = merge ? -1f : 1f;                // walking AWAY from the node along L
            bool prevOk = false;
            Vector3 prevIn = default, prevOut = default;
            float prevSM = 0f, gapStart = -1f, gapEnd = -1f;
            int side = 0;
            float travelled = 0f;
            int quads = 0;
            for (int k = 0; k <= 40; k++)
            {
                float sL = sL0 + dirL * k * GoreStep;
                if (sL < 0f || sL > L.length) break;
                travelled = k * GoreStep;
                if (travelled > GoreReach) break;
                var p = L.PointAt(sL);
                CityElevation.ProjectOn(M, p, out float sM);
                var q = M.PointAt(sM);
                var tM = M.TangentAt(sM);
                var rM = new Vector2(-tM.y, tM.x);     // M's "right" in the ribbon's sense (left of travel)
                float off = Vector2.Dot(p - q, rM);     // signed: which side of M the ramp is on
                int sideNow = off >= 0f ? 1 : -1;
                if (side == 0) side = sideNow;
                if (sideNow != side) break;
                var tL = L.TangentAt(sL);
                var rL = new Vector2(-tL.y, tL.x);
                // the ramp's edge that faces the mainline, and the mainline's
                // edge that faces the ramp
                float mHalf = M.width * 0.5f, lHalf = L.width * 0.5f;
                var outer = q + rM * (side * mHalf);
                float lSign = Vector2.Dot(rL, rM) >= 0f ? -side : side;
                var inner = p + rL * (lSign * lHalf);
                float gap = Vector2.Dot(inner - outer, rM) * side;
                bool ok = gap > -0.5f && gap < GoreMaxGap && sM > 0.5f && sM < M.length - 0.5f
                          && Mathf.Abs(sM - (merge ? M.length : 0f)) < GoreReach + trim + 5f;
                if (!ok) { if (prevOk) break; prevOk = false; continue; }
                float yIn = L.YAt(sL), yOut = M.YAt(sM);
                var vIn = new Vector3(inner.x - tm.origin.x, yIn, inner.y - tm.origin.z);
                var vOut = new Vector3(outer.x - tm.origin.x, yOut, outer.y - tm.origin.z);
                if (prevOk)
                {
                    var mid = (inner + outer + (new Vector2(prevIn.x, prevIn.z) + new Vector2(prevOut.x, prevOut.z) + new Vector2(tm.origin.x, tm.origin.z) * 2f)) * 0.25f;
                    if (mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y)
                    {
                        bool elev = M.ElevatedAt(sM);
                        var bk = buckets[(int)SlotOf(JunctionProfile, SurfaceOf(M, elev))];
                        float v0 = prevSM / 12f, v1 = sM / 12f;
                        bk.Up(prevOut, vOut, vIn, prevIn,
                              new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v1), new Vector2(1f, v0));
                        quads++;
                    }
                    if (gapStart < 0f) gapStart = Mathf.Min(prevSM, sM);
                    gapEnd = Mathf.Max(prevSM, sM);
                }
                prevOk = true; prevIn = vIn; prevOut = vOut; prevSM = sM;
            }
            if (gapStart >= 0f && Barriered(M))
                goreGaps.Add((M.index, side, gapStart - 1f, gapEnd + 1f));
            if (quads > 0) tm.goreCount++;
        }

        static bool InGoreGap(int edge, int side, float s0, float s1)
        {
            foreach (var g in goreGaps)
                if (g.edge == edge && g.side == side && s1 > g.s0 && s0 < g.s1) return true;
            return false;
        }

        // ------------------------------------------------------------------
        static void BuildRoadsAndDecks(CityMap map, float[] trims, TileMeshes tm,
                                       Vector2 min, Vector2 max)
        {
            segScratch.Clear();
            edgeScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 40f, max + Vector2.one * 40f, segScratch);
            foreach (var packed in segScratch) edgeScratch.Add(packed >> 12);

            foreach (var ei in edgeScratch)
            {
                var e = map.edges[ei];
                float sMin = trims[e.a], sMax = e.length - trims[e.b];
                if (sMax - sMin < 0.6f) continue;   // junction patch owns all of it

                var con = buckets[(int)Slot.Concrete];
                bool barrier = Barriered(e);

                // walk stations, clipped to the trims; sections at every
                // station boundary plus the exact ends
                float prevS = -1f;
                Vector3 prevL = default, prevR = default;
                bool prevElev = false;
                float sincePier = PierEvery * 0.6f;

                for (int i = 0; i < e.stS.Length; i++)
                {
                    float s = Mathf.Clamp(e.stS[i], sMin, sMax);
                    if (i < e.stS.Length - 1 && e.stS[i + 1] <= sMin) continue;
                    if (prevS >= 0f && s <= prevS + 0.01f && i < e.stS.Length - 1) continue;

                    var p = e.PointAt(s);
                    var tan = e.TangentAt(s);
                    // (-tan.y, tan.x) is tan turned 90 degrees COUNTER-clockwise
                    // in map view, which is the LEFT of travel. So the vertex
                    // called L below (p - right*hw) sits on the RIGHT of
                    // travel and R on the left; the ribbon's winding is
                    // verified with the names as they are, so the names stay
                    // and the texture U is mirrored to match: u = 0, the
                    // painter's left shoulder, goes on R. With symmetric
                    // textures nobody could tell; with a 3 m outside shoulder
                    // and a yellow inside edge line, the first cut put both on
                    // the wrong side of every carriageway.
                    var right = new Vector2(-tan.y, tan.x);
                    float y = e.YAt(s);
                    float hw = e.width * 0.5f;
                    var L = new Vector3(p.x - right.x * hw - tm.origin.x, y, p.y - right.y * hw - tm.origin.z);
                    var R = new Vector3(p.x + right.x * hw - tm.origin.x, y, p.y + right.y * hw - tm.origin.z);
                    bool elev = e.ElevatedAt(s);

                    if (prevS >= 0f && s > prevS)
                    {
                        // the tile owning the span midpoint emits it
                        var mid = e.PointAt((prevS + s) * 0.5f);
                        if (mid.x >= min.x && mid.x < max.x && mid.y >= min.y && mid.y < max.y)
                        {
                            // Per SPAN, not per edge: a road that climbs onto a
                            // viaduct halfway along is asphalt up to the
                            // abutment and concrete over the water.
                            var bk = buckets[(int)RoadSlot(e, prevElev || elev)];
                            float v0 = prevS / RoadVTile, v1 = s / RoadVTile;
                            // U = 0 on the left of travel (the L vertex), so a
                            // one-way carriageway's narrow inside shoulder and
                            // wide outside shoulder land where the painter put
                            // them. The winding (near-left, far-left, far-right,
                            // near-right) is the verified face-up order.
                            bk.Quad(prevL, L, R, prevR,
                                new Vector2(1f, v0), new Vector2(1f, v1),
                                new Vector2(0f, v1), new Vector2(0f, v0));

                            if (prevElev || elev)
                            {
                                EmitDeckSpan(map, e, con, tm, prevL, prevR, L, R, v0, v1);
                                sincePier += s - prevS;
                                if (sincePier >= PierEvery)
                                {
                                    sincePier = 0f;
                                    EmitPier(map, e, tm, (prevS + s) * 0.5f);
                                }
                            }
                            else
                            {
                                // Grounded span: give the tarmac a side. An
                                // elevated one already has a whole deck box.
                                EmitKerb(con, prevL, L, prevR, R, v0, v1);
                                if (barrier)
                                {
                                    var outL = new Vector2(-right.x, -right.y);
                                    if (!InGoreGap(e.index, -1, prevS, s)) EmitBarrier(prevL, L, outL, v0, v1);
                                    if (!InGoreGap(e.index, 1, prevS, s)) EmitBarrier(prevR, R, right, v0, v1);
                                }
                            }
                        }
                    }
                    prevS = s; prevL = L; prevR = R; prevElev = elev;
                    if (s >= sMax) break;
                }
            }
        }

        /// <summary>A Jersey barrier along one edge of a span: inner face, top
        /// and outer face, standing on the pavement edge and reaching outward.</summary>
        static void EmitBarrier(Vector3 a, Vector3 b, Vector2 outward, float v0, float v1)
        {
            var bk = barrierBucket;
            var o = new Vector3(outward.x, 0f, outward.y) * BarrierW;
            var up = Vector3.up * BarrierH;
            bk.Wall(a, b, a.y, a.y + BarrierH, -outward, v0, v1, 0.3f, 0.45f);            // inner face
            bk.Wall(a + o, b + o, a.y, a.y + BarrierH, outward, v0, v1, 0.3f, 0.45f);    // outer face
            bk.Up(a + up, b + up, b + o + up, a + o + up,
                  new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.5f, v1), new Vector2(0.5f, v0));
        }

        /// <summary>
        /// The two outward faces that turn a road ribbon into a slab.
        /// Winding per call-site here, verified: for the LEFT edge that means
        /// walking top-near, top-far, bottom-far, bottom-near; the right edge is
        /// the mirror of it.
        /// </summary>
        static void EmitKerb(Bucket con, Vector3 prevL, Vector3 L,
                             Vector3 prevR, Vector3 R, float v0, float v1)
        {
            var drop = Vector3.down * KerbDepth;
            var pLd = prevL + drop; var Ld = L + drop;
            var pRd = prevR + drop; var Rd = R + drop;
            con.Quad(prevL, L, Ld, pLd,
                new Vector2(0f, v0), new Vector2(0f, v1),
                new Vector2(0.15f, v1), new Vector2(0.15f, v0));
            con.Quad(prevR, pRd, Rd, R,
                new Vector2(0f, v0), new Vector2(0.15f, v0),
                new Vector2(0.15f, v1), new Vector2(0f, v1));
        }

        static void EmitDeckSpan(CityMap map, CityMap.Edge e, Bucket con, TileMeshes tm,
            Vector3 prevL, Vector3 prevR, Vector3 L, Vector3 R, float v0, float v1)
        {
            float dk = CityElevation.DeckThick;
            var dPL = prevL + Vector3.down * dk; var dPR = prevR + Vector3.down * dk;
            var dL = L + Vector3.down * dk; var dR = R + Vector3.down * dk;
            // fascia (outer faces) + soffit
            con.Quad(dPL, prevL, L, dL, new Vector2(0, v0), new Vector2(0.15f, v0), new Vector2(0.15f, v1), new Vector2(0, v1));
            con.Quad(prevR, dPR, dR, R, new Vector2(0.15f, v0), new Vector2(0, v0), new Vector2(0, v1), new Vector2(0.15f, v1));
            con.Quad(dPR, dPL, dL, dR, new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));

            // rails, inner+top+outer, both sides
            var up = Vector3.up * RailH;
            var inw = (prevR - prevL).normalized * RailW;
            EmitRail(con, prevL, L, up, inw, v0, v1);
            EmitRail(con, R, prevR, up, -inw, v1, v0);
        }

        static void EmitRail(Bucket con, Vector3 a, Vector3 b, Vector3 up, Vector3 inw, float v0, float v1)
        {
            con.Quad(a + inw, a + inw + up, b + inw + up, b + inw,
                new Vector2(0.3f, v0), new Vector2(0.45f, v0), new Vector2(0.45f, v1), new Vector2(0.3f, v1));
            con.Quad(a + inw + up, a + up, b + up, b + inw + up,
                new Vector2(0.45f, v0), new Vector2(0.5f, v0), new Vector2(0.5f, v1), new Vector2(0.45f, v1));
            con.Quad(a + up, a, b, b + up,
                new Vector2(0.45f, v0), new Vector2(0.3f, v0), new Vector2(0.3f, v1), new Vector2(0.45f, v1));
        }

        static void EmitPier(CityMap map, CityMap.Edge e, TileMeshes tm, float sAt)
        {
            var p = e.PointAt(sAt);
            float deckY = e.YAt(sAt) - CityElevation.DeckThick;
            float gy = CityElevation.GroundY(map, p.x, p.y);
            if (deckY - gy < 2.2f) return;

            var con = buckets[(int)Slot.Concrete];
            var tan = e.TangentAt(sAt);
            var right = new Vector3(-tan.y, 0f, tan.x);
            var fwd = new Vector3(tan.x, 0f, tan.y);
            var c = new Vector3(p.x - tm.origin.x, 0f, p.y - tm.origin.z);
            float hw = Mathf.Max(0.7f, e.width * 0.18f);
            var bottom = c + Vector3.up * (gy - 0.6f);
            var top = c + Vector3.up * deckY;
            EmitColumn(con, bottom, top, right * hw, fwd * 0.7f);

            tm.solids.Add(new SolidBox
            {
                center = c + Vector3.up * ((gy - 0.6f + deckY) * 0.5f),
                size = new Vector3(hw * 2f, deckY - gy + 0.6f, 1.4f),
                yawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg,
            });
        }

        static void EmitColumn(Bucket bk, Vector3 bottom, Vector3 top, Vector3 half1, Vector3 half2)
        {
            var b1 = bottom + half1 + half2; var b2 = bottom + half1 - half2;
            var b3 = bottom - half1 - half2; var b4 = bottom - half1 + half2;
            var t1 = top + half1 + half2; var t2 = top + half1 - half2;
            var t3 = top - half1 - half2; var t4 = top - half1 + half2;
            float vh = (top.y - bottom.y) / 6f;
            bk.Quad(b1, t1, t2, b2, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b2, t2, t3, b3, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b3, t3, t4, b4, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
            bk.Quad(b4, t4, t1, b1, new Vector2(0.55f, 0), new Vector2(0.55f, vh), new Vector2(0.7f, vh), new Vector2(0.7f, 0));
        }

        // ------------------------------------------------------------------
        static void BuildJunctions(CityMap map, float[] trims, TileMeshes tm,
                                   Vector2 min, Vector2 max)
        {
            var con = buckets[(int)Slot.Concrete];
            // The edge each corner came off is carried along so the skirt below
            // can tell a road MOUTH from a gap between two arms. Walling off a
            // mouth would put a concrete slab across the lane you drive in on.
            var corners = new List<(float ang, Vector3 pos, int edge)>(12);

            segScratch.Clear();
            map.EdgeSegsInRect(min - Vector2.one * 4f, max + Vector2.one * 4f, segScratch);
            var nodesHere = new HashSet<int>();
            foreach (var packed in segScratch)
            {
                var e = map.edges[packed >> 12];
                nodesHere.Add(e.a); nodesHere.Add(e.b);
            }

            foreach (var n in nodesHere)
            {
                if (trims[n] <= 0f) continue;
                var np = map.nodes[n];
                if (np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y) continue;

                // Intersections are resurfaced on their own schedule, so a
                // junction takes its age from the NODE rather than inheriting
                // one of its arms'.
                var bk = buckets[(int)SlotOf(JunctionProfile,
                    IsFresh(np) ? Surface.AsphaltNew : Surface.AsphaltOld)];

                float y = map.nodeY[n] + 0.012f;   // a hair proud of the arm ends
                corners.Clear();
                foreach (var ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    float trim = Mathf.Min(trims[n], e.length * 0.49f);
                    float at = e.a == n ? trim : e.length - trim;
                    var p = e.PointAt(at);
                    var tan = e.TangentAt(at);
                    var right = new Vector2(-tan.y, tan.x) * (e.width * 0.5f);
                    var c1 = p - right; var c2 = p + right;
                    corners.Add((Mathf.Atan2(c1.y - np.y, c1.x - np.x),
                        new Vector3(c1.x - tm.origin.x, y, c1.y - tm.origin.z), ei));
                    corners.Add((Mathf.Atan2(c2.y - np.y, c2.x - np.x),
                        new Vector3(c2.x - tm.origin.x, y, c2.y - tm.origin.z), ei));
                }
                if (corners.Count < 3) continue;
                corners.Sort((a, b) => a.ang.CompareTo(b.ang));

                int centerI = bk.v.Count;
                bk.v.Add(new Vector3(np.x - tm.origin.x, y, np.y - tm.origin.z));
                bk.uv.Add(new Vector2(np.x / 12f, np.y / 12f));
                for (int i = 0; i < corners.Count; i++)
                {
                    bk.v.Add(corners[i].pos);
                    bk.uv.Add(new Vector2((corners[i].pos.x + tm.origin.x) / 12f,
                                          (corners[i].pos.z + tm.origin.z) / 12f));
                }
                for (int i = 0; i < corners.Count; i++)
                {
                    int aI = centerI + 1 + i;
                    int bI = centerI + 1 + (i + 1) % corners.Count;
                    bk.t.Add(centerI); bk.t.Add(bI); bk.t.Add(aI);
                }

                // Skirt the perimeter, minus the road mouths. The two corners
                // of one arm sort adjacent, so a same-edge pair IS the mouth;
                // the pairs BETWEEN arms are the ones facing open ground.
                for (int i = 0; i < corners.Count; i++)
                {
                    var k0 = corners[i];
                    var k1 = corners[(i + 1) % corners.Count];
                    if (k0.edge == k1.edge) continue;          // road mouth
                    var drop = Vector3.down * KerbDepth;
                    con.Quad(k1.pos, k0.pos, k0.pos + drop, k1.pos + drop,
                        new Vector2(0f, 0f), new Vector2(0f, 0.6f),
                        new Vector2(0.15f, 0.6f), new Vector2(0.15f, 0f));
                }
            }
        }

        // ------------------------------------------------------------------
        static void BuildWater(CityMap map, TileMeshes tm, Vector2 min, Vector2 max)
        {
            var bk = buckets[(int)Slot.Water];

            foreach (var w in map.waters)
            {
                if (w.bbMax.x < min.x - 60f || w.bbMin.x > max.x + 60f ||
                    w.bbMax.y < min.y - 60f || w.bbMin.y > max.y + 60f) continue;

                if (!w.lake)
                {
                    float hw = w.width * 0.5f;
                    for (int i = 0; i + 1 < w.pts.Length; i++)
                    {
                        var mid = (w.pts[i] + w.pts[i + 1]) * 0.5f;
                        if (mid.x < min.x || mid.x >= max.x || mid.y < min.y || mid.y >= max.y) continue;
                        var d = (w.pts[i + 1] - w.pts[i]);
                        float len = d.magnitude;
                        if (len < 0.01f) continue;
                        d /= len;
                        var right = new Vector2(-d.y, d.x) * hw;
                        var a = w.pts[i]; var b = w.pts[i + 1];
                        float ya = CityElevation.RiverSurfaceY(a.x, a.y);
                        float yb = CityElevation.RiverSurfaceY(b.x, b.y);
                        bk.Quad(
                            new Vector3(a.x - right.x - tm.origin.x, ya, a.y - right.y - tm.origin.z),
                            new Vector3(b.x - right.x - tm.origin.x, yb, b.y - right.y - tm.origin.z),
                            new Vector3(b.x + right.x - tm.origin.x, yb, b.y + right.y - tm.origin.z),
                            new Vector3(a.x + right.x - tm.origin.x, ya, a.y + right.y - tm.origin.z),
                            new Vector2(0f, i / 3f), new Vector2(0f, (i + 1) / 3f),
                            new Vector2(1f, (i + 1) / 3f), new Vector2(1f, i / 3f));
                    }
                }
                else
                {
                    polyScratch.Clear();
                    polyScratch.AddRange(w.pts);
                    ClipPoly(polyScratch, min, max);
                    if (polyScratch.Count >= 3)
                        EarcutInto(bk, polyScratch, w.surfaceY, tm.origin, 26f);
                }
            }
        }

        static void ClipPoly(List<Vector2> poly, Vector2 min, Vector2 max)
        {
            ClipHalf(poly, p => p.x >= min.x, (a, b) => LerpX(a, b, min.x));
            ClipHalf(poly, p => p.x <= max.x, (a, b) => LerpX(a, b, max.x));
            ClipHalf(poly, p => p.y >= min.y, (a, b) => LerpY(a, b, min.y));
            ClipHalf(poly, p => p.y <= max.y, (a, b) => LerpY(a, b, max.y));
        }
        static Vector2 LerpX(Vector2 a, Vector2 b, float x) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.x - a.x) < 1e-6f ? 0f : (x - a.x) / (b.x - a.x));
        static Vector2 LerpY(Vector2 a, Vector2 b, float y) =>
            Vector2.Lerp(a, b, Mathf.Abs(b.y - a.y) < 1e-6f ? 0f : (y - a.y) / (b.y - a.y));

        static readonly List<Vector2> clipScratch = new List<Vector2>();
        static void ClipHalf(List<Vector2> poly, System.Func<Vector2, bool> inside,
                             System.Func<Vector2, Vector2, Vector2> cross)
        {
            clipScratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var prev = poly[(i + poly.Count - 1) % poly.Count];
                bool cIn = inside(cur), pIn = inside(prev);
                if (cIn)
                {
                    if (!pIn) clipScratch.Add(cross(prev, cur));
                    clipScratch.Add(cur);
                }
                else if (pIn) clipScratch.Add(cross(prev, cur));
            }
            poly.Clear();
            poly.AddRange(clipScratch);
        }

        /// <summary>Simple ear clipping, faces UP. Fine for the clipped lake
        /// pieces and for building roofs, which are small simple polygons.</summary>
        static void EarcutInto(Bucket bk, List<Vector2> poly, float y, Vector3 origin, float uvMeters)
        {
            var idx = new List<int>(poly.Count);
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            bool ccw = area > 0f;
            for (int i = 0; i < poly.Count; i++) idx.Add(ccw ? i : poly.Count - 1 - i);

            int baseI = bk.v.Count;
            foreach (var p in poly)
            {
                bk.v.Add(new Vector3(p.x - origin.x, y, p.y - origin.z));
                bk.uv.Add(new Vector2(p.x / uvMeters, p.y / uvMeters));
            }

            int guard = poly.Count * poly.Count + 16;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count];
                    int i1 = idx[i];
                    int i2 = idx[(i + 1) % idx.Count];
                    var a = poly[i0]; var b = poly[i1]; var c = poly[i2];
                    if ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y) <= 1e-7f) continue;
                    bool holds = true;
                    foreach (var j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(a, b, c, poly[j])) { holds = false; break; }
                    }
                    if (!holds) continue;
                    bk.t.Add(baseI + i0); bk.t.Add(baseI + i2); bk.t.Add(baseI + i1);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3)
            {
                bk.t.Add(baseI + idx[0]); bk.t.Add(baseI + idx[2]); bk.t.Add(baseI + idx[1]);
            }
        }

        static bool InTri(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ------------------------------------------------------------------
        //  Procedural frontage boxes (outside the footprint data) — CityWorld
        //  instantiates the prefab lots itself.
        // ------------------------------------------------------------------
        static void BuildBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> buildings,
                                   TileMeshes tm, int tx, int tz)
        {
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (buildings == null || !buildings.TryGetValue(key, out var list)) return;

            foreach (var b in list)
            {
                if (b.kind != 0) continue;

                float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                Vector2 fwd = new Vector2(sy, cy);           // faces the road
                Vector2 rgt = new Vector2(cy, -sy);
                Vector2 hw = rgt * (b.w * 0.5f);
                Vector2 hd = fwd * (b.d * 0.5f);

                var c1 = b.pos + hw + hd; var c2 = b.pos - hw + hd;
                var c3 = b.pos - hw - hd; var c4 = b.pos + hw - hd;
                float g = Mathf.Min(
                    Mathf.Min(CityElevation.GroundY(map, c1.x, c1.y), CityElevation.GroundY(map, c2.x, c2.y)),
                    Mathf.Min(CityElevation.GroundY(map, c3.x, c3.y), CityElevation.GroundY(map, c4.x, c4.y)));
                float y0 = g - BuildingSink;
                float y1 = y0 + b.h + BuildingSink;

                if (b.gable)
                {
                    // a suburban house: the long side faces the road
                    EmitGableHouse(tm, b.pos, rgt, b.w * 0.5f, b.d * 0.5f, y0, y0 + b.h * 0.7f, y1, Slot.FacadeHouse);
                    continue;
                }

                // Style 0 on a tall box is glass — a forty-storey slab wearing
                // a photographed brick office reads as a painted block.
                Slot wallSlot = b.style == 0 ? (b.h > 40f ? Slot.FacadeGlass : Slot.FacadeTower)
                              : b.style == 1 ? Slot.FacadeMid : Slot.FacadeBrick;
                bool shopFront = b.style == 3 && b.h > ShopFloorH + 1.5f;
                // walls: front (facing road), right, back, left — outward normals
                EmitWallStyled(tm, c2, c1, y0, y1, fwd, b.style == 3, shopFront, wallSlot);
                EmitWallStyled(tm, c1, c4, y0, y1, rgt, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c4, c3, y0, y1, -fwd, b.style == 3, false, wallSlot);
                EmitWallStyled(tm, c3, c2, y0, y1, -rgt, b.style == 3, false, wallSlot);

                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y1, tm), L(c3, y1, tm), L(c4, y1, tm), L(c1, y1, tm),
                    new Vector2(c2.x / RoofFlatM, c2.y / RoofFlatM), new Vector2(c3.x / RoofFlatM, c3.y / RoofFlatM),
                    new Vector2(c4.x / RoofFlatM, c4.y / RoofFlatM), new Vector2(c1.x / RoofFlatM, c1.y / RoofFlatM));

                tm.solids.Add(new SolidBox
                {
                    center = new Vector3(b.pos.x - tm.origin.x, (y0 + y1) * 0.5f, b.pos.y - tm.origin.z),
                    size = new Vector3(b.w, y1 - y0, b.d),
                    yawDeg = b.yaw * Mathf.Rad2Deg,
                });
            }
        }

        static Vector3 L(Vector2 p, float y, TileMeshes tm) =>
            new Vector3(p.x - tm.origin.x, y, p.y - tm.origin.z);

        /// <summary>One wall of a retail-or-not box: a shopfront on the
        /// ground floor where asked, brick above and elsewhere.</summary>
        static void EmitWallStyled(TileMeshes tm, Vector2 a, Vector2 c, float y0, float y1,
                                   Vector2 outward, bool retail, bool shopFront, Slot wallSlot)
        {
            float wallW = Vector2.Distance(a, c);
            if (retail)
            {
                float split = Mathf.Min(y0 + BuildingSink + ShopFloorH, y1);
                if (shopFront)
                {
                    float reps = Mathf.Max(1f, Mathf.Round(wallW / FacadeMeters[3].x));
                    EmitPanels(tm, Slot.Shops, a, c, y0 + BuildingSink, split, outward, reps, 1f);
                }
                else EmitFacadeQuad(tm, Slot.FacadeMid, a, c, y0, split, outward);
                if (y1 > split + 0.2f) EmitFacadeQuad(tm, Slot.FacadeMid, a, c, split, y1, outward);
                return;
            }
            EmitFacadeQuad(tm, wallSlot, a, c, y0, y1, outward);
        }

        static void EmitFacadeQuad(TileMeshes tm, Slot style, Vector2 a, Vector2 c,
                                   float y0, float y1, Vector2 outward)
        {
            var fm = FacadeMeters[(int)style - (int)Slot.FacadeTower];
            float wallW = Vector2.Distance(a, c);
            float u = Mathf.Max(1f, Mathf.Round(wallW / fm.x));
            float v = Mathf.Max(1f, Mathf.Round((y1 - y0) / fm.y));
            EmitPanels(tm, style, a, c, y0, y1, outward, u, v);
        }

        static int PanelCount(float meters) =>
            Mathf.Clamp(Mathf.CeilToInt(meters / FacadePanelMax), 1, meters > TallWallM ? TallPanelCap : FacadePanelCap);

        /// <summary>
        /// One wall, facing <paramref name="outward"/>, subdivided into
        /// affine-sized panels with the UV repeats handed out ACROSS the panels.
        /// The emitter checks its own work: if the first panel's normal points
        /// against the outward direction it was given, the tile counts a
        /// facing error and the audit fails the build. That is the whole
        /// history of the inside-out buildings, made into an assertion.
        /// </summary>
        static void EmitPanels(TileMeshes tm, Slot slot, Vector2 a, Vector2 c,
                               float y0, float y1, Vector2 outward, float uReps, float vReps)
        {
            var bk = buckets[(int)slot];
            int nx = PanelCount(Vector2.Distance(a, c));
            int ny = PanelCount(y1 - y0);
            for (int j = 0; j < ny; j++)
            {
                float t0 = (float)j / ny, t1 = (float)(j + 1) / ny;
                float ya = Mathf.Lerp(y0, y1, t0), yb = Mathf.Lerp(y0, y1, t1);
                for (int i = 0; i < nx; i++)
                {
                    float s0 = (float)i / nx, s1 = (float)(i + 1) / nx;
                    Vector2 pa = Vector2.Lerp(a, c, s0), pc = Vector2.Lerp(a, c, s1);
                    bk.Wall(L(pa, 0f, tm), L(pc, 0f, tm), ya, yb, outward,
                            uReps * s0, uReps * s1, vReps * t0, vReps * t1);
                    if (i == 0 && j == 0)
                    {
                        var n = bk.LastNormal();
                        if (Vector3.Dot(n, new Vector3(outward.x, 0f, outward.y)) < 0f) tm.wallFacingErrors++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  Real buildings from OSM footprints
        // ------------------------------------------------------------------
        static readonly List<Vector2> roofScratch = new List<Vector2>(32);

        static void BuildFootprints(CityMap map, TileMeshes tm, int tx, int tz)
        {
            var list = map.FootprintsInTile(tx, tz);
            if (list == null) return;
            foreach (var fi in list)
            {
                var f = map.footprints[fi];
                if (f.propKind != 0) continue;   // a model stands here; CityWorld places it
                float g = float.MaxValue;
                foreach (var p in f.pts) g = Mathf.Min(g, CityElevation.GroundY(map, p.x, p.y));
                float y0 = g - BuildingSink;
                float top = y0 + BuildingSink + f.h;

                if (f.gable)
                {
                    float eave = y0 + BuildingSink + f.h * 0.68f;
                    EmitGableHouse(tm, f.centre, f.u, f.hu, f.hv, y0, eave, top, Slot.FacadeHouse);
                    tm.footprintCount++;
                    continue;
                }

                Slot wallSlot = f.style == 0 ? Slot.FacadeGlass
                              : f.style == 1 ? Slot.FacadeTower
                              : f.style == 3 ? Slot.FacadeHouse
                              : Slot.FacadeMid;
                // A shopfront goes on the wall that faces the nearest street.
                int frontWall = -1;
                if (f.style == 4 && f.h > ShopFloorH + 1.5f &&
                    map.NearestRoadPoint(f.centre, 70f, skipLinks: true, out int rei, out float rs, out _))
                {
                    var q = map.edges[rei].PointAt(rs);
                    var toRoad = (q - f.centre).normalized;
                    float bestDot = 0.5f;
                    for (int i = 0; i < f.pts.Length; i++)
                    {
                        var d = f.pts[(i + 1) % f.pts.Length] - f.pts[i];
                        if (d.sqrMagnitude < 4f) continue;
                        var n = new Vector2(d.y, -d.x).normalized;   // outward, for a CCW polygon
                        float dt = Vector2.Dot(n, toRoad);
                        if (dt > bestDot) { bestDot = dt; frontWall = i; }
                    }
                }
                int n0 = f.pts.Length;
                for (int i = 0; i < n0; i++)
                {
                    var a = f.pts[i]; var c = f.pts[(i + 1) % n0];
                    var d = c - a;
                    if (d.sqrMagnitude < 0.04f) continue;
                    var outward = new Vector2(d.y, -d.x).normalized;
                    EmitWallStyled(tm, a, c, y0, top, outward, f.style == 4, i == frontWall, wallSlot);
                }

                roofScratch.Clear();
                roofScratch.AddRange(f.pts);
                EarcutInto(buckets[(int)Slot.RoofFlat], roofScratch, top, tm.origin, RoofFlatM);

                // A tall tower gets a crown: a smaller prism on top, then a
                // smaller one still — enough silhouette to tell the Bank of
                // America Corporate Center from a box, at a distance, in fog.
                if (f.h > 120f) EmitCrown(map, tm, f, top, wallSlot);

                tm.solids.Add(new SolidBox
                {
                    center = new Vector3(f.centre.x - tm.origin.x, (y0 + top) * 0.5f, f.centre.y - tm.origin.z),
                    size = new Vector3(f.hv * 2f, top - y0, f.hu * 2f),
                    yawDeg = Mathf.Atan2(f.u.x, f.u.y) * Mathf.Rad2Deg,
                });
                tm.footprintCount++;
            }
        }

        static void EmitCrown(CityMap map, TileMeshes tm, CityMap.Footprint f, float top, Slot slot)
        {
            float hu = f.hu, hv = f.hv;
            var v = new Vector2(-f.u.y, f.u.x);
            float y = top;
            for (int step = 0; step < 2; step++)
            {
                hu *= 0.62f; hv *= 0.62f;
                float h = f.h * (step == 0 ? 0.09f : 0.07f);
                var c1 = f.centre + f.u * hu + v * hv; var c2 = f.centre - f.u * hu + v * hv;
                var c3 = f.centre - f.u * hu - v * hv; var c4 = f.centre + f.u * hu - v * hv;
                EmitFacadeQuad(tm, slot, c2, c1, y, y + h, v);
                EmitFacadeQuad(tm, slot, c1, c4, y, y + h, f.u);
                EmitFacadeQuad(tm, slot, c4, c3, y, y + h, -v);
                EmitFacadeQuad(tm, slot, c3, c2, y, y + h, -f.u);
                var roof = buckets[(int)Slot.RoofFlat];
                roof.Up(L(c2, y + h, tm), L(c3, y + h, tm), L(c4, y + h, tm), L(c1, y + h, tm),
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));
                y += h;
            }
        }

        /// <summary>
        /// A gabled house on an oriented box: four walls to the eave, two roof
        /// planes to a ridge along the long axis, a gable triangle at each end.
        /// The same routine builds a real footprint's house and the interior
        /// fill's, so they cannot look like two kinds of house.
        /// </summary>
        static void EmitGableHouse(TileMeshes tm, Vector2 centre, Vector2 u, float hu, float hv,
                                   float y0, float eave, float ridge, Slot wallSlot)
        {
            var v = new Vector2(-u.y, u.x);
            var c1 = centre + u * hu + v * hv;   // +u +v
            var c2 = centre - u * hu + v * hv;   // -u +v
            var c3 = centre - u * hu - v * hv;   // -u -v
            var c4 = centre + u * hu - v * hv;   // +u -v
            EmitFacadeQuad(tm, wallSlot, c2, c1, y0, eave, v);
            EmitFacadeQuad(tm, wallSlot, c1, c4, y0, eave, u);
            EmitFacadeQuad(tm, wallSlot, c4, c3, y0, eave, -v);
            EmitFacadeQuad(tm, wallSlot, c3, c2, y0, eave, -u);

            var rN = centre + u * hu;   // ridge ends
            var rS = centre - u * hu;
            var roof = buckets[(int)Slot.RoofTiles];
            float uRep = Mathf.Max(1f, Mathf.Round(hu * 2f / RoofTileM));
            float vRep = Mathf.Max(1f, Mathf.Round(Mathf.Sqrt(hv * hv + (ridge - eave) * (ridge - eave)) / RoofTileM));
            // +v plane and -v plane, both facing up (Up() settles the order)
            roof.Up(L(rS, ridge, tm), L(rN, ridge, tm), L(c1, eave, tm), L(c2, eave, tm),
                    new Vector2(0f, 0f), new Vector2(uRep, 0f), new Vector2(uRep, vRep), new Vector2(0f, vRep));
            roof.Up(L(c3, eave, tm), L(c4, eave, tm), L(rN, ridge, tm), L(rS, ridge, tm),
                    new Vector2(0f, vRep), new Vector2(uRep, vRep), new Vector2(uRep, 0f), new Vector2(0f, 0f));

            // gable ends: vertical triangles facing +u and -u
            var wall = buckets[(int)wallSlot];
            EmitGableTri(wall, tm, c1, rN, c4, eave, ridge, u);
            EmitGableTri(wall, tm, c3, rS, c2, eave, ridge, -u);

            tm.solids.Add(new SolidBox
            {
                center = new Vector3(centre.x - tm.origin.x, (y0 + ridge) * 0.5f, centre.y - tm.origin.z),
                size = new Vector3(hv * 2f, ridge - y0, hu * 2f),
                yawDeg = Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg,
            });
        }

        static void EmitGableTri(Bucket bk, TileMeshes tm, Vector2 a, Vector2 apex, Vector2 c,
                                 float eave, float ridge, Vector2 outward)
        {
            // a triangle from a to c faces the LEFT of a->c, like a wall
            Vector2 d = c - a;
            Vector2 left = new Vector2(-d.y, d.x);
            if (Vector2.Dot(left, outward) < 0f) { var t = a; a = c; c = t; }
            var fm = FacadeMeters[(int)Slot.FacadeHouse - (int)Slot.FacadeTower];
            float uRep = Mathf.Max(1f, Mathf.Round(Vector2.Distance(a, c) / fm.x));
            bk.Tri(L(a, eave, tm), L(apex, ridge, tm), L(c, eave, tm),
                   new Vector2(0f, 0f), new Vector2(uRep * 0.5f, (ridge - eave) / fm.y), new Vector2(uRep, 0f));
        }

        // ------------------------------------------------------------------
        //  Interior fill: houses between the arterials, where OSM gave us
        //  roads but no footprints. A deterministic grid per tile, aligned to
        //  the nearest street, thinning with distance from uptown and from the
        //  road. Cheap gable boxes in the tile mesh — one draw call for a
        //  whole subdivision.
        // ------------------------------------------------------------------
        const float HouseCell = 24f;

        static void BuildHouses(CityMap map, TileMeshes tm, int tx, int tz)
        {
            var min = new Vector2(tx * TileSize, tz * TileSize);
            int cells = Mathf.RoundToInt(TileSize / HouseCell);
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    int gx = tx * cells + cx, gz = tz * cells + cz;
                    var c = new Vector2(min.x + (cx + 0.5f) * HouseCell + (Hash01(gx, gz, 1) - 0.5f) * 9f,
                                        min.y + (cz + 0.5f) * HouseCell + (Hash01(gx, gz, 2) - 0.5f) * 9f);
                    if (map.footprintBounds.Contains(c)) continue;

                    // nearest street, and the corridor test against every road
                    segScratch.Clear();
                    map.EdgeSegsInRect(c - Vector2.one * 260f, c + Vector2.one * 260f, segScratch);
                    float dRoad = float.MaxValue; Vector2 tanRoad = Vector2.right;
                    bool blocked = false;
                    foreach (var packed in segScratch)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = map.edges[ei];
                        Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                        float L2 = d.sqrMagnitude;
                        float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                        float dist = Vector2.Distance(c, a + d * t);
                        if (dist < e.CorridorHalf + 24f) { blocked = true; break; }
                        if (!e.link && e.cls < 5 && dist < dRoad) { dRoad = dist; tanRoad = d.normalized; }
                    }
                    if (blocked || dRoad > 250f) continue;

                    float distUp = Vector2.Distance(c, map.uptown);
                    float keep = distUp < 7000f ? 0.62f : distUp < 12000f ? 0.42f : distUp < 16000f ? 0.22f : 0.07f;
                    keep *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((dRoad - 110f) / 140f));
                    if (Hash01(gx, gz, 3) > keep) continue;

                    segScratch.Clear();
                    map.WaterSegsInRect(c - Vector2.one * 30f, c + Vector2.one * 30f, segScratch);
                    if (segScratch.Count > 0) continue;

                    float hu = 4.6f + Hash01(gx, gz, 4) * 2.2f;   // half length, along the street
                    float hv = 3.8f + Hash01(gx, gz, 5) * 1.6f;   // half depth
                    float eaveH = 3.0f + Hash01(gx, gz, 6) * 0.6f;
                    float riseH = 1.7f + Hash01(gx, gz, 7) * 0.9f;
                    var u = tanRoad;
                    var v = new Vector2(-u.y, u.x);
                    float g = float.MaxValue;
                    foreach (var corner in new[] { c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv })
                        g = Mathf.Min(g, CityElevation.GroundY(map, corner.x, corner.y));
                    float y0 = g - BuildingSink;
                    EmitGableHouse(tm, c, u, hu, hv, y0, y0 + BuildingSink + eaveH, y0 + BuildingSink + eaveH + riseH, Slot.FacadeHouse);
                    tm.houseCount++;
                }
        }

        static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 2246822519) + 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
