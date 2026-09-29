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
| usgs3dep | Terrain and creek beds: U.S. Geological Survey, 3D Elevation Program (3DEP) | public domain | https://www.usgs.gov/3d-elevation-program | charlotte_dem.bytes, charlotte_city.bytes |
| meckgis | Creeks, lakes and ponds in Mecklenburg: Mecklenburg County GIS, CC0 1.0 | CC0 1.0 | https://creativecommons.org/publicdomain/zero/1.0/ | charlotte_city.bytes |
| usgs3dhp | Creeks and lakes outside Mecklenburg: U.S. Geological Survey, 3D Hydrography Program (3DHP) | public domain | https://www.usgs.gov/3d-hydrography-program | charlotte_city.bytes |
| terrain | Mountain stage terrain: AWS Terrain Tiles (Mapzen/Tilezen); 3DEP and SRTM data courtesy of the U.S. Geological Survey | public domain; attribution requested | https://github.com/tilezen/joerd/blob/master/docs/attribution.md | the mountain stages' ground (tools/roads) |

## Registry

### In use

| Source | Used for | Licence | Duty | Files, snapshot |
|---|---|---|---|---|
| **OpenStreetMap**, via the Overpass API | Roads, ramps, junction controls, streets in the core, building footprints, the three race routes | ODbL 1.0 | Credit (above). Keep the city graph a separable derivative: non-OSM data goes in its own sections (WATR) or files. | `tools/city/cache/` (gitignored, on this machine only; sha256 in `cache_manifest.json`). `ways_all.json` and `nodes_all.json` snapshot `2026-09-12T02:44:33Z`; `streets_core.json` and `buildings_core.json` `2026-09-12T02:39:51Z`. Queries: `tools/city/fetch/`. |
| **USGS 3DEP 1/3 arc-second DEM**, cells n35w081, n35w082, n36w081, n36w082 (Last-Modified 2026-04-17 on the USGS staging bucket) | Charlotte's 60 m ground grid (`charlotte_dem.bytes`: the mean of the pixels in each 60 m cell, no filter; WP-04) and the creek beds (section WBED; WP-04b) | Public domain (US Government work) | Credit "U.S. Geological Survey, 3D Elevation Program" (above). | `tools/city/fetch/fetch_3dep.mjs` range-reads the COGs from `https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/current/` into `tools/city/cache/3dep/box13.f32` (or `%PSX_GIS_DIR%\3dep`), 6102 x 5616 px, 137 MB, gitignored; `box13.json` holds the georeference, the source files' Last-Modified/ETag and the sha256 (also in `cache_manifest.json`). |
| **Mecklenburg County GIS "Creeks and Streams"** (`CreeksAndStreams/FeatureServer/0`) and **"Lakes and Ponds"** (`LakesAndPonds/FeatureServer/0`) | Creek centrelines inside the county (class `mt640`: open channels draining more than 640 acres) and the lakes and ponds of 2 ha or more (section WATR; WP-04b) | **CC0 1.0.** The county's Open Mapping site: "Unless otherwise specified, our data is released under a CC0 ('No Rights Reserved') license and our open source software projects are released under a MIT software license" (checked 2026-09-28). The layers' metadata specifies no other licence (its use limitations are a warranty disclaimer and "does not support secondary distribution", i.e. no support, not a restriction). The "MIT" the surveys quoted is the site's software licence, not the data's. | Credit (above), as a courtesy. | `tools/city/fetch/fetch_water.mjs` into `tools/city/cache/water/meck_creeks.geojson` and `meck_lakes.geojson` (gitignored); query, fetch time, count and sha256 in `tools/city/fetch/water_manifest.json`. |
| **USGS 3D Hydrography Program (3DHP)** flowlines (layer 50) and waterbodies (layer 60) | Creek centrelines outside Mecklenburg (channel lines of Strahler order 4 or more), the stream order that sets every creek's modelled width, and lakes outside the county (WP-04b) | Public domain (US Government work) | Credit "U.S. Geological Survey, 3D Hydrography Program" (above). | `fetch_water.mjs` into `tools/city/cache/water/usgs_flowlines.geojson` and `usgs_waterbodies.geojson`; see `water_manifest.json`. |
| **US Census TIGERweb**, Mecklenburg County boundary (GEOID 37119) | Deciding which creeks are inside the county (county lines) and which outside (3DHP) | Public domain (US Government work) | None; not shipped. | `tools/city/cache/water/county.geojson`, same manifest. |
| **AWS Terrain Tiles "skadi"** (Mapzen/Tilezen joerd; in the US a bare-earth mosaic built from USGS NED/3DEP, with SRTM) | The mountain stages' ground (`tools/roads`). Charlotte's ground until WP-04 (2026-09-28) | Built on public-domain USGS data; Tilezen asks for attribution | Credit (above), while any of it ships. | `tools/roads/cache/*.hgt.gz` from `https://s3.amazonaws.com/elevation-tiles-prod/skadi/`. |
| **Quarry outlines**, OpenStreetMap ways 246229420 (Arrowood Quarry) and 246229422 (Pineville Quarry) | Naming the two places where the ground may be clamped to the datum (`DEM_CLAMP` in the exporter) | ODbL 1.0 | Covered by the OSM credit. | Hand-entered boxes in `export_osm.mjs`, keyed by the way ids. |
| **USGS 3DEP** 1 m, 1/3" (the flatness survey, 2026-09-27; the exporter's own 1/3" box since WP-04) | Editor-only truth for the metrics: `tools/city/truth/` (8 transects at 2 m, core at 10 m, city at 120 m) | Public domain | Credit "U.S. Geological Survey, 3D Elevation Program" wherever it is shown. Not shipped in the game. | `tools/city/truth/`; `transects_meta.json` says how each file was made. |
| **Owner's PSX texture packs** | Every material (owner rule) | The packs' own licences | Materials only from the packs. | `Assets/PSXRacing/Art`. |

