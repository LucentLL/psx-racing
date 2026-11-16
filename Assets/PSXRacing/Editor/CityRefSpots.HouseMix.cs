using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HOUSE MIX'S PICTURES (agent "houses", 2026-11-16): the same poses in a
    /// fresh process with PSX_CITY_HOUSEMIX=0 (the base) and with it on.
    ///   STREETS  PSX_HM_STREETS = "id|lat|lon|eraMin|eraMax;..." - within 600 m, the
    ///            local street point and side with the most house fronts of those eras
    ///            (CityMeshes.HouseMixOf: 0 pre-1940, 1 mill village, 2 1945-69, 3 1970-89,
    ///            4 1990-2009, 5 2010-); poses "kerb" and "raised" as RunStreets.
    ///   CLOSE    PSX_HM_CLOSE = "id|lat|lon|what;..." - the nearest mapped house within
    ///            900 m whose class is what (brick, front, paint, stone, table, siding)
    ///            and whose front a street comes within 30 m of; poses "wall" (square on,
    ///            7.5 m out) and "corner" (three-quarter, its front corner and side).
    ///   PSX_HM_HOURS (noon,night), PSX_HM_TIERS (enh,hd: Enhanced without and with the
    ///   HD sets; classic), PSX_HM_CLOSETIERS (enh,hd,classic), PSX_BLDG_TAG the tag.
    ///   PSX_BLDG_NOAUDIT=0 runs the windows audit first.
    /// Output Screenshots/bldg/hm_&lt;id&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;tier&gt;_&lt;tag&gt;.png and
    /// hm_log_&lt;tag&gt;.txt.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunHouseMix
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunHouseMix()
        {
            var I = CultureInfo.InvariantCulture;
            var log = new StringBuilder("HOUSE MIX SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "bldg");
            Directory.CreateDirectory(dir);
            string tag = System.Environment.GetEnvironmentVariable("PSX_BLDG_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "mix";
            int code = 0, shots = 0;
            try
            {
                // the kit first (a shared sandbox: another branch's job may have built it without the house slots)
                if (System.Environment.GetEnvironmentVariable("PSX_HM_KIT") != "0" && !PSXRacingBuilder.HouseMixKitBuild()) throw new System.Exception("the city kit did not build (housemix_kit_log.txt)");
                if (System.Environment.GetEnvironmentVariable("PSX_BLDG_NOAUDIT") == "0") CityAudit.WindowsAudit(false);
                log.Append($"tag {tag}: HOUSEMIX {CityMeshes.HouseMixOn} HOUSEWALLS {CityMeshes.HouseWallsOn} HOUSETINT {CityMeshes.HouseTintOn} HOUSEPARTS {CityMeshes.HousePartsOn}\n");
                var tiers = Pick("PSX_HM_TIERS", "enh,hd");
                var closeTiers = Pick("PSX_HM_CLOSETIERS", "enh,hd");
                bool anyHd = System.Array.IndexOf(tiers, "hd") >= 0 || System.Array.IndexOf(closeTiers, "hd") >= 0;
                if (anyHd)
                {
                    string err = TextureHDTool.Import(out int sets);
                    log.Append($"HD import: {(err ?? "ok")}, {sets} sets\n");
                    TextureHD.ResetMap();
                }
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                CityMeshes.HouseMixPrime(map);
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var hours = Pick("PSX_HM_HOURS", "noon,night");

                // ---- streets
                string spec = System.Environment.GetEnvironmentVariable("PSX_HM_STREETS") ?? "";
                foreach (var item in spec.Split(';'))
                {
                    var p = item.Split('|');
                    if (p.Length < 5) continue;
                    string id = p[0];
                    int e0 = int.Parse(p[3], I), e1 = int.Parse(p[4], I);
                    CityMap.Edge e = null; float s = 0f; Vector2 n = Vector2.zero, hmid = Vector2.zero; int count = 0; string mixNote = "";
                    bool got = false;
                    if (p[1] == "auto")
                    {
                        // "id|auto|kmMin-kmMax|era0|era1": the first ring spot (the audit's golden-angle walk) whose
                        // 600 m era cell is wanted and whose street fronts six houses of those eras
                        var km = p[2].Split('-');
                        float k0 = float.Parse(km[0], I) * 1000f, k1 = float.Parse(km[1], I) * 1000f;
                        for (int k = 0; k < 600 && !got; k++)
                        {
                            float ang = k * 2.39996f, rr = k0 + (k * 977 % Mathf.Max(1, (int)(k1 - k0)));
                            Vector2 cand = map.uptown + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                            int era = CityMeshes.HouseMixOf(cand).era;
                            if (era < e0 || era > e1) continue;
                            var fill = CityHouses.FillOf(map, Mathf.FloorToInt(cand.x / CityMeshes.TileSize), Mathf.FloorToInt(cand.y / CityMeshes.TileSize));
                            if (fill == null || fill.Count < 8) continue;
                            if (HmFrontage(map, cand, 350f, e0, e1, out e, out s, out n, out count, out mixNote, out hmid) && count >= 6) got = true;
                        }
                    }
                    else got = HmFrontage(map, LL(double.Parse(p[1], I), double.Parse(p[2], I)), p.Length > 5 ? float.Parse(p[5], I) : 600f, e0, e1, out e, out s, out n, out count, out mixNote, out hmid);
                    if (!got) { log.Append($"{id}: no street with houses of eras {e0}-{e1} near\n"); continue; }
                    Vector2 q = e.PointAt(s);
                    float y = e.YAt(s);
                    var t2 = e.TangentAt(s).normalized;
                    Vector3 n3 = new Vector3(n.x, 0f, n.y), t3 = new Vector3(t2.x, 0f, t2.y);
                    Vector3 kerb = new Vector3(q.x, y, q.y) - n3 * (e.width * 0.5f + 0.5f);
                    world.EnsureRing(kerb, 2);
                    var rot0 = Quaternion.LookRotation(t3, Vector3.up);
                    if (car != null) car.TeleportTo(kerb - n3 * 3f + Vector3.up * 0.35f, rot0); else player.transform.SetPositionAndRotation(kerb, rot0);
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>
                    {
                        ("kerb", kerb + Vector3.up * 1.2f, Quaternion.LookRotation(n3 * 30f + t3 * 6f + Vector3.up * 2.0f)),
                        ("raised", new Vector3(q.x, y, q.y) - n3 * 2f - t3 * 18f + Vector3.up * 8f,
                            Quaternion.LookRotation(n3 * 22f + t3 * 30f - Vector3.up * 5f)),
                    };
                    {
                        // "houses": 6 m up over the near kerb, looking at the middle of the houses this frame faces
                        // (a subdivision's fill houses stand up to 75 m back, out of the other two frames)
                        Vector3 eyeH = kerb + Vector3.up * 6f - t3 * 6f;
                        float gh = CityElevation.GroundY(map, hmid.x, hmid.y);
                        poses.Add(("houses", eyeH, Quaternion.LookRotation(new Vector3(hmid.x, gh + 2.5f, hmid.y) - eyeH)));
                    }
                    log.Append(string.Format(I, "{0}\t'{1}' cls {2} s {3:0} fronts {4} ({5})\tkerb ({6:0.0}, {7:0.0}) {8}\n",
                        id, e.name, e.cls, s, count, mixNote, kerb.x, kerb.z, LatLonOf(kerb.x, kerb.z)));
                    shots += HmShoot(cam, sun, "hm_" + id, poses, hours, tiers, tag, log);
                    world.DropAll();
                }

                // ---- close-ups ("id|lat|lon|what[|radius m]"; OSM houses and fill houses)
                spec = System.Environment.GetEnvironmentVariable("PSX_HM_CLOSE") ?? "";
                var used = new HashSet<Vector2>();
                foreach (var item in spec.Split(';'))
                {
                    var p = item.Split('|');
                    if (p.Length < 4) continue;
                    string id = p[0], what = p[3];
                    float radius = p.Length > 4 ? float.Parse(p[4], I) : 900f;
                    Vector2 at = LL(double.Parse(p[1], I), double.Parse(p[2], I));
                    if (!HmPickHouse(map, at, radius, what, used, out var hc, out Vector2 fn, out Vector2 ft, out float hd, out float hw, out string kind))
                    { log.Append($"{id}: no '{what}' house within {radius:0} m\n"); continue; }
                    used.Add(hc);
                    var hmx = CityMeshes.HouseMixOf(hc);
                    Vector2 mid = hc + fn * hd;
                    float g = CityElevation.GroundY(map, mid.x + fn.x * 7.5f, mid.y + fn.y * 7.5f);
                    float gw = CityElevation.GroundY(map, mid.x + fn.x * 0.5f, mid.y + fn.y * 0.5f);
                    Vector3 eyeW = new Vector3(mid.x + fn.x * 7.5f - ft.x * 1.2f, g + 1.6f, mid.y + fn.y * 7.5f - ft.y * 1.2f);
                    Vector3 lookW = new Vector3(mid.x, gw + 1.7f, mid.y);
                    Vector2 cornerP = mid + ft * hw;
                    Vector2 eyeC2 = cornerP + fn * 6.5f + ft * 5.0f;
                    float gc = CityElevation.GroundY(map, eyeC2.x, eyeC2.y);
                    Vector3 eyeC = new Vector3(eyeC2.x, gc + 1.7f, eyeC2.y);
                    Vector3 lookC = new Vector3(cornerP.x - ft.x * 1.0f - fn.x * 0.5f, gw + 1.6f, cornerP.y - ft.y * 1.0f - fn.y * 0.5f);
                    world.EnsureRing(new Vector3(mid.x, g, mid.y), 1);
                    if (car != null) car.TeleportTo(eyeW + new Vector3(fn.x, 0f, fn.y) * 12f, Quaternion.LookRotation(new Vector3(ft.x, 0f, ft.y)));
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>
                    {
                        ("wall", eyeW, Quaternion.LookRotation(lookW - eyeW)),
                        ("corner", eyeC, Quaternion.LookRotation(lookC - eyeC)),
                    };
                    log.Append(string.Format(I, "{0}\t{1}: {2} house at ({3:0.0}, {4:0.0}) {5} era {6} class {7} mat {8} found {9} table {10} infill {11}; front {12:0.0} m wide, {13:0.0} m deep\n",
                        id, what, kind, hc.x, hc.y, LatLonOf(hc.x, hc.y), CityMeshes.HmEraNames[hmx.era], CityMeshes.HmClassNames[hmx.cls],
                        hmx.mat, hmx.found, hmx.stoneTable, hmx.infill, hw * 2f, hd * 2f));
                    // STAGED: the trees are hidden for the wall close-ups (a yard tree stood in front of the
                    // first one); the sheet labels these cells so
                    var hidden = HmHideTrees();
                    try { shots += HmShoot(cam, sun, "hm_" + id, poses, new[] { "noon" }, closeTiers, tag, log); }
                    finally { foreach (var r in hidden) if (r != null) r.enabled = true; foreach (var b in hiddenB) if (b != null) b.enabled = true; hiddenB.Clear(); }
                    world.DropAll();
                }
                log.Append($"{shots} frames\nHOUSE MIX SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("HOUSE MIX SHOTS FAILED: " + ex + "\n"); code = 1; }
            finally { File.WriteAllText(Path.Combine(dir, $"hm_log_{tag}.txt"), log.ToString()); Debug.Log("[HouseMix]\n" + log); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static int HmShoot(Camera cam, Light sun, string file, List<(string name, Vector3 eye, Quaternion r)> poses, string[] hours, string[] tiers, string tag, StringBuilder log)
        {
            int n = 0;
            foreach (var h in hours)
            {
                int hour = HourOf(h);
                RaceHandoff.CalendarDay = 0;
                TimeOfDay.Apply(hour, sun);
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                if (globals != null) globals.Apply();
                var hd = TimeOfDay.At(hour);
                NightGlow.PreviewAll(hd.lightsOn);
                foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
                bool flick = System.Environment.GetEnvironmentVariable("PSX_HM_FLICK") == "1";
                foreach (var tier in tiers)
                {
                    // the editor's tier: PSX_TEX=enhanced (TextureTier.WantEnhanced in edit mode), PSX_HD=1 the HD sets over it
                    System.Environment.SetEnvironmentVariable("PSX_TEX", tier == "classic" ? "classic" : "enhanced");
                    TexturePrefs.Enhanced = tier != "classic";
                    TextureTier.ApplyAll();
                    System.Environment.SetEnvironmentVariable("PSX_HD", tier == "hd" ? "1" : "0");
                    TextureHD.OffAll();
                    PSXTexDecode.StampAll();
                    foreach (var ps in poses)
                    {
                        CarLights.PushGlobals();
                        PSXScreenshotTool.ShotAs(cam, $"bldg/{file}_{ps.name}_{h}_{tier}_{tag}", ps.eye, ps.r);
                        n++;
                        if (flick && tier == tiers[0])
                        {
                            // the depth-tie test (CityFlickerFinder's): the same frame with the near plane x1.01;
                            // coplanar faces flip where their depths tie
                            float n0 = cam.nearClipPlane;
                            cam.nearClipPlane = n0 * 1.01f;
                            PSXScreenshotTool.ShotAs(cam, $"bldg/{file}_{ps.name}_{h}_{tier}nf_{tag}", ps.eye, ps.r);
                            cam.nearClipPlane = n0;
                            n++;
                        }
                    }
                    if (tier == "hd") log.Append($"  {file} {h}: HD materials {TextureHD.Applied}\n");
                }
            }
            System.Environment.SetEnvironmentVariable("PSX_HD", "0");
            System.Environment.SetEnvironmentVariable("PSX_TEX", "enhanced");
            TexturePrefs.Enhanced = true;
            return n;
        }

        static readonly List<Behaviour> hiddenB = new List<Behaviour>();

        /// <summary>The trees' renderers off (tile tree cards, understory, median plants, the
        /// mesh trees and trunks), for a wall close-up; the caller turns them back on.</summary>
        static List<Renderer> HmHideTrees()
        {
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.enabled) continue;
                var go = r.gameObject;
                bool tree = go.layer == CityTrees.FoliageLayer || go.name == "Trees" || go.name == "Understory" || go.name == "MedianPlants";
                for (var t = go.transform; !tree && t != null; t = t.parent)
                    if (t.name == "CityMeshTrees" || t.name == "CityTreeTrunks") tree = true;
                if (tree) { r.enabled = false; hidden.Add(r); }
            }
            foreach (var b in Object.FindObjectsByType<CityMeshTrees>(FindObjectsInactive.Exclude)) if (b.enabled) { b.enabled = false; hiddenB.Add(b); }
            return hidden;
        }

        /// <summary>BestFrontage with an era filter (the house mix's era of each house) and a radius.</summary>
        static bool HmFrontage(CityMap map, Vector2 at, float radius, int era0, int era1, out CityMap.Edge best, out float bestS, out Vector2 bestN, out int bestCount, out string note, out Vector2 mid)
        {
            best = null; bestS = 0f; bestN = Vector2.zero; bestCount = 0; note = ""; mid = Vector2.zero;
            var houses = new List<Vector2>();
            float r2 = (radius + 100f) * (radius + 100f);
            foreach (var f in map.footprints)
                if (f != null && f.style == 3 && !f.hidden && (f.centre - at).sqrMagnitude < r2) houses.Add(f.centre);
            int t0x = Mathf.FloorToInt((at.x - radius - 100f) / CityMeshes.TileSize), t1x = Mathf.FloorToInt((at.x + radius + 100f) / CityMeshes.TileSize);
            int t0z = Mathf.FloorToInt((at.y - radius - 100f) / CityMeshes.TileSize), t1z = Mathf.FloorToInt((at.y + radius + 100f) / CityMeshes.TileSize);
            for (int tx = t0x; tx <= t1x; tx++)
                for (int tz = t0z; tz <= t1z; tz++)
                {
                    var fill = CityHouses.FillOf(map, tx, tz);
                    if (fill != null) foreach (var h in fill) houses.Add(h.c);
                }
            var keep = new List<Vector2>();
            foreach (var c in houses) { int era = CityMeshes.HouseMixOf(c).era; if (era >= era0 && era <= era1) keep.Add(c); }
            houses = keep;
            foreach (var e in map.edges)
            {
                if (e == null || e.cls > 1 || e.length < 60f) continue;
                if ((e.PointAt(e.length * 0.5f) - at).sqrMagnitude > (radius + e.length * 0.5f) * (radius + e.length * 0.5f)) continue;
                for (float s = 30f; s < e.length - 30f; s += 10f)
                {
                    Vector2 q = e.PointAt(s);
                    if ((q - at).sqrMagnitude > radius * radius) continue;
                    Vector2 t = e.TangentAt(s).normalized, n = new Vector2(-t.y, t.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int c = 0;
                        foreach (var h in houses)
                        {
                            var d = h - q;
                            float al = Vector2.Dot(d, t), off = Vector2.Dot(d, n) * side;
                            if (Mathf.Abs(al) < 40f && off > 6f + e.width * 0.5f && off < 75f) c++;   // (fill houses stand up to 75 m back)
                        }
                        if (c > bestCount) { bestCount = c; best = e; bestS = s; bestN = n * side; }
                    }
                }
            }
            if (best == null || bestCount < 4) return false;
            // the classes of the houses this frame faces
            var cnt = new int[CityMeshes.HmClasses]; int nm = 0;
            Vector2 bq = best.PointAt(bestS), bt = best.TangentAt(bestS).normalized;
            foreach (var h in houses)
            {
                var d = h - bq;
                float al = Vector2.Dot(d, bt), off = Vector2.Dot(d, bestN);
                if (Mathf.Abs(al) < 60f && off > 0f && off < 60f) cnt[CityMeshes.HouseMixOf(h).cls]++;
                if (Mathf.Abs(al) < 40f && off > 6f + best.width * 0.5f && off < 75f) { mid += h; nm++; }
            }
            var sb = new StringBuilder();
            for (int k = 0; k < cnt.Length; k++) if (cnt[k] > 0) sb.Append(CityMeshes.HmClassNames[k]).Append(' ').Append(cnt[k]).Append(", ");
            note = sb.ToString().TrimEnd(',', ' ');
            mid = nm > 0 ? mid / nm : bq + bestN * 25f;
            return true;
        }

        /// <summary>The nearest house (an OSM house footprint, a plain rectangle, or a fill
        /// house) of the wanted class within <paramref name="radius"/>: its centre, its front
        /// normal fn (toward the nearest street within 80 m; else its first long side), the
        /// along-front tangent ft, half depth and half width. The eye's spot 7.5 m out must be
        /// open ground (no mapped building within 2 m of it).</summary>
        static bool HmPickHouse(CityMap map, Vector2 at, float radius, string what, HashSet<Vector2> used,
                                out Vector2 best, out Vector2 fn, out Vector2 ft, out float halfDepth, out float halfWidth, out string kind)
        {
            best = Vector2.zero; fn = ft = Vector2.zero; halfDepth = halfWidth = 0f; kind = "";
            bool found = false;
            float bestD = radius * radius;
            var cands = new List<(Vector2 c, Vector2 u, float hu, float hv, string kind)>();
            foreach (var f in map.footprints)
            {
                if (f == null || f.style != 3 || f.hidden || f.part || CityMeshes.HouseBlock(f)) continue;
                if ((f.centre - at).sqrMagnitude >= bestD) continue;
                if (f.pts == null || f.pts.Length > 5 || f.hu < 4f || f.hv < 3.5f) continue;   // a plain rectangle, a house's size
                cands.Add((f.centre, f.u, f.hu, f.hv, "OSM"));
            }
            int r = Mathf.CeilToInt(radius / CityMeshes.TileSize) + 1;
            int cx = Mathf.FloorToInt(at.x / CityMeshes.TileSize), cz = Mathf.FloorToInt(at.y / CityMeshes.TileSize);
            for (int tx = cx - r; tx <= cx + r; tx++)
                for (int tz = cz - r; tz <= cz + r; tz++)
                {
                    var fill = CityHouses.FillOf(map, tx, tz);
                    if (fill == null) continue;
                    foreach (var h in fill)
                        if ((h.c - at).sqrMagnitude < bestD && h.hu >= 4f && h.hv >= 3.5f) cands.Add((h.c, h.u, h.hu, h.hv, "fill"));
                }
            foreach (var cd in cands)
            {
                float d2 = (cd.c - at).sqrMagnitude;
                if (d2 >= bestD || used.Contains(cd.c)) continue;
                var m = CityMeshes.HouseMixOf(cd.c);
                bool ok = what == "brick" ? m.cls == CityMeshes.HmBrick
                        : what == "brickold" ? m.cls == CityMeshes.HmBrick && m.mat == CityMeshes.HmOld
                        : what == "bricknew" ? m.cls == CityMeshes.HmBrick && m.mat == CityMeshes.HmNew
                        : what == "front" ? m.cls == CityMeshes.HmBrickFront
                        : what == "paint" ? m.cls == CityMeshes.HmPainted
                        : what == "stone" ? m.cls == CityMeshes.HmStoneFront
                        : what == "table" ? m.stoneTable
                        : what == "siding" ? m.cls == CityMeshes.HmSiding && !m.stoneTable
                        : false;
                if (!ok) continue;
                Vector2 u = cd.u, v = new Vector2(-u.y, u.x);
                Vector2 n;
                // a brick or stone front needs the street the walls code finds (60 m); the others any within 80 m
                float reach = what == "front" || what == "stone" || what == "table" ? 60f : 80f;
                if (map.NearestRoadPoint(cd.c, reach, true, out int ei, out float s, out _))
                {
                    Vector2 to = map.edges[ei].PointAt(s) - cd.c;
                    if (to.sqrMagnitude < 1f) continue;
                    to.Normalize();
                    float du0 = Vector2.Dot(to, u), dv0 = Vector2.Dot(to, v);
                    n = Mathf.Abs(du0) > Mathf.Abs(dv0) ? u * Mathf.Sign(du0) : v * Mathf.Sign(dv0);
                }
                else if (what == "front" || what == "stone" || what == "table") continue;
                else n = v;
                bool alongU = Mathf.Abs(Vector2.Dot(n, u)) > 0.7f;
                float hdp = alongU ? cd.hu : cd.hv, hwd = alongU ? cd.hv : cd.hu;
                Vector2 eye = cd.c + n * (hdp + 7.5f);
                bool blocked = false;
                foreach (var o in cands)
                {
                    if (o.c == cd.c || (o.c - eye).sqrMagnitude > (o.hu + o.hv + 3f) * (o.hu + o.hv + 3f)) continue;
                    Vector2 ov = new Vector2(-o.u.y, o.u.x);
                    if (Mathf.Abs(Vector2.Dot(eye - o.c, o.u)) < o.hu + 2f && Mathf.Abs(Vector2.Dot(eye - o.c, ov)) < o.hv + 2f) { blocked = true; break; }
                }
                if (blocked) continue;
                found = true; best = cd.c; bestD = d2; fn = n; ft = new Vector2(-n.y, n.x); halfDepth = hdp; halfWidth = hwd; kind = cd.kind;
            }
            return found;
        }
    }
}
