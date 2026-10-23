using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PSXRacing.LifeSim;

namespace PSXRacing
{
    /// <summary>
    /// Where a race starts, which way it runs and where it ends, read off the
    /// same centreline the picker's thumbnail and the race HUD's map draw. One
    /// answer for the menu's overhead map (<see cref="RouteMap"/>) and the
    /// start lines standing in Charlotte's free roam (City.CityRaceStarts), so
    /// the white dot on the map and the gate on the road are one place.
    /// </summary>
    public static class RouteGeo
    {
        public struct Ends
        {
            /// <summary>Index of the start line in the sampled list, -1 for
            /// a deck (a footprint, not a line).</summary>
            public int start;
            /// <summary>Index of the finish, -1 on a loop (the start is the
            /// finish).</summary>
            public int finish;
            /// <summary>+1 when the race runs up the list, -1 when it runs
            /// down it (a reverse twin borrows its forward twin's points).</summary>
            public int dir;
            public bool loop;
        }

        /// <summary>
        /// The ends of a venue on <paramref name="pts"/>
        /// (<c>TrackCatalog.Sample(def, Spacing)</c>). The same rules as the
        /// thumbnail's markers, except that a route with ends starts on its
        /// LINE rather than at the far end of the lead-in: a 60 m lead-in is
        /// a pixel on a map and a whole block on the street.
        /// </summary>
        public static Ends EndsOf(TrackCatalog.TrackDef def, List<Vector3> pts)
        {
            var e = new Ends { start = 0, finish = -1, dir = def.Reversed ? -1 : 1 };
            int n = pts.Count;
            if (n < 2) return e;
            if (def.IsDeckRun) { e.start = -1; e.loop = true; e.dir = 1; return e; }
            float sp = TrackCatalog.Spacing;
            if (def.IsSprintVariant)
            {
                e.start = Mathf.Clamp(Mathf.RoundToInt(def.sprintStartM / sp), 0, n - 1);
                e.finish = Mathf.Clamp(Mathf.RoundToInt(def.sprintFinishM / sp), 0, n - 1);
                e.dir = e.finish >= e.start ? 1 : -1;
                return e;
            }
            bool ends = def.drag || (def.stage && !def.loop) || (def.IsCityRace && !def.loop);
            if (!ends) { e.loop = true; return e; }
            int line = (def.stage || def.IsCityRace)
                ? Mathf.Clamp(Mathf.RoundToInt(def.stageStartLineM / sp), 0, n - 1) : 0;
            int fin = def.FinishIndex > 0 && def.FinishIndex < n ? def.FinishIndex : n - 1;
            if (def.Reversed) { e.start = fin; e.finish = line; e.dir = -1; }
            else { e.start = line; e.finish = fin; e.dir = 1; }
            return e;
        }

        /// <summary>
        /// The square of the world a map of this venue shows: centred on the
        /// route's bounding box, a little wider than its longer side (so the
        /// start dot is never on the frame) and never under 420 m (a parking
        /// deck is a 90 m rectangle; what a player wants to see is the blocks
        /// round it). The street bake (Editor/RouteMapBake) frames the same
        /// square, which is what lets the route be drawn over it.
        /// </summary>
        public static void Frame(TrackCatalog.TrackDef def, List<Vector3> pts, out Vector2 centre, out float spanM)
        {
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in pts)
            {
                lo = Vector2.Min(lo, new Vector2(p.x, p.z));
                hi = Vector2.Max(hi, new Vector2(p.x, p.z));
            }
            centre = (lo + hi) * 0.5f;
            float side = Mathf.Max(hi.x - lo.x, hi.y - lo.y);
            spanM = Mathf.Max(side * 1.14f + 60f, 420f);
        }

        /// <summary>The key a venue's baked street picture is stored under
        /// (Resources/RouteMaps/key), or null for a venue with no streets
        /// round it (every MAIN venue: a stage has no city graph).</summary>
        public static string StreetKey(TrackCatalog.TrackDef def)
        {
            if (def == null || !def.IsCityRace) return null;
            if (def.IsDeckRun) return "deck_" + def.deckWay;
            return def.cityRoute;
        }

        /// <summary>
        /// The start line in plan (x, z metres) and the way the race leaves
        /// it. False for a deck run (its line is inside the deck) and for a
        /// venue this edition does not carry.
        /// </summary>
        public static bool StartPose(TrackCatalog.TrackDef def, out Vector2 plan, out Vector2 heading)
        {
            plan = Vector2.zero; heading = Vector2.up;
            if (def == null || def.IsDeckRun || !Edition.Ships(def)) return false;
            var pts = TrackCatalog.Sample(def, TrackCatalog.Spacing);
            if (pts.Count < 4) return false;
            var e = EndsOf(def, pts);
            if (e.start < 0) return false;
            int n = pts.Count;
            int ahead = e.start + e.dir * 4;
            ahead = e.loop ? ((ahead % n) + n) % n : Mathf.Clamp(ahead, 0, n - 1);
            var a = pts[e.start];
            var b = pts[ahead];
            plan = new Vector2(a.x, a.z);
            var d = new Vector2(b.x - a.x, b.z - a.z);
            if (d.sqrMagnitude < 1e-4f) return false;
            heading = d.normalized;
            return true;
        }

