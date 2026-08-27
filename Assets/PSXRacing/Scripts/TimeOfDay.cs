using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The seven hours the game can be raced at, and everything each of them
    /// changes: sun angle, sun colour and strength, ambient, the fog band, the
    /// three sky gradient stops, and whether cars run their lights.
    ///
    /// This replaces three hard-coded arrays in RaceHandoffApplier that only
    /// knew morning / afternoon / night. Splitting the look out here means the
    /// scene builder, the race handoff and the LifeSim's picker all read the
    /// same table, and a new hour is one entry rather than four parallel edits.
    ///
    /// Fog does most of the work. The draw distance is 360 m and the circuits
    /// are up to 660 m across, so what the player reads as "time of day" is
    /// mostly the colour the world fades into and how close in it starts —
    /// which is exactly how a PS1 game got its atmosphere too.
    /// </summary>
    public static class TimeOfDay
    {
        public struct Preset
        {
            public string name;
            public string clock;
            public Vector3 sunEuler;
            public Color sunColor;
            public float sunIntensity;
            public Color ambient;
            public Color fogColor;
            public float fogNear, fogFar;
            public Color skyTop, skyHorizon, skyBottom;
            public float skySharpness;
            /// <summary>Cars run headlights and tail lights at this hour.</summary>
            public bool lightsOn;
        }

        public const int Dawn = 0, Morning = 1, Noon = 2, Afternoon = 3,
                         Sunset = 4, Dusk = 5, Night = 6;

        public static readonly Preset[] All =
        {
            new Preset
            {
                name = "DAWN", clock = "05:40",
                sunEuler = new Vector3(6f, -96f, 0f),
                sunColor = new Color(1.00f, 0.68f, 0.58f), sunIntensity = 0.72f,
                ambient = new Color(0.34f, 0.33f, 0.46f),
                fogColor = new Color(0.72f, 0.55f, 0.58f), fogNear = 60f, fogFar = 250f,
                skyTop = new Color(0.16f, 0.18f, 0.38f),
                skyHorizon = new Color(0.95f, 0.62f, 0.55f),
                skyBottom = new Color(0.20f, 0.18f, 0.24f),
                skySharpness = 4.5f, lightsOn = true,
            },
            new Preset
            {
                name = "MORNING", clock = "08:30",
                sunEuler = new Vector3(24f, -70f, 0f),
                sunColor = new Color(1.00f, 0.86f, 0.68f), sunIntensity = 1.05f,
                ambient = new Color(0.44f, 0.44f, 0.50f),
                fogColor = new Color(0.80f, 0.80f, 0.78f), fogNear = 105f, fogFar = 330f,
                skyTop = new Color(0.24f, 0.40f, 0.72f),
                skyHorizon = new Color(0.86f, 0.86f, 0.80f),
                skyBottom = new Color(0.28f, 0.30f, 0.30f),
                skySharpness = 6f, lightsOn = false,
            },
            new Preset
            {
                name = "NOON", clock = "12:30",
                sunEuler = new Vector3(68f, -22f, 0f),
                sunColor = new Color(1.00f, 0.98f, 0.92f), sunIntensity = 1.34f,
                ambient = new Color(0.52f, 0.53f, 0.58f),
                fogColor = new Color(0.74f, 0.82f, 0.90f), fogNear = 150f, fogFar = 355f,
                skyTop = new Color(0.20f, 0.44f, 0.86f),
                skyHorizon = new Color(0.72f, 0.85f, 0.96f),
                skyBottom = new Color(0.34f, 0.38f, 0.40f),
                skySharpness = 8f, lightsOn = false,
            },
            new Preset
            {
                name = "AFTERNOON", clock = "16:10",
                sunEuler = new Vector3(38f, 44f, 0f),
                sunColor = new Color(1.00f, 0.92f, 0.78f), sunIntensity = 1.20f,
                ambient = new Color(0.48f, 0.46f, 0.48f),
                fogColor = new Color(0.84f, 0.78f, 0.68f), fogNear = 120f, fogFar = 335f,
                skyTop = new Color(0.24f, 0.44f, 0.78f),
                skyHorizon = new Color(0.90f, 0.82f, 0.66f),
                skyBottom = new Color(0.30f, 0.28f, 0.26f),
                skySharpness = 6f, lightsOn = false,
            },
            new Preset
            {
                // The look the game shipped with, and still its signature: the
                // sky material's own defaults are this hour.
                name = "SUNSET", clock = "19:10",
                sunEuler = new Vector3(7f, 104f, 0f),
                sunColor = new Color(1.00f, 0.66f, 0.40f), sunIntensity = 1.05f,
                ambient = new Color(0.40f, 0.36f, 0.44f),
                fogColor = new Color(0.88f, 0.56f, 0.42f), fogNear = 75f, fogFar = 265f,
                skyTop = new Color(0.18f, 0.16f, 0.38f),
                skyHorizon = new Color(0.98f, 0.58f, 0.36f),
                skyBottom = new Color(0.25f, 0.20f, 0.22f),
                skySharpness = 5f, lightsOn = true,
            },
            new Preset
            {
                // Blue hour: the sun is BELOW the horizon, so almost everything
                // is ambient and the shading goes flat. That flatness is the
                // effect, not a bug in it.
                name = "DUSK", clock = "20:25",
                sunEuler = new Vector3(-5f, 116f, 0f),
                sunColor = new Color(0.62f, 0.56f, 0.82f), sunIntensity = 0.55f,
                ambient = new Color(0.26f, 0.26f, 0.38f),
                fogColor = new Color(0.30f, 0.26f, 0.40f), fogNear = 58f, fogFar = 215f,
                skyTop = new Color(0.09f, 0.09f, 0.24f),
                skyHorizon = new Color(0.52f, 0.32f, 0.42f),
                skyBottom = new Color(0.12f, 0.11f, 0.16f),
                skySharpness = 4f, lightsOn = true,
            },
            new Preset
            {
                name = "NIGHT", clock = "23:15",
                sunEuler = new Vector3(16f, 148f, 0f),
                sunColor = new Color(0.42f, 0.48f, 0.78f), sunIntensity = 0.38f,
                ambient = new Color(0.16f, 0.16f, 0.28f),
                fogColor = new Color(0.10f, 0.10f, 0.20f), fogNear = 45f, fogFar = 190f,
                skyTop = new Color(0.03f, 0.03f, 0.10f),
                skyHorizon = new Color(0.13f, 0.13f, 0.26f),
                skyBottom = new Color(0.04f, 0.04f, 0.08f),
                skySharpness = 3f, lightsOn = true,
            },
        };

        public static int Count => All.Length;

        public static Preset At(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];

        public static string Label(int index)
        {
            var p = At(index);
            return p.name + " " + p.clock;
        }

        /// <summary>The hour currently applied. Read by lights that spawn after
        /// the applier has already run.</summary>
        public static int Current { get; private set; } = Sunset;

        /// <summary>
        /// Which hour a LifeSim activity slot races at.
        ///
        /// The life sim has three slots and always will — the whole economy is
        /// built on three actions a day — so the seven hours fold into three
        /// bands, and the day number picks within the band. That way racing the
        /// morning slot on Tuesday and on Wednesday are not the same picture,
        /// without adding a fourth slot nobody asked for.
        /// </summary>
        public static int ForSlot(int slot, int day)
        {
            int[] band;
            switch (Mathf.Clamp(slot, 0, 2))
            {
                case 0: band = MorningBand; break;
                case 1: band = AfternoonBand; break;
                default: band = NightBand; break;
            }
            // Deterministic, not random: the same day and slot must give the
            // same hour whether the player is looking at the pre-race quote or
            // already in the car.
            int pick = Mathf.Abs(day * 7 + slot * 3) % band.Length;
            return band[pick];
        }

        static readonly int[] MorningBand = { Morning, Dawn, Morning };
        static readonly int[] AfternoonBand = { Noon, Afternoon };
        static readonly int[] NightBand = { Sunset, Night, Dusk, Night };

        /// <summary>
        /// Push an hour into the scene: the sun, the shader globals PSXGlobals
        /// owns, the sky gradient, and every car's lights.
        ///
        /// The sky material is INSTANCED before it is written to. RenderSettings
        /// holds the shared asset, and writing colours straight into it would
        /// edit Materials/Sky.mat on disk the first time anyone pressed Play in
        /// the editor — the last hour raced would then become the look the next
        /// build shipped with.
        /// </summary>
        public static void Apply(int index, Light sun)
        {
            index = Mathf.Clamp(index, 0, All.Length - 1);
            Current = index;
            var p = All[index];

            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
            }

            var globals = sun != null ? sun.GetComponent<PSXGlobals>() : null;
            if (globals == null) globals = Object.FindFirstObjectByType<PSXGlobals>();
            if (globals != null)
            {
                globals.ambient = p.ambient;
                globals.fogColor = p.fogColor;
                // Through the scene's own fog scale: the hour table stays one
                // table, and a venue that wants to see further (the mountain
                // stage) bakes the multiplier into its PSXGlobals instead of
                // into seven copied presets.
                float s = Mathf.Max(0.01f, globals.fogScale);
                globals.fogNear = p.fogNear * s;
                globals.fogFar = p.fogFar * s;
            }

            ApplySky(p);
            CarLights.SetAll(p.lightsOn);
            NightGlow.SetAll(p.lightsOn);
        }

        static Material skyInstance;

        static void ApplySky(Preset p)
        {
            var src = RenderSettings.skybox;
            if (src == null) return;
            if (skyInstance == null || skyInstance.shader != src.shader)
                skyInstance = new Material(src) { name = "Sky (runtime)" };
            // Reassigned every time: a scene load resets RenderSettings.skybox
            // back to the asset, so holding the instance alone is not enough.
            if (skyInstance.HasProperty("_TopColor")) skyInstance.SetColor("_TopColor", p.skyTop);
            if (skyInstance.HasProperty("_HorizonColor")) skyInstance.SetColor("_HorizonColor", p.skyHorizon);
            if (skyInstance.HasProperty("_BottomColor")) skyInstance.SetColor("_BottomColor", p.skyBottom);
            if (skyInstance.HasProperty("_HorizonSharpness")) skyInstance.SetFloat("_HorizonSharpness", p.skySharpness);
            RenderSettings.skybox = skyInstance;
        }
    }
}
