using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using PSXRacing;
using PSXRacing.LifeSim;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE ARCADE'S DAMAGE in play (ArcadeDamage; run through CrashPlayCheck.Run
    /// with PSX_ARCADE_DAMAGE_CHECK=1, which makes the session an arcade one).
    /// On the check venue, a 65 km/h square wall hit (the structural FAIL line)
    /// in each mode:
    ///   FULL         a ledger that acts and never banks: the car shows it, the
    ///                FAIL takes its power, the race ends DNF; no damage file,
    ///                no injury.
    ///   VISUAL ONLY  the dents alone: no lamp out, steam or bag, no pull, no
    ///                power loss, no DNF; the car drives on.
    ///   OFF          nothing armed.
    /// Then the scene is reloaded (what RESTART and the next race do) and the
    /// new car is checked clean. Pictures of the car after each hit.
    /// </summary>
    public class ArcadeDamagePlayCheckRunner : MonoBehaviour
    {
        CarController car;
        CollisionResponder resp;
        Rigidbody rb;
        float surfaceY;
        float zNext = -14000f;
        bool hold;
        float holdMps;
        string shots;

        static void Check(bool ok, string what, object got = null) => CrashPlayCheck.Check(ok, what, got);
        static void Note(string what) => CrashPlayCheck.Note(what);
        static float[] Low() { var d = new float[DamageModel.Dice]; for (int i = 0; i < d.Length; i++) d[i] = 0.1f; return d; }

        void FixedUpdate()
        {
            if (car == null || rb == null) return;
            float vf = Vector3.Dot(rb.linearVelocity, car.transform.forward);
            car.throttleInput = hold ? Mathf.Clamp01(0.35f + (holdMps - vf) * 0.6f) : 0f;
            car.brakeInput = 0f;
            car.steerInput = 0f;
            car.handbrakeInput = false;
        }

        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            shots = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots", "arcadedamage");
            Directory.CreateDirectory(shots);
            int saved = ArcadeDamage.Mode;
            IEnumerator body = Body();
            while (true)
            {
                object cur = null;
                bool more;
                try { more = body.MoveNext(); if (more) cur = body.Current; }
                catch (System.Exception e) { more = false; Check(false, "THREW: " + e.Message); Debug.LogException(e); }
                if (!more) break;
                yield return cur;
            }
            ArcadeDamage.Mode = saved;
            ArcadeMode.Simulate(false);
            RaceHandoff.ClearAll();
            CrashPlayCheck.Finish();
            EditorApplication.Exit(CrashPlayCheck.failures == 0 ? 0 : 1);
        }

        IEnumerator Fixed(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        IEnumerator Acquire()
        {
            for (int i = 0; i < 25; i++) yield return null;
            var rm = RaceManager.Instance;
            car = rm != null ? rm.playerCar : null;
            if (car == null) yield break;
            resp = car.GetComponent<CollisionResponder>();
            rb = car.GetComponent<Rigidbody>();
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) input.enabled = false;
            foreach (var sr in car.GetComponentsInChildren<StuckRecovery>(true)) sr.enabled = false;
            car.manualMode = false;
            float t = 0f;
            while (rm.State != RaceManager.RaceState.Racing && t < 10f) { t += Time.deltaTime; yield return null; }
        }

        DamageLedger Arm(int mode)
        {
            ArcadeDamage.Mode = mode;
            resp.Damage = null;
            resp.Ledger = null;
            resp.ResetDamage();
            car.faultAccelMult = 1f;
            car.hitSteerPull = 0f;
            bool armed = CrashStopGap.Arm(car);
            var L = resp.Damage;
            if (L != null) L.DrawOverride = Low;
            Note(ArcadeDamage.Names[mode] + ": CrashStopGap.Arm " + armed + ", ledger " + (L != null ? "banks " + L.Banks + ", acts " + L.Acts : "none"));
            return L;
        }

        IEnumerator Wall(string name, DamageLedger L)
        {
            var at = new Vector3(-600f, surfaceY + 0.6f, zNext);
            zNext += 400f;
            var wall = new GameObject("Wall" + name);
            wall.transform.position = at + new Vector3(0f, 0.6f, 24f);
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 2.4f, 1f);
            car.TeleportTo(at, Quaternion.identity);
            yield return Fixed(0.2f);
            float v = 65f / 3.6f;
            car.SetRolling(v);
            hold = true; holdMps = v;
            float t = 0f;
            while (t < 4f && (L == null ? resp.HardHits == 0 : L.incidents.Count == 0)) { t += Time.fixedDeltaTime; yield return new WaitForFixedUpdate(); }
            hold = false;
            yield return Fixed(1.0f);
            for (int i = 0; i < 20; i++) yield return null;   // DamageLook reads every 0.2 s
            Shoot(name);
            Destroy(wall);
        }

        bool DrivesAway()
        {
            return rb.linearVelocity.magnitude > 3f;
        }

        IEnumerator DriveTest(System.Action<float> got)
        {
            car.TeleportTo(new Vector3(-600f, surfaceY + 0.6f, zNext), Quaternion.identity);
            zNext += 400f;
            yield return Fixed(0.3f);
            hold = true; holdMps = 40f;
            yield return Fixed(3f);
            got(rb.linearVelocity.magnitude);
            hold = false;
        }

        void Shoot(string name)
        {
            if (car == null) return;
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 600f;
            Vector3 c = car.transform.position;
            go.transform.position = c + car.transform.forward * 4.8f + car.transform.right * 2.8f + Vector3.up * 1.5f;
            go.transform.LookAt(c + Vector3.up * 0.4f);
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string path = Path.Combine(shots, "arcade_damage_" + name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Note("picture " + path);
            cam.targetTexture = null;
            Destroy(tex); rt.Release(); Destroy(rt); Destroy(go);
        }

        IEnumerator Body()
        {
            yield return Acquire();
            Check(car != null, "there is a player car");
            if (car == null) yield break;
            Check(ArcadeMode.Active && !Edition.HasCareer, "an arcade session (no career)");
            Check(DamageModel.Live, "the located damage model is live");
            var rm = RaceManager.Instance;
            Note("race " + rm.State + "; car " + car.name);
            Check(ArcadeDamage.Armed == ArcadeDamage.Mode && resp.Damage != null,
                  "the race start armed the option's ledger (" + ArcadeDamage.Label + ")");
            var strip = new GameObject("DamageStrip");
            strip.layer = car.roadLayer;
            strip.transform.position = new Vector3(0f, 4000f, 0f);
            strip.AddComponent<BoxCollider>().size = new Vector3(3000f, 2f, 30000f);
            surfaceY = 4001f;
            foreach (var p in DamageBank.Files()) File.Delete(p);

            // ---- FULL ----
            var L = Arm(ArcadeDamage.Full);
            Check(L != null && !L.Banks && L.LiveFx, "FULL: a ledger that acts and never banks");
            yield return Wall("full", L);
            var look = car.GetComponent<DamageLook>();
            Check(look != null && !look.bodyOnly && look.Now.Any,
                  "FULL: the car shows the crash", look != null ? "front " + look.Now.zones.x.ToString("0.00") + ", lamps 0x" + look.Now.lampOut.ToString("x") + ", bags " + look.Now.bags : "no look");
            Check(L != null && L.Failed != DamageRules.FailNone && car.faultAccelMult == 0f,
                  "FULL: 65 km/h square into a wall is the structural FAIL - the car has no power", L != null ? "FAIL " + DamageRules.FailName(L.Failed) + ", power x" + car.faultAccelMult : "-");
            Check(rm.PlayerOut && rm.State == RaceManager.RaceState.Finished && ArcadeDamage.Dnf &&
                  RaceHandoff.ResultReady && RaceHandoff.FinishPos == 0,
                  "FULL: the race is over for the player, DNF (result stamped, continue goes to RESULT)",
                  "out " + rm.PlayerOut + ", state " + rm.State + ", dnf " + ArcadeDamage.Dnf + ", pos " + RaceHandoff.FinishPos);
            Check(DamageBank.Files().Length == 0 && L != null && L.incidents.All(i => i.injury == 0),
                  "FULL: nothing banked (no damage file) and no injury", DamageBank.Files().Length + " files");
            float fullSpeed = -1f;
            yield return DriveTest(s => fullSpeed = s);
            Check(fullSpeed < 1f, "FULL: at full throttle afterwards it does not drive away", fullSpeed.ToString("0.0") + " m/s");

            // ---- VISUAL ONLY ----
            ArcadeDamage.Dnf = false;
            rm.ReinstatePlayer();
            L = Arm(ArcadeDamage.Visual);
            Check(L != null && !L.Banks && !L.Acts, "VISUAL ONLY: a ledger for the look alone");
            yield return Wall("visual", L);
            look = car.GetComponent<DamageLook>();
            Check(look != null && look.bodyOnly && look.Now.zones != Vector4.zero && look.Now.lampOut == 0 &&
                  look.Now.bags == 0 && look.Now.radStage == 0,
                  "VISUAL ONLY: the dents, and no lamp out, bag or steam", look != null ? "front " + look.Now.zones.x.ToString("0.00") : "no look");
            Check(car.faultAccelMult == 1f && car.hitSteerPull == 0f && !rm.PlayerOut && !ArcadeDamage.Dnf,
                  "VISUAL ONLY: no power loss, no pull, no DNF", "power x" + car.faultAccelMult + ", pull " + car.hitSteerPull);
            float visSpeed = -1f;
            yield return DriveTest(s => visSpeed = s);
            Check(visSpeed > 3f, "VISUAL ONLY: it drives on after the hit", visSpeed.ToString("0.0") + " m/s");

            // ---- OFF ----
            L = Arm(ArcadeDamage.Off);
            Check(L == null, "OFF: nothing armed (today's arcade)");
            yield return Wall("off", null);
            Check(resp.Damage == null && car.faultAccelMult == 1f && !rm.PlayerOut, "OFF: no ledger, no power loss, no DNF");

            // ---- the next race: fresh ----
            ArcadeDamage.Mode = ArcadeDamage.Full;
            Arm(ArcadeDamage.Full);
            yield return Wall("before_restart", resp.Damage);
            var oldCar = car;
            RaceHandoff.ResultReady = false;
            Destroy(strip);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            yield return null;
            car = null;
            yield return Acquire();
            Check(car != null && car != oldCar, "RESTART: a new car");
            if (car == null) yield break;
            look = car.GetComponent<DamageLook>();
            Check(resp.Damage != null && resp.Damage.incidents.Count == 0 && resp.Damage.Failed == DamageRules.FailNone &&
                  car.faultAccelMult == 1f && car.hitSteerPull == 0f && (look == null || !look.Now.Any) &&
                  !ArcadeDamage.Dnf && !RaceManager.Instance.PlayerOut,
                  "RESTART / the next race: FULL armed again on a clean car (no incidents, full power, no look, no DNF)");
            Shoot("next_race");
        }
    }
}
