# Charlotte data: sources and licences

The registry of every source Charlotte's data is made from, what it is used
for, its licence and what that licence asks of us. Every package of the
Charlotte refinement that adds a source adds it here first (plan section 2.3,
"Sources"). Nothing from Google goes into any file: Google Maps, Street View
and satellite imagery are looked at, at named spots, and never saved, traced or
copied.

## Credits

**This table is read by tools** (`tools/city/lib/sources.mjs`). Each row is
one credit line, and three things are made from it:

- `tools/city/export_osm.mjs` joins the lines (one per line, in this order)
  into the attribution it writes into `charlotte_city.bytes` (section META) and
  `charlotte_routes.json` (`attribution`);
- `tools/city/credits.mjs --write` writes the pause menu's **CREDITS** page
  (`Assets/PSXRacing/Resources/psx_credits.txt`, shown by `CreditsPanel`) and
  **`LICENSES.txt`**, which every WebGL build carries beside `index.html`
  (`Assets/WebGLTemplates/PSXMobile/LICENSES.txt`);
- `node tools/city/credits.mjs` (no flag) checks both are up to date.

Both refuse to run if the table is missing or a row is malformed. Keep the
`credit`, `licence` and `licence link` columns plain ASCII, one line, with no
`|`. After an edit here: `credits.mjs --write`, then the export (`--out ...`),
so the data, the page and the file say the same thing. The in-race HUD shows
its own short OpenStreetMap line (`RaceHUD.OsmAttribution`) for the first seven
seconds; the full credits are these lines.

| id | credit | licence | licence link | shipped in |
|---|---|---|---|---|
| osm | Road network data (c) OpenStreetMap contributors, ODbL 1.0 | ODbL 1.0 | https://opendatacommons.org/licenses/odbl/1-0/ | charlotte_city.bytes, charlotte_bld.bytes, charlotte_routes.json |
| terrain | Terrain: AWS Terrain Tiles (Mapzen/Tilezen); 3DEP and SRTM data courtesy of the U.S. Geological Survey | public domain; attribution requested | https://github.com/tilezen/joerd/blob/master/docs/attribution.md | charlotte_dem.bytes |

## Registry

### In use

