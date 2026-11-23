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

                // ---- (rainglass 11-22) the wipers' stroke, phase maps and modelled nodes
                bool keepMap = Rain2.WiperMap;
                try
                {
                    Rain2.WiperMap = true;
                    float s0 = CockpitInterior.StrokeS(0f, 1f), sq = CockpitInterior.StrokeS(0.25f, 1f), sMid = CockpitInterior.StrokeS(0.5f, 1f),
                          sEnd = CockpitInterior.StrokeS(1f, 1f), sNear0 = CockpitInterior.StrokeS(0.02f, 1f), sNearTop = CockpitInterior.StrokeS(0.48f, 1f);
                    Check(s0 == 0f && sEnd == 0f && Mathf.Abs(sMid - 1f) < 1e-4f && Mathf.Abs(sq - 0.5f) < 1e-4f && sNear0 < 0.02f && sNearTop > 0.98f,
                          "the stroke value s: 0 at park, 1 at the top, eased at both ends (slow off park and at the top)",
                          $"s(0) {s0:0.000} s(.02) {sNear0:0.000} s(.25) {sq:0.000} s(.48) {sNearTop:0.000} s(.5) {sMid:0.000} s(1) {sEnd:0.000}");
                    bool dwell = true;
                    foreach (float r in new[] { 0.6f, 0.75f, 1f })
                    {
                        CockpitInterior.WiperTiming(r, out float st, out float pe);
                        if (pe - st < 0.15f) dwell = false;
                    }
                    Check(dwell, "every wiper setting rests at park between strokes (at least 0.15 s)");
                    // the placeholder maps from the drawn arcs
                    float mu = 1.45f, mv = 0.72f, reach = Mathf.Min(0.46f * mu, 1.02f * mv);
                    var wv = new Vector4(0.10f * mu, 0.52f * mu, -0.04f * mv, reach);
                    var av = new Vector4(CockpitInterior.WiperParkDeg * Mathf.Deg2Rad, CockpitInterior.WiperSweepDeg * Mathf.Deg2Rad, 0.20f, 0.011f * mu);
                    WiperPhase.Analytic(wv, av, mu, mv, out float[] pd, out float[] pp);
                    int swept = 0, rising = 0, tested = 0;
                    for (int i = 0; i < pd.Length; i++) if (!float.IsNaN(pd[i]) || !float.IsNaN(pp[i])) swept++;
                    // along a ring round the driver's spindle the phase grows with the angle
                    float prev = -1f; bool mono = true;
                    for (float ang = 8f; ang < 98f; ang += 6f)
                    {
                        float th = ang * Mathf.Deg2Rad, rr = reach * 0.7f;
                        float qx = wv.x + Mathf.Cos(th) * rr, qy = wv.z + Mathf.Sin(th) * rr;
                        int x = Mathf.FloorToInt(qx / mu * WiperPhase.W), y = Mathf.FloorToInt(qy / mv * WiperPhase.H);
                        if (x < 0 || y < 0 || x >= WiperPhase.W || y >= WiperPhase.H) continue;
                        float ph0 = pd[y * WiperPhase.W + x];
                        if (float.IsNaN(ph0)) continue;
                        tested++;
                        if (ph0 <= prev) mono = false; else rising++;
                        prev = ph0;
                    }
                    float share = 100f * swept / pd.Length;
                    Check(share > 35f && share < 95f && mono && tested >= 6,
                          "the placeholder phase maps: the blades sweep most of the screen and the phase grows along the stroke",
                          $"swept {share:0}% of the pane; ring samples {tested}, rising {rising}");
                    var tex = WiperPhase.Pack(pd, pp, mu, mv, true);
                    var px = tex.GetPixels32();
                    // inside a swept texel the edge channel reads > 0.5, outside < 0.5
                    int agree = 0, n = 0;
                    for (int i = 0; i < pd.Length; i += 7)
                    {
                        n++;
                        bool inside = !float.IsNaN(pd[i]);
                        if (inside == (px[i].b > 127)) agree++;
                    }
                    Check(tex.width == WiperPhase.W && tex.height == WiperPhase.H && !tex.isDataSRGB && agree >= n * 0.995f,
                          "the phase texture: 256 x 128, linear, its edge field agrees with the swept area",
                          $"{tex.width}x{tex.height} sRGB {tex.isDataSRGB} agree {agree}/{n}");
                    Object.DestroyImmediate(tex);
                    // a v10 layout's wipers block reads, and the hinge table interpolates
                    var lay = JsonUtility.FromJson<CockpitModel.Layout>("{\"wipers\":[{\"key\":\"driver\",\"armPart\":\"wiper_driver_arm\",\"armPivot\":{\"x\":-0.4,\"y\":0.9,\"z\":1.1},\"armAxis\":{\"x\":0,\"y\":1,\"z\":0},\"armRot\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"strokeDeg\":92.5,\"armSign\":-1,\"hingeArmDeg\":[0,5,10],\"hingeDeg\":[0,-1,-3]}]}");
                    Check(lay != null && lay.wipers != null && lay.wipers.Length == 1 && lay.wipers[0].strokeDeg == 92.5f && lay.wipers[0].armSign == -1f
                          && Mathf.Abs(CockpitInterior.Hinge(lay.wipers[0].hingeArmDeg, lay.wipers[0].hingeDeg, 7.5f) + 2f) < 1e-4f
                          && CockpitInterior.Hinge(lay.wipers[0].hingeArmDeg, lay.wipers[0].hingeDeg, 40f) == -3f,
                          "a v10 cockpit's wipers block reads (CockpitModel.WiperDef) and the blade's hinge table interpolates, held at its ends");
                    // the converter keeps Wiper_* arms and blades as their own parts
                    string convPath = System.IO.Path.Combine(Application.dataPath, "../tools/cockpit/cockpit_v08.py");
                    string conv = System.IO.File.Exists(convPath) ? System.IO.File.ReadAllText(convPath) : null;
                    if (conv == null) Line("  (the cockpit converter is not in this project copy - a sandbox: its check runs in the repo)");
                    else Check(conv.Contains("def wiper_parts(") && conv.Contains("o.name.startswith(\"Wiper_\")") && conv.Contains("WIPER AXIS CHECK FAILED"),
                          "the cockpit converter writes Wiper_* arms and blades as separate rotating parts and checks their axis convention");
                }
                finally { Rain2.WiperMap = keepMap; }

                // ---- (rainglass2 11-22) the round-2 switches reach the shaders, and off is off
                {
                    bool kb = Rain2.Bank2, kc = Rain2.Cover, kl = Rain2.Clear, kd = Rain2.LensDrops, kg = Rain2.CarBeads, ko = Rain2.On;
                    try
                    {
                        Rain2.On = true; Rain2.Bank2 = Rain2.Cover = Rain2.Clear = Rain2.LensDrops = Rain2.CarBeads = true;
                        Rain2.Push(1f, 1f);
                        var g2 = Shader.GetGlobalVector("_PSXRain2Glass2"); var g3 = Shader.GetGlobalVector("_PSXRain2Glass3");
                        Rain2.Bank2 = false; Rain2.Cover = Rain2.Clear = Rain2.LensDrops = Rain2.CarBeads = false;
                        Rain2.Push(1f, 1f);
                        var o2 = Shader.GetGlobalVector("_PSXRain2Glass2"); var o3 = Shader.GetGlobalVector("_PSXRain2Glass3");
                        Rain2.On = false; Rain2.Bank2 = Rain2.Cover = Rain2.Clear = Rain2.LensDrops = Rain2.CarBeads = true;
                        Rain2.Push(1f, 1f);
                        var f2 = Shader.GetGlobalVector("_PSXRain2Glass2");
                        Check(g2 == Vector4.one && g3.x == 1f && o2 == Vector4.zero && o3.x == 0f && f2 == Vector4.zero,
                              "the window-rain round-2 switches (BANK2, COVER, CLEAR, LENSDROPS, CARBEADS) reach the shaders; off, or rain 2 off, is fe3ce8a6's picture",
                              $"on {g2}/{g3.x} off {o2}/{o3.x} rain2 off {f2}");
                        string sd = System.IO.Path.Combine(Application.dataPath, "PSXRacing/Shaders");
                        string cab = System.IO.File.ReadAllText(System.IO.Path.Combine(sd, "PSXCabinGlass.shader"));
                        string lens = System.IO.File.ReadAllText(System.IO.Path.Combine(sd, "PSXLens.shader"));
                        Check(cab.Contains("_PSXRain2Glass2") && cab.Contains("R2G_BANK2_M") && cab.Contains("depoFresh") && lens.Contains("_PSXRain2Glass2.w"),
                              "the cabin glass draws the wiper's bank and its dropped lump; the lens reads its drop switch");
                    }
                    finally
                    {
                        Rain2.Bank2 = kb; Rain2.Cover = kc; Rain2.Clear = kl; Rain2.LensDrops = kd; Rain2.CarBeads = kg; Rain2.On = ko;
                    }
                }

                // ---- the spray
                Check(TireSpray.PoolIn > TireSpray.PoolOut && TireSpray.TailFull > TireSpray.TailFrom && TireSpray.TailCapacity >= TireSpray.Capacity,
                      "the tyres' pool has a way in above its way out; the rooster tail has room");

                TestSpray2();
                TestCarDrops2();

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

        // ---- SPRAY 2 and CARDROPS 2 (sprayfix 2026-11-23) ------------------------------------------

        /// <summary>The spray is born at the tyres: no puff or droplet above a third of its tyre's
        /// height at birth (TireSpray's births, every tail, three tyre sizes, a banked road); and drawn
        /// over the scene it can never outshine the light it is lit by.</summary>
        static void TestSpray2()
        {
            Random.InitState(1123);
            float worstNew = 0f, worstOld = 0f;
            Vector3 n = new Vector3(0.08f, 1f, 0.03f).normalized;       // an 8 % bank on a grade
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, n).normalized;
            Vector3 side = Vector3.Cross(n, fwd).normalized;
            Vector3 pt = new Vector3(10f, 2f, 5f);
            foreach (float r in new[] { 0.28f, 0.31f, 0.36f })
                for (int i = 0; i < 3000; i++)
                {
                    float tail = (i % 5) / 4f;
                    float dir = i % 7 == 0 ? -1f : 1f;
                    TireSpray.Mist2Birth(pt, n, fwd, side, r, dir, tail, fwd * 40f, out var bp, out _, out _, out _);
                    Vector3 dp = TireSpray.Drop2Birth(pt, n, fwd, side, r, dir);
                    worstNew = Mathf.Max(worstNew, Mathf.Max(Vector3.Dot(bp - pt, n), Vector3.Dot(dp - pt, n)) / (2f * r));
                    Vector3 op = TireSpray.MistOldBirth(pt, n, fwd, side, dir), od = TireSpray.DropOldBirth(pt, n, fwd, side, dir);
                    worstOld = Mathf.Max(worstOld, Mathf.Max(Vector3.Dot(op - pt, n), Vector3.Dot(od - pt, n)) / (2f * r));
                }
            Line("  spray births: highest " + worstNew.ToString("0.00") + " of the tyre's height (a99a8ef1: " + worstOld.ToString("0.00") +
                 "; its traffic mist 0.25 m over the body's origin)");
            if (!TireSpray.Spray2) { Line("  (PSX_SPRAY2=0: a99a8ef1's spray, its checks skipped)"); return; }
            Check(worstNew <= TireSpray.BirthShareMax + 1e-4f, "SPRAY2: every puff and droplet is born at its tyre, under a third of the tyre's height",
                  worstNew.ToString("0.000"));
            // Drawn OVER: n stacked puffs of opacity a, lit L, over a road R give R(1-a)^n + L(1-(1-a)^n), never past L.
            // The PSX/Rain constants read from the shader; the tint is TireSpray's.
            string rain = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "PSXRacing/Shaders/PSXRain.shader"));
            float sprayBase = ShaderDefine(rain, "SPRAY_BASE"), oldBase = ShaderDefine(rain, "RAIN_BASE"), cap = ShaderDefine(rain, "SPRAY_LAMP_CAP");
            // a rainy noon, and a night right behind a lit tail lamp, in the shader's linear units
            // (scene = ambient + sky/2 + sun x 0.35); the road behind: wet asphalt (albedo 0.10) in the same light
            var cases = new[] { ("rainy day", 0.55f, 0.0f), ("night behind a lit tail lamp", 0.02f, 2.5f) };
            foreach (var (what, scene, lamp) in cases)
            {
                float road = 0.10f * (scene + lamp * 0.3f);
                float lit = 0.82f * (scene * sprayBase + lamp / (1f + lamp / cap));
                float over = road, add = road;
                for (int k = 0; k < 16; k++)
                {
                    over = over * (1f - TireSpray.Alpha2) + lit * TireSpray.Alpha2;
                    add += 0.82f * (scene * oldBase + lamp * 1.4f) * TireSpray.Alpha;
                }
                Line("  " + what + ": 16 stacked puffs over the road (" + road.ToString("0.000") + "): " + over.ToString("0.000") +
                     " (a99a8ef1 added: " + add.ToString("0.000") + ")");
                Check(over <= lit + 1e-4f && over <= Mathf.Max(road * 6f, 0.30f) && lit <= 0.82f * (scene * sprayBase + cap) + 1e-4f,
                      "SPRAY2 (" + what + "): the plume never outshines the light it takes, and the lamps' share is capped", over.ToString("0.000"));
            }
            var m = TireSpray.SprayMaterial2();
            Check(m == null || (m.GetFloat("_Over") > 0.5f && Mathf.Approximately(m.GetFloat("_DstBlend"), (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha)),
                  "SPRAY2: the spray's material draws over the scene, not added to it");
            Check(rain.Contains("Blend [_SrcBlend] [_DstBlend], Zero One"), "PSX/Rain never writes the emitter mask (alpha: Zero One)");
        }

        static float ShaderDefine(string src, string name)
        {
            var mt = System.Text.RegularExpressions.Regex.Match(src, @"#define\s+" + name + @"\s+([0-9.]+)");
            return mt.Success ? float.Parse(mt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : float.NaN;
        }

        // ---- a CPU port of PSXCarDrops.cginc's CARDROPS 2 layers (the resolved look: a close camera) ----
        static float Frac(float x) => x - Mathf.Floor(x);
        static Vector4 CD2Hash(Vector2 p)
        {
            Vector4 p4 = new Vector4(Frac(p.x * 0.1031f), Frac(p.y * 0.1030f), Frac(p.x * 0.0973f), Frac(p.y * 0.1099f));
            float d = p4.x * (p4.w + 33.33f) + p4.y * (p4.z + 33.33f) + p4.z * (p4.x + 33.33f) + p4.w * (p4.y + 33.33f);
            p4 += new Vector4(d, d, d, d);
            return new Vector4(Frac((p4.x + p4.y) * p4.z), Frac((p4.x + p4.z) * p4.y), Frac((p4.y + p4.z) * p4.w), Frac((p4.z + p4.w) * p4.x));
        }
        static float CD2Dome(Vector2 d, float r) => Mathf.Clamp01(1f - Vector2.Dot(d, d) / (r * r));
        const float CellS = 0.0070f, RSMin = 0.150f, RSMax = 0.415f, CellL = 0.0160f, RLMin = 0.120f, RLMax = 0.260f, ColW = 0.0140f;
        static float CD2Bead(Vector2 q, float cell, float rMin, float rMax, float seed, float time, float density)
        {
            Vector2 id = new Vector2(Mathf.Floor(q.x / cell), Mathf.Floor(q.y / cell));
            Vector4 h = CD2Hash(id + new Vector2(seed, seed));
            Vector2 c = (id + new Vector2(0.5f + (h.x - 0.5f) * 0.5f, 0.5f + (h.y - 0.5f) * 0.5f)) * cell;
            float life = Frac(time * (0.05f + 0.08f * h.w) + h.z);
            float alive = (h.z <= density ? 1f : 0f) * Mathf.SmoothStep(0f, 1f, life / 0.08f) * (1f - Mathf.SmoothStep(0f, 1f, (life - 0.75f) / 0.25f));
            return CD2Dome(q - c, cell * Mathf.Lerp(rMin, rMax, h.w)) * alive;
        }
        static float CD2Riv(Vector2 q, Vector2 dir, float t, float seed, out float wipe)
        {
            Vector2 sd = new Vector2(dir.y, -dir.x);
            float along = Vector2.Dot(q, dir), x = Vector2.Dot(q, sd) / ColW, col = Mathf.Floor(x);
            Vector4 h = CD2Hash(new Vector2(col, seed));
            float span = Mathf.Lerp(0.06f, 0.30f, h.z);
            float y = along / span - t * Mathf.Lerp(0.6f, 1.4f, h.x) * 0.5f / span + h.y * 7f, cyc = Mathf.Floor(y);
            Vector4 k = CD2Hash(new Vector2(col, cyc + seed * 3.1f));
            float f = Frac(y);
            float wob = (Mathf.Sin(along * 41f + k.y * 6.28f) * 0.6f + Mathf.Sin(along * 97f + k.z * 6.28f) * 0.4f) * 0.0022f;
            float dx = (Frac(x) - 0.5f - (k.z - 0.5f) * 0.4f) * ColW - wob;
            float rHead = Mathf.Lerp(0.0012f, 0.0025f, k.w);
            float head = CD2Dome(new Vector2(dx, (f - 0.92f) * span), rHead);
            float back = (0.92f - f) * span, len = span * Mathf.Lerp(0.35f, 0.85f, k.y);
            float tr = (back >= 0f ? 1f : 0f) * (1f - Mathf.SmoothStep(0f, 1f, (back - len * 0.6f) / (len * 0.4f)));
            float halfW = Mathf.Max(rHead * 0.55f * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(back / len)), 1e-4f);
            float trail = Mathf.Clamp01(1f - Mathf.Abs(dx) / halfW) * tr * 0.55f;
            float wHalf = rHead * 1.2f;
            wipe = (Mathf.Abs(dx) <= wHalf ? 1f : 0f) * (back >= -rHead * 1.2f ? 1f : 0f) * (1f - Mathf.SmoothStep(0f, 1f, (back - len * 0.8f) / (len * 0.8f)));
            return Mathf.Max(head, trail);
        }

        /// <summary>Beads 2-10 mm across in the body's own metres on three panels of three cars; rivulets
        /// under 8 mm wide; no rivulet pixel over a standing bead (the CPU port of the shader, 0.25 mm pixels).</summary>
        static void TestCarDrops2()
        {
            string inc = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "PSXRacing/Shaders/PSXCarDrops.cginc"));
            Check(Mathf.Approximately(ShaderDefine(inc, "CD2_CELL_S"), CellS) && Mathf.Approximately(ShaderDefine(inc, "CD2_RS_MAX"), RSMax) &&
                  Mathf.Approximately(ShaderDefine(inc, "CD2_CELL_L"), CellL) && Mathf.Approximately(ShaderDefine(inc, "CD2_RL_MAX"), RLMax) &&
                  Mathf.Approximately(ShaderDefine(inc, "CD2_COL_W"), ColW),
                  "CARDROPS2: the self-test's port reads the shader's own sizes");
            // three cars: the body frame is the car's own metres (the bead sizes are set there)
            int cars = 0;
            foreach (var body in new[] { CarModelLibrary.Body.Hatch, CarModelLibrary.Body.Saloon, CarModelLibrary.Body.Pickup, CarModelLibrary.Body.Van })
            {
                if (cars >= 3) break;
                CarSpec spec = null;
                foreach (var c in CarCatalog.All) if (CarModelLibrary.BodyOf(c) == body) { spec = c; break; }
                if (spec == null) continue;
                var def = CarModelLibrary.LoadFor(spec);
                if (def == null || def.bodyMesh == null) continue;
                var fit = CarModelLibrary.Fit(def, spec);
                var sz = def.bodyMesh.bounds.size;
                float len = Mathf.Max(sz.x * Mathf.Max(fit.sx, 0.01f), sz.z * Mathf.Max(fit.sz, 0.01f));
                cars++;
                // three panels: the roof (flat), a door (upright), the boot lid / tailgate (35 degrees): a bead's
                // true diameter on the panel is its plane diameter over the cosine of the panel to its plane
                float dMin = 2f * CellS * RSMin * 1000f, dMax = 0f;
                foreach (float slant in new[] { 0f, 0f, 35f })
                {
                    float cos = Mathf.Cos(slant * Mathf.Deg2Rad);
                    dMax = Mathf.Max(dMax, Mathf.Max(2f * CellL * RLMax, 2f * CellS * RSMax) * 1000f / cos);
                }
                Line("  " + body + " '" + spec.id + "' (" + def.key + "): " + len.ToString("0.00") + " m long; beads " + dMin.ToString("0.0") + "-" +
                     dMax.ToString("0.0") + " mm on the roof, a door and a 35-degree lid");
                Check(len > 3.0f && len < 6.5f && dMin >= 2f - 1e-3f && dMax <= 10.2f, "CARDROPS2: " + body + " beads 2-10 mm across in the car's own metres",
                      len.ToString("0.00") + " m, " + dMin.ToString("0.0") + "-" + dMax.ToString("0.0") + " mm");
            }
            Check(cars >= 3, "CARDROPS2: three body types tried (hatch, saloon, a pickup or van)", cars);
            // rivulets and beads on a 20 x 20 cm patch, parked (down) and at speed (back along the air), at three times
            int both = 0, river = 0, beadPx = 0;
            float widest = 0f;
            var runs = new[] { (new Vector2(0f, -1f), 3.1f), (new Vector2(0f, -1f), 17.7f), (new Vector2(-0.8f, -0.6f), 41.3f) };
            foreach (var (dir, t) in runs)
            {
                const float px = 0.00025f;
                for (int j = 0; j < 800; j++)
                {
                    int run = 0;
                    for (int i = 0; i < 800; i++)
                    {
                        var q = new Vector2(0.3f + i * px, -0.2f + j * px);
                        float h = CD2Riv(q, dir, t, 4.1f, out float wipe);
                        float b = Mathf.Max(CD2Bead(q, CellS, RSMin, RSMax, 1.7f, t, 0.6f), CD2Bead(q, CellL, RLMin, RLMax, 9.3f, t, 0.3f)) * (1f - wipe);
                        bool w = h > 0.05f, bb = b > 0.05f;
                        if (w) river++;
                        if (bb) beadPx++;
                        if (w && bb) both++;
                        // the widest run of water across a line (the parked rivulets run along y here)
                        if (w && dir.x == 0f) { run++; widest = Mathf.Max(widest, run * px * 1000f); } else run = 0;
                    }
                }
            }
            Line("  rivulet pixels " + river + ", bead pixels " + beadPx + ", both at once " + both + "; widest rivulet " + widest.ToString("0.0") + " mm");
            Check(river > 200 && beadPx > 2000, "CARDROPS2: the patch has rivulets and beads to judge", river + " / " + beadPx);
            Check(widest < 8f, "CARDROPS2: a rivulet is under 8 mm wide", widest.ToString("0.0") + " mm");
            Check(both == 0, "CARDROPS2: no rivulet pixel over a standing bead (a rivulet takes the beads in its path)", both);
        }
    }
}
