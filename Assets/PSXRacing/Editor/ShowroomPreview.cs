using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE SHOWROOM's pictures and probes (owner, 2026-11-15: car viewing and
    /// selection like the early GT games, the car turning). Every showroom page
    /// at the owner's 2000x1077 window, 16:9, 4:3, a wide phone and a 640x360
    /// phone (LifeHomePreview.Shoot: the CLIP report and the pad-reach check
    /// come with each), plus:
    ///   * control overlaps per shot (NavScrollPreview.ReportControlOverlap);
    ///   * TURN: the idle spin runs, a drag through the stage's own handler
    ///     turns the car and coasts, the spin holds for 3 s after the release
    ///     and then comes back, and the right stick (a virtual pad) turns it;
    ///   * BROWSE: LEFT / RIGHT on the stage's arrows step the car and keep the
    ///     cursor; DOWN leaves the arrows for the page's buttons;
    ///   * PSX_SHOWROOM=0 (Showroom.On off): the car page and MY CARS as before.
    /// Writes Screenshots/showroom/. "[Showroom] DONE, n FAIL(s)" at the end.
    /// </summary>
    public static class ShowroomPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("owner_2000x1077", 2000, 1077),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
            ("phone_wide", 1998, 891),
            ("phone_640x360", 640, 360),
        };
        static readonly (string name, int w, int h)[] Two = { Sizes[0], Sizes[3] };

        static int fails;
        static void Fail(string s) { fails++; Debug.LogError("[Showroom] FAIL " + s); }

        [MenuItem("PSX Racing/Preview Showroom")]
        public static void Capture()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "showroom");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            fails = 0;
            bool keepOn = Showroom.On;
            try
            {
                Showroom.On = true;
                LifeSimManager.DeleteSave();
                LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
                // NEW GAME, WHAT ARE YOU DRIVING?: the lanes on the stage.
                LifeHomePreview.Shoot(outDir, "sr_carpick", sizes: Sizes, probe: (s, w) => Probe(s, w, "CarPick"));

                LifeRules.SeedFallbackCar(LifeSimManager.State);
                var st = LifeSimManager.State;
                foreach (var (id, cond, miles) in new[]
                {
                    ("toyota_mr2_1600_g__86", 62, 91000f), ("alpine_a310_1600ve__73", 48, 63000f),
                    ("subaru_impreza_sedan_wrx_sti__gc___94", 81, 38000f), ("honda_civic_sir_ii__eg___91", 55, 120000f),
                })
                {
                    var spec = CarCatalog.Get(id);
                    if (spec == null) { Debug.LogWarning("[Showroom] no catalog row " + id); continue; }
                    CarMarket.MakeOwnedCar(st, spec, cond, miles, spec.price);
                }
                var active = st.ActiveCar ?? st.cars[0];
                foreach (var fid in new[] { "alignment", "lamp_out", "radiator_leak" })
                {
                    var f = FaultCatalog.MakeById(fid, null);
                    if (f == null) continue;
                    f.hidden = false; f.diagnosed = true;
                    active.faults.Add(f);
                }
                LifeSimManager.Save();
                Debug.Log("[Showroom] " + st.cars.Count + " cars, active " + active.displayName);

                LifeHomePreview.Shoot(outDir, "sr_mycars", "garage", sizes: Sizes, probe: (s, w) => Probe(s, w, "Body"));
                LifeHomePreview.Shoot(outDir, "sr_mycars_turn", "garage", sizes: new[] { Sizes[0] }, probe: TurnProbe);
                LifeHomePreview.Shoot(outDir, "sr_mycars_car3", "garage", sizes: Two,
                    fields: new[] { ("garageShowId", (object)st.cars[Mathf.Min(2, st.cars.Count - 1)].id) },
                    probe: (s, w) => Probe(s, w, "Body"));
                LifeHomePreview.Shoot(outDir, "sr_carmenu", "carmenu", garageCar: active.id, sizes: Sizes,
                    probe: (s, w) => Probe(s, w, "Body"));
                LifeHomePreview.Shoot(outDir, "sr_carmenu_rr", "carmenu", garageCar: st.cars[Mathf.Min(2, st.cars.Count - 1)].id,
                    sizes: Two, probe: (s, w) => Probe(s, w, "Body"));
                if (st.dealerLot.Count == 0) CarMarket.RefreshLot(st);
                LifeSimManager.Save();
                LifeHomePreview.Shoot(outDir, "sr_dealer", "dealer", sizes: Sizes, probe: (s, w) => Probe(s, w, "Body"));
                LifeHomePreview.Shoot(outDir, "sr_dealer_used", "dealer", sizes: Two,
                    fields: new[] { ("dealerShow", (object)Mathf.Max(0, st.dealerLot.Count - 1)) },
                    probe: (s, w) => Probe(s, w, "Body"));
                if (st.dealerLot.Count > 0)
                {
                    var v = Viewings.Open(st, st.dealerLot[0], "lot");
                    st.activeViewing = v.key;
                    LifeSimManager.Save();
                    LifeHomePreview.Shoot(outDir, "sr_viewing", "viewing", sizes: Two, probe: (s, w) => Probe(s, w, "Body"));
                    st.activeViewing = "";
                    LifeSimManager.Save();
                }

                // The switch: PSX_SHOWROOM=0 builds the pages as they were.
                Showroom.On = false;
                LifeHomePreview.Shoot(outDir, "sr_off_carmenu", "carmenu", garageCar: active.id, sizes: new[] { Sizes[1] });
                LifeHomePreview.Shoot(outDir, "sr_off_mycars", "garage", sizes: new[] { Sizes[1] });
            }
            catch (System.Exception e) { Fail("threw: " + e); }
            finally
            {
                Showroom.On = keepOn;
                MenuKit.ScreenSizeOverride = Vector2.zero;
            }
            Debug.Log("[Showroom] DONE, " + fails + " FAIL(s)");
        }

        /// <summary>The overlap count, then the arrows' LEFT / RIGHT routing.</summary>
        static void Probe(LifeHomeScreen screen, string where, string rootName)
        {
            Transform root = null;
            foreach (var t in screen.GetComponentsInChildren<RectTransform>(false))
                if (t.name == rootName) { root = t; break; }
            if (rootName == "Body")
            {
                var body = typeof(LifeHomeScreen).GetField("body", BindingFlags.NonPublic | BindingFlags.Instance)
                               ?.GetValue(screen) as RectTransform;
                if (body != null) root = body;
            }
            if (root == null) { Fail(where + " no " + rootName); return; }
            var viewer = screen.GetComponent<CarViewer>();
            Debug.Log("[Showroom] " + where + " stage " + (viewer != null && viewer.Studio
                ? viewer.StudioTexture.width + "x" + viewer.StudioTexture.height : "NONE"));
            if (viewer == null || !viewer.Studio) Fail(where + " no showroom stage on the page");
            int n = NavScrollPreview.ReportControlOverlap(root.root, where);
            if (n > 0) Fail(where + " " + n + " control overlap(s)");
            BrowseProbe(screen, root, where);
        }

        static void BrowseProbe(LifeHomeScreen screen, Transform root, string where)
        {
            Button prev = null, next = null;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                if (b.name == "Btn_<") prev = b;
                if (b.name == "Btn_>") next = b;
            }
            if (prev == null || next == null) { Debug.Log("[Showroom] " + where + " browse: one car, no arrows"); return; }
            // The page's graph, as the watch builds it a frame in.
            var w = screen.GetComponent<MenuNavWatch>();
            var apply = typeof(MenuNavWatch).GetMethod("ApplyPending", BindingFlags.NonPublic | BindingFlags.Instance);
            if (w != null && apply != null) apply.Invoke(w, null);
            var kp = prev.GetComponent<ShowroomBrowseKeys>();
            var kn = next.GetComponent<ShowroomBrowseKeys>();
            var keepP = kp.browse; var keepN = kn.browse;
            var got = new List<int>();
            kp.browse = d => got.Add(d);
            kn.browse = d => got.Add(d);
            var esGo = new GameObject("ShowroomProbeES");
            var es = esGo.AddComponent<EventSystem>();
            string Move(GameObject from, MoveDirection dir)
            {
                es.SetSelectedGameObject(from);
                var data = new AxisEventData(es)
                {
                    moveDir = dir,
                    moveVector = dir == MoveDirection.Left ? Vector2.left : dir == MoveDirection.Right ? Vector2.right
                               : dir == MoveDirection.Down ? Vector2.down : Vector2.up,
                };
                ExecuteEvents.Execute(from, data, ExecuteEvents.moveHandler);
                var now = es.currentSelectedGameObject;
                return now != null ? now.name : "none";
            }
            try
            {
                string a = Move(next.gameObject, MoveDirection.Right);
                string b = Move(next.gameObject, MoveDirection.Left);
                string c = Move(prev.gameObject, MoveDirection.Left);
                string d = Move(prev.gameObject, MoveDirection.Right);
                string down = Move(next.gameObject, MoveDirection.Down);
                bool ok = got.Count == 4 && got[0] == 1 && got[1] == -1 && got[2] == -1 && got[3] == 1 &&
                          a == next.name && b == next.name && c == prev.name && d == prev.name;
                Debug.Log("[Showroom] " + where + " browse: RIGHT/LEFT on > gave " + string.Join(",", got) +
                          ", cursor stayed " + (ok ? "on the arrow" : a + "/" + b + "/" + c + "/" + d) +
                          "; DOWN from > goes to " + down + (ok ? "  OK" : "  BAD"));
                if (!ok) Fail(where + " LEFT / RIGHT on the arrows did not browse in place");
                if (down == next.name || down == "none") Fail(where + " DOWN from the arrows goes nowhere");
            }
            finally
            {
                kp.browse = keepP; kn.browse = keepN;
                Object.DestroyImmediate(esGo);
            }
        }

        /// <summary>Drag, coast, the 3 s hold, the idle spin's return, the stick.</summary>
        static void TurnProbe(LifeHomeScreen screen, string where)
        {
            var v = screen.GetComponent<CarViewer>();
            if (v == null || !v.Studio) { Fail(where + " turn: no showroom"); return; }
            CarViewerDrag drag = null;
            foreach (var d in screen.GetComponentsInChildren<CarViewerDrag>(false))
                if (d.name == "ShowroomView") { drag = d; break; }
            if (drag == null) { Fail(where + " turn: the stage has no drag handler"); return; }
            float t = 1000f, dt = 1f / 60f;
            void Run(float secs) { int n = Mathf.RoundToInt(secs / dt); for (int i = 0; i < n; i++) { t += dt; v.Tick(t, dt); } }

            Run(0.5f);
            float y0 = v.Yaw; Run(2f); float idle = (v.Yaw - y0) / 2f;

            var esGo = new GameObject("ShowroomTurnES");
            var es = esGo.AddComponent<EventSystem>();
            float dragTurn, coast, held, resumed, stickTurn = 0f, stickIn = 0f;
            try
            {
                var e = new PointerEventData(es) { pressPosition = new Vector2(1000f, 500f), position = new Vector2(990f, 500f) };
                drag.OnBeginDrag(e);
                float yd = v.Yaw;
                for (int i = 0; i < 10; i++)
                {
                    e.delta = new Vector2(-24f, 0f);
                    e.position += e.delta;
                    drag.OnDrag(e);
                    t += dt; v.Tick(t, dt);
                }
                dragTurn = v.Yaw - yd;
                drag.OnEndDrag(e);
                float y1 = v.Yaw; Run(1f); coast = v.Yaw - y1;
                float y2 = v.Yaw; Run(1.6f); held = v.Yaw - y2;
                Run(1.4f);
                float y3 = v.Yaw; Run(1f); resumed = v.Yaw - y3;
            }
            finally { Object.DestroyImmediate(esGo); }

            // The stick: a virtual pad's right stick hard right, read the way
            // LifeHomeScreen.Update reads it, applied the way it applies it.
            Gamepad pad = null;
            try
            {
                pad = InputSystem.AddDevice<Gamepad>();
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(1f, 0f) });
                InputSystem.Update();
                stickIn = CarViewer.TurnInput();
                float ys = v.Yaw;
                for (int i = 0; i < 30; i++) { v.Nudge(-stickIn * 200f * dt); t += dt; v.Tick(t, dt); }
                stickTurn = v.Yaw - ys;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Showroom] turn: virtual pad: " + ex.Message); }
            finally { if (pad != null) InputSystem.RemoveDevice(pad); }

            Debug.Log("[Showroom] " + where + " TURN: idle spin " + idle.ToString("0.0") + " deg/s; drag 240 px turned " +
                      dragTurn.ToString("0.0") + " deg, coasted " + coast.ToString("0.0") + " in 1 s after release, " +
                      held.ToString("0.0") + " in the next 1.6 s (held), " + resumed.ToString("0.0") +
                      " deg in the second after 4 s (resumed); right stick read " + stickIn.ToString("0.00") +
                      ", turned " + stickTurn.ToString("0.0") + " deg in 0.5 s");
            if (idle < 12f || idle > 24f) Fail(where + " idle spin " + idle);
            if (dragTurn < 10f) Fail(where + " a drag to the left did not turn the car");
            if (Mathf.Abs(held) > 4f) Fail(where + " the spin did not hold after a drag");
            if (resumed < 10f) Fail(where + " the idle spin did not come back");
            if (stickIn < 0.9f || Mathf.Abs(stickTurn) < 40f) Fail(where + " the right stick did not turn the car");
        }
    }
}
