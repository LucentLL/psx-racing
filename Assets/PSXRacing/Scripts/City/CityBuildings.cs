using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// Where the city's buildings stand, decided once at load from road
    /// frontage — RG2 has no footprint data, so this is generation, not
    /// tracing (see Docs/CHARLOTTE.md). Deterministic by construction: every
    /// choice hashes off (edge index, slot index), so the same tile always
    /// builds the same street, on every device, in every session.
    ///
    /// The shape of the city comes from three dials against distance-to-
    /// uptown: towers inside ~900 m (the 277 loop), midrise to ~2.6 km,
    /// low suburbia thinning out toward the 485 belt.
    /// </summary>
    public static class CityBuildings
    {
        public struct B
        {
            public Vector2 pos;     // footprint centre
            public float yaw;       // radians, facing the road
            public float w, d, h;   // metres
            public byte style;      // material slot selector (see CityMeshes)
        }

        public const float TileSize = 256f;

        static long TileKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        public static Dictionary<long, List<B>> Precompute(CityMap map)
        {
            var byTile = new Dictionary<long, List<B>>();
            var occupied = new HashSet<long>();      // 18 m occupancy cells
            var scratch = new HashSet<int>();
            int placed = 0;

            foreach (var e in map.edges)
            {
                if (e.link) continue;                       // no frontage on a ramp
                if (e.cls >= 5) continue;                   // or on a freeway mainline
                if (e.length < 30f) continue;

                for (int side = -1; side <= 1; side += 2)
                {
                    float at = 14f + Hash01(e.index, side + 7, 1) * 12f;
                    int slot = 0;
                    while (at < e.length - 14f)
                    {
                        slot++;
                        var p = e.PointAt(at);
                        float distUp = Vector2.Distance(p, map.uptown);

                        float bw, bd, bh;
                        byte style;
                        Pick(e, distUp, e.index, slot * 2 + (side + 1) / 2,
                             out bw, out bd, out bh, out style, out float keepP);

                        float step = bw + 6f + Hash01(e.index, slot, 3) * 10f;

                        if (Hash01(e.index, slot, 4) > keepP) { at += step; continue; }
                        if (e.ElevatedAt(at)) { at += step; continue; }

                        var tan = e.TangentAt(at);
                        var nrm = new Vector2(-tan.y, tan.x) * side;
                        float setback = e.width * 0.5f + 4f + bd * 0.5f
                                      + Hash01(e.index, slot, 5) * 5f;
                        var c = p + nrm * setback;

                        // never inside another road's corridor
                        scratch.Clear();
                        float r = CityElevation.MaxCorridorHalf + bw * 0.5f + 4f;
                        map.EdgeSegsInRect(new Vector2(c.x - r, c.y - r), new Vector2(c.x + r, c.y + r), scratch);
                        bool blocked = false;
                        foreach (var packed in scratch)
                        {
                            int oi = packed >> 12, si = packed & 0xFFF;
                            var o = map.edges[oi];
                            Vector2 a = o.pts[si], dseg = o.pts[si + 1] - a;
                            float L2 = dseg.sqrMagnitude;
                            float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a, dseg) / L2) : 0f;
                            float dd = Vector2.Distance(c, a + dseg * t);
                            float need = o.width * 0.5f + 3.5f + Mathf.Max(bw, bd) * 0.55f;
                            if (dd < need) { blocked = true; break; }
                        }
                        if (blocked) { at += step; continue; }

                        // not in the water
                        scratch.Clear();
                        map.WaterSegsInRect(new Vector2(c.x - 18f, c.y - 18f), new Vector2(c.x + 18f, c.y + 18f), scratch);
                        if (scratch.Count > 0) { at += step; continue; }

                        // one building per 18 m occupancy cell
                        long occ = (((long)Mathf.FloorToInt(c.x / 18f)) << 24) ^ (Mathf.FloorToInt(c.y / 18f) & 0xFFFFFF);
                        if (!occupied.Add(occ)) { at += step; continue; }

                        var b = new B
                        {
                            pos = c,
                            yaw = Mathf.Atan2(-nrm.x, -nrm.y),   // face back toward the road
                            w = bw, d = bd, h = bh, style = style,
                        };
                        int tx = Mathf.FloorToInt(c.x / TileSize), tz = Mathf.FloorToInt(c.y / TileSize);
                        long key = TileKey(tx, tz);
                        if (!byTile.TryGetValue(key, out var list)) byTile[key] = list = new List<B>(24);
                        list.Add(b);
                        placed++;

                        at += step;
                    }
                }
            }
            Debug.Log($"[City] buildings placed: {placed}");
            return byTile;
        }

        static void Pick(CityMap.Edge e, float distUp, int a, int b,
            out float w, out float d, out float h, out byte style, out float keepP)
        {
            float r1 = Hash01(a, b, 11), r2 = Hash01(a, b, 12), r3 = Hash01(a, b, 13);
            if (distUp < 900f)
            {
                // uptown: towers, denser on bigger streets
                w = 16f + r1 * 14f; d = 14f + r2 * 12f;
                float t = r3 * r3;                     // most towers modest, a few tall
                h = 22f + t * 95f;
                style = (byte)(r1 < 0.55f ? 0 : 1);    // glass / mid facade
                keepP = 0.92f;
            }
            else if (distUp < 2600f)
            {
                w = 12f + r1 * 12f; d = 10f + r2 * 8f;
                h = 7f + r3 * r3 * 22f;
                style = (byte)(r2 < 0.5f ? 1 : 2);     // mid / brick
                keepP = 0.8f;
            }
            else
            {
                w = 9f + r1 * 9f; d = 7f + r2 * 6f;
                h = 3.6f + r3 * 4.5f;
                style = (byte)(r1 < 0.25f ? 2 : 3);    // brick / shop-street low
                keepP = Mathf.Clamp01(1.25f - distUp / 9000f) * (e.lanes >= 4 ? 0.75f : 0.55f);
            }
            // ground-floor retail takes over on big surface streets in the core
            if (distUp < 1800f && e.lanes >= 4 && h < 26f && Hash01(a, b, 14) < 0.5f)
                style = 3;
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
