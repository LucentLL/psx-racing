using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// CARDOTS (2026-11-23; the owner: "Dots in shade...I see them when
    /// selecting a car to start the game. In menus. When parked in house
    /// driveway"). The player's SSAO (PlatformProfile.ApplyAO, laid over the
    /// opaques) left single dark pixels on the cars' paint; CarDots keeps the
    /// paint out of it. The check: a car on the showroom stage with its far
    /// flank in shade, the player's SSAO on (in memory, CarDotsShots.PlayerAO),
    /// for three paints. The SPECK is that frame minus the same frame without
    /// the SSAO; its local contrast (minus its 9x9 mean, inside the paint) and its
    /// DOTS (pixels more than 0.035 darker than that mean, per 10k paint pixels)
    /// must stay under <see cref="CarDotsLimit"/>; the frame without the fix and
    /// the paint's own local contrast are printed beside it.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Dark specks per 10k paint pixels the SSAO may leave with the fix on.</summary>
        const float CarDotsLimit = 0.5f;

        /// <summary>Only this (writes PSXRacing_selftest_cardots.txt).</summary>
        public static void RunCarDots()
        {
            log = new System.Text.StringBuilder();
            failures = 0;
            Guard(nameof(TestCarDotsShade), TestCarDotsShade);
            Line(failures == 0 ? "CARDOTS SELF-TEST OK" : "CARDOTS SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_cardots.txt"), log.ToString());
        }

        static void TestCarDotsShade()
        {
            Line("CARDOTS: the paint in shade on the showroom stage, the player's SSAO on (dots = px > 0.035 under their 9x9 mean, per 10k paint px)");
            Check(CarDots.Bit == 128 && DamageCabin.StencilBit == 64, "the paint's stencil bit is its own (128; DamageCabin's hole is 64)");
            foreach (var (key, skin) in CarDotsShots.ParseCars("civic_eg:red,fastback_96:white,gt_liftback_88:black"))
            {
                var def = CarModelLibrary.Load(key);
                if (def == null || skin >= def.SkinCount) { Check(false, "car " + key + ":" + skin + " loads"); continue; }
                var off = CarDotsShots.StudioContrast(def, skin, 40f, false, out int n1, out var plainOff);
                var on = CarDotsShots.StudioContrast(def, skin, 40f, true, out int n2, out var plainOn);
                string name = def.skinNames != null && skin < def.skinNames.Length ? def.skinNames[skin] : skin.ToString();
                Line($"    {key} {name}: paint px {n2}; SSAO speck rms {off.x * 255f:0.00} -> {on.x * 255f:0.00} (of 255), " +
                     $"dots {off.y:0.0} -> {on.y:0.0} per 10k; the paint's own local contrast rms {plainOff.x * 255f:0.00} -> {plainOn.x * 255f:0.00}, " +
                     $"dots {plainOff.y:0.0} -> {plainOn.y:0.0} per 10k (before -> after)");
                Check(n2 > 20000, $"{key}: the paint is on screen ({n2} px)", n2);
                Check(on.y <= CarDotsLimit, $"{key} {name}: dark specks the SSAO leaves on the paint <= {CarDotsLimit} per 10k", on.y.ToString("0.0"));
                Check(on.x * 255f <= 0.5f, $"{key} {name}: the SSAO's speck on the paint, rms <= 0.5 of 255", (on.x * 255f).ToString("0.00"));
            }
        }
    }
}
