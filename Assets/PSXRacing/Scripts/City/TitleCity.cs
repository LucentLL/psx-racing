using System.Collections.Generic;
using PSXRacing.LifeSim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing.City
{
    /// <summary>
    /// THE TITLE SCREEN: uptown Charlotte at night, live, behind CONTINUE GAME
    /// and NEW GAME.
    ///
    /// Owner 2026-10-31, with drone footage of uptown at night: "this is how I
    /// want the start menu to look" - and then "I'm not looking for a still. I
    /// want traffic moving in menu, in the background." So it is the real city,
    /// not a picture: a cold boot loads the Charlotte scene as a free roam at
    /// NIGHT with this component on it. The player's car is parked out of sight
    /// above uptown (kinematic, no colliders, no lights, no sound) so the
    /// streaming ring and the traffic both centre on uptown, and the camera
    /// drifts round the skyline the way the footage's drone does. Everything
    /// that belongs to driving - HUD, gauges, cockpit, touch pedals, pause
    /// menu, input - is switched off; the PSX display canvas stays, since the
    /// picture itself goes through it.
    ///
    /// Either button clears the handoff and reloads scene 0, which then runs
    /// the home screen exactly as it always did (wizard, car pick or home):
    /// <see cref="Shown"/> keeps the title to once per run.
    /// </summary>
    public class TitleCity : MonoBehaviour
    {
        /// <summary>The title has had its turn this run (shown or skipped).</summary>
        public static bool Shown;
        /// <summary>Tools: never show the title (a check that drives the menu).</summary>
        public static bool Skip;
        /// <summary>The title owns this Charlotte scene.</summary>
        public static bool On { get; private set; }

        // ---- the drone ------------------------------------------------------
        // Round uptown's centre (CityMap.uptown) once every ~3 minutes, looking
        // ACROSS the skyline ~20 degrees down like the footage, ~400 m from the
        // centre so the street grid and its traffic stay inside the 500 m far
        // plane and the 5x5 tile ring built round the parked car.
        //
        // No fixed height clears uptown: at 95 m the first cut flew through
        // the tower field (a wall filling the frame), at 250 m it hung beside
        // the crown. So the eye FOLLOWS THE ROOFS: a sphere cast down at the
        // eye and a little ahead along the orbit lifts it to clear whatever
        // stands there (fast up, slow back down), over a base height.
        const float OrbitRadiusM = 380f;
        const float OrbitDegPerS = 2.0f;
        const float EyeUpM = 190f, EyeBobM = 10f, EyeBobPeriodS = 47f;
        const float LookUpM = 60f;
        const float ClearM = 45f, ClearProbeR = 40f, ClearUpS = 0.5f, ClearDownS = 4f;
        static readonly float[] ClearAheadDeg = { 0f, 6f, 12f };
        const float Fov = 52f;
        /// <summary>The parked car's height over the ground: far above
        /// CityTraffic's LevelM, so no traffic car brakes for it.</summary>
        const float ParkUpM = 60f;
        /// <summary>The traffic density's clock: early evening (~half the
        /// cap), not the night table's 2-3 a.m. trickle - the owner asked for
        /// traffic moving, and a deserted grid reads as a still.</summary>
        const float TrafficClock = 19.5f;
        const float FadeAfterS = 1.4f, FadeS = 2.2f;

        CityMode mode;
        Camera cam;
        Vector3 centre, park;
        float angle, started, sweepAt;
        float lift = -1f, liftVel;
        Image fade;
        RectTransform root, confirmRoot;
        Canvas canvas;

        /// <summary>
        /// Called by the home screen as it starts. True when the title took
        /// the boot (Charlotte is loading); false to carry on as before.
        /// </summary>
        public static bool TryBoot()
        {
            if (Shown) return false;
            Shown = true;
            if (Skip || Application.isBatchMode || DoorTour.Active || PerfTour.Active ||
                ShotLink.Active) return false;
            // Back from somewhere is never a cold boot.
            if (RaceHandoff.ResultReady || RaceHandoff.TestDrive) return false;
            if (!CarCatalog.Ready) return false;
            string spec = CityFrontEnd.DefaultCarId();
            if (string.IsNullOrEmpty(spec)) return false;
            if (!CityFrontEnd.FillFreeRoam(spec, TimeOfDay.Night, (int)Weather.Clear, out int scene))
                return false;
            RaceHandoff.TitleScreen = true;
            // Through the gate, like every other way into Charlotte: on the
            // web the city's data is its own download, and a plain LoadScene
            // before it lands opened a city with no map - no tiles, no
            // title, the free-roam HUD over an empty sky (owner, 10-31).
            SceneGate.Load(scene);
            return true;
        }

        /// <summary>CityMode.Start, before it wakes the car: take the scene
        /// over when the boot asked for the title. True means the scene is
        /// handled - the title, or (when the city cannot carry it, e.g. its
        /// data failed to download) straight on to the home screen, never a
        /// drive nobody asked for.</summary>
        public static bool Attach(CityMode m)
        {
            if (!RaceHandoff.TitleScreen) return false;
            if (m == null || m.IsRace || m.world == null || m.world.Map == null || m.player == null)
            {
                Debug.LogWarning("[TitleCity] the city cannot carry the title - on to the home screen");
                RaceHandoff.ClearAll();
                SceneManager.LoadScene(0);
                return true;
            }
            var t = m.gameObject.AddComponent<TitleCity>();
            t.mode = m;
            t.Begin();
            return true;
        }

        void Begin()
        {
            On = true;
            started = Time.unscaledTime;
            CityTraffic.ClockOverride = TrafficClock;

            var map = mode.world.Map;
            float gy = CityElevation.GroundY(map, map.uptown.x, map.uptown.y);
            centre = new Vector3(map.uptown.x, gy, map.uptown.y);
            angle = Random.Range(0f, 360f);

            park = centre + Vector3.up * ParkUpM;
            ParkCar(mode.player, park);
            Quiet();

            var chase = FindAnyObjectByType<ChaseCamera>();
            cam = chase != null ? chase.GetComponent<Camera>() : null;
            if (cam == null) cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetParent(null, true);
                cam.fieldOfView = Fov;
            }
            Place();
            BuildUI();
        }

        /// <summary>The car stays (traffic and the streaming ring centre on
        /// it) but is nothing anyone can see, hear or hit.</summary>
        static void ParkCar(CarController car, Vector3 at)
        {
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

        void LateUpdate()
        {
            angle = Mathf.Repeat(angle + OrbitDegPerS * Time.deltaTime, 360f);
            Place();
            // Kept parked: a spawn seat that runs after CityMode.Start would
            // stand the invisible car back in a lane, where traffic would
            // queue behind nothing.
            var car = mode != null ? mode.player : null;
            if (car != null && (car.transform.position - park).sqrMagnitude > 1f)
            {
                var rb = car.GetComponent<Rigidbody>();
                if (rb != null) rb.position = park;
                car.transform.position = park;
            }
            if (Time.unscaledTime >= sweepAt)
            {
                SweepCanvases();
                sweepAt = Time.unscaledTime + 0.5f;
            }
            if (fade != null)
            {
                float a = 1f - Mathf.Clamp01((Time.unscaledTime - started - FadeAfterS) / FadeS);
                fade.color = new Color(0f, 0f, 0f, a);
                if (a <= 0f) { Destroy(fade.gameObject); fade = null; }
            }
        }

        void Place()
        {
            if (cam == null) return;
            float rad = angle * Mathf.Deg2Rad;
            float bob = Mathf.Sin((Time.unscaledTime - started) * 2f * Mathf.PI / EyeBobPeriodS) * EyeBobM;
            var eye = centre + new Vector3(Mathf.Cos(rad) * OrbitRadiusM, EyeUpM + bob, Mathf.Sin(rad) * OrbitRadiusM);

            // the roofs under the eye and just ahead of it
            float need = eye.y;
            foreach (float ahead in ClearAheadDeg)
            {
                float a = rad + ahead * Mathf.Deg2Rad;
                var top = new Vector3(centre.x + Mathf.Cos(a) * OrbitRadiusM, centre.y + 900f,
                    centre.z + Mathf.Sin(a) * OrbitRadiusM);
                if (Physics.SphereCast(top, ClearProbeR, Vector3.down, out var hit, 1000f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    need = Mathf.Max(need, hit.point.y + ClearM);
            }
            float want = need - eye.y;
            if (lift < 0f) lift = want;
            lift = Mathf.SmoothDamp(lift, want, ref liftVel, want > lift ? ClearUpS : ClearDownS,
                Mathf.Infinity, Mathf.Max(Time.deltaTime, 1e-4f));
            eye.y += lift;
            cam.transform.SetPositionAndRotation(eye,
                Quaternion.LookRotation(centre + Vector3.up * LookUpM - eye, Vector3.up));
        }

        // =================== the menu ===================
        void BuildUI()
        {
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "TitleCanvas", 50);

            // A dark band low on the screen for the buttons to sit on, the
            // city left clear above it.
            MenuKit.Stretch(canvas.transform, "Band", new Vector2(0f, 0f), new Vector2(1f, 0f),
                0f, 0f, 0f, 250f, new Color(0f, 0f, 0f, 0.55f));
            MenuKit.Scanlines(canvas.transform, 0.10f);

            MenuKit.Label(canvas.transform, Application.productName.ToUpperInvariant(), 64,
                new Vector2(0.5f, 1f), new Vector2(0f, -70f), TextAnchor.MiddleCenter,
                MenuKit.Accent, 900f, 80f, bold: true);
            MenuKit.Label(canvas.transform, "CHARLOTTE  ·  1999", MenuKit.Body,
                new Vector2(0.5f, 1f), new Vector2(0f, -128f), TextAnchor.MiddleCenter,
                MenuKit.Dim, 600f);

            root = MenuKit.Stretch(canvas.transform, "Buttons", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                -240f, -240f, 30f, 220f);
            BuildButtons();

            var f = MenuKit.Panel(canvas.transform, "Fade", Color.black);
            fade = f.GetComponent<Image>();
            fade.raycastTarget = false;
        }

        bool HasCareer => LifeSimManager.HasSave && !string.IsNullOrEmpty(LifeSimManager.State.playerName);

        void BuildButtons()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            var items = new List<Selectable>();
            float y = 96f;
            if (HasCareer)
            {
                items.Add(MenuKit.Button(root, "CONTINUE GAME", new Vector2(0.5f, 0f), new Vector2(0f, y),
                    new Vector2(440f, 60f), Leave, MenuKit.Head));
                y -= 76f;
            }
            items.Add(MenuKit.Button(root, "NEW GAME", new Vector2(0.5f, 0f), new Vector2(0f, y),
                new Vector2(440f, 60f), () => { if (HasCareer) Confirm(); else Leave(); }, MenuKit.Head));
            Wire(items);
        }

        /// <summary>NEW GAME over a career: the same erase-and-confirm as the
        /// home screen's START OVER.</summary>
        void Confirm()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            MenuKit.Label(root, "ERASE THIS CAREER? Money, cars and rep are gone.", 18,
                new Vector2(0.5f, 0f), new Vector2(0f, 180f), TextAnchor.MiddleCenter, MenuKit.Bad, 480f);
            var yes = MenuKit.Button(root, "YES — START OVER", new Vector2(0.5f, 0f), new Vector2(0f, 120f),
                new Vector2(440f, 54f), () => { LifeSimManager.DeleteSave(); Leave(); }, 20,
                new Color(0.42f, 0.12f, 0.12f, 1f));
            var no = MenuKit.Button(root, "CANCEL", new Vector2(0.5f, 0f), new Vector2(0f, 56f),
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
            if (!On) return;
            On = false;
            CityTraffic.ClockOverride = -1f;
        }
    }
}
