using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PSXRacing
{
    /// <summary>RUMBLE on/off and its level (CONTROLS tab). On at 70% by default.</summary>
    public static class RumblePrefs
    {
        const string OnKey = "psx.rumble", LevelKey = "psx.rumbleLevel";
        public const int DefaultLevel = 70, StepPercent = 10;
        static int on = -1, level = -1;

        public static bool On
        {
            get { if (on < 0) on = PlayerPrefs.GetInt(OnKey, 1) != 0 ? 1 : 0; return on == 1; }
            set { on = value ? 1 : 0; PlayerPrefs.SetInt(OnKey, on); PlayerPrefs.Save(); }
        }

        public static int Level
        {
            get { if (level < 0) level = Clamp(PlayerPrefs.GetInt(LevelKey, DefaultLevel)); return level; }
            set { level = Clamp(value); PlayerPrefs.SetInt(LevelKey, level); PlayerPrefs.Save(); }
        }

        static int Clamp(int v) => Mathf.Clamp(Mathf.RoundToInt(v / (float)StepPercent) * StepPercent, 0, 100);
        public static string Label => On ? "ON" : "OFF";
        public static void Toggle() => On = !On;
        /// <summary>What the mixer's output is multiplied by.</summary>
        public static float Gain => On ? Level / 100f : 0f;
        public static void ForgetCache() { on = -1; level = -1; }
    }

    /// <summary>
    /// One frame of what the car is doing, as the rumble and the force
    /// feedback both read it (<see cref="CarFeel"/> fills it from the player's
    /// car; the self-test writes one by hand).
    /// </summary>
    public struct FeelFrame
    {
        public float speed;          // m/s, unsigned
        public float rpm01;          // idle .. redline
        public float throttle;       // 0..1
        public bool limiter;
        /// <summary>0..1: how hard the wheels are being kicked up and down -
        /// kerbs, rumble strips, potholes, a rough verge (from the load swing).</summary>
        public float texture;
        /// <summary>0..1 of the grounded wheels off the tarmac.</summary>
        public float offroad;
        /// <summary>0..1: rubber sliding over the road, all four wheels.</summary>
        public float slide;
        /// <summary>0..1: the FRONT tyres past their grip.</summary>
        public float frontSlide;
        // Events since the last frame (0 = none):
        public float hit;            // 0..1 impact size
        public float hitSide;        // -1 left .. 1 right, where it struck
        public float bottom;         // 0..1 a bump stop struck
        public bool shift;           // a gear change
        // Steering (the force feedback's):
        public float satNorm;        // -1..1 self-aligning torque (+ = pushes the rim right)
        public float steerDeg;       // road-wheel angle
    }

    /// <summary>
    /// THE RUMBLE MIXER (owner, 2026-10-26: "Rumble (small)"). A sum of small
    /// effects, each with its own motor - the LOW motor (strong, heavy) for
    /// blows and big textures, the HIGH motor (weak, fine) for buzz - and the
    /// sum softly saturated so a hit during a slide still reads as a hit:
    ///   engine      a faint high buzz with revs and throttle; the limiter adds both
    ///   road        kerbs and rough ground (FeelFrame.texture), a verge buzz off the tarmac
    ///   slide       tyres sliding or spinning: the high motor
    ///   hit         a blow on the low motor, decaying over ~0.15 s
    ///   bottoming   a short heavy thump
    ///   gear shift  a 90 ms tick on the low motor
    /// Pure: no device, no Unity state - the self-test feeds it scripted frames.
    /// </summary>
    public sealed class RumbleMixer
    {
        float hitEnv, bottomEnv, shiftT;
        public const float ShiftPulse = 0.09f;

        public void Reset() { hitEnv = bottomEnv = shiftT = 0f; }

        /// <summary>Advance by dt and return (low, high) motor speeds 0..1,
        /// BEFORE the player's level.</summary>
        public Vector2 Step(in FeelFrame f, float dt)
        {
            hitEnv = Mathf.Max(hitEnv * Mathf.Exp(-dt / 0.15f), Mathf.Clamp01(f.hit));
            bottomEnv = Mathf.Max(bottomEnv * Mathf.Exp(-dt / 0.08f), Mathf.Clamp01(f.bottom));
            if (f.shift) shiftT = ShiftPulse; else shiftT = Mathf.Max(0f, shiftT - dt);

            float speedT = Mathf.Clamp01(f.speed / 40f);
            float low = 0f, high = 0f;
            // engine
            high += 0.08f * Mathf.Clamp01(f.rpm01) * Mathf.Clamp01(f.throttle);
            if (f.limiter) { low += 0.18f; high += 0.22f; }
            // road
            low += 0.55f * Mathf.Clamp01(f.texture) * Mathf.Lerp(0.5f, 1f, speedT);
            high += 0.35f * Mathf.Clamp01(f.texture);
            float verge = Mathf.Clamp01(f.offroad) * Mathf.Clamp01(f.speed / 15f);
            low += 0.18f * verge; high += 0.22f * verge;
            // slide
            high += 0.30f * Mathf.Clamp01(f.slide);
            low += 0.08f * Mathf.Clamp01(f.slide);
            // events
            low += 0.95f * hitEnv; high += 0.45f * hitEnv;
            low += 0.70f * bottomEnv; high += 0.20f * bottomEnv;
            if (shiftT > 0f) low += 0.35f;
            return new Vector2(Soft(low), Soft(high));
        }

        /// <summary>Linear to 0.6, then easing into 1 - two effects never clip
        /// to a flat 100% that hides which of them is which.</summary>
        static float Soft(float x)
        {
            if (x <= 0.6f) return Mathf.Max(0f, x);
            return 0.6f + 0.4f * (1f - Mathf.Exp(-(x - 0.6f) / 0.4f));
        }
    }

    /// <summary>
    /// The pad's motors. WebGL: PSXGamepadIO.jslib's vibrationActuator (Unity's
    /// WebGL input does not drive rumble); everywhere else the Input System's
    /// <c>Gamepad.SetMotorSpeeds</c>. Refreshed 20 times a second with an
    /// effect a little longer than that, so a held rumble is seamless and a
    /// stopped game lets go in a tenth of a second whatever happens.
    /// </summary>
    public static class RumbleOut
    {
        public const float RefreshS = 0.05f, EffectMs = 110f;
        static float lastLow = -1f, lastHigh = -1f, sinceSend;
        /// <summary>The last values sent (the self-test reads these).</summary>
        public static Vector2 Last { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PSXRumble(string id, float strong, float weak, float ms);
        [DllImport("__Internal")] static extern void PSXRumbleStopAll();
#endif

        public static void Set(float low, float high, float dt)
        {
            low = Mathf.Clamp01(low); high = Mathf.Clamp01(high);
            if (low < 0.02f) low = 0f;
            if (high < 0.02f) high = 0f;
            sinceSend += dt;
            bool zero = low <= 0f && high <= 0f;
            bool wasZero = lastLow <= 0f && lastHigh <= 0f;
            if (zero && wasZero) return;
            bool changed = Mathf.Abs(low - lastLow) > 0.03f || Mathf.Abs(high - lastHigh) > 0.03f;
            // A change goes out within a frame or two (at most 30 a second);
            // a steady value is refreshed before its effect runs out.
            if (!zero && sinceSend < (changed ? 0.033f : RefreshS)) return;
            sinceSend = 0f;
            lastLow = low; lastHigh = high;
            Last = new Vector2(low, high);
            var pad = Gamepad.current;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { PSXRumble(pad != null ? (pad.description.product ?? "") : "", low, high, EffectMs); } catch { }
#else
            if (pad != null) pad.SetMotorSpeeds(low, high);
#endif
        }

        /// <summary>Both motors off at once (pause, a menu, focus loss).</summary>
        public static void Stop()
        {
            bool was = lastLow > 0f || lastHigh > 0f;
            lastLow = lastHigh = 0f;
            Last = Vector2.zero;
            if (!was) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { PSXRumbleStopAll(); } catch { }
#else
            var pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
#endif
        }
    }
}
