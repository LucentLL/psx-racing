using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// NIGHT 4's PROOFS (night round 4, 2026-11-23), in the player.
    ///
    /// THE LEAK PROBE (&amp;leak=SPEC;SPEC). The owner: "why does light go through walls,
    /// highway medians, etc?" At a spot found near (lat, lon) where a solid of the asked
    /// kind stands between a light and a patch of ground, the light ARRIVING on that patch
    /// is measured with the light on and off, NIGHT 4 off and on: the rendered frame's
    /// luminance at the patch (a camera on the far side looking at it) and the light term
    /// itself (the static field's texel, or the car pools' texel, read back from the GPU).
    ///   SPEC = name~lat~lon~kind[~RoadNameHas]
    ///   kind lamp-building | lamp-wall | lamp-deck : one field street lamp (soloed: only it
    ///        is splatted) and ground it cannot see behind a building / a wall over its head /
    ///        a deck or pier;
    ///   kind car-barrier | car-wall | car-building | car-any : the player's car, lamps on,
    ///        at a spot on a road (named) where a median barrier / a wall / a building cuts
    ///        its beams' line to ground at its own level beyond.
    /// On a stage (&amp;stage=ID&amp;leak=car-rock) the same for a rock face along the stage
    /// road, and a night shot of the stage (PerfTour.Stage.cs calls <see cref="StageNight4"/>).
    /// Files: &lt;tag&gt;_leak.csv, &lt;tag&gt;_leak_&lt;name&gt;_&lt;variant&gt;.jpg.
    ///
    /// THE SKY PANORAMAS (&amp;skypano=name~lat~lon~RoadNameHas;...[&amp;apv=...]): at a road
    /// point, the view from 1.6 m at eight headings x three elevations (3, 20, 50 degrees),
    /// one 1280-wide frame each per variant: &lt;tag&gt;_sky_&lt;name&gt;_&lt;variant&gt;_h&lt;deg&gt;_e&lt;deg&gt;.jpg.
    /// The step check runs on these (scratchpad night4\py\skystep.py).
    /// </summary>
    public partial class PerfTour
    {
        readonly StringBuilder leakCsv = new StringBuilder();
        static readonly List<Vector3> n4Heads = new List<Vector3>();

        static string BlockerKind(RaycastHit h)
        {
            if (h.collider == null) return "-";
            string n = h.collider.name;
            if (n.StartsWith("Buildings")) return "building";
            if (n.StartsWith("Barriers") || n.StartsWith("Guardrail")) return "wall";
            if (n.StartsWith("Structure")) return "deck";
            if (n.StartsWith("Roads") && h.normal.y < -0.3f) return "deck";
            return "other:" + n;
        }

        static bool KindMatches(string want, string got)
        {
            if (want == "any" || want == "rock") return got != "-";
            if (want == "barrier") return got == "wall";
            return got == want;
        }

        /// <summary>The first solid (not thin, not a car) on the segment a-b, or false.</summary>
        static bool FirstSolid(Vector3 a, Vector3 b, out RaycastHit hit)
        {
            hit = default;
            var d = b - a; float len = d.magnitude;
            if (len < 0.05f) return false;
            var hits = Physics.RaycastAll(a, d / len, len, StreetLights.N4Mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            foreach (var h in hits)
            {
                if (h.distance >= best || h.collider == null) continue;
                var bb = h.collider.bounds;
                if (Mathf.Max(bb.size.x, bb.size.z) < StreetLights.N4ThinM) continue;
                best = h.distance; hit = h; any = true;
            }
            return any;
        }

        static bool GroundAt(Vector3 p, float fromY, out RaycastHit g)
            => Physics.Raycast(new Vector3(p.x, fromY, p.z), Vector3.down, out g, 40f, StreetLights.N4Mask, QueryTriggerInteraction.Ignore) && g.normal.y > 0.8f;

        IEnumerator LeakProbe(CityMode mode, string arg)
        {
            var world = mode.world; var map = world.Map; car = mode.player;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>() ?? Object.FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            int W = 1920, H = 1030;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 };
            rt.Create(); screen.Create(); apCam = cam; apRT = rt;
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var ci = CultureInfo.InvariantCulture;
            leakCsv.Length = 0;
            leakCsv.AppendLine("spot,kind,blocker,variant,ycode,light,detail");
            foreach (var spec in arg.Split(';'))
            {
                var f = spec.Split('~');
                if (f.Length < 4) continue;
                if (!double.TryParse(f[1], NumberStyles.Float, ci, out double la) || !double.TryParse(f[2], NumberStyles.Float, ci, out double lo)) continue;
                string name = f[0], kind = f[3], roadHas = f.Length > 4 ? f[4] : "";
                var at2 = LL(la, lo);
                var c0 = Ground(new Vector3(at2.x, 200f, at2.y));
                world.EnsureRing(c0, 1);
                car.ResetTo(c0 + Vector3.up * 0.5f, Quaternion.identity, seated: true);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    cam.transform.position = c0 + Vector3.up * 30f;
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 10f) break;
                    Pump(); yield return null;
                }
                for (int i = 0; i < 60; i++) { Pump(); yield return null; }
                if (kind.StartsWith("lamp-")) yield return LeakLamp(name, kind.Substring(5), c0, cam, rt, screen, blit, tex, W, H);
                else if (kind.StartsWith("car-")) yield return LeakCar(name, kind.Substring(4), c0, map, roadHas, null, cam, rt, screen, blit, tex, W, H);
            }
            Post("leak.csv", leakCsv.ToString());
            if (chase != null) chase.enabled = true;
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }

        /// <summary>The mean luminance (Ycode) in a box round world point p on the captured frame, or -1.</summary>
        float PatchY(Camera cam, Texture2D tex, int W, int H, Vector3 p, int half = 7)
        {
            var sp = cam.WorldToViewportPoint(p);
            if (sp.z <= 0f || sp.x < 0.02f || sp.x > 0.98f || sp.y < 0.02f || sp.y > 0.98f) return -1f;
            int cx = Mathf.RoundToInt(sp.x * W), cy = Mathf.RoundToInt(sp.y * H);
            var px = tex.GetPixels32();
            double sum = 0; int n = 0;
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    sum += Luma(px[y * W + x]); n++;
                }
            return n > 0 ? (float)(sum / n) : -1f;
        }

        IEnumerator Shoot(string file, Camera cam, Vector3 eye, Vector3 look, RenderTexture rt, RenderTexture screen, Material blit, Texture2D tex, int W, int H, int settle)
        {
            for (int i = 0; i < settle; i++)
            {
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up));
                Pump(); yield return null;
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up));
            Capture(cam, rt, screen, blit, tex, W, H);
            if (file != null) { var jpg = tex.EncodeToJPG(88); PSXPerfTour_Post(tag + "_" + file, jpg, jpg.Length); }
        }

        /// <summary>An eye that sees p from the side away from `src` (plan), clear of solids.</summary>
        static Vector3 EyeFor(Vector3 p, Vector3 src)
        {
            var away = new Vector3(p.x - src.x, 0f, p.z - src.z);
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            away.Normalize();
            foreach (float turn in new[] { 0f, 50f, -50f, 100f, -100f, 150f, -150f })
            {
                var dir = Quaternion.Euler(0f, turn, 0f) * away;
                var eye = p + dir * 6f + Vector3.up * 4.5f;
                if (!Physics.Linecast(eye, p + Vector3.up * 0.1f, StreetLights.N4Mask, QueryTriggerInteraction.Ignore)) return eye;
            }
            return p + Vector3.up * 8f + away * 0.5f;
        }

        IEnumerator LeakLamp(string name, string want, Vector3 c0, Camera cam, RenderTexture rt, RenderTexture screen, Material blit, Texture2D tex, int W, int H)
        {
            StreetLights.FieldLampsNear(c0, 220f, n4Heads);
            float bestScore = 0f; Vector3 bestHead = default, bestP = default; string bestB = "";
            foreach (var head in n4Heads)
            {
                if (!Physics.Raycast(head + Vector3.down * 0.3f, Vector3.down, out var gh, 40f, StreetLights.N4Mask, QueryTriggerInteraction.Ignore)) continue;
                float gy = gh.point.y;
                for (int b = 0; b < 48; b++)
                {
                    float a = (b + 0.25f) / 48f * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    for (float r = 4f; r <= 30f; r += 1.5f)
                    {
                        var q = head + dir * r;
                        if (!GroundAt(q, head.y - 0.5f, out var g) || Mathf.Abs(g.point.y - gy) > 2.5f) continue;
                        var p = g.point;
                        if (!FirstSolid(head + Vector3.down * 0.15f, p + Vector3.up * 0.05f, out var hit)) continue;
                        string bk = BlockerKind(hit);
                        if (!KindMatches(want, bk)) continue;
                        if (new Vector2(p.x - hit.point.x, p.z - hit.point.z).magnitude < 2f) continue;
                        float w = 2.4f * Mathf.Pow(Mathf.Max(0f, 1f - r * r / 324f), 2f) + 0.2f * Mathf.Pow(Mathf.Max(0f, 1f - r * r / 1600f), 2f);
                        if (w > bestScore) { bestScore = w; bestHead = head; bestP = p; bestB = bk + "@" + new Vector2(hit.point.x - head.x, hit.point.z - head.z).magnitude.ToString("0.0", CultureInfo.InvariantCulture) + "m"; }
                    }
                }
            }
            if (bestScore <= 0f) { Say("LEAK " + name + ": no lamp-" + want + " spot within 220 m"); leakCsv.AppendLine(name + ",lamp-" + want + ",-,none,-1,-1,no spot"); yield break; }
            var eye = EyeFor(bestP, bestHead);
            string where = "head " + V(bestHead) + " patch " + V(bestP) + " plan " + new Vector2(bestP.x - bestHead.x, bestP.z - bestHead.z).magnitude.ToString("0.0", CultureInfo.InvariantCulture) + " m";
            Say("LEAK " + name + " lamp-" + want + ": " + where + " blocker " + bestB);
            bool n4Was = StreetLights.Night4;
            var variants = new (string v, bool n4, bool solo)[] { ("n3_on", false, true), ("n3_off", false, false), ("n4_on", true, true), ("n4_off", true, false) };
            foreach (var vr in variants)
            {
                StreetLights.Night4 = vr.n4;
                StreetLights.N4SoloAt = vr.solo ? bestHead : new Vector3(bestHead.x + 1e6f, 0f, 0f);
                StreetLights.N4SoloM = 0.6f;
                StreetLights.N4Touch();
                yield return Shoot("leak_" + name + "_" + vr.v + ".jpg", cam, eye, bestP, rt, screen, blit, tex, W, H, 70);
                float y = PatchY(cam, tex, W, H, bestP);
                float light = StreetLights.N3LightAt(bestP);
                string det = "";
                int slot = StreetLights.N4NearestSlot(bestHead, 0.6f);
                if (StreetLights.N4ViewToward(slot, bestP, out _, out var view)) det = "view " + view.x.ToString("0.0", CultureInfo.InvariantCulture) + "-" + view.y.ToString("0", CultureInfo.InvariantCulture);
                leakCsv.AppendLine(name + ",lamp-" + want + "," + bestB + "," + vr.v + "," + y.ToString("0.0", CultureInfo.InvariantCulture) + "," + light.ToString("0.0000", CultureInfo.InvariantCulture) + "," + where + " " + det);
            }
            StreetLights.N4SoloAt = new Vector3(float.NaN, 0f, 0f);
            StreetLights.Night4 = n4Was;
            StreetLights.N4Touch();
            for (int i = 0; i < 40; i++) { Pump(); yield return null; }
        }

        /// <summary>A spot on a road (city: an edge whose name holds roadHas near c0; stage: the path)
        /// where the car's lamps' line to ground at their level beyond is cut by a solid of the kind.</summary>
        IEnumerator LeakCar(string name, string want, Vector3 c0, CityMap map, string roadHas, TrackPath path,
                            Camera cam, RenderTexture rt, RenderTexture screen, Material blit, Texture2D tex, int W, int H)
        {
            var spots = new List<(Vector3 pos, Vector3 fwd)>();
            if (path != null)
            {
                for (int i = 4; i < path.Count - 4; i += 3)
                {
                    var t = path.GetTangent(i); t.y = 0f; if (t.sqrMagnitude < 1e-4f) continue;
                    spots.Add((path.GetPoint(i), t.normalized));
                }
            }
            else
            {
                foreach (var e in map.edges)
                {
                    if (e.link || e.bridge || e.tunnel) continue;
                    if (!string.IsNullOrEmpty(roadHas) && (e.name == null || e.name.IndexOf(roadHas, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    for (float s = 0f; s < e.length; s += 4f)
                    {
                        var q = e.PointAt(s);
                        if ((new Vector2(q.x - c0.x, q.y - c0.z)).sqrMagnitude > 250f * 250f) continue;
                        var tt = e.TangentAt(s); var t3 = new Vector3(tt.x, 0f, tt.y).normalized;
                        var right = Vector3.Cross(Vector3.up, t3).normalized;
                        var basep = new Vector3(q.x, e.YAt(s), q.y);
                        float lane = Mathf.Clamp(e.width * 0.25f, 1.6f, 5.5f);
                        // a one-way carriageway: both lanes' sides; a two-way street: the right-hand lane each way
                        spots.Add((basep + right * lane, t3));
                        if (e.oneway) spots.Add((basep - right * lane, t3));
                        else spots.Add((basep - right * lane, -t3));
                    }
                }
            }
            float bestScore = 0f; Vector3 bPos = default, bFwd = default, bP = default; string bB = "";
            foreach (var sp in spots)
            {
                if (!GroundAt(sp.pos, sp.pos.y + 3f, out var cg)) continue;
                var pos = cg.point;
                var lamp = pos + Vector3.up * 0.65f + sp.fwd * 2f;
                for (float deg = -34f; deg <= 34f; deg += 4f)
                {
                    var dir = Quaternion.Euler(0f, deg, 0f) * sp.fwd;
                    for (float r = 8f; r <= 38f; r += 3f)
                    {
                        if (!GroundAt(lamp + dir * r, pos.y + 4f, out var g) || Mathf.Abs(g.point.y - pos.y) > 1.2f) continue;
                        var p = g.point;
                        if (!FirstSolid(lamp, p + Vector3.up * 0.05f, out var hit)) continue;
                        if (hit.normal.y > 0.8f) continue;
                        string bk = path != null ? (hit.collider != null ? "rock:" + hit.collider.name : "-") : BlockerKind(hit);
                        if (!KindMatches(want, path != null ? "x" : bk)) continue;
                        if (new Vector2(p.x - hit.point.x, p.z - hit.point.z).magnitude < 1.5f) continue;
                        float spread = 1f - Mathf.Clamp01((Mathf.Abs(deg) - 8f) / 26f);
                        float att = Mathf.Clamp01((r - 3f) / 7f) * (1f - Mathf.Clamp01((r - 30f) / 45f));
                        float w = spread * att;
                        if (w > bestScore) { bestScore = w; bPos = pos; bFwd = sp.fwd; bP = p; bB = bk + "@" + Vector3.Distance(lamp, hit.point).ToString("0.0", CultureInfo.InvariantCulture) + "m"; }
                    }
                }
            }
            if (bestScore <= 0f) { Say("LEAK " + name + ": no car-" + want + " spot"); leakCsv.AppendLine(name + ",car-" + want + ",-,none,-1,-1,no spot"); yield break; }
            var rot = Quaternion.LookRotation(bFwd, Vector3.up);
            var cl = car.GetComponentInChildren<CarLights>(true);
            var srcAt = bPos + bFwd * 2f;
            var eye = EyeFor(bP, srcAt);
            string where = "car " + V(bPos) + " patch " + V(bP) + " " + Vector3.Distance(srcAt, bP).ToString("0.0", CultureInfo.InvariantCulture) + " m";
            Say("LEAK " + name + " car-" + want + ": " + where + " blocker " + bB);
            bool n4Was = StreetLights.Night4;
            var variants = new (string v, bool n4, bool on, bool pool)[]
            {
                ("n3_on", false, true, false), ("n3_off", false, false, false), ("n3_pool", false, true, true),
                ("n4_on", true, true, false), ("n4_off", true, false, false), ("n4_pool", true, true, true),
            };
            foreach (var vr in variants)
            {
                StreetLights.Night4 = vr.n4; StreetLights.N4Touch();
                CarLights.ToolPoolOnly = vr.pool;
                if (cl != null) cl.enabled = vr.on;
                for (int i = 0; i < 6; i++) { car.ResetTo(bPos + Vector3.up * 0.45f, rot, seated: true); Pump(); yield return null; }
                yield return Shoot("leak_" + name + "_" + vr.v + ".jpg", cam, eye, bP, rt, screen, blit, tex, W, H, 50);
                float y = PatchY(cam, tex, W, H, bP);
                float pool = CarLights.PoolAt(bP);
                string det = cl != null ? "reach " + cl.OccReachToward(bP).ToString("0.0", CultureInfo.InvariantCulture) : "";
                leakCsv.AppendLine(name + ",car-" + want + "," + bB + "," + vr.v + "," + y.ToString("0.0", CultureInfo.InvariantCulture) + "," + pool.ToString("0.0000", CultureInfo.InvariantCulture) + "," + where + " " + det);
            }
            CarLights.ToolPoolOnly = false;
            StreetLights.Night4 = n4Was; StreetLights.N4Touch();
            if (cl != null) cl.enabled = false;
        }

        /// <summary>(PerfTour.Stage.cs) On a stage with &amp;leak=: the rock-face leak and three night
        /// frames of the stage from the game's chase camera, NIGHT 4 off and on.</summary>
        IEnumerator StageNight4(string id, TrackPath path)
        {
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>() ?? Object.FindAnyObjectByType<ChaseCamera>();
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            int W = 1920, H = 1030;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 };
            rt.Create(); screen.Create(); apCam = cam; apRT = rt;
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            leakCsv.Length = 0;
            leakCsv.AppendLine("spot,kind,blocker,variant,ycode,light,detail");
            // the night shots: the chase view at three points down the stage
            for (int k = 1; k <= 3; k++)
            {
                int i = Mathf.Clamp(path.Count * k / 4, 4, path.Count - 4);
                var p = path.GetPoint(i); var t = path.GetTangent(i); t.y = 0f; t.Normalize();
                foreach (bool n4 in new[] { false, true })
                {
                    StreetLights.Night4 = n4;
                    if (chase != null) chase.enabled = true;
                    for (int f = 0; f < 90; f++) { car.ResetTo(p + Vector3.up * 0.45f, Quaternion.LookRotation(t, Vector3.up), seated: true); Pump(); yield return null; }
                    Capture(cam, rt, screen, blit, tex, W, H);
                    var jpg = tex.EncodeToJPG(88);
                    PSXPerfTour_Post(tag + "_stage_" + id + "_" + k + (n4 ? "_n4" : "_n3") + ".jpg", jpg, jpg.Length);
                }
            }
            StreetLights.Night4 = true;
            if (chase != null) chase.enabled = false;
            var cl = car.GetComponentInChildren<CarLights>(true);
            if (cl != null) cl.enabled = true;
            yield return LeakCar(id, "rock", path.GetPoint(0), null, "", path, cam, rt, screen, blit, tex, W, H);
            Post(id + "_leak.csv", leakCsv.ToString());
            if (chase != null) chase.enabled = true;
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }

        IEnumerator SkyPano(CityMode mode, string arg)
        {
            var world = mode.world; car = mode.player; var map = world.Map;
            var input = car.GetComponent<PlayerCarInput>();
            if (input != null) { input.inputEnabled = false; input.enabled = false; }
            var cam = Camera.main;
            var chase = cam.GetComponent<ChaseCamera>() ?? Object.FindAnyObjectByType<ChaseCamera>();
            if (chase != null) chase.enabled = false;
            string url = Request() ?? "";
            var vars = new List<ProbeVariant>();
            foreach (var raw in (ShotLink.Query(url, "apv") ?? "").Split(';'))
            {
                string s = raw.Trim(); int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                var pv = new ProbeVariant { name = s.Substring(0, eq) };
                foreach (var os in s.Substring(eq + 1).Split(',')) { var op = os.Trim().Length > 0 ? MakeOp(os.Trim(), null, null) : null; if (op != null) pv.ops.Add(op); }
                vars.Add(pv);
            }
            if (vars.Count == 0) vars.Add(new ProbeVariant { name = "cur" });
            int W = 1280, H = 688;
            var rt = new RenderTexture(W, H, 32, RenderTextureFormat.Default) { antiAliasing = 1 };
            var screen = new RenderTexture(W, H, 0, RenderTextureFormat.Default) { antiAliasing = 1 };
            rt.Create(); screen.Create(); apCam = cam; apRT = rt;
            var outp = cam.GetComponent<PSXCameraOutput>();
            Material blit = outp != null && outp.display != null && outp.display.material != null ? new Material(outp.display.material) : null;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var ci = CultureInfo.InvariantCulture;
            float fov = cam.fieldOfView;
            cam.fieldOfView = 50f;
            foreach (var spec in arg.Split(';'))
            {
                var f = spec.Split('~');
                if (f.Length < 4) continue;
                if (!double.TryParse(f[1], NumberStyles.Float, ci, out double la) || !double.TryParse(f[2], NumberStyles.Float, ci, out double lo)) continue;
                var at2 = LL(la, lo);
                Vector3 pos;
                if (map.NearestRoadPoint(at2, 300f, false, out int ei, out float s0, out _))
                {
                    var e = map.edges[ei]; var q = e.PointAt(s0);
                    pos = new Vector3(q.x, e.YAt(s0), q.y);
                }
                else pos = Ground(new Vector3(at2.x, 200f, at2.y));
                pos = Ground(pos) + Vector3.up * 1.6f;
                world.EnsureRing(pos, 1);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 90f)
                {
                    car.ResetTo(pos + Vector3.forward * 15f - Vector3.up * 1.1f, Quaternion.identity, seated: true);
                    cam.transform.position = pos;
                    if (world.LiveTiles >= 25 && world.TreesPending == 0 && !world.TreeJobOpen && Time.realtimeSinceStartup - t0 > 10f) break;
                    Pump(); yield return null;
                }
                foreach (var pv in vars)
                {
                    foreach (var op in pv.ops) op.on();
                    for (int i = 0; i < 150; i++) { cam.transform.position = pos; Pump(); yield return null; }   // the sky weight eases over 2 s
                    for (int h = 0; h < 8; h++)
                        foreach (int el in new[] { 3, 20, 50 })
                        {
                            var rot = Quaternion.Euler(-el, h * 45f, 0f);
                            var look = pos + rot * Vector3.forward;
                            yield return Shoot("sky_" + f[0] + "_" + pv.name + "_h" + (h * 45).ToString("000") + "_e" + el.ToString("00") + ".jpg", cam, pos, look, rt, screen, blit, tex, W, H, 3);
                        }
                    for (int i = pv.ops.Count - 1; i >= 0; i--) pv.ops[i].off();
                }
                Say("SKYPANO " + f[0] + " at " + V(pos));
            }
            cam.fieldOfView = fov;
            if (chase != null) chase.enabled = true;
            rt.Release(); screen.Release(); Destroy(tex); if (blit != null) Destroy(blit);
        }
    }
}
