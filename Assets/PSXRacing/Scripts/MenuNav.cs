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
    /// anything — so with a pad in hand every screen was inert.
    ///
    /// Three parts, and all of them are needed:
    ///   * a CREATION-ORDER chain (<see cref="Column"/>), wired the instant a
    ///     page is built. It is never wrong about which controls exist, only
    ///     about where they are, and it is what makes the page live on the
    ///     frame it appears.
    ///   * a GEOMETRIC grid (<see cref="Grid"/>), which replaces that chain one
    ///     frame later, once the layout system has resolved the rects. Creation
    ///     order is reading order only while a page is one control per line: a
    ///     fault row is a caption and then DIY / MECH / DLR side by side, so
    ///     the chain walked DOWN through three buttons that share a line —
    ///     reported as "going sideways instead of down when I press down", and
    ///     as taking ten presses to cross a page with four faults on it.
    ///     Measuring is only safe once the rects exist, which is exactly what
    ///     <see cref="MenuNavWatch"/> waits for; measuring in the build frame is
    ///     the stale-rect trap that has broken this UI's layout twice.
    ///   * <see cref="MenuNavWatch"/> itself, which keeps a selection alive (a
    ///     mouse click on empty space clears it, and a cleared selection is a
    ///     dead pad) and scrolls the selected row into view.
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

        // ==============================================================
        //  Geometric grid
        // ==============================================================

        /// <summary>
        /// True once every control in <paramref name="items"/> has a resolved
        /// rect, i.e. the layout system has run at least once since they were
        /// created. An unresolved rect reports zero size, and grouping controls
        /// into lines by a position that is still (0,0) puts the whole page on
        /// one line — which is worse than the creation-order chain, not better.
        /// </summary>
        public static bool RectsResolved(IList<Selectable> items)
        {
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                var rt = items[i].transform as RectTransform;
                if (rt == null) return false;
                if (rt.rect.width <= 1f || rt.rect.height <= 1f) return false;
            }
            return true;
        }

        /// <summary>
        /// Split a set of controls into the LINES they actually occupy on
        /// screen, top to bottom, each ordered left to right.
        ///
        /// Two controls share a line when their centres are vertically closer
        /// than half the shorter one is tall. That tolerance is derived rather
        /// than fixed because these menus mix 40-unit repair buttons with
        /// 62-unit primary actions, and a constant that suits one splits the
        /// other in two.
        /// </summary>
        public static List<List<Selectable>> Lines(IList<Selectable> items)
        {
            var lines = new List<List<Selectable>>();
            if (items == null || items.Count == 0) return lines;

            var sorted = new List<Selectable>(items);
            sorted.RemoveAll(s => s == null || !(s.transform is RectTransform));
            // Top of the screen first. World space is safe here: these canvases
            // are axis-aligned overlays, so world Y is screen Y up to a scale.
            sorted.Sort((a, b) => CentreY(b).CompareTo(CentreY(a)));

            foreach (var s in sorted)
            {
                var line = lines.Count > 0 ? lines[lines.Count - 1] : null;
                if (line != null && SameLine(line[line.Count - 1], s)) line.Add(s);
                else lines.Add(new List<Selectable> { s });
            }
            foreach (var line in lines)
                line.Sort((a, b) => CentreX(a).CompareTo(CentreX(b)));
            return lines;
        }

        static float CentreY(Selectable s)
        {
            var rt = (RectTransform)s.transform;
            return rt.TransformPoint(rt.rect.center).y;
        }

        static float CentreX(Selectable s)
        {
            var rt = (RectTransform)s.transform;
            return rt.TransformPoint(rt.rect.center).x;
        }

        static float WorldHeight(Selectable s)
        {
            var rt = (RectTransform)s.transform;
            return Mathf.Abs(rt.rect.height * rt.lossyScale.y);
        }

        static bool SameLine(Selectable a, Selectable b)
        {
            float tol = Mathf.Max(4f, Mathf.Min(WorldHeight(a), WorldHeight(b)) * 0.5f);
            return Mathf.Abs(CentreY(a) - CentreY(b)) <= tol;
        }

        /// <summary>
        /// Wire a page as the two-dimensional thing it is: left/right walk the
        /// controls on one line, up/down step between lines and land on the
        /// control nearest the column you left from.
        ///
        /// Left/right deliberately do NOT wrap. Wrapping a three-button repair
        /// row means pressing left on DIY throws the cursor to DLR at the far
        /// side of the page, which reads as being flung rather than moved. The
        /// tab bar wraps because it is a strip you cycle; a page is not.
        /// </summary>
        public static void Grid(IList<Selectable> items)
        {
            var lines = Lines(items);
            for (int r = 0; r < lines.Count; r++)
            {
                var line = lines[r];
                for (int c = 0; c < line.Count; c++)
                {
                    float x = CentreX(line[c]);
                    var nav = new Navigation { mode = Navigation.Mode.Explicit };
                    if (c > 0) nav.selectOnLeft = line[c - 1];
                    if (c < line.Count - 1) nav.selectOnRight = line[c + 1];
                    if (r > 0) nav.selectOnUp = NearestInLine(lines[r - 1], x);
                    else if (lines.Count > 1) nav.selectOnUp = NearestInLine(lines[lines.Count - 1], x);
                    if (r < lines.Count - 1) nav.selectOnDown = NearestInLine(lines[r + 1], x);
                    else if (lines.Count > 1) nav.selectOnDown = NearestInLine(lines[0], x);
                    line[c].navigation = nav;
                }
            }
        }

        static Selectable NearestInLine(List<Selectable> line, float x)
        {
            Selectable best = null;
            float bestD = float.MaxValue;
            foreach (var s in line)
            {
                float d = Mathf.Abs(CentreX(s) - x);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// Join a tab row to the body beneath it in CREATION order: DOWN off any
        /// tab enters the body at its first control, UP off that control returns
        /// to <paramref name="activeTab"/> — the tab you are actually on, not the
        /// first one, so the cursor comes back where it left.
        ///
        /// Measures nothing, so it is safe in the frame the page is built.
        /// <see cref="JoinLines"/> replaces it once the rects exist.
        /// </summary>
        public static void Join(IList<Selectable> row, IList<Selectable> column, Selectable activeTab)
        {
            if (row == null || column == null || column.Count == 0) return;
            Selectable up = activeTab != null ? activeTab : (row.Count > 0 ? row[0] : null);
            for (int i = 0; i < row.Count; i++)
            {
                var nav = row[i].navigation;
                nav.selectOnDown = column[0];
                row[i].navigation = nav;
            }
            var first = column[0].navigation;
            first.selectOnUp = up;
            column[0].navigation = first;
            var last = column[column.Count - 1].navigation;
            last.selectOnDown = up;
            column[column.Count - 1].navigation = last;
        }

        /// <summary>
        /// The same join, done properly once the layout has resolved.
        ///
        /// Works in LINES rather than in a flat list, because a body whose top
        /// row is three buttons wide has three controls that must all lead back
        /// up to the tabs, and only one of them is <c>column[0]</c> — and
        /// because DOWN from a tab should land under that tab rather than at
        /// the left edge of the page.
        ///
        /// Never call this in the frame the page was built: it measures.
        /// </summary>
        public static void JoinLines(IList<Selectable> row, IList<Selectable> column, Selectable activeTab)
        {
            if (row == null || column == null || column.Count == 0) return;
            var lines = Lines(column);
            if (lines.Count == 0) return;
            Selectable up = activeTab != null ? activeTab : (row.Count > 0 ? row[0] : null);

            for (int i = 0; i < row.Count; i++)
            {
                var nav = row[i].navigation;
                nav.selectOnDown = NearestInLine(lines[0], CentreX(row[i]));
                row[i].navigation = nav;
            }

            foreach (var s in lines[0])
            {
                var nav = s.navigation;
                nav.selectOnUp = up;
                s.navigation = nav;
            }
            // Wrapping past the bottom would jump the cursor back over the whole
            // page; landing on the tab bar instead matches how the last row
            // reads on screen. Unconditional, so a single-line page can also be
            // left downwards and not only upwards.
            foreach (var s in lines[lines.Count - 1])
            {
                var nav = s.navigation;
                nav.selectOnDown = up;
                s.navigation = nav;
            }
        }

        /// <summary>Select a control, tolerating a null EventSystem (the editor
        /// preview tools build these screens with no event system at all).</summary>
        public static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        /// <summary>The control the cursor is on, if it is under
        /// <paramref name="root"/>. Null covers both "nothing selected" and
        /// "selected something else", which callers restoring a cursor across a
        /// rebuild treat alike.</summary>
        public static Selectable Selected(Transform root)
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return null;
            var s = es.currentSelectedGameObject.GetComponent<Selectable>();
            if (s == null) return null;
            if (root != null && !s.transform.IsChildOf(root)) return null;
            return s;
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
        /// Ask the watchdog to re-wire this page geometrically as soon as the
        /// layout system has resolved its rects.
        ///
        /// The creation-order chain the caller has already installed stays live
        /// until then, so the page is navigable on the frame it appears and
        /// simply becomes more correct one frame later.
        /// </summary>
        public static void Defer(MenuNavWatch watch, IList<Selectable> row,
                                 IList<Selectable> column, Selectable activeTab)
        {
            if (watch == null) return;
            watch.SetPending(row, column, activeTab);
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
    /// Keeps a menu usable from a pad once it has been built: re-wires the
    /// navigation graph geometrically once the rects exist, restores a lost
    /// selection, and keeps the selected control on screen.
    /// </summary>
    public class MenuNavWatch : MonoBehaviour
    {
        public Selectable fallback;

        GameObject lastSelected;

        List<Selectable> pendingRow;
        List<Selectable> pendingColumn;
        Selectable pendingActive;
        /// <summary>Frames spent waiting for the rects. A page whose controls
        /// never resolve (an inactive canvas, a preview harness that runs no
        /// layout pass at all) must not keep re-measuring for the rest of the
        /// scene.</summary>
        int waited;

        public void SetPending(IList<Selectable> row, IList<Selectable> column, Selectable activeTab)
        {
            pendingRow = row != null ? new List<Selectable>(row) : null;
            pendingColumn = column != null ? new List<Selectable>(column) : null;
            pendingActive = activeTab;
            waited = 0;
        }

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
            ApplyPending();

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
        /// Swap the creation-order chain for a graph built from where the
        /// controls actually ended up, on the first frame that is possible.
        /// </summary>
        void ApplyPending()
        {
            if (pendingColumn == null) return;
            pendingColumn.RemoveAll(s => s == null);
            if (pendingRow != null) pendingRow.RemoveAll(s => s == null);
            if (pendingColumn.Count == 0) { pendingColumn = null; pendingRow = null; return; }

            if (!MenuNav.RectsResolved(pendingColumn))
            {
                // Ten frames is a sixth of a second and far longer than the one
                // frame this normally takes; past that the page is not going to
                // resolve and the creation-order chain remains the answer.
                if (++waited > 10) { pendingColumn = null; pendingRow = null; }
                return;
            }

            MenuNav.Grid(pendingColumn);
            if (pendingRow != null && pendingRow.Count > 0)
                MenuNav.JoinLines(pendingRow, pendingColumn, pendingActive);
            pendingColumn = null;
            pendingRow = null;
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
