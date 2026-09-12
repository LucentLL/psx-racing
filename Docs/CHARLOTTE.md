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
  south — top-down and at street level, to `Screenshots/City`; plus the
  drive-along views (top, ahead, back, high) of the West 5th bridge, every
  freeway-to-freeway interchange, I-77 north of uptown and an overpass.
- `tools/city-play-check.ps1` (PSX Racing/Check City Spawns): PLAYS free
  roam and the 277 race headless and checks the car poses after physics —
  the two spawn bugs of 2026-09-12 were invisible to everything above.
- `tools/city-ground-probe.ps1 -At "x,z"`: which corridors set the ground
  height round a point, for chasing a GRASS note from the drive audit.
- The live URL is the real test.

## The 2026-09-12 pass: floating roads, ledges, invisible walls

Reported after the rebuild: "a lot of roads still floating in air, not
connecting, don't properly transition lane counts; invisible walls; bridges
have ledges blocking entrance; 277 and the 77/85-to-485 clovers need special
attention". Every item had a concrete cause, and the plan geometry was never
one of them (it is OpenStreetMap's, and it matches the satellite view by
construction). What changed:

- **The ground.** The DEM was a bare 5x5 min filter of SRTM: 6.6 m low on
  average and 20 m low beside every valley, so every hillside road stood in
  the air. It is a morphological opening now (erode, dilate back; a closing
  after it fills the radar's one-pixel pits): 2 m mean, and Trade & Tryon
  lands at 227 m ASL.
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
  break. `CityElevation.MeasureCrests` records each station's height above
  its neighbours' chord (and each node's, between its two steepest arms),
  and the corridor pin sinks the land by that much extra there; the kerb
  face reaches down through it. A straight grade keeps its 20 cm kerb. (2)
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

## Not in v1 (in order of likely next)

Traffic, gas stations / parking lots / mechanic shops in the city,
neighbourhoods beyond the 8 km core (each 8 x 8 km box is one Overpass
fetch), city race tracks drawn on the graph by the player, lane-level turn
markings at junctions, lamps and signal heads, a skyline backdrop past the
fog, a one-sided (MUTCD) lane taper on one-way carriageways, the ROVAL.
