# Mountain stages against USGS lidar (3DEP)

2026-11-09, branch `mountain-lidar` (off `mountain-nm` 70364204). Owner: "the mountain tracks probably aren't accurate or to scale; correct while checking lidar."

## The answer

**To scale: yes, they already were.** Plan length was checked earlier (within 0.09% on seven stages). This audit adds NC 226A (`swa`; its source is `overpass_swiss226a.json`): where the mapped line cut the inside of bends the real road is 20 m longer in 11.9 km (0.17%).

**Accurate: mostly, with four real faults.** The heights the stages were baked from are labelled "SRTM", but the tiles in `tools/roads/cache` (the AWS "skadi" set) are bare-earth USGS data resampled to 30 m in the United States, not radar treetops: along all eight roads the raw source is within 0.3 m of the lidar road on average (RMS 1.0 to 1.6 m). The note in the builder that says it "reads the canopy top, up to +33 m on these ridges" is wrong. So the land was never tens of metres off. What is off:

1. **Bridge decks sag into the ground under them (Blue Ridge Parkway, `brp`).** The bake smoothed the bare ground and laid the road on it, bridges included. The Linn Cove Viaduct (4488-4880 m) dips 16 m in the middle: the game goes down it at 8.5% and climbs out at 5%, where the real deck is a steady 2% descent. The other seven spans sit 1.7 to 3.9 m low.
2. **A hole in Beech Gap's far land (`beech`).** The far grid's western 2 km (33 columns, the whole 24 km height, everything west of longitude 83 W) is at sea level: a wall 850 to 1,830 m high, 8 km west of the road. The bake only loaded the tiles its road's box touched.
3. **The grade clamp flattens NC 226 to Gillespie Gap (`gap`).** The real road averages 10.2% for 700 m (3300-4000 m) with pitches of 12 to 13%, and is over 8.5% for 3.2 km; the bake clamped at 10.0%, so the game road falls up to 4.9 m below the real one and makes it up later.
4. **The Blowing Rock loop (`brock`) runs 1.5 km (7944-9432 m) on the US 221 / US 321 bypass**, which OSM tags as a divided highway (two lanes each way, four lanes undivided for 516 m, 50-55 mph). The game builds it as a 6.4 m two-lane road. This is a width and road-type error, not an elevation one.

Everything else is small: road heights within 1 m RMS of the lidar on six stages, grades softened (short steep pitches lose 1 to 3% of grade over about a quarter of each route), the mapped centreline a median 0.75 to 1.25 m from the middle of the lidar bench, which is about what the method can resolve.

**Corrected in this branch: NC 226A (`swa`)**, re-baked from lidar (line, heights, both grids). See "The correction" below. Blue Ridge is the next worst and is not corrected yet.

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

<!-- PHASE2 -->
