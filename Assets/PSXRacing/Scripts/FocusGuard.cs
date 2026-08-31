using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Freeze the game while the browser window is not focused, and say so.
    ///
    /// Reported as: "power cut out to car just as the race started. My
    /// controller felt like it disconnected." The screenshot carried the
    /// answer — the page's own CLICK HERE TO RESUME CONTROL banner was up, so
    /// document.hasFocus() was false — and every part of that is one bug:
    ///
    ///   * THE GAMEPAD API ONLY REPORTS TO A FOCUSED DOCUMENT. Chrome freezes
    ///     every axis and button at rest the moment the window loses focus, so
    ///     a pad really does disconnect. So does the keyboard, which Unity
    ///     WebGL listens for on the canvas.
    ///   * THE GAME CARRIES ON ANYWAY. requestAnimationFrame keeps firing for
    ///     a window that is merely unfocused rather than hidden, so the race
    ///     ran, the field drove away, and the player's car sat on the grid
    ///     answering nothing. Application.runInBackground is off and does not
    ///     cover this case: it is about VISIBILITY, not focus.
    ///
    /// The page has said so at the bottom of the screen for a while, and that
    /// is not enough on its own — a notice you read after losing a race is a
    /// post-mortem. Nothing in a web page can take focus back (giving focus to
    /// a window is the user's to do), so the only honest thing left is to stop
    /// the clock until they do, which turns "I lost control and the race went
    /// on without me" into "the game paused and came back".
    ///
    /// TWO WAYS IN, because neither is guaranteed on its own: Unity's
    /// OnApplicationFocus, and <see cref="SetFocus"/>, which the WebGL
    /// template calls by name from its own blur/focus listeners. They are
    /// idempotent, so both firing costs nothing.
    ///
    /// AND TWO WAYS OUT, for the same reason. Focus coming back is one. The
    /// other is any key or click AT ALL: an input event is proof of focus,
    /// because an unfocused document does not get them — so even a browser
    /// that never fires the event cannot leave the game frozen.
    ///
    /// Only armed where there is a car to lose control OF. A front-end menu
    /// runs on unscaled time and has nothing to freeze, and covering it with
    /// a modal would be a notice about a problem the player does not have.
    /// </summary>
    public class FocusGuard : MonoBehaviour
    {
        /// <summary>The object's name IS the address: the WebGL template
        /// reaches this by SendMessage, which addresses by GameObject name.
        /// Do not rename one without the other.</summary>
        public const string ObjectName = "PSXFocus";

        /// <summary>True while the window is unfocused and the game is being
        /// held. Read by the input layer, which must not act on the frame the
        /// player clicks back in.</summary>
        public static bool Frozen { get; private set; }

        static FocusGuard instance;

        Canvas canvas;
        GameObject panel;
        float savedScale = 1f;
        bool armed;

        /// <summary>Make sure one exists. Called from PSXBootstrap, which is
        /// in every scene that has a world in it.</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FocusGuard>();
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Frozen = false;
            BuildOverlay();
            Rearm();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this) { instance = null; Frozen = false; }
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            // A scene load with the clock stopped would start the next scene
            // frozen with no notice on it, which is the failure this exists to
            // prevent wearing a different hat.
            if (Frozen) Thaw();
            Rearm();
        }

        /// <summary>
        /// Is there a world here worth holding? One lookup per scene load
        /// rather than per frame.
        ///
        /// NEVER IN THE EDITOR. The editor reports the application as
        /// unfocused whenever the Game view is not the focused panel, so
        /// clicking the Inspector mid-play would throw a full-screen PAUSED
        /// notice over the game — which is a correct reading of a signal that
        /// means something completely different there. The bug this exists for
        /// is a browser one; the guard belongs in a build.
        /// </summary>
        void Rearm() =>
            armed = !Application.isEditor && FindFirstObjectByType<PlayerCarInput>() != null;

        void OnApplicationFocus(bool has) { if (has) Thaw(); else Freeze(); }

        /// <summary>Called by name from the WebGL template's blur/focus
        /// listeners: 0 for blurred, anything else for focused. An int rather
        /// than a bool because SendMessage cannot carry one.</summary>
        public void SetFocus(int focused)
        {
            if (focused != 0) Thaw(); else Freeze();
        }

        void Freeze()
        {
            // Nothing to hold in a menu, and nothing to hold on top of a pause
            // menu that is already holding it — PauseMenu owns timeScale while
            // it is open and would have it taken away underneath it.
            if (Frozen || !armed || PauseMenu.IsOpen) return;
            savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            // And the engine with it. Audio does not run on the game clock, so
            // a frozen car would sit there droning at whatever revs it was
            // holding when the window went away — which sounds exactly like
            // the thing this is here to stop being.
            AudioListener.pause = true;
            Frozen = true;
            if (panel != null) panel.SetActive(true);
        }

        void Thaw()
        {
            if (!Frozen) return;
            Frozen = false;
            AudioListener.pause = false;
            // NOT while the pause menu has it. Coming back with Escape opens
            // the menu and thaws in the same frame, and if the menu wins the
            // race, restoring the clock here would un-pause a game the player
            // has just paused. PauseMenu puts it back itself on close.
            if (!PauseMenu.IsOpen) Time.timeScale = savedScale;
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>
        /// Whether Application.isFocused is worth listening to on this
        /// platform.
        ///
        /// It is a fallback, not the mechanism — the two focus EVENTS are —
        /// and it carries the one failure mode that would be worse than the
        /// bug: a browser that reports the page as unfocused while the player
        /// is plainly using it would freeze, thaw on their tap, and freeze
        /// again on the next frame, for ever. So the first time input arrives
        /// from a "not focused" application, that claim is retired for the
        /// session and only the events are believed. A platform that lies once
        /// lies always.
        /// </summary>
        bool pollTrusted = true;
        float unfocusedSince = -1f;

        void Update()
        {
            if (!Frozen)
            {
                // Belt and braces the other way: a window that quietly lost
                // focus without the event arriving is a race running with no
                // driver, which is the whole report. Held for half a second
                // first, so a one-frame blip during a scene load is not a
                // modal.
                if (!armed || PauseMenu.IsOpen || !pollTrusted || Application.isFocused)
                {
                    unfocusedSince = -1f;
                    return;
                }
                if (unfocusedSince < 0f) unfocusedSince = Time.unscaledTime;
                else if (Time.unscaledTime - unfocusedSince > 0.5f) Freeze();
                return;
            }

            // Focus back, by whichever route says so first.
            if (Application.isFocused) { unfocusedSince = -1f; Thaw(); return; }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var touch = Touchscreen.current;
            bool input = (kb != null && kb.anyKey.wasPressedThisFrame) ||
                         (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                            mouse.rightButton.wasPressedThisFrame)) ||
                         (touch != null && touch.primaryTouch.press.wasPressedThisFrame);
            if (!input) return;
            // Input from an application that says it is not focused. It is
            // wrong, and it will go on being wrong.
            pollTrusted = false;
            unfocusedSince = -1f;
            Thaw();
        }

        /// <summary>
        /// The notice. Its own canvas at the top of the stack, because it has
        /// to be readable over the pause menu, the HUD and the instrument
        /// cluster — all three of which are canvases with opinions about
        /// sorting order.
        /// </summary>
        void BuildOverlay()
        {
            var canvasGO = new GameObject("FocusCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9000;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGO.transform, false);
            var dim = panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = false;
            var rt = dim.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MakeLine(font, "PAUSED", 46, 40f, new Color(1f, 0.84f, 0.40f));
            MakeLine(font, "THE WINDOW LOST FOCUS — CLICK THE GAME TO CARRY ON",
                     22, -14f, Color.white);
            MakeLine(font, "a browser sends no keyboard or gamepad to a window that " +
                           "is not focused", 17, -52f, new Color(0.72f, 0.72f, 0.72f));
            panel.SetActive(false);
        }

        void MakeLine(Font font, string text, int size, float y, Color colour)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(panel.transform, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = colour;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(1100f, 60f);
        }
    }
}
