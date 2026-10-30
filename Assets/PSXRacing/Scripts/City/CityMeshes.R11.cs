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
        public static bool[] Round11Flags() => new bool[0];
        public static void SetRound11Flags(bool[] f) { }

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
