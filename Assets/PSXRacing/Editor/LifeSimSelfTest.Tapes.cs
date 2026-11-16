using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE TAPES (2026-11-16, <see cref="Tapes"/>): the file round-trips a
    /// recording within the quantization, refuses what it cannot read (a
    /// newer version, a cut-off file, something that is not a tape), the
    /// shelf finds what was written and forgets what was sold, and the
    /// clerk's price only ever goes up with a better tape.
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        static void TestTapes()
        {
            Line("tapes:");
            // Two cars, 20 s at 15 Hz, one of them lapping a 60 m circle with
            // every visual state moving.
            const int hz = 15, n = 20 * hz + 1;
            var times = new List<float>();
            var frames = new List<List<RaceReplay.Frame>> { new List<RaceReplay.Frame>(), new List<RaceReplay.Frame>() };
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)hz;
                times.Add(t);
                float a = t * 0.7f;
                for (int c = 0; c < 2; c++)
                {
                    var f = new RaceReplay.Frame
                    {
                        pos = new Vector3(Mathf.Cos(a) * 60f + c * 3f, 410.25f + Mathf.Sin(a * 3f), Mathf.Sin(a) * 60f - 1234.5f),
                        rot = Quaternion.Euler(Mathf.Sin(a) * 4f, -a * Mathf.Rad2Deg, Mathf.Cos(a * 2f) * 3f),
                        lean = Quaternion.Euler(Mathf.Sin(a * 5f) * 2f, 0f, Mathf.Cos(a * 5f) * 3f),
                        rpm = 3000f + 2500f * Mathf.Sin(a * 2f),
                        speedKmh = 120f + 40f * Mathf.Sin(a),
                        forwardSpeed = (120f + 40f * Mathf.Sin(a)) / 3.6f,
                        throttle = 0.5f + 0.5f * Mathf.Sin(a * 3f),
                        brake = Mathf.Clamp01(-Mathf.Sin(a * 3f)),
                        steerDeg = 12f * Mathf.Sin(a * 4f),
                        roll0 = Mathf.DeltaAngle(0f, t * 977f), roll1 = Mathf.DeltaAngle(0f, t * 981f),
                        roll2 = Mathf.DeltaAngle(0f, t * 963f), roll3 = Mathf.DeltaAngle(0f, t * 959f),
                        slipF = 0.05f * Mathf.Sin(a), slipR = 0.08f * Mathf.Sin(a),
                        slideF = 0.3f + 0.3f * Mathf.Sin(a), slideR = 0.6f + 0.5f * Mathf.Sin(a),
                        gear = (sbyte)(1 + k % 5), lap = (byte)(1 + k / 100), place = (byte)(1 + c),
                        flags = (byte)(RaceReplay.FlagGrounded | RaceReplay.FlagOnRoad | (k % 7 == 0 ? RaceReplay.FlagDrifting : 0) | RaceReplay.FlagSeat),
                        hub0 = 0.3f, bodyPos = Vector3.up,
                    };
                    frames[c].Add(f);
                }
            }
            var h = new Tapes.Header
            {
                id = "selftest", venueId = "ChimneyRock", venueName = "Chimney Rock", day = 320, clock = 21.7f,
                hour = TimeOfDay.Night, weather = (int)Weather.Rain, result = "P1 OF 2", finishPos = 1, fieldSize = 2,
                margin = 0.31f, nearMisses = 2, crashes = 1, topKmh = 188f, won = true, playerIndex = 0,
            };
            h.cars.Add(new Tapes.Car { specId = "spec_a", paint = "skin_3", name = "MIDSHIP COUPE", player = true });
            h.cars.Add(new Tapes.Car { specId = "spec_b", paint = "", name = "ROADSTER 99" });

            byte[] bytes;
            using (var ms = new MemoryStream()) { Tapes.Write(ms, h, times, frames); bytes = ms.ToArray(); }
            Check(bytes.Length > 64 && bytes.Length < 2 * n * 57 + 4096, "a 20 s two-car tape is small", bytes.Length + " bytes");

            Tapes.Header back;
            List<float> t2; List<List<RaceReplay.Frame>> f2; string why;
            using (var ms = new MemoryStream(bytes)) back = Tapes.Read(ms, out t2, out f2, out why);
            Check(back != null, "it reads back", why);
            if (back == null) return;
            Check(back.version == Tapes.Version && back.venueId == "ChimneyRock" && back.day == 320 &&
                  Mathf.Abs(back.clock - 21.7f) < 1e-4f && back.hour == TimeOfDay.Night && back.weather == (int)Weather.Rain &&
                  back.result == "P1 OF 2" && back.won && back.nearMisses == 2 && back.crashes == 1 &&
                  Mathf.Abs(back.margin - 0.31f) < 1e-4f && back.cars.Count == 2 && back.cars[0].player &&
                  back.cars[0].paint == "skin_3" && back.cars[1].name == "ROADSTER 99",
                  "the label and the facts come back as written");
            Check(t2.Count == n && f2.Count == 2 && f2[0].Count == n && f2[1].Count == n, "every sample of every car", t2.Count);

            float dPos = 0f, dRot = 0f, dLean = 0f, dSpeed = 0f, dRpm = 0f, dSteer = 0f, dRoll = 0f, dPedal = 0f, dSlide = 0f;
            bool exact = true, seatGone = true;
            for (int c = 0; c < 2; c++)
                for (int k = 0; k < n; k++)
                {
                    var a = frames[c][k]; var b = f2[c][k];
                    dPos = Mathf.Max(dPos, (a.pos - b.pos).magnitude);
                    dRot = Mathf.Max(dRot, Quaternion.Angle(a.rot, b.rot));
                    dLean = Mathf.Max(dLean, Quaternion.Angle(a.lean, b.lean));
                    dSpeed = Mathf.Max(dSpeed, Mathf.Abs(a.speedKmh - b.speedKmh));
                    dRpm = Mathf.Max(dRpm, Mathf.Abs(a.rpm - b.rpm));
                    dSteer = Mathf.Max(dSteer, Mathf.Abs(a.steerDeg - b.steerDeg));
                    dRoll = Mathf.Max(dRoll, Mathf.Abs(Mathf.DeltaAngle(a.roll2, b.roll2)));
                    dPedal = Mathf.Max(dPedal, Mathf.Abs(a.throttle - b.throttle), Mathf.Abs(a.brake - b.brake));
                    dSlide = Mathf.Max(dSlide, Mathf.Abs(a.slideR - b.slideR));
                    if (a.gear != b.gear || a.lap != b.lap || a.place != b.place ||
                        (a.flags & ~RaceReplay.FlagSeat) != b.flags) exact = false;
                    if ((b.flags & RaceReplay.FlagSeat) != 0) seatGone = false;
                    if (Mathf.Abs(t2[k] - times[k]) > 1e-6f) exact = false;
                }
            Check(dPos < 1e-3f, "positions within a millimetre", dPos.ToString("0.00000") + " m");
            Check(dRot < 0.05f && dLean < 0.05f, "the body and its lean within 0.05 degrees",
                  dRot.ToString("0.000") + " / " + dLean.ToString("0.000") + " deg");
            Check(dSpeed <= 0.051f && dRpm <= 0.51f && dSteer <= 0.0051f && dPedal <= 0.0021f && dSlide <= 0.0051f,
                  "speed, revs, steer, pedals and slide within their steps",
                  dSpeed.ToString("0.000") + " km/h, " + dRpm.ToString("0.0") + " rpm, " + dSteer.ToString("0.0000") + " deg");
            Check(dRoll < 0.01f, "the wheels' roll within 0.01 degrees", dRoll.ToString("0.0000"));
            Check(exact, "gear, lap, place, flags and the clock exactly");
            Check(seatGone, "no seat flag on a tape (the body would sit at the origin)");

            // A tape from a newer build is refused by name, not misread.
            var newer = (byte[])bytes.Clone();
            newer[4] = (byte)(Tapes.Version + 1); newer[5] = 0;
            using (var ms = new MemoryStream(newer))
            {
                var r = Tapes.Read(ms, out _, out _, out string w2);
                Check(r == null && w2 != null && w2.Contains("NEWER"), "a newer version is refused", w2);
            }
            // A tape cut short is refused, the header alone still reads.
            var cut = new byte[bytes.Length * 6 / 10];
            System.Array.Copy(bytes, cut, cut.Length);
            using (var ms = new MemoryStream(cut))
            {
                var r = Tapes.Read(ms, out var tc, out _, out string w3);
                Check(r == null && tc == null && w3 != null, "a truncated tape is refused", w3);
            }
            using (var ms = new MemoryStream(cut))
                Check(Tapes.ReadHeader(ms, out _) != null, "but its label still reads (the shelf can show it)");
            using (var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
            {
                var r = Tapes.ReadHeader(ms, out string w4);
                Check(r == null && w4 == "NOT A TAPE", "something that is not a tape is not read", w4);
            }

            // The price only goes up with a better tape.
            var b0 = new Tapes.Header { fieldSize = 4, finishPos = 3, margin = 5f, duration = 90f, topKmh = 150f };
            int p0 = Tapes.Price(b0);
            Tapes.Header Mod(System.Action<Tapes.Header> m)
            {
                var x = new Tapes.Header { fieldSize = b0.fieldSize, finishPos = b0.finishPos, margin = b0.margin,
                                           duration = b0.duration, topKmh = b0.topKmh };
                m(x);
                return x;
            }
            int pWin = Tapes.Price(Mod(x => { x.won = true; x.finishPos = 1; }));
            int pClose = Tapes.Price(Mod(x => x.margin = 0.2f));
            int pMiss = Tapes.Price(Mod(x => x.nearMisses = 3));
            int pCrash = Tapes.Price(Mod(x => x.crashes = 1));
            int pFast = Tapes.Price(Mod(x => x.topKmh = 230f));
            int pShort = Tapes.Price(Mod(x => x.duration = 20f));
            Check(p0 >= 5 && pWin > p0 && pClose > p0 && pMiss > p0 && pCrash > p0 && pFast > p0 && pShort < p0,
                  "a win, a close finish, near misses, a crash and speed each raise the price; a short tape lowers it",
                  "base $" + p0 + ", win $" + pWin + ", close $" + pClose + ", 3 misses $" + pMiss +
                  ", crash $" + pCrash + ", 230 km/h $" + pFast + ", 20 s $" + pShort);
            int pBest = Tapes.Price(Mod(x => { x.won = true; x.finishPos = 1; x.margin = 0.1f; x.nearMisses = 10; x.crashes = 3; x.topKmh = 400f; x.fieldSize = 9; }));
            Check(pBest <= 600, "the best tape there is stays a 1999 price", "$" + pBest);

            // The shelf: write, list, load, sell (a folder of the test's own,
            // never the player's).
            string dir = Path.Combine(Path.GetTempPath(), "psx_tapes_selftest");
            string was = Tapes.DirOverride;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                Tapes.DirOverride = dir;
                Check(Tapes.Shelf().Count == 0 && Tapes.Count() == 0, "an empty shelf is empty");
                File.WriteAllBytes(Path.Combine(dir, "selftest.tape"), bytes);
                File.WriteAllBytes(Path.Combine(dir, "junk.tape"), new byte[] { 9, 9, 9 });
                var shelf = Tapes.Shelf();
                Check(shelf.Count == 1 && shelf[0].venueName == "Chimney Rock", "the shelf lists the tape and skips the junk", shelf.Count);
                if (shelf.Count == 1)
                {
                    var loaded = Tapes.Load(shelf[0].path, out var lt, out var lf, out string w5);
                    Check(loaded != null && lt.Count == n && lf.Count == 2, "a shelved tape loads whole", w5);
                    Check(Tapes.Remove(shelf[0]) && Tapes.Shelf().Count == 0, "a sold tape leaves the shelf");
                }
            }
            finally
            {
                Tapes.DirOverride = was;
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (System.Exception) { }
            }
        }
    }
}
