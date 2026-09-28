using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The chase rig IN THE RUNNING GAME (2026-09-28, the NFS U / MW pass).
    ///
    /// The framing probe (CamFrameProbe) measures ChaseCamera.SteadyPose —
    /// the pose Follow is supposed to converge on — because Follow lerps by
    /// Time.deltaTime and cannot run outside play mode. That leaves one
    /// question only the game can answer, and it is the one that hid the old
    /// rig's real framing for months: does the running camera actually SIT
    /// there at speed? The follow lag used to park it 1.2 m further back than
    /// the rig said above 22 km/h, and nothing that looked at still frames
    /// could see it.
    ///
    /// So: a race is started on a drag strip (straight and flat) in the FD,
    /// the car is driven kinematically down it at a held 0, 100 and 200 km/h
    /// in CHASE and CLOSE, the clock is stepped at a fixed 60 Hz, and after
    /// two and a half seconds the live camera is compared with SteadyPose for
    /// the same car, speed, screen and HUD — position, aim and lens — and the
    /// car's width share, contact line and horizon are read THROUGH THE LIVE
    /// CAMERA (its own projection matrix, lens shift and all).
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
            // The FD: the owner's car and the reference the shares are quoted at.
            CarSpec fd = null;
            var cars = CarCatalog.All;
            foreach (var c in cars)
                if (c.name != null && c.name.Contains("RX-7") && c.name.Contains("(FD")) { fd = c; break; }
            if (fd != null && cars.Count > 2)
            {
                RaceHandoff.CarSpecId = fd.id;
                RaceHandoff.OpponentSpecIds = cars[1].id;
                RaceHandoff.OpponentSkills = "1.0";
            }
            Note("venue " + id + ", car " + (fd != null ? fd.name : "(scene default)"));
            Verts.Clear();
            foreach (var m in CarModelLibrary.Models)
            {
                var def = CarModelLibrary.Load(m.key);
                if (def == null) continue;
                foreach (var mesh in new[] { def.bodyMesh, def.wheelMesh })
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
            log.AppendLine(failures == 0 ? "THE RUNNING CAMERA SITS WHERE THE RIG SAYS." : failures + " FAILURE(S).");
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutFile), log.ToString());
            Debug.Log("[CamPlay]\n" + log);
        }
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
        Vector3 dir;
        float holdV;
        bool driving;
        bool pending;
        ChaseCamera.View pendingView;
        float pendingKmh;
        readonly Dictionary<ChaseCamera.View, float> restW = new Dictionary<ChaseCamera.View, float>();

        void LateUpdate()
        {
            if (!pending) return;
            pending = false;
            Measure(pendingView, pendingKmh);
        }

        /// <summary>The car rides down the strip kinematically at the held
        /// speed, and its forward speed is published the way its own
        /// FixedUpdate would, so the rig reads exactly what it reads in a race.</summary>
        void Update()
        {
            if (!driving || car == null) return;
            car.transform.position += dir * holdV * Time.deltaTime;
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
            CamFramePlayCheck.Note(string.Format(CultureInfo.InvariantCulture, "shell {0}, frame {1}x{2} (aspect {3:0.000}), dials L {4} R {5}",
                body != null ? body.modelKey : "?", cam.pixelWidth, cam.pixelHeight, cam.aspect,
                GaugeCluster.TachCircle.ToString("0.000"), GaugeCluster.SpeedoCircle.ToString("0.000")));

            Time.captureDeltaTime = 1f / 60f;
            car.enabled = false;
            if (car.Body != null)
            {
                car.Body.isKinematic = true;
                car.Body.interpolation = RigidbodyInterpolation.None;
            }
            Vector3 f = car.transform.forward; f.y = 0f;
            dir = f.normalized;
            Vector3 start = car.transform.position;
            Quaternion level = Quaternion.LookRotation(dir, Vector3.up);

            foreach (var view in new[] { ChaseCamera.View.Chase, ChaseCamera.View.Close })
                foreach (float kmh in new[] { 0f, 100f, 200f })
                {
                    ChaseCamera.PreviewView(view);
                    car.transform.SetPositionAndRotation(start, level);
                    holdV = kmh / 3.6f;
                    driving = true;
                    for (int i = 0; i < 150; i++) yield return null;   // 2.5 s of game time
                    pendingView = view; pendingKmh = kmh; pending = true;
                    while (pending) yield return null;
                }
            driving = false;
            End();
        }

        void End()
        {
            Time.captureDeltaTime = 0f;
            CamFramePlayCheck.Finish();
            EditorApplication.Exit(CamFramePlayCheck.failures == 0 ? 0 : 1);
        }

        void Measure(ChaseCamera.View view, float kmh)
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

            // ---- the framing, through the live camera ----
            float minX = 1e9f, maxX = -1e9f;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                if (r.sharedMaterial != null && r.sharedMaterial.renderQueue > 2500) continue;   // glows, smoke, blob
                var m = mf.transform.localToWorldMatrix;
                if (!CamFramePlayCheck.Verts.TryGetValue(mf.sharedMesh, out var verts)) continue;
                foreach (var v in verts)
                {
                    Vector3 vp = cam.WorldToViewportPoint(m.MultiplyPoint3x4(v));
                    if (vp.z <= 0f) continue;
                    minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                }
            }
            float contact = cam.WorldToViewportPoint(t.TransformPoint(new Vector3(0f, 0f, -car.wheelbase * 0.5f))).y;
            float horizon = cam.WorldToViewportPoint(cam.transform.position + fwd * 100000f).y;
            float centre = (minX + maxX) * 0.5f;

            string tag = view.ToString().ToUpper() + " " + kmh.ToString("0", ci) + " km/h";
            CamFramePlayCheck.Note(string.Format(ci,
                "{0}: lens {1:0.000} m back of the origin (rig {2:0.000}), {3:0.000} up (rig {4:0.000}), {5:+0.000;-0.000} m sideways; aim off by {6:0.00} deg; FOV {7:0.00} (rig {8:0.00}); " +
                "W {9:0.000} centred at {10:0.000} (shift {11:+0.000;-0.000}), contact {12:0.000}, horizon {13:0.000}; the old follow lag would have added {14:0.00} m",
                tag, back, backS, up, upS, side, aimErr, cam.fieldOfView, vfov, maxX - minX, centre, shift, contact, horizon, oldTrail));
            CamFramePlayCheck.Check(Mathf.Abs(back - backS) < 0.05f && Mathf.Abs(up - upS) < 0.03f && Mathf.Abs(side) < 0.03f,
                tag + ": the running lens stands where SteadyPose puts it (within 5 cm)",
                (back - backS).ToString("+0.000;-0.000", ci) + " m along, " + (up - upS).ToString("+0.000;-0.000", ci) + " m up");
            CamFramePlayCheck.Check(aimErr < 0.35f && Mathf.Abs(fovErr) < 0.05f,
                tag + ": and looks where it says, through the lens it says",
                aimErr.ToString("0.00", ci) + " deg, FOV " + fovErr.ToString("+0.00;-0.00", ci));
            CamFramePlayCheck.Check(Mathf.Abs(centre - (0.5f + shift)) < 0.01f,
                tag + ": the car is centred where the lens shift puts it", centre.ToString("0.000", ci));
            float w = maxX - minX;
            if (kmh < 1f)
            {
                // The share the rig fits: the view's share x real width / 1.76 m,
                // times 16:9 / aspect on a screen narrower than 16:9 (Hor+).
                var frame = ChaseCamera.FrameOf(car.gameObject);
                float want = ChaseCamera.RigFor(view).share * frame.widthM / ChaseCamera.RefWidthM
                             * (cam.aspect < ChaseCamera.RefAspect ? ChaseCamera.RefAspect / cam.aspect : 1f);
                restW[view] = w;
                CamFramePlayCheck.Check(Mathf.Abs(w - want) < 0.015f,
                    tag + ": the car fills its share of the frame through the live lens",
                    w.ToString("0.000", ci) + " vs " + want.ToString("0.000", ci));
            }
            else if (restW.TryGetValue(view, out float w0))
                CamFramePlayCheck.Check(w >= w0 * 0.90f,
                    tag + ": and shrinks by 10% at most at speed (the old rig: a third)",
                    ((1f - w / w0) * 100f).ToString("0.0", ci) + "%");
        }
    }
}
