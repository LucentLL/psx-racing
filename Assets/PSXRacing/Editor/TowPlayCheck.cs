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
    /// R0c / OWNER'S ANSWERS 1 and 8 in the RUNNING game, in the check venue (a
    /// mountain stage, so a career race with a RaceManager): a non-debug
    /// career's pause menu has no RESTART and no RESET CAR; a career car put on
    /// its roof is NOT put back on the road (the old watchdog did it after 4.5
    /// s); the watchdog says CALL TOW TRUCK; the pause menu's leave row reads
    /// CALL TOW TRUCK with the fee; pressing it takes the fee, banks the
    /// planted crash onto the owned car from the seat, ends the drive and
    /// loads the front end - and the front end banks nothing twice.
    /// Writes PSXRacing_tow_play_check.txt. Exits itself (no -quit: it enters
    /// play mode). Watched - hidden batch play mode crashes in
    /// RenderTexture::Create on the owner's PC.
    /// </summary>
    public static class TowPlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal static string ownedId;

        [MenuItem("PSX Racing/Check Tow Truck (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;
            var scenes = EditorBuildSettings.scenes;
            int si = TrackCatalog.SceneIndex(TrackCatalog.CheckVenue);
            if (si < 0 || si >= scenes.Length || !File.Exists(scenes[si].path))
            {
                Check(false, "the check venue is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }
            // Never the owner's career.
            LifeSimManager.UseScratchSave("towplay");
            var s = LifeRules.SeedNewGame("TOWCHECK", 30, LifeRules.DefaultJobIndex);
            CarSpec spec = CarCatalog.All.FirstOrDefault(c => c.name.Contains("RX-7 Type RS") && CarModelLibrary.LoadFor(c) != null)
                           ?? CarCatalog.All.FirstOrDefault(c => CarModelLibrary.LoadFor(c) != null);
            var owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.faults.Clear();
            owned.carHP = owned.paint = owned.radiator = 80f;
            owned.fuel = 80f;
            s.activeCar = owned.id;
            s.money = 600;
            ownedId = owned.id;
            typeof(LifeSimManager).GetField("state", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, s);
            LifeSimManager.Save();

            EditorSceneManager.OpenScene(scenes[si].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;      // a career drive
            RaceHandoff.TrackIndex = TrackCatalog.CheckVenue;
            RaceHandoff.CalendarDay = 0;
            RaceHandoff.TimeOfDayIndex = TimeOfDay.Noon;
            RaceHandoff.Solo = true;
            RaceHandoff.CarId = owned.id;
            RaceHandoff.CarSpecId = spec.id;
            RaceHandoff.StartFuelPct = owned.fuel;
            LifeHomeScreen.FillCarRequestFor(s, owned);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange st)
        {
            if (st != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("TowPlayCheckRunner").AddComponent<TowPlayCheckRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "TOW CHECK: ALL HOLD." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                                           "PSXRacing_tow_play_check.txt"), log.ToString());
            Debug.Log(log.ToString());
        }
    }

    public class TowPlayCheckRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return Guarded(Body());
            TowPlayCheck.Finish();
            LifeSimManager.UseCareerSave();
            EditorApplication.Exit(TowPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object cur = null;
                bool more;
                try
                {
                    more = body.MoveNext();
                    if (more) cur = body.Current;
                }
                catch (System.Exception e)
                {
                    more = false;
                    TowPlayCheck.Check(false, "a step THREW: " + e.GetType().Name + " - " + e.Message);
                    Debug.LogException(e);
                }
                if (!more) yield break;
                if (cur is IEnumerator nested) yield return Guarded(nested);
                else yield return cur;
            }
        }

        static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                  .Invoke(target, args);

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var s = LifeSimManager.State;
            var owned = s.FindCar(TowPlayCheck.ownedId);
            var rm = RaceManager.Instance;
            var car = rm != null ? rm.playerCar : null;
            TowPlayCheck.Check(car != null && owned != null, "a career race with the owned car under the player");
            if (car == null || owned == null) yield break;
            TowPlayCheck.Check(CareerDrive.Strict && !CareerDrive.RestartOffered && !CareerDrive.ResetAllowed,
                               "a non-debug career drive: no RESTART, no reset, the tow");

            var pm = Object.FindAnyObjectByType<PauseMenu>();
            TowPlayCheck.Check(pm != null, "the race scene has its pause menu");
            if (pm == null) yield break;
            var rows = pm.GetComponentsInChildren<Button>(true)
                         .Select(b => b.GetComponentInChildren<Text>(true))
                         .Where(t => t != null).Select(t => t.text).ToList();
            TowPlayCheck.Note("pause rows: " + string.Join(" | ", rows));
            TowPlayCheck.Check(!rows.Any(r => r.Contains("RESTART")) && !rows.Any(r => r.Contains("RESET CAR")),
                               "the pause menu offers neither RESTART nor RESET CAR in a career");

            float t0 = Time.time;
            while (rm.State != RaceManager.RaceState.Racing && Time.time - t0 < 25f) yield return null;
            TowPlayCheck.Check(rm.State == RaceManager.RaceState.Racing, "the race went live", rm.State);

            var resp = car.GetComponent<CollisionResponder>();
            TowPlayCheck.Check(resp != null && resp.Ledger != null, "the career car's crash ledger is armed");
            if (resp != null && resp.Ledger != null)
            {
                resp.Ledger.DrawOverride = () => new CrashStopGap.Draws
                    { paint = 0.5f, bumper = 0.1f, body = 0.5f, radiator = 0.1f, radAmt = 0.5f, align = 0.1f };
                resp.RecordHit(12f, CrashStopGap.ZoneFront, -1, new Vector3(-0.5f, 0f, 2f), "planted", Time.time);
            }

            // On its roof, on a slab of road far above the course.
            var strip = new GameObject("TowStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(-600f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(200f, 2f, 200f);
            car.TeleportTo(new Vector3(-600f, 4001f + 1.4f, 0f), Quaternion.Euler(0f, 30f, 180f));
            for (int f = 0; f < 90; f++) yield return new WaitForFixedUpdate();
            int tp0 = car.TeleportCount;
            Vector3 p0 = car.transform.position;
            TowPlayCheck.Note("landed: up.y " + car.transform.up.y.ToString("0.00") + ", y " + p0.y.ToString("0.0"));
            var stuck = car.GetComponent<StuckRecovery>();
            TowPlayCheck.Check(stuck != null && stuck.enabled, "the player car has its stuck watchdog, switched on");
            yield return new WaitForSeconds(8f);
            TowPlayCheck.Check(car.TeleportCount == tp0 && (car.transform.position - p0).magnitude < 3f,
                               "8 s on its roof and the car is NOT put back on the road (the old watchdog reset it at 4.5 s)",
                               "teleports " + tp0 + " -> " + car.TeleportCount + ", moved " +
                               (car.transform.position - p0).magnitude.ToString("0.0") + " m");
            TowPlayCheck.Check(car.transform.up.y < 0.25f, "it is still on its roof", car.transform.up.y.ToString("0.00"));
            string prompt = stuck != null ? stuck.Prompt : null;
            TowPlayCheck.Check(stuck != null && stuck.Stranded && prompt != null && prompt.Contains("CALL TOW TRUCK"),
                               "the watchdog says how to get out", prompt != null ? prompt.Replace("\n", " / ") : "none");
            TowPlayCheck.Check(CareerDrive.NeedsTow(car), "the car needs a tow");

            Call(pm, "SetOpen", true);
            yield return null;
            var exitText = (Text)pm.GetType().GetField("exitLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pm);
            int fee = CareerDrive.TowFeeHere();
            // The menu's text component draws the em dash as a hyphen.
            TowPlayCheck.Check(exitText != null && exitText.text.Replace("—", "-") == "CALL TOW TRUCK - " + MenuKit.Money(fee) &&
                               fee == CareerDrive.TowFee(100f),
                               "the leave row reads CALL TOW TRUCK with the fee home from the mountains",
                               exitText != null ? exitText.text : "none");

            int money0 = s.money;
            float hp0 = owned.carHP, odo0 = owned.odoMiles;
            Call(pm, "LeaveOrTow");
            TowPlayCheck.Check(s.money == money0 - fee, "the tow took its fee", MenuKit.Money(money0) + " -> " + MenuKit.Money(s.money));
            TowPlayCheck.Check(owned.faults.Count(f => f.id == "bumper_crack" && f.hidden) == 1 && owned.carHP < hp0,
                               "the planted crash is banked onto the owned car from the seat (hidden bumper row, body lost)",
                               string.Join(",", owned.faults.Select(f => f.id)) + "  body " + hp0 + " -> " + owned.carHP.ToString("0.0"));
            TowPlayCheck.Check(s.calendarLog.Any(l => l.Contains("tow truck home")) && s.calendarLog.Any(l => l.Contains("quit")),
                               "the diary has the tow and the quit",
                               string.Join(" / ", s.calendarLog.Skip(Mathf.Max(0, s.calendarLog.Count - 2))));
            Note("odometer +" + (owned.odoMiles - odo0).ToString("0.00") + " mi");

            float t1 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().buildIndex != 0 && Time.realtimeSinceStartup - t1 < 60f) yield return null;
            TowPlayCheck.Check(SceneManager.GetActiveScene().buildIndex == 0, "the drive ended: the front end loaded",
                               SceneManager.GetActiveScene().name);
            for (int i = 0; i < 90; i++) yield return null;
            var s2 = LifeSimManager.State;
            var o2 = s2 != null ? s2.FindCar(TowPlayCheck.ownedId) : null;
            TowPlayCheck.Check(o2 != null && o2.faults.Count(f => f.id == "bumper_crack") == 1 && !RaceHandoff.ResultReady &&
                               s2.money == money0 - fee,
                               "at home the car is the one the tow brought: one bumper row (not banked twice), the fee paid once",
                               o2 != null ? o2.faults.Count(f => f.id == "bumper_crack") + " bumper row(s), $" + s2.money : "no car");
        }

        static void Note(string what) => TowPlayCheck.Note(what);
    }
}
