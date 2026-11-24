using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE LAB CHECK (2026-11-23): every LAB row flipped in a running game, its picture compared, and flipped back.
    ///
    ///   -perftour "stage=BlueRidge&amp;from=2400&amp;hour=night&amp;weather=rain&amp;lab=1[&amp;view=cockpit][&amp;labsnaps=1]"
    ///   -perftour "routes=trade&amp;from=1700&amp;hour=night&amp;lab=1"            (the city)
    ///
    /// At the tour's standing point (as probe=park), with the clock stopped (timeScale 0, the clouds paused), the
    /// main view is drawn into a texture (PSX's display blit included) each frame. Per row: the picture, the
    /// same picture again (the noise floor), the row flipped, the picture, the row flipped back, the picture.
    /// One line per row in &lt;tag&gt;_&lt;id&gt;_lab.txt:
    ///   PASS          an AT ONCE row: changed past the noise, and back to the baseline
    ///   NO CHANGE     nothing past the noise in this view / hour / weather (the row says what it needs)
    ///   NOT BACK      the flip back did not return the baseline (a half state): a failure
    ///   EXCEPTION     an error was logged while the row was flipped: a failure
    ///   PENDING OK    a NEXT LOAD row: chosen, its statics untouched, the picture unchanged (no half state)
    ///   BUILT IN / LOCKED   not flipped
    /// The tour ignores the owner's saved LAB choices and never writes them (LabSwitches.TestRun / Set(persist:
    /// false)). &amp;labsnaps=1 also writes each row's base and flipped frames (JPG).
    /// </summary>
    public static class LabCheck
    {
        /// <summary>Last frame's main view, small, as RGB bytes.</summary>
        public static Color32[] LabGrab(RenderTexture src, int w, int h, Texture2D into)
        {
            var small = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, small);
            var prev = RenderTexture.active;
            RenderTexture.active = small;
            into.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            into.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(small);
            return into.GetPixels32();
        }

        /// <summary>Mean absolute difference (0..255 a channel) and the share of pixels off by more than 8.</summary>
        public static void LabDiff(Color32[] a, Color32[] b, out float mean, out float share)
        {
            long sum = 0; int off = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                sum += d;
                if (d > 24) off++;
            }
            mean = a.Length > 0 ? sum / (3f * a.Length) : 0f;
            share = a.Length > 0 ? off * 100f / a.Length : 0f;
        }

        public static IEnumerator Run(string id, string tag, string weatherArg, bool snaps, System.Action<string, byte[]> post, System.Action<string> say)
        {
            var cam = Camera.main;
            if (cam == null) { say("LAB " + id + ": no main camera"); yield break; }
            const int W = 640, H = 360, Settle = 12;
            var full = new RenderTexture(1280, 720, PlatformProfile.PC ? 32 : 24, RenderTextureFormat.Default) { antiAliasing = 1, name = "LabCheckFull" };
            full.Create();
            var screen = new RenderTexture(1280, 720, 0, RenderTextureFormat.Default) { antiAliasing = 1, name = "LabCheckScreen" };
            screen.Create();
            var holder = new GameObject("PerfTourLab");
            holder.SetActive(false);
            var view = holder.AddComponent<LabCheckView>();
            view.cam = cam; view.rt = full; view.screen = screen;
            var outp = cam.GetComponent<PSXCameraOutput>();
            if (outp != null && outp.display != null) { view.blitMat = outp.display.material; view.blit = true; }
            view.Hold();
            holder.SetActive(true);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            RenderTexture Pic() => view.blit && view.blitMat != null ? screen : full;

            int errors = 0;
            var errText = new StringBuilder();
            void OnLog(string msg, string stack, LogType type)
            {
                if (type != LogType.Exception && type != LogType.Error) return;
                errors++;
                if (errText.Length < 300) errText.Append(msg.Length > 120 ? msg.Substring(0, 120) : msg).Append(" | ");
            }

            float wasScale = Time.timeScale; bool wasPaused = VolumetricClouds.Paused;
            for (int i = 0; i < 60; i++) yield return null;
            Time.timeScale = 0f; VolumetricClouds.Paused = true;
            for (int i = 0; i < 30; i++) yield return null;

            var rep = new StringBuilder();
            var p = cam.transform.position;
            rep.AppendLine($"LAB CHECK {id} {tag} at {p.x:0},{p.z:0} view {ChaseCamera.Current} hour {TimeOfDay.Current} weather {weatherArg} " +
                           $"{System.DateTime.Now:yyyy-MM-dd HH:mm}; test run {LabSwitches.TestRun} (saved choices ignored); picture {W}x{H} of 1280x720");
            rep.AppendLine("row | when | state -> flipped | base noise mean/share% | flipped mean/share% | back mean/share% | verdict");
            int pass = 0, fail = 0, nochange = 0;
            Application.logMessageReceived += OnLog;
            foreach (var r in LabSwitches.Rows())
            {
                string head = $"{r.id,-12} | {r.when,-8} | ";
                if (r.when == LabWhen.BuiltIn) { rep.AppendLine(head + "- | - | - | - | BUILT IN (not flipped)"); continue; }
                if (r.EnvHeld) { rep.AppendLine(head + r.StateWord(r.Chosen) + " | - | - | - | LOCKED by a variable (not flipped)"); continue; }
                errors = 0; errText.Length = 0;
                bool was = r.Chosen, liveWas = LabSwitches.Live(r);
                var b0 = LabGrab(Pic(), W, H, tex);
                for (int i = 0; i < Settle; i++) yield return null;
                var b1 = LabGrab(Pic(), W, H, tex);
                LabDiff(b0, b1, out float nM, out float nS);
                bool setOk;
                try { setOk = LabSwitches.Set(r, !was, false); }
                catch (System.Exception e) { setOk = false; errors++; errText.Append(e.Message); }
                for (int i = 0; i < Settle; i++) yield return null;
                var f = LabGrab(Pic(), W, H, tex);
                LabDiff(b1, f, out float fM, out float fS);
                bool liveFlipped = LabSwitches.Live(r);
                if (snaps)
                {
                    post(tag + "_" + id + "_lab_" + r.id + "_flip.jpg", tex.EncodeToJPG(88));
                }
                try { LabSwitches.Set(r, was, false); }
                catch (System.Exception e) { errors++; errText.Append(e.Message); }
                for (int i = 0; i < Settle; i++) yield return null;
                var bk = LabGrab(Pic(), W, H, tex);
                LabDiff(b1, bk, out float kM, out float kS);
                if (snaps) post(tag + "_" + id + "_lab_" + r.id + "_back.jpg", tex.EncodeToJPG(88));

                // past the noise: a mean over 0.15 + 3x the noise, or a share of pixels over 0.05% + 3x the noise's
                bool changed = fM > 0.15f + 3f * nM || fS > 0.05f + 3f * nS;
                bool back = kM <= 0.10f + 2f * nM && kS <= 0.05f + 2f * nS && LabSwitches.Live(r) == liveWas && r.Chosen == was;
                string verdict;
                if (errors > 0 || !setOk) { verdict = "EXCEPTION " + errText; fail++; }
                else if (!back) { verdict = "NOT BACK"; fail++; }
                else if (r.when == LabWhen.NextLoad)
                {
                    bool clean = liveFlipped == liveWas && !changed;
                    verdict = clean ? "PENDING OK (statics untouched until a load)" : "HALF STATE: a NEXT LOAD row changed the game at once";
                    if (clean) pass++; else fail++;
                }
                else if (changed) { verdict = "PASS"; pass++; }
                else { verdict = "NO CHANGE in this view (" + r.look + ")"; nochange++; }
                rep.AppendLine(head + $"{r.StateWord(was)} -> {r.StateWord(!was)} | {nM:0.00}/{nS:0.00} | {fM:0.00}/{fS:0.00} | {kM:0.00}/{kS:0.00} | {verdict}");
            }
            Application.logMessageReceived -= OnLog;
            rep.AppendLine($"SUMMARY {id}: pass {pass}, no change {nochange}, fail {fail}");
            post(tag + "_" + id + "_lab.txt", Encoding.UTF8.GetBytes(rep.ToString()));
            say("LAB " + id + ": pass " + pass + ", no change " + nochange + ", fail " + fail);
            foreach (var line in rep.ToString().Split('\n')) if (line.Length > 0) say("LAB| " + line.TrimEnd('\r'));

            Time.timeScale = wasScale; VolumetricClouds.Paused = wasPaused;
            Object.Destroy(holder);
            Object.Destroy(tex);
            full.Release(); screen.Release();
        }
    }

    /// <summary>The main view drawn into a texture every frame, the PSX display blit after it, waited for (as the
    /// perf probe's view): the check's frames.</summary>
    [DefaultExecutionOrder(20000)]
    public class LabCheckView : MonoBehaviour
    {
        public Camera cam;
        public RenderTexture rt, screen;
        public Material blitMat;
        public bool blit;
        RenderTexture was;
        bool held;
        public void Hold() { if (cam != null && !held) { was = cam.targetTexture; held = true; } }
        void OnDisable() { if (cam != null && held) cam.targetTexture = was; }
        void LateUpdate()
        {
            if (cam == null || rt == null) return;
            Hold();
            if (cam.targetTexture != rt) cam.targetTexture = rt;
            cam.Render();
            if (blit && blitMat != null && screen != null) Graphics.Blit(rt, screen, blitMat);
        }
    }
}
