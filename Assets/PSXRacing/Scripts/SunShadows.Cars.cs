using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE CARS' OWN SUN SHADOWS (the GT4 study, 2026-10-15).
    ///
    /// The owner put his Linn Cove frame beside GT4's Trial Mountain and asked
    /// what holds the look back. First on the list: every GT4 car sits in a
    /// HARD, DARK, car-shaped shadow thrown by the sun - mostly under it at
    /// noon, stretched long at a low sun - and that is what puts the car ON
    /// the road. Ours cast into the sun's map already, but that map is
    /// 17.6 cm a texel (180 m across, 1024): a car is 25 texels long in it, so
    /// its shadow came out a soft-edged lozenge, mostly hidden under the body,
    /// and the round blob under it said the rest.
    ///
    /// So the cars nearest the camera get a map of their OWN: the same
    /// projection down the LIGHT's rays (PSXGlobals' sun, not the photo sky's
    /// disc), the same packed 16-bit depth through PSX/ShadowCaster, but a box
    /// 40 m across at 1024 (3.9 cm a texel; 28 m at 512 on a touch device,
    /// 5.5 cm) round the stretch of road in front of the chase camera. Nothing
    /// else is drawn into it - a car's draws only, the road and the trees stay
    /// in the big map - and a car drawn here is LEFT OUT of the big map, so
    /// its shadow is never the sharp one and a blurred twin on top of each
    /// other. Every receiver reads it through PSXSunShadow (PSXSunShadow.cginc),
    /// so the road, the walls, the verge AND the other cars' paint take it
    /// without a line changed in any of their shaders.
    ///
    /// Why a map and not a projected silhouette (a mask on a decal): a decal
    /// needs the scene's depth to lie on a bank or a slope, which the Mobile
    /// renderer does not make; a depth map is right on every surface by
    /// construction - a kerb, a wall, the car beside - and costs a car's own
    /// draw calls, which the big map was already paying for it.
    ///
    /// The nearest <see cref="MaxCars"/> (fewer on a touch device) that fit
    /// inside the box; the rest keep their share of the big map. A car is a
    /// Rigidbody whose renderers measure over <see cref="MinCarRadiusM"/> -
    /// the pizza boxes in the passenger seat are Rigidbodies too.
    ///
    /// The strength is the big map's: the hour's (none at dusk or night, the
    /// lamps' hours), times the weather's cut (rain, fog, snow), times the
    /// player's SUN SHADOWS switch, and off with the governor's Level 2.
    /// The blob under every car (PSX/Shadow) keeps a share of itself as the
    /// contact darkening under the body (<see cref="BlobDayFade"/>).
    /// </summary>
    public static partial class SunShadows
    {
        /// <summary>The car map's box: half its side, metres, and how far up
        /// the road from the eye it is centred. A chase camera sits about
        /// 6 m behind its car, so the box runs from a few metres behind the
        /// eye to 33 m in front of it. Not snapped to texels: nothing in this
        /// map stands still, and a box riding with the camera keeps the
        /// player's car on the same texels while it drives straight.</summary>
        public const float CarHalfM = 20f, CarAheadM = 13f;
        public const float CarHalfTouchM = 14f, CarAheadTouchM = 9f;
        public static float CarHalf => small ? CarHalfTouchM : CarHalfM;
        public static float CarAhead => small ? CarAheadTouchM : CarAheadM;
        /// <summary>The column's reach toward the sun and away from it. Away
        /// is the long one: a car's shadow at the dawn light's 6 degrees is
        /// 12 m, and the box's far side is down the rays from its centre.</summary>
        public const float CarTowardM = 40f, CarAwayM = 90f;
        public const int CarResolution = 1024, CarResolutionTouch = 512;
        /// <summary>How many cars it holds, and how far from the eye a car
        /// may be (a low sun's box reaches far down the rays).</summary>
        public const int MaxCars = 8, MaxCarsTouch = 4;
        public const float CarMaxDistM = 45f, CarMaxDistTouchM = 32f;
        /// <summary>A Rigidbody whose renderers' bounds reach less than this
        /// from their centre is not a car (a pizza box, a dropped bottle).</summary>
        public const float MinCarRadiusM = 1.2f;
        /// <summary>Normal offset in the car map's texels and the depth bias
        /// in metres: what keeps a car's paint out of its own shadow (the
        /// road is never in this map, so it cannot shade itself here).</summary>
        public const float CarNormalOffsetTexels = 1.5f, CarDepthBiasM = 0.03f;
        /// <summary>The bias for a car's own paint as a receiver: no car
        /// shades itself from this map (its trunk lip cut a staircase into its
        /// bumper's highlight), a car a metre or more along the ray still does.</summary>
        public const float CarSelfBiasM = 0.45f;
        /// <summary>How much of the blob shadow goes in full sun: what is
        /// left (0.45 of it) is the contact darkening under the body.</summary>
        public const float BlobDayFade = 0.55f;

        /// <summary>How many cars the last car map drew, and in how many
        /// draw calls. For the tools and the play check.</summary>
        public static int CarsLast { get; private set; }
        public static int CarCallsLast { get; private set; }
        public static RenderTexture CarMap => carMap;
        public static Matrix4x4 WorldToCarMap { get; private set; }

        /// <summary>One car: a Rigidbody and the casters under it.</summary>
        class CarGroup
        {
            public Rigidbody body;
            public readonly List<Caster> parts = new List<Caster>();
            public Vector3 centre;
            public float radius;
            public float sort;
            /// <summary>The car map this car was last drawn into (carStamp).</summary>
            public int stamp;
        }

        static readonly Dictionary<Rigidbody, CarGroup> groupOf = new Dictionary<Rigidbody, CarGroup>();
        static readonly List<CarGroup> groups = new List<CarGroup>();
        static readonly List<CarGroup> chosen = new List<CarGroup>();
        static RenderTexture carMap;
        /// <summary>Bumped every car map; a caster whose group carries the
        /// current one is in the car map and stays out of the sun's.</summary>
        static int carStamp = 1;
        static bool carMapThisFrame;

        static readonly int CarMapId = Shader.PropertyToID("_PSXCarShadowMap");
        static readonly int CarMatrixId = Shader.PropertyToID("_PSXCarShadowMatrix");
        static readonly int CarParamsId = Shader.PropertyToID("_PSXCarShadowParams");
        static readonly int BlobFadeId = Shader.PropertyToID("_PSXBlobFade");
        static readonly int CarSelfId = Shader.PropertyToID("_PSXCarShadowBias");

        /// <summary>A moving caster joins its Rigidbody's group (Consider).</summary>
        static void JoinGroup(Caster c)
        {
            var body = c.r.GetComponentInParent<Rigidbody>();
            if (body == null) return;
            if (!groupOf.TryGetValue(body, out var g))
            {
                g = new CarGroup { body = body };
                groupOf[body] = g;
                groups.Add(g);
            }
            g.parts.Add(c);
            c.group = g;
        }

        static void ForgetGroups()
        {
            groupOf.Clear();
            groups.Clear();
            chosen.Clear();
            carMapThisFrame = false;
            CarsLast = CarCallsLast = 0;
        }

        static void CarsOff()
        {
            carMapThisFrame = false;
            Shader.SetGlobalVector(CarParamsId, Vector4.zero);
            Shader.SetGlobalFloat(BlobFadeId, 0f);
        }

        /// <summary>
        /// Choose the cars and queue their map on <see cref="cmd"/>, before
        /// the sun's map (which then leaves them out). Returns whether a map
        /// was queued. <paramref name="rot"/> is the sun's own rotation.
        /// </summary>
        static bool DrawCars(Vector3 eyeAt, Vector3 flat, Quaternion rot)
        {
            carStamp++;
            carMapThisFrame = false;
            CarsLast = CarCallsLast = 0;
            Fit(ref carMap, small ? CarResolutionTouch : CarResolution, "PSXCarShadowMap");

            Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
            Vector3 centre = eyeAt + flat * CarAhead;
            float half = CarHalf;
            int max = small ? MaxCarsTouch : MaxCars;
            float maxDist = small ? CarMaxDistTouchM : CarMaxDistM;

            chosen.Clear();
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                var g = groups[i];
                if (g.body == null) { DropGroup(i); continue; }
                // The group's extent from its live renderers, this frame.
                bool any = false;
                Bounds b = default;
                for (int p = g.parts.Count - 1; p >= 0; p--)
                {
                    var c = g.parts[p];
                    if (c.r == null) { g.parts.RemoveAt(p); continue; }
                    if (!c.r.enabled || !c.r.gameObject.activeInHierarchy) continue;
                    var rb = c.r.bounds;
                    c.centre = rb.center;
                    c.radius = rb.extents.magnitude;
                    if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
                }
                if (!any) continue;
                g.centre = b.center;
                g.radius = b.extents.magnitude;
                if (g.radius < MinCarRadiusM) continue;
                // Wholly inside the column, so its whole shadow is in the map.
                Vector3 d = g.centre - centre;
                if (Mathf.Abs(Vector3.Dot(d, right)) > half - g.radius) continue;
                if (Mathf.Abs(Vector3.Dot(d, up)) > half - g.radius) continue;
                float along = Vector3.Dot(d, fwd);
                if (along < -CarTowardM + g.radius || along > CarAwayM - g.radius) continue;
                g.sort = (g.centre - eyeAt).sqrMagnitude;
                if (g.sort > maxDist * maxDist) continue;
                chosen.Add(g);
            }
            if (chosen.Count == 0) return false;
            chosen.Sort((a, b2) => a.sort.CompareTo(b2.sort));
            if (chosen.Count > max) chosen.RemoveRange(max, chosen.Count - max);

            float far = CarTowardM + CarAwayM;
            Vector3 from = centre - fwd * CarTowardM;
            Matrix4x4 view = Matrix4x4.TRS(from, rot, Vector3.one).inverse;
            view.SetRow(2, -view.GetRow(2));
            Matrix4x4 proj = Matrix4x4.Ortho(-half, half, -half, half, 0f, far);
            Matrix4x4 toUnit = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
            WorldToCarMap = toUnit * proj * view;

            cmd.SetRenderTarget(carMap);
            cmd.ClearRenderTarget(true, true, Color.white);
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.SetGlobalMatrix(CasterMatrixId, WorldToCarMap);
            int calls = 0;
            float now = Time.unscaledTime;
            // Solids into r,g; then the cutouts (a grille, a spoked wheel)
            // into b,a - the reader takes the nearer of the two, so here a
            // cutout is as solid as anything else where it is not cut.
            foreach (var g in chosen)
            {
                g.stamp = carStamp;
                foreach (var c in g.parts)
                {
                    if (c.r == null || c.solids == 0 || !c.r.enabled || !c.r.gameObject.activeInHierarchy) continue;
                    c.drawnAt = now;
                    var proxy = c.solids > 1 ? ProxyFor(c) : null;
                    if (proxy != null) { cmd.DrawMesh(proxy, c.r.localToWorldMatrix, opaque, 0, 0); calls++; continue; }
                    for (int m = 0; m < c.mats.Length; m++)
                        if (c.mats[m] == opaque) { cmd.DrawRenderer(c.r, opaque, m, 0); calls++; }
                }
            }
            foreach (var g in chosen)
                foreach (var c in g.parts)
                {
                    if (c.r == null || c.solids == c.mats.Length || !c.r.enabled || !c.r.gameObject.activeInHierarchy) continue;
                    for (int m = 0; m < c.mats.Length; m++)
                        if (c.mats[m] != null && c.mats[m] != opaque) { cmd.DrawRenderer(c.r, c.mats[m], m, 1); calls++; }
                }
            CarsLast = chosen.Count;
            CarCallsLast = calls;
            chosen.Clear();
            carMapThisFrame = true;
            return true;
        }

        static void DropGroup(int i)
        {
            var g = groups[i];
            foreach (var c in g.parts) if (c != null) c.group = null;
            groups.RemoveAt(i);
            // The key is a destroyed Rigidbody: find it by value.
            Rigidbody dead = null;
            foreach (var kv in groupOf) if (kv.Value == g) { dead = kv.Key; break; }
            if (!ReferenceEquals(dead, null)) groupOf.Remove(dead);
        }

        /// <summary>Is this caster in the car map drawn this frame (and so
        /// left out of the sun's)?</summary>
        static bool InCarMap(Caster c) => carMapThisFrame && c.group != null && c.group.stamp == carStamp;

        /// <summary>After the command buffer ran: the car map's globals, and
        /// the blob's share.</summary>
        static void PushCars()
        {
            Shader.SetGlobalFloat(BlobFadeId, BlobDayFade * Strength);
            if (!carMapThisFrame) { Shader.SetGlobalVector(CarParamsId, Vector4.zero); return; }
            float texel = CarHalf * 2f / carMap.width;
            Shader.SetGlobalTexture(CarMapId, carMap);
            Shader.SetGlobalMatrix(CarMatrixId, WorldToCarMap);
            Shader.SetGlobalVector(CarParamsId, new Vector4(Strength, carMap.width, texel * CarNormalOffsetTexels,
                                                            CarDepthBiasM / (CarTowardM + CarAwayM)));
            Shader.SetGlobalVector(CarSelfId, new Vector4(CarSelfBiasM / (CarTowardM + CarAwayM), texel / (CarTowardM + CarAwayM), 0f, 0f));
        }
    }
}
