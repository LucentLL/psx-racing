using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Pause menu and live physics readout. Built at runtime on its own overlay
    /// canvas at device resolution, so the buttons stay finger-sized even though
    /// the game renders at 320x240.
    ///
    /// ESC or the MENU button opens it. There is no damage or fault system in
    /// this game, so "RESET CAR" clears the transient physics state instead:
    /// drift latch, e-brake window, wheelspin, gear, and body velocities.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public CarController playerCar;

        Canvas canvas;
        GameObject panel;
        Text debugText;
        bool open;
        bool debugOn;
        readonly StringBuilder sb = new StringBuilder(512);
        float debugTimer;

        void Start()
        {
            if (playerCar == null && RaceManager.Instance != null)
                playerCar = RaceManager.Instance.playerCar;
            BuildUI();
            SetOpen(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetOpen(!open);
            var pad = Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame) SetOpen(!open);

            if (debugOn && debugText != null)
            {
                // Unscaled: the readout must keep updating while paused, and it
                // does not need to run at full frame rate.
                debugTimer += Time.unscaledDeltaTime;
                if (debugTimer > 0.1f) { debugTimer = 0f; RefreshDebug(); }
            }
        }

        void SetOpen(bool v)
        {
            open = v;
            if (panel != null) panel.SetActive(v);
            Time.timeScale = v ? 0f : 1f;
            AudioListener.pause = v;
        }

        // ---- actions -------------------------------------------------------
        void Resume() => SetOpen(false);

        void RestartRace()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Put the car back on the racing line and clear every piece of latched
        /// physics state. This is the "clear faults" equivalent — there are no
        /// faults to clear, but a stuck drift state or a car beached off-track
        /// produces the same "something is broken" feeling.
        /// </summary>
        void ResetCar()
        {
            if (playerCar != null) RaceManager.Instance?.RespawnCar(playerCar);
            SetOpen(false);
        }

        void ToggleDebug()
        {
            debugOn = !debugOn;
            if (debugText != null) debugText.gameObject.SetActive(debugOn);
            if (debugOn) RefreshDebug();
        }

        void Quit()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
            // Browsers refuse to close a tab the user did not open by script, so
            // on WebGL this falls through and the menu simply stays up.
#endif
        }

        void RefreshDebug()
        {
            var car = playerCar;
            if (car == null) { debugText.text = "no player car"; return; }

            sb.Clear();
            sb.Append("FAULT SYSTEM: none installed\n");
            sb.Append("surface   : ").Append(car.onRoad ? "ROAD" : "OFF-ROAD (low grip)").Append('\n');
            sb.Append("grounded  : ").Append(car.anyWheelGrounded ? "yes" : "NO (airborne)").Append('\n');
            sb.Append("speed     : ").Append(Mathf.RoundToInt(car.speedKmh)).Append(" km/h  gear ")
              .Append(car.currentGear).Append("  rpm ").Append(Mathf.RoundToInt(car.currentRPM)).Append('\n');
            sb.Append("drifting  : ").Append(car.Drifting ? "YES" : "no")
              .Append("   ebrakeTimer ").Append(car.EbrakeTimer.ToString("0.00")).Append('\n');
            sb.Append("slip F/R  : ").Append((car.frontSlipAngle * Mathf.Rad2Deg).ToString("0.0"))
              .Append("deg / ").Append((car.rearSlipAngle * Mathf.Rad2Deg).ToString("0.0")).Append("deg\n");
            sb.Append("body slip : ").Append((car.chassisSlipAngle * Mathf.Rad2Deg).ToString("0.0")).Append("deg\n");
            sb.Append("wheelspin : ").Append(car.wheelspinRatio.ToString("0.00")).Append('\n');
            sb.Append("grip mult : ").Append(car.gripBonus.ToString("0.00"))
              .Append("   road mu ").Append(car.roadGrip.ToString("0.00"))
              .Append("  off mu ").Append(car.offroadGrip.ToString("0.00"));
            debugText.text = sb.ToString();
        }

        // ---- UI ------------------------------------------------------------
        void BuildUI()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            var canvasGO = new GameObject("MenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;            // above the touch controls
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Always-visible MENU button
            MakeButton(canvasGO.transform, "MENU", font, new Vector2(0f, 1f),
                       new Vector2(24f, -24f), new Vector2(120f, 62f), 20, () => SetOpen(true));

            // Dimmed modal panel
            panel = new GameObject("Panel");
            panel.transform.SetParent(canvasGO.transform, false);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.03f, 0.08f, 0.85f);
            var bgRT = bg.rectTransform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            var title = MakeText(panel.transform, "PAUSED", font, 34,
                                 new Vector2(0.5f, 1f), new Vector2(0f, -70f));
            title.fontStyle = FontStyle.Bold;

            float y = -150f;
            MakeButton(panel.transform, "RESUME", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), new Vector2(360f, 62f), 22, Resume); y -= 74f;
            MakeButton(panel.transform, "RESTART RACE", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), new Vector2(360f, 62f), 22, RestartRace); y -= 74f;
            MakeButton(panel.transform, "RESET CAR (CLEAR STATE)", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), new Vector2(360f, 62f), 19, ResetCar); y -= 74f;
            MakeButton(panel.transform, "TOGGLE DEBUG INFO", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), new Vector2(360f, 62f), 20, ToggleDebug); y -= 74f;
            MakeButton(panel.transform, "QUIT", font, new Vector2(0.5f, 1f),
                       new Vector2(0f, y), new Vector2(360f, 62f), 22, Quit);

            // Debug readout lives outside the panel so it stays up while driving
            var dbgGO = new GameObject("DebugText");
            dbgGO.transform.SetParent(canvasGO.transform, false);
            debugText = dbgGO.AddComponent<Text>();
            debugText.font = font;
            debugText.fontSize = 17;
            debugText.color = new Color(0.6f, 1f, 0.7f);
            debugText.alignment = TextAnchor.UpperLeft;
            debugText.horizontalOverflow = HorizontalWrapMode.Overflow;
            debugText.verticalOverflow = VerticalWrapMode.Overflow;
            debugText.raycastTarget = false;
            var dsh = dbgGO.AddComponent<Shadow>();
            dsh.effectColor = new Color(0f, 0f, 0f, 0.95f);
            dsh.effectDistance = new Vector2(1f, -1f);
            var dRT = debugText.rectTransform;
            dRT.anchorMin = new Vector2(0f, 1f); dRT.anchorMax = new Vector2(0f, 1f);
            dRT.pivot = new Vector2(0f, 1f);
            dRT.anchoredPosition = new Vector2(24f, -100f);
            dRT.sizeDelta = new Vector2(560f, 240f);
            dbgGO.SetActive(false);
        }

        static Text MakeText(Transform parent, string s, Font font, int size,
                             Vector2 anchor, Vector2 pos)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = s;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(420f, 50f);
            return t;
        }

        static void MakeButton(Transform parent, string label, Font font, Vector2 anchor,
                               Vector2 pos, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.16f);
            var rt = img.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.32f);
            colors.pressedColor = new Color(1f, 0.85f, 0.35f, 0.55f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var t = MakeText(go.transform, label, font, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero);
            t.fontStyle = FontStyle.Bold;
            t.rectTransform.sizeDelta = size;
        }
    }
}
