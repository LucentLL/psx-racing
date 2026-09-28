using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CHASE CAMERA'S FRAMING, MEASURED. (2026-09-28, the NFS U / MW pass.)
    ///
    /// "Adjust the camera angles to be more like NFS Underground and Most
    /// Wanted while maintaining real car and road dimensions." Whether a car
    /// "fills a quarter of the frame" or "sits on the bottom edge" is a number,
    /// and a number read off a screenshot by eye is how the chase view came to
    /// be judged at rest for months while the car was a third smaller in every
    /// frame anybody actually drove (the follow lag parked the lens 1.2 m
    /// further back above 22 km/h, and the speed rig added 0.9 m and 8 deg).
    ///
    /// So this stands each test car on a straight of a built venue, puts the
    /// scene's own camera exactly where the game would hold it in the STEADY
    /// STATE at 0, 100 and 200 km/h (speed rig, FOV pull and the follow lag's
    /// steady trail included), at 16:9 and at a 19.5:9 phone, and COMPUTES —
    /// by projecting the car's own body and wheel meshes through that camera,
    /// not by looking at pixels — the car's width share of the frame, its
    /// rear-axle contact line, silhouette bottom and roof, the horizon, the
    /// camera's height / pitch / distance off the tail, and how much of the
    /// car's silhouette lies under the HUD cluster on that screen (the dials,
    /// the gear panel, and on a phone the touch wheel and pedal column, laid
    /// out by the same arithmetic GaugeCluster and TouchControls use).
    ///
    /// Every number is checked against the NFS U / U2 / MW reference bands
    /// (see Targets) and the run ends with "[CamFrame] ALL IN TARGET" or
    /// "[CamFrame] FAIL n". It also writes a PNG of every case with the HUD
    /// regions, the silhouette's box, the horizon and the contact line drawn
    /// over it, and one contact sheet per view and screen.
    ///
    /// PSX_CAMFRAME_RIG = game (default: ChaseCamera.SteadyPose, the code the
    /// game runs) or legacy (the rig every scene carried on 2026-09-28, kept
    /// so the before/after can be re-measured). PSX_CAMFRAME_VENUE picks the
    /// venue (default GillespieGap: two real 3.2 m lanes). PSX_CAMFRAME_PNG=0
    /// skips the pictures.
    ///
    ///   tools\camframe-probe.ps1 [-Rig legacy]  ->  Screenshots\CamFrame\
    ///
    /// NOT -nographics: the PNGs render through the pipeline.
    /// </summary>
    public static class CamFrameProbe
    {
        /// <summary>The narrow 911, the reference FD, the wide Charger, the
        /// widest saloon (Crown Vic, traffic), a tall playable van and the
        /// tallest, widest traffic shell (Transit).</summary>
        static readonly string[] CarKeys =
            { "flatsix_coupe", "rx7_fd", "charger_69", "crown_victoria", "classic_van", "ford_transit" };
        static readonly float[] SpeedsKmh = { 0f, 100f, 200f };
        static readonly ChaseCamera.View[] Views = { ChaseCamera.View.Chase, ChaseCamera.View.Close };

        struct ScreenDef { public string tag; public float aspect; public bool touch, info; }
        static readonly ScreenDef[] MainScreens =
        {
            new ScreenDef { tag = "16x9", aspect = 16f / 9f, touch = false },
            // The owner's phone (2000x923 is 19.5:9) with the touch panel up.
            new ScreenDef { tag = "19.5x9", aspect = 19.5f / 9f, touch = true },
        };
        /// <summary>PSX_CAMFRAME_SCREENS=all: other phones and a touch tablet,
        /// measured and reported but not part of the verdict.</summary>
        static readonly ScreenDef[] InfoScreens =
        {
            new ScreenDef { tag = "18x9", aspect = 2f, touch = true, info = true },
            new ScreenDef { tag = "20x9", aspect = 20f / 9f, touch = true, info = true },
            new ScreenDef { tag = "16x9t", aspect = 16f / 9f, touch = true, info = true },
        };
        static ScreenDef[] Screens = MainScreens;

        /// <summary>The FD's width, which the reference W-shares are quoted at.</summary>
        const float RefWidthM = 1.76f;
        const int FrameLines = 480;

        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                                             "Screenshots", "CamFrame");

        // ------------------------------------------------------------------
        //  One car, measured
        // ------------------------------------------------------------------
        class CarGeo
        {
            public string key;
            public float widthM, tailZ, noseZ, roofY, rearAxleZ, meshBottom, boxTailZ;
            public Vector3 boxCenter, boxSize;
            public CarModelDef def;
            public float wheelbase;
            /// <summary>World-space vertices and triangles of the body and the
            /// four wheels, for the pose the car is standing in now.</summary>
            public List<Vector3> verts = new List<Vector3>();
            public List<int> tris = new List<int>();
            public List<Vector3> localVerts = new List<Vector3>();
        }

        struct Pose
        {
            public Vector3 pos;
            public Quaternion rot;
            public float vfov, near;
            /// <summary>Lens shift, fraction of the frame width (+ = right).</summary>
            public float shift;
        }

        class Row
        {
            public string rig, screen, view, car;
            public float kmh, aspect;
            public float back, height, pitch, vfov, hfov;
            public float w, wTarget, xH, contact, bottom, roof, horizon, shift;
            /// <summary>Road visible over the car from this many metres ahead
            /// of its nose (999 = the car hides the road).</summary>
            public float road;
            public int carCells, clusterCells, otherCells;
            public string clusterHit = "", otherHit = "";
            public List<string> fails = new List<string>();
            public bool tall, info;
        }

        static readonly List<Row> rows = new List<Row>();

        [MenuItem("PSX Racing/Camera Framing Probe")]
        public static void Run()
        {
            rows.Clear();
            string rig = Env("PSX_CAMFRAME_RIG", "game").ToLowerInvariant();
            string venue = Env("PSX_CAMFRAME_VENUE", "GillespieGap");
            bool pngs = Env("PSX_CAMFRAME_PNG", "1") != "0";
            if (Env("PSX_CAMFRAME_SCREENS", "") == "all")
            {
                var all = new List<ScreenDef>(MainScreens);
                all.AddRange(InfoScreens);
                Screens = all.ToArray();
            }
            else Screens = MainScreens;
            Directory.CreateDirectory(OutDir);

            TrackCatalog.TrackDef def = null;
            foreach (var d in TrackCatalog.All) if (d.id == venue) { def = d; break; }
            if (def == null) { Debug.LogError("[CamFrame] FAIL no venue " + venue); return; }
            if (!PSXScreenshotTool.Open(def, out var cam, out var player))
            {
                Debug.LogError("[CamFrame] FAIL could not open " + venue + " (run the scene build)");
                return;
            }
            LogSceneRig(cam);

            // Nothing on the framebuffer but the world: the HUD is drawn as
            // outlines from its own layout arithmetic instead, so the phone
            // frames show the PHONE's HUD and not the PC one this scene bakes.
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (c.isRootCanvas) c.enabled = false;

            var path = Object.FindFirstObjectByType<TrackPath>();
            if (path == null || !FindStraight(path, out Vector3 at, out Vector3 fwd, out string where))
            {
                Debug.LogError("[CamFrame] FAIL no straight found on " + venue);
                return;
            }
            Debug.Log("[CamFrame] venue " + venue + ", " + where + ", rig " + rig);

            var body = player.GetComponent<CarBody>();
            var car = player.GetComponent<CarController>();
            if (body == null || car == null) { Debug.LogError("[CamFrame] FAIL player has no CarBody"); return; }

            if (Env("PSX_CAMFRAME_HULLS", "") == "all") DumpAllShells(player, body, car, at, fwd);
            // The chase rig's per-shell silhouette table: written from the
            // meshes on -Emit, and always checked against them.
            int tableFails = CheckSilhouettes(player, body, car, at, fwd, Env("PSX_CAMFRAME_EMIT", "") == "1");

            var hulls = new StringBuilder();
            hulls.Append("{\n");
            bool firstHull = true;
            float keepFov = cam.fieldOfView, keepNear = cam.nearClipPlane;
            var sheets = new Dictionary<string, List<(Texture2D tex, int rowI, int colI)>>();

            for (int ci = 0; ci < CarKeys.Length; ci++)
            {
                string key = CarKeys[ci];
                var shell = CarModelLibrary.Load(key);
                if (shell == null) { Debug.LogError("[CamFrame] FAIL no shell " + key); continue; }
                body.widthMm = 0; // the model's reference car
                body.Apply(shell, 0);
                player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
                SeatWheels(car);
                var geo = Measure(key, player, body, car);
                if (geo == null) continue;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[CamFrame] car {0}: width {1:0.000} m, tail z {2:0.00} (box face {3:0.00}), nose {4:0.00}, roof {5:0.00} (def {6:0.00}), rear axle z {7:0.00}, mesh bottom {8:0.00}, box c {9} s {10}",
                    key, geo.widthM, geo.tailZ, geo.boxTailZ, geo.noseZ, geo.roofY,
                    shell.roofY, geo.rearAxleZ, geo.meshBottom, geo.boxCenter.ToString("0.000"), geo.boxSize.ToString("0.000")));
                AppendHull(hulls, geo, ref firstHull);

                foreach (var scr in Screens)
                    foreach (var v in Views)
                        for (int si = 0; si < SpeedsKmh.Length; si++)
                        {
                            float kmh = SpeedsKmh[si];
                            var pose = PoseFor(rig, v, scr, kmh / 3.6f, player);
                            var row = Evaluate(rig, scr, v, kmh, geo, pose, player.transform);
                            rows.Add(row);
                            if (!pngs) continue;
                            var tex = RenderFrame(cam, pose, scr, geo, row);
                            if (tex == null) continue;
                            string name = string.Format("cf_{0}_{1}_{2}_{3}_{4:000}", rig, v.ToString().ToLower(),
                                                        scr.tag, key, kmh);
                            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                            string sheetKey = rig + "_" + v.ToString().ToLower() + "_" + scr.tag;
                            if (!sheets.TryGetValue(sheetKey, out var list))
                                sheets[sheetKey] = list = new List<(Texture2D, int, int)>();
                            list.Add((tex, ci, si));
                        }
            }
            hulls.Append("\n}\n");
            File.WriteAllText(Path.Combine(OutDir, "camframe_hulls.json"), hulls.ToString());

            foreach (var kv in sheets) WriteSheet(kv.Key, kv.Value);
            cam.fieldOfView = keepFov;
            cam.nearClipPlane = keepNear;
            cam.ResetAspect();
            cam.ResetProjectionMatrix();

            int fails = WriteReport(rig, venue, where) + tableFails;
            Debug.Log(fails == 0 ? "[CamFrame] ALL IN TARGET (" + rows.Count + " cases, rig " + rig + ")"
                                 : "[CamFrame] FAIL " + fails + " case(s) out of target (rig " + rig + ")");
            Debug.Log("[CamFrame] done");
        }

        static string Env(string name, string fallback)
        {
            string s = System.Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
        }

        /// <summary>Whatever the baked scene carries on its ChaseCamera, read
        /// by NAME so this compiles whichever fields exist: the stale-scene trap
        /// (psx-racing-stale-scene-values) is a baked value outvoting the code.</summary>
        static void LogSceneRig(Camera cam)
        {
            var chase = cam.GetComponent<ChaseCamera>();
            if (chase == null) { Debug.Log("[CamFrame] scene camera has no ChaseCamera"); return; }
            var so = new SerializedObject(chase);
            var sb = new StringBuilder("[CamFrame] scene ChaseCamera:");
            foreach (string f in new[] { "distance", "height", "lookHeight", "baseFOV", "speedFOV", "speedPullBack",
                                         "speedDrop", "speedLookAhead", "lagClampM", "positionLag", "speedFullMps",
                                         "rotationLag", "rotationLagDrift" })
            {
                var p = so.FindProperty(f);
                if (p != null) sb.Append(' ').Append(f).Append(' ').Append(p.floatValue.ToString("0.###", CultureInfo.InvariantCulture));
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        //  Where the car stands
        // ------------------------------------------------------------------
        /// <summary>The straightest, flattest window of the path: 30 m behind
        /// the car to 160 m ahead of it, scored on summed heading change plus
        /// grade. The car goes in the right-hand lane, level, facing along it.</summary>
        static bool FindStraight(TrackPath p, out Vector3 at, out Vector3 fwd, out string where)
        {
            at = Vector3.zero; fwd = Vector3.forward; where = "";
            var w = p.waypoints;
            if (w == null || w.Length < 60) return false;
            int n = w.Length;
            bool loop = !p.HasEnds;
            float spacing = Mathf.Max(1f, p.spacing);
            int behind = Mathf.CeilToInt(30f / spacing), ahead = Mathf.CeilToInt(160f / spacing);
            float best = float.MaxValue; int bestI = -1;
            int lo = loop ? 0 : behind + 1, hi = loop ? n : n - ahead - 3;
            for (int i = lo; i < hi; i++)
            {
                float turn = 0f, grade = 0f;
                for (int k = -behind; k < ahead; k++)
                {
                    Vector3 a = W(w, i + k, loop), b = W(w, i + k + 1, loop), c = W(w, i + k + 2, loop);
                    Vector3 d1 = b - a, d2 = c - b;
                    float l1 = new Vector2(d1.x, d1.z).magnitude, l2 = new Vector2(d2.x, d2.z).magnitude;
                    if (l1 < 0.01f || l2 < 0.01f) continue;
                    turn += Vector2.Angle(new Vector2(d1.x, d1.z), new Vector2(d2.x, d2.z));
                    if (k > -4 && k < 12) grade = Mathf.Max(grade, Mathf.Abs(d1.y) / l1);
                }
                float score = turn + grade * 300f;
                if (score < best) { best = score; bestI = i; }
            }
            if (bestI < 0) return false;
            Vector3 f = W(w, bestI + 2, loop) - W(w, bestI - 2, loop); f.y = 0f;
            fwd = f.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float lane = p.roadWidth * 0.25f;
            at = w[bestI] + right * lane;
            // Seat on the road's own collider where one is under the point.
            if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out var hit, 10f,
                                ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.point.y - at.y) < 1.0f)
                at.y = hit.point.y;
            where = string.Format(CultureInfo.InvariantCulture,
                "straight at waypoint {0} (heading change {1:0.0} deg over 190 m, road {2:0.0} m, lane offset {3:0.00} m)",
                bestI, best, p.roadWidth, lane);
            return true;
        }

        static Vector3 W(Vector3[] w, int i, bool loop)
        {
            int n = w.Length;
            if (loop) return w[((i % n) + n) % n];
            return w[Mathf.Clamp(i, 0, n - 1)];
        }

        /// <summary>Wheels where the suspension rests them: on the rig's own
        /// wheelbase and track (CarBody.Apply set both for this shell) with the
        /// tyre touching the ground plane through the car's origin. In edit
        /// mode nothing else moves them, and a Charger shown on the FD's hubs
        /// would read its tyre contact half a metre wrong.</summary>
        static void SeatWheels(CarController car)
        {
            float ht = car.trackWidth * 0.5f, hb = car.wheelbase * 0.5f;
            var pos = new[]
            {
                new Vector3(-ht, car.wheelRadius, hb), new Vector3(ht, car.wheelRadius, hb),
                new Vector3(-ht, car.wheelRadius, -hb), new Vector3(ht, car.wheelRadius, -hb),
            };
            for (int i = 0; i < 4; i++)
            {
                if (car.wheelHubs[i] == null) continue;
                car.wheelHubs[i].localPosition = pos[i];
                car.wheelHubs[i].localRotation = Quaternion.identity;
            }
        }

        static CarGeo Measure(string key, GameObject player, CarBody body, CarController car)
        {
            var geo = new CarGeo { key = key, def = body.Def, wheelbase = car.wheelbase };
            var t = player.transform;
            var filters = new List<MeshFilter>();
            if (body.bodyFilter != null) filters.Add(body.bodyFilter);
            foreach (var wf in body.wheelFilters) if (wf != null) filters.Add(wf);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var r = mf.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                var vs = mesh.vertices;
                int baseI = geo.verts.Count;
                var m = mf.transform.localToWorldMatrix;
                bool isBody = mf == body.bodyFilter;
                foreach (var v in vs)
                {
                    Vector3 wv = m.MultiplyPoint3x4(v);
                    geo.verts.Add(wv);
                    Vector3 lv = t.InverseTransformPoint(wv);
                    geo.localVerts.Add(lv);
                    minY = Mathf.Min(minY, lv.y); maxY = Mathf.Max(maxY, lv.y);
                    if (!isBody) continue;
                    minX = Mathf.Min(minX, lv.x); maxX = Mathf.Max(maxX, lv.x);
                    minZ = Mathf.Min(minZ, lv.z); maxZ = Mathf.Max(maxZ, lv.z);
                }
                var tr = mesh.triangles;
                for (int i = 0; i < tr.Length; i++) geo.tris.Add(baseI + tr[i]);
            }
            if (geo.verts.Count == 0) { Debug.LogError("[CamFrame] FAIL no mesh on " + key); return null; }
            var box = player.GetComponent<BoxCollider>();
            geo.boxCenter = box != null ? box.center : Vector3.zero;
            geo.boxSize = box != null ? box.size : Vector3.one;
            geo.boxTailZ = geo.boxCenter.z - geo.boxSize.z * 0.5f;
            // The spec-sheet width the shell was scaled to (mirrors excluded),
            // which is what the reference W-shares are quoted per metre of.
            var model = CarModelLibrary.Get(key);
            geo.widthM = model != null && model.widthMm > 0 ? model.widthMm / 1000f : (maxX - minX);
            geo.tailZ = minZ;
            geo.noseZ = maxZ;
            geo.roofY = maxY;
            geo.meshBottom = minY;
            geo.rearAxleZ = -car.wheelbase * 0.5f;
            return geo;
        }

        /// <summary>Every shell in the library at its reference width: the
        /// body's car-local vertices (x divided back by the across-scale, so
        /// NATIVE), for fitting the chase rig's per-shell silhouette table.</summary>
        static void DumpAllShells(GameObject player, CarBody body, CarController car, Vector3 at, Vector3 fwd)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\n");
            bool first = true;
            foreach (var m in CarModelLibrary.Models)
            {
                var shell = CarModelLibrary.Load(m.key);
                if (shell == null) continue;
                body.widthMm = 0;
                body.Apply(shell, 0);
                player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
                var mesh = body.bodyFilter != null ? body.bodyFilter.sharedMesh : null;
                if (mesh == null) continue;
                var box = player.GetComponent<BoxCollider>();
                float sx = body.WidthScale;
                if (!first) sb.Append(",\n");
                first = false;
                sb.AppendFormat(ci, "  \"{0}\": {{\"sx\": {1:0.0000}, \"widthMm\": {2}, \"wheelbase\": {3:0.000}, \"track\": {4:0.000}, " +
                                    "\"boxCenter\": [{5:0.000},{6:0.000},{7:0.000}], \"boxSize\": [{8:0.000},{9:0.000},{10:0.000}], \"roofY\": {11:0.000}, \"v\": [",
                    m.key, sx, m.widthMm, car.wheelbase, car.trackWidth,
                    box.center.x, box.center.y, box.center.z, box.size.x, box.size.y, box.size.z, shell.roofY);
                var mx = body.bodyFilter.transform.localToWorldMatrix;
                var vs = mesh.vertices;
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 lv = player.transform.InverseTransformPoint(mx.MultiplyPoint3x4(vs[i]));
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat(ci, "{0:0.####},{1:0.###},{2:0.###}", lv.x / Mathf.Max(sx, 0.01f), lv.y, lv.z);
                }
                sb.Append("]}");
            }
            sb.Append("\n}\n");
            File.WriteAllText(Path.Combine(OutDir, "camframe_shells.json"), sb.ToString());
            Debug.Log("[CamFrame] every shell dumped to camframe_shells.json");
        }

        // ------------------------------------------------------------------
        //  The chase rig's silhouette table (ChaseSilhouettes.Table.cs)
        // ------------------------------------------------------------------
        static readonly float[] SilBands = { 0f, 0.2f, 0.35f, 0.5f, 0.65f, 0.8f, 1.01f };

        /// <summary>The lens poses a silhouette row has to hold over: 1.6-2.5 m
        /// up, 2-16 deg down, 1.8-3.6 m off the tail — every chase and close
        /// pose the rig can reach, and some it cannot.</summary>
        static IEnumerable<(double h, double pitch, double d)> SilPoses()
        {
            foreach (double h in new[] { 1.6, 2.0, 2.5 })
                foreach (double pd in new[] { 2.0, 8.0, 16.0 })
                    foreach (double d in new[] { 1.8, 2.6, 3.6 })
                        yield return (h, pd, d);
        }

        /// <summary>The body's vertices in the car's frame, x divided back by
        /// the across-scale (NATIVE), for the shell CarBody is wearing now.</summary>
        static List<Vector3> NativeBody(GameObject player, CarBody body)
        {
            var list = new List<Vector3>();
            var mesh = body.bodyFilter != null ? body.bodyFilter.sharedMesh : null;
            if (mesh == null) return list;
            float sx = Mathf.Max(body.WidthScale, 0.01f);
            var mx = body.bodyFilter.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices)
            {
                Vector3 lv = player.transform.InverseTransformPoint(mx.MultiplyPoint3x4(v));
                list.Add(new Vector3(lv.x / sx, lv.y, lv.z));
            }
            return list;
        }

        /// <summary>
        /// A shell's ChaseSilhouettes row, from its mesh: per height band of
        /// the body, and for the last 0.2 m of the tail, the vertex that is
        /// widest seen from behind summed over <see cref="SilPoses"/>; and the
        /// vertex a lens 2 m up and 2.6 m back sees lowest.
        /// </summary>
        static void SilhouetteRow(List<Vector3> v, float sx, out float tail, out List<Vector3> pts,
                                  out float lowY, out float lowZ)
        {
            tail = float.MaxValue;
            float top = float.MinValue;
            foreach (var q in v) { tail = Mathf.Min(tail, q.z); top = Mathf.Max(top, q.y); }
            pts = new List<Vector3>();
            for (int b = 0; b < SilBands.Length; b++)
            {
                var members = new List<Vector3>();
                foreach (var q in v)
                {
                    bool inGroup = b < SilBands.Length - 1
                        ? q.y >= SilBands[b] * top && q.y < SilBands[b + 1] * top
                        : q.z < tail + 0.2f;
                    if (inGroup) members.Add(q);
                }
                if (members.Count == 0) continue;
                var score = new double[members.Count];
                var r = new double[members.Count];
                foreach (var pose in SilPoses())
                {
                    double a = pose.pitch * Mathf.Deg2Rad, max = 0;
                    for (int i = 0; i < members.Count; i++)
                    {
                        var q = members[i];
                        double y = q.y - pose.h, z = q.z - (tail - pose.d);
                        r[i] = System.Math.Abs(q.x) * sx / (z * System.Math.Cos(a) - y * System.Math.Sin(a));
                        if (r[i] > max) max = r[i];
                    }
                    for (int i = 0; i < members.Count; i++) score[i] += r[i] / max;
                }
                int best = 0;
                for (int i = 1; i < members.Count; i++) if (score[i] > score[best]) best = i;
                var m = members[best];
                pts.Add(new Vector3(R3(Mathf.Abs(m.x)), R3(m.y), R3(m.z - tail)));
            }
            double bestAng = double.MinValue;
            lowY = 0f; lowZ = 0f;
            foreach (var q in v)
            {
                if (q.z >= tail + 1.5f) continue;
                double ang = System.Math.Atan2(2.0 - q.y, 2.6 + (q.z - tail));
                if (ang > bestAng) { bestAng = ang; lowY = R3(q.y); lowZ = R3(q.z - tail); }
            }
            tail = R3(tail);
        }

        static float R3(float x) => (float)System.Math.Round(x, 3);

        /// <summary>The widest a set of car-local points (x native) reads from
        /// a lens <paramref name="d"/> behind <paramref name="tail"/>, in x/z
        /// units — only ever compared with itself.</summary>
        static double SilWidth(IEnumerable<Vector3> v, float sx, float tail, (double h, double pitch, double d) pose)
        {
            double a = pose.pitch * Mathf.Deg2Rad, w = 0;
            foreach (var q in v)
            {
                double y = q.y - pose.h, z = q.z - (tail - pose.d);
                double zc = z * System.Math.Cos(a) - y * System.Math.Sin(a);
                if (zc > 0.05) w = System.Math.Max(w, System.Math.Abs(q.x) * sx / zc);
            }
            return w;
        }

        /// <summary>
        /// Every shell in the library against the table the game is running:
        /// the table's points must read within 3% of the mesh's own widest
        /// silhouette over every lens pose, and its tail within 2 cm. With
        /// <paramref name="emit"/>, also writes the table as the meshes give
        /// it, to Screenshots\CamFrame\ChaseSilhouettes.Table.cs, for copying
        /// over Scripts\ChaseSilhouettes.Table.cs after a shell changes.
        /// Returns the number of shells that fail.
        /// </summary>
        static int CheckSilhouettes(GameObject player, CarBody body, CarController car, Vector3 at, Vector3 fwd, bool emit)
        {
            var ci = CultureInfo.InvariantCulture;
            var file = new StringBuilder();
            file.Append("// <auto-generated>\n");
            file.Append("// Written by PSX Racing/Camera Framing Probe (PSX_CAMFRAME_EMIT=1,\n");
            file.Append("// tools\\camframe-probe.ps1 -Emit) from the body meshes. Do not edit by hand:\n");
            file.Append("// re-run the probe after a shell is re-baked or added. See ChaseSilhouettes.cs.\n");
            file.Append("// </auto-generated>\n");
            file.Append("namespace PSXRacing\n{\n    public static partial class ChaseSilhouettes\n    {\n");
            file.Append("        // key, tail z (car-local), lowest tail point (y, z-forward-of-tail),\n");
            file.Append("        // then (native half-width, y, z-forward-of-tail) per silhouette point.\n");
            file.Append("        static readonly Entry[] Table =\n        {\n");
            int fails = 0;
            double worstAll = 0;
            foreach (var m in CarModelLibrary.Models)
            {
                var shell = CarModelLibrary.Load(m.key);
                if (shell == null) continue;
                body.widthMm = 0;
                body.Apply(shell, 0);
                player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(fwd, Vector3.up));
                var v = NativeBody(player, body);
                if (v.Count == 0) continue;
                float sx = body.WidthScale;
                SilhouetteRow(v, sx, out float tail, out var pts, out float lowY, out float lowZ);

                file.Append("            new Entry(\"").Append(m.key).Append("\", ")
                    .Append(tail.ToString("0.000", ci)).Append("f, ").Append(lowY.ToString("0.000", ci)).Append("f, ")
                    .Append(lowZ.ToString("0.000", ci)).Append("f, new[] { ");
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i > 0) file.Append(", ");
                    file.Append(pts[i].x.ToString("0.000", ci)).Append("f, ").Append(pts[i].y.ToString("0.000", ci))
                        .Append("f, ").Append(pts[i].z.ToString("0.000", ci)).Append('f');
                }
                file.Append(" }),\n");

                // What the game runs with, against the mesh.
                if (!ChaseSilhouettes.TryGet(m.key, out var e))
                {
                    fails++;
                    Debug.LogError("[CamFrame] FAIL no ChaseSilhouettes row for " + m.key + " (run the probe with -Emit)");
                    continue;
                }
                var rowPts = new List<Vector3>();
                for (int i = 0; i < e.Count; i++) { var q = e.Point(i); rowPts.Add(new Vector3(q.x, q.y, q.z + e.tailZ)); }
                double worst = 0;
                foreach (var pose in SilPoses())
                {
                    double wm = SilWidth(v, sx, tail, pose), wt = SilWidth(rowPts, sx, tail, pose);
                    if (wm > 0) worst = System.Math.Max(worst, System.Math.Abs(1 - wt / wm));
                }
                worstAll = System.Math.Max(worstAll, worst);
                bool ok = worst <= 0.03 && Mathf.Abs(e.tailZ - tail) <= 0.02f;
                if (!ok)
                {
                    fails++;
                    Debug.LogError(string.Format(ci, "[CamFrame] FAIL ChaseSilhouettes row for {0} is stale: width off by up to {1:0.0}%, tail {2:0.000} vs mesh {3:0.000} (run the probe with -Emit)",
                        m.key, worst * 100, e.tailZ, tail));
                }
            }
            file.Append("        };\n    }\n}\n");
            if (emit)
            {
                File.WriteAllText(Path.Combine(OutDir, "ChaseSilhouettes.Table.cs"), file.ToString());
                Debug.Log("[CamFrame] silhouette table written to " + Path.Combine(OutDir, "ChaseSilhouettes.Table.cs"));
            }
            Debug.Log(string.Format(ci, "[CamFrame] silhouette table vs meshes: {0} shell(s) out, worst width error {1:0.0}%",
                                    fails, worstAll * 100));
            return fails;
        }

        static void AppendHull(StringBuilder sb, CarGeo g, ref bool first)
        {
            if (!first) sb.Append(",\n");
            first = false;
            var ci = CultureInfo.InvariantCulture;
            sb.Append("  \"").Append(g.key).Append("\": {");
            sb.AppendFormat(ci, "\"width\": {0:0.000}, \"tailZ\": {1:0.000}, \"noseZ\": {2:0.000}, \"roofY\": {3:0.000}, " +
                                "\"rearAxleZ\": {4:0.000}, \"wheelbase\": {5:0.000}, \"boxCenter\": [{6:0.000},{7:0.000},{8:0.000}], " +
                                "\"boxSize\": [{9:0.000},{10:0.000},{11:0.000}], \"defRoofY\": {12:0.000}, \"defNoseZ\": {13:0.000},\n    \"v\": [",
                g.widthM, g.tailZ, g.noseZ, g.roofY, g.rearAxleZ, g.wheelbase,
                g.boxCenter.x, g.boxCenter.y, g.boxCenter.z, g.boxSize.x, g.boxSize.y, g.boxSize.z,
                g.def != null ? g.def.roofY : 0f, g.def != null ? g.def.noseZ : 0f);
            for (int i = 0; i < g.localVerts.Count; i++)
            {
                var v = g.localVerts[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(ci, "{0:0.###},{1:0.###},{2:0.###}", v.x, v.y, v.z);
            }
            sb.Append("],\n    \"t\": [");
            for (int i = 0; i < g.tris.Count; i++) { if (i > 0) sb.Append(','); sb.Append(g.tris[i]); }
            sb.Append("]}");
        }

        // ------------------------------------------------------------------
        //  Where the lens is
        // ------------------------------------------------------------------
        static Pose PoseFor(string rig, ChaseCamera.View v, ScreenDef scr, float speedMps, GameObject player)
        {
            if (rig == "legacy") return LegacyPose(v, speedMps, player.transform, player.GetComponent<BoxCollider>());
            // THE GAME'S OWN POSE: ChaseCamera.SteadyPose is what Follow
            // converges on at a steady speed on a straight (the follow lag's
            // trail is led out, so nothing is added here for it), through the
            // same fit, fed the same frame ChaseCamera reads off the car and
            // the dials GaugeCluster would publish on this screen.
            var pose = new Pose { near = 0.25f };
            ChaseCamera.SteadyPose(v, scr.aspect, speedMps, ChaseCamera.DefaultSpeedFullMps, player.transform,
                                   ChaseCamera.FrameOf(player), DialsFor(scr),
                                   out pose.pos, out pose.rot, out pose.vfov, out pose.shift);
            return pose;
        }

        /// <summary>The dials GaugeCluster lays out on this screen, as the
        /// chase camera reads them (GaugeCluster.TachCircle / SpeedoCircle).</summary>
        static ChaseCamera.HudDials DialsFor(ScreenDef scr)
        {
            var d = new ChaseCamera.HudDials();
            foreach (var e in HudLayout(scr.aspect, scr.touch))
            {
                if (!e.round) continue;
                var c = new Vector4((e.x0 + e.x1) * 0.5f, (e.y0 + e.y1) * 0.5f, (e.x1 - e.x0) * 0.5f, (e.y1 - e.y0) * 0.5f);
                if (e.name == "tach") d.left = c; else d.right = c;
            }
            return d;
        }

        /// <summary>
        /// The chase rig as every baked scene carried it on 2026-09-28
        /// (ChaseCamera @ ac31258, and the scene YAML: distance 5.4, height
        /// 1.8, lookHeight 0.9, baseFOV 58, speedFOV 8, speedPullBack 0.9,
        /// speedDrop 0.25, speedLookAhead 2.5, lagClampM 1.2, positionLag 5,
        /// speedFullMps 55.6; CLOSE x0.62 / x1.15 / x0.85 and +4 deg). The
        /// steady trail of its first-order position lag is v / positionLag,
        /// pinned at lagClampM, so above 6 m/s the lens sat 1.2 m further back
        /// than "distance" said. Kept so the before/after can be re-measured.
        /// </summary>
        static Pose LegacyPose(ChaseCamera.View v, float speedMps, Transform car, BoxCollider box)
        {
            float fit = box != null ? Mathf.Clamp(box.size.z / 4.1f, 0.9f, 1.3f) : 1f;
            bool close = v == ChaseCamera.View.Close;
            float dm = close ? 0.62f : 1f, hm = close ? 1.15f : 1f, lm = close ? 0.85f : 1f;
            float t = ChaseCamera.SpeedT(speedMps, 55.6f);
            float dist = 5.4f * fit * dm + 0.9f * t * fit;
            float h = 1.8f * hm - 0.25f * t;
            float lag = Mathf.Min(Mathf.Abs(speedMps) / 5f, 1.2f);
            Vector3 fwd = car.forward; fwd.y = 0f; fwd.Normalize();
            var pose = new Pose();
            pose.pos = car.position - fwd * (dist + lag) + Vector3.up * h;
            Vector3 look = car.position + Vector3.up * (0.9f * lm) + fwd * (1.5f + 2.5f * t);
            pose.rot = Quaternion.LookRotation(look - pose.pos, Vector3.up);
            pose.vfov = 58f + (close ? 4f : 0f) + 8f * t;
            pose.near = 0.25f;
            return pose;
        }

        // ------------------------------------------------------------------
        //  The projection
        // ------------------------------------------------------------------
        /// <summary>Unity's symmetric perspective, by hand: viewport x, y
        /// (0..1, y up) and the depth along the lens. fieldOfView is VERTICAL.</summary>
        static Vector3 Project(Pose p, float aspect, Vector3 world)
        {
            Vector3 l = Quaternion.Inverse(p.rot) * (world - p.pos);
            float t = Mathf.Tan(p.vfov * 0.5f * Mathf.Deg2Rad);
            if (l.z < 1e-4f) return new Vector3(float.NaN, float.NaN, l.z);
            return new Vector3(0.5f + p.shift + 0.5f * l.x / (l.z * t * aspect), 0.5f + 0.5f * l.y / (l.z * t), l.z);
        }

        static Row Evaluate(string rig, ScreenDef scr, ChaseCamera.View v, float kmh, CarGeo g, Pose p, Transform car)
        {
            var r = new Row
            {
                rig = rig, screen = scr.tag, view = v == ChaseCamera.View.Chase ? "CHASE" : "CLOSE", car = g.key,
                kmh = kmh, aspect = scr.aspect, vfov = p.vfov, shift = p.shift,
                hfov = 2f * Mathf.Atan(Mathf.Tan(p.vfov * 0.5f * Mathf.Deg2Rad) * scr.aspect) * Mathf.Rad2Deg,
                tall = g.roofY > TallRoofM, info = scr.info,
            };
            Vector3 fwd = car.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 camLocal = car.InverseTransformPoint(p.pos);
            r.back = g.tailZ - camLocal.z;          // horizontal, lens to the rearmost bodywork
            r.height = camLocal.y;                  // above the ground plane through the car's origin
            Vector3 lf = p.rot * Vector3.forward;
            r.pitch = -Mathf.Asin(Mathf.Clamp(lf.y, -1f, 1f)) * Mathf.Rad2Deg;

            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            var proj = new Vector3[g.verts.Count];
            for (int i = 0; i < g.verts.Count; i++)
            {
                var q = Project(p, scr.aspect, g.verts[i]);
                proj[i] = q;
                if (float.IsNaN(q.x)) continue;
                minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
                minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
            }
            r.w = maxX - minX;
            r.xH = r.w * scr.aspect;
            r.bottom = minY;
            r.roof = maxY;
            r.contact = Project(p, scr.aspect, car.TransformPoint(new Vector3(0f, 0f, g.rearAxleZ))).y;
            r.horizon = Project(p, scr.aspect, p.pos + fwd * 100000f).y;

            // ---- how far ahead of the nose the road shows over the car: the
            // farthest ground point any vertex of the car's middle strip hides.
            float hidden = float.MinValue;
            bool blind = false;
            foreach (var lv in g.localVerts)
            {
                if (Mathf.Abs(lv.x) > 0.35f) continue;
                float dy = camLocal.y - lv.y;
                if (dy <= 0.01f) { if (lv.z > camLocal.z) blind = true; continue; }
                hidden = Mathf.Max(hidden, camLocal.z + (lv.z - camLocal.z) * camLocal.y / dy);
            }
            r.road = blind ? 999f : hidden - g.noseZ;

            // ---- the silhouette against the HUD, on a grid ---------------
            var hud = HudLayout(scr.aspect, scr.touch);
            var mask = Silhouette(proj, g.tris, scr.aspect, out int gw, out int gh);
            var clusterNames = new HashSet<string>();
            var otherNames = new HashSet<string>();
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    if (!mask[y * gw + x]) continue;
                    r.carCells++;
                    float fx = (x + 0.5f) / gw, fy = (y + 0.5f) / gh;
                    bool inCluster = false, inOther = false;
                    foreach (var e in hud)
                    {
                        if (!e.Contains(fx, fy)) continue;
                        if (e.cluster) { inCluster = true; clusterNames.Add(e.name); }
                        else { inOther = true; otherNames.Add(e.name); }
                    }
                    if (inCluster) r.clusterCells++;
                    if (inOther) r.otherCells++;
                }
            r.clusterHit = string.Join("+", clusterNames);
            r.otherHit = string.Join("+", otherNames);

            Targets(r, v, g, scr);
            return r;
        }

        const int GridW = 400;

        static bool[] Silhouette(Vector3[] proj, List<int> tris, float aspect, out int gw, out int gh)
        {
            gw = GridW;
            gh = Mathf.RoundToInt(GridW / aspect);
            var mask = new bool[gw * gh];
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = proj[tris[i]], b = proj[tris[i + 1]], c = proj[tris[i + 2]];
                if (float.IsNaN(a.x) || float.IsNaN(b.x) || float.IsNaN(c.x)) continue;
                float ax = a.x * gw, ay = a.y * gh, bx = b.x * gw, by = b.y * gh, cx = c.x * gw, cy = c.y * gh;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
                int x1 = Mathf.Min(gw - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
                int y1 = Mathf.Min(gh - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
                float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Mathf.Abs(area) < 1e-6f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = (bx - px) * (cy - py) - (by - py) * (cx - px);
                        float w1 = (cx - px) * (ay - py) - (cy - py) * (ax - px);
                        float w2 = (ax - px) * (by - py) - (ay - py) * (bx - px);
                        bool inside = area > 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0);
                        if (inside) mask[y * gw + x] = true;
                    }
            }
            return mask;
        }

        // ------------------------------------------------------------------
        //  The HUD, as GaugeCluster / TouchControls / RaceHUD / PizzaCam lay it
        // ------------------------------------------------------------------
        class HudEl
        {
            public string name;
            public bool cluster, round;
            public float x0, x1, y0, y1; // normalised, y up
            public bool Contains(float x, float y)
            {
                if (!round) return x >= x0 && x <= x1 && y >= y0 && y <= y1;
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, rx = (x1 - x0) * 0.5f, ry = (y1 - y0) * 0.5f;
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                return dx * dx + dy * dy <= 1f;
            }
        }

        /// <summary>
        /// The driving HUD's footprint on a screen of <paramref name="aspect"/>,
        /// normalised with y UP. The cluster and touch canvases are
        /// ScaleWithScreenSize 1280x720 at match 0.5, so the canvas is
        /// sqrt(921600 / aspect) units high; the dials are GaugeCluster.Build's
        /// arithmetic (radius 0.150 H, margin 0.03 H, and on a touch screen the
        /// band between TouchControls' wheel and pedal column); the wheel box
        /// and the pedal column are TouchControls'; the race map is RaceHUD's
        /// on the 480-line framebuffer; the pizza cam is PizzaCam's.
        /// </summary>
        static List<HudEl> HudLayout(float aspect, bool touch)
        {
            var list = new List<HudEl>();
            float H = Mathf.Sqrt(921600f / aspect), W = aspect * H;
            float radius = Mathf.Round(H * 0.150f), margin = Mathf.Round(H * 0.03f);
            Vector2 tach, speedo;
            float wheelInset = TouchControls.EdgeMargin + 300f;     // TouchControls.WheelInset
            float pedalsInset = 204.8f;                              // TouchControls.PedalsInset
            if (touch)
            {
                float left = wheelInset, right = W - pedalsInset;
                float band = Mathf.Max(160f, right - left);
                radius = Mathf.Min(radius, Mathf.Floor((band - 24f) * 0.25f));
                float mid = W * 0.5f;
                float lroom = mid - 12f - (left + 12f), rroom = (right - 12f) - (mid + 12f);
                radius = Mathf.Max(16f, Mathf.Min(radius, Mathf.Floor(Mathf.Min(lroom, rroom) * 0.5f)));
                tach = new Vector2(left + 12f + radius, radius + margin);
                speedo = new Vector2(right - 12f - radius, radius + margin);
            }
            else
            {
                tach = new Vector2(radius + margin, radius + margin);
                speedo = new Vector2(W - (radius + margin), radius + margin);
            }
            float r = radius;
            list.Add(new HudEl { name = "tach", cluster = true, round = true,
                x0 = (tach.x - r) / W, x1 = (tach.x + r) / W, y0 = (tach.y - r) / H, y1 = (tach.y + r) / H });
            list.Add(new HudEl { name = "speedo", cluster = true, round = true,
                x0 = (speedo.x - r) / W, x1 = (speedo.x + r) / W, y0 = (speedo.y - r) / H, y1 = (speedo.y + r) / H });
            if (!touch)
            {
                float gw = r * 0.52f, gh = r * 0.68f, gap = r * 0.14f;
                float gx = tach.x + r + gap + gw * 0.5f, gy = margin + gh * 0.5f;
                list.Add(new HudEl { name = "gear", cluster = true,
                    x0 = (gx - gw / 2) / W, x1 = (gx + gw / 2) / W, y0 = (gy - gh / 2) / H, y1 = (gy + gh / 2) / H });
                list.Add(new HudEl { name = "menu", x0 = 24f / W, x1 = 144f / W, y0 = (H - 86f) / H, y1 = (H - 24f) / H });
            }
            else
            {
                float e = TouchControls.EdgeMargin;
                list.Add(new HudEl { name = "wheel", cluster = true,
                    x0 = e / W, x1 = (e + 300f) / W, y0 = e / H, y1 = TouchControls.WheelTop / H });
                list.Add(new HudEl { name = "pedals", cluster = true,
                    x0 = (W - pedalsInset) / W, x1 = (W - e) / W, y0 = 26f / H, y1 = 460f / H });
                list.Add(new HudEl { name = "menu", x0 = 24f / W, x1 = 144f / W,
                    y0 = (TouchControls.WheelTop + 16f) / H, y1 = (TouchControls.WheelTop + 78f) / H });
            }
            float rightTop = speedo.y + r;
            const float PW = 236f, PH = 159f;
            float py, px;
            if (touch) { py = Mathf.Max(H - 30f - 74f - 14f - PH, rightTop + 14f); px = pedalsInset + 14f; }
            else { py = Mathf.Max(H * 0.60f - PH * 0.5f, rightTop + 14f); px = 18f; }
            py = Mathf.Min(py, H - PH - 18f);
            list.Add(new HudEl { name = "pizzacam", x0 = (W - px - PW) / W, x1 = (W - px) / W, y0 = py / H, y1 = (py + PH) / H });
            float fbW = FrameLines * aspect;
            float frac = touch ? 0.26f : 0.30f, centre = touch ? 1f - 0.11f - 0.13f : 0.60f;
            float mpx = Mathf.Round(FrameLines * frac);
            list.Add(new HudEl { name = "map", x0 = 4f / fbW, x1 = (4f + mpx) / fbW,
                y0 = centre - mpx / 2f / FrameLines, y1 = centre + mpx / 2f / FrameLines });
            return list;
        }

        // ------------------------------------------------------------------
        //  The reference bands (NFS U / U2 / MW, scratchpad cam_reference.md)
        // ------------------------------------------------------------------
        /// <summary>Roof above this counts as a tall vehicle: the lens rises
        /// with the roof and pitches down to keep the car on the bottom edge,
        /// the way MW's van / pickup / news-van cameras do.</summary>
        const float TallRoofM = 1.45f;

        /// <summary>
        /// The bands, y measured from the BOTTOM of the frame (Unity's
        /// viewport; the reference quotes from the top). Composition first —
        /// it is what the player sees — from the NFS U / U2 / MW frames:
        ///   W       the silhouette's share of the frame width: 25% x real
        ///           width / 1.76 m in CHASE (+-1.5 points), 34% in CLOSE
        ///           (+-2). At 200 km/h at most 10% smaller, and CHASE still
        ///           at least 23% x width / 1.76.
        ///   contact the rear tyres' line: CHASE 11% up at 16:9, 8% on a phone
        ///           (+-2, 2.5 on the phone) and 7-15% at any speed; CLOSE 4-15%.
        ///   horizon CHASE 51-55% at rest at 16:9, 49-53% at 200; 56-64% on
        ///           the phone. CLOSE sits steeper: 55-67% (16:9), 60-80% (phone).
        ///   roof    5-16% of the frame under the horizon: road over the car.
        ///   bottom  the tail's lowest edge in frame (CHASE); CLOSE may clip
        ///           it by 4% of the frame.
        ///   HUD     no cell of the silhouette under the cluster: the dials,
        ///           the gear panel, and on a phone the touch wheel and pedals.
        /// Tall vehicles (roof above 1.45 m) are framed by MW's van rule: the
        /// lens rises with the roof and pitches down, capped at 15 deg, and the
        /// car may fall short of its share (CHASE 80%, CLOSE 70%). On a phone,
        /// CLOSE may give up share to the lane between the dials, but never
        /// below the CHASE view's. The lens itself (height, distance, pitch) is
        /// REPORTED, not banded: with real shells the NFS reference lens (2.0 m
        /// up, 3.2 m off a generic square tail) puts a tapered FD at 21%, so
        /// the rig solves the lens per car to hold the composition instead.
        /// </summary>
        static void Targets(Row r, ChaseCamera.View v, CarGeo g, ScreenDef scr)
        {
            bool chase = v == ChaseCamera.View.Chase;
            bool phone = scr.aspect > 1.9f;
            bool rest = r.kmh < 1f, top = r.kmh > 150f;
            float wRef = (chase ? 0.25f : 0.34f) * g.widthM / RefWidthM;
            float tol = chase ? 0.015f : 0.02f;
            r.wTarget = wRef;
            var f = r.fails;

            // ---- width share -------------------------------------------------
            if (rest)
            {
                if (r.tall) Band(f, "W (tall)", r.w, (chase ? 0.80f : 0.70f) * wRef, wRef + tol);
                else if (chase || !phone) Band(f, "W", r.w, wRef - tol, wRef + tol);
                else
                {
                    float cw = ChaseRestW(r);
                    Band(f, "W (lane)", r.w, float.IsNaN(cw) ? 0f : cw - 0.002f, wRef + tol);
                }
            }
            else
            {
                float restW = RestW(r);
                if (!float.IsNaN(restW) && r.w < restW * 0.90f)
                    f.Add(string.Format(CultureInfo.InvariantCulture, "W shrinks {0:0.0}% (max 10%)", (1f - r.w / restW) * 100f));
                if (top && !r.tall && (chase || !phone))
                    Band(f, "W@200", r.w, (chase ? 0.23f : 0.31f) * g.widthM / RefWidthM - 0.0005f, 1f);
            }

            // ---- where the car sits -------------------------------------------
            if (chase)
            {
                Band(f, "contact", r.contact, 0.07f, 0.15f);
                if (rest && !r.tall)
                {
                    float c = phone ? 0.08f : 0.11f, ct = phone ? 0.025f : 0.02f;
                    Band(f, "contact@rest", r.contact, c - ct, c + ct);
                }
                Band(f, "bottom in frame", r.bottom, 0f, 1f);
            }
            else
            {
                Band(f, "contact", r.contact, 0.04f, 0.15f);
                Band(f, "bottom", r.bottom, -0.04f, 1f);
            }
            if (!r.tall)
            {
                if (rest)
                {
                    if (chase) Band(f, "horizon", r.horizon, phone ? 0.56f : 0.51f, phone ? 0.64f : 0.55f);
                    else Band(f, "horizon", r.horizon, phone ? 0.60f : 0.55f, phone ? 0.80f : 0.67f);
                }
                else if (top && chase && !phone) Band(f, "horizon@200", r.horizon, 0.49f, 0.53f);
                Band(f, "roof under horizon", r.horizon - r.roof, 0.05f, 0.16f);
            }
            else if (r.pitch > 15.05f) f.Add("pitch " + r.pitch.ToString("0.0", CultureInfo.InvariantCulture) + " over 15");

            // ---- the HUD ---------------------------------------------------------
            if (r.clusterCells > 0)
                f.Add(string.Format(CultureInfo.InvariantCulture, "under the HUD cluster ({0}) {1:0.0}% of the car",
                                    r.clusterHit, 100f * r.clusterCells / Mathf.Max(1, r.carCells)));
        }

        static float ChaseRestW(Row r)
        {
            foreach (var o in rows)
                if (o.rig == r.rig && o.screen == r.screen && o.view == "CHASE" && o.car == r.car && o.kmh < 1f)
                    return o.w;
            return float.NaN;
        }

        static float RestW(Row r)
        {
            foreach (var o in rows)
                if (o.rig == r.rig && o.screen == r.screen && o.view == r.view && o.car == r.car && o.kmh < 1f)
                    return o.w;
            return float.NaN;
        }

        static void Band(List<string> f, string what, float v, float lo, float hi)
        {
            if (v >= lo && v <= hi) return;
            f.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.000} not in [{2:0.000}, {3:0.000}]", what, v, lo, hi));
        }

        // ------------------------------------------------------------------
        //  The report
        // ------------------------------------------------------------------
        static int WriteReport(string rig, string venue, string where)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("CAMERA FRAMING PROBE  rig=" + rig + "  venue=" + venue + "  " + where);
            sb.AppendLine("y is measured from the BOTTOM of the frame (0..1). W = car silhouette width / frame width;");
            sb.AppendLine("xH = W x aspect; back = lens to the rearmost bodywork, horizontal; h = lens above the ground;");
            sb.AppendLine("contact = rear-axle ground point; bottom/roof = silhouette; HUD = % of the car's silhouette under the cluster.");
            sb.AppendLine();
            sb.AppendLine("screen  view  car             km/h  back   h     pitch  vFOV  hFOV   shift  W      Wtgt   xH     contact bottom roof   horizon road   HUD%   verdict");
            int fails = 0;
            var csv = new StringBuilder("rig,screen,view,car,kmh,back,height,pitch,vfov,hfov,shift,w,wtarget,xh,contact,bottom,roof,horizon,road,hudpct,hud,other,info,fails\n");
            foreach (var r in rows)
            {
                float hud = 100f * r.clusterCells / Mathf.Max(1, r.carCells);
                string verdict = r.fails.Count == 0 ? "ok" : (r.info ? "info: " : "FAIL: ") + string.Join("; ", r.fails);
                if (r.fails.Count > 0 && !r.info) fails++;
                sb.AppendLine(string.Format(ci,
                    "{0,-7} {1,-5} {2,-15} {3,4:0}  {4,5:0.00} {5,5:0.00} {6,5:0.0}  {7,4:0.0}  {8,5:0.0}  {9,6:+0.000;-0.000}  {10,5:0.000}  {11,5:0.000}  {12,5:0.000}  {13,6:0.000}  {14,5:0.000}  {15,5:0.000}  {16,6:0.000}  {17,5:0.0}  {18,5:0.0}  {19}",
                    r.screen, r.view, r.car, r.kmh, r.back, r.height, r.pitch, r.vfov, r.hfov, r.shift, r.w, r.wTarget, r.xH,
                    r.contact, r.bottom, r.roof, r.horizon, r.road, hud, verdict));
                csv.AppendLine(string.Format(ci,
                    "{0},{1},{2},{3},{4:0},{5:0.000},{6:0.000},{7:0.00},{8:0.00},{9:0.00},{10:0.0000},{11:0.0000},{12:0.0000},{13:0.0000},{14:0.0000},{15:0.0000},{16:0.0000},{17:0.0000},{18:0.00},{19:0.00},{20},{21},{22},\"{23}\"",
                    r.rig, r.screen, r.view, r.car, r.kmh, r.back, r.height, r.pitch, r.vfov, r.hfov, r.shift, r.w, r.wTarget, r.xH,
                    r.contact, r.bottom, r.roof, r.horizon, r.road, hud, r.clusterHit, r.otherHit, r.info ? 1 : 0, string.Join("; ", r.fails)));
            }
            sb.AppendLine();
            int counted = 0;
            foreach (var r in rows) if (!r.info) counted++;
            sb.AppendLine(fails == 0 ? "ALL IN TARGET (" + counted + " cases)" : "OUT OF TARGET: " + fails + " of " + counted);
            File.WriteAllText(Path.Combine(OutDir, "camframe_" + rig + ".txt"), sb.ToString());
            File.WriteAllText(Path.Combine(OutDir, "camframe_" + rig + ".csv"), csv.ToString());
            foreach (var line in sb.ToString().Split('\n'))
                if (line.Trim().Length > 0) Debug.Log("[CamFrame] " + line.TrimEnd());
            return fails;
        }

        // ------------------------------------------------------------------
        //  The pictures
        // ------------------------------------------------------------------
        static Texture2D RenderFrame(Camera cam, Pose p, ScreenDef scr, CarGeo g, Row row)
        {
            int h = FrameLines, w = Mathf.RoundToInt(h * scr.aspect) & ~1;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            var keepTarget = cam.targetTexture;
            cam.transform.SetPositionAndRotation(p.pos, p.rot);
            cam.fieldOfView = p.vfov;
            cam.nearClipPlane = p.near;
            cam.targetTexture = rt;
            cam.aspect = w / (float)h;
            if (Mathf.Abs(p.shift) > 1e-4f)
                cam.projectionMatrix = ChaseCamera.ShiftedProjection(p.vfov, w / (float)h, p.near, cam.farClipPlane, p.shift);
            else cam.ResetProjectionMatrix();
            StreetLights.Push(p.pos, p.rot * Vector3.forward);
            Texture2D tex = null;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
            }
            else Debug.LogWarning("[CamFrame] RenderRequest unsupported");
            cam.targetTexture = keepTarget;
            cam.ResetProjectionMatrix();
            rt.Release();
            Object.DestroyImmediate(rt);
            if (tex == null) return null;
            Annotate(tex, p, scr, g, row);
            return tex;
        }

        static readonly Color32 Amber = new Color32(255, 176, 32, 255);
        static readonly Color32 Grey = new Color32(170, 170, 170, 255);
        static readonly Color32 Cyan = new Color32(40, 230, 255, 255);
        static readonly Color32 Green = new Color32(80, 255, 80, 255);
        static readonly Color32 Magenta = new Color32(255, 60, 220, 255);
        static readonly Color32 Red = new Color32(255, 40, 40, 255);

        /// <summary>HUD footprint (amber = cluster, grey = the rest), the
        /// silhouette's box (cyan), horizon (green), rear-axle contact line
        /// (magenta), and the cells of car under the cluster (red).</summary>
        static void Annotate(Texture2D tex, Pose p, ScreenDef scr, CarGeo g, Row row)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            foreach (var e in HudLayout(scr.aspect, scr.touch))
            {
                var col = e.cluster ? Amber : Grey;
                if (e.round) Ellipse(px, w, h, e, col);
                else Rect(px, w, h, e.x0, e.y0, e.x1, e.y1, col);
            }
            // the car under the cluster
            var proj = new Vector3[g.verts.Count];
            for (int i = 0; i < g.verts.Count; i++) proj[i] = Project(p, scr.aspect, g.verts[i]);
            var mask = Silhouette(proj, g.tris, scr.aspect, out int gw, out int gh);
            var hud = HudLayout(scr.aspect, scr.touch);
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    if (!mask[y * gw + x]) continue;
                    float fx = (x + 0.5f) / gw, fy = (y + 0.5f) / gh;
                    bool hit = false;
                    foreach (var e in hud) if (e.cluster && e.Contains(fx, fy)) { hit = true; break; }
                    if (!hit) continue;
                    int X = Mathf.Clamp((int)(fx * w), 0, w - 1), Y = Mathf.Clamp((int)(fy * h), 0, h - 1);
                    Put(px, w, h, X, Y, Red); Put(px, w, h, X + 1, Y, Red); Put(px, w, h, X, Y + 1, Red);
                }
            float left = 0.5f - row.w * 0.5f, right = 0.5f + row.w * 0.5f;
            // silhouette box from the real extents
            float minX = 1e9f, maxX = -1e9f;
            foreach (var q in proj) { if (float.IsNaN(q.x)) continue; minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); }
            Rect(px, w, h, minX, row.bottom, maxX, row.roof, Cyan);
            HLine(px, w, h, 0f, 1f, row.horizon, Green, dashed: true);
            HLine(px, w, h, minX - 0.05f, maxX + 0.05f, row.contact, Magenta, dashed: false);
            // the target width, as two ticks either side of the contact line
            float tl = 0.5f + p.shift - row.wTarget * 0.5f, tr = 0.5f + p.shift + row.wTarget * 0.5f;
            VLine(px, w, h, tl, row.contact - 0.03f, row.contact + 0.03f, Magenta);
            VLine(px, w, h, tr, row.contact - 0.03f, row.contact + 0.03f, Magenta);
            tex.SetPixels32(px);
            tex.Apply();
        }

        static void Put(Color32[] px, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            px[y * w + x] = c;
        }

        static void HLine(Color32[] px, int w, int h, float x0, float x1, float y, Color32 c, bool dashed)
        {
            int Y = Mathf.RoundToInt(y * h);
            for (int x = Mathf.Max(0, (int)(x0 * w)); x < Mathf.Min(w, (int)(x1 * w)); x++)
                if (!dashed || (x / 6) % 2 == 0) Put(px, w, h, x, Y, c);
        }

        static void VLine(Color32[] px, int w, int h, float x, float y0, float y1, Color32 c)
        {
            int X = Mathf.RoundToInt(x * w);
            for (int y = Mathf.Max(0, (int)(y0 * h)); y < Mathf.Min(h, (int)(y1 * h)); y++) Put(px, w, h, X, y, c);
        }

        static void Rect(Color32[] px, int w, int h, float x0, float y0, float x1, float y1, Color32 c)
        {
            HLine(px, w, h, x0, x1, y0, c, false);
            HLine(px, w, h, x0, x1, y1, c, false);
            VLine(px, w, h, x0, y0, y1, c);
            VLine(px, w, h, x1, y0, y1, c);
        }

        static void Ellipse(Color32[] px, int w, int h, HudEl e, Color32 c)
        {
            float cx = (e.x0 + e.x1) * 0.5f * w, cy = (e.y0 + e.y1) * 0.5f * h;
            float rx = (e.x1 - e.x0) * 0.5f * w, ry = (e.y1 - e.y0) * 0.5f * h;
            for (int i = 0; i < 720; i++)
            {
                float a = i * Mathf.PI / 360f;
                Put(px, w, h, Mathf.RoundToInt(cx + Mathf.Cos(a) * rx), Mathf.RoundToInt(cy + Mathf.Sin(a) * ry), c);
            }
        }

        /// <summary>One contact sheet per rig, view and screen: a row per car,
        /// a column per speed, each frame at half size.</summary>
        static void WriteSheet(string key, List<(Texture2D tex, int rowI, int colI)> frames)
        {
            if (frames.Count == 0) return;
            int fw = frames[0].tex.width / 2, fh = frames[0].tex.height / 2;
            int cols = SpeedsKmh.Length, rowsN = CarKeys.Length, pad = 4;
            int W = cols * fw + (cols + 1) * pad, H = rowsN * fh + (rowsN + 1) * pad;
            var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 24, 24, 255);
            foreach (var f in frames)
            {
                var src = f.tex.GetPixels32();
                int sw = f.tex.width;
                int ox = pad + f.colI * (fw + pad);
                int oy = H - (pad + (f.rowI + 1) * (fh + pad)) + pad;  // first car on top
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        px[(oy + y) * W + ox + x] = src[(y * 2) * sw + x * 2];
            }
            sheet.SetPixels32(px);
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, "cf_sheet_" + key + ".png"), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            foreach (var f in frames) Object.DestroyImmediate(f.tex);
        }
    }
}
