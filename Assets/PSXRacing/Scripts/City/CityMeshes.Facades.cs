using UnityEngine;

namespace PSXRacing.City
{
    public static partial class CityMeshes
    {
        // ------------------------------------------------------------------
        //  THE BUILDING PASS: FACADE VARIETY (2026-10-28). The owner: "the
        //  city building textures (especially at night) look extremely
        //  unprofessional and low quality". Seven looks repeated across the
        //  whole city, and the glass at 10-16 px a floor. The atlas is now
        //  SIXTEEN columns (tools/city/facade_atlas.py): the three curtain
        //  walls re-laid at 8 floors a repeat (32 px a floor), and eight more
        //  from the owner's Buildings pack photographs and PSX Textures II -
        //  brick in six kinds, a loft and a rendered apartment block - picked
        //  per building by its use, height and hash. A tagged material or
        //  colour (OSM, truth/uptown_ref_looks.json) still picks the FAMILY
        //  it always did; only the untagged and the low and mid-rise brick
        //  and stone fan out. Columns 8-15 ride as the vertex red channel's
        //  low bit (PickFacade; TintByte keeps every tint's low bit clear):
        //  the alpha's top three bits were all the columns there were room
        //  for, and its low five are metal, crown and the crown's LED.
        // ------------------------------------------------------------------
        /// <summary>PSX_FAC_VARIETY=0: the families pick only the old seven
        /// looks (the A/B; the curtain walls stay re-laid).</summary>
        public static bool FacadeVarietyOff = System.Environment.GetEnvironmentVariable("PSX_FAC_VARIETY") == "0";

        const int LookParts = 7, LookRedSash = 8, LookTanTile = 9, LookMottled = 10, LookTenement = 11,
                  LookDecoBrick = 12, LookLoft = 13, LookTanBrick = 14, LookApartment = 15;

        /// <summary>The photographed masonry looks, the old brick (4) and
        /// stone (3) among them: no tower structure, only its AO and coping.</summary>
        static bool IsPlainLook(int look) => look == LookStone || look == LookBrick || (look >= LookRedSash && look <= LookApartment);

        /// <summary>Looks whose storey is a home's (3.1 m) when OSM tags no use.</summary>
        static bool IsHomeLook(int look) => look == LookBrick || look == LookRedSash || look == LookTenement ||
                                            look == LookTanBrick || look == LookApartment;

        /// <summary>The red channel: the tint with the column's high bit in
        /// its low bit (PSX/Lit: column = alpha / 32 + 8 x red's low bit).</summary>
        static byte TintR(float k, int look) => (byte)(TintByte(k) | ((look >> 3) & 1));

        /// <summary>The look's own stream, apart from PickFacade's family draw.</summary>
        static int LookDraw(uint h)
        {
            uint hb = h * 0x9E3779B1u; hb ^= hb >> 15; hb *= 0x85EBCA6Bu; hb ^= hb >> 13;
            return (int)((hb >> 8) % 100u);
        }

        /// <summary>The brick family: by use (offices and civic the smarter
        /// brick, homes the walk-ups and tenements), by height (the low
        /// blocks are NoDa's and South End's mills and shops: lofts among
        /// them) and by hash.</summary>
        static int BrickLook(uint h, float height, byte use)
        {
            if (FacadeVarietyOff) return LookBrick;
            int p = LookDraw(h);
            if (use == 1 || use == 3)
                return p < 30 ? LookDecoBrick : p < 55 ? LookTanTile : p < 78 ? LookMottled : LookBrick;
            if (use == 2)
                return p < 26 ? LookBrick : p < 48 ? LookRedSash : p < 68 ? LookTenement : p < 84 ? LookTanBrick : LookMottled;
            if (height <= 15f)
                return p < 20 ? LookBrick : p < 38 ? LookLoft : p < 56 ? LookRedSash : p < 72 ? LookTanBrick : p < 86 ? LookMottled : LookTenement;
            return p < 22 ? LookBrick : p < 40 ? LookDecoBrick : p < 56 ? LookTenement : p < 70 ? LookTanTile : p < 84 ? LookRedSash : LookLoft;
        }

