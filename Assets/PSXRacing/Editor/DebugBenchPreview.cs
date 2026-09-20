using System.Collections.Generic;
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
    /// Photographs the debug bench — both pages, and the pause menu that opens
    /// it — at the three aspects the menus are played at, without play mode.
    ///
    /// Same reason <see cref="LifeHomePreview"/> exists: the page is built
    /// entirely at runtime, so nothing about it is visible in the scene view
    /// and a compile proves nothing about whether forty two-line switches fit
    /// a cell, or whether the button beside RESUME is on the canvas at 4:3.
    /// And the same two rules for a preview that does not lie: tell MenuKit
    /// what device it is imitating BEFORE anything is built, and pin every
    /// canvas's scale factor by hand, because Screen still reports the
    /// batchmode window.
    ///
    /// It also walks the pad graph — the geometric one the player actually
    /// uses — and fails loudly on a control nothing points at, which is the
    /// one thing about a page like this a picture can never show.
    /// </summary>
    public static class DebugBenchPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("phone_wide", 1998, 891),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        [MenuItem("PSX Racing/Preview Debug Bench")]
        public static void Capture()
        {
            string outDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Screenshots");
            Directory.CreateDirectory(outDir);

            // A debug career with a real catalog car in it: an NA road car, so
            // the blower is offered and the wing is refused — both states on
            // one page.
            LifeSimManager.DeleteSave();
            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            var st = LifeSimManager.State;
            LifeRules.EnableDebug(st);
            CarSpec spec = null;
            foreach (var c in CarCatalog.All)
                if (!c.IsForcedInduction && !c.IsRaceCar && c.builtHp > c.hp) { spec = c; break; }
            if (spec == null) { Debug.LogError("[BenchPreview] no NA road car in the catalog"); return; }
            var car = new OwnedCar
            {
                id = "bench_preview", specId = spec.id, displayName = spec.name,
                catalogPrice = spec.price, paidPrice = spec.price, odoMiles = 88000f,
            };
            st.cars.Add(car);
            st.activeCar = car.id;

            Shoot(outDir, "bench_faults_clean", car, DebugCarPanel.Page.Faults);

            // The state the page spends its life in: some switches thrown, a
            // build half done. Includes the longest effect line in the data
            // (trans_slip) and one of the not-simulated ones (ps_leak).
            foreach (var id in new[] { "trans_slip", "rotor_warp", "alignment", "ps_leak", "display_failure" })
                DebugCarOps.SetFault(car, spec, id, true);
            // One FOUND, so both captions are in the picture.
            if (car.faults.Count > 0) { car.faults[0].hidden = false; car.faults[0].diagnosed = true; }
            DebugCarOps.SetStage(car, Upgrades.Kind.Power, 3);
            DebugCarOps.SetStage(car, Upgrades.Kind.Suspension, 4);
            DebugCarOps.SetStage(car, Upgrades.Kind.Seat, 2);
            DebugCarOps.SetMod(car, spec, Upgrades.Mod.Supercharger, true);
            DebugCarOps.SetMod(car, spec, Upgrades.Mod.WeldedDiff, true);

            Shoot(outDir, "bench_faults", car, DebugCarPanel.Page.Faults);
            Shoot(outDir, "bench_faults_end", car, DebugCarPanel.Page.Faults, scrollTo: 0f);
            Shoot(outDir, "bench_parts", car, DebugCarPanel.Page.Parts);
            Shoot(outDir, "bench_parts_end", car, DebugCarPanel.Page.Parts, scrollTo: 0f);

            ShootPause(outDir, "bench_pause");

            RaceHandoff.ClearAll();
            MenuKit.ScreenSizeOverride = Vector2.zero;
            Debug.Log("[BenchPreview] done");
        }

        static void Shoot(string outDir, string label, OwnedCar car, DebugCarPanel.Page page,
                          float scrollTo = 1f)
        {
            foreach (var size in Sizes)
            {
                var cam = NewStage(size.w, size.h, out var rt);
                MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);

                var host = new GameObject("Bench");
                var panel = host.AddComponent<DebugCarPanel>();
                panel.target = car;
                // Chosen BEFORE Open, not by turning the page after it: the
                // page is only ever built once that way, so nothing depends on
                // a teardown having happened.
                panel.Show(page);
                panel.Open();

                Repoint(cam, size.w, size.h);

                var sr = Object.FindAnyObjectByType<ScrollRect>();
                if (sr != null)
                {
                    if (scrollTo < 1f)
                    {
                        sr.verticalNormalizedPosition = Mathf.Clamp01(scrollTo);
                        Canvas.ForceUpdateCanvases();
                    }
                    float contentH = sr.content != null ? sr.content.sizeDelta.y : 0f;
                    float viewH = sr.viewport != null ? sr.viewport.rect.height : 0f;
                    var g = sr.GetComponent<Graphic>();
                    Debug.Log("[BenchPreview] " + label + "/" + size.name + " content " +
                              contentH.ToString("0") + " vs view " + viewH.ToString("0") +
                              (g != null && g.raycastTarget ? "  drag-catcher OK" : "  NO DRAG CATCHER"));
                }

                CheckReach(host.transform, label + "/" + size.name);
                CheckOverflow(host.transform, label + "/" + size.name);
                Snap(cam, rt, size.w, size.h, Path.Combine(outDir, label + "_" + size.name + ".png"));
            }
        }

        /// <summary>The pause menu as a debug career sees it mid-drive, with
        /// the bench's button beside RESUME.</summary>
        static void ShootPause(string outDir, string label)
        {
            foreach (var size in Sizes)
            {
                var cam = NewStage(size.w, size.h, out var rt);
                MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);
                RaceHandoff.ClearAll();
                RaceHandoff.FromLifeSim = true;

                var host = new GameObject("GameSystems");
                var menu = host.AddComponent<PauseMenu>();
                // BuildUI rather than Start: Start closes the panel again on
                // its last line, and a photograph of a closed menu is a
                // photograph of the MENU button.
                var build = typeof(PauseMenu).GetMethod("BuildUI",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (build == null) { Debug.LogError("[BenchPreview] no PauseMenu.BuildUI"); return; }
                build.Invoke(menu, null);

                Repoint(cam, size.w, size.h);

                // EVERY control, not just the bench's. The three buttons that
                // stand BESIDE the column — the bench, FILM GRADE, FULLSCREEN —
                // are placed by hand at a fixed offset from the centre, and
                // whether that offset is still on the canvas is a question
                // about the aspect, not about which button it is. Checking one
                // of the three proved nothing about the other two.
                bool found = false;
                var offCanvas = new List<string>();
                foreach (var b in host.GetComponentsInChildren<Button>(true))
                {
                    if (b.name.Contains("DEBUG: FAULTS")) found = true;
                    var r = (RectTransform)b.transform;
                    var corners = new Vector3[4];
                    r.GetWorldCorners(corners);
                    var canvasRT = (RectTransform)b.GetComponentInParent<Canvas>().transform;
                    var cc = new Vector3[4];
                    canvasRT.GetWorldCorners(cc);
                    bool inside = corners[0].x >= cc[0].x - 0.01f && corners[2].x <= cc[2].x + 0.01f &&
                                  corners[0].y >= cc[0].y - 0.01f && corners[2].y <= cc[2].y + 0.01f;
                    if (!inside) offCanvas.Add(b.name);
                }
                // And the panel's own two lines of text — the title and the
                // footer under the last row. The footer is the part of this
                // menu that fell off a phone FIRST, being below everything
                // else, and being unpressable it could do so without anyone
                // noticing. Direct children of the panel: a button's caption is
                // parented to the button and is CheckOverflow's business.
                foreach (var t in host.GetComponentsInChildren<Text>(true))
                {
                    if (t.transform.parent == null || t.transform.parent.name != "Panel") continue;
                    var tr = (RectTransform)t.transform;
                    var tc = new Vector3[4];
                    tr.GetWorldCorners(tc);
                    var canvasRT = (RectTransform)t.GetComponentInParent<Canvas>().transform;
                    var cc2 = new Vector3[4];
                    canvasRT.GetWorldCorners(cc2);
                    // Vertically only: these are centred lines with a generous
                    // 420-unit box that is wider than the words in it.
                    if (tc[0].y < cc2[0].y - 0.01f || tc[2].y > cc2[2].y + 0.01f)
                        offCanvas.Add("\"" + t.text + "\"");
                }

                if (offCanvas.Count > 0)
                    Debug.LogError("[BenchPreview] " + label + "/" + size.name +
                                   " OFF THE CANVAS — " + string.Join(", ", offCanvas));
                else Debug.Log("[BenchPreview] " + label + "/" + size.name +
                               " every pause row is on the canvas");
                if (!found) Debug.LogError("[BenchPreview] " + label + "/" + size.name +
                                           " NO BENCH BUTTON in a debug career's pause menu");

                CheckReach(host.transform, label + "/" + size.name);
                CheckOverflow(host.transform, label + "/" + size.name);
                Snap(cam, rt, size.w, size.h, Path.Combine(outDir, label + "_" + size.name + ".png"));
            }
        }

        // ------------------------------------------------------------------

        static Camera NewStage(int w, int h, out RenderTexture rt)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Not black: the bench's backdrop is 94% opaque over a RACE, and a
            // mid-grey behind it shows what bleeds through.
            cam.backgroundColor = new Color(0.35f, 0.42f, 0.50f);
            cam.orthographic = true;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            return cam;
        }

        /// <summary>
        /// Point every overlay canvas at the preview camera and pin its scale.
        ///
        /// TWO scalers are in play and they are not the same: MenuKit canvases
        /// match HEIGHT against the device's design column, and the pause
        /// menu's matches a 50/50 blend against 1280x720. Each is reproduced
        /// from its own settings, read off the component, so the picture is of
        /// the layout the player gets and not of one this tool invented.
        /// </summary>
        static void Repoint(Camera cam, int w, int h)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f + c.sortingOrder * 0.001f;
                var cs = c.GetComponent<CanvasScaler>();
                if (cs == null) continue;
                var reference = cs.referenceResolution;
                float match = cs.matchWidthOrHeight;
                float logW = Mathf.Log(w / reference.x, 2f), logH = Mathf.Log(h / reference.y, 2f);
                cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                cs.scaleFactor = Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
            }
            Canvas.ForceUpdateCanvases();
        }

        static void Snap(Camera cam, RenderTexture rt, int w, int h, string path)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[BenchPreview] wrote " + path);
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            MenuKit.ScreenSizeOverride = Vector2.zero;
        }

        /// <summary>Flood the geometric pad graph from the first control and
        /// name anything it never reaches — see LifeHomePreview.CheckNavReach,
        /// which this is, without a tab strip to join.</summary>
        static void CheckReach(Transform root, string where)
        {
            var rows = MenuNav.Collect(root);
            if (rows.Count == 0) { Debug.LogError("[BenchPreview] " + where + " nav: NO CONTROLS"); return; }
            if (!MenuNav.RectsResolved(rows))
            {
                Debug.LogError("[BenchPreview] " + where + " nav: rects unresolved, NOT CHECKED");
                return;
            }
            MenuNav.Grid(rows);

            var seen = new HashSet<Selectable>();
            var queue = new Queue<Selectable>();
            seen.Add(rows[0]); queue.Enqueue(rows[0]);
            while (queue.Count > 0)
            {
                var nav = queue.Dequeue().navigation;
                foreach (var next in new[] { nav.selectOnUp, nav.selectOnDown,
                                             nav.selectOnLeft, nav.selectOnRight })
                    if (next != null && seen.Add(next)) queue.Enqueue(next);
            }
            var lost = new List<string>();
            foreach (var s in rows) if (!seen.Contains(s)) lost.Add(s.name);
            if (lost.Count > 0)
                Debug.LogError("[BenchPreview] " + where + " nav: UNREACHABLE BY PAD — " +
                               string.Join(", ", lost));
            else
                Debug.Log("[BenchPreview] " + where + " nav: all " + rows.Count + " controls reachable");
        }

        /// <summary>
        /// Does any caption run out of the button it is written on?
        ///
        /// MenuKit text OVERFLOWS rather than clips, so a line one word too
        /// long is drawn across the neighbouring cell — legible in neither.
        /// preferredWidth is the width the text WANTS; compared with the rect
        /// it was given, per aspect, this is the check that the 46-character
        /// clip on an effect line is actually the right number.
        /// </summary>
        static void CheckOverflow(Transform root, string where)
        {
            int bad = 0; string first = null;
            foreach (var b in root.GetComponentsInChildren<Button>(false))
            {
                var t = b.GetComponentInChildren<Text>();
                if (t == null) continue;
                float room = ((RectTransform)t.transform).rect.width;
                if (room <= 1f || t.preferredWidth <= room + 0.5f) continue;
                bad++;
                if (first == null)
                    first = b.name + " wants " + t.preferredWidth.ToString("0") + " in " + room.ToString("0");
            }
            if (bad > 0)
                Debug.LogError("[BenchPreview] " + where + " TEXT OVERFLOWS its button on " + bad +
                               " control(s) — first: " + first);
            else Debug.Log("[BenchPreview] " + where + " every caption fits its button");
        }
    }
}
