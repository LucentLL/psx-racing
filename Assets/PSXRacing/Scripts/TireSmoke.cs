using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The cloud a scrubbing tyre throws off: white rubber smoke on tarmac,
    /// pale dust everywhere else.
    ///
    /// Hand-rolled rather than a ParticleSystem, for the same reason the rest
    /// of this game's presentation is hand-rolled: the puffs have to take the
    /// scene's manual fog (PSX/Decal reads the same _PSXFog globals every
    /// surface does), snap to the low-resolution grid with everything else, and
    /// be emitted from a contact patch the physics already computed. Wiring a
    /// ParticleSystem to all three is more code than the forty lines of
    /// integration below, and none of it would be readable.
    ///
    /// A fixed pool with no allocation after Awake, one mesh, one draw call.
    /// Dead particles collapse to a degenerate quad rather than being compacted
    /// out — the index buffer is built once and never touched again.
    ///
    /// The billboards face the MAIN camera, which is also the camera the
    /// mirror does not use. A puff seen in the rear-view is therefore edge-on
    /// to the wrong eye; at 128 pixels wide, behind a car, that is not a
    /// difference anyone can see, and it saves rebuilding the mesh twice.
    /// </summary>
    public class TireSmoke : MonoBehaviour
    {
        public CarController car;
        public Material material;

        /// <summary>Live puffs at once. The player's car earns a bigger pool
        /// than the opponents': its smoke is the smoke being looked at.</summary>
        public int capacity = 48;
        /// <summary>Scales emission and size together, so an opponent four car
        /// lengths away is not throwing up the same wall of smoke as the car
        /// the camera is on.</summary>
        public float density = 1f;

        /// <summary>Scrub speed where a tyre starts smoking, in m/s. Higher
        /// than the marks': rubber is laid down well before it starts to
        /// vaporise, which is why a car can leave a line without a cloud.</summary>
        const float SlideStart = 2.6f;
        const float SlideFull = 9f;

        /// <summary>Puffs per second per wheel at full scrub. High enough that
        /// consecutive ones OVERLAP at racing speed — a rate that leaves gaps
        /// reads as a row of circles being dropped on the road rather than as
        /// a cloud coming off a tyre.</summary>
        const float RateAtFull = 42f;

        const float LifeMin = 0.55f, LifeMax = 1.15f;
        const float SizeStart = 0.55f, SizeEnd = 2.9f;
        /// <summary>Peak opacity of one puff. Deliberately low — the cloud is
        /// made of a dozen of these stacked, and each one being solid is how
        /// smoke ends up looking like cotton wool.</summary>
        const float Alpha = 0.28f;
        /// <summary>Fraction of a puff's life spent fading IN. The rest fades
        /// out; a particle that appears at full opacity pops.</summary>
        const float FadeIn = 0.18f;

        /// <summary>Upward drift and how quickly the cloud gives up the car's
        /// velocity it was born with.</summary>
        const float Rise = 1.15f;
        const float Drag = 1.9f;
        /// <summary>How much of the car's own velocity a puff inherits. Smoke
        /// is left behind, not carried — at 1 the cloud would travel with the
        /// car and never appear to come off the tyres at all.</summary>
        const float Inherit = 0.30f;

        static readonly Color32 RoadSmoke = new Color32(0xE8, 0xE8, 0xEE, 0xFF);
        static readonly Color32 DirtSmoke = new Color32(0xC6, 0xAE, 0x84, 0xFF);

        struct Puff
        {
            public Vector3 pos, vel;
            public float age, life, spin;
            /// <summary>Per-puff size multiplier. Without it every puff is the
            /// same circle at the same moment of its life, and a dozen
            /// identical circles in a row reads as a row of circles — which is
            /// the one thing smoke never looks like.</summary>
            public float scale;
            public Color32 tint;
        }

        Puff[] pool;
        int cursor;

        Mesh mesh;
        GameObject holder;
        Vector3[] verts;
        Vector2[] uvs;
        Color32[] colors;
        readonly float[] budget = new float[4];

        Camera view;
        float staticLoad = 3000f;
        /// <summary>Whether last frame had anything alive, so the frame the
        /// cloud empties still gets uploaded and the one after it does not.
        /// </summary>
        bool hadAny;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (car != null) staticLoad = Mathf.Max(500f, car.massKg * 9.81f * 0.25f);

            capacity = Mathf.Clamp(capacity, 4, 512);
            pool = new Puff[capacity];
            verts = new Vector3[capacity * 4];
            uvs = new Vector2[capacity * 4];
            colors = new Color32[capacity * 4];

            var tris = new int[capacity * 6];
            for (int q = 0; q < capacity; q++)
            {
                int v = q * 4, t = q * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                // Two triangles of a quad, with the puff texture mapped across
                // it. Written once: only positions and colours move.
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);
            }

            mesh = new Mesh { name = "TireSmoke" };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = tris;

            holder = new GameObject(name + " Smoke");
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = holder.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            if (holder != null) Destroy(holder);
            if (mesh != null) Destroy(mesh);
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Advance the cloud by one step. Split out from LateUpdate so the
        /// preview tool can drive it on a clock of its own: Time.deltaTime is
        /// zero outside play mode, and a smoke system stepped by zero spawns
        /// nothing, moves nothing and photographs as an empty road.
        /// </summary>
        public void Tick(float dt)
        {
            if (car == null || material == null) return;
            if (dt <= 0f) return;

            Emit(dt);
            Integrate(dt);
            BuildMesh();
        }

        void Emit(float dt)
        {
            Vector3 carVel = car.Body != null ? car.Body.linearVelocity : Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                var c = car.wheelContacts[i];
                if (!c.grounded)
                {
                    // Do not carry a budget across a jump: landing would dump
                    // every puff the wheel banked while it was in the air.
                    budget[i] = 0f;
                    continue;
                }

                float t = Mathf.Clamp01((c.slide - SlideStart) / (SlideFull - SlideStart)) *
                          Mathf.Clamp01(c.load / staticLoad);
                if (t <= 0.01f) { budget[i] = 0f; continue; }

                budget[i] += t * RateAtFull * density * dt;
                // Bounded per frame. A long stall — a scene load, a breakpoint —
                // would otherwise hand one frame a hundred puffs and flush the
                // whole pool in an instant.
                int n = Mathf.Min(Mathf.FloorToInt(budget[i]), 4);
                budget[i] -= n;

                for (int k = 0; k < n; k++) Spawn(c, carVel, t);
            }
        }

        void Spawn(CarController.WheelContact c, Vector3 carVel, float t)
        {
            // Sideways from the contact patch, so the cloud comes off the SIDE
            // of the tyre the way it does on a car and not out of the middle
            // of the wheel.
            Vector3 side = Vector3.Cross(c.normal, c.forward);
            if (side.sqrMagnitude > 1e-6f) side.Normalize(); else side = Vector3.right;

            var p = new Puff
            {
                pos = c.point + c.normal * 0.16f
                      + side * Random.Range(-0.22f, 0.22f)
                      + c.forward * Random.Range(-0.2f, 0.1f),
                vel = carVel * Inherit
                      + c.normal * (Rise * Random.Range(0.6f, 1.4f))
                      + side * Random.Range(-1.1f, 1.1f),
                age = 0f,
                life = Random.Range(LifeMin, LifeMax) * Mathf.Lerp(0.7f, 1f, t),
                spin = Random.Range(0f, 360f),
                scale = Random.Range(0.68f, 1.35f),
                tint = c.onRoad ? RoadSmoke : DirtSmoke,
            };

            pool[cursor] = p;
            cursor = (cursor + 1) % capacity;
        }

        void Integrate(float dt)
        {
            float k = 1f - Mathf.Exp(-Drag * dt);
            for (int i = 0; i < capacity; i++)
            {
                if (pool[i].life <= 0f) continue;
                pool[i].age += dt;
                if (pool[i].age >= pool[i].life) { pool[i].life = 0f; continue; }
                // Bleed off the inherited velocity, but never the rise: a cloud
                // that stops climbing sits on the road like a puddle.
                pool[i].vel -= pool[i].vel * k;
                pool[i].vel.y += Rise * 0.35f * dt;
                pool[i].pos += pool[i].vel * dt;
            }
        }

        void BuildMesh()
        {
            var cam = View;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;

            bool any = false;
            Vector3 lo = Vector3.zero, hi = Vector3.zero;

            for (int i = 0; i < capacity; i++)
            {
                int v = i * 4;
                if (pool[i].life <= 0f)
                {
                    // Collapsed to a point AND cleared to zero alpha. Either
                    // alone leaves a dead particle drawing something: a
                    // zero-size quad still rasterises a pixel or two on a
                    // 240-line buffer.
                    verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero;
                    colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = default;
                    continue;
                }

                float u = pool[i].age / pool[i].life;
                float size = Mathf.Lerp(SizeStart, SizeEnd, u) * pool[i].scale * Mathf.Sqrt(density);
                // In fast, out slow: a puff is at its densest the moment it
                // leaves the tyre and thins as it expands.
                float a = u < FadeIn ? u / FadeIn : 1f - (u - FadeIn) / (1f - FadeIn);
                a = Mathf.Clamp01(a) * Alpha;

                // Spin, so a dozen copies of one texture do not read as a dozen
                // copies of one texture.
                float rad = pool[i].spin * Mathf.Deg2Rad + u * 0.7f;
                float cs = Mathf.Cos(rad) * size * 0.5f, sn = Mathf.Sin(rad) * size * 0.5f;
                Vector3 rx = right * cs + up * sn;
                Vector3 ry = up * cs - right * sn;

                Vector3 c0 = pool[i].pos;
                verts[v] = c0 - rx - ry;
                verts[v + 1] = c0 - rx + ry;
                verts[v + 2] = c0 + rx + ry;
                verts[v + 3] = c0 + rx - ry;

                var col = pool[i].tint;
                col.a = (byte)(a * 255f);
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = col;

                if (!any) { any = true; lo = hi = c0; }
                lo = Vector3.Min(lo, c0); hi = Vector3.Max(hi, c0);
            }

            // An idle car uploads NOTHING. Most of a race is spent not sliding,
            // and pushing a vertex buffer full of collapsed quads every frame
            // for every car on the grid is a few megabytes a second of bus
            // traffic to draw nothing at all. The frame the last puff dies
            // still uploads, which is what clears it.
            if (!any && !hadAny) return;
            hadAny = any;

            mesh.vertices = verts;
            mesh.colors32 = colors;
            // Bounds over the LIVE puffs only. The pool's dead slots sit at the
            // world origin, so a recalculated box would stretch from there to
            // the car — eight kilometres of it on Charlotte.
            mesh.bounds = any
                ? new Bounds((lo + hi) * 0.5f, hi - lo + Vector3.one * SizeEnd)
                : new Bounds(transform.position, Vector3.one);
        }

        /// <summary>
        /// The camera the billboards face. Resolved lazily and re-resolved when
        /// it goes null: a race restart destroys the camera this component was
        /// holding, and a cached dead reference is a whole race of smoke facing
        /// a direction nobody is looking from.
        /// </summary>
        Camera View
        {
            get
            {
                if (view == null) view = Camera.main;
                return view;
            }
        }
    }
}
