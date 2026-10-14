using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE NIGHT AROUND THE EYE (2026-10-13, after Touge Attack): one pass over
    /// the lamp registry per measured eye, for two things.
    ///   * The LAMP SUM NightDark decides lit from unlit by: every lit street
    ///     lamp within NightDark.FarM, weighted by distance and its gain.
    ///   * The LAMP CONES (LampCones, PSX/LampCone): the <see cref="LampCones.Cap"/>
    ///     nearest lit street lamps within <see cref="ConeReachM"/>, each a
    ///     soft additive light cone in the night air under its head.
    ///
    /// NO POPPING without a held set: the cones are ranked by distance alone
    /// (no frustum - a cone behind the eye costs a few vertices, and turning
    /// round must not re-rank anything), and each is faded against the FIRST
    /// EXCLUDED lamp's distance over <see cref="ConeRankBandM"/>. The k-th
    /// nearest distance is a continuous function of the eye's position, so the
    /// cone that drops out of the cap is already at zero when it goes, and the
    /// one coming in starts from zero. Only a lamp switching (the hour) or
    /// registering (a tile streaming in, beyond the reach) can change the set
    /// in a step. Every cone also fades over the last <see cref="ConeFadeBandM"/>
    /// of the reach, and the whole thing is the same in edit and play mode.
    /// </summary>
    public static partial class StreetLights
    {
        /// <summary>Most cones drawn at once; MUST equal CONE_MAX in PSXLampCone.shader.</summary>
        public const int ConeMax = 32;
        /// <summary>Metres: no cone further than this (the lamp table's reach).</summary>
        public const float ConeReachM = MaxReach;
        /// <summary>Metres at the edge of the reach over which a cone fades in.</summary>
        public const float ConeFadeBandM = 30f;
        /// <summary>Metres inside the first excluded lamp's distance over which
        /// a cone fades in (the cap's hand-over).</summary>
        public const float ConeRankBandM = 15f;
        /// <summary>Metres: a lamp whose ground cannot be found (no collider under
        /// it) gets this tall a cone.</summary>
        public const float ConeDefaultH = 7.5f;

        static readonly int[] coneIdx = new int[ConeMax + 1];
        static readonly float[] coneDist = new float[ConeMax + 1];
        static readonly RaycastHit[] groundHits = new RaycastHit[8];

        /// <summary>Lamps lighting the eye's surroundings at the last scan (the
        /// raw NightDark sum).</summary>
        public static float NearLampSum { get; private set; }

        /// <summary>
        /// The scan, the dark-night measure and the cones for one eye.
        /// <paramref name="instant"/>: edit mode / a tool's shot (no easing).
        /// </summary>
        static void NightFrame(Vector3 eye, float dt, bool instant)
        {
            int cap = Mathf.Clamp(LampCones.Cap, 0, ConeMax);
            int ranked = 0;
            float sum = 0f;
            float far2 = NightDark.FarM * NightDark.FarM;
            float reach2 = ConeReachM * ConeReachM;
            bool streetOn = StreetOn;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || e.kind != Kind.Street) continue;
                if (e.owned && e.owner == null) continue;      // Rank prunes it
                if (!Lit(ref e, streetOn)) continue;
                float d2 = (e.pos - eye).sqrMagnitude;
                if (d2 > far2 && d2 > reach2) continue;
                float d = Mathf.Sqrt(d2);
                float gain = e.intensity / StreetIntensity;
                if (d2 <= far2) sum += NightDark.Weight(d) * gain;
                if (d2 <= reach2 && cap > 0)
                {
                    // Keep the cap + 1 nearest, in order (the +1 is the first
                    // excluded, which the hand-over fades against).
                    int n = cap + 1;
                    if (ranked == n && d >= coneDist[n - 1]) continue;
                    int k = ranked < n ? ranked++ : n - 1;
                    while (k > 0 && coneDist[k - 1] > d) { coneDist[k] = coneDist[k - 1]; coneIdx[k] = coneIdx[k - 1]; k--; }
                    coneDist[k] = d;
                    coneIdx[k] = i;
                }
            }
            NearLampSum = sum;
            NightDark.Measure(eye, sum, dt, instant);
            FillCones(eye, cap, ranked);
        }

        static void FillCones(Vector3 eye, int cap, int ranked)
        {
            var A = LampCones.BufA;
            var B = LampCones.BufB;
            int shown = Mathf.Min(cap, ranked);
            float cut = ranked > cap ? coneDist[cap] : float.PositiveInfinity;
            float haze = LampCones.HazeFor(Seasons.CurrentWeather);
            float mix = Mathf.Lerp(LampCones.LitKeep, 1f, NightDark.Amount);
            float strength = LampCones.Strength * haze * mix;
            int n = 0;
            for (int k = 0; k < shown; k++)
            {
                ref Entry e = ref entries[coneIdx[k]];
                float d = coneDist[k];
                float fade = Mathf.Clamp01((ConeReachM - d) / ConeFadeBandM);
                if (cut < float.PositiveInfinity) fade *= Mathf.Clamp01((cut - d) / ConeRankBandM);
                float g = e.intensity / StreetIntensity * strength * fade;
                if (g <= 0f) continue;
                float h = Mathf.Clamp(e.pos.y - GroundUnder(ref e), 2f, 14f);
                A[n] = new Vector4(e.pos.x, e.pos.y, e.pos.z, h);
                B[n] = new Vector4(e.linear.x * g, e.linear.y * g, e.linear.z * g, LampCones.TopRadiusM);
                n++;
            }
            LampCones.Submit(n, eye);
        }

        /// <summary>The nearest registered street lamp to <paramref name="p"/>
        /// (lit or not, any distance) and the ground under it - for the shot
        /// tools that frame a lamp's cone.</summary>
        public static bool NearestStreetHead(Vector3 p, out Vector3 head, out float ground)
        {
            head = default; ground = 0f;
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used || e.kind != Kind.Street || (e.owned && e.owner == null)) continue;
                float d = (e.pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return false;
            head = entries[best].pos;
            ground = GroundUnder(ref entries[best]);
            return true;
        }

        /// <summary>The ground under a lamp head, found once (a ray straight
        /// down, the nearest hit that is not a car) and kept with the lamp.</summary>
        static float GroundUnder(ref Entry e)
        {
            if (e.groundKnown) return e.ground;
            e.groundKnown = true;
            float g = e.pos.y - ConeDefaultH;
            int hits = Physics.RaycastNonAlloc(e.pos + Vector3.down * 0.3f, Vector3.down, groundHits, 30f,
                                               Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                var hit = groundHits[i];
                if (hit.collider == null || hit.collider.attachedRigidbody != null) continue;
                if (hit.distance < best) { best = hit.distance; g = hit.point.y; }
            }
            e.ground = g;
            return g;
        }
    }
}