        /// <summary>What kind of race this is, the way the card says it.</summary>
        public static string KindOf(TrackCatalog.TrackDef def)
        {
            if (def.IsDeckRun) return "DECK RUN";
            if (def.drag) return "DRAG";
            if (def.IsSprintVariant) return "SPRINT";
            if (def.loop) return "CIRCUIT RACE";
            return def.stage ? "STAGE" : "SPRINT";
        }
    }

    /// <summary>
    /// THE VIEW FROM ABOVE (owner, 2026-10-19: "When selecting races, I want it
    /// to show a view from above"). A square map of one venue for the menus:
    /// a dark ground under a faint grid at the scale bar's step, the streets
    /// round a city race (a picture baked once per route by
    /// Editor/RouteMapBake, because the menu must never parse the city), and
    /// over them the route itself as vector geometry - amber with a dark
    /// halo, arrows along it the way it is driven, a white start and a red
    /// finish - so it stays sharp at any picture height.
    /// </summary>
    public static class RouteMap
    {
        /// <summary>
        /// Build the map at <paramref name="topLeft"/> (anchored at
        /// <paramref name="anchor"/>), <paramref name="size"/> units square.
        /// Returns its frame. Never measures a rect (the stale-rect rule):
        /// everything inside is anchored to the frame's edges.
        /// </summary>
        public static RectTransform Build(Transform parent, TrackCatalog.TrackDef def, Vector2 anchor,
                                          Vector2 topLeft, float size)
        {
            var frame = MenuKit.Rect(parent, "RouteMap", anchor, new Vector2(0f, 1f), topLeft,
                new Vector2(size, size), new Color(MenuKit.Accent.r, MenuKit.Accent.g, MenuKit.Accent.b, 0.85f));
            // Opaque: the amber frame behind it would tint a translucent
            // ground brown.
            var ground = MenuKit.Stretch(frame, "Ground", Vector2.zero, Vector2.one, 2f, 2f, 2f, -2f,
                new Color(0.050f, 0.056f, 0.072f, 1f));
            ground.gameObject.AddComponent<RectMask2D>();
            if (def == null || !Edition.Ships(def)) return frame;

            var pts = TrackCatalog.Sample(def, TrackCatalog.Spacing);
            RouteGeo.Frame(def, pts, out var centre, out float span);

            // The streets: one baked picture per route, in this frame.
            string key = RouteGeo.StreetKey(def);
            var streets = key != null ? Resources.Load<Texture2D>("RouteMaps/" + key) : null;
            if (streets != null)
            {
                var go = new GameObject("Streets", typeof(RectTransform));
                go.transform.SetParent(ground, false);
                var img = go.AddComponent<RawImage>();
                img.texture = streets;
                img.color = StreetTint;
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }

            var lineGo = new GameObject("Route", typeof(RectTransform), typeof(CanvasRenderer));
            lineGo.transform.SetParent(ground, false);
            var lrt = (RectTransform)lineGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var g = lineGo.AddComponent<RouteMapGraphic>();
            g.raycastTarget = false;
            g.Set(def, pts, centre, span, size - 4f);

            // North, and how far a step of the grid is. Labels at the type
            // floor, inside the frame's corners.
            float step = RouteMapGraphic.GridStep(span);
            MenuKit.Label(ground, "N", MenuKit.MinLabelSize, new Vector2(1f, 1f), new Vector2(-10f, -6f),
                TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.9f), 30f, height: 26f, bold: true)
                .rectTransform.pivot = new Vector2(1f, 1f);
            var scale = MenuKit.Label(ground, "GRID " + (step >= 1000f ? (step / 1000f).ToString("0.#") + " KM"
                                                                          : step.ToString("0") + " M"),
                MenuKit.MinLabelSize, new Vector2(0f, 0f), new Vector2(10f, 6f), TextAnchor.MiddleLeft,
                new Color(0.80f, 0.82f, 0.88f, 0.95f), size - 20f, height: 26f);
            scale.rectTransform.pivot = new Vector2(0f, 0f);
            return frame;
        }

        /// <summary>The baked streets are white with the class in the alpha;
        /// this is the grey they are shown in.</summary>
        static readonly Color StreetTint = new Color(0.62f, 0.64f, 0.70f, 1f);

        /// <summary>The line under a map, saying what the marks are.</summary>
        public static string LegendFor(TrackCatalog.TrackDef def)
        {
            if (def.IsDeckRun) return "AMBER: THE DECK";
            var e = RouteGeo.EndsOf(def, TrackCatalog.Sample(def, TrackCatalog.Spacing));
            return e.loop ? "START / FINISH: WHITE" : "START: WHITE  ·  FINISH: RED";
        }
    }

    /// <summary>
    /// The route, drawn as UI geometry inside its map's square: grid, halo,
    /// line, direction arrows, start and finish. Built once per page (the
    /// menu rebuilds on every press; the mesh is a few thousand quads).
    /// ExecuteAlways so the menu preview, which never enters play mode, sees
    /// it as the game does.
    /// </summary>
    [ExecuteAlways]
    public class RouteMapGraphic : MaskableGraphic
    {
        TrackCatalog.TrackDef def;
        List<Vector3> pts;
        Vector2 centre;
        float span = 1f, sizeUnits = 100f;

        static readonly Color32 Halo = new Color32(0, 0, 0, 230);
        static readonly Color32 Line = new Color32(247, 166, 35, 255);       // MenuKit.Accent
        static readonly Color32 Unraced = new Color32(247, 166, 35, 90);
        static readonly Color32 Arrow = new Color32(255, 246, 220, 255);
        static readonly Color32 StartCol = new Color32(255, 255, 255, 255);
        static readonly Color32 FinishCol = new Color32(255, 90, 70, 255);
        static readonly Color32 Grid = new Color32(120, 140, 170, 34);
        static readonly Color32 DeckFill = new Color32(247, 166, 35, 70);

        public void Set(TrackCatalog.TrackDef d, List<Vector3> p, Vector2 c, float s, float size)
        {
            def = d; pts = p; centre = c; span = Mathf.Max(1f, s); sizeUnits = size;
            SetVerticesDirty();
        }

        /// <summary>A round grid step: about a sixth of the map.</summary>
        public static float GridStep(float span)
        {
            float want = span / 6f;
            foreach (float s in new[] { 50f, 100f, 200f, 250f, 500f, 1000f, 2000f, 2500f, 5000f })
                if (s >= want) return s;
            return 10000f;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (def == null || pts == null || pts.Count < 2) return;
            // Sized off what Set was told, not off rect (the stale-rect rule):
            // the graphic is stretched over a frame of exactly that size.
            float w = sizeUnits;
            float k = w / 400f;                 // line widths are written for a 400-unit map
            float half = w * 0.5f;
            Vector2 L(Vector3 p) => new Vector2((p.x - centre.x) / span * w, (p.z - centre.y) / span * w);

            // The grid, at round metres, through the world's own origin so it
            // does not slide when the frame does.
            float step = GridStep(span);
            float x0 = Mathf.Ceil((centre.x - span * 0.5f) / step) * step;
            for (float gx = x0; gx < centre.x + span * 0.5f; gx += step)
            {
                float lx = (gx - centre.x) / span * w;
                Quad(vh, new Vector2(lx, -half), new Vector2(lx, half), 1f * k, Grid);
            }
            float z0 = Mathf.Ceil((centre.y - span * 0.5f) / step) * step;
            for (float gz = z0; gz < centre.y + span * 0.5f; gz += step)
            {
                float lz = (gz - centre.y) / span * w;
                Quad(vh, new Vector2(-half, lz), new Vector2(half, lz), 1f * k, Grid);
            }

            var e = RouteGeo.EndsOf(def, pts);
            int n = pts.Count;

            // A deck: its footprint, filled, outlined. No line to follow.
            if (def.IsDeckRun)
            {
                var poly = new List<Vector2>();
                foreach (var p in pts) poly.Add(L(p));
                var mid = Vector2.zero;
                foreach (var p in poly) mid += p;
                mid /= poly.Count;
                for (int i = 0; i + 1 < poly.Count; i++) Tri(vh, mid, poly[i], poly[i + 1], DeckFill);
                Polyline(vh, poly, 7f * k, Halo, closed: true);
                Polyline(vh, poly, 3f * k, Line, closed: true);
                return;
            }

            // The raced stretch, in the order it is driven.
            var raced = new List<Vector2>();
            var rest = new List<Vector2>();
            if (e.loop)
            {
                for (int i = 0; i <= n; i++)
                {
                    int idx = ((e.start + e.dir * i) % n + n) % n;
                    raced.Add(L(pts[idx]));
                }
            }
            else
            {
                for (int i = e.start; ; i += e.dir)
                {
                    raced.Add(L(pts[i]));
                    if (i == e.finish || i + e.dir < 0 || i + e.dir >= n) break;
                }
                // A sprint cut from a loop: the rest of the loop, faint, so
                // the cut reads as part of a circuit the player knows.
                if (def.IsSprintVariant)
                    foreach (var p in pts) rest.Add(L(p));
            }
            raced = Thin(raced, 1.2f * k);
            if (rest.Count > 1) { rest = Thin(rest, 1.2f * k); Polyline(vh, rest, 2f * k, Unraced, closed: true); }

            Polyline(vh, raced, 8f * k, Halo, closed: false);
            Polyline(vh, raced, 3.6f * k, Line, closed: false);

            // Arrows the way it is driven, evenly along the drawn length,
            // clear of the start and the finish marks.
            float total = 0f;
            for (int i = 1; i < raced.Count; i++) total += Vector2.Distance(raced[i - 1], raced[i]);
            float gap = Mathf.Max(46f * k, total / 14f);
            float nextAt = gap * 0.75f, run = 0f;
            for (int i = 1; i < raced.Count; i++)
            {
                Vector2 a = raced[i - 1], b = raced[i];
                float seg = Vector2.Distance(a, b);
                while (seg > 1e-4f && run + seg >= nextAt)
                {
                    float t = (nextAt - run) / seg;
                    if (nextAt < total - gap * 0.5f)
                        Chevron(vh, Vector2.Lerp(a, b, t), (b - a) / seg, 6.5f * k);
                    nextAt += gap;
                }
                run += seg;
            }

            // The ends: white start (red-ringed on a loop, where it is the
            // finish too), red finish.
            var s0 = raced[0];
            if (e.loop) { Disc(vh, s0, 9.5f * k, Halo); Disc(vh, s0, 8f * k, FinishCol); Disc(vh, s0, 5f * k, StartCol); }
            else
            {
                var f0 = raced[raced.Count - 1];
                Disc(vh, f0, 9f * k, Halo); Disc(vh, f0, 7f * k, FinishCol);
                Disc(vh, s0, 9f * k, Halo); Disc(vh, s0, 7f * k, StartCol);
            }
        }

        /// <summary>Drop points closer than <paramref name="minGap"/> to the
        /// last kept one: a 7 km stage at 4 m is 1,750 points on a 400-unit
        /// map, most of them on top of each other.</summary>
        static List<Vector2> Thin(List<Vector2> src, float minGap)
        {
            if (src.Count < 3) return src;
            var o = new List<Vector2>(src.Count / 2) { src[0] };
            for (int i = 1; i < src.Count - 1; i++)
                if ((src[i] - o[o.Count - 1]).sqrMagnitude >= minGap * minGap) o.Add(src[i]);
            o.Add(src[src.Count - 1]);
            return o;
        }

        static void Polyline(VertexHelper vh, List<Vector2> p, float width, Color32 col, bool closed)
        {
            int segs = closed ? p.Count : p.Count - 1;
            for (int i = 0; i < segs; i++) Quad(vh, p[i], p[(i + 1) % p.Count], width, col, cap: true);
        }

        /// <summary>One segment as a quad; <paramref name="cap"/> pushes both
        /// ends out by half the width so consecutive segments overlap at the
        /// joints instead of notching on a bend.</summary>
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 col, bool cap = false)
        {
            var d = b - a;
            float m = d.magnitude;
            if (m < 1e-4f) return;
            d /= m;
            var nrm = new Vector2(-d.y, d.x) * (width * 0.5f);
            if (cap) { a -= d * (width * 0.5f); b += d * (width * 0.5f); }
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = a - nrm; vh.AddVert(v);
            v.position = a + nrm; vh.AddVert(v);
            v.position = b + nrm; vh.AddVert(v);
            v.position = b - nrm; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
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

        static void Disc(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            const int Sides = 16;
            int i0 = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = c; vh.AddVert(v);
            for (int s = 0; s <= Sides; s++)
            {
                float a = s * Mathf.PI * 2f / Sides;
                v.position = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                vh.AddVert(v);
            }
            for (int s = 0; s < Sides; s++) vh.AddTriangle(i0, i0 + 1 + s, i0 + 2 + s);
        }

        /// <summary>An arrowhead pointing along <paramref name="dir"/>, on a
        /// dark outline so it reads on the amber line.</summary>
        static void Chevron(VertexHelper vh, Vector2 at, Vector2 dir, float size)
        {
            var side = new Vector2(-dir.y, dir.x);
            float o = size * 0.35f;
            Tri(vh, at + dir * (size + o), at - dir * (size * 0.7f + o) + side * (size + o),
                at - dir * (size * 0.7f + o) - side * (size + o), Halo);
            Tri(vh, at + dir * size, at - dir * size * 0.7f + side * size,
                at - dir * size * 0.7f - side * size, Arrow);
        }
    }
}
