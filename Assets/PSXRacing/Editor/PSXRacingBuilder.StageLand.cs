using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE LAND AROUND A STAGE'S TOWNS, from its world file (round 2 of Lake
    /// Lure, owner's 2022 video): what the forest must leave open and what
    /// stands there instead.
    ///
    ///   * OPEN GROUND (PSX_LURE_OPEN): parks, pitches, playgrounds, the beach,
    ///     the marina and the retail land of the 2024-09-20 map; yards round
    ///     every building; the lawn between each town building and its road;
    ///     a band either side of the two town streets. No tree is planted
    ///     there (keep-out bit 8), and the ground wears a mown lawn (the beach
    ///     its sand) instead of the bare forest floor. At 8:15 in the video
    ///     the Lake Lure Inn stands across an open lawn; round 1 hid it in
    ///     the forest.
    ///   * THE GRANITE (PSX_LURE_GRANITE): bare grey rock where the 2018 lidar
    ///     stands steeper than 52 degrees over 18 m of relief - Chimney
    ///     Rock's cliffs, Rumbling Bald, the gorge walls - in the Meadow
    ///     pack's photoscanned cliffs, faced down the fall line; no tree on
    ///     them. Never on a forested slope: the rule is the rock's own
    ///     steepness, read from the lidar by bake_world.mjs.
    ///   * THE MARINA (PSX_LURE_MARINA): the docks and finger piers of the
    ///     2024 map as plank walks on posts over the lake.
    ///   * GRASS BANKS (PSX_LURE_GRASSBANK): on the lake road and through the
    ///     towns the low cuts beside the road are grass, as in the video
    ///     (7:43, 10:05), not rock.
    /// </summary>
    public static partial class PSXRacingBuilder
    {
        [Serializable] class WorldOpen { public string kind; public float[] xz; }
        [Serializable] class WorldPier { public float[] xz; public int area; }

        const byte KeepOpen = 8, KeepPark = 32;
        /// <summary>PARKLAND (bit 32, the town streets' bands): lawn with the
        /// odd grove left standing - one 15 m block in five keeps its trees
        /// (the video's big street trees), the rest is open.</summary>
        static bool ParkGrove(float x, float z)
        {
            int bx = Mathf.FloorToInt(x / 15f), bz = Mathf.FloorToInt(z / 15f);
            return Hash(bx * 15.37f, bz * 15.73f) % 5 == 0;
        }
        /// <summary>The granite's 12 m cells (grown one cell), far terrain too:
        /// no tree stands on the rock.</summary>
        static HashSet<long> worldCliffKeep;
        const float CliffCell = 12f;
        static long CliffKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        static bool CliffKeepAt(float x, float z) =>
            worldCliffKeep != null && worldCliffKeep.Contains(CliffKey(Mathf.FloorToInt(x / CliffCell), Mathf.FloorToInt(z / CliffCell)));

        static byte WorldKeepBits(float x, float z)
        {
            if (worldKeep == null) return 0;
            int c = (int)((x - worldKeepX0) / WorldKeepCell), r = (int)((z - worldKeepZ0) / WorldKeepCell);
            if (c < 0 || r < 0 || c >= worldKeepCols || r >= worldKeepRows) return 0;
            return worldKeep[r * worldKeepCols + c];
        }

        // ---------------------------------------------------- mouth notches
        /// <summary>
        /// THE MOUTH NOTCH (PSX_LURE_MOUTHGAP), from StageWorldLoad before the
        /// roadside is planned: the stage builder plans its cuts without the
        /// side roads, so a cut bank ran straight across 25 of the mapped
        /// mouths and round 1 had to leave them out. Here the near grid is
        /// LOWERED (never raised) along each side road for its first 24 m past
        /// the tarmac, to the side road's own lidar profile eased in from the
        /// road's edge - the notch a real side road cuts through the bank -
        /// so the planner leaves the mouth open and the bank ends either side
        /// of it. Not on the town streets (their own bench), not on a bridge.
        /// </summary>
        static int WorldMouthBench()
        {
            var pts = track.stagePts;
            if (world.roads == null || pts == null || demNear == null || !WorldSwitch("PSX_LURE_MOUTHGAP")) return 0;
            var m = demNearMeta;
            float halfW = RoadWidth * 0.5f;
            int lowered = 0, mouths = 0;
            foreach (var r in world.roads)
            {
                if (r.xz == null || r.xz.Length < 6 || r.y == null || r.y.Length * 2 != r.xz.Length) continue;
                if (WorldOnStreet(r.st)) continue;
                if (world.parkRoad != null && Mathf.Abs(r.st - world.parkRoad.mouthSt) < 6f && r.cls == "service") continue;
                int st = Mathf.Clamp(Mathf.RoundToInt(r.st / Spacing), 0, pts.Length - 1);
                float roadY = pts[st].y - 0.12f;
                var line = WorldPts(r.xz);
                for (int i = 0; i < line.Length; i++) line[i].y = r.y[i];
                // its length along, from the first point (on or by the centreline)
                var acc = new float[line.Length];
                for (int i = 1; i < line.Length; i++) acc[i] = acc[i - 1] + Vector2.Distance(new Vector2(line[i].x, line[i].z), new Vector2(line[i - 1].x, line[i - 1].z));
                float reach = halfW + 24f, hw = Mathf.Max(r.w * 0.5f + 2f, 7f), ease = 6f;
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                for (int i = 0; i < line.Length && acc[i] <= reach + 12f; i++) { x0 = Mathf.Min(x0, line[i].x); x1 = Mathf.Max(x1, line[i].x); z0 = Mathf.Min(z0, line[i].z); z1 = Mathf.Max(z1, line[i].z); }
                int c0 = Mathf.Max(0, (int)((x0 - hw - ease - m.originX) / m.cell)), c1 = Mathf.Min(m.cols - 1, (int)((x1 + hw + ease - m.originX) / m.cell) + 1);
                int r0 = Mathf.Max(0, (int)((z0 - hw - ease - m.originZ) / m.cell)), r1 = Mathf.Min(m.rows - 1, (int)((z1 + hw + ease - m.originZ) / m.cell) + 1);
                bool any = false;
                for (int rr = r0; rr <= r1; rr++)
                    for (int cc = c0; cc <= c1; cc++)
                    {
                        float x = m.originX + cc * m.cell, z = m.originZ + rr * m.cell;
                        // the road's own bed is the road's
                        if (StageCorridor(x, z, halfW + 2.5f, out RoadFoot rf) && rf.d < halfW + 2.5f) continue;
                        // nearest point on the side road's first stretch
                        float best = float.MaxValue, along = 0f, sideY = roadY;
                        for (int i = 0; i + 1 < line.Length && acc[i] <= reach + 12f; i++)
                        {
                            float dx = line[i + 1].x - line[i].x, dz = line[i + 1].z - line[i].z, L2 = dx * dx + dz * dz;
                            float t = L2 > 1e-6f ? Mathf.Clamp01(((x - line[i].x) * dx + (z - line[i].z) * dz) / L2) : 0f;
                            float ex = line[i].x + dx * t - x, ez = line[i].z + dz * t - z, d2 = ex * ex + ez * ez;
                            if (d2 < best) { best = d2; along = acc[i] + t * Mathf.Sqrt(L2); sideY = Mathf.Lerp(line[i].y, line[i + 1].y, t); }
                        }
                        float perp = Mathf.Sqrt(best);
                        if (perp > hw + ease || along > reach) continue;
                        // from the road's edge height onto the side road's own profile over the first 16 m
                        float target = Mathf.Lerp(roadY, sideY, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfW, halfW + 16f, along)));
                        int k = rr * m.cols + cc;
                        float have = demNear[k] * 0.1f;
                        if (have <= target + 0.05f) continue;
                        float keep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hw, hw + ease, perp));
                        // the notch eases out at its far end too
                        keep = Mathf.Max(keep, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach - 6f, reach, along)));
                        short want = (short)Mathf.RoundToInt((target + (have - target) * keep) * 10f);
                        if (want < demNear[k]) { demNear[k] = want; lowered++; any = true; }
                    }
                if (any) mouths++;
            }
            Log($"Stage world mouth notches: {mouths} side roads notched through the bank, {lowered} near cells lowered to their profile (PSX_LURE_MOUTHGAP).");
            return lowered;
        }

        /// <summary>
        /// A MOUTH IN A MAPPED RAIL (PSX_LURE_MOUTHGAP), from the roadside plan:
        /// the map's guardrail ways run unbroken past ten side roads, so the
        /// planner railed them shut and round 1 left those mouths out. A real
        /// rail stops before a side road. Where every station the mouth needs
        /// (its half width and one more each way, as the mouth builder asks)
        /// is warranted by the mapped rail alone (reason 8: no critical fall,
        /// no water, no deck, no approach), those stations are left unwalled.
        /// Lake Lure's own frame only.
        /// </summary>
        internal static void LureMouthGaps(int s, bool[] want, byte[] reason)
        {
            if (world == null || world.roads == null || world.stage != StagePrefix || !WorldSwitch("PSX_LURE_MOUTHGAP") || !WorldSwitch("PSX_LURE_MOUTHS")) return;
            int n = want.Length, cleared = 0, mouths = 0;
            foreach (var r in world.roads)
            {
                if (r.xz == null || r.xz.Length < 6) continue;
                if (world.parkRoad != null && Mathf.Abs(r.st - world.parkRoad.mouthSt) < 6f && r.cls == "service") continue;
                if (SideIx(-r.side) != s) continue;   // the bake's +1 is LEFT; the builder's +1 is the right-hand side
                int st = Mathf.RoundToInt(r.st / Spacing), reach = Mathf.CeilToInt(r.w * 0.5f / Spacing) + 1;
                bool any = false, railOnly = true;
                for (int k = st - reach; k <= st + reach; k++)
                    if (k >= 0 && k < n && want[k]) { any = true; if (reason[k] != 8) { railOnly = false; break; } }
                if (!any || !railOnly) continue;
                for (int k = st - reach; k <= st + reach; k++) if (k >= 0 && k < n && want[k]) { want[k] = false; cleared++; }
                mouths++;
            }
            if (mouths > 0) Log($"Stage world mouth gaps ({(s == 0 ? "left" : "right")}): {mouths} side roads through a mapped rail, {cleared} stations left unwalled for them (PSX_LURE_MOUTHGAP).");
        }

        // ------------------------------------------------------- keep-out
        /// <summary>From BuildWorldKeepOut: the open ground (bit 8) and the
        /// granite's cells.</summary>
        static void WorldLandKeepOut(byte[] mask)
        {
            worldCliffKeep = null;
            if (WorldSwitch("PSX_LURE_GRANITE") && world.cliffs != null && world.cliffs.Length >= 6)
            {
                worldCliffKeep = new HashSet<long>();
                for (int k = 0; k + 5 < world.cliffs.Length; k += 6)
                {
                    int cx = Mathf.FloorToInt(world.cliffs[k] / CliffCell), cz = Mathf.FloorToInt(world.cliffs[k + 1] / CliffCell);
                    for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) worldCliffKeep.Add(CliffKey(cx + dx, cz + dz));
                }
            }
            if (!WorldSwitch("PSX_LURE_OPEN")) return;
            bool own = world.stage == StagePrefix;
            int areas = 0, yards = 0, lawnsBefore = 0;
            if (world.open != null)
                foreach (var o in world.open)
                    if (o.xz != null && o.xz.Length >= 6)
                    {
                        // the marina's land keeps its big shore trees (the video at 8:45): parkland, not a lawn
                        WorldFill(mask, new List<Vector3[]> { WorldPts(o.xz) }, o.kind == "marina" ? KeepPark : KeepOpen, 0);
                        areas++;
                    }
            if (world.buildings != null)
                foreach (var b in world.buildings)
                {
                    if (b.xz == null || b.xz.Length < 6) continue;
                    string zone = b.zone ?? "lake";
                    bool town = zone == "downtown" || zone == "village" || zone == "inlet" || zone == "batcave";
                    // a yard round it: 12 m in a town, 6 m by the lake road and in the gorge
                    WorldFill(mask, new List<Vector3[]> { WorldPts(b.xz) }, KeepOpen, town ? 4 : 2);
                    yards++;
                    // the lawn between a town building and the road it fronts (the inn across its lawn)
                    if (!town || b.dist > 120f || (b.fx * b.fx + b.fz * b.fz) < 0.25f) continue;
                    var ring = WorldPts(b.xz);
                    Vector3 cen = Vector3.zero; foreach (var p in ring) cen += p; cen /= ring.Length;
                    Vector3 dir = new Vector3(b.fx, 0f, b.fz).normalized, across = new Vector3(-dir.z, 0f, dir.x);
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var p in ring) { float u = Vector3.Dot(p - cen, across); lo = Mathf.Min(lo, u); hi = Mathf.Max(hi, u); }
                    float half = Mathf.Clamp((hi - lo) * 0.5f + 4f, 6f, 34f);
                    Vector3 c0 = cen + across * ((lo + hi) * 0.5f);
                    WorldBand(mask, new[] { c0, c0 + dir * b.dist }, half, KeepOpen);
                    lawnsBefore++;
                }
            // the town streets themselves (parkland): 36 m either side downtown, 18 m in the villages - and only
            // where the town is, within 60 m along the road of a building that fronts it (the village's wooded
            // outskirts keep their trees, as at 10:05)
            if (own)
            {
                var pts = track.stagePts;
                var fronted = new bool[pts.Length];
                if (world.buildings != null)
                    foreach (var b in world.buildings)
                    {
                        if (b.dist > 80f) continue;
                        int c = Mathf.RoundToInt(b.st / Spacing), reach = Mathf.CeilToInt(60f / Spacing);
                        for (int i = Mathf.Max(0, c - reach); i <= Mathf.Min(pts.Length - 1, c + reach); i++) fronted[i] = true;
                    }
                foreach (var zr in WorldStreets)
                {
                    int i0 = Mathf.Max(0, (int)(zr.x / Spacing)), i1 = Mathf.Min(pts.Length - 1, (int)(zr.y / Spacing));
                    var line = new List<Vector3>();
                    for (int i = i0; i <= i1 + 1; i++)
                    {
                        if (i <= i1 && fronted[i]) { line.Add(pts[i]); continue; }
                        if (line.Count >= 2) WorldBand(mask, line.ToArray(), zr.x < 6000f ? 36f : 18f, KeepPark);
                        line.Clear();
                    }
                }
            }
            else if (world.streets != null)
                foreach (var r in world.streets) if (r.xz != null && r.xz.Length >= 4) WorldBand(mask, WorldPts(r.xz), 18f, KeepPark);
            int n8 = 0; foreach (var v in mask) if ((v & (KeepOpen | KeepPark)) != 0) n8++;
            Log($"Stage world open ground: {areas} mapped areas, {yards} yards, {lawnsBefore} lawns before town buildings, the town streets' bands - " +
                $"{n8 * WorldKeepCell * WorldKeepCell / 1e6f:0.000} km2 the forest leaves open; granite keep-out {(worldCliffKeep?.Count ?? 0)} cells of 12 m.");
        }

        // ----------------------------------------------------------- build
        /// <summary>From BuildStageWorld, after the town: lawns and the beach,
        /// the granite, the marina's piers.</summary>
        static void BuildWorldLand(List<Vector3> pts, Transform parent)
        {
            Physics.SyncTransforms();
            if (WorldSwitch("PSX_LURE_OPEN")) BuildWorldLawns(parent);
            if (WorldSwitch("PSX_LURE_GRANITE")) BuildWorldGranite(parent);
            if (WorldSwitch("PSX_LURE_MARINA") && world.stage == StagePrefix) BuildWorldPiers(parent);
        }

        // ------------------------------------------------------------ lawns
        static void BuildWorldLawns(Transform parent)
        {
            if (worldKeep == null) return;
            // the beach's cells (sand, not grass)
            var beach = new byte[worldKeep.Length];
            if (world.open != null)
                foreach (var o in world.open)
                    if (o.kind == "beach" && o.xz != null && o.xz.Length >= 6) WorldFill(beach, new List<Vector3[]> { WorldPts(o.xz) }, 1, 0);
            float halfW = RoadWidth * 0.5f;
            bool own = world.stage == StagePrefix;
            int cols1 = worldKeepCols + 1;
            var cornerY = new Dictionary<int, float>();
            float CornerY(int c, int r)
            {
                int key = r * cols1 + c;
                if (!cornerY.TryGetValue(key, out float y))
                {
                    float x = worldKeepX0 + c * WorldKeepCell, z = worldKeepZ0 + r * WorldKeepCell;
                    cornerY[key] = y = DrapeY(x, z);
                }
                return y;
            }
            const float Tile = 240f;
            var bufs = new Dictionary<long, TownBuf>();
            int grass = 0, sand = 0, steep = 0;
            var lawn = new byte[worldKeep.Length];
            for (int r = 0; r < worldKeepRows; r++)
                for (int c = 0; c < worldKeepCols; c++)
                {
                    int idx = r * worldKeepCols + c;
                    byte v = worldKeep[idx];
                    if ((v & (KeepOpen | KeepPark)) == 0 || (v & 3) != 0) continue;
                    float cxw = worldKeepX0 + (c + 0.5f) * WorldKeepCell, czw = worldKeepZ0 + (r + 0.5f) * WorldKeepCell;
                    if ((v & 4) != 0 && (own || !NearStreet(cxw, czw, 1.6f))) continue;
                    float x = worldKeepX0 + (c + 0.5f) * WorldKeepCell, z = worldKeepZ0 + (r + 0.5f) * WorldKeepCell;
                    if (CliffKeepAt(x, z)) continue;
                    // a cell clear of the water and of side roads and lots (their decals' edges); in another stage's
                    // frame Main Street's own band does not count - the lawn comes to 1.6 m off its asphalt, under
                    // the sidewalks (round 1 left 4.5 m of bare ground there: the coordinator's 'sandy ground')
                    bool edge = false;
                    for (int dr = -1; dr <= 1 && !edge; dr++)
                        for (int dc = -1; dc <= 1 && !edge; dc++)
                        {
                            int r2 = r + dr, c2 = c + dc;
                            if (r2 < 0 || c2 < 0 || r2 >= worldKeepRows || c2 >= worldKeepCols) continue;
                            byte nv = worldKeep[r2 * worldKeepCols + c2];
                            if ((nv & 1) != 0) edge = true;
                            else if ((nv & 4) != 0 && (own || !NearStreet(worldKeepX0 + (c2 + 0.5f) * WorldKeepCell, worldKeepZ0 + (r2 + 0.5f) * WorldKeepCell, 1.6f))) edge = true;
                        }
                    if (edge) continue;
                    if (!own && NearStreet(x, z, 3.1f)) continue;
                    // off the tarmac, its verge strip and the town's sidewalks
                    if (StageCorridor(x, z, halfW + 6f, out RoadFoot f))
                    {
                        float m = f.station * Spacing;
                        bool walk = own && ((m > 5080f && m < 6020f) || (m > 6930f && m < 8020f));
                        if (f.d < halfW + (walk ? 4.4f : 3.2f)) continue;
                        if (BridgeAt(f.station) > 0f) continue;
                    }
                    float y00 = CornerY(c, r), y10 = CornerY(c + 1, r), y11 = CornerY(c + 1, r + 1), y01 = CornerY(c, r + 1);
                    float lo = Mathf.Min(Mathf.Min(y00, y10), Mathf.Min(y11, y01)), hi = Mathf.Max(Mathf.Max(y00, y10), Mathf.Max(y11, y01));
                    if (hi - lo > 2.2f) { steep++; continue; }   // a lawn on a 36-degree bank is not a lawn
                    lawn[idx] = (byte)(beach[idx] != 0 ? 2 : 1);
                }
            // the cells, the staircase's outer corners cut on the diagonal (a lawn's edge is not a 3 m staircase)
            bool Lawn(int c2, int r2) => c2 >= 0 && r2 >= 0 && c2 < worldKeepCols && r2 < worldKeepRows && lawn[r2 * worldKeepCols + c2] != 0;
            for (int r = 0; r < worldKeepRows; r++)
                for (int c = 0; c < worldKeepCols; c++)
                {
                    int idx = r * worldKeepCols + c;
                    if (lawn[idx] == 0) continue;
                    float x = worldKeepX0 + (c + 0.5f) * WorldKeepCell, z = worldKeepZ0 + (r + 0.5f) * WorldKeepCell;
                    float y00 = CornerY(c, r), y10 = CornerY(c + 1, r), y11 = CornerY(c + 1, r + 1), y01 = CornerY(c, r + 1);
                    long tk = ((long)Mathf.FloorToInt(x / Tile) << 32) ^ (uint)Mathf.FloorToInt(z / Tile);
                    if (!bufs.TryGetValue(tk, out var buf)) bufs[tk] = buf = new TownBuf();
                    float x0 = x - WorldKeepCell * 0.5f, z0 = z - WorldKeepCell * 0.5f, x1 = x0 + WorldKeepCell, z1 = z0 + WorldKeepCell;
                    const float lift = 0.05f;
                    var a = new Vector3(x0, y00 + lift, z0); var b2 = new Vector3(x0, y01 + lift, z1);
                    var cc = new Vector3(x1, y11 + lift, z1); var d = new Vector3(x1, y10 + lift, z0);
                    bool isSand = lawn[idx] == 2;
                    string k = isSand ? "sand" : "grass";
                    Vector2 ua = new Vector2(x0 / 4f, z0 / 4f), ub = new Vector2(x0 / 4f, z1 / 4f), uc = new Vector2(x1 / 4f, z1 / 4f), ud = new Vector2(x1 / 4f, z0 / 4f);
                    bool nN = !Lawn(c, r + 1), nS = !Lawn(c, r - 1), nE = !Lawn(c + 1, r), nW = !Lawn(c - 1, r);
                    int open = (nN ? 1 : 0) + (nS ? 1 : 0) + (nE ? 1 : 0) + (nW ? 1 : 0);
                    // triangles counter-clockwise seen from above (facing up)
                    if (open == 2 && nN && nE) buf.Tri(k, a, d, b2, ua, ud, ub);          // keep the south-west half
                    else if (open == 2 && nN && nW) buf.Tri(k, a, d, cc, ua, ud, uc);     // south-east
                    else if (open == 2 && nS && nE) buf.Tri(k, a, cc, b2, ua, uc, ub);    // north-west
                    else if (open == 2 && nS && nW) buf.Tri(k, d, cc, b2, ud, uc, ub);    // north-east
                    // facing up: (x0,z0) -> (x1,z0) -> (x1,z1) -> (x0,z1) runs counter-clockwise seen from above
                    else buf.Quad(k, a, d, cc, b2, ua, ud, uc, ub);
                    if (isSand) sand++; else grass++;
                }
            int n = 0;
            foreach (var kv in bufs) { TownEmit(kv.Value, "Lawn_" + n, parent); n++; }
            Log($"Stage world lawns: {grass * WorldKeepCell * WorldKeepCell / 1e4f:0.00} ha of mown grass and {sand * WorldKeepCell * WorldKeepCell / 1e4f:0.00} ha of beach sand " +
                $"on the open ground ({n} tiles); {steep} cells too steep for a lawn left to the ground.");
        }

        /// <summary>Within the given margin of a world street's edge (another stage's frame: Main Street).</summary>
        static bool NearStreet(float x, float z, float margin)
        {
            if (world.streets == null) return false;
            foreach (var st in world.streets)
            {
                if (st.xz == null) continue;
                float lim = st.w * 0.5f + margin;
                for (int i = 0; i + 3 < st.xz.Length; i += 2)
                {
                    float ax = st.xz[i], az = st.xz[i + 1], bx = st.xz[i + 2], bz = st.xz[i + 3];
                    float dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
                    float t = L2 > 1e-6f ? Mathf.Clamp01(((x - ax) * dx + (z - az) * dz) / L2) : 0f;
                    float ex = ax + dx * t - x, ez = az + dz * t - z;
                    if (ex * ex + ez * ez < lim * lim) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------ Main Street
        /// <summary>
        /// MAIN STREET in another stage's frame (the Park Road stage starts
        /// from it): round 1 drew it as bare asphalt over sandy ground. Now
        /// it carries the route's own paint - a double yellow centre line and
        /// white edge lines - and concrete sidewalks before its buildings, as
        /// the village street of the Lake Lure stage (PSX_LURE_MAINST).
        /// </summary>
        static void StreetPaintAndWalks(WorldRoad st, Vector3[] line, List<Vector3> sv, Transform parent, int no)
        {
            int rows = line.Length;
            if (sv.Count < rows * 5) return;
            float hw = st.w * 0.5f;
            var paint = new TownBuf(); var walks = new TownBuf();
            var nrm = new Vector3[rows];
            for (int i = 0; i < rows; i++)
            {
                Vector3 a = line[Mathf.Max(0, i - 1)], b = line[Mathf.Min(rows - 1, i + 1)];
                Vector3 t = b - a; t.y = 0; t.Normalize(); nrm[i] = new Vector3(-t.z, 0, t.x);
            }
            // the surface's height at an offset across a row (its five vertices span -hw .. +hw)
            float Yat(int i, float o)
            {
                float f = Mathf.Clamp((o / hw + 1f) * 2f, 0f, 3.999f);
                int k = (int)f;
                return Mathf.Lerp(sv[i * 5 + k].y, sv[i * 5 + k + 1].y, f - k);
            }
            Vector3 At(int i, float o, float lift)
            {
                var p = line[i] + nrm[i] * o;
                p.y = Yat(i, Mathf.Clamp(o, -hw, hw)) + lift;
                return p;
            }
            void Strip(TownBuf b, string k, int i, float o0, float o1, float lift)
            {
                Vector3 A = At(i - 1, o0, lift), B = At(i - 1, o1, lift), C = At(i, o1, lift), D = At(i, o0, lift);
                float L = Vector3.Distance(A, D);
                b.QuadR(k, A, B, C, D, Vector2.zero, new Vector2(1, 0), new Vector2(1, L), new Vector2(0, L));
            }
            // where buildings front it
            var cents = new List<Vector2>();
            if (world.buildings != null)
                foreach (var bd in world.buildings)
                {
                    if (bd.xz == null || bd.xz.Length < 6) continue;
                    float cx = 0, cz = 0; int m = bd.xz.Length / 2;
                    for (int k = 0; k < m; k++) { cx += bd.xz[2 * k]; cz += bd.xz[2 * k + 1]; }
                    cents.Add(new Vector2(cx / m, cz / m));
                }
            bool Fronted(Vector3 p) { foreach (var c in cents) if ((c.x - p.x) * (c.x - p.x) + (c.y - p.z) * (c.y - p.z) < 900f) return true; return false; }
            int walked = 0;
            for (int i = 1; i < rows; i++)
            {
                Strip(paint, "yellow", i, -0.17f, -0.07f, 0.012f);
                Strip(paint, "yellow", i, 0.07f, 0.17f, 0.012f);
                Strip(paint, "paint", i, -hw + 0.25f, -hw + 0.37f, 0.012f);
                Strip(paint, "paint", i, hw - 0.37f, hw - 0.25f, 0.012f);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 q0 = line[i - 1] + nrm[i - 1] * (sgn * (hw + 2.6f)), q1 = line[i] + nrm[i] * (sgn * (hw + 2.6f));
                    if (!Fronted(q0) || (WorldKeepBits(q0.x, q0.z) & 5) != 0 || (WorldKeepBits(q1.x, q1.z) & 5) != 0) continue;
                    // the walk: from the street's edge out 1.8 m, draped on the ground, flush
                    Vector3 A = line[i - 1] + nrm[i - 1] * (sgn * hw), D = line[i] + nrm[i] * (sgn * hw);
                    Vector3 B = line[i - 1] + nrm[i - 1] * (sgn * (hw + 1.8f)), C = line[i] + nrm[i] * (sgn * (hw + 1.8f));
                    A.y = Yat(i - 1, sgn * hw) + 0.03f; D.y = Yat(i, sgn * hw) + 0.03f;
                    B.y = DrapeY(B.x, B.z) + 0.06f; C.y = DrapeY(C.x, C.z) + 0.06f;
                    if (Mathf.Abs(B.y - A.y) > 0.5f || Mathf.Abs(C.y - D.y) > 0.5f) continue;
                    B.y = Mathf.Max(B.y, A.y); C.y = Mathf.Max(C.y, D.y);
                    if (sgn > 0f) walks.QuadR("concrete", A, B, C, D, new Vector2(A.x / 2f, A.z / 2f), new Vector2(B.x / 2f, B.z / 2f), new Vector2(C.x / 2f, C.z / 2f), new Vector2(D.x / 2f, D.z / 2f));
                    else walks.Quad("concrete", A, B, C, D, new Vector2(A.x / 2f, A.z / 2f), new Vector2(B.x / 2f, B.z / 2f), new Vector2(C.x / 2f, C.z / 2f), new Vector2(D.x / 2f, D.z / 2f));
                    walked++;
                }
            }
            TownEmit(paint, "StreetPaint_" + no, parent);
            TownEmit(walks, "StreetWalks_" + no, parent);
            Log($"Stage world Main Street {no}: double yellow and edge lines over {rows} rows, {walked} sidewalk panels before its buildings.");
        }

        /// <summary>The porch's wall (FORMS): the straight run of walls (edges
        /// within 6 degrees of one another) that faces the road best, its
        /// length weighted by the cube of how squarely it faces - the inn's
        /// whole long front, a shop's gable end rather than its long side.
        /// Returns the run's first edge (-1: none faces the road).</summary>
        static int PorchFront(List<Vector3> ring, Vector3 toRoad, out int lastEdge)
        {
            int n = ring.Count, best = -1; lastEdge = -1;
            float bestScore = 0f;
            for (int i0 = 0; i0 < n; i0++)
            {
                Vector3 e0 = ring[(i0 + 1) % n] - ring[i0]; e0.y = 0f;
                if (e0.sqrMagnitude < 0.09f) continue;
                e0.Normalize();
                // only a run's first edge starts a run
                Vector3 ep = ring[i0] - ring[(i0 + n - 1) % n]; ep.y = 0f;
                if (ep.sqrMagnitude > 0.09f && Vector3.Dot(ep.normalized, e0) >= 0.995f) continue;
                int i1 = i0;
                for (int k = 1; k < n; k++)
                {
                    int ii = (i0 + k) % n;
                    Vector3 e = ring[(ii + 1) % n] - ring[ii]; e.y = 0f;
                    if (e.sqrMagnitude < 1e-4f) { i1 = ii; continue; }
                    if (Vector3.Dot(e.normalized, e0) < 0.995f) break;
                    i1 = ii;
                }
                Vector3 chord = ring[(i1 + 1) % n] - ring[i0]; chord.y = 0f;
                float cl = chord.magnitude;
                if (cl < 4f) continue;
                Vector3 dir = chord / cl, outN = new Vector3(-dir.z, 0f, dir.x);
                float dot = Vector3.Dot(outN, toRoad);
                if (dot <= 0.35f) continue;   // as round 1: a wall turned up to 69 degrees from the road still takes the porch
                float score = cl * dot * dot * dot;
                if (score > bestScore) { bestScore = score; best = i0; lastEdge = i1; }
            }
            return best;
        }

        // ------------------------------------------------------------- roofs
        /// <summary>
        /// A HIPPED ROOF over any footprint (FORMS): each wall's eave runs up
        /// and in at the pitch to a crown d metres inside, capped flat (out of
        /// sight from the street). For the L-shaped and ragged footprints a
        /// gable cannot cover - round 1 gave them a flat cap: boxes. Returns
        /// false (and builds nothing) when the footprint is too thin for one.
        /// ring: clockwise from above.
        /// </summary>
        static bool HipRoof(TownBuf buf, List<Vector3> ring, float eave, float pitchDeg, string roofMat, float minDim)
        {
            int n = ring.Count;
            if (n < 3) return false;
            float tan = Mathf.Tan(pitchDeg * Mathf.Deg2Rad);
            const float ov = 0.45f;
            float d = Mathf.Min(minDim * 0.42f, 7f);
            Vector3[] inner = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (d < 0.9f) return false;
                var cand = RingOffset(ring, -d);
                if (InnerRingOk(ring, cand, d)) { inner = cand; break; }
                d *= 0.72f;
            }
            if (inner == null) return false;
            var outer = RingOffset(ring, ov);
            float yIn = eave + d * tan, yOut = eave - ov * tan;
            float tile = roofMat == "shingle" || roofMat == "tilered" ? 3f : 2.5f;
            float run = Mathf.Sqrt((d + ov) * (d + ov) + (yIn - yOut) * (yIn - yOut)) / tile;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 oa = new Vector3(outer[i].x, yOut, outer[i].z), oc = new Vector3(outer[j].x, yOut, outer[j].z);
                Vector3 ia = new Vector3(inner[i].x, yIn, inner[i].z), ic = new Vector3(inner[j].x, yIn, inner[j].z);
                Vector3 wa = new Vector3(ring[i].x, eave, ring[i].z), wc = new Vector3(ring[j].x, eave, ring[j].z);
                float len = Vector3.Distance(oa, oc) / tile;
                // the slope (faces up and out, as the wall below it faces out)
                buf.Quad(roofMat, oa, ia, ic, oc, Vector2.zero, new Vector2(0, run), new Vector2(len, run), new Vector2(len, 0));
                // the soffit under the overhang
                buf.QuadR("trim", oa, wa, wc, oc, Vector2.zero, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            }
            var ccw = new List<Vector3>(inner); ccw.Reverse();
            var tri = EarClip(ccw);
            for (int k = 0; k + 2 < tri.Count; k += 3)
            {
                Vector3 p0 = ccw[tri[k]], p1 = ccw[tri[k + 1]], p2 = ccw[tri[k + 2]];
                buf.Tri(roofMat, new Vector3(p0.x, yIn, p0.z), new Vector3(p1.x, yIn, p1.z), new Vector3(p2.x, yIn, p2.z),
                        new Vector2(p0.x / tile, p0.z / tile), new Vector2(p1.x / tile, p1.z / tile), new Vector2(p2.x / tile, p2.z / tile));
            }
            return true;
        }

        /// <summary>A clockwise ring moved out (off &gt; 0) or in (off &lt; 0) by
        /// its mitred corners (miters held to 3x).</summary>
        static Vector3[] RingOffset(List<Vector3> ring, float off)
        {
            int n = ring.Count;
            var o = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = ring[(i + n - 1) % n], q = ring[i], r = ring[(i + 1) % n];
                Vector3 e1 = q - p, e2 = r - q; e1.y = 0; e2.y = 0;
                if (e1.sqrMagnitude < 1e-6f) e1 = e2;
                if (e2.sqrMagnitude < 1e-6f) e2 = e1;
                e1.Normalize(); e2.Normalize();
                Vector3 n1 = new Vector3(-e1.z, 0, e1.x), n2 = new Vector3(-e2.z, 0, e2.x);   // outward (left of travel)
                Vector3 m = n1 + n2;
                if (m.sqrMagnitude < 1e-4f) m = n2;
                m.Normalize();
                float len = Mathf.Min(Mathf.Abs(off) / Mathf.Max(0.33f, Vector3.Dot(m, n1)), 3f * Mathf.Abs(off));
                o[i] = q + m * (len * Mathf.Sign(off));
                o[i].y = 0f;
            }
            return o;
        }

        static bool InnerRingOk(List<Vector3> ring, Vector3[] inner, float d)
        {
            int n = ring.Count;
            // same winding, a real area, every point inside and clear of every wall by most of d
            float sa = 0f, sb = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) { sa += (ring[j].x - ring[i].x) * (ring[j].z + ring[i].z); sb += (inner[j].x - inner[i].x) * (inner[j].z + inner[i].z); }
            if (Mathf.Sign(sa) != Mathf.Sign(sb) || Mathf.Abs(sb) < 0.03f * Mathf.Abs(sa)) return false;
            foreach (var p in inner)
            {
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                    if ((ring[i].z > p.z) != (ring[j].z > p.z) && p.x < ring[j].x + (p.z - ring[j].z) / (ring[i].z - ring[j].z) * (ring[i].x - ring[j].x)) inside = !inside;
                if (!inside) return false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float dx = ring[i].x - ring[j].x, dz = ring[i].z - ring[j].z, L2 = dx * dx + dz * dz;
                    float t = L2 > 1e-6f ? Mathf.Clamp01(((p.x - ring[j].x) * dx + (p.z - ring[j].z) * dz) / L2) : 0f;
                    float ex = ring[j].x + dx * t - p.x, ez = ring[j].z + dz * t - p.z;
                    if (ex * ex + ez * ez < 0.81f * d * d) return false;
                }
            }
            // and the inner ring does not cross itself
            for (int i = 0; i < n; i++)
                for (int k = i + 2; k < n; k++)
                {
                    if (i == 0 && k == n - 1) continue;
                    if (SegCross(inner[i], inner[(i + 1) % n], inner[k], inner[(k + 1) % n])) return false;
                }
            return true;
        }

        static bool SegCross(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1 = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x), d2 = (b.x - a.x) * (d.z - a.z) - (b.z - a.z) * (d.x - a.x);
            float d3 = (d.x - c.x) * (a.z - c.z) - (d.z - c.z) * (a.x - c.x), d4 = (d.x - c.x) * (b.z - c.z) - (d.z - c.z) * (b.x - c.x);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        // ---------------------------------------------------------- granite
        static void BuildWorldGranite(Transform parent)
        {
            var C = world.cliffs;
            if (C == null || C.Length < 6) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MeadowCliffDir + "/" + CliffNames[0] + ".FBX") == null)
            {
                Log("WARN: Stage world granite: the Meadow pack's cliffs are not extracted (tools/store-assets.ps1) - no granite.");
                return;
            }
            // Hickory Nut Gorge granite: paler and greyer than the Parkway's dark gneiss
            var mat = NMLitMat("NM_meadow_granite", MeadowCliffDir, "T_Photoscanned_Cliff_01_BC", "T_Photoscanned_Cliff_01_Normal",
                               null, false, new Color(0.84f, 0.83f, 0.80f));
            var vars = LoadGraniteVars(mat);
            // the rock's own skin over every cliff cell: the far land there is bare granite, not painted canopy
            BuildWorldGraniteSkin(parent, mat);
            if (vars.Count == 0) return;
            // the far grid this stage stands on (the Park Road stage's is smaller)
            var fm = demFarMeta;
            float fx0 = fm.originX + 2f * fm.cell, fz0 = fm.originZ + 2f * fm.cell, fx1 = fm.originX + (fm.cols - 3) * fm.cell, fz1 = fm.originZ + (fm.rows - 3) * fm.cell;
            var cells = new List<Vector4>(); var steep = new List<float>();
            for (int k = 0; k + 5 < C.Length; k += 6)
            {
                float x = C[k], z = C[k + 1];
                if (x < fx0 || x > fx1 || z < fz0 || z > fz1) continue;
                cells.Add(new Vector4(x, z, C[k + 3], C[k + 4])); steep.Add(C[k + 5]);
            }
            // the pieces themselves are the far mountain template's (PlaceRockPieces), the same cluster for cluster
            PlaceRockPieces(parent, mat, vars, cells, steep, PCContent ? 1600000 : 600000, "LureGranite_", "Granite_", "Stage world granite");
        }

        struct GraniteVar { public MeshForest.Variant v; public float frontAngle, length, height; public int[] tris; }

        /// <summary>The Meadow pack's photoscanned cliffs, each with its face's
        /// bearing, length, height and its levels' triangles.</summary>
        static List<GraniteVar> LoadGraniteVars(Material mat)
        {
            var vars = new List<GraniteVar>();
            foreach (var name in CliffNames)
            {
                var v = LoadVariant(MeadowCliffDir, name, mat);
                if (v == null) continue;
                Vector3 acc = Vector3.zero;
                float minY = float.MaxValue, maxY = float.MinValue, rMin = float.MaxValue, rMax = float.MinValue;
                using (var data = Mesh.AcquireReadOnlyMeshData(v.lods[0]))
                {
                    var d = data[0];
                    var nrm = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
                    var pos = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp);
                    d.GetNormals(nrm); d.GetVertices(pos);
                    for (int i = 0; i < nrm.Length; i++) acc += v.pre.MultiplyVector(nrm[i]);
                    acc.y = 0f;
                    var front = acc.sqrMagnitude > 1e-6f ? acc.normalized : Vector3.forward;
                    var right = new Vector3(front.z, 0f, -front.x);
                    for (int i = 0; i < pos.Length; i++)
                    {
                        var p = v.pre.MultiplyPoint3x4(pos[i]);
                        minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                        float rr = p.x * right.x + p.z * right.z; rMin = Mathf.Min(rMin, rr); rMax = Mathf.Max(rMax, rr);
                    }
                    nrm.Dispose(); pos.Dispose();
                    var tris = new int[3];
                    for (int l = 0; l < 3; l++) tris[l] = v.lods[l] != null ? (int)(v.lods[l].GetIndexCount(0) / 3) : 0;
                    vars.Add(new GraniteVar
                    {
                        v = v, frontAngle = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg,
                        length = Mathf.Max(1f, rMax - rMin), height = Mathf.Max(1f, maxY - minY), tris = tris,
                    });
                }
            }
            return vars;
        }

        /// <summary>
        /// THE CLIFF PIECES on rock cells (x, z, downhill x, downhill z; steep =
        /// the slope's tangent): the photoscanned cliffs on 24 m clusters, faced
        /// down the fall line, never by the road or on the town, within a
        /// triangle budget (the detailed level within 450 m of the road, the low
        /// one beyond; over budget the near ones go low, then the thinnest
        /// clusters go). Lake Lure's granite and the far mountain template's
        /// rock (PSXRacingBuilder.FarMtn) both lay theirs here. Returns the
        /// pieces placed.
        /// </summary>
        static int PlaceRockPieces(Transform parent, Material mat, List<GraniteVar> vars, List<Vector4> cellsIn, List<float> steepIn,
                                   long budget, string meshPrefix, string goPrefix, string what)
        {
            if (vars.Count == 0 || cellsIn.Count == 0) return 0;
            // 24 m clusters of the 12 m cells
            const float Cl = 24f;
            var clusters = new Dictionary<long, Vector4>();     // sum x, sum z, count, sum steep
            var cdir = new Dictionary<long, Vector2>();
            for (int k = 0; k < cellsIn.Count; k++)
            {
                float x = cellsIn[k].x, z = cellsIn[k].y;
                long key = ((long)Mathf.FloorToInt(x / Cl) << 32) ^ (uint)Mathf.FloorToInt(z / Cl);
                clusters.TryGetValue(key, out var s); clusters[key] = s + new Vector4(x, z, 1f, steepIn[k]);
                cdir.TryGetValue(key, out var dd); cdir[key] = dd + new Vector2(cellsIn[k].z, cellsIn[k].w);
            }
            float halfW = RoadWidth * 0.5f;
            var chunks = new Dictionary<long, List<CombineInstance>>();
            var cand = new List<(Vector3 pos, float yaw, float scale, int v, float dRoute, float weight)>();
            int placed = 0, nearRoad = 0, onTown = 0; long triTotal = 0;
            var rng = new System.Random(1999);
            foreach (var kv in clusters)
            {
                var s = kv.Value; float x = s.x / s.z, z = s.y / s.z;
                var dsum = cdir[kv.Key]; if (dsum.sqrMagnitude < 1e-4f) continue;
                var dir = dsum.normalized;
                int h = Hash(x, z);
                var gv = vars[h % vars.Count];
                // about 30 m of rock face per cluster, more where the cluster is full
                float want = Mathf.Lerp(20f, 32f, Mathf.Clamp01((s.z - 1f) / 3f)) * (0.85f + 0.3f * ((h / 7) % 100) / 100f);
                float scale = Mathf.Clamp(want / gv.length, 0.8f, 4.5f);
                if (gv.height * scale > 34f) scale = 34f / gv.height;
                float reach = gv.length * scale * 0.6f;
                // never by the road: its own cuts are the stage builder's
                if (StageCorridor(x, z, halfW + 16f + reach, out _)) { nearRoad++; continue; }
                // never on the town, the water or a lot
                bool blocked = false;
                for (int q = 0; q < 5 && !blocked; q++)
                {
                    float ox = q == 0 ? 0f : (q == 1 ? reach : q == 2 ? -reach : 0f), oz = q == 3 ? reach : q == 4 ? -reach : 0f;
                    if ((WorldKeepBits(x + ox, z + oz) & 7) != 0) blocked = true;
                }
                if (blocked) { onTown++; continue; }
                float gy = WorldGroundY(x, z);
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg - gv.frontAngle + (float)(rng.NextDouble() - 0.5) * 30f;
                var pos = new Vector3(x, gy - gv.height * scale * 0.28f, z);
                cand.Add((pos, yaw, scale, h % vars.Count, RouteDistanceCoarse(x, z), s.z * s.w));
            }
            long Sum(bool nearDetail) { long t = 0; foreach (var c in cand) t += vars[c.v].tris[nearDetail && c.dRoute < 450f ? 1 : 2]; return t; }
            bool detail = Sum(true) <= budget;
            cand.Sort((a, b) => b.weight.CompareTo(a.weight));
            int thinned = 0;
            foreach (var c in cand)
            {
                var gv = vars[c.v];
                int lod = detail && c.dRoute < 450f ? 1 : 2;
                if (triTotal + gv.tris[lod] > budget) { thinned++; continue; }
                var mesh = gv.v.lods[lod] ?? gv.v.lods[0];
                long ck = ((long)Mathf.FloorToInt(c.pos.x / 500f) << 32) ^ (uint)Mathf.FloorToInt(c.pos.z / 500f);
                if (!chunks.TryGetValue(ck, out var list)) chunks[ck] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = Matrix4x4.TRS(c.pos, Quaternion.Euler(0f, c.yaw, 0f), Vector3.one * c.scale) * gv.v.pre });
                triTotal += gv.tris[lod];
                placed++;
            }
            int n = 0;
            foreach (var kv in chunks)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), true, true, false);
                SaveMesh(m, meshPrefix + n);
                var go = new GameObject(goPrefix + n);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.isStatic = true;
                if (HorizonLayer >= 0) go.layer = HorizonLayer;   // drawn as far as the far ground is
                // no collider: high on the walls, never within the road's reach
                n++;
            }
            var sizes = new List<string>(); foreach (var gv in vars) sizes.Add($"{gv.length:0}x{gv.height:0} m {gv.tris[0]}/{gv.tris[1]}/{gv.tris[2]}");
            Log($"{what}: {placed} photoscanned cliff faces on {clusters.Count} lidar clusters of 24 m ({nearRoad} by the road and {onTown} on the town left out, " +
                $"{thinned} thinned to the budget; near LOD {(detail ? "1" : "2")}), " +
                $"{n} chunks, {triTotal / 1000}k triangles. Cliffs (length x height, LOD0/1/2 triangles): {string.Join("; ", sizes)}.");
            return placed;
        }

        /// <summary>The drawn far ground's height: its 60 m cells, each split
        /// on the (x0,z0)-(x1,z1) diagonal, as GridChunkMesh draws them.</summary>
        static float FarSurfaceY(float x, float z)
        {
            float X0 = Mathf.Floor(x / FarCell) * FarCell, Z0 = Mathf.Floor(z / FarCell) * FarCell;
            float u = (x - X0) / FarCell, w = (z - Z0) / FarCell;
            float ha = StageGroundHeightAt(X0, Z0), hb = StageGroundHeightAt(X0, Z0 + FarCell);
            float hc = StageGroundHeightAt(X0 + FarCell, Z0 + FarCell), he = StageGroundHeightAt(X0 + FarCell, Z0);
            float h = w >= u ? ha + u * (hc - hb) + w * (hb - ha) : ha + u * (he - ha) + w * (hc - he);
            return h - FarSink;
        }

        /// <summary>
        /// THE GRANITE'S SKIN: each 12 m cliff cell of the lidar laid over the
        /// drawn ground in the same photoscanned granite, a hair above it (the
        /// far ground's own triangles; the near lattice by a ray). Seen from
        /// the road at a kilometre or two this is what reads as a bare rock
        /// face - the cliff pieces alone read as boulders on painted canopy.
        /// Mapped across the fall line, so the rock's grain stands upright.
        /// </summary>
        static void BuildWorldGraniteSkin(Transform parent, Material mat)
        {
            var C = world.cliffs;
            var fm = demFarMeta;
            float fx0 = fm.originX + 2f * fm.cell, fz0 = fm.originZ + 2f * fm.cell, fx1 = fm.originX + (fm.cols - 3) * fm.cell, fz1 = fm.originZ + (fm.rows - 3) * fm.cell;
            float halfW = RoadWidth * 0.5f;
            var list = new List<Vector4>();
            for (int k = 0; k + 5 < C.Length; k += 6)
            {
                float x = C[k], z = C[k + 1], dx = C[k + 3], dz = C[k + 4];
                if (x < fx0 || x > fx1 || z < fz0 || z > fz1) continue;
                if (StageCorridor(x, z, halfW + 14f, out _)) continue;
                if ((WorldKeepBits(x, z) & 7) != 0) continue;
                list.Add(new Vector4(x, z, dx, dz));
            }
            // the skin itself is the far mountain template's (PSXRacingBuilder.FarMtn: BuildRockSkin), the same cell for cell
            int cells = list.Count;
            int n = BuildRockSkin(parent, mat, list, "LureGraniteSkin_", "GraniteSkin_");
            Log($"Stage world granite skin: {cells} lidar cliff cells of 12 m laid in bare granite over the drawn ground ({n} chunks).");
        }

        // ----------------------------------------------------------- marina
        static void BuildWorldPiers(Transform parent)
        {
            if (world.piers == null || world.piers.Length == 0 || world.lakeLevelY <= 0f) return;
            float halfW = RoadWidth * 0.5f;
            float deckY = world.lakeLevelY + 0.75f, bedY = world.lakeLevelY - 2.0f;
            var buf = new TownBuf();
            int piers = 0; float metres = 0f;
            foreach (var pr in world.piers)
            {
                if (pr.xz == null || pr.xz.Length < 4) continue;
                var line = WorldPts(pr.xz);
                bool inRoad = false;
                foreach (var p in line) if (StageCorridor(p.x, p.z, halfW + 3f, out _)) { inRoad = true; break; }
                if (inRoad) continue;
                // a main dock (several points) is 2.4 m wide, a finger pier 1.2 m
                float hw = line.Length > 2 ? 1.2f : 0.6f;
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    Vector3 a = line[i], c = line[i + 1];
                    Vector3 e = c - a; e.y = 0f; float len = e.magnitude; if (len < 0.5f) continue;
                    Vector3 dir = e / len, side = new Vector3(-dir.z, 0f, dir.x);
                    // run each segment on past its joint by half the width, so a bend has no notch
                    Vector3 a0 = a - dir * (i > 0 ? hw : 0f), c0 = c + dir * (i + 2 < line.Length ? hw : 0f);
                    float L = (c0 - a0).magnitude;
                    Vector3 p0 = new Vector3(a0.x, deckY, a0.z), p1 = new Vector3(c0.x, deckY, c0.z);
                    // the deck, facing up (boards across the walk)
                    buf.QuadR("planks", p0 - side * hw, p0 + side * hw, p1 + side * hw, p1 - side * hw,
                             new Vector2(0, 0), new Vector2(0, hw * 2f / 2f), new Vector2(L / 2f, hw * 2f / 2f), new Vector2(L / 2f, 0));
                    // its edge boards and posts
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        Vector3 eb = (p0 + p1) * 0.5f + side * (hw * sgn) - Vector3.up * 0.14f;
                        buf.Box("darkwood", eb, dir, Vector3.up, side, L * 0.5f, 0.14f, 0.04f);
                        int np = Mathf.Max(1, Mathf.RoundToInt(L / 3f));
                        for (int k = 0; k <= np; k++)
                        {
                            Vector3 q = Vector3.Lerp(p0, p1, k / (float)np) + side * ((hw - 0.1f) * sgn);
                            buf.Box("darkwood", new Vector3(q.x, (deckY + bedY) * 0.5f, q.z), dir, Vector3.up, side, 0.1f, (deckY - bedY) * 0.5f, 0.1f);
                        }
                    }
                    metres += len;
                }
                piers++;
            }
            TownEmit(buf, "Piers", parent);
            Log($"Stage world marina: {piers} docks and finger piers of the 2024 map, {metres:0} m of plank walk on posts {deckY - world.lakeLevelY:0.00} m over the lake.");
        }

        // ------------------------------------------------------- grass banks
        /// <summary>
        /// GRASS BANKS (owner's video 7:43, 10:05, 16:50): on the lake road, through
        /// downtown and the village (to 8,100 m) and at Bat Cave (from 10,400 m) the cuts beside US 64/74A are
        /// low, mown grass banks - a cut there under 5 m high wears grass, with
        /// no rock dressed on its face. The gorge keeps its rock. Lake Lure
        /// only (PSX_LURE_GRASSBANK=0: rock as round 1).
        /// </summary>
        internal static bool LureGrassBank(int[] run, float side)
        {
            if (StagePrefix != "lure" || world == null || !WorldSwitch("PSX_LURE_GRASSBANK") || run == null || run.Length == 0) return false;
            // the lake road, downtown and the village (to 8.1 km), and Bat Cave (from 10.4 km: grass over a
            // low stone foot at 16:50); the gorge between keeps its rock
            float m0 = run[0] * Spacing, m1 = run[run.Length - 1] * Spacing;
            if (!(m1 <= 8100f || m0 >= 10400f)) return false;
            int s = SideIx(side);
            float top = 0f;
            foreach (int i in run) if (rsFaceH != null && i >= 0 && i < rsFaceH[s].Length) top = Mathf.Max(top, rsFaceH[s][i]);
            return top < 5f;
        }

        /// <summary>The theme's timber rail stretches (PSX_LURE_TIMBER=0: the theme's rail everywhere).</summary>
        internal static bool InTimberSpan(float m)
        {
            if (theme == null || theme.timberSpans == null || !WorldSwitch("PSX_LURE_TIMBER")) return false;
            foreach (var sp in theme.timberSpans) if (m >= sp.x && m <= sp.y) return true;
            return false;
        }

        static Material lureGrassBankMat;
        internal static Material LureGrassBankMat()
        {
            if (lureGrassBankMat == null)
                lureGrassBankMat = MakeMat(MeshPrefix + "BankGrass", Root + "/Art/City/Pack/grass_7_city.png", affine: 0f, tint: new Color(0.92f, 0.95f, 0.88f));
            return lureGrassBankMat;
        }
    }
}
