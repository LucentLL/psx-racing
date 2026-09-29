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
- Four 1" height tiles, N34W081, N34W082, N35W081 and N35W082
  (`tools/roads/cache/*.hgt.gz`, via `tools/roads/lib.mjs`). They come from the
  AWS Terrain Tiles "skadi" set (Tilezen), which in the US is a BARE-EARTH
  mosaic built from USGS NED/3DEP. It is not SRTM radar and has no towers in
  it: a 1 km window round the Bank of America tower reads 215-232 m, and the
  tile agrees with 3DEP lidar to 1.5 m RMSE (survey_flatness, 2026-09-27).
  Tilezen asks for a credit, which was missing until WP-02: "Terrain: AWS
  Terrain Tiles (Mapzen/Tilezen); 3DEP and SRTM data courtesy of the U.S.
  Geological Survey" is now the second credit line.
- The water still comes from RG2's hand-traced creeks and Lake Wylie
  (`baselineWater.ts`), co-registered onto the OSM frame by ICP against RG2's
  own merged I-485 row (23 m residual). Since WP-02 these are **vendored** in
  `tools/city/vendor/rg2/` (the water file byte for byte, the two I-485 rows
  cut out with their source files' sha256 and commits; `vendor/README.md` has
  the provenance), so the export no longer needs an RG2 checkout, and a
  missing Overpass cache is an error rather than a silent fall-back to RG2's
  older snapshot. What RG2's `Maps/Rivers and Lake.png` was drawn over is NOT
  recorded anywhere; that is an open question for the owner (WP-04b replaces
  this water anyway).

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
- **The ground**: a 60 m grid (811 x 916) over the whole beltway, sampled
  from the four skadi tiles, then opened, closed and blurred. The filter was
  written to take out "roofs", on the belief that the source was radar with
  uptown's towers 200 m proud. The source is bare earth, so the filter removes
  real ridges and valleys instead: only 34% of the core's relief survives, and
  city-wide the grid is 3.63 m RMSE from 3DEP (1.53 m before the filter).
  Heights are stored in decimetres above the **datum, pinned at 97.0 m**
  (`DEM_BASE`, WP-02). It used to be `floor(min) - 2`, which is also 97 on
  this grid (the lowest cell, 99.2 m, is the Pineville Quarry pit at
  35.1203, -80.8975) but would have moved with any new ground, and every
  world y with it. The lowest the ground may go is 97.5 m (`DEM_FLOOR`): a
  cell below it is clamped up only inside a named pit box (`DEM_CLAMP`: the
  Pineville and Arrowood quarries, keyed by their OSM ways), and anywhere else
  the export fails, because a u16 below the datum wraps to a 6.5 km spike.
  3DEP puts those pits at 70.5 m and 89.5 m, so they lose up to ~27 m of
  depth when WP-04 ships it; today's grid clamps nothing.
