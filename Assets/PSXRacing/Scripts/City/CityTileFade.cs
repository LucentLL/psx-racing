using System.Collections.Generic;
using UnityEngine;

namespace PSXRacing.City
{
    /// <summary>
    /// CITY POP-IN (city-popin, 2026-10-28; owner, with NFS 2015 frames as the
    /// bar: "Buildings pop into existence slowly as you approach"). One small
    /// table every city draw reads in PSX/Lit (<c>_TileFade</c>, the
    /// <c>_PSXTileFadeTex</c> global): a texel per 256 m tile, 32 x 32 tiles
    /// wrapped (8 km - the live ring and the far buildings round it never span
    /// half of it), four shares:
    ///   r  a LIVE tile's buildings in (its far-building proxies out): 0 while
    ///      the tile is not built, up to 1 over <see cref="FadeSeconds"/> once
    ///      it stands up;
    ///   g  its other objects in (ground, roads, props, lamps): 1 while it is
    ///      not built (a stray object reads it whole), 0 to 1 as it stands up;
    ///   b  its tree frame in (trees, signs, poles, signals: planted a frame or
    ///      more after it stood up);
    ///   a  its far-building proxies in (CityProxies): 0 where no proxy layer
    ///      draws it, up to 1 as one enters range.
    /// The shader's dither is one pattern for both sides of each hand-over, so
    /// the proxy and the tile's own copy of a building share its pixels and
    /// never fight in depth while they swap. Edit mode (the tools' shots) never
    /// fades: every share is set whole at once.
    /// </summary>
    public static class CityTileFade
    {
        public const int N = 32;
        /// <summary>A tile, a tree frame or a proxy chunk fades in over this.</summary>
        public const float FadeSeconds = 0.5f;
        /// <summary>PSX_CITY_FADE=0 in a tool's environment: every share is set
        /// whole at once in play too (the A/B: the old pop).</summary>
        public static bool On = System.Environment.GetEnvironmentVariable("PSX_CITY_FADE") != "0";

        public const int R = 0, G = 1, B = 2, A = 3;
        /// <summary>The _TileFade modes (PSX/Lit).</summary>
        public const float ModeObject = 1f, ModeProxy = 2f, ModeTreeFrame = 3f, ModeBuildings = 4f, ModeSkyline = 5f;

        public static readonly int TexId = Shader.PropertyToID("_PSXTileFadeTex");
        public static readonly int OnId = Shader.PropertyToID("_PSXTileFadeOn");
        public static readonly int ModeId = Shader.PropertyToID("_TileFade");

        static Texture2D tex;
        static Color32[] px;
        static bool dirty;
        struct Ramp { public int i, ch; public float t0; }
        static readonly List<Ramp> ramps = new List<Ramp>(64);
        /// <summary>Bumped whenever a tile's buildings share (r) reaches 1:
        /// the skyline re-reads which towers its live tiles draw then.</summary>
        public static int BuildingsVersion { get; private set; }

        static int Index(int tx, int tz) => ((tz % N + N) % N) * N + ((tx % N + N) % N);

        /// <summary>The table, made once and handed to every shader.</summary>
        public static void Ensure()
        {
            if (tex == null)
            {
                tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true)
                {
                    name = "CityTileFade", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.HideAndDontSave, anisoLevel = 0,
                };
                px = new Color32[N * N];
                for (int k = 0; k < px.Length; k++) px[k] = new Color32(0, 255, 255, 0);
                ramps.Clear();
                dirty = true;
            }
            Shader.SetGlobalTexture(TexId, tex);
            Flush();
        }

        static bool Fades => On && Application.isPlaying;

        static void Put(int i, int ch, byte v)
        {
            var c = px[i];
            byte old = ch == R ? c.r : ch == G ? c.g : ch == B ? c.b : c.a;
            if (old == v) return;
            if (ch == R) c.r = v; else if (ch == G) c.g = v; else if (ch == B) c.b = v; else c.a = v;
            px[i] = c;
            dirty = true;
        }

        static void Cancel(int i, int ch)
        {
            for (int k = ramps.Count - 1; k >= 0; k--)
                if (ramps[k].i == i && ramps[k].ch == ch) ramps.RemoveAt(k);
        }

