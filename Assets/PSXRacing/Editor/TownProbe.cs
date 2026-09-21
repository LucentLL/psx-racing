using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;
using PSXRacing.OnFoot;
using PSXRacing.Town;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Photographs the town and the seller's street, and prints the
    /// measurements that decide whether either of them is standing up.
    ///
    /// It exists for the same reason PizzeriaPreview does: every placement
    /// failure in a code-built scene is SILENT. A house facing the wrong way, a
    /// car spawned inside its own garage, a shop seated a metre under its own
    /// apron and a fence built at the wrong scale all load without an error and
    /// all photograph as "the scene did not build". Numbers alone cannot catch
    /// them either — the first town build reported a perfectly sensible spawn
    /// point in front of a house that was showing the street its back garden.
    ///
    /// Renders through its OWN camera, deliberately: the scene's camera carries
    /// PSXCameraOutput, which takes it off screen and into a RenderTexture, and
    /// borrowing it here would photograph whatever the blit chain last left
    /// lying around.
    /// </summary>
    public static class TownProbe
    {
        const string OutDir = "Screenshots/Town";

        [MenuItem("PSX Racing/Probe Town")]
        public static void Run()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(OutDir);

            ProbeTown(log);
            ProbeNeighborhood(log);
            ProbeSellerLot(log);

            File.WriteAllText("PSXRacing_townprobe.txt", log.ToString());
            Debug.Log(log.ToString());
        }

        static void ProbeTown(StringBuilder log)
        {
            log.AppendLine("=== TOWN ===");
            if (!File.Exists(PSXRacingBuilder.TownScenePath))
            {
                log.AppendLine("scene missing — run the scene build");
                return;
            }
            EditorSceneManager.OpenScene(PSXRacingBuilder.TownScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();

            // THE EDGES FIRST, before the lot and the yard are dressed: a
            // parked shell is a collider too, and none of them is a lip.
            ProbeEdgeLips(log);
            ProbeRespawns(log);

            // The lot and the yard are filled at RUNTIME, and AddComponent runs
            // no Start outside play mode — so without this the probe
            // photographs an empty forecourt and an empty yard and says nothing
            // is wrong.
            // THE LIVE STATE, always. LifeRules.SeedNewGame RETURNS a state and
            // does not install one — LifeSimManager has no setter — so seeding
            // into a local leaves an orphan that Save() never writes and that
            // the scene's own components, which read LifeSimManager.State, can
            // never see. That is exactly how the first seller-lot probe
            // reported a viewing it had opened and then a driveway with no car
            // on it.
            var townState = LifeSimManager.State;
            if (townState.cars.Count == 0) LifeRules.SeedFallbackCar(townState);
            CarMarket.RefreshLot(townState);
            // ON A MEET NIGHT. The Eastside Lot is only full on the nights the
            // calendar says there is a meet, so a probe run on any other clock
            // photographs an empty car park and proves nothing about the twelve
            // cars, their stalls, or whether a walker can get between them. The
            // clock is put back afterwards: this is the LIVE state.
            int wasDay = townState.day, wasSlot = townState.slotIndex;
            townState.day = CarMeets.NextMeetDay(townState.day);
            townState.slotIndex = CarMeets.MeetSlot;
            LifeSimManager.Save();
            foreach (var w in Object.FindObjectsByType<TownWorld>(FindObjectsSortMode.None))
                w.PreviewBuild();
            Physics.SyncTransforms();
            ProbeMeet(log, townState);
            townState.day = wasDay;
            townState.slotIndex = wasSlot;
            LifeSimManager.Save();

            var car = Object.FindAnyObjectByType<CarController>();
            log.AppendLine(car != null
                ? "player car at " + car.transform.position.ToString("0.00") +
                  " facing " + car.transform.forward.ToString("0.00")
                : "NO PLAYER CAR");

            // Is the car standing on TARMAC? A layer-8 collider under the spawn
            // is the difference between driving and skating: CarController
            // decides onRoad by layer number and nothing on screen says which
            // one it found.
            if (car != null)
            {
                var from = car.transform.position + Vector3.up * 2f;
                if (GroundUnder(from, 12f, out var hit))
                    log.AppendLine("  ground under the spawn: " + hit.collider.name +
                                   " layer " + hit.collider.gameObject.layer +
                                   " at y " + hit.point.y.ToString("0.00") +
                                   (hit.collider.gameObject.layer == 8 ? "  (road, good)"
                                                                       : "  (NOT ROAD)"));
                else log.AppendLine("  NOTHING UNDER THE SPAWN");
            }

            // WALK ACROSS THE STREET. A photograph cannot say which collider it
            // is looking at; this can: every half metre from the north verge
            // to the south one at the spawn's x, what is actually under the
            // wheels — lawn, verge, kerb ramp, tarmac. (It walked "the home
            // street" from z 56 to -4 until the house moved to its own map,
            // after which it measured a lawn.)
            // TRIGGERS OFF. The first strip reported "DepartVenue" for three
            // stations in a row and said nothing about the tarmac underneath —
            // a venue volume is a collider and a raycast hits it, so the answer
            // to "what am I driving on" was being masked by the thing that asks
            // where you are going.
            bool wasTriggers = Physics.queriesHitTriggers;
            Physics.queriesHitTriggers = false;
            log.AppendLine("across the main street at the spawn, north verge to south verge:");
            string run = "";
            float lineX = car != null ? car.transform.position.x : -110f;
            for (float z = 9f; z >= -9f; z -= 0.5f)
            {
                var at = new Vector3(lineX, 4f, z);
                string what = Physics.Raycast(at, Vector3.down, out var h, 12f)
                    ? h.collider.name + "(" + h.collider.gameObject.layer + ")@" +
                      h.point.y.ToString("0.000") : "NOTHING";
                run += "z" + z.ToString("0.0") + " " + what + "   ";
            }
            log.AppendLine("  " + run);

            // EVERY surface stacked under one point, with its height: the back
            // of the north kerb, where the stone's ramp, the verge and the lawn
            // all lie over each other and only the order says which one a
            // wheel meets.
            var all = Physics.RaycastAll(new Vector3(lineX, 6f, 5.9f), Vector3.down, 14f);
            System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
            string stack = "";
            foreach (var h in all)
                stack += h.collider.name + "(" + h.collider.gameObject.layer + ")@" +
                         h.point.y.ToString("0.000") + "  ";
            log.AppendLine("  stacked at the kerb's back: " + (all.Length == 0 ? "nothing" : stack));
            Physics.queriesHitTriggers = wasTriggers;

            // WHAT IS DRAWING THE GRASS. The band survived switching the ground
            // off, which rules out the two-coplanar-surfaces theory entirely —
            // so something else is putting a grass texture across the
            // carriageway, and no amount of reasoning about depth precision was
            // going to name it. Every renderer whose bounds contain the point,
            // with the texture it is wearing.
            foreach (var probeAt in new[] { new Vector3(lineX, 0.05f, 13f),
                                            new Vector3(lineX, 0.05f, 0f) })
            {
                string who = "";
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var b = r.bounds;
                    b.Expand(0.4f);
                    if (!b.Contains(probeAt)) continue;
                    var m = r.sharedMaterial;
                    who += r.name + " [" + (m != null ? m.name : "no mat") + " / " +
                           (m != null && m.mainTexture != null ? m.mainTexture.name : "no tex") +
                           "] y" + b.center.y.ToString("0.00") + "   ";
                }
                log.AppendLine("  renderers over z" + Mathf.RoundToInt(probeAt.z) + ": " +
                               (who.Length == 0 ? "none" : who));
            }

            Report(log, "House");
            Report(log, "Pizzeria");
            Report(log, "Showroom");
            Report(log, "GasStation");
            Report(log, "Mechanic");
            Report(log, "PaintShop");
            foreach (var v in Object.FindObjectsByType<TownVenue>(FindObjectsSortMode.None))
                log.AppendLine("venue " + v.kind + " at " +
                               v.transform.position.ToString("0.0"));

            // The doors, and where they hang. A leaf that opens by rotating
            // about its own middle sweeps the doorway instead of clearing it,
            // and that is a hinge measured on the wrong edge — which nothing
            // in a still photograph can show.
            foreach (var d in Object.FindObjectsByType<PSXRacing.SwingDoor>(
                                  FindObjectsSortMode.None))
            {
                // Reported in WORLD space. The fields are stored in the
                // building's frame so a prefab can be stood up at any yaw, and
                // a probe printing those raw would be printing the door of a
                // building on a turntable.
                var frame = d.transform.parent;
                Vector3 leaf = frame != null
                    ? frame.TransformDirection(d.hingeToFree) : d.hingeToFree;
                Vector3 thru = frame != null
                    ? frame.TransformDirection(d.throughNormal) : d.throughNormal;
                log.AppendLine("door " + d.name + " at " +
                               d.transform.position.ToString("0.0") +
                               "  leaf " + leaf.magnitude.ToString("0.00") +
                               " m toward " + leaf.normalized.ToString("0.0") +
                               "  through " + thru.normalized.ToString("0.0"));
            }

            // The walk-up anchors, which are the whole of "can I do anything
            // here on foot". A null one is a hook that never gets built, and
            // TownWorld builds hooks inside an "if (anchor != null)".
            var tw0 = Object.FindFirstObjectByType<PSXRacing.Town.TownWorld>();
            if (tw0 != null)
            {
                if (tw0.pizzaHooks == null || tw0.pizzaHooks.Length == 0)
                    log.AppendLine("anchor pizza: MISSING");
                else
                    foreach (var h in tw0.pizzaHooks)
                        log.AppendLine("anchor " + (h == null ? "pizza: NULL"
                            : h.name + ": " + h.position.ToString("0.0")));
                foreach (var pair in new (string, Transform)[]
                {
                    ("mechanic", tw0.mechanicDoor), ("paint shop", tw0.paintDoor),
                    ("dealer", tw0.dealerDoor), ("yard gate", tw0.yardGate),
                    ("home", tw0.homeDoor), ("pizza kerb", tw0.pizzaKerb),
                })
                    log.AppendLine("anchor " + pair.Item1 + ": " +
                        (pair.Item2 == null ? "MISSING"
                                            : pair.Item2.position.ToString("0.0")));
            }
            int shells = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Shell") shells++;
            log.AppendLine("parked shells: " + shells + " (lot + yard)");

            // The yard's stripping, counted rather than photographed: a block
            // stack is a 20 cm object on the far side of a car that faces a
            // random way, so the camera can only ever hint. Wheels + missing
            // must sum to four on every wreck, and a yard with nothing missing
            // at all means the strip roll is dead.
            var tw = Object.FindFirstObjectByType<PSXRacing.Town.TownWorld>();
            if (tw != null)
            {
                int missingTotal = 0, blockStacks = 0;
                var line = new StringBuilder("yard corners:");
                for (int i = 0; i < tw.yardSpots.Length; i++)
                {
                    var spot = tw.yardSpots[i];
                    if (spot == null || spot.childCount == 0) continue;
                    var shell = spot.GetChild(0);
                    int wheels = 0, blocks = 0;
                    foreach (Transform c in shell)
                    {
                        if (c.name.StartsWith("Wheel")) wheels++;
                        if (c.name.StartsWith("Blocks") && c.name.EndsWith("_0")) blocks++;
                    }
                    missingTotal += 4 - wheels;
                    blockStacks += blocks;
                    line.Append("  #" + i + " " + wheels + "w/" + blocks + "b");
                }
                log.AppendLine(line.ToString());
                log.AppendLine("yard stripped wheels: " + missingTotal +
                               "  block stacks: " + blockStacks +
                               (missingTotal == blockStacks ? "  (every bare corner is held up)"
                                                            : "  (MISMATCH)"));
            }

            // The four shots that would each have caught a different bug.
            if (car != null)
            {
                var p = car.transform.position;
                // BOTH from down the drive looking back. "Behind the car" is
                // inside the house when the car is parked nose-out at its own
                // garage door, which is how the first probe photographed a
                // bedroom and reported nothing wrong.
                Shot("town_drive", p + car.transform.forward * 8f + Vector3.up * 2.2f,
                     -car.transform.forward);
                Shot("town_house", p + car.transform.forward * 22f + Vector3.up * 4f,
                     -car.transform.forward);
            }
            Shot("town_junction", new Vector3(-110f, 6f, 26f), new Vector3(0f, -0.35f, -1f));
            // The same view with the ground switched off. A band of grass lying
            // across a road that measures as continuous tarmac is either a
            // surface nobody meant to build or the ground fighting the road for
            // the depth buffer, and one photograph with the ground gone
            // separates the two — which no amount of reasoning about depth
            // precision managed to.
            var groundGO = GameObject.Find("TownGround");
            var groundR = groundGO != null ? groundGO.GetComponent<MeshRenderer>() : null;
            if (groundR != null)
            {
                groundR.enabled = false;
                Shot("town_junction_noground", new Vector3(-110f, 6f, 26f),
                     new Vector3(0f, -0.35f, -1f));
                groundR.enabled = true;
            }
            Shot("town_street", new Vector3(-30f, 7f, -18f), new Vector3(0.55f, -0.25f, 1f));
            // Eye level at the pumps, not above the canopy.
            Shot("town_forecourt", new Vector3(-52f, 2.2f, 6f), new Vector3(0f, -0.03f, 1f));
            Shot("town_forecourt_wide", new Vector3(-64f, 14f, -6f),
                 new Vector3(0.35f, -0.45f, 1f));
            Shot("town_dealer", new Vector3(62f, 9f, 2f), new Vector3(0f, -0.35f, 1f));
            // The shop from the apron the car parks on, and then its actual
            // DOOR, which is round the east end. Two shots because they answer
            // two questions the same picture cannot: whether the frontage
            // reads as a pizza shop, and whether the leaves are back in the
            // doorway. They had been DELETED — "the doors are missing to
            // Pizzeria and Convenience store" — and a hole in a wall
            // photographs as a perfectly good open door.
            Shot("town_pizzeria", new Vector3(-6f, 4f, -4f), new Vector3(0f, -0.25f, -1f));
            // Eye height, five metres out on the apron, square to the shop.
            // The doorway is MEASURED at bake time and the shop was turned 90
            // degrees after this shot was first aimed, so it spent one build
            // photographing the blank brick side — which is exactly the thing
            // the shot exists to catch, at the shot rather than in the game.
            Shot("town_pizzeria_door", new Vector3(-3.3f, 1.7f, -16.6f),
                 new Vector3(0f, -0.03f, -1f), fov: 48f);
            // The two trades, from the street. Both are authored — there is no
            // garage, workshop or spray booth in either art tree — so these are
            // the only way to know a unit reads as a unit rather than as a
            // shed with a coloured board over it.
            Shot("town_mechanic", new Vector3(58f, 4f, -3f), new Vector3(0f, -0.22f, -1f));
            Shot("town_paint", new Vector3(-92f, 4f, -3f), new Vector3(0f, -0.22f, -1f));
            Shot("town_yard", new Vector3(106f, 9f, -56f), new Vector3(0f, -0.32f, 1f));
            // Close on the front row of wrecks, eye height. The stripped
            // corners and their block stacks are 20 cm objects: they exist in
            // the wide shot only as a hunch, and every failure mode here — a
            // block floating, a wheel left inside a stack, a body sunk to its
            // sills — is silent at nine metres in the air.
            Shot("town_yard_close", new Vector3(94f, 1.6f, -33f),
                 new Vector3(1f, -0.12f, 0.35f));
            // Straight down over the whole map. The one shot that shows what a
            // town IS rather than what one corner of it looks like — whether
            // the roads join, whether anything is stranded on a lawn, and
            // whether a lot has a way in.
            Shot("town_top", new Vector3(0f, 260f, 10f), Vector3.down, fov: 78f);
            Shot("town_top_home", new Vector3(-105f, 90f, 34f), Vector3.down, fov: 70f);
        }

        /// <summary>
        /// THE CAR MEET: who is in the lot, whether each of them can be walked
        /// up to, and what it looks like — by day (the geometry) and at night
        /// (the look, which is the hour a meet actually happens at).
        ///
        /// The numbers are the part a photograph cannot give: a stall whose
        /// car overlaps its neighbour still photographs as two cars, and a
        /// hook the tarmac stands in front of photographs as a car with no
        /// prompt. So every car is measured against the next one along and
        /// every hook's aim point is reported with its height.
        /// </summary>
        static void ProbeMeet(StringBuilder log, LifeState s)
        {
            log.AppendLine("--- the car meet (" + LifeRules.DateLabel(s.day) + " " +
                           LifeRules.SlotNames[s.slotIndex] + ") ---");
            var world = Object.FindAnyObjectByType<TownWorld>();
            if (world == null || world.meetSpots == null || world.meetSpots.Length == 0)
            {
                log.AppendLine("  NO MEET LOT in this scene");
                return;
            }
            var roster = CarMeets.Roster(s, s.day);
            log.AppendLine("  " + roster.Count + " drivers for " + world.meetSpots.Length + " stalls");
            foreach (var r in roster)
                log.AppendLine("    seat " + r.seat + "  " + r.alias.PadRight(10) + " " +
                               (r.spec != null ? r.spec.name : "?") + "  [" + r.style + " -> " +
                               TrackCatalog.At(r.trackIndex).name + "]  " + r.Reputation + "  $" + r.purse +
                               (r.IsRival ? "  <-- BLACKLIST #" + r.rivalRank : ""));

            // Every hook: is it there, how high does it aim, what does it say.
            int hooks = 0, low = 0;
            foreach (var spot in world.meetSpots)
            {
                if (spot == null) continue;
                var t = spot.GetComponentInChildren<PSXRacing.OnFoot.FootTarget>(true);
                if (t == null) continue;
                hooks++;
                float aimY = t.FocusPoint.y - spot.position.y;
                if (aimY < 0.7f) low++;
                log.AppendLine("    " + spot.name + " at " + spot.position.ToString("0.0") +
                               "  aim +" + aimY.ToString("0.00") + " m  [" + t.Verb + "] " + t.title +
                               "  ·  " + t.detail);
            }
            log.AppendLine("  " + hooks + " walk-up hooks" +
                           (low > 0 ? "  — " + low + " AIM AT THE GROUND (the tarmac will block them)" : ""));

            // Body to body: the narrowest gap between any two parked cars.
            float tightest = float.MaxValue;
            var boxes = new System.Collections.Generic.List<Bounds>();
            foreach (var spot in world.meetSpots)
            {
                if (spot == null) continue;
                var col = spot.GetComponentInChildren<BoxCollider>(true);
                if (col != null) boxes.Add(col.bounds);
            }
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
                    float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
                    tightest = Mathf.Min(tightest, Mathf.Sqrt(dx * dx + dz * dz));
                }
            log.AppendLine("  tightest gap between two parked cars: " +
                           (boxes.Count > 1 ? tightest.ToString("0.00") + " m" : "n/a") +
                           (tightest < 0.6f ? "  <-- A WALKER (0.52 m) CANNOT PASS" : ""));

            Shot("town_meet", new Vector3(109f, 16f, -6f), new Vector3(0f, -0.42f, 1f));
            Shot("town_meet_eye", new Vector3(109f, 1.7f, 13f), new Vector3(0f, -0.02f, 1f));
            Shot("town_meet_aisle", new Vector3(94f, 1.7f, 21.5f), new Vector3(1f, -0.03f, 0.12f));
            Shot("town_meet_top", new Vector3(109f, 60f, 31f), Vector3.down, fov: 60f);

            // AND AT NIGHT, which is when it is. The hour's own rig: the sun
            // and sky through TimeOfDay, the globals pushed by hand (no Update
            // in edit mode), the lamps' glows switched on the way NightGlow
            // would. Put back to the baked hour afterwards, for the shots that
            // follow.
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            var globals = Object.FindAnyObjectByType<PSXGlobals>();
            if (sun != null)
            {
                TimeOfDay.Apply(TimeOfDay.Night, sun);
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
                SetGlows(true);
                Shot("town_meet_night", new Vector3(109f, 16f, -6f), new Vector3(0f, -0.42f, 1f),
                     sky: new Color(0.03f, 0.04f, 0.08f));
                Shot("town_meet_night_eye", new Vector3(109f, 1.7f, 13f), new Vector3(0f, -0.02f, 1f),
                     sky: new Color(0.03f, 0.04f, 0.08f));
                Shot("town_meet_night_aisle", new Vector3(94f, 1.7f, 21.5f), new Vector3(1f, -0.03f, 0.12f),
                     sky: new Color(0.03f, 0.04f, 0.08f));
                SetGlows(false);
                TimeOfDay.Apply(TimeOfDay.Sunset, sun);
                if (globals != null) globals.SendMessage("Apply", SendMessageOptions.DontRequireReceiver);
            }
        }

        /// <summary>
        /// The meet lot's lamps, lit the way the game lights them — the same
        /// change as PSXScreenshotTool.SetNightGlow (2026-09-21). Switching on
        /// every renderer under the NightGlow brought back the glow quads and
        /// the 16 m additive pool discs the night pass retired, with none of
        /// the per-pixel pool light that replaced them, because NightGlow's
        /// Awake — where the conversion happens — never runs in edit mode.
        /// PreviewAll does that conversion (idempotently) and SetAll(lit).
        /// The probe camera needs nothing else: StreetLights fills its lamp
        /// table from beginCameraRendering for whichever camera draws.
        /// </summary>
        static void SetGlows(bool lit) => NightGlow.PreviewAll(lit);

        static void ProbeNeighborhood(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine("=== NEIGHBOURHOOD ===");
            if (!File.Exists(PSXRacingBuilder.NeighborhoodScenePath))
            {
                log.AppendLine("scene missing — run the scene build");
                return;
            }
            EditorSceneManager.OpenScene(PSXRacingBuilder.NeighborhoodScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var car = Object.FindAnyObjectByType<CarController>();
            if (car != null && GroundUnder(car.transform.position + Vector3.up * 2f, 12f, out var hit))
                log.AppendLine("player car on " + hit.collider.name + " layer " +
                               hit.collider.gameObject.layer +
                               (hit.collider.gameObject.layer == WorldKit.RoadLayer ? "  (road, good)"
                                                                                    : "  (NOT ROAD)"));
            else log.AppendLine(car == null ? "NO PLAYER CAR" : "NOTHING UNDER THE SPAWN");
            ProbeEdgeLips(log);
            ProbeRespawns(log);
        }

        // ---- the edge-lip scan ----
        /// <summary>Metres between scanned stations along an edge.</summary>
        const float LipStationM = 1f;
        /// <summary>The scan runs from this far INSIDE the edge...</summary>
        const float LipInsideM = 0.3f;
        /// <summary>...to this far outside it: past a feather's catch and its
        /// toe, and past a kerb's verge.</summary>
        const float LipOutsideM = 2.5f;
        /// <summary>Sample pitch across the edge: a centimetre, so a feather's
        /// own 1V:8H fall adds 1.25 mm to a sample and a lip reads as the lip.
        /// At 5 cm the owner's inch on a 45% lawn read as 3.7 cm.</summary>
        const float LipPitchM = 0.01f;
        /// <summary>How far over RoadsideRules.EdgeDropM a lip may read before
        /// it WARNS: the slope a sample carries across a centimetre of steep
        /// lawn, plus float. The FAIL line has no tolerance.</summary>
        const float LipToleranceM = 0.005f;
        /// <summary>Each sample's downward ray starts this far over the edge and
        /// runs this far: it finds ground from 3 m above the road to 5 m
        /// below it, and anything deeper reads as the ray's floor.</summary>
        const float LipRayUpM = 3f, LipRayLengthM = 8f;
        /// <summary>Sample pitch of the barrier-warrant walk: the circuits'
        /// edge audit's own 5 cm. A warrant is a question about metres of
        /// fall, not about the centimetre a lip is.</summary>
        const float WarrantPitchM = 0.05f;
        /// <summary>Each warrant sample's ray starts this far over the edge —
        /// as high as a 1:1 cut can climb inside RoadsideRules.WarrantReachM,
        /// so a bank rising beside a drive is ground and not a void under the
        /// ray — and finds ground down to the same 5 m under the edge the lip
        /// rays do.</summary>
        const float WarrantRayUpM = RoadsideRules.WarrantReachM;

        /// <summary>
        /// DOES EVERY EDGE MEET WHAT IS BESIDE IT, as a car driving back onto
        /// it would find it?
        ///
        /// Written for "it is difficult to drive back onto tracks … roads stick
        /// out of the ground". The town had never been measured at all: its
        /// kerbs were 16 cm solid boxes and every slab a square 7.5 cm step, and
        /// no audit, probe or self-test looked, because they all run on
        /// TrackCatalog venues.
        ///
        /// Every ROAD-LAYER collider's true boundary — a mesh's edges that
        /// belong to one triangle, a box's top rectangle — every metre, scanned
        /// outward from 30 cm inside to 2.5 m outside at 1 cm. At each sample,
        /// the highest GROUND or ROAD collider under it (layers 0 and 8: the
        /// lawn, feathers, verges, slabs, kerb ramps). The number is the worst
        /// RISE between two neighbouring samples going INWARD — the step a car
        /// coming back meets — against RoadsideRules.EdgeDropM (WARN) and
        /// EdgeDropFailM (FAIL). A sloped surface costs a millimetre or so per
        /// sample; a face is one jump.
        ///
        /// A FALL IS NOT A LIP, and they are counted apart. Where the ground
        /// within the scan lies more than RoadsideRules.OpenDropM under the
        /// edge — the side of a drive graded down a hillside, the outer face of
        /// a retaining wall a metre and a half out — the question is the
        /// barrier warrant, not the owner's inch; a step whose foot is that far
        /// down is that fall's face and is listed as a FALL, never folded into
        /// the lip figure (where one 2 m wall would hide every 3 cm lip on the
        /// street behind it). Ground past the reach of the downward ray is a
        /// fall too, not a gap in the scan.
        ///
        /// AND THE WARRANT ITSELF IS WALKED, separately, the way the circuits'
        /// edge audit and the stage plan walk it: RoadsideRules.WorstCriticalFall
        /// from the edge out to RoadsideRules.WarrantReachM, ending at the first
        /// solid face across it. The FALL list above looks 2.5 m out, which is
        /// where the lips are, and half the warrant's reach: it listed 1.04 m
        /// beside the south edge of NbDrive0 (plot 0, west side) while a
        /// replica of this walk found the same garden 1.5-1.73 m down at 4.5-5.0
        /// m out, steeper than 1V:3H — critical under the shared rule and
        /// invisible to a 2.5 m scan (2026-09-13, round-one meshes). And the
        /// FALL list counts any metre of depth at any slope, where the warrant
        /// asks for CriticalFallM over a run steeper than TraversableSlope; it
        /// stays, as the screen for a structure edge over OpenDropM. Critical
        /// stations are tagged WARRANT. (That garden is graded now:
        /// PSXRacingBuilder.NbGroundY holds every drive's sides to 1V:4H
        /// falling, 2026-09-14, and LifeSimSelfTest walks the drives' sides
        /// this same way and fails on a critical station.)
        ///
        /// A station whose edge stands inside a SOLID-layer collider (the
        /// street running under a boundary wall) or under one (a drive's end
        /// inside its garage — asked with back faces ON, because a pack
        /// house's roof is one-sided and faces the sky) is not a lip anyone can
        /// reach, and a scan stops at the first solid face across it (a
        /// building, a lot wall).
        /// </summary>
        static void ProbeEdgeLips(StringBuilder log)
        {
            int groundMask = (1 << 0) | (1 << WorldKit.RoadLayer);
            int solidMask = 1 << WorldKit.SolidLayer;
            int surfaces = 0, stations = 0, walled = 0, warn = 0, fail = 0, falls = 0, critical = 0;
            var worst = new List<(float rise, string where)>();
            var worstFalls = new List<(float fall, string where)>();
            var worstCritical = new List<(float fall, string where)>();
            var failsBy = new System.Collections.Generic.Dictionary<string, int>();
            // One buffer for every station's walk: sample 0 is the edge, and
            // the walk never reaches past WarrantReachM.
            var warrantY = new float[Mathf.CeilToInt(RoadsideRules.WarrantReachM / WarrantPitchM) + 2];

            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (col == null || !col.enabled || col.isTrigger) continue;
                if (col.gameObject.layer != WorldKit.RoadLayer) continue;
                if (col.GetComponentInParent<CarController>() != null) continue;
                var edges = EdgesOf(col);
                if (edges.Count == 0) continue;
                surfaces++;
                foreach (var (a, b, outward) in edges)
                {
                    float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                    int n = Mathf.Max(1, Mathf.RoundToInt(len / LipStationM));
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, (k + 0.5f) / n);
                        stations++;
                        // Under a wall, or under a roof: the end of a drive inside
                        // its own garage is not an edge a car comes back over.
                        bool roofed;
                        bool backfaces = Physics.queriesHitBackfaces;
                        Physics.queriesHitBackfaces = true;
                        try
                        {
                            roofed = Physics.Raycast(p + Vector3.up * 0.1f, Vector3.up, 12f, solidMask,
                                                     QueryTriggerInteraction.Ignore);
                        }
                        finally { Physics.queriesHitBackfaces = backfaces; }
                        if (roofed || Physics.CheckSphere(p + Vector3.up * 0.5f, 0.25f, solidMask,
                                                          QueryTriggerInteraction.Ignore))
                        { walled++; continue; }
                        float reach = LipOutsideM;
                        if (Physics.Raycast(p + Vector3.up * 0.35f - outward * LipInsideM, outward,
                                            out var wall, LipInsideM + LipOutsideM, solidMask,
                                            QueryTriggerInteraction.Ignore))
                            reach = wall.distance - LipInsideM - LipPitchM;

                        float prev = float.NaN, rise = 0f, riseAt = 0f, fall = 0f, fallAt = 0f;
                        for (float e = -LipInsideM; e <= reach + 1e-4f; e += LipPitchM)
                        {
                            Vector3 q = p + outward * e;
                            // Nothing within the ray is ground further down than it
                            // reaches: a fall, recorded at the ray's own floor.
                            float y = Physics.Raycast(new Vector3(q.x, p.y + LipRayUpM, q.z), Vector3.down,
                                                      out var h, LipRayLengthM, groundMask,
                                                      QueryTriggerInteraction.Ignore)
                                ? h.point.y : p.y + LipRayUpM - LipRayLengthM;
                            if (e > 0f && p.y - y > fall) { fall = p.y - y; fallAt = e; }
                            // Inward is toward smaller e: the previous sample. A
                            // step whose foot is a fall's depth down is that
                            // fall's face, not a lip.
                            if (!float.IsNaN(prev) && prev - y > rise && p.y - y <= RoadsideRules.OpenDropM)
                            { rise = prev - y; riseAt = e; }
                            prev = y;
                        }
                        if (fall > RoadsideRules.OpenDropM)
                        {
                            falls++;
                            worstFalls.Add((fall, col.name + " at (" + p.x.ToString("0.0") + ", " +
                                                  p.z.ToString("0.0") + ") facing (" + outward.x.ToString("0.0") +
                                                  ", " + outward.z.ToString("0.0") + "), " +
                                                  fallAt.ToString("0.00") + " m out"));
                        }
                        // THE WARRANT: sample 0 is the edge's own height (a ray
                        // dropped exactly on a collider's boundary can fall past
                        // it), then the highest ground every WarrantPitchM out,
                        // NaN where there is none within reach — a void, which
                        // WorstCriticalFall calls a fall with no bottom. A solid
                        // face across the walk ends it: a car meets that before
                        // the fall behind it.
                        float warrantReach = RoadsideRules.WarrantReachM;
                        if (Physics.Raycast(p + Vector3.up * 0.35f, outward, out var guard, warrantReach,
                                            solidMask, QueryTriggerInteraction.Ignore))
                            warrantReach = guard.distance - WarrantPitchM;
                        int nw = Mathf.Clamp(Mathf.FloorToInt(warrantReach / WarrantPitchM + 1e-3f) + 1,
                                             1, warrantY.Length);
                        warrantY[0] = p.y;
                        for (int w = 1; w < nw; w++)
                        {
                            Vector3 q = p + outward * (w * WarrantPitchM);
                            warrantY[w] = Physics.Raycast(new Vector3(q.x, p.y + WarrantRayUpM, q.z), Vector3.down,
                                                          out var gh, WarrantRayUpM + (LipRayLengthM - LipRayUpM),
                                                          groundMask, QueryTriggerInteraction.Ignore)
                                        ? gh.point.y : float.NaN;
                        }
                        float crit = RoadsideRules.WorstCriticalFall(warrantY, 0, nw - 1, WarrantPitchM, 0f,
                                                                     out int footK);
                        if (footK >= 0)
                        {
                            critical++;
                            worstCritical.Add((crit, col.name + " at (" + p.x.ToString("0.0") + ", " +
                                                     p.z.ToString("0.0") + ") facing (" + outward.x.ToString("0.0") +
                                                     ", " + outward.z.ToString("0.0") + "), foot " +
                                                     (footK * WarrantPitchM).ToString("0.00") + " m out" +
                                                     (float.IsPositiveInfinity(crit) ? " (no ground under it)" : "")));
                        }

                        if (rise > RoadsideRules.EdgeDropM + LipToleranceM) warn++;
                        if (rise > RoadsideRules.EdgeDropFailM)
                        {
                            fail++;
                            failsBy.TryGetValue(col.name, out int c);
                            failsBy[col.name] = c + 1;
                        }
                        worst.Add((rise, col.name + " at (" + p.x.ToString("0.0") + ", " +
                                         p.z.ToString("0.0") + ") facing (" + outward.x.ToString("0.0") +
                                         ", " + outward.z.ToString("0.0") + "), " +
                                         riseAt.ToString("0.00") + " m out"));
                    }
                }
            }

            worst.Sort((x, y) => y.rise.CompareTo(x.rise));
            log.AppendLine("edge lips: " + stations + " stations on " + surfaces + " road surfaces (" +
                           walled + " under a wall)  worst climb back " +
                           (worst.Count > 0 ? worst[0].rise.ToString("0.000") : "-") + " m" +
                           (fail > 0 ? "  FAIL" : warn > 0 ? "  WARN" : "  OK"));
            log.AppendLine("  over the inch (" + (RoadsideRules.EdgeDropM + LipToleranceM).ToString("0.000") +
                           " with sampling): " + warn +
                           "   over the fail line (" + RoadsideRules.EdgeDropFailM.ToString("0.000") +
                           "): " + fail);
            foreach (var kv in failsBy)
                log.AppendLine("  FAIL on " + kv.Key + ": " + kv.Value + " station(s)");
            for (int i = 0; i < Mathf.Min(12, worst.Count); i++)
                log.AppendLine("  " + worst[i].rise.ToString("0.000") + " m  " + worst[i].where);

            worstFalls.Sort((x, y) => y.fall.CompareTo(x.fall));
            log.AppendLine("falls beside an edge (ground more than " + RoadsideRules.OpenDropM.ToString("0.0") +
                           " m under it within " + LipOutsideM.ToString("0.0") + " m): " + falls + " station(s)" +
                           (falls > 0 ? "  FALL" : "  none"));
            for (int i = 0; i < Mathf.Min(12, worstFalls.Count); i++)
                log.AppendLine("  " + worstFalls[i].fall.ToString("0.00") + " m  " + worstFalls[i].where);

            worstCritical.Sort((x, y) => y.fall.CompareTo(x.fall));
            log.AppendLine("barrier warrant (RoadsideRules.WorstCriticalFall to " +
                           RoadsideRules.WarrantReachM.ToString("0.0") + " m out, or the first solid face): " +
                           critical + " station(s) critical and unguarded" + (critical > 0 ? "  WARRANT" : "  none"));
            for (int i = 0; i < Mathf.Min(12, worstCritical.Count); i++)
                log.AppendLine("  " + (float.IsPositiveInfinity(worstCritical[i].fall)
                                           ? "void" : worstCritical[i].fall.ToString("0.00") + " m") +
                               "  " + worstCritical[i].where);
        }

        /// <summary>
        /// The horizontal boundary of a road collider, as world segments with
        /// an outward direction. A MeshCollider's edges that belong to exactly
        /// one triangle (welded to the millimetre, so a strip built with
        /// duplicate vertices still has an inside), outward away from that
        /// triangle; a BoxCollider's top rectangle, outward from its centre.
        /// Near-vertical edges have no outward and are skipped.
        /// </summary>
        static List<(Vector3 a, Vector3 b, Vector3 outward)> EdgesOf(Collider col)
        {
            var result = new List<(Vector3, Vector3, Vector3)>();
            if (col is BoxCollider box)
            {
                var t = box.transform;
                Vector3 c = box.center, h = box.size * 0.5f;
                var corners = new[]
                {
                    t.TransformPoint(c + new Vector3(-h.x, h.y, -h.z)),
                    t.TransformPoint(c + new Vector3(h.x, h.y, -h.z)),
                    t.TransformPoint(c + new Vector3(h.x, h.y, h.z)),
                    t.TransformPoint(c + new Vector3(-h.x, h.y, h.z)),
                };
                Vector3 mid = (corners[0] + corners[2]) * 0.5f;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = corners[i], b = corners[(i + 1) % 4];
                    Vector3 o = Outward(a, b, mid);
                    if (o != Vector3.zero) result.Add((a, b, o));
                }
                return result;
            }
            if (!(col is MeshCollider mc) || mc.sharedMesh == null) return result;
            var mesh = mc.sharedMesh;
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            var world = new Vector3[verts.Length];
            var key = new int[verts.Length];
            var weld = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                world[i] = mc.transform.TransformPoint(verts[i]);
                var k = Vector3Int.RoundToInt(world[i] * 1000f);
                if (!weld.TryGetValue(k, out int id)) { id = i; weld[k] = id; }
                key[i] = id;
            }
            var count = new System.Collections.Generic.Dictionary<long, (int n, int a, int b, int c)>();
            for (int t = 0; t + 2 < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = key[tris[t + e]], b = key[tris[t + (e + 1) % 3]], c = key[tris[t + (e + 2) % 3]];
                    if (a == b) continue;
                    long id = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    count[id] = count.TryGetValue(id, out var was) ? (was.n + 1, a, b, c) : (1, a, b, c);
                }
            foreach (var kv in count)
            {
                if (kv.Value.n != 1) continue;
                Vector3 a = world[kv.Value.a], b = world[kv.Value.b], c = world[kv.Value.c];
                Vector3 o = Outward(a, b, c);
                if (o != Vector3.zero) result.Add((a, b, o));
            }
            return result;
        }

        /// <summary>The horizontal unit perpendicular to a→b that points away
        /// from <paramref name="inside"/>, or zero for an edge with no plan
        /// length.</summary>
        static Vector3 Outward(Vector3 a, Vector3 b, Vector3 inside)
        {
            Vector3 d = b - a; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return Vector3.zero;
            Vector3 o = new Vector3(d.z, 0f, -d.x).normalized;
            Vector3 toIn = inside - a; toIn.y = 0f;
            return Vector3.Dot(o, toIn) > 0f ? -o : o;
        }

        /// <summary>
        /// The first thing straight down from <paramref name="from"/> that a car
        /// could stand on: every layer, triggers ignored, and never a car's own
        /// collider. Both spawn checks asked a plain raycast, and each could
        /// answer with something that is not ground — the town's with a zone or
        /// venue trigger (the default query hits triggers), the street's, which
        /// asked every layer, with the parked player car's own body box (a
        /// "surface" 1.57 m up that the self-test's HOME check also read). Now
        /// that verify.ps1 fails a run on "NOT ROAD", the question has to be
        /// the one it means. ProbeRespawns asks it the same way.
        /// </summary>
        static bool GroundUnder(Vector3 from, float reach, out RaycastHit ground)
        {
            ground = default;
            var hits = Physics.RaycastAll(from, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
                if (h.collider.GetComponentInParent<CarController>() == null) { ground = h; return true; }
            return false;
        }

        /// <summary>
        /// Does every place a stuck car is put back stand on a ROAD collider?
        /// The town's RespawnHome stood on the lawn 30 m from the street for a
        /// year, facing a kerb, and nothing asked.
        /// </summary>
        static void ProbeRespawns(StringBuilder log)
        {
            var mode = Object.FindAnyObjectByType<PSXRacing.City.CityMode>();
            if (mode == null || mode.respawnPoints == null || mode.respawnPoints.Length == 0)
            {
                log.AppendLine("respawns: NONE — FAIL");
                return;
            }
            int good = 0;
            foreach (var t in mode.respawnPoints)
            {
                if (t == null) { log.AppendLine("  respawn: NULL"); continue; }
                var hits = Physics.RaycastAll(t.position + Vector3.up * 2f, Vector3.down, 8f,
                                              ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                RaycastHit? first = null;
                foreach (var h in hits)
                    if (h.collider.GetComponentInParent<CarController>() == null) { first = h; break; }
                bool road = first.HasValue && first.Value.collider.gameObject.layer == WorldKit.RoadLayer;
                if (road) good++;
                log.AppendLine("  " + t.name + " at " + t.position.ToString("0.0") + ": " +
                    (first.HasValue
                        ? first.Value.collider.name + " layer " + first.Value.collider.gameObject.layer +
                          (road ? "  (road, good)" : "  (NOT ROAD)")
                        : "NOTHING UNDER IT"));
            }
            log.AppendLine("respawns: " + good + " of " + mode.respawnPoints.Length + " on a road collider" +
                           (good == mode.respawnPoints.Length ? "  OK" : "  FAIL"));
        }

        static void ProbeSellerLot(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine("=== SELLER LOT ===");
            if (!File.Exists(SellerLotSceneBuilder.ScenePath))
            {
                log.AppendLine("scene missing — run the scene build");
                return;
            }
            EditorSceneManager.OpenScene(SellerLotSceneBuilder.ScenePath, OpenSceneMode.Single);

            // A visit to dress the street with. Without one the world picks a
            // house and parks nothing, which is exactly what an expired advert
            // looks like — a state worth photographing, but not this one.
            var s = LifeSimManager.State;      // see the note in ProbeTown
            if (s.cars.Count == 0) LifeRules.SeedFallbackCar(s);
            if (s.newspaper.Count == 0) CarMarket.RefreshListings(s);
            if (s.newspaper.Count > 0)
            {
                var v = Viewings.Open(s, s.newspaper[0], "paper");
                s.activeViewing = v.key;
                LifeSimManager.Save();
                log.AppendLine("viewing: " + v.car.displayName + "  ask " + v.askPrice +
                               "  faults " + v.car.faults.Count);
            }
            foreach (var w in Object.FindObjectsByType<SellerLotWorld>(FindObjectsSortMode.None))
                w.PreviewBuild();
            Physics.SyncTransforms();

            var player = Object.FindAnyObjectByType<FirstPersonWalk>();
            log.AppendLine(player != null
                ? "player at " + player.transform.position.ToString("0.00")
                : "NO PLAYER");
            int houses = 0, shells = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith("House") && t.gameObject.activeInHierarchy) houses++;
                if (t.name == "Shell") shells++;
            }
            log.AppendLine("houses standing: " + houses + " (one per plot)");
            // The car IS the scene. A missing shell here is the whole feature
            // failing quietly: the hook hangs off it and the deal hangs off the
            // hook, so no shell means a house with nothing outside it.
            log.AppendLine("car on the drive: " + shells + " shell(s)");
            var live = Viewings.ByKey(LifeSimManager.State, LifeSimManager.State.activeViewing);
            log.AppendLine("  visit resolved: " + (live != null) +
                           "  spec: " + (live != null && CarCatalog.Get(live.car.specId) != null) +
                           "  model: " + (live != null &&
                               CarShell.DefFor(CarCatalog.Get(live.car.specId)) != null));
            // "[VERB] sentence": the bracket is the word on the thumb button,
            // so a probe run shows a hook that would ship saying USE.
            foreach (var h in Object.FindObjectsByType<FootTarget>(FindObjectsSortMode.None))
                log.AppendLine("hook " + h.name + " '" + h.title + "' — [" + h.Verb + "] " + h.action);

            // From the player's own eyes, which is the only view that says
            // whether the car is reachable and the house is behind it.
            if (player != null)
            {
                var pt = player.transform;
                Shot("seller_eye", pt.position + Vector3.up * 1.7f, pt.forward);
                Shot("seller_wide", pt.position + new Vector3(-6f, 8f, -12f),
                     new Vector3(0.35f, -0.45f, 1f));
            }
        }

        static void Report(StringBuilder log, string name)
        {
            var go = GameObject.Find(name);
            if (go == null) { log.AppendLine(name + ": MISSING"); return; }
            var b = WorldKit.BoundsOf(go);
            log.AppendLine(name + ": centre " + b.center.ToString("0.0") +
                           "  size " + b.size.ToString("0.0") +
                           "  base y " + b.min.y.ToString("0.00") +
                           "  yaw " + go.transform.eulerAngles.y.ToString("0"));
        }

        static void Shot(string name, Vector3 at, Vector3 look, float fov = 55f, Color? sky = null)
        {
            var camGO = new GameObject("ProbeCam");
            var cam = camGO.AddComponent<Camera>();
            // Straight down needs an explicit up vector: LookRotation(down, up)
            // is degenerate and Unity answers it with identity, which points
            // the camera at the horizon and photographs the sky.
            var dir = look.normalized;
            var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir, up));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // A flat colour stands in for the sky, so a NIGHT shot has to be told
            // it is night or the lot is photographed under a blue noon.
            cam.backgroundColor = sky ?? new Color(0.63f, 0.72f, 0.83f);

            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
