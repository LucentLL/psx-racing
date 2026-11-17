using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// ONE AI'S PLAN in a checkpoint race (DESIGN_v2 4.3, stage 1): the cost optimum (the authoring router's model)
    /// from where the car is to its next gate, then on through the following gates, so the road always reaches the
    /// gate and at least 900 m beyond the car; past the flag it keeps going for the whole shutdown (the next lap's
    /// chain on a circuit, the smallest legal turn at each node on a sprint), at least max(900 m, v^2/4.4 + 150 m).
    /// Built into the idle one of two TrackPaths (double buffer) and swapped onto the AIDriver.
    /// Rebuilt when the car passes a gate, after a respawn, on MISSED, and when less than 450 m of road is left
    /// (off once the run-out is in). Rebuilds start from the car's own path index (never behind it); the race
    /// allows one rebuild per frame (CheckpointRace's queue). Stage 1: optimum routes only (no persona variety).
    /// The paths live on an INACTIVE holder created after the race started, so no FindObjectOfType&lt;TrackPath&gt;
    /// (tools, dev probes, the replay's fallback) ever picks one up.
    /// STAGE 2 (cpfix 11-17):
    /// - VARIETY (PSX_CP_AI_VARIETY): each rival has a persona (from its name): a share of legs (0-35 %) it takes
    ///   off the optimum, and a band (2-6 % of the cost still to go) inside which any successor at a node is fair
    ///   game. Such a leg walks the field down choosing in that band (seeded per car, gate and lap: the same race
    ///   twice is the same race), never uphill, and is kept only if it costs at most 8 % over the optimum; else the
    ///   optimum. The player's autopilot always takes the optimum (it stands in for a player on the GPS line).
    /// - RE-ROUTE (PSX_CP_REROUTE): a rival more than 20 m off its own road for 1.5 s plans again from where it is
    ///   (at most every 3 s); a rival whose remaining distance has not fallen 50 m in 10 s plans again too (the
    ///   20 s watchdog in CheckpointRace still puts it back on its road after that).
    /// </summary>
    public class AIRoutePlanner : MonoBehaviour
    {
        CheckpointRace race;
        CheckpointRace.CarCP cp;
        AIDriver ai;
        TrackPath a, b;
        public TrackPath Path { get; private set; }
        internal bool queued;
        bool fullRunOut;
        public int Rebuilds { get; private set; }
        public float LastBuildMs { get; private set; }
        public float WorstBuildMs { get; private set; }
        /// <summary>The leg signature of the last plan, for the play check: the edges it drives.</summary>
        public string LastSignature { get; private set; } = "";
        /// <summary>For the play check: legs planned off the optimum (variety), and re-plans for being off its road.</summary>
        public int VariedLegs { get; private set; }
        public int Reroutes { get; private set; }
        /// <summary>Every leg this car planned: "gate:edge,edge,..." of the leg's first 40 edges (the variety gate
        /// counts distinct routes per leg across the field).</summary>
        public readonly List<string> LegLog = new List<string>(64);

        // ---- persona (stage 2)
        public static bool VarietyOn => CheckpointFile.On("PSX_CP_AI_VARIETY");
        public static bool RerouteOn => CheckpointFile.On("PSX_CP_REROUTE");
        float altChance = -1f, eps;
        uint seed;
        void Persona()
        {
            if (altChance >= 0f) return;
            uint h = 2166136261u;
            foreach (char ch in cp.car.name) { h ^= ch; h *= 16777619u; }
            seed = h == 0u ? 1u : h;
            float a = Hash01(seed, 11), b = Hash01(seed, 23);
            altChance = cp.isPlayer ? 0f : 0.35f * a;
            eps = 0.02f + 0.04f * b;
        }
        static float Hash01(uint h, uint salt)
        {
            h ^= salt * 0x9E3779B9u; h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }

        readonly List<RoutedPath.Piece> pieces = new List<RoutedPath.Piece>(512);
        // THE PLAN'S OWN METRES (cpfix 11-17): the pieces of the road the car is on now, their running metres, and where
        // each leg ends (its gate): a rival's remaining distance is measured along its OWN road, so a varied leg is
        // ranked by what it really drives (on the field's descent a varied rival jumped 259 m at a node on C1)
        readonly List<RoutedPath.Piece> planPieces = new List<RoutedPath.Piece>(512), nextPieces = new List<RoutedPath.Piece>(512);
        readonly List<float> planCum = new List<float>(512), nextCum = new List<float>(512);
        readonly List<int> planLegGate = new List<int>(16), nextLegGate = new List<int>(16);
        readonly List<float> planLegEnd = new List<float>(16), nextLegEnd = new List<float>(16);
        int planHint;

        /// <summary>Metres from (state, t) to gate <paramref name="k"/> along this car's own planned road; +Infinity
        /// when the car is not on its plan (a crash) or the plan does not reach that gate.</summary>
        public float RemainingOnPlan(int st, float t, int k)
        {
            int n = planPieces.Count;
            if (n == 0) return float.PositiveInfinity;
            int at = -1;
            int h = Mathf.Clamp(planHint, 0, n - 1);
            for (int d = 0; d < n && d < 40; d++)
            {
                int i = h + d;
                if (i < n) { var q = planPieces[i]; if (q.st == st && t >= q.t0 - 0.5f && t <= q.t1 + 0.5f) { at = i; break; } }
                int j = h - d - 1;
                if (d < 4 && j >= 0) { var q = planPieces[j]; if (q.st == st && t >= q.t0 - 0.5f && t <= q.t1 + 0.5f) { at = j; break; } }
            }
            if (at < 0) return float.PositiveInfinity;
            planHint = at;
            float cum = planCum[at] + Mathf.Clamp(t - planPieces[at].t0, 0f, planPieces[at].t1 - planPieces[at].t0);
            for (int g = 0; g < planLegGate.Count; g++)
                if (planLegGate[g] == k && planLegEnd[g] >= cum - 1f) return Mathf.Max(0f, planLegEnd[g] - cum);
            return float.PositiveInfinity;
        }
        readonly List<int> states = new List<int>(512);

        public static AIRoutePlanner Create(CheckpointRace race, CheckpointRace.CarCP cp, AIDriver ai, Transform parent)
        {
            var holder = new GameObject("CPRoute_" + cp.car.name);
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var p = cp.car.gameObject.AddComponent<AIRoutePlanner>();
            p.race = race; p.cp = cp; p.ai = ai;
            p.a = holder.AddComponent<TrackPath>();
            p.b = holder.AddComponent<TrackPath>();
            return p;
        }

        public void OnGatePassed(bool finished) { logLeg = true; race.Request(this); }
        /// <summary>The next plan is a leg's own (from its gate, or the start): the variety gate logs it.</summary>
        bool logLeg = true;
        public void OnRespawned() { race.Request(this); }
        public void OnMissed() { race.Request(this); }

        float offSince = -1f, lastReroute = -99f, staleAt = -1f;
        bool replanNote;
        void Update()
        {
            if (race == null || RaceReplay.Playing) return;
            // RE-ROUTE (stage 2): off its own road for 1.5 s, or no progress for 10 s: plan again from here
            if (RerouteOn && !cp.isPlayer && Path != null && !queued && ai != null && ai.path == Path && Time.time - lastReroute > 3f)
            {
                var pr = RaceManager.Instance != null ? RaceManager.Instance.GetProgress(cp.car) : null;
                if (pr != null && !pr.finished && !pr.retired && RaceManager.Instance.State == RaceManager.RaceState.Racing)
                {
                    bool off = cp.loc.valid && cp.loc.dist > 20f;
                    if (off) { if (offSince < 0f) offSince = Time.time; } else offSince = -1f;
                    bool stale = Time.time - cp.lastAdvanceTime > 10f && staleAt != cp.lastAdvanceTime;
                    if ((off && Time.time - offSince > 1.5f) || stale)
                    {
                        if (stale) staleAt = cp.lastAdvanceTime;
                        lastReroute = Time.time; offSince = -1f; Reroutes++;
                        Debug.Log($"[Checkpoints] {cp.car.name}: re-route ({(stale ? "no progress for 10 s" : "20 m off its road")})");
                        replanNote = true;
                        race.Request(this);
                        return;
                    }
                }
            }
            if (Path == null || queued || fullRunOut || ai == null || ai.path != Path) return;
            // the guard: less than 450 m of road left ahead of the car
            int idx = Path.NearestIndex(cp.car.transform.position, cp.pathHint >= 0 ? Mathf.Min(cp.pathHint, Path.Count - 1) : -1, 25);
            if ((Path.Count - 1 - idx) * Path.spacing < 450f) race.Request(this);
        }

        /// <summary>Plan from the car's spot and swap the new road in.</summary>
        public void Rebuild()
        {
            if (race == null || cp == null || ai == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var R = race.Router;
            var map = R.map;
            var car = cp.car;
            Vector3 pos = car.transform.position;
            // where to start: the car's own routed road (its edge and arc), else the graph under it
            int st; float t;
            if (!StartState(R, pos, out st, out t)) { Debug.LogWarning("[Checkpoints] " + car.name + ": no road to plan from"); return; }
            // start 20 m behind the car on the same state, so the car is not at index 0
            t = Mathf.Max(0f, t - 20f);
            pieces.Clear();
            nextLegGate.Clear(); nextLegEnd.Clear();
            var p = RaceManager.Instance != null ? RaceManager.Instance.GetProgress(car) : null;
            bool finished = p != null && p.finished;
            int k = cp.next;
            float carT = t + 20f;
            float speed = Mathf.Abs(car.forwardSpeed);
            float need = Mathf.Max(900f, speed * speed / 4.4f + 150f);
            float have = 0f;
            int lapsPlanned = 0;
            bool runOut = false;
            int sigEdges = 0;
            var sig = new System.Text.StringBuilder(64);
            if (finished && !race.Loop)
            {
                // a sprint's finisher: the gentlest road on for the whole shutdown
                ExtendStraight(R, st, t, need + 300f);
                runOut = true;
            }
            Persona();
            for (int leg = 0; leg < 64 && !(finished && !race.Loop); leg++)
            {
                bool finishGate = !finished && race.IsFinishGate(cp, k) && lapsPlanned == 0;
                // VARIETY: this leg off the optimum, by the persona's share (decided per gate and lap, so a re-plan
                // mid-leg keeps the same choice)
                bool varied = false;
                if (leg == 0 && VarietyOn && altChance > 0f && !finished)
                {
                    int lapNow = p != null ? p.lap : 1;
                    uint legSeed = seed ^ ((uint)k * 2654435761u) ^ ((uint)lapNow * 40503u);
                    if (Hash01(legSeed, 7) < altChance && DescendVaried(R, race.Cost(k), st, t, legSeed, states, out float vT))
                    { varied = true; variedLastT = vT; }
                }
                float lastT;
                if (varied) lastT = variedLastT;
                else if (!R.Descend(race.Cost(k), st, t, states, out lastT))
                {
                    Debug.LogWarning($"[Checkpoints] {car.name}: gate {k} unreachable from state {st}");
                    break;
                }
                if (leg == 0)
                {
                    if (varied) VariedLegs++;
                    // the leg's route as planned at its gate (a mid-leg re-plan starts elsewhere: not a route of its own);
                    // from its second road on (the first is where the car stood)
                    if (logLeg)
                    {
                        logLeg = false;
                        var ls = new System.Text.StringBuilder(64);
                        int lapNow = p != null ? p.lap : 1;
                        ls.Append(lapNow).Append('.').Append(k).Append(':');
                        for (int i = 1; i < states.Count && i < 40; i++) ls.Append(states[i] >> 1).Append(',');
                        if (LegLog.Count < 256) LegLog.Add(ls.ToString());
                    }
                }
                for (int i = 0; i < states.Count; i++)
                {
                    int s = states[i];
                    float t0 = i == 0 ? t : 0f, t1 = i == states.Count - 1 ? lastT : R.len[s];
                    AddPiece(s, t0, t1);
                    have += t1 - t0;
                    if (leg == 0 && sigEdges++ < 40) sig.Append(s >> 1).Append(',');
                }
                st = states[states.Count - 1]; t = lastT;
                nextLegGate.Add(k); nextLegEnd.Add(have);
                if (finishGate || (finished && leg == 0)) runOut = true;
                if (runOut)
                {
                    // past the flag: the next lap's chain on a circuit, else the gentlest road on
                    if (race.Loop) { k = race.NextAfter(k); if (have - carT >= need + 300f) break; continue; }
                    ExtendStraight(R, st, t, need + 300f - Mathf.Max(0f, have - carT));
                    break;
                }
                int nk = race.NextAfter(k);
                if (nk < 0) { ExtendStraight(R, st, t, need); runOut = true; break; }
                if (race.Loop && k == 0) lapsPlanned++;
                k = nk;
                if (have - carT >= 900f) break;
            }
            fullRunOut = runOut;
            if (pieces.Count == 0) return;
            var into = Path == a ? b : a;
            float rw = RaceManager.Instance != null && RaceManager.Instance.path != null ? RaceManager.Instance.path.roadWidth : 12f;
            if (!RoutedPath.Build(map, pieces, into, rw)) return;
            // the plan's own metres go live with its road
            planPieces.Clear(); planCum.Clear();
            float run = 0f;
            for (int i = 0; i < pieces.Count; i++) { planPieces.Add(pieces[i]); planCum.Add(run); run += pieces[i].t1 - pieces[i].t0; }
            planLegGate.Clear(); planLegGate.AddRange(nextLegGate);
            planLegEnd.Clear(); planLegEnd.AddRange(nextLegEnd);
            planHint = 0;
            Path = into;
            if (replanNote) { replanNote = false; CheckpointRace.NoteReplan(car); }
            ai.SetPath(into);
            cp.pathHint = -1;
            Rebuilds++;
            LastSignature = sig.ToString();
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            WorstBuildMs = Mathf.Max(WorstBuildMs, LastBuildMs);
        }

        float variedLastT;
        readonly List<int> vStates = new List<int>(512);

        /// <summary>
        /// A VARIED DESCENT: down the field from (st, t), at each node any successor whose cost to the gate is
        /// within the persona's band (eps x the cost still to go) of the best, chosen by the leg's seed; never one
        /// whose cost-to-go is not below the state's own (no loop). Kept only at <= 1.08 x the optimum's cost and
        /// only when it differs from the optimum; false otherwise (the caller takes the optimum).
        /// </summary>
        bool DescendVaried(CityRouter R, CityRouter.Field F, int st, float t, uint legSeed, List<int> outStates, out float lastT)
        {
            lastT = 0f;
            if (F == null || st < 0 || !R.legal[st]) return false;
            float opt = R.CostFrom(F, st, t);
            if (float.IsInfinity(opt) || opt < 150f) return false;
            vStates.Clear();
            vStates.Add(st);
            float g = F.seedG[st];
            if (g >= 0f && g >= t - 0.01f) return false;        // the gate is on this very road: nothing to vary
            float cost = Mathf.Max(0f, R.len[st] - t) * F.Mul(st);
            int x = st; bool differs = false;
            for (int guard = 0; guard < 2000; guard++)
            {
                float best = float.PositiveInfinity;
                int k0 = R.succStart[x], k1 = R.succStart[x + 1];
                for (int k = k0; k < k1; k++)
                {
                    float c = (F.cost ? CityRouter.TurnCost(R.succTurn[k]) : 0f) + F.D[R.succ[k]];
                    if (c < best) best = c;
                }
                if (float.IsInfinity(best)) return false;
                float band = eps * best;
                // the candidates in the band, cheaper-to-go than here; one picked by the seed
                int n = 0, pick = -1; float pickTurn = 0f;
                float roll = Hash01(legSeed ^ (uint)x, 31);
                for (int k = k0; k < k1; k++)
                {
                    int y = R.succ[k];
                    float c = (F.cost ? CityRouter.TurnCost(R.succTurn[k]) : 0f) + F.D[y];
                    if (c > best + band || F.D[y] >= F.D[x] - 1f && F.seedG[y] < 0f) continue;
                    n++;
                }
                if (n == 0) return false;
                int want = Mathf.Min(n - 1, (int)(roll * n)), i = 0;
                for (int k = k0; k < k1; k++)
                {
                    int y = R.succ[k];
                    float c = (F.cost ? CityRouter.TurnCost(R.succTurn[k]) : 0f) + F.D[y];
                    if (c > best + band || F.D[y] >= F.D[x] - 1f && F.seedG[y] < 0f) continue;
                    if (i++ == want) { pick = y; pickTurn = F.cost ? CityRouter.TurnCost(R.succTurn[k]) : 0f; break; }
                }
                if (pick < 0) return false;
                if (pick != F.nxt[x]) differs = true;
                vStates.Add(pick);
                cost += pickTurn;
                float pg = F.seedG[pick];
                if (pg >= 0f) { cost += pg * F.Mul(pick); lastT = pg; break; }
                cost += R.len[pick] * F.Mul(pick);
                if (cost > opt * 1.08f + 1f) return false;
                x = pick;
                if (guard == 1999) return false;
            }
            if (!differs || cost > opt * 1.08f + 1f) return false;
            outStates.Clear();
            outStates.AddRange(vStates);
            return true;
        }

        void AddPiece(int s, float t0, float t1)
        {
            int n = pieces.Count;
            if (n > 0 && pieces[n - 1].st == s && Mathf.Abs(pieces[n - 1].t1 - t0) < 0.05f)
            { var q = pieces[n - 1]; q.t1 = t1; pieces[n - 1] = q; return; }
            if (t1 - t0 < 0.02f && n > 0) return;
            pieces.Add(new RoutedPath.Piece { st = s, t0 = t0, t1 = t1 });
        }

        /// <summary>On from (st, t): the rest of the state, then at each node the legal successor with the smallest
        /// turn (never a dead end or a U-turn), until <paramref name="metres"/>.</summary>
        void ExtendStraight(CityRouter R, int st, float t, float metres)
        {
            float got = 0f;
            AddPiece(st, t, R.len[st]); got += R.len[st] - t;
            int x = st;
            for (int guard = 0; guard < 200 && got < metres; guard++)
            {
                int by = -1; float bt = float.PositiveInfinity;
                for (int k = R.succStart[x]; k < R.succStart[x + 1]; k++)
                {
                    int y = R.succ[k];
                    if (R.succStart[y + 1] - R.succStart[y] == 0) continue;   // a dead end
                    if (R.succTurn[k] < bt) { bt = R.succTurn[k]; by = y; }
                }
                if (by < 0) break;
                AddPiece(by, 0f, R.len[by]); got += R.len[by];
                x = by;
            }
        }

        bool StartState(CityRouter R, Vector3 pos, out int st, out float t)
        {
            st = -1; t = 0f;
            var path = ai.path;
            if (path != null && path.Routed && path.Count > 1)
            {
                int i = path.NearestIndex(pos, cp.pathHint >= 0 ? Mathf.Min(cp.pathHint, path.Count - 1) : -1, 25);
                Vector3 wp = path.GetPoint(i);
                if (new Vector2(pos.x - wp.x, pos.z - wp.z).sqrMagnitude < 15f * 15f)
                {
                    int e = path.edgeAt[i];
                    var ed = R.map.edges[e];
                    Vector3 tan = path.GetTangent(i);
                    int dir = path.dirAt != null ? path.dirAt[i] : (Vector2.Dot(ed.TangentAt(path.sAt[i]), new Vector2(tan.x, tan.z)) >= 0f ? 1 : -1);
                    float s = Mathf.Clamp(path.sAt[i] + dir * Vector3.Dot(pos - wp, tan), 0f, ed.length);
                    st = e * 2 + (dir == 1 ? 0 : 1);
                    if (!R.legal[st]) { st ^= 1; dir = -dir; }
                    t = dir == 1 ? s : ed.length - s;
                    return true;
                }
            }
            // the graph under the car (height-aware), the way it is pointed / moving
            var loc = cp.loc;
            Vector3 vel = cp.car.Body != null ? cp.car.Body.linearVelocity : Vector3.zero;
            if (!R.Nearest(pos, 40f, 6f, -1, 0f, out int e2, out float s2, out _)) return false;
            var ed2 = R.map.edges[e2];
            int d2 = R.DirOf(e2, s2, vel, cp.car.transform.forward);
            st = e2 * 2 + (d2 == 1 ? 0 : 1);
            if (!R.legal[st]) { st ^= 1; d2 = -d2; }
            t = d2 == 1 ? s2 : ed2.length - s2;
            return true;
        }
    }
}
