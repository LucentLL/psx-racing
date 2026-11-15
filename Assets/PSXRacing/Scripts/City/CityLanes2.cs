using System.Collections.Generic;
using System.IO;

namespace PSXRacing.City
{
    /// <summary>
    /// LANES2 (2026-11-15, the owner: "the city and roads should be the newest
    /// 2026 version"): today's lane counts where the map has none (the class
    /// default: a secondary road drawn two lanes where the real one has four)
    /// or a wrong one, from street-level views of each road.
    ///
    /// The table is tools/city/lanes2_overrides.tsv (way, lanes, the evidence).
    /// The exporter reads it only with PSX_EXPORT_LANES2=1 and designs the
    /// tapers, ribbon offsets and line sets for those counts as for any tagged
    /// way; tools/city/lanes2.mjs splices what that export does differently into
    /// charlotte_city.bytes as section LAN2 and leaves every other section byte
    /// for byte (the graph, its hash and every (edge, s) key stay). CityMap.Parse
    /// applies it: a changed edge's lane count, centre-lane flag and line set,
    /// the whole taper list and the split table (a split's offsets follow the lanes).
    ///
    ///   LAN2  u32 version (1) | u32 n | n x { u32 edge; u8 lanes; u8 turn; u8 nF;
    ///         u8 nB; u8 centre; u8 lsFlags; u16 turnOnly; u8 sub; u8 bike; u16 parking }
    ///         | TAPR's layout: the taper records and ribbon offsets for these counts
    ///         | SPLT's layout: the carriageway splits with their offsets for these counts
    ///
    /// PSX_CITY_LANES2=0: the section is not read - the roads as the tags alone give them.
    /// </summary>
    public static class CityLanes2
    {
        public static readonly bool On = System.Environment.GetEnvironmentVariable("PSX_CITY_LANES2") != "0";

        public struct Row { public byte lanes; public bool turn; public byte nF, nB, centre, lsFlags; public ushort turnOnly; public byte sub, bike;
            /// <summary>On-street parking (CityMeshes.Parking): bit 0 a parking lane left of a->b, bit 1 right.</summary>
            public ushort park; }

        /// <summary>The rows of the map last parsed, by edge (null: no LAN2 section, or switched off).</summary>
        public static Dictionary<int, Row> Rows { get; private set; }
        public static bool Applied(int edge) => Rows != null && Rows.ContainsKey(edge);
        public static string Report => Rows == null ? (On ? "LANES2: no LAN2 section in the data" : "LANES2: OFF (PSX_CITY_LANES2=0)") : $"LANES2: {Rows.Count} edges take today's lane count from section LAN2";

        /// <summary>Section LAN2's edge rows; leaves the reader at the taper list that follows them.</summary>
        public static Dictionary<int, Row> ReadRows(BinaryReader r)
        {
            uint version = r.ReadUInt32();
            if (version != 1) throw new System.Exception("charlotte_city.bytes: LAN2 version " + version + " (this reader knows 1)");
            int n = r.ReadInt32();
            var rows = new Dictionary<int, Row>(n);
            for (int i = 0; i < n; i++)
            {
                int edge = r.ReadInt32();
                var row = new Row { lanes = r.ReadByte(), turn = r.ReadByte() != 0, nF = r.ReadByte(), nB = r.ReadByte(), centre = r.ReadByte(), lsFlags = r.ReadByte(), turnOnly = r.ReadUInt16(), sub = r.ReadByte(), bike = r.ReadByte() };
                row.park = r.ReadUInt16();
                rows[edge] = row;
            }
            return rows;
        }

        public static void Loaded(Dictionary<int, Row> rows) { Rows = rows; }
    }
}
