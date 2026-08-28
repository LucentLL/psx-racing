using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// Gamepad and keyboard navigation for the runtime-built menus.
    ///
    /// The menus were mouse- and touch-only, and not because the input module
    /// was missing: <c>InputSystemUIInputModule</c> assigns its default action
    /// set when it is added, so Navigate/Submit/Cancel were being generated the
    /// whole time. They had nowhere to go. UGUI routes a navigation event to the
    /// CURRENTLY SELECTED object, and nothing in this game ever selected
    /// anything — so with a pad in hand every screen was inert, which is what
    /// "menus and starting screen are not navigable with a controller" was.
    ///
    /// Two halves, and both are needed:
    ///   * an explicit navigation graph, built here from creation order rather
    ///     than from geometry. Unity's Automatic mode picks neighbours by
    ///     measuring rects, and these menus are assembled and navigated in the
    ///     SAME frame — the stale-rect trap that has broken this UI's layout
    ///     twice would pick the neighbours wrong in exactly the same way.
    ///     Buttons are created top-to-bottom and left-to-right, so hierarchy
    ///     order IS reading order, and it needs no measurement to be correct.
    ///   * <see cref="MenuNavWatch"/>, which keeps a selection alive (a mouse
    ///     click on empty space clears it, and a cleared selection is a dead
    ///     pad) and scrolls the selected row into view.
    /// </summary>
    public static class MenuNav
    {
        /// <summary>
        /// Every navigable control under <paramref name="root"/>, in creation
        /// order. Non-interactable entries are skipped: this UI disables a
        /// button by passing a null handler, and letting the cursor land on
        /// "RACED TODAY — SLEEP FIRST" would read as the pad being stuck.
        /// </summary>
        public static List<Selectable> Collect(Transform root)
        {
            var found = new List<Selectable>();
            if (root == null) return found;
            root.GetComponentsInChildren(false, found);
            for (int i = found.Count - 1; i >= 0; i--)
                if (found[i] == null || !found[i].IsInteractable()) found.RemoveAt(i);
            return found;
        }

        /// <summary>Chain a vertical list: up/down walk it, and it wraps at both
        /// ends so a long list is reachable from either direction.</summary>
        public static void Column(IList<Selectable> items, bool wrap = true)
        {
            if (items == null || items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                if (i > 0) nav.selectOnUp = items[i - 1];
                else if (wrap && items.Count > 1) nav.selectOnUp = items[items.Count - 1];
                if (i < items.Count - 1) nav.selectOnDown = items[i + 1];
                else if (wrap && items.Count > 1) nav.selectOnDown = items[0];
                items[i].navigation = nav;
            }
        }

        /// <summary>Chain a horizontal row (the tab bar). Always wraps — a tab
        /// strip that stops at the ends makes the far tab feel unreachable.</summary>
        public static void Row(IList<Selectable> items)
        {
            if (items == null || items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = items[(i - 1 + items.Count) % items.Count];
                nav.selectOnRight = items[(i + 1) % items.Count];
                items[i].navigation = nav;
            }
        }

        /// <summary>
        /// Join a tab row to the body beneath it: DOWN off any tab enters the
        /// body, UP off the body's first row returns to <paramref name="activeTab"/>
        /// — the tab you are actually on, not the first one, so the cursor comes
        /// back where it left.
        /// </summary>
        public static void Join(IList<Selectable> row, IList<Selectable> column, Selectable activeTab)
        {
            if (row == null || column == null || column.Count == 0) return;
            for (int i = 0; i < row.Count; i++)
            {
                var nav = row[i].navigation;
                nav.selectOnDown = column[0];
                row[i].navigation = nav;
            }
            var first = column[0].navigation;
            first.selectOnUp = activeTab != null ? activeTab : (row.Count > 0 ? row[0] : null);
            column[0].navigation = first;

            // Wrapping past the bottom would jump the cursor back over the whole
            // page; landing on the tab bar instead matches how the last row
            // reads on screen. Unconditional, so a single-row page can also be
            // left downwards and not only upwards.
            var last = column[column.Count - 1].navigation;
            last.selectOnDown = activeTab != null ? activeTab
                                                  : (row.Count > 0 ? row[0] : null);
            column[column.Count - 1].navigation = last;
        }

        /// <summary>Select a control, tolerating a null EventSystem (the editor
        /// preview tools build these screens with no event system at all).</summary>
        public static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        /// <summary>Attach (or reuse) the watchdog on a menu root.</summary>
        public static MenuNavWatch Watch(GameObject host, Selectable fallback)
        {
            if (host == null) return null;
            var w = host.GetComponent<MenuNavWatch>();
            if (w == null) w = host.AddComponent<MenuNavWatch>();
            w.fallback = fallback;
            return w;
        }

        /// <summary>
        /// Did the player just ask to move around a menu? Used to decide whether
        /// a lost selection should be restored — restoring it on any frame at
        /// all would fight a mouse user, who clears the selection deliberately
        /// every time they click the background.
        /// </summary>
        public static bool NavigationRequested()
        {
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                    pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
                    pad.buttonSouth.wasPressedThisFrame) return true;
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.36f) return true;
            }
            var kb = Keyboard.current;
            if (kb != null &&
                (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                 kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                 kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                return true;
            return false;
        }
    }

    /// <summary>
    /// Keeps a menu usable from a pad once it has been built: restores a lost
    /// selection, and keeps the selected control on screen.
    /// </summary>
    public class MenuNavWatch : MonoBehaviour
    {
        public Selectable fallback;

        GameObject lastSelected;

        void Update()
        {
            var es = EventSystem.current;
            if (es == null || fallback == null) return;

            var cur = es.currentSelectedGameObject;
            bool alive = cur != null && cur.activeInHierarchy;
            if (!alive && MenuNav.NavigationRequested() && fallback.IsActive())
                es.SetSelectedGameObject(fallback.gameObject);
        }

        void LateUpdate()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var cur = es.currentSelectedGameObject;
            if (cur == lastSelected) return;
            lastSelected = cur;
            if (cur == null) return;

            // Safe to measure here and only here: LateUpdate runs after the
            // layout the build frame queued, so these rects are resolved.
            var scroll = cur.GetComponentInParent<ScrollRect>();
            if (scroll != null) EnsureVisible(scroll, cur.GetComponent<RectTransform>());
        }

        /// <summary>
        /// Scroll the minimum amount that brings <paramref name="target"/> fully
        /// inside the viewport. Centring instead would make every step down a
        /// long list re-scroll the whole page, which reads as the list moving
        /// rather than the cursor.
        /// </summary>
        public static void EnsureVisible(ScrollRect scroll, RectTransform target)
        {
            if (scroll == null || target == null) return;
            var content = scroll.content;
            var viewport = scroll.viewport != null ? scroll.viewport
                                                   : scroll.GetComponent<RectTransform>();
            if (content == null || viewport == null) return;

            float viewH = viewport.rect.height;
            float scrollable = content.rect.height - viewH;
            if (scrollable <= 1f) return;

            // Distance from the content's top edge down to the target's edges.
            Vector3 centreWorld = target.TransformPoint(target.rect.center);
            Vector2 local = content.InverseTransformPoint(centreWorld);
            float contentTopLocal = content.rect.yMax;
            float top = contentTopLocal - (local.y + target.rect.height * 0.5f);
            float bottom = contentTopLocal - (local.y - target.rect.height * 0.5f);

            float current = Mathf.Clamp01(1f - scroll.verticalNormalizedPosition) * scrollable;
            const float Pad = 24f;
            float want = current;
            if (top - Pad < current) want = top - Pad;
            else if (bottom + Pad > current + viewH) want = bottom + Pad - viewH;

            want = Mathf.Clamp(want, 0f, scrollable);
            scroll.verticalNormalizedPosition = 1f - want / scrollable;
        }
    }
}
