using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE RAIN'S SOUND (RAIN 2, 2026-11-16). The game had none: WeatherFx's
    /// note reads "there is no rain loop in the pack yet, and a silent rain
    /// beats a wrong one". These four are made in code, once, the first time
    /// it rains (a few short mono clips at 22 kHz; nothing is loaded, nothing
    /// is allocated after that):
    /// <list type="bullet">
    /// <item>RAIN ON THE ROAD - a broad hiss with a slow flutter in it: what
    /// the chase camera, a replay lens and a driver on foot hear;</item>
    /// <item>RAIN ON THE ROOF AND GLASS - the same shower heard from inside:
    /// duller, with the drumming of single drops on sheet metal; the cockpit
    /// view's;</item>
    /// <item>TYRE HISS - the player's tyres on wet tarmac, louder and higher
    /// with speed, by how wet the road under them is (so it carries on over
    /// a wet road after the rain has stopped);</item>
    /// <item>THE PUDDLE - one short wash when a tyre of the player's car
    /// hits standing water (TireSpray calls <see cref="PuddleHit"/>).</item>
    /// </list>
    /// Under a roof or in a tunnel the shower drops to a murmur; the menu and
    /// the pause screen mute it like the wind (AudioPrefs.Effects scales it).
    /// </summary>
    public class Rain2Audio : MonoBehaviour
    {
        const int Rate = 22050;
        /// <summary>The shower's loudness outside and (duller) in the cabin; the roof's drumming; the tyres' hiss at speed.</summary>
        public const float RoadOutside = 0.30f, RoadInCabin = 0.07f, RoofInCabin = 0.34f, HissMax = 0.26f, HitMax = 0.50f;
        /// <summary>Under cover the shower is this much of itself.</summary>
        public const float CoveredMul = 0.30f;
        const float GainTau = 0.25f;

        static Rain2Audio instance;
        AudioSource road, roof, hiss, hit;
        bool built;
        float gRoad, gRoof, gHiss, hissPitch = 1f;
        float hitCooldown;
        CarController car;
        Transform carOf;

        /// <summary>For the checks: the three beds' live gains.</summary>
        public static Vector3 Gains => instance == null ? Vector3.zero : new Vector3(instance.gRoad, instance.gRoof, instance.gHiss);

        void Awake() { instance = this; }
        void OnDestroy()
        {
            if (instance == this) instance = null;
            // the clips were made here: they go with it
            foreach (var s in new[] { road, roof, hiss, hit })
                if (s != null && s.clip != null) Destroy(s.clip);
        }

        /// <summary>A tyre of the player's car has hit standing water;
        /// <paramref name="strength"/> 0..1 by depth and speed.</summary>
        public static void PuddleHit(float strength)
        {
            var a = instance;
            if (a == null || !a.built || a.hit == null || a.hitCooldown > 0f || !Rain2.On || !Rain2.Audio) return;
            if (PauseMenu.IsOpen) return;
            a.hitCooldown = 0.22f;
            a.hit.pitch = Random.Range(0.88f, 1.12f);
            a.hit.PlayOneShot(a.hit.clip, HitMax * Mathf.Clamp01(strength) * AudioPrefs.Effects
                                          * (ChaseCamera.Current == ChaseCamera.View.Cockpit ? 0.7f : 1f));
        }

        void Update()
        {
            if (!Rain2.Measure) { Step(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), a0 = Rain2.AllocNow();
            Step();
            Rain2.AudioTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            Rain2.AudioAlloc += Rain2.AllocSince(a0);
        }

        void Step()
        {
            float dt = Time.unscaledDeltaTime;
            if (hitCooldown > 0f) hitCooldown -= dt;
            float rain = Rain2.On && Rain2.Audio ? Rain2.RainNow : 0f;

            // the player's car, for the hiss and the cabin
            var chase = ChaseCamera.Active;
            if (chase != null && chase.target != carOf)
            {
                carOf = chase.target;
                car = carOf != null ? carOf.GetComponent<CarController>() : null;
            }
            else if (chase == null) { car = null; carOf = null; }

            float wet = 0f, v = 0f;
            if (car != null && Rain2.On && Rain2.Audio)
            {
                var c = car.wheelContacts;
                if (c != null && c.Length >= 4)
                {
                    for (int i = 0; i < 4; i++) if (c[i].grounded && c[i].onRoad) wet += c[i].wetT * 0.25f;
                }
                v = Mathf.Abs(car.speedKmh) / 3.6f;
            }

            if (!built)
            {
                if (rain <= 0.01f && wet <= 0.05f) return;
                Build();
            }

            float mul = AudioPrefs.Effects;
            if (PauseMenu.IsOpen) mul = 0f;
            bool onFoot = OnFoot.ForecourtMode.OnFoot;
            bool cabin = !onFoot && car != null && ChaseCamera.Current == ChaseCamera.View.Cockpit
                         && EngineAudio.EarFor(car) != EngineAudio.Ear.Outside;
            float open = WeatherFx.Covered ? CoveredMul : 1f;

            float roadT = rain * (cabin ? RoadInCabin : RoadOutside) * open * mul;
            float roofT = cabin ? rain * RoofInCabin * (WeatherFx.Covered ? 0f : 1f) * mul : 0f;
            float hissT = 0f;
            if (car != null && !onFoot && EngineAudio.EarFor(car) != EngineAudio.Ear.Outside)
                hissT = Mathf.Clamp01(wet) * HissMax * Mathf.Pow(Mathf.Clamp01((v - 2f) / 45f), 0.8f) * (cabin ? 0.6f : 1f) * mul;

            gRoad = Smooth(gRoad, roadT, GainTau, dt);
            gRoof = Smooth(gRoof, roofT, GainTau, dt);
            gHiss = Smooth(gHiss, hissT, 0.12f, dt);
            hissPitch = Smooth(hissPitch, 0.82f + 0.45f * Mathf.Clamp01(v / 60f), 0.12f, dt);
            Apply(road, gRoad, 1f);
            Apply(roof, gRoof, 1f);
            Apply(hiss, gHiss, hissPitch);
        }

        static void Apply(AudioSource src, float volume, float pitch)
        {
            if (src == null) return;
            if (volume < 0.0005f)
            {
                if (src.volume != 0f) src.volume = 0f;
                return;
            }
            src.volume = volume;
            src.pitch = pitch;
        }

        static float Smooth(float current, float target, float tc, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(tc, 1e-4f)));

        // ------------------------------------------------------------------
        //  the clips
        // ------------------------------------------------------------------
        void Build()
        {
            built = true;
            road = MakeLoop(RoadClip(), "rain_road");
            roof = MakeLoop(RoofClip(), "rain_roof");
            hiss = MakeLoop(HissClip(), "tyre_hiss");
            var go = new GameObject("snd_puddle");
            go.transform.SetParent(transform, false);
            hit = go.AddComponent<AudioSource>();
            hit.clip = HitClip();
            hit.loop = false;
            hit.playOnAwake = false;
            hit.spatialBlend = 0f;
            hit.bypassListenerEffects = true;
            hit.dopplerLevel = 0f;
        }

        AudioSource MakeLoop(AudioClip clip, string label)
        {
            var go = new GameObject("snd_" + label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.bypassListenerEffects = true;
            src.dopplerLevel = 0f;
            AudioLoopStarter.PlayLoop(src);
            return src;
        }

        // a small deterministic generator: the same shower every run
        struct Rng
        {
            uint s;
            public Rng(uint seed) { s = seed == 0 ? 1u : seed; }
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777215f; }
            public float Signed() => Next() * 2f - 1f;
        }

        /// <summary>Wrap <paramref name="gen"/> (n + fade samples) into a loop
        /// of n: its tail is blended over its head, so the end runs on into
        /// the start.</summary>
        static AudioClip Loop(string name, float[] gen, int n, int fade, float peak)
        {
            var o = new float[n];
            float max = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float x = gen[i];
                if (i < fade) { float t = i / (float)fade; x = x * t + gen[n + i] * (1f - t); }
                o[i] = x;
                float a = x < 0f ? -x : x;
                if (a > max) max = a;
            }
            float g = peak / max;
            for (int i = 0; i < n; i++) o[i] *= g;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(o, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        static float Alpha(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);

        /// <summary>Rain on tarmac: white noise between about 900 Hz and
        /// 6 kHz, breathing a little, with a fine crackle of nearer drops.</summary>
        static AudioClip RoadClip()
        {
            int n = Rate * 3, fade = Rate / 4;
            var g = new float[n + fade];
            var r = new Rng(0x2A11u);
            float lp = 0f, hp = 0f, aL = Alpha(6000f), aH = Alpha(900f), flut = 0f;
            for (int i = 0; i < g.Length; i++)
            {
                float w = r.Signed();
                lp += (w - lp) * aL;
                hp += (lp - hp) * aH;
                float band = lp - hp;
                flut += (r.Signed() - flut) * 0.0004f;
                float tick = r.Next() < 0.004f ? r.Signed() * 1.6f : 0f;
                g[i] = band * (1f + flut * 6f) + tick * 0.25f;
            }
            return Loop("rain_road", g, n, fade, 0.85f);
        }

        /// <summary>The shower from inside a car: a dull bed, and single drops
        /// drumming on the roof - impulses through two resonances of thin
        /// sheet metal.</summary>
        static AudioClip RoofClip()
        {
            int n = Rate * 3, fade = Rate / 4;
            var g = new float[n + fade];
            var r = new Rng(0x7003u);
            float lp = 0f, aL = Alpha(1100f);
            // two resonators (b0 x, a1 y1, a2 y2): 310 Hz and 820 Hz
            float c1 = 2f * 0.9965f * Mathf.Cos(2f * Mathf.PI * 310f / Rate), d1 = 0.9965f * 0.9965f;
            float c2 = 2f * 0.9930f * Mathf.Cos(2f * Mathf.PI * 820f / Rate), d2 = 0.9930f * 0.9930f;
            float y1a = 0f, y1b = 0f, y2a = 0f, y2b = 0f;
            for (int i = 0; i < g.Length; i++)
            {
                float w = r.Signed();
                lp += (w - lp) * aL;
                float imp = r.Next() < 0.010f ? r.Signed() * (0.4f + 0.6f * r.Next()) : 0f;
                float y1 = imp + c1 * y1a - d1 * y1b; y1b = y1a; y1a = y1;
                float y2 = imp * 0.6f + c2 * y2a - d2 * y2b; y2b = y2a; y2a = y2;
                g[i] = lp * 1.1f + y1 * 0.055f + y2 * 0.035f;
            }
            return Loop("rain_roof", g, n, fade, 0.85f);
        }

        /// <summary>Tyres on wet tarmac: a smooth band of noise, 1.5 - 7 kHz.</summary>
        static AudioClip HissClip()
        {
            int n = Rate * 2, fade = Rate / 5;
            var g = new float[n + fade];
            var r = new Rng(0x5155u);
            float lp = 0f, hp = 0f, aL = Alpha(7000f), aH = Alpha(1500f);
            for (int i = 0; i < g.Length; i++)
            {
                float w = r.Signed();
                lp += (w - lp) * aL;
                hp += (lp - hp) * aH;
                g[i] = lp - hp;
            }
            return Loop("tyre_hiss", g, n, fade, 0.8f);
        }

        /// <summary>A tyre into standing water: a burst that opens bright and
        /// closes dull as the sheet falls back, about half a second.</summary>
        static AudioClip HitClip()
        {
            int n = (int)(Rate * 0.55f);
            var o = new float[n];
            var r = new Rng(0x9D17u);
            float lp = 0f, max = 1e-6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.006f) * Mathf.Exp(-t * 8.5f);
                float cut = Mathf.Lerp(700f, 5200f, Mathf.Exp(-t * 7f));
                lp += (r.Signed() - lp) * Alpha(cut);
                o[i] = lp * env;
                float a = o[i] < 0f ? -o[i] : o[i];
                if (a > max) max = a;
            }
            float g = 0.9f / max;
            for (int i = 0; i < n; i++) o[i] *= g;
            var clip = AudioClip.Create("puddle_hit", n, 1, Rate, false);
            clip.SetData(o, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }
    }
}
