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
    /// Ownership rule at tile seams: a road span, river span or junction
    /// belongs to the tile that contains its MIDPOINT (or node), so no strip
    /// is ever emitted twice. Adjacent tiles derive shared boundary vertices
    /// from the same stations and the same GroundY, so edges meet exactly.
    /// </summary>
    public static class CityMeshes
    {
        public const float TileSize = 256f;
        public const int GroundRes = 32;          // 8 m cells
        public const float RoadVTile = 18f;       // metres of road per texture repeat
        public const float RailH = 0.95f;
        public const float RailW = 0.3f;
        public const float PierEvery = 26f;
        public const float BuildingSink = 0.55f;

        public enum Slot
        {
            Ground = 0, RoadMinor, RoadMajor, DividedGrass, DividedAsphalt,
            Motorway, Ramp, Junction, Concrete, Water,
            FacadeTower, FacadeMid, FacadeBrick, Shops,
            COUNT,
        }

        public static Slot RoadSlot(CityMap.Edge e)
        {
            if (e.link) return Slot.Ramp;
            if (e.cls >= 5) return Slot.Motorway;
            if (e.median > 0.5f) return e.medianGrass ? Slot.DividedGrass : Slot.DividedAsphalt;
            return e.lanes >= 4 ? Slot.RoadMajor : Slot.RoadMinor;
        }

        // facade texture footprints in metres (how much wall one repeat covers)
        static readonly Vector2[] FacadeMeters =
        {
            new Vector2(9.5f, 12.5f),   // FacadeTower
            new Vector2(10.5f, 13.5f),  // FacadeMid
            new Vector2(6.5f, 6.5f),    // FacadeBrick
            new Vector2(24.0f, 4.2f),   // Shops (the atlas carries FOUR 6 m fronts per repeat)
        };
        const float ShopFloorH = 4.2f;

        public class SolidBox
        {
            public Vector3 center;   // tile-local
            public Vector3 size;
            public float yawDeg;
        }

        public class TileMeshes
        {
            public Vector3 origin;
            public Mesh ground;
            public Mesh roads;      public Slot[] roadSlots;
            public Mesh water;
            public Mesh buildings;  public Slot[] buildingSlots;
            public List<SolidBox> solids = new List<SolidBox>();
            public List<Vector4> lamps = new List<Vector4>(); // xyz + yaw, future use
        }

        // ---- growable buckets, one per slot, reused across tiles ----------
        class Bucket
        {
            public List<Vector3> v = new List<Vector3>(512);
            public List<Vector2> uv = new List<Vector2>(512);
            public List<int> t = new List<int>(1024);
            public void Clear() { v.Clear(); uv.Clear(); t.Clear(); }
            public int Count => v.Count;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
        }

        static readonly Bucket[] buckets = NewBuckets();
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

            BuildGround(map, tm, min);
            BuildRoadsAndDecks(map, nodeTrims, tm, min, max);
            BuildJunctions(map, nodeTrims, tm, min, max);
            BuildWater(map, tm, min, max);
            BuildBuildings(map, buildings, tm, tx, tz);

            tm.ground = MeshFrom("ground", new[] { Slot.Ground }, out _);
            tm.roads = MeshFrom("roads", new[]
            {
                Slot.RoadMinor, Slot.RoadMajor, Slot.DividedGrass, Slot.DividedAsphalt,
                Slot.Motorway, Slot.Ramp, Slot.Junction, Slot.Concrete,
            }, out var roadSlots);
            tm.roadSlots = roadSlots;
            tm.water = MeshFrom("water", new[] { Slot.Water }, out _);
            tm.buildings = MeshFrom("bld", new[]
            {
                Slot.FacadeTower, Slot.FacadeMid, Slot.FacadeBrick, Slot.Shops,
            }, out var bSlots);
            tm.buildingSlots = bSlots;
            return tm;
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

        // ------------------------------------------------------------------
        static void BuildGround(CityMap map, TileMeshes tm, Vector2 min)
        {
            var bk = buckets[(int)Slot.Ground];
            int res = GroundRes;
            float cell = TileSize / res;
            int stride = res + 1;
            var heights = new float[stride * stride];
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                    heights[z * stride + x] =
                        CityElevation.GroundY(map, min.x + x * cell, min.y + z * cell);

            int i0 = bk.v.Count;
            for (int z = 0; z <= res; z++)
                for (int x = 0; x <= res; x++)
                {
                    bk.v.Add(new Vector3(x * cell, heights[z * stride + x], z * cell));
                    bk.uv.Add(new Vector2((min.x + x * cell) / 24f, (min.y + z * cell) / 24f));
                }
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int a = i0 + z * stride + x;
                    bk.t.Add(a); bk.t.Add(a + stride); bk.t.Add(a + 1);
                    bk.t.Add(a + 1); bk.t.Add(a + stride); bk.t.Add(a + stride + 1);
                }
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

                var bk = buckets[(int)RoadSlot(e)];
                var con = buckets[(int)Slot.Concrete];

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
                            float v0 = prevS / RoadVTile, v1 = s / RoadVTile;
                            // corner order near-left, far-left, far-right,
                            // near-right — clockwise from above, which is the
                            // face Unity draws. The first cut of this went the
                            // other way round and every street in the city
                            // rendered only from underneath.
                            bk.Quad(prevL, L, R, prevR,
                                new Vector2(0f, v0), new Vector2(0f, v1),
                                new Vector2(1f, v1), new Vector2(1f, v0));

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
                        }
                    }
                    prevS = s; prevL = L; prevR = R; prevElev = elev;
                    if (s >= sMax) break;
                }
            }
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
            var bk = buckets[(int)Slot.Junction];
            var corners = new List<(float ang, Vector3 pos)>(12);

            for (int n = 0; n < map.nodes.Length; n++)
            {
                if (trims[n] <= 0f) continue;
                var np = map.nodes[n];
                if (np.x < min.x || np.x >= max.x || np.y < min.y || np.y >= max.y) continue;

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
                        new Vector3(c1.x - tm.origin.x, y, c1.y - tm.origin.z)));
                    corners.Add((Mathf.Atan2(c2.y - np.y, c2.x - np.x),
                        new Vector3(c2.x - tm.origin.x, y, c2.y - tm.origin.z)));
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
                        EarcutInto(bk, polyScratch, w.surfaceY, tm.origin);
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

        static void EarcutInto(Bucket bk, List<Vector2> poly, float y, Vector3 origin)
        {
            // simple ear clipping; the clipped lake pieces are small
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
                bk.uv.Add(new Vector2(p.x / 26f, p.y / 26f));
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
        static void BuildBuildings(CityMap map, Dictionary<long, List<CityBuildings.B>> buildings,
                                   TileMeshes tm, int tx, int tz)
        {
            long key = ((long)tx << 24) ^ (tz & 0xFFFFFF);
            if (buildings == null || !buildings.TryGetValue(key, out var list)) return;

            foreach (var b in list)
            {
                // Prefab lots are CityWorld's to instantiate — no facade box,
                // no solid; the model carries its own collider.
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

                bool shopFront = b.style == 3 && b.h > ShopFloorH + 1.5f;
                // walls: front (c1-c2, facing road), right, back, left
                EmitWall(tm, b, c2, c1, y0, y1, shopFront, frontWall: true);
                EmitWall(tm, b, c1, c4, y0, y1, shopFront, frontWall: false);
                EmitWall(tm, b, c4, c3, y0, y1, shopFront, frontWall: false);
                EmitWall(tm, b, c3, c2, y0, y1, shopFront, frontWall: false);

                // flat roof
                var con = buckets[(int)Slot.FacadeBrick];
                con.Quad(
                    L(c2, y1, tm), L(c1, y1, tm), L(c4, y1, tm), L(c3, y1, tm),
                    new Vector2(0f, 0.94f), new Vector2(0.08f, 0.94f),
                    new Vector2(0.08f, 0.99f), new Vector2(0f, 0.99f));

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

        static void EmitWall(TileMeshes tm, CityBuildings.B b, Vector2 a, Vector2 c,
                             float y0, float y1, bool shopFront, bool frontWall)
        {
            float wallW = Vector2.Distance(a, c);
            var style = (Slot)((int)Slot.FacadeTower + Mathf.Clamp(b.style, 0, 3));

            if (b.style == 3)
            {
                // retail: shopfront on the ground floor of the FRONT wall only,
                // brick everywhere else and above
                float split = Mathf.Min(y0 + BuildingSink + ShopFloorH, y1);
                if (frontWall && shopFront)
                {
                    var shops = buckets[(int)Slot.Shops];
                    float reps = Mathf.Max(1f, Mathf.Round(wallW / FacadeMeters[3].x));
                    shops.Quad(
                        L(a, y0 + BuildingSink, tm), L(c, y0 + BuildingSink, tm),
                        L(c, split, tm), L(a, split, tm),
                        new Vector2(0f, 0f), new Vector2(reps, 0f),
                        new Vector2(reps, 1f), new Vector2(0f, 1f));
                }
                else
                {
                    EmitFacadeQuad(tm, Slot.FacadeBrick, a, c, y0, split, wallW);
                }
                if (y1 > split + 0.2f)
                    EmitFacadeQuad(tm, Slot.FacadeBrick, a, c, split, y1, wallW);
                return;
            }

            EmitFacadeQuad(tm, style, a, c, y0, y1, wallW);
        }

        static void EmitFacadeQuad(TileMeshes tm, Slot style, Vector2 a, Vector2 c,
                                   float y0, float y1, float wallW)
        {
            var bk = buckets[(int)style];
            var fm = FacadeMeters[(int)style - (int)Slot.FacadeTower];
            float u = Mathf.Max(1f, Mathf.Round(wallW / fm.x));
            float v = Mathf.Max(1f, Mathf.Round((y1 - y0) / fm.y));
            bk.Quad(L(a, y0, tm), L(c, y0, tm), L(c, y1, tm), L(a, y1, tm),
                new Vector2(0f, 0f), new Vector2(u, 0f), new Vector2(u, v), new Vector2(0f, v));
        }
    }
}
