using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// SHOW FPS: frames per second, the average frame time, and the WORST frame
    /// in the last half second, in one small line at the bottom of the screen.
    ///
    /// For the owner's "60fps minimum, preferably 120" on the phone they test
    /// on. The worst frame is the number that matters: an average of 60 made of
    /// fifty-five 14 ms frames and five 50 ms hitches is a game that stutters,
    /// and only the worst frame says so. It is the one line a playtest report
    /// needs ("Harbor Point, 72 fps, worst 31 ms").
    ///
    /// One object for the whole session, made before the first scene rather
    /// than by a scene, because the menus and the walk-in scenes have no
    /// PSXBootstrap and a readout that only exists in a race cannot compare a
    /// race to a menu. It also applies the FRAME RATE setting once for those
    /// same scenes.
    /// </summary>
    public class FpsOverlay : MonoBehaviour
    {
        const float Window = 0.5f;

        static FpsOverlay instance;

        Canvas canvas;
        Text text;
        float acc, worst;
        int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            FrameRatePrefs.Apply();
            if (instance != null) return;
            var go = new GameObject("FpsOverlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FpsOverlay>();
            instance.Build();
        }

        void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over everything, menus included: it measures them too.
            canvas.sortingOrder = 400;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = new Color(0.85f, 1f, 0.85f);
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
            sh.effectDistance = new Vector2(1f, -1f);
            // Bottom centre, on the very edge: the one place no HUD piece sits
            // (the dials are either side of the road, CONTINUE is 96 up).
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 2f);
            rt.sizeDelta = new Vector2(600f, 22f);
            canvas.enabled = FpsOverlayPrefs.Enabled;
        }

        void Update()
        {
            bool on = FpsOverlayPrefs.Enabled;
            if (canvas.enabled != on) canvas.enabled = on;
            if (!on) { acc = 0f; frames = 0; worst = 0f; return; }

            float dt = Time.unscaledDeltaTime;
            acc += dt; frames++;
            if (dt > worst) worst = dt;
            if (acc < Window) return;

            float fps = frames / acc;
            text.text = Mathf.RoundToInt(fps) + " FPS   " +
                        (acc / frames * 1000f).ToString("0.0") + " ms   worst " +
                        (worst * 1000f).ToString("0") + " ms   " +
                        (FrameRatePrefs.Max ? "MAX" : "60 CAP");
            // Green when the worst frame met 60, amber when only the average
            // did, red when neither.
            text.color = worst <= 1f / 58f ? new Color(0.55f, 1f, 0.55f)
                       : fps >= 58f ? new Color(1f, 0.8f, 0.3f)
                       : new Color(1f, 0.4f, 0.35f);
            acc = 0f; frames = 0; worst = 0f;
        }
    }
}
