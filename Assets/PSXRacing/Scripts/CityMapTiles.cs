using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSXRacing
{
    /// <summary>
    /// THE WHOLE-CITY MAP UNDER A CHARLOTTE ROUTE (owner, 2026-11-16: "not a fan of each track getting
    /// its own saved images. It should be a picture of the entire city, the track drawn over it, then
    /// zoomed in to fit the track on screen for track preview before race").
    ///
    /// One map of all of Charlotte, drawn ONCE from the city's own data by tools/city/citymap_tiles.py:
    /// roads by class at their painted width, water, the tree canopy, commercial and industrial land,
    /// buildings, parking lots and rail, in the race card's own dark style. It ships as a pyramid of
    /// 512 px tiles under StreamingAssets/CityMap/(level)/(tx)_(ty).png, listed in
    /// Resources/charlotte_citymap.txt. Levels 0-4 cover the whole city (level 4 = 6.5 m a pixel); levels
    /// 5-8 (down to 0.4 m a pixel) are drawn only round the venues, which keeps a 400 m parking deck and
    /// a 1 km sprint sharp without shipping every woodland tile at 0.4 m.
    ///
    /// Why tiles rather than drawing the graph at runtime: the menu must never parse the city (8 MB and a
    /// second's work on a phone), and the map has 120,000 buildings, the canopy and the rail in it,
    /// which are cheap to draw once offline and expensive to mesh in a UI. The route itself stays
    /// vector (RouteMapGraphic) over it, sharp at any size.
    ///
    /// Every city venue (city routes and the deck runs) draws its card on this map; stages keep their
    /// photographs. PSX_TRACKMAP_CITY=0 brings back each city venue's own saved picture.
    /// </summary>
    public static class CityMapTiles
    {
        public const string IndexResource = "charlotte_citymap";   // charlotte_*: parked out of MAIN by EditionParking's rule
        public const string Folder = "CityMap";
        /// <summary>The tiles are a shade under full brightness, so the amber route over them is the
        /// brightest thing on the card.</summary>
        static readonly Color Tint = new Color(0.92f, 0.92f, 0.92f, 1f);

        static bool loaded;
        static float citySpan = 1f, cityX, cityZ;
        static int tilePx = 512;
        static readonly List<HashSet<int>> have = new List<HashSet<int>>();
        static int fullLevel = -1;

        /// <summary>On unless PSX_TRACKMAP_CITY=0, and only when the index shipped.</summary>
        public static bool Enabled =>
            System.Environment.GetEnvironmentVariable("PSX_TRACKMAP_CITY") != "0" && Load();

        static bool Load()
        {
            if (loaded) return fullLevel >= 0;
            loaded = true;
            var txt = Resources.Load<TextAsset>(IndexResource);
            if (txt == null) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var nf = System.Globalization.NumberStyles.Float;
            foreach (var raw in txt.text.Split('\n'))
            {
                var f = raw.Trim().Split(' ');
                if (f.Length >= 6 && f[0] == "CITYMAP")
                {
                    float.TryParse(f[2], nf, ci, out citySpan);
                    float.TryParse(f[3], nf, ci, out cityX);
                    float.TryParse(f[4], nf, ci, out cityZ);
                    int.TryParse(f[5], out tilePx);
                }
                else if (f.Length >= 2 && f[0] == "L" && int.TryParse(f[1], out int lv) && lv >= 0 && lv < 16)
                {
                    while (have.Count <= lv) have.Add(new HashSet<int>());
                    for (int i = 2; i < f.Length; i++)
                    {
                        int u = f[i].IndexOf('_');
                        if (u > 0 && int.TryParse(f[i].Substring(0, u), out int tx) && int.TryParse(f[i].Substring(u + 1), out int ty))
                            have[lv].Add(Key(tx, ty));
                    }
                }
            }
            // the deepest level with the whole city in it: the floor every card stands on
            for (int lv = 0; lv < have.Count; lv++)
                if (have[lv].Count >= (1 << lv) * (1 << lv)) fullLevel = lv;
            if (citySpan <= 1f || fullLevel < 0) { fullLevel = -1; return false; }
            return true;
        }

        static int Key(int tx, int ty) => (tx << 16) | ty;
        static bool Has(int lv, int tx, int ty) => lv < have.Count && have[lv].Contains(Key(tx, ty));

        /// <summary>The level whose pixels are closest to the card's own: a tile drawn between about
        /// 0.6x and 1.2x its size (sharp, and never so far shrunk that its lines shimmer).</summary>
        public static int LevelFor(float span, float sizePx)
        {
            float mpp = span / Mathf.Max(32f, sizePx);
            float lv = Mathf.Log(citySpan / tilePx / Mathf.Max(0.01f, mpp), 2f);
            return Mathf.Clamp(Mathf.FloorToInt(lv + 0.75f), 0, Mathf.Max(0, have.Count - 1));
        }

        /// <summary>
        /// The city under a card: tiles stretched over <paramref name="content"/>, whose square shows
        /// the world square (<paramref name="centre"/>, <paramref name="span"/> metres) - the frame
        /// RouteGeo.Frame gives the route. The whole-city level first (always there), then the level
        /// as sharp as the card is big wherever it was drawn. Anchors only (the stale-rect rule).
        /// </summary>
        public static void Place(RectTransform content, Vector2 centre, float span, float sizePx)
        {
            if (!Enabled || content == null) return;
            int want = LevelFor(span, sizePx);
            int floor = Mathf.Min(want, fullLevel);
            LastPlaced = PlaceLevel(content, floor, centre, span);
            if (want > floor) LastPlaced += PlaceLevel(content, want, centre, span);
            LastLevel = want;
            LastMetresPerPixel = citySpan / (1 << want) / tilePx;
            LastWantedMetresPerPixel = span / Mathf.Max(32f, sizePx);
        }

        /// <summary>What the last card was given (the preview tools report it).</summary>
        public static int LastLevel, LastPlaced;
        public static float LastMetresPerPixel, LastWantedMetresPerPixel;

        /// <summary>The tiles of one level that touch the frame, each a RawImage anchored to its own
        /// square of the frame (a hair over, so two tiles never show a seam between them).</summary>
        public static int PlaceLevel(RectTransform content, int level, Vector2 centre, float span)
            => PlaceLevel(content, level, centre, span, centre, span);

        /// <summary>The middle of the city square and its side (metres): the view a card opens on.</summary>
        public static Vector2 CityCentre { get { Load(); return new Vector2(cityX, cityZ); } }
        public static float CitySpan { get { Load(); return citySpan; } }

        /// <summary>The tiles of one level that touch the square (<paramref name="coverCentre"/>,
        /// <paramref name="coverSpan"/>), anchored in the frame (<paramref name="centre"/>,
        /// <paramref name="span"/>): a card that opens on the whole city carries the city's tiles far
        /// outside its own frame, clipped by the card until the zoom brings them in.</summary>
        public static int PlaceLevel(RectTransform content, int level, Vector2 centre, float span,
                                     Vector2 coverCentre, float coverSpan)
        {
            if (!Enabled || content == null) return 0;
            level = Mathf.Clamp(level, 0, have.Count - 1);
            int n = 1 << level;
            float t = citySpan / n;
            float x0 = cityX - citySpan * 0.5f, z1 = cityZ + citySpan * 0.5f;
            float fx0 = centre.x - span * 0.5f, fz0 = centre.y - span * 0.5f;
            float cx0 = coverCentre.x - coverSpan * 0.5f, cz0 = coverCentre.y - coverSpan * 0.5f;
            int tx0 = Mathf.Max(0, Mathf.FloorToInt((cx0 - x0) / t));
            int tx1 = Mathf.Min(n - 1, Mathf.FloorToInt((cx0 + coverSpan - x0) / t));
            int ty0 = Mathf.Max(0, Mathf.FloorToInt((z1 - (cz0 + coverSpan)) / t));
            int ty1 = Mathf.Min(n - 1, Mathf.FloorToInt((z1 - cz0) / t));
            int placed = 0;
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    if (!Has(level, tx, ty)) continue;
                    float ax0 = x0 + tx * t, az1 = z1 - ty * t;
                    var go = new GameObject("CityMap " + level + "/" + tx + "_" + ty, typeof(RectTransform));
                    go.transform.SetParent(content, false);
                    var img = go.AddComponent<RawImage>();
                    img.raycastTarget = false;
                    img.color = Tint;
                    var rt = img.rectTransform;
                    rt.anchorMin = new Vector2((ax0 - fx0) / span, (az1 - t - fz0) / span);
                    rt.anchorMax = new Vector2((ax0 + t - fx0) / span, (az1 - fz0) / span);
                    rt.offsetMin = new Vector2(-0.35f, -0.35f);
                    rt.offsetMax = new Vector2(0.35f, 0.35f);
                    var tex = Tile(level, tx, ty, img);
                    img.texture = tex;
                    img.enabled = tex != null;
                    placed++;
                }
            return placed;
        }

        // ---- the tiles themselves: read once, kept while they are used ----
        const int CacheMax = 48;
        static readonly Dictionary<long, Texture2D> cache = new Dictionary<long, Texture2D>();
        static readonly LinkedList<long> lru = new LinkedList<long>();
        static readonly Dictionary<long, LinkedListNode<long>> lruAt = new Dictionary<long, LinkedListNode<long>>();
        static readonly HashSet<long> fetching = new HashSet<long>();
        static readonly List<(long key, RawImage img)> waiting = new List<(long, RawImage)>();

        static long CacheKey(int level, int tx, int ty) => ((long)level << 40) | ((long)tx << 20) | (long)ty;

        static void Touch(long key)
        {
            if (lruAt.TryGetValue(key, out var node)) lru.Remove(node);
            lruAt[key] = lru.AddFirst(key);
        }

        static void Keep(long key, Texture2D tex)
        {
            cache[key] = tex;
            Touch(key);
            while (cache.Count > CacheMax && lru.Last != null)
            {
                long old = lru.Last.Value;
                lru.RemoveLast();
                lruAt.Remove(old);
                if (cache.TryGetValue(old, out var t) && t != null)
                {
                    if (Application.isPlaying) Object.Destroy(t); else Object.DestroyImmediate(t);
                }
                cache.Remove(old);
            }
        }

        static string PathOf(int level, int tx, int ty) =>
            Application.streamingAssetsPath + "/" + Folder + "/" + level + "/" + tx + "_" + ty + ".png";

        /// <summary>The tile's picture: at once from disk (the PC player, the editor), or null while a
        /// phone or a browser fetches it (the image fills in when it lands, if its card is still up).</summary>
        static Texture2D Tile(int level, int tx, int ty, RawImage img)
        {
            long key = CacheKey(level, tx, ty);
            if (cache.TryGetValue(key, out var have) && have != null) { Touch(key); return have; }
            string path = PathOf(level, tx, ty);
            if (!path.Contains("://"))
            {
                if (!System.IO.File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGB24, true)   // mipmapped: a card a third the size of a tile still reads cleanly
                { name = "CityMap " + level + "/" + tx + "_" + ty, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!t.LoadImage(System.IO.File.ReadAllBytes(path), true))
                {
                    if (Application.isPlaying) Object.Destroy(t); else Object.DestroyImmediate(t);
                    return null;
                }
                Keep(key, t);
                return t;
            }
            waiting.Add((key, img));
            if (fetching.Add(key))
            {
                var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(path, true);
                req.SendWebRequest().completed += _ =>
                {
                    Texture2D got = null;
                    try
                    {
                        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                            got = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
                        else Debug.LogWarning("[CityMap] no tile " + path + ": " + req.error);
                    }
                    finally { req.Dispose(); fetching.Remove(key); }
                    if (got == null) return;
                    got.wrapMode = TextureWrapMode.Clamp;
                    Keep(key, got);
                    for (int i = waiting.Count - 1; i >= 0; i--)
                    {
                        if (waiting[i].key != key) continue;
                        var w = waiting[i].img;
                        waiting.RemoveAt(i);
                        if (w == null) continue;   // its card has gone
                        w.texture = got;
                        w.enabled = true;
                    }
                };
            }
            return null;
        }
    }
}
