using System;
using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One car's data, baked from RG2's catalog (which itself is built from the
    /// GT4 database). Everything the physics needs to stop being hardcoded to a
    /// single RX-7: mass, a real per-car torque curve, gearing, drivetrain
    /// layout and a spec'd top speed.
    ///
    /// Curve and gear arrays arrive as ';'-joined strings — JsonUtility handles
    /// float lists, but keeping the pairing explicit in one field makes a bad
    /// bake obvious at a glance instead of silently mismatching indices.
    /// </summary>
    [Serializable]
    public class CarSpec
    {
        public string id, name, drv, color, origin;
        public int price, hp, kg, gears, modelYear, redline, idleRPM, peakTorqueNm;
        public bool defaultManual;
        public float topSpeedMps;
        public string tcRPMs, tcNorm, gearSpeeds;

        // === Engine identity ===
        /// <summary>Raw GT4 engine-type string, e.g. "V8 (OHV)", "Rotor2
        /// (Rotary)". Kept verbatim rather than parsed into an enum so a car
        /// that sounds wrong can be diffed straight against RG2's rules.</summary>
        public string eType;
        /// <summary>GT4 aspiration: "NA", "TURBO" or "SuperCharger". 137 of the
        /// 317 are factory turbos; this is what decides whether the forced-
        /// induction voice exists at all.</summary>
        public string asp;
        /// <summary>Displacement in cc, per GT4. 0 when unknown. Rotaries report
        /// PER CHAMBER here (a 13B reads 654), matching the source data.</summary>
        public int dispCc;
        /// <summary>Recorded engine family key — the folder under
        /// Resources/Engines this car speaks through. Empty means "no recording
        /// for this layout": the built-in rotary set stands in.</summary>
        public string engineFamily;

        // === Per-car engine voice ===
        // There is ONE recording per family, so without these all 30 cars that
        // share v8_american_classic_1 are the identical loop at the identical
        // rate. Baked by scripts/bake_voice.mjs straight out of RG2's own
        // computeEngineVoice + the iconic-voice table, so a car sounds here
        // exactly as it does in the game these recordings were tuned in.
        // Everything is the STOCK voice; CarTune's power stage walks the
        // exhaust ladder on top at runtime.

        /// <summary>Playback-rate multiplier on every band, ~0.87-1.14. The
        /// single biggest character axis: a 440 big-block plays its family
        /// 12% slower than the recording, which is 2.1 semitones of "lazy".
        /// Derived inside a per-car safety window, so it can never push a
        /// crossfade slot into the 0.66-1.50 pitch clamp.</summary>
        public float voiceRateMul;
        /// <summary>Level trim so a big-bore car sits heavier in the mix.</summary>
        public float voiceLevelMul;
        /// <summary>Peaking-filter formant: the axis that separates a gruff
        /// pushrod V8 (290 Hz) from an S2000 (980 Hz). Applied on the master
        /// tone chain, which is the only place this project has a parametric
        /// EQ — see AudioToneChain.</summary>
        public float voicePeakHz;
        public float voicePeakDb;
        /// <summary>High-shelf trim, dB. NEGATIVE for a soft cruiser, which is
        /// how a car ends up duller than the recording it borrows.</summary>
        public float voiceShelfDb;

        /// <summary>Firing-order lope: the once-per-cycle unevenness a big-cam
        /// pushrod V8 or an inline-five has and a static filter cannot say.
        /// 0 on an even-fire engine.</summary>
        public float lopeDepth;
        /// <summary>Modulation rate in cycles per crank revolution — 0.5 is the
        /// classic half-order lope.</summary>
        public float lopeOrder;
        /// <summary>RPM by which the wobble has fully smoothed out, as the real
        /// pulses fuse.</summary>
        public float lopeFadeTop;
        /// <summary>Per-car phase seed so spec-twin cars don't pulse in sync.</summary>
        public float lopePhase;

        // === Upgrade ladder endpoints ===
        /// <summary>Realistic fully-built streetable crank HP. The stage ladder
        /// interpolates stock -> built, so this is what a maxed POWER build is
        /// worth. Baked from RG2's platform overrides + aspiration buckets.</summary>
        public int builtHp;
        /// <summary>Minimum weight after streetable reduction, kg.</summary>
        public int minKg;

        [NonSerialized] public float[] curveRPM;
        [NonSerialized] public float[] curveNm;
        [NonSerialized] public float[] gearBoundMps;

        public void Decode()
        {
            if (curveRPM != null) return;
            curveRPM = ParseFloats(tcRPMs);
            var norm = ParseFloats(tcNorm);
            curveNm = new float[norm.Length];
            for (int i = 0; i < norm.Length; i++) curveNm[i] = norm[i] * peakTorqueNm;
            gearBoundMps = ParseFloats(gearSpeeds);
        }

        static float[] ParseFloats(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            var parts = s.Split(';');
            var outv = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out outv[i]);
            return outv;
        }

        /// <summary>
        /// Ratio SHAPE relative to top gear, by gear count — taken from real
        /// road-car gearboxes (the 6-speed row is the RX-7 FD's own box, which
        /// is what this project's handling was tuned against).
        ///
        /// Deliberately NOT derived from the catalog's gearSpeeds. Those look
        /// per-car but are not: RG2 builds them from GEAR_PATTERNS, a generic
        /// table keyed only on gear COUNT, whose 6-speed row implies a 5.88
        /// ratio spread against a real box's 4.98. Deriving from it handed every
        /// six-speed a first gear ~24% shorter than reality, and since first
        /// gear already makes roughly twice the force the rear tires can hold,
        /// that inflated the wheelspin ratio by half — which is the direct input
        /// to the yaw injector that rotates the car on throttle. The car got
        /// noticeably snappier, for no gain in fidelity over a fixed shape.
        /// </summary>
        static readonly float[] Shape4 = { 3.55f, 2.05f, 1.35f, 1.00f };
        static readonly float[] Shape5 = { 4.10f, 2.30f, 1.55f, 1.15f, 1.00f };
        static readonly float[] Shape6 = { 4.98f, 2.88f, 1.99f, 1.43f, 1.15f, 1.00f };

        /// <summary>
        /// Torque off this car's STOCK curve, Nm: CarController.RawTorqueAtRPM's
        /// walk exactly (ramp from zero under the first sample, linear between
        /// samples, flat past the last), on the unscaled curve. A power stage
        /// scales the whole curve, so for finding WHERE the engine peaks the
        /// stock shape is the only input there is.
        /// </summary>
        public float StockTorqueAt(float rpm)
        {
            Decode();
            var xs = curveRPM; var ys = curveNm;
            if (xs == null || ys == null || xs.Length < 2) return Mathf.Max(1f, peakTorqueNm);
            if (rpm <= xs[0]) return ys[0] * Mathf.InverseLerp(0f, xs[0], rpm);
            for (int i = 0; i < xs.Length - 1; i++)
            {
                if (rpm > xs[i + 1]) continue;
                float f = Mathf.InverseLerp(xs[i], xs[i + 1], rpm);
                return Mathf.Lerp(ys[i], ys[i + 1], f);
            }
            return ys[ys.Length - 1];
        }

        /// <summary>
        /// Where this engine makes its most power, between idle and redline —
        /// with the blower's curve on top when one is fitted, because a Roots
        /// blower boosts the bottom of the range hardest and so moves the peak.
        /// It is where top gear is anchored (see <see cref="BuildGearRatios"/>).
        ///
        /// Searched on a fixed 25 rpm grid so the race side and the garage side
        /// arrive at the same float, and memoised: both ask on every spec.
        /// </summary>
        public float PeakPowerRPM(bool blower)
        {
            int slot = blower ? 1 : 0;
            if (peakPowerRpmMemo != null && peakPowerRpmMemo[slot] > 0f) return peakPowerRpmMemo[slot];
            Decode();
            float best = -1f, at = redline;
            float from = Mathf.Max(idleRPM, 500);
            for (float rpm = from; rpm <= redline + 1e-3f; rpm += 25f)
            {
                float p = StockTorqueAt(rpm) * rpm *
                          (blower ? CarTune.BlowerBoost(rpm, idleRPM, redline) : 1f);
                if (p > best) { best = p; at = rpm; }
            }
            if (peakPowerRpmMemo == null) peakPowerRpmMemo = new float[2];
            peakPowerRpmMemo[slot] = at;
            return at;
        }
        [NonSerialized] float[] peakPowerRpmMemo;

        /// <summary>
        /// The top speed of this car AS BUILT, m/s: the stock figure
        /// (<see cref="topSpeedMps"/>, baked from GT4's spec fields) times the
        /// power build's percentage (<see cref="CarTune.TopSpeedMult"/>). A race
        /// car is already built and takes nothing — see
        /// <see cref="CarTune.BoughtOf"/>.
        /// </summary>
        public float BuildTopSpeedMps(int powerStage, bool blower) =>
            BuildTopSpeedMps(powerStage, blower, false);

        /// <param name="turboKit">A turbo kit fitted to an NA engine: the car is
        /// on the TURBO path (see <see cref="OnTurboPath"/>).</param>
        public float BuildTopSpeedMps(int powerStage, bool blower, bool turboKit)
        {
            float stock = topSpeedMps > 1f ? topSpeedMps : 60f;
            return IsRaceCar ? stock
                : stock * CarTune.TopSpeedMult(powerStage, blower && !turboKit, PathShare(turboKit));
        }

        // ---- the two power paths (CarTune: "two ladders") ----------------------

        public bool IsNaturallyAspirated => !IsForcedInduction;
        /// <summary>May take a turbo kit: an NA road car. A factory turbo is
        /// already on the turbo path; a factory blower and a race car stay put.</summary>
        public bool CanFitTurboKit => IsNaturallyAspirated && !IsRaceCar;
        /// <summary>Whether this car's power ladder is the TURBO one.</summary>
        public bool OnTurboPath(bool turboKit) => IsTurbo || (turboKit && CanFitTurboKit);

        /// <summary>Crank hp at stage 4 on this car's path: the baked build
        /// ceiling on the turbo path (and for anything with only one path), a
        /// share of its gain on the NA path.</summary>
        public int CeilingHp(bool turboKit)
        {
            if (!CanFitTurboKit || turboKit) return builtHp;
            return Mathf.RoundToInt(hp + (builtHp - hp) * CarTune.NaPathShare);
        }

        /// <summary>The ceiling's share of the full build's gain (1 or
        /// NaPathShare): what the top-speed rule scales by.</summary>
        public float PathShare(bool turboKit) =>
            builtHp > hp ? (CeilingHp(turboKit) - hp) / (float)(builtHp - hp) : 1f;

        /// <summary>Crank hp at a power stage on this car's path.</summary>
        public int HpAtStage(int stage, bool turboKit) =>
            CarTune.PowerAtStage(hp, CeilingHp(turboKit), stage);

        /// <summary>The stock gearbox: see the overload below.</summary>
        public float[] BuildGearRatios(float wheelRadius, float finalDrive) =>
            BuildGearRatios(wheelRadius, finalDrive, BuildTopSpeedMps(0, false), PeakPowerRPM(false));

        /// <summary>
        /// Gear ratios for this car: the shape above, scaled so that in top
        /// gear the engine is at <paramref name="anchorRpm"/> exactly at
        /// <paramref name="vmax"/>. That anchor is what makes a 90 hp hatchback
        /// and a 600 hp supercar both gear sensibly off one table.
        ///
        /// THE ANCHOR IS PEAK POWER, not redline (2026-09-21). With redline at
        /// top speed, a car whose power falls away before redline could be
        /// geared LONGER on the tuning page and go faster than its own top
        /// speed — up to 32% faster on the '71 Cuda, whose torque dies at the
        /// top of the range. With the power peak there, this gearbox is the
        /// fastest the car can have: go longer and the engine is below its peak
        /// at that speed, go shorter and it is past it. Over the catalog that
        /// puts first gear a median 3% LONGER than the old anchor did, so the
        /// feel of a launch barely moved.
        /// </summary>
        public float[] BuildGearRatios(float wheelRadius, float finalDrive, float vmax, float anchorRpm)
        {
            Decode();
            int n = Mathf.Clamp(gears, 3, 8);
            float[] shape = n <= 4 ? Shape4 : (n == 5 ? Shape5 : Shape6);

            vmax = vmax > 1f ? vmax : 60f;
            float anchor = anchorRpm > 1f ? Mathf.Min(anchorRpm, redline) : redline;
            float wheelRpmAtVmax = vmax / (2f * Mathf.PI * wheelRadius) * 60f;
            float topRatio = anchor / Mathf.Max(1f, wheelRpmAtVmax * finalDrive);

            var ratios = new float[n];
            for (int g = 0; g < n; g++)
            {
                // Gear counts outside the table (7-8) stretch the six-speed
                // shape rather than inventing a new one.
                float t = shape.Length > 1 ? g / (float)(n - 1) * (shape.Length - 1) : 0f;
                int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, shape.Length - 1);
                int j = Mathf.Min(i + 1, shape.Length - 1);
                ratios[g] = topRatio * Mathf.Lerp(shape[i], shape[j], t - i);
            }
            return ratios;
        }

        public bool IsTurbo => asp == "TURBO";
        public bool IsSupercharged => asp == "SuperCharger";
        /// <summary>Any forced induction — the gate on the boost voice.</summary>
        public bool IsForcedInduction => IsTurbo || IsSupercharged;
        /// <summary>Purpose-built race cars. Same name test RG2 uses; it drives
        /// the repair-cost premium and the skill gate, not the physics.</summary>
        public bool IsRaceCar => !string.IsNullOrEmpty(name) && name.Contains("Race Car");

        public bool IsFrontDriven => drv == "FF" || drv == "4WD";
        public bool IsRearDriven => drv != "FF";
        /// <summary>Share of drive torque sent forward. RG2 does not carry a
        /// centre-diff bias, so 4WD uses a fixed rear-leaning split — that is
        /// what makes an Impreza still rotate on throttle instead of ploughing.
        /// </summary>
        public float FrontDriveShare => drv == "FF" ? 1f : (drv == "4WD" ? 0.4f : 0f);
    }

    /// <summary>
    /// The baked catalog: 317 accessible non-bike cars from RG2, price-sorted.
    /// Loaded once from Resources.
    /// </summary>
    public static class CarCatalog
    {
        [Serializable] class Bundle { public List<CarSpec> cars = new List<CarSpec>(); }

        const string ResourceName = "rg2_cars";
        static List<CarSpec> all;
        static Dictionary<string, CarSpec> byId;

        static void Load()
        {
            if (all != null) return;
            var text = Resources.Load<TextAsset>(ResourceName);
            if (text == null)
            {
                Debug.LogWarning("CarCatalog: Resources/" + ResourceName +
                                 ".json missing — falling back to the built-in car.");
                all = new List<CarSpec>();
            }
            else all = JsonUtility.FromJson<Bundle>(text.text).cars;

            byId = new Dictionary<string, CarSpec>();
            foreach (var c in all) { c.Decode(); byId[c.id] = c; }
        }

        public static IReadOnlyList<CarSpec> All { get { Load(); return all; } }
        public static bool Ready { get { Load(); return all.Count > 0; } }

        public static CarSpec Get(string id)
        {
            Load();
            return id != null && byId.TryGetValue(id, out var c) ? c : null;
        }

        /// <summary>Cars whose price sits in a band, for the market's lanes.</summary>
        public static List<CarSpec> InPriceBand(int min, int max)
        {
            Load();
            var hits = new List<CarSpec>();
            foreach (var c in all) if (c.price >= min && c.price <= max) hits.Add(c);
            return hits;
        }

        public static List<CarSpec> InPriceBand(int min, int max, int maxAgeYears, int gameYear)
        {
            var hits = InPriceBand(min, max);
            hits.RemoveAll(c => gameYear - c.modelYear > maxAgeYears);
            return hits;
        }

        /// <summary>
        /// RG2's pick rule: sort by price and draw from the most expensive half
        /// of the eligible set, so a lane offers the best car the band allows
        /// rather than regressing to the cheapest every time.
        /// </summary>
        public static CarSpec PickFromUpperHalf(List<CarSpec> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            pool.Sort((a, b) => b.price.CompareTo(a.price));
            int half = Mathf.Max(1, pool.Count / 2);
            return pool[UnityEngine.Random.Range(0, half)];
        }
    }
}
