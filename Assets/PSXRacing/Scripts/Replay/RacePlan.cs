using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// WHERE THE CAMCORDERS STAND (round 2, owner 2026-11-16: "I don't like
    /// the follow cams ... at least one camera ... should record at each
    /// checkpoint"). One or two bystanders at every race point
    /// (<see cref="RacePoints"/>) and nowhere else:
    ///
    ///   FRONT - past the point, beside the road, looking back up it: the
    ///     cars come at the lens through the point.
    ///   BACK  - before the point, on the other side when it can be: takes
    ///     over as the car crosses and watches it go.
    ///
    /// A bystander stands on GROUND that is not road, not water, not a roof,
    /// a wall top or a car, level enough to stand on, with no road further out
    /// (a median, an island), clear of walls and rails, and with the point and
    /// the road each side of it in plain sight. When nowhere near will do, a
    /// LONG LENS from further along the road. A point this feed invented
    /// (RacePoint.soft) slides up to 120 m along the road to where somebody
    /// can stand; a real gate never moves.
    ///
    /// One plan per race scene, shared by the replay's director and by SAVE TO
    /// TAPE (the clips are scored at the points the cameras stood at).
    /// </summary>
    public class RacePlan
    {
        public struct Lens
        {
            public Vector3 pos;
            /// <summary>Stands before the point (the BACK angle).</summary>
            public bool back;
            /// <summary>A long lens from further along the road.</summary>
            public bool far;
            public bool crouch;
        }

        public class Station
        {
            public readonly List<Lens> lenses = new List<Lens>(2);
            /// <summary>The ground was there to judge (a streamed tile was built).</summary>
            public bool judged;
            /// <summary>Metres a soft point was moved along the road.</summary>
            public float slidM;
        }

        public RaceCourse course;
        public Station[] stations;
        TrackPath path;
        System.Func<int, bool> avoid;
        /// <summary>A city: a bystander may stand well back (a sidewalk past a
        /// parking lane, a plaza). On a mountain stage the woods start a few
        /// metres off the road and their leaves have no colliders to test a
        /// view against (round 2's first finish camera stood 23 m into the
        /// trees and filmed leaves): there, the cleared verge only, and a
        /// long lens from further along it when the point has none.</summary>
        bool city;

        public const float EyeM = 1.6f, CrouchM = 1.05f;

        static RacePlan cached;
        static RaceManager cachedRace;
        static RaceCourse cachedTape;

        /// <summary>The plan for the race in this scene (made once).</summary>
        public static RacePlan For(RaceManager rm, RaceReplay rp)
        {
            if (rm == null) return null;
            if (cached != null && cachedRace == rm && cachedTape == null) return cached;
            List<Vector3> grid = null;
            if (rp != null && rp.TrackCount > 0)
            {
                grid = new List<Vector3>();
                for (int i = 0; i < rp.TrackCount; i++)
                {
                    var f = rp.TrackFrames(i);
                    if (f != null && f.Count > 0) grid.Add(f[0].pos);
                }
            }
            var course = RacePoints.For(rm, grid);
            if (course == null) return null;
            var venue = RaceHUD.VenueDef();
            float spacing = rm.path != null ? rm.path.spacing : 4f;
            cached = Make(course, rm.path, idx => TrackCatalog.InTunnel(venue, idx * spacing));
            cachedRace = rm; cachedTape = null;
            return cached;
        }

        /// <summary>The plan for a tape's own course (its points do not move).</summary>
        public static RacePlan ForTape(RaceCourse course, RaceManager rm)
        {
            if (course == null) return null;
            if (cached != null && cachedTape == course && cachedRace == rm) return cached;
            cached = Make(course, rm != null ? rm.path : null, null);
            cachedRace = rm; cachedTape = course;
            return cached;
        }

        static RacePlan Make(RaceCourse course, TrackPath path, System.Func<int, bool> avoid)
        {
            var venue = RaceHUD.VenueDef();
            var plan = new RacePlan
            {
                course = course, path = path, avoid = avoid, stations = new Station[course.points.Count],
                city = venue != null && venue.city,
            };
            for (int i = 0; i < plan.stations.Length; i++) plan.stations[i] = new Station();
            for (int i = 0; i < plan.stations.Length; i++) plan.Ensure(i);
            // A circuit's finish is its start line: the same bystanders.
            return plan;
        }

        /// <summary>Plan point <paramref name="i"/>'s cameras if they are not
        /// planned yet. False while the ground is not there to judge (a city
        /// tile still streaming in): ask again.</summary>
        public bool Ensure(int i)
        {
            if (i < 0 || i >= stations.Length) return true;
            var st = stations[i];
            if (st.judged) return true;
            var p = course.points[i];
            // The same line twice (a circuit's start and finish): one crew.
            for (int k = 0; k < i; k++)
                if (stations[k].judged && (course.points[k].pos - p.pos).sqrMagnitude < 1f)
                {
                    st.lenses.AddRange(stations[k].lenses); st.judged = true;
                    return true;
                }
            if (!GroundThere(p.pos)) return false;
            bool tunnel = avoid != null && p.pathIdx >= 0 && avoid(p.pathIdx);
            if (!tunnel) Place(p, path, st.lenses, city);
            if (st.lenses.Count == 0 && p.soft && p.pathIdx >= 0 && path != null)
            {
                // Slide the point along the road to where somebody can stand.
                foreach (float s in new[] { 40f, -40f, 80f, -80f, 120f, -120f })
                {
                    int idx = p.pathIdx + Mathf.RoundToInt(s / Mathf.Max(0.5f, path.spacing));
                    if (path.HasEnds) { if (idx < 8 || idx > path.Count - 8) continue; }
                    else idx = path.Wrap(idx);
                    if (avoid != null && avoid(idx)) continue;
                    var q = p;
                    q.pathIdx = idx; q.pos = path.GetPoint(idx);
                    Vector3 t = path.GetTangent(idx); t.y = 0f;
                    q.heading = t.sqrMagnitude > 1e-6f ? t.normalized : p.heading;
                    if (!GroundThere(q.pos)) continue;
                    Place(q, path, st.lenses, city);
                    if (st.lenses.Count == 0) continue;
                    course.points[i] = q;
                    st.slidM = s;
                    break;
                }
            }
            st.judged = true;
            return true;
        }

        public int LensCount { get { int n = 0; foreach (var s in stations) n += s.lenses.Count; return n; } }
        public int Judged { get { int n = 0; foreach (var s in stations) if (s.judged) n++; return n; } }

        /// <summary>
        /// DURING THE RACE (the recorder calls this a couple of times a
        /// second): a streamed city has ground only round the cars, so a
        /// point's bystanders are found when a car first comes within 150 m
        /// of it - the tiles there are built then. By the flag every point
        /// the field reached has its cameras, and SAVE TO TAPE scores at the
        /// points as they finally stand.
        /// </summary>
        public static void Scout(RaceManager rm, RaceReplay rp)
        {
            if (rm == null || rp == null || rp.TrackCount == 0) return;
            var plan = For(rm, rp);
            if (plan == null) return;
            for (int i = 0; i < plan.stations.Length; i++)
            {
                if (plan.stations[i].judged) continue;
                Vector3 p = plan.course.points[i].pos;
                for (int c = 0; c < rp.TrackCount; c++)
                {
                    var car = rp.TrackCar(c);
                    if (car == null || (car.transform.position - p).sqrMagnitude > 150f * 150f) continue;
                    plan.Ensure(i);
                    break;
                }
            }
        }

        /// <summary>The ground is there at a point and under its cameras (a
        /// streamed tile is built): the tape may cut to it.</summary>
        public bool GroundReady(int i)
        {
            if (i < 0 || i >= stations.Length) return true;
            if (!GroundThere(course.points[i].pos)) return false;
            foreach (var l in stations[i].lenses)
                if (!Physics.Raycast(l.pos + Vector3.up * 0.3f, Vector3.down, 8f, SolidMask, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        // ------------------------------------------------------------------
        //  Placing one point's bystanders
        // ------------------------------------------------------------------
        static int SolidMask => ~((1 << 2) | (1 << 4) | (1 << LayerMask.NameToLayer("Foliage")));
        static int GroundMask => ~((1 << 2) | (1 << LayerMask.NameToLayer("Foliage")));
        const int RoadLayer = 8, WaterLayer = 4;

        static bool GroundThere(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 6f, Vector3.down, 20f, SolidMask, QueryTriggerInteraction.Ignore);

        /// <summary>The road's middle and heading <paramref name="s"/> metres
        /// along from the point (its own path when it stands on one).</summary>
        static void Along(RacePoint p, TrackPath path, float s, out Vector3 pos, out Vector3 heading)
        {
            if (path != null && p.pathIdx >= 0)
            {
                int idx = p.pathIdx + Mathf.RoundToInt(s / Mathf.Max(0.5f, path.spacing));
                idx = path.HasEnds ? Mathf.Clamp(idx, 0, path.Count - 1) : path.Wrap(idx);
                pos = path.GetPoint(idx);
                heading = path.GetTangent(idx); heading.y = 0f;
                heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : p.heading;
                return;
            }
            pos = p.pos + p.heading * s;
            heading = p.heading;
        }

        static readonly float[] CityLaterals = { 2.5f, 4.5f, 7.5f, 11f, 16f, 24f };
        static readonly float[] VergeLaterals = { 2.5f, 3.5f, 4.5f, 6f, 8f };
        static readonly float[] FrontAlong = { 24f, 34f, 15f, 46f };
        static readonly float[] BackAlong = { -22f, -32f, -14f, -44f };
        static readonly float[] FarAlong = { 62f, -60f, 90f, -88f, 125f, -120f, 160f };

        /// <summary>The bystanders for one point, best first: FRONT, then BACK.</summary>
        public static void Place(RacePoint p, TrackPath path, List<Lens> into, bool city = true)
        {
            into.Clear();
            var laterals = city ? CityLaterals : VergeLaterals;
            // The outside of the bend the road makes through the point.
            Along(p, path, -30f, out _, out var hIn);
            Along(p, path, 30f, out _, out var hOut);
            float turn = Vector3.SignedAngle(hIn, hOut, Vector3.up);
            int outside = turn < -4f ? 1 : turn > 4f ? -1 : ((p.index & 1) == 0 ? 1 : -1);
            bool crouch = (p.index % 3) == 2;

            Along(p, path, -45f, out var before, out _);
            Along(p, path, 45f, out var after, out _);
            int frontSide = 0;
            if (Find(p, path, FrontAlong, laterals, outside, false, before, crouch, out var front, out frontSide)) into.Add(front);
            // The other side for the second angle when it can be; else the same.
            int backFirst = frontSide != 0 ? -frontSide : outside;
            if (Find(p, path, BackAlong, laterals, backFirst, true, after, !crouch && (p.index % 2) == 1, out var back, out _)) into.Add(back);
            if (into.Count == 0)
            {
                // A long lens from further along: the point alone in sight.
                if (Find(p, path, FarAlong, laterals, outside, false, p.pos, false, out var far, out _))
                {
                    far.far = true;
                    far.back = Vector3.Dot(far.pos - p.pos, p.heading) < 0f;
                    into.Add(far);
                }
            }
        }

        static bool Find(RacePoint p, TrackPath path, float[] alongs, float[] laterals, int firstSide, bool back, Vector3 alsoSee,
                         bool crouch, out Lens lens, out int side)
        {
            lens = default; side = 0;
            Vector3 seeGate = p.pos + Vector3.up * 1.1f, seeAlso = alsoSee + Vector3.up * 1.1f;
            foreach (float s in alongs)
            {
                Along(p, path, s, out var c, out var h);
                Vector3 r = Vector3.Cross(Vector3.up, h);
                foreach (float lat in laterals)
                    for (int k = 0; k < 2; k++)
                    {
                        int sd = k == 0 ? firstSide : -firstSide;
                        Vector3 at = c + r * sd * (p.halfWidth + lat);
                        if (!Stand(at, c.y, r * sd, crouch, out var eye)) continue;
                        if (Blocked(eye, seeGate) || Blocked(eye, seeAlso)) continue;
                        // Nothing standing in front of the lens either (run 2's city
                        // tape had a utility pole across a third of one frame): a
                        // clear few metres toward the point, toward the road beside
                        // the lens, and toward the far end of its view.
                        if (Crowded(eye, seeGate) || Crowded(eye, c + Vector3.up * 1.1f) || Crowded(eye, seeAlso)) continue;
                        if (ClearViewOn && Fanned(eye, seeGate)) continue;
                        lens = new Lens { pos = eye, back = back, crouch = crouch };
                        side = sd;
                        return true;
                    }
            }
            return false;
        }

        /// <summary>Can somebody stand at this spot (see the class summary)?
        /// <paramref name="eye"/> is where the lens would be.</summary>
        public static bool Stand(Vector3 at, float roadY, Vector3 outward, bool crouch, out Vector3 eye)
        {
            eye = default;
            if (!Ground(at, roadY, out float y, out bool road, out bool ok) || road || !ok) return false;
            // Road further out as well: a median between two carriageways, an
            // island in a junction - nobody stands there.
            foreach (float o in new[] { 2.5f, 6f })
            {
                Vector3 q = at + outward * o;
                if (Ground(q, roadY, out _, out bool r2, out _) && r2) return false;
            }
            eye = new Vector3(at.x, y + (crouch ? CrouchM : EyeM), at.z);
            // Room for a person: nothing solid within an arm's length of the lens.
            if (Physics.CheckSphere(eye, 0.45f, SolidMask, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// <summary>The first thing under the sky at a spot. False when there
        /// is nothing at all. <paramref name="ok"/>: ground a person can
        /// stand on near the road's level - not water, not a car, not steep,
        /// not a roof or a tall wall top.</summary>
        static bool Ground(Vector3 at, float roadY, out float y, out bool road, out bool ok)
        {
            y = 0f; road = false; ok = false;
            if (!Physics.Raycast(new Vector3(at.x, roadY + 800f, at.z), Vector3.down, out var hit, 1000f,
                                 GroundMask, QueryTriggerInteraction.Ignore))
                return false;
            y = hit.point.y;
            int layer = hit.collider.gameObject.layer;
            road = layer == RoadLayer && Mathf.Abs(y - roadY) < 6f;
            ok = layer != RoadLayer && layer != WaterLayer && hit.rigidbody == null && hit.normal.y > 0.75f &&
                 y > roadY - 3f && y < roadY + 3.5f;
            return true;
        }

        /// <summary>Something solid between a lens and a point on the road, or
        /// a tree right in front of the lens.</summary>
        public static bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return false;
            if (Physics.Raycast(from, d / len, len - 2.5f, SolidMask, QueryTriggerInteraction.Ignore)) return true;
            int foliage = LayerMask.NameToLayer("Foliage");
            return foliage >= 0 && Physics.Raycast(from, d / len, Mathf.Min(7f, len - 2.5f), 1 << foliage,
                                                   QueryTriggerInteraction.Ignore);
        }

        /// <summary>Something solid (a pole, a sign, a rail's end) within a few
        /// metres of the lens on the way to a point: a fat ray, so a thing
        /// beside the sight line counts too.</summary>
        static bool Crowded(Vector3 from, Vector3 to)
        {
            // Level with the lens: a pole or a sign post counts, the low wall
            // or barrier it looks over does not.
            Vector3 d = to - from;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 1.5f) return false;
            return Physics.SphereCast(from, 0.35f, d / len, out _, Mathf.Min(7f, len - 1.5f), SolidMask, QueryTriggerInteraction.Ignore);
        }

        static int clearView = -1;
        /// <summary>PSX_CAM_CLEARVIEW=1 (default OFF: written after the last
        /// allowed test run, never played): a wider look for things standing
        /// in the lens's foreground. Uptown Loop's CHECKPOINT 3 front camera
        /// has a utility pole 15 degrees off its line to the point, which the
        /// three fat rays above pass beside.</summary>
        public static bool ClearViewOn
        {
            get
            {
                if (clearView < 0) clearView = System.Environment.GetEnvironmentVariable("PSX_CAM_CLEARVIEW") == "1" ? 1 : 0;
                return clearView == 1;
            }
        }

        /// <summary>Anything solid within 6 m of the lens, level with it, in a
        /// 60 degree fan round the line to a point.</summary>
        static bool Fanned(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            if (d.sqrMagnitude < 4f) return false;
            d.Normalize();
            foreach (float a in new[] { -30f, -20f, -10f, 10f, 20f, 30f })
                if (Physics.SphereCast(from, 0.2f, Quaternion.Euler(0f, a, 0f) * d, out _, 6f, SolidMask, QueryTriggerInteraction.Ignore)) return true;
            return false;
        }

        /// <summary>Is the ground under a spot road (for the checks)?</summary>
        public static bool RoadUnder(Vector3 at) => Ground(at, at.y, out _, out bool road, out _) && road;
    }
}
