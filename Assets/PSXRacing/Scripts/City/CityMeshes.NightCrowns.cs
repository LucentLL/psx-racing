using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE CROWNS REAL CHARLOTTE LIGHTS (the uptown night pass,
        //  2026-10-16; the owner on UG2's Bayview: "what can be done to make
        //  the city look at least this good", and on the city: "It should be
        //  familiar when driving through or viewing from a distance").
        //
        //  Before this every tower of 60 m and more wore a RANDOM accent
        //  colour on its crown after dark (blue, violet, teal, pink, green by
        //  a hash of its tint) - a confetti skyline, and not Charlotte's. Now
        //  a crown is lit in colour only where the real tower is: the table
        //  below, keyed by the exporter's landmark byte (tools/city/lib/
        //  parts.mjs, neutral names), gives the tower its LED colour - an
        //  index into PSX/Lit's crown palette, written into the facade's
        //  vertex alpha low three bits - and whether its crown carries LED
        //  edge STRIPS (CityBeacons draws those as beads of light). Every
        //  other tall tower keeps the floodlit top storeys in warm or cool
        //  white. Colours are steady: no light shows, no imagery.
        //
        //  The palette (PSX/Lit, the crown block): 1 warm white, 2 cool
        //  white, 3 blue, 4 violet, 5 teal, 6 gold, 7 green. The colours are
        //  the towers' usual night look as best known on 2026-10-16; the
        //  owner, who lives there, corrects them here.
        //  PSX_CITY_LEDS=0 drops the table (every crown floodlit white).
        // ------------------------------------------------------------------
        public struct NightCrown
        {
            public byte landmark, led; public bool strips; public string what;
            public NightCrown(byte landmark, byte led, bool strips, string what)
            { this.landmark = landmark; this.led = led; this.strips = strips; this.what = what; }
        }

        public const byte LedWarmWhite = 1, LedCoolWhite = 2, LedBlue = 3, LedViolet = 4, LedTeal = 5, LedGold = 6, LedGreen = 7;

        public static readonly NightCrown[] NightCrowns =
        {
            new NightCrown(1,  LedWarmWhite, false, "spired crown tower: the crown's rods and needle floodlit"),
            new NightCrown(2,  LedBlue,      true,  "open-frame tower: the LED-lit crown structure"),
            new NightCrown(3,  LedCoolWhite, false, "silver crown tower: the lantern lit white"),
            new NightCrown(4,  LedWarmWhite, false, "curved-top tower: the curved top floodlit"),
            new NightCrown(5,  LedWarmWhite, false, "pyramid-top tower: the copper top floodlit"),
            new NightCrown(9,  LedBlue,      false, "slim residential tower: the crown band"),
            new NightCrown(15, LedCoolWhite, false, "pointed tower: the crown lit white"),
            new NightCrown(16, LedCoolWhite, false, "slanted tower: the slanted top lit white"),
        };

        static readonly bool LedsOff = System.Environment.GetEnvironmentVariable("PSX_CITY_LEDS") == "0";
        static byte[] ledOfLandmark;
        static bool[] stripsOfLandmark;

        static void IndexNightCrowns()
        {
            ledOfLandmark = new byte[256];
            stripsOfLandmark = new bool[256];
            if (LedsOff) return;
            foreach (var c in NightCrowns) { ledOfLandmark[c.landmark] = c.led; stripsOfLandmark[c.landmark] = c.strips; }
        }

        /// <summary>The LED palette index of a landmark's crown (0: floodlit white).</summary>
        public static byte NightLedOf(byte landmark)
        {
            if (ledOfLandmark == null) IndexNightCrowns();
            return ledOfLandmark[landmark];
        }

        /// <summary>Whether a landmark's crown carries LED edge strips.</summary>
        public static bool NightStripsOf(byte landmark)
        {
            if (stripsOfLandmark == null) IndexNightCrowns();
            return stripsOfLandmark[landmark];
        }

        /// <summary>PSX/Lit's crown palette (linear), for what draws the same
        /// colour outside the facade: CityBeacons' strip beads.</summary>
        public static Color LedColour(byte led)
        {
            switch (led)
            {
                case LedWarmWhite: return new Color(1.00f, 0.86f, 0.66f);
                case LedCoolWhite: return new Color(0.80f, 0.88f, 1.00f);
                case LedBlue: return new Color(0.22f, 0.38f, 1.00f);
                case LedViolet: return new Color(0.55f, 0.26f, 1.00f);
                case LedTeal: return new Color(0.12f, 0.80f, 0.85f);
                case LedGold: return new Color(1.00f, 0.66f, 0.22f);
                case LedGreen: return new Color(0.20f, 0.95f, 0.38f);
                default: return Color.white;
            }
        }

        /// <summary>The building being emitted's LED index: set before
        /// PickFacade, carried in every facade tint the building writes (the
        /// alpha's low three bits; the crown bit decides whether the shader
        /// uses it), cleared by EndFacade.</summary>
        static byte facLed;
    }
}
