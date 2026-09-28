# Vendored inputs of the Charlotte export

**RETIRED by WP-04b (2026-09-28).** The export no longer reads either file:
Charlotte's creeks and lakes now come from Mecklenburg County GIS (CC0) and
USGS 3DHP (public domain), each with its bed sampled from USGS 3DEP
(`tools/city/lib/water.mjs`, `tools/city/fetch/fetch_water.mjs`). The files
stay here, byte for byte, as the record of what shipped until then (the
exports up to WP-02 can still be rebuilt from their commits), and they are no
longer inputs in `cache_manifest.json`. What the RG2 drawing was traced over is
still unrecorded; since nothing ships from it any more, the question no longer
blocks anything.

`tools/city/export_osm.mjs` used to read three files straight out of a
Racing-Game-2 checkout at `C:/Users/mcgee/code/Racing-Game-2`. Since WP-02
(2026-09-28) it reads these copies instead, so the export runs from this
repository and its cache alone. `.gitattributes` here turns off line-ending
conversion: the files are compared by sha256 (`cache_manifest.json`), so git
must store and check them out byte for byte.

Re-vendor (only if RG2 ever changes them, which would move the water):

    node tools/city/vendor/vendor_rg2.mjs [path/to/Racing-Game-2]

## rg2/baselineWater.ts

- **What:** Racing-Game-2's traced water: 30 river polylines (`BASELINE_RIVERS`,
  `[width, name, x1, y1, ...]`) and 2 lake polygons (`BASELINE_LAKES`, Lake
  Wylie among them), in RG2's tile frame (2500 x 2500 tiles of 17.21 m).
- **From:** RG2 `src/config/world/baselineWater.ts`, commit `a507b4b`
  (2026-07-02), copied byte for byte. sha256
  `fd98fa2f3ca216b05ddac921502cb5542c6817e418447e38b62925a027360da8`
  (the same as the RG2 working copy; CRLF line ends and one non-UTF-8 byte in
  its first comment, both kept).
- **Made by:** RG2 H981 (commit `18feef1`, 2026-07-02) traced it from RG2
  `Maps/Rivers and Lake.png`, a 3840 x 2278 GIMP 2.10 drawing saved
  2026-07-02 (blue lines on black; RG2 commit `0f18e9c` calls the four
  `Maps/*.png` "source reference maps the baseline network was traced from").
- **Traced over what: NOT RECORDED.** Neither RG2 nor the PNG's metadata says
  what base map the drawing was made over. This is an open question for the
  owner. The water is retired by WP-04b (Mecklenburg `Creeks_Streams`, USGS
  3DHP), after which this file is no longer read.

## rg2/i485_fit.json

- **What:** the two I-485 rows the water fit registers the traced water with
  (an ICP/Umeyama similarity from RG2's tile frame into this game's metre frame;
  about 23 m mean residual).
  - `legacy_i485`: RG2's hand-traced I-485, the first `BASELINE_ROADS` row named
    I-485 in RG2 `src/config/world/baselineRoads.ts` (commit `b291b04`,
    sha256 `a9487e34...c28c`), traced from RG2 `Maps/485.png` (the same
    un-recorded base map question applies).
  - `osm_i485`: RG2's own OpenStreetMap bake of the loop, the first row named
    I-485 in RG2 `fixtures/osm/charlotte_rows.json` (commit `64e143a`, sha256
    `427ebaac...b31f`). ODbL 1.0, covered by the OpenStreetMap credit.
- The full sha256s and commits are inside the file. Only these two rows of
  those files were ever read, so only they are kept; the numbers are exact
  (JSON round-trips a double).
