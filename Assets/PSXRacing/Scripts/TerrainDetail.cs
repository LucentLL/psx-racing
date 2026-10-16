using UnityEngine;

namespace PSXRacing
{
    /// <summary>
    /// THE TERRAIN DETAIL LAYER (the GT4 Trial Mountain study, gap 4,
    /// 2026-10-15). GT4's frames carry 1.5-2x the small-scale contrast of
    /// ours (mean horizontal luma gradient 8.9-11.7 against 5.8): its grass,
    /// dirt verges and rock cuts are photographs tiled densely, where our
    /// grass slope is one smooth tile stretched over a hillside ("a golf
    /// course").
    ///
    /// WHAT IT IS: one 256 px RGBA sheet in Resources (TerrainDetail.png,
    /// tools/terraindetail/make_detail.py), each CHANNEL a band-passed
    /// luminance tile from the owner's PSX Textures v3.1 pack - R grass_1,
    /// G dirt_4, B stone_1, A snow_3 - at mean 0.5. PSX/Lit reads it in
    /// WORLD SPACE (projected on the plane the surface faces most: the
    /// ground from above, a cut from the side) at TWO octaves, a 2 m repeat
    /// for the grain at the car and 7.4 m for clumps and facets out to the
    /// fade (at 480 lines the fine one alone is lost in its mips by 10-15 m),
    /// dots both with the material's <see cref="MaskId"/> amounts and
    /// multiplies the albedo by 1 + 0.75 x sum(amount x (2t - 1)) over the
    /// two: a luminance modulation around 1, so every
    /// season's dress, the base texture's colour and the snow stay what they
    /// were on average, and no hue is added (snow gets grey, never green).
    /// It fades out between <see cref="FadeNear"/> and <see cref="FadeFar"/>
    /// metres, and its mips (point-filtered, like every texel in the game)
    /// run to grey on their own before that.
    ///
    /// WIRED AT RUNTIME, no scene build: the sheet and its switch are shader
    /// globals (<see cref="ApplyGlobals"/>), and the amounts are stamped on
    /// each material by <see cref="Stamp"/>, which PSXTexDecode.Stamp calls -
    /// so every stamp point (scene load, the season's dress, the car and prop
    /// loaders, the editor's edit-mode renders) covers it. A material is
    /// classed by its ORIGINAL _MainTex (an ENHANCED twin answers for the
    /// texture it stands in for). The amounts are never saved into a .mat
    /// (PSXTexDecodeSaveGuard zeroes them).
    ///
    /// TIED TO THE TEXTURES ROW: ENHANCED (the desktop default) draws it,
    /// CLASSIC (the phone default) does not, and then a ground pixel pays
    /// one uniform test. On: two fetches of the one 256 px sheet (tex2Dgrad,
    /// 341 KB with mips, the only new sampler) and a dozen ALU per pixel of
    /// a detailed ground within FadeFar; nothing anywhere else.
    /// </summary>
    public static class TerrainDetail
    {
        public const string ResourcePath = "TerrainDetail";
        public static readonly int MaskId = Shader.PropertyToID("_PSXDetailMask");
        static readonly int TexId = Shader.PropertyToID("_PSXDetailTex");
        static readonly int ParamsId = Shader.PropertyToID("_PSXDetailParams");

        /// <summary>Metres one repeat of the sheet covers.</summary>
        public const float RepeatMetres = 2f;
        /// <summary>Full strength up to here (metres from the eye)...</summary>
        public const float FadeNear = 25f;
        /// <summary>...and gone by here.</summary>
        public const float FadeFar = 55f;

        public enum Kind { None, Grass, Dirt, Rock, Snow }

        // How hard each class modulates, per octave x 0.75 x (2t - 1); t has
        // a spread of 0.15, so 1.0 is about +-32% of the albedo where both
        // octaves are sharp (one sigma, linear light). Snow is gentler: it is
        // bright, near the tone curve's shoulder, and a grey mottle on it
        // reads as dirt sooner than grain on grass does.
        // Dirt is under grass: at 1.1 the gravel shoulder's coarse octave
        // read as wet stains.
        public const float GrassAmount = 1.2f;
        public const float DirtAmount = 0.85f;
        public const float RockAmount = 1.2f;
        public const float SnowAmount = 0.6f;

