using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PSXRacing
{
    /// <summary>
    /// THE LAMP TABLE: every street lamp and tail lamp in the loaded scenes,
    /// and the twelve of them PSXLamps.cginc lights the world from.
    ///
    /// The owner's ask (2026-09-21, from Need for Speed 2015): "how street
    /// lights bathe the road". A street lamp used to be a flat 16 m additive
    /// disc laid on the tarmac; it is now a real per-pixel light, and this is
    /// the registry behind it - the street-lamp twin of the headlight table
    /// CarLights pushes, kept separate so a city full of lamps never takes a
    /// slot from a car's low beam.
    ///
    /// WHO REGISTERS. NightGlow registers every lamp head of a scene (the
    /// circuits' baked "Glow" markers, the town's, a streamed city tile's) as
    /// <see cref="Kind.Street"/>, lit together by <see cref="StreetOn"/>,
    /// which the hour owns through NightGlow.SetAll. CarLights registers ONE
    /// <see cref="Kind.Point"/> per car at the midpoint of its tail lenses and
    /// moves / dims / switches it every frame with <see cref="Set"/>. Every
    /// entry carries its OWNER, and an entry whose owner has been destroyed
    /// is dropped at the next push - a city tile that streams out, a scene
    /// that closes in the editor, never leaves a lamp burning in thin air.
    ///
    /// WHEN IT PUSHES. From RenderPipelineManager.beginCameraRendering, for
    /// the camera about to render, with THAT camera's pose (see
    /// <see cref="EnsureHook"/>). Not from an Update: the cars and the chase
    /// camera move in LateUpdate, so a table chosen in Update is chosen for
    /// where the camera was a frame ago; and the scene view, the game view
    /// and a tool's render request each get a table chosen for their own eye.
    /// In PLAY mode only ONE camera chooses - the game's own view
    /// (<see cref="Camera.main"/>), once a frame - and every other camera (the
    /// cockpit mirror, the pizza cam, the scene view, a tool's push) draws
    /// with the table that one chose; see NO POPPING for why.
    ///
    /// WHICH TWELVE. Candidates are the lit entries within
    /// <see cref="MaxReach"/> whose pool (a sphere of its radius) touches the
    /// view frustum - a lamp behind the camera lights nothing the camera can
    /// see, so it never takes a slot from one ahead. Score is distance,
    /// nearest first. The <see cref="PointCap"/> nearest tail lamps (the cars
    /// right around the camera) and the <see cref="StreetCap"/> nearest
    /// street lamps are WANTED. Those shares are FIXED: the street's is eight
    /// whether or not four cars are in view.
    ///
    /// NO POPPING (review of 2026-09-21). The first version gave the street
    /// "twelve minus the tail lamps in view" and faded each kind against its
    /// own first excluded candidate - so the cut AND every lamp's fade were
    /// recomputed from the candidate list on every push. When an AI car's tail
    /// lamp came round a bend, or the frustum swung onto a lamp at forty
    /// metres, the street lost a slot, its cut moved a whole lamp nearer, and
    /// several pools and their wet streaks stepped darker or brighter in ONE
    /// frame - exactly the pop the fade was there to prevent, and on a wet
    /// road at night the most visible thing on screen. Now nothing a frame
    /// can change is allowed to move brightness by more than a little:
    ///   * the street's share never moves (<see cref="StreetCap"/>);
    ///   * the only fade that comes from WHERE a lamp is, is a fixed band at
    ///     the edge of reach (<see cref="FadeBandM"/>), so a lamp coming inside
    ///     110 m rises out of the fog, and that depends on nothing but the
    ///     lamp's own distance;
    ///   * the table is a small set of HELD lamps, each with a weight that
    ///     eases toward 1 while the lamp is wanted and toward 0 when it is not
    ///     (<see cref="EaseRate"/>, a quarter of a second). A newcomer takes a
    ///     slot only when one is free, and a held lamp gives its slot up only
    ///     once it has faded to nothing. The tail lamps' unused slots are how
    ///     a street lamp fades in WHILE the one it replaces fades out; they
    ///     are lent through that hand-over and never by moving the cut.
    /// A camera CUT (the eye jumps or turns further than a frame of driving
    /// could take it: a respawn, a replay cut, looking back) snaps the weights
    /// straight to their targets - the picture itself was cut, so there is
    /// nothing on screen for a lamp to pop against, and easing there would only
    /// show the new view's lamps coming on. EDIT mode has no frame loop to ease
    /// over, so every push there takes its targets directly: a tool's shot is
    /// the same picture however it got there.
    ///
    /// THE STALE-GLOBAL RULE: it always pushes, a count of zero when nothing
    /// is lit, and the arrays are always exactly <see cref="MaxLamps"/> long
    /// (Unity locks a global array's length the first time it is set).
    /// </summary>
    public static class StreetLights
    {
        /// <summary>Slots in the shader table. MUST equal PSX_MAX_LAMPS in
        /// PSXLamps.cginc.</summary>
        public const int MaxLamps = 12;
        /// <summary>At most this many tail lamps in the table at once: the
        /// cars nearest the camera. The rest of the slots are the street's.
        /// </summary>
        public const int PointCap = 4;
        /// <summary>Metres. A lamp further than this from the eye lights
        /// nothing the eye can make out through the night fog.</summary>
        public const float MaxReach = 110f;
        /// <summary>Slots the street lamps are ranked into: the table less the
        /// tail lamps' share. FIXED, whatever is in view - a share that moved
        /// with the number of cars in view is what made the pools jump (see
        /// NO POPPING in the class notes).</summary>
        public const int StreetCap = MaxLamps - PointCap;
        /// <summary>Metres, at the edge of <see cref="MaxReach"/>, over which a
        /// lamp fades in by distance: full brightness at MaxReach - FadeBandM,
        /// none at MaxReach. A band tied to the lamp's own distance, never to
        /// which other lamps happen to be in the table.</summary>
        public const float FadeBandM = 30f;
        /// <summary>Weight per second a held lamp eases toward its target
        /// (1 wanted, 0 not): a swap takes a quarter of a second. On UNSCALED
        /// time, so the pause menu does not freeze a hand-over half done.
        /// </summary>
        public const float EaseRate = 4f;
        /// <summary>The eye moved further than this in one frame: a cut (a
        /// respawn, a replay camera, a teleport), not driving. 300 km/h at
        /// 10 fps is 8 m.</summary>
        public const float CutJumpM = 20f;
        /// <summary>The eye turned through more than this in one frame: a cut
        /// (looking back, a replay camera), not a corner.</summary>
        public const float CutTurnDeg = 45f;
        /// <summary>Half-angle of the view cone <see cref="Push(Vector3, Vector3)"/>
        /// tests against when it has no camera (tools): 120 degrees across.</summary>
        public const float ConeHalfDeg = 60f;

        public enum Kind { Street = 0, Point = 1 }

        /// <summary>
        /// The street lamp's colour: a warm YELLOW BULB, authored sRGB and
        /// converted to linear when it is pushed.
        ///
        /// It was high-pressure sodium (1, .64, .30) — the lamp that really
        /// was on a 1999 American arterial, and nearly monochrome orange. The
        /// owner, 2026-09-21: "the street lights are too orange. They should
        /// be more natural yellow like bulbs from the 90's, similar to car
        /// headlights." So this is the halogen headlight's family
        /// (CarLights.Halogen is (1, .86, .62)), a little warmer and a little
        /// more saturated so a lamp still reads as a yellow bulb and not as a
        /// white one. Hue 42 degrees against sodium's 29.
        ///
        /// It lives on in every lamp-lit thing at night: the pools, the wet
        /// road's glints, the halos round the heads, the rain under them.
        /// </summary>
        public static readonly Color Bulb = new Color(1.00f, 0.87f, 0.56f);
        /// <summary>Pool radius in metres: a 7-9 m pole lights a disc of road
        /// about 15 m each way (the radius is the 3D reach from the head).
        ///
        /// 18, not the first cut's 22. At 22 a Charlotte arterial's pools (a
        /// lamp every 19 m, staggered) ran into one continuous orange wash;
        /// the NFS frames have POOLS - bright under each head, darker between -
        /// and that rhythm is most of what "street lights bathe the road" looks
        /// like from a moving car.</summary>
        public const float StreetRadius = 18f;
        /// <summary>Multiplier on the linear colour as it reaches the world.
        ///
        /// 2.0, and the pools are exactly as BRIGHT as they were: it was 3.4
        /// on sodium, and the bulb colour carries 1.68x the linear luminance
        /// (.754 against .449, because it has green in it where sodium had
        /// almost none), so 3.4 / 1.68 puts the same light on the road in a
        /// different colour. The night look's brightness targets were
        /// measured, and changing the hue of the lamps is not a reason to
        /// re-open them. (3.4 itself: with the 18 m pool it kept the road
        /// under a head where 22 m and 3.0 had it; the gaps between heads
        /// are what got darker.)</summary>
        public const float StreetIntensity = 2.0f;

        /// <summary>Street lamps lit. NightGlow.SetAll drives it from the
        /// hour; tail lamps ignore it (each has its own on).</summary>
        public static bool StreetOn { get; set; }

        // ------------------------------------------------------------------
        //  The registry. A flat array of entries with a free list: a handle is
        //  (generation << 16) | slot, so a handle kept after its entry was
        //  removed - and its slot reused by some other lamp - is recognised as
        //  stale and ignored, rather than quietly moving someone else's lamp.
        // ------------------------------------------------------------------
        struct Entry
        {
            public Object owner;
            public bool owned;       // registered WITH an owner: prune when it dies
            public Vector3 pos;
            public float radius;
            public Vector3 linear;   // the sRGB colour, linearised once at Add
            public float intensity;
            public Kind kind;
            public bool on;
            public bool used;
            public int gen;
        }

        static Entry[] entries = new Entry[64];
        static int high;                                 // slots [0, high) have ever been used
        static readonly List<int> freeSlots = new List<int>();
        static bool warnedFull;

        /// <summary>Live registered entries (for tests).</summary>
        public static int Registered { get; private set; }
        /// <summary>Lamps in the table after the last push (for tests).</summary>
        public static int PushedCount { get; private set; }

        /// <summary>
        /// Register a lamp. Returns a handle for <see cref="Set"/> and
        /// <see cref="Remove"/>, or -1 if the registry is full. Street lamps
        /// light while <see cref="StreetOn"/>; a Point lamp starts ON. The
        /// owner may be null (a test's lamps): such a lamp is never pruned and
        /// lives until <see cref="Remove"/>.
        /// </summary>
        public static int Add(Object owner, Vector3 pos, float radius, Color srgbColour, float intensity, Kind kind)
        {
            EnsureHook();
            int slot;
            if (freeSlots.Count > 0)
            {
                slot = freeSlots[freeSlots.Count - 1];
                freeSlots.RemoveAt(freeSlots.Count - 1);
            }
            else
            {
                if (high > 0xFFFF)
                {
                    if (!warnedFull) { warnedFull = true; Debug.LogWarning("StreetLights: registry full (65536 lamps)"); }
                    return -1;
                }
                if (high == entries.Length) System.Array.Resize(ref entries, entries.Length * 2);
                slot = high++;
            }
            ref Entry e = ref entries[slot];
            e.gen = e.gen % 0x7FFF + 1;                  // 1..32767, never 0
            e.used = true;
            e.owner = owner;
            e.owned = (object)owner != null;
            e.pos = pos;
            e.radius = Mathf.Max(0.5f, radius);
            var lin = srgbColour.linear;
            e.linear = new Vector3(lin.r, lin.g, lin.b);
            e.intensity = intensity;
            e.kind = kind;
            e.on = true;
            Registered++;
            return (e.gen << 16) | slot;
        }

        /// <summary>Move, dim or switch one lamp (the tail lamps, every
        /// frame). A stale or -1 handle is ignored.</summary>
        public static void Set(int handle, Vector3 pos, float intensity, bool on)
        {
            if (!Valid(handle, out int slot)) return;
            ref Entry e = ref entries[slot];
            e.pos = pos;
            e.intensity = intensity;
            e.on = on;
        }

        public static void Remove(int handle)
        {
            if (Valid(handle, out int slot)) Free(slot);
        }

        /// <summary>Drop every lamp this owner registered. Compares by
        /// reference, so it works from the owner's own OnDisable / OnDestroy
        /// when Unity already calls it "null".</summary>
        public static void RemoveAll(Object owner)
        {
            if ((object)owner == null) return;
            for (int i = 0; i < high; i++)
                if (entries[i].used && ReferenceEquals(entries[i].owner, owner)) Free(i);
        }

        /// <summary>Registered lamps of a kind within r of p, lit or not
        /// (skyglow detection and the like).</summary>
        public static int CountWithin(Vector3 p, float r, Kind kind)
        {
            int n = 0;
            float r2 = r * r;
            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used) continue;
                if (e.owned && e.owner == null) { Free(i); continue; }
                if (e.kind != kind) continue;
                if ((e.pos - p).sqrMagnitude <= r2) n++;
            }
            return n;
        }

        static bool Valid(int handle, out int slot)
        {
            slot = handle & 0xFFFF;
            if (handle < 0 || slot >= high) return false;
            return entries[slot].used && entries[slot].gen == (handle >> 16);
        }

        static void Free(int slot)
        {
            entries[slot].used = false;
            entries[slot].owner = null;
            freeSlots.Add(slot);
            Registered--;
        }

        // ------------------------------------------------------------------
        //  The push.
        // ------------------------------------------------------------------
        static readonly int CountId = Shader.PropertyToID("_PSXLampCount");
        static readonly int PosId = Shader.PropertyToID("_PSXLampPos");
        static readonly int ColorId = Shader.PropertyToID("_PSXLampColor");
        static readonly Vector4[] gPos = new Vector4[MaxLamps];
        static readonly Vector4[] gColor = new Vector4[MaxLamps];
        static readonly Plane[] planes = new Plane[6];
        // The WANTED lamps of each kind, nearest first: registry slots and
        // their distances, filled by Rank. Exactly the fixed shares long - no
        // "first excluded" entry any more, since nothing is faded against it.
        static readonly int[] streetIdx = new int[StreetCap];
        static readonly float[] streetScore = new float[StreetCap];
        static readonly bool[] streetFound = new bool[StreetCap];
        static readonly int[] pointIdx = new int[PointCap];
        static readonly float[] pointScore = new float[PointCap];
        static readonly bool[] pointFound = new bool[PointCap];
        static int rankedStreet, rankedPoint;

        // THE HELD SET (play mode): the lamps actually in the table, each by
        // HANDLE (so a lamp removed and its slot reused is dropped, not
        // confused with the newcomer) with its eased weight. Never more than
        // the table: the wanted lamps are at most PointCap + StreetCap ==
        // MaxLamps, so every wanted lamp is held within one ease of wanting it.
        static readonly int[] heldHandle = new int[MaxLamps];
        static readonly float[] heldShown = new float[MaxLamps];
        static int heldCount;
        // Time.frameCount of the last advance: once a frame, however many
        // times the game camera renders.
        static int advancedFrame = -1;
        // The eye the weights were last advanced for, to tell a cut.
        static bool haveLastEye;
        static Vector3 lastEye, lastFwd;
        // The no-MainCamera fallback's search, once a frame, into a buffer
        // that only ever grows (no per-frame allocation).
        static Camera[] camBuf = new Camera[8];
        static Camera fallbackDriver;
        static int fallbackFrame = -1;

        static bool hooked;

        /// <summary>
        /// Install the per-camera push, once. Called from <see cref="Add"/>
        /// and from PSXGlobals.OnEnable (which runs in edit mode too, so the
        /// scene view and a tool's render request are right with no tool
        /// doing anything). A domain reload drops the subscription together
        /// with this flag, and PSXGlobals' OnEnable after the reload puts it
        /// back.
        /// </summary>
        public static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            // A camera that draws only the UI layer (5) - the HUD overlay in
            // the camera stack, the output blit - keeps the table the scene
            // camera chose; re-choosing for its pose would light the world
            // for an eye that never draws it.
            if ((cam.cullingMask & ~(1 << 5)) == 0) return;
            // An inspector preview renders a preview scene, not this one: it
            // gets no lamps rather than the lamps nearest wherever it sits.
            if (cam.cameraType == CameraType.Preview) { PushNone(); return; }
            var t = cam.transform;
            if (!Application.isPlaying) { PushInstant(cam); return; }
            // PLAY: the game's view advances the hand-over, once a frame, for
            // its own frustum. Every other camera - the mirror and the pizza
            // cam render BEFORE it (lower depth, so their textures are ready
            // for it), the scene view after - draws with the weights as they
            // stand. Advancing per camera would step the weights two or three
            // times a frame, and a mirror looking backwards would rank its own
            // lamps IN and the view's OUT on every frame it rendered: the pop
            // back again, through the back door.
            if (Time.frameCount != advancedFrame && IsDriver(cam))
            {
                advancedFrame = Time.frameCount;
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                Rank(t.position, t.forward, false, true);
                Advance(t.position, t.forward, Time.unscaledDeltaTime);
            }
            EmitHeld(t.position);
        }

        /// <summary>
        /// The camera the player is looking through, the one whose frame
        /// advances the hand-over: <see cref="Camera.main"/> (the chase / cockpit
        /// / on-foot camera is tagged; CockpitView untags the mirror it copies
        /// from it precisely so Camera.main stays right). With no MainCamera,
        /// the deepest Game camera that draws the world: every secondary view
        /// this game makes (mirror depth-1, pizza cam -10, car viewer -50) sits
        /// BELOW the view it feeds, and a render-texture test would not do -
        /// the main camera itself draws into PSXCameraOutput's framebuffer.
        /// </summary>
        internal static bool IsDriver(Camera cam)
        {
            var main = Camera.main;
            if (main != null) return cam == main;
            if (fallbackFrame != Time.frameCount)
            {
                fallbackFrame = Time.frameCount;
                fallbackDriver = null;
                int count = Camera.allCamerasCount;
                if (camBuf.Length < count) camBuf = new Camera[count * 2];
                count = Camera.GetAllCameras(camBuf);
                float best = float.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    var c = camBuf[i];
                    camBuf[i] = null;
                    if (c == null || c.cameraType != CameraType.Game) continue;
                    if ((c.cullingMask & ~(1 << 5)) == 0) continue;
                    if (c.depth > best) { best = c.depth; fallbackDriver = c; }
                }
            }
            return cam == fallbackDriver;
        }

        /// <summary>Push for <see cref="Camera.main"/>: in edit mode chosen
        /// for its frustum; in play mode the table as the game's view last
        /// chose it (a push from outside the frame loop does not advance the
        /// hand-over). Pushes an empty table when there is no main camera.
        /// </summary>
        public static void Push()
        {
            var cam = Camera.main;
            if (cam == null) { PushNone(); return; }
            if (Application.isPlaying) { EmitHeld(cam.transform.position); return; }
            PushInstant(cam);
        }

        /// <summary>
        /// Push for an eye with no camera object (the screenshot tools, before
        /// a render request). In EDIT mode the table is chosen for this eye and
        /// taken at once, with a 120-degree cone about <paramref name="fwd"/>
        /// standing in for the frustum (a zero fwd tests every direction). In
        /// PLAY mode it is the table as the game's view last chose it, faded
        /// for distance from this eye: a tool push never advances the
        /// hand-over, or a sweep of shots would step the player's own lamps.
        /// </summary>
        public static void Push(Vector3 eye, Vector3 fwd)
        {
            if (Application.isPlaying) { EmitHeld(eye); return; }
            float m = fwd.magnitude;
            bool cone = m > 1e-4f;
            Rank(eye, cone ? fwd / m : Vector3.forward, cone, false);
            EmitRanked();
        }

        /// <summary>
        /// ONE PLAY FRAME for a test, in any mode: rank for this eye (the
        /// cone, as <see cref="Push(Vector3, Vector3)"/>), advance the
        /// hand-over by <paramref name="dt"/> seconds exactly as the game
        /// camera's frame does, and push the held table. The self-test drives
        /// the easing with it in edit mode, where no frame loop runs. Not for
        /// the game: it would advance the weights a second time in a frame.
        /// </summary>
        public static void PushFrame(Vector3 eye, Vector3 fwd, float dt)
        {
            float m = fwd.magnitude;
            bool cone = m > 1e-4f;
            Vector3 f = cone ? fwd / m : Vector3.forward;
            Rank(eye, f, cone, false);
            Advance(eye, f, dt);
            EmitHeld(eye);
        }

        static void PushInstant(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var t = cam.transform;
            Rank(t.position, t.forward, false, true);
            EmitRanked();
        }

        static void PushNone()
        {
            for (int i = 0; i < MaxLamps; i++) gPos[i] = gColor[i] = Vector4.zero;
            Send(0);
        }

        /// <summary>
        /// The ranking: which lamps are WANTED for this eye - the
        /// <see cref="PointCap"/> nearest tail lamps and the
        /// <see cref="StreetCap"/> nearest street lamps, into pointIdx /
        /// streetIdx. Allocation-free: a fixed insertion buffer per kind
        /// (twelve entries between them) instead of a sort, so a city of a
        /// thousand registered lamps costs one distance test each and the
        /// rest is a dozen compares at most. Also where dead owners are pruned.
        /// </summary>
        static void Rank(Vector3 eye, Vector3 fwd, bool useCone, bool useFrustum)
        {
            rankedStreet = rankedPoint = 0;
            float reach2 = MaxReach * MaxReach;
            float cosHalf = Mathf.Cos(ConeHalfDeg * Mathf.Deg2Rad);
            float sinHalf = Mathf.Sin(ConeHalfDeg * Mathf.Deg2Rad);
            bool streetOn = StreetOn;

            for (int i = 0; i < high; i++)
            {
                ref Entry e = ref entries[i];
                if (!e.used) continue;
                // Owner gone (a scene closed in the editor, where no OnDisable
                // runs; anything destroyed without saying so): drop it for
                // good. First, whatever its state, so a dead lamp never sits
                // in the registry through a whole day waiting to be lit. A
                // MonoBehaviour's null check is a managed pointer test.
                if (e.owned && e.owner == null) { Free(i); continue; }
                if (!Lit(ref e, streetOn)) continue;
                Vector3 d = e.pos - eye;
                float d2 = d.sqrMagnitude;
                if (d2 > reach2) continue;

                float dist = Mathf.Sqrt(d2);
                if (useFrustum)
                {
                    bool inside = true;
                    for (int p = 0; p < 6; p++)
                        if (planes[p].GetDistanceToPoint(e.pos) < -e.radius) { inside = false; break; }
                    if (!inside) continue;
                }
                else if (useCone && dist > e.radius)
                {
                    // Sphere against cone: inside if the angle off the axis is
                    // under the half-angle plus the sphere's own angular
                    // radius. cos(half + b) = cos(half)cos(b) - sin(half)sin(b).
                    float s = e.radius / dist;
                    float c = Mathf.Sqrt(1f - s * s);
                    if (Vector3.Dot(d, fwd) / dist < cosHalf * c - sinHalf * s) continue;
                }

                if (e.kind == Kind.Street) Offer(streetIdx, streetScore, ref rankedStreet, i, dist);
                else Offer(pointIdx, pointScore, ref rankedPoint, i, dist);
            }
        }

        /// <summary>Keep the buffer's best (lowest-score) entries in order.
        /// The buffer is exactly as long as can be taken.</summary>
        static void Offer(int[] idx, float[] score, ref int held, int i, float s)
        {
            int cap = idx.Length;
            if (held == cap && s >= score[cap - 1]) return;
            int k = held < cap ? held++ : cap - 1;
            while (k > 0 && score[k - 1] > s)
            {
                score[k] = score[k - 1];
                idx[k] = idx[k - 1];
                k--;
            }
            score[k] = s;
            idx[k] = i;
        }

        /// <summary>Is this lamp giving light at all: street lamps on the
        /// hour's switch, a tail lamp on its own, and neither at zero.</summary>
        static bool Lit(ref Entry e, bool streetOn)
        {
            return (e.kind == Kind.Street ? streetOn : e.on) && e.intensity > 0f;
        }

        /// <summary>The edge-of-reach fade: 1 inside MaxReach - FadeBandM, 0
        /// at MaxReach, linear between. The only fade that depends on where a
        /// lamp is, and it depends on nothing else.</summary>
        static float DistanceFade(float dist)
        {
            return Mathf.Clamp01((MaxReach - dist) / FadeBandM);
        }

        /// <summary>EDIT mode: the wanted lamps, taken at once at full weight
        /// (times the distance fade). A tool's single shot has no previous
        /// frame to be continuous with, and must not depend on where the
        /// camera was the shot before.</summary>
        static void EmitRanked()
        {
            int n = 0;
            for (int k = 0; k < rankedPoint; k++) Emit(ref n, pointIdx[k], DistanceFade(pointScore[k]));
            for (int k = 0; k < rankedStreet; k++) Emit(ref n, streetIdx[k], DistanceFade(streetScore[k]));
            Finish(n);
        }

        /// <summary>PLAY mode: the held lamps at their eased weights, faded
        /// for distance from THIS eye (the mirror's, the scene view's, a
        /// tool's) at the lamps' live positions - a tail lamp moves with its
        /// car whichever camera is drawing. Reads the held set, never changes
        /// it. A switched-off lamp goes dark in this very frame, weight or no
        /// weight: a switch is an event the player is meant to see (its halo
        /// and its lens go out in the same frame), not table churn to hide.
        /// </summary>
        static void EmitHeld(Vector3 eye)
        {
            int n = 0;
            bool streetOn = StreetOn;
            for (int h = 0; h < heldCount; h++)
            {
                if (!Valid(heldHandle[h], out int slot)) continue;
                ref Entry e = ref entries[slot];
                if (e.owned && e.owner == null) continue;   // pruned at the next Rank
                if (!Lit(ref e, streetOn)) continue;
                Emit(ref n, slot, heldShown[h] * DistanceFade((e.pos - eye).magnitude));
            }
            Finish(n);
        }

        /// <summary>
        /// One step of the hand-over, after a Rank for the same eye. Every held
        /// lamp eases toward 1 if it is still wanted and toward 0 if not, and
        /// gives its slot up only once it is at 0 (or its entry is gone - a
        /// removed lamp has nothing left to fade). Then each wanted lamp not
        /// yet held takes a free slot if there is one - tail lamps first, then
        /// street lamps nearest first - and starts one step up from dark. A
        /// newcomer that finds the table full waits: every lamp filling it is
        /// wanted (at most twelve, which fit) or fading out, gone within a
        /// quarter of a second.
        /// </summary>
        static void Advance(Vector3 eye, Vector3 fwd, float dt)
        {
            // A CUT snaps: step 1 takes every weight straight to its target and
            // frees every slot not wanted before the newcomers are seated.
            bool cut = !haveLastEye
                       || (eye - lastEye).sqrMagnitude > CutJumpM * CutJumpM
                       || Vector3.Dot(fwd, lastFwd) < Mathf.Cos(CutTurnDeg * Mathf.Deg2Rad);
            haveLastEye = true;
            lastEye = eye;
            lastFwd = fwd;
            float step = cut ? 1f : EaseRate * Mathf.Max(0f, dt);

            for (int k = 0; k < rankedPoint; k++) pointFound[k] = false;
            for (int k = 0; k < rankedStreet; k++) streetFound[k] = false;

            // Backwards, because Release moves the last held lamp into the
            // freed place - one this loop has already been through.
            for (int h = heldCount - 1; h >= 0; h--)
            {
                if (!Valid(heldHandle[h], out int slot)) { Release(h); continue; }
                bool wanted = Claim(pointIdx, rankedPoint, pointFound, slot)
                              || Claim(streetIdx, rankedStreet, streetFound, slot);
                float shown = Mathf.MoveTowards(heldShown[h], wanted ? 1f : 0f, step);
                if (!wanted && shown <= 0f) { Release(h); continue; }
                heldShown[h] = shown;
            }

            float first = Mathf.Min(1f, step);
            for (int k = 0; k < rankedPoint && heldCount < MaxLamps; k++)
                if (!pointFound[k]) Hold(pointIdx[k], first);
            for (int k = 0; k < rankedStreet && heldCount < MaxLamps; k++)
                if (!streetFound[k]) Hold(streetIdx[k], first);
        }

        /// <summary>Is registry slot <paramref name="slot"/> among the first
        /// <paramref name="count"/> ranked, and not already claimed? Marks it.
        /// </summary>
        static bool Claim(int[] idx, int count, bool[] found, int slot)
        {
            for (int k = 0; k < count; k++)
                if (!found[k] && idx[k] == slot) { found[k] = true; return true; }
            return false;
        }

        static void Hold(int slot, float shown)
        {
            heldHandle[heldCount] = (entries[slot].gen << 16) | slot;
            heldShown[heldCount] = shown;
            heldCount++;
        }

        static void Release(int h)
        {
            heldCount--;
            heldHandle[h] = heldHandle[heldCount];
            heldShown[h] = heldShown[heldCount];
        }

        /// <summary>Write one lamp into the next slot at weight
        /// <paramref name="w"/>. A lamp at zero is left out rather than
        /// written black: it lights nothing, and the shader's loop is shorter
        /// by one.</summary>
        static void Emit(ref int n, int i, float w)
        {
            ref Entry e = ref entries[i];
            float k = e.intensity * w;
            if (k <= 0f || n >= MaxLamps) return;
            gPos[n] = new Vector4(e.pos.x, e.pos.y, e.pos.z, e.radius);
            gColor[n] = new Vector4(e.linear.x * k, e.linear.y * k, e.linear.z * k, (float)e.kind);
            n++;
        }

        static void Finish(int n)
        {
            for (int i = n; i < MaxLamps; i++) gPos[i] = gColor[i] = Vector4.zero;
            Send(n);
        }

        static void Send(int n)
        {
            PushedCount = n;
            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalVectorArray(PosId, gPos);
            Shader.SetGlobalVectorArray(ColorId, gColor);
        }
    }
}