### Planned (not used yet): add the row above when a package starts using it

| Source | For | Licence | Duty |
|---|---|---|---|
| USGS 3DEP 1 m / NC Phase 4 QL1 lidar (2016-17) | Ditch templates and spot checks (not shipped) | Public domain | Same credit. |
| NC Spatial Data Download / NC OneMap | Alternative lidar access | Verify the terms first | Record on use. |
| USGS NHDPlus HR | Flow and slope attributes, if ever needed (3DHP replaced it for WP-04b) | Public domain | Credit USGS. |
| Mecklenburg County GIS (TreeCanopy, Streets `SPEEDLIMIT`/`NUMBEROFLANES`/`PAVEMENTWIDTH`, EdgeOfPavement) | WP-08, 20, 25, 32; metrics ACCURACY | CC0 unless a layer says otherwise (see the Creeks and Streams row above; the "MIT" is the county site's software licence) | Check each layer's metadata before shipping it. |
| City of Charlotte open data (signals, street trees, sidewalks, resurfacing `CURB`, storm channels) | WP-16, 20, 22, 25 | CC BY 4.0 | In-game credit "City of Charlotte", a licence link, and a note that the data was modified. |
| USFS / NLCD tree canopy and land cover | WP-08, WP-21 | Public domain | Credit USFS/USGS; verify the canopy year. |
| FHWA NBI | Bridges | Public domain | - |
| NCDOT GIS (outdoor advertising, structures, guardrail, road characteristics, signals, AADT) | Validation and statistics only | No licence stated | Derived placements only with the owner's OK and NCDOT's confirmation. Credit "NCDOT GIS Unit". Strip `Google_Map` / `GOOGLE_LINK` fields from anything kept. |
| FHWA MUTCD; AASHTO | Taper lengths, sign mounting, radii | Formulas only | Nothing copied. |
| npm `geotiff` | Not needed: `fetch_3dep.mjs` reads the COGs itself (TIFF directory, LZW, floating-point predictor) | MIT | - |
| tinf inflate (only if `DeflateStream` fails on WebGL) | WP-13 | zlib | Keep the notice in the source. |

### Retired

| Source | Used for | Until | Notes |
|---|---|---|---|
| **Racing-Game-2 traced water** (owner's own work) | The creeks and Lake Wylie (WATR) and the water spans they caused | WP-04b (2026-09-28) | 30 lines and 2 lakes traced from RG2 `Maps/Rivers and Lake.png`, registered with a 23 m ICP residual; none of its lines passed within 50 m of the five creek beds the terrain audit measures. What the drawing was traced over was never recorded; nothing ships from it now. Kept byte for byte in `tools/city/vendor/rg2/` as the record. |
| **Racing-Game-2 I-485 rows** | Registering that traced water (ICP) | WP-04b | `tools/city/vendor/rg2/i485_fit.json`, kept as the record. |

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
