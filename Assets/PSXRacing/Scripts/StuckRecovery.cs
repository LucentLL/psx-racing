using UnityEngine;
using UnityEngine.InputSystem;

namespace PSXRacing
{
    /// <summary>
    /// Notices when the player's car can no longer drive itself out of trouble,
    /// says so, and eventually puts it back on the racing line.
    ///
    /// The AI has had this since P2 (<see cref="AIDriver"/>'s stuck/pinned
    /// timers) precisely because a car ground into a barrier never recovers on
    /// its own — the wall's friction is deliberately near zero so a shallow
    /// contact lets you keep your line, which also means a square contact leaves
    /// nothing to push against. The player had no equivalent, and the recovery
    /// controls that did exist were all invisible: R on a keyboard, Back on a
    /// pad, RESET CAR three items down a pause menu. A player who beached the
    /// car nose-first into an embankment therefore experienced it as the game
    /// taking the car away from them — reported as "I lost all control of my car
    /// during the middle of the race".
    ///
    /// Three ways to be stuck, with different patience for each:
    ///   pinned   — grinding a barrier. Never resolves; recover quickly.
    ///   beached  — stationary while asking for throttle or brake, in clear air.
    ///              Usually a kerb or the scenery.
    ///   rolled   — on the roof or on a side. Nothing the player does helps.
    ///
    /// And three ways to have LEFT THE ROAD for good, recovered at once with
    /// no warning (see <see cref="LeftTheRoad"/>): under the world's floor,
    /// in the sea, or fallen below the road onto something that is not one.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class StuckRecovery : MonoBehaviour
    {
        public CarController car;
        public CollisionResponder responder;
        public PlayerCarInput input;
        /// <summary>Optional: the live tank, so a car that ran dry is not
        /// mistaken for one wedged into a bank.</summary>
        public FuelTank tank;

        /// <summary>Below this the car is not making progress. 4 km/h — a car
        /// crawling out of a gravel trap is above it, a car pushing a wall is
        /// not.</summary>
        public float movingKmh = 4f;

        public float pinnedSeconds = 2.0f;
        public float beachedSeconds = 4.0f;
        public float rolledSeconds = 1.5f;
        /// <summary>Grace between the warning appearing and the car being moved,
        /// so a player who was about to free themselves still can — and so the
        /// reset never happens without being announced first.</summary>
        public float warningSeconds = 3.0f;

        /// <summary>What the HUD should be showing, or null. Read by
        /// <see cref="RaceHUD"/>; kept as state here rather than pushed, so the
        /// HUD's change-gating still works.</summary>
        public string Prompt { get; private set; }

        /// <summary>Set while the car has been pointing back down the road long
        /// enough to mean it. Read by <see cref="RaceHUD"/>, which ranks it
        /// under <see cref="Prompt"/> — being stuck is the more urgent news.</summary>
        public bool WrongWay { get; private set; }

        /// <summary>Alignment against the path tangent below which the car is
        /// going the wrong way, and the speed and patience it takes to say so.
        /// The dot and the speed are AIDriver's numbers; the four seconds are
        /// not, deliberately — a human is allowed to be halfway through a spin.
        /// The TEST is stricter than the AI's: see UpdateWrongWay.</summary>
        const float WrongWayDot = -0.3f;
        const float WrongWayMinSpeed = 3f;      // m/s, as AIDriver measures it
        const float WrongWaySeconds = 4f;
        float wrongWayTimer;
        /// <summary>Last waypoint index, so the search is a window and not a walk.</summary>
        int pathHint = -1;

        void UpdateWrongWay(float dt)
        {
            var mgr = RaceManager.Instance;
            var path = mgr != null ? mgr.path : null;
            if (path == null || path.Count < 2 || car == null || car.Body == null)
            { wrongWayTimer = 0f; WrongWay = false; pathHint = -1; return; }

            Vector3 v = car.Body.linearVelocity; v.y = 0f;
            if (v.magnitude < WrongWayMinSpeed)
            { wrongWayTimer = 0f; WrongWay = false; return; }

            // BOTH have to be wrong, and that is not pedantry — it is the
            // difference between the two cars this test has to tell apart.
            // TRAVEL alone condemns a car REVERSING back onto the line, which
            // is the recovery the banner should be praising. The NOSE alone
            // (which is all AIDriver checks) condemns a car sliding backwards
            // through a corner it is still carrying. Going the wrong way means
            // pointed down the road AND moving that way.
            //
            // Hinted, because NearestIndex without one walks every waypoint and
            // Beech Gap has 2819 of them — 2819 distance tests per rendered
            // frame, on a phone, for a banner. Every other per-frame caller in
            // the project passes a hint; this was the exception.
            pathHint = path.NearestIndex(transform.position, pathHint);
            Vector3 tangent = path.GetTangent(pathHint);
            bool travelWrong = Vector3.Dot(v.normalized, tangent) < WrongWayDot;
            bool noseWrong = Vector3.Dot(transform.forward, tangent) < WrongWayDot;
            if (travelWrong && noseWrong) wrongWayTimer += dt;
            else wrongWayTimer = 0f;
            WrongWay = wrongWayTimer >= WrongWaySeconds;
        }

        float stuckTimer;

        /// <summary>
        /// Below this the car has left the world, and no amount of patience
        /// brings it back.
        ///
        /// Every other state here resolves eventually or is at least standing
        /// on something. Falling does not: the car is well above `movingKmh`
        /// all the way down, so `crawling` is false, so the watchdog never
        /// arms and the car descends for ever. On the circuits there was
        /// nowhere to fall from. Bogue Banks put a 20 m bridge over open water
        /// with a low parapet, which is a place to fall from.
        ///
        /// Derived from the route rather than a constant, because "too low"
        /// means something different on a sea-level island and a mountain
        /// 1200 m up.
        ///
        /// THE BACKSTOP, not the rule. Almost nothing that goes over an edge
        /// ever reaches it: a circuit keeps terrain under its spans, a stage
        /// span releases to the land, and Bogue Banks collides a seabed 4 m under
        /// the sound across its whole 340 m near band — so a car that dropped
        /// off a deck landed well above this, on its wheels, and was never
        /// recovered at all unless it also rolled or the player found the
        /// reset. <see cref="FellBelowTheRoad"/> and <see cref="seaY"/> are
        /// what catch those; this still catches whatever falls past them.
        /// </summary>
        float floorY = float.NegativeInfinity;

        // ------------------------------------------------------------------
        //  Fallen below the road
        // ------------------------------------------------------------------
        /// <summary>How far under the road a car has to be before it has
        /// FALLEN rather than run wide. A graded stage fill (RoadsideRules:
        /// RecoverableSlope 1V:6H across ClearZoneM 3.5 m from the shoulder at
        /// 1.1 m, SteepestRecoverableSlope 1V:4H beyond) is only 0.59 m down
        /// at the edge of the clear zone and does not reach 3 m until 14.2 m
        /// past the tarmac, off the recoverable roadside altogether;
        /// and it is well short of any deck — Bogue's bridge stands 20 m over
        /// the water, a Charlotte deck 5.55 m over the road beneath it.</summary>
        const float FellBelowRoadM = 3f;
        /// <summary>Metres past half the road's width within which "below the
        /// road" is measured: room for a car that went over an edge at speed
        /// and carried on across the land under it, but not so much that a
        /// road on the far side of a valley is ever the one it is said to
        /// have fallen from.</summary>
        const float FellLateralMarginM = 30f;
        /// <summary>Seconds with no wheel on the Road layer before a fall is
        /// believed. A car airborne off a crest is back down inside a second;
        /// a car that went over a parapet 20 m up is still falling.</summary>
        const float FellNoRoadSeconds = 1.5f;
        /// <summary>How often the geometric half of the test is asked once the
        /// cheap half passes. The city's asks the road graph, which is not a
        /// per-frame query on a phone; a quarter of a second is imperceptible
        /// next to the one and a half already waited.</summary>
        const float FellPollSeconds = 0.25f;
        /// <summary>Search radius for the nearest Charlotte street: past the
        /// lateral margin plus half the widest carriageway RoadProfiles builds
        /// (a seven-lane street, about 13 m), so a miss is never a street the
        /// test would have counted.</summary>
        const float CityFellSearchM = 60f;

        /// <summary>Seconds since any wheel last stood on the Road layer.</summary>
        float sinceRoadContact;
        /// <summary>The car's height when it last did — or when it was last
        /// teleported, since every teleport puts it on a road (a respawn, a
        /// grid, the city's seat on its street). Negative infinity until the
        /// first of either, so a car that has never been on a road cannot be
        /// said to have fallen off one.</summary>
        float lastRoadY = float.NegativeInfinity;
        float fellPoll;
        /// <summary><see cref="CarController.TeleportCount"/> as last seen.</summary>
        int seenTeleports;

        // ------------------------------------------------------------------
        //  In the sea
        // ------------------------------------------------------------------
        /// <summary>The stage builder's water plane is a GameObject by this
        /// name (PSXRacingBuilder.Stage.cs, BuildStageSea). It has NO
        /// collider, on purpose: its comment names this component as what
        /// brings a car back out of it.</summary>
        const string SeaObjectName = "Sea";
        /// <summary>How far under the water plane a car's origin has to be
        /// before it is in the sea rather than on the beach. The Bogue bake
        /// holds marsh — the lowest land it builds — 0.35 m above the plane,
        /// other land 0.4 m and the road 0.6 m (tools/bogue/fetch_bogue.mjs).
        /// A car's origin can be pressed under the surface it stands on only
        /// until its body box meets it: about 0.25 m on the FD, 0.42 m on the
        /// van, 0.6 m on the Land Rover — so even the tallest shell bottoming
        /// out on marsh stays a quarter of a metre clear of this. Half a
        /// metre under the plane, the sills are under water.</summary>
        const float SeaDepthM = 0.5f;
        /// <summary>The water plane's height in this scene, or negative
        /// infinity where there is no sea.</summary>
        float seaY = float.NegativeInfinity;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (responder == null) responder = GetComponent<CollisionResponder>();
            if (input == null) input = GetComponent<PlayerCarInput>();
            if (tank == null) tank = GetComponent<FuelTank>();
        }

