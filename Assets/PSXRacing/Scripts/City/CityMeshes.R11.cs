using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// REAL HEIGHTS round 11 (2026-10-30): the tile builder's round-11 rules and their debug
    /// traces. See Docs/CHARLOTTE.md "REAL HEIGHTS - round 11".
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>(round 11) The tile builder's round-11 switches as one set (the look tool's A/B).</summary>
        public static bool[] Round11Flags() => new[] { R11GoreStartOn };
        public static void SetRound11Flags(bool[] f)
        {
            if (f == null) return;
            if (f.Length > 0) R11GoreStartOn = f[0];
        }

        // ------------------------------------------------------------------
        //  GORESTART (round 10's BLUNT, I-277 e9902 s 18): an exit ramp's
        //  (or any branch's) outer rail that starts where its ribbon comes out
        //  of its host's pavement. OSM joins a ramp at the end of its taper, so
        //  its first metres are drawn collapsed onto the host's edge; the
        //  approach rail it carries (a structure ahead) began on the first span
        //  past that zone - ON the host's edge line, square to the host's
        //  traffic in its outside lane. The rail-end plan skipped any end whose
        //  open span was a collapsed (skip / wedge) span, so nothing turned it.
        //  DOT: no blunt end faces traffic; here there is no roadside to lead a
        //  W-beam in along (the host's lanes lie past it), so the run's own first
        //  TaperLenM is TURNED DOWN - sloped up from a curb's height to full, the
        //  Roadside Design Guide's sloped end - with no drop exception (what the
        //  approach rail guards lies ahead of it). PSX_CITY_R11_GORESTART=0: before.
        // ------------------------------------------------------------------
        public static bool R11GoreStartOn = System.Environment.GetEnvironmentVariable("PSX_CITY_R11_GORESTART") != "0";
        const int EndGoreStart = 4;
        /// <summary>(GORESTART) rail starts turned down, tile builds since load (the probe prints it).</summary>
        public static int R11GoreStarts;
        /// <summary>Is this edge a branch at either end (clipped onto a host)?</summary>
        static bool R11BranchEdge(Trims trims, CityMap.Edge e) => trims.branchA[e.index] >= 0 || trims.branchB[e.index] >= 0;

        /// <summary>(round 11, STREETSEAT) Every STREET branch end's seat where its ribbon runs
        /// INTO its host's pavement (the ramps' rule, <see cref="SeatIntoOnly"/>): the solve seats
        /// it there as it seats a ramp (CityElevation.PrepareSeats).</summary>
        public static List<Seat> StreetIntoSeats(CityMap map, Trims trims)
        {
            var result = new List<Seat>();
            for (int ei = 0; ei < map.edges.Length; ei++)
                for (int end = 0; end < 2; end++)
                {
                    int hostIdx = end == 0 ? trims.branchA[ei] : trims.branchB[ei];
                    if (hostIdx < 0) continue;
                    var L = map.edges[ei];
                    if (L.link || L.a == L.b) continue;
                    var seat = SeatOf(map, trims, L, map.edges[hostIdx], end == 0 ? L.a : L.b, true);
                    if (seat != null) result.Add(seat);
                }
            return result;
        }

        /// <summary>(round 11, the launch audit's EXPLAIN) A fan's interior points (FanSteiner: cluster
        /// members, the major pair's pavement, the arms' lines) as "(x,z) y" in world space.</summary>
        public static string R11FanPoints(CityMap map, Trims trims, int n)
        {
            if (!trims.patch[n]) return "";
            var pts = new List<Vector3>(32);
            FanSteiner(map, trims, FanKey(trims, n), Vector3.zero, pts);
            var sb = new System.Text.StringBuilder(" steiner:");
            foreach (var p in pts) sb.Append($" ({p.x:0.0},{p.z:0.0}) {p.y - FanProudM:0.00};");
            FanCentre(map, trims, FanKey(trims, n), out var c, out float cy);
            sb.Append($" centre ({c.x:0.0},{c.y:0.0}) {cy:0.00}");
            return sb.ToString();
        }

        // ---- debug: the rail-end plan on one edge -------------------------------------
        /// <summary>PSX_R11_RAILTRACE=edge: every barrier kind change the rail-end plan sees
        /// on that edge (each side), with the flags that decide whether it is planned
        /// (the editor's CityR11 probe prints <see cref="R11RailTrace"/>).</summary>
        public static readonly int R11RailTraceEdge =
            int.TryParse(System.Environment.GetEnvironmentVariable("PSX_R11_RAILTRACE"), out int r11rte) ? r11rte : -1;
        public static readonly List<string> R11RailTrace = new List<string>();

        static string R11RailTraceLine(CityMap.Edge e, TileMeshes tm, int side, int k, int n, int kL, int kR)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string Span(int i)
            {
                if (i < 1 || i >= n) return "-";
                var f = spanFlags[i]; var sf = f[side];
                return (f.decided ? "D" : "d") + (f.elev ? "E" : "") + (f.wedge ? "W" : "") + (f.skip ? "S" : "") + (f.approach ? "A" : "")
                       + (sf.rail ? "r" : "") + (sf.cut ? "c" : "") + (sf.median ? "m" : "") + (sf.gap ? "g" : "") + (sf.union ? "u" : "") + (sf.retain ? "t" : "");
            }
            var sec = sections[k];
            var p = (side < 0 ? sec.L : sec.R) + tm.origin;
            return string.Format(ci, "tile {0:0},{1:0} e{2} side {3} k {4}/{5} s {6:0.00} at ({7:0.0},{8:0.00},{9:0.0}) kinds {10}->{11} spans [{12}] [{13}] sec{14}{15}{16} strip {17:0.00}",
                tm.origin.x / TileSize, tm.origin.z / TileSize, e.index, side, k, n, sec.s, p.x, p.y, p.z, kL, kR, Span(k), Span(k + 1),
                sec.collapsed ? " collapsed" : "", sec.zoneEnd ? " zoneEnd" : "", sec.elev ? " elev" : "", sec.Strip(side));
        }
    }
}
