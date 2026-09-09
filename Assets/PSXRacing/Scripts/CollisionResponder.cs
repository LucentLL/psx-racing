using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// The project's collision layer. Before this existed there was no
    /// OnCollision* handler anywhere in the game: PhysX resolved a box against a
    /// frictionless (0.05) dead (bounce 0) barrier, and on the very next tick
    /// CarController's arcade stabilizers deleted the result — the lateral damper
    /// erased the sideways velocity, the counter-steer assist erased the yaw. A
    /// wall strike cost nothing, made no sound, and left no mark.
    ///
    /// This component supplies the consequence the physics material deliberately
    /// does not. Wall friction stays low ON PURPOSE — the NFS rail-slide, where a
    /// shallow contact lets you keep your line, is the target feel — so speed
    /// loss is applied here as an ANGLE-AWARE scrub instead of as blanket
    /// friction that would snag the car on every touch.
    ///
    /// Three classes of contact:
    ///   landing   — normal points up. Suspension's business, not ours.
    ///   glancing  — incidence below <see cref="GlancingIncidence"/>: grind along
    ///               the surface, tangential drag, scrape voice, tiny shake.
    ///   hard      — head-on: velocity scrub, impact voice, camera trauma, and a
    ///               grace window that stands the stabilizers down so the hit is
    ///               actually visible before the arcade layer catches the car.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CollisionResponder : MonoBehaviour
    {
        /// <summary>Below this closing speed into a surface, a contact is the
        /// solver settling rather than an event worth reporting.</summary>
        public float minImpactSpeed = 1.2f;
        /// <summary>Fraction of the surviving tangential speed a hard hit removes,
        /// per m/s of closing speed, capped by <see cref="maxScrub"/>. PhysX kills
        /// the normal component for us; this is the "that cost you the corner"
        /// part it does not model.</summary>
        public float scrubPerMps = 0.045f;
        public float maxScrub = 0.45f;
        /// <summary>Effective grind friction, applied as force rather than baked
        /// into the physics material so it can be angle- and speed-gated.</summary>
        public float scrapeFriction = 0.22f;
        public float maxScrapeDecel = 4.0f;      // m/s^2 — never snag the car
        public bool cameraShake;                 // player car only

        /// <summary>Accumulated hit energy for the LifeSim's damage apply-back.
        /// Written here, consumed by RaceManager when it stamps RaceHandoff.</summary>
        public float DamageScore { get; private set; }

        /// <summary>Count of DISCRETE heavy impacts, as opposed to the continuous
        /// <see cref="DamageScore"/>. The insurance record wants incidents, not
        /// energy: three separate barrier hits and one enormous one are the same
        /// number of dollars of damage and very different driving records.</summary>
        public int HardHits { get; private set; }

        const float GlancingIncidence = 0.5f;    // ~30 deg to the surface
        const float LandingNormalDot = 0.7f;
        /// <summary>Closing speed that makes a hit an INCIDENT. Around 22 km/h
        /// square into a wall — hard enough that a witness would call it a crash
        /// rather than a scrape.</summary>
        const float IncidentSpeed = 6f;
        /// <summary>A crash is one event even though PhysX reports it as a burst
        /// of contacts. Hits inside this window fold into the incident already
        /// counted, the same rate-limit the impact voice uses for the same
        /// reason.</summary>
        const float IncidentWindow = 0.6f;
        /// <summary>
        /// THE SAME RULE FOR THE SPEED SCRUB, and without it a wall does not
        /// cost you a corner, it costs you the race.
        ///
        /// The scrub deletes a share of the car's TANGENTIAL speed the instant
        /// it fires, which is right for one hit and catastrophic repeated. And
        /// it repeats: a stage's banks and barriers are built as RUNS of
        /// separate GameObjects along a 4 m station ladder, so a car leaning on
        /// one at speed leaves a collider and enters the next several times a
        /// second, and each entry is a fresh OnCollisionEnter. At 40 m/s that
        /// is ten scrubs a second, each taking a few percent, compounding to a
        /// standstill in about a second. "I keep getting stopped while driving
        /// on the edge of the road or smooth walls. It full stops the car."
        ///
        /// The player is right to name Underground: the original's walls
        /// grabbed exactly like this, and every Black Box game after it let you
        /// lean on a barrier and keep your line.
        /// </summary>
        const float ScrubWindow = 0.35f;
        float lastScrubTime = -10f;

        CarController car;
        Rigidbody rb;
        CollisionAudio audioVoices;

        float scrapeIntensity, scrapeLoad;
        float lastImpactTime = -10f;
        float lastIncidentTime = -10f;
        float lastContactTime = -10f;

        /// <summary>True while the car is up against something solid. The AI's
        /// stuck check reads this: a car grinding along a barrier at walking pace
        /// is pinned and needs recovering now, while a car simply going slowly in
        /// clear air is just going slowly.</summary>
        public bool InWallContact => Time.time - lastContactTime < 0.25f;

        void Awake()
        {
            car = GetComponent<CarController>();
            rb = GetComponent<Rigidbody>();
            audioVoices = GetComponent<CollisionAudio>();
        }

        void LateUpdate()
        {
            // Decay every frame; OnCollisionStay re-arms it while contact holds.
            if (audioVoices != null) audioVoices.SetScrape(scrapeIntensity, scrapeLoad);
            scrapeIntensity = Mathf.MoveTowards(scrapeIntensity, 0f, 6f * Time.deltaTime);
            scrapeLoad = Mathf.MoveTowards(scrapeLoad, 0f, 6f * Time.deltaTime);
        }

        void OnCollisionEnter(Collision c)
        {
            if (!Measure(c, out float normalSpeed, out float incidence, out Vector3 n)) return;

            // THE CARGO IS TOLD BEFORE ANY OF THE AUDIO GATES RUN.
            //
            // PizzaCargo used to poll InWallContact, which is stamped below —
            // after the landing test and after the minImpactSpeed early-out. A
            // passenger-side slam into a barrier whose foot is banked or kerbed
            // averages its normals upward and was thrown away as a landing, so
            // the load was never told about it. The cargo path gets its own,
            // looser vertical test (25 degrees rather than 45) and its own
            // speed floor, because "did something hit the car hard" and "should
            // this crunch" are different questions.
            FeedCargo(n, normalSpeed);

            if (Mathf.Abs(Vector3.Dot(n, Vector3.up)) > LandingNormalDot) return;
            if (normalSpeed < minImpactSpeed) return;

            bool hard = incidence >= GlancingIncidence;
            DamageScore += normalSpeed * (hard ? 1.6f : 0.4f);
            lastContactTime = Time.time;

            if (hard && normalSpeed >= IncidentSpeed &&
                Time.time - lastIncidentTime > IncidentWindow)
            {
                HardHits++;
                lastIncidentTime = Time.time;
            }

            if (audioVoices != null)
            {
                // Rate-limit: a multi-contact crash fires Enter several times in
                // consecutive ticks and would flam into one mushy noise.
                if (Time.time - lastImpactTime > 0.08f)
                {
                    audioVoices.PlayImpact(hard ? normalSpeed : normalSpeed * 0.45f);
                    lastImpactTime = Time.time;
                }
            }

            if (hard)
            {
                // ONCE PER INCIDENT, and never while already grinding. A
                // contact re-established during a sustained scrape is the same
                // wall, not a new crash — see ScrubWindow. `grinding` is read
                // BEFORE this handler stamps lastContactTime above... which it
                // has, so the window is checked against the scrub's own clock
                // rather than the contact's.
                bool freshHit = Time.time - lastScrubTime > ScrubWindow;
                Vector3 v = rb.linearVelocity;
                Vector3 tangential = v - Vector3.Project(v, n);
                float scrub = freshHit ? Mathf.Min(normalSpeed * scrubPerMps, maxScrub) : 0f;
                if (scrub > 0f) lastScrubTime = Time.time;
                rb.linearVelocity = v - tangential * scrub;

                // Stand the stabilizers down in proportion to the hit. This is
                // the single change that makes a collision exist at all: without
                // it the lateral damper (4.5/s, up to 0.7 g) and the counter-steer
                // assist (up to 3 rad/s^2) put the car back on its line within
                // about three physics ticks, and the player never sees the impact.
                car.RegisterImpact(Mathf.Clamp01(normalSpeed / 9f));
            }
            else
            {
                car.RegisterImpact(Mathf.Clamp01(normalSpeed / 9f) * 0.35f);
            }

            if (cameraShake && ChaseCamera.Active != null)
                ChaseCamera.Active.AddTrauma(Mathf.Clamp01(normalSpeed / (hard ? 10f : 22f)));
        }

        /// <summary>
        /// Hand the pizza load the velocity the car just gained from a wall.
        ///
        /// PhysX removes the closing speed along the contact normal, and on a
        /// hard hit <see cref="OnCollisionEnter"/> then scrubs some of the
        /// tangential component itself; both are velocity the CAR got and the
        /// cargo did not. Only the normal part is known at this point, which is
        /// the dominant term and the one a side slam is made of.
        /// </summary>
        void FeedCargo(Vector3 n, float normalSpeed)
        {
            // GATED ON THE SIDEWAYS SHOVE, which is the quantity a box on a
            // seat actually feels. A pure angle test has to choose between
            // admitting a bank — 26 to 45 degrees off vertical is a wall as far
            // as the load is concerned — and rejecting a hard landing on one.
            // Asking directly means a steep landing registers only in
            // proportion to how much of it went sideways.
            Vector3 delta = n * normalSpeed;
            if (Vector3.ProjectOnPlane(delta, Vector3.up).magnitude < CargoJoltMinSpeed) return;
            if (Mathf.Abs(Vector3.Dot(n, Vector3.up)) > CargoLandingDot) return;
            var cargo = PizzaCargo.Instance;
            if (cargo == null || cargo.Car != car) return;
            cargo.InjectImpact(n * normalSpeed);
        }

        /// <summary>Closing speed, m/s, that counts as a crash for the LOAD —
        /// about 7 km/h square into something. Above a parking nudge and well
        /// below anything a kerb produces along a horizontal normal.</summary>
        const float CargoJoltMinSpeed = 2.0f;
        /// <summary>How far off vertical a normal has to be before the cargo
        /// treats it as a wall rather than as ground: 25 degrees, against the
        /// 45 the audio and damage paths use. A barrier with a banked foot is
        /// still a barrier as far as a box on a seat is concerned.</summary>
        const float CargoLandingDot = 0.9f;

        void OnCollisionStay(Collision c)
        {
            if (!Classify(c, out float normalSpeed, out float incidence, out Vector3 n)) return;
            lastContactTime = Time.time;

            Vector3 v = rb.linearVelocity;
            Vector3 tangential = v - Vector3.Project(v, n);
            float tanSpeed = tangential.magnitude;
            if (tanSpeed < 1.5f) return;

            // The solver reports the impulse it needed this tick; dividing by the
            // step gives the force pressing the car into the surface, which is
            // exactly the load a friction term should scale with.
            float normalForce = c.impulse.magnitude / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            // ANGLE-AWARE, which this class's own header has always claimed and
            // this line never was: it billed a car brushing a barrier at three
            // degrees exactly what it billed one scraping in at thirty. Full
            // drag at the glancing threshold, tapering to nothing at a true
            // graze — so leaning on a wall costs a little and rubbing along
            // one costs almost none. The rail-slide the physics material is
            // kept frictionless FOR.
            float bite = Mathf.Clamp01(incidence / GlancingIncidence);
            float decel = Mathf.Min(scrapeFriction * normalForce / rb.mass, maxScrapeDecel) * bite;
            rb.AddForce(-tangential.normalized * (decel * rb.mass), ForceMode.Force);

            DamageScore += 0.06f * tanSpeed * Time.fixedDeltaTime;

            scrapeIntensity = Mathf.Max(scrapeIntensity, Mathf.Clamp01(tanSpeed / 28f));
            scrapeLoad = Mathf.Max(scrapeLoad, Mathf.Clamp01(normalForce / (rb.mass * 9.81f * 1.5f)));

            if (cameraShake && ChaseCamera.Active != null && incidence < GlancingIncidence)
                ChaseCamera.Active.AddTrauma(0.55f * Time.fixedDeltaTime * Mathf.Clamp01(tanSpeed / 30f));
        }

        /// <summary>
        /// Averages the contact normals and measures how squarely the car went
        /// into the surface. Returns false for anything that is really the ground.
        /// </summary>
        bool Classify(Collision c, out float normalSpeed, out float incidence, out Vector3 normal)
        {
            if (!Measure(c, out normalSpeed, out incidence, out normal)) return false;
            // A near-vertical normal is a landing or a kerb, which the suspension
            // already handles — crunching metal every time the car settles would
            // be the loudest bug in the game.
            return Mathf.Abs(Vector3.Dot(normal, Vector3.up)) <= LandingNormalDot;
        }

        /// <summary>
        /// The measurement half of <see cref="Classify"/>, with no landing test.
        ///
        /// Split out because the cargo needs a different verdict from the same
        /// numbers: what makes a noise and what throws an unrestrained box
        /// across a seat are not the same threshold — see <see cref="FeedCargo"/>.
        /// </summary>
        bool Measure(Collision c, out float normalSpeed, out float incidence, out Vector3 normal)
        {
            normalSpeed = 0f; incidence = 0f; normal = Vector3.zero;
            int count = c.contactCount;
            if (count == 0) return false;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++) sum += c.GetContact(i).normal;
            if (sum.sqrMagnitude < 0.0001f) return false;
            normal = sum.normalized;

            Vector3 approach = c.relativeVelocity;
            float vMag = approach.magnitude;
            if (vMag < 0.01f) return false;
            normalSpeed = Mathf.Abs(Vector3.Dot(approach, normal));
            incidence = normalSpeed / vMag;
            return true;
        }

        public void ResetDamage() { DamageScore = 0f; HardHits = 0; }
    }
}
