using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE DRIVER'S ARMS (2026-10-16), modelled on the owner's own clip
    /// (memory: psx-racing-cockpit-hands-reference): "hand/arm placement and
    /// articulation. This includes swapping hands to turn steering wheel,
    /// shifting gears, and pulling e-brake". He liked the look of the static
    /// HandsPack hands ("slightly realistic anime"), so these ARE that hand -
    /// the pack's whole arm, hand to elbow and a lengthened upper arm, on its
    /// own skin (tools/cockpit/cockpit_arms.py -> CockpitSrc/arms_geo.bytes, in
    /// the on-demand cockpit.bundle).
    ///
    /// EACH ARM is one mesh skinned HERE, on the CPU (~440 vertices), to two
    /// bones and two pose shapes: the FOREARM bone carries hand and forearm
    /// rigidly (wrists straight, as in the clip) and is placed by the rim; the
    /// UPPER bone bends at the elbow to aim at a shoulder beside the driver's
    /// head; the shapes are the pack's hand posed OPEN (fingers straight and
    /// spread) and RELAXED (a loose half-curl) over its GRIP. CPU because a
    /// SkinnedMeshRenderer re-skins only in the player loop (the edit-mode
    /// shot tools render stale skin) and a plain MeshRenderer is what the rest
    /// of the cabin mirrors for RHD. The mesh is the pack's right hand; the
    /// left is its mirror.
    ///
    /// EVERYTHING HERE IS IN THE LHD MODEL FRAME. The arms ride the cabin's
    /// Model transform, so an RHD car's -1 x scale mirrors them with the rest
    /// of the cabin: the model's right arm - the INBOARD one, the shifter
    /// side - shows as the driver's left in RHD. The wheel angle is the
    /// model's own (CockpitInterior.SetSteer already turns it for RHD).
    ///
    /// THE HANDS (<see cref="Planner"/>, angles only, testable without a
    /// scene): grip just over 9 and 3 and ride the rim for small inputs.
    /// A big turn is RIDDEN, as the owner's rally clip does (2026-11-02):
    /// the outside hand holds on and goes up, over the top and down the far
    /// side; the inside hand lets go low on its own side and rests (the
    /// shifter, its knee) until the wheel comes back near centre - or until
    /// the riding hand runs out of reach, when it takes the rim back on its
    /// own side, clear of the other, and that one lets go. The arms never
    /// cross, and both are never off the rim at once. The INBOARD hand leaves
    /// the rim for ~1 s on a MANUAL shift (CarController.ManualShifts) and
    /// for as long as the handbrake is held, out of frame below the dash.
    ///
    /// THE HANDS NEVER GO INTO THE WHEEL (owner, 2026-10-16: "fingers must
    /// NEVER penetrate the steering wheel"; <see cref="Contact"/>). The wheel
    /// is measured from its mesh (<see cref="Wheel"/>: the rim a torus of
    /// 18.4 mm section, three spokes, the pad) and the grip fitted to it in
    /// cockpit_arms.py: the palm a 2.5 mm skin gap off the rim. At run time
    /// the HAND bone holds the rim with its knuckles exactly along it (the
    /// forearm bends at the wrist toward its elbow); each finger curls to the
    /// furthest curl that stays clear (rim, spokes, pad); grips, regrips and
    /// landings keep the hand's measured reach off the spokes, aimed where
    /// the wheel will be when the hand arrives; and whatever is still inside
    /// is lifted out - off the rim along its surface, off the spokes and pad
    /// (and anything in the air) toward the driver. <see cref="Depths"/>
    /// measures it on the drawn vertices.
    ///
    /// THE ARMS HANG FROM THE DRIVER'S SHOULDERS (owner, 2026-11-02: "Can
    /// you improve the arm positioning while driving? It is pretty sporadic
    /// and arms often overlap unnaturally. maybe pretend they are attached to
    /// driver's shoulders in the driver seat"; <see cref="Shoulders"/>). Each
    /// arm is a two-bone chain from a shoulder fixed in the seat
    /// (<see cref="ShoulderJoint"/>) to the wrist of the hand on the rim: the
    /// elbow is SOLVED from the arm's own two lengths, hanging down and out
    /// (<see cref="PoleLow"/>), lifting with the hand on top of the wheel. Out
    /// of the door, console and seat (and the rim) the elbow swings round the
    /// shoulder-to-wrist line, both bones still joined; and then the two arms
    /// are kept apart (<see cref="ArmClear"/>), the higher arm's elbow
    /// swinging up over the other. Before it, the forearm was aimed at an
    /// elbow point that jumped with the crossing flag, and the upper arm only
    /// POINTED at a shoulder it could not reach.
    ///
    /// AND THEY NEVER CROSS (owner, the same day, with a rally clip): no hand
    /// over hand - one hand rides the rim round, the other lets go; see
    /// <see cref="Planner.Low"/>.
    ///
    /// FOR OTHER CAMERAS (the windshield reflection): <see cref="Live"/> lists
    /// every arm renderer in the scene. They carry the cabin's layer and
    /// SpeedBlur.CarRenderingLayer, and CockpitInterior hides the whole cabin,
    /// arms included, from every camera but the driver's (OnBeginCamera).
    /// </summary>
    public sealed class CockpitArms
    {
        public const string GeoName = "arms_geo";
        /// <summary>Wrist to elbow pivot, metres (cockpit_arms.py FOREARM).</summary>
        public const float ForearmLength = 0.255f;

        static bool contact = true;
        /// <summary>THE HANDS STAY OUTSIDE THE WHEEL (owner, 2026-10-16:
        /// "fingers must NEVER penetrate the steering wheel"): the grip sits
        /// the palm a skin gap off the measured rim, each finger curls only
        /// until it meets the wheel (rim, spokes, pad), grips keep off the
        /// spokes, and any hand still inside is lifted out along the wheel's
        /// surface. Off = the first cut (grip centroid on the rim's centre,
        /// full curl), for before/after measurements only.</summary>
        public static bool Contact
        {
            get => contact;
            set { contact = value; UpdateBlocked(); }
        }

        /// <summary>The planner's no-grip spans: a spoke within the hand's
        /// reach along the rim (its footprint, measured), plus Pad degrees.</summary>
        const float Pad = 3f;
        static void UpdateBlocked()
        {
            var d = rig;
            if (!contact || d == null || d.wheel == null) { Planner.BlockedL = Planner.BlockedR = null; return; }
            float toDeg = Mathf.Rad2Deg / d.wheel.rc;
            // Along +alpha the right hand reaches footMax (its index side), the
            // left (mirrored) -footMin.
            float rLo = d.footMin * toDeg, rHi = d.footMax * toDeg;
            float lLo = -d.footMax * toDeg, lHi = -d.footMin * toDeg;
            var bl = new List<float>(); var br = new List<float>();
            foreach (var sp in d.wheel.spokes)
            {
                bl.Add(sp.ang - sp.halfDeg - lHi - Pad); bl.Add(sp.ang + sp.halfDeg - lLo + Pad);
                br.Add(sp.ang - sp.halfDeg - rHi - Pad); br.Add(sp.ang + sp.halfDeg - rLo + Pad);
            }
            Planner.BlockedL = bl.ToArray();
            Planner.BlockedR = br.ToArray();
        }
        /// <summary>THE SHOULDERS (see the class): each arm a chain from a
        /// fixed shoulder to its wrist, the elbow solved. Off = the elbow-aim
        /// arms before 2026-11-02, for before/after measurements only.</summary>
        public static bool Shoulders = true;
        /// <summary>The LEFT shoulder joint from Driver_Eye (model frame; the
        /// right is the mirror): a seated driver's, out, down and a little
        /// ahead of the eye - with the arm's 0.255 + 0.313 m it holds the rim
        /// with the elbows bent.</summary>
        public static readonly Vector3 ShoulderJoint = new Vector3(-0.18f, -0.28f, 0.02f);
        /// <summary>How far a shoulder comes forward for a reach the arm
        /// cannot make from the seat (over the top, across the wheel), and
        /// how straight the arm gets before it starts to (of its length; a
        /// near-straight arm swings its elbow centimetres for every
        /// millimetre of wrist - the snap), easing in over ReachEase metres.</summary>
        public const float ShoulderReach = 0.14f, ReachBend = 0.88f, ReachEase = 0.05f;
        /// <summary>Where the elbow hangs, from the shoulder: DOWN (and a
        /// little forward with the hand low on the rim, back with it on top),
        /// and to the side AWAY from where the hand reaches (PoleOut) - a
        /// right hand across at 11 o'clock has its elbow low and to the right,
        /// never out past the hand (owner, 2026-11-02, the first chain: "right
        /// arm is on left side of wheel, bending backwards"). The door is
        /// closer than real shoulders: the cabin swing moves it in.</summary>
        public static readonly Vector3 PoleLow = new Vector3(0f, -0.30f, 0.12f);
        public static readonly Vector3 PoleHigh = new Vector3(0f, -0.20f, -0.04f);
        public const float PoleOut = 0.08f;
        /// <summary>An elbow out past its own wrist (in the direction the hand
        /// reached, metres) is bent backwards: no swing may put it there.</summary>
        public const float BackBend = 0.03f;
        /// <summary>Seconds the elbow's hang follows its pole in (a low pass:
        /// the hand moves, the elbow swings after it).</summary>
        public const float HangLag = 0.06f;
        /// <summary>The arms' bones kept this far apart (centre lines: two
        /// arm radii and a gap), and how fast an elbow may swing to do it or
        /// to stay out of the cabin (degrees a second).</summary>
        public const float ArmClear = 0.075f, SwivelRate = 300f, SwivelRateOut = 600f;
        /// <summary>The higher arm starts to lift with the bones this far
        /// apart (it has a few frames to get there before they would touch).</summary>
        public const float LiftClear = 0.10f;
        /// <summary>The arm's radius at the bone, for the cabin and rim checks.</summary>
        public const float ArmRadius = 0.034f;
        /// <summary>A hand's flight between the rim and where it rests: off the
        /// rim this far toward the driver over the first FlightLift of it,
        /// across, and onto the rim (or its rest) from FlightLand on - a hand
        /// let go on the far side still has its fingers round the rim, and one
        /// coming back must not come up through a spoke.</summary>
        public const float FlightBack = 0.06f, FlightLift = 0.35f, FlightLand = 0.7f;
        /// <summary>How fast an elbow drifts back to its own hang when nothing
        /// is in its way (degrees a second): slowly, so a lift held to keep
        /// the arms apart is not undone the next frame.</summary>
        public const float SwivelRateBack = 150f;

        /// <summary>The visual wheel's fastest turn, degrees a second (the
        /// physics rack sweeps lock to lock in ~0.15 s; hands cannot).</summary>
        public const float WheelMaxRate = 1080f;

        // ------------------------------------------------------------------
        //  Where the arm comes from (model frame, offsets from Driver_Eye,
        //  LEFT arm; the right is the mirror in x). Never in the forward view.
        // ------------------------------------------------------------------

        /// <summary>The forearm aims from here at a hand LOW on the rim (8 and
        /// 4 o'clock): an elbow down by the door / console, below the frame
        /// even when the driver looks over at the passenger seat.</summary>
        public static readonly Vector3 ElbowAim = new Vector3(-0.08f, -0.58f, 0.16f);
        /// <summary>... and from here at a hand on TOP of the rim: the elbow up
        /// and back toward the chest at shoulder width, so the forearm comes from
        /// behind and the palm lies DOWN over the rim instead of standing the
        /// hand on it.</summary>
        public static readonly Vector3 ElbowAimHigh = new Vector3(-0.11f, -0.42f, 0.10f);
        /// <summary>The upper arm aims at the shoulder: low and back, so the
        /// upper arm stays under the frame even at full look-over; inboard of the
        /// door trim (the cabin is narrower than a pair of shoulders).</summary>
        public static readonly Vector3 Shoulder = new Vector3(-0.13f, -0.36f, -0.06f);
        /// <summary>Where the inboard hand goes: the shifter knob and the
        /// handbrake lever (out of frame below the console), RIGHT arm.</summary>
        public static readonly Vector3 ShiftKnob = new Vector3(0.33f, -0.52f, 0.30f);
        public static readonly Vector3 BrakeLever = new Vector3(0.29f, -0.56f, 0.10f);
        /// <summary>... and where a free door-side hand rests: on its knee,
        /// under the wheel and out of frame (RIGHT arm; the left is the mirror).</summary>
        public static readonly Vector3 KneeRest = new Vector3(0.04f, -0.53f, 0.42f);
        /// <summary>How much of the aim along the rim's tangent is taken out
        /// (1 = knuckles exactly along the rim), for a hand low on the rim and
        /// on top of it. Low, the knuckle line rolls off the rim (~20 deg, as
        /// the owner's static hands were rolled 25) so the elbow can drop out
        /// of every view.</summary>
        public const float TangentLockLow = 0.12f, TangentLockHigh = 0.45f;

        // ------------------------------------------------------------------
        //  The data
        // ------------------------------------------------------------------

        /// <summary>One arm's geometry in its arm frame: the grip pose, the
        /// upper-arm weight, the two shapes as deltas.</summary>
        public sealed class ArmGeo
        {
            public Vector3[] pos, nrm;
            public Vector2[] uv;
            public float[] upper;
            public int[] tris;
            public Vector3[][] dPos, dNrm;
            public string[] shapeNames;
            public Vector3 elbow;
            /// <summary>Where the rim's tube centre sits in this hand's frame
            /// when it grips (the palm a skin gap off the rim).</summary>
            public Vector3 tube;
            /// <summary>Each finger's vertices and its curl levels (deltas from
            /// the GRIP base; the base level's are null).</summary>
            public Finger[] fingers;
            /// <summary>-1 palm/arm, else the finger (0 index .. 4 thumb), and
            /// the vertex's place in that finger's list.</summary>
            public sbyte[] fingerOf;
            public int[] fingerSlot;
            /// <summary>The pack's hand (the vertices that can touch the wheel).</summary>
            public bool[] isHand;
            /// <summary>The wrist joint (arm frame) and each vertex's weight on
            /// the HAND bone (1 past the wrist, blended over the wrist ring).</summary>
            public Vector3 wrist;
            public float[] handW;
        }

        public sealed class Finger
        {
            public string name;
            public int[] ids;
            public Vector3[][] dPos, dNrm;
        }

        public sealed class Rig
        {
            public ArmGeo right, left;
            /// <summary>The elbow pivot in the arm frame (right arm).</summary>
            public Vector3 elbow, shoulderEnd;
            public float upperLen;
            public int verts, tris;
            /// <summary>The hand's reach along the rim, arm-frame x of the
            /// RIGHT hand (the left's is mirrored), and the skin gap.</summary>
            public float footMin, footMax, gap;
            /// <summary>Curl levels (fractions of GRIP) and which is the base.</summary>
            public float[] levels;
            public int baseLevel;
            public Wheel wheel;
            public Cabin cabin;
        }

        /// <summary>
        /// THE CABIN WALLS a forearm can reach (owner via review, 2026-10-16:
        /// the forearm was cut by the door trim), measured by cockpit_arms.py
        /// from the v08 cabin mesh as heightfields in the model frame: the
        /// door's inner face, the console's driver-side face, the seat's top.
        /// <see cref="Depth"/> is how far a point is inside one (0 outside).
        /// </summary>
        public sealed class Cabin
        {
            public sealed class Wall
            {
                public string name;
                public int ka, kb, kv, solid, na, nb;
                public float a0, a1, b0, b1;
                public float[] v;
            }
            public Wall[] walls;
            public float cell;
            /// <summary>Thicker than this inside a wall is the far side of it.</summary>
            const float Thick = 0.15f;

            public float Depth(Vector3 p, out Vector3 outward, float margin = 0f)
            {
                float best = 0f;
                outward = Vector3.zero;
                foreach (var w in walls)
                {
                    float a = p[w.ka], b = p[w.kb];
                    int i = (int)((a - w.a0) / cell), j = (int)((b - w.b0) / cell);
                    if (i < 0 || j < 0 || i >= w.na || j >= w.nb) continue;
                    float v = w.v[i * w.nb + j];
                    if (float.IsNaN(v)) continue;
                    float d = (w.solid > 0 ? v - p[w.kv] : p[w.kv] - v) + margin;
                    if (d > best && d < Thick + margin)
                    {
                        best = d;
                        outward = Vector3.zero;
                        outward[w.kv] = w.solid > 0 ? 1f : -1f;
                    }
                }
                return best;
            }
        }

        /// <summary>
        /// THE STEERING WHEEL AS A SHAPE (2026-10-16, owner: "fingers must
        /// NEVER penetrate the steering wheel"): measured by cockpit_arms.py
        /// from the v08 wheel mesh, in the wheel's pivot frame (x right, y up
        /// the face, z along the column into the dash; it turns about z). The
        /// rim a torus of oval section, the pad a disc, each spoke two boxes
        /// along its axis. <see cref="Sdf"/> is metres outside (negative in).
        /// </summary>
        public sealed class Wheel
        {
            public float rc, a, b, zc, hubR, hubZ0, hubZ1;
            public Spoke[] spokes;

            public sealed class Spoke
            {
                public float ang, cos, sin;
                public float[] r0, r1, hw, z0, z1;
                /// <summary>Its angular half-width on the rim, degrees.</summary>
                public float halfDeg;
            }

            public float Sdf(Vector3 p)
            {
                float rho = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                float dx = (rho - rc) / a, dz = (p.z - zc) / b;
                float d = (Mathf.Sqrt(dx * dx + dz * dz) - 1f) * Mathf.Min(a, b);
                float hub = Mathf.Max(rho - hubR, Mathf.Max(hubZ0 - p.z, p.z - hubZ1));
                if (hub < d) d = hub;
                foreach (var s in spokes)
                {
                    float u = p.x * s.cos + p.y * s.sin, v = -p.x * s.sin + p.y * s.cos;
                    for (int k = 0; k < s.r0.Length; k++)
                    {
                        float box = Mathf.Max(Mathf.Max(s.r0[k] - u, u - s.r1[k]),
                                              Mathf.Max(Mathf.Abs(v) - s.hw[k], Mathf.Max(s.z0[k] - p.z, p.z - s.z1[k])));
                        if (box < d) d = box;
                    }
                }
                return d;
            }

            /// <summary>Which part of the wheel is nearest p (for reports).</summary>
            public string Which(Vector3 p)
            {
                int k = Part(p);
                return k == 0 ? "rim" : k == 1 ? "pad" : "spoke" + Mathf.RoundToInt(spokes[k - 2].ang);
            }

            /// <summary>The nearest part: 0 rim, 1 pad, 2 + spoke index.</summary>
            public int Part(Vector3 p)
            {
                float rho = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                float dx = (rho - rc) / a, dz = (p.z - zc) / b;
                float d = (Mathf.Sqrt(dx * dx + dz * dz) - 1f) * Mathf.Min(a, b);
                int w = 0;
                float hub = Mathf.Max(rho - hubR, Mathf.Max(hubZ0 - p.z, p.z - hubZ1));
                if (hub < d) { d = hub; w = 1; }
                for (int i = 0; i < spokes.Length; i++)
                {
                    var s = spokes[i];
                    float u = p.x * s.cos + p.y * s.sin, v = -p.x * s.sin + p.y * s.cos;
                    for (int k = 0; k < s.r0.Length; k++)
                    {
                        float box = Mathf.Max(Mathf.Max(s.r0[k] - u, u - s.r1[k]),
                                              Mathf.Max(Mathf.Abs(v) - s.hw[k], Mathf.Max(s.z0[k] - p.z, p.z - s.z1[k])));
                        if (box < d) { d = box; w = 2 + i; }
                    }
                }
                return w;
            }

            /// <summary>The outward direction at p (central differences).</summary>
            public Vector3 Normal(Vector3 p)
            {
                const float e = 0.0015f;
                var n = new Vector3(Sdf(p + Vector3.right * e) - Sdf(p - Vector3.right * e),
                                    Sdf(p + Vector3.up * e) - Sdf(p - Vector3.up * e),
                                    Sdf(p + Vector3.forward * e) - Sdf(p - Vector3.forward * e));
                return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.back;
            }
        }

        static Rig rig;
        static bool rigTried;

        /// <summary>The arms (from the cockpit bundle), or null.</summary>
        public static Rig Data
        {
            get
            {
                if (rig != null || rigTried) return rig;
                var ta = CockpitModel.ArmsAsset();
                if (ta == null) return null;      // not loaded yet, or missing
                rigTried = true;
                try { rig = Read(ta.bytes); }
                catch (System.Exception e) { Debug.LogWarning("[Cockpit] " + GeoName + " unreadable: " + e.Message); }
                UpdateBlocked();
                return rig;
            }
        }

        static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>The format is cockpit_arms.py's (its header says it).</summary>
        static Rig Read(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.ASCII))
            {
                if (new string(r.ReadChars(4)) != "PSXA" || r.ReadInt32() != 3) throw new IOException("not a v3 PSXA file");
                var g = new Rig { elbow = V3(r), upperLen = r.ReadSingle(), shoulderEnd = V3(r) };
                int nv = r.ReadInt32(), ni = r.ReadInt32(), ns = r.ReadInt32();
                var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv]; var up = new float[nv];
                for (int i = 0; i < nv; i++) pos[i] = V3(r);
                for (int i = 0; i < nv; i++) nrm[i] = V3(r);
                for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) up[i] = r.ReadSingle();
                var idx = new int[ni];
                for (int i = 0; i < ni; i++) idx[i] = r.ReadUInt16();
                var shapes = new List<(string, Vector3[], Vector3[])>();
                for (int s = 0; s < ns; s++)
                {
                    string name = new string(r.ReadChars(r.ReadByte()));
                    var dp = new Vector3[nv]; var dn = new Vector3[nv];
                    for (int i = 0; i < nv; i++) dp[i] = V3(r);
                    for (int i = 0; i < nv; i++) dn[i] = V3(r);
                    shapes.Add((name, dp, dn));
                }
                g.verts = nv;
                g.tris = ni / 3;
                g.right = MakeSide(pos, nrm, uv, up, idx, shapes, false, g.elbow);
                g.left = MakeSide(pos, nrm, uv, up, idx, shapes, true, g.elbow);
                // v2: the grip's fit, the fingers' curl levels, the wheel.
                var tube = V3(r);
                g.footMin = r.ReadSingle(); g.footMax = r.ReadSingle(); g.gap = r.ReadSingle();
                int nl = r.ReadInt32();
                g.baseLevel = r.ReadInt32();
                g.levels = new float[nl];
                for (int i = 0; i < nl; i++) g.levels[i] = r.ReadSingle();
                int nf = r.ReadInt32();
                var fr = new Finger[nf]; var fl = new Finger[nf];
                var ofR = new sbyte[nv]; var ofL = new sbyte[nv]; var slot = new int[nv];
                for (int i = 0; i < nv; i++) ofR[i] = ofL[i] = -1;
                for (int f = 0; f < nf; f++)
                {
                    string name = new string(r.ReadChars(r.ReadByte()));
                    int n = r.ReadInt32();
                    var ids = new int[n];
                    for (int i = 0; i < n; i++) { ids[i] = r.ReadInt32(); ofR[ids[i]] = ofL[ids[i]] = (sbyte)f; slot[ids[i]] = i; }
                    fr[f] = new Finger { name = name, ids = ids, dPos = new Vector3[nl][], dNrm = new Vector3[nl][] };
                    fl[f] = new Finger { name = name, ids = ids, dPos = new Vector3[nl][], dNrm = new Vector3[nl][] };
                    for (int L = 0; L < nl; L++)
                    {
                        if (L == g.baseLevel) continue;
                        var dp = new Vector3[n]; var dn = new Vector3[n];
                        for (int i = 0; i < n; i++) dp[i] = V3(r);
                        for (int i = 0; i < n; i++) dn[i] = V3(r);
                        fr[f].dPos[L] = dp; fr[f].dNrm[L] = dn;
                        var mp = new Vector3[n]; var mn = new Vector3[n];
                        for (int i = 0; i < n; i++) { mp[i] = MX(dp[i]); mn[i] = MX(dn[i]); }
                        fl[f].dPos[L] = mp; fl[f].dNrm[L] = mn;
                    }
                }
                var w = new Wheel { rc = r.ReadSingle(), a = r.ReadSingle(), b = r.ReadSingle(), zc = r.ReadSingle(),
                                    hubR = r.ReadSingle(), hubZ0 = r.ReadSingle(), hubZ1 = r.ReadSingle() };
                w.spokes = new Wheel.Spoke[r.ReadInt32()];
                for (int k = 0; k < w.spokes.Length; k++)
                {
                    float ang = r.ReadSingle();
                    int ns2 = r.ReadInt32();
                    var sp = new Wheel.Spoke { ang = ang, cos = Mathf.Cos(ang * Mathf.Deg2Rad), sin = Mathf.Sin(ang * Mathf.Deg2Rad),
                                               r0 = new float[ns2], r1 = new float[ns2], hw = new float[ns2], z0 = new float[ns2], z1 = new float[ns2] };
                    for (int j = 0; j < ns2; j++)
                    {
                        sp.r0[j] = r.ReadSingle(); sp.r1[j] = r.ReadSingle(); sp.hw[j] = r.ReadSingle();
                        sp.z0[j] = r.ReadSingle(); sp.z1[j] = r.ReadSingle();
                    }
                    // On the rim: the outer box's half-width at the rim's inner face.
                    float inner = w.rc - w.a;
                    sp.halfDeg = Mathf.Asin(Mathf.Clamp01(sp.hw[ns2 - 1] / inner)) * Mathf.Rad2Deg;
                    w.spokes[k] = sp;
                }
                g.wheel = w;
                // v3: the cabin walls.
                var cab = new Cabin { walls = new Cabin.Wall[r.ReadInt32()], cell = r.ReadSingle() };
                for (int k = 0; k < cab.walls.Length; k++)
                {
                    var wl = new Cabin.Wall { name = new string(r.ReadChars(r.ReadByte())) };
                    wl.ka = r.ReadInt32(); wl.kb = r.ReadInt32(); wl.kv = r.ReadInt32();
                    wl.a0 = r.ReadSingle(); wl.a1 = r.ReadSingle(); wl.b0 = r.ReadSingle(); wl.b1 = r.ReadSingle();
                    wl.solid = r.ReadInt32(); wl.na = r.ReadInt32(); wl.nb = r.ReadInt32();
                    wl.v = new float[wl.na * wl.nb];
                    for (int i = 0; i < wl.v.Length; i++) wl.v[i] = r.ReadSingle();
                    cab.walls[k] = wl;
                }
                g.cabin = cab;
                var isHand = new bool[nv];
                for (int i = 0; i < nv; i++) isHand[i] = up[i] <= 0f && pos[i].z > -0.13f;
                g.right.tube = tube; g.left.tube = MX(tube);
                g.right.fingers = fr; g.left.fingers = fl;
                g.right.fingerOf = ofR; g.left.fingerOf = ofL;
                g.right.fingerSlot = slot; g.left.fingerSlot = slot;
                g.right.isHand = isHand; g.left.isHand = isHand;
                // The wrist: the forearm's axis, ForearmLength in front of the
                // elbow (cockpit_arms.py FOREARM).
                var wrist = g.elbow + Vector3.forward * ForearmLength;
                var hw = new float[nv];
                for (int i = 0; i < nv; i++)
                    hw[i] = up[i] > 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(wrist.z - 0.014f, wrist.z + 0.014f, pos[i].z));
                g.right.wrist = wrist; g.left.wrist = MX(wrist);
                g.right.handW = hw; g.left.handW = hw;
                return g;
            }
        }

        static Vector3 MX(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        static ArmGeo MakeSide(Vector3[] pos, Vector3[] nrm, Vector2[] uv, float[] up, int[] idx,
                             List<(string, Vector3[], Vector3[])> shapes, bool mirror, Vector3 elbow)
        {
            System.Func<Vector3[], Vector3[]> M = a =>
            {
                var o = new Vector3[a.Length];
                for (int i = 0; i < a.Length; i++) o[i] = mirror ? MX(a[i]) : a[i];
                return o;
            };
            var t = (int[])idx.Clone();
            if (mirror) for (int i = 0; i < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
            var sd = new ArmGeo
            {
                pos = M(pos), nrm = M(nrm), uv = uv, upper = up, tris = t,
                dPos = new Vector3[shapes.Count][], dNrm = new Vector3[shapes.Count][], shapeNames = new string[shapes.Count],
                elbow = mirror ? MX(elbow) : elbow,
            };
            for (int k = 0; k < shapes.Count; k++)
            {
                sd.shapeNames[k] = shapes[k].Item1;
                sd.dPos[k] = M(shapes[k].Item2);
                sd.dNrm[k] = M(shapes[k].Item3);
            }
            return sd;
        }

        // ------------------------------------------------------------------
        //  The planner: what each hand is doing, in rim angles
        // ------------------------------------------------------------------

        public enum Mode { Grip, Slide, Move, Away }
        public enum Kind { None, Home, Reach }

        public sealed class Hand
        {
            public readonly bool left;
            public Mode mode = Mode.Grip;
            public Kind kind;
            /// <summary>Grip: where on the WHEEL it holds (wheel-local clock
            /// angle, degrees; 0 = 3 o'clock, 90 = 12, anticlockwise as the
            /// driver sees it).</summary>
            public float phi;
            /// <summary>Where it is on the RIM now (clock angle in the car).</summary>
            public float alpha;
            /// <summary>A Move along the rim: elapsed, length, from -> to (and
            /// Away's clock and landing).</summary>
            public float t, dur, from, to;
            /// <summary>Where a hand coming back wants to land (clock angle in
            /// the car) before it is moved off the spokes (`to`).</summary>
            public float want;
            /// <summary>Bumped when a straight-line phase starts (the solver
            /// snapshots its last pose there).</summary>
            public int cartGen;
            /// <summary>Away: 0 release, 1 drop, 2 hold, 3 back. brake: to the
            /// handbrake (else the shifter); free: let go at the end of its
            /// ride, resting (on the shifter, or its knee) until the rim
            /// wants it back.</summary>
            public int phase;
            public bool brake, free;
            /// <summary>Away, phase 1: flying on from one rest to another (the
            /// lever to the shifter), not off the rim.</summary>
            public bool fromRest;
            /// <summary>Slide: the way round the rim it is held from going (+1
            /// up, -1 down its own side).</summary>
            public float slideSign;
            public float hold;
            /// <summary>Pose weights 0..1 and where they are heading.</summary>
            public float open, relax, openT, relaxT;
            public Hand(bool left) { this.left = left; }
        }

        public sealed class Planner
        {
            /// <summary>Rest grip: degrees ABOVE 9 and 3 o'clock - just over the
            /// 9/3 spokes, as drivers hold a three-spoke wheel, the little-finger
            /// side resting on the spoke (2026-10-16 review: the clip's low 8/4
            /// grip sits under this cabin's frame, and the hands must show).</summary>
            public const float RestRise = 20f;
            /// <summary>THE RIDE (owner, 2026-11-02, with a rally clip: "I can't
            /// deal with these weirdly placed and backwards bending arms that
            /// overlap ... refer to this other video of how hands should turn a
            /// steering wheel"). No hand over hand: a hand HOLDS the rim and goes
            /// round with it - the outside hand of a turn up, over the top and
            /// down the far side, the inside hand down its own side until it
            /// lets go (to the shifter, or its knee) - so the arms never cross.
            /// Past what one hand can ride (lock is 450 deg), the free hand takes
            /// the rim back on its OWN side, once the other is clear of it, and
            /// then the other lets go.
            /// Measured as a hand's CLIMB: degrees round the rim from 6 o'clock
            /// up its own side (90 = its own 9 / 3 o'clock, 180 = 12, 270 = the
            /// far 3 / 9). A hand rides from LowOf to Far; with no hand to take
            /// over it holds on out to FarHard (or Slip under its low end), then
            /// the rim slips through it.</summary>
            public const float Low = 45f, Far = 300f, FarHard = 325f, Slip = 15f;
            /// <summary>The door-side hand (the model's left) lets go sooner:
            /// below ~8 o'clock its wrist would be in the door trim (the cabin
            /// walls, measured).</summary>
            public const float LowDoor = 65f;
            public static float LowOf(Hand h) => h.left ? LowDoor : Low;
            /// <summary>... and holds on no lower (Slip is the inboard hand's).</summary>
            static float SlipOf(Hand h) => h.left ? 0f : Slip;
            /// <summary>A free hand comes back to its own rest spot only with the
            /// other hand this far from it, now and when it lands (degrees of
            /// rim: the arms stay apart); and only when the wheel is near centre
            /// (ReturnTheta), calm near it (HomeMaxTheta), or the other hand's
            /// ride ends within the flight back (BackSec + ReturnLead) and
            /// ReturnSpare degrees.</summary>
            public const float ClearDeg = 50f, ReturnTheta = 30f, ReturnLead = 0.05f, ReturnSpare = 15f;
            /// <summary>The wheel spinning back toward centre carries no hand
            /// more than this past its own rest, up and over toward its far
            /// side: the rim slips through it (it gripped at its rest with the
            /// wheel wound round, and would ride the unwind across the top).</summary>
            public const float UnwindSlack = 40f;
            public const float HomeSec = 0.28f;
            public const float ReleaseSec = 0.15f, DropSec = 0.25f, HoldSec = 0.3f, BackSec = 0.3f;
            public const float CalmRate = 45f, CalmSec = 0.3f, HomeMaxTheta = 60f, HomeSlack = 20f;
            public const float PoseTau = 0.05f;

            public readonly Hand L = new Hand(true), R = new Hand(false);
            public float Theta, Omega;
            /// <summary>For the self-test: hands let go at a ride's end, hands
            /// back on the rim, reaches started.</summary>
            public int LetGos, Returns, Reaches;
            float lastTheta = float.NaN, calm;
            /// <summary>The handbrake is held with the other hand off the rim:
            /// that one comes back first.</summary>
            bool reachWait;

            public Planner() { Reset(0f); }

            /// <summary>Rest grip (wheel at centre). Not moved off the spokes: it
            /// is placed beside them on purpose (fingers rest on the spoke,
            /// never in it - the contact solve sees to that).</summary>
            public static float RestAlpha(bool left) =>
                Contact ? (left ? 180f - RestRise : RestRise) : (left ? 196f : -16f);   // off: the first cut's

            /// <summary>Wheel-local grip angles a hand may not take, per hand:
            /// (lo, hi) pairs - a spoke under its palm or fingers. Null = none
            /// (CockpitArms fills it from the measured wheel when Contact is on).</summary>
            public static float[] BlockedL, BlockedR;

            /// <summary>The nearest wheel-local grip angle off every spoke.</summary>
            public static float Snap(bool left, float phi)
            {
                var spans = left ? BlockedL : BlockedR;
                if (spans == null) return phi;
                if (Mathf.Abs(Mathf.DeltaAngle(phi, RestAlpha(left))) < 0.5f) return phi;   // the rest itself
                for (int pass = 0; pass < 3; pass++)
                {
                    bool moved = false;
                    for (int i = 0; i + 1 < spans.Length; i += 2)
                    {
                        float c = (spans[i] + spans[i + 1]) * 0.5f, half = (spans[i + 1] - spans[i]) * 0.5f;
                        float d = Mathf.DeltaAngle(c, phi);
                        if (Mathf.Abs(d) >= half) continue;
                        phi += (d >= 0f ? half : -half) - d + (d >= 0f ? 0.01f : -0.01f);
                        moved = true;
                    }
                    if (!moved) break;
                }
                return phi;
            }

            /// <summary>A world grip angle moved to the nearest free one on the
            /// wheel as it stands now.</summary>
            public float SnapAlpha(bool left, float alpha) => Snap(left, alpha - Theta) + Theta;

            /// <summary>... for a hand that lands `inSeconds` from now: free rim
            /// where the wheel WILL be (it keeps turning at Omega), so it lands
            /// on rim, not on a spoke that has come round meanwhile.</summary>
            public float SnapAlphaAt(bool left, float alpha, float inSeconds)
            {
                float th = Theta + Omega * inSeconds;
                return Snap(left, alpha - th) + th;
            }
            static float W360(float a) { a %= 360f; return a < 0f ? a + 360f : a; }
            public static float Beta(bool left, float alpha) => W360(left ? alpha : 180f - alpha);
            public static float AlphaOf(bool left, float beta) => left ? beta : 180f - beta;
            public float BetaOf(Hand h) => Beta(h.left, h.alpha);
            /// <summary>A hand's climb (see Low) at clock angle `alpha`, and back.</summary>
            public static float Climb(bool left, float alpha) => W360(270f - Beta(left, alpha));
            static float AlphaAtClimb(bool left, float climb) => AlphaOf(left, 270f - climb);
            public float ClimbOf(Hand h) => Climb(h.left, h.alpha);
            /// <summary>How fast a hand holding the rim climbs (degrees a second).</summary>
            float ClimbRate(Hand h) => h.left ? -Omega : Omega;
            Hand Other(Hand h) => h == L ? R : L;
            static bool OnRim(Hand h) => h.mode != Mode.Away;

            /// <summary>Both hands gripping at rest on a wheel at `theta` (at
            /// their rest spots in the car, off the spokes).</summary>
            public void Reset(float theta)
            {
                foreach (var h in new[] { L, R })
                {
                    h.mode = Mode.Grip; h.kind = Kind.None;
                    h.phase = 0; h.brake = h.free = false;
                    h.phi = Snap(h.left, RestAlpha(h.left) - theta);
                    h.alpha = h.phi + theta;
                    h.open = h.relax = h.openT = h.relaxT = 0f;
                }
                Theta = theta; Omega = 0f; lastTheta = theta; calm = 0f; reachWait = false;
            }

            /// <summary>One frame: the wheel at `theta` (model degrees), a manual
            /// shift this frame, the handbrake held.</summary>
            public void Step(float dt, float theta, bool shift, bool brake)
            {
                if (dt <= 0f) { Theta = theta; Snap(); return; }
                float raw = float.IsNaN(lastTheta) ? 0f : (theta - lastTheta) / dt;
                Omega = Mathf.Lerp(Omega, raw, 1f - Mathf.Exp(-dt / 0.05f));
                lastTheta = theta;
                Theta = theta;
                calm = Mathf.Abs(Omega) < CalmRate ? calm + dt : 0f;

                // Never both hands off the rim: a reach waits for the other.
                bool canReach = R.mode == Mode.Away || OnRim(L);
                reachWait = brake && !canReach;
                StepAway(L, dt, false, false);
                StepAway(R, dt, shift && canReach, brake && canReach);
                StepHand(L, dt);
                StepHand(R, dt);
                // The ride's ends: a hand there lets go once the other holds the
                // rim inside its own ride; a free hand comes back to its own side
                // when the rim wants it and that side is clear.
                LetGo(L); LetGo(R);
                Uncross();
                Return(L); Return(R);
                // Calm near centre: back to rest, one hand at a time.
                if (calm > CalmSec && L.mode == Mode.Grip && R.mode == Mode.Grip && Mathf.Abs(theta) < HomeMaxTheta)
                    foreach (var h in new[] { L, R })
                        if (Mathf.Abs(Mathf.DeltaAngle(h.phi, RestAlpha(h.left))) > HomeSlack)
                        {
                            float to = h.alpha + Mathf.DeltaAngle(h.alpha, RestAlpha(h.left) + theta);
                            StartRim(h, Kind.Home, to, HomeSec);
                            break;
                        }

                float k = 1f - Mathf.Exp(-dt / PoseTau);
                foreach (var h in new[] { L, R })
                {
                    h.open = Mathf.Lerp(h.open, h.openT, k);
                    h.relax = Mathf.Lerp(h.relax, h.relaxT, k);
                }
            }

            void Snap()
            {
                foreach (var h in new[] { L, R })
                {
                    if (h.mode == Mode.Grip) h.alpha = h.phi + Theta;
                    h.open = h.openT; h.relax = h.relaxT;
                }
            }

            void Grip(Hand h, float alpha)
            {
                // Never onto a spoke: a short slide along the rim to free rim first.
                float free = SnapAlpha(h.left, alpha);
                if (Mathf.Abs(free - alpha) > 3f)
                {
                    h.alpha = alpha;
                    StartRim(h, Kind.Home, free, 0.1f + Mathf.Abs(free - alpha) / 300f);
                    return;
                }
                h.mode = Mode.Grip; h.kind = Kind.None;
                h.alpha = free;
                h.phi = free - Theta;
                h.openT = 0f; h.relaxT = 0f;
            }

            void StepHand(Hand h, float dt)
            {
                switch (h.mode)
                {
                    case Mode.Grip:
                    {
                        h.alpha = h.phi + Theta;
                        h.openT = 0f; h.relaxT = 0f;
                        // Past the end of its ride with no hand to take over: held
                        // there, the rim slipping through it.
                        float c = ClimbOf(h);
                        if (c > FarHard || c < LowOf(h) - SlipOf(h))
                        {
                            h.mode = Mode.Slide;
                            h.slideSign = c > 180f ? 1f : -1f;
                            h.alpha = AlphaAtClimb(h.left, c > 180f ? FarHard : LowOf(h) - SlipOf(h));
                            h.relaxT = 0.6f;
                        }
                        else if (Theta * Omega < 0f && Mathf.Abs(Theta) > 5f && ClimbRate(h) > 0f
                                 && c > Climb(h.left, RestAlpha(h.left)) + UnwindSlack)
                        {
                            h.mode = Mode.Slide;
                            h.slideSign = 1f;
                            h.relaxT = 0.6f;
                        }
                        break;
                    }
                    case Mode.Slide:
                    {
                        // Grips again as the rim turns back into its ride, or stills.
                        float dc = ClimbRate(h);
                        if (dc * h.slideSign < -5f || calm > CalmSec) Grip(h, h.alpha);
                        else { h.openT = 0f; h.relaxT = 0.6f; }
                        break;
                    }
                    case Mode.Move:
                    {
                        // Along the rim: home, or off a spoke.
                        h.t += dt;
                        float u = Mathf.Clamp01(h.t / Mathf.Max(1e-3f, h.dur));
                        h.alpha = Mathf.Lerp(h.from, h.to, Smooth(u));
                        h.relaxT = 0.6f * Mathf.Sin(Mathf.PI * u);
                        h.openT = 0f;
                        if (u >= 1f) Grip(h, h.to);
                        break;
                    }
                }
            }

            public static float Smooth(float u) => u * u * (3f - 2f * u);

            /// <summary>A hand at the end of its ride lets go - only with the
            /// other gripping inside its own.</summary>
            void LetGo(Hand h)
            {
                if (!OnRim(h) || h.mode == Mode.Move) return;
                float c = ClimbOf(h);
                if (h.mode != Mode.Slide && c >= LowOf(h) && c <= Far) return;
                var o = Other(h);
                if (o.mode != Mode.Grip) return;
                float co = ClimbOf(o);
                if (co < LowOf(o) + 10f || co > Far - 10f) return;   // at an end itself
                Leave(h);
                h.free = true;
                LetGos++;
            }

            /// <summary>THE FOREARMS CROSS (an X in the picture): in the rim's
            /// plane, in rim radii, each forearm a line from its hand the way
            /// the driver's eye sees it go (down and out to its own corner, a
            /// third of a radius out per radius down: the shots) - nearer than
            /// CrossClear. A hand low on the far side is UNDER the other arm,
            /// not across it (the ride's far end).</summary>
            public const float CrossClear = 0.2f;
            static readonly Vector2 ElbowAtL = new Vector2(-1.65f, -1.8f), ElbowAtR = new Vector2(1.65f, -1.8f);
            static Vector2 OnRimAt(float alpha) => new Vector2(Mathf.Cos(alpha * Mathf.Deg2Rad), Mathf.Sin(alpha * Mathf.Deg2Rad));
            public static bool Crossed(float alphaL, float alphaR) =>
                SegDist(OnRimAt(alphaL), ElbowAtL, OnRimAt(alphaR), ElbowAtR) < CrossClear;

            /// <summary>Two hands riding the rim together about to cross (now,
            /// or a tenth of a second on): the one heading home lets go - the
            /// other is the one riding out (still: the one further from home).</summary>
            void Uncross()
            {
                if (!OnRim(L) || !OnRim(R)) return;
                float aheadL = L.mode == Mode.Grip ? Omega * 0.1f : 0f, aheadR = R.mode == Mode.Grip ? Omega * 0.1f : 0f;
                if (!Crossed(L.alpha, R.alpha) && !Crossed(L.alpha + aheadL, R.alpha + aheadR)) return;
                float rest = Climb(true, RestAlpha(true));
                Hand go = Mathf.Abs(Omega) > 20f ? (ClimbRate(L) < 0f ? L : R)
                        : (Mathf.Abs(ClimbOf(L) - rest) > Mathf.Abs(ClimbOf(R) - rest) ? L : R);
                Leave(go);
                go.free = true;
                LetGos++;
            }

            /// <summary>Does the rim want a free hand back now?</summary>
            bool Wanted(Hand f)
            {
                if (Mathf.Abs(Theta) < ReturnTheta || (calm > CalmSec && Mathf.Abs(Theta) < HomeMaxTheta)) return true;
                if (f.left && reachWait) return true;
                var o = Other(f);
                if (o.mode != Mode.Grip) return true;   // held at an end, sliding home, or away itself
                float dc = ClimbRate(o), co = ClimbOf(o);
                float room = dc >= 0f ? Far - co : co - LowOf(o);
                return room < Mathf.Abs(dc) * (BackSec + ReturnLead) + ReturnSpare;
            }

            /// <summary>Where on its own side a free hand coming back may land:
            /// climbs off its rest spot, the rest first.</summary>
            static readonly float[] LandTry = { 0f, -20f, -35f, 20f, 40f };

            /// <summary>How close the other hand comes to rim angle `at` on its
            /// way from `now` to `soon` (degrees; 0 if it passes over it).</summary>
            static float Sweep(float now, float soon, float at)
            {
                float d = Mathf.DeltaAngle(now, at), span = soon - now;
                if (Mathf.Abs(span) > 0.01f && Mathf.Sign(d) == Mathf.Sign(span) && Mathf.Abs(d) <= Mathf.Abs(span)) return 0f;
                return Mathf.Min(Mathf.Abs(d), Mathf.Abs(Mathf.DeltaAngle(soon, at)));
            }

            /// <summary>A free hand back on its own side (its rest spot, else
            /// the nearest of LandTry), if it is wanted and the other hand
            /// stays clear of the spot - where it is, and all the way round to
            /// where it will be when this one lands.</summary>
            void Return(Hand f)
            {
                // (from its rest, or most of the way down to it: straight back
                // from where it let go swept the arm through the seat and console)
                if (f.mode != Mode.Away || !f.free || !(f.phase == 2 || (f.phase == 1 && f.t >= DropSec * 0.6f)) || !Wanted(f)) return;
                var o = Other(f);
                float rest = Climb(f.left, RestAlpha(f.left)), land = float.NaN;
                float soon = o.mode == Mode.Grip ? o.alpha + Omega * (BackSec + ReturnLead) : o.mode == Mode.Move ? o.to : o.alpha;
                foreach (float dc in LandTry)
                {
                    if (rest + dc < LowOf(f) + 10f) continue;
                    float at = AlphaAtClimb(f.left, rest + dc);
                    if (!OnRim(o) || (Sweep(o.alpha, soon, at) >= ClearDeg
                                      && !Crossed(f.left ? at : o.alpha, f.left ? o.alpha : at)
                                      && !Crossed(f.left ? at : soon, f.left ? soon : at))) { land = at; break; }
                }
                if (float.IsNaN(land)) return;
                f.phase = 3; f.t = 0f; f.cartGen++;
                f.want = land;
                f.to = SnapAlphaAt(f.left, land, BackSec);
                Returns++;
            }

            void StartRim(Hand h, Kind kind, float to, float dur)
            {
                h.mode = Mode.Move; h.kind = kind;
                h.t = 0f; h.dur = dur; h.phase = 0;
                h.from = h.alpha; h.to = to;
            }

            /// <summary>Off the rim: released, then down to where it rests.</summary>
            void Leave(Hand h)
            {
                h.mode = Mode.Away; h.kind = Kind.Reach;
                h.phase = 0; h.t = 0f; h.hold = HoldSec;
                h.brake = h.free = false;
                h.cartGen++;
            }

            /// <summary>A hand off the rim: the INBOARD hand (the model's right:
            /// the shifter side) for a manual shift (~1 s) or the handbrake
            /// (held); either hand let go at the end of its ride (free: on the
            /// shifter or its knee until Return calls it back).</summary>
            void StepAway(Hand h, float dt, bool shift, bool brake)
            {
                if (h.mode != Mode.Away)
                {
                    if (!brake && !shift) return;
                    Leave(h);
                    h.brake = brake;
                    Reaches++;
                }
                else if (brake && !h.brake)
                {
                    // from the shifter (or on its way back): to the lever
                    h.brake = true; h.free = false;
                    if (h.phase != 0) { h.phase = 1; h.t = 0f; h.cartGen++; h.fromRest = true; }
                }
                else if (shift)
                {
                    h.hold = HoldSec; h.free = false;
                    if (h.phase == 3) { h.phase = 1; h.t = 0f; h.cartGen++; h.fromRest = true; Reaches++; }
                }
                h.t += dt;
                switch (h.phase)
                {
                    case 0: h.relaxT = 1f; h.openT = 0.2f; if (h.t >= ReleaseSec) { h.phase = 1; h.t = 0f; h.cartGen++; h.fromRest = false; } break;
                    case 1: h.relaxT = h.brake ? 0.15f : 1f; h.openT = 0f; if (h.t >= DropSec) { h.phase = 2; h.t = 0f; } break;
                    case 2:
                        h.relaxT = h.brake ? 0f : 1f; h.openT = 0f;
                        h.hold -= dt;
                        if (h.brake && !brake)
                        {
                            // off the lever: to the shifter to wait (Return may call it straight back)
                            h.brake = false; h.free = true;
                            h.phase = 1; h.t = 0f; h.cartGen++; h.fromRest = true;
                        }
                        else if (!h.brake && h.hold <= 0f) h.free = true;   // the shift done: it waits there
                        break;
                    default:
                        h.relaxT = 0.5f; h.openT = 0f;
                        if (h.t < BackSec * 0.7f) h.to = SnapAlphaAt(h.left, h.want, BackSec - h.t);
                        if (h.t >= BackSec) { h.free = false; Grip(h, h.to); }
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        //  The instance: two skinned arms under the cabin's model
        // ------------------------------------------------------------------

        /// <summary>Every arm renderer alive (the reflection camera's list).</summary>
        public static readonly List<Renderer> Live = new List<Renderer>();

        sealed class Arm
        {
            public Hand hand;
            public ArmGeo geo;
            public Transform fore, upper;
            public MeshRenderer mr;
            public Mesh mesh;
            public Vector3[] outP, outN;
            public float doneOpen = -1f, doneRelax = -1f;
            public Matrix4x4 doneF, doneU, doneH;
            /// <summary>Each finger's curl, 0 straight .. 1 GRIP (Contact).</summary>
            public float[] curl = { 1f, 1f, 1f, 1f, 1f }, doneCurl = { -1f, -1f, -1f, -1f, -1f };
            /// <summary>The hand's vertices before its bones (base + shapes).</summary>
            public Vector3[] loc;
            /// <summary>The HAND bone (pos, rot): the rim places it. The forearm
            /// hangs from its wrist, turned toward its elbow (foreRot).</summary>
            public Vector3 pos, forePos;
            public Quaternion rot = Quaternion.identity, foreRot = Quaternion.identity;
            public Vector3 startPos;
            public Quaternion startRot = Quaternion.identity, startForeRot = Quaternion.identity;
            public int gen = -1;
            public bool posed;
            /// <summary>The chain (Shoulders): shoulder, solved elbow, wrist
            /// (model frame); the elbow's swing about the shoulder-to-wrist
            /// line (degrees).</summary>
            public Vector3 shoulder, elbow, wrist, hang;
            public float swivel;
            /// <summary>The untangle's own swing on top of the swivel (degrees;
            /// the cabin's swing pulled it back each frame when they shared
            /// one) and which way it lifts (kept until it is let down).</summary>
            public float lift, liftSgn;
            public bool chained;
        }

        readonly Transform root;
        readonly Arm[] arms = new Arm[2];
        readonly CockpitModel.Layout lay;
        readonly Rig data;
        public readonly Planner Plan = new Planner();
        int lastShifts = -1;

        public Transform Root => root;
        public MeshRenderer LeftRenderer => arms[0].mr;
        public MeshRenderer RightRenderer => arms[1].mr;

        /// <summary>The arms under `model` (the cabin's Model transform), or
        /// null without their data.</summary>
        public static CockpitArms Create(Transform model, CockpitModel.Layout lay, int layer)
        {
            var d = Data;
            var skin = CockpitModel.Mat("skin");
            if (d == null || lay == null || lay.wheel == null || skin == null) return null;
            return new CockpitArms(model, lay, d, skin, layer);
        }

        CockpitArms(Transform model, CockpitModel.Layout lay, Rig d, Material skin, int layer)
        {
            this.lay = lay;
            data = d;
            root = new GameObject("Arms").transform;
            root.gameObject.layer = layer;
            root.SetParent(model, false);
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                var a = new Arm { hand = left ? Plan.L : Plan.R };
                a.fore = new GameObject(left ? "ForearmL" : "ForearmR").transform;
                a.upper = new GameObject(left ? "UpperArmL" : "UpperArmR").transform;
                a.fore.SetParent(root, false);
                a.upper.SetParent(root, false);
                var go = new GameObject(left ? "ArmL" : "ArmR");
                go.layer = layer;
                go.transform.SetParent(root, false);
                a.geo = left ? d.left : d.right;
                int n = a.geo.pos.Length;
                a.outP = new Vector3[n];
                a.outN = new Vector3[n];
                a.loc = new Vector3[n];
                a.mesh = new Mesh { name = left ? "CockpitArm_L" : "CockpitArm_R", hideFlags = HideFlags.DontSave };
                a.mesh.MarkDynamic();
                a.mesh.vertices = a.geo.pos;
                a.mesh.normals = a.geo.nrm;
                a.mesh.uv = a.geo.uv;
                a.mesh.triangles = a.geo.tris;
                // The driver's whole reach, in the model frame (never culled
                // while the cabin is not).
                a.mesh.bounds = new Bounds(lay.eye + new Vector3(0f, -0.3f, 0.25f), new Vector3(1.4f, 1.1f, 1.4f));
                go.AddComponent<MeshFilter>().sharedMesh = a.mesh;
                a.mr = go.AddComponent<MeshRenderer>();
                a.mr.sharedMaterial = skin;
                a.mr.shadowCastingMode = ShadowCastingMode.Off;
                a.mr.receiveShadows = false;
                arms[i] = a;
                Live.Add(a.mr);
            }
            Plan.Reset(0f);
            Solve(true, 0f);
        }

        public void Destroy()
        {
            // The transforms go with the cabin (they are its children).
            foreach (var a in arms)
            {
                if (a == null) continue;
                if (a.mr != null) Live.Remove(a.mr);
                if (a.mesh != null) Object.DestroyImmediate(a.mesh);
            }
        }

        public void Show(bool on)
        {
            if (root != null && root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
        }

        /// <summary>A frame of driving: the wheel's model angle, the car for
        /// its shifts and handbrake. dt 0 = a still (preview): poses snap.</summary>
        public void Tick(float dt, float wheelDeg, CarController car)
        {
            bool shift = false, brake = false;
            if (car != null)
            {
                int n = car.ManualShifts;
                shift = lastShifts >= 0 && n != lastShifts;
                lastShifts = n;
                brake = car.handbrakeInput;
            }
            Plan.Step(dt, wheelDeg, shift, brake);
            Solve(dt <= 0f, dt);
        }

        /// <summary>For previews and tests: step the planner with explicit inputs.</summary>
        public void Drive(float dt, float wheelDeg, bool shift, bool brake)
        {
            Plan.Step(dt, wheelDeg, shift, brake);
            Solve(dt <= 0f, dt);
        }

        // ------------------------------------------------------------------
        //  The solve: hands on the rim, forearms to their elbows
        // ------------------------------------------------------------------

        Vector3 Eye => lay.eye;
        static Vector3 Side(bool left, Vector3 leftOffset) => left ? leftOffset : MX(leftOffset);

        /// <summary>The rim tube's centre line at clock angle `alpha` (model
        /// frame): the measured rim (Contact), else the layout's estimate.</summary>
        public Vector3 RimPoint(float alpha)
        {
            bool m = Contact && data.wheel != null;
            float a = alpha * Mathf.Deg2Rad, r = m ? data.wheel.rc : lay.wheel.rimRadius;
            return lay.wheel.pivot + lay.wheel.rot * new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), m ? data.wheel.zc : lay.wheel.rimZ);
        }

        /// <summary>The wheel's frame in the model, as it stands now.</summary>
        public Matrix4x4 WheelMatrix => Matrix4x4.TRS(lay.wheel.pivot, lay.wheel.rot * Quaternion.Euler(0f, 0f, Plan.Theta), Vector3.one);

        public Vector3 RimTangent(float alpha)
        {
            float a = alpha * Mathf.Deg2Rad;
            return lay.wheel.rot * new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
        }

        /// <summary>The forearm's pose holding the rim at `alpha`: the grip
        /// centre on the tube, the knuckles along the rim, the forearm aimed
        /// back at its elbow.</summary>
        public void RimPose(bool left, float alpha, float off, out Vector3 pos, out Quaternion rot)
            => RimPose(left, alpha, off, out pos, out rot, out _);

        /// <summary>... and the forearm's turn: the HAND keeps its knuckles
        /// along the rim exactly (the grip fitted to the rim holds), the
        /// FOREARM leaves the rim's line by the wrist (TangentLock) to aim at
        /// an elbow out of every view.</summary>
        public void RimPose(bool left, float alpha, float off, out Vector3 pos, out Quaternion rot, out Quaternion foreRot)
            => RimPose(left, alpha, off, null, out pos, out rot, out foreRot);

        /// <summary>... the hand turned to the arm's SOLVED elbow (the chain's
        /// last one, <paramref name="elbowAt"/>), not an aim point.</summary>
        void RimPose(bool left, float alpha, float off, Vector3? elbowAt, out Vector3 pos, out Quaternion rot, out Quaternion foreRot)
        {
            Vector3 P = RimPoint(alpha);
            Vector3 X = RimTangent(alpha);
            // How high on the rim (0 = 6 o'clock... 1 = 12) picks the elbow.
            float high = Planner.Smooth(Mathf.Clamp01(Mathf.InverseLerp(-0.35f, 0.95f, Mathf.Sin(alpha * Mathf.Deg2Rad))));
            // (Contact off = the first cut's elbows, out by the door and console.)
            Vector3 lo = Contact ? ElbowAim : new Vector3(-0.22f, -0.74f, 0.16f);
            Vector3 hi = Contact ? ElbowAimHigh : new Vector3(-0.19f, -0.42f, 0.10f);
            Vector3 A = elbowAt ?? Eye + Side(left, Vector3.Lerp(lo, hi, high));
            Vector3 D = P - A;
            Vector3 Z = (D - Mathf.Lerp(TangentLockLow, TangentLockHigh, high) * Vector3.Dot(D, X) * X).normalized;
            Vector3 Xf = (X - Vector3.Dot(X, Z) * Z).normalized;
            foreRot = Quaternion.LookRotation(Z, Vector3.Cross(Z, Xf));
            Vector3 Y;
            if (Contact)
            {
                Vector3 Zh = (D - Vector3.Dot(D, X) * X).normalized;
                Y = Vector3.Cross(Zh, X);
                rot = Quaternion.LookRotation(Zh, Y);
            }
            else
            {
                Y = Vector3.Cross(Z, Xf);
                rot = foreRot;
            }
            // The rim's tube where the fitted palm wants it (Contact), and
            // lifted off it (toward the driver, out of the palm) when not gripping.
            Vector3 face = lay.wheel.rot * Vector3.back;
            Vector3 tube = Contact ? (left ? data.left.tube : data.right.tube) : Vector3.zero;
            pos = P - rot * tube + (face * 0.6f - Y * 0.4f) * off;
        }

        /// <summary>Where a hand off the rim goes (model frame): the inboard
        /// hand to the lever or the shifter knob, the door-side hand to its
        /// knee (the right arm's pose mirrored).</summary>
        void AwayPose(Hand h, out Vector3 pos, out Quaternion rot)
        {
            if (h.left)
            {
                pos = Eye + MX(KneeRest);
                var q = Quaternion.LookRotation(new Vector3(-0.15f, -0.2f, 1f), Vector3.down);
                rot = new Quaternion(q.x, -q.y, -q.z, q.w);   // M R M, M = mirror in x
                return;
            }
            pos = Eye + (h.brake ? BrakeLever : ShiftKnob);
            rot = h.brake ? Quaternion.LookRotation(new Vector3(0f, -0.45f, 1f), new Vector3(-1f, -0.35f, 0f))
                          : Quaternion.LookRotation(new Vector3(-0.1f, -0.35f, 1f), Vector3.down);
        }

        /// <summary>The solve's parts, for the profiler (tools\perf-probe.ps1).</summary>
        static readonly Unity.Profiling.ProfilerMarker FitMarker = new Unity.Profiling.ProfilerMarker("CockpitArms.Fit"),
            CabinMarker = new Unity.Profiling.ProfilerMarker("CockpitArms.KeepOutOfCabin"),
            PlaceMarker = new Unity.Profiling.ProfilerMarker("CockpitArms.Place");

        void Solve(bool snap, float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                var a = arms[i];
                var h = a.hand;
                // A straight-through-the-air phase starts from wherever the
                // hand was last frame.
                if (a.gen != h.cartGen) { a.gen = h.cartGen; a.startPos = a.pos; a.startRot = a.rot; a.startForeRot = a.foreRot; }
                Vector3? elbowAt = null;
                Vector3 tp; Quaternion tr, tf;
                bool cart = false;
                float u = 0f;
                Vector3 arc = Vector3.zero;
                switch (h.mode)
                {
                    case Mode.Grip:
                    case Mode.Slide:
                        RimPose(h.left, h.alpha, h.mode == Mode.Slide ? 0.012f * h.open : 0f, elbowAt, out tp, out tr, out tf);
                        break;
                    case Mode.Move:
                        RimPose(h.left, h.alpha, 0.015f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(h.t / h.dur)) + 0.01f * h.open, elbowAt, out tp, out tr, out tf);
                        break;
                    default: // Away: the shifter, the lever, its knee - and back
                    {
                        cart = true;
                        Vector3 rp; Quaternion rr, rf;
                        RimPose(h.left, h.phase == 3 ? h.to : Planner.RestAlpha(h.left), 0f, out rp, out rr, out rf);
                        AwayPose(h, out var ap, out var ar);
                        if (h.phase == 0)
                        {
                            tp = a.startPos + (lay.wheel.rot * Vector3.back) * 0.05f + Vector3.down * 0.02f;
                            tr = a.startRot; tf = a.startForeRot;
                            u = Mathf.Clamp01(h.t / Planner.ReleaseSec);
                        }
                        else if (h.phase == 1) { tp = ap; tr = tf = ar; u = Mathf.Clamp01(h.t / Planner.DropSec); arc = Vector3.down * 0.04f; }
                        else if (h.phase == 2) { tp = ap; tr = tf = ar; u = 1f; }
                        else
                        {
                            tp = rp; tr = rr; tf = rf; u = Mathf.Clamp01(h.t / Planner.BackSec); arc = Vector3.down * 0.03f;
                            // Onto the rim from in front and outside, never up through the wheel.
                            Vector3 outward = (RimPoint(h.to) - lay.wheel.pivot).normalized;
                            tp += (outward * 0.05f + lay.wheel.rot * Vector3.back * 0.05f) * (1f - Planner.Smooth(Mathf.Clamp01((u - 0.65f) / 0.35f)));
                        }
                        break;
                    }
                }
                // (a reach to the shifter or lever leaves its rest grip cleanly:
                // the back-off only steered it through the seat and console)
                if (cart && !snap && a.posed && h.mode == Mode.Away && ((h.phase == 1 && h.free && !h.fromRest) || h.phase == 3))
                {
                    float off = Planner.Smooth(Mathf.Clamp01(u / FlightLift)) * (1f - Planner.Smooth(Mathf.Clamp01((u - FlightLand) / (1f - FlightLand))));
                    float e = Planner.Smooth(Mathf.Clamp01((u - FlightLift * 0.5f) / (1f - FlightLift * 0.5f)));
                    a.pos = Vector3.Lerp(a.startPos, tp, e) + (lay.wheel.rot * Vector3.back) * (FlightBack * off) + arc * Mathf.Sin(Mathf.PI * e);
                    a.rot = Quaternion.Slerp(a.startRot, tr, e);
                    a.foreRot = Quaternion.Slerp(a.startForeRot, tf, e);
                }
                else if (cart && !snap && a.posed)
                {
                    float e = Planner.Smooth(u);
                    a.pos = Vector3.Lerp(a.startPos, tp, e) + arc * Mathf.Sin(Mathf.PI * u);
                    a.rot = Quaternion.Slerp(a.startRot, tr, e);
                    a.foreRot = Quaternion.Slerp(a.startForeRot, tf, e);
                }
                else
                {
                    a.pos = tp; a.rot = tr; a.foreRot = tf;
                }
                a.posed = true;
                if (Contact && data.wheel != null) using (FitMarker.Auto()) Fit(a);
                else for (int f = 0; f < 5; f++) a.curl[f] = 1f;
                if (Shoulders) { using (CabinMarker.Auto()) Reach(a, snap, dt); continue; }
                if (Contact && data.cabin != null) using (CabinMarker.Auto()) KeepOutOfCabin(a);
                using (PlaceMarker.Auto()) Place(a);
            }
            if (!Shoulders) return;
            using (CabinMarker.Auto())
            {
                Untangle(snap, dt);
                LastKeep = "";
                // A pose no swing of the elbow clears (a hand low by the seat
                // with the wheel wound round): the forearm turns at the wrist
                // as it always did, and the elbow goes with it - the upper arm
                // may then miss its (hidden) shoulder by a little.
                if (Contact && data.cabin != null)
                    for (int i = 0; i < 2; i++)
                    {
                        var a = arms[i];
                        // (on the drawn vertices, like the measure; nothing to
                        // do - one pass - when the arm is clear)
                        AimFore(a);
                        KeepOutOfCabin(a);
                        a.elbow = a.wrist + a.foreRot * (ElbowL(a.hand.left) - a.geo.wrist);
                    }
            }
            using (PlaceMarker.Auto()) { Place(arms[0]); Place(arms[1]); }
        }

        // ------------------------------------------------------------------
        //  The shoulders: a two-bone chain from the seat to the wrist
        // ------------------------------------------------------------------

        Vector3 ElbowL(bool left) => left ? MX(data.elbow) : data.elbow;
        Vector3 EndL(bool left) => left ? MX(data.shoulderEnd) : data.shoulderEnd;

        /// <summary>The chain's frame for an arm: shoulder (come forward if
        /// the wrist is out of reach), wrist, the line between them, how far
        /// along it the elbow's foot is, the elbow's distance off it, and the
        /// way it hangs (the pole, square to the line).</summary>
        void Chain(Arm a, out Vector3 S, out Vector3 W, out Vector3 dir, out float along, out float off, out Vector3 hang)
        {
            bool left = a.hand.left;
            Vector3 J = a.geo.wrist, eL = ElbowL(left);
            float L2 = (eL - J).magnitude, L1 = (EndL(left) - eL).magnitude;
            W = a.pos + a.rot * J;
            S = Eye + Side(left, ShoulderJoint);
            Vector3 d = W - S;
            float dist = d.magnitude, x = dist - (L1 + L2) * ReachBend;
            if (x > 0f && dist > 1e-5f)
            {
                // a smooth ramp (no kink in the shoulder's speed): x^2/2e, then x - e/2
                float come = x < ReachEase ? x * x / (2f * ReachEase) : x - ReachEase * 0.5f;
                S += d / dist * Mathf.Min(come, ShoulderReach);
                d = W - S;
                dist = d.magnitude;
            }
            dir = dist > 1e-5f ? d / dist : Vector3.forward;
            dist = Mathf.Clamp(dist, Mathf.Abs(L1 - L2) + 1e-3f, L1 + L2 - 1e-4f);
            along = (L1 * L1 - L2 * L2 + dist * dist) / (2f * dist);
            off = Mathf.Sqrt(Mathf.Max(0f, L1 * L1 - along * along));
            // Down and out; up with the wrist on top of the wheel.
            float high = Planner.Smooth(Mathf.Clamp01(Mathf.InverseLerp(lay.wheel.pivot.y - 0.10f, lay.wheel.pivot.y + 0.17f, W.y)));
            // away from the side the hand reached to (its own side when the
            // hand is in front of its shoulder)
            float away = -Mathf.Clamp((W.x - S.x) / 0.12f, -1f, 1f);   // smooth through zero (a flip snapped)
            Vector3 pole = Vector3.Lerp(PoleLow, PoleHigh, high) + new Vector3(away * PoleOut, 0f, 0f);
            hang = pole - Vector3.Dot(pole, dir) * dir;
            if (hang.sqrMagnitude < 1e-8f) hang = Vector3.down - Vector3.Dot(Vector3.down, dir) * dir;
            hang.Normalize();
        }

        /// <summary>The hang eased after the pole (HangLag), kept square to
        /// the shoulder-to-wrist line.</summary>
        Vector3 Ease(Arm a, Vector3 hang, Vector3 dir, bool snap, float dt)
        {
            Vector3 h = snap || !a.chained || a.hang.sqrMagnitude < 0.5f ? hang
                      : Vector3.Slerp(a.hang, hang, 1f - Mathf.Exp(-dt / HangLag));
            h -= Vector3.Dot(h, dir) * dir;
            if (h.sqrMagnitude < 1e-8f) h = hang;
            return h.normalized;
        }

        static Vector3 ElbowAt(Vector3 S, Vector3 dir, float along, float off, Vector3 hang, float swivel)
            => S + dir * along + (Quaternion.AngleAxis(swivel, dir) * hang) * off;

        /// <summary>How far an elbow is out past its own wrist in the direction
        /// the hand reached from the shoulder (metres; above BackBend the arm
        /// is bent backwards).</summary>
        static float PastWrist(Vector3 S, Vector3 E, Vector3 W)
        {
            float lat = W.x - S.x;
            float k = Planner.Smooth(Mathf.Clamp01((Mathf.Abs(lat) - 0.02f) / 0.04f));   // (no step: a step flipped the swing)
            return (E.x - W.x) * Mathf.Sign(lat) * k;
        }

        /// <summary>PastWrist for a hand reaching ACROSS the body's middle
        /// (the owner's backward bend: a right hand at 10 o'clock), in from
        /// 6 cm short of it; a hand on its own side (low by the door, at the
        /// lever) swings its elbow as the cabin needs.</summary>
        float BentAcross(bool left, Vector3 S, Vector3 E, Vector3 W)
        {
            float past = left ? W.x - Eye.x : Eye.x - W.x;
            float k = Planner.Smooth(Mathf.Clamp01((past + 0.06f) / 0.06f));
            return k > 0f ? PastWrist(S, E, W) * k : 0f;
        }

        /// <summary>How deep an arm laid shoulder-elbow-wrist is in the door,
        /// console, seat or rim (metres, the bones' centre lines at the arm's
        /// radius; the hand's last few cm left to the hand's own fit).</summary>
        float ChainDepth(Vector3 S, Vector3 E, Vector3 W)
        {
            float worst = 0f;
            var cab = data.cabin;
            Matrix4x4 toW = data.wheel != null ? WheelMatrix.inverse : Matrix4x4.identity;
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f;
                Vector3 qu = Vector3.Lerp(E, S, t * 0.8f);           // the upper arm (its shoulder end hidden)
                Vector3 qf = Vector3.Lerp(E, W, 0.10f + t * 0.82f);  // the forearm, to just short of the wrist
                if (cab != null)
                {
                    worst = Mathf.Max(worst, SkinDepth(cab, qu));
                    worst = Mathf.Max(worst, SkinDepth(cab, qf));
                }
                if (data.wheel != null) worst = Mathf.Max(worst, ArmRadius - data.wheel.Sdf(toW.MultiplyPoint3x4(qf)));
            }
            return worst;
        }

        /// <summary>A bone point's depth in the cabin at the arm's radius:
        /// along each wall's own axis (Depth's margin), and the skin below and
        /// either side of it - a wall is a heightfield with an edge (the door's
        /// sill), and a bone just over the edge read clear while the arm's
        /// underside was in the door.</summary>
        static float SkinDepth(Cabin cab, Vector3 q)
        {
            float d = cab.Depth(q, out _, ArmRadius);
            d = Mathf.Max(d, cab.Depth(q + Vector3.down * ArmRadius, out _, CabinMargin));
            d = Mathf.Max(d, cab.Depth(q + Vector3.left * ArmRadius, out _, CabinMargin));
            d = Mathf.Max(d, cab.Depth(q + Vector3.right * ArmRadius, out _, CabinMargin));
            return d;
        }

        static readonly float[] SwingTry = { 0f, 12f, -12f, 25f, -25f, 40f, -40f, 60f, -60f, 85f, -85f, 110f, -110f, 135f, -135f, 180f };
        static readonly float[] LiftTry = { 0f, 12f, 25f, 40f, 60f, 85f, 110f };

        /// <summary>The arm's chain solved: the elbow where the pole hangs it,
        /// swung round the shoulder-to-wrist line (from where it was) until
        /// it is clear of the cabin and the rim, never faster than SwivelRate.</summary>
        void Reach(Arm a, bool snap, float dt)
        {
            Chain(a, out var S, out var W, out var dir, out float along, out float off, out var hang);
            hang = Ease(a, hang, dir, snap, dt);
            a.hang = hang;
            float best = a.swivel, bestCost = float.MaxValue, now = 0f;
            for (int i = 0; i < SwingTry.Length; i++)
            {
                float sw = SwingTry[i];
                Vector3 E = ElbowAt(S, dir, along, off, hang, sw);
                float depth = data.cabin != null || data.wheel != null ? ChainDepth(S, E, W) : 0f;
                float bent = BentAcross(a.hand.left, S, E, W);
                float cost = depth * 1000f + Mathf.Max(0f, bent - BackBend) * 2000f
                           + Mathf.Abs(sw) * 0.02f + Mathf.Abs(sw - a.swivel) * 0.01f;
                if (cost < bestCost) { bestCost = cost; best = sw; }
                if (depth <= 0f && i == 0 && bent <= BackBend) break;   // the pole's own hang is clear
            }
            if (data.cabin != null || data.wheel != null) now = ChainDepth(S, ElbowAt(S, dir, along, off, hang, a.swivel), W);
            if (snap || !a.chained) { a.lift = 0f; a.liftSgn = 0f; }
            a.swivel = snap || !a.chained ? best : Mathf.MoveTowards(a.swivel, best, (now > 0f ? SwivelRateOut : SwivelRateBack) * dt);
            a.shoulder = S;
            a.wrist = W;
            a.elbow = ElbowAt(S, dir, along, off, hang, a.swivel + a.lift);
            a.chained = true;
        }

        /// <summary>The closest the two arms' bones come (centre lines; the
        /// forearms from a few cm off the wrist, so two hands near each other
        /// on the rim do not count).</summary>
        static float Gap(Vector3 sA, Vector3 eA, Vector3 wA, Vector3 sB, Vector3 eB, Vector3 wB)
        {
            Vector3 fA = Vector3.Lerp(wA, eA, 0.40f), fB = Vector3.Lerp(wB, eB, 0.40f);
            float g = SegDist(fA, eA, fB, eB);
            g = Mathf.Min(g, SegDist(fA, eA, eB, sB));
            g = Mathf.Min(g, SegDist(eA, sA, fB, eB));
            return g;
        }

        /// <summary>For the shot tool's trace: what the last untangle did,
        /// and what the last resort (KeepOutOfCabin) pushed out of.</summary>
        public string LastUntangle = "", LastKeep = "";
        /// <summary>The shot tool's switch for those (strings every frame are
        /// garbage in the game).</summary>
        public static bool TraceSolve;

        /// <summary>The two arms apart: the higher hand's swings its elbow up
        /// and over until the bones clear.</summary>
        void Untangle(bool snap, float dt)
        {
            Arm A = arms[0], B = arms[1];
            LastUntangle = "";
            Arm m = A.wrist.y >= B.wrist.y ? A : B;
            Arm o = m == A ? B : A;
            Settle(o, m, dt);   // the other lets down a lift it held
            Chain(m, out var S, out var W, out var dir, out float along, out float off, out _);
            Vector3 hang = m.hang;
            if (Gap(S, m.elbow, W, o.shoulder, o.elbow, o.wrist) >= LiftClear) { Settle(m, o, dt); return; }
            // only the way that LIFTS the elbow (up and over), chosen once:
            // either way each frame flipped it, a snap
            if (m.liftSgn == 0f)
            {
                float up = ElbowAt(S, dir, along, off, hang, m.swivel + 12f).y - ElbowAt(S, dir, along, off, hang, m.swivel - 12f).y;
                m.liftSgn = up >= 0f ? 1f : -1f;
            }
            float best = m.lift, bestCost = float.MaxValue;
            for (int i = 0; i < LiftTry.Length; i++)
            {
                float lf = m.liftSgn * LiftTry[i];
                Vector3 E = ElbowAt(S, dir, along, off, hang, m.swivel + lf);
                float gap = Gap(S, E, W, o.shoulder, o.elbow, o.wrist);
                float depth = ChainDepth(S, E, W);
                float cost = Mathf.Max(0f, LiftClear - gap) * 1000f + depth * 1000f
                           + Mathf.Max(0f, BentAcross(m.hand.left, S, E, W) - BackBend) * 2000f + LiftTry[i] * 0.01f;
                if (cost < bestCost) { bestCost = cost; best = lf; }
            }
            m.lift = snap ? best : Mathf.MoveTowards(m.lift, best, SwivelRateOut * dt);
            m.elbow = ElbowAt(S, dir, along, off, hang, m.swivel + m.lift);
            if (TraceSolve) LastUntangle = (m.hand.left ? "L" : "R") + " lifts " + m.lift.ToString("0") + " (best " + best.ToString("0") + ")";
        }

        /// <summary>A lift let down (SwivelRateBack) only as far as the arms
        /// stay clear.</summary>
        void Settle(Arm a, Arm o, float dt)
        {
            if (a.lift == 0f) { a.liftSgn = 0f; return; }
            Chain(a, out var S, out var W, out var dir, out float along, out float off, out _);
            float down = Mathf.MoveTowards(a.lift, 0f, SwivelRateBack * dt);
            Vector3 E = ElbowAt(S, dir, along, off, a.hang, a.swivel + down);
            if (Gap(S, E, W, o.shoulder, o.elbow, o.wrist) < LiftClear) return;
            a.lift = down;
            a.elbow = E;
            if (a.lift == 0f) a.liftSgn = 0f;
        }

        static float SegDist(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
            => SegDist((Vector3)p1, (Vector3)q1, (Vector3)p2, (Vector3)q2);

        static float SegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-9f && e <= 1e-9f) return r.magnitude;
            if (a <= 1e-9f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-9f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-9f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        /// <summary>For the shot tool: the arms' chains now (model frame) -
        /// shoulder joint, elbow, wrist, and where the upper arm's drawn
        /// shoulder end lands - and the wrist's bend (hand vs forearm, degrees).</summary>
        public void ChainNow(bool left, out Vector3 shoulder, out Vector3 elbow, out Vector3 wrist, out Vector3 upperEnd, out float wristBend)
        {
            var a = arms[left ? 0 : 1];
            Vector3 eL = ElbowL(left);
            elbow = a.forePos + a.foreRot * eL;
            wrist = a.pos + a.rot * a.geo.wrist;
            shoulder = Shoulders ? a.shoulder : Eye + Side(left, Shoulder);
            upperEnd = elbow + a.upper.localRotation * (EndL(left) - eL);
            wristBend = Vector3.Angle(a.rot * Vector3.forward, wrist - elbow);
        }

        /// <summary>For the shot tool's trace: an arm's elbow swing (degrees).</summary>
        public float ChainSwivel(bool left)
        {
            var a = arms[left ? 0 : 1];
            return a.swivel + a.lift;
        }

        /// <summary>For the shot tool: the worst elbow out past its wrist now
        /// with the hand reaching across (metres) - what the chain guards
        /// (BentAcross; a hand low by its own knee may tuck its elbow in).</summary>
        public float BentBack()
        {
            float worst = 0f;
            for (int i = 0; i < 2; i++)
            {
                ChainNow(i == 0, out var s, out var e, out var w, out _, out _);
                worst = Mathf.Max(worst, BentAcross(i == 0, s, e, w));
            }
            return worst;
        }

        /// <summary>For the shot tool: the closest the two arms' bones come now.</summary>
        public float ArmGap()
        {
            ChainNow(true, out var sA, out var eA, out var wA, out _, out _);
            ChainNow(false, out var sB, out var eB, out var wB, out _, out _);
            return Gap(sA, eA, wA, sB, eB, wB);
        }

        // ------------------------------------------------------------------
        //  Contact: fingers curl to the wheel, the hand stays outside it
        // ------------------------------------------------------------------

        /// <summary>A finger vertex's curl delta at curl c (0..1 of GRIP).</summary>
        Vector3 CurlDelta(Finger fg, int j, float c, bool normal = false)
        {
            var L = data.levels;
            int top = L.Length - 1;
            if (c >= L[top]) return Vector3.zero;
            int k = 0;
            while (k < top - 1 && c >= L[k + 1]) k++;
            var arr = normal ? fg.dNrm : fg.dPos;
            Vector3 a0 = arr[k] != null ? arr[k][j] : Vector3.zero, a1 = arr[k + 1] != null ? arr[k + 1][j] : Vector3.zero;
            return Vector3.Lerp(a0, a1, (c - L[k]) / Mathf.Max(1e-4f, L[k + 1] - L[k]));
        }

        bool FingerClear(Arm a, int f, float c, Matrix4x4 M, float wc, float clearAt)
        {
            var fg = a.geo.fingers[f];
            for (int j = 0; j < fg.ids.Length; j++)
            {
                int i = fg.ids[j];
                if (data.wheel.Sdf(M.MultiplyPoint3x4(a.loc[i] + CurlDelta(fg, j, c) * wc)) < clearAt) return false;
            }
            return true;
        }

        /// <summary>Curl each finger until it meets the wheel; then lift the
        /// whole hand out along the wheel's surface if any of it is still in.</summary>
        void Fit(Arm a)
        {
            var g = a.geo;
            var h = a.hand;
            var w = data.wheel;
            float wo = Mathf.Clamp01(h.open), wr = Mathf.Clamp01(h.relax), wc = Mathf.Clamp01(1f - wo - wr);
            Vector3[] dpo = g.dPos[0], dpr = g.dPos[1];
            for (int i = 0; i < g.pos.Length; i++)
                a.loc[i] = g.isHand[i] ? g.pos[i] + dpo[i] * wo + dpr[i] * wr : g.pos[i];
            float clearAt = data.gap * 0.6f;
            Matrix4x4 W = WheelMatrix, toW = W.inverse;
            for (int iter = 0; iter < 8; iter++)
            {
                Matrix4x4 M = toW * Matrix4x4.TRS(a.pos, a.rot, Vector3.one);
                for (int f = 0; f < 5; f++)
                {
                    // The FURTHEST clear curl, down from the full grip: a finger
                    // half curled can sweep through what the wrapped one clears
                    // (a fingertip behind the wheel, by a spoke's root).
                    float c = 1f;
                    if (wc >= 0.02f && !FingerClear(a, f, 1f, M, wc, clearAt))
                    {
                        float hi = 1f, lo = -1f;
                        for (float t = 0.95f; t >= -1e-4f; t -= 0.05f)
                        {
                            if (FingerClear(a, f, Mathf.Max(0f, t), M, wc, clearAt)) { lo = Mathf.Max(0f, t); break; }
                            hi = t;
                        }
                        if (lo < 0f) c = 0f;
                        else
                        {
                            for (int k = 0; k < 4; k++)
                            {
                                float m = (lo + hi) * 0.5f;
                                if (FingerClear(a, f, m, M, wc, clearAt)) lo = m; else hi = m;
                            }
                            c = lo;
                        }
                    }
                    a.curl[f] = c;
                }
                float worst = 0f;
                Vector3 at = Vector3.zero;
                for (int i = 0; i < g.pos.Length; i++)
                {
                    if (!g.isHand[i]) continue;
                    Vector3 p = a.loc[i];
                    int f = g.fingerOf[i];
                    if (f >= 0 && wc > 0f)
                    {
                        p += CurlDelta(g.fingers[f], g.fingerSlot[i], a.curl[f]) * wc;
                    }
                    Vector3 q = M.MultiplyPoint3x4(p);
                    float d = clearAt - w.Sdf(q);
                    if (d > worst) { worst = d; at = q; }
                }
                if (worst <= 1e-4f) break;
                // Out of the rim along its surface (fingers wrap it); out of the
                // spokes and the pad toward the driver - a hand in the air or
                // open on the wheel stays on the driver's side of them (their
                // nearest face can be the dash side, which would sink it through).
                bool onRim = w.Part(at) == 0;
                // (The rim's own surface even in the air: a relaxed hand let go
                // points its fingers at the rim, and straight back slides along
                // them without clearing them.)
                Vector3 dir = onRim ? w.Normal(at) : Vector3.back;
                a.pos += W.MultiplyVector(dir) * (worst + 0.0005f);
            }
        }

        /// <summary>Clear of the cabin by this, metres (door, console, seat).</summary>
        public const float CabinMargin = 0.005f;

        /// <summary>The forearm out of the door, console and seat: turned at
        /// the wrist (the hand keeps its grip) until its deepest point is a
        /// margin clear, a few passes.</summary>
        void KeepOutOfCabin(Arm a)
        {
            var g = a.geo;
            var cab = data.cabin;
            Matrix4x4 W = WheelMatrix, toW = W.inverse;
            Vector3 J = g.wrist;
            Vector3 jw = a.pos + a.rot * J;
            for (int iter = 0; iter < 10; iter++)
            {
                Vector3 fp = jw - a.foreRot * J;
                float worst = 0f;
                Vector3 at = Vector3.zero, n = Vector3.zero;
                string what = "";
                // The upper arm follows its elbow to the shoulder (as Place).
                Vector3 elbowL = a.hand.left ? MX(data.elbow) : data.elbow;
                Vector3 e = fp + a.foreRot * elbowL;
                Vector3 want = (Shoulders && a.chained ? a.shoulder : Eye + Side(a.hand.left, Shoulder)) - e;
                Quaternion ur = (want.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(a.foreRot * Vector3.back, want.normalized) : Quaternion.identity) * a.foreRot;
                for (int i = 0; i < g.pos.Length; i++)
                {
                    if (g.handW[i] > 0.5f) continue;
                    Vector3 q = g.upper[i] > 0.5f ? e + ur * (g.pos[i] - elbowL) : fp + a.foreRot * g.pos[i];
                    float need = cab.Depth(q, out var o, CabinMargin);
                    if (need > worst) { worst = need; at = q; n = o; what = g.upper[i] > 0.5f ? "cabin/upper" : "cabin/fore"; }
                    // ... and out of the wheel (the rim under a turned wrist).
                    if (data.wheel != null && g.upper[i] < 0.5f)
                    {
                        Vector3 qw = toW.MultiplyPoint3x4(q);
                        float dw = CabinMargin - data.wheel.Sdf(qw);
                        if (dw > worst) { worst = dw; at = q; n = W.MultiplyVector(data.wheel.Normal(qw)); what = "rim"; }
                    }
                }
                if (worst <= 1e-4f) break;
                if (iter == 0 && TraceSolve) LastKeep += $"{(a.hand.left ? "L" : "R")} {what} {worst * 1000f:0}mm at {(at - jw).magnitude * 100f:0}cm from wrist (chain depth {ChainDepth(a.shoulder, a.elbow, a.wrist) * 1000f:0}mm) ";
                Vector3 r = at - jw;
                Vector3 axis = Vector3.Cross(r, n);
                if (axis.sqrMagnitude < 1e-10f || r.sqrMagnitude < 1e-6f) break;
                float ang = Mathf.Rad2Deg * (worst / r.magnitude);
                a.foreRot = Quaternion.AngleAxis(Mathf.Min(ang * 1.1f, 30f), axis.normalized) * a.foreRot;
            }
        }

        /// <summary>The forearm and upper arm's depth in the cabin walls now
        /// (metres; 0 = outside), on the drawn vertices.</summary>
        public float CabinDepth()
        {
            if (data.cabin == null) return 0f;
            float worst = 0f;
            foreach (var a in arms)
            {
                var g = a.geo;
                for (int i = 0; i < a.outP.Length; i++)
                {
                    if (g.handW[i] > 0.5f) continue;
                    worst = Mathf.Max(worst, data.cabin.Depth(a.outP[i], out _));
                }
            }
            return worst;
        }

        /// <summary>How deep the arms are in the wheel now (metres; 0 =
        /// outside): the fingers (thumb included), the whole hand, and the rest
        /// of the forearm - measured on the skinned vertices as drawn.</summary>
        public void Depths(out float fingers, out float hand, out float forearm)
        {
            fingers = hand = forearm = 0f;
            if (data.wheel == null) return;
            Matrix4x4 toW = WheelMatrix.inverse;
            foreach (var a in arms)
            {
                var g = a.geo;
                for (int i = 0; i < a.outP.Length; i++)
                {
                    if (g.upper[i] >= 0.5f) continue;
                    float d = -data.wheel.Sdf(toW.MultiplyPoint3x4(a.outP[i]));
                    if (d <= 0f) continue;
                    if (g.fingerOf[i] >= 0) fingers = Mathf.Max(fingers, d);
                    if (g.isHand[i]) hand = Mathf.Max(hand, d);
                    else forearm = Mathf.Max(forearm, d);
                }
            }
        }

        /// <summary>For the self-test: the deepest finger in the wheel now -
        /// which hand, what of the wheel, what the hand is doing.</summary>
        public string DeepestFinger()
        {
            if (data.wheel == null) return "";
            Matrix4x4 toW = WheelMatrix.inverse;
            float worst = 0f; string where = "";
            foreach (var a in arms)
            {
                var g = a.geo;
                for (int i = 0; i < a.outP.Length; i++)
                {
                    if (g.fingerOf[i] < 0) continue;
                    Vector3 q = toW.MultiplyPoint3x4(a.outP[i]);
                    float d = -data.wheel.Sdf(q);
                    if (d <= worst) continue;
                    worst = d;
                    var h = a.hand;
                    where = $"{(h.left ? "L" : "R")} {data.wheel.Which(q)} {d * 1000f:0.0}mm: {h.mode}/{h.phase} t {h.t:0.00} a {h.alpha:0} to {h.to:0} open {h.open:0.00} relax {h.relax:0.00}";
                }
            }
            return where;
        }

        /// <summary>A hand's finger curls now (index .. thumb; 1 = GRIP).</summary>
        public float[] Curls(bool left) => arms[left ? 0 : 1].curl;

        /// <summary>For reports: what stops each finger curling further (the
        /// wheel part nearest its closest vertex), or "free".</summary>
        public string Stoppers(bool left)
        {
            var a = arms[left ? 0 : 1];
            if (data.wheel == null || a.geo.fingers == null) return "";
            Matrix4x4 M = WheelMatrix.inverse * Matrix4x4.TRS(a.pos, a.rot, Vector3.one);
            var sb = new System.Text.StringBuilder();
            for (int f = 0; f < a.geo.fingers.Length; f++)
            {
                var fg = a.geo.fingers[f];
                float c = Mathf.Min(1f, a.curl[f] + 0.1f), best = 9f;
                string who = "free";
                for (int j = 0; j < fg.ids.Length; j++)
                {
                    var q = M.MultiplyPoint3x4(a.loc[fg.ids[j]] + CurlDelta(fg, j, c));
                    float d = data.wheel.Sdf(q);
                    if (d < best)
                    {
                        best = d;
                        who = d < data.gap ? data.wheel.Which(q) + string.Format("@{0:0}deg/r{1:0.000}/z{2:+0.000;-0.000}",
                              Mathf.Repeat(Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg - Plan.Theta * 0f, 360f), new Vector2(q.x, q.y).magnitude, q.z) : "free";
                    }
                }
                sb.Append(fg.name).Append('=').Append(who).Append(' ');
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>For shots: where a hand holds the rim (the rim's tube
        /// centre under its palm), the rim tangent there and the wheel's face
        /// normal toward the driver, model frame.</summary>
        public void GripFrame(bool left, out Vector3 at, out Vector3 tangent, out Vector3 radialOut)
        {
            var h = left ? Plan.L : Plan.R;
            at = RimPoint(h.alpha);
            tangent = RimTangent(h.alpha);
            radialOut = (at - lay.wheel.pivot - Vector3.Project(at - lay.wheel.pivot, lay.wheel.rot * Vector3.forward)).normalized;
        }

        void Place(Arm a)
        {
            bool left = a.hand.left;
            if (Shoulders && a.chained) { PlaceChain(a); return; }
            // The forearm hangs from the hand's wrist joint.
            Vector3 J = a.geo.wrist;
            a.forePos = a.pos + a.rot * J - a.foreRot * J;
            a.fore.localPosition = a.forePos;
            a.fore.localRotation = a.foreRot;
            Vector3 elbowL = left ? MX(data.elbow) : data.elbow;
            Vector3 e = a.forePos + a.foreRot * elbowL;
            Vector3 s = Eye + Side(left, Shoulder);
            Vector3 back = a.foreRot * Vector3.back;
            Vector3 want = s - e;
            Quaternion bend = want.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(back, want.normalized) : Quaternion.identity;
            a.upper.localPosition = e;
            a.upper.localRotation = bend * a.foreRot;
            Skin(a, Matrix4x4.TRS(a.pos, a.rot, Vector3.one), Matrix4x4.TRS(a.forePos, a.foreRot, Vector3.one),
                 Matrix4x4.TRS(e, a.upper.localRotation, Vector3.one) * Matrix4x4.Translate(-elbowL));
        }

        /// <summary>The forearm's turn for the chain's elbow (rolled with the hand).</summary>
        void AimFore(Arm a)
        {
            Vector3 J = a.geo.wrist, eL = ElbowL(a.hand.left);
            Vector3 W = a.pos + a.rot * J;
            Vector3 Zf = W - a.elbow;
            Zf = Zf.sqrMagnitude > 1e-8f ? Zf.normalized : a.rot * Vector3.forward;
            Vector3 palm = a.rot * Vector3.up;
            Vector3 Yh = palm - Vector3.Dot(palm, Zf) * Zf;
            if (Yh.sqrMagnitude < 1e-6f) Yh = Vector3.Cross(Zf, a.rot * Vector3.right);
            Quaternion offFore = Quaternion.FromToRotation(Vector3.forward, (J - eL).normalized);
            a.foreRot = Quaternion.LookRotation(Zf, Yh.normalized) * Quaternion.Inverse(offFore);
        }

        /// <summary>The chain's bones: the forearm from the hand's wrist to the
        /// solved elbow (rolled with the hand: the forearm turns as the hand
        /// does), the upper arm from the elbow to the shoulder.</summary>
        void PlaceChain(Arm a)
        {
            bool left = a.hand.left;
            Vector3 J = a.geo.wrist, eL = ElbowL(left), endL = EndL(left);
            Vector3 W = a.pos + a.rot * J;
            // the arm frame's own forearm line (elbow pivot -> wrist ring) onto wrist - elbow
            AimFore(a);
            a.forePos = W - a.foreRot * J;
            a.fore.localPosition = a.forePos;
            a.fore.localRotation = a.foreRot;
            Vector3 e = a.forePos + a.foreRot * eL;
            Vector3 want = a.shoulder - e;
            Vector3 dirU = a.foreRot * (endL - eL).normalized;
            Quaternion bend = want.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(dirU, want.normalized) : Quaternion.identity;
            a.upper.localPosition = e;
            a.upper.localRotation = bend * a.foreRot;
            Skin(a, Matrix4x4.TRS(a.pos, a.rot, Vector3.one), Matrix4x4.TRS(a.forePos, a.foreRot, Vector3.one),
                 Matrix4x4.TRS(e, a.upper.localRotation, Vector3.one) * Matrix4x4.Translate(-eL));
        }

        /// <summary>Three bones (hand, forearm, upper arm) and two shapes, by
        /// hand (in the arm root's frame).</summary>
        void Skin(Arm a, Matrix4x4 H, Matrix4x4 F, Matrix4x4 U)
        {
            float wo = Mathf.Clamp01(a.hand.open), wr = Mathf.Clamp01(a.hand.relax);
            bool same = true;
            for (int f = 0; f < 5; f++) same &= Mathf.Abs(a.curl[f] - a.doneCurl[f]) < 1e-3f;
            if (same && Mathf.Abs(wo - a.doneOpen) < 1e-3f && Mathf.Abs(wr - a.doneRelax) < 1e-3f && F == a.doneF && U == a.doneU && H == a.doneH) return;
            a.doneOpen = wo; a.doneRelax = wr; a.doneF = F; a.doneU = U; a.doneH = H;
            for (int f = 0; f < 5; f++) a.doneCurl[f] = a.curl[f];
            var g = a.geo;
            Vector3[] p0 = g.pos, n0 = g.nrm, dpo = g.dPos[0], dno = g.dNrm[0], dpr = g.dPos[1], dnr = g.dNrm[1];
            float[] up = g.upper, hw = g.handW;
            for (int i = 0; i < p0.Length; i++)
            {
                Vector3 p = p0[i] + dpo[i] * wo + dpr[i] * wr;
                Vector3 q = n0[i] + dno[i] * wo + dnr[i] * wr;
                float u = up[i], h = hw[i];
                if (h >= 0.999f) { a.outP[i] = H.MultiplyPoint3x4(p); a.outN[i] = H.MultiplyVector(q); }
                else if (u <= 0.001f && h <= 0.001f) { a.outP[i] = F.MultiplyPoint3x4(p); a.outN[i] = F.MultiplyVector(q); }
                else if (u >= 0.999f) { a.outP[i] = U.MultiplyPoint3x4(p); a.outN[i] = U.MultiplyVector(q); }
                else
                {
                    float f = 1f - u - h;
                    a.outP[i] = F.MultiplyPoint3x4(p) * f + U.MultiplyPoint3x4(p) * u + H.MultiplyPoint3x4(p) * h;
                    a.outN[i] = F.MultiplyVector(q) * f + U.MultiplyVector(q) * u + H.MultiplyVector(q) * h;
                }
            }
            // The fingers' own curls (Contact), on top of the grip base.
            float wc = Mathf.Clamp01(1f - wo - wr);
            if (wc > 0f && g.fingers != null)
                for (int f = 0; f < g.fingers.Length; f++)
                {
                    if (a.curl[f] >= 0.999f) continue;
                    var fg = g.fingers[f];
                    for (int j = 0; j < fg.ids.Length; j++)
                    {
                        int i = fg.ids[j];
                        a.outP[i] += H.MultiplyVector(CurlDelta(fg, j, a.curl[f]) * wc);
                        a.outN[i] = (a.outN[i] + H.MultiplyVector(CurlDelta(fg, j, a.curl[f], true) * wc)).normalized;
                    }
                }
            a.mesh.vertices = a.outP;
            a.mesh.normals = a.outN;
        }

        /// <summary>For the self-test and shots: a hand's forearm pose and
        /// elbow in the model frame.</summary>
        public void Probe(bool left, out Vector3 grip, out Vector3 elbow, out Transform fore)
        {
            var a = arms[left ? 0 : 1];
            fore = a.fore;
            grip = a.pos;
            elbow = a.forePos + a.foreRot * (left ? MX(data.elbow) : data.elbow);
        }
    }
}
