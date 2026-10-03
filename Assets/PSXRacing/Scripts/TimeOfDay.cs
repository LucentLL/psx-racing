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

        /// <summary>
        /// NO FOG UNLESS IT IS FOGGY (owner rule, 2026-10-03). On a clear,
        /// rainy or snowy day <see cref="Apply"/> lays no distance fog: only
        /// the drawn world's last stretch, from this share of the draw
        /// distance out, fades into the sky's horizon colour (with
        /// <see cref="FogCurve"/> most of that in its last tenth), so the
        /// world never ends in a hard line. A Weather.Fog day keeps the
        /// hour's own band exactly as it was. False puts the old band back on
        /// every day (the before-pictures; nothing in the game sets it).
        /// </summary>
        public const float EdgeFadeStart = 0.80f;
        public static bool FogOnlyWhenFoggy = true;

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
                //
                // 2026-09-29: DARKER STILL - the owner, after driving the
                // colour build: "night is hardly dark at all, even without
                // street lights", beside real night drives and NFS Heat. On
                // those frames (tools/colour/colour_stats.py, "night") the
                // unlit road is Ycode 2-11, a treeline 0-2, the sky 1-20 - and here the
                // unlit town road measured 24-46, its snow 59-62, the Blue
                // Ridge trees read like dusk. The light census (colour-shots
                // -Sets census) split that between the ambient (road +17 of
                // 27 linear, snow +38 of 48) and the moon (about +10 on
                // both): both cut together, to about a sixth of their light -
                // the census showed these two scale the picture in step with
                // the numbers written here (a first cut to 0.44 only took the
                // unlit town snow from 59 to 37 and its white warehouse from
                // 74 to 49) - with the sky, the fog and the horizon taken down
                // alongside, a pair as always (the photograph's exposure 0.55
                // to 0.22). What lights a night road now is the car's own beam
                // and the lamps; the moon keeps a side that faces it, just
                // barely. colour_stats.py night holds the targets.
                name = "NIGHT", clock = "23:15",
                sunEuler = new Vector3(16f, 148f, 0f),
                sunColor = new Color(0.42f, 0.48f, 0.78f), sunIntensity = 0.036f,
                ambient = new Color(0.012f, 0.013f, 0.020f),
                fogColor = new Color(0.019f, 0.022f, 0.040f), fogNear = 45f, fogFar = 190f,
                skyTop = new Color(0.008f, 0.010f, 0.028f),
                skyHorizon = new Color(0.032f, 0.034f, 0.064f),
                skyBottom = new Color(0.016f, 0.016f, 0.032f),
                skySharpness = 3f, lightsOn = true,
                skyTex = "sky_night", skyTexAzimuth = 270f,
                skyTint = 0.55f, skyExposure = 0.22f, skyStars = 1.00f,
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

        // THE NIGHT BLOCK IS NIGHT (2026-09-21). The night band used to be
        // { Sunset, Night, Dusk, Night }: half the nights of a career were a
        // sunset or a blue hour under a lit, cloudy sky, and the owner's
        // report of that was "I am unable to race at night. Even when I
        // select night, the skybox is still afternoon." The bands are the
        // CLOCK now — every hour sits in the block its own clock time falls
        // in (SlotHours: 4-12, 12-20, 20-4) — so SUNSET 19:10 moved to the
        // day block where it belongs, and what the night block hands out
        // unasked is always NIGHT. DUSK is still a night-block hour, but a
        // CHOSEN one: see HoursIn, which the race booking's hour picker reads.
        static readonly int[] MorningBand = { Morning, Dawn, Morning };
        static readonly int[] AfternoonBand = { Noon, Afternoon, Sunset };
        static readonly int[] NightBand = { Night };

        /// <summary>
        /// Every hour whose clock time falls inside a block, in clock order —
        /// what a race written into that block may be run at. The booking's
        /// hour picker steps through these; <see cref="ForSlot"/> is what a
        /// booking with no hour chosen gets.
        /// </summary>
        public static int[] HoursIn(int slot)
        {
            switch (Mathf.Clamp(slot, 0, 2))
            {
                case 0: return MorningHours;
                case 1: return DayHours;
                default: return NightHours;
            }
        }

        static readonly int[] MorningHours = { Dawn, Morning };
        static readonly int[] DayHours = { Noon, Afternoon, Sunset };
        static readonly int[] NightHours = { Dusk, Night };

        /// <summary>Whether an hour belongs to a block. A booking's chosen hour
        /// is only honoured when it does, so a save edited by hand (or a block
        /// table that moves) cannot run a noon race in the night block.</summary>
        public static bool InSlot(int hour, int slot) =>
            System.Array.IndexOf(HoursIn(slot), hour) >= 0;

        /// <summary>The next hour of a block after <paramref name="hour"/>,
        /// wrapping — one tap of the booking's TIME button. An hour that is not
        /// in the block at all (the "block's own" -1) starts from the top.</summary>
        public static int StepHour(int slot, int hour, int dir = 1)
        {
            var hours = HoursIn(slot);
            int i = System.Array.IndexOf(hours, hour);
            if (i < 0) return hours[dir >= 0 ? 0 : hours.Length - 1];
            return hours[((i + dir) % hours.Length + hours.Length) % hours.Length];
        }

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
            var weather = Seasons.CurrentWeather;
            float urban = UrbanGlow();
            var p = Lit(index, weather, urban, out var anchor, out float fill);
            bool lights = p.lightsOn || Seasons.LightsOn(weather);

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
                float scale = Mathf.Max(0.01f, globals.fogScale);
                if (weather == Weather.Fog || !FogOnlyWhenFoggy)
                {
                    // A FOGGY DAY: the hour's band as signed off. Through the
                    // scene's own fog scale: the hour table stays one table,
                    // and a venue that wants to see further (the mountain
                    // stage) bakes the multiplier into its PSXGlobals instead
                    // of into seven copied presets.
                    globals.fogColor = p.fogColor;
                    float s = scale * Seasons.FogMul(weather);
                    globals.fogNear = p.fogNear * s;
                    globals.fogFar = p.fogFar * s;
                }
                else
                {
                    // EVERY OTHER DAY - clear, rain, snow - HAS NO FOG (the
                    // owner, 2026-10-03: "remove fog from the game unless the
                    // weather is foggy. I'm tired of everything in the
                    // distance being white"). Only the last stretch of the
                    // drawn world fades, so it never ends in a hard line: from
                    // EdgeFadeStart of the draw distance to the draw distance
                    // itself, into the sky's own HORIZON colour for the hour
                    // (what PSX/Sky paints the band at the horizon with, dark
                    // at night, blue by day - never whiter than the sky behind
                    // it). The draw distance is noon's band end times the
                    // scene's fogScale: the camera's far plane, which
                    // LifeSimSelfTest holds within 20% inside it (500 m on a
                    // circuit and in Charlotte, inside the city's two-tile
                    // ring; 1,500 m on a stage), so nothing pops in.
                    float edge = All[Noon].fogFar * scale;
                    globals.fogColor = p.skyHorizon;
                    globals.fogNear = edge * EdgeFadeStart;
                    globals.fogFar = edge;
                }
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
                // And what the PAINT reflects below the sky (2026-10-01, "when
                // the sun is up cars still look completely washed out and
                // white"): a sunlit hour's world, not its pale haze colour; the
                // ground white only under the snow dress.
                globals.paintDay = IsSunUp(index) || IsLowSun(index) ? 1f : 0f;
                globals.paintSnow = weather == Weather.Snow ? 1f : 0f;
                // And the COLOUR PASS (Shaders/PSXTone.cginc): one exposure
                // for the hour as the weather left it, the one tone curve, and
                // the halation keyed on light sources. The adaptation (C10) is
                // not the hour's to set.
                globals.tone = ToneEnabled ? 1f : 0f;
                globals.exposure = ExposureFor(p, anchor);
                globals.dayFill = fill;
                globals.emitKey = EmitKeyEnabled ? 1f : 0f;
                // The owner's C11 choice (LookChoices.SunLift, off): the
                // grade's lift fade under a clear sun.
                globals.gradeSun = LookChoices.SunLift ? GradeSunFor(index, weather) : 0f;
            }

            lastSkyPreset = p; lastSkySun = sun; lastSkyIndex = index; lastSkyWeather = weather; skyApplied = true;
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

        // ================================================================
        //  THE EXPOSURE (the colour pass, C2, 2026-09-29)
        // ================================================================

        /// <summary>
        /// THE ANCHOR OF THE EXPOSURE: PSX/Lit's retired light shoulder (toe
        /// 0.80, span 0.50, 2026-09-21), kept here as a NUMBER instead of a
        /// curve. It used to take every light over 0.8 toward 1.3 - a clear
        /// noon's 1.7-1.8 on a sunlit road to 1.21-1.23 - and then nothing
        /// rolled off the RADIANCE, so the owner's sunlit concrete deck sat at
        /// code 198, a snowfield on the clip, and the halation bloomed over
        /// both.
        ///
        /// The exposure is now the shoulder's own gain AT THE HOUR'S SUNLIT
        /// ROAD, applied to every light alike: S(L) / L, in the red and the
        /// green, the smaller of the two. So a sunlit road - the owner's road
        /// colours, which are never to get lighter - takes at most the light
        /// it took before at every hour and weather (the road-colour gate),
        /// while everything the shoulder used to lift or flatten is honest: a
        /// shaded face is darker by the same gain (harsh sun), and a face lit
        /// harder than the road is rolled off by the one tone curve on its
        /// radiance instead of by a flattened light. Measured (sidecars,
        /// 2026-09-29): a clear noon 0.68, a snowy one 0.76, a foggy one 0.82,
        /// a rainy one 0.93; an afternoon 0.86 (0.92 in snow, 0.96 in fog); a
        /// morning 0.98; dawn, sunset, dusk and night exactly 1.
        ///
        /// THE LIGHT IS THE LIGHT AS PUSHED. Shader.SetGlobalColor does NOT
        /// linearise in this project (read back off the GPU, 2026-09-29: the
        /// noon sun arrives as 1.340, 1.313, 1.233 and the noon ambient as
        /// 0.52, 0.53, 0.58 - the table's numbers exactly). The hour table's
        /// light colours are linear multipliers written as colours; the first
        /// cut of this function linearised them (a noon sun of 1.9) and got
        /// every exposure wrong - the deck 10 codes too dark at noon, 5 too
        /// light in snow.
        ///
        /// Two more things the numbers taught:
        ///   * THE ANCHOR ROAD LEANS <see cref="ExposureRoadTiltDeg"/> TOWARD
        ///     THE SUN. The owner's Samuel Street deck does (it climbs toward
        ///     both the noon and the afternoon sun: measured N.L 0.97 at noon
        ///     against a level road's 0.93, 0.72 in the afternoon against
        ///     0.62), and anchored on a level road it came out lighter than
        ///     its baseline.
        ///   * CHANNEL BY CHANNEL (see ExposureFor): on luminance, a snowy
        ///     noon's sunlit deck came out 4.5 codes lighter in its green.
        /// </summary>
        public const float ExposureToe = 0.80f, ExposureSpan = 0.50f;

        /// <summary>How far toward the sun the exposure's anchor road leans
        /// (see <see cref="ExposureToe"/>): the owner's Samuel Street deck's
        /// own lean, the steepest sunlit road the protocol measures. A level
        /// sunlit road then keeps 97-100% of its old light.</summary>
        public const float ExposureRoadTiltDeg = 8f;

        /// <summary>The tone curve's switch for tools: PSX_TONE=0 in the
        /// environment is the picture from before the colour pass (the A/B
        /// the protocol measures). Always on in a build.</summary>
        public static bool ToneEnabled => System.Environment.GetEnvironmentVariable("PSX_TONE") != "0";

        /// <summary>The emitter-keyed halation's switch for tools:
        /// PSX_EMITKEY=0 is the old brightness-keyed glow.</summary>
        public static bool EmitKeyEnabled => System.Environment.GetEnvironmentVariable("PSX_EMITKEY") != "0";

        /// <summary>
        /// The hour's exposure, as the weather and the city's skyglow left its
        /// light: S(L) / L, where L is the light on a sunlit road leaning
        /// <see cref="ExposureRoadTiltDeg"/> toward the sun and S the retired
        /// shoulder (see <see cref="ExposureToe"/>) - 1 whenever L is under
        /// the toe - taken in the red and in the green and the smaller kept.
        /// L is what the shaders receive: the sun's colour x intensity (as
        /// PSXGlobals pushes it, unconverted) times the sine of its elevation
        /// plus the tilt, plus the sky ambient an up-face takes.
        /// </summary>
        public static float ExposureFor(Preset p) => ExposureFor(p, p);

        /// <summary>
        /// The exposure for a preset <paramref name="lit"/> whose sky fill has
        /// been cut (<see cref="DayFillFor(Preset, int, Weather)"/>), anchored
        /// on the SAME preset before the cut (<paramref name="anchor"/>): the
        /// retired shoulder's output for the anchor's sunlit road, over the cut
        /// preset's light on that road. So the sunlit road comes out at the
        /// light it always had - never lighter, never darker - and everything
        /// the sky alone lights is darker by the cut. With no cut it is exactly
        /// S(L) / L, as before. It may pass 1 (a morning, whose low sun sits
        /// under the toe): the eye opening for the darker fill, the sunlit road
        /// still where it was.
        ///
        /// CHANNEL BY CHANNEL: the shoulder rolled each channel off on its
        /// own, so a luminance anchor lets the channel the light is richest in
        /// (the green of a snowy noon, the red of an afternoon sun) come out
        /// lighter than it was. The smaller of the red and green gains keeps
        /// both at or under their old light. NOT the blue: a road's blue is
        /// mostly the sky ambient's (0.75 of a noon's 1.9), and ruling on it
        /// takes 5% off every noon light to hold back a blue the old shoulder
        /// flattened - the sky's own fill on a sunlit road, which a real
        /// camera sees.
        /// </summary>
        public static float ExposureFor(Preset lit, Preset anchor)
        {
            RoadLight(anchor, out float ar, out float ag);
            RoadLight(lit, out float lr, out float lg);
            return Mathf.Min(Shoulder(ar) / Mathf.Max(lr, 1e-4f), Shoulder(ag) / Mathf.Max(lg, 1e-4f));
        }

        /// <summary>The red and green light on the exposure's anchor road: the
        /// sun's colour x intensity (as PSXGlobals pushes it, unconverted) on a
        /// road leaning <see cref="ExposureRoadTiltDeg"/> toward it, plus the
        /// sky ambient an up-face takes.</summary>
        static void RoadLight(Preset p, out float r, out float g)
        {
            Color sun = p.sunColor * p.sunIntensity;
            float el = p.sunEuler.x;
            float sinEl = el <= 0f ? 0f : Mathf.Sin(Mathf.Min(90f, el + ExposureRoadTiltDeg) * Mathf.Deg2Rad);
            Color sky = SkyAmbientFor(p);
            r = sun.r * sinEl + sky.r;
            g = sun.g * sinEl + sky.g;
        }

        /// <summary>The retired shoulder itself: S(x); x under the toe.</summary>
        static float Shoulder(float x) =>
            x <= ExposureToe ? x : ExposureToe + ExposureSpan * (1f - Mathf.Exp(-(x - ExposureToe) / ExposureSpan));

        /// <summary>
        /// HARSH SUN: the share of the sky's fill a clear noon keeps (the
        /// colour pass, review 2026-09-29). The owner: "gritty realism ...
        /// dark nights and HARSH SUNLIGHT", after a noon he called "washed
        /// out". The hour table's noon ambient (0.52, 0.53, 0.58) reaches the
        /// shaders as written (Shader.SetGlobalColor does not linearise here -
        /// see <see cref="ExposureToe"/>), where the plan's model had taken it
        /// for sRGB (0.25 of light): a car's shadow on the owner's sunlit deck
        /// measured 2.96:1 against the sun beside it (graded, linear) where
        /// the plan asks at least 5, and his photographed noon (reference 8)
        /// gives 7-9. Real clear-sky fill is 10-20% of a high sun's light.
        ///
        /// The cut is on the sky's fill ONLY, and the exposure is re-anchored
        /// on the uncut hour (<see cref="ExposureFor(Preset, Preset)"/>), so
        /// every sunlit road keeps its code - the owner's colours - and what
        /// changes is the shade: under and beside the car, the shaded sides of
        /// walls, buildings and parapets, under the trees. It fades with the
        /// sun's height (a morning keeps more of its fill than a noon) and with
        /// how hard the weather leaves the sun (<see cref="ShadowFor"/>: clear
        /// 1, snow 0.45, fog 0.2, rain 0.15 - an overcast sky IS the light).
        /// Dusk and night are not touched; dawn and sunset have their own cut
        /// (<see cref="LowSunFill"/>, not re-anchored).
        /// Swept 1.0-0.3 on the colour protocol (tools\colour\colour-shots.ps1
        /// -Sets tune, PSX_DAYFILL_SWEEP), the car's whole shadow on the
        /// owner's Samuel Street deck at a clear noon found by pixels against
        /// the frame's no-shadow twin (colour_stats.py shade): fill 1.0 put it
        /// at 122 (2.75:1 against the same deck in the sun), 0.6 105 (3.75),
        /// 0.5 99 (4.27), 0.4 92 (5.01), 0.3 83 (6.11) - the sunlit deck
        /// 193-194 throughout, the owner's colour held. 0.35 sits where the
        /// plan's model put it (the shadow about 85-88, 5.5-5.7:1) with a
        /// margin on both of the plan's numbers (70-95, at least 5).
        /// </summary>
        public const float HarshSunFill = 0.35f;

        /// <summary>For the look tools only: PSX_DAYFILL=x in the environment
        /// is the fill a clear noon keeps for that run (the sweep); unset, the
        /// constant. A build has no such variable.</summary>
        public static float HarshSunFillNow
        {
            get
            {
                string v = System.Environment.GetEnvironmentVariable("PSX_DAYFILL");
                return !string.IsNullOrEmpty(v) && float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f ? Mathf.Min(f, 1f) : HarshSunFill;
            }
        }

        /// <summary>
        /// What of its sky fill an hour keeps (<see cref="HarshSunFill"/>): 1
        /// but at a morning, noon or afternoon, where it falls toward the
        /// constant with the sun's height against noon's and with the weather's
        /// hardness of shadow (<see cref="ShadowFor"/>).
        /// </summary>
        public static float DayFillFor(Preset p, int hour, Weather w)
        {
            // The low sun's own fill (see LowSunFill): a share of it, by how
            // hard the weather leaves the sun - clear the whole cut, rain
            // and fog next to none.
            if (IsLowSun(hour))
                return Mathf.Lerp(1f, LowSunFillNow, ShadowFor(hour, w) / ShadowFor(hour, Weather.Clear));
            if (!IsSunUp(hour)) return 1f;
            float el = p.sunEuler.x;
            if (el <= 0f) return 1f;
            float up = Mathf.Clamp01(Mathf.Sin(el * Mathf.Deg2Rad) / Mathf.Sin(All[Noon].sunEuler.x * Mathf.Deg2Rad));
            return Mathf.Lerp(1f, HarshSunFillNow, up * ShadowFor(hour, w));
        }

        /// <summary><see cref="DayFillFor(Preset, int, Weather)"/> for an hour
        /// and a weather straight off the table (tools and the self-test).</summary>
        public static float DayFillFor(int hour, Weather w) => DayFillFor(At(hour), hour, w);

        /// <summary>Morning, noon or afternoon: the sun is up and high.</summary>
        static bool IsSunUp(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Morning: case Noon: case Afternoon: return true;
                default: return false;
            }
        }

        /// <summary>
        /// THE LOW SUN (2026-10-02). The owner, after the daylight pass:
        /// "lighting for Morning and Noon were greatly improved recently, but
        /// Dawn and Sunset still struggle with that very strong white, washed
        /// out filter lighting" - over two frames of West Trade Street at a
        /// low sun whose every pixel sat between display 0.08 and 0.80
        /// (median 0.36-0.40, the shaded towers 0.34-0.40). The daylight pass
        /// had skipped these two hours on all three of its terms:
        ///   * THE GRADE'S MATTE FLOOR. Morning to afternoon cut 80% of it (G1)
        ///     and the night end cuts 80% at night - but dawn kept 60% and
        ///     sunset 80% (only the night end's share), so SUNSET was the most
        ///     veiled hour the game had. <see cref="GradeSunFor"/> now tops
        ///     both up to the same 80% in clear air and snow.
        ///   * THE SKY'S FILL. At 6-7 degrees the sun hardly lights a road, so
        ///     everything the camera sees with the sun ahead - the road, both
        ///     rows of towers - was the sky's fill alone, at a brightness
        ///     (0.37-0.40) above the morning's cut fill. A low sun's shade is
        ///     the deepest of the day in a photograph, and blue.
        ///     <see cref="LowSunFill"/> cuts it; NOT re-anchored on the road as
        ///     the harsh sun is (<see cref="Lit"/>), because at this height the
        ///     "sunlit road" IS mostly fill - re-anchoring would give the cut
        ///     straight back. Roads only get darker for it, never lighter.
        ///   * THE PAINT'S WORLD. The cars mirrored the hour's pale peach fog
        ///     colour where the morning's mirror the lit world
        ///     (PSXGlobals.paintDay) - the same milky film the owner saw on
        ///     daytime cars.
        ///   * And DAWN'S DEW (<see cref="DampFor"/>): 0.18, a dusk's, mirrored
        ///     the pink-white horizon over the whole road; now sunset's 0.07.
        /// Measured (colour-shots -Sets lowsun, downtown Charlotte and the
        /// circuit): before, both hours floored at Ycode 27 with 5.2-6.1 stops
        /// p1-p99 where the approved morning has 7-8 and 8.25; the fill swept
        /// 1 / 0.65 / 0.5 - 0.65 puts sunset on the morning's own numbers
        /// (downtown median 37 vs 35, the shaded wall 33 vs 34).
        /// Dusk is not touched: its "sun" is an afterglow, the night end
        /// already takes 56% of the floor, and the owner calls it decent.
        /// PSX_LOWSUN=0 in a tool is the picture from before (the A/B).
        /// </summary>
        static bool IsLowSun(int hour) =>
            LowSunEnabled && (hour == Dawn || hour == Sunset);

        public static bool LowSunEnabled => System.Environment.GetEnvironmentVariable("PSX_LOWSUN") != "0";

        /// <summary>The share of its sky fill a clear dawn or sunset keeps
        /// (see <see cref="IsLowSun"/>). Chosen off a sweep on the colour
        /// protocol (PSX_LOWSUNFILL, -Sets lowsun) at downtown Charlotte and
        /// the circuit against the owner's approved morning.</summary>
        public const float LowSunFill = 0.65f;

        /// <summary>For the look tools only: PSX_LOWSUNFILL=x is the fill a
        /// clear dawn and sunset keep for that run (the sweep).</summary>
        public static float LowSunFillNow
        {
            get
            {
                string v = System.Environment.GetEnvironmentVariable("PSX_LOWSUNFILL");
                return !string.IsNullOrEmpty(v) && float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f ? Mathf.Min(f, 1f) : LowSunFill;
            }
        }

        /// <summary>
        /// THE HOUR AS APPLY LIGHTS IT - the one place it is built, for Apply,
        /// the tools and the self-test alike: the table's preset, then the
        /// weather on top, then the city's skyglow, then the harsh sun's fill
        /// cut. <paramref name="anchor"/> is the preset just before the cut -
        /// after the weather and the skyglow, exactly what the exposure was
        /// always taken from - so a sunlit road takes the light it took
        /// before and only the shade gets darker (see HarshSunFill). (The
        /// first cut took its anchor BEFORE the skyglow: the murk's hue shift
        /// then read as a cut, and every lamps-off city night came out up to 8
        /// codes dark - the road gate caught it.)
        /// </summary>
        public static Preset Lit(int index, Weather weather, float urban, out Preset anchor, out float fill)
        {
            index = Mathf.Clamp(index, 0, All.Length - 1);
            var p = All[index];

            // THE WEATHER RIDES ON TOP OF THE HOUR. The preset is a struct,
            // so this is a copy being adjusted and the table stays the table:
            // overcast darkens the sky and the ambient, everything but clear
            // air closes the fog in and runs the lights. See Seasons.
            p.skyExposure *= Seasons.SkyMul(weather);
            p.ambient *= Seasons.AmbientMul(weather);
            // And the SUN: a rainy noon used to keep the full hard sun of a
            // clear one — crisp shadow sides under a sky that had just been
            // darkened to 60% — which is the one tell that a weather layer
            // is a filter over a sunny scene. See SunMul.
            p.sunIntensity *= SunMul(weather);

            // THE CITY LIGHTS ITS OWN SKY. After the weather, so an overcast
            // city night is a murk the cloud holds down rather than a clear
            // sky the weather then greys. See ApplySkyglow.
            ApplySkyglow(ref p, SkyglowFor(index) * urban, index == Night ? NightMurk : 1f);

            // HARSH SUN (the colour pass, review 2026-09-29): see HarshSunFill.
            anchor = p;
            fill = DayFillFor(p, index, weather);
            p.ambient = new Color(p.ambient.r * fill, p.ambient.g * fill, p.ambient.b * fill, p.ambient.a);
            // THE LOW SUN's cut is not re-anchored (see IsLowSun): a 6-degree
            // sun's "sunlit road" is mostly the fill, so holding its light
            // would give the cut straight back. The road only gets darker.
            if (IsLowSun(index)) anchor = p;
            return p;
        }

        /// <summary><see cref="ExposureFor(Preset, Preset)"/> for an hour, a
        /// weather and how much of a city the scene is, built exactly as Apply
        /// builds it (<see cref="Lit"/>).</summary>
        public static float ExposureFor(int hour, Weather w, float urban = 0f)
        {
            var p = Lit(hour, w, urban, out var anchor, out _);
            return ExposureFor(p, anchor);
        }

        /// <summary>The harsh sun, for the self-test (see
        /// <see cref="HarshSunFill"/>): the LUMINANCE (Rec.709) of the light a
        /// sunlit anchor road and a shaded up-face RECEIVE after the exposure,
        /// with the fill cut (now) and without it (old). The exposure is one
        /// number, held on the red or the green (whichever binds, as it always
        /// was), so the sunlit road keeps its luminance to within a couple of
        /// percent and turns a shade warmer - less of its light is the blue sky
        /// fill (noon: red +4%, green 0, blue -6%, luminance +0.4%) - while the
        /// shade falls wherever the fill was cut.</summary>
        public static void HarshSunCheck(int hour, Weather w, out float sunNow, out float sunOld, out float shadeNow, out float shadeOld)
        {
            var p = Lit(hour, w, 0f, out var anchor, out _);
            float eNow = ExposureFor(p, anchor), eOld = ExposureFor(anchor, anchor);
            sunNow = Lum709(RoadLightRGB(p)) * eNow; sunOld = Lum709(RoadLightRGB(anchor)) * eOld;
            shadeNow = Lum709(SkyAmbientFor(p)) * eNow; shadeOld = Lum709(SkyAmbientFor(anchor)) * eOld;
        }

        static Color RoadLightRGB(Preset p)
        {
            Color sun = p.sunColor * p.sunIntensity;
            float el = p.sunEuler.x;
            float sinEl = el <= 0f ? 0f : Mathf.Sin(Mathf.Min(90f, el + ExposureRoadTiltDeg) * Mathf.Deg2Rad);
            return sun * sinEl + SkyAmbientFor(p);
        }

        static float Lum709(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

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
                case Weather.Snow: return IsSunUp(hour) ? SnowDayWetness : Mathf.Max(0.35f, damp);
                default: return damp;
            }
        }

        /// <summary>
        /// A SNOWY DAY'S ROAD (the colour pass, C13, measured 2026-09-29). The
        /// plan's trigger was "the road mirror brighter than the sky above it",
        /// and on the owner's Samuel Street deck at a snowy noon it was: the
        /// deck 14 m ahead read Ycode 205 under a sky of 176 - brighter than
        /// the same deck at a clear noon (193) - a 0.35-wet concrete mirroring
        /// the pale snow sky toward the horizon, one more white in a view that
        /// was all 176-216. With the sun up the road is slush and damp patches,
        /// not a sheet: 0.15. A snowy night keeps its 0.35 (the NFS night's
        /// streaks of sodium down a wet road).
        /// </summary>
        public const float SnowDayWetness = 0.15f;

        /// <summary>The clear-sky dampness of an hour (see WetnessFor).</summary>
        static float DampFor(int hour)
        {
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Night:  return 0.24f;
                case Dusk:   return 0.18f;
                // THE LOW SUN (2026-10-02, see IsLowSun): dawn's 0.18 dew
                // mirrored the pink-white horizon over the whole road - its
                // road measured 66-75 against the morning's 22 with the sun
                // ahead, the biggest white in a dawn frame. Sunset's breath.
                case Dawn:   return LowSunEnabled ? 0.07f : 0.18f;
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
        /// THE OWNER'S C11 CHOICE, "G1" (LookChoices.SunLift; ships off):
        /// how far PSX/Blit takes its sun-keyed lift fade - 1 at a CLEAR or a
        /// SNOWY morning, noon or afternoon, 0 at every other hour and in rain
        /// and fog (a grey noon has no hard light to cut the veil). Snow joined
        /// on review (2026-09-29): the owner singled out "noon, especially with
        /// snow", and a snowfield with the sun up is the brightest light the
        /// game has - where a matte veil over the darks shows most.
        /// </summary>
        public static float GradeSunFor(int hour, Weather w)
        {
            if (w != Weather.Clear && w != Weather.Snow) return 0f;
            switch (Mathf.Clamp(hour, 0, All.Length - 1))
            {
                case Morning: case Noon: case Afternoon: return 1f;
                // THE LOW SUN (see IsLowSun): what tops the night end's share
                // up to the day's whole cut. PSX/Blit takes 0.80 x night plus
                // 0.80 x this, so 1 - GradeNightFor is 80% at dawn and sunset
                // exactly as at noon and at night.
                case Dawn: case Sunset: return IsLowSun(hour) ? 1f - GradeNightFor(hour) : 0f;
                default: return 0f;
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
                    // The owner's C12 choice (LookChoices.CoolNight, off): a
                    // city's darks blue-teal instead of sodium brown.
                    var urbanMood = LookChoices.CoolNight ? CoolUrbanNightMood : UrbanNightMood;
                    var c = Color.Lerp(RuralNightMood, urbanMood, Mathf.Clamp01(urban));
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
        /// <summary>C12's city night (LookChoices.CoolNight, off): the NFS
        /// frames' blue-teal darks - a little greener than the mountain's
        /// blue, the colour of a city's mixed light in wet haze.</summary>
        static readonly Color CoolUrbanNightMood = new Color(0.70f, 0.86f, 1.00f, 1f);

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
        /// <summary>C12's murk (LookChoices.CoolNight, off): the city's haze
        /// lit by mixed light - a cool slate at about the sodium murk's own
        /// brightness, so the night gets no lighter or darker, only cooler.</summary>
        static readonly Color CoolMurk = new Color(0.125f, 0.137f, 0.15f, 1f);
        static Color Murk => LookChoices.CoolNight ? CoolMurk : SodiumMurk;

        /// <summary>How bright the city's murk is at NIGHT against the colour
        /// above (dusk and dawn keep it whole): the dark-night retune
        /// (2026-09-29) took the night's ambient and horizon to about a
        /// sixth of their light, and a city glowing as brightly as before
        /// over that would be the old grey night back in every town. NFS
        /// Heat's skyline sky measures (19,35,48) at its brightest band; the
        /// murk at 0.45 puts the town's horizon band under it.</summary>
        const float NightMurk = 0.45f;

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
        static void ApplySkyglow(ref Preset p, float g, float murkScale = 1f)
        {
            if (g <= 0f) return;
            // The sodium murk as signed off, or the owner's C12 choice (off),
            // at the hour's own strength (NightMurk).
            Color murk = Murk * murkScale;
            murk.a = 1f;
            p.fogColor = Opaque(Color.Lerp(p.fogColor, murk, 0.65f * g));
            p.skyHorizon = Opaque(Color.Lerp(p.skyHorizon, murk * 1.35f, 0.70f * g));
            p.skyTop = Opaque(Color.Lerp(p.skyTop, murk * 0.40f, 0.30f * g));
            float lumA = Lum(p.ambient);
            Color murkAtAmbient = murk * (lumA / Lum(murk));
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

                // THE HORIZON RING (Shaders/PSXFogRing.cginc): this panorama's
                // eight bearings, so the fog is the photograph's own horizon
                // colour in every direction. Off (exactly the old fog) when the
                // bake has no line for this sky.
                var ring = tex != null ? HorizonRing(p.skyTex) : null;
                if (ring != null) Shader.SetGlobalVectorArray("_PSXFogRing", ring);
                Shader.SetGlobalFloat("_PSXFogRingOn", ring != null ? FogRingAmount : 0f);

                // THE DYNAMIC SKY (SkyModePrefs): the same material in its
                // computed mode, and everything that read the photograph -
                // reflections, the horizon ring - pointed at the computed sky.
                bool dyn = SkyModePrefs.Dynamic;
                if (skyInstance.HasProperty("_Dynamic")) skyInstance.SetFloat("_Dynamic", dyn ? 1f : 0f);
                if (dyn)
                {
                    skyInstance.SetFloat("_Rotation", 0f);
                    DynamicSky.Apply(lastSkyIndex, lastSkyWeather, sun, p);
                }
                else DynamicSky.Stop();
            }
            RenderSettings.skybox = skyInstance;
        }

        static Preset lastSkyPreset;
        static Light lastSkySun;
        static int lastSkyIndex;
        static Weather lastSkyWeather;
        static bool skyApplied;

        /// <summary>Re-apply the last hour's sky - the pause menu's SKY switch,
        /// thrown mid-drive.</summary>
        public static void RefreshSky()
        {
            if (skyApplied) ApplySky(lastSkyPreset, lastSkySun);
        }

        /// <summary>How much of the horizon ring the fog takes (0..1).</summary>
        public const float FogRingAmount = 0.9f;

        static System.Collections.Generic.Dictionary<string, Vector4[]> horizonRings;

        /// <summary>The baked ring for a panorama (tools/sky/bake_horizon_rings.py
        /// writes Resources/Sky/horizon_rings.txt: a name and eight rgb
        /// ratios), or null. Parsed once.</summary>
        static Vector4[] HorizonRing(string sky)
        {
            if (horizonRings == null)
            {
                horizonRings = new System.Collections.Generic.Dictionary<string, Vector4[]>();
                var txt = Resources.Load<TextAsset>("Sky/horizon_rings");
                if (txt != null)
                    foreach (var line in txt.text.Split((char)10))
                    {
                        var f = line.Trim().Split(' ');
                        if (f.Length != 25) continue;
                        var ring = new Vector4[8];
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        for (int k = 0; k < 8; k++)
                            ring[k] = new Vector4(float.Parse(f[1 + k * 3], ci), float.Parse(f[2 + k * 3], ci),
                                                  float.Parse(f[3 + k * 3], ci), 1f);
                        horizonRings[f[0]] = ring;
                    }
            }
            return sky != null && horizonRings.TryGetValue(sky, out var r) ? r : null;
        }
    }
}
