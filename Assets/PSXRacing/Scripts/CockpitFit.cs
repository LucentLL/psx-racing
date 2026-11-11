using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE CABIN FITTED TO THE CAR'S OWN WINDSCREEN, IN ALL THREE AXES (owner,
    /// 2026-11-11: "all cockpits should fit inside each car model, with mirrors
    /// properly mounted just outside the cockpit window"; on the Yellowbird the
    /// eye sat too far back and high - almost no bonnet or wings in view).
    ///
    /// The cabin used to be scaled by LENGTH only, uniformly about the eye,
    /// until its dash front met the shell's cowl, so every car got the owner's
    /// cabin height and windscreen rake whatever its own. Now his windscreen
    /// pane is laid on the CAR'S windscreen: its base on the shell's cowl (the
    /// line where bonnet meets glass, CarModelBaker), its top on the shell's
    /// header (the front of the roof, measured off the body by
    /// Editor/CockpitFitProbe into CockpitFit.Table.cs - a build cannot read
    /// the shells). That gives the length scale (base to top, fore-aft) and the
    /// height scale (base to top, up), both kept near each other (the cabin's
    /// shape survives), and puts the eye where his Driver_Eye falls in that
    /// cabin: the seat moves with the car's windscreen, not with a fixed reach.
    /// Across: the narrower of the car's own mirrors (inner edge less 1 cm)
    /// and the shell's own side glass at the belt (less 1 cm), never wider
    /// than his cabin. A shell with no row keeps the old fit.
    /// PSX_COCKPIT_FIT3=0: the length-only fit (with the width fit of
    /// 2026-11-11 round 1).
    /// </summary>
    public static partial class CockpitFit
    {
        /// <summary>One shell, in the body's frame as the baker measures the
        /// cowl (yaw and offsets applied, no fit scale): the windscreen's top
        /// on the centreline (z, y of the roof's leading edge), the side
        /// glass's half-width a hand over the belt at the seat, the belt's
        /// height there.</summary>
        public readonly struct Row
        {
            public readonly string key;
            public readonly float headerZ, headerY, sideX, beltY;
            public Row(string key, float headerZ, float headerY, float sideX, float beltY)
            { this.key = key; this.headerZ = headerZ; this.headerY = headerY; this.sideX = sideX; this.beltY = beltY; }
        }

        static System.Collections.Generic.Dictionary<string, Row> byKey;

        public static bool TryGet(string key, out Row r)
        {
            if (byKey == null)
            {
                byKey = new System.Collections.Generic.Dictionary<string, Row>();
                foreach (var t in Table) byKey[t.key] = t;
            }
            r = default;
            return key != null && byKey.TryGetValue(key, out r);
        }

        public static bool On = System.Environment.GetEnvironmentVariable("PSX_COCKPIT_FIT3") != "0";

        /// <summary>The cabin's glass stands this far inside the shell's roof
        /// and side glass, metres.</summary>
        public const float Inset = 0.015f;
        /// <summary>The range of each scale and of height against length.</summary>
        public const float MinScale = 0.75f, MaxScale = 1.25f, MaxAniso = 1.18f;
        /// <summary>Fore-aft, the seat moves FORWARD freely (a short upright
        /// screen - a 911, a Charger - puts the driver nearer the glass) but
        /// back only a little: a long raked screen (an EG, an Evo) is glass
        /// laid over the bonnet, not a longer cabin, and moving the seat back
        /// by it lost the bonnet from view (round 2's first cut). Height: a
        /// hand either way.</summary>
        public const float MaxScaleZ = 1.05f, MinScaleY = 0.85f, MaxScaleY = 1.12f;
        /// <summary>The car's own door mirror stands at least this far ahead
        /// of the driver's eye, metres: a short screen moves the seat forward,
        /// never past the mirrors (the '70 Charger's went behind the eye).</summary>
        public const float MirrorAhead = 0.30f;

        /// <summary>His windscreen pane in his model's frame: base (front,
        /// low) and top (rear, high).</summary>
        public static bool Pane(out Vector3 baseP, out Vector3 topP)
        {
            baseP = topP = default;
            var L = CockpitModel.L;
            var parts = CockpitModel.Parts;
            if (L == null || parts == null || L.glass == null) return false;
            foreach (var g in L.glass)
                if (g.key == "windshield" && parts.TryGetValue(g.part, out var p) && p.mesh != null)
                {
                    var b = p.mesh.bounds;
                    baseP = new Vector3(0f, b.min.y, b.max.z);
                    topP = new Vector3(0f, b.max.y, b.min.z);
                    return b.size.y > 0.1f && b.size.z > 0.1f;
                }
            return false;
        }
    }
}
