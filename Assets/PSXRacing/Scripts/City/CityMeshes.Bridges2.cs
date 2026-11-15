using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// BRIDGES, ROUND 2 (2026-11-15, the owner: "review all roads for accuracy
    /// (including ... bridges ...). I plan to start adding more racing tracks
    /// in Charlotte today once roads are accurate"). What the bridges audit
    /// (Editor/CityAudit.Bridges.cs) found, fixed by rule. Every fix has its
    /// own switch; PSX_CITY_BRIDGE2=0 turns the lot off (the picture as it was).
    /// </summary>
    public static partial class CityMeshes
    {
        /// <summary>PSX_CITY_BRIDGE2=0: every round-2 bridge fix off (the A/B).</summary>
        public static bool Bridge2On = System.Environment.GetEnvironmentVariable("PSX_CITY_BRIDGE2") != "0";

        // ---- 1. GIRDERS KEEP THE ROAD'S CLEARANCE ---------------------------------
        // The audit measured the air under every deck, a ray up from every lane
        // of every road below: 418 places (136 roads) had under 4.3 m - under
        // 4.9 m over a freeway's main line - and every one was a GIRDER, not the
        // slab: the solve stands a slab 5.0 m over the road below
        // (CityElevation.ClearanceM) and the bridges pass then hung its girders
        // down to 3.8 m over it (GirderCarClearM: "a car, the chase camera and a
        // hand"), measured from the road's centre line at the deck's stations -
        // 3.67 m over I-85's outer lane under University Pointe Blvd. A truck
        // route's bridge is posted under 4.1 m; NCDOT builds 16 ft 6 in (5.03 m)
        // over an interstate and 15 ft (4.57 m) over a city street. Now a
        // deck's girders are as deep as the road below allows: 4.3 m kept over a
        // street, 4.9 m over a freeway's main line, each with GirderCrossFallM
        // for the lanes that stand higher than the centre line (cross-fall, a
        // super-elevated ramp). The girder is never thinner than GirderMinM
        // (0.45 m: a slab with nothing under it is the "very basic" bridge of
        // 2026-10-24), so over a freeway the solve's 5.0 m leaves 4.55 m: the
        // audit still lists those (the fix for them is the solve's clearance,
        // a height change, not made today).
        // PSX_CITY_BR2_GIRDERCLEAR=0: the girders as they were.
        static readonly bool girderClearOff = System.Environment.GetEnvironmentVariable("PSX_CITY_BR2_GIRDERCLEAR") == "0";
        public static bool GirderClearOn => Bridge2On && !girderClearOff;
        const float GirderClearStreetM = 4.3f, GirderClearFreewayM = 4.9f, GirderCrossFallM = 0.15f;
        /// <summary>The air a road below keeps under a deck's girders.</summary>
        static float GirderClearOver(CityMap.Edge below) =>
            (below.cls >= 5 && !below.link ? GirderClearFreewayM : GirderClearStreetM) + GirderCrossFallM;

        // ---- 2. NO DECK RAIL ON ANOTHER ROAD'S PAVEMENT ---------------------------
        // Where several ramps meet ON a bridge (the single-point interchanges:
        // I-77 at Tyvola Road, I-85 at Freedom Drive and at Brookshire Blvd)
        // each short link drew its own deck with its own parapets, and the
        // parapets stood as blocks across the other links' lanes in the middle
        // of the shared deck (the audit's RAIL-ON-LANE / WALL-IN-LANE, all of
        // them there). RailOverArms already keeps a rail off the pavement of
        // the arms AT ITS OWN NODE; these stood on the pavement of roads one
        // node further on. Now a span on structure near a junction draws no
        // rail on a side whose whole length - the rail's traffic face and half
        // a metre past its outer face - lies inside ANOTHER road's pavement at
        // its own level (the line model's extents, 0.3 m in from that road's
        // edges): pavement carries on both sides of it, so it guards no fall,
        // and that road's own outer edge keeps its own rail. Decided with the
        // span's flags from the map alone, so every tile draws the same and
        // the run's caps follow.
        // PSX_CITY_BR2_RAILPAVE=0: the rails as they were.
        static readonly bool railPaveOff = System.Environment.GetEnvironmentVariable("PSX_CITY_BR2_RAILPAVE") == "0";
        public static bool RailPaveOn => Bridge2On && !railPaveOff;
        /// <summary>How near (arc metres) a junction of three or more roads the rule looks.</summary>
        const float RailPaveNodeM = 60f;
        static readonly HashSet<int> rpSegs = new HashSet<int>();
        /// <summary>The last tile build's count of deck rail spans left out by the rule (audits).</summary>
        public static int railPaveSpans;

        /// <summary>Is the map point <paramref name="p"/> at height <paramref name="y"/>
        /// inside the pavement of a road other than <paramref name="e"/> at that level?</summary>
        static bool OnOtherPavement(CityMap map, CityMap.Edge e, Vector2 p, float y)
        {
            rpSegs.Clear();
            map.EdgeSegsInRect(p - Vector2.one * 18f, p + Vector2.one * 18f, rpSegs);
            foreach (int packed in rpSegs)
            {
                int oi = packed >> 12, si = packed & 0xFFF;
                if (oi == e.index) continue;
                var o = map.edges[oi];
                if (si + 1 >= o.pts.Length || o.tunnel) continue;
                Vector2 a = o.pts[si], d = o.pts[si + 1] - a;
                float L2 = d.sqrMagnitude;
                if (L2 < 1e-6f) continue;
                float t = Vector2.Dot(p - a, d) / L2;
                if (t < 0f || t > 1f) continue;   // beside this piece of its line only
                float len = Mathf.Sqrt(L2);
                float at = o.s[si] + len * t;
                if (Mathf.Abs(o.YAt(at) - y) > 0.45f) continue;
                LineModel.Extents(o, at, out float eM, out float eP);
                var tn = d / len;
                float lat = Vector2.Dot(p - (a + d * t), new Vector2(-tn.y, tn.x));
                if (lat > -(eM - 0.3f) && lat < eP - 0.3f) return true;
            }
            return false;
        }

        /// <summary>The rule above for one span's side (A, B: its sections; tile-local, <paramref name="org"/> the tile's origin).</summary>
        static bool DeckRailOnOtherPavement(CityMap map, CityMap.Edge e, in Section A, in Section B, int side, Vector3 org)
        {
            if (!RailPaveOn) return false;
            bool nearA = A.s < RailPaveNodeM && map.nodeEdges[e.a].Count >= 3, nearB = e.length - B.s < RailPaveNodeM && map.nodeEdges[e.b].Count >= 3;
            if (!nearA && !nearB) return false;
            Vector3 eA = A.Edge(side), eB = B.Edge(side);
            Vector2 oA = A.Out(side), oB = B.Out(side);
            for (int k = 0; k < 3; k++)
            {
                float t = 0.1f + 0.4f * k;
                var p3 = Vector3.Lerp(eA, eB, t) + org;
                var outw = Vector2.Lerp(oA, oB, t);
                if (outw.sqrMagnitude < 1e-6f) return false;
                outw.Normalize();
                var line = new Vector2(p3.x, p3.z);
                if (!OnOtherPavement(map, e, line - outw * RailW, p3.y)) return false;
                if (!OnOtherPavement(map, e, line + outw * (RailOverhangM + 0.5f), p3.y)) return false;
            }
            railPaveSpans++;
            return true;
        }

        // ---- 3. THE CITY BRIDGE RAIL ----------------------------------------------
        // Every bridge in the city wore one rail: the plain concrete parapet.
        // A 1990s freeway structure does (the "New Jersey" parapet); a city
        // street's bridge of the 1970s-80s - South Tryon, College, Church over
        // the Belk Freeway, the creek bridges on the avenues - carries a lower
        // concrete parapet with a metal rail on posts along its top (NCDOT's
        // one-bar metal rail). So on structure, on a street (class 3 and under,
        // not a ramp), a galvanized bar on short posts stands on the parapet's
        // top: render-only on the structure atlas (its steel swatch), above
        // the parapet's collider, on full-height pieces only (a sloped end
        // carries none). Freeways, expressways and ramps keep the bare parapet.
        // PSX_CITY_BR2_CITYRAIL=0: no bar.
        static readonly bool cityRailOff = System.Environment.GetEnvironmentVariable("PSX_CITY_BR2_CITYRAIL") == "0";
        public static bool CityRailOn => Bridge2On && BridgeDetailOn && !cityRailOff;
        /// <summary>The bar: its posts' pitch, its section (square), its underside over the parapet's top.</summary>
        const float CityRailPostM = 2.4f, CityRailBarM = 0.09f, CityRailRiseM = 0.24f;

        /// <summary>The bar over one deck rail piece from <paramref name="a"/> to <paramref name="b"/>
        /// (the rail's line, tile-local; in: toward the traffic; h: the parapet's height at each
        /// end; ov: its overhang; s: the edge's arc at each end, the posts' pitch is counted on).</summary>
        static void EmitCityRail(CityMap.Edge e, Vector3 a, Vector3 b, Vector2 inA, Vector2 inB, float hA, float hB, float ovA, float ovB, float sA, float sB)
        {
            if (!CityRailOn || e.cls > 3 || e.link) return;
            if (hA < RailH - 0.01f || hB < RailH - 0.01f) return;
            var bk = structBucket;
            Vector3 nA = Flat(inA), nB = Flat(inB);
            // the parapet's top, its middle
            Vector3 cA = a + nA * (0.5f * (RailW - ovA)) + Vector3.up * hA, cB = b + nB * (0.5f * (RailW - ovB)) + Vector3.up * hB;
            var along = cB - cA; along.y = 0f;
            if (along.sqrMagnitude < 0.01f) return;
            along.Normalize();
            float hw = 0.5f * CityRailBarM;
            Vector3 lo = Vector3.up * CityRailRiseM, hi = Vector3.up * (CityRailRiseM + CityRailBarM);
            var uv00 = new Vector2(CellGalv0, BandMisc0); var uv01 = new Vector2(CellGalv0, BandMisc1);
            var uv11 = new Vector2(CellGalv1, BandMisc1); var uv10 = new Vector2(CellGalv1, BandMisc0);
            bk.Face(cA + nA * hw + lo, cA + nA * hw + hi, cB + nB * hw + hi, cB + nB * hw + lo, nA + nB, uv00, uv01, uv11, uv10);
            bk.Face(cA - nA * hw + lo, cA - nA * hw + hi, cB - nB * hw + hi, cB - nB * hw + lo, -(nA + nB), uv00, uv01, uv11, uv10);
            bk.Up(cA + nA * hw + hi, cA - nA * hw + hi, cB - nB * hw + hi, cB + nB * hw + hi, uv00, uv01, uv11, uv10);
            bk.Down(cA + nA * hw + lo, cB + nB * hw + lo, cB - nB * hw + lo, cA - nA * hw + lo, uv00, uv10, uv11, uv01);
            float s0 = Mathf.Min(sA, sB), s1 = Mathf.Max(sA, sB);
            if (s1 - s0 < 1e-3f) return;
            for (float sp = Mathf.Ceil(s0 / CityRailPostM) * CityRailPostM; sp < s1; sp += CityRailPostM)
            {
                float t = Mathf.InverseLerp(sA, sB, sp);
                var c = Vector3.Lerp(cA, cB, t);
                var n = Vector3.Lerp(nA, nB, t);
                EmitBlock(bk, c, c + lo, along * 0.035f, n * hw, CellGalv0, CellGalv1, BandMisc0, BandMisc1);
            }
        }

        /// <summary>A tool flipped <see cref="Bridge2On"/>: what was decided
        /// once per map under the old value is decided again.</summary>
        public static void Bridge2Changed() { girderPlans.Clear(); girderMap = null; }
    }
}
