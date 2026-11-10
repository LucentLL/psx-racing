# Mountain stages against USGS lidar (3DEP)

2026-11-09, branch `mountain-lidar` (off `mountain-nm` 70364204). Owner: "the mountain tracks probably aren't accurate or to scale; correct while checking lidar."

## The answer

**To scale: yes, they already were.** Plan length was checked earlier on seven stages (within 0.09%). The eighth, NC 226A (`swa`), was not matched to its source then; it is `overpass_swiss226a.json`, and the same check gives +0.008% against the distance on the globe, with the bake a median 0.1 m (worst 1.2 m) off the OSM line. What the lidar adds: the mapped line cuts the inside of some bends, so the real NC 226A is 20 m longer in 11.9 km (0.17%).

**Accurate: mostly, with four real faults.** The heights the stages were baked from are labelled "SRTM", but they do not behave like radar: along all eight roads the bake's raw source is within 0.3 m of the lidar road on average (RMS 1.0 to 1.6 m), where treetop radar would stand metres high in forest. The tiles in `tools/roads/cache` are the AWS "skadi" set, which inside the United States is compiled from bare-earth USGS data at 30 m. The note in the builder that says it "reads the canopy top, up to +33 m on these ridges" is wrong. So the land was never tens of metres off. What is off:

1. **Bridge decks sag into the ground under them (Blue Ridge Parkway, `brp`).** The bake smoothed the bare ground and laid the road on it, bridges included. The Linn Cove Viaduct (4488-4880 m) dips 16 m in the middle: the game goes down it at 8.5% and climbs out at 5%, where the real deck is a steady 2% descent. The other seven spans sit 1.7 to 3.9 m low.
2. **A hole in Beech Gap's far land (`beech`).** The far grid's western 2 km (33 columns, the whole 24 km height, everything west of longitude 83 W) is at sea level: a wall 850 to 1,830 m high, 8 km west of the road. The bake only loaded the tiles its road's box touched.
3. **The grade clamp flattens NC 226 to Gillespie Gap (`gap`).** The real road averages 10.2% for 700 m (3300-4000 m) with pitches of 12 to 13%, and is over 8.5% for 3.2 km; the bake clamped at 10.0%, so the game road falls up to 4.9 m below the real one and makes it up later.
4. **The Blowing Rock loop (`brock`) runs 1.5 km (7944-9432 m) on the US 221 / US 321 bypass**, which OSM tags as a divided highway (two lanes each way, four lanes undivided for 516 m, 50-55 mph). The game builds it as a 6.4 m two-lane road. This is a width and road-type error, not an elevation one.

Everything else is small: road heights within 1 m RMS of the lidar on six stages, grades softened (short steep pitches lose 1 to 3% of grade over about a quarter of each route), the mapped centreline a median 0.75 to 1.25 m from the middle of the lidar bench, which is about what the method can resolve.

