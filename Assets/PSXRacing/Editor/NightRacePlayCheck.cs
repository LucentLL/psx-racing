using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// A RACE AT NIGHT, booked and started in the running game (2026-10-04).
    ///
    /// Owner: "scheduling work and races only allows three [slots] (which
    /// ironically makes it impossible to choose a race at night for a street
    /// racing game)". The day is six blocks now (LifeRules.SlotNames). This
    /// plays the owner's case through the real screens, in the LifeHome scene:
    ///
    ///   * the planner on tonight's NIGHT block offers WRITE IT IN, and the
    ///     press writes a race at NIGHT into the diary;
    ///   * the clock walks AFTERNOON -> EVENING -> NIGHT (one block each);
    ///   * at the zone line (the pre-race page in launcher mode) START loads
    ///     the venue, and the race runs at NIGHT - the handoff and the sky.
    ///
    ///   tools\nightrace-play-check.ps1 -> PSXRacing_nightrace_play_check.txt
    /// </summary>
    public static class NightRacePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static int venue;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || !File.Exists(scenes[0].path))
            {
                Check(false, "the LifeHome scene is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            LifeSimManager.StartNewGame("Night Check", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            LifeRules.SeedFallbackCar(S);
            S.money = 50000;
            S.health = 100f;
            S.ActiveCar.fuel = 95f;
            S.slotIndex = LifeRules.AfternoonSlot;
            // A circuit every edition ships, built in this sandbox.
            venue = 0;
            S.trackIndex = venue;
            LifeSimManager.Save();
            Check(scenes.Length > TrackCatalog.SceneIndex(venue) &&
                  File.Exists(scenes[TrackCatalog.SceneIndex(venue)].path),
                  "the venue's scene is built", TrackCatalog.At(venue).name);

            RaceHandoff.ClearAll();
            EditorSceneManager.OpenScene(scenes[0].path);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("NightRaceCheckRunner").AddComponent<NightRaceCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "A RACE AT NIGHT: BOOKED, DRIVEN TO, STARTED, DARK." : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_nightrace_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class NightRaceCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => NightRacePlayCheck.Check(ok, what, got);

        static void Set(object o, string name, object v)
        {
            var f = typeof(LifeHomeScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Check(false, "LifeHomeScreen has a field " + name); return; }
            f.SetValue(o, f.FieldType.IsEnum ? System.Enum.Parse(f.FieldType, v.ToString()) : v);
        }

        static void Rebuild(LifeHomeScreen screen) =>
            typeof(LifeHomeScreen).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(screen, null);

        /// <summary>The live button whose caption starts with this text.</summary>
        static Button FindButton(string startsWith)
        {
            foreach (var b in Object.FindObjectsByType<Button>())
            {
                if (b == null || !b.isActiveAndEnabled) continue;
                var t = b.GetComponentInChildren<Text>();
                if (t != null && t.text.StartsWith(startsWith)) return b;
            }
            return null;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var S = LifeSimManager.State;
            LifeHomeScreen screen = null;
            float t0 = Time.realtimeSinceStartup;
            while (screen == null && Time.realtimeSinceStartup - t0 < 20f)
            {
                screen = Object.FindAnyObjectByType<LifeHomeScreen>();
                yield return null;
            }
            for (int i = 0; i < 10; i++) yield return null;
            Check(screen != null, "the house's menu is up");
            if (screen == null) { Done(); yield break; }
            S = LifeSimManager.State;

            // ---- the planner, on tonight's NIGHT block ----
            Set(screen, "tab", "main");
            Set(screen, "calView", "Day");
            Set(screen, "calClockDay", S.day);
            Set(screen, "calClockSlot", S.slotIndex);
            Set(screen, "calSelDay", S.day);
            Set(screen, "calSelSlot", LifeRules.NightSlot);
            Set(screen, "calVenue", NightRacePlayCheck.venue);
            Rebuild(screen);
            yield return null; yield return null;
            var write = FindButton("WRITE IT IN");
            Check(write != null, "tonight's NIGHT block offers WRITE IT IN");
            if (write == null) { Done(); yield break; }
            write.onClick.Invoke();
            yield return null;
            var bk = LifeRules.BookingAt(S, S.day, LifeRules.NightSlot);
            Check(bk != null, "the planner wrote a race into tonight's NIGHT block");
            Check(bk != null && LifeRules.BookingHour(bk) == TimeOfDay.Night,
                  "to run at NIGHT", bk != null ? TimeOfDay.Label(LifeRules.BookingHour(bk)) : "-");

            // ---- the evening goes by ----
            int day = S.day;
            LifeRules.SpendActivitySlot(S, LifeRules.ActDrive);    // AFTERNOON
            LifeRules.SpendActivitySlot(S, LifeRules.ActErrand);   // EVENING
            Check(S.day == day && S.slotIndex == LifeRules.NightSlot, "the clock reaches NIGHT the same day",
                  LifeRules.SlotNames[S.slotIndex]);
            LifeSimManager.Save();

            // ---- the zone line: the pre-race page in launcher mode ----
            Set(screen, "raceFromLine", true);
            Set(screen, "tab", "prerace");
            Rebuild(screen);
            yield return null; yield return null;
            var start = FindButton("START");
            Check(start != null, "the line offers START", start != null ? start.GetComponentInChildren<Text>().text : "none");
            if (start == null) { Done(); yield break; }
            int want = TrackCatalog.SceneIndex(NightRacePlayCheck.venue);
            start.onClick.Invoke();
            Check(RaceHandoff.TimeOfDayIndex == TimeOfDay.Night,
                  "START hands the race NIGHT", TimeOfDay.Label(RaceHandoff.TimeOfDayIndex));
            Check(LifeRules.BookingOn(S, day) == null, "and strikes the booking out as it sets off");

            t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().buildIndex != want && Time.realtimeSinceStartup - t0 < 90f)
                yield return null;
            Check(SceneManager.GetActiveScene().buildIndex == want, "the venue loads",
                  SceneManager.GetActiveScene().name);
            for (int i = 0; i < 60; i++) yield return null;
            Check(TimeOfDay.Current == TimeOfDay.Night, "and the race runs at NIGHT",
                  TimeOfDay.Label(TimeOfDay.Current));
            Check(TimeOfDay.At(TimeOfDay.Current).lightsOn, "headlights-on hour");
            Done();
        }

        static void Done()
        {
            NightRacePlayCheck.Finish();
            EditorApplication.Exit(NightRacePlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
