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
    /// HOUSES ROUND 3: frames WHERE THE HOUSES ARE, by rule. For each named
    /// place (PSX_ROOF_STREETS = "id|lat|lon;..."), every local street within
    /// 300 m is walked every 10 m; at each point and side the house fronts
    /// (OSM house footprints and fill houses whose centre is within 40 m
    /// along and 6-40 m out) are counted; the best point (8 or more) wins.
    /// Poses: "kerb" = the far kerb, 1.2 m, looking square at the houses;
    /// "raised" = 8 m up over the near lane, three-quarter along the row.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunStreets
    ///   PSX_BLDG_TAG, PSX_BLDG_HOURS as RunBldg; PSX_BLDG_NOAUDIT=0 runs the windows audit first.
    /// Output Screenshots/bldg/&lt;id&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;tag&gt;.png, streets_log_&lt;tag&gt;.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunStreets()
        {
            if (System.Environment.GetEnvironmentVariable("PSX_BLDG_NOAUDIT") == "0") CityAudit.WindowsAudit(false);
            var I = CultureInfo.InvariantCulture;
            var log = new StringBuilder("STREET SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "bldg");
            Directory.CreateDirectory(dir);
            string tag = System.Environment.GetEnvironmentVariable("PSX_BLDG_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "r3";
            int code = 0, shots = 0;
            try
            {
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                bool tintOn = CityMeshes.HouseTintOn && !tag.StartsWith("base");
                var kit = CityKit.Get();
                if (kit != null && kit.slots != null)
                {
                    var hm = kit.slots[(int)CityMeshes.Slot.FacadeHouse];
                    if (hm != null) hm.SetFloat("_HouseTint", tintOn ? 1f : 0f);
                    var rm = kit.slots[(int)CityMeshes.Slot.RoofTiles];
                    if (rm != null && rm.HasProperty("_HouseTint")) rm.SetFloat("_HouseTint", tintOn && CityMeshes.HouseRoofTintOn ? 2f : 0f);
                }
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                var hours = Pick("PSX_BLDG_HOURS", "noon");
                string spec = System.Environment.GetEnvironmentVariable("PSX_ROOF_STREETS") ?? "";
                foreach (var item in spec.Split(';'))
                {
                    var p = item.Split('|');
                    if (p.Length < 3) continue;
                    string id = p[0];
                    Vector2 at = LL(double.Parse(p[1], I), double.Parse(p[2], I));
                    if (!BestFrontage(map, at, out var e, out float s, out Vector2 n, out int count))
                    { log.Append($"{id}: no street with houses near\n"); continue; }
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
                        ("kerb", kerb + Vector3.up * 1.2f, Quaternion.LookRotation(n3 * 30f + Vector3.up * 2.5f)),
                        ("raised", new Vector3(q.x, y, q.y) - n3 * 2f - t3 * 18f + Vector3.up * 8f,
                            Quaternion.LookRotation(n3 * 22f + t3 * 30f - Vector3.up * 5f)),
                    };
                    log.Append(string.Format(I, "{0}\t'{1}' cls {2} s {3:0} houses fronting {4}\tkerb ({5:0.0}, {6:0.0}) {7}\n",
                        id, e.name, e.cls, s, count, kerb.x, kerb.z, LatLonOf(kerb.x, kerb.z)));
                    foreach (var h in hours)
                    {
                        int hour = HourOf(h);
                        RaceHandoff.CalendarDay = 0;
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        var hd = TimeOfDay.At(hour);
                        NightGlow.PreviewAll(hd.lightsOn);
                        TextureHD.OffAll(); PSXTexDecode.StampAll();
                        foreach (var ps in poses)
                        {
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"bldg/{id}_{ps.name}_{h}_{tag}", ps.eye, ps.r);
                            shots++;
                        }
                    }
                    world.DropAll();
                }
                log.Append($"{shots} frames\nSTREET SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("STREET SHOTS FAILED: " + ex + "\n"); code = 1; }
            finally { File.WriteAllText(Path.Combine(dir, $"streets_log_{tag}.txt"), log.ToString()); Debug.Log("[Streets]\n" + log); }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string LatLonOf(float x, float z) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.000000},{1:0.000000}", z / 111132.0 + 35.18456015184093,
                x / (111320.0 * System.Math.Cos(35.18456 * System.Math.PI / 180.0)) - 80.81770185962013);

        /// <summary>The local street point and side within 300 m of <paramref name="at"/> with the most house fronts.</summary>
        static bool BestFrontage(CityMap map, Vector2 at, out CityMap.Edge best, out float bestS, out Vector2 bestN, out int bestCount)
        {
            best = null; bestS = 0f; bestN = Vector2.zero; bestCount = 0;
            var houses = new List<Vector2>();
            foreach (var f in map.footprints)
                if (f != null && f.style == 3 && !f.hidden && (f.centre - at).sqrMagnitude < 400f * 400f) houses.Add(f.centre);
            int t0x = Mathf.FloorToInt((at.x - 400f) / CityMeshes.TileSize), t1x = Mathf.FloorToInt((at.x + 400f) / CityMeshes.TileSize);
            int t0z = Mathf.FloorToInt((at.y - 400f) / CityMeshes.TileSize), t1z = Mathf.FloorToInt((at.y + 400f) / CityMeshes.TileSize);
            for (int tx = t0x; tx <= t1x; tx++)
                for (int tz = t0z; tz <= t1z; tz++)
                {
                    var fill = CityHouses.FillOf(map, tx, tz);
                    if (fill != null) foreach (var h in fill) houses.Add(h.c);
                }
            foreach (var e in map.edges)
            {
                if (e == null || e.cls > 1 || e.length < 60f) continue;
                if ((e.PointAt(e.length * 0.5f) - at).sqrMagnitude > (300f + e.length * 0.5f) * (300f + e.length * 0.5f)) continue;
                for (float s = 30f; s < e.length - 30f; s += 10f)
                {
                    Vector2 q = e.PointAt(s);
                    if ((q - at).sqrMagnitude > 300f * 300f) continue;
                    Vector2 t = e.TangentAt(s).normalized, n = new Vector2(-t.y, t.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int c = 0;
                        foreach (var h in houses)
                        {
                            var d = h - q;
                            float al = Vector2.Dot(d, t), off = Vector2.Dot(d, n) * side;
                            if (Mathf.Abs(al) < 40f && off > 6f + e.width * 0.5f && off < 40f) c++;
                        }
                        if (c > bestCount) { bestCount = c; best = e; bestS = s; bestN = n * side; }
                    }
                }
            }
            return best != null && bestCount >= 4;
        }
    }
}
