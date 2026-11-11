using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.OnFoot
{
    /// <summary>
    /// A club member at the club's own meet, walked up to: what the club is,
    /// what it takes, when it meets and races - and JOIN. MeetScreen's frame
    /// (StoreScreen's overlay, Escape and pad handling, the onClosed contract
    /// that hands the walker back). A refusal is printed where the button
    /// would have been; nothing pressable refuses.
    /// </summary>
    public class ClubScreen : MonoBehaviour
    {
        public System.Action onClosed;
        public ClubDef club;
        public string alias;
        public CarSpec spec;

        public bool IsOpen { get; private set; }
        Canvas canvas;
        string joined;

        static LifeState S => LifeSimManager.State;

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
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

        void Build()
        {
            var s = S;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "ClubCanvas", 140);
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.10f, 0.10f, 0.10f, 0.90f));
            MenuKit.GridBackdrop(canvas.transform);
            MenuKit.Scanlines(canvas.transform);
            var panel = MenuKit.Stretch(canvas.transform, "Club",
                Vector2.zero, Vector2.one, 40f, 40f, 26f, -26f, MenuKit.PanelBg);
            if (s == null || club == null) { Close(); return; }

            float wide = Mathf.Min(900f, MenuKit.HalfWidth * 2f - 120f);
            float y = -14f;
            void Say(string text, int size, Color ink, float h, bool bold = false)
            {
                var l = MenuKit.Label(panel, text, size, new Vector2(0.5f, 1f), new Vector2(0f, y),
                    TextAnchor.MiddleCenter, ink, wide, height: h, bold: bold);
                MenuKit.FitOneLine(l, wide);
                y -= h + 6f;
            }

            Say("THE " + club.name, MenuKit.Head, MenuKit.Accent, 40f, true);
            Say(alias + "  ·  " + (spec != null ? Short(spec.name, 40).ToUpperInvariant() : "A CAR"),
                MenuKit.Tiny, Color.white, 24f);
            Say("\"" + Pitch(club) + "\"", MenuKit.Tiny, MenuKit.Dim, 24f);
            y -= 6f;
            Say(club.rule + " CARS ONLY", 22, MenuKit.Accent, 28f, true);
            Say("MEET " + club.MeetWhen + " AT " + CarMeets.PlaceName + "  ·  CLUB RACE " + club.RaceWhen,
                MenuKit.Tiny, Color.white, 24f);
            Say("BOTH ARE REQUIRED. MISS " + Clubs.MissesToDrop + " IN " + Clubs.MissWindowDays +
                " DAYS, OR BRING THE WRONG CAR, AND YOU ARE OUT.", MenuKit.Tiny, MenuKit.Bad, 24f);
            Say("YOU START AT #" + Clubs.BoardSize + ". PASS THE MEMBER ABOVE YOU IN A CLUB RACE TO TAKE THEIR SPOT.",
                MenuKit.Tiny, MenuKit.Dim, 24f);
            y -= 10f;

            string no = joined != null ? null : Clubs.JoinRefusal(s, club);
            if (joined != null)
            {
                Say(joined, 22, MenuKit.Good, 30f, true);
            }
            else if (no == null)
            {
                MenuKit.Button(panel, "JOIN THE " + club.shortName + " CLUB", new Vector2(0.5f, 1f),
                    new Vector2(0f, y), new Vector2(520f, 58f), Join, 21, new Color(0.20f, 0.30f, 0.24f, 1f));
                y -= 66f;
            }
            else
            {
                Say(no.ToUpperInvariant(), MenuKit.Tiny, MenuKit.Bad, 28f, true);
            }

            MenuKit.Button(panel, joined != null ? "SEE YOU AT THE RACE" : "WALK AWAY", new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(340f, 46f), Close, 18);

            var rows = MenuNav.Collect(panel);
            MenuNav.Column(rows);
            if (rows.Count > 0)
            {
                MenuNav.Select(rows[0]);
                var watch = MenuNav.Watch(gameObject, rows[0]);
                MenuNav.Defer(watch, null, rows, null);
            }
        }

        static string Pitch(ClubDef c)
        {
            switch (c.id)
            {
                case "american": return "Cubic inches and a quarter mile. That is the whole club.";
                case "japanese": return "We go up the mountain on Sunday night. Keep up or go home.";
                case "euro": return "Sunday morning, coffee, then the loops. Bring something with a pedigree.";
                case "vintage": return "If it was built after disco, it is not a classic.";
                case "future": return "Nothing older than this year. Friday night, through the city.";
                default: return "We race every week. You turn up every week.";
            }
        }

        static string Short(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";

        void Join()
        {
            var s = S;
            if (s == null || club == null) { Close(); return; }
            string line = Clubs.Join(s, club);
            if (Clubs.IsMember(s, club))
            {
                joined = line;
                LifeSimManager.Save();
            }
            if (canvas != null) Destroy(canvas.gameObject);
            Build();
        }
    }
}
