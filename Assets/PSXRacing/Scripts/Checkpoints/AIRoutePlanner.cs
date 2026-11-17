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

        readonly List<RoutedPath.Piece> pieces = new List<RoutedPath.Piece>(512);
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

        public void OnGatePassed(bool finished) { race.Request(this); }
        public void OnRespawned() { race.Request(this); }
        public void OnMissed() { race.Request(this); }

        void Update()
        {
            if (race == null || RaceReplay.Playing) return;
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
            for (int leg = 0; leg < 64 && !(finished && !race.Loop); leg++)
            {
                bool finishGate = !finished && race.IsFinishGate(cp, k) && lapsPlanned == 0;
                if (!R.Descend(race.Cost(k), st, t, states, out float lastT))
                {
                    Debug.LogWarning($"[Checkpoints] {car.name}: gate {k} unreachable from state {st}");
                    break;
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
            Path = into;
            ai.SetPath(into);
            cp.pathHint = -1;
            Rebuilds++;
            LastSignature = sig.ToString();
            LastBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            WorstBuildMs = Mathf.Max(WorstBuildMs, LastBuildMs);
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
                    int dir = Vector2.Dot(ed.TangentAt(path.sAt[i]), new Vector2(tan.x, tan.z)) >= 0f ? 1 : -1;
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
