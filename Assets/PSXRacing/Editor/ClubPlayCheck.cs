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
    /// CAR CLUBS AND THE ENFORCED WEEK, played through the running game
    /// (2026-11-11), headless. From a seeded save, in play mode:
    ///
    ///   A. SAT NIGHT (two clubs meet - a clash): the town's Eastside Lot holds
    ///      the clubs' cars; walking up to a Japanese car opens the club page
    ///      and JOIN signs the player in (in a Japanese car); a Future car
    ///      refuses. Pulling into the lot signs in the meets: the Future meet,
    ///      in the wrong car, is a recorded miss - one club kept, one let down.
    ///   B. SUN EVENING: the club race at the line in the WRONG car is a miss.
    ///   C. MON AFTERNOON shift: Tony's counter hands over a CAR-MEET ORDER,
    ///      announced (cue), driven to the venue through the junction hop, and
    ///      judged at the door: fast and clean, an invitation.
    ///   D. SAT NIGHT again, in a car both clubs take: both meets signed in.
    ///   E. SUN EVENING: the club race launched from the line's pre-race page,
    ///      the result banked, the board moved, the block spent, the rest of the
    ///      board raced at the rollover.
    ///
    /// The races themselves are not driven: when the venue has loaded the
    /// check posts the result the race scene posts at the flag (RaceHandoff)
    /// and returns to the house, which banks it - the same apply-back a
    /// finished race reaches.
    ///
    ///   tools\club-play-check.ps1 -> PSXRacing_club_play_check.txt
    /// </summary>
    public static class ClubPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static string jpCar, usCar, fuCar, bothCar;

        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || !File.Exists(scenes[0].path) || TrackCatalog.TownSceneIndex <= 0)
            {
                Check(false, "the LifeHome and Town scenes are built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            LifeSimManager.StartNewGame("Club Check", 24, LifeRules.DefaultJobIndex);
            var S = LifeSimManager.State;
            S.money = 50000;
            S.health = 100f;
            S.debugMode = true;   // garage room for four cars
            S.garageSlots = 6;
            var jp = Clubs.ById("japanese"); var fu = Clubs.ById("future"); var am = Clubs.ById("american");
            CarSpec Pick(System.Func<CarSpec, bool> f) =>
                CarCatalog.All.FirstOrDefault(c => !c.IsRaceCar && !c.IsRallyCar && PSXRacing.OnFoot.CarShell.DefFor(c) != null && f(c));
            var jpSpec = Pick(c => jp.Fits(c) && c.modelYear < 1999);
            var usSpec = Pick(c => am.Fits(c) && c.modelYear < 1999);
            var fuSpec = Pick(c => fu.Fits(c) && !jp.Fits(c));
            var bothSpec = Pick(c => jp.Fits(c) && fu.Fits(c));
            OwnedCar Make(CarSpec spec) { var c = CarMarket.MakeOwnedCar(S, spec, 90, 3000f, spec.price); c.fuel = 95f; return c; }
            jpCar = Make(jpSpec).id; usCar = Make(usSpec).id; fuCar = Make(fuSpec).id; bothCar = Make(bothSpec).id;
            Check(jpSpec != null && usSpec != null && fuSpec != null && bothSpec != null,
                  "cars for the run: Japanese, American, a 1999 non-Japanese, a 1999 Japanese",
                  jpSpec?.name + " / " + usSpec?.name + " / " + fuSpec?.name + " / " + bothSpec?.name);
            // A member of the FUTURE club already (by invitation), owed from SAT.
            Clubs.Invite(S, fu, "check");
            Clubs.Join(S, fu);
            Clubs.StandingOf(S, fu).from = Duty.Ord(2, 0);
            // Shifts owed from MONDAY's (phase C), not the weekend's jumps.
            S.jobFrom = Duty.Ord(4, 0);
            S.day = 2; S.slotIndex = LifeRules.NightSlot;
            S.activeCar = jpCar;
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
            new GameObject("ClubCheckRunner").AddComponent<ClubCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Section(string s) => log.AppendLine("--- " + s);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "CAR CLUBS: LOT, JOIN, SIGN-IN, WRONG CAR, CLASH, MEET ORDER, CLUB RACE - ALL PLAYED."
                                         : failures + " FAILURE(S).");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_club_play_check.txt"),
                log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class ClubCheckRunner : MonoBehaviour
    {
        static void Check(bool ok, string what, object got = null) => ClubPlayCheck.Check(ok, what, got);
        static LifeState S => LifeSimManager.State;

        static void Set(object o, string name, object v)
        {
            var f = typeof(LifeHomeScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Check(false, "LifeHomeScreen has a field " + name); return; }
            f.SetValue(o, f.FieldType.IsEnum ? System.Enum.Parse(f.FieldType, v.ToString()) : v);
        }

        static void Rebuild(LifeHomeScreen screen) =>
            typeof(LifeHomeScreen).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(screen, null);

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

        IEnumerator WaitScene(int idx, float seconds = 120f)
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

        /// <summary>Into the town the way the house sends you (StartShops).</summary>
        IEnumerator GoToTown()
        {
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.FreeRoam = true;
            RaceHandoff.CarId = S.activeCar;
            RaceHandoff.CarSpecId = S.ActiveCar.specId;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.ForSlot(S.slotIndex, S.day);
            RaceHandoff.StartFuelPct = S.ActiveCar.fuel;
            LifeHomeScreen.FillCarRequestFor(S);
            LifeSimManager.Save();
            SceneManager.LoadScene(TrackCatalog.TownSceneIndex);
            yield return WaitScene(TrackCatalog.TownSceneIndex);
            yield return Seconds(1.5f);
        }

        static OnFoot.FootTarget[] ClubTargets() =>
            Object.FindObjectsByType<OnFoot.FootTarget>().Where(t => t != null && t.gameObject.name == "ClubCarTarget").ToArray();

        /// <summary>Drive (teleport) into the lot's entrance and let the town's
        /// own cue notice, the way a player arriving does.</summary>
        IEnumerator PullIntoLot(PSXRacing.Town.TownWorld world)
        {
            if (world.player != null && world.meetSign != null)
                world.player.ResetTo(world.meetSign.position + world.meetSign.forward * 4f + Vector3.up * 0.4f,
                                     world.meetSign.rotation);
            yield return Seconds(2f);
        }

        IEnumerator Race(System.Action post)
        {
            // The race scene is up: post the result it posts at the flag, and
            // go home, where the apply-back banks it.
            yield return Seconds(2f);
            post();
            RaceHandoff.ResultReady = true;
            yield return Home();
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            var jp = Clubs.ById("japanese"); var fu = Clubs.ById("future");
            for (int i = 0; i < 30; i++) yield return null;

            // ================= A. SAT NIGHT: the lot, JOIN, the clash =================
            ClubPlayCheck.Section("A. SAT 2 NIGHT, in a Japanese car: the lot, JOIN, sign-in, the clash");
            yield return GoToTown();
            var world = Object.FindAnyObjectByType<PSXRacing.Town.TownWorld>();
            Check(world != null, "the town is up", SceneManager.GetActiveScene().name);
            if (world == null) { Done(); yield break; }
            var targets = ClubTargets();
            int jpCars = targets.Count(t => t.title.Contains("JAPANESE CLUB"));
            int fuCars = targets.Count(t => t.title.Contains("FUTURE CLUB"));
            Check(jpCars == PSXRacing.Town.TownWorld.ClubLotCars && fuCars == PSXRacing.Town.TownWorld.ClubLotCars,
                  "both clubs meeting tonight have their cars in the Eastside Lot", jpCars + " Japanese, " + fuCars + " Future");
            var meetCars = Object.FindObjectsByType<OnFoot.FootTarget>().Count(t => t.gameObject.name == "MeetCarTarget");
            Check(meetCars + targets.Length <= world.meetSpots.Length,
                  "and the regular meet's crowd gives up their stalls", meetCars + " + " + targets.Length + " of " + world.meetSpots.Length);

            var fuTarget = targets.FirstOrDefault(t => t.title.Contains("FUTURE CLUB"));
            Check(fuTarget != null && fuTarget.action == "",
                  "a Future car: the player is already a member, nothing to press", fuTarget?.detail);
            var jpTarget = targets.FirstOrDefault(t => t.title.Contains("JAPANESE CLUB"));
            Check(jpTarget != null && jpTarget.onUse != null && jpTarget.verb == "TALK",
                  "a Japanese car offers TALK ABOUT JOINING", jpTarget?.title + " / " + jpTarget?.detail);
            if (jpTarget == null || jpTarget.onUse == null) { Done(); yield break; }
            jpTarget.onUse();
            yield return null; yield return null;
            var join = FindButton("JOIN THE JAPANESE CLUB");
            Check(join != null, "the club page offers JOIN THE JAPANESE CLUB (in a Japanese car)");
            if (join != null) join.onClick.Invoke();
            yield return null; yield return null;
            Check(Clubs.IsMember(S, jp) && Clubs.PlayerRank(S, jp) == Clubs.BoardSize,
                  "joined at the meet, at the bottom of the board", Clubs.PlayerRank(S, jp));
            Check(Duty.RecordFor(S, 2, LifeRules.NightSlot, Duty.KindMeet, jp.id)?.outcome == Duty.Done,
                  "and the Japanese meet is attended");
            var walk = FindButton("SEE YOU AT THE RACE");
            if (walk != null) walk.onClick.Invoke();
            yield return null;

            yield return PullIntoLot(world);
            Check(CarMeets.ArrivedTonight(S), "pulling into the lot signs the player in");
            var fuMeet = Duty.RecordFor(S, 2, LifeRules.NightSlot, Duty.KindMeet, fu.id);
            Check(fuMeet != null && fuMeet.outcome == Duty.WrongCar && fuMeet.note.Contains("1999 models"),
                  "THE CLASH: the Future meet, in a Japanese car, is NOT signed in - a miss, with the line",
                  fuMeet != null ? fuMeet.outcome + " - " + fuMeet.note : "no record");
            Check(Clubs.MissesInWindow(S, fu, S.day) == 1 && Clubs.MissesInWindow(S, jp, S.day) == 0,
                  "one club kept, the other let down: the strike is the Future club's");
            Check(S.calendarLog.Exists(l => l.Contains("future meet - wrong car")), "and the diary says so",
                  S.calendarLog.LastOrDefault());

            // ================= B. SUN EVENING: the wrong car at the line =================
            ClubPlayCheck.Section("B. SUN 3 EVENING: the club race at the line in an American car");
            S.day = 3; S.slotIndex = LifeRules.EveningSlot; S.activeCar = ClubPlayCheck.usCar;
            LifeSimManager.Save();
            LifeHomeScreen.PendingTab = "racenow";
            yield return Home();
            var wr = Duty.RecordFor(S, 3, LifeRules.EveningSlot, Duty.KindRace, jp.id);
            Check(wr != null && wr.outcome == Duty.WrongCar, "turning up at the line in the wrong car is the miss",
                  wr != null ? wr.outcome + " - " + wr.note : "no record");
            Check(Clubs.RaceDueNow(S) == null && FindButton("START") != null,
                  "and the line's page is the ordinary race page again (no club race to run)");

            // ================= C. MON AFTERNOON: a car-meet order =================
            ClubPlayCheck.Section("C. MON 4 AFTERNOON shift: a car-meet order, announced, driven, judged");
            S.day = 4; S.slotIndex = LifeRules.AfternoonSlot; S.activeCar = ClubPlayCheck.jpCar;
            S.workedToday = false;
            var expect = Clubs.MeetOrderFor(JsonUtility.FromJson<LifeState>(JsonUtility.ToJson(S)));
            LifeSimManager.Save();
            yield return GoToTown();
            world = Object.FindAnyObjectByType<PSXRacing.Town.TownWorld>();
            var door = Object.FindObjectsByType<OnFoot.FootTarget>()
                .FirstOrDefault(t => t.title.StartsWith("TONY'S") && t.action.StartsWith("CLOCK ON"));
            Check(door != null && door.onUse != null, "Tony's counter offers CLOCK ON in the scheduled shift");
            if (door == null) { Done(); yield break; }
            door.onUse();
            yield return Seconds(1.2f);
            Check(PizzaRun.Carrying && expect != null && PizzaRun.MeetClub == expect.id,
                  "the order is a car-meet order for the club the rota names", PizzaRun.MeetClub);
            Check(PSXRacing.Town.TownWorld.Cue != null && PSXRacing.Town.TownWorld.Cue.Contains(expect.shortName + " CLUB DELIVERY"),
                  "and the cue announces it", PSXRacing.Town.TownWorld.Cue);
            Check(S.slotIndex == LifeRules.NightSlot && Duty.RecordFor(S, 4, LifeRules.AfternoonSlot, Duty.KindShift)?.outcome == Duty.Done,
                  "the shift is worked and spent", LifeRules.SlotNames[S.slotIndex]);
            // Out through the junction: the house banks the leg and launches.
            LifeHomeScreen.PendingTab = "deliverrun";
            SceneManager.LoadScene(0);
            float t0 = Time.realtimeSinceStartup;
            while (!RaceHandoff.Delivery && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            int venue = RaceHandoff.TrackIndex;
            yield return WaitScene(TrackCatalog.SceneIndex(venue));
            Check(RaceHandoff.Delivery && RaceHandoff.DeliveryMeetClub == expect.id &&
                  SceneManager.GetActiveScene().buildIndex == TrackCatalog.SceneIndex(venue),
                  "the drop's venue loads carrying the club", SceneManager.GetActiveScene().name);
            yield return Race(() =>
            {
                RaceHandoff.RaceTimeSeconds = LifeRules.DeliveryParSeconds(venue, RaceHandoff.DeliveryDropFraction) * 0.7f;
                RaceHandoff.MetersDriven = 1500f;
                RaceHandoff.DamageScore = 0f; RaceHandoff.HardHits = 0;
                RaceHandoff.CargoReported = true; RaceHandoff.CargoCondition = 1f; RaceHandoff.CargoConsumable = 1f;
                RaceHandoff.CargoBoxes = 1; RaceHandoff.CargoImpacts = 0;
            });
            var est = Clubs.StandingOf(S, expect);
            Check(est != null && est.invited && S.mail.Exists(m => m.subject == "AN INVITATION FROM THE " + expect.name),
                  "fast and clean at the garage night: the club's invitation arrives",
                  S.calendarLog.LastOrDefault());

            // ================= D. SAT NIGHT: both meets, one car both clubs take =================
            ClubPlayCheck.Section("D. SAT 9 NIGHT, in a 1999 Japanese car: both meets signed in");
            S.jobFrom = Duty.Ord(11, 0);
            S.day = 8; S.slotIndex = LifeRules.NightSlot; S.activeCar = ClubPlayCheck.fuCar;
            Clubs.BeginRace(S, fu);                                    // Friday's Future race kept
            S.day = 9; S.slotIndex = LifeRules.NightSlot; S.activeCar = ClubPlayCheck.bothCar;
            LifeSimManager.Save();
            yield return GoToTown();
            world = Object.FindAnyObjectByType<PSXRacing.Town.TownWorld>();
            yield return PullIntoLot(world);
            Check(Duty.RecordFor(S, 9, LifeRules.NightSlot, Duty.KindMeet, jp.id)?.outcome == Duty.Done &&
                  Duty.RecordFor(S, 9, LifeRules.NightSlot, Duty.KindMeet, fu.id)?.outcome == Duty.Done,
                  "arriving in a car both clubs take signs in both meets");

            // ================= E. SUN EVENING: the club race =================
            ClubPlayCheck.Section("E. SUN 10 EVENING: the Japanese club race from the line");
            S.day = 10; S.slotIndex = LifeRules.EveningSlot; S.activeCar = ClubPlayCheck.jpCar;
            S.ActiveCar.fuel = 95f;
            S.money = 50000;
            LifeSimManager.Save();
            yield return Home();
            var opp = Clubs.RaceOpponent(S, jp, 10);
            Set(screen, "raceFromLine", true);
            Set(screen, "tab", "prerace");
            Rebuild(screen);
            yield return null; yield return null;
            Check(Object.FindObjectsByType<Text>().Any(t => t.isActiveAndEnabled && t.text == "JAPANESE CLUB RACE"),
                  "the line opens the JAPANESE CLUB RACE page");
            var start = FindButton("START");
            Check(start != null, "the line's pre-race page is the club race, with START",
                  start != null ? start.GetComponentInChildren<Text>().text : "none");
            if (start == null) { Done(); yield break; }
            int clubVenue = Clubs.RaceVenue(jp, 10);
            start.onClick.Invoke();
            Check(RaceHandoff.ClubRace && RaceHandoff.ClubRaceClub == jp.id && RaceHandoff.ClubOpponent == opp.alias &&
                  RaceHandoff.TrackIndex == clubVenue, "START hands over the club race: club, opponent, road",
                  RaceHandoff.ClubOpponent + " at " + TrackCatalog.At(clubVenue).name);
            Check(Duty.RecordFor(S, 10, LifeRules.EveningSlot, Duty.KindRace, jp.id)?.outcome == Duty.Done,
                  "attended at the start line");
            yield return WaitScene(TrackCatalog.SceneIndex(clubVenue));
            Check(SceneManager.GetActiveScene().buildIndex == TrackCatalog.SceneIndex(clubVenue), "the venue loads",
                  SceneManager.GetActiveScene().name);
            int moneyBefore = S.money;
            yield return Race(() =>
            {
                RaceHandoff.FinishPos = 1; RaceHandoff.FieldSize = 2;
                RaceHandoff.MetersDriven = 3000f; RaceHandoff.RaceTimeSeconds = 200f;
            });
            Check(Clubs.PlayerRank(S, jp) == 9 && Clubs.RankOf(S, jp, opp.alias) == 10,
                  "the result is banked: past #9 " + opp.alias + ", the board moves", Clubs.PlayerRank(S, jp));
            Check(S.day == 10 && S.slotIndex == LifeRules.NightSlot, "the race spent its block",
                  LifeRules.DateLabel(S.day) + " " + LifeRules.SlotNames[S.slotIndex]);
            Check(S.money - moneyBefore == Clubs.RacePurse, "and paid the club purse", S.money - moneyBefore);
            var board = Clubs.Board(S, jp);
            int racedBefore = board.Sum(e => e.wins + e.losses);
            LifeRules.SpendActivitySlot(S, LifeRules.ActDrive);   // through the night: the rollover
            int racedAfter = Clubs.Board(S, jp).Sum(e => e.wins + e.losses);
            Check(S.day == 11 && racedAfter > racedBefore && Clubs.PlayerRank(S, jp) == 9,
                  "at the rollover the rest of the board raced its neighbours",
                  (racedAfter - racedBefore) + " results; news: " + S.clubNews.LastOrDefault());
            LifeSimManager.Save();
            Done();
        }

        static void Done()
        {
            ClubPlayCheck.Finish();
            EditorApplication.Exit(ClubPlayCheck.failures == 0 ? 0 : 1);
        }
    }
}
