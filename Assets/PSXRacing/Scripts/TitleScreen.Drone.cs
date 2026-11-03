using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE DRONE (owner, 2026-11-02, a drone film of the Linn Cove Viaduct in
    /// peak colour at golden hour: "This is more what I had in mind for the
    /// opening menu of the game"). The title's camera is a sequence of slow
    /// aerial shots instead of one still view, each a steady eased move with a
    /// dip to black between them, the way the film cuts:
    ///   ESTABLISH  high and far out over the valley, pushing in on the
    ///              viaduct as it curves across the mountain;
    ///   PAN        sliding along the slope at mid height, the road below;
    ///   DESCEND    dropping over the canopy toward the road, tilting down;
    ///   TOP-DOWN   straight down, the road running up the frame, following a
    ///              car through the trees (or drifting along the road);
    ///   FOLLOW     above and behind a car crossing the viaduct;
    ///   POSTCARD   the view the title had before, drifting a few metres.
    /// Every eye is held over the canopy (GroundAt + CanopyClearM) and eased
    /// onto it, never snapped. A season turns over at a cut once it is due.
    /// V cuts to the next shot. <c>-titleshots &lt;folder&gt;</c> (or
    /// PSX_TITLE_SHOTS) photographs the middle of each shot once and quits -
    /// the check of the real player.
    /// </summary>
    public partial class TitleScreen
    {
        enum ShotKind { Establish, Pan, Descend, TopDown, Follow, Postcard }
        static readonly ShotKind[] ShotOrder =
            { ShotKind.Establish, ShotKind.Pan, ShotKind.Descend, ShotKind.TopDown, ShotKind.Follow, ShotKind.Postcard };

        const float ShotS = 13f, CutS = 0.45f, DroneFov = 50f, TopDownM = 105f;
        int shotIx = -1;
        ShotKind shot;
        float shotT0, shotLen;
        Vector3 e0, e1, l0, l1, eyeNow;
        Transform follow;
        bool droneOn, cutting;
        float eyeLift;
        string shotsDir;
        readonly HashSet<int> photographed = new HashSet<int>();

        void BeginDrone()
        {
            if (cam == null || views.Count == 0) return;
            droneOn = true;
            shotsDir = System.Environment.GetEnvironmentVariable("PSX_TITLE_SHOTS");
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-titleshots") shotsDir = args[i + 1];
            NextShot();
        }

        /// <summary>The valley side of the viaduct's middle: the side the ground falls away to.</summary>
        Vector3 ValleySide(out Vector3 along, out Vector3 mid)
        {
            var path = rm.path;
            mid = path.GetPoint(im);
            along = path.GetTangent(im).normalized;
            var right = Vector3.Cross(Vector3.up, along).normalized;
            return GroundAt(mid + right * 80f) <= GroundAt(mid - right * 80f) ? right : -right;
        }

        void NextShot()
        {
            shotIx = (shotIx + 1) % ShotOrder.Length;
            shot = ShotOrder[shotIx];
            shotT0 = Time.unscaledTime;
            shotLen = ShotS;
            follow = null;
            eyeLift = 0f;
            var path = rm.path;
            var valley = ValleySide(out var along, out var mid);
            int n = Mathf.Max(1, i1 - i0);
            Vector3 P(int i) => path.GetPoint(Mathf.Clamp(i, 1, path.Count - 2));
            cam.fieldOfView = shot == ShotKind.Postcard ? Fov : DroneFov;
            switch (shot)
            {
                case ShotKind.Establish:
                    e0 = mid + valley * 640f - along * 160f + Vector3.up * 240f;
                    e1 = mid + valley * 470f + along * 40f + Vector3.up * 185f;
                    l0 = mid + Vector3.up * 30f; l1 = mid;
                    shotLen = ShotS + 3f;
                    break;
                case ShotKind.Pan:
                    e0 = P(i0 - 30) + valley * 170f + Vector3.up * 95f;
                    e1 = P(i1 + 30) + valley * 170f + Vector3.up * 95f;
                    l0 = P(i0 + n / 6); l1 = P(i1 - n / 6);
                    break;
                case ShotKind.Descend:
                    e0 = P(i0 + n / 4) + valley * 110f + Vector3.up * 150f;
                    e1 = P(i0 + n / 2) + valley * 40f + Vector3.up * 42f;
                    l0 = P(i0 + n / 2); l1 = P(i0 + (3 * n) / 4) + Vector3.up * 2f;
                    break;
                case ShotKind.TopDown:
                case ShotKind.Follow:
                    follow = CarNearViaduct();
                    e0 = P(i0) + Vector3.up * TopDownM; e1 = P(i1) + Vector3.up * TopDownM;
                    l0 = P(i0); l1 = P(i1);
                    break;
                case ShotKind.Postcard:
                    var (pe, pl) = views[view];
                    e0 = pe; e1 = pe + (pl - pe).normalized * 6f + Vector3.up * 1.5f;
                    l0 = pl; l1 = pl;
                    break;
            }
            eyeNow = e0;
            Debug.Log($"[TitleScreen] drone shot {shot} for {shotLen:0} s" + (follow != null ? " following " + follow.name : ""));
        }

        /// <summary>A car on the road coming up to (or on) the viaduct: a
        /// racer first, then traffic.</summary>
        Transform CarNearViaduct()
        {
            var path = rm.path;
            Transform best = null; int bestD = int.MaxValue;
            void Try(Transform t, float speed)
            {
                if (t == null || speed < 6f) return;
                int idx = path.NearestIndex(t.position);
                if (idx < i0 - 220 || idx > i1) return;
                int d = Mathf.Abs(idx - (i0 - 40));
                if (d < bestD) { bestD = d; best = t; }
            }
            foreach (var car in rm.allCars)
                if (car != null && car != rm.playerCar && car.GetComponent<AIDriver>() is AIDriver ai && ai.driving && !ai.ShuttingDown)
                    Try(car.transform, Mathf.Abs(car.forwardSpeed));
            if (best == null && TrafficSystem.Instance != null)
                foreach (var rb in TrafficSystem.Instance.Obstacles)
                    if (rb != null && !rb.useGravity) Try(rb.transform, rb.linearVelocity.magnitude);
            return best;
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            // a drone's move: mostly steady, eased in and out
            return Mathf.Lerp(t, t * t * (3f - 2f * t), 0.6f);
        }

        void StepDrone()
        {
            if (!droneOn || cam == null) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            float t = (Time.unscaledTime - shotT0) / shotLen;
            if (!cutting && (t >= 1f || (kb != null && kb.vKey.wasPressedThisFrame)))
            {
                cutting = true;
                Fade(1f, CutS, () =>
                {
                    if (Time.unscaledTime >= nextSeason) { nextSeason = Time.unscaledTime + SeasonS; NextSeason(); }
                    NextShot();
                    cutting = false;
                    Fade(0f, CutS * 1.4f, null);
                });
            }
            float k = Ease(t);
            Vector3 eye, look;
            Vector3 upHint = Vector3.up;
            if ((shot == ShotKind.TopDown || shot == ShotKind.Follow) && follow != null)
            {
                var path = rm.path;
                int idx = path.NearestIndex(follow.position);
                var fwd = path.GetTangent(idx).normalized;
                if (shot == ShotKind.TopDown)
                {
                    eye = follow.position + Vector3.up * (TopDownM - 15f * k) - fwd * 8f;
                    look = follow.position + fwd * 8f;
                    upHint = fwd;   // the road runs up the frame
                }
                else
                {
                    eye = follow.position - fwd * (34f - 6f * k) + Vector3.up * (24f - 4f * k);
                    look = follow.position + fwd * 30f;
                }
            }
            else
            {
                eye = Vector3.Lerp(e0, e1, k);
                look = Vector3.Lerp(l0, l1, k);
                if (shot == ShotKind.TopDown) { upHint = (l1 - l0).normalized; look = eye - Vector3.up * TopDownM + upHint * 0.01f; }
            }
            // over the canopy, eased up onto it rather than snapped
            float floor = GroundAt(eye) + CanopyClearM - eye.y;
            eyeLift = Mathf.MoveTowards(eyeLift, Mathf.Max(0f, floor), 12f * Time.unscaledDeltaTime);
            if (floor > eyeLift + 6f) eyeLift = floor - 6f;
            eye.y += eyeLift;
            eyeNow = Vector3.Lerp(eyeNow, eye, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            var dir = look - eyeNow;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.down;
            var rot = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.97f
                ? Quaternion.LookRotation(dir, upHint)
                : Quaternion.LookRotation(dir, Vector3.up);
            cam.transform.SetPositionAndRotation(eyeNow, rot);

            // the player check: each shot photographed once, mid-move
            if (!string.IsNullOrEmpty(shotsDir) && t > 0.5f && photographed.Add(shotIx))
            {
                System.IO.Directory.CreateDirectory(shotsDir);
                string file = System.IO.Path.Combine(shotsDir, $"title_{shotIx}_{shot}.png");
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log("[TitleScreen] shot photographed: " + file);
                if (photographed.Count >= ShotOrder.Length) Invoke(nameof(QuitAfterShots), 1.5f);
            }
        }

        void QuitAfterShots() => Application.Quit();
    }
}
