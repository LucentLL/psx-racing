using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PSXRacing.City;

namespace PSXRacing.EditorTools
{
    /// <summary>
    /// THE CREEK SHOTS (Charlotte R1, 2026-09-29). R1 stood the city on USGS
    /// 3DEP ground and carved the creek beds, and the place to SEE that is a
    /// road dropping into a creek valley. For each (road, creek) pair below
    /// the crossing is FOUND in the city data - the road's polyline cut by
    /// the named creek's - so no coordinate here has to be exact: the lat/lon
    /// only picks between crossings (and, when a creek is not named in the
    /// data, marks where to look for the road's low point instead). The road
    /// is then walked both ways from the creek, through junctions into the
    /// straightest road that carries on, and the camera stands on the higher
    /// side <see cref="CrestMinM"/>-<see cref="CrestMaxM"/> back at
    /// driver's-eye height, facing down the road and pitched at the crossing,
    /// so the road falls to the creek and the far side climbs out of it
    /// inside the fog.
    ///
    /// Plus one WIDE RAISED view: 150 m over the ground west of uptown, past
    /// Stewart Creek, looking east across the Stewart and Irwin Creek valleys
    /// and I-77 to the towers.
    ///
    /// The world is the game's (CityWorld tiles with the game's materials)
    /// under CityRefSpots' daylight, so these sit beside the reference sheet.
    /// 1280x720 PNGs to Screenshots/City/creeks, plus creek_shots.txt: where
    /// each camera stood, the road at the crest and at the creek, the creek
    /// bed under it, and the road's profile every 20 m either side.
    ///
    /// Headless: -executeMethod PSXRacing.EditorTools.CityCreekShots.Run
    /// (tools\city-creek-shots.ps1).
    /// </summary>
    public static class CityCreekShots
    {
        struct Target
        {
            public string id, road, water, alt, what; public double lat, lon;
            public Target(string id, string road, string water, string alt, double lat, double lon, string what)
            { this.id = id; this.road = road; this.water = water; this.alt = alt; this.lat = lat; this.lon = lon; this.what = what; }
        }

        static readonly Target[] Targets =
        {
            new Target("trade_irwin", "Trade", "Irwin", null, 35.2345, -80.8560, "W Trade St down to Irwin Creek"),
            new Target("state_stewart", "State Street", "Stewart", null, 35.2395, -80.8665, "State St down to Stewart Creek"),
            new Target("rozzelles_stewart", "Rozzelles", "Stewart", null, 35.2465, -80.8665, "Rozzelles Ferry Rd down to Stewart Creek"),
            new Target("archdale_littlesugar", "Archdale", "Little Sugar", null, 35.1500, -80.8500, "Archdale Dr down to Little Sugar Creek"),
            new Target("independence_briar", "Independence", "Briar", "Edwards", 35.2109, -80.8014, "the Independence Sprint (US 74) down to Briar Creek"),
            // WP-14: four ordinary streets, no creek named - the camera finds
            // the street's lowest point within 700 m of the spot (the budget
            // probe's sites) and looks down into it, so the land either side
            // of a street on a hill is in the frame
            new Target("street_queens", "Queens Road", null, null, 35.1928, -80.8368, "Queens Rd W, Dilworth / Myers Park, into its dip"),
            new Target("street_central", "Central Avenue", null, null, 35.2202, -80.8090, "Central Ave, Plaza Midwood, into its dip"),
            new Target("street_providence", "Providence Road", null, null, 35.1684, -80.8047, "Providence Rd into its dip"),
            new Target("street_beatties", "Beatties Ford", null, null, 35.4225, -80.9159, "Beatties Ford Rd, rural, into its dip"),
        };

        const float EyeM = 1.2f, FovDeg = 58f, FarM = 500f;
        /// <summary>The camera stands on the higher side, at its highest point
        /// between these distances from the creek (measured along the road).
        /// Close, on purpose: the game's day fog in Charlotte closes at about
        /// 500 m (TimeOfDay's noon band x CircuitFogScale 1.4, the same
        /// 300-500 m as the daylight here), and from 260 m back the far side
        /// of the valley stood in it and every road read flat.</summary>
        const float CrestMinM = 100f, CrestMaxM = 150f, StepM = 10f;
        const int W = 1280, H = 720;

