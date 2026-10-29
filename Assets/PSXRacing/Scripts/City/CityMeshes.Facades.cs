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
