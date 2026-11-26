using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using PSXRacing.OnFoot;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE REPLAYFX ROUND (2026-11-26; the owner, over a night replay: "During
    /// replays I can see an illuminated silhouette of cars through objects. And
    /// please tone down the shakiness of the cameras. Maybe shakiness can vary
    /// between holders randomly, but currently they are all very shaky").
    ///  - CARSEEN: a car behind a pole and a leaf card, the player's SSAO on;
    ///    the paint's keep must not reach the pole or the leaves (CarDots.Seen).
    ///  - HANDS: every bystander's steadiness, and the picture's motion with the
    ///    car held still, today against new (CamcorderCam.HolderOf / Shake).
    /// </summary>
    public static partial class LifeSimSelfTest
    {
        /// <summary>This round's checks with the ones its files touch (writes PSXRacing_selftest_replayfx.txt).
        /// Opens an empty scene.</summary>
        public static void RunReplayFx()
        {
            log = new System.Text.StringBuilder();
            failures = 0;
            Guard(nameof(TestCamcorderHands), TestCamcorderHands);
            Guard(nameof(TestCarSeen), TestCarSeen);
            Guard(nameof(TestCarDotsShade), TestCarDotsShade);
            Guard(nameof(TestTapes), TestTapes);
            Guard(nameof(TestCamcorder), TestCamcorder);
            Guard(nameof(TestLab), TestLab);
            Line(failures == 0 ? "REPLAYFX SELF-TEST OK" : "REPLAYFX SELF-TEST FAILED (" + failures + ")");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "../PSXRacing_selftest_replayfx.txt"), log.ToString());
        }

        // ------------------------------------------------------------------
        //  The car's shape on what stands in front of it
        // ------------------------------------------------------------------
        static void TestCarSeen()
        {
            Line("CARSEEN: a car behind a pole 1 m from the lens and a leaf card, the player's SSAO on (kept = handed back without the occlusion)");
            System.Environment.SetEnvironmentVariable("PSX_PROFILE", "PC");
            bool seenWas = CarDots.Seen, onWas = CarDots.On;
            int wW = CarDotsShots.W, wH = CarDotsShots.H;
            CarDotsShots.W = 960; CarDotsShots.H = 540;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                CarDots.On = true; CarDots.Reset();
                var cam = Camera.main;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.localScale = new Vector3(200f, 0.2f, 200f);
                ground.transform.position = new Vector3(0f, -0.1f, 0f);
                ground.GetComponent<Renderer>().sharedMaterial = CarSeenShots.PoleMat();
                var spot = new GameObject("Spot").transform;
                var def = CarModelLibrary.Load("civic_eg");
                Check(def != null, "the car loads");
                if (def == null) return;
                var shell = CarShell.Spawn(spot, def, 0, out _, solid: false);
                PSXTexDecode.StampTree(shell.gameObject);
                foreach (var (name, distM, fov, poleAt) in new[] { ("wide", 10f, 50f, 1.0f), ("zoom", 45f, 10f, 2.2f) })
                {
                    var c = new Vector3(0f, 0.6f, 0f);
                    var eye = c + Vector3.right * distM + Vector3.up;
                    var dir = (c - eye).normalized;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir));
                    cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1000f;
                    float half = Mathf.Atan(1.0f / distM) * Mathf.Rad2Deg;
                    var objs = CarSeenShots.Occluders(eye, Quaternion.AngleAxis(-half, Vector3.up) * dir, poleAt, 0.12f,
                                                      Quaternion.AngleAxis(half * 1.1f, Vector3.up) * dir, poleAt * 2.2f,
                                                      CarSeenShots.PoleMat(), CarSeenShots.LeafMat());
                    int s0 = CarDots.SeenFrames;
                    var v = CarSeenShots.Footprint(cam, null, objs);
                    Line($"    {name}: occluders cover {v.occluderPx} px, over the car {v.carUnderPx} px; kept there TODAY {v.keptTodayOver}, NEW {v.keptNewOver}; " +
                         $"kept on the open car TODAY {v.keptTodayOpen}, NEW {v.keptNewOpen}; seen mark ran {CarDots.SeenFrames - s0} frames");
                    Check(CarDots.SeenFrames > s0, name + ": the seen mark ran (the occlusion is on, the pass enqueued)");
                    Check(v.carUnderPx > 1000, name + ": the pole and the leaves cover part of the car", v.carUnderPx);
                    Check(v.keptTodayOver > v.carUnderPx / 2, name + ": TODAY reproduces the bug - the car's shape kept on the pole and leaves", v.keptTodayOver);
                    Check(v.keptNewOver <= v.carUnderPx / 100, name + ": NEW keeps (almost) nothing on the pole and leaves (<= 1% of their px over the car)", v.keptNewOver);
                    Check(v.keptNewOpen >= v.keptTodayOpen * 98 / 100 && v.keptTodayOpen > 200,
                          name + ": NEW keeps the open car as TODAY did (>= 98%: the dots fix still covers the paint)", v.keptNewOpen + " / " + v.keptTodayOpen);
                    foreach (var o in objs) Object.DestroyImmediate(o);
                }
                Object.DestroyImmediate(shell.gameObject);
            }
            finally
            {
                CarDots.Seen = seenWas; CarDots.On = onWas;
                CarDotsShots.W = wW; CarDotsShots.H = wH;
            }
        }

        // ------------------------------------------------------------------
        //  The hands
        // ------------------------------------------------------------------
        public struct HandMotion { public float rmsDeg, peakDeg, rmsPct, peakPct, rollRms, rollPeak; }

        /// <summary>The picture's motion with the car held still in the aim, at
        /// <paramref name="fov"/> (vertical) with the car <paramref name="dist"/> m
        /// out: the aim's spring and the sway, as CamcorderCam.LateUpdate runs them,
        /// 120 s at 60 Hz. Displacement of the car from its mean place, degrees and
        /// % of a 16:9 frame's width; the roll (off level) in degrees.</summary>
        public static HandMotion MeasureHand(CamcorderCam.Hand hand, float seed, float fov, float dist)
        {
            const float dt = 1f / 60f; const int n = 7200, warm = 120;
            var lens = Vector3.zero;
            var target = new Vector3(0f, -0.8f, dist);
            float yaw = 0f, pitch = 0f, yv = 0f, pv = 0f;
            var dx = new List<float>(); var dy = new List<float>(); var rolls = new List<float>();
            for (int i = 0; i < n + warm; i++)
            {
                float t = i * dt;
                var eye = lens + CamcorderCam.Sway(t + seed, hand);
                var to = target - eye;
                float wy = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float wp = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                if (i == 0) { yaw = wy; pitch = wp; }
                float w = CamcorderCam.AimOmega, z = hand.zeta;
                yv = Mathf.Clamp(yv + (w * w * Mathf.DeltaAngle(yaw, wy) - 2f * z * w * yv) * dt, -CamcorderCam.MaxPanDegS, CamcorderCam.MaxPanDegS);
                yaw += yv * dt;
                pv = Mathf.Clamp(pv + (w * w * Mathf.DeltaAngle(pitch, wp) - 2f * z * w * pv) * dt, -120f, 120f);
                pitch += pv * dt;
                CamcorderCam.Shake(t, seed, hand, fov, out float sy, out float sp, out float roll);
                if (i < warm) continue;
                // the true direction to the car from the lens's eye minus where the lens points
                dx.Add((yaw + sy) - wy); dy.Add((pitch + sp) - wp); rolls.Add(roll);
            }
            float mx = dx.Average(), my = dy.Average();
            double s2 = 0; float pk = 0f;
            for (int i = 0; i < dx.Count; i++)
            {
                float d = Mathf.Sqrt((dx[i] - mx) * (dx[i] - mx) + (dy[i] - my) * (dy[i] - my));
                s2 += d * d; pk = Mathf.Max(pk, d);
            }
            float hfov = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * 16f / 9f) * Mathf.Rad2Deg;
            float rms = (float)System.Math.Sqrt(s2 / dx.Count);
            return new HandMotion
            {
                rmsDeg = rms, peakDeg = pk, rmsPct = rms / hfov * 100f, peakPct = pk / hfov * 100f,
                rollRms = Mathf.Sqrt(rolls.Average(r => r * r)), rollPeak = rolls.Max(r => Mathf.Abs(r)),
            };
        }

        static void TestCamcorderHands()
        {
            Line("HANDS: each bystander's steadiness; the picture's motion with the car held still (16:9 frame), round 2 -> new");
            var a = CamcorderCam.HolderOf(3, 1, new Vector3(120.3f, 4f, -88.6f));
            var b = CamcorderCam.HolderOf(3, 1, new Vector3(120.3f, 4f, -88.6f));
            Check(a.shake == b.shake && a.seed == b.seed && a.tilt == b.tilt, "the same bystander has the same hands every play");
            var all = new List<CamcorderCam.Hand>();
            for (int p = 0; p < 40; p++)
                for (int l = 0; l < 2; l++)
                    for (int k = 0; k < 25; k++)
                        all.Add(CamcorderCam.HolderOf(p, l, new Vector3(k * 37.1f - 400f, 0f, p * 113.7f + l * 9f)));
            int rock = all.Count(h => h.shake < 0.2f), fair = all.Count(h => h.shake >= 0.2f && h.shake < 0.55f), shaky = all.Count(h => h.shake >= 0.55f);
            Line($"    {all.Count} bystanders: rock steady {rock * 100f / all.Count:0}%, fairly steady {fair * 100f / all.Count:0}%, a little shaky {shaky * 100f / all.Count:0}%");
            Check(rock > all.Count * 5 / 100 && rock < all.Count * 16 / 100, "now and then one is rock steady (~10%)", rock);
            Check(fair > all.Count / 2, "most hold fairly steady (~65%)", fair);
            Check(shaky > all.Count * 15 / 100 && shaky < all.Count * 35 / 100, "some are a little shakier (~25%)", shaky);
            var sorted = all.OrderBy(h => h.shake).ToList();
            var picks = new[] { ("steadiest", sorted[0]), ("typical", sorted[sorted.Count / 2]), ("shakiest", sorted[sorted.Count - 1]) };
            // wide: the lens framing the car at 40 deg (26 m out); tight: a far lens at its 5 deg (217 m out)
            var zooms = new[] { ("wide 40 deg", 40f, 26f), ("tight 5 deg", 5f, 217f) };
            foreach (var (zn, fov, dist) in zooms)
            {
                foreach (var (pn, hand) in picks)
                {
                    var today = MeasureHand(CamcorderCam.Hand.Today, hand.seed, fov, dist);
                    var now = MeasureHand(hand, hand.seed, fov, dist);
                    Line($"    {zn,-12} {pn,-9} (shake {hand.shake:0.00}, zeta {hand.zeta:0.00}, tilt {hand.tilt:+0.00;-0.00}): " +
                         $"RMS {today.rmsPct:0.00}% -> {now.rmsPct:0.00}% of the width ({today.rmsDeg:0.000} -> {now.rmsDeg:0.000} deg), " +
                         $"peak {today.peakPct:0.00}% -> {now.peakPct:0.00}% ({today.peakDeg:0.000} -> {now.peakDeg:0.000} deg); " +
                         $"roll RMS {today.rollRms:0.00} -> {now.rollRms:0.00} deg, peak {today.rollPeak:0.00} -> {now.rollPeak:0.00} deg");
                    Check(now.rmsPct < today.rmsPct && now.peakPct < today.peakPct && now.rollPeak < today.rollPeak,
                          zn + " " + pn + ": steadier than round 2", now.rmsPct.ToString("0.00"));
                }
            }
            var typ = picks[1].Item2;
            var w = MeasureHand(typ, typ.seed, 40f, 26f); var tt = MeasureHand(typ, typ.seed, 5f, 217f);
            Check(tt.rmsPct <= w.rmsPct * 1.15f, "zoomed in is no wilder than wide (as a share of the frame)", tt.rmsPct.ToString("0.00") + " vs " + w.rmsPct.ToString("0.00"));
            float worst = picks.Max(p => MeasureHand(p.Item2, p.Item2.seed, 5f, 217f).peakPct);
            Check(worst <= 5f, "the shakiest hand's peak on the tightest lens stays under 5% of the frame", worst.ToString("0.00"));
        }
    }
}
