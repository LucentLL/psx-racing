using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// Fix pass 1, the proof for D and C (CrashPlayCheck with PSX_FIX1_FRAMES=1,
    /// a hidden play check; report PSXRacing_crash_play_check.txt, frames
    /// PSXRacing_fix1_frames.csv, pictures Screenshots/fix1).
    ///
    /// D - "the windows and side mirrors keep alternating between different
    /// states of damage while driving normally": a career car with a right-side
    /// door dent and a cracked roof glass drives a slalom (CarBodyLean leans the
    /// shell) with the look's frame taken as the shell leans (PSX_FAULT_LOOKREST=0)
    /// and at its rest pose. Each frame logs what the body's property block makes
    /// of one probe point by the right door mirror (the door dent under the
    /// beltline, the glass crack over it): with the lean in the frame it jumps at
    /// every push; at rest it holds.
    ///
    /// C - "a square appears around the car when smoking tires": at night, the
    /// headlamps on, the tyres smoking, one frozen frame of the chase camera with
    /// the own-car clip for every eye (PSX_SMOKE_BOXFIX=0) and for an eye inside.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class Fix1FramesRunner : MonoBehaviour
    {
        CarController car;
        Rigidbody rb;
        bool drive, smoke;
        float driveT, speed = 19f;
        static void Check(bool ok, string what, object got = null) => CrashPlayCheck.Check(ok, what, got);
        static void Note(string what) => CrashPlayCheck.Note(what);
        static string ShotDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "fix1");

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            var tank = car.GetComponent<FuelTank>();
            if (tank != null) tank.percent = 100f;
            if (!drive) { car.throttleInput = 0f; car.brakeInput = smoke ? 0f : 1f; car.steerInput = 0f; return; }
            driveT += Time.fixedDeltaTime;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = Mathf.Clamp01(0.4f + (speed - vf) * 0.5f);
            // a lift and a stab of brake every 3 s pitches it as the slalom rolls it
            bool brake = Mathf.Repeat(driveT, 3f) > 2.6f;
            car.brakeInput = brake ? 0.7f : 0f;
            if (brake) car.throttleInput = 0f;
            car.steerInput = Mathf.Sin(driveT * Mathf.PI * 2f / 2.2f) * 0.55f;
            car.handbrakeInput = false;
        }

        // before TireSmoke.LateUpdate: every tyre spinning (the smoke for C)
        void LateUpdate()
        {
            if (!smoke || car == null || car.wheelContacts == null) return;
            for (int i = 0; i < car.wheelContacts.Length; i++)
            {
                car.wheelContacts[i].grounded = true;
                car.wheelContacts[i].slide = 40f;
                car.wheelContacts[i].load = 1e6f;
                car.wheelContacts[i].wetT = 0f;
            }
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            IEnumerator body = Body();
            while (true)
            {
                object cur = null;
                bool more;
                try { more = body.MoveNext(); if (more) cur = body.Current; }
                catch (System.Exception e) { more = false; Check(false, "the run THREW: " + e.GetType().Name + " - " + e.Message); Debug.LogException(e); }
                if (!more) break;
                yield return cur;
            }
            Time.timeScale = 1f;
            DamageLook.RestOn = true;
            OwnCarClip.EyeInsideOnly = true;
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Body()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            Check(car != null, "there is a player car");
            if (car == null) yield break;
            rb = car.GetComponent<Rigidbody>();
            var resp = car.GetComponent<CollisionResponder>();
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            var temp = car.GetComponent<EngineTemp>();
            if (temp != null) temp.enabled = false;
            car.manualMode = false;
            var strip = new GameObject("Fix1Strip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);

            var s = LifeSimManager.State;
            var spec = CarCatalog.Get(RaceHandoff.CarSpecId) ?? CarCatalog.All[0];
            var owned = CarMarket.MakeOwnedCar(s, spec, 80, 40000f, spec.price);
            owned.id = "fix1#frames";
            owned.faults.Clear();
            s.activeCar = owned.id;
            RaceHandoff.CarId = owned.id;
            foreach (var p in DamageBank.Files()) File.Delete(p);
            if (resp != null) { resp.Damage = null; resp.Ledger = null; resp.ResetDamage(); }
            car.faultAccelMult = 1f;
            CrashStopGap.Arm(car);
            var L = resp != null ? resp.Damage : null;
            var look = car.GetComponent<DamageLook>();
            var cb = car.GetComponent<CarBody>();
            Check(L != null && look != null && cb != null && cb.bodyRenderer != null, "a career ledger, the look and the body renderer");
            if (L == null || look == null || cb == null || cb.bodyRenderer == null) yield break;
            L.PlantHit(DamageModel.ZSide, 1, -1, 5.5f, Time.time);    // the right doors dented
            L.PlantHit(DamageModel.ZRoof, 0, -1, 4.5f, Time.time + 0.01f);   // the glass cracked
            Directory.CreateDirectory(ShotDir);
            car.TeleportTo(new Vector3(-600f, 4001.6f, -14000f), Quaternion.identity);
            yield return Fixed(0.3f);

            var csv = new StringBuilder("mode,t,roll,pitch,pushes,sideDent,glassCrack,cy\n");
            foreach (bool rest in new[] { false, true })
            {
                DamageLook.RestOn = rest;
                DamageLook.Refresh(car);   // one push in this mode at once
                string mode = rest ? "rest" : "lean";
                car.TeleportTo(new Vector3(rest ? -300f : -600f, 4001.6f, -14000f), Quaternion.identity);
                yield return Fixed(0.2f);
                car.SetRolling(speed);
                drive = true; driveT = 0f;
                float minD = 9f, maxD = -9f, minG = 9f, maxG = -9f, minR = 99f, maxR = -99f;
                int changes = 0;
                float lastD = -1f, lastG = -1f;
                for (float t = 0f; t < 14f; t += Time.deltaTime)
                {
                    yield return null;
                    if (!Probe(cb, out float dent, out float glass, out float cy)) continue;
                    var e = cb.bodyRoot.localEulerAngles;
                    float roll = Mathf.DeltaAngle(0f, e.z), pitch = Mathf.DeltaAngle(0f, e.x);
                    minR = Mathf.Min(minR, roll); maxR = Mathf.Max(maxR, roll);
                    if (t < 1f) continue;   // the first push settles
                    minD = Mathf.Min(minD, dent); maxD = Mathf.Max(maxD, dent);
                    minG = Mathf.Min(minG, glass); maxG = Mathf.Max(maxG, glass);
                    if (lastD >= 0f && (Mathf.Abs(dent - lastD) > 0.005f || Mathf.Abs(glass - lastG) > 0.005f)) changes++;
                    lastD = dent; lastG = glass;
                    csv.Append(mode).Append(',').Append(t.ToString("0.00")).Append(',').Append(roll.ToString("0.00")).Append(',')
                       .Append(pitch.ToString("0.00")).Append(',').Append(changes).Append(',').Append(dent.ToString("0.000")).Append(',')
                       .Append(glass.ToString("0.000")).Append(',').Append(cy.ToString("0.000")).Append('\n');
                }
                drive = false;
                Note("D " + mode + ": the shell rolled " + minR.ToString("0.0") + ".." + maxR.ToString("0.0") + " deg; at the right mirror the door dent read " +
                     minD.ToString("0.000") + ".." + maxD.ToString("0.000") + ", the glass crack " + minG.ToString("0.000") + ".." + maxG.ToString("0.000") +
                     "; it changed " + changes + " times in 13 s");
                if (rest)
                    Check(changes == 0 && maxD - minD < 0.01f && maxG - minG < 0.01f,
                          "D: with the frame at the shell's rest pose the mirror and the glass hold one state while it leans", changes + " changes");
                else
                    Note("D (before, PSX_FAULT_LOOKREST=0): " + (changes > 0 ? "the state jumps at the pushes as the shell leans - the owner's flicker" : "no jump seen this run"));
                yield return Fixed(1.5f);
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "PSXRacing_fix1_frames.csv"), csv.ToString());

            // ---- C: night, headlamps on, the tyres smoking, one frozen frame twice ----
            yield return SmokeShots();
        }

        /// <summary>What the body's block makes of a point by the right door mirror (the shader's masks).</summary>
        static MaterialPropertyBlock mpb;
        bool Probe(CarBody cb, out float dent, out float glass, out float cy)
        {
            dent = glass = cy = 0f;
            var r = cb.bodyRenderer;
            var mf = cb.bodyFilter;
            if (r == null || mf == null || mf.sharedMesh == null) return false;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (mpb.GetFloat(DamageLook.OnId) < 0.5f) return false;
            var b = mf.sharedMesh.bounds;
            var lw = r.transform.localToWorldMatrix;
            var axis = new Vector3(((Vector3)lw.GetColumn(0)).magnitude, ((Vector3)lw.GetColumn(1)).magnitude, ((Vector3)lw.GetColumn(2)).magnitude);
            Vector4 A = mpb.GetVector(DamageLook.AId), B = mpb.GetVector(DamageLook.BId), F = mpb.GetVector(DamageLook.FId),
                    R = mpb.GetVector(DamageLook.RId), U = mpb.GetVector(DamageLook.UId), C = mpb.GetVector(DamageLook.CId);
            // the probe: across to the right flank, up by the beltline, forward to the door mirror
            float best = -1f; Vector3 probe = b.center;
            foreach (var sx in new[] { -1f, 1f })
            {
                var cand = b.center + new Vector3(sx * b.extents.x * 0.95f, b.extents.y * 0.30f, b.extents.z * 0.30f);
                var bp = Vector3.Scale(cand, axis) - (Vector3)C;
                float cx = Vector3.Dot(bp, (Vector3)R) * R.w;
                if (cx > best) { best = cx; probe = cand; }
            }
            var p = Vector3.Scale(probe, axis) - (Vector3)C;
            var c = new Vector3(Vector3.Dot(p, (Vector3)R) * R.w, Vector3.Dot(p, (Vector3)U) * U.w, Vector3.Dot(p, (Vector3)F) * F.w);
            float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.55f, c.y));
            float doors = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, Mathf.Abs(c.z)));
            float right = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.85f, c.x)) * doors;
            dent = right * A.w * low;
            glass = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.48f, c.y)) * B.x;
            cy = c.y;
            return true;
        }

        IEnumerator SmokeShots()
        {
            var sun = RenderSettings.sun;
            TimeOfDay.Apply(TimeOfDay.Night, sun);
            foreach (var l in Object.FindObjectsByType<CarLights>(FindObjectsInactive.Exclude)) l.PreviewBuild(true, false);
            car.TeleportTo(new Vector3(300f, 4001.6f, -14000f), Quaternion.identity);
            yield return Fixed(0.4f);
            drive = false;
            smoke = true;
            yield return Fixed(2.2f);
            var ts = car.GetComponent<TireSmoke>();
            var cam = Camera.main;
            Check(ts != null && cam != null, "C: the car smokes and there is a chase camera");
            if (ts == null || cam == null) { smoke = false; yield break; }
            Time.timeScale = 0f;
            foreach (bool fix in new[] { false, true })
            {
                OwnCarClip.EyeInsideOnly = fix;
                ts.Tick(0f);   // the clip's block only (no step)
                yield return null;
                string path = Path.Combine(ShotDir, fix ? "fix1_smoke_after.png" : "fix1_smoke_before.png");
                Check(Shoot(cam, path, 1280, 720), "C: the chase camera at night, tyres smoking, " +
                      (fix ? "the clip only for an eye inside (fix)" : "the clip for every eye (PSX_SMOKE_BOXFIX=0)") + ": " + path);
            }
            Time.timeScale = 1f;
            smoke = false;
        }

        static bool Shoot(Camera cam, string path, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24);
            var was = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[Fix1] shot failed: " + e.Message); return false; }
            finally { cam.targetTexture = was; RenderTexture.ReleaseTemporary(rt); }
        }

        IEnumerator Fixed(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime > 0f ? Time.deltaTime : 0.02f; yield return null; }
        }
    }

    /// <summary>Named as the file (as InjuryPlayCheck is): the runner above then has no editor
    /// MonoScript and can be added in play mode.</summary>
    public static class Fix1FramesCheck { }
}
