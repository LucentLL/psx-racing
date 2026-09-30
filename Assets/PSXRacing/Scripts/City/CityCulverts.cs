using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// WHERE A SMALL STREAM GOES UNDER A ROAD (plan WP-25). R1 (WP-04b) carved
    /// Charlotte's small streams, the ravines, into the ground and gave them no
    /// span: a road over one keeps its embankment, as a road over a culvert
    /// does. This finds those culverts and says where each END is, so the
    /// tile builder can stand a concrete headwall there with the pipe's mouth
    /// in it (CityMeshes.BuildCulverts).
    ///
    /// A culvert is a ravine's line crossing a road's line where the road is
    /// on the GROUND (a deck over a ravine is a bridge, not a culvert) and
    /// stands at least <see cref="MinFillM"/> over the ravine's floor (less
    /// leaves no room for a pipe and its cover; the road is then simply on
    /// the ground, and the channel meets it as a swale). Each end is found by
    /// walking the ravine's line away from the road, on a fixed 1 m grid of
    /// the line's own arc length, until the ground is:
    ///   * clear of every road: past its pavement, its clear zone (the DOT
    ///     rule: nothing a car leaving the road would hit) and half the wall;
    ///   * down at the TOE of the embankment: the drawn ground (the 8 m
    ///     lattice, what the wall will stand on) within <see cref="ToeTolM"/>
    ///     of the lowest it reaches along the rest of the walk.
    /// A divided road's two carriageways, or two roads over one ravine, each
    /// walk to the same grid point, so they find the same end; ends are keyed
    /// by (ravine, grid index) and kept once. Every tile that can own an end
    /// sees every crossing that can make it, so all tiles agree.
    ///
    /// The road is never touched: nothing here changes the ground function or
    /// a road's height. The embankment the road already sits on is the
    /// culvert's; the builder only adds the wall, its wing walls, the backfill
    /// behind them and the pipe's mouth, all past the clear zone.
    /// </summary>
    public static class CityCulverts
    {
        /// <summary>The road must stand this far over the ravine's floor at
        /// the crossing, and over the drawn ground at the end: a 0.9 m pipe
        /// and its cover.</summary>
        public const float MinFillM = 1.6f;
        /// <summary>The toe: the drawn ground within this of the lowest it
        /// reaches further out along the ravine.</summary>
        public const float ToeTolM = 0.35f;
        /// <summary>How far along the ravine an end is looked for, each way,
        /// on a grid of <see cref="WalkStepM"/>.</summary>
        public const float WalkMaxM = 60f, WalkStepM = 1f;
        /// <summary>The foot of the embankment: at most this over the lowest
        /// drawn ground ahead, with the fill FillBackM behind already
        /// FillBackRise of a wall's height higher.</summary>
        public const float FootOverToeM = 1.2f, FillBackM = 5f, FillBackRise = 0.7f;
        /// <summary>A channel the lattice shows at all: the lowest drawn
        /// ground along the walk within this of the carved floor.</summary>
        public const float ChannelShownM = 1.5f;
        /// <summary>Wall and pipe sizes: the pipe by the fill over it
        /// (0.9 / 1.2 / 1.5 m), the wall a pipe plus 0.45 m high and a pipe
        /// plus 2.4 m wide.</summary>
        public static float PipeFor(float fill) => fill >= 3.4f ? 1.5f : fill >= 2.6f ? 1.2f : 0.9f;
        public const float WallOverPipeM = 0.45f, WallBesidePipeM = 1.2f, WallThickM = 0.35f;
        /// <summary>Nothing within this of a creek (the ravine has joined it)
        /// or of a building's footprint.</summary>
        const float CreekClearM = 8f;

        public struct End
        {
            /// <summary>The middle of the wall's FRONT face at the pipe's
            /// invert, on the ravine's line (plan x, z), and the ground's
            /// height there.</summary>
            public Vector2 at;
            public float groundY;
            /// <summary>Unit, square off the nearest road and away from it:
            /// the way the wall faces. A headwall on a skewed crossing is
            /// built parallel to the road, so the fill it retains climbs
            /// straight behind it at the embankment's own slope (along the
            /// ravine, a crossing at 60 degrees climbs at half that).</summary>
            public Vector2 outward;
            /// <summary>Unit, along the ravine, away from the road: the way
            /// the water leaves.</summary>
            public Vector2 downstream;
            public float pipeD, wallH, wallW;
            /// <summary>The road the walk last had to clear, and how far past
            /// its pavement edge the end stands.</summary>
            public int edge;
            public float pastPave;
            /// <summary>(ravine, grid index): one wall per key.</summary>
            public long key;
        }

        public struct Culvert
        {
            public int water, edge;
            /// <summary>Arc lengths along the ravine and the road.</summary>
            public float ws, es;
            public Vector2 at;
            public float roadY, floorY;
            /// <summary>Null when the crossing takes no culvert (deck,
            /// shallow, no bed); the reason for the audit.</summary>
            public string skip;
            /// <summary>The ends toward lower and higher ravine arc length;
            /// has* false where the walk found none (and why).</summary>
            public bool hasLo, hasHi;
            public End lo, hi;
            public string missLo, missHi;
        }

        // the culverts of the last tiles asked for (the tile build and the
        // occupancy mask's static half both ask; neither is cheap to repeat)
        static readonly Dictionary<long, List<Culvert>> tileCache = new Dictionary<long, List<Culvert>>();
        static readonly Queue<long> tileOrder = new Queue<long>();
        static CityMap tileCacheMap;
        const int TileCacheSize = 48;

        /// <summary><see cref="Near"/> for one city tile, cached for the last
        /// few tiles asked for. The list is shared: read it, never change it.</summary>
        public static List<Culvert> ForTile(CityMap map, CityMeshes.Trims trims, int tx, int tz)
        {
            if (tileCacheMap != map) { tileCache.Clear(); tileOrder.Clear(); tileCacheMap = map; }
            long k = ((long)tx << 32) ^ (uint)tz;
            if (tileCache.TryGetValue(k, out var l)) return l;
            l = new List<Culvert>();
            var min = new Vector2(tx * CityMeshes.TileSize, tz * CityMeshes.TileSize);
            Near(map, trims, min, min + Vector2.one * CityMeshes.TileSize, l);
            tileCache[k] = l; tileOrder.Enqueue(k);
            while (tileOrder.Count > TileCacheSize) tileCache.Remove(tileOrder.Dequeue());
            return l;
        }

        static readonly HashSet<int> ravScratch = new HashSet<int>();
        static readonly HashSet<int> segScratch = new HashSet<int>();
        static readonly HashSet<int> nearScratch = new HashSet<int>();
        static readonly HashSet<long> seenScratch = new HashSet<long>();

        /// <summary>Every culvert whose crossing lies within
        /// <see cref="WalkMaxM"/> + 16 m of the rectangle (so every end that
        /// can fall inside it), in a fixed order: by ravine, then arc length.
        /// <paramref name="trims"/> (may be null) adds the race routes'
        /// run-off to what an end must clear.</summary>
        public static void Near(CityMap map, CityMeshes.Trims trims, Vector2 min, Vector2 max, List<Culvert> outList)
        {
            outList.Clear();
            if (map.waters == null) return;
            float m = WalkMaxM + 16f;
            ravScratch.Clear();
            map.RavineSegsInRect(min - Vector2.one * m, max + Vector2.one * m, ravScratch);
            seenScratch.Clear();
            var found = new List<Culvert>();
            foreach (int packed in ravScratch)
            {
                int wi = packed >> 12, j = packed & 0xFFF;
                var w = map.waters[wi];
                if (!w.ravine || w.pts == null || j + 1 >= w.pts.Length || w.bedY == null) continue;
                Vector2 c0 = w.pts[j], c1 = w.pts[j + 1];
                segScratch.Clear();
                map.EdgeSegsInRect(Vector2.Min(c0, c1) - Vector2.one, Vector2.Max(c0, c1) + Vector2.one, segScratch);
                foreach (int ep in segScratch)
                {
                    int ei = ep >> 12, i = ep & 0xFFF;
                    var e = map.edges[ei];
                    if (i + 1 >= e.pts.Length) continue;
                    if (!SegX(e.pts[i], e.pts[i + 1], c0, c1, out float t, out float u)) continue;
                    long key = ((long)wi << 40) ^ ((long)j << 26) ^ ((long)ei << 8) ^ i;
                    if (!seenScratch.Add(key)) continue;
                    var at = Vector2.LerpUnclamped(e.pts[i], e.pts[i + 1], t);
                    // only crossings whose ends can reach the rectangle
                    if (at.x < min.x - m || at.x > max.x + m || at.y < min.y - m || at.y > max.y + m) continue;
                    float ws = w.s[j] + (w.s[j + 1] - w.s[j]) * u;
                    float es = e.s[i] + (e.s[i + 1] - e.s[i]) * t;
                    found.Add(Solve(map, trims, wi, ei, ws, es, at));
                }
            }
            found.Sort((p, q) => p.water != q.water ? p.water.CompareTo(q.water) : p.ws != q.ws ? p.ws.CompareTo(q.ws) : p.edge.CompareTo(q.edge));
            // one wall per (ravine, grid index): the first culvert in the
            // fixed order keeps it
            var keys = new HashSet<long>();
            for (int k = 0; k < found.Count; k++)
            {
                var c = found[k];
                if (c.hasLo && !keys.Add(c.lo.key)) { c.hasLo = false; c.missLo = "shared"; }
                if (c.hasHi && !keys.Add(c.hi.key)) { c.hasHi = false; c.missHi = "shared"; }
                outList.Add(c);
            }
        }

        static Culvert Solve(CityMap map, CityMeshes.Trims trims, int wi, int ei, float ws, float es, Vector2 at)
        {
            var w = map.waters[wi];
            var e = map.edges[ei];
            var c = new Culvert { water = wi, edge = ei, ws = ws, es = es, at = at, roadY = e.YAt(es), floorY = float.NaN };
            if (e.ElevatedAt(es)) { c.skip = "deck"; return c; }
            float bed = CityElevation.BedYAt(w, ws);
            if (float.IsNaN(bed)) { c.skip = "no bed"; return c; }
            c.floorY = bed - CityElevation.RavineBelowBed;
            if (c.roadY - c.floorY < MinFillM) { c.skip = "shallow"; return c; }
            c.hasLo = Walk(map, trims, w, wi, ws, -1, c.roadY, out c.lo, out c.missLo);
            c.hasHi = Walk(map, trims, w, wi, ws, +1, c.roadY, out c.hi, out c.missHi);
            return c;
        }

        struct Sample { public float s, off, g; public Vector2 p, foot; public bool clear; public int edge; public float past; }
        static readonly List<Sample> walk = new List<Sample>(64);

        static bool Walk(CityMap map, CityMeshes.Trims trims, CityMap.Water w, int wi, float ws, int dir,
                         float roadY, out End end, out string miss)
        {
            end = default; miss = null;
            walk.Clear();
            int k0 = dir > 0 ? Mathf.FloorToInt(ws / WalkStepM) + 1 : Mathf.CeilToInt(ws / WalkStepM) - 1;
            // the widest wall this crossing could take, for the clearance
            float wallWMax = PipeFor(roadY - (CityElevation.BedYAt(w, ws) - CityElevation.RavineBelowBed)) + 2f * WallBesidePipeM;
            for (int k = k0; Mathf.Abs(k * WalkStepM - ws) <= WalkMaxM; k += dir)
            {
                float s = k * WalkStepM;
                if (s < 0f || s > w.length) break;
                var p = PointAt(w, s);
                float floor = CityElevation.BedYAt(w, s) - CityElevation.RavineBelowBed;
                bool clear = ClearOfRoads(map, p, wallWMax * 0.5f, out int edge, out float past, out Vector2 foot);
                if (clear && trims != null && RaceRunOff.Inside(map, trims, p)) clear = false;
                float g = CityMeshes.LatticeAt(map, p.x, p.y);
                walk.Add(new Sample { s = s, off = g - floor, g = g, p = p, foot = foot, clear = clear, edge = edge, past = past });
            }
            if (walk.Count == 0) { miss = "the ravine ends"; return false; }
            // the lowest the drawn ground gets under the rest of the walk
            float lowest = float.MaxValue;
            foreach (var sm in walk) if (sm.clear) lowest = Mathf.Min(lowest, sm.off);
            if (lowest == float.MaxValue) { miss = "never clear of a road"; return false; }
            if (lowest > ChannelShownM) { miss = "no channel on the lattice"; return false; }
            for (int i = 0; i < walk.Count; i++)
            {
                var sm = walk[i];
                if (!sm.clear) continue;
                // the toe: down within ToeTol of the lowest still ahead - or,
                // a little higher, at the FOOT OF THE EMBANKMENT: where the
                // fill behind climbs a wall's height within FillBackM, so the
                // wall is set into the fill and the backfill behind it meets
                // the fill (at the channel's own low point the land behind can
                // be a gentle run-out, and the wall stood free on it)
                float ahead = float.MaxValue;
                for (int j = i; j < walk.Count; j++) if (walk[j].clear) ahead = Mathf.Min(ahead, walk[j].off);
                bool atToe = sm.off <= ahead + ToeTolM;
                if (!atToe && sm.off <= ahead + FootOverToeM)
                {
                    float wallH = PipeFor(roadY - sm.g) + WallOverPipeM;
                    var backP = sm.p - TangentAt(w, sm.s) * dir * FillBackM;
                    atToe = CityMeshes.LatticeAt(map, backP.x, backP.y) - sm.g >= wallH * FillBackRise;
                }
                if (!atToe) continue;
                float fill = roadY - sm.g;
                if (fill < MinFillM) { miss = "shallow at the toe"; return false; }
                if (map.InLake(sm.p)) { miss = "a lake"; return false; }
                if (NearCreek(map, sm.p, CreekClearM)) { miss = "a creek"; return false; }
                float pipe = PipeFor(fill);
                float wallW = pipe + 2f * WallBesidePipeM;
                if (map.AnyFootprintNear(sm.p, wallW * 0.5f + 2f)) { miss = "a building"; return false; }
                var tan = TangentAt(w, sm.s) * dir;
                var away = sm.p - sm.foot;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : tan;
                // a face turned more than 60 degrees off the ravine would
                // stand along it: keep it square to the water there
                if (Vector2.Dot(away, tan) < 0.5f) away = tan;
                end = new End
                {
                    at = sm.p, groundY = sm.g, outward = away, downstream = tan,
                    pipeD = pipe, wallH = pipe + WallOverPipeM, wallW = wallW,
                    edge = sm.edge, pastPave = sm.past,
                    key = ((long)wi << 24) | (uint)Mathf.RoundToInt(sm.s / WalkStepM),
                };
                return true;
            }
            miss = "no toe";
            return false;
        }

        /// <summary>Is p clear of every road: past its pavement, its clear zone
        /// and <paramref name="halfWall"/>? Also the nearest road's index and
        /// how far past its pavement p stands.</summary>
        static bool ClearOfRoads(CityMap map, Vector2 p, float halfWall, out int nearEdge, out float nearPast, out Vector2 nearFoot)
        {
            nearEdge = -1; nearPast = float.MaxValue; nearFoot = p;
            const float R = 40f;
            nearScratch.Clear();
            map.EdgeSegsInRect(p - Vector2.one * R, p + Vector2.one * R, nearScratch);
            bool clear = true;
            foreach (int packed in nearScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                if (si + 1 >= e.pts.Length) continue;
                Vector2 a = e.pts[si], d = e.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                float dist = Vector2.Distance(p, a + d * t);
                float past = dist - e.width * 0.5f;
                if (past < nearPast) { nearPast = past; nearEdge = ei; nearFoot = a + d * t; }
                if (past < RoadsideOccupancy.ClearZoneOf(e) + halfWall + 0.5f) clear = false;
            }
            return clear;
        }

        static readonly HashSet<int> creekScratch = new HashSet<int>();
        static bool NearCreek(CityMap map, Vector2 p, float r)
        {
            creekScratch.Clear();
            map.WaterSegsInRect(p - Vector2.one * r, p + Vector2.one * r, creekScratch);
            foreach (int packed in creekScratch)
            {
                var w = map.waters[packed >> 12];
                if (w.lake) continue;
                int si = packed & 0xFFF;
                if (si + 1 >= w.pts.Length) continue;
                Vector2 a = w.pts[si], d = w.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / L2) : 0f;
                if (Vector2.Distance(p, a + d * t) < r + w.width * 0.5f) return true;
            }
            return false;
        }

        /// <summary>The point at arc length s along a water's line.</summary>
        public static Vector2 PointAt(CityMap.Water w, float s)
        {
            int i = Seg(w, s);
            float L = w.s[i + 1] - w.s[i];
            float t = L > 1e-6f ? Mathf.Clamp01((s - w.s[i]) / L) : 0f;
            return Vector2.LerpUnclamped(w.pts[i], w.pts[i + 1], t);
        }

        /// <summary>The unit direction of the line at arc length s.</summary>
        public static Vector2 TangentAt(CityMap.Water w, float s)
        {
            int i = Seg(w, s);
            var d = w.pts[i + 1] - w.pts[i];
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.right;
        }

        static int Seg(CityMap.Water w, float s)
        {
            int lo = 0, hi = w.pts.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (w.s[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return Mathf.Clamp(lo, 0, w.pts.Length - 2);
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            Vector2 r = b - a, q = d - c;
            float den = r.x * q.y - r.y * q.x;
            t = u = 0f;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 w = c - a;
            t = (w.x * q.y - w.y * q.x) / den;
            u = (w.x * r.y - w.y * r.x) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }
    }
}
