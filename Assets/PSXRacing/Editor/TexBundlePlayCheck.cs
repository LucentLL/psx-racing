using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// TEXTURES ENHANCED / CLASSIC in PLAY MODE, both ways the twins load
    /// (2026-10-11): the editor's own (EnhancedSrc), then the player's (the
    /// StreamingAssets bundle over UnityWebRequest), then a fetch that fails.
    /// At one venue's start (PSX_TEXPC_VENUE, GillespieGap by default), with
    /// the world frozen so the two shots are the same frame:
    ///   A. EnhancedSrc: ENHANCED loads twins, CLASSIC lets them all go.
    ///   B. the bundle: ENHANCED stays CLASSIC while it downloads, swaps when
    ///      it lands (shot), CLASSIC restores and unloads it (shot).
    ///   C. a missing bundle: stays CLASSIC, warns once, retries quietly.
    /// The two shots side by side (CLASSIC | ENHANCED) go to PSX_TEXPC_OUT
    /// (a .jpg path); the report to PSXRacing_texbundle_play_check.txt.
    /// The player's TEXTURES choice is put back as it was.
    ///
    ///   -executeMethod PSXRacing.EditorTools.TexBundlePlayCheck.Run (no -quit)
    /// </summary>
    public static class TexBundlePlayCheck
    {
        internal static readonly StringBuilder log = new StringBuilder();
        internal static int failures;
        const string Report = "PSXRacing_texbundle_play_check.txt";

        public static void Run()
        {
            log.Clear();
            failures = 0;
            string id = System.Environment.GetEnvironmentVariable("PSX_TEXPC_VENUE");
            if (string.IsNullOrEmpty(id)) id = "GillespieGap";
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;
            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            log.AppendLine("texture bundle play check at " + id + ":");
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                Done();
                return;
            }
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Morning;
            var cars = CarCatalog.All;
            RaceHandoff.CarSpecId = cars[20].id;
            RaceHandoff.OpponentSpecIds = cars[21].id + ";" + cars[22].id + ";" + cars[23].id;
            RaceHandoff.OpponentSkills = "1.0;0.95;0.9";
            RaceHandoff.Solo = true;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TexBundlePlayCheckRunner").AddComponent<TexBundlePlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  " + what);

        internal static void Done()
        {
            log.AppendLine(failures == 0 ? "TEXBUNDLE CHECK OK." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, Report), log.ToString());
            Debug.Log(log.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    public class TexBundlePlayCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => TexBundlePlayCheck.Check(ok, what, got);
        static void Note(string what) => TexBundlePlayCheck.Note(what);

        Material probe;
        Texture probeOrig;

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            const string key = "psx.texEnhanced";
            int prefWas = PlayerPrefs.GetInt(key, -1);
            int warnings = 0;
            Application.LogCallback count = (msg, st, type) =>
            {
                if (type == LogType.Warning && msg.StartsWith("[TexTier] ENHANCED textures unavailable")) warnings++;
            };
            Application.logMessageReceived += count;
            try
            {
                // Settle, then freeze: the same frame for every shot.
                for (float t = 0; t < 4f; t += Time.unscaledDeltaTime) yield return null;
                Time.timeScale = 0f;
                yield return null;

                TextureTier.ForceBundle = false;
                TextureTier.UrlOverride = null;
                TexturePrefs.Enhanced = false;
                yield return null;
                FindProbe();
                Check(probe != null, "a material on screen wears a mapped original", probe ? probe.name + " / " + probeOrig.name : null);

                // A. The editor's path: straight from EnhancedSrc.
                TexturePrefs.Enhanced = true;
                Check(TextureTier.LoadedCount > 0, "A EnhancedSrc: ENHANCED loads twins at once", TextureTier.LoadedCount);
                if (probe) Check(probe.mainTexture != probeOrig, "A EnhancedSrc: the probe wears its twin", probe.mainTexture ? probe.mainTexture.name : "null");
                TexturePrefs.Enhanced = false;
                Check(TextureTier.LoadedCount == 0, "A EnhancedSrc: CLASSIC lets every twin go", TextureTier.LoadedCount);
                if (probe) Check(probe.mainTexture == probeOrig, "A EnhancedSrc: the probe wears its original again");

                // B. The player's path: the bundle from StreamingAssets.
                TextureTier.ForceBundle = true;
                Note("bundle url " + TextureTier.BundleUrl);
                yield return null;
                var classic = Shot();
                yield return classic;
                TexturePrefs.Enhanced = true;
                Check(TextureTier.LoadedCount == 0 && TextureTier.BundleState == "Loading",
                      "B bundle: CLASSIC while it downloads", TextureTier.BundleState + ", " + TextureTier.LoadedCount + " twins");
                if (probe) Check(probe.mainTexture == probeOrig, "B bundle: the probe keeps its original meanwhile");
                float waited = 0f;
                while (TextureTier.BundleState == "Loading" && waited < 60f) { waited += Time.unscaledDeltaTime; yield return null; }
                Check(TextureTier.BundleState == "Ready" && TextureTier.BundleLoaded, "B bundle: it lands", $"{TextureTier.BundleState} after {waited:F1} s");
                Check(TextureTier.LoadedCount > 0, "B bundle: twins swapped in when it lands", TextureTier.LoadedCount);
                if (probe) Check(probe.mainTexture != probeOrig, "B bundle: the probe wears its twin", probe.mainTexture ? probe.mainTexture.name + " " + probe.mainTexture.width + "x" + probe.mainTexture.height : "null");
                yield return null;
                var enhanced = Shot();
                yield return enhanced;
                int twins = TextureTier.LoadedCount;
                TexturePrefs.Enhanced = false;
                Check(TextureTier.LoadedCount == 0 && !TextureTier.BundleLoaded && TextureTier.BundleState == "None",
                      "B bundle: CLASSIC restores and unloads the bundle", $"{twins} -> {TextureTier.LoadedCount} twins, bundle {(TextureTier.BundleLoaded ? "loaded" : "unloaded")}, {TextureTier.BundleState}");
                if (probe) Check(probe.mainTexture == probeOrig, "B bundle: the probe wears its original again");
                yield return null;
                var back = Shot();
                yield return back;

                Compose(classic.tex, enhanced.tex, back.tex);

                // C. The offline / 404 case.
                TextureTier.UrlOverride = "file:///" + Path.Combine(Directory.GetParent(Application.dataPath).FullName, "no-such-dir", TextureTier.BundleFile).Replace('\\', '/');
                TexturePrefs.Enhanced = true;
                waited = 0f;
                while (TextureTier.BundleState == "Loading" && waited < 20f) { waited += Time.unscaledDeltaTime; yield return null; }
                Check(TextureTier.BundleState == "Failed" && TextureTier.LoadedCount == 0, "C missing bundle: stays CLASSIC", TextureTier.BundleState);
                if (probe) Check(probe.mainTexture == probeOrig, "C missing bundle: the probe keeps its original");
                TexturePrefs.Enhanced = false;
                TexturePrefs.Enhanced = true;   // picked again: one more try, no second warning
                waited = 0f;
                while (TextureTier.BundleState == "Loading" && waited < 20f) { waited += Time.unscaledDeltaTime; yield return null; }
                Check(TextureTier.BundleState == "Failed", "C missing bundle: picking ENHANCED again retries", TextureTier.BundleState);
                Check(warnings == 1, "C missing bundle: warned exactly once", warnings);
                TexturePrefs.Enhanced = false;
            }
            finally
            {
                Application.logMessageReceived -= count;
                TextureTier.ForceBundle = false;
                TextureTier.UrlOverride = null;
                Time.timeScale = 1f;
                if (prefWas < 0) PlayerPrefs.DeleteKey(key); else PlayerPrefs.SetInt(key, prefWas);
                PlayerPrefs.Save();
            }
            TexBundlePlayCheck.Done();
        }

        /// <summary>The first renderer in view whose material wears a mapped
        /// original on _MainTex.</summary>
        void FindProbe()
        {
            var map = TextureTier.Map;
            if (map == null) return;
            var originals = new System.Collections.Generic.HashSet<Texture>();
            foreach (var p in map.pairs) if (p.original != null) originals.Add(p.original);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.isVisible) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_MainTex")) continue;
                    var t = m.mainTexture;
                    if (t != null && originals.Contains(t)) { probe = m; probeOrig = t; return; }
                }
            }
        }

        class Grab : CustomYieldInstruction
        {
            public Texture2D tex;
            public override bool keepWaiting => tex == null;
        }

        Grab Shot()
        {
            var g = new Grab();
            StartCoroutine(Capture(g));
            return g;
        }

        IEnumerator Capture(Grab g)
        {
            yield return new WaitForEndOfFrame();
            g.tex = ScreenCapture.CaptureScreenshotAsTexture();
        }

        static float Diff(Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height) return -1f;
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            long sum = 0;
            for (int i = 0; i < pa.Length; i += 7)
                sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
            return sum / (float)((pa.Length + 6) / 7 * 3);
        }

        /// <summary>CLASSIC | ENHANCED at half size, one JPG.</summary>
        static void Compose(Texture2D classic, Texture2D enhanced, Texture2D back)
        {
            float dEnh = Diff(classic, enhanced), dBack = Diff(classic, back);
            Note($"shots {classic.width}x{classic.height}: mean |CLASSIC - ENHANCED| {dEnh:F2}/255, |CLASSIC - CLASSIC again| {dBack:F2}/255");
            Check(dEnh > 0.5f, "the ENHANCED frame differs from the CLASSIC one", dEnh.ToString("F2"));
            Check(dBack >= 0f && dBack < 0.5f, "CLASSIC again matches the first CLASSIC frame", dBack.ToString("F2"));
            string outPath = System.Environment.GetEnvironmentVariable("PSX_TEXPC_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "texbundle_toggle.jpg");
            int w = classic.width / 2, h = classic.height / 2;
            var rt = RenderTexture.GetTemporary(w * 2 + 4, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w * 2 + 4, h, 0);
            Graphics.DrawTexture(new Rect(0, 0, w, h), classic);
            Graphics.DrawTexture(new Rect(w + 4, 0, w, h), enhanced);
            GL.PopMatrix();
            var outTex = new Texture2D(w * 2 + 4, h, TextureFormat.RGB24, false);
            outTex.ReadPixels(new Rect(0, 0, w * 2 + 4, h), 0, 0);
            outTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, outTex.EncodeToJPG(88));
            Note("shot " + outPath + " (left CLASSIC, right ENHANCED, both from the bundle path)");
            Object.Destroy(outTex);
        }
    }
}
