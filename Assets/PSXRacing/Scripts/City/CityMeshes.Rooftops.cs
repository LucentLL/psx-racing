using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE ROOFTOP KIT (skyline tops, 2026-10-16). A tower's roof is not
        //  bare: the roof's edge stands up as a parapet, the lifts and air
        //  handling sit in a screened plant penthouse (two on a long slab),
        //  a step in the massing has its own lip, and the tallest carry a
        //  mast. Every tower of RoofKitMinH and more whose OSM data gives it
        //  no parts and no roof shape gets them, sized off its own roof and
        //  placed by the same hash as its massing (deterministic, the far
        //  skyline's copy too). These are what real towers carry, not a
        //  landmark's shape: the real crowns are OSM's parts, the measured
        //  reference tiers (tools/city/ref_tiers.py) and the heroes.
        //  B3's podium towers (EmitMassing) wear the kit on their own steps.
        // ------------------------------------------------------------------
        /// <summary>The shortest tower that gets the rooftop kit (towers this
        /// tall are also never swapped for a pack model: CityBuildings).</summary>
        public const float RoofKitMinH = 40f;
        /// <summary>The shortest tower that carries a mast on its plant.</summary>
        const float MastMinH = 100f;
        /// <summary>The parapet lip round a kit roof, and the lip on a step.</summary>
        const float ParapetLipM = 1.2f, LedgeM = 0.9f;

        static CityMap kitMap;
        static bool[] kitHasParts;

        /// <summary>True when OSM building:parts (or measured tiers) stand on
        /// this outline: the parts are its roof, the kit stays off it.</summary>
        static bool OutlineHasParts(CityMap map, int fi)
        {
            if (kitMap != map)
            {
                kitMap = map;
                kitHasParts = new bool[map.footprints.Length];
                foreach (var p in map.footprints)
                    if (p.part && p.outline >= 0 && p.outline < kitHasParts.Length) kitHasParts[p.outline] = true;
            }
            return fi >= 0 && fi < kitHasParts.Length && kitHasParts[fi];
        }

        /// <summary>The kit on a plain prism's flat roof at
        /// <paramref name="top"/>: a parapet lip, the plant (and the mast on
        /// a tower <see cref="MastMinH"/> or taller).</summary>
        static void EmitRoofKit(TileMeshes tm, CityMap.Footprint f, List<Vector2> roof, float top, float towerH)
        {
            uint h = FacadeHash(f.centre + new Vector2(17.3f, -5.1f));   // the massing's stream
            Parapet(tm, roof, top, ParapetLipM);
            RoofPlant(tm, f, roof, top, 0.34f + 0.12f * (((h >> 24) & 0xFF) / 255f), 1.3f * facFloorH, h, towerH);
        }

        /// <summary>A step's lip: the walls below carried up past the lower
        /// roof (B3's podium roof, a setback).</summary>
        static void Ledge(TileMeshes tm, List<Vector2> poly, float y) => Parapet(tm, poly, y, LedgeM);

        /// <summary>The plant on a roof: one screened box, or two on a slab
        /// whose roof is long for its width, and on a tower
        /// <see cref="MastMinH"/> or taller a mast on the first box.</summary>
        static void RoofPlant(TileMeshes tm, CityMap.Footprint f, List<Vector2> roof, float y, float share, float tall, uint h, float towerH)
        {
            RoofBox(f, roof, out float hu, out float hv);
            bool two = hu > 1.8f * hv && hu > 12f;
            Vector2 at; float boxTop;
            bool placed = two
                ? PlantBox(tm, f, roof, y, share * 0.55f, tall, h, out at, out boxTop, -0.5f)
                : PlantBox(tm, f, roof, y, share, tall, h, out at, out boxTop);
            if (two) PlantBox(tm, f, roof, y, share * 0.45f, tall * 0.8f, h, out _, out _, 0.55f);
            if (placed && towerH >= MastMinH) Mast(tm, at, boxTop, Mathf.Clamp(0.06f * towerH, 6f, 12f));
        }

        /// <summary>The roof's half extents along the footprint's axes.</summary>
        static void RoofBox(CityMap.Footprint f, List<Vector2> roof, out float hu, out float hv)
        {
            var v = new Vector2(-f.u.y, f.u.x);
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var p in roof)
            {
                float a = Vector2.Dot(p - f.centre, f.u), b = Vector2.Dot(p - f.centre, v);
                u0 = Mathf.Min(u0, a); u1 = Mathf.Max(u1, a); v0 = Mathf.Min(v0, b); v1 = Mathf.Max(v1, b);
            }
            hu = (u1 - u0) * 0.5f; hv = (v1 - v0) * 0.5f;
        }

        /// <summary>A mast: a slim grey metal post <paramref name="tall"/>
        /// above <paramref name="y"/> (the far skyline leaves it out). Its
        /// tip goes in <see cref="TileMeshes.mastTips"/> for the night's
        /// beacons.</summary>
        static void Mast(TileMeshes tm, Vector2 at, float y, float tall)
        {
            if (skylineBuild) return;
            var keep = Bucket.Tint;
            HeroTint(HeroMetalCol, true, 0.62f, 0.63f, 0.66f, crown: false);
            HeroBox(tm, at, Vector2.right, 0.3f, 0.3f, y, y + tall, HeroMetalUV);
            Bucket.Tint = keep;
            tm.mastTips.Add(new Vector3(at.x, y + tall, at.y));
        }
    }
}
