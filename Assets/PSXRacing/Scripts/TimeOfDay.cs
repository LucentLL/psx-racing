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
    /// Fog does most of the work. The draw distance is 500 m on a circuit and
    /// the circuits are up to 660 m across, so what the player reads as "time
    /// of day" is mostly the colour the world fades into and how close in it
    /// starts — which is exactly how a PS1 game got its atmosphere too. How
    /// HARD it closes is not in this table: that is one renderer-wide curve,
    /// <see cref="FogCurve"/>.
    /// </summary>
    public static class TimeOfDay
    {
        /// <summary>
        /// How hard the fog band is bent (see <see cref="PSXGlobals.fogCurve"/>).
        ///
        /// One number for every hour and every venue, because this is a fact
        /// about the RENDERER — how a band of fog should fall across its own
        /// length — and not about what time it is. The hours already differ in
        /// the two things that are theirs: the colour the world fades into and
        /// how far away it starts.
        ///
        /// Written into the live PSXGlobals on every race, so it is the one
        /// authority: a scene baked before the field existed and a scene baked
        /// after it behave identically, which is not true of anything this
        /// project has ever left to a serialized default.
        /// </summary>
        public const float FogCurve = 2.2f;

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

            // ---- the panorama ----
            /// <summary>Which sky photograph hangs at this hour, by file name
            /// under <c>Resources/Sky/</c>. Empty falls back to the three-stop
            /// gradient, which is what every hour was before this.</summary>
            public string skyTex;
            /// <summary>Where the sun sits in THAT image, in degrees across it
            /// (0 at the left edge, 360 at the right). Every sky in the pack
            /// bakes its sun at 270, but storing it per hour is what lets the
            /// panorama be turned to face the scene's own sun instead of the
            /// scene being rebuilt to face the panorama — see ApplySky.</summary>
            public float skyTexAzimuth;
            /// <summary>How hard the photograph is pulled toward the three
            /// stops above. 0 is the raw image, 1 is the image's structure
            /// wearing the hour's colour entirely. The hours whose look is
            /// ALREADY the photograph (sunset, dawn) want little; the ones
            /// borrowing a sky from a different time of day want a lot.</summary>
            public float skyTint;
            /// <summary>Brightness multiplier on the photograph. These are
            /// rendered skies with a real sun in them, so the bright ones come
            /// in hotter than a game sky should be.</summary>
            public float skyExposure;
            /// <summary>Star field strength. Occluded by whatever cloud the
            /// photograph has, so this is the count you would see through a
            /// clear gap rather than a flat overlay.</summary>
            public float skyStars;
            /// <summary>Cars run headlights and tail lights at this hour.</summary>
            public bool lightsOn;
        }

        public const int Dawn = 0, Morning = 1, Noon = 2, Afternoon = 3,
                         Sunset = 4, Dusk = 5, Night = 6;

        // THE NIGHT GOT DARK (2026-09-21, the NFS pass). The owner, on Need
        // for Speed (2015): "I like how dark the night is, how much the skybox
        // effects the color and mood of the world and cars, how street lights
        // bathe the road." That was MEASURED, not eyeballed, off NFS night
        // frames: the display-luma floor (darkest 0.1%) sits at 0.008-0.015,
        // the median at 0.09-0.17, 27-54% of the frame is under 0.10 and
        // 60-94% under 0.20, highlights still reach 0.95+, and the mean
        // saturation is 0.50-0.58 — dark, but full of colour. The blue-hour
        // frame had mids near (.20,.22,.31) under a sky mid of (.25,.39,.59).
        // Ours, measured the same way on the old NIGHT: floor 0.108, median
        // 0.226, NOTHING under 0.10, saturation 0.13 — a milky grey. Two
        // things made that grey: the film grade's matte lift, which forbade
        // anything darker than 0.11 (PSX/Blit now fades it out with
        // _PSXGradeNight), and THIS table, whose night ambient and fog were
        // bright enough to light an empty road like an overcast afternoon.
        //
        // So NIGHT and DUSK were re-authored darker and bluer below, and
        // nothing else was: DAWN to SUNSET are exactly what the owner signed
        // off. The street lamps (StreetLights) and the headlights are what
        // light a night road now — which is the point: in the NFS frames the
        // road is bright where a lamp stands over it and near-black between.
        // What the hour ALSO means (how wet the road is, how night-graded the
        // picture is, the shadow tint, the city's sodium skyglow) is in the
        // functions under Apply, keyed off the same index, so the table stays
        // one table.
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
                skyTex = "sky_dawn", skyTexAzimuth = 270f,
                skyTint = 0.30f, skyExposure = 1.00f, skyStars = 0.15f,
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
                skyTex = "sky_morning", skyTexAzimuth = 270f,
                skyTint = 0.30f, skyExposure = 1.00f, skyStars = 0.00f,
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
                skyTex = "sky_noon", skyTexAzimuth = 270f,
                skyTint = 0.26f, skyExposure = 1.08f, skyStars = 0.00f,
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
                skyTex = "sky_afternoon", skyTexAzimuth = 270f,
                skyTint = 0.30f, skyExposure = 1.00f, skyStars = 0.00f,
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
                skyTex = "sky_sunset", skyTexAzimuth = 270f,
                skyTint = 0.20f, skyExposure = 1.00f, skyStars = 0.00f,
            },
            new Preset
            {
                // Blue hour. The sun is down, but the light is not gone: the
                // afterglow on the western horizon is still ONE direction,
                // and a car at dusk has a side that faces it and a side that
                // does not. This used to put the sun five degrees BELOW the
                // horizon and call the flatness the effect; the owner's
                // screenshot of that hour was the one that read as "covered
                // in flour". Two and a half degrees up keeps it a single
                // light source (every hour has exactly one: a sun or a moon)
                // and puts a sliver of it on the roof.
                //
                // 2026-09-21: BLUE hour, now, where it was a mauve one. The
                // NFS dusk frame is blue through and through — mids near
                // (.20,.22,.31), the sky mid (.25,.39,.59), the road
                // reflecting that sky — and the old purple-pink afterglow
                // read as a sunset that had not finished. Ambient, sun, fog
                // and horizon all moved to blue TOGETHER (the fog colour and
                // the horizon behind it are one pair: move one alone and the
                // terrain ends at a line against the sky), the sun came down
                // to 0.50 so the lamps and headlights start to matter, and
                // the photograph is pulled harder toward the stops (tint
                // 0.55).
                //
                // THE PHOTOGRAPH CHANGED TOO. sky_dusk.png was the pack's
                // Panorama_Sky_23 - pink cotton-candy cloud, mean rgb
                // (.63,.46,.52) - and no tint can make a pink sky blue: pink
                // times a blue gradient is purple, which is what the first
                // shots of this pass showed. It is now Panorama_Sky_18 (same
                // CC0 pack, same 1024x512 file, same GUID): steel-blue cloud
                // (.35,.37,.42) over a low warm glow, its sun baked at azimuth
                // 268 and 8 degrees up - the pack's usual 270, so the rotation
                // that turns every sky to face its light needs nothing new.
                // The old one is Sky_23 if the pink is ever wanted back.
                name = "DUSK", clock = "20:25",
                sunEuler = new Vector3(2.5f, 116f, 0f),
                sunColor = new Color(0.60f, 0.62f, 0.92f), sunIntensity = 0.50f,
                ambient = new Color(0.20f, 0.24f, 0.38f),
                // Sky and fog raised together (horizon and fog are one pair)
                // after the second round of shots: the frame's sky measured
                // (.21,.22,.29) against the NFS blue hour's (.25,.39,.59) while
                // the GROUND was already a touch brighter than NFS's - so only
                // the sky, the horizon and the fog that meets it went up.
                fogColor = new Color(0.22f, 0.29f, 0.48f), fogNear = 58f, fogFar = 215f,
                skyTop = new Color(0.09f, 0.15f, 0.40f),
                skyHorizon = new Color(0.38f, 0.46f, 0.70f),
                skyBottom = new Color(0.12f, 0.11f, 0.16f),
                skySharpness = 4f, lightsOn = true,
                skyTex = "sky_dusk", skyTexAzimuth = 270f,
                // 0.72 / 1.10: the steel-blue panorama at 0.55 / 0.95 measured
                // (.23,.24,.30) in the sky against the NFS frame's (.25,.39,.59)
                // - right hue family, too grey and too dim.
                skyTint = 0.72f, skyExposure = 1.25f, skyStars = 0.45f,
            },
            new Preset
            {
                // 2026-09-21: DARK. Ambient roughly halved (it was bright
                // enough to read an unlit road by), the moon at 0.22 — still
                // ONE light with a side that faces it, just a dim one — and
                // fog and horizon taken down together, in step, to a deep
                // blue that the city's sodium skyglow (Apply) then warms. The
                // panorama at 0.55 exposure: the stars stay (1.0), the
                // photograph's cloud stops glowing like a lit ceiling. Most
                // of what a player sees at this hour is now what a LAMP lights,
                // which is the NFS picture the owner pointed at.
                name = "NIGHT", clock = "23:15",
                sunEuler = new Vector3(16f, 148f, 0f),
                sunColor = new Color(0.42f, 0.48f, 0.78f), sunIntensity = 0.22f,
                ambient = new Color(0.075f, 0.080f, 0.125f),
                fogColor = new Color(0.045f, 0.050f, 0.090f), fogNear = 45f, fogFar = 190f,
                skyTop = new Color(0.015f, 0.018f, 0.05f),
                skyHorizon = new Color(0.07f, 0.075f, 0.14f),
                skyBottom = new Color(0.04f, 0.04f, 0.08f),
                skySharpness = 3f, lightsOn = true,
                skyTex = "sky_night", skyTexAzimuth = 270f,
                skyTint = 0.55f, skyExposure = 0.55f, skyStars = 1.00f,
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

            // THE WEATHER RIDES ON TOP OF THE HOUR. The preset is a struct,
            // so this is a copy being adjusted and the table stays the table:
            // overcast darkens the sky and the ambient, everything but clear
            // air closes the fog in and runs the lights. See Seasons.
            var weather = Seasons.CurrentWeather;
            p.skyExposure *= Seasons.SkyMul(weather);
            p.ambient *= Seasons.AmbientMul(weather);
            // And the SUN: a rainy noon used to keep the full hard sun of a
            // clear one — crisp shadow sides under a sky that had just been
            // darkened to 60% — which is the one tell that a weather layer
            // is a filter over a sunny scene. See SunMul.
            p.sunIntensity *= SunMul(weather);
            bool lights = p.lightsOn || Seasons.LightsOn(weather);

            // THE CITY LIGHTS ITS OWN SKY. After the weather, so an overcast
            // city night is a murk the cloud holds down rather than a clear
            // sky the weather then greys. See ApplySkyglow.
            float urban = UrbanGlow();
            ApplySkyglow(ref p, SkyglowFor(index) * urban);

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
                float s = Mathf.Max(0.01f, globals.fogScale) * Seasons.FogMul(weather);
                globals.fogNear = p.fogNear * s;
                globals.fogFar = p.fogFar * s;
                globals.fogCurve = FogCurve;
                globals.skyAmbient = SkyAmbientFor(p);
                // What the hour means beyond its light (2026-09-21, the NFS
                // pass). FIELDS, not Shader.SetGlobal: PSXGlobals re-pushes
                // every one of its fields every frame, edit mode included, so
                // a global set here directly would be overwritten on the next
                // tick — and a scene that never applies an hour keeps the
                // fields' zero defaults, which is today's picture exactly.
                globals.wetness = WetnessFor(index, weather);
                globals.night = NightFor(index);
                globals.gradeNight = GradeNightFor(index);
                globals.mood = MoodFor(index, urban);
                // And what the hour means for the SUN (the day pass): the
                // per-pixel, shouldered light; how hard its shadows are; how
                // much brighter the haze is toward it.
                globals.sunModel = 1f;
                globals.sunShadow = ShadowFor(index, weather);
                globals.skyShade = SkyShadeFor(index);
                globals.fogSun = FogSunFor(index, weather);
            }

            ApplySky(p, sun);
            CarLights.SetAll(lights);
            NightGlow.SetAll(p.lightsOn);
        }

        /// <summary>
        /// The ambient light an upward face gets: the hour's ambient, pulled
        /// toward the colour of its own sky at the same brightness. Same
        /// luminance as the plain ambient on purpose: this changes the HUE
        /// of the light from above, not how much of it there is, so no scene
        /// gets brighter or darker than it was tuned to be. PSXGlobals hands
        /// it to the shaders as _PSXSkyAmbient.
        ///
        /// The pull was 0.55 and is 0.75 since 2026-09-21: "how much the
        /// skybox effects the color and mood of the world and cars" was the
        /// owner's second NFS point, and in those frames a roof under a blue
        /// dusk IS blue and a car under a sodium city sky IS orange. Still at
        /// the ambient's own luminance, so no hour got brighter for it.
        /// </summary>
        public static Color SkyAmbientFor(Preset p)
        {
            Color sky = p.skyTop * 0.55f + p.skyHorizon * 0.45f;
            float lumA = Lum(p.ambient);
            float lumS = Lum(sky);
            Color skyAtAmbient = lumS > 1e-3f ? sky * (lumA / lumS) : p.ambient;
            var c = Color.Lerp(p.ambient, skyAtAmbient, SkyAmbientPull);
            c.a = 1f;
            return c;
        }

        /// <summary>How far an upward face's ambient leans to the sky's hue
        /// (0 = plain ambient, 1 = the sky's colour at the ambient's
        /// brightness). See <see cref="SkyAmbientFor"/>.</summary>
        public const float SkyAmbientPull = 0.75f;

        /// <summary>The luminance weights every colour sum in this file uses.
        /// (The same Rec.601 weights, rounded, that SkyAmbientFor always
        /// had; one helper so the skyglow and the sky ambient cannot drift
        /// onto two different ideas of "the same brightness".)</summary>
        static float Lum(Color c) => c.r * 0.30f + c.g * 0.59f + c.b * 0.11f;

        // ================================================================
        //  What the hour means besides its light (2026-09-21, the NFS pass)
        // ================================================================
        //
        // Everything below is keyed off the same hour index as the table, and
        // is written into the scene's PSXGlobals by Apply. Each one is a
        // function rather than a Preset field for two reasons: several of
        // them fold in the WEATHER or the VENUE, which a per-hour field cannot
        // know, and a new Preset field would default to zero in every one of
        // the seven entries that did not spell it out — a silent black hour
        // the first time somebody added one.

        /// <summary>
        /// How wet the roads are, 0..1 — PSXGlobals.wetness, which the road
        /// shaders multiply by each material's own <c>_Wet</c> mask (a road
        /// is 1, a verge is 0) to darken the asphalt and reflect the sky, the
        /// lamps and the headlights in it.
        ///
        /// Rain soaks everything. Fog leaves a film and snow a slush, both
        /// less. And CLEAR NIGHTS ARE DAMP: 0.24 at night, 0.18 at dusk and
        /// dawn, a breath of it at sunset, nothing in daylight. (It was 0.40 /
        /// 0.30 until the owner, 2026-09-21: "the roads are a bit too
        /// reflective when it's not raining" - so the damp is 60% of what it
        /// was and rain, at 1, is as wet as ever: the gap between a dry night
        /// and a wet one is the thing that got wider.) That last one
        /// is an art licence, not meteorology — NFS (2015) is wet every night
        /// whatever the sky is doing, and a streak of sodium light down a
        /// damp road is most of what the owner meant by "street lights bathe
        /// the road". It is visual only: grip stays the weather's alone
        /// (Seasons.GripMult), so a damp clear night drives like a dry one.
        ///
        /// Weather never makes a road DRIER than the hour already has it: a
        /// fog or snow night takes whichever of the two is wetter.
        /// </summary>
        public static float WetnessFor(int hour, Weather w)
        {
            float damp = DampFor(hour);
            switch (w)
            {
                case Weather.Rain: return 1f;
                case Weather.Fog:  return Mathf.Max(0.55f, damp);
                case Weather.Snow: return Mathf.Max(0.35f, damp);
                default: return damp;
            }
        }

        /// <summary>The clear-sky dampness of an hour (see WetnessFor).</summary>
        static float DampFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night:  return 0.24f;
                case Dusk:   return 0.18f;
                case Dawn:   return 0.18f;
                case Sunset: return 0.07f;
                default:     return 0f;
            }
        }

        /// <summary>
        /// How hard the sun's shadows are, 0..1 — PSXGlobals.sunShadow, which
        /// SunShadows and PSXSunShadow.cginc read.
        ///
        /// Full from morning to afternoon. A little less at sunset and at
        /// dawn: the sun is 6-7 degrees up, every shadow is ten times as long
        /// as the thing casting it, and a road that is ALL shadow reads as an
        /// overcast one - the leak keeps the raking light the hour is for.
        /// NOTHING at dusk and at night: the dusk "sun" is an afterglow two
        /// and a half degrees up, kept as one soft direction on purpose, and
        /// the night belongs to the lamps (whose frames are also the ones
        /// that can least afford a second pass over the scene).
        ///
        /// Weather takes the rest: under rain cloud the light comes from the
        /// whole sky and a shadow is a smudge under the car, which is the
        /// blob shadow's job. Measured off the owner's overcast Forza frame:
        /// no cast shadow anywhere in it, and a contact patch under the car.
        /// </summary>
        public static float ShadowFor(int hour, Weather w)
        {
            float s;
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Morning: case Noon: case Afternoon: s = 1f; break;
                case Dawn: case Sunset: s = 0.85f; break;
                default: s = 0f; break;
            }
            switch (w)
            {
                case Weather.Rain: return s * 0.15f;
                case Weather.Fog:  return s * 0.20f;
                case Weather.Snow: return s * 0.45f;
                default: return s;
            }
        }

        /// <summary>
        /// How much of its ambient a roofed-over place loses, 0..1 —
        /// PSXGlobals.skyShade, the top-down map (SunShadows). Whatever the
        /// WEATHER: it is the sky being hidden, not the sun, and the darkest
        /// thing in the owner's overcast frame is the ground under the car.
        /// Off from dusk, with the sun's map and for its reasons: those hours
        /// are the lamps', and their ambient is already next to nothing.
        /// </summary>
        public static float SkyShadeFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Dawn: case Morning: case Noon: case Afternoon: case Sunset: return 1f;
                default: return 0f;
            }
        }

        /// <summary>
        /// The sun in the haze — PSXGlobals.fogSun: rgb is what the fog
        /// colour GAINS looking straight at the sun (sRGB, authored like the
        /// table), alpha is how tight the glow is (the exponent on the cosine
        /// of the angle off the sun: 3 is a glow that fills a third of the
        /// horizon, 8 a tighter one).
        ///
        /// Measured, low sun: the far hills toward the sun 0.93, the near
        /// ones 0.45, the sky forty degrees round 0.53 — so distance fades
        /// to something that depends on where you are looking. The lower the
        /// sun the more air its light comes through sideways and the
        /// stronger and warmer the glow; at noon it is overhead, the horizon
        /// is nowhere near it and the term does almost nothing. The SKY's
        /// horizon band takes the same term (PSXSky), because the fog colour
        /// and the horizon behind it are one pair.
        ///
        /// Under cloud there is no sun to look toward: none.
        /// </summary>
        public static Color FogSunFor(int hour, Weather w)
        {
            if (w != Weather.Clear) return new Color(0f, 0f, 0f, 0f);
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Dawn:      return new Color(0.42f, 0.30f, 0.24f, 5f);
                case Morning:   return new Color(0.36f, 0.33f, 0.27f, 4f);
                case Noon:      return new Color(0.16f, 0.16f, 0.15f, 3f);
                case Afternoon: return new Color(0.36f, 0.31f, 0.22f, 4f);
                case Sunset:    return new Color(0.44f, 0.28f, 0.14f, 5f);
                default:        return new Color(0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// How night-time an hour is, 0..1 — PSXGlobals.night. The lit
        /// windows on the city's facades and the lens dirt read it: how many
        /// windows are lit, and - past its midpoint only, so sunset and dawn
        /// keep a clean lens (LensFx.DirtFor) - how much dust a lamp shows up
        /// on the glass. Not
        /// the same thing as <see cref="Preset.lightsOn"/>, which is a yes/no
        /// about headlights and is already yes at sunset and dawn.
        /// </summary>
        public static float NightFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night:  return 1f;
                case Dusk:   return 0.75f;
                case Dawn:   return 0.5f;
                case Sunset: return 0.3f;
                default:     return 0f;
            }
        }

        /// <summary>
        /// How much of the NIGHT grade PSX/Blit uses, 0..1 —
        /// PSXGlobals.gradeNight. At 1 the film grade's matte lift is mostly
        /// gone (a lift is lens veiling glare, and veiling glare scales with
        /// how much light the scene has), its vignette is deeper and its
        /// blues keep their colour. At 0 the grade is BIT-IDENTICAL to the one
        /// the owner signed off, which is every daylight hour. Kept a separate
        /// curve from <see cref="NightFor"/> so the grade can be tuned without
        /// moving which windows light up.
        /// </summary>
        public static float GradeNightFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night:  return 1f;
                case Dusk:   return 0.7f;
                case Dawn:   return 0.5f;
                case Sunset: return 0.25f;
                default:     return 0f;
            }
        }

        /// <summary>
        /// How much of a city the loaded scene is, for the skyglow and the
        /// night mood: 1 in the streamed Charlotte (a CityWorld is there), 0.6
        /// on a venue with street lamps (a NightGlow is there — the circuits'
        /// lit streets, the town's meet lot), 0 out on a mountain stage where
        /// the only light is your own.
        ///
        /// FindAnyObjectByType, not NightGlow's own static list: it works in
        /// edit mode, where the screenshot tools apply hours to scenes whose
        /// components have never run Awake. Active objects only — a lamp
        /// group somebody switched off is not lighting any sky. Called once
        /// per Apply (once per scene load), so the scene walk is affordable.
        /// </summary>
        public static float UrbanGlow()
        {
            if (Object.FindAnyObjectByType<City.CityWorld>() != null) return 1f;
            if (Object.FindAnyObjectByType<NightGlow>() != null) return 0.6f;
            return 0f;
        }

        /// <summary>
        /// The shadow tint of an hour — PSXGlobals.mood, which PSX/Blit's
        /// grade split-tones into the darks (rgb = the hue at any
        /// brightness, a = how much). NFS darks are never neutral: a city
        /// night's murk is sodium-brown, a mountain night's is blue-grey, blue
        /// hour is blue all the way down. Venue-aware at night only — by dusk
        /// the sky is still brighter than any street lamp.
        /// </summary>
        public static Color MoodFor(int hour, float urban)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night:
                {
                    var c = Color.Lerp(RuralNightMood, UrbanNightMood, Mathf.Clamp01(urban));
                    // A city's darks are its sodium murk, and harder than a
                    // mountain's blue: the NFS city frames' darkest 5% measure
                    // SATURATED brown-orange (.037,.026,.002), the mountain
                    // road's a neutral violet. First shots at a flat 0.22 had
                    // the city darks at a grey (.03,.02,.025).
                    c.a = 0.22f + 0.12f * Mathf.Clamp01(urban);
                    return c;
                }
                // Blue hour: the one hour whose shadows are its sky's colour.
                case Dusk:   return new Color(0.60f, 0.70f, 1.00f, 0.32f);
                case Sunset: return new Color(1.00f, 0.70f, 0.75f, 0.10f);
                case Dawn:   return new Color(0.85f, 0.75f, 1.00f, 0.10f);
                default:     return new Color(0f, 0f, 0f, 0f);
            }
        }

        static readonly Color RuralNightMood = new Color(0.55f, 0.62f, 1.00f, 1f);
        static readonly Color UrbanNightMood = new Color(1.00f, 0.72f, 0.42f, 1f);

        /// <summary>
        /// What weather leaves of the SUN: rain 0.50, fog 0.70, snow 0.80.
        /// Kept here rather than in Seasons beside SkyMul and AmbientMul
        /// because it is a statement about the directional light, which only
        /// this file touches. Overcast is the SHADOW going soft as much as
        /// the light going dim: halve the key and the ambient (barely
        /// dimmed) becomes most of the light, which is what a wet afternoon
        /// looks like.
        /// </summary>
        public static float SunMul(Weather w)
        {
            switch (w)
            {
                case Weather.Rain: return 0.50f;
                case Weather.Fog:  return 0.70f;
                case Weather.Snow: return 0.80f;
                default: return 1f;
            }
        }

        /// <summary>How much of a city's sodium skyglow an hour shows before
        /// the venue scales it: all of it at night, half at dusk (the sky is
        /// still lit), a little at dawn, none while the sun is up.</summary>
        static float SkyglowFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night: return 1f;
                case Dusk:  return 0.5f;
                case Dawn:  return 0.3f;
                default:    return 0f;
            }
        }

        /// <summary>The colour a sodium-lit city throws up into its own haze:
        /// sRGB, authored like every colour in the table (PSXGlobals and the
        /// sky material convert to linear on the way in).</summary>
        static readonly Color SodiumMurk = new Color(0.20f, 0.12f, 0.055f, 1f);

        /// <summary>
        /// SKYGLOW. A city at night lights the underside of its own haze:
        /// the fog a street fades into is not the deep blue of a mountain
        /// night but a brown-orange murk, and the sky over the rooftops
        /// glows the same colour down to the horizon. NFS's city frames are
        /// that murk from edge to edge (their darks measure ~(.037,.026,.002),
        /// sodium-warm, where the mountain frame's are neutral).
        ///
        /// THE FOG COLOUR AND THE SKY HORIZON MOVE TOGETHER, always. They are
        /// one pair: the terrain fades into the fog colour and the sky behind
        /// it is the horizon colour, and moving one without the other draws a
        /// line where the world ends against a brighter or darker sky (the
        /// fog-distance note in this project's history). The top of the sky
        /// takes less of it — the glow is a dome over the horizon — and the
        /// ambient leans toward the murk's HUE at its own brightness, so the
        /// city gets warmer and not brighter. <paramref name="g"/> 0 is a
        /// no-op, which is every daylight hour and every stage.
        /// </summary>
        static void ApplySkyglow(ref Preset p, float g)
        {
            if (g <= 0f) return;
            p.fogColor = Opaque(Color.Lerp(p.fogColor, SodiumMurk, 0.65f * g));
            p.skyHorizon = Opaque(Color.Lerp(p.skyHorizon, SodiumMurk * 1.35f, 0.70f * g));
            p.skyTop = Opaque(Color.Lerp(p.skyTop, SodiumMurk * 0.40f, 0.30f * g));
            float lumA = Lum(p.ambient);
            Color murkAtAmbient = SodiumMurk * (lumA / Lum(SodiumMurk));
            // The ambient keeps its own alpha (the weather multiply above
            // already scaled it along with the colour, as it always has).
            float a = p.ambient.a;
            p.ambient = Color.Lerp(p.ambient, murkAtAmbient, 0.5f * g);
            p.ambient.a = a;
        }

        /// <summary>A Color scaled or lerped toward a scaled one carries the
        /// scale into alpha too; the sky material and the fog are handed
        /// opaque colours, as the table authors them.</summary>
        static Color Opaque(Color c) { c.a = 1f; return c; }

        static Material skyInstance;
        static readonly System.Collections.Generic.Dictionary<string, Texture2D> skyTextures =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>
        /// Load a sky panorama once and keep it.
        ///
        /// Cached including its MISSES — a null entry is a real answer here.
        /// Without that, an hour whose texture failed to import would hit
        /// Resources.Load every single time the hour was applied, which on a
        /// scene load is the loading screen and on a WebGL build is a stall
        /// looking for a file that is not there.
        /// </summary>
        static Texture2D SkyTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (skyTextures.TryGetValue(name, out var tex)) return tex;
            tex = Resources.Load<Texture2D>("Sky/" + name);
            skyTextures[name] = tex;
            return tex;
        }

        /// <summary>
        /// TURN THE SKY TO FACE THE SUN.
        ///
        /// Every panorama in the pack bakes its sun at the same place in the
        /// image, and the seven hours put their directional light in seven
        /// different places. Left alone that is a sunset glowing in the north
        /// while the light rakes in from the west — the single thing that would
        /// give the whole trick away.
        ///
        /// Derived from the light's own transform rather than from the hour's
        /// sunEuler, so it stays right if a scene aims its sun somewhere else
        /// (the city does) and there is no second copy of the angle to keep in
        /// step. The sun is the direction light comes FROM, hence -forward.
        /// </summary>
        static float SkyRotationFor(Preset p, Light sun)
        {
            // The live light where there is one, the hour table where there
            // is not. A scene with no sun still has a sky, and leaving that at
            // rotation zero would point every panorama at world +Z regardless
            // of the hour it is meant to be.
            Vector3 toSun = sun != null ? -sun.transform.forward
                                        : -(Quaternion.Euler(p.sunEuler) * Vector3.forward);
            // Straight up or straight down has no azimuth to match; leaving the
            // panorama where it is beats snapping it to whatever atan2 returns
            // for a zero-length vector.
            if (new Vector2(toSun.x, toSun.z).sqrMagnitude < 1e-6f) return 0f;
            float worldAzi = Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
            // The shader samples u = azimuth/360 + 0.5 + rotation/360, so a
            // feature baked at image azimuth T shows up in the world at
            // T - 180 - rotation. Solve that for rotation.
            return p.skyTexAzimuth - 180f - worldAzi;
        }

        static void ApplySky(Preset p, Light sun)
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

            // The panorama, and the four numbers that make it this hour's. A
            // missing texture drops _PanoAmount to zero rather than rendering
            // Unity's white default across the whole sky, so a failed import is
            // the old gradient and not a blank screen.
            if (skyInstance.HasProperty("_MainTex"))
            {
                var tex = SkyTexture(p.skyTex);
                float rot = SkyRotationFor(p, sun);
                skyInstance.SetTexture("_MainTex", tex);
                skyInstance.SetFloat("_PanoAmount", tex != null ? 1f : 0f);
                skyInstance.SetFloat("_Rotation", rot);
                skyInstance.SetFloat("_Tint", p.skyTint);
                skyInstance.SetFloat("_Exposure", Mathf.Max(0.01f, p.skyExposure));
                skyInstance.SetFloat("_Stars", p.skyStars);

                // THE SAME SKY, FOR THE PAINT. PSX/CarPaint reflects the
                // panorama the sky material is showing: this texture, this
                // rotation, this hour tint, so the reflection on a bonnet
                // and the sky behind it are one picture. Globals rather than
                // per-material properties, because a race carries several
                // dozen car materials and none of them is instanced.
                Shader.SetGlobalTexture("_PSXSkyTex", tex != null ? (Texture)tex : Texture2D.blackTexture);
                Shader.SetGlobalFloat("_PSXSkyAmount", tex != null ? 1f : 0f);
                Shader.SetGlobalFloat("_PSXSkyRotation", rot);
                Shader.SetGlobalFloat("_PSXSkyTint", p.skyTint);
                Shader.SetGlobalFloat("_PSXSkyExposure", Mathf.Max(0.01f, p.skyExposure));
                Shader.SetGlobalColor("_PSXSkyTop", p.skyTop);
                Shader.SetGlobalColor("_PSXSkyHorizon", p.skyHorizon);
                Shader.SetGlobalFloat("_PSXSkySharpness", p.skySharpness);
            }
            RenderSettings.skybox = skyInstance;
        }
    }
}
