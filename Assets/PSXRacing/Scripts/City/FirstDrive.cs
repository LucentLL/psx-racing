using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// The guided first drive (owner, 2026-11-06): the basics and what can go
    /// wrong with the car, taught on the road. Stop at a stop sign, stop at a
    /// light, park in a bay, get out and pump gas. A new career that answers NO
    /// to "Do you already have a license?" arrives here once, in free roam.
    ///
    /// Every step accepts ANY sign, light or bay that fits rather than one chosen
    /// spot: the map's line points at the nearest, but a player who comes to a
    /// proper stop somewhere else has still done what was asked.
    /// </summary>
    public sealed class FirstDrive : MonoBehaviour
    {
        public static bool Active { get; private set; }
        /// <summary>The line for the HUD's notice slot, or null.</summary>
        public static string Prompt { get; private set; }
        /// <summary>Where the map's line points, while there is such a place.</summary>
        public static bool HasTarget { get; private set; }
        public static Vector2 Target { get; private set; }
        public static string TargetLabel { get; private set; }

        enum Step { Wait, Go, StopSign, Light, LightWait, Park, Pump, Done }

        const float StopKmh = 1.5f;
        /// <summary>How long under StopKmh is a stop. A brake pedal held down
        /// selects reverse 0.4 s after 2 km/h (CarController.UpdateGearbox's
        /// reverseHold) and is back over StopKmh about 0.5 s after going under it,
        /// so anything longer never credits a driver who keeps the pedal down.</summary>
        const float StopDwell = 0.3f;
        const float LessonSeconds = 9f;
        const float PraiseSeconds = 4f;
        const float DoneSeconds = 12f;

        CityMode mode;
        CityMap map;
        System.Collections.Generic.IReadOnlyList<CitySignals.Junction> junctions;
        Step step;
        float stepAt, stoppedFor, praiseUntil, nextScan;
        string praise;
        int greens;
        bool sawOnFoot;
        // The approach being driven into, so a line crossed without stopping can
        // be told from a stop made behind it.
        bool haveLine;
        CitySignals.Approach line;

        void Start()
        {
            mode = CityMode.Instance;
            if (mode == null || mode.world == null || mode.player == null) { enabled = false; return; }
            map = mode.world.Map;
            junctions = map != null ? CitySignals.All(map, CityWorld.TrimsFor(map)) : null;
            Active = true;
            Enter(Step.Wait);
        }

        void OnDestroy() => Clear();

        static void Clear()
        {
            Active = false;
            Prompt = null;
            HasTarget = false;
            TargetLabel = null;
        }

        void Enter(Step s, string saidFirst = null)
        {
            step = s;
            stepAt = Time.time;
            stoppedFor = 0f;
            haveLine = false;
            HasTarget = false;
            nextScan = 0f;
            if (saidFirst != null) { praise = saidFirst; praiseUntil = Time.time + PraiseSeconds; }
        }

        float Kmh => Mathf.Abs(mode.player.forwardSpeed) * 3.6f;
        Vector2 Plan => mode.PlayerPlan;
        Vector2 Forward
        {
            get
            {
                var f = mode.player.transform.forward;
                return new Vector2(f.x, f.z).normalized;
            }
        }

        static string GasKey() => TouchControls.Instance != null && TouchControls.Instance.Visible ? "THE GAS PEDAL"
            : UnityEngine.InputSystem.Gamepad.current != null ? "RIGHT TRIGGER" : "W";
        static string BrakeKey() => TouchControls.Instance != null && TouchControls.Instance.Visible ? "THE BRAKE PEDAL"
            : UnityEngine.InputSystem.Gamepad.current != null ? "LEFT TRIGGER" : "S";

        void Say(string objective, string lesson = null)
        {
            if (Time.time < praiseUntil) { Prompt = praise; return; }
            bool teaching = lesson != null && Time.time - stepAt < LessonSeconds + PraiseSeconds;
            Prompt = teaching ? lesson : objective;
        }

        void Update()
        {
            switch (step)
            {
                case Step.Wait:
                    Prompt = null;
                    if (mode.Live) Enter(Step.Go);
                    break;

                case Step.Go:
                    Prompt = "YOUR FIRST DRIVE\n" + GasKey() + " TO GO, " + BrakeKey() + " TO SLOW AND STOP";
                    if (Kmh > 25f || Time.time - stepAt > 20f) Enter(Step.StopSign);
                    break;

                case Step.StopSign:
                    Say("STOP AT THE STOP SIGN\nCOME TO A FULL STOP BEHIND THE LINE",
                        "FOLLOW THE LINE ON THE MAP\nTHE TOP OF THE SCREEN SAYS HOW FAR");
                    AimAtNearest(false, "STOP SIGN");
                    if (WatchLine(false, out bool signRan))
                        Enter(Step.Light, "GOOD. A FULL STOP, THEN GO WHEN IT IS CLEAR");
                    else if (signRan)
                    {
                        praise = "YOU ROLLED THROUGH THAT ONE\nTRY THE NEXT";
                        praiseUntil = Time.time + PraiseSeconds;
                    }
                    break;

                case Step.Light:
                    Say("STOP AT THE NEXT RED LIGHT\nGREEN MEANS KEEP GOING",
                        "WATCH THE TEMP GAUGE. IF TEMP BLINKS RED THE ENGINE IS OVERHEATING\nEASE OFF, OR IT WILL LET GO");
                    AimAtNearest(true, "TRAFFIC LIGHT");
                    if (WatchLine(true, out bool ranLight))
                        Enter(Step.LightWait);
                    else if (ranLight)
                    {
                        // Which colour it was is decided where it was crossed.
                        if (lastCrossAspect == CitySignals.Aspect.Green)
                        {
                            greens++;
                            if (greens >= 3) { Enter(Step.Park, "GREEN MEANS GO, RED MEANS STOP. YOU HAVE IT"); break; }
                            praise = "THAT ONE WAS GREEN, SO YOU KEEP GOING\nSTOP FOR THE NEXT RED";
                        }
                        else praise = "THAT LIGHT WAS NOT GREEN. STOP AT THE LINE NEXT TIME";
                        praiseUntil = Time.time + PraiseSeconds;
                    }
                    break;

                case Step.LightWait:
                    Prompt = "GOOD. WAIT FOR THE GREEN, THEN GO";
                    if (CitySignals.AspectNow(line) == CitySignals.Aspect.Green && Kmh > 5f)
                        Enter(Step.Park);
                    break;

                case Step.Park:
                    Say("PARK IN A PARKING SPOT\nSTOP BETWEEN THE LINES OF ANY BAY",
                        "A COOLANT LEAK MAKES AN ENGINE OVERHEAT\nCHECK COOLANT UNDER MY CARS BEFORE A DRIVE");
                    if (Time.time >= nextScan) { nextScan = Time.time + 2f; AimAtLot(); }
                    stoppedFor = InBay() && Kmh < StopKmh ? stoppedFor + Time.deltaTime : 0f;
                    if (stoppedFor > StopDwell)
                        Enter(Step.Pump, "PARKED. BUMPS AND HITS WEAR THE CAR, AND WORN PARTS FAIL\nDELMAR AUTO FIXES THEM");
                    break;

                case Step.Pump:
                    Say("DRIVE TO THE GAS STATION AND STOP AT A PUMP\nTHEN GET OUT AND PUMP GAS",
                        "OUT OF FUEL, THE CAR STOPS\nTHE PAUSE MENU CAN CALL A FUEL TRUCK, BUT IT COSTS MORE");
                    if (Time.time >= nextScan) { nextScan = Time.time + 2f; AimAtStation(); }
                    if (OnFoot.ForecourtMode.OnFoot) sawOnFoot = true;
                    else if (sawOnFoot)
                        Enter(Step.Done);
                    else
                    {
                        // A full tank is offered no pump and Charlotte has no other
                        // door (ForecourtMode.TickInCar), and the lesson just above
                        // offers the truck that fills it: stopped here is enough.
                        var tank = mode.player.GetComponent<FuelTank>();
                        bool full = GasPump.AtPump && tank != null && tank.percent >= 99.5f;
                        stoppedFor = full && Kmh < StopKmh ? stoppedFor + Time.deltaTime : 0f;
                        if (stoppedFor > StopDwell) Enter(Step.Done);
                    }
                    break;

                case Step.Done:
                    Prompt = "THAT IS THE BASICS. THE ROAD IS YOURS\nTO GO HOME, OPEN THE MENU AND PICK END DRIVE";
                    if (Time.time - stepAt > DoneSeconds)
                    {
                        // Over for good: RESTART reloads this scene with the handoff as it stands.
                        RaceHandoff.FirstDrive = false;
                        Clear();
                        enabled = false;
                    }
                    break;
            }
        }

        CitySignals.Aspect lastCrossAspect;

        /// <summary>
        /// The approach the car is driving into, if any: the same way it faces,
        /// the line ahead or just passed, and within its road.
        /// </summary>
        bool FindApproach(bool signal, out CitySignals.Approach best)
        {
            best = default;
            if (junctions == null) return false;
            var p = Plan;
            var f = Forward;
            float bestD = 30f;
            bool found = false;
            for (int i = 0; i < junctions.Count; i++)
            {
                var j = junctions[i];
                if (j.signal != signal) continue;
                if ((j.centre - p).sqrMagnitude > 80f * 80f) continue;
                foreach (var a in j.approaches)
                {
                    if (!signal && a.ctl != 2) continue;
                    if (Vector2.Dot(a.u, f) < 0.7f) continue;
                    var to = a.at - p;
                    float ahead = Vector2.Dot(to, a.u);
                    if (ahead < -6f || ahead > bestD) continue;
                    if (Mathf.Abs(Vector2.Dot(to, a.Right)) > 12f) continue;
                    bestD = ahead;
                    best = a;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>True on a proper stop behind a line; <paramref name="ran"/> on
        /// the frame the line was crossed without one.</summary>
        bool WatchLine(bool signal, out bool ran)
        {
            ran = false;
            if (haveLine)
            {
                float ahead = Vector2.Dot(line.at - Plan, line.u);
                if (ahead < -4f)
                {
                    ran = true;
                    lastCrossAspect = signal ? CitySignals.AspectNow(line) : default;
                    haveLine = false;
                    stoppedFor = 0f;
                    return false;
                }
                if (ahead > 40f) haveLine = false;
            }
            if (!haveLine && FindApproach(signal, out var a) && Vector2.Dot(a.at - Plan, a.u) > -1f)
            {
                line = a;
                haveLine = true;
            }
            if (!haveLine) return false;

            float d = Vector2.Dot(line.at - Plan, line.u);
            bool stopped = Kmh < StopKmh && d > -1.5f && d < 15f;
            // A light is only a stop while it says stop.
            if (signal && CitySignals.AspectNow(line) == CitySignals.Aspect.Green) stopped = false;
            stoppedFor = stopped ? stoppedFor + Time.deltaTime : 0f;
            return stoppedFor > StopDwell;
        }

        void AimAtNearest(bool signal, string label)
        {
            // A line in hand: it stays the target until it is stopped at or crossed.
            // The scan below skips anything within 40 m, so it would send the map's
            // line off to the next junction while the car is braking for this one.
            if (haveLine) { SetTarget(true, line.at, label); return; }
            if (Time.time < nextScan) return;
            nextScan = Time.time + 2f;
            if (junctions == null) return;
            var p = Plan;
            var f = Forward;
            float best = float.MaxValue;
            bool found = false;
            Vector2 at = default;
            for (int i = 0; i < junctions.Count; i++)
            {
                var j = junctions[i];
                if (j.signal != signal) continue;
                if (!signal)
                {
                    bool stopSign = false;
                    foreach (var a in j.approaches) if (a.ctl == 2) { stopSign = true; break; }
                    if (!stopSign) continue;
                }
                var to = j.centre - p;
                float dist = to.magnitude;
                if (dist < 40f) continue;
                // Mildly prefer what is ahead: no U-turn for one a little closer.
                float score = dist * (Vector2.Dot(to / dist, f) > 0f ? 1f : 1.6f);
                if (score < best) { best = score; at = j.centre; found = true; }
            }
            SetTarget(found, at, label);
        }

        void AimAtLot()
        {
            if (map == null || map.lots == null) return;
            var p = Plan;
            float best = float.MaxValue;
            Vector2 at = default;
            bool found = false;
            foreach (var lot in map.lots)
            {
                if (lot.runFoot == null) continue;
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    if (lot.runN[r] == 0) continue;
                    float d = (lot.runFoot[r] - p).sqrMagnitude;
                    if (d < best) { best = d; at = lot.runFoot[r]; found = true; }
                }
            }
            SetTarget(found, at, "PARKING");
        }

        /// <summary>The car's middle between a bay's lines and inside its depth,
        /// in any row of any lot.</summary>
        bool InBay()
        {
            if (map == null || map.lots == null) return false;
            var p = Plan;
            foreach (var lot in map.lots)
            {
                if (lot.runFoot == null) continue;
                for (int r = 0; r < lot.runFoot.Length; r++)
                {
                    if ((lot.runFoot[r] - p).sqrMagnitude > 200f * 200f) continue;
                    var u = lot.runU != null ? lot.runU[r] : lot.u;
                    var v = new Vector2(-u.y, u.x);
                    var rel = p - lot.runFoot[r];
                    float along = Vector2.Dot(rel, u) / CityMeshes.StallW;
                    float deep = Vector2.Dot(rel, v);
                    if (along < 0f || along > lot.runN[r]) continue;
                    if (deep < 0.5f || deep > CityMeshes.StallD - 0.5f) continue;
                    float frac = along - Mathf.Floor(along);
                    if (frac > 0.15f && frac < 0.85f) return true;
                }
            }
            return false;
        }

        void AimAtStation()
        {
            if (map == null || map.stations == null) return;
            var p = Plan;
            float best = float.MaxValue;
            Vector2 at = default;
            bool found = false;
            foreach (var st in map.stations)
            {
                float d = (st.origin - p).sqrMagnitude;
                if (d < best) { best = d; at = st.origin; found = true; }
            }
            SetTarget(found, at, "GAS STATION");
        }

        void SetTarget(bool found, Vector2 at, string label)
        {
            HasTarget = found;
            Target = at;
            TargetLabel = label;
        }
    }
}
