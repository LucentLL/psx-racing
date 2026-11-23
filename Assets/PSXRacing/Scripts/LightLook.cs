using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LOOK OF THE NIGHT LIGHTS (the lightlook round, 2026-11-23). The owner,
    /// after the pop-in fix, with two clips of another game as reference: "These
    /// videos are also decent references for how street lights can be handled at
    /// speed." His frames show every lamp of a road as a bright point with a soft
    /// halo receding to the vanishing point, and the road lit continuously by the
    /// row; ours showed a lit patch round the car and few lamps beyond it.
    ///
    /// One switch for the whole look: PSX_LIGHT_LOOK=0 is 992312c8's picture.
    /// Every piece also has its own sub-switch (PSX_LIGHT_LOOK_&lt;NAME&gt;=0), and
    /// every piece is SHADER-SIDE or read at push time, so flipping a static here
    /// changes the very next render (the shot tool's same-pose A/B, the perf
    /// probe's set: op). The globals are written by <see cref="Push"/> from
    /// StreetLights' per-camera hook, every camera, every frame (the stale-global
    /// rule: every push writes the switch).
    /// </summary>
    public static class LightLook
    {
        static bool Env(string n) => System.Environment.GetEnvironmentVariable(n) != "0";

        /// <summary>The whole round (PSX_LIGHT_LOOK=0: 992312c8's lights).</summary>
        public static bool On = Env("PSX_LIGHT_LOOK");
        /// <summary>1. Every lamp head a bright point with a soft halo to the
        /// vanishing point (PSX/Halo: a point floor in real pixels, a hotter
        /// core, the far city's lamps matched to the streamed ones).</summary>
        public static bool Points = Env("PSX_LIGHT_LOOK_POINTS");

        /// <summary>2. The road lit by the row past the near field's 200 m: the
        /// far lamp field (StreetLights.FarField.cs).</summary>
        public static bool FarPools = Env("PSX_LIGHT_LOOK_FARPOOLS");

        /// <summary>3. A far car's lamps a steady point with a halo that grows
        /// (PSX/Glow _LensLook; CarLights' lens materials).</summary>
        public static bool CarPoints = Env("PSX_LIGHT_LOOK_CARPOINTS");
        /// <summary>Read once: whether a lamp-map body gets its far-point
        /// sprites at all (with the look off at start, none are made).</summary>
        public static readonly bool CarPointsAtStart = On && CarPoints;

        /// <summary>5. Sodium ceiling lamps in the stage tunnels (TunnelLamps).</summary>
        public static bool Tunnels = Env("PSX_LIGHT_LOOK_TUNNELS");

        /// <summary>4/7. The headlight table's hand-overs twice as soft (CarLights: rank band 90 m,
        /// distance fade 140-240 m).</summary>
        public static bool Beams = Env("PSX_LIGHT_LOOK_BEAMS");

        public static bool PointsOn => On && Points;
        public static bool CarPointsOn => On && CarPoints;

        static readonly int LookId = Shader.PropertyToID("_PSXLightLook");

        /// <summary>The look's globals, for this camera (StreetLights.OnBeginCamera).
        /// x = the lamp points, y = the car lamp points (0/1).</summary>
        public static void Push()
        {
            Shader.SetGlobalVector(LookId, new Vector4(PointsOn ? 1f : 0f, CarPointsOn ? 1f : 0f, 0f, 0f));
        }
    }
}
