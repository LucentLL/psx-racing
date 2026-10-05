using System.Collections;
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
    /// The chase rig IN THE RUNNING GAME (2026-09-28, the NFS U / MW pass).
    ///
    /// The framing probe (CamFrameProbe) measures ChaseCamera.SteadyPose —
    /// the pose Follow is supposed to converge on — because Follow lerps by
    /// Time.deltaTime and cannot run outside play mode. That leaves the
    /// questions only the game can answer, and they are the ones that hid the
    /// old rig's real framing for months: does the running camera actually SIT
    /// there at speed — and on a HILL? The follow lag used to park it 1.2 m
    /// further back than the rig said above 22 km/h, and its vertical half
    /// still dropped the lens v x grade / positionLag on a climb (0.44 m at
    /// 100 km/h on 8%) after the along-track half was led out; nothing that
    /// looked at still frames on a flat straight could see either.
    ///
    /// So: a race is started on a drag strip in the FD, the game camera is
    /// pointed at an off-screen framebuffer of a set shape (16:9, and a
    /// 19.5:9 phone's) and the car is driven KINEMATICALLY along a road laid
    /// for the purpose, the clock stepped at a fixed 60 Hz:
    ///   LEVEL      at a held 0, 100 and 200 km/h, CHASE and CLOSE; after 2.5 s
    ///              the live lens is compared with SteadyPose (position, aim,
    ///              lens) and the car's share, contact line and horizon are read
    ///              THROUGH THE LIVE CAMERA (its own projection, shift and all).
    ///   GRADES     a straight +7% and -7% at 100 and 200 km/h. A steady grade
    ///              must show the level road's picture turned onto the grade:
    ///              the lens where the level pose, tilted, puts it; the roof's
    ///              gap under the road's own horizon inside the probe's band; the
    ///              lens as far over the roof; the road showing over the roof
    ///              from as near the nose.
    ///   CURVES     a sag into +8% and a crest into -8% (vertical curves of
    ///              80 m at 100 km/h, 200 m at 200 km/h - K 10 and 25, sharper
    ///              than a road is built for either speed): the worst frame of
    ///              the transition. The lens may never come within HALF its
    ///              level-road margin of the roof, and on the sag the road must
    ///              keep showing over the roof.
    /// Frames of each are written to Screenshots\CamFrame\cf_play_*.png.
    ///
    ///   tools\camframe-play-check.ps1  ->  PSXRacing_camframe_play_check.txt
    /// </summary>
    public static class CamFramePlayCheck
    {
        internal static StringBuilder log;
        internal static int failures;
        internal const string OutFile = "PSXRacing_camframe_play_check.txt";
        /// <summary>The shells' vertices, read in EDIT mode: the body meshes
        /// import with Read/Write off, and play mode refuses to hand them over
        /// (the first run measured every car as zero wide). Kept across the
        /// play-mode switch, which reloads no domain here.</summary>
        internal static readonly Dictionary<Mesh, Vector3[]> Verts = new Dictionary<Mesh, Vector3[]>();

        [MenuItem("PSX Racing/Check Camera Framing (play mode)")]
        public static void Run()
        {
            log = new StringBuilder();
            failures = 0;

            string id = System.Environment.GetEnvironmentVariable("PSX_CAMPLAY_VENUE");
            if (string.IsNullOrEmpty(id)) id = "DragQuarter";
            int index = -1;
            for (int i = 0; i < TrackCatalog.Count; i++)
                if (TrackCatalog.At(i).id == id) index = i;

            var scenes = EditorBuildSettings.scenes;
            int s = index >= 0 ? TrackCatalog.SceneIndex(index) : -1;
            if (s < 0 || s >= scenes.Length || !File.Exists(scenes[s].path))
            {
                Check(false, "the venue " + id + " is built");
                Finish();
                EditorApplication.Exit(1);
                return;
            }

            EditorSceneManager.OpenScene(scenes[s].path);
            RaceHandoff.ClearAll();
            RaceHandoff.FromLifeSim = true;
            RaceHandoff.TrackIndex = index;
            // The FD: the owner's car and the reference the shares are quoted
            // at — or the first catalog car on the shell PSX_CAMPLAY_SHELL
            // names (the tightest roof margins in the library are the Supra's
            // and the hatchbacks', tools\camframe-probe.ps1).
            CarSpec fd = null;
            var cars = CarCatalog.All;
            string shell = System.Environment.GetEnvironmentVariable("PSX_CAMPLAY_SHELL");
            if (!string.IsNullOrEmpty(shell))
            {
                foreach (var c in cars)
                    if (CarModelLibrary.KeyFor(c) == shell) { fd = c; break; }
                if (fd == null)
                {
                    Check(false, "a catalog car wears the shell " + shell);
                    Finish();
                    EditorApplication.Exit(1);
                    return;
                }
            }
            else
                foreach (var c in cars)
                    if (c.name != null && c.name.Contains("RX-7") && c.name.Contains("(FD")) { fd = c; break; }
            if (fd != null && cars.Count > 2)
            {
                RaceHandoff.CarSpecId = fd.id;
                RaceHandoff.OpponentSpecIds = (cars[1] != fd ? cars[1] : cars[2]).id;
                RaceHandoff.OpponentSkills = "1.0";
            }
            Note("venue " + id + ", car " + (fd != null ? fd.name : "(scene default)"));
            Verts.Clear();
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) continue;
                // The raised-lamp body too: a race with its lights on drives a
                // pop-up shell on lampsUpMesh, and without its vertices the car
                // measured as its wheels alone (roof 0.56 m, 33 false FAILs).
                foreach (var mesh in new[] { def.bodyMesh, def.lampsUpMesh, def.wheelMesh })
                    if (mesh != null && !Verts.ContainsKey(mesh)) Verts[mesh] = mesh.vertices;
            }

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnState;
            new GameObject("CamFramePlayCheckRunner").AddComponent<CamFramePlayRunner>();
        }

        internal static void Check(bool ok, string what, object got = null)
        {
            if (!ok) failures++;
            log.AppendLine("  " + (ok ? "ok   " : "FAIL ") + what + (got != null ? "  [" + got + "]" : ""));
        }

        internal static void Note(string what) => log.AppendLine("  note " + what);

        internal static void Finish()
        {
            log.AppendLine(failures == 0 ? "THE RUNNING CAMERA SITS WHERE THE RIG SAYS, LEVEL AND ON A GRADE." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutFile), log.ToString());
            Debug.Log("[CamPlay]\n" + log);
        }
    }

    /// <summary>A road for the car to be driven along: level, a straight
    /// grade from its origin (<see cref="L"/> 0), or level to
    /// <see cref="x0"/> and then a parabolic vertical curve <see cref="L"/>
    /// long into grade <see cref="g"/>. x is horizontal metres along
    /// <see cref="dir"/>.</summary>
    sealed class PlayRoad
    {
        public Vector3 origin, dir, right;
        public float g, x0, L;
        public string name;

        public float Y(float x)
        {
            if (L <= 0f) return g * x;
            if (x < x0) return 0f;
            if (x < x0 + L) { float u = x - x0; return g * u * u / (2f * L); }
            return g * L * 0.5f + g * (x - x0 - L);
        }

        public float Slope(float x)
        {
            if (L <= 0f) return g;
            if (x < x0) return 0f;
            if (x < x0 + L) return g * (x - x0) / L;
            return g;
        }

        public Vector3 Pos(float x) => origin + dir * x + Vector3.up * Y(x);
        public Vector3 Fwd(float x) => (dir + Vector3.up * Slope(x)).normalized;
        public Vector3 Up(float x) => Vector3.Cross(Fwd(x), right);
        public Quaternion Rot(float x) => Quaternion.LookRotation(Fwd(x), Up(x));
    }

    /// <summary>Runs AFTER everything else (ChaseCamera's LateUpdate
    /// included), so the measurement reads the lens and the car of the SAME
    /// frame. A coroutine resumes after Update — the car has moved, the lens
    /// has not — and WaitForEndOfFrame never comes in batch mode (no game
    /// view), which is how the first version of this check hung.</summary>
    [DefaultExecutionOrder(10000)]
    public class CamFramePlayRunner : MonoBehaviour
    {
        CarController car;
        Camera cam;
        ChaseCamera chase;
        Vector3 dir, start;
        float holdV;
        bool driving;
        PlayRoad road;
        float xCar;
        Material roadMat;
        readonly List<GameObject> slabs = new List<GameObject>();
        readonly List<MeshFilter> bodyParts = new List<MeshFilter>();
        float roofLocal, noseLocal;
        string screen, shellKey = "car";

        // ---- a measurement request for LateUpdate ----
        System.Action pendingAction;

        /// <summary>What one frame shows, through a camera.</summary>
        struct Look
        {
            public float minX, maxX, top, clear, horizonRoad, horizonWorld, gap, roadFrom, contact;
            public float W => maxX - minX;
        }

        const float RoadMax = 400f;
        const float Lift = 30f;

        /// <summary>Level-road values per view and speed, the graded runs'
        /// reference.</summary>
        readonly Dictionary<string, Look> level = new Dictionary<string, Look>();
        readonly Dictionary<ChaseCamera.View, float> restW = new Dictionary<ChaseCamera.View, float>();
        readonly StringBuilder table = new StringBuilder();

        void LateUpdate()
        {
            if (pendingAction == null) return;
            var a = pendingAction;
            pendingAction = null;
            a();
        }

        /// <summary>The car rides the road kinematically at the held speed,
        /// and its forward speed is published the way its own FixedUpdate
        /// would, so the rig reads exactly what it reads in a race.</summary>
        void Update()
        {
            if (!driving || car == null || road == null) return;
            float s = road.Slope(xCar);
            xCar += holdV * Time.deltaTime / Mathf.Sqrt(1f + s * s);
            car.transform.SetPositionAndRotation(road.Pos(xCar), road.Rot(xCar));
            car.forwardSpeed = holdV;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            yield return new WaitForFixedUpdate();
            // The race hand-off puts the catalog car's shell on during Start.
            for (int i = 0; i < 20; i++) yield return null;

            cam = Camera.main;
            chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            car = RaceManager.Instance != null ? RaceManager.Instance.playerCar : null;
            CamFramePlayCheck.Check(cam != null && chase != null && car != null, "the race has a chase camera and a player");
            if (cam == null || chase == null || car == null) { End(); yield break; }

            var body = car.GetComponent<CarBody>();
            if (body != null && !string.IsNullOrEmpty(body.modelKey)) shellKey = body.modelKey;
            CamFramePlayCheck.Note(string.Format(CultureInfo.InvariantCulture, "shell {0}, dials L {1} R {2}; positionLag {3}/s, lagClampM {4} m",
                body != null ? body.modelKey : "?",
                GaugeCluster.TachCircle.ToString("0.000"), GaugeCluster.SpeedoCircle.ToString("0.000"),
                chase.positionLag, chase.lagClampM));

            Time.captureDeltaTime = 1f / 60f;
            car.enabled = false;
            if (car.Body != null)
            {
                car.Body.isKinematic = true;
                car.Body.interpolation = RigidbodyInterpolation.None;
            }
            Vector3 f = car.transform.forward; f.y = 0f;
            dir = f.normalized;
            start = car.transform.position;
            car.transform.SetPositionAndRotation(start, Quaternion.LookRotation(dir, Vector3.up));

            // The strip's own material for the laid road, so the frames read.
            if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                var r = hit.collider.GetComponent<MeshRenderer>();
                if (r != null) roadMat = r.sharedMaterial;
            }

            // The car's parts that draw (glows, smoke and the blob shadow out),
            // and its roof and nose in its own frame.
            roofLocal = -1e9f; noseLocal = -1e9f;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                if (r.sharedMaterial != null && r.sharedMaterial.renderQueue > 2500) continue;
                if (!CamFramePlayCheck.Verts.TryGetValue(mf.sharedMesh, out var verts)) continue;
                bodyParts.Add(mf);
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    Vector3 l = car.transform.InverseTransformPoint(m.MultiplyPoint3x4(v));
                    roofLocal = Mathf.Max(roofLocal, l.y);
                    noseLocal = Mathf.Max(noseLocal, l.z);
                }
            }
            CamFramePlayCheck.Note(string.Format(CultureInfo.InvariantCulture, "roof {0:0.000} m, nose {1:0.000} m forward of the origin", roofLocal, noseLocal));

            // The game draws into its own low-res framebuffer (PSXCameraOutput);
            // this check points the camera at one of a chosen SHAPE instead.
            foreach (var o in Object.FindObjectsByType<PSXCameraOutput>(FindObjectsInactive.Exclude)) o.enabled = false;

            table.AppendLine("screen | case | lens over roof m | roof gap under road horizon (16:9 terms) | road over roof from m past nose | W | contact | world horizon | road horizon");
            foreach (var scr in new[] { (w: 854, h: 480, name: "16x9"), (w: 1040, h: 480, name: "19.5x9") })
            {
                screen = scr.name;
                var rt = new RenderTexture(scr.w, scr.h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
                rt.Create();
                cam.targetTexture = rt;
                cam.aspect = scr.w / (float)scr.h;
                level.Clear();
                restW.Clear();
                CamFramePlayCheck.Note(string.Format(CultureInfo.InvariantCulture, "==== SCREEN {0}: {1}x{2} (aspect {3:0.000}) ====", scr.name, scr.w, scr.h, cam.aspect));

                // ---- LEVEL ----
                foreach (var view in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Close })
                    foreach (float kmh in new[] { 0f, 100f, 200f })
                    {
                        var r = new PlayRoad { origin = start, dir = dir, right = Vector3.Cross(Vector3.up, dir), name = "level" };
                        ClearSlabs();
                        yield return Drive(view, r, kmh, 150);
                        bool done = false;
                        pendingAction = () => { MeasureLevel(view, kmh, r); done = true; };
                        while (!done) yield return null;
                    }

                // ---- STEADY GRADES ----
                foreach (var view in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Close })
                    foreach (float g in new[] { 0.07f, -0.07f })
                        foreach (float kmh in new[] { 100f, 200f })
                        {
                            var r = new PlayRoad
                            {
                                origin = start + Vector3.up * Lift, dir = dir, right = Vector3.Cross(Vector3.up, dir), g = g,
                                name = (g > 0 ? "+" : "") + (g * 100f).ToString("0", CultureInfo.InvariantCulture) + "%",
                            };
                            LayRoad(r, -60f, kmh / 3.6f * 3f + RoadMax + 20f);
                            yield return Drive(view, r, kmh, 180);
                            bool done = false;
                            pendingAction = () => { MeasureGrade(view, kmh, r); done = true; };
                            while (!done) yield return null;
                        }

                // ---- VERTICAL CURVES ----
                foreach (var view in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Close })
                    foreach (var c in new[] { (g: 0.08f, L: 80f, kmh: 100f), (g: -0.08f, L: 80f, kmh: 100f),
                                              (g: 0.08f, L: 200f, kmh: 200f), (g: -0.08f, L: 200f, kmh: 200f) })
                    {
                        float v = c.kmh / 3.6f;
                        var r = new PlayRoad
                        {
                            origin = start + Vector3.up * Lift, dir = dir, right = Vector3.Cross(Vector3.up, dir),
                            g = c.g, L = c.L, x0 = v * 2.5f,
                            name = (c.g > 0 ? "sag into +8%" : "crest into -8%") + " over " + c.L.ToString("0", CultureInfo.InvariantCulture) + " m",
                        };
                        yield return Curve(view, r, c.kmh);
                    }

                cam.targetTexture = null;
                cam.ResetAspect();
                rt.Release();
                Object.Destroy(rt);
            }
            driving = false;
            ClearSlabs();
            CamFramePlayCheck.log.AppendLine("---- TABLE ----");
            CamFramePlayCheck.log.Append(table);
            End();
        }

        /// <summary>Put the car at the start of the road in this view at this
        /// speed and drive it for <paramref name="frames"/> frames.</summary>
        IEnumerator Drive(ChaseCamera.View view, PlayRoad r, float kmh, int frames)
        {
            ChaseCamera.PreviewView(view);
            road = r;
            xCar = 0f;
            car.transform.SetPositionAndRotation(r.Pos(0f), r.Rot(0f));
            holdV = kmh / 3.6f;
            driving = true;
            for (int i = 0; i < frames; i++) yield return null;
        }

        IEnumerator Curve(ChaseCamera.View view, PlayRoad r, float kmh)
        {
            var ci = CultureInfo.InvariantCulture;
            float xEnd = r.x0 + r.L + 150f;
            LayRoad(r, -60f, xEnd + RoadMax + 20f);
            ChaseCamera.PreviewView(view);
            road = r;
            xCar = 0f;
            car.transform.SetPositionAndRotation(r.Pos(0f), r.Rot(0f));
            holdV = kmh / 3.6f;
            driving = true;
            float minClear = 1e9f, maxFrom = -1e9f, minGap = 1e9f, atMinClear = 0f, atMaxFrom = 0f;
            bool shotMid = false, shotEnd = false;
            string tag = view.ToString().ToUpper() + " " + kmh.ToString("0", ci) + " km/h " + r.name;
            string file = "cf_play_" + shellKey + "_" + screen + "_" + view.ToString().ToLower() + "_" + (r.g > 0 ? "sag" : "crest") + "_" + kmh.ToString("0", ci);
            while (xCar < xEnd)
            {
                bool done = false;
                pendingAction = () =>
                {
                    if (xCar >= r.x0 - 10f)
                    {
                        var k = Read(cam, r, xCar);
                        if (k.clear < minClear) { minClear = k.clear; atMinClear = xCar - r.x0; }
                        if (k.roadFrom > maxFrom) { maxFrom = k.roadFrom; atMaxFrom = xCar - r.x0; }
                        minGap = Mathf.Min(minGap, k.gap);
                        if (!shotMid && xCar >= r.x0 + r.L * 0.5f) { Png(file + "_mid"); shotMid = true; }
                        if (!shotEnd && xCar >= r.x0 + r.L) { Png(file + "_end"); shotEnd = true; }
                    }
                    done = true;
                };
                while (!done) yield return null;
            }
            if (!level.TryGetValue(Key(view, kmh), out var lv)) yield break;
            CamFramePlayCheck.Note(string.Format(ci,
                "{0}: lens over the roof at worst {1:0.000} m ({2:+0;-0} m into the curve; level road {3:0.000}); road over the roof from at worst {4:0.0} m past the nose ({5:+0;-0} m in; level road {6:0.0}); roof gap under the local road horizon at least {7:0.000}",
                tag, minClear, atMinClear, lv.clear, maxFrom, atMaxFrom, lv.roadFrom, minGap));
            table.AppendLine(string.Format(ci, "{0} | {1} worst | {2:0.000} (level {3:0.000}) | - | {4:0.0} (level {5:0.0}) | - | - | - | -",
                screen, tag, minClear, lv.clear, maxFrom, lv.roadFrom));
            CamFramePlayCheck.Check(minClear >= 0.5f * lv.clear,
                screen + " " + tag + ": the lens never comes within half its level-road margin of the roof",
                minClear.ToString("0.000", ci) + " m vs " + (0.5f * lv.clear).ToString("0.000", ci));
            if (r.g > 0f)
                CamFramePlayCheck.Check(maxFrom <= lv.roadFrom * 1.25f + 1f,
                    screen + " " + tag + ": and the road keeps showing over the roof (level road x1.25 + 1 m at most)",
                    maxFrom.ToString("0.0", ci) + " m vs " + (lv.roadFrom * 1.25f + 1f).ToString("0.0", ci));
        }

        void End()
        {
            Time.captureDeltaTime = 0f;
            CamFramePlayCheck.Finish();
            EditorApplication.Exit(CamFramePlayCheck.failures == 0 ? 0 : 1);
        }

        static string Key(ChaseCamera.View v, float kmh) => v + "@" + kmh.ToString("0", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------
        //  Reading a frame
        // ------------------------------------------------------------------
        /// <summary>The car and the road through camera <paramref name="c"/>,
        /// with the car at <paramref name="x"/> on road <paramref name="r"/>.</summary>
        Look Read(Camera c, PlayRoad r, float x)
        {
            var k = new Look { minX = 1e9f, maxX = -1e9f, top = -1e9f };
            foreach (var mf in bodyParts)
            {
                if (!CamFramePlayCheck.Verts.TryGetValue(mf.sharedMesh, out var verts)) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    Vector3 vp = c.WorldToViewportPoint(m.MultiplyPoint3x4(v));
                    if (vp.z <= 0f) continue;
                    k.minX = Mathf.Min(k.minX, vp.x); k.maxX = Mathf.Max(k.maxX, vp.x);
                    k.top = Mathf.Max(k.top, vp.y);
                }
            }
            k.clear = car.transform.InverseTransformPoint(c.transform.position).y - roofLocal;
            k.horizonRoad = c.WorldToViewportPoint(c.transform.position + r.Fwd(x) * 100000f).y;
            k.horizonWorld = c.WorldToViewportPoint(c.transform.position + r.dir * 100000f).y;
            k.gap = k.horizonRoad - k.top;
            k.contact = c.WorldToViewportPoint(car.transform.TransformPoint(new Vector3(0f, 0f, -car.wheelbase * 0.5f))).y;
            // The road straight ahead: the first point of it that shows OVER
            // the car's roof line and is not behind a nearer brow of the road.
            float xn = x + noseLocal, runMax = -1e9f;
            k.roadFrom = RoadMax;
            for (float d = 0f; d <= RoadMax; d += 0.25f)
            {
                Vector3 vp = c.WorldToViewportPoint(r.Pos(xn + d));
                if (vp.z <= 0f) continue;
                bool shows = vp.y > k.top && vp.y >= runMax;
                runMax = Mathf.Max(runMax, vp.y);
                if (shows) { k.roadFrom = d; break; }
            }
            return k;
        }

        /// <summary>The same frame read through the live lens moved to
        /// another pose for a moment (nothing renders in between, and the rig
        /// keeps its own state, not the transform's).</summary>
        Look ReadAt(Vector3 pos, Quaternion rot, float vfov, PlayRoad r, float x)
        {
            var keepP = cam.transform.position; var keepR = cam.transform.rotation; float keepF = cam.fieldOfView;
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = vfov;
            var k = Read(cam, r, x);
            cam.transform.SetPositionAndRotation(keepP, keepR);
            cam.fieldOfView = keepF;
            return k;
        }

        /// <summary>A gap measured in THIS frame's height, in the 16:9 frame
        /// terms the rig's band is written in: the same angle over the 16:9
        /// lens's half-height.</summary>
        float Gap16(float gap, ChaseCamera.View view, float kmh)
        {
            ChaseCamera.SteadyPose(view, ChaseCamera.RefAspect, kmh / 3.6f, chase.speedFullMps, car.transform,
                                   ChaseCamera.FrameOf(car.gameObject), ChaseCamera.LiveDials(),
                                   out _, out _, out float v16, out _);
            return gap * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(v16 * 0.5f * Mathf.Deg2Rad);
        }

        // ------------------------------------------------------------------
        //  Level road
        // ------------------------------------------------------------------
        void MeasureLevel(ChaseCamera.View view, float kmh, PlayRoad r)
        {
            var ci = CultureInfo.InvariantCulture;
            var t = car.transform;
            Vector3 fwd = t.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // ---- where the running camera IS ----
            Vector3 rel = cam.transform.position - t.position;
            float back = -Vector3.Dot(rel, fwd), up = rel.y, side = Vector3.Dot(rel, right);

            // ---- where the rig says it should be ----
            ChaseCamera.SteadyPose(view, cam.aspect, kmh / 3.6f, chase.speedFullMps, t, ChaseCamera.FrameOf(car.gameObject),
                                   ChaseCamera.LiveDials(), out Vector3 pos, out Quaternion rot, out float vfov, out float shift);
            Vector3 relS = pos - t.position;
            float backS = -Vector3.Dot(relS, fwd), upS = relS.y;
            float aimErr = Quaternion.Angle(cam.transform.rotation, rot);
            float fovErr = cam.fieldOfView - vfov;

            // What the OLD rig's follow lag added in this state: the steady
            // trail v / positionLag, pinned at lagClampM.
            float oldTrail = Mathf.Min(kmh / 3.6f / Mathf.Max(chase.positionLag, 0.01f), chase.lagClampM);

            var k = Read(cam, r, xCar);
            level[Key(view, kmh)] = k;
            float centre = (k.minX + k.maxX) * 0.5f;

            string tag = screen + " " + view.ToString().ToUpper() + " " + kmh.ToString("0", ci) + " km/h level";
            CamFramePlayCheck.Note(string.Format(ci,
                "{0}: lens {1:0.000} m back of the origin (rig {2:0.000}), {3:0.000} up (rig {4:0.000}), {5:+0.000;-0.000} m sideways; aim off by {6:0.00} deg; FOV {7:0.00} (rig {8:0.00}); " +
                "W {9:0.000} centred at {10:0.000} (shift {11:+0.000;-0.000}), contact {12:0.000}, horizon {13:0.000}; lens {14:0.000} m over the roof, roof gap {15:0.000} ({16:0.000} in 16:9 terms), road over the roof from {17:0.0} m past the nose; the old follow lag would have added {18:0.00} m",
                tag, back, backS, up, upS, side, aimErr, cam.fieldOfView, vfov, k.W, centre, shift, k.contact, k.horizonWorld,
                k.clear, k.gap, Gap16(k.gap, view, kmh), k.roadFrom, oldTrail));
            table.AppendLine(string.Format(ci, "{0} | {1} {2} km/h level | {3:0.000} | {4:0.000} | {5:0.0} | {6:0.000} | {7:0.000} | {8:0.000} | {9:0.000}",
                screen, view.ToString().ToUpper(), kmh.ToString("0", ci), k.clear, Gap16(k.gap, view, kmh), k.roadFrom, k.W, k.contact, k.horizonWorld, k.horizonRoad));
            CamFramePlayCheck.Check(Mathf.Abs(back - backS) < 0.05f && Mathf.Abs(up - upS) < 0.03f && Mathf.Abs(side) < 0.03f,
                tag + ": the running lens stands where SteadyPose puts it (within 5 cm)",
                (back - backS).ToString("+0.000;-0.000", ci) + " m along, " + (up - upS).ToString("+0.000;-0.000", ci) + " m up");
            CamFramePlayCheck.Check(aimErr < 0.35f && Mathf.Abs(fovErr) < 0.05f,
                tag + ": and looks where it says, through the lens it says",
                aimErr.ToString("0.00", ci) + " deg, FOV " + fovErr.ToString("+0.00;-0.00", ci));
            CamFramePlayCheck.Check(Mathf.Abs(centre - (0.5f + shift)) < 0.01f,
                tag + ": the car is centred where the lens shift puts it", centre.ToString("0.000", ci));
            if (kmh < 1f)
            {
                // The share the rig fits: the view's share x real width / 1.76 m,
                // times 16:9 / aspect on a screen narrower than 16:9 (Hor+).
                var frame = ChaseCamera.FrameOf(car.gameObject);
                float want = ChaseCamera.RigFor(view).share * frame.widthM / ChaseCamera.RefWidthM
                             * (cam.aspect < ChaseCamera.RefAspect ? ChaseCamera.RefAspect / cam.aspect : 1f);
                restW[view] = k.W;
                CamFramePlayCheck.Check(Mathf.Abs(k.W - want) < 0.015f,
                    tag + ": the car fills its share of the frame through the live lens",
                    k.W.ToString("0.000", ci) + " vs " + want.ToString("0.000", ci));
            }
            else if (restW.TryGetValue(view, out float w0))
                CamFramePlayCheck.Check(k.W >= w0 * 0.90f,
                    tag + ": and shrinks by 10% at most at speed (the old rig: a third)",
                    ((1f - k.W / w0) * 100f).ToString("0.0", ci) + "%");
            // The roof's gap under the horizon: at rest inside the band the rig
            // SOLVES to (ChaseRig.gapLo/gapHi); at every speed inside the
            // reference band the framing probe applies (5-16% of a 16:9 frame).
            var q = ChaseCamera.RigFor(view);
            float g16 = Gap16(k.gap, view, kmh);
            if (kmh < 1f)
                CamFramePlayCheck.Check(g16 >= q.gapLo - 0.005f && g16 <= q.gapHi + 0.005f,
                    tag + ": the roof's gap under the horizon is where the rig solves it",
                    g16.ToString("0.000", ci) + " in " + q.gapLo.ToString("0.00", ci) + "-" + q.gapHi.ToString("0.00", ci));
            CamFramePlayCheck.Check(g16 >= CamFrameProbe.RoofGapLo && g16 <= CamFrameProbe.RoofGapHi,
                tag + ": the roof's gap under the horizon is inside the reference band (the probe's)",
                g16.ToString("0.000", ci) + " in " + CamFrameProbe.RoofGapLo.ToString("0.00", ci) + "-" + CamFrameProbe.RoofGapHi.ToString("0.00", ci));
            if (kmh >= 99f) Png("cf_play_" + shellKey + "_" + screen + "_" + view.ToString().ToLower() + "_level_" + kmh.ToString("0", ci));
        }

        // ------------------------------------------------------------------
        //  A steady grade
        // ------------------------------------------------------------------
        void MeasureGrade(ChaseCamera.View view, float kmh, PlayRoad r)
        {
            var ci = CultureInfo.InvariantCulture;
            var t = car.transform;
            string tag = screen + " " + view.ToString().ToUpper() + " " + kmh.ToString("0", ci) + " km/h " + r.name;
            if (!level.TryGetValue(Key(view, kmh), out var lv)) return;

            // The rig's contract on a steady grade: the level road's pose,
            // turned about the car onto the grade.
            ChaseCamera.SteadyPose(view, cam.aspect, kmh / 3.6f, chase.speedFullMps, t, ChaseCamera.FrameOf(car.gameObject),
                                   ChaseCamera.LiveDials(), out Vector3 flatPos, out Quaternion flatRot, out float vfov, out _);
            Quaternion tilt = Quaternion.FromToRotation(r.dir, r.Fwd(xCar));
            Vector3 wantPos = t.position + tilt * (flatPos - t.position);
            Quaternion wantRot = tilt * flatRot;
            Vector3 err = t.InverseTransformVector(cam.transform.position - wantPos);
            float aimErr = Quaternion.Angle(cam.transform.rotation, wantRot);

            var k = Read(cam, r, xCar);
            // And what a LEVEL rig would show here — the level pose with the
            // follow lag's vertical trail led out too, and nothing turned.
            var lvl = ReadAt(flatPos, flatRot, vfov, r, xCar);
            float g16 = Gap16(k.gap, view, kmh), lg16 = Gap16(lvl.gap, view, kmh), l0 = Gap16(lv.gap, view, kmh);

            CamFramePlayCheck.Note(string.Format(ci,
                "{0}: lens off the tilted level pose by {1:+0.000;-0.000} m up, {2:+0.000;-0.000} m along (car frame), aim {3:0.00} deg; " +
                "lens {4:0.000} m over the roof (level road {5:0.000}); roof gap under the road's horizon {6:0.000} (level road {7:0.000}); road over the roof from {8:0.0} m past the nose (level road {9:0.0}); " +
                "W {10:0.000}, contact {11:0.000}, world horizon {12:0.000}, road horizon {13:0.000}. " +
                "A LEVEL rig here (vertical trail led out, not turned): lens {14:0.000} m over the roof, gap {15:0.000}, road from {16:0.0} m",
                tag, err.y, err.z, aimErr, k.clear, lv.clear, g16, l0, k.roadFrom, lv.roadFrom,
                k.W, k.contact, k.horizonWorld, k.horizonRoad, lvl.clear, lg16, lvl.roadFrom));
            table.AppendLine(string.Format(ci, "{0} | {1} {2} km/h {3} | {4:0.000} | {5:0.000} | {6:0.0} | {7:0.000} | {8:0.000} | {9:0.000} | {10:0.000}",
                screen, view.ToString().ToUpper(), kmh.ToString("0", ci), r.name, k.clear, g16, k.roadFrom, k.W, k.contact, k.horizonWorld, k.horizonRoad));
            table.AppendLine(string.Format(ci, "{0} | {1} {2} km/h {3} LEVEL RIG (what-if) | {4:0.000} | {5:0.000} | {6:0.0} | {7:0.000} | {8:0.000} | {9:0.000} | {10:0.000}",
                screen, view.ToString().ToUpper(), kmh.ToString("0", ci), r.name, lvl.clear, lg16, lvl.roadFrom, lvl.W, lvl.contact, lvl.horizonWorld, lvl.horizonRoad));

            // The rig's own read of the grade, and the tools' steady pose on it.
            float trueDeg = Mathf.Atan(r.g) * Mathf.Rad2Deg;
            ChaseCamera.SteadyPose(view, cam.aspect, kmh / 3.6f, chase.speedFullMps, t, ChaseCamera.FrameOf(car.gameObject),
                                   ChaseCamera.LiveDials(), out Vector3 gPos, out Quaternion gRot, out _, out _, trueDeg);
            CamFramePlayCheck.Check(Mathf.Abs(chase.GradeDeg - trueDeg) < 0.1f,
                tag + ": the rig reads the road's grade off the car's travel (0.1 deg)",
                chase.GradeDeg.ToString("0.00", ci) + " vs " + trueDeg.ToString("0.00", ci));
            CamFramePlayCheck.Check((gPos - wantPos).magnitude < 0.005f && Quaternion.Angle(gRot, wantRot) < 0.05f,
                tag + ": SteadyPose on the grade is the level pose turned onto it (the tools' pose)",
                (gPos - wantPos).magnitude.ToString("0.0000", ci) + " m, " + Quaternion.Angle(gRot, wantRot).ToString("0.000", ci) + " deg");
            CamFramePlayCheck.Check(err.magnitude < 0.05f && aimErr < 0.35f,
                tag + ": the running lens stands where the level pose, turned onto the grade, puts it (5 cm, 0.35 deg)",
                err.magnitude.ToString("0.000", ci) + " m, " + aimErr.ToString("0.00", ci) + " deg");
            CamFramePlayCheck.Check(Mathf.Abs(k.clear - lv.clear) < 0.03f,
                tag + ": the lens clears the roof by its level-road margin (3 cm)",
                k.clear.ToString("0.000", ci) + " vs " + lv.clear.ToString("0.000", ci));
            CamFramePlayCheck.Check(g16 >= CamFrameProbe.RoofGapLo && g16 <= CamFrameProbe.RoofGapHi && Mathf.Abs(g16 - l0) < 0.01f,
                tag + ": the roof's gap under the ROAD's horizon is inside the reference band and the level road's (0.01)",
                g16.ToString("0.000", ci) + " in " + CamFrameProbe.RoofGapLo.ToString("0.00", ci) + "-" + CamFrameProbe.RoofGapHi.ToString("0.00", ci) + ", level " + l0.ToString("0.000", ci));
            CamFramePlayCheck.Check(k.roadFrom <= lv.roadFrom + Mathf.Max(1f, 0.1f * lv.roadFrom),
                tag + ": the road shows over the roof from as near the nose as on the level (1 m / 10%)",
                k.roadFrom.ToString("0.0", ci) + " m vs " + lv.roadFrom.ToString("0.0", ci));
            Png("cf_play_" + shellKey + "_" + screen + "_" + view.ToString().ToLower() + "_grade" + (r.g > 0 ? "up" : "down") + "_" + kmh.ToString("0", ci));
        }

        // ------------------------------------------------------------------
        //  The road and the pictures
        // ------------------------------------------------------------------
        void ClearSlabs()
        {
            foreach (var s in slabs) if (s != null) Destroy(s);
            slabs.Clear();
        }

        /// <summary>A two-lane road (6.6 m) laid along <paramref name="r"/> in
        /// 4 m slabs, for the frames. Nothing collides with it: the car is
        /// kinematic and the camera has no collision.</summary>
        void LayRoad(PlayRoad r, float from, float to)
        {
            ClearSlabs();
            for (float x = from; x < to; x += 4f)
            {
                Vector3 a = r.Pos(x), b = r.Pos(x + 4f);
                Vector3 up = Vector3.Cross((b - a).normalized, r.right);
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.name = "PlayRoad";
                var col = s.GetComponent<Collider>();
                if (col != null) Destroy(col);
                s.transform.SetPositionAndRotation((a + b) * 0.5f - up * 0.1f, Quaternion.LookRotation(b - a, up));
                s.transform.localScale = new Vector3(6.6f, 0.2f, (b - a).magnitude + 0.05f);
                var mr = s.GetComponent<MeshRenderer>();
                if (roadMat != null) mr.sharedMaterial = roadMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                slabs.Add(s);
            }
        }

        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "CamFrame");

        /// <summary>What the live camera shows now, through the game's dither,
        /// to Screenshots\CamFrame\<paramref name="name"/>.png.</summary>
        void Png(string name)
        {
            var target = cam.targetTexture;
            if (target == null) return;
            int w = target.width, h = target.height;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            rt.Create();
            StreetLights.Push(cam.transform.position, cam.transform.forward);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                var shown = PSXScreenshotTool.Dithered(rt);
                var prev = RenderTexture.active;
                RenderTexture.active = shown;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev == rt || prev == shown ? null : prev;
                if (shown != rt) { shown.Release(); Object.DestroyImmediate(shown); }
                Directory.CreateDirectory(OutDir);
                ShotSidecar.WritePng(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            cam.targetTexture = target;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
