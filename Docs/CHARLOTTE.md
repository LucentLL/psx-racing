# Charlotte (2026-08-25, rebuilt 2026-09-11)

Asked for: base the game on a real city the way Midnight Club used Atlanta/LA —
a 3D Charlotte "as close to scale as possible while maintaining LoD, draw
distance, and 60-100+ FPS", with two standing rules: **every road crossing
water gets a bridge, and every highway crossing a road goes over it**.

Then, 2026-09-11: "Charlotte should have a complete road system of major
roads. Refer to actual map for lane counts, which roads go over others with
bridges, how highway entrance/exit ramps work. I want the full city built with
skyscrapers and buildings, houses. Not 100% accuracy, but recognizable. The
free roam and race tracks in Charlotte should be on the same map." That pass
replaced the data pipeline end to end; this document describes what stands.

## The source: OpenStreetMap, read raw

The first port read Racing-Game-2's baked rows: whole named roads, dual
carriageways merged into one painted ribbon, lane counts collapsed to a class
index, junctions guessed from geometric crossings. `tools/city/export_osm.mjs`
reads the raw Overpass ways instead (cached under `tools/city/cache/`, all
© OpenStreetMap contributors, ODbL — the attribution is on the HUD's opening
seconds and in every venue blurb). Every source and its licence is in the
registry **`tools/city/SOURCES.md`** (WP-02); its Credits table is the one
place a credit line is written, and the exporter copies those lines into the
data (it refuses to run without them):

- `ways_all.json` — every motorway/trunk/primary/secondary/tertiary way and
  their `_link` ramps inside the I-485 beltway bbox (18,628 ways), with per-node
  ids and every tag: `lanes`, `lanes:forward/backward`, `oneway`, `bridge`,
  `tunnel`, `layer`, `maxspeed`, `junction=roundabout`.
- `streets_core.json` — residential/unclassified/living streets and NAMED
  service roads in an 8 x 8 km core round uptown, so the uptown grid and the
  streetcar suburbs are complete where the player spends most of their time.
- `buildings_core.json` — 35,112 building elements in the same core, with
  `height` / `building:levels` where mapped (every tower in uptown is).
- `nodes_all.json` — signal / stop / yield nodes, matched to graph nodes BY ID.
- **The ground (WP-04):** the USGS 3D Elevation Program's 1/3 arc-second
  bare-earth DEM (about 10 m; public domain, credit "U.S. Geological Survey,
  3D Elevation Program"), cells n35w081, n35w082, n36w081 and n36w082.
  `tools/city/fetch/fetch_3dep.mjs` range-reads only the tiles of those Cloud
  Optimized GeoTIFFs that cover the DEM box (about 0.1 GB of the 1.94 GB; its
  own TIFF directory reader, LZW decoder and floating-point predictor, no npm
  packages) into `tools/city/cache/3dep/box13.f32` (137 MB, gitignored; or
  `%PSX_GIS_DIR%\3dep`), with `box13.json` holding the georeference, the
  source files' Last-Modified/ETag and the sha256. Until WP-04 the ground was
  the AWS Terrain Tiles "skadi" 1" tiles (Tilezen; also bare earth, built from
  USGS NED/3DEP, 1.5 m RMSE against 3DEP), which the mountain stages still
  use, so their credit line stays.
- **The water (WP-04b):** creek centrelines from Mecklenburg County GIS
  "Creeks and Streams" inside the county (CC0, the county's Open Mapping
  licence) and USGS 3D Hydrography Program flowlines outside it (public
  domain), lakes and ponds from the county's "Lakes and Ponds" and 3DHP's
  waterbodies, the county line from Census TIGER.
  `tools/city/fetch/fetch_water.mjs` pages them out of the ArcGIS services,
  sorted by object id, into `tools/city/cache/water/` (gitignored;
  `fetch/water_manifest.json` has each query, count and sha256). They replace
  RG2's hand-traced creeks and Lake Wylie, which were registered onto the OSM
  frame with a 23 m ICP residual and passed within 50 m of none of the five
  surveyed creek beds; that file stays vendored in `tools/city/vendor/rg2/` as
  the record (what `Maps/Rivers and Lake.png` was drawn over was never
  recorded), and nothing ships from it now.

Every input is pinned in `tools/city/cache_manifest.json` (size, sha256, and
the Overpass `timestamp_osm_base`: 2026-09-12T02:44:33Z for the arterials and
controls, 02:39:51Z for the core streets and buildings). The Overpass cache is
gitignored and exists only on the machine that fetched it; the queries that
made it are in `tools/city/fetch/`.

What the exporter makes of it:

- **Real topology.** A junction is a node two ways share, by id. Ways are cut
  at those nodes into 25,042 edges between 20,194 nodes (3,846 km; 179,325
  polyline points). The 25,250 edges and 3,966 km this said before were
  counted with the toll lanes still in. Dead ends within 2.5 m of another
  edge are welded onto it (one, in this snapshot — the arterial and
  minor-street fetches share their nodes).
- **Real carriageways.** A divided road is two one-way edges with the ground
  showing between them. Each carries its own lane count (`lanes`, or
  `lanes:forward + backward`, or a per-class default), and a two-way road with
  an odd count has a centre turn lane (the TWLTL on every undivided Charlotte
  arterial).
- **Real grade separations.** A crossing with no shared node IS a separation
  and OSM says which road is on top: `tunnel < layer < bridge`. 782 of them
  (928 before the toll lanes were dropped), every one decided by a tag; the
  class-rank fallback never fired.
- **Real bridges.** `bridge=yes` marks the exact extent of every deck (53.1 km
  on 791 edges), separately from the 264 water spans found geometrically.
- **Three race routes** as edge chains through this graph, from the way-id
  lists `tools/clt/fetch_clt.mjs` verified on 2026-09-07 (every id still
  present). Uptown Loop 9.20 km (loop, 1.61 km on structure), Tryon Street
  Sprint 6.02 km, Independence Sprint 7.02 km.
- **The ground**: a 30 m grid (1621 x 1831, PDEM v3 in delta-coded 1 km
  blocks since WP-13; see that section) over the whole beltway, each node
  the MEAN of the 3DEP 1/3" pixels whose centres lie in its own cell; until
  WP-13 a 60 m grid (811 x 916), each node the mean of its own 60 m cell,
  with **no filter after it** (WP-04, 2026-09-28). Until then it was the skadi
  tiles run through an opening, a closing and a blur written to take out
  "roofs", on the belief that the source was radar with uptown's towers 200 m
  proud; the source was bare earth, so the filter removed real ridges and
  valleys instead (34% of the core's relief survived; 3.63 m RMSE from 3DEP).
  Now: 0.64 m RMSE city-wide, 77% of the core's relief kept, Trade & Tryon
  232.06 m against 232.59 m (it was 227.38). Heights are stored in decimetres
  above the **datum, pinned at 97.0 m** (`DEM_BASE`, WP-02); it used to be
  `floor(min) - 2`, which would have moved with the new ground and every world
  y with it. The lowest the ground may go is 97.5 m (`DEM_FLOOR`): a cell
  below it is clamped up only inside a named pit box (`DEM_CLAMP`: the
  Pineville and Arrowood quarries, keyed by their OSM ways; 3DEP puts their
  floors at 69-71 m and 89.5 m, so they lose up to ~27 m of depth), and
  anywhere else the export fails, because a u16 below the datum wraps to a
  6.5 km spike. The grid now runs 97.5..293.3 m ASL (it was 99.2..273.9).
- **The water** (`tools/city/lib/water.mjs`, WP-04b): 239 creek lines (719
  km: the county's open channels draining more than a square mile, 3DHP
  channels of Strahler order 4 or more outside), 1,545 RAVINES (1,251 km: the
  smaller streams, county classes draining 100-640 acres and 3DHP order 2-3,
  carved into the ground but dry, with no span), and 106 lakes of 2 ha or
  more that are flat in the hydro-flattened 3DEP (the level is the median of
  their pixels). Each creek and ravine carries its BED: the lowest 3DEP pixel
  within a few metres of the surveyed line every 20 m (40 m for a ravine),
  made to fall downstream. Width is modelled from the stream order (6 m at
  order 4 to 40 m). 560 water spans (264 with RG2's lines): every road over a
  creek is a deck (the owner's rule), a road over a ravine keeps its
  embankment (culverts are WP-25's).
- **31,695 footprints** (styles glass / midrise / brick / house / shops,
  gabled where the polygon is a house's), counter-clockwise, with heights.

Outputs (all in `Assets/PSXRacing/Resources`): `charlotte_city.bytes` (3.05 MB
graph + water + beds + separations + spans + routes; a versioned binary the
runtime reads through a BinaryReader — the JSON equivalent was 9 MB),
`charlotte_dem.bytes` (1.5 MB), `charlotte_bld.bytes` (1.7 MB),
`charlotte_routes.json` (the menu's copy of the routes: lengths, lines, a 20 m
polyline each). 6.30 MB raw, 3.49 MB Brotli (5.79 / 3.04 MB before WP-04: the
water +0.31 MB Brotli, the unfiltered ground +0.13 MB). Debug plots land in
`tools/city/charlotte_*.png`.

**The container (WP-02, 2026-09-28).** `charlotte_city.bytes` is `PSXC`
version 2: a header and a **section table** ({tag, offset, length}), then the
sections META (attribution, uptown), NODE, NAME, EDGE (the edge records and
their point counts), PNTS (every edge's points, in edge order), WATR (each
water's kind since WP-04b: 0 creek, 1 lake, 2 ravine), WBED (WP-04b: each
water's bed, u16 cm above the datum then i16 cm steps, a lake's one value its
level; optional, a file without it still parses), XING, SPAN, ROUT and
**GHSH, the graph hash**. `CityMap.Parse` reads the sections it
knows by tag and skips any other, so a later export can add a section (lanes,
controls, station heights) without breaking an older build. Version 1 was the
same content as one sequential record, with the points inline in EDGE;
moving them to their own section also made the file 88 KB smaller after
Brotli (1.328 -> 1.240 MB), for 246 more raw bytes. `charlotte_dem.bytes` is
`PDEM` version 2: the v1 header plus the scale the u16 heights are multiplied
by (0.1 m), which `CityElevation.BaseY` used to hard-code; it takes its bytes
through `CityElevation.LoadDem(byte[])` and block-copies the grid (critic C46:
the parser and the DEM both take bytes now, so a separately downloaded
Charlotte file only swaps where the bytes come from). The layouts are written
down once, in `tools/city/lib/citydata.mjs`, which reads v1 and v2.

**The graph hash** is a CRC-32 (zlib's) of the edge count and, per edge, its
two node indices and its length in centimetres (summed in double precision
from the stored float points, rounded half up); `CityMap.GraphHashOf` computes
the same number in C#. It does not depend on the layout: the v1 file and its
v2 re-export both hash to **27bccd93**. Data derived per (edge, s) is stamped
with it, and `CityAudit` fails if the file's GHSH and the recomputed hash
disagree (`CityMap.Parse` logs an error too).

The exporter writes nothing unless told where (2026-09-28):

- `node tools/city/export_osm.mjs --check` exports in memory and compares
  byte for byte with the four shipped files. It also checks the inputs
  against `cache_manifest.json` and the result against
  `tools/city/fingerprint.json` (counts that survive a Node change), and
  exits 1 on any difference. From the cache it reproduces the shipped files
  **byte for byte**, on Node 24.15.0, which `tools/city/package.json` pins.
- `--out <dir> [--fingerprint <file>]` writes the four files anywhere.
  Resources is only written when it is named:
  `--out Assets/PSXRacing/Resources --fingerprint tools/city/fingerprint.json`.
- `--manifest` records the inputs again after a re-fetch. It records
  `SOURCES.md` by its Credits lines only, so prose edits to the registry do
  not trip `--check`.
- `node tools/city/determinism.mjs` exports twice, in two separate Node
  processes, into scratch directories (`--no-plots`), and compares every
  file's sha256 (about 10 s). `--check` says the shipped files are the
  export's output; this says the export is a function of its inputs.

**Credits (WP-02, critic C17), per edition.** `tools/city/SOURCES.md`'s
Credits table is also the source of the pause menu's **CREDITS** page (a row
beside the column, under TOGGLE DEBUG INFO; `CreditsPanel` shows the build's
own edition's page - `Resources/psx_credits.txt` for ALL,
`psx_credits_main.txt`, `psx_credits_city.txt` - wrapped to the column), of
the one-line credit on the CITY front page (`CityFrontEnd.Credits`, CITY's row
of `Resources/psx_credits_line.txt`, made from the table's `short` column) and
of **`LICENSES.txt`**, which the WebGL template carries into every build beside
`index.html` (`LICENSES-MAIN.txt` / `LICENSES-CITY.txt` beside it;
`PSXBuildWebGL.PickLicenses` keeps the build's own edition's) and
`build-and-publish.ps1` publishes with it (live: `/city/LICENSES.txt`). A row's
`shipped in` names the editions whose build carries its data (`CITY: ...;
MAIN: ...`), so the CITY page credits OpenStreetMap, USGS 3DEP, Mecklenburg
County GIS and USGS 3DHP and not the stages' terrain, and MAIN the reverse.
`node tools/city/credits.mjs --write` regenerates them all; without `--write`
it checks them; `--build <Build/WebGL>` checks a finished build carries its own
edition's `LICENSES.txt` (the publish runs it before a deploy).
`tools/bench-preview.ps1` photographs the page at the three aspects and logs
any clipped text.

**The other OpenStreetMap layers (WP-02, critic C19).**
`tools/city/fetch/fetch_layers.mjs` fetches, in one step, the layers later
packages read (crossings, turn restrictions, traffic calming, railways, power,
trees, advertising, barriers, culverts), every query pinned to the road
snapshot with `[date:"2026-09-12T02:44:33Z"]`, into
`tools/city/cache/layers/` (gitignored), recorded in
`tools/city/fetch/layers_manifest.json`. Nothing reads them yet.

## Scale: the layout scales, the streets do not

The car is a real-size object, so nothing the car touches can shrink: lane
widths, bridge clearances, building doors are 1:1 at ANY city scale. Layout
metres (node positions, `CityMap.LayoutScale`, the one knob, at 1.0) and
section metres (a lane is 3.6576 m, never scaled) are different currencies.

Widths are DERIVED from `RoadProfiles`, a closed table of twenty rows
(two-way 2/3T/4/5T/6 lanes; one-way street 1-4; ramp 1-3; expressway
carriageway 2-4; motorway carriageway 2-6) each with its shoulders — a
freeway's outside shoulder is 3 m and its inside one 1.2 m, in the direction
of travel. The mesh, the painter and the material table all read that one
row, so the paint and the pavement cannot disagree, and OSM's `lanes` tag
only ever picks a row.

## The inversion: this project's first runtime world

Every circuit is baked whole into a scene by the editor builder; a city that
size cannot be. `Charlotte.unity` is baked nearly EMPTY — lighting, camera+HUD,
the player car, one `CityWorld` — and the world is generated at runtime, per
256 m tile, from the graph. A ring of tiles within ~640 m of the car exists;
everything else is data. One tile build per frame while moving, the tile under
the car force-built synchronously. Tile meshes are TILE-LOCAL so precision
never depends on distance from origin.

Per tile (`CityMeshes.Build`): ground grid graded to the roads, road ribbons
in the profile's painted surface (asphalt or concrete, new or old, hashed from
position), kerb skirts, MERGE GORES where a ramp meets its mainline (the wedge
between the ramp's inner edge and the carriageway's outer edge, filled for as
long as they run within 4.5 m of each other), Jersey barriers along every
grounded freeway and expressway carriageway (their own mesh and collider,
with gaps where the gores attach), junction fan patches, bridge decks with
rails and piers, water, the real footprints (extruded prisms with flat roofs,
crowns on anything over 120 m, gabled houses with a ridge along the lot's
long axis), the procedural frontage boxes outside the footprint data, and the
interior fill — gabled house boxes on a 24 m grid between the arterials,
aligned to the nearest street, thinning with distance from uptown and from
the road.

`GroundHeightAt` cannot come along: the city height field is the DEM plus
corridor pinning against the road edges registered in the queried tile's
spatial-hash cell — same shelf / sink / blend shape as the circuits.

## Elevation: solved from facts

Load order: bytes → graph → **crossing facts** → elevation profiles → tile
index. `CityElevation.Solve`:

1. Every edge follows THE ROADS' GROUND, Gaussian-smoothed over 25 m,
   grade-limited (4% freeways, 5% expressways, 6.5% streets, 8% ramps and
   local streets). A tagged bridge is structure end to end and holds at least
   the line between its ends. Since WP-04 the roads' ground is the DEM through
   a 0.8-cell (48 m) Gaussian (`RoadDemSigmaDefault`; the land itself reads
   the raw grid): on the unfiltered 3DEP ground two carriageways 20-40 m apart,
   or a ramp and the road it runs inside, read the grid's slope at their own
   centrelines and came out up to 2.5 m apart. A carriageway of a divided road
   (one-way, not a ramp, named) reads it at the MIDLINE between itself and its
   nearest opposite carriageway of the same name within 45 m
   (`PairedRoadBaseY`), so both climb the same hill. WP-06 replaces all of
   this with measured road profiles.
2. **Trenches.** A freeway mainline crossed by a surface street is DUG under
   it (5 m + deck, 4.5% approaches). Charlotte's inner freeways (the Belk,
   Brookshire) run in cuts under streets that stay at grade; raising every
   cross street onto an embankment would hump the whole uptown grid. THE CUT
   RUNS THROUGH THE INTERCHANGES: the trough is carried through the freeway's
   own nodes into the next mainline edge, those nodes are pinned to the cut
   (the one place the max-of-ends rule yields), and the ramps meeting them
   are cut down along an 8% cone of their own — 230 crossings solve this way
   (CityAudit counts 230 on the shipped graph; this said 285 before).
   Freeway over freeway, ramp over anything, or a mainline carrying a water
   span keeps the hump rule. The street's OSM bridge tag makes it a deck; a
   bare `layer=1` gets its stations over the cut marked structure by the
   trench pass, or it would pin the ground up under itself and float.
3. Raises: every separation lifts its OVER edge clear of the under road by
   5 m + deck at 4.5% approaches; humps that overlap merge into viaducts.
   Water spans hold their line, and since WP-04b are lifted (never lowered)
   until their soffit clears the water under them by 1 m, with 4.5%
   approaches: the water is at its real level now, and a road the 60 m grid
   averages down into a valley would otherwise have the creek over its deck. Junction nodes take their highest incident
   end. Iterated to a fixed point, then the interiors relax (never steeper
   than an edge's own ends), then fresh raises alternate with APPROACH CONES:
   every node standing above the ground seeds a 4.5% cone through the graph
   (RaiseCone), on through any junction it still stands above, so a lifted
   bridge's embankment runs back through the blocks behind it instead of
   ending in a 37% ramp on a 36 m approach edge. Nothing that can lower a
   road runs after the relax.
4. Structure = tagged bridge, water span, trench-crossing stations, or more
   than 1.4 m above the DEM.

Ramps seated beside their hosts (the 2026-09-12 pass below) gained one rule
with the real ground: a short lane seated on one road at its a end and
another at its b end stepped from one host's height to the other's between
two stations, 16-19% on three slip lanes whose two roads now stand 1.3-1.8 m
apart. `SplitTwoHosts` frees stations at the meeting point, each time the
hosts move, while the step is steeper than 13%, until 10%, and the lane
climbs between them (10 stations city-wide).

The ground under the water (`CityElevation.Ground`, WP-04b): each creek's and
ravine's line is carved to its stored bed - a creek's floor 0.9 m under the
bed (the water 0.1 m under it), flat for max(6 m, half its width + 3 m) each
side, then banks at 1V:2H up to the land; a ravine 0.4 m under its bed, 3 m
each side - as a MIN with the grid, so a valley the grid already shows is not
dug twice. The road corridors then grade the land to the roads as before (a
creek within a road's 11.5 m flat verge is under it until WP-14). A creek's
water is a sheet at bed - 0.1 m in 8 m pieces that follow the bed, from bank
to bank; a piece over a grounded road's pavement lower than the water is left
out, so water never stands on a road. A lake is one flat surface 0.3 m under
its level in the hydro-flattened 3DEP.

## Buildings

Inside `CityMap.footprintBounds` (the 8 x 8 km core) the real footprints are
the buildings; the procedural frontage pass stands down there, and the
restaurants only take a lot no footprint is on. About half of the square-ish
30-135 m footprints wear one of the owner's skyscraper-pack models scaled onto
the lot (never more than 40% either way); the rest are extruded with a drawn
curtain-wall texture (the pack has no glass), the photographed brick office
and mid-rise facades, or the drawn siding-and-window for houses, with the
pack's roof tiles on every gable. Retail footprints put the shopfront atlas on
the wall that faces the nearest street.

Outside the core: the frontage boxes and prefab lots as before (houses,
trailers, the pizzeria pack's blocks, the two restaurants), plus the interior
fill, so a subdivision reads as a subdivision across the fog.

## Races on the same map

A venue with `TrackDef.cityRoute` set is a Charlotte scene like the free-roam
one plus three AI cars, an empty `TrackPath`, a `RaceManager` and the handoff
applier. At load `CityMode.Awake` resamples the route's edge chain at 4 m with
the city's solved heights into the path, stands the grid on it (AI rows first,
player at the back, measured back from the line), registers the AI as
streaming anchors so the world exists under them, and steps aside —
`CityMode.Instance` stays null, so DriveSession, the HUD and the pause menu
see a RaceManager race exactly as on a circuit. A loop route's waypoint 0 is
its start line; a route with ends keeps a 60 m lead-in and a shutdown, exactly
as a stage bake did, so the reverse twin (Tryon II) and the finish remap need
no new rules. `charlotte_routes.json` gives the menu the length, the line and
a map polyline without parsing the graph. `IsRoam` (city with no route) is
what every picker skips; `IsCityRace` is a venue.

The three CLT stage bakes (`Resources/clt_*.json`, `Art/CLT`, `tools/clt`)
are retired.

## Verification

- `tools/city-cycle.ps1`: code + data into the warm sandbox, then
  `CityAudit` and `CityPreview`, no scene build (~4 min).
- `CityAudit` (menu: PSX Racing/Audit City): graph counts, DEM loaded,
  separations decided by tags, trenches present, connectivity from spawn,
  every enforced separation clears, every tagged bridge on structure, every
  water span decked, grades under 16%, every route chains and closes and
  builds a path of the right length, tile determinism, real footprints in
  the uptown tile, barriers on a freeway tile, a gore at a ramp merge, houses
  in a suburb tile, and ZERO walls facing inward — the emitter checks each
  wall's normal against the outward direction it was given.
- `CityPreview` (PSX Racing/Preview Charlotte): uptown, a freeway viaduct, a
  trench, a water bridge, a gore, I-485, the prefab suburb, a footprint
  neighbourhood, a filled block, the restaurants, and the skyline from 600 m
  south — top-down and at street level, to `Screenshots/City`; plus the
  drive-along views (top, ahead, back, high) of the West 5th bridge, every
  freeway-to-freeway interchange, I-77 north of uptown and an overpass.
- `tools/city-play-check.ps1` (PSX Racing/Check City Spawns): PLAYS free
  roam and the 277 race headless and checks the car poses after physics —
  the two spawn bugs of 2026-09-12 were invisible to everything above.
- `tools/city-ground-probe.ps1 -At "x,z"`: which corridors set the ground
  height round a point, for chasing a GRASS note from the drive audit.
- `tools/city-roadside-probe.ps1 -Spots "edge:s:side;..."` (WP-04): stands
  up the tiles round a roadside audit note (LEDGE, LIP, FACE, OPEN) as the
  audit does and prints the first surface a walk out from the lane edge meets
  every 5 cm, the ground function's terms, and the roads round it.
  `-Lanes "edge:s:lane;fan:node:edge:inset:lat"` asks a LANE or FAN note's
  question instead: what stands in the lane's column, and every rail within
  4 m by its owner (edge side and span, fan chord or gore nose); `-ArmLog
  "edge,..."` adds how `RailOverArms` read those edges' rails, piece by piece.
- The live URL is the real test.

### The refinement's instruments (WP-01, 2026-09-28)

Built so every package of the Charlotte refinement is measured the same way
before and after. The baselines are in `tools/city/baseline/`.

- **`node tools/city/export_osm.mjs --check`**: the export reproduces the
  shipped files (see above).
- **`node tools/city/metrics.mjs`** (about 21 s, no Unity). It measures the
  shipped files, or an `--out` export with `--data <dir>`.
  - `--json` writes every number.
  - `--compare tools/city/baseline/metrics_baseline.json` prints what moved.
  - Sections:
    - GRAPH;
    - KINKS: free-vertex turns per km by class, folds, split nodes;
    - LANES: painted centre turn lanes against the tags, uneven splits;
    - HEIGHT: against USGS 3DEP in `tools/city/truth/`;
    - CONTROL: signals, stops and give-ways by junction class;
    - SIZE: raw and Brotli, per file and per section;
    - ACCURACY: not measured yet, because the county pavement layers are
      not fetched.
  - Baseline: 7.48 kinks of 5° or more per km, and 1,531 inner-edge folds
    over 10 cm. The centre turn lane is painted on 154.8 km but tagged on
    only 23.3 km. The city DEM is 3.63 m RMSE from 3DEP. The core keeps 34%
    of its relief. The 8 transects keep 13 of 91 real crests and dips.
- **`tools/city/truth/`** freezes the flatness survey's 3DEP reference into
  2 MB: the 8 transects at 2 m with masks, the land at ±30 and ±60 m, the
  core at 10 m and the city at 120 m. 3DEP is public domain.
- **`py tools/size-ledger.py`** reports:
  - the files of a WebGL build, failing over 95 MiB;
  - the Brotli size of each Charlotte data file;
  - the Build Report split from the Unity log;
  - every committed version of the data in git.
- **`CityBudgetProbe`** runs at the end of `CityAudit` and writes
  `city_budget.txt`.
  - It builds the 5x5 ring through `CityWorld.EnsureTile` at 9 fixed sites.
  - It times each tile: mesh build, stand-up, MeshCollider cook and props.
  - From a 1.2 m eye it counts, four ways at 58° out to 500 m, the in-view
    draw proxy, the sun-map casters, the vertices, triangles and colliders.
  - It times Parse and Solve and weighs the heap the map holds.
  - These are editor numbers; a phone is several times slower.
- **The FPS overlay's CITY line** (SHOW FPS on, in Charlotte) shows:
  - the live tiles and colliders;
  - the slowest tile build of the last 10 s;
  - the draw and SetPass counts where the player reports them.

  The owner reads it on the phone at the test page. It decides WP-09: over
  16.7 ms there, time-slice the tile build.
- **`tools/city-refspots.ps1 -Label <name> [-Before <dir>]`**
  (`CityRefSpots`) shoots fixed driver-eye views and makes contact sheets,
  before and after side by side. The views are:
  - the 21 Street View points (A1-A15);
  - the 24 kink spots (the verdicts plus the census's worst 20), each also
    from above;
  - the 11 named crests and dips.
- **TerrainFidelity** (WP-04, in `CityAudit`): the creek bed at the seven
  creek transects of `tools/city/truth/creek_transects.json` (3DEP 1 m;
  `make_creek_truth.mjs`), judged on the carved terrain within 1 m and the
  graded ground reported beside it; the sharpest crest on each route off
  structure (the crests2 method: a 16 m Gaussian, no crest under R 400 m,
  critic C23); the land beside the routes at 30/60/100 m; stations made
  structure by the 3.5 m last resort and cut-wall metres against their
  pre-WP-04 values (+25% / +20% at most); every water span's soffit above its
  water. `metrics.mjs` measures the creek beds offline the same way (HEIGHT,
  "creek beds").

## WP-04 and WP-04b (release R1, 2026-09-28): the hills and the creeks

**What changed.** The ground is 3DEP's 1/3" bare earth averaged over each
60 m cell with no filter after it, and the water is the county's and USGS's
surveyed creeks and lakes with beds sampled from 3DEP (the sources and the
rules are under "The source" above; the solve and the ground under
"Elevation"). Same grid (811 x 916), same format for the DEM, the datum
still 97.0 m.

**Measured before -> after** (`metrics.mjs`, `CityAudit`; before = the data
and code at 2565d60):

| Measure | Before | After | Plan target |
|---|---|---|---|
| DEM RMSE against 3DEP (city, 120 m) | 3.63 m | 0.64 m | <= 1.0 m |
| Core relief kept (8 x 8 km, 10 m truth) | 0.34 | 0.77 | >= 0.70 |
| Grid slope p90 at 60 m | 4.49% | 10.3% | >= 8% |
| Cells steeper than 6% | 2.8% | 34.3% | >= 15% |
| Trade & Tryon | 227.38 m (-5.21) | 232.06 m (-0.53) | |
| Transects: crests and dips kept (DEM on the road line) | 13 of 91 | 58 of 91 | |
| Creek beds within 1 m of 3DEP (7 transects, carved terrain) | 0 of 7 (+3.5..+6.8 m) | 7 of 7 (-0.85..+0.25 m) | <= 1.0 m |
| Sharpest route crest off structure (16 m Gaussian) | R 487 m | R 490 m | >= R 400 m |
| Stations made structure by the 3.5 m margin | 1,874 | 1,494 | <= +25% |
| Cut walls on the 118 roadside tiles | 1.34 km | 1.07 km | <= +20% |
| Freeway trenches under streets | 230 | 212 | |
| Water spans | 264 | 560 | > 200 |
| Worst grade (not a sliver) | 15.2% | 13.0% | <= 16% |
| Data, Brotli | 3.04 MB | 3.49 MB | DEM +0.15, water +0.1-0.3 |

The land beside the routes stands further from the road now (|land - road|
p90 at 30 m: 0.76 -> 1.69 m on the transects), which is still the 11.5 m flat
verge and the corridor blend; WP-14 regrades the roadside.

**What the real ground broke, and the fixes.** Adjacent roads stand up to a
metre or more apart where the filtered grid had them level:

- divided carriageways read their own side of a hillside: they read the
  midline now (`PairedRoadBaseY`), and the roads read the grid through a
  0.8-cell Gaussian;
- a slip lane seated on two hosts stepped between them: `SplitTwoHosts`;
- a squeezed neighbour higher than the edge left a slot at its retaining
  face's foot (0.4 m beside a Tyvola ramp, 3.7 m between I-77's viaduct
  carriageways): the lower edge gets a rail over the strip;
- a squeeze step grew past a ledge between two sections: judged at the
  unsqueezed end too;
- water spans whose road the grid averages into a valley had the creek
  over the deck: lifted to clear it by 1 m (12 spans, up to 3.1 m).

Six roadside spots are left for WP-14 and named in `CityAudit`
(`KnownRoadsideSpots`): two 1 m open deck edges at gore gaps (I-277 e1237,
the Tyvola ramp e2735), a steep verge in a 2.3 m cut on a ramp off I-277
(e1489), a 5 cm lip between two touching roads (e9314), and two ledges
(North Caldwell Street at East 12th, the ramp e2858 off the Independence
Expressway). A failure anywhere else still fails the audit.

**Rails in lanes (the review of R1, 2026-09-29).** The first of the fixes
above (a rail at a squeezed neighbour's face) put rails into lanes. The lane
survey's solids within 2 m over a lane (a road's own rail in brackets) went
46 (26) before WP-04 -> 49 (15) on the new ground -> 67 (30) after that fix,
three on the Uptown Loop, and the fan mouth probe found a 1.2 m deck rail
across North Kings Drive's mouth at node 6995 (0 -> 2 probes). Neither was a
check, so CITY AUDIT stayed OK. The builder now:

- lays a rail's overhang over no squeezed neighbour's drawn pavement (the
  chords between their cross-sections), nor over any road's within a car's
  height below it (Albemarle Road's deck rail stood over the Independence
  Expressway's right lane on the Independence route) (`OverhangBeside`);
- squeezes a section past the outside of a bend against the vertex
  (`Squeeze`);
- cuts a rail at a junction, in half-metre pieces, where its back is to
  another arm's or the fan's pavement at its height, or where it stands on
  that pavement (`RailOverArms`): Elizabeth Avenue's deck at Kings Drive,
  Tyvola Road's host rails across its ramps, North Davidson Street at node
  13275.

After: 25 probes (12) in 22 runs, the fan mouths 0. Two checks are new:
nothing solid at a junction fan's lane mouths, and nothing solid over a lane
but the named spots (`CityAudit.KnownLaneSolids`, 19 spots for the 22 runs).
Of the 22, 8 were in the pre-WP-04 audit, 4 are on the Tyvola Road tile the
audit probes since WP-04 moved its twelve most elevated tiles, and 10 are new
with WP-04's ground: nine a rail's face within 10 cm of the lane line (a rail
on its own edge 0.45-0.65 m in from the lane extent), one East 12th Street's
approach rail over the link e11144, the two 0.9 m apart in height. On the race
routes: Uptown Loop 3 (I-277 e2308 and e2321, both pre-WP-04; I-77 e1891, a
face beside the lane line, new), Tryon 0, Independence 0. A new solid anywhere
on the probed tiles fails the audit; WP-14 takes the named ones.

Measured with it: `race-play-check` (330 s, seed 0) retires 1, 3 and 1 rivals
on Uptown, Tryon and Independence (5; 7 before WP-04, 6 at R1 as first
committed), none into a barrier (1 before WP-04, 2 at R1); `city-play-check`
CITY SPAWNS OK. The tile build costs the same: `CityBudgetProbe` alone,
interleaved with e565c40's builder in the same sandbox, p95 68.8 and 70.9 ms
against 70.3 and 65.7 ms (p50 21.6/24.0 against 22.7/24.1); the probe's
readings swing more than that between identical runs.

**Land in lanes (the second review of R1, 2026-09-29).** The lane survey
counted land as the first thing over a lane without failing on it, and from
the WIP commit on it listed a probe 0.55 m inside the ramp e280's merge lane,
30 m before it joins I-77 on the Uptown Loop's tile, meeting land 1.75 m down
(+0.01 m before WP-04). It was a crack under 5 cm wide along the seam where
the clipped ramp meets I-77, both on structure: a gap side on the ground lays
a flush strip under its seam, a deck span returned before it, and the land
under the approach that used to stand at the deck is 1.75 m down on 3DEP. A
wheel probe through the crack meets that drop. The builder now:

- floors a gap side's seam on a deck too, with a strip 0.3 m wide held under
  the pavement at its own level (`EmitSeam`, `SeamCeiling`; held under the
  lowest pavement it crossed, a first try dropped seams 2.7 m onto the lanes
  of I-77 and I-277 under five bridges);
- keeps a connector's tuck under the pavement that stopped it where the tuck
  lies (`Connect`), and halves a strip quad whose tuck would stand on that
  pavement between its cross-sections (`LayTucked`): Armory Drive's
  connector stood as a grass wedge from +0.04 m over Sam Ryburn Walk's lane
  line to +0.29 m at its edge, where the branch's clipped end climbs 0.3 m to
  Armory's height within a metre. (Halving every quad whose tucks differed
  in height put ridges between neighbouring connectors at 20 junction
  corners; gated on the pavement, none.)

A new check fails on land more than 0.12 m over a lane, or a hole to land
that far under one, anywhere on the probed tiles, unless it is a named spot
(`CityAudit.LaneStepM`, the drive audit's step; `KnownLaneLand`, empty). The
new audit on f957b12's builder fails on e280 alone; on this one it passes.
Nine land runs under 0.12 m are still counted, each with the road under it:
floors seen through cracks at gap spans with no road under them (e280 +0.01,
e230 -0.02 and e8170 -0.01, seam strips; e9770 -0.06), verges flush in a
paving gap (Baxter Street +0.00; East 10th Street +0.06, a gore nose verge
5 cm over the road), Arlington Avenue's right lane line (-0.08, its own verge
outside the chord its ribbon is drawn on), and two left for WP-14's grading:
East 13th Street (e10973) +0.118 m, North College Street's verge 4 cm over
its mouth at node 8984 (+0.04 before WP-04), and Arlington Avenue (e21874)
+0.11 m, its own verge from before a bend over the left lane line past it,
where the ribbon's chord runs inside the lane extent (+0.06 before). `tools/city-roadside-probe.ps1` names the strip
that laid any land it meets and maps the surface round a point ("grid:").

Measured with it: CITY AUDIT OK, every line of the audit the same as before
this fix but the land runs; solids over lanes unchanged (25 probes, 22 runs,
all named). `city-play-check` CITY SPAWNS OK; `race-play-check` (330 s, seed
0) finishes all three races and retires 1, 3 and 0 rivals on Uptown, Tryon
and Independence (4; 5 after the rails fix, 7 before WP-04). Uptown's one is a
planned driver error (MISTAKE at wp 1236, 158 km/h) that carried the Charger
10 m off its line into a barrier at wp 1295, where R1 as first committed lost
the Skyline the same way; Tryon's three are traffic and a lamp post, as before
WP-04. `CityBudgetProbe` interleaved with f957b12's builder: p95 68.4 and 68.0 ms
against 72.0 and 65.6 ms, worst view 200 draws both; uptown's 25 tiles carry
273k triangles against 271k. `verify.ps1 -NoMirror`: VERIFY PASS.

**Irwin Creek** runs 14 m off I-77's pavement north of uptown: the carved
terrain has its bed (-0.85 m), but the graded ground there is +1.95 m, held
up by I-77's flat verge. Creeks beside roads are WP-14's too.

**Play.** `city-play-check` seats free roam and the 277 grid as before. The
three city races (race-play-check, 330 s, seed 0) all finish, before and
after; rivals retired before -> after: Uptown 1 -> 2, Tryon 3 -> 3,
Independence 3 -> 1 (the planned driver error and traffic make single runs
noisy, critic C18). The route profiles' sharp 4 m kinks (the approach
cones' toes, not the ground) are the same count before and after (Uptown
127 -> 120, Tryon 17 -> 18, Independence 14 -> 12 under R 250 m).

**The WebGL build** (G-web, a local build served statically): FREE ROAM
CHARLOTTE loads with "[City] parsed in 110 ms, elevation solved in 386 ms"
and no console errors. WebGL.data is 81.40 MiB (80.98 before; the 95 MiB
gate is far off). The CREDITS page now carries five sources and lays them
out in two columns on a phone.

**Where the hills and the creeks show (2026-09-29, for the owner's first
drive).** Every water span was ranked by how deep a valley its road drops
into (the lower of the two rims within 800 m along the same street, minus
the creek's bed), and the best near uptown were walked on the SOLVED road
(`Edge.YAt`) and shot at the driver's eye and from above in the city
sandbox:

| Spot | Road climbs out of the creek, each side (within 800 m) | Steepest 50 m | Water in view |
|---|---|---|---|
| West Trade Street at Irwin Creek, 1.4 km west of Trade & Tryon (down from uptown, up to Beatties Ford Road) | 24.4 / 27.0 m | 5.2% | beside West 5th Street, one block north (21.4 / 25.3 m, 8.0%) |
| State Street and Rozzelles Ferry Road at Stewart Creek, ~3 km NW | 26.9 / 24.5 m (State); 25.9 / 20.1 m (Rozzelles) | 6.2% | the widest creek near uptown |
| Archdale Drive at Little Sugar Creek, ~9 km S | 20.5 / 26.3 m, a second dip beyond | 7.3% | yes |

On a race route, the Independence Sprint falls 30.9 m in 1.44 km from the
Pecan Avenue crest to the Briar Creek Road dip, with Briar Creek beside the
expressway. At the driver's eye these read as gentle: the grades are
Charlotte's real 3-5%, and the land beside the road is still the 11.5 m flat
verge (WP-14). The raised views show the valleys and the water best. The
same shots caught painted lines that look kinked on East 4th Street at
Little Sugar Creek, Rozzelles Ferry Road and Archdale Drive (for the
smooth-lines gate).

### Smoothness gate (WP-G, 2026-09-28)

The owner's rule: "I circled some concerning squiggly road lines. This should
never happen. Nor should any sharp angles of road or road lines." Painted
lines and road edges never wobble and never kink. The gate proves it on what
the renderer draws, not on the data (the circled creek deck is dead straight
in OSM).

- **What it measures.** Every painted line is found per TRIANGLE of every
  ribbon quad, as the iso-line U = u of its texture run (U is affine inside a
  triangle, so a bend at a quad diagonal is sampled at its peak, never between
  samples). The runs (u, width, colour, dash band) are read from the road PNGs,
  not from the painter's code. Both ribbon edges, the ribbon midline and (mesh
  gate) fan perimeters are measured too. Lines are chained through mitred
  nodes, tile seams and BEND FANS (a 2-arm node past `ContinueCos` drawn as a
  junction slab; plan A2: never legitimate): across one, a ribbon edge runs on
  along the slab's own perimeter (offline: straight from mouth to mouth), the
  midline straight from mouth to mouth, so B2/B3 judge the corner the slab
  draws, and a line ending at its mouths is no legitimate end (C2). Two line
  ends within V across a joint or seam are one line: the shape checks run on
  the joined polyline with the (invisible) step taken out, so a kink at the
  node is still judged; past V the step is a JUMP. The reference is a
  PaintPlan built from data only: the graph polyline, RoadProfiles' layout
  rules and the Trims taper table; never the builder's sections, U or quads.
  - A (position): A0 TEXTURE (every painted run within half a texel of its
    plan line, every plan line painted), A1 OFF against the plan, A2 SKEW of
    the centre pair on the drawn ribbon, A3 INSET of an edge line (against the
    plan's inset), A4 LINEWIDTH, A5 STRAY, A5b MISSING (report-only). A
    SQUEEZED ribbon edge is no longer exempt from A1 (the spec's X6 still
    exempts A2 and A3 there): it is judged against where plan I7 puts it, "the
    cut needed, max-filtered over the taper floor and eased like a taper". The
    cut (how far the drawn edge sits inside the design edge) is split at its
    turning points into rises and falls; EASE: inside a rise or fall, two
    samples w apart (w under the floor L) may differ by at most H_L·g(w/L),
    g(x) = 1.5x - 0.5x³, the most a smoothstep of height H_L over L changes
    over any w, where H_L is the LOCAL height - the change across the
    floor-length window centred on the pair (never more than the whole rise):
    review 4 found that with the whole rise's height a fast step hidden inside
    a taller, slow rise passed (1 m in 8.9 m after a slow 1 m ramp over 80 m
    read 0; it now reads 32.7 cm, as it does alone); HOLD: a dip between two
    cuts narrower than L, where the edge comes back out, reads the lower cut
    less the cut. The floor is `SmoothRules.TaperFloor` (the plan's proposed
    street 15 m, arterial and ramp 30 m, freeway 90 m). A squeeze eased over
    its floor or longer, several stacked, and held between cuts reads 0; one
    stepping in 1 m over 8 m (review 3's case) reads 39.5 cm. Per edge, not
    yet per chain. The mesh gate's tap flags an edge SQUEEZED from its cause -
    `SqueezeSection` moved it in against a parallel neighbour (`Section.sqL /
    sqR`) - never from where the drawn edge stands (review 5: it flagged any
    edge inside its half width, so a builder regression that pulled an edge in
    was judged only against its own envelope and skipped A2/A3, while the
    offline replica, which flags the squeeze only when it fired, failed it);
    an edge in for any other cause is judged against the design edge.
  - B (shape): B1 JITTER (circle fit over ±2 m), B2 KINK, B3 CURVE (radius
    under the class minimum), B4 JUMP where a line continues (its partner
    across a node is the nearest line of its colour and pattern within one
    lane), B4s SEAM at a tile seam. B2 is the facet sagitta min(c-, c+, 10
    m)·turn/8 at every vertex (a lone kink may turn 1.15°), the chords running
    to where the line has turned again by a quarter as much (one vertex, or
    several adding up: the bisector pinch and diagonal crossings never shorten
    a chord, a curve that keeps turning does). A corner SPLIT over close
    vertices (3° as 1.5 + 1.5 a metre apart; Lower Rocky River Road's 15.4° as
    7.7 + 7.7 0.91 m apart) is judged as ONE corner too: its virtual corner
    Vc, summed turn, chords from Vc, and its own rounding dr (Vc to the drawn
    polygon); the chase view out to `KinkViewM` (40 m, where a pixel is 16.7
    cm) shows a kink exactly when the gap f - dr passes max(V, dr) with dr
    under that pixel. A sampled arc, a WP-11 fillet or a bend spread over 5 m
    or more never reads as a corner; B3 judges those. A HEDGED corner - a turn
    and a turn back of a quarter of it or more within a metre or two, [6, -2.4]
    degrees a metre apart - stopped every chord at the counter-turn, so its
    parts scored centimetres and nothing was reported; the painted lines do it
    at nearly every section kink (the diagonal crossing lands a few cm from the
    section and turns back). Every window of turns of both signs within 10 m is
    now judged by its NET turn too: rounded at most as the net turn split in
    two at the window's ends, dr = (w/2)·tan(T/2), with chords from its middle
    (a lower bound, anywhere). A window BETWEEN TWO STRAIGHTS is judged three
    more ways (review 4: where the net turn was 0, or the window wider than 2.5
    m, nothing measured the shape). A straight, for min(4w, 10 m) on each side:
    the line does not turn again by a quarter of the window's largest heading
    (round 3's rule, judged from the drawn segment into the window); OR it runs
    within V of ONE straight line and turns over that stretch by no more than
    the lone limit (review 5: a legal 0.57° vertex 9.4 m before North Irwin
    Avenue's 11.5 cm jog switched every rule off; a single bend of up to 1.15°
    at the middle of a 10 m stretch is still straight), judged from that line
    - the strip's centre - less its own spread, so a zigzag of under V about a
    straight is judged against its mean, not against one of its legs; OR it
    runs on to the strand's END, whatever its length (review 5: Baxter
    Street's jog, 0.79 m before the fan mouth that trims its ribbon, passed
    both gates). A sampled curve turns too much to be a straight, so an S-bend
    of sampled arcs is never between two:
    - its NET CORNER at the drawn virtual corner, rounded by the drawn polygon,
      SIGNED: a drawing that passes outside the corner (a NOTCH, [-1.8, 6,
      -1.8] degrees 1.5 m apart) is rounded by minus its overshoot, so it
      scores the net corner plus the overshoot (4.0x, the lone net corner
      2.1x), never a rounded corner's credit;
    - OUTSIDE: how far any drawn vertex stands outside the region every smooth
      convex transition between the two straights occupies (the triangle to
      their meeting point, or for a step the hull of the two ends and their
      feet on the other line) - a BUMP off a straight and back ([2, -4, 2]
      degrees 2 m apart: 7.0 cm, 2.8x), a notch, a zigzag between straights;
    - its JOG: the least gap d between the approach and exit lines, a sideways
      step. Drawn over a quarter of the chord cap (2.5 m) or less it scores
      the whole step, as round 3 did (review 5: the ease applied there too
      forgave 6-25% of a step, and the re-record dropped 763 of the city's
      keys - they are back); drawn over more, the part d·(1 - g(w/15 m)) that
      came faster than the plan's fastest ease (the squeeze envelope's
      smoothstep over the shortest TaperFloor): [+3, -3] degrees 0.6 m apart
      (K7, 3.1 cm) 1.26x, a 2.6 cm step over 0.6 m 1.05x, an 18.2 cm step over
      3.2 m (South Old Statesville Road) 5.0x, a 3 cm drift over 10 m nothing.
      B4 fails the same step past V at a node; at 6 m, 0.6 m of road is 7 of
      the 240 rows.
    WIDER WINDOWS, up to `KinkViewM` (review 5: a 14 cm bump on 4 m rises
    passed once its top made it wider than the 10 m cap; East 12th Street's
    notch, a 28.7 cm dip before a 1.9° corner over 10.6 m, passed): from a
    feature's first real turn to its last (no vertex within 10 m either side
    turning a quarter of its largest turn - a window ending inside an eased
    transition's tail is not the feature), between straights of a whole 10 m
    (within V of one line), the signed net corner as above and OUTSIDE by the
    part of the excursion that came faster than the plan's ease (the squeeze
    envelope's EASE on the outside distance): the 14 cm bump 3.4x, a 20 cm
    raised-cosine bump over 16 m 2.9x, East 12th's notch 2.1x; an eased bump,
    the plan's own out-and-back over two floors, reads 0. No jog there: a
    wide S-bend between straights is a road's reverse curve, B3's.
    A ZIGZAG PEAK - a vertex whose chords stop at a turn BACK on both sides -
    scores twice its facet sagitta, c·turn/4: the eye's line is the zigzag's
    mean, not an arc through the neighbours (the spec's section 2, "symmetric
    zigzag: peak deviation <= V"; ±3° every 3 m, ±3.9 cm, now 1.7x). A WAVE
    (review 5: a sampled sinusoid of ±8 cm every 10 m, a zigzag whose peaks
    are split in two or flat, a data zigzag WP-11 filleted into tangent arcs -
    no sharp peak, no straights, no cluster - passed everything): the line's
    LOBES are its runs of turns of one sign over its significant vertices
    (turning a quarter of the largest turn within 5 m: a diagonal crossing is
    no lobe); a lobe the line leaves and enters by turning back within 10 m
    (as the zigzag peak's rule has it), next to another such lobe, stands off
    the wave's MEAN LINE - the chord of its two inflections - and that
    deviation, over at most 10 m, is judged at V: the ±8 cm wave 3.8x, ±20 cm
    every 24 m 6.1x, a split-peak zigzag of 7.9 cm 2.0x, the filleted ±10°
    zigzag 3.9x, the spec's own ±3 cm every 10 m 1.3x; a ±2 cm wave, a ±10 cm meander every 80 m, a single S-bend
    and a lone bump (OUTSIDE's) do not. A lobe standing `SimplifyEpsM` (0.5 m,
    the plan's WP-10 Douglas-Peucker tolerance) or more off its mean line is
    geometry, not a wobble: WP-10 removes every sideways excursion under it
    before WP-11 fillets what is left, so a winding road's lobes pass and B3
    judges their radius (on an edge or paint line that test is the data
    line's, so a lobe that close to the limit is left to the centreline).
    A HOOK (review 6: the lone rule's chord toward a strand end is the stub d,
    so it scored about the end's offset / 8, and a corner in a line's last
    metres passed until its end stood 8V, 20 cm, off - Westinghouse Boulevard's
    -6° 1.73 m before its mouth, 18.1 cm off): a lone vertex whose chord runs
    on to the strand's end inside the 10 m cap takes the stub as the eye's
    exit straight and scores the corner extrapolated from the approach, never
    more than the end's own offset, min(min(c, 10 m)·|t|/8, d·sin|t|). A 6°
    hook 1 m before a dead end reads 4.2x (10.5 cm), a 13° one 0.84 m before
    it 10.2x on the ribbon edges (the paint stops short of it), a 6° one 1.2 m
    before a T junction's fan mouth 2.7-4.4x on all 7 lines; a 2° turn 0.1 m
    before the end (0.35 cm off) and a legal 1° vertex 3 m before it pass.
    THE ARC'S FRAME (review 6: every rule that measures an excursion needed a
    STRAIGHT reference - the lone chord stopped where the arc itself had
    turned a quarter of the kink, OUTSIDE, JOG and the net corner needed
    straights, the wave lobes needed turns of both signs - so on an arc
    tighter than about R 500 a 20 cm bump over 8 m, a 20 cm jog over 4 m, a
    3.5° kink against the curve, a ±9 cm wave, the owner's squeeze zigzag and
    a WP-11-filleted data spike all passed): every rule runs a second time on
    the line UNROLLED along the curvature of the constant-curvature reference
    it runs on, and a vertex scores the worse reading ("..., on a curve
    (judged in the frame of its arc)"). The reference: at probes 2.5 m apart,
    the least-squares circle of the vertices that sample the 20 m window about
    the probe (40 m for a coarsely drawn curve; three or more vertices, no gap
    over half the half window, so a lone or split corner among straights is
    no curve), every one within V/4 of it, a curvature under 2V/(10 m)² (R
    2000) being a straight's; clean probes side by side on curvatures that far
    apart hold a step, a clean island under 10 m is a smooth feature's top,
    and each clean curvature is its run's median (those whose windows reach a
    feature's tail left out); a stretch without one (a feature) takes the
    curvature both its clean sides agree on, or the one a strand end leaves
    while the line keeps within 4 lone limits of its heading (that end then
    OPEN: no hook, no end-length straight there), else none - a curvature step
    (a fillet's tangent point, an S-bend's inflection) has no frame. The
    frame's points drop the curve's chord points (within V/50 of the chord:
    diagonal crossings, tile cuts, which the frame would show as a sawtooth of
    the chord's sagitta) and sample a straight piece inside a curve; in the
    frame a vertex or cluster still curving the same way on both sides scores
    nothing as a lone corner or cluster (its facets are the line's own, judged
    as drawn). A raw vertex collinear in the frame of its curve is no sample
    (runs do not split there, as they do not at a straight's collinear
    vertices). A straight has no frame, so every straight-road reading is
    unchanged; on R 1000 to R 50 the frame reproduces them: the largest
    raised-cosine bump that passes is 2.5-2.6 cm over 8 m (was 3.9-35.4 cm),
    a smooth jog over 4 m 3.7 cm (was 6.6-41.6), a lone kink against the curve
    1.0-1.2° (was up to 7.1°), a sampled wave ±2.1-2.9 cm (was ±16.8 cm on R
    100), a two-vertex data jog over 2 m 2.5 cm (was 13.6), a tent bump over 8
    m 2.5-2.6 cm (was 13.7); the S7 squeeze zigzag reads 1.8-1.9x on R 300 to
    R 100 (was nothing), and the R 300 data spike WP-11 filleted 3.5x. Clean
    arcs, WP-11 fillets of R 300 / 100 / 60 data curves, eased bumps and
    tapers, long S-bends and sub-V features on curves read 0. The raw reading
    is kept as a floor (no key loosens), so the raw rules' own curve bias
    stays (see "Open for the spec"). The run text names which rule fired ("a
    zigzag peak", "a bump or notch", "a hedged corner", "a jog", "a wave", "a
    hook"). `tools/city/lib/kink.mjs` and `CitySmooth.KinkScores /
    KinkScoresOn / ArcFrame` are the same code; a cross-check compiles the C#
    out of CitySmooth.cs and runs it on the JS's inputs (674,537 vertices on
    20,481 polylines - 15,490 of them the city's own gate strands, every line
    kind, and 5,000 random features on straights and arcs - 199,519 scored
    past V, 14,835 of them in the arc frame and 6,989 hooks: identical to
    2.7e-10, the same rule and the same silent vertices every time). The
    offline rules parser reads a `const float` as C# holds it (Math.fround:
    V is 0.02500000037, not 0.025) and a `const double` as a double; review 7
    found the arc frame's discrete tests (|k| <= tolK at exactly R 2000, the
    V/4 fit, the V/50 chord points, a ceil at an exact ratio) flipping on the
    1.5e-8 difference - 39 score and 5 rule-label mismatches in 2,996 random
    lines, an R 2000 bump failed offline at 1.20x and read 0.04x in C#. With
    the float values: 0 mismatches of any kind on 4,492 random lines (arcs of
    R 15-4000, bumps, jogs, waves, hooks, exempt vertices) and the exact R
    1999/2000/2001 arcs, run against the COMPILED CitySmooth by reflection,
    largest relative difference 0. The re-record that came with it
    (`--allow-loosen`, listed in its commit) moved only samples sitting
    exactly on a limit: 16 keys one ratio quantum lower (a value divided by
    the float V or DashTol), and a plan line's existence where a tapered half
    width reaches exactly its offset + `ExistInsetM` (A1: 25 keys gone, 293
    lower, 688 shorter, 325 m; C2: one end at exactly one lane, MatchM).
  - C (continuity): C1 GAP, C2 END (only at junction fan mouths, dead ends,
    a gore NOSE - a collapsed section within 2 m - or plan lane drops; an end
    with a partner within a lane is a JUMP, not an end; a bend fan's mouths
    are no legitimate end; a line starting or stopping along a branch's attach
    arc, where the branch is only clipped, ends mid-road: review 3 found any
    clipped section within 2 m excused 5,388 such ends), C3 DASH (10 ft dash /
    30 ft gap ±20% along the chain).
  - D1 CROSS: paint more than 20 cm inside other pavement at the same level,
    against the triangles the renderer draws (a fan's star or its ear-clipped
    corners, read from the mesh; gore quads by their real long sides). Only a
    branch's ATTACH ARC (its clip range / seat pieces + `MergeMarginM`, 1 m) is
    a merge zone, report-only until WP-18b; the same branch/host pair anywhere
    else, and paint inside another road at a shared fan or mitred node, is
    gated.
- **V = 2.5 cm**, one framebuffer pixel at 6 m in the 240-line chase view, is
  every lateral limit. Every number is in `Editor/SmoothRules.cs`. A texture
  run's own rounding is subtracted from the position checks, but only up to
  half a texel (V is half a texel; today 0.1-2.8 cm against caps of 0.9-3.6
  cm); a run further off fails A0, so a painter regression cannot hide.
- **Two implementations, one answer.** `node tools/city/linecheck.mjs`
  (offline, about 2 minutes, the plan-view replica of the builder in
  `tools/city/lib/linesim.mjs`) and `Editor/CitySmooth.cs` (the built meshes,
  through the tap in CityMeshes). They must agree on every check both measure;
  **a disagreement is a gate bug**, fixed before anything else. Only the mesh
  gate sees tile seams, fans and heights. linecheck's replica follows the
  builder: a CityMeshes change updates `linesim.mjs` (and `--model`) in the
  same commit. `node tools/city/gateprobes.mjs` runs 230 synthetic probes with
  known answers (split kinks, legitimate fillets and S-bends, a kink behind a
  sub-V step, bend fans, attach arcs, straight roads, hedged corners, notches,
  jogs of every width, bumps and zigzags, waves, jogs into a fan mouth, legal
  vertices beside a jog, bumps and notches wider than the cap, C2's closed
  list, squeezed edges against I7 - also inside a slow rise - an edge pulled
  in without a squeeze, features on a curve and the curves that are no
  features, the rules read as C# floats, one run one name whichever way a
  chain runs, B2 the same read backwards, B3 at each sample's own class,
  B1 sampling the same places however a strand is cut (L4),
  hooks at a line's end, the ratchet, STALE, the gate's code and the
  pin in the fingerprint, the loosening refusal, the gate moving with the
  data, the builder replica as data); every review finding is one, and
  all must pass - `linecheck --ratchet` runs them. `metrics.mjs` gains a SMOOTH section: B2/B3 on
  the exported centreline and its offset curves, in half a second, as an
  early warning.
- **The agreement instruments** (review 8: a disagreement is put down to a
  mechanism AT THE RUN'S OWN SECTIONS, never to what lies near it).
  `tools/city/gatecmp.mjs compare` matches the two gates' runs and puts every
  run one has and the other lacks down to the first of: *design* (B4s, a
  junction fan's perimeter, a bend fan's mouth or a bend fan inside the
  check's window, D1 inside a gore quad or a fan: the mesh gate only);
  *agrees* (the other gate reads 1x or more at the run's own place on the
  same line: both fail, and cut or key the run differently); *ring* (the
  window reaches the joint where linecheck cuts a closed ring, or lies on a
  small ring both gates cut); *level* (D1: the replica's plan test of level
  against the builder's heights); *input* (along the line and its chain for
  the check's window - B1 2.25 m, B2 KinkViewM, B3 CurveHalfM, C3 a dash and
  a gap, else 1 m - or, for D1, the other road: the builder's tap and the
  replica differ in a clip, squeeze or collapse flag, the builder's per-tile
  tables disagreeing with themselves, a section only one cuts, a ribbon
  corner or U more than 1 mm apart, the surface); *threshold* (the same
  input, the other gate reading 0.9-1x there and this one under 1.1x);
  *kept* and *float* (B-checks, from the strand dumps: the same sections to 1
  mm but different kept vertices; or the same kept vertices, and
  `lib/kink.mjs` run on each gate's own coordinates reproduces each gate's
  own B2 - a discrete step flipping on a sub-millimetre difference); else
  UNEXPLAINED, a gate bug. What each gate was GIVEN comes from `city-smooth.ps1
  -Mode FULL -TapDump <file.gz>` (`PSX_SMOOTH_TAPDUMP`: every tile's tap -
  each ribbon quad's edge, arcs, flags, corners, U and heights, gore quads,
  fans - gzipped, 'TAP2') against `linesim.mjs`'s sections; what each READ at
  the other's runs from the traces: `gatecmp.mjs places <csv>` lists a gate's
  runs (the RunBuilder checks: not the events B4, B4s, C1-C3), and `-Trace
  <places> -TraceOut <reads>` (`PSX_SMOOTH_TRACE`) or `linecheck --trace
  <places> --trace-out <reads>` records the other gate's highest ratio there
  on the same line and line class; how each KEPT a strand from
  `PSX_SMOOTH_STRANDS` / `linecheck --strands <edge list> --strands-out
  <file>` (every strand through those edges: its kept vertices, exemptions and
  B2 scores). The traces check themselves: linecheck traced at its own
  946,005 runs reads each at or above its own ratio, and gatecmp reports how
  many PARTNERED runs the other gate reads at 1x or more (99.95%). `linecheck
  --reverse` reads every strand backwards: the ratchet against the forward
  run's baseline passes, check for check (review 8's L5, city-wide).
- **Modes.** FAST runs inside `CityAudit.Run` (the drive and roadside
  audits' tiles, the reference spots, one band of 1/12 of the road tiles:
  `PSX_SMOOTH_BAND`). It is OPT-IN (`PSX_SMOOTH_FAST=1`, which
  `city-smooth.ps1 -Mode FAST` sets) until its first Unity run validates the
  tap and its cost; then `SmoothRules.FastInAudit` puts it in every city cycle
  (done 2026-09-29: 1,037 tiles in 112-162 s inside the audit, its keys
  matching the FULL baseline's - 0 new, 0 worse, 0 longer - CITY AUDIT OK).
  While `ReportOnly` even a crash of the gate is an info line, and the tap is
  switched off in a `finally` around the two audits. FULL
  (`tools/city-smooth.ps1 -Mode FULL`) runs every tile a ribbon reaches (each
  edge every 2 m, its half width + 1 m: 10,311 tiles) and is the G-ship run of
  every city release. SHOTS shoots each worst offender from above (2.2 cm a
  pixel, with the measured line and the plan drawn over it), from the chase
  camera at 240 lines and from `_high`.
- **The ratchet.** The first cycle only reports (`SmoothRules.ReportOnly`).
  From the next, a RATCHET check fails on any new violation key, on any key
  worse than its own baseline ratio or LONGER than its own baseline bad
  length, on more runs, on more metres (FULL) or a worse city-wide worst; a
  ZERO check (A0, B4s now) fails on any run; a REPORT check never fails. A key
  is (OSM way, arc along the way in 5 m steps, check, line), and a run carries
  one for EVERY 5 m bucket its bad samples touch, each with that bucket's
  worst ratio and bad length (in 0.1 m steps): a violation that spreads,
  thickens or worsens anywhere trips it. Runs break where two bad samples are
  more than 10 m apart. FAST compares its run count only with the baseline's
  runs in the tiles it analysed (the baseline stores runs per tile).
- **Baselines and their inputs.** `tools/city/baseline/linecheck_baseline.json`
  (offline, one entry per builder model: `asbuilt` today, 8.4 MB) and
  `smooth_baseline.json` (the mesh entry `mesh`, by `city-smooth.ps1 -Mode FULL
  -WriteBaseline`). Each entry records the INPUTS it was measured on: the
  graph hash, a digest of each container section the gate reads (NODE, NAME,
  EDGE, PNTS, SPAN, XING), of the geometry rules in SmoothRules.cs (not the
  rollout switches or the ranking; the creek pin's ways are in it), of the
  road PNGs, (mesh) of the DEM, of the GATE'S OWN CODE (review 5: a loosening
  edit to the gate moved no input, so it passed its own ratchet and was never
  STALE - offline `tools/city/lib`'s checks, plan, texture scan, rules parser
  and ratchet, `code`; mesh `CitySmooth.cs` and the tap in CityMeshes),
  (offline) of the BUILDER REPLICA (`linesim.mjs`, `citydata.mjs`, `replica`:
  review 6 - in the gate's digest, every builder fix mirrored there read as a
  gate loosening), and the model; every check's STATE; and the PINNED ways (a way dropped from
  the pin fails, stale or not). A ratchet against other inputs is
  **STALE, and STALE FAILS**: linecheck exits 1, `city-smooth.ps1` exits 1,
  and the city audit writes a failing Check ("smoothness baseline measured on
  today's inputs"), counted in `CITY AUDIT: N FAILURES`, so `city-cycle.ps1`
  and `verify.ps1` exit 1 (once `ReportOnly` is off). The data moved, so the
  keys cannot tell a regression from the move, and a gate that went quiet
  there (review 3: it used to print info lines) would pass whatever regression
  arrives with it. The way out is the explicit re-record in the commit that
  moves the inputs - a re-export, a merge that brings new SPAN/XING rows, a
  threshold change: `linecheck.mjs --write-baseline` (or `city-smooth.ps1
  -Mode FULL -WriteBaseline`, which fails unless the sandbox's baseline was
  rewritten by that run) prints BEFORE -> AFTER per check (runs, metres,
  worst, keys) and what moved, for the commit message - and REFUSES, writing
  nothing, when the re-record would LOOSEN the gate: the GATE moved (its
  rules or its check code) and the data did not, but keys vanished or score
  lower (review 5: the previous re-record dropped 763 B2 keys inside a total
  that grew by 20,342); the gate moved TOGETHER with the data (review 6: the
  key comparison was skipped whenever any data input moved, so a loosening
  rode along with a re-export or the charlotte merge's NAME/SPAN unseen) - it
  is then re-recorded in two steps, the gate change on the old data first,
  then the data; or a check state or a pinned way loosened. A move of the
  data alone - a re-export, the builder replica (offline), the builder itself
  (mesh: CityMeshes beyond the tap is in no input) - compares no keys and
  records freely (a builder fix drops keys; review 6: the refusal read every
  builder fix as a loosening, M0's U alone as 735k keys). A builder fix in
  the same commit as a gate change is two steps too. `--allow-loosen`
  (`-AllowLoosen`) records a refused re-record anyway, for a deliberate,
  signed-off change, its list in the commit. A ribbon
  edge's key names the side of the edge its bucket lies on (RL / RR): a chain
  runs some edges backwards, and labelled by the run's worst sample a key
  jumped sides - vanishing from the ratchet - whenever the worst moved to
  such an edge. A check gating looser than when its baseline was recorded
  (ZERO -> RATCHET -> REPORT) fails too, stale or not; a ZERO check or the
  creek pin fails whatever the baseline. **The offline ratchet runs in
  `tools\city-cycle.ps1` and `tools\verify.ps1`** (`linecheck --no-census
  --ratchet`, which also runs the probes; exit 1 fails them - review 5:
  nothing ran it, and with the mesh gate report-only no regression failed any
  automatic step).
  Merging `charlotte` (WP-04's water spans: 264 -> 560) into this branch keeps
  graph 27bccd93 but changes SPAN and NAME: the ratchet FAILS there as STALE
  until the merge commit re-records. Done 2026-09-29 in two steps: the gate
  change (review 7's float constants) on the gate branch's old data first
  (0e4e6f8), then the merge into `charlotte` re-recorded the data move alone
  (NAME, SPAN and R1's `citydata.mjs` in the replica; no key compared): A1
  100,817 -> 100,799 runs, B2 472,742 -> 472,778, B1 254,788 -> 254,777, the
  rest within a few runs, every worst unchanged. Review 8's gate fixes (the
  gate alone moved, on the same data) re-recorded both baselines with
  --allow-loosen, the loosening listed in their commit: B1's re-phased and
  de-biased stations and B2's symmetric kept vertices move keys both ways
  (offline B1 254,431 -> 256,561 runs, worst 16.7x -> 17.4x; B2 473,247 ->
  473,450), the joint pairing and naming fixes RENAME B4, C2 and C3 keys
  (the run counts unchanged but C3's +2), and a key's bad length is now
  split between the two buckets an arc joins. The creek (ways 1078015030, 16671358,
  1252904925) is pinned: any run there fails from the day its fix lands
  (`SmoothRules.PinActive`). A check goes to hard zero when the package named
  in its row of `SmoothRules.Checks` lands; nothing is ever loosened.
- **Reading `city_smooth.txt`** (linecheck prints the same). The header gives
  V, the mode and band, the graph hash, the tiles and kilometres, the bend
  fans, and STALE when the baseline's inputs differ. The table gives, per
  check: its state, the violation runs, their metres, the worst value in the
  check's unit, the worst as a multiple of its limit, how many runs are DATA
  (the exported line kinks there, or a bend fan) or BUILDER (the mesh added
  it), and the baseline (its runs - in FAST, in these tiles - its metres, its
  worst, and how many keys are new, worse or longer). Indented REPORT rows are
  the parts not gated yet (D1 on an attach arc, C3 stubs). Then the creek pin,
  and the worst 40 (ratio capped at 40, times class weight, times 2 on a race
  route or 1.5 at a reference spot; one per way, check and line within 40 m).
  Each row names the line (EL/ER edge line, RL/RR ribbon edge right/left of
  travel, MID, CPAIR, `C<colour><s|d><offset>` a centre or lane line), the
  edge, OSM way and name, the profile, deck or ground, s, the run length, game
  coordinates, lat/lon, the tile, the cause (TAPER, DIAGONAL, VERTEX, MITRE,
  SQUEEZE, CLIP, STRUCTURE-END, BEND-FAN, SEAM, FAN) and a spot token that
  `PSX_SMOOTH_SPOTS` shoots. Every run is in `city_smooth.csv`.
- **The first Unity run (2026-09-29, R4 prep; the R4 BEFORE).** On
  PSXCity after the merges of main (watch mode, the stage lane ladder,
  Chimney Rock - since held back again by main) and of this gate into R1's
  city. FAST (inside CityAudit, band 8 of 12): 1,037 tiles, 445 km of
  ribbon, 126,202 runs in 112 s, CITY AUDIT OK, the drive audit's five zeros
  still zero. FULL (every tile a ribbon reaches): 10,311 tiles, 3,701 km of
  ribbon, 14,222 km of line, 323,618 strands, 602-906 s (the machine shared
  with one to five other Unity jobs; the backward B2 walk costs about a
  third). Both gates, runs / metres / worst, as re-measured after review 8's
  fixes (below; the first run's B-family figures in brackets):

  | check | CitySmooth (mesh) | linecheck (offline) |
  |---|---|---|
  | A0 TEXTURE (ZERO) | 0 | 0 |
  | A1 OFF | 100,812 / 1,082,476 m / 5.72 m (228.8x) | 100,799 / 1,080,536 m / 5.64 m (225.4x) |
  | A2 SKEW | 15,179 / 80,455 m / 2.97 m | 15,181 / 80,457 m / 2.97 m |
  | A3 INSET | 15,788 / 356,469 m / 1.69 m | 15,797 / 356,325 m / 1.69 m |
  | A4 LINEWIDTH | 29,993 / 289,054 m / 4.0x | 29,988 / 289,069 m / 4.0x |
  | A5 STRAY | 17,913 / 177,217 m / 5.72 m | 17,913 / 177,191 m / 5.64 m |
  | A5b MISSING (report) | 27,789 / 275,310 m | 29,052 / 334,592 m |
  | B1 JITTER | 256,408 / 56,539 m / 43.5 cm, 17.4x [254,355 / 55,706 m / 41.7 cm] | 256,561 / 56,604 m / 43.6 cm, 17.4x [254,431 / 55,717 m / 41.7 cm] |
  | B2 KINK | 475,028 / 1,017,696 m / 4.84 m (193.5x) [474,902 / 1,019,010 m] | 473,450 / 1,025,855 m / 4.84 m [473,247 / 1,027,599 m] |
  | B3 CURVE | 9,059 / 11,789 m / R 2.4 m (37.4x) [9,054 / 11,813 m] | 8,766 / 12,733 m / R 3.5 m (25.7x) |
  | B4 JUMP | 26,801 / 7.02 m (280.9x) | 26,809 / 7.02 m |
  | B4s SEAM (ZERO) | 4 / 71.5 cm | not measurable |
  | C1 GAP | 235 / 6,377 m / 602.9 m | 231 / 6,324 m / 592.5 m |
  | C2 END | 16,784 / 11.4x | 16,775 / 11.3x |
  | C3 DASH | 10,922 / 43,377 m / 250% (+1,002 stubs, report) [10,920] | 10,919 / 43,358 m / 250% (+1,005) [10,917] |
  | D1 CROSS | 3,378 / 10,470 m / 9.37 m (+801 on attach arcs) | 913 / 5,630 m / 9.37 m (+1,050) |
  | E1 FLOAT | 0 | 0 |

  The creek pin reads the same in both gates, check for check: A1 43 (1.92
  m), A2 12 (33.3 cm), A3 4 (17.1 cm), A4 12, A5 6 (1.92 m), A5b 6, B1 9
  (5.0 cm), B2 22 (16.9 cm), B4 8 (18.6 cm), C2 4 (3.5x), C3 2. The mesh
  baseline (`smooth_baseline.json`, entry `mesh`) and the offline one are
  recorded on this code.
  - **A correction.** The first write-up of this run said 68 runs were left
    unexplained. Its own per-place classifier had left 555; the 68 came from
    a second pass that filed 272 runs reading 1.1-1.5x in one gate and nothing
    in the other as "near the threshold" (under a 1.1x label) and 215 as "a
    clip or clip pair within 40 m" - proximity, not a mechanism - and it left
    the 106 "within 1.5 m of a tile seam" out. By its own 1.1x criterion 340
    were unexplained (review 8).
  - **Review 8's gate fixes, in both gates** (each shown by a probe, the
    C#-against-JS extractions of the changed functions agreeing bit for bit:
    688,700 B1 stations and 81,045 kept vertices):
    - B1's stations stand on the WAY'S arc (wayOff + s, the arc the keys
      are rounded on) at whole steps and a half, not at whole steps from the
      strand's middle: the mesh gate cuts strands at its 3x3 ring, so the two
      gates sampled a different phase wherever they cut a strand differently
      (1,508 / 1,684 B1 runs one gate had and the other lacked; L4). The half
      step keeps a station off the 5 m key boundaries, where a float s put a
      sample in one bucket walked one way and the next the other way.
    - Every B-check reads a strand the same walked from either end (L5: the
      city with every strand reversed gives every key the same ratio and bad
      length; before, 111 B2 and 2,610 B1 keys moved): the collinear drop
      keeps a vertex when the walk from EITHER end keeps it (it dropped
      relative to the last kept vertex, one walk at a time); B1 exempts a
      window where the line it samples is exempt (both ends of a segment, or
      the vertex it sits on - the segment's start alone exempted it one way
      and judged it the other); a station at a segment's midpoint takes the
      name of its FirstEnd end; a key's bad length gets half the arc from the
      previous bad sample (it got all of it, in the walk's direction).
    - A dash or gap ending at a joint is named after the joint's FirstEnd
      piece (e11484 one way, e1904 the other); equal-distance joint pairs are
      ordered by their end points, not by the order the walk met them.
    - D1 names what the paint is inside ("inside e14400", a gore quad, the
      fan at a node).
  - **Run by run** (gatecmp.mjs, above). Unpartnered after the fixes: 4,362
    mesh runs and 2,294 offline ones of about 1.0M each (5,435 / 3,409 in the
    first run's comparison, A5b included). Per check (mesh / offline): A1 129 / 64, A2 2
    / 3, A3 18 / 28, A4 14 / 8, A5 8 / 3, A5b 228 / 840, B1 433 / 529 (was
    1,508 / 1,684), B2 929 / 572, B3 186 / 52, B4 6 / 14, B4s 4 / 0, C1 12 /
    6, C2 71 / 64, C3 4 / 5 (was 61 / 68), D1 2,318 / 106. The reads check
    themselves: 99.95% of the runs each gate partners read 1x or more in the
    other. Every one of the 6,656 put down to a mechanism at its own
    sections:
    - by design 2,908: D1 inside a gore quad 1,886 or a junction fan 362
      (the replica draws neither - D1 is the mesh gate's by design), a bend
      fan's mouth 411 or a bend fan inside the check's window 4 (the mesh
      walks the slab's perimeter), a junction fan's perimeter 241, a tile
      seam 4;
    - both gates fail there (the other reads 1x or more on the same line;
      the run is cut or keyed differently) 1,452 - 1,028 of them A5b;
    - the two gates were GIVEN different sections 1,720 (the tap against the
      replica, over the check's window along the line and its chain): a
      clip, squeeze or collapse flag 767, a section only one of them cuts 506
      (361 cut by the builder only: its per-tile clip tables add samples), a ribbon
      corner 1-5 mm apart 354 (the builder's float32 section math against
      the replica's doubles), the other road's ribbon under D1's paint in one
      of them 73, the builder's per-tile tables disagreeing with themselves 9,
      the surface (on structure) 5, U 4, a corner over 5 mm apart 2;
    - the same sections, and the other gate reads 0.9-1x there while this one
      reads under 1.1x: 320;
    - the same sections to 1 mm, but the collinear drop (0.02 degrees) kept
      different vertices within the check's window 93, or the same kept
      vertices and the kink rules - run by lib/kink.mjs on EACH gate's own
      dumped coordinates - reproduce each gate's own reading 19: a discrete
      step of the rules flips on a sub-millimetre difference (Endhaven Lane
      e19051: a 9 cm segment's turn is -0.02 degrees in one and -0.04 in the
      other, and a wave's lobes change);
    - a closed ring 52: linecheck starts a chain at its lowest edge, so it
      cuts each of the city's 7 rings of mitred joints at one joint (the two
      I-485 carriageways, 105.8 and 105.6 km: at e625/e10056 a lane line's
      1.27 m jump, 51x, is judged only by the mesh gate), and the mesh gate
      cuts a small ring (5 of 78-129 m) at a joint of its own;
    - D1's level 40: the replica's plan test (a crossing within 80 m, or
      structure) separates two roads the builder draws on one level (24), or
      the builder's surfaces stand more than CrossDyM apart where the replica
      has no heights (16).
    **52 remain UNEXPLAINED** (0.005%), open for WP-G: A5b 7 (report-only),
    D1 10, B1 14, B2 2, B3 3, A3 4, A1 5, C2 7; by ratio, 20 under 1.1x, 16
    at 1.1-1.5x, 7 at 1.5-3x, 9 at 3x or more (the worst: A5b 20x on seven clipped
    edges offline only; D1 at Ballantyne Commons Parkway e16895 12.0x offline
    only, the mesh gate reading 0 on the same pavement at one level; B2 at
    e10046 and e14870, mesh only, no strand dumped there). The
    list is `gatecmp.json`'s `UNEXPLAINED` rows.
  - **Open for WP-G** from this comparison: close linecheck's rings (link the
    closing joint and read the strand round it); the replica's section
    fidelity (the builder's per-tile clip samples, its float32 section math:
    506 + 354 + 93 + 19 runs); D1 offline - the replica draws no gore quads
    and no fans, so linecheck cannot see 2,248 of the mesh gate's 3,378 D1
    runs (paint into a gore 1,886, into a fan 362), and has no heights; and
    the 52.
  - **G-play, re-baselined (review 8).** The prep's Uptown runs retired 2
    rivals each time against the 1 WP-04 recorded - one run of one seed. An
    interleaved A/B on PSXCity, the same baked scenes, hidden (`-batchmode
    -nographics`: a watched editor on this machine stops at "Revert All
    Window Layouts"), seed 0, 330 s: A is charlotte f92348e's Scripts AND
    Editor (before the R4-prep merges; its own RacePlayCheck, without the
    autopilot's allowReverse = false), B this tree; one to three other Unity
    jobs on the machine at each start.
    - Uptown, A B B A A B B A A B: A retired 0, 1, 2, 2, 3 (mean 1.6), B 1,
      1, 2, 1, 2 (mean 1.4). The causes are the same in both: the first-
      corner pile-up at wp 150-370 (A 5: traffic 4, a barrier; B 3: traffic,
      the player's car, the road's edge), the barrier at wp 1482 (A 1, B 2),
      traffic at wp 2037 (A 1, B 1), a Transit at wp 1265 (A 1); B also had
      one stall with no hit (wp 344).
    - Independence, A B B A A B B A: A 1, 1, 0, 1 (mean 0.75), B 1, 3, 0, 0
      (mean 1.0; with this tree's two single runs, 0 and 1, 0.83 over six),
      all traffic but one contact with the player's car.
    - Tryon: 3 and 3 on this tree (single runs; the baseline's 3).
    The merged tree is no worse than the pre-merge one; a single run is a
    draw from 0-3. G-play's baseline is now the mean of five or more
    interleaved runs: Uptown 1.5, Independence 0.8, Tryon 3 (single runs, to
    be sampled the same way the next time it moves).
  - **B4s SEAM, a ZERO check, fails at 4 places** (report-only this cycle):
    Albemarle Road e1999 / e6285 at the x = 4864 seam (71.5 / 67.5 cm) and
    Cameron Boulevard e8145 / e12946 at the z = 13568 seam (24.4 / 23.5
    cm): the RR edge of each carriageway pair stands in a different place in
    the two tiles that cut it. A BUILDER defect, not the gate's: `clipPairs`
    and `clips` are rebuilt per tile from the gores that tile builds, and
    Squeeze skips a neighbour in `clipPairs` - so the same section is
    squeezed in one tile and not in the next (e1999/e6285 are a clip pair,
    their gore's host chain ending at the seam). The census's clip bugs
    (plan A4 WP-11b) own it; its zero date is R4.
  - **Review 7's holes on curves** reproduce unchanged on this tree (WP-11
    owns them): no frame near a tangent point or on a bend under about
    80-100 m (a 30 cm jog on an R 100 bend of 30 degrees reads nothing; a
    bump over 8 m passes up to 36 cm on R 300 52 m bends), the squeeze
    zigzag S6/S7 on a bend reads nothing, split or hedged hooks pass with
    the end 31-45 cm off (Camp Road e24806 45.5 cm, 1.14x on two lines
    only), a hook at a curve's open end passes at 3.0-9.3 degrees, and R
    2000-3000 has no reference (a 3 cm bump reads 0.04x at R 2001-3000,
    1.11x at R 1999).
- **Where it stands** (linecheck, graph 27bccd93, today's builder): 100,817
  A1 (1,049 of them, 32.2 km, squeezed edges outside their I7 envelope), 15,178
  A2, 472,742 B2 runs over 1,026.6 km (409,247 DATA: OSM vertices, and
  corners split over them; by the rule that gave each run its worst vertex:
  296,905 lone, 52,391 split, 42,950 hedged, 28,667 hooks, 15,953 jogs, 2,236
  zigzag peaks, 13,584 bumps or notches and 20,056 waves - most of the waves,
  419 km, BUILDER paint on taper spans, the diagonal scallop the owner
  circled) and 254,788 B1 runs; 8,518 B3; 26,809
  B4 jumps; 16,776 C2 (with the 101 bend fans' mouths - 87 of the fans turn
  60° or more - and 5,353 line ends along branch attach arcs), 10,917 C3; D1
  gates 914 runs / 5.6 km of paint inside another road, 6.2 km more is on
  attach arcs. 1,446,475 keys. The worst B2 is Sunset Road's data dogleg
  (22.7°, -32.2°, -31.1°, 21.7° in 39 m), 4.84 m outside its net corner.
  Review 6's fixes moved only B2: +8,349 runs, +40.7 km, 624,779 keys (+13,249
  new, 27,233 scoring higher, 0 vanished, 0 lower - the re-record needed no
  --allow-loosen); the runs with a new key are nearly all HOOKS (9,614, 7,333
  of them DATA: a data vertex a metre or two before a fan mouth or dead end;
  the reviewer's 40 hook spots all fail now on 4-9 lines each at 3-13x - 14
  of them read nothing before - e.g. Westinghouse Boulevard 5.5x, Sam Drenan Road 6.7x,
  Monroe Road 4.5x, East 16th Street 3.4x, Lawyers Road 5.6x). The ARC'S FRAME
  acts on today's city almost nowhere: it needs a reference, vertices within
  V/4 of one circle over 20-40 m, and today's curves are coarse, irregular
  OSM polygons (of 15,490 city strands in the cross-check, 50 have a frame
  and 2 vertices score in it; the reviewer's 7 candidate jogs on data curves
  still read under V - their curves turn 2-10° a vertex, irregularly). It is
  WP-11's acceptance test: its fillets and WP-11b's eased squeezes are the
  sampled arcs the frame judges. Review 5's fixes moved only B-family keys: B2 +328 runs, +90.5 km, +14,156
  keys (0 vanished and 0 lower against the previous baseline, a ribbon edge's
  RL and RR compared as one line - the keys' side labelling moved; against
  round 3's baseline too, so the 763 keys the ease had dropped are back), B1
  +5 and B3 +1 keys (the same relabelling). The reviewer's city lists: jogs
  into a fan mouth 37 of 39 flagged (was 7; Carmel Road 5.2x, e10246 4.7x,
  Baxter Street 3.6x, South Tryon Street 3.6x, Circumferential Road 3.0x - the
  two left are a 6.4° stub 1.2 m from its mouth, lone-judged, and a 3.2 cm
  step); jogs and bumps beside one legal vertex 13 of 18 (was 3; North Irwin
  Avenue 2.6x, Pelton Street 1.2x, Matthews-Mint Hill Road 1.7x, Prospect
  Street 2.2x, West Sugar Creek Road 2.6x - the five left are steps of 4-7
  cm whose part faster than the ease is barely over V); wide shapes 8 of 24
  (East 12th Street 2.2x; the rest sit on gentle curves, no straights). The
  creek as built: yellow 0.39 m off at s 51.6, white lane line 1.92 m off and
  STRAY, B2 16.9 cm (22 runs), B4 18.6 cm. Under the M0 stopgap (`--model
  m0`) the creek's centre and lane lines read 0; its tw4 edge lines still
  slide 0.36 m to the drawn edge before the crop removes them (A1, A3, C2),
  and e14582's ribbon edge now reads B2 3.9 cm (3 runs, 1.6x): its eased
  taper curve is followed 12 m on by a -1.14° data vertex, a notch the wide
  rule sees (WP-10's Douglas-Peucker removes that vertex). The city under M0:
  428,412 B2 runs (13,538 BUILDER; 420,176 before the hook rule), 42,198 A1,
  12,344 B4 jumps (most of 1-2 m: M0 does not touch the lane-count steps at
  nodes). metrics' SMOOTH: B2 at 73,174 centreline and 157,434 offset-curve
  vertices (72,874 / 156,852 before review 6).
  linecheck also reproduces the 2026-09-28 census exactly (all 23 rows:
  264.88 km of wobble of 5 cm or more, 75,073 edge kinks over 2°; `--model
  nominalU` its U-only run: 17.47 km).
- **Open for the spec** (calibration questions, reported, not changed):
  - The arc's frame is built from V and the chord cap, not from constants of
    its own: a reference circle holds every vertex of a 20 m window within
    V/4 (6.25 mm) and must be sampled (three vertices, no gap over 5 m; 40 m
    and 10 m for a coarse curve); curvatures within 2V/(10 m)^2 (5e-4, R
    2000) are one curve; a clean island under 10 m is no reference; a line
    end takes the curve on while its heading stays within 4 lone limits
    (4.6°); chord points are those within V/50 (0.5 mm) of the chord. A
    rougher drawn curve (today's OSM ones) gets no frame.
  - The raw rules keep their own curve bias, kept as a floor so that no key
    loosens: on a gentle curve a wave's mean line (the chord of two
    inflections) sags by the curve's sagitta and a cluster's summed turn
    includes the curve's own turn, so a ±1.7 cm wave every 10 m on R 300 reads
    past V (on a straight ±2.6 cm passes) and a ±8 cm wave every 16 m reads
    5.3x there against 3.3x in the frame. Letting the frame replace the raw
    reading inside a reference would be a deliberate --allow-loosen (an
    earlier draft that did so dropped B2 keys on today's city).
  - The hook rule cannot tell a hook from a curve cut off within its first
    chord: a line that runs straight, turns at a tangent point into an R 30 m
    arc sampled at the 2 cm sagitta, and ends 1-2 m on reads 1.3-1.7x (the
    end stands 3.5-7 cm off the approach, as the arc itself does); an R 7.9 m
    fillet cut 1 m in 2.7-4.0x; R 100 m passes. At an OPEN end of the frame (a
    curve's reference carried on to the end) no hook is judged: a hook at the
    end of a curving line is the raw rules' only.
  - `KinkViewM` = 40 m (the split-corner rule's viewing distance, from the
    spec's "at R 7.5 a 90° corner stays rounded out to about 40 m"; since
    review 5 also the widest window a bump or notch is judged over).
  - B1 fails at 2.9 cm (1.15x) on the INNER ribbon edge at the tangent points
    of an R_min street fillet (inner radius about 3.5 m; the spec calibrated B1
    on the centreline down to R 7.5).
  - B3 reads a sampled arc about 2% under its radius, so a WP-11 fillet needs a
    few percent over R_min.
  - The cluster rule (unchanged here) reads a smoothstep lane shift or squeeze
    as a kink at its ends: 1 m over 10 m sampled every 0.5 m reads 2.0x, the
    I7 squeeze of 1 m over its 15 m floor 1.6x on its edge and midline (1.2x
    at 2 m sections; two stacked 1.6x), an eased 1 m bump over 15 + 15 m 1.8x,
    and back-to-back R 12 m / 30° fillets with no tangent read 1.1x. If R4
    draws I7 or TAPR shifts as smoothstep, this needs a decision (the spec's
    Appendix A calls a 10 m eased taper a pass). The squeeze probes (Q2, Q4)
    assert only the I7 envelope.
  - A jog wider than a quarter of the cap is judged by the part of its step
    that came faster than a smoothstep over the SHORTEST class taper floor
    (the street's 15 m) - for every class, since B2 runs per line, not per
    class - so a motorway line is held to a street's ease; one of 2.5 m or
    less by the whole step (round 3's rule, restored). The cliff at 2.5 m (a
    3.3 cm step reads 1.32x over 2.5 m and passes over 2.6 m) wants a
    decision if the discount is to stay. A jog on a clean curve is judged in the
    arc's frame as on a straight; on a rough one (no reference) only B1 sees it (about half its step).
  - An S-bend of sampled arcs SHORTER than the 10 m chord cap between two
    straights is a jog: R 30 m arcs turning 3° each way shift a line 8.2 cm in
    3.1 m (2.3x), and 8° each way 58 cm in 8.4 m (5.8x). Wider S-bends are
    never judged as jogs (a road's reverse curve; B3's).
  - A straight is now also a stretch within V of one line turning no more than
    the lone limit, judged from that line less its spread: a single legal
    vertex no longer switches the window rules off. The same test cuts both
    ways: a zigzag of under V about a straight is judged against its mean, and
    one that touches the straight at every trough - a row of 3.5 cm bumps
    over 2 m legs - fails as its single bump does (review 4's control "zigzag
    ±2° every 2 m" was that one-sided shape; the control is now centred).
  - Wider windows judge OUTSIDE by the part faster than the plan's ease, and
    the 10 m ones by the whole excursion (round 3/4's rule, kept so nothing
    loosens): a 4 cm bump reads 1.6x over 10 m and 1.1x over 10.5 m. A row
    of bumps wider than the cap is eased too (the ±1° every 4 m one-sided
    zigzag, 3.5 cm peaks over 4 m legs, passes).
  - The WAVE rule's geometry limit is the plan's WP-10 Douglas-Peucker
    tolerance (`SimplifyEpsM`, 0.5 m): a lobe standing that far off its mean
    line is a winding road's, B3's: the wave rule judges a lobe of 45 cm and
    leaves one of 55 cm to the corner, cluster and curve rules (a ±60 cm wave
    every 20 m still fails those, 17.8x); if WP-10 changes its tolerance, this
    follows. A
    wave longer than the cap is judged by its worst 10 m sub-chord (a ±10 cm
    meander every 80 m passes, its 10 m chords sag under a cm).
  - A zigzag peak scores twice its facet sagitta (the spec's section 2 rule).
    The spec's Appendix A still lists "zigzag ±3 cm, 10 m period: B pass; A1
    catches it" - on a DATA line A1 cannot (the plan follows it), so B2 now
    fails it (the sampled wave 1.3x, the sharp zigzag 1.2x). The appendix row
    wants updating.
  - Owner question: a squeezed edge that weaves 0.5 m in and out every 36 m
    passes the I7 envelope (each rise is longer than the 15 m floor, and I7
    only holds dips narrower than the floor).
  - The squeeze envelope runs per edge, not per chain: a squeeze that crosses
    a node is judged on each side separately.

## WP-10 (R4): the line clean-up (2026-09-29)

The owner, after ten minutes on /city/: "Squiggly roads. A lot of roads where
opposite direction merged into a single road. All sections of road that add an
additional lane for a turn lane, instead expand a lane and widen on both
sides, as if center aligned." (plan amendment A8). WP-10 is the exporter half
of the fix: `tools/city/lib/lineclean.mjs`, run by `export_osm.mjs` right after
the dead-end weld, so crossings, water spans, routes and plots read the
cleaned lines. The runtime draws its lanes from it in WP-11b; until then the
ribbons still taper symmetrically, and the census counts every lane change
as a symmetric taper (5,504 edges; 5,774 before WP-10). WP-10 landed in two
rounds on 2026-09-29: 051ea80, then its review's fixes. This section
describes the second.

- **Tagged nodes first** (critic C3): every control node on a kept way is
  recorded as (way id, node id, raw distance) before any vertex moves and
  projected to (edge, s) at the end - section `TAGN` (7,302). Every raw
  vertex two OSM highway ways share - kept or not (toll, private, unnamed
  service) - is pinned (23,592). The belt's unfetched residential and
  service ways (the ids-only Overpass query) are not in the cache yet.
- **Lane-count data**, per CHAIN (a road through its nodes: same one-way-ness,
  under 45 degrees, same name at a junction). A piece with its OWN `lanes=`
  tag is never changed: a short tagged piece is a turn bay or an auxiliary
  lane, and TAPR draws it on one side (2,521 such pieces under two taper
  floors; 0 edges drawn below their own tag). The first round deleted them
  (2,194 tagged edges lost a lane, 1,231 of them naming it in `turn:lanes`,
  Dalton Avenue's dual left bay and I-277's merge lane among them). Untagged
  only: an A->B->A flicker under 100 m or a piece under two taper floors
  takes A (130, 5.8 km); an untagged ground piece between two tagged ones
  takes theirs (146, "inferred"); an untagged bridge keeps the default and
  is listed (46).
- **Simplify**: vertices under 2 m apart collapse, then Douglas-Peucker at
  0.5 m between pinned vertices; links and roundabouts are left for WP-11
  (critic C32). Decks and tunnels are left alone (801), and where the lane
  count changes the first OSM piece keeps its length (11,291 edges):
  today's builder tapers over that piece, and dropping its far vertex
  stretched a 49 m mw3-mw4-mw3 deck of I-277 (Uptown Loop) into a taper that
  never reached full width, its rails in the lanes. 179k -> 119k points
  before PARA.
- **Doglegs**: two opposite turns of 8 degrees or more within 20 m, 15 m clear
  of a junction, jog up to 4 m: 61 -> 0 (57 straightened, worst 3.64 m).
- **TAPR** (section `TAPR`): at each of the 7,652 lane-count changes along a
  chain, the ribbon SIDE that moves - never both. Tagged sides come from
  `turn:lanes` (a left-turn lane opens on the left, a merge_to_left lane
  ends on the right) and `lanes:forward/backward` (5,200); untagged, an
  added lane opens on the right of the direction that gains it and a centre
  gain on a two-way road is a left-turn bay for the direction entering it
  (the other direction's side moves, unless that moves more through lanes).
  A run wider than both neighbours opens and closes on ONE side (3,729 turn
  bays). AT A JUNCTION NODE the lane opens or ends full width at the mouth,
  with no taper (1,984; flag 2, len 0): a bay runs full width to the stop
  line (the first round tapered 1,010 of them to nothing where cars turn).
  Elsewhere the length is MUTCD WS^2/60 (to 40 mph) or WS, turn bays 30-55
  m, floors 15 / 30 / 90 m, fitted to the chain's room (2,525) or absorbed
  where there is none (1,916). Then every edge's OFFSET off its OSM line:
  the fixed edge is continuous across a change and the offset HOLDS along
  the run, with no mid-block shift (the first round eased long runs back
  onto their own line with a 1.8-3.7 m shifting taper in 1,478 blocks: a
  swerve). A bay closes on the side it opened, so it comes back; an
  untagged lane at a junction mouth opens on the side that brings the
  ribbon back toward its line (49); past one lane off the line, the next
  junction mouth re-anchors it (142). 8,998 edges, |offset| p50 1.83 m, p90
  3.66 m, p99 7.3 m.
- **PARA** (section `PARA`): carriageways whose pavements, as R4 draws them
  (lanes centred, the facing side's shoulder, the TAPR offset), overlap or
  leave under 1.2 m (one road's two carriageways, express beside general
  lanes) or 1.0 m are moved apart: per chain a lateral offset - the deficit,
  plateau-filled, rate-limited by the MUTCD shifting taper for its speed and
  box-smoothed - zero where the two chains really meet, capped at 6 m
  (freeways and their ramps) or 4 m off the RIBBON's own line (a TAPR offset
  toward the partner is cancelled on top of the cap), 20 passes (12 left
  short residuals at zone edges). Where two chains meet: a node they share,
  out to where the mapped lines part for good (walked 120 m, 200 m on a
  freeway or ramp, the TAPR offsets counted), else need / sin(angle); and a
  JUNCTION BOX - two carriageways joined across a crossing street by a
  connector of 30 m or less, exempt (not pinned) within the connector's
  length (at least 15 m) plus the street's half width and a 25 ft curb
  return. A ramp lying inside its mainline's pavement by more than the cap
  is an attach zone (WP-18b's gore), a surface street's slip lane under 200
  m is junction channelisation (WP-19). Pairs still short by more than 25
  cm are the review list. Measured on the pavements R4 draws (km of
  carriageway pair, outside the attach zones):

  | | before | after |
  |---|---|---|
  | one road's two carriageways | 107.5 | 0 |
  | ramps beside a mainline | 3.1 | 0.7 |
  | review pairs: city / 8 x 8 km core / race routes | 2,089 | 5 / 0 / 0 |

  (The first round left 97 / 10 / 2, East 3rd Street's own carriageways
  0.62 m short for 60 m among them.) Attach zones: 135.7 km of pair, 107.5
  of it ramps at their gores.
- **Crossings**: two edges at the same level that share a node (or a node
  one sub-50 m edge away) and cross within 150 m of it are one merging into
  the other, never a grade separation. The simplify and PARA left 22 such
  mapped lines crossing a few metres off their node, and the solver lifted
  them (a 70 m I-485 node on the first run). 783 crossings, all forced by
  tags.
- **The solver** (`CityElevation.RaiseAllCrossings`): a ramp seated beside
  its mainline takes the mainline's height (a seated station is its
  host's), and on a skewed pair crossing the same road the mainline's hump
  peaks at its own crossing, metres along from the ramp's: the I-77
  connector over I-85 cleared it by 4.35 m once PARA had moved it 4 m clear
  of I-77. In the fresh passes, a seated branch that would clear what it
  crosses by less than ClearanceM - 0.55 m (just inside the audit's 0.6 m
  margin) has the stations its hump needs above the host FREED
  (`SeatStationsFreed`): it rises off its host there and nothing else
  changes (worst clearance now 4.60 m). Raising the host instead stood I-77
  0.8 m above its own other carriageway (25 ledge points); freeing at
  ClearanceM - 0.3 m freed branches that passed and opened two deck gaps on
  the Uptown Loop's tiles.
- **Toll roads: HELD BACK** (`KEEP_TOLL = false` in `export_osm.mjs`). Kept,
  PARA moves the express lanes clear (express beside general lanes 80.1 ->
  0.2 km) but the first round's city audit failed on them: their layer=1
  connector flyovers over I-485 clear the general lanes by -0.55 m, an I-77
  connector climbs 39% into a 26 m bridge (35.3759,-80.8470), and a Monroe
  Expressway ramp crossed Independence Boulevard at grade. That is
  elevation-solver work, scheduled as its own package in the plan (A8's
  schedule note). A one-lane freeway carriageway (the I-485 express lanes
  are one lane each way) draws with the `ramp1` section, not as an 11.5 m
  `mw2` (`RoadProfiles.IndexFor`, `citydata.profileFor`).
- **LANW**: the real lane widths by class (12 ft freeway and ramp, 11 ft
  arterial and collector, 10.5 ft tertiary, 10 ft local). The game keeps
  3.6576 m until WP-11b (plan A3).
- **The city audit's named spots.** PARA moves carriageways apart, and
  today's builder (no TAPR offsets drawn yet) reads the corners they leave.
  None is on a race route; North Kings Drive's mouth at node 6995 and the
  Uptown Loop's I-277 spots of the first round are fixed (the simplify
  hold). Named for the WP-11 / 11b package that ships this data to fix
  first: North Kings Drive at Armory Drive (mapped 5 m apart for five
  lanes, 10.8 m now, into Armory's clipped one-way connectors:
  face-kings-9677, lip-kings-9677, ledge-armory-23550, nose-kings-armory,
  armory-23550, armory-23549), ledge-ramp-1489, lip-link-11147,
  lip-link-14090 and fan-gordon, and at Tyvola Road's bridge
  ledge-tyvola-13557, tyvola-328-rail and tyvola-328-face. The pre-WP-10
  named lane solids the new lines cleared are pruned.
- The container gains LANW, TAPR, PARA and TAGN after GHSH (optional in
  `citydata.mjs`; CityMap skips tags it does not know). `PSX_LC_DIAG=<file>`
  prints PARA by kind before and after, lists the review pairs in the core
  and on the routes, and writes them all as JSON.

## WP-11 (R4): fillets, sagitta densify, chain continuity (2026-09-30)

The owner's rule of 2026-09-28: "Nor should any sharp angles of road or road
lines." WP-11 is step 7 of `lineclean.mjs` (`tools/city/lib/fillet.mjs`),
after PARA and before the tagged nodes are projected, so crossings, water
spans, routes, `charlotte_routes.json` and the minimap all read the rounded
lines.

- **Strands.** Edges joined through every 2-arm node AND through the through
  pair of every mitred junction (a ramp's split or merge, a fork: the
  builder's own ComputeTrims decision, replicated in `mitredThrough`). Other
  junction nodes and dead ends are fixed, and the leg touching one keeps
  max(1 m, a quarter) of its first segment straight, so ComputeTrims'
  through / branch / fan decisions stand. A 2-arm node or mitred junction
  moves onto its arc (median 0.06 m, max 10.3 m at a near U-turn); the
  clipped arms at a moved junction follow it, the move eased out over
  max(10 m, 20 x the move).
- **Radius.** Wanted R = max(R_floor, R_E): R_E keeps the arc within the
  class Emax (street 0.75 m, arterial 1.0, trunk 1.25, motorway 1.5, ramp
  1.25), R_floor = max(the gate's R_min by class, half width + 3 m) keeps
  the inner edge from folding; never further off the mapped corner than
  max(Emax, half the road width). Each segment is shared between its two
  corners in proportion to their TURN, so the curvature is as even as the
  mapped line allows (a nearly straight vertex never starves a real corner
  beside it); a corner capped by its arc leaves the rest to its neighbour.
  A corner that still cannot reach its floor is fitted together with its
  same-hand neighbour across the short segment (one corner where their outer
  tangents meet), if that arc stays within Emax of every mapped vertex.
  Left under the floor: 790 corners (S-bends on short tangents, single-vertex
  hairpins), 217 past Emax - counted in the export log, not hidden.
- **Densify by sagitta.** Chord <= sqrt(8 eps R^2 / (R + hw)), eps = 2 cm
  (SmoothRules.DensifyEpsM) on the outer drawn edge; past 10 m (ChordCapM)
  the gate's own lone-vertex facet, min(c, 10 m) x turn / 8 <= eps, sets it.
  Points 184,961 -> 452,643: charlotte_city.bytes 3.71 -> 5.88 MB raw, 1.99
  -> 3.6 MB Brotli (the plan's budget was +1.71 MB raw; the fallback, a
  radius per vertex expanded in CityMap.Parse, is not built).
- **C11 splits** (891 places where an undivided road opens into its two
  carriageways): section `SPLT` (`lib/splits.mjs`) records each one's
  carriageways, their centre offsets in the undivided section and the MUTCD
  shifting-taper rate. Data only: the line model (WP-11b) draws them as
  median tapers; the builder still mitres the straighter carriageway and
  clips the other.
- **Chain continuity (runtime).** `CityMeshes.ChainContinuity` walks every
  chain of mitred through joints once per map: texture V = (vOff + vDir s) /
  RoadVTile, so the dash phase runs on through way splits, decks and tile
  seams, and the surface age is chosen once per chain (`Edge.ageSeed`).
  `linesim.mjs` replays the same V.
- **Measured** (linecheck, the offline gate, BEFORE -> AFTER; the mesh gate
  agrees within a few %): B2 KINK 413,672 -> 98,690 runs (data-caused
  352,496 -> 11,587; what is left is mostly the builder's symmetric tapers,
  WP-11b's), B3 CURVE 8,747 -> 2,527, B1 JITTER 290,814 -> 179,693, C3 DASH
  11,895 -> 32, A1 OFF 101,416 -> 78,139, C1 GAP 102 -> 91; bend fans (2-arm
  nodes drawn as junction slabs) 109 -> 0; drawn-edge kinks of 10 degrees or
  more 12,408 -> 1,155, of 25 or more 2,402 -> 322; quads folding back 193 ->
  78. Up: A2 SKEW 13,431 -> 16,245 and D1 CROSS 777 -> 822 (builder: tapers
  and clips on the denser sections). The mesh gate (CitySmooth FULL, every
  road tile): B2 475,028 -> 100,305, B3 9,059 -> 2,577, B1 256,408 ->
  179,917, C3 10,922 -> 42, C1 235 -> 104, D1 3,378 -> 2,953, A1 100,812 ->
  78,219. Both baselines re-recorded on this data.
- **Audits.** DRIVE AUDIT zeros, CITY AUDIT OK, city-play-check and the three
  races pass as before (Uptown's two rivals still retire at wp 249, 996 m,
  "into Roads", as on WP-10's tree). The fillets moved lines 0-0.6 m at eight
  roadside / lane-survey probes that already stood on a squeeze strip, a
  retaining face or a rail between roads at two heights (Tyvola Road's
  bridge, North Caldwell's three heights, US 74 at I-277, the ramps e13344,
  e2470 and e8463, I-277's e8482): named in CityAudit's WP-11 blocks for
  WP-11b, none on a race route.
- The gate probe S2 now reads the dash phase running on across splits (no
  C3), and the chain's head is its end with the lower (x, z), so the phase
  never depends on edge order (probe L1).

**WP-11 review fixes (2026-09-30).** Two majors from the review:

- **Near U-turns folded.** A strand ran through every 2-arm node, including
  the two I-85 nodes where a carriageway and its ramp meet nose to nose (both
  arriving, both leaving), and the departure cap max(Emax, hw) left a near
  U-turn's radius under the half width: R 1.2 m and R 3.8 m hairpins on a
  9.4 m half width, nodes moved 10.3 m and 8.4 m, and bend slabs became
  folded mitres (Wynfield Creek Pkwy, Reedy Creek Rd, a primary link, Old
  Charlotte Hwy). Now two one-way arms that both arrive or both leave are
  not run across (4 pairs), and an interior node whose fillet cannot keep the
  inner edge at half width + 3 m is HELD (42): the strand splits there, the
  node stays mapped, both legs keep their direction and the builder draws
  the bend slab it drew before WP-11. All six named spots are held (node
  moves 0), the largest node move is 4.3 m (was 10.3), the gate's worst B3
  is R 3.2-3.3 m (was R 1.2 m: x27.2 offline and x46.7 mesh, both from
  x128.8), worst A3 1.69 m (was 3.05). Left: 29 bend fans (0 on f1e139e, 109
  before WP-11) and 137 free vertices (inside one OSM way) still under half
  width + 3 m, listed in the export log (raw mitres before WP-11).
- **Tile build cost.** Profiled per phase (a sandbox-only stopwatch build of
  CityBudgetProbe), the time went where the points went: the verge solves
  (DropAt / SolveStrip / ClearRun), the ground's corridor query, the squeeze
  and the outline scans all walk segments or spans, and 2.45x the points
  put twice the segments in every 64 m cell and hundreds of spans in a long
  freeway outline. Three fixes, the first two exact (same output):
  `CityMap.EdgeSegsNear` drops the cell's segments whose box misses the
  query (the ground's 100 m corridor query and the squeeze's 64 m one, which
  ignore anything further); `Outline` keeps a box per 16 spans and every hot
  outline scan (ClearRun, MeetPavement, PavementAt, PavementAlong,
  OutlineHeightAt, InsideDeep, the strip seam, the footprint clearance) skips
  the blocks nowhere near it; and the fillet leaves LONE vertices alone
  (where the line turns 0.92 degrees or less in all within 10 m either side,
  the gate's own lone-vertex facet is within eps): 44,238 corners, points
  452,643 -> 409,241, charlotte_city.bytes 5.88 -> 5.53 MB raw (3.67 -> 3.42
  MB Brotli). Summed build phases over the probe's 226 tiles: WP-10 5.46 s,
  f1e139e 8.68 s, now 7.23 s. `CityBudgetProbe` interleaved in PSXCity (the
  WP-10 tree 2398767 against this one, A B A B): p95 60.1 / 59.4 ms against
  90.9 / 97.6 ms, **+58%, still over WP-11's +25% trigger**: by the plan's
  own rule WP-09 (time-sliced tile build) joins R4, to be confirmed on the
  phone reading (critic C29). By site: dilworth 55 -> 97-101 ms (curvy
  streets: arcs at 1.5-2 m chords, each a section with its own verge
  solves), i77_north 44-50 -> 69-71, i485_south 130 -> 167, trade_tryon 86 ->
  103-114; providence fell 64-66 -> 48-49. The map heap reading swings 20.7-
  28.6 MB between identical runs; the points' own arrays (Vector2 + arc
  float) grow by 224k x 12 B = +2.7 MB.
- Measured on the fixed data: linecheck BEFORE (f1e139e) -> AFTER B1 179,693
  -> 170,671, B2 98,690 -> 96,864 runs (747.8 km against 732.1: the lone
  vertices' runs are longer), B3 2,527 -> 2,531, C3 42 -> 31, A1 78,139 ->
  76,836; up A5 STRAY 15,117 -> 15,227 and C2 END 14,471 -> 14,606. The mesh
  gate agrees (B1 179,917 -> 170,834, B2 100,305 -> 98,596, C3 42 -> 31, B3
  2,577 -> 2,579, C2 14,485 -> 14,616). Both baselines re-recorded. DRIVE
  AUDIT zeros, CITY AUDIT OK, city-play-check OK, 231/231 gate probes; races
  (150 s): Uptown 2 retired at wp 249 into Roads as on WP-10 and f1e139e,
  Tryon 3 (lamp posts at lat +5.4 to +8.3, then traffic; WP-10 3),
  Independence 0.
## The lines on /city/ (2026-09-30): WP-10 + WP-11 merged with city-r1 and published

WP-10's and WP-11's lines went to /city/ ahead of WP-11b (the owner, after
13 hours of line work: ship what is done). Merged with city-r1 at ff8ec74
(the CITY edition, trees, WP-13/14's 30 m ground and graded land, WP-23's
signs). What the published page changes: the squiggles (fillets, the
simplify, the doglegs) and divided roads drawn apart (PARA). What it does
NOT change yet: a lane added for a turn still widens the road on both
sides - the one-sided offsets are in the data (TAPR) and WP-11b draws them.

- Data: re-exported from the merged exporter (graph 089d7141); the signs
  rebuilt off the moved lines, the canopy as shipped; the tree audit's
  canopy truth re-read. linecheck re-recorded (inputs moved, readings
  identical).
- The first verify of the merge failed on two sandbox/import points and two
  audit spots: ReleaseBudget.Apply had never run on PSXCity since the colour
  fix (six textures; ApplyFromCommandLine, then verify again), and two
  off-route spots of the WP-11b families were named (ledge-w4th-14607,
  mcdowell-15185); 12 roadside spots, 7 lane solids and the lane-land spot
  the merge took away were pruned. The second verify: VERIFY PASS (CITY),
  DRIVE AUDIT zeros, CITY AUDIT OK.
- city-play-check: the WP-14 drive-off stage fails at one spot the moved
  lines now let its picker choose (I-277 e1919 s=90, left side, Uptown
  Loop: the probe 12 m out is clear, but I-277's other carriageway e2305
  converges further along, and the car, braking 19 m past the edge, goes
  over the median's concrete barriers and rolls: 1.64 m of air). Its one
  cut candidate had a rail in the run, so no cut was driven (a rerun drove
  it). A picker that checks the whole run was tried and reverted: it found
  no fills and a new I-77 level spot (e1891 s=140) with 1.39 m of air 25 m
  out. Open: the stage's picker (WP-14's) and the run-off past 20 m.
- Races (150 s): Uptown 2 retired (traffic, wp 1051-1057), Tryon 3
  (traffic, a lamp post, the player), Independence 1 (OK) - AI against
  traffic, as on WP-10 and WP-11.
- Published: /city/ build stamp 20260930090352 (charlotte d854759); the
  site root byte-identical.

## WP-11b (R4): one line model - lanes added on ONE side, paint drawn as columns (2026-09-30)

The owner, after ten minutes on /city/: "All sections of road that add an
additional lane for a turn lane, instead expand a lane and widen on both
sides, as if center aligned. Two lane to three lane to two lane." (plan A8).
WP-10 wrote the answer into the data (TAPR: which side each lane change
opens on, and every edge's ribbon offset off its OSM line; SPLT: where an
undivided road opens into its carriageways); WP-11b is the runtime that
draws it. `Scripts/City/LineModel.cs`.

- **The model.** Every drawn lateral - both ribbon edges and every painted
  line - is the edge's OSM line plus an offset from the model:
  - OFFSET: the lanes' centre sits `lmOff` (TAPR) off the OSM line, each
    side with its own shoulder (a freeway's 1.2 m inside and 3 m outside:
    the lanes, not the pavement, are centred - what PARA separated the
    carriageways by). `Edge.PaveEdgeM`, `Trims.HalfWidthAt / ExtentsAt /
    CentreAt / ReachAt`, `LaneExtents`, fans, clips, the squeeze, lamps,
    signs, buildings, trees, the corridor and the audits all read it.
  - ONE-SIDED TAPERS: where two through ribbons meet mitred and one edge
    steps, only that side eases (smoothstep), on the wider arm, over TAPR's
    MUTCD length - run on through the next mitred joints, never clamped to
    one OSM piece (`ComputeTrims.Taper` and its 0.9-of-an-edge clamp are
    gone). No TAPR record for that side: the class default (WS^2/60 to 40
    mph, WS from 45, floors 15/30/90 m); a junction-mouth record the builder
    mitres (a lane opening at a merge): the class floor.
  - BOTH EDGES STEP (a change the data centred on a node the builder
    mitres, a one-way carriageway leaving a two-way road): one edge is held
    - a one-way keeps its direction's half, else TAPR's fixed side, else the
    smaller step - the narrow arm is shifted over to it on a MUTCD shifting
    taper and the wide arm eases the whole difference on the other side.
  - MEDIAN TAPERS (SPLT, critic C11): each carriageway starts where its
    lanes are in the undivided road and eases onto its own line over the
    shifting taper; the undivided road is not tapered on the other
    carriageway's half.
  - MIRRORED TAPR: 14 of WP-10's 7,652 changes (all two lanes, at junction
    nodes) carried the narrow run's offset mirrored - I-277 at US 74 drew
    I-277's two lanes as the right pair of the four, and US 74's, joining on
    the right, squeezed them 4.4 m. The narrow run is put on TAPR's fixed
    edge at load, out to the junction at its far end (left alone if another
    change comes first).
- **Paint columns** (plan A2 I2/I3). A span whose two sections are the full
  model width and within the bend limit (w (1 - cos theta) / 2 <= V / 2) is
  one quad with U 1..0, exact. Anything else - a taper, a squeeze, a clip, a
  tighter bend - is a row of strips: each painted line its own 12 cm strip
  at its texels (U fixed, a quarter texel in; never measured against the
  section's width), the pavement between lines from the paint-free texels
  beside the line R-ward of it. No squeezed texture, no diagonal bending a
  line, no paint where the model has none. Across a taper a line with a
  partner in the narrow layout eases to it (edge lines with edges, the
  centre group in order from the held edge, white lines by the side of the
  centre they are on, the narrow layout mirrored when the two arms run
  opposite ways through the node); a line with none ends where the taper
  reaches full width, on its own section. The layout is ONE function,
  `RoadProfiles.PaintLines`, which the painter now reads too (PNGs
  unchanged). The tap records a column span as its strips (`Span.strips`);
  CitySmooth reads paint from the strips and edges from the outer ones.
- **The squeeze, eased** (I7): the cut each side is sampled on a fixed
  lattice along the edge (every F/6 from its a end), max-filtered and eased
  over the class floor F, with sections through every ramp; a section never
  cuts less than it needs. After a tile build `LaneExtents` reads the drawn
  sections, so the audits probe the edge the tile drew.
- **Fans**: the arms' corners are the model's (off-centre where a lane was
  added); where the chord from one arm's corner to the next would cut back
  across the first metre and a half of a mouth (a corner further out than
  its neighbour's), the arm's edge line is carried back first.
- **Traffic, the AI, the grid, spawn and respawn** drive the lanes' centre
  (`LineModel.LanePoint`): the race path is built on it (tapers sampled),
  the route's start and finish arcs scaled onto its length.
- **The gate** reads the model: CitySmooth's design edges and lines are the
  line model's (`EdgeD`, `PlanOff`, `PlanExists`), A2 skips bins inside a
  taper (the centre pair is off the midline there by design).
- **Joins that are not a clean one-sided change.** Both edges step (the
  data centred a lane change on a node the builder mitres, a one-way
  leaving a two-way road): one edge is held - a one-way keeps its
  direction's half, else TAPR's fixed side, else the smaller step - and the
  narrow arm is shifted over to it on a MUTCD shifting taper; the arms
  stick out on opposite sides (the exporter re-anchored at a junction the
  builder mitres): the narrower is shifted by the least that makes one edge
  flush. Every ease and shift is capped at its run's room (the edges it
  runs on through, up to the next change): a class default of 90 m on a
  35 m I-277 piece ran into the next join and left 2-4 m steps there.
- **T-junctions.** A column span's vertices lie ON the next span's single
  quad edge, and a ray down exactly on a section line fell through the
  hairline (the drive audit's probes land there). Column spans reach 2 mm
  past their sections, coplanar.
- **Diagnostics**: `Editor/LineModelProbe.cs` (PSX_LM_NODES, PSX_LM_EDGES,
  PSX_LM_PTS: a node's arms and fan corners, an edge's model and sections,
  what a ray meets on a built tile) and CityAudit's LINE MODEL block.
- **Measured** (CityAudit LINE MODEL): symmetric widenings at mitred joins
  (a lane added or dropped, both edges moving more than V) **0 of 14,182**
  (core 0, routes 0; 545 before the held/crossed/split rules, all joins
  symmetric before WP-11b); merged carriageways (opposite one-ways of one
  road drawn under 0.3 m apart, 60 m clear of their ends) **0 m** in the
  core and on the routes; through-lane continuity: 636 lines still jump
  more than V at a join (core 62, routes 0) - mostly profile shoulders
  (a one-way's yellow edge line 6 cm off a two-way's double yellow where a
  divided road begins; an expressway's 2.4 m outside shoulder against a
  street's 0.3 m) - open. 9,005 edges drawn off their OSM line (1,150 km),
  6,529 one-sided tapers, 1,374 median tapers at 891 splits, 85 held and
  59 crossed joins, 9 mirrored offsets put right (5 left).
- **Audits**: DRIVE AUDIT zeros, CITY AUDIT OK. The line model fixed the
  named spots it was handed (face-kings-9677, lip-caldwell-11145, and all
  nine KnownLaneSolids from WP-04/10/11 but davidson-14102) and moved
  others: named for WP-14 in the audit's lists with notes (five roadside,
  nine lane solids and one lane land, three fan mouths at East Morehead
  Street). Three are on race routes, as i277-us74-2321 was before: I-277 at
  US 74 (the two drawn 0.3 m into each other before the merge, 0.8 m apart
  in height), I-277's deck over its exit ramp e175 (TAPR carries I-277 a
  lane right of its line there), and the Independence Expressway deck
  beside Albemarle Road's. Tile build p95 98.6-112 ms against 100.1 (three
  other editors were running); the uptown tile 416 -> 1,004 road vertices.
- **The moving side's edge line rides its edge** (partner = the narrow
  layout's shoulder in from the moving edge): anchored on the fixed side it
  jumped 2.6 m where a taper ran on through a joint whose OTHER side
  changed (I-277 e2438/e2435).
- **The smoothness gate** (CitySmooth FULL, every road tile; BEFORE the
  WP-10/11 baseline d854759 -> AFTER; runs, metres): A1 OFF 76,893 / 936.6
  km -> 6,069 / 9.8 km (paint off its design line); B1 JITTER 170,834 / 26.6
  km -> 8,715 / 4.1 km; B2 KINK 98,596 / 742 km -> 44,470 / 114 km; A2 SKEW
  15,748 -> 48; A3 INSET 12,907 / 335 km -> 331 / 3.5 km; A4 LINEWIDTH
  26,538 / 274 km -> 570 / 0.7 km; A5 STRAY 15,223 / 160 km -> 635 / 1.8 km;
  A5b MISSING 23,883 / 244 km -> 6,442 / 98 km; B4 JUMP 24,768 -> 3,344; B4s
  SEAM 2 -> 0; C1 GAP 102 -> 27; C2 END 14,616 -> 6,983. Up: B3 CURVE 2,579
  / 5.4 km -> 2,984 / 6.5 km, C3 DASH 31 -> 102 (dashes do not end on a dash
  boundary yet), D1 CROSS 2,964 / 8.2 km -> 5,760 / 9.8 km (paint over
  another pavement: ribbons moved off their lines into neighbours that are
  not squeezed - at two heights, or host and branch). **The creek pin
  (ways 1078015030, 16671358, 1252904925) reads no violations.** Baseline
  re-recorded with -AllowLoosen (the gate's reference and tap moved with
  the builder: its keys cannot be compared across the move; B3, C3 and D1
  the looser ones). The worst runs left: the edge line on a squeezed side
  (it moves in with the edge now; the gate's plan still expects it at the
  model's line), I-277 beside its other carriageway, John Belk's inset.
- **Play**: city-play-check (-NoWatch: a visible editor stops at the
  window-layout dialog in PSXCity) 2 failures, the same two as before -
  WP-14's drive-off stage (now at I-277 e1499 s=165; before at I-77 e1891)
  and its fills-and-cuts count; its "on a street" reads the line model's
  pavement now (the grid stands on the lanes' centre, 6.3 m off I-277's OSM
  line). Races (150 s, seed 0): all three run the whole 150 s; Uptown 2
  retired (traffic, as before), Tryon 1 (a lamp post; 3 before),
  Independence 2 (car contact with the player's autopilot; 1 before).
  CityPreview's driver's-eye shots stand on the lanes' centre.
- **Not done here**: linecheck's builder replica (lib/linesim.mjs) still
  models the pre-11b symmetric taper - the offline gate reads the same
  data but not the new builder; the gate's plan for an edge LINE on a
  squeezed side (it moves with the edge now) still expects the model's
  unsqueezed line (A1 OFF there); dashed lines do not yet end on a dash
  boundary (C3); real lane widths (LANW) not flipped (3.6576 m kept).


## WP-07 (2026-09-29): the draw-call prepay, the city kit, the lamp metal

Nothing new to see: this package pays for the trees, poles and signs that
come next. Branch `charlotte-scenery`.

**The city prop variants** (`Editor/CityPropBaker.cs`, run by every
`BakeCityProps`). The streamed city stands up a cheaper copy of the four
props that cost it the most draw calls; everything else (the Emerald Isle
beach town, the house, town and seller scenes) keeps the full prefabs. The
copies live in `Resources/CityProps/City`, and `CityProps.CityPrefab` hands
them to `CityWorld` (and to `CityPreview`).

| Prop | Draws before | City variant |
|---|---|---|
| `house_simple` | 13 | 1 |
| `trailer_00` / `_02` / `_05` | 9 / 10 / 10 | 1 each |
| `burger_drive` | 355 | 37 from anywhere outside it |
| `pizzeria` | 396 | 24 from anywhere outside it |

- **Houses and trailers: one atlas each family.** The pack materials TILE
  (a wall's UVs run to 8), so a plain atlas cannot hold them. Every vertex
  carries its texture's cell in its COLOUR (texel corner and side in a
  256 px sheet), and PSX/Lit's `PSX_ATLAS_RECT` variant wraps the UV inside
  the cell with `frac()`. Point-sampled with no mips, the wrap is exact. The
  cells are the pack textures themselves, resampled from their source files
  (64 px, the busiest one 128 px); nothing is drawn. The foundation skirt
  is folded in. `Art/City/Props/city_house_atlas.png`,
  `city_trailer_atlas.png`.
- **Two traps, both caught by the first render.** Medium mesh compression
  quantises vertex colours to SIX bits, which moved every cell off its
  texels (black seams down every repeat): meshes that carry cells are never
  compressed, and the bake reads the cells back off the saved asset. And
  the pack textures ship to WebGL as RGB565 (`ReleaseBudget`), which samples
  without the sRGB decode; an atlas left at 24-bit sRGB drew every house a
  fifth darker. The atlas takes the same WebGL override. After both, the
  comparison sheet (`CityPropBaker.CompareShots`,
  `Screenshots/City/props_compare.png`, full prefab beside variant from two
  corners) measures a brightness ratio of 1.000-1.001 on every prop, and a
  mean per-pixel difference of 2-3 levels in 255 (the 64 px cells).
- **The restaurants keep being places** (plan critic C1). The shell is merged
  by material (21 and 20 materials); the door leaves keep their own
  renderers because they swing; every piece keeps its collider (248 and 71,
  the same count as the full prefab); the order bay is untouched. What is
  ROOM is decided by looking: rays from 24 bearings at 1.2, 3 and 7 m and
  from above, to each piece's bounds; a piece no ray reaches without passing
  another piece's collider goes behind `CityPropInterior`. The city props
  wear opaque windows, the pickup window too, so the room can be seen only
  from inside the building or through a door standing open, and those are
  the only two things that switch it on: the camera or the player within
  1 m of the room's box (the room pieces' own bounds, baked as `hull`; off
  again past 2 m), or any of the building's hinged doors off its stop
  (checked every frame, so the room is there the frame a leaf moves; a
  door opens for the car within 3.4 m of it).
- **The first cut switched on by distance, and the review caught it.** It
  drew the room whenever the car or camera was within 40 m of the lot, and
  the probe measured in edit mode with the room baked off. Its restaurant
  eyes were 19-32 m from the lots, so the 342 and 429 saved draws described
  a state the game never showed there: driving past on the fronting road
  the burger cost 307 draws against 355 and the pizzeria 392 against 396,
  and up to 280 room pieces went back into the sun map. The probe now
  applies the runtime rule at its eye (`CityPropInterior.Apply(eye, car)`,
  doors included), and adds a second eye per restaurant: the chase camera
  of a car stopped where it orders (`CityPropBaker.BayStop`: the
  drive-thru's lane beside the menu board, the pizzeria's kerb; the
  pizzeria's bay box is centred INSIDE the shop).
- **Proved by rendering, not by argument.** `CompareShots` now also writes
  `Screenshots/City/room_check.png` and renders each restaurant with the
  room off and on, counting the pixels that change. From 32 street eyes
  round each lot (1.2 and 3 m up, 23-27 m out) at most 4 pixels in 144,000
  change (the burger; 0 for the pizzeria). From the bay's chase camera and
  the driver's seat looking at the building, 0 change for both. From inside
  the building, 18% and 83% change, so the test can see a room when there is
  one. The rule lights none of those outside eyes.
- `city-play-check` runs both restaurants: stopped at the bay ORDER is
  offered and the room stays off (door shut); the camera put inside the
  building draws it and taking it back out behind the car removes it; a car
  pulled up to a door swings it open and the room is drawn through it; 70 m
  off the door shuts and the room goes, with the lot still standing.
  (Charlotte has no getting out of the car; the walk-in rooms keep their
  colliders and doors for when it does.)

**The city kit** (`Scripts/City/CityKit.cs`, `Resources/CityKit.asset`).
Every city runtime material in one Resources asset: the 96 slot materials,
the lamp posts, the shaders they use (so WebGL keeps them), and empty
places for WP-08's trees (one per season dress), WP-15's furniture atlas,
WP-17's markings and WP-23's sign faces. `CityWorld` reads it; the scenes
no longer serialize a materials array (`CityWorld.materials` is
`[NonSerialized]`, for tools only). `PSXRacingBuilder.EnsureCityKit`
rewrites it from `CityMaterials()` at every city scene build and every
city tool run. **From here a new city material is a kit change, never a
rebake of the four city scenes.** The four were rebuilt once here with
`tools/city-rebake.ps1` (the city scenes and the prop variants only, about
two minutes warm; it leaves the full build's log alone).

**The lamp posts** wear pack metal: the house pack's `Metal.jpg` (already
shipped with the house), UVs a metre a repeat, tinted so the posts land on
the dark weathered tone they had as a flat tint (worked in linear light:
the texture is RGB565, sampled without the decode).

**Measured** (`city_budget.txt`, which now runs every site twice, with the
variants and with the full prefabs, and adds the first drive-thru and the
first pizzeria, each from its fronting road and from its order bay; the
room switch applied at every eye, and it draws no room at any of the four):

| Site | Most draws saved in one view |
|---|---|
| suburb 6 km (Randolph Rd) | 88 (target 20+) |
| Providence Rd | 93 |
| Dilworth | 24 |
| burger lot, from its road (22.8 m from the room) | 342 |
| burger, stopped in the order bay (2.8 m) | 328 |
| pizza lot, from its road (19.3 m) | 429 (target 300+) |
| pizza, stopped at the kerb (5.2 m) | 408 |

Sun-map casters at the burger lot fell from 346 to 57 (the room's pieces
cast only while it is drawn). The busiest view in the city is unchanged
(200 draws, uptown). Tile build p95 over the nine sites: 69.0 ms against
WP-04's 68.4 (+1%, inside the +15% ratchet), and 68.9 ms for the same
tiles in the same run with the full prefabs (the first measurement, taken
with 3-4 other Unity jobs on the machine, read 75.8 against 72.9: only the
A/B inside one run means anything while the machine is shared). The probe now
also prints each site's prop stand-up time, with every prop instantiated once
both ways before it starts (the variants carry the full prefabs' own
meshes and colliders, so whichever pass ran first paid their first load and
MeshCollider cook): a restaurant lot 10.5 / 12.7 ms with the variants
against 9.2 / 13.7 ms (burger / pizza lot, within the run's noise); 25
suburb houses 5.4 against 2.0 ms (about 0.1 ms a house, left for now). Map heap 18.3 MB (18.2 at WP-04). WebGL.data 81.40 -> 81.67 MiB
(+0.27, the package's budget 0.3).

**G-web.** A local WebGL build of the branch (`build-and-publish -SkipScenes
-SkipDeploy -PagesDir city` after a full scene build, GUID audit OK, served
from 127.0.0.1): FREE ROAM CHARLOTTE loads (`[City] parsed in 116 ms,
elevation solved in 470 ms`, `buildings placed: 5625 (+10 restaurants ...)`),
no console errors, no missing-kit or missing-variant warning; the build log
shows PSX/Lit compiled for gles3 with both variants (plain and
`PSX_ATLAS_RECT`).

**A sandbox trap met on the way.** 221 of the committed `.mat.meta` files in
`Materials` have no `.mat` beside them (the scene build makes those), so every
sandbox mints its own GUIDs for them. Copying `Materials` from a fresh
worktree (every mtime new, so `/XO` lets it all through) put the source's
GUIDs back over 219 of them and broke every prefab and scene that used them;
`city-rebake.ps1` does not copy `Materials`, and a full scene build puts a
sandbox right.

## WP-08 (2026-09-29, release R3): the trees

The owner, after driving it: "its all so barren and flat". The city had not
one tree; now each 256 m tile plants the trees the present-day canopy map
says it has, a typical tile 150-300 and a wooded one 400. Branch
`charlotte-scenery`.

**The canopy map** (`Resources/charlotte_canopy.bytes`, PCAN v1, its own file
so the lines release can re-export the road data without touching it). The
USDA Forest Service's NLCD Tree Canopy Cover, v2025.6, the 2024 layer (the
owner's PRESENT DAY; public domain, credited on the CREDITS page and in
`LICENSES.txt`). `tools/city/fetch/fetch_canopy.mjs` asks the USFS image
service on the Interagency Imagery Portal for exactly that raster over the DEM
box, in its own Albers projection and pixel lattice, uncompressed, and caches
it (4.4 MB, gitignored). `tools/city/canopy.mjs` averages it onto the DEM's
own 60 m lattice (4 x 4 samples a cell, `lib/canopygrid.mjs` projects each to
Albers, checked against the ArcGIS geometry service to 0.9 m) and stores it in
4-point steps: 743 KB raw, **393 KB Brotli** (536 KB in whole percent).
`--check` rebuilds it byte for byte. The box averages 39.8% canopy, the 8 km
core 23.8%, the square kilometre round uptown 5.1%. `CityCanopy` reads it.

**What is already beside the road** (`Scripts/City/RoadsideOccupancy.cs`): a
2 m mask per tile, built once, that every roadside object asks before it
stands (the plan's one occupancy mask; poles and signs take their places from
it in later packages). It reserves the pavement and the clear zone past it
(freeway 9 m, trunk 6, arterial 3.5, collector 2.5, local 2, a ramp 4.5), each
junction's fan, at every corner of a real junction a sight triangle with 10 m
legs along the two kerb lines and a furniture spot behind the corner, the
real footprints, the procedural and prop lots (a restaurant's with 10 m of
lane, bay and parking round it: the first plant put a trunk in the
pizzeria's order bay, which the budget probe's bay camera found), the fill
houses, the lamp feet, creeks and lakes, and the ground under every deck.
Every mark is widened by half a cell's diagonal, so any point of a free cell
is clear. **The road's edges are read in one place**, `RoadEdgeAt` (the
centreline and drawn half width at an arc position): the lines release
replaces that one body, and everything placed from the mask re-seats with
the lines. 1.0 ms a tile in the editor.

**The trees** (`Scripts/City/CityTrees.cs`). Each 16 m square of a tile
plants canopy x 256 m2 / 95 m2 (the plan's crown) trees on the free cells of
the mask, never two trunks within 6 m; what the road squares cannot plant is
planted in a second pass on free ground within 14 m of a carriageway (the
canopy the map shows over a street is the crowns of the trees beside it);
along every grounded freeway two rows of hardwoods stand just past the clear
zone where the canopy map has woods, a tree every 7 m, out of the share of
the square they stand in (the tree walls). Every choice is a hash of the
square's (or the freeway station's) global index, and a square belongs to one
tile: the same trees on every build, none twice across a seam, and nothing
depends on which tile was built first. Species by place: willow-oak rows in
the old city (inside 4.5 km of uptown), 45% pine in the suburbs past it,
crape myrtles along the commercial arterials, sycamores by the creeks, now
and then a bare one. They wear the stage forest's five season atlases (the
owner's CC0 retro tree pack, already shipped: 0 MB), one material per dress
in the city kit, chosen by the calendar. Broadleaf 11.5-16 m (a crown of
about 118 m2: at 95 m2 the crowns, which overlap where they are planted at
random, drew 5-7 points less canopy along the roads than the map has), pines
15-22 m. Two crossed cards a tree, one mesh a tile on the Foliage layer: one
draw and one sun-map caster a tile. Phones plant 60% (the owner's lower
tier, `Application.isMobilePlatform`).

**No leaves in a lane.** A third of the billboards paint foliage to the
ground. The kit measures each atlas cell off the five PNGs (how far the
painted tree reaches out from its trunk below each twentieth of its height,
the widest of the dresses), and a tree whose card could reach a road is
turned to 45 degrees to it and grown, shrunk or given another billboard of
its species until what it paints below 4.2 m over EVERY road near it stays
0.4 m off the pavement; failing that it is stood back by as much. The first
version trusted the stage forest's "foliage starts at 28% of the height" and
the first shot of Dilworth Road put an orange maple across the lane at eye
height.

**Big trunks stop cars; small trees break away** (Q15: the plan's default,
which the owner kept - trunks of 30 cm and up solid, small trees breakaway).
A tree's trunk diameter comes from its height (an open-grown hardwood about
4 cm a metre less 15, a pine grown in a stand 2.5 cm a metre less 12: a 12 m
willow oak 33 cm, a 15 m pine 26 cm); crape myrtles (a clump of thin stems),
dead snags and the young understory trees break away whatever their height.
A trunk of 30 cm or more within 25 m of a carriageway is solid, and goes to
the city's trunk table, `CityWorld.Trunks` (the plan's
`TreeTrunks.AddTable/RemoveTable`): each tile hands its trunks over when its
trees are planted and takes them back when it is dropped, and the table
stands a capsule (4 m tall, radius off the card, on objects named
`TreeTrunk`) only in the 40 m cells round each car, one cell a physics step,
exactly as a stage forest's. A tile build stands no trunk collider at all.

**Not in race run-off** (`Scripts/City/RaceRunOff.cs`, plan section 5).
Along the Uptown Loop, Tryon and Independence routes the mask keeps 8 m
past the drawn edge clear on both sides, and 16 m on the outside of every
bend (the heading turning 12 degrees or more over 32 m), worked out once
from the routes' edge chains with the half width read through `RoadEdgeAt`,
and marked as clear zone. An arterial's own clear zone (3.5 m) is sized for
a driver at the limit, not a racing car running wide.

**On a frame of their own** (WP-09's third stage, for the trees only). A
tile's trees are not planted in its build: `EnsureTile` queues them with the
lattice its build cached (`CityMeshes.TakeLattice/PutLattice`, so they stand
on the same triangles without GroundY at every corner again, which doubled
the planting), and `CityWorld.Update` plants the nearest waiting tile on a
frame that built no tile. A tile a car is in plants at once, and
`EnsureRing` (spawns, race grids, the tools) plants its ring before it
returns. The FPS overlay's CITY line counts a tree frame like a tile build.

**Checked:**

- **TreeAudit** (in `CityAudit`, and alone as `CityAudit.RunTrees`, about a
  minute): 514 tiles (the plan's shot spots with their neighbours and every
  9th tile of the network), 126,257 trees. Against the geometry measured
  again, not the mask: 0 trunks on pavement, in a clear zone, in a fan, in a
  building, lot or fill house, in water, under a deck or on a reserved cell;
  0 cards painting leaves over a road below 4.2 m; 0 outside the tile that
  planted them; the same trees on a second build. Planted against what the
  canopy asks: median 1.00, 483 of 490 tiles within +-20% (the lowest, 0.46,
  a tile that is mostly water). Canopy within 25 m of the centreline by
  class, the crowns against the 30 m map over exactly the audit's sample
  points (`canopy.mjs` prints the truth table): secondary 9.8% against 13.0,
  motorway 11.4 / 14.9, core arterials 6.2 / 8.1, core residential 28.8 /
  31.0, every one within the plan's 5 points. (The shipped 60 m grid reads
  15.2 / 23.9 / 8.9 / 31.4 at the same points: its cells smear the woods
  beside a freeway over the freeway.)
  After the review: 614 tiles (the 514 and the 100 tiles the three race
  routes run through), 136,186 trees; 0 in a route's run-off (measured
  against the route marks); Q15 holds for every tree (11,239 solid: oak
  5,093, hardwood 3,946, pine 1,945, sycamore 255; no crape myrtle or snag;
  3,783 within reach of a road break away); the canopy bands unchanged
  (9.9 / 11.4 / 6.2 / 28.9 against 13.0 / 14.9 / 8.1 / 31.0).
- **city-play-check** drives the real car at the trunks nearest Queens Road
  West in Myers Park, dead on and 0.9 m to the side at 50 km/h: all eight
  runs stop the car (it arrives at 52-53 km/h and leaves at 0-10), none
  with the trunk inside its body. "Inside" is judged against the car's own
  collider (a 1.45 x 1.02 x 3.20 m box), which the check now reads off the
  car: the stage harness's generic 4.1 m box put the two thinnest trunks
  "inside" a nose the car does not have. The first runs found a trunk in
  the pizzeria's order bay (hence the lot margin) and the run-up raycast
  starting under the city's ground (world y is 97 m ASL down). Since the
  review it takes its trunks from the table and checks each was stood once
  the car was beside it: 8 of 8 stopped (52-53 km/h in, 0-10 out), 52
  capsules standing round the car where 123 solid trunks are within 150 m.
- **race-play-check on the three city routes** (review; G-play): 5 seeds a
  route, each raced with the trees and without (`-Venues ... -Seeds 0,1,2,3,4
  -Trees ab`, 30 races of 150 s in one launch, about 75 minutes). **No car
  hit a tree trunk in any of the 30** (hits into `TreeTrunk` counted from
  6 m/s). Rivals retired, trees on against off: Uptown Loop 7 / 8, Tryon
  13 / 11, Independence 5 / 5, all 25 / 24 of 45 starts. The two runs of a
  seed are identical until something differs in frame timing (a tree frame
  shifts what the traffic draws), then diverge: Tryon's two extra were a
  rival into traffic at 77 s and, in seed 4, a different chain after a car
  contact at 18 s - neither near a tree. What does retire them is older than
  the trees: 62 hard hits into other racers, 57 into traffic (most in the
  first 30 s: C18's grid clearance), 33 into lamp posts on Tryon (poles in
  race run-off: plan section 5, the lamps' own package), 5 into barriers.
  19 of the 30 races fail the check's own "at most one rival retires",
  with trees and without alike.
- The DRIVE AUDIT's five zeros and the roadside audit's zeros are unchanged
  (the trees are not in the tiles the drive audit stands up; they stand on
  their own in EnsureTile).
- Reference spots against WP-07 (`Screenshots/City/ref_wp08`), and the
  trees in all five dresses at three spots (`Screenshots/City/trees`,
  `CityRefSpots.RunTreeDresses`).

**Measured** (`city_budget.txt`; its A/B builds each site with trees and
then without, back to back).

The first version planted the trees and stood their capsules inside the tile
build: the trees' own share of a tile was 2.6-4.9 ms at p95 by site, and
8.0-11.2 ms in the old city (Tryon, Dilworth, Plaza Midwood), about 3 ms of
it `AddComponent<CapsuleCollider>`, against the plan's +1.5 ms. Its headline
"tile p95 81.8 -> 82.1 ms" was one of three noisy A/B runs (the others read
+14% and +24%), and the baseline was moved from WP-07's 69.0 ms to 82.1 on
it. The review's answer:

| | with trees | without (same run) |
|---|---|---|
| tile build, all 225 tiles | p50 23.3, p95 73.2 ms | p50 23.8, p95 78.8 ms |
| the trees' own frame, all tiles | p50 2.1, p95 4.9, max 13.6 ms | - |
| the trees' own frame, p95 by site | 2.3-3.3 ms (freeways, suburbs, the restaurant lots); 4.6-4.8 (uptown, Tryon); 5.5-6.5 (Dilworth, Plaza Midwood) | - |
| draws in a view | +7 to +12 (one a tile in view; the most at Plaza Midwood) | - |
| sun-map casters in the ring | +25 (one a tile) | - |
| colliders on the tiles | +0 (the table stands the trunks round the cars: 52 capsules at Myers Park, where 123 solid trunks are within 150 m) | - |
| worst view | 209 draws (uptown) | 200 |

The tile build no longer carries the trees at all (same code with and
without: the 5.6 ms between the columns is the machine, shared with other
Unity jobs), so the plan's "tile p95 +1.5 ms at most" holds by construction,
and the tile-build ratchet stays WP-07's 69.0 ms. The trees cost a frame of
their own, never the same frame as a tile build, at 5.5-6.5 ms at p95 in the
old city: the planting was also cut (the fit test stops at the first road a
card would hang over and skips a road the leaves cannot reach, the kit's
table is read once a tile, the second pass looks only 14 m for a road, the
normals are handed over): at Dilworth planting went from 7.2 to 5.3 ms at
p95, and the trees' whole cost from 10.3 to 5.5. The in-view
draws are past the plan's +10% ratchet at the sparser sites (Beatties Ford
27 -> 36), which WP-07's prepay was for (88 draws saved in a suburb view,
300+ at a restaurant). The phone reading (FPS overlay CITY line at Queens
Road West, critic C30) is the owner's to take; the line now counts tree
frames too.

**Size.** `charlotte_canopy.bytes` +393 KB Brotli (the plan's 0.3-0.5 MB);
the tree materials are 5 small .mat files over atlases the stages already
ship (the Build Report lists each `TreeAtlas*.png` once: not stored twice,
critic C34). `charlotte_city.bytes` changes only in its attribution (the
USFS credit line; every other section byte-identical). WebGL.data 81.67 ->
82.25 MiB (+0.58: the canopy grid as Unity compresses it, and the code),
the largest file 82.25 MiB, under the 95 MiB ratchet (`size-ledger.py` now
counts the canopy file with the rest of Charlotte's data).

**G-web.** A local WebGL build of the branch (`build-and-publish -SkipScenes
-SkipDeploy -PagesDir city`, BUILD OK, GUID audit OK), served from
127.0.0.1: a new career on 1 January (the SNOW dress), out of the drive to
the end of the street, FREE ROAM CHARLOTTE loads (`[City] parsed in 108 ms,
elevation solved in 365 ms`), no console errors and no missing-canopy or
missing-kit warning, and the trees stand along South Tryon in their snow
dress. The CREDITS page shows the USFS line without clipping.

## WP-13 and WP-14 (2026-09-29, release R5): the 30 m ground, and the land graded to the road

The owner, after R1: the hills read as a road on a flat strip. They did.
`CityElevation.Ground` held every grounded road's land flat for 11.5 m past
the pavement, then blended it to the DEM over 26 m more. On a hillside, that
is a shelf 23 m wider than the road. Branch `charlotte-roadside`.

**WP-13: the ground on a 30 m grid.** USGS 3DEP 1/3" is averaged over 30 m
cells: 1621 x 1831 nodes, which are the old 60 m lattice's nodes plus the
midpoints between them.
- **Storage.** `charlotte_dem.bytes` is PDEM v3 (`tools/city/lib/pdem3.mjs`):
  - the v2 header, then 1 km blocks of 32 x 32 cells (33 x 33 nodes, one
    node shared with the next block), a block index, and per node a zigzag
    varint residual against a planar prediction;
  - decimetres above the 97.0 m datum, as before;
  - 3.17 MB raw, 2.04 MB Brotli (the 60 m grid was 1.49 / 0.78, so
    +1.26 MB against the plan's +1.9).
- **Why no deflate.** The plan's deflated blocks would have needed an
  inflater proven on IL2CPP WebGL. The build's own Brotli compresses the
  residuals better (2.04 MB against 2.14 MB for raw deflate), and the
  reader is plain C#.
- **Reading it.** `CityElevation.LoadDem` keeps only the compressed bytes.
  `BaseY` decodes a block the first time a query lands in it, into a
  128-slot cache (279 KB). A cell never straddles two blocks. v1 and v2
  files are still read. The DEM holds 3.3 MB, against 1.4 MB for the
  60 m array.
- **The roads still read a 60 m grid.** `BuildRoadDem` rebuilds it from the
  30 m nodes with [1/4, 1/2, 1/4] weights each way, then applies the same
  0.8-cell Gaussian.
  - The transient array stays 3 MB, where a 30 m road grid would be 12 MB.
  - A 60 m cell's own pixels cannot be recovered from 30 m cell means, so
    the roads' ground moved 0.17 m RMS (p99 0.54 m). The spawn seats moved
    by 0.09 m at most.
  - `metrics.mjs` emulates this through `citydata.roadGridSteps`.
  - The canopy map stays on the 60 m lattice: `canopy.mjs --check` is
    still byte for byte.
- **metrics.mjs, 60 m grid -> 30 m grid:**

  | Measure | 60 m | 30 m | Plan |
  |---|---|---|---|
  | Core relief kept | 0.77 | 0.90 | >= 0.85 |
  | Core slope p90 | 7.2% | 9.4% | >= 9% |
  | Core RMSE against 3DEP 10 m | 1.01 m | 0.74 m | <= 0.6 m (**not met**) |

  The core RMSE carries a 0.32 m bias that is identical on both grids, so it
  is the truth's registration, not the grid. Without it the RMSE is 0.67 m.

**WP-14: roadside sections** (`RoadsideRules`, the city block). Each side of
a grounded road now takes its road's section:
- **Bench.** The land stays at the road's pin (the tarmac less the 10 cm
  sink and the sag allowance) across the bench. Freeway or expressway 8 m,
  arterial or ramp 4.5 m, local street 2 m (`CityBenchM`).
- **Fill.** From the bench the land falls at 1V:4H to the natural land
  (`CityFillSlope`). This is the road's FLOOR, and the highest floor holds,
  so an upper road's verge is never graded down onto a neighbour's ledge.
- **Cut.** The land stays at the pin across 8.5 m, then climbs at the 1V:3H
  back slope (`RoadsideRules.BackSlope`, which the city now uses) to the land.
  - 8.5 m is the 8 m lattice's width, not a design width (`CityCutBandM`).
  - A lattice triangle whose far corner is d metres out and whose near
    corner is under the pavement crosses the edge with at most
    (11.31 - d) / 11.31 of that corner's rise. With the rise (d - 8.5) / 3,
    that is never more than 6 cm, inside the 10 cm sink.
- **Cap.** No grass through a lower road's lanes: the lowest cap holds. The
  cap is the pin across the band, the back slope out to 11.31 m, and nothing
  past that.
- **Bank (fix round).** A section runs at its own slope until it meets the
  land. One too deep to meet it by its REACH (`CityElevation.SectionReachM`:
  where the ground query stops seeing the road, 50 m less the road's edge,
  at most 44 m) steepens to a 1V:2H bank (`RoadsideRules.CityBankSlope`)
  for its last stretch, anchored on the land at the reach, and ends exactly
  there. `SectionFill` / `SectionCut` are the one definition; Ground, `InCut`
  and the probes read them.
  - It replaced a smoothstep fade over 20-32 m, which the review worked out
    at 1V:1.2H for a 10 m fill and 1V:1.3H for a 10 m cut, steeper than the
    old 26 m blend.
- **The pavement edge** is read through one accessor,
  `CityMap.Edge.PaveEdgeM(at, side)` (with `SideOf` / `SideAt`): the
  section, `InCut`, the land and ground probes and the play check's
  run-off. Today it is half the width either side; R4 (plan A8, a lane
  added on one side) replaces its body.
- **The verge falls at 1V:4H** past its shoulder (it was 1V:6H across the
  clear zone). That is the fill's own slope, and its search runs 12 m, so it
  comes down onto a fill whose lattice chord sags under the section.
- **Retaining walls** (`InCut`, freeway outside edges) stand only where the
  graded cut cannot fit. The DEM guard must pass (2 m at 4, 8 and 12 m), and
  then either even the 1V:2H bank would have to start inside the band (over
  the road's own level), or a road more than 2 m above, or a building,
  stands in the slope before it meets the land. The audit prints wall
  metres by cause.

What the land does now, from `Editor/CityLandProbe.cs`. The same file runs
on both codes; grounded non-ramp edges, every 10 m, both sides. "Flat" means
the land is within 0.25 m of the road; "kept" is the share of the natural
rise or fall left after grading.

| Where | Measure | City-r1 (3f27ead) | R5 |
|---|---|---|---|
| 8 named streets, 124 km | flat at 10 m past the edge | 88% | 42% |
| 8 named streets, 124 km | kept at 10 m | 0 | 0.28 |
| 8 named streets, 124 km | kept at 15 m | 0.20 | 1.00 |
| 8 named streets, 124 km | kept at 20 m | 0.50 | 1.00 |
| The three routes, 17 km | flat at 10 m | 80% | 41% |
| The three routes, 17 km | kept at 15 m | 0.23 | 0.39 |
| The three routes, 17 km | kept at 20 m | 0.49 | 0.70 |

Within 5 m of the edge nothing moved. That is the bench, and on the uphill
side the lattice's band.

**The city audit** (PSXShip, `city-cycle`): CITY AUDIT OK, the DRIVE AUDIT
zeros held.
- Roadside audit green: 198.6 km of verge, 43.2 km of rail (43.5 before),
  pits 8360 -> 4132, every one with a rule.
- Cut walls on the 118 roadside tiles: 1.07 -> 0.41 km (the plan: at least
  50% less). By cause: 210 m where the back slope cannot reach the land,
  188 m where a road above stands in the slope, 10 m for a building.
  - Fix round (the 1V:2H bank): 0.32 km, all of it a road above in the
    slope; no cut on the audited tiles is too deep for the bank.
- The land beside the routes, |land - road| p90: 3.81 m at 30 m and 5.57 m
  at 60 m, with 40% of points more than 2 m off the road at 60 m. That meets
  the plan's table with critic C22's 30 m target, now a check.
- The known roadside spots went from 6 to 2 (3 before the fix round):
  - four are graded away (the ramp e1489's face in its cut, the lip at e9314,
    the ledges on North Caldwell Street and on ramp e2858);
  - two are deck-rail gaps;
  - one was new with WP-13: a 0.49 m ledge off I-77 (e2739) beside the ramp
    e5219. It was first listed for R4; the review refused that, and the fix
    round FIXED it in the builder. The ramp runs 3.7 m off I-77 and
    0.98-1.00 m above it. `Ungraded` railed a verge stopped by a road more
    than `OpenDropM` (1.0 m) below, so the ramp's side flickered between a
    rail on a retaining face (1.00 m up) and a graded connector (0.98 m up),
    and the connector's end stood proud beside the wall. A connector that
    reaches the road below at no more than the traversable 1V:3H is now
    graded ground whatever its depth (`TraversableConnector`): the ramp's
    side is one graded slope. The list is down to 2 (the deck-rail gaps);
    rail on the audited tiles 43.2 -> 42.9 km, verge 198.7 -> 199.0 km.
- Two WP-13 leftovers were fixed in the builder:
  - Bryant Street's rail on a retaining face over a graded creek bank: the
    verge now falls at 1V:4H.
  - e5252's 0.29 m step to e9986's pavement, which was a 0.32 m ledge onto
    e9986's verge: `Ungraded` now measures to the neighbour's verge, and the
    edge takes its rail.

**Budget.** `CityBudgetProbe` was run back to back on city-r1 and on R5,
under the same machine load:
- Tile build p95 125.4 -> 90.4 ms, and 89.6 -> 78.8 ms without trees. Under
  that load this is noise; in any case there is no increase.
- Draws unchanged; worst view 209.
- Map heap 18.8 -> 19.2 MB. This reading swings about 3 MB between runs of
  the same code, because Unity's conservative collector keeps or drops the
  solve's 3 MB road grid.
- The DEM: +1.9 MB.

**Play.** `city-play-check -Edition CITY -NoWatch` passes (PSXShip).
- **New drive-off stage.** A car leaves a race route at 25 m/s and 15
  degrees onto the graded roadside. Spots are found from the solved ground,
  only where the road's own section grades the land, with nothing solid in
  the run. Five spots ran: 2 fills, 1 cut and 2 level verges, on I-277 and
  North Tryon. Across the race run-off each one came through with no speed
  lost to a hit, stayed upright (up >= 0.82) and left no drop (at most
  0.4 m of air).
- **The first run found a real trap.** Beside I-277 (e1482) under South
  Boulevard's bridge approach, the embankment's fill held the land 2.3 m up
  12 m out, and I-277's cap ended at 11.31 m, so the car stopped dead on the
  step. The stage now leaves out spots where another road's floor or cap sets
  the land: two roads meeting like that is the rail warrant's business, not
  the section's.
- **WP-08's trunk runs, adjusted.**
  - Their run-ups must now be level (0.25 m across a 12 x 2 m strip).
    Graded banks rolled the car.
  - The off-centre run is held on its line at 0.6 m (it was 0.9 m, unheld).
    At 0.9 m, a 1.55 m car overlaps a 0.3-0.4 m trunk by only 0.13-0.23 m,
    so the drift across the run-up decided hit or miss. With the line held,
    such a hit glanced off at 12-16 km/h.
  - All four trunks tried stop the car, dead on and off-centre.

**Not shipped: a cut's bank from the shoulder.** On the uphill side the
land is still level for 8.5 m, because the lattice needs that band. The fix
is a verge that climbs from its shoulder straight to the lattice's tangent
point, over the band. It was built (in `SolveStrip`, asked for by
`EmitSide`), in three rounds:
- Round 1 put land over lanes at 16 DRIVE AUDIT probes. It climbed into the
  lanes of ramps a metre up, which `ClearRun`'s level-verge rule calls
  decks.
- Round 2 bounded it by the bend radius and kept it 20 m off edge ends and
  off clipped sections. It still failed the same way.
- Round 3 stopped it at every pavement, and the audit went green. The shots,
  though, showed fins where a bank cross-section meets a plain one: State
  Street's right side has a sawtooth face 30 m out.

So it was reverted. It belongs with WP-21's verge strips (swales, ditches),
which need the same five-point cross-section a bank-with-transition needs.
The code is in the session's scratchpad for that package, not in the branch.

**G-web.** The CITY player was built locally (`build-and-publish
-SkipScenes -SkipDeploy -PagesDir city`, after a scene build). Results:
- BUILD OK, GUID audit OK, WEBGL CONTENTS OK.
- `WebGL.data.unityweb` is 40.04 MiB. The ALL build it compares with
  (82.25 MiB at WP-08) grows by the DEM's +1.26 MB, well under the 95 MiB
  ratchet.
- Served from 127.0.0.1 and opened in the browser pane, FREE ROAM
  CHARLOTTE loads: `[City] parsed in 151 ms, elevation solved in 599 ms`,
  with no console errors or warnings.
- The solve is about 0.23 s slower than WP-08's 365 ms, because the 60 m
  road grid is rebuilt from the 30 m blocks at load.

Fills are 1V:4H from the bench, so the land 3.5 m past the bench is at most
0.9 m under the road. No fill is 2 m deep inside the clear zone, and WP-24's
list of fills needing a rail is empty by construction.

**The review's fix round (2026-09-29).**
- **Faces are measured now.** `CityLandProbe` walks the ground every metre
  to 48 m past the edge and counts 4 m stretches steeper than 1V:2H and
  steeper than the DEM there: "section" where this road's own fill or cut
  sets the ground, "two roads" where another road's floor, cap or deck does.
  Same file on both codes (city-r1 given only the accessor):

  | Where | Section faces, city-r1 | Now | Two-road faces, city-r1 | Now |
  |---|---|---|---|---|
  | Three routes, 3472 station-sides | 38 (1.09%) | 8 (0.23%) | 235 (6.75%) | 144 (4.15%) |
  | 8 named streets, 24718 | 24 (0.10%) | 1 (0.004%) | 11 (0.04%) | 7 (0.03%) |
  | Freeways, 84008 | 1129 (1.34%) | 529 (0.63%) | 3697 (4.40%) | 2448 (2.91%) |

  The section faces left are one kind: a cut too deep to grade by its reach,
  where the bank from the land meets the cap's 11.3 m limit (the lattice
  may not rise within a triangle of the pavement) - I-85, I-77 and I-485
  cuts 6-7 m deep with the hill still climbing past 36 m. `InCut` walls such
  a cut at the edge; the step at 11.3 m behind the wall stays grass until a
  retaining wall can stand there (not in this package). The two-road faces
  are floor/cap conflicts between roads a level apart, which the rail
  warrant guards.
- **The plan's shots, before/after** (`Editor/CityRoadsideShots.cs`, spots
  found in the data and written to a file the before run re-reads): the
  deepest I-485 cut south of the city, the deepest I-77 fill, the deepest
  I-277 fill north of uptown (Brookshire), Providence Road at its creek, and
  three streets across a hillside (Andrill Terrace, West Tremont Avenue,
  Runnymede Lane). Seat, over-the-bank, and over-the-bank with trees off.
  **The visible payoff is small.** From the driver's seat the pairs differ
  by 10-20% of pixels; the one change a driver sees at once is the I-485
  cut's retaining wall gone (a graded bank now). From over the bank the
  fills fall away sooner and deeper (I-77: -0.6/-1.8/-3.1 m at 10/15/20 m
  against -0.2/-1.0/-2.4 m), but the land's shape reads much the same. The
  cut side is still level for 8.5 m - the lattice's band - and that is where
  the owner's "road on a flat strip" still shows. It needs the verge-strip
  bank (WP-21), not more grading.
- **Drive-off judged the whole way.** Upright and air are now judged
  until the car stops, not only across the 8 m run-off, and the steepest
  ground under the car is logged. All 5 spots pass; the steepest ground
  crossed is 1V:3.0H (the I-277 cut) and 1V:3.8-4.0H elsewhere. The two runs
  that still do 75-82 km/h after 7 s run along the road on 1V:3.8-3.9H
  ground, not down a bank.
- **race-play-check** (PSXShip, the three city routes, seeds 0-4, trees on,
  150 s, `-NoWatch`), against WP-08's trees-on batch on the same seeds:

  | Route | Rivals retired, WP-08 | Now |
  |---|---|---|
  | UptownLoop | 7 of 15 | 5 of 15 |
  | TryonSprint | 13 of 15 | 13 of 15 |
  | IndependenceSprint | 5 of 15 | 7 of 15 |
  | All three | 25 of 45 | 25 of 45 |

  None retired into the ground. The causes are the same kinds as WP-08's:
  traffic (12), lamp posts on Tryon (7), each other or the player (4),
  Barriers (2: a grid scramble on the Uptown Loop at 9 s, a car running
  7 m left of the line on Independence at 139 s). Six of Independence's
  seven are hits on other cars or traffic at 64-144 s. The terrain moved every
  road a little (WP-13), so a seed does not replay the same race and a
  per-seed pairing means nothing; 5 runs a route carry +/-2 of noise. The
  total is at the baseline and no retirement is the roadside's.
- **G-full** (`verify.ps1 -NoMirror`, edition ALL, PSXShip): scene build
  OK (21 venues), SELF-TEST OK, town probe OK, every terrain audit OK, LANE
  AUDIT OK, CITY AUDIT OK, screenshots written. It FAILS on 8 obstacle-audit
  lines, all Chimney Rock (edge faces at wp 668/983, a face past the reach
  at wp 2, a pocket behind a wall at wp 914, one ghost collider). They are
  the same 8 lines, to the centimetre, as the `-city` tree's verify at
  07:06 on 2026-09-29, before WP-13 or WP-14 existed; this branch touches
  no stage code. They are Chimney Rock's (held for size), not Charlotte's.
- **G-budget** (`CityBudgetProbe`): all 225 tiles p95 79.2 ms with trees,
  75.4 ms without; worst view 209 draws, unchanged. The pre-fix branch read
  90.4 / 78.8 ms and city-r1 125.4 / 89.6 ms, under heavier machine load,
  so the one extra DEM sample per binding section costs nothing measurable.
  No asset changed, so the size ledger is WP-13's (+1.26 MB).

## WP-23 (2026-09-29, release R9): billboards, business signs, exit gantries

The owner, after driving it: "its all so barren and flat ... hills, ditches,
trees, billboards". There was not one sign in the city. Now the interstates
and US/NC routes carry billboards at the density NCDOT's permits say each
route has, every business on a collector or bigger has its pole sign at the
road, and every motorway exit has its overhead sign. Branch
`charlotte-scenery`.

**The data** (`Resources/charlotte_signs.bytes`, PSGN v1, its own file like
the canopy grid, so the lines release re-exports the roads without it;
`tools/city/signs.mjs`, `--check` rebuilds it byte for byte; 803 KB raw,
**56 KB Brotli**; `CitySignData` reads it):

- a 60 m grid on the DEM's lattice of two bits: **billboard zoning** (North
  Carolina lets a billboard stand along an interstate or a federal-aid primary
  only in a commercial or industrial area within 660 ft of the right of way,
  19A NCAC 02E .0203: OSM landuse commercial, retail or industrial within
  100 m, or a business within 150 m) and **commercial frontage** (retail or
  commercial land at the cell, or a business within 40 m). 2,263 landuse
  polygons; 12.9% of the box is zoned, 4.2% frontage;
- **the routes and their density**, a STATISTIC off NCDOT's outdoor
  advertising permits (plan Q8: NCDOT data for statistics only, never
  positions), as the plan's critic measured it per route in Mecklenburg:
  I-77 1.20, I-85 1.49, I-277 1.97, I-485 0.29, US 74 1.25, US 29 0.70, NC 16
  0.56, NC 24 0.54, NC 49 0.29, NC 27 0.16, US 21 0 boards per km of route;
  the class figure (interstate 0.87, US/NC 0.60) for a route it did not
  measure. Each route's OSM billboards count toward its figure; the rest are
  drawn at random on its ZONED stretches, the rate raised by what the spacing
  rule takes back (a Poisson process thinned by "a board loses to any better
  one on its side within the rule's distance": r (1 - e^-L) / L stand, solved
  for r);
- the 43 OSM billboards (`advertising=billboard`), where they are;
- 3,960 businesses (OSM shops, fast food, restaurants, fuel, banks,
  pharmacies, car washes, dealers, tyres, supermarkets, bars, motels) within
  70 m of a collector or bigger, each with the kind of sign it puts up;
- per motorway way that ends at an exit, the lanes of each destination group
  from its OSM `destination:lanes` (187 of the 195 exits are tagged).

Two new OSM layers, fetched with the rest by `fetch/fetch_layers.mjs`, pinned
to the road snapshot's moment: `landuse` (2,231 elements) and `business`
(6,544).

**The placement** (`Scripts/City/CitySigns.cs`), per tile on its
`RoadsideOccupancy` mask, BEFORE the trees (the plan's priority), on the tree
frame (`CityTrees.Build` builds the mask, places the signs, then plants):

- **exit gantries** first (official signs): a sign bridge 30 m before every
  motorway_link diverge, its legs on the first ground clear of every
  carriageway's clear zone on each side (across both carriageways when the
  median has no room; a cantilever from the right leg when the whole span
  would pass 85 m), a panel over each destination group of lanes (the exit's
  panel, with its up-right arrow, over the exit lanes), the panels 5.8 m over
  the highest road under the span (the plan's 5.5; NCDOT's 17.5 ft). Green
  panels with a white border and abstract white bars (plan Q7: code-drawn, no
  text); the truss and legs in the lamp posts' pack metal, lighter. Unlit:
  NCDOT's panels are retroreflective today, and the headlights and street
  lamps light them;
- **billboards**: stations every 25 m along each route, a board where the
  hash falls under the rate and the zoning allows (on the outer side of a
  freeway carriageway, either side of a two-way road), no two on one side of a
  freeway within 500 ft (152.4 m) or of another route within 100 ft
  (.0203(2)), none within 80 m of a ramp's gore (not to hide an official sign;
  the rule's 500 ft interchange setback applies only outside a town's limits,
  and the belt is nearly all Charlotte and its towns). Sizes from NCDOT's
  Mecklenburg permits: a freeway's **14 x 48 ft bulletin** (the p90), elsewhere
  the **median 12 x 36 ft**, or a third of the time a 12 x 24 ft poster; faces
  9.5 m or more over the ground on a freeway (total height about 14-16 m;
  NCDOT's median 13.4 m), 6.5 m elsewhere. A monopole, and a **V with its
  point toward the road** where traffic comes both ways (the far carriageway
  across the median), back to back beside a two-way road where one plane faces
  both ways well enough. Each face is turned to the drivers 150 m up the road
  (the walk up the road follows it through junctions and round bends, and a
  one-way carriageway is only ever walked against its traffic). Set back past
  the clear zone as far as needed to stand on free ground (10-40 m on a
  freeway). **Lit** the way the game's lamps are: two floodlights under a
  bulletin's face, one under a poster's, each a lamp head handed to the tile's
  own NightGlow (its halo, and a StreetLights pool), and the face glows in its
  own colours after dark through the atlas's night mask (brightest at the
  foot, where the lamps are). The owner of a board keeps the trees out of its
  line of sight: the first 60 m toward its drivers is marked on the mask;
- **business pole signs**: one at the frontage of every business in the data,
  and every 32 m along the commercial frontage where OSM maps no shop - the
  landuse or a business says it is commercial, or it is an arterial the game
  lines with stores - wherever a STORE stands behind (the nearest building
  within 90 m back and 35 m either way is a shop, not a house, an office tower
  or nothing: no pylon on a lawn beside houses), its front wall at least 3 m
  back (room for the cabinet: a building at the kerb wears its sign on its
  wall), never on a divided road's median side, and none uptown inside the
  loop (1.3 km of Trade and Tryon: its signs are on the walls). An OSM
  business at the kerb gets none either. No two on ONE side of the road within 30 m (across the road
  only within 12 m), settled best first: a sign loses to a better one of the
  next tile always, to one of its own tile only if that one stood. A 2.4-3.2
  m cabinet 4.5-6.5 m up (6.9-9.7 m overall; the plan's 6-10), on one post or
  two, square to the road and turned, on a bend, to split its two drivers 80
  m either way; lit from inside after dark; stood up to 14 m back and 11 m
  along from its place for free ground, wholly inside its own tile. No tree
  trunk within 5 m of one, nor within 2.5 m of the first 45 m of each of its
  drivers' lines of sight. Breakaway (Q15): no collider.

Every post, leg and face footprint stands on free ground (no pavement, clear
zone, sight triangle, corner spot, building, lot, lamp foot, water, deck or
race run-off), and marks what it takes. Nothing overhangs a road but a
gantry's panels. Deterministic: every choice is a hash of an edge station or a
business index; a sign belongs to the tile its nominal post is in, and the
spacing is settled over everything within 340 m of the tile, so no sign is
placed twice across a seam.

**Across the seams.** A gantry's legs stand up to 65 m off its carriageway and
a billboard's line of sight runs 60 m, so both reach into the next tile. They
are decided from global data only: the STATIC mask
(`RoadsideOccupancy.Static`, every reservation but a tile build's own fill
houses and lamp feet, cached for the 32 tiles last asked for) of whatever tile
each leg, post or face lands in. Every tile computes every gantry and
billboard that could reach it (once a session: `CitySigns.Pure`, its boxes
recorded for its owner to draw) and marks their ground on its own mask before
its trees; the owner draws it. The owner alone knows its fill houses and lamp
feet, so it drops one standing on them (the next tile then keeps a few cells
clear for nothing, never the reverse). A gantry's span is marked too: no tree
under the truss. A business's cabinet stands wholly inside its own tile.

**The faces** (`Art/City/Signs/CitySigns.png`, one 512 x 512 atlas, RGB565 on
WebGL; `CitySigns_night.png`, its 128 px night mask; `tools/city/signs_atlas.py
--check` rebuilds both byte for byte). PACK PICTURES ONLY (plan Q6's default,
"pack faces only"; nothing is lettered in code: the only words on a face are
the ones its pack picture carries), every brand FICTIONAL: STACK BURGER is the
BurgerPiz `menu_burger.png` burger on red, SLICE HOUSE the All pack's
`Foods_04.jpg` pizza on green; the Gas_station pack's own `6twelve` and its
price board and striped `Sign.jpg`; the Pizzeria pack's PIZZA banner and five
pictures of its `Decorative_Sign.png` ("Burgers - Best in Town", two "Eat Good
Food", a dancing couple, a made-up NORTH CAROLINA plate), looked at one by one:
its two CAMEL signs, the Harley-Davidson bar-and-shield ones, the contour cola
bottles and HOLLYWOOD GASOLINE are left out. The trades with no shop sign in
the packs show what they sell: a motel the All pack's palm beach
(`Paintings.jpg`), a bank the PSX Mega Pack's gold bar, a pharmacy the
NEUROZAM-9 pill bottle's prescription label, a car lot the NC plate and two
boggle-pack wheels, tyres two wheels, a car wash a wheel over PSX Textures
water, a grocer the All pack's bananas and watermelon, a lounge the dancers;
a strip mall's pylon four tenants (a shirt, an old television, a pizza, a
burger). The grounds are the lamp posts' pack `Metal.jpg` tinted to each
sign's colour; a tourism bulletin shows `Paintings.jpg`'s coast and desert.
Not used although the plan listed them: the Buildings pack's `Shops_01-31`,
photographs of real storefronts with real names and phone numbers. The gantry
panels are the plan's Q7 code-drawn pattern, the only code-drawn faces.

**Drawn** in one mesh a tile with the kit's one material (`CityKit.signs`,
`Materials/CitySigns.mat`): one draw a tile that has signs, not a sun-map
caster. The billboards' floodlights join the tile's street lamps in its one
NightGlow (no extra halo draw). SOLID: billboard monopoles and gantry legs, box
colliders on a Solid-layer object a tile named `CityPost`.

**PSX/Lit** gained `_NightFace` (default 0): with the night-window switch on,
a sign material's mask makes the texel glow in its own colours
(`SIGN_GAIN` 0.85) instead of a window's flat warm or cool. A uniform branch:
every facade draws as before.

**Checked:**

- **SignAudit** (in `CityAudit`, and alone as `CityAudit.RunSigns`, about 3
  minutes): 3,539 tiles - the shot spots with their neighbours, every tile an
  interstate or US/NC route runs through (45 m either side of it) and the
  three race routes'. 329 billboards (268 bulletins and 36 ft boards, 61
  posters; 33 of them OSM's), 2,769 business pole signs, 182 exit gantries (14
  cantilevers), 4,737 faces. Measured again against the geometry: 0 posts or
  legs on pavement, in a clear zone, under a deck, in a building, a lake or
  race run-off, or on a cell the mask (built afresh, without the signs)
  reserves; 0 faces over pavement; every gantry panel 5.5 m or more over the
  road under it (the lowest 5.74 m); every face turned to its traffic (dot
  0.9 or better to its driver 150 m up the road, 80 m for a cabinet; worst
  0.918), that driver on a road and coming toward it; every sign in the tile
  that placed it, none twice across a seam, the same signs on a second build;
  one mesh with one material a tile; every billboard lit; the monopoles and
  legs solid (679 posts), the cabinets breakaway. **Density**: interstates
  149 placed for NCDOT's 167 (0.73 per km of route against 0.82: -11%), US/NC
  routes 172 for 197 (0.48 against 0.55: -13%), both inside the plan's +-20%
  (per route: I-77 53/57, I-85 56/66, I-485 31/30, I-277 9/14, US 74 32/39,
  NC 16 31/28 ...). What falls short is the ground: 44 boards won their
  spacing and found no free ground within 40 m of the road. Exit gantries 182
  over 223 km of motorway, one an exit (NCDOT counts 1.01 per km: advance
  signs are not built); 13 exits found no ground for a leg.
- **CITY AUDIT OK**: the DRIVE AUDIT's five zeros 0, the roadside audit's
  zeros 0, the tree audit OK (136,170 trees on its tiles; planted/asked median
  1.00, the four canopy bands within 5 points: the signs take a little ground
  from them), the lamp and terrain audits as before.
- **city-play-check** CITY SPAWNS OK (the Myers Park trunks still stop the
  car, 8 of 8). **SELF-TEST OK**, **GUID AUDIT OK**, typecheck OK.
- `export_osm.mjs --check` OK (charlotte_city.bytes changes only in its
  attribution: the NCDOT credit line), `signs.mjs --check`,
  `signs_atlas.py --check`, `canopy.mjs --check` and `credits.mjs` OK.
- Shots (`CityRefSpots.RunSigns`, `Screenshots/City/signs`): at I-277, I-77
  west of uptown, I-85, South Blvd and the Independence strip, the nearest
  bulletin, gantry, poster and cabinet from their own drivers' line, before
  (the city built with no signs), after and at night, and each face close up.

**Measured** (`city_budget.txt`; its A/B pass is now the same sites with no
signs, trees both ways):

| | with signs | without (same run) |
|---|---|---|
| tile build (EnsureTile), all 225 tiles | p50 25.2, p95 86.1 ms | p50 24.9, p95 80.7 ms (the signs are not in this path: machine noise, with six other Unity jobs on it) |
| the tree frame, signs placed first on it | p50 2.4, p95 5.8, max 16.8 ms (WP-08: 2.1 / 4.9 / 13.6) | - |
| placing a tile's signs | p95 0.1-1.6 ms by site | - |
| draws in a view | +0 to +5 (one a tile with signs in view; no shadow caster) | - |
| worst view | 212 draws (uptown; WP-08 209) | 207 |

Heap: the sign data holds its 743 KB grid and 3,960 businesses (under 1 MB).

**The races** (`race-play-check -Venues UptownLoop,TryonSprint,IndependenceSprint
-Seeds 0,1 -Signs ab`, a new switch: every race with the signs and then
without, and the report now counts hits on the `CityPost` colliders): 12
rivals retired with the signs and 12 without, over the same six races; 0 hits
on a billboard post or a gantry leg in any of them. The retirements are the
routes' own (traffic, the Tryon lamp posts, the player's car), and where the
two runs of a race part it is after the first retirement, the chaos every run
of these races has (the same race run twice with the signs on retired 2 and
then 1).

**Size.** `charlotte_signs.bytes` +56 KB Brotli; the atlas ships as RGB565
(512 KB in the player, 0.50 MiB in the Build Report) and its night mask is
128 px. WebGL.data 82.25 -> 82.40 MiB (+0.15, under the plan's +0.25-0.35:
the atlas compresses well), the largest file 82.40 MiB, under the 95 MiB
ratchet. `charlotte_city.bytes` changes only in its attribution.
`size-ledger.py` counts the sign file with the rest of Charlotte's data.

**G-web.** A local WebGL build of the branch (`build-and-publish -SkipScenes
-SkipDeploy -PagesDir city`, BUILD OK, GUID audit OK) served from 127.0.0.1: a
new career (1 January, the SNOW dress), down the street to the line, FREE ROAM
CHARLOTTE loads (`[City] parsed in 136 ms, elevation solved in 597 ms`) with no
console error and no missing sign data or kit warning; every PSX/Lit surface
draws (the shader with `_NightFace` compiles on WebGL 2); the pause menu's
CREDITS page shows the NCDOT line, wrapped, not clipped. The spawn on Tryon
uptown has no sign in view (the uptown frontage is building to the kerb), and
the drive to one was not made in the browser; the editor shots are the look.

### WP-23 review (2026-09-29): business signs where the stores are, pack faces only, signs across the seams

The review found three things wrong with the first pass.

**1. Too few business signs, some of them in the wrong place.** Two signs
on opposite sides of an arterial knocked each other out: the 30 m spacing
had no same-side test, and a 4-5 lane road puts the two sides 26-29 m
apart. The frontage fill was one station in 0.7 every 40 m. A candidate also
lost to a better one that then found no ground. Meanwhile the fills stood
wherever OSM landuse said "commercial", which was often in grass beside
houses, because the game's procedural suburbs do not read landuse. Now:

- no two signs on ONE side within 30 m; across the road only within 12 m;
- settled best first, and a sign loses only to a better one of its own tile
  that actually stood (or to any of the next tile's);
- the frontage is every collector or bigger with commercial landuse or a
  business within 40 m, and every arterial, checked every 32 m;
- a fill stands only where a STORE is the nearest building behind it (within
  90 m back and 35 m either way), with its front wall at least 3 m back. There
  is none in front of a house, an office tower or an empty field, and none in
  a divided road's median;
- no pole sign uptown inside the loop (1.3 km of Trade and Tryon), and none
  for an OSM business at the kerb: those signs are on the walls;
- a cabinet may move up to 14 m back and 11 m along the road to find ground;
- trees are kept off the first 45 m of each of a cabinet's drivers' lines of
  sight (2.5 m wide), as billboards already were.

On the audited tiles that gives 1,680 business signs (1,054 at OSM
businesses, 626 frontage fills), on 32.9 km of store frontage: one
every 20 m, and the fills alone one every 52 m (the plan: every
30-60 m). The first pass placed 2,769, of which many stood on a lawn, in a
median, uptown, or against a wall. What does not stand is the ground the game
gives them:

- 8,753 frontage stations where OSM says commercial but the game has a
  house or nothing behind (mostly the procedural suburbs and the Independence
  expressway, which has no buildings along it);
- 1,058 where the store or the business is at the kerb;
- 542 that lost the spacing, and 104 with no free ground.

The shots' streets show the same thing (`CityAudit`'s per-street lines).
Along Statesville Avenue the fills stand every 30 m in front of stores. South
Boulevard at Archdale is grass and houses in the game, so it gets two signs.

**2. Faces lettered in code.** The OWNER DECISIONS took Q6's default, "pack
faces only". The first atlas lettered nine trades and all the slogans in
Aileron. The atlas now uses only pack pictures (see "The faces" above), and
no lettering is drawn in code. `signs_atlas.py --check` rebuilds it byte for
byte.

**3. Signs that cross a tile seam were invisible to the next tile.** A
gantry's leg, a billboard's face, its line of sight and a gantry's span were
marked only on the owner's mask, clipped at the seam. The next tile planted
trees on them, and nothing checked a foot there. Now both kinds are decided
from global data (the static masks), and every tile they reach marks their
ground before its trees (see "Across the seams" above).

The decisions cost the tile that first needs them, on the masks of the tiles
round it. `CityWorld` spends a frame of its own on each 3 ms slice of them
(`CitySigns.Prepare`, `CityWorld.PrepareSigns`), before the tile's tree frame.
`EnsureRing` does the same slices back to back, so the budget probe times the
frames play would have.

The sign audit now also checks:

- every leg and post in a tile not its own, against that tile's own full
  mask (its fill houses and lamps too) and against the marks of the tile that
  planted round it;
- no tree trunk on any sign's ground in any tile;
- no two signs' posts in one another;
- the business-sign density on store frontage.

Results: 67 legs and posts in the next tile, all on free ground, all
marked there; 0 trees on a sign's ground; 0 posts in one another. The owner
dropped 4 gantries or billboards on its own fill houses or lamps.

**Checked (the review's run):**

- SIGN AUDIT OK: 332 billboards (271 bulletins, 61 posters, 32 of
  them OSM's), 1,680 business signs, 182 gantries (13 cantilevers).
- Billboard density: interstates 149/167 (-11%), US/NC 175/197 (-11%).
- CITY AUDIT OK: DRIVE AUDIT 0/0/0/0/0, roadside 0, tree audit OK.
- The roadside probe ran; city-play-check CITY SPAWNS OK.
- SELF-TEST OK, GUID AUDIT OK, typecheck OK.
- Race batch, signs on, three routes, seeds 0 and 1: 11 rivals retired (12
  in the first pass's A/B); 0 hits on a sign post; 0 on a trunk.
- `signs.mjs --check` OK (frontage 4.2% of the box, was 3.6%);
  `signs_atlas.py --check` OK.

**Budget** (same sites; signs on / off): the worst view is 209 draws with the signs and without (trade_tryon); at most +7 draws in one view (tryon_start, one a tile with signs in view); tile build p95 70.5 ms with the signs, 76.1 without (the signs are not in that path: noise); the tree frames, the signs' slices on frames of their own, p95 5.3 ms, max 11.0 (the first pass 5.8 / 16.8, WP-08 4.9 / 13.6). The static-mask cache holds 32 tiles (about 2 MB) and the decided gantries and billboards a few KB each.

**Size:** WebGL.data 82.46 MiB (the first pass 82.40, before WP-23 82.25), under the 95 MiB ratchet; the atlas 0.50 MiB in the Build Report as before; charlotte_signs.bytes 803 KB raw, 56 KB Brotli; SIZE LEDGER OK.

**Shots:** `CityRefSpots.RunSigns` adds a `<spot>_street` frame for each spot
(the street of the nearest business sign, from its driver 80 m up the road).
It also adds three streets the game lines with stores: Central Ave in Plaza
Midwood, Albemarle Rd, and Wilkinson Blvd.

**Not done:** on the audited tiles there are about 280 km of OSM commercial frontage where the
game's buildings are houses or nothing, because the procedural suburbs do not
read landuse. Signs wait for stores there. A buildings pass that puts shops on
commercial landuse would let them stand; it is not part of this package.

## WP-25 (2026-09-30, release R9): culverts, headwalls, creek banks, ponds

R1 (WP-04b) put the real creeks in: the county's and 3DHP's lines, beds
sampled from 3DEP, the ground carved to them, a water sheet on every creek and
a bridge wherever a road crosses one. What it left: the small streams (the
ravines) went under their roads with nothing to show where, the creeks' banks
were the same lawn as everything else, and ponds under 2 ha were not kept.

**Culverts** (`Scripts/City/CityCulverts.cs`). A culvert is a ravine's line
crossing a road's line where the road is on the ground (a deck over a ravine is
a bridge) and at least 1.6 m over the ravine's floor (less leaves no room for a
pipe and its cover; the channel then meets the road as a swale). Each end is
found by walking the ravine away from the road on a fixed 1 m grid of the
line's own arc length until the drawn ground (the 8 m lattice) is:

- clear of every road: past its pavement, its clear zone and half the wall,
  and outside the race routes' run-off;
- down at the toe: within 0.35 m of the lowest ground further out, or up to
  1.2 m over it where the fill 5 m behind already stands 70% of a wall higher
  (the foot of the embankment; at the channel's own low point the land behind
  can be a gentle run-out and the wall stood free on it).

A divided road's two carriageways, or two roads over one ravine, walk to the
same grid point and find the same end: ends are keyed by (ravine, grid index)
and kept once, and every tile that can own an end sees every crossing that can
make it. The culvert solve never changes the ground function or a road's
height: the embankment the road already stands on is the culvert's.

**Each end's form** (review, 2026-09-30: of 55 sampled ends 47 were a
concrete box standing free on the lawn, with no channel in front and a
concrete-walled berm behind sloping down to the road). The walk decides it
(`CityCulverts.HeadwallFits`):

- a **HEADWALL** only where it reads as one: the drawn ground in front of the
  face is no more than 0.3 m over the pipe's invert at 1.5, 3 and 4.5 m (a
  channel or the toe runs on out of the pipe, not a bank), and the fill behind
  rises through the backfill's level, across the whole width between the
  wings, within 6.5 m. The pipe is the largest of 0.9 / 1.2 / 1.5 m (up to
  what the fill over it takes) whose wall fits, one size for both ends;
- elsewhere the **PIPE PROJECTING** from the toe of the fill.

**Headwalls** (`CityMeshes.Hydro.cs`). A concrete headwall (the city
concrete, the barriers mesh on the Solid layer) faced square off the nearest
road, the pipe's mouth in it (an octagon, its inside the lamp posts' dark pack
metal: a corrugated pipe, and the dark a mouth needs, render-only). Behind it
the embankment: a grass berm level with the backfill from the wall's back face
to where the fill rises through it, its sides falling at the steepest graded
bank (1V:2H) until they pass a hand under the lattice, wrapping round the
wall's ends as a quarter cone; short concrete wings (1.5 m) with their coping
a lip over the berm. A concrete apron 1.5 m deep at its foot (the kerbs'
render-only concrete, draped on the lattice). The long wing walls and the
falling berm are gone.

**Projecting pipes.** A concrete barrel, eight-sided, its bore a quarter
under the drawn ground at the mouth (silted), running 4 m back into the fill
at half the ground's rise there and never with its back end out of the ground
(on a flat toe it dips in, a pipe coming up out of the toe); the mouth's ring
and the dark inside as a headwall's; Solid, like the wall.

**Ditches.** Every end drains into a strip of the banks' clay 10 m down the
ravine from its face, the pipe plus 0.6 m wide, narrowing over its last third,
draped on the lattice like the banks (never within 3 m of a grounded road's
pavement). No new material and no new slot: the walls and barrels are the
barriers mesh, the apron the kerbs', the bore the lamps', the berm ground and
the ditch the banks'. The occupancy mask (static half and the tile's own)
marks each end, its berm or barrel and its ditch, so no tree or sign post
stands in a headwall or grows in its ditch.

**Creek banks.** A band of the owner's pack clay (PSX Textures II
`dirt_pt_7`, the red-brown of Piedmont clay, copied as
`Art/City/Pack/dirt_pt_7_city.png`, tinted to the grass's brightness) runs
along both banks of every creek, from a metre under the flat floor's edge (the
lattice meets the water between its vertices, so the edge wanders) to 4.5 m
past it, draped exactly on the lattice: each quad is cut against every
lattice triangle it covers and each piece set on that triangle's plane, 6 cm
proud (corners merely set on the lattice bridged its folds, and the grass came
through in patches). Never within 3 m of a grounded road's pavement, and
**only where the water shows** (`CityMeshes.WaterShows`, the hydro audit's own
test, at each 8 m piece's middle): where a fill buried the sheet, two clay
strips 5.5 m wide with lawn between them read as a dirt track (review,
2026-09-30: at all three named bridges, and beside I-77 along Irwin Creek).
Render-only, the city kit's `bank` material (`CityKit.bank`, no Slot change),
one draw on a tile with a creek or a culvert end.

**The clay dresses with the ground** (review, 2026-09-30: on a snow day every
creek was edged in red clay on white). The city scene's season wardrobe
registers `CityBank` beside `CityGround` (`PSXRacingBuilder
.RegisterCitySeasonal`; `GroundKindOf` knows `dirt_pt_7_city.png` as dirt):
the dirt tints through the year and the snow turf, at the snow ground's tint,
on a snowy day; `CityWorld.BankMaterial` asks `SeasonDress.Substitute` for it
as a tile is built. Four generated materials (`CityBank_Winter/Spring/Summer
/Snow.mat`), no new texture. The city scenes carry the wardrobe, so it ships
with their next build.

**The channel at a bridge** (`CityElevation.Ground`, review 2026-09-30: at W
Trade St, State St and Archdale Dr the water started 20-50 m downstream; the
ground beside and under every deck stood at the road). Two rules, by a creek
only (a ravine is not a channel: a road over one keeps its embankment), and
off with WP-25 (`CityMeshes.HydroOff`, the A/B instruments' "before"):

- the disc round a bridge approach's vertex (a station on structure within
  40 m of it along its road, or at its node on another arm:
  `SpillsAtStructure`) spilled its 1V:4H fill down the creek beside the deck.
  By a creek it falls at the steepest graded bank (1V:2H, a bridge's
  spill-through slope) and no lower than the creek's carved channel;
- under a deck's footprint no disc holds the land over the channel at all:
  the pavement there is the deck, and the land was held 4 m over Irwin
  Creek's water under W Trade St, dug only to the soffit's air.

Beside a road (a true perpendicular foot) the section is as WP-14 made it: its
verge still grades onto a 1V:4H fill, so no roadside, rail or verge changed.
The pit census names the new cause ("a creek's channel at a bridge").

**Mitred water.** The creek sheet's sides are mitred at every bend of the
line, so one 8 m piece meets the next (square per segment, the outside of each
bend opened a notch).

**Ponds** (`tools/city/lib/water.mjs`, `export_osm.mjs`). The county's
Lakes and Ponds and 3DHP's lakes down to 0.2 ha (R1 kept 2 ha and up), by the
same rule (flat in the hydro-flattened 3DEP), simplified at 2.5 m. A pond
never touches a road, so none makes a water span: one within 4 m of a road's
pavement, with a road point inside it, or PERCHED over a road (a road within
16 m of its shore whose 3DEP ground is under the pond's level plus 1 m: the
solve can cut a road a hand or two under 3DEP, and the water would hang over
the slope beside it) is left out. Of 1,458 ponds of 0.2-2 ha in the box, 210
are not flat, 2 near a road and 15 perched: 1,231 kept. `CityMap.LakesAt` hashes the lakes on 256 m cells, so the ground
function tests the few near a point instead of all 1,337.

**Sources.** No new source: the ponds are the county's and 3DHP's layers
already in the export; the culverts are the ravine lines (county `mt100`/`mt300`
and 3DHP orders 2-3) crossed with the road graph. OSM's `tunnel=culvert` ways
(3,840 in the layers cache) and FHWA NBI are not read. `SOURCES.md` records
the use.

**Measured** (PSXScenery; `CityAudit.HydroAudit`, also alone as
`CityAudit.RunHydro` -> `city_hydro_audit.txt`):

| Measure | Value |
|---|---|
| Ravine crossings of a road | 824: 706 culverts, 48 decks, 70 too shallow for a pipe |
| Culvert ends | 767 (243 culverts with both ends): 298 headwalls set into the fill, 469 pipes projecting from the toe; pipes 0.9 m x483, 1.2 m x98, 1.5 m x186 |
| Ends not stood | never clear of a road 148, no channel on the lattice 202, shallow at the toe 132, shared 121, a creek 24, a building 9, a lake 9 |
| Nearest end to a pavement | 4.7 m, every one past its road's clear zone |
| Every culvert keeps its embankment | 0 of 706 fail: the road on the ground over every pipe, the ground under its line within 1.1 m of it (deepest 1.01 m, the corridor sink and sag) |
| Ends drawn (25 sampled tiles) | 55 of 55 (16 headwalls, 39 pipes), each once, in the tile it stands in; 0 walls facing the wrong way; the fill met the backfill behind 16 of the 16 headwalls; every one of the 25 tiles drew its ditches |
| Water shown along the creeks (every 8 m) | 694 of 710 km, 97.7% (Irwin 73%, Stewart 97%, Little Sugar 94%, Briar 98%, McAlpine 100%, Sugar 98%); before the channel at a bridge 689 km, 97.1% |
| At the named bridges (every 4 m within 60 m, off the deck) | State St over Stewart Creek 27 of 28 (within 24 m of the deck 9 of 10); Archdale Dr over Little Sugar Creek 26 of 28 (8 of 10); W Trade St over Irwin Creek 10 of 24 (0 of 6) |
| Pits beside grounded ribbons | 4,313: the lower-road conflicts 3,502 and decks 556 + 7 as before, the water under the opened channels 245, the channel at a bridge 3; none unexplained |
| Ponds | 1,233 water bodies under 2 ha as drawn (739 ha), water shown at the middle of 1,203; none within 3.5 m of a road, none over a grounded road within 16 m |
| Creek beds (7 transects) | 7 of 7 within 1 m of 3DEP, as R1 |
| Decks over water | 560 spans, none with water over the soffit |

Checks: CITY AUDIT OK (DRIVE AUDIT 0/0/0/0/0, roadside audit green and its
verge, ledge, face and rail counts as before, the tree and sign audits green,
the hydro audit's six checks); `city-play-check -Edition CITY` CITY SPAWNS
OK; TEXDECODE AUDIT OK (933 textures in the 16-bit set, the clay among them);
`credits.mjs --check` OK; SIZE LEDGER OK (the data did not change in the
review pass).

**Budget** (`CityBudgetProbe`, the A/B is WP-25: the same tiles without
culverts, ditches, banks and the channel at a bridge, `CityMeshes.HydroOff`):
the worst view 209 draws both ways (trade_tryon); at most +4 draws in one view
(i77_north, plaza_midwood, south_blvd, W Trade St at Irwin Creek, Archdale
Dr); tile build p95 74.8 ms without, 74.5 with, on the same 225 tiles; parse
189 ms + solve 1184 ms, map heap 20.1 MB (the first pass read 178 / 1119 /
19.6: run to run). Data: `charlotte_city.bytes` +156 KB raw, +95 KB Brotli
(WATR +141 KB raw for the ponds' points); the clay texture 256 x 256 in the
16-bit set.

**Shots:** `tools\city-hydro-shots.ps1 -Label before|after`
(`CityHydroShots`): per creek (W Trade St over Irwin Creek, State St over
Stewart Creek, Archdale Dr over Little Sugar Creek) from the bridge's
downstream edge, 2.5 m over the water 40 m downstream, 35 m up 120 m
downstream, and the deck's view again in the SNOW dress; culverts near Queens
Road: the best headwall (McDonald Avenue) from 12 m down its ditch,
three-quarter and from the road, then two more headwalls (Colville Road,
Hartford Avenue) and two projecting pipes (South McDowell Street, Baxter
Street) three-quarter; a 0.56 ha pond off Tyvola Road from 25 m up. The
cameras depend only on the data and the ground before WP-25
(`GroundBefore`); "before" runs with WP-25 off.

**Not done:**

- W Trade St over Irwin Creek still shows no water within 24 m of its deck
  (10 of 24 samples within 60 m). The channel under the deck is open now, but
  just downstream W Trade St's other carriageway (e6605, 18.9 m wide) leaves
  the bridge's end diagonally with its paved edge 3.5 m from the creek's
  line, inside the creek's own 6 m flat floor, and upstream W Trade St's own
  approach runs beside the channel: that land is a road's pavement and bench,
  which the channel may not take. Only moving the road (or a longer deck)
  opens it.
- Irwin Creek shows water on 73% of its length: north of uptown it runs 14 m
  off I-77, whose bench and 1V:4H fill hold the ground over the channel (R1's
  note; WP-14's grading keeps it). The channel rule takes only a bridge
  approach's spill, never the section beside a road, so no verge or rail
  changed; carving a creek through a fill beside a road is a grading change
  (a later WP-14 pass), not this one.
- The ravines carry no water: their channels are narrower than the lattice
  can hold a sheet in.
- 182 of the 706 culverts have no drawn end (the ravine never clears a road
  within 60 m, or the lattice shows no channel): the road simply crosses the
  swale there. An end where the lattice shows no channel would be the same
  object on a lawn the review took out.

### WP-25 on /city/ (2026-09-30): merged with the lines and published

- **Merged:** origin/city-r1 (0a5e0cd: WP-10/11's lines) into
  charlotte-hydro.
- **Data:** re-exported from the merged exporter. Graph 089d7141, as
  city-r1. 1,231 ponds and 564 water spans. `--check` reproduces the data
  byte for byte, and the signs and canopy files come out as shipped.
- **Pond clearance:** the pond filter now measures a road by the piece's own
  lane count (`e.lanes`, WP-10) and no longer by the way's. The same ponds
  are kept.
- **linecheck:** its baseline was STALE only because the NAME section moved
  (the ponds' names). It was re-recorded, and every check reads the same
  before and after.
- **The first verify** failed one check: a culvert on Chestnut Lane at
  (9790,-13017), 2.9 m short of its 9 m bridge over West Fork Twelvemile
  Creek, with the ground 1.79 m under the road.
  - The cause was the deck's cap. WP-11 had put a vertex 2.4 m into the
    deck, and `InStructureWedge` read a point beyond the deck's first
    segment as the outer wedge of that vertex.
  - The wedge now needs the other segment to clamp to the shared vertex
    (`ClampsAt`), both at an interior vertex and at a node.
  - The pits change: dug under a deck 900 -> 765, held under a deck's
    pavement 34 -> 6.
  - The same bug is on city-r1 before this merge. Only WP-25's audit looks
    under a road's line there.
- **The second verify:** VERIFY PASS (CITY). CITY AUDIT OK with 0 of 706
  culverts failing (deepest 0.89 m). DRIVE AUDIT zeros, roadside audit green,
  hydro audit green. TEXDECODE AUDIT OK.
- **Budget:** worst view 207 draws, as city-r1. Tile p95 99.5 ms, against
  city-r1's recorded 100.1 and WP-25's A/B 100.8 without and 99.5 with.
  Heap 17.1 MB (city-r1 17.0). All of it within the ratchet.
- **Size:** WebGL.data 41.65 MiB, against 41.49 MiB for city-r1's last
  /city/ build. SIZE LEDGER OK.
- **city-play-check -Edition CITY:** 2 failures, both in WP-14's drive-off
  stage, as on city-r1.
  - "At least four spots with fills and cuts" fails as before: the one cut
    spot has a rail in its run.
  - e1919 (I-277), city-r1's failing spot, now passes: the car stays
    upright with 0.12 m of air. Its first hard contact is a tree trunk
    20.5 m past the edge, outside the run-off.
  - The newly failing spot is e4746 North Tryon Street s=40 L. The stage now
    names what stopped the car: a building. In plan, that footprint's corner
    stands 7.9 m past the pavement edge, 1.24 m beside the run's centre line.
    The picker's clear-run cast (radius 0.9 m, as far as the car's centre
    runs) misses it.
  - Whether the car's centre is still inside the 8 m run-off when it meets
    the corner decides the verdict: 6.8 m here, just past 8 m on city-r1
    (9.2 m at the end). This is the picker's open item, not the land.
- **Published:** /city/ build stamp 20260930121608 (charlotte-hydro
  d540adf). gh-pages 934e646. Door tour 5/5. The site root is
  byte-identical.

## WP-15 (2026-09-30, release R6): utility poles, wires, cobra-heads, acorn posts

The Street View survey: "Utility poles and wires are the most visible thing
the game lacks. Wood poles 12-14 m tall, 40-50 m apart, usually one side,
1-3 m behind the curb or edge. 2-4 wire levels ... on every arterial and
two-lane in the sample except uptown, freeways and Myers Park. Cobra-head
street lights usually hang on these poles." Uptown (spot A1): "Black
pedestrian lamp posts about 4.5-5 m tall with double-globe (acorn) heads ...
every 20-30 m each side. No overhead wires." Branch `charlotte-poles`.

**Which roads** (`CityPoles.IsPoleEdge`): every street, collector, arterial
and trunk street, except ramps, tunnels, roundabouts, freeways, trunk
expressways (posted 85 km/h or more), uptown and Myers Park. **Uptown** is
measured off the map: inside the freeway loop, nearer Trade and Tryon than the
nearest motorway in its bearing (32 sectors, 40 m inside;
`CityPoles.Uptown`). **Myers Park** is a polygon round its streets' extent in
the OSM cache (Hermitage, Queens Rd W, Wellesley, Colville, Sherwood and the
rest; `CityPoles.InMyersPark`): its wires run behind the houses. On a pole
road the cobra-head on every pole IS the street light:
`CityMeshes.PlaceLamps` stands no lamp post there, and the pitch goes from the
lamps' 38-55 m to the poles' 35-55 m.

**The lines.** A road is followed through every node where it plainly carries
on - the straightest arm within 60 degrees at a two-arm node, within 30 and of
the same name at a junction, a one-way only into a one-way the same way - so a
line runs down a road on ONE side across its way splits and past its side
streets (`CityPoles.Chains`, once a map, at load). A one-way carriageway's line
runs on its outside (a divided road gets one along each outside, as on
Albemarle and South Blvd); a two-way road's side is a hash of its line.
Stations every 45 m +- 5 m along it.

**Where a pole stands.** Square off its station, past the pavement edge
(`CityMap.Edge.PaveEdgeM`, the one accessor R4 replaces) and the road's clear
zone (`RoadsideOccupancy.ClearZoneOf`) by 1.0, 1.8, 2.6 or 3.4 m, nudged up to
6 m along: the first spot the STATIC roadside mask leaves free (no pavement,
clear zone, sight triangle, corner spot, building, lot - a model's yard and
driveway - water, ground under a deck, race run-off) and that is off every
road's keep-out (`CitySigns.RoadsClear`), on ground within 3 m of its road's
height; else none. A tile's own fill houses stand 24 m or more off every road
and its lamp posts inside the clear zones, so nothing a tile builds for itself
can be under a pole: every pole is decided from global data alone (the static
masks), once a session, and every tile finds the same ones. A pole belongs to
the tile its foot is in; its foot is marked on the tile's mask before the
signs and the trees (the plan's priority: poles, lamps, signs, trees). The
decisions a tile needs are made in 3 ms slices on frames of their own after
the signs' (`CityPoles.Prepare` from `CityWorld.PrepareSigns`), so its tree
frame only draws them; the lines themselves are worked out as the map loads
(`CityPoles.Warm`).

**The pole**: a 0.3 m wood pole 12-14 m tall, a 2.4 m crossarm 0.35 m under
its top with two pins, and a cobra-head on a davit arm (1.6-4.6 m) reaching
back over the road's edge, its lens 8.2 m over the road. **The wires**, from a
pole to the next standing one of its line within 70 m (or the one after, over a
station that stood none): the crossarm's pair, then by the line's hash a
neutral 1.9 m down, a telecom cable 4.6 m down and a second at 5.2, sagging
1.5% of the span. A span is not strung when any road under it would be less
than 5.5 m below its lowest wire (checked every 2 m), when the ground comes
within 4 m, or when it would pass through a deck or a building. Solid (Q15):
a box up every pole on a Solid-layer object named `CityPost` (the signs'
posts' name, the audits' and play checks' one prefix).

**Uptown's acorn posts** are street lamps (`CityMeshes.LampAcorn`, placed by
`PlaceLamps` under every lamp rule): 4.7 m black posts with an acorn globe,
every 25 m on each side of every street inside the loop, the globe the lens.
They are pedestrian lamps and there are four times as many as the street
lamps they replace, so each lights the road at a quarter of a cobra-head
(`CityWorld.AcornLightGain` 0.25, `NightGlow.Init(heads, gains)`) and its
halo is smaller and fainter (`NightGlow.HaloSizeFor`): at full strength they
took uptown's residential night frame from a median of 0.073 to 0.238 (the
reference band is 0.09-0.17); at 0.25 it is 0.140.

**No SOLID lamp post in race run-off.** WP-08's race batch found the racers
running wide into the lamp posts on Tryon (33 hits; "the lamps' own package").
A lamp whose foot is in a city race route's run-off (`RaceRunOff`: 8 m past
the drawn edge, 16 m on the outside of a bend) stands where it would and
BREAKS AWAY (`CityMeshes.Lamp.breakaway`; Q15's pattern, as the business
cabinets and the thin trunks): drawn and lit, no collider
(`CityWorld.Attach` stands a box for every other post). WP-15 first stepped
such a post back from the road up to 16 m, else stood none - and uptown, where
the buildings stand at the sidewalk, that left N Tryon without one post by
day and black at night (the review, from the before/after shots). The lamp
audit checks every post in run-off breaks away and none outside it does; the
pole audit stands the tiles up as the game does and counts the colliders
(one a solid post, none a breakaway one), checks at least 85% of the lamp
stations of uptown's race streets stand their post, and prints how much of
those streets is more than 20 m from a light.

**The signs keep clear of the poles.** The review found the telecom cables
(6-9 m up) running straight through a lit burger sign on the Tryon Sprint out
of NoDa: a business's cabinet (2.4-3.2 m, 4.5-9.7 m up) stood 0.8 m past the
clear zone, in the band the pole line stands in (1.0-3.4 m past it), and the
signs were placed after the poles on a mask that knew only the poles' feet.
Now a business's cabinet, a billboard's post, the beam to each face and each
face with its catwalk and floodlights stand only where
`CityPoles.SignClear` finds no wire (1.13 m either side of the line between
two poles' feet: the crossarm's primaries), no pole or crossarm and no
cobra-head arm or head within 0.5 m in plan; they step back from the road
past the line as they already step back from everything else. The poles
never look at the signs, so they are decided first and the same either way,
and a billboard (decided from global data) stays the same in every tile.
`CityPoles.Prepare` decides the stations round a tile that its business
signs will ask about, so the tree frame finds them decided. The sign audit
measures every drawn wire (its six segments, sag and all) and every pole
part against every sign box - cabinet and posts, face, catwalk and
floodlights, beam, gantry panel and truss, solid post and leg - in 3D, with
the signs and poles both on; `PSX_SIGN_POLE_KEEP=0` turns the keep-out off to
show that check fails without it.

**One draw.** `tools/city/furniture_atlas.py` makes a 256 px atlas of four
128 px cells from pack art the game already ships: PSX Textures II
`wood_pt_4.png` (the guardrail posts' wood) and the lamp posts' house-pack
`Metal.jpg`, tinted in linear light by the lamp posts' own material tint (the
lamp posts drawn from it are the lamp posts they were), to black paint (the
acorn posts) and near black (the wires). Nothing drawn in code. A tile's poles,
wires, cobra-heads AND its street lamps (re-emitted from its lamp list) are one
mesh on the kit's `furniture` material (PSX/Lit's PSX_FURNITURE variant); when
it stands, the lamp posts' own mesh is destroyed. So the poles cost no draw:
the tile draws its furniture where it drew its lamps. The cobra-heads join the
tile's NightGlow with its lamps and the billboards' floodlights: yellow bulbs,
like every street lamp (the owner's rule).

**The wires in the shader.** A wire 8 cm thick is a pixel wide at 19 m on the
240-line screen and breaks into crawling dashes past that. PSX_FURNITURE is
the prop atlas variant (the cell in the vertex colour) plus this: a wire
vertex sits ON the wire's line with (side, half width) in TEXCOORD1 and the
wire's direction in its normal, and the vertex shader stands the ribbon up
across the line, square to the eye, at least one framebuffer pixel wide; it
fades into the fog from 100 m, gone by 180, and is not drawn past 200 m. Every
other vertex has TEXCOORD1 = 0. One more PSX/Lit variant (always included:
none, PSX_ATLAS_RECT, PSX_FURNITURE).

**The pole audit** (`CityAudit.PoleAudit`, in every city audit; alone:
`CityAudit.RunPoles` -> `city_pole_audit.txt`), on 3x3 tiles round Albemarle
Rd, Rocky River Rd, Central Ave, uptown, Brentwood Pl, South Blvd,
Providence Rd, Myers Park and Beatties Ford Rd, and every tile of the three
race routes: no pole on pavement, in a clear zone, under a deck, in a building,
lot, lake or race run-off, on a reserved cell of the static mask (sight
triangles, corner spots), on a fill house or a lamp post of its tile; none on a
freeway, uptown or in Myers Park; every wire 5.5 m over every road it crosses
(measured every metre with the road's own height); every span's far pole
standing in its own tile; the pitch p50 40-60 m; the same poles on a second
build from a cold cache; one mesh a tile; a cobra-head on every pole; uptown lit
by acorn posts; no SOLID lamp post in race run-off and none breaking away
outside it; a box collider for every solid post and none for a breakaway one
(the tiles stood up as the game does); and at least 85% of the lamp stations
of uptown's race streets standing their acorn post (the reviewed build stood
almost none on N Tryon). It also prints why stations stood no pole, how much
of the pole roads is more than 40 m from any light, and uptown's race streets'
dark runs (more than 20 m from a light) with what became of the lamp stations
there (`CityMeshes.LampTrace`): the 10 m either side of a junction are the
lamps' fan clearance, older than WP-15.

**The sign audit** also measures the signs against the poles in 3D (the
review): every drawn wire (its six segments, sag and all) and every pole
part - the pole, its crossarm, its cobra-head's arm and head - against every
sign box, with 0.3 m of air, on the 3,539 tiles it audits.
`PSX_SIGN_POLE_KEEP=0` runs it with the keep-out off.

**Shots**: `tools/city-pole-shots.ps1 -Label before|after [-Sheet]`
(`CityRefSpots.RunPoles`): the driver's eye in the lane at Albemarle Rd,
Central Ave, Rocky River Rd, Brentwood Pl, E Trade St, N Tryon St and Queens
Rd W, by day and at night, and a close look at the nearest pole ahead;
`tools/city/polesheet.py` pairs them. `-Spots a15_albemarle,a1_tryon`
(`PSX_POLE_SPOTS`) shoots only those. At each spot the log checks the feet
within 120 m of the camera (`CityRefSpots.SpotFeet`): every pole on no cell
of the static mask (pavement, clear zone, sight triangle, corner, building,
lot or driveway) and out of every road's keep-out, every lamp post off the
carriageway and out of the buildings - `CLEAR` or `NOT CLEAR`. `-WireSigns` adds
`CityRefSpots.RunWireSigns`: one job, the signs placed as the reviewed build
placed them and as they stand now, from the same cameras (22 m back in the
lane of the sign's road) at the business signs by the race routes a wire ran
through, or over (the Tryon Sprint's nearest crossing among them).


**Checked** (sandbox PSXShip, the CITY edition):

- `verify -NoMirror -Edition CITY`: VERIFY PASS (scene build, SELF-TEST OK -
  the texture-decode test included - terrain, obstacle and lane audits, CITY
  AUDIT OK, screenshots), after ReleaseBudget (933 textures of the 16-bit set,
  the atlas already right).
- CITY AUDIT OK again on the final code: the DRIVE AUDIT's five zeros, the
  roadside audit, the lamp audit (no lamp in race run-off: 0), the pole audit,
  the tree audit and the sign audit (billboards 332, business signs 1,677,
  gantries 182; density within 20%).
- Pole audit: 182 tiles, 1,279 poles of 1,649 stations, 950 spans, 3,820
  wires; 0 on pavement, clear zones, sight triangles, corner spots, lots,
  decks, water or run-off, 0 uptown, in Myers Park or on a freeway; lowest
  wire over a road 5.56 m; pitch p10/p50/p90 36.2/44.3/52.5 m; 852 of 852
  span ends standing; same poles from a cold cache.
- city-play-check CITY: CITY SPAWNS OK (72 ok, the drive-off 5 of 5, the
  Myers Park trunks, the order bays, the race grids).
- race-play-check, the three routes x seeds 0-4, 150 s: poles on 24 of 45
  rivals retired, poles off 26 (the same build, A/B), WP-14's batch 25; then
  with the lamps out of run-off 21 of 45 (Uptown Loop 10, Tryon 5 - it was 12
  with 7 lamp-post hits - Independence 6). No car hit a pole in any race.
- Night look (`nightlook-shots -Only city`, against the same run on the
  commit before): arterial night median 0.081 -> 0.055 (a pole road now,
  cobra-heads 35-55 m apart), residential (uptown, acorns) 0.073 -> 0.140,
  sat 0.57 -> 0.61 (the band's top is 0.58), motorway unchanged; every frame's
  floor, median and share under 0.10 in band.
- Budget (CityBudgetProbe): worst view 209 -> 211 draws (+1%); tile build p95
  70.5 -> 75.6 ms (the pole work is not in the tile build; noise); tree frames
  p95 5.3 -> 5.0 ms, max 11.0 -> 15.2 (a cold static mask in a slice);
  furniture about 2,600 vertices a tile with poles.
- G-web: the CITY WebGL player (BUILD OK, GUID AUDIT OK, the kit's shaders
  packed) served from 127.0.0.1: the front end, FREE ROAM on Tryon uptown
  (acorn posts lit at sunset), the TRYON STREET SPRINT II grid in NoDa (poles,
  crossarms, cobra-heads lit, the wires unbroken lines on the 240-line
  screen), the same grid at NIGHT 23:15; no console error.
- Size: WebGL.data 40.30 -> 40.33 MiB (+0.03), shaders 0.52 -> 0.53 MiB (one
  more PSX/Lit variant), the atlas 64 KB; SIZE LEDGER OK.

**Not done:** snapping stations to OSM `power=pole` and taking a line's side
from `power=line` (the layer is fetched): a two-way road's line takes a
hashed side.

## WP-15 and the launch fix together (2026-09-30): rails out of the lanes

`charlotte` merged `charlotte-poles` (WP-15) and `charlotte-launch` (vertical
curves, `JoinBuriedEnds`, seat pins, `CityLaunchDrive`/`CityLaunchAudit`).
The one conflict in code was `RawSectionsOf`: city-r1 builds its sections
through `SectionAt` and eases the squeeze, and `JoinBuriedEnds` runs after
that on the final list. The two recorded baselines keep city-r1's
measurements plus the lines only WP-15 recorded (no tool reads them; not
re-measured). The data checks (`export_osm --check`, signs, canopy, credits)
are byte-identical.

**A junction's rail never stands in a lane** (`CityMeshes.RailRunsOffLanes`).
The launch branch's drive met a 2 m barrier on North Caldwell Street where
the link e11144 leaves it (around (-1152.5,5052.5)). The probe named it: the
GORE NOSE at the merge of North Caldwell (e11145) into its host East 12th
Street (e11148, node 11802). Its outer end is on East 12th's edge, which the
line model draws 0.65 m inside North Caldwell's one lane, and the block ran
from there to North Caldwell's far edge, across the lane. Node 3578's fan
chord rail stood over the same lane 0.3-0.8 m up (the audit's known
`caldwell-11145`). The rails a junction lays off two roads' geometry, a gore
nose's block, its two tails and a fan's chord, now keep only their pieces in
no lane. A point is in a lane when drawn pavement (a ribbon, or a fan other
than the chord's own; one road, or two meeting on a seam) lies 0.3 m to both
sides of it, at a height the rail stands in: its top over the lane's wheels,
its foot under `RoadsideRules.OpenDropM` over the lane. The cut therefore
opens no drop the rail census fails. A road's own side rails are untouched.
Not measured city-wide (no audit run for this merge).

Drives (`CityLaunchDrive`, RX-7, `PSX_LAUNCH_WALK`):
- West Trade Street at (-3157,5588), SE-bound at 140 km/h: ok, never airborne.
- East Woodlawn Road at South Boulevard, 110 km/h: ok both ways; the ESE
  run crosses the buried stub e11126.
- North Caldwell into East 12th, 70 km/h: the nose is gone and the car
  reaches the break at 63 km/h, but it LAUNCHES on the merge crest there and
  lands against East 12th's far rail. The path climbs 0.22 m in 2 m onto
  East 12th (129.37 -> 129.59), then falls at 2%: a 13% grade break.
  e11145 has stations only at 0, 8 and 16 m. The seam is at 5.5-6.4 m,
  0.20-0.28 m under the host, and the clipped vertex takes the host's height
  there. The launch branch's own profile at this spot was the same shape
  (7.5% up, then 3% down); its car never got past the nose. Open.

city-play-check CITY: 3 failures, all in WP-14's drive-off stage. e4746
North Tryon and the fill/cut count are known. The fill picker now takes
e14612 North Tryon s=165 R instead of e1499 I-277, because the merge's
heights moved which fill comes first. There the verge falls 1V:4 to a shelf
0.63 m down (2.5-6 m out) and then drops 1V:4 again to -1.9 m at 12 m. The
car gets 1.48 m of air and tilts to up 0.40. This is WP-14 grading.

## The 2026-09-12 pass: floating roads, ledges, invisible walls

Reported after the rebuild: "a lot of roads still floating in air, not
connecting, don't properly transition lane counts; invisible walls; bridges
have ledges blocking entrance; 277 and the 77/85-to-485 clovers need special
attention". Every item had a concrete cause, and the plan geometry was never
one of them (it is OpenStreetMap's, and it matches the satellite view by
construction). What changed:

- **The ground.** The DEM was a bare 5x5 min filter of the height tiles: 6.6 m
  low on average and 20 m low beside every valley, so every hillside road
  stood in the air. It is a morphological opening now (erode, dilate back; a
  closing after it). Trade & Tryon lands at 227.4 m ASL. **Corrected
  2026-09-28:** the real ground there is 232.6 m (USGS EPQS on the 1 m 3DEP
  lidar DEM, 232.59 m), so the game is 5.2 m low. The tiles are bare earth,
  not radar, so there were never any roofs or radar pits to remove; the
  opening still flattens real crests (see "The ground" above).
- **Decks from facts only.** A station is on structure when it is a tagged
  bridge, a water span, a trench deck, or the OVER road of a crossing within
  `DeckReach` of it (the under road's corridor and most of its blend, plus
  its own corridor, stretched for an oblique crossing). Everything else that
  stands above the terrain is an EMBANKMENT and the ground grades up to it;
  the old "1.4 m above the DEM" rule is a 3.5 m last resort.
- **Faces.** Kerbs, deck fascias and soffits were wound inward: a bridge seen
  from beside or below was a paper ribbon with rails. Nothing draws a raw
  vertical quad any more (`Wall`, `WallSloped`, `Down`).
- **Junctions.** A node's arms are a THROUGH PAIR plus BRANCHES (anything
  within 60 degrees of another arm: the ramp, else the lower class, else the
  narrower). A through road with only branches beside it draws no fan: it
  runs on mitred and each branch is CLIPPED against it — while its inner
  edge is inside the host it is cut back to the host's edge along its own
  cross-line, at the host's height; while the whole ribbon is inside it
  collapses to a point on that edge. Both roads are chains (a mainline is cut
  at every ramp node; so is the ramp), projected onto as one polyline, and
  the branch is sampled at every host vertex and wherever the cut changes
  state. The host's barrier and both roads' rails stand down for the whole
  attachment; the painted gore fills 0-4.5 m of separation. Anything else is
  a fan whose corners sit at each arm's OWN height (the flat fan was the
  ledge at every bridge mouth) with one trim per node, chosen so the corner
  cones never interleave.
- **Widths.** The wider of two through arms tapers to the narrower over
  25-80 m (30 m per lane) at every mitred node — plain continuations and
  merges alike. Parallel roads mapped closer than their widths (I-77's
  express lanes beside its general lanes) split the space between their
  centrelines in proportion and share one barrier or rail.
- **Colliders.** Buildings collide as the walls you see (a MeshCollider on
  the tile's building mesh) instead of an oriented box that reached into the
  street wherever a footprint was not a rectangle. Piers nudge along the
  deck to stay out of the road below.
- **The drive audit** (`CityAudit.DriveAudit`) stands real tiles up with
  their colliders and rays every lane every half metre: holes, surfaces off
  the solve, steps over 12 cm, and anything solid across the lane at wheel
  height. The rewritten builder's first run scored 388 walls / 60 steps /
  148 off-surface across seven tiles; it ships at 0 / 18 / 37, all of the
  remainder under 0.45 m at ramp seams where OSM draws the ramp a lane
  inside its mainline at a different height.

## The second 2026-09-12 pass: the spawn, the grass, the walls, the map

Reported after the first pass shipped: "each time I free roam Charlotte my
car is dropped from the sky; when I race on the 277 it drops me in the
center of Charlotte, just like free roam; grass coming up through the roads
or concrete; on I-77 the lines and road zigzag back and forth; roads and
highways have outside shoulder walls they don't have in real life; I'd like
a map to view where I'm at in the city."

- **The spawn was a stale bake.** The scene bakes the car at the spawn's
  solved height, and the solve moved with the DEM on the morning of the
  12th while the shipped scenes were baked the night before — the city is
  solved again from the shipped data at every load, the scene is not.
  `CityMode.SeatOnStreet` now puts the player on the nearest street at the
  graph's own height at load, through `TeleportTo`, and the baked pose is
  only a hint. A bake can never go stale on this again.
- **The race grid was written before the car woke.** `SetupRace` runs in
  `CityMode.Awake`; `CarController.TeleportTo` returned early while `Body`
  was null (the car's own Awake had not run — Awake order between objects
  is a coin flip), so only the transform moved and the interpolated body
  painted its baked pose back on the first step. The teleport now asks the
  GameObject for its rigidbody when the component has not cached one yet.
  `tools/city-play-check.ps1` (`CityPlayCheck`) plays both scenes headless
  and asserts the poses after a physics step: on a street, at street
  height, still there two seconds later; the field at the line and nowhere
  near the free-roam spawn. Its first run found a third fault: `BuildPath`
  lerped heights between OSM polyline VERTICES, and the solve lives at the
  10 m stations, so between two vertices 200 m apart the path ran straight
  while the road humped — the whole field stood 1.0-1.7 m over the 277 at
  the green. The path samples vertices and stations now, and the audit
  checks every waypoint against the solved surface of the route edge it
  lies on (0 of 2,299 off by more than 15 cm).
- **Grass through the tarmac, three causes.** (0) The land between two
  corridors is a weighted MEAN of their heights; a lattice vertex just
  outside the Independence Expressway's corridor, inside the blend of an
  overpass abutment four metres higher and twenty away, stood 1.2 m proud
  and the cell it cornered rose through the outside lane (found with
  `tools/city-ground-probe.ps1`). The "never above the lowest tarmac" cap
  now reaches `CapReach` (5 m, a lattice cell's diagonal) past the
  corridor. (1) The ground under a road is
  an 8 m lattice sampled from the road's solved height, straight between its
  vertices, while the road bends at its 10 m stations: where the profile
  breaks OVER (a cone meeting the flat, a trench mouth, a crest at a
  junction) the straight ground ran above the bent road by up to half the
  break. `CityElevation.MeasureSags` records how much the grade increases at
  each station, a sag (never negative; an edge end carries the worst pair it
  forms with any other arm at its node). The corridor pin sinks the land by
  that much extra there (`SagAt`), and the kerb face reaches down through it.
  It was `MeasureCrests` and measured crests until 2026-09-13, which was the
  wrong way round: the straight lattice runs above the road at a dip, not at
  a crest. A straight grade keeps its 20 cm kerb. (2)
  A deck over land the DEM calls level — 790 edges tagged `bridge=yes`, most
  over creeks and railway cuts a 60 m grid cannot see — had the ground
  running through the concrete. Structure now CAPS the land at
  `UnderDeckAir` (1.2 m) under the soffit; never a raise, so a real valley
  keeps its depth. The drive audit gained a GRASS probe: the first collider
  a lane ray meets must be the road, whatever the height difference.
- **The zigzag was the express lanes.** OSM maps the I-77 Express Lanes
  (2019) as a second carriageway 5.5 m from the general lanes, so the two
  ribbons were squeezed against each other and the paint slalomed wherever
  the mapped lines drifted. The game is set in 1999: the exporter drops
  every `toll=yes` way (I-77 Express, I-485 Express, the Monroe Expressway,
  their ramps — 177 ways) and the four ramp pieces that then led nowhere.
  **Owner decision 2026-09-28:** roads, buildings and signals are present
  day; only the cars are 1999. WP-10 (2026-09-29, "WP-10 (R4): the line
  clean-up" above) can bring them back, moved apart from the general lanes
  by PARA instead of squeezed, but holds them back (KEEP_TOLL) until their
  connector flyovers clear I-485 in the solver (a package of its own in the
  plan's A8 schedule note). Six I-77 ways
  (3.6 km) still carry the express lanes inside their own `lanes` tag,
  which leaves two kinks (35.33637,-80.84876 and 35.33150,-80.84806).
  Where a squeeze remains (tight divided arterials, frontage roads) the
  ribbon now CROPS the painted profile instead of compressing it: texture U
  comes from each vertex's true lateral offset.
- **Walls on the median side only.** A Jersey barrier stood on both edges of
  every freeway carriageway. The outside shoulder is open verge now, and
  gets a wall only where the DEM stands more than 2 m above the road beside
  it — the retaining walls of the 277 trench and the I-77 cut are real.
- **The city map.** `CityMinimap`: a MaskableGraphic that re-tessellates the
  streets within 340 m of the car into line quads (freeways amber and wide,
  arterials pale, streets grey, water blue — the thumbnail's palette),
  heading up with a north tick on the rim, rebuilt when the car has moved
  1.5 m or turned 1.5 degrees, ten times a second at most. It sits where
  the race map sits, mid-left; the HUD builds it at runtime, so the shipped
  scene needs no rebake. `tools/city-ground-probe.ps1` prints, for a point,
  every corridor that has a say in the ground height round it.

## The third 2026-09-12 pass: ramps that are the mainline until they leave it

Reported: "entrance ramps going up through the center of a road like a
staircase in the center of a house". Preview shots from the freeway's own
windscreen (`CityPreview.RunStaircases`) showed exactly that: a thin concrete
sliver rising out of I-485's lanes past the Johnston Road overpass, and one
climbing out of US 74's cut.

- **The cause.** OSM joins a ramp to its carriageway at the END of the
  taper, so a ramp's last hundred metres or so lie inside the mainline's
  pavement. `CityMeshes.EmitBranch` clips it against the host there — but
  only while the two are within `AttachDy` (0.6 m) in height, and the solver
  never tried to make them so: every edge was solved on its own profile and
  two edges met only at their shared NODE. So wherever the ramp and its host
  diverged vertically before they diverged in plan, the clip let go and the
  ramp's ribbon stood inside the host's lanes at its own height. The worst
  and commonest case was the TRENCH rule — written for uptown's cuts, it
  sinks a freeway 5.5 m under every street bridge city-wide (230 of them), so
  the ramps passing under the same suburban overpass beside I-485 and I-85
  stood four to five metres above the dipped mainline. The rest were approach
  cones and terrain lifting a ramp still inside its host.
- **The instrument: the overlap census** (`CityAudit.OverlapCensus`,
  `CityAudit.RunOverlaps` — seconds, no tiles). Every 4 m of every ribbon,
  against every other ribbon it overlaps in plan, excusing the junction fan
  and anything off the end of the other road: two overlapping pavements must
  be one surface or a clearance apart. It writes `city_overlaps.csv` and
  dumps the worst pairs' profiles, nodes and crossings. First run: 19.7 km
  of ribbon at a wrong height, 7.2 km of it ramp beside road, over 3.1 km of
  ramp more than 0.6 m off its host. The city audit now FAILS past 60 m of
  the last number.
- **The fix: seats** (`CityElevation.PrepareSeats` / `SeatBranches`).
  `CityMeshes.BranchSeats` walks every ramp end exactly as `EmitBranch` does
  — same chains, same 6 m step, same 4.5 m gore — without the height test
  and with the host's unsqueezed width, so the solver's zone is never shorter
  than the tile's. Every ramp station inside it (plus one past its far end,
  so the lerp to the next station does not lift the run's last metres) is
  SEATED: it takes the host's height under it; `RaiseHump`, `RaiseCone`,
  the relax sweep and the bridge/water holds leave it alone (a cone stops at
  the first seated station and never comes out of the far side to lift the
  mainline's node); and it is re-seated after the trench pass, inside the
  raise/reconcile loop, after the relax and after every cone round. Seated
  stations inherit the host's structure (a ramp inside a deck's pavement is
  on the deck).
- **The climb out** (`ClimbOut`): past the last seated station the ramp
  leaves at its class's grade, or exactly as steep as its next fixed height
  needs. A collector road seated on the I-277 bridge for 70 of its 81 m had
  10 m left to drop 4.6 m (50% on the uptown race route), and one lying in
  the 277 cut for all 261 m had a junction 2.7 m above the cut at its far
  end. So a far END out of reach of 1.5x the class grade is moved: RAISED
  when too low (the node and its neighbours follow through the solver's own
  snap and cones, and the cone loop now also runs until the seats stop
  moving ends), LOWERED when too high only if the node is a plain
  ground-level junction of ramps (`LowerFarNode`: at its terrain, not a
  trench pin, every arm a ramp, none on structure). Ramps only: seating one
  carriageway of a divided STREET on the other copied that host's short
  cliffs onto a hundred metres of Parkwood Avenue.
- **Results.** Ramp overlap past the attach limit 20 m (from over 3.1 km);
  all ribbon mis-heights 6.3 km (from 19.7, what is left mostly a freeway's
  two carriageways squeezed together 0.3-0.9 m apart — real on hillsides);
  no station grade past 16%; every clearance holds (worst 4.54 m); the drive
  audit's remaining 17 steps and 25 off-surface probes, which were ramp
  seams, went to zero. 15,915 ramp stations seated; the whole solve is
  ~0.85 s, the seat search 0.11 s of it.

## The 2026-09-13/14 pass: roads meet the ground by DOT standards

Reported, about every venue: getting back onto a road was hard because the
roads stood proud of the dirt, and bridges had open gaps. Then the owner's
rule: "Roads sitting cm above the ground do not need rails/walls, they
should meet the ground properly by DOT standards." The shared contract is
`Scripts/RoadsideRules.cs` (FHWA/AASHTO numbers, used by builders and audits
alike). The city part:

- **Verges instead of lips.** A grounded ribbon's edge meets a verge built
  into the tile's Ground mesh: a 4% shoulder, 1V:6H across the clear zone,
  then 1V:4H to the lattice (which sits `CorridorSink` = 0.10 m under the
  corridor, with a per-station sag allowance). Kerb faces are render-only
  (the "Kerbs" child). `DropFrom` warrants a rail only where that grading
  cannot reach the ground inside 8 m, the step onto a lower road passes
  `OpenDropM`, or the fall is critical. `ClearRun` tests other roads'
  pavement AS DRAWN (outlines and fan triangles), not their nominal width.
- **Rails where a drop warrants one, never in a lane.** Solid capped prisms
  on elevated spans, 20 m approaches, ungraded ledges, fan chords and gore
  noses. A host's rail gap follows the branch's OUTER-edge arc; round one
  cut it at the branch centreline and laid rails along four ramp lanes.
  `Squeeze` now separates carriageways up to a car's height plus a deck
  apart, which took I-277's own rails out of its lanes over e1921.
- **Junction fans built arm by arm** (ordered envelope, ears where a fan is
  not star-shaped). Sorting corners by angle let a bent link split Tyvola
  Road's mouth into a 1 m deep hole in its lanes.
- **Instruments.** CityAudit's roadside audit (verge lips, faces, the rail
  census, the pit census, and since round four the unguarded-ledge check),
  a fan-mouth probe, a lane survey ("not a check" but for its building
  class), and `CityEdgeProbe` (every tile boundary edge: open drops,
  grounded lip histogram).
- **Results, round one → three.** Edge over a drop with no rail 145 m → 0;
  verge steps 906 → 4 (5-6 cm pavement-to-pavement seams where two
  solved roads touch); body-box faces 43 → 0; every lane mouth has road
  under it; drive audit clean on all nine spots.
- **Round four: ledges, seams, buildings, arms (2026-09-14).**
  - *Ledges are a check* ("no unguarded 0.3-1 m ledge within 1.5 m past a
    grounded edge", walked from a rail's width inside the edge, so a
    barrier on the edge shields what lies behind it): 80 → 0. The causes:
    squeeze half strips that never saw a neighbour more than 0.5 m up
    (`ClearRun`'s overhead is now the squeeze's own height band, and a half
    strip reaches 15 cm under the higher pavement); squeezed or stopped
    verges a level above the next road (`RoadsideRules.LedgeStepM`, 0.3 m:
    past it the edge stands on a retaining face with a rail, `Ungraded`,
    Jersey medians exempt); gore-nose verges laid off the painted gore's
    far edge; a verge collapsing along a fan chord (`FanEntry` insets the
    perimeter edge); a corner fill's plate 0.9 m over e7753's verge.
  - *Touching pavements meet* (`MeetPavement`): an edge vertex more than
    the edge drop over another grounded pavement within 0.2 m steps down
    onto it, by at most 12 cm, reading the other road without the step, so
    the order roads are sectioned in changes nothing. The 5-6 cm seams
    went 4 → 0. A per-tile cache of world-space sections (`RawSectionsOf`)
    pays for it.
  - *Buildings off the pavement* (`FitFootprint`/`FitHouse`): walls stand
    0.6 m clear of the drawn road, houses shrink as boxes, and a building
    split in two or cut below half its area is left out (616 cut, 41 left
    out city-wide). 43 lane probes stood in a wall; none do, and the audit
    fails one, counting a wall behind any other collider in the column.
  - *Arms of one junction* more than 0.25 m apart are squeezed like any
    two roads (`ArmsApart`): the link e14103 over North Davidson Street is
    clear. Fan-edge and gore-nose rails stand down where pavement carries
    on past them at their height, sampled 0.5 m apart (`PavedOnward`, the
    Freedom Drive and Tyvola Road bridge clusters), and rails move out up
    to 0.15 m at polyline bends (`VertexRailOut`).
  - *Fan chords* are probed at their corners as well as between (node
    5211's high corner stood 1.95 m over the land once the corner fill was
    gone), and are not railed by the ledge rule: a harness census of every
    fan found 13 chords it railed, each stepping onto another junction's
    fan or an arm within 5 m of its trim, 4 of them standing in a lane
    mouth. Fan-mouth solids city-wide went 45 → 26.
- **Left:** the lane survey's 46 solids over a lane (named and checked
  since the review of R1: see "Rails in lanes" under WP-04; squeeze splits at one
  road's cross-sections and not the other's on I-277/I-77 and e7753 beside
  South Boulevard; deck rails where an edge's width changes at a node;
  Albemarle Road under the Independence Expressway's retaining wall, where
  seating the branch would put 16% grades off its deck) and 10 verge or
  seam slivers of 1-6 cm over clipped lanes (land past 12 cm is checked
  since the second review of R1: see "Land in lanes" under WP-04). Off the audited tiles: the 13
  junctions drawn into each other a level apart keep a 0.3-0.7 m step (the
  fans' to seat or split, not a rail's); squeezed rails stand in the mouths
  at node 625 (e343 beside Tyvola Road, where the squeeze ends between
  sections) and node 2587 (e3805 beside Wilkinson Boulevard); grass stands
  5-11 cm over the lower road at a handful of steep gore noses; 41
  buildings are missing where a road runs through their footprint.

## The test page (2026-09-28)

The Charlotte refinement is built on the `charlotte` branch and tested on
its own page until it is merged into the game (owner decision, 2026-09-28):

- **https://lucentll.github.io/psx-racing/city/** is published with
  `tools\build-and-publish.ps1 -PagesDir city`, with
  `$env:PSX_SANDBOX='C:\Users\mcgee\PSXCity'` (the branch's own sandbox,
  never `PSXBuild`). The game stays at the site root.
- **Only `main` publishes the game at the root.** A root publish (no
  `-PagesDir`) from this branch, from any branch but `main`, from a detached
  HEAD, or from a checkout git cannot read is refused before the build
  starts. The test folders beside the root would survive it, but the game
  at the root would be replaced by this branch's work in progress. So "ship"
  on this branch means `-PagesDir city` plus a push of the `charlotte`
  branch, never a root publish or a push to `main`. `-AllowRootFromBranch`
  overrides the refusal, and is only for when the owner has asked for this
  branch's build at the root.
- **Neither publish wipes the other.** gh-pages is still one orphan commit,
  force-pushed each time, so its history never grows (each build is about
  85 MB). But every publish now reads the live gh-pages first (trees only,
  under a second, no build bytes downloaded) and replaces only its own part:
  `-PagesDir city` replaces `city/` and keeps everything else, and a root
  publish keeps `city/` (`-KeepDirs`, default `city`) plus any folder holding
  a `psx-subpage.txt`. Before pushing, it checks that every kept entry has
  the same git hash (so the same bytes). It pushes with a lease against the
  commit it read, so a publish that lands in between makes it start again
  instead of being overwritten.
- **Main needs the root half too.** Until the commit "Publish keeps gh-pages
  test folders" is on `main`, a publish from `main` runs the old script and
  wipes `/city/`.
- **The page says it is a test.** The tab title is "PSX Racing - CHARLOTTE
  TEST", and a tag at the top of the loading screen shows the branch, commit
  and build time. `curl .../city/psx-subpage.txt` gives the same details.
  The tag is the first item of the splash column, in the flow, directly
  above the title. It first floated at the top of the screen. That cleared
  the title while the page was loading, but once START appeared the column
  grew by about 100 px and the title slid up under the tag: on a phone held
  landscape (640x360, 780x340) the tag covered half of "PSX Racing" on the
  screen where the owner taps START. In the flow it cannot overlap anything.
  On screens 420 px tall or less, the gap under the subtitle and the gap
  above START close up by exactly what the tag adds (34 px), so the test
  page's START screen is the same height as the game's and fits wherever
  the game's does.
- **Checked in a browser (2026-09-28)**, with the staged pages served
  locally. The loader was swapped for a stub, so the template's own code put
  each page into LOADING or READY. Every splash item was measured at 18
  sizes, from 1280x720 down to 568x268 landscape plus four portrait phones.
  The new page had no overlaps, nothing outside the screen and no text
  spilling out of its box in all 36 cases. On every landscape screen of 420
  px or less, its READY column was the same height as the root page's. A
  32-character `-PagesLabel` wrapped inside the tag and stayed clean. The
  previous page, run as a control, reproduced the overlap in 12 cases
  (640x360: tag 14-62 px, title from 46.5 px). The real page then loaded
  the live build to READY at 780x340 and at 640x360 (phone emulation) and
  measured the same as the stub.
- `-DryRun` does all of it against the live gh-pages and then stops before
  the push. It prints what is kept, replaced and removed, the changes per
  folder, and the upload size.
- **The test page has its own save database.** The career is PlayerPrefs.
  Unity's WebGL runtime keeps it at
  `/idbfs/<md5 of the page URL up to its last '/'>/PlayerPrefs`. Live, those
  folders are md5 of `https://lucentll.github.io/psx-racing` (`bcbc7f3e...`)
  and md5 of `.../city` (`2bcb837e...`). The folders differ, but Unity keeps
  them all in one IndexedDB database per origin, named `/idbfs`. A page
  loads the whole database into memory at start, and every save writes the
  whole of it back from memory. Entries whose timestamp differs are
  rewritten, and entries the tab does not hold are deleted. With one tab at
  a time that is harmless. With `/` and `/city/` open together (two tabs, or
  one left in the background on a phone), a save on either page restores
  the other page's career as it was when this tab loaded, and deletes
  anything the other page saved for the first time since. So a `-PagesDir`
  page carries a small script ahead of the Unity loader (`psx-save-db`).
  When Unity opens `/idbfs`, the script opens `/idbfs-city` instead. The
  game at the root is untouched and keeps the owner's career in `/idbfs`. No
  test page went live before this, so no save needs to be carried over.
  Two things are still shared by origin: the fullscreen choice
  (localStorage `psx.fullscreen`) and Unity's data cache. The cache is keyed
  by URL, so a phone that plays both pages stores two data files.
- **Checked in a browser (2026-09-28)**, with the published pages served
  locally and the live build. As a control, the city page was served with
  the new script cut out. The root saved twice while it was open: a newer
  version of a file it already had, then a file it had never saved before.
  One save on the control page put back the old version of the first file
  and deleted the second. With the script, the same sequence left `/idbfs`
  exactly as the root had written it. Then with both real games open, a new
  career on `/city/` ($1,377) was saved to `/idbfs-city`. The root career
  ($407) was saved with SLEEP, and then the city career was saved with
  SLEEP in the tab that had been open since before the root's save. Each
  save changed only its own database. After reloading both pages, each
  resumed its own career.

## Floating houses, STOP signs and traffic signals (2026-09-30)

**The floating houses.** The owner: "some houses are floating above their
foundations". The foundation skirt the prop baker puts under every prop ran
from 5 cm above the pivot down, whatever the model. The house pack's house
stands on a 0.59 m plinth above its pivot, and the trailers stand 0.24 m
above theirs. So every city house had a 0.55 m slot of daylight between the
top of its foundation and the bottom of its walls. With the house's 0.30 m
sink, the top of its foundation was also below the ground at the lot's high
corner, so the house hovered about 0.3 m over the grass there. Of the 3,110
houses and trailers probed, 3,109 showed more than 0.1 m of open gap (worst
1.37 m). The fix:

- The skirt now reaches up to 8 cm inside the model's own lowest course (and
  never lower than the old 5 cm), and down to 3 m below the pivot
  (`CityProps.SkirtDepthM`, `PSXRacingBuilder.AddSkirt`).
- `CityBuildings.SeatY` now seats a lot on the DRAWN ground
  (`CityMeshes.LatticeAt`: the corners, the edge midpoints, the centre and
  every lattice corner inside the footprint) instead of `GroundY` at five
  points. `GroundY` is the field the lattice samples, and it bulges between
  lattice corners.
- A lot whose drawn ground falls further than the skirt reaches
  (`CityProps.MaxFallM`) is left empty. That is 38 of the 3,110.

After the fix, 0 of the 3,110 show a gap. `CityRefSpots.RunSignals` prints
the four worst lots before and after.

**STOP signs and signals (the visible part of WP-26 to WP-28;
`Scripts/City/CitySignals.cs`).**

- *Where the controls come from.* The export's TAGN section (WP-10), now read
  into `CityMap.tagged`. The meaning of a STOP at a junction node (`stop=all`,
  `stop=minor`, `direction`) comes from `Resources/charlotte_stops.bytes`
  (`node tools/city/stop_tags.mjs`, keyed by OSM node id), so the graph is not
  re-exported.
- *Which junctions are signalised.* A signal at a junction node, or up to
  20 m before one, marks that node. Marked nodes within 40 m of each other
  are one junction, so a divided road's crossing is one junction.
- *Which approaches stop.* `stop=all` stops every approach. A direction tag
  stops that way. Otherwise the node's minor road stops; where both roads
  are the same class, both stop.
- *Counts* (from the static mask): 994 signalised junctions (3,329
  approaches) and 504 stop-controlled junctions (936 approaches, 103 of them
  all-way). Placed: 897 STOP signs, 4,265 stop bars, and 5,540 signal heads
  on 1,196 mast arms and 520 span wires (2,236 solid signal poles). 350
  approaches found no clear spot.
- *Where things stand.* Nothing stands in a lane, a driveway, a building or
  lot, water, under a deck, or in a race run-off. The check is
  `RoadsideOccupancy.RoadEdgeDistance` on the line model's edges plus the
  mask bits. The clear zone, sight triangles and corner spots are allowed:
  the mask reserves them for this furniture.
  - A STOP sign stands at the stop line on the approach's right.
  - A mast-arm pole stands at the far-right corner, found by walking just
    outside the right edge across the junction. Its arm carries a head over
    each inbound lane, up to three.
  - Outside uptown, a span wire runs between the far-right corners of an
    opposing pair, with a head hung where each inbound lane crosses it.
    Where no span strings, mast arms are used instead.
  - The junction's centre tile owns all of it, and the feet are marked on
    the mask before the poles, signs and trees.
- *Phones.* A tile's junction furniture is three draws whatever it holds:
  - one static mesh on the kit's `signals` atlas
    (`tools/city/signals_atlas.py`: the owner's pack metal, and the STOP
    sign from his Roads pack, `T (11).jpg`);
  - one mesh of lenses on ONE runtime material shared by the whole city. Its
    4 x 2 texture is rewritten by the clock when a phase changes;
  - one mesh of PSX/Halo halos, lit after dark and recoloured by vertex
    colour. `uv1.y = 10 + yaw` makes a halo one-sided, so nothing glows out
    of the back of a head.
- *The clock.* `CitySignals.AspectOf(group, t)`: green 14 s, yellow 3.5 s,
  all-red 1.5 s, per opposing pair. It is ticked by `CityWorld.Update`.
  Traffic obeying it is the next package.

## P0 (roads pass, 2026-10-02): the publish loop, the audit hooks, the tiers, two lanes

The owner's roads pass (merges, line meaning, no gaps, intentional
connections, smooth laterally and vertically; highways and primary roads
first, then minor roads, then neighbourhood roads and parking lots) runs as
two lanes at once. P0 is the groundwork both lanes stand on. It changes no
road: the city audit's output is the same by construction.

**Two lanes, disjoint files.**

| | Lane A | Lane B |
|---|---|---|
| Tree | `C:\Users\mcgee\PSX Racing`, branch `main` | `C:\Users\mcgee\PSX Racing-city` (a worktree), branch `city-pass`, reset from main by P0 |
| Sandbox | `C:\Users\mcgee\PSXBuild` (the default) | `C:\Users\mcgee\PSXCity`: `$env:PSX_SANDBOX` set for every tool run, tools run from the lane-B tree |
| Pushes | straight to main | `city-pass` only; reaches main at a merge point |

`city-pass` is a work lane, not an edition: it is never published from. Only
lane B runs `tools/city/export_osm.mjs` and commits
`Resources/charlotte_*.bytes`; binaries are never hand-merged (a conflict is
re-exported on the merged source and passes `export --check` and
`determinism.mjs`). One Unity job per sandbox at a time.

**Publishing: both editions, from main, from one commit.** Since the
2026-10-01 unification there is one codebase, and every release publishes
the root (MAIN) and `/city/` (CITY) from the same main commit.
`tools\build-and-publish.ps1`:

- `-Both` runs the script twice in the same sandbox: MAIN to the root, then
  CITY to `/city/` with `-SkipScenes` (the scenes are the same for both
  editions; only the WebGL build differs). It reads the commit and the
  tracked working tree before the first and again before the second, and
  refuses the second if either moved. A failed MAIN publish stops it, so
  nothing goes to `/city/`. Without `-Both`, the same pair is
  `-SkipScenes`, then `-SkipScenes -PagesDir city`, with nothing committed
  between them.
- **Only main publishes the root and `/city/`.** The old rule covered the
  root only, and `/city/` was published from the `charlotte` branch.
  `-AllowRootFromBranch` overrides both. Any other `-PagesDir` is a preview
  folder and is not tied to main.
- **The 'charlotte'-branch guard is retired.** It compared every
  `Resources\charlotte_*` file with the tip of the local `charlotte` branch,
  so the first `/city/` publish after any re-export would have been refused.
  An edition that carries Charlotte (CITY, ALL) is now refused only when its
  city data is uncommitted (`git status --porcelain` on
  `Resources/charlotte_*`, untracked files included). `-AllowOlderCity` is
  accepted, does nothing, and says so.
- **A `-SkipScenes` CITY build checks the sandbox's city data.**
  `-SkipScenes` builds the sandbox as it stands, so a sandbox last filled from
  an older source would put an older city on `/city/` under the new commit's
  name. The exported `charlotte_*.bytes` / `.json` in the sandbox must match
  the source's (SHA-256), or the publish is refused before the build;
  `tools\city-cycle.ps1` on that source brings them over.
  (`charlotte_thumb.png` is skipped: the scene build bakes it in the sandbox.)
- **The Bee cut-off retry.** About ten minutes into a WebGL build the editor
  gives up on bee_backend: `TaskCanceledException` in build.log, and
  `"interrupted","reason":"BeeDriver connection terminated"` in
  `Library\Bee\tundra.log.json`. Finished nodes are cached, so a re-run gets
  further. The build step now runs up to `-BuildTries` (default 3) times per
  edition. It retries after a cut, or after a run that left no
  `build_ok.txt` and no build error a retry cannot fix. A compile error, an
  IL2CPP error or a missing runtime shader stops it at once. `build_ok.txt`
  is deleted before every try, and every try's logs are kept in the sandbox
  as `build_<EDITION>_try<n>.log` and `tundra_<EDITION>_try<n>.log.json`.
  Unity is never killed: a try that outruns its 45 minutes is left running,
  and no second build is started beside it.

**`tools\city-launch.ps1` (new).** The launch audit (`CityLaunchAudit.Run`)
on its own. It copies code and data into the sandbox the way city-cycle
does (or `-NoMirror`), runs one Unity job through `unity-wait.ps1`, and
prints `city_launch.txt`. `-Box x0,z0,x1,z1` (game metres) sets
`PSX_LAUNCH_BOX` for a package's boxed launch (about 1-2 min). With no box it
is the release gates' "city-launch full". `-Label` names the run, and
`-OutDir` copies the reports out.

**`tools\city-cycle.ps1` (lane A's).**

- The Unity job's budget is 45 min. It was 25 min, and the audit alone took
  15.1 min on 2026-10-02.
- `-Full` sets `PSX_AUDIT_FULL=1`.
- `-NoRatchet` runs linecheck without `--ratchet` (lane B; see below).
- The report-scope switches are listed at the top of the script, and the
  ones that are set are printed when it starts.

**The audit hooks (`Editor/CityAudit.Hooks.cs`).** `CityAudit.Run` calls seven
partial hooks right after the LINE MODEL block, in this order:
`TwinReport`, `ProfileReport`, `PaintReport`, `MergeReport`,
`CoverageReport`, `LotReport`, `BulbReport`. Each takes
`(CityMap map, CityMeshes.Trims trims)`, and the buildings a tile build needs
are `AuditBuildings`. A package implements its hook in its own partial file
(`CityAudit.Twin.cs`, ...), and a hook nobody implements compiles away.

Each report gets its scope from `CityAudit.ScopeFor("TWIN")` (critic C7):

- **City-wide only under `PSX_AUDIT_FULL=1`.** That is for the release gates.
- **Otherwise boxed.** The box is `PSX_<NAME>_BOX`, else `PSX_AUDIT_BOX`
  (`x0,z0,x1,z1`, game metres), else `CityAudit.OwnerBox`: uptown inside the
  I-277 loop plus W 5th St over I-77, 3.0 x 3.3 km.
- **Tiers.** `PSX_<NAME>_TIER` or `PSX_AUDIT_TIER` (`1`, `1,2`, `all`).
  The default is all tiers.
- A bad value is named in the scope line and the default is used. A typo
  never quietly becomes a city-wide run.

NAME is one of COVER, TWIN, PROFILE, PAINT, MERGE, LOT, BULB. Each report
starts with `AuditScope.Describe()`. An agent reads only its own block of the
audit output.

**The tiers (`Scripts/City/CityTier.cs`).**

- **T1** = `cls >= 3`: motorway, trunk, primary and their links.
- **T2** = cls 1-2: secondary, tertiary and their links.
- **T3** = cls 0: residential, unclassified, living_street, service, plus
  parking.

`CityTier.Of(edge)`, `Short`, `Name`. The offline mirror is `citydata.mjs`
`tierOf` (lane B, B1).

**The audits' window onto the builder (`Scripts/City/CityMeshes.AuditView.cs`,
critic C4).** The Editor scripts compile into a separate assembly and see
only `public` members, while the roads pass's audits need the builder's own
answers. `CityMeshes.AuditView` is read-only and FROZEN: no signature
changes once it is on main, and anything new is an overload.

- *The last tile build's tables:*
  - `SectionAt` (a fresh cross-section) and `SectionsOf` (the sections as
    drawn), as `SectionView`.
  - `DrawnLines` / `DrawnLinesAt`: the builder's own trimming of the line
    model's lines at a section.
  - `ClipAt`, `ClipsOf`, `ClippedEdges`, `HostChainOf`, `HostEdgeAt` and
    `ClipPair`: the clip ranges.

  Ask right after building the tile that owns the place.
- *Records, kept across builds between `BeginRecord` and `EndRecord`:*
  - `Spans`: every span a tile draws, once city-wide, with each side's gap /
    rail / retain / cut / median / caps.
  - `Piers`: every pier stood, once.
  - `Gores`: every branch end's P and N. P is where the branch's outer edge
    leaves the host's edge. N is the last attached sample, where the gore
    ends and the nose stands. A gore is listed once per tile within reach,
    so de-duplicate by (node, branchEdge).

  Rails stay `CityMeshes.railLog`.
- *Sides:* -1 is the L vertex (the right of travel), +1 the R vertex (the
  left of travel; a one-way carriageway's median side).

In a build none of this records anything: the hooks in the builder are null
checks.

**The linecheck baseline from here on (critic C3).**

- `tools/city/baseline/linecheck_baseline.json` fingerprints the gate's
  inputs: the graph sections, `citydata.mjs`, `linesim.mjs`, every road PNG
  and the gate's code. Nearly every package moves one of them, and a STALE
  baseline fails `--ratchet`, which fails city-cycle.
- So the baseline is re-recorded **only on main**, by the lane-A agent
  performing a merge point, with `--write-baseline` and BEFORE -> AFTER in
  the commit. `--allow-loosen` is never used.
- Lane B never commits the baseline. It runs `city-cycle -NoRatchet` (the
  gate without `--ratchet`) and compares the per-check table with its own
  before-numbers.
- `linesim.mjs` and `linecheck_baseline.json` move to lane A at M6. Until
  then lane B edits `linesim.mjs` (through B6), so the "a CityMeshes change
  updates `linesim.mjs` in the same commit" rule (Smoothness gate, above)
  applies at the merge points instead: the lane-A agent brings the replica
  and the baseline level there.
- A5 and A15, which repaint the road PNGs, re-record in their own commits.

**These docs.** Both lanes write here. Edits are append-only, one section per
package, added before "Not in v1". Earlier sections are history: where one
disagrees with a later one (the test page above still describes publishing
`/city/` from the `charlotte` branch with `PSXCity`), the later one holds.

## A1 (roads pass, 2026-10-02): the mesh instruments - coverage, turning movements, compression, the owner's views

Before A1 no audit could see what the owner described: "no gaps", "not
short of merging, not going too far where texture pop-up happens later",
"dip down and go up under bridges ... at angles". The overlap census ignores
anything within 25 cm in height, the drive audit probes lanes along edges on
nine tiles, and the launch audit only sees crests. A1 adds the instruments.
It changes no road: the CITY AUDIT reads the same 4 failures, the launch
counter the same 121 / 711, the tiles are byte-identical with the tap on.

**COVERAGE** (`Editor/CityAudit.Coverage.cs`, the `CoverageReport` hook;
headless `CityCoverage.RunHeadless`, runner `tools\city-coverage.ps1`).

- Every tile in scope is built with `CityMeshes.RecordTap`, so each road
  triangle has an OWNER: the edge whose ribbon or deck it is (one owner for
  both, and for every paint column and span of the edge), the node whose fan,
  the host whose gore quad. The ground mesh is split into the 8 m LATTICE (all
  three corners on the grid) and the STRIPS laid off it (verges, seams, half
  strips, corner fills).
- Up-facing triangles are rasterised in plan in 8 m blocks: 0.25 m cells, and
  0.10 m in junction and gore NEIGHBOURHOODS (a fan node's trim + the arm's
  reach + 5 m; a gore quad + 5 m). Each cell keeps its top six surfaces.
- The INTENDED OUTLINE (up to four levels a cell): each edge's
  `LaneExtents` between its trims (squeeze, ease and clip applied; sections
  on the polyline vertex bisectors and on the mitre at a mitred node, as the
  builder cuts them; loop edges included), every fan's triangles, every gore
  quad, and each JUNCTION CLUSTER's convex hull (`CityJunctionClusters`: fan
  nodes joined by an edge the two fans swallow, or by a median crossing of
  30 m or less - one intersection drawn as several fans, until A11).
- The blocks, each m2 by tier (a pair takes the better tier):
  - `COVERAGE COPLANAR`: two owners within 5 cm. Relations: fan/fan (same
    cluster, other), fan over its own arm / a cluster mate's arm / a foreign
    road, arm/arm (two ribbons of one node), branch/host (a clip pair),
    ribbon/ribbon, gore/road, and the mitre seam of a through pair (noise
    level: 119 m2 T1 city-wide). The deck share is printed apart.
  - `COVERAGE HOLES`: outline cells (eroded one cell) with no road surface
    within 0.6 m of the outline's height, by outline kind (ribbon, deck,
    fan, gore, cluster box).
  - `COVERAGE OUTSIDE`: road surface 0.25 m beyond every outline at its
    height. Near zero by construction (fans and gores are their own
    outline): it catches a ribbon drawn past its own footprint.
  - `COVERAGE OVERSHOOT`: a clipped branch's surface over its host's
    (within 0.6 m), and of it past the host's far lane edge.
  - `COVERAGE UNDERLAP`: a surface 0.5-8 cm under a road surface, by what is
    under it (a road piece, a strip, the lattice): flicker at range.
  - `COVERAGE TAP IDENTITY` (the one Check): four tiles of the owner's box
    built with the tap off and on are the same numbers.
  - `COVERAGE DATA GAPS` (critic C9, report only, city-wide always: graph
    only): interior dead ends 0.3-6 m SHORT of another road at the same
    layer, or INSIDE its pavement, by tier, with the list. HOLES cannot see
    these (the outline is built from the same edges); B1/B8/B9's welds are
    measured here.
- Scope (critic C7): boxed to `CityAudit.OwnerBox` unless
  `PSX_AUDIT_FULL=1` or `PSX_COVER_BOX`; tiers `PSX_COVER_TIER`. A city-wide
  run also prints the OwnerBox and W5th sub-boxes, so a boxed package run has
  a number to compare with. City-wide: 728 s (13,938 tiles); the default box
  about a minute. Writes `city_coverage.txt` / `.json` at the project root.

**The launch audit's A1 blocks** (`Editor/CityLaunchAudit.cs`;
`tools\city-launch.ps1` now also copies `city_launch.json`). The LAUNCH
counter keeps its paths, so it stays comparable with 2026-10-02.

- `LAUNCH BY TIER`: the counter split (T1 46 / T2 63 / T3 12).
- `FLOWN TURNS OFF THE PAVEMENT`: flown turns whose break lies on land,
  counted APART from LAUNCH, as fails by tier (0 today: such samples have no
  height to fly from).
- `PATH MISSES BY TIER`: the flown paths' samples off their own pavement
  (nothing within 3 m / land first / a lower road), split into edge paths,
  movement arms and movement arcs.
- `TURNING MOVEMENTS`: every legal movement through every lone fan and every
  junction cluster, from each arm entering it to each arm leaving it (one-way
  respected, turns over 135 deg left out), on LANE-CORRECT lines (a right
  turn from the rightmost lane to the rightmost, a left from the innermost to
  the innermost, through on the rightmost; lanes counted in from the right of
  travel off the line model's extents), trim + 10 m to trim + 10 m, a sample
  every 0.5 m. Appended to the flown samples, read off the same meshes, never
  flown. A movement FAILS when a sample has no road of its own (the launch
  audit's 0.6 m level rule) and STEPS at 12 cm in 0.5 m. Lone fans and
  clusters apart, by tier (a movement takes its better arm's tier), with the
  turn type.
- `COMPRESSION` (critic C6): on the flown paths' 1 m samples every grade
  break is found (one sign, past 0.03 %, consecutive samples = one break) and
  a SAG break A is judged against what a vertical curve sampled at the
  solver's own stations breaks there: `A <= (h1 + h2) / (2 R_sag) + 0.1 %`,
  h1 and h2 the distances to the neighbouring breaks (each capped at 10 m),
  R_sag AASHTO's comfort radius at the design speed (owner_decisions / B2:
  motorway 65, trunk and primary 50, secondary, tertiary and freeway ramps
  40, other links 35, local 30, 25 mph in the last 30 m before a STOP or a
  signal; radii 2,774 / 1,640 / 1,049 / 803 / 590 / 410 m). B4's curves pass
  and a corner does not: I-77's trench V under W 5th breaks 6.74 % where
  0.44 % is allowed. One spot per 6 m, by tier and by the launch audit's
  causes.

**The views** (`Editor/CityPreview.cs`). `PSX_PREVIEW_SPOTS` takes shot
names or groups (`w5th_owner,merge_i85_e3031_far`, `merges`, `all`, a
prefix*). Unset, the preview shoots what it always did plus the W 5th group.
The NAMED VIEWS, each standing on the nearest edge with the given name to a
2026-10-02 plan point (so a re-export that renumbers edges moves nothing),
and `preview_spots.txt` says where each camera stood:

| Group | Views |
|---|---|
| w5th | `w5th_owner` (node 2069, heading 134, 1.2 m eye, near plane 0.2: the owner's frame, eastbound onto the bridge), `w5th_wb` (westbound from the east signal, heading 314), `w5th_west_junction` (top, n4116/n4117) |
| twin | `twin_i277` (e1910/e1921), `twin_e2437` (the negative case) |
| profiles | `prof_w5th_i77`, `prof_i277_belk`, `prof_sunset_i77`, `prof_johnston_i485`: side elevations, the roads alone, heights x5 |
| merges | `merge_i85_e3031_near` / `_far` (the I-85 entrance from 200 m and 570 m back, the far one through a 30 deg lens: pop-in at range), `merge_i77_e8`, `merge_i77_node0` |
| junctions | `fork_n9862`, `cluster_ntryon_harris`, `cluster_mallard_harris`, `cluster_pineville_carmel` |
| paint | `paint_morehead_e1427`, `paint_mtholly_e1967`, `paint_i85_mw6_e2121`, `paint_stryon_e11444`, `paint_westblvd_e10174` |
| lateral | `lat_pineville_10874`, `lat_steele_14505`, `lat_ntryon_11669`, `lat_gleneagles_20141` |

The reference spots gain `sv_w5th_west` (35.23801,-80.85467, heading 134):
`city-refspots -Before` shows the owner's frame before and after.

**The BEFORE baseline**: `tools/city/baseline/mesh_audit_baseline.json`
(main@4924a98d plus these instruments, graph 089d7141), city-wide with the
OwnerBox and W5th sub-boxes, the city-wide launch numbers, a boxed launch
over W 5th, the replica comparison and the caveats. The headline:

| Metric | T1 | T2 | T3 |
|---|---|---|---|
| COPLANAR m2 (fan/fan, arm/arm, branch/host, gore/road) | 1,382 / 3,486 / 1,117 / 30,223 | 456 / 4,958 / 1,631 / 27,331 | 14 / 151 / 49 / 1,098 |
| HOLES in cluster boxes m2 (clusters over 2 m2) | 20,742 (322) | 9,219 (251) | 315 (19) |
| OVERSHOOT branch over host m2 | 1,662 | 1,863 | 55 |
| UNDERLAP m2 (road / strip / lattice) | 35,558 / 80,071 / 5,166 | 32,270 / 48,553 / 6,297 | 1,563 / 2,174 / 2,174 |
| DATA GAPS short / inside | 3 / 0 | 3 / 1 | 18 / 3 |
| TURNING off pavement: lone fans, clusters | 12 of 2,211, 327 of 2,395 | 28 of 7,666, 243 of 2,545 | 7 of 12,691, 8 of 337 |
| COMPRESSION sag places (of them trench) | 11,846 (514) | 17,859 | 6,853 |

W 5th itself (the W5th sub-box): the west junction n4116/n4117 has 55 m2 of
grass inside its box and the WB left e5448 -> e2909 leaves the pavement for
1.5 m; the east junction 19 m2; the I-77 trench V breaks 6.74 % and 4.58 %.

**For the packages after this one.** Every check is REPORT state. A package
flips its own checks to `Check()` for its tier (A3: arm/arm, branch/host,
underlaps; A11: cluster holes and cluster movements; B4: COMPRESSION), runs
boxed (`tools\city-coverage.ps1`, about a minute, or its AuditOnly with the
default box), and compares with the OwnerBox sub-box of the baseline. The
release gates run `city-cycle -Full` (coverage city-wide, 12 min) and
`city-launch` (8 min).

## B1 (roads pass, 2026-10-02): twin decks are one structure - the table, one height, the TWIN report

The owner's example: West 5th Street over I-77 is ONE bridge with a median,
and Charlotte drew it as two decks 1.5 m apart - four parapets, a 0.92 m slot
down to the freeway, two pier lines out of step. OSM maps each carriageway as
its own way, so nothing in the data said the two decks were one structure. B1
says it, holds each such pair to one height, and measures all of it. It draws
nothing new: the mesh is plan A2's (lane A), which reads this table.

**OSM's bridge outlines (section BRST).** `fetch/fetch_ways.mjs` now also
asks for every `man_made=bridge` area (`cache/bridges_mm.json`; the shipped
copy is the bridges diagnosis's read-only fetch of 2026-10-02: 336 ways and
one multipolygon, today's outlines rather than the 2026-09-12 road snapshot).
`lib/bridges.mjs` gives each `bridge=yes` edge the outline holding at least
60% of its 4 m samples - its structId (way id, or relation id | 0x80000000) -
and the exporter writes them to the optional section BRST with two more
tables: the water spans a culvert claims (below) and the twin-deck overrides
(`tools/city/deckpairs_overrides.json`, FORCE / NEVER by way pair; empty:
owner Q7's default, trust OSM). 381 of 791 bridge edges sit in one of 215
outlines. CityMap reads BRST into `Edge.structId`, `map.culvertSpan` and
`map.deckOverrides`. The graph hash is unchanged (089d7141).

**The culvert creeks (owner Q8, critic C2).** A water span whose middle is
within 15 m of an OSM `tunnel=culvert` line is a creek OSM says is piped
under the road: 317 of 564 spans (T1 194, T2 120, T3 3), the critic's list
exactly. Package B2 converts them into culverts; until then they stay decks
exactly as they are, and no twin-deck union joins one: 26 pairs (380 m) that
would otherwise have been one structure are kept apart, among them W 5th
Street's own Irwin Creek pair e5446/e5449 east of the signal.

**The twin-deck table (`Scripts/City/DeckPairs.cs`, `map.deckPairs`).** Built
at load from the graph alone, before the solve; the same rule step for step
as `tools/city/deckpairs.mjs`:

- DECKS (critic C1): a `bridge=yes` edge end to end, an over-edge within
  `CityElevation.DeckReach` of its crossing, a water span.
- PAIRS: every 2 m of deck, the nearest deck on each side running within 15
  degrees, its foot inside that edge's deck, the gap between the two
  ribbons' facing edges (`lmPlus` / `lmMinus`) under 20 m; kept when one edge
  sees the other for 10 m. The gap is the median sample.
- ONE STRUCTURE (owner_decisions.md): gap <= 0.3 m no (the squeeze or the
  clip already joins them); another OSM layer no; an override; a culvert's
  creek no; the same outline yes up to 20 m, two outlines never; else G =
  6.1 m for one motorway's or trunk's two carriageways, 9.1 m for an
  arterial's, 3.05 m for a ramp beside its road (or two ramps), 1.2 m for any
  other pair.
- Each pair carries its kind, tier, median (Barrier: motorway, trunk, ramps,
  arterials over 80 km/h; Raised: other arterials; Flush under 1.2 m;
  `DeckPairs.DyLimit` 0.46 / 0.15 / 0.05 m), each edge's range beside the
  other and the arc maps between them (`Pair.ArcOnOther`). `map.deckPairsOf`
  / `map.deckUnionsOf` index them by edge.
- **Completed on the solved structure** (`DeckPairs.Complete`, at the end of
  the solve): the solve also puts decks where the facts named none - the
  3.5 m margin's untagged viaducts, a seated ramp on its host's deck, a deck
  end carried to its twin's, the station either side of every deck - and two
  of those side by side draw four parapets like any other. The census runs
  again on `ElevatedAt`: a facts pair keeps its decision and grows its range,
  a new pair is decided by the same rule and marked `Pair.solved` (it took
  no height hold).

**One height (CityElevation, critic C12).** `HoldTwinDecks` raises each
union deck's stations inside the pair's range to its twin's height there
(RaiseHump: flat top, 4.5% cones), raises only, INSIDE the cone loop - which
now ends only when the holds move nothing either - so the cones carry each
hold into the approaches and the vertical curves round its crests. A station
more than 1.0 m off its twin is left (a split level: the union ends there,
A2). The curves round each carriageway on its own chain (its own junction
planes, pins, crossing windows) and part the twins again; re-running the
curves after a second hold parted them again (0.198 m left) and lifted the
stations round every crest they could not settle. So the last word is a
POINTWISE hold of the interior stations beside the deck (no cones), then a
CREST GUARD: a raised station whose break is now past the curves' own crest
limit (and worse than before) comes back down to it, never below where the
curves left it. (Raising a station sharpens the crest only at that station;
where the raise stops short of a crossing - an edge end, a seat, a deck end -
the plateau's corners are crests, and the guard takes them back: 26 on
2026-10-02.) Then, after MarkStructure, `AlignTwinStructure` carries a deck
end on to its twin's when it stops at most 15 m short (one abutment line for
A2: 2 pairs, +4 stations - the deck ends mostly coincide at the 10 m station
spacing already), and the land under a union's gap is capped under the
soffit out to the middle of the gap (`DeckPairs.UnionCapReach` in `Ground`),
where the twin's own cap takes over. `PSX_CITY_TWINHOLD=0` turns the holds,
the alignment and the cap off (the TWIN report's before-numbers).

**What the holds reached (2026-10-02, PSXCity).** TWIN (e), the twins'
height difference every 2 m on the union decks: p50 0.090 -> 0.002 m, p95
0.603 -> 0.086 m, max 0.998 -> 0.291 m (4,580 / 4,601 samples; 59 more
samples over 1 m apart are split levels, e.g. Tuckaseegee Road 9.5 m, I-77
e1876/e1880 1.15 m). The interior of every deck is level; what is left is at
the DECK ENDS: an edge's end station is its node's, shared with the
approaches, and is held only from inside the pair's range. Holding the end
stations too (tried: `TwinArc`'s window a station past the range in the cone
loop) took (e) to p95 0.013 / max 0.150 m but its cones ran through the
nodes into the approaches and lifted 146 more stations over the 3.5 m margin
(3,224 against 3,078). The B1 gate (p95 2 cm, max 5 cm) therefore still
FAILS; closing it needs the end stations held without lifting the approaches
over the margin - a twin-aware vertical-curve pass (plan B4's limiter) or
3DEP approach profiles (B7). Side effects of the holds: the 3.5 m margin
stations 2,831 -> 3,078 (+8.7%, over the plan's +5% allowance for this
pre-existing failure: the lower twin's approaches are lifted to the higher
one's abutment; 1,010 of the 3,078 are on or beside a twin deck's edge);
the grade check improves (8 stations over 16%, worst 29.4% on the Blair Road
bridge twins -> 3, worst 17.5%); clearance worst 4.70 m unchanged; DRIVE
AUDIT zeros; rail census 0 runs / 0 m; pits dug under a deck 653 -> 635-648;
overlap census 636 -> 616-628 m; one more LAUNCH spot in the boxed launch
over uptown, I-277 and I-77 (16 against the baseline's 15 there: 0.152 m at
the East 11th Street fan, node 675, -1153,4853), all 15 earlier spots
unchanged. Load: the facts table ~120-170 ms at parse, the completion
~230 ms in the solve (editor).

**The TWIN report (`Editor/CityAudit.Twin.cs`, the `TwinReport` hook).** The
table by tier, then: (a) inner rail metres, (b) open slot (down-rays across
the gap), (c) walls crossed lane to lane beyond the designed median, (d) the
partner's piers - these four build tiles and run in the report's scope
(`ScopeFor("TWIN")`, boxed by default) and REPORT until A2 flips
`TwinMeshGated`; (e) the twins' height difference every 2 m (gate: p95 2 cm,
max 5 cm, on what the holds govern) and (f) an independent finder on the
solved structure (gate: 0 T1 pairs within their G kept apart without a
reason) read the solved map only and cover every pair. The table goes to
`twin_pairs.csv` beside city_audit.txt: `node tools/city/deckpairs.mjs
--compare <it>` checks the facts table against the offline census
(2026-10-02: 310 of 310 decisions agree once the tool replays
`LineModel.FixMirrored`'s nine re-offset edges).

**The table's numbers (2026-10-02).** 310 parallel deck pairs, 272 more than
0.3 m apart. The facts table makes 155 of them one structure (T1 89, T2 66;
9,330 m of carriageway pair, 18,660 m of inner parapet for A2 to take
away) and keeps 26 culvert pairs apart. Against the bridges diagnosis's 183:
183 = 155 + 26 culvert pairs + e1518/e1550 (an I-485 ramp 12 m from its
mainline whose samples fall mostly in two different outlines: the 60% rule
reads TWO where "any shared outline" read ONE) + e2316/e2352 (I-277 at US 74,
one of `LineModel.FixMirrored`'s re-offset edges: drawn overlapping, so the
squeeze joins it). Completed on the solved structure: 599 pairs, 243 one
structure (T1 153, T2 90; 12,790 m), 88 of them on decks only the solve made
(I-277's untagged viaduct pieces, West 5th Street's own west approaches
e1252/e5448). TWIN (f) is 0 against that table. In the default box, the
mesh today (A2's before-numbers): T1 51 union pairs with 5,268 m of inner
rail, 2,625 m of open slot, 1,338 samples crossing a wall beyond the designed
median, 65 partner piers; T2 12 pairs, 767 m, 390 m, 201, 11. West 5th Street
(e1253/e5445, outline w984482059, Raised median): 150 m of inner rail (2 x
76), a 75 m open slot, 2 walls crossed lane to lane, 2 partner piers; its
two decks stand at most 0.079 m apart after the holds (0.030 m with the end
stations held too), the I-277 viaduct e1910/e1921 0.163 m (0.034 m).

**`tools/city/deckpairs.mjs`.** The census and decisions offline (BRST, or
the cache when the file predates it), the W 5th / I-277 lines,
`--csv`, `--review` (the owner's Street View list:
`tools/city/baseline/deckpairs_review.csv`), `--write-baseline` /
`--check` (`baseline/deckpairs_baseline.json`, the union set by way pair),
`--compare`. citydata.mjs gains `tierOf` (CityTier's mirror), TAPR's records
and offsets, and BRST.

**Dead ends short of a road (critic C9).** The exporter now gates every
interior dead end within 6 m of a same-level road's pavement: T1 must be
welded or explained (T2 4 and T3 21 reported for B8 / B9). Weld only a
same-name end or a link onto its road, never a true non-connection. The
three T1 ends are all REAL connections to roads the snapshot does not carry,
so none is welded and each is explained in `DEAD_END_EXPLAINED` with its
evidence: the two I-85 entrances near 35.258,-81.006 start at a roadside
facility (both carriageways leave by a link that dead-ends 320 m upstream:
its service roads are not fetched), and the Johnston Road slip
(way 1233246203) turns right onto way 1233246204 (OSM restriction 16858537),
a street outside the residential coverage (Q6, B9). Welding any of them onto
the road beside would invent a connection.

**Size (critic C17).** `tools/city/baseline/roads_pass_size_ledger.json`:
charlotte_city.bytes 5,685,880 -> 5,692,964 B (+7,084: BRST 6,864 + 12 of
table + 208 B of attribution - main@9a3e54ee added a car-model credit row to
SOURCES.md without re-exporting, so `export --check` failed on main before
this). Running cap +2.5 MB through R6.

## M1 (roads pass, 2026-10-02): B1 merged into main

`city-pass@dc932b51` (B1) is merged into main on top of A1 (`7aad17a4`), and
`city-pass` is fast-forwarded to the merge. From here, main carries the
facts in B1's section (BRST, `map.deckPairs`, the twin height holds, the TWIN
report) and A1's instruments together. Lane B has A1's coverage, launch and
preview tools.

- **Conflicts.** There was one, in this file: A1 and B1 each appended a
  section before "Not in v1", and both are kept in commit order. The rest of
  the merge was disjoint. A1 touched only Editor audits, tools and
  `mesh_audit_baseline.json`. B1 touched `tools/city`, the City scripts and
  the data. No binary needed merging.
- **Data.** `export_osm.mjs --check` passes: 15 of 15 inputs match the
  manifest, and the four shipped files are IDENTICAL, with
  `charlotte_city.bytes` at 5,692,964 B, sha 5338fd9c. The fingerprint is
  identical. `determinism.mjs` passes: two processes wrote the same bytes.
  The check ran in the lane-B tree, because the gitignored caches live there.
  Its `tools/city` and `Resources` match the merge exactly: the merge is
  city-pass plus A1's 9 files, none of which the exporter reads.
- **linecheck (critic C3).** The baseline went STALE on one input: the
  builder replica's digest, because B1 changed `citydata.mjs` (611baa93 ->
  fc463ee9). Every reading was identical. It was re-recorded on main with
  `--write-baseline`, without `--allow-loosen`, and the ratchet passes again.
  The before and after numbers are in the merge commit.
- **What main now carries from B1's open items** (B1's section has the
  details):
  - **TWIN (e) fails.** p95 0.086 m and max 0.291 m against the 0.02 / 0.05
    gate, at the deck ends. This is a fifth CITY AUDIT failure beside the 4
    known ones.
  - **3.5 m margin stations: 2,831 -> 3,078 (+8.7 %).** The release gate
    allows +5 %.
  - **One new marginal LAUNCH:** 0.152 m at the East 11th St fan, node 675.

  The R1 gate cannot pass until B2 or B4, or an owner/orchestrator decision,
  settles these.
- **Baselines measured before B1's heights.** A1's
  `mesh_audit_baseline.json` (COMPRESSION, LAUNCH, coverage) predates B1, and
  B1 moves heights. A boxed comparison after M1 must allow for that, and the
  R1 gate re-records.
- **PSXBuild's city data is now older than main's.** Run a city-cycle (or a
  verify) in PSXBuild before any `-SkipScenes` publish. Otherwise the CITY
  half refuses (P0).

## A2 (roads pass, 2026-10-02): twin decks drawn as one structure

The owner's example, West 5th Street over I-77, was drawn as two decks 1.5 m
apart: four parapets, a 0.92 m slot down to the freeway, and two pier lines
out of step. B1's table (`DeckPairs`, `map.deckPairs`) says which parallel
decks are one structure. A2 draws them that way. The code is in
`Scripts/City/CityMeshes.Unions.cs` (new), with small hooks in `CityMeshes.cs`.

- **Runs (`BuildDeckUnions`, once per map, at the end of `ComputeTrims`).**
  - Every union pair gets one run over its whole range on both decks. B1's
    TWIN probes measure that range.
  - The owner is the pair's lower edge index. It draws the median to the
    nearest of `across`: the partner edge, plus the edges a mitred node
    carries the partner on into. Twin decks rarely end square: W 5th's
    westbound deck starts 1.66 m past the eastbound one's end, so the slab
    sweeps across onto the approach there.
  - A run ends only where nothing stands beside it (a 1 m foot slack) or
    the two carriageways are more than `UnionSplitDyM` = 1.0 m apart in
    height. That is a split level, matching B1's TwinHoldMaxDyM.
  - Two drawing runs on one side of one edge share their overlap at its
    middle. The cut is carried along the primary owner and partner link only.
- **Approaches (critic C21).** From a run end where both carriageways come
  down to the ground, the owner's road is walked on through mitred nodes.
  The median carries on while the road across stays beside it, on the
  ground, at one height (`SharedGuardDyM` for curbs and Jerseys, 0.10 m for
  flush), and short of a junction fan's trim (1 m set back).
  - Raised medians: up to 80 m, gap 0.6-6.1 m (A16's grass line).
  - Jerseys and flush strips: the 20 m approach-rail band only. The rest of
    the freeway median is A12's.
  - The inner approach rails stand down wherever this median is laid.
- **Sides (`SideFlags.union`, `UnionSideHere`).** A union side carries no
  rail, Jersey, cut wall, verge, kerb face or fascia. It counts as union only
  as drawn:
  - An owner's road across must stand at least 5 cm apart. A branch clipped
    onto it does not.
  - A partner must lie beside an owner run that covers it, with that owner's
    drawn edge at least 10 cm away. Otherwise it keeps its own rail or verge.
  - Run ends are section positions. They are not added inside a clip.
- **The median (`EmitUnionMedian`, one emitter for A16 too: critic D4).**
  The owner draws it span by span, in pieces of 2 m or less. Each piece runs
  from its own drawn edge to the partner's drawn edge (`DrawnEdgeAt`: the
  partner's sections as this tile cut them).
  - **Flush:** a strip one inch down, tucked 0.10 m under both roads.
  - **Raised:** the same, plus two battered 0.10 m mountable curbs (top set
    back 8 cm) and a top that runs from one carriageway's height to the
    other's.
  - **Barrier:** a 0.81 m Jersey on the gap's centre line, its top over the
    higher side, so a split level up to 0.46 m stands as a taller face on
    the low side.
  - On a deck: the soffit continues across the gap at DeckThick.
  - At a closed run end: on a deck, an end face down to the soffit and a
    rail across the slab's end; on the ground, a battered end curb or a
    Jersey cap.
- **Piers (`EmitBent`).** The partner stands none inside a run, and a lone
  pier is never nudged into one. The owner stands a bent: a column under
  each carriageway (the owner, its partners, and theirs; up to 4) and a
  0.9 m cap beam from fascia to fascia. The bent is nudged as one unit off
  the roads below. Where no nudge clears the whole bent, the columns that
  clear on their own stand without a beam.
- **No new draw calls.** The strip and curbs use the owner span's own road
  slot (its paint-free shoulder texels). The soffit, end faces and bents use
  the Concrete slot that every deck span already uses. Jerseys and end rails
  go in the barrier mesh. All 17 budget sites have identical draws against
  lane B's latest run, which has no unions.
- **Elsewhere.**
  - `RoadsideOccupancy` marks the ground under a union's gap as under a
    deck, so no tree grows through the slab.
  - `LampSideClear` keeps lamps off union sides.
  - `AuditView.UnionRunsOf` and `SideView.union` expose the runs to audits.
  - `DescribeSide` prints `union(owner|partner of eN ...)`.
  - `CityPreview` adds `w5th_top`.
- **The audits.**
  - `CityAudit.Twin.cs`: `TwinMeshGated` is now true, so (a)-(d) are checks.
    One correction to (b): a Barrier median's own Jersey stands on the slab
    at the gap's centre, and its top (0.81 m up) counts as cover.
    Otherwise every Barrier union read as a hole at the centre ray. These
    are the only two edits to that (lane B) file.
  - `DriveAudit` adds the W 5th lateral: rays 0.5 m over the deck from one
    carriageway's inner lane to the other's must cross only the designed
    median.
  - The audit prints `CityMeshes.LastUnionReport`.

**BEFORE -> AFTER** (B1's run on the OwnerBox for BEFORE, PSXBuild after A2):

| | Before | After |
|---|---|---|
| TWIN (a) inner rail, T1 / T2 | 5,268 / 767 m | 249 / 0 m |
| TWIN (b) open slot, T1 / T2 | 2,625 / 390 m | 129 / 3.0 m |
| TWIN (c) walls, T1 / T2 | 1,338 / 201 samples | 70 / 3 |
| TWIN (d) partner piers, T1 / T2 | 65 / 11 | 2 / 0 |
| W 5th (a)/(b)/(c)/(d) | 150 m / 75 m / 38 (2 walls) / 2 | 0 / 0 / 0 / 0 |
| W 5th lateral, walls crossed | 2 | 0 (Raised, designed 0) |
| rail on the 117 roadside tiles | 51.2 km | 43.4 km |
| city-wide | - | 238 of 243 union pairs drawn, 12.8 km of run; 235 approach runs, 4.9 km |

- **The T1 residue is three I-77 pairs** by W 5th: e1255/e6672, e1876/e1880
  and e1877/e1891. Today's solve has them 1.05-1.15 m apart: B1's split
  levels, the 5 m carriageway step in the trench V. They get no run, which
  accounts for 249 m, 128 m, 65 samples and both piers. B2 (I-77 NB/SB
  within 0.5 m) joins them with no change here.
- **Left after that:** 1.0 m slot + 5 wall samples (T1) and 3.0 m + 3
  samples (T2), at run ends of I-277 ramp/mainline pairs and West 4th
  Street Extension.
- **Roadside.**
  - LIP and the lane survey are back to their baseline.
  - The rail census reads 8 runs of 1 m (baseline 0). Six are at the start
    of a partner edge at staggered I-277 nodes (e2305, e2312, e2313, e2349,
    e2351, e6403); one is the e2141/e8481 approach; one is Shopping Center
    Drive e13174.
  - FACE reads 1: North Sharon Amity Road e6162, a raised median's end curb
    0.4 m off a verge.
  - These are open, listed in the release log.
- **Unchanged:** DRIVE AUDIT zeros; "dug under a deck" pits 648 -> 648;
  boxed launch (-4000,3300,-1000,6600) 16 LAUNCH / 81 UNLOAD (B1's run,
  same spots, routes 0/0/0).
- **Budget.** Tile build p95 is 101.4 ms (B1 runs 104.8-108.0; lane B 106.4).
  `BuildDeckUnions` takes 91 ms in the editor, against the plan's 50 ms.
  The solve's own mid-solve `ComputeTrims` (ramp seats) skips it.
- **Smoothness (report-only) runs:** A1 +3, A5 +1, B1 +4, B2 +16, B3 +3,
  C2 +2. The worst values are unchanged. The extra runs come from the new
  sections at run ends.

**For the next packages.**
- **A12** carries the freeway median Jersey on from the 20 m approach band
  (`ApproachRailM`) where these runs stop. Today the centre Jersey is
  capped there and each carriageway's own median Jersey starts capped.
- **A16** reuses `Profile` / `EmitUnionMedian` (Raised, on the ground) for
  arterial medians.
- **B6 (TAPR offsets) widens W 5th's median.** The slab follows the drawn
  edges, so it widens by itself.

## B2 (roads pass, 2026-10-02): creeks piped under the road, the PROFILE audit, one trench decision per crossing

Three things, in the order the owner's answers put them: the creeks OSM says
run through a pipe stop being bridges (owner Q8), the road's vertical design
is measured (the PROFILE audit and the offline VERTICAL section), and a
street over a freeway is either dug under or humped over - never both, and
never one carriageway each way (the owner's West 5th Street over I-77).

**Culverts (owner Q8, section CULV).** Every road crossing a county/USGS
creek line used to get a railed deck. A crossing is now a CULVERT when, by
geometry (`tools/city/lib/culverts.mjs`): an OSM `tunnel=culvert` /
`culvert=yes` waterway passes within 15 m of the crossing point AND crosses
the road's ribbon or passes within 3 m of it (its paved half width plus a
metre for the line model's offsets) within 15 m along the road; the road is
not tagged `bridge=yes` (OSM's word wins) nor a tunnel; and its deck would
hold no other water. A crossing of the same creek within 25 m of a piped one
is on the same fill (a ramp seated on its mainline, the other carriageway)
unless it is tagged a bridge. 320 candidates: 312 piped (T1 193, T2 116, T3
3; 3.21 / 1.88 / 0.05 km of deck gone, 564 -> 252 water spans), 8 left as
bridges - 7 tagged `bridge=yes` (Eastway Drive's two carriageways over
Edwards Branch, East 7th Street over Little Sugar Creek, ...) and East
Independence Expressway over Edwards Branch, whose culvert line passes 4.8 m
outside its ribbon. Every candidate, its decision and its reason is in
`tools/city/baseline/culverts_q8.csv`. In the owner's spot: West 5th
Street's e5446 / e5449 over Irwin Creek east of the signal and I-77
southbound's e6608 (the water span that exempted it from the cut) are
piped.

- **What a culvert is in the game.** No span: no deck, no parapets - the
  road keeps its embankment. The creek line runs on under the road, as a
  WP-25 ravine does: `CityElevation.HoldCulverts` holds the road at least
  `CityCulverts.MinFillM` (1.6 m) over the creek's carved floor across the
  creek's flat floor (raises only, before the vertical curves; the stations
  stay grounded, the 3.5 m margin never makes a deck of it or of a road
  within 30 m of it), so the road's fill always stands over the water and
  the sheet under it is hidden; the water shows again where the fill comes
  down to it. `CityCulverts` walks the creek from each crossing (section
  CULV's list, not a segment search) both ways to the first place clear of
  every road's pavement and clear zone where the drawn ground comes down to
  the WATER, and stands the WP-25 end there - a headwall with its backfill
  where the fill meets it, else a projecting pipe - with its clay ditch. The
  tile builder draws them with the ravines' code, unchanged.
- **Data.** Section CULV (optional; layout in `lib/citydata.mjs`): per
  crossing the culvert way, the creek (WATR index) and its arc, the road and
  its arc, the hold's half length along the road, the creek's bed there.
  BRST's culvert table is empty from B2 on (a piped creek has no span; a
  span left is a bridge), so no twin-deck pair is kept apart as a culvert
  creek any more (`deckpairs.mjs`: pairs 310 -> 255, culvert pairs kept
  apart 26 -> 0, unions 155 -> 155, the same set). `charlotte_city.bytes`
  5,692,964 -> 5,694,168 B (+1,204: CULV +8,740, SPAN -3,744, BRST -3,804,
  table +12; size ledger). Graph hash unchanged (089d7141); `export --check`
  and `determinism.mjs` pass.

**The PROFILE audit (`Editor/CityAudit.Profile.cs`, the `ProfileReport`
hook).** Station-only, so it covers every chain and crossing city-wide in
the scope's tiers (the box limits only the listed lines); about 0.3 s.

- CURVES (REPORT, plan B4's): chains through the nodes' through pairs (the
  VerticalCurves rule), consecutive same-sign breaks of 0.1% or more as one
  curve, judged L >= max(K A, 3V ft) at the owner's design speeds (motorway
  65 mph, trunk/primary 50, secondary/tertiary 40, local 30, ramps 40, loop
  ramps 30 - a link turning 120 degrees at a mean radius under 120 m, 164
  edges - and 25 in the last 30 m before a stop or a signal); sags fail
  under AASHTO's comfort K (V^2/46.5), target the headlight K; crests by
  stopping sight distance. (A1's COMPRESSION judges links at 35 mph below
  trunk class; this report follows owner_decisions' 40.)
- GRADES (REPORT): station grades over the class maximum (5/7/9/12%, ramps 8%).
- CROSSINGS: clearance under the deck (REPORT: 4.9 m over a freeway, 4.4 m
  over a street), separations over ClearanceM + DeckThick + 1.5 = 7.05 m
  (REPORT), and the CHECK: no T1 DOUBLE separation (a dug freeway whose
  street still stands over 7.05 m).
- DECISIONS: the trench rule's crossings grouped by (over road, under road,
  within 60 m); CHECK: no group mixed (dug and humped), and no ramp beside a
  dug mainline left out of the cut. Every crossing the B2 rule decided
  differently is in `trench_redecided.txt` beside city_audit.txt.
- PAIRS: a divided road's two carriageways under one street, across the
  median at each crossing's section; CHECK T1 |dy| <= 0.5 m.
- W 5TH: the four crossings line by line; CHECK the two I-77 carriageways
  within 0.5 m and the separation over the northbound at most 7.05 m.
- Every crossing to `profile_crossings.csv` beside city_audit.txt.

**One trench decision per crossing (`CityElevation.SinkTrenches`).** The
decision layer only (critic D1): the cut is still the 4.5% V the solver
always made; B4 rounds it, B7 replaces it with 3DEP profiles.

- A crossing's cut is blocked by water only where the cut REACHES it
  (depth / 4.5% + 10 m along the freeway, through its nodes) - not by a
  water span anywhere on the edge.
- `TrenchEndM` (20 m) is measured along the freeway to its nearest real
  junction (a node of three arms or more, or a dead end), through way splits.
- Crossings of one street over one freeway within 60 m are ONE decision: all
  dug to one bottom (the lowest any member needs), flat across every
  member's section on each carriageway (a skewed street crosses the two
  carriageways metres apart along the freeway) - or, where any member's cut
  would reach water or every member stands at a junction, none.
- A ramp passing under the same street within 60 m of a dug mainline's
  crossing, within 25 degrees of parallel, is dug too, for its own street
  (no deeper), climbing out at 8% - through nodes where only ramps meet (West
  5th's ramp e338 meets e8463 40 m past the street).
- A dug crossing's deck runs on through the street's node into the street
  beyond when the crossing is that close to it.
- `RelaxTrenches`: the raise loop can still lift a street over its cut (a
  junction cone, a twin hold, a crest the curves round); the cut then gives
  the excess back - each carriageway by its own spare clearance, at most
  0.4 m more than the group's other carriageway, a ramp by no more than its
  carriageways, never above the profile before the cut and never past
  another cut there - inside the cone loop and after the curves' first two
  rounds (critic C12: the last round rounds it).
- `PSX_CITY_TRENCHRULE=0` gives the rule before B2 (a measuring override).

**The offline VERTICAL section (`metrics.mjs`, `lib/vertical.mjs`).** The
solve emulated in about two seconds (step 1, the trench rule - either -, the
raises, cones, relax; no water spans, seats, twin holds or curves) and the
PROFILE numbers on it, plus 3DEP's real separation at the tall ones when the
1/3" cache is there (`--vertical-old` adds the old rule for a BEFORE).
`tools/city/baseline/vertical_baseline.json` records BEFORE and AFTER.

**What it reached (2026-10-02, PSXCity; BEFORE = B1 at main@8ea140b6, AFTER =
the B2 run of the committed rules, before the three corrections below).**

| | BEFORE | AFTER |
|---|---|---|
| W 5th: I-77 northbound vs southbound under the street | 5.09 m apart | 0.24 m (real 0.4) |
| W 5th: separation over the northbound | 10.99 m | 5.70 m (real 4.9-5.5) |
| DOUBLE separations, T1 | 54 | 1 (East 7th Street over the I-277 ramp e521, 7.30 m) |
| MIXED decisions | 31 groups, 119 crossings | 0 |
| T1 carriageway pairs over 0.5 m apart, crossing points | 98 of 327 | 56 |
| ... across the median (the check) | (not measured in game; emulated 153) | 51, worst 1.38 m (emulated after the slack correction: 84) |
| crossings dug | 213 | 327 (63 of them ramps), 97 of 103 groups dug, 6 at grade |
| separations over 7.05 m, T1 | 93 | 22 |
| clearance under 4.9 m over a freeway (REPORT) | 0 | 1 (4.88 m, a ramp over I-85; the worst, 4.70 m, unchanged) |
| T1 sags short of the comfort K / corners | 5,627 / 1,268 | 5,581 / 1,171 |
| water spans | 564 | 252 (312 culverts) |
| 3.5 m margin stations | 3,078 | 2,803 |
| creek culverts: ends / headwalls / both ends | - | 330 / 288 / 125 of 312 |
| "every culvert keeps its embankment" | 1 (e1979, known) | 2 (+ Highland Creek Parkway) |
| grade past 16% | 3, worst 17.5% | 6, worst 17.6% (I-277's ramp e329) |

Of the old rule's skips (`tools/city/baseline/trench_decisions_b2.csv`): the
46 "water span somewhere on the edge" are 21 over roads that are freeways or
ramps (no trench candidates; the instrumented old rule named the water
first), 21 now dug and 4 at grade for water within the cut's reach; the 41
"within 20 m of the edge's end" are 30 dug and 11 at grade (every crossing of
the group at a real junction).

**Corrected after that run (typecheck and the emulation only; the R1
gate's city audit and full launch audit are their first full check):**

1. The vertical curves' Up-only window round a creek pipe (the water span's
   rule, kept) is dropped on a road that also carries a trench's cut and
   stands more than 2.4 m over the pipe's cover: I-277's ramp e329 runs 5 m
   over Little Sugar Creek's pipe right before its cut under East 7th Street,
   and the window made the curves round the cut's lip by lifting the road
   4.2 m (17.6%).
2. `RelaxTrenches` bounds what a carriageway gives back by its group's least
   TOTAL plus 0.4 m; bounding each round let the slack add up (Lakeview Road's
   two carriageways of I-77 1.38 m apart). Emulated pairs over 0.5 m 118 -> 84.
3. HoldCulverts holds two carriageways of one road below secondary (the
   classes PairedRoadBaseY does not read at one midline) over one pipe within
   a metre of each other, raising the lower: Highland Creek Parkway's stood
   3.3 m apart, the land graded to the lower, the higher on nothing. Five
   pairs city-wide, among them West 5th Street's over Irwin Creek.

**The boxed launch (uptown + W 5th + I-277, -4000,3300 .. -1000,6600; run on
the corrected code with correction 1 applied to every pipe and correction 3
to every class - both narrowed afterwards):** 17 LAUNCH spots against 15 in
the 2026-10-02 baseline and 16 after B1: the same 15, B1's East 11th Street
fan (0.240 m now), and one new, Elizabeth Avenue at its signal with North
Kings Drive (node 6995, e5917, 0.542 m, T2) - the junction where Elizabeth
Avenue's e9267 crosses Little Sugar Creek's pipe 2 m from the node. Its
cause is not established; the broad form of correction 1 freed that pipe's
window, which the narrowed form does not. Routes in the box: 0 LAUNCH.
Solve 2.81 s (trenches 30 ms).

**Not reached (open for B4/B7 or the gate):** DOUBLE 1 and the carriageway
pairs (the check reads 51 before the slack correction) are not at 0; the
creek culverts stand both ends at 125 of 312 (the WP-25 walk misses an end
where no channel shows on the lattice within 60 m - 134 - or the walk never
clears every road's clear zone - 54); the three corrections are unmeasured
in a city audit; the Elizabeth Avenue launch spot is open.

## L1 (roads pass, 2026-10-02): B2 merged, R1 published, one lane from here

`city-pass@113bebaa` (B2) is merged into main on top of A2 (`6a1cf396`), and
release R1 ("West 5th": twin decks as one structure, creeks piped under the
road, one trench decision per crossing) is published to both editions from
that main commit. The lean plan replaces the two lanes: every later step works
in this tree, with the Unity sandbox `C:\Users\mcgee\PSXBuild`. `city-pass` is
left equal to main, and `PSX Racing-city` / `PSXCity` are not used again.

- **The merge.** It had one conflict, in this file (the A2 and B2 sections),
  and both are kept in commit order. `export_osm.mjs --check` passes in this
  tree, where the caches now live: 15 of 15 inputs, the four files IDENTICAL,
  and `charlotte_city.bytes` at 5,694,168 B.
- **linecheck (C3).** It was STALE on SPAN and the replica, as B2 left it, and
  was re-recorded on main without `--allow-loosen`. The worst values are
  unchanged. B2 KINK went from 96,864 runs and 747,802 m to 96,842 runs and
  747,970 m, and D1 from 817 to 818. The numbers are in the merge commit.
- **The CITY AUDIT on main (OwnerBox scope, PSXBuild) has 13 failures.**
  - **The 4 known ones:** 3 grades over 16 %, worst 17.5 % (B2's culvert-window
    correction brought it back from 6); 1 ledge; 2,803 margin stations
    (3,078 after B1, 2,831 before it); and culvert embankments, now 2.
  - **TWIN e:** p95 0.080 m and max 0.291 m, at the deck ends.
  - **TWIN a/b/c/d:** 249 m, 132 m, 74 and 2.
  - **Rail census:** 8 runs, 8 m.
  - **FACE:** 1.
  - **PROFILE DOUBLE:** 1.
  - **PROFILE PAIRS:** 15 of 327, worst 1.05 m. It was 51 in B2's last run,
    before its relax-slack correction.

  The DRIVE AUDIT reads zeros, and W 5th passes both of its checks: the lateral
  check, and the I-77 carriageways 0.24 m apart with a 5.67 m separation.
- **Boxed launch** (-4000,3300,-1000,6600): 16 LAUNCH (T1 3 / T2 10 / T3 3)
  and 86 UNLOAD. The race routes have 0 LAUNCH. B2's new Elizabeth Ave spot
  (node 6995) is gone. Only the East 11th St fan (node 675, 0.24 m) is new
  against the 15 baseline spots.
- **Before the publish.** `city-rebake` passed: CITY BUILD OK, 4 scenes.
  The watched `city-play-check` has 1 failure, the known one. The drive-off
  finds no cut spot on the routes (2 fill, 0 cut, 2 level), the same as in
  PSXCity and PSXRec since 2026-09-30. Every car is on its street, at street
  height.
- **Known, carried forward:**
  - **TWIN a-d residue.** Three I-77 pairs south of W 5th (e1876/e1880,
    e1877/e1891, e1255/e6672) stand 1.05-1.15 m apart. That is past A2's
    1.0 m split-level line, so they get no union run. The fix is the vertical
    pass (L3/B4) holding them within 1 m, with no A2 change.
  - **TWIN e** at the deck ends (L3).
  - **The Highland Creek Parkway culvert HOLE.** e19428 stands at 110.40 and
    e13028 at 107.07 over one pipe, 10.5 m apart. B2's culvert twin hold does
    not hold it in the final solve.
  - **The E 11th St fan launch** (L7 junctions).
  - **The rail census and FACE** at staggered union ends (A2 item 3/4).

## L2 (roads pass, 2026-10-03): lines mean what MUTCD says - the line set, unmarked local streets, two texels a line

Every edge now carries its own **line set** (section LSET), and the ribbon
paints that set rather than its profile's symmetric default. Yellow is drawn
between the two directions wherever they meet, including on an uneven split.
A centre turn lane is drawn only where the tags say so. Local streets carry
no paint (owner Q1). Every line is at least two texels wide on a 256 px
texture (Q3). Lane counts and profile rows are unchanged, so every width is
identical.

- **The data (`tools/city/lib/lineset.mjs`, read by `citydata.mjs` and by
  `CityMap`).** Section LSET holds 8 B per edge:
  - u8 nF, u8 nB (lanes along a->b and against it);
  - u8 centre (0 none, 1 double yellow, 2 TWLTL);
  - u8 flags (1 marked, 2/4 a turn bay for forward/backward traffic from
    TAPR's turn-bay records, 8/16 lane_markings yes/no, 32 unpaved);
  - u16 turn-only lanes (bits 0-7 forward, 8-15 backward; from turn:lanes);
  - u8 sub-class (residential, unclassified, living_street, service,
    parking_aisle, driveway, alley, drive-through, track);
  - u8 bike lanes and shoulders, left and right.

  A count lent by lineclean's flicker/short/inferred fixes now lends the
  donor's split too, in the receiving piece's own frame.
- **The TWLTL rule.** A TWLTL needs `lanes:both_ways >= 1`,
  `turn:lanes:both_ways` or `centre_turn_lane=yes`. It is never drawn where
  `lanes:forward + lanes:backward = lanes`: that road is an uneven split
  with a double yellow. Three cases stay TWLTL without tag evidence, and the
  exporter lists them with `PSX_LSET_DIAG=1`. In total that is 25.4 km on
  167 edges:
  - an odd count with no forward/backward tags (22.9 km);
  - `forward + backward + 1 = lanes`, which is the tags' own remainder
    (1.5 km, 'implied');
  - a lent count from such a donor (1.0 km).

  Where the profile cannot hold the tagged lanes, they are shared in
  proportion without evening the split. That covers a 7-lane street drawn as
  six, and a tagged TWLTL on an even count padded to the next odd profile.
- **Q1 marked.** An edge is marked if it is tertiary and up, or if its
  tags say lane_markings=yes, lanes >= 3, or forward/backward. lane_markings=no
  unmarks any road.
- **metrics LANES (BEFORE -> AFTER):**
  - TWLTL painted on contradicted tags: 108.4 -> **0 km**;
  - uneven splits drawn symmetric: 131.5 -> **0 km**;
  - TWLTL painted: 223.5 -> 48.3 km. Of that, 25.7 km has no evidence on the
    edge's OWN tags, because metrics does not follow a lent count;
  - unmarked: 0 -> 515.9 km (T1 0.4, T2 0.1, T3 515.3; the T1/T2 part is
    lane_markings=no).

  `charlotte_city.bytes` grows by 200,352 B (5,694,168 -> 5,894,520), under
  the +250 KB cap. `export --check` reads IDENTICAL.
- **The paint rule (`RoadProfiles.LinesFor`), one table for the painter and
  the line model.** Across from the plus edge:
  1. the edge line;
  2. the nB lanes, with broken white between them;
  3. the centre: a double yellow, a TWLTL (solid outside, broken inside), or
     nothing on a two-way road one lane wide;
  4. the nF lanes;
  5. the edge line.

  An unmarked edge has no lines. `PaintLines(profile)` is
  `LinesFor(profile, its default split)`, so the 84 textures paint as
  before. Line centres stay on their 12 cm grid: 6 cm in from the shoulder,
  and a pair 24 cm apart. The smoothness gate's plan (`linegate.mjs`) mirrors
  that grid and did not move.
- **Q3 (`RoadProfiles.TexWidthOf` = 256 for all, `PaintHalfOf`).** A line's
  half width is 6 cm, or 1.01 texels where a texel is wider than that.
  - The five 512 px textures (mw4/5/6, tw5t, tw6) are now 256. The importer
    had capped them to 256 anyway, which left their lines 1.2 texels wide.
  - Their lines, and xw4's, widen to two texels. Only the width grows: line
    centres keep their grid, so the gate's plan does not re-key.
  - **One exception (`YellowHalfOf`).** tw6's double yellow pair (22.5 m at
    256 px, 8.8 cm a texel) keeps its 12 cm lines, 1 texel each with 2 texels
    between them. Two 2-texel lines and a gap texel do not fit the pair's
    24 cm. Widened, they merged into one 4-texel band: the smoothness gate
    failed A0 TEXTURE (8 runs) and two S1 probes. Moving the pair to 27 cm
    re-keys the gate's centre lines (an `--allow-loosen` re-record), so that
    is offered to the owner, not done.
  - The tw6 PNGs were patched for that rule offline. The painter is replicated
    exactly: every other texel matches Unity's own output.
- **linecheck (C3).** The baseline was re-recorded on main for the road PNGs
  and the replica (`citydata.mjs`), with no `--allow-loosen`. The ratchet
  passes and so do all 231 probes. BEFORE -> AFTER (texel quantisation of the
  wider lines; the geometry did not move):

  | Check | Runs | Metres |
  |---|---|---|
  | A0 | 0 -> 0 | |
  | A1 | 76,835 -> 76,875 | 935,157 -> 935,421 |
  | A3 | 12,897 -> 12,891 | |
  | A5 | 15,227 -> 15,274 | |
  | A5b | 25,117 -> 25,146 | |
  | B1 | 170,658 -> 170,626 | |
  | B2 KINK | 96,842 -> 96,879 | 747,970 -> 748,819 |
  | C2 | 14,606 -> 14,607 | |
  | D1 | 818 -> 818 | |

  Every worst value is unchanged (B1 19.3 -> 19.2).
- **The line model (`LineModel.LayoutOf(edge)`).** It returns the profile's
  texture layout when the line set is the default, and then the ribbon is
  drawn exactly as before (one quad where the sections allow). Otherwise it
  builds an edge layout, cached by (profile, nF, nB, centre, marked). Each
  line is drawn from a texture column that carries its paint (`srcM`):
  - the same line where the texture has it there;
  - otherwise a solid white, solid yellow, or broken column of the same
    colour;
  - a broken line whose texture has no broken column of that colour (a 2+1
    split's white on tw3t) is cut from the solid column into 10 ft dashes at
    the V phase (`StripDashed`).

  Pavement comes from the texture's widest paint-free run. An unmarked span
  is one quad of it.
  - **On a full-width span, identity runs** draw every stretch where the
    texture already shows the right thing as one strip at the quad's own U.
    Only the differences get strips of their own.
  - `LinesAt`, `DrawnLines`, the tapers' partner lines (the narrow EDGE's
    layout now), `AuditView`, `CitySmooth`, `LineModelProbe` and the LINE
    MODEL report all read the edge layout. `CitySignals.MakeApproach` takes
    the inbound lanes and the divider from the line set (`DividerLat`).
- **The PAINT report (`Editor/CityAudit.Paint.cs`, the B3 hook).** It reads
  the lines as drawn every 10 m between the junction trims, with no tile
  build, against the line set's expectation:
  - V1 centre kind; V2 centre more than 5 cm off the boundary (outside
    tapers); V3 colour; V4 any line on an unmarked edge;
  - V9 lines under two texels;
  - BEFORE: the profiles' own lines.

  **OwnerBox:**

  | Tier | V1-V4 after | Two-way km wrong before (V4 km before) |
  |---|---|---|
  | T1 | 0 | 1.7 of 7.9 |
  | T2 | 0 | 2.2 of 15.3 |
  | T3 | 0 | 0.1 of 1.0 (V4 44.6 km) |

  **T1 city-wide:** 23.0 of 135.7 two-way km wrong before, 0 after.
  **V9:** 0 of 160 lines in 33 layouts. tw6's pair rule came after that run;
  V9 now lists the pair as kept, not as a fault. Gates: V1-V4 = 0 on T1 (box
  and city), and V9 = 0.
- **The CITY AUDIT (OwnerBox, PSXBuild)** has the same 13 known failures as
  L1. The DRIVE AUDIT reads zeros. Draws are identical at every budget site.
  - Verts: trade_tryon 574k -> 583k (+1.6 %), i277_uptown 572k -> 577k,
    plaza_midwood 452k -> 452k, and the others within 1k.
  - Tile p95: 98.2 -> 102.1 ms.
- **Shots** (3, `PSX_PREVIEW_SPOTS`, before the tw6 pair rule; none of them
  is tw6):
  - `paint_morehead_e1427`: W Morehead's 2+1 split has its double yellow at
    the boundary. The 2-lane side's broken white is cut from the solid column
    in 10 ft dashes.
  - `paint_mtholly_e1967`: Mount Holly Rd 1+3. The double yellow is on the
    1|3 boundary. The edge is one 152 m TAPR taper (4 -> 2 lanes), and its
    whites end inside the taper, as they did before.
  - `paint_i85_mw6_e2121`: I-85 mw6 with two-texel lines.
- **Known, for L4.** The LINE MODEL through-lane continuity count rose
  636 -> 1,921 lines (core 62 -> 118). The cause is 681 two-arm joins of one
  profile where OSM's split changes at the node, which the symmetric paint hid:
  - 360 are the 3-lane 2+1 / 1+2 swap;
  - 198 are a TWLTL meeting a double yellow;
  - 119 are other split changes (tw4, tw5t);
  - 4 are marked meeting unmarked.

  The double yellow steps across a lane there. The MUTCD answer is a
  centre-line shift taper, which is L4's job (B6/A6: through lanes keep
  position, standard taper lengths). AI and race paths still drive the
  lanes' centre (`LanePoint`), so they do not cross a moved yellow.
  Per-direction lane paths are L5's job.
- **Not done (lean, offer later):** the offline paintcheck mirror and its
  baseline (B3), the PAINT HASH parity (A5), V5-V8 (A8 merge paint, L6),
  and the 8 px palette margin (A5).

## L3 (roads pass, 2026-10-03): one bridge reads as one bridge, sags rounded, seams under the surface - R2

L3 has four parts. The owner's two notes on the R1 top view of West 5th
Street over I-77 come first. The vertical-curve limiter now works on sags
too. The seam strips drop under the surface beside them. The per-arm fan
trims are written, but they are switched off.

- **The join rule (DeckPairs.cs, `tools/city/deckpairs.mjs`).** The owner
  said: "There are plenty of split bridges. This one should not have been."
  Without an OSM `man_made=bridge` outline, two carriageways now join only
  when the gap is 6.1 m or less, for every class. The 9.1 m arterial band
  is gone. The same rule covers pairs on decks the solve made. The 101 ONE /
  25 TWO outline decisions are unchanged. Unions went from 155 to 144 (T1
  89 -> 84, T2 66 -> 60), and 11 rule unions are two structures again.
  `deckpairs_baseline.json` has been re-recorded.
- **One surface per structure (`CityMeshes.ShareSurfaceAges`).** A road's
  age decides whether it is drawn new or old: light or brown concrete on a
  deck, black or grey asphalt on the ground. The age was chosen once per
  chain, and the two carriageways of a divided road are two chains. Every
  chain is now joined to the chain across its median. That means a union
  deck's partner, and the opposite carriageway of any divided road: one-way,
  not a ramp, the same name, running the other way within 40 m. Each joined
  group takes one seed, its lowest. Both halves of a bridge are now one
  concrete, and both approach carriageways are one asphalt.
- **One abutment line (`CityElevation.UnionAbutments`, after
  `DeckPairs.Complete`).** Each end of a union is walked out from the pair's
  middle, through two-arm nodes. The deck that stops short is carried on to
  the line square to the pair through its twin's end. This is capped at
  `TwinAlignMaxM` (15 m) and never passes a junction. A station is inserted
  exactly on that line, and every per-station array (stS, stY, stElev,
  stSeat, stSag, TerrainProfiles) is kept in step. This changes structure
  only; no height moves.

  The mesh now treats a span as deck by its MIDDLE (`ElevatedAt`), which is
  the rule the land under it already used. The deck surface used to run on
  over the span before the structure end, as far as the previous section
  (a station or a vertex), so two twins ended that far out of step. The
  outlines' `deck[]` uses the same rule.

  The line is square to the pair, not skewed parallel to the road below:
  the ribbons' cross-sections are square, and a skewed end would need
  skewed sections through the rails, medians and verges.
- **Sags (`VerticalCurves`, plan B4).** The limiter is two-sided. A sag's
  break must satisfy brk <= (h1 + h2) / 2 R_sag. R_sag is AASHTO's comfort
  radius at the owner's design speeds: 65 mph motorway, 50 trunk and
  primary, 40 secondary, tertiary and ramps, 30 local and loop ramps, and
  25 mph over the last 30 m before a stop or signal. Those give 2,769,
  1,639, 1,049, 590 and 410 m. The speed table (`CityElevation.VerticalMph`
  and `IsLoopRamp`) is now shared with the PROFILE audit.

  **It ships OFF (`PSX_CITY_VSAG=1` turns it on), because both audits broke
  the profiles:**
  - Run 1 used B4's 3.0 / 3.0 m caps and let the stations that may only
    rise (decks) round sags. Decks ratcheted up every round: I-485 over
    I-77 and a Brookshire Freeway ramp (e1251) ended 3-9 m off their seats
    and nodes. Grades went over 16 % at 32 spots (worst 41 %), with 55 lane
    steps, 2 under-height crossings, and union pairs split up to 8.8 m.
  - Run 2 went back to 1.5 / 0.8 and moved FREE stations only. It still
    pumped. A sag lowers its free neighbours, and a crest beside a pin
    raises them, so the node or deck station beside a pin walked 2-5 m:
    I-485 52 %, I-77 / I-277 bridges 20 %, 174 lane steps, unions split
    4.6 m.

  The rounding itself works: corners (A >= 2 % over <= 10 m) went from
  1,171 / 1,504 / 1,177 to 131 / 234 / 58 (T1 / T2 / T3). The next attempt
  is RAISE-ONLY sags (a sag lifts its free middle and lowers nothing), tried
  offline first. The crest radius is unchanged (the launch audit's).

  A road over a culvert that has lost its Up-only window now has a floor
  instead: the pipe's cover.
- **Seam strips (plan A3 FD8).** The inch-down seam under a stood-down side
  now drops 8 cm under the surface above it within 6 cm of the crack, and
  stays there.
- **Per-arm trims (plan A3 FIX-3, `PSX_CITY_ARMTRIM=1`, OFF).** Each arm is
  trimmed by its own need, using the facing sides' extents, and never by
  more than the old common trim. The first audit measured it in the
  OwnerBox and found it worse:
  - coplanar arm/arm T1: 127 -> 183 m2;
  - fan over its own arm T1: 27 -> 74 m2;
  - two lane mouths at node 13336 sat on another arm 0.4 m off.

  A curving arm needs A3's chord directions (FIX-2). This is left for L7.
- **The PROFILE audit** adds a count per tier of sags whose mean radius
  L / A is under the comfort radius. This is the B4 measure; the comfort-K
  count also applies AASHTO's 3V minimum length.

**BEFORE -> AFTER**
- **deckpairs:** unions 155 -> 144 (T1 89 -> 84, T2 66 -> 60), 9,330 ->
  8,804 m. 11 parted: ways 1079342990/1079342992, 172473104/172473106,
  184071819/215490404, 240074350/791634480, 272969923/272969924,
  297810913/326565808, 326220607/326220611, 493245238/597403827,
  49589368/545164014, 65996427/65996466 and 66224167/1068112998.
  `--compare` with the game's table: 255 decisions agree, 0 differ.
- **One abutment line (the solve):** 321 deck ends carried on to their
  twin's line on 154 pairs, up to 15 m, with 320 stations inserted. 135
  ends were left: a junction, or more than 15 m.
- **W 5th St over I-77** (`shots/r2/w5th_top.png`, `w5th_owner.png`): one
  concrete across both halves. Both ends sit on one straight line, square
  to the road, where R1 had them staggered and a dark wedge at the
  north-west end. The two approach carriageways are one asphalt. TWIN for
  W 5th: inner rail 0, slot 0, walls 0, piers 0, lateral rays 0.
- **The I-277 viaduct** (`twin_i277.png`): one concrete.
- **Seam underlaps (OwnerBox, coverage), verge/seam strip:**
  - T1: 4,863 -> 1,782 m2
  - T2: 1,883 -> 1,042 m2
  - T3: 340 -> 208 m2
  - Road-piece underlaps are unchanged (gores: A7/A8).
- **FACE** (body box back onto a grounded edge): 1 -> 0 in both runs.
- **Heights as shipped (the boxed launch, OwnerBox):**
  - The vertical-curve rounds match R1 to the digit (2,621 -> 220
    crests, 22,951 projections).
  - LAUNCH T1 3 / T2 10 / T3 3, UNLOAD 86, routes 0 / 0 / 0 (R1: the
    same).
  - Solve 2,649 ms.

  No height moved, so there was no rebake.
- **Not measured as shipped:** the city audit. Both runs carried the sag
  limiter. Their height failures (grades, TWIN a-d split levels, drive
  steps, the overlap census, the margin stations) went with the limiter,
  and the heights now match R1's. The mesh-only checks those runs did
  measure are listed above.

**Not done (lean):** A3 FIX-2 (chord arm directions), the geometric branch
test, FD7 re-entries, and JoinBuriedEnds. Also not done from B4: the SSD
crest radii, the adaptive station step (it would double the stations on
local streets), the C1 blends (BlendEndsToNodes, ClimbOut, HoldBridges),
and the clearance inequality.

## L4 (roads pass, 2026-10-03): lanes line up - the split decides the side, each direction on its own outside, the relay

L4 makes the through lanes keep their place across joins and junctions. The
data now takes the side a lane opens on from the same split the paint uses
(L2's line set), and the line model draws the three cases the old rules
could not.

- **The side comes from the split (`lib/lineclean.mjs` TAPR,
  `lib/lineset.mjs` `makeSplitOf`).** TAPR used to guess each direction's
  lanes from the count; L2's line set read them from the tags. Where they
  disagreed, the wrong edge moved. A 1+1 road meeting a 1+2 one put the
  extra lane on the forward side, so the through lane jumped 3.7 m across
  51 junctions. Both now read one function.
- **Each direction on its own outside (owner Q4).** On a two-way road, a
  change of lane count is split by direction: the forward lanes change on
  the right, the other direction's on the left. The centre line and the
  through lanes stay put. A centre turn lane that opens with them goes to
  ONE side, as a turn bay does. Mid-block, where that would carry the
  ribbon more than half a lane off its line, the side that keeps it nearest
  wins, so the offset never drifts (Steele Creek Rd had run 38 m off it in
  the first try). TAPR writes a record for each moving side. The line
  model eases both edges over ONE length (`TwoSided`), and the lines
  between the edges pair from the centre outward (`PairFromCentre`): the
  lanes a direction adds are its outer ones.
- **Merge side (plan B6 FD1).** A lane that opens or ends where a link (or
  a one-way fork of 35 degrees or less) joins a one-way chain is on the
  branch's side: 249 changes, 80 of them on the left. Such a node is never
  re-anchored.
- **Chains across a name change (FX2b).** At a node of three or more arms,
  the straightest pair of one class (dot < -0.94) now chains across a name
  change, for TAPR and PARA only. A road that changes its name at a
  junction keeps its lanes' position through it.
- **No jump at a re-anchor (FX3, SHIFT records, TAPR flag 16).** A run more
  than a lane off its line is still re-anchored at the next junction, but
  the run past it now starts where the lanes are. It shifts back over the
  MUTCD shifting taper (L/2, at least the class floor). At a MITRED node
  the join's own eases already draw the step, so the shift is skipped
  there. There are 118 records, and 82 are drawn.
- **Splits at junctions (FX2a, `lib/splits.mjs`).** A two-way road that
  becomes its two carriageways AT a junction (a cross street at the node:
  4+ arms) is a split node too, with its median taper: SPLT went from 891
  to 961.
- **Standard lengths (FX4).** A turn bay is 12:1 within 30-55 m. An added
  lane is 15:1. A dropped lane uses the MUTCD merging taper (WS^2/60 below
  45 mph, WS from 45). None is ever under the class floor.
- **The relay (`LineModel.Relay`).** This is a join of two arms of one
  width whose line sets differ: 2+1 against 1+2, a double yellow against a
  TWLTL. L2 drew each edge's own split, and at 681 such joins the centre
  line jumped a lane. Now each arm's lines ease to where the two layouts
  meet. The length is the MUTCD merging taper where a lane line ends, or
  the shifting taper where lines only move. It is centred on the node, and
  an arm with no room gives its share to the other. A line with no partner
  ends where its arm reaches full width. A width taper on the same spot
  moves the line on from the relay's place. The builder draws a relayed
  span line by line (`Relayed`), and PAINT counts it as a taper.
- **The LINE MODEL report (`Editor/CityAudit.LineModel.cs`)** gains OwnerBox
  and T1 counts, and three new lines: LANE ALIGN (fan junctions, the travel
  lanes in against the travel lanes out), TAPER (one-sided eases steeper
  than 10.6 degrees at their middle) and OFFSET (edges more than a lane off
  their line). The symmetric-widening check now accepts a per-direction
  widening (Q4). `CityAudit.LineModelOnly` runs this report and PAINT
  alone, in about a minute (`-executeMethod
  PSXRacing.EditorTools.CityAudit.LineModelOnly`, writes `line_model.txt`).

**Numbers (city-wide unless boxed).**
- Through-lane continuity at joins: 1,921 -> 647 lines (pre-L2 636).
  The OwnerBox has 8 (T1 1); T1 city-wide 113.
- Relays: 682, with 1 jump left at one. 369 are shorter than the MUTCD length
  for want of room.
- LANE ALIGN at fan junctions: 128 (T1 30) through lanes off. In the OwnerBox:
  14 (T1 1). The offline replica measured 752 before
  (T1 203; OwnerBox 81, T1 27).
- Symmetric widenings: 0, plus 175 per-direction widenings (Q4).
- OFFSET: TAPR offsets more than a lane off the line went 597 -> 316 edges.
  The worst was 12.80 m and is still 12.80 m. The Unity count after the
  mirrored fixes is 324 (T1 162, OwnerBox 6).
- Twin decks: 144 -> 145 unions, with 5 joined and 4 parted (re-recorded).
  The carriageways moved with their lanes: an I-277 pair that had
  overlapped by 1.78 m now stands 1.68 m apart.
- linecheck (re-recorded; a data move): A1 76,875 -> 76,957 runs, B2
  KINK 96,879 -> 97,073, D1 818 -> 829; every worst value unchanged.
- Boxed launch (OwnerBox): LAUNCH 16 -> 18 (T1 3 -> 4, T2 10 -> 11),
  UNLOAD 86 -> 92, uptown route 0 -> 1 UNLOAD. Solve 2.65 -> 3.04 s. New
  spots include Elizabeth Ave x N Kings Dr (-1311, 3745), 0.50 m, where
  the tw2 arm now sits a lane over to line up, and South Blvd (-2573,
  3880), 0.45 m, beside a merge-side change.
- CITY AUDIT: 17 failures. Against L2's 13: TWIN f 3, the lane survey
  (4 runs not named) and the verge steps (29) were already failing in
  both L3 audits (19 / 20 verge points there). The 2 PAINT failures were
  relays; the relay-as-taper exclusion was checked by `LineModelOnly`:
  PAINT T1 0 / 0. DRIVE AUDIT zeros.

**Known, left.**
- TAPER: 2,490 of 6,701 (T1 811, OwnerBox 71) eases are steeper than 10.6 degrees. These are short
  OSM pieces that leave no room. The plan's narrow-side taper (FX4 flag 32)
  is not written; L5's merge zones extend the aux lanes.
- 0.06 m: a one-way's single yellow edge line meeting a double yellow pair
  is 6 cm off the pair's line.
- One-way <-> two-way continuations that are not splits, and centre turn
  lanes that open AT a junction mouth. One direction there still jumps a
  lane across the junction. That belongs to the junctions package (L7).
- OFFSET: one-way chains that add lanes on one side up to a junction
  (Pineville-Matthews Rd ow4, 14.6 m).

## L5 (roads pass, 2026-10-03): merge zones - the ramp ends at its nose, the host carries the lane, AASHTO lengths

Before L5 a ramp and its mainline were never one pavement. The ramp was
clipped to the mainline's edge and narrowed to a zero-width sliver tens of
metres before OSM's merge node, and the mainline did not widen at all: the
merging lane rode the shoulder, and where OSM adds the lane at the node it
faded in from nothing over 90 m (diag/merges M1-M3). L5 (plan A7 + A9, owner
Q2) gives every tier-1 merge and diverge one pavement.

- **The nose N (`CityMeshes.Merge.cs`, `BuildMergeZones`, run once per
  map at the end of `ComputeTrims`, after `BuildEases`).** For every one-way
  branch of a tier-1 host at a mitred node (a ramp, or a one-way fork at 35
  degrees or less), the ramp is walked away from its node until its inner
  LANE edge meets the host's through-lane edge (gap + both shoulders = 0,
  bisection to a few cm). Over 35 degrees there it is a junction mouth and
  is left alone. Plan geometry only, no heights, so the elevation's own
  `ComputeTrims` and the tiles see the same zones.
- **The ramp ends at N with shared vertices.** Its last section is laid on
  the HOST's cross-line at N: the inner vertex on the host's own edge, the
  outer vertex on the widened edge, both at the host's height - the same
  points the host's section at N has (`ApplyZoneCut`; the host gets a
  section at N and one 2 cm past it, `ZoneForcedSamples`). Nothing of the
  ramp is drawn between N and its node: no narrowing strip, no collapsed
  sliver, no seam strip. The end section sits where the ramp's OUTER vertex
  crosses that line, and no other ramp section lies within its skew, so it
  never folds over the one before.
- **The host carries the lane (`LineModel.Ease.aux`).** An aux ease widens
  the host on the ramp's side from N: k lanes (style G: the lanes OSM adds
  or drops at the node - that join's 90 m mouth ease no longer narrows the
  pavement (`centreOnly`), though the lanes' centre still eases over it so
  traffic drives as before; style T: the ramp's own lanes), full width
  through the node, the shoulder eased from the ramp's at N to the host's
  at 20:1. Extents (pavement, roadside, decks, verge) include it; the lanes'
  centre does not. The edge line on that side rides the aux lane's outer
  edge, so the ramp's edge line runs on into it; the dotted line between
  the aux lane and the through lanes is L6's (A8).
- **Owner Q2: extended to DOT length.** Where OSM leaves less than
  AASHTO's full-width length from N - acceleration (Green Book exhibit
  10-70) for a merge, deceleration (10-73) for a diverge, by the host's
  design speed (owner_decisions: motorway 65, trunk/primary 50 mph; a
  posted speed above wins) and the ramp's (40 mph, a loop 30) - the lane
  runs on at full width past the node, then the taper: merge 90 m on a
  freeway or ramp host, else the MUTCD W S; exit 75 m / 30 m. New pavement
  only on the ramp's side. Clamped at a fan junction, a tunnel, a twin-deck
  union, a lane change on that side, and short of the next entrance's zone;
  run on at full width into the next exit on the same side (an auxiliary
  lane). Style G keeps OSM's lane length (reported).
- **Not zones.** These are drawn as before: a ramp whose run beside its
  host lies under another zone's aux lane, a zone on a twin-deck union or
  whose ramp is one, ramps whose lanes are apart at the node, and nodes
  where OSM adds more lanes than the ramp brings. The clip now meets the
  widened edge, so nothing is drawn twice.
- **The builder's side.** The host stands its verge or rail down from N
  exactly. The gore opens AT N (its first quad starts from the end
  section's inner vertex). The 2 cm span from the end section to the
  ramp's first collapsed one is skipped (its sides lay across the lane).
  A gantry lays its panels over the road's own lanes (`ExtentsNoAux`) and
  reads its clearance under them; a driver point 150 m back is kept on
  that road's pavement.
- **Lanes, the AI and traffic follow (plan A9, `ZoneLanePoint`).**
  `LineModel.LanePoint` on a ramp between N and its node is the host's aux
  lane, moving over to the host's lanes by the node; the race line samples
  it every 3 m (`ZoneSamples`). Traffic, the grid and the AI read only it.
- **The MERGE report (`Editor/CityAudit.Merge.cs`, the hook; also in
  `LineModelOnly`).** Zones, ROOM (lane room before/after), CUT, NOSE
  (shared vertices: GATE), Q2, SIDE, SEAT, LANE PATH, the routes that use a
  zone, and why the other branch ends are not zones.
- `PSX_CITY_MERGEZONES=0` draws L4's merges; `PSX_CITY_ZONELANES=0` keeps
  L5's drawing with L4's lane paths (the A/B the race check used). The
  preview group `zones` (CityPreview) shoots three of them from the driver's
  seat.

**Numbers.**
- MERGE, OwnerBox: 35 zones (14 merges, 21 diverges; style G 23, T 12).
  Host lane room short of k lanes over N..node: BEFORE (L4's pavement)
  1,389 m in 35 zones (p50 37, p90 70 m) -> AFTER, drawn, from N to the
  taper start: 0 m. NOSE: the ramp's end section on the host's vertices,
  worst 0.000 m (gate). 1,445 m of ramp no longer drawn between N and
  its node. Q2: all 12 style-T zones run on past the node (p50 57, max
  227 m of new pavement); 7 clamped (twin deck 3, lane change 3,
  junction 1).
- MERGE, city-wide: 876 zones (331 merges, 545 diverges). BEFORE 39,163 m
  short in 876 zones (p50 35, p90 93 m) -> AFTER 1,194 m in 24 zones (the
  squeeze against a neighbouring road). 460 style-T zones run on (p50 86,
  max 481 m); 79 clamped. Lane path: at N 0.00 m, at the node p50 0.00 /
  max 0.75 m. 62 style G whose OSM lane is shorter than AASHTO (left as
  OSM); 32 with the data's change on the far side.
- LINE MODEL: continuity 647 -> 664 city-wide (OwnerBox 8 -> 8, T1 113 ->
  124). TAPER 2,490 of 6,701 -> 2,469 of 6,293 (the eases that now only
  steer the lanes' centre are not counted).
- COVERAGE, OwnerBox T1: gore/road coplanar 1,553 -> 1,466 m2, branch over
  host 141 -> 140 m2, underlap under road pieces 1,821 -> 1,751 m2, holes
  in a ribbon 0.9 -> 1.2 m2.
- Boxed launch: LAUNCH 18 -> 18, UNLOAD 92 -> 91, routes 0/0/0, path
  misses identical. Solve 3,041 -> 3,624 ms (the zones are built in both
  ComputeTrims calls, ~0.5 s each).
- City audit (run 2 of 2): 17 failures against L4's 15. Same as L4: grade,
  TWIN a-f, PROFILE x2, lane survey, margin stations, culverts. Worse:
  verge steps 29 -> 79, a new FACE check 0 -> 4, rail census 6 -> 7 runs,
  unguarded ledges 1 -> 14, sign posts 0 -> 1 (a gantry's driver point
  off the road). All five came from the zone ends. They were fixed after
  run 2 and checked by targeted roadside probes (city-roadside-probe, 4
  runs; every new spot listed now ends in verge, rail or road). The full
  audit was not run a third time (lean budget).
- Race (watched, Uptown, 240 s, seed 0): the first run retired 2 rivals at
  the I-277 exit e4595's nose. The ramp's 2 cm end span had laid a deck
  rail ACROSS the lane; an A/B with PSX_CITY_ZONELANES=0 showed it was the
  geometry, not the lane path. Fixed (that span is skipped). The watched
  rerun had 0 retirements and 0 hits. Its one failure, auto-exposure
  pumping (gain 1.72), is in both watched runs and is not a road check.

**Known, left.**
- Paint is L6's: the dotted line between the aux lane and the through
  lanes, the channelizing gore lines and DW. The outer edge line already
  rides the aux lane.
- Not zones: 206 mouths over 35 degrees, 208 branches at fans, 51 on
  twin-deck unions (+ ramps that are union decks), 80 with no clean place
  for the nose, 135 ramp-to-ramp ends clipped to another ramp.
- 24 zones city-wide are squeezed short by a neighbouring road (1,194 m).
- Style G keeps OSM's lane length (62 shorter than AASHTO).
- linesim.mjs (linecheck's replica) does not model aux lanes.
- The full city audit was not run after the last fixes.

## L6 (roads pass, 2026-10-03): lane-use paint - merges, gores, mouths, turn lanes, arrows, ONLY, yields - R3

L5 gave every tier-1 merge one pavement, but no paint said what the added
lane was: the through lanes' edge line had moved out with the aux lane and
nothing was left between them, a gore was one white line and a yellow one
that met nowhere, and a host's edge line ran straight across every side
mouth. L6 (plan A8, owner Q5 (b)/(c)) paints what MUTCD paints there.

- **Every mark is a column of the ribbon (critic C5), `CityMeshes.Marks.cs`.**
  A span that carries a mark is drawn as columns (`EmitMarked`): the
  model's lines, the marks' own lines, the pavement between. A glyph
  (arrow, letter, shark tooth) is cut out of its lane's pavement column:
  the column is split at the glyph's vertices along the span and across at
  its pieces. Every vertex lies on the span's own surface, so nothing is
  lifted and nothing is laid twice. Paint comes from the profile texture's
  solid-white texels, in the road's own slot: **draws +0, no new texture,
  no decal, no lift**. A section is forced where each line mark starts and
  ends (`MarkForcedSamples`, before the zones' own), so a line changes
  style exactly there. `PSX_CITY_MARKS=0` draws L5's paint.
- **The aux lane (DW).** In every merge zone the wide dotted white line
  (0.20 m, 3 ft line / 9 ft gap, phase on the chain distance) runs at the
  through lanes' edge from the nose N to where the taper starts, on the
  host's side of the zone only. There is none through the taper (MUTCD
  3B.04). Style G's lane gets it from N to the node, and OSM's own lane past
  the node keeps its lane line.
- **The gore (CH).** From N to the physical nose P, both sides of the gore are
  0.20 m solid white channelizing lines that meet at N.
  - P is where the two pavements stand 4.5 m apart and the painted gore ends,
    found once per zone by walking the ramp from N.
  - The host's side is its own edge line on that side, drawn as CH. It is
    white even on a left exit, where it was yellow.
  - The ramp's side is its inner lane edge. While that edge lies over the
    host's shoulder, it is drawn on the HOST's ribbon. Once it is over the
    ramp's own pavement, it is the ramp's inner edge line, white, and no
    longer stood down by the clip.
  - The ramp-side line is never drawn inside the through lanes, and it eases
    onto the host's line over the first metres past N. The two roads'
    lane-edge offsets differ by a shoulder's rounding at a few gores.
  - The ramp's yellow left edge starts at P.
- **Exit-only drops (V8).** A style G diverge is one where OSM drops the lane
  at the exit node. There, the line between the exit-only lane and the
  through lanes is wide dotted for min(OSM's run, 805 m) back from the node.
  On a two-way host it is the k-th white line on the exit side.
- **Mouths (V5).** A branch clipped onto a mitred host at over 35 degrees
  stops the host's edge line across its mouth. The mouth is where the
  branch's two pavement edges cross the host's, plus 0.6 m each side.
- **Turn-only lanes (V7, LS).** A lane line between a turn-only lane and a
  through lane is solid white, on every marked road. The turn-only lanes come
  from turn:lanes (LSET), or from a TAPR left-turn bay where the tags say
  nothing (its leftmost lane).
- **Arrows and ONLY (Q5 b, tier 1).** Each turn-only lane gets a turn arrow
  (3.66 m) and the word ONLY (2.44 m letters), ending 3 m before its drawn end
  at a junction, so the driver reads the arrow first. The arrow points left
  for the left lanes and right for the right ones. Freeway mainlines are
  left out: an exit-only lane gets its group from the zone 30 m before the
  node, elongated x2 on a freeway-like road (MUTCD 3B.20). A group is laid
  only where its lane runs at full width: no taper, aux lane, relay or
  median-shift change.
- **Yield lines (Q5 c, tier 1).** A row of shark teeth (0.45 m bases, 0.6 m
  high, 0.15 m apart, points toward the driver) goes across a link's lanes
  0.5 m before it meets the street. It is laid where OSM tags give_way at
  that end. It is also laid where a one-way link enters a street with no
  signal or stop at, or within 40 m of, the node, at more than 35 degrees.
  A link clipped onto a mitred road ends where it meets that road's pavement.
- **The MARKS report (`Editor/CityAudit.Marks.cs`, inside PAINT).** It reads
  the marks the way the builder draws them (`OverrideAt`, `GoreLatAt`): DW
  over each zone's full-width run, a solid line at the through edge there,
  V6 (both gore lines white and their clear gap at the V's point at most
  0.10 m), V8, V5 (sampled where the branch's centre line crosses the host's
  edge), V7, the glyphs, and E1 (lift 0). It also runs in `LineModelOnly`.

**Numbers.** BEFORE is L5, where none of these marks existed. It is read off L5's drawing rules, not re-measured.

| MARKS check | OwnerBox before | OwnerBox after | T1 city-wide after |
|---|---|---|---|
| DW from N to the taper start | 0 of 1,323 m | 1,323 of 1,323 m | 63,076 of 63,076 m |
| Solid line at the through edge there | n/a (no line) | 0 m | 0 m |
| V6 gores white on both sides, closed at N | 0 of 35 (the ramp's side yellow, starting only where the pavements part) | 35 of 35 (worst 0.00 m) | 869 of 872 |
| V8 exit-only drops dotted | 0 of 16 | 16 of 16 | 228 of 229 |
| V5 host edge line across a mouth over 35 deg | 19 of 19 | 0 of 19 | 0 of 283 |
| V7 turn-only boundaries solid | 0 of 130 | 130 of 130 | 2,305 of 2,305 |

- **City-wide (all tiers):**
  - 786 aux lanes dotted (63,484 m);
  - 869 gores;
  - 228 exit-only drops (65,887 m);
  - 938 mouths stopped (113 skipped: an unmarked host, or no mouth on the host);
  - 6,557 turn-only lines solid on 5,718 edges (51 TAPR bays);
  - 929 arrow + ONLY groups (541 left, 388 right; 1,456 not laid: a taper, too short, or no junction);
  - 186 yield lines (6 on a tagged give_way).

  The marks build once per trims, in 345 ms (zones 315 ms), at the first tile's sections. It does not run in the elevation's ComputeTrims.
- **CITY AUDIT (OwnerBox):** 16 failures, against L5 run 2's 17. They are the known set: grade, TWIN a-f, PROFILE x2, lane survey, margin stations and culverts.
  - L5's after-run fixes are now confirmed by a full audit: verge steps 79 -> 26, FACE 4 -> 1, rail census 7 -> 6 runs, ledges 14 -> 1, sign posts 1 -> 0.
  - The one FACE left (e20511, East Independence Expwy) is already in L5's run 2. L6's marks on that edge start and end nowhere near it.
  - DRIVE AUDIT: zeros. linecheck --ratchet: PASS (231 probes).
  - Draws: worst view 211, unchanged.
  - Verts: trade_tryon 603k, i277_uptown 629k, plaza_midwood 451k. L2's were 583k / 577k / 452k, so L3-L6 together stay within +10 %.
  - The Unity smoothness gate (FAST) still throws as it did in L4 and L5 (not L6's).
- **Shots** (`shots\r3\`):
  - `marks_mint_top`: South Mint St from above. A left-turn lane with ↰ ONLY behind a solid white line, a right lane dotted off as it drops into the slip lane with ↱ ONLY, and the slip lane's gore lines meeting at N.
  - `zone_us74_e439`: US 74 at the exit e439, driver's eye. The exit's dotted line reads at range, but the elongated group is too far to read in this frame.
  - `merge_i85_e3031_far`: the I-85 merge from 400 m, 30 deg lens. The dotted lines at range show no speckle or pop-in.

**Known, left.**
- City-wide: 3 gores open (the worst is zone n10965, where the ramp's polyline is 4.1 m off the host line 6 m past N). 1 exit drop is not dotted (e4700: 1 lane line for k = 2).
- Arrows, ONLY and yields on tier 2 belong to R5 (A13). Crosswalks belong to L7 (A18).
- Not done:
  - the optional gore chevrons;
  - "turns DW again into a downstream drop" along OSM's own aux lanes;
  - lifting D1 CROSS out of report-only. CitySmooth is not in L6's test budget.

## L7 (roads pass, 2026-10-03): junctions - curb returns, one paved area per cluster, crosswalks - R4

Before L7 every junction was a fan trimmed just clear of its arms (0.6 m
past the crossing road's edge), so its corners were square and a right turn
swept over the corner lot. A divided road crossing a street was one fan per
carriageway, with the median crossing drawn as a ribbon between them, and a
left turn across the median ran over grass. In the OwnerBox that was 893 m2
of grass inside the tier-1 junction boxes. L7 covers plan A10, A11 (core)
and owner Q5 (a). The code is in `Scripts/City/CityMeshes.Junctions.cs`,
`FanCorners` / `BuildJunctions` in `CityMeshes.cs`, `CitySignals.cs` and
`CityMeshes.Marks.cs`.

- **Curb returns (A10).**
  - Every corner between two arms gets a circular arc tangent to both
    arms' edge lines. This applies when the arms are neither going
    straight through (170 deg or more) nor clipped against each other.
  - R comes from WP-19's table by class pair: local x local 7.5 m,
    arterial x local 10, arterial x arterial 12, ramp terminal 15.
  - Where two carriageways of one road part (same name, one way in and
    one out), the corner is a median nose of 1.2 m. There is no curb
    return where one road goes on through a split or a widening (same
    name, over 120 deg), nor between two ramps: there the corner is a
    gore's. The first audit had a 10 m arc at link-only node 597 that
    trimmed its ramps back 28-33 m and stood a fan chord over Tyvola
    Road's drop.
  - `ComputeTrims` grows each arm to the arc's tangent point:
    trim = s + R / tan(phi / 2) + 0.15, where s is where the two edge
    lines meet.
  - The tangent R / tan(phi / 2) is capped at 18 m. At an acute corner it
    runs away: a ramp terminal at 34 deg on Tyvola Road wanted 49 m and
    trimmed the road back 53-64 m. There the radius gives instead.
  - An edge keeps at least 40% of its length as ribbon (or what it had
    before). Two junctions close together share the room, and their arcs
    are capped by it: R_eff = tan(phi / 2) x room. Corners with less
    than 0.5 m of room stay square and are listed.
  - `FanCorners` draws the arc where the edge lines meet behind both
    mouths, on the node's side of the chord. It keeps 0.1 m of each edge
    line straight, uses a 6 cm sagitta and at most 10 pieces a corner.
    That is 11-15 deg a piece, the phone's budget for the verges each
    piece lays.
  - Each piece's verge meets the next one's on their shared bisector
    (`RingOut`), so there is no sliver and no wedge, and no corner fill
    at the arc's points.
- **One paved area per junction cluster (A11 core).**
  - Fan nodes join one cluster when an edge between them is swallowed by
    the two fans (a ribbon under 0.6 m), or when they are joined by a
    median crossing of 30 m or less. This is A1's `CityJunctionClusters`
    rule, now at run time.
  - A cluster is drawn as ONE ring round all its members' outside arms,
    sorted about the members' centroid, and drawn once, by the lowest
    member.
  - It is triangulated from the centroid at the members' mean height,
    and then each member node goes in as a Steiner point at its own
    height (split the triangle, Lawson flips to Delaunay; `RefineFan`).
    The centroid alone flattened clusters that rise across their median.
    The first audit found South Boulevard at node 466 0.6 m under the land
    graded to its high carriageway (fan mouth probe, 55 misses).
  - Where an inside edge is wider than the chords between its neighbours,
    the ring is carried out round that edge's sides (`RingTakeIn`).
    Briar Creek Road's 11.8 m piece with turn lanes needed this.
  - The ring runs straight across a median opening. Its curb returns
    follow the rule above.
  - The crossing edge draws no ribbon (trims half its length each,
    `Trims.internalEdge`). It also gets no stop bar or approach.
  - A group stays as separate fans and is listed when any of these hold:
    more than 8 nodes, over 80 m across, rising over 2.5 m, or a road of
    its own between two members.
  - Every reader of a fan goes through `FanCorners` / `FanCentre`:
    verges, rails, kerbs, `FanOnStructure`, `FanY`, lamps' reach and the
    audits' `FanPerimeter`. `FanPerimeter` returns a cluster's ring for
    its owner only.
  - `PSX_CITY_CLUSTERS=0` and `PSX_CITY_ARCS=0` measure without these.
- **Crosswalks (Q5 a).**
  - Every arm of a tier-1 signalised junction (`CitySignals`' signal
    clusters whose best arm is tier 1) gets a crosswalk. Excluded are
    freeways, ramps, tunnels, clipped arms and arms with no room.
  - The crosswalk is continental: 0.6 m bars 0.6 m apart, along the
    travel, curb to curb less 0.3 m. It is 3 m wide and starts 0.3 m
    out from the patch.
  - It is cut into the ribbon as an L6 glyph: draws +0, no lift.
  - Every layout line on that arm is OFF from the patch to the stop bar.
  - The stop bar moves to 1.2 m behind the crosswalk (MUTCD 3B.16),
    `CitySignals.CrossStopBackM`. The approach's `sStop` moves with it,
    so traffic stops behind the crosswalk.
  - Arrow + ONLY groups end 1.75 m behind that bar.
  - `PSX_CITY_CROSSWALKS=0` turns them off.
- **Every fan follows its arms' profiles.** Two points on each arm's line
  under the fan (0.35 and 0.7 of its trim, at the profile's height; for
  trims of 5 m or more) go in with the Steiner points. A cone from the
  node to mouths 10-20 m out cut under a crest's profile.
- **The lattice under the fans** (`PrepareFanFloor` / `FanFloor`).
  - The lattice is pinned 10 cm under the nearest road's PROFILE, but a fan
    is its own surface. Since the bigger fans, the lattice came within
    0.5-8 cm of the fan (UNDERLAP T1 lattice 332 -> 2,955 m2, Trade x Tryon
    269 m2: flicker at range) or over it (the turning movements' arcs met
    land first: 0 -> 160 T1 samples).
  - Before the ground is laid, every fan within 50 m of the tile is
    triangulated as the builder draws it. This is done once per trims and
    always with the tile's clip table empty, so the result never depends on
    build order.
  - A lattice corner at least 0.75 m inside a fan is held 14 cm under its
    surface, lowered by at most 0.12 m. The lattice is only ever lowered.
    Deeper dips next to a fan's edge showed up as ledges beside clipped arms
    in audit 3.
- **The COVERAGE cluster box.** A1's cluster box was the convex hull of
  the fans' chords, a stand-in "until A11". A cluster drawn as one is now
  judged against its own ring, fanned from its centroid. The hull would
  count the corner lots between each curb-return arc and the chord that
  used to cut it as holes: audit 1 found T1 893 -> 5,777 m2, all of it
  corner lots. A group left as separate fans keeps the hull.
- **The JUNCTIONS report** (`Editor/CityAudit.Junctions.cs`, at the end of
  COVERAGE; `CityAudit.JunctionsOnly` runs it alone in about 2 minutes)
  covers:
  - the trims, the clusters (the run-time set against A1's), and the curb
    returns per tier (full arc R_eff >= 0.9 R / room-capped / square,
    with the class pairs);
  - the split nodes (A4, measured only) and the crosswalks.

**BEFORE (L6 audit, L5 launch) -> AFTER (L7 audit 4, launch 2), OwnerBox**

| | before | after |
|---|---|---|
| grass in T1 / T2 / T3 junction (cluster) boxes, m2 (A1's hull -> the cluster's ring) | 893 / 712 / 109 | 26 / 20 / 0.1 |
| clusters with a hole over 2 m2, T1 | 24 | 2 |
| fan/fan coplanar in one cluster, T1 / T2, m2 | 119 / 94 | 0 / 0 |
| fan over its own arm / a cluster arm, T1, m2 | 53 / 23 | 34 / 7.5 |
| turning movements off the pavement (lane-correct), all tiers | 45 | 23 |
| of them T1 lone fans / T1 clusters | 1 / 11 | 4 / 4 |
| T2 lone fans / T2 clusters | 6 / 18 | 4 / 5 |
| T3 lone fans / T3 clusters | 3 / 6 | 6 / 0 |
| LAUNCH / UNLOAD (boxed), race routes | 18 / 91, 0 LAUNCH | 12 / 69, 0 LAUNCH |
| lattice 0.5-8 cm under a road (flicker), T1, m2 | 332 | 549 |
| tile build p95 (editor, 225 tiles) | 107.5 ms | 144.5 ms |
| worst view draws (Trade x Tryon) | 211 | 206 |

- CURB RETURNS, T1 fans in scope: 303 corners. 233 have a full arc
  (R_eff >= 0.9 R), 65 are room-capped (R_eff p50 6.9 m) and 5 are square
  (listed).
- CLUSTERS city-wide: 614 of 641 are drawn as one, 27 are left as fans
  (listed). In scope that is every one of A1's (T1 26, T2 33, T3 6). Their
  triangles cover their rings exactly, none is folded, and the surface
  stands at every member node's own height.
- CROSSWALKS: 1,931 on 445 tier-1 signalised junctions (16,469 bars). In
  scope there are 268 on 67 of 70.
- Arrow + ONLY groups: 929 -> 856, because the ribbons are shorter by the
  curb returns and the crosswalks.
- The CITY AUDIT still fails the same 16 checks as L6. DRIVE AUDIT zeros,
  the fan mouth probe 0, PAINT and MARKS all ok, linecheck PASS.
- Values that moved, all inside checks that were already failing:
  - verge 26 -> 28;
  - lane survey 4 -> 5 (e9373's rail in e5251's lane at (4799,1005));
  - ledges 1 -> 5 points (North Tryon e10902 at a 0.6 m median, 0.56 m;
    E 13th e10973 0.33 m; e13344 0.31 m);
  - TWIN b 134 -> 139 m, TWIN c 71 -> 78 (union ranges stop shorter of the
    bigger fans).
- The elevation solve is unchanged (the same 15,535 seats): no rebake.

**Not in L7 (lean), for L8 or later.**
- A4 split mitres: the split nodes are measured, not changed (city-wide
  226 of 961 are fans).
- The turning movements' cubic (trim to trim) still cuts the curb return
  at obtuse corners where a junction's common trim is long (Davidson x
  Trade, n4069).
- Per-arm trims (A3 FIX-3) would shorten those. A11's exact trims (each outside arm trimmed only to clear the
others' footprints) and its 1.2 m median-nose geometry at a cluster's
median opening are not done either: the ring runs straight across.

## L8 (roads pass, 2026-10-03): minor mouths, cul-de-sacs, dead ends, parking lots - R5

L8 covers plan A13 (through lines across minor mouths), A17 (bulbs and
clean dead ends) and a lean B9/B10 (parking lots), plus two L7 regressions:
the tile build time and the lattice shimmer under the fans. The code is in
`Scripts/City/CityMeshes.Minor.cs` (A13, A17), `Scripts/City/CityMeshes.Lots.cs`
(lots), `tools/city/lib/lots.mjs` and `tools/city/fetch/fetch_lots.mjs` (the
data), and `Editor/CityAudit.Minor.cs` (the MINOR report, after MARKS).

- **Through lines across minor mouths (A13).**
  - A lone fan qualifies when two non-link arms go straight through it
    (cos below -0.85), every other arm meets them at 60 deg or more and is
    of no higher class than the lower of the two, no arm is a roundabout's,
    the node is not a signal's (CitySignals), not an all-way stop, and no
    stop is tagged on either through arm. Clusters and fans on structure
    are left as they were.
  - A line is drawn where both through arms paint one of the same kind
    (edge, yellow, yellow broken, white broken) within 0.3 m of each other
    in the frame of travel. The edge line on a side a street opens on is
    broken there; the far one runs on.
  - The strip is the line's own column of the arm's texture
    (srcM +- half, the ribbon's own U), V carried on from the arm it leaves
    (the dash phase), straight from mouth to mouth or a cubic where the
    arms turn more than 5 deg. A broken line its texture has no broken
    column for is cut into its 10 ft dashes.
  - It is CUT INTO the fan (critic C5): every fan triangle loses the strip
    where the strip crosses it and the strip is drawn on that triangle's
    plane, in the arm's own slot. Lift 0, no second surface. A straight
    line is one quad (a curve one every 2 m), so a triangle splits into a
    handful of pieces.
  - `PSX_CITY_THROUGHPAINT=0` turns it off.
  - Arrows + ONLY in turn-only lanes and the yield lines at link ends now
    run on tier 2 as well (owner Q5 b/c, placed in A13 for T2).
- **Cul-de-sac bulbs (A17).**
  - The one Overpass fetch (`fetch_lots.mjs`, core box, 'out count' first:
    94 nodes, 6,775 ways, 113 relations) brings the turning circles.
    Section TURN: a turning_circle / turning_loop node that is a graph dead
    end, its radius OSM's diameter / 2 or 12.2 m (AASHTO 40 ft).
  - A bulb is a one-arm FAN: the street trimmed to sqrt(R^2 - hw^2), its two
    edge corners, then the circle anticlockwise (6 cm sagitta), on a plane
    that carries the street's end grade on (at most 5 %). Being a fan it
    gets the fans' verges round its rim, the lattice held under it and the
    audits' fan tests. A street too short for its bulb, a link's end and an
    end on structure keep their square end (listed).
  - `PSX_CITY_BULBS=0` turns bulbs and dead-end verges off.
- **Clean dead ends (A17).** Every other dead end on the ground lays the
  fan chords' verge (EmitVergeLine) across its end, so the land meets the
  tarmac there as it does along the sides. Type III barricades at the data's
  edge are not done.
- **Parking lots (B9/B10 lean).**
  - Data (`lib/lots.mjs`, sections LOTS and LENT): amenity=parking ways and
    multipolygons' closed outer rings, surface lots only (not multi-storey,
    underground, rooftop, street-side or lane). Each is rasterised at
    0.25 m, the road bands taken out (the OSM line, the ribbon's half width
    and 1.0 m, the verge's shoulder and toe), traced by marching squares and
    simplified (RDP 0.2 m). Under 40 m2, or under 30 % left: dropped.
  - Stalls: 90 deg, 2.74 x 5.49 m, double-loaded about a 7.32 m aisle, laid
    along the lot's longest side, kept only wholly inside the lot inset
    0.3 m and 0.6 m clear of every building; a row needs two. Stored as runs
    (first separator and count), not lines.
  - Entrances: one per ring on the nearest street of tertiary class or
    below within 25 m, 15 m or more from the edge's ends.
  - In the game the lots are cut INTO the lattice: every lattice triangle a
    ring crosses is cut along the ring's edges (only where each edge really
    crosses a piece) and each piece is lot or ground by its middle. The lot
    pieces go to the ROADS mesh in the junction slab's asphalt (road layer,
    road grip, the fans' slot), on the lattice's own plane: flush with the
    land and the verge's foot. The stall separators (0.10 m) are cut out of
    the lot again, run by run (parallel cuts, linear), in the solid white
    column of tw2's texture.
  - A restaurant's own lot (a prop lot) that covers a fifth of an OSM lot's
    box keeps it. Nothing is planted or stood in a lot (RoadsideOccupancy
    Other).
  - Entrances: the street's verge across the entrance (7.3 m, 4.3 m off a
    one-way, 1.5 m wings) is poured concrete (the Pavement slot) where it
    was grass.
  - `PSX_CITY_LOTS=0` turns the lots off.
- **Tile build time (L7's +34 %).** Profiled once (a temporary stopwatch
  per function under CityBudgetProbe, removed): `RefineFan` (L7's Lawson
  flips) was 801 ms of the Trade & Tryon ring's ~3,100 ms - 1,127
  refinements of the same fans, 0.7 ms each. It now keeps its result by its
  inputs, bit for bit (a miss is refined again): 801 -> 134 ms. The lattice
  corners have an array in front of their dictionary for the tile and 128 m
  round it.
- **The lattice under the fans (L7's shimmer).** A lattice corner held under
  a fan is held deeper the further in it lies: 14 cm at the 0.75 m inset as
  L7 left it, grading to 28 cm (lowered at most 30 cm) at 2 m in.
  A bulb benches the land instead: it holds every lattice corner within its radius and 12 m
  past it under its plane, however deep (12 m: every lattice triangle that
  reaches the disc has all three corners in it). The street's corridor is
  narrower than a bulb, and the first shot showed the land poking through
  the disc's rim in the corridor's steps.

**BEFORE (L7 audit 4) -> AFTER (L8 audit 1), OwnerBox unless city-wide**

| | before | after |
|---|---|---|
| junctions carrying through lines across the mouth, in scope (T1/T2/T3) | 0 | 86 (24/60/2), 256 lines, 6.4 km |
| the same city-wide | 0 | 882 (220/655/7), 2,470 lines, 65.0 km; 1,036 near edge lines broken |
| arrow + ONLY groups / yield lines, city-wide (T2 added) | 856 / 184 | 1,855 / 298 |
| turning circles drawn as bulbs | 0 of 74 | 73 of 74 (1 street too short) |
| plain dead ends with a verge across the end | 0 | 852 city-wide (76 in scope) |
| surface lots / stall lines / entrances (aprons), core box | 0 | 2,601 (4.25 km2) / 90,925 (84,820 stalls) / 1,646 |
| charlotte_city.bytes | 5.93 MB | 6.31 MB (+379 KB: TURN, LOTS, LENT) |
| lattice 0.5-8 cm under a road (shimmer), T1 / T2 / T3, m2 | 674 / 1,045 / 294 | 303 / 547 / 381 |
| tile build p95 (editor, 225 tiles) | 144.5 ms | 123.1 ms (L6 107.5; cap +15 % = 123.6) |
| Trade & Tryon ring: RefineFan / roads phase | 801 / 1,876 ms | 134 / 1,319 ms (2-site probe) |
| worst view draws (Trade x Tryon) | 206 | 210 |
| sliced build: longest step / p99 | 47.9 / 3.70 ms | 51.2 / 3.24 ms (75 tiles identical) |

- The A13 decisions city-wide (first reason wins): signal 383, all-way stop
  97, a stop on the through road 62, a side road of higher rank 188, no
  straight-through pair or a side road under 60 deg 367, no line on both
  sides 1,438 (unmarked local streets, owner Q1), a cluster 1,485, on
  structure 13, roundabout 10.
- T3 lattice shimmer is up 294 -> 381: the bulbs' rims (each a few tens of
  m2) before the 12 m bench; the bench came after the audit.
- The CITY AUDIT fails the same 16 checks as L7, every value the same or
  better (verge 28 -> 27). DRIVE AUDIT zeros, the fan mouth probe 0, PAINT
  T1 V1-V4 0, MARKS all ok, COVERAGE coplanar and holes unchanged.
- linecheck: the baseline was re-recorded on main (its inputs moved:
  citydata.mjs); every check BEFORE = AFTER.
- `export --check` passes; the fetch is recorded in cache_manifest.json
  (today's OSM: the attic query at the cache's moment ran Overpass out of
  memory).

**Not in L8 (lean), for later.**
- A13's T2 bays (already solid since L6), the T2 host edge-line breaks at
  mitred mouths (L6's are T1), the 0.05 m minor-mouth continuity audit (the
  MINOR census counts what is drawn), and junctions inside clusters.
- A17's reverse curves (R 7.6 m) between a street's edge and its bulb, Type
  III barricades at the data's edge, and B9's welds/overshoots.
- B9/B10: aisles from OSM's parking_aisle ways (fetched, not used), a lot's
  holes (islands, buildings inside it are paved under), apron wings sealed
  to the lattice (the apron is the verge's own geometry re-poured), the
  lots' own audit raster (LOT audit), and lots in CityElevation's ground.

## HEIGHTS (leftover item 1, 2026-10-03): road heights from lidar - built, measured, left OFF

The owner: "a lot of roads dip down and go up under bridges, but they do so at
angles, not smooth transitions like DOT requires." The cause (diagnosis V2/V4,
plan B7): every road read its height off the smoothed land (60 m grid, 48 m
Gaussian), which cannot see a freeway's cut, so every street bridge over a
freeway was made by digging a 4.5% V under it (SinkTrenches).

**What was built (all OFF in the shipped game):**
- `tools/city/lib/roadprofile.mjs`: the tier 1+2 road profiles (20,809 edges,
  363,765 stations) sampled from 3DEP 1/3" at the solver's own stations
  (median of 5 across the road); bridges, tunnels, water spans, 15 m of every
  bridge approach (on to 100 m while the ground falls steeper than the class)
  and anything 5% steeper than its class masked and drawn across; carriageway
  pairs pulled within 0.3 m; node heights from the ground within 40 m, then
  kept within 1.6x the class grade of each other; chains (straightest pairs,
  highest class first, a pinned "ghost" of the road on through a fixed
  junction) smoothed, grade-capped and limited to AASHTO curvature (crest =
  stopping sight, sag = headlight). Section RPRF, +527 KB. Written only with
  `node tools/city/export_osm.mjs --rprf`.
- Runtime (`CityElevation`, `PSX_CITY_RPRF=1` with an RPRF file): measured
  roads start from their profile; no trench under a measured road; humps and
  cones on a measured road are ADDITIVE (the lift fades on top of the real
  grade, capped by the plain envelope, crest radius 1.6x the launch radius);
  the curves pass may lower but not lift a measured station round a crest;
  measured stubs keep their real rise; a ramp seat's offset eases out on a
  cosine; unmeasured junctions within 150 m take a measured junction's offset;
  a station the lidar saw on the ground is never a deck by the 3.5 m margin.
- Instruments: `CityAudit.HeightsOnly` (solve + PROFILE report + a station
  dump, ~2 min, no tiles), `tools/city/heights_check.mjs` (HEIGHT: game minus
  3DEP on grounded stations), the PROFILE report's DIPS UNDER BRIDGES line,
  `PSX_CITY_HTRACE` (named stations after every solve step), CityPreview
  group `heights` (three driver-eye spots).

**BEFORE (main) -> AFTER (RPRF on)**
- HEIGHT vs 3DEP, T1: p50 0.83 / p95 3.51 m -> 0.02 / 0.41 m (T2 0.64 / 2.28 -> 0.03 / 0.35). PASS.
- Dips under bridges (a sag short of comfort K within 30 m on the road under), T1: 390 of 663 (313 at dug trenches) -> 81 (0 dug). Trenches dug 327 -> 0; measured cuts 228.
- PROFILE T1 sags short of comfort K 5,600 -> 2,346; corners 1,171 -> 197. Boxed COMPRESSION trench T1 85 -> 0.
- DOUBLE 2 -> 0; PAIRS T1 13 -> 4 (worst 1.05 -> 0.65 m); lowest clearance 4.75 -> 4.89 m; 3.5 m margin stations 2,814 -> 1,769; W 5th separation 5.70 -> 6.68 m, carriageways 0.34 -> 0.27 m.
- Solve 3.3-3.6 s -> 3.0-4.0 s.

**Why OFF (FAILED):** LAUNCH. Boxed 12 -> 15 (T1 3 -> 4, uptown route 0 -> 1: the I-277 e2308 hump), city-wide 115 -> 206 (UNLOAD 590 -> 854): junction fans 21 -> 35 and mitred joints 11 -> 33, mostly measured T2 streets meeting junctions at 3-6% breaks (real crowns the smoothed land never had), half of them @mesh-off-data. The one AuditOnly (an earlier build) also had grades past 16% 3 -> 5 (Armory Dr, Morehead Ridge Dr; the junction ease added after it targets them, not re-audited), cut walls 214 -> 1,035 m on the roadside tiles (a new FAIL: a continuous real cut with a street along its top needs a retaining wall), faces 1 -> 3, rail census 6 -> 7 runs, lane survey 5 -> 6, TWIN e max 0.29 -> 1.0 m. PAIRS, DOUBLE, margin, TWIN a-d, ledges and verge got better.

**Next:** fan planes and mitred joints that follow measured arms (or a junction landing in the offline limiter using the runtime's own through pairs); the cut-wall gate needs the owner's word on real retaining walls; then one city-wide launch.

Logs and photos: scratchpad `heights\` (probes 1-12, checks, checks_r2, checks_r3) and `ba\heights\`.

### HEIGHTS finish attempt (2026-10-03): junction landings, C1 blends, walls where a slope has no room - still OFF

The junction regression had one cause in the curves pass: a measured station
on its profile could only fall (the anti-ratchet freeze), so wherever a
measured road reached a junction the node could not be held on its major
road's plane (the plane needs every arm free), no arm landed on it, and no
street branch was pinned onto its host (the branch's ribbon ran under the
host's: the `@mesh-off-data` half). Built, all of it only with the measured
heights (`RprfOn`; OFF solves and draws exactly as shipped):

- **Measured roads free at junctions**: within 60 m of a node of three arms
  or more, and on every street-branch station (`LandingFreeM`).
- **Junction landings (C1)**: every arm but the major through pair runs ON
  the plane across the major road's pavement and out to its fan corner
  (`d0` = half width over the sine of the meeting angle + 1 m, at least the
  arm's trim), then eases from the plane's height and grade into its own
  profile on a cubic Hermite as long as AASHTO asks at the arm's design speed
  (25 mph on a STOP/signal approach and on a minor arm into a bigger road):
  crest K for stopping sight, sag K for headlight sight, else the comfort K,
  else the gentlest that fits (then only the plane is held). Never a ramp
  past 15% unless the plane or the arm is. `CrestStopR`, `SagHeadlightR`.
- **C1 end blends**: `BlendEndsToNodes` fades on a smoothstep (one over the
  whole edge up to 180 m) instead of a linear ramp over each half; unmeasured
  profiles re-clamped to their class grade where their ends allow.
- **Measured decks free in the first two curves rounds** (the fresh raise
  puts back any clearance they give): a twin hold had lifted I-277 e2308 over
  N Brevard St into a 4.8% one-station crest on the uptown route.
- **Grade guard**: last, an edge of 30 m+ with a station grade past 15.5% is
  eased to it where its ends allow (not bridges, crossings, water, culverts).
- **Walls where a slope has no room** (`CityMeshes.InCut`, the owner's DOT
  rule): a road above in the slope walls the cut only where its 1V:4H fill
  still stands over this road's back slope where the lattice cap stops
  (`CityLatticeReachM`), i.e. where `Ground` would leave a drop; a deck above
  never walls it (decks hold no land). The audit lists the walls by road.
- Instruments: the launch report's GRADES line (the audit's 16% check on the
  same solve), the vertical-curves line's `junction landings`, CityPreview
  group `jgrade` (S Mint St at W 4th St, West Blvd node 5192).

**Shipped (OFF) / heights ON before / heights ON after:**
- LAUNCH city-wide 115 / 206 / **178** (T1 42/67/69, T2 65/125/99, T3 8/14/10; UNLOAD 590/854/707); uptown route 0/2/1 (e2422 at node 3640, a raised-cone mitred joint outside the box). Boxed 12 / 15 / **11**, routes 0. By cause, ON before -> after: mitred joints off the data 48 -> 22, mitred joints 34 -> 27, terrain profile off the data 43 -> 31, junction fans 36 -> 34, **junction nodes 2 -> 16** (nodes are now held on their plane, so a crest AT the node can no longer be lowered away).
- Grades past 16%: 3 / 5 / **0** (+8 slivers, worst 15.5%; the guard eased 9 edges).
- Cut walls on the roadside tiles: 214 / 1,035 / **597 m** (I-277 216 m north of uptown at 35.2294,-80.8311; I-85 209 m at 35.2479,-80.8960; East Independence Expressway 90 m and Blvd 30 m near Elizabeth; I-77 43 m; Albemarle Rd 10 m; all but 15 m "a road above"; which of them are really walled is still to check against the real roads); the 50%-below-WP-04 check still FAILS (597 vs 535 m).
- DOUBLE 2 / 0 / 0; PAIRS T1 13 / 4 / 4 (worst 0.65 m); lowest clearance 4.75 / 4.89 / 4.89 m; DRIVE AUDIT zeros; tile build p95 112.5 / 118.9 ms (cap 123.6); solve 3.3-3.8 s; audit failures 16 / 16 / 14 (faces 3 -> 1, rail runs 7 -> 6, lane survey 6 -> 5, ledges 4 -> 1, culverts 2 -> 1; verge 26 -> 30).

**Why still OFF:** LAUNCH city-wide 178 against the gate of 115, spread over
junction fans (34 vs 22 shipped), mitred joints (27 + 22 off the data vs
11 + 9), junction nodes (16 + 9 vs 3 + 6) and terrain profiles off the data
(31 vs 19) - no one rule closes 63 spots, and the budget was one more
city-wide run.

**Next:** ease the major through pair through the node on its own vertical
curve first (so the held node sits ON a curve, not at a crest), then land the
minor arms on that; fan corners of the major arms are still on their own
grade (the fan folds where an arm climbs into the junction); the
terrain-profile `@mesh-off-data` spots along edges (a neighbouring ribbon
over the path); the remaining walls checked against the real roads.
Logs: scratchpad `jgrade\` (box1-5, all1, audit1); BEFORE shots of the two
junctions in `ba\jgrade\before\` (no AFTER: nothing shipped changed).

### HEIGHTS: freeways only (2026-10-03, the owner's decision) - one short attempt, still OFF

The owner: the lidar heights for the motorway and trunk MAINLINES only (where
the dips under bridges are); ramps and every street keep the shipped solver,
ramps seated onto the freeway as before and climbing off on a smooth curve.
Built (all with `PSX_CITY_RPRF=1` and an `--rprf` export only):

- `export_osm.mjs --rprf` designs the whole tier 1+2 network as before and
  writes RPRF for the 1,860 motorway/trunk mainline edges only (78 KB).
- The rules of the finish attempt above (C1 end blends, junction landings,
  the grade guard) run only on roads that meet a measured node or one eased
  toward it (`Touched`, `MeasuredNode`); everything else solves as shipped.
- The measured offset eases out along RAMPS only (`EaseUnmeasuredNodes`),
  never moving a node a street reaches.
- An unmeasured ramp at a measured freeway climbs off its seat on the same
  cosine ease a measured ramp uses, on the profile step 1 gave it
  (`rampBase`): the 8% line from a seat deep in the real cut met a cone in a
  38% step (e521 by I-277).

**Boxed LAUNCH (gate 12; shipped 12):** first try 18 (grades past 16% 11,
all ramps climbing out of the real cuts, up to 38%); with the ramp-only ease
and the ramp climb-out 15, uptown route 1 (I-277 e2308's clearance hump over
N Brevard St, a crest of 4.3% in one station at 150 km/h that the curves pass
leaves beside frozen measured stations), grades past 16% 4 (gate 3: ramps
e4675 28%, e1245 16.3%, link e12408 16.2%, and Bethel Road 17.5%), and a new
1.05 m ramp deck-end launch at node 708 (e13779, the I-277/I-77 ramps). Most
of the other boxed spots are the shipped streets' (S Mint St at W 4th St
0.42 m as shipped). Stopped there (budget: 3 boxed, 1 city-wide, 1 AuditOnly,
1 preview; used 2 boxed, nothing else).

**Next, if freeways-only is tried again:** the ramps are the problem - a ramp
on the smoothed land meeting a freeway in its real cut has metres to climb
in a few stations where it also crosses streets (I-277/I-77 uptown). Either
the ramps whose seats lie on a measured mainline take their own measured
profile too (an owner call: it widens the scope), or each such ramp gets one
designed vertical profile from its seat to its terminal (grade-capped, K-sized)
before the cones; and I-277 e2308's hump needs its crest rounded by lifting its
measured neighbours within the twin pair.

## Hotfix (2026-10-03): no square concrete in the race's way, no stray blocks

The owner, racing the Uptown Loop at night: "90 degree concrete formations
on 277 ... they ended my race" (frames at 0:15, 0:45 and 1:02). Then:
stray concrete median blocks "between roads where they shouldn't exist" (a
5 m Jersey on the grass between a merge ramp and its freeway). The three
I-277 spots, on the route's first two kilometres:

- **(a) 0:15, e2311 (~650 m), right.** The gore nose of the exit deck
  e2382: `EmitNose` closes the open V between two diverging decks with a
  rail block square across the nose, its face to the painted gore's traffic.
- **(b) 0:45, e6403 (~1,130 m), left.** The twin decks' union median (A2)
  ended at the structure: its Jersey capped square, then each carriageway's
  own median Jersey started capped round a 2.6 m grass median at their top -
  a 1 m concrete box with grass on top. A2's Jersey approach covered only
  the 20 m approach band (the rest was A12's).
- **(c) 1:02, e2144 (~1,920 m), left.** The left exit's gore: the median
  Jersey started square beside the yellow line, the land behind at its top.

**The rules (general).**

- **A sloped end** (CityMeshes.cs, `TaperLenM` 8 m, `TaperFootM` 0.12 m): a
  median Jersey's run end is turned down from 0.81 m to a curb over 8 m
  (1:10, the sloped concrete end) instead of capped square. Per-section
  heights (`taperL/taperR`) are decided with the flares in the run-end loop;
  a hand-over to another barrier kind is left as it was. Rails stay full
  height: a rail is there for a drop.
- **The gore nose keeps its block and gets a sloped V in front** (`EmitNose`):
  two rails from an apex on the painted gore's middle line to the nose's
  ends, rising from a curb at the apex to full height at the block, their
  traffic faces ending on the two pavements' edges. The apex stands 3x the
  nose's half width back (1:3 legs), no further than the gore leaves 1.2 m
  for it, never closer than 2.5x. Each leg only where it stands in no lane;
  a leg whose low end was in a lane starts at curb height where it leaves
  it (`EmitRailOffLanes` heights).
- **Union medians** (`CityMeshes.Unions.cs`):
  - Only opposing traffic stands a median (`MedianOfUnion`): a ramp beside
    its mainline or another ramp, or two roads the same way, share the deck
    with a flush strip and NO median (they merge and diverge across a gore).
  - Approach medians only between the twin carriageways of one road (pair
    kind Dual; every sample: no ramp, the same name, running the other way).
    A Jersey approach now carries on for as long as the twins stay beside
    each other in the band (gap within 6.6 m, one height within
    `SharedGuardDyM`, short of a fan), over structure the solve made but
    never onto an OSM bridge kept apart, up to 2.5 km
    (`UnionApproachBarrierM`). A walk stopped by the next run meets it
    exactly (one line, no ends turned down at each other).
  - Closed ends: on the ground the Jersey is turned down over 8 m (a
    curb-high cap); on a deck the median splits into a V over max(8 m, 1.5x
    the gap) - two legs as wide as a parapet, meeting each carriageway's own
    parapet on its edge - instead of a rail square across the slab's end.
  - A standing median (Jersey, curbs, legs) only where the road across is
    drawn beyond this one's edge; the flush strip as before.
- **No isolated piece under 30 m** (`MinMedianRunM`): a median Jersey or a
  cut wall run ending inside its edge at both ends is dropped in the
  side-flag pass (from the spans' data, so every tile drops the same piece);
  a union median that short, closed at both ends, is a flush strip
  (`UnionDrawsSolid`). PSX_CITY_KEEP_SHORT=1 keeps them (the before-count).
- **The audits.**
  - `CityAudit.RunDrive` (`tools\city-cycle.ps1 -DriveOnly`,
    `city_drive.txt`, PSX_DRIVE_ROUTES): the DRIVE AUDIT on every tile the
    three race routes cross (111 tiles), checked on the routes' 184 edges,
    other roads reported; then the union runs along each route and the
    short barrier census.
  - **BLUNT** (every drive audit): a ray at bumper height (0.45 m) along the
    travel from points across the lanes, and out past either edge over
    pavement that carries on flush and is reached without crossing a
    barrier, must meet no face within 60 deg of square to it.
  - **The short barrier census** (`CityAudit.Barriers.cs`, in the city audit
    and RunDrive): every tile a barriered road crosses (1,540) built with
    the span record on; isolated Jersey-height pieces under 30 m counted
    city-wide, the ten shortest listed. A check: 0.
  - TWIN (b)/(c) count a closed deck end's V legs as its designed median
    (`InUnionEndV`).
- **Named views** (`CityPreview`): `i277_a_gore`, `i277_b_union_end`,
  `i277_c_median` (group `i277`), `short_i485_e14173` (group `short`).

**BEFORE -> AFTER**

| | BEFORE (main e2a80f1c) | AFTER |
|---|---|---|
| DRIVE AUDIT, the three race routes' 184 edges (111 tiles): walls / steps / holes / off / grass | 0 / 0 / 0 / 0 / 0 | 0 / 0 / 0 / 0 / 0 |
| BLUNT on those edges (uptown 27, independence 2) | 29 | 0 |
| BLUNT on the other roads of those tiles (reported) | 75 | 12 |
| DRIVE AUDIT, the nine default tiles, BLUNT | (no check) | 0, every other probe 0 |
| short barrier census, city-wide (1,540 tiles) | 29 pieces: 25 median Jerseys / cut walls (558 m), 4 union medians | 0 |
| union approach runs | 232, 4,165 m | 246, 14,394 m |
| `BuildDeckUnions` (editor) | 76 ms | 165-187 ms |
| city audit (OwnerBox) failures | 16 (L8) | 16, the same set |
| TWIN (b) open slot / (c) walls | 139.0 m / 78 | 138.0 m / 75 |
| rail census | 6 runs, 6 m | 5 runs, 5 m |
| unguarded ledges | 5 | 4 |
| lane survey, runs not named (already failing) | 5 | 8 (V legs' faces beside three ramps' lane lines) |
| tile build p95 (9 budget sites) | 123.1 ms | 112.4 ms |

- The ten shortest pieces BEFORE: a 6.0 m union Raised median on Tuckaseegee
  Road (e8945), a 7.6 m median Jersey on Independence Boulevard (e16243), a
  7.8 m cut wall on I-485 at its ramp (e14173, the BEFORE/AFTER pair
  `short_i485_e14173`), a 9.2 m union Raised median on University City
  Boulevard (e10094), an 11.4 m union Jersey between the ramps e11144/e11147
  at East 12th Street, then 17.8-19.0 m cut walls on I-485 (e2606, e14168),
  I-85 (e9930, e11457) and I-77 (e8470). A piece another barrier carries on
  from (a median Jersey between two rails) is one barrier changing kind, not
  a block, and is neither counted nor dropped.
- **Not done (lean):** the lane survey's three new runs are V legs' traffic
  faces at three ramps' edges (e14103, e327, e2735; no probe of the drive
  audit or BLUNT meets them); a nose whose gore never opens 0.6 m keeps its
  block alone. Rails are not tapered (a rail guards a drop).
- **The race:** `tools\race-play-check.ps1 -Venue UptownLoop -Hour night
  -Finish` (rebaked, -NoWatch: the watched editor stopped on the window
  layout prompt): all four cars home in 259 s, 0 hard hits, 0 retirements,
  0 traffic wrecks, no hits by kind.
- **No rebake** for the geometry (tiles build at runtime); no height moved.

## Parapet and rail ends (leftover item 2, 2026-10-03): W-beam lead-ins, sloped ends, closed gaps

The owner's W 5th Street frame (westbound from the east signal): the bridge
parapet starts on its approach as a blunt concrete block, its end a flat
grey face square to the traffic. The hotfix sloped median Jersey ends and
gave gore noses a V, but left every rail full height with a square cap. DOT
practice: the approach guardrail (W-beam on posts) connects to the parapet
and begins with an end terminal flared away from the traffic; where there is
no room, the concrete end is sloped down.

**The rule** (`CityMeshes.DecideSideFlagsSteps`, the rail-end pass before the
caps; every rail run that ends ON THE GROUND into nothing). The open roadside
past the end is walked on this edge's own spans (decided, no barrier, gap,
union, deck, wedge, approach, squeeze, retaining face or collapsed section),
at most `RailEndWalkM` (two lead-ins and a metre):

- **W-beam lead-in** where it holds `WBeamLenM` (19.4 m) and the beam's
  corridor (0.4-1.7 m past the edge) keeps `WBeamRoadPadM` (1.3 m) off every
  other road's pavement (`WBeamCorridorClear`, from the map's centrelines and
  widths, so every tile decides the same): the concrete carries on
  `TaperLenM` (8 m), sloped down to a curb, and a W-beam (top 0.79 m, beam
  0.31 m, a corrugated face) on 6x8 in wood posts every 1.905 m with
  blockouts runs from the parapet's full height along that slope (the
  transition), then its 11.43 m terminal flares 1.22 m away from the road
  and is turned down into the ground over its last 2.5 m (`EmitWBeam`).
- **Sloped past the end** where the room holds half of `TaperLenM`: the
  concrete carries on (to 8 m, never past its share of the open roadside)
  and slopes down to a curb over that length. The full-height rail keeps the
  length it had, so the drop it guards stays guarded.
- **Sloped inside the run** where it does not (a rail running to its node,
  or into a gore on the ground): the run's own last 8 m slope down - except
  a rail standing over a drop to its very end (a retaining face, a deck),
  which keeps its full height where it stands (the rail census guards a
  drop to its end) and is sloped past its end over what open roadside there
  is, if 1.5 m or more (`ShortSlopeM`: steeper, but no square face).
- **Gap closed** where another barrier stands on within two lead-ins'
  length (two bridges close together: N Graham Street between its two
  bridges): the rail runs on at full height to it (the Roadside Design
  Guide closes short gaps between barrier runs) - no ends at all. Past that,
  a stretch shared with another barrier is split, so two facing lead-ins
  never meet.
- Left as they were: ends on a deck (the gore nose's V stands there), a
  hand-over to another barrier kind, a hand-over to a union's median (its
  owner turns its own ends).

`FlagReachM` 40 -> 60 m: a plan reads up to `RailEndWalkM` past an end and
a W-beam reaches 19.4 m, and every tile that draws a piece of either must
see the same open roadside. Street lamps keep off a lead-in
(`LampSideClear`). The rail's end caps wear the concrete as its faces do
(they were one texel: the flat grey).

**Drawn with the furniture, solid on its own.** The W-beam is pack metal
and its posts pack wood from the furniture atlas (`CityPoles.EmitWBeam`,
`EmitWBeamPost`, in `CityPoles.Build`): the tile's one furniture draw with
its poles and lamps, no new material, no new draw where a tile already has
furniture. Its collider is collider-only (`TileMeshes.guardrails`, a box
from the beam's face back past its posts, "Guardrail" on the Solid layer,
`CityWorld.Attach`). The city preview draws the lead-ins alone
(`CityPoles.WBeamMesh`; the preview stands no poles or lamps).

**The probe: BLUNT rail ends** (`CityAudit.DriveAudit`, `RailEnds`). BLUNT's
lane rays never reached a rail (its face stands 0.3 m inside the edge).
Along each drawn edge line - 0.15 m inside it, 0.15, 0.75 and 1.2 m outside
(the verge a car runs off onto) - a ray at bumper height (0.45 m) along the
travel, both ways, must meet no RAIL's end face within 60 deg of square
(a recorded rail piece's end no other piece carries on from, or the
Guardrail collider). Columns a barrier already stands in are skipped. An end
within 50 m of a structure end, on the ground, is on a bridge approach: the
check. `RunDrive` (`tools\city-cycle.ps1 -DriveOnly`) runs it on the race
routes, the nine default tiles and every tile of the OwnerBox
(PSX_DRIVE_RAILBOX=0 skips the box; PSX_DRIVE_BOX moves it), and prints
what the tiles decided (W-beam / sloped / inside / closed / kept).
`CityPreview` group `parapets` (with `w5th_wb`): `parapet_i277_e10558`,
`parapet_e12th_e2325`, `parapet_graham_e1941`.

**BEFORE -> AFTER**

| | BEFORE (main c073a472) | AFTER |
|---|---|---|
| BLUNT rail ends on bridge approaches, the race routes' 184 edges | 36 | **0** (2 trailing ends of one-way I-277, reported) |
| the same, the nine default tiles | 27 | **0** |
| the same, every tile of the OwnerBox (182) | 202 | **9** (16 with trailing ends of one-way roads) |
| rail ends met square anywhere, routes / nine tiles / box | 60 / 42 / 347 | 13 (+12 trailing) / 10 (+5) / 80 (+49) |
| treatments in the box (240 tiles built) | - | W-beam 146, sloped past the end 46 (1 short), sloped inside 83, gaps closed 14, kept square over a drop 19 |
| W-beam in the box | - | 1,892 pieces, 1,022 posts on 51 tiles |
| DRIVE AUDIT, routes (111 tiles) and nine tiles: walls / steps / holes / off / grass / BLUNT | 0 each | 0 each |
| city audit (OwnerBox) failures | 16 | 17: the same 16 + the new RAIL ENDS check (1, e1252, before the short-slope rule; 0 with it) |
| rail census | 5 runs, 5 m | 5 runs, 5 m (first try 14 runs, 17 m: fixed) |
| unguarded ledges / verge steps / body-box face / lane survey | 4 / 27 / 1 / 8 | 4 / 27 / 1 / 8 |
| short barrier census, city-wide | 0 | 0 |
| tile build p95 (9 budget sites) | 112.4 ms | 120.4 ms (+7%) |
| worst view draws (trade_tryon) / culverts and banks | 210 / +6 | 210 / +6 |

- The BLUNT rail-end rays run both ways only on a two-way road, as BLUNT's
  do: a one-way road's trailing end meets no traffic (DOT asks no terminal
  there); those are counted and reported, not checked. The BEFORE counts
  ran both ways on every road.
- **Not done (lean):** the box's last 9 faces: rails standing on a
  retaining face or a deck right to their end with under 1.5 m of open
  roadside past it (19 kept square in the box; South Boulevard e9969/e3603),
  and the hand-overs to a union's raised median (North Kings Drive e9676).
  A lead-in never crosses a node: where the open roadside runs on into the
  next OSM way (W 5th's way split 3 m before its approach rail) the end
  slopes inside the run instead of getting a W-beam. 12 of the box's 51
  W-beam tiles have no street lamp of their own: a furniture draw there
  unless a utility pole stands on the tile (the budget sites' draws did
  not move). The pack metal reads dark grey (the lamp posts' tint).
- No rebake (tiles build at runtime); no height moved.

## Junction boxes (leftover item 3, 2026-10-03): the main road's surface, ramps crossing at a skew, Little Rock Road under I-85

Two owner faults. A paved junction was drawn in another asphalt than the
roads round it (Trade and Tryon, top view). And Little Rock Road under I-85
was "a mess": lines at odd angles, white lines across the road, a grass
patch in the paved area, road pieces lying over each other. Top views
cannot see under a deck, so the junction was read from the driver's seat
(`CityPreview` group `jbox`; `jboxdbg` holds internal views from 3.2 m over
the road, under the deck) and from a plan of the built meshes
(`CityAudit.DumpRoads`, `PSX_DUMP_BOX`, internal; the coverage run dumps
too when it is set). The code is in `CityMeshes.Junctions.cs`
(`FanMainArms`, `FanSurface`, `CurbRadiusAt`), `CityMeshes.Minor.cs`
(`CrossingLines`, `ThroughPair`) and `ComputeTrims` / `ShareSurfaceAges` /
`PierBlocked` in `CityMeshes.cs`.

- **A junction takes its main road's surface** (`FanSurface`).
  - Before, a junction was aged from its own node (the 40% position hash).
    In the OwnerBox 107 of 292 T1/T2 junctions were in another age than
    their main road: a grey patch between black roads, or black between
    grey (West Arrowood Road at I-77 was one).
  - The main arm is the through pair of the highest class and widest arms
    (a road going through beats one that ends there; a street beats a ramp;
    a pair under one name beats two names; then speed), or the best single
    arm where nothing goes through. A cluster reads all its members'
    outside arms.
  - Material too: concrete where the main arm is on structure at its trim.
    No new slot: the junction slab's four surfaces were in the kit already.
  - The main road's two arms share one age through the junction, and so do
    the two arms of a road crossing it under one name (North and South
    Tryon are one street: `RoadKey` drops the compass word). Side arms meet
    the junction at their mouths, the straight cut across the arm at its
    trim (0.3 m inside a crosswalk).
  - The joins are bounded: a group of roads grows to 2 km at most
    (`FanJoinMaxM`), main pairs first. Unbounded, they ran one age over
    most of the city and new asphalt fell from 38% of the road length to
    11% (this item's first audit). Bounded it is 38.3% (38.1% before).
  - Trade and Tryon did not change on main: Tryon is its main road, already
    new asphalt like the junction; West Trade is old asphalt and meets it
    at the crosswalk.
  - `PSX_CITY_FANSURF=0` ages junctions from their node again.
- **A ramp crossing a carriageway at a skew is a junction** (`ComputeTrims`).
  - Arms within 60 degrees of each other are clipped, not trimmed. Where a
    ramp or turning roadway crossed a carriageway under 60 degrees, each of
    its halves was clipped against the carriageway: a merge on one side, a
    diverge on the other. Its edge lines ran on across the carriageway's
    lanes, and wedges of one ribbon lay over the other.
  - Now, where one link is clipped beside the arm coming in and another
    beside the arm going out, on the two sides of the through road, going on
    through each other (within 45 degrees of straight), the node is a
    junction: no arm clipped, every arm trimmed clear of the others. Acute
    pairs are trimmed by their true overlap down to 15 degrees
    (`CrossingMinSin`; 30 degrees elsewhere).
  - A crossing's corners under 60 degrees are a gore's point, not a curb
    return (`CurbRadiusAt`): a 15 m return at 36 degrees trimmed Little
    Rock Road's ramps back 32 m.
  - Not on a bridge: Tyvola Road's links cross it on its deck over I-77,
    and there the fan's chords are deck edges (the first audit found
    parapet faces at three lane mouths). Those keep the clip.
  - Not street over street: the first audit drew every skewed crossing this
    way (102 nodes). At Dalton Avenue x North Graham a cluster's grass hole
    doubled (20.9 -> 41.7 m2), and at East 12th x North Caldwell a rail end
    stood in a lane mouth (the fan mouth probe). Those, and the other
    street skews (Gold Hill Road over Highway 21, Beatties Ford Road over
    West 5th), keep the clip for now. About two dozen ramp crossings
    city-wide qualify. `PSX_CITY_CROSSINGS=0` draws none.
- **The main road keeps its lines across a crossing** (`CrossingLines`).
  - A ramp crossing at 19-21 degrees lies over the carriageway for about
    30 m, and all of it is the junction's. A bare slab there read as an
    unpainted plaza (the first AFTER photo of Little Rock Road).
  - The main road's straight-through pairs of arms (not ramps: the first
    pair, and any carriageway parallel to it, a divided road's two halves)
    carry their lane lines across the fan, cut in as A13's through lines
    are. Their edge lines go across too, broken where a crossing road's
    pavement lies.
  - The crossing roads' own lines stop at the junction.
- **Little Rock Road under I-85.** OSM draws four motorway_link ways
  crossing between Little Rock Road's two carriageways under the deck (the
  left turns to and from the ramps), crossing each other at nodes 428 and
  520 and the carriageways at 19-46 degrees: eight nodes in 40 m, seven of
  them merge-and-diverge clips. Now they are crossings and join one cluster
  (nodes 427, 428, 429, 485, 519, 520, 521, 11028; 10 inside edges): one
  paved area, no grass, no ramp lines across it, Little Rock Road's lanes
  and edge lines carried through.
- **No pier in a junction.** A pier or bent no longer stands inside (or
  within its clearance of) a junction's paved area below its deck
  (`PierBlocked` reads the tile's fan floors).
- **The I-85 decks there stay one structure.** e1167/e1170 (ways 38572136
  and 38572139) have no `man_made=bridge` outline, so the owner's rule
  decides: opposite carriageways with a 4.6 m gap (under 6.1 m) are one
  union. The soffit over the junction is that deck, 42 m wide and 100-117 m
  long as OSM draws the bridges. A NEVER override in
  `tools/city/deckpairs_overrides.json` would open the 4.6 m slot of sky if
  the owner's Street View shows two structures.
- **The other interchange: West Arrowood Road under I-77** (box -8320,-5320
  to -8000,-5060). It has no ramp crossing. Its west junction was the grey
  patch; it now takes West Arrowood's surface. Its other faults are still
  there: the two carriageways between the ramp signals overlap
  (ribbon/ribbon T2 110 m2, four lanes each way on lines OSM puts close
  together), a 6.7 m2 grass hole in the west cluster, and a fan over a
  foreign road (56 m2).
- **Known:** a fan stands 1.2 cm proud of its arms (`FanProudM`). From a
  driver's eye 3-4 m before a mouth the step shows as a thin line (the
  AFTER photo southbound). Every fan has it; it is not new.
- No rebake (tiles build at runtime). Node heights at the converted
  crossings move by up to 0.26 m, because the solve treats a fan's node as
  a junction. No height rule changed.

**BEFORE -> AFTER**

| | before | after |
|---|---|---|
| T1/T2 junctions in another surface than their main arm, OwnerBox | 107 of 292 | **0** of 298 |
| the main road's other arm in another surface, T1 / T2 | 27 / 40 | 14 / 12 |
| new asphalt share of road length, city-wide | 38.1% | 38.3% |
| Little Rock box, coplanar T1 m2 (arm/arm, clip, ribbon, gore, fan over road) | 437 | 224 (cluster over its own arms 114 of it) |
| Little Rock box, branch overshoot T1 m2 / underlap road piece T1 / T2 | 163 / 267 / 164 | 44 / 190 / 45 |
| Little Rock box, grass in the cluster box | 0 (no cluster) | 5.4 m2 |
| West Arrowood box | (identical before and after; faults listed above) | |
| worst view draws (trade_tryon) | 210 | 205 |
| tile build p95 (9 budget sites) | 120.4 ms | 119.6 ms |

- The one AuditOnly ran on the first rule (every skewed crossing; no lines
  across). It failed the 16 known checks plus the fan mouth probe (4 probes:
  Tyvola Road's deck x3, East 12th x North Caldwell). The bridge rule and
  the ramps-only rule then return those nodes to their earlier drawing, so
  the probe is expected back at 0, but it has not been re-run. The lane
  survey stayed at 8 runs; the PAINT, MARKS and DRIVE AUDIT results were
  clean.
- Boxed launch in the OwnerBox on the first rule: 12 -> 13 spots (one at
  North Caldwell split into two smaller ones, 0.493 -> 0.173 + 0.169 m).
  Routes stayed at 0. Both of those nodes are street skews, which now keep
  the clip.

## Parking aisles, islands and cul-de-sac necks (leftover item 4, 2026-10-03)

L8 laid 2,601 lots flush with stall lines and curb cuts, but on its own grid:
no driving aisles from OSM, no islands, buildings paved under, and the
street ran into each cul-de-sac bulb at a 60-120 degree kink. The data is
in `tools/city/lib/lots.mjs` (LOTS v2), the drawing in
`Scripts/City/CityMeshes.Lots.cs` and `CityMeshes.Minor.cs` (`BulbNecks`,
`NeckArc`), the checks in `Editor/CityAudit.Minor.cs` (MINOR NECK, LOT AUDIT,
and `CityAudit.LotsOnly` on its own).

- **Aisles from OSM.** The L8 fetch already held 4,045
  `service=parking_aisle` ways, so nothing new was fetched.
  - 3,960 are on the surface; the rest are tunnels, covered, indoor, or on
    another layer or level.
  - An aisle is 7.32 m two-way or 4.9 m one-way. One with any length inside a
    lot polygon belongs to that lot: its corridor is paved with the lot, out
    to 30 m past the lot's box.
  - An aisle no lot reaches (or runs on past its lot) is paved on its own, as
    an "aisle-only" lot with no stalls.
  - Where an aisle ends within 6 m of a street (tertiary or below), the
    lot's entrance and concrete apron go there. Otherwise L8's
    nearest-point rule applies.
- **Stall rows along the aisles.**
  - Each run of stalls has its own direction (LOTS v2). The stalls stand
    square to their aisle on both sides, from the aisle's edge (the line
    RDP-simplified at 0.5 m, longest segment first).
  - A stall is kept only wholly inside the lot (inset 0.3 m, 0.6 m off
    buildings), clear of every aisle corridor and of the rows already laid.
  - The rest of the lot gets L8's double-loaded module on the main aisle's
    own lines, in phase with its stalls. There, a stall is kept only where
    the aisle it faces is paved and free of stalls.
  - A lot with no OSM aisle keeps L8's grid.
- **Holes and islands.**
  - A lot is cut clear of every building (grown 0.45 m; a gabled house by
    its drawn box), of OSM's inner rings, and of the lots laid before it
    (grown 0.4 m).
  - An inner ring that is not a building and is at most 400 m2 is an
    ISLAND. So is the end stall of a row of 4 or more whose next slot would
    leave the lot (set back 0.6 m from the aisle, 0.15 m off the last line).
  - An island is raised curbed grass. Its top is the lattice's own pieces
    lifted 0.15 m, in the ground mesh's grass (or its paving where the tile
    has no grass). Its curb is a vertical face from 5 cm under the lot to
    1 cm over the top, in the pavement concrete (or the structural concrete
    where the tile has no pavement). That is at most +1 draw a tile.
- **The road keeps its own pavement.**
  - At run time a lot is cut along every junction fan's ring and every
    grounded ribbon's drawn edge (`LineModel.Extents` + 15 cm, between the
    trims, 2 m steps). Pieces under them are left to the road.
  - The export's road band does not know the aux lanes and turn bays, and
    a fan's curb returns reach past it.
  - `PSX_CITY_LOTROADCUT=0` turns the cut off and lays the lots as L8 did.
- **Cul-de-sac necks.**
  - Where each street edge meets its bulb there is now a curb return of the
    class curb radius (`CurbRadius`: 7.5 m local, 12 m arterial), tangent to
    the edge at the mouth and to the circle from outside (a reverse curve).
  - It is solved after the line model's eases and the merge zones, on the
    ribbon's own normal, 5 % over the radius. On a straight street the
    radius is t^2 = (R + r)^2 - (h + r)^2.
  - The circle stands on the mouth's own line, at the node's foot on it
    (within 0.6 R), and the fan is laid round that centre. So a street that
    bends into its bulb meets it as a straight one would.
  - A street too short keeps 2 m of ribbon and gets a smaller bulb, else a
    smaller radius. None of the 73 needed either.
  - `PSX_CITY_BULBNECK=0` draws the L8 bulb.
- **Tools.**
  - `CityPreview` view `lot_eye_139` is a driver's eye 1.2 m over the lot's
    own surface (`NamedView.ground`).
  - `CityAudit.LotsOnly` (`lots_audit.txt`) runs the neck census, the LOT
    AUDIT and 18 tiles timed twice, with the road cut off and on.
    `PSX_LOTS_NECKONLY=1` runs the census alone.

**BEFORE -> AFTER**

| | before | after |
|---|---|---|
| OSM parking aisles drawn (core box) | 0 of 4,045 | **3,894** (96.3 %; 98.3 % of the 3,960 on the surface), 277.4 of 278.7 km |
| lots / area | 2,601 / 4.25 km2 | 3,695 (1,021 aisle-only) / 4.84 km2 |
| stall rows (aisle / fill / L8 grid) / stalls | 6,105 / 84,820 | 6,262 (3,542 / 1,171 / 1,549) / 57,873 |
| islands (row ends / OSM) | 0 | 3,250 (3,139 / 111) |
| holes (buildings / other inner rings) | 0 (paved under) | 73 / 103 |
| entrances (where an aisle meets a street) | 1,646 | 2,422 (1,594) |
| bulb necks sharper than the class curb radius | 145 of 146 (tightest 1.65 m, kink up to 122 deg) | **0** of 146 (tightest 7.86 m, kink 7 deg: the arc's chords) |
| LOT AUDIT, OwnerBox: lot pavement over a building / ribbon / fan / another lot | 11,554 / 565.5 / 243 / 28.3 m2 (134 lots) | **0 / 0 / 0 / 0** (fans 382 m2 and ribbon bands 1,383 m2 cut out) |
| charlotte_city.bytes | 6.31 MB | 6.64 MB |
| CITY AUDIT failures | 17 (item 3's run: the 16 known + the fan mouth probe) | 16 (the 16 known; fan mouth probe 0) |
| DRIVE AUDIT | zeros | zeros |
| tile build p95, 9 budget sites (cap +10 % = 131.6 ms) | 119.6 ms | 124.5 ms |
| worst view draws (trade_tryon) | 205 | 206 |
| lattice 0.5-8 cm under a road, T1 / T2 / T3 m2 (L8's audit before) | 303 / 547 / 381 | 292 / 504 / 187 |

- The one AuditOnly failed only the 16 known checks. Each value is the
  same as before except the terrain-fidelity margin stations, 2,804 ->
  2,816. That was not traced: item 3's last fixes were never audited.
- The MINOR NECK and LOT AUDIT checks are new and pass. MINOR LOTS still
  triangulates every ring. linecheck --ratchet PASS (LOTS is not one of
  its inputs) and `export --check` IDENTICAL.
- Fewer stalls is expected. L8's grid laid stall lines straight across the
  real aisles (lot 139, now lot 143: 348 stalls on its grid, now 230 in 28
  rows beside its 13 OSM aisles, 7 islands, 3 buildings out). OwnerBox
  stalls 22,301 -> 14,253.
- Not done (lean): islands planted with trees, directional arrows on
  one-way aisles, end-of-row islands at cross aisles, lots in
  CityElevation's ground.

## Houses on their lots, and their driveways (leftover item 6, 2026-10-03)

The owner: "houses lack driveways and many houses are above ground with
their concrete foundations". A prefab home was seated on the HIGHEST drawn
ground under it and its baked skirt covered the rest, so on a slope its
downhill side showed up to 2.9 m of concrete; a procedural house (a real
footprint's gable box, a frontage gable, a fill house) stood on the lowest
of its corners' GroundY, so its uphill side ran up to 4.5 m into the hill,
and 13 in the two audited boxes showed daylight under a wall. No house had a
driveway. The work is in
`Scripts/City/CityHouses.cs` (the table, the pads, the driveways),
`CityMeshes.Houses.cs` (the driveways cut into the ground), the house seats
in `CityMeshes.cs` / `CityBuildings.PropSeat`, and the census in
`Editor/CityAudit.Houses.cs`.

- **One table of the city's buildings** (`CityHouses`, per 256 m tile,
  global data only, so every tile that asks gets the same answer): the
  real footprints, CityBuildings' lots and the interior fill. The fill's
  placement moved here from `BuildHouses` unchanged (cell by cell, the same
  hashes). A HOUSE is a footprint's gabled box or house polygon, a frontage
  gable, a fill house, the prefab house or a trailer; every other building
  is a blocker.
- **The lot is graded** (`CityHouses.Lattice`, read by
  `CityMeshes.LatticeVertex`). A lattice corner whose NEAREST building is a
  house within 16 m of its walls is pulled to that house's pad: fully within
  8 m, smoothstep back to the land at 16 m. The pad is the natural ground at
  the middle of the house's front (the face toward its nearest street),
  within 1.5 m of the ground at its middle, then held inside the limits of
  the corners it grades. Those limits are the road section's own
  (`CityElevation.Ground`'s terms; `GroundTerms.cut` is new): never above a
  road's cap, back slope or deck protection, never below a road's fill
  floor; a creek's or lake's banks are not touched. A corner nearer a
  building that is not a house keeps its land. Everything that stands on the
  lattice (houses, trees, poles, signs, lots, verges) stands on the graded
  ground.
- **Each house sits on its own pad.**
  - A procedural house's storeys start at its pad, never more than 0.6 m
    under the highest ground at its walls (`CityHouses.FloorBuryM`). Its
    walls run down to the lowest drawn ground at them, so none stands over
    air.
  - A prefab home is seated on its pad. It sits into its high corner by as
    much as its own plinth allows (the house's 0.59 m course stays over the
    grass, `SeatBuryM` 0.21 m; trailers 0) and at most 0.5 m more where a
    neighbour's pad rises behind it (`PropSeatOn`). `CityProps.Def.baseM`
    records the model's lowest course (house 0.59 m, trailers 0.24 m, from
    the bake log).
- **Driveways** (`CityHouses.DrivewayOf`). There is no OSM `service=driveway`
  in the cache, so every driveway is synthesized.
  - It runs 3 m wide from the street's drawn edge to the house's face, at
    the garage end its hash picks, then the other end, then nearer the
    middle. Every face that looks toward a street within 75 m is tried,
    nearest street first. A frontage lot uses only its front.
  - It must be square to its street and to the face within 32 degrees, and
    at most 60 m long. Where a parking lot lies between the house and its
    street, the driveway starts inside the lot's pavement. Where buildings
    in front leave a gap, the driveway is aimed through it.
  - It never crosses another building or its lot (box + 0.3 m, a blocker
    + 0.6 m), a parking lot, water or a ravine, another road, or a
    junction's mouth. For that last check, its own street is measured along
    it from the fan's trim, other junctions by their fan's reach, each with
    4 m clear. Two neighbours' driveways may meet.
  - It is cut into the lattice triangles like a parking lot: drawn in the
    ground mesh's pavement concrete, flush, in the ground collider, at most
    +1 draw a tile.
  - The street's verge across its mouth and 1 m flared wings is poured
    concrete: the curb cut (`ApronAt`).
  - The static occupancy mask marks every driveway and curb cut (+0.5 m), so
    no pole, sign, signal or tree stands on one. No street lamp stands on
    one (`TryLamp`).
- **Switches.** `PSX_CITY_HOUSEPADS=0` turns the grading and the seats off;
  `PSX_CITY_DRIVEWAYS=0` turns the driveways off.
- **Tools.**
  - `CityAudit.HousesOnly` (houses_audit.txt) and the full audit's HOUSES
    block. Both measure the owner box and a suburban box (the 1.5 km round
    the 1 km cell with the most falling prefab lots; `PSX_HOUSE_SUBURB_BOX`).
  - They report each house kind, the fall at the walls, the EXPOSED
    FOUNDATION of prefabs, procedural houses BURIED, GAPs and DRIVEWAYS
    (with why not, and every tree, pole, sign post, signal pole, STOP sign
    and lamp tested against them).
  - `PSX_HOUSE_EXPLAIN=1` explains the lattice corners round the worst lots.
  - `CityPreview` group `houses` holds the four photographs.

**BEFORE -> AFTER** (HousesOnly, boxed)

| | before | after |
|---|---|---|
| prefab EXPOSED FOUNDATION, suburban box (82 homes) p50 / p95 / max | 1.27 / 2.65 / 2.93 m (76 over 0.6 m) | **0.29 / 0.42 / 0.75 m** (3 over 0.6 m, 0 over 1.0) |
| houses with daylight under them (owner box / suburban) | 12 (worst 1.35 m) / 1 | **0 / 0** |
| procedural houses BURIED p50 / p95 / max, owner box (1,229 houses) | 0.79 / 2.10 / 4.53 m | 0.13 / 0.60 / 0.60 m |
| procedural houses BURIED p50 / p95 / max, suburban box | 0.60 / 1.76 / 3.41 m | 0.01 / 0.50 / 0.60 m |
| fall at the walls, prefab homes, suburban p50 / p95 | 1.02 / 2.36 m | 0.16 / 0.57 m |
| driveways, owner box | 0 | **1,012 of 1,232 (82.1 %)** |
| driveways, suburban box | 0 | **714 of 1,031 (69.3 %; 88.3 % of the 809 with a mapped street within 75 m)** |
| furniture standing on a driveway | - | **0** (12,457 + 9,773 objects tested) |
| tile build p50, owner box / suburban (HousesOnly) | 104.8 / 32.6 ms | 101.8 / 37.3 ms |
| budget, 9 sites: tile build p50 / p95 / max (cap +10 % = 137 ms) | 31.7 / 124.5 / 252.0 ms | 37.8 / **126.5** / 243.0 ms |
| tree frames p95 / max; worst view draws | 6.7 / 20.9 ms; 206 | 9.3 / 23.5 ms; 206 |
| kept off the driveways (audit tiles): street lamps / utility poles / trees / billboards / business signs | 351 / 1,088 / 135,687 / 321 / 1,439 | 348 / 1,084 / 135,434 / 310 / 1,435 |
| DRIVE AUDIT | zeros | zeros |
| CITY AUDIT failures | 16 known | 18: the same 16 (same values) + the two driveway gates below |

- The one AuditOnly also kept linecheck --ratchet PASS and the WP-09
  sliced build identical to the one-go build (75 tiles). No data changed
  (no export). Map heap 25.4 -> 25.7 MB.
- **The driveway target (95 %) is not met.**
  - Owner box (uptown's dense blocks), houses without one:
    - 88 have other buildings between them and every street;
    - 52 would run more than 32 degrees off square;
    - 36 sit at a junction's mouth;
    - 14 have their face on the street's edge;
    - 20 have no street within reach.
  - Suburban box: 186 of the 317 without one are fill houses with no street
    within 75 m. The fill stands in for subdivisions whose streets are not in
    the data (outside the core only the arterials are fetched).
- **Not done (lean).**
  - Shared private lanes for houses behind houses.
  - Bending a driveway round an obstacle.
  - Letting a driveway leave its face at up to 63 degrees again (the run
    before the 32-degree rule had 1,058 owner-box driveways, 85.9 %).
  - Fetching the residential streets beyond the core (Q6), which would give
    the fill houses streets.

## Invisible colliders (2026-10-04): the I-277 wall at 1:18, closed solids, an audit

The owner, Uptown Loop at night, race clock 1:18: "I still hit an invisible
wall right here on 277". He was on a deck with a parapet on his left and a
lower deck past it, and the car stopped on what looked like clear pavement.

**What the wall was.** It is 2,765 m into the loop (route waypoint 692), at
I-277's left exit by East 4th Street. The 15 m deck e14177 ends at node 3557.
A 1 m connector, e2344, then reaches node 2264, where mainline e1393 starts.
e1393 is squeezed against the deck it carries on from. The squeeze treats
e14177 as a parallel neighbour, so e1393's drawn ribbon starts 6.4 m further
right (left edge -1.5 m, against the deck's +4.9 m). The twin carriageways'
union median strip (A2, a Jersey, `open0` false) started at that edge.

`Continued` said a run on e2344 carried the median on, but the two medians
are 6.4 m apart. So the Jersey's first span had no end face. Two things
followed:
- The eye saw nothing. The PSX shaders cull back faces, so looking into the
  hollow end showed nothing drawn.
- The audits saw nothing either. Ray queries skip back faces, so BLUNT and
  RAIL ENDS rays went straight into the open end.

PhysX collides a triangle mesh from both sides, so the car stopped dead.
The DRIVE audit also skips edges shorter than 1 m, which is where it stood.

**The rules (general).**
- `EdgeLinesUp` (CityMeshes.cs). A rail or median barrier carries on uncapped
  across an edge end only where the through road draws its edge on the same
  side within 0.75 m (`EdgeContinueM`) of where this one's ends. Otherwise the
  end is a run end: capped, with the run-end treatment.
- `UnionEndAdrift` (CityMeshes.Unions.cs) applies the same test to a union
  run's end at a node. An end that meets nothing is CLOSED: the V on a deck,
  the turned-down Jersey on the ground. The V's legs now have an end face at
  the closed end.
- A collider is its drawn geometry:
  - The W-beam collider is the drawn beam: face (on the W's middle), back
    8 cm behind, top, underside and ends. It was a 0.45 m box from half a
    metre under the verge.
  - A lamp post's box is the drawn post: 0.26 m cobra-head, 0.14 m acorn.
    It was 0.3 m for both, 8 cm of solid round every uptown acorn post.

**The audit: INVISIBLE COLLIDERS** (`Editor/CityAudit.Invisible.cs`, in
`tools\city-cycle.ps1 -DriveOnly` after the drive audit; menu "Audit City
Invisible Colliders" writes `city_invisible.txt`; PSX_DRIVE_INVIS=0 skips it,
PSX_INVIS_BOX=0 skips the box).

It stands up the game's own tiles through CityWorld (EnsureTile and
PlantTrees: poles, signs, signals and the trunk table), then checks two
things.
- **LANES.** Along every lane of the three race routes (1 m) and of every
  road in the OwnerBox (both ways, 2 m), with back faces ON:
  - a car-sized box (1.7 x 1.1 x 4 m, floor 0.2 m up, pitched with the road)
    on each lane centre, kept 0.6 m inside the drawn edge (the rail line) and
    swept 1.05 m on;
  - rays at 0.3, 0.6, 1.0 and 1.4 m;
  - a ceiling ray to 1.6 m;
  - the trunk table.

  A hit with no drawn face turned toward the car within 5 cm is INVISIBLE (a
  FAIL). For a box that overlaps, rays from 18 points inside it look for a
  front face that is drawn.
- **SOLIDS.** For every Solid-layer collider that is not its own drawn mesh,
  each triangle centre (or box side centre) needs a drawn face within 5 cm,
  or must sit inside drawn geometry.

**BEFORE -> AFTER**

| | BEFORE | AFTER |
|---|---|---|
| INVISIBLE in the three routes' lanes | 2 (uptown 2,765 m, e14177/e2344, the hollow Jersey end) | **0** |
| SOLIDS on the routes' tiles (uptown, live colliders) | 10,998 samples (460 lamp posts, 38 W-beams, 1 prop) | 21 (prop prefab boxes only) |
| INVISIBLE in the OwnerBox lanes (reported) | - | 51 (see below) |
| DRIVE AUDIT, routes: walls / steps / holes / off / grass / BLUNT | 0 | 0 |
| RAIL ENDS on approaches: routes / box | 0 / 9 (item 2) | 0 / 8 |
| short barrier census | 0 | 0 |
| CITY AUDIT (one AuditOnly, OwnerBox) | 18 (item 6) | 18, the same set and values |

**Not done (HARD STOP, said honestly).**
- **The race line still meets the median there.** The watched race check
  (`race-play-check -Venue UptownLoop -Hour night -Finish`) came back RACE
  CHECK OK, but the autopilot player hit Barriers HARD at waypoint 692
  (1:19.8, 35.8 m/s). The Skyline was pinned at waypoint 698. That spot is
  now a drawn V leg with an end face, not a hollow end, but the route's own
  line (e1393's OSM line) still runs 1.5 m outside e1393's squeezed ribbon
  onto the median strip.
- The fix is a squeeze rule, not a collider: never squeeze a road against the
  carriageway it carries on from (here through a connector under 3 m). It
  touches lane extents everywhere such connectors are, so it needs its own
  audit and race check.
- OwnerBox lanes (reported):
  - the tower_01_4 prop's baked Solid box stands over South Boulevard and
    link e7753 (46 hits);
  - three fan-chord rails are 0.95 m over a lane with no underside drawn
    (N Caldwell / E 12th / link e5331);
  - the ground stands 0.21 m over an East Trade Street lane.
- The prefab Solid boxes (towers, trailers, house_simple) are larger than
  their models; that needs a prop rebake.

## Race maps show the streets (2026-10-04)

The owner: "on Race Maps, I'd like them to show the local streets in darker
gray. Race Track Map is in brighter white to stand out. This can help with
navigating interchanges and side streets."

**What changed.** `TrackCatalog.Thumbnail(def, size, hud: true)` draws the
Charlotte street network for a city race (`def.IsCityRace`) before the
route (`DrawCityStreets`):
- It uses every edge inside the map's square, in the map's own `MapFrame`
  projection, least important first.
- Local streets are the darkest grey (62), then secondary/tertiary (80), then
  primary/trunk (98). Freeways and ramps are the lightest grey (120), 2 px
  wide for the freeways, so an interchange reads.
- The route is drawn on top in white. Its dark halo now also covers the grey
  streets under it, never the line.
- The street drawing is rasterised once into the map texture and cached per
  size. Nothing new is drawn per frame.
- The main game's venues are unchanged (no street network).

**Tools only:**
- `TrackCatalog.HudStreetsOff`, `TrackCatalog.ForgetThumbnails`,
  `RaceHUD.RebuildPreviewMap`.
- `Editor/RaceMapShots.cs` (menu "Race Map Shots (Uptown)") shoots the
  UptownLoop chase view twice, BEFORE (no streets) and AFTER. It writes
  `Screenshots\racemap_uptown_{before,after}.png`.

## Stray medians on I-277 (2026-10-04): no squeeze against the road it carries on from, same-way decks, walls in lanes

The owner, two Uptown Loop frames on I-277 in the loop's lower-right part
(night 1:58.9, day 1:29.1): "I found stray medians on 277." He was on a
concrete deck with a wall starting in the middle of the road ahead, traffic
beyond it, and a wall between lanes of the same direction ending in a flat
grey face.

**What was wrong.** This is the spot the invisible-wall fix left open: 2,765 m
into the loop (waypoint 692, by East 4th Street). The 4-lane deck e14177
ends at node 3557, the 1.4 m connector e2344 runs to node 2264, and mainline
e1393 (3 lanes) starts there, the exit e7338 leaving on its right. The line
model lays e1393 out exactly where e14177's lanes run (-10.32..+4.86 m off its
OSM line). Then `Squeeze` cut its left half to 1.2 m.

The squeeze looks for a parallel road beside every section. Past the end of
a segment it counts the vertex "where the road runs on through it", and node
3557 has two edges. e14177's end stood 1.4 m behind e1393's first section, so
it read as a neighbour on the left. Its pull moved e1393's drawn left edge
from +4.86 to -1.53 m, and the ease carried that 20 m on. The union median
with the opposite carriageway (e2351) starts at e1393's drawn edge, so its V
stood 6.4 m inside the deck's lanes. That is the wall in the middle of the
road (the race line runs on e1393's OSM line), with lanes of the same
direction on both sides of it and the V leg's end face toward the car.

**The rules (general).**
- **No squeeze against the road a carriageway carries on from**
  (`CityMeshes.CarriesOnThrough`, in `Squeeze`). A road that reaches this one
  through a connector edge under `CarryOnLinkM` (3 m) is one carriageway in
  three OSM pieces, never a road beside it. Two edges sharing a node were
  already one pavement at one height (`ArmsApart`); this adds the stub
  between them. The same height test applies (within `ArmSplitDyM`).
  PSX_CITY_SQUEEZE_CARRYON=1 restores the old squeeze, for a BEFORE count.
- **Same-way decks share no parapet** (`SameWayDeckBeside`, in the side-flag
  pass). Two one-way decks running the same way (within 25 degrees) that are
  squeezed together and stand within 0.10 m of each other in height (a ramp
  on structure beside its mainline, a collector beside its carriageway) are
  one deck. The squeeze strip between them is floored flush and no rail
  stands in it. Opposing traffic keeps its one shared barrier. Same-way
  union pairs already had a flush strip and no median (hotfix
  `MedianOfUnion`). This rule fires on 0 edges of the race routes' 318
  tiles today, so it is a guard, not a change there.
- No new end faces. BLUNT is unchanged at 0 on the routes.

**The audit: WALLS IN LANES** (`CityAudit.Invisible.cs`, in
`city-cycle -DriveOnly`, a check).
- On every race route edge, the INVISIBLE sweep's drawn hits are measured
  against the edge's DESIGNED edge lines: `LineModel.LinesAt`, unsqueezed
  and unclipped, which are the lanes the driver sees painted.
- A barrier, rail or solid met more than a rail's inset (`RailW`, 0.3 m)
  inside those lines is a wall in a lane. A parapet's foot on a shoulder is
  not.
- `RunDrive` also prints two lists: **CARRY-ON SQUEEZE** (the edges the new
  rule changed, with their drawn extents) and **SAME-WAY DECKS**.

**BEFORE -> AFTER** (main 61484764; the drive audit on the three routes):

| | BEFORE | AFTER |
|---|---|---|
| e1393 drawn extents at s 0.5 (its deck e14177: -10.32..+4.86) | -10.32..-1.53 (squeezed against e14177) | **-10.32..+4.86** |
| CARRY-ON SQUEEZE, edges on the route tiles | 1 squeezed (e1393) | 0 squeezed |
| WALLS IN LANES, the three routes | 2 places, both uptown 2,764-2,765 m | **0** |
| the same at a 0.1 m allowance (first run) | 10 places, 33 hits (+8 South Tryon St) | 8 places (South Tryon St only) |
| INVISIBLE drawn faces in the routes' 0.6 m lane band (reported) | 12 notes | 9 |
| DRIVE AUDIT on the routes: walls / steps / holes / off / grass / BLUNT | 0 each | 0 each |
| INVISIBLE in the routes' lanes / rail ends on approaches (routes) | 0 / 0 | 0 / 0 |
| short barrier census, city-wide | 0 | 0 |
| SAME-WAY DECKS, edges changed on the route tiles | - | 0 |
| CITY AUDIT (one AuditOnly, OwnerBox) | 18 (invisible colliders) | 18, the same set; TWIN (c) walls 77 -> 75, every other value equal |
| LINE MODEL: through-lane continuity (routes) / LANE ALIGN / OFFSET / symmetric widenings | 658 (4) / 128 / 324 / 0 | the same (the squeeze is a mesh step after the model) |
| tile build p95, 9 budget sites (CityBudgetProbe, back to back, old squeeze by PSX_CITY_SQUEEZE_CARRYON=1) | 128.0, 124.9 ms | 131.5, 137.8 ms |

- The South Tryon St places at the 0.1 m allowance are its bridge rail
  (e1107/e1108): the rail's traffic face stands 0.3 m inside a curbless
  edge, 0.23 m over the painted edge line. That is the rail's designed inset,
  not a wall in a lane.
- The 9 notes left in the 0.6 m band are parapet feet on shoulders: I-277
  e2438 at 1,781 m, e4601/e2139/e8474 at 8,675-8,772 m, South Tryon, and
  Independence e13562. All are outside the edge lines.
- The race check (watched, `race-play-check -Venue UptownLoop -Hour night
  -Finish`): RACE CHECK OK, 260 s.
  - The autopilot player finished with 0 hard hits (damage 4). Before, it hit
    Barriers HARD at waypoint 692.
  - One rival, the Skyline, took its planned driver error at 36 s. It drifted
    off its lane across the shoulder into the right edge barrier of I-277
    e2315 at 1,484 m (wp 371) and retired. That is the roadside parapet, not
    a stray wall, and far from this change.
- Named views: `CityPreview` group `i277b`, the loop's south-east quarter at
  1.2 m on the race line (1,750-3,600 m). BEFORE/AFTER pairs:
  `i277b_2745` (the 2,765 m median start, 20 m before it) and `i277b_2725`.
- Tile build p95: the AuditOnly read 223.1 ms against 117.6, but it ran
  beside another project's Unity build: parse +63% and the solve +163%,
  code this change does not touch. The back-to-back probes above differ
  only in one edge's squeeze. On the mean the new code is +6.5% over the
  old (134.7 against 126.4 ms), inside the +10% budget. Two runs of the
  same code differ by up to 5%.
- **Known, not done (lean).**
  - The OwnerBox's 12 BLUNT rail ends (bridge approaches off the routes,
    item 2's list).
  - The prefab Solid boxes (towers) that INVISIBLE reports in the box.
  - Rails standing within 0.6 m of a drawn edge on curves (the band notes
    above).
- No rebake (tiles build at runtime); no height moved.

## Uptown facades (Uptown B1, 2026-10-04): one atlas, real floors, tints from OSM

The before-shots of the five uptown views (`CityPreview.RunUptownRef`) showed
every tower in one code-drawn 64 px blue-grey glass whose fixed 8 m repeat
aliased into curved moire bands at a distance, and at dusk every pane lit the
same cold white. This package replaces that look.

- **One facade atlas.** `Art/City/Facade/city_facade_atlas.png` (1024 x 256,
  made by `py tools/city/facade_atlas.py`) holds eight 128 x 256 columns: a
  silver grid curtain wall, a blue glass and a teal glass (the owner's
  skyscraper_pack `building_03/04/08`), precast stone and brick with punched
  windows (the Buildings pack's `building_10`/`building_01`, the facades the
  city already wore); 5-7 are spare copies. Every OSM tower, midrise, brick
  block, shop's upper floors and fill box wears it through ONE material
  (`CityFacadeGlass`, slot `FacadeGlass`). Houses and shopfront strips keep
  their own. Sources and licences are in `tools/city/SOURCES.md` (owner's
  licensed assets, confirmed 2026-10-04; no brands in any of them).
- **PSX/Lit `PSX_FACADE`.** The vertex colour carries the building: rgb a tint
  (x2, 128 = as painted), alpha the column (x32). U wraps inside the column;
  the texture and the night mask are read with `tex2Dgrad` on the wrapped uv's
  own derivatives, and the atlas (only it: `PSXTextureCaps.MipsFor`) keeps
  point-filtered mip maps. That is what removes the moire.
- **Real floors.** V counts the building's own storeys from its ground floor
  (`CityMeshes.PickFacade`): 3.8 m an office, 3.1 m a home (OSM
  apartments/residential/hotel, or brick with no use tagged), stretched so the
  roof is a whole floor; crowns continue the bands. U is whole repeats per wall
  (bays 2.2-2.85 m on the curtain walls).
- **Looks and tints.** OSM first: `building:material` picks the family
  (glass, brick, stone/concrete; metal = the silver grid) and
  `building:colour` the tint (that colour over the column's mean, in linear
  light). Otherwise by height class and a position hash: towers mostly glass in
  three kinds (12% stone), midrises a third each of glass, stone and brick, low
  blocks and shops mostly brick, each in a shade from the look's palette.
- **PBLD v2.** The exporter writes one look byte per footprint (use class,
  material class, a colour flag + RGB). The 8 x 8 km snapshot has 21
  `building:colour` and 3 `building:material` outline tags, so today the
  palette does nearly all the work; B2 (building:part) brings 155 colours and
  85 materials and will bump PBLD to v3. `export_osm.mjs --check` reproduced
  the shipped bytes before the change; after it only `charlotte_bld.bytes`
  (1,742,804 -> 1,774,560 B) and the fingerprint moved.
- **Night.** `WIN_LIT_FRAC` 0.42 -> 0.34: ~25% of windows lit at dusk
  (`_PSXNight` 0.75), ~34% at night, the warm/cool mix unchanged. The curtain
  walls light by FLOOR SEGMENTS (four bays of one floor share an id), so a
  tower at dusk is dark glass with lit office floors, not a lit wall.
- **Retired:** the drawn `city_facade_glass.png` and its mask
  (`tools/night/window_masks.py` no longer lists it). Slots `FacadeTower`,
  `FacadeBrick` and (on tiles) `FacadeMid` are now unused by the tiles but kept.

**Numbers** (`CityBudgetProbe`, sandbox, same machine, before = main f103b468):
no view at any of the 17 sites draws more; the sum of the 68 views 7,599 ->
7,387 draws; worst view 206 -> 189 (trade_tryon); tile build p95 over all 225
tiles 127.1 -> 122.5 ms (single sites move +-10%, the run-to-run noise with
other Unity jobs on the machine). The five reference views were re-shot at noon
and dusk (`scratchpad/ba/uptown_b1_*.jpg` in the planning session).

**Known, not done (lean):** facades at 1 km+ fall to the mean colour of their
look (no bands - the price of no moire; a mip bias could keep a hint of
floors); crowns are not lit; the house siding is still drawn in code; heights
from building:part are B2.

## Uptown heights from building:part (Uptown B2, 2026-10-04): tiers, setbacks, roof shapes

The game read only `height` and `building:levels` off each OSM outline, so a
building whose heights live on its `building:part`s stood as a 15-16 m slab
(the 300 tower, a new office tower, the stadium), and every tower was one
prism. This package reads the parts.

- **One fetch.** `tools/city/fetch/fetch_parts.mjs` (count first, then
  `out geom`) wrote `tools/city/cache/parts_core.json` (gitignored; sha256 in
  `cache_manifest.json`): every `building:part` way and relation plus the
  `type=building` relations in the same 8 x 8 km core box as
  `fetch_bld.mjs`, as of `[date:"2026-09-12T02:44:33Z"]` (1,088 ways, 40
  relations; Overpass prints the fetch day as `timestamp_osm_base` for a
  dated query).
- **Parts to outlines** (`tools/city/lib/parts.mjs`). Each part goes to the
  smallest kept outline holding its centroid. A part is usable when it has
  `height` or `building:levels` (3.4 m a level; `min_height` or
  `building:min_level` its floor). If the usable parts carry less than half
  the parts' area the outline stays as it was (7, e.g. the silver crown
  tower's 61 parts with no heights). If they cover 80% of the outline
  (sampled), the outline is HIDDEN and drawn by its parts (107); otherwise
  (19) the outline stands, no taller than its tallest part, and the parts
  that rise above it start at its roof.
- **Layer > 0 outlines come back with their parts.** The exporter drops every
  outline with `layer > 0` as "elevated", but most are towers over their own
  garage. 29 of them come back when their parts give heights and no road runs
  through them (the same straddle test). The rest stay dropped.
- **Indices do not move.** Outlines keep their index and order (the pack
  tower hash, lots, signs and houses read footprints by index or occupancy);
  hidden outlines stay for occupancy with their height raised to the tallest
  part. The 29 rescued outlines, then the 838 parts, are appended after them.
- **PBLD v3** (`charlotte_bld.bytes` 1,774,560 -> 1,882,371 B): after the v2
  look byte, one byte per footprint (bit0 hidden, bit1 part, bit2 never
  swapped, bits3-5 roof shape, bit7 extra) and, for parts, shaped roofs and
  landmarks, an extra block (u16 floor dm, u16 roof height dm, i16
  `roof:direction` or -1, u8 landmark, i32 the part's outline). The part
  inherits its outline's use, and takes its own `building:colour`/`material`
  over the outline's.
- **Tile builder** (`CityMeshes.BuildFootprints`). A part stands on its
  outline's ground (the lowest under the outline's and its own corners), from
  `minH` to `h`. A floating tier gets a soffit. The facade look is hashed off
  the outline's centre and the floors counted over the outline's height, so
  one tower's tiers match. Roofs (`EmitRoofShape`, in the walls' facade so a
  glass crown reads as glass): pyramidal to an apex, dome in three rings,
  round a barrel to the long axis, gabled a ridge with hipped ends, skillion
  one plane along `roof:direction` over sloped wall tops. 29 outlines carry
  their own `roof:shape` too (houses keep their gables). There is no shopfront
  strip on a part: a part is bucketed by its own centre, and a shop slot new to
  a tile is a draw call. There are no generic crown boxes on parts, shaped
  roofs or outlines drawn with parts. Collider = the drawn mesh, as before.
- **Kept out of the random pack-tower swap**: parts, their outlines, rescued
  outlines and the landmark table (`LANDMARKS` in `lib/parts.mjs`, 18 real
  buildings keyed by OSM id under NEUTRAL names only, with a landmark byte
  for phase C).
- **What it changes uptown:** the 300 tower 16 -> 141 m (on its 16 m podium),
  the new office tower 15 -> 147 m, the stadium 16 -> 58 m (37 parts; its bowl
  is C5), the spired crown tower's ten setback tiers to its 300 m spire,
  the pointed tower's 160-190 m pyramid, the slanted tower's skillion, the
  open-frame tower's parts, and the brick headquarters and glass setback
  tower's setbacks. Unnamed buildings go 16 -> 88 m, 12 -> 85 m, 12 -> 128 m,
  33 -> 117 m.

**Checks.** `export_osm.mjs --check` before the change: only
`charlotte_bld.bytes` and the bld fingerprint (version, footprints 31,696 ->
32,563, points, parts 838, hidden 107, roofs 115, landmarks 18) moved;
city/dem/routes stayed byte-identical. After the re-export it reads EXPORT CHECK
OK. Typecheck OK. Drive audit along the uptown route (`PSX_DRIVE_ROUTES=uptown`,
178 tiles): "nothing solid stands across any lane" 0, WALLS IN LANES 0, no
Buildings collider among the 155 reported lane faces. Its 3 FAILs are rail ends
(BLUNT 12), barrier/ground ceilings (5) and a pack tower's box collider (28
samples), none of them building meshes. `CityBudgetProbe` against B1
(24bc41dd): the sum of the 68 views 7,387 -> 7,389 draws, worst view 189 ->
189. One view is worse: trade_tryon back, 146 -> 150. The rest are equal or
fewer. Tile p95 over 225 tiles 122.5 -> 129.1 ms (+5%; nohydro 109.3 -> 106.5;
sites with no building change moved +-10% between the runs). Uptown sites:
trade_tryon 197.5 -> 209.8, tryon_start 162.0 -> 168.4, i277_uptown 333.2 ->
332.8 ms. Uptown ring tris 437k -> 465k.

**Known, not done (lean):** layer > 0 outlines without parts are still
dropped (a city-wide census would bring more back). 31 parts with no outline at
all are skipped. Part `roof:colour` is not read. The trade_tryon back +4 draws
are not explained yet; the suspected cause is occupancy moved by the rescued
block south of Trade. The stadium is a 58 m dark box until C5.

## Uptown facade materials (Uptown B1b, 2026-10-04): glass mirrors the sky, metal catches the sun

Owner after B1: "Buildings seem to have that white coating washed out faded
look that cars used to have. Can they be textured and lit properly as
metal/stone/glass?"

- **The cause** (measured on the five uptown views, `CityPreview.RunUptownRef`,
  boxes inside named towers; Ycode = Rec.709 Y re-encoded 0-255, sat = HSV
  saturation): B1's facade lit every texel of the atlas as MATTE PAINT
  (PSX/Lit's ambient + sun on the painted texel), and the atlas is mostly
  glass: the three curtain walls are 60-90% panes, stone and brick carry
  windows. Matte-lit glass is the cars' old "flour" look - its colour is
  whatever the hour's light makes of a grey texel, never the sky it mirrors.
  Noon: glass towers Ycode 30-36 at sat 0.13-0.17, silver towers 49-53 at
  0.04-0.06 (neutral grey), under a 222 sky. Sunset: towers 109-129, pale
  beige (sat 0.21-0.24), brighter than the sky behind them (83). Dusk: 104-126
  against an 86-88 sky. NOT the cause: the grade's floor (G1 already takes 80%
  of it by day, and these editor shots carry G1), the tints (silver palette
  0.80-1.15), the fog (pushed to 1.7-4 km in these views).
- **The fix, in PSX/Lit's PSX_FACADE variant** (no new variant, material or
  draw). Per pixel, from the column and the texel: on a curtain wall the dark
  texels are panes and the bright ones frames; on stone and brick the dark AND
  colourless texels are windows (brick is dark too, but red).
  - GLASS: Schlick (F0 0.12 curtain wall, 0.06 punched window) over
    `FacadeEnv` - the hour's sky above R.y 0.12 (adapted, by day half the
    exposure gap, as PSX/CarPaint), below it the CITY round the tower (walls
    of albedo 0.16 lit by the hour, brighter where the ray meets their sunny
    side, under 35% haze; the night ground's fog colour at dusk and night),
    then the ground. Tinted by the glass's own hue (0.85), following the
    painted pane (0.5 + 6 x its luminance) so the texture stays, a sun glint,
    and 0.55 of the old matte light (the pane as painted). The first cut
    mirrored the pale horizon band below 0.22 and made every noon tower a flat
    pale cyan; the city band is what keeps glass darker than the sky.
  - METAL (frames and mullions; crowns, roof shapes and OSM metal by the
    vertex alpha's half step, column x 32 + 16, `CityMeshes.FacadeMetal`):
    0.55 of the matte light, 0.45 albedo-tinted reflection, a broad sun
    highlight (pow 24, 0.40).
  - STONE / BRICK: matte as before; their windows are glass up close (the far
    mips average the windows into the wall, so a distant one stays matte).
- **After** (before -> after): noon glass 30-36 -> 54-75 (0.24-0.34 of the
  sky's Ycode), sat 0.13-0.17 -> 0.21-0.36, blue (R-B -3 -> -4..-25); silver
  49-67 -> 69-84, sat 0.04-0.07 -> 0.07-0.14. Sunset: glass and silver towers
  3-20 darker (frame_over_stadium 101 vs 121) and warmer (sat 0.18-0.25 ->
  0.29-0.38 on most). Dusk: 1-10 darker. Night: unlit glass p5 4-8 (dark), the lit
  windows as before. Stone and brick unchanged.
- **Targets** (the owner's three references: a clear-sky noon skyline, sunset
  glass reflections, blue-hour dusk): glass darker than the sky and coloured
  (0.3-0.5 of its Ycode, sat >= 0.2, blue at noon, warm at sunset) - met at
  noon and sunset. NOT met: at dusk the towers' mean stays at or above the
  sky (lit windows and the dusk sun on masonry; unlit glass p5 47-70 vs the
  sky's 74); a sunlit silver tower at sunset is still above the sky (101 vs
  83 with the sun behind the camera).
- Budget: CityBudgetProbe 68 views 7,389 -> 7,389 draws, none worse.
- Runner: `PSX_UPTOWN_HOURS=noon,sunset,dusk,night` (default noon,dusk).
- Not done: reflections are a sky + city model, not the real geometry; the
  dusk balance; stone and brick albedo untouched.

## Uptown massing and the far skyline (Uptown B3 + B4, 2026-10-04)

- **B3, towers with no parts** (`CityMeshes.Massing.cs`). Every tower of 60 m
  and more that OpenStreetMap gives no building:parts, no roof:shape and no
  landmark entry was a prism to its roof and, over 120 m, two generic stacked
  boxes. Now, chosen by a hash of where it stands (the same tower every visit,
  and never a copy of a real one): a PODIUM of 3-6 storeys over the whole
  footprint (stone under about half the glass towers) where the footprint's
  box is 22 m+ across and 40 m of shaft is left above it; a SHAFT inset 2-4 m
  (mitred inset; a fold, a sharp spike or under a third of the area left keeps
  the footprint as it is); and one roof: a metal-screened plant PENTHOUSE
  behind a 1.2 m parapet, a SETBACK (the top 2-8 storeys stepped in 3-5 m, a
  small plant box on it; towers of 90 m+) or a 2.2 m PARAPET band. The
  podium's walls are the prism's, so nothing the tile collides with moved
  out. The two-box crown is kept only for a landmark with nothing better yet
  (phase C).
- **B4, the far skyline** (`CitySkyline.cs`, `CityMeshes.Skyline.cs`). The
  city streams two tiles round the car and the far plane is 500 m, so from
  I-77 uptown did not exist until ~500 m out. The tallest towers (60 at most,
  60 m and up) within 2.5 km of uptown (an outline's height is its tallest
  part's) are emitted
  once, at load, by the tile builder's own `EmitFootprint` - parts, roof
  shapes, crowns, B3 massing, facade column and tint - with one panel a wall,
  no pavement cut, outlines simplified to 1.5 m, no parapets and no parts
  under 45 m: one mesh, 55 towers (140 buildings and parts), 3,102
  triangles, two submeshes (the facade atlas, the flat roofs) = 2 draws. No
  collider, no shadow (SunShadows.Exclude), its own copies of the two kit
  materials.
- **No floating buildings** (owner on the first publish: "I see buildings
  floating in the sky" - from I-77 the land under uptown is past the drawn
  world, so low, wide blocks hung over the horizon with sky beneath them):
  every skyline wall runs on 300 m below its ground, every tier stands on
  the ground (a tier whose podium part is left out cannot float), and the
  skyline is towers only - 60 m and up, parts 45 m and up (the first cut
  took buildings from 45 m and parts from 20 m: 236 pieces, 5,095
  triangles).
- **Beyond the far plane** (PSX/Lit `_Skyline`, 0 on every other material):
  past the world's fade start A (fogNear, 398 m in play) each skyline vertex
  is moved in along its own line of sight to A + s(1 - e^-(d-A)/s), s = 0.7 of
  the fade band - the same pixel, depth squeezed but in order and never deeper
  than the real point. It ends where the world's edge fade is about half: the
  last, mostly sky-coloured metres of the world are drawn behind the towers
  (ending the band at the far plane cut every far tower to a sliver behind
  faded treetops). Haze by the REAL distance, 1 - e^-(d/24 km) toward the
  hour's horizon colour (0.12 at 3 km, 0.19 at 5 km; 9 km in rain or snow);
  off in fog weather. The owner's no-fog rule holds: the drawn world still
  ends in its own short edge fade and nothing turns white. At night the
  facade's own lit windows.
- **No doubles**: a building is left out (its triangles dropped from the
  index list, re-set only when the set changes) while its tile is live AND it
  is nearer than A - there the tile draws it, clear. Further out the tile's
  copy is fading into the sky and the squeezed skyline copy stands in front of
  it. `CitySkyline.Refresh` also sets the renderer's bounds round the squeezed
  towers, so Unity culls it facing away; CityWorld calls it every LateUpdate
  with Camera.main; tools call `CityWorld.RefreshSkyline(cam)`.
- **Checks**: CityBudgetProbe 68 views: every heading +0 (32) or exactly +2
  (36, facing uptown), worst 189 -> 191; tile build p95 140.6 -> 139.8 ms.
  Drive audit (uptown): nothing solid in a lane 0; the same three FAILs as B2
  (rail ends 12, invisible colliders 5 + 28). In play
  (`CityPreview.RunSkylinePlay`, the game's own draw distance, PSX_SKYLINE=0
  for the before): from I-77 3 km north the skyline stands over the road by
  day and as lit windows at night; from 5 km it shows between the roadside
  trees; from the south the line of sight to uptown is through trees inside
  the drawn world.
- Not done: pack towers among the tallest would be left out (none are);
  the far windows twinkle as the camera moves (sub-pixel windows); the swap
  at 398 m is low-poly to full detail at equal colour.

## No sheet ends in the air (2026-10-04): the W Trade St report at Graham St, the open-edge audit, closing faces, short cut walls that hold land

The owner, free roam at night, HUD "West Trade Street", clock 2:04: "I just
hit a barrier between these roads. There is a thin layer of dirt and I can
see under the dirt and the road to the right."

**The audit: GROUND OPEN EDGES** (`Editor/CityGroundEdges.cs`, menu "Audit
City Ground Open Edges", `city_ground_edges.txt`). It stands the tiles up
(3x3 at a time, the kerbs and creek banks given probe colliders so a drawn
face counts). Every sheet edge that no other triangle shares (welded to
2 mm) is walked a metre at a time. A ray goes down 10 cm past the edge. If
the first surface is more than 0.15 m lower and a ray across from outside
meets no face turned toward it, the sheet ENDS IN THE AIR: from below the
driver sees under it, and its collider edge stops the car. Switches:
- `PSX_GEDGE_BOX=x0,z0,x1,z1` (default the OwnerBox), `PSX_GEDGE_ROUTES=1`
  (the routes' tiles; edges within 30 m of a route edge counted apart);
- `PSX_GEDGE_MESHES=Ground,Roads` (the Roads mesh too);
- `PSX_GEDGE_COVERED=1` keeps edges under another surface (a verge tucked
  under its road's edge, normal), tagged;
- `PSX_GEDGE_LAND=1` adds land standing over a Roads triangle (noisy: a
  ribbon's tucked edge reads as land over it);
- `PSX_GEDGE_SHOW=n` places listed.
Each place names the strip that laid the sheet (`CityMeshes.groundLog`) and
the short barrier runs the side-flag pass dropped (`CityMeshes.shortDropLog`,
probe only).

**What it found (BEFORE, main a3b5feab; Ground and Roads meshes).**
- OwnerBox: 1,441 open metres in 390 places (804 m with a 0.3-1.5 m drop:
  a bumper meets the edge). Race routes' tiles: 1,302 m in 320 places.
- By what laid the sheet (OwnerBox / routes, metres):
  - verge ENDS (a verge's cross-section where the next span lays no verge:
    deck abutments, squeezed spans, junction trims): 998 / 1,027;
  - road surface edges: 224 / 167 (deck corners past their approach, fans'
    envelope stretches);
  - corner fills 88 / 34, the lattice 49 / 24, verges' far edges 36 / 23,
    fan chord verges 25 / 19, seams and half strips 21 / 6.
- On West Trade Street uptown (the paved cells within 1 km of Trade &
  Tryon) there is one place: **the median nose at Graham Street**
  (-2648,5096). This is the owner's frame. The minimap shows W Trade x
  Graham with the carriageways' 160 m oval (e5785/e5932) beside it.
  - The Graham fan's ring (`DebugFanRing`, nodes 7662/7498, 41 corners) runs
    from e5932's widened median corner (-2647.86,5095.78) to e5784's
    (-2658.93,5107.62). That stretch is flagged `mouthNext`: the envelope of
    two overlapping mouths.
  - So no kerb and no chord verge were laid along it. The fan's paving
    ended 0.53 m over a lattice the fan floor had sunk under it (0.42-0.53 m
    under both surfaces), a slot 0.45-2 m wide before e5932's median verge.
  - That is 12 m of open road edge and 2 m of open verge: the "thin layer of
    dirt" and "the road to the right", both seen from below, and an edge at
    bumper height.
  - It predates every recent change. With PSX_CITY_LOTROADCUT=0,
    HOUSEPADS=0, DRIVEWAYS=0, KEEP_SHORT=1 and BULBNECK=0 all switched off,
    it is the same 14 m.

**The fix (the owner's decision: close the whole class).**
- **The Graham nose: a fan's envelope that faces open ground is a free edge**
  (`EnvelopeOverGround`, in the fan perimeter loop).
  - It applies to a grounded fan's `mouthNext` stretch between two
    DIFFERENT arms' corners with no pavement beyond it at its height
    (`PavedOnward`) and no road under it.
  - Such a stretch gets its kerb, its chord verge and its corner fills like
    any free chord. The verge covers the slot.
  - An arm's own mouth, an envelope over another road's pavement, and any
    stretch on structure stay as before.
  - `PSX_CITY_ENVELOPE_VERGE=0` restores the old behaviour.
- **No sheet ends in the air: the closing pass** (`CityMeshes.Skirts.cs`,
  `CloseOpenGround`). It runs once per tile build, after every sheet is laid
  and before the lots' islands. `PSX_CITY_SKIRTS=0` turns it off (the
  before-count).
  - **Finding open edges.** The sheets laid after the lattice (verges,
    seams, strips, fills; the lattice, lots and driveways lie flush on it)
    are welded by position (2 mm, open-addressing tables). Every edge that
    one triangle uses, off the tile border, is open.
  - **Ground edges.** An open edge that is not a strip's inner edge (under
    its own road) and stands more than `SkirtMinM` (0.10 m) over the lattice
    gets a face straight down to the lattice, tucked 0.10 m under, turned
    away from its sheet. Up to `RetainFaceM` (0.5 m) the face is the sheet's
    own cut edge (grass or paving). Taller faces are a retaining wall in
    concrete.
  - **Road edges.** The roads' sheets (ribbons, fans, decks, their concrete)
    are welded the same way and tested in pieces of 2 m at most. A piece is
    skipped as no step when either:
    - a strip's inner edge runs along it (within 8 cm, from 15 cm under to
      2 cm over); or
    - pavement carries on past it at its height (a lying road triangle
      0.6 m out at its quarter points, within 0.15 m, from a 4 m grid of the
      tile's road triangles).
  - **Deck edges.** A road piece more than `StructureDropM` (2.5 m) over the
    ground, or with a road passing under it, is a deck's edge. It gets a
    fascia a deck deep (`DeckThick`), unless a standing concrete or parapet
    face already hangs below it along that edge. Any other road piece is a
    step in the ground and gets the ground face, starting from a kerb's foot
    where a kerb is drawn.
  - **Never in another road's lanes** (`OverRoad`). Where the tile's drawn
    pavement lies under an open edge, and it is a lower road's designed lane
    (`LineModel.CentreAt`), no face goes down through it. The map's roads
    are read within 3 m of the tile border, where that pavement may be the
    next tile's (North Tryon St under the ramp e2382). The edge gets a
    fascia a deck deep at most, never closer than `CarClearM` (1.6 m) to the
    lane, or nothing under a lower clearance. Over a shoulder, fan or gore
    the face stands as a retaining face would.
  - **Colliders.** A face over a drop (more than `SoftStepM`, 1 m, over the
    land within a metre out) is in the ground or road mesh, so its collider
    is exactly what is drawn. A step that is not a drop is drawn
    render-only with the kerbs (see "Closing faces that are steps", below).
    The pass never welds its own faces (each bucket's counts are taken when
    it starts).
- **Cost** (CityGroundEdges, 685 builds of the OwnerBox and route tiles):
  the pass takes p50 9.6 ms, p95 28.2 ms, max 46.5 ms per tile build. The
  whole build (with the probe's ground log on) is p50 102 ms, p95 309 ms.
  First cuts of the pass cost 63-230 ms (Dictionary welding, the outlines'
  PavedOnward per edge). CityBudgetProbe, back to back with the pass off and
  on, read the "meshes" phase +5 to +12 ms a tile on average. Tile build p95
  moved by less than this machine's run-to-run noise (+/-30% within the
  hour, phases this pass does not touch included).

**BEFORE -> AFTER** (CityGroundEdges, `PSX_GEDGE_MESHES=Ground,Roads`,
`PSX_GEDGE_ROUTES=1`; drop past 0.15 m, no face closing it, not under another
surface)

| | BEFORE | AFTER |
|---|---|---|
| open metres, OwnerBox | 1,441 (390 places; 804 m at 0.3-1.5 m) | **120** (59 places; 50 m at 0.3-1.5 m) |
| open metres, race routes' tiles | 1,302 (320 places; 807 m at 0.3-1.5 m) | **104** (53 places; 46 m at 0.3-1.5 m) |
| the Graham St nose (W Trade St) | 14 m (12 road edge + 2 verge), 0.53 m | **0** |
| verge ends, OwnerBox / routes | 998 / 1,027 m | 59 / 59 m |
| road edges, OwnerBox / routes | 224 / 167 m | 50 / 35 m |
| the lattice, OwnerBox / routes | 49 / 24 m | 8 / 9 m |
| closing faces laid (685 builds) | - | 198,815 pieces, 212 km (most under their own surfaces); fascias 27.7 km |

**The residue (120 m OwnerBox / 104 m routes), explained.**
- **Verge ends over a lower road's lanes**, 59 / 59 m. Examples: the ramps
  e371, e2422, e9809, e2037 by I-277; I-277 e4596, e2413, e2315; I-77 e1255,
  e1877; E 11th St e6020.
  - The upper verge overhangs a road below. A face down to the ground would
    stand in that road's lanes, so by the rule above there is none. Where
    the clearance allows it there is a fascia; otherwise nothing.
  - This is a conflict between the two roads' geometry (the abutment
    belongs beside the lower road's clear zone), not something a face can
    close.
- **Deck corners and ramp ends**, 50 / 35 m, 1-7 m drops: I-277 e2349,
  e2321, e2305, e2315; ramps e2359, e7753, e439. A 2 m piece reads as paved
  past, verged, or over a lane where only part of it is.
- **The lattice's own edges under decks**, 8 / 9 m at 0.15-1.6 m: e1916,
  I-277 e8474, e2353 and e2139, S Tryon e1108. Plus 2 m of half strip.
- None is on West Trade Street.
- Next, if the owner wants them: pull those verges back to the lower road's
  clear zone (an abutment, not an overhang), and test deck ends in 0.5 m
  pieces.

**Short cut walls that hold land** (the hotfix's suspected part). The
hotfix (2026-10-03) drops every cut wall run under `MinMedianRunM` closed
at both ends. A cut wall that retains land is not a stray block: dropped,
the land behind would end in an open edge over the verge. The rule now
(`CityMeshes.CutRunHolds`, `CutHoldM` 0.3 m): a short cut wall run is kept
when the graded ground (`CityElevation.GroundY`, global data) 1-3 m behind
its line stands more than 0.3 m over the road's edge. Kept runs carry
`cutWhy = CutWhyHolds` (4), and the short barrier census does not count
them (it reports them). `PSX_CITY_CUTHOLD=0` drops them as before.
- Measured: the four short cut walls the hotfix drops on the OwnerBox and
  route tiles (I-277 e1483 s 10-31; I-77 e2132 s 89-109 and e6608 s 198-222
  under W 5th St; I-77 e2739 s 129-159) hold 0.00 m. The ground behind them
  is graded to the road, so they were free-standing stubs, and the drop
  left no open edge. The rule keeps 0 of them. The open-edge census is
  identical before and after (OwnerBox 1,217 m / 353, routes 1,135 m / 286).
  The hotfix is NOT the cause of the W Trade report.
- City-wide (the short barrier census, 1,540 tiles): 0 isolated pieces,
  0 short cut walls kept for holding land. The rule is a guard; it changes
  no geometry today.
- `city-cycle -DriveOnly`, with the closing pass: every route probe reads 0
  (walls, steps, holes, off, grass, BLUNT), and WALLS IN LANES reads 0. The
  known failures remain: box BLUNT rail ends 12 and SOLIDS 81. INVISIBLE
  lanes is 51 against main's 50; the extra one is a Roads sweep on E 11th
  St e10610 (box, not a route), 0.23 m over a lane, its drawn face turned
  away.
- Earlier variants of the pass were rejected by this audit:
  - faces hung down through lower roads' lanes (N Tryon St under e2382);
  - fascias stacked under soffits.
  The lane rule (`OverRoad`) is the fix for those.
- `city-cycle -AuditOnly` was NOT run on the final code (HARD STOP). The
  last one, on the cut-wall commit, had the known 18. When it was run, the
  closing faces put the body-box check at 142: see below.
- Named views (`CityPreview`):
  - group `tradedirt_photo`: `tradedirt_nose_low` and `tradedirt_verge_i277`,
    0.7 m eyes (`rise`, `lookAt`), the BEFORE/AFTER photographs;
  - group `fascia_check`: two deck edges given fascias;
  - group `tradedirt`: the Graham nose and the candidates ruled out;
  - group `cutkeep`: I-77 under W 5th St.

### Closing faces that are steps: render-only (2026-10-04, the body-box regression)

The first `city-cycle -AuditOnly` on the closing pass put the roadside check
"no face stops the body box coming back onto a grounded edge" at **142**
(main: 1). The check casts a ray `CarClearanceFloorM` (8 cm) + 1 cm over
the land a metre out from a grounded edge, back toward it, for a metre. It
fails on any vertical face in the Ground or Roads colliders (the Solid
layer is exempt, as a designed wall), however tall. The 142 were the new
closing faces, most standing about 9 cm over the verge a metre out: a
verge's open end, or a road edge piece the verge test missed, closed down
to a lattice that lay deeper than the verge beside it.

**The rule** (`CityMeshes.Skirts.cs`, `LandOut`, `SoftStepM`):
- For each closing face piece, the LAND is the highest surface within a
  metre out from its middle: samples at 0.25, 0.5 and 1 m, each the highest
  of the lattice, the tile's road triangles and its ground sheets past the
  lattice (two 4 m triangle grids, `TriGrid`: the roads one the pass
  already had, and a new one for the verges, seams, strips and fills), up
  to 0.3 m over the face's top.
- A face whose top stands no more than `SoftStepM` = `RoadsideRules.OpenDropM`
  (1 m) over that land is a step, not a drop. It is drawn RENDER-ONLY with
  the kerbs (the Kerbs mesh, concrete), as the kerb's own inch under every
  grounded edge already is, for the same reason. The sheet's own edge
  stands inside every car's body there, so the body meets that edge where
  it would have met the face, and no car slips under it. The collider is
  main's; only the view is closed.
- Past 1 m the face is over a drop and stays in the ground or road mesh
  (concrete retaining face), so it collides: a car could pass under the
  edge it closes.
- Past the tile's border the next tile's sheets are not built, so the land
  there is unknown, and the face is taken as a step.
- Deck and lane fascias are unchanged.
- No draw call is added: the Kerbs mesh is already one draw on every tile
  with a street. Every face that was dirt or paving under 0.5 m is now
  concrete, like a kerb or an edging.
- `CityGroundEdges` gives the Kerbs mesh a probe collider, so a
  render-only face still counts as closing its edge.

**Measured.**
- `city-cycle -AuditOnly`: body box **142 -> 1**. The one left is main's
  own (E Independence Expwy e20511, (1944.8,2511.5)). There are 18
  FAILURES, the same names as before; the unguarded-ledge check is still
  clear.
  - The first cut (a step of up to `LedgeStepM` 0.3 m, land 1 m out only)
    left 4. Two were faces 0.4-0.6 m out from the road edge, whose land
    lay nearer than a metre. One was at a tile border (e2858), with the
    land on the next tile. One was a raised sheet's end standing 0.44 m
    over the road beside it (e343). A probe of the pass's decisions
    (`CityMeshes.skirtLog`, probe only) showed which.
- `city-cycle -DriveOnly`: every route probe is 0 and WALLS IN LANES is 0.
  INVISIBLE lanes is 5 (6 with every closing face in the collider), solids
  41 and box BLUNT rail ends 12, both as before.
- `CityGroundEdges` (`PSX_GEDGE_MESHES=Ground,Roads`, `PSX_GEDGE_ROUTES=1`):
  open metres are **109** in the OwnerBox (was 120) and **90** on the
  routes (was 104).
  - Of 211,673 m of closing faces on 685 tile builds, 195,209 m are
    render-only steps.
  - The pass costs p50 11.1, p95 30.9 and max 47.6 ms a tile build (was
    9.6 / 28.3 / 50.4 ms). The ground grid accounts for the extra.
- Photographs: `CityPreview.RunEyePlay` (new). These are named Eye views
  through the GAME's camera in Charlotte.unity: its sky, hour, grade and
  tile ring, a 1.2 m eye, no HUD, noon and night. `PSX_EYE_VIEWS`,
  `PSX_EYE_HOURS`. The views are `tradedirt_graham_wb`, `tradedirt_nose_nw`
  and the new `groundedges_i277_eye` (the I-277 verge end by S College St
  from the driver's eye).
## Uptown landmark heroes (Uptown C, 2026-10-05): five shapes of their own

- **What** (`CityMeshes.Landmarks.cs`). Five landmarks are drawn as their own
  shapes, procedural low-poly, by eye from reference photographs - shapes
  only: no logo, name, sign, slogan, team mark or screen content anywhere
  (owner rule 2026-10-04), neutral names in code and data. The table
  (`Heroes`) is keyed by the OSM element the exporter's landmark table
  (`tools/city/lib/parts.mjs`) keys the same building by; PBLD v3's landmark
  byte is that table's index + 1. `on` false leaves one to B2/B3 (switched
  off, still listed); `PSX_HEROES=0` (or a list of landmark bytes) is the
  editor's A/B switch.
  - **C1 spired crown tower** (landmark 1): OSM's stepped tiers stay; a ring
    of upright metal spikes on the edge of every tier above 140 m (taller up
    the crown) and a needle on the top tier to 265 m, the building's real
    height (OSM's needle part ran to 300 m with a 7 m core column under it:
    both left out).
  - **C2 open-frame tower** (2): the shaft and its two sloped glass parts
    stay; OSM's slabs standing in for the crown (180-240 m) are replaced by
    an open steel frame over their box: corner and mid-face columns, ring
    beams in three bays, an X of bracing across every face of every bay.
  - **C3 silver crown tower** (3; its 61 parts carry no heights - a hand
    table): a seven-storey stone podium over the lot, a granite-grey stone
    shaft set in 1.5 m, two 2.5 m setbacks at 76% and 84% of the height, and
    a silver lantern that flares back out over the last setback, ringed by
    upright fins that stand 3-12 m proud of the roof, stepping up to the
    middle of each face.
  - **C4 pyramid-top tower** (5): a warm stone shaft to 30 m under the top,
    a metal cornice, a steep copper-green pyramid and a slim four-legged
    lattice spire, tied twice, to the outline's height. Landmarks never
    take a pack tower.
  - **C5 the stadium** (14): OSM's 38 parts drew solid tiers to 43 m. The
    bowl part's outline is now the OUTER WALL (precast, one panel a wall);
    inside it the stands step from the rim to an open turf field along the
    bowl's long axis - a concrete rim, a steep blue upper deck, a dark fascia
    band, a step, a blue lower deck and the field wall - as spokes from the
    wall's own corners to a rounded field edge, the field on the highest
    ground under it. The two board parts are BLANK dark boards on the rim.
    The corner towers and the outer ring's parts stay OSM's.
- **No new material or draw.** Every hero surface is the facade atlas
  (`Slot.FacadeGlass`; metal by the vertex alpha's metal bit, B1b) or the
  flat roofs. Solid members sample ONE atlas texel (column 0's white frame
  for metal, column 3's plain precast for matte; both 0 in the night mask, so
  never a lit window), tinted per hero. The far skyline draws the same
  `EmitFootprint`, so the crowns stand on the horizon too, simplified (fewer
  fins, no bracing): 3,102 -> 3,602 triangles, still 2 draws.
- **Drawn, not collided.** A hero's crown (everything above 30 m) and the
  stadium's stands, field and boards go to `Bucket.tn` while
  `Bucket.NoCollide` is set: `MeshFrom` draws them after the collided
  triangles, and `ColliderFrom` builds the tile's buildings collider from the
  rest (`TileMeshes.buildingCollider`, cooked by `CityWorld` instead of the
  drawn mesh; null - the drawn mesh collides, as before - on every tile
  without a hero). The stadium collides on its outer wall only.
- **Checks**: triangles per building as the tiles draw it (CityBudgetProbe
  prints `[Landmarks]`): spired crown 1,926 (base + crown 658), open frame
  1,675 (shaft + frame 1,193), silver crown 1,776, pyramid top 556, the
  stadium 3,119 with OSM's outer-ring parts (the bowl 560). Budget probe,
  heroes off -> on: the uptown sites' views +0 draws; 7 headings at three
  sites well outside uptown +2 (the far skyline's two draws now in view
  there); worst view 191 -> 191; tile build p95
  138.2 -> 130.5-134.5 ms, mesh build p95 128.0 -> 125.7-128.6. Drive audit
  (uptown): nothing solid in a lane 0; the same three FAILs as B2-B4 (rail
  ends 12, invisible colliders 5 + 28). Preview: `CityPreview.RunUptownRef`
  has a close view of each hero (`close_*`).
## West Trade Street road fixes (2026-10-05): poles off the fans, ramps end at the road, lane blips

The owner on West Trade Street: "I keep finding traffic light posts in the
middle of intersections. Roads should not overlap or clip. Adding a lane is
different than forcing a lane to move over and back twice at 45 degrees
within a few feet."

- POLES. `CitySignals.FootOk` tested a signal or STOP foot against the road
  ribbons only, never the junction fan (curb returns, cluster rings), so
  `FarCorner` often stood the pole out on the fan. A foot now also keeps its
  offset (1.0 m signals, 0.6 m STOP) clear of every fan ring
  (`CityMeshes.FurnitureOffFans` / `JunctionPavementDistance`, rings built
  from the graph and trims alone). `CityRefSpots.RunPoleCensus`, city-wide:
  signal poles on the drivable surface 757 -> 0 (all on fans), within 0.6 m
  133 -> 0; STOP posts 0 -> 0. Cost: 50 fewer poles, approaches with no clear
  spot 390 -> 418. PSX_CITY_POLEFANS=0 is the before.
- MOUTHS. At node 505 (West Trade x the I-77 ramps) the off-ramp e280 was
  shallow to both the on-ramp e281 and West Trade, and took the first pair's
  host, e281 - itself clipped onto West Trade and wholly inside it (merge
  zone). Clipped against a zero-width ramp, e280 drew its full width over
  West Trade's lanes for 25 m. (Heights agree to 5 cm: AttachDy was not it.)
  `ComputeTrims` now moves a branch whose host is a LINK clipped onto a third
  arm, shallow to it too, onto that third arm. A road of its own between the
  two keeps its branch (South Kings Drive between Henley Place and Morehead:
  the first try, "greatest candidate", put Henley over Kings). The crossing
  test and fan trims read the first host (`clipFirst`), so no junction
  changes kind or trim. Six branches move city-wide (`CityMeshes.HostMoves`).
  `CityPreview.RunMouthCensus` (lesser surface inside a greater one at the
  same node): West Trade box 107 -> 5 m2 (left: e9612 over e9611, both West
  Trade, node 10935); around all 24 candidate nodes 871 -> 379 m2 (left: the
  Kings/Morehead tangle and a ramp-on-ramp triangle at node 3844).
  PSX_CITY_MOUTHCLIP=0 is the before.
- BLIPS (OFF). lineclean's BLIP RULE gives a street piece with no room for its
  FX4/MUTCD tapers its neighbours' layout (West Trade's 38 m 3+3 piece between
  3+2 ones: blips 2,060 -> 0, 1,626 fixed). Its re-export failed the drive
  audit: West 5th Street e8318's line offset flipped (-3462,6052) and land
  stands 0.82 m over its lane; a widened North Tryon piece left a 0.56 m
  ledge. The rule stayed in the code, OFF (a gated version, still OFF: the next section).
- Tools: `CityPreview.RunRoadBattery` (PSX_ROAD_STEPS poles,mouth,spots),
  `RunRoadSpots` (PSX_ROAD_SPOTS driver-eye frames, PSX_ROAD_TAG),
  `RunMouthCensus` (PSX_MOUTH_BOX, several boxes with ';'; PSX_PROBE_NODES).

## Lane blips gated, street crossings (2026-10-05, roads-b; both OFF until the gates run)

- WHY THE FULL BLIP RULE FAILED. Not a stale height or corridor: Ground,
  the verges and the fans all read the line model. A fixed piece changed
  more than its own lanes. (1) TAPR's ribbon offsets run down a chain from
  its head, so taking out a piece whose two eases did not cancel moved every
  run after it - 1,232 pieces (167 km) of unchanged width moved sideways,
  among them West 5th Street's one-way e8317-e8320, 3.66 m across its line
  into its cluster's patch (the 0.85 m step and the land over its lane).
  (2) A NARROWER piece taken up to its neighbours' count widened the
  pavement: North Tryon's 3 lanes between 4s became 4, and its 2-lane drop
  one 148 m taper beside a lower road (the 0.56 m ledge).
- THE GATE (`lineclean.mjs`, PSX_LC_BLIPS=1; unset is off): a dry TAPR pass with the
  blips in place; only a WIDER blip that the ribbon leaves at the offset it
  entered is taken out; then the real pass, and a chain where a piece that
  is not a blip still moved (a run turned into a bay and locked its sides
  the other way) gets its blips back. Blips with no room 2,060 -> 396; 1,239
  taken out (73 km); kept: narrower 171, ribbon shift 109, chain restored 116
  (16 chains). City-wide 1,462 pieces narrowed, none widened, 1 piece of
  unchanged width moved (Selwyn Avenue, was 1,232). =all is the full
  rule. Its export: graph 1dc91316 -> 700a8c2b; linecheck most counts fall a
  quarter (transitions gone), B3 CURVE +7 runs, C1 GAP +1 run. NOT SHIPPED:
  the data stays the rule-off export; the city/drive audits never ran on it.
- STREET CROSSINGS (`CityMeshes.StreetCrossOn`, PSX_CITY_STREETCROSS=1 on,
  off by default until the audits run). The crossing rule (leftover item 3) took only links; South Kings
  Drive and East Morehead Street, two divided roads crossing at about 30
  degrees, met at four mitred nodes where each Kings half hugged a Morehead
  carriageway and was drawn over by it. Streets crossing at a skew are now a
  crossing too: nodes 7045/7046/7053 are one junction cluster. Mouth census
  over the 24 candidate boxes 502 -> 195 m2 (Kings/Morehead 283 -> 38, all
  of it the Morehead split at node 11891; Pleasant Road x Gold Hill 46 -> 2).
  Left: the ramp-on-ramp triangle at node 3844 (95 m2, two branches clipped
  onto one host overlap each other) and West Trade node 10935 (5 m2).

## Not in v1 (in order of likely next)

Traffic, gas stations / parking lots / mechanic shops in the city,
neighbourhoods beyond the 8 km core (each 8 x 8 km box is one Overpass
fetch), city race tracks drawn on the graph by the player, lane-level turn
markings at junctions, a skyline backdrop past the fog, a
one-sided (MUTCD) lane taper on one-way carriageways, the ROVAL.

Street lamps are no longer on this list: they stand on the verges since the
night pass (2026-09-21, `CityMeshes.PlaceLamps`, checked by `LampAudit`).
Everything else here, and much more, is scheduled by the refinement plan
(2026-09-28: hills, trees, smooth lines, lanes and paint, roadside detail,
junction control, city traffic, street races).

## Leftovers A (2026-10-05): lots clear of the parts, floodlit crowns and raised sheet ends (the last two built, OFF)

- **Lots over uptown buildings.** `export_osm.mjs` built the parking lots
  before `applyParts`, so lots were cut by the outlines only and paved under
  B2's building:parts and the layer > 0 outlines they rescue (LOT AUDIT 252.8
  m2 in 9 lots). A dry run of `applyParts` on copies now gives the lots every
  footprint the tiles draw. Re-exported: only `charlotte_city.bytes` moved
  (dem, bld, routes and the graph hash 1dc91316 the same). The linecheck
  baseline was already STALE on the builder replica (B1/B2's `citydata.mjs`);
  re-recorded with `--write-baseline`: every check before = after.
- **Floodlit crowns** (`CityMeshes.FacadeCrown`, vertex alpha +8): a tower of
  `CrownMinH` 60 m and more is lit from `facCrownY` (a tenth of its height
  under the top, 8-20 m) up - walls crossing it split there, roof shapes,
  heroes' metal but the stadium's boards; the skyline comes through the same
  emit. The PSX/Lit half (the +8 decode, the CROWN_* night pass: the texel x
  warm or cool flood x 0.55 x night squared, fogged like a window) is not on
  the branch; until it is and is seen at night, `PSX_CITY_CROWNS=1` only.
- **Raised sheet ends** (`CityMeshes.SlopeRun`). A sheet standing
  LedgeStepM..OpenDropM (0.3-1 m) over the land a metre out took a
  render-only face; in the collider its own edge was the ledge (W 4th St Ext
  e14607 0.34 m, a verge 0.6 m past the edge; e343's squeezed span end 0.44
  m). It now runs on down at CityFillSlope 1:4 until it meets the land (or
  pavement flush with it) and tucks ToeTuckM under, in the ground's material
  and collider; kept as before where a road lies under the slope (two roads a
  level apart: e343 beside Tyvola Road) or no land is met within 4.4 m.
  `CityGroundEdges` counts them (RAISED SHEET ENDS; PSX_GEDGE_EDGES names
  edges). On the two spots' tiles: 288 pieces / 425 m -> 46 / 61 m, both
  named ends gone. Not gated yet: `PSX_CITY_FORESLOPES=1` only.

## Lit city streets at night (city-night, 2026-10-05)

The owner: "The lighting in Charlotte feels off. The roads and cars are too dark at night on the main roads", with five uptown night photographs (N Tryon at 6th and at 4th, an uptown two-lane, a wide arterial into uptown, a one-way four-lane), then five more of the towers and the aerial grid. Only colours and light patterns are taken from them, never a name, logo or sign.

Measured with `colour_stats.py citynight` (boxes in the picture, since the photographs carry no sidecar): the photographs' asphalt reads Ycode 90-141, lane paint 166-218, car bodies 36-111 and the frame median 51-110, with 0-30% of the frame under 20. Their tower faces read 12-45 under a 5-37 sky, lit windows 88-153 on 6-32% of a face, and whole lit floors show as rows. Ours read road 4-25, paint 64-89, cars 14-20, median 5-24 and faces 4-7. **The cause:** the 12-slot lamp table lit only the 8 lamps nearest the eye, while 8-13 stand within 80 m and 37-53 within 160 m of every uptown spot. An 18 m pool under a 9 m head also lights barely 10 m of road, so a lit street was a few pools with black gaps between them, under the owner's dark-night ambient.

**The lamp field** (`StreetLights.Field.cs`, `PSXLamps.cginc` `PSXLampField`). The Charlotte tiles mark their lamp heads (`NightGlow.cityField` -> `StreetLights.MarkField`). Every marked lamp within 250 m of the eye (180-240 of them uptown) is splatted top-down into one 256x256 RGBAHalf texture that follows the eye in 32 m steps and is rebuilt only when it steps or a lamp changes:
- R holds the pool, a 28 m windowed disc at 1.8x the lamp's intensity;
- G holds a 40 m scattered glow at 0.30x, which reaches car sides and the lower storeys;
- B and A hold the glow-weighted head height, so a deck above the heads or a road 20 m under them takes none.

Every PSX surface reads the field with one fetch inside `PSXLampsCore`. While the field is on, the table's street entries give only their wet glints, and tail lamps are unchanged. `PSX/Lit` reads the lamp light on a dark road through the beam's albedo floor (`PSXBeamAlbedoGain`), so the owner's asphalt colour is unchanged and is seen the way a camera sees lit asphalt. Uptown heads are white LED (sRGB 1, .95, .86) within 700-1300 m of the Square, and the neighbourhoods' heads are warm bulbs (1, .84, .52). The field adds no draw call and no renderer; its cost is 512 KB of texture and a CPU splat when the window steps.

**Unlit roads keep the dark night.** Only the city marks lamps. The Town, Blue Ridge, the sprints and every other venue never turn the field on (`_PSXLampFieldST.w` is pushed 0 on every frame), so their pictures do not change. `colour-shots.ps1 -Sets townnight` shoots the Town spot with the field off and on: the regions are identical (unlit road darkest 7.9, frame median 14, 79% under 20, sky 9.8, night gate 0 misses), and only 0.03% of pixels differ, which is the stars. `PSX_LAMPFIELD=0` (editor) turns the field off, and the picture is then exactly the code before this pass: N Tryon's field-off frame matches the pre-pass frame number for number.

**Towers in the lit city** (`PSX/Lit`, only while the field is on):
- the lit streets' bounce on a facade (`_PSXCityBounce`, 0.075/0.069/0.063 linear);
- random lit windows at 20%, plus whole lit storeys as bands (16% of a building's storeys, counted from the street level the field knows);
- the window glow at x0.50;
- a warm uplight wash on the lower facade of 35% of buildings;
- LED lines on the podium slabs of 10% of them (green or the building's accent);
- crowns washed in a hash-picked accent (white, blue, violet, teal, pink, green) at 0.85, with LED lines on their storeys.

After the pass, at the same spots (refs in brackets):

| spot | road | paint | car | frame median |
| --- | --- | --- | --- | --- |
| N Tryon at 6th | 4 -> 51 | 64 -> 191 | 14 -> 50 | 5 -> 48 |
| S Tryon at 4th | 14 -> 72 | - | 14 -> 26 | 14 -> 41 |
| W 7th St | 25 -> 97 | - | 19 -> 69 | 24 -> 87 |
| E 3rd St (one-way) | 19 -> 105 | - | 20 -> 65 | 19 -> 92 |
| E Trade St | 23 -> 88 | 89 -> 203 | 14 -> 44 | 18 -> 60 |
| refs | 90-141 | 166-218 | 36-111 | 51-110 |

Towers, over every frame with a tower box: faces 4-7 -> 7-40 (refs 12-45), lit windows 164-193 -> 113-137 (88-153), lit share 7-34% -> 11-36% (6-32%), row banding 0.11-0.43 -> 0.24-0.59 (0.12-0.67). Composites: `scratchpad\ba\night_1..5.jpg`, `night_bld_1..3.jpg` and `night_town_unlit.jpg` (REF | BEFORE | AFTER).

**Open.** The road 2-8 m ahead at the two Tryon junction spots (51, 72) is still under the refs' 90. Lamps stand back from a junction's fan (`LampEndClear`), so the mouth of a big junction is the gap; the next step is a lamp on the fan's corner, not more gain. The skyline from 2 km shows warm lit streets only within the 250 m window. The far skyline's crowns did not take the accent palette (the crown box read warm), probably because the far skyline is not the facade shader; check it before any retune.

Instruments: `CityPreview.RunEyePlay` with `PSX_EYE_JCT="name:roadA:roadB:back:pick:pitch[:rise[:sq]]"` (the driver's eye before the node two named roads share; pick -1 heads toward the Square), `PSX_EYE_CAR=1` (the player's car parked 14 m ahead), and `PSX_EYE_AB=1` (each frame with the field off as `_off`, then on).

**Correction round (2026-10-05).** Lit glass glittered: the building key was `frac(dot(tint, ...) x 43.758)` on the interpolated half tint, and a one-ulp wobble gave every pixel its own key. The key now comes from the tint rounded to its 8 bits. Lit units, storeys, LED and podium lines and uplights follow the atlas's own floors (`uv.y` x FacadeLooks' floors), not world height. A curtain wall's panes get a box-filtered spandrel and mullions. Anything under two pixels shows its mean, so the far-mip checkerboard is gone too. Under the lamps a low-chroma upward texel (asphalt, concrete, pavement) is read at its luminance with 15% of its tint. Road sat at the spots went from 0.22-0.52 to 0.18-0.41 (refs 0.06-0.21). The LED head is (1, 0.98, 0.94) and the bulb (1, 0.90, 0.72).
