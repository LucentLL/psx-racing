using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// R1a part 1 in the RUNNING game (tools\crash-play-check.ps1 runs this
    /// when the located model is on; PSX_FAULT_LOCATED=0 runs R0a's check).
    /// Real PhysX contacts on the flat strip, a career car armed the real way
    /// (CrashStopGap.Arm -> DamageLedger), every die forced low so each row
    /// that can come up does:
    ///   W  walls square on at 15 / 30 / 50 km/h: ONE FRONT incident each,
    ///      delta-V over the pulse ~ the closing speed, the band it reads;
    ///   S  a side swipe along a barrier built of 4 m sections: the sections
    ///      merge into few incidents, on the struck side;
    ///   R  a 60 km/h roll: one ROLL incident banked when it comes to rest
    ///      (no respawn), its roof pulses inside it, not beside it;
    ///   D  a 5 m drop: one DROP incident at ~9.9 m/s (the belly pulse inside
    ///      it), the bump stops striking on their own;
    ///   F  the drive's damage files folded into the career: every incident
    ///      once, a second fold banks nothing, the files gone after the save.
    /// The clean lap and the race sample are RacePlayCheck with the census
    /// (DamageCensus, PSX_DAMAGE_CENSUS).
    /// </summary>
    public class DamagePlayCheckRunner : MonoBehaviour
    {
        CarController car;
        CollisionResponder resp;
        Rigidbody rb;
        float surfaceY;
        float zNext = -14000f;
        const float XLane = -600f;
        bool hold;
        float holdMps;
        readonly List<GameObject> props = new List<GameObject>();
        readonly List<Incident> all = new List<Incident>();
        OwnedCar owned;

        static void Check(bool ok, string what, object got = null) => CrashPlayCheck.Check(ok, what, got);
        static void Note(string what) => CrashPlayCheck.Note(what);
        static float[] Low() { var d = new float[DamageModel.Dice]; for (int i = 0; i < d.Length; i++) d[i] = 0.1f; return d; }

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = hold ? Mathf.Clamp01(0.35f + (holdMps - vf) * 0.6f) : 0f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            car.handbrakeInput = false;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return Guarded(Body());
            foreach (var p in props) if (p != null) Destroy(p);
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Guarded(IEnumerator body)
        {
            while (true)
            {
                object cur = null;
                bool more;
                try { more = body.MoveNext(); if (more) cur = body.Current; }
                catch (System.Exception e)
                {
                    more = false;
                    Check(false, "a run THREW: " + e.GetType().Name + " - " + e.Message);
                    Debug.LogException(e);
                }
                if (!more) yield break;
                if (cur is IEnumerator nested) yield return Guarded(nested);
                else yield return cur;
            }
        }

        DamageLedger Fresh()
        {
            if (resp.Damage != null) { resp.Damage.Flush(); all.AddRange(resp.Damage.incidents); }
            resp.Damage = null;
            resp.Ledger = null;
            resp.ResetDamage();
            CrashStopGap.Arm(car);
            var L = resp.Damage;
            if (L != null) L.DrawOverride = Low;
            return L;
        }

        IEnumerator Fixed(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        Vector3 NextStart()
        {
            var at = new Vector3(XLane, surfaceY + 0.6f, zNext);
            zNext += 400f;
            return at;
        }

        GameObject Box(string name, Vector3 centre, Vector3 size, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(centre, rot);
            go.AddComponent<BoxCollider>().size = size;
            props.Add(go);
            return go;
        }

        static string Show(Incident i) =>
            DamageModel.KindName(i.kind) + " " + DamageModel.ZoneName(i.zone) +
            (i.corner >= 0 ? " " + DamageModel.CornerNames[i.corner] : i.side < 0 ? " L" : i.side > 0 ? " R" : "") +
            " dv " + i.dv.ToString("0.0") + " b" + i.band + (i.glancing ? " glancing" : "") + ": " + i.outcome;

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            Check(car != null, "there is a player car");
            if (car == null) yield break;
            resp = car.GetComponent<CollisionResponder>();
            rb = car.GetComponent<Rigidbody>();
            Check(DamageModel.Live, "PSX_FAULT_LOCATED and PSX_FAULT_CRASH are on (the located model is live)");
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;

            var strip = new GameObject("DamageStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4001f;
            props.Add(strip);

            // the career car this drive banks onto (a scratch career)
            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId) ?? CarCatalog.All[0];
            owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.id = "r1a#play";
            owned.faults.Clear();
            owned.carHP = owned.paint = owned.radiator = 80f;
            s.activeCar = owned.id;
            RaceHandoff.CarId = owned.id;
            foreach (var p in DamageBank.Files()) File.Delete(p);
            Note("car " + spec.name + ", " + rb.mass.ToString("0") + " kg; every die forced to 0.1");

            foreach (float kmh in new[] { 15f, 30f, 50f }) yield return WallRun(kmh);
            yield return SwipeRun();
            yield return RollRun();
            yield return DropRun();

            // F: the files, folded once
            var last = resp.Damage;
            if (last != null) all.AddRange(last.incidents);
            int events = all.Count(i => i.outcome.Any);
            int folded = CrashStopGap.FoldDamage(car);
            int again = DamageBank.FoldFiles(s);
            Check(folded == events && owned.history.Count == events && again == 0,
                  "every damage incident of the drive banked onto the career car exactly once (a second fold: nothing)",
                  "incidents " + events + ", folded " + folded + ", history " + owned.history.Count + ", again " + again);
            LifeSimManager.Save();
            Check(DamageBank.Files().Length == 0, "and the damage files are gone once the save holds them");
            Note("career car after the drive: body " + owned.carHP.ToString("0") + ", paint " + owned.paint.ToString("0") +
                 ", radiator " + owned.radiator.ToString("0") + ", rows " + string.Join(",", owned.faults.Select(f => f.id + (f.hidden ? "(hidden)" : ""))));
            Check(owned.faults.All(f => f.hidden), "every row it wrote is hidden (no part named before a person finds it)");
        }

        IEnumerator WallRun(float kmh)
        {
            var L = Fresh();
            float v = kmh / 3.6f;
            var at = NextStart();
            Box("Wall" + kmh, at + new Vector3(0f, 0.6f, 14f), new Vector3(6f, 2.4f, 1f), Quaternion.identity);
            car.TeleportTo(at, Quaternion.identity);
            yield return Fixed(0.2f);
            car.SetRolling(v);
            hold = true; holdMps = v;
            float t = 0f;
            while (t < 8f && (L == null || L.incidents.Count == 0)) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            hold = false;
            yield return Fixed(1.5f);
            if (L == null) { Check(false, kmh + " km/h wall: no ledger armed"); yield break; }
            var hits = L.incidents.Where(i => i.kind == DamageModel.KHit && i.outcome.Any).ToList();
            var front = hits.Where(i => i.zone == DamageModel.ZFront).OrderByDescending(i => i.dv).FirstOrDefault();
            int want = kmh < 20f ? 2 : kmh < 40f ? 4 : 5;
            Check(front != null && hits.Count(i => i.zone == DamageModel.ZFront) == 1,
                  kmh + " km/h square into a wall: ONE FRONT incident", string.Join(" | ", L.incidents.Select(Show)));
            if (front == null) yield break;
            Check(front.dv >= 0.7f * v && front.dv <= 1.25f * v,
                  "  delta-V over the pulse ~ the closing speed (" + v.ToString("0.0") + " m/s), capped x1.2", front.dv.ToString("0.00"));
            Check(front.band == want, "  it reads band " + want + " of the FRONT table", Show(front));
        }

        IEnumerator SwipeRun()
        {
            var L = Fresh();
            float v = 80f / 3.6f;
            var at = NextStart();
            // a barrier of 4 m sections on the right, the car angled 6 degrees into it
            var sections = new HashSet<Collider>();
            for (int k = 0; k < 30; k++)
                Box("Rail" + k, at + new Vector3(2.4f, 0.4f, 6f + k * 4f), new Vector3(0.4f, 1.2f, 3.98f), Quaternion.identity);
            System.Action<CollisionResponder, float, bool, Collider> onHit = (r, mps, hard, col) =>
                { if (r == resp && col != null && col.name.StartsWith("Rail")) sections.Add(col); };
            CollisionResponder.HitReportedOn += onHit;
            car.TeleportTo(at, Quaternion.Euler(0f, 6f, 0f));
            yield return Fixed(0.2f);
            car.SetRolling(v);
            hold = true; holdMps = v;
            yield return Fixed(3f);
            hold = false;
            yield return Fixed(1.5f);
            CollisionResponder.HitReportedOn -= onHit;
            if (L == null) { Check(false, "side swipe: no ledger armed"); yield break; }
            var inc = L.incidents.Where(i => i.kind == DamageModel.KHit).ToList();
            Check(inc.Count > 0 && inc.Count <= 4 && sections.Count >= 2 && inc.All(i => i.side > 0 || i.zone == DamageModel.ZFront),
                  "a side swipe at 80 km/h along a 4 m-section barrier: " + sections.Count + " sections struck, " + inc.Count +
                  " incident(s), all on the struck (right) side", string.Join(" | ", inc.Select(Show)));
            Check(inc.All(i => !i.outcome.HasRow(DamageModel.RadiatorId)), "  a swipe cracks no radiator");
        }

        IEnumerator RollRun()
        {
            var L = Fresh();
            float v = 60f / 3.6f;
            var at = NextStart();
            car.TeleportTo(at, Quaternion.identity);
            yield return Fixed(0.2f);
            car.SetRolling(v);
            hold = true; holdMps = v;
            yield return Fixed(0.5f);
            hold = false;
            rb.linearVelocity += Vector3.up * 3.5f;
            rb.angularVelocity = car.transform.forward * 6f;
            float t = 0f;
            while (t < 12f && (L == null || !L.incidents.Any(i => i.kind == DamageModel.KRoll))) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            yield return Fixed(0.5f);
            if (L == null) { Check(false, "roll: no ledger armed"); yield break; }
            var roll = L.incidents.FirstOrDefault(i => i.kind == DamageModel.KRoll);
            Check(roll != null && roll.quarters >= 2 && roll.zone == DamageModel.ZRoof && roll.outcome.Any,
                  "a 60 km/h roll: ONE ROLL incident, banked when it came to rest (no respawn), two or more quarter-turns, it costs",
                  roll != null ? Show(roll) + ", " + roll.quarters + " quarter-turns, " + t.ToString("0.0") + " s" : "none in " + t.ToString("0.0") + " s");
            Check(!L.incidents.Any(i => i.kind == DamageModel.KHit && i.zone == DamageModel.ZRoof),
                  "  the roof pulses are the roll's, not separate hits", string.Join(" | ", L.incidents.Select(Show)));
            car.TeleportTo(NextStart(), Quaternion.identity);
            yield return Fixed(0.5f);
        }

        IEnumerator DropRun()
        {
            var L = Fresh();
            var at = NextStart();
            car.TeleportTo(at + Vector3.up * 5f, Quaternion.identity);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            float t = 0f;
            while (t < 6f && (L == null || !L.incidents.Any(i => i.kind == DamageModel.KDrop))) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            yield return Fixed(1.5f);
            if (L == null) { Check(false, "drop: no ledger armed"); yield break; }
            var drop = L.incidents.FirstOrDefault(i => i.kind == DamageModel.KDrop);
            Check(drop != null && drop.dv >= 8.5f && drop.dv <= 11.5f && drop.band == 4,
                  "a 5 m drop: ONE DROP incident at ~9.9 m/s, the frame band",
                  drop != null ? Show(drop) : "none: " + string.Join(" | ", L.incidents.Select(Show)));
            Check(!L.incidents.Any(i => i.kind == DamageModel.KHit && i.zone == DamageModel.ZUnder),
                  "  the belly pulse is the drop's, not a separate hit; the stops strike on their own (" +
                  L.incidents.Count(i => i.kind == DamageModel.KBottom) + " corner strike(s))",
                  string.Join(" | ", L.incidents.Select(Show)));
        }
    }

    /// <summary>
    /// The race census (PSX_DAMAGE_CENSUS = a CSV path, set by the runner):
    /// in play mode every responder runs the located model as a census (it
    /// banks nothing) and each finished incident is a line - car, player or
    /// not, kind, zone, delta-V, band, outcome, the rows' repair price.
    /// </summary>
    [InitializeOnLoad]
    public static class DamagePlayCheck   // named as the file: the runner above then has no editor MonoScript
    {
        static readonly string path = System.Environment.GetEnvironmentVariable("PSX_DAMAGE_CENSUS");

        static DamagePlayCheck()
        {
            if (string.IsNullOrEmpty(path)) return;
            EditorApplication.playModeStateChanged += st =>
            {
                if (st != PlayModeStateChange.EnteredPlayMode) return;
                DamageModel.CensusAll = true;
                DamageModel.IncidentReported -= Log;
                DamageModel.IncidentReported += Log;
            };
            if (!File.Exists(path))
                File.WriteAllText(path, "venue,time,car,player,kind,zone,side,corner,dv,band,glancing,narrow,what,any,scuffonly,cost,outcome\n");
        }

        static void Log(CollisionResponder r, Incident i)
        {
            var c = r != null ? r.GetComponent<CarController>() : null;
            bool player = c != null && RaceManager.Instance != null && RaceManager.Instance.playerCar == c;
            int cost = 0;
            foreach (var row in i.outcome.rows) { var f = FaultCatalog.MakeById(row.id, "jpn"); if (f != null) cost += f.cost; }
            if (i.outcome.alignSeverity > 0) cost += 60;
            string Clean(string x) => (x ?? "").Replace(",", " ").Replace("\n", " ");
            var t = TrackCatalog.At(RaceHandoff.TrackIndex);
            File.AppendAllText(path, string.Join(",", new[]
            {
                Clean(t != null ? t.id : "?"), Time.time.ToString("0.0"), Clean(c != null ? c.name : "?"), player ? "1" : "0",
                DamageModel.KindName(i.kind), DamageModel.ZoneName(i.zone), i.side.ToString(), i.corner.ToString(),
                i.dv.ToString("0.00"), i.band.ToString(), i.glancing ? "1" : "0", i.narrow ? "1" : "0", Clean(i.what),
                i.outcome.Any ? "1" : "0", i.outcome.ScuffOnly ? "1" : "0", cost.ToString(), Clean(i.outcome.ToString()),
            }) + "\n");
        }
    }
}
