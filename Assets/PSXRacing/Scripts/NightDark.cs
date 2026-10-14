using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE DARK WHERE NOTHING IS LIT (2026-10-13, after Touge Attack).
    ///
    /// The owner's ruling on our night: "dark, if not ambient light in the
    /// area (like a wooded road outside of the city)". Touge Attack's unlit
    /// mountain night, measured off 2560x1440 frames, is a frame mean of 7-9
    /// codes with a MEDIAN of 0 and 83-86% of the pixels under 12: a black sky,
    /// no moon fill, no ambient - the headlight pool, the odd street lamp's
    /// cone and the gauges are the only light, and the scenery pops out of
    /// the black where the beam finds it.
    ///
    /// So the night is now two regimes, chosen by WHERE THE EYE IS:
    ///   * LIT - Charlotte (its lamp field and its skyglow, the whole city),
    ///     anywhere with street lamps round the eye (a circuit's barrier
    ///     lamps, a lit town street), and indoors (a WeatherShelter box: a
    ///     room has its own lights): today's night exactly.
    ///   * UNLIT - a mountain stage, a parkway loop, a sprint, a wooded road:
    ///     the hour's ambient, its moon, its fog band and its sky (the shaders'
    ///     PSX_SKY_DARK_KEEP) cut toward black, and the film grade's last night
    ///     lift (PSX/Blit) taken away, so black is black.
    ///
    /// THE MEASURE: every lit street lamp within <see cref="FarM"/> of the eye
    /// counts its gain (1 for a full cobra-head) at weight 1 inside
    /// <see cref="NearM"/>, easing to 0 at FarM; the sum is mapped through
    /// <see cref="LitLo"/>..<see cref="LitHi"/> (a lone lamp on a mountain is
    /// still a dark road - it gets its cone, not an ambient). The weights are
    /// smooth in distance, so the measure is continuous as the eye moves, and
    /// play mode eases the result over <see cref="EaseTau"/> seconds on top,
    /// so a street lamp switching on, or a tile streaming in, never pops.
    /// One pass over the lamp registry, shared with the cones
    /// (StreetLights.Cones.cs), once a frame for the game's view.
    ///
    /// THE PUSH: PSXGlobals.Apply hands its unscaled light here
    /// (<see cref="FromGlobals"/>) and this pushes the scaled ambient, sky
    /// ambient, moon and fog colour, and _PSXNightDark (the sky shader and
    /// the grade read it). StreetLights re-pushes at render time for the
    /// camera about to draw (the same hook as the lamp table), so an edit-mode
    /// tool's shot is measured at its own eye. At every hour but NIGHT, and
    /// with PSX_NIGHTDARK=0, Amount is exactly 0 and every pushed value is
    /// the one PSXGlobals pushed itself: day frames are bit-identical.
    /// </summary>
    public static class NightDark
    {
        /// <summary>PSX_NIGHTDARK=0 (editor A/B): today's night everywhere.</summary>
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PSX_NIGHTDARK") != "0";

        /// <summary>What a fully unlit night keeps of the hour's ambient and sky
        /// ambient (linear in the authored number - the census finding, see
        /// psx-racing-dark-night).</summary>
        public static float AmbientKeep = 0.15f;
        /// <summary>What it keeps of the moon (the hour's directional light).</summary>
        public static float MoonKeep = 0.15f;
        /// <summary>What it keeps of the fog / edge-fade colour. MUST equal
        /// PSX_SKY_DARK_KEEP in PSXTone.cginc and PSXSkyReflect.cginc: the fog
        /// colour and the sky's horizon are one pair (the land fades into the
        /// one in front of the other).</summary>
        public const float FogKeep = 0.30f;

        /// <summary>Metres: a lamp counts fully inside this ...</summary>
        public const float NearM = 40f;
        /// <summary>... and not at all past this.</summary>
        public const float FarM = 120f;
        /// <summary>Lamp sum at or under which the eye is in the dark ...</summary>
        public const float LitLo = 1.5f;
        /// <summary>... and at or over which it is on a lit street.</summary>
        public const float LitHi = 5f;
        /// <summary>Seconds: the time constant the play-mode eye eases with.</summary>
        public const float EaseTau = 1.2f;
        /// <summary>The eye moved further than this in one frame: a cut (a
        /// respawn, a replay cut), snapped rather than eased.</summary>
        const float CutJumpM = 20f;

        /// <summary>1 where the whole venue is lit (Charlotte: its skyglow is
        /// ambient light on every road of it), else 0. TimeOfDay.Apply sets it
        /// from UrbanGlow.</summary>
        public static float CityFloor;

        /// <summary>The darkness pushed (0 lit or not night .. 1 fully unlit night).</summary>
        public static float Amount { get; private set; }
        /// <summary>What the eye is easing toward.</summary>
        public static float Target { get; private set; }
        /// <summary>The lamp sum at the last measured eye (logs, shots).</summary>
        public static float LampSum { get; private set; }
        /// <summary>The lit share at the last measured eye, 0..1.</summary>
        public static float LitShare { get; private set; }

        static readonly int DarkId = Shader.PropertyToID("_PSXNightDark");
        static readonly int AmbId = Shader.PropertyToID("_PSXAmbient");
        static readonly int SkyAmbId = Shader.PropertyToID("_PSXSkyAmbient");
        static readonly int LightId = Shader.PropertyToID("_PSXLightColor");
        static readonly int FogId = Shader.PropertyToID("_PSXFogColor");

        static bool haveBase;
        static Color bAmb, bSky, bLight, bFog;
        static float bNight;
        static bool haveEye;
        static Vector3 lastEye;

        /// <summary>How much of the dark an hour can have: NIGHT only (dusk is
        /// a lit sky; the lamps are on but the world is not black yet).</summary>
        public static float NightGate(float night) => Mathf.Clamp01((night - 0.75f) * 4f);

        /// <summary>The lit share for a lamp sum (no city floor).</summary>
        public static float LitFor(float lampSum)
        {
            float x = Mathf.Clamp01((lampSum - LitLo) / (LitHi - LitLo));
            return x * x * (3f - 2f * x);
        }

        /// <summary>One lamp's weight in the sum at distance d.</summary>
        public static float Weight(float d)
        {
            float x = Mathf.Clamp01((d - NearM) / (FarM - NearM));
            return 1f - x * x * (3f - 2f * x);
        }

        /// <summary>A new scene: the next measure snaps.</summary>
        public static void Reset() { haveEye = false; }

        /// <summary>
        /// The eye's measure (StreetLights' scan): <paramref name="lampSum"/>
        /// at <paramref name="eye"/>. Eased by <paramref name="dt"/> in play,
        /// taken at once when <paramref name="instant"/> (edit mode, tools)
        /// or across a cut. Then pushed.
        /// </summary>
        internal static void Measure(Vector3 eye, float lampSum, float dt, bool instant)
        {
            LampSum = lampSum;
            LitShare = Mathf.Max(CityFloor, LitFor(lampSum));
            // INDOORS (a building you can walk into, WeatherShelter): a room at
            // night is lit by its own lamps, which the street registry never
            // sees - the house and the shops keep the night they had.
            if (LitShare < 1f && WeatherShelter.Contains(eye)) LitShare = 1f;
            Target = Enabled && haveBase ? NightGate(bNight) * (1f - LitShare) : 0f;
            bool cut = instant || !haveEye || (eye - lastEye).sqrMagnitude > CutJumpM * CutJumpM;
            haveEye = true;
            lastEye = eye;
            if (cut) Amount = Target;
            else
            {
                Amount = Mathf.Lerp(Amount, Target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / EaseTau));
                if (Mathf.Abs(Amount - Target) < 1e-3f) Amount = Target;
            }
            Push();
        }

        /// <summary>
        /// PSXGlobals.Apply's light, as it pushed it: kept as the base the dark
        /// scales, then pushed scaled by the current Amount. Not night (or the
        /// switch off): Amount goes to 0 at once, so a day frame is exactly
        /// the frame PSXGlobals pushed.
        /// </summary>
        internal static void FromGlobals(Color ambient, Color skyAmbient, Color light, Color fog, float night)
        {
            bAmb = ambient; bSky = skyAmbient; bLight = light; bFog = fog; bNight = night;
            haveBase = true;
            if (!Enabled || NightGate(night) <= 0f) { Amount = 0f; Target = 0f; }
            Push();
        }

        /// <summary>The scaled light and _PSXNightDark, from the base and the
        /// current Amount. Every camera's render pushes it (the stale-global
        /// rule: always written).</summary>
        public static void Push()
        {
            float k = Amount;
            Shader.SetGlobalFloat(DarkId, k);
            if (!haveBase) return;
            Shader.SetGlobalColor(AmbId, Mul(bAmb, Mathf.Lerp(1f, AmbientKeep, k)));
            Shader.SetGlobalColor(SkyAmbId, Mul(bSky, Mathf.Lerp(1f, AmbientKeep, k)));
            Shader.SetGlobalColor(LightId, Mul(bLight, Mathf.Lerp(1f, MoonKeep, k)));
            Shader.SetGlobalColor(FogId, Mul(bFog, Mathf.Lerp(1f, FogKeep, k)));
        }

        /// <summary>The scene's PSXGlobals went away: nothing to scale, and no
        /// stale dark left on a menu's grade.</summary>
        public static void Clear()
        {
            haveBase = false;
            haveEye = false;
            Amount = 0f;
            Target = 0f;
            Shader.SetGlobalFloat(DarkId, 0f);
        }

        static Color Mul(Color c, float s) => s == 1f ? c : new Color(c.r * s, c.g * s, c.b * s, c.a);
    }
}