        internal static Vector2 LL(double lat, double lon)
        {
            const double Lat0 = 35.18456015184093, Lon0 = -80.81770185962013;
            double mLon = 111320.0 * System.Math.Cos(Lat0 * System.Math.PI / 180.0);
            return new Vector2((float)((lon - Lon0) * mLon), (float)((lat - Lat0) * 111132.0)) * CityMap.LayoutScale;
        }

        internal static bool NameHas(string name, string part) =>
            !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(part) &&
            name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        internal struct Hit
        {
            public CityMap.Edge edge; public float s; public Vector2 at;
            public CityMap.Water water; public float ws; public string how;
        }

        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
        {
            Vector2 r = b - a, q = d - c;
            float den = r.x * q.y - r.y * q.x;
            t = u = 0f;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 w = c - a;
            t = (w.x * q.y - w.y * q.x) / den;
            u = (w.x * r.y - w.y * r.x) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>Every place a road named <paramref name="road"/> (no
        /// ramps) crosses a creek or ravine named <paramref name="water"/>.</summary>
        internal static List<Hit> Crossings(CityMap map, string road, string water)
        {
            var hits = new List<Hit>();
            if (map.waters == null || string.IsNullOrEmpty(water)) return hits;
            foreach (var w in map.waters)
            {
                if (w.lake || w.pts == null || w.pts.Length < 2 || !NameHas(w.name, water)) continue;
                Vector2 wmn = w.pts[0], wmx = w.pts[0];
                foreach (var q in w.pts) { wmn = Vector2.Min(wmn, q); wmx = Vector2.Max(wmx, q); }
                foreach (var e in map.edges)
                {
                    if (e.link || !NameHas(e.name, road)) continue;
                    Vector2 mn = e.pts[0], mx = e.pts[0];
                    foreach (var q in e.pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                    if (mx.x < wmn.x || mn.x > wmx.x || mx.y < wmn.y || mn.y > wmx.y) continue;
                    for (int i = 0; i + 1 < e.pts.Length; i++)
                        for (int j = 0; j + 1 < w.pts.Length; j++)
                        {
                            if (!SegX(e.pts[i], e.pts[i + 1], w.pts[j], w.pts[j + 1], out float t, out float u)) continue;
                            float ws = w.s != null && w.s.Length == w.pts.Length ? w.s[j] + (w.s[j + 1] - w.s[j]) * u : 0f;
                            hits.Add(new Hit
                            {
                                edge = e, s = e.s[i] + (e.s[i + 1] - e.s[i]) * t,
                                at = Vector2.LerpUnclamped(e.pts[i], e.pts[i + 1], t),
                                water = w, ws = ws, how = "crossing of '" + w.name + "'"
                            });
                        }
                }
            }
            return hits;
        }

        /// <summary>The lowest point of the named road within
        /// <paramref name="r"/> of <paramref name="near"/>.</summary>
        static bool Lowest(CityMap map, string road, Vector2 near, float r, out Hit hit)
        {
            hit = default; float best = float.MaxValue; bool ok = false;
            foreach (var e in map.edges)
            {
                if (e.link || !NameHas(e.name, road)) continue;
                for (float s = 0f; s <= e.length; s += 5f)
                {
                    var p = e.PointAt(s);
                    if (Vector2.Distance(p, near) > r) continue;
                    float y = e.YAt(s);
                    if (y < best) { best = y; hit = new Hit { edge = e, s = s, at = p, how = "lowest point of the road (no named crossing)" }; ok = true; }
                }
            }
            return ok;
        }

        /// <summary>The creek's bed height (above the datum) at arc length
        /// <paramref name="ws"/>, or NaN when the data has no bed.</summary>
        static float BedAt(CityMap.Water w, float ws)
        {
            if (w == null || w.bedY == null || w.bedY.Length == 0 || w.bedStep <= 0f) return float.NaN;
            float f = Mathf.Clamp(ws / w.bedStep, 0f, w.bedY.Length - 1);
            int i = Mathf.Min((int)f, w.bedY.Length - 1), j = Mathf.Min(i + 1, w.bedY.Length - 1);
            return Mathf.Lerp(w.bedY[i], w.bedY[j], f - i);
        }

        struct Sample { public Vector2 p; public float y, along; }

        /// <summary>Carry on through a junction: the straightest non-ramp
        /// road leaving the node, the same name preferred.</summary>
        static bool Next(CityMap map, ref CityMap.Edge cur, ref float at, ref int dir)
        {
            int node = dir > 0 ? cur.b : cur.a;
            Vector2 head = cur.TangentAt(dir > 0 ? cur.length : 0f) * dir;
            CityMap.Edge best = null; float bestScore = 0.25f; int bestDir = 1;
            foreach (int k in map.nodeEdges[node])
            {
                var c = map.edges[k];
                if (c == cur || c.link) continue;
                int cd; Vector2 outT;
                if (c.a == node) { cd = 1; outT = c.TangentAt(0f); }
                else if (c.b == node) { cd = -1; outT = -c.TangentAt(c.length); }
                else continue;
                float score = Vector2.Dot(head, outT) + (c.name == cur.name ? 0.5f : 0f);
                if (score > bestScore) { bestScore = score; best = c; bestDir = cd; }
            }
            if (best == null) return false;
            cur = best; dir = bestDir; at = bestDir > 0 ? 0f : best.length;
            return true;
        }

        /// <summary>The road every <see cref="StepM"/> from (e, s) in
        /// direction <paramref name="dir"/> (+1 along the points), out to
        /// <paramref name="maxD"/> or the road's end.</summary>
        static List<Sample> Walk(CityMap map, CityMap.Edge e, float s, int dir, float maxD)
        {
            var list = new List<Sample> { new Sample { p = e.PointAt(s), y = e.YAt(s), along = 0f } };
            var cur = e; float at = s, along = 0f; int hops = 0;
            while (along < maxD)
            {
                float r = StepM;
                while (r > 1e-4f)
                {
                    float avail = dir > 0 ? cur.length - at : at;
                    if (r <= avail) { at += dir * r; r = 0f; }
                    else
                    {
                        r -= avail; at = dir > 0 ? cur.length : 0f;
                        if (++hops > 200 || !Next(map, ref cur, ref at, ref dir)) return list;
                    }
                }
                along += StepM;
                list.Add(new Sample { p = cur.PointAt(at), y = cur.YAt(at), along = along });
            }
            return list;
        }

        /// <summary>The highest sample between CrestMinM and CrestMaxM back
        /// (the last one there when the road ends sooner).</summary>
        static int Crest(List<Sample> side)
        {
            int best = -1;
            for (int i = 0; i < side.Count; i++)
            {
                if (side[i].along < CrestMinM || side[i].along > CrestMaxM) continue;
                if (best < 0 || side[i].y > side[best].y) best = i;
            }
            if (best < 0)
            {
                best = side.Count - 1;
                while (best > 0 && side[best].along > CrestMaxM) best--;
            }
            return best;
        }

        [MenuItem("PSX Racing/Preview Charlotte Creek Shots")]
        public static void Run()
        {
            var map = CityMap.Get();
            if (map == null) { Debug.LogError("[CityCreekShots] no city data"); return; }
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "City", "creeks");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);

            PSXRacingBuilder.EnsureCityTextures();
            // CityRefSpots' daylight, so these sit beside the reference sheet
            Shader.SetGlobalFloat("_PSXFogNear", 300f);
            Shader.SetGlobalFloat("_PSXFogFar", FarM);
            Shader.SetGlobalColor("_PSXFogColor", new Color(0.72f, 0.78f, 0.86f));
            Shader.SetGlobalFloat("_PSXSnap", 0f);
            Shader.SetGlobalColor("_PSXAmbient", new Color(0.55f, 0.55f, 0.6f));
            Shader.SetGlobalVector("_PSXLightDir", new Vector4(-0.4f, 0.8f, -0.3f, 0f).normalized);
            Shader.SetGlobalColor("_PSXLightColor", new Color(0.9f, 0.87f, 0.8f));

            var go = new GameObject("~cityCreekShots");
            var world = go.AddComponent<CityWorld>();
            world.materials = PSXRacingBuilder.CityMaterials();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var log = new StringBuilder();
            log.Append("id\tfound\troad\tcreek_x\tcreek_z\troad_y_at_creek\tbed_y\tcrest_y\tdrop_m\tcrest_back_m\teye_x\teye_y\teye_z\twhat\n");
            var profiles = new StringBuilder();
            Vector2? tradeAt = null;
            int shots = 0;
            try
            {
                foreach (var t in Targets)
                {
                    var hint = LL(t.lat, t.lon);
                    var hits = Crossings(map, t.road, t.water);
                    if (hits.Count == 0 && t.alt != null) hits = Crossings(map, t.road, t.alt);
                    Hit hit = default; bool found = false;
                    float bestD = 2500f;
                    foreach (var h in hits)
                    {
                        float d = Vector2.Distance(h.at, hint);
                        if (d < bestD) { bestD = d; hit = h; found = true; }
                    }
                    if (!found) found = Lowest(map, t.road, hint, 700f, out hit);
                    if (!found) { Debug.LogWarning($"[CityCreekShots] {t.id}: no '{t.road}' near the spot"); continue; }

                    float yCreek = hit.edge.YAt(hit.s);
                    float bed = BedAt(hit.water, hit.ws);
                    // 320 m each way: the camera's window, and the profile logged below
                    var fwd = Walk(map, hit.edge, hit.s, +1, 320f);
                    var back = Walk(map, hit.edge, hit.s, -1, 320f);
                    int cf = Crest(fwd), cb = Crest(back);
                    var side = fwd[cf].y >= back[cb].y ? fwd : back;
                    var other = side == fwd ? back : fwd;
                    int ic = side == fwd ? cf : cb;
                    var eyeS = side[ic];
                    var aimS = side[Mathf.Max(0, ic - 8)];

                    var eye = new Vector3(eyeS.p.x, eyeS.y + EyeM, eyeS.p.y);
                    Vector2 yaw = aimS.p - eyeS.p;
                    if (yaw.sqrMagnitude < 1f) yaw = hit.at - eyeS.p;
                    yaw.Normalize();
                    float horiz = Mathf.Max(1f, Vector2.Distance(eyeS.p, hit.at));
                    float pitch = Mathf.Atan2(yCreek + 1.0f - eye.y, horiz);
                    var look = new Vector3(yaw.x * Mathf.Cos(pitch), Mathf.Sin(pitch), yaw.y * Mathf.Cos(pitch));

                    // the ground from the eye down to the creek and up the far side
                    world.EnsureRing(eye, 1);
                    world.EnsureRing(new Vector3(side[ic / 2].p.x, 0f, side[ic / 2].p.y), 1);
                    world.EnsureRing(new Vector3(hit.at.x, 0f, hit.at.y), 1);
                    var far = other[Mathf.Min(other.Count - 1, 25)];
                    world.EnsureRing(new Vector3(far.p.x, 0f, far.p.y), 1);
                    Shoot(dir, t.id, eye, Quaternion.LookRotation(look), FovDeg, FarM);
                    shots++;
                    if (t.id == "trade_irwin") tradeAt = hit.at;

                    log.Append(string.Format(inv, "{0}\t{1}\t{2}\t{3:0.0}\t{4:0.0}\t{5:0.00}\t{6}\t{7:0.00}\t{8:0.0}\t{9:0}\t{10:0.0}\t{11:0.00}\t{12:0.0}\t{13}\n",
                        t.id, hit.how, hit.edge.name, hit.at.x, hit.at.y, yCreek,
                        float.IsNaN(bed) ? "-" : bed.ToString("0.00", inv), eyeS.y, eyeS.y - yCreek, eyeS.along,
                        eye.x, eye.y, eye.z, t.what));
                    // the profile: camera side negative, creek 0, far side positive
                    profiles.Append(t.id).Append(" (m along the road: height above the creek crossing; camera side first)\n  ");
                    for (int i = side.Count - 1; i >= 1; i -= 2)
                        profiles.Append(string.Format(inv, "-{0:0}:{1:+0.0;-0.0} ", side[i].along, side[i].y - yCreek));
                    profiles.Append("| 0:0.0 |");
                    for (int i = 2; i < other.Count; i += 2)
                        profiles.Append(string.Format(inv, " +{0:0}:{1:+0.0;-0.0}", other[i].along, other[i].y - yCreek));
                    profiles.Append("\n");
                    Debug.Log($"[CityCreekShots] {t.id}: {hit.how} on '{hit.edge.name}', road {yCreek:0.0} at the creek, crest {eyeS.y:0.0} {eyeS.along:0} m back (drop {eyeS.y - yCreek:0.0} m)");
                    world.DropAll();
                }

                // THE WIDE RAISED VIEW: west of uptown, over Stewart Creek,
                // looking east over both creek valleys and I-77 to the towers.
                var up = map.uptown;
                Vector2 west = tradeAt.HasValue ? (tradeAt.Value - up).normalized : new Vector2(-0.94f, 0.34f).normalized;
                var eyeXZ = up + west * 2500f;
                float gy = CityElevation.GroundY(map, eyeXZ.x, eyeXZ.y);
                float upY = map.NearestRoadPoint(up, 300f, false, out int ue, out float us, out _) ? map.edges[ue].YAt(us) : gy;
                var eyeW = new Vector3(eyeXZ.x, gy + 150f, eyeXZ.y);
                var target = new Vector3(up.x, upY + 20f, up.y);
                const float wideFov = 34f, wideFar = 3400f;
                // every tile inside the view's wedge, out past uptown
                Vector2 f2 = (up - eyeXZ).normalized, r2 = new Vector2(f2.y, -f2.x);
                float half = Mathf.Atan(Mathf.Tan(wideFov * 0.5f * Mathf.Deg2Rad) * W / H) + 4f * Mathf.Deg2Rad;
                float reach = Vector2.Distance(eyeXZ, up) + 700f;
                float ts = CityMeshes.TileSize;
                int tiles = 0;
                int tx0 = Mathf.FloorToInt((Mathf.Min(eyeXZ.x, up.x) - reach) / ts), tx1 = Mathf.FloorToInt((Mathf.Max(eyeXZ.x, up.x) + reach) / ts);
                int tz0 = Mathf.FloorToInt((Mathf.Min(eyeXZ.y, up.y) - reach) / ts), tz1 = Mathf.FloorToInt((Mathf.Max(eyeXZ.y, up.y) + reach) / ts);
                for (int tz = tz0; tz <= tz1; tz++)
                    for (int tx = tx0; tx <= tx1; tx++)
                    {
                        var c = new Vector2((tx + 0.5f) * ts, (tz + 0.5f) * ts) - eyeXZ;
                        float a = Vector2.Dot(c, f2), l = Mathf.Abs(Vector2.Dot(c, r2));
                        if (a < -ts || a > reach || l > Mathf.Max(0f, a) * Mathf.Tan(half) + ts) continue;
                        world.EnsureTile(tx, tz);
                        tiles++;
                    }
                Shader.SetGlobalFloat("_PSXFogNear", 1400f);
                Shader.SetGlobalFloat("_PSXFogFar", wideFar);
                Shoot(dir, "wide_west_over_creeks", eyeW, Quaternion.LookRotation(target - eyeW), wideFov, wideFar);
                shots++;
                log.Append(string.Format(inv, "wide_west_over_creeks\traised\t-\t-\t-\t-\t-\t-\t-\t-\t{0:0.0}\t{1:0.00}\t{2:0.0}\t150 m over the ground {3:0} m west of uptown, looking east at the towers ({4} tiles, fog 1400-3400 m)\n",
                    eyeW.x, eyeW.y, eyeW.z, Vector2.Distance(eyeXZ, up), tiles));
                Debug.Log($"[CityCreekShots] wide: {tiles} tiles, eye {eyeW}, ground {gy:0.0}");
                world.DropAll();
            }
            finally
            {
                if (world != null) world.DropAll();
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(dir, "creek_shots.txt"), log.ToString() + "\n" + profiles.ToString());
            Debug.Log($"[CityCreekShots] {shots} shots to {dir}");
        }

        internal static void Shoot(string dir, string name, Vector3 pos, Quaternion rot, float fov, float far)
        {
            var camGO = new GameObject("~creekCam");
            var cam = camGO.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.86f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = far;
            cam.fieldOfView = fov;
            var rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
        }
    }
}
