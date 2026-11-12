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
    /// (this build's copy or the older build's), and one that would not load.
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

                Case(outDir, "rewritten", report, () =>
                {
                    // As the v25 build the next round ships: its own .last,
                    // then an exe from before the guard writes the career back.
                    LifeSimManager.SimulateCodeVersion(LifeSimManager.LastUnstampedVersion + 1);
                    Career("VINCE", 40);
                    var st = LifeSimManager.State;
                    st.saveVersion = LifeSimManager.LastUnstampedVersion + 1;
                    LifeRules.SleepUntilMorning(st);
                    LifeSimManager.Save();
                    var old = JsonUtility.FromJson<LifeState>(PlayerPrefs.GetString(LifeSimManager.SaveKey));
                    old.day += 2;
                    string stripped = Regex.Replace(JsonUtility.ToJson(old), "\"writerVersion\":-?\\d+,?", "");
                    PlayerPrefs.SetString(LifeSimManager.SaveKey, stripped);
                    LifeSimManager.Reload();
                }, after: () => LifeSimManager.SimulateCodeVersion(0));

                Case(outDir, "failed", report, () =>
                {
                    Career("VINCE", 40);
                    PlayerPrefs.SetString(LifeSimManager.SaveKey, "{\"saveVersion\":" + cur + ",\"money\":12,\"cars\":[{\"id\":");
                    LifeSimManager.Reload();
                });
            }
            finally
            {
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
            foreach (var c in clips) report.AppendLine(c);
            File.WriteAllText(Path.Combine(outDir, "saveguard_report.txt"), report.ToString());
            Debug.Log("[SaveGuardPreview] CLIP TOTAL " + found + "\n" + report);
        }

        static string Career(string name, int day)
        {
            LifeSimManager.UseScratchSave(Scratch);
            LifeSimManager.StartNewGame(name, 25, LifeRules.DefaultJobIndex);
            LifeSimManager.State.day = day;
            LifeSimManager.Save();
            return PlayerPrefs.GetString(LifeSimManager.SaveKey);
        }

        static void Case(string outDir, string label, StringBuilder report, System.Action setup, System.Action after = null)
        {
            try
            {
                setup();
                report.AppendLine(label + ": trouble " + LifeSimManager.Trouble + ", read-only " + LifeSimManager.ReadOnly +
                                  ", .last offered " + LifeSimManager.HasLastCopy);
                report.AppendLine("  note: " + (LifeSimManager.TroubleTitle() ?? "(none)"));
                foreach (var size in Sizes) Shoot(outDir, label, size, report);
            }
            catch (System.Exception e)
            {
                report.AppendLine(label + " THREW " + e);
                Debug.LogError("[SaveGuardPreview] " + label + " threw: " + e);
            }
            finally { after?.Invoke(); }
        }

        static void Shoot(string outDir, string label, (string name, int w, int h) size, StringBuilder report)
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
