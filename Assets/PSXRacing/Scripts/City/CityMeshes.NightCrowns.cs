using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE CROWNS REAL CHARLOTTE LIGHTS - AS A 1990s CITY LIT THEM
        //  (the uptown night pass, 2026-10-16; the 1990s city, 2026-10-29).
        //
        //  The owner, 2026-10-29, with a 1990s photograph of uptown at dusk:
        //  "The city (logic and technology) should resemble the 90s even
        //  though roads and buildings are present day. LEDs were not common."
        //  His photograph: one tower floodlit white over its whole face, the
        //  rest dark tops with red aviation lights - no coloured crowns, no
        //  outlines. So the 2026-10-16 palette (blue, violet, teal, gold,
        //  green LED crowns, LED lines up the open frame) is gone: a crown is
        //  FLOODLIT - warm (the photograph's floodlit tower, measured hue 48,
        //  sat 0.23) or metal halide (a white a little cool, never blue) - or
        //  DARK. Every tower the table does not name is floodlit or dark by
        //  its hash (PSX/Lit CROWN_DARK_SHARE, CROWN_WARM_SHARE).
        //
        //  The table, keyed by the exporter's landmark byte (tools/city/lib/
        //  parts.mjs, neutral names), gives a named tower its flood - written
        //  into the facade's vertex alpha low two bits (FacadeFloodMask; the
        //  third is FacadeHomeBit) - and whether its light is on its FRAME
        //  (the open-frame tower: the white members floodlit from the roof,
        //  the glass under them dark, no floodlit band on the shaft). The
        //  owner, who lives there, corrects them here.
        //  PSX_CITY_FLOODS=0 drops the table (every crown by its hash).
        // ------------------------------------------------------------------
        public struct NightCrown
        {
            public byte landmark, flood; public bool frame; public string what;
            public NightCrown(byte landmark, byte flood, bool frame, string what)
            { this.landmark = landmark; this.flood = flood; this.frame = frame; this.what = what; }
        }

        /// <summary>PSX/Lit's flood codes (facFlood): 0 by the tower's hash,
        /// 1 warm, 2 metal halide, 3 dark.</summary>
        public const byte FloodByHash = 0, FloodWarm = 1, FloodHalide = 2, FloodDark = 3;

        public static readonly NightCrown[] NightCrowns =
        {
            new NightCrown(1,  FloodHalide, false, "spired crown tower: the crown's rods and needle floodlit white"),
            new NightCrown(2,  FloodHalide, true,  "open-frame tower: its white frame floodlit from the roof, the glass under it dark"),
            new NightCrown(3,  FloodHalide, false, "silver crown tower: the lantern floodlit white"),
            new NightCrown(4,  FloodWarm,   false, "curved-top tower: the curved top floodlit warm"),
            new NightCrown(5,  FloodWarm,   false, "pyramid-top tower: the copper top floodlit warm"),
            new NightCrown(9,  FloodDark,   false, "slim residential tower: no crown light (its coloured band is today's)"),
            new NightCrown(15, FloodHalide, false, "pointed tower: the crown floodlit white"),
            new NightCrown(16, FloodHalide, false, "slanted tower: the slanted top floodlit white"),
        };

        static readonly bool FloodsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_FLOODS") == "0";
        static byte[] floodOfLandmark;
        static bool[] frameOfLandmark;

        static void IndexNightCrowns()
        {
            floodOfLandmark = new byte[256];
            frameOfLandmark = new bool[256];
            if (FloodsOff) return;
            foreach (var c in NightCrowns) { floodOfLandmark[c.landmark] = c.flood; frameOfLandmark[c.landmark] = c.frame; }
        }

        /// <summary>A landmark's crown flood code (0: by the tower's hash).</summary>
        public static byte NightFloodOf(byte landmark)
        {
            if (floodOfLandmark == null) IndexNightCrowns();
            return floodOfLandmark[landmark];
        }

        /// <summary>Whether a landmark's light is on its frame (no floodlit
        /// band on its shaft; HeroOpenFrame floods the white members).</summary>
        public static bool NightFrameOf(byte landmark)
        {
            if (frameOfLandmark == null) IndexNightCrowns();
            return frameOfLandmark[landmark];
        }

        /// <summary>The building being emitted's flood code: set before
        /// PickFacade, carried in every facade tint the building writes (the
        /// alpha's low two bits; the crown bit decides whether the shader
        /// uses it), cleared by EndFacade.</summary>
        static byte facLed;
    }
}
