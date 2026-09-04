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
            LifeSimManager.Save();
            foreach (var w in Object.FindObjectsByType<TownWorld>(FindObjectsSortMode.None))
                w.PreviewBuild();
            Physics.SyncTransforms();

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
                if (Physics.Raycast(from, Vector3.down, out var hit, 12f))
                    log.AppendLine("  ground under the spawn: " + hit.collider.name +
                                   " layer " + hit.collider.gameObject.layer +
                                   " at y " + hit.point.y.ToString("0.00") +
                                   (hit.collider.gameObject.layer == 8 ? "  (road, good)"
                                                                       : "  (NOT ROAD)"));
                else log.AppendLine("  NOTHING UNDER THE SPAWN");
            }

            // WALK THE ROUTE. A picture of the junction showed a band of grass
            // across the road that no bounds figure explained, and a photograph
            // cannot say which collider it is looking at. This can: every four
            // metres from the garage door to the main street, what is actually
            // under the wheels.
            // TRIGGERS OFF. The first strip reported "DepartVenue" for three
            // stations in a row and said nothing about the tarmac underneath —
            // a venue volume is a collider and a raycast hits it, so the answer
            // to "what am I driving on" was being masked by the thing that asks
            // where you are going.
            bool wasTriggers = Physics.queriesHitTriggers;
            Physics.queriesHitTriggers = false;
            log.AppendLine("home street, garage door to the main road:");
            string run = "";
            float lineX = car != null ? car.transform.position.x : -110f;
            for (float z = 56f; z >= -4f; z -= 4f)
            {
                var at = new Vector3(lineX, 4f, z);
                string what = Physics.Raycast(at, Vector3.down, out var h, 12f)
                    ? h.collider.name + "(" + h.collider.gameObject.layer + ")@" +
                      h.point.y.ToString("0.000") : "NOTHING";
                run += "z" + Mathf.RoundToInt(z) + " " + what + "   ";
            }
            log.AppendLine("  " + run);

            // EVERY surface stacked under one point in the band, with its
            // height. A band of grass across a road that the strip says is
            // continuous tarmac is either a second surface nobody meant to
            // build or a depth fight, and this is the line that tells them
            // apart.
            var all = Physics.RaycastAll(new Vector3(lineX, 6f, 13f), Vector3.down, 14f);
            System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
            string stack = "";
            foreach (var h in all)
                stack += h.collider.name + "(" + h.collider.gameObject.layer + ")@" +
                         h.point.y.ToString("0.000") + "  ";
            log.AppendLine("  stacked at z13: " + (all.Length == 0 ? "nothing" : stack));
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
            foreach (var h in Object.FindObjectsByType<FootTarget>(FindObjectsSortMode.None))
                log.AppendLine("hook " + h.name + " '" + h.title + "' — " + h.action);

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

        static void Shot(string name, Vector3 at, Vector3 look, float fov = 55f)
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
            cam.backgroundColor = new Color(0.63f, 0.72f, 0.83f);

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
