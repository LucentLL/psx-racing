using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE FLOATING HOUSES AND THE JUNCTION FURNITURE (2026-09-30), checked at
    /// the spots they touch and nowhere else:
    ///
    ///   PrepareSignals  the prop bake (the foundation skirt now reaches up to
    ///                   each model's own base: CityProps.SkirtDepthM) and the
    ///                   city kit (its new signals material), for a tool run
    ///                   with no scene build. Writes PSXRacing_signals_prepare.txt.
    ///   RunSignals      the before/after numbers of the prop lots whose
    ///                   foundations were furthest from their floors, the
    ///                   junction census, and three shots to
    ///                   Screenshots/City/signals: the worst of those houses
    ///                   now (house_fixed), an uptown signalised junction at
    ///                   night (uptown_signals_night) and a STOP sign
    ///                   (stop_sign). Writes signals.txt beside them.
    ///
    /// Headless (RunSignals with graphics):
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.PrepareSignals
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunSignals
    /// </summary>
    public static partial class CityRefSpots
    {
        static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        /// <summary><see cref="PrepareSignals"/> then <see cref="RunSignals"/>, one editor.</summary>
        public static void PrepareAndRunSignals()
        {
            PrepareSignals();
            RunSignals();
        }

        public static void PrepareSignals()
        {
            string file = Path.Combine(ProjectRoot(), "PSXRacing_signals_prepare.txt");
            if (File.Exists(file)) File.Delete(file);
            var sb = new StringBuilder();
            try
            {
                PSXRacingBuilder.BakeCityProps();
                PSXRacingBuilder.EnsureCityTextures();
                var kit = AssetDatabase.LoadAssetAtPath<CityKit>(RuntimeShaders.KitPath);
                sb.AppendLine("kit.signals: " + (kit != null && kit.signals != null ? kit.signals.name + " (" + kit.signals.shader.name + ", " + AssetDatabase.GetAssetPath(kit.signals.mainTexture) + ")" : "MISSING"));
                foreach (byte kind in new[] { CityProps.House, CityProps.Trailer0, CityProps.Trailer1, CityProps.Trailer2 })
                {
                    PropBase(kind, out float bse, out float top, out float bot);
                    sb.AppendLine($"prop {CityProps.Defs[kind].res}: model base {bse:0.000} m over the pivot, skirt top {top:0.000}, bottom {bot:0.000}");
                }
                sb.AppendLine("PREPARE OK");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("PREPARE FAILED: " + e);
                Debug.LogException(e);
            }
            File.WriteAllText(file, sb.ToString());
            AssetDatabase.SaveAssets();
        }

        /// <summary>The baked full prefab's model base and its skirt's top and
        /// bottom, metres over the pivot (NaN where missing).</summary>
        static void PropBase(byte kind, out float modelBase, out float skirtTop, out float skirtBottom)
        {
            modelBase = skirtTop = skirtBottom = float.NaN;
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PSXRacing/Resources/" + CityProps.Defs[kind].res + ".prefab");
            if (pf == null) return;
            var go = (GameObject)Object.Instantiate(pf);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                float lo = float.MaxValue;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = r.gameObject.name;
                    if (n == "Skirt") { skirtTop = r.bounds.max.y; skirtBottom = r.bounds.min.y; continue; }
                    if (n == "Solid" || n == "Apron" || n == "OrderBay") continue;
                    lo = Mathf.Min(lo, r.bounds.min.y);
                }
                if (lo < float.MaxValue) modelBase = lo;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Daylight visible between a floor and the ground at height
        /// g, less what a skirt from <paramref name="bot"/> to
        /// <paramref name="top"/> covers.</summary>
        static float Gap(float floor, float top, float bot, float g) =>
            Mathf.Max(0f, floor - g) - Mathf.Max(0f, Mathf.Min(floor, top) - Mathf.Max(g, bot));

        struct LotCheck
        {
            public CityBuildings.B b; public float oldGap, newGap, seatOld, seatNew, hiG, loG; public Vector2 lowAt; public bool dropped;
        }

        /// <summary>
        /// THE POLE CENSUS (2026-10-04; the owner on West Trade Street: "I keep
        /// finding traffic light posts in the middle of intersections"): every
        /// signal pole and STOP post in the city, before (PSX_CITY_POLEFANS off:
        /// feet tested against the ribbons only) and after (and against the
        /// junction fans), each foot tested against the drivable surface - every
        /// ribbon (the line model's extents) and every junction fan's ring
        /// (curb returns, clusters). Counts a foot ON it (its radius touching)
        /// and within MUTCD's 0.6 m of it. Writes pole_census.txt at the project
        /// root. Headless: -executeMethod PSXRacing.EditorTools.CityRefSpots.RunPoleCensus
        /// </summary>
        public static void RunPoleCensus()
        {
            string file = Path.Combine(ProjectRoot(), "pole_census.txt");
            if (File.Exists(file)) File.Delete(file);
            var log = new StringBuilder();
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[PoleCensus] no city data"); return; }
            var go = new GameObject("~poleCensus");
            var world = go.AddComponent<CityWorld>();
            bool was = CityMeshes.FurnitureOffFans;
            try
            {
                world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 0);
                world.DropAll();
                var trims = world.NodeTrims;
                var bld = world.Buildings;
                var tiles = CitySignals.Tiles(map, trims);
                foreach (bool on in new[] { false, true })
                {
                    CityMeshes.FurnitureOffFans = on;
                    int poles = 0, stops = 0, onFan = 0, onRib = 0, nearAny = 0, stopOn = 0, stopNear = 0, refused = 0;
                    var rows = new List<string>();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var t in tiles)
                    {
                        var st = CitySignals.Build(map, trims, bld, null, null, t.x, t.y);
                        refused += st.refused;
                        void Test(Vector3 foot, float r, bool stop)
                        {
                            var f = new Vector2(foot.x, foot.z);
                            int tx = Mathf.FloorToInt(f.x / CityMeshes.TileSize), tz = Mathf.FloorToInt(f.y / CityMeshes.TileSize);
                            var sm = RoadsideOccupancy.Static(map, trims, bld, tx, tz);
                            float dRib = sm.RoadEdgeDistance(f, 3f, out _, out int edge, out _);
                            float dFan = CityMeshes.JunctionPavementDistance(map, trims, f, 3f);
                            float d = Mathf.Min(dRib, dFan) - r;
                            if (stop) { stops++; if (d < 0f) stopOn++; else if (d < 0.6f) stopNear++; }
                            else
                            {
                                poles++;
                                if (d < 0f) { if (dFan - r < 0f) onFan++; else onRib++; }
                                else if (d < 0.6f) nearAny++;
                            }
                            if (d < 0f && rows.Count < 15)
                                rows.Add($"    {(stop ? "STOP post" : "signal pole")} at ({f.x:0.0}, {f.y:0.0}) {CityAudit.LatLon(f.x, f.y)}: {(dFan - r < 0f ? "on a junction fan" : $"on e{edge} '{(edge >= 0 ? map.edges[edge].name : "")}'s ribbon")} (fan {dFan:0.00} m, ribbon {dRib:0.00} m)");
                        }
                        foreach (var p in st.poles) Test(p.foot, CitySignals.PoleColliderW * 0.5f, false);
                        foreach (var s in st.stops) Test(s.foot, 0.05f, true);
                        foreach (var m in new[] { st.mesh, st.lamps, st.halos }) if (m != null) Object.DestroyImmediate(m);
                    }
                    log.AppendLine($"POLES {(on ? "AFTER (feet off the junction fans)" : "BEFORE (PSX_CITY_POLEFANS=0)")}: {poles} signal poles over {tiles.Count} tiles - ON the drivable surface {onFan + onRib} (on a junction fan {onFan}, on a ribbon {onRib}), within 0.6 m of it {nearAny}; " +
                                  $"{stops} STOP posts - on it {stopOn}, within 0.6 m {stopNear}; approaches with no clear spot {refused}; {clock.ElapsedMilliseconds} ms");
                    foreach (var r in rows) log.AppendLine(r);
                }
            }
            catch (System.Exception e) { log.AppendLine("POLE CENSUS FAILED: " + e); Debug.LogException(e); }
            finally
            {
                CityMeshes.FurnitureOffFans = was;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(file, log.ToString());
            Debug.Log("[PoleCensus] " + log.ToString().Replace("\n", " | "));
        }

        public static void RunSignals()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(ProjectRoot(), "Screenshots", "City", "signals");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            string txt = Path.Combine(dir, "signals.txt");
            if (File.Exists(txt)) File.Delete(txt);
            var log = new StringBuilder();
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~citySignalShots");
            var world = go.AddComponent<CityWorld>();
            GameObject sunGo = null;
            try
            {
                // edit mode runs no Awake: a tile build initialises the world
                world.EnsureRing(new Vector3(map.uptown.x, 0f, map.uptown.y), 0);
                world.DropAll();
                var trims = world.NodeTrims;
                var bld = world.Buildings;

                // ---- 1. the houses: before (GroundY at five points, the skirt
                // from 5 cm over the pivot) and after (the drawn ground, the
                // skirt up to the model's base)
                var bases = new Dictionary<byte, float>();
                foreach (byte kind in new[] { CityProps.House, CityProps.Trailer0, CityProps.Trailer1, CityProps.Trailer2 })
                {
                    PropBase(kind, out float bse, out _, out _);
                    bases[kind] = float.IsNaN(bse) ? 0f : bse;
                    log.AppendLine($"# {CityProps.Defs[kind].res}: model base {bases[kind]:0.000} m over its pivot (sink {CityProps.Defs[kind].sink:0.00})");
                }
                var checks = new List<LotCheck>();
                int scanned = 0, oldBad = 0, newBad = 0, dropped = 0;
                var keys = bld.Keys.OrderBy(k => k).ToList();
                foreach (var k in keys)
                {
                    foreach (var b in bld[k])
                    {
                        if (!bases.ContainsKey(b.kind)) continue;
                        if (scanned >= 4000) break;
                        scanned++;
                        var def = CityProps.Defs[b.kind];
                        float bse = bases[b.kind];
                        float sOld = CityBuildings.SeatYGroundField(map, b.pos, b.w, b.d, b.yaw);
                        float sNew = CityBuildings.SeatY(map, b.pos, b.w, b.d, b.yaw, out float lo);
                        // the lot's low point, for the camera
                        Vector2 lowAt = b.pos; float lowG = float.MaxValue;
                        float cy = Mathf.Cos(b.yaw), sy = Mathf.Sin(b.yaw);
                        var fwd = new Vector2(sy, cy); var rgt = new Vector2(cy, -sy);
                        for (int iz = -1; iz <= 1; iz++)
                            for (int ix = -1; ix <= 1; ix++)
                            {
                                var c = b.pos + rgt * (b.w * 0.5f * ix) + fwd * (b.d * 0.5f * iz);
                                float g = CityMeshes.LatticeAt(map, c.x, c.y);
                                if (g < lowG) { lowG = g; lowAt = c; }
                            }
                        float pO = sOld - def.sink, pN = sNew - def.sink;
                        float oldGap = Mathf.Max(Gap(pO + bse, pO + 0.05f, pO - 2.15f, sNew), Gap(pO + bse, pO + 0.05f, pO - 2.15f, lo));
                        float newGap = Mathf.Max(Gap(pN + bse, pN + bse + CityProps.SkirtTuckM, pN - CityProps.SkirtDepthM, sNew),
                                                 Gap(pN + bse, pN + bse + CityProps.SkirtTuckM, pN - CityProps.SkirtDepthM, lo));
                        bool drop = sNew - lo > CityProps.MaxFallM(def);
                        if (oldGap > 0.1f) oldBad++;
                        if (drop) dropped++;
                        else if (newGap > 0.1f) newBad++;
                        checks.Add(new LotCheck { b = b, oldGap = oldGap, newGap = drop ? 0f : newGap, seatOld = sOld, seatNew = sNew, hiG = sNew, loG = lo, lowAt = lowAt, dropped = drop });
                    }
                    if (scanned >= 4000) break;
                }
                log.AppendLine($"houses and trailers checked: {scanned}; before: {oldBad} with over 0.1 m of daylight between the ground and the floor that the foundation did not cover; after: {newBad} ({dropped} lots left empty: the drawn ground falls further under them than a foundation reaches)");
                log.AppendLine("worst four before -> after (x, z | seat old/new | ground hi/lo under the lot | open gap old -> new):");
                var worst = checks.Where(c => !c.dropped).OrderByDescending(c => c.oldGap).Take(4).ToList();
                foreach (var c in worst)
                    log.AppendLine($"  {CityProps.Defs[c.b.kind].res} at ({c.b.pos.x:0.0}, {c.b.pos.y:0.0}) | seat {c.seatOld:0.00} / {c.seatNew:0.00} | ground {c.hiG:0.00} / {c.loG:0.00} | {c.oldGap:0.00} m -> {c.newGap:0.00} m");

                // ---- 2. the junction census (the static mask; a tile's own lamps
                // can refuse a few more feet when it is really built)
                int nSig = 0, nStop = 0, nAllWay = 0, sigApp = 0, stopApp = 0;
                foreach (var j in CitySignals.All(map, trims))
                {
                    if (j.signal) { nSig++; sigApp += j.approaches.Count; }
                    else { nStop++; stopApp += j.approaches.Count; if (j.allWay) nAllWay++; }
                }
                int tiles = 0, heads = 0, stops = 0, bars = 0, mast = 0, span = 0, poles = 0, refused = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (var t in CitySignals.Tiles(map, trims))
                {
                    var st = CitySignals.Build(map, trims, bld, null, null, t.x, t.y);
                    tiles++;
                    heads += st.heads.Count; stops += st.stops.Count; bars += st.stopBars;
                    mast += st.mastArms; span += st.spanWires; poles += st.poles.Count; refused += st.refused;
                    foreach (var m in new[] { st.mesh, st.lamps, st.halos }) if (m != null) Object.DestroyImmediate(m);
                }
                log.AppendLine($"junctions: {nSig} signalised ({sigApp} approaches), {nStop} stop-controlled ({stopApp} approaches, {nAllWay} all-way) over {tiles} tiles");
                log.AppendLine($"placed: {stops} STOP signs, {bars} stop bars, {heads} signal heads on {mast} mast arms and {span} span wires ({poles} signal poles); {refused} approaches with no clear spot; census {clock.ElapsedMilliseconds} ms");

                // ---- 3. the shots
                DayLight();
                // (a) the worst house, from its low side
                if (worst.Count > 0)
                {
                    var c = worst[0];
                    var centre = c.b.pos;
                    var away = c.lowAt - centre;
                    if (away.sqrMagnitude < 1e-3f) away = new Vector2(Mathf.Sin(c.b.yaw), Mathf.Cos(c.b.yaw));
                    away.Normalize();
                    var e2 = centre + away * (Mathf.Max(c.b.w, c.b.d) * 0.5f + 11f);
                    world.EnsureRing(new Vector3(centre.x, 0f, centre.y), 1);
                    float ey = CityMeshes.LatticeAt(map, e2.x, e2.y) + 1.7f;
                    var eye = new Vector3(e2.x, ey, e2.y);
                    var look = new Vector3(centre.x, c.seatNew + 0.6f, centre.y);
                    CitySignals.Tick(5.0);
                    Shoot(dir, "house_fixed", eye, Quaternion.LookRotation(look - eye), 0f);
                    log.AppendLine($"shot house_fixed: eye ({eye.x:0.0}, {eye.y:0.00}, {eye.z:0.0}) at the lot ({centre.x:0.0}, {centre.y:0.0}); props dropped this run {CityWorld.PropsDropped}");
                    world.DropAll();
                }

                // (b) a STOP sign by day: the stop-controlled junction nearest
                // Dilworth whose sign stood, from 24 m back in its lane
                var dil = LL(35.20225, -80.84758);
                bool stopShot = false;
                foreach (var j in CitySignals.All(map, trims).Where(q => !q.signal).OrderBy(q => (q.centre - dil).sqrMagnitude).Take(12))
                {
                    world.EnsureRing(new Vector3(j.centre.x, 0f, j.centre.y), 1);
                    CitySignals.Stop found = default; bool ok = false;
                    foreach (var st in world.LiveSignals)
                        foreach (var s in st.stops)
                            if (!ok && j.approaches.Any(q => q.edge == s.edge) && Vector2.Distance(new Vector2(s.foot.x, s.foot.z), j.centre) < 40f) { found = s; ok = true; }
                    if (!ok) { world.DropAll(); continue; }
                    var a = j.approaches.First(q => q.edge == found.edge);
                    var p = a.at - a.u * 11f + a.Right * ((a.inL + a.inR) * 0.5f);
                    var eye = new Vector3(p.x, a.roadY + 1.3f, p.y);
                    var look = new Vector3(found.foot.x, a.roadY + 2.0f, found.foot.z) + new Vector3(a.u.x, 0f, a.u.y) * 4f;
                    CitySignals.Tick(5.0);
                    Shoot(dir, "stop_sign", eye, Quaternion.LookRotation(look - eye), 0f);
                    log.AppendLine($"shot stop_sign: junction {j.id} at ({j.centre.x:0.0}, {j.centre.y:0.0}){(j.allWay ? " (all-way)" : "")}, sign at ({found.foot.x:0.0}, {found.foot.z:0.0}), eye ({eye.x:0.0}, {eye.y:0.00}, {eye.z:0.0}); {j.approaches.Count} approaches: '{map.edges[a.edge].name}'");
                    world.DropAll();
                    stopShot = true;
                    break;
                }
                if (!stopShot) log.AppendLine("shot stop_sign: NO STOP SIGN STOOD near Dilworth");

                // (c) an uptown signalised junction at night: the one nearest
                // Trade & Tryon whose heads face the camera's approach
                sunGo = new GameObject("~signalSun");
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                var globals = sunGo.AddComponent<PSXGlobals>();
                globals.sun = sun;
                TimeOfDay.Apply(TimeOfDay.Night, sun);
                globals.Apply();
                nightSky = globals.fogColor;
                bool nightShot = false;
                foreach (var j in CitySignals.All(map, trims).Where(q => q.signal && q.uptown && q.approaches.Count >= 3).OrderBy(q => (q.centre - map.uptown).sqrMagnitude).Take(12))
                {
                    world.EnsureRing(new Vector3(j.centre.x, 0f, j.centre.y), 1);
                    // the approach with the most heads facing it
                    int bestN = 0; CitySignals.Approach best = default;
                    foreach (var a in j.approaches)
                    {
                        int n = 0;
                        foreach (var st in world.LiveSignals)
                            foreach (var h in st.heads)
                                if (Vector2.Dot(h.face, -a.u) > 0.9f && Vector2.Distance(new Vector2(h.pos.x, h.pos.z), j.centre) < 60f) n++;
                        if (n > bestN) { bestN = n; best = a; }
                    }
                    if (bestN == 0) { world.DropAll(); continue; }
                    var p = best.at - best.u * 5f + best.Right * ((best.inL + best.inR) * 0.5f);
                    world.EnsureRing(new Vector3(p.x, 0f, p.y), 1);
                    var eye = new Vector3(p.x, best.roadY + 1.2f, p.y);
                    // at the heads that face it
                    var aim = Vector3.zero; int na = 0;
                    foreach (var st in world.LiveSignals)
                        foreach (var h in st.heads)
                            if (Vector2.Dot(h.face, -best.u) > 0.9f && Vector2.Distance(new Vector2(h.pos.x, h.pos.z), j.centre) < 60f) { aim += h.pos; na++; }
                    var look = na > 0 ? aim / na - Vector3.up * 1.5f : new Vector3(j.centre.x, j.roadY + 4.5f, j.centre.y);
                    // by day first, from the same eye (the geometry)
                    DayLight();
                    NightGlow.PreviewAll(false);
                    CitySignals.Tick(best.group == 0 ? 6.0 : CitySignals.HalfCycleS + 6.0);
                    Shoot(dir, "uptown_signals_day", eye, Quaternion.LookRotation(look - eye), 0f);
                    TimeOfDay.Apply(TimeOfDay.Night, sun);
                    nightSky = globals.fogColor;
                    NightGlow.PreviewAll(true);
                    globals.Apply();
                    // its own pair green
                    CitySignals.Tick(best.group == 0 ? 6.0 : CitySignals.HalfCycleS + 6.0);
                    Shoot(dir, "uptown_signals_night", eye, Quaternion.LookRotation(look - eye), 0f);
                    // and a frame later in the cycle: the pair on yellow / the other green
                    CitySignals.Tick(best.group == 0 ? CitySignals.GreenS + 1.0 : CitySignals.HalfCycleS + CitySignals.GreenS + 1.0);
                    Shoot(dir, "uptown_signals_night_yellow", eye, Quaternion.LookRotation(look - eye), 0f);
                    string names = string.Join(" / ", j.approaches.Select(q => map.edges[q.edge].name).Distinct());
                    log.AppendLine($"shot uptown_signals_night: junction {j.id} at ({j.centre.x:0.0}, {j.centre.y:0.0}) ({names}), {j.approaches.Count} approaches, {bestN} heads facing the camera, eye ({eye.x:0.0}, {eye.y:0.00}, {eye.z:0.0})");
                    SpotFeet(world, map, "uptown_signals_night", eye, log);
                    world.DropAll();
                    nightShot = true;
                    break;
                }
                if (!nightShot) log.AppendLine("shot uptown_signals_night: NO UPTOWN SIGNAL HEADS STOOD");

                // (d) a span wire by day (outside uptown), for the geometry
                DayLight();
                NightGlow.PreviewAll(false);
                foreach (var j in CitySignals.All(map, trims).Where(q => q.signal && !q.uptown && q.approaches.Count >= 4).OrderBy(q => (q.centre - dil).sqrMagnitude).Take(20))
                {
                    world.EnsureRing(new Vector3(j.centre.x, 0f, j.centre.y), 1);
                    bool spanHere = false;
                    foreach (var st in world.LiveSignals) if (st.spanWires > 0 && st.heads.Exists(h => Vector2.Distance(new Vector2(h.pos.x, h.pos.z), j.centre) < 40f)) spanHere = true;
                    if (!spanHere) { world.DropAll(); continue; }
                    var a = j.approaches[0];
                    var p = a.at - a.u * 18f + a.Right * ((a.inL + a.inR) * 0.5f);
                    var eye = new Vector3(p.x, a.roadY + 1.3f, p.y);
                    var look = new Vector3(j.centre.x, j.roadY + 5.5f, j.centre.y);
                    CitySignals.Tick(a.group == 0 ? 6.0 : CitySignals.HalfCycleS + 6.0);
                    Shoot(dir, "span_wire_day", eye, Quaternion.LookRotation(look - eye), 0f);
                    log.AppendLine($"shot span_wire_day: junction {j.id} at ({j.centre.x:0.0}, {j.centre.y:0.0}) ('{map.edges[a.edge].name}'), eye ({eye.x:0.0}, {eye.y:0.00}, {eye.z:0.0})");
                    world.DropAll();
                    break;
                }
            }
            catch (System.Exception e)
            {
                log.AppendLine("RUN FAILED: " + e);
                Debug.LogException(e);
            }
            finally
            {
                nightSky = null;
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
                if (sunGo != null) Object.DestroyImmediate(sunGo);
                File.WriteAllText(txt, log.ToString());
                Debug.Log("[CityRefSpots] signals:\n" + log);
            }
        }
    }
}
