using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE MIST BEHIND THE TRAFFIC (RAIN 2, 2026-11-16; the owner's driving
    /// clips: "spray and mist hang behind the cars ahead"). TireSpray rides a
    /// CarController - the player, the rivals - and the city's and the
    /// stages' traffic cars are not CarControllers, so in free roam nothing on
    /// the road but the player threw any water. This gives every traffic car
    /// near the camera the same low mist behind its rear wheels, growing with
    /// its speed, from ONE shared particle system: no component a car, no
    /// physics read but the body's velocity.
    ///
    /// The cars are found through CarLights' own registry (every car that has
    /// lamps is in it; the ones with a CarController are TireSpray's and are
    /// skipped), re-read twice a second into lists that are reused. Drawn with
    /// the tyre spray's material (PSX/Rain), so a car ahead is a pair of tail
    /// lamps in a red haze and oncoming spray glows in the player's beams.
    /// Ticked by <see cref="Rain2Driver"/> while rain falls; Rain2.Spray off
    /// (or PSX_RAIN2=0): never built. A phone gets half the puffs.
    /// </summary>
    public static class Rain2Traffic
    {
        /// <summary>Traffic further than this from the camera throws nothing.</summary>
        public const float Reach = 110f;
        const int Cap = 480;
        const float RefreshSeconds = 0.5f, RearM = 1.9f, HalfTrackM = 0.72f;

        static readonly List<Rigidbody> bodies = new List<Rigidbody>(96);
        static readonly List<float> owed = new List<float>(96);
        // (TireSpray.Spray2) how far the body's origin stands above the road (m, along its up), read
        // from its colliders' lowest point when the list is re-read: the mist is born ON the road.
        static readonly List<float> rideH = new List<float>(96);
        /// <summary>A traffic car's tyre radius for the births (no CarController to ask).</summary>
        const float TrafficWheelR = 0.31f;
        static float nextRefresh;
        static ParticleSystem ps;
        static GameObject holder;
        static ParticleSystem.EmitParams emit;

        /// <summary>For the checks: traffic cars known, puffs alive.</summary>
        public static int Cars => bodies.Count;
        public static int Live => ps != null ? ps.particleCount : 0;

        /// <summary>The body's origin over its lowest collider point (the tyres' bottoms, or the
        /// sills for a body-only collider: within a few cm of the road either way), 0 if none.</summary>
        static readonly List<Collider> colBuf = new List<Collider>(16);
        static float RideHeight(Rigidbody rb)
        {
            var cols = colBuf;
            rb.GetComponentsInChildren(false, cols);
            float low = float.PositiveInfinity;
            for (int i = 0; i < cols.Count; i++)
                if (cols[i] != null && !cols[i].isTrigger) low = Mathf.Min(low, cols[i].bounds.min.y);
            if (float.IsInfinity(low)) return 0f;
            return Mathf.Clamp(rb.position.y - low, 0f, 1.2f);
        }

        static void Refresh()
        {
            bodies.Clear();
            owed.Clear();
            rideH.Clear();
            var all = CarLights.AllLive;
            for (int i = 0; i < all.Count; i++)
            {
                var l = all[i];
                if (l == null || l.TryGetComponent<CarController>(out _)) continue;
                if (!l.TryGetComponent<Rigidbody>(out var rb)) continue;
                bodies.Add(rb);
                owed.Add(0f);
                rideH.Add(TireSpray.Spray2 ? RideHeight(rb) : 0f);
            }
        }

        public static void Tick(float dt, Vector3 eye, Rain2.Tier tier)
        {
            if (!Rain2.On || !Rain2.Spray || dt <= 0f) return;
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + RefreshSeconds; Refresh(); }
            float share = tier == Rain2.Tier.Mobile ? 0.5f : 1f;
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null || !rb.gameObject.activeInHierarchy) continue;
                Vector3 p = rb.position;
                if ((p - eye).sqrMagnitude > Reach * Reach) { owed[i] = 0f; continue; }
                Vector3 v = rb.linearVelocity;
                float kmh = v.magnitude * 3.6f;
                float t = Mathf.Clamp01((kmh - TireSpray.SpeedStart) / (TireSpray.SpeedFull - TireSpray.SpeedStart));
                if (t <= 0f) { owed[i] = 0f; continue; }
                if (ps == null && !Build()) return;
                owed[i] += t * TireSpray.RateAtFull * 2f * share * dt;
                int n = Mathf.Min(Mathf.FloorToInt(owed[i]), 4);
                owed[i] -= n;
                if (n == 0) continue;
                var tr = rb.transform;
                Vector3 fwd = tr.forward, side = tr.right, up = tr.up;
                // moving backwards relative to its nose (a car reversing): the water goes the other way
                float dir = Vector3.Dot(v, fwd) >= 0f ? 1f : -1f;
                for (int j = 0; j < n; j++)
                {
                    float sx = (Random.value < 0.5f ? -1f : 1f) * HalfTrackM + Random.Range(-0.18f, 0.18f);
                    if (TireSpray.Spray2)
                    {
                        // (sprayfix) the rear tyre's contact patch, on the road under the body
                        Vector3 contact = p - up * rideH[i] - fwd * (RearM * dir) + side * sx;
                        TireSpray.Mist2Birth(contact, up, fwd, side, TrafficWheelR, dir, 0f, v,
                                             out var bp, out var bv, out float bl, out float bs);
                        emit.position = bp;
                        emit.velocity = bv;
                        emit.startLifetime = bl;
                        emit.startSize = bs;
                        emit.startColor = new Color(0.80f, 0.82f, 0.86f, TireSpray.Alpha2);
                        TireSpray.NoteBirth(bp, contact, up, TrafficWheelR);
                    }
                    else
                    {
                    emit.position = p - fwd * (RearM * dir) + side * sx + up * 0.25f;
                    emit.velocity = v * TireSpray.Inherit + up * Random.Range(0.9f, 2.2f)
                                    - fwd * (Random.Range(0.5f, 1.6f) * dir) + side * Random.Range(-0.9f, 0.9f);
                    emit.startLifetime = TireSpray.Life * Random.Range(0.8f, 1.15f);
                    emit.startSize = TireSpray.SizeEnd * Random.Range(0.8f, 1.2f);
                    emit.startColor = new Color(0.80f, 0.82f, 0.86f, TireSpray.Alpha);
                    }
                    emit.rotation = Random.Range(0f, 360f);
                    ps.Emit(emit, 1);
                }
            }
        }

        static bool Build()
        {
            var mat = TireSpray.Spray2 ? TireSpray.SprayMaterial2() : TireSpray.SprayMaterial();
            if (mat == null) { Rain2.Spray = false; return false; }
            holder = new GameObject("Rain2 Traffic Mist") { layer = CarReflections.NoReflectLayer };
            Object.DontDestroyOnLoad(holder);
            ps = holder.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Cap;
            main.startLifetime = TireSpray.Life;
            main.startSpeed = 0f;
            main.startSize = TireSpray.SizeEnd;
            main.gravityModifier = TireSpray.Spray2 ? TireSpray.Gravity2 : 0.35f;
            if (TireSpray.Spray2) TireSpray.Spray2Look(ps);
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            float s0 = TireSpray.Spray2 ? TireSpray.Size2Start / TireSpray.Size2End : TireSpray.SizeStart / TireSpray.SizeEnd;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, s0, 2.2f, 2.2f), new Keyframe(1f, 1f, 0.15f, 0.15f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var r = holder.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (TireSpray.Spray2) r.sortMode = ParticleSystemSortMode.Distance;
            emit = new ParticleSystem.EmitParams();
            ps.Play();
            return true;
        }

        /// <summary>Build the shared system now (the driver, on the session's first rainy frame).</summary>
        public static void Prewarm()
        {
            if (Rain2.On && Rain2.Spray && ps == null) Build();
        }

        /// <summary>A new scene: the last one's cars are gone, and its mist with them.</summary>
        public static void Clear()
        {
            bodies.Clear();
            owed.Clear();
            rideH.Clear();
            nextRefresh = 0f;
            if (ps != null) ps.Clear();
        }
    }
}
