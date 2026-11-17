using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CAMCORDER (2026-11-16; round 2: cameras at the race's points, the
    /// tape as their clips, the flex score). The parts that need no scene:
    /// the OSD's words, the race points fed from a path / from the checkpoint
    /// system's gates / from its file's text, the clips of a synthetic drive,
    /// what a clip's camera saw (a slide, a crash, a lead, a near miss, air),
    /// and the v2 tape (points and clips ride on it; a v1 tape still loads).
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestCamcorder()
        {
            Line("camcorder:");
            Check(Camcorder.DateStamp(320) == "NOV 16 1999", "the stamp's date is the career's, camcorder order", Camcorder.DateStamp(320));
            Check(Camcorder.TimeStamp(21.7f) == "9:42 PM", "and its clock", Camcorder.TimeStamp(21.7f));
            Check(Camcorder.TimeStamp(0.25f) == "12:15 AM" && Camcorder.TimeStamp(12f) == "12:00 PM", "midnight and noon read as a camcorder reads them",
                  Camcorder.TimeStamp(0.25f) + " / " + Camcorder.TimeStamp(12f));
            Check(Camcorder.Counter(83.9f) == "0:01:23" && Camcorder.Counter(3725f) == "1:02:05", "the tape counter", Camcorder.Counter(3725f));

            var tgo = new GameObject("CamcorderRing");
            var sgo = new GameObject("CamcorderStage");
            try
            {
                // ---- race points off a path ----
                const float R = 636.6198f;   // a 4 km ring of 4 m stations
                var ring = tgo.AddComponent<TrackPath>();
                int n = 1000;
                ring.waypoints = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    ring.waypoints[i] = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                }
                ring.curvatures = new float[n];
                ring.spacing = 4f; ring.roadWidth = 10f;

                var city = RacePoints.FromPath(ring, -1, 3, true);
                Check(city.loop && city.laps == 3 && city.source == "route", "a circuit's course is a loop with its laps", city.laps + " " + city.source);
                Check(city.points.Count >= 2 && city.points[0].kind == RacePoint.Kind.Start &&
                      city.points[city.points.Count - 1].kind == RacePoint.Kind.Finish &&
                      (city.points[0].pos - city.points[city.points.Count - 1].pos).magnitude < 0.01f,
                      "it starts and finishes on the one line");
                Check(city.Checkpoints == 5, "a 4 km city lap gets a checkpoint every ~700 m", city.Checkpoints);
                bool ordered = true, even = true, numbered = true;
                for (int i = 1; i <= city.Checkpoints && i < city.points.Count; i++)
                {
                    var p = city.points[i];
                    if (p.kind != RacePoint.Kind.Checkpoint || p.index != i || !p.soft) numbered = false;
                    if (p.pathIdx <= city.points[i - 1].pathIdx) ordered = false;
                    if (Mathf.Abs((p.pathIdx - city.points[i - 1].pathIdx) - n / 6f) > 1.5f) even = false;
                }
                Check(ordered && even && numbered, "numbered in route order, evenly spaced, free to slide", ordered + "/" + even + "/" + numbered);
                Check(Mathf.Abs(Vector3.Dot(city.points[1].heading, ring.GetTangent(city.points[1].pathIdx).normalized) - 1f) < 1e-3f &&
                      Mathf.Abs(city.points[1].halfWidth - 5f) < 1e-3f, "each with the road's heading and half width");
                var splits = RacePoints.FromPath(ring, -1, 2, false);
                Check(splits.Checkpoints == 3 && splits.source == "splits", "a 4 km circuit gets a split every ~1 km", splits.Checkpoints);
                var sprint = RacePoints.FromPath(ring, 400, 3, true);
                Check(!sprint.loop && sprint.laps == 1 && sprint.points[sprint.points.Count - 1].pathIdx == 400 && sprint.Checkpoints == 1,
                      "a sprint on a loop ends at its own finish", sprint.points[sprint.points.Count - 1].pathIdx + ", " + sprint.Checkpoints + " cp");

                var stage = sgo.AddComponent<TrackPath>();
                stage.waypoints = new Vector3[600];
                for (int i = 0; i < 600; i++) stage.waypoints[i] = new Vector3(0f, 0f, i * 4f);
                stage.curvatures = new float[600];
                stage.spacing = 4f; stage.roadWidth = 8f; stage.pointToPoint = true; stage.finishIndex = 560;
                var grid = new List<Vector3> { new Vector3(-2f, 0f, 8f), new Vector3(2f, 0f, 20f) };
                var st = RacePoints.FromPath(stage, -1, 1, false, grid);
                Check(!st.loop && st.points[0].pathIdx == 7 && st.points[st.points.Count - 1].pathIdx == 560,
                      "a stage starts just ahead of its grid and ends at its traps", st.points[0].pathIdx + " .. " + st.points[st.points.Count - 1].pathIdx);
                Check(st.Checkpoints == 1, "with its split points between", st.Checkpoints);
                stage.pointToPoint = false; stage.drag = true; stage.finishIndex = 100;
                var strip = RacePoints.FromPath(stage, -1, 1, false, grid);
                Check(strip.Checkpoints == 1 && Mathf.Abs(strip.points[1].pathIdx - (7 + 100) / 2f) <= 1f,
                      "a strip has one, half way", strip.points[1].pathIdx);

                // ---- the checkpoint system's gates, and its file's text ----
                var gates = new List<RacePoints.Gate>
                {
                    new RacePoints.Gate { kind = "line", x = 0f, y = 1f, z = 0f, hx = 0f, hz = 1f, l0 = -6f, l1 = 6f },
                    new RacePoints.Gate { kind = "turn", x = 0f, y = 1f, z = 400f, hx = 1f, hz = 0f, l0 = -4f, l1 = 12f },
                    new RacePoints.Gate { kind = "split", x = 400f, y = 1f, z = 400f, hx = 0f, hz = -1f, l0 = -6f, l1 = 6f },
                };
                var gl = RacePoints.FromGates(gates, true, 2, Vector3.zero, Vector3.forward);
                Check(gl != null && gl.loop && gl.laps == 2 && gl.points.Count == 4 && gl.Checkpoints == 2 &&
                      gl.points[0].kind == RacePoint.Kind.Start && gl.points[3].kind == RacePoint.Kind.Finish &&
                      (gl.points[0].pos - gl.points[3].pos).magnitude < 0.01f,
                      "a circuit's gate 0 is its start and its finish; the rest are its checkpoints", gl != null ? gl.points.Count : 0);
                // A span that is not centred moves the line's middle (+ = right of the heading).
                Check(gl != null && Mathf.Abs(gl.points[1].halfWidth - 8f) < 1e-3f && Mathf.Abs(gl.points[1].pos.z - 396f) < 1e-3f,
                      "a gate's spans give its width and its middle", gl != null ? gl.points[1].halfWidth + " at z " + gl.points[1].pos.z : "");
                var gs = RacePoints.FromGates(gates, false, 1, new Vector3(0f, 1f, -50f), Vector3.forward);
                Check(gs != null && !gs.loop && gs.points.Count == 4 && gs.Checkpoints == 2 && gs.points[0].pos.z == -50f &&
                      gs.points[3].kind == RacePoint.Kind.Finish && gs.points[3].pos.x == 400f,
                      "a sprint starts at its grid and its last gate is the finish");
                const string json = "{\"version\":1,\"graph\":\"b2aa3453\",\"route\":\"test\",\"chains\":[" +
                    "{\"dir\":\"fwd\",\"loop\":1,\"cps\":[" +
                    "{\"kind\":\"line\",\"routeM\":0,\"x\":1,\"y\":2,\"z\":3,\"hx\":0,\"hz\":1,\"depth\":6,\"spans\":[{\"e\":1,\"s\":2,\"y\":2,\"l0\":-12.4,\"l1\":9.8}]}," +
                    "{\"kind\":\"turn\",\"x\":10,\"y\":2,\"z\":300,\"hx\":1,\"hz\":0,\"spans\":[{\"l0\":-5,\"l1\":5},{\"l0\":8,\"l1\":20}]}]}," +
                    "{\"dir\":\"rev\",\"loop\":1,\"cps\":[{\"kind\":\"line\",\"x\":1,\"y\":2,\"z\":3,\"hx\":0,\"hz\":-1}]}]}";
                var fwd = RacePoints.ParseFile(json, false, out bool fl);
                var rev = RacePoints.ParseFile(json, true, out bool rl);
                Check(fwd != null && fwd.Count == 2 && fl && fwd[0].kind == "line" && Mathf.Abs(fwd[0].l0 + 12.4f) < 1e-3f && Mathf.Abs(fwd[0].l1 - 9.8f) < 1e-3f &&
                      fwd[1].z == 300f && fwd[1].l0 == -5f && fwd[1].l1 == 20f,
                      "the checkpoint file's text reads in its own shape (kind, x y z, hx hz, spans)", fwd != null ? fwd.Count : -1);
                Check(rev != null && rev.Count == 1 && rl && rev[0].hz == -1f && rev[0].l0 == -6f, "a reverse twin reads its own chain; no spans = a lane each side");
                Check(RacePoints.ParseFile("not json", false, out _) == null && RacePoints.ParseFile("{}", false, out _) == null, "junk is no course");

                // ---- the clips of a synthetic drive round the ring ----
                // 3 s on the grid 20 m behind the line, then 40 m/s for two laps and a bit.
                const float V = 40f, Hz = 30f;
                var course = RacePoints.FromPath(ring, -1, 2, true);
                MakeDrive(R, V, Hz, 3f, 2f * 4000f + 20f + 160f, -20f, out var times, out var frames);
                var clips = TapeClips.Plan(course, times, frames);
                int K = course.Checkpoints;
                Check(clips.Count == 1 + 2 * (K + 1), "a clip at the start, at every checkpoint of every lap, at the line each lap", clips.Count + " for " + K + " checkpoints x 2 laps");
                if (clips.Count != 1 + 2 * (K + 1)) return;
                bool kinds = clips[0].kind == TapeClip.Kind.Start && clips[clips.Count - 1].kind == TapeClip.Kind.Finish;
                bool inOrder = true, noOverlap = true, window = true, lapsOk = true;
                for (int i = 1; i < clips.Count; i++)
                {
                    var c = clips[i];
                    if (c.tIn < clips[i - 1].tOut - 1e-3f) noOverlap = false;
                    if (c.tPass <= clips[i - 1].tPass) inOrder = false;
                    // 150 m in, 70 m out at 40 m/s (a 30 Hz sample either side).
                    if (i < clips.Count - 1 && (Mathf.Abs((c.tPass - c.tIn) - 150f / V) > 0.15f || Mathf.Abs((c.tOut - c.tPass) - 70f / V) > 0.15f)) window = false;
                    int wantLap = (i - 1) / (K + 1) + 1;
                    if (c.kind == TapeClip.Kind.Checkpoint && c.lap != wantLap) lapsOk = false;
                }
                int mid = 1 + K;   // the line after lap 1
                Check(kinds && inOrder && noOverlap, "start first, finish last, in the order driven, never two at once", kinds + "/" + inOrder + "/" + noOverlap);
                Check(window, "each picks the car up 150 m out and lets it go 70 m past");
                Check(lapsOk && clips[mid].kind == TapeClip.Kind.Lap && clips[mid].lap == 2, "the line between laps is LAP 2, the checkpoints know their lap");
                Check(Mathf.Abs(clips[clips.Count - 1].tOut - times[times.Count - 1]) < 1e-3f, "the finish clip runs to the end of the tape");
                // The car is at the point at tPass (the first checkpoint: a share of a lap + the 20 m to the line).
                float wantPass = 3f + (20f + 4000f / (K + 1)) / V;
                Check(Mathf.Abs(clips[1].tPass - wantPass) < 0.1f, "a clip's pass is when the car is at its point", clips[1].tPass + " vs " + wantPass);
                Check(TapeClips.Label(clips[0], K, 2) == "START" && TapeClips.Label(clips[2], K, 2) == "CHECKPOINT 2/" + K &&
                      TapeClips.Label(clips[mid], K, 2) == "LAP 2/2" && TapeClips.Label(clips[clips.Count - 1], K, 2) == "FINISH",
                      "the OSD names them START, CHECKPOINT 2/" + K + ", LAP 2/2, FINISH", TapeClips.Label(clips[2], K, 2));
                Check(TapeClips.At(clips, clips[2].tPass) == 2 && TapeClips.At(clips, (clips[2].tOut + clips[3].tIn) * 0.5f) == -1, "between two clips the tape is in neither");
                // A car that stops after the first checkpoint: the tape ends there.
                MakeDrive(R, V, Hz, 3f, 20f + 4000f / (K + 1) + 100f, -20f, out var t2, out var f2);
                var shortClips = TapeClips.Plan(course, t2, f2);
                Check(shortClips.Count == 2 && shortClips[1].kind == TapeClip.Kind.Checkpoint, "a car that never reached checkpoint 2 has no clip of it, and no finish", shortClips.Count);

                // ---- what a clip's camera saw ----
                var clean = clips[2];
                TapeClips.Score(ref clean, times, frames, null, 4, null);
                Check(clean.tags == 0 && Mathf.Abs(clean.kmh - V * 3.6f) < 1f && clean.value == Mathf.RoundToInt((V * 3.6f - 60f) * 0.12f),
                      "a clean pass is its speed and nothing else", TapeClips.Tags(clean) + " $" + clean.value);
                // The same pass with the tail out 25 degrees for a second round the point.
                var slid = Yawed(frames, times, clips[2].tPass - 0.5f, clips[2].tPass + 0.5f, 25f);
                var slide = clips[2];
                TapeClips.Score(ref slide, times, slid, null, 4, null);
                Check(slide.Has(TapeClip.Slide) && slide.value > clean.value + 10 && Mathf.Abs(slide.slideSec - 1f) < 0.15f && Mathf.Abs(slide.slideDeg - 25f) < 2f,
                      "a slide held through the point is seen and is worth more", TapeClips.Tags(slide) + " $" + slide.value + " vs $" + clean.value);
                var longer = clips[2];
                TapeClips.Score(ref longer, times, Yawed(frames, times, clips[2].tPass - 1f, clips[2].tPass + 1f, 40f), null, 4, null);
                Check(longer.value > slide.value, "a longer, wider slide is worth more again", "$" + longer.value + " vs $" + slide.value);
                // Sideways outside the window: the camera did not see it.
                var unseen = clips[2];
                TapeClips.Score(ref unseen, times, Yawed(frames, times, clips[2].tOut + 1f, clips[2].tOut + 2f, 25f), null, 4, null);
                Check(unseen.value == clean.value && unseen.tags == 0, "a slide between two points is on nobody's tape");
                // A spin (backwards to the way it is going) loses value.
                var spun = clips[2];
                TapeClips.Score(ref spun, times, Yawed(frames, times, clips[2].tPass - 0.3f, clips[2].tPass + 0.3f, 170f), null, 4, null);
                Check(spun.Has(TapeClip.Spin) && spun.value < clean.value, "a spin in view loses value", TapeClips.Tags(spun) + " $" + spun.value);
                // A crash: 70 km/h gone in a fifth of a second, in view.
                var crash = clips[2];
                TapeClips.Score(ref crash, times, Crashed(frames, times, clips[2].tPass - 0.5f), null, 4, null);
                Check(crash.Has(TapeClip.Crash) && crash.value < clean.value, "a crash in view loses value", TapeClips.Tags(crash) + " $" + crash.value);
                // Leading, a pass, door to door.
                var lead = Placed(frames, times, 0f, 9999f, 1);
                var led = clips[2];
                TapeClips.Score(ref led, times, lead, null, 4, null);
                Check(led.Has(TapeClip.Led) && led.value == clean.value + 10, "leading through the point is seen", TapeClips.Tags(led));
                var solo = clips[2];
                TapeClips.Score(ref solo, times, lead, null, 1, null);
                Check(!solo.Has(TapeClip.Led), "nobody leads a solo run");
                var gained = Placed(Placed(frames, times, 0f, 9999f, 3), times, clips[2].tPass, 9999f, 2);
                var pass = clips[2];
                TapeClips.Score(ref pass, times, gained, null, 4, null);
                Check(pass.Has(TapeClip.Pass) && pass.value == clean.value + 20, "a place gained in view is a pass", TapeClips.Tags(pass));
                var door = clips[2];
                TapeClips.Score(ref door, times, frames, new List<IReadOnlyList<RaceReplay.Frame>> { Shifted(frames, 2.2f) }, 4, null);
                Check(door.Has(TapeClip.Door) && door.value == clean.value + 12, "a rival alongside is door to door", TapeClips.Tags(door));
                // A near miss: one traffic car standing a door's width off the line at the point.
                Vector3 pp = course.points[clips[2].point].pos, ph = course.points[clips[2].point].heading;
                Vector3 parked = pp + Vector3.Cross(Vector3.up, ph) * 2f;
                var miss = clips[2];
                TapeClips.Score(ref miss, times, frames, null, 4, (k, into) => { into.Clear(); into.Add(parked); return true; });
                Check(miss.Has(TapeClip.NearMiss) && !miss.Has(TapeClip.NearMiss2) && miss.value == clean.value + 15, "a traffic car missed by a door's width is a near miss", TapeClips.Tags(miss));
                // Air: wheels off the ground for a third of a second in view.
                var air = clips[2];
                TapeClips.Score(ref air, times, Flagged(frames, times, clips[2].tPass, clips[2].tPass + 0.35f, RaceReplay.FlagGrounded, false), null, 4, null);
                Check(air.Has(TapeClip.Air) && air.value == clean.value + 20, "wheels off the ground is air", TapeClips.Tags(air));
                Check(TapeClips.Line(slide, 2).StartsWith("L1 CP 2: ") && TapeClips.Line(slide, 2).Contains("SLIDE") && TapeClips.Line(clean, 1).StartsWith("CP 2: "),
                      "the row reads CP 2: <speed> · SLIDE", TapeClips.Line(slide, 2));

                // ---- the v2 tape: the points and the clips ride on it ----
                var h = new Tapes.Header { id = "selftest", venueId = "Ring", venueName = "Ring", day = 320, fieldSize = 4, finishPos = 2 };
                h.cars.Add(new Tapes.Car { specId = "a", name = "A", player = true });
                h.course = course;
                var scored = new List<TapeClip>();
                for (int i = 0; i < clips.Count; i++)
                {
                    var c = clips[i];
                    TapeClips.Score(ref c, times, i == 2 ? slid : frames, null, 4, null);
                    scored.Add(c);
                }
                h.clips = scored;
                int sum = Tapes.ResultValue(h);
                foreach (var c in scored) sum += c.value;
                Check(Tapes.Price(h) == Mathf.Max(5, sum), "a clip tape's price is its clips plus the result", "$" + Tapes.Price(h));
                var cleanTape = new Tapes.Header { fieldSize = 4, finishPos = 2, course = course, clips = new List<TapeClip>() };
                for (int i = 0; i < clips.Count; i++) { var c = clips[i]; TapeClips.Score(ref c, times, frames, null, 4, null); cleanTape.clips.Add(c); }
                Check(Tapes.Price(h) > Tapes.Price(cleanTape), "the tape with the slide on it sells for more than the clean one", "$" + Tapes.Price(h) + " vs $" + Tapes.Price(cleanTape));
                Check(Tapes.BestLine(h).Contains("SLIDE"), "its best clip is the slide", Tapes.BestLine(h));
                var tapeTimes = new List<float>();
                var tapeFrames = new List<List<RaceReplay.Frame>> { new List<RaceReplay.Frame>() };
                for (int i = 0; i < times.Count; i += 2) { tapeTimes.Add(times[i]); tapeFrames[0].Add(frames[i]); }
                byte[] bytes;
                using (var ms = new MemoryStream()) { Tapes.Write(ms, h, tapeTimes, tapeFrames); bytes = ms.ToArray(); }
                Tapes.Header back;
                using (var ms = new MemoryStream(bytes)) back = Tapes.Read(ms, out _, out _, out _);
                bool same = back != null && back.course != null && back.course.points.Count == course.points.Count &&
                            back.course.laps == 2 && back.course.loop && back.clips.Count == scored.Count;
                if (same)
                    for (int i = 0; i < scored.Count; i++)
                    {
                        var a = scored[i]; var b = back.clips[i];
                        if (a.kind != b.kind || a.point != b.point || a.index != b.index || a.lap != b.lap || a.tags != b.tags || a.value != b.value ||
                            Mathf.Abs(a.tIn - b.tIn) > 1e-4f || Mathf.Abs(a.tPass - b.tPass) > 1e-4f || Mathf.Abs(a.tOut - b.tOut) > 1e-4f || Mathf.Abs(a.kmh - b.kmh) > 1e-3f) same = false;
                    }
                if (same)
                    for (int i = 0; i < course.points.Count; i++)
                    {
                        var a = course.points[i]; var b = back.course.points[i];
                        if (a.kind != b.kind || a.index != b.index || (a.pos - b.pos).magnitude > 1e-3f || (a.heading - b.heading).magnitude > 1e-3f) same = false;
                    }
                Check(same && Tapes.Price(back) == Tapes.Price(h), "points, clips, tags and price come back off the tape", back != null ? "$" + Tapes.Price(back) : "unreadable");
                // The clips a tape's own points give its own (15 Hz) samples: the same race.
                var again = back != null && back.course != null ? TapeClips.Plan(back.course, tapeTimes, tapeFrames[0]) : new List<TapeClip>();
                bool sameRace = again.Count == scored.Count;
                if (sameRace) for (int i = 0; i < again.Count; i++) if (again[i].kind != scored[i].kind || Mathf.Abs(again[i].tPass - scored[i].tPass) > 0.1f) sameRace = false;
                Check(sameRace, "played back, the tape's own points cut the same clips", again.Count);
                // A v1 tape (no points, no clips, the version word 1) still loads and keeps the old price.
                var old = new Tapes.Header { id = "old", venueId = "Ring", venueName = "Ring", day = 320, fieldSize = 4, finishPos = 1, won = true, topKmh = 200f, duration = 60f };
                old.cars.Add(new Tapes.Car { specId = "a", name = "A", player = true });
                byte[] ob;
                using (var ms = new MemoryStream()) { Tapes.Write(ms, old, tapeTimes, tapeFrames); ob = ms.ToArray(); }
                ob[4] = 1; ob[5] = 0;
                Tapes.Header oldBack;
                List<List<RaceReplay.Frame>> of;
                using (var ms = new MemoryStream(ob)) oldBack = Tapes.Read(ms, out _, out of, out _);
                Check(oldBack != null && oldBack.course == null && oldBack.clips.Count == 0 && of != null && of[0].Count == tapeTimes.Count && Tapes.Price(oldBack) > 5,
                      "a v1 tape still loads, with no clips and its old price", oldBack != null ? "$" + Tapes.Price(oldBack) : "unreadable");
                ob[4] = 9;
                using (var ms = new MemoryStream(ob))
                {
                    var none = Tapes.ReadHeader(ms, out string newer);
                    Check(none == null && newer != null && newer.Contains("NEWER"), "a tape from a newer build is refused by name", newer);
                }
            }
            finally { Object.DestroyImmediate(tgo); Object.DestroyImmediate(sgo); }
        }

        /// <summary>A car on the ring: <paramref name="wait"/> seconds standing
        /// <paramref name="s0"/> metres from the line, then a steady speed for
        /// <paramref name="metres"/>.</summary>
        static void MakeDrive(float R, float v, float hz, float wait, float metres, float s0,
                              out List<float> times, out List<RaceReplay.Frame> frames)
        {
            times = new List<float>(); frames = new List<RaceReplay.Frame>();
            float total = wait + metres / v;
            for (int k = 0; k / hz <= total; k++)
            {
                float t = k / hz;
                float s = s0 + Mathf.Max(0f, t - wait) * v;
                float a = s / R;
                var pos = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                var tan = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                times.Add(t);
                frames.Add(new RaceReplay.Frame
                {
                    pos = pos, rot = Quaternion.LookRotation(tan, Vector3.up),
                    speedKmh = t > wait ? v * 3.6f : 0f, forwardSpeed = t > wait ? v : 0f,
                    flags = (byte)(RaceReplay.FlagGrounded | RaceReplay.FlagOnRoad), place = 2, lap = 1,
                });
            }
        }

        static List<RaceReplay.Frame> Yawed(List<RaceReplay.Frame> f, List<float> t, float t0, float t1, float deg)
        {
            var o = new List<RaceReplay.Frame>(f);
            for (int i = 0; i < o.Count; i++)
                if (t[i] >= t0 && t[i] <= t1) { var x = o[i]; x.rot = x.rot * Quaternion.Euler(0f, deg, 0f); o[i] = x; }
            return o;
        }

        static List<RaceReplay.Frame> Crashed(List<RaceReplay.Frame> f, List<float> t, float at)
        {
            var o = new List<RaceReplay.Frame>(f);
            for (int i = 0; i < o.Count; i++)
                if (t[i] >= at && t[i] < at + 1.5f) { var x = o[i]; x.speedKmh = Mathf.Max(0f, x.speedKmh - 70f * Mathf.Clamp01((t[i] - at) / 0.2f)); o[i] = x; }
            return o;
        }

        static List<RaceReplay.Frame> Placed(List<RaceReplay.Frame> f, List<float> t, float t0, float t1, byte place)
        {
            var o = new List<RaceReplay.Frame>(f);
            for (int i = 0; i < o.Count; i++)
                if (t[i] >= t0 && t[i] <= t1) { var x = o[i]; x.place = place; o[i] = x; }
            return o;
        }

        static List<RaceReplay.Frame> Flagged(List<RaceReplay.Frame> f, List<float> t, float t0, float t1, byte flag, bool on)
        {
            var o = new List<RaceReplay.Frame>(f);
            for (int i = 0; i < o.Count; i++)
                if (t[i] >= t0 && t[i] <= t1)
                {
                    var x = o[i];
                    x.flags = on ? (byte)(x.flags | flag) : (byte)(x.flags & ~flag);
                    o[i] = x;
                }
            return o;
        }

        /// <summary>The same drive a little to the car's right: a rival alongside.</summary>
        static List<RaceReplay.Frame> Shifted(List<RaceReplay.Frame> f, float right)
        {
            var o = new List<RaceReplay.Frame>(f);
            for (int i = 0; i < o.Count; i++) { var x = o[i]; x.pos += x.rot * Vector3.right * right; o[i] = x; }
            return o;
        }
    }
}