        void Start()
        {
            // In Start, not Awake: RaceManager builds its path in Awake, and
            // asking too early gets a null every time and silently disables
            // the guard.
            var rm = RaceManager.Instance;
            if (rm != null && rm.path != null && rm.path.Count > 0)
            {
                float lowest = float.MaxValue;
                foreach (var w in rm.path.waypoints) if (w.y < lowest) lowest = w.y;
                // 60 m under the lowest point of the road. Deeper than any
                // gorge floor, any seabed and any legitimate excursion, so
                // nothing that is still in the world can reach it.
                floorY = lowest - 60f;
            }

            // The drawn plane, not the catalog's number for it: TrackIndex
            // names whatever venue the menu last chose, which a scene opened
            // straight from the editor never set, and the height the water is
            // DRAWN at is the one a player sees the car go under. Renderer bounds
            // rather than the mesh, which static batching replaces; a flat
            // plane's box has no height, so its centre is the surface.
            var sea = GameObject.Find(SeaObjectName);
            var seaRenderer = sea != null ? sea.GetComponent<Renderer>() : null;
            if (seaRenderer != null) seaY = seaRenderer.bounds.center.y;
        }

        void Update()
        {
            bool live = DriveSession.Live &&
                        (input == null || input.inputEnabled) && !PauseMenu.IsOpen;

            // Ahead of every gate: whether a wheel is on the road is a fact
            // about the car during a countdown or a pause as much as while
            // live, and the fall test needs to know how long ago it last was.
            if (car != null) TrackRoadContact(Time.deltaTime);

            // Off the road for good: recover NOW, with no warning banner and
            // no grace period. The grace exists so a player who was about to
            // free themselves still can, and there is no freeing yourself from
            // this — a car under the world is a kilometre down by the time the
            // prompt could be read, and one in the sound or on a gorge floor
            // has no road it can climb back to. Deliberately ahead of the
            // parked-on-purpose excuses too: nobody parks below the seabed.
            if (live && car != null && LeftTheRoad())
            {
                DriveSession.Respawn(car);
                stuckTimer = 0f;
                Prompt = null;
                // This return skips the wrong-way block below, so the flag has
                // to be cleared here too or a recovered car keeps a banner it
                // earned somewhere it no longer is; and the index hint is now
                // a kilometre out.
                wrongWayTimer = 0f; WrongWay = false; pathHint = -1;
                // The fall test's own memory of the drop that fired it is
                // cleared by the teleport itself: see TrackRoadContact.
                return;
            }

            // WRONG WAY. Warned about, never acted on.
            //
            // The AI have carried this test since P2 (AIDriver.UpdateRecovery:
            // heading against the path tangent, under -0.3 for two seconds
            // above 3 m/s) and turn themselves round when it trips. The player
            // had no equivalent, which is how a grid facing the wrong way ends
            // up as "I started facing the other cars" — the whole field quietly
            // corrected itself and the one car nobody could correct was the
            // one being driven. It is also the cheapest possible answer to
            // being spun round mid-race with no landmark to tell you.
            //
            // A BANNER AND NOTHING ELSE. Turning the player's car round for
            // them is exactly the complaint this component was written to
            // answer; telling them is not. Four seconds rather than the AI's
            // two, because a human is allowed to be halfway through a spin.
            if (live) UpdateWrongWay(Time.deltaTime);
            else { wrongWayTimer = 0f; WrongWay = false; }

            bool rolled = car != null && Vector3.Dot(transform.up, Vector3.up) < 0.25f;
            bool pinned = responder != null && responder.InWallContact;

            // Two states where a stationary car is not a stuck car, and where
            // respawning it would be the game taking it away rather than
            // handing it back:
            //
            //   at a pump — the player parked there on purpose, and the whole
            //     point of the forecourt is standing still on it.
            //   out of fuel — the racing line is no more drivable than where
            //     the car died, so the watchdog would fire, teleport, find the
            //     car still motionless, and fire again. The way out of this one
            //     is the fuel truck in the pause menu, which the HUD says so.
            //
            // NEITHER excuse covers a car on its ROOF or grinding a wall. Those
            // two readings are not explained by "the driver chose to stop", and
            // standing down for them meant a car that rolled onto the forecourt
            // stayed there for good — with the fuel prompt over the top of the
            // banner that would have told it how to get out.
            //   at a venue — the same excuse as the pump, and it was missing.
            //     A car stopped at the junction at the end of your own street
            //     is a car being asked where it is going, and the watchdog took
            //     the banner off it: RaceHUD ranks this Prompt ABOVE
            //     TownVenue's, so "PRESS F — WHERE TO?" was replaced by
            //     "STUCK — AUTO-RESET IN 3", and then the car was teleported
            //     off the menu it was standing on.
            //     AND AT AN EDGE, which the same pass missed. The town's two
            //     ends print "PRESS F — HEAD HOME" and then wait to be
            //     pressed, so they are a car stopped on purpose in front of a
            //     question by exactly the same argument, and the watchdog was
            //     still free to take the banner off that one and teleport the
            //     car away from the road out of town.
            bool parkedOnPurpose = !rolled && !pinned;
            if (parkedOnPurpose && (GasPump.AtPump || Town.TownVenue.AtVenue ||
                                    Town.TownEdge.AtEdge ||
                                    (tank != null && tank.Empty)))
                live = false;

            // And never while the driver is out of it. A car with nobody in it
            // is not stuck, it is parked — and teleporting it back to the
            // racing line would leave its owner standing on the forecourt
            // watching it go.
            if (OnFoot.ForecourtMode.OnFoot) live = false;

            if (!live || car == null)
            {
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            bool asking = car.throttleInput > 0.15f || car.brakeInput > 0.15f;
            bool crawling = Mathf.Abs(car.speedKmh) < movingKmh;

            // Rolled counts even at rest and even with no input: a car on its
            // roof is not a player waiting on the grid, and the countdown gate
            // above already excludes the actual grid.
            bool trapped = rolled || (crawling && (pinned || asking));
            if (!trapped)
            {
                stuckTimer = 0f;
                // Not stuck, but possibly not on the road either — and a car
                // that is driving perfectly well on the wrong side of a
                // barrier is invisible to every test above, because all three
                // of them are about a car that has STOPPED.
                if (UpdateOffTrack(Time.deltaTime)) return;
                Prompt = null;
                return;
            }
            offTrackTimer = 0f;     // being stuck is the more urgent news

            stuckTimer += Time.deltaTime;
            float limit = rolled ? rolledSeconds : pinned ? pinnedSeconds : beachedSeconds;
            if (stuckTimer < limit) { Prompt = null; return; }

            float untilReset = limit + warningSeconds - stuckTimer;
            if (untilReset <= 0f)
            {
                DriveSession.Respawn(car);
                stuckTimer = 0f;
                Prompt = null;
                return;
            }

            Prompt = "STUCK — " + ResetControlName() + "\nAUTO-RESET IN " +
                     Mathf.CeilToInt(untilReset);
        }

        void TrackRoadContact(float dt)
        {
            // A TELEPORT STARTS THE COUNT AGAIN, whoever did it: this one's
            // respawn above, the stuck countdown's, R, the pause menu's RESET
            // CAR. The car is on a road now, and the wheel contacts are still
            // the last physics step's — the place it was taken FROM. Holding
            // on to that across a reset onto a street under a deck reads as a
            // car 3 m below its last road and below the deck overhead in plan,
            // and throws it back up onto the overpass before its wheels have
            // reported the street it was put on.
            if (car.TeleportCount != seenTeleports)
            {
                seenTeleports = car.TeleportCount;
                sinceRoadContact = 0f;
                lastRoadY = transform.position.y;
                fellPoll = 0f;
                return;
            }

            // grounded first: onRoad is left as it was when a wheel lifts
            var wheels = car.wheelContacts;
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i].grounded && wheels[i].onRoad)
                {
                    sinceRoadContact = 0f;
                    lastRoadY = transform.position.y;
                    return;
                }
            sinceRoadContact += dt;
        }

        /// <summary>
        /// Has the car left the road in a way no driving brings it back from?
        /// Under the world's floor; under the sea; or fallen below the road.
        ///
        /// The sea is a plane with no collider, so a car over a Bogue parapet
        /// used to settle on the seabed 4 m down and drive about under the
        /// sound, recovered only if it happened to roll. There is no beach
        /// under the plane and no road either, so being under it is the whole
        /// test. Like the floor, it stands ahead of the on-foot excuse:
        /// nobody parks in the sea.
        /// </summary>
        bool LeftTheRoad()
        {
            float y = transform.position.y;
            if (y < floorY || y < seaY - SeaDepthM) return true;
            // A car with nobody in it is parked, not fallen: the same rule as
            // the watchdog below, for the same reason.
            return !OnFoot.ForecourtMode.OnFoot && FellBelowTheRoad(y);
        }

        /// <summary>
        /// FELL BELOW THE ROAD: over a deck edge onto a gorge floor, off a
        /// fill down the land under it, through a gap in a rail onto the
        /// street beneath. The car is on its wheels, so it is neither rolled
        /// nor beached, and it can drive, so it is never trapped; before this
        /// it could drive about down there for as long as the player liked,
        /// and inside the off-track banner's 20 m it was not even told how to
        /// get out.
        ///
        /// Three things, all of which must hold:
        ///
        ///   no wheel on the Road layer for <see cref="FellNoRoadSeconds"/> —
        ///     which is what keeps this off a car legitimately on the LOWER
        ///     road of a grade separation, however far below the upper one
        ///     it is. Every tarmac ribbon is on that layer, across its decks
        ///     too, and so are the forecourts and Charlotte's streets;
        ///   <see cref="FellBelowRoadM"/> below where the car last HAD one —
        ///     it came off a road rather than drove out to low ground. That is
        ///     Charlotte free roam's grass under a viaduct: level land a car
        ///     reaches from the street beside it, where the nearest road in
        ///     plan is the deck overhead;
        ///   that far below the road it is beside, within
        ///     <see cref="FellLateralMarginM"/> past half its width —
        ///     measured against the car's OWN leg of a race (RaceManager's
        ///     progress index as the hint), so the lower leg of a hairpin or
        ///     the Parkway loop's road under its own bridge is never the one it
        ///     is judged by; and in free roam against the nearest street on
        ///     the graph, at that street's solved height.
        /// </summary>
        bool FellBelowTheRoad(float y)
        {
            if (sinceRoadContact < FellNoRoadSeconds || !(lastRoadY - y > FellBelowRoadM))
            {
                fellPoll = 0f;
                return false;
            }
            fellPoll -= Time.deltaTime;
            if (fellPoll > 0f) return false;
            fellPoll = FellPollSeconds;

            Vector3 pos = transform.position;
            var mgr = RaceManager.Instance;
            if (mgr != null)
            {
                var path = mgr.path;
                if (path == null || path.Count < 2) return false;
                // The progress index follows the car along its own leg every
                // frame; this component's hint only moves while the car is
                // quick or off track, and a car dropping straight down is
                // neither. A finished car's progress stops updating.
                var progress = mgr.GetProgress(car);
                int hint = progress != null && !progress.finished ? progress.nearestIdx : pathHint;
                pathHint = path.NearestIndex(pos, hint);
                return pos.y < path.GetPoint(pathHint).y - FellBelowRoadM &&
                       Lateral(path, pathHint) < path.roadWidth * 0.5f + FellLateralMarginM;
            }

            // Free roam. A city RACE has a RaceManager and took the branch above.
            var city = City.CityMode.Instance;
            var map = city != null && city.world != null ? city.world.Map : null;
            if (map == null) return false;   // the town: no graph, and nothing to fall from
            if (!map.NearestRoadPoint(new Vector2(pos.x, pos.z), CityFellSearchM, skipLinks: false,
                                      out int ei, out float at, out float dist))
                return false;
            var edge = map.edges[ei];
            if (edge.stS == null || edge.stS.Length == 0) return false;
            return dist < edge.width * 0.5f + FellLateralMarginM &&
                   pos.y < edge.YAt(at) - FellBelowRoadM;
        }

        /// <summary>How far off the centreline stops being "running wide" and
        /// starts being "not on the road", on top of half the road's own width.
        /// Twenty metres puts it well outside the barrier line on every venue
        /// — a circuit walls at ten — so running onto the grass never trips
        /// it and being over a wall does.</summary>
        const float OffTrackMarginM = 20f;
        /// <summary>Seconds out there before the banner. Long enough that a
        /// wide moment through a corner never earns it.</summary>
        const float OffTrackSeconds = 4f;
        float offTrackTimer;

        /// <summary>
        /// THE WAY BACK, and only that: a banner, never a teleport.
        ///
        /// "It's easy to get off the track, but then it often feels like
        /// you're stuck outside of a barrier and can't get back on." Both
        /// halves are true and only the second is a bug. A guard wall is
        /// one-directional by construction — it is there to stop you leaving
        /// the road, and it stops you rejoining it just as well — so a car
        /// that gets past one can drive for kilometres with no way back. The
        /// three states above cannot see this, because all three are about a
        /// car that has stopped moving, and this one has not.
        ///
        /// It only ever SAYS so. The reset control has existed all along and
        /// is simply invisible (R, X/Square, or the pause menu), which is the
        /// whole problem; naming it is the fix. Taking the car away from
        /// someone who is still driving it is the exact complaint this
        /// component was written to answer, and a lap that runs wide past a
        /// forecourt must not end in a teleport.
        /// </summary>
        /// <returns>True if this owns the prompt on this frame.</returns>
        bool UpdateOffTrack(float dt)
        {
            var mgr = RaceManager.Instance;
            var path = mgr != null ? mgr.path : null;
            if (path == null || path.Count < 2 || car == null)
            { offTrackTimer = 0f; return false; }

            float limit = path.roadWidth * 0.5f + OffTrackMarginM;
            pathHint = path.NearestIndex(transform.position, pathHint);
            float lateral = Lateral(path, pathHint);
            if (lateral >= limit)
            {
                // Confirm against a FULL search before believing it. The
                // hinted one walks a 25-station window, so a hint left behind
                // by a spin measures the distance to the wrong part of the
                // road — and on a course that doubles back, to a part of it
                // the car is nowhere near. Only paid for while apparently off
                // track, which is rare.
                pathHint = path.NearestIndex(transform.position);
                lateral = Lateral(path, pathHint);
            }
            if (lateral < limit) { offTrackTimer = 0f; return false; }

            offTrackTimer += dt;
            if (offTrackTimer < OffTrackSeconds) return false;
            Prompt = "OFF TRACK — " + ResetControlName();
            return true;
        }

        float Lateral(TrackPath path, int idx) =>
            Vector3.ProjectOnPlane(transform.position - path.GetPoint(idx), Vector3.up).magnitude;

        /// <summary>
        /// Name the control the player actually has. Telling a pad player to
        /// press R, or a phone player to press anything, is the game not knowing
        /// what it is running on — the same mistake the finish banner used to
        /// make.
        /// </summary>
        static string ResetControlName()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "OPEN MENU — RESET CAR";
            if (Gamepad.current != null) return "PRESS X / SQUARE TO RESET";
            return "PRESS R TO RESET";
        }
    }
}
