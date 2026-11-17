using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// One CLIP of a tape: what the camera at one race point shot of one car -
    /// from about 150 m out, through the point, to 70 m past it - and what it
    /// SAW (the flex score: owner 2026-11-16, "gives players a chance to
    /// 'flex' at checkpoints to improve their tape's value").
    /// </summary>
    public struct TapeClip
    {
        public enum Kind : byte { Start = 0, Checkpoint = 1, Lap = 2, Finish = 3 }

        public Kind kind;
        /// <summary>Which point of the course (RaceCourse.points).</summary>
        public int point;
        /// <summary>The checkpoint's number (1-based), 0 otherwise.</summary>
        public int index;
        public int lap;
        /// <summary>Tape seconds: the clip starts, the car is at the point, the clip ends.</summary>
        public float tIn, tPass, tOut;

        // ---- what the camera saw (the player's car) ----
        public float kmh;
        public ushort tags;
        public float slideSec, slideDeg;
        public int value;

        public const ushort Slide = 1, Air = 2, Led = 4, Pass = 8, Door = 16, NearMiss = 32, Crash = 64, Spin = 128,
                            NearMiss2 = 256;
        public bool Has(ushort t) => (tags & t) != 0;
    }

    public static class TapeClips
    {
        /// <summary>The camera picks the car up this far out and leaves it this far past.</summary>
        public const float InM = 150f, OutM = 70f;
        /// <summary>The grid shot before the lights, seconds.</summary>
        public const float GridSeconds = 2.5f;

        // ------------------------------------------------------------------
        //  THE CLIPS of one car's race (pure: the self-test runs it)
        // ------------------------------------------------------------------
        /// <summary>
        /// The clips the course's cameras took of the car whose recording this
        /// is: the start, every checkpoint it reached in order (every lap of
        /// a circuit, the lap line between them), the finish. A point the car
        /// never reached ends the tape there: a tape proves what happened.
        /// </summary>
        public static List<TapeClip> Plan(RaceCourse course, IReadOnlyList<float> times, IReadOnlyList<RaceReplay.Frame> f)
        {
            var clips = new List<TapeClip>();
            if (course == null || course.points.Count < 2 || times == null || f == null) return clips;
            int n = Mathf.Min(times.Count, f.Count);
            if (n < 4) return clips;

            // Metres driven by each sample.
            var dist = new float[n];
            for (int i = 1; i < n; i++) dist[i] = dist[i - 1] + Flat(f[i].pos - f[i - 1].pos).magnitude;

            int iStart = course.IndexOf(RacePoint.Kind.Start), iFinish = course.IndexOf(RacePoint.Kind.Finish);
            if (iStart < 0 || iFinish < 0) return clips;
            var cps = new List<int>();
            for (int i = 0; i < course.points.Count; i++) if (course.points[i].kind == RacePoint.Kind.Checkpoint) cps.Add(i);

            // ---- the start: the grid, the lights, the launch ----
            int move = 0;
            while (move < n - 1 && f[move].speedKmh < 3f) move++;
            int startPass = Cross(course.points[iStart], f, n, 0);
            if (startPass < 0) startPass = move;
            int startOut = Ahead(dist, n, Mathf.Max(startPass, move), OutM);
            var start = new TapeClip
            {
                kind = TapeClip.Kind.Start, point = iStart, lap = 1,
                tIn = Mathf.Max(times[0], times[move] - GridSeconds), tPass = times[startPass], tOut = times[startOut],
            };
            clips.Add(start);

            int cursor = Mathf.Max(startPass, move) + 1;
            int laps = course.loop ? Mathf.Max(1, course.laps) : 1;
            bool broken = false;
            for (int lap = 1; lap <= laps && !broken; lap++)
            {
                foreach (int pi in cps)
                {
                    int at = Cross(course.points[pi], f, n, cursor);
                    if (at < 0) { broken = true; break; }
                    Add(clips, TapeClip.Kind.Checkpoint, pi, course.points[pi].index, lap, at, dist, times, n, false);
                    cursor = at + 1;
                }
                if (broken) break;
                bool last = lap == laps;
                int line = Cross(course.points[iFinish], f, n, cursor);
                if (line < 0) { broken = true; break; }
                Add(clips, last ? TapeClip.Kind.Finish : TapeClip.Kind.Lap, iFinish, 0, last ? lap : lap + 1, line, dist, times, n, last);
                cursor = line + 1;
            }
            return clips;
        }

        static void Add(List<TapeClip> clips, TapeClip.Kind kind, int point, int index, int lap, int at,
                        float[] dist, IReadOnlyList<float> times, int n, bool toEnd)
        {
            int i0 = Behind(dist, at, InM), i1 = toEnd ? n - 1 : Ahead(dist, n, at, OutM);
            var c = new TapeClip { kind = kind, point = point, index = index, lap = lap, tPass = times[at], tIn = times[i0], tOut = times[i1] };
            // Never back into the clip before it: two points close together
            // cut where the first one's camera let go.
            if (clips.Count > 0)
            {
                var prev = clips[clips.Count - 1];
                if (c.tIn < prev.tOut)
                {
                    float cut = Mathf.Min(prev.tOut, Mathf.Max(prev.tPass + 0.5f, (prev.tPass + c.tPass) * 0.5f));
                    prev.tOut = cut; c.tIn = cut;
                    clips[clips.Count - 1] = prev;
                }
            }
            if (c.tOut - c.tIn < 1.5f) c.tOut = Mathf.Min(times[n - 1], c.tIn + 1.5f);
            clips.Add(c);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The sample after which the car crosses the point's line
        /// (either way: the router, not the line, decides what is sensible),
        /// inside its width and on its level; -1 when it never does.</summary>
        static int Cross(RacePoint p, IReadOnlyList<RaceReplay.Frame> f, int n, int from)
        {
            Vector3 h = p.heading, r = Vector3.Cross(Vector3.up, h);
            float reach = p.halfWidth + (p.pathIdx >= 0 ? 14f : 6f);
            for (int i = Mathf.Max(0, from); i < n - 1; i++)
            {
                Vector3 a = f[i].pos - p.pos, b = f[i + 1].pos - p.pos;
                // A respawn or a tow is a jump, not a crossing (teleports pass nothing).
                if ((b - a).sqrMagnitude > 15f * 15f) continue;
                float d0 = Vector3.Dot(a, h), d1 = Vector3.Dot(b, h);
                if ((d0 < 0f) == (d1 < 0f)) continue;
                float u = Mathf.Abs(d0) / Mathf.Max(1e-4f, Mathf.Abs(d0) + Mathf.Abs(d1));
                Vector3 x = Vector3.Lerp(a, b, u);
                if (Mathf.Abs(Vector3.Dot(x, r)) > reach || Mathf.Abs(x.y) > 8f) continue;
                return i;
            }
            return -1;
        }

        static int Behind(float[] dist, int at, float metres)
        {
            int i = at;
            while (i > 0 && dist[at] - dist[i] < metres) i--;
            return i;
        }

        static int Ahead(float[] dist, int n, int at, float metres)
        {
            int i = at;
            while (i < n - 1 && dist[i] - dist[at] < metres) i++;
            return i;
        }

        /// <summary>The clip on screen at a tape time, or -1 between clips.</summary>
        public static int At(List<TapeClip> clips, float t)
        {
            for (int i = 0; i < clips.Count; i++) if (t >= clips[i].tIn && t <= clips[i].tOut) return i;
            return -1;
        }

        // ------------------------------------------------------------------
        //  THE FLEX: what a clip's camera saw of the car (pure)
        // ------------------------------------------------------------------
        /// <summary>
        /// Score one clip from the car's own samples, the rest of the field's
        /// (<paramref name="others"/>, same sample clock) and the traffic
        /// (<paramref name="trafficAt"/>: the live traffic cars at a sample,
        /// or null). <paramref name="field"/> = cars racing.
        /// </summary>
        public static void Score(ref TapeClip c, IReadOnlyList<float> times, IReadOnlyList<RaceReplay.Frame> f,
                                 IList<IReadOnlyList<RaceReplay.Frame>> others, int field,
                                 System.Func<int, List<Vector3>, bool> trafficAt)
        {
            int n = Mathf.Min(times.Count, f.Count);
            c.kmh = 0f; c.tags = 0; c.slideSec = 0f; c.slideDeg = 0f; c.value = 0;
            if (n < 4) return;
            int i0 = Index(times, n, c.tIn), i1 = Index(times, n, c.tOut), ip = Index(times, n, c.tPass);
            c.kmh = Mathf.Abs(f[ip].speedKmh);

            float airSec = 0f, spinSec = 0f;
            int misses = 0;
            float lastMiss = -99f, lastCrash = -99f;
            bool crashed = false, door = false;
            var traffic = new List<Vector3>();
            for (int i = Mathf.Max(1, i0); i <= i1 && i < n - 1; i++)
            {
                float dt = Mathf.Max(1e-3f, times[i + 1] - times[i - 1]);
                Vector3 v = Flat(f[i + 1].pos - f[i - 1].pos) / dt;
                float speed = v.magnitude;
                float step = times[i + 1] - times[i];
                // How sideways: the body's heading against the way it is going.
                if (speed > 8f)
                {
                    float ang = Vector3.Angle(Flat(f[i].rot * Vector3.forward), v);
                    if (ang > 100f) spinSec += step;
                    else if (ang >= 12f) { c.slideSec += step; c.slideDeg = Mathf.Max(c.slideDeg, ang); }
                }
                if (speed > 8f && (f[i].flags & RaceReplay.FlagGrounded) == 0) airSec += step;

                // A crash: thirty km/h gone in a third of a second.
                int later = Index(times, n, times[i] + 0.3f);
                if (times[i] - lastCrash > 2f && f[i].speedKmh - f[later].speedKmh > 30f) { crashed = true; lastCrash = times[i]; }

                // Door to door with a rival: alongside, overlapping.
                if (others != null && Mathf.Abs(f[i].speedKmh) > 40f)
                {
                    var inv = Quaternion.Inverse(f[i].rot);
                    foreach (var o in others)
                    {
                        if (o == null || i >= o.Count) continue;
                        Vector3 d = inv * (o[i].pos - f[i].pos);
                        if (Mathf.Abs(d.x) > 1.2f && Mathf.Abs(d.x) < 3.4f && Mathf.Abs(d.z) < 4.5f && Mathf.Abs(d.y) < 2.5f) { door = true; break; }
                    }
                }
                // A near miss: past a traffic car within a door's width, and no crash after.
                if (trafficAt != null && times[i] - lastMiss > 1f && Mathf.Abs(f[i].speedKmh) > 60f && trafficAt(i, traffic))
                {
                    var inv = Quaternion.Inverse(f[i].rot);
                    foreach (var p in traffic)
                    {
                        Vector3 d = inv * (p - f[i].pos);
                        if (Mathf.Abs(d.x) < 3.0f && Mathf.Abs(d.x) > 1.2f && Mathf.Abs(d.z) < 3f && Mathf.Abs(d.y) < 3f)
                        {
                            int after = Index(times, n, times[i] + 1f);
                            if (f[i].speedKmh - f[after].speedKmh <= 30f) { misses++; lastMiss = times[i]; }
                            break;
                        }
                    }
                }
            }

            float value = 0f;
            if (c.kind != TapeClip.Kind.Start) value += Mathf.Clamp((c.kmh - 60f) * 0.12f, 0f, 25f);
            if (c.slideSec >= 0.4f)
            {
                c.tags |= TapeClip.Slide;
                value += Mathf.Clamp(c.slideSec * 12f, 0f, 30f) + Mathf.Clamp((c.slideDeg - 12f) * 0.5f, 0f, 15f);
            }
            if (airSec >= 0.2f) { c.tags |= TapeClip.Air; value += 20f; }
            if (field > 1)
            {
                if (f[ip].place == 1) { c.tags |= TapeClip.Led; value += 10f; }
                if (f[i1].place > 0 && f[i0].place > 0 && f[i1].place < f[i0].place && c.kind != TapeClip.Kind.Start)
                { c.tags |= TapeClip.Pass; value += 20f; }
                if (door) { c.tags |= TapeClip.Door; value += 12f; }
            }
            if (misses > 0) { c.tags |= TapeClip.NearMiss; value += 15f; }
            if (misses > 1) { c.tags |= TapeClip.NearMiss2; value += 15f; }
            if (crashed) { c.tags |= TapeClip.Crash; value -= 35f; }
            if (spinSec >= 0.25f) { c.tags |= TapeClip.Spin; value -= 20f; }
            c.value = Mathf.RoundToInt(Mathf.Clamp(value, -40f, 120f));
        }

        static int Index(IReadOnlyList<float> times, int n, float t)
        {
            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (times[mid] <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // ------------------------------------------------------------------
        //  Words
        // ------------------------------------------------------------------
        /// <summary>The OSD's name for a clip: "START", "CHECKPOINT 3/9", "LAP 2/3", "FINISH".</summary>
        public static string Label(TapeClip c, int checkpoints, int laps)
        {
            switch (c.kind)
            {
                case TapeClip.Kind.Start: return "START";
                case TapeClip.Kind.Finish: return "FINISH";
                case TapeClip.Kind.Lap: return "LAP " + c.lap + "/" + Mathf.Max(1, laps);
                default: return "CHECKPOINT " + c.index + "/" + Mathf.Max(1, checkpoints);
            }
        }

        /// <summary>The row's short name: "START", "CP 3", "LAP 2", "FINISH" (a
        /// later lap's checkpoint says which: "L2 CP 3").</summary>
        public static string Short(TapeClip c, int laps)
        {
            switch (c.kind)
            {
                case TapeClip.Kind.Start: return "START";
                case TapeClip.Kind.Finish: return "FINISH";
                case TapeClip.Kind.Lap: return "LAP " + c.lap;
                default: return (laps > 1 ? "L" + c.lap + " " : "") + "CP " + c.index;
            }
        }

        /// <summary>What it saw: "94 MPH · SLIDE · LED".</summary>
        public static string Tags(TapeClip c)
        {
            var s = new System.Text.StringBuilder();
            void Put(string w) { if (s.Length > 0) s.Append(" · "); s.Append(w); }
            if (c.kind != TapeClip.Kind.Start)
                Put(Mathf.RoundToInt(SpeedUnits.FromKmh(c.kmh)) + SpeedUnits.Suffix.ToUpperInvariant());
            if (c.Has(TapeClip.Slide)) Put("SLIDE");
            if (c.Has(TapeClip.Air)) Put("AIR");
            if (c.Has(TapeClip.Pass)) Put("PASS");
            if (c.Has(TapeClip.Door)) Put("DOOR TO DOOR");
            if (c.Has(TapeClip.Led)) Put("LED");
            if (c.Has(TapeClip.NearMiss2)) Put("2 NEAR MISSES");
            else if (c.Has(TapeClip.NearMiss)) Put("NEAR MISS");
            if (c.Has(TapeClip.Crash)) Put("CRASH");
            if (c.Has(TapeClip.Spin)) Put("SPIN");
            return s.ToString();
        }

        /// <summary>"CP 3: 94 MPH · SLIDE · LED".</summary>
        public static string Line(TapeClip c, int laps)
        {
            string t = Tags(c);
            return Short(c, laps) + (t.Length > 0 ? ": " + t : "");
        }
    }
}
