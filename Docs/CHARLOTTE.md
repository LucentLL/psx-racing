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
- **The ground**: a 60 m grid (811 x 916) over the whole beltway, each node
  the MEAN of the 3DEP 1/3" pixels whose centres lie in its own 60 m cell,
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
