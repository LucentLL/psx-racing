using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The parts shelf's pictures (v24, owner 2026-11-11): the owner's wreck
    /// page - a maxed EG Civic in the driveway, $1,003,215, skill 7, at a
    /// wrecked EG with two used turbos, a subframe, semi-slicks and light
    /// wheels on it - and, once the shelf exists, STORED PARTS with three
    /// parts, the pick-a-car step with two Civics, and a Civic's own pages
    /// saying "on the shelf". Writes Screenshots/spares/.
    ///
    /// Written to compile against the code BEFORE the shelf as well (anything
    /// new is reached by reflection), so the same tool shoots the "before"
    /// from the base branch and the "after" from this one. The walk-up prompt
    /// that ghosted behind the wreck rows is built too (a FootScreen with the
    /// wreck's title and detail in it) - the next frame's FootScreen.Update is
    /// run only when the page has switched the prompt off, which is exactly
    /// what the game's next frame does.
    /// </summary>
    public static class SparesPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
            ("phone_wide", 1998, 891),
        };

        const string EgId = "honda_civic_sir_ii__eg___91";
        const string Donor = "WRECKED HONDA CIVIC SIR-II (EG) `93";

        static string OutDir()
        {
            string d = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "spares");
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>PSX_SPARES_TAG=before|after names the wreck shots.</summary>
        public static void Capture()
        {
            string tag = System.Environment.GetEnvironmentVariable("PSX_SPARES_TAG");
            if (string.IsNullOrEmpty(tag)) tag = "after";
            string outDir = OutDir();
            LifeHomePreview.clipCount = 0;

            // Its own career key: the editor's PlayerPrefs are shared by every
            // sandbox on this PC, and other agents' previews use the main one.
            LifeSimManager.UseScratchSave("spares");
            LifeSimManager.DeleteSave();
            LifeSimManager.StartNewGame("VINCE", 25, LifeRules.DefaultJobIndex);
            var s = LifeSimManager.State;
            var eg = CarCatalog.Get(EgId);
            if (eg == null) { Debug.LogError("[Spares] no EG Civic in the catalog"); return; }
            s.cars.Clear();
            s.activeCar = "";
            var maxed = CarMarket.MakeOwnedCar(s, eg, 92, 64000f, eg.price);
            for (var k = (Upgrades.Kind)0; k <= Upgrades.LastKind; k++) Upgrades.SetStage(maxed, k, Upgrades.MaxStage);
            maxed.fuel = 80f;
            s.activeCar = maxed.id;
            s.money = 1003215;
            s.mechSkill = 7.5f;

            int wreck = FindOwnersWreck(s);
            Junkyard.LookOverWreck(s, wreck);
            s.mechSkill = 7.5f;
            LifeSimManager.Save();
            Debug.Log("[Spares] wreck " + wreck + " in week " + (s.day / 7) + " (day " + s.day + ")");

            ShootWreck(outDir, "wreck_" + tag, wreck);

            var spares = System.Type.GetType("PSXRacing.LifeSim.Spares, Assembly-CSharp");
            if (spares == null)
            {
                Debug.Log("[Spares] no Spares type in this build - wreck shots only");
                Debug.Log("[Spares] CLIP TOTAL " + LifeHomePreview.clipCount);
                return;
            }

            // A second, stock EG Civic, with a known worn-tyre fault.
            var second = CarMarket.MakeOwnedCar(s, eg, 70, 118000f, eg.price / 4);
            second.displayName = eg.name;
            second.fuel = 60f;
            second.tires = 38f;
            second.faults.Add(new CarFault { id = "tire_wear", label = "Uneven Tire Wear", stat = "tires",
                                             add = 30, cost = 180, days = 1, repairType = "delivery",
                                             hidden = false, diagnosed = true });
            // Three parts on the shelf, off this very wreck: the clean turbo,
            // the semi-slicks, the subframe.
            var shelve = spares.GetMethod("Shelve");
            var contents = Junkyard.WreckContents(s, wreck);
            var picked = new List<YardPart>();
            foreach (var want in new[] { "TURBO + MANIFOLD, USED", "SEMI-SLICKS, ONE WEEKEND", "REAR SUBFRAME + DIFF" })
            {
                YardPart best = null;
                foreach (var r in contents)
                    if (r.part.label == want && (best == null || r.part.grade > best.grade)) best = r.part;
                if (best != null) picked.Add(best);
            }
            if (picked.Count < 3)
            {
                picked.Clear();
                picked.Add(new YardPart { label = "TURBO + MANIFOLD, USED", upgradeKind = "power", maxStage = 4, grade = 88, stat = "engine", donorHint = "off a track car" });
                picked.Add(new YardPart { label = "SEMI-SLICKS, ONE WEEKEND", upgradeKind = "tires", maxStage = 3, grade = 74, stat = "engine", donorHint = "one season on it" });
                picked.Add(new YardPart { label = "REAR SUBFRAME + DIFF", stat = "hp", add = 38, basePrice = 480, days = 3, grade = 90, donorHint = "no play in it" });
            }
            var keepPrice = typeof(Junkyard).GetMethod("PullKeepPrice");
            foreach (var p in picked)
            {
                int paid = (keepPrice != null ? (int)keepPrice.Invoke(null, new object[] { s, p }) : 100) -
                           Junkyard.ToolRental(s);
                shelve.Invoke(null, new object[] { s, p, EgId, Donor.Substring("WRECKED ".Length), paid });
            }
            // The tyre spare a worn-tyre Civic would want, to show REPLACE.
            shelve.Invoke(null, new object[] { s, new YardPart { label = "MATCHED SET + TYRES", stat = "tires", add = 45,
                basePrice = 260, days = 2, grade = 81, donorHint = "four the same, finally" }, "", "THE YARD'S RACK", 180 });
            s.mechSkill = 24f;
            LifeSimManager.Save();
            string semi = null;
            if (typeof(LifeState).GetField("partsShelf")?.GetValue(s) is System.Collections.IList shelf)
                foreach (var p in shelf)
                {
                    var t = p.GetType();
                    if (((string)t.GetField("label").GetValue(p)).StartsWith("SEMI"))
                        semi = (string)t.GetField("id").GetValue(p);
                }

            var sizes = new (string, int, int)[] { ("landscape_16x9", 1280, 720), ("tablet_4x3", 1024, 768), ("phone_wide", 1998, 891) };
            Shoot(outDir, "spares_stored", "parts", null, sizes, null, 1f);
            Shoot(outDir, "spares_pick", "partfit", null, sizes, new (string, object)[] { ("sparePickId", semi ?? "") }, 1f);
            Shoot(outDir, "spares_carpage", "carmenu", second.id, sizes, null, 0.35f);
            Shoot(outDir, "spares_tune", "tune", second.id, sizes, null, 0.55f);
            Shoot(outDir, "spares_mycars", "garage", null, sizes, null, 1f);
            Shoot(outDir, "spares_mechanic", "service", second.id, sizes, null, 0.6f);
            Debug.Log("[Spares] CLIP TOTAL " + LifeHomePreview.clipCount);
        }

        /// <summary>The wreck whose five slots are the owner's (SCRAP TURBO,
        /// CLEAN TURBO, CLEAN SUBFRAME, SOLID SEMI-SLICKS, SOLID LIGHT WHEELS),
        /// searched over weeks and shells; else the shell with the most
        /// hardware still on it. Leaves s.day in that week.</summary>
        static int FindOwnersWreck(LifeState s)
        {
            string[] labels = { "TURBO + MANIFOLD, USED", "TURBO + MANIFOLD, USED", "REAR SUBFRAME + DIFF",
                                "SEMI-SLICKS, ONE WEEKEND", "LIGHT WHEELS + SEATS" };
            string[] grades = { "SCRAP", "CLEAN", "CLEAN", "SOLID", "SOLID" };
            int bestScore = -1, bestDay = s.day, bestWreck = 0;
            for (int week = 0; week < 1200; week++)
            {
                s.day = week * 7 + 1;
                for (int w = 0; w < 12; w++)
                {
                    var c = Junkyard.WreckContents(s, w);
                    bool exact = c.Count == 5;
                    int hw = 0;
                    for (int i = 0; i < c.Count; i++)
                    {
                        if (c[i].stripped) { exact = false; continue; }
                        if (c[i].part.IsUpgrade) hw++;
                        if (i < 5 && (c[i].part.label != labels[i] || Junkyard.GradeWord(c[i].part.grade) != grades[i])) exact = false;
                    }
                    if (exact) { Debug.Log("[Spares] found the owner's wreck exactly"); return w; }
                    int score = hw * 10 + (c.Exists(r => r.Present && r.part.label.Contains("TURBO")) ? 5 : 0) +
                                (c.TrueForAll(r => !r.stripped) ? 3 : 0);
                    if (score > bestScore) { bestScore = score; bestDay = s.day; bestWreck = w; }
                }
            }
            s.day = bestDay;
            Debug.Log("[Spares] owner's exact wreck not in range; using the most hardware (" + bestScore + ")");
            return bestWreck;
        }

        static void Shoot(string outDir, string label, string tab, string garageCar,
                          (string, int, int)[] sizes, (string, object)[] fields, float scrollTo)
        {
            var m = typeof(LifeHomePreview).GetMethod("Shoot", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { Debug.LogError("[Spares] LifeHomePreview.Shoot not found"); return; }
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                switch (ps[i].Name)
                {
                    case "outDir": args[i] = outDir; break;
                    case "label": args[i] = label; break;
                    case "tab": args[i] = tab; break;
                    case "scrollTo": args[i] = scrollTo; break;
                    case "garageCar": args[i] = garageCar; break;
                    case "sizes": args[i] = sizes; break;
                    case "fields": args[i] = fields; break;
                    default: args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null; break;
                }
            }
            try { m.Invoke(null, args); }
            catch (System.Exception e) { Debug.LogError("[Spares] shot " + label + " threw: " + (e.InnerException ?? e)); }
        }

        static void ShootWreck(string outDir, string label, int wreck)
        {
            foreach (var size in Sizes)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cam = new GameObject("PreviewCam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                // The yard at dusk stands in for the world behind the page.
                cam.backgroundColor = new Color(0.30f, 0.29f, 0.26f);
                cam.orthographic = true;
                var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;
                MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);

                // The walk-up prompt, as the town leaves it while the page is up.
                var footGo = new GameObject("FootUI");
                var foot = footGo.AddComponent<PSXRacing.OnFoot.FootScreen>();
                var start = typeof(PSXRacing.OnFoot.FootScreen).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
                start?.Invoke(foot, null);
                SetText(foot, "titleText", Donor);
                SetText(foot, "detailText", Junkyard.WreckPartsLeft(LifeSimManager.State, wreck) +
                                            " parts still on it, yours for the pulling.");
                SetText(foot, "actionText", "PRESS F —  PULL   ·   PULL SOMETHING OFF IT");

                var host = new GameObject("Wreck");
                var screen = host.AddComponent<PSXRacing.OnFoot.WreckScreen>();
                screen.wreck = wreck;
                screen.donorName = Donor;
                var specField = typeof(PSXRacing.OnFoot.WreckScreen).GetField("donorSpecId");
                if (specField != null) specField.SetValue(screen, EgId);
                screen.Open();
                // The next frame: the prompt repaints - blank, if the page
                // switched it off; otherwise it holds the wreck's words.
                if (!foot.show)
                    typeof(PSXRacing.OnFoot.FootScreen).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(foot, null);

                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = cam;
                    c.planeDistance = 10f;
                    var cs = c.GetComponent<CanvasScaler>();
                    if (cs == null) continue;
                    float f = c.name == "GarageCanvas"
                        ? Mathf.Sqrt((size.w / 1280f) * (size.h / 720f))   // its own 1280x720, half width half height
                        : size.h / MenuKit.DesignHeight;
                    cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    cs.scaleFactor = f;
                }
                Canvas.ForceUpdateCanvases();
                LifeHomePreview.ReportClippedText(cam, label + "/" + size.name, size.w, size.h);

                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = Path.Combine(outDir, "menu_" + label + "_" + size.name + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("[Spares] wrote " + path);
                Object.DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                MenuKit.ScreenSizeOverride = Vector2.zero;
            }
        }

        static void SetText(object o, string field, string value)
        {
            var f = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.GetValue(o) is Text t) t.text = value;
        }
    }
}
