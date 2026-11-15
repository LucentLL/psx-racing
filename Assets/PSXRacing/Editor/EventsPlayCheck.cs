using System.Collections;
using System.IO;
using System.Linq;
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
    /// EVENTS (v29), played through the running game, headless - the
    /// ClubPlayCheck pattern. From a seeded save, in play mode:
    ///
    ///   A. SAT 2 MORNING: signed up for the ROOKIE CUP (a JAPANESE club member
    ///      in a Japanese car). The line's pre-race page is the event's; START
    ///      hands over closed roads (traffic NONE), a MORNING hour, the event's
    ///      field and its key. The venue loads; a P2 is posted and banked at
    ///      the house: the second prize, racing rep, the club's points, the
    ///      block spent.
    ///   B. FRI 8 NIGHT: signed up for the FRIDAY NIGHT RUN. START hands over
    ///      the NIGHT hour with traffic on (LIGHT/MEDIUM); a win is banked:
    ///      street rep, and the night race rolls the day without a no-show.
    ///
    /// The races are not driven: when the venue has loaded the check posts the
    /// result the race scene posts at the flag and returns to the house.
    ///   tools\events-play-check.ps1 -> PSXRacing_events_play_check.txt
    /// </summary>
    public static class EventsPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;

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
            LifeSimManager.StartNewGame("Events Check", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            S.money = 50000;
            S.health = 100f;
            S.debugMode = true;
            var spec = CarCatalog.All.FirstOrDefault(c => !c.IsRaceCar && !c.IsRallyCar && c.origin == "jpn" &&
                                                          CarSpec.ToHp(c.hp) <= 170 &&
                                                          PSXRacing.OnFoot.CarShell.DefFor(c) != null);
            Check(spec != null, "a Japanese car under 170 hp (the ROOKIE CUP and the FRIDAY NIGHT RUN take it)", spec?.name);
            if (spec == null) { Finish(); EditorApplication.Exit(1); return; }
            var car = CarMarket.MakeOwnedCar(S, spec, 92, 3000f, spec.price);
            car.fuel = 95f;
            S.activeCar = car.id;
            var jp = Clubs.ById("japanese");
            Clubs.Invite(S, jp, "check");
            Clubs.Join(S, jp);
            S.streetRep = 15f;
            S.jobFrom = Duty.Ord(400, 0);   // no shifts owed during the jumps
            bool a = Events.SignUp(S, Events.ById("rookie"), out string la);
            bool b = Events.SignUp(S, Events.ById("fridaynight"), out string lb);
            Check(a && b, "signed up for the ROOKIE CUP and the FRIDAY NIGHT RUN", la + " / " + lb);
            S.day = 2; S.slotIndex = LifeRules.MorningSlot;
            Duty.lastNotice = null;
            LifeSimManager.Save();

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
            new GameObject("EventsCheckRunner").AddComponent<EventsCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Section(string s) => log.AppendLine("--- " + s);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "EVENTS: SIGN-UP, SANCTIONED RACE, STREET RACE - ALL PLAYED." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_events_play_check.txt"),
                              log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class EventsCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => EventsPlayCheck.Check(ok, what, got);
        static LifeState S => LifeSimManager.State;

        static void Set(object o, string name, object v)
        {
            var f = typeof(LifeHomeScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Check(false, "LifeHomeScreen has a field " + name); return; }
            f.SetValue(o, v);
        }

        static void Rebuild(LifeHomeScreen screen) =>
            typeof(LifeHomeScreen).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(screen, null);

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

        static bool TextOnScreen(string text) =>
            Object.FindObjectsByType<Text>().Any(t => t.isActiveAndEnabled && t.text == text);

        IEnumerator WaitScene(int idx, float seconds = 150f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().buildIndex != idx && Time.realtimeSinceStartup - t0 < seconds)
                yield return null;
            for (int i = 0; i < 20; i++) yield return null;
        }

        IEnumerator Seconds(float s)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < s) yield return null;
        }

        LifeHomeScreen screen;
        IEnumerator Home()
        {
            SceneManager.LoadScene(0);
            yield return WaitScene(0);
            screen = null;
            float t0 = Time.realtimeSinceStartup;
            while (screen == null && Time.realtimeSinceStartup - t0 < 20f)
            {
                screen = Object.FindAnyObjectByType<LifeHomeScreen>();
                yield return null;
            }
            for (int i = 0; i < 10; i++) yield return null;
        }

        /// <summary>The line's pre-race page, START pressed; returns the venue
        /// it handed over, or -1.</summary>
        IEnumerator StartAtLine(string title, System.Action<int> got)
        {
            yield return Home();
            Set(screen, "raceFromLine", true);
            Set(screen, "tab", "prerace");
            Rebuild(screen);
            yield return null; yield return null;
            Check(TextOnScreen(title), "the line opens the event's pre-race page, \"" + title + "\"");
            var start = FindButton("START");
            Check(start != null, "with START live", start != null ? start.GetComponentInChildren<Text>().text : "none");
            if (start == null) { got(-1); yield break; }
            start.onClick.Invoke();
            got(RaceHandoff.TrackIndex);
        }

        IEnumerator Race(int venue, System.Action post)
        {
            yield return WaitScene(TrackCatalog.SceneIndex(venue));
            Check(SceneManager.GetActiveScene().buildIndex == TrackCatalog.SceneIndex(venue), "the venue loads",
                  SceneManager.GetActiveScene().name);
            yield return Seconds(2f);
            post();
            RaceHandoff.ResultReady = true;
            yield return Home();
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 30; i++) yield return null;
            var rookie = Events.ById("rookie");
            var fri = Events.ById("fridaynight");

            // ================= A. SAT 2 MORNING: the ROOKIE CUP =================
            EventsPlayCheck.Section("A. SAT 2 MORNING: the ROOKIE CUP, race 1, from the line");
            int venue = -1;
            yield return StartAtLine("ROOKIE · RACE 1 OF 2", v => venue = v);
            var e = Events.EntryFor(S, rookie, 0);
            Check(venue == Events.VenueOf(rookie, 0) && RaceHandoff.EventKey == "rookie#0" && RaceHandoff.EventRace == 0 &&
                  e != null && e.inFlight == 0, "START hands over the event's road and key; committed at the line",
                  venue >= 0 ? TrackCatalog.At(venue).name : "none");
            Check(RaceHandoff.TrafficLevel == TrafficLevels.None, "a sanctioned race: closed roads, traffic NONE",
                  TrafficLevels.Name(RaceHandoff.TrafficLevel));
            Check(TimeOfDay.InSlot(RaceHandoff.TimeOfDayIndex, LifeRules.MorningSlot), "in the MORNING block",
                  TimeOfDay.Label(RaceHandoff.TimeOfDayIndex));
            var field = Events.FieldFor(rookie, 0, 0);
            int handed = string.IsNullOrEmpty(RaceHandoff.OpponentSpecIds) ? 0 : RaceHandoff.OpponentSpecIds.Split(';').Length;
            Check(handed == field.cars.Count && handed >= 3, "the field on the grid is the event's (~" + field.roughHp + " hp)", handed);
            if (venue < 0) { Done(); yield break; }
            int money = S.money;
            float rep = S.racingRep, street = S.streetRep;
            int clubPts = Events.PointsOf(S, "japanese");
            yield return Race(venue, () =>
            {
                RaceHandoff.FinishPos = 2; RaceHandoff.FieldSize = 8;
                RaceHandoff.MetersDriven = 3000f; RaceHandoff.RaceTimeSeconds = 240f;
            });
            Check(Events.PlaceOf(e, 0) == 2 && e.inFlight == -1, "the result is banked at the house: P2", Events.PlaceOf(e, 0));
            Check(S.money - money == rookie.prizes[1], "the second prize is paid", S.money - money);
            Check(Mathf.Approximately(S.racingRep - rep, Events.RacingRepGain[1]) && Mathf.Approximately(S.streetRep, street),
                  "racing rep +" + Events.RacingRepGain[1] + ", street rep untouched", S.racingRep);
            Check(Events.PointsOf(S, "japanese") - clubPts == Events.ClubPoints[1],
                  "the JAPANESE club scores its points for the member's P2 in a Japanese car", Events.PointsOf(S, "japanese"));
            Check(S.day == 2 && S.slotIndex == LifeRules.NoonSlot && !LifeRules.RacedToday(S),
                  "the race spent its block, not the purse race's daily cap",
                  LifeRules.DateLabel(S.day) + " " + LifeRules.SlotNames[S.slotIndex]);

            // ================= B. FRI 8 NIGHT: the FRIDAY NIGHT RUN =================
            EventsPlayCheck.Section("B. FRI 8 NIGHT: the FRIDAY NIGHT RUN, race 1, from the line");
            S.day = 8; S.slotIndex = LifeRules.NightSlot;
            S.ActiveCar.fuel = 95f;
            S.lastAnyRaceDay = S.day;
            LifeSimManager.Save();
            venue = -1;
            yield return StartAtLine("FRI RUN · RACE 1 OF 2", v => venue = v);
            var f = Events.EntryFor(S, fri, 0);
            Check(venue == Events.VenueOf(fri, 0) && RaceHandoff.EventKey == "fridaynight#0" && f != null && f.inFlight == 0,
                  "START hands over the street race", venue >= 0 ? TrackCatalog.At(venue).name : "none");
            Check(RaceHandoff.TrafficLevel >= TrafficLevels.Light && RaceHandoff.TrafficLevel <= TrafficLevels.Medium,
                  "a street race: the night's traffic is on (LIGHT or MEDIUM)", TrafficLevels.Name(RaceHandoff.TrafficLevel));
            Check(RaceHandoff.TimeOfDayIndex == TimeOfDay.Night, "at NIGHT", TimeOfDay.Label(RaceHandoff.TimeOfDayIndex));
            if (venue < 0) { Done(); yield break; }
            street = S.streetRep;
            rep = S.racingRep;
            money = S.money;
            yield return Race(venue, () =>
            {
                RaceHandoff.FinishPos = 1; RaceHandoff.FieldSize = 8;
                RaceHandoff.MetersDriven = 2500f; RaceHandoff.RaceTimeSeconds = 180f;
            });
            Check(Events.PlaceOf(f, 0) == 1 && f.prize[0] == fri.prizes[0], "a win, banked: the first prize",
                  S.money - money);
            Check(Mathf.Approximately(S.streetRep - street, Events.StreetRepGain[0]),
                  "street rep +" + Events.StreetRepGain[0], S.streetRep);
            Check(S.day == 9 && S.slotIndex == LifeRules.DawnSlot && Events.PlaceOf(f, 0) == 1,
                  "the NIGHT race rolled the day; banked first, so it is no no-show",
                  LifeRules.DateLabel(S.day) + " " + LifeRules.SlotNames[S.slotIndex]);
            Check(S.evNews.Count > 0, "the results strip has the race", S.evNews.LastOrDefault());
            LifeSimManager.Save();
            Done();
        }

        static void Done()
        {
            EventsPlayCheck.Finish();
            EditorApplication.Exit(EventsPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
