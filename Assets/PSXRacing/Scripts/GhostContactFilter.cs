using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Drops the contacts that stop a car dead against a wall that is smooth.
    ///
    /// "I still regularly encounter invisible barriers while car is scraping
    /// against barriers of race tracks. The walls seem smooth (even bridges for
    /// drag race), but the car goes from full speed to instantly stopped."
    ///
    /// The walls WERE smooth to look at and were not smooth to the solver.
    /// Every builder barrier was a chain of separate BoxColliders, one per 4 m
    /// station chord, each overlapping the next. In the pair (car, box k+1)
    /// that box's END FACE is a real face, and it lies in the plane the car is
    /// sliding on — or up to 7 cm proud of it where the chord turns. PhysX has
    /// no way to know the face is buried inside box k: it suppresses internal
    /// edges only within ONE triangle mesh, never between two colliders. So
    /// the car's leading corner meets that end face, the contact normal points
    /// straight back down the road, and the solver deletes the forward speed
    /// in a single step. The player's sweep CCD is worse than the discrete
    /// pass: it rewinds the car to the time of impact with the face and then
    /// kills the velocity along the face's normal. WallScrapeAudit measured
    /// it: 76 dead stops in 2.3 km of DragQuarter, 136 to 20 km/h in one
    /// 1/60 s step, one every 36 m.
    ///
    /// The builder now bakes each run as one closed mesh, which removes those
    /// seams at the source. This class is the net under what the builder
    /// cannot reach: the Charlotte tiles' back-to-back END CAPS where a median
    /// Jersey hands over to a bridge rail, the gore-nose rails, the open ends
    /// of a barrier at a 256 m tile boundary (two MeshColliders, so the same
    /// two-collider problem), and whatever the next builder chains again.
    ///
    /// WHAT A GHOST LOOKS LIKE, from the car's side of the pair:
    ///   * its normal is roughly horizontal and lies ALONG the road — it
    ///     pushes against (or with) the car's travel, not sideways;
    ///   * its point sits in the car's side band: less than
    ///     <see cref="GhostDepthM"/> in from the side face of the box. A seam
    ///     is an edge standing at most a few centimetres into the car's path;
    ///   * the car is SLIDING along a surface on that same side: some contact
    ///     on that side had a lateral normal this step or in the last three.
    /// The third condition is the one that makes it safe. A car driven square
    /// into a wall also has contacts at its two front corners, with sideDepth
    /// zero and a normal against travel; without "already sliding on that
    /// side" the filter would let it through the wall. A head-on or angled
    /// crash with no side contact before it is never touched.
    ///
    /// THE CONTACT CANNOT TELL A SEAM FROM A WALL, and two more guards exist
    /// because of it. At first touch a 3 cm seam and a wall closing across the
    /// road look identical from the pair: the car's leading corner, a normal
    /// against travel, penetration zero. Only the obstacle's EXTENT into the
    /// car's path differs, and the pair does not carry it. So:
    ///   * the car's own side contacts are never ghosts. A car spun sideways
    ///     into a wall meets it with its side face — sideDepth zero, normal
    ///     against travel — and that same contact is the "sliding" evidence.
    ///     A contact counted as lateral evidence is excluded from filtering,
    ///     so no contact can vouch for itself;
    ///   * <see cref="Publish"/> sweeps the car's box, shrunk by the ghost
    ///     band on each side, along its velocity for a step and a half of
    ///     travel. A seam reaches less than the band into the path and the
    ///     shrunk box slides past it; a real wall ahead — square, angled, a
    ///     step deeper than the band — is hit. Nothing is filtered on a step
    ///     whose sweep hit a face turned toward the car by more than 30
    ///     degrees; a face it is merely converging on at a shallow angle (a
    ///     wall flaring out to meet a rock cut) is one it will slide along,
    ///     and does not stand the filter down. The sweep only runs for a car
    ///     that has touched a side in the last half second.
    /// And a contact already more than <see cref="MaxGhostPenetrationM"/>
    /// deep is always kept: a seam never gets that far in, so anything that
    /// has is a wall the filter was wrong about, and the solver gets it back.
    ///
    /// The action is <c>IgnoreContact</c>: the neighbouring surface already
    /// supplies the lateral contact that holds the car on its line, so a
    /// dropped seam costs nothing but the stop.
    ///
    /// THREADING. The two events run on PhysX worker threads (inline on
    /// WebGL), several times per step with different batches of pairs. No
    /// Unity API is touched inside them beyond the pair's own accessors. The
    /// registry is a flat struct array written only on the main thread, in
    /// <see cref="Register"/>/<see cref="Publish"/>, which never overlap the
    /// simulation; the per-side "sliding" stamps are the only thing the
    /// callbacks write, through Volatile, and every thread writes the same
    /// value — the step number.
    /// </summary>
    public static class GhostContactFilter
    {
        /// <summary>Global A/B switch. Harnesses flip it to measure a wall
        /// with and without the filter; off, the callbacks return at once
        /// and Publish skips its sweep.</summary>
        public static bool Enabled = true;

        /// <summary>
        /// An edge reaching less than this into the car's side band is a
        /// ghost. The builder's worst stub stood 7 cm proud of the chord it
        /// followed, and the car's own penetration into the wall it leans on
        /// adds the solver's couple of centimetres; ten covers both with room.
        /// A kerb, a pier or a wall that steps in by more than this is a real
        /// thing the car should hit.
        /// </summary>
        public const float GhostDepthM = 0.10f;

        /// <summary>Contacts dropped since <see cref="ResetStats"/>, counted
        /// from the physics threads with Interlocked.</summary>
        public static int IgnoredCount;

        /// <summary>Below this horizontal speed a dead stop is not a symptom
        /// anybody notices, and a parking nudge should get exact physics.</summary>
        const float MinSpeedMps = 3f;
        /// <summary>A normal further off horizontal than this is ground, a
        /// kerb or a landing — the suspension's business.</summary>
        const float MaxNormalY = 0.5f;
        /// <summary>|cos| of the angle between the contact's horizontal normal
        /// and the car's travel beyond which it pushes ALONG the road.</summary>
        const float AlongTravelDot = 0.5f;
        /// <summary>|cos| between a normal and the car's right axis above which
        /// the contact is the car's SIDE meeting something: sliding evidence,
        /// and never itself a ghost. 0.8 is 37 degrees — a wall hit at a
        /// steeper angle than that is not being slid along.</summary>
        const float LateralDot = 0.8f;
        /// <summary>How far in from the side face a lateral contact may sit and
        /// still count as the car leaning on that side. The Jersey's slope
        /// puts the touch a few cm up the flank; 30 cm keeps out a lateral
        /// normal met under the floor.</summary>
        const float LateralBandM = 0.30f;
        /// <summary>Steps a side contact stays good evidence of sliding. A car
        /// grinding along a wall loses and regains contact every few steps as
        /// the stabilizers and the solver trade it back and forth.</summary>
        const int SlideMemorySteps = 3;
        /// <summary>A contact deeper than this is never dropped — see the
        /// class summary.</summary>
        const float MaxGhostPenetrationM = 0.15f;

        // ---- the path-clear sweep ----
        /// <summary>The sweep's box is the car's, shrunk by this on each side:
        /// the ghost band plus 2 cm so a seam at exactly the limit cannot clip
        /// it.</summary>
        const float ClearInsetM = GhostDepthM + 0.02f;
        /// <summary>Share of the box height taken off the bottom so the sweep
        /// rides over the road's own sag and a kerb, and off the top so an
        /// overhanging rail cap does not count.</summary>
        const float ClearFloorTrim = 0.15f;
        const float ClearRoofTrim = 0.10f;
        /// <summary>The sweep starts this far behind the car's pose, so an
        /// obstacle already inside the car's nose — exactly the one it must
        /// not miss — lies inside the swept volume rather than at its start.
        /// The buffered cast used here reports a start overlap as a hit at
        /// distance zero (the single-hit cast would skip it); either way the
        /// only start overlap that is not an obstacle is the car's own box,
        /// which <see cref="PathClear"/> skips by identity.</summary>
        const float ClearPullBackM = 0.5f;
        /// <summary>Reach past the car's nose, in steps of travel plus a
        /// margin. One step is as far as a dropped contact can let the car go
        /// before the next verdict.</summary>
        const float ClearStepsAhead = 1.5f;
        const float ClearMarginM = 0.3f;
        /// <summary>A sweep hit blocks filtering only when its face is turned
        /// toward the car's travel by more than this (sin 30 degrees): see
        /// PathClear.</summary>
        const float FacingBlockDot = 0.5f;
        /// <summary>Floor under the step used for the reach, in case the
        /// project's fixed step is shorter than the one a harness simulates.</summary>
        const float MinStepS = 1f / 50f;
        /// <summary>The sweep runs only for a car that touched a side this
        /// recently: the signature needs a side contact anyway, and a car in
        /// clear air should not pay a physics query every step.</summary>
        const int CastWarmSteps = 30;

        const int Never = -1000000;
        const int ConvUnknown = 0, ConvShape = 1, ConvActor = 2;

        /// <summary>
        /// One registered car box. Everything but the three stamps is written
        /// on the main thread only; the callbacks read it.
        /// </summary>
        struct Entry
        {
            public BoxCollider box;          // main thread only
            public EntityId id;
            public int layer, matrixMask;    // main thread only (the sweep's mask)
            public Vector3 bodyPos;          // actor pose at Publish
            public Quaternion bodyRot;
            public Vector3 centreInBody;     // box centre in the actor's frame
            public Quaternion relRot;        // box rotation in the actor's frame
            public Vector3 half;             // world half extents
            public Vector3 velocity;
            public int step;
            public int clearStep;            // the step whose sweep found the path clear
            public int lateralL, lateralR;   // written by the callbacks
        }

        static Entry[] entries = new Entry[16];
        static int count;
        static bool subscribed;
        /// <summary>
        /// What the pair's position/rotation describe: the SHAPE's world pose
        /// (PhysX documents PxContactModifyPair::transform as shape-to-world)
        /// or the actor's. Learned once, from the first discrete pair whose
        /// pose matches one reading to the millimetre and not the other, and
        /// the same for every car after that. It matters for the side-band
        /// test only through the box's centre offset, which is zero across on
        /// every car — but a harness box on a child object has one.
        /// </summary>
        static int poseConvention;
        static readonly RaycastHit[] castHits = new RaycastHit[8];
        static readonly System.Action<PhysicsScene, NativeArray<ModifiableContactPair>> onModify = OnModify;
        static readonly System.Action<PhysicsScene, NativeArray<ModifiableContactPair>> onModifyCCD = OnModifyCCD;

        public static void ResetStats() => Interlocked.Exchange(ref IgnoredCount, 0);

        /// <summary>
        /// Enter a car's box. Idempotent. Subscribes both events on first use
        /// rather than from a RuntimeInitializeOnLoadMethod, because the
        /// edit-mode audits register a bare probe box with no play mode and no
        /// CarController anywhere. Works for any BoxCollider; with an
        /// attachedRigidbody the pose is the body's.
        /// </summary>
        public static void Register(BoxCollider box)
        {
            if (box == null) return;
            box.hasModifiableContacts = true;
            if (!subscribed)
            {
                Physics.ContactModifyEvent += onModify;
                Physics.ContactModifyEventCCD += onModifyCCD;
                subscribed = true;
            }

            int k = IndexOf(box);
            if (k < 0)
            {
                if (count == entries.Length) Purge();
                if (count == entries.Length) System.Array.Resize(ref entries, entries.Length * 2);
                k = count;
                entries[k] = new Entry
                {
                    box = box,
                    layer = -1,
                    lateralL = Never,
                    lateralR = Never,
                    clearStep = Never,
                };
                count = k + 1;
            }
            entries[k].id = box.GetEntityId();
            Capture(ref entries[k], box);
        }

        /// <summary>
        /// Take a box out. Matched by reference, not by Unity's null, so it
        /// works from an OnDestroy in which the collider has already gone.
        /// </summary>
        public static void Unregister(BoxCollider box)
        {
            if (ReferenceEquals(box, null)) return;
            int k = IndexOf(box);
            if (k < 0) return;
            if (box != null) box.hasModifiableContacts = false;
            int last = count - 1;
            entries[k] = entries[last];
            entries[last] = default;
            count = last;
        }

        /// <summary>
        /// MAIN THREAD, once per physics step, before the step. Re-reads the
        /// pose, the box's centre and size — CarBody.Apply rewrites both per
        /// model at runtime — and the velocity, advances the car's step
        /// counter, and runs the path-clear sweep. A box that was never
        /// registered, or has been destroyed, is ignored.
        /// </summary>
        public static void Publish(BoxCollider box)
        {
            if (ReferenceEquals(box, null)) return;
            int k = IndexOf(box);
            if (k < 0 || box == null) return;
            ref Entry e = ref entries[k];
            Capture(ref e, box);
            e.step++;
            bool warm = e.step - Mathf.Max(e.lateralL, e.lateralR) <= CastWarmSteps;
            e.clearStep = Enabled && warm && PathClear(ref e, box) ? e.step : Never;
        }

        /// <summary>
        /// Enter Play Mode with domain reload off keeps statics: an edit-mode
        /// audit's probe would still be registered and a harness's
        /// Enabled = false would still be set. Start every play session clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            if (subscribed)
            {
                Physics.ContactModifyEvent -= onModify;
                Physics.ContactModifyEventCCD -= onModifyCCD;
                subscribed = false;
            }
            for (int i = 0; i < entries.Length; i++) entries[i] = default;
            count = 0;
            poseConvention = ConvUnknown;
            IgnoredCount = 0;
            Enabled = true;
        }

        static int IndexOf(BoxCollider box)
        {
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(entries[i].box, box)) return i;
            return -1;
        }

        /// <summary>Drop entries whose collider was destroyed without an
        /// Unregister, before growing the table for a new one.</summary>
        static void Purge()
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (entries[i].box != null) continue;
                entries[i] = entries[count - 1];
                entries[count - 1] = default;
                count--;
            }
        }

        static void Capture(ref Entry e, BoxCollider box)
        {
            Transform bt = box.transform;
            Rigidbody rb = box.attachedRigidbody;
            Transform body = rb != null ? rb.transform : bt;
            // The RIGIDBODY's pose, not the transform's: every car is
            // interpolated, and in FixedUpdate its transform can be a drawn
            // pose between steps rather than the one PhysX is about to use.
            e.bodyPos = rb != null ? rb.position : bt.position;
            e.bodyRot = rb != null ? rb.rotation : bt.rotation;
            e.velocity = rb != null ? rb.linearVelocity : Vector3.zero;
            Vector3 scale = bt.lossyScale;
            Vector3 size = Vector3.Scale(box.size, scale);
            e.half = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f;
            if (body == bt)
            {
                e.relRot = Quaternion.identity;
                e.centreInBody = Vector3.Scale(box.center, scale);
            }
            else
            {
                // A box on a child of the body. Child and body are drawn
                // together, so their RELATIVE pose is right even mid-
                // interpolation.
                Quaternion inv = Quaternion.Inverse(body.rotation);
                e.relRot = inv * bt.rotation;
                e.centreInBody = inv * (bt.TransformPoint(box.center) - body.position);
            }
        }

        /// <summary>
        /// Is the car's path clear of anything reaching deeper than the ghost
        /// band, for the next step and a half? The car's box, shrunk by
        /// <see cref="ClearInsetM"/> on both sides and trimmed top and bottom,
        /// swept along the velocity — the 3D velocity, so a climb does not
        /// read as the road rising into the box. Any hit but the car itself
        /// means no filtering this step, including another car ahead.
        /// </summary>
        static bool PathClear(ref Entry e, BoxCollider box)
        {
            Vector3 v = e.velocity;
            float flat = new Vector3(v.x, 0f, v.z).magnitude;
            if (flat <= MinSpeedMps) return false;
            float speed = v.magnitude;
            Vector3 dir = v / speed;

            Quaternion rot = e.bodyRot * e.relRot;
            Vector3 centre = e.bodyPos + e.bodyRot * e.centreInBody;
            Vector3 h = e.half;
            float inset = Mathf.Min(ClearInsetM, h.x * 0.5f);
            float floorTrim = 2f * h.y * ClearFloorTrim;
            float roofTrim = 2f * h.y * ClearRoofTrim;
            var half = new Vector3(h.x - inset, h.y - 0.5f * (floorTrim + roofTrim), h.z);
            float pull = Mathf.Min(ClearPullBackM, h.z);
            Vector3 from = centre + rot * new Vector3(0f, 0.5f * (floorTrim - roofTrim), 0f) - dir * pull;
            float reach = pull + speed * Mathf.Max(Time.fixedDeltaTime, MinStepS) * ClearStepsAhead + ClearMarginM;

            PhysicsScene scene = box.gameObject.scene.GetPhysicsScene();
            int hits = scene.BoxCast(from, half, dir, castHits, rot, reach, CastMask(ref e, box),
                                     QueryTriggerInteraction.Ignore);
            Rigidbody rb = box.attachedRigidbody;
            Vector3 travel = new Vector3(v.x, 0f, v.z) / flat;
            for (int i = 0; i < hits; i++)
            {
                Collider c = castHits[i].collider;
                if (c == null || c == box) continue;
                if (rb != null && c.attachedRigidbody == rb) continue;
                // ONLY A FACE TURNED TOWARD THE CAR is a wall ahead. A face
                // the car is converging on at a shallow angle — where a guard
                // wall flares out to meet a rock face that runs straight on,
                // 3.6 degrees at Mount Mitchell wp 1533 — is a surface it will
                // slide along, and every contact with it is lateral (never a
                // ghost anyway); standing the filter down for it left the
                // hand-over's own seam live, and it stopped the car dead. A
                // pier, a wall end, a step or a wall closing across the road
                // faces the car by more than 30 degrees and still blocks, and
                // whatever the sweep says, nothing reaching deeper than
                // GhostDepthM into the side band is ever filtered. A cast that
                // starts overlapping something reports a normal straight back
                // along the cast: that blocks, as it should.
                Vector3 n = castHits[i].normal; n.y = 0f;
                if (n.sqrMagnitude > 1e-6f && Vector3.Dot(n.normalized, travel) > -FacingBlockDot) continue;
                return false;
            }
            return true;
        }

        /// <summary>The layers this box can actually touch: the collision
        /// matrix for its layer, then the body's and the collider's own
        /// include/exclude overrides — an audit probe excludes everything but
        /// the walls, and its sweep should see exactly what it can hit.</summary>
        static int CastMask(ref Entry e, BoxCollider box)
        {
            int layer = box.gameObject.layer;
            if (layer != e.layer)
            {
                int m = 0;
                for (int l = 0; l < 32; l++)
                    if (!Physics.GetIgnoreLayerCollision(layer, l)) m |= 1 << l;
                e.layer = layer;
                e.matrixMask = m;
            }
            int mask = e.matrixMask;
            Rigidbody rb = box.attachedRigidbody;
            if (rb != null) mask = (mask | rb.includeLayers.value) & ~rb.excludeLayers.value;
            return (mask | box.includeLayers.value) & ~box.excludeLayers.value;
        }

        static void OnModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) => Filter(pairs, false);
        static void OnModifyCCD(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) => Filter(pairs, true);

        /// <summary>
        /// Worker thread. Two passes over the batch: the first stamps which
        /// side of which car is leaning on something, the second drops the
        /// ghosts, so a side contact and the seam it runs into can arrive in
        /// the same batch — for a city tile they arrive in the same PAIR.
        /// </summary>
        static void Filter(NativeArray<ModifiableContactPair> pairs, bool ccd)
        {
            if (!Enabled) return;
            Entry[] reg = entries;
            int n = Volatile.Read(ref count);
            if (n <= 0) return;
            if (n > reg.Length) n = reg.Length;
            int len = pairs.Length;

            // PASS 1 — sliding evidence. A normal within 37 degrees of the
            // car's right axis, met within 30 cm of that side face, is the
            // side leaning on something. Its SIGN is not asked: whichever way
            // PhysX orients it, a lateral push at a side face comes from
            // outside that face.
            for (int p = 0; p < len; p++)
            {
                ModifiableContactPair pair = pairs[p];
                int k = CarOf(ref pair, reg, n, out bool carFirst);
                if (k < 0) continue;
                ref Entry e = ref reg[k];
                Frame(ref pair, ref e, carFirst, ccd, out Vector3 centre, out Vector3 right);
                int step = e.step;
                int cc = pair.contactCount;
                for (int i = 0; i < cc; i++)
                {
                    float lx = Vector3.Dot(pair.GetPoint(i) - centre, right);
                    if (e.half.x - Mathf.Abs(lx) > LateralBandM) continue;
                    if (Mathf.Abs(Vector3.Dot(pair.GetNormal(i), right)) <= LateralDot) continue;
                    if (lx >= 0f) Volatile.Write(ref e.lateralR, step);
                    else Volatile.Write(ref e.lateralL, step);
                }
            }

            // PASS 2 — the ghosts.
            for (int p = 0; p < len; p++)
            {
                ModifiableContactPair pair = pairs[p];
                int k = CarOf(ref pair, reg, n, out bool carFirst);
                if (k < 0) continue;
                ref Entry e = ref reg[k];
                int step = e.step;
                if (e.clearStep != step) continue;
                bool slideL = step - Volatile.Read(ref e.lateralL) <= SlideMemorySteps;
                bool slideR = step - Volatile.Read(ref e.lateralR) <= SlideMemorySteps;
                if (!slideL && !slideR) continue;

                Vector3 v = carFirst ? pair.bodyVelocity : pair.otherBodyVelocity;
                v.y = 0f;
                float speed = v.magnitude;
                if (speed <= MinSpeedMps) continue;
                Vector3 travel = v / speed;

                Frame(ref pair, ref e, carFirst, ccd, out Vector3 centre, out Vector3 right);
                int cc = pair.contactCount;
                for (int i = 0; i < cc; i++)
                {
                    Vector3 nrm = pair.GetNormal(i);
                    if (Mathf.Abs(nrm.y) > MaxNormalY) continue;
                    // The car's own side meeting something is evidence, never a
                    // ghost: see the class summary on a car spun into a wall.
                    if (Mathf.Abs(Vector3.Dot(nrm, right)) > LateralDot) continue;
                    // ALONG the road, in either sense. Orienting the normal by
                    // the car's centre ("flip it until it points inward") gets
                    // a seam met by the FRONT corner right and one met by the
                    // REAR corner — the tail-out wall-ride this game is built
                    // for — backwards, and that stop would go through. The
                    // car's geometry cannot orient a normal lying along its
                    // own side face; the obstacle's could, and the pair does
                    // not carry it. Taking both senses is safe: the dangerous
                    // one (pushes against travel) is judged by every guard
                    // here, and the other — something behind the car shoving
                    // it on — is either a seam's boost or a car leaving what
                    // it touched.
                    if (Mathf.Abs(nrm.x * travel.x + nrm.z * travel.z) <= AlongTravelDot) continue;
                    float lx = Vector3.Dot(pair.GetPoint(i) - centre, right);
                    if (e.half.x - Mathf.Abs(lx) >= GhostDepthM) continue;
                    if (!(lx >= 0f ? slideR : slideL)) continue;
                    if (pair.GetSeparation(i) < -MaxGhostPenetrationM) continue;
                    pair.IgnoreContact(i);
                    Interlocked.Increment(ref IgnoredCount);
                }
            }
        }

        /// <summary>The registry slot of the pair's car, or -1 when neither
        /// collider is a registered car — or both are: car against car is
        /// never filtered.</summary>
        static int CarOf(ref ModifiableContactPair pair, Entry[] reg, int n, out bool carFirst)
        {
            EntityId a = pair.colliderEntityId;
            EntityId b = pair.otherColliderEntityId;
            int ka = -1, kb = -1;
            for (int k = 0; k < n; k++)
            {
                EntityId id = reg[k].id;
                if (id == a) ka = k;
                else if (id == b) kb = k;
            }
            carFirst = ka >= 0;
            if ((ka >= 0) == (kb >= 0)) return -1;
            return carFirst ? ka : kb;
        }

        /// <summary>
        /// The car box's centre and right axis as the SOLVER saw them for
        /// this pair — for a CCD pair that is the pose at the time of impact,
        /// up to a step's travel from the one Publish recorded, and the side
        /// band is ten centimetres wide.
        /// </summary>
        static void Frame(ref ModifiableContactPair pair, ref Entry e, bool carFirst, bool ccd,
                          out Vector3 centre, out Vector3 right)
        {
            Vector3 q = carFirst ? pair.position : pair.otherPosition;
            Quaternion r = carFirst ? pair.rotation : pair.otherRotation;
            int conv = Volatile.Read(ref poseConvention);
            if (conv == ConvUnknown && !ccd) conv = Calibrate(q, ref e);
            if (conv == ConvActor)
            {
                centre = q + r * e.centreInBody;
                r = r * e.relRot;
            }
            else centre = q;
            right = r * Vector3.right;
        }

        /// <summary>
        /// Learn <see cref="poseConvention"/> from a DISCRETE pair: its pose
        /// is the start-of-step pose Publish just recorded, so it matches one
        /// reading to float precision. Needs a box whose centre is off the
        /// body's origin (every car's is, 72 cm up); until then the shape
        /// reading is assumed, which is also the only one a centred box has.
        /// </summary>
        static int Calibrate(Vector3 q, ref Entry e)
        {
            Vector3 c = e.centreInBody;
            float cm = c.magnitude;
            if (cm < 0.02f) return ConvUnknown;
            Vector3 shapeAt = e.bodyPos + e.bodyRot * c;
            float dShape = (q - shapeAt).magnitude;
            float dActor = (q - e.bodyPos).magnitude;
            int verdict = ConvUnknown;
            if (dShape < 0.005f && dActor > 0.5f * cm) verdict = ConvShape;
            else if (dActor < 0.005f && dShape > 0.5f * cm) verdict = ConvActor;
            if (verdict != ConvUnknown) Interlocked.CompareExchange(ref poseConvention, verdict, ConvUnknown);
            return verdict;
        }
    }
}
