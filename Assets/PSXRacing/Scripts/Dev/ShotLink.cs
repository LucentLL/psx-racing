using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE PLAYER'S OWN PICTURE, ON REQUEST - the colour pass's harness
    /// (2026-09-29, plan C0b). Editor frames are an inference about the game;
    /// the colour bug that started this (RGB565 textures read without sRGB
    /// decode) lived in exactly the gap between the two. So a DEVELOPMENT
    /// WebGL build, opened from this machine with a deep link, stands the
    /// car on a protocol spot (<see cref="ColourSpots"/>), sets the hour,
    /// weather, dress, lights, lens and grade the way the game does, waits,
    /// and captures its own final frame - the canvas as the player sees it.
    ///
    ///   ?psxshot=venue,spot,hour,weather,season,cam,lights,lens,grade[,name[,hud]]
    ///       one frame. hour: TimeOfDay index or name; weather: Clear|Fog|
    ///       Rain|Snow; season: Winter|Spring|Summer|Fall|Baked; cam: chase
    ///       (the protocol eye) | game (the chase rig left running) | rigchase
    ///       / rigclose (the rig left running in that view: the player's own
    ///       picture, with hud=1 the real HUD - the MENU button, the map);
    ///       lights:
    ///       auto|on|off; lens, grade: 0|1; hud: 0 (hidden) | 1.
    ///   &amp;pose=x,y,z,qx,qy,qz,qw   the car's pose (from the editor frame's
    ///       sidecar), so both pictures are the same place to the millimetre.
    ///   ?psxshots=file.json   a list of shots, fetched beside the page:
    ///       {"shots":[{"name","venue","spot","hour","weather","season",
    ///       "cam","lights","lens","grade","hud","pose":[7 floats]}]}
    ///
    /// Each capture goes to window.__psxShot ({name, png: base64}) and is
    /// POSTed to __psxshot?name=... beside the page (tools\colour\shot-link.mjs
    /// serves the build and writes those to disk; a plain static server just
    /// refuses the POST). The console says "[ShotLink] SHOT name WxH" and a
    /// STATE line with what the frame was drawn with, and "[ShotLink] DONE".
    ///
    /// INERT anywhere but a development player served from this machine
    /// (127.0.0.1 / localhost) whose URL asks for it: never in the editor,
    /// never in a release build, never on the site.
    /// </summary>
    public class ShotLink : MonoBehaviour
    {
        public static bool Active { get; private set; }

        /// <summary>Frames a placed shot runs before it is captured.</summary>
        public const int SettleFrames = 90;
        /// <summary>Extra frames a streamed city gets to build round the car.</summary>
        public const int CityFrames = 150;

        [System.Serializable]
        public class Shot
        {
            public string name, venue, spot, hour, weather = "Clear", season = "Fall", cam = "chase", lights = "auto";
            public int lens, grade = 1, hud;
            public float[] pose;
        }

        [System.Serializable]
        class ShotList { public Shot[] shots; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PSXShotLink_Post(string name, byte[] data, int length);
        [DllImport("__Internal")] static extern void PSXShotLink_Note(string kind, string text);
#else
        static void PSXShotLink_Post(string name, byte[] data, int length) { }
        static void PSXShotLink_Note(string kind, string text) { }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string url = Application.absoluteURL;
            if (!LocalHost(url)) return;
            string one = Query(url, "psxshot"), many = Query(url, "psxshots");
            if (string.IsNullOrEmpty(one) && string.IsNullOrEmpty(many)) return;
            Active = true;
            var go = new GameObject("ShotLink");
            DontDestroyOnLoad(go);
            var link = go.AddComponent<ShotLink>();
            link.single = one; link.listFile = many; link.poseArg = Query(url, "pose");
            Debug.Log("[ShotLink] BEGIN " + (string.IsNullOrEmpty(many) ? "psxshot=" + one : "psxshots=" + many));
        }

        string single, listFile, poseArg;

        public static bool LocalHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)) return false;
            if (u.Scheme != "http" && u.Scheme != "https") return false;
            string host = u.Host.ToLowerInvariant();
            return host == "127.0.0.1" || host == "localhost" || host == "[::1]" || host == "::1";
        }

        public static string Query(string url, string key)
        {
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)) return null;
            foreach (var part in u.Query.TrimStart('?').Split('&'))
            {
                int eq = part.IndexOf('=');
                string k = eq < 0 ? part : part.Substring(0, eq);
                if (!string.Equals(k, key, System.StringComparison.OrdinalIgnoreCase)) continue;
                return eq < 0 ? "" : UnityWebRequest.UnEscapeURL(part.Substring(eq + 1));
            }
            return null;
        }

        /// <summary>venue,spot,hour,weather,season,cam,lights,lens,grade[,name[,hud]]</summary>
        public static Shot Parse(string spec, string pose)
        {
            var f = spec.Split(',');
            string At(int i, string d) => i < f.Length && f[i].Trim().Length > 0 ? f[i].Trim() : d;
            var s = new Shot
            {
                venue = At(0, "CityCircuit"), spot = At(1, "grid"), hour = At(2, "Noon"), weather = At(3, "Clear"),
                season = At(4, "Fall"), cam = At(5, "chase"), lights = At(6, "auto"),
                lens = At(7, "0") == "1" ? 1 : 0, grade = At(8, "1") == "0" ? 0 : 1,
            };
            s.name = At(9, "shot_" + s.venue + "_" + s.spot + "_" + s.hour);
            s.hud = At(10, "0") == "1" ? 1 : 0;
            if (!string.IsNullOrEmpty(pose))
            {
                var p = pose.Split(',');
                if (p.Length == 7)
                {
                    s.pose = new float[7];
                    for (int i = 0; i < 7; i++) float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out s.pose[i]);
                }
            }
            return s;
        }

        IEnumerator Start()
        {
            // The boot scene first: its own start-up (the career, the menus)
            // runs before anything is loaded over it.
            for (int i = 0; i < 30; i++) yield return null;
            var shots = new List<Shot>();
            if (!string.IsNullOrEmpty(listFile))
            {
                string url = new System.Uri(new System.Uri(Application.absoluteURL), listFile).ToString();
                using (var req = UnityWebRequest.Get(url))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.Log("[ShotLink] FAIL the list " + url + ": " + req.error);
                        Debug.Log("[ShotLink] DONE 0 shots, 1 failed");
                        yield break;
                    }
                    var list = JsonUtility.FromJson<ShotList>(req.downloadHandler.text);
                    if (list?.shots != null) shots.AddRange(list.shots);
                }
            }
            else shots.Add(Parse(single, poseArg));

            int ok = 0, failed = 0;
            foreach (var s in shots)
            {
                bool done = false;
                yield return Take(s, r => done = r);
                if (done) ok++; else failed++;
            }
            Debug.Log($"[ShotLink] DONE {ok} shots, {failed} failed");
            PSXShotLink_Note("done", $"{ok} {failed}");
        }

        IEnumerator Take(Shot s, System.Action<bool> result)
        {
            result(false);
            if (!TrackCatalog.TryIndexOf(s.venue, out int venue)) { Debug.Log("[ShotLink] FAIL " + s.name + ": no venue " + s.venue); yield break; }
            int hour = HourOf(s.hour);
            System.Enum.TryParse(s.weather, true, out Weather weather);
            bool baked = string.Equals(s.season, "Baked", System.StringComparison.OrdinalIgnoreCase);
            System.Enum.TryParse(baked ? "Fall" : s.season, true, out Season season);

            // THE REQUEST, the way a standalone (editor-Play) race reads it:
            // no career, the hour, the day (its season is the dress), and the
            // weather forced.
            RaceHandoff.FromLifeSim = false;
            RaceHandoff.TrackIndex = venue;
            RaceHandoff.TimeOfDayIndex = hour;
            RaceHandoff.CalendarDay = baked ? 0 : ColourSpots.DayIn(season);
            RaceHandoff.WeatherOverride = (int)weather;
            FilmGradePrefs.Enabled = s.grade != 0;
            LensFxPrefs.Enabled = s.lens != 0;

            string sceneId = TrackCatalog.SceneIdOf(venue);
            int build = TrackCatalog.BuildIndexOfScene(sceneId);
            if (build < 0) { Debug.Log("[ShotLink] FAIL " + s.name + ": scene " + sceneId + " is not in this player"); yield break; }
            var op = SceneManager.LoadSceneAsync(build, LoadSceneMode.Single);
            while (op != null && !op.isDone) yield return null;
            for (int i = 0; i < 20; i++) yield return null;

            // THE PLACE. Nothing else on the road: the field and the traffic
            // out (the editor frames have neither), the car held still where
            // the editor stood it (lifted, unsimulated, as there).
            var playerGo = GameObject.Find("RX-7 Player");
            var rm = RaceManager.Instance;
            CarController player = rm != null && rm.playerCar != null ? rm.playerCar : playerGo != null ? playerGo.GetComponent<CarController>() : null;
            if (player == null) { Debug.Log("[ShotLink] FAIL " + s.name + ": no player car in " + sceneId); yield break; }
            foreach (var c in FindObjectsByType<CarController>(FindObjectsInactive.Exclude))
                if (c != null && c != player) c.gameObject.SetActive(false);
            if (TrafficSystem.Instance != null) TrafficSystem.Instance.gameObject.SetActive(false);

            Vector3 pos; Quaternion rot; string info;
            if (s.pose != null && s.pose.Length == 7)
            {
                pos = new Vector3(s.pose[0], s.pose[1], s.pose[2]);
                rot = new Quaternion(s.pose[3], s.pose[4], s.pose[5], s.pose[6]);
                info = "pose from the request";
            }
            else
            {
                var spot = ColourSpots.Find(s.spot) ?? new ColourSpots.Spot { id = s.spot, venue = s.venue, how = "grid" };
                if (!ColourSpots.Pose(spot, player.transform, out pos, out rot, out info))
                { Debug.Log("[ShotLink] FAIL " + s.name + ": " + info); yield break; }
            }
            Hold(player, pos, rot);

            // Lights: auto is the hour's and the weather's (TimeOfDay.Apply ran
            // on load); on/off force it, the way the editor frames do.
            bool? lights = s.lights == "on" ? true : s.lights == "off" ? false : (bool?)null;
            if (lights.HasValue) CarLights.SetAll(lights.Value);

            var cam = PlaceCamera(s, pos, rot);
            var hidden = new List<Canvas>();
            int frames = SettleFrames + (FindAnyObjectByType<CityWorld>() != null ? CityFrames : 0);
            for (int i = 0; i < frames; i++)
            {
                Hold(player, pos, rot);
                if (lights.HasValue) CarLights.SetAll(lights.Value);
                if (s.hud == 0) HideHud(hidden);
                yield return null;
                if (cam != null && !RigCam(s.cam)) Aim(cam, pos, rot);
            }
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png = tex.EncodeToPNG();
            int w = tex.width, h = tex.height;
            Destroy(tex);
            PSXShotLink_Post(s.name, png, png.Length);
            Debug.Log($"[ShotLink] SHOT {s.name} {w}x{h}");
            string state = State(s, info, cam);
            Debug.Log("[ShotLink] STATE " + s.name + " " + state);
            PSXShotLink_Note("state", s.name + " " + state);
            foreach (var c in hidden) if (c != null) c.enabled = true;
            result(true);
        }

        static int HourOf(string h)
        {
            if (int.TryParse(h, out int i)) return Mathf.Clamp(i, 0, TimeOfDay.Count - 1);
            for (int k = 0; k < TimeOfDay.Count; k++)
                if (string.Equals(TimeOfDay.At(k).name, h, System.StringComparison.OrdinalIgnoreCase)) return k;
            return TimeOfDay.Noon;
        }

        static void Hold(CarController car, Vector3 pos, Quaternion rot)
        {
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                car.TeleportTo(pos, rot);
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            if ((car.transform.position - pos).sqrMagnitude > 1e-6f || Quaternion.Angle(car.transform.rotation, rot) > 0.01f)
            {
                if (rb != null) { rb.position = pos; rb.rotation = rot; }
                car.transform.SetPositionAndRotation(pos, rot);
            }
        }

        static Camera PlaceCamera(Shot s, Vector3 pos, Quaternion rot)
        {
            var chase = FindAnyObjectByType<ChaseCamera>();
            var camGo = GameObject.Find("PSXCamera");
            var cam = camGo != null ? camGo.GetComponent<Camera>() : Camera.main;
            if (s.cam == "rigclose") ChaseCamera.PreviewView(ChaseCamera.View.Close);
            else if (s.cam == "rigchase") ChaseCamera.PreviewView(ChaseCamera.View.Chase);
            if (RigCam(s.cam)) return cam;
            if (chase != null) chase.enabled = false;
            if (cam != null) Aim(cam, pos, rot);
            return cam;
        }

        /// <summary>A camera the game's own rig drives (not re-aimed here).</summary>
        static bool RigCam(string c) => c == "game" || c == "rigclose" || c == "rigchase";

        static void Aim(Camera cam, Vector3 pos, Quaternion rot)
        {
            ColourSpots.Eye(pos, rot, out Vector3 eye, out Quaternion look);
            cam.transform.SetPositionAndRotation(eye, look);
            cam.ResetProjectionMatrix();
            cam.fieldOfView = ColourSpots.Fov;
        }

        /// <summary>Every canvas but the one that shows the framebuffer.</summary>
        static void HideHud(List<Canvas> hidden)
        {
            var output = FindAnyObjectByType<PSXCameraOutput>();
            var display = output != null && output.display != null ? output.display.canvas : null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c == null || !c.enabled || !c.isRootCanvas || c == display) continue;
                c.enabled = false;
                hidden.Add(c);
            }
        }

        static string State(Shot s, string info, Camera cam)
        {
            string probeFmt = "not loaded", probeSrgb = "null";
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (t != null && t.name == "city_road_track_120_asphalt_new") { probeFmt = t.graphicsFormat.ToString(); probeSrgb = t.isDataSRGB ? "true" : "false"; break; }
            var output = FindAnyObjectByType<PSXCameraOutput>();
            var inv = CultureInfo.InvariantCulture;
            return "{" +
                $"\"hour\":\"{TimeOfDay.At(TimeOfDay.Current).name}\",\"weather\":\"{Seasons.CurrentWeather}\"," +
                $"\"dress\":{SeasonDress.AppliedDress},\"calendarDay\":{RaceHandoff.CalendarDay}," +
                $"\"grade\":{FilmGradePrefs.Amount.ToString(inv)},\"lens\":{LensFxPrefs.Amount.ToString(inv)}," +
                $"\"lines\":{(output != null ? output.height : 0)},\"pixels\":\"{PSXQuality.Current}\"," +
                $"\"headlights\":{CarLights.PushedCount},\"lamps\":{StreetLights.PushedCount}," +
                $"\"fov\":{(cam != null ? cam.fieldOfView : 0f).ToString(inv)},\"screen\":\"{Screen.width}x{Screen.height}\"," +
                $"\"probe\":\"{probeFmt}\",\"probeSRGB\":{probeSrgb},\"where\":\"{info.Replace("\"", "'")}\"" +
                "}";
        }
    }
}
