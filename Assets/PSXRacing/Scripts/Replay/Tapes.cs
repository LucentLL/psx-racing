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
        static int enabled = -1;
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
        public const ushort Version = 1;
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

        /// <summary>What is on it, as the clerk would say it.</summary>
        public static string Pitch(Header h)
        {
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
                return ReadHeaderBody(r, v);
            }
            catch (System.Exception e) { why = "UNREADABLE (" + e.GetType().Name + ")"; return null; }
        }

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