**Corrected in this branch (round 1 and round 2):** all eight stages are re-baked from lidar. Five pass every stage audit in a lab build and have before/after pictures: NC 226A (`swa`), Blue Ridge (`brp`), Gillespie Gap (`gap`), the Little Switzerland loop (`swiss`, bar one terrain-audit line that is the tunnel under the horizon stand-in, not the bake) and the Blowing Rock loop (`brock`). **Three are re-baked but fail a few audit lines on the new land and are not release-ready: Beech Gap (`beech`, 2 half-sections), Chimney Rock (`chimney`, 4) and Mount Mitchell (`mtm`, 1)**; each is its own commit and reverts cleanly. Fault 1 (decks), fault 2 (Beech Gap's hole) and fault 3 (the clamp) are fixed in the data; fault 4 (the Blowing Rock bypass) is not. See "Round 2" and "Guardrails" below.

## The table

Game = the shipped bake. Real = USGS 3DEP. Line offsets are of the lidar bench centre from the game centreline. Heights are game minus real (positive: the game road is too high).

<!-- TABLE:BEGIN -->
| Stage | Length | Line vs bench: median / p95 / worst | Stretches > 2 m off | Height: bias / RMS / worst | Climb + descent, real / game | Grade p95, real / game | Grade max, real / game | Grade off by > 1% | Near grid RMS (30-100 m / 100-500 m) | Far grid RMS |
|---|---|---|---|---|---|---|---|---|---|---|
| NC 226A, Little Switzerland (`swa`) | 11.92 km | 0.75 / 3.0 / 6.2 m | 34 (1412 m, 12%) | +0.33 / 0.82 / +3.0 m at 10972 m | 580 / 572 m | 7.9 / 7.5% | 9.2 / 8.6% | 24% of the route | 2.3 / 2.3 m | 2.6 m |
| NC 226 to Gillespie Gap (`gap`) | 6.69 km | 1.00 / 3.8 / 6.2 m | 14 (968 m, 14%) | -1.42 / 2.37 / -4.9 m at 3792 m | 419 / 411 m | 11.5 / 10.0% | 13.0 / 10.0% | 26% of the route | 2.0 / 2.1 m | 2.6 m |
| NC 128, Mount Mitchell (`mtm`) | 7.47 km | 1.25 / 3.5 / 6.5 m | 19 (1856 m, 25%) | -0.33 / 0.56 / -1.6 m at 6604 m | 435 / 431 m | 8.7 / 8.8% | 9.4 / 9.2% | 10% of the route | 1.5 / 2.2 m | 3.0 m |
| NC 215 from Beech Gap (`beech`) | 11.28 km | 0.75 / 3.0 / 6.2 m | 20 (1292 m, 11%) | -0.17 / 0.90 / -2.7 m at 144 m | 737 / 736 m | 8.6 / 8.9% | 9.5 / 9.3% | 19% of the route | 2.3 / 2.3 m | HOLE, see text |
| Chimney Rock Park Road (`chimney`) | 4.42 km | 0.75 / 2.5 / 6.0 m | 4 (164 m, 4%) | +0.07 / 0.71 / +1.9 m at 1116 m | 264 / 263 m | 8.5 / 7.9% | 14.7 / 8.6% | 38% of the route | 2.6 / 3.1 m | 2.5 m |
| Blue Ridge Parkway, Linn Cove (`brp`) | 7.25 km | 0.75 / 3.0 / 5.8 m | 11 (1044 m, 14%) | -1.69 / 3.30 / -16.0 m at 4660 m | 174 / 186 m | 4.9 / 5.0% | 6.1 / 8.5% | 31% of the route | 1.9 / 2.0 m | 2.5 m |
| Little Switzerland - Gillespie Gap loop (`swiss`) | 9.68 km | 0.75 / 3.0 / 5.2 m | 17 (948 m, 10%) | +0.18 / 0.85 / +4.2 m at 4104 m | 482 / 472 m | 7.6 / 8.0% | 8.8 / 9.5% | 25% of the route | 1.9 / 1.9 m | 2.5 m |
| Blowing Rock - Moses Cone loop (`brock`) | 9.88 km | 1.25 / 3.2 / 7.8 m | 22 (2164 m, 22%) | -0.39 / 0.96 / +3.2 m at 8076 m | 367 / 350 m | 6.9 / 7.0% | 7.4 / 8.1% | 25% of the route | 1.3 / 1.5 m | 2.7 m |
<!-- TABLE:END -->

One sheet for all eight (real against game profile, and the difference): `mountain_lidar_audit_sheet.png` in the session scratchpad (path in the hand-back).

## The correction: NC 226A (`swa`)

`node tools/roads/bake_3dep.mjs swa` re-bakes the stage from lidar in the same four files the builder already reads (`Resources/swa_stage.json`, `Art/Swiss226A/swa_dem_near.bytes`, `swa_dem_far.bytes`, `swa_dem_meta.json`). `--check` re-derives them from the tile cache with no network and compares byte for byte (passes). The SRTM bake is kept in `tools/roads/srtm_bakes/swa_srtm/` (outside Assets, so nothing ships twice); every re-bake starts from that copy.

**The line.** 15 stretches (628 m of road) moved onto the lidar bench, by 2.2 to 4.7 m, each faded in and out over 24 m:

| From - to (m, old stations) | Length | Moved |
|---|---|---|
| 44 - 68 | 28 m | 2.4 m right |
| 160 - 184 | 28 m | 2.2 m left |
| 412 - 488 | 80 m | 2.4 m left |
| 528 - 556 | 32 m | 2.5 m left |
| 1884 - 1904 | 24 m | 2.4 m left |
| 3652 - 3712 | 64 m | 3.4 m left |
| 3848 - 3884 | 40 m | 4.0 m left |
| 4248 - 4268 | 24 m | 2.5 m right |
| 4604 - 4628 | 28 m | 2.3 m right |
| 7120 - 7176 | 60 m | 2.6 m left |
| 7760 - 7780 | 24 m | 2.2 m left |
| 9172 - 9220 | 52 m | 3.0 m left |
| 10212 - 10260 | 52 m | 3.0 m left |
| 10408 - 10432 | 28 m | 2.9 m left |
| 11136 - 11196 | 64 m | 4.7 m left |

19 more stretches read over 2 m off and were left alone, because the evidence there is not good enough to move a road on: at eleven the shelf was found at fewer than half the stations, at six it is 13 to 17.5 m wide (a junction mouth or a pull-off pulls the shelf's middle sideways), two are under 16 m long. The largest of those: 9320-9348 m (4.9 m, at a side road), 9692-9736 m (4.5 m, a 17.5 m wide spot), 11696-11752 m (4.3 m), 8880-8904 m (4.3 m), 5452-5500 m (4.1 m). They want a look at imagery before anyone moves them.

Moving the line outward on bends made the road 20 m longer (11,917 to 11,937 m): the mapped line had cut the inside of those bends. The tightest corner went from 11.2 m to 11.7 m (the switchback at 6.9 km). The start line is where it was (33 m); the finish is the same place, now at 11,738 m. The three sprint cuts in `TrackCatalog` are the same places at their new distances: 3688 to 3696, 7832 to 7848, 11721 to 11738; the MIDDLE/LOWER boundary reads 635 m on the lidar, not 636.

**The heights.** The lidar ground on the bench under every waypoint, with one gaussian of sigma 8 m along the road and no grade clamp. Why 8 m: it is the smallest sigma at which the tightest vertical curve clears the game's own floor (`LifeSimSelfTest.CheckElevation` refuses under 200 m because the 9 m ground lattice cuts through a sharper crest; the bake asks for 230 m and got 347 m at the first sigma tried). The SRTM bake used sigma 60 m, three more 22 m passes and an 11% clamp.

| | SRTM bake | 3DEP bake | Real (lidar) |
|---|---|---|---|
| Height against the lidar road: bias / RMS / worst | +0.33 / 0.82 / +2.95 m | 0 / 0 / 0.04 m (same instrument: a consistency check, not an independent one) | |
| First waypoint / last waypoint | 1041.3 / 469.5 m | 1043.5 / 469.1 m | 1043.5 / 469.1 m |
| Descent / climb | 571.7 / 0.0 m | 576.9 / 2.6 m | 577.4 / 3.0 m |
| Grade mean / p95 / max | 4.79 / 7.47 / 8.55% | 4.85 / 7.89 / 9.29% | 4.86 / 7.94 / 9.51% |
| Road over 8.5% | 32 m | 120 m | 168 m |
| Grade more than 1% off the real road | 24% of the route (60 stretches) | 0% | |
| Stretches with the line over 2 m off the bench | 34 (1412 m) | 19 (852 m), the ones left alone | |

What a driver gets: the short steep pitches are back (the SRTM bake had turned 7 to 8% pitches of 50 m into 4 to 5%, and a 130 m nearly level stretch (1%) at 11.0-11.1 km into a 4% descent), and the three small rises near the bottom exist (2.6 m of climb on a road the game had as pure descent).

**The land.** Near grid (12 m, 465 x 542) from the 1/3" DEM at full resolution; far grid (60 m, 354 x 370) from its 4x overview; same lattices, same `baseM` (429), so the world did not move. Against the SRTM bake the near grid changed by 2.16 m RMS (largest 14.6 m, on a crest 500 m off the road) and the far grid by 2.56 m RMS (largest 66.7 m, the quarry). `swa_dem_wide.bytes` (240 m, `bake_wide.mjs`) is untouched and still SRTM: at 240 m cells the difference is below a cell's own relief.

**Lab build and pictures.** One `StageLab` build of SwissNC226A in PSXLab (PC content) with the SRTM bake and one with the 3DEP bake, same code (this branch), then `HazeProbe` pictures at eight places, each as the chase view and as a 25 m high view turned 60 degrees to the open side (noon, fall dress). The after stations are the same places on the re-baked line.

| StageLab | SRTM bake | 3DEP bake |
|---|---|---|
| Roadside checks | 0 failing | 0 failing |
| Obstacle audit (edge drop, edge face, edge falls, barriers, run ends, surface, facing, barrier faces, wall solids) | all ok | all ok |
| Terrain audit: road clear of the ground at its tightest; probes buried | 0.344 m; 0 of 8,943 | 0.331 m; 0 of 8,958 |
| Ground lattice under the shoulder | ok on 49,280 probes | ok on 67,276 probes |
| Lane audit | ok (2.990 m lanes) | ok (2.990 m lanes) |
| Near-grid vertices the builder lowered under the road shelf | 588 (up to 1.51 m) | 784 (up to 1.26 m) |
| Half-sections with a barrier within 8 m | 5,258 of 5,962 | 4,660 of 5,972 |
| Guard posts | 5,472 (459 / km) | 4,148 (347 / km) |
| Wall runs / rock cuts | 51 / 31 | 78 / 23 |
| Benched shoulders over 2.5 m; mean drivable shoulder | 378; 2.1 m | 883; 2.7 m |
| "warn edge slope" (steeper than 1V:6H for over 1 m inside the clear zone; a warning, not a failure) | 97 half-sections, 388 m of edge | 637 half-sections, 2,548 m of edge |

Nothing fails. **The roadside did change, and that is the thing to look at.** The lidar land has the real bench and verge in it, so the ground beside the tarmac stays level for longer before it falls. The builder's own roadside rules then warrant a barrier at fewer places (a quarter fewer guard posts) and leave six times as much edge in the "steep but not guarded" warning band. That is the rules reacting to truer ground, not a fault in the bake, but it is a visible change to the stage: for example the guardrail on the right at 9,360 m is now open grass shoulder, and the wooden fence at 10,200 m is gone where the lidar has a level wide spot. If the owner wants the old density of rail on this road, that is a `RoadsideRules` threshold, not the data.

What the pictures show (all in `Docs/shots/lidar_audit/`, `swa_ba_<station>_chase.jpg` and `_vista.jpg`, before on the left):

- The road sits on its bench in all eight chase views: no floating or buried tarmac, shoulders flush, kerb strip and paint unbroken.
- Rock cuts stand on the uphill side where the lidar has a cut face (8,400, 9,360, 5,450, 11,100 and 11,160 m), taller than before; at 6,900 m (into the 11 m switchback) the cut became a grass bank.
- Guardrails and fences that remain are seated on the shoulder; trees are seated; nothing hangs in the air.
- At 11,160 m, where the line moved 4.7 m, the bend the mapped line had straightened is there.
- The high views show the same hills: no hole, spike or step, near or far. The land changed by about 2 m RMS, which a wide view cannot show.
- Steepest stretch (8,400 and 9,360 m), tightest hairpin (6,900 m) and the valley (10,200 m) are among the eight.

These are stills from an editor probe. Nobody has driven it. The full StageLab reports are in `Docs/lidar_audit/stage_lab_*.txt`.

**Round 2: rebuilt once more with the surveyed rail** (256-372 m left). Roadside checks 0 failing, every obstacle, terrain and lane audit ok, 4,176 guard posts; the rail is in the picture `shots/lidar_audit/swa_rail_264_chase.jpg`, on the left through the bend with the cut bank on the right, as on the real road.

## The second correction: Blue Ridge Parkway (`brp`)

`node tools/roads/bake_3dep.mjs brp --keep-line`. The SRTM bake is kept in `tools/roads/srtm_bakes/brp_srtm/`.

**The line was not moved.** The Parkway's shelf is 11 m wide around 6.4 m of pavement, and its grass shoulders are not the same width on both sides, so the shelf's middle does not locate the pavement to better than about 2.3 m. The lidar reads the shelf more than 2 m off the mapped line at 11 places (the longest: 980-1124 m at an overlook, 2484-2644 m, 3572-3820 m across two bridges, 5624-5744 m, 6776-6880 m); none is evidence enough to move this road. Plan, bridge spans, start and finish lines are unchanged to the centimetre.

**The heights.** Lidar ground on the shelf, sigma 8 m (tightest vertical curve 599 m), no clamp, and a straight line across each of the eight bridge spans from abutment to abutment. That last part is an assumption: bare-earth lidar has no decks in it, so a deck's true profile between its abutments is not measured.

| | SRTM bake | 3DEP bake | Real (lidar) |
|---|---|---|---|
| Height against the lidar road: bias / RMS / worst | -1.69 / 3.30 / -15.96 m | 0 / 0 / 0 (same instrument) | |
| Linn Cove Viaduct (4488-4880 m) | dips 16 m: down at 8.5%, up at 5% | 1342.2 to 1334.2 m, a steady 2.05% | |
| The other seven spans | 1.7 to 3.9 m low | straight between abutments | |
| Climb / descent | 65.3 / 121.3 m | 59.3 / 114.1 m | 59.6 / 114.3 m |
| Grade p95 / max | 4.98 / 8.49% | 4.91 / 6.05% | 4.91 / 6.13% |
| Grade more than 1% off the real road | 31% of the route | 0% | |
| First / last waypoint | 1331.1 / 1275.1 m | 1328.2 / 1273.4 m | 1328.2 / 1273.4 m |

With the decks where they belong, the bare ground is up to 23 m under the Linn Cove deck line (it was 7.5 m under the sagging one) and 9 to 20 m under the others.

**The land.** Near grid 613 x 401, far grid 384 x 341, same lattices, `baseM` 1235. Against the SRTM bake: near 2.04 m RMS (largest 31.9 m: Grandfather's crags, which the 30 m data had clipped), far 2.49 m RMS.

**Lab build and pictures.** One `StageLab` build of BlueRidge with each bake, same code, then pictures at five places (the approach to the viaduct at 4,460 m, mid-span at 4,660 m, its far end at 4,800 m, and the spans at 2,740 and 1,660 m).

| StageLab | SRTM bake | 3DEP bake |
|---|---|---|
| Roadside checks | 0 failing | 0 failing |
| Obstacle audit, deck rails (796 deck and approach half-sections), barrier on both sides of every span station (318) | all ok | all ok |
| Terrain audit: road clear of the ground at its tightest; probes buried | 0.309 m; 0 of 4,722 | 0.294 m; 0 of 4,722 |
| Gorge under the full-depth spans (238 waypoints) | 11.2 m deep | 22.8 m deep |
| Lane audit | ok (3.200 m lanes) | ok (3.200 m lanes) |
| Near-grid vertices lowered under the road shelf | 398 (up to 0.84 m) | 448 (up to 0.71 m) |
| Half-sections with a barrier within 8 m; guard posts | 2,965 of 3,624; 2,528 | 2,791 of 3,624; 2,495 |
| Rock cuts | 29 | 26 |
| Mean drivable shoulder | 2.9 m | 3.8 m |
| "warn edge slope" | 190 half-sections, 760 m of edge | 154 half-sections, 616 m of edge |

Nothing fails, and here the roadside barely changed (33 fewer posts in 7.2 km).

What the pictures show (`Docs/shots/lidar_audit/brp_ba_<station>_chase.jpg` and `_vista.jpg`):

- **Mid-viaduct (4,660 m):** before, the car is at the bottom of the dip with the deck climbing ahead and trees crowding the rail; after, the deck runs out level-to-falling with open sky and the piedmont beyond it.
- **Approach (4,460 m) and far end (4,800 m):** before, the road drops onto the deck and climbs off it; after, it carries straight on.
- **The spans at 2,740 and 1,660 m:** 2 to 4 m higher, no visible change of character.
- Rails are continuous and seated on every deck in view; trees and the land beside the approaches are seated.

Round 2, from below (`shots/lidar_audit/brp_under_<station>.jpg`, five views on the open side, 9 m under the road): at 4,700 m the Linn Cove deck stands on a tall pier that reaches the ground, slab and rail intact; the other four views are mostly the forest that grows under the spans, with the deck seen through it at 2,750 and 5,510 m. Nothing hangs or is cut off in what can be seen. The title screen, which flies this stage, was not rendered: there is no batch harness for it (it takes its positions from the path, so it follows the new deck).

## Round 2: the other six stages (2026-11-10)

Each re-baked by `node tools/roads/bake_3dep.mjs <pre>` (`--check` passes for all eight), built once in the lab sandbox with this branch's code, and photographed at the same places as the scene that was in the sandbox before (built 2026-11-09 from the SRTM bakes). The SRTM bakes are kept in `tools/roads/srtm_bakes/<pre>_srtm/`.

| Stage | Height before: bias / RMS / worst | Grade p95 / max: before, after (real) | Grade > 1% off: before, after | Climb + descent: before, after (real) | Lab build: roadside checks, other audits | Road clear of ground, buried probes | Guard posts, edge with a barrier |
|---|---|---|---|---|---|---|---|
| NC 226 to Gillespie Gap (`gap`) | -1.42 / 2.37 / -4.9 m | 10.0 / 10.0%, 11.4 / 12.7% (11.5 / 13.0%) | 26%, 0% | 411, 418 m (419) | 0 failing; all ok | 0.331 m, 0 | 1,338; 1,845 of 3,344 half-sections |
| NC 215 from Beech Gap (`beech`) | -0.17 / 0.90 / -2.7 m | 8.9 / 9.3%, 8.5 / 9.4% (8.6 / 9.5%) | 19%, 1% | 736, 737 m (738) | 0 failing; SEE TEXT (3 FAIL lines) | 0.329 m, 0 | 4,451; 3,949 of 5,640 half-sections |
| Chimney Rock Park Road (`chimney`) | +0.07 / 0.71 / +1.9 m | 7.9 / 8.6%, 8.3 / 14.2% (8.6 / 14.7%) | 38%, 0% | 263, 263 m (264) | 0 failing; SEE TEXT (6 FAIL lines) | 0.330 m, 0 | 1,019; 1,528 of 2,208 half-sections |
| NC 128, Mount Mitchell (`mtm`) | -0.33 / 0.56 / -1.6 m | 8.8 / 9.2%, 8.7 / 9.4% (8.7 / 9.7%) | 10%, 0% | 431, 435 m (435) | 0 failing; SEE TEXT (2 FAIL lines) | 0.339 m, 0 | not reported; 2,984 of 3,742 half-sections |
| Little Switzerland - Gillespie Gap loop (`swiss`) | +0.18 / 0.85 / +4.2 m | 8.0 / 9.5%, 7.6 / 8.5% (7.6 / 8.8%) | 25%, 0% | 472, 481 m (482) | 0 failing; SEE TEXT (1 FAIL lines) | -28.145 m, 115 | 2,263; 3,190 of 4,842 half-sections |
| Blowing Rock - Moses Cone loop (`brock`) | -0.39 / 0.96 / +3.2 m | 7.0 / 8.1%, 6.9 / 7.2% (6.9 / 7.4%) | 25%, 0% | 350, 366 m (367) | 0 failing; all ok | 0.350 m, 0 | 1,962; 2,224 of 4,938 half-sections |

After a re-bake the height error against the lidar is zero by construction (the same instrument), so it is not listed.

**NC 226 to Gillespie Gap (`gap`, GillespieGap).** Line: 4 stretches (132 m) moved onto the bench, 10 left alone, two of them because they are within 120 m of the finish (`--hold-ends 120`, after the first re-bake failed finish-play-check; see "Drive checks"); tightest corner 8.6 to 9.5 m. Built twice: the numbers and pictures here are the second build. The 10.0% clamp is gone: the climb from 2.6 to 5.9 km now holds 10 to 13% as the road does. Plots: [profile after](shots/lidar_audit/gap_profile_after.png), [plan after](shots/lidar_audit/gap_plan_after.png); pictures `shots/lidar_audit/gap_ba_*.jpg`; lab report `lidar_audit/stage_lab_gap_3dep.txt`.

**NC 215 from Beech Gap (`beech`, BeechGap).** Line: 2 stretches (88 m) moved (the shelf is 9 m wide here, so a stretch must be 2.45 m off to move), 14 left alone. **The sea-level strip west of 83 W in the far grid is gone** (it is 3DEP land now). The two OSM bridges the old bake dropped (8,652-8,668 m and 10,656-10,676 m) are carried straight across in height; they are not built as decks (see "Not done"). Plots: [profile after](shots/lidar_audit/beech_profile_after.png), [plan after](shots/lidar_audit/beech_plan_after.png); pictures `shots/lidar_audit/beech_ba_*.jpg`; lab report `lidar_audit/stage_lab_beech_3dep.txt`.

**Chimney Rock Park Road (`chimney`, ChimneyRock).** Line: 2 stretches (60 m) moved, 2 left alone; top hairpin 6.1 to 6.3 m. The real 13 to 15% pitch at 1,772-1,836 m is back (the old bake had 8.5%); the bake's steepest 4 m is 14.8%, under the self-test's 15%. Plots: [profile after](shots/lidar_audit/chimney_profile_after.png), [plan after](shots/lidar_audit/chimney_plan_after.png); pictures `shots/lidar_audit/chimney_ba_*.jpg`; lab report `lidar_audit/stage_lab_chimney_3dep.txt`.

**NC 128, Mount Mitchell (`mtm`, MtMitchell).** Line: 15 stretches (1,672 m) moved 2 to 5 m onto the bench, 5 left alone; the route is 15 m longer (7,461 to 7,476 m). This road's mapped line sat to one side of the shelf for long stretches. Plots: [profile after](shots/lidar_audit/mtm_profile_after.png), [plan after](shots/lidar_audit/mtm_plan_after.png); pictures `shots/lidar_audit/mtm_ba_*.jpg`; lab report `lidar_audit/stage_lab_mtm_3dep.txt`.

**Little Switzerland - Gillespie Gap loop (`swiss`, LittleSwitzerland).** A loop: the line is kept as mapped (the shelf reads over 2 m off it at 17 places). Heights straight through the tunnel (3,992-4,180 m), across the bridge at the start line and across the 40 m dip at 2,744-2,780 m, where the bare earth drops 5.9 m under the Parkway; road-level imagery from 60 m back shows no bridge parapet there, so it is carried as an embankment, not added as a deck. The one failing line in its lab report is the terrain audit reading the tunnel under the horizon stand-in mesh (see "Not done"); the first lab build of this stage compiled the code of another branch after a lock race and was discarded and run again. Plots: [profile after](shots/lidar_audit/swiss_profile_after.png), [plan after](shots/lidar_audit/swiss_plan_after.png); pictures `shots/lidar_audit/swiss_ba_*.jpg`; lab report `lidar_audit/stage_lab_swiss_3dep.txt`.

**Blowing Rock - Moses Cone loop (`brock`, BlowingRock).** A loop: the line is kept as mapped (22 places over 2 m off). Sigma 10 m here (8 m left one vertical curve under 230 m). The two short OSM bridges (1,124-1,148 m, 9,432-9,456 m) are carried straight across in height. **The US 221/321 bypass (7,944-9,432 m) is still built as a 6.4 m two-lane road** (see "Not done"). Plots: [profile after](shots/lidar_audit/brock_profile_after.png), [plan after](shots/lidar_audit/brock_plan_after.png); pictures `shots/lidar_audit/brock_ba_*.jpg`; lab report `lidar_audit/stage_lab_brock_3dep.txt`.

## Guardrails against the real road (round 2, 2026-11-10)

The lidar re-bake of NC 226A took away a quarter of the guard posts, and the after-picture at 9,360 m shows a verge that falls away with no rail. Is that wrong? **No: the real road has no rail there, or almost anywhere.**

Seventeen places on NC 226A were looked at on public road-level imagery (Google Street View, captures of May 2024 and, at the bottom, February 2025; reference only, nothing saved or copied): fourteen where the SRTM bake had a rail and the lidar bake has none, the 10,200 m fence, and two where both have one. (The three short stretches where the lidar bake has a rail the SRTM bake lacked, 4,068-4,112 m left, 10,812-10,836 m and 11,212-11,248 m right, were not looked at.) Stations are on the re-baked line.

| Place (m, side) | Real road | SRTM bake | Lidar bake | Lidar ground: drop within 5 / 10 / 20 m of the shelf edge | Bend |
|---|---|---|---|---|---|
| 264 L | **rail**: W-beam rail on the left, from about 260 m round the bend to at least 360 m | rail | rail | 3.2 / 5.2 / 9.6 m | straight |
| 1,964 L | none: grass verge falling into woods | rail (wrong) | none | 3.0 / 6.2 / 11.2 m | straight |
| 3,604 L | none: grass verge and a utility pole | rail (wrong) | none | 3.1 / 6.1 / 10.7 m | outside, R 84 m |
| 3,976 L | none: wide level verge with trees | rail (wrong) | none | 1.7 / 5.9 / 11.2 m | inside, R 111 m |
| 4,428 R | none: grass verge, trees | rail (wrong) | none | 3.2 / 4.7 / 9.3 m | straight |
| 4,788 R | none: grass verge, a curve sign | rail (wrong) | none | 2.2 / 3.6 / 5.6 m | straight |
| 4,920 R | none: grass verge, trees (view not sharp) | rail (wrong) | none | 3.4 / 6.7 / 12.0 m | inside, R 122 m |
| 5,744 R | none: grass verge falling away | rail (wrong) | none | 4.0 / 7.3 / 14.7 m | outside, R 112 m |
| 6,536 R | none: grass shoulder into the bend | rail (wrong) | none | 1.1 / 3.1 / 9.9 m | inside, R 34 m |
| 8,032 R | none: grass verge under trees | rail (wrong) | none | 3.8 / 8.8 / 13.2 m | inside, R 93 m |
| 8,592 R | none: a house and its lawn | rail (wrong) | none | 0.7 / 2.6 / 4.0 m | straight |
| 9,372 R | none: grass shoulder, trees | rail (wrong) | none | 3.5 / 6.2 / 12.3 m | outside, R 93 m |
| 9,464 R | none: grass verge | rail (wrong) | none | 2.1 / 4.3 / 7.2 m | inside, R 175 m |
| 10,588 R | none: cleared level ground (Feb 2025) | rail (wrong) | none | 0.6 / 1.1 / 1.3 m | straight |
| 10,216 R | none: a shed and a dirt yard at road level (Feb 2025) | none | none | 2.0 / 2.1 / 2.2 m | inside, R 67 m |
| 6,900 R | none: grass verge, steep woods below, into the 11 m switchback | rail (wrong) | rail (wrong) | 2.2 / 3.7 / 6.3 m | outside, R 40 m |
| 8,416 R | none: grass fill slope and a utility pole | rail (wrong) | rail (wrong) | 3.6 / 5.8 / 8.0 m | inside, R 103 m |
| **Right, of 17** | | **2** | **15** | | |

- **The real road has a guardrail at one of the seventeen**: on the left through the first bend below the village, from about 260 m to at least 360 m (its lower end was not seen). Everywhere else it is a grass verge and trees, including drops of 10 to 15 m within 20 m of the road.
- **The SRTM bake was wrong at fifteen.** Its plan walled 11.3 km of the 23.8 km of road edge (the whole downhill side) because 30 m ground carries the hillside's slope straight through the road, so five metres out is always "a critical fall".
- **The lidar bake is right at fourteen without help and fifteen with the one surveyed rail.** It is still wrong at 6,900 m and 8,416 m right, where the game's fall warrant puts a rail and the real road has none.
- **The fence at 10,200 m** is rightly gone: the real ground there is a yard at road level.

**No rule on the land reproduces the real rails.** The lidar section at the one real rail (3 m down within 5 m of the shelf, 5 to 7 m within 10 m, 10 to 14 m within 20 m, inside of a bend) is the section at 1,964 L, 3,604 L (outside of a bend), 5,744 R (outside of a bend, 14.7 m in 20 m), 8,032 R and 9,372 R, none of which has a rail. Drop height, slope beyond the shelf, fill or cut side and the side of the bend were all tried; nothing separates the one from the dozen. Where NCDOT put a rail on this road is not a function of the ground, so it cannot be warranted by a rule that reads the ground. It has to be looked at.

What was built instead:

- **Surveyed rails.** `Art/<stage>/<pre>_rails.json` lists spans the real road has (metres along the route, side, what was seen). `PlanStageRoadside` walls them in addition to its own warrant (reason 8). NC 226A has the one span, 256-372 m left; the plan now walls it (it had been open).
- **The plan as a table**, and a way to get it in a minute: the lab build and the new `StageLab.Plan` (no scene build) write `PSXRacing_roadside_<id>.csv`, one row per station with what each side is and why. Both plans of NC 226A are in `Docs/lidar_audit/rails/`; `tools/roads/rails_compare.py` scores a plan against the surveyed places, `rails_features.py` prints the lidar section at any station.
- **The fall warrant was left as it is.** It is the owner's rule of 2026-09-13 (a barrier where a slope or drop cannot be graded recoverable), every stage audit polices it ("edge falls: none unguarded"), and taking rails off real 10 m drops is a decision about how the game plays, not a correction of data.

**The decision this leaves for the owner.** The game still walls 9.3 km of NC 226A's edge; the real road walls perhaps a few hundred metres. If the stage should look like the photographs, most of that rail has to go, and then a car that leaves the road goes down the mountain as it would in life. The tools to do it are here (survey list, plan table); the choice is not mine to make.

Only NC 226A was surveyed. The other seven stages have no survey list; their rails are the fall warrant's.

## Drive checks (round 2, 2026-11-10)

All in the lab sandbox on the re-baked scenes, hidden (`-batchmode`, `-NoWatch`), with this branch's code. Reports are in `Docs/lidar_audit/drive/`.

**`race-play-check`, every stage raced to the finish** (the harness drives the player; three AI rivals; the hour's own traffic; seed 0; three times game speed, physics at its fixed step; a 900 s cap).

| Venue | Raced | Rivals home of 3 (mean time) | Retired | Recoveries | Hard hits | The check's own failures |
|---|---|---|---|---|---|---|
| SwissNC226A | 678 s | 3 (629 s) | 0 | 3 | 2 | 1: a rival drove the wrong way for 5.5 s |
| BlueRidge | 241 s | 2 (239 s) | 1 | 0 | 1 | none |
| GillespieGap | 298 s | 3 (292 s) | 0 | 0 | 0 | none |
| BeechGap | 446 s | 1 (385 s) | 2 | 1 | 4 | 1: two rivals retired (at most one allowed) |
| ChimneyRock | 481 s | 3 (474 s) | 0 | 5 | 4 | 1: a rival drove the wrong way for 13.5 s |
| MtMitchell | 343 s | 3 (337 s) | 0 | 0 | 0 | none |
| LittleSwitzerland | 900 s (the cap) | 2 (371 s) | 1 | 0 | 2 | 1: the player's car drove the wrong way for 11 s |
| BlowingRock | 533 s | 3 (487 s) | 0 | 1 | 2 | 1: a rival drove the wrong way for 5.5 s |

Every stage loads, grids, and is driven end to end by AI on the new road: at least one rival finishes everywhere, all three on five stages. **Five of the eight races trip one line of the check each.** There is no baseline to set them against: the SRTM scenes were not raced before they were rebuilt, and the tools' own notes (`tools/unity-wait.ps1`) say a race's story varies between runs with the same seed. So these are recorded, not attributed. Beech Gap's two retirements and Little Switzerland's run to the time cap are the two worth a second run before anyone trusts those stages.

**The NC 226A thirds whose cut lines moved** (`SwissNC226AUpper`, `Middle`, `Lower`, `UpperRev`), same check: all three rivals home in each (267, 196, 242 and 202 s raced), nobody retired, **no failures**.

**`sprint-check`** (every sprint staged on its road, line to line): 72 ok, 0 FAIL, "EVERY SPRINT RUNS ON ITS LOOP, LINE TO LINE".

**`reverse-check`** (every reverse twin's grid): 36 ok, 0 FAIL, "ALL REVERSE RACES LINE UP".

**`finish-play-check`** (the car carried to 32 m before the flag and let roll; does the race end, and does the player keep the car): NC 226A, Mount Mitchell, Beech Gap and Chimney Rock pass (14 ok each). **Gillespie Gap failed it on its first re-bake** ("the car crosses the line and the race ends": still racing after 15 s). Cause: a 16 m stretch moved 2.2 m onto the bench 36 m before the flag, on the bend at the gap, put an S into the approach. The bake now has `--hold-ends <m>` (no stretch is moved within that distance of the start or finish line); Gillespie Gap was re-baked with `--hold-ends 120`, rebuilt, and passes (14 ok), with the stage audits still clean. Its full race above was run on the first build and not repeated on the second. Blue Ridge and the two loops were not put through this check.

Not run: any drive by a person, a player build, night or weather, and a second seed.

## Not done, not verified

Round 2 additions first.

- **Three re-bakes fail audit lines and are not release-ready.** Beech Gap: UNWARRANTED BARRIER at wp 59 right and wp 681 left (a rock top 0.18 m over the road two stations into a cut run; a cut face at a wall's buried terminal with level land behind). Chimney Rock: EDGE FACE at wp 1019 L, 661 R, 1021 L (0.08 to 0.12 m in 0.13 m, about 7 m past the tarmac edge) and UNWARRANTED BARRIER at wp 0 R (the end pad 0.29 m under the road, 1 cm inside the pocket band). Mount Mitchell: EDGE FACE at wp 1544-1545 R (0.07 m against 0.06). All are the builder's cut-run ends and lattice rise holds meeting new ground a few centimetres the wrong side of a threshold; they were not fixed. Whether the SRTM scenes of these stages failed anything was not run (the old scenes were photographed, not audited).
- **Little Switzerland's terrain audit line** ("road stands -28.1 m clear ... StandIn_-2_0; 115 probes buried") is the 188 m tunnel under the PC horizon's stand-in mesh. `TerrainAudit` has no tunnel exemption for that mesh. Not caused by the bake; not fixed; not run on the SRTM scene.
- **The Blowing Rock bypass is not built at its real width.** The builder has one `RoadWidth` for a whole track (`track.roadWidth`, read in about 80 places: ribbon, kerb strip, shoulder profile, wall and rail offsets, lane paint, posts, end pads, the AI line's lateral offsets, the minimap stroke). A stretch at another width and lane count needs a width per station through all of them, plus a second carriageway or a median for the divided part. That is a builder feature, not a bake, and it was not started.
- **No short bridge was added as a deck.** A span must be longer than 52 m (two 26 m approach ramps: `TrackCatalog.BridgeRampM`, enforced by the self-test), and the real ones are 16 to 24 m (NC 215 at 8,652-8,668 and 10,656-10,676 m; Blowing Rock at 1,124-1,148 and 9,432-9,456 m). Their heights are carried straight across, so the road no longer dips into the creek, and the land under them is an embankment. Building them as decks needs the ramp to scale with the span.
- **The unmapped dip on the Little Switzerland loop (2,744-2,780 m)**: one road-level view from 60 m back shows the Parkway on grass with no parapet; not confirmed as a bridge, carried as an embankment.
- **Guardrails were surveyed on NC 226A only**, at 17 places, not along its whole length; the other seven stages have no survey. The fall warrant still walls about 9.3 km of NC 226A's edge where the real road has almost none (the owner's call, see "Guardrails").
- **The title screen's flight over Blue Ridge was not rendered** (no batch harness).
- **Short moved stretches can put an S into a straight.** The bake moves a stretch only where it is over the threshold and fades the move to nothing 24 m either side, even when the line beside it is still 1.9 m off. That is what broke Gillespie Gap's finish approach. `--hold-ends` keeps it away from the lines; the proper cure (grow each moved stretch outward until the offset is small, then fade) was not written, because it would change the NC 226A, Mount Mitchell, Beech Gap and Chimney Rock bakes that are already built. NC 226A has such a stretch at 44-68 m, just past its start line; its races and its finish check pass.
- **The race check was not repeated** on the second builds of NC 226A (with the surveyed rail) and Gillespie Gap (ends held); their stage audits and, for Gillespie Gap, the finish check were.
- **Beech Gap's road renders black** in two of its chase pictures (10,668 and 6,080 m), before and after alike; not looked into.
- Before-pictures of the six round-2 stages are of the scenes that were in the sandbox (built 2026-11-09 18:27-19:07 from the SRTM bakes by the PC scene pass), not of a fresh build on this branch's code.

Still true from round 1:

- **Bridge deck heights are interpolated**, not measured: bare-earth lidar has no decks.
- **The pavement's own width and edge** cannot be read from a bare-earth DEM. No width was changed.
- **Overlooks and viewpoints** were not checked: the cached Overpass files hold highway ways only. The game builds none; the lidar's wide spots per stage are where the real ones are.
- **The line stretches left alone** on every stage (junction mouths, pull-offs, no clear shelf) were not checked against imagery.
- **`<pre>_dem_wide.bytes` stays SRTM** on every stage (240 m cells).
- **The datum question** (OSM WGS84 against the DEM's NAD83, about 1 m) is left open; no shift is applied.

## How it was measured

- **Road line.** At every 4 m waypoint, a cross-section of the USGS 1 m lidar DEM (NC_Phase5_2018_A18, bare earth), 30 m either side, every 0.5 m. The road is the shelf: the run of ground under 12% cross slope, 4.5 to 18 m wide, that the mapped line sits on or within 3 m of (`tools/roads/bench.mjs`). Its middle against the line is the offset. A running median along the road (40 m) drops driveways and side roads. Where the ground beside the road is as level as the road (valley floor, lots, junction mouths) there is no shelf to find: 10 to 40% of stations per stage, shown grey in the plan plots.
- **What that can and cannot say.** The shelf is pavement plus shoulders plus ditch; its middle is the pavement's middle to about a metre. Offsets under 1.5 m are noise. A filtered offset over 2 m held for 16 m or more is a real displacement of the mapped line.
- **Datum.** OSM is nominally WGS84, the DEMs NAD83; here they differ by about 1.0 m (0.6 m east, 0.8 m south). No shift was applied, because the imagery OSM was traced from has its own unrecorded frame. The mean offsets found are under 0.4 m on six stages (Mount Mitchell +0.7 m east / -0.5 m north, Blowing Rock +0.3 / -0.5).
- **Height.** The lidar ground on the shelf (median within 1.5 m of its middle), a straight line across the stage's bridge and tunnel spans, across OSM bridge ways the bake dropped for being under 56 m, and across any place the bare earth drops more than 1.2 m under the road for a few tens of metres (a deck nobody mapped). Grades are read after an 8 m gaussian so two lidar posts 4 m apart are not read as a slope.
- **Terrain.** The 1/3 arc-second 3DEP DEM sampled at every point of the game's own near (12 m) and far (60 m) lattices: full resolution (about 9 m) for the near grid, the file's 4x overview (about 37 m) for the far grid. The overview's georeference was checked against the full-resolution data (best fit at zero shift). The 4x overview differs from the full data by about 2 m RMS on these slopes, so a far-grid RMS of 2.5 m is the floor of the comparison, not an error in the game.
- **Structures and width.** OSM `bridge`, `tunnel`, `lanes`, `width`, `oneway` tags of the ways lying on the route (within 3 m for 60% of their length). The Overpass files in the cache hold highway ways only: overlooks and viewpoints (nodes) are not in them and were not checked.
- Tools: `tools/roads/audit_3dep.mjs` (numbers, `Docs/lidar_audit/<pre>_audit.json`), `audit_3dep_plots.py` (pictures), `audit_3dep_report.py` (the tables below).

## Width, in one place

| Stage | Game | Lidar shelf, narrowest tenth / median | Reading |
|---|---|---|---|
| NC 226A (`swa`) | 6.1 m | 6.0 / 7.0 m | right |
| NC 226 Gillespie Gap (`gap`) | 6.7 m | 7.0 / 9.0 m | right; a primary route with real shoulders and 11 wide spots |
| NC 128 Mount Mitchell (`mtm`) | 6.1 m | 6.0 / 7.5 m | right |
| NC 215 Beech Gap (`beech`) | 6.1 m | 7.5 / 9.0 m | pavement plausible; the shelf is 1.5 m wider than NC 226A's all the way down |
| Chimney Rock (`chimney`) | 6.1 m | 5.5 / 7.0 m | right; OSM marks 1776-2024 m one-way (the split pair below the upper lot) |
| Blue Ridge Parkway (`brp`) | 6.4 m | 9.5 / 11.0 m | pavement right (20-22 ft); the grass shoulders are about 2 m a side, the game's verge is 1.15 m |
| Little Switzerland loop (`swiss`) | 6.4 m | 6.5 / 8.5 m | right |
| Blowing Rock loop (`brock`) | 6.4 m | 8.0 / 11.0 m | wrong on the bypass, 7944-9432 m (see fault 4) |

The lidar cannot separate asphalt from a gravel shoulder, so no stage's paved width was changed. What the game's roadside depends on if a width is ever changed: `TrackDef.roadWidth` feeds the ribbon, the kerb strip, `StageWallOffsetFor` (guard walls and rails at half width + 1.15 m), the shoulder and bank grading, the lane paint (`LaneAudit`), the AI line and the 1v1 restage check (5.2 m beside a rival), the track map stroke, and each bake's hairpin floor (`minCornerR`: half width + 1.5 m).

## Terrain, in one place

- Near grids (12 m): 1.8 to 3.0 m RMS everywhere, no bias (-0.5 to +0.2 m). Within 100 m of the road 1.3 to 2.6 m RMS, worst single points 6 to 13 m. That is 30 m data failing to hold sharp shapes, not misplaced land: the worst points are crests and cliff lines. Chimney Rock has the largest (the cliff under the Chimney, game 39 m too high at one lattice point 100-500 m from the road, 6 m too high as a 240 m block mean); Grandfather Mountain's crags are 32 m low at one point 500 m off the Parkway; Mount Mitchell's ridge 17 m low.
- Far grids (60 m): 2.5 to 3.0 m RMS, the comparison's floor. No missing gorge and no ridge 40 m low on any stage. The exceptions are quarries dug since the game's data was made: the Woodlawn quarry 5.6 km south-east of the foot of NC 226A (game 67 m high; it is in the far grids of `swa`, `gap` and `swiss`), one near Boone on Blowing Rock's grid (59 m) and one near Swannanoa on Mount Mitchell's (41 m). 6 to 11 lattice points each out of about 125,000.
- Beech Gap's hole (fault 2) is the one wrong landform, and it is at the far grid's edge.

## Downloads

All from the public USGS bucket `prd-tnm.s3.amazonaws.com`, by HTTP range request, with the User-Agent `psx-racing-mountain-bake/1.0 (game map data; USGS 3DEP elevation)`, on 2026-11-09. **149.6 MB in all** (the owner's ceiling was about 200 MB): 105.2 MB of 1 m lidar DEM in 176 tiles of 512 x 512 m, 42.8 MB of 1/3" DEM in 56 tiles, 1.6 MB of file headers and one check tile. The cache and its ledger (every URL, Last-Modified, ETag, tile and byte count) are in the main checkout, git-ignored: `tools/roads/cache/3dep/sources.json`.

| Source file | Tiles | Read | Of |
|---|---|---|---|
| `StagedProducts/Elevation/13/TIFF/current/n36w083/USGS_13_n36w083.tif` | 39 | 31.0 MB | 482 MB |
| `.../13/TIFF/current/n37w082/USGS_13_n37w082.tif` | 13 | 9.4 MB | 477 MB |
| `.../13/TIFF/current/n36w082/USGS_13_n36w082.tif` | 2 | 1.8 MB | 482 MB |
| `.../13/TIFF/current/n36w084/USGS_13_n36w084.tif` | 2 | 0.6 MB | 482 MB |
| `StagedProducts/Elevation/1m/Projects/NC_Phase5_2018_A18/TIFF/USGS_1M_17_x40y397_NC_Phase5_2018_A18.tif` (NC 226A, NC 226, the loop) | 59 | 38.2 MB | 342 MB |
| `..._x32y391_...` (NC 215) | 32 | 19.5 MB | 312 MB |
| `..._x42y400_...` (Parkway, Linn Cove) | 24 | 13.5 MB | 306 MB |
| `..._x38y396_...` (Mount Mitchell) | 21 | 12.6 MB | 325 MB |
| `..._x38y393_...` (Chimney Rock) | 10 | 6.9 MB | 345 MB |
| `..._x43y401_...`, `..._x43y400_...`, `..._x44y401_...` (Blowing Rock) | 30 | 14.4 MB | 968 MB |

The corridor read is 60 m either side of each route. Nothing more is needed to re-bake the other six stages. Whole 1 m tiles for all eight would be about 2.6 GB.

## Running it again

```
set PSX_ROADS_CACHE=C:\Users\mcgee\PSX Racing\tools\roads\cache
node tools/roads/audit_3dep.mjs all --offline            numbers -> Docs/lidar_audit/<pre>_audit.json
py -I tools/roads/audit_3dep_plots.py Docs/lidar_audit Docs/shots/lidar_audit --sheet sheet.png
py -I tools/roads/audit_3dep_report.py Docs/lidar_audit Docs/mountain_lidar_audit.md
node tools/roads/bake_3dep.mjs <pre> [--keep-line]       re-bake one stage into Assets
node tools/roads/bake_3dep.mjs <pre> --check             re-derive and compare, no network
node tools/roads/audit_3dep.mjs <pre> --res tools/roads/srtm_bakes/<pre>_srtm --art tools/roads/srtm_bakes/<pre>_srtm    audit the old bake
```

`Docs/lidar_audit/*.json` are the audits of the SRTM bakes (the "before" of every stage); `Docs/lidar_audit/after/` holds the audits of the two re-baked stages. Running `audit_3dep.mjs all` now would overwrite the `swa` and `brp` "before" files with "after" numbers: pass `--out`.

## Per stage

<!-- STAGES:BEGIN -->
### NC 226A, Little Switzerland (`swa`, SwissNC226A)

11.92 km; baked by `fetch_road.mjs swiss226a`. Plots: [profile](shots/lidar_audit/swa_profile.png), [plan and offsets](shots/lidar_audit/swa_plan.png).

**Line.** Bench found at 77% of stations. Offset of the bench centre from the game centreline: median 0.75 m, p95 3.0 m, worst single station 6.2 m (at 4084 m). Mean offset +0.07 m east, -0.25 m north. 34 stretches where the filtered offset is over 2 m, 1412 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 9320 - 9348 | 32 m | 4.9 m | bench is to the right of the line |
| 11136 - 11196 | 64 m | 4.7 m | bench is to the left of the line |
| 9692 - 9736 | 48 m | 4.5 m | bench is to the right of the line |
| 8880 - 8904 | 28 m | 4.3 m | bench is to the left of the line |
| 11696 - 11752 | 60 m | 4.3 m | bench is to the right of the line |
| 5452 - 5500 | 52 m | 4.1 m | bench is to the right of the line |

**Height.** Real 1043.5 m at the first waypoint, 469.1 m at the last (game 1041.3, 469.4). Game minus real: bias +0.33 m, RMS 0.82 m, p95 1.68 m, worst +2.95 m at 10972 m. The bake's raw source sampled at the waypoints, before its smoothing: bias +0.32 m, RMS 1.35 m, worst +6.5 m. Climb 3 m real / 0 game; descent 577 / 572.
No stretch is more than 3 m off once the bias is taken out.

**Grade** (over an 8 m gaussian). Real: mean 4.86%, p95 7.90%, max 9.22% at 9356 m. Game: mean 4.79%, p95 7.47%, max 8.55% at 9472 m. Over 8.5%: 152 m of the real road, 32 m of the game's. The game's grade is more than 1% of grade off the real one on 60 stretches, 2852 m (24% of the route); RMS difference 0.92% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 11004 - 11132 | 132 m | -1.1% | -4.2% |
| 9672 - 9724 | 56 m | -7.2% | -4.4% |
| 5432 - 5484 | 56 m | -7.8% | -5.1% |
| 6684 - 6732 | 52 m | -6.8% | -4.1% |
| 8204 - 8284 | 84 m | -2.1% | -4.7% |
| 11380 - 11508 | 132 m | +2.0% | -0.5% |

**Width.** Game 6.1 m. Lidar bench (pavement + shoulders + any level verge): median 7 m, narrowest tenth 6 m, widest tenth 10 m. OSM `lanes`: 2: 11328 m, (no way matched): 596 m; no `width` tag on any way of the route.
6 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 4-44 m right (18.5 m); 1292-1312 m left (15 m); 1348-1380 m left (15.5 m); 9704-9736 m right (18 m); 9896-9916 m right (19 m); 9940-9960 m right (15 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 4952 | -0.51 m | 2.31 m | +7.8 m | 1208, -1673 |
| near (12 m) | 30 - 100 m | 10546 | -0.33 m | 2.33 m | -9.2 m | 1352, -845 |
| near (12 m) | 100 - 500 m | 49821 | -0.18 m | 2.26 m | -9.7 m | 848, 367 |
| near (12 m) | 500 - edge m | 186711 | -0.16 m | 2.11 m | +14.6 m | 8, -3125 |
| far (60 m) | 0 - 100 m | 612 | -0.62 m | 2.88 m | +10.0 m | -736, 1399 |
| far (60 m) | 100 - 500 m | 1999 | -0.11 m | 2.71 m | -9.3 m | 1544, 139 |
| far (60 m) | 500 - 2300 m | 10319 | -0.07 m | 2.52 m | -12.2 m | -2296, 3139 |
| far (60 m) | 2300 - edge m | 118050 | +0.03 m | 2.56 m | +66.7 m | 4784, -5561 |

Largest wrong landforms (block means): near grid 240 m block at (-76, -1001), 577 m from the road near station 6424 m: game +4.5 m; near grid 240 m block at (-316, -1001), 583 m from the road near station 4992 m: game +3.5 m; near grid 240 m block at (-1516, -281), 67 m from the road near station 4056 m: game +3.0 m; far grid 600 m block at (4904, -5381), 4177 m from the road near station 11920 m: game +4.9 m; far grid 600 m block at (-2296, -8381), 7051 m from the road near station 11272 m: game +2.4 m; far grid 600 m block at (704, -4781), 2550 m from the road near station 11752 m: game +2.3 m.

**Structures.** Stage bridges: none. Stage tunnels: none. OSM bridge ways on the route: none. OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### NC 226 to Gillespie Gap (`gap`, GillespieGap)

6.69 km; baked by `fetch_road.mjs gillespie`. Plots: [profile](shots/lidar_audit/gap_profile.png), [plan and offsets](shots/lidar_audit/gap_plan.png).

**Line.** Bench found at 71% of stations. Offset of the bench centre from the game centreline: median 1.00 m, p95 3.8 m, worst single station 6.2 m (at 4724 m). Mean offset -0.16 m east, -0.10 m north. 14 stretches where the filtered offset is over 2 m, 968 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 4636 - 4980 | 348 m | 4.7 m | bench is to the left of the line |
| 1044 - 1108 | 68 m | 4.5 m | bench is to the left of the line |
| 3664 - 3776 | 116 m | 4.3 m | bench is to the left of the line |
| 5764 - 5812 | 52 m | 3.7 m | bench is to the right of the line |
| 5448 - 5472 | 28 m | 2.7 m | bench is to the left of the line |
| 5960 - 5980 | 24 m | 2.6 m | bench is to the right of the line |

**Height.** Real 441.6 m at the first waypoint, 852.5 m at the last (game 442.6, 852.9). Game minus real: bias -1.42 m, RMS 2.37 m, p95 4.40 m, worst -4.92 m at 3792 m. The bake's raw source sampled at the waypoints, before its smoothing: bias +0.24 m, RMS 1.47 m, worst +6.0 m. Climb 415 m real / 411 game; descent 4 / 0.
Stretches more than 3 m off after the bias is taken out: 20-168 m (+4.8 m); 3692-3828 m (-3.5 m); 4940-5044 m (-3.3 m); 5704-5756 m (-3.2 m); 4504-4532 m (-3.1 m).

**Grade** (over an 8 m gaussian). Real: mean 6.26%, p95 11.48%, max 13.03% at 3656 m. Game: mean 6.14%, p95 10.00%, max 10.00% at 3672 m. Over 8.5%: 3188 m of the real road, 3380 m of the game's. The game's grade is more than 1% of grade off the real one on 36 stretches, 1728 m (26% of the route); RMS difference 1.16% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 0 - 52 | 56 m | -5.8% | +0.4% |
| 6012 - 6048 | 40 m | -0.6% | +4.2% |
| 5752 - 5856 | 108 m | +5.6% | +10.0% |
| 4540 - 4596 | 60 m | +6.6% | +10.0% |
| 6560 - 6616 | 60 m | -2.0% | +1.1% |
| 6280 - 6320 | 44 m | +4.4% | +7.4% |

**Width.** Game 6.7 m. Lidar bench (pavement + shoulders + any level verge): median 9 m, narrowest tenth 7 m, widest tenth 16 m. OSM `lanes`: 2: 4780 m, (no way matched): 1908 m; no `width` tag on any way of the route.
11 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 1044-1084 m left (18.5 m); 3396-3428 m left (16.5 m); 3484-3520 m right (19 m); 3556-3604 m right (19 m); 3728-3748 m left (19 m); 4292-4312 m right (18.5 m); 4348-4428 m right (18 m); 4472-4572 m left (19 m); 4832-4852 m left (16 m); 4960-4980 m left (19 m); 5960-5980 m right (19 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 2781 | -0.65 m | 1.95 m | -7.4 m | -351, 1286 |
| near (12 m) | 30 - 100 m | 6318 | -0.74 m | 1.96 m | -8.1 m | -639, 1358 |
| near (12 m) | 100 - 500 m | 37302 | -0.45 m | 2.08 m | -11.5 m | -855, 890 |
| near (12 m) | 500 - edge m | 133278 | -0.22 m | 2.05 m | +12.4 m | 1341, 2090 |
| far (60 m) | 0 - 100 m | 358 | -0.98 m | 2.30 m | -8.0 m | 285, -82 |
| far (60 m) | 100 - 500 m | 1506 | -0.37 m | 2.59 m | +10.7 m | 945, -2722 |
| far (60 m) | 500 - 2300 m | 9597 | -0.17 m | 2.46 m | -11.7 m | 1305, -802 |
| far (60 m) | 2300 - edge m | 110899 | -0.04 m | 2.59 m | +49.7 m | 825, -6502 |

Largest wrong landforms (block means): near grid 240 m block at (-735, 1418), 14 m from the road near station 5936 m: game -2.7 m; near grid 240 m block at (1425, -742), 1178 m from the road near station 3352 m: game -2.5 m; near grid 240 m block at (-15, 1418), 52 m from the road near station 5216 m: game -2.5 m; far grid 600 m block at (885, -6322), 3077 m from the road near station 0 m: game +4.9 m; far grid 600 m block at (-6915, -5122), 7175 m from the road near station 48 m: game +2.6 m; far grid 600 m block at (8085, 5078), 8481 m from the road near station 5104 m: game -2.5 m.

**Structures.** Stage bridges: none. Stage tunnels: none. OSM bridge ways on the route: none. OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### NC 128, Mount Mitchell (`mtm`, MtMitchell)

7.47 km; baked by `fetch_road.mjs mtmitchell`. Plots: [profile](shots/lidar_audit/mtm_profile.png), [plan and offsets](shots/lidar_audit/mtm_plan.png).

**Line.** Bench found at 85% of stations. Offset of the bench centre from the game centreline: median 1.25 m, p95 3.5 m, worst single station 6.5 m (at 1540 m). Mean offset +0.68 m east, -0.47 m north. 19 stretches where the filtered offset is over 2 m, 1856 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 1520 - 1564 | 48 m | 4.8 m | bench is to the right of the line |
| 3756 - 3828 | 76 m | 4.0 m | bench is to the right of the line |
| 3864 - 4016 | 156 m | 4.0 m | bench is to the right of the line |
| 4388 - 4592 | 208 m | 3.7 m | bench is to the right of the line |
| 4636 - 4788 | 156 m | 3.6 m | bench is to the right of the line |
| 1048 - 1152 | 108 m | 3.5 m | bench is to the left of the line |

**Height.** Real 1573.1 m at the first waypoint, 2004.7 m at the last (game 1574.1, 2003.5). Game minus real: bias -0.33 m, RMS 0.56 m, p95 1.13 m, worst -1.59 m at 6604 m. The bake's raw source sampled at the waypoints, before its smoothing: bias -0.32 m, RMS 0.98 m, worst -3.6 m. Climb 433 m real / 430 game; descent 2 / 1.
No stretch is more than 3 m off once the bias is taken out.

**Grade** (over an 8 m gaussian). Real: mean 5.83%, p95 8.74%, max 9.42% at 7048 m. Game: mean 5.78%, p95 8.85%, max 9.22% at 6892 m. Over 8.5%: 624 m of the real road, 612 m of the game's. The game's grade is more than 1% of grade off the real one on 18 stretches, 760 m (10% of the route); RMS difference 0.63% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 16 - 64 | 52 m | +6.2% | +3.5% |
| 7428 - 7460 | 36 m | +4.6% | +2.0% |
| 3860 - 3892 | 36 m | +7.0% | +4.7% |
| 6404 - 6484 | 84 m | +7.4% | +5.7% |
| 5500 - 5536 | 40 m | -1.1% | +0.6% |
| 5620 - 5652 | 36 m | +4.8% | +3.1% |

**Width.** Game 6.1 m. Lidar bench (pavement + shoulders + any level verge): median 7.5 m, narrowest tenth 6 m, widest tenth 11 m. OSM `lanes`: 2: 6984 m, (no way matched): 236 m, (untagged): 248 m; no `width` tag on any way of the route.
2 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 3716-3744 m right (17 m); 7244-7268 m right (18 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 3099 | -0.22 m | 1.41 m | -5.9 m | -352, -2961 |
| near (12 m) | 30 - 100 m | 7173 | -0.24 m | 1.53 m | -5.5 m | -304, -849 |
| near (12 m) | 100 - 500 m | 42177 | -0.41 m | 2.17 m | -14.4 m | -1204, -2049 |
| near (12 m) | 500 - edge m | 179167 | -0.02 m | 2.70 m | -17.4 m | -1396, -1881 |
| far (60 m) | 0 - 100 m | 408 | +0.24 m | 1.69 m | -5.7 m | -460, -801 |
| far (60 m) | 100 - 500 m | 1698 | -0.38 m | 2.37 m | +11.5 m | -40, 2079 |
| far (60 m) | 500 - 2300 m | 10480 | -0.05 m | 2.96 m | -14.7 m | 2300, 2319 |
| far (60 m) | 2300 - edge m | 117890 | +0.12 m | 3.02 m | +41.3 m | -7720, -10881 |

Largest wrong landforms (block means): near grid 240 m block at (-640, 3339), 1494 m from the road near station 6096 m: game +4.6 m; near grid 240 m block at (-160, 2379), 435 m from the road near station 6096 m: game +4.4 m; near grid 240 m block at (-1840, -1941), 973 m from the road near station 1272 m: game -4.4 m; far grid 600 m block at (-7660, -11121), 10576 m from the road near station 0 m: game +5.1 m; far grid 600 m block at (-5860, -8721), 7593 m from the road near station 0 m: game +3.9 m; far grid 600 m block at (-2860, 3879), 3301 m from the road near station 6088 m: game +3.5 m.

**Structures.** Stage bridges: none. Stage tunnels: none. OSM bridge ways on the route: none. OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### NC 215 from Beech Gap (`beech`, BeechGap)

11.28 km; baked by `fetch_road.mjs nc215`. Plots: [profile](shots/lidar_audit/beech_profile.png), [plan and offsets](shots/lidar_audit/beech_plan.png).

**Line.** Bench found at 90% of stations. Offset of the bench centre from the game centreline: median 0.75 m, p95 3.0 m, worst single station 6.2 m (at 7488 m). Mean offset -0.13 m east, -0.12 m north. 20 stretches where the filtered offset is over 2 m, 1292 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 4388 - 4464 | 80 m | 4.9 m | bench is to the right of the line |
| 10788 - 10860 | 76 m | 4.8 m | bench is to the right of the line |
| 3136 - 3340 | 208 m | 4.7 m | bench is to the left of the line |
| 2276 - 2328 | 56 m | 4.5 m | bench is to the right of the line |
| 7432 - 7508 | 80 m | 4.1 m | bench is to the right of the line |
| 3532 - 3600 | 72 m | 3.4 m | bench is to the left of the line |

**Height.** Real 1622.8 m at the first waypoint, 887.7 m at the last (game 1623.1, 887.4). Game minus real: bias -0.17 m, RMS 0.90 m, p95 1.86 m, worst -2.69 m at 144 m. The bake's raw source sampled at the waypoints, before its smoothing: bias -0.17 m, RMS 1.63 m, worst +7.5 m. Climb 1 m real / 0 game; descent 736 / 736.
No stretch is more than 3 m off once the bias is taken out.

**Grade** (over an 8 m gaussian). Real: mean 6.54%, p95 8.57%, max 9.50% at 6056 m. Game: mean 6.53%, p95 8.91%, max 9.35% at 5260 m. Over 8.5%: 732 m of the real road, 1468 m of the game's. The game's grade is more than 1% of grade off the real one on 32 stretches, 2176 m (19% of the route); RMS difference 0.84% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 10652 - 10688 | 40 m | +2.4% | -1.1% |
| 0 - 104 | 108 m | +0.8% | -2.5% |
| 10160 - 10240 | 84 m | -8.4% | -5.5% |
| 8636 - 8740 | 108 m | +0.7% | -2.1% |
| 10612 - 10644 | 36 m | -4.8% | -2.1% |
| 192 - 364 | 176 m | -7.0% | -4.6% |

**Width.** Game 6.1 m. Lidar bench (pavement + shoulders + any level verge): median 9 m, narrowest tenth 7.5 m, widest tenth 13.5 m. OSM `lanes`: (no way matched): 908 m, (untagged): 10368 m; no `width` tag on any way of the route.
5 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 2280-2308 m right (18 m); 2392-2416 m left (15 m); 4420-4456 m right (18 m); 9336-9356 m right (18.5 m); 10804-10852 m right (19 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 4705 | +0.03 m | 2.47 m | -12.6 m | -283, 2689 |
| near (12 m) | 30 - 100 m | 10900 | -0.05 m | 2.26 m | -11.9 m | -499, 2293 |
| near (12 m) | 100 - 500 m | 59468 | -0.36 m | 2.32 m | -10.0 m | 533, 3673 |
| near (12 m) | 500 - edge m | 275323 | -0.59 m | 2.25 m | -22.1 m | -1531, 3061 |
| far (60 m) | 0 - 100 m | 632 | -0.29 m | 2.92 m | -11.1 m | 2237, -2219 |
| far (60 m) | 100 - 500 m | 2369 | -0.31 m | 2.82 m | -9.9 m | 317, 3061 |
| far (60 m) | 500 - 2300 m | 13060 | -0.46 m | 2.61 m | -12.9 m | -2863, -2279 |
| far (60 m) | 2300 - edge m | 129101 | -124.93 m | 394.87 m | -1834.9 m | -8083, 11881 |

Largest wrong landforms (block means): near grid 240 m block at (-1603, 3661), 1279 m from the road near station 584 m: game -4.8 m; near grid 240 m block at (-1843, 3661), 1507 m from the road near station 592 m: game -4.0 m; near grid 240 m block at (317, 3181), 160 m from the road near station 120 m: game -3.9 m; far grid 600 m block at (-9103, 12241), 12286 m from the road near station 376 m: game -1695.1 m; far grid 600 m block at (-8503, 12241), 11868 m from the road near station 344 m: game -1664.2 m; far grid 600 m block at (-9703, 12241), 12717 m from the road near station 480 m: game -1626.8 m.

**Structures.** Stage bridges: none. Stage tunnels: none. OSM bridge ways on the route: 8652-8668 m (ground 4.08 m below the deck line; NOT in the stage); 10656-10660 m (ground 3.5 m below the deck line; NOT in the stage); 10664-10676 m (ground 3.43 m below the deck line; NOT in the stage). OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### Chimney Rock Park Road (`chimney`, ChimneyRock)

4.42 km; baked by `fetch_road.mjs chimney`. Plots: [profile](shots/lidar_audit/chimney_profile.png), [plan and offsets](shots/lidar_audit/chimney_plan.png).

**Line.** Bench found at 59% of stations. Offset of the bench centre from the game centreline: median 0.75 m, p95 2.5 m, worst single station 6.0 m (at 2580 m). Mean offset +0.15 m east, -0.22 m north. 4 stretches where the filtered offset is over 2 m, 164 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 2552 - 2624 | 76 m | 3.9 m | bench is to the left of the line |
| 1384 - 1424 | 44 m | 3.2 m | bench is to the right of the line |
| 76 - 100 | 28 m | 2.6 m | bench is to the right of the line |
| 2376 - 2388 | 16 m | 2.1 m | bench is to the right of the line |

**Height.** Real 324.9 m at the first waypoint, 587.4 m at the last (game 324.8, 586.5). Game minus real: bias +0.07 m, RMS 0.71 m, p95 1.49 m, worst +1.91 m at 1116 m. The bake's raw source sampled at the waypoints, before its smoothing: bias +0.06 m, RMS 1.12 m, worst +4.1 m. Climb 263 m real / 262 game; descent 1 / 0.
No stretch is more than 3 m off once the bias is taken out.

**Grade** (over an 8 m gaussian). Real: mean 5.98%, p95 8.51%, max 14.74% at 1796 m. Game: mean 5.95%, p95 7.87%, max 8.55% at 1780 m. Over 8.5%: 228 m of the real road, 20 m of the game's. The game's grade is more than 1% of grade off the real one on 37 stretches, 1672 m (38% of the route); RMS difference 1.41% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 1772 - 1836 | 68 m | +14.7% | +8.5% |
| 3952 - 4016 | 68 m | -0.6% | +4.7% |
| 1068 - 1096 | 32 m | +1.1% | +5.3% |
| 4372 - 4412 | 44 m | +7.0% | +3.2% |
| 4032 - 4064 | 36 m | +10.1% | +7.0% |
| 2184 - 2216 | 36 m | +9.0% | +6.1% |

**Width.** Game 6.1 m. Lidar bench (pavement + shoulders + any level verge): median 7 m, narrowest tenth 5.5 m, widest tenth 11 m. OSM `lanes`: (untagged): 4152 m, (no way matched): 264 m; no `width` tag on any way of the route.
OSM says these stretches are not a plain two-way two-lane road: 1776-1984 m (lanes untagged, one-way, Chimney Rock Park Road); 1988-2024 m (lanes untagged, one-way, Chimney Rock Park Road).
2 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 2020-2044 m left (16.5 m); 4352-4412 m left (17 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 1711 | +1.27 m | 2.22 m | +8.5 m | -314, -61 |
| near (12 m) | 30 - 100 m | 2977 | +1.46 m | 2.56 m | +12.7 m | -482, -13 |
| near (12 m) | 100 - 500 m | 16718 | +0.91 m | 3.07 m | +38.8 m | -626, 95 |
| near (12 m) | 500 - edge m | 65618 | -0.02 m | 3.03 m | +36.7 m | -1178, 107 |
| far (60 m) | 0 - 100 m | 191 | +1.02 m | 2.70 m | +9.3 m | 682, 71 |
| far (60 m) | 100 - 500 m | 667 | +1.00 m | 3.66 m | +24.7 m | -638, 71 |
| far (60 m) | 500 - 2300 m | 6165 | +0.24 m | 3.33 m | +21.4 m | -1178, -2269 |
| far (60 m) | 2300 - edge m | 95377 | +0.05 m | 2.42 m | -28.3 m | 1702, 2591 |

Largest wrong landforms (block means): near grid 240 m block at (-998, 191), 496 m from the road near station 4208 m: game +6.2 m; near grid 240 m block at (-758, 191), 260 m from the road near station 4208 m: game +5.9 m; near grid 240 m block at (-1238, 191), 735 m from the road near station 4208 m: game +5.8 m; far grid 600 m block at (4702, -109), 3695 m from the road near station 1616 m: game +3.4 m; far grid 600 m block at (-1898, 1691), 1441 m from the road near station 0 m: game -3.4 m; far grid 600 m block at (-1298, 491), 680 m from the road near station 8 m: game +3.3 m.

**Structures.** Stage bridges: none. Stage tunnels: none. OSM bridge ways on the route: none. OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### Blue Ridge Parkway, Linn Cove (`brp`, BlueRidge)

7.25 km; baked by `tools/brp/fetch_brp.mjs`. Plots: [profile](shots/lidar_audit/brp_profile.png), [plan and offsets](shots/lidar_audit/brp_plan.png).

**Line.** Bench found at 72% of stations. Offset of the bench centre from the game centreline: median 0.75 m, p95 3.0 m, worst single station 5.8 m (at 3652 m). Mean offset +0.13 m east, -0.17 m north. 11 stretches where the filtered offset is over 2 m, 1044 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 980 - 1124 | 148 m | 4.7 m | bench is to the left of the line |
| 3572 - 3820 | 252 m | 4.7 m | bench is to the left of the line |
| 2484 - 2644 | 164 m | 3.5 m | bench is to the right of the line |
| 6776 - 6880 | 108 m | 3.2 m | bench is to the left of the line |
| 1252 - 1328 | 80 m | 3.1 m | bench is to the right of the line |
| 488 - 532 | 48 m | 3.0 m | bench is to the right of the line |

**Height.** Real 1328.2 m at the first waypoint, 1273.4 m at the last (game 1331.1, 1275.1). Game minus real: bias -1.69 m, RMS 3.30 m, p95 6.84 m, worst -15.96 m at 4660 m. The bake's raw source sampled at the waypoints, before its smoothing: bias -0.29 m, RMS 1.50 m, worst -7.5 m. Climb 60 m real / 65 game; descent 114 / 121.
Stretches more than 3 m off after the bias is taken out: 4440-4876 m (-14.3 m); 0-52 m (+4.5 m).

**Grade** (over an 8 m gaussian). Real: mean 2.40%, p95 4.91%, max 6.13% at 32 m. Game: mean 2.57%, p95 4.98%, max 8.49% at 4484 m. Over 8.5%: 0 m of the real road, 0 m of the game's. The game's grade is more than 1% of grade off the real one on 18 stretches, 2240 m (31% of the route); RMS difference 1.75% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 4676 - 5000 | 328 m | -2.0% | +5.0% |
| 4332 - 4648 | 320 m | -2.0% | -8.5% |
| 2756 - 2884 | 132 m | -1.9% | +2.7% |
| 0 - 168 | 172 m | +5.9% | +2.1% |
| 7120 - 7244 | 128 m | -5.3% | -2.1% |
| 5336 - 5484 | 152 m | -1.9% | -4.7% |

**Width.** Game 6.4 m. Lidar bench (pavement + shoulders + any level verge): median 11 m, narrowest tenth 9.5 m, widest tenth 14.5 m. OSM `lanes`: 2: 5128 m, (no way matched): 2120 m; no `width` tag on any way of the route.
3 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 1004-1044 m left (19 m); 1080-1100 m left (19 m); 1512-1532 m right (18.5 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 3026 | -0.81 m | 2.18 m | -12.2 m | -1101, -467 |
| near (12 m) | 30 - 100 m | 7243 | -0.68 m | 1.94 m | -11.3 m | -1005, -539 |
| near (12 m) | 100 - 500 m | 43307 | -0.49 m | 1.99 m | -15.7 m | 2007, 541 |
| near (12 m) | 500 - edge m | 192237 | -0.15 m | 2.05 m | -31.9 m | -957, 889 |
| far (60 m) | 0 - 100 m | 409 | -0.73 m | 2.18 m | -7.8 m | -1101, -491 |
| far (60 m) | 100 - 500 m | 1743 | -0.42 m | 2.12 m | +8.8 m | -1881, -1331 |
| far (60 m) | 500 - 2300 m | 10439 | +0.08 m | 2.42 m | +16.7 m | -981, 1129 |
| far (60 m) | 2300 - edge m | 118353 | +0.09 m | 2.50 m | +21.6 m | -3081, -7871 |

Largest wrong landforms (block means): near grid 240 m block at (219, 1969), 1206 m from the road near station 3576 m: game +4.2 m; near grid 240 m block at (2619, 1969), 965 m from the road near station 0 m: game +3.9 m; near grid 240 m block at (2379, 1969), 965 m from the road near station 0 m: game +3.8 m; far grid 600 m block at (1599, 2389), 1361 m from the road near station 0 m: game +3.9 m; far grid 600 m block at (999, 3589), 2702 m from the road near station 0 m: game +3.4 m; far grid 600 m block at (-5001, -10211), 8814 m from the road near station 7240 m: game +3.4 m.

**Structures.** Stage bridges: 1612-1724 m (in OSM); 1864-2028 m (in OSM); 2688-2816 m (in OSM); 3472-3612 m (in OSM); 3704-3884 m (in OSM); 4488-4880 m (in OSM); 5464-5564 m (in OSM); 6576-6664 m (in OSM). Stage tunnels: none. OSM bridge ways on the route: 1632-1724 m (ground 14.09 m below the deck line; in the stage); 1888-1940 m (ground 9.78 m below the deck line; in the stage); 1944-2032 m (ground 11.89 m below the deck line; in the stage); 2704-2820 m (ground 17.33 m below the deck line; in the stage); 3488-3576 m (ground 15.36 m below the deck line; in the stage); 3580-3616 m (ground 8.08 m below the deck line; in the stage); 3736-3884 m (ground 11.11 m below the deck line; in the stage); 4500-4624 m (ground 20.78 m below the deck line; in the stage); 4628-4752 m (ground 23.11 m below the deck line; in the stage); 4756-4820 m (ground 16.26 m below the deck line; in the stage); 4824-4872 m (ground 7.99 m below the deck line; in the stage); 4876-4884 m (ground 5.13 m below the deck line; in the stage); 5480-5568 m (ground 19.88 m below the deck line; in the stage); 6636-6668 m (ground 9.24 m below the deck line; in the stage). OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

### Little Switzerland - Gillespie Gap loop (`swiss`, LittleSwitzerland)

9.68 km, loop; baked by `fetch_loop.mjs switzerland`. Plots: [profile](shots/lidar_audit/swiss_profile.png), [plan and offsets](shots/lidar_audit/swiss_plan.png).

**Line.** Bench found at 86% of stations. Offset of the bench centre from the game centreline: median 0.75 m, p95 3.0 m, worst single station 5.2 m (at 6676 m). Mean offset +0.16 m east, -0.37 m north. 17 stretches where the filtered offset is over 2 m, 948 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 1608 - 1660 | 56 m | 4.4 m | bench is to the left of the line |
| 1424 - 1464 | 44 m | 3.5 m | bench is to the left of the line |
| 8972 - 9088 | 120 m | 3.4 m | bench is to the right of the line |
| 6660 - 6760 | 104 m | 3.4 m | bench is to the right of the line |
| 7620 - 7724 | 108 m | 3.3 m | bench is to the right of the line |
| 7256 - 7292 | 40 m | 3.2 m | bench is to the right of the line |

**Height.** Real 859.2 m at the first waypoint, 859.2 m at the last (game 858.7, 858.4). Game minus real: bias +0.18 m, RMS 0.85 m, p95 1.70 m, worst +4.24 m at 4104 m. The bake's raw source sampled at the waypoints, before its smoothing: bias +0.09 m, RMS 1.44 m, worst +7.4 m. Climb 241 m real / 236 game; descent 241 / 236.
Stretches more than 3 m off after the bias is taken out: 4036-4176 m (+4.1 m).

**Grade** (over an 8 m gaussian). Real: mean 4.97%, p95 7.64%, max 8.75% at 6120 m. Game: mean 4.87%, p95 8.03%, max 9.54% at 3596 m. Over 8.5%: 24 m of the real road, 168 m of the game's. The game's grade is more than 1% of grade off the real one on 38 stretches, 2396 m (25% of the route); RMS difference 1.26% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 9580 - 9652 | 76 m | +8.6% | +1.4% |
| 9660 - 9680 | 24 m | +1.9% | +8.1% |
| 0 - 40 | 44 m | +1.9% | +8.0% |
| 4968 - 5048 | 84 m | +3.4% | -1.5% |
| 4732 - 4816 | 88 m | +6.5% | +2.0% |
| 4832 - 4944 | 116 m | -5.2% | -0.9% |

**Width.** Game 6.4 m. Lidar bench (pavement + shoulders + any level verge): median 8.5 m, narrowest tenth 6.5 m, widest tenth 11.5 m. OSM `lanes`: 1: 36 m, 2: 9296 m, (no way matched): 340 m, (untagged): 12 m; no `width` tag on any way of the route.
OSM says these stretches are not a plain two-way two-lane road: 9564-9592 m (lanes 1, one-way); 9596-9604 m (lanes 1, one-way).
2 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 1620-1648 m left (15.5 m); 4660-4684 m right (15 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 3949 | -0.49 m | 2.17 m | -8.3 m | 1621, 28 |
| near (12 m) | 30 - 100 m | 8129 | -0.31 m | 1.91 m | -6.7 m | 1429, 124 |
| near (12 m) | 100 - 500 m | 32729 | -0.47 m | 1.86 m | -11.1 m | 1393, -440 |
| near (12 m) | 500 - edge m | 104953 | -0.29 m | 1.77 m | +10.4 m | -1763, -1052 |
| far (60 m) | 0 - 100 m | 473 | -0.16 m | 2.26 m | -6.4 m | 1873, 196 |
| far (60 m) | 100 - 500 m | 1326 | -0.45 m | 2.18 m | -8.7 m | 1573, -104 |
| far (60 m) | 500 - 2300 m | 8654 | -0.16 m | 2.24 m | -10.9 m | 2353, -1304 |
| far (60 m) | 2300 - edge m | 105982 | +0.02 m | 2.52 m | +53.0 m | 3073, -7784 |

Largest wrong landforms (block means): near grid 240 m block at (1573, 16), 2 m from the road near station 9152 m: game -2.7 m; near grid 240 m block at (-2267, -1184), 963 m from the road near station 5040 m: game -2.6 m; near grid 240 m block at (-1307, -944), 593 m from the road near station 5776 m: game -2.6 m; far grid 600 m block at (3193, -7844), 7635 m from the road near station 8576 m: game +5.1 m; far grid 600 m block at (-2207, 5956), 5397 m from the road near station 4440 m: game +3.3 m; far grid 600 m block at (-7007, -8444), 9422 m from the road near station 4976 m: game +3.0 m.

**Structures.** Stage bridges: 9676-9792 m (in OSM). Stage tunnels: 3992-4180 m (in OSM). OSM bridge ways on the route: 24-68 m (ground 6.79 m below the deck line; in the stage). OSM tunnels: 3996-4184 m (Little Switzerland Tunnel; in the stage). Places the lidar ground drops away under the road with no bridge mapped: 2744-2780 m (5.85 m).

### Blowing Rock - Moses Cone loop (`brock`, BlowingRock)

9.88 km, loop; baked by `fetch_loop.mjs blowingrock`. Plots: [profile](shots/lidar_audit/brock_profile.png), [plan and offsets](shots/lidar_audit/brock_plan.png).

**Line.** Bench found at 69% of stations. Offset of the bench centre from the game centreline: median 1.25 m, p95 3.2 m, worst single station 7.8 m (at 5320 m). Mean offset +0.29 m east, -0.51 m north. 22 stretches where the filtered offset is over 2 m, 2164 m of road in all. The largest:

| From - to (m) | Length | Peak offset | Side |
|---|---|---|---|
| 5236 - 5356 | 124 m | 6.2 m | bench is to the right of the line |
| 5076 - 5120 | 48 m | 5.7 m | bench is to the right of the line |
| 4480 - 4680 | 204 m | 4.8 m | bench is to the left of the line |
| 8472 - 8960 | 492 m | 3.9 m | bench is to the right of the line |
| 2416 - 2544 | 132 m | 3.5 m | bench is to the left of the line |
| 9520 - 9604 | 88 m | 3.5 m | bench is to the left of the line |

**Height.** Real 1058.8 m at the first waypoint, 1058.6 m at the last (game 1058.7, 1058.4). Game minus real: bias -0.39 m, RMS 0.96 m, p95 2.14 m, worst +3.18 m at 8076 m. The bake's raw source sampled at the waypoints, before its smoothing: bias -0.32 m, RMS 1.45 m, worst -6.3 m. Climb 184 m real / 175 game; descent 184 / 175.
Stretches more than 3 m off after the bias is taken out: 8036-8116 m (+3.6 m).

**Grade** (over an 8 m gaussian). Real: mean 3.72%, p95 6.87%, max 7.38% at 152 m. Game: mean 3.54%, p95 7.01%, max 8.14% at 1916 m. Over 8.5%: 0 m of the real road, 0 m of the game's. The game's grade is more than 1% of grade off the real one on 32 stretches, 2428 m (25% of the route); RMS difference 1.19% of grade. The largest:

| From - to (m) | Length | Real grade | Game grade |
|---|---|---|---|
| 7932 - 8056 | 128 m | -7.0% | -0.5% |
| 9528 - 9620 | 96 m | +6.1% | +0.4% |
| 7824 - 7920 | 100 m | +5.8% | +0.4% |
| 5160 - 5256 | 100 m | +0.9% | -2.6% |
| 8092 - 8216 | 128 m | +0.2% | -3.1% |
| 4104 - 4212 | 112 m | +3.2% | -0.0% |

**Width.** Game 6.4 m. Lidar bench (pavement + shoulders + any level verge): median 11 m, narrowest tenth 8 m, widest tenth 16 m. OSM `lanes`: 2: 8584 m, 3: 76 m, 4: 468 m, (untagged): 520 m, (no way matched): 228 m; no `width` tag on any way of the route.
OSM says these stretches are not a plain two-way two-lane road: 4216-4244 m (lanes untagged, one-way, Cone Road); 7944-8092 m (lanes 2, one-way, Valley Boulevard); 8096-8268 m (lanes 2, one-way, Valley Boulevard); 8272-8396 m (lanes 3, one-way, Valley Boulevard); 8400-8568 m (lanes 4, Valley Boulevard); 8572-8608 m (lanes 4, James E Holshouser Jr Highway); 8612-8692 m (lanes 4, James E Holshouser Jr Highway); 8696-8876 m (lanes 4, James E Holshouser Jr Highway); 8880-8916 m (lanes 4, James E Holshouser Jr Highway); 8920-9004 m (lanes 2, one-way, James E Holshouser Jr Highway); 9008-9408 m (lanes 2, one-way, James E Holshouser Jr Highway); 9412-9432 m (lanes untagged, one-way).
6 wide spots (bench 5 m over the median for 24 m or more: pull-offs, overlooks, junction mouths): 5588-5628 m right (19 m); 7832-7904 m right (19 m); 8508-8544 m right (18.5 m); 8556-8620 m right (19 m); 8632-8664 m right (19 m); 9120-9228 m right (19 m).

**Terrain** (game grid minus 3DEP 1/3", on the grid's own lattice).

| Grid | Distance from the road | Points | Bias | RMS | Worst | Worst at (x east, z north) |
|---|---|---|---|---|---|---|
| near (12 m) | 0 - 30 m | 4077 | -0.23 m | 1.64 m | +9.5 m | 999, -107 |
| near (12 m) | 30 - 100 m | 9263 | -0.27 m | 1.30 m | +6.8 m | -1581, -467 |
| near (12 m) | 100 - 500 m | 50668 | -0.31 m | 1.48 m | -9.0 m | -285, 1561 |
| near (12 m) | 500 - edge m | 139932 | -0.11 m | 1.95 m | +14.5 m | -3069, 73 |
| far (60 m) | 0 - 100 m | 532 | -0.39 m | 1.54 m | +7.5 m | -1581, -455 |
| far (60 m) | 100 - 500 m | 2031 | -0.25 m | 1.75 m | -7.4 m | -201, 1585 |
| far (60 m) | 500 - 2300 m | 9351 | -0.08 m | 2.31 m | -12.9 m | 2259, 685 |
| far (60 m) | 2300 - edge m | 111926 | -0.02 m | 2.78 m | +58.7 m | -3141, 6565 |

Largest wrong landforms (block means): near grid 240 m block at (-1161, -1595), 711 m from the road near station 6200 m: game -3.0 m; near grid 240 m block at (-2121, -2315), 1706 m from the road near station 5232 m: game -2.9 m; near grid 240 m block at (-921, -1595), 582 m from the road near station 6232 m: game -2.9 m; far grid 600 m block at (-3501, 6265), 5657 m from the road near station 3408 m: game +3.7 m; far grid 600 m block at (-10101, -3335), 8484 m from the road near station 4248 m: game +3.4 m; far grid 600 m block at (-2901, 6265), 5395 m from the road near station 2024 m: game +3.1 m.

**Structures.** Stage bridges: 9688-9808 m (in OSM). Stage tunnels: none. OSM bridge ways on the route: 1124-1148 m (ground 4.82 m below the deck line; NOT in the stage); 9432-9456 m (ground 5.42 m below the deck line; NOT in the stage); 9716-9764 m (ground 8.42 m below the deck line; in the stage); 9768-9796 m (ground 8.29 m below the deck line; in the stage). OSM tunnels: none. Places the lidar ground drops away under the road with no bridge mapped: none.

<!-- STAGES:END -->
