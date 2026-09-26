using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Moving traffic in the RUNNING game (tools\traffic-play-check.ps1).
    ///
    /// Loads a venue at the morning rush, lets the countdown run out, puts the
    /// player on autopilot and watches the traffic for thirty seconds:
    /// which SIDE each car drives on (right of its own direction of travel -
    /// North America), which way it FACES, how high it rides over the tarmac,
    /// and how fast it goes. Then it lines the player up behind a car in its
    /// lane and rear-ends it: the traffic car must become a wreck and the
    /// player's car must take the damage - the "real physics" the owner chose.
    /// With graphics (no -nographics), it saves three chase-camera frames.
    /// </summary>
    public static class TrafficPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

        [MenuItem("PSX Racing/Check Traffic (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            string id = System.Environment.GetEnvironmentVariable("PSX_TRAFFIC_VENUE");
            if (string.IsNullOrEmpty(id)) id = "RidgePass";
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            log.AppendLine("traffic on " + id + ":");
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Morning;
            var cars = CarCatalog.All;
            if (cars.Count > 2)
            {
                RaceHandoff.CarSpecId = cars[0].id;
                RaceHandoff.OpponentSpecIds = cars[1].id + ";" + cars[2].id;
                RaceHandoff.OpponentSkills = "1.0;0.95";
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TrafficPlayCheckRunner").AddComponent<TrafficPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "TRAFFIC KEEPS RIGHT." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_traffic_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class TrafficPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();

            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            var path = rm != null ? rm.path : null;
            TrafficPlayCheck.Check(rm != null && car != null && path != null, "the scene has a race, a player and a path");
            if (rm == null || car == null || path == null) { Done(); yield break; }

            float until = Time.realtimeSinceStartup + 15f;
            while (rm.State == RaceManager.RaceState.Countdown && Time.realtimeSinceStartup < until) yield return null;
            TrafficPlayCheck.Check(rm.State == RaceManager.RaceState.Racing, "the race goes live", rm.State);

            var ts = TrafficSystem.Instance;
            TrafficPlayCheck.Check(ts != null, "the race has a traffic system");
            if (ts == null) { Done(); yield break; }

            // Autopilot: the player's input off, an AIDriver on its line.
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.inputEnabled = false;
            var auto = car.gameObject.AddComponent<AIDriver>();
            auto.path = path;
            auto.skill = 0.9f;

            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            float limit = (def != null ? def.speedLimitKmh : 80f) / 3.6f;
            int samples = 0, wrongSide = 0, wrongWay = 0, badRide = 0, tooFast = 0, maxLive = 0;
            int sawFwd = 0, sawBack = 0;
            float worstRide = 0f, worstSide = 0f;
            int shot = 0;
            float t0 = Time.time;
            while (Time.time - t0 < 30f)
            {
                yield return new WaitForSeconds(0.5f);
                var bodies = ts.Obstacles;
                maxLive = Mathf.Max(maxLive, bodies.Count);
                foreach (var rb in bodies)
                {
                    if (rb == null || !rb.gameObject.activeInHierarchy || rb.useGravity) continue;   // gravity on = a wreck
                    samples++;
                    Vector3 pos = rb.position;
                    int i = path.NearestIndex(pos);
                    Vector3 p = path.GetPoint(i), tan = path.GetTangent(i);
                    Vector3 right = Vector3.Cross(Vector3.up, tan).normalized;
                    float lat = Vector3.Dot(pos - p, right);
                    float along = Vector3.Dot(rb.transform.forward, tan);
                    int dir = along >= 0f ? 1 : -1;
                    if (dir > 0) sawFwd++; else sawBack++;
                    // Right of its OWN direction: + for the race direction, - against it.
                    if (lat * dir < 0.8f) { wrongSide++; worstSide = Mathf.Max(worstSide, -lat * dir); }
                    if (Mathf.Abs(along) < 0.85f) wrongWay++;
                    if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, 1 << 8))
                    {
                        float ride = Mathf.Abs(pos.y - hit.point.y);
                        worstRide = Mathf.Max(worstRide, ride);
                        if (ride > 0.25f) badRide++;
                    }
                    if (rb.linearVelocity.magnitude > limit * 1.15f) tooFast++;
                }
                if (Time.time - t0 > 8f && shot < 3 && bodies.Count > 0 && Shoot("traffic_" + shot)) shot++;
            }
            TrafficPlayCheck.Note("peak " + maxLive + " traffic cars live; " + samples + " samples (" +
                                  sawFwd + " with the race, " + sawBack + " oncoming)");
            TrafficPlayCheck.Note(ts.WreckLog.Count + " wrecks in the drive: " + string.Join("; ", ts.WreckLog));
            TrafficPlayCheck.Check(maxLive > 0 && samples > 20, "traffic appears once the race is on", maxLive);
            TrafficPlayCheck.Check(sawBack > 0, "some of it is ONCOMING on a two-way road", sawBack);
            TrafficPlayCheck.Check(wrongSide == 0, "every car keeps RIGHT of its own direction of travel",
                                   wrongSide + " samples, worst " + worstSide.ToString("0.00") + " m over");
            TrafficPlayCheck.Check(wrongWay == 0, "and faces the way it is going", wrongWay);
            TrafficPlayCheck.Check(badRide == 0, "and rides on the tarmac (within 25 cm)",
                                   "worst " + worstRide.ToString("0.00") + " m");
            TrafficPlayCheck.Check(tooFast == 0, "and keeps near the " + (limit * 3.6f).ToString("0") + " km/h limit", tooFast);

            // ---- HIT ONE -----------------------------------------------------
            // Wait for one: at any given moment every same-direction car may
            // be far off, or already a wreck.
            Rigidbody victim = null;
            float waitUntil = Time.time + 20f;
            while (victim == null && Time.time < waitUntil)
            {
                float best = float.MaxValue;
                foreach (var rb in ts.Obstacles)
                {
                    if (rb == null || rb.useGravity) continue;
                    // Either direction: a T-bone does not care which way it is going.
                    float d = (rb.position - car.transform.position).sqrMagnitude;
                    if (d < best) { best = d; victim = rb; }
                }
                if (victim == null) yield return new WaitForSeconds(0.5f);
            }
            TrafficPlayCheck.Check(victim != null, "a driving traffic car to hit");
            if (victim != null)
            {
                Destroy(auto);
                var responder = car.GetComponent<CollisionResponder>();
                float damage0 = responder != null ? responder.DamageScore : 0f;
                // A T-BONE, not a rear-end: a car rolling straight on a
                // winding road leaves its lane before it catches anyone. The
                // player starts beside the lane, just ahead of the traffic car,
                // and drives across it.
                Vector3 vf = victim.transform.forward, vr = victim.transform.right;
                Vector3 from = victim.position + vr * 5.5f + vf * (victim.linearVelocity.magnitude * 0.35f)
                               + Vector3.up * 0.4f;
                car.TeleportTo(from, Quaternion.LookRotation(-vr, Vector3.up));
                var prog = rm.GetProgress(car);
                if (prog != null) prog.nearestIdx = path.NearestIndex(from);
                yield return new WaitForFixedUpdate();
                car.SetRolling(12f);
                float until2 = Time.time + 4f;
                float closest = float.MaxValue;
                while (Time.time < until2 && !victim.useGravity)
                {
                    car.steerInput = 0f; car.throttleInput = 0.6f; car.brakeInput = 0f;
                    closest = Mathf.Min(closest, (car.transform.position - victim.position).magnitude);
                    yield return new WaitForFixedUpdate();
                }
                TrafficPlayCheck.Note("t-bone: closest " + closest.ToString("0.0") + " m; wrecks: " +
                                      string.Join("; ", ts.WreckLog));
                TrafficPlayCheck.Check(victim.useGravity, "the car that is hit becomes a WRECK (the servo lets go)");
                yield return new WaitForSeconds(1f);
                float damage1 = responder != null ? responder.DamageScore : 0f;
                TrafficPlayCheck.Check(damage1 > damage0, "and the player's car takes the damage",
                                       damage0.ToString("0.0") + " -> " + damage1.ToString("0.0"));
                TrafficPlayCheck.Note("wreck moving at " + victim.linearVelocity.magnitude.ToString("0.0") +
                                      " m/s a second later");
                // "Traffic gets stuck in the road (literally)": a wreck must come
                // to rest ON the tarmac, not sunk into it by a lifted box.
                yield return new WaitForSeconds(3f);
                if (Physics.Raycast(victim.position + Vector3.up * 2f, Vector3.down, out RaycastHit rest, 5f, 1 << 8))
                {
                    float sunk = rest.point.y - victim.position.y;
                    TrafficPlayCheck.Check(sunk < 0.04f, "the wreck rests ON the road, not sunk into it",
                                           (sunk * 100f).ToString("0") + " cm below the surface");
                }
                else TrafficPlayCheck.Note("the wreck left the road; rest height not measured");
                Shoot("traffic_wreck");
            }
            Done();
        }

        static bool Shoot(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;
            var cam = Camera.main;
            if (cam == null) return false;
            var rt = new RenderTexture(960, 540, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release();
            return true;
        }

        void Done()
        {
            TrafficPlayCheck.Finish();
            EditorApplication.Exit(TrafficPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
