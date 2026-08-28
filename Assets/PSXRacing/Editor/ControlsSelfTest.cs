using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Headless check on the touch controls: drives real pointer events into
    /// TouchPedal and TouchWheel and asserts which way each one moves.
    ///
    /// This exists because the controls have now shipped wrong twice — a
    /// throttle that stuck on, a brake permanently at 30%, a wheel that steered
    /// the wrong way, and a handbrake that engaged upward. Every one of those
    /// compiled perfectly and looked fine in a screenshot; they are behaviours,
    /// and only exercising the behaviour catches them.
    ///
    /// Edit mode runs no lifecycle callbacks, so Awake is invoked by reflection
    /// the same way the preview tools do it.
    ///
    /// Menu: PSX Racing/Run Controls Self-Test.
    /// </summary>
    public static class ControlsSelfTest
    {
        static StringBuilder log;
        static int failures;

        [MenuItem("PSX Racing/Run Controls Self-Test")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            TestPedalDirections();
            TestDisplayIsNotInput();
            TestWheelDirection();

            Line(failures == 0 ? "CONTROLS SELF-TEST OK"
                               : "CONTROLS SELF-TEST FAILED (" + failures + ")");
            Debug.Log(log.ToString());
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_controls_log.txt"),
                log.ToString());
        }

        static void Line(string s) => log.AppendLine(s);

        static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            Line((ok ? "  ok   " : "  FAIL ") + what + (got != null ? "  (got " + got + ")" : ""));
        }

        // ---------------------------------------------------------------

        /// <summary>A pedal is pushed by dragging UP; the handbrake is a lever
        /// pulled DOWN. The two must not share a direction.</summary>
        static void TestPedalDirections()
        {
            Line("pedal direction:");

            var pedal = MakePedal(topMounted: false, height: 210f);
            Drag(pedal, fromY: 500f, toY: 605f);           // half a bar UP
            Check(Approx(pedal.Amount, 0.5f), "a pedal dragged UP engages", pedal.Amount);
            Release(pedal);
            Check(pedal.Amount == 0f, "and releases to zero", pedal.Amount);

            Drag(pedal, fromY: 500f, toY: 395f);           // half a bar DOWN
            Check(pedal.Amount == 0f, "a pedal dragged DOWN stays off", pedal.Amount);
            Release(pedal);

            var lever = MakePedal(topMounted: true, height: 132f);
            Drag(lever, fromY: 500f, toY: 434f);           // half a bar DOWN
            Check(Approx(lever.Amount, 0.5f),
                  "the e-brake lever dragged DOWN engages", lever.Amount);
            Release(lever);

            Drag(lever, fromY: 500f, toY: 566f);           // half a bar UP
            Check(lever.Amount == 0f,
                  "the e-brake lever dragged UP stays off", lever.Amount);
            Release(lever);
        }

        /// <summary>
        /// The regression that produced a permanent 30% brake: ReflectState
        /// mirrors the car's state onto the gauge for keyboard players, and while
        /// that shared a field with the touch value it fed straight back in as a
        /// real request. Display must be write-only from the car's side.
        /// </summary>
        static void TestDisplayIsNotInput()
        {
            Line("display is not input:");
            var pedal = MakePedal(topMounted: false, height: 210f);

            // Exactly what PlayerCarInput does while input is disabled on the
            // starting grid, every frame.
            for (int i = 0; i < 5; i++) pedal.SetVisualAmount(0.3f);
            Check(pedal.Amount == 0f,
                  "a mirrored 0.3 brake does NOT become a real brake request",
                  pedal.Amount);

            pedal.SetVisualAmount(0f);
            Check(pedal.Amount == 0f, "and clears cleanly", pedal.Amount);
        }

        /// <summary>
        /// The wheel measures rotation about its HUB. The hit zone is pivoted at
        /// its bottom-left corner, so taking atan2 of the raw local point put the
        /// centre of rotation 150 units down-and-left of the visible hub — which
        /// inverted the sign on the near side.
        /// </summary>
        static void TestWheelDirection()
        {
            Line("wheel direction:");
            var wheel = MakeWheel(size: 300f);

            // Recentre between cases. The wheel ACCUMULATES from wherever it
            // already is — that is the point of a rotary control, and it means a
            // second sweep from a turned wheel only unwinds it. (The first cut
            // of this test missed that and read the unwind as "does not steer
            // left"; the rim eases home in Update, which edit mode never runs.)
            wheel.SetVisualAxis(0f);

            // Grip at the top of the rim (the zone is at 0,0, 300 square, so its
            // hub is 150,150) and sweep RIGHT: a clockwise turn must steer right.
            PointerDown(wheel, new Vector2(150f, 290f));
            DragTo(wheel, new Vector2(230f, 265f));
            float right = wheel.Axis ?? 0f;
            Check(right > 0f, "sweeping the rim clockwise steers RIGHT", right);
            PointerUp(wheel);

            wheel.SetVisualAxis(0f);
            PointerDown(wheel, new Vector2(150f, 290f));
            DragTo(wheel, new Vector2(70f, 265f));
            float left = wheel.Axis ?? 0f;
            Check(left < 0f, "sweeping it anticlockwise steers LEFT", left);
            Check(Approx(Mathf.Abs(left), Mathf.Abs(right)),
                  "and by the same amount — the two directions are symmetric",
                  right + " vs " + left);
            PointerUp(wheel);

            // THE test: is this a wheel, or a horizontal slider wearing a wheel?
            //
            // The same horizontal finger movement must produce a LOT of rotation
            // at the top of the rim (moving across the circle) and almost NONE
            // at the side (moving along the radius). A linear control gives the
            // same answer in both places — which is what "it rotates based on
            // linear placement, it is fake UI" was describing, and what
            // measuring angles about the zone's corner had turned it into.
            wheel.SetVisualAxis(0f);
            PointerDown(wheel, new Vector2(150f, 280f));      // 12 o'clock
            DragTo(wheel, new Vector2(180f, 280f));           // 30px right
            float atTop = Mathf.Abs(wheel.Axis ?? 0f);
            PointerUp(wheel);

            wheel.SetVisualAxis(0f);
            PointerDown(wheel, new Vector2(270f, 150f));      // 3 o'clock
            DragTo(wheel, new Vector2(300f, 150f));           // the same 30px
            float atSide = Mathf.Abs(wheel.Axis ?? 0f);
            PointerUp(wheel);

            Check(atTop > 0.05f, "30px across the TOP of the rim turns the wheel", atTop);
            Check(atSide < 0.01f,
                  "the same 30px along the rim's SIDE barely turns it — the "
                  + "control is rotary, not a disguised horizontal slider", atSide);

            // The hub bug's signature was a sign that flipped depending on which
            // side of the rim you gripped. Grip the LEFT edge — the side nearest
            // the pivot corner, where it used to invert — and sweep clockwise.
            //
            // Clockwise at 9 o'clock means the hand goes UP (9 -> 10 -> 11), not
            // down; the first cut of this check had that backwards and read a
            // correct anticlockwise result as a failure.
            wheel.SetVisualAxis(0f);
            PointerDown(wheel, new Vector2(12f, 150f));
            DragTo(wheel, new Vector2(20f, 210f));
            float nearCorner = wheel.Axis ?? 0f;
            Check(nearCorner > 0f,
                  "clockwise on the rim's corner-side edge ALSO steers right",
                  nearCorner);
            PointerUp(wheel);

            wheel.SetVisualAxis(0f);
            PointerDown(wheel, new Vector2(12f, 150f));
            DragTo(wheel, new Vector2(20f, 90f));
            float nearCornerDown = wheel.Axis ?? 0f;
            Check(nearCornerDown < 0f,
                  "and anticlockwise there steers left — the sign follows the "
                  + "rotation, not the side of the wheel", nearCornerDown);
            PointerUp(wheel);

            // The dead zone must guard the HUB, which sits at the middle of the
            // zone — not at its pivot corner.
            Check(!TryAngle(wheel, new Vector2(150f, 150f)),
                  "the hub itself is inside the dead zone");
            Check(TryAngle(wheel, new Vector2(150f, 290f)),
                  "and the rim is not");
            Check(TryAngle(wheel, new Vector2(8f, 8f)),
                  "the pivot CORNER is not treated as the hub");
            // Sized off the WIDTH like the source (300 * 0.15 = 45), not off the
            // radius, which would have guarded only 22.5.
            Check(!TryAngle(wheel, new Vector2(150f + 40f, 150f)),
                  "40px from the hub is still dead (source guards 45, not 22.5)");
            Check(TryAngle(wheel, new Vector2(150f + 50f, 150f)),
                  "and 50px from the hub is live");
        }

        // ---------------------------------------------------------------
        // harness

        static Canvas canvas;

        static Canvas Root()
        {
            if (canvas != null) return canvas;
            var go = new GameObject("TestCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        static TouchPedal MakePedal(bool topMounted, float height)
        {
            var go = new GameObject("Pedal");
            go.transform.SetParent(Root().transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(84f, height);
            var pedal = go.AddComponent<TouchPedal>();
            pedal.topMounted = topMounted;
            Invoke(pedal, "Awake");
            return pedal;
        }

        static TouchWheel MakeWheel(float size)
        {
            var go = new GameObject("Wheel");
            go.transform.SetParent(Root().transform, false);
            var rt = go.AddComponent<RectTransform>();
            // Anchored and pivoted into the bottom-left corner, exactly as
            // TouchControls builds it — that pivot IS the bug's habitat.
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(size, size);
            var wheel = go.AddComponent<TouchWheel>();
            Invoke(wheel, "Awake");
            return wheel;
        }

        static PointerEventData Evt(Vector2 pos) =>
            new PointerEventData(EventSystem.current) { pointerId = 7, position = pos };

        static void Drag(TouchPedal p, float fromY, float toY)
        {
            p.OnPointerDown(Evt(new Vector2(40f, fromY)));
            p.OnDrag(Evt(new Vector2(40f, toY)));
        }

        static void Release(TouchPedal p) => p.OnPointerUp(Evt(Vector2.zero));

        static void PointerDown(TouchWheel w, Vector2 pos) => w.OnPointerDown(Evt(pos));
        static void DragTo(TouchWheel w, Vector2 pos) => w.OnDrag(Evt(pos));
        static void PointerUp(TouchWheel w) => w.OnPointerUp(Evt(Vector2.zero));

        static bool TryAngle(TouchWheel w, Vector2 screenPos)
        {
            var m = typeof(TouchWheel).GetMethod("TryAngle",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var args = new object[] { screenPos, 0f };
            return (bool)m.Invoke(w, args);
        }

        static void Invoke(object obj, string method) =>
            obj.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
               ?.Invoke(obj, null);

        static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.02f;
    }
}
