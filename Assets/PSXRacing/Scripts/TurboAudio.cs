using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Forced-induction voice, using the recorded Turbo Sound Pack takes.
    /// Ported from Racing Game 2's forcedInduction.ts (recorded-sample path).
    ///
    /// Turbo — three layers:
    ///  - a spool loop, volume rising with RPM and pitch tracking it 0.5x -> 1.5x
    ///  - a max-boost loop crossfaded in on the physics rev-limiter flag
    ///  - blow-off shots fired on a throttle release that had real boost behind it
    ///
    /// Supercharger — one layer: a belt-driven whine tied straight to RPM, with
    /// no spool lag and no blow-off, because a positive-displacement blower has
    /// neither. Four of the 317 catalog cars are supercharged.
    ///
    /// Naturally aspirated — nothing. 176 of the 317 are NA, and the single
    /// worst thing this component can do is put a turbo on all of them, which is
    /// what it did while the built-in RX-7 was the only car in the game.
    ///
    /// The boost value is a proxy, not a manifold model: a first-order lag that
    /// spools up slowly and collapses roughly four times faster, which is what
    /// makes lifting mid-corner sound like a dump rather than a fade.
    /// </summary>
    public class TurboAudio : MonoBehaviour
    {
        public enum Aspiration { NaturallyAspirated, Turbo, Supercharger }

        public CarController car;
        /// <summary>Which voice to run. Set per car by RaceHandoffApplier from
        /// the catalog's GT4 aspiration field.</summary>
        public Aspiration aspiration = Aspiration.Turbo;

        public AudioClip spoolClip;
        public AudioClip maxLoopClip;
        public AudioClip superchargerOnClip;
        public AudioClip superchargerOffClip;
        public AudioClip[] blowOffLong;
        public AudioClip[] blowOffShort;

        [Range(0f, 2f)] public float masterVolume = 1f;
        public bool spatial;

        // ---- constants from forcedInduction.ts ----
        const float SpoolRpmFloor = 0.22f;   // below this there is no exhaust flow
        const float SpoolUpRate = 2.8f;      // ~0.36 s to 63%
        const float SpoolDownRate = 9.0f;    // charge dumps ~4x faster than it builds
        const float BovGasWas = 0.45f;       // throttle must fall THROUGH this...
        const float BovGasNow = 0.20f;       // ...to below this, in one frame
        const float BovMinBoost = 0.30f;
        const float BovCooldown = 0.6f;      // so pedal flutter cannot machine-gun it
        const float LongShotThreshold = 0.8f;
        const float ShotVolLow = 0.50f;      // a lift at low revs is silent
        const float ShotVolFull = 0.90f;
        const float LoopVolStart = 0.40f;    // spool silent below this rpmNorm
        const float LoopVolPeak = 0.353f;
        const float LoopVolume = 2.25f;      // vendor authored for 3D rolloff; we are 2D
        const float TurboMaster = 0.5f;
        const float LimiterBlend = 0.03f;
        const float GainTC = 0.05f;

        AudioSource spoolSrc, maxSrc, shotSrc, blowerOnSrc, blowerOffSrc;
        float boost;
        float prevThrottle;
        float bovCooldown;
        float spoolGain, maxGain, blowerGain;
        int shotIndex;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            BuildVoice();
        }

        /// <summary>
        /// Point this at a different aspiration and rebuild. Called once by
        /// RaceHandoffApplier before the lights, so the teardown never happens
        /// mid-race; no-ops when nothing changed.
        /// </summary>
        public void SetAspiration(Aspiration a)
        {
            if (a == aspiration && built) return;
            aspiration = a;
            BuildVoice();
        }

        bool built;

        void BuildVoice()
        {
            if (spoolSrc != null) Destroy(spoolSrc.gameObject);
            if (maxSrc != null) Destroy(maxSrc.gameObject);
            if (blowerOnSrc != null) Destroy(blowerOnSrc.gameObject);
            if (blowerOffSrc != null) Destroy(blowerOffSrc.gameObject);
            spoolSrc = maxSrc = blowerOnSrc = blowerOffSrc = null;
            spoolGain = maxGain = blowerGain = 0f;
            boost = 0f;
            built = true;

            if (aspiration == Aspiration.Turbo)
            {
                spoolSrc = MakeLoop(spoolClip, "turbo_spool");
                maxSrc = MakeLoop(maxLoopClip, "turbo_max");
            }
            else if (aspiration == Aspiration.Supercharger)
            {
                blowerOnSrc = MakeLoop(superchargerOnClip, "blower_on");
                blowerOffSrc = MakeLoop(superchargerOffClip, "blower_off");
            }

            // The one-shot source is kept for every aspiration — it costs one
            // idle voice, and rebuilding it would drop a blow-off already in
            // flight. FireBlowOff is gated on the mode instead.
            if (shotSrc == null)
            {
                var go = new GameObject("snd_turbo_bov");
                go.transform.SetParent(transform, false);
                shotSrc = go.AddComponent<AudioSource>();
                shotSrc.playOnAwake = false;
                shotSrc.loop = false;
                Configure(shotSrc);
            }
        }

        void OnEnable()
        {
            if (car == null) car = GetComponent<CarController>();
            if (car != null) car.Upshifted += PlayShiftFlutter;
        }

        void OnDisable()
        {
            if (car != null) car.Upshifted -= PlayShiftFlutter;
        }

        AudioSource MakeLoop(AudioClip clip, string label)
        {
            if (clip == null) return null;
            var go = new GameObject("snd_" + label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            Configure(src);
            AudioLoopStarter.PlayLoop(src);
            return src;
        }

        void Configure(AudioSource src)
        {
            src.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 70f;
                src.minDistance = 5f;
            }
        }

        void Update()
        {
            if (car == null || aspiration == Aspiration.NaturallyAspirated) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float rpmNorm = Mathf.Clamp01(
                (car.currentRPM - car.idleRPM) / Mathf.Max(car.revLimitRPM - car.idleRPM, 1f));
            float throttle = Mathf.Clamp01(car.throttleInput);

            if (aspiration == Aspiration.Supercharger) { UpdateBlower(rpmNorm, throttle, dt); return; }

            // --- boost proxy: asymmetric first-order lag ---
            float flow = Mathf.Clamp01((rpmNorm - SpoolRpmFloor) / 0.5f);
            float target = throttle > 0.1f ? flow : 0f;
            float rate = target > boost ? SpoolUpRate : SpoolDownRate;
            boost = Mathf.MoveTowards(boost, target, rate * dt);

            // --- blow-off: the pedal must fall THROUGH the window in one frame ---
            bovCooldown = Mathf.Max(0f, bovCooldown - dt);
            if (prevThrottle > BovGasWas && throttle < BovGasNow &&
                boost > BovMinBoost && bovCooldown <= 0f)
            {
                FireBlowOff(rpmNorm);
                bovCooldown = BovCooldown;
            }
            prevThrottle = throttle;

            // --- spool loop ---
            float loopVol = rpmNorm <= LoopVolStart ? 0f
                : Mathf.SmoothStep(0f, LoopVolPeak, (rpmNorm - LoopVolStart) / (1f - LoopVolStart));
            float spoolTarget = loopVol * LoopVolume * TurboMaster * masterVolume * Mathf.Max(0.25f, boost);
            spoolGain = Smooth(spoolGain, spoolTarget, GainTC, dt);
            if (spoolSrc != null)
            {
                spoolSrc.volume = spoolGain;
                spoolSrc.pitch = Mathf.Lerp(0.5f, 1.5f, rpmNorm);
            }

            // --- max-boost loop, gated on the physics limiter flag ---
            float maxTarget = car.RevLimiterActive
                ? LoopVolPeak * LoopVolume * TurboMaster * masterVolume : 0f;
            maxGain = Smooth(maxGain, maxTarget, LimiterBlend + GainTC, dt);
            if (maxSrc != null) { maxSrc.volume = maxGain; maxSrc.pitch = 1f; }
        }

        /// <summary>
        /// Belt-driven whine: gain and pitch ride RPM directly, with no lag
        /// term at all. That is the whole character difference — a blower is
        /// making boost the instant the crank turns, so there is nothing to
        /// spool and nothing to dump on a lift. Load only crossfades between the
        /// on- and off-throttle takes.
        ///
        /// The take is a single mid-range rung, so the pitch span is kept
        /// narrower than the turbo's 0.5-1.5: past about ±35% a stretched whine
        /// stops sounding like gears and starts sounding like a sample.
        /// </summary>
        void UpdateBlower(float rpmNorm, float throttle, float dt)
        {
            float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.55f, rpmNorm))
                           * LoopVolPeak * LoopVolume * TurboMaster * masterVolume;
            blowerGain = Smooth(blowerGain, target, GainTC, dt);
            float pitch = Mathf.Lerp(0.72f, 1.35f, rpmNorm);
            if (blowerOnSrc != null)
            {
                blowerOnSrc.volume = blowerGain * throttle;
                blowerOnSrc.pitch = pitch;
            }
            if (blowerOffSrc != null)
            {
                blowerOffSrc.volume = blowerGain * (1f - throttle) * 0.8f;
                blowerOffSrc.pitch = pitch;
            }
        }

        void FireBlowOff(float rpmNorm)
        {
            var pool = rpmNorm > LongShotThreshold ? blowOffLong : blowOffShort;
            if (pool == null || pool.Length == 0 || shotSrc == null) return;

            // A lift at low revs has nothing to dump, so it stays silent.
            float vol = Mathf.Clamp01((rpmNorm - ShotVolLow) / (ShotVolFull - ShotVolLow));
            if (vol <= 0.01f) return;

            shotIndex = (shotIndex + 1) % pool.Length;   // cycle so it never repeats back to back
            shotSrc.pitch = Random.Range(0.94f, 1.06f);
            AudioLoopStarter.PlayOneShot(shotSrc, pool[shotIndex],
                                         vol * TurboMaster * masterVolume * 1.6f);
        }

        /// <summary>Upshift flutter: a partial dump at 55%, since the throttle is
        /// only closed for the length of the shift.</summary>
        public void PlayShiftFlutter(float rpmNorm)
        {
            if (aspiration != Aspiration.Turbo) return;   // nothing to flutter
            if (bovCooldown > 0.38f) return;   // a lift-and-shift must not double-psshh
            var pool = blowOffShort;
            if (pool == null || pool.Length == 0 || shotSrc == null) return;
            float vol = Mathf.Clamp01((rpmNorm - ShotVolLow) / (ShotVolFull - ShotVolLow)) * 0.55f;
            if (vol <= 0.01f) return;
            shotIndex = (shotIndex + 1) % pool.Length;
            shotSrc.pitch = Random.Range(0.96f, 1.08f);
            AudioLoopStarter.PlayOneShot(shotSrc, pool[shotIndex],
                                         vol * TurboMaster * masterVolume * 1.6f);
            bovCooldown = 0.22f;
        }

        static float Smooth(float current, float target, float tc, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(tc, 1e-4f)));
    }
}
