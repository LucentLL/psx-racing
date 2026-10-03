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
