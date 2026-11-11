using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TEXTURE BATCH PICTURES (Charlotte texture batch 01, 2026-11-11): the
    /// same camera poses with the HD tier OFF (the game as the PC build draws it
    /// today) and ON (Codex's sets through TextureHD, the road base included),
    /// at noon, dusk and night with the street lamps, at the places the batch
    /// changes: an Uptown street with its junction and brick pavers, an Uptown
    /// street with plain sidewalks, an arterial with a planted median, a
    /// concrete freeway deck, a surface parking lot. Shot through the game's
    /// own path (Charlotte.unity opened, TimeOfDay, NightGlow, the lamp table,
    /// PSXScreenshotTool.ShotAs: PSX/Blit, film grade) at the PC picture height.
    ///
    /// Poses: chase (the game's chase camera behind the parked car: texel
    /// density against the car), eye (driver's eye, 1.2 m, in front of the
    /// car, looking down the lane: the near road and the far repeat), walk
    /// (1.6 m over the sidewalk, Uptown places only), and for the lot and the
    /// freeway a high view (8 m) for the repeat at distance.
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTexB01
    ///   PSX_HD_SRC = the HD sets' folder (TextureHDTool); PSX_TEXB_ONLY=place,place;
    ///   PSX_TEXB_HOURS=noon,dusk,night; PSX_TEXB_MODES=before,after
    /// Output: Screenshots/texb01/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;before|after&gt;.png
    /// and texb01_log.txt (where each camera stood, what the HD tier applied).
    /// </summary>
    public static partial class CityRefSpots
    {
        struct TexPlace { public string id, road, what; public double lat, lon; public float hdg; public bool walk, high, ctx; }

        /// <summary>Context frames for the artist's NEXT list (PSX_TEXB_CTX=1):
        /// the surfaces batch 01 does not touch, as the game draws them with
        /// batch 01 in, at noon and at night.</summary>
        static readonly TexPlace[] CtxPlaces =
        {
            new TexPlace { id = "ctx_e_trade", lat = 35.22690, lon = -80.84272, road = "Trade", hdg = 140f, walk = true, ctx = true, what = "E Trade St, Tryon to College: storefront ground floors, a skybridge" },
            new TexPlace { id = "ctx_n_tryon", lat = 35.22745, lon = -80.84275, road = "Tryon", hdg = 50f, walk = true, ctx = true, what = "N Tryon St, Trade to 5th: street furniture, tree grates, lobbies" },
            new TexPlace { id = "ctx_e_4th_sky", lat = 35.22610, lon = -80.84385, road = "4th", hdg = 140f, walk = false, ctx = true, what = "E 4th St (a skybridge)" },
            new TexPlace { id = "ctx_wilkinson", lat = 35.22421, lon = -80.88659, road = "Wilkinson", hdg = 260f, ctx = true, what = "Wilkinson Blvd: a concrete median, curbs" },
            new TexPlace { id = "ctx_i77", lat = 35.22700, lon = -80.86208, road = "I-77", hdg = 46f, ctx = true, what = "I-77 west of Uptown: Jersey barrier, grass verge" },
            new TexPlace { id = "ctx_providence", lat = 35.13864, lon = -80.78400, road = "Providence", hdg = 328f, ctx = true, what = "Providence Rd: planted median, curb and gutter, verge" },
        };

        static readonly TexPlace[] TexPlaces =
        {
            new TexPlace { id = "up_square", lat = 35.22700, lon = -80.84338, road = "Tryon", hdg = 50f, walk = true, what = "The Square: S Tryon into Trade (junction slab, brick pavers)" },
            new TexPlace { id = "up_e4th", lat = 35.22610, lon = -80.84385, road = "4th", hdg = 140f, walk = true, what = "E 4th St, Tryon to College (concrete sidewalks, a junction ahead)" },
            new TexPlace { id = "brookshire", lat = 35.26560, lon = -80.88162, road = "Brookshire", hdg = 149f, what = "NC-16 Brookshire Blvd by N Crigler St (divided arterial, planted median)" },
        };

        const int TexShotHeight = 720;

        public static void RunTexB01()
        {
            var log = new StringBuilder("TEXB01 SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "texb01");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            string hdWas = System.Environment.GetEnvironmentVariable("PSX_HD");
            try
            {
                // the HD sets, imported as the PC build imports them
                System.Environment.SetEnvironmentVariable("PSX_HD", "1");
                string err = TextureHDTool.Import(out int sets);
                TextureHD.ResetMap();
                log.Append($"HD import: {sets} sets from {System.Environment.GetEnvironmentVariable("PSX_HD_SRC")}{(err != null ? " ERROR " + err : "")}\n");

                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) { log.Append($"picture height {output.height} -> {TexShotHeight} for these frames\n"); output.height = TexShotHeight; }
                TexturePrefs.Enhanced = true;   // the PC default
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();

                string only = System.Environment.GetEnvironmentVariable("PSX_TEXB_ONLY");
                var hours = Pick("PSX_TEXB_HOURS", "noon,dusk,night");
                var modes = Pick("PSX_TEXB_MODES", "before,after");

                var places = new List<(string id, string what, CityMap.Edge e, float s, float hdg, bool walk, bool high)>();
                bool ctxRun = System.Environment.GetEnvironmentVariable("PSX_TEXB_CTX") == "1";
                foreach (var tp in ctxRun ? CtxPlaces : TexPlaces)
                {
                    if (!Snap(map, LL(tp.lat, tp.lon), tp.road, out var e, out float s, out bool named)) { log.Append($"{tp.id}: no road near\n"); continue; }
                    places.Add((tp.id, tp.what + (named ? "" : " (NOT the named road)"), e, s, tp.hdg, tp.walk, false));
                }
                // a concrete freeway: the longest elevated motorway run nearest Uptown
                if (ctxRun) { }
                else if (FindDeck(map, out var de, out float ds)) places.Add(("fwy_deck", $"elevated freeway '{de.name}' (concrete deck)", de, ds, float.NaN, false, true));
                else log.Append("fwy_deck: no elevated motorway near Uptown\n");
                // a surface parking lot: the largest within 2.5 km of Uptown
                var lot = ctxRun ? null : FindLot(map, out Vector2 lotA0, out Vector2 lotC0);
                Vector2 lotA = Vector2.zero, lotC = Vector2.zero;
                if (lot != null) FindLot(map, out lotA, out lotC);

                ShootAll(hours, modes, only);
                // THE VARIANTS (A/B only, never a default): PSX_TEXB_VARIANTS =
                // "Name=<folder of the full set list with the variant swapped in>=place,place;..."
                string vars = System.Environment.GetEnvironmentVariable("PSX_TEXB_VARIANTS");
                if (!string.IsNullOrEmpty(vars))
                    foreach (var v in vars.Split(';'))
                    {
                        var parts = v.Split('=');
                        if (parts.Length < 3) continue;
                        System.Environment.SetEnvironmentVariable("PSX_HD_SRC", parts[1]);
                        TextureHD.OffAll();
                        string verr = TextureHDTool.Import(out int vsets);
                        TextureHD.ResetMap();
                        log.Append($"VARIANT {parts[0]}: {vsets} sets from {parts[1]}{(verr != null ? " ERROR " + verr : "")}\n");
                        ShootAll(Pick("PSX_TEXB_VARIANT_HOURS", "noon,night"), new[] { "v" + parts[0] }, parts[2]);
                    }

                void ShootAll(string[] hoursA, string[] modesA, string onlyA)
                {
                foreach (var pl in places)
                {
                    if (!string.IsNullOrEmpty(onlyA) && System.Array.IndexOf(onlyA.Split(','), pl.id) < 0) continue;
                    NightLookShots.Stand(pl.e, pl.s, out Vector3 pos, out Quaternion rot);
                    Vector3 fwd = rot * Vector3.forward;
                    if (!float.IsNaN(pl.hdg))
                    {
                        // along the place's compass heading, in the right-hand lane of it
                        float hr = pl.hdg * Mathf.Deg2Rad;
                        var want = new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr));
                        var t2 = pl.e.TangentAt(pl.s);
                        var fh = new Vector3(t2.x, 0f, t2.y).normalized;
                        if (Vector3.Dot(fh, want) < 0f) fh = -fh;
                        Vector2 p = pl.e.PointAt(pl.s);
                        float lat = pl.e.oneway ? 0f : pl.e.width * 0.25f;
                        pos = new Vector3(p.x, pl.e.YAt(pl.s) + 0.35f, p.y) + Vector3.Cross(Vector3.up, fh) * lat;
                        rot = Quaternion.LookRotation(fh, Vector3.up);
                    }
                    world.EnsureRing(pos, 2);
                    if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                    Vector3 f = rot * Vector3.forward, fl = new Vector3(f.x, 0f, f.z).normalized;
                    Vector3 rightV = Vector3.Cross(Vector3.up, fl).normalized;
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    {
                        Vector3 eye = pos - fl * 8f + Vector3.up * 2.6f;
                        poses.Add(("chase", eye, Quaternion.LookRotation(pos + Vector3.up * 0.8f + fl * 20f - eye)));
                        Vector3 e2 = pos + fl * 4f + Vector3.up * 0.95f;
                        poses.Add(("eye", e2, Quaternion.LookRotation(fl * 40f + Vector3.down * 1.4f)));
                        if (pl.walk)
                        {
                            Vector3 e3 = pos + rightV * (pl.e.width * 0.5f + (pl.e.oneway ? 2.0f : 2.0f + pl.e.width * 0.25f)) + Vector3.up * 1.45f;
                            poses.Add(("walk", e3, Quaternion.LookRotation(fl * 20f + Vector3.down * 1.6f)));
                        }
                        if (pl.high)
                        {
                            Vector3 e4 = pos - fl * 30f + Vector3.up * 8f;
                            poses.Add(("high", e4, Quaternion.LookRotation(fl * 150f + Vector3.down * 8f)));
                        }
                    }
                    log.Append($"{pl.id}\t'{pl.e.name}' cls {pl.e.cls} profile {pl.e.profile} width {pl.e.width:0.0} m{(pl.e.oneway ? " one-way" : "")}\tcar ({pos.x:0.0}, {pos.y:0.00}, {pos.z:0.0}) fwd ({fl.x:0.00}, {fl.z:0.00})\t{pl.what}\n");
                    shots += ShootSet(cam, sun, dir, pl.id, poses, hoursA, modesA, log, pl.id == "up_square");
                    world.DropAll();
                }
                if (lot != null && (string.IsNullOrEmpty(onlyA) || System.Array.IndexOf(onlyA.Split(','), "lot") >= 0))
                {
                    float gy = GroundY(lotA);
                    Vector3 a = new Vector3(lotA.x, gy, lotA.y), c = new Vector3(lotC.x, GroundY(lotC), lotC.y);
                    world.EnsureRing(a, 2);
                    // the car out of the way: parked at the corner, facing in
                    var dirL = (c - a); dirL.y = 0f; dirL.Normalize();
                    var carPos = a + Vector3.up * 0.35f;
                    if (car != null) car.TeleportTo(carPos, Quaternion.LookRotation(dirL)); else player.transform.SetPositionAndRotation(carPos, Quaternion.LookRotation(dirL));
                    var poses = new List<(string, Vector3, Quaternion)>
                    {
                        ("chase", a - dirL * 8f + Vector3.up * 2.6f, Quaternion.LookRotation(a + Vector3.up * 0.8f + dirL * 20f - (a - dirL * 8f + Vector3.up * 2.6f))),
                        ("eye", a + dirL * 4f + Vector3.up * 1.3f, Quaternion.LookRotation(dirL * 30f + Vector3.down * 1.3f)),
                        ("high", a - dirL * 10f + Vector3.up * 8f, Quaternion.LookRotation(dirL * 80f + Vector3.down * 8f)),
                    };
                    log.Append($"lot\tbox {lot.box}\tcorner ({a.x:0.0}, {a.y:0.00}, {a.z:0.0}) toward ({c.x:0.0}, {c.z:0.0})\tthe largest surface lot near Uptown\n");
                    shots += ShootSet(cam, sun, dir, "lot", poses, hoursA, modesA, log);
                    world.DropAll();
                }
                }
                log.Append($"{shots} frames to {dir}\nTEXB01 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("TEXB01 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_HD", hdWas);
                File.WriteAllText(Path.Combine(dir, "texb01_log.txt"), log.ToString());
                Debug.Log("[TexB01]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string[] Pick(string env, string dflt)
        {
            string v = System.Environment.GetEnvironmentVariable(env);
            return (string.IsNullOrEmpty(v) ? dflt : v).Split(',');
        }

        static int HourOf(string h) => h == "noon" ? TimeOfDay.Noon : h == "dusk" ? TimeOfDay.Dusk : h == "night" ? TimeOfDay.Night : TimeOfDay.Afternoon;

        static int ShootSet(Camera cam, Light sun, string dir, string place, List<(string name, Vector3 eye, Quaternion r)> poses,
                            string[] hours, string[] modes, StringBuilder log, bool jsetAtNoon = false)
        {
            int n = 0;
            foreach (var h in hours)
            {
                // the junction A/B: at noon, also the slab on its own 12 m set (TextureHD.JunctionOnBase false)
                var ms = new List<string>(modes);
                if (jsetAtNoon && h == "noon" && ms.Contains("after")) ms.Add("jset");
                int hour = HourOf(h);
                RaceHandoff.CalendarDay = 0;
                TimeOfDay.Apply(hour, sun);
                var globals = Object.FindAnyObjectByType<PSXGlobals>();
                if (globals != null) globals.Apply();
                var hd = TimeOfDay.At(hour);
                NightGlow.PreviewAll(hd.lightsOn);
                foreach (var lights in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) lights.PreviewBuild(hd.lightsOn);
                foreach (var m in ms)
                {
                    bool after = m != "before";
                    System.Environment.SetEnvironmentVariable("PSX_HD", after ? "1" : "0");
                    TextureHD.JunctionOnBase = m != "jset";
                    TextureHD.OffAll();
                    PSXTexDecode.StampAll();
                    foreach (var p in poses)
                    {
                        CarLights.PushGlobals();
                        string file = $"texb01/{place}_{p.name}_{h}_{m}";
                        PSXScreenshotTool.ShotAs(cam, file, p.eye, p.r);
                        n++;
                    }
                    log.Append($"  {place} {h} {m}: HD materials {TextureHD.Applied}\n");
                }
            }
            TextureHD.JunctionOnBase = true;
            return n;
        }

        static float GroundY(Vector2 p) =>
            Physics.Raycast(new Vector3(p.x, 600f, p.y), Vector3.down, out var hit, 1200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;

        static bool FindDeck(CityMap map, out CityMap.Edge best, out float bestS)
        {
            best = null; bestS = 0f; float bestD = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e == null || e.pts == null || e.cls < 5 || e.link || e.length < 200f) continue;
                for (float s = 60f; s <= e.length - 60f; s += 20f)
                {
                    if (!e.ElevatedAt(s) || !e.ElevatedAt(s - 50f) || !e.ElevatedAt(s + 50f)) continue;
                    float d = Vector2.Distance(e.PointAt(s), map.uptown);
                    if (d < bestD) { bestD = d; best = e; bestS = s; }
                }
            }
            return best != null && bestD < 6000f;
        }

        static CityMap.Lot FindLot(CityMap map, out Vector2 corner, out Vector2 centre)
        {
            corner = centre = Vector2.zero;
            if (map.lots == null) return null;
            CityMap.Lot best = null; float bestA = 0f;
            foreach (var l in map.lots)
            {
                if (l == null || l.aisleOnly) continue;
                var c = new Vector2((l.box.x + l.box.z) * 0.5f, (l.box.y + l.box.w) * 0.5f);
                if (Vector2.Distance(c, map.uptown) > 2500f) continue;
                float a = (l.box.z - l.box.x) * (l.box.w - l.box.y);
                if (a > bestA) { bestA = a; best = l; }
            }
            if (best == null) return null;
            centre = new Vector2((best.box.x + best.box.z) * 0.5f, (best.box.y + best.box.w) * 0.5f);
            // a ring vertex pulled 15% toward the centre: inside the lot, at its edge
            var v = best.ring[0];
            corner = Vector2.Lerp(v, centre, 0.15f);
            return best;
        }
    }
}
