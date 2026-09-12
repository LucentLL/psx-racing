using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing.City
{
    /// <summary>
    /// The free-roam map: every street within a few hundred metres of the
    /// car, heading up, drawn straight out of the road graph as UI geometry.
    ///
    /// The race HUD's corner map is a picture of ONE line (the circuit) with
    /// dots on it, framed once per venue. Charlotte has no line to frame —
    /// 25,000 edges over thirty kilometres — and what a driver in an unknown
    /// city needs is not the whole of it but the next three turns: the GTA
    /// radar. So this is a MaskableGraphic that re-tessellates the streets
    /// around the car into line quads whenever the car has moved a couple of
    /// metres or turned a couple of degrees, ten times a second at most. The
    /// segments come from the same spatial hash the tile builder and the
    /// respawn use (CityMap.EdgeSegsInRect), so the map is the road, not a
    /// drawing of it; a mesh of a thousand quads is nothing to a canvas, and
    /// there is no texture to upload and nothing to bake.
    ///
    /// Heading up rather than north up: the reference radars rotate, and a
    /// player mid-corner can act on "the freeway is up and to the right" and
    /// cannot act on "north-north-east". A tick on the rim says where north
    /// went. Freeways are amber and widest, arterials pale, streets grey,
    /// water blue — the palette of the menu's baked city thumbnail, so the
    /// two maps read as one place.
    /// </summary>
    public class CityMinimap : MaskableGraphic
    {
        public CityMap map;
        public Transform car;
        /// <summary>Metres from the car to the map's edge. The camera's far
        /// plane is 360 m; a map a shade wider than what the fog shows is a
        /// map that tells you something.</summary>
        public float radiusM = 340f;
        /// <summary>The whole widget, for the HUD to rebuild on a resize.</summary>
        public GameObject Root { get; private set; }

        static readonly Color32 ColLocal = new Color32(150, 152, 160, 255);
        static readonly Color32 ColMinor = new Color32(205, 205, 210, 255);   // tertiary, secondary
        static readonly Color32 ColMajor = new Color32(240, 228, 170, 255);   // primary, trunk
        static readonly Color32 ColFwy = new Color32(255, 190, 70, 255);
        static readonly Color32 ColRamp = new Color32(220, 180, 100, 255);
        static readonly Color32 ColWater = new Color32(70, 130, 190, 255);
        static readonly Color32 ColPlayer = new Color32(255, 72, 56, 255);
        static readonly Color32 ColPlayerEdge = new Color32(255, 255, 255, 255);
        static readonly Color32 ColNorth = new Color32(255, 255, 255, 230);

        const float RefreshS = 0.1f;
        const float MoveM = 1.5f, TurnDeg = 1.5f;
        /// <summary>A canvas mesh holds 65k vertices; four per segment.</summary>
        const int MaxSegs = 14000;

        readonly HashSet<int> segs = new HashSet<int>();
        readonly HashSet<int> wsegs = new HashSet<int>();
        Vector3 lastPos = new Vector3(float.NaN, 0f, 0f);
        float lastYaw, nextRefresh;

        /// <summary>
        /// Stand the map up under a HUD: an amber one-pixel frame round a
        /// dark translucent ground, the streets clipped inside it. Placed
        /// where the race map sits (mid-left, above the tach or the touch
        /// wheel), so the two modes put their map in one place.
        /// </summary>
        public static CityMinimap Create(Transform hud, int px, float centreYFrac, CityMap map, Transform car)
        {
            var root = new GameObject("CityMap", typeof(RectTransform));
            root.transform.SetParent(hud, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, centreYFrac);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(4f, 0f);
            rt.sizeDelta = new Vector2(px, px);
            var frame = root.AddComponent<Image>();
            frame.color = new Color(0.97f, 0.65f, 0.14f, 0.8f);
            frame.raycastTarget = false;

            var ground = new GameObject("Ground", typeof(RectTransform));
            ground.transform.SetParent(root.transform, false);
            var grt = (RectTransform)ground.transform;
            grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(1f, 1f); grt.offsetMax = new Vector2(-1f, -1f);
            var bg = ground.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.66f);
            bg.raycastTarget = false;
            ground.AddComponent<RectMask2D>();

            var roads = new GameObject("Streets", typeof(RectTransform));
            roads.transform.SetParent(ground.transform, false);
            var rrt = (RectTransform)roads.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
            rrt.pivot = new Vector2(0.5f, 0.5f);
            var mm = roads.AddComponent<CityMinimap>();
            mm.map = map;
            mm.car = car;
            mm.raycastTarget = false;
            mm.Root = root;
            HudOnTop.Apply(root);
            return mm;
        }

        void Update()
        {
            if (map == null || car == null) return;
            if (Time.unscaledTime < nextRefresh) return;
            float yaw = car.eulerAngles.y;
            if (!float.IsNaN(lastPos.x) &&
                (car.position - lastPos).sqrMagnitude < MoveM * MoveM &&
                Mathf.Abs(Mathf.DeltaAngle(yaw, lastYaw)) < TurnDeg) return;
            nextRefresh = Time.unscaledTime + RefreshS;
            lastPos = car.position;
            lastYaw = yaw;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (map == null || car == null) return;
            var rect = rectTransform.rect;
            float half = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (half < 8f) return;
            float scale = half / radiusM;
            var c = new Vector2(car.position.x, car.position.z);
            float yaw = car.eulerAngles.y * Mathf.Deg2Rad;
            // rotate the world by the car's heading so its forward is up the page
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            Vector2 Local(Vector2 w)
            {
                float dx = w.x - c.x, dz = w.y - c.y;
                return new Vector2((dx * cs - dz * sn) * scale, (dx * sn + dz * cs) * scale);
            }
            // line widths follow the map's size: these are pixels on a 240 px map
            float px = half / 120f;
            float clip = half + 6f * px;
            bool Outside(Vector2 a, Vector2 b) =>
                (a.x < -clip && b.x < -clip) || (a.x > clip && b.x > clip) ||
                (a.y < -clip && b.y < -clip) || (a.y > clip && b.y > clip);

            float reach = radiusM * 1.45f;   // the rotated square's corners
            var lo = c - Vector2.one * reach;
            var hi = c + Vector2.one * reach;
            int drawn = 0;

            // water first, under everything
            wsegs.Clear();
            map.WaterSegsInRect(lo, hi, wsegs);
            foreach (var packed in wsegs)
            {
                int wi = packed >> 12, si = packed & 0xFFF;
                var w = map.waters[wi];
                if (si + 1 >= w.pts.Length) continue;
                var a = Local(w.pts[si]); var b = Local(w.pts[si + 1]);
                if (Outside(a, b)) continue;
                float wd = w.lake ? 1.5f * px : Mathf.Clamp(w.width * scale, 1.5f * px, 8f * px);
                Line(vh, a, b, wd, ColWater);
                if (++drawn >= MaxSegs) break;
            }

            // streets, least important first so a freeway is drawn OVER the
            // service road beside it
            segs.Clear();
            map.EdgeSegsInRect(lo, hi, segs);
            for (int pass = 0; pass < 4 && drawn < MaxSegs; pass++)
            {
                foreach (var packed in segs)
                {
                    int ei = packed >> 12, si = packed & 0xFFF;
                    var e = map.edges[ei];
                    int rank = e.link ? 3 : e.cls >= 5 ? 3 : e.cls >= 3 ? 2 : e.cls >= 1 ? 1 : 0;
                    if (rank != pass) continue;
                    if (si + 1 >= e.pts.Length) continue;
                    var a = Local(e.pts[si]); var b = Local(e.pts[si + 1]);
                    if (Outside(a, b)) continue;
                    Color32 col; float wd;
                    if (e.link) { col = ColRamp; wd = 2.0f * px; }
                    else if (e.cls >= 5) { col = ColFwy; wd = 4.0f * px; }
                    else if (e.cls >= 3) { col = ColMajor; wd = 3.0f * px; }
                    else if (e.cls >= 1) { col = ColMinor; wd = 2.0f * px; }
                    else { col = ColLocal; wd = 1.5f * px; }
                    Line(vh, a, b, wd, col);
                    if (++drawn >= MaxSegs) break;
                }
            }

            // north, on the rim
            var north = new Vector2(-sn, cs);
            var nAt = north * (half - 6f * px);
            var nSide = new Vector2(north.y, -north.x);
            Tri(vh, nAt + north * 4f * px, nAt - north * 3f * px + nSide * 3.5f * px,
                nAt - north * 3f * px - nSide * 3.5f * px, ColNorth);

            // the car: a nose-up arrow with a white edge, always at the centre
            Tri(vh, new Vector2(0f, 9f * px), new Vector2(-7f * px, -7f * px), new Vector2(7f * px, -7f * px), ColPlayerEdge);
            Tri(vh, new Vector2(0f, 6.5f * px), new Vector2(-4.5f * px, -5f * px), new Vector2(4.5f * px, -5f * px), ColPlayer);
        }

        static readonly UIVertex[] quad = new UIVertex[4];

        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 col)
        {
            var d = b - a;
            float m = d.magnitude;
            if (m < 1e-3f) return;
            d /= m;
            var n = new Vector2(-d.y, d.x) * (width * 0.5f);
            // a hair of overlap along the line hides the seam at every vertex
            a -= d * (width * 0.25f); b += d * (width * 0.25f);
            Set(0, a - n, col); Set(1, a + n, col); Set(2, b + n, col); Set(3, b - n, col);
            vh.AddUIVertexQuad(quad);
        }

        static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = a; vh.AddVert(v);
            v.position = b; vh.AddVert(v);
            v.position = c; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        static void Set(int i, Vector2 p, Color32 col)
        {
            var v = UIVertex.simpleVert;
            v.position = p;
            v.color = col;
            quad[i] = v;
        }
    }
}
