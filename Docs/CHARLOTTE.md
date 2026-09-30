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
make it. Nothing here changes the ground function or a road's height: the
embankment the road already stands on is the culvert's.

**Headwalls** (`CityMeshes.Hydro.cs`). At each end: a concrete headwall
(the city concrete, the barriers mesh on the Solid layer) faced square off the
nearest road, the pipe's mouth in it (an octagon of 0.9, 1.2 or 1.5 m by the
fill over it, its inside the lamp posts' dark pack metal: a corrugated pipe,
and the dark a mouth needs, render-only), wing walls back along both sides and
grass backfill between them. The backfill is flat at the coping until the fill
behind rises through it; if that has not happened 6 m back it is a berm
falling at 1V:2H. No new material and no new slot: a tile that already has
barriers and lamps draws nothing more. The occupancy mask (static half and the
tile's own) marks each end, so no tree or sign post stands in a headwall.

**Creek banks.** A band of the owner's pack clay (PSX Textures II
`dirt_pt_7`, the red-brown of Piedmont clay, copied as
`Art/City/Pack/dirt_pt_7_city.png`, tinted to the grass's brightness) runs
along both banks of every creek, from a metre under the flat floor's edge (the
lattice meets the water between its vertices, so the edge wanders) to 4.5 m
past it, draped exactly on the lattice: each quad is cut against every
lattice triangle it covers and each piece set on that triangle's plane, 6 cm
proud (corners merely set on the lattice bridged its folds, and the grass came
through in patches). Never within 3 m of a grounded road's pavement.
Render-only, the city kit's new `bank` material (`CityKit.bank`, no Slot
change), one draw on a tile with a creek.

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
| Headwalls | 767 (243 culverts with both ends); pipes 0.9 m x241, 1.2 m x120, 1.5 m x406 |
| Ends not stood | never clear of a road 148, no channel on the lattice 202, shallow at the toe 132, shared 121, a creek 24, a building 9, a lake 9 |
| Nearest headwall to a pavement | 4.7 m, every one past its road's clear zone |
| Every culvert keeps its embankment | 0 of 706 fail: the road on the ground over every pipe, the ground under its line within 1.1 m of it (deepest 1.01 m, the corridor sink and sag) |
| Ends drawn (26 sampled tiles) | 55 of 55, each once, in the tile it stands in; 0 walls facing the wrong way |
| Water shown along the creeks (every 8 m) | 689 of 710 km, 97.1% (Irwin 71%, Stewart 95%, Little Sugar 92%, Briar 96%, McAlpine 99%, Sugar 96%) |
| Ponds | 1,233 water bodies under 2 ha as drawn (739 ha), water shown at the middle of 1,203; none within 3.5 m of a road, none over a grounded road within 16 m |
| Creek beds (7 transects) | 7 of 7 within 1 m of 3DEP, as R1 |
| Decks over water | 560 spans, none with water over the soffit |

Checks: CITY AUDIT OK (DRIVE AUDIT 0/0/0/0/0, roadside audit green, the tree
and sign audits green, the hydro audit's four new checks);
`city-play-check -Edition CITY` CITY SPAWNS OK; TEXDECODE AUDIT OK (933
textures in the 16-bit set, the clay among them); `export_osm.mjs --check`
byte-identical; SIZE LEDGER OK.

**Budget** (`CityBudgetProbe`, the A/B is now WP-25: the same tiles without
culverts and banks, `CityMeshes.HydroOff`): the worst view 209 draws both
ways (trade_tryon); at most +4 draws in one view (i77_north 65 -> 69; the
extra site W Trade St at Irwin Creek +4 on 180-227); tile build p95 70.5 ms
without, 73.4 with, on the same 225 tiles (+4%; a first run read 72.3 / 72.5);
parse 178 ms + solve 1119 ms, map heap 19.6 MB. Data: `charlotte_city.bytes` +156 KB raw, +95 KB Brotli (WATR
+141 KB raw for the ponds' points); the clay texture 256 x 256 in the 16-bit
set.

**Shots:** `tools\city-hydro-shots.ps1 -Label before|after`
(`CityHydroShots`): per creek (W Trade St over Irwin Creek, State St over
Stewart Creek, Archdale Dr over Little Sugar Creek) from the bridge's
downstream edge, 2.5 m over the water 40 m downstream and 35 m up 120 m
downstream; a culvert (McDonald Avenue) from its channel, three-quarter and
from the road, and four more three-quarter (South McDowell Street, Baxter
Street twice, Hartford Avenue); a 0.56 ha pond off Tyvola Road from 25 m up.
The cameras depend only on the data and the ground; "before" runs with the
tiles' WP-25 work off and HEAD's data.

**Not done:**

- Irwin Creek shows water on 71% of its length: north of uptown it runs 14 m
  off I-77, whose fill holds the ground over the channel (R1's note; WP-14's
  grading keeps it). Near some bridges the approach fills' 8 m lattice
  triangles lap over the channel for 20-40 m; the sheet is there, under grass.
  Carving a creek through a fill beside a road is a grading change (WP-24's
  or a later WP-14 pass), not this one.
- The ravines carry no water: their channels are narrower than the lattice
  can hold a sheet in.
- 182 of the 706 culverts have no drawn end (the ravine never clears a road
  within 60 m, or the lattice shows no channel): the road simply crosses the
  swale there.

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
  day; only the cars are 1999. The toll and express lanes come back in the
  refinement's lines phase (WP-10/11), with the parallel carriageways drawn
  properly instead of squeezed. Until then the drop stands. Six I-77 ways
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

## Not in v1 (in order of likely next)

Traffic, gas stations / parking lots / mechanic shops in the city,
neighbourhoods beyond the 8 km core (each 8 x 8 km box is one Overpass
fetch), city race tracks drawn on the graph by the player, lane-level turn
markings at junctions, signal heads, a skyline backdrop past the fog, a
one-sided (MUTCD) lane taper on one-way carriageways, the ROVAL.

Street lamps are no longer on this list: they stand on the verges since the
night pass (2026-09-21, `CityMeshes.PlaceLamps`, checked by `LampAudit`).
Everything else here, and much more, is scheduled by the refinement plan
(2026-09-28: hills, trees, smooth lines, lanes and paint, roadside detail,
junction control, city traffic, street races).
