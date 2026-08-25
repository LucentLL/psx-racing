using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// Every circuit in the game, as data.
    ///
    /// One track used to be a `ControlPoints` array inside the scene builder,
    /// which is fine for one track and useless for four: the LifeSim has to be
    /// able to NAME the circuits, quote their length before a race, and draw
    /// them in a picker, and none of that is reachable from editor-only code.
    /// So the shapes live here, in runtime code, and the builder consumes them
    /// through <see cref="Sample"/> — the same resampler the waypoints are
    /// generated from, so the length the menu quotes is the length the car
    /// actually drives rather than a hand-typed number that drifts.
    ///
    /// The scene list in Build Settings is [0] LifeHome then one scene per
    /// entry here, IN THIS ORDER. <see cref="SceneIndex"/> is the only place
    /// that contract is written down.
    /// </summary>
    public static class TrackCatalog
    {
        public class TrackDef
        {
            /// <summary>Catalog key AND scene file name. Both, deliberately: a
            /// track whose scene is called something else is a lookup nobody
            /// can follow.</summary>
            public string id;
            public string name;
            public string blurb;
            /// <summary>Circuit control points in metres (x, z), a closed loop
            /// with a Catmull-Rom spline through them.</summary>
            public Vector2[] controlPoints;

            /// <summary>
            /// Height in metres at each control point, splined by exactly the
            /// same Catmull-Rom as the plan shape so a crest lands where the
            /// corner does. Null, or the wrong length, means a flat circuit —
            /// which is what a drag strip has to be.
            ///
            /// Authored rather than generated. A height field the road is
            /// draped over gives you gradients nobody chose, and the numbers
            /// that matter here are the ones you cannot see in a list: peak
            /// GRADE (these run 5% in town, 10% over the pass) and the vertical
            /// RADIUS of the crests, which has to stay large enough that the
            /// ground mesh under the road does not cut through it between its
            /// own vertices. tools/elevation-check prints both.
            /// </summary>
            public float[] controlHeights;

            /// <summary>
            /// Elevated spans, as (first metre, last metre) along the lap from
            /// the start line. Inside one the road keeps its graded height and
            /// the GROUND does not follow it — it drops away by
            /// <see cref="bridgeDepth"/> into a gorge, and the builder throws a
            /// deck and piers under the tarmac to carry it across.
            ///
            /// Spans are authored rather than inferred from "road is higher
            /// than terrain", because with the terrain built to follow the road
            /// that condition is never true by accident. A bridge is a decision.
            /// </summary>
            public Vector2[] bridges;
            /// <summary>How far the ground falls away beneath a bridge span.
            /// Deep enough that the piers read as structure rather than as
            /// kerbstones.</summary>
            public float bridgeDepth = 9f;

            public float roadWidth = 12f;
            /// <summary>Laps for a full race. Short circuits run more of them so
            /// every race lands near 3.3 km — the distance the fuel and wear
            /// economy was balanced against when there was only one track.
            /// </summary>
            public int laps = 3;

            /// <summary>A straight strip rather than a closed loop. Everything
            /// downstream branches on this: the waypoints are not cyclic, the
            /// road ribbon does not close, the grid stages ON the line instead
            /// of behind it, and the race ends at a distance rather than a lap
            /// count.</summary>
            public bool drag;

            /// <summary>A streamed open city (Charlotte) rather than a circuit.
            /// The scene is built nearly empty and CityWorld generates tiles at
            /// runtime; there is no waypoint loop, no laps, no TrackPath —
            /// every loop-shaped consumer branches on this the way it already
            /// branches on <see cref="drag"/>.</summary>
            public bool city;
            /// <summary>Metres from the line to the traps. 402.336 is a quarter
            /// mile, 201.168 an eighth — spelled out rather than rounded,
            /// because the whole point of a drag strip is the number at the end
            /// of it.</summary>
            public float dragMeters;
            /// <summary>What the HUD calls it. See TrackPath.dragLabel.</summary>
            public string dragLabel = "";
            /// <summary>Shutdown area past the traps. Long enough that a car
            /// doing 250 km/h through the lights has somewhere to stop.</summary>
            public float dragShutdown = 320f;

            float length = -1f;
            /// <summary>Centreline length in metres, measured off the resampled
            /// waypoints rather than declared.</summary>
            public float LengthM
            {
                get
                {
                    if (city) return 0f;   // an open city has no lap to measure
                    if (length < 0f) length = Sample(this, Spacing).Count * Spacing;
                    return length;
                }
            }

            /// <summary>What a full race covers, for the fuel gate and the
            /// pre-race quote. A strip is measured to the TRAPS — the shutdown
            /// area is real distance the car covers, but quoting a quarter mile
            /// as 722 m would be the one number a drag racer would not forgive.
            /// </summary>
            public float RaceMeters => drag ? dragMeters : LengthM * laps;

            /// <summary>Waypoint index the traps sit at, or -1 on a circuit.</summary>
            public int FinishIndex => drag ? Mathf.RoundToInt(dragMeters / Spacing) : -1;

            /// <summary>
            /// Whether this venue has pumps you can pull into mid-race.
            ///
            /// Every circuit does. A drag strip does not, and cannot: the race
            /// is 400 metres in a straight line and ends at the traps, so there
            /// is no point on the run where a forecourt would be reachable.
            /// The pre-race fuel gate branches on this — a strip still has to
            /// be entered with enough fuel for the whole run.
            /// </summary>
            public bool hasFuelStop => !drag && !city;   // city pumps are a follow-up; the fuel truck covers it
        }

        /// <summary>Waypoint spacing, metres. The scene builder reads its own
        /// Spacing from here so the menu and the mesh cannot disagree.</summary>
        public const float Spacing = 4f;

        // Layouts 2-4 were generated as polar loops — r(t) = R(1 + sum a_k
        // cos(k t + phi_k)) — which cannot self-intersect however the harmonics
        // are tuned, then checked for minimum corner radius (>= 22 m, so a
        // hairpin is tight rather than impossible) and for self-clearance (no
        // two parts of the circuit closer than road + both wall lines).
        public static readonly TrackDef[] All =
        {
            new TrackDef
            {
                id = "CityCircuit",
                name = "SUNSET CITY GP",
                blurb = "Downtown blocks and close walls. The circuit this game was tuned on.",
                roadWidth = 12f,
                laps = 3,
                controlPoints = new[]
                {
                    new Vector2(0, 0),      new Vector2(120, 0),   new Vector2(180, 8),
                    new Vector2(215, 40),   new Vector2(220, 95),  new Vector2(205, 150),
                    new Vector2(230, 205),  new Vector2(215, 260), new Vector2(160, 285),
                    new Vector2(80, 290),   new Vector2(0, 285),   new Vector2(-70, 265),
                    new Vector2(-110, 215), new Vector2(-105, 150),new Vector2(-140, 100),
                    new Vector2(-135, 40),  new Vector2(-90, -5),
                },
                // Downtown on a slope: flat along the pit straight, climbing
                // the whole east side to a bluff at the top of the circuit,
                // then back down through the west. Peak grade 6.8%, which is a
                // steep city street and nothing worse.
                controlHeights = new[]
                {
                    0f,    0.5f,  2f,    5f,    8f,    10f,   11f,   10f,   7f,
                    4f,    2f,    1f,    2f,    4f,    3f,    1f,    0f,
                },
                // The flyover across the top of the bluff, where the road is
                // already 11 m up: the ground drops out from under it into a
                // cutting and the circuit crosses on a deck.
                bridges = new[] { new Vector2(340f, 450f) },
                bridgeDepth = 9f,
            },
            new TrackDef
            {
                id = "HarborPoint",
                name = "HARBOR POINT",
                blurb = "Short, narrow and relentless. Nowhere to put the power down.",
                roadWidth = 10.5f,
                laps = 4,
                controlPoints = new[]
                {
                    new Vector2(142, 0),   new Vector2(109, 45),  new Vector2(79, 79),
                    new Vector2(38, 92),   new Vector2(0, 110),   new Vector2(-53, 128),
                    new Vector2(-89, 89),  new Vector2(-94, 39),  new Vector2(-122, 0),
                    new Vector2(-128, -53),new Vector2(-91, -91), new Vector2(-47, -114),
                    new Vector2(0, -106),  new Vector2(32, -78),  new Vector2(80, -80),
                    new Vector2(143, -59),
                },
                // Dock land is flat. The one thing that is not is the lift over
                // the channel on the north side, and everything else here is
                // the approach to it and the run back down. 5.0% at its worst.
                controlHeights = new[]
                {
                    0f,    0.6f,  1.6f,  3.0f,  4.8f,  5.6f,  4.0f,  1.8f,
                    0.4f,  0f,    0f,    0.4f,  0.9f,  0.6f,  0.2f,  0f,
                },
                // The channel itself. Ten metres of water under the deck, which
                // is the only place on this circuit you can see daylight beside
                // the road.
                bridges = new[] { new Vector2(150f, 270f) },
                bridgeDepth = 10f,
            },
            new TrackDef
            {
                id = "RidgePass",
                name = "RIDGE PASS",
                blurb = "Long, wide and flowing, through the trees. Two corners bite.",
                roadWidth = 13f,
                laps = 2,
                controlPoints = new[]
                {
                    new Vector2(295, 0),    new Vector2(221, 50),   new Vector2(170, 84),
                    new Vector2(138, 118),  new Vector2(93, 136),   new Vector2(46, 144),
                    new Vector2(0, 178),    new Vector2(-70, 222),  new Vector2(-152, 224),
                    new Vector2(-212, 180), new Vector2(-243, 119), new Vector2(-241, 55),
                    new Vector2(-204, 0),   new Vector2(-177, -40), new Vector2(-195, -96),
                    new Vector2(-198, -169),new Vector2(-138, -204),new Vector2(-57, -181),
                    new Vector2(0, -154),   new Vector2(48, -152),  new Vector2(107, -157),
                    new Vector2(180, -153), new Vector2(268, -131), new Vector2(323, -74),
                },
                // The reason the circuit is called a pass. 28 m from the valley
                // floor to the summit at the north end, down the far side into
                // a second, lower saddle, and a long descent home. Peak grade
                // 9.8% — a real mountain road, and the steepest thing in the
                // game.
                controlHeights = new[]
                {
                    0f,    5f,    10.5f, 15f,   19f,   22.5f, 26f,   28.5f,
                    27f,   23f,   18f,   13f,   9f,    10f,   15f,   18.5f,
                    17f,   12.5f, 8f,    5.5f,  6.5f,  6.5f,  4.5f,  1.5f,
                },
                // The viaduct over the gorge on the second saddle, 140 m of it
                // with the floor 14 m down. The road is around 18 m up here, so
                // what you are looking at over the parapet is a long way.
                bridges = new[] { new Vector2(920f, 1060f) },
                bridgeDepth = 14f,
            },
            new TrackDef
            {
                id = "AirfieldSprint",
                name = "AIRFIELD SPRINT",
                blurb = "Two long straights joined by hairpins. Gearing decides this one.",
                roadWidth = 14f,
                laps = 2,
                controlPoints = new[]
                {
                    new Vector2(331, 0),   new Vector2(276, 62),  new Vector2(170, 88),
                    new Vector2(90, 96),   new Vector2(30, 106),  new Vector2(-30, 106),
                    new Vector2(-90, 96),  new Vector2(-170, 88), new Vector2(-276, 62),
                    new Vector2(-331, 0),  new Vector2(-276, -62),new Vector2(-170, -88),
                    new Vector2(-90, -96), new Vector2(-30, -106),new Vector2(30, -106),
                    new Vector2(90, -96),  new Vector2(170, -88), new Vector2(276, -62),
                },
                // An airfield is flat, and this one stays flat — 2 m of drainage
                // camber across the whole site, 1.3% at its worst. It is the
                // circuit that proves gearing rather than gradient decides a
                // race, and putting a hill on it would take that away.
                controlHeights = new[]
                {
                    0f,   0.8f,  1.6f,  2.0f,  1.6f,  1.6f,  2.0f,  1.6f,  0.8f,
                    0f,  -0.8f, -1.6f, -2.0f, -1.6f, -1.6f, -2.0f, -1.6f, -0.8f,
                },
            },

            // The strips. Wide enough for four lanes because the grid is four
            // cars; a real strip is two, and the LifeSim can send one opponent
            // when it wants a proper heads-up run.
            new TrackDef
            {
                id = "DragQuarter",
                name = "DRAG STRIP — 1/4 MILE",
                blurb = "402 m in a straight line. Gearing, launch, and nothing else.",
                roadWidth = 18f,
                laps = 1,
                drag = true,
                dragMeters = 402.336f,
                dragLabel = "1/4 MILE",
            },
            new TrackDef
            {
                id = "DragEighth",
                name = "DRAG STRIP — 1/8 MILE",
                blurb = "201 m. Over before a long gearbox has finished thinking.",
                roadWidth = 18f,
                laps = 1,
                drag = true,
                dragMeters = 201.168f,
                dragShutdown = 260f,
                dragLabel = "1/8 MILE",
            },
            // LAST, deliberately: every scene index before it holds. The city
            // is not in the race picker (StepTrack skips it) — FREE ROAM on
            // the MAIN tab is its door.
            new TrackDef
            {
                id = "Charlotte",
                name = "CHARLOTTE",
                blurb = "The whole city at 1:1 — uptown to the 485 belt. Free roam.",
                roadWidth = 12f,
                laps = 1,
                city = true,
            },
        };

        public static int Count => All.Length;

        public static TrackDef At(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)];

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return 0;
        }

        /// <summary>Build-settings index of a track's scene. Scene 0 is
        /// LifeHome, so the tracks start at 1.</summary>
        public static int SceneIndex(int trackIndex) =>
            1 + Mathf.Clamp(trackIndex, 0, All.Length - 1);

        /// <summary>
        /// Build-settings index of the walk-in garage.
        ///
        /// LAST, after every circuit, and that is the whole reason it is
        /// expressed as a formula rather than as a number: the track scenes are
        /// addressed by their position in this list, so a scene inserted
        /// anywhere before them would send every race to the wrong circuit.
        /// Adding one at the end costs nothing.
        /// </summary>
        public static int GarageSceneIndex => 1 + All.Length;

        /// <summary>
        /// Dense Catmull-Rom through the control points, then an arc-length
        /// resample at <paramref name="spacing"/> metres. This IS the track: the
        /// road ribbon, the walls, the AI racing line and the length the menu
        /// quotes are all derived from this one list.
        /// </summary>
        public static List<Vector3> Sample(TrackDef def, float spacing)
        {
            // A city has no centreline. Nothing should ask for one; a caller
            // that does gets a token stub rather than a NullReference deep in
            // spline maths it has no business reaching.
            if (def.city)
                return new List<Vector3> { Vector3.zero, new Vector3(spacing, 0f, 0f) };

            // A strip is not a spline. Waypoint 0 IS the start line — the cars
            // stage on it rather than rolling up to it — and the list runs
            // forward to the traps and on through the shutdown area.
            if (def.drag)
            {
                var strip = new List<Vector3>();
                float total = def.dragMeters + def.dragShutdown;
                for (float d = 0f; d <= total + 0.001f; d += spacing)
                    strip.Add(new Vector3(d, 0f, 0f));
                return strip;
            }

            var cps = def.controlPoints;
            int cpCount = cps.Length;
            // The height spline is optional and is checked for LENGTH, not just
            // for null: a heights array that has drifted out of step with the
            // control points would otherwise put a crest on the wrong corner,
            // and every symptom of that is "the track feels wrong" rather than
            // an error.
            var hs = def.controlHeights != null && def.controlHeights.Length == cpCount
                ? def.controlHeights : null;
            var dense = new List<Vector3>(cpCount * 40);
            for (int i = 0; i < cpCount; i++)
            {
                Vector2 p0 = cps[(i - 1 + cpCount) % cpCount];
                Vector2 p1 = cps[i];
                Vector2 p2 = cps[(i + 1) % cpCount];
                Vector2 p3 = cps[(i + 2) % cpCount];
                float h0 = 0f, h1 = 0f, h2 = 0f, h3 = 0f;
                if (hs != null)
                {
                    h0 = hs[(i - 1 + cpCount) % cpCount]; h1 = hs[i];
                    h2 = hs[(i + 1) % cpCount]; h3 = hs[(i + 2) % cpCount];
                }
                for (int s = 0; s < 40; s++)
                {
                    float t = s / 40f;
                    Vector2 pt = 0.5f * ((2f * p1) + (-p0 + p2) * t
                        + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                    float y = hs == null ? 0f
                        : 0.5f * ((2f * h1) + (-h0 + h2) * t
                            + (2f * h0 - 5f * h1 + 4f * h2 - h3) * t * t
                            + (-h0 + 3f * h1 - 3f * h2 + h3) * t * t * t);
                    dense.Add(new Vector3(pt.x, y, pt.y));
                }
            }

            var pts = new List<Vector3>();
            float acc = 0f;
            pts.Add(dense[0]);
            for (int i = 1; i <= dense.Count; i++)
            {
                Vector3 prev = dense[i - 1];
                Vector3 cur = dense[i % dense.Count];
                float d = Vector3.Distance(prev, cur);
                acc += d;
                while (acc >= spacing)
                {
                    float overshoot = acc - spacing;
                    pts.Add(Vector3.Lerp(cur, prev, overshoot / Mathf.Max(d, 0.0001f)));
                    acc = overshoot;
                }
            }
            // Drop the last point if it landed on top of the first
            if (Vector3.Distance(pts[pts.Count - 1], pts[0]) < spacing * 0.5f)
                pts.RemoveAt(pts.Count - 1);
            return pts;
        }

        /// <summary>
        /// How much of a bridge there is at <paramref name="metres"/> along the
        /// lap: 0 on solid ground, 1 out over the middle of a span, and a
        /// cosine ramp in between.
        ///
        /// The ramp is the whole reason this is a fraction rather than a bool.
        /// A hard edge would drop the ground <see cref="TrackDef.bridgeDepth"/>
        /// metres between two adjacent waypoints — a cliff face across the road
        /// at the abutment, which is where the deck is supposed to meet solid
        /// ground. Ramping over <see cref="BridgeRampM"/> gives the gorge sloped
        /// ends and the deck something to land on.
        /// </summary>
        public const float BridgeRampM = 26f;

        public static float BridgeBlend(TrackDef def, float metres)
        {
            if (def == null || def.bridges == null || def.bridges.Length == 0) return 0f;
            float lap = Mathf.Max(def.LengthM, 1f);
            float best = 0f;
            foreach (var span in def.bridges)
            {
                // Measured on the LAP, so a span may legitimately wrap past the
                // start line. Both the distance forward from the start of the
                // span and back from its end are taken modulo the lap.
                float from = Mathf.Repeat(metres - span.x, lap);
                float len = Mathf.Repeat(span.y - span.x, lap);
                if (from > len) continue;                    // outside this span
                float into = Mathf.Min(from, len - from);    // metres from the nearer end
                float t = Mathf.Clamp01(into / BridgeRampM);
                // Cosine rather than linear: a linear ramp leaves a crease in
                // the ground at both ends of it, which on a hillside reads as a
                // modelling seam rather than as a valley.
                best = Mathf.Max(best, 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI));
            }
            return best;
        }

        /// <summary>Centre of a circuit's bounding box, which is where the
        /// ground plane and the sky have to be pinned.</summary>
        public static Bounds BoundsOf(TrackDef def)
        {
            var pts = Sample(def, Spacing);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        // ------------------------------------------------------------------
        //  Picker map
        // ------------------------------------------------------------------
        static readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();

        /// <summary>
        /// A map of the circuit, drawn from the same centreline the road mesh is
        /// built from. Generated rather than authored: four hand-drawn PNGs
        /// would be four things to redraw the moment a corner moves, and the
        /// shape is already in the data.
        ///
        /// Cached per track — a menu rebuild happens on every button press, and
        /// rasterising four circuits per press is not free.
        /// </summary>
        public static Texture2D Thumbnail(TrackDef def, int size = 128)
        {
            if (thumbs.TryGetValue(def.id, out var hit) && hit != null) return hit;

            if (def.city)
            {
                // Baked at scene-build time from the real road graph — parsing
                // 1.5 MB of city JSON to draw a menu chip would be the wrong
                // trade. Missing asset just means no map on the button.
                var baked = Resources.Load<Texture2D>("charlotte_thumb");
                if (baked != null) thumbs[def.id] = baked;
                return baked;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var pts = Sample(def, Spacing);
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);

            // ONE scale for both axes: a 660 m long circuit should read as long,
            // not be stretched to fill the same square as a compact one.
            float span = Mathf.Max(b.size.x, b.size.z);
            float scale = (size - 12) / Mathf.Max(span, 1f);
            float ox = size * 0.5f - b.center.x * scale;
            float oz = size * 0.5f - b.center.z * scale;

            var line = new Color32(255, 204, 64, 255);
            var halo = new Color32(92, 70, 30, 255);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var c = pts[(i + 1) % pts.Count];
                // Waypoints are 4 m apart and the map is at most 128 px across,
                // so consecutive points land on the same pixel or its neighbour:
                // a dot per waypoint plus one at each midpoint draws a
                // continuous ribbon without needing a line rasteriser.
                Plot(px, size, a.x * scale + ox, a.z * scale + oz, line, halo);
                Plot(px, size, (a.x + c.x) * 0.5f * scale + ox,
                               (a.z + c.z) * 0.5f * scale + oz, line, halo);
            }
            var white = new Color32(255, 255, 255, 255);
            Plot(px, size, pts[0].x * scale + ox, pts[0].z * scale + oz, white, white);
            // On a strip the interesting end is the OTHER one: a horizontal bar
            // with one dot on it says nothing about where the traps are.
            if (def.drag && def.FinishIndex > 0 && def.FinishIndex < pts.Count)
            {
                var f = pts[def.FinishIndex];
                var red = new Color32(255, 90, 70, 255);
                Plot(px, size, f.x * scale + ox, f.z * scale + oz, red, red);
            }

            tex.SetPixels32(px);
            tex.Apply();
            thumbs[def.id] = tex;
            return tex;
        }

        static void Plot(Color32[] px, int size, float fx, float fy, Color32 c, Color32 halo)
        {
            int x = Mathf.RoundToInt(fx), y = Mathf.RoundToInt(fy);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sy < 0 || sx >= size || sy >= size) continue;
                    int idx = sy * size + sx;
                    if (dx == 0 && dy == 0) px[idx] = c;
                    else if (px[idx].a == 0) px[idx] = halo;
                }
        }
    }
}
