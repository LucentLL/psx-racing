using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ON-STREET PARKING LANES (2026-11-15, the lanes round: "streets whose
    //  curb-to-curb width is more than their travel lanes because cars park
    //  along them" - Camden Rd, East Blvd, the Uptown and South End streets
    //  were drawn at the width of their two travel lanes, 7.9 m, where the
    //  real street is 11-13 m between its curbs).
    //
    //  A parking lane is PAVEMENT beside the travel lanes: the curb, the gutter
    //  and the walk move out by its width; no traffic lane is added, the lane
    //  lines stay where they are (the street's own edge line, where it has one,
    //  is then the parking lane's line: MUTCD 3B.19), traffic and the race line
    //  never see it. It is a WIDEN ease of the line model on the street's side
    //  (the slip lanes' curve widening is the same kind: LineModel.Ext adds it
    //  to the pavement's extents only), full width along the block and eased
    //  to nothing before each junction - no parking within ParkClearM of the
    //  corner (NC G.S. 20-162: 25 ft), which also leaves every junction's own
    //  shape exactly as it was.
    //
    //  WHICH STREETS: section LAN2's rows (CityLanes2.Row.park: bit 0 the left
    //  of a->b, bit 1 the right), written by tools/city/lanes2.mjs from the
    //  map's own parking tags and from tools/city/parking2.tsv (streets seen
    //  in today's street-level views). No rule by district yet: a street
    //  with no evidence keeps its width.
    //  PSX_CITY_PARKING=0: no parking lane anywhere (and none without LAN2).
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool ParkingOn = System.Environment.GetEnvironmentVariable("PSX_CITY_PARKING") != "0";
        /// <summary>A parallel parking lane, to the gutter's edge (CLDSM: 7 ft plus the gutter pan's share).</summary>
        public const float ParkLaneM = 2.3f;
        /// <summary>No parking this near a junction's corner (past the ribbon's own trim), and the ease into the lane.</summary>
        public const float ParkClearM = 6f, ParkEaseM = 5f;
        /// <summary>The shortest full-width run worth drawing (two cars).</summary>
        public const float ParkMinRunM = 11f;
        public static int ParkingEdges; public static float ParkingSideM;
        public static string ParkingReport => !ParkingOn ? "parking lanes OFF (PSX_CITY_PARKING=0)" : $"parking lanes: {ParkingEdges} edges, {ParkingSideM / 1000f:0.0} km of curb side";

        /// <summary>After the line model's eases are built (ComputeTrimsCore): the parking lanes' widen eases.</summary>
        static void BuildParkingLanes(CityMap map, Trims t)
        {
            ParkingEdges = 0; ParkingSideM = 0f;
            var rows = CityLanes2.Rows;
            if (!ParkingOn || rows == null) return;
            foreach (var kv in rows)
            {
                int park = kv.Value.park & 3;
                if (park == 0 || kv.Key < 0 || kv.Key >= map.edges.Length) continue;
                var e = map.edges[kv.Key];
                if (e.a == e.b || e.link || e.bridge || e.tunnel || e.cls >= 4) continue;
                float a0 = t.atA[e.index] + ParkClearM, b0 = t.atB[e.index] + ParkClearM;
                float run = e.length - a0 - b0 - 2f * ParkEaseM;
                if (run < ParkMinRunM) continue;
                const float ds = 1f;
                int n = Mathf.FloorToInt(e.length / ds) + 1;
                var prof = new float[n + 1];
                for (int i = 0; i < n; i++)
                {
                    float s = i * ds;
                    float f = Mathf.Min(LineModel.Smooth((s - a0) / ParkEaseM), LineModel.Smooth((e.length - s - b0) / ParkEaseM));
                    prof[i] = ParkLaneM * f;
                }
                prof[n] = 0f;
                bool any = false;
                for (int side = -1; side <= 1; side += 2)
                {
                    // the left of a->b is the line model's plus side
                    if ((park & (side > 0 ? 1 : 2)) == 0) continue;
                    // a one-way carriageway of a divided road parks on its outer (right) side only
                    if (e.oneway && side > 0 && CurbMedianClass(e)) continue;
                    var all = new List<LineModel.Ease>(e.lmEase ?? System.Array.Empty<LineModel.Ease>());
                    // the slip widening's tag: a plain pavement widening, no refuge island's and no apron's
                    all.Add(new LineModel.Ease { widen = true, side = (sbyte)side, fromA = true, dw = 1f, len = e.length + 1f, d0 = 0f, prof = prof, profD0 = 0f, profDs = ds, tag = SlipWidenTag });
                    e.lmEase = all.ToArray();
                    ParkingSideM += run + ParkEaseM; any = true;
                }
                if (any) ParkingEdges++;
            }
        }
    }
}
