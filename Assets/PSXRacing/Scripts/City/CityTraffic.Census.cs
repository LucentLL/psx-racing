using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE ROAD-LOGIC CENSUS BY TRAFFIC (2026-10-29, the owner's idea:
    /// "logging traffic traveling the city can help improve road logic").
    /// While <see cref="Log"/> is set, every traffic car is a probe of the road
    /// network it drives, and what it meets goes into the log:
    ///   wreck      what it hit (barrier, building, pole, ground, road surface,
    ///              another car) and the move it was making
    ///   contact    a touch that did not wreck it (a graze along a barrier)
    ///   stuck      under 1 m/s for 10 s with nothing to queue for (a crawl),
    ///              a junction box it cannot clear, a STOP that never lets it
    ///              go, a red that never ends, a left turn that never gets a gap
    ///   lane       a turn lane it could not reach (a replan), a forced change,
    ///              a turn begun from the wrong lane, a lane that ends
    ///   lanes      a straight-through with no lane that continues
    ///   merge      a ramp merge that stalls or waits on the ramp
    ///   offpave    its lane outside the drawn pavement; a turn off the road
    ///   wrongway   across the divider of a two-way road
    ///   route      a U-turn, a one-way with no way on
    ///   vertical   the drawn road under it: a step, a crest that would launch
    ///              a car at the limit, a sag that jolts it
    ///   lateral    a bend tighter than the limit allows, a kink, a heading
    ///              that snaps, a turn tighter than it was planned
    ///   conflict   two cars crossing each other in one junction box
    /// <see cref="CensusStaticAudit"/> adds what needs no driving: phase
    /// groups that give crossing approaches a green together, junctions of
    /// big roads with no control, one-way traps and islands, implausible or
    /// missing limits, straight-throughs whose lanes do not line up, ramps
    /// with no room to merge. <see cref="CensusMode"/> lets a tool tour the
    /// city: cars born over the whole disc round the anchor (no camera rule),
    /// routes leaning to roads not yet driven. Editor/CityTrafficCensus.cs.
    /// </summary>
    public partial class CityTraffic
    {
        /// <summary>The road log the traffic writes into (null: none, no cost).</summary>
        public static CityRoadLog Log;
        /// <summary>A tool touring the city: born anywhere within
        /// <see cref="CensusRadiusM"/> of the anchor, recycled past
        /// <see cref="CensusDespawnM"/>, never a camera rule; routes lean to
        /// roads the log has not seen driven.</summary>
        public static bool CensusMode;
        public static float CensusRadiusM = 430f, CensusDespawnM = 520f;

        const float G = 9.81f;

        sealed class Watch
        {
            public float slowSince = -1f, whySince;
            public int stuckEv = -1;
            public byte lastWhy = 255;
            public float nextSample;
            public int offEv = -1, offKey = -1, wrongEv = -1, wrongKey = -1, turnOffKey = -1;
            public float offSince, wrongSince;
            // the road under it, every metre travelled
            public float dAcc;
            public int hn;
            public readonly float[] hy = new float[8], hd = new float[8], hpsi = new float[8];
            public float vertCool = -9f, stepCool = -9f, latCool = -9f, kinkCool = -9f, snapCool = -9f, contactCool = -9f, noRoadCool = -9f, poseCool = -9f;
            public Vector2 prevTan;
            public bool havePrev;
            public int prevLeg = int.MinValue, latFrom, skipSnapKey = int.MinValue;
            public float mergeSlow = -1f, rampWait = -1f;
            public bool mergeLogged, rampLogged;
        }
        Watch[] watch;

        Watch W(Car c)
        {
            if (watch == null || watch.Length <= c.id) System.Array.Resize(ref watch, Mathf.Max(c.id + 1, cap));
            return watch[c.id] ??= new Watch();
        }

        /// <summary>The tools: may traffic drive this edge (paved, a real
        /// road), and the limit it drives it at (km/h).</summary>
        public bool Routable(int ei) => routable != null && ei >= 0 && ei < routable.Length && routable[ei];
        public float LimitKmh(int ei) => limitMps != null && ei >= 0 && ei < limitMps.Length ? limitMps[ei] * 3.6f : 0f;

        /// <summary>A tool moved the anchor: fill round it quickly.</summary>
        public void CensusArrive() { firstFillUntil = Time.time + 2.5f; nextSpawn = 0f; }

        void CensusSpawned(Car c)
        {
            var w = W(c);
            w.slowSince = -1f; w.stuckEv = -1; w.offEv = -1; w.offKey = -1; w.wrongEv = -1; w.wrongKey = -1; w.turnOffKey = -1;
            w.hn = 0; w.dAcc = 0f; w.havePrev = false; w.mergeSlow = -1f; w.rampWait = -1f; w.mergeLogged = w.rampLogged = false;
            w.nextSample = Time.time + 0.5f;
            Log.spawned++;
        }

        // ---- the log's words ---------------------------------------------------
        string MoveOf(Car c)
        {
            if (c.legCount == 0) return "none";
            ref Leg L = ref c.legs[0];
            if (L.turn) return L.merge ? "merge" : L.exit ? "exit" : L.kind == -1 ? "turn L" : L.kind == 1 ? "turn R" : L.kind == 2 ? "U-turn" : "straight";
            if (c.lc) return "lane change";
            return c.legCount > 1 && c.legs[1].merge ? "ramp" : "run";
        }

        static Vector3 At(Car c) => new Vector3(c.pos.x, c.y, c.pos.y);

        int Note(Car c, string cat, string sub, float value, string detail, int node = int.MinValue, int edge = int.MinValue, int edge2 = int.MinValue)
        {
            int e1 = -1, e2 = -1, n = -1;
            if (c.legCount > 0)
            {
                ref Leg L = ref c.legs[0];
                e1 = L.edge; n = L.node; e2 = L.turn ? L.edge2 : -1;
            }
            if (node != int.MinValue) n = node;
            if (edge != int.MinValue) e1 = edge;
            if (edge2 != int.MinValue) e2 = edge2;
            return Log.Add(cat, sub, n, e1, e2, At(c), c.id, c.speed, value, MoveOf(c), detail, Mathf.Atan2(c.fwd.x, c.fwd.y) * Mathf.Rad2Deg);
        }

        static string HitKind(string what)
        {
            if (string.IsNullOrEmpty(what) || what == "?") return "unknown";
            if (what.StartsWith("CityTraffic_")) return "car";
            if (what.StartsWith("Barriers") || what.StartsWith("Guardrail")) return "barrier";
            if (what.StartsWith("Buildings")) return "building";
            if (what.StartsWith("Roads")) return "road-surface";
            if (what.StartsWith("Ground")) return "ground";
            if (what.StartsWith("Banks")) return "bank";
            if (what.StartsWith(CitySigns.PostName)) return "pole";
            if (what.StartsWith("CityTreeTrunks") || what.StartsWith("Trees")) return "tree";
            if (what.StartsWith("Solid")) return "prop";
            if (what.StartsWith("Water")) return "water";
            return "other";
        }

        void CensusWreck(Car c, string by)
        {
            string what = c.lastContact ?? "?";
            string kind = HitKind(what);
            if (kind == "other" && player != null && what.StartsWith(player.name)) kind = "player";
            Note(c, "wreck", kind, c.hasCommand ? (c.rb.linearVelocity - c.commanded).magnitude : 0f, by);
        }

        void CensusContact(Car c, string what, Vector3 point)
        {
            var w = W(c);
            if (Time.time < w.contactCool) return;
            w.contactCool = Time.time + 1.5f;
            string kind = HitKind(what);
            if (kind == "road-surface" || kind == "car") return;   // the ribbon under it; a bump is its own line
            // where on the car: across (+ right of travel), along (+ ahead), up from the lane's height
            Vector2 d = new Vector2(point.x, point.z) - c.pos;
            float across = c.fwd.y * d.x - c.fwd.x * d.y, along = Vector2.Dot(d, c.fwd), up = point.y - c.y;
            // the ground at the lane height is a speculative contact (PhysX
            // reports surfaces within a step's travel of the box, 0.22 m up):
            // the verge at the road edge, not ground in the car's way
            if ((kind == "ground" || kind == "bank") && up < 0.15f) return;
            string where = Mathf.Abs(across) > c.halfW - 0.15f ? (across > 0f ? "its right side" : "its left side")
                         : Mathf.Abs(along) > c.halfL - 0.2f ? (along > 0f ? "its nose" : "its tail") : "its underside";
            Note(c, "contact", kind, up, $"{what} touched {where} ({across:+0.0;-0.0} m across, {along:+0.0;-0.0} along, {up:0.00} m above the lane height); " + Describe(c));
        }

        // ======================================================================
        //  Every physics step
        // ======================================================================
        void CensusStep(float dt)
        {
            Log.simSeconds += dt;
            for (int i = 0; i < live.Count; i++)
            {
                var c = live[i];
                if (c.wrecked || c.legCount == 0) continue;
                var w = W(c);
                Log.carSeconds += dt;
                ref Leg L = ref c.legs[0];
                if (!L.turn) Log.driven[L.edge] += c.speed * dt;
                Stuck(c, w, ref L);
                Geometry(c, w, ref L, dt);
                if (Time.time >= w.nextSample) { w.nextSample = Time.time + 0.5f; Pavement(c, w, ref L); }
                Merging(c, w, ref L);
            }
            if (Time.time >= nextBoxScan) { nextBoxScan = Time.time + 0.25f; BoxConflicts(); }
        }

        float nextBoxScan;
        readonly Dictionary<int, float> boxCool = new Dictionary<int, float>();

        // ---- stuck ---------------------------------------------------------------
        /// <summary>What held a car (Drive's binding gap): 0 nothing, 1 a car
        /// ahead, 2 a wreck, 3 the player, 4 a car crossing its path; 10 + the
        /// control (1 red / yellow, 2 a car still in the box on its green, 3 a
        /// left turn's oncoming on a green, 4 a STOP, 5 a yield, 6 a left turn's
        /// oncoming at an unsigned junction); 20 the end of a dead end; 21
        /// waiting short of the junction for the lane its turn needs.</summary>
        static string WhyName(byte why)
        {
            switch (why)
            {
                case 0: return "nothing ahead";
                case 1: return "a car ahead";
                case 2: return "a wreck ahead";
                case 3: return "the player";
                case 4: return "a car crossing";
                case 5: return "a car on another road in its lane";
                case 11: return "a red";
                case 12: return "a car in the box on its green";
                case 13: return "oncoming (left on green)";
                case 14: return "a STOP";
                case 15: return "a yield";
                case 16: return "oncoming (left, unsigned)";
                case 20: return "a dead end";
                case 21: return "the lane its turn needs";
                default: return "why " + why;
            }
        }

        void Stuck(Car c, Watch w, ref Leg L)
        {
            if (c.speed >= 1f)
            {
                if (w.stuckEv >= 0) Log.SetValue(w.stuckEv, Time.time - w.slowSince);
                w.stuckEv = -1; w.slowSince = -1f;
                return;
            }
            if (w.slowSince < 0f) { w.slowSince = Time.time; w.whySince = Time.time; w.lastWhy = c.why; return; }
            if (w.stuckEv != -1) return;
            // the time held by THIS reason (a queue that reaches its red has
            // not waited at the red for all of it)
            if (c.why != w.lastWhy) { w.lastWhy = c.why; w.whySince = Time.time; }
            float t = Time.time - Mathf.Max(w.slowSince, w.whySince);
            bool inBox = L.turn;
            string sub;
            float need = 10f;
            switch (c.why)
            {
                case 1:
                    // a queue: the car at its head says why - unless the queue
                    // stands in a junction box
                    if (!inBox) return;
                    sub = "gridlock-box"; need = 15f; break;
                case 2: sub = "behind-wreck"; need = 12f; break;
                case 3: return;
                case 4: sub = inBox ? "gridlock-box" : "crossing-blocked"; need = 15f; break;
                case 5: sub = "other-road-in-lane"; need = 15f; break;
                case 11: sub = "red-never-ends"; need = 60f; break;
                case 12: sub = "box-blocked-on-green"; need = 25f; break;
                case 13: case 16: sub = "left-turn-starved"; need = 45f; break;
                case 14: sub = "stop-deadlock"; need = 30f; break;
                case 15: sub = "yield-starved"; need = 40f; break;
                case 20: return;
                case 21: sub = "lane-change-wait"; need = 12f; break;
                default: sub = inBox ? "crawl-in-turn" : "crawl"; break;
            }
            if (t < need) return;
            int ev = Note(c, "stuck", sub, t, $"held by {WhyName(c.why)} {c.gap:0.0} m ahead, wants {c.vDesCached:0.0} m/s; " + Describe(c),
                             node: c.why >= 10 && c.why < 20 ? c.ctlNode : int.MinValue);
            w.stuckEv = ev >= 0 ? ev : -2;
        }

        // ---- the road under it -------------------------------------------------------
        /// <summary>The drawn road under a plan point: true and its height when
        /// a road collider is there; false over the ground, a walk, a solid
        /// (kind 1) or over a tile not built (kind 2).</summary>
        bool Surface(Vector2 p, float y, out float hitY, out int kind)
        {
            hitY = y; kind = 0;
            var o = new Vector3(p.x, y + 2.5f, p.y);
            if (Physics.Raycast(o, Vector3.down, out var hit, 5f, roadMask, QueryTriggerInteraction.Ignore)) { hitY = hit.point.y; return true; }
            kind = world != null && world.TileLive(p) ? 1 : 2;
            return false;
        }

        void Geometry(Car c, Watch w, ref Leg L, float dt)
        {
            // a heading that SNAPS where one leg hands over to the next (or in
            // a turn's arc): more than 6 deg in one step (a run's own vertices
            // are kinks, below)
            int legKey = LegKey(ref L);
            if (legKey != w.prevLeg) w.latFrom = w.hn;   // the heading's own history starts with each leg
            if (w.havePrev && c.speed > 1f && (L.turn || legKey != w.prevLeg) && legKey != w.skipSnapKey)
            {
                float snap = Vector2.Angle(w.prevTan, c.tan);
                if (snap > 6f && Time.time > w.snapCool)
                {
                    w.snapCool = Time.time + 1.5f;
                    Note(c, "lateral", "heading-snap", snap, $"travel heading jumped {snap:0.0} deg in one step at {c.speed:0.0} m/s {(legKey != w.prevLeg ? "where one leg hands over to the next" : "inside the turn arc")}; " + Describe(c));
                }
            }
            w.prevTan = c.tan; w.havePrev = true; w.prevLeg = legKey;

            w.dAcc += c.speed * dt;
            if (w.dAcc < 1f) return;
            float d = (w.hn > 0 ? w.hd[(w.hn - 1) & 7] : 0f) + w.dAcc;
            w.dAcc = 0f;
            if (!Surface(c.pos, c.y, out float ys, out _)) { w.hn = 0; return; }
            float psi = Mathf.Atan2(c.tan.y, c.tan.x) * Mathf.Rad2Deg;
            if (w.hn > 0) psi = w.hpsi[(w.hn - 1) & 7] + Mathf.DeltaAngle(w.hpsi[(w.hn - 1) & 7], psi);
            int k = w.hn & 7;
            w.hy[k] = ys; w.hd[k] = d; w.hpsi[k] = psi;
            w.hn++;
            if (w.hn < 7) return;
            int i0 = (w.hn - 1) & 7, i1 = (w.hn - 2) & 7, i3 = (w.hn - 4) & 7, i4 = (w.hn - 5) & 7, i6 = (w.hn - 7) & 7;
            float d03 = w.hd[i0] - w.hd[i3], d36 = w.hd[i3] - w.hd[i6], d06 = w.hd[i0] - w.hd[i6];
            if (d03 < 1f || d36 < 1f || d06 > 14f) return;
            float vLim = L.turn ? Mathf.Min(limitMps[L.edge], limitMps[L.edge2]) : limitMps[L.edge];

            // VERTICAL: the grade change over the last ~6 m (+ a sag, - a
            // crest) as the vertical velocity it gives a car at the limit
            // (v x the grade change): over a crest the car floats off by
            // dv^2 / 2g - 1.5 m/s is 11 cm, 2.5 m/s 32 cm, a launch
            float g2 = (w.hy[i0] - w.hy[i3]) / d03, g1 = (w.hy[i3] - w.hy[i6]) / d36;
            float dg = g2 - g1, dvz = vLim * Mathf.Abs(dg);
            if (((dg < 0f && dvz > 1.5f) || (dg > 0f && dvz > 2f)) && Time.time > w.vertCool)
            {
                w.vertCool = Time.time + 2f;
                string sub = dg < 0f ? (dvz > 2.5f ? "crest-launch" : "crest") : "sag-jolt";
                float kv = Mathf.Abs(dg) / (0.5f * d06);
                string floats = dg < 0f ? $" (floats {dvz * dvz / (2f * G) * 100f:0} cm)" : "";
                Note(c, "vertical", sub, dvz,
                     $"grade {g1 * 100f:0.0}% -> {g2 * 100f:0.0}% within {d06:0} m (vertical radius {1f / Mathf.Max(1e-4f, kv):0} m): {dvz:0.0} m/s vertical at the limit {vLim * 3.6f:0} km/h{floats}; " + Describe(c));
            }
            // a STEP: the last metre against the slope before it
            float d01 = w.hd[i0] - w.hd[i1], d14 = w.hd[i1] - w.hd[i4];
            if (d01 > 0.3f && d14 > 1f)
            {
                float dy = w.hy[i0] - w.hy[i1];
                float gLast = dy / d01, gBefore = (w.hy[i1] - w.hy[i4]) / d14;
                if (Mathf.Abs(dy) > 0.08f && Mathf.Abs(gLast - gBefore) > 0.12f && Time.time > w.stepCool)
                {
                    w.stepCool = Time.time + 2f;
                    Note(c, "vertical", "step", Mathf.Abs(dy), $"the road surface {(dy > 0f ? "rises" : "drops")} {Mathf.Abs(dy) * 100f:0} cm in {d01:0.0} m (grade {gBefore * 100f:0}% before); " + Describe(c));
                }
            }

            // LATERAL: the travel heading over the last ~6 m of this leg, at the limit
            if (w.hn - w.latFrom < 7) return;
            float kh = Mathf.Abs(w.hpsi[i0] - w.hpsi[i6]) * Mathf.Deg2Rad / d06;
            if (!L.turn)
            {
                float turn1 = Mathf.Abs(w.hpsi[i0] - w.hpsi[i1]), turn6 = Mathf.Abs(w.hpsi[i0] - w.hpsi[i6]);
                float aLat = vLim * vLim * kh;
                if ((aLat > 6.5f || (aLat > 4.5f && vLim > 13.4f)) && turn1 < 0.5f * turn6 && Time.time > w.latCool)
                {
                    w.latCool = Time.time + 3f;
                    float vOk = Mathf.Sqrt(ALat / Mathf.Max(1e-4f, kh));
                    var re = map.edges[L.edge];
                    Note(c, "lateral", re.link && re.speedKmh <= 0 ? "ramp-bend-no-limit" : "bend-tighter-than-limit", aLat / G,
                         $"radius {1f / Mathf.Max(1e-4f, kh):0} m mid-road: {aLat / G:0.00} g at the limit {vLim * 3.6f:0} km/h (comfortable {vOk * 3.6f:0} km/h); " + Describe(c));
                }
                if (turn1 >= 4f && d01 < 1.6f && turn1 >= 0.6f * turn6 && Time.time > w.kinkCool)
                {
                    w.kinkCool = Time.time + 2f;
                    Note(c, "lateral", "kink", turn1, $"the lane turns {turn1:0.0} deg within {d01:0.0} m mid-road (a corner in the line); " + Describe(c));
                }
            }
            else if (L.kind != 2)
            {
                float a = c.speed * c.speed * kh;
                if (a > 5.5f && Time.time > w.latCool)
                {
                    w.latCool = Time.time + 3f;
                    Note(c, "lateral", "turn-tighter-than-planned", a / G,
                         $"turn arc radius {1f / Mathf.Max(1e-4f, kh):0} m at {c.speed * 3.6f:0} km/h ({a / G:0.00} g; planned {L.vMax * 3.6f:0} km/h); " + Describe(c));
                }
            }
        }

        // ---- pavement, direction (twice a second) ------------------------------------------
        void Pavement(Car c, Watch w, ref Leg L)
        {
            if (!L.turn)
            {
                var e = map.edges[L.edge];
                float s = Mathf.Clamp(L.s0 + L.dir * c.u, 0f, e.length);
                LineModel.Extents(e, s, out float eM, out float eP);
                float over = Mathf.Max((-eM - 0.3f) - (c.lat - c.halfW), (c.lat + c.halfW) - (eP + 0.3f));
                if (over > 0f)
                {
                    if (w.offKey != L.key || Time.time - w.offSince > 30f)
                    {
                        w.offKey = L.key; w.offSince = Time.time;
                        w.offEv = Note(c, "offpave", "lane-outside-pavement", over,
                                       $"lane {c.lane}{(c.lc ? "->" + c.laneTo : "")} of {Lanes(L.edge, L.dir)} at lat {c.lat:0.0}, pavement -{eM:0.0}..+{eP:0.0}, {over:0.00} m over; " + Describe(c));
                    }
                }
                else if (w.offKey == L.key) w.offKey = -1;
                if (!e.oneway && !c.lc)
                {
                    float div = LineModel.DividerLat(e);
                    float wrong = L.dir > 0 ? c.lat - (div + 1.0f) : (div - 1.0f) - c.lat;
                    if (wrong > 0f && (w.wrongKey != L.key || Time.time - w.wrongSince > 30f))
                    {
                        w.wrongKey = L.key; w.wrongSince = Time.time;
                        Note(c, "wrongway", "across-divider", wrong + 1f, $"lat {c.lat:0.0} vs divider {div:0.0}; " + Describe(c));
                    }
                }
                // the drawn road under a run's lane centre
                bool onRoad = Surface(c.pos, c.y, out float ys, out int kind);
                if (!onRoad && kind == 1 && over <= 0f && Time.time > w.noRoadCool)
                {
                    w.noRoadCool = Time.time + 10f;
                    Note(c, "offpave", "no-road-under-lane", 1f, "no road collider under the lane centre (inside the line model pavement); " + Describe(c));
                }
                else if (onRoad && Mathf.Abs(ys - c.y) > 0.35f && Time.time > w.poseCool)
                {
                    w.poseCool = Time.time + 10f;
                    Note(c, "vertical", "pose-off-surface", ys - c.y, $"the traffic height {c.y:0.00} vs the drawn road {ys:0.00}; " + Describe(c));
                }
                return;
            }
            // a turn: the centre and the front corners over the road
            int key = (L.node << 3) ^ (L.edge * 31 + L.edge2);
            if (w.turnOffKey == key) return;
            var right = new Vector2(c.fwd.y, -c.fwd.x);
            int offN = 0, solidN = 0; string which = "";
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = k == 0 ? c.pos : c.pos + c.fwd * c.halfL + right * (k == 1 ? -c.halfW : c.halfW);
                if (Surface(p, c.y, out _, out int kind)) continue;
                if (kind == 2) return;
                offN++;
                which += k == 0 ? " centre" : k == 1 ? " front-left" : " front-right";
                if (Physics.CheckBox(new Vector3(p.x, c.y + 0.8f, p.y), new Vector3(0.15f, 0.5f, 0.15f), Quaternion.identity, 1 << CityWorld.SolidLayer, QueryTriggerInteraction.Ignore)) solidN++;
            }
            if (offN == 0) return;
            w.turnOffKey = key;
            Note(c, "offpave", offN >= 3 || which.Contains("centre") ? "turn-leaves-road" : "turn-clips-corner", offN,
                 $"off the road:{which}{(solidN > 0 ? " (into a solid)" : "")}; " + Describe(c));
        }

        // ---- merges -----------------------------------------------------------------
        void Merging(Car c, Watch w, ref Leg L)
        {
            if (L.turn && (L.merge || L.exit))
            {
                if (c.speed < 2f) { if (w.mergeSlow < 0f) w.mergeSlow = Time.time; }
                else w.mergeSlow = -1f;
                if (!w.mergeLogged && w.mergeSlow >= 0f && Time.time - w.mergeSlow > 4f)
                {
                    w.mergeLogged = true;
                    Note(c, "merge", L.merge ? "merge-stalled" : "exit-stalled", Time.time - w.mergeSlow, $"under 2 m/s for 4 s in the {(L.merge ? "merge" : "diverge")}, held by {WhyName(c.why)}; " + Describe(c));
                }
                return;
            }
            w.mergeSlow = -1f; w.mergeLogged = false;
            // on the ramp, the merge next: stopped waiting for the mainline
            if (!L.turn && c.legCount > 1 && c.legs[1].merge)
            {
                if (c.speed < 1f) { if (w.rampWait < 0f) w.rampWait = Time.time; }
                else { w.rampWait = -1f; w.rampLogged = false; }
                if (!w.rampLogged && w.rampWait >= 0f && Time.time - w.rampWait > 8f && c.why != 1)
                {
                    w.rampLogged = true;
                    Note(c, "merge", "ramp-waiting", Time.time - w.rampWait, $"stopped on the ramp 8 s, held by {WhyName(c.why)}; " + Describe(c), node: c.legs[1].node, edge2: c.legs[1].edge2);
                }
            }
            else { w.rampWait = -1f; w.rampLogged = false; }
        }

        // ---- crossing flows in one box ------------------------------------------------------
        void BoxConflicts()
        {
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                if (a.wrecked || a.boxJ < 0 || a.speed < 2f || a.legCount == 0 || !a.legs[0].turn) continue;
                for (int j = i + 1; j < live.Count; j++)
                {
                    var b = live[j];
                    if (b.wrecked || b.boxJ != a.boxJ || b.inKey == a.inKey || b.speed < 2f || b.legCount == 0 || !b.legs[0].turn) continue;
                    if ((a.pos - b.pos).sqrMagnitude > 5f * 5f || Mathf.Abs(a.y - b.y) > LevelM) continue;
                    if (Mathf.Abs(Vector2.Dot(a.fwd, b.fwd)) > 0.5f) continue;
                    boxCool.TryGetValue(a.boxJ, out float until);
                    if (Time.time < until) continue;
                    boxCool[a.boxJ] = Time.time + 3f;
                    byte ca = ApproachCtl(a.inKey), cb = ApproachCtl(b.inKey);
                    string sub = ca == CtlSignal && cb == CtlSignal ? "signal-crossing-flows"
                               : ca == CtlStop || cb == CtlStop ? "stop-crossing-flows"
                               : ca == CtlNone && cb == CtlNone ? "uncontrolled-crossing" : "yield-crossing-flows";
                    Note(a, "conflict", sub, (a.pos - b.pos).magnitude,
                         $"two cars cross in one junction box {(a.pos - b.pos).magnitude:0.0} m apart ({CtlName(ca)} / {CtlName(cb)}); A: {Describe(a)} | B: {Describe(b)}",
                         edge2: b.legs[0].edge);
                }
            }
        }

        static string CtlName(byte k) => k == CtlSignal ? "signal" : k == CtlStop ? "STOP" : k == CtlYield ? "yield" : "no control";

        byte ApproachCtl(int key)
        {
            if (key < 0) return CtlNone;
            int ei = key >> 1;
            if (ei < 0 || ei >= map.edges.Length) return CtlNone;
            return CtlAt(map.edges[ei], (key & 1) == 1 ? 1 : -1, out _);
        }

        // ---- turns --------------------------------------------------------------------
        void CensusTurnDone(Car c, ref Leg T)
        {
            Log.Move(T.edge, T.dir, T.edge2);
            if (T.kind != 2) return;
            var e = map.edges[T.edge];
            string sub = T.edge2 != T.edge ? "hairpin-turn" : degree[T.node] <= 1 ? "u-turn-dead-end" : "u-turn-forced";
            Note(c, "route", sub, degree[T.node],
                 sub == "hairpin-turn" ? $"a turn of more than 150 deg at node {T.node} from e{T.edge} '{e.name}' into e{T.edge2} '{map.edges[T.edge2].name}' (taken at 3 m/s as a U-turn)"
                 : $"a U-turn at node {T.node} (degree {degree[T.node]}) on e{T.edge} '{e.name}' cls{e.cls}{(sub == "u-turn-forced" ? ": no other legal way on, or the 1-in-1000 pick" : ": a dead end")}");
        }

        static int LegKey(ref Leg L) => L.turn ? (L.node * 7919) ^ (L.edge * 31 + L.edge2) : L.key;

        void CensusWrongLaneTurn(Car c, ref Leg N)
        {
            W(c).skipSnapKey = LegKey(ref N);   // its snap is this, not the road's
            string kind = N.merge ? "merge" : N.exit ? "exit" : N.kind == -1 ? "left" : N.kind == 1 ? "right" : N.kind == 2 ? "U-turn" : "straight on";
            Note(c, "lane", "turn-from-wrong-lane", Mathf.Abs(c.lane - N.laneIn),
                 $"began its {kind} at node {N.node} from lane {c.lane}{(c.lc ? " (still changing to " + c.laneTo + ")" : "")}; the turn is laid from lane {N.laneIn}: it cuts across in the box; " + Describe(c),
                 node: N.node, edge: N.edge, edge2: N.edge2);
        }

        // ---- route bias (a tool touring the city) ------------------------------------------
        float CensusRouteBonus(int ei, CityMap.Edge o)
        {
            float b = 1f;
            if (Log != null && Log.driven[ei] < 1f) b *= 3f;
            if (o.link) b *= 2f;
            return b;
        }

        bool CensusSpawnPoint(out int ei, out float s)
        {
            s = 0f;
            float r = CensusRadiusM * Mathf.Sqrt(Random.value), a = Random.value * Mathf.PI * 2f;
            var p = playerPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (!map.NearestRoadPoint(p, 45f, false, out ei, out s, out _)) { if (Log != null) Log.spNoRoad++; return false; }
            if (spawnW[ei] <= 0f) { if (Log != null) Log.spNoSpawn++; return false; }
            float accept = (0.25f + 0.75f * spawnW[ei] / maxSpawnW) * (Log != null && Log.driven[ei] >= 1f ? 0.5f : 1f);
            if (Random.value < accept) return true;
            if (Log != null) Log.spReject++;
            return false;
        }

        // ======================================================================
        //  The audit that needs no driving
        // ======================================================================
        /// <summary>Everything about the network a car need not drive to
        /// find: logged as events with value = how bad, move "static".</summary>
        Vector2 mapMin, mapMax, coreMin, coreMax;
        bool haveBounds;

        /// <summary>Near where the data stops (the whole map's box, or for a
        /// local street the core streets' box): a road cut there is no dead
        /// end of the city's.</summary>
        bool AtMapEdge(int n, int cls)
        {
            // the data's own frontier (section FRNT, lib/frontier.mjs): where
            // the fetch boxes or the exporter's drops end the road, exactly -
            // the old guess below (400 m from the outermost node) missed most
            // of them (I-77 at the SC line, Albemarle Road, I-85 north)
            if (map.nodeFrontier != null) return map.Frontier(n);
            if (!haveBounds || n < 0) return false;
            var p = map.nodes[n];
            float d = Mathf.Min(Mathf.Min(p.x - mapMin.x, mapMax.x - p.x), Mathf.Min(p.y - mapMin.y, mapMax.y - p.y));
            if (d < 400f) return true;
            if (cls > 1) return false;
            float dc = Mathf.Min(Mathf.Min(p.x - coreMin.x, coreMax.x - p.x), Mathf.Min(p.y - coreMin.y, coreMax.y - p.y));
            return dc < 150f;
        }

        void Bounds()
        {
            mapMin = coreMin = new Vector2(float.MaxValue, float.MaxValue);
            mapMax = coreMax = -mapMin;
            foreach (var e in map.edges)
                foreach (int n in new[] { e.a, e.b })
                {
                    var p = map.nodes[n];
                    mapMin = Vector2.Min(mapMin, p); mapMax = Vector2.Max(mapMax, p);
                    if (e.cls == 0 && !e.link) { coreMin = Vector2.Min(coreMin, p); coreMax = Vector2.Max(coreMax, p); }
                }
            if (coreMin.x > coreMax.x) { coreMin = mapMin; coreMax = mapMax; }
            haveBounds = true;
        }

        public void CensusStaticAudit(CityRoadLog log, StringBuilder report)
        {
            int ne = map.edges.Length, nn = map.nodes.Length;
            Bounds();
            if (map.nodeFrontier != null)
            {
                int[] why = new int[5]; int fr = 0;
                for (int n = 0; n < map.nodeFrontier.Length; n++)
                {
                    byte b = map.nodeFrontier[n];
                    if (b == 0) continue;
                    fr++;
                    for (int k = 0; k < 5; k++) if ((b & (1 << k)) != 0) why[k]++;
                }
                report.AppendLine($"  the data's frontier (section FRNT): {fr} nodes where the data stops and the road goes on - outside the beltway box {why[0]}, minor streets not fetched there {why[1]}, a dropped way {why[2]}, a one-way into what the map lacks {why[3]}, a road joined to nothing else {why[4]}: the data's end, not a dead end");
            }
            else report.AppendLine($"  the map: ({mapMin.x:0},{mapMin.y:0})..({mapMax.x:0},{mapMax.y:0}); local streets ({coreMin.x:0},{coreMin.y:0})..({coreMax.x:0},{coreMax.y:0}) - a road cut within 400 m of the map's edge (150 m of the streets' box for a local) is the data's end, not a dead end");
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 P(Vector2 p, float y) => new Vector3(p.x, y, p.y);

            // ---- 1. SIGNALS: crossing approaches in one phase group
            int sigJ = 0, stopJ = 0, allWay = 0, conflictPairs = 0;
            var js = CitySignals.All(map, trims);
            if (js != null)
                foreach (var j in js)
                {
                    if (!j.signal) { stopJ++; if (j.allWay) allWay++; continue; }
                    sigJ++;
                    for (int a = 0; a < j.approaches.Count; a++)
                        for (int b = a + 1; b < j.approaches.Count; b++)
                        {
                            var A = j.approaches[a]; var B = j.approaches[b];
                            if (A.edge == B.edge || A.group != B.group) continue;
                            float ang = Vector2.Angle(A.u, B.u);
                            if (ang < 55f || ang > 125f) continue;
                            conflictPairs++;
                            log.Add("signal", "conflicting-green", A.node, A.edge, B.edge, P(j.centre, j.roadY), -1, 0f, 1f, "static",
                                    $"junction {j.id}: approaches e{A.edge} '{map.edges[A.edge].name}' and e{B.edge} '{map.edges[B.edge].name}' cross at {ang:0} deg and are both phase group {A.group}: green together");
                        }
                }
            report.AppendLine($"  signals: {sigJ} signalised junctions, {stopJ} STOP junctions ({allWay} all-way); crossing approaches green together: {conflictPairs} pairs");

            // ---- 2. JUNCTION CONTROL where no signal or STOP stands: big roads
            // crossing with nobody given the way
            int noCtl = 0, unsigned3 = 0;
            for (int n = 0; n < nn; n++)
            {
                if (degree[n] < 3 || nodeJunction[n] >= 0 || trims.mitre[n]) continue;
                unsigned3++;
                if (majorA[n] >= 0) continue;
                int big = 0, bigCls = 0; int e0 = -1, e1 = -1;
                foreach (int ei in map.nodeEdges[n])
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || !routable[ei] || e.link || e.cls < 2) continue;
                    big++; bigCls = Mathf.Max(bigCls, e.cls);
                    if (e0 < 0) e0 = ei; else if (e1 < 0) e1 = ei;
                }
                if (big < 3) continue;
                noCtl++;
                var e00 = map.edges[e0];
                log.Add("control", "no-control-major-crossing", n, e0, e1, P(map.nodes[n], e00.YAt(e00.a == n ? 0f : e00.length)), -1, 0f, big, "static",
                        $"node {n}: {big} arms of class 2+ ('{e00.name}' / '{(e1 >= 0 ? map.edges[e1].name : "")}') meet with no signal or STOP and no through road: everyone yields, the car on the right first");
            }
            report.AppendLine($"  unsigned junctions (3+ arms): {unsigned3}; of them big roads crossing with no control: {noCtl}");

            // ---- 3. ROUTES: one-way traps (no way on, not even back) and one-ways nobody can enter
            int traps = 0, sources = 0, deadArterial = 0, cut = 0;
            for (int i = 0; i < ne; i++)
            {
                if (!routable[i]) continue;
                var e = map.edges[i];
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    if (dir < 0 && e.oneway) continue;
                    int n = dir > 0 ? e.b : e.a;
                    int exits = 0, ways = 0;
                    foreach (int oi in map.nodeEdges[n])
                    {
                        var o = map.edges[oi];
                        if (o.a == o.b || !routable[oi]) continue;
                        if (oi == i) continue;
                        ways++;
                        int d2 = o.a == n ? 1 : -1;
                        if (d2 < 0 && o.oneway) continue;
                        exits++;
                    }
                    if (exits > 0) continue;
                    if (AtMapEdge(n, e.cls)) { cut++; continue; }
                    if (e.oneway)
                    {
                        traps++;
                        log.Add("route", "one-way-trap", n, i, -1, P(map.nodes[n], e.YAt(dir > 0 ? e.length : 0f)), -1, 0f, ways, "static",
                                $"one-way e{i} '{e.name}' cls{e.cls} ends at node {n} with no legal way on ({ways} other arms, all one-way inbound or none)");
                    }
                    else if (ways > 0 || e.cls >= 2)
                    {
                        deadArterial++;
                        log.Add("route", ways > 0 ? "dead-end-junction" : "major-road-dead-end", n, i, -1, P(map.nodes[n], e.YAt(dir > 0 ? e.length : 0f)), -1, 0f, ways, "static",
                                ways > 0 ? $"e{i} '{e.name}' meets {ways} arms at node {n} and may enter none of them (all one-way inbound): only a U-turn"
                                         : $"e{i} '{e.name}' cls{e.cls}{(e.link ? " ramp" : "")} simply ends at node {n} (a class-2+ road with no way on)");
                    }
                }
                if (e.oneway)
                {
                    int n = e.a, entries = 0;
                    foreach (int oi in map.nodeEdges[n])
                    {
                        var o = map.edges[oi];
                        if (oi == i || o.a == o.b || !routable[oi]) continue;
                        int arrive = o.b == n ? 1 : -1;     // o driven toward n
                        if (arrive < 0 && o.oneway) continue;
                        entries++;
                    }
                    if (entries == 0 && AtMapEdge(n, e.cls)) cut++;
                    else if (entries == 0)
                    {
                        sources++;
                        log.Add("route", "one-way-no-entry", n, i, -1, P(map.nodes[n], e.YAt(0f)), -1, 0f, 1f, "static",
                                $"one-way e{i} '{e.name}' cls{e.cls} starts at node {n} where no road may drive in");
                    }
                }
            }

            // ---- 4. ISLANDS: the directed network's strongly connected parts
            Islands(log, report);
            report.AppendLine($"  one-way traps {traps}, one-ways nobody can enter {sources}, dead ends of class 2+ roads or with only inbound arms {deadArterial}; roads cut at the data's edge (not counted) {cut}");

            // ---- 5. SPEED LIMITS
            var missKm = new float[6]; var missN = new int[6]; var allKm = new float[6];
            int odd = 0, jumps = 0;
            for (int i = 0; i < ne; i++)
            {
                if (!routable[i]) continue;
                var e = map.edges[i];
                int cls = Mathf.Clamp(e.cls, 0, 5);
                allKm[cls] += e.length / 1000f;
                if (e.speedKmh <= 0) { missKm[cls] += e.length / 1000f; missN[cls]++; continue; }
                string why = null;
                if (e.speedKmh < 16) why = "under 10 mph";
                else if (!e.link && e.cls == 0 && e.speedKmh > 73) why = "over 45 mph on a local street";
                else if (!e.link && e.cls == 1 && e.speedKmh > 90) why = "over 55 mph on a minor road";
                else if (!e.link && e.cls >= 5 && e.speedKmh < 72) why = "under 45 mph on a freeway";
                else if (e.link && e.speedKmh > 105) why = "over 65 mph on a ramp";
                if (why == null) continue;
                odd++;
                var m = e.PointAt(e.length * 0.5f);
                log.Add("speed", "implausible-limit", e.a, i, -1, P(m, e.YAt(e.length * 0.5f)), -1, 0f, e.speedKmh, "static",
                        $"e{i} '{e.name}' cls{e.cls}{(e.link ? " ramp" : "")} maxspeed {e.speedKmh} km/h ({e.speedKmh / 1.609f:0} mph): {why}");
            }
            // a limit that jumps along one named road
            for (int n = 0; n < nn; n++)
            {
                var arms = map.nodeEdges[n];
                for (int a = 0; a < arms.Count; a++)
                    for (int b = a + 1; b < arms.Count; b++)
                    {
                        var A = map.edges[arms[a]]; var B = map.edges[arms[b]];
                        if (!routable[A.index] || !routable[B.index] || A.speedKmh <= 0 || B.speedKmh <= 0) continue;
                        if (string.IsNullOrEmpty(A.name) || A.name != B.name || A.link || B.link) continue;
                        if (Mathf.Abs(A.speedKmh - B.speedKmh) < 32) continue;
                        if (Vector2.Angle(-ArmOut(A, n), ArmOut(B, n)) > 30f) continue;
                        jumps++;
                        log.Add("speed", "limit-jump", n, A.index, B.index, P(map.nodes[n], A.YAt(A.a == n ? 0f : A.length)), -1, 0f, Mathf.Abs(A.speedKmh - B.speedKmh), "static",
                                $"'{A.name}' changes from {A.speedKmh} to {B.speedKmh} km/h at node {n} with nothing between");
                    }
            }
            var sb = new StringBuilder("  speed limits missing (the traffic's class default used): ");
            string[] cn = { "local", "minor", "collector", "arterial", "trunk", "freeway" };
            for (int k = 0; k < 6; k++) sb.Append($"{cn[k]} {missKm[k]:0}/{allKm[k]:0} km{(k < 5 ? ", " : "")}");
            report.AppendLine(sb.ToString());
            report.AppendLine($"  implausible limits {odd}, limit jumps of 20+ mph along one road {jumps}");
            for (int k = 2; k < 6; k++)
                if (missN[k] > 0) log.Add("speed", "missing-limit-" + cn[k], -1, -1, -1, Vector3.zero, -1, 0f, missKm[k], "static", $"{missN[k]} {cn[k]} edges ({missKm[k]:0.0} km) carry no maxspeed");

            // ---- 6. LANES: straight-throughs whose lanes do not line up
            int noLane = 0, misaligned = 0, straights = 0;
            for (int n = 0; n < nn; n++)
            {
                var arms = map.nodeEdges[n];
                if (arms.Count < 2) continue;
                foreach (int ei in arms)
                {
                    var e = map.edges[ei];
                    if (e.a == e.b || !routable[ei]) continue;
                    int dir = e.b == n ? 1 : -1;
                    if (dir < 0 && e.oneway) continue;
                    foreach (int oi in arms)
                    {
                        if (oi == ei) continue;
                        var o = map.edges[oi];
                        if (o.a == o.b || !routable[oi]) continue;
                        int d2 = o.a == n ? 1 : -1;
                        if (d2 < 0 && o.oneway) continue;
                        float ang = Vector2.Angle(TravelTan(e, dir, dir > 0 ? e.length : 0f), TravelTan(o, d2, d2 > 0 ? 0f : o.length));
                        if (ang >= 35f) continue;
                        if (e.link != o.link && (e.cls >= 4 || o.cls >= 4)) continue;   // a merge or diverge: its own lanes
                        straights++;
                        float sA = EndS(e, dir), sB = d2 > 0 ? Back(o, n) : o.length - Back(o, n);
                        int nIn = Lanes(ei, dir), nOut = Lanes(oi, d2);
                        Vector2 t0 = TravelTan(e, dir, sA), nrm = new Vector2(-t0.y, t0.x);
                        float worst = 0f, best = float.MaxValue; int ends = 0;
                        for (int i = 0; i < nIn; i++)
                        {
                            Vector2 p = LanePos(e, dir, i, sA);
                            float bd = float.MaxValue;
                            for (int jj = 0; jj < nOut; jj++) bd = Mathf.Min(bd, Mathf.Abs(Vector2.Dot(LanePos(o, d2, jj, sB) - p, nrm)));
                            best = Mathf.Min(best, bd);
                            if (bd > 2f) { ends++; worst = Mathf.Max(worst, bd); }
                        }
                        var at = P(map.nodes[n], e.YAt(dir > 0 ? e.length : 0f));
                        if (ends == nIn)
                        {
                            noLane++;
                            log.Add("lanes", "no-continuing-lane", n, ei, oi, at, -1, 0f, best, "static",
                                    $"straight on from e{ei} '{e.name}' ({nIn} lanes) into e{oi} '{o.name}' ({nOut} lanes): no lane within 2 m of any lane across the node (nearest {best:0.0} m) - the lanes jump sideways");
                        }
                        else if (ends > 0 && nOut >= nIn)
                        {
                            misaligned++;
                            log.Add("lanes", "lanes-misaligned", n, ei, oi, at, -1, 0f, worst, "static",
                                    $"straight on from e{ei} '{e.name}' ({nIn} lanes) into e{oi} '{o.name}' ({nOut} lanes): {ends} lane(s) find nothing within 2 m (worst {worst:0.0} m) though the next road has as many lanes");
                        }
                    }
                }
            }
            report.AppendLine($"  straight-throughs {straights}: no lane continues {noLane}, lanes misaligned (as many lanes, not lined up) {misaligned}");

            // ---- 7. RAMPS: the room a merge is driven over
            int shortZone = 0, merges = 0;
            for (int i = 0; i < ne; i++)
            {
                var e = map.edges[i];
                if (!routable[i] || !e.link) continue;
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    if (dir < 0 && e.oneway) continue;
                    int n = dir > 0 ? e.b : e.a;
                    bool joins = false; int into = -1;
                    foreach (int oi in map.nodeEdges[n])
                    {
                        var o = map.edges[oi];
                        if (oi == i || o.link || o.cls < 4 || !routable[oi]) continue;
                        int d2 = o.a == n ? 1 : -1;
                        if (d2 < 0 && o.oneway) continue;
                        if (Vector2.Angle(TravelTan(e, dir, dir > 0 ? e.length : 0f), TravelTan(o, d2, d2 > 0 ? 0f : o.length)) < 35f) { joins = true; into = oi; }
                    }
                    if (!joins) continue;
                    merges++;
                    float z = ZoneBack(e, dir);
                    if (z >= 30f) continue;
                    shortZone++;
                    log.Add("merge", "short-merge-zone", n, i, into, P(map.nodes[n], e.YAt(dir > 0 ? e.length : 0f)), -1, 0f, z, "static",
                            $"ramp e{i} '{e.name}' ({e.length:0} m) runs within 12 deg of the freeway for only {z:0} m before it joins e{into} '{map.edges[into].name}'");
                }
            }
            report.AppendLine($"  ramp merges {merges}: merge zone under 30 m {shortZone}");
        }

        /// <summary>Does part <paramref name="part"/> lead (by next: the arcs
        /// out, or the reverse arcs in) to the giant part or to the data's
        /// frontier (CityMap.nodeFrontier)? A search over the vertices
        /// outside the giant, bounded.</summary>
        bool Leads(int part, int giant, int[] comp, int V, System.Func<int, int[]> next)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            for (int v = 0; v < V; v++) if (comp[v] == part) { seen.Add(v); stack.Push(v); }
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                if (comp[v] == giant) return true;
                var e = map.edges[v >> 1];
                if (map.Frontier(e.a) || map.Frontier(e.b)) return true;
                foreach (int w in next(v))
                    if (seen.Add(w)) stack.Push(w);
                if (seen.Count > 50000) return true;
            }
            return false;
        }

        /// <summary>The directed network (an edge driven one way is a vertex;
        /// a legal move across a node an arc, a U-turn included where the road
        /// is two-way): its strongly connected parts. Everything outside the
        /// biggest is a place a car cannot leave or cannot reach.</summary>
        void Islands(CityRoadLog log, StringBuilder report)
        {
            int ne = map.edges.Length, V = ne * 2;
            // vertex v = edge * 2 + (driven a->b ? 1 : 0)
            bool Valid(int v) { int ei = v >> 1; return routable[ei] && ((v & 1) == 1 || !map.edges[ei].oneway); }
            var index = new int[V]; var low = new int[V]; var comp = new int[V];
            var onStack = new bool[V];
            for (int i = 0; i < V; i++) { index[i] = -1; comp[i] = -1; }
            var stack = new Stack<int>();
            var call = new Stack<(int v, int it)>();
            var succ = new List<int>(8);
            int counter = 0, comps = 0;
            var compLen = new List<float>();
            var compRep = new List<int>();
            void Succ(int v, List<int> into)
            {
                into.Clear();
                int ei = v >> 1; var e = map.edges[ei];
                int n = (v & 1) == 1 ? e.b : e.a;
                foreach (int oi in map.nodeEdges[n])
                {
                    var o = map.edges[oi];
                    if (o.a == o.b || !routable[oi]) continue;
                    int d2 = o.a == n ? 1 : -1;
                    if (d2 < 0 && o.oneway) continue;
                    into.Add((oi << 1) | (d2 > 0 ? 1 : 0));
                }
            }
            var succCache = new Dictionary<int, int[]>();
            int[] SuccOf(int v)
            {
                if (succCache.TryGetValue(v, out var s)) return s;
                Succ(v, succ);
                s = succ.ToArray();
                succCache[v] = s;
                return s;
            }
            for (int root = 0; root < V; root++)
            {
                if (!Valid(root) || index[root] >= 0) continue;
                call.Push((root, 0));
                index[root] = low[root] = counter++;
                stack.Push(root); onStack[root] = true;
                while (call.Count > 0)
                {
                    var (v, it) = call.Pop();
                    var ss = SuccOf(v);
                    bool descended = false;
                    while (it < ss.Length)
                    {
                        int w2 = ss[it++];
                        if (index[w2] < 0)
                        {
                            call.Push((v, it));
                            index[w2] = low[w2] = counter++;
                            stack.Push(w2); onStack[w2] = true;
                            call.Push((w2, 0));
                            descended = true;
                            break;
                        }
                        if (onStack[w2]) low[v] = Mathf.Min(low[v], index[w2]);
                    }
                    if (descended) continue;
                    if (low[v] == index[v])
                    {
                        float len = 0f; int rep = v;
                        while (true)
                        {
                            int x = stack.Pop(); onStack[x] = false; comp[x] = comps;
                            len += map.edges[x >> 1].length;
                            if (map.edges[x >> 1].cls > map.edges[rep >> 1].cls) rep = x;
                            if (x == v) break;
                        }
                        compLen.Add(len); compRep.Add(rep);
                        comps++;
                    }
                    if (call.Count > 0)
                    {
                        var (pv, pit) = call.Peek();
                        low[pv] = Mathf.Min(low[pv], low[v]);
                    }
                }
            }
            int giant = 0;
            for (int i = 1; i < comps; i++) if (compLen[i] > compLen[giant]) giant = i;
            // the reverse arcs (Leads into a part)
            Dictionary<int, List<int>> pred = null;
            int[] PredOf(int v)
            {
                if (pred == null)
                {
                    pred = new Dictionary<int, List<int>>();
                    for (int u = 0; u < V; u++)
                    {
                        if (!Valid(u)) continue;
                        foreach (int w2 in SuccOf(u))
                        {
                            if (!pred.TryGetValue(w2, out var l)) pred[w2] = l = new List<int>(2);
                            l.Add(u);
                        }
                    }
                }
                return pred.TryGetValue(v, out var lp) ? lp.ToArray() : System.Array.Empty<int>();
            }
            float total = 0f;
            for (int i = 0; i < comps; i++) total += compLen[i];
            // the parts outside the giant, biggest first
            var order = new List<int>();
            for (int i = 0; i < comps; i++) if (i != giant) order.Add(i);
            order.Sort((a, b) => compLen[b].CompareTo(compLen[a]));
            float outside = 0f;
            foreach (int i in order) outside += compLen[i];
            int logged = 0, cutParts = 0;
            float cutKm = 0f;
            foreach (int i in order)
            {
                if (compLen[i] < 150f || logged >= 60) break;
                int rep = compRep[i]; var e = map.edges[rep >> 1];
                var m = e.PointAt(e.length * 0.5f);
                // can it be left (an arc out of it into the giant) or reached?
                // and does it touch the data's edge (a road cut there)?
                bool canLeave = false, edgeCut = false, canEnter = false;
                for (int v = 0; v < V; v++)
                {
                    if (comp[v] != i) continue;
                    var ev = map.edges[v >> 1];
                    if (!edgeCut && (AtMapEdge(ev.a, ev.cls) || AtMapEdge(ev.b, ev.cls))) edgeCut = true;
                    if (!canLeave) foreach (int w2 in SuccOf(v)) if (comp[w2] == giant) { canLeave = true; break; }
                }
                if (map.nodeFrontier != null)
                {
                    // with the data's frontier known (section FRNT) a part is
                    // judged by where its roads LEAD: out of it, on to the rest
                    // of the city or off the map at a frontier (I-77 south of
                    // Carowinds runs on into South Carolina, which the data
                    // lacks), and into it, from either. Run 1's 47 inescapable
                    // and 13 unreachable parts were nearly all one hop from the
                    // data's edge (2026-10-29).
                    canLeave = Leads(i, giant, comp, V, SuccOf);
                    canEnter = Leads(i, giant, comp, V, PredOf);
                    if (canLeave && canEnter) { cutParts++; cutKm += compLen[i] / 1000f; continue; }
                }
                else if (edgeCut) { cutParts++; cutKm += compLen[i] / 1000f; continue; }
                logged++;
                log.Add("route", canLeave ? "unreachable-part" : "inescapable-part", e.a, rep >> 1, -1, new Vector3(m.x, e.YAt(e.length * 0.5f), m.y), -1, 0f, compLen[i] / 1000f, "static",
                        $"{compLen[i] / 1000f:0.00} km of directed road round e{rep >> 1} '{e.name}' cls{e.cls} {(canLeave ? "that no car from the rest of the city can drive into" : "that a car, once in, can never drive out of")}");
            }
            report.AppendLine($"  directed network: {comps} strongly connected parts; the biggest holds {compLen[giant] / 1000f:0} of {total / 1000f:0} km (directed); outside it {outside / 1000f:0.0} km in {comps - 1} parts ({logged} of 150 m+ logged; {cutParts} more, {cutKm:0.0} km, touch the data's edge and are not counted)");
        }
    }
}
