namespace PSXRacing.EditorTools
{
    /// <summary>
    /// The texture-size ceiling, in one place. 256 is the PS1's own texture
    /// page and a 4x cut in download size for a phone, so it is the rule;
    /// these are the exemptions, each one a decision.
    /// </summary>
    public static class PSXTextureCaps
    {
        public static int MaxFor(string assetPath)
        {
            // The forest atlas is 4x4 species on one 512 sheet: clamping it to
            // 256 would halve every tree to 64 px - chunkier than the circuit
            // trees it replaces.
            if (assetPath.Contains("/BRP/Gen/TreeAtlas")) return 512;
            // The converted traffic cars (owner, 2026-09-25: "can we increase
            // the texture size cap?"). A GT2 car was four 256 PS1 pages; its
            // baked 512 atlas holds the same texels. Opted in by NAME, by
            // tools/carmodels/convert_rip.py, so no other car grows by accident.
            if (assetPath.Contains("/Art/Car/Models/") && assetPath.EndsWith("_atlas_512.png")) return 512;
            return 256;
        }
    }
}
