using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.LifeSim
{
    /// <summary>R1c-A: what the career carries after a crash (LifeState.fate, v27).</summary>
    [System.Serializable]
    public class CrashFate
    {
        /// <summary>The seat belt: ON unless the player takes it off (the pause menu).</summary>
        public bool beltOff;
        // the worst injury of a drive, waiting for the home screen (CrashFates.Process)
        public int pending;
        public float pendingScore;
        public int pendingDay;
        public string pendingCar = "", pendingWhere = "", pendingHit = "";
        public int pendingMph;
        // recovering: no work, no driving before injuredUntil
        public int injuredFrom, injuredUntil;
        public string injuryWhat = "";
        // the one hospital summary
        public int hospDays, hospBill, hospInsurer, hospDeductible, hospPaid, hospFromDay;
        public string hospNote = "";
        /// <summary>What the player could not pay of a hospital bill (R1c-B's BILLS can carry it).</summary>
        public int hospitalOwed;
        /// <summary>The page due once: "hospital" (the summary) or "" (none). The
        /// write-off and the obituary are due while their state stands.</summary>
        public string page = "";
        // the write-off decision (the insurer asked once, CrashHooks.TotalLoss)
        public string writeOffCar = "";
        public int writeOffEstimate, writeOffValue, writeOffPayout, writeOffDeductible;
        public string writeOffNote = "";
        // game over
        public bool dead, deathLocked;
        public int deathDay;
        public string obituary = "";
    }

    /// <summary>
    /// R1c-A "WHAT A CRASH COSTS YOU: THE CAR AND THE DRIVER" (Fault_System_Design
    /// 2026-11-11, OWNER'S ANSWERS 2026-11-13: the owner's idea, KEEP INJURY
    /// SIMPLE, NO MODE CHOICE). Save v27.
    ///
    /// THE DRIVER (PSX_INJURY=0: nobody is ever hurt, no belt row). Every
    /// banked pulse is read as an injury SCORE in m/s, as if a belted driver
    /// took it square on: delta-V x direction (side 1.4, rear 0.7) x a narrow
    /// tree or pole 1.3 x the structural FAIL's intrusion 1.15 x the belt (off:
    /// 1.6 head-on, x2.5 in a roll) x the bags that fired (0.85) x a race car's
    /// cage (0.55) x HEALTH (0.9 at 100 to 1.2 at 0). Under 9.5 nothing; 9.5-15 a
    /// CONCUSSION (the picture blurs and goes dark, the drive ends with the
    /// tow); 15-28 HOSPITAL (a broken arm, a broken leg, a broken leg and ribs:
    /// days there, the bill through CrashHooks.Hospital, no work or driving
    /// until recovered, HEALTH stretching the recovery 0.8-1.4x, shifts are
    /// unpaid medical leave and never a strike); 28 and over DEATH (game over:
    /// an obituary in the Herald, the career locked, NEW GAME). No mode choice.
    ///
    /// THE CAR (PSX_FAIL_STAYS=0: a FAIL car drives again after the tow). A
    /// structural FAIL leaves a hidden row for what failed (frame, subframe,
    /// knuckle, roof structure) and the car does not move under its own power
    /// until a shop has repaired it; the shops tow it in. PSX_WRITE_OFF=0: no
    /// write-off. A real crash (5 m/s, or a FAIL) whose repair at Delmar's
    /// would cost more than 75% of what the car was worth totals it: keep it
    /// on a salvage title or junk it, the payout from CrashHooks.TotalLoss.
    /// </summary>
    public static class CrashFates
    {
        static string Env(string k) => System.Environment.GetEnvironmentVariable(k);
        public static bool InjuryOn = Env("PSX_INJURY") != "0";
        public static bool FailStaysOn = Env("PSX_FAIL_STAYS") != "0";
        public static bool WriteOffOn = Env("PSX_WRITE_OFF") != "0";

        public const int None = 0, Concussion = 1, Hospital = 2, Death = 3;
        public static string OutcomeName(int o) =>
            o == Concussion ? "concussion" : o == Hospital ? "hospital" : o == Death ? "death" : "none";

        public const float ConcussAt = 9.5f, HospitalAt = 15f, LegAt = 19f, LongAt = 23f, DeathAt = 28f;
        public const float WriteOffShare = 0.75f;
        public const int TitleSalvage = 2;

        // =================== the driver ===================
        public struct Occupant
        {
            public bool belted, bagsFired, cage;
            public int bags;
            public float health;
        }

        public static float HealthMult(float health) => Mathf.Lerp(1.2f, 0.9f, Mathf.Clamp01(health / 100f));
        public static float RecoveryMult(float health) => Mathf.Lerp(1.4f, 0.8f, Mathf.Clamp01(health / 100f));

        /// <summary>The injury score of one pulse (pure).</summary>
        public static float Score(int kind, int zone, int corner, float dv, int quarters, bool narrow, int fail, Occupant o)
        {
            float s;
            if (kind == DamageModel.KRoll) s = (dv + 2f * Mathf.Max(0, quarters)) * (o.belted ? 1f : 2.5f);
            else if (kind == DamageModel.KDrop) s = dv * (o.belted ? 1f : 1.3f);
            else if (kind == DamageModel.KHit)
            {
                bool frontal = zone == DamageModel.ZFront || (zone == DamageModel.ZCorner && corner >= 0 && corner < 2);
                float dir = zone == DamageModel.ZSide ? 1.4f : zone == DamageModel.ZRear ? 0.7f :
                            zone == DamageModel.ZCorner ? (frontal ? 1f : 0.8f) : zone == DamageModel.ZUnder ? 0.6f : 1f;
                float belt = o.belted ? 1f : zone == DamageModel.ZSide ? 1.2f : zone == DamageModel.ZRear ? 1.1f : 1.6f;
                float bag = o.bagsFired && frontal ? (o.belted ? 0.85f : 0.8f) : 1f;
                s = dv * dir * belt * bag * (narrow ? 1.3f : 1f);
            }
            else return 0f;   // a bump stop, the water: nobody is hurt
            if (fail != DamageRules.FailNone) s *= 1.15f;
            if (o.cage) s *= 0.55f;
            return s * HealthMult(o.health);
        }

        public static int OutcomeOf(float score) =>
            score >= DeathAt ? Death : score >= HospitalAt ? Hospital : score >= ConcussAt ? Concussion : None;

        public struct Stay { public int tier, hospitalDays, recoveryDays, bill; public string what; }

        /// <summary>A hospital-level score as one stay, 1999 prices (pure).</summary>
        public static Stay StayFor(float score, float health)
        {
            var st = new Stay { tier = score >= LongAt ? 3 : score >= LegAt ? 2 : 1 };
            float lo = st.tier == 1 ? HospitalAt : st.tier == 2 ? LegAt : LongAt;
            float hi = st.tier == 1 ? LegAt : st.tier == 2 ? LongAt : DeathAt;
            float t = Mathf.Clamp01((score - lo) / (hi - lo));
            st.what = st.tier == 1 ? "a broken arm" : st.tier == 2 ? "a broken leg" : "a broken leg and cracked ribs";
            st.hospitalDays = st.tier == 1 ? 1 + Mathf.RoundToInt(t) : st.tier == 2 ? 3 + Mathf.RoundToInt(2f * t) : 8 + Mathf.RoundToInt(6f * t);
            float bill = st.tier == 1 ? Mathf.Lerp(2400f, 4000f, t) : st.tier == 2 ? Mathf.Lerp(11000f, 18000f, t) : Mathf.Lerp(32000f, 60000f, t);
            st.bill = Mathf.RoundToInt(bill / 10f) * 10;
            float rec = st.tier == 1 ? Mathf.Lerp(21f, 28f, t) : st.tier == 2 ? Mathf.Lerp(42f, 56f, t) : Mathf.Lerp(63f, 84f, t);
            st.recoveryDays = Mathf.Max(st.hospitalDays + 1, Mathf.RoundToInt(rec * RecoveryMult(health)));
            return st;
        }

        public static bool BeltOff(LifeState s) => InjuryOn && s != null && s.fate != null && s.fate.beltOff;

        /// <summary>The pause menu's row (career drives only).</summary>
        public static bool BeltRowOffered => InjuryOn && RaceHandoff.FromLifeSim && Edition.HasCareer && LifeSimManager.State != null;
        public static string BeltLabel() => "SEAT BELT: " + (BeltOff(LifeSimManager.State) ? "OFF" : "ON");
        public static void ToggleBelt()
        {
            var s = LifeSimManager.State;
            if (s == null) return;
            if (s.fate == null) s.fate = new CrashFate();
            s.fate.beltOff = !s.fate.beltOff;
            LifeSimManager.Save();
        }

        /// <summary>Who sits in this drive's car.</summary>
        public static Occupant DriveOccupant()
        {
            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId);
            return new Occupant
            {
                belted = !BeltOff(s),
                bags = SrsBag.Fitted(),
                cage = spec != null && (spec.IsRaceCar || spec.IsRallyCar),
                health = s != null ? s.health : 70f,
            };
        }

        /// <summary>The ledger, after every resolve on the career car: the
        /// incident's injury (it only grows) and, from a concussion up, the
        /// picture going.</summary>
        public static void OnIncident(CarController car, Incident inc, bool banks)
        {
            if (!InjuryOn || !banks || inc == null) return;
            var o = DriveOccupant();
            o.bagsFired = inc.outcome.HasRow(SrsBag.Id);
            float sc = Score(inc.kind, inc.zone, inc.corner, inc.dv, inc.quarters, inc.narrow, inc.fail, o);
            if (sc <= inc.injScore) return;
            inc.injScore = sc;
            int was = inc.injury;
            inc.injury = OutcomeOf(sc);
            if (inc.injury >= Concussion && inc.injury > was)
            {
                Debug.Log("[Fates] " + OutcomeName(inc.injury) + ": score " + sc.ToString("0.0") + " (" +
                          DamageModel.KindName(inc.kind) + " " + DamageModel.ZoneName(inc.zone) + ", dv " + inc.dv.ToString("0.0") +
                          (o.belted ? ", belted" : ", NO BELT") + (o.bagsFired ? ", bags fired" : "") + (o.cage ? ", cage" : "") + ")");
                InjuryFx.Begin(car, inc.injury);
            }
        }

        // =================== the fold (DamageBank.ApplyIncident) ===================
        public const string FrameId = "frame_bent", SubframeId = "subframe_failed", KnuckleId = "knuckle_broken", RoofId = "roof_crushed";
        public static readonly string[] FailIds = { FrameId, SubframeId, KnuckleId, RoofId };
        public static bool IsFailRow(string id) => System.Array.IndexOf(FailIds, id) >= 0;

        /// <summary>The row a FAIL leaves (what DamageRules decided failed).</summary>
        public static string FailRowFor(int fail, int zone)
        {
            if (fail == DamageRules.FailSubframe) return SubframeId;
            if (fail == DamageRules.FailKnuckle) return KnuckleId;
            if (fail == DamageRules.FailRoof) return RoofId;
            return zone == DamageModel.ZCorner ? KnuckleId : zone == DamageModel.ZRoof ? RoofId : FrameId;
        }

        /// <summary>Once per incident, after its rows: the FAIL's row, the
        /// driver's injury, the write-off check.</summary>
        public static void AfterIncident(LifeState s, OwnedCar car, DamageBank.IncidentRec r, int valueBefore)
        {
            if (car == null || r == null) return;
            int day = s != null ? s.day : 0;
            if (FailStaysOn && r.fail != DamageRules.FailNone)
            {
                string id = FailRowFor(r.fail, r.zone);
                if (!car.faults.Exists(f => f != null && f.id == id))
                {
                    var nf = FaultCatalog.MakeById(id, FaultCatalog.FamilyOf(car));
                    if (nf != null)
                    {
                        FaultCause.StampImpact(nf, day, car.odoMiles, r.zone, r.side, r.dv);
                        nf.hitId = r.hitId;
                        nf.stage = 3f;
                        nf.severity = 2f;
                        nf.corner1 = r.corner + 1;
                        car.faults.Add(nf);
                    }
                }
                car.wreckHit = r.hitId;
            }
            KeepInjury(s, car, r);
            if (s == null) return;
            CheckWriteOff(s, car, valueBefore, r);
        }

        /// <summary>The driver's half of an incident: the worst injury of the
        /// day waits for the home screen (also the debug bench's CLEAR ALL
        /// FAULTS, which puts the car right but not the driver).</summary>
        public static void KeepInjury(LifeState s, OwnedCar car, DamageBank.IncidentRec r)
        {
            if (s == null || car == null || r == null) return;
            if (s.fate == null) s.fate = new CrashFate();
            if (InjuryOn && r.inj > None && r.injScore > s.fate.pendingScore && !s.fate.dead)
            {
                var f = s.fate;
                f.pending = OutcomeOf(r.injScore);
                f.pendingScore = r.injScore;
                f.pendingDay = s.day;
                f.pendingCar = car.displayName ?? "";
                f.pendingWhere = Where();
                f.pendingHit = HitWords(r);
                f.pendingMph = Mathf.RoundToInt(r.dv * 2.237f / 5f) * 5;
            }
        }

        static string Where()
        {
            var city = City.CityMode.Instance;
            if (city != null && !city.IsRace) return "in Charlotte";
            var t = TrackCatalog.At(RaceHandoff.TrackIndex);
            return t != null && !string.IsNullOrEmpty(t.name) ? "at " + t.name : "on the road";
        }

        static string HitWords(DamageBank.IncidentRec r)
        {
            if (r.kind == DamageModel.KRoll) return "rolled over";
            if (r.kind == DamageModel.KDrop) return "came down hard off a drop";
            string w = (r.what ?? "").ToLowerInvariant();
            if (r.narrow) return w.Contains("pole") || w.Contains("light") || w.Contains("sign") ? "hit a pole" : "hit a tree";
            if (w.Contains("ai") || w.Contains("traffic") || w.Contains("player") || w.Contains("car")) return "collided with another car";
            return "hit a wall";
        }

        // =================== the home screen ===================
        public static bool Dead(LifeState s) => s != null && s.fate != null && s.fate.dead;

        /// <summary>A page the home screen must show before anything else.</summary>
        public static bool PageDue(LifeState s) =>
            s != null && s.fate != null && (s.fate.dead || s.fate.page == "hospital" || !string.IsNullOrEmpty(s.fate.writeOffCar));

        /// <summary>The pending injury, once: the hospital stay (days pass, the bill
        /// through CrashHooks.Hospital), or death. True when something happened.</summary>
        public static bool Process(LifeState s)
        {
            if (s == null || s.fate == null || s.fate.pending == None || s.fate.dead) return false;
            var f = s.fate;
            int o = f.pending;
            float score = f.pendingScore;
            f.pending = None;
            f.pendingScore = 0f;
            if (o == Concussion)
            {
                s.calendarLog.Add(LifeRules.LogDate(s.day) + ": blacked out at the wheel after the crash " + f.pendingWhere + " — a concussion");
                LifeSimManager.Save();
                return true;
            }
            if (o == Death)
            {
                f.dead = true;
                f.deathDay = f.pendingDay;
                f.obituary = Obituary(s, f);
                s.calendarLog.Add(LifeRules.LogDate(f.pendingDay) + ": killed in a crash " + f.pendingWhere);
                LifeSimManager.Save();   // the last write: the career is locked from here (LifeSimManager.Save)
                return true;
            }
            var st = StayFor(score, s.health);
            f.injuryWhat = st.what;
            f.injuredFrom = f.pendingDay;
            f.injuredUntil = f.pendingDay + st.recoveryDays;
            f.hospFromDay = s.day;
            for (int i = 0; i < st.hospitalDays; i++) LifeRules.SleepUntilMorning(s);
            var pay = CrashHooks.Hospital(s, st.bill);
            int owes = Mathf.Max(0, pay.playerOwes);
            // R1c-B's Charge: what the money covers is paid now, the rest goes on a payment plan
            int planned = Insurance.Charge(s, owes, "the hospital bill");
            int paid = owes - planned;
            f.hospitalOwed += planned;
            f.hospDays = st.hospitalDays;
            f.hospBill = st.bill;
            f.hospInsurer = Mathf.Max(0, pay.insurerPaid);
            f.hospDeductible = Mathf.Max(0, pay.deductible);
            f.hospPaid = paid;
            f.hospNote = pay.note ?? "";
            f.page = "hospital";
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": out of hospital after " + st.hospitalDays + " day" + (st.hospitalDays == 1 ? "" : "s") +
                              " with " + st.what + " — bill " + MenuKit.Money(st.bill) + ", you paid " + MenuKit.Money(paid));
            LifeSimManager.Save();
            return true;
        }

        public static void HospitalSeen(LifeState s)
        {
            if (s == null || s.fate == null) return;
            s.fate.page = "";
            LifeSimManager.Save();
        }

        /// <summary>The Herald's notice. Names no part of the car.</summary>
        public static string Obituary(LifeState s, CrashFate f)
        {
            string name = string.IsNullOrEmpty(s.playerName) ? "A local driver" : s.playerName;
            string car = string.IsNullOrEmpty(f.pendingCar) ? "a car" : "a " + f.pendingCar;
            string hit = string.IsNullOrEmpty(f.pendingHit) ? "crashed" : f.pendingHit;
            string speed = f.pendingMph >= 20 ? " at about " + f.pendingMph + " mph" : "";
            string job = WorkSchedule.Employed(s) ? " The driver delivered pizza for Tony's." : "";
            return name + ", " + s.age + ", of Charlotte, died " + LifeRules.DateLabel(f.pendingDay) + " when " + car + " " +
                   hit + speed + " " + f.pendingWhere + ". Rescue crews pronounced the driver dead at the scene." + job +
                   " Services will be announced.";
        }

        // =================== the car ===================
        /// <summary>A FAIL car that no shop has repaired yet: it does not move.</summary>
        public static bool Undrivable(OwnedCar car) =>
            FailStaysOn && car != null && car.wreckHit != 0 && car.faults != null && car.faults.Exists(f => f != null && IsFailRow(f.id));

        public const string WreckLine = "WRECKED — IT WON'T DRIVE UNTIL A SHOP FIXES IT";

        /// <summary>A shop's tow-in for a car that cannot be driven to them.</summary>
        public static int TowIn(OwnedCar car) => Undrivable(car) ? CareerDrive.TowHookUp : 0;

        public static bool Hurt(LifeState s) => InjuryOn && s != null && s.fate != null && !s.fate.dead && s.day < s.fate.injuredUntil;

        /// <summary>LifeRules.DriveRefusal's player half.</summary>
        public static string HurtRefusal(LifeState s) =>
            Hurt(s) ? "RECOVERING — NO DRIVING UNTIL " + LifeRules.DateLabel(s.fate.injuredUntil) : null;

        /// <summary>Tony's counter.</summary>
        public static string WorkRefusal(LifeState s) =>
            Hurt(s) ? "you are on medical leave until " + LifeRules.DateLabel(s.fate.injuredUntil) : null;

        /// <summary>Duty.Settle: an obligation on a day the player was hurt is
        /// medical leave - unpaid, and never held against them.</summary>
        public static bool ExcusedOn(LifeState s, int day) =>
            InjuryOn && s != null && s.fate != null && s.fate.injuredUntil > 0 && day >= s.fate.injuredFrom && day < s.fate.injuredUntil;

        public static int RepairEstimate(LifeState s, OwnedCar car)
        {
            int sum = 0;
            if (car == null || car.faults == null) return 0;
            foreach (var f in car.faults)
            {
                if (f == null || f.cause != FaultCause.Impact) continue;
                var venue = DamageHomes.DealerOnly(f.id) ? FaultCatalog.Venue.Dealer : FaultCatalog.Venue.Mechanic;
                sum += FaultCatalog.GetQuote(s, car, f, venue).price;
            }
            return sum;
        }

        public static bool IsWriteOff(int estimate, int value) => value > 0 && estimate > WriteOffShare * value;

        static void CheckWriteOff(LifeState s, OwnedCar car, int valueBefore, DamageBank.IncidentRec r)
        {
            if (!WriteOffOn || car.title == TitleSalvage || !string.IsNullOrEmpty(s.fate.writeOffCar)) return;
            if (r.fail == DamageRules.FailNone && r.band < 3) return;   // a real crash, not a scuff
            int est = RepairEstimate(s, car);
            if (!IsWriteOff(est, valueBefore)) return;
            var pay = CrashHooks.TotalLoss(s, car, valueBefore);   // asked ONCE: a claim may move a premium
            // what the payout left owing on the car's loan (R1c-B pays the lender first)
            if (pay.playerOwes > 0) Insurance.Charge(s, pay.playerOwes, "what the loan still owed on the " + car.displayName);
            var f = s.fate;
            f.writeOffCar = car.id;
            f.writeOffEstimate = est;
            f.writeOffValue = valueBefore;
            f.writeOffPayout = Mathf.Max(0, pay.insurerPaid);
            f.writeOffDeductible = Mathf.Max(0, pay.deductible);
            f.writeOffNote = pay.note ?? "";
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": the " + car.displayName + " is a write-off — repairs about " +
                              MenuKit.Money(est) + ", worth " + MenuKit.Money(valueBefore));
        }

        public static OwnedCar WriteOffCar(LifeState s) =>
            s == null || s.fate == null || string.IsNullOrEmpty(s.fate.writeOffCar) ? null : s.FindCar(s.fate.writeOffCar);

        /// <summary>KEEP IT: a salvage title; an insurer keeps back the salvage.</summary>
        public static int KeepNet(LifeState s, OwnedCar car) => Mathf.Max(0, s.fate.writeOffPayout - Room.ScrapValue(car));
        /// <summary>JUNK IT: the insurer's payout (they take the car), or the yard's scrap; a loan is settled from it.</summary>
        public static int JunkNet(LifeState s, OwnedCar car) =>
            (s.fate.writeOffPayout > 0 ? s.fate.writeOffPayout : Room.ScrapValue(car)) - CarMarket.LoanPayoff(s, car.id);

        public static string KeepSalvage(LifeState s)
        {
            var car = WriteOffCar(s);
            if (car == null) { ClearWriteOff(s); return "no car"; }
            int net = KeepNet(s, car);
            s.money += net;
            car.title = TitleSalvage;
            s.calendarLog.Add(LifeRules.LogDate(s.day) + ": kept the " + car.displayName + " on a salvage title" +
                              (net > 0 ? " — " + MenuKit.Money(net) + " from the insurer" : ""));
            ClearWriteOff(s);
            LifeSimManager.Save();
            return null;
        }

        public static string JunkRefusal(LifeState s)
        {
            var car = WriteOffCar(s);
            if (car == null) return "no car";
            int net = JunkNet(s, car);
            return net < 0 && s.money < -net ? "you still owe " + MenuKit.Money(-net) + " on it" : null;
        }

        public static string JunkWreck(LifeState s)
        {
            string why = JunkRefusal(s);
            if (why != null) return why;
            var car = WriteOffCar(s);
            int net = JunkNet(s, car);
            s.money += net;
            ClearWriteOff(s);
            Room.LeaveCareer(s, car, "the " + car.displayName + " went for salvage — " + MenuKit.Money(net));
            LifeSimManager.Save();
            return null;
        }

        static void ClearWriteOff(LifeState s)
        {
            if (s == null || s.fate == null) return;
            s.fate.writeOffCar = "";
            s.fate.writeOffEstimate = s.fate.writeOffValue = s.fate.writeOffPayout = s.fate.writeOffDeductible = 0;
            s.fate.writeOffNote = "";
        }
    }
}
