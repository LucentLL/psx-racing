using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The water a car's rear tyres throw up on a wet road: a low, growing
    /// mist that trails behind each rear wheel once the car is moving.
    ///
    /// Part of the 2026-09-21 NFS (2015) night pass. The owner's ask was the
    /// reference's "particle effects on screen for rain and light", and the
    /// single most recognisable piece of it on a wet night is the spray: a
    /// car ahead is a pair of red tail lamps inside a red haze of its own
    /// rooster tail, and a car behind pushes a white cloud through its own
    /// headlights. That only works because the spray is drawn with PSX/Rain,
    /// which lights every particle from the street-lamp table (tail lamps are
    /// in it as Point lights), the headlight table and the scene's ambient -
    /// so the SAME mist is grey by day, red behind a braking car and white in
    /// a following car's beams, with no colour chosen here at all.
    ///
    /// Added at runtime by <see cref="WeatherFx"/> to every
    /// <see cref="CarController"/> in the scene when the day's weather is
    /// Rain (and to cars that appear later: it polls). Nothing is baked, so a
    /// dry day costs nothing, and no scene rebuild is needed to ship it.
    ///
    /// A ParticleSystem, unlike <see cref="TireSmoke"/>'s hand-rolled pool,
    /// because the reasons TireSmoke gives against one are answered by the
    /// shader: PSX/Rain reads the scene fog and the snap grid itself, and the
    /// particles are still born at the contact patch the physics computed
    /// (explicit <c>Emit</c> calls, no emitter shape). What the system adds is
    /// the integration, the billboarding and the culling for free.
    ///
    /// TRAPS it avoids:
    /// <list type="bullet">
    /// <item>The particle object is NOT a child of the car. Code all over the
    /// game walks a car's child renderers - paint swaps, bounds for framing,
    /// the collider fit - and a ten-metre world-space trail inside that
    /// hierarchy would be measured as part of the car. It lives at the scene
    /// root and is destroyed with this component.</item>
    /// <item>Replay makes every body kinematic, so <c>Body.linearVelocity</c>
    /// reads zero and the mist would be left hanging in place behind a car
    /// doing 200. The inherited velocity comes from <c>forwardSpeed</c>
    /// whenever the body is kinematic (RaceReplay writes it every frame, and
    /// the wheel contacts too).</item>
    /// <item>The system is built on the first frame that actually sprays: a
    /// town full of parked CarControllers on a rainy day gets one idle
    /// component each, not one idle ParticleSystem each.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public class TireSpray : MonoBehaviour
    {
        public CarController car;

        /// <summary>Road speed (km/h) where the spray starts, and where it
        /// reaches full rate. Below 30 a tyre just sheds water off its
        /// tread; the spray is ramped in from zero there so it never pops
        /// on.</summary>
        public const float SpeedStart = 30f;
        public const float SpeedFull = 200f;
        /// <summary>Particles per second per rear wheel at <see cref="SpeedFull"/>
        /// and above, falling linearly to zero at <see cref="SpeedStart"/>.
        /// At 60 and a 0.55 s life a wheel holds about 33 live puffs at
        /// full speed - enough to overlap into one cloud, few enough that
        /// eight cars cost well under a thousand particles.</summary>
        public const float RateAtFull = 60f;
        /// <summary>Seconds a puff lives. Short on purpose: spray hangs for a
        /// car length or two at racing speed, not for the whole straight.</summary>
        public const float Life = 0.55f;
        /// <summary>Puff size (m) at birth and at death. Born tyre-sized at the
        /// contact patch, dies as a cloud about as tall as the car's roof.</summary>
        public const float SizeStart = 0.35f, SizeEnd = 1.8f;
        /// <summary>Fraction of the car's velocity a puff is born with. The
        /// mist is dragged along a little by the car's wake but is mostly
        /// LEFT BEHIND, which is what draws it out into a trail.</summary>
        public const float Inherit = 0.35f;
        /// <summary>Peak opacity of one puff. PSX/Rain is additive and a cloud
        /// is a dozen stacked puffs, so each one stays faint.</summary>
        public const float Alpha = 0.22f;
        /// <summary>Live-particle cap per car: two wheels x rate x life, plus
        /// headroom for the life jitter.</summary>
        public const int Capacity = 96;

        /// <summary>The water's own tint, a touch greyer than the falling rain
        /// (road water carries grime). The light that actually colours it
        /// comes from the shader.</summary>
        static readonly Color SprayColour = new Color(0.80f, 0.82f, 0.86f, Alpha);

        // One material and one texture for every car in every scene. Static,
        // HideAndDontSave, and re-made if Unity destroyed them (the Unity null
        // check catches that), so nothing leaks per scene.
        static Material sharedMat;
        static Texture2D sharedTex;
        static bool warned;

        ParticleSystem ps;
        GameObject holder;
        ParticleSystem.EmitParams emit;
        /// <summary>Fractional puffs owed per rear wheel (RL, RR).</summary>
        readonly float[] budget = new float[2];

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
        }

        void OnDestroy()
        {
            if (holder != null) Destroy(holder);
        }

        void LateUpdate()
        {
            if (car == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Only while it is actually raining in this scene: a car that
            // outlives the WeatherFx instance stops spraying rather than
            // throwing water off a dry road.
            float t = WeatherFx.Raining
                ? Mathf.Clamp01((car.speedKmh - SpeedStart) / (SpeedFull - SpeedStart))
                : 0f;
            if (t <= 0f && ps == null) return;

            var contacts = car.wheelContacts;
            if (contacts == null || contacts.Length < 4) return;

            Vector3 carVel = CarVelocity();
            // Which way is "behind": reversing fast throws the water forward.
            float dir = car.forwardSpeed >= 0f ? 1f : -1f;

            for (int k = 0; k < 2; k++)
            {
                var c = contacts[2 + k];
                // Airborne, off the tarmac (grass does not throw a sheet of
                // water), or too slow: nothing, and no banked budget - landing
                // would otherwise dump every puff the wheel owed in the air.
                if (t <= 0f || !c.grounded || !c.onRoad) { budget[k] = 0f; continue; }
                if (ps == null && !Build()) return;

                budget[k] += t * RateAtFull * dt;
                // Bounded per frame, as TireSmoke does: a long stall must not
                // hand one frame a wall of spray.
                int n = Mathf.Min(Mathf.FloorToInt(budget[k]), 4);
                budget[k] -= n;
                for (int j = 0; j < n; j++) Spawn(c, carVel, dir);
            }
        }

        /// <summary>The car's velocity, from the body when physics owns it and
        /// from the replay-written <c>forwardSpeed</c> when it does not (see the
        /// replay trap above).</summary>
        Vector3 CarVelocity()
        {
            var body = car.Body;
            if (body != null && !body.isKinematic) return body.linearVelocity;
            return car.transform.forward * car.forwardSpeed;
        }

        void Spawn(CarController.WheelContact c, Vector3 carVel, float dir)
        {
            Vector3 n = c.normal.sqrMagnitude > 1e-6f ? c.normal.normalized : Vector3.up;
            Vector3 fwd = c.forward.sqrMagnitude > 1e-6f ? c.forward.normalized : car.transform.forward;
            Vector3 side = Vector3.Cross(n, fwd);
            if (side.sqrMagnitude > 1e-6f) side.Normalize(); else side = car.transform.right;

            // Just behind and above the contact patch - where the tread leaves
            // the road and flings the water it picked up.
            emit.position = c.point + n * 0.22f - fwd * (0.30f * dir)
                            + side * Random.Range(-0.18f, 0.18f);
            // Up and back off the tread, a little sideways, on top of the share
            // of the car's own motion.
            emit.velocity = carVel * Inherit
                            + n * Random.Range(0.9f, 2.2f)
                            - fwd * (Random.Range(0.5f, 1.6f) * dir)
                            + side * Random.Range(-0.9f, 0.9f);
            emit.startLifetime = Life * Random.Range(0.8f, 1.15f);
            // The size over life is a multiplier on this (SizeStart/SizeEnd
            // -> 1), so start size here is the size a puff DIES at.
            emit.startSize = SizeEnd * Random.Range(0.8f, 1.2f);
            emit.startColor = SprayColour;
            // Spin, so a dozen copies of one texture are not a dozen copies.
            emit.rotation = Random.Range(0f, 360f);
            ps.Emit(emit, 1);
        }

        /// <summary>Make the particle system on first use. False when no rain
        /// shader could be found (logged once), in which case the car simply
        /// never sprays.</summary>
        bool Build()
        {
            var mat = SprayMaterial();
            if (mat == null) { enabled = false; return false; }

            holder = new GameObject(car.name + " Spray");
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            ps = holder.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Capacity;
            main.startLifetime = Life;
            main.startSpeed = 0f;
            main.startSize = SizeEnd;
            main.startColor = SprayColour;
            // A light pull back down: the mist lifts off the tread to about
            // the car's waist and settles, instead of climbing like smoke.
            main.gravityModifier = 0.35f;

            // No automatic emission and no shape: every puff comes from an
            // explicit Emit at a contact patch.
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // Grows fast then slows: a puff is tyre-sized for an instant and
            // spends most of its life as a spreading cloud.
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            float s0 = SizeStart / SizeEnd;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, s0, 2.2f, 2.2f),
                new Keyframe(1f, 1f, 0.15f, 0.15f)));

            // Fade in over the first eighth, out over the rest: one that
            // appears at full strength pops.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var r = holder.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            emit = new ParticleSystem.EmitParams();
            ps.Play();
            return true;
        }

        /// <summary>The shared spray material: PSX/Rain, or PSX/Decal if the
        /// rain shader is missing from the build (an unlit grey mist beats a
        /// pink one), or nothing.</summary>
        static Material SprayMaterial()
        {
            if (sharedMat != null) return sharedMat;
            var shader = Shader.Find("PSX/Rain");
            if (shader == null) shader = Shader.Find("PSX/Decal");
            if (shader == null)
            {
                if (!warned) { warned = true; Debug.LogWarning("[TireSpray] No PSX/Rain or PSX/Decal shader; wet-road spray disabled."); }
                return null;
            }
            if (sharedTex == null) sharedTex = MistTexture();
            sharedMat = new Material(shader)
            {
                name = "TireSpray (runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = sharedTex,
            };
            if (sharedMat.HasProperty("_Tint")) sharedMat.SetColor("_Tint", Color.white);
            return sharedMat;
        }

        /// <summary>A soft, CLUMPY blob, sixteen texels square and point
        /// filtered like every texture in the game. A smooth disc stacked a
        /// dozen times reads as a row of discs; hashed holes in it read as
        /// droplets and mist. Deterministic (an integer hash, not Random), so
        /// the spray looks the same every run.</summary>
        static Texture2D MistTexture()
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "TireSpray mist",
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (n * 0.5f);
                    float fall = Mathf.Clamp01(1f - d);
                    fall *= fall;
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
                    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                    float noise = (h & 0xFFFF) / 65535f;
                    byte a = (byte)(255f * Mathf.Clamp01(fall * (0.55f + 0.65f * noise)));
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
