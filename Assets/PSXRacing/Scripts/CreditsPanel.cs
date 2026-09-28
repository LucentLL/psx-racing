using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// THE CREDITS PAGE the pause menu opens (Charlotte refinement WP-02, plan
    /// critic C17): the credit and licence lines the map and terrain data owe,
    /// in full. The in-race HUD only has room for a seven-second
    /// "MAP DATA (C) OPENSTREETMAP CONTRIBUTORS", and the terrain's credit
    /// (the AWS Terrain Tiles / USGS line) had no place in the game at all.
    ///
    /// The text is Resources/psx_credits.txt, generated with LICENSES.txt
    /// (published beside index.html) from tools/city/SOURCES.md by
    /// tools/city/credits.mjs, so the page, the file on Pages and the
    /// attribution inside the data all say the same thing. Plain text,
    /// WRAPPED to the column (owner rule: no text may clip).
    ///
    /// Opened over the pause menu like the debug bench and closed the same
    /// way: BACK, Escape, B / Circle or START, and the pause menu ignores the
    /// frame this page closed on (<see cref="ClosedFrame"/>).
    /// </summary>
    public class CreditsPanel : MonoBehaviour
    {
        public System.Action onClosed;
        public bool IsOpen { get; private set; }
        /// <summary>The frame the page last closed on; see
        /// DebugCarPanel.ClosedFrame for why the pause menu needs it.</summary>
        public int ClosedFrame { get; private set; } = -1;

        const string ResourceName = "psx_credits";
        /// <summary>What the page says if the generated text is missing: the
        /// one credit ODbL cannot do without.</summary>
        const string Fallback = "MAP AND TERRAIN DATA\n\nRoad network data (c) OpenStreetMap contributors, ODbL 1.0";

        Canvas canvas;
        readonly List<MenuNavWatch> stoodDown = new List<MenuNavWatch>();

        /// <summary>The page's text: the generated credits, or the fallback.</summary>
        public static string Text()
        {
            var ta = Resources.Load<TextAsset>(ResourceName);
            string s = ta != null ? ta.text : null;
            if (ta != null) Resources.UnloadAsset(ta);
            return string.IsNullOrEmpty(s) ? Fallback : s.Replace("\r\n", "\n").TrimEnd();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MenuKit.EnsureEventSystem();
            canvas = MenuKit.Canvas(transform, "CreditsCanvas", 210);   // over the pause menu's 200
            MenuKit.Panel(canvas.transform, "Backdrop", new Color(0.08f, 0.08f, 0.08f, 0.94f));
            MenuKit.GridBackdrop(canvas.transform);
            // The pause menu's nav watch would pull the cursor back onto its
            // own rows; it stands down while this page is up.
            stoodDown.Clear();
            foreach (var w in FindObjectsByType<MenuNavWatch>(FindObjectsInactive.Exclude))
                if (w != null && w.enabled) { w.enabled = false; stoodDown.Add(w); }
            var back = Build(canvas.transform);
            MenuNav.Select(back);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            foreach (var w in stoodDown) if (w != null) w.enabled = true;
            stoodDown.Clear();
            // DestroyImmediate outside play mode: the preview tool opens the
            // page in the editor.
            if (canvas != null) { if (Application.isPlaying) Destroy(canvas.gameObject); else DestroyImmediate(canvas.gameObject); }
            canvas = null;
            onClosed?.Invoke();
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
                (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
                Close();
        }

        /// <summary>
        /// Lay the page out on <paramref name="parent"/> (a MenuKit canvas):
        /// the title, the text wrapped to a column that fits the canvas at any
        /// aspect, and BACK under it. Returns the BACK button. Public for the
        /// preview tool, which lays it out without play mode.
        /// </summary>
        public Button Build(Transform parent)
        {
            var top = new Vector2(0.5f, 1f);
            var title = MenuKit.Label(parent, "CREDITS", 34, top, new Vector2(0f, -56f),
                                      TextAnchor.MiddleCenter, MenuKit.Accent, 600f, 50f, bold: true);
            // The column: never wider than the canvas less a margin each side,
            // never wider than a comfortable reading width.
            float colW = Mathf.Min(900f, MenuKit.HalfWidth * 2f - 96f);
            float y = -104f;
            // Left-aligned text: x is the column's LEFT edge (MenuKit pivots a
            // label on its alignment), so the column is centred by starting it
            // half its width left of the middle.
            MenuKit.Para(parent, Text(), 20, top, new Vector2(-colW * 0.5f, y), out float used,
                         TextAnchor.UpperLeft, Color.white, colW);
            y -= used + 18f;
            var back = MenuKit.Button(parent, "BACK", top, new Vector2(0f, y - 22f), new Vector2(260f, 44f), Close, 22);
            back.navigation = new Navigation { mode = Navigation.Mode.None };
            return back;
        }
    }
}
