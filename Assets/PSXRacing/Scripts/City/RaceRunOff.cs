using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// RACE RUN-OFF along Charlotte's city routes (plan section 5: "Do not put
    /// trees, poles or signs in ... race run-off"; G-play: AI retirements at or
    /// below baseline). The Uptown Loop, the Tryon Street Sprint and the
    /// Independence Sprint are raced flat out on the streets the trees line,
    /// and a street's clear zone (an arterial's 3.5 m) is sized for a driver at
    /// the posted limit, not for a racing car that runs wide. So along every
    /// route the roadside is kept clear <see cref="RunOffM"/> past the drawn
    /// edge on both sides, and on the OUTSIDE of every bend - where a car that
    /// runs out of grip goes - <see cref="CornerRunOffM"/>.
    ///
    /// Marks are capsules in plan, worked out once per map from the routes'
    /// edge chains, the half width read through
    /// <see cref="RoadsideOccupancy.RoadEdgeAt"/> (the one accessor the lines
    /// release replaces), and bucketed by tile; RoadsideOccupancy marks them
    /// as clear zone, so everything placed from the mask keeps off them.
    /// </summary>
    public static class RaceRunOff
    {
        /// <summary>Kept clear past the drawn edge, both sides, all along a
        /// route: about a car's length and a half beyond an arterial's clear
        /// zone.</summary>
        public const float RunOffM = 8f;
        /// <summary>Kept clear on the outside of a bend, past the drawn edge.</summary>
        public const float CornerRunOffM = 16f;
        /// <summary>A bend: the heading turns by this much over
        /// <see cref="BendSpanM"/> of route.</summary>
        public const float BendDeg = 12f;
        const float BendSpanM = 32f;
        const float StepM = 4f;

        public struct Mark { public Vector2 a, b; public float r; }

        static CityMap cachedFor;
        static CityMeshes.Trims cachedTrims;
        static Dictionary<long, List<Mark>> byTile;

        /// <summary>The run-off marks reaching into a tile; null if none.</summary>
        public static List<Mark> For(CityMap map, CityMeshes.Trims trims, int tx, int tz)
        {
            Ensure(map, trims);
            return byTile.TryGetValue(Key(tx, tz), out var l) ? l : null;
        }

        /// <summary>Is a plan point inside any route's run-off?</summary>
        public static bool Inside(CityMap map, CityMeshes.Trims trims, Vector2 p)
        {
            var l = For(map, trims, Mathf.FloorToInt(p.x / CityMeshes.TileSize), Mathf.FloorToInt(p.y / CityMeshes.TileSize));
            if (l == null) return false;
            foreach (var m in l) if (RoadsideOccupancy.DistToSeg(p, m.a, m.b) < m.r) return true;
            return false;
        }

        /// <summary>How many tiles the run-off reaches into.</summary>
        public static int Tiles(CityMap map, CityMeshes.Trims trims) { Ensure(map, trims); return byTile.Count; }

        static long Key(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        static readonly List<Vector2> pts = new List<Vector2>(4096);
        static readonly List<float> hws = new List<float>(4096);
        static readonly List<bool> tun = new List<bool>(4096);

        static void Ensure(CityMap map, CityMeshes.Trims trims)
        {
            if (byTile != null && cachedFor == map && cachedTrims == trims) return;
            cachedFor = map; cachedTrims = trims;
            byTile = new Dictionary<long, List<Mark>>();
            if (map == null || map.routes == null) return;
            foreach (var r in map.routes)
            {
                if (r.edges == null || r.edges.Length == 0) continue;
                // the route's centreline in plan every StepM, with its drawn half width
                pts.Clear(); hws.Clear(); tun.Clear();
                for (int k = 0; k < r.edges.Length; k++)
                {
                    var e = map.edges[r.edges[k]];
                    bool fwd = r.dirs[k] >= 0;
                    int n = Mathf.Max(1, Mathf.CeilToInt(e.length / StepM));
                    for (int i = pts.Count == 0 ? 0 : 1; i <= n; i++)
                    {
                        float s = e.length * i / n;
                        if (!fwd) s = e.length - s;
                        RoadsideOccupancy.RoadEdgeAt(e, trims, s, out var p, out _, out float hwL, out float hwR);
                        pts.Add(p); hws.Add(Mathf.Max(hwL, hwR)); tun.Add(e.tunnel);
                    }
                }
                int count = pts.Count, w = Mathf.Max(1, Mathf.RoundToInt(BendSpanM * 0.5f / StepM));
                int At(int i) => r.loop ? ((i % count) + count) % count : Mathf.Clamp(i, 0, count - 1);
                for (int i = 0; i + 1 < count; i++)
                {
                    if (tun[i] || tun[i + 1]) continue;
                    Vector2 a = pts[i], b = pts[i + 1];
                    var d = b - a;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    float hw = Mathf.Max(hws[i], hws[i + 1]);
                    Add(new Mark { a = a, b = b, r = hw + RunOffM });
                    // a bend: the heading into this step against the heading out of it
                    var d0 = pts[At(i)] - pts[At(i - w)];
                    var d1 = pts[At(i + 1 + w)] - pts[At(i + 1)];
                    if (d0.sqrMagnitude < 1e-4f || d1.sqrMagnitude < 1e-4f) continue;
                    float turn = Vector2.SignedAngle(d0, d1);
                    if (Mathf.Abs(turn) < BendDeg) continue;
                    d.Normalize();
                    var left = new Vector2(-d.y, d.x);
                    // a bend to the left runs a car out to the right
                    var outside = turn > 0f ? -left : left;
                    float half = CornerRunOffM * 0.5f;
                    var off = outside * (hw + half);
                    Add(new Mark { a = a + off, b = b + off, r = half });
                }
            }
        }

        static void Add(Mark m)
        {
            float ts = CityMeshes.TileSize;
            int x0 = Mathf.FloorToInt((Mathf.Min(m.a.x, m.b.x) - m.r) / ts), x1 = Mathf.FloorToInt((Mathf.Max(m.a.x, m.b.x) + m.r) / ts);
            int z0 = Mathf.FloorToInt((Mathf.Min(m.a.y, m.b.y) - m.r) / ts), z1 = Mathf.FloorToInt((Mathf.Max(m.a.y, m.b.y) + m.r) / ts);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long k = Key(x, z);
                    if (!byTile.TryGetValue(k, out var l)) byTile[k] = l = new List<Mark>();
                    l.Add(m);
                }
        }
    }
}
