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
    /// scene): grip LOW at ~8:30 and ~3:30 and ride the rim for small inputs.
    /// A big turn goes hand over hand as the clip does: the hand carried down
    /// lets the rim slide through it at ~5 o'clock; when the hand carried up
    /// reaches the top, the low one comes up and CROSSES OVER to grab at ~11
    /// palm down; the top hand then goes OPEN, slides on along the rim and
    /// drops back to its own side. A fast unwind spins the wheel through
    /// loose open hands, which then re-grip at rest. The INBOARD hand leaves
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
        /// hand on it (and crossed arms make an X in the picture).</summary>
        public static readonly Vector3 ElbowAimHigh = new Vector3(-0.11f, -0.42f, 0.10f);
        /// <summary>The crossing arm's aim: its elbow comes up and back, so its
        /// forearm passes OVER the other one.</summary>
        public static readonly Vector3 CrossLift = new Vector3(0.02f, 0.07f, -0.08f);
        /// <summary>The arm under a crossing one ducks a little.</summary>
        public static readonly Vector3 UnderDuck = new Vector3(0f, -0.06f, 0.04f);
        /// <summary>The upper arm aims at the shoulder: low and back, so the
        /// upper arm stays under the frame even at full look-over; inboard of the
        /// door trim (the cabin is narrower than a pair of shoulders).</summary>
        public static readonly Vector3 Shoulder = new Vector3(-0.13f, -0.36f, -0.06f);
        /// <summary>Where the inboard hand goes: the shifter knob and the
        /// handbrake lever (out of frame below the console), RIGHT arm.</summary>
        public static readonly Vector3 ShiftKnob = new Vector3(0.33f, -0.52f, 0.30f);
        public static readonly Vector3 BrakeLever = new Vector3(0.29f, -0.56f, 0.10f);
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
        public enum Kind { None, Cross, Return, Home, Reach }

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
            /// <summary>A Move: elapsed, length, the rim path (from -> to; a
            /// Return slides from -> slideTo, then drops through the air to `to`).</summary>
            public float t, dur, from, to, slideTo;
            /// <summary>Where a flight wants to land (world clock angle) before
            /// it is moved off the spokes (`to`, re-aimed until late in the flight).</summary>
            public float want;
            /// <summary>Bumped when a straight-line phase starts (the solver
            /// snapshots its last pose there).</summary>
            public int cartGen;
            /// <summary>Came over the top of the other arm (aims high).</summary>
            public bool crossed;
            /// <summary>Away: 0 release, 1 drop, 2 hold, 3 back; a Return: 0
            /// slide, 1 drop. brake: the handbrake (else the shifter).</summary>
            public int phase;
            public bool brake;
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
            /// <summary>In each hand's own angle (beta: 180 = 9 o'clock for the
            /// left hand and 3 o'clock for the right, smaller = up):</summary>
            public const float TopBeta = 92f, CrossBeta = 58f, OverBeta = 30f, BottomBeta = 255f;
            /// <summary>The door-side hand (the model's left) lets the rim slide
            /// through it sooner: below ~8 o'clock its wrist would be in the
            /// door trim (the cabin walls, measured).</summary>
            public const float BottomBetaDoor = 205f;
            public static float Bottom(Hand h) => h.left ? BottomBetaDoor : BottomBeta;
            public const float UnwindMin = 50f, UnwindRate = 260f, UnwindExit = 120f, SlideRate = 300f;
            public const float CrossSec = 0.24f, HomeSec = 0.28f, ReturnSlideDeg = 35f, ReturnSlideSec = 0.14f, ReturnDropSec = 0.26f;
            public const float ReleaseSec = 0.15f, DropSec = 0.25f, HoldSec = 0.3f, BackSec = 0.3f;
            public const float CalmRate = 45f, CalmSec = 0.3f, HomeMaxTheta = 60f, HomeSlack = 20f;
            public const float PoseTau = 0.05f;

            public readonly Hand L = new Hand(true), R = new Hand(false);
            public float Theta, Omega;
            public bool Unwinding;
            /// <summary>For the self-test: crossings made, reaches started.</summary>
            public int Crossings, Reaches, Unwinds;
            float lastTheta = float.NaN, calm;

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
            Hand Other(Hand h) => h == L ? R : L;
            static bool OnRim(Hand h) => h.mode == Mode.Grip || h.mode == Mode.Slide;

            /// <summary>Both hands gripping at rest on a wheel at `theta`.</summary>
            public void Reset(float theta)
            {
                foreach (var h in new[] { L, R })
                {
                    h.mode = Mode.Grip; h.kind = Kind.None; h.crossed = false;
                    h.alpha = RestAlpha(h.left) + theta;
                    h.phi = RestAlpha(h.left);
                    h.open = h.relax = h.openT = h.relaxT = 0f;
                }
                Theta = theta; Omega = 0f; lastTheta = theta; Unwinding = false; calm = 0f;
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
                if (!Unwinding)
                {
                    Unwinding = Mathf.Abs(theta) > UnwindMin && theta * Omega < 0f && Mathf.Abs(Omega) > UnwindRate;
                    if (Unwinding) Unwinds++;
                }
                else if (Mathf.Abs(Omega) < UnwindExit || Mathf.Abs(theta) < 12f) Unwinding = false;

                StepAway(dt, shift, brake);
                StepHand(L, dt);
                StepHand(R, dt);

                // HAND OVER HAND: the hand carried up reached the top - the
                // low one crosses over it. Only winding ON (away from centre):
                // the way back is the unwind's, through loose hands.
                if (!Unwinding && Mathf.Abs(Omega) > 30f && (theta * Omega > 0f || Mathf.Abs(theta) < UnwindMin) && OnRim(L) && OnRim(R))
                {
                    Hand lead = Omega < 0f ? L : R, trail = Other(lead);
                    if (lead.mode == Mode.Grip && BetaOf(lead) <= TopBeta && !trail.crossed) StartCross(trail);
                }
                // Calm again: back to 8 and 4, one hand at a time.
                if (!Unwinding && calm > CalmSec && L.mode == Mode.Grip && R.mode == Mode.Grip && Mathf.Abs(theta) < HomeMaxTheta)
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
                if (h.crossed && (h.mode == Mode.Grip || h.mode == Mode.Slide) && BetaOf(h) > 140f) h.crossed = false;
                switch (h.mode)
                {
                    case Mode.Grip:
                    {
                        h.alpha = h.phi + Theta;
                        h.openT = 0f; h.relaxT = 0f;
                        float b = BetaOf(h);
                        if (Unwinding) { h.mode = Mode.Slide; h.openT = 1f; }
                        else if (b > Bottom(h)) { h.mode = Mode.Slide; h.alpha = AlphaOf(h.left, Bottom(h)); h.relaxT = 0.7f; h.crossed = false; }
                        else if (b < OverBeta) { h.mode = Mode.Slide; h.alpha = AlphaOf(h.left, OverBeta); h.openT = 0.6f; }
                        break;
                    }
                    case Mode.Slide:
                    {
                        if (Unwinding)
                        {
                            // The rim spins through an open hand drifting home.
                            float rest = h.alpha + Mathf.DeltaAngle(h.alpha, RestAlpha(h.left));
                            h.alpha = Mathf.MoveTowards(h.alpha, rest, SlideRate * dt);
                            h.openT = 1f; h.relaxT = 0f;
                            break;
                        }
                        float b = BetaOf(h), db = h.left ? Omega : -Omega;
                        bool bottom = b >= Bottom(h) - 0.5f, top = b <= OverBeta + 0.5f;
                        if ((bottom && db < -5f) || (top && db > 5f) || (!bottom && !top)) Grip(h, h.alpha);
                        else { h.openT = top ? 0.6f : 0f; h.relaxT = bottom ? 0.7f : 0f; }
                        break;
                    }
                    case Mode.Move:
                    {
                        h.t += dt;
                        float u = Mathf.Clamp01(h.t / Mathf.Max(1e-3f, h.dur));
                        // A flight keeps its landing off the spokes where the wheel
                        // will be (re-aimed until 70% of the way, then held).
                        if (h.kind == Kind.Cross && u < 0.7f)
                            h.to = SnapAlphaAt(h.left, h.want, h.dur - h.t);
                        else if (h.kind == Kind.Return && h.phase == 1)
                        {
                            float ud = (h.t - ReturnSlideSec) / ReturnDropSec;
                            if (ud < 0.7f) h.to = SnapAlphaAt(h.left, h.want, ReturnSlideSec + ReturnDropSec - h.t);
                        }
                        if (h.kind == Kind.Cross)
                        {
                            // A loose hand in the air, closing round the rim as it lands.
                            h.relaxT = u < 0.6f ? 0.6f : 0f;
                            h.openT = 0f;
                        }
                        else if (h.kind == Kind.Return)
                        {
                            float slide = Mathf.Clamp01(h.t / ReturnSlideSec);
                            h.alpha = Mathf.Lerp(h.from, h.slideTo, Smooth(slide));
                            if (slide >= 1f && h.phase == 0)
                            {
                                h.phase = 1; h.cartGen++;
                                h.want = h.to;
                                h.to = SnapAlphaAt(h.left, h.want, ReturnDropSec);
                            }
                            // Open while it slides; closing as it drops to the rim.
                            h.openT = h.phase == 0 ? 1f : 0f;
                            h.relaxT = 0f;
                        }
                        else
                        {
                            h.alpha = Mathf.Lerp(h.from, h.to, Smooth(u));
                            h.relaxT = 0.6f * Mathf.Sin(Mathf.PI * u);
                            h.openT = 0f;
                        }
                        if (u >= 1f)
                        {
                            Kind was = h.kind;
                            // Where it flew to: the target off the spokes (the solver's too).
                            Grip(h, h.to);
                            if (was == Kind.Cross)
                            {
                                Crossings++;
                                // The top hand lets go: open, on along the rim, home.
                                var o = Other(h);
                                if (OnRim(o) && BetaOf(o) < 120f) StartReturn(o);
                            }
                        }
                        break;
                    }
                }
            }

            public static float Smooth(float u) => u * u * (3f - 2f * u);

            void StartCross(Hand h)
            {
                h.mode = Mode.Move; h.kind = Kind.Cross;
                h.t = 0f;
                h.dur = CrossSec * Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(300f, 1000f, Mathf.Abs(Omega)));
                h.want = AlphaOf(h.left, CrossBeta);
                h.to = SnapAlphaAt(h.left, h.want, h.dur);
                h.crossed = true;
                h.cartGen++;
            }

            void StartReturn(Hand h)
            {
                // On in the wheel's direction, then drop back to its own side.
                float dir = Mathf.Abs(Omega) > 20f ? Mathf.Sign(Omega) : (h.left ? -1f : 1f);
                h.mode = Mode.Move; h.kind = Kind.Return;
                h.t = 0f; h.phase = 0;
                h.from = h.alpha;
                h.slideTo = h.alpha + dir * ReturnSlideDeg;
                h.dur = ReturnSlideSec + ReturnDropSec;
                h.to = RestAlpha(h.left);
                h.crossed = false;
            }

            void StartRim(Hand h, Kind kind, float to, float dur)
            {
                h.mode = Mode.Move; h.kind = kind;
                h.t = 0f; h.dur = dur; h.phase = 0;
                h.from = h.alpha; h.to = to;
            }

            /// <summary>The INBOARD hand (the model's right: the shifter side)
            /// off the rim: a manual shift (~1 s) or the handbrake (held).</summary>
            void StepAway(float dt, bool shift, bool brake)
            {
                var h = R;
                if (h.mode != Mode.Away)
                {
                    if (!brake && !shift) return;
                    h.mode = Mode.Away; h.kind = Kind.Reach;
                    h.phase = 0; h.t = 0f; h.brake = brake; h.hold = HoldSec;
                    h.crossed = false;
                    h.cartGen++;
                    Reaches++;
                }
                else if (brake && !h.brake) { h.brake = true; if (h.phase == 3) { h.phase = 1; h.t = 0f; h.cartGen++; } }
                else if (shift)
                {
                    h.hold = HoldSec;
                    if (h.phase == 3) { h.phase = 1; h.t = 0f; h.cartGen++; Reaches++; }
                }
                h.t += dt;
                switch (h.phase)
                {
                    case 0: h.relaxT = 1f; h.openT = 0.2f; if (h.t >= ReleaseSec) { h.phase = 1; h.t = 0f; h.cartGen++; } break;
                    case 1: h.relaxT = h.brake ? 0.15f : 1f; h.openT = 0f; if (h.t >= DropSec) { h.phase = 2; h.t = 0f; } break;
                    case 2:
                        h.relaxT = h.brake ? 0f : 1f; h.openT = 0f;
                        h.hold -= dt;
                        if (h.brake && !brake) { h.brake = false; h.hold = Mathf.Max(h.hold, 0f); }
                        if (!h.brake && h.hold <= 0f) { h.phase = 3; h.t = 0f; h.cartGen++; h.want = RestAlpha(h.left); h.to = SnapAlphaAt(h.left, h.want, BackSec); }
                        break;
                    default:
                        h.relaxT = 0.5f; h.openT = 0f;
                        if (h.t < BackSec * 0.7f) h.to = SnapAlphaAt(h.left, h.want, BackSec - h.t);
                        // Back on the rim where it left it in the car: 3:30 (LHD).
                        if (h.t >= BackSec) Grip(h, h.to);
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
            Solve(true);
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
            Solve(dt <= 0f);
        }

        /// <summary>For previews and tests: step the planner with explicit inputs.</summary>
        public void Drive(float dt, float wheelDeg, bool shift, bool brake)
        {
            Plan.Step(dt, wheelDeg, shift, brake);
            Solve(dt <= 0f);
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
        public void RimPose(bool left, float alpha, float crossLift, float duck, float off, out Vector3 pos, out Quaternion rot)
            => RimPose(left, alpha, crossLift, duck, off, out pos, out rot, out _);

        /// <summary>... and the forearm's turn: the HAND keeps its knuckles
        /// along the rim exactly (the grip fitted to the rim holds), the
        /// FOREARM leaves the rim's line by the wrist (TangentLock) to aim at
        /// an elbow out of every view.</summary>
        public void RimPose(bool left, float alpha, float crossLift, float duck, float off, out Vector3 pos, out Quaternion rot, out Quaternion foreRot)
        {
            Vector3 P = RimPoint(alpha);
            Vector3 X = RimTangent(alpha);
            // How high on the rim (0 = 6 o'clock... 1 = 12) picks the elbow.
            float high = Planner.Smooth(Mathf.Clamp01(Mathf.InverseLerp(-0.35f, 0.95f, Mathf.Sin(alpha * Mathf.Deg2Rad))));
            // (Contact off = the first cut's elbows, out by the door and console.)
            Vector3 lo = Contact ? ElbowAim : new Vector3(-0.22f, -0.74f, 0.16f);
            Vector3 hi = Contact ? ElbowAimHigh : new Vector3(-0.19f, -0.42f, 0.10f);
            Vector3 A = Eye + Side(left, Vector3.Lerp(lo, hi, high))
                        + Side(left, CrossLift) * crossLift + Side(left, UnderDuck) * duck;
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

        /// <summary>The inboard hand's reach target (model frame, right arm).</summary>
        void AwayPose(bool brake, out Vector3 pos, out Quaternion rot)
        {
            pos = Eye + (brake ? BrakeLever : ShiftKnob);
            rot = brake ? Quaternion.LookRotation(new Vector3(0f, -0.45f, 1f), new Vector3(-1f, -0.35f, 0f))
                        : Quaternion.LookRotation(new Vector3(-0.1f, -0.35f, 1f), Vector3.down);
        }

        void Solve(bool snap)
        {
            Hand other = null;
            for (int i = 0; i < 2; i++)
            {
                var a = arms[i];
                var h = a.hand;
                other = arms[1 - i].hand;
                // A straight-through-the-air phase starts from wherever the
                // hand was last frame.
                if (a.gen != h.cartGen) { a.gen = h.cartGen; a.startPos = a.pos; a.startRot = a.rot; a.startForeRot = a.foreRot; }
                float duck = other.crossed && !h.crossed ? 1f : 0f;
                float lift = h.crossed ? 1f : 0f;
                Vector3 tp; Quaternion tr, tf;
                bool cart = false;
                float u = 0f;
                Vector3 arc = Vector3.zero;
                switch (h.mode)
                {
                    case Mode.Grip:
                    case Mode.Slide:
                        RimPose(h.left, h.alpha, lift, duck, h.mode == Mode.Slide ? 0.012f * h.open : 0f, out tp, out tr, out tf);
                        break;
                    case Mode.Move when h.kind == Kind.Cross:
                    {
                        float land = h.to;
                        RimPose(h.left, land, 1f, 0f, 0f, out tp, out tr, out tf);
                        cart = true;
                        u = Mathf.Clamp01(h.t / h.dur);
                        arc = (lay.wheel.rot * Vector3.back) * 0.07f + Vector3.up * 0.04f;
                        // Onto the rim from in front and outside, not up through the wheel.
                        Vector3 outward = (RimPoint(land) - lay.wheel.pivot).normalized;
                        tp += (outward * 0.05f + lay.wheel.rot * Vector3.back * 0.05f) * (1f - Planner.Smooth(Mathf.Clamp01((u - 0.65f) / 0.35f)));
                        break;
                    }
                    case Mode.Move when h.kind == Kind.Return && h.phase == 1:
                    {
                        float land = h.to;
                        RimPose(h.left, land, 0f, duck, 0f, out tp, out tr, out tf);
                        cart = true;
                        u = Mathf.Clamp01((h.t - Planner.ReturnSlideSec) / Planner.ReturnDropSec);
                        arc = (lay.wheel.rot * Vector3.back) * 0.08f + Vector3.down * 0.03f;
                        // It comes in to the rim from in front and outside, never
                        // through the wheel: the landing held off until the end.
                        Vector3 outward = (RimPoint(land) - lay.wheel.pivot).normalized;
                        tp += (outward * 0.05f + lay.wheel.rot * Vector3.back * 0.05f) * (1f - Planner.Smooth(Mathf.Clamp01((u - 0.65f) / 0.35f)));
                        break;
                    }
                    case Mode.Move:
                        RimPose(h.left, h.alpha, lift, duck, 0.015f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(h.t / h.dur)) + 0.01f * h.open, out tp, out tr, out tf);
                        break;
                    default: // Away
                    {
                        cart = true;
                        Vector3 rp; Quaternion rr, rf;
                        RimPose(h.left, h.phase == 3 ? h.to : Planner.RestAlpha(h.left), 0f, 0f, 0f, out rp, out rr, out rf);
                        AwayPose(h.brake, out var ap, out var ar);
                        if (h.phase == 0)
                        {
                            tp = a.startPos + (lay.wheel.rot * Vector3.back) * 0.05f + Vector3.down * 0.02f;
                            tr = a.startRot; tf = a.startForeRot;
                            u = Mathf.Clamp01(h.t / Planner.ReleaseSec);
                        }
                        else if (h.phase == 1) { tp = ap; tr = tf = ar; u = Mathf.Clamp01(h.t / Planner.DropSec); arc = Vector3.down * 0.04f; }
                        else if (h.phase == 2) { tp = ap; tr = tf = ar; u = 1f; }
                        else { tp = rp; tr = rr; tf = rf; u = Mathf.Clamp01(h.t / Planner.BackSec); arc = Vector3.down * 0.03f; }
                        break;
                    }
                }
                if (cart && !snap && a.posed)
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
                if (Contact && data.wheel != null) Fit(a);
                else for (int f = 0; f < 5; f++) a.curl[f] = 1f;
                if (Contact && data.cabin != null) KeepOutOfCabin(a);
                Place(a);
            }
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
                // In the air (crossing, dropping back, reaching): over everything.
                float uu = h.kind == Kind.Return ? (h.t - Planner.ReturnSlideSec) / Planner.ReturnDropSec : h.t / Mathf.Max(1e-3f, h.dur);
                bool inAir = h.mode == Mode.Away
                             || (h.mode == Mode.Move && uu < 0.85f && (h.kind == Kind.Cross || (h.kind == Kind.Return && h.phase == 1)));
                Vector3 dir = onRim && !inAir ? w.Normal(at) : Vector3.back;
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
                // The upper arm follows its elbow to the shoulder (as Place).
                Vector3 elbowL = a.hand.left ? MX(data.elbow) : data.elbow;
                Vector3 e = fp + a.foreRot * elbowL;
                Vector3 want = Eye + Side(a.hand.left, Shoulder) - e;
                Quaternion ur = (want.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(a.foreRot * Vector3.back, want.normalized) : Quaternion.identity) * a.foreRot;
                for (int i = 0; i < g.pos.Length; i++)
                {
                    if (g.handW[i] > 0.5f) continue;
                    Vector3 q = g.upper[i] > 0.5f ? e + ur * (g.pos[i] - elbowL) : fp + a.foreRot * g.pos[i];
                    float need = cab.Depth(q, out var o, CabinMargin);
                    if (need > worst) { worst = need; at = q; n = o; }
                    // ... and out of the wheel (the rim under a turned wrist).
                    if (data.wheel != null && g.upper[i] < 0.5f)
                    {
                        Vector3 qw = toW.MultiplyPoint3x4(q);
                        float dw = CabinMargin - data.wheel.Sdf(qw);
                        if (dw > worst) { worst = dw; at = q; n = W.MultiplyVector(data.wheel.Normal(qw)); }
                    }
                }
                if (worst <= 1e-4f) break;
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
