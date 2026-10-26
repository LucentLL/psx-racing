using System.Text;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CLIMATE AND THE WEATHER LAYERS (2026-10-25). Pins the model the
    /// owner asked for, not the old per-month roll: each region's year against
    /// its station's normals, snow as rare as Charlotte's and as common as
    /// Boone's, the layers truly independent (snow on the ground over a dry
    /// road under a clear sky; a soaked road under a clear sky after the rain;
    /// a bridge iced on a night the road is only wet), and the grip each
    /// surface hands a tyre. Prints the CLIMATE PRINTOUT (the gate) into the
    /// log.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only the climate (seconds, no scenes): writes
        /// PSXRacing_selftest_weather.txt.</summary>
        public static void RunWeather()
        {
            log = new StringBuilder();
            failures = 0;
            Guard(nameof(TestClimate), TestClimate);
            Line(failures == 0 ? "WEATHER SELF-TEST OK" : "WEATHER SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_weather.txt"), log.ToString());
        }

        struct RegionCase { public string name; public ClimateRegion r; public float elev; }

        static void TestClimate()
        {
            Line("climate + weather layers:");
            Check(Climate.Epoch == LifeRules.Epoch, "the climate's day 1 is the LifeSim's day 1");
            Check(Seasons.DressFor(Season.Summer, SceneryKind.Snow) == Seasons.DressSnow &&
                  Seasons.DressFor(Season.Winter, SceneryKind.Dry) == (int)Season.Winter,
                  "snow LYING on the scenery picks the snow dress, whatever the sky");

            // Determinism: the same day, hour and venue always answer the same,
            // asked in any order.
            bool same = true;
            int d0 = LifeRules.DayNumber(new System.DateTime(1999, 1, 1));
            for (int k = 0; k < 200; k++)
            {
                int d = d0 + (k * 37) % 1500;
                var a = Climate.At(ClimateRegion.Mountains, 1300f, d, 6.5f);
                var b = Climate.At(ClimateRegion.Mountains, 1300f, d, 6.5f);
                if (a.airC != b.airC || a.road.water != b.road.water || a.snowDepthCm != b.snowDepthCm || a.sky != b.sky) same = false;
            }
            Check(same, "the same day, hour and venue always give the same weather");

            // THE PRINTOUT: ten simulated years per region against the normals.
            const int years = 10;
            var cases = new[]
            {
                new RegionCase { name = "CHARLOTTE (CLT)", r = ClimateRegion.Piedmont, elev = Climate.CltElevM },
                new RegionCase { name = "MOUNTAINS @ Boone 1016 m", r = ClimateRegion.Mountains, elev = Climate.BooneElevM },
                new RegionCase { name = "MOUNT MITCHELL 1800 m", r = ClimateRegion.Mountains, elev = 1800f },
                new RegionCase { name = "COAST (Morehead City)", r = ClimateRegion.Coast, elev = Climate.CoastElevM },
            };
            string[] mon = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            int dA = LifeRules.DayNumber(new System.DateTime(1999, 1, 1));
            int dB = LifeRules.DayNumber(new System.DateTime(1999 + years, 1, 1));
            float[] snowDaysPerYear = new float[cases.Length];
            int snowSceneryDryRoad = 0, wetRoadClearSky = 0, bridgeIceRoadNot = 0, treatedSnowIcyRoad = 0, summerSnow = 0;
            for (int ci = 0; ci < cases.Length; ci++)
            {
                var c = cases[ci];
                var hi = new double[12]; var lo = new double[12]; var nh = new double[12]; var nl = new double[12];
                var n = new int[12]; var wet = new int[12];
                int snowDays = 0, events = 0, cover = 0; bool prevSnow = false; float fall = 0f;
                int scenH = 0, roadCovH = 0, roadIceH = 0, bridgeIceH = 0, fogDays = 0;
                for (int d = dA; d < dB; d++)
                {
                    bool fogToday = false;
                    for (int hh = 0; hh < 22; hh++) if (Climate.At(c.r, c.elev, d, hh + 0.5f).fog) { fogToday = true; break; }
                    if (fogToday) fogDays++;
                    int m = Climate.DateOf(d).Month - 1;
                    var rec = Climate.DayAt(c.r, c.elev, d);
                    Climate.Normals(c.r, c.elev, d, out float h, out float l);
                    hi[m] += rec.hiC; lo[m] += rec.loC; nh[m] += h; nl[m] += l; n[m]++;
                    if (rec.precipDay) wet[m]++;
                    float cm = Climate.SnowfallCm(c.r, c.elev, d);
                    fall += cm;
                    bool sd = cm >= 0.25f;
                    if (sd) { snowDays++; if (!prevSnow) events++; if (m >= 5 && m <= 7) summerSnow++; }
                    prevSnow = sd;
                    for (int hh = 0; hh < 24; hh += 3)
                    {
                        var w = Climate.At(c.r, c.elev, d, hh + 0.5f);
                        if (hh == 6 && w.snowDepthCm >= 1f) cover++;
                        bool roadDry = w.road.water < 0.1f && w.road.snow < 0.1f && w.road.slush < 0.1f && !w.road.ice;
                        if (w.scenery == SceneryKind.Snow && roadDry && w.sky <= SkyKind.PartlyCloudy) snowSceneryDryRoad++;
                        if (w.road.water > 0.5f && w.precip == PrecipKind.None && w.sky <= SkyKind.PartlyCloudy) wetRoadClearSky++;
                        if (w.bridge.ice && !w.road.ice) bridgeIceRoadNot++;
                        if (w.treated && w.road.snow > 0.3f) treatedSnowIcyRoad++;
                        if (w.scenery == SceneryKind.Snow) scenH++;
                        if (w.road.snow >= 0.05f || w.road.slush >= 0.05f) roadCovH++;
                        if (w.road.ice) roadIceH++;
                        if (w.bridge.ice) bridgeIceH++;
                    }
                }
                // THE ROADS CLEAR LONG BEFORE THE SCENERY (owner, 2026-10-26):
                // snow or slush on the open road a small share of the hours
                // snow lies on the verges; black ice rare, bridges first.
                // FOG STAYS RARE: fog days a year against heavy-fog normals
                // (CLT ~10-20, mountains more, the coast some).
                Line("     snow on the scenery " + (scenH * 3f / years).ToString("0") + " h/yr, snow/slush on the open road " +
                     (roadCovH * 3f / years).ToString("0") + " h/yr, black ice road/bridges " + (roadIceH * 3f / years).ToString("0") +
                     "/" + (bridgeIceH * 3f / years).ToString("0") + " h/yr, fog days " + (fogDays / (float)years).ToString("0.0") + "/yr");
                if (scenH > 20)
                    Check(roadCovH <= 0.1f * scenH, c.name + ": snow/slush on the road under a tenth of the scenery's snow hours",
                          roadCovH + " vs " + scenH);
                Check(bridgeIceH >= roadIceH, c.name + ": bridges ice first", bridgeIceH + " vs " + roadIceH);
                float fogMax = c.r == ClimateRegion.Mountains ? 50f : c.r == ClimateRegion.Coast ? 25f : 20f;
                Check(fogDays / (float)years <= fogMax && fogDays / (float)years >= 3f, c.name + ": fog stays rare", fogDays / (float)years);
                snowDaysPerYear[ci] = snowDays / (float)years;
                Line("  == " + c.name + "  (" + years + " simulated years; sim hi/lo F vs normal hi/lo F, precip days)");
                var sb = new StringBuilder();
                for (int m = 0; m < 12; m++)
                {
                    sb.Append("     ").Append(mon[m]).Append(' ')
                      .Append(F(hi[m] / n[m])).Append('/').Append(F(lo[m] / n[m])).Append(" vs ")
                      .Append(F(nh[m] / n[m])).Append('/').Append(F(nl[m] / n[m])).Append("  ")
                      .Append((wet[m] / (float)years).ToString("0.0"));
                    if (m % 3 == 2) { Line(sb.ToString()); sb.Length = 0; }
                }
                Line("     snow days (>=0.1 in) " + (snowDays / (float)years).ToString("0.0") + "/yr, events " +
                     (events / (float)years).ToString("0.0") + "/yr, snowfall " + (fall / years / 2.54f).ToString("0.0") +
                     " in/yr, snow-covered dawns " + (cover / (float)years).ToString("0.0") + "/yr");
                // Normals: monthly means within 3 C, January and July.
                foreach (int m in new[] { 0, 6 })
                    Check(System.Math.Abs(hi[m] / n[m] - nh[m] / n[m]) < 3.0 && System.Math.Abs(lo[m] / n[m] - nl[m] / n[m]) < 3.0,
                          c.name + ": " + mon[m] + " mean high/low within 3 C of the normals",
                          F(hi[m] / n[m]) + "/" + F(lo[m] / n[m]) + " vs " + F(nh[m] / n[m]) + "/" + F(nl[m] / n[m]));
            }
            Line("     (normals: CLT 1.9 snow days 3.5 in; Boone 1 SE 14.2 days 25.6 in; Morehead City 0.8 in, no snow-day month)");
            Check(snowDaysPerYear[0] >= 0.8f && snowDaysPerYear[0] <= 4f, "Charlotte: snow a couple of days a year", snowDaysPerYear[0].ToString("0.0"));
            Check(snowDaysPerYear[1] >= 8f && snowDaysPerYear[1] <= 25f, "the mountains: far more", snowDaysPerYear[1].ToString("0.0"));
            Check(snowDaysPerYear[2] > snowDaysPerYear[1], "and more again up on Mount Mitchell", snowDaysPerYear[2].ToString("0.0"));
            Check(snowDaysPerYear[3] <= 0.6f, "the beach: next to never", snowDaysPerYear[3].ToString("0.0"));
            Check(summerSnow == 0, "and nobody's June, July or August snows", summerSnow);

            // THE LAYERS ARE INDEPENDENT (the owner's examples).
            Check(snowSceneryDryRoad > 0, "snow on the scenery over a DRY road under a clear sky happens", snowSceneryDryRoad);
            Check(wetRoadClearSky > 0, "a WET road under a clear sky (after the rain) happens", wetRoadClearSky);
            Check(bridgeIceRoadNot > 0, "a bridge iced while the road is not happens", bridgeIceRoadNot);
            Check(treatedSnowIcyRoad == 0, "a TREATED road never carries lying snow (slush and water)", treatedSnowIcyRoad);

            // THE PATCHY DRY/WET LOOK AFTER RAIN (owner, 2026-10-26: "I like
            // how the roads have dry and wet patches"): every drying spell
            // after rain, ten Charlotte and Blue Ridge years, an hour at a
            // time, spends hours with the road part wet and part dry (water
            // share 0.1-0.9 - the patch mask's share is the amount), clearing
            // skies included.
            foreach (var pc in new[] { new RegionCase { name = "CHARLOTTE", r = ClimateRegion.Piedmont, elev = Climate.CltElevM },
                                       new RegionCase { name = "BLUE RIDGE", r = ClimateRegion.Mountains, elev = 1300f } })
            {
                int spells = 0, shortSpells = 0, sunnySpells = 0, minPatchy = 99, minSunny = 99, patchyHours = 0;
                bool inSpell = false, lastRain = false; int sp = 0, spSun = 0, len = 0;
                for (int d = dA; d < dB; d++)
                    for (int hh = 0; hh < 24; hh++)
                    {
                        var w = Climate.At(pc.r, pc.elev, d, hh + 0.5f);
                        if (w.precip != PrecipKind.None) { inSpell = false; lastRain = w.precip == PrecipKind.Rain; continue; }
                        if (!inSpell && lastRain && w.road.water >= 0.9f && !w.road.ice) { inSpell = true; sp = spSun = len = 0; }
                        lastRain = false;
                        if (!inSpell) continue;
                        len++;
                        if (w.road.water >= 0.1f && w.road.water <= 0.9f)
                        {
                            sp++; patchyHours++;
                            if (w.sky <= SkyKind.PartlyCloudy && hh >= 8 && hh <= 17) spSun++;
                        }
                        if (w.road.water < 0.02f)
                        {
                            inSpell = false; spells++;
                            if (sp < 2) shortSpells++;
                            minPatchy = Mathf.Min(minPatchy, sp);
                            if (sp > 0 && spSun * 2 >= sp) { sunnySpells++; minSunny = Mathf.Min(minSunny, sp); }
                        }
                        else if (w.road.ice || len > 96) inSpell = false;
                    }
                Line("     " + pc.name + ": " + (spells / (float)years).ToString("0.0") + " drying spells/yr after rain, patchy phase >= " +
                     minPatchy + " h (sunny spells " + (sunnySpells / (float)years).ToString("0.0") + "/yr, >= " + minSunny + " h), " +
                     (patchyHours / (float)years).ToString("0") + " patchy h/yr");
                Check(spells > 0 && shortSpells == 0 && sunnySpells > 0 && minSunny >= 2,
                      pc.name + ": every drying spell after rain is patchy for hours, clearing skies included",
                      shortSpells + " short of " + spells + ", sunny min " + minSunny + " h");
            }

            // RAIN AND SNOW FALL FROM A CLOSED SKY (owner, 2026-10-26: "Rain
            // or snow, only a few clouds in the sky"): every race hour of ten
            // Charlotte and Boone years, as the game resolves it, plus a
            // tool's forced clear sky with snow in it.
            int precipOpen = 0, precipHours = 0;
            for (int d = dA; d < dB; d++)
                for (int h = 0; h < TimeOfDay.All.Length; h++)
                    foreach (string venue in new[] { "Charlotte", "BlueRidge" })
                    {
                        var w = Seasons.At(d, h, venue);
                        if (w.precip == PrecipKind.None) continue;
                        precipHours++;
                        if (w.sky < SkyKind.Overcast) precipOpen++;
                    }
            var forced = Seasons.Forced;
            Seasons.Forced = new WeatherNow { day = dA, sky = SkyKind.Clear, precip = PrecipKind.Snow, precipRate = 1f };
            bool forcedClosed = Seasons.Now.sky >= SkyKind.Overcast;
            Seasons.Forced = forced;
            Check(precipHours > 0 && precipOpen == 0 && forcedClosed,
                  "rain and snow only ever fall from an OVERCAST or STORM sky (the date line, both sky modes)",
                  precipOpen + " open of " + precipHours + " falling race hours, forced clear+snow closed " + forcedClosed);
            Check(Seasons.SkyOverPrecip(new WeatherNow { sky = SkyKind.PartlyCloudy, precip = PrecipKind.None, road = new DeckState { water = 1f } }).sky
                  == SkyKind.PartlyCloudy, "after the rain the sky may clear over a wet road");

            // GRIP per surface (dry stays exactly 1: handling bit-identical).
            var dry = RoadSurface.OfDeck(new DeckState(), 0.7f);
            var soaked = RoadSurface.OfDeck(new DeckState { water = 1f }, 0.3f);
            var slush = RoadSurface.OfDeck(new DeckState { slush = 1f }, 0.5f);
            var snowy = RoadSurface.OfDeck(new DeckState { snow = 1f }, 0.5f);
            var ice = RoadSurface.OfDeck(new DeckState { water = 1f, ice = true }, 0.5f);
            var frost = RoadSurface.OfDeck(new DeckState { frost = true }, 0.5f);
            Check(dry.grip == 1f && dry.wetT == 0f, "a dry road is grip 1 exactly", dry.grip);
            Check(Mathf.Abs(soaked.grip - 0.70f) < 1e-4f && soaked.wetT == 1f, "a soaked road 0.70", soaked.grip);
            Check(slush.grip < soaked.grip && snowy.grip < slush.grip && ice.grip < snowy.grip && frost.grip < soaked.grip,
                  "slush < wet, snow < slush, ice < snow, frost < wet",
                  slush.grip.ToString("0.00") + " " + snowy.grip.ToString("0.00") + " " + ice.grip.ToString("0.00") + " " + frost.grip.ToString("0.00"));
            Line("     grip: dry 1.00, wet " + soaked.grip.ToString("0.00") + ", slush " + slush.grip.ToString("0.00") +
                 ", snow " + snowy.grip.ToString("0.00") + ", frost " + frost.grip.ToString("0.00") + ", ice " + ice.grip.ToString("0.00"));
            // A drying road is PATCHY: half its water leaves some of a 400 m
            // square wet and some of it dry, and the same spot answers the same.
            var half = new DeckState { water = 0.5f };
            int wetSpots = 0, drySpots = 0;
            for (int x = 0; x < 20; x++)
                for (int z = 0; z < 20; z++)
                {
                    var sp = RoadSurface.OfDeck(half, RoadSurface.Patch(x * 20f, z * 20f));
                    if (sp.grip < 0.8f) wetSpots++; else if (sp.grip >= 0.999f) drySpots++;
                }
            Check(wetSpots > 40 && drySpots > 40, "a drying road has wet stretches and dry ones", wetSpots + " wet / " + drySpots + " dry of 400");
            Check(RoadSurface.Patch(123.4f, -567.8f) == RoadSurface.Patch(123.4f, -567.8f), "and the patches are fixed in the world");

            // The bench's forced skies keep their old grips.
            Check(Mathf.Abs(Seasons.GripMult(Weather.Rain, true) - 0.70f) < 1e-4f && Seasons.GripMult(Weather.Clear, true) == 1f &&
                  Seasons.GripMult(Weather.Snow, true) < Seasons.GripMult(Weather.Rain, true),
                  "the bench: RAIN 0.70, CLEAR 1, SNOW below rain",
                  Seasons.GripMult(Weather.Snow, true).ToString("0.00"));
            // The sky's light: overcast without rain is the grey light; snow
            // falling is snow's; a broken sky is clear.
            Check(Seasons.SkyLightOf(new WeatherNow { sky = SkyKind.Overcast }) == Weather.Rain &&
                  Seasons.SkyLightOf(new WeatherNow { sky = SkyKind.Overcast, precip = PrecipKind.Snow }) == Weather.Snow &&
                  Seasons.SkyLightOf(new WeatherNow { sky = SkyKind.PartlyCloudy }) == Weather.Clear,
                  "an overcast sky lights grey, falling snow lights as snow, a broken sky as clear");

            // Two sample forecasts, as the player reads them.
            int jan20 = LifeRules.DayNumber(new System.DateTime(1999, 1, 20));
            for (int k = 0; k < 4; k++)
            {
                var w = Seasons.At(jan20 + k, TimeOfDay.Night, "BlueRidge");
                Line("     BLUE RIDGE " + LifeRules.DateLabel(jan20 + k) + " NIGHT: " + Seasons.SkyLine(w) + "  |  " + Seasons.TempLine(w, true));
            }
        }

        static string F(double c) => ((float)(c * 9.0 / 5.0 + 32.0)).ToString("0");
    }
}
