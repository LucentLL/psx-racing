using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Engine audio phase 2 in the RUNNING game (tools\engine-phase2-check.ps1):
    /// the on-demand family bundles, end to end in play mode, through the
    /// player's own loader path (EngineVoiceLibrary.ForceBundle, the bundles
    /// built for the editor's platform into PSX_ENGINE_URL - a WebGL bundle's
    /// AAC audio does not play in the Windows editor).
    ///   - the field's families are fetched before the countdown and every car
    ///     speaks through its OWN family, its takes decoded and not silent;
    ///   - a family that is slow to arrive (DebugSlowPack) gets the default
    ///     family as a quiet stand-in, then its own voice;
    ///   - a family that cannot be fetched (its bundle deleted) falls back to
    ///     the default at full level, without an exception;
    ///   - idle packs are trimmed to the allowance.
    /// Writes PSXRacing_engine_phase2_check.txt.
    /// </summary>
    public static class EnginePhase2PlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal const string Player = "mazda_rx_7_type_r__fd__j___91";
        internal const string RivalA = "nissan_240zg__hs30___71";          // i6_japanese_2
        internal const string RivalB = "dodge_charger_440_r_t__70";        // v8_american_classic_1 (slow)
        internal const string SlowPack = "v8_american_classic_1";
        internal const string DeadFamily = "v12_british";                  // bundle deleted by the .ps1

        [MenuItem("PSX Racing/Check Engine Bundles (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            log.AppendLine("ENGINE AUDIO PHASE 2 PLAY CHECK " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var scenes = EditorBuildSettings.scenes;
            int s = TrackCatalog.SceneIndex(0);
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + TrackCatalog.At(0).id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            log.AppendLine("  scene " + scenes[s].path);
            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = 0;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.CarSpecId = Player;
            RaceHandoff.OpponentSpecIds = RivalA + ";" + RivalB;
            RaceHandoff.OpponentSkills = "0.9;0.9";

            EngineVoiceLibrary.ForceBundle = true;
            EngineVoiceLibrary.UrlOverride = System.Environment.GetEnvironmentVariable("PSX_ENGINE_URL");
            EngineVoiceLibrary.DebugSlowPack = SlowPack;
            EngineVoiceLibrary.DebugSlowSeconds = 3f;
            log.AppendLine("  bundles from " + EngineVoiceLibrary.UrlOverride + " (slow: " + SlowPack + " 3 s, dead: " + DeadFamily + ")");

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("EnginePhase2Runner").AddComponent<EnginePhase2Runner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "ENGINE PHASE 2 CHECK PASSED." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_engine_phase2_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }

        /// <summary>The audible sources of one car, as "name:clip" pairs.</summary>
        internal static List<AudioSource> Sources(Component car) =>
            car.GetComponentsInChildren<AudioSource>(true).Where(a => a.clip != null && a.loop).ToList();

        /// <summary>Peak of a clip's decoded samples (0 = silent or unreadable).</summary>
        internal static float Peak(AudioClip c)
        {
            if (c == null || c.loadState != AudioDataLoadState.Loaded) return 0f;
            var d = new float[Mathf.Min(c.samples, 44100) * c.channels];
            if (!c.GetData(d, 0)) return 0f;
            float p = 0f;
            for (int i = 0; i < d.Length; i += 7) p = Mathf.Max(p, Mathf.Abs(d[i]));
            return p;
        }
    }

    public class EnginePhase2Runner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => EnginePhase2PlayCheck.Check(ok, what, got);
        static void Note(string what) => EnginePhase2PlayCheck.Note(what);

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var rm = RaceManager.Instance;
            Check(rm != null && rm.playerCar != null, "the scene has a race and a player car");
            if (rm == null || rm.playerCar == null) { Done(); yield break; }

            var cars = rm.allCars.Where(c => c != null && c.isActiveAndEnabled).ToList();
            var engines = cars.Select(c => c.GetComponent<EngineAudio>()).Where(e => e != null).ToList();
            Check(engines.Count >= 3, "the player and two rivals have an EngineAudio", engines.Count);

            // Sample every car's voice for 8 s: which family speaks, and when.
            var seen = new Dictionary<EngineAudio, List<string>>();
            foreach (var e in engines) seen[e] = new List<string>();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                foreach (var e in engines)
                {
                    string now = (e.LiveFamily ?? "-") + (e.StandingIn ? "(stand-in)" : "");
                    var l = seen[e];
                    if (l.Count == 0 || !l[l.Count - 1].EndsWith("=" + now))
                        l.Add((Time.realtimeSinceStartup - t0).ToString("0.00") + "s=" + now);
                }
                yield return null;
            }
            foreach (var e in engines)
            {
                var car = e.GetComponent<CarController>();
                string want = EngineVoiceLibrary.Resolve(car.activeSpec);
                Note((car == rm.playerCar ? "PLAYER " : "rival  ") + (car.activeSpec != null ? car.activeSpec.id : "?") +
                     " wants " + want + ": " + string.Join(" -> ", seen[e]));
                Check(e.LiveFamily == want && !e.StandingIn, "  speaks through its own family after 8 s", e.LiveFamily);
                var srcs = EnginePhase2PlayCheck.Sources(e);
                int loaded = srcs.Count(a => a.clip.loadState == AudioDataLoadState.Loaded);
                int playing = srcs.Count(a => a.isPlaying);
                Check(srcs.Count > 0 && loaded == srcs.Count && playing == srcs.Count,
                      "  every loop is decoded and playing", loaded + "/" + playing + "/" + srcs.Count);
                var idle = srcs.FirstOrDefault(a => a.gameObject.name == "snd_idle");
                float pk = idle != null ? EnginePhase2PlayCheck.Peak(idle.clip) : 0f;
                Check(pk > 0.01f, "  its idle take is not silent", pk.ToString("0.000"));
                if (want == EnginePhase2PlayCheck.SlowPack)
                    Check(seen[e].Any(x => x.Contains("(stand-in)")) && !seen[e][0].Contains("(stand-in)"),
                          "  the slow family waited, then the default stood in, then it spoke");
            }
            Note("library: " + EngineVoiceLibrary.Census());

            // A family that cannot be fetched: the default speaks at full level.
            var victim = engines.FirstOrDefault(e => e.GetComponent<CarController>() != rm.playerCar);
            bool threw = false;
            try { victim.SetFamily(EnginePhase2PlayCheck.DeadFamily); }
            catch (System.Exception ex) { threw = true; Note("SetFamily threw " + ex.Message); }
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 3f) yield return null;
            Check(!threw, "asking for an unfetchable family does not throw");
            Check(EngineVoiceLibrary.State(EnginePhase2PlayCheck.DeadFamily, EngineVoiceLibrary.Set.Exterior) == EngineVoiceLibrary.Avail.Failed,
                  "its bundle reports Failed", EngineVoiceLibrary.State(EnginePhase2PlayCheck.DeadFamily, EngineVoiceLibrary.Set.Exterior));
            Check(victim.LiveFamily == EngineVoiceLibrary.DefaultFamily && !victim.StandingIn,
                  "the car keeps a voice: the default family at full level", victim.LiveFamily);

            // Trim: ask for every other family; idle packs stay within the allowance.
            foreach (var p in EngineBundleBuilder.Plan())
                if (p.set == EngineVoiceLibrary.Set.Exterior) EngineVoiceLibrary.Request(p.family, p.set);
            float t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < 6f) yield return null;
            Note("after requesting every family: " + EngineVoiceLibrary.Census());
            Check(EngineVoiceLibrary.IdleLoaded <= EngineVoiceLibrary.IdleAllowance,
                  "idle packs are trimmed to the allowance", EngineVoiceLibrary.IdleLoaded + " <= " + EngineVoiceLibrary.IdleAllowance);

            yield return Spatial(rm.playerCar);
            yield return Cabin(rm.playerCar);
            Done();
        }

        /// <summary>ITEM 4: the cockpit set. In the seat the family's interior
        /// recordings play (2D), the exterior set is parked after the 0.25 s
        /// crossfade; back out, the exterior returns and the cabin goes.</summary>
        IEnumerator Cabin(CarController player)
        {
            var e = player.GetComponent<EngineAudio>();
            string fam = EngineVoiceLibrary.Resolve(player.activeSpec);
            int Count(bool interior) => e.GetComponentsInChildren<AudioSource>(true).Count(a =>
                a.loop && a.transform.parent != null && a.transform.parent.name.StartsWith(interior ? "snd_int_" : "snd_") &&
                (interior || !a.transform.parent.name.StartsWith("snd_int_")));
            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            float t = Time.realtimeSinceStartup, peak = 0;
            while (e.InsideMix < 1f && Time.realtimeSinceStartup - t < 6f) { peak = Mathf.Max(peak, Count(false) + Count(true)); yield return null; }
            float took = Time.realtimeSinceStartup - t;
            yield return Wait(0.3f);
            int ext = Count(false), inn = Count(true);
            Note($"COCKPIT cabin {e.CabinFamily} mix {e.InsideMix:0.00} after {took:0.00} s; sources ext {ext} int {inn} (peak during the fade {peak})");
            Check(e.CabinFamily == fam && e.InsideMix >= 1f, "cockpit: the family's interior set plays", e.CabinFamily);
            Check(ext == 0 && inn > 0, "inside, the exterior set is parked - one set running", ext + " ext / " + inn + " int");
            var ins = e.GetComponentsInChildren<AudioSource>(true).Where(a => a.loop && a.transform.parent != null && a.transform.parent.name.StartsWith("snd_int_")).ToList();
            Check(ins.All(a => a.spatialBlend == 0f), "the cabin set is 2D");
            Check(ins.All(a => a.clip != null && a.clip.loadState == AudioDataLoadState.Loaded && a.isPlaying), "every cabin loop is decoded and playing");
            var idle = ins.FirstOrDefault(a => a.gameObject.name == "snd_idle");
            Check(idle != null && EnginePhase2PlayCheck.Peak(idle.clip) > 0.01f, "the cabin idle take is not silent");

            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            t = Time.realtimeSinceStartup;
            while ((e.InsideMix > 0f || e.CabinFamily != null) && Time.realtimeSinceStartup - t < 3f) yield return null;
            took = Time.realtimeSinceStartup - t;
            ext = Count(false); inn = Count(true);
            Note($"CHASE   back after {took:0.00} s; sources ext {ext} int {inn}; live {e.LiveFamily}");
            Check(e.CabinFamily == null && ext > 0 && inn == 0 && e.LiveFamily == fam, "back out: the exterior set returns, the cabin is torn down");
            Check(took < 0.6f, "  ...within the crossfade", took.ToString("0.00") + " s");
        }

        static IEnumerator Wait(float s)
        {
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < s) yield return null;
        }

        /// <summary>ITEM 2: the player's own sounds follow the listener -
        /// mostly 2D from its chase rig, 2D in the seat, full 3D with the
        /// opponents' rolloff and doppler from a trackside replay lens.</summary>
        IEnumerator Spatial(CarController player)
        {
            var e = player.GetComponent<EngineAudio>();
            var tyre = player.GetComponentsInChildren<AudioSource>(true).FirstOrDefault(a => a.gameObject.name == "snd_skid");
            var turbo = player.GetComponentsInChildren<AudioSource>(true).FirstOrDefault(a => a.gameObject.name == "snd_turbo_spool");
            var wind = player.GetComponentsInChildren<AudioSource>(true).Where(a => a.gameObject.name.StartsWith("snd_wind")).ToList();
            string Snap()
            {
                var eng = e.GetComponentsInChildren<AudioSource>(true).Where(a => a.loop && a.clip != null && a.gameObject.name.StartsWith("snd_") &&
                                                                              a.transform.parent != null && a.transform.parent.name.StartsWith("snd_")).ToList();
                float b0 = eng.Count > 0 ? eng.Min(a => a.spatialBlend) : -1f, b1 = eng.Count > 0 ? eng.Max(a => a.spatialBlend) : -1f;
                float dop = eng.Count > 0 ? eng.Max(a => a.dopplerLevel) : -1f;
                float md = eng.Count > 0 ? eng.Max(a => a.minDistance) : -1f;
                return $"ear {e.CurrentEar} blend {e.CurrentBlend:0.00}; engine {eng.Count} src blend {b0:0.00}-{b1:0.00} doppler {dop:0.0} min {md:0}; " +
                       $"tyre {(tyre != null ? tyre.spatialBlend.ToString("0.00") + "/" + tyre.dopplerLevel.ToString("0.0") : "-")}; " +
                       $"turbo {(turbo != null ? turbo.spatialBlend.ToString("0.00") : "-")}; wind vol {(wind.Count > 0 ? wind.Max(w => w.volume).ToString("0.000") : "-")}";
            }
            bool EngineAt(float blend, float doppler) =>
                e.GetComponentsInChildren<AudioSource>(true).Where(a => a.loop && a.clip != null && a.transform.parent != null &&
                                                                     a.transform.parent.name.StartsWith("snd_") && !a.transform.parent.name.StartsWith("snd_int_"))
                 .All(a => Mathf.Abs(a.spatialBlend - blend) < 0.01f && Mathf.Abs(a.dopplerLevel - doppler) < 0.01f);

            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            yield return Wait(0.6f);
            Note("CHASE   " + Snap());
            Check(e.CurrentEar == EngineAudio.Ear.Chase && EngineAt(EngineAudio.ChaseBlend, 0f),
                  "chase view: the player's engine is mostly 2D (blend 0.35, no doppler)");
            Check(tyre == null || Mathf.Abs(tyre.spatialBlend - EngineAudio.ChaseBlend) < 0.01f, "  ...and so are its tyres");

            ChaseCamera.PreviewView(ChaseCamera.View.Cockpit);
            yield return Wait(0.6f);
            Note("COCKPIT " + Snap());
            Check(e.CurrentEar == EngineAudio.Ear.Cockpit && (tyre == null || tyre.spatialBlend <= EngineAudio.CockpitBlend + 0.001f),
                  "cockpit view: the player's sounds are 2D");

            ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            var rr = RaceReplay.Instance;
            float t = Time.realtimeSinceStartup;
            while (rr != null && !rr.Available && Time.realtimeSinceStartup - t < 25f) yield return null;
            Check(rr != null && rr.Available, "a replay is available to watch");
            if (rr == null || !rr.Available) yield break;
            rr.Begin();
            yield return Wait(0.8f);
            Note("REPLAY  " + Snap() + " (mode " + rr.Mode + ")");
            Check(e.CurrentEar == EngineAudio.Ear.Outside && EngineAt(1f, EngineAudio.OutsideDoppler),
                  "trackside replay lens: the player's engine is full 3D with the opponents' doppler (0.6)");
            Check(tyre == null || (tyre.spatialBlend > 0.99f && tyre.dopplerLevel > 0.99f), "  ...its tyres full 3D too");
            Check(turbo == null || turbo.spatialBlend > 0.99f, "  ...and its turbo");
            Check(wind.All(w => w.volume < 0.001f), "  ...and the driver's wind bed is silent");
            rr.End();
            yield return Wait(0.6f);
            Note("AFTER   " + Snap());
            Check(e.CurrentEar == EngineAudio.Ear.Chase, "back from the replay: chase again");
        }

        void Done()
        {
            EnginePhase2PlayCheck.Finish();
            EditorApplication.Exit(EnginePhase2PlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
