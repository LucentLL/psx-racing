using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    // ======================================================================
    //  ROADSIDE STRIPS: ON-STREET PARKING LANES, BIKE LANES, PAVED SHOULDERS
    //  (2026-11-15, the lanes round).
    //
    //  The owner put a street view beside the game's Beatties Ford Rd (13:10):
    //  the real road has a white line, about 1.4 m of asphalt beyond it, then
    //  the gutter pan and the curb; the game ran the outer lane to the gutter.
    //  And streets where cars park along the curb (Camden Rd, East Blvd, the
    //  Uptown and South End streets) were drawn at the width of their two
    //  travel lanes, 7.9 m, where the real street is 11-13 m between its curbs.
    //
    //  A strip is PAVEMENT beside the travel lanes: the curb, the gutter and
    //  the walk move out by its width; no traffic lane is added, the lane lines
    //  stay where they are, traffic and the race line never see it. It is a
    //  WIDEN ease of the line model on the street's side (the slip lanes' curve
    //  widening is the same kind: LineModel.Ext adds it to the pavement's
    //  extents only).
    //    a bike lane        1.5 m of asphalt from the lane's line to the gutter
    //    a paved shoulder   1.2 m
    //    a parking lane     2.3 m
    //  (the road's own profile already paves 0.3 m beyond the outer lane.)
    //  A bike lane and a paved shoulder keep the white line a curbed side
    //  otherwise drops (Curbs2 CurbNoEdgeLines): it is the bike lane's line.
    //  A parking lane is unmarked, as the ones seen are.
    //
    //  ALONG THE ROAD: full width along the block; through a plain joint of the
    //  road (a node of two edges, both carrying the same strip on that side);
    //  eased to nothing before every junction's corner, which leaves each
    //  junction's own shape exactly as it was. (The real bike lane runs up to
    //  the corner: listed in the hand-back as what still differs.)
    //
    //  WHICH ROADS: section LAN2's rows (CityLanes2.Row.park), written by
    //  tools/city/lanes2.mjs: parking from the map's parking tags and
    //  tools/city/parking2.tsv; bike lanes and shoulders from the map's cycleway
    //  tags on the roads named in tools/city/strips2.tsv (the roads proven so
    //  far) and from that table's own rows (seen in a view, not tagged).
    //    PSX_CITY_LANES2=0      none of it (no LAN2 rows are read)
    //    PSX_CITY_PARKING=0     no parking lane
    //    PSX_CITY_STRIPS=0      no bike lane or paved shoulder
    //    PSX_CITY_STRIPS_ALL=1  also every bike lane / shoulder the map tags
    //                           (488 km of road side; NOT proven, default off)
    // ======================================================================
    public static partial class CityMeshes
    {
        public static bool ParkingOn = System.Environment.GetEnvironmentVariable("PSX_CITY_PARKING") != "0";
        public static bool StripsOn = System.Environment.GetEnvironmentVariable("PSX_CITY_STRIPS") != "0";
        public static bool StripsAll = System.Environment.GetEnvironmentVariable("PSX_CITY_STRIPS_ALL") == "1";
        /// <summary>Asphalt from the outer lane's edge to the gutter: a parallel parking lane, a bike lane, a paved shoulder.</summary>
        public const float ParkLaneM = 2.3f, BikeLaneM = 1.5f, ShoulderM = 1.2f;
        /// <summary>What every profile already paves beyond its outer lane.</summary>
        const float StripHasM = 0.3f;
        /// <summary>No parking this near a junction's corner (past the ribbon's own trim; NC G.S. 20-162: 25 ft), and the ease into the lane.</summary>
        public const float ParkClearM = 6f, ParkEaseM = 5f;
        /// <summary>A bike lane or shoulder: where it has closed before a corner, and its (gentler) ease.</summary>
        public const float StripClearM = 3f, StripEaseM = 9f;
        /// <summary>The shortest full-width run worth drawing (two cars; a bike lane's shortest piece).</summary>
        public const float ParkMinRunM = 11f, StripMinRunM = 8f;
        public static int ParkingEdges; public static float ParkingSideM;
        public static int StripEdges; public static float StripSideM;
        public static string ParkingReport => !CityLanes2.On ? "roadside strips OFF (PSX_CITY_LANES2=0)"
            : (ParkingOn ? $"parking lanes: {ParkingEdges} edges, {ParkingSideM / 1000f:0.0} km of curb side" : "parking lanes OFF (PSX_CITY_PARKING=0)") + "; "
            + (StripsOn ? $"bike lanes and paved shoulders: {StripEdges} edges, {StripSideM / 1000f:0.0} km of road side" + (StripsAll ? " (PSX_CITY_STRIPS_ALL=1: every tagged one)" : "") : "bike lanes and shoulders OFF (PSX_CITY_STRIPS=0)");

        /// <summary>Per edge: bit 1 the left side's strip keeps its white line, bit 2 the right's (BuildParkingLanes).</summary>
        static sbyte[] stripLine;
        /// <summary>Bit 1: the left of a->b carries a marked strip (a bike lane, a paved shoulder) and keeps its edge
        /// line beside the curb; bit 2: the right. The one caller masks CurbNoEdgeLines with it (EmitRibbon).</summary>
        static int StripEdgeLines(CityMap.Edge e)
            => stripLine != null && e != null && e.index >= 0 && e.index < stripLine.Length ? stripLine[e.index] : 0;

        /// <summary>An edge's strips: bit 0/1 a parking lane left / right of a->b, bit 2/3 a bike lane, bit 4/5 a paved shoulder.</summary>
        static int StripBits(CityMap.Edge e)
        {
            int b = 0;
            var rows = CityLanes2.Rows;
            if (rows != null && rows.TryGetValue(e.index, out var r)) b = r.park & 63;
            if (StripsAll) b |= (e.lsBike & 15) << 2;
            if (!ParkingOn) b &= ~3;
            if (!StripsOn) b &= ~60;
            return b;
        }

        /// <summary>The widening (m) of an edge's side, 0 for none; marked: it keeps a white line; park: a parking lane.</summary>
        static float StripWiden(CityMap.Edge e, int bits, int side, out bool marked, out bool park)
        {
            bool left = side > 0;   // the left of a->b is the line model's plus side
            park = (bits & (left ? 1 : 2)) != 0;
            bool lane = (bits & (left ? 4 : 8)) != 0, sh = (bits & (left ? 16 : 32)) != 0;
            marked = lane || sh;
            if (!park && !marked) return 0f;
            // a one-way carriageway of a divided road has its strip on the outer (right) side only
            if (e.oneway && left && CurbMedianClass(e)) { marked = false; park = false; return 0f; }
            return (park ? ParkLaneM : lane ? BikeLaneM : ShoulderM) - StripHasM;
        }

        /// <summary>The same strip runs on through this end: a plain joint of two edges (no junction, no trim),
        /// the other edge carrying a strip of the same width on the same side of the road.</summary>
        static bool StripThrough(CityMap map, Trims t, CityMap.Edge e, int node, int side, float w, float[] wL, float[] wR)
        {
            var at = map.nodeEdges != null && node >= 0 && node < map.nodeEdges.Length ? map.nodeEdges[node] : null;
            if (at == null || at.Count != 2) return false;
            int oi = at[0] == e.index ? at[1] : at[0];
            if (oi == e.index || oi < 0 || oi >= map.edges.Length) return false;
            var o = map.edges[oi];
            if (o.a == o.b) return false;
            float te = e.a == node ? t.atA[e.index] : t.atB[e.index], to = o.a == node ? t.atA[oi] : t.atB[oi];
            if (te > 0.01f || to > 0.01f) return false;
            // sides as seen leaving the node: the two edges leave it in opposite directions
            int outE = e.a == node ? side : -side;
            int sideO = o.a == node ? -outE : outE;
            float wo = sideO > 0 ? wL[oi] : wR[oi];
            return wo > 0f && Mathf.Abs(wo - w) < 0.01f;
        }

        /// <summary>After the line model's eases are built (ComputeTrimsCore): the strips' widen eases.</summary>
        static void BuildParkingLanes(CityMap map, Trims t)
        {
            ParkingEdges = 0; ParkingSideM = 0f; StripEdges = 0; StripSideM = 0f; stripLine = null;
            var rows = CityLanes2.Rows;
            if (!CityLanes2.On || (rows == null && !StripsAll) || (!ParkingOn && !StripsOn)) return;
            int ne = map.edges.Length;
            var cand = new List<int>();
            if (StripsAll) { for (int i = 0; i < ne; i++) cand.Add(i); }
            else foreach (var kv in rows) if ((kv.Value.park & 63) != 0 && kv.Key >= 0 && kv.Key < ne) cand.Add(kv.Key);
            cand.Sort();   // the rows' order is a dictionary's: keep the build the same from run to run
            var wL = new float[ne]; var wR = new float[ne]; var flags = new byte[ne];   // flags: 1 left marked, 2 right marked, 4 left parking, 8 right parking
            var act = new List<int>();
            foreach (int i in cand)
            {
                var e = map.edges[i];
                if (e.a == e.b || e.link || e.bridge || e.tunnel || e.cls >= 4) continue;
                int bits = StripBits(e);
                if (bits == 0) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    float w = StripWiden(e, bits, side, out bool marked, out bool park);
                    if (w <= 0f) continue;
                    if (side > 0) wL[i] = w; else wR[i] = w;
                    if (marked) flags[i] |= (byte)(side > 0 ? 1 : 2);
                    if (park) flags[i] |= (byte)(side > 0 ? 4 : 8);
                }
                if (wL[i] > 0f || wR[i] > 0f) act.Add(i);
            }
            // a side too short between its corners drops its strip; that can end a neighbour's run through
            // their joint, so settle it before any ease is written
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                foreach (int i in act)
                {
                    var e = map.edges[i];
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float w = side > 0 ? wL[i] : wR[i];
                        if (w <= 0f) continue;
                        bool park = (flags[i] & (side > 0 ? 4 : 8)) != 0;
                        float clear = park ? ParkClearM : StripClearM, ease = park ? ParkEaseM : StripEaseM;
                        bool thA = StripThrough(map, t, e, e.a, side, w, wL, wR), thB = StripThrough(map, t, e, e.b, side, w, wL, wR);
                        float run = e.length - (thA ? 0f : t.atA[i] + clear + ease) - (thB ? 0f : t.atB[i] + clear + ease);
                        if ((thA && thB) || run >= (park ? ParkMinRunM : StripMinRunM)) continue;
                        if (side > 0) wL[i] = 0f; else wR[i] = 0f;
                        flags[i] = (byte)(flags[i] & (side > 0 ? 0xFA : 0xF5));
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            stripLine = new sbyte[ne];
            const float ds = 1f;
            foreach (int i in act)
            {
                var e = map.edges[i];
                bool any = false, anyPark = false;
                for (int side = -1; side <= 1; side += 2)
                {
                    float w = side > 0 ? wL[i] : wR[i];
                    if (w <= 0f) continue;
                    bool park = (flags[i] & (side > 0 ? 4 : 8)) != 0;
                    float clear = park ? ParkClearM : StripClearM, ease = park ? ParkEaseM : StripEaseM;
                    bool thA = StripThrough(map, t, e, e.a, side, w, wL, wR), thB = StripThrough(map, t, e, e.b, side, w, wL, wR);
                    float a0 = t.atA[i] + clear, b0 = t.atB[i] + clear;
                    int n = Mathf.FloorToInt(e.length / ds) + 2;
                    var prof = new float[n];
                    for (int k = 0; k < n; k++)
                    {
                        float s = Mathf.Min(k * ds, e.length);
                        float fa = thA ? 1f : LineModel.Smooth((s - a0) / ease), fb = thB ? 1f : LineModel.Smooth((e.length - s - b0) / ease);
                        prof[k] = w * Mathf.Min(fa, fb);
                    }
                    var all = new List<LineModel.Ease>(e.lmEase ?? System.Array.Empty<LineModel.Ease>());
                    // the slip widening's tag: a plain pavement widening, no refuge island's and no apron's
                    all.Add(new LineModel.Ease { widen = true, side = (sbyte)side, fromA = true, dw = 1f, len = e.length + 1f, d0 = 0f, prof = prof, profD0 = 0f, profDs = ds, tag = SlipWidenTag });
                    e.lmEase = all.ToArray();
                    float run = e.length - (thA ? 0f : a0 + ease * 0.5f) - (thB ? 0f : b0 + ease * 0.5f);
                    if (park) { ParkingSideM += run; anyPark = true; } else { StripSideM += run; any = true; }
                    if ((flags[i] & (side > 0 ? 1 : 2)) != 0) stripLine[i] |= (sbyte)(side > 0 ? 1 : 2);
                }
                if (anyPark) ParkingEdges++;
                if (any) StripEdges++;
            }
        }
    }
}
