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
seconds and in every venue blurb):

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
- SRTM 1-arcsecond (`tools/roads/cache/*.hgt.gz`, via `tools/roads/lib.mjs`).
- The water still comes from RG2's hand-traced creeks and Lake Wylie
  (`baselineWater.ts`), co-registered onto the OSM frame by ICP against RG2's
  own merged I-485 row (23 m residual).

What the exporter makes of it:

- **Real topology.** A junction is a node two ways share, by id. Ways are cut
  at those nodes into 25,250 edges between 20,359 nodes (3,966 km). Dead ends
  within 2.5 m of another edge are welded onto it (one, in this snapshot —
  the arterial and minor-street fetches share their nodes).
- **Real carriageways.** A divided road is two one-way edges with the ground
  showing between them. Each carries its own lane count (`lanes`, or
  `lanes:forward + backward`, or a per-class default), and a two-way road with
  an odd count has a centre turn lane (the TWLTL on every undivided Charlotte
  arterial).
- **Real grade separations.** A crossing with no shared node IS a separation
  and OSM says which road is on top: `tunnel < layer < bridge`. 928 of them,
  every one decided by a tag; the class-rank fallback never fired.
- **Real bridges.** `bridge=yes` marks the exact extent of every deck (56.5 km
  of them), separately from the 275 water spans found geometrically.
- **Three race routes** as edge chains through this graph, from the way-id
  lists `tools/clt/fetch_clt.mjs` verified on 2026-09-07 (every id still
  present). Uptown Loop 9.20 km (loop, 1.61 km on structure), Tryon Street
  Sprint 6.02 km, Independence Sprint 7.02 km.
- **The ground**: a 60 m SRTM grid over the whole beltway, min-filtered over a
  120 m reach (uptown's roofs are 200 m radar returns) and blurred, stored as
  metres above a datum a little under the county's lowest point.
- **31,870 footprints** (styles glass / midrise / brick / house / shops,
  gabled where the polygon is a house's), counter-clockwise, with heights.

Outputs (all in `Assets/PSXRacing/Resources`): `charlotte_city.bytes` (2.5 MB
graph + water + separations + spans + routes; a versioned binary the runtime
reads through a BinaryReader — the JSON equivalent was 9 MB), `charlotte_dem.bytes`
(1.4 MB), `charlotte_bld.bytes` (1.7 MB), `charlotte_routes.json` (the menu's
copy of the routes: lengths, lines, a 20 m polyline each). Debug plots land in
`tools/city/charlotte_*.png`.

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
   are cut down along an 8% cone of their own — 285 crossings solve this way.
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
  south — top-down and at street level, to `Screenshots/City`.
- The live URL is the real test.

## Not in v1 (in order of likely next)

Traffic, lane-level turn markings at junctions, lamps and signal heads,
in-city gas stations, footprints beyond the core, a skyline backdrop past the
fog, city races beyond the three (a SouthPark loop needs the junction-arc
pass), the ROVAL.
