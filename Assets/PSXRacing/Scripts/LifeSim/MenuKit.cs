using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// Code-generated UGUI primitives for the LifeSim menus, following the
    /// pattern PauseMenu/TouchControls established: an overlay canvas at
    /// device resolution (ScaleWithScreenSize 1280x720) so text stays readable
    /// and targets stay finger-sized on phones, with PSX-era styling carried
    /// by palette and type rather than by the 320x240 render target.
    ///
    /// Lists paginate instead of scrolling — both simpler in code-built UGUI
    /// and what PS1-era menus actually did.
    /// </summary>
    public static class MenuKit
    {
        public static readonly Color Bg = new Color(0.06f, 0.04f, 0.10f, 0.97f);
        public static readonly Color PanelBg = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color BtnBg = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color BtnBgDisabled = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color Accent = new Color(1f, 0.84f, 0.40f);   // sunset gold
        public static readonly Color Good = new Color(0.55f, 1f, 0.65f);
        public static readonly Color Bad = new Color(1f, 0.45f, 0.42f);
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

        static Font font;
        public static Font Font =>
            font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static Canvas Canvas(Transform parent, string name, int sortOrder)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color? bg = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt;
            if (bg.HasValue)
            {
                var img = go.AddComponent<Image>();
                img.color = bg.Value;
                rt = img.rectTransform;
            }
            else rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Text Label(Transform parent, string text, int size,
            Vector2 anchor, Vector2 pos, TextAnchor align = TextAnchor.MiddleLeft,
            Color? color = null, float width = 560f, float height = 40f, bool bold = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
            sh.effectDistance = new Vector2(1f, -1f);
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(anchor.x, anchor.y);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(width, height);
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 anchor,
            Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick,
            int fontSize = 20, Color? bg = null)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg ?? BtnBg;
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
            colors.disabledColor = BtnBgDisabled;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var t = Label(go.transform, label, fontSize, new Vector2(0.5f, 0.5f),
                Vector2.zero, TextAnchor.MiddleCenter, Color.white, size.x, size.y, bold: true);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return btn;
        }

        /// <summary>Money with RG2's sign convention: green when the delta is
        /// income, red when it is a cost.</summary>
        public static string Money(int amount) => "$" + amount.ToString("N0");
    }
}
