using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>
    /// R1a: DAMAGE IS BANKED THE MOMENT IT HAPPENS (Fault_System_Design §2
    /// "Size and timing", §3.1 "Banking"). A hit never runs a full Save():
    /// the career car's DamageLedger writes a small per-car file beside the
    /// save's own copies (named by the save manager's DamageFilePath; 2 KB or
    /// less), and this folds it into the career by hitId - at the
    /// exit stamps (finish line, QUIT / the tow, the city exits), at RESTART,
    /// at the apply-back and at the next Load - so folding twice changes
    /// nothing. A folded file is deleted only after the save that carries it
    /// (AfterSave). A file names its car; a car that is not in the career
    /// banks nothing and never falls through to the active car.
    /// Every row it writes is HIDDEN (FaultCatalog.MakeById) and carries its
    /// cause: impact, zone, side, delta-V, hitId.
    /// </summary>
    public static class DamageBank
    {
        [Serializable] public class RowRec
        {
            public string id;
            public int stage, zone, side, corner;
            public float mag;
        }

        [Serializable] public class IncidentRec
        {
            public int hitId, kind, zone, side, corner, band, alignSide, alignSeverity, quarters;
            public float dv, paint, body, radiator, leak;
            public bool flood, hydrolock, narrow;
            /// <summary>R1c-A: the structural FAIL (DamageRules) and the driver's injury (CrashFates).</summary>
            public int fail, inj;
            public float injScore;
            public string what = "";
            public List<RowRec> rows = new List<RowRec>();
        }

        [Serializable] public class DamageFile
        {
            public int v = 1;
            public string carId, drive;
            public int day;
            public float odo;
            public List<IncidentRec> incidents = new List<IncidentRec>();
        }

        public const string Tag = ".dmg.";
        /// <summary>History kept per car (design: 48).</summary>
        public const int HistoryMax = 48;

        /// <summary>False once a scan found no files (cleared by a write), so
        /// the apply-back's fold costs nothing on the thousands of legs a
        /// self-test banks.</summary>
        static bool maybeFiles = true;
        static readonly List<string> folded = new List<string>();
        static LifeState foldedInto;
        static readonly Dictionary<string, float> legBody = new Dictionary<string, float>();

        static string Safe(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s ?? "")
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }

        public static string PathFor(string carId, string drive) => LifeSimManager.DamageFilePath(Safe(carId), Safe(drive));

        public static IncidentRec Record(Incident inc)
        {
            var o = inc.outcome;
            var r = new IncidentRec
            {
                hitId = inc.hitId, kind = inc.kind, zone = inc.zone, side = inc.side, corner = inc.corner,
                band = inc.band, dv = inc.dv, paint = o.paintLoss, body = o.bodyLoss, radiator = o.radiatorLoss,
                leak = o.leakPctMin, alignSide = o.alignSide, alignSeverity = o.alignSeverity, quarters = inc.quarters,
                flood = o.flood, hydrolock = o.hydrolock, what = inc.what ?? "",
                narrow = inc.narrow, fail = inc.fail, inj = inc.injury, injScore = inc.injScore,
            };
            foreach (var row in o.rows)
                r.rows.Add(new RowRec { id = row.id, stage = row.stage, mag = row.mag, zone = row.zone, side = row.side, corner = row.corner });
            return r;
        }

        /// <summary>Write one drive's file (replaces its previous write). Never
        /// throws; nothing in an edition with no career.</summary>
        public static bool Write(DamageFile f)
        {
            if (f == null || string.IsNullOrEmpty(f.carId) || !LifeSimManager.Persists) return false;
            try
            {
                string p = PathFor(f.carId, f.drive), tmp = p + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(tmp, JsonUtility.ToJson(f));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
                maybeFiles = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DamageBank] could not write the damage file: " + e.Message);
                return false;
            }
        }

        /// <summary>The damage files of the career's key on disk.</summary>
        public static string[] Files() => LifeSimManager.DamageFiles();

        /// <summary>A row's stage: its own, or (a row from before v26, or one an
        /// older path rolled) its legacy severity read as 1 -> 2, 2 -> 3.</summary>
        public static float StageOf(CarFault f) =>
            f == null ? 0f : f.stage > 0f ? f.stage : f.severity >= 1.5f ? 3f : 2f;

        /// <summary>
        /// Fold every damage file of this career's key into <paramref name="s"/>,
        /// once per hitId. Returns the incidents newly banked. The files go
        /// after the next save of this state (AfterSave). Never throws.
        /// </summary>
        public static int FoldFiles(LifeState s)
        {
            if (s == null || !LifeSimManager.Persists || !maybeFiles) return 0;
            var paths = Files();
            if (paths.Length == 0) { maybeFiles = false; return 0; }
            int merged = 0;
            foreach (var p in paths)
            {
                DamageFile f = null;
                try { f = JsonUtility.FromJson<DamageFile>(File.ReadAllText(p)); }
                catch (Exception e) { Debug.LogWarning("[DamageBank] unreadable damage file " + p + ": " + e.Message); }
                if (f == null) continue;
                var car = s.FindCar(f.carId);
                if (car != null && f.incidents != null)
                    foreach (var r in f.incidents) if (ApplyIncident(s, car, r)) merged++;
                if (!folded.Contains(p)) folded.Add(p);
                foldedInto = s;
            }
            return merged;
        }

        /// <summary>LifeSimManager.Save, once the career is on disk: the files
        /// folded into it are no longer needed.</summary>
        public static void AfterSave(LifeState saved)
        {
            if (folded.Count == 0 || saved == null || saved != foldedInto) return;
            foreach (var p in folded)
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            folded.Clear();
            foldedInto = null;
        }

        /// <summary>The body the damage files took off this car since the last
        /// apply-back (its body-lane wear roll reads the body from before the
        /// leg's crash, as R0a's did).</summary>
        public static float TakeLegBody(OwnedCar car)
        {
            if (car == null || car.id == null || !legBody.TryGetValue(car.id, out float b)) return 0f;
            legBody.Remove(car.id);
            return b;
        }

        static bool SameRow(CarFault f, RowRec r)
        {
            if (f == null || f.id != r.id) return false;
            if (r.id == DamageModel.LampId) return f.zone1 == r.zone && f.hitSide == r.side;
            // R1a part 2 (PSX_FAULT_CRUSH): one nose (tail) panel row whichever side
            // took it - a harder hit raises its stage; a door per side
            if (r.id == DamageModel.PanelId)
                return f.zone1 == r.zone && ((DamageRules.CrushOn && r.zone != DamageModel.ZSide) || f.hitSide == r.side);
            if (r.id == DamageModel.BarId) return f.zone1 == r.zone;
            return true;
        }

        /// <summary>
        /// One incident onto one car - exactly once (its hitId goes into the
        /// car's history; a hitId already there is skipped). Lanes, then rows
        /// (a row the car already carries is raised to the new stage, never
        /// doubled), the alignment by R0a's one rule, a flood title, the event.
        /// </summary>
        public static bool ApplyIncident(LifeState s, OwnedCar car, IncidentRec r)
        {
            if (car == null || r == null || r.hitId == 0) return false;
            if (car.history == null) car.history = new List<CarEvent>();
            if (car.faults == null) car.faults = new List<CarFault>();
            if (car.history.Exists(e => e != null && e.hitId == r.hitId)) return false;
            int valueBefore = CrashFates.WriteOffOn ? CarMarket.CarValue(car) : 0;   // R1c-A: what it was worth before
            int day = s != null ? s.day : 0;
            float odo = car.odoMiles;
            if (r.paint > 0f) car.paint = Mathf.Max(0f, car.paint - r.paint);
            if (r.body > 0f)
            {
                car.carHP = Mathf.Max(0f, car.carHP - r.body);
                legBody[car.id ?? ""] = (legBody.TryGetValue(car.id ?? "", out float lb) ? lb : 0f) + r.body;
            }
            if (r.radiator > 0f) car.radiator = Mathf.Max(0f, car.radiator - r.radiator);
            string family = FaultCatalog.FamilyOf(car);
            string kind = DamageModel.KindName(r.kind);
            if (r.rows != null)
                foreach (var row in r.rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.id)) continue;
                    var have = car.faults.Find(f => SameRow(f, row));
                    if (have != null)
                    {
                        if (row.stage <= StageOf(have)) continue;
                        Stamp(FaultCause.StampImpact(have, day, odo, row.zone, row.side, r.dv), row, r, kind);
                        continue;
                    }
                    var nf = FaultCatalog.MakeById(row.id, family);
                    if (nf == null) continue;
                    car.faults.Add(Stamp(FaultCause.StampImpact(nf, day, odo, row.zone, row.side, r.dv), row, r, kind));
                }
            if (r.alignSeverity > 0)
            {
                CrashBank.ApplyOutcome(car, new CrashStopGap.Outcome
                {
                    classes = CrashStopGap.Cls.Alignment, alignSide = r.alignSide, alignSeverity = r.alignSeverity,
                }, day, r.zone, r.alignSide, r.dv);
                var a = car.faults.Find(f => f != null && f.id == FaultCatalog.AlignmentId);
                if (a != null && a.bornDay == day && Mathf.Approximately(a.hitMps, Mathf.Max(0f, r.dv)))
                {
                    a.hitId = r.hitId;
                    if (r.corner >= 0) a.corner1 = r.corner + 1;
                    if (a.stage <= 0f) a.stage = a.severity >= 1.5f ? 3f : 2f;
                }
            }
            CrashFates.AfterIncident(s, car, r, valueBefore);   // R1c-A: the FAIL stays, the driver, the write-off
            if (r.flood) car.title = OwnedCar.TitleFlood;
            car.history.Add(new CarEvent
            {
                day = day, odo = odo, kind = r.kind, zone1 = r.zone, corner1 = r.corner + 1, dv = r.dv, hitId = r.hitId,
            });
            while (car.history.Count > HistoryMax) car.history.RemoveAt(0);
            return true;
        }

        /// <summary>The rest of a hit row's cause, after FaultCause.StampImpact:
        /// what kind of incident, the corner, hitId, stage, size.</summary>
        static CarFault Stamp(CarFault f, RowRec row, IncidentRec r, string kind)
        {
            if (f == null) return null;
            if (r.kind != DamageModel.KHit) f.causeNote = kind + " " + f.causeNote;
            if (row.corner >= 0) f.causeNote += " " + DamageModel.CornerNames[Mathf.Clamp(row.corner, 0, 3)];
            f.hitId = r.hitId;
            if (row.id == DamageModel.GlassId) f.cost = DamageHomes.GlassCost(f.cost, f.stage, row.stage);   // R1c-A: priced by stage
            f.stage = row.stage;
            f.mag = row.mag;
            f.corner1 = row.corner + 1;
            // the legacy effect path reads severity: stage 3 is "severe"
            f.severity = row.stage >= 3 ? 2f : 1f;
            return f;
        }

        /// <summary>The v26 step for one car: an event list. A row's stage is
        /// read through <see cref="StageOf"/> (legacy severity 1 -> 2, 2 -> 3;
        /// design §2), never written here, so the step re-runs as a no-op.</summary>
        public static void MigrateCar(OwnedCar car)
        {
            if (car == null) return;
            if (car.history == null) car.history = new List<CarEvent>();
        }
    }
}
