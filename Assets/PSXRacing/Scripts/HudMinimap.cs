using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.City;

namespace PSXRacing
{
    /// <summary>
    /// THE ONE DRIVING MAP, after Need for Speed (2015) — free roam and every
    /// race type (city routes, mountain stages, the beach, the strips).
    ///
    /// The owner, 2026-10-26, beside an NFS screenshot: "I want city and race
    /// maps to be more like NFS (not with orange square blocking view)". What
    /// it replaces were two maps: an opaque amber-framed square of Charlotte's
    /// streets in free roam (CityMinimap) and a north-up white outline of the
    /// whole venue in a race (TrackCatalog.Thumbnail). Both sat mid-left, at
    /// the height of the road ahead.
    ///
    /// What the reference does, and so what this does:
    /// - NO BOX. The roads are drawn straight over the picture, faint and thin;
    ///   the only backing is a soft dark radial fade that is gone well before
    ///   the edge. Everything fades out toward a circle — no hard rim.
    /// - The ROUTE is the one bright thing: the race line from the car to the
    ///   finish, or in free roam the GPS route along the streets to the current
    ///   destination (the nearest food the signpost names). Cyan, with a thin
    ///   dark edge so it reads over snow and sky.
    /// - HEADING UP, the player's arrow centred and LOW, so most of the circle
    ///   is the road ahead; a tick on the rim says where north went.
    /// - Rivals as warm dots; the finish (or destination) marked, held on the
    ///   rim when it is further than the map reaches; the distance left as a
    ///   small number under the arrow.
    /// - It zooms out with speed, the way the reference does.
    ///
    /// Cost: the roads are one mesh, rebuilt only when the car has moved or
    /// turned past a threshold (and never more than ~15 times a second), on
    /// its own nested Canvas. Between rebuilds the mesh is MOVED, not rebuilt:
    /// its transform carries the car's motion since the last build, so the map
    /// scrolls and turns smoothly at the frame rate for the price of three
    /// property writes. The markers (rivals, rim marks, the arrow) are a few
    /// dozen vertices rebuilt every frame. Nothing allocates per frame.
    /// </summary>
    // CanvasRenderer REQUIRED, as Image and Text declare it: Graphic only
    // adds one itself in a player, and in the editor its GetComponent returns
    // a fake null - the graphic is created, registered and never drawn.
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public class HudMinimap : MaskableGraphic
    {
        // ------------------------------------------------------------------
        //  What it draws
        // ------------------------------------------------------------------
        /// <summary>The player's car.</summary>
        public Transform car;
        /// <summary>Charlotte's street network, drawn faint. Null on venues
        /// with no street graph (stages, circuits, strips, the beach).</summary>
        public CityMap streets;
        /// <summary>The race route. Null in free roam.</summary>
        public TrackPath path;
        /// <summary>The race: rivals and the player's progress. Null in free
        /// roam and in edit-mode previews.</summary>
        public RaceManager race;
        /// <summary>Free roam: the race gates on the map and a GPS route to
        /// <see cref="SetDestination"/>.</summary>
        public bool freeRoam;

        /// <summary>The whole widget, to hide, show and destroy.</summary>
        public GameObject Root { get; private set; }

        // ------------------------------------------------------------------
        //  Look
        // ------------------------------------------------------------------
        // Faint roads: a cool white at low alpha, brighter and wider by class.
        static readonly Color32 ColLocal = new Color32(205, 214, 228, 58);
        static readonly Color32 ColMinor = new Color32(210, 219, 232, 78);
        static readonly Color32 ColMajor = new Color32(218, 226, 238, 104);
        static readonly Color32 ColFwy = new Color32(224, 231, 242, 130);
        /// <summary>A stage or circuit with no streets: its own road, faint.</summary>
        static readonly Color32 ColTrack = new Color32(218, 226, 238, 118);
        /// <summary>The route (NFS's blue): bright and opaque.</summary>
        static readonly Color32 ColRoute = new Color32(64, 196, 255, 255);
        static readonly Color32 ColRouteEdge = new Color32(0, 12, 28, 120);
        static readonly Color32 ColArrow = new Color32(255, 255, 255, 255);
        static readonly Color32 ColArrowEdge = new Color32(0, 10, 24, 220);
        /// <summary>Rivals: warm, so they never read as part of the blue route.</summary>
        static readonly Color32 ColRival = new Color32(255, 112, 64, 255);
        static readonly Color32 ColDotEdge = new Color32(12, 8, 6, 230);
        static readonly Color32 ColGate = new Color32(255, 214, 90, 255);
        static readonly Color32 ColNorth = new Color32(255, 255, 255, 200);
        static readonly Color32 ColDest = new Color32(64, 196, 255, 255);
        static readonly Color ColFade = new Color(0f, 0.015f, 0.05f, 0.30f);

        /// <summary>Metres from the arrow to the edge of the circle sideways
        /// (and half the circle's width), standing still and flat out. The
        /// arrow sits low, so the road ahead reaches ~1.4x this.</summary>
        public const float RangeMinM = 230f, RangeMaxM = 420f;
        /// <summary>Speeds the zoom runs between, m/s (~30 and ~180 km/h).</summary>
        const float ZoomSlowMps = 8f, ZoomFastMps = 50f;
        /// <summary>Where the arrow sits, as a fraction of the half-size below
        /// the centre.</summary>
        public const float ArrowDrop = 0.45f;
        /// <summary>The circle everything fades out toward, as a fraction of
        /// the half-size; fully drawn inside <see cref="FadeInFrac"/> of it.</summary>
        const float CircleFrac = 0.98f, FadeInFrac = 0.52f;

        const float RefreshS = 0.066f;
        const float MoveM = 1.5f, TurnDeg = 1.5f, ZoomStep = 0.03f;
        /// <summary>A canvas mesh holds 65k vertices.</summary>
        const int MaxVerts = 60000;

        // ------------------------------------------------------------------
        //  Parts
        // ------------------------------------------------------------------
        RectTransform contentRT;
        MinimapMarks marks;
        Text distText;
        float half = 64f;

        // the pose the road mesh was built at, and the live one
        Vector2 bc; float bYaw, bScale = 1f, bcs = 1f, bsn;
        Vector2 lc; float lYaw, lScale = 1f, lcs = 1f, lsn;
        bool built;
        float rangeM = RangeMinM;
        float nextBuild;
        int dataVersion, builtVersion = -1;

        // ------------------------------------------------------------------
        //  Build
        // ------------------------------------------------------------------
        /// <summary>
        /// Stand the map up on <paramref name="hud"/> at a square of
        /// <paramref name="size"/> units whose CENTRE is at
        /// <paramref name="centre"/> from <paramref name="anchor"/>.
        /// </summary>
        public static HudMinimap Create(Transform hud, Vector2 anchor, Vector2 centre, float size,
                                        Transform car, CityMap streets, TrackPath path, RaceManager race,
                                        bool freeRoam)
        {
            var root = new GameObject("Minimap", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
            rt.sizeDelta = new Vector2(size, size);

            var fade = Child<MinimapFade>(root.transform, "Fade");
            fade.raycastTarget = false;

            // The roads on a nested canvas of their own: the per-frame move
            // re-batches this mesh only, never the rest of the HUD.
            var contentGO = new GameObject("Roads", typeof(RectTransform));
            contentGO.transform.SetParent(root.transform, false);
            var crt = (RectTransform)contentGO.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(size, size);
            contentGO.AddComponent<Canvas>();
            var mm = contentGO.AddComponent<HudMinimap>();
            mm.raycastTarget = false;
            mm.car = car; mm.streets = streets; mm.path = path; mm.race = race; mm.freeRoam = freeRoam;
            mm.Root = root;
            mm.contentRT = crt;
            mm.half = size * 0.5f;

            mm.marks = Child<MinimapMarks>(root.transform, "Marks");
            mm.marks.raycastTarget = false;
            mm.marks.owner = mm;

            // The distance left, under the arrow.
            var tgo = new GameObject("Distance", typeof(RectTransform));
            tgo.transform.SetParent(root.transform, false);
            var t = tgo.AddComponent<SafeText>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = DistFontSize;
            t.color = new Color(0.62f, 0.88f, 1f);
            t.alignment = TextAnchor.UpperCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            HudOnTop.AddOutline(tgo);
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 1f);
            float px = mm.half / 72f;
            trt.anchoredPosition = new Vector2(0f, -mm.half * ArrowDrop - 10f * px);
            trt.sizeDelta = new Vector2(size, DistFontSize + 4f);
            t.text = "";
            mm.distText = t;

            HudOnTop.Apply(root);
            return mm;
        }

        /// <summary>The distance readout's type size, HUD units (the lap
        /// counter's 12: the same weight as the other small readouts).</summary>
        public const int DistFontSize = 12;

        static T Child<T>(Transform parent, string name) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        /// <summary>The markers' graphic, for the tools.</summary>
        public MinimapMarks Marks => marks;

        /// <summary>The distance readout, for the HUD preview's text checks.</summary>
        public Text DistanceText => distText;

        // ------------------------------------------------------------------
        //  Free-roam destination (GPS)
        // ------------------------------------------------------------------
        Vector2 dest; bool destValid;
        Vector2 solvedDest = new Vector2(float.NaN, 0f);
        float nextSolve;
        readonly MinimapGps gps = new MinimapGps();
        int gpsIdx;

        /// <summary>Where free roam is heading (the nearest food the signpost
        /// names). The route is solved along the streets, again only when the
        /// destination moves or the car leaves the route.</summary>
        public void SetDestination(Vector2 at, bool valid)
        {
            destValid = valid;
            dest = at;
            if (!valid && gps.Valid) { gps.Clear(); dataVersion++; }
        }

        /// <summary>Metres along the GPS route from the car to the
        /// destination's kerb; false with no route.</summary>
        public bool GpsRemaining(out float m)
        {
            m = 0f;
            if (!freeRoam || !destValid || !gps.Valid || car == null) return false;
            m = RouteRemaining(new Vector2(car.position.x, car.position.z));
            return true;
        }

        float RouteRemaining(Vector2 c)
        {
            var pts = gps.pts;
            int i = Mathf.Clamp(gpsIdx, 0, pts.Count - 2);
            Vector2 a = pts[i], b = pts[i + 1], d = b - a;
            float L2 = d.sqrMagnitude;
            float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
            return Mathf.Max(0f, gps.Total - (gps.arc[i] + Mathf.Sqrt(L2) * t));
        }

        /// <summary>Follow the car along the route: the nearest vertex in a
        /// window round the last one. Distance off the route, metres.</summary>
        float TrackGps(Vector2 c)
        {
            var pts = gps.pts;
            if (pts.Count < 2) return float.MaxValue;
            int lo = Mathf.Max(0, gpsIdx - 12), hi = Mathf.Min(pts.Count - 2, gpsIdx + 80);
            float best = float.MaxValue; int bi = gpsIdx;
            for (int i = lo; i <= hi; i++)
            {
                Vector2 a = pts[i], d = pts[i + 1] - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, d) / L2) : 0f;
                float dd = (a + d * t - c).sqrMagnitude;
                if (dd < best) { best = dd; bi = i; }
            }
            if (bi != gpsIdx) { gpsIdx = bi; dataVersion++; }
            return Mathf.Sqrt(best);
        }

        void TickGps(Vector2 c)
        {
            if (!freeRoam || streets == null) return;
            if (!destValid) return;
            bool moved = float.IsNaN(solvedDest.x) || (dest - solvedDest).sqrMagnitude > 25f;
            bool off = gps.Valid && TrackGps(c) > 40f;
            if ((moved || off || !gps.Valid) && Time.unscaledTime >= nextSolve)
            {
                nextSolve = Time.unscaledTime + (moved ? 0.5f : 2f);
                solvedDest = dest;
                gps.Solve(streets, c, dest);
                gpsIdx = 0;
                if (gps.Valid) TrackGps(c);
                dataVersion++;
            }
        }

        // ------------------------------------------------------------------
        //  Per frame
        // ------------------------------------------------------------------
        void LateUpdate()
        {
            if (!Application.isPlaying || car == null) return;
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Tools only: lay the map out for the car where it stands
        /// (no Update runs outside play mode). Call Canvas.ForceUpdateCanvases
        /// after.</summary>
        public void PreviewNow()
        {
            if (car == null) return;
            if (freeRoam && destValid && streets != null)
            {
                var c = new Vector2(car.position.x, car.position.z);
                solvedDest = dest;
                gps.Solve(streets, c, dest);
                gpsIdx = 0;
                if (gps.Valid) TrackGps(c);
            }
            built = false;
            Tick(0f);
        }

        void Tick(float dt)
        {
            var c = new Vector2(car.position.x, car.position.z);
            float yaw = car.eulerAngles.y;
            TickGps(c);

            // zoom with speed, eased
            float spd = 0f;
            var rb = CarBody();
            if (rb != null) spd = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
            float target = Mathf.Lerp(RangeMinM, RangeMaxM, Mathf.InverseLerp(ZoomSlowMps, ZoomFastMps, spd));
            rangeM = dt <= 0f ? target : rangeM + (target - rangeM) * (1f - Mathf.Exp(-dt / 0.9f));
            float scale = half / rangeM;

            lc = c; lYaw = yaw; lScale = scale;
            lcs = Mathf.Cos(yaw * Mathf.Deg2Rad); lsn = Mathf.Sin(yaw * Mathf.Deg2Rad);

            bool due = Time.unscaledTime >= nextBuild || dt <= 0f;
            bool stale = !built || dataVersion != builtVersion
                || (c - bc).sqrMagnitude > MoveM * MoveM
                || Mathf.Abs(Mathf.DeltaAngle(yaw, bYaw)) > TurnDeg
                || Mathf.Abs(scale / bScale - 1f) > ZoomStep;
            // Far past the threshold (a teleport, a respawn): now, not later.
            bool urgent = built && ((c - bc).sqrMagnitude > rangeM * rangeM * 0.04f
                || Mathf.Abs(Mathf.DeltaAngle(yaw, bYaw)) > 20f);
            if (stale && (due || urgent))
            {
                CapturePose();
                nextBuild = Time.unscaledTime + RefreshS;
                SetVerticesDirty();
            }
            Compensate();
            UpdateDistance();
            if (marks != null) marks.SetVerticesDirty();
        }

        void CapturePose()
        {
            bc = lc; bYaw = lYaw; bScale = lScale; bcs = lcs; bsn = lsn;
            builtVersion = dataVersion;
            built = true;
        }

        /// <summary>Carry the road mesh from the pose it was built at to the
        /// live one: turned about the arrow by the change of heading, scaled
        /// by the change of zoom, slid by the distance driven.</summary>
        void Compensate()
        {
            if (contentRT == null) return;
            float k = lScale / Mathf.Max(1e-6f, bScale);
            float dyaw = Mathf.DeltaAngle(bYaw, lYaw);
            Vector2 d = bc - lc;
            var T = new Vector2(d.x * lcs - d.y * lsn, d.x * lsn + d.y * lcs) * lScale;
            var o = ArrowPoint;
            float r = dyaw * Mathf.Deg2Rad, cr = Mathf.Cos(r), sr = Mathf.Sin(r);
            var ro = new Vector2(o.x * cr - o.y * sr, o.x * sr + o.y * cr) * k;
            contentRT.localPosition = new Vector3(o.x - ro.x + T.x, o.y - ro.y + T.y, 0f);
            contentRT.localRotation = Quaternion.Euler(0f, 0f, dyaw);
            contentRT.localScale = new Vector3(k, k, 1f);
        }

        Transform cachedFor;
        CarController carCtl;
        Rigidbody carRb;

        void Cache()
        {
            if (cachedFor == car) return;
            cachedFor = car;
            carCtl = car != null ? car.GetComponent<CarController>() : null;
            carRb = car != null ? car.GetComponent<Rigidbody>() : null;
        }

        CarController CarCtl() { Cache(); return carCtl; }
        Rigidbody CarBody() { Cache(); return carRb; }

        /// <summary>Where the arrow is, in the widget (centre origin).</summary>
        public Vector2 ArrowPoint => new Vector2(0f, -half * ArrowDrop);

        // ------------------------------------------------------------------
        //  The distance readout
        // ------------------------------------------------------------------
        int lastDistKey = int.MinValue;
        float nextDist;

        void UpdateDistance()
        {
            if (distText == null) return;
            if (Application.isPlaying && Time.unscaledTime < nextDist) return;
            nextDist = Time.unscaledTime + 0.25f;
            float m = RemainingM();
            int key = m < 0f ? -1 : Mathf.RoundToInt(m / 10f);
            if (key == lastDistKey) return;
            lastDistKey = key;
            if (key < 0) { distText.text = ""; return; }
            float mm = key * 10f;
            distText.text = mm >= 1000f
                ? (mm / 1000f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " km"
                : Mathf.RoundToInt(mm) + " m";
        }

        /// <summary>Metres to the finish (any race) or to the destination
        /// (free roam), -1 when there is nothing to measure to.</summary>
        float RemainingM()
        {
            if (car == null) return -1f;
            if (freeRoam) return GpsRemaining(out float g) ? g : -1f;
            if (path == null || path.Count < 2) return -1f;
            var cc = CarCtl();
            var p = race != null && cc != null ? race.GetProgress(cc) : null;
            if (p != null)
            {
                float r = race.RemainingToFinishM(p);
                if (r >= 0f) return r;
                return Mathf.Max(0f, (race.totalLaps + 1) * path.TotalLength - p.progress);
            }
            // a preview: no progress, measure from the nearest station
            int idx = path.NearestIndex(car.position);
            if (path.HasEnds)
                return Mathf.Max(0f, ((path.finishIndex > 0 ? path.finishIndex : path.Count - 1) - idx) * path.spacing);
            return Mathf.Max(0f, (path.Count - idx) * path.spacing);
        }

        // ------------------------------------------------------------------
        //  The road mesh
        // ------------------------------------------------------------------
        float rc, rIn, pxu;
        readonly HashSet<int> segs = new HashSet<int>();

        Vector2 Local(Vector2 w)
        {
            float dx = w.x - bc.x, dz = w.y - bc.y;
            return new Vector2((dx * bcs - dz * bsn) * bScale, (dx * bsn + dz * bcs) * bScale - half * ArrowDrop);
        }

        Vector2 Local(Vector3 w) => Local(new Vector2(w.x, w.z));

        /// <summary>Vertices in the last road mesh, for the tools.</summary>
        public int LastVerts { get; private set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            LastVerts = 0;
            if (car == null || !built) return;
            PopulateRoads(vh);
            LastVerts = vh.currentVertCount;
        }

        void PopulateRoads(VertexHelper vh)
        {
            if (half < 8f) return;
            rc = half * CircleFrac;
            rIn = rc * FadeInFrac;
            pxu = half / 72f;
            float rangeHere = rc / Mathf.Max(1e-6f, bScale);
            // the circle's centre in the world: ahead of the car by the drop
            var fwd = new Vector2(bsn, bcs);
            var centreW = bc + fwd * (half * ArrowDrop / Mathf.Max(1e-6f, bScale));
            float reach = rangeHere * 1.06f;

            // the streets, least important first so a freeway is drawn over
            // the service road beside it
            if (streets != null)
            {
                segs.Clear();
                streets.EdgeSegsInRect(centreW - Vector2.one * reach, centreW + Vector2.one * reach, segs);
                for (int pass = 0; pass < 4; pass++)
                {
                    foreach (var packed in segs)
                    {
                        int ei = packed >> 12, si = packed & 0xFFF;
                        var e = streets.edges[ei];
                        int rank = e.link || e.cls >= 5 ? 3 : e.cls >= 3 ? 2 : e.cls >= 1 ? 1 : 0;
                        if (rank != pass || si + 1 >= e.pts.Length) continue;
                        Color32 col; float wd;
                        if (rank == 3) { col = ColFwy; wd = e.link ? 1.6f : 2.6f; }
                        else if (rank == 2) { col = ColMajor; wd = 2.0f; }
                        else if (rank == 1) { col = ColMinor; wd = 1.6f; }
                        else { col = ColLocal; wd = 1.25f; }
                        FadedLine(vh, Local(e.pts[si]), Local(e.pts[si + 1]), wd * pxu, col, 0f);
                        if (vh.currentVertCount > MaxVerts) break;
                    }
                }
            }

            if (path != null && path.Count >= 2) DrawRace(vh, centreW, reach);
            else if (freeRoam && gps.Valid) DrawGps(vh);
        }

        void DrawRace(VertexHelper vh, Vector2 centreW, float reach)
        {
            int n = path.Count;
            bool ends = path.HasEnds;
            float sp = Mathf.Max(0.5f, path.spacing);
            float reach2 = (reach + sp) * (reach + sp);
            // a stage or circuit has no streets: its own road, faint
            if (streets == null)
            {
                int last = ends ? n - 1 : n;
                for (int i = 0; i < last; i++)
                {
                    Vector3 a = path.waypoints[i], b = path.waypoints[(i + 1) % n];
                    if ((new Vector2(a.x, a.z) - centreW).sqrMagnitude > reach2) continue;
                    FadedLine(vh, Local(a), Local(b), 2.2f * pxu, ColTrack, 0f);
                }
            }

            // the route: from the car to the finish (a lap race: round the
            // lap, short of the stretch just behind the car)
            int idx = CurrentIndex();
            float ahead = AheadM(idx);
            int k = Mathf.Clamp(Mathf.CeilToInt(ahead / sp), 0, n);
            if (k <= 0) return;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = 0; j < k; j++)
                {
                    int i = idx + j, i1 = i + 1;
                    if (ends) { if (i1 >= n) break; }
                    else { i %= n; i1 %= n; }
                    Vector3 a = path.waypoints[i], b = path.waypoints[i1];
                    if ((new Vector2(a.x, a.z) - centreW).sqrMagnitude > reach2) continue;
                    var la = Local(a); var lb = Local(b);
                    if (j == 0) la = ArrowPoint;   // the line starts at the arrow
                    if (pass == 0) FadedLine(vh, la, lb, 5.8f * pxu, ColRouteEdge, 0f);
                    else FadedLine(vh, la, lb, 3.4f * pxu, ColRoute, 0.25f);
                }
            }
        }

        void DrawGps(VertexHelper vh)
        {
            var pts = gps.pts;
            int start = Mathf.Clamp(gpsIdx, 0, pts.Count - 2);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = start; i < pts.Count - 1; i++)
                {
                    var la = Local(pts[i]); var lb = Local(pts[i + 1]);
                    if (i == start) la = ArrowPoint;
                    if (pass == 0) FadedLine(vh, la, lb, 5.8f * pxu, ColRouteEdge, 0f);
                    else FadedLine(vh, la, lb, 3.4f * pxu, ColRoute, 0.25f);
                }
            }
        }

        /// <summary>The player's station on the route.</summary>
        int CurrentIndex()
        {
            var cc = CarCtl();
            var p = race != null && cc != null ? race.GetProgress(cc) : null;
            return p != null ? Mathf.Clamp(p.nearestIdx, 0, path.Count - 1) : path.NearestIndex(car.position);
        }

        /// <summary>How much route to draw ahead of station <paramref name="idx"/>.</summary>
        float AheadM(int idx)
        {
            float L = path.TotalLength, sp = path.spacing;
            if (path.HasEnds)
                return Mathf.Max(0f, ((path.finishIndex > 0 ? path.finishIndex : path.Count - 1) - idx) * sp);
            float rem = RemainingM();
            float lap = L - Mathf.Min(150f, L * 0.3f);
            return rem >= 0f ? Mathf.Min(rem, lap) : lap;
        }

        /// <summary>Where the finish is, for the marks: the baked finish of a
        /// route with ends, the door of a circuit sprint, the line on a lap
        /// race's last lap. False when there is none to show.</summary>
        internal bool FinishPoint(out Vector2 w)
        {
            w = default;
            if (path == null || path.Count < 2) return false;
            int fi = -1;
            if (path.HasEnds) fi = path.finishIndex > 0 ? path.finishIndex : path.Count - 1;
            else if (race != null && race.Sprint) fi = race.sprintFinishIndex;
            else
            {
                float rem = RemainingM();
                if (rem >= 0f && rem < path.TotalLength) fi = 0;
            }
            if (fi < 0 || fi >= path.Count) return false;
            var p = path.waypoints[fi];
            w = new Vector2(p.x, p.z);
            return true;
        }

        internal bool Destination(out Vector2 w)
        {
            w = dest;
            return freeRoam && destValid;
        }

        // ------------------------------------------------------------------
        //  Geometry
        // ------------------------------------------------------------------
        static readonly UIVertex[] quad = new UIVertex[4];

        /// <summary>A line clipped to the circle, its alpha falling to zero
        /// toward the rim (cut into short pieces wherever it crosses the
        /// fade band, since a quad can only fade along its length).</summary>
        void FadedLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 col, float overlap)
        {
            Vector2 d = b - a;
            float A = Vector2.Dot(d, d);
            if (A < 1e-6f) return;
            float B = 2f * Vector2.Dot(a, d), C = Vector2.Dot(a, a) - rc * rc;
            float disc = B * B - 4f * A * C;
            if (disc <= 0f) return;
            float sq = Mathf.Sqrt(disc);
            float t0 = Mathf.Max(0f, (-B - sq) / (2f * A)), t1 = Mathf.Min(1f, (-B + sq) / (2f * A));
            if (t1 <= t0) return;
            Vector2 p0 = a + d * t0, p1 = a + d * t1;
            float r0 = p0.magnitude, r1 = p1.magnitude;
            int n = 1;
            if (r0 > rIn || r1 > rIn)
                n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(A) * (t1 - t0) / (rc * 0.08f)), 1, 12);
            Vector2 prev = p0; byte pa = FadeAlpha(r0, col.a);
            for (int i = 1; i <= n; i++)
            {
                Vector2 q = i == n ? p1 : Vector2.Lerp(p0, p1, i / (float)n);
                byte qa = FadeAlpha(q.magnitude, col.a);
                if (pa > 0 || qa > 0) Quad(vh, prev, q, width, col, pa, qa, overlap);
                prev = q; pa = qa;
            }
        }

        byte FadeAlpha(float r, byte a)
        {
            if (r <= rIn) return a;
            float t = Mathf.Clamp01((r - rIn) / Mathf.Max(1e-3f, rc - rIn));
            return (byte)Mathf.RoundToInt(a * (1f - t * t * (3f - 2f * t)));
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 col, byte aa, byte ba, float overlap)
        {
            var d = b - a;
            float m = d.magnitude;
            if (m < 1e-4f) return;
            d /= m;
            var n = new Vector2(-d.y, d.x) * (width * 0.5f);
            // An opaque line overlaps itself a hair at every joint to hide the
            // seam; a translucent one must not, or every joint is a bead.
            if (overlap > 0f) { a -= d * (width * overlap); b += d * (width * overlap); }
            var ca = col; ca.a = aa;
            var cb = col; cb.a = ba;
            Set(0, a - n, ca); Set(1, a + n, ca); Set(2, b + n, cb); Set(3, b - n, cb);
            vh.AddUIVertexQuad(quad);
        }

        static void Set(int i, Vector2 p, Color32 col)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = col;
            quad[i] = v;
        }

        internal static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = a; vh.AddVert(v);
            v.position = b; vh.AddVert(v);
            v.position = c; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>A filled octagon: a dot that reads as round at HUD size.</summary>
        internal static void Dot(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            int i0 = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = c; vh.AddVert(v);
            for (int k = 0; k < 8; k++)
            {
                float a = (k + 0.5f) * Mathf.PI * 0.25f;
                v.position = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                vh.AddVert(v);
            }
            for (int k = 0; k < 8; k++) vh.AddTriangle(i0, i0 + 1 + k, i0 + 1 + (k + 1) % 8);
        }

        // ------------------------------------------------------------------
        //  The live markers (MinimapMarks draws them)
        // ------------------------------------------------------------------
        internal float Half => half;

        internal Vector2 LiveLocal(Vector2 w)
        {
            float dx = w.x - lc.x, dz = w.y - lc.y;
            return new Vector2((dx * lcs - dz * lsn) * lScale, (dx * lsn + dz * lcs) * lScale - half * ArrowDrop);
        }

        /// <summary>North, as a unit vector on the page.</summary>
        internal Vector2 NorthOnPage => new Vector2(-lsn, lcs);

        internal void Rivals(VertexHelper vh)
        {
            float px = half / 72f, lim = half * CircleFrac * 0.92f;
            if (race != null)
            {
                var cars = race.allCars;
                for (int i = 0; i < cars.Count; i++)
                {
                    var c = cars[i];
                    if (c == null || c.transform == car || !c.gameObject.activeInHierarchy) continue;
                    RivalDot(vh, c.transform.position, px, lim);
                }
            }
            else if (!Application.isPlaying && !freeRoam)
            {
                foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                    if (c.transform != car) RivalDot(vh, c.transform.position, px, lim);
            }
        }

        void RivalDot(VertexHelper vh, Vector3 p, float px, float lim)
        {
            var at = LiveLocal(new Vector2(p.x, p.z));
            if (at.magnitude > lim) return;
            Dot(vh, at, 3.6f * px, ColDotEdge);
            Dot(vh, at, 2.5f * px, ColRival);
        }

        internal void RimMarks(VertexHelper vh)
        {
            float px = half / 72f;
            float edge = half * CircleFrac - 8f * px;
            // the race gates, held on the rim when they are further than the
            // map reaches - the way to them, the way NFS's map points at events
            if (freeRoam)
            {
                var gates = CityRaceStarts.Gates;
                for (int i = 0; i < gates.Count; i++)
                {
                    var at = LiveLocal(gates[i].plan);
                    bool far = at.magnitude > edge;
                    if (far) at *= edge / at.magnitude;
                    Diamond(vh, at, (far ? 5.5f : 6.5f) * px, ColDotEdge);
                    Diamond(vh, at, (far ? 3.8f : 4.8f) * px, ColGate);
                }
            }
            if (Destination(out var dw))
            {
                var at = LiveLocal(dw);
                if (at.magnitude > edge) at *= edge / at.magnitude;
                Dot(vh, at, 5.2f * px, ColDotEdge);
                Dot(vh, at, 4.0f * px, ColDest);
                Dot(vh, at, 1.6f * px, ColArrow);
            }
            if (FinishPoint(out var fw))
            {
                var at = LiveLocal(fw);
                if (at.magnitude > edge) at *= edge / at.magnitude;
                Chequer(vh, at, 4.5f * px);
            }
            // north, on the rim
            var north = NorthOnPage;
            var nAt = north * (half * CircleFrac - 4f * px);
            var side = new Vector2(north.y, -north.x);
            Tri(vh, nAt + north * 3.5f * px, nAt - north * 3f * px + side * 3f * px,
                nAt - north * 3f * px - side * 3f * px, ColNorth);
        }

        static void Diamond(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            Tri(vh, c + new Vector2(0f, r), c + new Vector2(r, 0f), c + new Vector2(0f, -r), col);
            Tri(vh, c + new Vector2(0f, r), c + new Vector2(0f, -r), c + new Vector2(-r, 0f), col);
        }

        /// <summary>A small chequered flag square with a dark edge.</summary>
        static void Chequer(VertexHelper vh, Vector2 c, float h)
        {
            Box(vh, c, h + 1.2f, ColDotEdge);
            float q = h * 0.5f;
            var w = new Color32(255, 255, 255, 255);
            var k = new Color32(20, 20, 24, 255);
            Box(vh, c + new Vector2(-q, q), q, w);
            Box(vh, c + new Vector2(q, q), q, k);
            Box(vh, c + new Vector2(-q, -q), q, k);
            Box(vh, c + new Vector2(q, -q), q, w);
        }

        static void Box(VertexHelper vh, Vector2 c, float h, Color32 col)
        {
            Set(0, c + new Vector2(-h, -h), col); Set(1, c + new Vector2(-h, h), col);
            Set(2, c + new Vector2(h, h), col); Set(3, c + new Vector2(h, -h), col);
            vh.AddUIVertexQuad(quad);
        }

        internal void Arrow(VertexHelper vh)
        {
            float px = half / 72f;
            var o = ArrowPoint;
            ArrowShape(vh, o, 1.35f * px, ColArrowEdge);
            ArrowShape(vh, o, px, ColArrow);
        }

        static void ArrowShape(VertexHelper vh, Vector2 o, float s, Color32 col)
        {
            var tip = o + new Vector2(0f, 7.5f * s);
            var l = o + new Vector2(-5.5f * s, -5.5f * s);
            var r = o + new Vector2(5.5f * s, -5.5f * s);
            var notch = o + new Vector2(0f, -2.2f * s);
            Tri(vh, tip, l, notch, col);
            Tri(vh, tip, notch, r, col);
        }
    }

    /// <summary>
    /// The free-roam GPS: A* over Charlotte's road graph from the car to the
    /// destination's nearest street, one-ways respected, local streets a
    /// shade dearer than the through roads (a GPS prefers the arterial).
    /// Every array is allocated once per map and reused; a solve is a few
    /// milliseconds and happens only when the destination moves or the car
    /// leaves the route.
    /// </summary>
    public sealed class MinimapGps
    {
        public readonly List<Vector2> pts = new List<Vector2>(1024);
        public readonly List<float> arc = new List<float>(1024);
        public bool Valid => pts.Count >= 2;
        public float Total => arc.Count > 0 ? arc[arc.Count - 1] : 0f;

        CityMap map;
        float[] g;
        int[] seen, closed, prevNode, prevEdge;
        int stamp;
        float[] heapK; int[] heapN; int heapCount;
        readonly List<int> chain = new List<int>(512);

        const int MaxPops = 60000;
        const int SeedFwd = -2, SeedBack = -3;

        public void Clear() { pts.Clear(); arc.Clear(); }

        void Ensure(CityMap m)
        {
            if (map == m && g != null) return;
            map = m;
            int n = m.nodes.Length;
            g = new float[n]; seen = new int[n]; closed = new int[n];
            prevNode = new int[n]; prevEdge = new int[n];
            heapK = new float[m.edges.Length * 2 + 64];
            heapN = new int[heapK.Length];
            stamp = 0;
        }

        public bool Solve(CityMap m, Vector2 from, Vector2 to)
        {
            Clear();
            if (m == null || m.nodes == null || m.edges == null || m.nodeEdges == null) return false;
            Ensure(m);
            if (!m.NearestRoadPoint(from, 60f, false, out int es, out float ss, out _)) return false;
            if (!m.NearestRoadPoint(to, 220f, true, out int eg, out float sg, out _)) return false;
            var E = m.edges;
            var S = E[es]; var G = E[eg];
            // The heuristic aims at the KERB, not the lot: the lot can be 200 m
            // off the road, and a straight line to it would overestimate.
            to = G.PointAt(sg);
            stamp++;
            heapCount = 0;

            float best = float.MaxValue;
            int bestNode = -1; bool bestFwd = true; bool direct = false;
            if (es == eg)
            {
                if (sg >= ss) { best = sg - ss; direct = true; }
                else if (!S.oneway) { best = ss - sg; direct = true; }
            }
            Seed(S.b, S.length - ss, SeedFwd, to);
            if (!S.oneway) Seed(S.a, ss, SeedBack, to);

            int pops = 0;
            while (heapCount > 0 && pops++ < MaxPops)
            {
                Pop(out float f, out int n);
                if (closed[n] == stamp) continue;
                if (f >= best) break;
                closed[n] = stamp;
                float gn = g[n];
                // the destination's own street, from either end
                if (n == G.a && gn + sg < best) { best = gn + sg; bestNode = n; bestFwd = true; direct = false; }
                if (n == G.b && !G.oneway && gn + (G.length - sg) < best)
                { best = gn + (G.length - sg); bestNode = n; bestFwd = false; direct = false; }
                var list = m.nodeEdges[n];
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    int ei = list[i];
                    var e = E[ei];
                    if (e.a == e.b) continue;
                    int to2;
                    if (e.a == n) to2 = e.b;
                    else if (e.b == n && !e.oneway) to2 = e.a;
                    else continue;
                    if (closed[to2] == stamp) continue;
                    float cost = e.length * (e.cls == 0 && !e.link ? 1.2f : 1f);
                    float ng = gn + cost;
                    if (seen[to2] == stamp && ng >= g[to2]) continue;
                    seen[to2] = stamp;
                    g[to2] = ng;
                    prevNode[to2] = n;
                    prevEdge[to2] = ei;
                    Push(ng + Vector2.Distance(m.nodes[to2], to), to2);
                }
            }
            if (best == float.MaxValue) return false;

            if (direct) { Emit(S, ss, sg); }
            else
            {
                chain.Clear();
                int n = bestNode;
                int guard = 0;
                while (prevEdge[n] >= 0 && guard++ < 100000) { chain.Add(prevEdge[n]); chain.Add(n); n = prevNode[n]; }
                if (prevEdge[n] == SeedFwd) Emit(S, ss, S.length);
                else Emit(S, ss, 0f);
                for (int i = chain.Count - 2; i >= 0; i -= 2)
                {
                    var e = E[chain[i]];
                    bool fwd = e.b == chain[i + 1];
                    Emit(e, fwd ? 0f : e.length, fwd ? e.length : 0f);
                }
                if (bestFwd) Emit(G, 0f, sg); else Emit(G, G.length, sg);
            }
            return Valid;
        }

        void Seed(int node, float cost, int how, Vector2 to)
        {
            if (seen[node] == stamp && cost >= g[node]) return;
            seen[node] = stamp;
            g[node] = cost;
            prevNode[node] = -1;
            prevEdge[node] = how;
            Push(cost + Vector2.Distance(map.nodes[node], to), node);
        }

        void Emit(CityMap.Edge e, float s0, float s1)
        {
            Add(e.PointAt(s0));
            if (s1 >= s0) { for (int i = 0; i < e.pts.Length; i++) if (e.s[i] > s0 && e.s[i] < s1) Add(e.pts[i]); }
            else { for (int i = e.pts.Length - 1; i >= 0; i--) if (e.s[i] < s0 && e.s[i] > s1) Add(e.pts[i]); }
            Add(e.PointAt(s1));
        }

        void Add(Vector2 p)
        {
            int c = pts.Count;
            if (c > 0)
            {
                float d = Vector2.Distance(pts[c - 1], p);
                if (d < 0.05f) return;
                arc.Add(arc[c - 1] + d);
            }
            else arc.Add(0f);
            pts.Add(p);
        }

        void Push(float k, int n)
        {
            if (heapCount >= heapK.Length) return;   // full: the search degrades, never throws
            int i = heapCount++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapK[p] <= k) break;
                heapK[i] = heapK[p]; heapN[i] = heapN[p];
                i = p;
            }
            heapK[i] = k; heapN[i] = n;
        }

        void Pop(out float k, out int n)
        {
            k = heapK[0]; n = heapN[0];
            int last = --heapCount;
            if (last <= 0) return;
            float lk = heapK[last]; int ln = heapN[last];
            int i = 0;
            while (true)
            {
                int c = i * 2 + 1;
                if (c >= last) break;
                if (c + 1 < last && heapK[c + 1] < heapK[c]) c++;
                if (heapK[c] >= lk) break;
                heapK[i] = heapK[c]; heapN[i] = heapN[c];
                i = c;
            }
            heapK[i] = lk; heapN[i] = ln;
        }
    }
}