        // ------------------------------------------------------------------
        //  THE SKYLINE PASS (2026-10-29). The owner, with a daytime aerial
        //  from the stadium side: "The uptown skyline does not seem accurate".
        //  Three faults in WHAT a tower wears (the shading's are PSX/Lit's):
        //   * a part tagged only "concrete" in OSM threw its tower's measured
        //     colour away (the exporter keeps a part's colour only when the
        //     part names no material), so the spired crown tower's main shaft
        //     wore a hashed grey at half the albedo of the tiers round it;
        //   * a measured tower whose outline stands only to its podium's roof
        //     (the reference tiers drawn on it) was given the PODIUM's height
        //     class - its 50-storey shaft a mid-rise's punched precast;
        //   * the reference model is older than some of the towers and wrong
        //     about one: LandmarkLooks below.
        // ------------------------------------------------------------------
        const byte MatGlass = 1, MatBrick = 2;
        struct LandmarkLook
        {
            public byte landmark, mat, rhythm; public Color32 colour;
            public LandmarkLook(byte landmark, byte mat, byte rhythm, Color32 colour)
            { this.landmark = landmark; this.mat = mat; this.rhythm = rhythm; this.colour = colour; }
        }
        /// <summary>What a landmark (the exporter's neutral table,
        /// tools/city/lib/parts.mjs) is clad in TODAY, where the reference
        /// model (truth/uptown_ref_looks.json) is older or wrong and OSM tags
        /// nothing: the material always, a colour only where the owner's
        /// photograph shows the cladding (its glass measured there, in the
        /// reference looks' terms: the 550 S Tryon glass the model has as
        /// #c1d7d9 reads (88,125,149) in the photograph, the scale between).
        /// Shapes stay OSM's; a colour of none lets the family hash its kind.</summary>
        static readonly LandmarkLook[] LandmarkLooks =
        {
            // 7 the glass setback tower (2021, newer than the model): a glass curtain wall
            new LandmarkLook(7, MatGlass, 0, default),
            // 8 the brick headquarters (2021): brick, after South End's mills (its architect), glass only at the penthouse
            new LandmarkLook(8, MatBrick, 0, default),
            // 9 the slim residential tower: the model has it as grey stone; it is blue glass
            // with a white slab edge every floor (the photograph: (47,95,128))
            new LandmarkLook(9, MatGlass, RhythmHorizontal, new Color32(0x67, 0xa2, 0xbb, 255)),
            // 15 the pointed tower (2019; half clad in the photograph, the model has it as tan stone):
            // blue glass between light frames that climb it, under OSM's pyramidal glass crown (the photograph's glass: (51,82,106))
            new LandmarkLook(15, MatGlass, RhythmVertical, new Color32(0x70, 0x8b, 0x9b, 255)),
        };
        /// <summary>PSX_CITY_LOOKFIX=0: the three faults as they were (the A/B).</summary>
        public static bool LookFixOff = System.Environment.GetEnvironmentVariable("PSX_CITY_LOOKFIX") == "0";

        static CityMap towerTopMap;
        static float[] towerTopOf;
        /// <summary>An outline's height as a TOWER: its own, or its tallest
        /// part's where parts stand on it.</summary>
        static float TowerTop(CityMap map, int oi)
        {
            if (towerTopMap != map)
            {
                towerTopMap = map;
                var fs = map.footprints;
                towerTopOf = new float[fs.Length];
                for (int i = 0; i < fs.Length; i++) towerTopOf[i] = fs[i].h;
                for (int i = 0; i < fs.Length; i++)
                {
                    int o = fs[i].outline;
                    if (fs[i].part && o >= 0 && o < fs.Length && fs[i].h > towerTopOf[o]) towerTopOf[o] = fs[i].h;
                }
            }
            return oi >= 0 && oi < towerTopOf.Length ? towerTopOf[oi] : 0f;
        }

