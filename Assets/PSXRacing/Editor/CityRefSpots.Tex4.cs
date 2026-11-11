using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// TEXTURES ROUND 4 (tex4, 2026-11-11): the owner's "why are building
    /// textures so blurry" - the shopfronts, Codex's roads/walks R2 and the
    /// glass facade candidates c00-c02, each shot with the HD stage the owner's
    /// exe carries today (texb01, asphalt gain 3.1) and with the new stage, at
    /// noon, dusk and night, through the game's own path (as RunTexB01).
    ///
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunTex4
    ///   PSX_TEX4_MODES = "name=folder[;folder]=gain|name=..." (default
    ///     r1 = texb01 at 3.1, new = texb02 at 1; the 11-11 shots used texb02
    ///     with the glass candidates in a separate folder, since merged into it)
    ///   PSX_TEXB_HOURS = noon,dusk,night; PSX_TEX4_ONLY = place,place
    ///   PSX_TEX4_SHOPS = how many shop buildings to shoot (default 6)
    /// Output: Screenshots/tex4/&lt;place&gt;_&lt;pose&gt;_&lt;hour&gt;_&lt;mode&gt;.png + tex4_log.txt
    /// </summary>
    public static partial class CityRefSpots
    {
        static readonly TexPlace[] Tex4Places =
        {
            new TexPlace { id = "R_square", lat = 35.22700, lon = -80.84338, road = "Tryon", hdg = 50f, walk = true, what = "The Square: S Tryon into Trade (pavers; the black-road-in-shade spot)" },
            new TexPlace { id = "R_e4th", lat = 35.22610, lon = -80.84385, road = "4th", hdg = 140f, walk = true, what = "E 4th St, Tryon to College (concrete sidewalks)" },
            new TexPlace { id = "G_glass_towers", lat = 35.22660, lon = -80.84395, road = "Tryon", hdg = 50f, bld = true, what = "S Tryon St, 4th to Trade: glass curtain-wall towers" },
            new TexPlace { id = "G_stone_towers", lat = 35.22930, lon = -80.84010, road = "Tryon", hdg = 230f, bld = true, what = "N Tryon St, 7th back to 6th" },
        };

        public static void RunTex4()
        {
            var log = new StringBuilder("TEX4 SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "tex4");
            Directory.CreateDirectory(dir);
            int code = 0, shots = 0;
            string hdWas = System.Environment.GetEnvironmentVariable("PSX_HD"), srcWas = System.Environment.GetEnvironmentVariable("PSX_HD_SRC");
            float gainWas = TextureHDMap.AsphaltNewGain;
            try
            {
                string modesEnv = System.Environment.GetEnvironmentVariable("PSX_TEX4_MODES");
                if (string.IsNullOrEmpty(modesEnv))
                    modesEnv = @"r1=C:\Users\mcgee\PSXHDStage\texb01=3.1|new=C:\Users\mcgee\PSXHDStage\texb02=1";
                var modes = new List<(string name, string src, float gain)>();
                foreach (var m in modesEnv.Split('|'))
                {
                    var p = m.Split('=');
                    if (p.Length < 3) continue;
                    modes.Add((p[0], p[1], float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)));
                }
                var hours = Pick("PSX_TEXB_HOURS", "noon,dusk,night");
                string only = System.Environment.GetEnvironmentVariable("PSX_TEX4_ONLY");
                int nShops = int.TryParse(System.Environment.GetEnvironmentVariable("PSX_TEX4_SHOPS"), out int ns) ? ns : 6;

                System.Environment.SetEnvironmentVariable("PSX_HD", "1");
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindFirstObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();

                // ---- the places: car pose + camera poses ----
                var places = new List<(string id, string what, Vector3 carPos, Quaternion carRot, List<(string name, Vector3 eye, Quaternion r)> poses)>();
                foreach (var tp in Tex4Places)
                {
                    if (!Snap(map, LL(tp.lat, tp.lon), tp.road, out var e, out float s, out bool named)) { log.Append($"{tp.id}: no road near\n"); continue; }
                    float hr = tp.hdg * Mathf.Deg2Rad;
                    var want = new Vector3(Mathf.Sin(hr), 0f, Mathf.Cos(hr));
                    var t2 = e.TangentAt(s);
                    var fl = new Vector3(t2.x, 0f, t2.y).normalized;
                    if (Vector3.Dot(fl, want) < 0f) fl = -fl;
                    Vector2 p = e.PointAt(s);
                    float lat = e.oneway ? 0f : e.width * 0.25f;
                    var rightV = Vector3.Cross(Vector3.up, fl).normalized;
                    var pos = new Vector3(p.x, e.YAt(s) + 0.35f, p.y) + rightV * lat;
                    var poses = new List<(string, Vector3, Quaternion)>();
                    if (tp.bld)
                    {
                        float half = e.oneway ? e.width * 0.5f : e.width * 0.25f;
                        poses.Add(("eye", pos + fl * 4f + Vector3.up * 0.95f, Quaternion.LookRotation(fl * 40f + Vector3.up * 2.0f)));
                        poses.Add(("across", pos - rightV * (lat + e.width * 0.5f - 1.0f) + Vector3.up * 1.6f, Quaternion.LookRotation(rightV * 20f + fl * 9f + Vector3.up * 6f)));
                        poses.Add(("up", pos + rightV * (half + 1.5f) + Vector3.up * 1.6f, Quaternion.LookRotation(fl * 14f + rightV * 7f + Vector3.up * 13f)));
                    }
                    else
                    {
                        Vector3 eye = pos - fl * 8f + Vector3.up * 2.6f;
                        poses.Add(("chase", eye, Quaternion.LookRotation(pos + Vector3.up * 0.8f + fl * 20f - eye)));
                        poses.Add(("eye", pos + fl * 4f + Vector3.up * 0.95f, Quaternion.LookRotation(fl * 40f + Vector3.down * 1.4f)));
                        if (tp.walk)
                            poses.Add(("walk", pos + rightV * ((e.oneway ? e.width * 0.5f : e.width * 0.25f) + 1.3f) + Vector3.up * 1.45f,
                                       Quaternion.LookRotation(fl * 12f - rightV * 1.5f + Vector3.down * 1.75f)));
                    }
                    places.Add((tp.id, tp.what + (named ? "" : " (NOT the named road)"), pos, Quaternion.LookRotation(fl), poses));
                }
                // THE SHOPFRONTS: core retail footprints (OSM shop, inside the core:
                // the whole four-front atlas on the wall facing the nearest street,
                // CityMeshes.EmitWallStyled), nearest Uptown first
                var shopIdx = new List<int>();
                for (int i = 0; i < map.footprints.Length; i++)
                {
                    var f = map.footprints[i];
                    if (f == null || f.style != 4 || f.hidden || f.part || f.h < 6f || !map.footprintBounds.Contains(f.centre)) continue;
                    shopIdx.Add(i);
                }
                shopIdx.Sort((a, b) => Vector2.Distance(map.footprints[a].centre, map.uptown).CompareTo(Vector2.Distance(map.footprints[b].centre, map.uptown)));
                int taken = 0;
                foreach (int fi in shopIdx)
                {
                    if (taken >= nShops) break;
                    var f = map.footprints[fi];
                    if (!map.NearestRoadPoint(f.centre, 70f, true, out int rei, out float rs, out _)) continue;
                    var re = map.edges[rei];
                    var q = re.PointAt(rs);
                    var toRoad = (q - f.centre).normalized;
                    int wall = -1; float best = 0.5f;
                    for (int i = 0; i < f.pts.Length; i++)
                    {
                        var d = f.pts[(i + 1) % f.pts.Length] - f.pts[i];
                        if (d.sqrMagnitude < 144f) continue;   // a 12 m wall at least (four 3 m fronts)
                        var n = new Vector2(d.y, -d.x).normalized;
                        float dt = Vector2.Dot(n, toRoad);
                        if (dt > best) { best = dt; wall = i; }
                    }
                    if (wall < 0) continue;
                    Vector2 a = f.pts[wall], c = f.pts[(wall + 1) % f.pts.Length];
                    float wallW = Vector2.Distance(a, c);
                    Vector2 nrm = new Vector2((c - a).y, -(c - a).x).normalized, mid = (a + c) * 0.5f;
                    float y = re.YAt(rs);
                    var t2 = re.TangentAt(rs);
                    var fl = new Vector3(t2.x, 0f, t2.y).normalized;
                    var rightV = Vector3.Cross(Vector3.up, fl).normalized;
                    if (Vector3.Dot(rightV, new Vector3(f.centre.x - q.x, 0f, f.centre.y - q.y)) < 0f) { fl = -fl; rightV = -rightV; }
                    float lat = re.oneway ? 0f : re.width * 0.25f;
                    // the car 14 m short of the shop, in the lane by it
                    Vector2 q0 = q - new Vector2(fl.x, fl.z) * 14f;
                    var carPos = new Vector3(q0.x, y + 0.35f, q0.y) + rightV * lat;
                    var poses = new List<(string, Vector3, Quaternion)>();
                    Vector3 eye = carPos - fl * 8f + Vector3.up * 2.6f;
                    poses.Add(("chase", eye, Quaternion.LookRotation(carPos + Vector3.up * 0.8f + fl * 20f - eye)));
                    // face on from 9 m out, eye height, the whole front in frame
                    float back = Mathf.Clamp(wallW * 0.45f, 8f, 14f);
                    var m3 = new Vector3(mid.x, y, mid.y);
                    var n3 = new Vector3(nrm.x, 0f, nrm.y);
                    Vector3 eF = m3 + n3 * back + Vector3.up * 1.6f;
                    poses.Add(("front", eF, Quaternion.LookRotation(m3 + Vector3.up * 2.3f - eF)));
                    int reps = Mathf.Max(1, Mathf.RoundToInt(wallW / 24f));
                    places.Add(($"S{taken}", $"shop footprint {fi} h {f.h:0.0} m, front wall {wallW:0.0} m = {reps} repeat(s), {wallW / (4f * reps):0.00} m a front x 4.2 m; road '{re.name}' {Vector2.Distance(q, mid):0.0} m off",
                                carPos, Quaternion.LookRotation(fl), poses));
                    taken++;
                }

                foreach (var md in modes)
                {
                    System.Environment.SetEnvironmentVariable("PSX_HD_SRC", md.src);
                    TextureHDMap.AsphaltNewGain = md.gain;
                    TextureHD.OffAll();
                    string err = TextureHDTool.Import(out int sets);
                    TextureHD.ResetMap();
                    TextureHD.OffAll();
                    PSXTexDecode.StampAll();
                    log.Append($"MODE {md.name}: {sets} sets from {md.src}, asphalt_new gain {md.gain}{(err != null ? " ERROR " + err : "")}\n");
                    foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                        if (mat != null && mat.name == "CityShops")
                        {
                            var mt = mat.mainTexture;
                            log.Append($"  CityShops main texture: {(mt != null ? mt.name + " " + mt.width + " x " + mt.height + " " + mt.filterMode + " aniso " + mt.anisoLevel + " mips " + (mt is Texture2D t2d ? t2d.mipmapCount : 0) : "none")}, PSX_HD {mat.IsKeywordEnabled(TextureHD.Keyword)}\n");
                        }
                    foreach (var pl in places)
                    {
                        if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), pl.id) < 0) continue;
                        world.EnsureRing(pl.carPos, 2);
                        if (car != null) car.TeleportTo(pl.carPos, pl.carRot); else player.transform.SetPositionAndRotation(pl.carPos, pl.carRot);
                        if (md.name == modes[0].name) log.Append($"{pl.id}\tcar ({pl.carPos.x:0.0}, {pl.carPos.y:0.00}, {pl.carPos.z:0.0})\t{pl.what}\n");
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
                            PSXTexDecode.StampAll();
                            foreach (var p in pl.poses)
                            {
                                CarLights.PushGlobals();
                                PSXScreenshotTool.ShotAs(cam, $"tex4/{pl.id}_{p.name}_{h}_{md.name}", p.eye, p.r);
                                shots++;
                            }
                        }
                        world.DropAll();
                    }
                    log.Append($"  HD materials live: {TextureHD.Applied}\n");
                }
                log.Append($"{shots} frames to {dir}\nTEX4 SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("TEX4 SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                System.Environment.SetEnvironmentVariable("PSX_HD", hdWas);
                System.Environment.SetEnvironmentVariable("PSX_HD_SRC", srcWas);
                TextureHDMap.AsphaltNewGain = gainWas;
                File.WriteAllText(Path.Combine(dir, "tex4_log.txt"), log.ToString());
                Debug.Log("[Tex4]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
