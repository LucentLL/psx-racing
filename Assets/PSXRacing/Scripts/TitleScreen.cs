using System.Collections.Generic;
using PSXRacing.LifeSim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// THE TITLE SCREEN: the Linn Cove Viaduct, live, behind CONTINUE GAME and
    /// NEW GAME.
    ///
    /// Owner 2026-10-31, after the first title (uptown Charlotte at night)
    /// shipped: "The uptown buildings need a LOT of help ... Another option is
    /// to load the Blue Ridge Parkway track and have a static camera view of
    /// the iconic bridge with traffic (and the occasional racer) drive by.
    /// sound effects incoming and passing by, mountain echo, nice view,
    /// seasons, occasional fog. could even transition seasons." Chosen: build
    /// it now, seasons cycling behind a fade.
    ///
    /// A cold boot loads the Blue Ridge stage as a rolling-start race with
    /// this component on its RaceManager. The player's car is parked out of
    /// sight beside the viaduct (the traffic window centres on it), the
    /// camera stands still on the valley side looking along the viaduct, the
    /// AI field races through and finished racers are sent back upstream one
    /// at a time. Everything that belongs to driving - HUD, gauges, cockpit,
    /// touch pedals, pause menu, replay recorder - is switched off; the PSX
    /// display canvas stays, since the picture itself goes through it.
    ///
    /// Either button clears the handoff and reloads scene 0, where the home
    /// screen carries on as it always has. <see cref="Shown"/> keeps the title
    /// to once per run.
    /// </summary>
    public partial class TitleScreen : MonoBehaviour
    {
        /// <summary>The title has had its turn this run (shown or skipped).</summary>
        public static bool Shown;
        /// <summary>Tools: never show the title (a check that drives the menu).</summary>
        public static bool Skip;
        /// <summary>The title owns this scene.</summary>
        public static bool On { get; private set; }

        const string Venue = "BlueRidge";

        /// <summary>A season lasts this long before the fade to the next.</summary>
        const float SeasonS = 100f;
        const float FadeS = 0.9f, FirstFadeAfterS = 1.2f, FirstFadeS = 2.0f;
        /// <summary>A finished racer goes round again this often (a range,
        /// so it does not feel like a timer), started this far upstream.</summary>
        const float RacerEveryMinS = 45f, RacerEveryMaxS = 90f, RacerUpstreamM = 520f, RacerMps = 27f;
        const float Fov = 42f;

        RaceManager rm;
        RaceHandoffApplier applier;
        Camera cam;
        int i0, i1, im;
        float started, sweepAt, nextRacer, nextSeason, trafficAudioAt;
        Season season;
        Image fade;
        float fadeFrom, fadeTo, fadeT0, fadeLen;
        System.Action atBlack;
        RectTransform root;
        Canvas canvas;
        AudioClip trafficClip;
        readonly HashSet<GameObject> voiced = new HashSet<GameObject>();

        // =================== boot ===================

        /// <summary>
        /// Called by the home screen as it starts. True when the title took
        /// the boot (Blue Ridge is loading); false to carry on as before.
        /// </summary>
        public static bool TryBoot()
        {
            if (Shown) return false;
            Shown = true;
            if (Skip || Application.isBatchMode || DoorTour.Active || PerfTour.Active || ShotLink.Active) return false;
            if (RaceHandoff.ResultReady || RaceHandoff.TestDrive) return false;
            if (!TrackCatalog.TryIndexOfShipped(Venue, out int track)) return false;
            int scene = TrackCatalog.SceneIndex(track);
            if (!TrackCatalog.SceneShipped(scene)) return false;

            RaceHandoff.ClearAll();
            RaceHandoff.TrackIndex = track;
            RaceHandoff.TitleScreen = true;
            // Rolling: live on frame one, no countdown, no starters.
            RaceHandoff.RollingStartKmh = 60f;
            RaceHandoff.TrafficLevel = TrafficLevels.Medium;
            var now = System.DateTime.Now;
            RaceHandoff.CalendarDay = DayIn(Seasons.OfMonth(now.Month), now.Month, Mathf.Min(now.Day, 28));
            RaceHandoff.TimeOfDayIndex = PickHour();
            RaceHandoff.WeatherOverride = (int)PickWeather(Seasons.Of(RaceHandoff.CalendarDay));
            SceneGate.Load(scene);
            return true;
        }

        static int DayIn(Season s, int month = 0, int dom = 15)
        {
            if (month <= 0) month = s == Season.Winter ? 1 : s == Season.Spring ? 4 : s == Season.Summer ? 7 : 10;
            return LifeRules.DayNumber(new System.DateTime(1999, month, dom));
        }

        static int PickHour()
        {
            // -psx-title-hour N (TimeOfDay index: 0 dawn .. 6 night): one hour every
            // run, for side-by-side shots (the A/B of a render setting)
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-psx-title-hour" && int.TryParse(args[i + 1], out int fixedHour)) return Mathf.Clamp(fixedHour, 0, 6);
            float r = Random.value;
            // golden hour mostly, as the drone film (owner, 2026-11-02)
            return r < 0.25f ? TimeOfDay.Morning : r < 0.4f ? TimeOfDay.Afternoon : TimeOfDay.Sunset;
        }

        /// <summary>Clear mostly; now and then fog in the gaps (the owner's
        /// "occasional fog"), and snow some winters.</summary>
        static Weather PickWeather(Season s)
        {
            float r = Random.value;
            if (s == Season.Winter && r < 0.35f) return Weather.Snow;
            return r > 0.90f ? Weather.Fog : Weather.Clear;
        }

        /// <summary>RaceManager.Start, last thing: take the race over when
        /// the boot asked for the title.</summary>
        public static void Attach(RaceManager race)
        {
            if (!RaceHandoff.TitleScreen || race == null) return;
            var def = TrackCatalog.At(RaceHandoff.TrackIndex);
            TrackCatalog.EnsureStage(def);
            if (race.path == null || race.path.Count < 20 || def.bridges == null || def.bridges.Length == 0)
            {
                Debug.LogWarning("[TitleScreen] this venue cannot carry the title - on to the home screen");
                RaceHandoff.ClearAll();
                SceneManager.LoadScene(0);
                return;
            }
            var t = race.gameObject.AddComponent<TitleScreen>();
            t.rm = race;
            t.Begin(def);
        }

        void Begin(TrackCatalog.TrackDef def)
        {
            On = true;
            started = Time.unscaledTime;
            applier = rm.GetComponent<RaceHandoffApplier>();
            season = Seasons.Of(RaceHandoff.CalendarDay);
            nextSeason = Time.unscaledTime + SeasonS;
            nextRacer = Time.time + Random.Range(RacerEveryMinS, RacerEveryMaxS);

            // The viaduct: the venue's longest bridge span.
            var path = rm.path;
            Vector2 best = def.bridges[0];
            foreach (var b in def.bridges) if (b.y - b.x > best.y - best.x) best = b;
            i0 = Mathf.Clamp(Mathf.RoundToInt(best.x / path.spacing), 1, path.Count - 2);
            i1 = Mathf.Clamp(Mathf.RoundToInt(best.y / path.spacing), 1, path.Count - 2);
            im = (i0 + i1) / 2;

            var replay = rm.GetComponent<RaceReplay>();
            if (replay != null) replay.enabled = false;

            if (rm.playerCar != null) ParkCar(rm.playerCar, ParkSpot(path));
            Quiet();

            var chase = FindAnyObjectByType<ChaseCamera>();
            cam = chase != null ? chase.GetComponent<Camera>() : null;
            if (cam == null) cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetParent(null, true);
                cam.fieldOfView = Fov;
                PlaceCamera(path);
                Ambience(cam.gameObject);
            }
            BuildUI();
            // the drone film's shots, from the postcard spots found above (TitleScreen.Drone.cs)
            BeginDrone();
        }

        // =================== the parked car ===================

        /// <summary>Beside the road at the viaduct's near end, up and off to
        /// the side: the traffic window (200 m behind to 700 m ahead of the
        /// player) centres on the viaduct, and nothing in a lane brakes for it.</summary>
        Vector3 ParkSpot(TrackPath path)
        {
            int i = Mathf.Max(1, i0 - 35);
            var p = path.GetPoint(i);
            var right = Vector3.Cross(Vector3.up, path.GetTangent(i)).normalized;
            return p + right * 25f + Vector3.up * 25f;
        }

        static void ParkCar(CarController car, Vector3 at)
        {
            car.enabled = false;   // its physics would write a kinematic body's velocity every step
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.position = at;
            }
            car.transform.position = at;
            foreach (var r in car.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in car.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var a in car.GetComponentsInChildren<AudioSource>(true)) a.mute = true;
            foreach (var l in car.GetComponentsInChildren<Light>(true)) l.enabled = false;
            Off<PlayerCarInput>(car); Off<CarLights>(car); Off<EngineAudio>(car); Off<TireAudio>(car);
            Off<TurboAudio>(car); Off<CollisionAudio>(car); Off<SkidMarks>(car); Off<TireSmoke>(car);
            Off<StuckRecovery>(car); Off<FuelTank>(car);
        }

        static void Off<T>(Component on) where T : Behaviour
        {
            foreach (var b in on.GetComponentsInChildren<T>(true)) b.enabled = false;
        }

        /// <summary>Everything that belongs to driving, scene-wide.</summary>
        static void Quiet()
        {
            foreach (var b in FindObjectsByType<ChaseCamera>(FindObjectsInactive.Include)) b.enabled = false;
            foreach (var b in FindObjectsByType<CockpitView>(FindObjectsInactive.Include)) b.enabled = false;
            foreach (var b in FindObjectsByType<PauseMenu>(FindObjectsInactive.Include)) b.enabled = false;
            foreach (var b in FindObjectsByType<TouchControls>(FindObjectsInactive.Include)) b.enabled = false;
            foreach (var b in FindObjectsByType<RaceHUD>(FindObjectsInactive.Include)) b.enabled = false;
            foreach (var b in FindObjectsByType<GaugeCluster>(FindObjectsInactive.Include)) b.enabled = false;
        }

        /// <summary>Every canvas but the PSX display (the picture itself) and
        /// ours: the HUD, cluster, cockpit and touch canvases, and any a
        /// driving system builds later (so this runs again now and then).</summary>
        void SweepCanvases()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c == canvas || !c.isRootCanvas) continue;
                if (c.gameObject.name == "DisplayCanvas") continue;
                c.gameObject.SetActive(false);
            }
        }

        // =================== the camera ===================

        /// <summary>
        /// THE POSTCARD (the owner's Linn Cove photo): up the slope at one end
        /// of the viaduct, close, looking ALONG the deck as it sweeps round the
        /// mountain, the valley open beyond it. The first cut stood 110 m out
        /// on the valley side and the deck was a thin band in a lot of empty
        /// hillside ("too far away ... doesn't capture the iconic view").
        ///
        /// Spots are tried closest first, uphill side first, at both ends;
        /// one is good when it sees at least 4 of 5 points along the deck and
        /// stands high enough over the ground to be above the canopy (tree
        /// crowns have no colliders, so the line-of-sight test cannot see
        /// them). Every good spot is kept: V steps through them (a hidden key,
        /// for picking the best one off the live build).
        /// </summary>
        void PlaceCamera(TrackPath path)
        {
            views.Clear();
            int n = i1 - i0;
            var deck = new Vector3[5];
            foreach (int end in new[] { 0, 1 })
            {
                // from the start looking along the way the road runs, or from
                // the far end looking back
                int at = end == 0 ? i0 : i1;
                float dirSign = end == 0 ? 1f : -1f;
                var a = path.GetPoint(at);
                var along = path.GetTangent(at).normalized * dirSign;
                var right = Vector3.Cross(Vector3.up, along).normalized;
                float upR = GroundAt(a + right * 50f), upL = GroundAt(a - right * 50f);
                float uphill = upR >= upL ? 1f : -1f;
                for (int k = 0; k < 5; k++)
                {
                    float f = 0.15f + 0.175f * k;
                    int wi = end == 0 ? i0 + Mathf.RoundToInt(n * f) : i1 - Mathf.RoundToInt(n * f);
                    deck[k] = path.GetPoint(wi) + Vector3.up * 1.5f;
                }
                int li = end == 0 ? i0 + Mathf.RoundToInt(n * 0.6f) : i1 - Mathf.RoundToInt(n * 0.6f);
                var look = path.GetPoint(li) + Vector3.up * 1f;

                foreach (float side in new[] { uphill, -uphill })
                    foreach (float dist in new[] { 30f, 45f, 60f, 80f })
                        foreach (float back in new[] { 15f, 35f })
                            foreach (float up in new[] { 25f, 35f, 50f })
                            {
                                var eye = a - along * back + right * side * dist + Vector3.up * up;
                                if (eye.y - GroundAt(eye) < CanopyClearM) continue;
                                int seen = 0;
                                foreach (var p in deck)
                                    if (!Physics.Linecast(eye, p, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                        seen++;
                                if (seen >= 4) views.Add((eye, look));
                            }
            }
            if (views.Count == 0)
            {
                // nothing passed: the old wide view from the valley side
                var a = path.GetPoint(i0);
                var t = path.GetTangent(i0).normalized;
                var right = Vector3.Cross(Vector3.up, t).normalized;
                views.Add((a - t * 60f + right * 110f + Vector3.up * 35f, path.GetPoint(im) + Vector3.up * 2f));
            }
            view = 0;
            ShowView();
        }

        const float CanopyClearM = 22f;
        readonly List<(Vector3 eye, Vector3 look)> views = new List<(Vector3, Vector3)>();
        int view;

        void ShowView()
        {
            var (eye, look) = views[view];
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            Debug.Log($"[TitleScreen] view {view + 1}/{views.Count}: eye {eye} looking at {look} (viaduct wp {i0}-{i1})");
        }

        static float GroundAt(Vector3 p)
        {
            var top = new Vector3(p.x, p.y + 600f, p.z);
            return Physics.Raycast(top, Vector3.down, out var hit, 2000f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ? hit.point.y : p.y - 1000f;
        }

        // =================== the sound ===================

        /// <summary>The mountain: wind under everything and an echo on the
        /// listener (a reverb zone; the web player has no reverb, so this is
        /// the Windows/Android build's echo).</summary>
        void Ambience(GameObject listenerHost)
        {
            var wind = Resources.Load<AudioClip>("Sfx/wind_low");
            if (wind != null)
            {
                var src = listenerHost.AddComponent<AudioSource>();
                src.clip = wind; src.loop = true; src.spatialBlend = 0f;
                src.bypassListenerEffects = true;   // the air at the listener, not a sound in the space
                src.volume = 0.22f * AudioPrefs.Effects;
                src.Play();
            }
            // the mountain's echo: AcousticSpace's own (it hears the ridges); the
            // fixed Mountains zone only where that is switched off
            if (listenerHost.GetComponent<AcousticSpace>() == null)
            {
                var zone = listenerHost.AddComponent<AudioReverbZone>();
                zone.reverbPreset = AudioReverbPreset.Mountains;
                zone.minDistance = 60f;
                zone.maxDistance = 120f;
            }
        }

        /// <summary>Traffic has no voice of its own: lend each car one loop of
        /// a racer's engine, 3D with doppler, so it comes in and goes by.</summary>
        void VoiceTraffic()
        {
            if (trafficClip == null)
            {
                foreach (var car in rm.allCars)
                {
                    if (car == null || car == rm.playerCar) continue;
                    foreach (var s in car.GetComponentsInChildren<AudioSource>())
                        if (s.loop && s.clip != null && s.clip.length > 0.3f &&
                            (trafficClip == null || s.clip.length > trafficClip.length))
                            trafficClip = s.clip;
                }
                if (trafficClip == null) return;
            }
            foreach (var tc in FindObjectsByType<TrafficContact>(FindObjectsInactive.Exclude))
            {
                var go = tc.gameObject;
                if (!voiced.Add(go)) continue;
                var src = go.AddComponent<AudioSource>();
                src.clip = trafficClip; src.loop = true;
                src.spatialBlend = 1f; src.dopplerLevel = 1.2f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 8f; src.maxDistance = 400f;
                src.pitch = Random.Range(0.55f, 0.75f);
                src.volume = 0.5f * AudioPrefs.Engine;
                src.timeSamples = Random.Range(0, Mathf.Max(1, trafficClip.samples - 1));
                src.Play();
            }
        }

        // =================== the racers ===================

        /// <summary>One finished (or long-gone) racer back upstream of the
        /// viaduct, rolling at a racing pace, when the road there is clear.</summary>
        void SendRacer()
        {
            var path = rm.path;
            int at = Mathf.Max(2, i0 - Mathf.RoundToInt(RacerUpstreamM / path.spacing));
            AIDriver pick = null;
            foreach (var car in rm.allCars)
            {
                if (car == null || car == rm.playerCar) continue;
                var ai = car.GetComponent<AIDriver>();
                if (ai == null || ai.Retired) continue;
                int idx = path.NearestIndex(car.transform.position);
                // gone by: shut down at the finish, or well past the far end
                if (ai.ShuttingDown || !ai.driving || idx > i1 + 120) { pick = ai; break; }
            }
            if (pick == null) return;

            var pos = path.GetPoint(at);
            var fwd = path.GetTangent(at).normalized;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            pos += right * 1.8f + Vector3.up * 0.6f;
            foreach (var c in Physics.OverlapSphere(pos, 9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != null && c.attachedRigidbody.gameObject != pick.gameObject) return;   // try again next time

            var carC = pick.GetComponent<CarController>();
            carC.TeleportTo(pos, Quaternion.LookRotation(fwd, Vector3.up));
            carC.SetRolling(RacerMps);
            pick.Resume();
        }

        // =================== the seasons ===================

        void NextSeason()
        {
            season = (Season)(((int)season + 1) % 4);
            RaceHandoff.CalendarDay = DayIn(season);
            RaceHandoff.TimeOfDayIndex = PickHour();
            RaceHandoff.WeatherOverride = (int)PickWeather(season);
            if (applier != null) applier.ReapplyWorld();
        }

        // =================== the frame ===================

        void LateUpdate()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!droneOn && kb != null && kb.vKey.wasPressedThisFrame && views.Count > 1 && cam != null)
            {
                view = (view + 1) % views.Count;
                ShowView();
            }
            if (Time.unscaledTime >= sweepAt)
            {
                SweepCanvases();
                sweepAt = Time.unscaledTime + 0.5f;
            }
            if (Time.unscaledTime >= trafficAudioAt)
            {
                VoiceTraffic();
                trafficAudioAt = Time.unscaledTime + 1f;
            }
            if (Time.time >= nextRacer)
            {
                SendRacer();
                nextRacer = Time.time + Random.Range(RacerEveryMinS, RacerEveryMaxS);
            }
            StepDrone();
            // the drone turns the season over at its own cuts
            if (!droneOn && atBlack == null && Time.unscaledTime >= nextSeason)
            {
                nextSeason = Time.unscaledTime + SeasonS;
                Fade(1f, FadeS, () => { NextSeason(); Fade(0f, FadeS * 1.6f, null); });
            }
            StepFade();
        }

        void Fade(float to, float seconds, System.Action whenDone)
        {
            if (fade == null) { whenDone?.Invoke(); return; }
            fadeFrom = fade.color.a; fadeTo = to; fadeT0 = Time.unscaledTime; fadeLen = seconds;
            atBlack = whenDone;
        }

        void StepFade()
        {
            if (fade == null || fadeLen <= 0f) return;
            float k = Mathf.Clamp01((Time.unscaledTime - fadeT0) / fadeLen);
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(fadeFrom, fadeTo, Mathf.SmoothStep(0f, 1f, k)));
            if (k >= 1f)
            {
                fadeLen = 0f;
                var done = atBlack;
                atBlack = null;
                done?.Invoke();
            }
        }

        // =================== the menu ===================

        void BuildUI()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "TitleCanvas", 50);

            // The menu down the LEFT, over the valley: the viaduct runs through
            // the middle and right of the frame, and a centred column of
            // buttons sat right on the deck (10-31, the postcard camera).
            MenuKit.Scanlines(canvas.transform, 0.10f);

            MenuKit.Label(canvas.transform, Application.productName.ToUpperInvariant(), 64,
                new Vector2(0f, 1f), new Vector2(48f, -56f), TextAnchor.MiddleLeft,
                MenuKit.Accent, 900f, 80f, bold: true);
            MenuKit.Label(canvas.transform, "NORTH CAROLINA  ·  1999", MenuKit.Body,
                new Vector2(0f, 1f), new Vector2(52f, -112f), TextAnchor.MiddleLeft,
                MenuKit.Dim, 600f);

            root = MenuKit.Stretch(canvas.transform, "Buttons", new Vector2(0f, 0f), new Vector2(0f, 0f),
                0f, -520f, 24f, 220f);
            BuildButtons();

            // Over everything, the menu included: the seasons fade through it.
            var f = MenuKit.Panel(canvas.transform, "Fade", Color.black);
            fade = f.GetComponent<Image>();
            fade.raycastTarget = false;
            fade.color = Color.black;
            Fade(0f, FirstFadeS, null);
            fadeT0 = Time.unscaledTime + FirstFadeAfterS;
        }

        bool HasCareer => LifeSimManager.HasSave && !string.IsNullOrEmpty(LifeSimManager.State.playerName);

        void BuildButtons()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            var items = new List<Selectable>();
            float y = 96f;
            if (HasCareer)
            {
                items.Add(MenuKit.Button(root, "CONTINUE GAME", new Vector2(0f, 0f), new Vector2(48f, y),
                    new Vector2(440f, 60f), Leave, MenuKit.Head));
                y -= 76f;
            }
            items.Add(MenuKit.Button(root, "NEW GAME", new Vector2(0f, 0f), new Vector2(48f, y),
                new Vector2(440f, 60f), () => { if (HasCareer) Confirm(); else Leave(); }, MenuKit.Head));
            Wire(items);
        }

        /// <summary>NEW GAME over a career: the same erase-and-confirm as the
        /// home screen's START OVER.</summary>
        void Confirm()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            MenuKit.Label(root, "ERASE THIS CAREER? Money, cars and rep are gone.", 18,
                new Vector2(0f, 0f), new Vector2(48f, 180f), TextAnchor.MiddleLeft, MenuKit.Bad, 520f);
            var yes = MenuKit.Button(root, "YES — START OVER", new Vector2(0f, 0f), new Vector2(48f, 120f),
                new Vector2(440f, 54f), () => { LifeSimManager.DeleteSave(); Leave(); }, 20,
                new Color(0.42f, 0.12f, 0.12f, 1f));
            var no = MenuKit.Button(root, "CANCEL", new Vector2(0f, 0f), new Vector2(48f, 56f),
                new Vector2(440f, 54f), BuildButtons, 20);
            Wire(new List<Selectable> { no, yes });
        }

        void Wire(List<Selectable> items)
        {
            MenuNav.Column(items);
            MenuNav.Watch(root.gameObject, items[0]);
            MenuNav.Select(items[0]);
        }

        /// <summary>Into the game proper: scene 0 with a clean handoff, where
        /// the home screen picks up as it always has.</summary>
        void Leave()
        {
            RaceHandoff.ClearAll();
            SceneManager.LoadScene(0);
        }

        void OnDestroy()
        {
            if (On) On = false;
        }
    }
}
