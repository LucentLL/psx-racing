using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// THE ROAD LOG (2026-10-29, the owner: "I want to improve road logic (a
    /// lot of strange implementation currently) and I think logging traffic
    /// traveling the city can help improve road logic"). What city traffic
    /// meets on the road network, as events: a wreck and what it hit, a car
    /// stuck with nothing to queue for, a lane it could not reach, a turn that
    /// leaves the pavement, a jolt, a bend tighter than the limit, crossing
    /// flows in one junction, a one-way with no way on... each with its edge,
    /// node, road name and place. <see cref="CityTraffic.Log"/> holds one while
    /// a tool runs (Editor/CityTrafficCensus); null in play, so the traffic
    /// pays nothing for it. Also the coverage: metres driven per edge, the
    /// movements taken across each node, car-seconds simulated.
    /// </summary>
    public sealed class CityRoadLog
    {
        public struct Ev
        {
            public float t;
            public string cat, sub;
            public int node, edge, edge2, car;
            public Vector3 at;
            public float speed, value, heading;
            public string move, detail;
        }

        public readonly List<Ev> events = new List<Ev>();
        /// <summary>Events past this are counted, not kept.</summary>
        public int Cap = 250000;
        public int dropped;
        public readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>Metres driven on each edge (runs), and the movements
        /// completed across a node: (edge in * 2 + arrives at b) -> edge out.</summary>
        public float[] driven;
        public readonly Dictionary<long, int> moves = new Dictionary<long, int>();
        public double carSeconds, simSeconds;
        public int spawned;
        /// <summary>Census spawns: tries, and why they failed.</summary>
        public int spTries, spNoFree, spNoRoad, spNoSpawn, spReject, spBounds, spCrowded;

        public CityRoadLog(int edges) { driven = new float[edges]; }

        public int Add(string cat, string sub, int node, int edge, int edge2, Vector3 at, int car, float speed, float value, string move, string detail, float heading = 0f)
        {
            string k = cat + "/" + sub;
            counts.TryGetValue(k, out int n);
            counts[k] = n + 1;
            if (events.Count >= Cap) { dropped++; return -1; }
            events.Add(new Ev
            {
                t = Time.time, cat = cat, sub = sub, node = node, edge = edge, edge2 = edge2, car = car,
                at = at, speed = speed, value = value, heading = heading, move = move ?? "", detail = detail ?? "",
            });
            return events.Count - 1;
        }

        /// <summary>An episode's event (stuck, off the pavement) takes its
        /// final measure when it ends.</summary>
        public void SetValue(int i, float v)
        {
            if (i < 0 || i >= events.Count) return;
            var e = events[i];
            e.value = v;
            events[i] = e;
        }

        public void Move(int eIn, int dirIn, int eOut)
        {
            long k = ((long)((eIn << 1) | (dirIn > 0 ? 1 : 0)) << 32) | (uint)eOut;
            moves.TryGetValue(k, out int n);
            moves[k] = n + 1;
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        /// <summary>Every event, one CSV row each (the road name from the map).</summary>
        public string ToCsv(CityMap map)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(events.Count * 160 + 256);
            sb.AppendLine("t,cat,sub,node,edge,edge2,road,cls,x,z,y,hdg,car,speed,value,move,detail");
            foreach (var e in events)
            {
                string road = "";
                int cls = -1;
                if (map != null && e.edge >= 0 && e.edge < map.edges.Length) { road = map.edges[e.edge].name; cls = map.edges[e.edge].cls; }
                sb.Append(e.t.ToString("0.0", ic)).Append(',').Append(e.cat).Append(',').Append(e.sub).Append(',')
                  .Append(e.node).Append(',').Append(e.edge).Append(',').Append(e.edge2).Append(',').Append(Csv(road)).Append(',').Append(cls).Append(',')
                  .Append(e.at.x.ToString("0.0", ic)).Append(',').Append(e.at.z.ToString("0.0", ic)).Append(',').Append(e.at.y.ToString("0.0", ic)).Append(',')
                  .Append(e.heading.ToString("0", ic)).Append(',').Append(e.car).Append(',').Append(e.speed.ToString("0.0", ic)).Append(',').Append(e.value.ToString("0.###", ic)).Append(',')
                  .Append(Csv(e.move)).Append(',').Append(Csv(e.detail)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
