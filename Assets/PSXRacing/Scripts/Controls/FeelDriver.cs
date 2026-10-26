using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Reads the player's car into a <see cref="FeelFrame"/> - the one place
    /// the rumble and the force feedback learn what the car is doing.
    ///   texture  the swing of the wheel loads (a kerb or a pothole moves a
    ///            wheel's load by a large share of its static weight in a tick
    ///            or two; body roll moves it over half a second), smoothed
    ///   hits     CollisionResponder's counted impacts on THIS car, with the
    ///            side they landed on
    ///   bottom   CarController.Bottomed (a bump stop struck)
    ///   SAT      the front tyres' lateral force and slip (FfbModel.SatOne)
    /// </summary>
    public sealed class CarFeel
    {
        CarController car;
        CollisionResponder responder;
        readonly float[] prevLoad = new float[4];
        bool primed;
        float tex;
        int prevGear;
        float pendingHit, pendingSide, pendingBottom;
        static CarFeel listening;

        public CarController Car => car;

        public void Bind(CarController c)
        {
            if (c == car) return;
            Unbind();
            car = c;
            if (car == null) return;
            responder = car.GetComponent<CollisionResponder>();
            car.Bottomed += OnBottomed;
            if (listening == null) { CollisionResponder.HitReportedOn += OnHit; }
            listening = this;
            primed = false;
            prevGear = car.currentGear;
        }

        public void Unbind()
        {
            if (car != null) car.Bottomed -= OnBottomed;
            if (listening == this) { CollisionResponder.HitReportedOn -= OnHit; listening = null; }
            car = null; responder = null;
            pendingHit = pendingBottom = 0f;
        }

        void OnBottomed(float closing, int wheel) => pendingBottom = Mathf.Max(pendingBottom, Mathf.Clamp01(closing / 2.5f));

        static void OnHit(CollisionResponder r, float speed, bool hard, Collider other)
        {
            var self = listening;
            if (self == null || self.car == null || r == null || r.gameObject != self.car.gameObject) return;
            float h = Mathf.Clamp01(speed / 10f);
            if (h <= self.pendingHit) return;
            self.pendingHit = h;
            float side = 0f;
            if (other != null)
            {
                var t = self.car.transform;
                Vector3 p = other.bounds.ClosestPoint(t.position);
                side = Mathf.Sign(t.InverseTransformPoint(p).x);
            }
            self.pendingSide = side;
        }

        public FeelFrame Sample(float dt)
        {
            var f = new FeelFrame();
            if (car == null) return f;
            f.speed = Mathf.Abs(car.forwardSpeed);
            float span = Mathf.Max(1f, car.redlineRPM - car.idleRPM);
            f.rpm01 = Mathf.Clamp01((car.currentRPM - car.idleRPM) / span);
            f.throttle = Mathf.Clamp01(car.throttleInput);
            f.limiter = car.RevLimiterActive;

            float staticLoad = Mathf.Max(100f, car.massKg * 9.81f * 0.25f);
            float swing = 0f, slide = 0f, frontSlide = 0f, sat = 0f;
            int grounded = 0, off = 0, fronts = 0;
            var wc = car.wheelContacts;
            for (int i = 0; i < 4; i++)
            {
                float load = wc[i].grounded ? wc[i].load : 0f;
                if (primed) swing += Mathf.Abs(load - prevLoad[i]) / staticLoad;
                prevLoad[i] = load;
                if (!wc[i].grounded) continue;
                grounded++;
                if (!wc[i].onRoad) off++;
                slide += wc[i].slide;
                if (i < 2)
                {
                    fronts++;
                    frontSlide += wc[i].slide;
                    sat += FfbModel.SatOne(wc[i].latForce, wc[i].slipAngle, wc[i].gripCircle);
                }
            }
            primed = true;
            float step = Mathf.Max(dt, Time.fixedDeltaTime);
            float rate = swing / Mathf.Max(1e-4f, step);
            float texNow = Mathf.Clamp01((rate - 4f) / 26f);
            tex = Mathf.Max(texNow, tex * Mathf.Exp(-dt / 0.06f));
            f.texture = tex;
            f.offroad = grounded > 0 ? off / (float)grounded : 0f;
            f.slide = Mathf.Clamp01(slide / 12f);
            f.frontSlide = Mathf.Clamp01(frontSlide / 5f);
            f.satNorm = fronts > 0 ? sat / 2f : 0f;
            f.steerDeg = car.SteerAngleDeg;

            f.hit = pendingHit; f.hitSide = pendingSide; pendingHit = 0f;
            f.bottom = pendingBottom; pendingBottom = 0f;
            f.shift = car.currentGear != prevGear && prevGear != 0 && car.currentGear != 0;
            prevGear = car.currentGear;
            return f;
        }
    }

    /// <summary>
    /// Runs the rumble and the force feedback for the player's car, one
    /// persistent object for the whole session (booted after the first
    /// scene, like AudioFader). Both go to ZERO - not just quiet - whenever
    /// the player is not driving: the pause menu, any front-end screen, on
    /// foot, a replay, a window that lost focus. A force left running on a
    /// wheel nobody is holding is the one thing this must never do.
    /// </summary>
    public sealed class FeelDriver : MonoBehaviour
    {
        static FeelDriver instance;
        readonly CarFeel feel = new CarFeel();
        readonly RumbleMixer mixer = new RumbleMixer();
        readonly FfbModel ffb = new FfbModel();
        float testT = -1f;
        bool focused = true;

        public const float TestSeconds = 0.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Get();
            // A wheel the player connected before comes back without a click.
            if (WheelConfig.Data.ffbOn) ForceFeedback.AutoConnect();
        }

        public static FeelDriver Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("FeelDriver");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            instance = go.AddComponent<FeelDriver>();
            return instance;
        }

        /// <summary>FORCE > TEST: a short left-right wiggle, from the menu.</summary>
        public static void StartTest() { Get().testT = 0f; }

        /// <summary>Is the player at the wheel of a car right now?</summary>
        public static bool Driving(PlayerCarInput pci)
        {
            if (pci == null || !pci.isActiveAndEnabled) return false;
            if (PauseMenu.IsOpen || FocusGuard.Frozen || Time.timeScale <= 0f) return false;
            if (RaceReplay.Playing) return false;
            if (OnFoot.ForecourtMode.OnFoot) return false;
            return true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (testT >= 0f)
            {
                testT += dt;
                if (testT > TestSeconds) { testT = -1f; ForceFeedback.Stop(); }
                else if (ForceFeedback.Current == ForceFeedback.State.Ready)
                {
                    float s = WheelConfig.Data.ffbStrengthPct / 100f;
                    float side = Mathf.Repeat(testT, 0.4f) < 0.2f ? 1f : -1f;
                    ForceFeedback.Send(new FfbOut { constant = 0.35f * side * s, damper = 0.1f, texPeriodMs = 50f },
                                       WheelConfig.Data.ffbInvert);
                }
                RumbleOut.Stop();
                return;
            }

            var pci = PlayerCarInput.Active;
            if (!focused || !Driving(pci))
            {
                Quiet();
                return;
            }
            feel.Bind(pci.Car);
            float gdt = Mathf.Max(1e-4f, Time.deltaTime);
            var f = feel.Sample(gdt);
            var r = mixer.Step(f, gdt) * RumblePrefs.Gain;
            RumbleOut.Set(r.x, r.y, dt);

            var d = WheelConfig.Data;
            if (d.ffbOn && ForceFeedback.Current == ForceFeedback.State.Ready)
                ForceFeedback.Send(ffb.Step(f, gdt, d.ffbStrengthPct / 100f), d.ffbInvert);
            else ForceFeedback.Stop();
        }

        void Quiet()
        {
            mixer.Reset();
            ffb.Reset();
            feel.Unbind();
            RumbleOut.Stop();
            ForceFeedback.Stop();
        }

        void OnApplicationFocus(bool has) { focused = has; if (!has) Quiet(); }
        void OnApplicationPause(bool paused) { if (paused) Quiet(); }
        void OnDisable() => Quiet();
    }
}
