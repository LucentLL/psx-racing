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
    /// THE LOT ENTRANCE PICTURES (lot entrances, 2026-11-15): the same camera
    /// poses BEFORE (the pass's lots, entrances, corridors, ramps and stall
    /// cuts hidden: CityMeshes.LotEntSet(false)) and AFTER, the tiles built
    /// again between. Places: PSX_LOTSHOT_SITES, '|' separated
    /// "label;edge;s;side" (CityLotDrive's SITES line: a street place and the
    /// side its lot is on). Poses: "eye" - the driver's eye in the near lane
    /// 16 m before the entrance, looking at it; "square" - from the far side
    /// of the street, square on to the entrance; "in" - from over the lot,
    /// looking back out at the street.
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunLots
    /// Output: Screenshots/lots/&lt;n&gt;_&lt;pose&gt;_&lt;before|after&gt;.png, lots_log.txt.
    /// </summary>
    public static partial class CityRefSpots
    {
        public static void RunLots()
        {
            var I = CultureInfo.InvariantCulture;
            var log = new StringBuilder("LOT ENTRANCE SHOTS " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "lots");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            int code = 0, shots = 0;
            try
            {
                var def = TrackCatalog.At(TrackCatalog.IndexOf("Charlotte"));
                if (def.id != "Charlotte" || !PSXScreenshotTool.Open(def, out var cam, out var player)) throw new System.Exception("Charlotte did not open");
                var output = Object.FindAnyObjectByType<PSXCameraOutput>();
                if (output != null) output.height = TexShotHeight;
                TexturePrefs.Enhanced = true;
                var world = Object.FindAnyObjectByType<CityWorld>();
                var map = CityMap.Get();
                if (world == null || map == null) throw new System.Exception("no CityWorld / city data");
                var sun = NightLookShots.FindSun();
                var car = player.GetComponent<CarController>();
                string env = System.Environment.GetEnvironmentVariable("PSX_LOTSHOT_SITES");
                if (string.IsNullOrEmpty(env)) throw new System.Exception("PSX_LOTSHOT_SITES not set");
                int n = 0;
                var flick = new StringBuilder();
                foreach (var spec in env.Split('|'))
                {
                    var f = spec.Split(';');
                    if (f.Length >= 2 && f[1].StartsWith("@"))
                    {
                        // by place: "label;@x,z" - the entrance whose mouth is nearest
                        var c = f[1].Substring(1).Split(',');
                        CityMeshes.LotEntSet(map, true);
                        int en = CityMeshes.LotEntranceNear(map, new Vector2(float.Parse(c[0], I), float.Parse(c[1], I)), out _);
                        if (en < 0) continue;
                        var E = map.lotEntrances[en];
                        f = new[] { f[0], E.edge.ToString(I), E.s.ToString("0.0", I), ((int)E.side).ToString(I) };
                    }
                    if (f.Length < 4) continue;
                    var e = map.edges[int.Parse(f[1], I)];
                    float s = Mathf.Clamp(float.Parse(f[2], I), 0f, e.length);
                    int side = int.Parse(f[3], I);
                    var p = e.PointAt(s); var t = e.TangentAt(s);
                    var left = LineModel.SectionLeft(e, s);
                    LineModel.Extents(e, s, out float eMinus, out float ePlus);
                    var nrm = left * side;
                    var mouth = p + nrm * (side > 0 ? ePlus : eMinus);
                    float y = e.YAt(s);
                    // the lot's asphalt along the line (as the pass has it)
                    CityMeshes.LotEntSet(map, true);
                    float gap = 3f;
                    for (float d = 0f; d <= 62f; d += 0.25f)
                    {
                        int li = CityMeshes.LotPavedForTools(map, mouth + nrm * d);
                        if (li >= 0 && !map.lots[li].aisleOnly) { gap = d; break; }
                    }
                    // right-hand traffic: the lane beside this edge runs with the lot on its right
                    var fwd2 = t * -side;
                    Vector3 M = new Vector3(mouth.x, y, mouth.y), N = new Vector3(nrm.x, 0f, nrm.y), F = new Vector3(fwd2.x, 0f, fwd2.y);
                    var poses = new List<(string name, Vector3 eye, Quaternion r)>();
                    Vector3 eye = M - N * 1.8f - F * 16f + Vector3.up * 1.15f;
                    poses.Add(("eye", eye, Quaternion.LookRotation(M + N * (gap * 0.5f + 1.5f) + Vector3.up * 0.2f - eye)));
                    Vector3 sq = M - N * Mathf.Min(e.width - 0.8f, 9f) + Vector3.up * 1.5f - F * 2.5f;
                    poses.Add(("square", sq, Quaternion.LookRotation(M + N * (gap + 2f) - sq)));
                    Vector3 inl = M + N * (gap + 9f) + F * 3f + Vector3.up * 2.6f;
                    poses.Add(("in", inl, Quaternion.LookRotation(M + N * (gap * 0.3f) - inl)));
                    log.Append(string.Format(I, "{0}\t{1}\te{2} '{3}' cls {4} s {5:0} side {6} walk {7}; mouth ({8:0.0}, {9:0.0}); the lot's asphalt {10:0.0} m from the pavement\n",
                        n, f[0], e.index, e.name, e.cls, s, side, CityMeshes.WalkSide(map, e, side), mouth.x, mouth.y, gap));
                    {
                        // the same view for the flicker finder (PSX_FLICK_EXTRA): the eye's place, looking at the entrance
                        var fa = M - N * 1.8f - F * 12f; var fd = (F * 12f + N * (1.8f + gap * 0.5f + 2f)).normalized;
                        flick.Append(flick.Length > 0 ? "|" : "").Append(string.Format(I, "lot{0};{1:0.0};{2:0.0};{3:0.000};{4:0.000}", n, fa.x, fa.z, fd.x, fd.z));
                    }
                    var pos = M - N * 1.8f - F * 60f + Vector3.up * 0.35f;
                    var rot = Quaternion.LookRotation(F, Vector3.up);
                    foreach (var mode in new[] { "after", "before" })
                    {
                        CityMeshes.LotEntSet(map, mode == "after");
                        world.DropAll();
                        world.EnsureRing(M, 1);
                        if (car != null) car.TeleportTo(pos, rot); else player.transform.SetPositionAndRotation(pos, rot);
                        RaceHandoff.CalendarDay = 0;
                        RaceHandoff.WeatherOverride = -1;
                        int hour = HourOf("noon");
                        TimeOfDay.Apply(hour, sun);
                        var globals = Object.FindAnyObjectByType<PSXGlobals>();
                        if (globals != null) globals.Apply();
                        NightGlow.PreviewAll(TimeOfDay.At(hour).lightsOn);
                        PSXTexDecode.StampAll();
                        foreach (var ps in poses)
                        {
                            CarLights.PushGlobals();
                            PSXScreenshotTool.ShotAs(cam, $"lots/{n}_{ps.name}_{mode}", ps.eye, ps.r);
                            shots++;
                        }
                    }
                    n++;
                }
                CityMeshes.LotEntSet(map, true);
                log.Append("FLICK " + flick + "\n");
                log.Append($"{shots} frames to {dir}\nLOT ENTRANCE SHOTS OK\n");
            }
            catch (System.Exception ex) { log.Append("LOT ENTRANCE SHOTS FAILED: " + ex + "\n"); Debug.LogException(ex); code = 1; }
            finally
            {
                RaceHandoff.WeatherOverride = -1;
                File.WriteAllText(Path.Combine(dir, "lots_log.txt"), log.ToString());
                Debug.Log("[LotShots]\n" + log);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
