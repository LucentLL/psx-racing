using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// A scrolling list of equal-height BLOCKS that never rests on a half-cut
    /// row (coordinator, 2026-11-11, by the owner's "no text may clip" rule):
    /// the viewport is a whole number of blocks tall, a wheel notch moves one
    /// block, a drag or fling settles on a block edge, a pad/keyboard cursor
    /// that moves onto a block scrolls that whole block into view, and two
    /// cues say how many blocks are above and below.
    ///
    /// Built by <see cref="Make"/> on a viewport rect sized
    /// (shown x step - gap); the rows are parented to the returned content,
    /// block i's top at y = -i x step.
    /// </summary>
    public class BlockSnap : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        public ScrollRect scroll;
        public RectTransform content;
        public float step;
        public int count, shown;
        public Text above, below;

        bool dragging;
        GameObject lastSel;
        int focusFrames;

        /// <summary>How many whole blocks fit in <paramref name="avail"/>
        /// units, at least one, at most <paramref name="count"/>.</summary>
        public static int Fit(float avail, float step, float gap, int count) =>
            Mathf.Clamp(Mathf.FloorToInt((avail + gap) / step), 1, Mathf.Max(1, count));

        /// <summary>Turn <paramref name="view"/> (already sized to whole
        /// blocks) into the list; returns the content to draw rows into.</summary>
        public static RectTransform Make(RectTransform view, int count, int shown, float step, float gap,
                                         Text above, Text below)
        {
            var content = MenuKit.ScrollBody(view);
            content.sizeDelta = new Vector2(0f, Mathf.Max(count * step - gap, shown * step - gap));
            var sr = view.GetComponent<ScrollRect>();
            sr.scrollSensitivity = step;
            sr.inertia = false;
            var snap = view.gameObject.AddComponent<BlockSnap>();
            snap.scroll = sr;
            snap.content = content;
            snap.step = step;
            snap.count = count;
            snap.shown = shown;
            snap.above = above;
            snap.below = below;
            snap.Cues();
            return content;
        }

        public void OnBeginDrag(PointerEventData e) => dragging = true;
        public void OnEndDrag(PointerEventData e) => dragging = false;

        void LateUpdate() => Tick();

        /// <summary>One frame of the rule; public so a preview can run it.</summary>
        public void Tick()
        {
            if (scroll == null || content == null) return;
            float maxY = Mathf.Max(0f, (count - shown) * step);
            float y = content.anchoredPosition.y;

            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != lastSel)
            {
                lastSel = sel;
                focusFrames = sel != null && sel.transform.IsChildOf(content) ? 12 : 0;
            }
            if (focusFrames > 0 && sel != null)
            {
                focusFrames--;
                ShowBlockOf(sel);
            }
            else if (!dragging)
            {
                float snapped = Mathf.Clamp(Mathf.Round(y / step) * step, 0f, maxY);
                if (Mathf.Abs(snapped - y) > 0.5f) Set(snapped);
            }
            Cues();
        }

        /// <summary>Scroll so the block holding <paramref name="g"/> is wholly
        /// in view, on a block edge, moving as little as possible.</summary>
        public void ShowBlockOf(GameObject g)
        {
            if (g == null || scroll == null || content == null) return;
            float maxY = Mathf.Max(0f, (count - shown) * step);
            float y = content.anchoredPosition.y;
            int i = BlockOf(g.transform as RectTransform);
            float lo = Mathf.Max(0f, (i - shown + 1) * step), hi = Mathf.Min(maxY, i * step);
            Set(Mathf.Clamp(Mathf.Round(Mathf.Clamp(y, lo, hi) / step) * step, lo, hi));
            Cues();
        }

        /// <summary>Which block a control is in (public for the preview's
        /// pad walk).</summary>
        public int BlockIndexOf(GameObject g) => g != null ? BlockOf(g.transform as RectTransform) : -1;

        /// <summary>Is block i wholly inside the viewport now?</summary>
        public bool BlockFullyShown(int i)
        {
            float y = content != null ? content.anchoredPosition.y : 0f;
            return i * step >= y - 0.5f && (i + 1) * step <= y + shown * step + 0.5f;
        }

        int BlockOf(RectTransform rt)
        {
            // Walk up to the content's direct child: its anchored y is the
            // block's own offset from the top of the list.
            Transform t = rt;
            while (t != null && t.parent != content) t = t.parent;
            var c = t as RectTransform;
            float top = c != null ? -c.anchoredPosition.y : 0f;
            return Mathf.Clamp(Mathf.FloorToInt((top + 1f) / step), 0, Mathf.Max(0, count - 1));
        }

        void Set(float y)
        {
            scroll.StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }

        public void Cues()
        {
            int top = content != null ? Mathf.RoundToInt(content.anchoredPosition.y / Mathf.Max(1f, step)) : 0;
            int up = Mathf.Clamp(top, 0, count);
            int down = Mathf.Max(0, count - shown - up);
            if (above != null) above.text = up > 0 ? up + " MORE ABOVE" : "";
            if (below != null) below.text = down > 0 ? down + " MORE BELOW" : "";
        }
    }
}
