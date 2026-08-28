using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Headlights, tail lights and brake lights, drawn the way a PS1 game drew
    /// them: additive sprites on the bodywork plus a stretched pool of light on
    /// the tarmac in front. No real lights are involved — PSX/Lit shades from a
    /// single global direction and ignores the scene's lights entirely, so a
    /// Unity spotlight here would cost a draw pass and change nothing.
    ///
    /// Every offset is measured off the car's own BoxCollider, which CarBody
    /// resizes when a shell is fitted. A hard-coded 1.72 x 4.1 m set of
    /// positions would hang the lamps of a Land Rover in mid-air and bury a
    /// supermini's inside its own doors.
    ///
    /// Brake lights are independent of the hour: they come on whenever the car
    /// is braking, day or night, because that is the one lighting cue that
    /// tells you what the car in front is about to do.
    /// </summary>
    public class CarLights : MonoBehaviour
    {
        public CarController car;
        public BoxCollider box;

        static readonly List<CarLights> all = new List<CarLights>();
        static bool lightsOn;

        /// <summary>Turn every car's running lights on or off. Called by
        /// <see cref="TimeOfDay.Apply"/>; the hour owns this, not the car.
        /// </summary>
        public static void SetAll(bool on)
        {
            lightsOn = on;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i] == null) { all.RemoveAt(i); continue; }
                all[i].Refresh();
            }
        }

        MeshRenderer[] headLens = new MeshRenderer[2];
        MeshRenderer[] tailLens = new MeshRenderer[2];
        Transform pool;
        MeshRenderer poolRenderer;
        Vector3 fitCenter, fitSize;
        bool braking;

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            Refresh();
        }

        void OnDisable()
        {
            all.Remove(this);
            if (pool != null) pool.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            // The pool is deliberately NOT a child of the car — see Build — so
            // it does not go away with it unless it is taken away.
            if (pool != null) Destroy(pool.gameObject);
        }

        void Start()
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            Build();
            // The hour is usually applied before this car exists (the applier
            // runs in RaceManager's Start); pick up whatever it decided.
            lightsOn = TimeOfDay.At(TimeOfDay.Current).lightsOn;
            Refresh();
        }

        /// <summary>
        /// Rebuild the lamp positions if the body changed shape. CarBody can
        /// re-fit the collider after this component has already built itself —
        /// the LifeSim hands over a grid during Start, and component Start order
        /// is undefined — so the fit is re-checked rather than trusted once.
        /// Two Vector3 compares a frame is not worth caching around.
        /// </summary>
        void LateUpdate()
        {
            if (box == null) return;
            if (box.center != fitCenter || box.size != fitSize) Fit();

            bool nowBraking = car != null && (car.brakeInput > 0.15f || car.handbrakeInput);
            if (nowBraking != braking)
            {
                braking = nowBraking;
                var mat = braking ? TailBrightMat : TailDimMat;
                for (int i = 0; i < 2; i++)
                    if (tailLens[i] != null) tailLens[i].sharedMaterial = mat;
                for (int i = 0; i < 2; i++)
                    if (tailLens[i] != null) tailLens[i].enabled = lightsOn || braking;
            }

            PlacePool();
        }

        /// <summary>
        /// The pool lies on the ground plane in WORLD space rather than riding
        /// the body. Parented to the car it would pitch under braking and cut
        /// through the road, which is exactly when the player is looking at it.
        /// </summary>
        void PlacePool()
        {
            if (pool == null || !pool.gameObject.activeSelf) return;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            float steer = car != null ? car.steerInput : 0f;
            fwd = Quaternion.AngleAxis(steer * 12f, Vector3.up) * fwd;
            Vector3 origin = transform.position;
            pool.SetPositionAndRotation(
                new Vector3(origin.x, PoolHeight, origin.z) + fwd * poolDistance,
                Quaternion.LookRotation(Vector3.down, fwd));
        }

        /// <summary>
        /// Build the lamps outside play mode and force them on or off.
        ///
        /// Only the screenshot tool calls this. Whether a headlight quad ends up
        /// buried in the bodywork of one of sixteen shells is a purely visual
        /// failure that throws nothing, and the cheapest way to catch it is a
        /// picture — which means the editor needs a way to make the lamps exist
        /// without entering play mode.
        /// </summary>
        public void PreviewBuild(bool lit)
        {
            if (car == null) car = GetComponent<CarController>();
            if (box == null) box = GetComponent<BoxCollider>();
            Build();
            lightsOn = lit;
            Refresh();
            // LateUpdate never runs outside play mode, so the pool would sit at
            // the world origin — which on the city circuit is the start line,
            // close enough to look deliberate and be completely wrong.
            PlacePool();
        }

        /// <summary>Road ribbon sits at 0.12 and the kerbs at 0.13, so the pool
        /// goes just above both.</summary>
        const float PoolHeight = 0.155f;
        float poolDistance = 7f;

        void Refresh()
        {
            for (int i = 0; i < 2; i++)
            {
                if (headLens[i] != null) headLens[i].enabled = lightsOn;
                if (tailLens[i] != null) tailLens[i].enabled = lightsOn || braking;
            }
            if (pool != null) pool.gameObject.SetActive(lightsOn && isActiveAndEnabled);
        }

        void Build()
        {
            if (headLens[0] != null) return;
            for (int i = 0; i < 2; i++)
            {
                headLens[i] = MakeQuad("Headlight" + i, transform, HeadMat);
                tailLens[i] = MakeQuad("Taillight" + i, transform, TailDimMat);
            }

            var poolGO = new GameObject(name + " LightPool");
            poolRenderer = MakeQuad("Pool", poolGO.transform, PoolMat);
            poolRenderer.transform.localScale = Vector3.one;
            pool = poolGO.transform;
            pool.gameObject.SetActive(false);

            Fit();
        }

        void Fit()
        {
            if (box == null) return;
            fitCenter = box.center;
            fitSize = box.size;

            float halfW = fitSize.x * 0.5f;
            float noseZ = fitCenter.z + fitSize.z * 0.5f;
            float tailZ = fitCenter.z - fitSize.z * 0.5f;
            float headY = fitCenter.y - fitSize.y * 0.12f;
            float tailY = fitCenter.y + fitSize.y * 0.02f;
            float lensW = Mathf.Clamp(fitSize.x * 0.26f, 0.22f, 0.55f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var h = headLens[i].transform;
                h.localPosition = new Vector3(side * halfW * 0.62f, headY, noseZ + 0.03f);
                h.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                h.localScale = new Vector3(lensW, lensW * 0.62f, 1f);

                var t = tailLens[i].transform;
                t.localPosition = new Vector3(side * halfW * 0.66f, tailY, tailZ - 0.03f);
                t.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                t.localScale = new Vector3(lensW * 0.9f, lensW * 0.5f, 1f);
            }

            // The pool starts just past the nose and stretches down the road.
            poolDistance = noseZ + 5.5f;
            if (poolRenderer != null)
                poolRenderer.transform.localScale = new Vector3(fitSize.x * 3.4f, 14f, 1f);
        }

        static MeshRenderer MakeQuad(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        // ------------------------------------------------------------------
        //  Shared assets. Built once per run and reused by every car: two
        //  opponents' headlights should not be two materials.
        // ------------------------------------------------------------------
        static Mesh quadMesh;
        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null) return quadMesh;
                quadMesh = new Mesh { name = "GlowQuad" };
                quadMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f),   new Vector3(0.5f, -0.5f, 0f),
                };
                quadMesh.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(1f, 1f), new Vector2(1f, 0f),
                };
                quadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quadMesh.RecalculateNormals();
                quadMesh.RecalculateBounds();
                return quadMesh;
            }
        }

        static Texture2D glowTex;
        /// <summary>Radial falloff, generated rather than imported: one 48x48
        /// blob is every lamp in the game and an asset for it would be one more
        /// thing to keep in sync with the shader.</summary>
        static Texture2D GlowTex
        {
            get
            {
                if (glowTex != null) return glowTex;
                const int n = 48;
                glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - (n - 1) * 0.5f) / (n * 0.5f);
                        float dy = (y - (n - 1) * 0.5f) / (n * 0.5f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;                       // tighter core, softer edge
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                glowTex.SetPixels32(px);
                glowTex.Apply();
                return glowTex;
            }
        }

        static Material MakeGlowMat(string name, Color tint, float strength)
        {
            var shader = Shader.Find("PSX/Glow");
            // A missing shader would otherwise draw magenta rectangles all over
            // the cars, which reads as damage rather than as a build problem.
            if (shader == null) { Debug.LogWarning("CarLights: PSX/Glow shader missing"); return null; }
            var m = new Material(shader) { name = name };
            m.mainTexture = GlowTex;
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_Strength")) m.SetFloat("_Strength", strength);
            return m;
        }

        static Material headMat, tailDim, tailBright, poolMat;
        /// <summary>
        /// Selective yellow, on every car. This game is set among 1960s-90s
        /// machinery and a white LED headlight is decades out of period —
        /// tungsten and halogen both burn yellow, and French cars were legally
        /// required to. It is also the single cheapest thing that dates the
        /// night scenes correctly.
        /// </summary>
        static Material HeadMat => headMat != null ? headMat
            : headMat = MakeGlowMat("Headlight", new Color(1.00f, 0.80f, 0.32f), 1.5f);
        static Material TailDimMat => tailDim != null ? tailDim
            : tailDim = MakeGlowMat("Taillight", new Color(1.00f, 0.16f, 0.10f), 0.9f);
        static Material TailBrightMat => tailBright != null ? tailBright
            : tailBright = MakeGlowMat("Brakelight", new Color(1.00f, 0.12f, 0.06f), 2.6f);
        /// <summary>The pool the lamps throw, in the same yellow — a warm lamp
        /// with a white pool reads as two different lights.</summary>
        static Material PoolMat => poolMat != null ? poolMat
            : poolMat = MakeGlowMat("LightPool", new Color(0.96f, 0.82f, 0.42f), 0.42f);
    }
}
