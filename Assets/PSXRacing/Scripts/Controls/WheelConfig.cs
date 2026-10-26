using System;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>One control bound to one raw input: an axis (a pedal, a
    /// handbrake lever) or a button. <see cref="rest"/> and <see cref="full"/>
    /// are the raw values at rest and fully pressed, as CAPTURED - so a pedal
    /// that reads 1 at rest and -1 pressed, or a combined throttle/brake axis
    /// resting at 0, needs nothing else.</summary>
    [Serializable]
    public class WheelBinding
    {
        public string dev = "";
        public int ord;
        public bool button;
        public int index = -1;
        public float rest;
        public float full = 1f;

        public bool Bound => index >= 0 && !string.IsNullOrEmpty(dev);
        public void Clear() { dev = ""; ord = 0; button = false; index = -1; rest = 0f; full = 1f; }
        public WheelBinding Copy() => (WheelBinding)MemberwiseClone();
        public string Label => !Bound ? "NOT SET" : (button ? "BUTTON " + (index + 1) : "AXIS " + (index + 1));
    }

    /// <summary>The steering axis: its raw value at full left, at full right
    /// and at centre, as captured. Left above right is fine (an inverted
    /// axis): the mapping divides by the signed span.</summary>
    [Serializable]
    public class WheelSteerBinding
    {
        public string dev = "";
        public int ord;
        public int index = -1;
        public float left = -1f, right = 1f, centre;

        public bool Bound => index >= 0 && !string.IsNullOrEmpty(dev) && Mathf.Abs(right - left) > 0.05f;
        public void Clear() { dev = ""; ord = 0; index = -1; left = -1f; right = 1f; centre = 0f; }
        public string Label => Bound ? "AXIS " + (index + 1) : "NOT SET";
    }

    /// <summary>Everything the WHEEL SETUP screen sets, saved as one JSON
    /// string in PlayerPrefs (psx.wheel).</summary>
    [Serializable]
    public class WheelConfigData
    {
        public int version = 1;
        /// <summary>The player's switch: a configured wheel can be parked
        /// without forgetting its setup (CONTROLS > WHEEL).</summary>
        public bool enabled = true;
        public WheelSteerBinding steer = new WheelSteerBinding();
        /// <summary>Degrees lock to lock the BASE is set to (Moza Pit House's
        /// steering angle) - the raw axis spans exactly this.</summary>
        public int rotationDeg = WheelConfig.DefaultRotation;
        public int ratioMode;                 // WheelConfig.RatioCar / RatioFullLock
        public int linearityPct = 100;        // FULL LOCK only: 100 = linear
        public bool speedLock;                // the car's speed-sensitive lock (an assist)
        public int steerDeadPct;
        public WheelBinding throttle = new WheelBinding();
        public WheelBinding brake = new WheelBinding();
        public WheelBinding clutch = new WheelBinding();
        public int pedalDeadPct = WheelConfig.DefaultPedalDead;
        public int invertMask;                // 1 throttle, 2 brake, 4 clutch
        public WheelBinding shiftUp = new WheelBinding();
        public WheelBinding shiftDown = new WheelBinding();
        public WheelBinding handbrake = new WheelBinding();
        public WheelBinding lookBack = new WheelBinding();
        public WheelBinding reset = new WheelBinding();
        public WheelBinding camera = new WheelBinding();
        public WheelBinding pause = new WheelBinding();
        public bool ffbOn = true;
        public int ffbStrengthPct = WheelConfig.DefaultFfbStrength;
        public bool ffbInvert;
    }

    /// <summary>
    /// THE WHEEL'S SETUP and the arithmetic from raw numbers to the car
    /// (owner, 2026-10-26: "Wheel and pedal input with a setup screen, so the
    /// Moza drives properly").
    ///
    /// STEERING. The raw axis spans the base's whole rotation (ROTATION, set to
    /// what Pit House says - 900 by default). Two ways onto the road wheels:
    ///   1:1 CAR   the car's own steering ratio. Every car's lock is reached
    ///             at 450 degrees of wheel - two and a half turns lock to lock,
    ///             a road car's - so road angle = wheel angle x lock / 450. A
    ///             900 degree base meets the car's lock exactly at its stops; a
    ///             540 degree base runs out of wheel before the car runs out of
    ///             lock, the way a real car would on that wheel.
    ///   FULL LOCK the base's whole rotation maps onto the car's lock, through
    ///             LINEARITY (100% straight; more = finer near centre).
    /// Either way the result is a road-wheel ANGLE, which CarController takes
    /// as it is (<see cref="CarController.wheelSteerDeg"/>), skipping the pad's
    /// expo, the keyboard's unwind, STEERING SENSITIVITY and - unless SPEED
    /// LOCK is on - the speed-sensitive lock that exists so a thumb cannot
    /// flick a car into a spin. Those are assists for controls without travel;
    /// a wheel has travel.
    /// </summary>
    public static class WheelConfig
    {
        const string PrefKey = "psx.wheel";
        public const int DefaultRotation = 900, MinRotation = 180, MaxRotation = 1080, RotationStep = 10;
        public const int RatioCar = 0, RatioFullLock = 1;
        /// <summary>Wheel degrees at which every car's lock is reached in 1:1 CAR.</summary>
        public const float CarLockAtWheelDeg = 450f;
        public const int DefaultPedalDead = 3, MaxPedalDead = 20, MaxSteerDead = 10;
        public const int MinLinearity = 100, MaxLinearity = 250, LinearityStep = 10;
        public const int DefaultFfbStrength = 60;

        static WheelConfigData data;

        public static WheelConfigData Data
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        static void Load()
        {
            data = null;
            string s = PlayerPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(s))
            {
                try { data = JsonUtility.FromJson<WheelConfigData>(s); } catch { data = null; }
            }
            if (data == null) data = new WheelConfigData();
            Sanitize(data);
        }

        static void Sanitize(WheelConfigData d)
        {
            if (d.steer == null) d.steer = new WheelSteerBinding();
            d.throttle = d.throttle ?? new WheelBinding();
            d.brake = d.brake ?? new WheelBinding();
            d.clutch = d.clutch ?? new WheelBinding();
            d.shiftUp = d.shiftUp ?? new WheelBinding();
            d.shiftDown = d.shiftDown ?? new WheelBinding();
            d.handbrake = d.handbrake ?? new WheelBinding();
            d.lookBack = d.lookBack ?? new WheelBinding();
            d.reset = d.reset ?? new WheelBinding();
            d.camera = d.camera ?? new WheelBinding();
            d.pause = d.pause ?? new WheelBinding();
            d.rotationDeg = Mathf.Clamp(Mathf.RoundToInt(d.rotationDeg / (float)RotationStep) * RotationStep,
                                        MinRotation, MaxRotation);
            d.ratioMode = Mathf.Clamp(d.ratioMode, RatioCar, RatioFullLock);
            d.linearityPct = Mathf.Clamp(d.linearityPct, MinLinearity, MaxLinearity);
            d.steerDeadPct = Mathf.Clamp(d.steerDeadPct, 0, MaxSteerDead);
            d.pedalDeadPct = Mathf.Clamp(d.pedalDeadPct, 0, MaxPedalDead);
            d.ffbStrengthPct = Mathf.Clamp(d.ffbStrengthPct, 0, 100);
            d.invertMask &= 7;
        }

        /// <summary>Write it out now. Flushed: on Web, PlayerPrefs live in
        /// IndexedDB and a closed tab is not a clean quit.</summary>
        public static void Save()
        {
            if (data == null) return;
            Sanitize(data);
            PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        /// <summary>Forget the cached copy (tests: what a fresh page load does).</summary>
        public static void ForgetCache() => data = null;

        /// <summary>Back to nothing set up (the self-test's clean slate).</summary>
        public static void ResetAll() { data = new WheelConfigData(); Save(); }

        /// <summary>Set up enough to drive: a steering axis and a throttle.</summary>
        public static bool Configured => Data.steer.Bound && Data.throttle.Bound;
        /// <summary>Configured and switched on.</summary>
        public static bool Active => Data.enabled && Configured;

        public static string RatioLabel => Data.ratioMode == RatioCar ? "1:1 CAR" : "FULL LOCK";

        // ---- the arithmetic (pure, for the self-test) -----------------------

        /// <summary>The steering axis as -1 (full left) .. 1 (full right) of the
        /// base's whole rotation, with a centre deadzone.</summary>
        public static float SteerNorm(WheelSteerBinding s, float raw, int deadPct)
        {
            float half = (s.right - s.left) * 0.5f;
            if (Mathf.Abs(half) < 1e-4f) return 0f;
            float centre = Mathf.Abs(s.centre - (s.left + s.right) * 0.5f) < Mathf.Abs(half) * 0.25f
                ? s.centre : (s.left + s.right) * 0.5f;
            float n;
            // Each side by its own reach, so a centre a hair off the middle of
            // the captured span still puts both stops at exactly -1 and 1.
            if ((raw - centre) * Mathf.Sign(half) >= 0f)
                n = (raw - centre) / Mathf.Max(1e-4f, Mathf.Abs(s.right - centre)) * Mathf.Sign(half);
            else
                n = (raw - centre) / Mathf.Max(1e-4f, Mathf.Abs(centre - s.left)) * Mathf.Sign(half);
            n = Mathf.Clamp(n, -1f, 1f);
            float d = Mathf.Clamp01(deadPct / 100f);
            if (d > 0f)
            {
                float m = Mathf.Abs(n);
                n = m <= d ? 0f : Mathf.Sign(n) * (m - d) / (1f - d);
            }
            return n;
        }

        /// <summary>Degrees the steering wheel itself is turned (+ = right).</summary>
        public static float WheelDegrees(float norm, int rotationDeg) => norm * rotationDeg * 0.5f;

        /// <summary>The road-wheel angle the car is asked for, in degrees.</summary>
        public static float RoadDegrees(float norm, int rotationDeg, int ratioMode, int linearityPct, float carLockDeg)
        {
            if (ratioMode == RatioCar)
                return WheelDegrees(norm, rotationDeg) * carLockDeg / CarLockAtWheelDeg;
            float e = Mathf.Max(1f, linearityPct / 100f);
            return Mathf.Sign(norm) * Mathf.Pow(Mathf.Abs(norm), e) * carLockDeg;
        }

        /// <summary>A pedal or lever, 0 at rest .. 1 fully pressed, with a
        /// deadzone at BOTH ends (a pedal that never quite reaches its stop
        /// still gives full throttle).</summary>
        public static float Pedal(WheelBinding b, float raw, int deadPct, bool invert)
        {
            float span = b.full - b.rest;
            if (Mathf.Abs(span) < 1e-4f) return 0f;
            float t = Mathf.Clamp01((raw - b.rest) / span);
            if (invert) t = 1f - t;
            float d = Mathf.Clamp(deadPct / 100f, 0f, 0.45f);
            if (t <= d) return 0f;
            if (t >= 1f - d) return 1f;
            return (t - d) / (1f - 2f * d);
        }

        /// <summary>A binding's 0..1 reading from the live pads (0 if its device is gone).</summary>
        public static float Read(WheelBinding b, int deadPct = 0, bool invert = false)
        {
            if (b == null || !b.Bound) return 0f;
            var pad = RawPads.Find(b.dev, b.ord);
            if (pad == null) return 0f;
            float raw = b.button ? pad.Button(b.index) : pad.Axis(b.index);
            if (b.button) return invert ? 1f - Mathf.Clamp01(raw) : Mathf.Clamp01(raw);
            return Pedal(b, raw, deadPct, invert);
        }

        public static bool Held(WheelBinding b) => Read(b) > 0.5f;

        /// <summary>The live steering axis, -1..1 of the rotation (null if unbound or gone).</summary>
        public static float? SteerNow()
        {
            var s = Data.steer;
            if (!s.Bound) return null;
            var pad = RawPads.Find(s.dev, s.ord);
            if (pad == null) return null;
            return SteerNorm(s, pad.Axis(s.index), Data.steerDeadPct);
        }

        public static bool Inverted(int bit) => (Data.invertMask & bit) != 0;
        public const int InvThrottle = 1, InvBrake = 2, InvClutch = 4;

        /// <summary>The INVERT row's states, in cycle order.</summary>
        public static readonly int[] InvertCycle = { 0, InvThrottle, InvBrake, InvClutch, 7 };
        public static string InvertLabel
        {
            get
            {
                switch (Data.invertMask)
                {
                    case 0: return "NONE";
                    case InvThrottle: return "THROTTLE";
                    case InvBrake: return "BRAKE";
                    case InvClutch: return "CLUTCH";
                    case 7: return "ALL";
                    default: return "MIXED";
                }
            }
        }
        public static void CycleInvert()
        {
            int i = Array.IndexOf(InvertCycle, Data.invertMask);
            Data.invertMask = InvertCycle[(i + 1 + InvertCycle.Length) % InvertCycle.Length];
            Save();
        }
    }
}
