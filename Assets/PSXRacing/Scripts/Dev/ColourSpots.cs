using System.Collections.Generic;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE COLOUR PROTOCOL'S PLACES - one definition for the editor's frames
    /// (Editor\ColourShots.cs) and the player's (ShotLink.cs), so a pair of
    /// frames from the two is the same place, eye and measuring boxes.
    /// The colour pass, 2026-09-29 (scratchpad PLAN.md, "Measurement protocol").
    ///
    /// A spot says where the car stands; <see cref="Eye"/> is the chase view at
    /// rest every look tool uses (8 m back, 2.6 m up, looking 20 m past the
    /// car - psx_hour_*, nl_*, dl_*), so these frames sit beside those; and
    /// <see cref="RegionTable"/> are the boxes the numbers are read in, placed
    /// in the world round the car and PROJECTED into the frame (the sidecar
    /// carries both), so a box lands on the same road at any aspect ratio:
    /// the beam axis at 22-58 m, "just outside" the beam (15 m ahead, 2 m past
    /// its 34 degree edge), the road beside, ahead of and behind the car.
    /// </summary>
    public static class ColourSpots
    {
        public const float ChaseBack = 8f, ChaseUp = 2.6f, LookUp = 0.8f, LookAhead = 20f;
        /// <summary>Vertical FOV of every protocol frame: the scene camera's
        /// serialized 58 (ChaseCamera.baseFOV), set explicitly on both paths
        /// so the player's chase rig cannot move it.</summary>
        public const float Fov = 58f;
        /// <summary>Lift over the road for a placed car (the grid lift).</summary>
        public const float CarLift = 0.35f;

        public class Spot
        {
            public string id;
            /// <summary>TrackCatalog id whose scene is opened.</summary>
            public string venue;
            /// <summary>grid: the scene's own car pose. path: the TrackPath at
            /// <see cref="alongM"/>. cityDeck: the middle third of the named
            /// street's longest run on structure. cityUnder: the road that
            /// passes under the named street, 30 m short of it.</summary>
            public string how;
            public string street;
            public float alongM;
            /// <summary>cityDeck: +1 faces along the edge's points, -1 against.</summary>
            public int dir = 1;
            public string note;
        }

        public static readonly Spot[] Protocol =
        {
            // The owner's own view (his frames 4 and 5, 2026-09-28): found by
            // the explore set (ColourShots) - 230 m along the Samuel Street
            // edge, facing back toward its deck, the brick block ahead right.
            new Spot { id = "S1",  venue = "Charlotte", how = "cityDeck", street = "Samuel Street", alongM = 230f, dir = -1,
                       note = "the owner's Samuel Street view (his frames 4 and 5): concrete road between parapets" },
            new Spot { id = "S1d", venue = "Charlotte", how = "cityDeck", street = "Samuel Street",
                       note = "on the Samuel Street deck itself (30% into the structure)" },
            new Spot { id = "S1u", venue = "Charlotte", how = "cityUnder", street = "Samuel Street",
                       note = "an underpass: the road under the Samuel Street deck, else under the nearest deck to it" },
            new Spot { id = "S2",  venue = "BlowingRock", how = "grid", note = "Blowing Rock at its start: the snow stretch" },
            new Spot { id = "S3",  venue = "BlueRidge", how = "grid", note = "a mountain stage with no lamps (nl_blueridge_chase)" },
            new Spot { id = "CC",  venue = "CityCircuit", how = "grid", note = "the circuit's grid: the psx_hour_* view (S4 at dusk)" },
            new Spot { id = "T1",  venue = "LittleSwitzerland", how = "path", alongM = 3962f, note = "30 m short of the tunnel (3992-4180 m), looking in" },
            new Spot { id = "T2",  venue = "LittleSwitzerland", how = "path", alongM = 4086f, note = "inside the tunnel bore" },
        };

        public static Spot Find(string id)
        {
            foreach (var s in Protocol) if (string.Equals(s.id, id, System.StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>A calendar day in <paramref name="season"/> (the first
        /// one), so a frame can wear that season's dress: the dress is the
        /// day's season unless it snows (Seasons.DressIndex).</summary>
        public static int DayIn(Season season)
        {
            for (int d = 1; d <= 400; d++) if (Seasons.Of(d) == season) return d;
            return 0;
        }

        // ------------------------------------------------------------------
        //  Where the car stands
        // ------------------------------------------------------------------

        /// <summary>
        /// The car's pose for a spot, in the scene that is loaded now.
        /// <paramref name="scenePlayer"/> is the player car's transform as the
        /// scene placed it (the "grid" pose). False, with the reason, when the
        /// scene does not have what the spot needs.
        /// </summary>
        public static bool Pose(Spot s, Transform scenePlayer, out Vector3 pos, out Quaternion rot, out string info)
        {
            pos = Vector3.zero; rot = Quaternion.identity; info = "";
            switch (s.how)
            {
                case "grid":
                    if (scenePlayer == null) { info = "no player car in the scene"; return false; }
                    pos = scenePlayer.position; rot = scenePlayer.rotation;
                    info = "the scene's own car pose";
                    return true;
                case "path":
                {
                    var path = Object.FindAnyObjectByType<TrackPath>();
                    if (path == null || path.Count == 0) { info = "no TrackPath"; return false; }
                    int i = path.Wrap(Mathf.RoundToInt(s.alongM / path.spacing));
                    Vector3 fwd = path.GetTangent(i).normalized;
                    Vector3 flat = new Vector3(fwd.x, 0f, fwd.z).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
                    // The right-hand lane of a two-lane road.
                    pos = path.GetPoint(i) + right * (path.roadWidth * 0.25f) + Vector3.up * CarLift;
                    rot = Quaternion.LookRotation(fwd, Vector3.up);
                    info = $"waypoint {i} ({i * path.spacing:0} m), road {path.roadWidth:0.0} m";
                    return true;
                }
                case "cityDeck":
                case "cityUnder":
                    return CityPose(s, out pos, out rot, out info);
            }
            info = "unknown spot kind " + s.how;
            return false;
        }

        static bool CityPose(Spot s, out Vector3 pos, out Quaternion rot, out string info)
        {
            pos = Vector3.zero; rot = Quaternion.identity; info = "";
            var map = CityMap.Get();
            if (map == null) { info = "charlotte_city.bytes is missing"; return false; }
            if (!Deck(map, s.street, out var best, out float bestA, out float bestB))
            { info = "no '" + s.street + "' edge on structure"; return false; }
            if (s.how == "cityDeck")
            {
                // alongM: metres along the deck's edge (0 = 30% into the deck).
                float sAt = s.alongM != 0f ? Mathf.Clamp(s.alongM, 1f, best.length - 1f) : bestA + (bestB - bestA) * 0.3f;
                int dir = s.dir < 0 ? -1 : 1;
                if (best.oneway) dir = 1;
                Stand(best, sAt, dir, out pos, out rot);
                info = $"'{best.name}' edge {best.index} deck {bestA:0}-{bestB:0} m of {best.length:0} m, car at {sAt:0} m facing {(dir > 0 ? "along" : "against")} the edge, " +
                       $"cls {best.cls}, {best.lanes} lanes, width {best.width:0.0} m{(best.oneway ? ", one-way" : "")}";
                return true;
            }
            // cityUnder: the road passing under the street - or, when nothing
            // does (a deck over a railway or a creek), under the nearest
            // deck to it that a road passes under.
            Vector2 deckMid = best.PointAt((bestA + bestB) * 0.5f);
            int pick = -1, pickDir = 1; float pickD = float.MaxValue, pickS = 0f, pickX = 0f;
            for (int ci = 0; ci < map.crossings.Length; ci++)
            {
                var c = map.crossings[ci];
                if (c.over < 0 || c.over >= map.edges.Length || c.under < 0 || c.under >= map.edges.Length) continue;
                var ov = map.edges[c.over];
                var e = map.edges[c.under];
                if (ov == null || e == null || e.pts == null || e.pts.Length < 2) continue;
                float d = ov.name == s.street ? -1f : (c.at - deckMid).magnitude;
                if (d >= pickD) continue;
                // Where along the under edge the crossing is, and a 30 m run-up.
                float sx = 0f, dBest = float.MaxValue;
                for (float at = 0f; at <= e.length; at += 0.5f)
                {
                    float q = (e.PointAt(at) - c.at).sqrMagnitude;
                    if (q < dBest) { dBest = q; sx = at; }
                }
                int dir = +1; float sAt = sx - 30f;
                if (sAt < 2f)
                {
                    if (e.oneway) continue;
                    dir = -1; sAt = sx + 30f;
                    if (sAt > e.length - 2f) continue;
                }
                pickD = d; pick = ci; pickDir = dir; pickS = sAt; pickX = sx;
            }
            if (pick >= 0)
            {
                var c = map.crossings[pick];
                var over = map.edges[c.over];
                var e = map.edges[c.under];
                Stand(e, pickS, pickDir, out pos, out rot);
                info = $"under '{over.name}' (edge {over.index}{(over.name == s.street ? "" : $", the nearest deck to {s.street}: {pickD:0} m")}) " +
                       $"on '{e.name}' edge {e.index} (cls {e.cls}), crossing at {pickX:0} m, car at {pickS:0} m facing {(pickDir > 0 ? "along" : "against")} the edge";
                return true;
            }
            info = "no crossing in the map";
            return false;
        }

        /// <summary>The street's longest run on structure: its edge and the
        /// run's two ends in metres along it.</summary>
        public static bool Deck(CityMap map, string street, out CityMap.Edge best, out float bestA, out float bestB)
        {
            best = null; bestA = 0f; bestB = 0f;
            foreach (var e in map.edges)
            {
                if (e == null || e.name != street || e.pts == null || e.pts.Length < 2) continue;
                float a = -1f;
                for (float at = 0f; at <= e.length; at += 1f)
                {
                    bool el = e.ElevatedAt(at);
                    if (el && a < 0f) a = at;
                    if ((!el || at + 1f > e.length) && a >= 0f)
                    {
                        float b = el ? at : at - 1f;
                        if (b - a > bestB - bestA) { best = e; bestA = a; bestB = b; }
                        a = -1f;
                    }
                }
            }
            return best != null;
        }

        /// <summary>Where a car stands on a city edge: the right-hand lane of
        /// a two-way street, the middle of a one-way one, pitched with the
        /// road (NightLookShots.Stand, plus a direction).</summary>
        public static void Stand(CityMap.Edge e, float s, int dir, out Vector3 pos, out Quaternion rot)
        {
            Vector2 p = e.PointAt(s), t2 = e.TangentAt(s) * dir;
            // Behind and ahead of the car in the direction it faces.
            float sa = Mathf.Clamp(s - 3f * dir, 0f, e.length), sb = Mathf.Clamp(s + 3f * dir, 0f, e.length);
            float run = Mathf.Abs(sb - sa);
            float rise = run > 0f ? (e.YAt(sb) - e.YAt(sa)) / run : 0f;
            Vector3 fwd = new Vector3(t2.x, rise, t2.y).normalized;
            Vector3 right = new Vector3(t2.y, 0f, -t2.x);
            float lateral = e.oneway ? 0f : e.width * 0.25f;
            pos = new Vector3(p.x, e.YAt(s) + CarLift, p.y) + right * lateral;
            rot = Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>The chase view at rest (the look tools' eye).</summary>
        public static void Eye(Vector3 pos, Quaternion rot, out Vector3 eye, out Quaternion look)
        {
            Vector3 fwd = rot * Vector3.forward;
            eye = pos - fwd * ChaseBack + Vector3.up * ChaseUp;
            look = Quaternion.LookRotation(pos + Vector3.up * LookUp + fwd * LookAhead - eye);
        }

        // ------------------------------------------------------------------
        //  Where the numbers are read
        // ------------------------------------------------------------------

        public struct Region
        {
            public string name;
            /// <summary>Car-local metres on the road: x right, z forward.</summary>
            public float x, z;
            /// <summary>Half extents on the road, metres (across, along).</summary>
            public float hx, hz;
            public string kind;
            public Region(string name, float x, float z, float hx, float hz, string kind)
            { this.name = name; this.x = x; this.z = z; this.hx = hx; this.hz = hz; this.kind = kind; }
        }

        /// <summary>The beam's outer half-angle (CarLights.BeamOuterDeg).</summary>
        public const float BeamEdgeDeg = 34f;

        public static readonly Region[] RegionTable =
        {
            new Region("road_ahead_14", 0f, 14f, 0.8f, 1.5f, "road"),
            new Region("road_left_4", -2.6f, 4f, 0.6f, 1.2f, "road"),
            new Region("road_right_4", 2.6f, 4f, 0.6f, 1.2f, "road"),
            new Region("beam_22", 0f, 22f, 0.8f, 2f, "beam"),
            new Region("beam_31", 0f, 31f, 0.8f, 2f, "beam"),
            new Region("beam_40", 0f, 40f, 0.9f, 2.5f, "beam"),
            new Region("beam_49", 0f, 49f, 1.0f, 3f, "beam"),
            new Region("beam_58", 0f, 58f, 1.1f, 3.5f, "beam"),
            // 15 m ahead, 2 m past the beam's 34 degree edge (the "just
            // outside" spot of the targets, inside the planned spill lobe).
            new Region("outside_L", -(15f * Mathf.Tan(BeamEdgeDeg * Mathf.Deg2Rad) + 2f), 15f, 0.6f, 1.2f, "outside"),
            new Region("outside_R", 15f * Mathf.Tan(BeamEdgeDeg * Mathf.Deg2Rad) + 2f, 15f, 0.6f, 1.2f, "outside"),
            // The road within 3 m behind the car (the tail lamps' wash).
            new Region("tail_2", 0f, -3.9f, 0.6f, 0.6f, "tail"),
            new Region("verge_L", -8f, 12f, 1f, 1.5f, "verge"),
            new Region("verge_R", 8f, 12f, 1f, 1.5f, "verge"),
        };

        public class RegionHit
        {
            public string name, kind, hit;
            public Vector3 world;
            public bool onGround, visible;
            /// <summary>All four corners landed on the centre's surface
            /// within 0.5 m of its height: the box is one flat patch of
            /// that surface, not a patch that hangs over a deck edge onto
            /// the ground below (which projects huge).</summary>
            public bool clean;
            public string occluder;
            /// <summary>Normalised image box, origin TOP-left: x0, y0, x1, y1.</summary>
            public float x0, y0, x1, y1;
            public bool inFrame;
        }

        /// <summary>
        /// Every region of <see cref="RegionTable"/> for a car at
        /// <paramref name="pos"/>/<paramref name="rot"/>, seen by
        /// <paramref name="cam"/> as it stands NOW (pose, FOV, aspect set by
        /// the caller): each box's corners are dropped onto whatever surface
        /// is under them (the car itself ignored) and projected. Plus two
        /// screen-fixed boxes: the sky high in the middle, the car's roof.
        /// </summary>
        public static List<RegionHit> Project(Camera cam, Vector3 pos, Quaternion rot, Transform car, float aspect)
        {
            var list = new List<RegionHit>();
            // A car just teleported (and, in edit mode, anything moved at
            // all) is not in the physics scene until this.
            Physics.SyncTransforms();
            Vector3 fwd = rot * Vector3.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
            float roadY = pos.y - CarLift;
            float slope = fwd.y / Mathf.Max(0.2f, new Vector2(fwd.x, fwd.z).magnitude);
            foreach (var r in RegionTable)
            {
                var h = new RegionHit { name = r.name, kind = r.kind };
                Vector3 c = Ground(pos + right * r.x + flat * r.z, roadY + slope * r.z, car, out h.hit, out h.onGround);
                h.world = c;
                float minx = 1f, miny = 1f, maxx = 0f, maxy = 0f;
                int inFront = 0;
                h.clean = h.onGround;
                for (int k = 0; k < 4; k++)
                {
                    float sx = (k & 1) == 0 ? -r.hx : r.hx, sz = (k & 2) == 0 ? -r.hz : r.hz;
                    Vector3 w = Ground(pos + right * (r.x + sx) + flat * (r.z + sz), roadY + slope * (r.z + sz), car, out string cw, out bool cg);
                    if (!cg || cw != h.hit || Mathf.Abs(w.y - c.y) > 0.5f) h.clean = false;
                    Vector3 v = cam.WorldToViewportPoint(w);
                    if (v.z <= 0.05f) continue;
                    inFront++;
                    minx = Mathf.Min(minx, v.x); maxx = Mathf.Max(maxx, v.x);
                    miny = Mathf.Min(miny, 1f - v.y); maxy = Mathf.Max(maxy, 1f - v.y);
                }
                h.x0 = Mathf.Clamp01(minx); h.x1 = Mathf.Clamp01(maxx);
                h.y0 = Mathf.Clamp01(miny); h.y1 = Mathf.Clamp01(maxy);
                h.inFrame = inFront == 4 && h.x1 > h.x0 && h.y1 > h.y0 && maxx > 0f && minx < 1f && maxy > 0f && miny < 1f;
                // Seen, or behind something (the car, a wall, a parapet)?
                h.visible = true; h.occluder = "";
                Vector3 eye = cam.transform.position;
                var hits = Physics.RaycastAll(eye, (c - eye).normalized, Vector3.Distance(eye, c) - 0.15f);
                foreach (var hh in hits)
                {
                    if (hh.collider == null) continue;
                    if (hh.collider.isTrigger) continue;
                    h.visible = false;
                    h.occluder = hh.collider.name + (car != null && hh.collider.transform.IsChildOf(car) ? " (the car)" : "");
                    break;
                }
                list.Add(h);
            }
            // The sky: a fixed box high in the middle of the frame.
            list.Add(new RegionHit { name = "sky_top", kind = "sky", x0 = 0.40f, x1 = 0.60f, y0 = 0.03f, y1 = 0.12f, inFrame = true, visible = true, clean = true, occluder = "", hit = "screen" });
            // The car's roof, projected.
            Vector3 roof = pos - Vector3.up * CarLift + Vector3.up * 1.3f - flat * 0.3f;
            Vector3 vr = cam.WorldToViewportPoint(roof);
            float rw = 0.025f, rh = 0.018f;
            list.Add(new RegionHit
            {
                name = "car_roof", kind = "car", world = roof, hit = "projected",
                x0 = Mathf.Clamp01(vr.x - rw), x1 = Mathf.Clamp01(vr.x + rw),
                y0 = Mathf.Clamp01(1f - vr.y - rh), y1 = Mathf.Clamp01(1f - vr.y + rh),
                inFrame = vr.z > 0f, visible = true, clean = true, occluder = "",
            });
            return list;
        }

        /// <summary>The surface under a point, as seen from above: the
        /// HIGHEST hit of a ray cast down through it within 3 m below and
        /// 1.5 m above the expected road height, the car ignored - so a deck
        /// or a tunnel roof overhead is not taken for the road, and the road
        /// ribbon wins over the terrain mesh a few centimetres under it (the
        /// nearest-to-expected rule picked the terrain on the stages).</summary>
        static Vector3 Ground(Vector3 p, float expectY, Transform car, out string what, out bool onGround)
        {
            what = "none"; onGround = false;
            var from = new Vector3(p.x, expectY + 6f, p.z);
            var hits = Physics.RaycastAll(from, Vector3.down, 14f);
            float top = float.MinValue; Vector3 at = new Vector3(p.x, expectY, p.z);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                if (car != null && h.collider.transform.IsChildOf(car)) continue;
                float dy = h.point.y - expectY;
                if (dy < -3f || dy > 1.5f) continue;
                if (h.point.y > top) { top = h.point.y; at = h.point; what = h.collider.name; onGround = true; }
            }
            return at;
        }

        /// <summary>The sidecar's "regions" entry.</summary>
        public static List<object> ToSidecar(List<RegionHit> hits)
        {
            var list = new List<object>();
            foreach (var h in hits)
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = h.name, ["kind"] = h.kind,
                    ["box"] = new List<object> { h.x0, h.y0, h.x1, h.y1 },
                    ["world"] = h.world, ["surface"] = h.hit ?? "", ["onGround"] = h.onGround,
                    ["inFrame"] = h.inFrame, ["visible"] = h.visible, ["occluder"] = h.occluder ?? "", ["clean"] = h.clean,
                });
            return list;
        }
    }
}
