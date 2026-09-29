using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE EYE'S ADAPTATION (the colour pass, C10, 2026-09-29): _PSXAdapt,
    /// which multiplies the hour's exposure on every lit surface and the sky,
    /// the fog and the emitters with it (Shaders/PSXTone.cginc).
    ///
    /// C2 gave each hour ONE exposure, set for its sunlit road. Under a roof
    /// that is the wrong one: the sky map takes an underpass's ambient to a
    /// fifth (SunShadows' sky map) and the sun is gone, so the Little
    /// Switzerland tunnel sat on the grade's floor at a clear noon (T2: road
    /// 35, walls 31 of 255) and the underside of the Samuel Street deck near
    /// it - black holes where a real eye, or a camera's meter, opens up.
    ///
    /// WHAT IT MEASURES: how OPEN the sky is over the player, from five rays
    /// of at most <see cref="ProbeM"/> metres - straight up from the eye and
    /// from the car's roof (0.4 each), and three toward the sun from the roof
    /// (0.2 between them) - and nothing else. No GPU meter: WebGL has no
    /// asynchronous read-back, and a meter kept on the GPU is two more passes
    /// per frame on a phone. The rays cost nothing there and give the same
    /// answer on every device. They see colliders only, so a tree's leaves
    /// (billboards, no collider - only the trunks have one) never close the
    /// sky: no pumping down a tree-lined road. A tunnel in the venue's table
    /// (TrackCatalog.tunnels) is a roof whatever the rays find.
    ///
    /// WHAT IT DOES: by day the target is 1 + <see cref="Strength"/> x (1 -
    /// openness), at most <see cref="MaxGain"/> (+1.26 stops); scaled by the
    /// hour's daylight, so at night it is 1 (a tunnel at night is its lamps).
    /// The eye goes dark slowly and bright fast - <see cref="RiseTau"/> into a
    /// tunnel, <see cref="FallTau"/> out of it - so a tunnel opens up over a
    /// couple of seconds and its exit blooms and settles, and a gantry
    /// flicking over the car in a fifth of a second moves it by a hair.
    ///
    /// Play mode only (PSXGlobals.Update ticks it); the tools set the settled
    /// value for a pose with <see cref="SteadyFor"/>. PSX_ADAPT=0 in a tool's
    /// environment holds it at 1 (the before-picture).
    /// </summary>
    public static class ExposureAdapt
    {
        /// <summary>The gain a fully covered place asks for by day, over 1.</summary>
        public const float Strength = 1.4f;
        /// <summary>The most the eye opens: +1.26 stops.</summary>
        public const float MaxGain = 2.4f;
        /// <summary>Seconds (time constant) to open up in the dark, and to
        /// close down in the light.</summary>
        public const float RiseTau = 1.2f, FallTau = 0.35f;
        /// <summary>How far each ray looks for a roof.</summary>
        public const float ProbeM = 40f;
        const float UpWeight = 0.4f, SunWeight = 0.2f / 3f;
        /// <summary>The sun rays fan this far either side of the sun.</summary>
        const float SunFanDeg = 10f;

        /// <summary>The tools' switch: PSX_ADAPT=0 holds the eye at 1.</summary>
        public static bool Enabled => System.Environment.GetEnvironmentVariable("PSX_ADAPT") != "0";

        /// <summary>This frame's settled value, the openness it was read
        /// from, whether the venue's tunnel table said "roof", and the
        /// adaptation as it stands (for the play check's trace).</summary>
        public static float Target { get; private set; } = 1f;
        public static float Openness { get; private set; } = 1f;
        public static bool Tunnel { get; private set; }
        public static float Current { get; private set; } = 1f;

        static readonly RaycastHit[] hits = new RaycastHit[16];
        static int pathHint = -1;

        /// <summary>A scene has loaded: the eye starts adapted to the open sky.</summary>
        public static void Reset()
        {
            Current = 1f; Target = 1f; Openness = 1f; Tunnel = false; pathHint = -1;
        }

        /// <summary>The settled gain for an openness, by daylight (0 night .. 1 day).</summary>
        public static float TargetFor(float openness, float day) =>
            Mathf.Min(MaxGain, 1f + Strength * (1f - Mathf.Clamp01(openness)) * Mathf.Clamp01(day));

        /// <summary>One step of the eye toward <paramref name="target"/>.</summary>
        public static float Step(float current, float target, float dt)
        {
            float tau = target > current ? RiseTau : FallTau;
            return target + (current - target) * Mathf.Exp(-Mathf.Max(0f, dt) / tau);
        }

        /// <summary>How open the sky is over <paramref name="eye"/> and the car
        /// (0 roofed .. 1 open): the five rays (see the class summary).
        /// <paramref name="car"/> may be null (on foot): its rays start at
        /// the eye.</summary>
        public static float OpennessAt(Vector3 eye, Transform car, Vector3 toSun)
        {
            Vector3 roof = car != null ? car.position + Vector3.up * 1.4f : eye;
            float open = 0f;
            if (!Blocked(eye, Vector3.up, car)) open += UpWeight;
            if (!Blocked(roof, Vector3.up, car)) open += UpWeight;
            if (toSun.y > 0.02f)
            {
                Vector3 s = toSun.normalized;
                for (int k = -1; k <= 1; k++)
                {
                    Vector3 d = Quaternion.AngleAxis(k * SunFanDeg, Vector3.up) * s;
                    if (!Blocked(roof, d, car)) open += SunWeight;
                }
            }
            else open += 3f * SunWeight;   // no sun to be out of
            return open;
        }

        static bool Blocked(Vector3 from, Vector3 dir, Transform ignore)
        {
            int n = Physics.RaycastNonAlloc(from, dir, hits, ProbeM, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null) continue;
                if (ignore != null && c.transform.IsChildOf(ignore)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Is the car inside one of the venue's tunnels (the
        /// catalogue's own table, metres along the path)?</summary>
        static bool InVenueTunnel(Transform car, TrackPath path, TrackCatalog.TrackDef def)
        {
            if (car == null || path == null || path.Count == 0 || def == null || def.tunnels == null) return false;
            pathHint = path.NearestIndex(car.position, pathHint >= 0 && pathHint < path.Count ? pathHint : -1, 40);
            return TrackCatalog.InTunnel(def, pathHint * path.spacing);
        }

        /// <summary>
        /// The frame's adaptation, stepped (PSXGlobals.Update, play mode).
        /// 1 with no hour applied (an interior) or with PSX_ADAPT=0.
        /// </summary>
        public static float Tick(PSXGlobals g, float dt)
        {
            if (g == null || g.tone < 0.5f || !Enabled) { Current = 1f; Target = 1f; return 1f; }
            var cam = Camera.main;
            if (cam == null) return Current;
            var rm = RaceManager.Instance;
            Transform car = rm != null && rm.playerCar != null ? rm.playerCar.transform
                          : ChaseCamera.Active != null ? ChaseCamera.Active.target : null;
            Vector3 toSun = g.sun != null ? -g.sun.transform.forward : Vector3.up;
            float open = OpennessAt(cam.transform.position, car, toSun);
            Tunnel = rm != null && InVenueTunnel(car, rm.path, RaceHUD.VenueDef());
            if (Tunnel) open = 0f;
            Openness = open;
            Target = TargetFor(open, 1f - g.night);
            Current = Step(Current, Target, dt);
            return Current;
        }

        /// <summary>
        /// The SETTLED adaptation for a pose (the look tools: a frame is a
        /// moment a player has been standing in for a while). The same rays
        /// and the same tunnel table as <see cref="Tick"/>; <paramref name="path"/>
        /// and <paramref name="def"/> may be null.
        /// </summary>
        public static float SteadyFor(Vector3 eye, Transform car, Vector3 toSun, float night,
                                      TrackPath path, TrackCatalog.TrackDef def)
        {
            if (!Enabled) return 1f;
            pathHint = -1;
            float open = OpennessAt(eye, car, toSun);
            Tunnel = InVenueTunnel(car, path, def);
            if (Tunnel) open = 0f;
            Openness = open;
            Target = TargetFor(open, 1f - night);
            return Target;
        }
    }
}
