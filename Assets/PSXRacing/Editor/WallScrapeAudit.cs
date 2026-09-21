using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// SLIDE A CAR DOWN EVERY WALL AND SEE WHETHER THE WALL GRABS IT.
    ///
    /// "I still regularly encounter invisible barriers while the car is
    /// scraping against barriers of race tracks. The walls seem smooth (even
    /// bridges for drag race), but the car goes from full speed to instantly
    /// stopped." Nothing else in the tool box can see that. The obstacle audit
    /// asks whether a collider stands inside the barrier line, the sweep asks
    /// whether a car-sized box overlaps anything where a car sits — both are
    /// questions about where things ARE, and a wall that snags is exactly
    /// where it should be. What is wrong with it is what the solver does with
    /// a car moving along it, so this moves a car along it.
    ///
    /// The car is a proxy: the player's body box, the player's mass, the
    /// player's CCD mode and the player's body friction, with no wheels. It
    /// collides with the Solid layer and nothing else — the road, the verge and
    /// the ground are the suspension's business and would only add noise — and
    /// it is flown rather than driven (Drive): a bounded thrust holds the
    /// speed the bends ahead allow, grip turns it with the road, a steady
    /// 0.6 g presses it into the wall, and its heading is held a few degrees
    /// into the wall along the road's own tangent. That is a driver leaning
    /// on a barrier through a corner, which is where the report comes from.
    /// A loss after the proxy has come off the wall is a crash across the
    /// road, not a seam, and is not counted.
    ///
    /// A snag is a speed loss no scrape can explain: more than
    /// <see cref="SnagDropMps"/> of speed along the road in ONE physics step,
    /// i.e. a deceleration of 24 g. The wall's friction at 0.6 g of pressure
    /// costs 0.04 g, so there is no overlap between the two. After a snag the
    /// proxy is put back on its line a couple of stations on and carries on,
    /// so one pass reports every seam in the run rather than the first.
    ///
    /// Both directions, because a seam is directional (a box whose end stands
    /// proud of its neighbour catches a car coming one way and not the other)
    /// and every circuit and stage has a reversed twin driven the other way.
    ///
    /// TWO MODES, because there are two fixes and each has to be seen doing
    /// its own work. PSX_SCRAPE_FILTER=0 (the default) switches
    /// <see cref="GhostContactFilter"/> off for the whole run and measures the
    /// GEOMETRY alone: a wall built as one continuous solid has no seams to
    /// catch on, and this mode is the one that proves it. PSX_SCRAPE_FILTER=1
    /// registers the proxy with the filter exactly as CarController registers
    /// the player's body, publishes its pose before every step, and counts
    /// what the filter threw away per venue — the safety net, measured on the
    /// same walls. Run both: geometry smooth with the filter off is the fix;
    /// smooth only with it on is a net catching a fall that should not happen.
    ///
    /// AND POSITIVE CONTROLS, which must hold in BOTH modes. A filter that
    /// deletes contacts can make every wall "smooth" in the most useless way
    /// there is — by letting the car through it — and a scrape audit alone
    /// would certify that as a triumph. So at the longest run on each side the
    /// proxy is also flown square at the wall at 20 m/s from 6 m out, and at
    /// 30 degrees into it at 30 m/s, with its foot in it the whole way. The
    /// wall must STOP it: the box may sink no more than one step of travel
    /// into the face (0.4 m; see ControlSinkM) and must not end the flight
    /// still going in. A control that fails is a CONTROL FAILED line, and it
    /// outranks every smooth run in the report.
    ///
    /// Neither of those crashes is the one the filter could get wrong. It
    /// only ever drops a contact while the car is ALREADY sliding on that
    /// side, and a head-on or a 30-degree strike arrives with no side contact
    /// before it — so they would pass a filter whose every other guard was
    /// broken. Hence THE STEP CONTROLS, flown the way the scrape is flown,
    /// pressed along the wall at 137 km/h into a box the harness stands on
    /// the face for the one flight:
    ///   * a 30 cm STEP, a pier or a flare — a real thing the car must hit.
    ///     PhysX alone glances a box sliding at 137 km/h sideways past a
    ///     block that shallow (measured), so the outcome is reported and the
    ///     verdict is the filter's: in filter mode it must not have dropped a
    ///     single contact while the car was at the step;
    ///   * in filter mode only, a 5 cm SEAM, the builder's old stub, flown
    ///     twice: with the filter switched off it should stop the proxy dead
    ///     (the ghost, reproduced), and with it on the proxy must slide past
    ///     it. Off-snags and on-snags is a CONTROL FAILED. It is the one
    ///     thing in this report that shows the filter is LIVE — on walls with
    ///     no seams left, "no ghosts dropped" reads the same whether the
    ///     callbacks ran or not.
    ///
    /// A clean report also needs something to have been measured: a circuit
    /// or strip where the scan finds no wall at all (every one is built with
    /// them), a venue whose proxy was mostly off its wall, and a run with no
    /// control anywhere are all HARNESS SUSPECT, never WALLS SMOOTH.
    ///
    /// Menu: PSX Racing/Audit Wall Scrape. Batch: -executeMethod
    /// PSXRacing.EditorTools.WallScrapeAudit.Run (PSX_SCRAPE_ONLY=Id1,Id2 to
    /// restrict venues, PSX_SCRAPE_FILTER=1 for the filter mode). Report:
    /// PSXRacing_wall_scrape.txt at the project root.
    /// </summary>
    public static class WallScrapeAudit
    {
        const int SolidLayer = 9;
        const int CarLayer = 2;
        static readonly Vector3 CarSize = new Vector3(1.72f, 1.0f, 4.1f);
        const float CarMass = 1280f;
        /// <summary>Box centre above the road datum: the ribbon's 12 cm lift,
        /// the body's 22 cm of daylight at rest, half the box.</summary>
        const float CentreAboveDatum = 0.84f;
        const float Dt = 1f / 60f;
        const float Speed = 38f;               // 137 km/h
        const float PressG = 0.6f;
        const float YawInDeg = 3f;
        /// <summary>Speed along the road lost in a single step that counts as a
        /// snag: 4 m/s in 1/60 s is 24 g.</summary>
        const float SnagDropMps = 4f;
        /// <summary>Stations kept clear of each end of a wall run: the run's
        /// ends are where the stone flares or ends, and hitting a flare is a
        /// real crash, not a ghost.</summary>
        const int RunEndMargin = 3;
        const int MinRunStations = 8;
        const int MaxSnagLines = 40;

        // ---- the positive controls ----
        /// <summary>Square into the wall: 72 km/h, a crash anybody would
        /// call one, from far enough out that the proxy is at speed and in
        /// clear air when it arrives.</summary>
        const float ControlHeadOnMps = 20f;
        const float ControlHeadOnGapM = 6f;
        /// <summary>The shallow crash: 108 km/h at 30 degrees, which is
        /// exactly where a filter judging "is this contact along the car's
        /// side?" has to be right about the difference between a seam and a
        /// wall. Started 3 m off the face so it lands inside the run.</summary>
        const float ControlAngledMps = 30f;
        const float ControlAngledDeg = 30f;
        const float ControlAngledGapM = 3f;
        /// <summary>How far the box may sink into the face before the wall
        /// counts as driven through. Up to ONE STEP OF TRAVEL, not
        /// centimetres: at 72 km/h the box moves 0.33 m a step, under PhysX's
        /// sweep threshold for a body this size, so the discrete pass meets
        /// the wall wherever the step lands — anything up to 0.33 m in — and
        /// pushes it out. Measured 0.32 m on Little Switzerland's old boxes
        /// and new solid alike, stopped dead both times. A car the wall does
        /// not stop goes metres through, not centimetres.</summary>
        const float ControlSinkM = 0.4f;
        /// <summary>Still moving into the wall this fast when the flight ends
        /// means the wall is not holding it...</summary>
        const float ControlStopMps = 3f;
        /// <summary>...provided it is also this deep in by then.</summary>
        const float ControlHoldDepthM = 0.1f;
        /// <summary>A control flight that never got this close to the face
        /// proves nothing about the wall and is reported as a harness fault,
        /// not a pass.</summary>
        const float ControlReachM = 0.25f;
        /// <summary>A run this long (after its end margins) holds the angled
        /// control's approach and slide without leaving it.</summary>
        const int ControlMinStations = 24;
        const int ControlHeadOnSteps = 60;
        const int ControlAngledSteps = 45;

        // ---- the step controls ----
        /// <summary>A step three times the filter's ghost band: a real
        /// obstacle by the filter's own definition, and one the path-clear
        /// sweep (the band plus 2 cm in from each side) cannot miss.</summary>
        const float StubProtrudeM = 0.30f;
        /// <summary>The seam: half the ghost band, inside the 7 cm the old
        /// box chains stood proud by. Nothing a driver can see.</summary>
        const float SeamProtrudeM = 0.05f;
        const float StepLengthM = 2f;
        /// <summary>How far the step's box reaches back INTO the wall, so its
        /// back is buried and only the face and the end stand in the road.</summary>
        const float StepBuriedM = 0.4f;
        /// <summary>Stations of pressed slide before the step: long enough
        /// for the side contact the filter's "sliding" test needs.</summary>
        const int StepApproachStations = 12;
        const int StepMaxSteps = 150;
        /// <summary>Steps held against the step after reaching it before the
        /// flight is judged.</summary>
        const int StepHoldSteps = 30;
        /// <summary>Share of the approach steps the proxy must spend on the
        /// wall for a step flight to mean anything.</summary>
        const float StepApproachShare = 0.5f;

        /// <summary>This run's mode (PSX_SCRAPE_FILTER=1), and the proxy box
        /// the filter was told about — Publish needs the collider, and the
        /// finally block needs to Unregister exactly what was registered.</summary>
        static bool filterOn;
        static BoxCollider probeBox;
        static int controlsRun, controlFailures;
        static long ghostsAlongTotal, ghostsControlTotal;
        /// <summary>Venues whose clean result proves nothing (no wall found
        /// where one is always built, or a proxy mostly off its wall).</summary>
        static int suspectVenues;
        /// <summary>Filter mode: seam controls whose filter-off flight did
        /// snag (so the on flight means something), and of those, the ones the
        /// filter slid past.</summary>
        static int seamConclusive, seamLive;

        struct Snag
        {
            public string venue, side, dir, what;
            public int station;
            public float before, after;
        }

        [MenuItem("PSX Racing/Audit Wall Scrape")]
        public static void Run()
        {
            var log = new StringBuilder();
            string only = System.Environment.GetEnvironmentVariable("PSX_SCRAPE_ONLY");
            var filter = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            // The mode is the run's, not the venue's: one report, one answer
            // to "with or without the net".
            filterOn = System.Environment.GetEnvironmentVariable("PSX_SCRAPE_FILTER") == "1";
            controlsRun = controlFailures = 0;
            ghostsAlongTotal = ghostsControlTotal = 0;
            suspectVenues = seamConclusive = seamLive = 0;
            var prevMode = Physics.simulationMode;
            bool prevFilter = GhostContactFilter.Enabled;
            int venues = 0, totalSnags = 0;
            float totalMetres = 0f;
            var summary = new StringBuilder();
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                // OFF means off for everything in the scene, not merely "the
                // proxy was not registered": a filter left enabled could still
                // be acting on anything else that registered itself.
                GhostContactFilter.Enabled = filterOn;
                foreach (var def in TrackCatalog.Scened)
                {
                    if (def.city) continue;
                    if (filter != null && !filter.Contains(def.id)) continue;
                    int snags = ScrapeOne(def, log, out float metres, out string verdict);
                    summary.AppendLine("  " + def.id.PadRight(22) + verdict);
                    if (snags >= 0) { venues++; totalSnags += snags; totalMetres += metres; }
                }
            }
            finally
            {
                Physics.simulationMode = prevMode;
                GhostContactFilter.Enabled = prevFilter;
            }

            var head = new StringBuilder();
            head.AppendLine("=== WALL SCRAPE AUDIT: a car leaning on every wall, both ways ===");
            head.AppendLine("  mode: " + (filterOn
                ? "FILTER ON - GhostContactFilter registered on the proxy, pose published every step (PSX_SCRAPE_FILTER=1)"
                : "FILTER OFF - the wall geometry alone (PSX_SCRAPE_FILTER=0)"));
            head.AppendLine("  proxy: " + CarSize.x + " x " + CarSize.y + " x " + CarSize.z + " m box, " + CarMass +
                            " kg, ContinuousDynamic, " + (Speed * 3.6f).ToString("0") + " km/h, pressed at " +
                            PressG + " g, " + YawInDeg + " deg into the wall; snag = more than " + SnagDropMps +
                            " m/s lost in one 1/60 s step");
            head.AppendLine("  controls: square at " + (ControlHeadOnMps * 3.6f).ToString("0") + " km/h from " +
                            ControlHeadOnGapM + " m, and " + ControlAngledDeg + " deg at " +
                            (ControlAngledMps * 3.6f).ToString("0") + " km/h, into the longest run on each side; " +
                            "the wall must stop the box within " + ControlSinkM + " m of its face and under " +
                            ControlStopMps + " m/s into it. Then a " + (StubProtrudeM * 100f).ToString("0") +
                            " cm step stood on the face and slid into, which must stop it" +
                            (filterOn ? ", and a " + (SeamProtrudeM * 100f).ToString("0") +
                                        " cm seam flown filter-off then filter-on, which the filter must slide it past" : ""));
            head.Append(summary);
            if (filterOn)
            {
                head.AppendLine("  ghosts dropped by the filter: " + ghostsAlongTotal + " along the walls, " +
                                ghostsControlTotal + " during the controls");
                head.AppendLine("  seam controls: " + seamConclusive + " conclusive (the seam stopped the proxy with the filter off), " +
                                seamLive + " slid past with it on" +
                                (seamConclusive == 0 ? " - nothing in this run shows the filter is live" : ""));
            }
            // The controls FIRST: a smooth report over walls a car can drive
            // through is the one result worse than a snag.
            if (controlFailures > 0)
                head.AppendLine("CONTROL FAILED: " + controlFailures + " of " + controlsRun +
                                " controls went into or through a wall, or a seam stopped the car with the filter on - " +
                                (filterOn ? "the filter is deleting real contacts or missing ghosts" : "the walls do not stop a car") +
                                "; nothing below certifies anything.");
            // Then anything that makes a clean result mean nothing. Upper
            // case, because tools/wall-scrape.ps1 fails the run on it.
            bool nothingMeasured = venues > 0 && (totalMetres <= 0f || controlsRun == 0);
            if (venues > 0 && totalMetres <= 0f)
                head.AppendLine("HARNESS SUSPECT: not one metre of wall was scraped in " + venues + " venues - the scan saw no walls.");
            else if (venues > 0 && controlsRun == 0)
                head.AppendLine("HARNESS SUSPECT: no run anywhere was long enough for a control - nothing shows these walls stop a car.");
            if (suspectVenues > 0)
                head.AppendLine("HARNESS SUSPECT: " + suspectVenues + " venue(s) below measured nothing they could be trusted on (see their lines).");
            bool trusted = controlFailures == 0 && suspectVenues == 0 && !nothingMeasured;
            head.AppendLine(totalSnags == 0
                ? (trusted
                    ? "WALLS SMOOTH: " + venues + " venues, " + (totalMetres / 1000f).ToString("0.0") + " km of wall scraped, no snags, " +
                      controlsRun + " controls held."
                    : "no snags along " + (totalMetres / 1000f).ToString("0.0") + " km, but see the failures above - not smooth until they are gone.")
                : "SNAGS: " + totalSnags + " across " + venues + " venues (" + (totalMetres / 1000f).ToString("0.0") + " km scraped).");
            string text = head.ToString() + log.ToString();
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PSXRacing_wall_scrape.txt"),
                text);
            Debug.Log(text);
        }

        /// <returns>Snag count, or -1 when the venue could not be audited.</returns>
        static int ScrapeOne(TrackCatalog.TrackDef def, StringBuilder log, out float metres, out string verdict)
        {
            metres = 0f;
            string scenePath = "Assets/PSXRacing/Scenes/" + def.id + ".unity";
            log.AppendLine("");
            log.AppendLine("wall scrape — " + def.id);
            if (!System.IO.File.Exists(scenePath))
            {
                log.AppendLine("  MISSING SCENE " + scenePath);
                verdict = "MISSING SCENE";
                return -1;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || path.Count < 4)
            {
                log.AppendLine("  no TrackPath");
                verdict = "NO TRACKPATH";
                return -1;
            }

            // Nothing else in the scene may move while the proxy does: the
            // grid's cars would fall through an edit-mode world with no
            // controller holding them up, and wake every step for nothing.
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                rb.isKinematic = true;

            int n = path.Count;
            float half = path.roadWidth * 0.5f;
            float reach = PSXRacingBuilder.WallOffsetFor(def) + 4f;
            var dist = new float[2][] { new float[n], new float[n] };
            var hitName = new string[2][] { new string[n], new string[n] };
            int walledStations = 0;
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                for (int i = 0; i < n; i++)
                {
                    dist[s][i] = -1f;
                    Vector3 c = path.GetPoint(i);
                    Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized * side;
                    Vector3 o = c + Vector3.up * 0.6f;
                    if (!Physics.Raycast(o, r, out var hit, reach, 1 << SolidLayer, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.distance < half - 0.5f) continue;                 // something ON the road: not this audit's
                    if (Vector3.Dot(hit.normal, -r) < 0.7f) continue;          // not facing the road
                    dist[s][i] = hit.distance;
                    hitName[s][i] = Path(hit.collider.transform);
                    walledStations++;
                }
            }
            log.AppendLine("  " + walledStations + " station-sides have a solid face facing the road within " +
                           reach.ToString("0.0") + " m");
            if (walledStations == 0)
            {
                // A stage may have none (walls go only where DOT warrants
                // one). A circuit or a strip never does — BuildWalls rings
                // every one — so a scan that finds nothing there is a scan
                // that cannot see the walls (wrong layer, wrong winding, not
                // built), and "smooth" over it would be the emptiest pass
                // there is.
                if (!def.stage)
                {
                    suspectVenues++;
                    log.AppendLine("  HARNESS SUSPECT: every circuit and strip is built walled, and the scan found no wall facing the road");
                    verdict = "NO WALLS FOUND  HARNESS SUSPECT";
                    return 0;
                }
                verdict = "no walls";
                return 0;
            }

            // The runs: consecutive stations with a wall at a near-constant
            // distance. A jump of more than half a metre is a different
            // surface (a flare, a portal, a bank handing over to stone) and
            // starts a new run, so the proxy is never flown into it.
            var runs = new List<(int s, int a, int b)>();
            for (int s = 0; s < 2; s++)
            {
                int start = -1;
                for (int i = 0; i <= n; i++)
                {
                    bool on = i < n && dist[s][i] > 0f &&
                              (i == 0 || start < 0 || Mathf.Abs(dist[s][i] - dist[s][i - 1]) < 0.5f);
                    if (on && start < 0) start = i;
                    else if (!on && start >= 0)
                    {
                        if (i - 1 - start + 1 >= MinRunStations) runs.Add((s, start, i - 1));
                        start = i < n && dist[s][i] > 0f ? i : -1;
                    }
                }
            }

            var probe = MakeProbe();
            var snags = new List<Snag>();
            int steps = 0, contactSteps = 0;
            int ghostsAlong = 0, ghostsControls = 0, controlFails = 0, controls = 0;
            var controlLog = new StringBuilder();
            try
            {
                // Per venue, so a count is the walls of THIS venue.
                if (filterOn) GhostContactFilter.ResetStats();
                reseats = 0;
                offWallLosses = 0;
                foreach (var run in runs)
                {
                    foreach (int dirSign in new[] { 1, -1 })
                    {
                        int from = dirSign > 0 ? run.a + RunEndMargin : run.b - RunEndMargin;
                        int to = dirSign > 0 ? run.b - RunEndMargin : run.a + RunEndMargin;
                        if ((to - from) * dirSign < 4) continue;
                        FlyRun(def, path, probe, run.s, from, to, dirSign, dist, snags,
                               ref steps, ref contactSteps, ref metres);
                    }
                }
                if (filterOn)
                {
                    ghostsAlong = GhostContactFilter.IgnoredCount;
                    // Counted apart from the scrape: a filter that drops
                    // contacts while a car is being driven INTO a wall is the
                    // failure the controls exist to catch, and even when the
                    // wall holds, a count here is worth reading.
                    GhostContactFilter.ResetStats();
                }
                controlFails = RunControls(path, probe, runs, dist, controlLog, out controls);
                if (filterOn) ghostsControls = GhostContactFilter.IgnoredCount;
            }
            finally { DestroyProbe(probe); }
            controlsRun += controls;
            controlFailures += controlFails;
            ghostsAlongTotal += ghostsAlong;
            ghostsControlTotal += ghostsControls;

            float contactShare = steps > 0 ? (float)contactSteps / steps : 0f;
            log.AppendLine("  " + runs.Count + " wall runs, " + (metres / 1000f).ToString("0.00") + " km flown along them, " +
                           "in contact " + (contactShare * 100f).ToString("0") + "% of " + steps + " steps; " +
                           reseats + " re-seat(s) after coming off the wall, " + offWallLosses +
                           " speed loss(es) off the wall not counted as snags");
            // THE CONTROL. A proxy that bounced off the first wall and flew
            // down the middle of the road would pass every run; the contact
            // share says it was actually leaning on the wall while it was
            // measured.
            if (steps > 0 && contactShare < 0.6f)
            {
                suspectVenues++;
                log.AppendLine("  HARNESS SUSPECT: the proxy was off the wall for most of the run — a clean result here proves nothing");
            }
            else if (steps == 0 && runs.Count == 0)
                log.AppendLine("  (walls seen, but no run of " + MinRunStations + " stations at a steady distance to fly along)");

            var byWhat = new Dictionary<string, int>();
            foreach (var sn in snags) { byWhat.TryGetValue(sn.what, out int k); byWhat[sn.what] = k + 1; }
            foreach (var kv in byWhat)
                log.AppendLine("  touching at the snag: " + kv.Key + "  x" + kv.Value);
            int shown = 0;
            foreach (var sn in snags)
            {
                if (shown++ >= MaxSnagLines) { log.AppendLine("  ... " + (snags.Count - MaxSnagLines) + " more"); break; }
                log.AppendLine("  SNAG  wp " + sn.station + " " + sn.side + ", driving " + sn.dir + ": " +
                               (sn.before * 3.6f).ToString("0") + " -> " + (sn.after * 3.6f).ToString("0") +
                               " km/h in one step  [" + sn.what + "]");
            }
            if (snags.Count == 0) log.AppendLine("  SMOOTH — no snag in either direction on any run");
            if (filterOn)
                log.AppendLine("  ghosts dropped: " + ghostsAlong + " along the walls, " + ghostsControls + " during the controls");
            log.Append(controlLog);
            if (controls == 0)
                log.AppendLine("  (no run long enough for a control: " + (2 * RunEndMargin + ControlMinStations) + " stations wanted)");
            verdict = (snags.Count == 0 ? "smooth" : snags.Count + " SNAGS") + "  (" + (metres / 1000f).ToString("0.0") +
                      " km, contact " + (contactShare * 100f).ToString("0") + "%)" +
                      (controls == 0 ? "  no controls"
                       : controlFails == 0 ? "  controls " + controls + "/" + controls + " held"
                       : "  CONTROL FAILED x" + controlFails) +
                      (filterOn ? "  ghosts " + ghostsAlong : "") +
                      (steps > 0 && contactShare < 0.6f ? "  HARNESS SUSPECT" : "");
            return snags.Count;
        }

        /// <summary>
        /// THE POSITIVE CONTROLS: the longest run on each side, driven INTO,
        /// square and at thirty degrees. Everything else this audit measures
        /// is "did the wall let go of the car"; this is the other half, "did
        /// the wall hold it" — and it must, in both modes, or a filter that
        /// deletes contacts would pass the scrape by the simple expedient of
        /// having no walls.
        /// </summary>
        /// <returns>Controls that failed.</returns>
        static int RunControls(TrackPath path, Rigidbody rb, List<(int s, int a, int b)> runs, float[][] dist,
                               StringBuilder log, out int run)
        {
            run = 0;
            int fails = 0;
            for (int s = 0; s < 2; s++)
            {
                int best = -1, bestLen = 0;
                for (int i = 0; i < runs.Count; i++)
                {
                    int len = runs[i].b - runs[i].a + 1;
                    if (runs[i].s == s && len > bestLen) { best = i; bestLen = len; }
                }
                if (best < 0 || bestLen < 2 * RunEndMargin + ControlMinStations) continue;
                int mid = (runs[best].a + runs[best].b) / 2;
                foreach (bool headOn in new[] { true, false })
                {
                    run++;
                    bool held = FlyControl(path, rb, s, mid, headOn, dist, out string got, out string why);
                    string what = (headOn
                                      ? "square at " + (ControlHeadOnMps * 3.6f).ToString("0") + " km/h"
                                      : ControlAngledDeg.ToString("0") + " deg at " + (ControlAngledMps * 3.6f).ToString("0") + " km/h") +
                                  " into the " + (s == 0 ? "L" : "R") + " wall at wp " + mid;
                    if (held) log.AppendLine("  ok   control " + what + ": stopped by the wall  [" + got + "]");
                    else
                    {
                        fails++;
                        log.AppendLine("  CONTROL FAILED " + what + ": " + why + "  [" + got + "]");
                    }
                }

                // ---- THE STEP CONTROLS: the proxy SLIDING, as in the scrape,
                // into something stood on the face. Near the start of the same
                // run, so the approach is a full pressed slide along it.
                int kStart = runs[best].a + RunEndMargin;
                int kStep = kStart + StepApproachStations;
                string sideName = s == 0 ? "L" : "R";

                // WHAT THIS CONTROL CAN AND CANNOT ASK. It was written as "the
                // step must stop the car", and PhysX does not do that on its
                // own: measured on DragQuarter with the filter dropping
                // nothing, a box sliding at 137 km/h moves 0.63 m a step, lands
                // 0.58 m past a 30 cm step's face and 0.30 m into it sideways,
                // and the solver takes the SHALLOW way out — sideways, for
                // 3 m/s. A 30 cm block on a wall is a glancing bump to a car
                // leaning on the wall, with or without any filter. So the
                // outcome is reported, and the verdict is the one thing the
                // filter is answerable for: with it on, not one contact was
                // dropped while the car was AT a step deeper than its band
                // (its path sweep must have seen the step and stood down).
                run++;
                var stub = FlyStep(path, rb, s, kStart, kStep, StubProtrudeM, dist);
                string stubWhat = "a " + (StubProtrudeM * 100f).ToString("0") + " cm step on the " + sideName +
                                  " wall at wp " + kStep + ", slid into at " + (Speed * 3.6f).ToString("0") + " km/h";
                string outcome = stub.snagged ? "stopped by it" : stub.cleared ? "glanced past it (the solver's sideways way out)"
                               : "hit it";
                if (!stub.reached || stub.share < StepApproachShare)
                    log.AppendLine("  --   control " + stubWhat + ": " +
                                   (!stub.reached ? "never reached the step" : "was not sliding on the wall when it got there") +
                                   " (harness) - proves nothing  [" + stub.Describe() + "]");
                else if (filterOn && stub.stepGhosts > 0)
                {
                    fails++;
                    log.AppendLine("  CONTROL FAILED " + stubWhat + ": the filter dropped " + stub.stepGhosts +
                                   " contact(s) from a step deeper than its band - it is deleting real contacts  [" + stub.Describe() + "]");
                }
                else
                    log.AppendLine("  ok   control " + stubWhat + ": " + outcome +
                                   (filterOn ? ", and the filter left every contact with it alone" : "") + "  [" + stub.Describe() + "]");

                if (!filterOn) continue;
                // The seam, twice. Off first: if it does not stop the proxy
                // with nothing in the way, the synthetic seam has not
                // reproduced the ghost at this spot and the on-flight is
                // measuring nothing — reported, not failed.
                StepResult off;
                bool wasEnabled = GhostContactFilter.Enabled;
                try
                {
                    GhostContactFilter.Enabled = false;
                    off = FlyStep(path, rb, s, kStart, kStep, SeamProtrudeM, dist);
                }
                finally { GhostContactFilter.Enabled = wasEnabled; }
                var on = FlyStep(path, rb, s, kStart, kStep, SeamProtrudeM, dist);
                string seamWhat = "a " + (SeamProtrudeM * 100f).ToString("0") + " cm seam on the " + sideName +
                                  " wall at wp " + kStep;
                string seamGot = "filter off: " + off.Describe() + "; filter on: " + on.Describe();
                if (off.share < StepApproachShare || on.share < StepApproachShare)
                    log.AppendLine("  --   seam control " + seamWhat + ": not sliding on the wall at the seam (harness) - proves nothing  [" +
                                   seamGot + "]");
                else if (!off.snagged)
                    log.AppendLine("  --   seam control " + seamWhat + ": did not stop the proxy even with the filter off - " +
                                   "no ghost reproduced here, so the filter-on flight shows nothing  [" + seamGot + "]");
                else
                {
                    run++;
                    seamConclusive++;
                    if (on.cleared && !on.snagged)
                    {
                        seamLive++;
                        log.AppendLine("  ok   seam control " + seamWhat + ": stopped the proxy dead with the filter off, " +
                                       "slid past with it on - the filter is live  [" + seamGot + "]");
                    }
                    else
                    {
                        // Both flights start from the same pose and speed in a
                        // deterministic edit-mode step, so whatever differs
                        // between them is the filter's doing.
                        fails++;
                        string seamWhy = on.snagged ? "the seam still stopped it dead with the filter on"
                                       : !on.reached ? "with the filter on the proxy never got to the seam"
                                       : "with the filter on it neither stopped at nor got past the seam";
                        log.AppendLine("  CONTROL FAILED seam control " + seamWhat + ": " + seamWhy +
                                       " - the filter did not drop a seam that stops the car without it  [" + seamGot + "]");
                    }
                }
            }
            return fails;
        }

        /// <summary>One step flight, as <see cref="FlyStep"/> measured it.</summary>
        struct StepResult
        {
            /// <summary>The proxy's front got within
            /// <see cref="ControlReachM"/> of the step's face.</summary>
            public bool reached;
            /// <summary>More than <see cref="SnagDropMps"/> of speed gone in
            /// one step while the proxy's front was AT the step (from 1.5 m
            /// short of its face to a metre past its end).</summary>
            public bool snagged;
            /// <summary>The proxy's front got a metre past the step's far
            /// end: it went by.</summary>
            public bool cleared;
            /// <summary>Furthest the proxy's front got past the step's near
            /// face, along the road.</summary>
            public float sink;
            public float endAlong, worstDrop, share;
            /// <summary>Contacts the filter dropped over the whole flight, and
            /// over the part of it with the proxy's front AT the step (1.5 m
            /// short of its face to a metre past its end) — the only ones that
            /// can be the step's own.</summary>
            public int ghosts, stepGhosts;

            public string Describe() =>
                "front " + (sink == float.MinValue ? "-" : sink.ToString("+0.00;-0.00") + " m") + " past the face at most, " +
                "worst step at it -" + worstDrop.ToString("0.0") + " m/s, " + endAlong.ToString("0.0") + " m/s at the end, " +
                "on the wall " + (share * 100f).ToString("0") + "% of the approach" +
                (filterOn ? ", ghosts dropped " + ghosts + " (" + stepGhosts + " at the step)" : "");
        }

        /// <summary>
        /// Stand a box of the given protrusion on the wall's face at station
        /// <paramref name="kStep"/>, slide the proxy into it from
        /// <paramref name="kStart"/> exactly as the scrape flies (pressed at
        /// <see cref="PressG"/>, nose <see cref="YawInDeg"/> in, speed held),
        /// and take the box away again. The step's face is square to the road
        /// and its back is buried <see cref="StepBuriedM"/> in the wall, so all
        /// that stands in the road is the face and the end the car meets.
        /// </summary>
        static StepResult FlyStep(TrackPath path, Rigidbody rb, int s, int kStart, int kStep, float protrude,
                                  float[][] dist)
        {
            var res = new StepResult { sink = float.MinValue };
            float side = s == 0 ? -1f : 1f;
            Vector3 c = path.GetPoint(kStep);
            Vector3 t = path.GetTangent(kStep); t.y = 0f; t.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, t).normalized * side;
            float width = protrude + StepBuriedM;
            // Lateral span [dist - protrude, dist + buried] from the
            // centreline; height from 0.3 m under the datum to 2.1 m over it,
            // round the proxy's 0.34 - 1.34 m.
            Vector3 centre = c + r * (dist[s][kStep] - protrude + width * 0.5f) + Vector3.up * 0.9f;
            Vector3 face = centre - t * (StepLengthM * 0.5f);
            var go = new GameObject("WallScrapeStep");
            go.layer = SolidLayer;
            go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(t, Vector3.up));
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(width, 2.4f, StepLengthM);
            int ghosts0 = GhostContactFilter.IgnoredCount;
            try
            {
                Physics.SyncTransforms();
                Place(path, rb, s, kStart, 1, dist);
                float prevAlong = Vector3.Dot(rb.linearVelocity, path.GetTangent(kStart));
                int k = kStart, approach = 0, approachOn = 0, sinceReach = 0;
                for (int i = 0; i < StepMaxSteps; i++)
                {
                    k = path.NearestIndex(rb.position, k, 6);
                    if (dist[s][k] <= 0f) break;
                    Vector3 tk = path.GetTangent(k);
                    // The scrape's own driver, so the proxy arrives at the
                    // step exactly as it arrives at a seam in the scrape.
                    Drive(path, rb, k, side, 1);

                    float sinkBefore = Vector3.Dot(rb.position - face, t) + Support(rb.rotation, t);
                    int ghostsBefore = GhostContactFilter.IgnoredCount;
                    if (filterOn) GhostContactFilter.Publish(probeBox);
                    Physics.Simulate(Dt);

                    // The proxy's leading corner (the wall-side one: the nose
                    // is yawed in) against the step's near face, along the road.
                    float sink = Vector3.Dot(rb.position - face, t) + Support(rb.rotation, t);
                    if (sink > res.sink) res.sink = sink;
                    // Ghosts dropped during a step that ended with the front at
                    // the step, or started there: the only ones that can be
                    // contacts with the step itself.
                    if (sink > -1.5f && sinkBefore < StepLengthM + 1f)
                        res.stepGhosts += GhostContactFilter.IgnoredCount - ghostsBefore;

                    // A snag counts only AT the step: one on the approach is
                    // the wall's own (a seam the scrape reports anyway), and
                    // crediting it to the step would call a seam control
                    // conclusive on the strength of a different seam.
                    float now = Vector3.Dot(rb.linearVelocity, tk);
                    float drop = prevAlong - now;
                    prevAlong = now;
                    if (sink > -1.5f && sink < StepLengthM + 1f)
                    {
                        if (drop > res.worstDrop) res.worstDrop = drop;
                        if (drop > SnagDropMps) res.snagged = true;
                    }
                    if (sink < -1f)
                    {
                        approach++;
                        if (Touching(rb, out _)) approachOn++;
                    }
                    if (res.reached) sinceReach++;
                    else if (sink > -ControlReachM) res.reached = true;
                    if (sink > StepLengthM + 1f) { res.cleared = true; break; }
                    if (sinceReach >= StepHoldSteps) break;
                }
                res.endAlong = Vector3.Dot(rb.linearVelocity, t);
                res.share = approach > 0 ? (float)approachOn / approach : 0f;
            }
            finally
            {
                Object.DestroyImmediate(go);
                Physics.SyncTransforms();
            }
            res.ghosts = GhostContactFilter.IgnoredCount - ghosts0;
            return res;
        }

        /// <summary>
        /// Fly the proxy at the wall and say whether the wall stopped it.
        ///
        /// The depth is measured against the wall AT THE STATION THE BOX IS
        /// BESIDE, off the same scan the runs came from, rather than against
        /// one plane at the aim point: the angled flight slides twenty metres
        /// along the face after it lands, and on a 50 m bend a fixed plane is
        /// two metres adrift of the wall by the end of it.
        /// </summary>
        static bool FlyControl(TrackPath path, Rigidbody rb, int s, int k, bool headOn, float[][] dist,
                               out string got, out string why)
        {
            float side = s == 0 ? -1f : 1f;
            Vector3 c = path.GetPoint(k);
            Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(k)).normalized * side;
            // Square to the FACE, which is what "head-on" means to the car —
            // the scan's ray only says the face is within 45 degrees of the
            // road's right vector.
            Vector3 n = -r;
            if (Physics.Raycast(c + Vector3.up * 0.6f, r, out var hit, dist[s][k] + 1f, 1 << SolidLayer,
                                QueryTriggerInteraction.Ignore))
            {
                Vector3 hn = hit.normal; hn.y = 0f;
                if (hn.sqrMagnitude > 1e-4f) n = hn.normalized;
            }

            float speed = headOn ? ControlHeadOnMps : ControlAngledMps;
            // The angled flight starts back up the road by as far as it needs
            // to close 3 m at 30 degrees, so it lands at the aim station.
            int back = headOn ? 0 : Mathf.CeilToInt(ControlAngledGapM / Mathf.Tan(ControlAngledDeg * Mathf.Deg2Rad) / path.spacing);
            int kS = path.Wrap(k - back);
            if (dist[s][kS] <= 0f)
            {
                got = "wp " + kS + " has no wall";
                why = "never reached the wall (harness: the approach left the run)";
                return false;
            }
            Vector3 cS = path.GetPoint(kS);
            Vector3 tS = path.GetTangent(kS);
            Vector3 rS = Vector3.Cross(Vector3.up, tS).normalized * side;
            Vector3 heading = headOn ? -n : Quaternion.AngleAxis(ControlAngledDeg * side, Vector3.up) * tS;
            heading.y = 0f;
            heading.Normalize();
            Quaternion rot = Quaternion.LookRotation(heading, Vector3.up);

            // As far out as the road allows, up to the full gap: a narrow
            // stage has the far wall inside 6 m + a car length of the near
            // one, and a proxy started half inside it measures that.
            float gap = headOn ? ControlHeadOnGapM : ControlAngledGapM;
            Vector3 at = Vector3.zero;
            bool clear = false;
            foreach (float g in new[] { 6f, 4.5f, 3f, 2f, 1.2f })
            {
                if (g > gap + 1e-3f) continue;
                at = cS + rS * (dist[s][kS] - Support(rot, rS) - g);
                at.y = DatumY(path, at, kS) + CentreAboveDatum;
                if (!Physics.CheckBox(at, CarSize * 0.5f, rot, 1 << SolidLayer, QueryTriggerInteraction.Ignore))
                {
                    clear = true;
                    gap = g;
                    break;
                }
            }
            if (!clear)
            {
                got = "no clear spot to start from";
                why = "never reached the wall (harness: nowhere to start the flight)";
                return false;
            }

            rb.position = at;
            rb.rotation = rot;
            rb.transform.SetPositionAndRotation(at, rot);
            rb.linearVelocity = heading * speed;
            rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            float minGap = float.MaxValue, endGap = float.MaxValue;
            int kk = kS;
            Vector3 rEnd = rS;
            int stepsN = headOn ? ControlHeadOnSteps : ControlAngledSteps;
            for (int i = 0; i < stepsN; i++)
            {
                kk = path.NearestIndex(rb.position, kk, 8);
                Vector3 tk = path.GetTangent(kk);
                // The angled flight keeps its thirty degrees to the ROAD as
                // it slides, so a bend does not quietly turn it into a
                // head-on or a graze halfway through.
                Vector3 want = headOn ? heading : Quaternion.AngleAxis(ControlAngledDeg * side, Vector3.up) * tk;
                want.y = 0f;
                want.Normalize();

                Vector3 v = rb.linearVelocity;
                float y = DatumY(path, rb.position, kk) + CentreAboveDatum;
                v.y = (y - rb.position.y) / Dt * 0.5f;
                rb.linearVelocity = v;
                // The driver's foot stays in it: holds the flight's speed
                // along its heading, up to 0.8 g — against a wall that has
                // stopped the box, that is 0.8 g shoving it into the face.
                float aFwd = Mathf.Clamp((speed - Vector3.Dot(v, want)) * 3f, 0f, 8f);
                rb.AddForce(want * aFwd, ForceMode.Acceleration);

                Vector3 fwd = rb.transform.forward; fwd.y = 0f;
                float err = Vector3.SignedAngle(fwd, want, Vector3.up) * Mathf.Deg2Rad;
                rb.angularVelocity = Vector3.up * Mathf.Clamp(err * 10f, -3f, 3f);

                if (filterOn) GhostContactFilter.Publish(probeBox);
                Physics.Simulate(Dt);

                kk = path.NearestIndex(rb.position, kk, 8);
                if (dist[s][kk] <= 0f) continue;
                Vector3 rk = Vector3.Cross(Vector3.up, path.GetTangent(kk)).normalized * side;
                float lat = Vector3.Dot(rb.position - path.GetPoint(kk), rk);
                float gapNow = dist[s][kk] - lat - Support(rb.rotation, rk);
                if (gapNow < minGap) minGap = gapNow;
                endGap = gapNow;
                rEnd = rk;
            }
            float intoEnd = Vector3.Dot(rb.linearVelocity, rEnd);
            bool reached = minGap < ControlReachM;
            got = "started " + gap.ToString("0.0") + " m off, deepest " +
                  (minGap == float.MaxValue ? "-" : (-minGap).ToString("+0.00;-0.00") + " m into the face") +
                  ", " + intoEnd.ToString("0.0") + " m/s into it at the end";
            if (minGap < -ControlSinkM) why = "drove through the wall";
            // Moving "into" the wall is measured along the ROAD's lateral
            // axis, which on a bend or a flare is not the wall's normal: a
            // proxy sliding along a curving wall at 11 cm deep read as 5 m/s
            // into it (Harbor Point wp 59). A wall that is not holding lets
            // the box in, so the speed only counts with depth to show for it.
            else if (intoEnd >= ControlStopMps && endGap < -ControlHoldDepthM)
                why = "was still moving into the wall at the end - it is not holding";
            else if (!reached) why = "never reached the wall (harness)";
            else why = "";
            return why.Length == 0;
        }

        /// <summary>Half the proxy's extent along <paramref name="dir"/> in
        /// pose <paramref name="rot"/>: how far its surface reaches toward a
        /// wall whose normal that is.</summary>
        static float Support(Quaternion rot, Vector3 dir)
        {
            Vector3 e = CarSize * 0.5f;
            return e.x * Mathf.Abs(Vector3.Dot(rot * Vector3.right, dir)) +
                   e.y * Mathf.Abs(Vector3.Dot(rot * Vector3.up, dir)) +
                   e.z * Mathf.Abs(Vector3.Dot(rot * Vector3.forward, dir));
        }

        static Rigidbody MakeProbe()
        {
            var go = new GameObject("WallScrapeProbe");
            go.layer = CarLayer;
            var box = go.AddComponent<BoxCollider>();
            box.size = CarSize;
            box.sharedMaterial = new PhysicsMaterial("ScrapeProbe")
            {
                dynamicFriction = PSXRacingBuilder.CarSlideFriction,
                staticFriction = PSXRacingBuilder.CarSlideFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = CarMass;
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.None;
            // The wall and nothing else: the ground is the suspension's.
            rb.excludeLayers = ~(1 << SolidLayer);
            // In filter mode the proxy is registered the way CarController
            // registers the player's body, so the filter sees the same box,
            // the same velocity and the same published pose it would in the
            // game — anything less and the A/B measures a different filter.
            probeBox = box;
            if (filterOn) GhostContactFilter.Register(box);
            return rb;
        }

        /// <summary>The proxy, its material and its registration, all gone:
        /// the next venue opens a scene with nothing of this one left in the
        /// filter's registry.</summary>
        static void DestroyProbe(Rigidbody rb)
        {
            var box = rb != null ? rb.GetComponent<BoxCollider>() : null;
            if (box != null && filterOn) GhostContactFilter.Unregister(box);
            var mat = box != null ? box.sharedMaterial : null;
            if (rb != null) Object.DestroyImmediate(rb.gameObject);
            if (mat != null) Object.DestroyImmediate(mat);
            probeBox = null;
        }

        static void FlyRun(TrackCatalog.TrackDef def, TrackPath path, Rigidbody rb, int s, int from, int to, int dirSign,
                           float[][] dist, List<Snag> snags, ref int steps, ref int contactSteps, ref float metres)
        {
            float side = s == 0 ? -1f : 1f;
            int k = from;
            Place(path, rb, s, k, dirSign, dist);
            Vector3 lastPos = rb.position;
            float prevAlong = Vector3.Dot(rb.linearVelocity, path.GetTangent(k) * dirSign);
            int sinceTouch = 0;
            int guard = Mathf.Abs(to - from) * 8 + 200;
            while (guard-- > 0)
            {
                k = path.NearestIndex(rb.position, k, 6);
                if ((to - k) * dirSign <= 0) break;
                // Off the run (it ended, or the proxy was thrown clear of it):
                // stop rather than fly into whatever is beyond.
                if (dist[s][k] <= 0f) break;

                Vector3 t = path.GetTangent(k) * dirSign;
                Drive(path, rb, k, side, dirSign);

                // Main thread, once per step, BEFORE the step: the filter's
                // callback runs on the physics thread and may not read a
                // transform, so it judges contacts against this snapshot.
                if (filterOn) GhostContactFilter.Publish(probeBox);
                Physics.Simulate(Dt);
                steps++;

                Vector3 moved = rb.position - lastPos; moved.y = 0f;
                metres += moved.magnitude;
                lastPos = rb.position;

                float now = Vector3.Dot(rb.linearVelocity, t);
                bool touching = Touching(rb, out string what);
                if (touching) contactSteps++;
                bool scraping = sinceTouch <= ScrapeMemorySteps;
                sinceTouch = touching ? 0 : sinceTouch + 1;
                // OFF THE WALL: a proxy that has not touched anything for a
                // third of a second is not scraping any more — it lost an
                // inside wall on a bend its grip could not hold, and what it
                // meets next is a crash across the road, not a seam. It goes
                // back on the wall where it is, and the loss that follows is
                // not a snag.
                if (sinceTouch > ReseatSteps)
                {
                    reseats++;
                    Place(path, rb, s, k, dirSign, dist);
                    lastPos = rb.position;
                    prevAlong = Vector3.Dot(rb.linearVelocity, t);
                    sinceTouch = 0;
                    continue;
                }
                if (prevAlong - now > SnagDropMps && !scraping)
                {
                    // A loss with no scrape behind it (the proxy had come off
                    // the wall): back on the line, not counted.
                    offWallLosses++;
                    k = path.Wrap(k + 2 * dirSign);
                    if ((to - k) * dirSign <= 0 || dist[s][k] <= 0f) break;
                    Place(path, rb, s, k, dirSign, dist);
                    lastPos = rb.position;
                    prevAlong = Vector3.Dot(rb.linearVelocity, path.GetTangent(k) * dirSign);
                    sinceTouch = 0;
                    continue;
                }
                if (prevAlong - now > SnagDropMps)
                {
                    snags.Add(new Snag
                    {
                        venue = def.id, side = s == 0 ? "L" : "R", dir = dirSign > 0 ? "forward" : "backward",
                        station = k, before = prevAlong, after = now,
                        what = touching ? what : "nothing (a contact the step already resolved)",
                    });
                    // Back on the line two stations on, at speed, and carry on:
                    // one pass reports every seam in the run.
                    k = path.Wrap(k + 2 * dirSign);
                    if ((to - k) * dirSign <= 0 || dist[s][k] <= 0f) break;
                    Place(path, rb, s, k, dirSign, dist);
                    lastPos = rb.position;
                    prevAlong = Vector3.Dot(rb.linearVelocity, path.GetTangent(k) * dirSign);
                    sinceTouch = 0;
                    continue;
                }
                prevAlong = now;
            }
        }

        /// <summary>Steps since the proxy last touched a solid that still count
        /// as scraping: the solver trades a pressed contact on and off every
        /// few steps.</summary>
        const int ScrapeMemorySteps = 3;
        /// <summary>Steps off every wall after which the proxy is put back on
        /// the one it is supposed to be leaning on.</summary>
        const int ReseatSteps = 20;
        /// <summary>
        /// The proxy's GRIP, m/s^2 — what a car's tyres give it to turn with
        /// the road. The first version had none: a steady 0.6 g press and a
        /// heading held along the road, which on a bend with the scraped wall
        /// on the INSIDE needed v^2/R of 2-3 g it could not make. It flew on
        /// straight, crossed the track and hit the far wall at 20-40 degrees
        /// — City Circuit wp 185, Harbor Point wp 125/131 — and the report
        /// called the far wall a seam. Now lateral velocity away from the wall
        /// is taken back at up to this, like tyres would, and the speed is held
        /// to what the bend allows on it (sqrt(grip * R)).
        /// </summary>
        const float GripMps2 = 15f;
        static int reseats, offWallLosses;

        /// <summary>Braking the driver will do for a bend ahead, m/s^2.</summary>
        const float BrakeMps2 = 8f;
        /// <summary>Stations of road the driver reads ahead for a bend: 100 m,
        /// the distance to come down from 137 km/h to a hairpin's 50 at
        /// <see cref="BrakeMps2"/>.</summary>
        const int BendLookStations = 25;

        /// <summary>
        /// The fastest the proxy may be going at station k and still make
        /// every bend in the next <see cref="BendLookStations"/> on
        /// <see cref="GripMps2"/>: min over the stations j ahead of
        /// sqrt(grip * R_j + 2 * brake * distance to j). Reading only the
        /// station underfoot put it into Blowing Rock's R 15 m kink at wp 1981
        /// at 130 km/h — a crash any car would have there, reported as three
        /// snags.
        /// </summary>
        static float BendSpeed(TrackPath path, int k, int dirSign)
        {
            float best = Speed;
            if (path.curvatures == null) return best;
            for (int o = 0; o <= BendLookStations; o++)
            {
                int j = path.Wrap(k + o * dirSign);
                if (j < 0 || j >= path.curvatures.Length) break;
                float kappa = path.curvatures[j];
                if (kappa <= 1e-4f) continue;
                float v = Mathf.Sqrt(GripMps2 / kappa + 2f * BrakeMps2 * o * path.spacing);
                if (v < best) best = v;
            }
            return best;
        }

        /// <summary>
        /// One step of the proxy's driver, before Physics.Simulate: seat it on
        /// the road datum, hold the speed the bend allows, turn it with the
        /// road on grip, lean it on the wall at <see cref="PressG"/>, and keep
        /// its nose <see cref="YawInDeg"/> into the wall.
        /// </summary>
        static void Drive(TrackPath path, Rigidbody rb, int k, float side, int dirSign)
        {
            Vector3 t = path.GetTangent(k) * dirSign;
            Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(k)).normalized * side;
            Vector3 v = rb.linearVelocity;
            float along = Vector3.Dot(v, t);

            // Height: the road datum under the proxy, interpolated between
            // the stations either side, plus the body's seat.
            float y = DatumY(path, rb.position, k) + CentreAboveDatum;
            v.y = (y - rb.position.y) / Dt * 0.5f;
            rb.linearVelocity = v;

            float target = BendSpeed(path, k, dirSign);
            // Braking into a bend at 8 m/s^2 is 0.13 m/s a step: nowhere near a
            // snag's 4.
            float aFwd = Mathf.Clamp((target - along) * 3f, -8f, 8f);
            float vLat = Vector3.Dot(v, r);          // + toward the wall
            float aLat = PressG * 9.81f + Mathf.Clamp(-vLat * 8f, 0f, GripMps2);
            rb.AddForce(t * aFwd + r * aLat, ForceMode.Acceleration);

            Vector3 want = Quaternion.AngleAxis(YawInDeg * side * dirSign, Vector3.up) * t;
            Vector3 fwd = rb.transform.forward; fwd.y = 0f;
            float err = Vector3.SignedAngle(fwd, want, Vector3.up) * Mathf.Deg2Rad;
            rb.angularVelocity = Vector3.up * Mathf.Clamp(err * 10f, -3f, 3f);
        }

        static void Place(TrackPath path, Rigidbody rb, int s, int k, int dirSign, float[][] dist)
        {
            float side = s == 0 ? -1f : 1f;
            Vector3 c = path.GetPoint(k);
            Vector3 t = path.GetTangent(k) * dirSign;
            Vector3 r = Vector3.Cross(Vector3.up, path.GetTangent(k)).normalized * side;
            Vector3 heading = Quaternion.AngleAxis(YawInDeg * side * dirSign, Vector3.up) * t;
            // Clear of the wall by the yawed box's front corner and a hair.
            float lateral = dist[s][k] - CarSize.x * 0.5f - CarSize.z * 0.5f * Mathf.Sin(YawInDeg * Mathf.Deg2Rad) - 0.03f;
            Vector3 at = c + r * lateral + Vector3.up * CentreAboveDatum;
            rb.position = at;
            rb.rotation = Quaternion.LookRotation(heading, Vector3.up);
            rb.transform.SetPositionAndRotation(at, rb.rotation);
            // At the speed the bends ahead allow, not flat out: re-seated at
            // 137 km/h a few stations short of Blowing Rock's R 15 m kink, the
            // proxy crashed there on the driver's account, not the wall's.
            rb.linearVelocity = t * BendSpeed(path, k, dirSign);
            rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }

        static float DatumY(TrackPath path, Vector3 p, int k)
        {
            Vector3 a = path.GetPoint(k);
            Vector3 t = path.GetTangent(k);
            float u = Vector3.Dot(p - a, t);
            Vector3 b = path.GetPoint(u >= 0f ? k + 1 : k - 1);
            float span = Mathf.Max(0.01f, Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)));
            return Mathf.Lerp(a.y, b.y, Mathf.Clamp01(Mathf.Abs(u) / span));
        }

        /// <summary>Is the proxy against a solid right now, and which ones —
        /// named with their collider type, which is the question the report
        /// is really asking (a box chain, or one surface).</summary>
        static bool Touching(Rigidbody rb, out string what)
        {
            var hits = Physics.OverlapBox(rb.position, CarSize * 0.5f + new Vector3(0.04f, 0f, 0.04f), rb.rotation,
                                          1 << SolidLayer, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) { what = ""; return false; }
            var names = new SortedSet<string>();
            foreach (var h in hits) names.Add(Kind(h));
            what = hits.Length + " collider(s): " + string.Join(", ", names);
            return true;
        }

        static string Kind(Collider c)
        {
            string type = c is MeshCollider mc ? (mc.convex ? "convex mesh" : "mesh") : c.GetType().Name.Replace("Collider", " box").Replace("Box box", "box");
            string name = c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name;
            return name + " (" + type + ")";
        }

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