- **31,695 footprints** (styles glass / midrise / brick / house / shops,
  gabled where the polygon is a house's), counter-clockwise, with heights.

Outputs (all in `Assets/PSXRacing/Resources`): `charlotte_city.bytes` (2.5 MB
graph + water + separations + spans + routes; a versioned binary the runtime
reads through a BinaryReader — the JSON equivalent was 9 MB), `charlotte_dem.bytes`
(1.5 MB), `charlotte_bld.bytes` (1.7 MB), `charlotte_routes.json` (the menu's
copy of the routes: lengths, lines, a 20 m polyline each). 5.8 MB raw, about
3.0 MB Brotli. Debug plots land in `tools/city/charlotte_*.png`.

**The container (WP-02, 2026-09-28).** `charlotte_city.bytes` is `PSXC`
version 2: a header and a **section table** ({tag, offset, length}), then the
sections META (attribution, uptown), NODE, NAME, EDGE (the edge records and
their point counts), PNTS (every edge's points, in edge order), WATR, XING,
SPAN, ROUT and **GHSH, the graph hash**. `CityMap.Parse` reads the sections it
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

**Credits (WP-02, critic C17).** `tools/city/SOURCES.md`'s Credits table is
also the source of the pause menu's **CREDITS** page (a row beside the column,
under TOGGLE DEBUG INFO; `CreditsPanel` shows
`Resources/psx_credits.txt`, wrapped to the column) and of **`LICENSES.txt`**,
which the WebGL template carries into every build beside `index.html`.
`node tools/city/credits.mjs --write` regenerates both; without `--write` it
checks them. `tools/bench-preview.ps1` photographs the page at the three
aspects and logs any clipped text.

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

1. Every edge follows the DEM, Gaussian-smoothed over 25 m, grade-limited
   (4% freeways, 5% expressways, 6.5% streets, 8% ramps and local streets).
   A tagged bridge is structure end to end and holds at least the line
   between its ends.
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
   Water spans hold their line. Junction nodes take their highest incident
   end. Iterated to a fixed point, then the interiors relax (never steeper
   than an edge's own ends), then fresh raises alternate with APPROACH CONES:
   every node standing above the ground seeds a 4.5% cone through the graph
   (RaiseCone), on through any junction it still stands above, so a lifted
   bridge's embankment runs back through the blocks behind it instead of
   ending in a 37% ramp on a 36 m approach edge. Nothing that can lower a
   road runs after the relax.
4. Structure = tagged bridge, water span, trench-crossing stations, or more
   than 1.4 m above the DEM.

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
    yet per chain.
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
    (a lower bound, anywhere). A window BETWEEN TWO STRAIGHTS - the line does
    not turn again by a quarter of the window's largest heading for min(4w, 10
    m) on each side, or runs straight on to its end at least w away; a sampled
    curve's next vertex stops that, so an S-bend of sampled arcs is
    never one - is judged three more ways (review 4: where the net turn was 0,
    or the window wider than 2.5 m, nothing measured the shape):
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
      step, of which d·(1 - g(w/15 m)) came faster than the plan's fastest
      ease (the squeeze envelope's smoothstep over the shortest TaperFloor):
      [+3, -3] degrees 0.6 m apart (K7, 3.1 cm) 1.2x, an 18.2 cm step over 3.2
      m (South Old Statesville Road) 5.0x, a 3 cm drift over 10 m nothing. B4
      fails the same step past V at a node; at 6 m, 0.6 m of road is 7 of the
      240 rows.
    A ZIGZAG PEAK - a vertex whose chords stop at a turn BACK on both sides -
    scores twice its facet sagitta, c·turn/4: the eye's line is the zigzag's
    mean, not an arc through the neighbours (the spec's section 2, "symmetric
    zigzag: peak deviation <= V"; ±3° every 3 m, ±3.9 cm, now 1.7x). The run
    text names which rule fired ("a zigzag peak", "a bump or notch", "a
    hedged corner", "a jog"). `tools/city/lib/kink.mjs` and
    `CitySmooth.KinkScores` are the same code; a cross-check compiles the C#
    out of CitySmooth.cs and runs it on the JS's inputs (130,323 vertices on
    15,764 polylines, 8,000 of them the city's own, 59,342 scored past V under
    every rule, and 528,890 squeeze samples: identical to 1.5e-8, the same rule
    every time).
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
  same commit. `node tools/city/gateprobes.mjs` runs 135 synthetic probes with
  known answers (split kinks, legitimate fillets and S-bends, a kink behind a
  sub-V step, bend fans, attach arcs, straight roads, hedged corners, notches,
  jogs of every width, bumps and zigzags, C2's closed list, squeezed edges
  against I7 - also inside a slow rise - the ratchet and STALE); every review
  finding is one, and all must pass. `metrics.mjs` gains a SMOOTH section: B2/B3 on
  the exported centreline and its offset curves, in half a second, as an
  early warning.
- **Modes.** FAST runs inside `CityAudit.Run` (the drive and roadside
  audits' tiles, the reference spots, one band of 1/12 of the road tiles:
  `PSX_SMOOTH_BAND`). It is OPT-IN (`PSX_SMOOTH_FAST=1`, which
  `city-smooth.ps1 -Mode FAST` sets) until its first Unity run validates the
  tap and its cost; then `SmoothRules.FastInAudit` puts it in every city cycle.
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
  rollout switches or the ranking), of the road PNGs and (mesh) of the DEM,
  and the model; and every check's STATE. A ratchet against other inputs is
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
  worst, keys) and what moved, for the commit message. A check gating looser
  than when its baseline was recorded (ZERO -> RATCHET -> REPORT) fails too,
  stale or not; a ZERO check or the creek pin fails whatever the baseline.
  Merging `charlotte` (WP-04's water spans: 264 -> 560) into this branch keeps
  graph 27bccd93 but changes SPAN and NAME: the ratchet FAILS there as STALE
  until the merge commit re-records. The creek (ways 1078015030, 16671358,
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
- **Where it stands** (linecheck, graph 27bccd93, today's builder): 100,817
  A1 (1,049 of them, 32.2 km, squeezed edges outside their I7 envelope), 15,178
  A2, 464,065 B2 runs over 895.4 km (399,029 DATA: OSM vertices, and corners
  split over them; by the rule that gave each run its worst vertex: 321,978
  lone, 56,985 split, 38,498 hedged, 16,655 jogs, 28,760 zigzag peaks -
  26,862 of them BUILDER paint at taper diagonals, 485 km, the squiggle the
  owner circled - and 1,189 bumps or notches) and 254,788 B1 runs; 8,518 B3;
  26,809 B4 jumps; 16,776 C2 (with the 101 bend fans' mouths - 87 of the fans
  turn 60° or more - and 5,353 line ends along branch attach arcs), 10,917
  C3; D1 gates 914 runs / 5.6 km of paint inside another road, 6.2 km more is
  on attach arcs. 1,419,064 keys. Review 4's fixes moved A1 +140 runs (+2.6
  km) and B2 -1,725 runs but +116.6 km (runs merged as more of each line went
  bad) and +20,342 keys; nothing else. The reviewer's city examples are all
  flagged but one (Blakeney Professional Drive's bump 4.3x, East Tremont
  Avenue 2.1x, Camp Road 3.2x, North Tryon Street's notch 1.9x, South Old
  Statesville Road's 18.2 cm jog 5.0x, Carson Boulevard 4.6x, Rocky River
  Road 3.6x, Rea Road's and North Tryon Street's squeezes 4.9x / 4.3x);
  Baxter Street's jog ends 0.8 m before the fan mouth that trims its ribbon,
  so there is no exit straight to step from (the fan is the mesh gate's). On
  the design centreline (review 4's census, per edge) the unflagged bumps
  went 11 -> 2 and the unflagged 2.5-10 m jogs 367 -> 191: 160 of those are
  gentle steps whose part faster than the ease is under V, 31 have another
  corner within 10 m. The creek as built: yellow 0.39 m off at s 51.6, white
  lane line 1.92 m off and STRAY, B2 16.7 cm (21 runs), B4 18.6 cm. Under the
  M0 stopgap (`--model m0`) the creek's centre and lane lines read 0 (no B2
  on the creek under the new rules either; the city's BUILDER B2 there moves
  only 7,056 -> 7,944 runs); its tw4 edge lines still slide 0.36 m to the
  drawn edge before the crop removes them (A1, A3, C2), and the city keeps
  12,344 B4 jumps (most of 1-2 m: M0 does not touch the lane-count steps at
  nodes). linecheck also reproduces the
  2026-09-28 census exactly (264.88 km of wobble of 5 cm or more, 75,073 edge
  kinks over 2°; `--model nominalU` its U-only run: 17.47 km).
- **Open for the spec** (calibration questions, reported, not changed):
  - `KinkViewM` = 40 m (the split-corner rule's viewing distance, from the
    spec's "at R 7.5 a 90° corner stays rounded out to about 40 m").
  - B1 fails at 2.9 cm (1.15x) on the INNER ribbon edge at the tangent points
    of an R_min street fillet (inner radius about 3.5 m; the spec calibrated B1
    on the centreline down to R 7.5).
  - B3 reads a sampled arc about 2% under its radius, so a WP-11 fillet needs a
    few percent over R_min.
  - The cluster rule (unchanged here) reads a smoothstep lane shift or squeeze
    as a kink at its ends: 1 m over 10 m sampled every 0.5 m reads 2.0x, the
    I7 squeeze of 1 m over its 15 m floor 1.6x on its edge and midline (1.2x
    at 2 m sections; two stacked 1.6x), and back-to-back R 12 m / 30° fillets
    with no tangent read 1.1x. If R4 draws I7 or TAPR shifts as smoothstep,
    this needs a decision (the spec's Appendix A calls a 10 m eased taper a
    pass). The squeeze probes (Q2, Q4) assert only the I7 envelope.
  - A jog is judged over any transition up to the 10 m chord cap, by the part
    of its step that came faster than a smoothstep over the SHORTEST class
    taper floor (the street's 15 m) - for every class, since B2 runs per line,
    not per class. So a 2.6 cm step over 0.6 m (1.05 V; review 3's G1 case)
    now passes (2.45 cm is faster than the ease), and a motorway line is held
    to a street's ease. A jog on a curve is B1's (about half its step).
  - An S-bend of sampled arcs SHORTER than the 10 m chord cap between two
    straights is a jog: R 30 m arcs turning 3° each way shift a line 8.2 cm in
    3.1 m (2.3x), and 8° each way 58 cm in 8.4 m (5.8x). A sidestep that fast
    is faster than any plan ease; if WP-11 ever fits a real reverse curve that
    short, it needs a decision. Longer S-bends never fit a window.
  - "Between two straights" is min(4w, 10 m) of line not turning by a quarter
    of the window's heading on each side, or straight on to the line's end at
    least w away; 31 of the census's remaining jogs have another corner closer
    than that.
  - A zigzag peak scores twice its facet sagitta (the spec's section 2 rule).
    The spec's Appendix A still lists "zigzag ±3 cm, 10 m period: B pass; A1
    catches it" - on a DATA line A1 cannot (the plan follows it), so B2 now
    fails it (1.2x). The appendix row wants updating.
  - Owner question: a squeezed edge that weaves 0.5 m in and out every 36 m
    passes the I7 envelope (each rise is longer than the 15 m floor, and I7
    only holds dips narrower than the floor).
  - The squeeze envelope runs per edge, not per chain: a squeeze that crosses
    a node is judged on each side separately.

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
- **Left:** the lane survey's 46 solids over a lane (squeeze splits at one
  road's cross-sections and not the other's on I-277/I-77 and e7753 beside
  South Boulevard; deck rails where an edge's width changes at a node;
  Albemarle Road under the Independence Expressway's retaining wall, where
  seating the branch would put 16% grades off its deck) and 10 verge or
  seam slivers of 1-6 cm over clipped lanes. Off the audited tiles: the 13
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
