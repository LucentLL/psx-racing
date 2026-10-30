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
            // (signs round 2) PSX_HSIGN_BOTH=1: the census with round 1's signs
            // first (every PSX_CITY_SIGNS2 switch off: city_hsigns_r1.txt), then
            // round 2's (city_hsigns.txt) - the before and after in one run
            if (System.Environment.GetEnvironmentVariable("PSX_HSIGN_BOTH") == "1")
            {
                CitySigns.SetSigns2(false);
                Census("city_hsigns_r1.txt");
                CitySigns.SetSigns2(true);
            }
            Census("city_hsigns.txt");
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

        static void Census(string outName)
        {
            var log = new System.Text.StringBuilder();
            string outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, outName);
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
                    if (what.StartsWith("ExitGantry")) lines.Add($"  ({at.x:0},{at.y:0}) {(ok ? "stands" + what.Substring(10) + " face at (" + face.x.ToString("0.0") + "," + face.y.ToString("0.0") + "," + face.z.ToString("0.0") + ") " : "NO ROOM (" + why + ")")} {legend}");
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

                // ---- signs round 2: the structures, their density, the new signs
                var s2 = CitySigns.Signs2Stats(map, trims, buildings);
                float fwyKm = s2.freewayCarriagewayKm * 0.5f;   // divided freeway: two carriageways a km of route
                int overheadStructures = s2.signBridges + s2.cantilevers;
                log.AppendLine($"SIGNS ROUND 2 (switches: legs {CitySigns.S2Legs}, glyph {CitySigns.S2Glyph}, density {CitySigns.S2Density}, bridge {CitySigns.S2Bridge}, curves {CitySigns.S2Curves}, exit-only {CitySigns.S2ExitOnly}, night {CitySigns.S2Night}, gores {CitySigns.S2Gores})");
                log.AppendLine($"  freeway: {s2.freewayCarriagewayKm:0.0} km of one-way carriageway = {fwyKm:0.0} km of route");
                log.AppendLine($"  OVERHEAD STRUCTURES {overheadStructures} ({s2.signBridges} sign bridges, {s2.cantilevers} cantilevers) = {overheadStructures / Mathf.Max(1f, fwyKm):0.00} per km of freeway (NCDOT ~1.01); with the {s2.bridgeMounted} bridge-mounted signs {(overheadStructures + s2.bridgeMounted) / Mathf.Max(1f, fwyKm):0.00} per km");
                log.AppendLine($"  advance signs: ground-mounted {s2.groundAdvance}, hung on another exit's sign bridge {s2.shared} ({s2.sharedHosts} hosts); left out by MUTCD 2E.33 (closer than a mile: the 1 MILE sign {s2.droppedMile}; closer than 300 m: no advance sign {s2.droppedHalf}; a minor interchange's 1 MILE sign {s2.droppedMinor})");
                log.AppendLine($"  EXIT ONLY panels {s2.exitOnlyPanels} (down arrows on the advance signs {s2.exitOnlyArrows})");
                log.AppendLine($"  curve warnings {s2.curveSigns} ({s2.curveSignsLeft} supplementary on the left), refused {s2.curveRefused}; chevrons {s2.chevrons}, refused {s2.chevronRefused}");
                log.AppendLine($"    CityCurves spans looked at {s2.spansSeen}: signed {s2.spansSigned}, under 10 mph below the limit {s2.spansSmall}, not a curve (a kink, a junction's turn) {s2.spansKink}, the exit-speed sign's own curve {s2.spansCovered}, one curve carried on across a node {s2.spansCarried}, a second sign at one sign's place {s2.spansTwin}");
                log.AppendLine($"  gore signs standing {s2.goresPlaced}; on the PAINTED gore (gap under {CityMeshes.GorePaintedGapM:0.0} m) {s2.goresOnPaint}");
                log.AppendLine($"  sign-lighting luminaires (halo dots at night) {s2.lampHeads}");

                // ---- the layout check: every face's mosaic as built (no legend clips, no layer over another)
                var lay = CitySigns.LayoutCheck(map, trims, buildings, out int fChecked, out int fClip, out int fOver, out int fTwin);
                log.AppendLine($"LAYOUT CHECK: {fChecked} faces, legends clipping {fClip}, quads overlapping in a face {fOver}, two signs in one plane {fTwin}" + (fClip + fOver + fTwin == 0 ? " - OK" : " - FAIL"));
                foreach (var l in lay) log.AppendLine(l);
                // ---- eye views onto the new signs (CityPreview PSX_EYE_LOOKS), nearest uptown first
                log.AppendLine("SHOT SPOTS (PSX_EYE_LOOKS lines):");
                foreach (var l in CitySigns.ShotSpots(map, trims, buildings, new Vector2(-2500f, 5200f), 4)) log.AppendLine("  " + l);
                // ---- the glyph probe: what stands within a metre and a half of I-85 exit 36's gore sign
                string probe = System.Environment.GetEnvironmentVariable("PSX_HSIGN_PROBE") ?? "-5760.5,130.2,8388.2";
                var pv = probe.Split(',');
                if (pv.Length == 3)
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    var at = new Vector3(float.Parse(pv[0], ci), float.Parse(pv[1], ci), float.Parse(pv[2], ci));
                    log.AppendLine($"GLYPH PROBE at ({at.x:0.0},{at.y:0.0},{at.z:0.0}):");
                    foreach (var l in CitySigns.QuadProbe(map, trims, buildings, at, 1.5f)) log.AppendLine(l);
                }

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
                Debug.Log("[HSigns] " + outName + ": " + log.ToString().Split('\n')[0]);
            }
        }
    }
}
