using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Updates the UGUI HUD: speed, gear, RPM bar, lap, times, position,
    /// countdown and results.
    ///
    /// Every text field is change-gated. Assigning Text.text rebuilds the mesh
    /// and allocates, and most of these values change once a lap — rebuilding
    /// all seven strings every frame was the single largest managed allocation
    /// in the game.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        public CarController car;
        /// <summary>The analog dials, which own the speed and the gear. The
        /// HUD used to print both in a corner as well, and a number in the
        /// corner and a number on the instrument are the same number — printing
        /// it twice is how a HUD ends up with a readout that disagrees with its
        /// own gauge. The dials themselves no longer print either: the needle
        /// IS the speedometer, and the gear has its own small panel beside the
        /// tach when nothing else on screen is showing it.</summary>
        public GaugeCluster cluster;
        public Text lapText;
        public Text timeText;
        public Text lastLapText;
        public Text posText;
        public Text centerText;
        /// <summary>Flashes the name of the camera view for a moment after it
        /// changes. Six views are worth having only if the player can tell
        /// which one they just switched to.</summary>
        public Text camText;

        /// <summary>
        /// WRONG WAY, under the stuck watchdog and over everything else.
        ///
        /// Under it because being wedged on your roof is the more urgent news
        /// and the two can be true at once; over the pump and the venue prompts
        /// because none of those matter while you are driving at the field.
        /// The AI have had this test since P2 and act on it silently; the
        /// player gets told and left to steer.
        /// </summary>
        string WrongWayPrompt() =>
            stuck != null && stuck.WrongWay ? "WRONG WAY" : null;

        /// <summary>The player car's stuck watchdog. Its prompt takes over the
        /// centre banner while racing — the banner is empty then anyway, and a
        /// car that cannot move is the only thing worth saying at that
        /// moment.</summary>
        public StuckRecovery stuck;

        /// <summary>The live tank. Optional — a scene built before fuel existed
        /// simply has no gauge.</summary>
        public FuelTank tank;
        /// <summary>Level text beside the bar: "FUEL 62%".</summary>
        public Text fuelText;
        /// <summary>The bar itself. Emptied by shrinking the rect from the left
        /// edge, so it needs no sliced sprite and no fill mode.</summary>
        public RectTransform fuelFill;
        /// <summary>Full width of the fill, in framebuffer pixels. Set by the
        /// builder alongside the rect, because reading sizeDelta back on the
        /// frame the rect was created is the stale-read trap the menus already
        /// learned about the hard way.</summary>
        public float fuelFillWidth = 46f;

        static readonly Color FuelOk = new Color(0.55f, 0.95f, 0.6f);
        static readonly Color FuelLow = new Color(1f, 0.78f, 0.25f);
        static readonly Color FuelOut = new Color(1f, 0.35f, 0.3f);

        int lastFuelPct = int.MinValue;

        /// <summary>Set by RaceHandoffApplier from the car's faults. A dead
        /// cluster blanks speed/gear/tach; a failing tach wanders.</summary>
        public bool hideGauges;
        public bool rpmFlutter;

        int lastLap = int.MinValue;
        int lastPos = int.MinValue;
        int lastTimeCentis = int.MinValue;
        float lastBest = -1f;
        string lastCenter;
        string lastCam;
        /// <summary>Whether the player has changed view yet this race. Until
        /// they do, the flash carries the control that changes it — after that
        /// they plainly know, and repeating it would be nagging.</summary>
        bool camHintUsed;
        ChaseCamera.View lastCamView;

        /// <summary>How long the camera name stays up after a switch. Long
        /// enough to read at a glance, short enough not to become chrome.</summary>
        const float CamFlashSeconds = 2.4f;

        /// <summary>The OpenStreetMap attribution ODbL requires wherever the
        /// road data is shown, and how long it holds its slot. The city has
        /// always shown it; a stage is the same map data with elevation under
        /// it, so it shows the same line for the same seven seconds.</summary>
        const string OsmAttribution = "MAP DATA (C) OPENSTREETMAP CONTRIBUTORS";
        const float AttributionSeconds = 7f;
        /// <summary>Whether this race is on baked map data. Read once off the
        /// handoff's venue: the HUD serves every scene and a circuit owes
        /// nobody an attribution.</summary>
        bool? stageVenue;
        bool lastAttribution;

        /// <summary>Invariant culture: on a machine with a comma decimal
        /// separator the lap clock would otherwise read 1'23,456.</summary>
        static string FormatTime(float t)
        {
            if (t <= 0f) return "--'--\"---";
            int m = (int)(t / 60f);
            float s = t - m * 60;
            return string.Format(CultureInfo.InvariantCulture, "{0}'{1:00.000}", m, s)
                         .Replace(".", "\"");
        }

        static void Set(Text field, string value)
        {
            if (field != null && field.text != value) field.text = value;
        }

        /// <summary>
        /// The free-roam HUD: the lap slot carries the street name (which is
        /// what Midnight Club printed there, and the single most useful line
        /// in a real city), the clock carries the session, the position slot
        /// says what mode this is, and the centre banner keeps its watchdog
        /// and dry-tank duties. The first seconds also carry the OSM
        /// attribution the road data legally requires.
        /// </summary>
        void UpdateCity(City.CityMode city)
        {
            if (cluster != null)
            {
                cluster.hideGauges = hideGauges;
                cluster.rpmFlutter = rpmFlutter;
            }

            // The venue's own name, not a literal: this same HUD serves the
            // town, and a street called CHARLOTTE two hundred miles from
            // Charlotte was in the first screenshot the owner sent back.
            string street = city.CurrentStreet;
            Set(lapText, string.IsNullOrEmpty(street) ? city.VenueName : street);

            int centis = Mathf.FloorToInt(city.SessionSeconds * 100f);
            if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(city.SessionSeconds)); }

            Set(posText, "FREE ROAM");
            // The attribution has its seven seconds, then the slot becomes the
            // signpost. Ten restaurants over 2,574 km of road behind a 360 m
            // fog wall are findable only by accident otherwise — the question
            // that prompted this was literally "where are they?". The town has
            // no attribution and no food index; its errand cue rides the same
            // slot instead — where the shift wants you, with the same arrow.
            Set(lastLapText, city.SessionSeconds < AttributionSeconds && world != null && world.Map != null
                ? OsmAttribution
                : Town.TownWorld.Cue ?? FoodCue());

            UpdateFuel(ownsActionButton: false);

            if (lastCamView != ChaseCamera.Current) { lastCamView = ChaseCamera.Current; camHintUsed = true; }
            string cam = "";
            if (Time.unscaledTime - ChaseCamera.ChangedAt < CamFlashSeconds)
            {
                cam = ChaseCamera.ViewNames[(int)ChaseCamera.Current];
                if (!camHintUsed) cam += "   " + CameraHowTo();
            }
            if (cam != lastCam) { lastCam = cam; Set(camText, cam); }

            string center = !city.Live ? city.VenueName
                : (stuck != null ? stuck.Prompt : null)
                  // No WrongWayPrompt here on purpose: UpdateCity only runs
                  // when there is no RaceManager, and the wrong-way test needs
                  // that manager's path, so it is always false on this branch.
                  ?? GasPump.Prompt
                  // THE VENUE BEFORE THE DOOR HANDLE. ForecourtMode used to win
                  // this chain and suppressed itself by reading TownVenue's
                  // AtVenue — a static written in the SAME frame by a component
                  // with no execution order against it, so on the frame the car
                  // stops the banner can read "GET OUT AND WALK" over a junction
                  // that is offering a menu. Ordering the chain by specificity
                  // makes it true regardless of who ran first.
                  ?? Town.TownVenue.Prompt
                  ?? OnFoot.ForecourtMode.Prompt
                  ?? Town.TownEdge.Prompt
                  ?? DriveThru.Prompt
                  ?? DryTankPrompt()
                  ?? "";
            if (center != lastCenter) { lastCenter = center; Set(centerText, center); }

            // ONE DECISION PER FRAME FOR THE ONE CONTEXTUAL BUTTON A PHONE HAS.
            //
            // This used to be a RECLAIM: UpdateFuel hid the button for a nozzle
            // that does not exist out here, and this chain took it back twenty
            // lines later. Both calls land inside the same Update, so the
            // button's GameObject was SetActive(false) and SetActive(true)
            // again on every rendered frame the player spent in free roam —
            // and a UI object deactivated under a finger has its press
            // cancelled. Whatever else that costs, it is not something to leave
            // in the path of the only button that opens a menu.
            //
            // Ordered by who is PROMPTING, most specific first, and it always
            // ends by clearing: a chain with no else leaves a stale label on
            // screen from the last thing that wanted it.
            var cityTouch = TouchControls.Instance;
            if (cityTouch != null)
            {
                if (GasPump.AtPump && GasPump.Prompt != null)
                    cityTouch.SetAction(true, "FUEL");
                else if (DriveThru.AtBay) cityTouch.SetAction(true, "ORDER");
                else if (Town.TownVenue.AtVenue) cityTouch.SetAction(true, "OPEN");
                // No edge branch: a zone line opens its menu on crossing and
                // never asks to be pressed (TownEdge.AtEdge is always false).
                else if (OnFoot.ForecourtMode.OfferGetOut)
                    cityTouch.SetAction(true, "GET OUT");
                else cityTouch.SetAction(false);
            }
        }


        /// <summary>
        /// Which way to the nearest drive-thru, and how far. An eight-point
        /// arrow relative to where the CAR IS POINTING rather than a compass
        /// bearing, because a player mid-corner can act on "over your left
        /// shoulder" and cannot act on "north-north-east".
        ///
        /// Refreshed on a timer rather than per frame: it is a string built by
        /// concatenation on a screen whose whole design is change-gated, and
        /// the answer moves by a metre a frame.
        /// </summary>
        static readonly string[] FoodArrows =
            { "^", "/^", ">", "\v", "v", "v/", "<", "^\\" };

        float foodNext;
        string foodLine = "";

        string FoodCue()
        {
            if (world == null || car == null) return "";
            if (Time.unscaledTime < foodNext) return foodLine;
            foodNext = Time.unscaledTime + 0.4f;

            if (!world.NearestFood(car.transform.position, out string label,
                                   out Vector2 at, out float dist))
            { foodLine = ""; return foodLine; }

            // Standing in the car park already: the order prompt says the rest.
            if (dist < 30f) { foodLine = label; return foodLine; }

            Vector3 to = new Vector3(at.x - car.transform.position.x, 0f,
                                     at.y - car.transform.position.z);
            float rel = Vector3.SignedAngle(
                new Vector3(car.transform.forward.x, 0f, car.transform.forward.z), to, Vector3.up);
            int oct = Mathf.RoundToInt(Mathf.Repeat(rel, 360f) / 45f) % 8;

            string range = dist >= 1000f
                ? (dist / 1000f).ToString("0.0") + " km"
                : Mathf.RoundToInt(dist / 10f) * 10 + " m";
            foodLine = FoodArrows[oct] + "  " + label + "  " + range;
            return foodLine;
        }

        /// <summary>The city world, for the attribution gate above. Wired by
        /// the city scene builder; null on every circuit.</summary>
        public City.CityWorld world;

        /// <summary>
        /// Name the control the player actually HAS. On a phone there is no C
        /// key, and telling someone to press one on a device without a keyboard
        /// reads as the game not knowing what it is running on — the same
        /// mistake the finish banner used to make about RESTART.
        /// </summary>
        static string CameraHowTo()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible)
                return "(MENU → CAMERA)";
            return UnityEngine.InputSystem.Gamepad.current != null
                ? "(TRIANGLE / Y)" : "(PRESS C)";
        }

        // =================== the race map ===================
        //
        // Gran Turismo's corner map: the circuit as a white line with the
        // field on it, drawn from the same centreline the road was built
        // from (TrackCatalog.Thumbnail) and placed with the same projection,
        // so the dot is on the road because the road is where the dot's
        // maths says it is. Built at runtime rather than by the scene
        // builder so it reaches every venue without a rebake and can follow
        // the framebuffer's line count, which the player can change.

        /// <summary>Map height as a fraction of the frame height. About the
        /// size the reference draws it.</summary>
        public float mapFrac = 0.30f;
        /// <summary>Where the map's centre sits up the left edge, as a
        /// fraction of the frame height. Above the tach on a desktop layout
        /// and above the touch wheel on a phone, under the lap counter on
        /// both.</summary>
        public float mapCentreYFrac = 0.60f;

        GameObject mapRoot;
        RectTransform playerDot;
        readonly System.Collections.Generic.List<RectTransform> rivalDots =
            new System.Collections.Generic.List<RectTransform>();
        TrackCatalog.MapFrame mapFrame;
        bool mapValid;
        int mapBuiltPx = -1;
        static readonly Color PlayerDotColor = new Color(1f, 0.28f, 0.22f);
        // SOLID, AND NOT WHITE. The road is drawn as a white line, and the
        // first cut put the rivals on it as 92% white with no edge - on the
        // line they vanished, off it they read as a smudge, and the owner
        // called them "white or transparent", which is what they were. Green
        // with a dark edge is what the reference game used for the field.
        static readonly Color RivalDotColor = new Color(0.30f, 0.95f, 0.40f);
        static readonly Color RivalEdgeColor = new Color(0.05f, 0.12f, 0.06f);
        /// <summary>
        /// Dot sizes as a fraction of the map's width. They were 0.07 and
        /// 0.045 - on a HUD drawn at device resolution that is a 25 px
        /// player marker on a track line two pixels wide, a marker wider than
        /// the road it is on. The reference draws its dots at about a
        /// thirtieth of the map; these are a shade over that so they survive
        /// the framebuffer's dither, and the outline is fixed at a pixel.
        /// </summary>
        const float PlayerDotFrac = 0.034f, RivalDotFrac = 0.027f;
        int RivalDotPx() => Mathf.Max(2, Mathf.RoundToInt(mapBuiltPx * RivalDotFrac));

        int FrameHeight()
        {
            var rt = transform as RectTransform;
            float h = rt != null ? rt.rect.height : 0f;
            return h < 32f ? 240 : Mathf.RoundToInt(h);
        }

        /// <summary>The venue this race is on. The handoff's index when the
        /// race came from the LifeSim; the scene's own name otherwise, so an
        /// editor race on Beech Gap does not draw the city circuit's map.</summary>
        internal static TrackCatalog.TrackDef VenueDef()
        {
            if (RaceHandoff.FromLifeSim) return TrackCatalog.At(RaceHandoff.TrackIndex);
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            foreach (var t in TrackCatalog.Scened) if (t.id == scene) return t;
            return TrackCatalog.At(RaceHandoff.TrackIndex);
        }

        void EnsureMap()
        {
            int px = Mathf.Max(24, Mathf.RoundToInt(FrameHeight() * mapFrac));
            if (mapRoot != null && px == mapBuiltPx) return;
            if (mapRoot != null)
            {
                if (Application.isPlaying) Destroy(mapRoot); else DestroyImmediate(mapRoot);
            }
            rivalDots.Clear();
            mapBuiltPx = px;

            var def = VenueDef();
            mapValid = TrackCatalog.MapFrameFor(def, px, out mapFrame);
            if (!mapValid) return;
            var tex = TrackCatalog.Thumbnail(def, px, hud: true);

            mapRoot = new GameObject("TrackMap", typeof(RectTransform));
            mapRoot.transform.SetParent(transform, false);
            var rt = (RectTransform)mapRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, mapCentreYFrac);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(4f, 0f);
            rt.sizeDelta = new Vector2(px, px);

            var imgGO = new GameObject("Road", typeof(RectTransform));
            imgGO.transform.SetParent(mapRoot.transform, false);
            var img = imgGO.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero;

            int dot = Mathf.Max(3, Mathf.RoundToInt(px * PlayerDotFrac));
            playerDot = MakeDot(mapRoot.transform, dot, PlayerDotColor, Color.white);
            HudOnTop.Apply(mapRoot);
        }

        /// <summary>A filled square of <paramref name="size"/> pixels with a
        /// one-pixel edge in <paramref name="edge"/> around it. Every marker
        /// has an edge: a dot with no edge disappears the moment it crosses
        /// a line of its own colour.</summary>
        static RectTransform MakeDot(Transform parent, int size, Color color, Color edge)
        {
            var go = new GameObject("Dot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size + 2, size + 2);
            var img = go.AddComponent<Image>();
            img.color = edge;
            img.raycastTarget = false;
            var inner = new GameObject("Fill", typeof(RectTransform));
            inner.transform.SetParent(go.transform, false);
            var ir = (RectTransform)inner.transform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 0.5f);
            ir.pivot = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(size, size);
            var ii = inner.AddComponent<Image>();
            ii.color = color;
            ii.raycastTarget = false;
            return rt;
        }

        /// <summary>Build the map and put every car in the scene on it,
        /// outside play mode — for the screenshot tool, which otherwise
        /// photographs a HUD with no map because nothing calls Update.</summary>
        public void PreviewMap()
        {
            EnsureMap();
            if (!mapValid || mapRoot == null) return;
            mapRoot.SetActive(true);
            int rivals = 0;
            foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
            {
                RectTransform dot;
                if (c == car) dot = playerDot;
                else
                {
                    if (rivals >= rivalDots.Count)
                        rivalDots.Add(MakeDot(mapRoot.transform, RivalDotPx(), RivalDotColor, RivalEdgeColor));
                    dot = rivalDots[rivals++];
                }
                if (dot == null) continue;
                Vector3 p = c.transform.position;
                dot.anchoredPosition = new Vector2(mapFrame.X(p.x), mapFrame.Y(p.z));
            }
            if (playerDot != null) playerDot.SetAsLastSibling();
            HudOnTop.Apply(mapRoot);
        }

        void UpdateMap(RaceManager rm)
        {
            EnsureMap();
            if (!mapValid || mapRoot == null) return;
            if (!mapRoot.activeSelf) mapRoot.SetActive(true);

            int rivals = 0;
            foreach (var c in rm.allCars)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                RectTransform dot;
                if (c == car) dot = playerDot;
                else
                {
                    if (rivals >= rivalDots.Count)
                        rivalDots.Add(MakeDot(mapRoot.transform, RivalDotPx(), RivalDotColor, RivalEdgeColor));
                    dot = rivalDots[rivals++];
                    // Under the player's, always: the dot that matters is the
                    // one drawn last.
                    dot.SetAsFirstSibling();
                }
                if (dot == null) continue;
                Vector3 p = c.transform.position;
                dot.anchoredPosition = new Vector2(mapFrame.X(p.x), mapFrame.Y(p.z));
            }
            if (playerDot != null) playerDot.SetAsLastSibling();
            for (int i = rivals; i < rivalDots.Count; i++)
                if (rivalDots[i].gameObject.activeSelf) rivalDots[i].gameObject.SetActive(false);
            for (int i = 0; i < rivals; i++)
                if (!rivalDots[i].gameObject.activeSelf) rivalDots[i].gameObject.SetActive(true);
        }

        // =================== the replay readout ===================
        //
        // While the replay plays the HUD is the spectator's: the lap, clock
        // and position of the car being watched at the moment being watched,
        // REPLAY and the car's name in the corner the reference put them in,
        // and none of the driver's things — no fuel, no map, no cluster.

        bool replayWasOn;
        Text replayText, replayCarText;

        Text MakeHudText(string name, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
            sh.effectDistance = new Vector2(1f, -1f);
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(200f, 30f);
            HudOnTop.Apply(go);
            return t;
        }

        void OnReplayToggled(bool on)
        {
            if (on)
            {
                if (replayText == null)
                {
                    replayText = MakeHudText("Replay", new Vector2(0f, 0f), new Vector2(10f, 30f), 12, TextAnchor.MiddleLeft);
                    replayCarText = MakeHudText("ReplayCar", new Vector2(0f, 0f), new Vector2(10f, 16f), 10, TextAnchor.MiddleLeft);
                    replayCarText.color = new Color(0.85f, 0.85f, 0.85f);
                }
                replayText.gameObject.SetActive(true);
                replayCarText.gameObject.SetActive(true);
                if (fuelFill != null && fuelFill.parent != null) fuelFill.parent.gameObject.SetActive(false);
                Set(fuelText, "");
                if (mapRoot != null) mapRoot.SetActive(false);
                var touch = TouchControls.Instance;
                if (touch != null) { touch.SetAction(false); touch.SetContinue(true); }
            }
            else
            {
                if (replayText != null) replayText.gameObject.SetActive(false);
                if (replayCarText != null) replayCarText.gameObject.SetActive(false);
                if (fuelFill != null && fuelFill.parent != null) fuelFill.parent.gameObject.SetActive(true);
                // Every change-gate reset, so the race's own readouts repaint.
                lastLap = int.MinValue; lastPos = int.MinValue;
                lastTimeCentis = int.MinValue; lastBest = -1f;
                lastCenter = null; lastCam = null; lastTipLine = null;
                lastFuelPct = int.MinValue;
                lastFuelColor = new Color(-1f, -1f, -1f, -1f);
            }
        }

        void UpdateReplay(RaceManager rm)
        {
            var rp = RaceReplay.Instance;
            if (rp == null) return;
            var f = rp.FocusFrame;
            bool ends = rm.path != null && rm.path.HasEnds;
            Set(lapText, ends ? rm.path.dragLabel : "LAP " + Mathf.Max(1, (int)f.lap) + "/" + rm.totalLaps);
            int centis = Mathf.FloorToInt(rp.ReplayTime * 100f);
            if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(rp.ReplayTime)); }
            Set(lastLapText, "");
            Set(posText, RaceHandoff.Delivery ? "" : "POS " + Mathf.Max(1, (int)f.place) + "/" + rm.allCars.Count);
            Set(centerText, rp.Paused ? "PAUSED" : "");
            Set(replayText, "REPLAY");
            Set(replayCarText, rp.FocusName);
            string cam = Time.unscaledTime - rp.ModeChangedAt < CamFlashSeconds
                ? RaceReplay.CamNames[(int)rp.Mode]
                : ReplayHowTo();
            Set(camText, cam);
        }

        /// <summary>The replay's controls, named for the device in hand.</summary>
        static string ReplayHowTo()
        {
            if (TouchControls.Instance != null && TouchControls.Instance.Visible) return "";
            return UnityEngine.InputSystem.Gamepad.current != null
                ? "A PLAY/PAUSE   D-PAD SKIP / CAR   Y CAMERA   B EXIT"
                : "SPACE PLAY/PAUSE   ARROWS SKIP / CAR   C CAMERA   ESC EXIT";
        }

        /// <summary>The line under the results that offers the replay.</summary>
        static string ReplayOffer(bool touch)
        {
            var rp = RaceReplay.Instance;
            if (rp == null || !rp.Available) return "";
            if (touch) return "\nTAP REPLAY TO WATCH IT BACK";
            return UnityEngine.InputSystem.Gamepad.current != null
                ? "\nX / SQUARE FOR REPLAY" : "\nV FOR REPLAY";
        }

        void Awake() => HudOnTop.Apply(gameObject);

        /// <summary>Whether the last frame was drawn for somebody on foot.
        /// Drives the one-shot blank/restore below.</summary>
        bool lastFoot;

        /// <summary>
        /// A driver's HUD for a player who is not driving is noise — the lap
        /// clock, the fuel bar and the mode banner were all still printing over
        /// the walk-around view, reported as "while walking, I still see race
        /// car UI on screen". Blank the lot on the way out of the car and
        /// reset every change-gate on the way back in, so the fields repaint
        /// with live values rather than trusting stale ones.
        /// </summary>
        bool OnFootNow()
        {
            bool foot = OnFoot.ForecourtMode.OnFoot;
            if (foot == lastFoot) return foot;
            lastFoot = foot;
            if (foot)
            {
                Set(lapText, ""); Set(timeText, ""); Set(lastLapText, "");
                Set(posText, ""); Set(centerText, ""); Set(camText, "");
                Set(fuelText, "");
                if (mapRoot != null) mapRoot.SetActive(false);
                if (fuelFill != null && fuelFill.parent != null)
                    fuelFill.parent.gameObject.SetActive(false);
                var touch = TouchControls.Instance;
                if (touch != null) { touch.SetAction(false, ""); touch.SetContinue(false); }
            }
            else
            {
                if (fuelFill != null && fuelFill.parent != null)
                    fuelFill.parent.gameObject.SetActive(true);
                lastLap = int.MinValue; lastPos = int.MinValue;
                lastTimeCentis = int.MinValue; lastBest = -1f;
                lastCenter = null; lastCam = null; lastTipLine = null;
                lastFuelPct = int.MinValue;
                lastFuelColor = new Color(-1f, -1f, -1f, -1f);
            }
            return foot;
        }

        void Update()
        {
            var rm = RaceManager.Instance;
            if (car == null) return;
            if (OnFootNow()) return;
            if (rm == null)
            {
                // No RaceManager means Charlotte: same canvas, no laps to count.
                var city = City.CityMode.Instance;
                if (city != null) UpdateCity(city);
                return;
            }
            var p = rm.GetProgress(car);

            bool replaying = RaceReplay.Playing;
            if (replaying != replayWasOn) { replayWasOn = replaying; OnReplayToggled(replaying); }
            if (replaying) { UpdateReplay(rm); return; }

            // A dead instrument cluster ($350 to fix) blanks the readouts rather
            // than hiding the widgets: the player should see that the gauges are
            // broken, not that the HUD is missing.
            if (cluster != null)
            {
                cluster.hideGauges = hideGauges;
                cluster.rpmFlutter = rpmFlutter;
            }

            bool drag = rm.path != null && rm.path.drag;
            bool ends = rm.path != null && rm.path.HasEnds;

            if (p != null)
            {
                // A strip has no laps to count, so the slot names the distance
                // instead — "LAP 1/1" on a quarter mile is a readout that tells
                // the player nothing they did not already know. A stage names
                // its run the same way.
                if (RaceHandoff.Delivery && (ends || rm.Sprint))
                {
                    // A delivery is a SPRINT to a door: the slot says how far
                    // the door is rather than a lap count that never reaches
                    // 2 — and on a stage it replaces the run's name for the
                    // same reason. The range is a float that moves every
                    // frame, so it is timer-gated and rounded before it is
                    // allowed anywhere near Text.text.
                    UpdateDropRange(p, rm);
                }
                else
                {
                    int lap = ends ? 1 : Mathf.Min(p.lap, rm.totalLaps);
                    if (lap != lastLap)
                    {
                        lastLap = lap;
                        Set(lapText, ends ? rm.path.dragLabel : "LAP " + lap + "/" + rm.totalLaps);
                    }
                }

                // The clock only needs redrawing when a hundredth ticks over.
                int centis = Mathf.FloorToInt(p.raceTime * 100f);
                if (centis != lastTimeCentis) { lastTimeCentis = centis; Set(timeText, FormatTime(p.raceTime)); }

                // On a stage the BEST slot carries the map attribution for its
                // first seconds — it has nothing to say before the first lap
                // anyway — and hands the slot back by resetting the change-
                // gate, so it repaints on the next frame rather than waiting
                // for a best lap that a point-to-point run never sets.
                if (stageVenue == null) stageVenue = TrackCatalog.At(RaceHandoff.TrackIndex).stage;
                bool attribution = stageVenue.Value && p.raceTime < AttributionSeconds;
                if (attribution != lastAttribution) { lastAttribution = attribution; lastBest = -1f; }
                if (attribution)
                    Set(lastLapText, OsmAttribution);
                else if (!Mathf.Approximately(p.bestLapTime, lastBest))
                {
                    lastBest = p.bestLapTime;
                    Set(lastLapText, p.bestLapTime > 0f ? "BEST " + FormatTime(p.bestLapTime) : "");
                }
            }

            int pos = rm.GetPosition(car);
            if (RaceHandoff.Delivery)
            {
                // A delivery has no field, so "POS 1/1" is a readout that tells
                // the player nothing — the slot carries the tip instead. It has
                // to be here and it has to be LIVE: the whole job is now graded
                // on the clock and on the state of the box, and a grade the
                // player only learns about on the results screen is not a rule
                // they can drive to, it is a surprise. Watching it fall as you
                // run late, and drop a band the moment you hit something, is the
                // mechanic.
                UpdateDeliveryTip(p);
            }
            else if (pos != lastPos) { lastPos = pos; Set(posText, "POS " + pos + "/" + rm.allCars.Count); }

            UpdateMap(rm);
            UpdateFuel();

            // Unscaled: the pause menu freezes time, and a view switched just
            // before pausing should not have its label frozen on screen with it.
            if (lastCamView != ChaseCamera.Current) { lastCamView = ChaseCamera.Current; camHintUsed = true; }
            string cam = "";
            if (Time.unscaledTime - ChaseCamera.ChangedAt < CamFlashSeconds)
            {
                cam = ChaseCamera.ViewNames[(int)ChaseCamera.Current];
                if (!camHintUsed) cam += "   " + CameraHowTo();
            }
            if (cam != lastCam) { lastCam = cam; Set(camText, cam); }

            string center = null;
            switch (rm.State)
            {
                case RaceManager.RaceState.Countdown:
                    float remaining = rm.CountdownRemaining - 1f;
                    center = remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "GO!";
                    break;
                case RaceManager.RaceState.Racing:
                    // Priority, most urgent first: the lights, then the stuck
                    // watchdog, then the nozzle, then a dry tank.
                    //
                    // The watchdog is above the pump because it now only speaks
                    // when the car is on its ROOF or pinned against a wall — it
                    // stands down for a car merely parked on a forecourt. When
                    // it does speak on a forecourt, it is because the player
                    // rolled it there, and "HOLD F TO FUEL" over the top of the
                    // only instructions for getting out is the game answering a
                    // question nobody asked.
                    // No "GO!" on a rolling start: the car was already going
                    // when the scene opened. The countdown is zero there so
                    // this is only a guard, but it is the guard that keeps a
                    // banner off a screen the player is already driving on.
                    center = rm.CountdownRemaining > 0f && !rm.RollingStart ? "GO!"
                           : (stuck != null ? stuck.Prompt : null)
                             ?? WrongWayPrompt()
                             ?? GasPump.Prompt
                             ?? OnFoot.ForecourtMode.Prompt
                             ?? Town.TownVenue.Prompt
                             ?? Town.TownEdge.Prompt
                             ?? DriveThru.Prompt
                             ?? DryTankPrompt()
                             ?? "";
                    break;
                case RaceManager.RaceState.Finished:
                    // A blacklist challenge is about the name, not the position:
                    // the ladder headline goes first and the timing sheet after.
                    string ladder = RaceHandoff.RivalRank > 0
                        ? "#" + RaceHandoff.RivalRank + " " + RaceHandoff.RivalAlias +
                          (pos == 1 ? " DEFEATED\n" : " KEEPS THE SPOT\n")
                        : "";
                    // Name the control the player actually HAS. On a phone there
                    // is no R key, and telling someone to press one on a device
                    // without a keyboard reads as the game not knowing what it
                    // is running on. The CONTINUE button appears at the bottom of
                    // a touch screen for exactly this moment.
                    bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
                    string how = touch ? "TAP CONTINUE"

                               : UnityEngine.InputSystem.Gamepad.current != null ? "PRESS A / CROSS"
                               : "PRESS R";
                    // A drag result is an ET and a trap speed. Reporting a "best
                    // lap" for a single 402 m run would be the circuit's answer
                    // to a question the strip did not ask. A stage result is an
                    // ET too — but a trap speed on a mountain finish line is
                    // drag talk, so the stage sheet is the time alone. A sprint
                    // round part of a circuit is one run as well: its ET is the
                    // result, and "BEST" of one lap nobody completed is blank.
                    string sheet = ends || rm.Sprint
                        ? "\nET " + FormatTime(p != null ? p.finishTime : 0f) +
                          (drag ? "   TRAP " + Mathf.RoundToInt(SpeedUnits.FromKmh(
                                      p != null ? p.trapSpeedKmh : 0f)) + SpeedUnits.Suffix : "")
                        : "\nBEST " + FormatTime(p != null ? p.bestLapTime : 0f);
                    // A delivery is not a race result. Reporting "FINISH! P1" for
                    // a solo run to a customer's door would be the circuit's
                    // answer to a question the job did not ask; what the player
                    // wants at that moment is whether the tip survived.
                    string head = RaceHandoff.Delivery
                        ? DeliverySheet(p != null ? p.finishTime : 0f)
                        : ladder + "FINISH!  P" + pos + sheet;
                    center = head +
                             "\n\n" + how +
                             (RaceHandoff.FromLifeSim ? " TO GO HOME" : " TO RESTART") +
                             ReplayOffer(touch);
                    break;
            }
            if (center != lastCenter) { lastCenter = center; Set(centerText, center); }
        }

        // =================== the delivery readout ===================
        //
        // Both halves below go through LifeRules.ScoreDelivery, which is also
        // what the apply-back pays from. That is deliberate and it is the whole
        // point of the function existing: a HUD that counts down its own idea of
        // the tip and a wallet that grants a different one is worse than showing
        // nothing, because the player would learn to distrust the number and
        // then the mechanic is invisible again.

        string lastTipLine;
        /// <summary>Rebuilt on a timer rather than per frame. It is a string
        /// built out of two floats and every assignment to Text.text rebuilds
        /// the mesh — the same reason every other field on this HUD is
        /// change-gated. Four times a second is faster than a tip actually
        /// moves.</summary>
        float nextTipAt;
        const float TipRefreshSeconds = 0.25f;

        /// <summary>Live damage off the car itself. The stamped
        /// RaceHandoff.DamageScore does not exist until the run ends, and the
        /// point of this readout is to react the instant the player hits
        /// something.</summary>
        CollisionResponder responder;
        bool responderChecked;

        /// <summary>The last DROP range painted, in the units it was painted
        /// at — whole tens of metres under a kilometre, whole hundreds over —
        /// so the slot repaints only when a digit would.</summary>
        int lastDropM = int.MinValue;
        float nextDropAt;

        /// <summary>
        /// "DROP 640 m" / "DROP 1.2 km" in the lap slot: how far the door is.
        /// Off RaceManager.RemainingToFinishM, which is the same progress the
        /// standings use. Same cadence as the tip beside it (TipRefreshSeconds)
        /// and the same formatter as the city's food cue, for the same reason:
        /// a range is read at a glance, and tens of metres is the resolution a
        /// driver can act on.
        /// </summary>
        void UpdateDropRange(RaceManager.CarProgress p, RaceManager rm)
        {
            if (lapText == null) return;
            if (Time.unscaledTime < nextDropAt) return;
            nextDropAt = Time.unscaledTime + TipRefreshSeconds;

            float m = Mathf.Max(0f, rm.RemainingToFinishM(p));
            int key = m >= 1000f ? Mathf.RoundToInt(m / 100f) * 100
                                 : Mathf.RoundToInt(m / 10f) * 10;
            if (key == lastDropM) return;
            lastDropM = key;
            Set(lapText, "DROP " + (key >= 1000
                ? (key / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
                : key + " m"));
        }

        void UpdateDeliveryTip(RaceManager.CarProgress p)
        {
            if (posText == null) return;
            if (Time.unscaledTime < nextTipAt) return;
            nextTipAt = Time.unscaledTime + TipRefreshSeconds;

            if (!responderChecked)
            {
                responder = car != null ? car.GetComponent<CollisionResponder>() : null;
                responderChecked = true;
            }

            var drop = LiveDrop(p != null ? p.raceTime : 0f);
            // Short. This is the top-right corner of a 240-line framebuffer at
            // 12 px, and the slot it inherited held "POS 1/4". The box only
            // earns a mention once it has stopped being intact — a state line
            // that is showing the good news every second of every clean run is
            // chrome, and it would push the number that matters off the edge.
            //
            // IT NEVER SAYS "REFUSED" WHILE THE RUN IS GOING. Refusal is the
            // customer's decision, made at a door the player has not reached
            // yet: reported as "it says the pizza is refused before I finished
            // delivery — this is not possible to know until attempting to
            // deliver". What the driver CAN see is the state of the box, so
            // that is what this says. The tip beside it already falls to zero,
            // which is the honest half of the same news.
            string line = "TIP $" + drop.tip +
                (drop.condition >= LifeSim.LifeRules.PizzaPerfectCondition ? ""
                 : "  " + LifeSim.LifeRules.PizzaConditionLabel(drop.condition));
            if (line != lastTipLine) { lastTipLine = line; Set(posText, line); }

        }

        /// <summary>
        /// Score the drop as it stands.
        ///
        /// Off the LIVE responder while the run is going, because
        /// RaceHandoff.DamageScore does not exist until the finish and the point
        /// of the readout is to react the instant the player hits something —
        /// but off the STAMPED numbers once the result is in. RaceManager kills
        /// input at the line and the car keeps rolling; a delivery that coasted
        /// into a barrier on its slowing-down lap would otherwise show a result
        /// screen worse than the one the wallet is about to pay from, which is
        /// the one direction this readout must never drift.
        /// </summary>
        LifeSim.LifeRules.DeliveryOutcome LiveDrop(float seconds)
        {
            bool stamped = RaceHandoff.ResultReady;
            // The live cargo while the run is going, the stamped value after —
            // same rule as the damage tally below it, and for the same reason:
            // input dies at the line but the car keeps rolling, and a box that
            // slid off on the slowing-down lap must not make the results screen
            // read worse than the wallet.
            float? cargo = stamped
                ? (RaceHandoff.CargoReported ? RaceHandoff.CargoCondition : (float?)null)
                : (PizzaCargo.Instance != null && PizzaCargo.Instance.BoxCount > 0
                       ? PizzaCargo.Instance.Condition : (float?)null);
            return LifeSim.LifeRules.ScoreDelivery(
                RaceHandoff.DeliveryPay, RaceHandoff.TrackIndex, seconds,
                stamped ? RaceHandoff.DamageScore
                        : (responder != null ? responder.DamageScore : 0f),
                stamped ? RaceHandoff.HardHits
                        : (responder != null ? responder.HardHits : 0),
                inProgress: !stamped, cargoCondition: cargo,
                carryCondition: RaceHandoff.CarryCondition);
        }

        string DeliverySheet(float finishTime)
        {
            if (!responderChecked)
            {
                responder = car != null ? car.GetComponent<CollisionResponder>() : null;
                responderChecked = true;
            }
            var drop = LiveDrop(finishTime);
            string clock = LifeSim.LifeRules.DeliveryClock(drop.seconds) +
                           "  (PAR " + LifeSim.LifeRules.DeliveryClock(drop.parSeconds) + ")";
            if (drop.refused)
                return "REFUSED\nthe box was a write-off\n" + clock + "\nNO TIP";
            return "DELIVERED\n" + clock +
                   "\nBOX " + LifeSim.LifeRules.PizzaConditionLabel(drop.condition) +
                   "\nTIP  " + LifeSim.MenuKit.Money(drop.tip);
        }

        Color lastFuelColor = new Color(-1f, -1f, -1f, -1f);

        /// <summary>
        /// The fuel gauge, and the contextual touch button that goes with it.
        ///
        /// This one readout lives on the 240-line HUD canvas rather than in the
        /// device-resolution cluster, and the split is the project's own: the
        /// CABIN — what you read under a needle and what you hold — is at device
        /// resolution, and the RACE DATA printed over the world stays at 240
        /// lines. How much fuel is left is race data. It is also the only number
        /// on screen that can end the race on its own, which is why it gets a
        /// bar rather than a line of text: a bar is read at a glance mid-corner
        /// and a percentage is not.
        /// </summary>
        /// <param name="ownsActionButton">False on the free-roam path, which
        /// arbitrates the ACTION button itself against the pump, the order
        /// window, the venues and the door handle. Passing true from two places
        /// that both write the same button every frame is how it ended up being
        /// toggled off and on once per frame.</param>
        void UpdateFuel(bool ownsActionButton = true)
        {
            if (tank != null)
            {
                float pct = Mathf.Clamp(tank.percent, 0f, 100f);

                if (fuelFill != null)
                {
                    float w = fuelFillWidth * pct * 0.01f;
                    var size = fuelFill.sizeDelta;
                    if (!Mathf.Approximately(size.x, w))
                        fuelFill.sizeDelta = new Vector2(w, size.y);

                    Color want = tank.Empty ? FuelOut : tank.Low ? FuelLow : FuelOk;
                    if (want != lastFuelColor)
                    {
                        lastFuelColor = want;
                        var img = fuelFill.GetComponent<Image>();
                        if (img != null) img.color = want;
                        if (fuelText != null) fuelText.color = want;
                    }
                }

                // Ceil, not round: a tank with anything at all in it must not
                // read as 0%, because 0% is the number that means the engine
                // has stopped.
                int shown = tank.Empty ? 0 : Mathf.Max(1, Mathf.CeilToInt(pct));
                if (shown != lastFuelPct)
                {
                    lastFuelPct = shown;
                    Set(fuelText, "FUEL " + shown + "%");
                }
            }

            // The FUEL button only exists while a nozzle is offering itself, and
            // CONTINUE only once the race is over — the two contextual controls
            // that replaced the permanent CAM and RESET pair.
            var touch = TouchControls.Instance;
            if (touch != null)
            {
                bool over = RaceManager.Instance != null &&
                            RaceManager.Instance.State == RaceManager.RaceState.Finished;
                if (ownsActionButton)
                {
                    // The one contextual button offers the replay once the
                    // race is over and there is one to offer.
                    bool replayOffer = over && RaceReplay.Instance != null && RaceReplay.Instance.Available;
                    if (replayOffer) touch.SetAction(true, "REPLAY");
                    else touch.SetAction(!over && GasPump.AtPump && GasPump.Prompt != null, "FUEL");
                }
                touch.SetContinue(over);
            }
        }

        /// <summary>
        /// What to say to a player whose engine just stopped.
        ///
        /// It has to name the way OUT, not just the problem. A dry tank is the
        /// one state in the game the car cannot drive itself out of, so the
        /// banner points at the pause menu, where the fuel truck is.
        /// </summary>
        string DryTankPrompt()
        {
            if (tank == null || !tank.Empty) return null;
            string how = TouchControls.Instance != null && TouchControls.Instance.Visible
                ? "TAP MENU (TOP LEFT)"
                : UnityEngine.InputSystem.Gamepad.current != null ? "PRESS START" : "PRESS ESC";
            return "OUT OF FUEL\n" + how + " TO CALL THE FUEL TRUCK";
        }
    }
}
