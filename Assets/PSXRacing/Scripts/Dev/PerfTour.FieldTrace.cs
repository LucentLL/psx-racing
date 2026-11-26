using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE FIELD TRACE (cityai round 2; &amp;trace=1 on a stage's field run): what every car did, from the running game
    /// (a built exe or the editor's tour), for the junction-turn plans off | on (tools\city\cityai_field_plan.py).
    /// At 10 Hz of game time: every race car (rival / player) and every race-traffic car within 80 m of a junction turn
    /// of the course line: t, id, kind, x, z, heading (deg, 0 = +z, clockwise), speed (m/s), and for race cars the
    /// controls (steer, throttle, brake), backing up, and the AI's give-way reason. Files: &lt;tag&gt;_&lt;stage&gt;_field.csv,
    /// _turns.csv (the course line's turns of 30 degrees or more), _edges.csv / _kerbs.csv (the map round each turn).
    /// &amp;seed=N seeds the race's draws (traffic, the rivals' mistakes) as the editor's play check does. Measures only.
    /// </summary>
    public partial class PerfTour
    {
        StringBuilder ftb;
        float ftNext, ftT0;
        readonly List<Vector3> ftTurns = new List<Vector3>();
        readonly List<float> ftDeg = new List<float>();
        readonly Dictionary<Rigidbody, int> ftIds = new Dictionary<Rigidbody, int>();
        readonly HashSet<Rigidbody> ftRace = new HashSet<Rigidbody>();
        int ftWrecks; readonly StringBuilder ftWreckNotes = new StringBuilder();
        static readonly CultureInfo FtCI = CultureInfo.InvariantCulture;

        bool FieldTraceOn => ShotLink.Query(Request() ?? "", "trace") == "1";

        void FieldBegin(string id, TrackPath path)
        {
            ftb = null;
            if (!FieldTraceOn || path == null) return;
            ftb = new StringBuilder(1 << 20);
            ftb.Append("t,id,kind,x,z,heading,speed,steer,thr,brk,rev,why\n");
            ftNext = 0f; ftT0 = Time.time; ftIds.Clear(); ftWreckNotes.Length = 0; ftWrecks = TrafficSystem.Instance != null ? TrafficSystem.Instance.WreckLog.Count : 0;
            TurnMapDump.Turns(path, ftTurns, ftDeg);
            Post(id + "_turns.csv", TurnMapDump.TurnsCsv(ftTurns, ftDeg));
            TurnMapDump.Map(ftTurns, out string edges, out string kerbs);
            Post(id + "_edges.csv", edges);
            Post(id + "_kerbs.csv", kerbs);
            Say("FIELD TRACE " + id + ": " + ftTurns.Count + " junction turns of 30 deg or more on the course line");
        }

        void FieldSample()
        {
            if (ftb == null) return;
            float t = Time.time - ftT0;
            if (t < ftNext) return;
            ftNext = t + 0.1f;
            var rm = RaceManager.Instance;
            var raceBodies = ftRace; raceBodies.Clear();
            if (rm != null && rm.allCars != null)
                foreach (var c in rm.allCars)
                {
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (c.Body != null) raceBodies.Add(c.Body);
                    var ai = c.GetComponent<AIDriver>();
                    string kind = c == rm.playerCar ? "player" : "rival";
                    Row(t, c.name, kind, c.transform.position, c.transform.forward, Mathf.Abs(c.forwardSpeed),
                        c.steerInput, c.throttleInput, c.brakeInput, ai != null && ai.DebugBackingUp, ai != null ? ai.DebugGiveWhy : "");
                }
            var ts = TrafficSystem.Instance;
            if (ts != null && ts.WreckLog.Count > ftWrecks) { for (int w = ftWrecks; w < ts.WreckLog.Count; w++) ftWreckNotes.Append(t.ToString("0.0", FtCI)).Append(" s: ").Append(ts.WreckLog[w]).Append('\n'); ftWrecks = ts.WreckLog.Count; }
            if (ts != null)
                for (int i = 0; i < ts.Obstacles.Count; i++)
                {
                    var rb = ts.Obstacles[i];
                    if (rb == null || raceBodies.Contains(rb)) continue;
                    var p = rb.position;
                    bool near = false;
                    foreach (var c in ftTurns) { float dx = p.x - c.x, dz = p.z - c.z; if (dx * dx + dz * dz < 80f * 80f) { near = true; break; } }
                    if (!near) continue;
                    if (!ftIds.TryGetValue(rb, out int n)) { n = ftIds.Count + 1; ftIds[rb] = n; }
                    var v = rb.linearVelocity; v.y = 0f;
                    Row(t, "T" + n + ":" + rb.name, "traffic", p, rb.transform.forward, v.magnitude, 0f, 0f, 0f, false, "");
                }
        }

        void Row(float t, string id, string kind, Vector3 p, Vector3 fwd, float v, float st, float th, float br, bool rev, string why)
        {
            float hd = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            ftb.Append(t.ToString("0.00", FtCI)).Append(',').Append(id.Replace(',', ';')).Append(',').Append(kind).Append(',')
               .Append(p.x.ToString("0.00", FtCI)).Append(',').Append(p.z.ToString("0.00", FtCI)).Append(',').Append(hd.ToString("0.0", FtCI)).Append(',')
               .Append(v.ToString("0.00", FtCI)).Append(',').Append(st.ToString("0.00", FtCI)).Append(',').Append(th.ToString("0.00", FtCI)).Append(',')
               .Append(br.ToString("0.00", FtCI)).Append(',').Append(rev ? 1 : 0).Append(',').Append((why ?? "").Replace(',', ';').Replace(' ', '_')).Append('\n');
        }

        void FieldEnd(string id)
        {
            if (ftb == null) return;
            Post(id + "_field.csv", ftb.ToString());
            var tsw = TrafficSystem.Instance;
            if (tsw != null) Post(id + "_wrecks.txt", ftWreckNotes.ToString());
            Say("FIELD TRACE " + id + ": " + (ftb.Length / 1024) + " KB");
            ftb = null;
        }

        /// <summary>&amp;seed=N: the race's draws seeded (RacePlayCheck's recipe), once the race manager is up.</summary>
        void FieldSeed()
        {
            string s = ShotLink.Query(Request() ?? "", "seed");
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int seed)) Random.InitState(9173 + seed * 101);
        }
    }
}
