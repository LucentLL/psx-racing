using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE ROAD LAYER AT ONE SPOT (2026-10-25, the owner: "Roads should be
    /// able to be dry, while scenery is covered in snow ... Certain patches
    /// can be dry, some can be wet/slick (like bridges)").
    ///
    /// The hour's road (<see cref="Seasons.Now"/>: water, slush, snow, ice on
    /// the open road and, apart, on bridge decks) is laid over the world
    /// through a fixed patch mask, so the same stretch dries last every lap
    /// and a driver can learn it - and the road shader draws the very same
    /// mask (PSXWet.cginc PSXWetPatch / PSXLocalShare, ported line for line
    /// below: change one, change both). Each tyre asks for the spot it stands
    /// on; the AI and the traffic ask for the road ahead.
    /// </summary>
    public static class RoadSurface
    {
        public struct Sample
        {
            /// <summary>Multiplier on the tyre's dry mu (1 = dry tarmac).</summary>
            public float grip;
            /// <summary>0 dry .. 1 as slippery as a soaked road (or worse):
            /// what the drift layer, the screech, the marks and the smoke read.</summary>
            public float wetT;
            /// <summary>0..1 water standing here (aquaplaning, spray).</summary>
            public float water;
            public SurfaceKind kind;
        }

        // ---- the mask: PSXWet.cginc, ported -------------------------------

        const float PatchScale = 0.0263f, PatchLo = 0.25f, PatchHi = 0.75f, PatchSpan = 0.85f, PatchEdge = 0.15f;

        static float Frac(float x) => x - Mathf.Floor(x);

        /// <summary>PSXLitHash.</summary>
        static float Hash(float px, float py)
        {
            float x = Frac(px * 0.1031f), y = Frac(py * 0.1031f), z = Frac(px * 0.1031f);
            float d = x * (y + 33.33f) + y * (z + 33.33f) + z * (x + 33.33f);
            x += d; y += d; z += d;
            return Frac((x + y) * z);
        }

        /// <summary>PSXLitNoise.</summary>
        static float Noise(float px, float py)
        {
            float cx = Mathf.Floor(px), cy = Mathf.Floor(py);
            float fx = px - cx, fy = py - cy;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Hash(cx, cy), b = Hash(cx + 1f, cy), d = Hash(cx, cy + 1f), e = Hash(cx + 1f, cy + 1f);
            float ab = a + (b - a) * sx, de = d + (e - d) * sx;
            return ab + (de - ab) * sy;
        }

        static float SmoothStep01(float lo, float hi, float x)
        {
            float t = Mathf.Clamp01((x - lo) / (hi - lo));
            return t * t * (3f - 2f * t);
        }

        /// <summary>PSXWetPatch: 0 = a crown that dries first, 1 = a low spot
        /// that keeps its water (and its snow) longest.</summary>
        public static float Patch(float x, float z)
        {
            float px = x * PatchScale, py = z * PatchScale;
            float n = Noise(px + 101.3f, py + 101.3f) * 0.65f + Noise(px * 2.03f + 47.7f, py * 2.03f + 47.7f) * 0.35f;
            return SmoothStep01(PatchLo, PatchHi, n);
        }

        /// <summary>PSXLocalShare: how much of a deck's amount lies on this patch.</summary>
        public static float LocalShare(float amount, float patch) =>
            Mathf.Clamp01((amount - PatchSpan * (1f - patch)) / PatchEdge);

        // ---- grip ----------------------------------------------------------

        /// <summary>A deck's state read at one patch value.</summary>
        public static Sample OfDeck(in DeckState d, float patch)
        {
            var s = new Sample { grip = 1f, kind = SurfaceKind.Dry };
            float lw = LocalShare(d.water, patch);
            float lsl = LocalShare(d.slush, patch);
            float ls = LocalShare(d.snow, patch);
            s.water = lw;
            if (lw > 0f)
            {
                s.grip = Mathf.Lerp(1f, Climate.WetGrip, lw);
                s.kind = lw >= 0.5f ? SurfaceKind.Wet : SurfaceKind.Damp;
                if (d.ice && lw > 0.2f) { s.grip = Climate.IceGrip; s.kind = SurfaceKind.Ice; }
            }
            if (lsl > 0f)
            {
                float g = Mathf.Lerp(Climate.WetGrip, Climate.SlushGrip, lsl);
                if (g < s.grip) { s.grip = g; s.kind = SurfaceKind.Slush; }
            }
            if (ls > 0f)
            {
                float g = Mathf.Lerp(Climate.WetGrip, Climate.SnowGrip, ls);
                if (g < s.grip) { s.grip = g; s.kind = SurfaceKind.Snow; }
            }
            if (d.frost && Climate.FrostGrip < s.grip) { s.grip = Climate.FrostGrip; s.kind = SurfaceKind.Frost; }
            s.wetT = Mathf.Clamp01((1f - s.grip) / (1f - Climate.WetGrip));
            return s;
        }

        /// <summary>The surface under a tyre at a world point, now.</summary>
        public static Sample At(Vector3 p, bool road, bool bridge)
        {
            var w = Seasons.Now;
            if (!road) return OffRoad(w);
            return OfDeck(bridge ? w.bridge : w.road, Patch(p.x, p.z));
        }

        /// <summary>The ground off the road: snow lying, or wet.</summary>
        public static Sample OffRoad(in WeatherNow w)
        {
            var s = new Sample();
            if (w.scenery == SceneryKind.Snow) { s.grip = Climate.SnowOffroadGrip; s.kind = SurfaceKind.Snow; }
            else
            {
                s.grip = Mathf.Lerp(1f, Climate.WetOffroadGrip, w.groundWet);
                s.kind = w.groundWet > 0.5f ? SurfaceKind.Wet : w.groundWet > 0.05f ? SurfaceKind.Damp : SurfaceKind.Dry;
                s.water = w.groundWet;
            }
            s.wetT = Mathf.Clamp01((1f - s.grip) / (1f - Climate.WetGrip));
            return s;
        }

        /// <summary>The open road's grip at a typical patch (the mask's
        /// middle): what the HUD line, the traffic's caution and anything
        /// without a point to stand on reads.</summary>
        public static float TypicalRoadGrip(in WeatherNow w) => OfDeck(w.road, 0.5f).grip;

        // ---- bridges -------------------------------------------------------

        /// <summary>Does a deck behave differently from the road today? Only
        /// then is it worth a ray to find out what a wheel is standing on.</summary>
        public static bool BridgeMatters(in WeatherNow w) =>
            w.bridge.ice != w.road.ice || w.bridge.frost || Mathf.Abs(w.bridge.snow - w.road.snow) > 0.05f ||
            Mathf.Abs(w.bridge.slush - w.road.slush) > 0.05f || Mathf.Abs(w.bridge.water - w.road.water) > 0.1f;

        /// <summary>Clear air this far under a road surface makes it a bridge.</summary>
        public const float BridgeAirM = 4f;

        static readonly RaycastHit[] hitScratch = new RaycastHit[8];
        static readonly System.Collections.Generic.HashSet<int> segScratch = new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// Is the road under this wheel a BRIDGE DECK? Derived, not flagged,
        /// from what each venue already has at runtime:
        ///   - Charlotte: the map's own edges (OSM bridge=yes), the one at this
        ///     point whose profile height matches the wheel's (a deck over a
        ///     street is two edges in plan);
        ///   - a circuit's decks are named BridgeDeck#;
        ///   - anywhere else, AIR UNDERNEATH (<see cref="ProbeBridge"/>), with
        ///     a second look from above so a road ribbon that dips into its
        ///     own terrain does not read as a deck.
        /// Called only when a deck differs from the road today
        /// (<see cref="BridgeMatters"/>), a wheel at a time, a few times a second.
        /// </summary>
        public static bool IsBridge(Vector3 contact, Vector3 up, Collider col, int mask)
        {
            var world = City.CityWorld.Active;
            if (world != null) return CityBridgeAt(contact);
            if (col != null && col.name.StartsWith("BridgeDeck", System.StringComparison.Ordinal)) return true;
            if (!ProbeBridge(contact, up, mask)) return false;
            int n = Physics.RaycastNonAlloc(contact + up * 0.3f, -up, hitScratch, BridgeAirM + 0.3f, mask,
                                            QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (hitScratch[i].collider != col) return false;
            return true;
        }

        static bool CityBridgeAt(Vector3 p)
        {
            var map = City.CityMap.Get();
            if (map == null) return false;
            const float R = 9f;
            segScratch.Clear();
            map.EdgeSegsInRect(new Vector2(p.x - R, p.z - R), new Vector2(p.x + R, p.z + R), segScratch);
            var q = new Vector2(p.x, p.z);
            float best = 1.5f; bool bridge = false;
            foreach (var packed in segScratch)
            {
                int ei = packed >> 12, si = packed & 0xFFF;
                var e = map.edges[ei];
                Vector2 a = e.pts[si], b = e.pts[si + 1], d = b - a;
                float L2 = d.sqrMagnitude;
                float t = L2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, d) / L2) : 0f;
                if (Vector2.Distance(q, a + d * t) > R) continue;
                float dy = Mathf.Abs(e.YAt(e.s[si] + Mathf.Sqrt(L2) * t) - p.y);
                if (dy < best) { best = dy; bridge = e.bridge; }
            }
            return bridge;
        }

        /// <summary>
        /// Is the road at this contact point a BRIDGE DECK? Derived, not
        /// flagged - every venue builds its decks its own way (circuits'
        /// BridgeDeck meshes, Bogue's graded causeways, Charlotte's tile decks
        /// and parking decks), and the one thing all of them share is air
        /// underneath. A ray from just under the surface: single-sided mesh
        /// colliders and solids it starts inside do not answer it, so what it
        /// finds is the ground below; nothing within <see cref="BridgeAirM"/>
        /// means a deck with no ground holding its warmth. (A top deck of a
        /// parking garage counts too: it ices like one.)
        /// </summary>
        public static bool ProbeBridge(Vector3 contact, Vector3 up, int mask)
        {
            Vector3 from = contact - up * 0.35f;
            return !Physics.Raycast(from, -up, BridgeAirM, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
