using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// RACE POINTS (owner, 2026-11-16 21:50: "at least one camera ... should
    /// record at each checkpoint. Logically, it proves the cars started at the
    /// starting line, reached each checkpoint, then reached the finish line").
    ///
    /// The small interface the camcorder films through: the START LINE, every
    /// CHECKPOINT in order, the FINISH LINE - each a position, the heading the
    /// race crosses it in, a kind and an index. The cameras stand at these and
    /// nowhere else; the tape is the clips taken at them, in order; the flex
    /// score is what each one saw.
    ///
    /// WHO FEEDS IT
    ///   1. The checkpoint system, when a race is a checkpoint race: set
    ///      <see cref="Provider"/> (or call <see cref="FromGates"/> with its
    ///      gate chain - the fields are the checkpoint file's own: kind, x, y,
    ///      z, hx, hz and the spans' lateral range).
    ///   2. Until then, what exists: the venue's own start and finish plus
    ///      EVENLY SPACED points along its route (city tracks, every ~700 m)
    ///      or SPLIT points (mountain stages and circuits every ~1 km, a drag
    ///      strip's half-way) - <see cref="FromPath"/>.
    ///   3. A tape carries the points it was shot at (Tapes, v2), so it plays
    ///      back as it was shot whatever the build's feed has become.
    /// </summary>
    public struct RacePoint
    {
        public enum Kind : byte { Start = 0, Checkpoint = 1, Finish = 2 }

        public Kind kind;
        /// <summary>The checkpoint's number, 1-based; 0 for the start and the finish.</summary>
        public int index;
        /// <summary>The middle of the line, on the road.</summary>
        public Vector3 pos;
        /// <summary>The way the race crosses it (unit, flat).</summary>
        public Vector3 heading;
        /// <summary>Half the line's width, m (the road's half width).</summary>
        public float halfWidth;
        /// <summary>The station on the race's own path it was planted at, or -1
        /// (a fed gate). Tools only: nothing races by it.</summary>
        public int pathIdx;
        /// <summary>A point this feed invented (evenly spaced, a split): the
        /// camera planner may slide it a little along the road to where
        /// somebody can stand. A real gate never moves.</summary>
        public bool soft;
    }

    public class RaceCourse
    {
        public readonly List<RacePoint> points = new List<RacePoint>();
        /// <summary>A circuit: the start line is the finish line, crossed once
        /// more per lap.</summary>
        public bool loop;
        public int laps = 1;
        /// <summary>"gates" (the checkpoint system), "route" (evenly spaced),
        /// "splits" (a stage or a strip), "tape" (read off a tape).</summary>
        public string source = "";

        public int Checkpoints
        {
            get { int n = 0; foreach (var p in points) if (p.kind == RacePoint.Kind.Checkpoint) n++; return n; }
        }

        public int IndexOf(RacePoint.Kind kind)
        {
            for (int i = 0; i < points.Count; i++) if (points[i].kind == kind) return i;
            return -1;
        }
    }

    public static class RacePoints
    {
        /// <summary>The checkpoint system's feed: answers the course for a
        /// race, or null to leave it to the fallback. Set once at load.</summary>
        public static System.Func<RaceManager, RaceCourse> Provider;

        /// <summary>Metres between the points this feed plants: a city route,
        /// then a stage or a circuit.</summary>
        public const float CityEveryM = 700f, SplitEveryM = 1000f;
        public const int MaxCheckpoints = 16;

        static int fileSwitch = -1;
        /// <summary>Read Resources/CityCheckpoints/&lt;route&gt;.json when no
        /// provider answers (PSX_CAM_CPFILE=1; default OFF until a real file
        /// has been filmed).</summary>
        public static bool FileOn
        {
            get
            {
                if (fileSwitch < 0) fileSwitch = System.Environment.GetEnvironmentVariable("PSX_CAM_CPFILE") == "1" ? 1 : 0;
                return fileSwitch == 1;
            }
        }

        /// <summary>
        /// The course of the race in this scene. <paramref name="grid"/> is
        /// where the field stood at the lights (the recording's first frames)
        /// when it is known: a road with ends starts where its grid is.
        /// </summary>
        public static RaceCourse For(RaceManager rm, IList<Vector3> grid = null)
        {
            if (rm == null || rm.path == null || rm.path.Count < 8) return null;
            RaceCourse c = null;
            if (Provider != null)
            {
                try { c = Provider(rm); }
                catch (System.Exception e) { Debug.LogWarning("[RacePoints] the provider threw: " + e.Message); c = null; }
            }
            if (c == null && FileOn) c = FromFile(rm, grid);
            if (c == null || c.points.Count < 2)
            {
                var def = RaceHUD.VenueDef();
                bool city = def != null && def.IsCityRace;
                c = FromPath(rm.path, rm.Sprint ? rm.sprintFinishIndex : -1, Mathf.Max(1, rm.totalLaps), city, grid);
            }
            return c;
        }

        // ------------------------------------------------------------------
        //  The fallback: from the race's own path (pure - the self-test runs it)
        // ------------------------------------------------------------------
        public static RaceCourse FromPath(TrackPath path, int sprintFinishIndex, int laps, bool city,
                                          IList<Vector3> grid = null)
        {
            var c = new RaceCourse { source = city ? "route" : "splits" };
            int n = path.Count;
            float half = path.roadWidth * 0.5f;
            int startIdx, finishIdx;
            if (path.HasEnds)
            {
                // The start is where the grid's front row is (a strip's cars
                // stand abreast; a stage's stand in file from the first
                // stations), a few metres ahead of it so every car crosses it.
                int front = 1;
                if (grid != null)
                    foreach (var g in grid) front = Mathf.Max(front, path.NearestIndex(g));
                startIdx = Mathf.Clamp(front + Mathf.CeilToInt(6f / Mathf.Max(0.5f, path.spacing)), 1, n - 4);
                finishIdx = path.finishIndex > 0 ? Mathf.Min(path.finishIndex, n - 2) : n - 2;
                c.loop = false; c.laps = 1;
            }
            else if (sprintFinishIndex > 0)
            {
                startIdx = 0; finishIdx = Mathf.Min(sprintFinishIndex, n - 2);
                c.loop = false; c.laps = 1;
            }
            else
            {
                startIdx = 0; finishIdx = 0;
                c.loop = true; c.laps = Mathf.Max(1, laps);
            }

            float lengthM = (c.loop ? n : Mathf.Max(1, finishIdx - startIdx)) * path.spacing;
            int cps;
            if (path.drag) cps = lengthM > 150f ? 1 : 0;                       // the half-way split
            else
            {
                float every = city ? CityEveryM : SplitEveryM;
                cps = Mathf.Clamp(Mathf.RoundToInt(lengthM / every) - 1, lengthM > 500f ? 1 : 0, MaxCheckpoints);
            }

            c.points.Add(At(path, startIdx, RacePoint.Kind.Start, 0, half, false));
            int span = c.loop ? n : finishIdx - startIdx;
            for (int k = 1; k <= cps; k++)
            {
                int idx = startIdx + Mathf.RoundToInt(span * (k / (float)(cps + 1)));
                c.points.Add(At(path, c.loop ? path.Wrap(idx) : idx, RacePoint.Kind.Checkpoint, k, half, true));
            }
            c.points.Add(At(path, finishIdx, RacePoint.Kind.Finish, 0, half, false));
            return c;
        }

        static RacePoint At(TrackPath path, int idx, RacePoint.Kind kind, int index, float half, bool soft)
        {
            Vector3 t = path.GetTangent(idx);
            t.y = 0f;
            t = t.sqrMagnitude > 1e-6f ? t.normalized : Vector3.forward;
            return new RacePoint { kind = kind, index = index, pos = path.GetPoint(idx), heading = t, halfWidth = half, pathIdx = idx, soft = soft };
        }

        // ------------------------------------------------------------------
        //  The checkpoint system's gates, in its file's own shape
        // ------------------------------------------------------------------
        /// <summary>One gate as the checkpoint file writes it (cps[]): the
        /// centre, the heading, and the lateral range its spans cover.</summary>
        [System.Serializable]
        public struct Gate
        {
            public string kind;          // "line" | "turn" | "ramp" | "split" | "finish"
            public float x, y, z;
            public float hx, hz;
            /// <summary>Lateral range along the gate line, m (+ = right of the heading).</summary>
            public float l0, l1;
        }

        /// <summary>
        /// A course from the checkpoint system's chain. A CIRCUIT's gate 0 is
        /// its lap line (start and finish; the other gates are its
        /// checkpoints). A SPRINT's last gate is its finish, and its start is
        /// the grid (not a gate): <paramref name="gridPos"/> and
        /// <paramref name="gridHeading"/>.
        /// </summary>
        public static RaceCourse FromGates(IList<Gate> gates, bool loop, int laps, Vector3 gridPos, Vector3 gridHeading)
        {
            if (gates == null || gates.Count < (loop ? 2 : 1)) return null;
            var c = new RaceCourse { source = "gates", loop = loop, laps = loop ? Mathf.Max(1, laps) : 1 };
            RacePoint P(Gate g, RacePoint.Kind kind, int index)
            {
                var h = new Vector3(g.hx, 0f, g.hz);
                h = h.sqrMagnitude > 1e-6f ? h.normalized : Vector3.forward;
                // The line's middle, when its spans are not centred on it.
                var right = Vector3.Cross(Vector3.up, h);
                float mid = (g.l0 + g.l1) * 0.5f, half = Mathf.Max(3f, Mathf.Abs(g.l1 - g.l0) * 0.5f);
                return new RacePoint
                {
                    kind = kind, index = index, heading = h, halfWidth = half, pathIdx = -1, soft = false,
                    pos = new Vector3(g.x, g.y, g.z) + right * mid,
                };
            }
            if (loop)
            {
                c.points.Add(P(gates[0], RacePoint.Kind.Start, 0));
                for (int i = 1; i < gates.Count; i++) c.points.Add(P(gates[i], RacePoint.Kind.Checkpoint, i));
                c.points.Add(P(gates[0], RacePoint.Kind.Finish, 0));
            }
            else
            {
                gridHeading.y = 0f;
                c.points.Add(new RacePoint
                {
                    kind = RacePoint.Kind.Start, index = 0, pos = gridPos, pathIdx = -1, halfWidth = 6f,
                    heading = gridHeading.sqrMagnitude > 1e-6f ? gridHeading.normalized : Vector3.forward,
                });
                for (int i = 0; i < gates.Count - 1; i++) c.points.Add(P(gates[i], RacePoint.Kind.Checkpoint, i + 1));
                c.points.Add(P(gates[gates.Count - 1], RacePoint.Kind.Finish, 0));
            }
            return c;
        }

        // ---- the checkpoint file itself (Resources/CityCheckpoints/<route>.json) ----
        [System.Serializable] class FileSpan { public float l0 = 0f, l1 = 0f; }
        [System.Serializable]
        class FileCp
        {
            public string kind = "";
            public float x = 0f, y = 0f, z = 0f, hx = 0f, hz = 1f;
            public FileSpan[] spans = null;
        }
        [System.Serializable] class FileChain { public string dir = "fwd"; public int loop = 0; public FileCp[] cps = null; }
        [System.Serializable] class FileRoot { public int version = 0; public string route = ""; public FileChain[] chains = null; }

        /// <summary>The gates of a checkpoint file's text for one direction
        /// ("fwd" / "rev"), or null. Pure.</summary>
        public static List<Gate> ParseFile(string json, bool reversed, out bool loop)
        {
            loop = false;
            if (string.IsNullOrEmpty(json)) return null;
            FileRoot root;
            try { root = JsonUtility.FromJson<FileRoot>(json); }
            catch (System.Exception) { return null; }
            if (root == null || root.chains == null) return null;
            string want = reversed ? "rev" : "fwd";
            foreach (var ch in root.chains)
            {
                if (ch == null || ch.cps == null || ch.dir != want) continue;
                loop = ch.loop != 0;
                var list = new List<Gate>(ch.cps.Length);
                foreach (var cp in ch.cps)
                {
                    float l0 = -6f, l1 = 6f;
                    if (cp.spans != null && cp.spans.Length > 0)
                    {
                        l0 = float.MaxValue; l1 = float.MinValue;
                        foreach (var sp in cp.spans) { l0 = Mathf.Min(l0, Mathf.Min(sp.l0, sp.l1)); l1 = Mathf.Max(l1, Mathf.Max(sp.l0, sp.l1)); }
                    }
                    list.Add(new Gate { kind = cp.kind, x = cp.x, y = cp.y, z = cp.z, hx = cp.hx, hz = cp.hz, l0 = l0, l1 = l1 });
                }
                return list;
            }
            return null;
        }

        static RaceCourse FromFile(RaceManager rm, IList<Vector3> grid)
        {
            var def = RaceHUD.VenueDef();
            if (def == null || !def.IsCityRace || def.IsDeckRun) return null;
            var text = Resources.Load<TextAsset>("CityCheckpoints/" + def.cityRoute);
            if (text == null) return null;
            var gates = ParseFile(text.text, def.Reversed, out bool loop);
            if (gates == null || gates.Count == 0) return null;
            // The gates stand on the suggested route, which is the race's
            // path: one that is not near it is in some other frame (a stale
            // file, another graph) and the whole file is left alone.
            var path = rm.path;
            foreach (var g in gates)
            {
                var p = new Vector3(g.x, g.y, g.z);
                int i = path.NearestIndex(p);
                if ((path.GetPoint(i) - p).sqrMagnitude > 80f * 80f)
                {
                    Debug.LogWarning("[RacePoints] CityCheckpoints/" + def.cityRoute + ": a gate is not on this race's route; the file is ignored");
                    return null;
                }
            }
            Vector3 gp = path.GetPoint(1), gh = path.GetTangent(1);
            if (grid != null && grid.Count > 0)
            {
                int front = 1;
                foreach (var g in grid) front = Mathf.Max(front, path.NearestIndex(g));
                gp = path.GetPoint(front + 2); gh = path.GetTangent(front + 2);
            }
            return FromGates(gates, loop, Mathf.Max(1, rm.totalLaps), gp, gh);
        }
    }
}
