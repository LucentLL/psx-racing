using System.Text;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// RAIN 2 (2026-11-16): the rules that have no picture - where water
    /// stands (the tyres' port of the shader's pools), how the standing water
    /// follows the weather, the wipers' timing, and that with the switch off
    /// the shaders are handed nothing. Seconds, no scenes.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>Only this (writes PSXRacing_selftest_rain2.txt).</summary>
        public static void RunRain2()
        {
            log = new StringBuilder();
            failures = 0;
            Guard(nameof(TestRain2), TestRain2);
            Line(failures == 0 ? "RAIN2 SELF-TEST OK" : "RAIN2 SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_rain2.txt"), log.ToString());
        }

        /// <summary>The share of a 120 m square of ground with normal n that
        /// holds a pool, per cent (half-metre cells, the current level).</summary>
        static float Rain2PoolShare(Vector3 n, float ox, float oz)
        {
            int hit = 0, all = 0;
            for (float x = 0f; x < 120f; x += 0.5f)
                for (float z = 0f; z < 120f; z += 0.5f)
                {
                    all++;
                    if (Rain2.PoolAt(new Vector3(ox + x, 0f, oz + z), n) > 0.5f) hit++;
                }
            return 100f * hit / all;
        }

        static void TestRain2()
        {
            Line("rain 2:");
            bool keepOn = Rain2.On, keepPools = Rain2.Pools, keepRings = Rain2.Rings;
            try
            {
                // ---- the standing water follows the weather
                Check(Mathf.Approximately(Rain2.LevelFor(false, 0f, 0f), 0f), "a dry road holds no standing water");
                Check(Rain2.LevelFor(true, 1f, 1f) > 0.95f, "a downpour on a soaked road fills the pools", Rain2.LevelFor(true, 1f, 1f));
                Check(Rain2.LevelFor(true, 1f, 0.1f) < 0.5f, "the first minutes of rain have not filled them yet", Rain2.LevelFor(true, 1f, 0.1f));
                Check(Rain2.LevelFor(true, 0.2f, 1f) < Rain2.LevelFor(true, 1f, 1f), "light rain stands lower than a downpour");
                float after = Rain2.LevelFor(false, 0f, 1f), later = Rain2.LevelFor(false, 0f, 0.4f);
                Check(after > 0.5f && later > 0f && later < after, "after the rain the pools stay and drain with the road's own water",
                      after.ToString("0.00") + " then " + later.ToString("0.00"));

                // ---- where it stands (the CPU's port of PSXRain2.cginc)
                Rain2.On = true; Rain2.Pools = true; Rain2.Rings = true;
                Rain2.Push(1f, 1f);
                Vector3 flat = Vector3.up;
                Vector3 crown = new Vector3(0.02f, 1f, 0f).normalized;       // a 2 % crossfall
                Vector3 bank = new Vector3(0.06f, 1f, 0.05f).normalized;     // a 6 % bank on a 5 % grade
                float fFlat = Rain2PoolShare(flat, 1200f, -3400f), fCrown = Rain2PoolShare(crown, 1200f, -3400f),
                      fBank = Rain2PoolShare(bank, 1200f, -3400f);
                Check(fFlat > 22f && fFlat < 55f, "a flat lot in a downpour is a quarter to a half standing water", fFlat.ToString("0.0") + " %");
                Check(fCrown > 8f && fCrown < fFlat, "a crowned road holds less than a flat lot, but holds some", fCrown.ToString("0.0") + " %");
                Check(fBank < 1.5f, "a banked, climbing road sheds it all", fBank.ToString("0.0") + " %");
                Check(Rain2.PoolAt(new Vector3(10f, 0f, 10f), Vector3.right) == 0f, "a wall holds no pool");
                float a1 = Rain2.PoolAt(new Vector3(431.5f, 0f, -220.25f), flat), a2 = Rain2.PoolAt(new Vector3(431.5f, 0f, -220.25f), flat);
                Check(a1 == a2, "a pool is where it is: the same answer twice");
                Rain2.Push(0.6f, 0.3f);
                float fLow = Rain2PoolShare(flat, 1200f, -3400f);
                Check(fLow < fFlat * 0.5f && fLow < 12f, "with little water standing only the deepest dips hold it", fLow.ToString("0.0") + " %");
                Rain2.Push(0f, 0f);
                Check(Rain2PoolShare(flat, 1200f, -3400f) == 0f, "no standing water, no pools");

                // ---- what the shaders are handed
                Rain2.Push(1f, 1f);
                var v = Shader.GetGlobalVector("_PSXRain2");
                Check(v.x > 0.5f && v.y > 0.9f && v.z > 0.9f, "on: the shaders get the rain and the level", v);
                Rain2.Rings = false;
                Rain2.Push(1f, 1f);
                Check(Shader.GetGlobalVector("_PSXRain2").w == 0f, "Rain2.Rings off: no ring layers");
                Rain2.Rings = true; Rain2.Pools = false;
                Rain2.Push(1f, 1f);
                v = Shader.GetGlobalVector("_PSXRain2");
                Check(v.z == 0f && Rain2.PoolLevel == 0f, "Rain2.Pools off: no standing water (the old soft puddles)", v);
                Rain2.Pools = true; Rain2.On = false;
                Rain2.Push(1f, 1f);
                Check(Shader.GetGlobalVector("_PSXRain2") == Vector4.zero && Rain2.RainNow == 0f && Rain2.PoolLevel == 0f,
                      "PSX_RAIN2=0: the shaders are handed nothing", Shader.GetGlobalVector("_PSXRain2"));

                // ---- the wipers
                CockpitInterior.WiperTiming(0.6f, out float sL, out float pL);
                CockpitInterior.WiperTiming(0.75f, out float sM, out float pM);
                CockpitInterior.WiperTiming(1f, out float sH, out float pH);
                Check(pL > pM && pM > pH && sL <= pL && sM <= pM && sH <= pH,
                      "the wipers: intermittent in light rain, continuous in steady rain, fast in a downpour",
                      $"{sL:0.0}/{pL:0.0} {sM:0.0}/{pM:0.0} {sH:0.0}/{pH:0.0} s");
                Check(CockpitInterior.WiperFill(1f, 0f) < CockpitInterior.WiperFill(0.55f, 0f) &&
                      CockpitInterior.WiperFill(1f, 1f) < CockpitInterior.WiperFill(1f, 0f),
                      "a wiped screen beads up again sooner in harder rain and at speed");

                // ---- the spray
                Check(TireSpray.PoolIn > TireSpray.PoolOut && TireSpray.TailFull > TireSpray.TailFrom && TireSpray.TailCapacity >= TireSpray.Capacity,
                      "the tyres' pool has a way in above its way out; the rooster tail has room");

                // ---- the two wet passes read the same pools
                string dir = System.IO.Path.Combine(Application.dataPath, "PSXRacing/Shaders");
                string lit = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "PSXLit.shader"));
                string ssr = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "PSXWetSSR.cginc"));
                Check(lit.Contains("PSXRain2At(") && ssr.Contains("PSXRain2At(") && lit.Contains("PSXRain2Normal(") && ssr.Contains("PSXRain2Normal("),
                      "both wet passes (the surface, the world in the water) find the pools and the rings the same way");
                Check(System.IO.File.Exists(System.IO.Path.Combine(dir, "PSXRain2.cginc.meta")) &&
                      System.IO.File.Exists(System.IO.Path.Combine(dir, "PSXSpecBend.cginc.meta")),
                      "the two new shader includes carry their metas (no GUID churn at the next sync)");
            }
            finally
            {
                Rain2.On = keepOn; Rain2.Pools = keepPools; Rain2.Rings = keepRings;
                Rain2.Push(0f, 0f);
            }
        }
    }
}
