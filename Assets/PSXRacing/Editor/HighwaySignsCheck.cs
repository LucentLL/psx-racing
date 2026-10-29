using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE HIGHWAY SIGNS CHECK (the highway signs pass, 2026-10-29): every
    /// exit's guide-sign sites decided from the graph as the game decides
    /// them (CitySigns.GuideCensus), counted by kind - how many stand, how
    /// many found no room - with each exit's legend as the map words it; every
    /// legend's letters checked against the guide atlas's face (a letter the
    /// face does not have would set as a gap); then the sign audit
    /// (CityAudit.RunSigns) and, with PSX_HSIGN_SHOTS=1, the "hsigns" views
    /// (CityPreview.RunEyePlay). Writes city_hsigns.txt at the project root.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.HighwaySignsCheck.Run
    /// (tools\hsign-shots.ps1 -Check).
    /// </summary>
    public static class HighwaySignsCheck
    {
        public static void Run()
        {
            var log = new System.Text.StringBuilder();
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_hsigns.txt");
            try
            {
                var map = CityMap.Get();
                if (map == null) { log.AppendLine("HSIGNS: no city data"); return; }
                var trims = CityMeshes.NodeTrims(map);
                var buildings = CityBuildings.Precompute(map);
                var placed = new Dictionary<string, int>(); var tried = new Dictionary<string, int>();
                var lines = new List<string>();
                int quads = 0, faces = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var whys = new Dictionary<string, int>();
                var perTile = new Dictionary<long, int>();
                var small = new List<string>();
                CitySigns.GuideCensus(map, trims, buildings, (what, ok, at, legend, q, f, face, why) =>
                {
                    tried.TryGetValue(what, out int t); tried[what] = t + 1;
                    if (ok)
                    {
                        placed.TryGetValue(what, out int p); placed[what] = p + 1; quads += q; faces += f;
                        long tk = ((long)Mathf.FloorToInt(at.x / CityMeshes.TileSize) << 32) ^ (uint)Mathf.FloorToInt(at.y / CityMeshes.TileSize);
                        perTile.TryGetValue(tk, out int pq); perTile[tk] = pq + q;
                    }
                    else { string k = what + ": " + why; whys.TryGetValue(k, out int w); whys[k] = w + 1; }
                    if (what == "ExitGantry") lines.Add($"  ({at.x:0},{at.y:0}) {(ok ? "stands " : "NO ROOM (" + why + ")")} {legend}");
                    else if (ok) small.Add($"  {what} of the exit at ({at.x:0},{at.y:0}): face at ({face.x:0.0},{face.y:0.0},{face.z:0.0})");
                });
                log.AppendLine($"HIGHWAY SIGNS CENSUS ({clock.Elapsed.TotalSeconds:0.0} s): sites by kind, standing / decided");
                foreach (var kv in tried)
                {
                    placed.TryGetValue(kv.Key, out int p);
                    log.AppendLine($"  {kv.Key,-18} {p,4} / {kv.Value,4}");
                }
                log.AppendLine($"  guide-atlas quads {quads} on {faces} faces (all exits; a tile draws its own few)");
                int maxQ = 0; long sumQ = 0;
                foreach (var v in perTile.Values) { maxQ = Mathf.Max(maxQ, v); sumQ += v; }
                log.AppendLine($"  per tile with guide signs ({perTile.Count} tiles of {CityMeshes.TileSize:0} m): mean {(perTile.Count > 0 ? sumQ / perTile.Count : 0)} quads ({(perTile.Count > 0 ? 4 * sumQ / perTile.Count : 0)} vertices), most {maxQ} quads ({4 * maxQ} vertices); one more draw call on each");
                log.AppendLine("WHY A SITE FOUND NO ROOM:");
                foreach (var kv in whys) log.AppendLine($"  {kv.Key}: {kv.Value}");

                // the legends against the face: every letter must have a cell
                int exits = CitySignData.Exits.Length, real = 0, fallback = 0, none = 0, numbered = 0, routed = 0, adv = 0, thruCity = 0, missing = 0;
                var missingChars = new HashSet<char>();
                foreach (var ex in CitySignData.Exits)
                {
                    if (!string.IsNullOrEmpty(ex.number)) numbered++;
                    if (!string.IsNullOrEmpty(ex.route)) routed++;
                    if (ex.advisory > 0) adv++;
                    if (!string.IsNullOrEmpty(ex.thruCity)) thruCity++;
                    if (!string.IsNullOrEmpty(ex.dest1)) { if ((ex.flags & 4) != 0) fallback++; else real++; }
                    else if (string.IsNullOrEmpty(ex.route)) none++;
                    foreach (var str in new[] { ex.number, ex.route, ex.dest1, ex.dest2, ex.cardinal, ex.thru, ex.thruCity })
                        foreach (char c in str ?? "")
                            if (c != ' ' && CityGuideFont.Green.IndexOf(c) < 0) { missing++; missingChars.Add(c); }
                }
                log.AppendLine($"LEGENDS: {exits} exits; numbered {numbered}; a route {routed}; destinations from the map {real}, from a fallback {fallback}, nothing to say {none}; posted advisories {adv}; pull-throughs with a control city {thruCity}");
                log.AppendLine(missing == 0 ? "LEGEND FACE OK: every letter of every legend has its cell"
                                            : $"LEGEND FACE: {missing} letters with no cell ({new string(new List<char>(missingChars).ToArray())}) - they set as gaps");
                log.AppendLine("EXIT DIRECTION SIGNS:");
                foreach (var l in lines) log.AppendLine(l);
                log.AppendLine("SIGNS AND STRUCTURES BESIDE THEM (faces):");
                foreach (var l in small) log.AppendLine(l);
            }
            catch (System.Exception ex) { log.AppendLine("HSIGNS EXCEPTION " + ex); Debug.LogException(ex); }
            finally
            {
                File.WriteAllText(outPath, log.ToString());
                Debug.Log("[HSigns] " + log.ToString().Split('\n')[0]);
            }
            if (System.Environment.GetEnvironmentVariable("PSX_HSIGN_AUDIT") != "0")
            {
                CityAudit.RunSigns();
                string audit = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "city_sign_audit.txt");
                if (File.Exists(audit))
                {
                    var tail = File.ReadAllLines(audit);
                    Debug.Log("[HSigns] sign audit: " + (tail.Length > 0 ? tail[tail.Length - 1] : "(empty)"));
                }
            }
            if (System.Environment.GetEnvironmentVariable("PSX_HSIGN_SHOTS") == "1") CityPreview.RunEyePlay();
        }
    }
}
