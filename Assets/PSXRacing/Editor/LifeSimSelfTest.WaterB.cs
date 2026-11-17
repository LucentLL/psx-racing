using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// WATER PART B (2026-11-17): the HD water's switch, its weather memo and
    /// its runtime looks. Nothing here renders.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestWaterB()
        {
            // the weather on the water follows a FORCED weather too (item 16, m3:
            // the memo used to key on the calendar and the bench's override only)
            var keep = Seasons.Forced;
            try
            {
                var w = Seasons.Now;
                w.sky = SkyKind.Clear; w.precip = PrecipKind.None; w.precipRate = 0f;
                Seasons.Forced = w;
                WaterLook.Weather(out _, out float dryRain, out float dryClear);
                w.sky = SkyKind.Storm; w.precip = PrecipKind.Rain; w.precipRate = 0.9f;
                Seasons.Forced = w;
                WaterLook.Weather(out _, out float wetRain, out float wetClear);
                Check(dryRain == 0f && dryClear > 0.99f && wetRain >= 0.89f && wetClear == 0f,
                      "the water's weather follows a forced weather (dry: no rain, a clear sky's glint; a storm: rain 0.9, no glint)",
                      $"{dryRain:0.00}/{dryClear:0.00} -> {wetRain:0.00}/{wetClear:0.00}");
            }
            finally { Seasons.Forced = keep; WaterLook.Invalidate(); }

            // the looks the game applies by a material's name (no scene pass for a retune)
            Check(WaterPresets.ForName("LakeLure_LakeWater", out var lake) && lake.look == WaterPresets.Lake.look
                  && WaterPresets.ForName("ChimneyRock_RiverWater", out var river) && river.look == WaterPresets.River.look
                  && WaterPresets.ForName("CityWater", out var city) && city.look.z == 0f
                  && !WaterPresets.ForName("EmeraldIsle_Sea", out _),
                  "the HD water's looks by name: Lake Lure's lake, Chimney Rock's river, the city (no shore foam); the sea keeps its own");
            Check(PSXRacingBuilder.WaterPreset.Lake.look == WaterPresets.Lake.look && PSXRacingBuilder.WaterPreset.City.absorb == WaterPresets.City.absorb,
                  "the builders write the same looks the game applies");

            // THE SWITCH: on a PC the row's default is HD; PSX_WATER_HD overrides it; phones never
            Check(!WaterPrefs.Offered || PlayerPrefs.GetInt("psx.waterhd", 1) != 0 || !WaterPrefs.HD,
                  "the WATER row reads its saved value (default HD on a PC)");
            string env = System.Environment.GetEnvironmentVariable("PSX_WATER_HD");
            Check(env == "1" ? WaterLook.On : env == "0" ? !WaterLook.On : WaterLook.On == WaterPrefs.HD,
                  "PSX_WATER_HD, when set, decides the water; otherwise the row", "env " + (env ?? "unset") + ", On " + WaterLook.On);
            var sh = Shader.Find(WaterLook.ShaderName);
            Check(sh != null && !UnityEditor.ShaderUtil.ShaderHasError(sh), "PSX/Water compiles (both variants are in it)");
        }
    }
}
