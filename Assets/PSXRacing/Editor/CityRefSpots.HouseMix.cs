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
                    Vector2 at = LL(double.Parse(p[1], I), double.Parse(p[2], I));
                    int e0 = int.Parse(p[3], I), e1 = int.Parse(p[4], I);
                    if (!HmFrontage(map, at, 600f, e0, e1, out var e, out float s, out Vector2 n, out int count, out string mixNote))
                    { log.Append($"{id}: no street with houses of eras {e0}-{e1} near\n"); continue; }
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
                    log.Append(string.Format(I, "{0}\t'{1}' cls {2} s {3:0} fronts {4} ({5})\tkerb ({6:0.0}, {7:0.0}) {8}\n",
                        id, e.name, e.cls, s, count, mixNote, kerb.x, kerb.z, LatLonOf(kerb.x, kerb.z)));
                    shots += HmShoot(cam, sun, "hm_" + id, poses, hours, tiers, tag, log);
                    world.DropAll();
                }

                // ---- close-ups
                spec = System.Environment.GetEnvironmentVariable("PSX_HM_CLOSE") ?? "";
                var used = new HashSet<CityMap.Footprint>();
                foreach (var item in spec.Split(';'))
                {
                    var p = item.Split('|');
                    if (p.Length < 4) continue;
                    string id = p[0], what = p[3];
                    Vector2 at = LL(double.Parse(p[1], I), double.Parse(p[2], I));
                    if (!HmPickHouse(map, at, 900f, what, used, out var f, out Vector2 fn, out Vector2 ft, out float hd, out float hw))
                    { log.Append($"{id}: no '{what}' house with a street front near\n"); continue; }
                    used.Add(f);
                    var hmx = CityMeshes.HouseMixOf(f.centre);
                    Vector2 mid = f.centre + fn * hd;
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
                    log.Append(string.Format(I, "{0}\t{1}: house at ({2:0.0}, {3:0.0}) {4} era {5} class {6} mat {7} found {8} table {9} infill {10}; {11:0} x {12:0} m, h {13:0.0}\n",
                        id, what, f.centre.x, f.centre.y, LatLonOf(f.centre.x, f.centre.y), CityMeshes.HmEraNames[hmx.era], CityMeshes.HmClassNames[hmx.cls],
                        hmx.mat, hmx.found, hmx.stoneTable, hmx.infill, f.hu * 2f, f.hv * 2f, f.h));
                    shots += HmShoot(cam, sun, "hm_" + id, poses, new[] { "noon" }, closeTiers, tag, log);
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
                foreach (var tier in tiers)
                {
                    TexturePrefs.Enhanced = tier != "classic";
                    System.Environment.SetEnvironmentVariable("PSX_HD", tier == "hd" ? "1" : "0");
                    TextureHD.OffAll();
                    PSXTexDecode.StampAll();
                    foreach (var ps in poses)
                    {
                        CarLights.PushGlobals();
                        PSXScreenshotTool.ShotAs(cam, $"bldg/{file}_{ps.name}_{h}_{tier}_{tag}", ps.eye, ps.r);
                        n++;
                    }
                    if (tier == "hd") log.Append($"  {file} {h}: HD materials {TextureHD.Applied}\n");
                }
            }
            System.Environment.SetEnvironmentVariable("PSX_HD", "0");
            TexturePrefs.Enhanced = true;
            return n;
        }

        /// <summary>BestFrontage with an era filter (the house mix's era of each house) and a radius.</summary>
        static bool HmFrontage(CityMap map, Vector2 at, float radius, int era0, int era1, out CityMap.Edge best, out float bestS, out Vector2 bestN, out int bestCount, out string note)
        {
            best = null; bestS = 0f; bestN = Vector2.zero; bestCount = 0; note = "";
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
                            if (Mathf.Abs(al) < 40f && off > 6f + e.width * 0.5f && off < 45f) c++;
                        }
                        if (c > bestCount) { bestCount = c; best = e; bestS = s; bestN = n * side; }
                    }
                }
            }
            if (best == null || bestCount < 4) return false;
            // the classes of the houses this frame faces
            var cnt = new int[CityMeshes.HmClasses];
            Vector2 bq = best.PointAt(bestS), bt = best.TangentAt(bestS).normalized;
            foreach (var h in houses)
            {
                var d = h - bq;
                float al = Vector2.Dot(d, bt), off = Vector2.Dot(d, bestN);
                if (Mathf.Abs(al) < 60f && off > 0f && off < 60f) cnt[CityMeshes.HouseMixOf(h).cls]++;
            }
            var sb = new StringBuilder();
            for (int k = 0; k < cnt.Length; k++) if (cnt[k] > 0) sb.Append(CityMeshes.HmClassNames[k]).Append(' ').Append(cnt[k]).Append(", ");
            note = sb.ToString().TrimEnd(',', ' ');
            return true;
        }

        /// <summary>The nearest mapped house of the wanted class whose front faces a street
        /// within 30 m: its front normal fn, along-front tangent ft, half depth and half width.</summary>
        static bool HmPickHouse(CityMap map, Vector2 at, float radius, string what, HashSet<CityMap.Footprint> used,
                                out CityMap.Footprint best, out Vector2 fn, out Vector2 ft, out float halfDepth, out float halfWidth)
        {
            best = null; fn = ft = Vector2.zero; halfDepth = halfWidth = 0f;
            float bestD = radius * radius;
            foreach (var f in map.footprints)
            {
                if (f == null || f.style != 3 || f.hidden || f.part || used.Contains(f) || CityMeshes.HouseBlock(f)) continue;
                float d2 = (f.centre - at).sqrMagnitude;
                if (d2 >= bestD) continue;
                if (f.pts == null || f.pts.Length > 5 || f.hu < 4f || f.hv < 3.5f) continue;   // a plain rectangle, a house's size
                var m = CityMeshes.HouseMixOf(f.centre);
                bool ok = what == "brick" ? m.cls == CityMeshes.HmBrick
                        : what == "front" ? m.cls == CityMeshes.HmBrickFront
                        : what == "paint" ? m.cls == CityMeshes.HmPainted
                        : what == "stone" ? m.cls == CityMeshes.HmStoneFront
                        : what == "table" ? m.stoneTable
                        : what == "siding" ? m.cls == CityMeshes.HmSiding && !m.stoneTable
                        : false;
                if (!ok) continue;
                if (!map.NearestRoadPoint(f.centre, 30f, true, out int ei, out float s, out _)) continue;
                Vector2 to = map.edges[ei].PointAt(s) - f.centre;
                if (to.sqrMagnitude < 1f) continue;
                to.Normalize();
                Vector2 u = f.u, v = new Vector2(-u.y, u.x);
                float du = Vector2.Dot(to, u), dv = Vector2.Dot(to, v);
                Vector2 n = Mathf.Abs(du) > Mathf.Abs(dv) ? u * Mathf.Sign(du) : v * Mathf.Sign(dv);
                float hdp = Mathf.Abs(du) > Mathf.Abs(dv) ? f.hu : f.hv, hwd = Mathf.Abs(du) > Mathf.Abs(dv) ? f.hv : f.hu;
                // the eye's spot must be open ground (no other building within 2 m of it)
                Vector2 eye = f.centre + n * (hdp + 7.5f);
                bool blocked = false;
                foreach (var o in map.footprints)
                    if (o != null && o != f && !o.hidden && (o.centre - eye).sqrMagnitude < (o.hu + o.hv + 2f) * (o.hu + o.hv + 2f)
                        && Mathf.Abs(Vector2.Dot(eye - o.centre, o.u)) < o.hu + 2f && Mathf.Abs(Vector2.Dot(eye - o.centre, new Vector2(-o.u.y, o.u.x))) < o.hv + 2f) { blocked = true; break; }
                if (blocked) continue;
                best = f; bestD = d2; fn = n; ft = new Vector2(-n.y, n.x); halfDepth = hdp; halfWidth = hwd;
            }
            return best != null;
        }
    }
}
