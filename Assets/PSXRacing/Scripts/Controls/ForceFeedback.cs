using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace PSXRacing
{
    /// <summary>What the wheel is asked for this frame, in the GAME's sign:
    /// + pushes the rim to the right.</summary>
    public struct FfbOut
    {
        public float constant;      // -1..1
        public float spring;        // 0..1 centring stiffness
        public float damper;        // 0..1
        public float texAmp;        // 0..1 road texture (a sine on the rim)
        public float texPeriodMs;
        public static readonly FfbOut Zero = new FfbOut { texPeriodMs = 50f };
    }

    /// <summary>
    /// THE FORCE FEEDBACK MODEL, from the tyres (owner, 2026-10-26: "A WebHID
    /// force-feedback test on your base").
    ///
    ///   SELF-ALIGNING TORQUE  each front tyre's lateral force times its
    ///       trail. The PNEUMATIC trail shrinks as the tyre slips and is gone
    ///       by 0.24 rad, so the torque PEAKS at about 8 degrees of slip - a
    ///       little before the tyre's own grip peak at 0.17 rad - and then
    ///       LIGHTENS: the wheel going light is the warning that the front is
    ///       about to wash out, which is the whole point of feeling it. A
    ///       small mechanical trail (caster) stays, so a sliding front is
    ///       light but never dead.
    ///   SPRING   a gentle centre that firms with speed and softens while the
    ///       front slides (the device runs it, at its own kHz loop).
    ///   DAMPER   weight - heaviest parked, lighter with speed.
    ///   TEXTURE  kerbs, rough ground and the verge as a sine on the rim;
    ///            a bump stop struck is a short heavy burst.
    ///   JOLTS    an impact kicks the rim toward the side that was hit,
    ///            decaying in about a tenth of a second.
    /// Everything is clamped to -1..1 and multiplied by FFB STRENGTH. Pure:
    /// the self-test drives it with scripted frames.
    /// </summary>
    public sealed class FfbModel
    {
        public const float TrailGoneSlip = 0.24f, MechTrail = 0.15f;
        /// <summary>The peak of (lateral force / grip) x trail fraction over
        /// slip, for the game's tyre curve - SAT is normalised by it so 1.0 is
        /// the most a front tyre can push back at its load.</summary>
        public const float SatPeak = 0.415f;
        public const float SatGain = 0.75f;

        float joltEnv, joltSign = 1f, bumpEnv;

        public void Reset() { joltEnv = bumpEnv = 0f; }

        public static float TrailFrac(float slipRad) =>
            MechTrail + (1f - MechTrail) * Mathf.Max(0f, 1f - Mathf.Abs(slipRad) / TrailGoneSlip);

        /// <summary>One tyre's self-aligning torque, -1..1-ish, + = rim to the
        /// right. latForce is the force along the wheel's right (CarController
        /// sign), circle the tyre's grip at its load.</summary>
        public static float SatOne(float latForce, float slipRad, float circle)
        {
            if (circle < 1f) return 0f;
            return -latForce * TrailFrac(slipRad) / (circle * SatPeak);
        }

        public FfbOut Step(in FeelFrame f, float dt, float strength)
        {
            joltEnv *= Mathf.Exp(-dt / 0.10f);
            if (f.hit > joltEnv) { joltEnv = Mathf.Clamp01(f.hit); joltSign = f.hitSide >= 0f ? 1f : -1f; }
            bumpEnv = Mathf.Max(bumpEnv * Mathf.Exp(-dt / 0.08f), Mathf.Clamp01(f.bottom));

            float speedT = Mathf.Clamp01(f.speed / 30f);
            float sat = Mathf.Clamp(f.satNorm, -1.5f, 1.5f) * SatGain;
            float constant = sat + 0.6f * joltEnv * joltSign;
            float spring = (0.04f + 0.10f * speedT) * (1f - 0.7f * Mathf.Clamp01(f.frontSlide));
            float damper = Mathf.Lerp(0.30f, 0.12f, Mathf.Clamp01(f.speed / 20f));
            float tex = 0.45f * Mathf.Clamp01(f.texture) + 0.25f * Mathf.Clamp01(f.offroad) * Mathf.Clamp01(f.speed / 15f);
            tex = Mathf.Max(tex, 0.8f * bumpEnv);
            float s = Mathf.Clamp01(strength);
            return new FfbOut
            {
                constant = Mathf.Clamp(constant, -1f, 1f) * s,
                spring = Mathf.Clamp01(spring) * s,
                damper = Mathf.Clamp01(damper) * s,
                texAmp = Mathf.Clamp01(tex) * s,
                texPeriodMs = bumpEnv > 0.2f ? 25f : Mathf.Lerp(60f, 30f, speedT),
            };
        }
    }

    /// <summary>
    /// The wheel's motor, through PSXForceFeedback.jslib (WebHID + HID PID).
    /// Browser only, and only Chrome / Edge on a desktop: everywhere else
    /// <see cref="Supported"/> is false and the CONNECT row says so. The
    /// desktop build would need DirectInput, which is not wired.
    /// </summary>
    public static class ForceFeedback
    {
        public enum State { None = 0, Connecting = 1, Ready = 2, NoPid = 3, Error = 4, Refused = 5, Unsupported = 6 }
        const string AutoKey = "psx.ffbAuto";

        /// <summary>The last forces handed over (the self-test reads it).</summary>
        public static FfbOut LastSent { get; private set; } = FfbOut.Zero;
        public static bool Live { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PSXFFBSupported();
        [DllImport("__Internal")] static extern int PSXFFBConnect(int any);
        [DllImport("__Internal")] static extern int PSXFFBAuto();
        [DllImport("__Internal")] static extern int PSXFFBState();
        [DllImport("__Internal")] static extern string PSXFFBName();
        [DllImport("__Internal")] static extern void PSXFFBSet(float c, float k, float d, float pa, float pp);
        [DllImport("__Internal")] static extern void PSXFFBStop();
        static int supported = -1;
        public static bool Supported
        {
            get { if (supported < 0) { try { supported = PSXFFBSupported(); } catch { supported = 0; } } return supported == 1; }
        }
        public static State Current
        {
            get
            {
                if (!Supported) return State.Unsupported;
                int s; try { s = PSXFFBState(); } catch { s = 4; }
                if (s == 2 && PlayerPrefs.GetInt(AutoKey, 0) == 0) { PlayerPrefs.SetInt(AutoKey, 1); PlayerPrefs.Save(); }
                return (State)Mathf.Clamp(s, 0, 5);
            }
        }
        public static string Name { get { try { return PSXFFBName() ?? ""; } catch { return ""; } } }
        public static void Connect(bool any) { if (Supported) { try { PSXFFBConnect(any ? 1 : 0); } catch { } } }
        /// <summary>Reopen the wheel the player allowed last time (no click needed).</summary>
        public static void AutoConnect()
        {
            if (Supported && PlayerPrefs.GetInt(AutoKey, 0) == 1) { try { PSXFFBAuto(); } catch { } }
        }
        static void Push(FfbOut o, float deviceSign)
        {
            try { PSXFFBSet(o.constant * deviceSign, o.spring, o.damper, o.texAmp, o.texPeriodMs); } catch { }
        }
        static void PushStop() { try { PSXFFBStop(); } catch { } }
#else
        public static bool Supported => false;
        public static State Current => State.Unsupported;
        public static string Name => "";
        public static void Connect(bool any) { }
        public static void AutoConnect() { }
        static void Push(FfbOut o, float deviceSign) { }
        static void PushStop() { }
#endif

        /// <summary>
        /// Forces for this frame. The DEVICE's sign is DirectInput's: a
        /// positive force along X comes FROM +X and pushes the rim LEFT, so
        /// the game's "+ = right" goes out negated - and FFB INVERT flips it
        /// for a base that reads it the other way (the test page's "swap").
        /// </summary>
        public static void Send(FfbOut o, bool invert)
        {
            LastSent = o;
            Live = true;
            Push(o, invert ? 1f : -1f);
        }

        /// <summary>Every force off, now.</summary>
        public static void Stop()
        {
            LastSent = FfbOut.Zero;
            if (!Live) return;
            Live = false;
            PushStop();
        }

        public static string StatusLabel
        {
            get
            {
                switch (Current)
                {
                    case State.Ready: return RawPads.ShortName(Name, 14);
                    case State.Connecting: return "CONNECTING";
                    case State.NoPid: return "NO FFB ON IT";
                    case State.Error: return "FAILED";
                    case State.Refused: return "NOT PICKED";
                    case State.Unsupported: return "NOT HERE";
                }
                return "CONNECT";
            }
        }
    }
}
