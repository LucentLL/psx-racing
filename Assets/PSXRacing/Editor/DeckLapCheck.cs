using System.Collections;
using System.Collections.Generic;
using System.Text;
using PSXRacing.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// PARKING DECKS, part 1 (2026-10-05): PLAYS Charlotte's free roam and has
    /// an AIDriver drive each deck's lap (street, driveway, up the helix, the
    /// roof's loop, down, out) on the player's own car, judging every physics
    /// step: STUCK (under 0.5 m/s for 8 s), FELL (a level under the path),
    /// LAUNCH (off the floor over 0.35 s, or rising over 3.5 m/s), LEFT (6 m
    /// off the path), RECOVERED (the AI's own stuck recovery). Lap times are
    /// the result part 2's time trial starts from.
    ///
    /// PSX_DECK_IDS="way,way,..." the decks to lap (default three of the
    /// whitelist); PSX_DECK_SHOTS=way photographs that deck's five views (street
    /// facade, driver's eye at the entrance, inside a level, the ramp, the roof)
    /// into Screenshots/Decks with tag PSX_DECK_TAG; PSX_DECK_HOUR=night shoots
    /// at night (inside + roof only). PSX_DECK_LAPS=0 photographs only.
    /// Writes PSXRacing_deck_lap.txt; exit 1 on any failure.
    /// </summary>
    public static class DeckLapCheck
    {
        internal static readonly StringBuilder log = new StringBuilder();
        internal static int failures;

        [MenuItem("PSX Racing/Check Parking Deck Laps")]
        public static void Run()
        {
            log.Clear(); failures = 0;
            int roamIdx = TrackCatalog.IndexOf("Charlotte");
            var scenes = EditorBuildSettings.scenes;
            int s = roamIdx >= 0 ? TrackCatalog.SceneIndex(roamIdx) : -1;
            if (s < 0 || s >= scenes.Length || !System.IO.File.Exists(scenes[s].path)) { Fail("Charlotte scene not built"); Finish(); return; }
            EditorSceneManager.OpenScene(scenes[s].path);
            bool night = System.Environment.GetEnvironmentVariable("PSX_DECK_HOUR") == "night";
            bool ok = PSXRacing.LifeSim.CityFrontEnd.FillFreeRoam(PSXRacing.LifeSim.CityFrontEnd.DefaultCarId(),
                                                                  night ? TimeOfDay.Night : TimeOfDay.Noon, 0, out int scene);
            Line("free roam handoff " + (ok ? "filled" : "NOT FILLED") + ", hour " + (night ? "night" : "noon"));
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("DeckLapRunner").AddComponent<DeckLapRunner>();
        }

        internal static void Line(string s) { log.AppendLine(s); Debug.Log("[DeckLap] " + s); }
        internal static void Fail(string s) { failures++; Line("  FAIL " + s); }
        internal static void Finish()
        {
            Line(failures == 0 ? "DECK LAP OK" : "DECK LAP FAILED: " + failures);
            System.IO.File.WriteAllText("PSXRacing_deck_lap.txt", log.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    public class DeckLapRunner : MonoBehaviour
    {
        const float HangSeconds = 2400f, LapTimeout = 900f;
        float bornAt; bool finished;
        void Awake() { bornAt = Time.realtimeSinceStartup; DontDestroyOnLoad(gameObject); }
        void Update()
        {
            if (finished || Time.realtimeSinceStartup - bornAt < HangSeconds) return;
            finished = true; DeckLapCheck.Fail("HUNG"); DeckLapCheck.Finish();
        }

        static uint[] Ids(string env, uint[] dflt)
        {
            var v = System.Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrEmpty(v)) return dflt;
            var l = new List<uint>();
            foreach (var t in v.Split(',')) if (uint.TryParse(t.Trim(), out var u)) l.Add(u);
            return l.ToArray();
        }
        static CityDecks.Deck Find(uint way) { foreach (var d in CityDecks.All) if (d.way == way) return d; return null; }

        IEnumerator Start()
        {
            yield return null; yield return new WaitForFixedUpdate(); yield return null;
            var mode = CityMode.Instance;
            if (mode == null || mode.player == null || mode.world == null || mode.world.Map == null)
            { DeckLapCheck.Fail("free roam did not start"); finished = true; DeckLapCheck.Finish(); yield break; }
            var map = mode.world.Map;
            var carGo = mode.player.gameObject;
            var car = carGo.GetComponent<CarController>();
            var rb = carGo.GetComponent<Rigidbody>();
            foreach (var mb in carGo.GetComponents<MonoBehaviour>())
                if (mb is PlayerCarInput || mb is StuckRecovery) mb.enabled = false;

            // the counts, every deck OSM has
            int listed = 0, solved = 0;
            var reasons = new Dictionary<string, int>();
            foreach (var d in CityDecks.All)
            {
                if (!d.Listed) continue;
                listed++;
                if (CityDecks.Solve(map, d)) solved++;
                else { reasons.TryGetValue(d.why, out int c); reasons[d.why] = c + 1; DeckLapCheck.Line("  listed deck " + d.way + " stays solid: " + d.why); }
            }
            DeckLapCheck.Line("decks in OSM " + CityDecks.All.Length + ", listed " + listed + ", solved drivable " + solved);

            var shotId = Ids("PSX_DECK_SHOTS", new uint[0]);
            if (shotId.Length > 0)
            {
                var d = Find(shotId[0]);
                if (d == null || !d.ok) DeckLapCheck.Fail("photo deck " + shotId[0] + " not drivable");
                else yield return Shots(d, map, car, rb);
            }
            if (System.Environment.GetEnvironmentVariable("PSX_DECK_LAPS") != "0")
                foreach (var id in Ids("PSX_DECK_IDS", new uint[] { 90480727, 255159816, 500204485 }))
                {
                    var d = Find(id);
                    if (d == null || !d.ok) { DeckLapCheck.Fail("deck " + id + " not drivable: " + (d != null ? d.why : "unknown id")); continue; }
                    yield return Lap(d, car, rb, carGo);
                }
            finished = true;
            DeckLapCheck.Finish();
        }

        /// <summary>Park the car at a point and wait until the deck's tile is
        /// built and its floor answers a ray.</summary>
        IEnumerator Arrive(CityDecks.Deck d, CarController car, Rigidbody rb, Vector3 at, Vector3 toward)
        {
            rb.isKinematic = true;
            var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(toward - at, Vector3.up).normalized, Vector3.up);
            car.TeleportTo(at + Vector3.up * 0.6f, rot);
            float t0 = Time.realtimeSinceStartup;
            var c = d.W3(0f, 0f, 0f);
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                yield return null;
                if (d.built && Physics.Raycast(new Vector3(at.x, at.y + 3f, at.z), Vector3.down, 6f, (1 << 8)) &&
                    Physics.Raycast(new Vector3(c.x, c.y + 1.5f, c.z) + new Vector3(d.U.x, 0f, d.U.y) * (d.x0 - d.T * 0.5f), Vector3.down, 3f, (1 << 8)))
                    break;
            }
            for (int i = 0; i < 30; i++) yield return null;
            car.TeleportTo(at + Vector3.up * 0.4f, rot);
            rb.isKinematic = false;
            yield return new WaitForFixedUpdate();
        }

        IEnumerator Lap(CityDecks.Deck d, CarController car, Rigidbody rb, GameObject carGo)
        {
            var w = d.lap;
            DeckLapCheck.Line("deck " + d.way + " (" + d.Label + ", " + (2 * d.hv).ToString("0") + " x " + (2 * d.hu).ToString("0") +
                              " m, bays " + (d.slope * 100f).ToString("0.0") + "%, driveway " + d.driveLen.ToString("0.0") + " m, " + w.Length + " waypoints):");
            yield return Arrive(d, car, rb, w[0], w[2]);
            if (!d.built) { DeckLapCheck.Fail(d.way + ": its tile never drew the deck (" + d.why + ")"); yield break; }
            DeckLapCheck.Line("  triangles " + d.trisDeck + "; tile draws " + TileDraws(d));
            var go = new GameObject("DeckLapPath");
            var tp = go.AddComponent<TrackPath>();
            int n = w.Length;
            var curv = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 a = w[Mathf.Max(0, i - 1)], b = w[i], c = w[Mathf.Min(n - 1, i + 1)];
                a.y = b.y = c.y = 0f;
                float len = Mathf.Max(0.5f, 0.5f * (Vector3.Distance(a, b) + Vector3.Distance(b, c)));
                curv[i] = (i == 0 || i == n - 1) ? 0f : Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad / len;
            }
            var sm = new float[n];
            for (int i = 0; i < n; i++) { float s = 0f; int k = 0; for (int o = -2; o <= 2; o++) { int j = i + o; if (j < 0 || j >= n) continue; s += curv[j]; k++; } sm[i] = s / k; }
            // a deck is driven at deck speed: the curvature the AI reads is 2.5x the
            // drawn one (a 16 m turning-bay arc taken as if it were 6.4 m)
            for (int i = 0; i < n; i++) sm[i] *= 2.5f;
            tp.waypoints = w; tp.curvatures = sm; tp.spacing = CityDecks.LapStepM; tp.roadWidth = 3f; tp.drag = false; tp.pointToPoint = true;
            var ai = carGo.AddComponent<AIDriver>();
            ai.path = tp; ai.skill = 0.95f;
            float t = 0f, slowFor = 0f, airFor = 0f, maxOff = 0f;
            int idx = 0, best = 0, launches = 0, falls = 0, lefts = 0, recov = 0;
            string lastWhy = ai.LastRecoveryWhy;
            bool done = false, stuck = false;
            while (t < LapTimeout)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var p = rb.position;
                idx = tp.NearestIndex(p, idx, 12);
                if (idx > best) best = idx;
                var q = w[idx];
                float off = new Vector2(p.x - q.x, p.z - q.z).magnitude;
                maxOff = Mathf.Max(maxOff, off);
                if (off > 6f) { if (lefts++ == 0) DeckLapCheck.Line("  LEFT the path at wp " + idx + " (" + off.ToString("0.0") + " m)"); }
                if (p.y < q.y - 2f) { if (falls++ == 0) DeckLapCheck.Line("  FELL at wp " + idx + " (" + (q.y - p.y).ToString("0.0") + " m under)"); }
                bool ground = Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, 1.6f, (1 << 8) | 1, QueryTriggerInteraction.Ignore);
                airFor = ground ? 0f : airFor + Time.fixedDeltaTime;
                if (airFor > 0.35f || rb.linearVelocity.y > 3.5f) { if (launches++ == 0) DeckLapCheck.Line("  LAUNCH at wp " + idx + " (air " + airFor.ToString("0.00") + " s, vy " + rb.linearVelocity.y.ToString("0.0") + ")"); airFor = 0f; }
                slowFor = rb.linearVelocity.magnitude < 0.5f ? slowFor + Time.fixedDeltaTime : 0f;
                if (ai.LastRecoveryWhy != lastWhy) { recov++; lastWhy = ai.LastRecoveryWhy; DeckLapCheck.Line("  RECOVERED at wp " + idx + ": " + lastWhy); }
                if (slowFor > 8f) { stuck = true; DeckLapCheck.Line("  STUCK at wp " + best + " of " + n + " " + p); break; }
                if (best >= n - 3) { done = true; break; }
            }
            Object.Destroy(ai); Object.Destroy(go);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            float pathM = 0f; for (int i = 1; i < n; i++) pathM += Vector3.Distance(w[i - 1], w[i]);
            DeckLapCheck.Line("  lap " + (done ? "COMPLETE" : "INCOMPLETE") + " in " + t.ToString("0.0") + " s over " + pathM.ToString("0") + " m (avg " +
                              (pathM / Mathf.Max(t, 0.1f) * 3.6f).ToString("0") + " km/h); max off path " + maxOff.ToString("0.0") + " m; launches " + launches +
                              ", falls " + falls + ", left " + lefts + ", recoveries " + recov + (stuck ? ", STUCK" : ""));
            if (!done || stuck || launches > 0 || falls > 0 || lefts > 0 || recov > 0) DeckLapCheck.Fail(d.way + ": lap not clean");
        }

        static int TileDraws(CityDecks.Deck d)
        {
            var c = d.W3(-d.hu + d.T * 0.5f, 1.5f, 0f);
            if (!Physics.Raycast(c, Vector3.down, out var hit, 3f, 1 << 8)) return -1;
            var root = hit.collider.transform.parent;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) if (r.enabled) n += r.sharedMaterials.Length;
            return n;
        }

        // ---- the photographs (normal game views: the game's camera, its renderer) ----
        IEnumerator Shots(CityDecks.Deck d, CityMap map, CarController car, Rigidbody rb)
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_DECK_TAG") ?? "after";
            bool night = System.Environment.GetEnvironmentVariable("PSX_DECK_HOUR") == "night";
            var cam = Camera.main;
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            if (chase != null) chase.enabled = false;
            if (CityDecks.Enabled)
            {
                // the car waits in the ground turning bay, out of every frame -
                // SETTLED on its wheels first: frozen one step after Arrive's
                // drop it hung 0.4 m over the floor (the "floating car" of the
                // first facade shot)
                yield return Arrive(d, car, rb, d.W3(-d.hu + 3f, 0f, d.hv * 0.5f), d.W3(0f, 0f, d.hv * 0.5f));
                for (int i = 0; i < 120; i++) yield return new WaitForFixedUpdate();
                rb.isKinematic = true;
                DeckLapCheck.Line("  shot car parked: " + (Physics.Raycast(car.transform.position + Vector3.up, Vector3.down, out var gh, 6f, 1 << 8)
                                  ? "body " + (car.transform.position.y - gh.point.y).ToString("0.00") + " m over the floor it stands on" : "no floor under it"));
            }
            else
            {
                // PSX_DECKS=0 (the BEFORE photos): the deck is the solid
                // building, so the car waits on the street behind the facade
                // camera and the shots wait for the tile to be built
                var behind = d.EntryP + d.EntryN * (d.driveLen + 40f);
                rb.isKinematic = true;
                car.TeleportTo(new Vector3(d.W(behind.x, behind.y).x, d.roadY + 0.5f, d.W(behind.x, behind.y).y), Quaternion.identity);
                var probe = d.W3(0f, 0f, 0f);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 120f && !Physics.Raycast(new Vector3(probe.x, d.y0 + d.levels * 3.05f + 30f, probe.z), Vector3.down, 80f, 1 << 8))
                    yield return null;
                for (int i = 0; i < 60; i++) yield return null;
            }
            DeckLapCheck.Line("deck " + d.way + " shots (" + tag + "): tile draws " + TileDraws(d) + ", deck drawn " + d.built + ", triangles " + d.trisDeck);
            float hz = d.hv * 0.5f, H = CityDecks.FloorM; int top = d.levels - 1;
            Vector2 E = d.EntryP, nL = d.EntryN;
            var views = new List<(string name, Vector3 eye, Vector3 at)>();
            var far = E + nL * (d.driveLen + 16f);
            if (!night)
            {
                views.Add(("1_facade", new Vector3(d.W(far.x, far.y).x, d.roadY + 1.6f, d.W(far.x, far.y).y), d.W3(0f, top * H * 0.5f, 0f)));
                var dr = E + nL * (d.driveLen + 1f);
                views.Add(("2_entrance", new Vector3(d.W(dr.x, dr.y).x, d.roadY + 1.2f, d.W(dr.x, dr.y).y), d.W3(E.x - nL.x * 12f, 1.0f, E.y - nL.y * 12f)));
            }
            float xi = d.x0 + 3f;
            views.Add(("3_inside", d.W3(xi, CityDecks.BayA(d, 1, xi) + 1.3f, -hz - 1.2f), d.W3(d.x1, CityDecks.BayA(d, 1, d.x1) + 1.0f, -hz)));
            if (!night) views.Add(("4_ramp", d.W3(d.x0 - 4f, CityDecks.TurnLo(1) + 1.3f, -hz), d.W3(d.x1, CityDecks.BayA(d, 1, d.x1) + 0.3f, -hz)));
            views.Add(("5_roof", d.W3(-d.hu + 2f, CityDecks.TurnLo(top) + 1.7f, d.hv - 2f), d.W3(d.x1, CityDecks.BayB(d, top - 1, d.x1) + 3f, -hz)));
            var dir = System.IO.Path.Combine("Screenshots", "Decks");
            System.IO.Directory.CreateDirectory(dir);
            var rt = new RenderTexture(1280, 720, 24);
            foreach (var v in views)
            {
                cam.transform.position = v.eye;
                cam.transform.rotation = Quaternion.LookRotation(v.at - v.eye, Vector3.up);
                for (int i = 0; i < 20; i++) yield return null;
                var keep = cam.targetTexture;
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = keep;
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null;
                string file = System.IO.Path.Combine(dir, tag + (night ? "_night_" : "_noon_") + v.name + ".png");
                System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.Destroy(tex);
                DeckLapCheck.Line("  shot " + file);
            }
            if (chase != null) chase.enabled = true;
            rb.isKinematic = false;
        }
    }
}