        /// <summary>Set a share whole (no fade).</summary>
        public static void Set(int tx, int tz, int ch, float v)
        {
            if (px == null) Ensure();
            int i = Index(tx, tz);
            Cancel(i, ch);
            Put(i, ch, (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f));
            if (ch == R && v >= 1f) BuildingsVersion++;
        }

        /// <summary>Fade a share in from 0 (whole at once in edit mode, or
        /// with PSX_CITY_FADE=0).</summary>
        public static void FadeIn(int tx, int tz, int ch)
        {
            if (px == null) Ensure();
            if (!Fades) { Set(tx, tz, ch, 1f); return; }
            int i = Index(tx, tz);
            Cancel(i, ch);
            Put(i, ch, 0);
            ramps.Add(new Ramp { i = i, ch = ch, t0 = Time.unscaledTime });
        }

        public static float Get(int tx, int tz, int ch)
        {
            if (px == null) return ch == R || ch == A ? 0f : 1f;
            var c = px[Index(tx, tz)];
            return (ch == R ? c.r : ch == G ? c.g : ch == B ? c.b : c.a) / 255f;
        }

        /// <summary>A tile stood up: its buildings and objects fade in
        /// (<paramref name="fade"/> false: whole now - the tile under a car,
        /// a tool's ring).</summary>
        public static void TileUp(int tx, int tz, bool fade)
        {
            if (fade) { FadeIn(tx, tz, R); FadeIn(tx, tz, G); }
            else { Set(tx, tz, R, 1f); Set(tx, tz, G, 1f); }
        }

        /// <summary>Its tree frame was planted.</summary>
        public static void TreesUp(int tx, int tz, bool fade)
        {
            if (fade) FadeIn(tx, tz, B); else Set(tx, tz, B, 1f);
        }

        /// <summary>A tile dropped: its proxies draw it near again.</summary>
        public static void TileDown(int tx, int tz)
        {
            Set(tx, tz, R, 0f); Set(tx, tz, G, 1f); Set(tx, tz, B, 1f);
        }

        /// <summary>Advance the fades and upload what changed (once a frame).</summary>
        public static void Tick()
        {
            if (px == null) return;
            if (ramps.Count > 0)
            {
                float now = Time.unscaledTime;
                for (int k = ramps.Count - 1; k >= 0; k--)
                {
                    var rp = ramps[k];
                    float t = Fades ? (now - rp.t0) / FadeSeconds : 1f;
                    if (t >= 1f)
                    {
                        Put(rp.i, rp.ch, 255);
                        if (rp.ch == R) BuildingsVersion++;
                        ramps.RemoveAt(k);
                    }
                    else Put(rp.i, rp.ch, (byte)Mathf.Clamp(Mathf.RoundToInt(t * 255f), 1, 254));
                }
            }
            Flush();
        }

        static void Flush()
        {
            if (!dirty || tex == null) return;
            dirty = false;
            tex.SetPixels32(px);
            tex.Apply(false, false);
        }

        /// <summary>The proxies draw (1) or not (0: fog, off): a tile's own
        /// buildings then run out to the far plane wherever they are.</summary>
        public static void SetProxiesOn(bool on) => Shader.SetGlobalVector(OnId, new Vector4(on ? 1f : 0f, 0f, 0f, 0f));

        // ---- the per-renderer switch ----
        static MaterialPropertyBlock mpb;
        /// <summary>Put <paramref name="r"/> in a fade mode, keeping whatever
        /// else its property block holds.</summary>
        public static void Mark(Renderer r, float mode)
        {
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(ModeId, mode);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Every renderer under <paramref name="root"/> not yet in a
        /// mode: <paramref name="mode"/> (the buildings' child its own).</summary>
        public static void MarkTile(GameObject root, float mode, string buildingsChild)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.HasPropertyBlock())
                {
                    mpb ??= new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    if (mpb.GetFloat(ModeId) > 0.5f) continue;
                }
                Mark(r, buildingsChild != null && r.gameObject.name == buildingsChild ? ModeBuildings : mode);
            }
        }
    }
}
