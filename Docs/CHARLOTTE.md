# Charlotte (2026-08-25)

Asked for: base the game on a real city the way Midnight Club used Atlanta/LA —
a 3D Charlotte, ported from the extensive road network already built in the
HTML game (Racing-Game-2, scaled 1:6 there), "as close to scale as possible
while maintaining LoD, draw distance, and 60-100+ FPS", with two standing
rules: **every road crossing water gets a bridge, and every highway crossing a
road goes over it**. Minor roads are missing from the data and need to be
connected.

## The source: two datasets, one each for land and water

RG2 carries TWO Charlottes. The hand-traced one (the `Maps/*.png` layers →
`baselineRoads.ts`, 109 roads) is what the reference images render — and it is
the only home of the WATER: 30 creeks and the two Lake Wylie arms
(`baselineWater.ts`). The second is a full OSM import
(`fixtures/osm/charlotte_rows.json`): 3,076 rows / 16,660 verts, every road
named, with lane counts, one-way flags, `divided`, real bridge `deck` spans,
840+ real interchange ramp rows, and 2,389 intersections tagged
signal/stop/yield — geo-registered and invertible to lat/lon.

The port takes ROADS from the OSM bake (richer, welded, junction-split, ramps
real) and WATER from the traced set, co-registered into the OSM frame by
fitting the one shape both datasets share: the I-485 loop. Attribution: OSM
data is ODbL — "Map data © OpenStreetMap contributors" must be user-visible,
and goes on the Charlotte loading/menu surface.

True residential streets exist in NEITHER set (the Overpass fetch stopped at
tertiary). The secondary+tertiary net (1,371 rows) is the connected minor
network for now; the full residential grid is a one-line filter change in
RG2's `tools/osm/fetch.mjs` plus a re-download, offered as a follow-up.

## Scale: the layout scales, the streets do not

The car is a real-size object (a 4.29 m Charger), so nothing that the car
touches can shrink: lane widths, bridge clearances, building doors are 1:1 at
ANY city scale. RG2 already works this way — layout at 17.212 real m/tile
(the ÷6), cross-sections at the true 2.8687 m/tile — and the port keeps the
two currencies separate, with the layout compression dialled from 1:6 to
**1:1**. One knob, `LayoutScale`, applied to graph geometry only. At 1.0 the
I-485 loop is ~31 km across, the origin sits at the loop's centroid (how the
OSM bake is registered), and the far edge lands ~16 km out, where a float32
world position quantizes at ~2 mm — inside the wobble a PSX renderer adds on
purpose. If far-edge physics jitter shows up on the live build, the knob
drops (0.5 halves every drive time and every coordinate) without touching
data, which is stored in real metres.

Widths come from RG2's lane ladder (`laneStandardizedWidth`), resolved in the
EXPORTER so Unity never re-implements it: the `w` in a row is a class index,
not a width — w=12 is 8 lanes + shoulders ≈ 33 m of interstate, w=5 is a
7.3 m two-lane street, and I-485's profile is keyed by NAME in RG2, which is
exactly the kind of rule that must be baked out, not ported.

Perf does not depend on the knob: fog closes at ≤355 m and the far plane is
360, so the streamed ring is the same ~25 tiles at any scale. Scale only
changes how much WORLD there is, not how much of it exists at once.

## The inversion: this project's first runtime world

Every circuit is baked whole into a scene by the editor builder; a city that
size cannot be (15,000 tiles worth of mesh). So `Charlotte.unity` is baked
nearly EMPTY — lighting, camera+HUD, the player car, and one `CityWorld`
component — and the world is generated at runtime, per 256 m tile, from a
road-graph JSON in Resources (the `rg2_cars.json` pattern; synchronous
`Resources.Load` is the one loading path this project trusts on WebGL).

Per tile: ground grid graded to the roads, road ribbons with drawn per-class
surfaces, intersection patches, water ribbons, bridge decks, buildings,
lamps. A ring of tiles within ~640 m of the car exists; everything else is
data. Budget: one tile build per frame while moving (a tile crossing takes
~4-10 s, a full new row is 5 tiles — never close), and the tile under the car
is force-built synchronously if the budget ever loses that race. Tile
meshes are built in TILE-LOCAL coordinates so vertex precision never depends
on distance from origin; only transforms carry the offset.

`GroundHeightAt` cannot come along: it is a Gaussian mean over EVERY waypoint,
O(track) per vertex, and a city has ~10⁵ road samples. The city height field
is tile-local by construction: an O(1) analytic base terrain (long-wavelength
relief, ~12 m amplitude), plus corridor pinning against only the road edges
registered in the queried tile's spatial-hash cell — same shelf / sink /
blend shape as the circuits, different lookup.

## The graph, and the two bridge rules as geometry passes

Load order: JSON → nodes/edges (polylines, class ∈ interstate / ramp / major /
minor, lanes, one-way, name) → **crossing solve** → elevation profiles →
tile index.

