using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE DOOR TOUR: every kind of door this edition has, walked IN THE
    /// PLAYER, through the front end's own buttons and hops.
    ///
    /// Why it exists: since the editions every door finds its scene through
    /// TrackCatalog.BuildIndexOfScene, whose player branch no editor tool can
    /// run (see <see cref="DoorAudit"/>). DoorAudit proves at boot that every
    /// door RESOLVES; this proves the doors actually OPEN - the button is
    /// pressed, the scene loads, the car is on the ground, nothing threw.
    /// tools\door-tour.mjs serves a built player locally, opens it with
    /// <c>?doortour</c>, takes a picture at each arrival and fails on any
    /// FAIL line. Review, 2026-09-29: "the design is right; it is just
    /// unproven where it matters."
    ///
    /// INERT ON THE SITE: it only wakes in a player served from this machine
    /// (127.0.0.1 / localhost) whose URL asks for it, never in the editor,
    /// and it walks a throwaway profile's save (the tool's headless browser
    /// starts with an empty one).
    ///
    /// Each door is the real one, pressed the way a player presses it:
    ///   MAIN  a race at the line (LifeHomeScreen.PendingTab "racenow", then
    ///         the page's own START) for a circuit, a stage, a reverse twin,
    ///         a sprint in its loop's scene, Chimney Rock and a drag; a stale
    ///         save's Charlotte race (START must run Sunset City GP); IN TOWN,
    ///         your street and the walk-in (the "town" / "drivehome" /
    ///         "garagewalk" hops); a seller's listing (GO AND SEE IT) and its
    ///         TEST DRIVE; a pizza drop (the counter's roll, then the
    ///         "deliverrun" hop); and free roam, which MAIN must REFUSE.
    ///   CITY  FREE ROAM and every city race button on the front page.
    /// </summary>
    public class DoorTour : MonoBehaviour
    {
        /// <summary>Is a tour running in this player?</summary>
        public static bool Active { get; private set; }

        /// <summary>Seconds a scene runs before it is judged: long enough for
        /// the grid to form, the city to stream in and a car to land.</summary>
        const float SettleSeconds = 8f;
        const float LoadTimeout = 90f;
        const float RefuseWait = 6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor) return;
            if (!RequestedBy(Application.absoluteURL)) return;
            Active = true;
            var go = new GameObject("DoorTour");
            DontDestroyOnLoad(go);
            go.AddComponent<DoorTour>();
        }

        /// <summary>
        /// Does this page URL ask for a tour? Only from this machine
        /// (127.0.0.1, localhost, [::1]) and only with a <c>doortour</c>
        /// query key. Pure, so the self-test can hold the rule to that.
        /// </summary>
        public static bool RequestedBy(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            string host = u.Host.ToLowerInvariant();
            if (host != "127.0.0.1" && host != "localhost" && host != "[::1]" && host != "::1") return false;
            string q = u.Query.TrimStart('?');
            foreach (var part in q.Split('&'))
            {
                string key = part.Split('=')[0];
                if (string.Equals(key, "doortour", System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  the doors
        // ------------------------------------------------------------------

        /// <summary>One door: how to set it up, how to press it, and what
        /// must be on the other side.</summary>
        public class Door
        {
            public string label;
            /// <summary>State the door expects the player to have set up
            /// (the venue picked, an order collected). Run before the front
            /// end loads.</summary>
            public System.Action prepare;
            /// <summary>LifeHomeScreen.PendingTab (MAIN) - the front end's own
            /// hop - or null.</summary>
            public string hop;
            /// <summary>The button pressed on the page the front end draws,
            /// by its GameObject name, or null when the hop itself loads.</summary>
            public string press;
            /// <summary>The scene that must load (a file name), or null to take
            /// it from the venue the scene was handed (RaceHandoff.TrackIndex)
            /// - a delivery's or a test drive's rolled venue.</summary>
            public string expectScene;
            /// <summary>"race", "drive", "foot", "seller": what must be in the
            /// scene once it has settled.</summary>
            public string sentinel = "race";
            /// <summary>The door must NOT open: no scene, and a toast saying why.</summary>
            public bool refuse;
            /// <summary>A door the game may legitimately decline (a seller who
            /// will not hand over the keys): SKIP, not FAIL.</summary>
            public bool mayDecline;
            /// <summary>When declined, walk this door first and try again (up
            /// to three times): a private seller says no at random, the next
            /// seller may not.</summary>
            public Door retryVia;
        }

        /// <summary>MAIN's doors (ALL's too - a tour of an ALL player walks
        /// these plus free roam, which ALL opens).</summary>
        public static List<Door> MainDoors(EditionKind edition)
        {
            var doors = new List<Door>();
            foreach (var id in new[] { "CityCircuit", "BlueRidge", "BlueRidgeRev", "BlowingRockSprint",
                                       "ChimneyRock", "ChimneyRockRev", "DragQuarter" })
            {
                if (!TrackCatalog.TryIndexOf(id, out int v) || !Edition.ShipsIn(TrackCatalog.At(v), edition)) continue;
                int captured = v;
                doors.Add(new Door
                {
                    label = "race " + id,
                    prepare = () => PickVenue(captured),
                    hop = "racenow",
                    press = StartButton,
                    expectScene = TrackCatalog.SceneIdOf(v),
                });
            }
            // An old save still pointing at a Charlotte race: the line's page
            // must offer (and START must run) this edition's fallback.
            if (edition == EditionKind.Main && TrackCatalog.TryIndexOf("UptownLoop", out int uptown))
                doors.Add(new Door
                {
                    label = "stale save's UptownLoop -> fallback",
                    prepare = () => PickVenue(uptown),
                    hop = "racenow",
                    press = StartButton,
                    expectScene = TrackCatalog.SceneIdOf(TrackCatalog.FirstOffered()),
                });
            doors.Add(new Door { label = "IN TOWN", hop = "town", expectScene = "Town", sentinel = "drive" });
            doors.Add(new Door { label = "your street (DRIVE)", hop = "drivehome", expectScene = "Neighborhood", sentinel = "drive" });
            doors.Add(new Door { label = "walk-in garage", hop = "garagewalk", expectScene = "Neighborhood", sentinel = "foot" });
            var seller = new Door
            {
                label = "seller viewing",
                prepare = OpenNextListing,
                press = "Btn_GO AND SEE IT  (an afternoon)",
                expectScene = "SellerLot",
                sentinel = "seller",
            };
            doors.Add(seller);
            doors.Add(new Door
            {
                label = "test drive",
                hop = "viewing",
                press = "Btn_TEST DRIVE IT",
                sentinel = "race",
                mayDecline = true,
                retryVia = seller,
            });
            doors.Add(new Door
            {
                label = "pizza delivery",
                prepare = CollectOrder,
                hop = "deliverrun",
                sentinel = "race",
            });
            doors.Add(new Door
            {
                label = "free roam (Charlotte)",
                hop = "charlotte",
                expectScene = "Charlotte",
                sentinel = "drive",
                refuse = edition == EditionKind.Main,
            });
            return doors;
        }

        /// <summary>CITY's doors: FREE ROAM and every race row the front page
        /// draws (twins included when the catalog has them).</summary>
        public static List<Door> CityDoors()
        {
            var doors = new List<Door>
            {
                new Door { label = "FREE ROAM - CHARLOTTE", press = "Btn_roam", expectScene = "Charlotte", sentinel = "drive" },
            };
            for (int i = 0; i < TrackCatalog.Count; i++)
            {
                var t = TrackCatalog.At(i);
                if (!t.IsCityRace || !Edition.ShipsIn(t, EditionKind.City)) continue;
                doors.Add(new Door
                {
                    label = "race " + t.id,
                    press = "Btn_race_" + t.id,
                    expectScene = TrackCatalog.SceneIdOf(i),
                });
            }
            return doors;
        }

        const string StartButton = "Btn_START  >>";

        static LifeState S => LifeSimManager.State;

        static void PickVenue(int v)
        {
            // What the line's page reads, and nothing it would refuse on: the
            // tour races several times a day, which the career allows once.
            S.trackIndex = v;
            S.lastRaceDay = 0;
            S.bookings.Clear();
        }

        static int listingPick;

        /// <summary>The paper's next car: a second visit (a retried test
        /// drive) goes to a different seller.</summary>
        static void OpenNextListing()
        {
            CarMarket.RefreshListings(S);
            if (S.newspaper.Count == 0) return;
            LifeHomeScreen.PendingListing = S.newspaper[listingPick % S.newspaper.Count].specId;
            listingPick++;
        }

        /// <summary>The counter's roll (PizzaShift.Collect) and the order
        /// taken out of the door (PizzaShift.Drive's StartRun) - the town leg
        /// between them is IN TOWN's door, walked on its own.</summary>
        static void CollectOrder()
        {
            var toppings = LifeRules.RollOrderToppings(LifeRules.MaxOrderBoxes);
            int bottles = LifeRules.RollOrderBottles(toppings.Length);
            int pay = LifeRules.RollDeliveryPay(S) * toppings.Length;
            int track = LifeRules.DeliveryTrackIndex(S);
            float drop = LifeRules.RollDropFraction();
            float par = LifeRules.DeliveryParSeconds(track, drop);
            PizzaRun.StartRun(toppings, bottles, pay, track, par, TimeOfDay.ForSlot(S.slotIndex, S.day), drop);
        }

        /// <summary>A career to walk MAIN's doors with: the wizard's commit
        /// and a car, full, at home. Only ever in the tool's throwaway
        /// browser profile.</summary>
        static void SeedCareer()
        {
            if (!LifeSimManager.HasSave || string.IsNullOrEmpty(S.playerName))
                LifeSimManager.StartNewGame("DOOR TOUR", 30, LifeRules.DefaultJobIndex);
            if (S.cars.Count == 0)
            {
                // The wizard's own second step: the first of the starting
                // lanes it offers (the built-in car only when there is no
                // catalog, as the wizard itself falls back).
                var lanes = CarMarket.RollStartingLanes(S.basePay, S.creditScore);
                if (lanes.Count > 0) CarMarket.ApplyStartingLane(S, lanes[0]);
                else LifeRules.SeedFallbackCar(S);
            }
            FillUp();
            LifeSimManager.Save();
        }

        static void FillUp()
        {
            var car = S.ActiveCar;
            if (car != null) car.fuel = 100f;
        }

        // ------------------------------------------------------------------
        //  the walk
        // ------------------------------------------------------------------

        int lastLoaded = -1;
        float lastLoadedAt;
        bool recording;
        readonly List<string> errors = new List<string>();

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.logMessageReceived += OnLog;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= OnLog;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            lastLoaded = s.buildIndex;
            lastLoadedAt = Time.realtimeSinceStartup;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (!recording) return;
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (msg != null && msg.StartsWith("[Door")) return;
            // Where it happened: the scene being left, the front end, or the
            // door's own scene - the verdict is per door, the error may not be.
            string where = SceneManager.GetActiveScene().name;
            string top = string.IsNullOrEmpty(stack) ? "" : " @ " + Clip(stack.Split('\n')[0].Trim(), 80);
            if (errors.Count < 20) errors.Add(type + " [in " + where + "]: " + Clip(msg, 140) + top);
        }

        static void Say(string line) => Debug.Log(line);

        IEnumerator Start()
        {
            // The boot scene's own first frames (the front end builds itself).
            for (int i = 0; i < 3; i++) yield return null;
            var edition = Edition.Current;
            var doors = edition == EditionKind.City ? CityDoors() : MainDoors(edition);
            if (Edition.HasCareer)
            {
                try { SeedCareer(); }
                catch (System.Exception e) { Say("[DoorTour] FAIL setup: the career would not seed: " + e.Message); }
            }
            Say("[DoorTour] BEGIN " + Edition.Name(edition) + " " + doors.Count + " doors, " +
                SceneManager.sceneCountInBuildSettings + " scenes in this build");
            int pass = 0, fail = 0, skip = 0;
            for (int n = 0; n < doors.Count; n++)
            {
                string verdict = null;
                for (int tries = 1; ; tries++)
                {
                    yield return Walk(doors[n], n + 1, doors.Count, v => verdict = v);
                    if (verdict == null || !verdict.StartsWith("SKIP") || doors[n].retryVia == null || tries >= 3) break;
                    // Declined: another way in, and ask again.
                    Say("[DoorTour] RETRY " + verdict);
                    string via = null;
                    yield return Walk(doors[n].retryVia, n + 1, doors.Count, v => via = v);
                    Say("[DoorTour] RETRY " + (via ?? "no verdict"));
                    if (via == null || !via.StartsWith("PASS")) break;
                }
                if (verdict == null) verdict = "FAIL " + doors[n].label + ": no verdict";
                if (verdict.StartsWith("PASS")) pass++;
                else if (verdict.StartsWith("SKIP")) skip++;
                else fail++;
                if (verdict.StartsWith("FAIL")) Debug.LogWarning("[DoorTour] " + verdict);
                else Say("[DoorTour] " + verdict);
            }
            Say("[DoorTour] DONE " + Edition.Name(edition) + " " + pass + "/" + doors.Count + " passed, " +
                fail + " failed, " + skip + " skipped");
            // Home, and stop: the page stays up for the last picture.
            recording = false;
            SceneManager.LoadScene(0);
        }

        IEnumerator Walk(Door d, int n, int total, System.Action<string> done)
        {
            errors.Clear();
            recording = true;
            if (Edition.HasCareer)
            {
                FillUp();
                d.prepare?.Invoke();
                LifeHomeScreen.PendingTab = d.hop;
                LifeSimManager.Save();
            }
            else
            {
                d.prepare?.Invoke();
                CityFrontEnd.PendingPage = "drive";
            }

            // The front end first: every door is on one of its pages.
            lastLoaded = -1;
            SceneManager.LoadScene(0);
            float t = 0f;
            while (lastLoaded != 0 && t < LoadTimeout) { t += Time.unscaledDeltaTime; yield return null; }
            if (lastLoaded != 0) { recording = false; done("FAIL " + d.label + ": the front end never came back"); yield break; }
            // A hop loads its scene from the front end's own Start; a page
            // door needs the page built (a frame) before its button exists.
            yield return null;
            yield return null;

            // A toast lives ~4 s; the verdict may be read later than that, so
            // the last one shown since the front end came up is kept.
            string seenToast = ToastText();
            // The door is pressed now (a hop pressed itself as the front end
            // started, a frame or two ago).
            float doorAt = Time.realtimeSinceStartup;
            if (d.press != null && lastLoaded == 0)
            {
                var b = FindButton(d.press);
                if (b == null)
                {
                    string toast = seenToast;
                    recording = false;
                    done((d.mayDecline ? "SKIP " : "FAIL ") + d.label + ": no '" + d.press + "' on the page" +
                         (toast != null ? " (toast: " + toast + ")" : "") + "; buttons: " + ButtonNames(14));
                    yield break;
                }
                if (!b.interactable)
                {
                    recording = false;
                    done((d.mayDecline ? "SKIP " : "FAIL ") + d.label + ": '" + d.press + "' is disabled");
                    yield break;
                }
                b.onClick.Invoke();
            }

            // Wait for the door's scene, or for the refusal.
            t = 0f;
            float limit = d.refuse ? RefuseWait : LoadTimeout;
            while (lastLoaded == 0 && t < limit)
            {
                seenToast = ToastText() ?? seenToast;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (lastLoaded == 0)
            {
                string toast = ToastText() ?? seenToast;
                recording = false;
                if (d.refuse && !string.IsNullOrEmpty(toast))
                    done("PASS " + d.label + ": refused, nothing loaded (toast: " + toast + ")");
                else if (d.mayDecline && !string.IsNullOrEmpty(toast))
                    done("SKIP " + d.label + ": the game declined (toast: " + toast + ")");
                else
                    done("FAIL " + d.label + ": nothing loaded in " + Mathf.RoundToInt(t) + " s" +
                         (toast != null ? " (toast: " + toast + ")" : " and no toast"));
                yield break;
            }
            if (d.refuse)
            {
                recording = false;
                done("FAIL " + d.label + ": should have been refused, loaded #" + lastLoaded + " " +
                     SceneManager.GetActiveScene().path);
                yield break;
            }

            // ARRIVED. Which scene should this have been?
            int arrived = lastLoaded;
            float loadSeconds = Mathf.Max(0f, lastLoadedAt - doorAt);
            var scene = SceneManager.GetActiveScene();
            int handed = RaceHandoff.TrackIndex;
            string wantName = d.expectScene ??
                              (handed >= 0 && handed < TrackCatalog.Count ? TrackCatalog.SceneIdOf(handed) : "?");
            string want = TrackCatalog.ScenePathOf(wantName);
            int resolved = TrackCatalog.BuildIndexOfScene(wantName);

            // Let it settle: the grid forms, the city streams in, the car
            // lands. Sampled twice, so a car falling through the world shows.
            yield return new WaitForSecondsRealtime(1.5f);
            var car = PlayerCar();
            float y0 = car != null ? car.transform.position.y : 0f;
            yield return new WaitForSecondsRealtime(SettleSeconds);
            Say("[DoorTour] ARRIVED " + n + "/" + total + " " + d.label);
            yield return new WaitForSecondsRealtime(1.5f);

            var problems = new List<string>();
            if (SceneManager.GetActiveScene().buildIndex != arrived)
                problems.Add("left the scene on its own (now #" + SceneManager.GetActiveScene().buildIndex + " " +
                             SceneManager.GetActiveScene().path + ")");
            if (scene.path != want) problems.Add("loaded '" + scene.path + "', wanted '" + want + "'");
            if (resolved != arrived) problems.Add("resolves to #" + resolved + " but #" + arrived + " loaded");
            string what = Sentinel(d.sentinel, out string missing);
            if (missing != null) problems.Add(missing);
            car = PlayerCar();
            if (car != null && (d.sentinel == "race" || d.sentinel == "drive"))
            {
                float y1 = car.transform.position.y;
                if (float.IsNaN(y1) || y0 - y1 > 25f)
                    problems.Add("the car fell " + (y0 - y1).ToString("0") + " m (" + y0.ToString("0.0") + " -> " +
                                 y1.ToString("0.0") + ")");
            }
            if (errors.Count > 0) problems.Add(errors.Count + " error(s): " + string.Join(" / ", errors.GetRange(0, Mathf.Min(3, errors.Count))));
            recording = false;

            // The venue the scene was handed, for anything raced: a twin and a
            // sprint share their road's scene, and this says which one ran.
            string where = "#" + arrived + " " + scene.name +
                           (d.sentinel == "race" && handed >= 0 && handed < TrackCatalog.Count
                               ? " as " + TrackCatalog.At(handed).id : "");
            if (problems.Count == 0)
                done("PASS " + d.label + ": " + where + ", " + what + ", loaded in " +
                     loadSeconds.ToString("0.0") + " s, 0 errors");
            else
                done("FAIL " + d.label + ": " + where + ": " + string.Join("; ", problems));
        }

        static CarController PlayerCar()
        {
            if (RaceManager.Instance != null && RaceManager.Instance.playerCar != null)
                return RaceManager.Instance.playerCar;
            var applier = Object.FindAnyObjectByType<RaceHandoffApplier>();
            if (applier != null && applier.playerCar != null) return applier.playerCar;
            var city = Object.FindAnyObjectByType<PSXRacing.City.CityMode>();
            if (city != null && city.player != null) return city.player;
            var town = Object.FindAnyObjectByType<PSXRacing.Town.TownWorld>();
            if (town != null && town.player != null) return town.player;
            return null;
        }

        /// <summary>What proves the door opened onto something playable, as
        /// a phrase for the PASS line; <paramref name="missing"/> says what
        /// is not there.</summary>
        static string Sentinel(string kind, out string missing)
        {
            missing = null;
            switch (kind)
            {
                case "race":
                    if (RaceManager.Instance == null) { missing = "no RaceManager"; return ""; }
                    if (RaceManager.Instance.playerCar == null) { missing = "no player car on the grid"; return ""; }
                    return "race manager + player car";
                case "drive":
                    if (PlayerCar() == null) { missing = "no player car"; return ""; }
                    return "player car";
                case "seller":
                    if (Object.FindAnyObjectByType<PSXRacing.OnFoot.SellerLotWorld>() == null)
                    { missing = "no SellerLotWorld"; return ""; }
                    return "seller's driveway";
                case "foot":
                    if (Camera.main == null) { missing = "no camera"; return ""; }
                    return "on foot, camera live";
            }
            return kind;
        }

        static Button FindButton(string name)
        {
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude))
                if (b != null && b.gameObject.name == name) return b;
            return null;
        }

        static string ButtonNames(int max)
        {
            var names = new List<string>();
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude))
            {
                if (b == null) continue;
                names.Add(b.gameObject.name);
                if (names.Count >= max) break;
            }
            return string.Join(", ", names);
        }

        static string ToastText()
        {
            var go = GameObject.Find("Toast");
            if (go == null) return null;
            var text = go.GetComponentInChildren<Text>();
            return text != null ? Clip(text.text, 90) : null;
        }

        static string Clip(string s, int n) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s.Substring(0, n) + "...").Replace('\n', ' ');
    }
}
