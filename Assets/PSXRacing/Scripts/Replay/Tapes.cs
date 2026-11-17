using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE TAPES (owner, 2026-11-16: "The tapes can be saved (for actual
    /// player to rewatch or clip into a montage), then sell for in-game
    /// money"). A race's replay kept as a labelled cassette: it sits on the
    /// shelf at home, plays on the VCR there (<see cref="TapePlayback"/>),
    /// and the VIDEO counter buys it for what is on it.
    ///
    /// THE FILE is small and versioned. "PSXT", a u16 version, then a header
    /// the shelf reads on its own (the label, the field, the facts the price
    /// is made of), then the samples deflated: every car at 15 Hz (every
    /// other sample of the 30 Hz recording), position as three floats and
    /// everything else quantized. A four-car, three-minute race is about
    /// half a megabyte before deflate. Nothing of a pizza load or the
    /// traffic rides on a tape; the cars are the picture.
    ///
    /// Version rule: a reader refuses a NEWER version (the shelf names it as
    /// "a tape from a newer build") and reads every older one. Version 1 is
    /// the first; there is nothing older.
    ///
    /// Files: Application.persistentDataPath/Tapes/&lt;id&gt;.tape, one per tape.
    /// Tapes are GLOBAL, not per career: the career save has no stable id to
    /// file them under, and the save itself is not touched by any of this
    /// (both ways of loading a save stay exactly as they were).
    ///
    /// Switch: PSX_TAPES=0 hides every tape door (the results line, the save
    /// keys, the shelf and the VIDEO counter); the files stay where they are.
    /// </summary>
    public static class Tapes
    {
        static int enabled = -1, montageOn = -1;

        /// <summary>THE MONTAGE door on the shelf (PSX_MONTAGE=1 to open it;
        /// off by default until its own check has played one).</summary>
        public static bool MontageEnabled
        {
            get
            {
                if (montageOn < 0) montageOn = System.Environment.GetEnvironmentVariable("PSX_MONTAGE") == "1" ? 1 : 0;
                return montageOn == 1 && Enabled;
            }
        }
        static int flexOn = -1;
        /// <summary>The flex score (PSX_TAPE_FLEX, default on): a tape's price
        /// is the sum of its clips plus the result. 0 = round 1's price.</summary>
        public static bool FlexOn
        {
            get
            {
                if (flexOn < 0) flexOn = System.Environment.GetEnvironmentVariable("PSX_TAPE_FLEX") == "0" ? 0 : 1;
                return flexOn == 1;
            }
        }

        /// <summary>For tools: open or shut the montage door in this process.</summary>
        public static void ForceMontage(bool on) => montageOn = on ? 1 : 0;
        /// <summary>The tape doors are open (PSX_TAPES, default on).</summary>
        public static bool Enabled
        {
            get
            {
                if (enabled < 0) enabled = System.Environment.GetEnvironmentVariable("PSX_TAPES") == "0" ? 0 : 1;
                return enabled == 1;
            }
        }

        public const string Magic = "PSXT";
        /// <summary>2 (2026-11-16, round 2): a tape carries the race points it
        /// was shot at and the clips its cameras took, each with what it saw
        /// (the EXT2 block after the samples). A v1 tape has neither: it
        /// loads and plays, and is priced the old way.</summary>
        public const ushort Version = 2;
        /// <summary>Samples per second on a tape: every other sample of the race's 30.</summary>
        public const int TapeHz = 15;
        /// <summary>No tape shorter than this: a countdown is not a tape.</summary>
        public const float MinSeconds = 5f;
        /// <summary>Longest tape: a T-120 at SP is two hours; the recorder caps a race at 15 min anyway.</summary>
        public const float MaxSeconds = 15f * 60f;

        /// <summary>For tools and the self-test: tapes go here instead of the
        /// player's own folder (never write test tapes onto the owner's shelf).</summary>
        public static string DirOverride;

        public static string Dir =>
            !string.IsNullOrEmpty(DirOverride) ? DirOverride : Path.Combine(Application.persistentDataPath, "Tapes");

        // ------------------------------------------------------------------
        //  The label and the facts
        // ------------------------------------------------------------------
        public class Car
        {
            public string specId = "", paint = "", name = "";
            public bool player;
        }

        public class Header
        {
            public ushort version = Version;
            public string id = "";
            public string venueId = "", venueName = "";
            public int day;
            public float clock;
            public int hour;
            public int weather;
            public string result = "";
            public int finishPos, fieldSize;
            /// <summary>Seconds to the next car at the line (the closest of the
            /// car ahead and the car behind); large when nobody was near.</summary>
            public float margin = 99f;
            public int nearMisses, crashes;
            public float topKmh;
            /// <summary>Always 0: the game has no police (yet). Kept in the
            /// format so a later build can fill it without a version bump.</summary>
            public int police;
            public bool won;
            public float duration;
            public int hz = TapeHz;
            public int samples;
            public int playerIndex;
            public List<Car> cars = new List<Car>();
            /// <summary>The file this header was read from (not stored).</summary>
            public string path = "";
            /// <summary>The order tapes went on the shelf (file time, not stored).</summary>
            public long shelvedTicks;

            /// <summary>A MONTAGE: clips of one road and one field joined into
            /// one tape (<see cref="Montage"/>). Its price is set when it is cut
            /// (<see cref="basePrice"/>), and <see cref="cuts"/> are the tape
            /// times each clip after the first starts at - the deck cuts there
            /// (a hard seek) instead of sliding the cars across the map.
            /// Stored in the tape's extension block (after the samples).</summary>
            public bool montage;
            public int basePrice;
            public List<float> cuts = new List<float>();

            /// <summary>THE RACE POINTS the tape was shot at (v2; null on a v1
            /// tape and on a montage) and THE CLIPS its cameras took of the
            /// player's car, in order, each with its flex score.</summary>
            public RaceCourse course;
            public List<TapeClip> clips = new List<TapeClip>();
            public int Laps => course != null ? Mathf.Max(1, course.laps) : 1;

            /// <summary>The clip worth the most, or -1.</summary>
            public int BestClip
            {
                get
                {
                    int best = -1;
                    for (int i = 0; i < clips.Count; i++) if (best < 0 || clips[i].value > clips[best].value) best = i;
                    return best;
                }
            }

            public string PlayerCarName => playerIndex >= 0 && playerIndex < cars.Count ? cars[playerIndex].name : "";

            /// <summary>The spine: "CHIMNEY ROCK · NOV 16 1999 · P1 OF 4".</summary>
            public string Spine => venueName.ToUpperInvariant() + "  ·  " + Camcorder.DateStamp(day) +
                                   (string.IsNullOrEmpty(result) ? "" : "  ·  " + result);
            public string Length => Camcorder.Counter(duration);
        }

        // ------------------------------------------------------------------
        //  THE PRICE at the VIDEO counter
        // ------------------------------------------------------------------
        /// <summary>
        /// What the clerk pays, whole dollars, 1999 money: a dull tape of a
        /// race is fifteen bucks, a win is more, a photo finish more again,
        /// near misses and a crash are what people actually rent, and a tape
        /// that tops 200 km/h on the speedo has something to show. Monotonic
        /// in every fact (the self-test holds it to that), scaled down for a
        /// tape too short to be worth a rental.
        /// </summary>
        public static int Price(Header h)
        {
            if (h == null) return 0;
            if (h.montage && h.basePrice > 0) return h.basePrice;
            // ROUND 2: the sum of what each point's camera saw, plus the result.
            if (h.clips != null && h.clips.Count > 0)
            {
                float sum = ResultValue(h);
                foreach (var c in h.clips) sum += c.value;
                return Mathf.Max(5, Mathf.RoundToInt(sum));
            }
            float v = 15f;
            if (h.won) v += 40f;
            v += 5f * Mathf.Clamp(h.fieldSize - 1, 0, 8);
            if (h.fieldSize > 1)
            {
                if (h.margin < 0.25f) v += 70f;
                else if (h.margin < 0.5f) v += 50f;
                else if (h.margin < 1f) v += 25f;
            }
            v += 12f * Mathf.Clamp(h.nearMisses, 0, 10);
            v += 35f * Mathf.Clamp(h.crashes, 0, 3);
            v += Mathf.Clamp((h.topKmh - 160f) * 0.5f, 0f, 80f);
            v += 60f * Mathf.Clamp(h.police, 0, 1);
            float len = Mathf.Clamp(h.duration / 60f, 0.5f, 1.2f);
            return Mathf.Max(5, Mathf.RoundToInt(v * len));
        }

        /// <summary>The RESULT's share of a clip tape's price: that it is a
        /// race at all, a win or a podium, the size of the field, a close
        /// finish. (A police chase would go here: the game has none.)</summary>
        public static int ResultValue(Header h)
        {
            if (h == null) return 0;
            float v = 10f;
            if (h.won) v += 40f;
            else if (h.finishPos > 0 && h.finishPos <= 3 && h.fieldSize >= 4) v += 15f;
            v += 4f * Mathf.Clamp(h.fieldSize - 1, 0, 7);
            if (h.fieldSize > 1)
            {
                if (h.margin < 0.25f) v += 50f;
                else if (h.margin < 0.5f) v += 35f;
                else if (h.margin < 1f) v += 20f;
            }
            v += 60f * Mathf.Clamp(h.police, 0, 1);
            return Mathf.RoundToInt(v);
        }

        /// <summary>The clips, one line each: "CP 3: 94 MPH · SLIDE · LED  +$37".</summary>
        public static List<string> ClipLines(Header h, bool withValue)
        {
            var lines = new List<string>();
            if (h == null || h.clips == null) return lines;
            foreach (var c in h.clips)
            {
                string line = TapeClips.Line(c, h.Laps);
                if (withValue) line += (c.value >= 0 ? "  +$" + c.value : "  -$" + (-c.value));
                lines.Add(line);
            }
            return lines;
        }

        /// <summary>The tape's best clip for its row: "CP 3: 94 MPH · SLIDE · LED".</summary>
        public static string BestLine(Header h)
        {
            if (h == null || h.clips == null || h.clips.Count == 0) return "";
            return TapeClips.Line(h.clips[h.BestClip], h.Laps);
        }

        /// <summary>What is on it, as the clerk would say it.</summary>
        public static string Pitch(Header h)
        {
            if (h != null && h.clips != null && h.clips.Count > 0) return ClipPitch(h);
            var parts = new List<string>();
            if (h.won) parts.Add("a win");
            else if (h.finishPos > 0 && h.fieldSize > 1) parts.Add("P" + h.finishPos + " of " + h.fieldSize);
            if (h.fieldSize > 1 && h.margin < 1f) parts.Add("a close finish (" + h.margin.ToString("0.00") + " s)");
            if (h.nearMisses > 0) parts.Add(h.nearMisses + (h.nearMisses == 1 ? " near miss" : " near misses"));
            if (h.crashes > 0) parts.Add(h.crashes == 1 ? "a crash" : h.crashes + " crashes");
            if (h.topKmh >= 160f) parts.Add(Mathf.RoundToInt(SpeedUnits.FromKmh(h.topKmh)) + SpeedUnits.Suffix + " on the clock");
            if (parts.Count == 0) return "Nothing much happens on it.";
            return char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts).Substring(1) + ".";
        }

        /// <summary>A clip tape in the clerk's words: how much of the race is
        /// on it and the moments worth money.</summary>
        static string ClipPitch(Header h)
        {
            int cps = 0, slides = 0, passes = 0, misses = 0, crashes = 0, air = 0;
            bool start = false, finish = false;
            foreach (var c in h.clips)
            {
                if (c.kind == TapeClip.Kind.Start) start = true;
                if (c.kind == TapeClip.Kind.Finish) finish = true;
                if (c.kind == TapeClip.Kind.Checkpoint) cps++;
                if (c.Has(TapeClip.Slide)) slides++;
                if (c.Has(TapeClip.Pass)) passes++;
                if (c.Has(TapeClip.NearMiss)) misses++;
                if (c.Has(TapeClip.Crash) || c.Has(TapeClip.Spin)) crashes++;
                if (c.Has(TapeClip.Air)) air++;
            }
            var parts = new List<string>();
            parts.Add((start ? "the start, " : "") + cps + (cps == 1 ? " checkpoint" : " checkpoints") +
                      (finish ? " and the finish" : ", no finish"));
            if (h.won) parts.Add("a win");
            else if (h.finishPos > 0 && h.fieldSize > 1 && finish) parts.Add("P" + h.finishPos + " of " + h.fieldSize);
            if (slides > 0) parts.Add(slides == 1 ? "a slide on camera" : slides + " slides on camera");
            if (passes > 0) parts.Add(passes == 1 ? "a pass" : passes + " passes");
            if (misses > 0) parts.Add(misses == 1 ? "a near miss" : misses + " near misses");
            if (air > 0) parts.Add("air");
            if (crashes > 0) parts.Add(crashes == 1 ? "one mess" : crashes + " messes");
            string t = string.Join(", ", parts);
            return char.ToUpperInvariant(t[0]) + t.Substring(1) + ".";
        }

        /// <summary>
        /// THE CLIPS of the race just run: what each point's camera took of
        /// the player's car and what it saw (round 2). Fills
        /// <see cref="Header.course"/> and <see cref="Header.clips"/>.
        /// </summary>
        public static void Flex(Header h, RaceReplay rp, RaceManager rm)
        {
            var plan = RacePlan.For(rm, rp);
            if (plan == null || plan.course == null || h.playerIndex < 0 || h.playerIndex >= rp.TrackCount) return;
            var times = rp.SampleTimes;
            var mine = rp.TrackFrames(h.playerIndex);
            var clips = TapeClips.Plan(plan.course, times, mine);
            var others = new List<IReadOnlyList<RaceReplay.Frame>>();
            for (int i = 0; i < rp.TrackCount; i++) if (i != h.playerIndex) others.Add(rp.TrackFrames(i));
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                TapeClips.Score(ref c, times, mine, others, Mathf.Max(1, h.fieldSize), rp.TrafficAt);
                clips[i] = c;
            }
            // The tape's own copy of the course: a later feed cannot change it.
            var course = new RaceCourse { loop = plan.course.loop, laps = plan.course.laps, source = plan.course.source };
            course.points.AddRange(plan.course.points);
            h.course = course;
            h.clips = clips;
        }

        // ------------------------------------------------------------------
        //  SAVE: the race just run, off RaceReplay
        // ------------------------------------------------------------------
        /// <summary>
        /// Copy the race's recording onto a new tape. Null and a reason when
        /// there is nothing worth a tape (no recording, still recording, too
        /// short); the header of the tape written otherwise.
        /// </summary>
        public static Header SaveFromReplay(RaceReplay rp, RaceManager rm, out string why)
        {
            why = null;
            if (rp == null || rp.TrackCount == 0) { why = "NOTHING RECORDED"; return null; }
            if (!rp.RecordingDone) { why = "STILL RECORDING"; return null; }
            var times = rp.SampleTimes;
            float dur = times.Count > 1 ? times[times.Count - 1] - times[0] : 0f;
            if (dur < MinSeconds) { why = "TOO SHORT FOR A TAPE"; return null; }

            var h = new Header();
            int track = RaceHandoff.TrackIndex;
            var def = TrackCatalog.At(track);
            h.venueId = def.id ?? "";
            h.venueName = string.IsNullOrEmpty(def.name) ? h.venueId : def.name;
            h.day = Mathf.Max(1, RaceHandoff.CalendarDay);
            h.hour = TimeOfDay.Current;
            h.clock = TimeOfDay.ClockHours(TimeOfDay.Current);
            h.weather = (int)Seasons.CurrentWeather;

            // The field: which recorded car is the player's, and what each is.
            h.playerIndex = 0;
            for (int i = 0; i < rp.TrackCount; i++)
            {
                var c = rp.TrackCar(i);
                bool isPlayer = rm != null && c != null && c == rm.playerCar;
                if (isPlayer) h.playerIndex = i;
                var spec = c != null ? c.activeSpec : null;
                h.cars.Add(new Car
                {
                    specId = spec != null && !string.IsNullOrEmpty(spec.id) ? spec.id : "",
                    paint = isPlayer ? (RaceHandoff.CarPaintSkin ?? "") : "",
                    name = rp.TrackName(i),
                    player = isPlayer,
                });
            }

            // The result, from the race itself.
            if (rm != null && rm.playerCar != null)
            {
                h.fieldSize = rm.allCars.Count;
                h.finishPos = rm.GetPosition(rm.playerCar);
                var me = rm.GetProgress(rm.playerCar);
                if (me != null && me.finished)
                {
                    float best = 99f;
                    foreach (var c in rm.allCars)
                    {
                        if (c == null || c == rm.playerCar) continue;
                        var p = rm.GetProgress(c);
                        if (p == null || !p.finished) continue;
                        best = Mathf.Min(best, Mathf.Abs(p.finishTime - me.finishTime));
                    }
                    h.margin = best;
                }
            }
            else
            {
                h.fieldSize = Mathf.Max(1, RaceHandoff.FieldSize);
                h.finishPos = RaceHandoff.FinishPos;
            }
            h.won = h.finishPos == 1 && h.fieldSize > 1;
            h.result = h.fieldSize > 1 && h.finishPos > 0 ? "P" + h.finishPos + " OF " + h.fieldSize
                     : h.fieldSize <= 1 ? "SOLO RUN" : "";

            // Decimate to the tape's rate: every other sample (30 -> 15 Hz).
            int step = Mathf.Max(1, Mathf.RoundToInt(RaceReplay.SampleHz / TapeHz));
            var keep = new List<int>();
            for (int k = 0; k < times.Count; k += step) keep.Add(k);
            if (keep[keep.Count - 1] != times.Count - 1) keep.Add(times.Count - 1);

            var frames = new List<List<RaceReplay.Frame>>();
            for (int i = 0; i < rp.TrackCount; i++)
            {
                var src = rp.TrackFrames(i);
                var list = new List<RaceReplay.Frame>(keep.Count);
                foreach (int k in keep) list.Add(src[Mathf.Min(k, src.Count - 1)]);
                frames.Add(list);
            }
            var ts = new List<float>(keep.Count);
            float t0 = times[0];
            foreach (int k in keep) ts.Add(times[k] - t0);

            // The facts the price is made of, off the full-rate recording.
            Facts(rp, h.playerIndex, out h.topKmh, out h.crashes, out h.nearMisses);
            // ...and what each race point's camera saw (round 2).
            if (FlexOn)
            {
                try { Flex(h, rp, rm); }
                catch (System.Exception e) { Debug.LogWarning("[Tapes] flex scoring failed: " + e.Message); h.course = null; h.clips = new List<TapeClip>(); }
            }

            h.duration = ts[ts.Count - 1];
            h.samples = ts.Count;
            h.id = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Random.Range(0, 0xFFFF).ToString("x4");
            try
            {
                Directory.CreateDirectory(Dir);
                h.path = Path.Combine(Dir, h.id + ".tape");
                using (var fs = File.Create(h.path)) Write(fs, h, ts, frames);
                h.shelvedTicks = System.DateTime.UtcNow.Ticks;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Tapes] save failed: " + e.Message);
                why = "THE TAPE JAMMED (" + e.GetType().Name + ")";
                return null;
            }
            return h;
        }

        /// <summary>Top speed, crashes and near misses off the player's own
        /// full-rate frames and the recorded traffic.</summary>
        static void Facts(RaceReplay rp, int player, out float topKmh, out int crashes, out int nearMisses)
        {
            topKmh = 0f; crashes = 0; nearMisses = 0;
            if (player < 0 || player >= rp.TrackCount) return;
            var f = rp.TrackFrames(player);
            var times = rp.SampleTimes;
            int n = Mathf.Min(f.Count, times.Count);
            foreach (var fr in f) topKmh = Mathf.Max(topKmh, fr.speedKmh);

            // A CRASH: thirty km/h gone in 0.3 s. No brakes do that (it is
            // nearly 3 g); a wall, a car or a tree does.
            float lastCrash = -99f;
            int win = Mathf.Max(1, Mathf.RoundToInt(0.3f * RaceReplay.SampleHz));
            for (int k = 0; k + win < n; k++)
            {
                if (times[k] - lastCrash < 2f) continue;
                if (f[k].speedKmh - f[k + win].speedKmh > 30f) { crashes++; lastCrash = times[k]; }
            }

            // A NEAR MISS: alongside a live traffic car (overlapping lengthwise,
            // within about a metre door to door) at over 60 km/h, and no crash
            // in the next second. One per second at most.
            var traffic = new List<Vector3>();
            float lastMiss = -99f;
            for (int k = 0; k < n; k += 2)
            {
                if (times[k] - lastMiss < 1f) continue;
                if (f[k].speedKmh < 60f) continue;
                if (!rp.TrafficAt(k, traffic) || traffic.Count == 0) continue;
                var inv = Quaternion.Inverse(f[k].rot);
                bool hit = false;
                foreach (var p in traffic)
                {
                    Vector3 d = inv * (p - f[k].pos);
                    if (Mathf.Abs(d.x) < 3.0f && Mathf.Abs(d.x) > 1.2f && Mathf.Abs(d.z) < 3f && Mathf.Abs(d.y) < 3f) { hit = true; break; }
                }
                if (!hit) continue;
                int after = Mathf.Min(n - 1, k + Mathf.RoundToInt(RaceReplay.SampleHz));
                bool crashed = f[k].speedKmh - f[after].speedKmh > 30f;
                if (crashed) continue;
                nearMisses++;
                lastMiss = times[k];
            }
        }

        // ------------------------------------------------------------------
        //  The file
        // ------------------------------------------------------------------
        static void WriteHeader(BinaryWriter w, Header h)
        {
            w.Write(h.id ?? "");
            w.Write(h.venueId ?? ""); w.Write(h.venueName ?? "");
            w.Write(h.day); w.Write(h.clock); w.Write(h.hour); w.Write(h.weather);
            w.Write(h.result ?? "");
            w.Write(h.finishPos); w.Write(h.fieldSize); w.Write(h.margin);
            w.Write(h.nearMisses); w.Write(h.crashes); w.Write(h.topKmh); w.Write(h.police);
            w.Write(h.won);
            w.Write(h.duration); w.Write(h.hz); w.Write(h.samples);
            w.Write(h.playerIndex);
            w.Write(h.cars.Count);
            foreach (var c in h.cars)
            {
                w.Write(c.specId ?? ""); w.Write(c.paint ?? ""); w.Write(c.name ?? ""); w.Write(c.player);
            }
        }

        static Header ReadHeaderBody(BinaryReader r, ushort version)
        {
            var h = new Header { version = version };
            h.id = r.ReadString();
            h.venueId = r.ReadString(); h.venueName = r.ReadString();
            h.day = r.ReadInt32(); h.clock = r.ReadSingle(); h.hour = r.ReadInt32(); h.weather = r.ReadInt32();
            h.result = r.ReadString();
            h.finishPos = r.ReadInt32(); h.fieldSize = r.ReadInt32(); h.margin = r.ReadSingle();
            h.nearMisses = r.ReadInt32(); h.crashes = r.ReadInt32(); h.topKmh = r.ReadSingle(); h.police = r.ReadInt32();
            h.won = r.ReadBoolean();
            h.duration = r.ReadSingle(); h.hz = r.ReadInt32(); h.samples = r.ReadInt32();
            h.playerIndex = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("bad field size " + n);
            for (int i = 0; i < n; i++)
                h.cars.Add(new Car { specId = r.ReadString(), paint = r.ReadString(), name = r.ReadString(), player = r.ReadBoolean() });
            if (h.samples < 2 || h.samples > MaxSeconds * TapeHz * 2 + 16) throw new InvalidDataException("bad sample count " + h.samples);
            return h;
        }

        const float RotQ = 32767f;
        const float RollQ = 32767f / 180f;

        static short Q(float v, float scale) => (short)Mathf.Clamp(Mathf.RoundToInt(v * scale), short.MinValue, short.MaxValue);
        static ushort UQ(float v, float scale) => (ushort)Mathf.Clamp(Mathf.RoundToInt(v * scale), 0, ushort.MaxValue);

        static void WriteRot(BinaryWriter w, Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-6f) q = Quaternion.identity; else { q.x /= m; q.y /= m; q.z /= m; q.w /= m; }
            w.Write(Q(q.x, RotQ)); w.Write(Q(q.y, RotQ)); w.Write(Q(q.z, RotQ)); w.Write(Q(q.w, RotQ));
        }

        static Quaternion ReadRot(BinaryReader r)
        {
            var q = new Quaternion(r.ReadInt16() / RotQ, r.ReadInt16() / RotQ, r.ReadInt16() / RotQ, r.ReadInt16() / RotQ);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-4f) return Quaternion.identity;
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        /// <summary>One car's sample, 57 bytes before deflate.</summary>
        static void WriteFrame(BinaryWriter w, RaceReplay.Frame f)
        {
            w.Write(f.pos.x); w.Write(f.pos.y); w.Write(f.pos.z);
            WriteRot(w, f.rot);
            WriteRot(w, f.lean);
            w.Write(UQ(f.speedKmh, 10f));
            w.Write(Q(f.forwardSpeed, 100f));
            w.Write(UQ(f.rpm, 1f));
            w.Write(Q(f.steerDeg, 100f));
            w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(f.throttle * 255f), 0, 255));
            w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(f.brake * 255f), 0, 255));
            w.Write(f.gear);
            w.Write(f.lap); w.Write(f.place);
            // No seat on a tape (hubs and body height are not stored): the
            // flag would seat the body at the origin.
            w.Write((byte)(f.flags & ~RaceReplay.FlagSeat));
            w.Write(Q(f.slipF, 100f)); w.Write(Q(f.slipR, 100f));
            w.Write(UQ(f.slideF, 100f)); w.Write(UQ(f.slideR, 100f));
            w.Write(Q(f.roll0, RollQ)); w.Write(Q(f.roll1, RollQ)); w.Write(Q(f.roll2, RollQ)); w.Write(Q(f.roll3, RollQ));
        }

        static RaceReplay.Frame ReadFrame(BinaryReader r)
        {
            var f = new RaceReplay.Frame();
            f.pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            f.rot = ReadRot(r);
            f.lean = ReadRot(r);
            f.speedKmh = r.ReadUInt16() / 10f;
            f.forwardSpeed = r.ReadInt16() / 100f;
            f.rpm = r.ReadUInt16();
            f.steerDeg = r.ReadInt16() / 100f;
            f.throttle = r.ReadByte() / 255f;
            f.brake = r.ReadByte() / 255f;
            f.gear = r.ReadSByte();
            f.lap = r.ReadByte(); f.place = r.ReadByte();
            f.flags = (byte)(r.ReadByte() & ~RaceReplay.FlagSeat);
            f.slipF = r.ReadInt16() / 100f; f.slipR = r.ReadInt16() / 100f;
            f.slideF = r.ReadUInt16() / 100f; f.slideR = r.ReadUInt16() / 100f;
            f.roll0 = r.ReadInt16() / RollQ; f.roll1 = r.ReadInt16() / RollQ;
            f.roll2 = r.ReadInt16() / RollQ; f.roll3 = r.ReadInt16() / RollQ;
            return f;
        }

        /// <summary>Write a whole tape: header in the clear, samples deflated.</summary>
        public static void Write(Stream s, Header h, List<float> times, List<List<RaceReplay.Frame>> frames)
        {
            h.samples = times.Count;
            h.duration = times.Count > 0 ? times[times.Count - 1] : 0f;
            var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true);
            w.Write(System.Text.Encoding.ASCII.GetBytes(Magic));
            w.Write(Version);
            WriteHeader(w, h);
            byte[] payload;
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                using (var bw = new BinaryWriter(z, System.Text.Encoding.UTF8, true))
                {
                    for (int k = 0; k < times.Count; k++)
                    {
                        bw.Write(times[k]);
                        for (int i = 0; i < h.cars.Count; i++) WriteFrame(bw, frames[i][k]);
                    }
                }
                payload = ms.ToArray();
            }
            w.Write(payload.Length);
            w.Write(payload);
            // The extension block: only a montage has one.
            if (h.montage || (h.cuts != null && h.cuts.Count > 0))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes(ExtMagic));
                w.Write(h.montage);
                w.Write(h.basePrice);
                int nc = h.cuts != null ? h.cuts.Count : 0;
                w.Write(nc);
                for (int i = 0; i < nc; i++) w.Write(h.cuts[i]);
            }
            // The race points and the clips (v2).
            if (h.course != null && h.course.points.Count > 0)
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes(Ext2Magic));
                WriteCourse(w, h);
            }
            w.Flush();
        }

        /// <summary>The header only (the shelf). Null with a reason for a
        /// file that is not a tape, a truncated one, or a newer version.</summary>
        public static Header ReadHeader(Stream s, out string why)
        {
            why = null;
            try
            {
                var r = new BinaryReader(s, System.Text.Encoding.UTF8, true);
                var magic = r.ReadBytes(4);
                if (magic.Length != 4 || System.Text.Encoding.ASCII.GetString(magic) != Magic) { why = "NOT A TAPE"; return null; }
                ushort v = r.ReadUInt16();
                if (v > Version) { why = "A TAPE FROM A NEWER BUILD (v" + v + ")"; return null; }
                if (v < 1) { why = "UNKNOWN TAPE VERSION " + v; return null; }
                var h = ReadHeaderBody(r, v);
                ReadExtension(s, r, h);
                return h;
            }
            catch (System.Exception e) { why = "UNREADABLE (" + e.GetType().Name + ")"; return null; }
        }

        const string ExtMagic = "EXT1", Ext2Magic = "EXT2";

        static void WriteCourse(BinaryWriter w, Header h)
        {
            var c = h.course;
            w.Write((byte)Mathf.Clamp(c.laps, 1, 255));
            w.Write(c.loop);
            w.Write(c.source ?? "");
            w.Write((ushort)c.points.Count);
            foreach (var p in c.points)
            {
                w.Write((byte)p.kind);
                w.Write((ushort)Mathf.Clamp(p.index, 0, 65535));
                w.Write(p.pos.x); w.Write(p.pos.y); w.Write(p.pos.z);
                w.Write(p.heading.x); w.Write(p.heading.z);
                w.Write(p.halfWidth);
            }
            int n = h.clips != null ? h.clips.Count : 0;
            w.Write((ushort)n);
            for (int i = 0; i < n; i++)
            {
                var k = h.clips[i];
                w.Write((byte)k.kind);
                w.Write((ushort)Mathf.Clamp(k.point, 0, 65535));
                w.Write((ushort)Mathf.Clamp(k.index, 0, 65535));
                w.Write((byte)Mathf.Clamp(k.lap, 0, 255));
                w.Write(k.tIn); w.Write(k.tPass); w.Write(k.tOut);
                w.Write(k.kmh);
                w.Write(k.tags);
                w.Write(k.slideSec); w.Write(k.slideDeg);
                w.Write((short)Mathf.Clamp(k.value, -32000, 32000));
            }
        }

        static void ReadCourse(BinaryReader r, Header h)
        {
            var c = new RaceCourse { source = "tape" };
            c.laps = Mathf.Max(1, (int)r.ReadByte());
            c.loop = r.ReadBoolean();
            r.ReadString();   // where the points came from when it was shot
            int np = r.ReadUInt16();
            if (np > 4096) throw new System.IO.InvalidDataException("points");
            for (int i = 0; i < np; i++)
            {
                var p = new RacePoint { pathIdx = -1 };
                p.kind = (RacePoint.Kind)r.ReadByte();
                p.index = r.ReadUInt16();
                p.pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float hx = r.ReadSingle(), hz = r.ReadSingle();
                p.heading = new Vector3(hx, 0f, hz);
                p.halfWidth = r.ReadSingle();
                c.points.Add(p);
            }
            int nk = r.ReadUInt16();
            if (nk > 8192) throw new System.IO.InvalidDataException("clips");
            var clips = new List<TapeClip>(nk);
            for (int i = 0; i < nk; i++)
            {
                var k = new TapeClip();
                k.kind = (TapeClip.Kind)r.ReadByte();
                k.point = r.ReadUInt16();
                k.index = r.ReadUInt16();
                k.lap = r.ReadByte();
                k.tIn = r.ReadSingle(); k.tPass = r.ReadSingle(); k.tOut = r.ReadSingle();
                k.kmh = r.ReadSingle();
                k.tags = r.ReadUInt16();
                k.slideSec = r.ReadSingle(); k.slideDeg = r.ReadSingle();
                k.value = r.ReadInt16();
                clips.Add(k);
            }
            h.course = c;
            h.clips = clips;
        }

        /// <summary>The extension block after the samples (a montage's cuts and
        /// price), read without disturbing the stream: it is put back where the
        /// header ended, so the samples can be read next. A tape without one
        /// (every race tape) reads as it is.</summary>
        static void ReadExtension(Stream s, BinaryReader r, Header h)
        {
            if (!s.CanSeek) return;
            long at = s.Position;
            try
            {
                if (s.Length - at < 4) return;
                int len = r.ReadInt32();
                if (len <= 0 || at + 4 + len + 4 > s.Length) return;
                s.Position = at + 4 + len;
                // The blocks after the samples, in the order they were written.
                while (s.Length - s.Position >= 4)
                {
                    var magic = r.ReadBytes(4);
                    string m = magic.Length == 4 ? System.Text.Encoding.ASCII.GetString(magic) : "";
                    if (m == ExtMagic)
                    {
                        h.montage = r.ReadBoolean();
                        h.basePrice = r.ReadInt32();
                        int nc = r.ReadInt32();
                        if (nc < 0 || nc > 4096) { h.montage = false; h.basePrice = 0; return; }
                        for (int i = 0; i < nc; i++) h.cuts.Add(r.ReadSingle());
                    }
                    else if (m == Ext2Magic) ReadCourse(r, h);
                    else break;
                }
            }
            catch (System.Exception) { h.cuts.Clear(); h.course = null; h.clips = new List<TapeClip>(); }
            finally { s.Position = at; }
        }

        // ------------------------------------------------------------------
        //  THE MONTAGE: clips joined into a new tape
        // ------------------------------------------------------------------
        public class Clip
        {
            public Header tape;
            public float tIn, tOut;
            public float Length => Mathf.Max(0f, tOut - tIn);
        }

        /// <summary>A clip can join a montage that starts with
        /// <paramref name="first"/>: the same road and the same field (the
        /// deck plays one venue with one set of cars).</summary>
        public static bool SameRace(Header first, Header h)
        {
            if (first == null || h == null) return false;
            if (first.venueId != h.venueId || first.cars.Count != h.cars.Count || first.playerIndex != h.playerIndex) return false;
            for (int i = 0; i < h.cars.Count; i++)
                if ((first.cars[i].specId ?? "") != (h.cars[i].specId ?? "")) return false;
            return true;
        }

        /// <summary>
        /// Cut the clips out of their tapes and join them, in order, onto a new
        /// tape. The tapes the clips came from are taped over (they leave the
        /// shelf) once the montage is safely written: an edit is made from the
        /// originals, and one race cannot be sold twice. The montage sells for
        /// what its tapes would have, and a little more for the editing.
        /// </summary>
        public static Header Montage(List<Clip> clips, out string why)
        {
            why = null;
            if (clips == null || clips.Count == 0) { why = "NO CLIPS"; return null; }
            var first = clips[0].tape;
            foreach (var c in clips)
            {
                if (c.tape == null || string.IsNullOrEmpty(c.tape.path)) { why = "A CLIP HAS NO TAPE"; return null; }
                if (!SameRace(first, c.tape)) { why = "ONE ROAD AND ONE FIELD PER MONTAGE"; return null; }
                if (c.Length < 1f) { why = "A CLIP IS UNDER A SECOND"; return null; }
            }

            var cache = new Dictionary<string, (List<float> t, List<List<RaceReplay.Frame>> f)>();
            var times = new List<float>();
            var frames = new List<List<RaceReplay.Frame>>();
            for (int i = 0; i < first.cars.Count; i++) frames.Add(new List<RaceReplay.Frame>());
            var cuts = new List<float>();
            var sources = new List<Header>();
            float topKmh = 0f;
            foreach (var c in clips)
            {
                if (!cache.TryGetValue(c.tape.path, out var src))
                {
                    var hh = Load(c.tape.path, out var tt, out var ff, out string w);
                    if (hh == null) { why = "A TAPE WILL NOT LOAD: " + w; return null; }
                    src = (tt, ff);
                    cache[c.tape.path] = src;
                    sources.Add(c.tape);
                }
                int k0 = -1, k1 = -1;
                for (int k = 0; k < src.t.Count; k++)
                {
                    if (src.t[k] < c.tIn - 1e-4f) continue;
                    if (src.t[k] > c.tOut + 1e-4f) break;
                    if (k0 < 0) k0 = k;
                    k1 = k;
                }
                if (k0 < 0 || k1 - k0 < 2) { why = "A CLIP HAS NO PICTURE ON IT"; return null; }
                // The clip starts where the last one ended: the same instant,
                // so the join is a cut, not a slide (the deck seeks hard there).
                float offset = times.Count > 0 ? times[times.Count - 1] : 0f;
                if (times.Count > 0) cuts.Add(offset);
                float t0 = src.t[k0];
                for (int k = k0; k <= k1; k++)
                {
                    times.Add(offset + (src.t[k] - t0));
                    for (int car = 0; car < frames.Count; car++) frames[car].Add(src.f[car][k]);
                    topKmh = Mathf.Max(topKmh, src.f[first.playerIndex][k].speedKmh);
                }
            }
            if (times.Count < 2 || times[times.Count - 1] < MinSeconds) { why = "TOO SHORT FOR A TAPE"; return null; }

            var h = new Header
            {
                venueId = first.venueId, venueName = first.venueName,
                day = first.day, clock = first.clock, hour = first.hour, weather = first.weather,
                finishPos = first.finishPos, fieldSize = first.fieldSize,
                playerIndex = first.playerIndex,
                montage = true, cuts = cuts,
                topKmh = topKmh,
            };
            foreach (var c in first.cars) h.cars.Add(new Car { specId = c.specId, paint = c.paint, name = c.name, player = c.player });
            float worth = 0f;
            h.margin = 99f;
            foreach (var sh in sources)
            {
                worth += Price(sh);
                h.won |= sh.won;
                h.margin = Mathf.Min(h.margin, sh.margin);
                h.nearMisses += sh.nearMisses;
                h.crashes += sh.crashes;
            }
            h.basePrice = Mathf.Max(5, Mathf.RoundToInt(worth * MontageBonus));
            h.result = "MONTAGE · " + clips.Count + (clips.Count == 1 ? " CLIP" : " CLIPS");
            h.id = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_m" + Random.Range(0, 0xFFF).ToString("x3");
            try
            {
                Directory.CreateDirectory(Dir);
                h.path = Path.Combine(Dir, h.id + ".tape");
                using (var fs = File.Create(h.path)) Write(fs, h, times, frames);
                h.shelvedTicks = System.DateTime.UtcNow.Ticks;
            }
            catch (System.Exception e)
            {
                why = "THE TAPE JAMMED (" + e.GetType().Name + ")";
                return null;
            }
            // Written: the originals are taped over.
            foreach (var sh in sources) Remove(sh);
            return h;
        }

        /// <summary>An edited tape sells for this much more than its tapes would.</summary>
        public const float MontageBonus = 1.15f;

        /// <summary>The whole tape: header, sample clock and every car's frames.</summary>
        public static Header Read(Stream s, out List<float> times, out List<List<RaceReplay.Frame>> frames, out string why)
        {
            times = null; frames = null;
            var h = ReadHeader(s, out why);
            if (h == null) return null;
            try
            {
                var r = new BinaryReader(s, System.Text.Encoding.UTF8, true);
                int len = r.ReadInt32();
                if (len <= 0 || len > 64 * 1024 * 1024) { why = "BAD PAYLOAD"; return null; }
                var payload = r.ReadBytes(len);
                if (payload.Length != len) { why = "TRUNCATED TAPE"; return null; }
                times = new List<float>(h.samples);
                frames = new List<List<RaceReplay.Frame>>();
                for (int i = 0; i < h.cars.Count; i++) frames.Add(new List<RaceReplay.Frame>(h.samples));
                using (var ms = new MemoryStream(payload))
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                using (var br = new BinaryReader(z))
                {
                    for (int k = 0; k < h.samples; k++)
                    {
                        times.Add(br.ReadSingle());
                        for (int i = 0; i < h.cars.Count; i++) frames[i].Add(ReadFrame(br));
                    }
                }
                return h;
            }
            catch (System.Exception e)
            {
                why = e is EndOfStreamException ? "TRUNCATED TAPE" : "UNREADABLE (" + e.GetType().Name + ")";
                times = null; frames = null;
                return null;
            }
        }

        // ------------------------------------------------------------------
        //  The shelf
        // ------------------------------------------------------------------
        /// <summary>Every readable tape on the shelf, newest first.</summary>
        public static List<Header> Shelf()
        {
            var list = new List<Header>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                foreach (var path in Directory.GetFiles(Dir, "*.tape"))
                {
                    try
                    {
                        using (var fs = File.OpenRead(path))
                        {
                            var h = ReadHeader(fs, out _);
                            if (h == null) continue;
                            h.path = path;
                            h.shelvedTicks = File.GetLastWriteTimeUtc(path).Ticks;
                            list.Add(h);
                        }
                    }
                    catch (System.Exception) { }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Tapes] shelf: " + e.Message); }
            list.Sort((a, b) => b.shelvedTicks.CompareTo(a.shelvedTicks));
            return list;
        }

        public static int Count()
        {
            try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.tape").Length : 0; }
            catch (System.Exception) { return 0; }
        }

        /// <summary>Load a tape file whole.</summary>
        public static Header Load(string path, out List<float> times, out List<List<RaceReplay.Frame>> frames, out string why)
        {
            times = null; frames = null;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var h = Read(fs, out times, out frames, out why);
                    if (h != null) h.path = path;
                    return h;
                }
            }
            catch (System.Exception e) { why = "NO TAPE (" + e.GetType().Name + ")"; return null; }
        }

        /// <summary>The tape leaves the shelf (sold). True when the file is gone.</summary>
        public static bool Remove(Header h)
        {
            if (h == null || string.IsNullOrEmpty(h.path)) return false;
            try { if (File.Exists(h.path)) File.Delete(h.path); return !File.Exists(h.path); }
            catch (System.Exception e) { Debug.LogWarning("[Tapes] remove: " + e.Message); return false; }
        }
    }
}