The crossing solve is where the user's two rules live, as rules. The OSM
bake already did the hard classification (junction-split at real shared
nodes; "same-level crossings with no shared node are never real junctions"
got one chain lifted; real bridges carry `deck`; z ∈ {0,4,5,6,7} records who
stacks over whom) — the Unity side turns those DISCRETE facts into
CONTINUOUS elevation:

1. Default: every edge follows the base terrain. A freeway is NOT 14 m in the
   air its whole length — real Charlotte freeways run at grade and rise at
   crossings, and z is a stacking ORDER, not an altitude.
2. Crossing that shares a node → at-grade intersection: ribbons trimmed
   back, a flat patch fills the junction (which also kills z-fighting by
   construction — overlapping ribbons never coexist). Control type
   (signal/stop) comes from the isect overlay.
3. Crossing without a shared node → grade separation: the higher-z edge (tie:
   higher class) takes a **raise constraint** at that arc position — clear
   the lower road by 5.1 m — and the profile solver lifts a hump with ≤4%
   approaches. Consecutive humps that overlap merge into a viaduct (which is
   what I-277 through uptown becomes, automatically). Ground pinning is
   disabled under the elevated run; the road below pins the ground; piers
   connect; a deck mesh spans it. That is the "every highway over a road gets
   a bridge" rule, held by the solver rather than by authored spans.
4. Road × water crossing (geometric, against the co-registered creeks, OR'd
   with the OSM `deck` spans) → **bridge**: the road's profile holds its
   smoothed line, the terrain carves the creek bed under it, deck + rails +
   piers span it. Same carve/deck contract as the circuit bridges: both read
   the same span so they cannot drift.
5. Interchanges need no invention: the 840+ `*_link` rows are the real
   ramps, already snapped to their mainlines. Their endpoint nodes inherit
   the heights of what they join, so a ramp climbs because its ends do.

Elevation profile per edge: sample base terrain along arc length → smooth →
grade-limit (4% freeways, 6% streets) → apply raise constraints and deck
locks → blend approaches. Node heights are shared so junctions meet exactly.

## Minor roads: the tertiary net now, residential later

Residential/service streets were never fetched from Overpass (the filter
stopped at tertiary) — that is the missing detail. What DOES exist and ships
now: 583 secondary + 788 tertiary rows, junction-split and connected, which
at city scale is the minor-collector network. The graph invariant stands
regardless: the self-test asserts one connected component reachable from
spawn. The true residential grid is a follow-up = widen one regex in RG2's
`tools/osm/fetch.mjs`, re-fetch, re-bake, re-export (needs the owner's nod —
it is a large Overpass download).

## What a city street is (vs a circuit)

No barrier ribbon, no kerb strip, no walls-every-4 m (a circuit's 800
BoxColliders per track cannot scale and a city does not want walls). A street
is: road ribbon (layer 8 Road — grip comes from layer), painted per-class
surface drawn by code (the punch-clock rule: never source a marking, draw
it — interstate lanes + shoulders, 4-lane arterial with center turn lane,
2-lane minor), sidewalk strip in the core, buildings seated at real setbacks
with one BoxCollider each (Solid), ground everywhere else (MeshCollider,
default layer = offroad grip). Driving off-road is legal Midnight Club
behaviour; grass grip is the penalty.

Buildings: procedural boxes from road frontage — uptown tower cluster inside
the 277 loop (facade textures building_01..11 from the OneDrive pack, tiled
by storey), shop strips along core majors (Shops_00..31 ground floor + brick
upper), low suburbia outward. Heights from distance-to-uptown with hash
jitter. v2 replaces the placement with RG2 footprints if/when traced.

## Mode wiring

City = a `TrackCatalog` entry with `city = true`, appended LAST (after the
drag strips) so every existing SceneIndex holds; the garage moves up one,
which the formula and self-test absorb. `PSXRacingBuilder.Build` branches to
`BuildCityScene` for it. Everything loop-shaped is gated the way `drag`
already gates: resample/corner-radius/self-clearance tests exempt,
lap HUD off, `RaceManager` lap logic off. FREE ROAM launches from the
LifeHome MAIN tab like a practice session (no purse, no opponents, fuel and
odometer real), lands at the uptown spawn, and exits via the pause menu.
Respawn/stuck recovery re-target the nearest graph edge sample instead of a
TrackPath index.

## Verification

- `PSX Racing/Preview Charlotte`: builds tiles around N probe points in edit
  mode, renders top-down + street-level PNGs (the no-play-mode preview
  pattern; runtime-built worlds are invisible until photographed).
- `CityAudit` (self-test section): graph is one component from spawn; every
  water crossing carries a deck; every grade separation clears 4.6 m
  measured deck-underside to road; no edge grade over limit; every tile
  build is deterministic (same tile twice → same vertex count).
- The live URL is the real test (WebGL-only failure modes are the norm here).

## Not in v1 (in order of likely next)

Traffic (needs the graph — which now exists — plus spawn/despawn ring and
yield rules), city races (point-to-point checkpoints through the graph),
minimap, gas stations in-city (fuel truck covers stranding until then),
skyline backdrop beyond fog, real minor-road traces, building footprint
traces, interchange ramp geometry beyond diamond slips.
