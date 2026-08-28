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
            /// <summary>0 = procedural facade box (CityMeshes emits it).
            /// Anything else is a CityProps prefab kind — the mesh pass skips
            /// it and CityWorld instantiates the model instead.</summary>
            public byte kind;
        }

        public const float TileSize = 256f;

        static long TileKey(int tx, int tz) => ((long)tx << 24) ^ (tz & 0xFFFFFF);

        public static Dictionary<long, List<B>> Precompute(CityMap map)
        {
            var byTile = new Dictionary<long, List<B>>();
            var occupied = new HashSet<long>();      // 18 m occupancy cells
            var scratch = new HashSet<int>();
            int placed = 0;

            // Restaurants first, so their lots claim occupancy before the
            // frontage loop fills the street with houses.
            int landmarks = PlaceLandmarks(map, byTile, occupied, scratch);

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

                        // A slot can trade its procedural box for a real model:
                        // houses (and the odd trailer) own the outer suburbs,
                        // the pizzeria pack's mid-rises salt the shop streets.
                        byte kind = PickProp(e, distUp, e.index,
                            slot * 2 + (side + 1) / 2, ref bw, ref bd, ref bh);

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

                        // one building per 18 m occupancy cell; a real model
                        // claims every cell under its lot, because two houses
                        // from the SAME prefab intersecting reads as a glitch
                        // where two different procedural boxes just read dense
                        if (kind == 0)
                        {
                            long occ = (((long)Mathf.FloorToInt(c.x / 18f)) << 24) ^ (Mathf.FloorToInt(c.y / 18f) & 0xFFFFFF);
                            if (!occupied.Add(occ)) { at += step; continue; }
                        }
                        else if (!ClaimCells(occupied, c, bw, bd)) { at += step; continue; }

                        var b = new B
                        {
                            pos = c,
                            yaw = Mathf.Atan2(-nrm.x, -nrm.y),   // face back toward the road
                            w = bw, d = bd, h = bh, style = style, kind = kind,
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
            Debug.Log($"[City] buildings placed: {placed} (+{landmarks} restaurants)");
            return byTile;
        }

        /// <summary>
        /// A real model in a slot the procedural pass was about to fill. The
        /// footprint is swapped for the prefab's so every test downstream — the
        /// corridor check, the water check, the occupancy grid — measures the
        /// actual lot.
        /// </summary>
        static byte PickProp(CityMap.Edge e, float distUp, int a, int b,
                             ref float w, ref float d, ref float h)
        {
            byte kind = 0;
            if (distUp > 2600f && e.lanes < 4)
            {
                // the suburbs: half the frontage becomes real houses, and past
                // 5 km the odd lot is a trailer instead
                float r = Hash01(a, b, 15);
                if (distUp > 5000f && r < 0.15f)
                    kind = (byte)(CityProps.Trailer0 + (int)(Hash01(a, b, 17) * 2.999f));
                else if (r < 0.62f)
                    kind = CityProps.House;
            }
            else if (distUp > 900f && distUp < 2600f && e.lanes >= 4)
            {
                // shop streets in the midrise band: the pizzeria pack's blocks
                if (Hash01(a, b, 18) < 0.30f)
                    kind = (byte)(CityProps.Block0 + (int)(Hash01(a, b, 19) * 7.999f));
            }
            if (kind != 0 && CityProps.Defs.TryGetValue(kind, out var def))
            {
                w = def.w; d = def.d; h = def.h;
                return kind;
            }
            return 0;
        }

        /// <summary>Claim every 18 m occupancy cell under a w×d lot centred at
        /// c. All-or-nothing: on any collision nothing is claimed and the slot
        /// is skipped, so a model never interpenetrates a neighbour.</summary>
        static bool ClaimCells(HashSet<long> occupied, Vector2 c, float w, float d)
        {
            float half = Mathf.Max(w, d) * 0.5f;
            int x0 = Mathf.FloorToInt((c.x - half) / 18f), x1 = Mathf.FloorToInt((c.x + half) / 18f);
            int y0 = Mathf.FloorToInt((c.y - half) / 18f), y1 = Mathf.FloorToInt((c.y + half) / 18f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (occupied.Contains(((long)x << 24) ^ (y & 0xFFFFFF))) return false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    occupied.Add(((long)x << 24) ^ (y & 0xFFFFFF));
            return true;
        }

        /// <summary>
        /// The restaurants: a handful of drive-thrus and pizzerias on big
        /// surface streets around the belt, spaced out like a real chain map.
        /// Deterministic — the edge list is stable and every choice hashes off
        /// the edge index — so every player's Charlotte has the same food.
        /// </summary>
        static int PlaceLandmarks(CityMap map, Dictionary<long, List<B>> byTile,
                                  HashSet<long> occupied, HashSet<int> scratch)
        {
            var sites = new List<Vector2>();
            byte next = CityProps.Burger;
            const int Want = 10;

            foreach (var e in map.edges)
            {
                if (sites.Count >= Want) break;
                if (e.link || e.cls >= 5 || e.lanes < 4 || e.length < 90f) continue;

                float at = e.length * (0.35f + Hash01(e.index, 1, 22) * 0.3f);
                var p = e.PointAt(at);
                float distUp = Vector2.Distance(p, map.uptown);
                if (distUp < 1200f || distUp > 9500f) continue;
                if (Hash01(e.index, 0, 21) > 0.35f) continue;
                if (e.ElevatedAt(at)) continue;

                bool near = false;
                foreach (var s in sites)
                    if (Vector2.Distance(s, p) < 1500f) { near = true; break; }
                if (near) continue;

                var def = CityProps.Defs[next];
                int side = Hash01(e.index, 2, 23) < 0.5f ? -1 : 1;
                var tan = e.TangentAt(at);
                var nrm = new Vector2(-tan.y, tan.x) * side;
                float setback = e.width * 0.5f + 6f + def.d * 0.5f;
                var c = p + nrm * setback;

                // same corridor test the frontage loop applies, on the real lot
                scratch.Clear();
                float r = CityElevation.MaxCorridorHalf + def.w * 0.5f + 4f;
                map.EdgeSegsInRect(new Vector2(c.x - r, c.y - r), new Vector2(c.x + r, c.y + r), scratch);
                bool blocked = false;
                foreach (var packed in scratch)
                {
                    int oi = packed >> 12, si = packed & 0xFFF;
                    var o = map.edges[oi];
                    Vector2 a2 = o.pts[si], dseg = o.pts[si + 1] - a2;
                    float L2 = dseg.sqrMagnitude;
                    float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - a2, dseg) / L2) : 0f;
                    float dd = Vector2.Distance(c, a2 + dseg * t);
                    float need = o.width * 0.5f + 3.5f + Mathf.Max(def.w, def.d) * 0.55f;
                    if (dd < need && o != e) { blocked = true; break; }
                    // its own street only has to clear the lot's near edge
                    if (o == e && dd < o.width * 0.5f + 2f + def.d * 0.45f) { blocked = true; break; }
                }
                if (blocked) continue;

                scratch.Clear();
                map.WaterSegsInRect(new Vector2(c.x - 24f, c.y - 24f), new Vector2(c.x + 24f, c.y + 24f), scratch);
                if (scratch.Count > 0) continue;

                if (!ClaimCells(occupied, c, def.w, def.d)) continue;

                var b = new B
                {
                    pos = c,
                    yaw = Mathf.Atan2(-nrm.x, -nrm.y),
                    w = def.w, d = def.d, h = def.h, style = 3, kind = next,
                };
                int tx = Mathf.FloorToInt(c.x / TileSize), tz = Mathf.FloorToInt(c.y / TileSize);
                long key = TileKey(tx, tz);
                if (!byTile.TryGetValue(key, out var list)) byTile[key] = list = new List<B>(24);
                list.Add(b);
                sites.Add(p);
                next = next == CityProps.Burger ? CityProps.Pizzeria : CityProps.Burger;
            }
            return sites.Count;
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

        /// <summary>
        /// Ground height a prefab lot should SEAT at: the highest GroundY under
        /// its footprint (centre + four corners). A model cannot stretch its
        /// walls into a bank the way the procedural boxes do, so the first pass
        /// — seating on the CENTRE sample — buried whole ground floors where
        /// the corridor blend fell away from the road. The high corner wins and
        /// the baked foundation skirt covers whatever the low corner exposes.
        /// </summary>
        public static float SeatY(CityMap map, Vector2 pos, float w, float d, float yaw)
        {
            float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
            Vector2 fwd = new Vector2(sy, cy);
            Vector2 rgt = new Vector2(cy, -sy);
            Vector2 hw = rgt * (w * 0.5f);
            Vector2 hd = fwd * (d * 0.5f);
            float g = CityElevation.GroundY(map, pos.x, pos.y);
            foreach (var c in new[] { pos + hw + hd, pos - hw + hd, pos - hw - hd, pos + hw - hd })
                g = Mathf.Max(g, CityElevation.GroundY(map, c.x, c.y));
            return g;
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
