using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PSXRacing.City;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// ROAD LOGIC, JUNCTIONS: the driver's view of a junction's control before
    /// and after (CitySignals.Control). For each target - PSX_JUNCTION_SHOTS
    /// "x,z,label;x,z,label", else Clanton Road at the I-77 ramps (two crossing
    /// approaches green together before) and Colony Road at Carnegie Boulevard
    /// (no control before) - the junction there now, its main approach A and a
    /// crossing one B, a clock time when A has had its green 5 s, and from
    /// 22 m short of each stop line at a driver's eye: BEFORE (the old
    /// two-group clock, OSM's controls only: PSX_CITY_LEGACY_PHASES /
    /// PSX_CITY_WARRANTS=0) and AFTER, at that same clock time. Screenshots to
    /// Screenshots/City/junctions with junctions.txt (each head's aspect).
    ///   -executeMethod PSXRacing.EditorTools.CityRefSpots.RunJunctionShots
    /// </summary>
    public static partial class CityRefSpots
    {
        struct JShot { public string label; public Vector2 at; public Vector3 eyeA, eyeB, lookA, lookB; public double t; public bool ok; public string note; }

        public static void RunJunctionShots()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityRefSpots] no city data"); return; }
            string dir = Path.Combine(ProjectRoot(), "Screenshots", "City", "junctions");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            string txt = Path.Combine(dir, "junctions.txt");
            var log = new StringBuilder();
            var targets = new List<(Vector2 p, string label)>();
            var env = System.Environment.GetEnvironmentVariable("PSX_JUNCTION_SHOTS");
            if (!string.IsNullOrEmpty(env))
                foreach (var part in env.Split(';'))
                {
                    var q = part.Split(',');
                    if (q.Length >= 2 && float.TryParse(q[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                        && float.TryParse(q[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                        targets.Add((new Vector2(x, z), q.Length > 2 ? q[2] : $"j{targets.Count}"));
                }
            if (targets.Count == 0)
            {
                targets.Add((new Vector2(-5832f, 1433f), "clanton_i77"));
                targets.Add((new Vector2(-393f, -3680f), "colony_carnegie"));
            }
            PSXRacingBuilder.EnsureCityTextures();
            var go = new GameObject("~cityJunctionShots");
            var world = go.AddComponent<CityWorld>();
            bool warrants0 = CitySignals.WarrantsOn, legacy0 = CitySignals.LegacyPhases;
            try
            {
                world.EnsureRing(new Vector3(targets[0].p.x, 0f, targets[0].p.y), 0);
                world.DropAll();
                var trims = world.NodeTrims;
                DayLight();
                NightGlow.PreviewAll(false);

                // ---- AFTER's junctions: the eyes and the clock time
                CitySignals.LegacyPhases = false; CitySignals.WarrantsOn = true; CitySignals.Invalidate();
                var shots = new List<JShot>();
                foreach (var (p, label) in targets)
                {
                    var s = new JShot { label = label, at = p };
                    var j = CitySignals.All(map, trims).Where(q => q.approaches.Count >= 2).OrderBy(q => (q.centre - p).sqrMagnitude).FirstOrDefault();
                    if (j == null || Vector2.Distance(j.centre, p) > 60f) { s.note = "no junction there"; shots.Add(s); continue; }
                    var A = j.approaches.OrderBy(a => a.group).ThenByDescending(a => map.edges[a.edge].cls).First();
                    var B = j.approaches.Where(a => Mathf.Abs(Vector2.Dot(a.u, A.u)) < 0.64f).OrderByDescending(a => map.edges[a.edge].cls).FirstOrDefault();
                    if (B.edge == 0 && B.node == 0) B = j.approaches.First(a => a.edge != A.edge);
                    s.t = j.signal ? CitySignals.GreenTime(A.channel, 5f) : 5.0;
                    s.eyeA = Eye(A, out s.lookA, j); s.eyeB = Eye(B, out s.lookB, j);
                    s.at = j.centre;
                    s.ok = true;
                    s.note = $"junction {j.id} ({(j.signal ? $"signal, {j.phases} phases, plan {CitySignals.Plans[Mathf.Max(0, j.plan)].key}" : j.allWay ? "all-way STOP" : "STOP")}{(j.warrant ? ", by warrant" : ", OSM")}) " +
                             $"A '{map.edges[A.edge].name}' e{A.edge} phase {A.group}, B '{map.edges[B.edge].name}' e{B.edge} phase {B.group}, clock {s.t:0.0} s";
                    shots.Add(s);
                }
                foreach (var mode in new[] { "before", "after" })
                {
                    CitySignals.LegacyPhases = mode == "before"; CitySignals.WarrantsOn = mode == "after"; CitySignals.Invalidate();
                    world.DropAll();
                    foreach (var s in shots)
                    {
                        log.AppendLine($"{mode} {s.label}: {s.note}");
                        if (!s.ok) continue;
                        world.EnsureRing(new Vector3(s.at.x, 0f, s.at.y), 1);
                        CitySignals.Tick(s.t);
                        var j = CitySignals.All(map, trims).Where(q => q.approaches.Count >= 2).OrderBy(q => (q.centre - s.at).sqrMagnitude).FirstOrDefault();
                        if (j != null && Vector2.Distance(j.centre, s.at) < 40f)
                            foreach (var a in j.approaches)
                                log.AppendLine($"    e{a.edge} '{map.edges[a.edge].name}' {(j.signal ? $"phase {a.group} channel {a.channel}: {CitySignals.AspectOf(a.channel, s.t)}" : a.ctl == 2 ? "STOP" : "yield")}");
                        else log.AppendLine("    no control here");
                        Shoot(dir, $"{s.label}_{mode}_A", s.eyeA, Quaternion.LookRotation(s.lookA - s.eyeA), 0f, 960, 540);
                        Shoot(dir, $"{s.label}_{mode}_B", s.eyeB, Quaternion.LookRotation(s.lookB - s.eyeB), 0f, 960, 540);
                        world.DropAll();
                    }
                }
                log.AppendLine(CitySignals.ControlReport());
            }
            catch (System.Exception e)
            {
                log.AppendLine("RUN FAILED: " + e);
                Debug.LogException(e);
            }
            finally
            {
                CitySignals.WarrantsOn = warrants0; CitySignals.LegacyPhases = legacy0; CitySignals.Invalidate();
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
                File.WriteAllText(txt, log.ToString());
                Debug.Log("[CityRefSpots] junctions:\n" + log);
            }
        }

        /// <summary>A driver's eye 22 m short of approach a's stop line in its
        /// inbound lanes, looking at the junction a little above the heads.</summary>
        static Vector3 Eye(CitySignals.Approach a, out Vector3 look, CitySignals.Junction j)
        {
            var p = a.at - a.u * 22f + a.Right * ((a.inL + a.inR) * 0.5f);
            var eye = new Vector3(p.x, a.roadY + 1.15f, p.y);
            var q = a.at + a.u * 10f;
            look = new Vector3(q.x, (j.signal ? a.roadY + 4.2f : a.roadY + 1.6f), q.y);
            return eye;
        }
    }
}
