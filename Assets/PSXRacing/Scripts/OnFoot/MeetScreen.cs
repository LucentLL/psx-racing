using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// One driver at the car meet, called out: who they are, what they are
    /// leaning on, the race they want, what is riding on it — and RACE.
    ///
    /// The owner's brief: "Walking up to a car gives the option to challenge
    /// the racer." In RG2 that option IS the challenge — one tap on a pulsing
    /// button and you are on the strip (src/ui/hud/meetChallengeHint.ts) —
    /// because every challenge there is the same quarter mile. Here a driver
    /// names their own race, and it might be eleven kilometres of mountain on
    /// a third of a tank, so there is one page between the prompt and the
    /// start line: it says WHERE, for HOW MUCH and against WHAT before the
    /// player is committed to it. It is the pre-race page's job, done standing
    /// in a car park.
    ///
    /// A refusal is printed where the button would have been. Nothing
    /// pressable on this page refuses.
    ///
    /// Built on <see cref="WreckScreen"/>'s frame, which is
    /// <see cref="StoreScreen"/>'s: same overlay canvas, same Escape handling,
    /// same pad wiring, same onClosed contract that hands the walker back to
    /// whoever froze it.
    /// </summary>
    public class MeetScreen : MonoBehaviour
    {
        /// <summary>Called when the player walks away, so the opener can let
        /// the walker go again.</summary>
        public System.Action onClosed;
        /// <summary>The driver being talked to.</summary>
        public MeetRacer racer;
        /// <summary>The player's own car, parked somewhere in this lot. The
        /// race is reached through the front end, and the way BACK from it
        /// puts this car down where it is standing now — see
        /// <see cref="Town.TownReturn"/>.</summary>
        public CarController playerCar;

        public bool IsOpen { get; private set; }

        Canvas canvas;

        static LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            // The pointer belongs to the page while the page is up.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Build();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (canvas != null) Destroy(canvas.gameObject);
            canvas = null;
            onClosed?.Invoke();
        }

        void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
                (pad != null && pad.buttonEast.wasPressedThisFrame))
                Close();
        }

        /// <summary>What they say when you walk up. Two lines per kind of
        /// driver, picked off their name so the same driver says the same
        /// thing all night; the blacklist's own taunts for the name on the
        /// board.</summary>
        static string Line(MeetRacer r, string playerCar)
        {
            if (r.IsRival) return Blacklist.Taunt(Blacklist.ByAlias(r.rivalAlias), playerCar);
            string[] lines =
                r.style == "DRAG" ? new[] { "Straight line. You and me. Loser buys the gas.",
                                            "Anybody can turn. Let's see what it pulls." }
              : r.style == "TOUGE" ? new[] { "There is a road up the mountain. Keep up if you can.",
                                             "Guardrail on one side, nothing on the other. You in?" }
              : r.style == "STREET" ? new[] { "Through the city, no lifting. Watch my tail lights.",
                                              "Uptown, right now. The lights are just suggestions." }
              : new[] { "Anybody can go straight. Let's see you turn.",
                        "Three laps. Whoever is in front at the end was faster." };
            int pick = Mathf.Abs((r.alias ?? "").GetHashCode()) % lines.Length;
            return lines[pick];
        }

        void Build()
        {
            var s = S;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "MeetCanvas", 140);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.10f, 0.10f, 0.10f, 0.90f));
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);

            var panel = MenuKit.Stretch(canvas.transform, "Meet",
                Vector2.zero, Vector2.one, 40f, 40f, 26f, -26f, MenuKit.PanelBg);

            if (s == null || racer == null || racer.spec == null) { Close(); return; }

            var mine = s.ActiveCar;
            var track = racer.trackIndex >= 0 && racer.trackIndex < TrackCatalog.Count
                ? TrackCatalog.At(racer.trackIndex) : null;
            int purse = CarMeets.PurseFor(s, racer);
            string no = CarMeets.Refusal(s, racer);

            float y = -14f;
            MenuKit.Label(panel,
                (racer.IsRival ? "#" + racer.rivalRank + " " : "") + racer.alias,
                MenuKit.Title, new Vector2(0.5f, 1f), new Vector2(0f, y),
                TextAnchor.MiddleCenter, MenuKit.Accent, 820f, bold: true)
                .rectTransform.pivot = new Vector2(0.5f, 1f);
            y -= 50f;

            // Their car, and how good the lot says they are in it. A WORD for
            // the skill, the way condition is a word: a driver can tell
            // somebody is quick, not that they are 0.94.
            MenuKit.Label(panel,
                Short(racer.spec.name, 44).ToUpperInvariant() + "   ·   " + racer.spec.hp + " hp  " +
                racer.spec.drv + "   ·   " + racer.Reputation,
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                Color.white, 900f, height: 24f);
            y -= 32f;

            MenuKit.Label(panel, "\"" + Line(racer, LifeRules.ShortName(mine)) + "\"",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 900f, height: 24f);
            y -= 40f;

            // THE RUN and THE MONEY: the two things the player is agreeing to.
            if (track != null)
            {
                MenuKit.Label(panel, "THE RUN   ·   " + track.name, 22, new Vector2(0.5f, 1f),
                    new Vector2(0f, y), TextAnchor.MiddleCenter, MenuKit.Accent, 900f,
                    height: 28f, bold: true);
                y -= 30f;
                MenuKit.Label(panel, LifeHomeScreen.VenueSummary(track) + "   ·   one on one",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                    TextAnchor.MiddleCenter, Color.white, 900f, height: 24f);
                y -= 34f;
            }
            MenuKit.Label(panel,
                "THE MONEY   ·   " + MenuKit.Money(purse) + " if you win, nothing if you do not",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Good, 900f, height: 24f, bold: true);
            y -= 30f;

            if (mine != null && track != null)
            {
                float burn = LifeRules.RaceFuelBurnPct(track.RaceMeters, mine);
                MenuKit.Label(panel,
                    "YOU   ·   " + LifeRules.ShortName(mine) + "   ·   FUEL " +
                    Mathf.RoundToInt(mine.fuel) + "%, this burns about " +
                    Mathf.RoundToInt(burn) + "%",
                    MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y),
                    TextAnchor.MiddleCenter, MenuKit.Dim, 900f, height: 24f);
                y -= 28f;
            }
            int run = CarMeets.RunsTonight(s);
            MenuKit.Label(panel,
                run >= CarMeets.MaxRunsPerMeet ? "THAT WAS THE NIGHT'S LAST RUN"
                    : "RUN " + (run + 1) + " OF " + CarMeets.MaxRunsPerMeet +
                      " TONIGHT   ·   the meet does not cost you another block",
                MenuKit.Tiny, new Vector2(0.5f, 1f), new Vector2(0f, y), TextAnchor.MiddleCenter,
                MenuKit.Dim, 900f, height: 24f);
            y -= 40f;

            if (no == null)
            {
                MenuKit.Button(panel, "RACE " + racer.alias + "  >>", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(520f, 58f), Race, 21,
                    new Color(0.62f, 0.48f, 0.12f, 1f));
                y -= 66f;
            }
            else
            {
                MenuKit.Label(panel, no.ToUpperInvariant(), MenuKit.Tiny, new Vector2(0.5f, 1f),
                    new Vector2(0f, y), TextAnchor.MiddleCenter, MenuKit.Bad, 900f,
                    height: 28f, bold: true);
                y -= 40f;
            }

            MenuKit.Button(panel, "WALK AWAY", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(340f, 46f), Close, 18);

            // Without this the page is mouse- and touch-only: UGUI routes pad
            // navigation to the SELECTED object and nothing here would ever
            // set one.
            var rows = MenuNav.Collect(panel);
            MenuNav.Column(rows);
            if (rows.Count > 0)
            {
                MenuNav.Select(rows[0]);
                var watch = MenuNav.Watch(gameObject, rows[0]);
                MenuNav.Defer(watch, null, rows, null);
            }
        }

        static string Short(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";

        /// <summary>
        /// To the start line — through the front end, because leaving the town
        /// has to bank the drive first and scene 0 is the only place that
        /// happens (the same reason a delivery leaves by "deliverrun").
        ///
        /// Three things cross the hop: WHO was called out (CarMeets.SetPending,
        /// picked up by LifeHomeScreen's "meetrace" page id), WHERE THE CAR IS
        /// PARKED (TownReturn, so the race comes back to this lot and this
        /// stall rather than to the house), and that the leg so far is a
        /// commute — the night at the meet is one block, paid by the drive
        /// home, not one per race.
        /// </summary>
        void Race()
        {
            var s = S;
            if (s == null || racer == null) { Close(); return; }
            // Something changed between the page being drawn and the press
            // (the clock, the tank). Redraw, so the reason is where the button
            // was.
            if (CarMeets.Refusal(s, racer) != null)
            {
                if (canvas != null) Destroy(canvas.gameObject);
                Build();
                return;
            }

            CarMeets.SetPending(s, racer);
            IsOpen = false;
            Town.TownReturn.Arm(playerCar, CarMeets.PlaceName);
            Town.TownExit.GoHome(playerCar, "meetrace", commute: true);
        }
    }
}
