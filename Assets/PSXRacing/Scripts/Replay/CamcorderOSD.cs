using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// The CAMCORDER's on-screen display (see <see cref="Camcorder"/>), drawn
    /// on the race HUD's canvas (into the PSX framebuffer, on the stacked HUD
    /// camera while the camcorder draws, so it stays crisp over the soft tape)
    /// and kept inside the 4:3 picture.
    ///
    /// THE VIEWFINDER (a replay just raced): a blinking red dot and REC, the
    /// battery, SP and the tape counter across the top; the date stamp the
    /// owner asked for in the career's own date and clock ("NOV 16 1999" over
    /// "9:42 PM") at the foot, the way a stamp was burned into the tape.
    ///
    /// THE DECK (a tape at home, <see cref="Camcorder.Vcr"/>): PLAY / PAUSE /
    /// FF / REW and the deck's counter; the burned-in stamp stays, because it
    /// is on the tape.
    ///
    /// Nothing here reads a key; it only prints. Sized from the canvas it is
    /// on (a twenty-second of the picture's height), so it is the same on a
    /// 240-line picture and a 1440-line one, and raised clear of the touch
    /// replay bar when that bar is up.
    /// </summary>
    public class CamcorderOSD : MonoBehaviour
    {
        static CamcorderOSD instance;

        RectTransform root;
        Text rec, counter, mode, date, clock, help, label, sub;
        Image dot;
        RectTransform battery;
        Image[] cells;
        bool built;

        /// <summary>Show or hide the OSD on the race HUD (made on first use).</summary>
        public static void Show(bool on)
        {
            if (!on)
            {
                // A tape at home keeps the deck's OSD whatever the camera.
                if (instance != null && !Camcorder.Vcr) instance.gameObject.SetActive(false);
                return;
            }
            if (instance == null)
            {
                var hud = Object.FindFirstObjectByType<RaceHUD>();
                if (hud == null) return;
                var go = new GameObject("CamcorderOSD", typeof(RectTransform));
                go.transform.SetParent(hud.transform, false);
                instance = go.AddComponent<CamcorderOSD>();
            }
            instance.gameObject.SetActive(true);
        }

        /// <summary>For the tools: the OSD's texts this frame.</summary>
        public static string Describe() => instance == null || !instance.isActiveAndEnabled ? "" :
            instance.label.text + (instance.sub.text.Length > 0 ? " " + instance.sub.text : "") + " | " +
            instance.mode.text + " | " + instance.rec.text + " | " + instance.counter.text + " | " +
            instance.clock.text + " | " + instance.date.text;

        void Awake()
        {
            relabelled = -1;   // a new scene, a new touch bar
            root = (RectTransform)transform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rec = MakeText("Rec", font, TextAnchor.UpperLeft);
            mode = MakeText("Mode", font, TextAnchor.UpperLeft);
            counter = MakeText("Counter", font, TextAnchor.UpperRight);
            clock = MakeText("Clock", font, TextAnchor.LowerLeft);
            date = MakeText("Date", font, TextAnchor.LowerLeft);
            help = MakeText("Help", font, TextAnchor.LowerCenter);
            // The clip's name (round 2): START, CHECKPOINT 3/9, FINISH.
            label = MakeText("Clip", font, TextAnchor.UpperLeft);
            sub = MakeText("ClipSub", font, TextAnchor.UpperLeft);
            help.color = new Color(1f, 0.85f, 0.35f);
            dot = MakeImage("Dot", root, new Color(0.95f, 0.1f, 0.08f));
            dot.sprite = Circle();
            battery = MakeImage("Battery", root, new Color(0, 0, 0, 0)).rectTransform;
            // The battery: an outline, a nub, three cells.
            var edge = MakeImage("Edge", battery, Color.white);
            edge.type = Image.Type.Simple;
            var inner = MakeImage("Inner", battery, new Color(0.05f, 0.05f, 0.05f, 0.85f));
            var nub = MakeImage("Nub", battery, Color.white);
            cells = new Image[3];
            for (int i = 0; i < 3; i++) cells[i] = MakeImage("Cell" + i, battery, Color.white);
            Stretch(edge.rectTransform, 0f, 0f, 0.88f, 1f);
            Stretch(inner.rectTransform, 0.06f, 0.16f, 0.82f, 0.84f);
            Stretch(nub.rectTransform, 0.88f, 0.3f, 1f, 0.7f);
            for (int i = 0; i < 3; i++)
                Stretch(cells[i].rectTransform, 0.11f + i * 0.24f, 0.24f, 0.11f + i * 0.24f + 0.2f, 0.76f);
            built = true;
            foreach (var g in GetComponentsInChildren<Graphic>(true)) { g.raycastTarget = false; HudOnTop.Apply(g.gameObject); }
        }

        static Sprite circle;
        /// <summary>The REC lamp's round dot, drawn once (no built-in sprite
        /// is loadable in a player).</summary>
        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int S = 32;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[S * S];
            float c = (S - 1) * 0.5f, r = S * 0.5f - 1f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(r - d + 0.5f)));
                }
            t.SetPixels32(px);
            t.Apply();
            circle = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            circle.hideFlags = HideFlags.DontSave;
            return circle;
        }

        Text MakeText(string name, Font font, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.75f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            return t;
        }

        static Image MakeImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var im = go.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            var rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            return im;
        }

        static void Stretch(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rt, float x, float y, float w, float h, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = pivot;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        void LateUpdate()
        {
            if (!built) return;
            var rp = RaceReplay.Instance;
            if (rp == null || !RaceReplay.Playing || (!Camcorder.Vcr && rp.Mode != RaceReplay.CamMode.Camcorder))
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            // The 4:3 picture inside the canvas.
            var r = root.rect;
            float W = r.width, H = r.height;
            float pw = Mathf.Min(W, H * 4f / 3f), ph = Mathf.Min(H, W * 3f / 4f);
            float x0 = (W - pw) * 0.5f, y0 = (H - ph) * 0.5f;
            int fs = Mathf.Max(8, Mathf.RoundToInt(ph / 21f));
            float mx = pw * 0.07f, my = ph * 0.07f;
            bool touch = TouchControls.Instance != null && TouchControls.Instance.Visible;
            float foot = y0 + ph * (touch ? 0.17f : 0.08f);
            if (touch) RelabelTouch();

            foreach (var t in new[] { rec, counter, mode, date, clock, label }) t.fontSize = fs;
            sub.fontSize = Mathf.Max(8, Mathf.RoundToInt(fs * 0.8f));
            help.fontSize = Mathf.Max(8, Mathf.RoundToInt(fs * 0.6f));

            float clockHours = Camcorder.StampClock + rp.ReplayTime / 3600f;
            date.text = Camcorder.DateStamp(Camcorder.StampDay);
            clock.text = Camcorder.TimeStamp(clockHours);
            Place(date.rectTransform, x0 + mx, foot, pw * 0.6f, fs * 1.3f, Vector2.zero);
            Place(clock.rectTransform, x0 + mx, foot + fs * 1.25f, pw * 0.6f, fs * 1.3f, Vector2.zero);

            float top = y0 + ph - my;
            // The clip's name on the second line, left; a circuit's lap under it.
            label.text = Camcorder.ClipLabel ?? "";
            sub.text = Camcorder.ClipSub ?? "";
            label.color = sub.color = Camcorder.Vcr ? new Color(0.55f, 1f, 0.6f) : Color.white;
            Place(label.rectTransform, x0 + mx, top - fs * 1.3f, pw * 0.7f, fs * 1.3f, new Vector2(0f, 1f));
            Place(sub.rectTransform, x0 + mx, top - fs * 2.55f, pw * 0.7f, fs * 1.1f, new Vector2(0f, 1f));
            float blink = Mathf.Repeat(Time.unscaledTime, 1f);
            if (Camcorder.Vcr)
            {
                // The deck's own words.
                string m = rp.Paused ? "PAUSE  II" : rp.Rate > 1.01f ? "FF  >>" : rp.Rate < -0.01f ? "REW  <<" : "PLAY  >";
                mode.text = m;
                mode.color = new Color(0.55f, 1f, 0.6f);
                rec.text = "";
                dot.enabled = false;
                battery.gameObject.SetActive(false);
                Place(mode.rectTransform, x0 + mx, top, pw * 0.5f, fs * 1.3f, new Vector2(0f, 1f));
                counter.text = Camcorder.Counter(rp.ReplayTime);
                counter.color = new Color(0.55f, 1f, 0.6f);
                Place(counter.rectTransform, x0 + pw - mx, top, pw * 0.4f, fs * 1.3f, new Vector2(1f, 1f));
            }
            else
            {
                mode.text = "";
                rec.text = "REC";
                rec.color = Color.white;
                dot.enabled = blink < 0.6f;
                float d = fs * 0.62f;
                Place(dot.rectTransform, x0 + mx, top - fs * 0.62f, d, d, new Vector2(0f, 0.5f));
                Place(rec.rectTransform, x0 + mx + d * 1.5f, top, pw * 0.3f, fs * 1.3f, new Vector2(0f, 1f));
                battery.gameObject.SetActive(true);
                float bw = fs * 1.9f, bh = fs * 0.9f;
                Place(battery, x0 + pw - mx, top - fs * 0.62f, bw, bh, new Vector2(1f, 0.5f));
                // The pack runs down over a long tape, and the last cell blinks.
                // A pack lasts the best part of an hour: a cell per quarter hour.
                int lit = Mathf.Clamp(3 - Mathf.FloorToInt(rp.ReplayTime / 900f), 1, 3);
                for (int i = 0; i < 3; i++) cells[i].enabled = i < lit && !(lit == 1 && blink > 0.5f);
                counter.text = "SP  " + Camcorder.Counter(rp.ReplayTime);
                counter.color = Color.white;
                Place(counter.rectTransform, x0 + pw - mx - bw - fs * 0.6f, top, pw * 0.5f, fs * 1.3f, new Vector2(1f, 1f));
            }

            // The controls, under the picture (or under the stamp when the
            // picture fills the height), never on the touch bar.
            help.text = touch ? "" : HowTo();
            Place(help.rectTransform, W * 0.5f, Mathf.Max(2f, y0 * 0.5f - fs * 0.3f), W * 0.9f, fs, new Vector2(0.5f, 0f));
        }

        static string HowTo()
        {
            bool pad = UnityEngine.InputSystem.Gamepad.current != null;
            if (Camcorder.Vcr)
                return pad ? "A PLAY/PAUSE   D-PAD REW / FF   B EJECT" : "SPACE PLAY/PAUSE   ARROWS REW / FF   ESC EJECT";
            return pad ? "A PAUSE   D-PAD SKIP / CAR   Y CAMERA   B EXIT" : "SPACE PAUSE   ARROWS SKIP / CAR   C CAMERA   ESC EXIT";
        }

        /// <summary>The touch replay bar's skip buttons scan on the deck: say so
        /// (TouchControls' own labels, put back for a race's replay).</summary>
        static int relabelled = -1;
        static void RelabelTouch()
        {
            int want = Camcorder.Vcr ? 1 : 0;
            if (relabelled == want) return;
            var bar = GameObject.Find("ReplayBar");
            if (bar == null) return;
            SetLabel(bar.transform, "Replay0", want == 1 ? "<< REW" : "<< 10s");
            SetLabel(bar.transform, "Replay2", want == 1 ? "FF >>" : "10s >>");
            relabelled = want;
        }

        static void SetLabel(Transform bar, string child, string text)
        {
            var b = bar.Find(child);
            var t = b != null ? b.GetComponentInChildren<Text>(true) : null;
            if (t != null) t.text = text;
        }

        bool visible = true;
        void SetVisible(bool on)
        {
            if (visible == on) return;
            visible = on;
            foreach (Transform c in root) c.gameObject.SetActive(on);
        }
    }
}
