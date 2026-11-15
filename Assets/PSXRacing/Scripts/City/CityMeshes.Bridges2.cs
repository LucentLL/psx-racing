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

        /// <summary>A tool flipped <see cref="Bridge2On"/>: what was decided
        /// once per map under the old value is decided again.</summary>
        public static void Bridge2Changed() { girderPlans.Clear(); girderMap = null; }
    }
}