        static Texture2D sheet;
        static bool sheetTried;

        /// <summary>The sheet, loaded once (null when the build lacks it:
        /// then the layer stays off).</summary>
        public static Texture2D Sheet
        {
            get
            {
                if (sheet == null && !sheetTried)
                {
                    sheetTried = true;
                    sheet = Resources.Load<Texture2D>(ResourcePath);
                }
                return sheet;
            }
        }

        /// <summary>Whether the layer draws: the TEXTURES row's ENHANCED.
        /// In the editor, PSX_DETAIL=0/1 forces it (A/B shots); edit-mode
        /// renders draw it unless forced off.</summary>
        public static bool Enabled
        {
            get
            {
#if UNITY_EDITOR
                string env = System.Environment.GetEnvironmentVariable("PSX_DETAIL");
                if (env == "0") return false;
                if (env == "1") return true;
                if (!Application.isPlaying) return true;
#endif
                return TexturePrefs.Enhanced;
            }
        }

        /// <summary>The sheet, the repeat, the fade and the switch, as
        /// shader globals. Cheap; called at boot, on every TEXTURES toggle
        /// and before the editor's edit-mode renders.</summary>
        public static void ApplyGlobals()
        {
            var t = Sheet;
            bool on = t != null && Enabled;
            if (t != null) Shader.SetGlobalTexture(TexId, t);
            Shader.SetGlobalVector(ParamsId, new Vector4(1f / RepeatMetres, FadeNear,
                                                         1f / (FadeFar - FadeNear), on ? 1f : 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => ApplyGlobals();

        /// <summary>What a ground texture IS, off its file name (the
        /// ORIGINAL's: see <see cref="TextureTier.OriginalOf"/>). Roads,
        /// walls, concrete and everything else: None.</summary>
        public static Kind KindOf(string texName)
        {
            if (string.IsNullOrEmpty(texName)) return Kind.None;
            string n = texName;
            // the stages' generated near ground and far mottle, per dress
            if (n == "Turf_Snow" || n == "Mottle_Snow") return Kind.Snow;
            if (n.StartsWith("Turf_") || n.StartsWith("Mottle_") || n == "FallMottle") return Kind.Grass;
            // lawns, coast grass, marsh, scrub (the builder's GroundKindOf)
            if (n.IndexOf("grass", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("Scrub")) return Kind.Grass;
            // the stages' gravel verge (GasStation Ground.jpg), the pack dirts
            if (n == "Ground" || n == "T (5)" || n.StartsWith("dirt_")) return Kind.Dirt;
            // the stages' rock cuts (rock_pt_2) and any pack rock
            if (n.StartsWith("rock_")) return Kind.Rock;
            return Kind.None;
        }

        /// <summary>The per-channel amounts a class stamps.</summary>
        public static Vector4 MaskFor(Kind k)
        {
            switch (k)
            {
                case Kind.Grass: return new Vector4(GrassAmount, 0f, 0f, 0f);
                case Kind.Dirt: return new Vector4(0f, DirtAmount, 0f, 0f);
                case Kind.Rock: return new Vector4(0f, 0f, RockAmount, 0f);
                case Kind.Snow: return new Vector4(0f, 0f, 0f, SnowAmount);
                default: return Vector4.zero;
            }
        }

        /// <summary>The class of a material, off its main texture.</summary>
        public static Kind KindOf(Material m)
        {
            if (m == null || !m.HasProperty(MaskId)) return Kind.None;
            var t = TextureTier.OriginalOf(m.mainTexture);
            return t == null ? Kind.None : KindOf(t.name);
        }

        /// <summary>Set this material's amounts from its texture. True when
        /// they changed. Never leaves a saved .mat dirty.</summary>
        public static bool Stamp(Material m)
        {
            if (m == null || !m.HasProperty(MaskId)) return false;
            Vector4 want = MaskFor(KindOf(m));
            if (m.GetVector(MaskId) == want) return false;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(m))
            {
                bool wasDirty = UnityEditor.EditorUtility.IsDirty(m);
                m.SetVector(MaskId, want);
                if (!wasDirty) UnityEditor.EditorUtility.ClearDirty(m);
                return true;
            }
#endif
            m.SetVector(MaskId, want);
            return true;
        }
    }
}