| Source | Used for | Licence | Duty | Files, snapshot |
|---|---|---|---|---|
| **OpenStreetMap**, via the Overpass API | Roads, ramps, junction controls, streets in the core, building footprints, the three race routes | ODbL 1.0 | Credit (above). Keep the city graph a separable derivative: non-OSM data goes in its own sections (WATR) or files. | `tools/city/cache/` (gitignored, on this machine only; sha256 in `cache_manifest.json`). `ways_all.json` and `nodes_all.json` snapshot `2026-09-12T02:44:33Z`; `streets_core.json` and `buildings_core.json` `2026-09-12T02:39:51Z`. Queries: `tools/city/fetch/`. |
| **AWS Terrain Tiles "skadi"** (Mapzen/Tilezen joerd; in the US a bare-earth mosaic built from USGS NED/3DEP, with SRTM) | The 60 m ground grid (`charlotte_dem.bytes`); the mountain stages' ground too (`tools/roads`) | Built on public-domain USGS data; Tilezen asks for attribution | Credit (above), while any of it ships. **It was missing until WP-02 (2026-09-28).** | `tools/roads/cache/N34W081`, `N34W082`, `N35W081`, `N35W082` `.hgt.gz` from `https://s3.amazonaws.com/elevation-tiles-prod/skadi/` (sha256 in `cache_manifest.json`). |
| **Racing-Game-2 traced water** (owner's own work) | The creeks and Lake Wylie (section WATR), and the water spans they cause (SPAN) | Owner's own work | None. **Open question for the owner: what map was `Maps/Rivers and Lake.png` drawn over?** It is a GIMP 2.10 drawing (saved 2026-07-02), blue lines on black, with nothing in its metadata about a base map. If it was traced over Google Maps it must go; WP-04b replaces it with Mecklenburg `Creeks_Streams` and USGS 3DHP anyway. | Vendored in `tools/city/vendor/rg2/baselineWater.ts` (byte for byte). See `tools/city/vendor/README.md`. |
| **Racing-Game-2 I-485 rows** (the water fit's registration pair) | Registering the traced water into the game frame (ICP, 23 m mean residual) | The legacy row: owner's own trace (`Maps/485.png`). The OSM row: ODbL 1.0 (RG2's own OSM bake) | None beyond the OSM credit. | `tools/city/vendor/rg2/i485_fit.json`, with the source files' sha256 and commits. |
| **Quarry outlines**, OpenStreetMap ways 246229420 (Arrowood Quarry) and 246229422 (Pineville Quarry) | Naming the two places where the ground may be clamped to the datum (`DEM_CLAMP` in the exporter) | ODbL 1.0 | Covered by the OSM credit. | Hand-entered boxes in `export_osm.mjs`, keyed by the way ids. |
| **USGS 3DEP** 1 m, 1/3" (the flatness survey, 2026-09-27) | Editor-only truth for the metrics: `tools/city/truth/` (8 transects at 2 m, core at 10 m, city at 120 m) | Public domain | Credit "U.S. Geological Survey, 3D Elevation Program" wherever it is shown. Not shipped in the game. | `tools/city/truth/`; `transects_meta.json` says how each file was made. |
| **Owner's PSX texture packs** | Every material (owner rule) | The packs' own licences | Materials only from the packs. | `Assets/PSXRacing/Art`. |

### Planned (not used yet): add the row above when a package starts using it

| Source | For | Licence | Duty |
|---|---|---|---|
| USGS 3DEP 1/3" tiles n35w081, n35w082, n36w081, n36w082 (2026-04-17) | WP-04 60 m grid, WP-05 road heights, WP-13 30 m grid | Public domain | Credit "U.S. Geological Survey, 3D Elevation Program". Replaces the skadi credit only when skadi is no longer used anywhere (the stages use it too). |
| USGS 3DEP 1 m / NC Phase 4 QL1 lidar (2016-17) | Ditch templates and spot checks (not shipped) | Public domain | Same credit. |
| NC Spatial Data Download / NC OneMap | Alternative lidar access | Verify the terms first | Record on use. |
| USGS 3DHP / NHDPlus | WP-04b/WP-25 creeks and lakes outside Mecklenburg | Public domain | Credit USGS. |
| Mecklenburg County GIS (`Creeks_Streams`, TreeCanopy, Streets `SPEEDLIMIT`/`NUMBEROFLANES`/`PAVEMENTWIDTH`, EdgeOfPavement) | WP-04b, 08, 20, 25, 32; metrics ACCURACY | MIT or CC0 per dataset (the county's GitHub and catalog disagree) | Record per file; verify before shipping. MIT needs its notice to travel with copies. |
| City of Charlotte open data (signals, street trees, sidewalks, resurfacing `CURB`, storm channels) | WP-16, 20, 22, 25 | CC BY 4.0 | In-game credit "City of Charlotte", a licence link, and a note that the data was modified. |
| USFS / NLCD tree canopy and land cover | WP-08, WP-21 | Public domain | Credit USFS/USGS; verify the canopy year. |
| FHWA NBI | Bridges | Public domain | - |
| NCDOT GIS (outdoor advertising, structures, guardrail, road characteristics, signals, AADT) | Validation and statistics only | No licence stated | Derived placements only with the owner's OK and NCDOT's confirmation. Credit "NCDOT GIS Unit". Strip `Google_Map` / `GOOGLE_LINK` fields from anything kept. |
| FHWA MUTCD; AASHTO | Taper lengths, sign mounting, radii | Formulas only | Nothing copied. |
| npm `geotiff` | WP-04 acquisition (dev only) | MIT | Dev dependency only. |
| tinf inflate (only if `DeflateStream` fails on WebGL) | WP-13 | zlib | Keep the notice in the source. |

### Not a source

Google Maps, Street View and satellite imagery: **look only**, at the spots a
package names. Nothing saved, traced or copied; no bulk scraping. The same goes
for NCDOT signal design plans (`Signal_Pla`).

## Notes

- **One pinned fetch for the layers later packages need** (critic C19):
  `tools/city/fetch/fetch_layers.mjs` fetches pedestrian crossings, turn
  restrictions, traffic calming, railways and level crossings, power poles and
  lines, trees, advertising, barriers and culverts over the beltway bbox,
  every query pinned with `[date:"2026-09-12T02:44:33Z"]` (the road
  snapshot's `timestamp_osm_base`), so node ids and way splits match the
  graph. Bodies in `tools/city/cache/layers/` (gitignored); what was fetched,
  when, how many elements and each body's sha256 in
  `tools/city/fetch/layers_manifest.json` (committed). An answer from a server
  whose data ends before the pinned date is refused (a stale mirror silently
  answers a future `[date:]` with its own last state). OpenStreetMap, ODbL,
  covered by the OSM credit. Nothing ships from these until a package uses
  them.
- The fetch scripts in `tools/city/fetch/` that made the road cache send a
  User-Agent with the owner's e-mail address, and they are in the public
  repository. Replacing it with the repository URL is owner question Q12; they
  are unchanged until he answers (the new `fetch_layers.mjs` names the
  repository only).
