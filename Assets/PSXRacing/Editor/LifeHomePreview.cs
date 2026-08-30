using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Renders the LifeSim menu to PNGs at several aspect ratios, without play
    /// mode, so layout can be checked before a build reaches a phone.
    ///
    /// This exists because the menu is generated entirely at runtime: nothing is
    /// visible in the scene view, the editor Game view is one aspect ratio, and
    /// the bug that actually shipped — the body panel riding up over the tab bar
    /// — only appears on a canvas shorter than about 718 units, which is to say
    /// only on a wide phone. Compiling proves nothing about layout.
    /// </summary>
    public static class LifeHomePreview
    {
        // The first entry is the reporter's handset aspect (~2.24:1), which is
        // where the overlap showed up.
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("phone_wide", 1998, 891),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        [MenuItem("PSX Racing/Preview LifeSim Menu")]
        public static void Capture()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            // No save -> the wizard. Capture that at every aspect first.
            LifeSimManager.DeleteSave();
            Shoot(outDir, "wizard");

            // Then seed a running game and capture the screens that actually get
            // used day to day — the home hub was where the overlap was reported.
            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            LifeRules.SeedFallbackCar(LifeSimManager.State);
            // Something in the diary before anything is photographed. Without it
            // the calendar renders a month of empty cells and proves nothing
            // about the marker row or the day panel — and the gold "IN THE DIARY
            // TODAY" row on MAIN only exists on a day that has a booking, so it
            // is otherwise a piece of layout nobody has ever seen.
            LifeRules.Book(LifeSimManager.State, LifeSimManager.State.day, 1, false);
            LifeRules.Book(LifeSimManager.State, LifeSimManager.State.day + 2, 3, false);
            LifeRules.Book(LifeSimManager.State, LifeSimManager.State.day + 9, 4, true);
            LifeSimManager.Save();

            Shoot(outDir, "home");

            // MAIN once per BAND of the day. The shift button and the sleep
            // button both say something different in each one - the shop is
            // shut before noon, and only the night sleep rolls the calendar -
            // and a screen shot in one state proves nothing about the other two.
            //
            // No scrollTo any more. MAIN is two columns precisely so that all of
            // it is above the fold on every canvas, so there is no second half
            // to scroll down to; mustFit below is what holds that to account.
            for (int slot = 0; slot < LifeRules.SlotNames.Length; slot++)
            {
                LifeSimManager.State.slotIndex = slot;
                LifeSimManager.Save();
                Shoot(outDir, "home_" + LifeRules.SlotNames[slot].ToLower(), "main",
                      mustFit: true);
            }

            LifeSimManager.State.slotIndex = 0;
            LifeSimManager.Save();

            // Debug mode replaces the top of MAIN with a three-across tool row.
            // That row is positioned from ColL/ColW arithmetic rather than by
            // anchors, so it is the one piece of this screen whose fit is not
            // self-evident — and a state that only exists behind a button is a
            // state nobody would otherwise render.
            LifeRules.EnableDebug(LifeSimManager.State);
            LifeSimManager.Save();
            Shoot(outDir, "home_debug");

            // A SECOND car, because the garage's car switcher only draws when
            // there is something to switch between — a one-car garage renders
            // identically with and without it, so the single-car capture proves
            // nothing about the row that was just added.
            if (CarCatalog.Ready && CarCatalog.All.Count > 1)
            {
                CarMarket.MakeOwnedCar(LifeSimManager.State, CarCatalog.All[1], 88, 41000f, 12500);
                LifeSimManager.Save();
                Shoot(outDir, "garage_multi", "garage");
                Shoot(outDir, "debugcars", "debugcars");
                var extra = LifeSimManager.State.cars[LifeSimManager.State.cars.Count - 1];
                LifeSimManager.State.cars.Remove(extra);
            }

            LifeSimManager.State.debugMode = false;
            LifeSimManager.State.garageSlots = 1;
            LifeSimManager.Save();

            // The blacklist board is the tallest screen in the game — ten rows
            // plus a header — so it is the one most likely to run off the bottom
            // of a short canvas. Give it a state where a rung is actually open,
            // or the capture shows ten identical locked rows and proves nothing
            // about the challenge button.
            var s = LifeSimManager.State;
            s.streetRacesWon = 3;
            s.streetRep = 10f;
            // Give the garage something to show: a worn car with a real fault is
            // the state the repair options appear in, and those options going
            // off-screen is exactly what got reported.
            var car = s.ActiveCar;
            if (car != null)
            {
                car.engine = 57f; car.tires = 38f; car.carHP = 41f;
                car.paint = 51f; car.fuel = 43f;
                if (car.faults.Count == 0)
                {
                    var f = FaultCatalog.RollWearFault(car, "tires", false);
                    // Revealed by hand: faults now arrive hidden, and the whole
                    // point of this shot is the repair row's LAYOUT, which a
                    // hidden fault does not draw.
                    if (f != null) { f.hidden = false; f.diagnosed = true; car.faults.Add(f); }
                }
            }
            s.money = 4841;

            LifeSimManager.Save();

            // Every tab, not just the two that had been looked at. Three of the
            // four bugs found here were on tabs nobody had rendered.
            foreach (var t in new[] { "rivals", "garage", "calendar", "news", "options",
                                      "market", "eat", "bills", "jobs",
                                      "inspect", "inspectfocus", "toolbox" })
                Shoot(outDir, t, t);

            // OPTIONS took NEW GAME and the debug rung off the launch screen, so
            // it is now the tallest of the settings pages and the one where
            // something can fall off the end. Shot from the bottom of the
            // scroll, where those two live.
            Shoot(outDir, "options_meta", "options", scrollTo: 0f);

            // The condition bars, which are below the fold on the GARAGE tab and
            // are the whole subject of "condition % should not be shown outside
            // debug mode". A tab shot from the top of the scroll shows the
            // turntable and proves nothing about them.
            Shoot(outDir, "garage_bars", "garage", scrollTo: 0.45f);

            LifeSimManager.DeleteSave();
        }

        /// <param name="scrollTo">Where to leave the body's scroll before the
        /// shutter: 1 is the top, 0 the bottom. A page taller than the canvas
        /// cannot be photographed whole, so the shots that care about what is
        /// underneath say where to look.</param>
        /// <param name="mustFit">Assert that this page does NOT scroll on any
        /// aspect. "All of it on one screen" is a REQUIREMENT of the launch
        /// screen, not a nicety, and a requirement nothing checks is one that
        /// decays the next time a line of text is added to the page. A PNG
        /// cannot show what is below the fold; this can.</param>
        static void Shoot(string outDir, string label, string tab = null, float scrollTo = 1f,
                          bool mustFit = false)
        {
            foreach (var size in Sizes)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var camGO = new GameObject("PreviewCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.04f, 0.09f);
                cam.orthographic = true;

                var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = 1 };
                cam.targetTexture = rt;

                // Tell MenuKit what device this is BEFORE anything is built.
                // Screen still reports the batchmode editor window, not this
                // RenderTexture, so without the override every shot would be
                // laid out for whatever the editor happens to be — which is how
                // a "phone" capture came back showing the desktop column.
                MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);

                var host = new GameObject("LifeHome");
                var screen = host.AddComponent<LifeHomeScreen>();

                // Which tab to shoot is chosen BEFORE Start, not by rebuilding
                // after it. Rebuild tears the old body down with Destroy(), which
                // is deferred to the end of a frame that never comes outside play
                // mode — so a post-Start switch photographed both tabs stacked on
                // top of each other. Setting the field first means only the
                // wanted screen is ever built.
                if (tab != null)
                {
                    var tabField = typeof(LifeHomeScreen).GetField("tab",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (tabField == null) Debug.LogError("[HomePreview] no tab field");
                    else tabField.SetValue(screen, tab);
                }

                // Start() is where the whole UI is constructed. Editor scripts do
                // not get lifecycle callbacks, so call it directly.
                var start = typeof(LifeHomeScreen).GetMethod("Start",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (start == null) { Debug.LogError("[HomePreview] no Start()"); return; }
                start.Invoke(screen, null);

                // The UI builds a ScreenSpaceOverlay canvas, which ignores
                // cameras and render textures. Re-point it at the preview camera
                // so it composites into the RT at the size we asked for.
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 10f;

                    // CanvasScaler's ScaleWithScreenSize reads Screen too, so it
                    // would scale for the editor window rather than for this
                    // RenderTexture. Pin the factor by hand: RT pixels divided by
                    // the design column gives exactly the unit space the layout
                    // was written against.
                    var cs = c.GetComponent<CanvasScaler>();
                    if (cs != null)
                    {
                        cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                        cs.scaleFactor = size.h / MenuKit.DesignHeight;
                    }
                }
                Canvas.ForceUpdateCanvases();

                // Report whether this screen can actually be scrolled to the
                // bottom. A static render cannot show that, and "the options are
                // off screen" turned out to be a scroll that received no drag
                // events rather than a layout that was too tall.
                var sr = Object.FindFirstObjectByType<UnityEngine.UI.ScrollRect>();
                if (sr != null && scrollTo < 1f)
                {
                    sr.verticalNormalizedPosition = Mathf.Clamp01(scrollTo);
                    Canvas.ForceUpdateCanvases();
                }
                if (sr != null && tab != null)
                {
                    float contentH = sr.content != null ? sr.content.sizeDelta.y : 0f;
                    float viewH = sr.viewport != null ? sr.viewport.rect.height : 0f;
                    var g = sr.GetComponent<UnityEngine.UI.Graphic>();
                    bool draggable = g != null && g.raycastTarget;
                    // A content rect is never sized SHORTER than its viewport
                    // (FitScrollContent floors it), so anything past a unit or
                    // two of slack is real overflow rather than rounding.
                    bool scrolls = contentH > viewH + 1f;
                    string line = "[HomePreview] " + label + "/" + size.name +
                                  " content " + contentH.ToString("0") + " vs view " +
                                  viewH.ToString("0") + (scrolls ? "  SCROLLS" : "  fits") +
                                  (draggable ? "  drag-catcher OK" : "  NO DRAG CATCHER");
                    if (mustFit && scrolls)
                        Debug.LogError(line + "  <-- MUST FIT ON ONE SCREEN, and does not");
                    else Debug.Log(line);
                }

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                string path = Path.Combine(outDir, "menu_" + label + "_" + size.name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[HomePreview] wrote " + path);

                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                MenuKit.ScreenSizeOverride = Vector2.zero;
            }
        }
    }
}
