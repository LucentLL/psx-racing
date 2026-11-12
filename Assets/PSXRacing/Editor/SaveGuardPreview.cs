using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TITLE'S SAVE-GUARD NOTES (R0a) at the three preview aspects, with
    /// LifeHomePreview's CLIP/OVERLAP report: a healthy career (for
    /// comparison), a newer build's (READ-ONLY), one an older build saved over
    /// (this build's copy or the older build's), one that would not load, and
    /// NEW GAME pressed on that last one (its confirm, review 11-12).
    ///
    /// The title is built bare - its menu only, without the Blue Ridge race it
    /// normally sits on - over a hazy-sky grey, so what is checked is the
    /// layout and the words, not the scenery. Everything runs on a scratch
    /// key (never the career's). Writes Screenshots/saveguard/ and
    /// saveguard_report.txt beside the PNGs.
    /// </summary>
    public static class SaveGuardPreview
    {
        static readonly (string name, int w, int h)[] Sizes =
        {
            ("phone_wide", 1998, 891),
            ("landscape_16x9", 1280, 720),
            ("tablet_4x3", 1024, 768),
        };

        const string Scratch = "preview.saveguard";

        public static void Capture()
        {
            string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "saveguard");
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            var clips = new List<string>();
            Application.LogCallback hook = (msg, trace, type) =>
            {
                if (msg.Contains("[HomePreview] CLIP")) clips.Add(msg);
            };
            Application.logMessageReceived += hook;
            var editionWas = Edition.Simulated;
            Edition.Simulate(EditionKind.All);
            bool guardWas = LifeSimManager.Guard;
            LifeSimManager.Guard = true;
            int clipsBefore = LifeHomePreview.clipCount;
            underButton = 0;
            try
            {
                int cur = LifeSimManager.CodeVersion;

                Case(outDir, "ok", report, () => Career("VINCE", 40));

                Case(outDir, "newer", report, () =>
                {
                    var s = JsonUtility.FromJson<LifeState>(Career("VINCE", 40));
                    s.saveVersion = s.writerVersion = cur + 1;
                    PlayerPrefs.SetString(LifeSimManager.SaveKey, JsonUtility.ToJson(s));
                    LifeSimManager.Reload();
                });

                Case(outDir, "rewritten", report, () => Rewritten(false),
                     after: () => { LifeSimManager.BeforeMigrateHook = null; LifeSimManager.SimulateCodeVersion(0); });

                // THIS BUILD'S COPY pressed, and the copy would not load: the
                // menu comes back with CONTINUE and says so (review 11-12).
                Case(outDir, "rewritten_refused", report, () => Rewritten(true),
                     after: () => { LifeSimManager.BeforeMigrateHook = null; LifeSimManager.SimulateCodeVersion(0); });

                Case(outDir, "failed", report, () =>
                {
                    Career("VINCE", 40);
                    PlayerPrefs.SetString(LifeSimManager.SaveKey, "{\"saveVersion\":" + cur + ",\"money\":12,\"cars\":[{\"id\":");
                    LifeSimManager.Reload();
                });

                // NEW GAME pressed on that title: the confirm a failed career
                // now gets (review 11-12), with its own two-line warning.
                Case(outDir, "failed_confirm", report, () =>
                {
                    Career("VINCE", 40);
                    PlayerPrefs.SetString(LifeSimManager.SaveKey, "{\"saveVersion\":" + cur + ",\"money\":12,\"cars\":[{\"id\":");
                    LifeSimManager.Reload();
                }, confirm: true);
            }
            finally
            {
                LifeSimManager.BeforeMigrateHook = null;
                LifeSimManager.SimulateCodeVersion(0);
                LifeSimManager.Guard = guardWas;
                LifeSimManager.UseScratchSave(Scratch);
                LifeSimManager.UseCareerSave();
                Edition.Simulate(editionWas);
                MenuKit.ScreenSizeOverride = Vector2.zero;
                Application.logMessageReceived -= hook;
            }
            int found = LifeHomePreview.clipCount - clipsBefore;
            report.AppendLine("CLIP TOTAL " + found);
            report.AppendLine("UNDER A BUTTON TOTAL " + underButton);
            foreach (var c in clips) report.AppendLine(c);
            File.WriteAllText(Path.Combine(outDir, "saveguard_report.txt"), report.ToString());
            Debug.Log("[SaveGuardPreview] CLIP TOTAL " + found + ", UNDER A BUTTON TOTAL " + underButton + "\n" + report);
        }

        /// <summary>As the v25 build the next round ships, with a stand-in v25
        /// step (Migrate has none above v24 yet): its own .last, then an exe
        /// from before the guard writes the career back. With
        /// <paramref name="refuse"/>, THIS BUILD'S COPY is then chosen and its
        /// upgrade throws (a simulated broken step), so it is refused.</summary>
        static void Rewritten(bool refuse)
        {
            int v25 = LifeSimManager.LastUnstampedVersion + 1;
            LifeSimManager.SimulateCodeVersion(v25);
            LifeSimManager.BeforeMigrateHook = s => { if (s.saveVersion < v25) s.saveVersion = v25; };
            Career("VINCE", 40);
            var st = LifeSimManager.State;
            st.saveVersion = v25;
            LifeRules.SleepUntilMorning(st);
            LifeSimManager.Save();
            int lastDay = st.day;
            var old = JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(LifeSimManager.SaveKey));
            old.day += 2;
            string stripped = Regex.Replace(JsonUtility.ToJson(old), "\"writerVersion\":-?\\d+,?", "");
            PlayerPrefs.SetString(LifeSimManager.SaveKey, stripped);
            LifeSimManager.Reload();
            if (!refuse) return;
            LifeSimManager.BeforeMigrateHook = s =>
            {
                if (s.day == lastDay) throw new System.InvalidOperationException("a broken step (simulated)");
                if (s.saveVersion < v25) s.saveVersion = v25;
            };
            if (LifeSimManager.UseLastCopy()) Debug.LogError("[SaveGuardPreview] the .last copy was not refused");
        }

        /// <summary>Texts found under a button's face this capture (Shoot).</summary>
        static int underButton;

        static Rect WorldRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y),
                                   Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
        }

        static string Career(string name, int day)
        {
            LifeSimManager.UseScratchSave(Scratch);
            LifeSimManager.StartNewGame(name, 25, LifeRules.DefaultJobIndex);
            LifeSimManager.State.day = day;
            LifeSimManager.Save();
            return PlayerPrefs.GetString(LifeSimManager.SaveKey);
        }

        static void Case(string outDir, string label, StringBuilder report, System.Action setup, System.Action after = null,
                         bool confirm = false)
        {
            try
            {
                setup();
                report.AppendLine(label + ": trouble " + LifeSimManager.Trouble + ", read-only " + LifeSimManager.ReadOnly +
                                  ", .last offered " + LifeSimManager.HasLastCopy);
                report.AppendLine("  note: " + (LifeSimManager.TroubleTitle() ?? "(none)"));
                foreach (var size in Sizes) Shoot(outDir, label, size, report, confirm);
            }
            catch (System.Exception e)
            {
                report.AppendLine(label + " THREW " + e);
                Debug.LogError("[SaveGuardPreview] " + label + " threw: " + e);
            }
            finally { after?.Invoke(); }
        }

        static void Shoot(string outDir, string label, (string name, int w, int h) size, StringBuilder report,
                          bool confirm = false)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("PreviewCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // A hazy Blue Ridge sky behind the menu, not a black void: the
            // notes are read over scenery in the game.
            cam.backgroundColor = new Color(0.43f, 0.52f, 0.60f);
            cam.orthographic = true;
            var rt = new RenderTexture(size.w, size.h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            MenuKit.ScreenSizeOverride = new Vector2(size.w, size.h);

            var host = new GameObject("Title");
            var title = host.AddComponent<TitleScreen>();
            var build = typeof(TitleScreen).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
            if (build == null) { Debug.LogError("[SaveGuardPreview] no BuildUI()"); return; }
            build.Invoke(title, null);
            // The fade starts black and lifts over two seconds; take it away.
            var fade = typeof(TitleScreen).GetField("fade", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(title) as Image;
            if (fade != null) fade.color = new Color(0f, 0f, 0f, 0f);
            if (confirm)
            {
                // NEW GAME pressed: Confirm() clears the buttons with Destroy,
                // which does nothing outside play mode - clear them here first.
                var root = typeof(TitleScreen).GetField("root", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(title) as RectTransform;
                var confirmM = typeof(TitleScreen).GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance);
                if (root == null || confirmM == null) { Debug.LogError("[SaveGuardPreview] no root / Confirm()"); return; }
                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
                confirmM.Invoke(title, null);
            }

            foreach (var c in Object.FindObjectsByType<Canvas>())
            {
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 10f;
                var cs = c.GetComponent<CanvasScaler>();
                if (cs != null)
                {
                    cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    cs.scaleFactor = size.h / MenuKit.DesignHeight;
                }
            }
            Canvas.ForceUpdateCanvases();
            LifeHomePreview.ReportClippedText(cam, "title_" + label + "/" + size.name, size.w, size.h);

            var labels = new List<string>();
            foreach (var b in Object.FindObjectsByType<Button>())
            {
                var t = b.GetComponentInChildren<Text>();
                if (t == null) continue;
                // A button caption wider than its button reads as clipped too.
                float bw = ((RectTransform)b.transform).rect.width;
                labels.Add(t.text + (t.preferredWidth > bw - 8f ? " [WIDER THAN ITS BUTTON: " + t.preferredWidth.ToString("0") + " > " + bw.ToString("0") + "]" : ""));
            }
            report.AppendLine("  " + size.name + " buttons: " + string.Join(" | ", labels));

            // TEXT UNDER A BUTTON'S FACE. The CLIP report compares text with
            // text, and the failed confirm's second line hid behind YES's
            // image while it read 0 (11-12): every text that is not a
            // button's own caption is checked against every button's rect.
            var buttons = Object.FindObjectsByType<Button>();
            foreach (var t in Object.FindObjectsByType<Text>())
            {
                if (string.IsNullOrEmpty(t.text) || t.GetComponentInParent<Button>() != null) continue;
                var tr = WorldRect(t.rectTransform);
                foreach (var b in buttons)
                {
                    if (!tr.Overlaps(WorldRect((RectTransform)b.transform))) continue;
                    underButton++;
                    var cap = b.GetComponentInChildren<Text>();
                    report.AppendLine("  " + size.name + " TEXT UNDER A BUTTON: \"" +
                                      (t.text.Length > 40 ? t.text.Substring(0, 40) + "..." : t.text) + "\" under " +
                                      (cap != null ? cap.text : b.name));
                }
            }

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size.w, size.h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size.w, size.h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(outDir, "title_" + label + "_" + size.name + ".png");
            ShotSidecar.WritePng(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            MenuKit.ScreenSizeOverride = Vector2.zero;
        }
    }
}
