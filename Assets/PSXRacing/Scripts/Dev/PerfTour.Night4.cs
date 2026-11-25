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
                if (kind == "flip") { yield return FlipTest(name, c0, map, roadHas); continue; }
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

        /// <summary>(night5) The leak probe's wait after each switch, in seconds of GAME time (it was 6 + 50
        /// frames: 0.14 s in the hidden exe at ~400 fps, several seconds in the editor - the two did not
        /// measure the same thing). The frame counts stay as a floor.</summary>
        public static float LeakSettleS = 1.5f;

        IEnumerator Shoot(string file, Camera cam, Vector3 eye, Vector3 look, RenderTexture rt, RenderTexture screen, Material blit, Texture2D tex, int W, int H, int settle, float settleS = 0f)
        {
            float t0 = Time.time;
            for (int i = 0; i < settle || Time.time - t0 < settleS; i++)
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
                        if (new Vector2(p.x - hit.point.x, p.z - hit.point.z).magnitude < 3f) continue;
                        // a patch in the solid's full shadow, not at its edge: the ground 4 degrees either side
                        // of it is cut off from the head too
                        bool edge = false;
                        foreach (float side in new[] { -4f, 4f })
                        {
                            var q2 = head + Quaternion.Euler(0f, side, 0f) * (p - head);
                            if (!GroundAt(q2, head.y - 0.5f, out var g2) || !FirstSolid(head + Vector3.down * 0.15f, g2.point + Vector3.up * 0.05f, out _)) { edge = true; break; }
                        }
                        if (edge) continue;
                        float w = 2.4f * Mathf.Pow(Mathf.Max(0f, 1f - r * r / 324f), 2f) + 0.2f * Mathf.Pow(Mathf.Max(0f, 1f - r * r / 1600f), 2f);
                        if (w > bestScore) { bestScore = w; bestHead = head; bestP = p; bestB = bk + "@" + new Vector2(hit.point.x - head.x, hit.point.z - head.z).magnitude.ToString("0.0", CultureInfo.InvariantCulture) + "m"; }
                    }
                }
            }
            if (bestScore <= 0f) { Say("LEAK " + name + ": no lamp-" + want + " spot within 220 m"); leakCsv.AppendLine(name + ",lamp-" + want + ",-,none,-1,-1,no spot"); yield break; }
            var eye = EyeFor(bestP, bestHead);
            Vector3 offLamp = new Vector3(bestHead.x + 1e6f, 0f, 0f);
            StreetLights.FieldLampsNear(bestHead, 900f, n4Heads);
            foreach (var hh in n4Heads) if ((hh - bestHead).sqrMagnitude > 300f * 300f) { offLamp = hh; break; }
            string where = "head " + V(bestHead) + " patch " + V(bestP) + " plan " + new Vector2(bestP.x - bestHead.x, bestP.z - bestHead.z).magnitude.ToString("0.0", CultureInfo.InvariantCulture) + " m";
            Say("LEAK " + name + " lamp-" + want + ": " + where + " blocker " + bestB);
            bool n4Was = StreetLights.Night4;
            var variants = new (string v, bool n4, bool solo)[] { ("n3_on", false, true), ("n3_off", false, false), ("n4_on", true, true), ("n4_off", true, false) };
            foreach (var vr in variants)
            {
                StreetLights.Night4 = vr.n4;
                // "off": another lamp alone, 300 m+ away (the field stays on, so the city-night regime is the same)
                StreetLights.N4SoloAt = vr.solo ? bestHead : offLamp;
                StreetLights.N4SoloM = 0.6f;
                StreetLights.N4Touch();
                yield return Shoot("leak_" + name + "_" + vr.v + ".jpg", cam, eye, bestP, rt, screen, blit, tex, W, H, 70, LeakSettleS);
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
                        if (new Vector2(p.x - hit.point.x, p.z - hit.point.z).magnitude < 3f) continue;
                        // a solid over the lamps' height (a Jersey, a wall, a building, rock: a low kerb's real
                        // shadow is a metre or two) and the patch in its full shadow, not at its edge
                        var lampFlat = new Vector3(p.x, lamp.y, p.z);
                        if (!FirstSolid(lamp, lampFlat, out var hh2) || Vector3.Dot(hh2.normal, Vector3.up) > 0.8f) continue;
                        bool edgeC = false;
                        foreach (float side in new[] { -5f, 5f })
                        {
                            var d2 = Quaternion.Euler(0f, side, 0f) * (lampFlat - lamp);
                            if (!FirstSolid(lamp, lamp + d2, out var hs) || Vector3.Dot(hs.normal, Vector3.up) > 0.8f) { edgeC = true; break; }
                        }
                        if (edgeC) continue;
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
            // (night5) the patch's distance from the fan's origin: the patch is lit while the reach toward it is past it
            float patchD = -1f;
            foreach (var vr in variants)
            {
                StreetLights.Night4 = vr.n4; StreetLights.N4Touch();
                CarLights.ToolPoolOnly = vr.pool;
                if (cl != null) cl.enabled = vr.on;
                // (night5) the frames and seconds from the switch until the reach toward the patch first ends
                // short of it (n4 variants with the lamps on): how long the patch could take light it should not
                float tSw = Time.time; int fSw = 0; string trace = "";
                bool traced = false;
                float t6 = Time.time;
                for (int i = 0; i < 6 || Time.time - t6 < 0.1f; i++)
                {
                    car.ResetTo(bPos + Vector3.up * 0.45f, rot, seated: true); Pump(); yield return null; fSw++;
                    if (vr.n4 && vr.on && cl != null && !traced) { if (patchD < 0f) patchD = cl.OccPatchDist(bP); if (cl.OccReachToward(bP) >= 0f && cl.OccReachToward(bP) < patchD) { traced = true; trace = " cut " + fSw + "f " + (Time.time - tSw).ToString("0.000", CultureInfo.InvariantCulture) + "s"; } }
                }
                yield return Shoot("leak_" + name + "_" + vr.v + ".jpg", cam, eye, bP, rt, screen, blit, tex, W, H, 50, LeakSettleS);
                float y = PatchY(cam, tex, W, H, bP);
                float pool = CarLights.PoolAt(bP);
                string det = cl != null ? "reach " + cl.OccReachToward(bP).ToString("0.0", CultureInfo.InvariantCulture) + " patchD " + patchD.ToString("0.0", CultureInfo.InvariantCulture) + trace : "";
                leakCsv.AppendLine(name + ",car-" + want + "," + bB + "," + vr.v + "," + y.ToString("0.0", CultureInfo.InvariantCulture) + "," + pool.ToString("0.0000", CultureInfo.InvariantCulture) + "," + where + " " + det);
            }
            CarLights.ToolPoolOnly = false;
            // (night5) THE SHRINK: the beam's reaches set clear on every bearing (as if the car had just turned
            // toward the barrier or met its end from open road), then frame by frame until the reach toward
            // the patch ends short of it; frames and game seconds, at this run's own frame rate
            if (cl != null && patchD > 0f)
            {
                StreetLights.Night4 = true; StreetLights.N4Touch(); cl.enabled = true;
                for (int i = 0; i < 30; i++) { car.ResetTo(bPos + Vector3.up * 0.45f, rot, seated: true); Pump(); yield return null; }
                cl.ToolOccClear();
                float ts = Time.time; int fs = 0; string res = "never in 2 s";
                while (Time.time - ts < 2f && fs < 4000)
                {
                    car.ResetTo(bPos + Vector3.up * 0.45f, rot, seated: true); Pump(); yield return null; fs++;
                    float rr = cl.OccReachToward(bP);
                    if (rr >= 0f && rr < patchD) { res = fs + " frames " + (Time.time - ts).ToString("0.000", CultureInfo.InvariantCulture) + " s (mean frame " + ((Time.time - ts) / fs * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " ms)"; break; }
                }
                Say("LEAK " + name + " shrink: the patch cut after " + res);
                leakCsv.AppendLine(name + ",car-" + want + "," + bB + ",shrink,-1,-1," + res);
            }
            StreetLights.Night4 = n4Was; StreetLights.N4Touch();
            if (cl != null) cl.enabled = false;
        }

        /// <summary>(PerfTour.Stage.cs) On a stage with &amp;leak=: the rock-face leak and three night
        /// frames of the stage from the game's chase camera, NIGHT 4 off and on.</summary>
        /// <summary>(night5) NIGHT 4 SWITCHED ON MID-DRIVE (leak SPEC kind "flip": name~lat~lon~flip~RoadNameHas). The
        /// car on that road, NIGHT 4 off; switched on as the LAB row does (the static, then N4Touch); frame by frame:
        /// the frame's ms, the lamps found, the field lamps within 400 m NIGHT 3 lights that NIGHT 4 holds back (no
        /// view yet), the player car's fan known. Then off, 150 m on down the road (2 m a frame), on again; the same.
        /// Rows "flip" in &lt;tag&gt;_leak.csv.</summary>
        IEnumerator FlipTest(string name, Vector3 c0, CityMap map, string roadHas)
        {
            var ci = CultureInfo.InvariantCulture;
            // a stretch of the named road near c0, 200 m of it straight enough to drive down
            Vector3 pos = c0, fwd = Vector3.forward; float best = float.MaxValue;
            foreach (var e in map.edges)
            {
                if (e.link || e.bridge || e.tunnel || e.length < 60f) continue;
                if (!string.IsNullOrEmpty(roadHas) && (e.name == null || e.name.IndexOf(roadHas, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                var q = e.PointAt(e.length * 0.5f);
                float d2 = (new Vector2(q.x - c0.x, q.y - c0.z)).sqrMagnitude;
                if (d2 < best) { best = d2; var tt = e.TangentAt(e.length * 0.5f); fwd = new Vector3(tt.x, 0f, tt.y).normalized; pos = new Vector3(q.x, e.YAt(e.length * 0.5f), q.y) + Vector3.Cross(Vector3.up, fwd) * Mathf.Clamp(e.width * 0.25f, 1.6f, 5.5f); }
            }
            pos = Ground(pos) + Vector3.up * 0.45f;
            var rot = Quaternion.LookRotation(fwd);
            var cl = car.GetComponentInChildren<CarLights>(true);
            if (cl != null) cl.enabled = true;
            var cam = Camera.main;
            var chase = cam != null ? cam.GetComponent<ChaseCamera>() : null;
            if (chase != null) chase.enabled = true;
            bool n4Was = StreetLights.Night4;
            for (int pass = 0; pass < 2; pass++)
            {
                StreetLights.Night4 = false; StreetLights.N4Touch();
                if (pass == 0)
                {
                    for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 3f;) { car.ResetTo(pos, rot, seated: true); Pump(); yield return null; }
                }
                else
                {
                    // 150 m on down the road with NIGHT 4 off, 2 m a frame (the ring streams as it would)
                    for (float m = 0f; m <= 150f; m += 2f) { var p = Ground(pos + fwd * m) + Vector3.up * 0.45f; car.ResetTo(p, rot, seated: true); Pump(); yield return null; }
                    pos = Ground(pos + fwd * 150f) + Vector3.up * 0.45f;
                    for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 1f;) { car.ResetTo(pos, rot, seated: true); Pump(); yield return null; }
                }
                int held0 = StreetLights.N4WithheldNear(pos, 400f, out int lit0);
                // switched on as the LAB row does
                float tOn = Time.realtimeSinceStartup;
                StreetLights.Night4 = true; StreetLights.N4Touch();
                float worst = 0f; int worstF = -1, clearF = -1, fanF = -1, heldMax = 0;
                var sb = new StringBuilder();
                for (int f = 0; f < 240; f++)
                {
                    float fs = Time.realtimeSinceStartup;
                    car.ResetTo(pos, rot, seated: true); Pump(); yield return null;
                    float ms = (Time.realtimeSinceStartup - fs) * 1000f;
                    if (ms > worst) { worst = ms; worstF = f; }
                    int held = StreetLights.N4WithheldNear(pos, 400f, out int lit);
                    heldMax = Mathf.Max(heldMax, held);
                    if (held == 0 && clearF < 0 && !StreetLights.N4BatchOut) clearF = f;
                    if (fanF < 0 && cl != null && cl.OccKnownNow) fanF = f;
                    if (f < 8 || f % 30 == 0) sb.Append(" f" + f + ":" + ms.ToString("0", ci) + "ms/held" + held + "/" + lit);
                    if (f > 20 && clearF >= 0 && fanF >= 0) break;
                }
                string res = "pass " + (pass == 0 ? "first flip" : "after 150 m off") + ": NIGHT 3 lit " + lit0 + " lamps within 400 m (held before the flip " + held0 + "); " +
                             "lamps found at the flip " + StreetLights.N4FlipLamps + " in " + StreetLights.N4LastMs.ToString("0.0", ci) + " ms (N4LastMs); worst frame " + worst.ToString("0.0", ci) + " ms at f" + worstF +
                             "; held back max " + heldMax + ", none from f" + clearF + "; player fan known at f" + fanF + " | " + sb;
                Say("FLIP " + name + " " + res);
                leakCsv.AppendLine(name + ",flip,-,pass" + pass + ",-1,-1," + res.Replace(',', ';'));
            }
            StreetLights.Night4 = n4Was; StreetLights.N4Touch();
            if (cl != null) cl.enabled = false;
            if (chase != null) chase.enabled = false;
        }

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
                            string stem = "sky_" + f[0] + "_" + pv.name + "_h" + (h * 45).ToString("000") + "_e" + el.ToString("00");
                            yield return Shoot(stem + ".jpg", cam, pos, look, rt, screen, blit, tex, W, H, 3);
                            // the sky alone (nothing on any layer drawn): the step check's frame
                            int mask = cam.cullingMask;
                            cam.cullingMask = 0;
                            Capture(cam, rt, screen, blit, tex, W, H);
                            cam.cullingMask = mask;
                            var png = tex.EncodeToJPG(97);
                            PSXPerfTour_Post(tag + "_" + stem + "_skyonly.jpg", png, png.Length);
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
