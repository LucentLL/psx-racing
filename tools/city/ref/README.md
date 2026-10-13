# Uptown reference measurements

The owner's model of uptown Charlotte (City Pack 7, in the owner's OneDrive
`Low Poly City/city_pack_7.glb`) may NOT be shipped. It is used only to
MEASURE public buildings. No mesh, texture or UV of it enters the game.

1. Export the model's triangles and its wall colours (Blender 4.1, headless):

       set SRC=...\Low Poly City\city_pack_7.glb
       set OUT=%TEMP%\cp7_tris.json    & blender --background --python blender_tris.py
       set OUT=%TEMP%\cp7_cols.json    & blender --background --python blender_wallcols.py

2. `cp7_xform.json` is the 2D similarity from the model to export_osm.mjs's
   local metres. It was fitted on 11 towers: rms 7 m, scale 0.967,
   rotation -46.85 deg.
3. From tools/city:

       py ref_tiers.py %TEMP%\cp7_tris.json ref\cp7_xform.json   -> truth/uptown_ref_parts.json (opt-in, PSX_CITY_REFTIERS=1)
       py ref_looks.py %TEMP%\cp7_cols.json ref\cp7_xform.json   -> truth/uptown_ref_looks.json (on; PSX_CITY_REFLOOKS=0 off)

4. Run `node export_osm.mjs --manifest`, then `--out ...` as usual.
