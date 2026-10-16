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
    /// FOR OTHER CAMERAS (the windshield reflection): <see cref="Live"/> lists
    /// every arm renderer in the scene. They carry the cabin's layer and
    /// SpeedBlur.CarRenderingLayer, and CockpitInterior hides the whole cabin,
    /// arms included, from every camera but the driver's (OnBeginCamera).
    /// </summary>
    public sealed class CockpitArms
    {
        public const string GeoName = "arms_geo";
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
        public static readonly Vector3 ElbowAim = new Vector3(-0.22f, -0.74f, 0.16f);
        /// <summary>... and from here at a hand on TOP of the rim: the elbow up
        /// and back toward the chest at shoulder width, so the forearm comes from
        /// behind and the palm lies DOWN over the rim instead of standing the
        /// hand on it (and crossed arms make an X in the picture).</summary>
        public static readonly Vector3 ElbowAimHigh = new Vector3(-0.19f, -0.42f, 0.10f);
        /// <summary>The crossing arm's aim: its elbow comes up and back, so its
        /// forearm passes OVER the other one.</summary>
        public static readonly Vector3 CrossLift = new Vector3(0.02f, 0.07f, -0.08f);
        /// <summary>The arm under a crossing one ducks a little.</summary>
        public static readonly Vector3 UnderDuck = new Vector3(0f, -0.06f, 0.04f);
        /// <summary>The upper arm aims at the shoulder: low and back, so the
        /// upper arm stays under the frame even at full look-over.</summary>
        public static readonly Vector3 Shoulder = new Vector3(-0.19f, -0.36f, -0.06f);
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
        }

        public sealed class Rig
        {
            public ArmGeo right, left;
            /// <summary>The elbow pivot in the arm frame (right arm).</summary>
            public Vector3 elbow, shoulderEnd;
            public float upperLen;
            public int verts, tris;
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
                return rig;
            }
        }

        static Vector3 V3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>The format is cockpit_arms.py's (its header says it).</summary>
        static Rig Read(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.ASCII))
            {
                if (new string(r.ReadChars(4)) != "PSXA" || r.ReadInt32() != 1) throw new IOException("not a v1 PSXA file");
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
            /// <summary>Rest grip: degrees BELOW 9 and 3 o'clock (the clip's
            /// hands sit at ~8 and 4; this cabin's camera shows the wheel's
            /// top half, so a little higher keeps the hands in the picture).</summary>
            public const float RestDrop = 16f;
            /// <summary>In each hand's own angle (beta: 180 = 9 o'clock for the
            /// left hand and 3 o'clock for the right, smaller = up):</summary>
            public const float TopBeta = 92f, CrossBeta = 58f, OverBeta = 30f, BottomBeta = 245f;
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

            public static float RestAlpha(bool left) => left ? 180f + RestDrop : -RestDrop;
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
                h.mode = Mode.Grip; h.kind = Kind.None;
                h.alpha = alpha;
                h.phi = alpha - Theta;
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
                        else if (b > BottomBeta) { h.mode = Mode.Slide; h.alpha = AlphaOf(h.left, BottomBeta); h.relaxT = 0.7f; h.crossed = false; }
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
                        bool bottom = b >= BottomBeta - 0.5f, top = b <= OverBeta + 0.5f;
                        if ((bottom && db < -5f) || (top && db > 5f) || (!bottom && !top)) Grip(h, h.alpha);
                        else { h.openT = top ? 0.6f : 0f; h.relaxT = bottom ? 0.7f : 0f; }
                        break;
                    }
                    case Mode.Move:
                    {
                        h.t += dt;
                        float u = Mathf.Clamp01(h.t / Mathf.Max(1e-3f, h.dur));
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
                            if (slide >= 1f && h.phase == 0) { h.phase = 1; h.cartGen++; }
                            h.openT = u < 0.8f ? 1f : 0f;
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
                h.to = AlphaOf(h.left, CrossBeta);
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
                        if (!h.brake && h.hold <= 0f) { h.phase = 3; h.t = 0f; h.cartGen++; }
                        break;
                    default:
                        h.relaxT = 0.5f; h.openT = 0f;
                        // Back on the rim where it left it in the car: 3:30 (LHD).
                        if (h.t >= BackSec) Grip(h, RestAlpha(h.left));
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
            public Matrix4x4 doneF, doneU;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public Vector3 startPos;
            public Quaternion startRot = Quaternion.identity;
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

        public Vector3 RimPoint(float alpha)
        {
            float a = alpha * Mathf.Deg2Rad, r = lay.wheel.rimRadius;
            return lay.wheel.pivot + lay.wheel.rot * new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), lay.wheel.rimZ);
        }

        public Vector3 RimTangent(float alpha)
        {
            float a = alpha * Mathf.Deg2Rad;
            return lay.wheel.rot * new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
        }

        /// <summary>The forearm's pose holding the rim at `alpha`: the grip
        /// centre on the tube, the knuckles along the rim, the forearm aimed
        /// back at its elbow.</summary>
        public void RimPose(bool left, float alpha, float crossLift, float duck, float off, out Vector3 pos, out Quaternion rot)
        {
            Vector3 P = RimPoint(alpha);
            Vector3 X = RimTangent(alpha);
            // How high on the rim (0 = 6 o'clock... 1 = 12) picks the elbow.
            float high = Planner.Smooth(Mathf.Clamp01(Mathf.InverseLerp(-0.35f, 0.95f, Mathf.Sin(alpha * Mathf.Deg2Rad))));
            Vector3 A = Eye + Side(left, Vector3.Lerp(ElbowAim, ElbowAimHigh, high))
                        + Side(left, CrossLift) * crossLift + Side(left, UnderDuck) * duck;
            Vector3 D = P - A;
            Vector3 Z = (D - Mathf.Lerp(TangentLockLow, TangentLockHigh, high) * Vector3.Dot(D, X) * X).normalized;
            X = (X - Vector3.Dot(X, Z) * Z).normalized;
            Vector3 Y = Vector3.Cross(Z, X);
            rot = Quaternion.LookRotation(Z, Y);
            // Lifted off the rim (toward the driver, out of the palm) when not gripping.
            Vector3 face = lay.wheel.rot * Vector3.back;
            pos = P + (face * 0.6f - Y * 0.4f) * off;
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
                if (a.gen != h.cartGen) { a.gen = h.cartGen; a.startPos = a.pos; a.startRot = a.rot; }
                float duck = other.crossed && !h.crossed ? 1f : 0f;
                float lift = h.crossed ? 1f : 0f;
                Vector3 tp; Quaternion tr;
                bool cart = false;
                float u = 0f;
                Vector3 arc = Vector3.zero;
                switch (h.mode)
                {
                    case Mode.Grip:
                    case Mode.Slide:
                        RimPose(h.left, h.alpha, lift, duck, h.mode == Mode.Slide ? 0.012f * h.open : 0f, out tp, out tr);
                        break;
                    case Mode.Move when h.kind == Kind.Cross:
                        RimPose(h.left, h.to, 1f, 0f, 0f, out tp, out tr);
                        cart = true;
                        u = Mathf.Clamp01(h.t / h.dur);
                        arc = (lay.wheel.rot * Vector3.back) * 0.07f + Vector3.up * 0.04f;
                        break;
                    case Mode.Move when h.kind == Kind.Return && h.phase == 1:
                        RimPose(h.left, h.to, 0f, duck, 0f, out tp, out tr);
                        cart = true;
                        u = Mathf.Clamp01((h.t - Planner.ReturnSlideSec) / Planner.ReturnDropSec);
                        arc = (lay.wheel.rot * Vector3.back) * 0.08f + Vector3.down * 0.03f;
                        break;
                    case Mode.Move:
                        RimPose(h.left, h.alpha, lift, duck, 0.015f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(h.t / h.dur)) + 0.01f * h.open, out tp, out tr);
                        break;
                    default: // Away
                    {
                        cart = true;
                        Vector3 rp; Quaternion rr;
                        RimPose(h.left, Planner.RestAlpha(h.left), 0f, 0f, 0f, out rp, out rr);
                        AwayPose(h.brake, out var ap, out var ar);
                        if (h.phase == 0)
                        {
                            tp = a.startPos + (lay.wheel.rot * Vector3.back) * 0.05f + Vector3.down * 0.02f;
                            tr = a.startRot;
                            u = Mathf.Clamp01(h.t / Planner.ReleaseSec);
                        }
                        else if (h.phase == 1) { tp = ap; tr = ar; u = Mathf.Clamp01(h.t / Planner.DropSec); arc = Vector3.down * 0.04f; }
                        else if (h.phase == 2) { tp = ap; tr = ar; u = 1f; }
                        else { tp = rp; tr = rr; u = Mathf.Clamp01(h.t / Planner.BackSec); arc = Vector3.down * 0.03f; }
                        break;
                    }
                }
                if (cart && !snap && a.posed)
                {
                    float e = Planner.Smooth(u);
                    a.pos = Vector3.Lerp(a.startPos, tp, e) + arc * Mathf.Sin(Mathf.PI * u);
                    a.rot = Quaternion.Slerp(a.startRot, tr, e);
                }
                else
                {
                    a.pos = tp; a.rot = tr;
                }
                a.posed = true;
                Place(a);
            }
        }

        void Place(Arm a)
        {
            bool left = a.hand.left;
            a.fore.localPosition = a.pos;
            a.fore.localRotation = a.rot;
            Vector3 elbowL = left ? MX(data.elbow) : data.elbow;
            Vector3 e = a.pos + a.rot * elbowL;
            Vector3 s = Eye + Side(left, Shoulder);
            Vector3 back = a.rot * Vector3.back;
            Vector3 want = s - e;
            Quaternion bend = want.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(back, want.normalized) : Quaternion.identity;
            a.upper.localPosition = e;
            a.upper.localRotation = bend * a.rot;
            Skin(a, Matrix4x4.TRS(a.pos, a.rot, Vector3.one),
                 Matrix4x4.TRS(e, a.upper.localRotation, Vector3.one) * Matrix4x4.Translate(-elbowL));
        }

        /// <summary>Two bones and two shapes, by hand (in the arm root's frame).</summary>
        static void Skin(Arm a, Matrix4x4 F, Matrix4x4 U)
        {
            float wo = Mathf.Clamp01(a.hand.open), wr = Mathf.Clamp01(a.hand.relax);
            if (Mathf.Abs(wo - a.doneOpen) < 1e-3f && Mathf.Abs(wr - a.doneRelax) < 1e-3f && F == a.doneF && U == a.doneU) return;
            a.doneOpen = wo; a.doneRelax = wr; a.doneF = F; a.doneU = U;
            var g = a.geo;
            Vector3[] p0 = g.pos, n0 = g.nrm, dpo = g.dPos[0], dno = g.dNrm[0], dpr = g.dPos[1], dnr = g.dNrm[1];
            float[] up = g.upper;
            for (int i = 0; i < p0.Length; i++)
            {
                Vector3 p = p0[i] + dpo[i] * wo + dpr[i] * wr;
                Vector3 q = n0[i] + dno[i] * wo + dnr[i] * wr;
                float u = up[i];
                if (u <= 0.001f) { a.outP[i] = F.MultiplyPoint3x4(p); a.outN[i] = F.MultiplyVector(q); }
                else if (u >= 0.999f) { a.outP[i] = U.MultiplyPoint3x4(p); a.outN[i] = U.MultiplyVector(q); }
                else
                {
                    a.outP[i] = F.MultiplyPoint3x4(p) * (1f - u) + U.MultiplyPoint3x4(p) * u;
                    a.outN[i] = F.MultiplyVector(q) * (1f - u) + U.MultiplyVector(q) * u;
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
            elbow = a.pos + a.rot * (left ? MX(data.elbow) : data.elbow);
        }
    }
}