        /// <summary>What PickFacade is given for a footprint (EmitFootprint
        /// and the probe alike): its material, colour and rhythm, and the
        /// height its look and storeys are picked by. <paramref name="ownH"/>
        /// is its own drawn height (an outline's walls).</summary>
        static void FacadeArgs(CityMap map, int fi, CityMap.Footprint f, CityMap.Footprint gf, float ownH,
                               out byte mat, out Color32 colour, out byte rhythm, out float lookH)
        {
            mat = f.mat; colour = f.colour; rhythm = f.rhythm != 0 ? f.rhythm : gf.rhythm;
            lookH = gf != f ? Mathf.Max(gf.h, 1f) : ownH;
            if (LookFixOff) return;
            // a part naming only its outline's material keeps the outline's measured colour
            if (gf != f && colour.a != 255 && gf.colour.a == 255 && (mat == gf.mat || mat == 0)) { colour = gf.colour; mat = gf.mat; }
            // the height class of the whole tower, every part of it alike (one storey height)
            int oi = gf != f ? f.outline : fi;
            float top = TowerTop(map, oi);
            if (top > lookH + 0.5f && (gf != f || top > f.h + 0.5f)) lookH = top;
            // the landmark's own cladding today
            byte lm = gf.landmark;
            if (lm != 0)
                foreach (var L in LandmarkLooks)
                    if (L.landmark == lm) { mat = L.mat; colour = L.colour; if (L.rhythm != 0) rhythm = L.rhythm; break; }
        }

        /// <summary>A footprint's material as FacadeArgs resolves it.</summary>
        static byte FacadeArgsMat(CityMap map, int fi, CityMap.Footprint f)
        {
            var gf = f.part && f.outline >= 0 && f.outline < map.footprints.Length ? map.footprints[f.outline] : f;
            FacadeArgs(map, fi, f, gf, f.h, out byte m, out _, out _, out _);
            return m;
        }

        /// <summary>A GLASS tower's pyramid crown (the pointed tower's
        /// prismatic glass spire, OSM's pyramidal part): roof shapes are drawn
        /// as metal (B1b), and on the tower's own dark blue glass column the
        /// metal light read a dark grey cone. Its glass and frames as the
        /// silver grid's - a light, glinting crown, as one reads from afar.
        /// EmitFootprint restores the look and tint after the roof.</summary>
        static void GlassCrownLook(bool on)
        {
            if (!on) return;
            facLook = LookSilver;
            var t = Bucket.Tint;
            Bucket.Tint = new Color32(TintR(1.30f, LookSilver), TintByte(1.36f), TintByte(1.46f), (byte)((t.a & 31) | ((LookSilver & 7) * 32)));
        }

        /// <summary>THE SKYLINE PASS (2026-10-29): the look and tint a
        /// footprint's walls get, picked exactly as EmitFootprint picks them
        /// (a part by its outline's centre, height and rhythm) - one line for
        /// the match tool's table (Editor/SkylineMatchShots.cs). Never call it
        /// while a tile is being built (it borrows the builder's statics).</summary>
        public static string FacadeProbe(CityMap map, int fi)
        {
            var f = map.footprints[fi];
            var gf = f.part && f.outline >= 0 && f.outline < map.footprints.Length ? map.footprints[f.outline] : f;
            FacadeArgs(map, fi, f, gf, f.h, out byte pm, out Color32 pc, out byte pr, out float ph);
            PickFacade(gf.centre, (byte)(f.style == 4 ? 2 : f.style), f.use, pm, pc, 0f, ph, pr);
            var t = Bucket.Tint;
            int look = facLook;
            EndFacade();
            var m = FacadeLooks[look].meanLin;
            float kr = (t.r & 0xFE) / 127.5f, kg = t.g / 127.5f, kb = t.b / 127.5f;
            int hero = f.landmark != 0 ? HeroOf(map, f) : -1;
            return $"look {look,2} k ({kr:0.00},{kg:0.00},{kb:0.00}) albedo ({m.x * kr:0.000},{m.y * kg:0.000},{m.z * kb:0.000})" +
                   (hero >= 0 ? $" hero {Heroes[hero].shape}" : "");
        }

        /// <summary>The stone and render family below office-tower height:
        /// homes mostly the rendered apartment block (South End's mid-rises),
        /// offices the precast and granite, untagged a mix.</summary>
        static int StoneLook(uint h, float height, byte use)
        {
            if (FacadeVarietyOff) return LookStone;
            int p = LookDraw(h);
            if (use == 2) return p < 60 ? LookApartment : p < 85 ? LookStone : LookTanTile;
            if (use == 1 || use == 3) return p < 40 ? LookStone : p < 72 ? (height >= 15f ? LookPrecast : LookStone) : LookGranite;
            return p < 45 ? LookStone : p < 75 ? LookApartment : LookTanTile;
        }
    }
}
